using System.Collections.Concurrent;
using System.Text;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Statistics;

namespace ApiHealthDashboard.Services;

public sealed class EndpointEmailNotificationService : IEndpointNotificationService
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EndpointEmailNotificationService> _logger;
    private readonly SmtpEmailOptions _smtpOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, NotificationDispatchRecord> _dispatchRecords = new(StringComparer.OrdinalIgnoreCase);

    public EndpointEmailNotificationService(
        DashboardConfig dashboardConfig,
        SmtpEmailOptions smtpOptions,
        IEmailSender emailSender,
        TimeProvider timeProvider,
        ILogger<EndpointEmailNotificationService> logger)
    {
        _dashboardConfig = dashboardConfig;
        _smtpOptions = smtpOptions;
        _emailSender = emailSender;
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
        var hasExistingDispatchRecord = _dispatchRecords.ContainsKey(endpoint.Id);
        var notification = BuildNotificationDecision(
            notificationSettings,
            previousCondition,
            currentCondition,
            hasExistingDispatchRecord);
        if (notification is null)
        {
            return;
        }

        if (IsWithinCooldown(endpoint.Id, notification.Signature, notificationSettings.CooldownMinutes))
        {
            _logger.LogDebug(
                "Skipped email notification for endpoint {EndpointId} because the notification is within the configured cooldown window.",
                endpoint.Id);
            return;
        }

        var subject = BuildSubject(notificationSettings.SubjectPrefix, notification.EventType, endpoint.Name, notification.SubjectLabel);
        var body = BuildBody(endpoint, previousCondition, currentCondition, notification);

        await _emailSender.SendAsync(
            new EmailMessage
            {
                To = recipients.To,
                Cc = recipients.Cc,
                Subject = subject,
                Body = body
            },
            cancellationToken);

        _dispatchRecords[endpoint.Id] = new NotificationDispatchRecord(
            notification.Signature,
            _timeProvider.GetUtcNow());

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
                !string.Equals(previousCondition.Label, currentCondition.Label, StringComparison.OrdinalIgnoreCase))
            {
                return new NotificationDecision(
                    "Alert",
                    currentCondition.Label,
                    $"alert:{currentCondition.Label}:{currentCondition.Status}:{currentCondition.TrendLabel}");
            }
        }
        else if (previousCondition.IsProblem && settings.NotifyOnRecovery)
        {
            return new NotificationDecision(
                "Recovery",
                currentCondition.Label,
                $"recovery:{previousCondition.Label}:{currentCondition.Status}");
        }

        return null;
    }

    private bool IsWithinCooldown(string endpointId, string signature, int cooldownMinutes)
    {
        if (!_dispatchRecords.TryGetValue(endpointId, out var record))
        {
            return false;
        }

        if (!string.Equals(record.Signature, signature, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (_timeProvider.GetUtcNow() - record.SentUtc) < TimeSpan.FromMinutes(cooldownMinutes);
    }

    private static string BuildSubject(string subjectPrefix, string eventType, string endpointName, string label)
    {
        var prefix = string.IsNullOrWhiteSpace(subjectPrefix) ? "[ApiHealthDashboard]" : subjectPrefix.Trim();
        return $"{prefix} {eventType}: {endpointName} - {label}";
    }

    private static string BuildBody(
        EndpointConfig endpoint,
        NotificationCondition previousCondition,
        NotificationCondition currentCondition,
        NotificationDecision notification)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Endpoint: {endpoint.Name} ({endpoint.Id})");
        builder.AppendLine($"URL: {endpoint.Url}");
        builder.AppendLine($"Priority: {EndpointPriority.Normalize(endpoint.Priority)}");
        builder.AppendLine($"Event: {notification.EventType}");
        builder.AppendLine($"Current status: {currentCondition.Status}");
        builder.AppendLine($"Current trend: {currentCondition.TrendLabel}");

        if (!string.IsNullOrWhiteSpace(previousCondition.Label))
        {
            builder.AppendLine($"Previous condition: {previousCondition.Label}");
        }

        if (!string.IsNullOrWhiteSpace(currentCondition.ErrorSummary))
        {
            builder.AppendLine($"Error: {currentCondition.ErrorSummary}");
        }

        if (currentCondition.CheckedUtc is DateTimeOffset checkedUtc)
        {
            builder.AppendLine($"Checked: {checkedUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss 'UTC'}");
        }

        builder.AppendLine();
        builder.AppendLine(notification.EventType == "Recovery"
            ? "The endpoint has recovered from its previous problem state."
            : "The endpoint entered or changed problem state and may need attention.");

        return builder.ToString().TrimEnd();
    }

    private sealed record NotificationDispatchRecord(string Signature, DateTimeOffset SentUtc);

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
        public static NotificationCondition Empty { get; } = new(false, string.Empty, "Unknown", "Awaiting trend", null, null);
    }
}
