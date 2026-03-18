namespace ApiHealthDashboard.Configuration;

public interface IYamlConfigLoader
{
    DashboardConfigLoadResult Load(string path);

    DashboardConfigLoadResult LoadSelectedEndpoints(string dashboardPath, IEnumerable<string> endpointFilePaths);
}
