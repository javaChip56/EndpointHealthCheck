using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly SmtpEmailOptions _options;

    public SmtpEmailSender(
        SmtpEmailOptions options,
        ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject
        };

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mailMessage.Body = message.HtmlBody;
            mailMessage.IsBodyHtml = true;
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody,
                new ContentType(MediaTypeNames.Text.Plain)));
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody,
                new ContentType(MediaTypeNames.Text.Html)));
        }
        else
        {
            mailMessage.Body = message.TextBody;
            mailMessage.IsBodyHtml = false;
        }

        foreach (var recipient in message.To)
        {
            mailMessage.To.Add(recipient);
        }

        foreach (var recipient in message.Cc)
        {
            mailMessage.CC.Add(recipient);
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        _logger.LogInformation(
            "Sending SMTP email notification to {ToCount} recipient(s) and {CcCount} CC recipient(s).",
            mailMessage.To.Count,
            mailMessage.CC.Count);

        await client.SendMailAsync(mailMessage, cancellationToken);
    }
}
