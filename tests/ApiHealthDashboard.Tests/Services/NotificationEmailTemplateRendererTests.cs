using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.FileProviders;

namespace ApiHealthDashboard.Tests.Services;

public sealed class NotificationEmailTemplateRendererTests : IDisposable
{
    private readonly string _rootDirectory;

    public NotificationEmailTemplateRendererTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ApiHealthDashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public void Render_WhenTemplatesExist_RendersTextAndHtmlContent()
    {
        var templateDirectory = Path.Combine(_rootDirectory, "Templates", "Email");
        Directory.CreateDirectory(templateDirectory);
        File.WriteAllText(Path.Combine(templateDirectory, "notification.txt"), "Event: {{EventType}}\nEndpoint: {{EndpointName}}\nSummary: {{SummaryText}}");
        File.WriteAllText(Path.Combine(templateDirectory, "notification.html"), "<h1>{{EventType}}</h1><div>{{EndpointName}}</div><div>{{SummaryText}}</div>");

        var renderer = new NotificationEmailTemplateRenderer(
            new EmailTemplateOptions(),
            new FakeHostEnvironment(_rootDirectory),
            NullLogger<NotificationEmailTemplateRenderer>.Instance);

        var result = renderer.Render(new NotificationEmailTemplateModel(
            "Test subject",
            "Alert",
            "Orders API",
            "orders-api",
            "https://orders.example.com/health",
            "High",
            "Unknown",
            "Failing",
            "Healthy",
            "Timed out",
            "2026-03-20 12:00:00 UTC",
            "The endpoint entered or changed problem state and may need attention."));

        Assert.Contains("Event: Alert", result.TextBody, StringComparison.Ordinal);
        Assert.Contains("Orders API", result.TextBody, StringComparison.Ordinal);
        Assert.NotNull(result.HtmlBody);
        Assert.Contains("<h1>Alert</h1>", result.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Orders API", result.HtmlBody, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "ApiHealthDashboard.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
