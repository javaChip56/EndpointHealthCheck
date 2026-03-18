namespace ApiHealthDashboard.Domain;

public sealed class RecentPollSample
{
    public DateTimeOffset CheckedUtc { get; set; }

    public string Status { get; set; } = "Unknown";

    public long DurationMs { get; set; }

    public string ResultKind { get; set; } = string.Empty;

    public string? ErrorSummary { get; set; }

    public RecentPollSample Clone()
    {
        return new RecentPollSample
        {
            CheckedUtc = CheckedUtc,
            Status = Status,
            DurationMs = DurationMs,
            ResultKind = ResultKind,
            ErrorSummary = ErrorSummary
        };
    }
}
