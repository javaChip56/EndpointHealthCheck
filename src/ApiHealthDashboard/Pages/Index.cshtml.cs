using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApiHealthDashboard.Pages;

public class IndexModel : PageModel
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly ILogger<IndexModel> _logger;
    private readonly IEndpointScheduler _scheduler;
    private readonly IEndpointStateStore _stateStore;

    public IndexModel(
        DashboardConfig dashboardConfig,
        IEndpointStateStore stateStore,
        IEndpointScheduler scheduler,
        ILogger<IndexModel> logger)
    {
        _dashboardConfig = dashboardConfig;
        _stateStore = stateStore;
        _scheduler = scheduler;
        _logger = logger;
    }

    public IReadOnlyList<EndpointSummaryViewModel> Endpoints { get; private set; } = [];

    public IReadOnlyList<EndpointSummaryViewModel> ProblemEndpoints { get; private set; } = [];

    public DashboardCountersViewModel Counters { get; private set; } = new();

    public bool HasConfiguredEndpoints => Endpoints.Count > 0;

    public void OnGet()
    {
        LoadDashboard();
    }

    public async Task<IActionResult> OnPostRefreshAllAsync(CancellationToken cancellationToken)
    {
        var refreshedCount = await _scheduler.RefreshAllEnabledAsync(cancellationToken);

        TempData["StatusMessage"] = refreshedCount > 0
            ? $"Triggered refresh for {refreshedCount} enabled endpoint(s)."
            : "No endpoint refreshes were started. Endpoints may already be polling or disabled.";
        TempData["StatusType"] = refreshedCount > 0 ? "success" : "warning";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshEndpointAsync(string endpointId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            TempData["StatusMessage"] = "No endpoint was selected for refresh.";
            TempData["StatusType"] = "warning";
            return RedirectToPage();
        }

        var refreshed = await _scheduler.RefreshEndpointAsync(endpointId, cancellationToken);

        TempData["StatusMessage"] = refreshed
            ? $"Triggered refresh for endpoint '{endpointId}'."
            : $"Refresh for endpoint '{endpointId}' was skipped because it is already polling or not configured.";
        TempData["StatusType"] = refreshed ? "success" : "warning";

        return RedirectToPage();
    }

    private void LoadDashboard()
    {
        var statesById = _stateStore.GetAll().ToDictionary(
            static state => state.EndpointId,
            StringComparer.OrdinalIgnoreCase);

        Endpoints = _dashboardConfig.Endpoints
            .Select(endpoint =>
            {
                statesById.TryGetValue(endpoint.Id, out var state);
                return EndpointSummaryViewModel.From(endpoint, state);
            })
            .OrderBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Counters = new DashboardCountersViewModel
        {
            Total = Endpoints.Count,
            Enabled = Endpoints.Count(static endpoint => endpoint.Enabled),
            Disabled = Endpoints.Count(static endpoint => !endpoint.Enabled),
            Polling = Endpoints.Count(static endpoint => endpoint.IsPolling),
            Healthy = Endpoints.Count(static endpoint => endpoint.Status == "Healthy"),
            Degraded = Endpoints.Count(static endpoint => endpoint.Status == "Degraded"),
            Unhealthy = Endpoints.Count(static endpoint => endpoint.Status == "Unhealthy"),
            Unknown = Endpoints.Count(static endpoint => endpoint.Status is not ("Healthy" or "Degraded" or "Unhealthy"))
        };

        ProblemEndpoints = Endpoints
            .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint.ErrorText) ||
                                      endpoint.Status is "Degraded" or "Unhealthy")
            .OrderBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogDebug("Loaded dashboard with {EndpointCount} endpoint summaries.", Endpoints.Count);
    }

    public sealed class DashboardCountersViewModel
    {
        public int Total { get; init; }

        public int Enabled { get; init; }

        public int Disabled { get; init; }

        public int Polling { get; init; }

        public int Healthy { get; init; }

        public int Degraded { get; init; }

        public int Unhealthy { get; init; }

        public int Unknown { get; init; }
    }

    public sealed class EndpointSummaryViewModel
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Status { get; init; }

        public required string StatusBadgeClass { get; init; }

        public required string FrequencyText { get; init; }

        public required bool Enabled { get; init; }

        public bool IsPolling { get; init; }

        public string LastCheckedText { get; init; } = "Never";

        public string LastSuccessfulText { get; init; } = "Never";

        public string DurationText { get; init; } = "-";

        public string? ErrorText { get; init; }

        public string ErrorSummary => string.IsNullOrWhiteSpace(ErrorText) ? "None" : ErrorText;

        public string StatusDescription => !Enabled
            ? "Disabled"
            : IsPolling
                ? "Polling"
                : Status;

        public static EndpointSummaryViewModel From(EndpointConfig endpoint, EndpointState? state)
        {
            var status = state?.Status ?? "Unknown";

            return new EndpointSummaryViewModel
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                Status = status,
                StatusBadgeClass = ToBadgeClass(status),
                FrequencyText = $"{endpoint.FrequencySeconds} sec",
                Enabled = endpoint.Enabled,
                IsPolling = state?.IsPolling ?? false,
                LastCheckedText = FormatDateTime(state?.LastCheckedUtc),
                LastSuccessfulText = FormatDateTime(state?.LastSuccessfulUtc),
                DurationText = state?.DurationMs is long durationMs ? $"{durationMs} ms" : "-",
                ErrorText = state?.LastError
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
}
