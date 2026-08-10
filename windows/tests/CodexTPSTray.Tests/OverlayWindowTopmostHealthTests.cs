using System;
using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayWindowTopmostHealthTests
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private sealed class HealthFakeInterop : OverlayWindow.IWindowNativeInterop
    {
        private OverlayWindow.RECT _rect = new() { Left = 100, Top = 100, Right = 300, Bottom = 200 };

        public int ExtendedStyle { get; set; }
        public List<(IntPtr InsertAfter, uint Flags)> SetWindowPosCalls { get; } = new();
        public int GetWindowLongCallCount { get; private set; }

        public IntPtr GetHandle(Window window) => new(0x1234);

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
        {
            SetWindowPosCalls.Add((hWndInsertAfter, uFlags));
            if (hWndInsertAfter == HWND_TOPMOST)
                ExtendedStyle |= WS_EX_TOPMOST;
            if (X != 0 || Y != 0)
            {
                _rect.Left = X;
                _rect.Top = Y;
            }
            return true;
        }

        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect)
        {
            lpRect = _rect;
            return true;
        }

        public int GetWindowLong(IntPtr hWnd, int nIndex)
        {
            GetWindowLongCallCount++;
            return ExtendedStyle;
        }

        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
        {
            int old = ExtendedStyle;
            ExtendedStyle = dwNewLong;
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
    }

    private sealed class ZeroHwndInterop : OverlayWindow.IWindowNativeInterop
    {
        public IntPtr GetHandle(Window window) => IntPtr.Zero;
        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags) => true;
        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect) { lpRect = default; return false; }
        public int GetWindowLong(IntPtr hWnd, int nIndex) => 0;
        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong) => 0;
    }

    private sealed class HealthFakeTimer : ITopmostHealthTimer
    {
        public bool IsStarted { get; private set; }
        public Action? Tick { get; private set; }

        public void Start(TimeSpan interval, Action tick)
        {
            IsStarted = true;
            Tick = tick;
        }

        public void Stop()
        {
            IsStarted = false;
            Tick = null;
        }

        public void FireTick() => Tick?.Invoke();
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

    private static (OverlayWindow, HealthFakeInterop, FakeDispatcher, HealthFakeTimer) CreateWindow()
    {
        var interop = new HealthFakeInterop { ExtendedStyle = WS_EX_TOPMOST };
        var dispatcher = new FakeDispatcher();
        var timer = new HealthFakeTimer();
        var window = new OverlayWindow(new FakeWorkAreaProvider(), interop, timer, dispatcher);
        return (window, interop, dispatcher, timer);
    }

    [Fact]
    public void IsNativeTopmostSet_BitSet_ReturnsTrue()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, _, _) = CreateWindow();
            interop.ExtendedStyle = WS_EX_TOPMOST;

            Assert.True(window.IsNativeTopmostSet());
        });
    }

    [Fact]
    public void IsNativeTopmostSet_BitMissing_ReturnsFalse()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, _, _) = CreateWindow();
            interop.ExtendedStyle = 0;

            Assert.False(window.IsNativeTopmostSet());
        });
    }

    [Fact]
    public void IsNativeTopmostSet_ZeroHwnd_ReturnsTrue()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var dispatcher = new FakeDispatcher();
            var timer = new HealthFakeTimer();
            var window = new OverlayWindow(new FakeWorkAreaProvider(), new ZeroHwndInterop(), timer, dispatcher);

            Assert.True(window.IsNativeTopmostSet());
        });
    }

    [Fact]
    public void HwndHook_WindowPosChanged_QueuesHealthCheck()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, dispatcher, _) = CreateWindow();

            window.InvokeHwndHookForTest(WM_WINDOWPOSCHANGED);

            Assert.Single(dispatcher.QueuedActions);
        });
    }

    [Fact]
    public void HwndHook_OtherMessage_DoesNotQueueHealthCheck()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, dispatcher, _) = CreateWindow();

            window.InvokeHwndHookForTest(0x0001);

            Assert.Empty(dispatcher.QueuedActions);
        });
    }

    [Fact]
    public void HwndHook_AttachedAfterRealShow_DetachedAfterShutdown()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, _, _) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                Assert.False(window.IsHwndHookAttachedForTest);

                window.Show();
                Assert.True(window.IsHwndHookAttachedForTest);

                window.PrepareForShutdown();
                Assert.False(window.IsHwndHookAttachedForTest);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void UpdateSettings_TracksOverlayEnabled()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, _, _) = CreateWindow();

            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });
            Assert.True(window.IsOverlayEnabledForTest);

            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = false });
            Assert.False(window.IsOverlayEnabledForTest);
        });
    }

    [Fact]
    public void Show_StartsHealthMonitor_Hide_StopsHealthMonitor()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, _, timer) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                Assert.True(window.TopmostHealthMonitorForTest.IsRunningForTest);
                Assert.True(timer.IsStarted);

                window.Hide();
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);
                Assert.False(timer.IsStarted);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void Show_WhileDisabled_DoesNotStartMonitor()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, _, timer) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = false });

            try
            {
                window.Show();
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);
                Assert.False(timer.IsStarted);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void VisibleWindow_EnabledThroughUpdateSettings_StartsMonitor()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, dispatcher, timer) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = false });

            try
            {
                window.Show();
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);

                window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });
                Assert.True(window.TopmostHealthMonitorForTest.IsRunningForTest);
                Assert.True(timer.IsStarted);

                dispatcher.ExecuteAll();
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void VisibleWindow_DisabledThroughUpdateSettings_StopsMonitor()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, _, timer) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                Assert.True(window.TopmostHealthMonitorForTest.IsRunningForTest);

                window.UpdateSettings(TraySettings.Default with { OverlayEnabled = false });
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);
                Assert.False(timer.IsStarted);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void Show_Hide_Show_CyclesMonitorStartStop()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, _, _) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                Assert.True(window.TopmostHealthMonitorForTest.IsRunningForTest);

                window.Hide();
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);

                window.Show();
                Assert.True(window.TopmostHealthMonitorForTest.IsRunningForTest);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void HealthCheck_BitMissing_RecoversWithOneTopmostCall()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, dispatcher, timer) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                dispatcher.ExecuteAll();

                int topmostAfterShow = interop.TopmostAssertionCount;
                Assert.True(topmostAfterShow >= 1);

                interop.ExtendedStyle &= ~WS_EX_TOPMOST;

                timer.FireTick();
                dispatcher.ExecuteAll();

                Assert.Equal(topmostAfterShow + 1, interop.TopmostAssertionCount);

                timer.FireTick();
                dispatcher.ExecuteAll();
                Assert.Equal(topmostAfterShow + 1, interop.TopmostAssertionCount);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void HealthCheck_BitPresent_DoesNotCallSetWindowPos()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, dispatcher, timer) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                dispatcher.ExecuteAll();

                int topmostAfterShow = interop.TopmostAssertionCount;
                int getWindowLongBefore = interop.GetWindowLongCallCount;

                timer.FireTick();
                dispatcher.ExecuteAll();

                Assert.Equal(topmostAfterShow, interop.TopmostAssertionCount);
                Assert.True(interop.GetWindowLongCallCount > getWindowLongBefore);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void HealthCheck_DisabledOverlay_DoesNotRecover()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, dispatcher, _) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                dispatcher.ExecuteAll();

                int topmostAfterShow = interop.TopmostAssertionCount;

                interop.ExtendedStyle &= ~WS_EX_TOPMOST;
                window.UpdateSettings(TraySettings.Default with { OverlayEnabled = false });
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);

                window.TopmostHealthMonitorForTest.QueueCheck();
                dispatcher.ExecuteAll();

                Assert.Equal(topmostAfterShow, interop.TopmostAssertionCount);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void HealthCheck_Hidden_DoesNotRecover()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, dispatcher, _) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                dispatcher.ExecuteAll();

                int topmostAfterShow = interop.TopmostAssertionCount;

                interop.ExtendedStyle &= ~WS_EX_TOPMOST;
                window.Hide();
                Assert.False(window.TopmostHealthMonitorForTest.IsRunningForTest);

                window.TopmostHealthMonitorForTest.QueueCheck();
                dispatcher.ExecuteAll();

                Assert.Equal(topmostAfterShow, interop.TopmostAssertionCount);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void HealthCheck_Shutdown_DoesNotRecover()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, interop, dispatcher, _) = CreateWindow();
            window.Opacity = 0;
            window.UpdateSettings(TraySettings.Default with { OverlayEnabled = true });

            try
            {
                window.Show();
                dispatcher.ExecuteAll();

                int topmostAfterShow = interop.TopmostAssertionCount;

                interop.ExtendedStyle &= ~WS_EX_TOPMOST;
                window.TopmostHealthMonitorForTest.PrepareForShutdown();

                window.TopmostHealthMonitorForTest.QueueCheck();
                dispatcher.ExecuteAll();

                Assert.Equal(topmostAfterShow, interop.TopmostAssertionCount);
            }
            finally
            {
                window.PrepareForShutdown();
            }
        });
    }

    [Fact]
    public void HealthCheck_ConsecutiveWindowPosChangedMessages_Coalesce()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var (window, _, dispatcher, _) = CreateWindow();

            window.InvokeHwndHookForTest(WM_WINDOWPOSCHANGED);
            window.InvokeHwndHookForTest(WM_WINDOWPOSCHANGED);
            window.InvokeHwndHookForTest(WM_WINDOWPOSCHANGED);

            Assert.Single(dispatcher.QueuedActions);
        });
    }
}
