using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class DpiHelperTests
{
    //
    // SanitizeDpi: guard against invalid DPI inputs
    //

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-96)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SanitizeDpi_InvalidValues_FallbackTo96(double invalidDpi)
    {
        Assert.Equal(DpiHelper.DefaultDpi, DpiHelper.SanitizeDpi(invalidDpi));
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    [InlineData(192)]
    public void SanitizeDpi_ValidValues_ReturnsSameValue(double validDpi)
    {
        Assert.Equal(validDpi, DpiHelper.SanitizeDpi(validDpi));
    }

    //
    // DPI scale factor: dpi / 96
    //

    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(168, 1.75)]
    [InlineData(192, 2.0)]
    public void GetDpiScaleFactor_StandardScalings(double dpi, double expectedFactor)
    {
        Assert.Equal(expectedFactor, DpiHelper.GetDpiScaleFactor(dpi), 6);
    }

    [Fact]
    public void GetDpiScaleFactor_InvalidDpi_FallsBackTo96()
    {
        Assert.Equal(1.0, DpiHelper.GetDpiScaleFactor(0));
        Assert.Equal(1.0, DpiHelper.GetDpiScaleFactor(-144));
        Assert.Equal(1.0, DpiHelper.GetDpiScaleFactor(double.NaN));
    }

    //
    // Pixel <-> DIP conversions
    //

    [Theory]
    [InlineData(96, 100, 100)]     // 100% scaling
    [InlineData(120, 100, 125)]   // 125% scaling
    [InlineData(144, 100, 150)]   // 150% scaling
    [InlineData(168, 100, 175)]   // 175% scaling
    [InlineData(192, 100, 200)]   // 200% scaling
    public void ConvertDipToPhysicalPixels_StandardScalings(double dpi, double dip, double expectedPhysical)
    {
        double physical = DpiHelper.ConvertDipToPhysicalPixels(dip, dpi);
        Assert.Equal(expectedPhysical, physical, 0);
    }

    [Theory]
    [InlineData(96, 100, 100)]     // 100% scaling
    [InlineData(120, 125, 100)]   // 125% scaling
    [InlineData(144, 150, 100)]   // 150% scaling
    [InlineData(168, 175, 100)]   // 175% scaling
    [InlineData(192, 200, 100)]   // 200% scaling
    public void ConvertPhysicalPixelsToDip_StandardScalings(double dpi, double physical, double expectedDip)
    {
        double dip = DpiHelper.ConvertPhysicalPixelsToDip(physical, dpi);
        Assert.Equal(expectedDip, dip, 0);
    }

    //
    // Round-trip: DIP -> physical -> DIP at each standard DPI
    //

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    [InlineData(192)]
    public void DipToPhysicalToDip_RoundTrip_PreservesValue(double dpi)
    {
        const double originalDip = 16.0;
        double physical = DpiHelper.ConvertDipToPhysicalPixels(originalDip, dpi);
        double roundTripDip = DpiHelper.ConvertPhysicalPixelsToDip(physical, dpi);
        Assert.Equal(originalDip, roundTripDip, 6);
    }

    //
    // Margin conversion: symmetric margins
    //

    [Theory]
    [InlineData(96, 16, 16)]    // 16 DIP margin = 16 physical @ 100%
    [InlineData(120, 16, 20)]   // 16 DIP margin = 20 physical @ 125%
    [InlineData(144, 16, 24)]   // 16 DIP margin = 24 physical @ 150%
    [InlineData(168, 16, 28)]   // 16 DIP margin = 28 physical @ 175%
    [InlineData(192, 16, 32)]   // 16 DIP margin = 32 physical @ 200%
    public void ConvertDipMarginToPhysical_Symmetric(double dpi, double dipMargin, double expectedPhysical)
    {
        Thickness dipThickness = new Thickness(dipMargin);
        Thickness physicalThickness = DpiHelper.ConvertDipMarginToPhysical(dipThickness, dpi, dpi);

        Assert.Equal(expectedPhysical, physicalThickness.Left, 0);
        Assert.Equal(expectedPhysical, physicalThickness.Top, 0);
        Assert.Equal(expectedPhysical, physicalThickness.Right, 0);
        Assert.Equal(expectedPhysical, physicalThickness.Bottom, 0);
    }

    //
    // Margin conversion: asymmetric margins with different X/Y DPI
    //

    [Fact]
    public void ConvertDipMarginToPhysical_AsymmetricMargins()
    {
        Thickness dip = new Thickness(10, 20, 30, 40);
        double dpiX = 144; // 150%
        double dpiY = 120; // 125%

        Thickness physical = DpiHelper.ConvertDipMarginToPhysical(dip, dpiX, dpiY);

        Assert.Equal(15, physical.Left);
        Assert.Equal(25, physical.Top);
        Assert.Equal(45, physical.Right);
        Assert.Equal(50, physical.Bottom);
    }

    //
    // Margin round-trip
    //

    [Fact]
    public void ConvertMargin_RoundTrip()
    {
        Thickness original = new Thickness(8, 16, 24, 32);
        double dpiX = 168;
        double dpiY = 120;

        Thickness physical = DpiHelper.ConvertDipMarginToPhysical(original, dpiX, dpiY);
        Thickness roundTrip = DpiHelper.ConvertPhysicalMarginToDip(physical, dpiX, dpiY);

        Assert.Equal(original.Left, roundTrip.Left, 6);
        Assert.Equal(original.Top, roundTrip.Top, 6);
        Assert.Equal(original.Right, roundTrip.Right, 6);
        Assert.Equal(original.Bottom, roundTrip.Bottom, 6);
    }

    //
    // MonitorInfo defaults
    //

    [Fact]
    public void MonitorInfo_DefaultDpiIs96()
    {
        var monitor = new MonitorInfo("test", new Rect(0, 0, 1920, 1080), true);
        Assert.Equal(96.0, monitor.DpiX);
        Assert.Equal(96.0, monitor.DpiY);
    }

    //
    // DpiHelper.GetDpiForWindow / GetDpiForMonitor zero handle fallback
    //

    [Fact]
    public void GetDpiForWindow_ZeroHandle_FallbackTo96()
    {
        Assert.Equal(DpiHelper.DefaultDpi, DpiHelper.GetDpiForWindow(System.IntPtr.Zero));
    }

    [Fact]
    public void GetDpiForMonitor_ZeroHandle_FallbackTo96()
    {
        var (dpiX, dpiY) = DpiHelper.GetDpiForMonitor(System.IntPtr.Zero);
        Assert.Equal(DpiHelper.DefaultDpi, dpiX);
        Assert.Equal(DpiHelper.DefaultDpi, dpiY);
    }

    //
    // HwndSource DPI fallback when no CompositionTarget
    //

    [Fact]
    public void GetDpiForHwndSource_NullSource_FallbackTo96()
    {
        var (dpiX, dpiY) = DpiHelper.GetDpiForHwndSource(null!);
        Assert.Equal(DpiHelper.DefaultDpi, dpiX);
        Assert.Equal(DpiHelper.DefaultDpi, dpiY);
    }
}
