using System;
using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

// ════════════════════════════════════════════════════════════════════════════
// Pure calculator tests — AxisLockCalculator (no WPF window required)
// ════════════════════════════════════════════════════════════════════════════

public class AxisLockCalculatorTests
{
    // ── SmoothStep ──

    [Fact]
    public void SmoothStep_BelowEdge0_ReturnsZero()
    {
        Assert.Equal(0.0, AxisLockCalculator.SmoothStep(0.15, 0.50, 0.10));
    }

    [Fact]
    public void SmoothStep_AboveEdge1_ReturnsOne()
    {
        Assert.Equal(1.0, AxisLockCalculator.SmoothStep(0.15, 0.50, 0.60));
    }

    [Fact]
    public void SmoothStep_AtEdge0_ReturnsZero()
    {
        Assert.Equal(0.0, AxisLockCalculator.SmoothStep(0.15, 0.50, 0.15));
    }

    [Fact]
    public void SmoothStep_AtEdge1_ReturnsOne()
    {
        Assert.Equal(1.0, AxisLockCalculator.SmoothStep(0.15, 0.50, 0.50));
    }

    [Fact]
    public void SmoothStep_AtMidpoint_ReturnsHalf()
    {
        // SmoothStep(0, 1, 0.5) = 0.25 * 2 = 0.5
        Assert.Equal(0.5, AxisLockCalculator.SmoothStep(0.0, 1.0, 0.5));
    }

    // ── Dominance ──

    [Fact]
    public void Dominance_PureHorizontal_ReturnsOne()
    {
        Assert.Equal(1.0, AxisLockCalculator.Dominance(100, 0));
    }

    [Fact]
    public void Dominance_PureVertical_ReturnsMinusOne()
    {
        Assert.Equal(-1.0, AxisLockCalculator.Dominance(0, 100));
    }

    [Fact]
    public void Dominance_Diagonal_ReturnsZero()
    {
        Assert.Equal(0.0, AxisLockCalculator.Dominance(50, 50));
    }

    [Fact]
    public void Dominance_ZeroDisplacement_ReturnsZero()
    {
        Assert.Equal(0.0, AxisLockCalculator.Dominance(0, 0));
    }

    [Fact]
    public void Dominance_NegativeDeltas_UsesAbsoluteValues()
    {
        // |−100| − |−5|  /  |−100| + |−5|  =  95 / 105
        Assert.Equal(95.0 / 105.0, AxisLockCalculator.Dominance(-100, -5));
    }

    [Fact]
    public void PixelThresholds_UseOriginalValues()
    {
        Assert.Equal(6.0, AxisLockCalculator.EnterThreshold);
        Assert.Equal(8.0, AxisLockCalculator.ResetThreshold);
        Assert.Equal(8.0, AxisLockCalculator.SwitchMargin);
        Assert.Equal(12.0, AxisLockCalculator.TransitionWidth);
    }

    // ── ComputeTarget: hard axis constraints ──

    [Fact]
    public void ComputeTarget_StrongHorizontal_HardAxisConstraint()
    {
        // dx=100, dy=5 → dominance ≈ 0.905 > LockFullRatio → full H suppression.
        // Top is pressed to originTop; Left follows dx freely.
        var (left, top) = AxisLockCalculator.ComputeTarget(100, 5, 200, 150);
        Assert.Equal(300, left);
        Assert.Equal(150, top);
    }

    [Fact]
    public void ComputeTarget_StrongVertical_HardAxisConstraint()
    {
        // dx=5, dy=100 → dominance ≈ −0.905 → full V suppression.
        // Left is pressed to originLeft; Top follows dy freely.
        var (left, top) = AxisLockCalculator.ComputeTarget(5, 100, 200, 150);
        Assert.Equal(200, left);
        Assert.Equal(250, top);
    }

    // ── ComputeTarget: free movement ──

    [Fact]
    public void ComputeTarget_Diagonal_FreeMovement()
    {
        // dominance = 0 → no suppression → window follows cursor.
        var (left, top) = AxisLockCalculator.ComputeTarget(50, 50, 200, 150);
        Assert.Equal(250, left);
        Assert.Equal(200, top);
    }

    [Fact]
    public void ComputeTarget_InsideSwitchMargin_FollowsCursor()
    {
        var (left, top) = AxisLockCalculator.ComputeTarget(54, 48, 200, 150);
        Assert.Equal(254, left);
        Assert.Equal(198, top);
    }

