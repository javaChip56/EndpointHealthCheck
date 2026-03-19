namespace ApiHealthDashboard.Configuration;

public sealed class RuntimeStateOptions
{
    public const string SectionName = "RuntimeState";

    public bool Enabled { get; set; } = true;

    public string DirectoryPath { get; set; } = "runtime-state/endpoints";

    public bool CleanupEnabled { get; set; } = true;

    public double CleanupIntervalMinutes { get; set; } = 30;

    public bool DeleteOrphanedStateFiles { get; set; } = true;

    public double OrphanedStateFileRetentionHours { get; set; } = 5;

    public int RecentSampleLimit { get; set; } = 25;

    public int NotificationHistoryLimit { get; set; } = 20;

    public string ResolveDirectoryPath(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var configuredPath = string.IsNullOrWhiteSpace(DirectoryPath)
            ? "runtime-state/endpoints"
            : DirectoryPath;

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    public TimeSpan GetCleanupInterval()
    {
        return CleanupIntervalMinutes <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromMinutes(CleanupIntervalMinutes);
    }

    public TimeSpan GetOrphanedStateFileRetention()
    {
        return OrphanedStateFileRetentionHours <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromHours(OrphanedStateFileRetentionHours);
    }

    public int GetRecentSampleLimit()
    {
        return Math.Max(RecentSampleLimit, 0);
    }

    public int GetNotificationHistoryLimit()
    {
        return Math.Max(NotificationHistoryLimit, 0);
    }
}
