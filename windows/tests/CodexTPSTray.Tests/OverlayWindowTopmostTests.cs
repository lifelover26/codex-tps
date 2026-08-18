using System;
using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayWindowTopmostTests
{
    private static readonly IntPtr FakeHwnd = new(0x1234);
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private sealed class FakeWindowNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public List<(IntPtr HWnd, IntPtr InsertAfter, int X, int Y, int CX, int CY, uint Flags)> SetWindowPosCalls { get; } = new();
        public int GetWindowLongCallCount { get; private set; }
        public int SetWindowLongCallCount { get; private set; }
        public int GetHandleCallCount { get; private set; }

        private int _windowLongStyle = 0;
        private OverlayWindow.RECT _windowRect;

        public FakeWindowNativeInterop()
        {
            _windowRect = new OverlayWindow.RECT
            {
                Left = 100,
                Top = 100,
                Right = 300,
                Bottom = 200
            };
        }

        public IntPtr GetHandle(Window window)
        {
            GetHandleCallCount++;
            return FakeHwnd;
        }

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
        {
            SetWindowPosCalls.Add((hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags));
            if (X != 0 || Y != 0)
            {
                _windowRect.Left = X;
                _windowRect.Top = Y;
                _windowRect.Right = X + (_windowRect.Right - _windowRect.Left);
                _windowRect.Bottom = Y + (_windowRect.Bottom - _windowRect.Top);
            }
            return true;
        }

        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect)
        {
            lpRect = _windowRect;
            return true;
        }

        public int GetWindowLong(IntPtr hWnd, int nIndex)
        {
            GetWindowLongCallCount++;
            return _windowLongStyle;
        }

        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
        {
            SetWindowLongCallCount++;
            int old = _windowLongStyle;
            _windowLongStyle = dwNewLong;
            return old;
        }

        public int TopmostAssertionCount
        {
            get
            {
                int count = 0;
                foreach (var call in SetWindowPosCalls)
                {
                    if (call.InsertAfter == HWND_TOPMOST)
                        count++;
                }
                return count;
            }
        }

        public (IntPtr HWnd, IntPtr InsertAfter, int X, int Y, int CX, int CY, uint Flags)? GetLastTopmostCall()
        {
            for (int i = SetWindowPosCalls.Count - 1; i >= 0; i--)
            {
                if (SetWindowPosCalls[i].InsertAfter == HWND_TOPMOST)
                    return SetWindowPosCalls[i];
            }
            return null;
        }
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

    [Fact]
    public void UpdateSettings_LockChangedToTrue_AssertsTopmostAfterStylesApplied()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            var settings = TraySettings.Default with { OverlayLocked = true };

            window.UpdateSettings(settings);

            Assert.True(interop.TopmostAssertionCount >= 1, "Expected at least one HWND_TOPMOST SetWindowPos call after locking");
            var topmostCall = interop.GetLastTopmostCall();
            Assert.NotNull(topmostCall);
            Assert.Equal(FakeHwnd, topmostCall!.Value.HWnd);
            Assert.Equal(HWND_TOPMOST, topmostCall.Value.InsertAfter);
            Assert.Equal(0, topmostCall.Value.X);
            Assert.Equal(0, topmostCall.Value.Y);
            Assert.Equal(0, topmostCall.Value.CX);
            Assert.Equal(0, topmostCall.Value.CY);
            uint expectedFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED;
            Assert.Equal(expectedFlags, topmostCall.Value.Flags);
        });
    }

    [Fact]
    public void UpdateSettings_LockChangedToFalse_AssertsTopmostAfterStylesApplied()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            var lockedSettings = TraySettings.Default with { OverlayLocked = true };
            window.UpdateSettings(lockedSettings);
            int topmostCountAfterLock = interop.TopmostAssertionCount;

            var unlockedSettings = TraySettings.Default with { OverlayLocked = false };
            window.UpdateSettings(unlockedSettings);

            Assert.True(interop.TopmostAssertionCount > topmostCountAfterLock,
                "Expected additional HWND_TOPMOST call after unlocking");
        });
    }

    [Fact]
    public void UpdateSettings_NoLockChange_DoesNotReAssertTopmost()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            var lockedSettings = TraySettings.Default with { OverlayLocked = true };
            window.UpdateSettings(lockedSettings);
            int countAfterLock = interop.TopmostAssertionCount;

            var stillLockedSettings = TraySettings.Default with { OverlayLocked = true };
            window.UpdateSettings(stillLockedSettings);

            Assert.Equal(countAfterLock, interop.TopmostAssertionCount);
        });
    }

    [Fact]
    public void MoveToPosition_AfterSuccessfulMove_AssertsTopmost()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            window.MoveToPosition(500, 300);

            var lastTopmost = interop.GetLastTopmostCall();
            Assert.NotNull(lastTopmost);
            Assert.Equal(HWND_TOPMOST, lastTopmost!.Value.InsertAfter);

            int lastTopmostIndex = -1;
            int moveCallIndex = -1;
            for (int i = 0; i < interop.SetWindowPosCalls.Count; i++)
            {
                var call = interop.SetWindowPosCalls[i];
                if (call.X == 500 && call.Y == 300 && call.InsertAfter == IntPtr.Zero)
                    moveCallIndex = i;
                if (call.InsertAfter == HWND_TOPMOST)
                    lastTopmostIndex = i;
            }

            Assert.True(moveCallIndex >= 0, "Expected a SetWindowPos move call");
            Assert.True(lastTopmostIndex > moveCallIndex, "Topmost assertion should occur AFTER the move call");
        });
    }

    [Fact]
    public void ResetPosition_AfterSuccessfulMove_AssertsTopmost()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            var settings = TraySettings.Default with
            {
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
                OverlayLeft = null,
                OverlayTop = null
            };

            window.ResetPosition(settings);

            var lastTopmost = interop.GetLastTopmostCall();
            Assert.NotNull(lastTopmost);
            Assert.Equal(HWND_TOPMOST, lastTopmost!.Value.InsertAfter);
        });
    }

    [Fact]
    public void ResetPosition_WithExplicitCoordinates_AssertsTopmost()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            var settings = TraySettings.Default with
            {
                OverlayLeft = 150.0,
                OverlayTop = 250.0
            };

            window.ResetPosition(settings);

            Assert.True(interop.TopmostAssertionCount >= 1, "Expected topmost assertion after ResetPosition");
            var lastTopmost = interop.GetLastTopmostCall();
            Assert.Equal(HWND_TOPMOST, lastTopmost!.Value.InsertAfter);
        });
    }

    [Fact]
    public void MoveToPosition_TopmostDoesNotStealFocus()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            window.MoveToPosition(100, 200);

            var topmostCall = interop.GetLastTopmostCall();
            Assert.NotNull(topmostCall);
            Assert.True((topmostCall!.Value.Flags & SWP_NOACTIVATE) != 0,
                "Topmost assertion must include SWP_NOACTIVATE to avoid stealing focus");
        });
    }
}
