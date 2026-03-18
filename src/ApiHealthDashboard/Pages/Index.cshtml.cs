using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Scheduling;
using ApiHealthDashboard.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

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

    public int RefreshUiSeconds => _dashboardConfig.Dashboard.RefreshUiSeconds;

    public void OnGet()
    {
        LoadDashboard();
    }

    public IActionResult OnGetLiveSection()
    {
        LoadDashboard();

        return new PartialViewResult
        {
            ViewName = "_DashboardLiveSection",
            ViewData = new ViewDataDictionary<IndexModel>(ViewData, this)
        };
    }

    public async Task<IActionResult> OnPostRefreshAllAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Manual refresh requested for all enabled endpoints.");

        var refreshedCount = await _scheduler.RefreshAllEnabledAsync(cancellationToken);

        _logger.LogInformation(
            "Manual refresh request for all enabled endpoints started {RefreshedCount} refresh operation(s).",
            refreshedCount);

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
            _logger.LogWarning("Manual refresh requested on the dashboard without an endpoint id.");
            TempData["StatusMessage"] = "No endpoint was selected for refresh.";
            TempData["StatusType"] = "warning";
            return RedirectToPage();
        }

        _logger.LogInformation(
            "Manual refresh requested on the dashboard for endpoint {EndpointId}.",
            endpointId);

        var refreshed = await _scheduler.RefreshEndpointAsync(endpointId, cancellationToken);

        _logger.LogInformation(
            "Manual refresh requested on the dashboard for endpoint {EndpointId} completed with outcome {RefreshStarted}.",
            endpointId,
            refreshed);

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
            .OrderByDescending(static endpoint => endpoint.PrioritySortOrder)
            .ThenBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
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
            .OrderByDescending(static endpoint => endpoint.PrioritySortOrder)
            .ThenBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
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

        public required string Priority { get; init; }

        public required string PriorityBadgeClass { get; init; }

        public int PrioritySortOrder { get; init; }

        public bool IsPolling { get; init; }

        public bool ShowIdHint { get; init; }

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

        public bool ShowStatusDescription => !string.Equals(StatusDescription, Status, StringComparison.OrdinalIgnoreCase);

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
                Priority = EndpointPriority.Normalize(endpoint.Priority),
                PriorityBadgeClass = ToPriorityBadgeClass(endpoint.Priority),
                PrioritySortOrder = EndpointPriority.GetSortOrder(endpoint.Priority),
                IsPolling = state?.IsPolling ?? false,
                ShowIdHint = ShouldShowIdHint(endpoint.Name, endpoint.Id),
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

        private static bool ShouldShowIdHint(string name, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return !string.Equals(NormalizeForComparison(name), NormalizeForComparison(id), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeForComparison(string value)
        {
            var buffer = new char[value.Length];
            var length = 0;

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    buffer[length++] = char.ToLowerInvariant(character);
                }
            }

            return new string(buffer, 0, length);
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

        private static string ToPriorityBadgeClass(string priority)
        {
            return EndpointPriority.Normalize(priority) switch
            {
                EndpointPriority.Critical => "badge-danger",
                EndpointPriority.High => "badge-warning",
                EndpointPriority.Low => "badge-secondary",
                _ => "badge-info"
            };
        }
    }
}
