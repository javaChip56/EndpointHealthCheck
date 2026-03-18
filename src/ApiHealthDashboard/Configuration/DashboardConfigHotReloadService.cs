using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ApiHealthDashboard.Scheduling;

namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfigHotReloadService : IHostedService, IDisposable
{
    private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(750);

    private readonly DashboardBootstrapOptions _bootstrapOptions;
    private readonly DashboardConfig _dashboardConfig;
    private readonly ConfigurationWarningState _configurationWarningState;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DashboardConfigHotReloadService> _logger;
    private readonly PollingSchedulerService _pollingSchedulerService;
    private readonly IYamlConfigLoader _yamlConfigLoader;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<string, byte> _watchedFilePaths = new(StringComparer.OrdinalIgnoreCase);

    private List<FileSystemWatcher> _watchers = new();
    private CancellationTokenSource? _reloadDelayCancellationTokenSource;
    private CancellationToken _stoppingToken;
    private string _resolvedDashboardPath = string.Empty;

    public DashboardConfigHotReloadService(
        DashboardConfig dashboardConfig,
        DashboardConfigLoadResult initialLoadResult,
        ConfigurationWarningState configurationWarningState,
        PollingSchedulerService pollingSchedulerService,
        IYamlConfigLoader yamlConfigLoader,
        IOptions<DashboardBootstrapOptions> bootstrapOptions,
        IHostEnvironment environment,
        ILogger<DashboardConfigHotReloadService> logger)
    {
        _dashboardConfig = dashboardConfig;
        _configurationWarningState = configurationWarningState;
        _pollingSchedulerService = pollingSchedulerService;
        _yamlConfigLoader = yamlConfigLoader;
        _bootstrapOptions = bootstrapOptions.Value;
        _environment = environment;
        _logger = logger;
        _resolvedDashboardPath = ResolveConfigPath(_bootstrapOptions.ResolveDashboardConfigPath(), _environment.ContentRootPath);

        foreach (var path in initialLoadResult.WatchedFilePaths)
        {
            _watchedFilePaths.TryAdd(Path.GetFullPath(path), 0);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingToken = cancellationToken;
        ResetWatchers();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _reloadDelayCancellationTokenSource?.Cancel();
            _reloadDelayCancellationTokenSource?.Dispose();
            _reloadDelayCancellationTokenSource = null;

            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        _reloadGate.Dispose();
    }

    private void ResetWatchers()
    {
        lock (_syncRoot)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers = BuildWatchers();
        }
    }

    private List<FileSystemWatcher> BuildWatchers()
    {
        var directories = _watchedFilePaths.Keys
            .Append(_resolvedDashboardPath)
            .Select(static path => Path.GetDirectoryName(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var watchers = new List<FileSystemWatcher>(directories.Length);
        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory!);

            var watcher = new FileSystemWatcher(directory!)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
            };

            watcher.Changed += OnWatchedFileChanged;
            watcher.Created += OnWatchedFileChanged;
            watcher.Deleted += OnWatchedFileChanged;
            watcher.Renamed += OnWatchedFileRenamed;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        _logger.LogInformation(
            "Started YAML hot-reload watchers for {DirectoryCount} configuration directory/directories.",
            watchers.Count);

        return watchers;
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldReloadForPath(e.FullPath))
        {
            return;
        }

        ScheduleReload(e.FullPath, e.ChangeType.ToString());
    }

    private void OnWatchedFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!ShouldReloadForPath(e.OldFullPath) && !ShouldReloadForPath(e.FullPath))
        {
            return;
        }

        ScheduleReload(e.FullPath, $"Renamed from {e.OldName}");
    }

    private bool ShouldReloadForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        return _watchedFilePaths.ContainsKey(fullPath) ||
               string.Equals(fullPath, _resolvedDashboardPath, StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleReload(string path, string reason)
    {
        _logger.LogInformation(
            "Detected YAML configuration change for {ChangedPath} ({Reason}). Scheduling reload.",
            path,
            reason);

        CancellationTokenSource delayCts;

        lock (_syncRoot)
        {
            _reloadDelayCancellationTokenSource?.Cancel();
            _reloadDelayCancellationTokenSource?.Dispose();
            _reloadDelayCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
            delayCts = _reloadDelayCancellationTokenSource;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ReloadDebounce, delayCts.Token);
                await ReloadAsync(delayCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }, delayCts.Token);
    }

    public Task ReloadNowAsync(CancellationToken cancellationToken = default)
    {
        return ReloadAsync(cancellationToken);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            _resolvedDashboardPath = ResolveConfigPath(_bootstrapOptions.ResolveDashboardConfigPath(), _environment.ContentRootPath);
            var loadResult = _yamlConfigLoader.Load(_resolvedDashboardPath);

            _dashboardConfig.CopyFrom(loadResult.Config);
            _configurationWarningState.UpdateWarnings(loadResult.Warnings);

            _watchedFilePaths.Clear();
            foreach (var path in loadResult.WatchedFilePaths)
            {
                _watchedFilePaths.TryAdd(Path.GetFullPath(path), 0);
            }

            ResetWatchers();
            await _pollingSchedulerService.ReloadConfigurationAsync(cancellationToken);

            foreach (var warning in loadResult.Warnings)
            {
                _logger.LogWarning("{ConfigurationWarning}", warning);
            }

            _logger.LogInformation(
                "YAML hot-reload applied successfully with {EndpointCount} configured endpoints.",
                _dashboardConfig.Endpoints.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var warnings = new[]
            {
                $"Configuration reload failed. The last successfully loaded configuration is still active. {ex.Message}"
            };
            _configurationWarningState.UpdateWarnings(warnings);

            _logger.LogError(
                ex,
                "YAML hot-reload failed. The last successfully loaded configuration remains active.");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static string ResolveConfigPath(string configuredPath, string contentRootPath)
    {
        var configPath = string.IsNullOrWhiteSpace(configuredPath)
            ? "dashboard.yaml"
            : configuredPath;

        return Path.IsPathRooted(configPath)
            ? Path.GetFullPath(configPath)
            : Path.GetFullPath(Path.Combine(contentRootPath, configPath));
    }
}
