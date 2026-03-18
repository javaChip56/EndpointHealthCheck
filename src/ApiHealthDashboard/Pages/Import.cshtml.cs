using System.ComponentModel.DataAnnotations;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApiHealthDashboard.Pages;

public sealed class ImportModel : PageModel
{
    private readonly DashboardConfig _dashboardConfig;
    private readonly IEndpointImportService _endpointImportService;
    private readonly ILogger<ImportModel> _logger;

    public ImportModel(
        DashboardConfig dashboardConfig,
        IEndpointImportService endpointImportService,
        ILogger<ImportModel> logger)
    {
        _dashboardConfig = dashboardConfig;
        _endpointImportService = endpointImportService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public EndpointImportResult? Result { get; private set; }

    public int ExistingEndpointCount => _dashboardConfig.Endpoints.Count;

    public bool HasExistingEndpoints => ExistingEndpointCount > 0;

    public void OnGet()
    {
        InitializeDefaults();
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        InitializeDefaults();

        if (!ValidateInput())
        {
            return Page();
        }

        try
        {
            Result = await _endpointImportService.ImportAsync(
                new EndpointImportRequest
                {
                    Id = Input.Id,
                    Name = Input.Name,
                    Url = Input.Url,
                    Enabled = Input.Enabled,
                    FrequencySeconds = Input.FrequencySeconds,
                    TimeoutSeconds = Input.TimeoutSeconds,
                    HeadersText = Input.HeadersText,
                    IncludeDiscoveredChecks = Input.IncludeDiscoveredChecks
                },
                cancellationToken);

            Input.Id = Result.SuggestedEndpoint.Id;
            Input.Name = Result.SuggestedEndpoint.Name;
            ModelState.Clear();

            _logger.LogInformation(
                "Import preview generated for suggested endpoint {EndpointId}.",
                Result.SuggestedEndpoint.Id);
        }
        catch (EndpointImportException ex)
        {
            foreach (var error in ex.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            _logger.LogWarning(
                "Import preview validation failed with {ErrorCount} error(s).",
                ex.Errors.Count);
        }

        return Page();
    }

    private void InitializeDefaults()
    {
        if (Input.FrequencySeconds <= 0)
        {
            Input.FrequencySeconds = 30;
        }
    }

    private bool ValidateInput()
    {
        ModelState.ClearValidationState(nameof(Input));

        var validationContext = new ValidationContext(Input);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(Input, validationContext, validationResults, validateAllProperties: true);

        foreach (var validationResult in validationResults)
        {
            var memberNames = validationResult.MemberNames.Any()
                ? validationResult.MemberNames
                : [string.Empty];

            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(
                    string.IsNullOrWhiteSpace(memberName) ? string.Empty : $"{nameof(Input)}.{memberName}",
                    validationResult.ErrorMessage ?? "Validation failed.");
            }
        }

        return isValid;
    }

    public sealed class InputModel
    {
        [Display(Name = "Endpoint ID")]
        public string? Id { get; set; }

        [Display(Name = "Endpoint name")]
        public string? Name { get; set; }

        [Required]
        [Display(Name = "Endpoint URL")]
        public string Url { get; set; } = string.Empty;

        [Display(Name = "Enabled")]
        public bool Enabled { get; set; } = true;

        [Range(1, int.MaxValue)]
        [Display(Name = "Frequency seconds")]
        public int FrequencySeconds { get; set; } = 30;

        [Range(1, int.MaxValue)]
        [Display(Name = "Timeout seconds")]
        public int? TimeoutSeconds { get; set; }

        [Display(Name = "Headers")]
        public string HeadersText { get; set; } = string.Empty;

        [Display(Name = "Use discovered top-level checks as includeChecks")]
        public bool IncludeDiscoveredChecks { get; set; }
    }
}
