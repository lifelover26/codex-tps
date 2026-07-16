using System;
using CodexTPSCore;

namespace CodexTPSTray;

public enum MetricWindow
{
    OneMinute,
    FiveMinutes,
    ThirtyMinutes,
    OneHour
}

public static class MetricWindowExtensions
{
    public static WindowMetrics GetMetrics(this MetricWindow window, UsageSnapshot snapshot)
    {
        return window switch
        {
            MetricWindow.OneMinute => snapshot.OneMinute,
            MetricWindow.FiveMinutes => snapshot.FiveMinutes,
            MetricWindow.ThirtyMinutes => snapshot.ThirtyMinutes,
            MetricWindow.OneHour => snapshot.OneHour,
            _ => snapshot.OneMinute
        };
    }

    public static string GetDisplayName(this MetricWindow window)
    {
        return window switch
        {
            MetricWindow.OneMinute => "1 Minute",
            MetricWindow.FiveMinutes => "5 Minutes",
            MetricWindow.ThirtyMinutes => "30 Minutes",
            MetricWindow.OneHour => "1 Hour",
            _ => "1 Minute"
        };
    }

    public static int GetSeconds(this MetricWindow window)
    {
        return window switch
        {
            MetricWindow.OneMinute => 60,
            MetricWindow.FiveMinutes => 300,
            MetricWindow.ThirtyMinutes => 1800,
            MetricWindow.OneHour => 3600,
            _ => 60
        };
    }
}