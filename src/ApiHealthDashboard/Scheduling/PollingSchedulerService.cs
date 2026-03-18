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
    private readonly IEndpointPoller _endpointPoller;
    private readonly IHealthResponseParser _healthResponseParser;
    private readonly ILogger<PollingSchedulerService> _logger;
    private readonly IEndpointStateStore _stateStore;
    private readonly TimeProvider _timeProvider;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, EndpointLoopRegistration> _loopRegistrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _endpointLocks = new(StringComparer.OrdinalIgnoreCase);
    private CancellationToken _serviceCancellationToken;
    private bool _started;

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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Starting polling scheduler for {EnabledEndpointCount} enabled endpoints out of {TotalEndpointCount} configured endpoints.",
            _dashboardConfig.Endpoints.Count(static endpoint => endpoint.Enabled),
            _dashboardConfig.Endpoints.Count);

        lock (_syncRoot)
        {
            _serviceCancellationToken = stoppingToken;
            _started = true;
        }

        await ApplyCurrentConfigurationAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await StopAllLoopsAsync();
        }
    }

    public async Task<bool> RefreshEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        var endpoint = GetEndpoint(endpointId);
        var endpointLock = GetOrCreateEndpointLock(endpointId);
        if (endpoint is null)
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

    public async Task ReloadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Reloading polling scheduler for {EnabledEndpointCount} enabled endpoints out of {TotalEndpointCount} configured endpoints.",
            _dashboardConfig.Endpoints.Count(static endpoint => endpoint.Enabled),
            _dashboardConfig.Endpoints.Count);

        if (!_started)
        {
            _stateStore.Initialize(_dashboardConfig.Endpoints);
            return;
        }

        await ApplyCurrentConfigurationAsync(cancellationToken);
    }

    private async Task RunEndpointLoopAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(endpoint.FrequencySeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _timeProvider, cancellationToken);

                var endpointLock = GetOrCreateEndpointLock(endpoint.Id);
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

    private EndpointConfig? GetEndpoint(string endpointId)
    {
        return _dashboardConfig.Endpoints.FirstOrDefault(
            endpoint => string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));
    }

    private SemaphoreSlim GetOrCreateEndpointLock(string endpointId)
    {
        lock (_syncRoot)
        {
            if (_endpointLocks.TryGetValue(endpointId, out var existingLock))
            {
                return existingLock;
            }

            var endpointLock = new SemaphoreSlim(1, 1);
            _endpointLocks[endpointId] = endpointLock;
            return endpointLock;
        }
    }

    private async Task ApplyCurrentConfigurationAsync(CancellationToken cancellationToken)
    {
        EndpointLoopRegistration[] previousRegistrations;
        List<EndpointConfig> enabledEndpoints;
        CancellationToken serviceToken;

        lock (_syncRoot)
        {
            previousRegistrations = _loopRegistrations.Values.ToArray();
            _loopRegistrations.Clear();
            enabledEndpoints = _dashboardConfig.Endpoints
                .Where(static endpoint => endpoint.Enabled)
                .Select(static endpoint => endpoint.Clone())
                .ToList();
            serviceToken = _serviceCancellationToken;
        }

        foreach (var registration in previousRegistrations)
        {
            registration.CancellationTokenSource.Cancel();
        }

        if (previousRegistrations.Length > 0)
        {
            try
            {
                await Task.WhenAll(previousRegistrations.Select(static registration => registration.Task));
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stateStore.Initialize(_dashboardConfig.Endpoints);

        foreach (var endpoint in enabledEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var loopCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            var loopTask = RunEndpointLoopAsync(endpoint, loopCts.Token);

            lock (_syncRoot)
            {
                _endpointLocks[endpoint.Id] = GetOrCreateEndpointLock(endpoint.Id);
                _loopRegistrations[endpoint.Id] = new EndpointLoopRegistration(loopCts, loopTask);
            }
        }
    }

    private async Task StopAllLoopsAsync()
    {
        EndpointLoopRegistration[] registrations;

        lock (_syncRoot)
        {
            registrations = _loopRegistrations.Values.ToArray();
            _loopRegistrations.Clear();
            _started = false;
        }

        foreach (var registration in registrations)
        {
            registration.CancellationTokenSource.Cancel();
        }

        if (registrations.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(registrations.Select(static registration => registration.Task));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record EndpointLoopRegistration(
        CancellationTokenSource CancellationTokenSource,
        Task Task);
}
