using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;
using Xunit;

namespace CodexTPSTray.Tests;

public class SystemEventsOverlayRecoverySourceTests
{
    [Fact]
    public void Subscribe_AddsAllThreeHandlers()
    {
        PowerModeChangedEventHandler? addedPower = null;
        SessionSwitchEventHandler? addedSession = null;
        EventHandler? addedDisplay = null;

        var source = new SystemEventsOverlayRecoverySource(
            handler => addedPower = handler,
            _ => { },
            handler => addedSession = handler,
            _ => { },
            handler => addedDisplay = handler,
            _ => { }
        );

        int callbackCount = 0;
        source.Subscribe(() => callbackCount++);

        Assert.NotNull(addedPower);
        Assert.NotNull(addedSession);
        Assert.NotNull(addedDisplay);

        addedPower!.Invoke(source, new PowerModeChangedEventArgs(PowerModes.Resume));
        Assert.Equal(1, callbackCount);

        addedSession!.Invoke(source, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
        Assert.Equal(2, callbackCount);

        addedDisplay!.Invoke(source, EventArgs.Empty);
        Assert.Equal(3, callbackCount);
    }

    [Fact]
    public void Unsubscribe_RemovesSameHandlers()
    {
        PowerModeChangedEventHandler? addedPower = null;
        PowerModeChangedEventHandler? removedPower = null;
        SessionSwitchEventHandler? addedSession = null;
        SessionSwitchEventHandler? removedSession = null;
        EventHandler? addedDisplay = null;
        EventHandler? removedDisplay = null;

        var source = new SystemEventsOverlayRecoverySource(
            handler => addedPower = handler,
            handler => removedPower = handler,
            handler => addedSession = handler,
            handler => removedSession = handler,
            handler => addedDisplay = handler,
            handler => removedDisplay = handler
        );

        source.Subscribe(() => { });
        source.Unsubscribe();

        Assert.Same(addedPower, removedPower);
        Assert.Same(addedSession, removedSession);
        Assert.Same(addedDisplay, removedDisplay);
    }

    [Theory]
    [InlineData(PowerModes.Suspend)]
    [InlineData(PowerModes.StatusChange)]
    public void PowerMode_NonResume_DoesNotFireCallback(PowerModes mode)
    {
        PowerModeChangedEventHandler? handler = null;
        var source = new SystemEventsOverlayRecoverySource(
            h => handler = h, _ => { },
            _ => { }, _ => { },
            _ => { }, _ => { }
        );

        int count = 0;
        source.Subscribe(() => count++);

        handler!.Invoke(source, new PowerModeChangedEventArgs(mode));
        Assert.Equal(0, count);
    }

    [Fact]
    public void PowerMode_Resume_FiresCallback()
    {
        PowerModeChangedEventHandler? handler = null;
        var source = new SystemEventsOverlayRecoverySource(
            h => handler = h, _ => { },
            _ => { }, _ => { },
            _ => { }, _ => { }
        );

        int count = 0;
        source.Subscribe(() => count++);

        handler!.Invoke(source, new PowerModeChangedEventArgs(PowerModes.Resume));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock)]
    [InlineData(SessionSwitchReason.SessionLogoff)]
    [InlineData(SessionSwitchReason.SessionLogon)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect)]
    [InlineData(SessionSwitchReason.RemoteDisconnect)]
    [InlineData(SessionSwitchReason.SessionRemoteControl)]
    public void SessionSwitch_IrrelevantReasons_DoNotFire(SessionSwitchReason reason)
    {
        SessionSwitchEventHandler? handler = null;
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { },
            h => handler = h, _ => { },
            _ => { }, _ => { }
        );

        int count = 0;
        source.Subscribe(() => count++);

        handler!.Invoke(source, new SessionSwitchEventArgs(reason));
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionUnlock)]
    [InlineData(SessionSwitchReason.ConsoleConnect)]
    [InlineData(SessionSwitchReason.RemoteConnect)]
    public void SessionSwitch_RelevantReasons_FireCallback(SessionSwitchReason reason)
    {
        SessionSwitchEventHandler? handler = null;
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { },
            h => handler = h, _ => { },
            _ => { }, _ => { }
        );

        int count = 0;
        source.Subscribe(() => count++);

        handler!.Invoke(source, new SessionSwitchEventArgs(reason));
        Assert.Equal(1, count);
    }

    [Fact]
    public void DisplaySettingsChanged_FiresCallback()
    {
        EventHandler? handler = null;
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { },
            _ => { }, _ => { },
            h => handler = h, _ => { }
        );

        int count = 0;
        source.Subscribe(() => count++);

        handler!.Invoke(source, EventArgs.Empty);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        int removeCount = 0;
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => removeCount++,
            _ => { }, _ => removeCount++,
            _ => { }, _ => removeCount++
        );

        source.Subscribe(() => { });
        source.Dispose();
        source.Dispose();

        Assert.Equal(3, removeCount);
    }

    [Fact]
    public void Subscribe_AddThrows_ReturnsFalse()
    {
        var source = new SystemEventsOverlayRecoverySource(
            _ => throw new InvalidOperationException("test"),
            _ => { },
            _ => { }, _ => { },
            _ => { }, _ => { }
        );

        bool result = source.Subscribe(() => { });
        Assert.False(result);
    }

    [Fact]
    public void Subscribe_NullCallback_ReturnsFalse()
    {
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }
        );
        bool result = source.Subscribe(null!);
        Assert.False(result);
    }

    [Fact]
    public void Dispose_PreventsResubscribe()
    {
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }
        );

        source.Subscribe(() => { });
        source.Dispose();
        Assert.False(source.Subscribe(() => { }));
    }

    [Fact]
    public void Unsubscribe_RemoveThrows_DoesNotThrow()
    {
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => throw new InvalidOperationException("test"),
            _ => { }, _ => { },
            _ => { }, _ => { }
        );

        source.Subscribe(() => { });
        source.Unsubscribe();
    }

    [Fact]
    public void Subscribe_SecondSubscribeFails_RollsBackFirst()
    {
        PowerModeChangedEventHandler? addedPower = null;
        PowerModeChangedEventHandler? removedPower = null;
        int sessionSubAttempts = 0;

        var source = new SystemEventsOverlayRecoverySource(
            handler => addedPower = handler,
            handler => removedPower = handler,
            _ => { sessionSubAttempts++; throw new InvalidOperationException("session sub failed"); },
            _ => { },
            _ => { }, _ => { }
        );

        bool result = source.Subscribe(() => { });

        Assert.False(result);
        Assert.Equal(1, sessionSubAttempts);
        // Power subscription must have been rolled back
        Assert.NotNull(addedPower);
        Assert.Same(addedPower, removedPower);
    }

    [Fact]
    public void Subscribe_ThirdSubscribeFails_RollsBackFirstTwo()
    {
        PowerModeChangedEventHandler? addedPower = null;
        PowerModeChangedEventHandler? removedPower = null;
        SessionSwitchEventHandler? addedSession = null;
        SessionSwitchEventHandler? removedSession = null;
        var rollbackOrder = new List<string>();
        int displaySubAttempts = 0;

        var source = new SystemEventsOverlayRecoverySource(
            handler => addedPower = handler,
            handler => { rollbackOrder.Add("power"); removedPower = handler; },
            handler => addedSession = handler,
            handler => { rollbackOrder.Add("session"); removedSession = handler; },
            _ => { displaySubAttempts++; throw new InvalidOperationException("display sub failed"); },
            _ => { }
        );

        bool result = source.Subscribe(() => { });

        Assert.False(result);
        Assert.Equal(1, displaySubAttempts);
        // Both prior subscriptions must have been rolled back
        Assert.NotNull(addedPower);
        Assert.Same(addedPower, removedPower);
        Assert.NotNull(addedSession);
        Assert.Same(addedSession, removedSession);
        // Reverse order: session unsubscribed before power
        Assert.Equal(new[] { "session", "power" }, rollbackOrder);
    }

    [Fact]
    public void Unsubscribe_FirstUnsubscribeThrows_StillCallsOtherTwo()
    {
        // Display is unsubscribed first in BestEffortDetach (reverse order).
        // If display unsubscribe throws, session and power must still be called.
        int displayUnsubCalls = 0;
        int sessionUnsubCalls = 0;
        int powerUnsubCalls = 0;

        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { powerUnsubCalls++; },
            _ => { }, _ => { sessionUnsubCalls++; },
            _ => { }, _ => { displayUnsubCalls++; throw new InvalidOperationException("display unsub failed"); }
        );

        source.Subscribe(() => { });
        source.Unsubscribe();

        Assert.Equal(1, displayUnsubCalls);
        Assert.Equal(1, sessionUnsubCalls);
        Assert.Equal(1, powerUnsubCalls);
    }

    [Fact]
    public void Unsubscribe_MiddleUnsubscribeThrows_StillCallsLast()
    {
        // Session is unsubscribed second. If it throws, power must still be called.
        int displayUnsubCalls = 0;
        int sessionUnsubCalls = 0;
        int powerUnsubCalls = 0;

        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { powerUnsubCalls++; },
            _ => { }, _ => { sessionUnsubCalls++; throw new InvalidOperationException("session unsub failed"); },
            _ => { }, _ => { displayUnsubCalls++; }
        );

        source.Subscribe(() => { });
        source.Unsubscribe();

        Assert.Equal(1, displayUnsubCalls);
        Assert.Equal(1, sessionUnsubCalls);
        Assert.Equal(1, powerUnsubCalls);
    }

    [Fact]
    public void SubscribeFailure_DisposeIsIdempotent()
    {
        var source = new SystemEventsOverlayRecoverySource(
            _ => { }, _ => { },
            _ => throw new InvalidOperationException("fail"), _ => { },
            _ => { }, _ => { }
        );

        bool result = source.Subscribe(() => { });
        Assert.False(result);

        source.Dispose();
        source.Dispose();
    }

    [Fact]
    public void SubscribeFailure_WithRollbackException_DisposeIsSafeAndIdempotent()
    {
        var source = new SystemEventsOverlayRecoverySource(
            _ => { },
            _ => throw new InvalidOperationException("power unsub fail during rollback"),
            _ => throw new InvalidOperationException("session sub fail"), _ => { },
            _ => { }, _ => { }
        );

        bool result = source.Subscribe(() => { });
        Assert.False(result);

        // Dispose after a failed subscribe (where rollback also threw) must not throw
        // and must be idempotent.
        source.Dispose();
        source.Dispose();
    }
}

