using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.State;

public sealed class InMemoryEndpointStateStore : IEndpointStateStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, EndpointState> _states = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryEndpointStateStore(IEnumerable<EndpointConfig> endpoints)
    {
        Initialize(endpoints);
    }

    public IReadOnlyCollection<EndpointState> GetAll()
    {
        lock (_syncRoot)
        {
            return _states.Values
                .Select(static state => state.Clone())
                .OrderBy(static state => state.EndpointName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static state => state.EndpointId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public EndpointState? Get(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        lock (_syncRoot)
        {
            return _states.TryGetValue(endpointId, out var state)
                ? state.Clone()
                : null;
        }
    }

    public void Upsert(EndpointState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.EndpointId);

        lock (_syncRoot)
        {
            _states[state.EndpointId] = state.Clone();
        }
    }

    public void Initialize(IEnumerable<EndpointConfig> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        lock (_syncRoot)
        {
            var existingStates = new Dictionary<string, EndpointState>(_states, StringComparer.OrdinalIgnoreCase);
            _states.Clear();

            foreach (var endpoint in endpoints)
            {
                if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Id))
                {
                    continue;
                }

                if (existingStates.TryGetValue(endpoint.Id, out var existingState))
                {
                    var preservedState = existingState.Clone();
                    preservedState.EndpointId = endpoint.Id;
                    preservedState.EndpointName = endpoint.Name;
                    _states[endpoint.Id] = preservedState;
                    continue;
                }

                _states[endpoint.Id] = CreateInitialState(endpoint);
            }
        }
    }

    private static EndpointState CreateInitialState(EndpointConfig endpoint)
    {
        return new EndpointState
        {
            EndpointId = endpoint.Id,
            EndpointName = endpoint.Name,
            Status = "Unknown",
            IsPolling = false
        };
    }
}
