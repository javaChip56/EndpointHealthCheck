namespace ApiHealthDashboard.Services;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailMessage
{
    public IReadOnlyList<string> To { get; init; } = [];

    public IReadOnlyList<string> Cc { get; init; } = [];

    public required string Subject { get; init; }

    public required string TextBody { get; init; }

    public string? HtmlBody { get; init; }
}
