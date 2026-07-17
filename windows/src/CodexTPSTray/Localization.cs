using CodexTPSCore;

namespace CodexTPSTray;

public enum Language
{
    English,
    Chinese
}

public static class Localization
{
    public static string GetStatusText(CollectionStatus status, bool isRefreshing, int malformedLines, bool hasSnapshot, Language language)
    {
        if (isRefreshing)
        {
            return language == Language.Chinese ? "读取中" : "Reading";
        }

        if (!hasSnapshot)
        {
            return language == Language.Chinese ? "等待数据" : "Waiting";
        }

        return status switch
        {
            CollectionStatus.Ready => malformedLines > 0
                ? (language == Language.Chinese ? $"就绪 ({malformedLines} 部分记录无法解析)" : $"Ready ({malformedLines} malformed)")
                : (language == Language.Chinese ? "就绪" : "Ready"),
            CollectionStatus.SessionsDirectoryMissing => language == Language.Chinese ? "未找到会话目录" : "No sessions",
            CollectionStatus.ReadFailed => language == Language.Chinese ? "读取失败" : "Error",
            _ => language == Language.Chinese ? "未知" : "Unknown"
        };
    }

    public static string MetricWindowOneMinute(Language language)
    {
        return language == Language.Chinese ? "1 分钟" : "1 min";
    }

    public static string MetricWindowFiveMinutes(Language language)
    {
        return language == Language.Chinese ? "5 分钟" : "5 min";
    }

    public static string MetricWindowThirtyMinutes(Language language)
    {
        return language == Language.Chinese ? "30 分钟" : "30 min";
    }

    public static string MetricWindowOneHour(Language language)
    {
        return language == Language.Chinese ? "1 小时" : "1 hour";
    }

    public static string GetMetricWindowDisplayName(MetricWindow window, Language language)
    {
        return window switch
        {
            MetricWindow.OneMinute => MetricWindowOneMinute(language),
            MetricWindow.FiveMinutes => MetricWindowFiveMinutes(language),
            MetricWindow.ThirtyMinutes => MetricWindowThirtyMinutes(language),
            MetricWindow.OneHour => MetricWindowOneHour(language),
            _ => MetricWindowOneMinute(language)
        };
    }

    public static string OverlayMetricWindowOneMinute(Language language)
    {
        return language == Language.Chinese ? "1 分钟" : "1 min";
    }

    public static string OverlayMetricWindowFiveMinutes(Language language)
    {
        return language == Language.Chinese ? "5 分钟" : "5 min";
    }

    public static string OverlayMetricWindowThirtyMinutes(Language language)
    {
        return language == Language.Chinese ? "30 分钟" : "30 min";
    }

    public static string OverlayMetricWindowOneHour(Language language)
    {
        return language == Language.Chinese ? "1 小时" : "1 hr";
    }

    public static string GetOverlayMetricWindowDisplayName(MetricWindow window, Language language)
    {
        return window switch
        {
            MetricWindow.OneMinute => OverlayMetricWindowOneMinute(language),
            MetricWindow.FiveMinutes => OverlayMetricWindowFiveMinutes(language),
            MetricWindow.ThirtyMinutes => OverlayMetricWindowThirtyMinutes(language),
            MetricWindow.OneHour => OverlayMetricWindowOneHour(language),
            _ => OverlayMetricWindowOneMinute(language)
        };
    }

    public static string RefreshCadenceFiveSeconds(Language language)
    {
        return language == Language.Chinese ? "5 秒" : "5 Seconds";
    }

    public static string RefreshCadenceFifteenSeconds(Language language)
    {
        return language == Language.Chinese ? "15 秒" : "15 Seconds";
    }

    public static string RefreshCadenceThirtySeconds(Language language)
    {
        return language == Language.Chinese ? "30 秒" : "30 Seconds";
    }

    public static string RefreshCadenceSixtySeconds(Language language)
    {
        return language == Language.Chinese ? "60 秒" : "60 Seconds";
    }

