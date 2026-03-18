namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfigLoadResult
{
    public required DashboardConfig Config { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
