namespace ApiHealthDashboard.Configuration;

public interface IYamlConfigLoader
{
    DashboardConfig Load(string path);
}
