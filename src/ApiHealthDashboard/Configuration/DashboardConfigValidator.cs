namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfigValidator
{
    public IReadOnlyList<string> Validate(DashboardConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();
        var endpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config.Dashboard.RefreshUiSeconds <= 0)
        {
            errors.Add("dashboard.refreshUiSeconds must be greater than zero.");
        }

        if (config.Dashboard.RequestTimeoutSecondsDefault <= 0)
        {
            errors.Add("dashboard.requestTimeoutSecondsDefault must be greater than zero.");
        }

        for (var index = 0; index < config.Endpoints.Count; index++)
        {
            var endpoint = config.Endpoints[index];
            var prefix = $"endpoints[{index}]";

            if (string.IsNullOrWhiteSpace(endpoint.Id))
            {
                errors.Add($"{prefix}.id is required.");
            }
            else if (!endpointIds.Add(endpoint.Id))
            {
                errors.Add($"Endpoint id '{endpoint.Id}' must be unique.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Name))
            {
                errors.Add($"{prefix}.name is required.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Url))
            {
                errors.Add($"{prefix}.url is required.");
            }
            else if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"{prefix}.url must be an absolute HTTP or HTTPS URL.");
            }

            if (endpoint.FrequencySeconds <= 0)
            {
                errors.Add($"{prefix}.frequencySeconds must be greater than zero.");
            }

            if (endpoint.TimeoutSeconds is <= 0)
            {
                errors.Add($"{prefix}.timeoutSeconds must be greater than zero when specified.");
            }

            if (!EndpointPriority.IsValid(endpoint.Priority))
            {
                errors.Add(
                    $"{prefix}.priority must be one of: {string.Join(", ", EndpointPriority.AllowedValues)}.");
            }

            foreach (var header in endpoint.Headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    errors.Add($"{prefix}.headers contains an empty header name.");
                }
            }
        }

        return errors;
    }
}
