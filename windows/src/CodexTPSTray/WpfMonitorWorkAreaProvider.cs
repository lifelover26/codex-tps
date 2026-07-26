using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;

namespace CodexTPSTray;

public class WpfMonitorWorkAreaProvider : IMonitorWorkAreaProvider
{
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

        foreach (Screen screen in Screen.AllScreens)
        {
            infos.Add(new MonitorInfo(
                DeviceName: screen.DeviceName ?? string.Empty,
                WorkingArea: ScreenToRect(screen.WorkingArea),
                IsPrimary: string.Equals(screen.DeviceName, primaryDeviceName, StringComparison.OrdinalIgnoreCase)
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