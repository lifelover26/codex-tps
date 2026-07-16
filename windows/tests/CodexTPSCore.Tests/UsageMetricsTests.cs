using System;
using Xunit;

namespace CodexTPSCore.Tests;

public class UsageMetricsTests
{
    [Fact]
    public void TestRollingWindowsUseFixedWindowDenominators()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1000);
        var recent = Event(now.AddSeconds(-30), total: 600, input: 500, cached: 400, output: 100);
        var older = Event(now.AddSeconds(-120), total: 1500, input: 1200, cached: 800, output: 300);

        var snapshot = UsageMetricsCalculator.Snapshot(
            events: new[] { recent, older },
            now: now,
            activeSessions: 2,
            malformedRelevantLines: 0
        );

        Assert.Equal(10.0, snapshot.OneMinute.TokensPerSecond, 3);
        Assert.Equal(1.0, snapshot.OneMinute.RequestsPerMinute, 3);
        Assert.Equal(0.8, snapshot.OneMinute.CacheRatio, 3);

        Assert.Equal(7.0, snapshot.FiveMinutes.TokensPerSecond, 3);
        Assert.Equal(0.4, snapshot.FiveMinutes.RequestsPerMinute, 3);

        Assert.Equal(7.0 / 6.0, snapshot.ThirtyMinutes.TokensPerSecond, 3);
        Assert.Equal(1.0 / 15.0, snapshot.ThirtyMinutes.RequestsPerMinute, 3);

        Assert.Equal(7.0 / 12.0, snapshot.OneHour.TokensPerSecond, 3);
        Assert.Equal(1.0 / 30.0, snapshot.OneHour.RequestsPerMinute, 3);

        Assert.Equal(2, snapshot.ActiveSessions);
    }

    private UsageEvent Event(
        DateTimeOffset date,
        long total,
        long input,
        long cached,
        long output)
    {
        return new UsageEvent(
            Timestamp: date,
            Usage: new TokenUsage(
                inputTokens: input,
                cachedInputTokens: cached,
                outputTokens: output,
                totalTokens: total
            ),
            SessionID: Guid.NewGuid().ToString(),
            DeduplicationKey: Guid.NewGuid().ToString()
        );
    }
}
