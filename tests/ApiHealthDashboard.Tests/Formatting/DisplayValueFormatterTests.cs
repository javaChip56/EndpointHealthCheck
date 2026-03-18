using ApiHealthDashboard.Formatting;

namespace ApiHealthDashboard.Tests.Formatting;

public sealed class DisplayValueFormatterTests
{
    [Fact]
    public void Format_WithEmptyList_ReturnsEmptyMarker()
    {
        var result = DisplayValueFormatter.Format(new List<object>());

        Assert.Equal("(empty)", result);
    }

    [Fact]
    public void Format_WithNonEmptyList_ReturnsJsonArray()
    {
        var result = DisplayValueFormatter.Format(new List<object?> { "core", "orders" });

        Assert.Equal("[\"core\",\"orders\"]", result);
    }
}
