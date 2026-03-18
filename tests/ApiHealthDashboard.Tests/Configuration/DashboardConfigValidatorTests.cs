using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Tests.Configuration;

public sealed class DashboardConfigValidatorTests
{
    private readonly DashboardConfigValidator _validator = new();

    [Fact]
    public void Validate_WithValidConfiguration_ReturnsNoErrors()
    {
        var config = new DashboardConfig
        {
            Dashboard = new DashboardSettings
            {
                RefreshUiSeconds = 15,
                RequestTimeoutSecondsDefault = 20
            },
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    FrequencySeconds = 30,
                    TimeoutSeconds = 5,
                    Headers = new Dictionary<string, string>
                    {
                        ["X-Trace"] = "enabled"
                    }
                }
            ]
        };

        var errors = _validator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_WithCaseInsensitiveDuplicateEndpointIds_ReturnsDuplicateError()
    {
        var config = new DashboardConfig
        {
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "https://orders.example.com/health",
                    FrequencySeconds = 30
                },
                new EndpointConfig
                {
                    Id = "ORDERS-API",
                    Name = "Orders API Replica",
                    Url = "https://orders-replica.example.com/health",
                    FrequencySeconds = 30
                }
            ]
        };

        var errors = _validator.Validate(config);

        Assert.Contains(errors, static error => error.Contains("must be unique", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithRelativeUrl_ReturnsUrlError()
    {
        var config = new DashboardConfig
        {
            Endpoints =
            [
                new EndpointConfig
                {
                    Id = "orders-api",
                    Name = "Orders API",
                    Url = "/health",
                    FrequencySeconds = 30
                }
            ]
        };

        var errors = _validator.Validate(config);

        Assert.Contains("endpoints[0].url must be an absolute HTTP or HTTPS URL.", errors);
    }
}
