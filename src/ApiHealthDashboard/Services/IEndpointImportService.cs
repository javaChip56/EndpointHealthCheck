namespace ApiHealthDashboard.Services;

public interface IEndpointImportService
{
    Task<EndpointImportResult> ImportAsync(EndpointImportRequest request, CancellationToken cancellationToken);
}
