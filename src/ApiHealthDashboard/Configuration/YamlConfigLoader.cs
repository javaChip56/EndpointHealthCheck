using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiHealthDashboard.Configuration;

public sealed partial class YamlConfigLoader : IYamlConfigLoader
{
    private readonly IDeserializer _deserializer;
    private readonly DashboardConfigValidator _validator;

    public YamlConfigLoader(DashboardConfigValidator validator)
    {
        _validator = validator;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public DashboardConfigLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new DashboardConfigLoadResult
            {
                Config = new DashboardConfig(),
                Warnings =
                [
                    $"Dashboard configuration file '{path}' was not found. The dashboard started with no configured endpoints."
                ]
            };
        }

        var dashboardConfig = DeserializeDashboardConfig(path);
        Normalize(dashboardConfig);
        var warnings = new List<string>();

        var mergedConfig = new DashboardConfig
        {
            Dashboard = dashboardConfig.Dashboard,
            EndpointFiles = new List<string>(dashboardConfig.EndpointFiles),
            Endpoints = dashboardConfig.Endpoints
                .Select(CloneEndpoint)
                .ToList()
        };

        var dashboardDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();

        foreach (var endpointFilePath in dashboardConfig.EndpointFiles)
        {
            var resolvedEndpointFilePath = ResolveConfigPath(endpointFilePath, dashboardDirectory);
            if (!File.Exists(resolvedEndpointFilePath))
            {
                warnings.Add($"Endpoint configuration file '{resolvedEndpointFilePath}' was not found. It was skipped.");
                continue;
            }

            var fileEndpoints = LoadEndpointsFromFile(resolvedEndpointFilePath);
            mergedConfig.Endpoints.AddRange(fileEndpoints);
        }

        Normalize(mergedConfig);

        var errors = _validator.Validate(mergedConfig);
        if (errors.Count > 0)
        {
            throw new DashboardConfigurationException(path, errors);
        }

