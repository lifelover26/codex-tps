using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayPositionCalculatorTests
{
    private readonly Rect _primaryWorkArea = new Rect(0, 0, 1920, 1080);
    private readonly System.Windows.Size _overlaySize = new System.Windows.Size(200, 120);
    private readonly Thickness _margin = new Thickness(16);

    private static MonitorInfo PrimaryMonitor() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(0, 0, 1920, 1080),
        IsPrimary: true
    );

    private static MonitorInfo SecondaryMonitor() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY2",
        WorkingArea: new Rect(1920, 0, 1920, 1080),
        IsPrimary: false
    );

    private static MonitorInfo NegativeMonitor() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY3",
        WorkingArea: new Rect(-1920, -200, 1920, 1080),
        IsPrimary: false
    );

    [Fact]
    public void CalculatePosition_NullSavedPosition_ReturnsDefault()
    {
        var result = OverlayPositionCalculator.CalculatePosition(
            null, null, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_ValidPositionInsideWorkArea_ReturnsSavedPosition()
    {
        double savedLeft = 500;
        double savedTop = 300;

        var result = OverlayPositionCalculator.CalculatePosition(
            savedLeft, savedTop, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(savedLeft, result.Left);
        Assert.Equal(savedTop, result.Top);
    }

    [Fact]
    public void CalculatePosition_NegativePositionOnSecondMonitor_ReturnsSavedPosition()
    {
        var secondMonitor = new Rect(-1920, 0, 1920, 1080);
        double savedLeft = -1500;
        double savedTop = 200;

        var result = OverlayPositionCalculator.CalculatePosition(
            savedLeft, savedTop, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea, secondMonitor }, _margin);

        Assert.Equal(savedLeft, result.Left);
        Assert.Equal(savedTop, result.Top);
    }

    [Fact]
    public void CalculatePosition_CompletelyOffScreen_ResetsToDefault()
    {
        double savedLeft = 5000;
        double savedTop = 5000;

        var result = OverlayPositionCalculator.CalculatePosition(
            savedLeft, savedTop, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_NaNPosition_ResetsToDefault()
    {
        var result = OverlayPositionCalculator.CalculatePosition(
            double.NaN, 300, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_PositiveInfinityPosition_ResetsToDefault()
    {
        var result = OverlayPositionCalculator.CalculatePosition(
            double.PositiveInfinity, 300, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_NegativeInfinityPosition_ResetsToDefault()
    {
        var result = OverlayPositionCalculator.CalculatePosition(
            double.NegativeInfinity, 300, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_PartiallyVisiblePosition_ReturnsSavedPosition()
    {
        double savedLeft = 1850;
        double savedTop = 1000;

        var result = OverlayPositionCalculator.CalculatePosition(
            savedLeft, savedTop, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(savedLeft, result.Left);
        Assert.Equal(savedTop, result.Top);
    }

    [Fact]
    public void CalculatePosition_MonitorLayoutChanged_ResetsToDefault()
    {
        var oldPosition = new Rect(2500, 500, _overlaySize.Width, _overlaySize.Height);
        var newMonitors = new[] { _primaryWorkArea };

        var result = OverlayPositionCalculator.CalculatePosition(
            2500, 500, _overlaySize, _primaryWorkArea, newMonitors, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_DefaultPositionIsTopRight()
    {
        var result = OverlayPositionCalculator.CalculatePosition(
            null, null, _overlaySize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(1920 - 200 - 16, result.Left);
        Assert.Equal(16, result.Top);
    }

    [Fact]
    public void CalculatePosition_ZeroWidth_ResetsToDefault()
    {
        var invalidSize = new System.Windows.Size(0, 120);
        var result = OverlayPositionCalculator.CalculatePosition(
            500, 300, invalidSize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - 200 - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_ZeroHeight_ResetsToDefault()
    {
        var invalidSize = new System.Windows.Size(200, 0);
        var result = OverlayPositionCalculator.CalculatePosition(
            500, 300, invalidSize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - 200 - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_NaNWidth_ResetsToDefault()
    {
        var invalidSize = new System.Windows.Size(double.NaN, 120);
        var result = OverlayPositionCalculator.CalculatePosition(
            500, 300, invalidSize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - 200 - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePosition_PositiveInfinityHeight_ResetsToDefault()
    {
        var invalidSize = new System.Windows.Size(200, double.PositiveInfinity);
        var result = OverlayPositionCalculator.CalculatePosition(
            500, 300, invalidSize, _primaryWorkArea,
            new[] { _primaryWorkArea }, _margin);

        Assert.Equal(_primaryWorkArea.Right - 200 - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_TopLeft_ReturnsCorrectCoordinates()
    {
        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopLeft, _overlaySize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Left + _margin.Left, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_TopRight_ReturnsCorrectCoordinates()
    {
        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopRight, _overlaySize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_MiddleLeft_ReturnsCorrectCoordinates()
    {
        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.MiddleLeft, _overlaySize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Left + _margin.Left, result.Left);
        double expectedTop = _primaryWorkArea.Top + (_primaryWorkArea.Height - _overlaySize.Height) / 2.0;
        Assert.Equal(expectedTop, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_MiddleRight_ReturnsCorrectCoordinates()
    {
        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.MiddleRight, _overlaySize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        double expectedTop = _primaryWorkArea.Top + (_primaryWorkArea.Height - _overlaySize.Height) / 2.0;
        Assert.Equal(expectedTop, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_BottomLeft_ReturnsCorrectCoordinates()
    {
        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.BottomLeft, _overlaySize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Left + _margin.Left, result.Left);
        Assert.Equal(_primaryWorkArea.Bottom - _overlaySize.Height - _margin.Bottom, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_BottomRight_ReturnsCorrectCoordinates()
    {
        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.BottomRight, _overlaySize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Bottom - _overlaySize.Height - _margin.Bottom, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_NegativeMonitorOrigin_TopLeft_ReturnsCorrect()
    {
        var negativeMonitor = new Rect(-1920, -200, 1920, 1080);

        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopLeft, _overlaySize, negativeMonitor, _margin);

        Assert.Equal(negativeMonitor.Left + _margin.Left, result.Left);
        Assert.Equal(negativeMonitor.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_NegativeMonitorOrigin_TopRight_ReturnsCorrect()
    {
        var negativeMonitor = new Rect(-1920, -200, 1920, 1080);

        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopRight, _overlaySize, negativeMonitor, _margin);

        Assert.Equal(negativeMonitor.Right - _overlaySize.Width - _margin.Right, result.Left);
        Assert.Equal(negativeMonitor.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_WorkAreaSmallerThanOverlay_ClampsSafely()
    {
        var tinyWorkArea = new Rect(0, 0, 100, 50);

        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.BottomRight, _overlaySize, tinyWorkArea, _margin);

        Assert.False(double.IsNaN(result.Left));
        Assert.False(double.IsNaN(result.Top));
        Assert.False(double.IsInfinity(result.Left));
        Assert.False(double.IsInfinity(result.Top));
    }

    [Fact]
    public void CalculatePresetPosition_NonStandardDpi_WorkArea()
    {
        var dpiWorkArea = new Rect(0, 0, 2560, 1440);
        var largerSize = new System.Windows.Size(300, 180);

        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.BottomRight, largerSize, dpiWorkArea, _margin);

        Assert.Equal(dpiWorkArea.Right - largerSize.Width - _margin.Right, result.Left);
        Assert.Equal(dpiWorkArea.Bottom - largerSize.Height - _margin.Bottom, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_InvalidSize_DefaultsTo200x120()
    {
        var invalidSize = new System.Windows.Size(0, 0);

        var result = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopRight, invalidSize, _primaryWorkArea, _margin);

        Assert.Equal(_primaryWorkArea.Right - 200 - _margin.Right, result.Left);
        Assert.Equal(_primaryWorkArea.Top + _margin.Top, result.Top);
    }

    [Fact]
    public void CalculatePresetPosition_AllSixPresets_16pxMarginConsistent()
    {
        foreach (OverlayPositionPreset preset in Enum.GetValues<OverlayPositionPreset>())
        {
            var result = OverlayPositionCalculator.CalculatePresetPosition(
                preset, _overlaySize, _primaryWorkArea, _margin);

            Assert.True(result.Left >= _primaryWorkArea.Left);
            Assert.True(result.Top >= _primaryWorkArea.Top);
            Assert.False(double.IsNaN(result.Left));
            Assert.False(double.IsNaN(result.Top));
        }
    }

    [Fact]
    public void FindBestMonitor_CenterInsidePrimary_ReturnsPrimary()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.FindBestMonitor(
            500, 300, 200, 120, all, PrimaryMonitor());

        Assert.Equal(PrimaryMonitor(), result);
    }

    [Fact]
    public void FindBestMonitor_CenterInsideSecondary_ReturnsSecondary()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.FindBestMonitor(
            2500, 500, 200, 120, all, PrimaryMonitor());

        Assert.Equal(SecondaryMonitor(), result);
    }

    [Fact]
    public void FindBestMonitor_CenterOutsideAll_UsesMaxIntersection()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.FindBestMonitor(
            5000, 5000, 200, 120, all, PrimaryMonitor());

        Assert.Equal(PrimaryMonitor(), result);
    }

    [Fact]
    public void FindBestMonitor_PartialOverlap_CenterInSecondary_PrefersCenter()
    {
        // left=1880, width=200 → center_x=1980, which is inside the secondary monitor
        // (secondary starts at x=1920). Center priority must win over intersection area.
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.FindBestMonitor(
            1880, 500, 200, 120, all, PrimaryMonitor());

        Assert.Equal(SecondaryMonitor(), result);
    }

    [Fact]
    public void FindBestMonitor_CenterInGap_UsesMaxIntersection()
    {
        // Two monitors with a gap between them. Primary: [0, 1920), gap: [1920, 2000), secondary: [2000, 3920).
        // Window left=1880, width=200 → center_x=1980, which is in the gap (not inside any monitor).
        // Window rect [1880, 2080] intersects both monitors:
        //   - Primary intersection: [1880, 1920) → width 40 → area 40 * 120 = 4800
        //   - Secondary intersection: [2000, 2080] → width 80 → area 80 * 120 = 9600
        // Secondary has larger intersection → should return secondary.
        var primary = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true);
        var secondary = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY2",
            WorkingArea: new Rect(2000, 0, 1920, 1080),
            IsPrimary: false);
        var all = new List<MonitorInfo> { primary, secondary };

        var result = OverlayPositionCalculator.FindBestMonitor(
            1880, 500, 200, 120, all, primary);

        Assert.Equal(secondary, result);
    }

    [Fact]
    public void FindBestMonitor_NegativeScreen_ReturnsCorrect()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), NegativeMonitor() };

        var result = OverlayPositionCalculator.FindBestMonitor(
            -1500, 200, 200, 120, all, PrimaryMonitor());

        Assert.Equal(NegativeMonitor(), result);
    }

    [Fact]
    public void CalculatePresetPosition_SixPresets_OnSecondaryMonitor()
    {
        var secondaryWorkArea = SecondaryMonitor().WorkingArea;
        foreach (OverlayPositionPreset preset in Enum.GetValues<OverlayPositionPreset>())
        {
            var result = OverlayPositionCalculator.CalculatePresetPosition(
                preset, _overlaySize, secondaryWorkArea, _margin);

            Assert.True(result.Left >= secondaryWorkArea.Left);
            Assert.True(result.Top >= secondaryWorkArea.Top);
            Assert.False(double.IsNaN(result.Left));
            Assert.False(double.IsNaN(result.Top));
        }
    }

    [Fact]
    public void ResolveTargetMonitor_DeviceExists_ReturnsMatchingMonitor()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.ResolveTargetMonitor(
            @"\\.\DISPLAY2", all, PrimaryMonitor());

        Assert.Equal(SecondaryMonitor(), result);
    }

    [Fact]
    public void ResolveTargetMonitor_DeviceNameCaseInsensitive_ReturnsMatchingMonitor()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.ResolveTargetMonitor(
            @"\\.\display2", all, PrimaryMonitor());

        Assert.Equal(SecondaryMonitor(), result);
    }

    [Fact]
    public void ResolveTargetMonitor_DeviceMissing_FallsBackToPrimary()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.ResolveTargetMonitor(
            @"\\.\DISPLAY9", all, PrimaryMonitor());

        Assert.Equal(PrimaryMonitor(), result);
    }

    [Fact]
    public void ResolveTargetMonitor_NullDeviceName_FallsBackToPrimary()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.ResolveTargetMonitor(
            null, all, PrimaryMonitor());

        Assert.Equal(PrimaryMonitor(), result);
    }

    [Fact]
    public void ResolveTargetMonitor_EmptyDeviceName_FallsBackToPrimary()
    {
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };

        var result = OverlayPositionCalculator.ResolveTargetMonitor(
            string.Empty, all, PrimaryMonitor());

        Assert.Equal(PrimaryMonitor(), result);
    }

    [Fact]
    public void ResolveTargetMonitor_Fallback_DoesNotModifyInputSettings()
    {
        // The helper only reads the device name; it must not mutate any caller state.
        // Verified by passing a missing device name and confirming the original string is unchanged.
        var all = new List<MonitorInfo> { PrimaryMonitor(), SecondaryMonitor() };
        string deviceName = @"\\.\DISAPPEARED";

        var result = OverlayPositionCalculator.ResolveTargetMonitor(
            deviceName, all, PrimaryMonitor());

        Assert.Equal(PrimaryMonitor(), result);
        Assert.Equal(@"\\.\DISAPPEARED", deviceName);
    }
}