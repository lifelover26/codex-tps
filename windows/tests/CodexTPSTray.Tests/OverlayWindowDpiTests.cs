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
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
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
    public void ExecuteDpiRepositionCore_CustomMode_ReanchorsFromRatios_WithoutDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(192);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 100, Top = 100, Right = 300, Bottom = 220 };

            var window = new OverlayWindow(provider, interop);
            bool dragFired = false;
            window.DragCompleted += (_, _) => dragFired = true;

            // Custom position with normalized ratios (no legacy absolute coords).
            window.UpdateSettings(TraySettings.Default with
            {
                PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays,
                SharedPosition = new OverlayPositionState.Custom(0.5, 0.25)
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // Auto-reposition must NOT fire DragCompleted (no fake drag).
            Assert.False(dragFired);

            // Re-anchor from ratios against the 1920x1040 work area:
            // xRange = 1920 - 200 = 1720, yRange = 1040 - 120 = 920
            // left = 0.5 * 1720 = 860, top = 0.25 * 920 = 230
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(860, moveCall!.Value.X);
            Assert.Equal(230, moveCall.Value.Y);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_CustomMode_RepeatedDpiCalls_IdempotentNoDrift()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(144);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 860, Top = 230, Right = 1060, Bottom = 350 };

            var window = new OverlayWindow(provider, interop);
            int dragCount = 0;
            window.DragCompleted += (_, _) => dragCount++;

            window.UpdateSettings(TraySettings.Default with
            {
                PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays,
                SharedPosition = new OverlayPositionState.Custom(0.5, 0.25)
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();
            var firstMove = FindMoveCall(interop.SetWindowPosCalls);

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();
            var secondMove = FindMoveCall(interop.SetWindowPosCalls);

            // Repeated DPI notifications are idempotent: same position, no drift,
            // and never a DragCompleted.
            Assert.NotNull(firstMove);
            Assert.NotNull(secondMove);
            Assert.Equal(firstMove!.Value.X, secondMove!.Value.X);
            Assert.Equal(firstMove.Value.Y, secondMove.Value.Y);
            Assert.Equal(0, dragCount);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_CustomMode_LegacyMigrates_Reanchors_WithoutDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(96);
            var interop = new FakeWindowNativeInterop();
            interop.CurrentRect = new OverlayWindow.RECT { Left = 500, Top = 300, Right = 700, Bottom = 420 };

            var window = new OverlayWindow(provider, interop);
            bool dragFired = false;
            window.DragCompleted += (_, _) => dragFired = true;
            TraySettings? migrated = null;
            window.CustomPositionSettingsUpdated += s => migrated = s;

            // Legacy absolute coordinates, no ratios yet.
            window.UpdateSettings(TraySettings.Default with
            {
                SharedPosition = null,
                OverlayLeft = 500.0,
                OverlayTop = 300.0
            });

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            Assert.False(dragFired);
            Assert.NotNull(migrated);
            // Migration clears legacy coords and produces normalized ratios.
            Assert.Null(migrated!.OverlayLeft);
            Assert.Null(migrated.OverlayTop);
            var migratedCustom = Assert.IsType<OverlayPositionState.Custom>(migrated.SharedPosition);
            Assert.Equal(500.0 / 1720.0, migratedCustom.XRatio, 6);
            Assert.Equal(300.0 / 920.0, migratedCustom.YRatio, 6);

            // Position is preserved through migration (same relative spot).
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(500, moveCall!.Value.X);
            Assert.Equal(300, moveCall.Value.Y);
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
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopRight)
            });

            // Switch to BottomLeft
            window.UpdateSettings(TraySettings.Default with
            {
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.BottomLeft)
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
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopRight)
            });

            // Simulate drag to custom position: settings change to custom with ratios.
            window.UpdateSettings(TraySettings.Default with
            {
                PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays,
                SharedPosition = new OverlayPositionState.Custom(0.5, 0.25)
            });

            bool dragFired = false;
            window.DragCompleted += (_, _) => dragFired = true;

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // Must NOT revert to the TopRight preset (1704, 16) and must NOT fire
            // a fake DragCompleted. It re-anchors from the saved ratios (860, 230).
            Assert.False(dragFired);
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(860, moveCall!.Value.X);
            Assert.Equal(230, moveCall.Value.Y);
        });
    }

    [Fact]
    public void ExecuteDpiRepositionCore_CustomDpiSync_ReanchorsFromRatios_WithoutDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var provider = CreateSingleMonitorProvider(144);
            var interop = new FakeWindowNativeInterop();
            // Window at physical (800, 600) after DPI change — an intermediate
            // WPF placement that must NOT be written back to settings.
            interop.CurrentRect = new OverlayWindow.RECT { Left = 800, Top = 600, Right = 1000, Bottom = 720 };

            var window = new OverlayWindow(provider, interop);

            // Custom position with normalized ratios (the source of truth).
            window.UpdateSettings(TraySettings.Default with
            {
                PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays,
                SharedPosition = new OverlayPositionState.Custom(0.5, 0.25)
            });

            bool dragFired = false;
            window.DragCompleted += (_, _) => dragFired = true;

            interop.SetWindowPosCalls.Clear();
            window.ExecuteDpiRepositionCore();

            // The intermediate physical position (800, 600) is never saved; no
            // fake DragCompleted fires. The window re-anchors to the ratios (860, 230).
            Assert.False(dragFired);
            var moveCall = FindMoveCall(interop.SetWindowPosCalls);
            Assert.NotNull(moveCall);
            Assert.Equal(860, moveCall!.Value.X);
            Assert.Equal(230, moveCall.Value.Y);
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
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopRight)
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
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopLeft),
                OverlayTargetMonitorId = @"\\.\DISPLAY2"
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

    // =========================================================================
    // MonitorPanelWindow DPI layout normalization: re-asserts fixed 390 DIP
    // width after a DPI transition and on re-show after hide.
    // =========================================================================

    [Fact]
    public void MonitorPanel_WindowRoot_EnablesPixelRounding()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));

            // UseLayoutRounding + SnapsToDevicePixels on the Window root ensure the
            // WPF-rendered background exactly tiles the Win32 client area at
            // non-integer DPI scales, preventing the 1px black HWND edge.
            Assert.True(window.UseLayoutRounding);
            Assert.True(window.SnapsToDevicePixels);
        });
    }

    [Fact]
    public void MonitorPanel_RootContentGrid_EnablesPixelRounding()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));

            var rootGrid = Assert.IsType<System.Windows.Controls.Grid>(window.Content);
            Assert.True(rootGrid.UseLayoutRounding);
            Assert.True(rootGrid.SnapsToDevicePixels);
        });
    }

    [Fact]
    public void MonitorPanel_DeclaredWidthAndSizeToContent_AreFixedByXaml()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));

            // The XAML-declared fixed width and height-only SizeToContent must
            // survive construction (the pixel-rounding fix must not change them).
            Assert.Equal(MonitorPanelWindow.PanelLogicalWidth, window.Width);
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
        });
    }

    [Fact]
    public void MonitorPanel_NormalizePanelLayout_RestoresFixedLogicalWidthAndHeightOnlySizeToContent()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));

            // Simulate width drift left by a PerMonitorV2 DPI transition.
            window.Width = 480;
            window.SizeToContent = SizeToContent.WidthAndHeight;

            MonitorPanelWindow.RECT rect = window.NormalizePanelLayout();

            Assert.Equal(MonitorPanelWindow.PanelLogicalWidth, window.Width);
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
            // No live HWND in a unit test -> default rect, but the layout pass ran.
            Assert.Equal(1, window.NormalizePanelLayoutCallCount);
            Assert.Equal(0, rect.Left);
        });
    }

    [Fact]
    public void MonitorPanel_ExecuteDpiRepositionCore_NormalizesLayoutBeforeReposition()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));
            window.Width = 520;
            window.SizeToContent = SizeToContent.WidthAndHeight;

            int before = window.NormalizePanelLayoutCallCount;
            window.ExecuteDpiRepositionCore();

            // Normalization must run as part of the DPI reposition path.
            Assert.Equal(before + 1, window.NormalizePanelLayoutCallCount);
            Assert.Equal(MonitorPanelWindow.PanelLogicalWidth, window.Width);
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
        });
    }

    [Fact]
    public void MonitorPanel_MultipleDpiCallbacks_OnlyLatestDispatchRuns()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));
            int before = window.NormalizePanelLayoutCallCount;

            long gen1 = window.QueueDpiChangedGeneration();
            long gen2 = window.QueueDpiChangedGeneration();
            long gen3 = window.QueueDpiChangedGeneration();

            bool ran1 = window.RunDpiChangedDispatch(gen1);
            bool ran2 = window.RunDpiChangedDispatch(gen2);
            bool ran3 = window.RunDpiChangedDispatch(gen3);

            // Only the latest generation is current; older ones are deduped.
            Assert.False(ran1);
            Assert.False(ran2);
            Assert.True(ran3);
            Assert.Equal(before + 1, window.NormalizePanelLayoutCallCount);
        });
    }

    [Fact]
    public void MonitorPanel_HideInvalidatesStaleDpiCallback()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));
            int before = window.NormalizePanelLayoutCallCount;

            // DPI event fires and queues a callback while the panel is visible.
            long gen = window.QueueDpiChangedGeneration();

            // Panel hides before the callback runs.
            window.InvalidateDpiGeneration();

            // The stale callback must NOT run or normalize.
            bool ran = window.RunDpiChangedDispatch(gen);

            Assert.False(ran);
            Assert.Equal(before, window.NormalizePanelLayoutCallCount);
        });
    }

    [Fact]
    public void MonitorPanel_AfterHide_NewDpiCallbackRunsOnReshow()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));
            int before = window.NormalizePanelLayoutCallCount;

            long staleGen = window.QueueDpiChangedGeneration();
            window.InvalidateDpiGeneration(); // hide
            long freshGen = window.QueueDpiChangedGeneration(); // new DPI event after re-show

            bool staleRan = window.RunDpiChangedDispatch(staleGen);
            bool freshRan = window.RunDpiChangedDispatch(freshGen);

            Assert.False(staleRan);
            Assert.True(freshRan);
            Assert.Equal(before + 1, window.NormalizePanelLayoutCallCount);
        });
    }

    [Fact]
    public void MonitorPanel_ShowNearTray_AfterDpiChangeWhileHidden_NormalizesLayout()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = new MonitorPanelWindow(new MonitorPanelViewModel(TraySettings.Default));
            // Keep the window invisible throughout so the test never steals focus
            // or flashes on screen. ShowNearTray captures/restores Opacity, so
            // starting at 0 keeps it at 0 across the show/hide/reshow cycle.
            window.Opacity = 0;

            try
            {
                // Lifecycle: show, then hide (panel hidden with a live HWND).
                window.Show();
                window.Hide();

                // Simulate DPI change while hidden: corrupt the logical width as
                // if the stale physical HWND size leaked into the property.
                window.Width = 460;
                window.SizeToContent = SizeToContent.WidthAndHeight;
                int before = window.NormalizePanelLayoutCallCount;

                // Re-show via ShowNearTray — must normalize at the current DPI.
                window.ShowNearTray();

                Assert.True(window.NormalizePanelLayoutCallCount > before);
                Assert.Equal(MonitorPanelWindow.PanelLogicalWidth, window.Width);
                Assert.Equal(SizeToContent.Height, window.SizeToContent);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }
}
