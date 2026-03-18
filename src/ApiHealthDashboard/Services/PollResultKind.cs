namespace ApiHealthDashboard.Services;

public enum PollResultKind
{
    Success,
    Timeout,
    NetworkError,
    HttpError,
    EmptyResponse,
    UnknownError
}
