using System;

namespace CodexTPSTray;

public enum RefreshCadence
{
    FiveSeconds = 5,
    FifteenSeconds = 15,
    ThirtySeconds = 30,
    SixtySeconds = 60
}

public static class RefreshCadenceExtensions
{
    public static TimeSpan ToTimeSpan(this RefreshCadence cadence)
    {
        return TimeSpan.FromSeconds((int)cadence);
    }

    public static string GetDisplayName(this RefreshCadence cadence)
    {
        return cadence switch
        {
            RefreshCadence.FiveSeconds => "5 Seconds",
            RefreshCadence.FifteenSeconds => "15 Seconds",
            RefreshCadence.ThirtySeconds => "30 Seconds",
            RefreshCadence.SixtySeconds => "60 Seconds",
            _ => "15 Seconds"
        };
    }

    public static RefreshCadence FromSeconds(int seconds)
    {
        return seconds switch
        {
            5 => RefreshCadence.FiveSeconds,
            15 => RefreshCadence.FifteenSeconds,
            30 => RefreshCadence.ThirtySeconds,
            60 => RefreshCadence.SixtySeconds,
            _ => RefreshCadence.FifteenSeconds
        };
    }
}