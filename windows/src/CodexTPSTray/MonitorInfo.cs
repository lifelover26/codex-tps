using System.Windows;

namespace CodexTPSTray;

public sealed record MonitorInfo(string DeviceName, Rect WorkingArea, bool IsPrimary, double DpiX = 96.0, double DpiY = 96.0);