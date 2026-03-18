namespace ApiHealthDashboard.Configuration;

public sealed class DashboardBootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string EndpointsConfigPath { get; set; } = "endpoints.yaml";
}
