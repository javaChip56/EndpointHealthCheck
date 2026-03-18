using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Pages;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Pages;

public sealed class IndexModelTests
{
    [Fact]
    public async Task OnPostRefreshAllAsync_SetsSuccessMessageAndRedirects()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var scheduler = new StubEndpointScheduler(refreshAllCount: 2);
        var model = new IndexModel(config, store, scheduler, NullLogger<IndexModel>.Instance)
        {
            TempData = PageModelTestHelpers.CreateTempData()
        };

        var result = await model.OnPostRefreshAllAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Equal("Triggered refresh for 2 enabled endpoint(s).", model.TempData["StatusMessage"]);
        Assert.Equal("success", model.TempData["StatusType"]);
    }

    [Fact]
    public async Task OnPostRefreshEndpointAsync_SetsWarningWhenRefreshSkipped()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var scheduler = new StubEndpointScheduler(refreshEndpointResult: false);
        var model = new IndexModel(config, store, scheduler, NullLogger<IndexModel>.Instance)
        {
            TempData = PageModelTestHelpers.CreateTempData()
        };

        var result = await model.OnPostRefreshEndpointAsync("orders-api", CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(
            "Refresh for endpoint 'orders-api' was skipped because it is already polling or not configured.",
            model.TempData["StatusMessage"]);
        Assert.Equal("warning", model.TempData["StatusType"]);
    }

    [Fact]
    public async Task OnPostRefreshEndpointAsync_WithoutEndpointId_SetsWarning()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance)
        {
            TempData = PageModelTestHelpers.CreateTempData()
        };

        var result = await model.OnPostRefreshEndpointAsync(string.Empty, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("No endpoint was selected for refresh.", model.TempData["StatusMessage"]);
        Assert.Equal("warning", model.TempData["StatusType"]);
    }

    [Fact]
    public void OnGet_LoadsConfiguredEndpointSummaries()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance);

        model.OnGet();

        Assert.Equal(2, model.Endpoints.Count);
        Assert.Equal(2, model.Counters.Total);
    }

    [Fact]
    public void OnGet_CalculatesMixedStatusCountersAndProblemEndpoints()
    {
        var config = CreateConfig();
        config.Endpoints[0].Priority = EndpointPriority.High;
        config.Endpoints[1].Priority = EndpointPriority.Critical;
        var store = new InMemoryEndpointStateStore(config.Endpoints);

        store.Upsert(new ApiHealthDashboard.Domain.EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy"
        });

        store.Upsert(new ApiHealthDashboard.Domain.EndpointState
        {
            EndpointId = "billing-api",
            EndpointName = "Billing API",
            Status = "Degraded",
            LastError = "Dependency timeout",
            RecentSamples =
            [
                new ApiHealthDashboard.Domain.RecentPollSample
                {
                    CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 0, 0, TimeSpan.Zero),
                    Status = "Healthy",
                    DurationMs = 120,
                    ResultKind = "Success"
                },
                new ApiHealthDashboard.Domain.RecentPollSample
                {
                    CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 1, 0, TimeSpan.Zero),
                    Status = "Degraded",
                    DurationMs = 340,
                    ResultKind = "Success",
                    ErrorSummary = "Dependency timeout"
                }
            ]
        });

        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance);

        model.OnGet();

        Assert.Equal(2, model.Counters.Total);
        Assert.Equal(2, model.Counters.Enabled);
        Assert.Equal(0, model.Counters.Disabled);
        Assert.Equal(1, model.Counters.Healthy);
        Assert.Equal(1, model.Counters.Degraded);
        Assert.Equal(0, model.Counters.Unhealthy);
        Assert.Equal(0, model.Counters.Unknown);
        Assert.Single(model.ProblemEndpoints);
        Assert.Equal("billing-api", model.ProblemEndpoints[0].Id);
        Assert.Equal(EndpointPriority.Critical, model.ProblemEndpoints[0].Priority);
        Assert.Equal("50% success", model.ProblemEndpoints[0].RecentSuccessRateText);
        Assert.Equal("230 ms avg", model.ProblemEndpoints[0].RecentAverageDurationText);
    }

    [Fact]
    public void OnGet_SortsEndpointsByPriorityBeforeName()
    {
        var config = CreateConfig();
        config.Endpoints[0].Priority = EndpointPriority.Low;
        config.Endpoints[1].Priority = EndpointPriority.Critical;
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance);

        model.OnGet();

        Assert.Equal("billing-api", model.Endpoints[0].Id);
        Assert.Equal(EndpointPriority.Critical, model.Endpoints[0].Priority);
    }

    [Fact]
    public void OnGet_WithNoConfiguredEndpoints_ExposesEmptyDashboardState()
    {
        var config = new DashboardConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance);

        model.OnGet();

        Assert.False(model.HasConfiguredEndpoints);
        Assert.Empty(model.Endpoints);
        Assert.Empty(model.ProblemEndpoints);
        Assert.Equal(0, model.Counters.Total);
    }

    [Fact]
    public void OnGetLiveSection_ReturnsDashboardPartial()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance)
        {
            PageContext = new PageContext
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            }
        };

        var result = model.OnGetLiveSection();

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_DashboardLiveSection", partial.ViewName);
        Assert.Equal(config.Dashboard.RefreshUiSeconds, model.RefreshUiSeconds);
    }

    [Fact]
    public void OnGet_CountsDisabledAndUnknownEndpoints()
    {
        var config = CreateConfig();
        config.Endpoints[1].Enabled = false;

        var store = new InMemoryEndpointStateStore(config.Endpoints);
        store.Upsert(new ApiHealthDashboard.Domain.EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy"
        });

        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance);

        model.OnGet();

        Assert.Equal(1, model.Counters.Enabled);
        Assert.Equal(1, model.Counters.Disabled);
        Assert.Equal(1, model.Counters.Unknown);
    }

    private static DashboardConfig CreateConfig()
    {
        return new DashboardConfig
        {
            Dashboard = new DashboardSettings
            {
                RefreshUiSeconds = 10
            },
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    Priority = EndpointPriority.Normal,
                    Enabled = true,
                    FrequencySeconds = 30
                },
                new EndpointConfig
                {
                    Id = "billing-api",
                    Name = "Billing API",
                    Url = "https://billing.example.com/health",
                    Priority = EndpointPriority.Normal,
                    Enabled = true,
                    FrequencySeconds = 60
                }
            ]
        };
    }

    private sealed class StubEndpointScheduler : IEndpointScheduler
    {
        private readonly int _refreshAllCount;
        private readonly bool _refreshEndpointResult;

        public StubEndpointScheduler(int refreshAllCount = 0, bool refreshEndpointResult = true)
        {
            _refreshAllCount = refreshAllCount;
            _refreshEndpointResult = refreshEndpointResult;
        }

        public Task<bool> RefreshEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_refreshEndpointResult);
        }

        public Task<int> RefreshAllEnabledAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_refreshAllCount);
        }
    }
}
