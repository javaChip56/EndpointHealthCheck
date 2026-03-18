namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfig
{
    public DashboardSettings Dashboard { get; set; } = new();

    public List<EndpointConfig> Endpoints { get; set; } = new();
}

public sealed class DashboardSettings
{
    public int RefreshUiSeconds { get; set; } = 10;

    public int RequestTimeoutSecondsDefault { get; set; } = 10;

    public bool ShowRawPayload { get; set; }
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
}