public class OverlayTopmostRecoveryCoordinatorTests
{
    private sealed class FakeRecoverySource : IOverlayRecoveryEventSource
    {
        private readonly object _sync = new();
        private readonly Func<bool>? _subscribeAction;
        private readonly Action? _unsubscribeAction;
        private Action? _callback;
        private bool _isSubscribed;

        public int SubscribeCount { get; private set; }
        public int UnsubscribeCount { get; private set; }

        public FakeRecoverySource(Func<bool>? subscribeAction = null, Action? unsubscribeAction = null)
        {
            _subscribeAction = subscribeAction;
            _unsubscribeAction = unsubscribeAction;
        }

        public bool Subscribe(Action callback)
        {
            lock (_sync)
            {
                if (_isSubscribed) return true;
                SubscribeCount++;
                if (_subscribeAction != null)
                {
                    try
                    {
                        if (!_subscribeAction()) return false;
                    }
                    catch { return false; }
                }
                _callback = callback;
                _isSubscribed = true;
                return true;
            }
        }

        public void Unsubscribe()
        {
            lock (_sync)
            {
                if (!_isSubscribed) return;
                UnsubscribeCount++;
                _unsubscribeAction?.Invoke();
                _callback = null;
                _isSubscribed = false;
            }
        }

        public void RaiseEvent()
        {
            lock (_sync) { _callback?.Invoke(); }
        }
    }

