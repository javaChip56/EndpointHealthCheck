namespace ApiHealthDashboard.Domain;

public sealed class HealthNode
{
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "Unknown";

    public string? Description { get; set; }

    public string? ErrorMessage { get; set; }

    public string? DurationText { get; set; }

    public Dictionary<string, object?> Data { get; set; } = new();

    public List<HealthNode> Children { get; set; } = new();

    public HealthNode Clone()
    {
        return new HealthNode
        {
            Name = Name,
            Status = Status,
            Description = Description,
            ErrorMessage = ErrorMessage,
            DurationText = DurationText,
            Data = new Dictionary<string, object?>(Data),
            Children = Children.Select(static child => child.Clone()).ToList()
        };
    }
}
