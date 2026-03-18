namespace ApiHealthDashboard.Domain;

public sealed class EndpointState
{
    public string EndpointId { get; set; } = string.Empty;

    public string EndpointName { get; set; } = string.Empty;

    public string Status { get; set; } = "Unknown";

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public DateTimeOffset? LastSuccessfulUtc { get; set; }

    public long? DurationMs { get; set; }

    public string? LastError { get; set; }

    public HealthSnapshot? Snapshot { get; set; }

    public bool IsPolling { get; set; }

    public EndpointState Clone()
    {
        return new EndpointState
        {
            EndpointId = EndpointId,
            EndpointName = EndpointName,
            Status = Status,
            LastCheckedUtc = LastCheckedUtc,
            LastSuccessfulUtc = LastSuccessfulUtc,
            DurationMs = DurationMs,
            LastError = LastError,
            Snapshot = Snapshot?.Clone(),
            IsPolling = IsPolling
        };
    }
}
