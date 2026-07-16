using System;
using System.Globalization;
using CodexTPSCore;

namespace CodexTPSTray;

public static class OverlayFormatter
{
    public static string[] FormatOverlay(UsageSnapshot snapshot, MetricWindow window, Language language)
    {
        var lines = new string[6];

        lines[0] = Localization.OverlayTitle(language);
        lines[1] = Localization.GetOverlayMetricWindowDisplayName(window, language);

        var metrics = window.GetMetrics(snapshot);

        string tpsValue = MonitorPanelViewModel.FormatCompactNumber(metrics.TokensPerSecond);
        string tpsUnit = Localization.TokenPerSecond(language);
        lines[2] = $"{tpsValue} {tpsUnit}";

        string rpmValue = MonitorPanelViewModel.FormatCompactNumber(metrics.RequestsPerMinute);
        string rpmUnit = Localization.OverlayRequestsPerMinute(language);
        lines[3] = $"{rpmValue} {rpmUnit}";

        string sessionsValue = snapshot.ActiveSessions.ToString(CultureInfo.InvariantCulture);
        string sessionsUnit = Localization.OverlaySessions(language);
        lines[4] = $"{sessionsValue} {sessionsUnit}";

        double cacheRatio = metrics.CacheRatio;
        string cachePercent = cacheRatio.ToString("P0", CultureInfo.InvariantCulture).Replace(" ", "");
        string cacheLabel = Localization.OverlayCache(language);
        lines[5] = $"{cachePercent} {cacheLabel}";

        return lines;
    }
}