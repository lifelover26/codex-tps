using System;
using System.Globalization;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

public class MonitorPanelViewModelTests
{
    [Fact]
    public void FormatCompactNumber_Zero_ReturnsZero()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(0);
        Assert.Equal("0", result);
    }

    [Fact]
    public void FormatCompactNumber_SmallValue_ReturnsTwoDecimalPlaces()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(0.5);
        Assert.Equal("0.50", result);
    }

    [Fact]
    public void FormatCompactNumber_NormalValue_ReturnsOneDecimalPlace()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(12.5);
        Assert.Equal("12.5", result);
    }

    [Fact]
    public void FormatCompactNumber_Thousands_ReturnsKiloFormat()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(1500);
        Assert.Equal("1.5K", result);
    }

    [Fact]
    public void FormatCompactNumber_Millions_ReturnsMegaFormat()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(2500000);
        Assert.Equal("2.5M", result);
    }

    [Fact]
    public void FormatCompactNumber_NaN_ReturnsZero()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(double.NaN);
        Assert.Equal("0", result);
    }

    [Fact]
    public void FormatCompactNumber_Infinity_ReturnsZero()
    {
        string result = MonitorPanelViewModel.FormatCompactNumber(double.PositiveInfinity);
        Assert.Equal("0", result);
    }

    [Fact]
    public void FormatStatus_Reading_ReturnsReading()
    {
        string result = MonitorPanelViewModel.FormatStatus(CollectionStatus.Ready, true, 0, Language.English);
        Assert.Equal("Reading", result);
    }

    [Fact]
    public void FormatStatus_Ready_ReturnsReady()
    {
        string result = MonitorPanelViewModel.FormatStatus(CollectionStatus.Ready, false, 0, Language.English);
        Assert.Equal("Ready", result);
    }

    [Fact]
    public void FormatStatus_ReadyWithMalformed_ReturnsReadyWithCount()
    {
        string result = MonitorPanelViewModel.FormatStatus(CollectionStatus.Ready, false, 5, Language.English);
        Assert.Equal("Ready (5 malformed)", result);
    }

    [Fact]
    public void FormatStatus_NoSessions_ReturnsNoSessions()
    {
        string result = MonitorPanelViewModel.FormatStatus(CollectionStatus.SessionsDirectoryMissing, false, 0, Language.English);
        Assert.Equal("No sessions", result);
    }

    [Fact]
    public void FormatStatus_Error_ReturnsError()
    {
        string result = MonitorPanelViewModel.FormatStatus(CollectionStatus.ReadFailed, false, 0, Language.English);
        Assert.Equal("Error", result);
    }

    [Fact]
    public void ViewModel_InitialState_HasDefaultValues()
    {
        var viewModel = new MonitorPanelViewModel(TraySettings.Default);

        Assert.Equal("Waiting", viewModel.StatusText);
        Assert.Equal("", viewModel.LastUpdateTimeText);
        Assert.Equal("0", viewModel.TotalTpsText);
        Assert.Equal("0", viewModel.InputTpsText);
        Assert.Equal("0", viewModel.CachedTpsText);
        Assert.Equal("0", viewModel.OutputTpsText);
        Assert.Equal("0", viewModel.ReasoningTpsText);
        Assert.Equal("0", viewModel.RequestsPerMinuteText);
        Assert.Equal("0", viewModel.ActiveSessionsText);
        Assert.Equal("0%", viewModel.CacheRatioText);
        Assert.Equal("1 min", viewModel.SelectedWindowDisplayName);
        Assert.Equal("15 Seconds", viewModel.SelectedCadenceDisplayName);
    }

    [Fact]
    public void ViewModel_UpdateSnapshot_UpdatesAllProperties()
    {
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready);
        var viewModel = new MonitorPanelViewModel(TraySettings.Default);

        viewModel.UpdateSnapshot(snapshot);

        Assert.Equal("Ready", viewModel.StatusText);
        Assert.NotEqual("", viewModel.LastUpdateTimeText);
        Assert.Equal("0", viewModel.TotalTpsText);
        Assert.Equal("0", viewModel.ActiveSessionsText);
    }

    [Fact]
    public void ViewModel_UpdateSettings_UpdatesDisplayNames()
    {
        var viewModel = new MonitorPanelViewModel(TraySettings.Default);
        var newSettings = new TraySettings(MetricWindow.OneHour, RefreshCadence.FiveSeconds);

        viewModel.UpdateSettings(newSettings);

        Assert.Equal("1 hour", viewModel.SelectedWindowDisplayName);
        Assert.Equal("5 Seconds", viewModel.SelectedCadenceDisplayName);
    }

    [Fact]
    public void ViewModel_IsRefreshing_UpdatesStatusText()
    {
        var viewModel = new MonitorPanelViewModel(TraySettings.Default);

        viewModel.IsRefreshing = true;
        Assert.Equal("Reading", viewModel.StatusText);

        viewModel.IsRefreshing = false;
        Assert.Equal("Waiting", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_WithRealMetrics_FormatsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new UsageSnapshot(
            GeneratedAt: now,
            OneMinute: new WindowMetrics(
                WindowSeconds: 60,
                RequestCount: 10,
                RequestsPerMinute: 10,
                TokensPerSecond: 150.5,
                InputTokensPerSecond: 50.25,
                CachedInputTokensPerSecond: 10.5,
                OutputTokensPerSecond: 80.0,
                ReasoningTokensPerSecond: 10.0,
                CacheRatio: 0.2,
                TotalTokens: 9030
            ),
            FiveMinutes: WindowMetrics.Empty(300),
            ThirtyMinutes: WindowMetrics.Empty(1800),
            OneHour: WindowMetrics.Empty(3600),
            ActiveSessions: 3,
            MalformedRelevantLines: 0,
            Status: CollectionStatus.Ready
        );

        var viewModel = new MonitorPanelViewModel(TraySettings.Default);
        viewModel.UpdateSnapshot(snapshot);

        Assert.Equal("150.5", viewModel.TotalTpsText);
        Assert.Equal("50.2", viewModel.InputTpsText);
        Assert.Equal("10.5", viewModel.CachedTpsText);
        Assert.Equal("80.0", viewModel.OutputTpsText);
        Assert.Equal("10.0", viewModel.ReasoningTpsText);
        Assert.Equal("10.0", viewModel.RequestsPerMinuteText);
        Assert.Equal("3", viewModel.ActiveSessionsText);
        Assert.Equal("20%", viewModel.CacheRatioText);
    }

    [Fact]
    public void FormatCompactNumber_UsesInvariantCulture_RegardlessOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            string result = MonitorPanelViewModel.FormatCompactNumber(1234.5);

            Assert.Equal("1.2K", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void ViewModel_UsesInvariantCulture_RegardlessOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");

            var now = DateTimeOffset.UtcNow;
            var snapshot = new UsageSnapshot(
                GeneratedAt: now,
                OneMinute: new WindowMetrics(
                    WindowSeconds: 60,
                    RequestCount: 10,
                    RequestsPerMinute: 10.5,
                    TokensPerSecond: 150.5,
                    InputTokensPerSecond: 50.25,
                    CachedInputTokensPerSecond: 0,
                    OutputTokensPerSecond: 100.25,
                    ReasoningTokensPerSecond: 0,
                    CacheRatio: 0.25,
                    TotalTokens: 9030
                ),
                FiveMinutes: WindowMetrics.Empty(300),
                ThirtyMinutes: WindowMetrics.Empty(1800),
                OneHour: WindowMetrics.Empty(3600),
                ActiveSessions: 3,
                MalformedRelevantLines: 0,
                Status: CollectionStatus.Ready
            );

            var viewModel = new MonitorPanelViewModel(TraySettings.Default);
            viewModel.UpdateSnapshot(snapshot);

            Assert.Equal("150.5", viewModel.TotalTpsText);
            Assert.Equal("10.5", viewModel.RequestsPerMinuteText);
            Assert.Equal("25%", viewModel.CacheRatioText);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void ViewModel_EnglishLocalization_HasCorrectLabels()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("Input", viewModel.InputLabel);
        Assert.Equal("Cached", viewModel.CachedLabel);
        Assert.Equal("Output", viewModel.OutputLabel);
        Assert.Equal("Reasoning", viewModel.ReasoningLabel);
        Assert.Equal("Active Sessions", viewModel.ActiveSessionsLabel);
        Assert.Equal("Cache Ratio", viewModel.CacheRatioLabel);
        Assert.Equal("Refresh", viewModel.RefreshCadenceLabel);
        Assert.Equal("Launch at Login", viewModel.LaunchAtLoginLabel);
        Assert.Equal("Requests/min", viewModel.RequestsPerMinuteLabel);
        Assert.Equal("token/s", viewModel.TokenPerSecondLabel);
        Assert.Equal("Refresh", viewModel.RefreshTooltip);
        Assert.Equal("Open Sessions Folder", viewModel.OpenFolderTooltip);
        Assert.Equal("Exit", viewModel.ExitTooltip);
    }

    [Fact]
    public void ViewModel_ChineseLocalization_HasCorrectLabels()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("输入", viewModel.InputLabel);
        Assert.Equal("缓存", viewModel.CachedLabel);
        Assert.Equal("输出", viewModel.OutputLabel);
        Assert.Equal("推理", viewModel.ReasoningLabel);
        Assert.Equal("活跃会话", viewModel.ActiveSessionsLabel);
        Assert.Equal("缓存占比", viewModel.CacheRatioLabel);
        Assert.Equal("自动刷新", viewModel.RefreshCadenceLabel);
        Assert.Equal("登录时启动", viewModel.LaunchAtLoginLabel);
        Assert.Equal("请求/分钟", viewModel.RequestsPerMinuteLabel);
        Assert.Equal("token/s", viewModel.TokenPerSecondLabel);
        Assert.Equal("立即刷新", viewModel.RefreshTooltip);
        Assert.Equal("打开 Codex 会话目录", viewModel.OpenFolderTooltip);
        Assert.Equal("退出 Codex TPS", viewModel.ExitTooltip);
    }

    [Fact]
    public void ViewModel_EnglishStatusText_Ready()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready);

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_ChineseStatusText_Ready()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready);

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("就绪", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_EnglishStatusText_NoSessions()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.SessionsDirectoryMissing);

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("No sessions", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_ChineseStatusText_NoSessions()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.SessionsDirectoryMissing);

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("未找到会话目录", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_EnglishStatusText_Error()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.ReadFailed);

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("Error", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_ChineseStatusText_Error()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.ReadFailed);

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("读取失败", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_EnglishStatusText_Reading()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);

        viewModel.IsRefreshing = true;
        Assert.Equal("Reading", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_ChineseStatusText_Reading()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);

        viewModel.IsRefreshing = true;
        Assert.Equal("读取中", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_EnglishMetricWindowDisplayNames()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("1 min", viewModel.SelectedWindowDisplayName);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.FifteenSeconds, Language.English));
        Assert.Equal("5 min", viewModel.SelectedWindowDisplayName);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.ThirtyMinutes, RefreshCadence.FifteenSeconds, Language.English));
        Assert.Equal("30 min", viewModel.SelectedWindowDisplayName);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.OneHour, RefreshCadence.FifteenSeconds, Language.English));
        Assert.Equal("1 hour", viewModel.SelectedWindowDisplayName);
    }

    [Fact]
    public void ViewModel_ChineseMetricWindowDisplayNames()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("1 分钟", viewModel.SelectedWindowDisplayName);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.FifteenSeconds, Language.Chinese));
        Assert.Equal("5 分钟", viewModel.SelectedWindowDisplayName);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.ThirtyMinutes, RefreshCadence.FifteenSeconds, Language.Chinese));
        Assert.Equal("30 分钟", viewModel.SelectedWindowDisplayName);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.OneHour, RefreshCadence.FifteenSeconds, Language.Chinese));
        Assert.Equal("1 小时", viewModel.SelectedWindowDisplayName);
    }

    [Fact]
    public void ViewModel_LanguageSwitch_UpdatesAllLabels()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("Input", viewModel.InputLabel);
        Assert.Equal("Refresh", viewModel.RefreshCadenceLabel);

        viewModel.UpdateSettings(new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese));

        Assert.Equal("输入", viewModel.InputLabel);
        Assert.Equal("自动刷新", viewModel.RefreshCadenceLabel);
    }

    [Fact]
    public void ViewModel_EnglishStatusText_Waiting()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("Waiting", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_ChineseStatusText_Waiting()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);

        Assert.Equal("等待数据", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_EnglishStatusText_ReadyWithPartialData()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = new UsageSnapshot(
            GeneratedAt: DateTimeOffset.UtcNow,
            OneMinute: WindowMetrics.Empty(60),
            FiveMinutes: WindowMetrics.Empty(300),
            ThirtyMinutes: WindowMetrics.Empty(1800),
            OneHour: WindowMetrics.Empty(3600),
            ActiveSessions: 0,
            MalformedRelevantLines: 5,
            Status: CollectionStatus.Ready
        );

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("Ready (5 malformed)", viewModel.StatusText);
    }

    [Fact]
    public void ViewModel_ChineseStatusText_ReadyWithPartialData()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = new UsageSnapshot(
            GeneratedAt: DateTimeOffset.UtcNow,
            OneMinute: WindowMetrics.Empty(60),
            FiveMinutes: WindowMetrics.Empty(300),
            ThirtyMinutes: WindowMetrics.Empty(1800),
            OneHour: WindowMetrics.Empty(3600),
            ActiveSessions: 0,
            MalformedRelevantLines: 5,
            Status: CollectionStatus.Ready
        );

        viewModel.UpdateSnapshot(snapshot);
        Assert.Equal("就绪 (5 部分记录无法解析)", viewModel.StatusText);
    }

    [Fact]
    public void StatusPresentationState_Waiting()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.Waiting, state.Status);
        Assert.Equal("Waiting", state.Text);
        Assert.Equal(StatusDotColor.Gray, state.DotColor);
    }

    [Fact]
    public void StatusPresentationState_Waiting_Chinese()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.Chinese);
        var viewModel = new MonitorPanelViewModel(settings);

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.Waiting, state.Status);
        Assert.Equal("等待数据", state.Text);
        Assert.Equal(StatusDotColor.Gray, state.DotColor);
    }

    [Fact]
    public void StatusPresentationState_Refreshing()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready);

        viewModel.UpdateSnapshot(snapshot);
        viewModel.IsRefreshing = true;

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.Refreshing, state.Status);
        Assert.Equal("Reading", state.Text);
        Assert.Equal(StatusDotColor.Blue, state.DotColor);
    }

    [Fact]
    public void StatusPresentationState_Ready()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready);

        viewModel.UpdateSnapshot(snapshot);

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.Ready, state.Status);
        Assert.Equal("Ready", state.Text);
        Assert.Equal(StatusDotColor.Green, state.DotColor);
    }

    [Fact]
    public void StatusPresentationState_ReadyWithPartialData()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = new UsageSnapshot(
            GeneratedAt: DateTimeOffset.UtcNow,
            OneMinute: WindowMetrics.Empty(60),
            FiveMinutes: WindowMetrics.Empty(300),
            ThirtyMinutes: WindowMetrics.Empty(1800),
            OneHour: WindowMetrics.Empty(3600),
            ActiveSessions: 0,
            MalformedRelevantLines: 3,
            Status: CollectionStatus.Ready
        );

        viewModel.UpdateSnapshot(snapshot);

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.ReadyWithPartialData, state.Status);
        Assert.Equal("Ready (3 malformed)", state.Text);
        Assert.Equal(StatusDotColor.Orange, state.DotColor);
    }

    [Fact]
    public void StatusPresentationState_NoSessions()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.SessionsDirectoryMissing);

        viewModel.UpdateSnapshot(snapshot);

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.NoSessions, state.Status);
        Assert.Equal("No sessions", state.Text);
        Assert.Equal(StatusDotColor.Orange, state.DotColor);
    }

    [Fact]
    public void StatusPresentationState_Error()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds, Language.English);
        var viewModel = new MonitorPanelViewModel(settings);
        var snapshot = UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.ReadFailed);

        viewModel.UpdateSnapshot(snapshot);

        var state = viewModel.StatusPresentationState;

        Assert.Equal(PanelStatus.Error, state.Status);
        Assert.Equal("Error", state.Text);
        Assert.Equal(StatusDotColor.Red, state.DotColor);
    }

    [Fact]
    public void Localization_Total()
    {
        Assert.Equal("Total", Localization.Total(Language.English));
        Assert.Equal("总计", Localization.Total(Language.Chinese));
    }

    [Fact]
    public void Localization_Status()
    {
        Assert.Equal("Status", Localization.Status(Language.English));
        Assert.Equal("状态", Localization.Status(Language.Chinese));
    }

    [Fact]
    public void Localization_LanguageDisplayNames()
    {
        Assert.Equal("English", Localization.English(Language.English));
        Assert.Equal("English", Localization.English(Language.Chinese));
        Assert.Equal("Chinese", Localization.Chinese(Language.English));
        Assert.Equal("简体中文", Localization.Chinese(Language.Chinese));
    }
}