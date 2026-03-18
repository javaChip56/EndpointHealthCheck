using System.Text.Json;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Services;

namespace ApiHealthDashboard.Cli;

public sealed class CliExecutionService
{
    private readonly IEndpointPoller _endpointPoller;
    private readonly IHealthResponseParser _healthResponseParser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CliExecutionService> _logger;

    public CliExecutionService(
        IEndpointPoller endpointPoller,
        IHealthResponseParser healthResponseParser,
        TimeProvider timeProvider,
        ILogger<CliExecutionService> logger)
    {
        _endpointPoller = endpointPoller;
        _healthResponseParser = healthResponseParser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CliExecutionReport> ExecuteAsync(
        DashboardConfig config,
        CliOptions options,
        string dashboardConfigPath,
        IReadOnlyCollection<string> configurationWarnings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configurationWarnings);

        _logger.LogInformation(
            "Starting CLI execution in {Mode} mode for {EndpointCount} configured endpoints.",
            options.RunAll ? "suite" : "selected-endpoints",
            config.Endpoints.Count);

        var tasks = config.Endpoints
            .Select(endpoint => ExecuteEndpointAsync(config, endpoint, cancellationToken))
            .ToArray();

        var endpoints = tasks.Length == 0
            ? new List<CliEndpointExecutionReport>()
            : [.. await Task.WhenAll(tasks)];

        var summary = BuildSummary(config, endpoints);
        var report = new CliExecutionReport
        {
            Mode = options.RunAll ? "suite" : "selected-endpoints",
            DashboardConfigPath = dashboardConfigPath,
            ExecutedUtc = _timeProvider.GetUtcNow().ToString("O"),
            SelectedEndpointFiles = [.. options.EndpointFiles],
            ConfigurationWarnings = [.. configurationWarnings],
            Summary = summary,
            Endpoints = endpoints
        };

        _logger.LogInformation(
            "Completed CLI execution with overall status {OverallStatus}. Executed {ExecutedEndpoints} endpoints and skipped {SkippedEndpoints}.",
            summary.OverallStatus,
            summary.ExecutedEndpoints,
            summary.SkippedEndpoints);

        return report;
    }

    private async Task<CliEndpointExecutionReport> ExecuteEndpointAsync(
        DashboardConfig config,
        EndpointConfig endpoint,
        CancellationToken cancellationToken)
    {
        if (!endpoint.Enabled)
        {
            return new CliEndpointExecutionReport
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                Url = endpoint.Url,
                Enabled = false,
                FrequencySeconds = endpoint.FrequencySeconds,
                TimeoutSeconds = endpoint.TimeoutSeconds ?? config.Dashboard.RequestTimeoutSecondsDefault,
                ExecutionState = "Skipped",
                Status = "Unknown",
                PollResultKind = "Skipped",
                ErrorMessage = "Endpoint is disabled."
            };
        }

        var pollResult = await _endpointPoller.PollAsync(endpoint, cancellationToken);
        var report = new CliEndpointExecutionReport
        {
            Id = endpoint.Id,
            Name = endpoint.Name,
            Url = endpoint.Url,
            Enabled = true,
            FrequencySeconds = endpoint.FrequencySeconds,
            TimeoutSeconds = endpoint.TimeoutSeconds ?? config.Dashboard.RequestTimeoutSecondsDefault,
            ExecutionState = "Executed",
            Status = "Unknown",
            PollResultKind = pollResult.Kind.ToString(),
            CheckedUtc = pollResult.CheckedUtc.ToString("O"),
            DurationMs = pollResult.DurationMs,
            StatusCode = pollResult.StatusCode is null ? null : (int)pollResult.StatusCode.Value,
            ErrorMessage = pollResult.ErrorMessage,
            ResponseBody = pollResult.ResponseBody
        };

        if (pollResult.IsSuccess && !string.IsNullOrWhiteSpace(pollResult.ResponseBody))
        {
            var snapshot = _healthResponseParser.Parse(endpoint, pollResult.ResponseBody, pollResult.DurationMs);
            report.Status = snapshot.OverallStatus;
            report.Snapshot = CreateSnapshotReport(snapshot);

            if (TryGetParserError(snapshot, out var parserError))
            {
                report.ErrorMessage = $"Failed to parse health response: {parserError}";
            }
        }

