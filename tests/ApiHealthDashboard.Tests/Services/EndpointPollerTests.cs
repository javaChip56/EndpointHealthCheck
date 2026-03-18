using System.Net;
using System.Net.Http.Headers;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Services;

public sealed class EndpointPollerTests
{
    [Fact]
    public async Task PollAsync_WithSuccessfulResponse_ReturnsPayloadAndAppliesHeaders()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"Healthy"}""")
            });
        });

        var poller = CreatePoller(handler);

        var result = await poller.PollAsync(
            new EndpointConfig
            {
                Id = "orders-api",
                Name = "Orders API",
                Url = "https://orders.example.com/health",
                TimeoutSeconds = 5,
                Headers = new Dictionary<string, string>
                {
                    ["X-Trace-Id"] = "trace-123"
                }
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PollResultKind.Success, result.Kind);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("""{"status":"Healthy"}""", result.ResponseBody);
        Assert.Null(result.ErrorMessage);
        Assert.NotEqual(default, result.CheckedUtc);
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.TryGetValues("X-Trace-Id", out var values));
        Assert.Equal("trace-123", Assert.Single(values));
    }

    [Fact]
    public async Task PollAsync_WithTimeout_ReturnsTimeoutResult()
    {
        var handler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"Healthy"}""")
                };
            });

        var poller = CreatePoller(
            handler,
            new DashboardConfig
            {
                Dashboard = new DashboardSettings
                {
                    RequestTimeoutSecondsDefault = 1
                }
            });

        var result = await poller.PollAsync(
            new EndpointConfig
            {
                Id = "slow-api",
                Name = "Slow API",
                Url = "https://slow.example.com/health"
            },
            CancellationToken.None);

        Assert.Equal(PollResultKind.Timeout, result.Kind);
        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PollAsync_WithNetworkFailure_ReturnsNetworkErrorResult()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("No such host is known."));

        var poller = CreatePoller(handler);

        var result = await poller.PollAsync(
            new EndpointConfig
            {
                Id = "broken-api",
                Name = "Broken API",
                Url = "https://broken.example.com/health",
                TimeoutSeconds = 5
            },
            CancellationToken.None);

        Assert.Equal(PollResultKind.NetworkError, result.Kind);
        Assert.Contains("No such host", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PollAsync_WithNonSuccessStatusCode_ReturnsHttpErrorResult()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("temporarily unavailable")
            }));

        var poller = CreatePoller(handler);

        var result = await poller.PollAsync(
            new EndpointConfig
            {
                Id = "billing-api",
                Name = "Billing API",
                Url = "https://billing.example.com/health",
                TimeoutSeconds = 5
            },
            CancellationToken.None);

        Assert.Equal(PollResultKind.HttpError, result.Kind);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal("temporarily unavailable", result.ResponseBody);
        Assert.Contains("503", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollAsync_WithEmptySuccessfulResponse_ReturnsEmptyResponseResult()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("   ")
            }));

        var poller = CreatePoller(handler);

        var result = await poller.PollAsync(
            new EndpointConfig
            {
                Id = "notifications-api",
                Name = "Notifications API",
                Url = "https://notifications.example.com/health",
                TimeoutSeconds = 5
            },
            CancellationToken.None);

        Assert.Equal(PollResultKind.EmptyResponse, result.Kind);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("empty response body", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static EndpointPoller CreatePoller(
        HttpMessageHandler handler,
        DashboardConfig? dashboardConfig = null)
    {
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new EndpointPoller(
            new StubHttpClientFactory(client),
            dashboardConfig ?? new DashboardConfig(),
            NullLogger<EndpointPoller>.Instance);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
