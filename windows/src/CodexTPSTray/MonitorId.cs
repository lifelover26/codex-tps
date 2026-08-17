using System;

namespace CodexTPSTray;

/// <summary>
/// Encapsulates the monitor identity used to key per-display position records.
/// Resolution prefers <see cref="MonitorInfo.StableId"/> (a physical monitor
/// identifier from the Win32 CCD API that survives "show only on 1/2" topology
/// changes) and falls back to the GDI <see cref="MonitorInfo.DeviceName"/>
/// (e.g. "\\.\DISPLAY1") when the stable id is unavailable. A missing or blank
/// identity degrades to a sentinel so a primary monitor with no name still has
/// a stable key. Resolution/coordinates are deliberately NOT used as identity.
/// </summary>
public static class MonitorId
{
    private const string FallbackKey = "primary";

    public static string Resolve(MonitorInfo monitor)
    {
        if (monitor is null)
            return FallbackKey;

        if (!string.IsNullOrWhiteSpace(monitor.StableId))
            return monitor.StableId!;

        string deviceName = monitor.DeviceName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceName))
            return FallbackKey;

        return deviceName;
    }

    public static bool Equals(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
