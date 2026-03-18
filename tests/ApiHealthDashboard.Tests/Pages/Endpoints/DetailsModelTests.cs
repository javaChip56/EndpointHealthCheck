using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Pages.Endpoints;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Pages.Endpoints;

public sealed class DetailsModelTests
{
    [Fact]
    public void OnGet_WithUnknownEndpoint_ReturnsNotFound()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new DetailsModel(config, store, new StubEndpointScheduler(), NullLogger<DetailsModel>.Instance);

        var result = model.OnGet("missing-api");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostRefreshAsync_SetsSuccessMessageAndRedirectsToEndpoint()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new DetailsModel(config, store, new StubEndpointScheduler(refreshEndpointResult: true), NullLogger<DetailsModel>.Instance)
        {
            TempData = PageModelTestHelpers.CreateTempData()
        };

        var result = await model.OnPostRefreshAsync("orders-api", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("orders-api", redirect.RouteValues!["id"]);
        Assert.Equal("Triggered refresh for endpoint 'orders-api'.", model.TempData["StatusMessage"]);
        Assert.Equal("success", model.TempData["StatusType"]);
    }

    [Fact]
    public void OnGet_WithKnownEndpoint_LoadsEndpointDetails()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new DetailsModel(config, store, new StubEndpointScheduler(), NullLogger<DetailsModel>.Instance);

        var result = model.OnGet("orders-api");

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Endpoint);
        Assert.Equal("orders-api", model.Endpoint!.Id);
    }

    [Fact]
    public void OnGet_WithoutRouteId_LoadsFirstConfiguredEndpoint()
    {
        var config = CreateConfig();
        config.Endpoints.Add(new EndpointConfig
        {
            Id = "billing-api",
            Name = "Billing API",
            Url = "https://billing.example.com/health",
            FrequencySeconds = 60
        });

        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new DetailsModel(config, store, new StubEndpointScheduler(), NullLogger<DetailsModel>.Instance);

        var result = model.OnGet(null);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Endpoint);
        Assert.Equal("orders-api", model.Endpoint!.Id);
    }

    [Fact]
    public void OnGet_WithSnapshot_LoadsDetailedDiagnostics()
    {
        var config = CreateConfig(showRawPayload: true);
        config.Endpoints[0].Headers["X-Api-Key"] = "super-secret";
        config.Endpoints[0].IncludeChecks.Add("database");
        config.Endpoints[0].ExcludeChecks.Add("cache");

        var store = new InMemoryEndpointStateStore(config.Endpoints);
        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Degraded",
            LastCheckedUtc = new DateTimeOffset(2026, 03, 18, 8, 30, 0, TimeSpan.Zero),
            LastSuccessfulUtc = new DateTimeOffset(2026, 03, 18, 8, 29, 0, TimeSpan.Zero),
            DurationMs = 420,
            LastError = "Database latency exceeded threshold.",
            Snapshot = new HealthSnapshot
            {
                OverallStatus = "Degraded",
                RetrievedUtc = new DateTimeOffset(2026, 03, 18, 8, 30, 0, TimeSpan.Zero),
                DurationMs = 420,
                RawPayload = "{\"status\":\"Degraded\"}",
                Metadata = new Dictionary<string, object?>
                {
                    ["region"] = "sgp-1",
                    ["statusCode"] = 200
                },
                Nodes =
                [
                    new HealthNode
                    {
                        Name = "database",
                        Status = "Degraded",
                        DurationText = "350 ms",
                        Children =
                        [
                            new HealthNode
                            {
                                Name = "primary",
                                Status = "Unhealthy",
                                ErrorMessage = "Connection timeout"
                            }
                        ]
                    },
                    new HealthNode
                    {
                        Name = "messaging",
                        Status = "Healthy"
                    }
                ]
            }
        });

        var model = new DetailsModel(config, store, new StubEndpointScheduler(), NullLogger<DetailsModel>.Instance);

        var result = model.OnGet("orders-api");

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Endpoint);
        Assert.Equal(2, model.Endpoint!.TopLevelCheckCount);
        Assert.Equal(3, model.Endpoint.TotalCheckCount);
        Assert.Equal(1, model.Endpoint.NestedCheckCount);
        Assert.Equal(1, model.Endpoint.HealthyCheckCount);
        Assert.Equal(1, model.Endpoint.DegradedCheckCount);
        Assert.Equal(1, model.Endpoint.UnhealthyCheckCount);
        Assert.Equal("********", model.Endpoint.Headers.Single().ValuePreview);
        Assert.Equal(["database"], model.Endpoint.IncludeChecks);
        Assert.Equal(["cache"], model.Endpoint.ExcludeChecks);
        Assert.Equal(2, model.Endpoint.SnapshotMetadata.Count);
        Assert.Equal("{\"status\":\"Degraded\"}", model.Endpoint.RawPayload);
    }

    [Fact]
    public void OnGet_WhenRawPayloadIsDisabled_HidesCapturedPayload()
    {
        var config = CreateConfig(showRawPayload: false);
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            Snapshot = new HealthSnapshot
            {
                RawPayload = "{\"status\":\"Healthy\"}"
            }
        });

        var model = new DetailsModel(config, store, new StubEndpointScheduler(), NullLogger<DetailsModel>.Instance);

        var result = model.OnGet("orders-api");

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Endpoint);
        Assert.False(model.Endpoint!.ShowRawPayload);
        Assert.Null(model.Endpoint.RawPayload);
    }

    [Fact]
    public async Task OnPostRefreshAsync_WithoutAnyConfiguredEndpoint_ReturnsNotFound()
    {
        var config = new DashboardConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new DetailsModel(config, store, new StubEndpointScheduler(), NullLogger<DetailsModel>.Instance)
        {
            TempData = PageModelTestHelpers.CreateTempData()
        };

        var result = await model.OnPostRefreshAsync(null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    private static DashboardConfig CreateConfig(bool showRawPayload = false)
    {
        return new DashboardConfig
        {
            Dashboard = new DashboardSettings
            {
                ShowRawPayload = showRawPayload
            },
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
    }

    private sealed class StubEndpointScheduler : IEndpointScheduler
    {
        private readonly bool _refreshEndpointResult;

        public StubEndpointScheduler(bool refreshEndpointResult = true)
        {
            _refreshEndpointResult = refreshEndpointResult;
        }

        public Task<bool> RefreshEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_refreshEndpointResult);
        }

        public Task<int> RefreshAllEnabledAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
