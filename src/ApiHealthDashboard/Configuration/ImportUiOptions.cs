namespace ApiHealthDashboard.Configuration;

public sealed class ImportUiOptions
{
    public const string SectionName = "Import";

    public int MinimumRecommendedPollFrequencySeconds { get; set; } = 180;
}
