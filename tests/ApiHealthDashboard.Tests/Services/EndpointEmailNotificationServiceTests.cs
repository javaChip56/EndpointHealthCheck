using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Services;

public sealed class EndpointEmailNotificationServiceTests
{
    [Fact]
    public async Task NotifyAsync_WhenEndpointEntersFailingState_SendsAlertEmail()
    {
        var config = CreateConfig();
        var sender = new FakeEmailSender();
        var service = CreateService(config, sender);

        var previousState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T01:00:00Z"),
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T00:59:00Z"),
                    Status = "Healthy",
                    DurationMs = 90,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:00:00Z"),
                    Status = "Healthy",
                    DurationMs = 95,
                    ResultKind = "Success"
                }
            ]
        };

        var currentState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Unknown",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T01:05:00Z"),
            LastError = "Timed out",
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:04:00Z"),
                    Status = "Unknown",
                    DurationMs = 200,
                    ResultKind = "Timeout",
                    ErrorSummary = "Timed out"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:05:00Z"),
                    Status = "Unknown",
                    DurationMs = 210,
                    ResultKind = "NetworkError",
                    ErrorSummary = "Connection refused"
                }
            ]
        };

        await service.NotifyAsync(config.Endpoints[0], previousState, currentState);

        var message = Assert.Single(sender.Messages);
        Assert.Contains("Alert", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Failing", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Event: Alert", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("<strong>Alert</strong>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Equal(["ops@example.com", "service-owner@example.com"], message.To.OrderBy(static value => value).ToArray());
        Assert.Equal(["lead@example.com", "teamlead@example.com"], message.Cc.OrderBy(static value => value).ToArray());
        var dispatch = Assert.Single(currentState.NotificationDispatches);
        Assert.Equal("Alert", dispatch.EventType);
        Assert.Equal("Failing", dispatch.ConditionLabel);
    }

    [Fact]
    public async Task NotifyAsync_WhenEndpointRecovers_SendsRecoveryEmail()
    {
        var config = CreateConfig();
        var sender = new FakeEmailSender();
        var service = CreateService(config, sender);

        var previousState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Unknown",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T01:05:00Z"),
            LastError = "Timed out",
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:04:00Z"),
                    Status = "Unknown",
                    DurationMs = 200,
                    ResultKind = "Timeout",
                    ErrorSummary = "Timed out"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:05:00Z"),
                    Status = "Unknown",
                    DurationMs = 210,
                    ResultKind = "NetworkError",
                    ErrorSummary = "Connection refused"
                }
            ]
        };

        var currentState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T01:10:00Z"),
            LastSuccessfulUtc = DateTimeOffset.Parse("2026-03-19T01:10:00Z"),
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:09:00Z"),
                    Status = "Healthy",
                    DurationMs = 80,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:10:00Z"),
                    Status = "Healthy",
                    DurationMs = 82,
                    ResultKind = "Success"
                }
            ]
        };

        await service.NotifyAsync(config.Endpoints[0], previousState, currentState);

        var message = Assert.Single(sender.Messages);
        Assert.Contains("Recovery", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Event: Recovery", message.TextBody, StringComparison.Ordinal);
        Assert.Single(currentState.NotificationDispatches);
    }

    [Fact]
    public async Task NotifyAsync_WhenEndpointIsAlreadyFailingAndNoPriorNotificationExists_SendsInitialAlert()
    {
        var config = CreateConfig();
        var sender = new FakeEmailSender();
        var service = CreateService(config, sender);

        var previousState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Unknown",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T01:05:00Z"),
            LastError = "Timed out",
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:04:00Z"),
                    Status = "Unknown",
                    DurationMs = 200,
                    ResultKind = "Timeout",
                    ErrorSummary = "Timed out"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:05:00Z"),
                    Status = "Unknown",
                    DurationMs = 210,
                    ResultKind = "NetworkError",
                    ErrorSummary = "Connection refused"
                }
            ]
        };

        var currentState = previousState.Clone();
        currentState.LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T01:06:00Z");

        await service.NotifyAsync(config.Endpoints[0], previousState, currentState);

        var message = Assert.Single(sender.Messages);
        Assert.Contains("Alert", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Failing", message.Subject, StringComparison.Ordinal);
        Assert.Single(currentState.NotificationDispatches);
    }

    [Fact]
    public async Task NotifyAsync_WhenProblemTrendChangesFromImprovingToWorsening_SendsNewAlert()
    {
        var config = CreateConfig();
        var sender = new FakeEmailSender();
        var service = CreateService(config, sender);

        var previousState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Degraded",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T02:00:00Z"),
            LastError = "Latency exceeded threshold",
            NotificationDispatches =
            [
                new EndpointNotificationDispatch
                {
                    EventType = "Alert",
                    ConditionLabel = "Degraded (Improving)",
                    Signature = "alert:Degraded:Degraded:Improving",
                    SentUtc = DateTimeOffset.Parse("2026-03-19T01:30:00Z"),
                    To = ["ops@example.com"]
                }
            ],
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:58:00Z"),
                    Status = "Unhealthy",
                    DurationMs = 140,
                    ResultKind = "Success",
                    ErrorSummary = "Latency exceeded threshold"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T01:59:00Z"),
                    Status = "Degraded",
                    DurationMs = 120,
                    ResultKind = "Success",
                    ErrorSummary = "Latency exceeded threshold"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:00:00Z"),
                    Status = "Degraded",
                    DurationMs = 115,
                    ResultKind = "Success",
                    ErrorSummary = "Latency exceeded threshold"
                }
            ]
        };

        var currentState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Degraded",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T02:05:00Z"),
            LastError = "Latency exceeded threshold",
            NotificationDispatches = previousState.NotificationDispatches.Select(static dispatch => dispatch.Clone()).ToList(),
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:03:00Z"),
                    Status = "Healthy",
                    DurationMs = 90,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:04:00Z"),
                    Status = "Degraded",
                    DurationMs = 125,
                    ResultKind = "Success",
                    ErrorSummary = "Latency exceeded threshold"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:05:00Z"),
                    Status = "Degraded",
                    DurationMs = 140,
                    ResultKind = "Success",
                    ErrorSummary = "Latency exceeded threshold"
                }
            ]
        };

        await service.NotifyAsync(config.Endpoints[0], previousState, currentState);

        var message = Assert.Single(sender.Messages);
        Assert.Contains("Alert", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Worsening", message.Subject, StringComparison.Ordinal);
        Assert.Equal(2, currentState.NotificationDispatches.Count);
    }

    [Fact]
    public async Task NotifyAsync_WhenRecoveredEndpointSettlesFromImprovingToStable_SendsStabilizedEmail()
    {
        var config = CreateConfig();
        var sender = new FakeEmailSender();
        var service = CreateService(config, sender);

        var previousState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T02:10:00Z"),
            LastSuccessfulUtc = DateTimeOffset.Parse("2026-03-19T02:10:00Z"),
            NotificationDispatches =
            [
                new EndpointNotificationDispatch
                {
                    EventType = "Recovery",
                    ConditionLabel = "Healthy (Improving)",
                    Signature = "recovery:Flapping:Healthy",
                    SentUtc = DateTimeOffset.Parse("2026-03-19T02:08:00Z"),
                    To = ["ops@example.com"]
                }
            ],
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:08:00Z"),
                    Status = "Unknown",
                    DurationMs = 200,
                    ResultKind = "HttpError",
                    ErrorSummary = "Endpoint returned HTTP 404 (NotFound)."
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:09:00Z"),
                    Status = "Healthy",
                    DurationMs = 80,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:10:00Z"),
                    Status = "Healthy",
                    DurationMs = 82,
                    ResultKind = "Success"
                }
            ]
        };

        var currentState = new EndpointState
        {
            EndpointId = "orders-api",
            EndpointName = "Orders API",
            Status = "Healthy",
            LastCheckedUtc = DateTimeOffset.Parse("2026-03-19T02:12:00Z"),
            LastSuccessfulUtc = DateTimeOffset.Parse("2026-03-19T02:12:00Z"),
            NotificationDispatches = previousState.NotificationDispatches.Select(static dispatch => dispatch.Clone()).ToList(),
            RecentSamples =
            [
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:09:00Z"),
                    Status = "Healthy",
                    DurationMs = 80,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:10:00Z"),
                    Status = "Healthy",
                    DurationMs = 82,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:11:00Z"),
                    Status = "Healthy",
                    DurationMs = 79,
                    ResultKind = "Success"
                },
                new RecentPollSample
                {
                    CheckedUtc = DateTimeOffset.Parse("2026-03-19T02:12:00Z"),
                    Status = "Healthy",
                    DurationMs = 81,
                    ResultKind = "Success"
                }
            ]
        };

        await service.NotifyAsync(config.Endpoints[0], previousState, currentState);

        var message = Assert.Single(sender.Messages);
        Assert.Contains("Stabilized", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Stable Healthy", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Event: Stabilized", message.TextBody, StringComparison.Ordinal);
        Assert.Equal(2, currentState.NotificationDispatches.Count);
        Assert.Equal("Stabilized", currentState.NotificationDispatches[^1].EventType);
    }

    private static EndpointEmailNotificationService CreateService(DashboardConfig config, FakeEmailSender sender)
    {
        return new EndpointEmailNotificationService(
            config,
            new RuntimeStateOptions
            {
                NotificationHistoryLimit = 20
            },
            new SmtpEmailOptions
            {
                Enabled = true,
                Host = "smtp.example.com",
                Port = 587,
                FromAddress = "dashboard@example.com",
                FromName = "ApiHealthDashboard"
            },
            sender,
            new FakeNotificationEmailTemplateRenderer(),
            TimeProvider.System,
            NullLogger<EndpointEmailNotificationService>.Instance);
    }

    private static DashboardConfig CreateConfig()
    {
        return new DashboardConfig
        {
            Dashboard = new DashboardSettings
            {
                Notifications = new DashboardNotificationSettings
                {
                    Enabled = true,
                    NotifyOnRecovery = true,
                    CooldownMinutes = 60,
                    MinimumPriority = EndpointPriority.Normal,
                    SubjectPrefix = "[ApiHealthDashboard]",
                    To = ["ops@example.com"],
                    Cc = ["teamlead@example.com"]
                }
            },
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    Enabled = true,
                    FrequencySeconds = 30,
                    Priority = EndpointPriority.High,
                    NotificationEmails = ["service-owner@example.com"],
                    NotificationCc = ["lead@example.com"]
                }
            ]
        };
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationEmailTemplateRenderer : INotificationEmailTemplateRenderer
    {
        public NotificationEmailContent Render(NotificationEmailTemplateModel model)
        {
            return new NotificationEmailContent(
                $"Subject: {model.Subject}\nEvent: {model.EventType}\nCurrent status: {model.CurrentStatus}\nCurrent trend: {model.CurrentTrend}\nSummary: {model.SummaryText}",
                $"<html><body><strong>{model.EventType}</strong><div>{model.EndpointName}</div><div>{model.CurrentTrend}</div></body></html>");
        }
    }
}
