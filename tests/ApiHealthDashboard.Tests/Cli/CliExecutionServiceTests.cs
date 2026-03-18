using ApiHealthDashboard.Cli;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.Tests.Logging;

namespace ApiHealthDashboard.Tests.Cli;

public sealed class CliExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithSuiteMode_ProducesSummaryAndSnapshot()
    {
        var poller = new StubEndpointPoller(
            new Dictionary<string, PollResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["orders-api"] = new()
                {
                    Kind = PollResultKind.Success,
                    CheckedUtc = DateTimeOffset.Parse("2026-03-18T00:00:00Z"),
                    DurationMs = 42,
                    StatusCode = System.Net.HttpStatusCode.OK,
                    ResponseBody = """{"status":"Healthy"}"""
                },
                ["billing-api"] = new()
                {
                    Kind = PollResultKind.HttpError,
                    CheckedUtc = DateTimeOffset.Parse("2026-03-18T00:01:00Z"),
                    DurationMs = 15,
                    StatusCode = System.Net.HttpStatusCode.NotFound,
                    ResponseBody = "not found",
                    ErrorMessage = "Endpoint returned HTTP 404 (NotFound)."
                }
            });
        var parser = new StubHealthResponseParser(
            new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.Parse("2026-03-18T00:00:00Z"),
                DurationMs = 42,
                RawPayload = """{"status":"Healthy"}""",
                Metadata = new Dictionary<string, object?>
                {
                    ["region"] = "apac"
                },
                Nodes =
                [
                    new HealthNode
                    {
                        Name = "self",
                        Status = "Healthy"
                    }
                ]
            });
        var logger = new TestLogger<CliExecutionService>();
        var service = new CliExecutionService(poller, parser, TimeProvider.System, logger);
        var config = new DashboardConfig
        {
            Dashboard = new DashboardSettings
            {
                RequestTimeoutSecondsDefault = 10
            },
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    Priority = EndpointPriority.Critical,
                    Enabled = true,
                    FrequencySeconds = 30
                },
                new EndpointConfig
                {
                    Id = "billing-api",
                    Name = "Billing API",
                    Url = "https://billing.example.com/health",
                    Priority = EndpointPriority.Low,
                    Enabled = true,
                    FrequencySeconds = 60
                }
            ]
        };
        var options = new CliOptions
        {
            IsCliMode = true,
            RunAll = true
        };

        var report = await service.ExecuteAsync(
            config,
            options,
            "dashboard.yaml",
            ["missing endpoint file warning"],
            CancellationToken.None);

        Assert.Equal("suite", report.Mode);
        Assert.Equal("dashboard.yaml", report.DashboardConfigPath);
        Assert.Single(report.ConfigurationWarnings);
        Assert.Equal(2, report.Summary.TotalEndpoints);
        Assert.Equal(2, report.Summary.ExecutedEndpoints);
        Assert.Equal(1, report.Summary.SuccessfulPolls);
        Assert.Equal(1, report.Summary.FailedPolls);
        Assert.Equal("Healthy", report.Summary.HealthyEndpoints == 1 ? "Healthy" : "Unknown");
        Assert.Equal("Healthy", report.Summary.OverallStatus);

        var successEndpoint = Assert.Single(report.Endpoints.Where(static endpoint => endpoint.Id == "orders-api"));
        Assert.Equal("Executed", successEndpoint.ExecutionState);
        Assert.Equal(EndpointPriority.Critical, successEndpoint.Priority);
        Assert.Equal("Healthy", successEndpoint.Status);
        Assert.NotNull(successEndpoint.Snapshot);
        Assert.Single(successEndpoint.Snapshot!.Nodes);
        Assert.Single(successEndpoint.Snapshot.MetadataEntries);

        var failedEndpoint = Assert.Single(report.Endpoints.Where(static endpoint => endpoint.Id == "billing-api"));
        Assert.Equal("Executed", failedEndpoint.ExecutionState);
        Assert.Equal("HttpError", failedEndpoint.PollResultKind);
        Assert.Equal("Unknown", failedEndpoint.Status);
        Assert.Null(failedEndpoint.Snapshot);
    }

    [Fact]
    public async Task ExecuteAsync_WithDisabledEndpoint_SkipsExecution()
    {
        var poller = new StubEndpointPoller(new Dictionary<string, PollResult>(StringComparer.OrdinalIgnoreCase));
        var parser = new StubHealthResponseParser(new HealthSnapshot());
        var logger = new TestLogger<CliExecutionService>();
        var service = new CliExecutionService(poller, parser, TimeProvider.System, logger);
        var config = new DashboardConfig
        {
            Dashboard = new DashboardSettings(),
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "disabled-api",
                    Name = "Disabled API",
                    Url = "https://disabled.example.com/health",
                    Priority = EndpointPriority.High,
                    Enabled = false,
                    FrequencySeconds = 30
                }
            ]
        };
        var options = new CliOptions
        {
            IsCliMode = true,
            EndpointFiles = ["endpoints/disabled-api.yaml"]
        };

        var report = await service.ExecuteAsync(
            config,
            options,
            "dashboard.yaml",
            [],
            CancellationToken.None);

        var endpoint = Assert.Single(report.Endpoints);
        Assert.Equal("selected-endpoints", report.Mode);
        Assert.Equal(EndpointPriority.High, endpoint.Priority);
        Assert.Equal("Skipped", endpoint.ExecutionState);
        Assert.Equal("Skipped", endpoint.PollResultKind);
        Assert.Equal("Endpoint is disabled.", endpoint.ErrorMessage);
        Assert.Equal(1, report.Summary.SkippedEndpoints);
        Assert.Empty(poller.PolledEndpointIds);
    }

    private sealed class StubEndpointPoller : IEndpointPoller
    {
        private readonly IReadOnlyDictionary<string, PollResult> _results;

        public StubEndpointPoller(IReadOnlyDictionary<string, PollResult> results)
        {
            _results = results;
        }

        public List<string> PolledEndpointIds { get; } = new();

        public Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
        {
            PolledEndpointIds.Add(endpoint.Id);
            return Task.FromResult(_results[endpoint.Id]);
        }
    }

    private sealed class StubHealthResponseParser : IHealthResponseParser
    {
        private readonly HealthSnapshot _snapshot;

        public StubHealthResponseParser(HealthSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task? CallCountTask { get; private set; }

        public HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs)
        {
            return _snapshot.Clone();
        }
    }
}
