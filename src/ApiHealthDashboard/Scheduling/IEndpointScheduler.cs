namespace ApiHealthDashboard.Scheduling;

public interface IEndpointScheduler
{
    Task<bool> RefreshEndpointAsync(string endpointId, CancellationToken cancellationToken = default);

    Task<int> RefreshAllEnabledAsync(CancellationToken cancellationToken = default);
}
