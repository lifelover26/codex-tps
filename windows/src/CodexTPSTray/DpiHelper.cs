using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexTPSTray;

public static class DpiHelper
{
    public const double DefaultDpi = 96.0;

    public static double SanitizeDpi(double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0)
            return DefaultDpi;
        return dpi;
    }

    public static double GetDpiScaleFactor(double dpi)
    {
        return SanitizeDpi(dpi) / DefaultDpi;
    }

    public static Thickness ConvertDipMarginToPhysical(Thickness dipMargin, double dpiX, double dpiY)
    {
        double scaleX = GetDpiScaleFactor(dpiX);
        double scaleY = GetDpiScaleFactor(dpiY);
        return new Thickness(
            dipMargin.Left * scaleX,
            dipMargin.Top * scaleY,
            dipMargin.Right * scaleX,
            dipMargin.Bottom * scaleY
        );
    }

    public static Thickness ConvertPhysicalMarginToDip(Thickness physicalMargin, double dpiX, double dpiY)
    {
        double scaleX = GetDpiScaleFactor(dpiX);
        double scaleY = GetDpiScaleFactor(dpiY);
        return new Thickness(
            physicalMargin.Left / scaleX,
            physicalMargin.Top / scaleY,
            physicalMargin.Right / scaleX,
            physicalMargin.Bottom / scaleY
        );
    }

    public static double ConvertPhysicalPixelsToDip(double physicalPixels, double dpi)
    {
        return physicalPixels / GetDpiScaleFactor(dpi);
    }

    public static double ConvertDipToPhysicalPixels(double dip, double dpi)
    {
        return dip * GetDpiScaleFactor(dpi);
    }

    public static (double Left, double Top, double Width, double Height) ConvertPhysicalRectToDip(
        double physicalLeft,
        double physicalTop,
        double physicalWidth,
        double physicalHeight,
        double dpiX,
        double dpiY)
    {
        double scaleX = GetDpiScaleFactor(dpiX);
        double scaleY = GetDpiScaleFactor(dpiY);
        return (
            physicalLeft / scaleX,
            physicalTop / scaleY,
            physicalWidth / scaleX,
            physicalHeight / scaleY
        );
    }

    public static (int Left, int Top, int Width, int Height) ConvertDipRectToPhysical(
        double dipLeft,
        double dipTop,
        double dipWidth,
        double dipHeight,
        double dpiX,
        double dpiY)
    {
        double scaleX = GetDpiScaleFactor(dpiX);
        double scaleY = GetDpiScaleFactor(dpiY);
        return (
            (int)Math.Round(dipLeft * scaleX),
            (int)Math.Round(dipTop * scaleY),
            (int)Math.Round(dipWidth * scaleX),
            (int)Math.Round(dipHeight * scaleY)
        );
    }

    public static Rect ConvertPhysicalRectToDipRect(
        System.Drawing.Rectangle physicalRect,
        double dpiX,
        double dpiY)
    {
        double scaleX = GetDpiScaleFactor(dpiX);
        double scaleY = GetDpiScaleFactor(dpiY);
        return new Rect(
            physicalRect.Left / scaleX,
            physicalRect.Top / scaleY,
            physicalRect.Width / scaleX,
            physicalRect.Height / scaleY
        );
    }

    public static double GetDpiForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return DefaultDpi;

        try
        {
            uint dpi = NativeMethods.GetDpiForWindow(hwnd);
            return SanitizeDpi(dpi);
        }
        catch
        {
            return DefaultDpi;
        }
    }

    public static (double DpiX, double DpiY) GetDpiForHwndSource(HwndSource hwndSource)
    {
        if (hwndSource?.CompositionTarget != null)
        {
            Matrix transformToDevice = hwndSource.CompositionTarget.TransformToDevice;
            return (
                SanitizeDpi(DefaultDpi * transformToDevice.M11),
                SanitizeDpi(DefaultDpi * transformToDevice.M22)
            );
        }

        return (DefaultDpi, DefaultDpi);
    }

    // Query DPI for an arbitrary HMONITOR without a window.
    // Uses GetScaleFactorForMonitor (shcore.dll) which is the supported API for
    // per-monitor-aware contexts, instead of GetDpiForMonitor which is not
    // recommended for use on per-monitor-aware UI threads.
    // Scale factor is returned as an integer percentage (100 = 100%, 125 = 125%, etc.).
    public static (double DpiX, double DpiY) GetDpiForMonitor(IntPtr hmonitor)
    {
        if (hmonitor == IntPtr.Zero)
            return (DefaultDpi, DefaultDpi);

        try
        {
            int scaleFactor = QueryScaleFactorForMonitor(hmonitor);
            if (scaleFactor > 0)
            {
                double dpi = SanitizeDpi(scaleFactor * DefaultDpi / 100.0);
                return (dpi, dpi);
            }
        }
        catch
        {
        }

        return (DefaultDpi, DefaultDpi);
    }

    public static IntPtr GetMonitorForPoint(System.Drawing.Point point)
    {
        try
        {
            POINT pt = new POINT { x = point.X, y = point.Y };
            return NativeMethods.MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static IntPtr GetMonitorForWindow(IntPtr hwnd)
    {
        try
        {
            return NativeMethods.MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static int QueryScaleFactorForMonitor(IntPtr hmonitor)
    {
        int scale = 100;
        try
        {
            int hr = NativeMethods.GetScaleFactorForMonitor(hmonitor, out scale);
            if (hr != 0) // S_OK == 0
                return 0;
        }
        catch
        {
            return 0;
        }
        return scale > 0 ? scale : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        // GetScaleFactorForMonitor returns DEVICE_SCALE_FACTOR as an integer
        // (100 = 100%, 125 = 125%, 150 = 150%, etc.). This is the supported API
        // for querying monitor DPI in per-monitor-aware contexts.
        [DllImport("shcore.dll")]
        public static extern int GetScaleFactorForMonitor(IntPtr hmonitor, out int pScale);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    }
}
