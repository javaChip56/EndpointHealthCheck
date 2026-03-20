namespace ApiHealthDashboard.Configuration;

public sealed class EmailTemplateOptions
{
    public const string SectionName = "Email:Templates";

    public string DirectoryPath { get; set; } = "Templates/Email";

    public string TextTemplateFileName { get; set; } = "notification.txt";

    public string HtmlTemplateFileName { get; set; } = "notification.html";

    public string ResolveDirectoryPath(string contentRootPath)
    {
        var configuredPath = string.IsNullOrWhiteSpace(DirectoryPath)
            ? "Templates/Email"
            : DirectoryPath.Trim();

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }
}
