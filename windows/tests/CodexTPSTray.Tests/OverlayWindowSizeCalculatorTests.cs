using System;
using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayWindowSizeCalculatorTests
{
    private static readonly IntPtr FakeHwnd = new(0x7788);

    private sealed class FakeNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public OverlayWindow.RECT CurrentRect = new()
        {
            Left = 100,
            Top = 100,
            Right = 340,
            Bottom = 236
        };

        public List<(int X, int Y, int Width, int Height, uint Flags)> ResizeCalls { get; } = new();

        public IntPtr GetHandle(Window window) => FakeHwnd;

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
        {
            if (cx > 0 || cy > 0)
                ResizeCalls.Add((X, Y, cx, cy, uFlags));
            return true;
        }

        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect)
        {
            lpRect = CurrentRect;
            return true;
        }

        public int GetWindowLong(IntPtr hWnd, int nIndex) => 0;
        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong) => 0;
    }

    private sealed class FakeWorkAreaProvider : IMonitorWorkAreaProvider
    {
        private static readonly Rect WorkArea = new(0, 0, 1920, 1040);

        public Rect GetPrimaryWorkArea() => WorkArea;
        public IReadOnlyList<Rect> GetAllWorkAreas() => new[] { WorkArea };
        public IReadOnlyList<MonitorInfo> GetAllMonitorInfos() => new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", WorkArea, true)
        };
    }

    [Theory]
    [InlineData(96, 112)]
    [InlineData(120, 140)]
    [InlineData(144, 168)]
    [InlineData(192, 224)]
    public void ToPhysicalPixels_UsesDpiScale(double dpi, int expected)
    {
        Assert.Equal(expected, OverlayWindowSizeCalculator.ToPhysicalPixels(112, dpi));
    }

    [Fact]
    public void CalculatePhysicalSize_UsesFixedWidthAndContentHeight()
    {
        var result = OverlayWindowSizeCalculator.CalculatePhysicalSize(136, 144, 144);

        Assert.Equal(168, result.Width);
        Assert.Equal(204, result.Height);
    }

    [Fact]
    public void CalculatePhysicalSize_InvalidDpiFallsBackToDefault()
    {
        var result = OverlayWindowSizeCalculator.CalculatePhysicalSize(136, 0, double.NaN);

        Assert.Equal(112, result.Width);
        Assert.Equal(136, result.Height);
    }

    [Fact]
    public void OverlayWindow_LoadedWithWideNativeRect_RequestsFixedWidth()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var native = new FakeNativeInterop();
            var window = new OverlayWindow(new FakeWorkAreaProvider(), native);

            window.Show();

            Assert.Equal(112, window.Width);
            Assert.Equal(112, window.MinWidth);
            Assert.Equal(112, window.MaxWidth);
            Assert.Contains(native.ResizeCalls, call => call.Width == 112);

            native.ResizeCalls.Clear();
            window.ReassertTopmost();
            Assert.Contains(native.ResizeCalls, call => call.Width == 112);

            window.PrepareForShutdown();
        });
    }

    [Theory]
    [InlineData(112, 112, false)]
    [InlineData(112, 113, false)]
    [InlineData(112, 114, true)]
    [InlineData(0, 112, false)]
    public void Differs_UsesOnePixelTolerance(int actual, int expected, bool differs)
    {
        Assert.Equal(differs, OverlayWindowSizeCalculator.Differs(actual, expected));
    }
}
