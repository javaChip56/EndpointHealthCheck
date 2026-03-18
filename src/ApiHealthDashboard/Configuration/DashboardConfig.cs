namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfig
{
    public DashboardSettings Dashboard { get; set; } = new();

    public List<string> EndpointFiles { get; set; } = new();

    public List<EndpointConfig> Endpoints { get; set; } = new();

    public DashboardConfig Clone()
    {
        return new DashboardConfig
        {
            Dashboard = Dashboard.Clone(),
            EndpointFiles = [.. EndpointFiles],
            Endpoints = Endpoints.Select(static endpoint => endpoint.Clone()).ToList()
        };
    }

    public void CopyFrom(DashboardConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Dashboard = source.Dashboard.Clone();
        EndpointFiles = [.. source.EndpointFiles];
        Endpoints = source.Endpoints.Select(static endpoint => endpoint.Clone()).ToList();
    }
}

public sealed class DashboardSettings
{
    public int RefreshUiSeconds { get; set; } = 10;

    public int RequestTimeoutSecondsDefault { get; set; } = 10;

    public bool ShowRawPayload { get; set; }

    public DashboardSettings Clone()
    {
        return new DashboardSettings
        {
            RefreshUiSeconds = RefreshUiSeconds,
            RequestTimeoutSecondsDefault = RequestTimeoutSecondsDefault,
            ShowRawPayload = ShowRawPayload
        };
    }
}

public sealed class EndpointConfig
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int FrequencySeconds { get; set; } = 30;

    public int? TimeoutSeconds { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> IncludeChecks { get; set; } = new();

    public List<string> ExcludeChecks { get; set; } = new();

    public EndpointConfig Clone()
    {
        return new EndpointConfig
        {
            Id = Id,
            Name = Name,
            Url = Url,
            Enabled = Enabled,
            FrequencySeconds = FrequencySeconds,
            TimeoutSeconds = TimeoutSeconds,
            Headers = new Dictionary<string, string>(Headers, StringComparer.OrdinalIgnoreCase),
            IncludeChecks = [.. IncludeChecks],
            ExcludeChecks = [.. ExcludeChecks]
        };
    }
}
