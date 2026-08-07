using System;

namespace CodexTPSTray;

internal static class OverlayWindowSizeCalculator
{
    public const double WidthDip = 112.0;

    public static int ToPhysicalPixels(double dip, double dpi)
    {
        if (!double.IsFinite(dip) || dip <= 0)
            return 0;

        return Math.Max(1, (int)Math.Round(DpiHelper.ConvertDipToPhysicalPixels(dip, dpi)));
    }

    public static (int Width, int Height) CalculatePhysicalSize(double heightDip, double dpiX, double dpiY)
    {
        return (
            ToPhysicalPixels(WidthDip, dpiX),
            ToPhysicalPixels(heightDip, dpiY)
        );
    }

    public static bool Differs(int actual, int expected, int tolerance = 1)
    {
        if (actual <= 0 || expected <= 0)
            return false;

        return Math.Abs(actual - expected) > Math.Max(0, tolerance);
    }
}
