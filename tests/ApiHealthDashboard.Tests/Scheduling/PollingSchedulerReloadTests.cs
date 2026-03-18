using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Scheduling;

public sealed class PollingSchedulerReloadTests
{
    [Fact]
    public async Task ReloadConfigurationAsync_WhenNotStarted_SynchronizesStateStoreToCurrentConfig()
    {
        var sharedConfig = new DashboardConfig
        {
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    Enabled = true,
                    FrequencySeconds = 30
                }
            ]
        };
        var stateStore = new InMemoryEndpointStateStore(sharedConfig.Endpoints);
        stateStore.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy"
        });

        var scheduler = new PollingSchedulerService(
            sharedConfig,
            stateStore,
            new NoOpEndpointPoller(),
            new NoOpHealthResponseParser(),
            TimeProvider.System,
            NullLogger<PollingSchedulerService>.Instance);

        sharedConfig.CopyFrom(new DashboardConfig
        {
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "billing-api",
                    Name = "Billing API",
                    Url = "https://billing.example.com/health",
                    Enabled = true,
                    FrequencySeconds = 60
                }
            ]
        });

        await scheduler.ReloadConfigurationAsync();

        Assert.Null(stateStore.Get("orders-api"));
        Assert.NotNull(stateStore.Get("billing-api"));
    }

    private sealed class NoOpEndpointPoller : IEndpointPoller
    {
        public Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NoOpHealthResponseParser : IHealthResponseParser
    {
        public HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs)
        {
            throw new NotSupportedException();
        }
    }
}
