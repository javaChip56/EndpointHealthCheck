using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.State;

namespace ApiHealthDashboard.Scheduling;

public sealed class PollingSchedulerService : BackgroundService, IEndpointScheduler
{
    private const string ManualTriggerSource = "manual";
    private const string ScheduledTriggerSource = "scheduled";

    private readonly DashboardConfig _dashboardConfig;
    private readonly Dictionary<string, EndpointConfig> _endpointsById;
    private readonly Dictionary<string, SemaphoreSlim> _endpointLocks;
    private readonly IEndpointPoller _endpointPoller;
    private readonly IHealthResponseParser _healthResponseParser;
    private readonly ILogger<PollingSchedulerService> _logger;
    private readonly IEndpointStateStore _stateStore;
    private readonly TimeProvider _timeProvider;

    public PollingSchedulerService(
        DashboardConfig dashboardConfig,
        IEndpointStateStore stateStore,
        IEndpointPoller endpointPoller,
        IHealthResponseParser healthResponseParser,
        TimeProvider timeProvider,
        ILogger<PollingSchedulerService> logger)
    {
        _dashboardConfig = dashboardConfig;
        _stateStore = stateStore;
        _endpointPoller = endpointPoller;
        _healthResponseParser = healthResponseParser;
        _timeProvider = timeProvider;
        _logger = logger;
        _endpointsById = dashboardConfig.Endpoints.ToDictionary(
            static endpoint => endpoint.Id,
            StringComparer.OrdinalIgnoreCase);
        _endpointLocks = dashboardConfig.Endpoints.ToDictionary(
            static endpoint => endpoint.Id,
            static _ => new SemaphoreSlim(1, 1),
            StringComparer.OrdinalIgnoreCase);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = _dashboardConfig.Endpoints
            .Where(static endpoint => endpoint.Enabled)
            .Select(endpoint => RunEndpointLoopAsync(endpoint, stoppingToken))
            .ToArray();

        _logger.LogInformation(
            "Starting polling scheduler for {EnabledEndpointCount} enabled endpoints out of {TotalEndpointCount} configured endpoints.",
            tasks.Length,
            _dashboardConfig.Endpoints.Count);

        return tasks.Length == 0
            ? Task.CompletedTask
            : Task.WhenAll(tasks);
    }

    public async Task<bool> RefreshEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        if (!_endpointsById.TryGetValue(endpointId, out var endpoint) ||
            !_endpointLocks.TryGetValue(endpointId, out var endpointLock))
        {
            return false;
        }

        var lockAcquired = false;

        try
        {
            lockAcquired = await endpointLock.WaitAsync(0, cancellationToken);
            if (!lockAcquired)
            {
                _logger.LogWarning(
                    "Skipped {TriggerSource} refresh for endpoint {EndpointId} because a poll is already in progress.",
                    ManualTriggerSource,
                    endpointId);

                return false;
            }

            await PollEndpointCoreAsync(endpoint, ManualTriggerSource, cancellationToken);
            return true;
        }
        finally
        {
            if (lockAcquired)
            {
                endpointLock.Release();
            }
        }
    }

    public async Task<int> RefreshAllEnabledAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _dashboardConfig.Endpoints
            .Where(static endpoint => endpoint.Enabled)
            .Select(endpoint => RefreshEndpointAsync(endpoint.Id, cancellationToken))
            .ToArray();

        if (tasks.Length == 0)
        {
            return 0;
        }

        var results = await Task.WhenAll(tasks);
        return results.Count(static result => result);
    }

    private async Task RunEndpointLoopAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(endpoint.FrequencySeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _timeProvider, cancellationToken);

                if (!_endpointLocks.TryGetValue(endpoint.Id, out var endpointLock))
                {
                    _logger.LogWarning(
                        "Skipping scheduled poll for endpoint {EndpointId} because no endpoint lock was configured.",
                        endpoint.Id);
                    continue;
                }

                var lockAcquired = false;

                try
                {
                    lockAcquired = await endpointLock.WaitAsync(0, cancellationToken);
                    if (!lockAcquired)
                    {
                        _logger.LogDebug(
                            "Skipped {TriggerSource} refresh for endpoint {EndpointId} because a poll is already in progress.",
                            ScheduledTriggerSource,
                            endpoint.Id);
                        continue;
                    }

                    await PollEndpointCoreAsync(endpoint, ScheduledTriggerSource, cancellationToken);
                }
                finally
                {
                    if (lockAcquired)
                    {
                        endpointLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Endpoint scheduler loop failed for endpoint {EndpointId}.",
                    endpoint.Id);
            }
        }

        _logger.LogInformation(
            "Stopped scheduler loop for endpoint {EndpointId}.",
            endpoint.Id);
    }

    private async Task PollEndpointCoreAsync(EndpointConfig endpoint, string triggerSource, CancellationToken cancellationToken)
    {
        var state = _stateStore.Get(endpoint.Id) ?? CreateFallbackState(endpoint);
        state.EndpointName = endpoint.Name;
        state.IsPolling = true;
        _stateStore.Upsert(state);

        _logger.LogInformation(
            "Starting {TriggerSource} poll for endpoint {EndpointId} with timeout {TimeoutSeconds}s.",
            triggerSource,
            endpoint.Id,
            endpoint.TimeoutSeconds ?? _dashboardConfig.Dashboard.RequestTimeoutSecondsDefault);

        var result = await _endpointPoller.PollAsync(endpoint, cancellationToken);

        var updatedState = _stateStore.Get(endpoint.Id) ?? CreateFallbackState(endpoint);
        updatedState.EndpointName = endpoint.Name;
        updatedState.LastCheckedUtc = result.CheckedUtc;
        updatedState.DurationMs = result.DurationMs;
        updatedState.IsPolling = false;

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.ResponseBody))
        {
            var snapshot = _healthResponseParser.Parse(endpoint, result.ResponseBody, result.DurationMs);
            updatedState.Snapshot = snapshot;
            updatedState.Status = snapshot.OverallStatus;

            if (TryGetParserError(snapshot, out var parserError))
            {
                updatedState.LastError = $"Failed to parse health response: {parserError}";
                _logger.LogWarning(
                    "Parser produced an error snapshot for endpoint {EndpointId} during a {TriggerSource} poll: {ParserError}",
                    endpoint.Id,
                    triggerSource,
                    parserError);
            }
            else
            {
                updatedState.LastSuccessfulUtc = result.CheckedUtc;
                updatedState.LastError = null;
            }
        }
        else
        {
            updatedState.Status = "Unknown";
            updatedState.LastError = result.ErrorMessage;
        }

        _stateStore.Upsert(updatedState);

        _logger.LogInformation(
            "Completed {TriggerSource} poll for endpoint {EndpointId} with status {EndpointStatus}, result kind {PollResultKind}, duration {DurationMs}ms, and status code {StatusCode}.",
            triggerSource,
            endpoint.Id,
            updatedState.Status,
            result.Kind,
            result.DurationMs,
            result.StatusCode);
    }

    private static EndpointState CreateFallbackState(EndpointConfig endpoint)
    {
        return new EndpointState
        {
            EndpointId = endpoint.Id,
            EndpointName = endpoint.Name,
            Status = "Unknown"
        };
    }

    private static bool TryGetParserError(HealthSnapshot snapshot, out string parserError)
    {
        if (snapshot.Metadata.TryGetValue("parserError", out var value) &&
            value is string errorText &&
            !string.IsNullOrWhiteSpace(errorText))
        {
            parserError = errorText;
            return true;
        }

        parserError = string.Empty;
        return false;
    }
}
