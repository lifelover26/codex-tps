using System;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

public class MetricWindowTests
{
    [Fact]
    public void GetMetrics_MapsToCorrectWindow()
    {
        var snapshot = CreateDistinctSnapshot();

        Assert.Equal(10.0, MetricWindow.OneMinute.GetMetrics(snapshot).TokensPerSecond, 3);
        Assert.Equal(20.0, MetricWindow.FiveMinutes.GetMetrics(snapshot).TokensPerSecond, 3);
        Assert.Equal(30.0, MetricWindow.ThirtyMinutes.GetMetrics(snapshot).TokensPerSecond, 3);
        Assert.Equal(40.0, MetricWindow.OneHour.GetMetrics(snapshot).TokensPerSecond, 3);
    }

    [Fact]
    public void GetDisplayName_ReturnsCorrectValues()
    {
        Assert.Equal("1 Minute", MetricWindow.OneMinute.GetDisplayName());
        Assert.Equal("5 Minutes", MetricWindow.FiveMinutes.GetDisplayName());
        Assert.Equal("30 Minutes", MetricWindow.ThirtyMinutes.GetDisplayName());
        Assert.Equal("1 Hour", MetricWindow.OneHour.GetDisplayName());
    }

    [Fact]
    public void GetSeconds_ReturnsCorrectValues()
    {
        Assert.Equal(60, MetricWindow.OneMinute.GetSeconds());
        Assert.Equal(300, MetricWindow.FiveMinutes.GetSeconds());
        Assert.Equal(1800, MetricWindow.ThirtyMinutes.GetSeconds());
        Assert.Equal(3600, MetricWindow.OneHour.GetSeconds());
    }

    private static UsageSnapshot CreateDistinctSnapshot()
    {
        return new UsageSnapshot(
            GeneratedAt: DateTimeOffset.UtcNow,
            OneMinute: CreateWindowMetrics(10.0),
            FiveMinutes: CreateWindowMetrics(20.0),
            ThirtyMinutes: CreateWindowMetrics(30.0),
            OneHour: CreateWindowMetrics(40.0),
            ActiveSessions: 1,
            MalformedRelevantLines: 0,
            Status: CollectionStatus.Ready
        );
    }

    private static WindowMetrics CreateWindowMetrics(double tps)
    {
        return new WindowMetrics(
            WindowSeconds: 60,
            RequestCount: 1,
            RequestsPerMinute: 1.0,
            TokensPerSecond: tps,
            InputTokensPerSecond: tps * 0.5,
            CachedInputTokensPerSecond: tps * 0.2,
            OutputTokensPerSecond: tps * 0.3,
            ReasoningTokensPerSecond: tps * 0.1,
            CacheRatio: 0.4,
            TotalTokens: (long)tps
        );
    }
}