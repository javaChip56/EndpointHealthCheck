using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Scheduling;

public sealed class PollingSchedulerServiceTests
{
    [Fact]
    public async Task RefreshEndpointAsync_OnSuccessfulPoll_UpdatesRuntimeState()
    {
        var endpoint = new EndpointConfig
        {
            Id = "orders-api",
            Name = "Orders API",
            Url = "https://orders.example.com/health",
            Enabled = true,
            FrequencySeconds = 30
        };

        var store = new InMemoryEndpointStateStore([endpoint]);
        var scheduler = CreateScheduler(
            new DashboardConfig { Endpoints = [endpoint] },
            store,
            new DelegateEndpointPoller((_, _) => Task.FromResult(new PollResult
            {
                Kind = PollResultKind.Success,
                CheckedUtc = DateTimeOffset.UtcNow,
                DurationMs = 123,
                ResponseBody = """{"status":"Healthy"}"""
            })),
            new DelegateHealthResponseParser((_, _, durationMs) => new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                RawPayload = """{"status":"Healthy"}"""
            }));

        var refreshed = await scheduler.RefreshEndpointAsync("orders-api");
        var state = store.Get("orders-api");

        Assert.True(refreshed);
        Assert.NotNull(state);
        Assert.Equal("Healthy", state!.Status);
        Assert.False(state.IsPolling);
        Assert.Equal(123, state.DurationMs);
        Assert.NotNull(state.LastCheckedUtc);
        Assert.NotNull(state.LastSuccessfulUtc);
        Assert.Null(state.LastError);
        Assert.NotNull(state.Snapshot);
        Assert.Equal("Healthy", state.Snapshot!.OverallStatus);
        var recentSample = Assert.Single(state.RecentSamples);
        Assert.Equal("Healthy", recentSample.Status);
        Assert.Equal("Success", recentSample.ResultKind);
        Assert.Null(recentSample.ErrorSummary);
    }

    [Fact]
    public async Task RefreshEndpointAsync_WhenEndpointAlreadyPolling_ReturnsFalseAndPreventsOverlap()
    {
        var endpoint = new EndpointConfig
        {
            Id = "orders-api",
            Name = "Orders API",
            Url = "https://orders.example.com/health",
            Enabled = true,
            FrequencySeconds = 30
        };

        var pollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPollToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollCount = 0;

        var scheduler = CreateScheduler(
            new DashboardConfig { Endpoints = [endpoint] },
            new InMemoryEndpointStateStore([endpoint]),
            new DelegateEndpointPoller(async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref pollCount);
                pollStarted.TrySetResult();
                await allowPollToFinish.Task.WaitAsync(cancellationToken);
                return new PollResult
                {
                    Kind = PollResultKind.Success,
                    CheckedUtc = DateTimeOffset.UtcNow,
                    DurationMs = 10,
                    ResponseBody = """{"status":"Healthy"}"""
                };
            }),
            new DelegateHealthResponseParser((_, _, durationMs) => new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs
            }));

        var firstRefreshTask = scheduler.RefreshEndpointAsync("orders-api");
        await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondRefreshResult = await scheduler.RefreshEndpointAsync("orders-api");

        allowPollToFinish.TrySetResult();
        var firstRefreshResult = await firstRefreshTask;

        Assert.True(firstRefreshResult);
        Assert.False(secondRefreshResult);
        Assert.Equal(1, pollCount);
    }

    [Fact]
    public async Task StartAsync_PollsEnabledEndpointsIndependentlyAndSkipsDisabledEndpoints()
    {
        var orders = new EndpointConfig
        {
            Id = "orders-api",
            Name = "Orders API",
            Url = "https://orders.example.com/health",
            Enabled = true,
            FrequencySeconds = 1
        };

        var billing = new EndpointConfig
        {
            Id = "billing-api",
            Name = "Billing API",
            Url = "https://billing.example.com/health",
            Enabled = true,
            FrequencySeconds = 1
        };

        var disabled = new EndpointConfig
        {
            Id = "disabled-api",
            Name = "Disabled API",
            Url = "https://disabled.example.com/health",
            Enabled = false,
            FrequencySeconds = 1
        };

        var slowPollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSlowPollToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fastPollCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polledEndpoints = new List<string>();
        var syncRoot = new object();

        var scheduler = CreateScheduler(
            new DashboardConfig { Endpoints = [orders, billing, disabled] },
            new InMemoryEndpointStateStore([orders, billing, disabled]),
            new DelegateEndpointPoller(async (endpoint, cancellationToken) =>
            {
                lock (syncRoot)
                {
                    polledEndpoints.Add(endpoint.Id);
                }

                if (endpoint.Id == "orders-api")
                {
                    slowPollStarted.TrySetResult();
                    await allowSlowPollToFinish.Task.WaitAsync(cancellationToken);
                }
                else if (endpoint.Id == "billing-api")
                {
                    fastPollCompleted.TrySetResult();
                }

                return new PollResult
                {
                    Kind = PollResultKind.Success,
                    CheckedUtc = DateTimeOffset.UtcNow,
                    DurationMs = 25,
                    ResponseBody = """{"status":"Healthy"}"""
                };
            }),
            new DelegateHealthResponseParser((endpoint, _, durationMs) => new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                RawPayload = endpoint.Id
            }));

        await scheduler.StartAsync(CancellationToken.None);

        try
        {
            await slowPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await fastPollCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            lock (syncRoot)
            {
                Assert.Contains("orders-api", polledEndpoints);
                Assert.Contains("billing-api", polledEndpoints);
                Assert.DoesNotContain("disabled-api", polledEndpoints);
            }
        }
        finally
        {
            allowSlowPollToFinish.TrySetResult();
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RefreshAllEnabledAsync_ReturnsStartedRefreshCountForEnabledEndpointsOnly()
    {
        var orders = new EndpointConfig
        {
            Id = "orders-api",
            Name = "Orders API",
            Url = "https://orders.example.com/health",
            Enabled = true,
            FrequencySeconds = 30
        };

        var billing = new EndpointConfig
        {
            Id = "billing-api",
            Name = "Billing API",
            Url = "https://billing.example.com/health",
            Enabled = false,
            FrequencySeconds = 30
        };

        var startedEndpointIds = new List<string>();

        var scheduler = CreateScheduler(
            new DashboardConfig { Endpoints = [orders, billing] },
            new InMemoryEndpointStateStore([orders, billing]),
            new DelegateEndpointPoller((endpoint, _) =>
            {
                startedEndpointIds.Add(endpoint.Id);
                return Task.FromResult(new PollResult
                {
                    Kind = PollResultKind.Success,
                    CheckedUtc = DateTimeOffset.UtcNow,
                    DurationMs = 5,
                    ResponseBody = """{"status":"Healthy"}"""
                });
            }),
            new DelegateHealthResponseParser((_, _, durationMs) => new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs
            }));

        var refreshedCount = await scheduler.RefreshAllEnabledAsync();

        Assert.Equal(1, refreshedCount);
        Assert.Equal(["orders-api"], startedEndpointIds);
    }

    [Fact]
    public async Task RefreshEndpointAsync_WhenParserReturnsErrorSnapshot_PersistsLastError()
    {
        var endpoint = new EndpointConfig
        {
            Id = "orders-api",
            Name = "Orders API",
            Url = "https://orders.example.com/health",
            Enabled = true,
            FrequencySeconds = 30
        };

        var store = new InMemoryEndpointStateStore([endpoint]);
        var scheduler = CreateScheduler(
            new DashboardConfig { Endpoints = [endpoint] },
            store,
            new DelegateEndpointPoller((_, _) => Task.FromResult(new PollResult
            {
                Kind = PollResultKind.Success,
                CheckedUtc = DateTimeOffset.UtcNow,
                DurationMs = 55,
                ResponseBody = """{"status":"Healthy"}"""
            })),
            new DelegateHealthResponseParser((_, _, durationMs) => new HealthSnapshot
            {
                OverallStatus = "Unknown",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                Metadata = new Dictionary<string, object?>
                {
                    ["parserError"] = "Malformed nested payload"
                }
            }));

        var refreshed = await scheduler.RefreshEndpointAsync("orders-api");
        var state = store.Get("orders-api");

        Assert.True(refreshed);
        Assert.NotNull(state);
        Assert.Equal("Unknown", state!.Status);
        Assert.Equal("Failed to parse health response: Malformed nested payload", state.LastError);
        Assert.NotNull(state.Snapshot);
        var recentSample = Assert.Single(state.RecentSamples);
        Assert.Equal("Unknown", recentSample.Status);
        Assert.Equal("Success", recentSample.ResultKind);
        Assert.Equal("Failed to parse health response: Malformed nested payload", recentSample.ErrorSummary);
    }

    [Fact]
    public async Task RefreshEndpointAsync_TrimsRecentSamplesToConfiguredLimit()
    {
        var endpoint = new EndpointConfig
        {
            Id = "orders-api",
            Name = "Orders API",
            Url = "https://orders.example.com/health",
            Enabled = true,
            FrequencySeconds = 30
        };

        var store = new InMemoryEndpointStateStore([endpoint]);
        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T00:00:00Z"),
                    Status = "Healthy",
                    DurationMs = 10,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T00:01:00Z"),
                    Status = "Healthy",
                    DurationMs = 11,
                    ResultKind = "Success"
                }
            ]
        });

        var scheduler = CreateScheduler(
            new DashboardConfig { Endpoints = [endpoint] },
            store,
            new DelegateEndpointPoller((_, _) => Task.FromResult(new PollResult
            {
                Kind = PollResultKind.Timeout,
                CheckedUtc = DateTimeOffset.Parse("2026-03-19T00:02:00Z"),
                DurationMs = 999,
                ErrorMessage = "Timed out"
            })),
            new DelegateHealthResponseParser((_, _, durationMs) => new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs
            }),
            new RuntimeStateOptions
            {
                RecentSampleLimit = 2
            });

        await scheduler.RefreshEndpointAsync("orders-api");

        var state = store.Get("orders-api");

        Assert.NotNull(state);
        Assert.Equal(2, state!.RecentSamples.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-03-19T00:01:00Z"), state.RecentSamples[0].CheckedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-03-19T00:02:00Z"), state.RecentSamples[1].CheckedUtc);
        Assert.Equal("Timeout", state.RecentSamples[1].ResultKind);
    }

    private static PollingSchedulerService CreateScheduler(
        DashboardConfig config,
        IEndpointStateStore stateStore,
        IEndpointPoller endpointPoller,
        IHealthResponseParser healthResponseParser,
        RuntimeStateOptions? runtimeStateOptions = null)
    {
        return new PollingSchedulerService(
            config,
            stateStore,
            endpointPoller,
            new NoOpEndpointNotificationService(),
            healthResponseParser,
            runtimeStateOptions ?? new RuntimeStateOptions(),
            TimeProvider.System,
            NullLogger<PollingSchedulerService>.Instance);
    }

    private sealed class DelegateEndpointPoller : IEndpointPoller
    {
        private readonly Func<EndpointConfig, CancellationToken, Task<PollResult>> _pollAsync;

        public DelegateEndpointPoller(Func<EndpointConfig, CancellationToken, Task<PollResult>> pollAsync)
        {
            _pollAsync = pollAsync;
        }

        public Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
        {
            return _pollAsync(endpoint, cancellationToken);
        }
    }

    private sealed class DelegateHealthResponseParser : IHealthResponseParser
    {
        private readonly Func<EndpointConfig, string, long, HealthSnapshot> _parse;

        public DelegateHealthResponseParser(Func<EndpointConfig, string, long, HealthSnapshot> parse)
        {
            _parse = parse;
        }

        public HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs)
        {
            return _parse(endpoint, json, durationMs);
        }
    }

    private sealed class NoOpEndpointNotificationService : IEndpointNotificationService
    {
        public Task NotifyAsync(EndpointConfig endpoint, EndpointState? previousState, EndpointState currentState, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
