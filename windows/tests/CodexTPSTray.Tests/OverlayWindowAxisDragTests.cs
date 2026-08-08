using System;
using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class AxisLockCalculatorTests
{
    [Fact]
    public void WithinThreshold_ReturnsNone()
    {
        Assert.Equal(AxisLockDirection.None,
            AxisLockCalculator.DetermineDirection(2, 1, 4, AxisLockDirection.None));
    }

    [Fact]
    public void ExactlyAtThreshold_ReturnsNone()
    {
        // Must strictly exceed threshold on the dominant axis.
        Assert.Equal(AxisLockDirection.None,
            AxisLockCalculator.DetermineDirection(4, 0, 4, AxisLockDirection.None));
    }

    [Fact]
    public void XGreaterThanY_ReturnsHorizontal()
    {
        Assert.Equal(AxisLockDirection.Horizontal,
            AxisLockCalculator.DetermineDirection(10, 2, 4, AxisLockDirection.None));
    }

    [Fact]
    public void YGreaterThanX_ReturnsVertical()
    {
        Assert.Equal(AxisLockDirection.Vertical,
            AxisLockCalculator.DetermineDirection(2, 10, 4, AxisLockDirection.None));
    }

    [Fact]
    public void Tie_ReturnsVertical()
    {
        Assert.Equal(AxisLockDirection.Vertical,
            AxisLockCalculator.DetermineDirection(10, 10, 4, AxisLockDirection.None));
    }

    [Fact]
    public void AlreadyHorizontal_StaysHorizontalEvenIfVerticalLarger()
    {
        Assert.Equal(AxisLockDirection.Horizontal,
            AxisLockCalculator.DetermineDirection(0, 100, 4, AxisLockDirection.Horizontal));
    }

    [Fact]
    public void AlreadyVertical_StaysVerticalEvenIfHorizontalLarger()
    {
        Assert.Equal(AxisLockDirection.Vertical,
            AxisLockCalculator.DetermineDirection(100, 0, 4, AxisLockDirection.Vertical));
    }

    [Fact]
    public void NegativeDeltas_ComparedByAbsoluteValue()
    {
        Assert.Equal(AxisLockDirection.Horizontal,
            AxisLockCalculator.DetermineDirection(-20, -3, 4, AxisLockDirection.None));
        Assert.Equal(AxisLockDirection.Vertical,
            AxisLockCalculator.DetermineDirection(-3, -20, 4, AxisLockDirection.None));
    }
}

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

    // ── 1. Shift at press, horizontal lock ──

    [Fact]
    public void ShiftAtPress_HorizontalLock_KeepsTopConstant()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            // Below threshold: free drag.
            window.HandleDragMove(new System.Drawing.Point(503, 502), shiftHeld: true);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Above threshold: lock horizontal.
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            var last = LastMove(interop);
            Assert.Equal(260, last.X); // 200 + 60
            Assert.Equal(153, last.Y); // 150 + 3 (frozen at free-drag position)
        });
    }

    // ── 2. Shift at press, vertical lock ──

    [Fact]
    public void ShiftAtPress_VerticalLock_KeepsLeftConstant()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(503, 560), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            // Continue moving — Left frozen.
            window.HandleDragMove(new System.Drawing.Point(503, 580), shiftHeld: true);
            var last = LastMove(interop);
            Assert.Equal(203, last.X);
            Assert.Equal(230, last.Y);
        });
    }

    // ── 3. Free drag → press Shift, no jump at transition ──

    [Fact]
    public void FreeDrag_ThenShift_NoJumpAtTransition()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);
            int moveCountBefore = interop.MoveCalls.Count;
            int leftBefore = interop.CurrentRect.Left;
            int topBefore = interop.CurrentRect.Top;

            // Press Shift mid-drag.
            window.HandleDragMove(new System.Drawing.Point(535, 513), shiftHeld: true);

            // No new move call on transition — window position unchanged.
            Assert.Equal(moveCountBefore, interop.MoveCalls.Count);
            Assert.Equal(leftBefore, interop.CurrentRect.Left);
            Assert.Equal(topBefore, interop.CurrentRect.Top);
        });
    }

    // ── 4. Free drag → press Shift → horizontal lock ──

    [Fact]
    public void FreeDrag_ThenShift_HorizontalLock()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            // Free drag.
            window.HandleDragMove(new System.Drawing.Point(530, 510), shiftHeld: false);

            // Press Shift (transition — no move).
            window.HandleDragMove(new System.Drawing.Point(535, 513), shiftHeld: true);

            // Move horizontally — should lock horizontal.
            window.HandleDragMove(new System.Drawing.Point(570, 515), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            // Anchor was re-established at (535, 513) → window (230, 160).
            // delta = (570-535, 515-513) = (35, 2). Free pos = (265, 162).
            // Frozen Top = 162. Window = (265, 162).
            var last = LastMove(interop);
            Assert.Equal(265, last.X);
            Assert.Equal(162, last.Y);
        });
    }

    // ── 5. Free drag → press Shift → vertical lock ──

    [Fact]
    public void FreeDrag_ThenShift_VerticalLock()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(510, 530), shiftHeld: false);

            // Press Shift (transition — no move).
            window.HandleDragMove(new System.Drawing.Point(513, 535), shiftHeld: true);

            // Move vertically — should lock vertical.
            window.HandleDragMove(new System.Drawing.Point(515, 570), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            // Anchor at (513, 535) → window (210, 180).
            // delta = (515-513, 570-535) = (2, 35). Free pos = (212, 215).
            // Frozen Left = 212. Window = (212, 215).
            var last = LastMove(interop);
            Assert.Equal(212, last.X);
            Assert.Equal(215, last.Y);
        });
    }

    // ── 6. Horizontal lock → release Shift → free drag ──

    [Fact]
    public void HorizontalLock_ReleaseShift_FreeDragBothAxes()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Release Shift (transition — no move, re-anchor).
            window.HandleDragMove(new System.Drawing.Point(565, 506), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Free drag — both X and Y must change.
            window.HandleDragMove(new System.Drawing.Point(575, 520), shiftHeld: false);
            var last = LastMove(interop);
            // Anchor at (565, 506) → window (260, 153).
            // delta = (10, 14). Window = (270, 167).
            Assert.Equal(270, last.X);
            Assert.Equal(167, last.Y);
        });
    }

    // ── 7. Vertical lock → release Shift → free drag ──

    [Fact]
    public void VerticalLock_ReleaseShift_FreeDragBothAxes()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(503, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);

            // Release Shift (transition — no move, re-anchor).
            window.HandleDragMove(new System.Drawing.Point(506, 565), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Free drag — both X and Y must change.
            window.HandleDragMove(new System.Drawing.Point(520, 580), shiftHeld: false);
            var last = LastMove(interop);
            // Anchor at (506, 565) → window (203, 210).
            // delta = (14, 15). Window = (217, 225).
            Assert.Equal(217, last.X);
            Assert.Equal(225, last.Y);
        });
    }

    // ── 8. Horizontal lock → release Shift → press Shift → vertical lock ──

    [Fact]
    public void HorizontalLock_ReleaseShift_PressShift_VerticalLock()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Release Shift.
            window.HandleDragMove(new System.Drawing.Point(565, 506), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Press Shift again (transition — re-anchor at current pos).
            window.HandleDragMove(new System.Drawing.Point(567, 508), shiftHeld: true);

            // Move vertically — should lock vertical.
            window.HandleDragMove(new System.Drawing.Point(567, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);

            // Anchor at (567, 508) → window (260, 153).
            // delta = (0, 52). Free pos = (260, 205). Frozen Left = 260.
            var last = LastMove(interop);
            Assert.Equal(260, last.X);
            Assert.Equal(205, last.Y);
        });
    }

    // ── 9. Vertical lock → release Shift → press Shift → horizontal lock ──

    [Fact]
    public void VerticalLock_ReleaseShift_PressShift_HorizontalLock()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(503, 560), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);

            // Release Shift.
            window.HandleDragMove(new System.Drawing.Point(506, 565), shiftHeld: false);
            Assert.Equal(AxisLockDirection.None, window.CurrentAxisLockDirection);

            // Press Shift again.
            window.HandleDragMove(new System.Drawing.Point(508, 567), shiftHeld: true);

            // Move horizontally — should lock horizontal.
            window.HandleDragMove(new System.Drawing.Point(560, 567), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Anchor at (508, 567) → window (203, 210).
            // delta = (52, 0). Free pos = (255, 210). Frozen Top = 210.
            var last = LastMove(interop);
            Assert.Equal(255, last.X);
            Assert.Equal(210, last.Y);
        });
    }

    // ── 10. Multiple Shift press/release, no jumps ──

    [Fact]
    public void MultipleShiftToggles_NoJumps()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(520, 510), shiftHeld: false);
            // Window at (220, 160).

            // Toggle Shift on.
            window.HandleDragMove(new System.Drawing.Point(525, 513), shiftHeld: true);
            int leftAfterPress = interop.CurrentRect.Left;
            int topAfterPress = interop.CurrentRect.Top;

            // Toggle Shift off.
            window.HandleDragMove(new System.Drawing.Point(528, 516), shiftHeld: false);
            Assert.Equal(leftAfterPress, interop.CurrentRect.Left);
            Assert.Equal(topAfterPress, interop.CurrentRect.Top);

            // Toggle Shift on again.
            window.HandleDragMove(new System.Drawing.Point(530, 518), shiftHeld: true);
            Assert.Equal(leftAfterPress, interop.CurrentRect.Left);
            Assert.Equal(topAfterPress, interop.CurrentRect.Top);

            // Toggle Shift off again.
            window.HandleDragMove(new System.Drawing.Point(532, 520), shiftHeld: false);
            Assert.Equal(leftAfterPress, interop.CurrentRect.Left);
            Assert.Equal(topAfterPress, interop.CurrentRect.Top);
        });
    }

    // ── 11. Multiple Shift press/release, no duplicate DragCompleted ──

    [Fact]
    public void MultipleShiftToggles_NoDuplicateDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(520, 510), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(525, 513), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(530, 515), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(535, 518), shiftHeld: false);
            window.HandleDragMove(new System.Drawing.Point(540, 520), shiftHeld: true);
            window.EndDrag();

            Assert.Equal(1, dragCompletedCalls);
        });
    }

    // ── Below threshold with Shift: free drag ──

    [Fact]
    public void ShiftHeld_BelowThreshold_FreeDragStillMoves()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(502, 501), shiftHeld: true);

            Assert.False(window.IsAxisLocked);
            Assert.Equal(202, LastMove(interop).X);
            Assert.Equal(151, LastMove(interop).Y);
        });
    }

    // ── Equal displacement locks vertical ──

    [Fact]
    public void EqualDisplacement_LocksVertical()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(510, 510), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Vertical, window.CurrentAxisLockDirection);
            var last = LastMove(interop);
            // Free pos = (210, 160). Frozen Left = 210.
            Assert.Equal(210, last.X);
            Assert.Equal(160, last.Y);
        });
    }

    // ── No Shift: free drag ──

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

    // ── Direction persists while Shift held ──

    [Fact]
    public void DirectionLocked_ShiftStillHeld_StaysLocked()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);
            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);

            // Continue with Shift held — direction must persist.
            window.HandleDragMove(new System.Drawing.Point(580, 510), shiftHeld: true);

            Assert.Equal(AxisLockDirection.Horizontal, window.CurrentAxisLockDirection);
            var last = LastMove(interop);
            Assert.Equal(280, last.X);
            Assert.Equal(153, last.Y); // still frozen
        });
    }

    // ── EndDrag fires DragCompleted once ──

    [Fact]
    public void EndDrag_FiresDragCompletedOnceWithFinalPhysicalCoords()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();
            int dragCompletedCalls = 0;
            double savedLeft = 0, savedTop = 0;
            window.DragCompleted += (l, t) => { dragCompletedCalls++; savedLeft = l; savedTop = t; };

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);
            window.EndDrag();

            Assert.Equal(1, dragCompletedCalls);
            Assert.Equal(260.0, savedLeft);
            Assert.Equal(153.0, savedTop);
            Assert.False(window.IsDragging);

            // Second EndDrag is a no-op.
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

    // ── CancelDrag cleans state without DragCompleted ──

    [Fact]
    public void CancelDrag_CleansStateWithoutFiringDragCompleted()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _) = CreateWindow();
            int dragCompletedCalls = 0;
            window.DragCompleted += (_, _) => dragCompletedCalls++;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);

            window.CancelDrag();

            Assert.False(window.IsDragging);
            Assert.Equal(0, dragCompletedCalls);

            // Subsequent EndDrag must not fire DragCompleted either.
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

    // ── Size preservation ──

    [Fact]
    public void AxisDragMove_PreservesWindowSizeViaNosizeFlag()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();
            int originalWidth = interop.CurrentRect.Right - interop.CurrentRect.Left;
            int originalHeight = interop.CurrentRect.Bottom - interop.CurrentRect.Top;

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(700, 503), shiftHeld: true);

            int width = interop.CurrentRect.Right - interop.CurrentRect.Left;
            int height = interop.CurrentRect.Bottom - interop.CurrentRect.Top;
            Assert.Equal(originalWidth, width);
            Assert.Equal(originalHeight, height);
        });
    }

    // ── SetWindowPos flags ──

    [Fact]
    public void AxisDragMove_PreservesTopmostAndNoActivateFlags()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop) = CreateWindow();

            window.HandleMouseLeftButtonDown(new System.Drawing.Point(500, 500), shiftHeld: true);
            window.HandleDragMove(new System.Drawing.Point(560, 503), shiftHeld: true);

            var last = LastMove(interop);
            Assert.True((last.Flags & SWP_NOSIZE) != 0);
            Assert.True((last.Flags & SWP_NOZORDER) != 0);
            Assert.True((last.Flags & SWP_NOACTIVATE) != 0);
        });
    }

    // ── Locked prevents drag ──

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
