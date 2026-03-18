namespace ApiHealthDashboard.Configuration;

public interface IYamlConfigLoader
{
    DashboardConfigLoadResult Load(string path);
}
