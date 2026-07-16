using System;
using System.Globalization;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

public class TooltipFormatTests
{
    [Fact]
    public void FormatTooltip_NullSnapshot_ReturnsDefault()
    {
        string result = TrayTextFormatter.FormatTooltip(null, MetricWindow.OneMinute);

        Assert.Equal("Codex TPS", result);
        Assert.True(result.Length <= 63);
    }

    [Fact]
    public void FormatTooltip_NeverExceeds63Characters()
    {
        var snapshot = CreateSnapshot(
            tps: 9999.9,
            status: CollectionStatus.SessionsDirectoryMissing,
            activeSessions: 999
        );

        foreach (MetricWindow window in Enum.GetValues<MetricWindow>())
        {
            string result = TrayTextFormatter.FormatTooltip(snapshot, window);
            Assert.True(result.Length <= 63, $"Tooltip exceeds 63 chars for {window}: '{result}' (length: {result.Length})");
        }
    }

    [Fact]
    public void FormatTooltip_ZeroActivity()
    {
        var snapshot = CreateSnapshot(
            tps: 0.0,
            status: CollectionStatus.Ready,
            activeSessions: 0
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.OneMinute);

        Assert.True(result.Length <= 63);
        Assert.Contains("TPS:", result);
    }

    [Fact]
    public void FormatTooltip_LargeNumbers()
    {
        var snapshot = CreateSnapshot(
            tps: 1000000.0,
            status: CollectionStatus.Ready,
            activeSessions: 999
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.OneHour);

        Assert.True(result.Length <= 63);
    }

    [Fact]
    public void FormatTooltip_LongStatus()
    {
        var snapshot = CreateSnapshot(
            tps: 100.0,
            status: CollectionStatus.SessionsDirectoryMissing,
            activeSessions: 100
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.ThirtyMinutes);

        Assert.True(result.Length <= 63);
        Assert.Contains("No sessions", result);
    }

    [Fact]
    public void FormatTooltip_ShortStatus()
    {
        var snapshot = CreateSnapshot(
            tps: 50.0,
            status: CollectionStatus.Ready,
            activeSessions: 5
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.FiveMinutes);

        Assert.True(result.Length <= 63);
        Assert.Contains("Ready", result);
    }

    [Fact]
    public void FormatTooltip_ErrorStatus()
    {
        var snapshot = CreateSnapshot(
            tps: 0.0,
            status: CollectionStatus.ReadFailed,
            activeSessions: 0
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.OneMinute);

        Assert.True(result.Length <= 63);
        Assert.Contains("Error", result);
    }

    [Fact]
    public void GetWindowShortText_ReturnsCorrectValues()
    {
        Assert.Equal("1m", TrayTextFormatter.GetWindowShortText(MetricWindow.OneMinute));
        Assert.Equal("5m", TrayTextFormatter.GetWindowShortText(MetricWindow.FiveMinutes));
        Assert.Equal("30m", TrayTextFormatter.GetWindowShortText(MetricWindow.ThirtyMinutes));
        Assert.Equal("1h", TrayTextFormatter.GetWindowShortText(MetricWindow.OneHour));
    }

    [Fact]
    public void GetStatusText_ReturnsCorrectValues()
    {
        Assert.Equal("Ready", TrayTextFormatter.GetStatusText(CollectionStatus.Ready));
        Assert.Equal("No sessions", TrayTextFormatter.GetStatusText(CollectionStatus.SessionsDirectoryMissing));
        Assert.Equal("Error", TrayTextFormatter.GetStatusText(CollectionStatus.ReadFailed));
    }

    [Fact]
    public void FormatTps_UsesInvariantCulture()
    {
        double tps = 123.456;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string result = TrayTextFormatter.FormatTps(tps);

            Assert.Equal("123.5", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void FormatTooltip_English_NeverExceeds63Characters()
    {
        var snapshot = CreateSnapshot(
            tps: 9999.9,
            status: CollectionStatus.SessionsDirectoryMissing,
            activeSessions: 999
        );

        foreach (MetricWindow window in Enum.GetValues<MetricWindow>())
        {
            string result = TrayTextFormatter.FormatTooltip(snapshot, window, Language.English);
            Assert.True(result.Length <= 63, $"English tooltip exceeds 63 chars for {window}: '{result}' (length: {result.Length})");
        }
    }

    [Fact]
    public void FormatTooltip_Chinese_NeverExceeds63Characters()
    {
        var snapshot = CreateSnapshot(
            tps: 9999.9,
            status: CollectionStatus.SessionsDirectoryMissing,
            activeSessions: 999
        );

        foreach (MetricWindow window in Enum.GetValues<MetricWindow>())
        {
            string result = TrayTextFormatter.FormatTooltip(snapshot, window, Language.Chinese);
            Assert.True(result.Length <= 63, $"Chinese tooltip exceeds 63 chars for {window}: '{result}' (length: {result.Length})");
        }
    }

    [Fact]
    public void FormatTooltip_EnglishStatusText()
    {
        var snapshot = CreateSnapshot(
            tps: 100.0,
            status: CollectionStatus.Ready,
            activeSessions: 5
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.OneMinute, Language.English);
        Assert.Contains("Ready", result);
        Assert.True(result.Length <= 63);
    }

    [Fact]
    public void FormatTooltip_ChineseStatusText()
    {
        var snapshot = CreateSnapshot(
            tps: 100.0,
            status: CollectionStatus.Ready,
            activeSessions: 5
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.OneMinute, Language.Chinese);
        Assert.Contains("就绪", result);
        Assert.True(result.Length <= 63);
    }

    [Fact]
    public void FormatTooltip_ChineseNoSessionsStatus()
    {
        var snapshot = CreateSnapshot(
            tps: 0.0,
            status: CollectionStatus.SessionsDirectoryMissing,
            activeSessions: 0
        );

        string result = TrayTextFormatter.FormatTooltip(snapshot, MetricWindow.OneMinute, Language.Chinese);
        Assert.Contains("未找到会话目录", result);
        Assert.True(result.Length <= 63);
    }

    private static UsageSnapshot CreateSnapshot(double tps, CollectionStatus status, int activeSessions)
    {
        var metrics = new WindowMetrics(
            WindowSeconds: 60,
            RequestCount: 10,
            RequestsPerMinute: 10.0,
            TokensPerSecond: tps,
            InputTokensPerSecond: tps * 0.5,
            CachedInputTokensPerSecond: tps * 0.2,
            OutputTokensPerSecond: tps * 0.3,
            ReasoningTokensPerSecond: tps * 0.1,
            CacheRatio: 0.4,
            TotalTokens: (long)(tps * 60)
        );

        return new UsageSnapshot(
            GeneratedAt: DateTimeOffset.UtcNow,
            OneMinute: metrics,
            FiveMinutes: metrics,
            ThirtyMinutes: metrics,
            OneHour: metrics,
            ActiveSessions: activeSessions,
            MalformedRelevantLines: 0,
            Status: status
        );
    }
}