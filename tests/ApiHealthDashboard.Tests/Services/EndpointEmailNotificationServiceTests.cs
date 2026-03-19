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
}
