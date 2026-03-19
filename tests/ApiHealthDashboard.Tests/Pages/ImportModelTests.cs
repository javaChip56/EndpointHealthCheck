using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Pages;
using ApiHealthDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiHealthDashboard.Tests.Pages;

public sealed class ImportModelTests
{
    [Fact]
    public async Task OnPostPreviewAsync_WithValidInput_LoadsImportResult()
    {
        var importResult = new EndpointImportResult
        {
            SuggestedEndpoint = new EndpointConfig
            {
                Id = "orders-api",
                Name = "Orders API",
                Url = "https://orders.example.com/health",
                Enabled = true,
                FrequencySeconds = 30,
                NotificationEmails = ["ops@example.com"],
                NotificationCc = ["lead@example.com"]
            },
            GeneratedYaml = "id: 'orders-api'",
            ProbeResult = new PollResult
            {
                Kind = PollResultKind.Success,
                DurationMs = 42
            },
            ProbeStatusText = "Probe completed successfully.",
            MatchSummary = "No existing endpoint matched the suggested id or URL."
        };

        var model = new ImportModel(
            new DashboardConfig(),
            new StubEndpointImportService(importResult),
            Options.Create(new ImportUiOptions
            {
                MinimumRecommendedPollFrequencySeconds = 180
            }),
            NullLogger<ImportModel>.Instance)
        {
            Input = new ImportModel.InputModel
            {
                Url = "https://orders.example.com/health",
                FrequencySeconds = 30,
                NotificationEmailsText = "ops@example.com",
                NotificationCcText = "lead@example.com"
            }
        };

        var result = await model.OnPostPreviewAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Result);
        Assert.Equal("orders-api", model.Input.Id);
        Assert.Equal("Orders API", model.Input.Name);
        Assert.Equal("ops@example.com", model.Input.NotificationEmailsText);
        Assert.Equal("lead@example.com", model.Input.NotificationCcText);
        Assert.Equal("Poll frequency below the recommended soft limit of 180 seconds may create unnecessary load.", model.FrequencyRecommendationWarning);
    }

    [Fact]
    public async Task OnPostPreviewAsync_WithInvalidInput_AddsModelError()
    {
        var model = new ImportModel(
            new DashboardConfig(),
            new StubEndpointImportService(new EndpointImportResult
            {
                SuggestedEndpoint = new EndpointConfig
                {
                    Id = "ignored",
                    Name = "Ignored",
                    Url = "https://ignored.example.com/health",
                    Enabled = true,
                    FrequencySeconds = 30
                },
                GeneratedYaml = string.Empty,
                ProbeResult = new PollResult(),
                ProbeStatusText = string.Empty,
                MatchSummary = string.Empty
            }),
            Options.Create(new ImportUiOptions
            {
                MinimumRecommendedPollFrequencySeconds = 180
            }),
            NullLogger<ImportModel>.Instance)
        {
            Input = new ImportModel.InputModel
            {
                Url = string.Empty,
                FrequencySeconds = 30
            }
        };

        var result = await model.OnPostPreviewAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Null(model.Result);
        Assert.Equal("Poll frequency below the recommended soft limit of 180 seconds may create unnecessary load.", model.FrequencyRecommendationWarning);
    }

    private sealed class StubEndpointImportService : IEndpointImportService
    {
        private readonly EndpointImportResult _result;

        public StubEndpointImportService(EndpointImportResult result)
        {
            _result = result;
        }

        public Task<EndpointImportResult> ImportAsync(EndpointImportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }
}
