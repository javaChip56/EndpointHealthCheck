namespace ApiHealthDashboard.Configuration;

public sealed class ConfigurationWarningState
{
    private readonly object _syncRoot = new();
    private IReadOnlyList<string> _warnings;

    public ConfigurationWarningState(IReadOnlyList<string> warnings)
    {
        _warnings = warnings ?? [];
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_syncRoot)
            {
                return _warnings;
            }
        }
    }

    public bool HasWarnings
    {
        get
        {
            lock (_syncRoot)
            {
                return _warnings.Count > 0;
            }
        }
    }

    public void UpdateWarnings(IReadOnlyList<string> warnings)
    {
        lock (_syncRoot)
        {
            _warnings = warnings ?? [];
        }
    }
}
