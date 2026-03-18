using System.Text.Json;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.State;
using ApiHealthDashboard.Tests.Logging;

namespace ApiHealthDashboard.Tests.State;

public sealed class FileBackedEndpointStateStoreTests : IDisposable
{
    private readonly string _stateDirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "ApiHealthDashboard.Tests",
        nameof(FileBackedEndpointStateStoreTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Upsert_PersistsCurrentStateToPerEndpointFile()
    {
        var logger = new TestLogger<FileBackedEndpointStateStore>();
        var store = new FileBackedEndpointStateStore(
            [CreateEndpoint("orders-api", "Orders API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(),
            logger);

        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-18T15:00:00Z"),
            LastSuccessfulUtc = DateTimeOffset.Parse("2026-03-18T15:00:00Z"),
            DurationMs = 123,
            LastError = null,
            IsPolling = true,
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-18T14:59:00Z"),
                    Status = "Healthy",
                    DurationMs = 120,
                    ResultKind = "Success"
                }
            ],
            Snapshot = new HealthSnapshot
            {
                OverallStatus = "Healthy",
                RetrievedUtc = DateTimeOffset.Parse("2026-03-18T15:00:00Z"),
                DurationMs = 123,
                RawPayload = """{"status":"Healthy"}""",
                Metadata = new Dictionary<string, object?>
                {
                    ["region"] = "apac"
                },
                Nodes =
                [
                    new HealthNode
                    {
                        Name = "database",
                        Status = "Healthy",
                        Data = new Dictionary<string, object?>
                        {
                            ["provider"] = "sql"
                        }
                    }
                ]
            }
        });

        var stateFiles = Directory.GetFiles(_stateDirectoryPath, "*.state.json");

        var stateFile = Assert.Single(stateFiles);
        var json = File.ReadAllText(stateFile);

