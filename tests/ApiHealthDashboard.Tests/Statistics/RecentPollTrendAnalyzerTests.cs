using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Statistics;

namespace ApiHealthDashboard.Tests.Statistics;

public sealed class RecentPollTrendAnalyzerTests
{
    [Fact]
    public void Analyze_AllFailedUnknownSamples_ReturnsFailingTrend()
    {
        var samples = new[]
        {
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 0, 0, TimeSpan.Zero),
                Status = "Unknown",
                DurationMs = 120,
                ResultKind = "Timeout",
                ErrorSummary = "Timed out"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 1, 0, TimeSpan.Zero),
                Status = "Unknown",
                DurationMs = 130,
                ResultKind = "NetworkError",
                ErrorSummary = "Connection refused"
            }
        };

        var result = RecentPollTrendAnalyzer.Analyze(samples);

        Assert.Equal(RecentPollTrendKind.Failing, result.TrendKind);
        Assert.Empty(result.Transitions);
    }

    [Fact]
    public void Analyze_StableSuccessfulSamples_RemainsStable()
    {
        var samples = new[]
        {
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 0, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 90,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 1, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 95,
                ResultKind = "Success"
            }
        };

        var result = RecentPollTrendAnalyzer.Analyze(samples);

        Assert.Equal(RecentPollTrendKind.Stable, result.TrendKind);
    }
}
