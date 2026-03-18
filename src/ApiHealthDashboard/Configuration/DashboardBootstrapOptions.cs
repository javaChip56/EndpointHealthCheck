namespace ApiHealthDashboard.Configuration;

public sealed class DashboardBootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string DashboardConfigPath { get; set; } = "dashboard.yaml";

    public string? EndpointsConfigPath { get; set; }

    public string ResolveDashboardConfigPath()
    {
        return !string.IsNullOrWhiteSpace(DashboardConfigPath)
            ? DashboardConfigPath
            : EndpointsConfigPath ?? "dashboard.yaml";
    }
}
