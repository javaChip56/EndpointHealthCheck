namespace ApiHealthDashboard.Services;

public sealed class EndpointImportException : Exception
{
    public EndpointImportException(IEnumerable<string> errors)
        : base("The endpoint import request is invalid.")
    {
        Errors = errors
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Select(static error => error.Trim())
            .ToArray();
    }

    public IReadOnlyList<string> Errors { get; }
}