    private sealed class FakeRecoveryTimer : IRecoveryTimer
    {
        public TimeSpan? LastDelay { get; private set; }
        public Action? LastCallback { get; private set; }
        public int RestartCount { get; private set; }
        public int CancelCount { get; private set; }

        public void Restart(TimeSpan delay, Action callback)
        {
            LastDelay = delay;
            LastCallback = callback;
            RestartCount++;
        }

        public void Cancel()
        {
            CancelCount++;
            LastCallback = null;
        }

        public void FireLastCallback()
        {
            var cb = LastCallback;
            LastCallback = null;
            cb?.Invoke();
        }
    }

    private sealed class FakeRecoveryTarget : IOverlayTopmostRecoveryTarget
    {
        public bool IsVisible { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        public int ReassertCount { get; private set; }
        public List<string> CallOrder { get; } = new();

        public void ReassertTopmost()
        {
            ReassertCount++;
            CallOrder.Add("Reassert");
        }
    }

    private static (FakeRecoverySource, FakeDispatcher, FakeRecoveryTarget, FakeRecoveryTimer, OverlayTopmostRecoveryCoordinator) CreateCoordinator(
        TimeSpan? delay = null,
        FakeRecoverySource? source = null,
        FakeRecoveryTarget? target = null)
    {
        var src = source ?? new FakeRecoverySource();
        var dispatcher = new FakeDispatcher();
        var tgt = target ?? new FakeRecoveryTarget();
        var timer = new FakeRecoveryTimer();
        var coord = new OverlayTopmostRecoveryCoordinator(
            src, dispatcher, tgt, timer, delay ?? TimeSpan.FromMilliseconds(1500));
        return (src, dispatcher, tgt, timer, coord);
    }

