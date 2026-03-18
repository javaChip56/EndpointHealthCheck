using System.Net;

namespace ApiHealthDashboard.Services;

public sealed class PollResult
{
    public PollResultKind Kind { get; init; }

    public DateTimeOffset CheckedUtc { get; init; }

    public long DurationMs { get; init; }

    public HttpStatusCode? StatusCode { get; init; }

    public string? ResponseBody { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsSuccess => Kind == PollResultKind.Success;
}
