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

    [Fact]
    public void Analyze_WhenRecentSamplesSettleIntoBetterStatus_ChangesFromFlappingToImproving()
    {
        var samples = new[]
        {
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 0, 0, TimeSpan.Zero),
                Status = "Degraded",
                DurationMs = 90,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 1, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 92,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 2, 0, TimeSpan.Zero),
                Status = "Degraded",
                DurationMs = 94,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 3, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 91,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 4, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 89,
                ResultKind = "Success"
            }
        };

        var result = RecentPollTrendAnalyzer.Analyze(samples);

        Assert.Equal(RecentPollTrendKind.Improving, result.TrendKind);
    }

    [Fact]
    public void Analyze_WhenImprovingStreakSettlesIntoSameStatus_ReturnsStable()
    {
        var samples = new[]
        {
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 0, 0, TimeSpan.Zero),
                Status = "Unknown",
                DurationMs = 90,
                ResultKind = "HttpError",
                ErrorSummary = "Endpoint returned HTTP 404 (NotFound)."
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 1, 0, TimeSpan.Zero),
                Status = "Unknown",
                DurationMs = 92,
                ResultKind = "HttpError",
                ErrorSummary = "Endpoint returned HTTP 404 (NotFound)."
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 2, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 30,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 3, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 29,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 4, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 28,
                ResultKind = "Success"
            },
            new RecentPollSample
            {
                CheckedUtc = new DateTimeOffset(2026, 03, 19, 0, 5, 0, TimeSpan.Zero),
                Status = "Healthy",
                DurationMs = 27,
                ResultKind = "Success"
            }
        };

        var result = RecentPollTrendAnalyzer.Analyze(samples);

        Assert.Equal(RecentPollTrendKind.Stable, result.TrendKind);
    }
}
