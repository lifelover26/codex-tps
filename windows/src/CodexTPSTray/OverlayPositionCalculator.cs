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