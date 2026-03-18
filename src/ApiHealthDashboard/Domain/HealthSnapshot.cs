namespace ApiHealthDashboard.Domain;

public sealed class HealthSnapshot
{
    public string OverallStatus { get; set; } = "Unknown";

    public DateTimeOffset RetrievedUtc { get; set; }

    public long DurationMs { get; set; }

    public string RawPayload { get; set; } = string.Empty;

    public List<HealthNode> Nodes { get; set; } = new();

    public Dictionary<string, object?> Metadata { get; set; } = new();

    public HealthSnapshot Clone()
    {
        return new HealthSnapshot
        {
            OverallStatus = OverallStatus,
            RetrievedUtc = RetrievedUtc,
            DurationMs = DurationMs,
            RawPayload = RawPayload,
            Nodes = Nodes.Select(static node => node.Clone()).ToList(),
            Metadata = new Dictionary<string, object?>(Metadata)
        };
    }
}
