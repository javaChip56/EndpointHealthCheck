using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.Parsing;

public interface IHealthResponseParser
{
    HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs);
}
