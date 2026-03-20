using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.State;

public sealed class FileBackedEndpointStateStore : IEndpointStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly InMemoryEndpointStateStore _innerStore;
    private readonly ConcurrentDictionary<string, object> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cleanupSyncRoot = new();
    private readonly object _initializeSyncRoot = new();
    private readonly ILogger<FileBackedEndpointStateStore> _logger;
    private readonly RuntimeStateOptions _options;
    private readonly string _stateDirectoryPath;
    private DateTimeOffset _nextCleanupUtc = DateTimeOffset.MinValue;
    private HashSet<string> _configuredStateFilePaths = new(StringComparer.OrdinalIgnoreCase);

    public FileBackedEndpointStateStore(
        IEnumerable<EndpointConfig> endpoints,
        string stateDirectoryPath,
        RuntimeStateOptions options,
        ILogger<FileBackedEndpointStateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectoryPath);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
        _stateDirectoryPath = Path.GetFullPath(stateDirectoryPath);
        var endpointList = endpoints
            .Where(static endpoint => endpoint is not null)
            .Select(static endpoint => endpoint.Clone())
            .ToArray();
        _innerStore = new InMemoryEndpointStateStore(endpointList);

        Directory.CreateDirectory(_stateDirectoryPath);
        UpdateConfiguredStateFilePaths(endpointList);
        RestorePersistedStates(endpointList, restoreWhenStateIsInitial: true);
        TryCleanupPersistedFiles(force: true);
    }

    public IReadOnlyCollection<EndpointState> GetAll()
    {
        return _innerStore.GetAll();
    }

    public EndpointState? Get(string endpointId)
    {
        return _innerStore.Get(endpointId);
    }

    public void Upsert(EndpointState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _innerStore.Upsert(state);
        PersistState(state);
        TryCleanupPersistedFiles(force: false);
    }

    public void Initialize(IEnumerable<EndpointConfig> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var endpointList = endpoints
            .Where(static endpoint => endpoint is not null)
            .Select(static endpoint => endpoint.Clone())
            .ToArray();

        lock (_initializeSyncRoot)
        {
            _innerStore.Initialize(endpointList);
            UpdateConfiguredStateFilePaths(endpointList);
            RestorePersistedStates(endpointList, restoreWhenStateIsInitial: true);
            TryCleanupPersistedFiles(force: true);
        }
    }

    private void RestorePersistedStates(
        IEnumerable<EndpointConfig> endpoints,
        bool restoreWhenStateIsInitial)
    {
        foreach (var endpoint in endpoints)
        {
            if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Id))
            {
                continue;
            }

            if (restoreWhenStateIsInitial)
            {
                var currentState = _innerStore.Get(endpoint.Id);
                if (currentState is not null && !IsInitialState(currentState))
                {
                    continue;
                }
            }

            var filePath = GetStateFilePath(endpoint.Id);
            if (!File.Exists(filePath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                var persistedState = JsonSerializer.Deserialize<PersistedEndpointState>(stream, JsonOptions);
                if (persistedState is null)
                {
                    continue;
                }

                var restoredState = persistedState.ToRuntimeState();
                restoredState.EndpointId = endpoint.Id;
                restoredState.EndpointName = endpoint.Name;
                restoredState.IsPolling = false;

                _innerStore.Upsert(restoredState);

                _logger.LogInformation(
                    "Restored persisted runtime state for endpoint {EndpointId} from {StateFilePath}.",
                    endpoint.Id,
                    filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to restore persisted runtime state for endpoint {EndpointId} from {StateFilePath}. The endpoint will start with a fresh in-memory state.",
                    endpoint.Id,
                    filePath);
            }
        }
    }

    private void PersistState(EndpointState state)
    {
        var endpointId = state.EndpointId;
        var stateFilePath = GetStateFilePath(endpointId);
        var tempFilePath = $"{stateFilePath}.tmp";
        var fileLock = _fileLocks.GetOrAdd(endpointId, static _ => new object());
        var persistedState = PersistedEndpointState.FromRuntimeState(state);

        lock (fileLock)
        {
            Directory.CreateDirectory(_stateDirectoryPath);

            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, persistedState, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(stateFilePath))
                {
                    File.Replace(tempFilePath, stateFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempFilePath, stateFilePath);
                }
            }
            catch
            {
                TryDeleteTempFile(tempFilePath);
                throw;
            }
        }
    }

    private void UpdateConfiguredStateFilePaths(IEnumerable<EndpointConfig> endpoints)
    {
        var configuredPaths = endpoints
            .Where(static endpoint => endpoint is not null && !string.IsNullOrWhiteSpace(endpoint.Id))
            .Select(endpoint => Path.GetFullPath(GetStateFilePath(endpoint.Id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_cleanupSyncRoot)
        {
            _configuredStateFilePaths = configuredPaths;
        }
    }

    private void TryCleanupPersistedFiles(bool force)
    {
        if (!_options.CleanupEnabled || !Directory.Exists(_stateDirectoryPath))
        {
            return;
        }

        HashSet<string> configuredPaths;
        var now = DateTimeOffset.UtcNow;
        var cleanupInterval = _options.GetCleanupInterval();

        lock (_cleanupSyncRoot)
        {
            if (!force &&
                cleanupInterval > TimeSpan.Zero &&
                now < _nextCleanupUtc)
            {
                return;
            }

            _nextCleanupUtc = cleanupInterval > TimeSpan.Zero
                ? now.Add(cleanupInterval)
                : now;

            configuredPaths = new HashSet<string>(_configuredStateFilePaths, StringComparer.OrdinalIgnoreCase);
        }

        CleanupOrphanedStateFiles(configuredPaths, now);
    }

    private void CleanupOrphanedStateFiles(
        HashSet<string> configuredPaths,
        DateTimeOffset now)
    {
        if (!_options.DeleteOrphanedStateFiles)
        {
            return;
        }

        var retention = _options.GetOrphanedStateFileRetention();
        var cutoffUtc = now.UtcDateTime - retention;

        foreach (var stateFilePath in Directory.EnumerateFiles(_stateDirectoryPath, "*.state.json", SearchOption.TopDirectoryOnly))
        {
            var fullStateFilePath = Path.GetFullPath(stateFilePath);
            if (configuredPaths.Contains(fullStateFilePath))
            {
                continue;
            }

            DateTime lastWriteUtc;

            try
            {
                lastWriteUtc = File.GetLastWriteTimeUtc(fullStateFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to inspect orphaned runtime state file {StateFilePath} during cleanup.",
                    fullStateFilePath);
                continue;
            }

            if (retention > TimeSpan.Zero && lastWriteUtc > cutoffUtc)
            {
                continue;
            }

            try
            {
                File.Delete(fullStateFilePath);

                _logger.LogInformation(
                    "Deleted orphaned runtime state file {StateFilePath} during cleanup.",
                    fullStateFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete orphaned runtime state file {StateFilePath} during cleanup.",
                    fullStateFilePath);
            }
        }
    }

    private string GetStateFilePath(string endpointId)
    {
        return Path.Combine(_stateDirectoryPath, $"{CreateSafeFileStem(endpointId)}.state.json");
    }

    private static string CreateSafeFileStem(string endpointId)
    {
        var sanitizedCharacters = endpointId
            .Select(static character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-')
            .ToArray();

        var sanitizedId = new string(sanitizedCharacters).Trim('-');
        if (string.IsNullOrWhiteSpace(sanitizedId))
        {
            sanitizedId = "endpoint";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(endpointId));
        var hash = Convert.ToHexString(hashBytes.AsSpan(0, 6)).ToLowerInvariant();

        return $"{sanitizedId}-{hash}";
    }

    private static bool IsInitialState(EndpointState state)
    {
        return state.Status == "Unknown" &&
               state.LastCheckedUtc is null &&
               state.LastSuccessfulUtc is null &&
               state.DurationMs is null &&
               string.IsNullOrWhiteSpace(state.LastError) &&
               state.Snapshot is null &&
               !state.IsPolling;
    }

    private static void TryDeleteTempFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
        }
    }

    private sealed class PersistedEndpointState
    {
        public string EndpointId { get; set; } = string.Empty;

        public string EndpointName { get; set; } = string.Empty;

        public string Status { get; set; } = "Unknown";

        public DateTimeOffset? LastCheckedUtc { get; set; }

        public DateTimeOffset? LastSuccessfulUtc { get; set; }

        public long? DurationMs { get; set; }

        public string? LastError { get; set; }

        public HealthSnapshot? Snapshot { get; set; }

        public List<RecentPollSample> RecentSamples { get; set; } = new();

        public List<EndpointNotificationDispatch> NotificationDispatches { get; set; } = new();

        public static PersistedEndpointState FromRuntimeState(EndpointState state)
        {
            return new PersistedEndpointState
            {
                EndpointId = state.EndpointId,
                EndpointName = state.EndpointName,
                Status = state.Status,
                LastCheckedUtc = state.LastCheckedUtc,
                LastSuccessfulUtc = state.LastSuccessfulUtc,
                DurationMs = state.DurationMs,
                LastError = state.LastError,
                Snapshot = state.Snapshot?.Clone(),
                RecentSamples = state.RecentSamples.Select(static sample => sample.Clone()).ToList(),
                NotificationDispatches = state.NotificationDispatches.Select(static dispatch => dispatch.Clone()).ToList()
            };
        }

        public EndpointState ToRuntimeState()
        {
            return new EndpointState
            {
                EndpointId = EndpointId,
                EndpointName = EndpointName,
                Status = Status,
                LastCheckedUtc = LastCheckedUtc,
                LastSuccessfulUtc = LastSuccessfulUtc,
                DurationMs = DurationMs,
                LastError = LastError,
                Snapshot = Snapshot?.Clone(),
                RecentSamples = RecentSamples.Select(static sample => sample.Clone()).ToList(),
                NotificationDispatches = NotificationDispatches.Select(static dispatch => dispatch.Clone()).ToList(),
                IsPolling = false
            };
        }
    }
}
