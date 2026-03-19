using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.Services;
using ApiHealthDashboard.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApiHealthDashboard.Tests.Configuration;

public sealed class DashboardConfigHotReloadServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public DashboardConfigHotReloadServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiHealthDashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ReloadNowAsync_WhenYamlChanges_UpdatesSharedConfigAndStateStore()
    {
        var dashboardPath = WriteNamedConfig(
            "dashboard.yaml",
            """
            dashboard:
              refreshUiSeconds: 10
            endpointFiles:
              - endpoints/orders-api.yaml
            """);

        WriteNamedConfig(
            "endpoints/orders-api.yaml",
            """
            id: orders-api
            name: Orders API
            url: https://orders.example.com/health
            enabled: true
            frequencySeconds: 30
            """);

        var loader = new YamlConfigLoader(new DashboardConfigValidator());
        var initialLoadResult = loader.Load(dashboardPath);
        var sharedConfig = initialLoadResult.Config.Clone();
        var warningState = new ConfigurationWarningState(initialLoadResult.Warnings);
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
            new StubEndpointPoller(),
            new NoOpEndpointNotificationService(),
            new StubHealthResponseParser(),
            new RuntimeStateOptions(),
            TimeProvider.System,
            NullLogger<PollingSchedulerService>.Instance);

        var service = new DashboardConfigHotReloadService(
            sharedConfig,
            initialLoadResult,
            warningState,
            scheduler,
            loader,
            Options.Create(new DashboardBootstrapOptions
            {
                DashboardConfigPath = dashboardPath
            }),
            new TestHostEnvironment
            {
                ContentRootPath = _tempDirectory
            },
            NullLogger<DashboardConfigHotReloadService>.Instance);

        try
        {
            WriteNamedConfig(
                "dashboard.yaml",
                """
                dashboard:
                  refreshUiSeconds: 25
                endpointFiles:
                  - endpoints/orders-api.yaml
                  - endpoints/billing-api.yaml
                """);

            WriteNamedConfig(
                "endpoints/billing-api.yaml",
                """
                id: billing-api
                name: Billing API
                url: https://billing.example.com/health
                enabled: true
                frequencySeconds: 60
                """);

            await service.ReloadNowAsync();

            Assert.Equal(25, sharedConfig.Dashboard.RefreshUiSeconds);
            Assert.Equal(2, sharedConfig.Endpoints.Count);
            Assert.Contains(sharedConfig.Endpoints, static endpoint => endpoint.Id == "billing-api");
            Assert.False(warningState.HasWarnings);

            var preservedOrdersState = stateStore.Get("orders-api");
            Assert.NotNull(preservedOrdersState);
            Assert.Equal("Healthy", preservedOrdersState!.Status);

            var newBillingState = stateStore.Get("billing-api");
            Assert.NotNull(newBillingState);
            Assert.Equal("Unknown", newBillingState!.Status);
        }
        finally
        {
            service.Dispose();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteNamedConfig(string relativePath, string content)
    {
        var path = Path.Combine(_tempDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "ApiHealthDashboard.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class StubEndpointPoller : IEndpointPoller
    {
        public Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PollResult
            {
                Kind = PollResultKind.Success,
                CheckedUtc = DateTimeOffset.UtcNow,
                DurationMs = 1,
                ResponseBody = """{"status":"Healthy"}"""
            });
        }
    }

    private sealed class StubHealthResponseParser : IHealthResponseParser
    {
        public HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs)
        {
            return new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                RawPayload = json
            };
        }
    }

    private sealed class NoOpEndpointNotificationService : IEndpointNotificationService
    {
        public Task NotifyAsync(EndpointConfig endpoint, EndpointState? previousState, EndpointState currentState, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