    [Fact]
    public void ComputeTarget_ZeroDisplacement_ReturnsOrigin()
    {
        var (left, top) = AxisLockCalculator.ComputeTarget(0, 0, 200, 150);
        Assert.Equal(200, left);
        Assert.Equal(150, top);
    }

    [Fact]
    public void ComputeTarget_ExplicitHorizontalState_UsesOriginRail()
    {
        var (left, top) = AxisLockCalculator.ComputeTarget(
            80, 40, 200, 150, AxisLockDirection.Horizontal);

        Assert.Equal(280, left);
        Assert.Equal(150, top);
    }

    [Fact]
    public void ComputeTarget_ExplicitVerticalState_UsesOriginRail()
    {
        var (left, top) = AxisLockCalculator.ComputeTarget(
            40, 80, 200, 150, AxisLockDirection.Vertical);

        Assert.Equal(200, left);
        Assert.Equal(230, top);
    }

    [Fact]
    public void ComputeTarget_NegativeDeltas_StrongHorizontal()
    {
        var (left, top) = AxisLockCalculator.ComputeTarget(-100, -5, 200, 150);
        Assert.Equal(100, left);
        Assert.Equal(150, top);
    }

    // ── ComputeTarget: transition zone (partial suppression) ──

    [Fact]
    public void ComputeTarget_TransitionZone_PartialSuppression()
    {
        // Horizontal leads by 14px: halfway through the 8..20px entry band.
        // Free (40,26) blends halfway to H (40,0).
        var (left, top) = AxisLockCalculator.ComputeTarget(40, 26, 200, 150);
        Assert.Equal(240, left);
        Assert.Equal(163, top);
    }

    [Fact]
    public void ComputeTarget_TransitionZone_SymmetricForVertical()
    {
        // Symmetric halfway blend from free movement to the V rail.
        var (left, top) = AxisLockCalculator.ComputeTarget(26, 40, 200, 150);
        Assert.Equal(213, left);
        Assert.Equal(190, top);
    }

    [Fact]
    public void ComputeTarget_Continuous_NoJumpNearDiagonal()
    {
        // The 8px entry margin keeps a near-diagonal nudge free.
        var (left0, top0) = AxisLockCalculator.ComputeTarget(50, 50, 200, 150);
        var (left1, top1) = AxisLockCalculator.ComputeTarget(51, 49, 200, 150);
        Assert.Equal(250, left0);
        Assert.Equal(200, top0);
        Assert.Equal(251, left1);
        Assert.Equal(199, top1);
    }

    // ── EvaluateDirection: entering a direction from None ──

