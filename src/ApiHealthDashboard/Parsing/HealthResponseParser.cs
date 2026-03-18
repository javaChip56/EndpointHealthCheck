using System.Text.Json;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.Parsing;

public sealed class HealthResponseParser : IHealthResponseParser
{
    private static readonly HashSet<string> ExplicitChildContainerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "entries",
        "children",
        "checks",
        "results",
        "nodes",
        "items"
    };

    private static readonly HashSet<string> StructuralPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "description",
        "duration",
        "durationText",
        "error",
        "errorMessage",
        "exception",
        "message",
        "data",
        "entries",
        "children",
        "checks",
        "results",
        "nodes",
        "items",
        "name"
    };

    private readonly ILogger<HealthResponseParser> _logger;

    public HealthResponseParser(ILogger<HealthResponseParser> logger)
    {
        _logger = logger;
    }

    public HealthSnapshot Parse(EndpointConfig endpoint, string json, long durationMs)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(json);

        var retrievedUtc = DateTimeOffset.UtcNow;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var nodes = ParseRootNodes(root);
            var filteredNodes = ApplyFilters(nodes, endpoint.IncludeChecks, endpoint.ExcludeChecks);

            return new HealthSnapshot
            {
                OverallStatus = ExtractStatus(root) ?? AggregateStatus(filteredNodes.Select(static node => node.Status)),
                RetrievedUtc = retrievedUtc,
                DurationMs = durationMs,
                RawPayload = json,
                Nodes = filteredNodes,
                Metadata = ExtractRootMetadata(root)
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse health response JSON for endpoint {EndpointId}.",
                endpoint.Id);

            return new HealthSnapshot
            {
                OverallStatus = "Unknown",
                RetrievedUtc = retrievedUtc,
                DurationMs = durationMs,
                RawPayload = json,
                Metadata = new Dictionary<string, object?>
                {
                    ["parserError"] = ex.Message
                }
            };
        }
    }

    private static List<HealthNode> ParseRootNodes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new List<HealthNode>();
        }

        var nodes = new List<HealthNode>();

        foreach (var property in root.EnumerateObject())
        {
            if (ExplicitChildContainerNames.Contains(property.Name))
            {
                nodes.AddRange(ParseExplicitContainer(property.Value));
                continue;
            }

            if (StructuralPropertyNames.Contains(property.Name))
            {
                continue;
            }

            if (LooksLikeHealthNode(property.Value))
            {
                nodes.Add(ParseNode(property.Name, property.Value));
                continue;
            }

            if (LooksLikeHealthCollection(property.Value))
            {
                nodes.Add(ParseContainerNode(property.Name, property.Value));
            }
        }

        return nodes;
    }

    private static List<HealthNode> ParseExplicitContainer(JsonElement container)
    {
        var nodes = new List<HealthNode>();

        if (container.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in container.EnumerateObject())
            {
                nodes.Add(ParseNode(property.Name, property.Value));
            }
        }
        else if (container.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in container.EnumerateArray())
            {
                index++;
                var nodeName = ExtractName(item) ?? $"item-{index}";
                nodes.Add(ParseNode(nodeName, item));
            }
        }

        return nodes;
    }

    private static HealthNode ParseContainerNode(string name, JsonElement container)
    {
        var children = new List<HealthNode>();

        if (container.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in container.EnumerateObject())
            {
                if (LooksLikeHealthNode(property.Value))
                {
                    children.Add(ParseNode(property.Name, property.Value));
                }
                else if (LooksLikeHealthCollection(property.Value))
                {
                    children.Add(ParseContainerNode(property.Name, property.Value));
                }
            }
        }
        else if (container.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in container.EnumerateArray())
            {
                index++;
                var childName = ExtractName(item) ?? $"{name}-{index}";
                children.Add(ParseNode(childName, item));
            }
        }

        return new HealthNode
        {
            Name = name,
            Status = AggregateStatus(children.Select(static child => child.Status)),
            Children = children
        };
    }

    private static HealthNode ParseNode(string name, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new HealthNode
            {
                Name = name,
                Status = "Unknown",
                Data = new Dictionary<string, object?>
                {
                    ["value"] = ConvertJsonValue(element)
                }
            };
        }

        var children = new List<HealthNode>();

        foreach (var property in element.EnumerateObject())
        {
            if (ExplicitChildContainerNames.Contains(property.Name))
            {
                children.AddRange(ParseExplicitContainer(property.Value));
                continue;
            }

            if (StructuralPropertyNames.Contains(property.Name))
            {
                continue;
            }

            if (LooksLikeHealthNode(property.Value))
            {
                children.Add(ParseNode(property.Name, property.Value));
                continue;
            }

            if (LooksLikeHealthCollection(property.Value))
            {
                children.Add(ParseContainerNode(property.Name, property.Value));
            }
        }

        var data = ExtractNodeData(element);

        return new HealthNode
        {
            Name = name,
            Status = ExtractStatus(element) ?? AggregateStatus(children.Select(static child => child.Status)),
            Description = GetString(element, "description"),
            ErrorMessage = GetString(element, "error")
                ?? GetString(element, "errorMessage")
                ?? GetString(element, "exception"),
            DurationText = GetString(element, "duration")
                ?? GetString(element, "durationText"),
            Data = data,
            Children = children
        };
    }

    private static Dictionary<string, object?> ExtractRootMetadata(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>();
        }

        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            if (ExplicitChildContainerNames.Contains(property.Name))
            {
                continue;
            }

            if (property.NameEquals("status"))
            {
                continue;
            }

            if (property.NameEquals("data") && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in property.Value.EnumerateObject())
                {
                    metadata[item.Name] = ConvertJsonValue(item.Value);
                }

                continue;
            }

            if (StructuralPropertyNames.Contains(property.Name))
            {
                metadata[property.Name] = ConvertJsonValue(property.Value);
                continue;
            }

            if (LooksLikeHealthNode(property.Value) || LooksLikeHealthCollection(property.Value))
            {
                continue;
            }

            metadata[property.Name] = ConvertJsonValue(property.Value);
        }

        return metadata;
    }

    private static Dictionary<string, object?> ExtractNodeData(JsonElement element)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("data") && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in property.Value.EnumerateObject())
                {
                    data[item.Name] = ConvertJsonValue(item.Value);
                }

                continue;
            }

            if (ExplicitChildContainerNames.Contains(property.Name) || StructuralPropertyNames.Contains(property.Name))
            {
                continue;
            }

            if (LooksLikeHealthNode(property.Value) || LooksLikeHealthCollection(property.Value))
            {
                continue;
            }

            data[property.Name] = ConvertJsonValue(property.Value);
        }

        return data;
    }

    private static List<HealthNode> ApplyFilters(
        IEnumerable<HealthNode> nodes,
        IEnumerable<string> includeChecks,
        IEnumerable<string> excludeChecks)
    {
        var includeSet = includeChecks
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excludeSet = excludeChecks
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var includeAll = includeSet.Count == 0;

        return nodes
            .Select(node => FilterNode(node, includeSet, excludeSet, includeAll))
            .Where(static node => node is not null)
            .Cast<HealthNode>()
            .ToList();
    }

    private static HealthNode? FilterNode(
        HealthNode node,
        HashSet<string> includeSet,
        HashSet<string> excludeSet,
        bool includeAll)
    {
        if (excludeSet.Contains(node.Name))
        {
            return null;
        }

        var filteredChildren = node.Children
            .Select(child => FilterNode(child, includeSet, excludeSet, includeAll))
            .Where(static child => child is not null)
            .Cast<HealthNode>()
            .ToList();

        if (includeAll || includeSet.Contains(node.Name) || filteredChildren.Count > 0)
        {
            return new HealthNode
            {
                Name = node.Name,
                Status = node.Status,
                Description = node.Description,
                ErrorMessage = node.ErrorMessage,
                DurationText = node.DurationText,
                Data = new Dictionary<string, object?>(node.Data),
                Children = filteredChildren
            };
        }

        return null;
    }

    private static bool LooksLikeHealthNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (StructuralPropertyNames.Contains(property.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeHealthCollection(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(static item => item.ValueKind == JsonValueKind.Object);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                (LooksLikeHealthNode(property.Value) || LooksLikeHealthCollection(property.Value)))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractStatus(JsonElement element)
    {
        return GetString(element, "status") switch
        {
            null => null,
            var status => NormalizeStatus(status)
        };
    }

    private static string AggregateStatus(IEnumerable<string> statuses)
    {
        var highestSeverity = 0;
        var resolvedStatus = "Unknown";

        foreach (var status in statuses)
        {
            var normalized = NormalizeStatus(status);
            var severity = normalized switch
            {
                "Unhealthy" => 4,
                "Degraded" => 3,
                "Healthy" => 2,
                _ => 1
            };

            if (severity > highestSeverity)
            {
                highestSeverity = severity;
                resolvedStatus = normalized;
            }
        }

        return resolvedStatus;
    }

    private static string NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "healthy" => "Healthy",
            "degraded" => "Degraded",
            "unhealthy" => "Unhealthy",
            "unknown" => "Unknown",
            _ => value.Trim()
        };
    }

    private static string? ExtractName(JsonElement element)
    {
        return GetString(element, "name");
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static object? ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    static property => ConvertJsonValue(property.Value),
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(ConvertJsonValue)
                .ToList(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var intValue) => intValue,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }
}
