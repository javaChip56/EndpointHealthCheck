using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Statistics;

namespace ApiHealthDashboard.Services;

public sealed class EndpointEmailNotificationService : IEndpointNotificationService
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly IEmailSender _emailSender;
    private readonly INotificationEmailTemplateRenderer _templateRenderer;
    private readonly ILogger<EndpointEmailNotificationService> _logger;
    private readonly RuntimeStateOptions _runtimeStateOptions;
    private readonly SmtpEmailOptions _smtpOptions;
    private readonly TimeProvider _timeProvider;

    public EndpointEmailNotificationService(
        DashboardConfig dashboardConfig,
        RuntimeStateOptions runtimeStateOptions,
        SmtpEmailOptions smtpOptions,
        IEmailSender emailSender,
        INotificationEmailTemplateRenderer templateRenderer,
        TimeProvider timeProvider,
        ILogger<EndpointEmailNotificationService> logger)
    {
        _dashboardConfig = dashboardConfig;
        _runtimeStateOptions = runtimeStateOptions;
        _smtpOptions = smtpOptions;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task NotifyAsync(
        EndpointConfig endpoint,
        EndpointState? previousState,
        EndpointState currentState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(currentState);

        var notificationSettings = _dashboardConfig.Dashboard.Notifications;
        if (!notificationSettings.Enabled)
        {
            return;
        }

        if (!_smtpOptions.Enabled || !HasSmtpConfiguration())
        {
            _logger.LogDebug(
                "Skipped email notification for endpoint {EndpointId} because SMTP email is not enabled or configured.",
                endpoint.Id);
            return;
        }

        if (EndpointPriority.GetSortOrder(endpoint.Priority) < EndpointPriority.GetSortOrder(notificationSettings.MinimumPriority))
        {
            return;
        }

        var recipients = ResolveRecipients(endpoint, notificationSettings);
        if (recipients.To.Count == 0)
        {
            _logger.LogDebug(
                "Skipped email notification for endpoint {EndpointId} because no notification recipients were configured.",
                endpoint.Id);
            return;
        }

        var previousCondition = DescribeCondition(previousState);
        var currentCondition = DescribeCondition(currentState);
        var hasExistingDispatchRecord = currentState.NotificationDispatches.Count > 0;
        var notification = BuildNotificationDecision(
            notificationSettings,
            previousCondition,
            currentCondition,
            hasExistingDispatchRecord);
        if (notification is null)
        {
            return;
        }

        if (IsWithinCooldown(currentState, notification.Signature, notificationSettings.CooldownMinutes))
        {
            _logger.LogDebug(
                "Skipped email notification for endpoint {EndpointId} because the notification is within the configured cooldown window.",
                endpoint.Id);
            return;
        }

        var subject = BuildSubject(notificationSettings.SubjectPrefix, notification.EventType, endpoint.Name, notification.SubjectLabel);
        var body = BuildBody(endpoint, previousCondition, currentCondition, notification, subject);

        await _emailSender.SendAsync(
            new EmailMessage
            {
                To = recipients.To,
                Cc = recipients.Cc,
                Subject = subject,
                TextBody = body.TextBody,
                HtmlBody = body.HtmlBody
            },
            cancellationToken);

        AppendNotificationDispatch(currentState, notification, recipients);

        _logger.LogInformation(
            "Sent {NotificationEventType} email notification for endpoint {EndpointId} to {ToCount} recipient(s).",
            notification.EventType,
            endpoint.Id,
            recipients.To.Count);
    }

    private bool HasSmtpConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_smtpOptions.Host) &&
               _smtpOptions.Port > 0 &&
               !string.IsNullOrWhiteSpace(_smtpOptions.FromAddress);
    }

    private static NotificationRecipients ResolveRecipients(
        EndpointConfig endpoint,
        DashboardNotificationSettings settings)
    {
        var to = settings.To
            .Concat(endpoint.NotificationEmails)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var cc = settings.Cc
            .Concat(endpoint.NotificationCc)
            .Where(email => !to.Contains(email, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NotificationRecipients(to, cc);
    }

    private NotificationCondition DescribeCondition(EndpointState? state)
    {
        if (state is null)
        {
            return NotificationCondition.Empty;
        }

        var trendAnalysis = RecentPollTrendAnalyzer.Analyze(state.RecentSamples.Select(static sample => sample.Clone()));
        var status = string.IsNullOrWhiteSpace(state.Status) ? "Unknown" : state.Status.Trim();
        var trendLabel = trendAnalysis.TrendKind switch
        {
            RecentPollTrendKind.Failing => "Failing",
            RecentPollTrendKind.Flapping => "Flapping",
            RecentPollTrendKind.Worsening => "Worsening",
            RecentPollTrendKind.Improving => "Improving",
            RecentPollTrendKind.Stable => $"Stable {status}",
            _ => "Awaiting trend"
        };

        if (!string.IsNullOrWhiteSpace(state.LastError) && trendAnalysis.TrendKind == RecentPollTrendKind.Failing)
        {
            return new NotificationCondition(true, "Failing", status, trendLabel, state.LastError, state.LastCheckedUtc);
        }

        if (status is "Unhealthy" or "Degraded")
        {
            return new NotificationCondition(true, status, status, trendLabel, state.LastError, state.LastCheckedUtc);
        }

        if (trendAnalysis.TrendKind is RecentPollTrendKind.Failing or RecentPollTrendKind.Flapping or RecentPollTrendKind.Worsening)
        {
            return new NotificationCondition(true, trendLabel, status, trendLabel, state.LastError, state.LastCheckedUtc);
        }

        if (!string.IsNullOrWhiteSpace(state.LastError))
        {
            return new NotificationCondition(true, "Error", status, trendLabel, state.LastError, state.LastCheckedUtc);
        }

        return new NotificationCondition(false, status, status, trendLabel, null, state.LastCheckedUtc);
    }

    private static NotificationDecision? BuildNotificationDecision(
        DashboardNotificationSettings settings,
        NotificationCondition previousCondition,
        NotificationCondition currentCondition,
        bool hasExistingDispatchRecord)
    {
        if (currentCondition.IsProblem)
        {
            if (!hasExistingDispatchRecord ||
                !previousCondition.IsProblem ||
                !string.Equals(previousCondition.Label, currentCondition.Label, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previousCondition.TrendLabel, currentCondition.TrendLabel, StringComparison.OrdinalIgnoreCase))
            {
                return new NotificationDecision(
                    "Alert",
                    currentCondition.SubjectLabel,
                    $"alert:{currentCondition.Label}:{currentCondition.Status}:{currentCondition.TrendLabel}");
            }
        }
        else if (previousCondition.IsProblem && settings.NotifyOnRecovery)
        {
            return new NotificationDecision(
                "Recovery",
                currentCondition.SubjectLabel,
                $"recovery:{previousCondition.Label}:{currentCondition.Status}");
        }
        else if (settings.NotifyOnRecovery &&
                 hasExistingDispatchRecord &&
                 IsStableTransition(previousCondition, currentCondition))
        {
            return new NotificationDecision(
                "Stabilized",
                currentCondition.TrendLabel,
                $"stabilized:{currentCondition.Status}:{currentCondition.TrendLabel}");
        }

        return null;
    }

    private static bool IsStableTransition(NotificationCondition previousCondition, NotificationCondition currentCondition)
    {
        return !previousCondition.IsProblem &&
               !currentCondition.IsProblem &&
               !IsStableTrend(previousCondition.TrendLabel) &&
               IsStableTrend(currentCondition.TrendLabel);
    }

    private static bool IsStableTrend(string? trendLabel)
    {
        return !string.IsNullOrWhiteSpace(trendLabel) &&
               trendLabel.StartsWith("Stable ", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsWithinCooldown(EndpointState currentState, string signature, int cooldownMinutes)
    {
        var record = currentState.NotificationDispatches
            .OrderByDescending(static dispatch => dispatch.SentUtc)
            .FirstOrDefault(dispatch => string.Equals(dispatch.Signature, signature, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            return false;
        }

        return (_timeProvider.GetUtcNow() - record.SentUtc) < TimeSpan.FromMinutes(cooldownMinutes);
    }

    private void AppendNotificationDispatch(
        EndpointState currentState,
        NotificationDecision notification,
        NotificationRecipients recipients)
    {
        currentState.NotificationDispatches.Add(new EndpointNotificationDispatch
        {
            EventType = notification.EventType,
            ConditionLabel = notification.SubjectLabel,
            Signature = notification.Signature,
            SentUtc = _timeProvider.GetUtcNow(),
            To = [.. recipients.To],
            Cc = [.. recipients.Cc]
        });

        var historyLimit = _runtimeStateOptions.GetNotificationHistoryLimit();
        if (historyLimit <= 0)
        {
            currentState.NotificationDispatches.Clear();
            return;
        }

        if (currentState.NotificationDispatches.Count <= historyLimit)
        {
            return;
        }

        var removeCount = currentState.NotificationDispatches.Count - historyLimit;
        currentState.NotificationDispatches.RemoveRange(0, removeCount);
    }

    private static string BuildSubject(string subjectPrefix, string eventType, string endpointName, string label)
    {
        var prefix = string.IsNullOrWhiteSpace(subjectPrefix) ? "[ApiHealthDashboard]" : subjectPrefix.Trim();
        return $"{prefix} {eventType}: {endpointName} - {label}";
    }

    private NotificationEmailContent BuildBody(
        EndpointConfig endpoint,
        NotificationCondition previousCondition,
        NotificationCondition currentCondition,
        NotificationDecision notification,
        string subject)
    {
        return _templateRenderer.Render(new NotificationEmailTemplateModel(
            subject,
            notification.EventType,
            endpoint.Name,
            endpoint.Id,
            endpoint.Url,
            EndpointPriority.Normalize(endpoint.Priority),
            currentCondition.Status,
            currentCondition.TrendLabel,
            previousCondition.Label,
            currentCondition.ErrorSummary ?? string.Empty,
            currentCondition.CheckedUtc is DateTimeOffset checkedUtc
                ? checkedUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
                : "-",
            notification.EventType switch
            {
                "Recovery" => "The endpoint has recovered from its previous problem state.",
                "Stabilized" => "The endpoint has settled into a stable state after recovering from an earlier issue.",
                _ => "The endpoint entered or changed problem state and may need attention."
            }));
    }

    private sealed record NotificationRecipients(IReadOnlyList<string> To, IReadOnlyList<string> Cc);

    private sealed record NotificationDecision(string EventType, string SubjectLabel, string Signature);

    private sealed record NotificationCondition(
        bool IsProblem,
        string Label,
        string Status,
        string TrendLabel,
        string? ErrorSummary,
        DateTimeOffset? CheckedUtc)
    {
        public string SubjectLabel => string.Equals(Label, TrendLabel, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(Status, TrendLabel, StringComparison.OrdinalIgnoreCase)
            ? Label
            : $"{Label} ({TrendLabel})";

        public static NotificationCondition Empty { get; } = new(false, string.Empty, "Unknown", "Awaiting trend", null, null);
    }
}