    [Fact]
    public void EvaluateDirection_None_StrongHorizontal_LocksHorizontal()
    {
        var r = AxisLockCalculator.EvaluateDirection(100, 5, AxisLockDirection.None);
        Assert.Equal(AxisLockDirection.Horizontal, r.Direction);
        Assert.True(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_None_StrongVertical_LocksVertical()
    {
        var r = AxisLockCalculator.EvaluateDirection(5, 100, AxisLockDirection.None);
        Assert.Equal(AxisLockDirection.Vertical, r.Direction);
        Assert.True(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_None_NearDiagonal_StaysNone()
    {
        var r = AxisLockCalculator.EvaluateDirection(50, 50, AxisLockDirection.None);
        Assert.Equal(AxisLockDirection.None, r.Direction);
        Assert.False(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_None_InsideSwitchMargin_StaysNone()
    {
        var r = AxisLockCalculator.EvaluateDirection(54, 48, AxisLockDirection.None);
        Assert.Equal(AxisLockDirection.None, r.Direction);
        Assert.False(r.AxisChanged);
    }

    // ── EvaluateDirection: hysteresis (staying locked) ──

    [Fact]
    public void EvaluateDirection_Horizontal_NearDiagonal_StaysHorizontal()
    {
        var r = AxisLockCalculator.EvaluateDirection(50, 50, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.Horizontal, r.Direction);
        Assert.False(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_Horizontal_BelowSwitchThreshold_StaysHorizontal()
    {
        // Vertical leads by exactly 8px; switching requires more than 8px.
        var r = AxisLockCalculator.EvaluateDirection(40, 48, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.Horizontal, r.Direction);
        Assert.False(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_Vertical_NearDiagonal_StaysVertical()
    {
        var r = AxisLockCalculator.EvaluateDirection(50, 50, AxisLockDirection.Vertical);
        Assert.Equal(AxisLockDirection.Vertical, r.Direction);
        Assert.False(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_Vertical_BelowSwitchThreshold_StaysVertical()
    {
        // Horizontal leads by exactly 8px; switching requires more than 8px.
        var r = AxisLockCalculator.EvaluateDirection(48, 40, AxisLockDirection.Vertical);
        Assert.Equal(AxisLockDirection.Vertical, r.Direction);
        Assert.False(r.AxisChanged);
    }

    // ── EvaluateDirection: switching axes ──

    [Fact]
    public void EvaluateDirection_Horizontal_StrongVertical_Switches()
    {
        var r = AxisLockCalculator.EvaluateDirection(5, 100, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.Vertical, r.Direction);
        Assert.True(r.AxisChanged);
    }

    [Fact]
    public void EvaluateDirection_Vertical_StrongHorizontal_Switches()
    {
        var r = AxisLockCalculator.EvaluateDirection(100, 5, AxisLockDirection.Vertical);
        Assert.Equal(AxisLockDirection.Horizontal, r.Direction);
        Assert.True(r.AxisChanged);
    }

    [Fact]
    public void Project_HorizontalToVertical_UsesVisibleTransitionBand()
    {
        AxisProjection start = AxisLockCalculator.Project(
            40, 48, 200, 150, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.Horizontal, start.Direction);
        Assert.Equal(240, start.Left);
        Assert.Equal(150, start.Top);
        Assert.Equal(0.0, start.TransitionProgress);

        AxisProjection midpoint = AxisLockCalculator.Project(
            40, 54, 200, 150, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.Horizontal, midpoint.Direction);
        Assert.Equal(220, midpoint.Left);
        Assert.Equal(177, midpoint.Top);
        Assert.Equal(0.5, midpoint.TransitionProgress);

        AxisProjection complete = AxisLockCalculator.Project(
            40, 60, 200, 150, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.Vertical, complete.Direction);
        Assert.Equal(200, complete.Left);
        Assert.Equal(210, complete.Top);
        Assert.Equal(1.0, complete.TransitionProgress);
    }

    // ── EvaluateDirection: zero displacement ──

    [Fact]
    public void EvaluateDirection_ZeroDisplacement_ReturnsNone()
    {
        var r = AxisLockCalculator.EvaluateDirection(0, 0, AxisLockDirection.Horizontal);
        Assert.Equal(AxisLockDirection.None, r.Direction);
        Assert.True(r.AxisChanged);
    }

    // ── EvaluateDirection: negative deltas ──

    [Fact]
    public void EvaluateDirection_NegativeDeltas_ComparedByAbsoluteValue()
    {
        Assert.Equal(AxisLockDirection.Horizontal,
            AxisLockCalculator.EvaluateDirection(-100, -5, AxisLockDirection.None).Direction);
        Assert.Equal(AxisLockDirection.Vertical,
            AxisLockCalculator.EvaluateDirection(-5, -100, AxisLockDirection.None).Direction);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Integration tests — OverlayWindow drag with continuous axis locking
// ════════════════════════════════════════════════════════════════════════════

public class OverlayWindowAxisDragTests
{
    private static readonly IntPtr FakeHwnd = new(0xABCD);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private sealed class FakeWindowNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public OverlayWindow.RECT CurrentRect;
        public List<(int X, int Y, uint Flags)> MoveCalls { get; } = new();

        public FakeWindowNativeInterop()
        {
            CurrentRect = new OverlayWindow.RECT { Left = 200, Top = 150, Right = 320, Bottom = 230 };
        }

        public IntPtr GetHandle(Window window) => FakeHwnd;

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
        {
            if ((uFlags & SWP_NOMOVE) == 0)
            {
                int width = CurrentRect.Right - CurrentRect.Left;
                int height = CurrentRect.Bottom - CurrentRect.Top;
                CurrentRect.Left = X;
                CurrentRect.Top = Y;
                CurrentRect.Right = X + width;
                CurrentRect.Bottom = Y + height;
                MoveCalls.Add((X, Y, uFlags));
            }
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
        public Rect GetPrimaryWorkArea() => new(0, 0, 1920, 1040);
        public IReadOnlyList<Rect> GetAllWorkAreas() => new[] { new Rect(0, 0, 1920, 1040) };
        public IReadOnlyList<MonitorInfo> GetAllMonitorInfos() => new[]
        {
            new MonitorInfo(DeviceName: @"\\.\DISPLAY1", WorkingArea: new Rect(0, 0, 1920, 1040), IsPrimary: true)
        };
    }

    private static (OverlayWindow window, FakeWindowNativeInterop interop) CreateWindow()
    {
        var interop = new FakeWindowNativeInterop();
        var window = new OverlayWindow(new FakeWorkAreaProvider(), interop);
        return (window, interop);
    }

    private static (int X, int Y, uint Flags) LastMove(FakeWindowNativeInterop interop) =>
        interop.MoveCalls[^1];

    // ── 1. Strong horizontal: hard axis constraint ──

    [Fact]
    public void StrongHorizontal_HardAxisConstraint()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            // dx=100, dy=10 → dominance ≈ 0.818 → full H suppression.
            window.HandleDragMove(new System.Drawing.Point(600, 510), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(300, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top); // pressed to originTop
        });
    }

    // ── 2. Strong vertical: hard axis constraint ──

    [Fact]
    public void StrongVertical_HardAxisConstraint()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            // dx=10, dy=100 → dominance ≈ −0.818 → full V suppression.
            window.HandleDragMove(new System.Drawing.Point(510, 600), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left); // pressed to originLeft
            Assert.Equal(250, interop.CurrentRect.Top);
        });
    }

    // ── 3. Far arcs use a short state-driven transition ──

    [Fact]
    public void Arc_HorizontalToVertical_WithoutReturningToOrigin_SwitchesSmoothly()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // Strong H: Window → (300, 150).
            window.HandleDragMove(new System.Drawing.Point(600, 510), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(300, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // A diagonal remains on the current H rail because the opposite
            // axis has not crossed the 8px switch margin.
            window.HandleDragMove(new System.Drawing.Point(550, 550), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(250, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Vertical leads by 14px: halfway through the fixed 12px blend.
            window.HandleDragMove(new System.Drawing.Point(540, 554), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(220, interop.CurrentRect.Left);
            Assert.Equal(177, interop.CurrentRect.Top);

            // Vertical leads by 20px: transition completes on the V rail.
            window.HandleDragMove(new System.Drawing.Point(540, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(210, interop.CurrentRect.Top);

            // Strong V continues on the hard V rail.
            window.HandleDragMove(new System.Drawing.Point(510, 600), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(250, interop.CurrentRect.Top);
        });
    }

    // ── 4. Intermediate points move monotonically through the blend ──

    [Fact]
    public void Arc_WithIntermediatePoints_TransitionIsMonotonic()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // Strong H: Window → (300, 150).
            window.HandleDragMove(new System.Drawing.Point(600, 510), shiftHeld: true);
            int prevLeft = interop.CurrentRect.Left;
            int prevTop = interop.CurrentRect.Top;
            Assert.Equal(300, prevLeft);
            Assert.Equal(150, prevTop);

            // Still on the H rail.
            window.HandleDragMove(new System.Drawing.Point(570, 530), shiftHeld: true);
            int left1 = interop.CurrentRect.Left;
            int top1 = interop.CurrentRect.Top;
            Assert.Equal(270, left1);
            Assert.Equal(150, top1);

            // Just inside the transition: movement has started toward V but
            // remains close to the H rail.
            window.HandleDragMove(new System.Drawing.Point(545, 555), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            int left2 = interop.CurrentRect.Left;
            int top2 = interop.CurrentRect.Top;
            Assert.Equal(242, left2);
            Assert.Equal(154, top2);

            // Midpoint and completion progress monotonically toward V.
            window.HandleDragMove(new System.Drawing.Point(540, 554), shiftHeld: true);
            Assert.Equal(220, interop.CurrentRect.Left);
            Assert.Equal(177, interop.CurrentRect.Top);

            window.HandleDragMove(new System.Drawing.Point(540, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(210, interop.CurrentRect.Top);
        });
    }

    // ── 5. Diagonal jitter from None: no frequent direction flip ──

    [Fact]
    public void DiagonalJitter_FromNone_NoDirectionFlip()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // dominance = 0 → None.
            window.HandleDragMove(new System.Drawing.Point(550, 550), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Horizontal leads by only 4px, inside the 8px margin.
            window.HandleDragMove(new System.Drawing.Point(552, 548), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Vertical leads by only 4px, inside the 8px margin.
            window.HandleDragMove(new System.Drawing.Point(548, 552), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // dominance = 0 → None.
            window.HandleDragMove(new System.Drawing.Point(550, 550), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
        });
    }

    // ── 6. Diagonal jitter from Horizontal: no frequent direction flip ──

    [Fact]
    public void DiagonalJitter_FromHorizontal_NoDirectionFlip()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // Lock H.
            window.HandleDragMove(new System.Drawing.Point(600, 510), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Move near diagonal — hysteresis keeps H.
            window.HandleDragMove(new System.Drawing.Point(550, 550), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            window.HandleDragMove(new System.Drawing.Point(552, 548), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            window.HandleDragMove(new System.Drawing.Point(548, 552), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            window.HandleDragMove(new System.Drawing.Point(550, 550), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
        });
    }

    // ── 7. H → short blend → V → short blend → H ──

    [Fact]
    public void HorizontalVerticalHorizontal_Cycle_ReturnToOrigin_NoDrift()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // H: Window → (300, 150).
            window.HandleDragMove(new System.Drawing.Point(600, 510), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Complete an H → V transition.
            window.HandleDragMove(new System.Drawing.Point(540, 554), shiftHeld: true);
            Assert.Equal(220, interop.CurrentRect.Left);
            Assert.Equal(177, interop.CurrentRect.Top);
            window.HandleDragMove(new System.Drawing.Point(540, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(210, interop.CurrentRect.Top);

            // Complete a V → H transition through the same fixed-width band.
            window.HandleDragMove(new System.Drawing.Point(554, 540), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(227, interop.CurrentRect.Left);
            Assert.Equal(170, interop.CurrentRect.Top);
            window.HandleDragMove(new System.Drawing.Point(560, 540), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(260, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Cursor returns to origin: Window → (200, 150). No drift.
            window.HandleDragMove(new System.Drawing.Point(500, 500), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 8. Multiple blended switches: no accumulated drift ──

    [Fact]
    public void MultipleSwitches_NoAccumulatedDrift()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // H.
            window.HandleDragMove(new System.Drawing.Point(600, 510), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(300, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // V through the fixed transition band.
            window.HandleDragMove(new System.Drawing.Point(540, 554), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(540, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(210, interop.CurrentRect.Top);

            // H again.
            window.HandleDragMove(new System.Drawing.Point(554, 540), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 540), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(260, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // V again.
            window.HandleDragMove(new System.Drawing.Point(540, 554), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(540, 560), shiftHeld: true);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(210, interop.CurrentRect.Top);

            // H again.
            window.HandleDragMove(new System.Drawing.Point(554, 540), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 540), shiftHeld: true);
            Assert.Equal(260, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Cursor returns to origin: Window → (200, 150). No drift.
            window.HandleDragMove(new System.Drawing.Point(500, 500), shiftHeld: true);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 9. Cursor returns to origin after H lock → exact original ──

    [Fact]
    public void CursorReturnsToOrigin_WindowReturnsToOriginal()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // H: Window → (230, 150).
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Continue H: Window → (260, 150).
            window.HandleDragMove(new System.Drawing.Point(560, 502), shiftHeld: true);
            Assert.Equal(260, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Cursor returns exactly to origin: Window → (200, 150).
            window.HandleDragMove(new System.Drawing.Point(500, 500), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 10. Horizontal path is reversible (no drift along the rail) ──

    [Fact]
    public void HorizontalPath_IsReversible()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // Lock H: Window → (230, 150).
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(230, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Add vertical mouse displacement: stays H. Top stays at originTop.
            window.HandleDragMove(new System.Drawing.Point(540, 510), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(240, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Mouse returns along X: Window → (220, 150). No drift.
            window.HandleDragMove(new System.Drawing.Point(520, 500), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(220, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 11. Release Shift → immediate free drag from the original origin ──

    [Fact]
    public void ReleaseShift_ImmediateFreeDrag()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            // Lock H: Window → (230, 150).
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Continue H: Window → (240, 150).
            window.HandleDragMove(new System.Drawing.Point(540, 502), shiftHeld: true);
            Assert.Equal(240, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Release Shift: the transition frame is processed with the new
            // state. The origin is NOT re-anchored, so the window immediately
            // jumps to origin + total dx/dy = (200+43, 150+4) = (243, 154).
            window.HandleDragMove(new System.Drawing.Point(543, 504), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
            Assert.Equal(243, interop.CurrentRect.Left);
            Assert.Equal(154, interop.CurrentRect.Top);

            // Free drag continues from the same mouse-down origin, not from the
            // release position: (200+55, 150+20) = (255, 170).
            window.HandleDragMove(new System.Drawing.Point(555, 520), shiftHeld: false);
            Assert.Equal(255, interop.CurrentRect.Left);
            Assert.Equal(170, interop.CurrentRect.Top);
        });
    }

    // ── 12. Press/release Shift uses the mouse-down origin throughout ──

    [Fact]
    public void ShiftPressRelease_UsesOriginalOriginThroughout()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            // Free drag: Window → (220, 160).
            window.HandleDragMove(new System.Drawing.Point(520, 510), shiftHeld: false);
            Assert.Equal(220, interop.CurrentRect.Left);
            Assert.Equal(160, interop.CurrentRect.Top);

            // Press Shift — the transition frame is processed (no re-anchor).
            // dx=25, dy=13 from the mouse-down origin (500,500). Horizontal
            // leads by 12px, partway through the 8..20px blend, so the window
            // moves to (225, 160) with axis still None.
            window.HandleDragMove(new System.Drawing.Point(525, 513), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
            Assert.Equal(225, interop.CurrentRect.Left);
            Assert.Equal(160, interop.CurrentRect.Top);

            // Lock H: dx=45, dy=13 from the mouse-down origin. Horizontal leads
            // by 32px → full H rail through the original Top=150.
            // target = (200+45, 150) = (245, 150).
            window.HandleDragMove(new System.Drawing.Point(545, 513), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(245, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Continue H: Window → (255, 150).
            window.HandleDragMove(new System.Drawing.Point(555, 513), shiftHeld: true);
            Assert.Equal(255, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Release Shift — transition frame processed, origin untouched.
            // Free drag from mouse-down origin: (200+57, 150+15) = (257, 165).
            window.HandleDragMove(new System.Drawing.Point(557, 515), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
            Assert.Equal(257, interop.CurrentRect.Left);
            Assert.Equal(165, interop.CurrentRect.Top);

            // Free drag continues from the mouse-down origin: (200+70, 150+20) = (270, 170).
            window.HandleDragMove(new System.Drawing.Point(570, 520), shiftHeld: false);
            Assert.Equal(270, interop.CurrentRect.Left);
            Assert.Equal(170, interop.CurrentRect.Top);
        });
    }

    // ── 13. Free drag then Shift still works (origin stays at mouse-down) ──

    [Fact]
    public void FreeDrag_ThenShift_HorizontalLock()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);

            // Press Shift — transition frame is processed against the mouse-down
            // origin (no re-anchor). dx=35, dy=13 → horizontal rail.
            window.HandleDragMove(new System.Drawing.Point(535, 513), shiftHeld: true);

            // Move horizontally — lock horizontal.
            // (560, 515): dx=60, dy=15 from the mouse-down origin. H rail.
            window.HandleDragMove(new System.Drawing.Point(560, 515), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
        });
    }

    // ── 13a. Free drag then press Shift uses the mouse-down origin ──

    [Fact]
    public void FreeDrag_ThenPressShift_UsesOriginalMouseDownOrigin()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            // Initial window (200, 150). Mouse down at (500, 500), no Shift.
            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);

            // Free drag to (530, 510): Window → (230, 160).
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);
            Assert.Equal(230, interop.CurrentRect.Left);
            Assert.Equal(160, interop.CurrentRect.Top);

            // Press Shift at (535, 513): dx=35, dy=13 from the mouse-down origin.
            // Horizontal leads by 22px → full H rail through the ORIGINAL Top=150,
            // not the press-time Top=160. Window → (235, 150).
            window.HandleDragMove(new System.Drawing.Point(535, 513), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(235, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 13b. Release Shift restores free drag from the original origin ──

    [Fact]
    public void ReleaseShift_RestoresFreeDragFromOriginalOrigin()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // H lock: Window → (240, 150).
            window.HandleDragMove(new System.Drawing.Point(540, 502), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(240, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Continue H: Window → (260, 150).
            window.HandleDragMove(new System.Drawing.Point(560, 502), shiftHeld: true);
            Assert.Equal(260, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Release Shift at (565, 510): the current frame immediately restores
            // free drag from the mouse-down origin — NOT accumulated from (260, 150).
            // target = (200+65, 150+10) = (265, 160).
            window.HandleDragMove(new System.Drawing.Point(565, 510), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);
            Assert.Equal(265, interop.CurrentRect.Left);
            Assert.Equal(160, interop.CurrentRect.Top);

            // Continue free drag: still from the mouse-down origin.
            // (570, 520) → (200+70, 150+20) = (270, 170).
            window.HandleDragMove(new System.Drawing.Point(570, 520), shiftHeld: false);
            Assert.Equal(270, interop.CurrentRect.Left);
            Assert.Equal(170, interop.CurrentRect.Top);
        });
    }

    // ── 13c. Release then press Shift re-evaluates axis from the original origin ──

    [Fact]
    public void ReleaseThenPressShift_ReevaluatesAxisFromOriginalOrigin()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);

            // H lock: Window → (240, 150).
            window.HandleDragMove(new System.Drawing.Point(540, 502), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Release Shift and free-move to a vertical-dominant total displacement.
            // (545, 510) → (245, 160); (510, 600) → (210, 250).
            window.HandleDragMove(new System.Drawing.Point(545, 510), shiftHeld: false);
            Assert.Equal(245, interop.CurrentRect.Left);
            Assert.Equal(160, interop.CurrentRect.Top);
            window.HandleDragMove(new System.Drawing.Point(510, 600), shiftHeld: false);
            Assert.Equal(210, interop.CurrentRect.Left);
            Assert.Equal(250, interop.CurrentRect.Top);

            // Press Shift at (510, 600): dx=10, dy=100 from the mouse-down origin.
            // Vertical leads by 90px → full V rail through the ORIGINAL Left=200.
            // target = (200, 150+100) = (200, 250).
            window.HandleDragMove(new System.Drawing.Point(510, 600), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(250, interop.CurrentRect.Top);
        });
    }

    // ── 13d. Repeated Shift toggles do not change the drag origin ──

    [Fact]
    public void RepeatedShiftToggles_DoNotChangeDragOrigin()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);

            // Free drag: Window → (230, 160).
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);

            // Press Shift at (540, 520): dx=40, dy=20 → H rail. Window → (240, 150).
            window.HandleDragMove(new System.Drawing.Point(540, 520), shiftHeld: true);
            Assert.Equal(240, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Release Shift at (550, 530): free drag from origin. Window → (250, 180).
            window.HandleDragMove(new System.Drawing.Point(550, 530), shiftHeld: false);
            Assert.Equal(250, interop.CurrentRect.Left);
            Assert.Equal(180, interop.CurrentRect.Top);

            // Press Shift at (560, 540): dx=60, dy=40 → H rail. Window → (260, 150).
            window.HandleDragMove(new System.Drawing.Point(560, 540), shiftHeld: true);
            Assert.Equal(260, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Release Shift at (570, 550): free drag. Window → (270, 200).
            window.HandleDragMove(new System.Drawing.Point(570, 550), shiftHeld: false);
            Assert.Equal(270, interop.CurrentRect.Left);
            Assert.Equal(200, interop.CurrentRect.Top);

            // Press Shift at (580, 560): dx=80, dy=60 → H rail. Window → (280, 150).
            window.HandleDragMove(new System.Drawing.Point(580, 560), shiftHeld: true);
            Assert.Equal(280, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);

            // Release Shift at (590, 570): free drag. Window → (290, 220).
            window.HandleDragMove(new System.Drawing.Point(590, 570), shiftHeld: false);
            Assert.Equal(290, interop.CurrentRect.Left);
            Assert.Equal(220, interop.CurrentRect.Top);

            // Cursor returns exactly to the mouse-down coordinate: Window must
            // return exactly to the original (200, 150) — no accumulated drift.
            window.HandleDragMove(new System.Drawing.Point(500, 500), shiftHeld: false);
            Assert.Equal(200, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 13e. The Shift transition frame is processed, not skipped ──

    [Fact]
    public void ShiftTransitionFrame_IsProcessed()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);

            // Free drag: Window → (230, 160). One move recorded.
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);
            Assert.Single(interop.MoveCalls);

            // Press Shift at (540, 520): this transition frame must NOT be a
            // no-op return. dx=40, dy=20 → H rail. Window → (240, 150).
            int movesBeforeTransition = interop.MoveCalls.Count;
            window.HandleDragMove(new System.Drawing.Point(540, 520), shiftHeld: true);
            Assert.Equal(movesBeforeTransition + 1, interop.MoveCalls.Count);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            Assert.Equal(240, interop.CurrentRect.Left);
            Assert.Equal(150, interop.CurrentRect.Top);
        });
    }

    // ── 14. Near-diagonal small movement: essentially free, not locked ──

    [Fact]
    public void NearDiagonalSmallMovement_EssentiallyFree()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            // (503, 502): both movement and directional lead are below the
            // physical thresholds, so the window follows the cursor freely.
            window.HandleDragMove(new System.Drawing.Point(503, 502), shiftHeld: true);

            Assert.False(window.IsAxisLocked);
            Assert.Equal(203, LastMove(interop).X);
            Assert.Equal(152, LastMove(interop).Y);
        });
    }

    // ── 15. No Shift: free drag ──

    [Fact]
    public void NoShift_FreeDrag_WindowFollowsMouse()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);

            Assert.False(window.IsAxisLocked);
            var last = LastMove(interop);
            Assert.Equal(230, last.X);
            Assert.Equal(160, last.Y);
        });
    }

    // ── 16. DragCompleted fires exactly once ──

    [Fact]
    public void DragCompleted_FiresOnce()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(540, 502), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(550, 560), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(620, 565), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(625, 570), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(627, 572), shiftHeld: true);
            window.EndDrag();

            Assert.Equal(1, dragCompletedCalls);
        });
    }

    // ── 17. EndDrag fires DragCompleted once ──

    [Fact]
    public void EndDrag_FiresDragCompletedOnce()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);
            window.EndDrag();

            Assert.Equal(1, dragCompletedCalls);
            Assert.False(window.IsDragging);

            window.EndDrag();
            Assert.Equal(1, dragCompletedCalls);
        });
    }

    [Fact]
    public void EndDrag_FreeDragAlsoFiresDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);
            window.EndDrag();

            Assert.Equal(1, dragCompletedCalls);
        });
    }

    [Fact]
    public void EndDrag_WithoutBegin_IsNoOp()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.EndDrag();

            Assert.Equal(0, dragCompletedCalls);
            Assert.False(window.IsDragging);
        });
    }

    // ── 18. CancelDrag cleans state without DragCompleted ──

    [Fact]
    public void CancelDrag_CleansStateWithoutFiringDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);

            window.CancelDrag();

            Assert.False(window.IsDragging);
            Assert.Equal(0, dragCompletedCalls);

            window.EndDrag();
            Assert.Equal(0, dragCompletedCalls);
        });
    }

    [Fact]
    public void MoveAfterCancel_IsIgnored()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();
            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.CancelDrag();

            int before = interop.MoveCalls.Count;
            window.HandleDragMove(new System.Drawing.Point(700, 700), shiftHeld: true);

            Assert.Equal(before, interop.MoveCalls.Count);
        });
    }

    // ── 19. Size preservation ──

    [Fact]
    public void AxisDragMove_PreservesWindowSize()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();
            int originalWidth = interop.CurrentRect.Right - interop.CurrentRect.Left;
            int originalHeight = interop.CurrentRect.Bottom - interop.CurrentRect.Top;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 502), shiftHeld: true);

            int width = interop.CurrentRect.Right - interop.CurrentRect.Left;
            int height = interop.CurrentRect.Bottom - interop.CurrentRect.Top;
            Assert.Equal(originalWidth, width);
            Assert.Equal(originalHeight, height);
        });
    }

    // ── 20. SetWindowPos flags ──

    [Fact]
    public void AxisDragMove_PreservesTopmostAndNoActivateFlags()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(530, 502), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 502), shiftHeld: true);

            var last = LastMove(interop);
            Assert.True((last.Flags & SWP_NOSIZE) != 0);
            Assert.True((last.Flags & SWP_NOZORDER) != 0);
            Assert.True((last.Flags & SWP_NOACTIVATE) != 0);
        });
    }

    // ── 21. Locked prevents drag ──

    [Fact]
    public void Locked_PreventsDragStart()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();
            window.UpdateSettings(TraySettings.Default with { OverlayLocked = true });

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);

            Assert.False(window.IsDragging);
            Assert.Empty(interop.MoveCalls);
        });
    }
}
