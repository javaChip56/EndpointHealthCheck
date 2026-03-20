using ApiHealthDashboard.Domain;

namespace ApiHealthDashboard.Statistics;

public static class RecentPollSampleMetricsCalculator
{
    public static RecentPollSampleMetrics Calculate(IEnumerable<RecentPollSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var orderedSamples = samples
            .Where(static sample => sample is not null)
            .OrderBy(static sample => sample.CheckedUtc)
            .ToArray();

        if (orderedSamples.Length == 0)
        {
            return RecentPollSampleMetrics.Empty;
        }

        var successCount = orderedSamples.Count(IsSuccessfulSample);
        var failureCount = orderedSamples.Length - successCount;
        var averageDurationMs = (long)Math.Round(
            orderedSamples.Average(static sample => sample.DurationMs),
            MidpointRounding.AwayFromZero);

        return new RecentPollSampleMetrics
        {
            SampleCount = orderedSamples.Length,
            SuccessCount = successCount,
            FailureCount = failureCount,
            AverageDurationMs = averageDurationMs,
            LastStatusChangeUtc = ResolveLastStatusChangeUtc(orderedSamples)
        };
    }

    private static bool IsSuccessfulSample(RecentPollSample sample)
    {
        return string.Equals(sample.ResultKind, "Success", StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrWhiteSpace(sample.ErrorSummary);
    }

    private static DateTimeOffset? ResolveLastStatusChangeUtc(IReadOnlyList<RecentPollSample> orderedSamples)
    {
        if (orderedSamples.Count < 2)
        {
            return null;
        }

        var currentStatus = NormalizeStatus(orderedSamples[^1].Status);
        var streakStartUtc = orderedSamples[^1].CheckedUtc;
        var sawDifferentStatus = false;

        for (var index = orderedSamples.Count - 2; index >= 0; index--)
        {
            var sampleStatus = NormalizeStatus(orderedSamples[index].Status);
            if (string.Equals(sampleStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
            {
                streakStartUtc = orderedSamples[index].CheckedUtc;
                continue;
            }

            sawDifferentStatus = true;
            break;
        }

        return sawDifferentStatus ? streakStartUtc : null;
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : status.Trim();
    }
}

public sealed class RecentPollSampleMetrics
{
    public static RecentPollSampleMetrics Empty { get; } = new();

    public int SampleCount { get; init; }

    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public long AverageDurationMs { get; init; }

    public DateTimeOffset? LastStatusChangeUtc { get; init; }

    public bool HasSamples => SampleCount > 0;

    public int SuccessRatePercent => SampleCount == 0
        ? 0
        : (int)Math.Round((double)SuccessCount * 100 / SampleCount, MidpointRounding.AwayFromZero);
}
