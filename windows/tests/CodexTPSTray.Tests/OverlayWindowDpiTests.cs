using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayWindowDpiTests
{
    private static readonly IntPtr FakeHwnd = new(0x5678);

    private sealed class FakeWindowNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public OverlayWindow.RECT CurrentRect;
        public List<(int X, int Y, uint Flags)> SetWindowPosCalls { get; } = new();
        public int ExtendedStyle;

        public IntPtr GetHandle(Window window) => FakeHwnd;

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
        {
            SetWindowPosCalls.Add((X, Y, uFlags));
            return true;
        }

        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect)
        {
            lpRect = CurrentRect;
            return true;
        }

        public int GetWindowLong(IntPtr hWnd, int nIndex) => ExtendedStyle;
        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
        {
            int old = ExtendedStyle;
            ExtendedStyle = dwNewLong;
            return old;
        }
    }

    private sealed class FakeWorkAreaProvider : IMonitorWorkAreaProvider
    {
        private readonly Rect _primary;
        private readonly IReadOnlyList<Rect> _all;
        private readonly IReadOnlyList<MonitorInfo> _infos;

        public FakeWorkAreaProvider(
            Rect primary,
            IReadOnlyList<Rect> all,
            IReadOnlyList<MonitorInfo> infos)
        {
            _primary = primary;
            _all = all;
            _infos = infos;
        }

        public Rect GetPrimaryWorkArea() => _primary;
        public IReadOnlyList<Rect> GetAllWorkAreas() => _all;
        public IReadOnlyList<MonitorInfo> GetAllMonitorInfos() => _infos;
    }

    private static FakeWorkAreaProvider CreateSingleMonitorProvider(double dpi = 96.0)
    {
        var rect = new Rect(0, 0, 1920, 1040);
        var info = new MonitorInfo(@"\\.\DISPLAY1", rect, true, dpi, dpi);
        return new FakeWorkAreaProvider(rect, new[] { rect }, new[] { info });
    }

    // SWP_NOMOVE = 0x0002 — the move call does NOT have this flag
    private const uint SWP_NOMOVE = 0x0002;

    private static (int X, int Y, uint Flags)? FindMoveCall(IList<(int X, int Y, uint Flags)> calls)
    {
        for (int i = calls.Count - 1; i >= 0; i--)
        {
            if ((calls[i].Flags & SWP_NOMOVE) == 0)
                return calls[i];
        }
        return null;
    }

    // =========================================================================
    // DpiRepositionGuard: generation-based DPI callback scheduling
    // =========================================================================

    public class DpiRepositionGuardTests
    {
        [Fact]
        public void Queue_ReturnsIncrementingGenerations()
        {
            var guard = new DpiRepositionGuard();
            long gen1 = guard.Queue();
            long gen2 = guard.Queue();
            long gen3 = guard.Queue();

            Assert.True(gen2 > gen1);
            Assert.True(gen3 > gen2);
        }

        [Fact]
        public void IsCurrent_TrueForLatestQueued()
        {
            var guard = new DpiRepositionGuard();
            long gen = guard.Queue();
            Assert.True(guard.IsCurrent(gen));
        }

        [Fact]
        public void IsCurrent_FalseForStaleGeneration()
        {
            var guard = new DpiRepositionGuard();
            long old = guard.Queue();
            guard.Queue(); // newer generation

            Assert.False(guard.IsCurrent(old));
        }

        [Fact]
        public void ConsecutiveQueueCalls_Merge_OnlyLastIsCurrent()
        {
            var guard = new DpiRepositionGuard();
            long gen1 = guard.Queue();
            long gen2 = guard.Queue();
            long gen3 = guard.Queue();

            Assert.False(guard.IsCurrent(gen1));
            Assert.False(guard.IsCurrent(gen2));
            Assert.True(guard.IsCurrent(gen3));
        }

        [Fact]
        public void Invalidate_MakesAllPreviousGenerationsStale()
        {
            var guard = new DpiRepositionGuard();
            long gen = guard.Queue();
            Assert.True(guard.IsCurrent(gen));

            guard.Invalidate();

            Assert.False(guard.IsCurrent(gen));
        }

        [Fact]
        public void Invalidate_QueuingAfterInvalidate_NewGenerationIsCurrent()
        {
            var guard = new DpiRepositionGuard();
            long oldGen = guard.Queue();
            guard.Invalidate();
            long newGen = guard.Queue();

            Assert.False(guard.IsCurrent(oldGen));
            Assert.True(guard.IsCurrent(newGen));
        }

        [Fact]
        public void HideShowCycle_OldCallbackDoesNotExecuteOnNewShow()
        {
            var guard = new DpiRepositionGuard();

            // Simulate DPI event while visible
            long genBeforeHide = guard.Queue();
            Assert.True(guard.IsCurrent(genBeforeHide));

            // Window hides
            guard.Invalidate();

            // Window shows again, new DPI event
            long genAfterShow = guard.Queue();

            // Old callback from before hide must not execute
            Assert.False(guard.IsCurrent(genBeforeHide));
            Assert.True(guard.IsCurrent(genAfterShow));
        }
    }

    // =========================================================================
    // OverlayWindow DPI reposition behavior
    // =========================================================================

    [Fact]
    public void ExecuteDpiRepositionCore_PresetMode_ReanchorsToPresetPosition()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(96);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 100, Top = 100, Right = 300, Bottom = 220 };

            var window = new OverlayWindow(provider, interop);
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = OverlayPositionPreset.TopRight,
                OverlayMonitorDeviceName = null
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // TopRight at 96 DPI: X = 1920 - 200 - 16 = 1704, Y = 0 + 16 = 16
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(1704, moveCall!.Value.X);
            Assert.Equal(16, moveCall.Value.Y);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_CustomMode_FiresDragCompletedWithPhysicalCoords()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(192);
            var interop = new FakeWindowNativeInterop();
            // Window at physical (3000, 500), size 200x120
            interop.CurrentRect = new OverlayWindow.RECT { Left = 3000, Top = 500, Right = 3200, Bottom = 620 };

            var window = new OverlayWindow(provider, interop);
            double savedLeft = double.NaN, savedTop = double.NaN;
            window.DragCompleted += (l, t) => { savedLeft = l; savedTop = t; };

            // Custom position mode (no preset)
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = null,
                OverlayLeft = 999,
                OverlayTop = 999
            });

            window.ExecuteDpiRepositionCore();

            // Must save physical pixel coordinates, NOT DIP-converted values
            Assert.Equal(3000.0, savedLeft, 0);
            Assert.Equal(500.0, savedTop, 0);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_PresetSwitch_TopRightToBottomLeft_UsesNewPreset()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(96);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 1704, Top = 16, Right = 1904, Bottom = 136 };

            var window = new OverlayWindow(provider, interop);

            // Start with TopRight
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = OverlayPositionPreset.TopRight
            });

            // Switch to BottomLeft
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = OverlayPositionPreset.BottomLeft
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // BottomLeft at 96 DPI: X = 0 + 16 = 16, Y = 1040 - 120 - 16 = 904
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(16, moveCall!.Value.X);
            Assert.Equal(904, moveCall.Value.Y);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_DragToCustom_DoesNotRevertToPreset()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(96);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 500, Top = 300, Right = 700, Bottom = 420 };

            var window = new OverlayWindow(provider, interop);

            // Start with preset
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = OverlayPositionPreset.TopRight
            });

            // Simulate drag to custom position: settings change to custom
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = null,
                OverlayLeft = 500.0,
                OverlayTop = 300.0
            });

            double savedLeft = double.NaN, savedTop = double.NaN;
            window.DragCompleted += (l, t) => { savedLeft = l; savedTop = t; };

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // Should NOT have moved to a preset position (no SetWindowPos move call)
            // Should have fired DragCompleted with current physical coords
            Assert.Equal(500.0, savedLeft, 0);
            Assert.Equal(300.0, savedTop, 0);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_CustomDpiSync_SettingsStayConsistent()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(144);
            var interop = new FakeWindowNativeInterop();
            // Window at physical (800, 600) after DPI change
            interop.CurrentRect = new OverlayWindow.RECT { Left = 800, Top = 600, Right = 1000, Bottom = 720 };

            var window = new OverlayWindow(provider, interop);

            // Custom position with old coords
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = null,
                OverlayLeft = 750.0,
                OverlayTop = 580.0
            });

            double syncedLeft = double.NaN, syncedTop = double.NaN;
            window.DragCompleted += (l, t) => { syncedLeft = l; syncedTop = t; };

            window.ExecuteDpiRepositionCore();

            // The synced coordinates should be the final physical position, not the old saved coords
            Assert.Equal(800.0, syncedLeft, 0);
            Assert.Equal(600.0, syncedTop, 0);
            Assert.NotEqual(750.0, syncedLeft);
            Assert.NotEqual(580.0, syncedTop);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_PresetOnHighDpi_ScalesMarginsCorrectly()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(192);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 0, Top = 0, Right = 200, Bottom = 120 };

            var window = new OverlayWindow(provider, interop);
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = OverlayPositionPreset.TopRight
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // TopRight at 192 DPI: margin = 16 DIP * (192/96) = 32 physical
            // X = 1920 - 200 - 32 = 1688, Y = 0 + 32 = 32
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(1688, moveCall!.Value.X);
            Assert.Equal(32, moveCall.Value.Y);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_PresetOnNegativeCoordinateMonitor_AnchorsCorrectly()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var secondaryWorkArea = new Rect(-1600, 0, 1600, 860);
            var primaryWorkArea = new Rect(0, 0, 1920, 1040);

            var monitors = new[]
            {
                new MonitorInfo(@"\\.\DISPLAY2", secondaryWorkArea, false, 120, 120),
                new MonitorInfo(@"\\.\DISPLAY1", primaryWorkArea, true, 96, 96)
            };
            var allAreas = new List<Rect> { secondaryWorkArea, primaryWorkArea };
            var provider = new FakeWorkAreaProvider(primaryWorkArea, allAreas, monitors);

            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = -1600, Top = 0, Right = -1400, Bottom = 120 };

            var window = new OverlayWindow(provider, interop);
            window.UpdateSettings(TraySettings.Default with
            {
                OverlayPosition = OverlayPositionPreset.TopLeft,
                OverlayMonitorDeviceName = @"\\.\DISPLAY2"
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // TopLeft on secondary at 120 DPI: margin = 16 * (120/96) = 20 physical
            // X = -1600 + 20 = -1580, Y = 0 + 20 = 20
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(-1580, moveCall!.Value.X);
            Assert.Equal(20, moveCall.Value.Y);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_NoSettings_DoesNothing()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(96);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 100, Top = 100, Right = 300, Bottom = 220 };

            var window = new OverlayWindow(provider, interop);
            // Don't call UpdateSettings - _currentSettings is null

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // No move calls (only style/topmost calls from ApplyExtendedStyles/EnsureTopmost)
            // No DragCompleted should fire
            bool anyMoveCall = false;
            foreach (var call in interop.SetWindowPosCalls)
            {
                if (call.X != 0 || call.Y != 0)
                    anyMoveCall = true;
            }
            Assert.False(anyMoveCall);
        });
    }

    // =========================================================================
    // OverlayPositionCalculator: production conversion entry points
    // =========================================================================

    [Fact]
    public void CalculatePosition_CustomPosition_OnNegativeCoordinateMonitor_WorksCorrectly()
    {
        var primaryWorkArea = new Rect(0, 0, 1920, 1040);
        var secondaryWorkArea = new Rect(-1920, 0, 1920, 1040);
        var allWorkAreas = new List<Rect> { secondaryWorkArea, primaryWorkArea };
        var margin = new Thickness(16);

        var (left, top) = OverlayPositionCalculator.CalculatePosition(
            savedLeft: -1500,
            savedTop: 200,
            overlaySize: new System.Windows.Size(200, 120),
            primaryWorkArea: primaryWorkArea,
            allWorkAreas: allWorkAreas,
            margin: margin);

        Assert.Equal(-1500.0, left);
        Assert.Equal(200.0, top);
    }

    [Fact]
    public void CalculatePosition_OutOfBounds_FallsBackToDefault()
    {
        var primaryWorkArea = new Rect(0, 0, 1920, 1040);
        var allWorkAreas = new List<Rect> { primaryWorkArea };
        var margin = new Thickness(16);

        var (left, top) = OverlayPositionCalculator.CalculatePosition(
            savedLeft: 5000,
            savedTop: 5000,
            overlaySize: new System.Windows.Size(200, 120),
            primaryWorkArea: primaryWorkArea,
            allWorkAreas: allWorkAreas,
            margin: margin);

        double expectedLeft = 1920 - 200 - 16;
        double expectedTop = 0 + 16;
        Assert.Equal(expectedLeft, left, 0);
        Assert.Equal(expectedTop, top, 0);
    }

    // =========================================================================
    // MonitorPanelWindow DPI reposition: uses window center, not Cursor.Position
    // =========================================================================

    [Fact]
    public void MonitorPanel_ResolveScreenFromWindowRect_UsesWindowCenter_NotCursor()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var viewModel = new MonitorPanelViewModel(TraySettings.Default);
            var window = new MonitorPanelWindow(viewModel);

            // Window at (100, 200) to (300, 400) — center is (200, 300)
            var rect = new MonitorPanelWindow.RECT { Left = 100, Top = 200, Right = 300, Bottom = 400 };

            System.Drawing.Point? capturedPoint = null;
            Screen? expectedScreen = Screen.PrimaryScreen;
            window.ScreenFromPointResolver = pt =>
            {
                capturedPoint = pt;
                return expectedScreen;
            };

            var result = window.ResolveScreenFromWindowRect(rect);

            // Verify the resolver was called with the window center, not Cursor.Position
            Assert.NotNull(capturedPoint);
            Assert.Equal(200, capturedPoint!.Value.X); // (100 + 300) / 2
            Assert.Equal(300, capturedPoint.Value.Y); // (200 + 400) / 2
            Assert.Same(expectedScreen, result);
        });
    }

    [Fact]
    public void MonitorPanel_ResolveScreenFromWindowRect_ZeroSize_ReturnsNull()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var viewModel = new MonitorPanelViewModel(TraySettings.Default);
            var window = new MonitorPanelWindow(viewModel);

            bool resolverCalled = false;
            window.ScreenFromPointResolver = pt =>
            {
                resolverCalled = true;
                return Screen.PrimaryScreen;
            };

            var rect = new MonitorPanelWindow.RECT { Left = 100, Top = 200, Right = 100, Bottom = 200 };
            var result = window.ResolveScreenFromWindowRect(rect);

            Assert.Null(result);
            Assert.False(resolverCalled);
        });
    }

    [Fact]
    public void MonitorPanel_ResolveScreenFromWindowRect_NegativeCoordinateMonitor_UsesCenter()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var viewModel = new MonitorPanelViewModel(TraySettings.Default);
            var window = new MonitorPanelWindow(viewModel);

            // Window on a negative-coordinate monitor: (-1600, 0) to (-1400, 120)
            // Center is (-1500, 60)
            var rect = new MonitorPanelWindow.RECT { Left = -1600, Top = 0, Right = -1400, Bottom = 120 };

            System.Drawing.Point? capturedPoint = null;
            window.ScreenFromPointResolver = pt =>
            {
                capturedPoint = pt;
                return Screen.PrimaryScreen;
            };

            window.ResolveScreenFromWindowRect(rect);

            Assert.NotNull(capturedPoint);
            Assert.Equal(-1500, capturedPoint!.Value.X); // (-1600 + -1400) / 2
            Assert.Equal(60, capturedPoint.Value.Y); // (0 + 120) / 2
        });
    }
}
