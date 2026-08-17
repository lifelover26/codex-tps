using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;

namespace CodexTPSTray;

public class WpfMonitorWorkAreaProvider : IMonitorWorkAreaProvider
{
    private readonly IDisplayIdentityProvider? _identityProvider;

    public WpfMonitorWorkAreaProvider()
        : this(null)
    {
    }

    public WpfMonitorWorkAreaProvider(IDisplayIdentityProvider? identityProvider)
    {
        _identityProvider = identityProvider;
    }

    public Rect GetPrimaryWorkArea()
    {
        Screen? primary = Screen.PrimaryScreen;
        if (primary == null)
        {
            return new Rect(0, 0, 1920, 1080);
        }
        return ScreenToRect(primary.WorkingArea);
    }

    public IReadOnlyList<Rect> GetAllWorkAreas()
    {
        var workAreas = new List<Rect>();
        foreach (Screen screen in Screen.AllScreens)
        {
            workAreas.Add(ScreenToRect(screen.WorkingArea));
        }
        return workAreas;
    }

    public IReadOnlyList<MonitorInfo> GetAllMonitorInfos()
    {
        var infos = new List<MonitorInfo>();
        Screen? primary = Screen.PrimaryScreen;
        string? primaryDeviceName = primary?.DeviceName ?? string.Empty;

        IReadOnlyDictionary<string, string>? stableIdMap = null;
        try
        {
            stableIdMap = _identityProvider?.BuildDeviceNameToStableIdMap();
        }
        catch
        {
        }

        foreach (Screen screen in Screen.AllScreens)
        {
            IntPtr hmonitor = DpiHelper.GetMonitorForPoint(screen.Bounds.Location);
            var (dpiX, dpiY) = DpiHelper.GetDpiForMonitor(hmonitor);
            string deviceName = screen.DeviceName ?? string.Empty;
            string? stableId = null;
            if (stableIdMap != null && !string.IsNullOrWhiteSpace(deviceName) && stableIdMap.TryGetValue(deviceName, out var id))
            {
                stableId = id;
            }

            infos.Add(new MonitorInfo(
                DeviceName: deviceName,
                WorkingArea: ScreenToRect(screen.WorkingArea),
                IsPrimary: string.Equals(screen.DeviceName, primaryDeviceName, StringComparison.OrdinalIgnoreCase),
                DpiX: dpiX,
                DpiY: dpiY,
                StableId: stableId
            ));
        }
        return infos;
    }

    private Rect ScreenToRect(System.Drawing.Rectangle screenRect)
    {
        return new Rect(
            screenRect.Left,
            screenRect.Top,
            screenRect.Width,
            screenRect.Height
        );
    }
}
