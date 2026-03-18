using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Tests.Configuration;

public sealed class YamlConfigLoaderTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly YamlConfigLoader _loader;

    public YamlConfigLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiHealthDashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _loader = new YamlConfigLoader(new DashboardConfigValidator());
    }

    [Fact]
    public void Load_WithValidYaml_ReturnsNormalizedConfiguration()
    {
        var configPath = WriteConfig(
            """
            dashboard:
              refreshUiSeconds: 15
              requestTimeoutSecondsDefault: 12
              showRawPayload: true
            endpoints:
              - id: orders-api
                name: Orders API
                url: https://orders.example.com/health
                enabled: true
                frequencySeconds: 30
                timeoutSeconds: 5
                headers:
                  X-Trace: enabled
                includeChecks:
                  - self
                  - database
                excludeChecks:
                  - optional-third-party
            """);

        var config = _loader.Load(configPath);

        Assert.Equal(15, config.Dashboard.RefreshUiSeconds);
        Assert.Equal(12, config.Dashboard.RequestTimeoutSecondsDefault);
        Assert.True(config.Dashboard.ShowRawPayload);
        Assert.Single(config.Endpoints);

        var endpoint = config.Endpoints[0];
        Assert.Equal("orders-api", endpoint.Id);
        Assert.Equal("Orders API", endpoint.Name);
        Assert.Equal("https://orders.example.com/health", endpoint.Url);
        Assert.Equal(30, endpoint.FrequencySeconds);
        Assert.Equal(5, endpoint.TimeoutSeconds);
        Assert.Equal("enabled", endpoint.Headers["X-Trace"]);
        Assert.Equal(["self", "database"], endpoint.IncludeChecks);
        Assert.Equal(["optional-third-party"], endpoint.ExcludeChecks);
    }

    [Fact]
    public void Load_WithNullOptionalCollections_NormalizesThemToEmpty()
    {
        var configPath = WriteConfig(
            """
            endpoints:
              - id: notifications-api
                name: Notifications API
                url: https://notifications.example.com/health
                frequencySeconds: 45
                headers:
                includeChecks:
                excludeChecks:
            """);

        var config = _loader.Load(configPath);
        var endpoint = Assert.Single(config.Endpoints);

        Assert.Empty(endpoint.Headers);
        Assert.Empty(endpoint.IncludeChecks);
        Assert.Empty(endpoint.ExcludeChecks);
        Assert.Equal(10, config.Dashboard.RefreshUiSeconds);
        Assert.Equal(10, config.Dashboard.RequestTimeoutSecondsDefault);
    }

    [Fact]
    public void Load_WithDuplicateEndpointIds_ThrowsHelpfulValidationError()
    {
        var configPath = WriteConfig(
            """
            endpoints:
              - id: shared-api
                name: Orders API
                url: https://orders.example.com/health
                frequencySeconds: 30
              - id: shared-api
                name: Billing API
                url: https://billing.example.com/health
                frequencySeconds: 60
            """);

        var exception = Assert.Throws<DashboardConfigurationException>(() => _loader.Load(configPath));

        Assert.Contains(exception.Errors, static error => error.Contains("must be unique", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_WithInvalidValues_AggregatesValidationErrors()
    {
        var configPath = WriteConfig(
            """
            dashboard:
              refreshUiSeconds: 0
              requestTimeoutSecondsDefault: 0
            endpoints:
              - id:
                name:
                url: ftp://orders.example.com/health
                frequencySeconds: 0
                timeoutSeconds: 0
                headers:
                  "": invalid
            """);

        var exception = Assert.Throws<DashboardConfigurationException>(() => _loader.Load(configPath));

        Assert.Contains("dashboard.refreshUiSeconds must be greater than zero.", exception.Errors);
        Assert.Contains("dashboard.requestTimeoutSecondsDefault must be greater than zero.", exception.Errors);
        Assert.Contains("endpoints[0].id is required.", exception.Errors);
        Assert.Contains("endpoints[0].name is required.", exception.Errors);
        Assert.Contains("endpoints[0].url must be an absolute HTTP or HTTPS URL.", exception.Errors);
        Assert.Contains("endpoints[0].frequencySeconds must be greater than zero.", exception.Errors);
        Assert.Contains("endpoints[0].timeoutSeconds must be greater than zero when specified.", exception.Errors);
        Assert.Contains("endpoints[0].headers contains an empty header name.", exception.Errors);
    }

    [Fact]
    public void Load_ReplacesEnvironmentVariableTokensWhenPresent()
    {
        const string variableName = "API_HEALTH_DASHBOARD_TEST_HEADER";
        var originalValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "secret-value");

            var configPath = WriteConfig(
                $$"""
                endpoints:
                  - id: secured-api
                    name: Secured API
                    url: https://secured.example.com/health
                    frequencySeconds: 20
                    headers:
                      X-Api-Key: ${{{variableName}}}
                """);

            var config = _loader.Load(configPath);

            Assert.Equal("secret-value", config.Endpoints[0].Headers["X-Api-Key"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }

    [Fact]
    public void Load_WithMissingFile_ThrowsHelpfulError()
    {
        var missingPath = Path.Combine(_tempDirectory, "missing.yaml");

        var exception = Assert.Throws<DashboardConfigurationException>(() => _loader.Load(missingPath));

        Assert.Contains("was not found", exception.Errors.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_WithMalformedYaml_ThrowsHelpfulParseError()
    {
        var configPath = WriteConfig(
            """
            endpoints:
              - id: broken-api
                name: Broken API
                url: https://broken.example.com/health
                headers: [invalid
            """);

        var exception = Assert.Throws<DashboardConfigurationException>(() => _loader.Load(configPath));

        Assert.Contains("Failed to parse YAML", exception.Errors.Single(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteConfig(string content)
    {
        var path = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content);
        return path;
    }
}
