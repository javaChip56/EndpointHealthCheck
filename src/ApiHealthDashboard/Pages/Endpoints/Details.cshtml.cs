using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApiHealthDashboard.Pages.Endpoints;

public class DetailsModel : PageModel
{
    public string EndpointId { get; private set; } = "orders-api";

    public void OnGet(string? id)
    {
        EndpointId = string.IsNullOrWhiteSpace(id) ? "orders-api" : id;
    }
}
