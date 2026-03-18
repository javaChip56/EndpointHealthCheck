using ApiHealthDashboard.Configuration;
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
