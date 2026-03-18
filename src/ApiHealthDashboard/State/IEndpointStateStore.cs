using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.State;

public interface IEndpointStateStore
{
    IReadOnlyCollection<EndpointState> GetAll();

    EndpointState? Get(string endpointId);

    void Upsert(EndpointState state);

    void Initialize(IEnumerable<EndpointConfig> endpoints);
}
