using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.Statistics;

public static class RecentPollTrendAnalyzer
{
    public static RecentPollTrendAnalysis Analyze(IEnumerable<RecentPollSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var orderedSamples = samples
            .Where(static sample => sample is not null)
            .OrderBy(static sample => sample.CheckedUtc)
            .ToArray();

        if (orderedSamples.Length == 0)
        {
            return RecentPollTrendAnalysis.Empty;
        }

        var transitions = new List<RecentPollStatusTransition>();

        for (var index = 1; index < orderedSamples.Length; index++)
        {
            var previousStatus = NormalizeStatus(orderedSamples[index - 1].Status);
            var currentStatus = NormalizeStatus(orderedSamples[index].Status);

            if (string.Equals(previousStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            transitions.Add(new RecentPollStatusTransition
            {
                FromStatus = previousStatus,
                ToStatus = currentStatus,
                ChangedUtc = orderedSamples[index].CheckedUtc
            });
        }

        return new RecentPollTrendAnalysis
        {
            TrendKind = ResolveTrendKind(orderedSamples, transitions),
            Transitions = transitions
        };
    }

    private static RecentPollTrendKind ResolveTrendKind(
        IReadOnlyList<RecentPollSample> orderedSamples,
        IReadOnlyCollection<RecentPollStatusTransition> transitions)
    {
        if (orderedSamples.Count < 2)
        {
            return RecentPollTrendKind.InsufficientData;
        }

        if (orderedSamples.All(IsFailedSample))
        {
            return RecentPollTrendKind.Failing;
        }

        if (transitions.Count == 0)
        {
            return RecentPollTrendKind.Stable;
        }

        var currentStatus = NormalizeStatus(orderedSamples[^1].Status);
        var currentStreakLength = GetTrailingStatusStreakLength(orderedSamples, currentStatus);
        var previousDifferentStatus = GetPreviousDifferentStatus(orderedSamples, currentStatus);

        if (currentStreakLength >= 2 && previousDifferentStatus is not null)
        {
            var previousRank = GetStatusRank(previousDifferentStatus);
            var currentRank = GetStatusRank(currentStatus);

            if (currentRank > previousRank)
            {
                return RecentPollTrendKind.Improving;
            }

            if (currentRank < previousRank)
            {
                return RecentPollTrendKind.Worsening;
            }
        }

        if (transitions.Count >= 3)
        {
            return RecentPollTrendKind.Flapping;
        }

        var firstRank = GetStatusRank(orderedSamples[0].Status);
        var lastRank = GetStatusRank(orderedSamples[^1].Status);

        if (lastRank > firstRank)
        {
            return RecentPollTrendKind.Improving;
        }

        if (lastRank < firstRank)
        {
            return RecentPollTrendKind.Worsening;
        }

        return RecentPollTrendKind.Flapping;
    }

    private static int GetTrailingStatusStreakLength(IReadOnlyList<RecentPollSample> orderedSamples, string currentStatus)
    {
        var streakLength = 0;

        for (var index = orderedSamples.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(NormalizeStatus(orderedSamples[index].Status), currentStatus, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            streakLength++;
        }

        return streakLength;
    }

    private static string? GetPreviousDifferentStatus(IReadOnlyList<RecentPollSample> orderedSamples, string currentStatus)
    {
        for (var index = orderedSamples.Count - 1; index >= 0; index--)
        {
            var sampleStatus = NormalizeStatus(orderedSamples[index].Status);
            if (!string.Equals(sampleStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
            {
                return sampleStatus;
            }
        }

        return null;
    }

    private static bool IsFailedSample(RecentPollSample sample)
    {
        return !string.Equals(sample.ResultKind, "Success", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sample.ErrorSummary);
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : status.Trim();
    }

    private static int GetStatusRank(string? status)
    {
        return NormalizeStatus(status) switch
        {
            "Healthy" => 3,
            "Degraded" => 2,
            "Unhealthy" => 1,
            _ => 0
        };
    }
}

public sealed class RecentPollTrendAnalysis
{
    public static RecentPollTrendAnalysis Empty { get; } = new();

    public RecentPollTrendKind TrendKind { get; init; }

    public IReadOnlyList<RecentPollStatusTransition> Transitions { get; init; } = [];

    public bool HasTransitions => Transitions.Count > 0;
}

public sealed class RecentPollStatusTransition
{
    public required string FromStatus { get; init; }

    public required string ToStatus { get; init; }

    public required DateTimeOffset ChangedUtc { get; init; }
}

public enum RecentPollTrendKind
{
    InsufficientData = 0,
    Stable = 1,
    Improving = 2,
    Worsening = 3,
    Flapping = 4,
    Failing = 5
}