    public static string RefreshCadenceFiveSecondsCompact(Language language)
    {
        return language == Language.Chinese ? "5 秒" : "5 sec";
    }

    public static string RefreshCadenceFifteenSecondsCompact(Language language)
    {
        return language == Language.Chinese ? "15 秒" : "15 sec";
    }

    public static string RefreshCadenceThirtySecondsCompact(Language language)
    {
        return language == Language.Chinese ? "30 秒" : "30 sec";
    }

    public static string RefreshCadenceSixtySecondsCompact(Language language)
    {
        return language == Language.Chinese ? "60 秒" : "60 sec";
    }

    public static string GetRefreshCadenceCompactName(RefreshCadence cadence, Language language)
    {
        return cadence switch
        {
            RefreshCadence.FiveSeconds => RefreshCadenceFiveSecondsCompact(language),
            RefreshCadence.FifteenSeconds => RefreshCadenceFifteenSecondsCompact(language),
            RefreshCadence.ThirtySeconds => RefreshCadenceThirtySecondsCompact(language),
            RefreshCadence.SixtySeconds => RefreshCadenceSixtySecondsCompact(language),
            _ => RefreshCadenceFifteenSecondsCompact(language)
        };
    }

    public static string GetRefreshCadenceDisplayName(RefreshCadence cadence, Language language)
    {
        return cadence switch
        {
            RefreshCadence.FiveSeconds => RefreshCadenceFiveSeconds(language),
            RefreshCadence.FifteenSeconds => RefreshCadenceFifteenSeconds(language),
            RefreshCadence.ThirtySeconds => RefreshCadenceThirtySeconds(language),
            RefreshCadence.SixtySeconds => RefreshCadenceSixtySeconds(language),
            _ => RefreshCadenceFifteenSeconds(language)
        };
    }

    public static string TokenPerSecond(Language language)
    {
        return language == Language.Chinese ? "token/s" : "token/s";
    }

    public static string TokenPerSecondLabel(Language language)
    {
        return language == Language.Chinese ? "token/s" : "token/s";
    }

    public static string RequestsPerMinute(Language language)
    {
        return language == Language.Chinese ? "请求/分钟" : "Requests/min";
    }

    public static string Input(Language language)
    {
        return language == Language.Chinese ? "输入" : "Input";
    }

    public static string Cached(Language language)
    {
        return language == Language.Chinese ? "缓存" : "Cached";
    }

    public static string Output(Language language)
    {
        return language == Language.Chinese ? "输出" : "Output";
    }

    public static string Reasoning(Language language)
    {
        return language == Language.Chinese ? "推理" : "Reasoning";
    }

    public static string ActiveSessions(Language language)
    {
        return language == Language.Chinese ? "活跃会话" : "Active Sessions";
    }

    public static string CacheRatio(Language language)
    {
        return language == Language.Chinese ? "缓存占比" : "Cache Ratio";
    }

    public static string RefreshCadenceLabel(Language language)
    {
        return language == Language.Chinese ? "自动刷新" : "Refresh";
    }

    public static string LaunchAtLogin(Language language)
    {
        return language == Language.Chinese ? "登录时启动" : "Launch at Login";
    }

    public static string RefreshTooltip(Language language)
    {
        return language == Language.Chinese ? "立即刷新" : "Refresh";
    }

    public static string OpenSessionsFolderTooltip(Language language)
    {
        return language == Language.Chinese ? "打开 Codex 会话目录" : "Open Sessions Folder";
    }

    public static string ExitTooltip(Language language)
    {
        return language == Language.Chinese ? "退出 Codex TPS" : "Exit";
    }

    public static string RefreshMenu(Language language)
    {
        return language == Language.Chinese ? "立即刷新" : "Refresh";
    }

    public static string ExitMenu(Language language)
    {
        return language == Language.Chinese ? "退出 Codex TPS" : "Exit";
    }

    public static string OpenSessionsFolderMenu(Language language)
    {
        return language == Language.Chinese ? "打开 Codex 会话目录" : "Open Sessions Folder";
    }