        return new DashboardConfigLoadResult
        {
            Config = mergedConfig,
            Warnings = warnings
        };
    }

    private DashboardConfig DeserializeDashboardConfig(string path)
    {
        var yaml = ReadYaml(path);

        try
        {
            var expandedYaml = ReplaceEnvironmentTokens(yaml);
            return _deserializer.Deserialize<DashboardConfig>(expandedYaml) ?? new DashboardConfig();
        }
        catch (YamlException ex)
        {
            throw new DashboardConfigurationException(
                path,
                [$"Failed to parse YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}"],
                ex);
        }
    }

    private List<EndpointConfig> LoadEndpointsFromFile(string path)
    {
        var yaml = ReadYaml(path);

        try
        {
            var expandedYaml = ReplaceEnvironmentTokens(yaml);
            var endpoints = new List<EndpointConfig>();
            YamlException? parseException = null;

            try
            {
                var endpointFile = _deserializer.Deserialize<EndpointFileDocument>(expandedYaml) ?? new EndpointFileDocument();
                if (endpointFile.Endpoints.Count > 0)
                {
                    endpoints = endpointFile.Endpoints;
                }
            }
            catch (YamlException ex)
            {
                parseException = ex;
            }

            if (endpoints.Count == 0)
            {
                try
                {
                    var singleEndpoint = _deserializer.Deserialize<EndpointConfig>(expandedYaml);
                    if (HasEndpointContent(singleEndpoint))
                    {
                        endpoints = [singleEndpoint!];
                    }
                }
                catch (YamlException ex)
                {
                    parseException ??= ex;
                }
            }

            NormalizeEndpoints(endpoints);

            if (endpoints.Count == 0)
            {
                if (parseException is not null)
                {
                    throw new DashboardConfigurationException(
                        path,
                        [$"Failed to parse YAML at line {parseException.Start.Line}, column {parseException.Start.Column}: {parseException.Message}"],
                        parseException);
                }

                throw new DashboardConfigurationException(path, [$"Endpoint file '{path}' did not contain any endpoint definitions."]);
            }

            return endpoints;
        }
        catch (DashboardConfigurationException)
        {
            throw;
        }
        catch (YamlException ex)
        {
            throw new DashboardConfigurationException(
                path,
                [$"Failed to parse YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}"],
                ex);
        }
    }

    private static string ReadYaml(string path)
    {
        if (!File.Exists(path))
        {
            throw new DashboardConfigurationException(
                path,
                [$"Configuration file '{path}' was not found."]);
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new DashboardConfigurationException(
                path,
                [$"Unable to read configuration file '{path}'."],
                ex);
        }
    }

    private static void Normalize(DashboardConfig config)
    {
        config.Dashboard ??= new DashboardSettings();
        config.EndpointFiles = NormalizeFileList(config.EndpointFiles);
        config.Endpoints ??= new List<EndpointConfig>();
        NormalizeEndpoints(config.Endpoints);
    }

    private static void NormalizeEndpoints(List<EndpointConfig>? endpoints)
    {
        if (endpoints is null)
        {
            return;
        }

        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index] ?? new EndpointConfig();

            endpoint.Id = endpoint.Id?.Trim() ?? string.Empty;
            endpoint.Name = endpoint.Name?.Trim() ?? string.Empty;
            endpoint.Url = endpoint.Url?.Trim() ?? string.Empty;
            endpoint.Headers = endpoint.Headers is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(endpoint.Headers, StringComparer.OrdinalIgnoreCase);
            endpoint.IncludeChecks = NormalizeCheckList(endpoint.IncludeChecks);
            endpoint.ExcludeChecks = NormalizeCheckList(endpoint.ExcludeChecks);

            endpoints[index] = endpoint;
        }
    }

    private static List<string> NormalizeFileList(List<string>? values)
    {
        if (values is null)
        {
            return new List<string>();
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToList();
    }

    private static List<string> NormalizeCheckList(List<string>? values)
    {
        if (values is null)
        {
            return new List<string>();
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToList();
    }

    private static string ReplaceEnvironmentTokens(string yaml)
    {
        return EnvironmentVariablePattern().Replace(
            yaml,
            static match =>
            {
                var variableName = match.Groups["name"].Value;
                return Environment.GetEnvironmentVariable(variableName) ?? match.Value;
            });
    }

    private static string ResolveConfigPath(string configuredPath, string baseDirectory)
    {
        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(baseDirectory, configuredPath));
    }

    private static bool HasEndpointContent(EndpointConfig? endpoint)
    {
        return endpoint is not null &&
               (!string.IsNullOrWhiteSpace(endpoint.Id) ||
                !string.IsNullOrWhiteSpace(endpoint.Name) ||
                !string.IsNullOrWhiteSpace(endpoint.Url) ||
                endpoint.TimeoutSeconds is not null ||
                endpoint.Headers.Count > 0 ||
                endpoint.IncludeChecks.Count > 0 ||
                endpoint.ExcludeChecks.Count > 0 ||
                endpoint.Enabled != true ||
                endpoint.FrequencySeconds != 30);
    }

    private static EndpointConfig CloneEndpoint(EndpointConfig endpoint)
    {
        return new EndpointConfig
        {
            Id = endpoint.Id,
            Name = endpoint.Name,
            Url = endpoint.Url,
            Enabled = endpoint.Enabled,
            FrequencySeconds = endpoint.FrequencySeconds,
            TimeoutSeconds = endpoint.TimeoutSeconds,
            Headers = new Dictionary<string, string>(endpoint.Headers, StringComparer.OrdinalIgnoreCase),
            IncludeChecks = [.. endpoint.IncludeChecks],
            ExcludeChecks = [.. endpoint.ExcludeChecks]
        };
    }

    private sealed class EndpointFileDocument
    {
        public List<EndpointConfig> Endpoints { get; set; } = new();
    }

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled)]
    private static partial Regex EnvironmentVariablePattern();
}
