using System.Diagnostics;
using System.Net;
using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Services;

public sealed class EndpointPoller : IEndpointPoller
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EndpointPoller> _logger;

    public EndpointPoller(
        IHttpClientFactory httpClientFactory,
        DashboardConfig dashboardConfig,
        ILogger<EndpointPoller> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dashboardConfig = dashboardConfig;
        _logger = logger;
    }

    public async Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Url);

        var checkedUtc = DateTimeOffset.UtcNow;
        var timeoutSeconds = endpoint.TimeoutSeconds ?? _dashboardConfig.Dashboard.RequestTimeoutSecondsDefault;
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);

        foreach (var header in endpoint.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(EndpointPoller));
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Endpoint poll returned HTTP {StatusCode} for endpoint {EndpointId}.",
                    (int)response.StatusCode,
                    endpoint.Id);

                return new PollResult
                {
                    Kind = PollResultKind.HttpError,
                    CheckedUtc = checkedUtc,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    StatusCode = response.StatusCode,
                    ResponseBody = responseBody,
                    ErrorMessage = $"Endpoint returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
                };
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                _logger.LogWarning(
                    "Endpoint poll returned an empty response body for endpoint {EndpointId}.",
                    endpoint.Id);

                return new PollResult
                {
                    Kind = PollResultKind.EmptyResponse,
                    CheckedUtc = checkedUtc,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    StatusCode = response.StatusCode,
                    ResponseBody = responseBody,
                    ErrorMessage = "Endpoint returned an empty response body."
                };
            }

            return new PollResult
            {
                Kind = PollResultKind.Success,
                CheckedUtc = checkedUtc,
                DurationMs = stopwatch.ElapsedMilliseconds,
                StatusCode = response.StatusCode,
                ResponseBody = responseBody
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                "Endpoint poll timed out after {TimeoutSeconds} seconds for endpoint {EndpointId}.",
                timeoutSeconds,
                endpoint.Id);

            return new PollResult
            {
                Kind = PollResultKind.Timeout,
                CheckedUtc = checkedUtc,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = $"Endpoint request timed out after {timeoutSeconds} seconds."
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                ex,
                "Endpoint poll failed with a network error for endpoint {EndpointId}.",
                endpoint.Id);

            return new PollResult
            {
                Kind = PollResultKind.NetworkError,
                CheckedUtc = checkedUtc,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Endpoint poll failed with an unexpected error for endpoint {EndpointId}.",
                endpoint.Id);

            return new PollResult
            {
                Kind = PollResultKind.UnknownError,
                CheckedUtc = checkedUtc,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}
