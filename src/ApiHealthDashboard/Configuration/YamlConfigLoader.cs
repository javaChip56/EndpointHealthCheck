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

    public DashboardConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new DashboardConfigurationException(
                path,
                [$"Configuration file '{path}' was not found."]);
        }

        string yaml;

        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new DashboardConfigurationException(
                path,
                [$"Unable to read configuration file '{path}'."],
                ex);
        }

        DashboardConfig config;

        try
        {
            var expandedYaml = ReplaceEnvironmentTokens(yaml);
            config = _deserializer.Deserialize<DashboardConfig>(expandedYaml) ?? new DashboardConfig();
        }
        catch (YamlException ex)
        {
            throw new DashboardConfigurationException(
                path,
                [$"Failed to parse YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}"],
                ex);
        }

        Normalize(config);

        var errors = _validator.Validate(config);
        if (errors.Count > 0)
        {
            throw new DashboardConfigurationException(path, errors);
        }

        return config;
    }

    private static void Normalize(DashboardConfig config)
    {
        config.Dashboard ??= new DashboardSettings();
        config.Endpoints ??= new List<EndpointConfig>();

        for (var index = 0; index < config.Endpoints.Count; index++)
        {
            var endpoint = config.Endpoints[index] ?? new EndpointConfig();

            endpoint.Id = endpoint.Id?.Trim() ?? string.Empty;
            endpoint.Name = endpoint.Name?.Trim() ?? string.Empty;
            endpoint.Url = endpoint.Url?.Trim() ?? string.Empty;
            endpoint.Headers = endpoint.Headers is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(endpoint.Headers, StringComparer.OrdinalIgnoreCase);
            endpoint.IncludeChecks = NormalizeCheckList(endpoint.IncludeChecks);
            endpoint.ExcludeChecks = NormalizeCheckList(endpoint.ExcludeChecks);

            config.Endpoints[index] = endpoint;
        }
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

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled)]
    private static partial Regex EnvironmentVariablePattern();
}
