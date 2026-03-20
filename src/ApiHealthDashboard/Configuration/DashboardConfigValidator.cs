using System.ComponentModel.DataAnnotations;

namespace ApiHealthDashboard.Configuration;

public sealed class DashboardConfigValidator
{
    private static readonly EmailAddressAttribute EmailValidator = new();

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

        if (config.Dashboard.Notifications.CooldownMinutes <= 0)
        {
            errors.Add("dashboard.notifications.cooldownMinutes must be greater than zero.");
        }

        if (!EndpointPriority.IsValid(config.Dashboard.Notifications.MinimumPriority))
        {
            errors.Add(
                $"dashboard.notifications.minimumPriority must be one of: {string.Join(", ", EndpointPriority.AllowedValues)}.");
        }

        ValidateEmailList(config.Dashboard.Notifications.To, "dashboard.notifications.to", errors);
        ValidateEmailList(config.Dashboard.Notifications.Cc, "dashboard.notifications.cc", errors);

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

            ValidateEmailList(endpoint.NotificationEmails, $"{prefix}.notificationEmails", errors);
            ValidateEmailList(endpoint.NotificationCc, $"{prefix}.notificationCc", errors);
        }

        return errors;
    }

    private static void ValidateEmailList(IEnumerable<string> values, string prefix, ICollection<string> errors)
    {
        var index = 0;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !EmailValidator.IsValid(value))
            {
                errors.Add($"{prefix}[{index}] must be a valid email address.");
            }

            index++;
        }
    }
}
