using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayPositionCalculatorTests
{
    private readonly Rect _primaryWorkArea = new Rect(0, 0, 1920, 1080);
    private readonly System.Windows.Size _overlaySize = new System.Windows.Size(200, 120);
    private readonly Thickness _margin = new Thickness(16);

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
}