        return report;
    }

    private static CliExecutionSummary BuildSummary(
        DashboardConfig config,
        IReadOnlyCollection<CliEndpointExecutionReport> endpoints)
    {
        var summary = new CliExecutionSummary
        {
            TotalEndpoints = config.Endpoints.Count,
            EnabledEndpoints = config.Endpoints.Count(static endpoint => endpoint.Enabled),
            ExecutedEndpoints = endpoints.Count(static endpoint => string.Equals(endpoint.ExecutionState, "Executed", StringComparison.OrdinalIgnoreCase)),
            SkippedEndpoints = endpoints.Count(static endpoint => string.Equals(endpoint.ExecutionState, "Skipped", StringComparison.OrdinalIgnoreCase)),
            SuccessfulPolls = endpoints.Count(static endpoint => string.Equals(endpoint.PollResultKind, PollResultKind.Success.ToString(), StringComparison.OrdinalIgnoreCase)),
            FailedPolls = endpoints.Count(static endpoint =>
                string.Equals(endpoint.ExecutionState, "Executed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(endpoint.PollResultKind, PollResultKind.Success.ToString(), StringComparison.OrdinalIgnoreCase))
        };

        foreach (var endpoint in endpoints)
        {
            switch (NormalizeStatus(endpoint.Status))
            {
                case "Healthy":
                    summary.HealthyEndpoints++;
                    break;
                case "Degraded":
                    summary.DegradedEndpoints++;
                    break;
                case "Unhealthy":
                    summary.UnhealthyEndpoints++;
                    break;
                default:
                    summary.UnknownEndpoints++;
                    break;
            }
        }

        summary.OverallStatus = AggregateStatus(endpoints.Select(static endpoint => endpoint.Status));
        return summary;
    }

    private static CliSnapshotReport CreateSnapshotReport(HealthSnapshot snapshot)
    {
        return new CliSnapshotReport
        {
            OverallStatus = snapshot.OverallStatus,
            RetrievedUtc = snapshot.RetrievedUtc.ToString("O"),
            DurationMs = snapshot.DurationMs,
            RawPayload = snapshot.RawPayload,
            MetadataEntries = snapshot.Metadata
                .Select(CreateEntry)
                .ToList(),
            Nodes = snapshot.Nodes
                .Select(CreateNodeReport)
                .ToList()
        };
    }

    private static CliNodeReport CreateNodeReport(HealthNode node)
    {
        return new CliNodeReport
        {
            Name = node.Name,
            Status = node.Status,
            Description = node.Description,
            ErrorMessage = node.ErrorMessage,
            DurationText = node.DurationText,
            DataEntries = node.Data
                .Select(CreateEntry)
                .ToList(),
            Children = node.Children
                .Select(CreateNodeReport)
                .ToList()
        };
    }

    private static CliKeyValueEntry CreateEntry(KeyValuePair<string, object?> pair)
    {
        return new CliKeyValueEntry
        {
            Key = pair.Key,
            Value = ConvertValue(pair.Value)
        };
    }

    private static string? ConvertValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            _ => JsonSerializer.Serialize(value, CliReportSerializer.JsonOptions)
        };
    }

    private static bool TryGetParserError(HealthSnapshot snapshot, out string parserError)
    {
        if (snapshot.Metadata.TryGetValue("parserError", out var value) &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            parserError = text;
            return true;
        }

        parserError = string.Empty;
        return false;
    }

    private static string AggregateStatus(IEnumerable<string> statuses)
    {
        var highestSeverity = 0;
        var resolvedStatus = "Unknown";

        foreach (var status in statuses)
        {
            var normalized = NormalizeStatus(status);
            var severity = normalized switch
            {
                "Unhealthy" => 4,
                "Degraded" => 3,
                "Healthy" => 2,
                _ => 1
            };

            if (severity > highestSeverity)
            {
                highestSeverity = severity;
                resolvedStatus = normalized;
            }
        }

        return resolvedStatus;
    }

    private static string NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "healthy" => "Healthy",
            "degraded" => "Degraded",
            "unhealthy" => "Unhealthy",
            "unknown" => "Unknown",
            _ => value.Trim()
        };
    }
}