    [Fact]
    public void Start_SubscribesOnce()
    {
        var (src, _, _, _, coord) = CreateCoordinator();
        coord.Start();
        bool second = coord.Start();
        Assert.Equal(1, src.SubscribeCount);
        Assert.True(second);
    }

    [Fact]
    public void ResumeEvent_QueuesImmediateDispatch()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();

        Assert.Single(dispatcher.QueuedActions);
        Assert.Equal(0, target.ReassertCount);
        Assert.Equal(0, timer.RestartCount);
    }

    [Fact]
    public void ImmediateDispatch_VisibleAndEnabled_ReassertsTopmostAndSchedulesDelay()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(1, target.ReassertCount);
        Assert.Equal(1, timer.RestartCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), timer.LastDelay);
    }

    [Fact]
    public void DelayedRetry_WhenFired_ReassertsAgain()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        timer.FireLastCallback();

        Assert.Equal(2, target.ReassertCount);
    }

    [Fact]
    public void ImmediateDispatch_HiddenWindow_DoesNotReassert()
    {
        var target = new FakeRecoveryTarget { IsVisible = false };
        var (src, dispatcher, _, timer, coord) = CreateCoordinator(target: target);
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(0, target.ReassertCount);
        Assert.Equal(1, timer.RestartCount); // delayed retry is still scheduled
    }

    [Fact]
    public void ImmediateDispatch_Disabled_DoesNotReassert()
    {
        var target = new FakeRecoveryTarget { IsEnabled = false };
        var (src, dispatcher, _, timer, coord) = CreateCoordinator(target: target);
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(0, target.ReassertCount);
        Assert.Equal(1, timer.RestartCount);
    }

    [Fact]
    public void DelayedRetry_HiddenWindow_DoesNotReassert()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        Assert.Equal(1, target.ReassertCount);

        target.IsVisible = false;
        timer.FireLastCallback();
        Assert.Equal(1, target.ReassertCount);
    }

    [Fact]
    public void MultipleEvents_CoalescedIntoOneImmediate()
    {
        var (src, dispatcher, target, _, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        src.RaiseEvent();
        src.RaiseEvent();

        Assert.Single(dispatcher.QueuedActions);
    }

    [Fact]
    public void MultipleEvents_ResetsDelayedTimer()
    {
        var (src, dispatcher, _, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        Assert.Equal(1, timer.RestartCount);

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        Assert.Equal(2, timer.RestartCount);
    }

    [Fact]
    public void MultipleEvents_OnlyOneDelayedCallback()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        src.RaiseEvent();
        dispatcher.ExecuteAll();
        src.RaiseEvent();
        dispatcher.ExecuteAll();

        // After 3 event bursts and 3 immediate dispatches, only the LAST delayed callback should be pending
        Assert.Equal(3, timer.RestartCount);
        timer.FireLastCallback();
        Assert.Equal(4, target.ReassertCount); // 3 immediate + 1 delayed
    }

    [Fact]
    public void AfterImmediateExecutes_NextEventCanQueueAgain()
    {
        var (src, dispatcher, target, _, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        Assert.Equal(1, target.ReassertCount);

        src.RaiseEvent();
        Assert.Single(dispatcher.QueuedActions);
        dispatcher.ExecuteAll();
        Assert.Equal(2, target.ReassertCount);
    }

    [Fact]
    public void PrepareForShutdown_UnsubscribesAndCancelsTimer()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();
        src.RaiseEvent();

        coord.PrepareForShutdown();

        Assert.Equal(1, src.UnsubscribeCount);
        Assert.Equal(1, timer.CancelCount);

        dispatcher.ExecuteAll();
        Assert.Equal(0, target.ReassertCount);

        src.RaiseEvent();
        Assert.Empty(dispatcher.QueuedActions);
    }

    [Fact]
    public void Dispose_UnsubscribesAndCancelsTimer()
    {
        var (src, _, _, timer, coord) = CreateCoordinator();
        coord.Start();
        coord.Dispose();

        Assert.Equal(1, src.UnsubscribeCount);
        Assert.Equal(1, timer.CancelCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (src, _, _, timer, coord) = CreateCoordinator();
        coord.Start();
        coord.Dispose();
        coord.Dispose();

        Assert.Equal(1, src.UnsubscribeCount);
        Assert.Equal(1, timer.CancelCount);
    }

    [Fact]
    public void PrepareForShutdown_DelayedCallbackDoesNotReassert()
    {
        var (src, dispatcher, target, timer, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();
        dispatcher.ExecuteAll();
        Assert.Equal(1, target.ReassertCount);

        coord.PrepareForShutdown();
        timer.FireLastCallback();
        Assert.Equal(1, target.ReassertCount);
    }

    [Fact]
    public void PrepareForShutdown_BeforeStart_StartReturnsFalse()
    {
        var (src, _, _, _, coord) = CreateCoordinator();
        coord.PrepareForShutdown();
        bool result = coord.Start();
        Assert.False(result);
        Assert.Equal(0, src.SubscribeCount);
    }

    [Fact]
    public void SubscribeFailure_DoesNotThrow_ReturnsFalse()
    {
        var src = new FakeRecoverySource(subscribeAction: () => throw new InvalidOperationException("test"));
        var dispatcher = new FakeDispatcher();
        var target = new FakeRecoveryTarget();
        var timer = new FakeRecoveryTimer();
        var coord = new OverlayTopmostRecoveryCoordinator(src, dispatcher, target, timer, TimeSpan.FromMilliseconds(1500));

        bool result = coord.Start();
        Assert.False(result);
    }

    [Fact]
    public void RecoveryEvent_GoesThroughDispatcher()
    {
        var (src, dispatcher, target, _, coord) = CreateCoordinator();
        coord.Start();

        src.RaiseEvent();

        // Before dispatcher executes, no reassert should have happened
        Assert.Equal(0, target.ReassertCount);
        Assert.Single(dispatcher.QueuedActions);
    }
}

public class OverlayWindowReassertTopmostTests
{
    private static readonly IntPtr FakeHwnd = new(0x1234);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private sealed class FakeWindowNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public List<(IntPtr HWnd, IntPtr InsertAfter, int X, int Y, int CX, int CY, uint Flags)> SetWindowPosCalls { get; } = new();

        public IntPtr GetHandle(Window window) => FakeHwnd;
        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
        {
            SetWindowPosCalls.Add((hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags));
            return true;
        }
        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect)
        {
            lpRect = new OverlayWindow.RECT { Left = 100, Top = 100, Right = 300, Bottom = 200 };
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

    [Fact]
    public void ReassertTopmost_CallsSetWindowPosWithTopmostAndCorrectFlags()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var interop = new FakeWindowNativeInterop();
            var provider = new FakeWorkAreaProvider();
            var window = new OverlayWindow(provider, interop);

            window.ReassertTopmost();

            var topmostCalls = interop.SetWindowPosCalls.FindAll(c => c.InsertAfter == HWND_TOPMOST);
            Assert.NotEmpty(topmostCalls);
            var last = topmostCalls[^1];
            Assert.Equal(FakeHwnd, last.HWnd);
            Assert.Equal(0, last.X);
            Assert.Equal(0, last.Y);
            Assert.Equal(0, last.CX);
            Assert.Equal(0, last.CY);
            uint expectedFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED;
            Assert.Equal(expectedFlags, last.Flags);
        });
    }
}
