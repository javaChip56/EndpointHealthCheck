using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace ApiHealthDashboard.Tests.Services;

public sealed class EndpointImportServiceTests
{
    [Fact]
    public async Task ImportAsync_WithSuccessfulProbe_GeneratesYamlAndDiffAgainstExistingEndpoint()
    {
        var config = new DashboardConfig
        {
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    Priority = EndpointPriority.High,
                    Enabled = true,
                    FrequencySeconds = 60,
                    NotificationEmails = ["ops@example.com"],
                    NotificationCc = ["lead@example.com"]
                }
            ]
        };

        var service = new EndpointImportService(
            config,
            new StubEndpointPoller(new PollResult
            {
                Kind = PollResultKind.Success,
                DurationMs = 145,
                ResponseBody = "{\"status\":\"Healthy\"}"
            }),
            new StubHealthResponseParser(new HealthSnapshot
            {
                OverallStatus = "Healthy",
                Nodes =
                [
                    new HealthNode
                    {
                        Name = "database",
                        Status = "Healthy"
                    },
                    new HealthNode
                    {
                        Name = "cache",
                        Status = "Healthy"
                    }
                ]
            }),
            NullLogger<EndpointImportService>.Instance);

        var result = await service.ImportAsync(
            new EndpointImportRequest
            {
                Url = "https://orders.example.com/health",
                FrequencySeconds = 30,
                IncludeDiscoveredChecks = true,
                NotificationCcText = "override@example.com"
            },
            CancellationToken.None);

        Assert.Equal("orders-api", result.SuggestedEndpoint.Id);
        Assert.Equal("Orders API", result.SuggestedEndpoint.Name);
        Assert.Equal(EndpointPriority.High, result.SuggestedEndpoint.Priority);
        Assert.True(result.HasExistingMatch);
        Assert.Contains("priority: 'High'", result.GeneratedYaml, StringComparison.Ordinal);
        Assert.Contains("notificationEmails:", result.GeneratedYaml, StringComparison.Ordinal);
        Assert.Contains("'ops@example.com'", result.GeneratedYaml, StringComparison.Ordinal);
        Assert.Contains("'override@example.com'", result.GeneratedYaml, StringComparison.Ordinal);
        Assert.Contains("includeChecks:", result.GeneratedYaml, StringComparison.Ordinal);
        Assert.Contains("'database'", result.GeneratedYaml, StringComparison.Ordinal);
        Assert.Equal(["cache", "database"], result.TopLevelCheckNames);
        Assert.Contains(result.DiffLines, static line => line.Prefix == "+");
        Assert.Contains("Matched existing endpoint 'orders-api'", result.MatchSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithInvalidHeaderFormat_ThrowsValidationException()
    {
        var service = new EndpointImportService(
            new DashboardConfig(),
            new StubEndpointPoller(new PollResult { Kind = PollResultKind.Success }),
            new StubHealthResponseParser(new HealthSnapshot()),
            NullLogger<EndpointImportService>.Instance);

        var exception = await Assert.ThrowsAsync<EndpointImportException>(() =>
            service.ImportAsync(
                new EndpointImportRequest
                {
                    Url = "https://orders.example.com/health",
                    HeadersText = "Authorization Bearer token"
                },
                CancellationToken.None));

        Assert.Contains("Header line 1 must use the format", exception.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithoutExistingMatch_ReturnsTruncatedResponsePreview()
    {
        var longResponse = new string('x', 15000);
        var service = new EndpointImportService(
            new DashboardConfig(),
            new StubEndpointPoller(new PollResult
            {
                Kind = PollResultKind.Success,
                DurationMs = 50,
                ResponseBody = longResponse
            }),
            new StubHealthResponseParser(new HealthSnapshot()),
            NullLogger<EndpointImportService>.Instance);

        var result = await service.ImportAsync(
            new EndpointImportRequest
            {
                Url = "https://billing.example.com/health",
                FrequencySeconds = 45
            },
            CancellationToken.None);

        Assert.False(result.HasExistingMatch);
        Assert.Equal(EndpointPriority.Normal, result.SuggestedEndpoint.Priority);
        Assert.True(result.ResponsePreviewWasTruncated);
        Assert.Equal(12000, result.ResponsePreview.Length);
        Assert.Empty(result.DiffLines);
    }

    [Fact]
    public async Task ImportAsync_WithJsonResponse_PrettyPrintsResponsePreview()
    {
        var service = new EndpointImportService(
            new DashboardConfig(),
            new StubEndpointPoller(new PollResult
            {
                Kind = PollResultKind.Success,
                DurationMs = 25,
                ResponseBody = "{\"status\":\"Healthy\",\"checks\":{\"db\":{\"status\":\"Healthy\"}}}"
            }),
            new StubHealthResponseParser(new HealthSnapshot
            {
                OverallStatus = "Healthy"
            }),
            NullLogger<EndpointImportService>.Instance);

        var result = await service.ImportAsync(
            new EndpointImportRequest
            {
                Url = "https://orders.example.com/health",
                FrequencySeconds = 180
            },
            CancellationToken.None);

        Assert.Contains("\n", result.ResponsePreview.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\"status\": \"Healthy\"", result.ResponsePreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithNotificationRecipients_AddsThemToSuggestedEndpoint()
    {
        var service = new EndpointImportService(
            new DashboardConfig(),
            new StubEndpointPoller(new PollResult
            {
                Kind = PollResultKind.Success,
                DurationMs = 25,
                ResponseBody = "{\"status\":\"Healthy\"}"
            }),
            new StubHealthResponseParser(new HealthSnapshot
            {
                OverallStatus = "Healthy"
            }),
            NullLogger<EndpointImportService>.Instance);

        var result = await service.ImportAsync(
            new EndpointImportRequest
            {
                Url = "https://orders.example.com/health",
                FrequencySeconds = 180,
                NotificationEmailsText = "ops@example.com; oncall@example.com",
                NotificationCcText = "lead@example.com"
            },
            CancellationToken.None);

        Assert.Equal(["oncall@example.com", "ops@example.com"], result.SuggestedEndpoint.NotificationEmails.OrderBy(static value => value).ToArray());
        Assert.Equal(["lead@example.com"], result.SuggestedEndpoint.NotificationCc);
    }

    [Fact]
    public async Task ImportAsync_WithHttpNotFound_DoesNotGenerateYamlPreview()
    {
        var service = new EndpointImportService(
            new DashboardConfig(),
            new StubEndpointPoller(new PollResult
            {
                Kind = PollResultKind.HttpError,
                StatusCode = HttpStatusCode.NotFound,
                ErrorMessage = "Endpoint returned HTTP 404 (NotFound)."
            }),
            new StubHealthResponseParser(new HealthSnapshot()),
            NullLogger<EndpointImportService>.Instance);

        var result = await service.ImportAsync(
            new EndpointImportRequest
            {
                Url = "https://missing.example.com/health",
                FrequencySeconds = 30
            },
            CancellationToken.None);

        Assert.Equal(PollResultKind.HttpError, result.ProbeResult.Kind);
        Assert.False(result.HasGeneratedYamlPreview);
        Assert.Null(result.GeneratedYaml);
        Assert.Empty(result.DiffLines);
        Assert.False(string.IsNullOrWhiteSpace(result.ProbeStatusText));
    }

    private sealed class StubEndpointPoller : IEndpointPoller
    {
        private readonly PollResult _pollResult;

        public StubEndpointPoller(PollResult pollResult)
        {
            _pollResult = pollResult;
        }

        public Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
        {
            return Task.FromResult(_pollResult);
        }
    }

    private sealed class StubHealthResponseParser : IHealthResponseParser
    {
        private readonly HealthSnapshot _snapshot;

        public StubHealthResponseParser(HealthSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs)
        {
            return _snapshot;
        }
    }
}
