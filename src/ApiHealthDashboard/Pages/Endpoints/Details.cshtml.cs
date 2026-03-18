using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApiHealthDashboard.Pages.Endpoints;

public class DetailsModel : PageModel
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly ILogger<DetailsModel> _logger;
    private readonly IEndpointScheduler _scheduler;
    private readonly IEndpointStateStore _stateStore;

    public DetailsModel(
        DashboardConfig dashboardConfig,
        IEndpointStateStore stateStore,
        IEndpointScheduler scheduler,
        ILogger<DetailsModel> logger)
    {
        _dashboardConfig = dashboardConfig;
        _stateStore = stateStore;
        _scheduler = scheduler;
        _logger = logger;
    }

    public string EndpointId { get; private set; } = string.Empty;

    public EndpointDetailsViewModel? Endpoint { get; private set; }

    public IActionResult OnGet(string? id)
    {
        var endpointId = ResolveEndpointId(id);
        if (endpointId is null)
        {
            return NotFound();
        }

        return TryLoadEndpoint(endpointId)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostRefreshAsync(string? id, CancellationToken cancellationToken)
    {
        var endpointId = ResolveEndpointId(id);
        if (endpointId is null)
        {
            return NotFound();
        }

        var refreshed = await _scheduler.RefreshEndpointAsync(endpointId, cancellationToken);

        TempData["StatusMessage"] = refreshed
            ? $"Triggered refresh for endpoint '{endpointId}'."
            : $"Refresh for endpoint '{endpointId}' was skipped because it is already polling or not configured.";
        TempData["StatusType"] = refreshed ? "success" : "warning";

        return RedirectToPage(new { id = endpointId });
    }

    private string? ResolveEndpointId(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return _dashboardConfig.Endpoints.FirstOrDefault()?.Id;
    }

    private bool TryLoadEndpoint(string endpointId)
    {
        var endpointConfig = _dashboardConfig.Endpoints.FirstOrDefault(
            endpoint => string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));

        if (endpointConfig is null)
        {
            return false;
        }

        EndpointId = endpointConfig.Id;

        var state = _stateStore.Get(endpointConfig.Id);
        Endpoint = EndpointDetailsViewModel.From(endpointConfig, state, _dashboardConfig.Dashboard.ShowRawPayload);

        _logger.LogDebug("Loaded endpoint details for {EndpointId}.", endpointConfig.Id);
        return true;
    }

    public sealed class EndpointDetailsViewModel
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Url { get; init; }

        public required bool Enabled { get; init; }

        public required string FrequencyText { get; init; }

        public required string TimeoutText { get; init; }

        public required string Status { get; init; }

        public required string StatusBadgeClass { get; init; }

        public required bool IsPolling { get; init; }

        public string LastCheckedText { get; init; } = "Never";

        public string LastSuccessfulText { get; init; } = "Never";

        public string DurationText { get; init; } = "-";

        public string? ErrorText { get; init; }

        public IReadOnlyList<HeaderSummaryViewModel> Headers { get; init; } = [];

        public IReadOnlyList<HealthNode> Nodes { get; init; } = [];

        public string? RawPayload { get; init; }

        public bool ShowRawPayload { get; init; }

        public static EndpointDetailsViewModel From(
            EndpointConfig endpoint,
            EndpointState? state,
            bool showRawPayload)
        {
            var status = state?.Status ?? "Unknown";
            var timeoutSeconds = endpoint.TimeoutSeconds?.ToString() ?? "Default";

            return new EndpointDetailsViewModel
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                Url = endpoint.Url,
                Enabled = endpoint.Enabled,
                FrequencyText = $"{endpoint.FrequencySeconds} seconds",
                TimeoutText = endpoint.TimeoutSeconds is null ? "Default timeout" : $"{timeoutSeconds} seconds",
                Status = status,
                StatusBadgeClass = ToBadgeClass(status),
                IsPolling = state?.IsPolling ?? false,
                LastCheckedText = FormatDateTime(state?.LastCheckedUtc),
                LastSuccessfulText = FormatDateTime(state?.LastSuccessfulUtc),
                DurationText = state?.DurationMs is long durationMs ? $"{durationMs} ms" : "-",
                ErrorText = state?.LastError,
                Headers = endpoint.Headers
                    .OrderBy(static header => header.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static header => new HeaderSummaryViewModel
                    {
                        Name = header.Key,
                        ValuePreview = string.IsNullOrEmpty(header.Value) ? "(empty)" : "********"
                    })
                    .ToArray(),
                Nodes = state?.Snapshot?.Nodes.Select(static node => node.Clone()).ToArray() ?? [],
                RawPayload = showRawPayload ? state?.Snapshot?.RawPayload : null,
                ShowRawPayload = showRawPayload
            };
        }

        private static string FormatDateTime(DateTimeOffset? value)
        {
            return value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Never";
        }

        private static string ToBadgeClass(string status)
        {
            return status switch
            {
                "Healthy" => "badge-success",
                "Degraded" => "badge-warning",
                "Unhealthy" => "badge-danger",
                _ => "badge-secondary"
            };
        }
    }

    public sealed class HeaderSummaryViewModel
    {
        public required string Name { get; init; }

        public required string ValuePreview { get; init; }
    }
}
