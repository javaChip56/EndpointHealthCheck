namespace ApiHealthDashboard.Configuration;

public sealed class RuntimeStateOptions
{
    public const string SectionName = "RuntimeState";

    public bool Enabled { get; set; } = true;

    public string DirectoryPath { get; set; } = "runtime-state/endpoints";

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
}
