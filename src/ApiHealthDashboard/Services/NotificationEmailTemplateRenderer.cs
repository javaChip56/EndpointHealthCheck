using System.Net;
using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Services;

public interface INotificationEmailTemplateRenderer
{
    NotificationEmailContent Render(NotificationEmailTemplateModel model);
}

public sealed class NotificationEmailTemplateRenderer : INotificationEmailTemplateRenderer
{
    private readonly string _htmlTemplatePath;
    private readonly ILogger<NotificationEmailTemplateRenderer> _logger;
    private readonly string _textTemplatePath;

    public NotificationEmailTemplateRenderer(
        EmailTemplateOptions options,
        IHostEnvironment environment,
        ILogger<NotificationEmailTemplateRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        _logger = logger;

        var templateDirectory = options.ResolveDirectoryPath(environment.ContentRootPath);
        _textTemplatePath = Path.Combine(templateDirectory, options.TextTemplateFileName);
        _htmlTemplatePath = Path.Combine(templateDirectory, options.HtmlTemplateFileName);
    }

    public NotificationEmailContent Render(NotificationEmailTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var textBody = RenderTemplate(_textTemplatePath, BuildTokenMap(model, encodeHtml: false))
            ?? BuildFallbackTextBody(model);
        var htmlBody = RenderTemplate(_htmlTemplatePath, BuildTokenMap(model, encodeHtml: true));

        return new NotificationEmailContent(textBody, htmlBody);
    }

    private string? RenderTemplate(string templatePath, IReadOnlyDictionary<string, string> tokens)
    {
        try
        {
            if (!File.Exists(templatePath))
            {
                _logger.LogWarning(
                    "Notification email template file {TemplatePath} was not found. Falling back to built-in content where available.",
                    templatePath);
                return null;
            }

            var template = File.ReadAllText(templatePath);
            return ApplyTokens(template, tokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to render notification email template {TemplatePath}. Falling back to built-in content where available.",
                templatePath);
            return null;
        }
    }

    private static string ApplyTokens(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var rendered = template;

        foreach (var token in tokens)
        {
            rendered = rendered.Replace($"{{{{{token.Key}}}}}", token.Value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static Dictionary<string, string> BuildTokenMap(NotificationEmailTemplateModel model, bool encodeHtml)
    {
        string Sanitize(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            return encodeHtml ? WebUtility.HtmlEncode(normalized) : normalized;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Subject"] = Sanitize(model.Subject),
            ["EventType"] = Sanitize(model.EventType),
            ["EndpointName"] = Sanitize(model.EndpointName),
            ["EndpointId"] = Sanitize(model.EndpointId),
            ["EndpointUrl"] = Sanitize(model.EndpointUrl),
            ["Priority"] = Sanitize(model.Priority),
            ["CurrentStatus"] = Sanitize(model.CurrentStatus),
            ["CurrentTrend"] = Sanitize(model.CurrentTrend),
            ["PreviousCondition"] = Sanitize(model.PreviousCondition),
            ["ErrorSummary"] = Sanitize(model.ErrorSummary),
            ["CheckedUtc"] = Sanitize(model.CheckedUtcText),
            ["SummaryText"] = Sanitize(model.SummaryText)
        };
    }

    private static string BuildFallbackTextBody(NotificationEmailTemplateModel model)
    {
        var previousCondition = string.IsNullOrWhiteSpace(model.PreviousCondition) ? "-" : model.PreviousCondition;
        var errorSummary = string.IsNullOrWhiteSpace(model.ErrorSummary) ? "-" : model.ErrorSummary;

        return
$""""
Endpoint: {model.EndpointName} ({model.EndpointId})
URL: {model.EndpointUrl}
Priority: {model.Priority}
Event: {model.EventType}
Current status: {model.CurrentStatus}
Current trend: {model.CurrentTrend}
Previous condition: {previousCondition}
Error: {errorSummary}
Checked: {model.CheckedUtcText}

{model.SummaryText}
"""";
    }
}

public sealed record NotificationEmailContent(string TextBody, string? HtmlBody);

public sealed record NotificationEmailTemplateModel(
    string Subject,
    string EventType,
    string EndpointName,
    string EndpointId,
    string EndpointUrl,
    string Priority,
    string CurrentStatus,
    string CurrentTrend,
    string PreviousCondition,
    string ErrorSummary,
    string CheckedUtcText,
    string SummaryText);
