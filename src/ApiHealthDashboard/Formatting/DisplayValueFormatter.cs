using System.Collections;
using System.Text.Json;

namespace ApiHealthDashboard.Formatting;

public static class DisplayValueFormatter
{
    public static string Format(object? value)
    {
        return value switch
        {
            null => "(null)",
            string text when string.IsNullOrWhiteSpace(text) => "(empty)",
            string text => text,
            IEnumerable values => FormatEnumerable(values),
            _ => JsonSerializer.Serialize(value)
        };
    }

    private static string FormatEnumerable(IEnumerable values)
    {
        var items = values.Cast<object?>().ToList();
        return items.Count == 0
            ? "(empty)"
            : JsonSerializer.Serialize(items);
    }
}
