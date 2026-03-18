namespace ApiHealthDashboard.Configuration;

public sealed class ConfigurationWarningState
{
    public ConfigurationWarningState(IReadOnlyList<string> warnings)
    {
        Warnings = warnings;
    }

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;
}
