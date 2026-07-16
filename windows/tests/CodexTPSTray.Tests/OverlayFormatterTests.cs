using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayFormatterTests
{
    private UsageSnapshot CreateTestSnapshot()
    {
        return new UsageSnapshot(
            GeneratedAt: DateTimeOffset.UtcNow,
            OneMinute: new WindowMetrics(60, 10, 10.0, 19600, 10000, 9800, 5000, 4600, 0.98, 19600),
            FiveMinutes: new WindowMetrics(300, 50, 10.0, 19600, 10000, 9800, 5000, 4600, 0.98, 19600),
            ThirtyMinutes: new WindowMetrics(1800, 300, 10.0, 19600, 10000, 9800, 5000, 4600, 0.98, 19600),
            OneHour: new WindowMetrics(3600, 600, 10.0, 19600, 10000, 9800, 5000, 4600, 0.98, 19600),
            ActiveSessions: 4,
            MalformedRelevantLines: 0,
            Status: CollectionStatus.Ready
        );
    }

    [Fact]
    public void FormatOverlay_English_OneMinute_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.OneMinute, Language.English);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("1 min", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 req/min", lines[3]);
        Assert.Equal("4 sessions", lines[4]);
        Assert.Equal("98% cache", lines[5]);
    }

    [Fact]
    public void FormatOverlay_English_FiveMinutes_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.FiveMinutes, Language.English);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("5 min", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 req/min", lines[3]);
        Assert.Equal("4 sessions", lines[4]);
        Assert.Equal("98% cache", lines[5]);
    }

    [Fact]
    public void FormatOverlay_English_ThirtyMinutes_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.ThirtyMinutes, Language.English);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("30 min", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 req/min", lines[3]);
        Assert.Equal("4 sessions", lines[4]);
        Assert.Equal("98% cache", lines[5]);
    }

    [Fact]
    public void FormatOverlay_English_OneHour_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.OneHour, Language.English);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("1 hr", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 req/min", lines[3]);
        Assert.Equal("4 sessions", lines[4]);
        Assert.Equal("98% cache", lines[5]);
    }

    [Fact]
    public void FormatOverlay_Chinese_OneMinute_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.OneMinute, Language.Chinese);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("1 分钟", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 请求/分钟", lines[3]);
        Assert.Equal("4 个会话", lines[4]);
        Assert.Equal("98% 缓存", lines[5]);
    }

    [Fact]
    public void FormatOverlay_Chinese_FiveMinutes_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.FiveMinutes, Language.Chinese);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("5 分钟", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 请求/分钟", lines[3]);
        Assert.Equal("4 个会话", lines[4]);
        Assert.Equal("98% 缓存", lines[5]);
    }

    [Fact]
    public void FormatOverlay_Chinese_ThirtyMinutes_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.ThirtyMinutes, Language.Chinese);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("30 分钟", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 请求/分钟", lines[3]);
        Assert.Equal("4 个会话", lines[4]);
        Assert.Equal("98% 缓存", lines[5]);
    }

    [Fact]
    public void FormatOverlay_Chinese_OneHour_ReturnsCorrectLines()
    {
        var snapshot = CreateTestSnapshot();
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.OneHour, Language.Chinese);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("1 小时", lines[1]);
        Assert.Equal("19.6K token/s", lines[2]);
        Assert.Equal("10.0 请求/分钟", lines[3]);
        Assert.Equal("4 个会话", lines[4]);
        Assert.Equal("98% 缓存", lines[5]);
    }

    [Fact]
    public void FormatOverlay_EmptySnapshot_ZeroValues()
    {
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready);
        var lines = OverlayFormatter.FormatOverlay(snapshot, MetricWindow.OneMinute, Language.English);

        Assert.Equal(6, lines.Length);
        Assert.Equal("Codex TPS", lines[0]);
        Assert.Equal("1 min", lines[1]);
        Assert.Equal("0 token/s", lines[2]);
        Assert.Equal("0 req/min", lines[3]);
        Assert.Equal("0 sessions", lines[4]);
        Assert.Equal("0% cache", lines[5]);
    }
}