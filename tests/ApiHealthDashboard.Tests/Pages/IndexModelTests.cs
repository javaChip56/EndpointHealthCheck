using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Pages;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
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
    public void OnGet_LoadsConfiguredEndpointSummaries()
    {
        var config = CreateConfig();
        var store = new InMemoryEndpointStateStore(config.Endpoints);
        var model = new IndexModel(config, store, new StubEndpointScheduler(), NullLogger<IndexModel>.Instance);

        model.OnGet();

        Assert.Equal(2, model.Endpoints.Count);
        Assert.Equal(2, model.Counters.Total);
    }

    private static DashboardConfig CreateConfig()
    {
        return new DashboardConfig
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
                },
                new EndpointConfig
                {
                    Id = "billing-api",
                    Name = "Billing API",
                    Url = "https://billing.example.com/health",
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
