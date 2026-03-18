using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Services;

public interface IEndpointPoller
{
    Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken);
}
