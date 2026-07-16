using System;
using System.Globalization;
using CodexTPSCore;

namespace CodexTPSTray;

public static class TrayTextFormatter
{
    private const int MaxTooltipLength = 63;
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    public static string FormatTooltip(UsageSnapshot? snapshot, MetricWindow window)
    {
        if (!snapshot.HasValue)
        {
            return "Codex TPS";
        }

        var metrics = window.GetMetrics(snapshot.Value);
        string statusText = GetStatusText(snapshot.Value.Status);
        string windowText = GetWindowShortText(window);

        double tps = metrics.TokensPerSecond;
        int activeSessions = snapshot.Value.ActiveSessions;

        string tpsPart = FormatTps(tps);
        string sessionsPart = activeSessions.ToString(InvariantCulture);

        string tooltip = $"TPS: {tpsPart}/s | {windowText} | {statusText} | {sessionsPart} sessions";

        if (tooltip.Length <= MaxTooltipLength)
        {
            return tooltip;
        }

        return TruncateTooltip(tooltip);
    }

    public static string FormatTps(double tps)
    {
        if (tps >= 1000)
        {
            return tps.ToString("F0", InvariantCulture);
        }
        if (tps >= 10)
        {
            return tps.ToString("F1", InvariantCulture);
        }
        return tps.ToString("F1", InvariantCulture);
    }

    public static string GetStatusText(CollectionStatus status)
    {
        return status switch
        {
            CollectionStatus.Ready => "Ready",
            CollectionStatus.SessionsDirectoryMissing => "No sessions",
            CollectionStatus.ReadFailed => "Error",
            _ => "Unknown"
        };
    }

    public static string GetWindowShortText(MetricWindow window)
    {
        return window switch
        {
            MetricWindow.OneMinute => "1m",
            MetricWindow.FiveMinutes => "5m",
            MetricWindow.ThirtyMinutes => "30m",
            MetricWindow.OneHour => "1h",
            _ => "1m"
        };
    }

    private static string TruncateTooltip(string tooltip)
    {
        if (tooltip.Length <= MaxTooltipLength)
        {
            return tooltip;
        }

        string[] parts = tooltip.Split(" | ");
        if (parts.Length >= 4)
        {
            string shortened = $"{parts[0]} | {parts[1]} | {parts[2]}";
            if (shortened.Length <= MaxTooltipLength)
            {
                return shortened;
            }
        }

        if (parts.Length >= 2)
        {
            string shortened = $"{parts[0]} | {parts[1]}";
            if (shortened.Length <= MaxTooltipLength)
            {
                return shortened;
            }
        }

        return tooltip.Substring(0, MaxTooltipLength);
    }
}