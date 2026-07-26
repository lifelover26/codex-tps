using System.Windows;

namespace CodexTPSTray;

public sealed record MonitorInfo(string DeviceName, Rect WorkingArea, bool IsPrimary);