    public static string MetricWindowMenu(Language language)
    {
        return language == Language.Chinese ? "时间窗口" : "Metric Window";
    }

    public static string MetricsMenu(Language language)
    {
        return language == Language.Chinese ? "指标" : "Metrics";
    }

    public static string Total(Language language)
    {
        return language == Language.Chinese ? "总计" : "Total";
    }

    public static string Status(Language language)
    {
        return language == Language.Chinese ? "状态" : "Status";
    }

    public static string RefreshCadenceMenu(Language language)
    {
        return language == Language.Chinese ? "刷新间隔" : "Refresh Cadence";
    }

    public static string LanguageMenu(Language language)
    {
        return language == Language.Chinese ? "语言" : "Language";
    }

    public static string English(Language language)
    {
        return language == Language.Chinese ? "English" : "English";
    }

    public static string Chinese(Language language)
    {
        return language == Language.Chinese ? "简体中文" : "Chinese";
    }

    public static string FailedToEnableLaunchAtLogin(Language language)
    {
        return language == Language.Chinese ? "无法设置登录时启动。" : "Failed to enable launch at login.";
    }

    public static string FailedToDisableLaunchAtLogin(Language language)
    {
        return language == Language.Chinese ? "无法取消登录时启动。" : "Failed to disable launch at login.";
    }

    public static string FailedToOpenSessionsFolder(Language language)
    {
        return language == Language.Chinese ? "无法打开会话目录。" : "Failed to open sessions folder.";
    }

    public static string FailedToReadStartupSettings(Language language)
    {
        return language == Language.Chinese ? "无法读取启动设置。" : "Failed to read startup settings.";
    }

    public static string OverlayTitle(Language language)
    {
        return "Codex TPS";
    }

    public static string OverlayRequestsPerMinute(Language language)
    {
        return language == Language.Chinese ? "请求/分钟" : "req/min";
    }

    public static string OverlaySessions(Language language)
    {
        return language == Language.Chinese ? "个会话" : "sessions";
    }

    public static string OverlayCache(Language language)
    {
        return language == Language.Chinese ? "缓存" : "cache";
    }

    public static string OverlayMenu(Language language)
    {
        return language == Language.Chinese ? "悬浮窗" : "Overlay";
    }

    public static string ShowOverlayMenu(Language language)
    {
        return language == Language.Chinese ? "显示悬浮窗" : "Show Overlay";
    }

    public static string LockOverlayMenu(Language language)
    {
        return language == Language.Chinese ? "锁定悬浮窗" : "Lock Overlay";
    }

    public static string ResetOverlayPositionMenu(Language language)
    {
        return language == Language.Chinese ? "重置悬浮窗位置" : "Reset Overlay Position";
    }

    public static string ThemeMenu(Language language)
    {
        return language == Language.Chinese ? "主题" : "Theme";
    }

    public static string OverlayThemeMenu(Language language)
    {
        return language == Language.Chinese ? "主题" : "Theme";
    }

    public static string GetApplicationThemeDisplayName(ApplicationThemePreference preference, Language language)
    {
        return preference switch
        {
            ApplicationThemePreference.System => language == Language.Chinese ? "跟随系统" : "System",
            ApplicationThemePreference.Light => language == Language.Chinese ? "浅色" : "Light",
            ApplicationThemePreference.Dark => language == Language.Chinese ? "深色" : "Dark",
            _ => language == Language.Chinese ? "跟随系统" : "System"
        };
    }

    public static string GetOverlayThemeDisplayName(OverlayThemePreference preference, Language language)
    {
        return preference switch
        {
            OverlayThemePreference.FollowApplication => language == Language.Chinese ? "跟随应用" : "Follow Application",
            OverlayThemePreference.System => language == Language.Chinese ? "跟随系统" : "System",
            OverlayThemePreference.Light => language == Language.Chinese ? "浅色" : "Light",
            OverlayThemePreference.Dark => language == Language.Chinese ? "深色" : "Dark",
            _ => language == Language.Chinese ? "跟随应用" : "Follow Application"
        };
    }
}