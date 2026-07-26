using System;
using System.Collections.Generic;
using System.Windows;

namespace CodexTPSTray;

public static class OverlayPositionCalculator
{
    public static (double Left, double Top) CalculatePosition(
        double? savedLeft,
        double? savedTop,
        System.Windows.Size overlaySize,
        Rect primaryWorkArea,
        IReadOnlyList<Rect> allWorkAreas,
        Thickness margin)
    {
        if (!savedLeft.HasValue || !savedTop.HasValue)
        {
            return GetDefaultPosition(overlaySize, primaryWorkArea, margin);
        }

        double left = savedLeft.Value;
        double top = savedTop.Value;

        if (double.IsNaN(left) || double.IsInfinity(left) ||
            double.IsNaN(top) || double.IsInfinity(top))
        {
            return GetDefaultPosition(overlaySize, primaryWorkArea, margin);
        }

        if (double.IsNaN(overlaySize.Width) || double.IsInfinity(overlaySize.Width) ||
            double.IsNaN(overlaySize.Height) || double.IsInfinity(overlaySize.Height) ||
            overlaySize.Width <= 0 || overlaySize.Height <= 0)
        {
            return GetDefaultPosition(new System.Windows.Size(200, 120), primaryWorkArea, margin);
        }

        Rect overlayRect = new Rect(left, top, overlaySize.Width, overlaySize.Height);

        bool intersectsAnyWorkArea = false;
        foreach (var workArea in allWorkAreas)
        {
            if (overlayRect.IntersectsWith(workArea))
            {
                intersectsAnyWorkArea = true;
                break;
            }
        }

        if (!intersectsAnyWorkArea)
        {
            return GetDefaultPosition(overlaySize, primaryWorkArea, margin);
        }

        return (left, top);
    }

    public static MonitorInfo FindBestMonitor(
        double overlayLeft,
        double overlayTop,
        double overlayWidth,
        double overlayHeight,
        IReadOnlyList<MonitorInfo> allMonitorInfos,
        MonitorInfo primaryMonitorInfo)
    {
        double centerX = overlayLeft + overlayWidth / 2.0;
        double centerY = overlayTop + overlayHeight / 2.0;

        foreach (var info in allMonitorInfos)
        {
            var wa = info.WorkingArea;
            if (centerX >= wa.Left && centerX < wa.Right && centerY >= wa.Top && centerY < wa.Bottom)
            {
                return info;
            }
        }

        Rect overlayRect = new Rect(overlayLeft, overlayTop, overlayWidth, overlayHeight);
        MonitorInfo? best = null;
        double bestArea = 0;

        foreach (var info in allMonitorInfos)
        {
            var wa = info.WorkingArea;
            Rect intersection = Rect.Intersect(overlayRect, wa);
            if (intersection.IsEmpty)
                continue;

            double area = intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = info;
            }
        }

        return best ?? primaryMonitorInfo;
    }

    public static (double Left, double Top) CalculatePresetPosition(
        OverlayPositionPreset preset,
        System.Windows.Size overlaySize,
        Rect workArea,
        Thickness margin)
    {
        double width = overlaySize.Width;
        double height = overlaySize.Height;

        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            width = 200;
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            height = 120;

        double left, top;

        switch (preset)
        {
            case OverlayPositionPreset.TopLeft:
                left = workArea.Left + margin.Left;
                top = workArea.Top + margin.Top;
                break;
            case OverlayPositionPreset.TopRight:
                left = workArea.Right - width - margin.Right;
                top = workArea.Top + margin.Top;
                break;
            case OverlayPositionPreset.MiddleLeft:
                left = workArea.Left + margin.Left;
                top = workArea.Top + (workArea.Height - height) / 2.0;
                break;
            case OverlayPositionPreset.MiddleRight:
                left = workArea.Right - width - margin.Right;
                top = workArea.Top + (workArea.Height - height) / 2.0;
                break;
            case OverlayPositionPreset.BottomLeft:
                left = workArea.Left + margin.Left;
                top = workArea.Bottom - height - margin.Bottom;
                break;
            case OverlayPositionPreset.BottomRight:
                left = workArea.Right - width - margin.Right;
                top = workArea.Bottom - height - margin.Bottom;
                break;
            default:
                return GetDefaultPosition(overlaySize, workArea, margin);
        }

        double minLeft = workArea.Left;
        double maxLeft = workArea.Right - width;
        double minTop = workArea.Top;
        double maxTop = workArea.Bottom - height;

        left = ClampToRange(left, minLeft, maxLeft);
        top = ClampToRange(top, minTop, maxTop);

        return (left, top);
    }

    public static MonitorInfo ResolveTargetMonitor(
        string? deviceName,
        IReadOnlyList<MonitorInfo> allMonitorInfos,
        MonitorInfo primaryMonitorInfo)
    {
        if (!string.IsNullOrEmpty(deviceName))
        {
            foreach (var info in allMonitorInfos)
            {
                if (string.Equals(info.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }
        }

        return primaryMonitorInfo;
    }

    private static double ClampToRange(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return min;
        if (max < min)
            return min;
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }

    private static (double Left, double Top) GetDefaultPosition(
        System.Windows.Size overlaySize,
        Rect primaryWorkArea,
        Thickness margin)
    {
        double left = primaryWorkArea.Right - overlaySize.Width - margin.Right;
        double top = primaryWorkArea.Top + margin.Top;

        return (left, top);
    }
}