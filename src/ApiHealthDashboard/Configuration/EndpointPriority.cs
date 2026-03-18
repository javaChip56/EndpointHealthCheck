namespace ApiHealthDashboard.Configuration;

public static class EndpointPriority
{
    public const string Critical = "Critical";
    public const string High = "High";
    public const string Normal = "Normal";
    public const string Low = "Low";

    public static IReadOnlyList<string> AllowedValues { get; } =
    [
        Critical,
        High,
        Normal,
        Low
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Normal;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "critical" => Critical,
            "high" => High,
            "normal" => Normal,
            "low" => Low,
            _ => value.Trim()
        };
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = Normalize(value);
        return AllowedValues.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static int GetSortOrder(string? value)
    {
        return Normalize(value) switch
        {
            Critical => 4,
            High => 3,
            Normal => 2,
            Low => 1,
            _ => 0
        };
    }
}
