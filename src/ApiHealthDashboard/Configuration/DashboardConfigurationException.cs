namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfigurationException : Exception
{
    public DashboardConfigurationException(string configPath, IReadOnlyCollection<string> errors)
        : base(CreateMessage(configPath, errors))
    {
        ConfigPath = configPath;
        Errors = errors;
    }

    public DashboardConfigurationException(
        string configPath,
        IReadOnlyCollection<string> errors,
        Exception innerException)
        : base(CreateMessage(configPath, errors), innerException)
    {
        ConfigPath = configPath;
        Errors = errors;
    }

    public string ConfigPath { get; }

    public IReadOnlyCollection<string> Errors { get; }

    private static string CreateMessage(string configPath, IReadOnlyCollection<string> errors)
    {
        if (errors.Count == 0)
        {
            return $"Dashboard configuration '{configPath}' is invalid.";
        }

        return $"Dashboard configuration '{configPath}' is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}";
    }
}