        Assert.Contains("\"status\":\"Healthy\"", json);
        Assert.Contains("\"endpointId\":\"orders-api\"", json);
        Assert.Contains("\"recentSamples\":[", json);
        Assert.DoesNotContain("isPolling", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RestoresPersistedStateForConfiguredEndpoint()
    {
        var initialStore = new FileBackedEndpointStateStore(
            [CreateEndpoint("orders-api", "Orders API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(),
            new TestLogger<FileBackedEndpointStateStore>());

        initialStore.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Degraded",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-18T16:00:00Z"),
            LastSuccessfulUtc = DateTimeOffset.Parse("2026-03-18T15:59:00Z"),
            DurationMs = 456,
            LastError = "Slow dependency",
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-18T15:58:00Z"),
                    Status = "Healthy",
                    DurationMs = 430,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-18T16:00:00Z"),
                    Status = "Degraded",
                    DurationMs = 456,
                    ResultKind = "Success",
                    ErrorSummary = "Slow dependency"
                }
            ],
            Snapshot = new HealthSnapshot
            {
                OverallStatus = "Degraded",
                RetrievedUtc = DateTimeOffset.Parse("2026-03-18T16:00:00Z"),
                DurationMs = 456,
                RawPayload = """{"status":"Degraded"}"""
            }
        });

        var restoredStore = new FileBackedEndpointStateStore(
            [CreateEndpoint("orders-api", "Orders API Reloaded")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(),
            new TestLogger<FileBackedEndpointStateStore>());

        var restoredState = restoredStore.Get("orders-api");

        Assert.NotNull(restoredState);
        Assert.Equal("Degraded", restoredState!.Status);
        Assert.Equal("Orders API Reloaded", restoredState.EndpointName);
        Assert.Equal("Slow dependency", restoredState.LastError);
        Assert.NotNull(restoredState.Snapshot);
        Assert.False(restoredState.IsPolling);
        Assert.Equal(2, restoredState.RecentSamples.Count);
        Assert.Equal("Degraded", restoredState.RecentSamples[^1].Status);
    }

    [Fact]
    public void Initialize_RestoresPersistedStateOnlyForNewEndpoints()
    {
        var seedStore = new FileBackedEndpointStateStore(
            [CreateEndpoint("billing-api", "Billing API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(),
            new TestLogger<FileBackedEndpointStateStore>());

        seedStore.Upsert(new EndpointState
        {
            EndpointId = "billing-api",
            EndpointName = "Billing API",
            Status = "Healthy",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-18T17:00:00Z")
        });

        var logger = new TestLogger<FileBackedEndpointStateStore>();
        var store = new FileBackedEndpointStateStore(
            [CreateEndpoint("orders-api", "Orders API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(),
            logger);

        store.Upsert(new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Degraded"
        });

        var ordersStateFile = Directory.GetFiles(_stateDirectoryPath, "*.state.json")
            .Single(path => Path.GetFileName(path).Contains("orders-api", StringComparison.OrdinalIgnoreCase));

        OverwritePersistedState(
            ordersStateFile,
            "orders-api",
            "Orders API",
            "Unhealthy");

        store.Initialize(
            [
                CreateEndpoint("orders-api", "Orders API Reloaded"),
                CreateEndpoint("billing-api", "Billing API Reloaded")
            ]);

        var ordersState = store.Get("orders-api");
        var billingState = store.Get("billing-api");

        Assert.NotNull(ordersState);
        Assert.NotNull(billingState);
        Assert.Equal("Degraded", ordersState!.Status);
        Assert.Equal("Orders API Reloaded", ordersState.EndpointName);
        Assert.Equal("Healthy", billingState!.Status);
        Assert.Equal("Billing API Reloaded", billingState.EndpointName);
    }

    [Fact]
    public void Initialize_DeletesOrphanedStateFilesWhenRetentionHasExpired()
    {
        var seedStore = new FileBackedEndpointStateStore(
            [CreateEndpoint("billing-api", "Billing API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(cleanupIntervalMinutes: 0, orphanedRetentionHours: 5),
            new TestLogger<FileBackedEndpointStateStore>());

        seedStore.Upsert(new EndpointState
        {
            EndpointId = "billing-api",
            EndpointName = "Billing API",
            Status = "Healthy"
        });

        var billingStateFile = Assert.Single(Directory.GetFiles(_stateDirectoryPath, "*.state.json"));
        File.SetLastWriteTimeUtc(billingStateFile, DateTime.UtcNow.AddHours(-6));

        seedStore.Initialize([CreateEndpoint("orders-api", "Orders API")]);

        Assert.Empty(Directory.GetFiles(_stateDirectoryPath, "*.state.json"));
    }

    [Fact]
    public void Initialize_KeepsRecentOrphanedStateFilesUntilRetentionExpires()
    {
        var seedStore = new FileBackedEndpointStateStore(
            [CreateEndpoint("billing-api", "Billing API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(cleanupIntervalMinutes: 0, orphanedRetentionHours: 5),
            new TestLogger<FileBackedEndpointStateStore>());

        seedStore.Upsert(new EndpointState
        {
            EndpointId = "billing-api",
            EndpointName = "Billing API",
            Status = "Healthy"
        });

        var billingStateFile = Assert.Single(Directory.GetFiles(_stateDirectoryPath, "*.state.json"));
        File.SetLastWriteTimeUtc(billingStateFile, DateTime.UtcNow.AddHours(-1));

        seedStore.Initialize([CreateEndpoint("orders-api", "Orders API")]);

        Assert.Single(Directory.GetFiles(_stateDirectoryPath, "*.state.json"));
    }

    [Fact]
    public void Initialize_DoesNotDeleteOrphanedStateFilesWhenCleanupIsDisabled()
    {
        var seedStore = new FileBackedEndpointStateStore(
            [CreateEndpoint("billing-api", "Billing API")],
            _stateDirectoryPath,
            CreateRuntimeStateOptions(cleanupEnabled: true, cleanupIntervalMinutes: 0, deleteOrphanedStateFiles: false, orphanedRetentionHours: 0),
            new TestLogger<FileBackedEndpointStateStore>());

        seedStore.Upsert(new EndpointState
        {
            EndpointId = "billing-api",
            EndpointName = "Billing API",
            Status = "Healthy"
        });

        seedStore.Initialize([CreateEndpoint("orders-api", "Orders API")]);

        Assert.Single(Directory.GetFiles(_stateDirectoryPath, "*.state.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateDirectoryPath))
        {
            Directory.Delete(_stateDirectoryPath, recursive: true);
        }
    }

    private static EndpointConfig CreateEndpoint(string id, string name)
    {
        return new EndpointConfig
        {
            Id = id,
            Name = name,
            Url = $"https://{id}.example.com/health"
        };
    }

    private static RuntimeStateOptions CreateRuntimeStateOptions(
        bool cleanupEnabled = true,
        double cleanupIntervalMinutes = 30,
        bool deleteOrphanedStateFiles = true,
        double orphanedRetentionHours = 5)
    {
        return new RuntimeStateOptions
        {
            Enabled = true,
            DirectoryPath = "runtime-state/endpoints",
            CleanupEnabled = cleanupEnabled,
            CleanupIntervalMinutes = cleanupIntervalMinutes,
            DeleteOrphanedStateFiles = deleteOrphanedStateFiles,
            OrphanedStateFileRetentionHours = orphanedRetentionHours,
            RecentSampleLimit = 25
        };
    }

    private static void OverwritePersistedState(
        string stateFilePath,
        string endpointId,
        string endpointName,
        string status)
    {
        var payload = new
        {
            endpointId,
            endpointName,
            status
        };

        File.WriteAllText(stateFilePath, JsonSerializer.Serialize(payload));
    }
}
