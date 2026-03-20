using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.Services;

public interface IEndpointNotificationService
{
    Task NotifyAsync(
        EndpointConfig endpoint,
        EndpointState? previousState,
        EndpointState currentState,
        CancellationToken cancellationToken = default);
}
