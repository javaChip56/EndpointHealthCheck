using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Formatting;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.Statistics;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace ApiHealthDashboard.Pages.Endpoints;

public class DetailsModel : PageModel
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly ILogger<DetailsModel> _logger;
    private readonly IEndpointScheduler _scheduler;
    private readonly IEndpointStateStore _stateStore;

    public DetailsModel(
        DashboardConfig dashboardConfig,
        IEndpointStateStore stateStore,
        IEndpointScheduler scheduler,
        ILogger<DetailsModel> logger)
    {
        _dashboardConfig = dashboardConfig;
        _stateStore = stateStore;
        _scheduler = scheduler;
        _logger = logger;
    }

    public string EndpointId { get; private set; } = string.Empty;

    public EndpointDetailsViewModel? Endpoint { get; private set; }

    public IActionResult OnGet(string? id)
    {
        var endpointId = ResolveEndpointId(id);
        if (endpointId is null)
        {
            _logger.LogWarning("Endpoint details were requested, but no endpoint id could be resolved.");
            return NotFound();
        }

        return TryLoadEndpoint(endpointId)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostRefreshAsync(string? id, CancellationToken cancellationToken)
    {
        var endpointId = ResolveEndpointId(id);
        if (endpointId is null)
        {
            _logger.LogWarning("Manual refresh was requested from the details page, but no endpoint id could be resolved.");
            return NotFound();
        }

        _logger.LogInformation(
            "Manual refresh requested from the details page for endpoint {EndpointId}.",
            endpointId);

        var refreshed = await _scheduler.RefreshEndpointAsync(endpointId, cancellationToken);

        _logger.LogInformation(
            "Manual refresh requested from the details page for endpoint {EndpointId} completed with outcome {RefreshStarted}.",
            endpointId,
            refreshed);

        TempData["StatusMessage"] = refreshed
            ? $"Triggered refresh for endpoint '{endpointId}'."
            : $"Refresh for endpoint '{endpointId}' was skipped because it is already polling or not configured.";
        TempData["StatusType"] = refreshed ? "success" : "warning";

        return RedirectToPage(new { id = endpointId });
    }

    private string? ResolveEndpointId(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return _dashboardConfig.Endpoints.FirstOrDefault()?.Id;
    }

    private bool TryLoadEndpoint(string endpointId)
    {
        var endpointConfig = _dashboardConfig.Endpoints.FirstOrDefault(
            endpoint => string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));

        if (endpointConfig is null)
        {
            _logger.LogWarning(
                "Endpoint details were requested for unknown endpoint {EndpointId}.",
                endpointId);
            return false;
        }

        EndpointId = endpointConfig.Id;

        var state = _stateStore.Get(endpointConfig.Id);
        Endpoint = EndpointDetailsViewModel.From(endpointConfig, state, _dashboardConfig.Dashboard.ShowRawPayload);

        _logger.LogDebug("Loaded endpoint details for {EndpointId}.", endpointConfig.Id);
        return true;
    }

    public sealed class EndpointDetailsViewModel
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Url { get; init; }

        public required bool Enabled { get; init; }

        public required string EnabledText { get; init; }

        public required string Priority { get; init; }

        public required string PriorityBadgeClass { get; init; }

        public required string FrequencyText { get; init; }

        public required string TimeoutText { get; init; }

        public required string Status { get; init; }

        public required string StatusBadgeClass { get; init; }

        public required bool IsPolling { get; init; }

        public string StatusSummary { get; init; } = string.Empty;

        public string LastCheckedText { get; init; } = "Never";

        public string LastSuccessfulText { get; init; } = "Never";

        public string LastRetrievedText { get; init; } = "Never";

        public string DurationText { get; init; } = "-";

        public int RecentSampleCount { get; init; }

        public string RecentSuccessRateText { get; init; } = "No recent samples";

        public string RecentFailureCountText { get; init; } = "0 failures";

        public string RecentAverageDurationText { get; init; } = "-";

        public string LastStatusChangeText { get; init; } = "No recent change";

        public string RecentTrendText { get; init; } = "Awaiting trend";

        public string RecentTrendBadgeClass { get; init; } = "badge-light";

        public string RecentTrendSummary { get; init; } = "Need more samples to detect a trend.";

        public string? ErrorText { get; init; }

        public string ErrorSummary => string.IsNullOrWhiteSpace(ErrorText) ? "None" : ErrorText;

        public IReadOnlyList<HeaderSummaryViewModel> Headers { get; init; } = [];

        public IReadOnlyList<string> IncludeChecks { get; init; } = [];

        public IReadOnlyList<string> ExcludeChecks { get; init; } = [];

        public IReadOnlyList<HealthNode> Nodes { get; init; } = [];

        public IReadOnlyList<MetadataSummaryViewModel> SnapshotMetadata { get; init; } = [];

        public int TopLevelCheckCount { get; init; }

        public int TotalCheckCount { get; init; }

        public int NestedCheckCount { get; init; }

        public int HealthyCheckCount { get; init; }

        public int DegradedCheckCount { get; init; }

        public int UnhealthyCheckCount { get; init; }

        public int UnknownCheckCount { get; init; }

        public string? RawPayload { get; init; }

        public bool ShowRawPayload { get; init; }

        public bool HasParsedChecks => Nodes.Count > 0;

        public bool HasFilters => IncludeChecks.Count > 0 || ExcludeChecks.Count > 0;

        public bool HasSnapshotMetadata => SnapshotMetadata.Count > 0;

        public bool HasRecentSamples => RecentSamples.Count > 0;

        public IReadOnlyList<RecentPollSampleViewModel> RecentSamples { get; init; } = [];

        public IReadOnlyList<StatusTransitionViewModel> RecentStatusTransitions { get; init; } = [];

        public bool HasStatusTransitions => RecentStatusTransitions.Count > 0;

        public static EndpointDetailsViewModel From(
            EndpointConfig endpoint,
            EndpointState? state,
            bool showRawPayload)
        {
            var status = state?.Status ?? "Unknown";
            var timeoutSeconds = endpoint.TimeoutSeconds?.ToString() ?? "Default";
            var nodes = state?.Snapshot?.Nodes.Select(static node => node.Clone()).ToArray() ?? [];
            var flattenedNodes = FlattenNodes(nodes).ToArray();
            var snapshotDurationMs = state?.DurationMs ?? state?.Snapshot?.DurationMs;
            var recentSamples = state?.RecentSamples
                .Select(static sample => sample.Clone())
                .ToArray() ?? [];
            var recentMetrics = RecentPollSampleMetricsCalculator.Calculate(recentSamples);
            var trendAnalysis = RecentPollTrendAnalyzer.Analyze(recentSamples);

            return new EndpointDetailsViewModel
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                Url = endpoint.Url,
                Enabled = endpoint.Enabled,
                EnabledText = endpoint.Enabled ? "Enabled" : "Disabled",
                Priority = EndpointPriority.Normalize(endpoint.Priority),
                PriorityBadgeClass = ToPriorityBadgeClass(endpoint.Priority),
                FrequencyText = $"{endpoint.FrequencySeconds} seconds",
                TimeoutText = endpoint.TimeoutSeconds is null ? "Default timeout" : $"{timeoutSeconds} seconds",
                Status = status,
                StatusBadgeClass = ToBadgeClass(status),
                IsPolling = state?.IsPolling ?? false,
                StatusSummary = BuildStatusSummary(endpoint.Enabled, state?.IsPolling ?? false, status, state?.LastError),
                LastCheckedText = FormatDateTime(state?.LastCheckedUtc),
                LastSuccessfulText = FormatDateTime(state?.LastSuccessfulUtc),
                LastRetrievedText = FormatDateTime(state?.Snapshot?.RetrievedUtc),
                DurationText = snapshotDurationMs is long durationMs ? $"{durationMs} ms" : "-",
                RecentSampleCount = recentMetrics.SampleCount,
                RecentSuccessRateText = recentMetrics.HasSamples
                    ? $"{recentMetrics.SuccessRatePercent}% success"
                    : "No recent samples",
                RecentFailureCountText = recentMetrics.HasSamples
                    ? $"{recentMetrics.FailureCount} failure{(recentMetrics.FailureCount == 1 ? string.Empty : "s")}"
                    : "0 failures",
                RecentAverageDurationText = recentMetrics.HasSamples
                    ? $"{recentMetrics.AverageDurationMs} ms"
                    : "-",
                LastStatusChangeText = recentMetrics.LastStatusChangeUtc is DateTimeOffset lastStatusChangeUtc
                    ? FormatDateTime(lastStatusChangeUtc)
                    : "No recent change",
                RecentTrendText = ToTrendText(trendAnalysis.TrendKind, status),
                RecentTrendBadgeClass = ToTrendBadgeClass(trendAnalysis.TrendKind),
                RecentTrendSummary = BuildTrendSummary(trendAnalysis, recentMetrics),
                ErrorText = state?.LastError,
                Headers = endpoint.Headers
                    .OrderBy(static header => header.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static header => new HeaderSummaryViewModel
                    {
                        Name = header.Key,
                        ValuePreview = string.IsNullOrEmpty(header.Value) ? "(empty)" : "********"
                    })
                    .ToArray(),
                IncludeChecks = endpoint.IncludeChecks
                    .OrderBy(static check => check, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ExcludeChecks = endpoint.ExcludeChecks
                    .OrderBy(static check => check, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Nodes = nodes,
                SnapshotMetadata = state?.Snapshot?.Metadata
                    .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static item => new MetadataSummaryViewModel
                    {
                        Name = item.Key,
                        Value = FormatMetadataValue(item.Value)
                    })
                    .ToArray() ?? [],
                TopLevelCheckCount = nodes.Length,
                TotalCheckCount = flattenedNodes.Length,
                NestedCheckCount = Math.Max(flattenedNodes.Length - nodes.Length, 0),
                HealthyCheckCount = flattenedNodes.Count(static node => node.Status == "Healthy"),
                DegradedCheckCount = flattenedNodes.Count(static node => node.Status == "Degraded"),
                UnhealthyCheckCount = flattenedNodes.Count(static node => node.Status == "Unhealthy"),
                UnknownCheckCount = flattenedNodes.Count(static node => node.Status is not ("Healthy" or "Degraded" or "Unhealthy")),
                RawPayload = showRawPayload ? FormatPayloadPreview(state?.Snapshot?.RawPayload) : null,
                ShowRawPayload = showRawPayload,
                RecentStatusTransitions = trendAnalysis.Transitions
                    .OrderByDescending(static transition => transition.ChangedUtc)
                    .Take(6)
                    .Select(CreateStatusTransitionViewModel)
                    .ToArray(),
                RecentSamples = recentSamples
                    .OrderByDescending(static sample => sample.CheckedUtc)
                    .Take(10)
                    .Select(CreateRecentSampleViewModel)
                    .ToArray()
            };
        }

        private static IEnumerable<HealthNode> FlattenNodes(IEnumerable<HealthNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;

                foreach (var child in FlattenNodes(node.Children))
                {
                    yield return child;
                }
            }
        }

        private static string FormatDateTime(DateTimeOffset? value)
        {
            return value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Never";
        }

        private static string BuildStatusSummary(bool enabled, bool isPolling, string status, string? errorText)
        {
            if (!enabled)
            {
                return "Polling is disabled for this endpoint in YAML configuration.";
            }

            if (isPolling)
            {
                return "A refresh is currently in progress for this endpoint.";
            }

            if (!string.IsNullOrWhiteSpace(errorText))
            {
                return "The latest poll completed with an error that needs attention.";
            }

            return status switch
            {
                "Healthy" => "The latest poll completed successfully and all reported checks are healthy.",
                "Degraded" => "The latest poll completed, but one or more checks reported a degraded state.",
                "Unhealthy" => "The latest poll completed and at least one reported check is unhealthy.",
                _ => "No successful health snapshot has been captured yet."
            };
        }

        private static string FormatMetadataValue(object? value)
        {
            return DisplayValueFormatter.Format(value);
        }

        private static string? FormatPayloadPreview(string? rawPayload)
        {
            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                return rawPayload;
            }

            try
            {
                using var document = JsonDocument.Parse(rawPayload);
                return JsonSerializer.Serialize(
                    document.RootElement,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
            }
            catch (JsonException)
            {
                return rawPayload;
            }
        }

        private static string ToBadgeClass(string status)
        {
            return status switch
            {
                "Healthy" => "badge-success",
                "Degraded" => "badge-warning",
                "Unhealthy" => "badge-danger",
                _ => "badge-secondary"
            };
        }

        private static string ToPriorityBadgeClass(string priority)
        {
            return EndpointPriority.Normalize(priority) switch
            {
                EndpointPriority.Critical => "badge-danger",
                EndpointPriority.High => "badge-warning",
                EndpointPriority.Low => "badge-secondary",
                _ => "badge-info"
            };
        }

        private static RecentPollSampleViewModel CreateRecentSampleViewModel(RecentPollSample sample)
        {
            return new RecentPollSampleViewModel
            {
                CheckedText = FormatDateTime(sample.CheckedUtc),
                Status = sample.Status,
                StatusBadgeClass = ToBadgeClass(sample.Status),
                IndicatorBadgeClass = ToRecentIndicatorClass(sample),
                ResultKind = sample.ResultKind,
                ResultKindBadgeClass = ToResultKindBadgeClass(sample),
                DurationText = $"{sample.DurationMs} ms",
                ErrorSummary = string.IsNullOrWhiteSpace(sample.ErrorSummary) ? "None" : sample.ErrorSummary,
                HasError = !string.IsNullOrWhiteSpace(sample.ErrorSummary)
            };
        }

        private static string ToResultKindBadgeClass(RecentPollSample sample)
        {
            if (!string.IsNullOrWhiteSpace(sample.ErrorSummary))
            {
                return "badge-danger";
            }

            return sample.ResultKind switch
            {
                "Success" => "badge-success",
                "Timeout" => "badge-warning",
                "NetworkError" => "badge-danger",
                "HttpError" => "badge-danger",
                "EmptyResponse" => "badge-warning",
                _ => "badge-secondary"
            };
        }

        private static string ToRecentIndicatorClass(RecentPollSample sample)
        {
            if (!string.IsNullOrWhiteSpace(sample.ErrorSummary) ||
                !string.Equals(sample.ResultKind, "Success", StringComparison.OrdinalIgnoreCase))
            {
                return "sample-indicator-failure";
            }

            return sample.Status switch
            {
                "Healthy" => "sample-indicator-healthy",
                "Degraded" => "sample-indicator-degraded",
                "Unhealthy" => "sample-indicator-unhealthy",
                _ => "sample-indicator-unknown"
            };
        }

        private static StatusTransitionViewModel CreateStatusTransitionViewModel(RecentPollStatusTransition transition)
        {
            return new StatusTransitionViewModel
            {
                FromStatus = transition.FromStatus,
                FromStatusBadgeClass = ToBadgeClass(transition.FromStatus),
                ToStatus = transition.ToStatus,
                ToStatusBadgeClass = ToBadgeClass(transition.ToStatus),
                ChangedText = FormatDateTime(transition.ChangedUtc)
            };
        }

        private static string ToTrendText(RecentPollTrendKind trendKind, string currentStatus)
        {
            return trendKind switch
            {
                RecentPollTrendKind.Failing => "Failing",
                RecentPollTrendKind.Improving => "Improving",
                RecentPollTrendKind.Worsening => "Worsening",
                RecentPollTrendKind.Flapping => "Flapping",
                RecentPollTrendKind.Stable => $"Stable {currentStatus}",
                _ => "Awaiting trend"
            };
        }

        private static string ToTrendBadgeClass(RecentPollTrendKind trendKind)
        {
            return trendKind switch
            {
                RecentPollTrendKind.Failing => "badge-danger",
                RecentPollTrendKind.Improving => "badge-success",
                RecentPollTrendKind.Worsening => "badge-warning",
                RecentPollTrendKind.Flapping => "badge-danger",
                RecentPollTrendKind.Stable => "badge-info",
                _ => "badge-light"
            };
        }

        private static string BuildTrendSummary(RecentPollTrendAnalysis trendAnalysis, RecentPollSampleMetrics recentMetrics)
        {
            if (!recentMetrics.HasSamples)
            {
                return "No recent samples retained yet.";
            }

            return trendAnalysis.TrendKind switch
            {
                RecentPollTrendKind.Failing =>
                    "Recent checks are consistently failing and need attention.",
                RecentPollTrendKind.Improving =>
                    $"Recent checks are recovering across {trendAnalysis.Transitions.Count} status change{(trendAnalysis.Transitions.Count == 1 ? string.Empty : "s")}.",
                RecentPollTrendKind.Worsening =>
                    $"Recent checks are trending worse across {trendAnalysis.Transitions.Count} status change{(trendAnalysis.Transitions.Count == 1 ? string.Empty : "s")}.",
                RecentPollTrendKind.Flapping =>
                    $"Recent checks have changed status {trendAnalysis.Transitions.Count} times and may be unstable.",
                RecentPollTrendKind.Stable =>
                    "Recent checks are holding a consistent status.",
                _ => "Need at least two retained samples to detect a trend."
            };
        }
    }

    public sealed class HeaderSummaryViewModel
    {
        public required string Name { get; init; }

        public required string ValuePreview { get; init; }
    }

    public sealed class MetadataSummaryViewModel
    {
        public required string Name { get; init; }

        public required string Value { get; init; }
    }

    public sealed class RecentPollSampleViewModel
    {
        public required string CheckedText { get; init; }

        public required string Status { get; init; }

        public required string StatusBadgeClass { get; init; }

        public required string IndicatorBadgeClass { get; init; }

        public required string ResultKind { get; init; }

        public required string ResultKindBadgeClass { get; init; }

        public required string DurationText { get; init; }

        public required string ErrorSummary { get; init; }

        public bool HasError { get; init; }
    }

    public sealed class StatusTransitionViewModel
    {
        public required string FromStatus { get; init; }

        public required string FromStatusBadgeClass { get; init; }

        public required string ToStatus { get; init; }

        public required string ToStatusBadgeClass { get; init; }

        public required string ChangedText { get; init; }
    }
}
