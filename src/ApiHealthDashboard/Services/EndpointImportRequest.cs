namespace ApiHealthDashboard.Services;

public sealed class EndpointImportRequest
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string Url { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int FrequencySeconds { get; init; } = 30;

    public int? TimeoutSeconds { get; init; }

    public string HeadersText { get; init; } = string.Empty;

    public bool IncludeDiscoveredChecks { get; init; }
}
