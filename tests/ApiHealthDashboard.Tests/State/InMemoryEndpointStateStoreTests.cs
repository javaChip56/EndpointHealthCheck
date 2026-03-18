using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.State;

namespace ApiHealthDashboard.Tests.State;

public sealed class InMemoryEndpointStateStoreTests
{
    [Fact]
    public void Constructor_InitializesStatesForConfiguredEndpoints()
    {
        var store = new InMemoryEndpointStateStore(
            [
                new EndpointConfig { Id = "orders-api", Name = "Orders API", Url = "https://orders.example.com/health" },
                new EndpointConfig { Id = "billing-api", Name = "Billing API", Url = "https://billing.example.com/health" }
            ]);

        var states = store.GetAll();

        Assert.Equal(2, states.Count);
        Assert.Contains(states, static state => state.EndpointId == "orders-api" && state.Status == "Unknown");
        Assert.Contains(states, static state => state.EndpointId == "billing-api" && state.Status == "Unknown");
    }

    [Fact]
    public void Upsert_StoresAndReturnsDeepCopies()
    {
        var store = new InMemoryEndpointStateStore(
            [new EndpointConfig { Id = "orders-api", Name = "Orders API", Url = "https://orders.example.com/health" }]);

        var originalState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastError = "none",
            IsPolling = true,
            Snapshot = new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RawPayload = """{"status":"Healthy"}""",
                Nodes =
                [
                    new HealthNode
                    {
                        Name = "database",
                        Status = "Healthy",
                        Children =
                        [
                            new HealthNode { Name = "read-model", Status = "Healthy" }
                        ]
                    }
                ]
            }
        };

        store.Upsert(originalState);

        originalState.Status = "Unhealthy";
        originalState.Snapshot!.Nodes[0].Status = "Unhealthy";

        var storedState = store.Get("orders-api");

        Assert.NotNull(storedState);
        Assert.Equal("Healthy", storedState!.Status);
        Assert.Equal("Healthy", storedState.Snapshot!.Nodes[0].Status);

        storedState.Snapshot.Nodes[0].Children[0].Status = "Unhealthy";

        var storedStateAgain = store.Get("orders-api");
        Assert.Equal("Healthy", storedStateAgain!.Snapshot!.Nodes[0].Children[0].Status);
    }

    [Fact]
    public void Initialize_ReplacesExistingRuntimeStateWithConfiguredEndpoints()
    {
        var store = new InMemoryEndpointStateStore(
            [new EndpointConfig { Id = "orders-api", Name = "Orders API", Url = "https://orders.example.com/health" }]);

        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastError = "stale"
        });

        store.Initialize(
            [
                new EndpointConfig { Id = "billing-api", Name = "Billing API", Url = "https://billing.example.com/health" }
            ]);

        Assert.Null(store.Get("orders-api"));

        var billingState = store.Get("billing-api");
        Assert.NotNull(billingState);
        Assert.Equal("Unknown", billingState!.Status);
        Assert.Null(billingState.LastError);
    }

    [Fact]
    public void Initialize_PreservesExistingRuntimeStateForMatchingEndpointIds()
    {
        var store = new InMemoryEndpointStateStore(
            [new EndpointConfig { Id = "orders-api", Name = "Orders API", Url = "https://orders.example.com/health" }]);

        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastError = "none"
        });

        store.Initialize(
            [
                new EndpointConfig { Id = "orders-api", Name = "Orders API Reloaded", Url = "https://orders.example.com/health" }
            ]);

        var state = store.Get("orders-api");
        Assert.NotNull(state);
        Assert.Equal("Healthy", state!.Status);
        Assert.Equal("Orders API Reloaded", state.EndpointName);
        Assert.Equal("none", state.LastError);
    }

    [Fact]
    public async Task Upsert_AllowsConcurrentUpdatesWithoutCorruptingTheStore()
    {
        var store = new InMemoryEndpointStateStore(
            [
                new EndpointConfig { Id = "orders-api", Name = "Orders API", Url = "https://orders.example.com/health" },
                new EndpointConfig { Id = "billing-api", Name = "Billing API", Url = "https://billing.example.com/health" }
            ]);

        var tasks = Enumerable.Range(0, 200)
            .Select(index => Task.Run(() =>
            {
                var endpointId = index % 2 == 0 ? "orders-api" : "billing-api";
                store.Upsert(new EndpointState
                {
                    EndpointId = endpointId,
                    EndpointName = endpointId == "orders-api" ? "Orders API" : "Billing API",
                    Status = index % 3 == 0 ? "Healthy" : "Degraded",
                    DurationMs = index,
                    LastCheckedUtc = DateTimeOffset.UtcNow
                });
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var states = store.GetAll();

        Assert.Equal(2, states.Count);
        Assert.Contains(states, static state => state.EndpointId == "orders-api");
        Assert.Contains(states, static state => state.EndpointId == "billing-api");
        Assert.All(states, static state => Assert.NotEqual(string.Empty, state.Status));
    }
}
