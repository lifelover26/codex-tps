using System;
using Xunit;

namespace CodexTPSTray.Tests;

public class TopmostHealthMonitorTests
{
    private sealed class FakeHealthTarget : ITopmostHealthTarget
    {
        public bool IsVisible { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        public bool NativeTopmostSet { get; set; } = true;
        public int RecoverCount { get; private set; }
        public int IsNativeTopmostSetCallCount { get; private set; }
        public Action? OnRecover { get; set; }

        public bool IsNativeTopmostSet()
        {
            IsNativeTopmostSetCallCount++;
            return NativeTopmostSet;
        }

        public void RecoverTopmost()
        {
            RecoverCount++;
            OnRecover?.Invoke();
        }
    }

    private sealed class FakeHealthTimer : ITopmostHealthTimer
    {
        public bool IsStarted { get; private set; }
        public TimeSpan? Interval { get; private set; }
        public Action? Tick { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public void Start(TimeSpan interval, Action tick)
        {
            StartCount++;
            IsStarted = true;
            Interval = interval;
            Tick = tick;
        }

        public void Stop()
        {
            StopCount++;
            IsStarted = false;
            Tick = null;
        }

        public void FireTick() => Tick?.Invoke();
    }

    private static (FakeHealthTarget, FakeDispatcher, FakeHealthTimer, TopmostHealthMonitor) CreateMonitor(TimeSpan? interval = null)
    {
        var target = new FakeHealthTarget();
        var dispatcher = new FakeDispatcher();
        var timer = new FakeHealthTimer();
        var monitor = new TopmostHealthMonitor(target, dispatcher, timer, interval ?? TimeSpan.FromSeconds(2));
        return (target, dispatcher, timer, monitor);
    }

    [Fact]
    public void CheckOnce_TopmostBitPresent_DoesNotRecover()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = true;

        monitor.CheckOnce();

        Assert.Equal(0, target.RecoverCount);
        Assert.Equal(1, target.IsNativeTopmostSetCallCount);
    }

    [Fact]
    public void CheckOnce_TopmostBitMissing_RecoversOnce()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.CheckOnce();

        Assert.Equal(1, target.RecoverCount);
    }

    [Fact]
    public void CheckOnce_AfterRecovery_BitNowPresent_DoesNotRecoverAgain()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.CheckOnce();
        Assert.Equal(1, target.RecoverCount);

        target.NativeTopmostSet = true;
        monitor.CheckOnce();
        Assert.Equal(1, target.RecoverCount);
    }

    [Fact]
    public void CheckOnce_HiddenWindow_DoesNotRecover()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.IsVisible = false;
        target.NativeTopmostSet = false;

        monitor.CheckOnce();

        Assert.Equal(0, target.RecoverCount);
    }

    [Fact]
    public void CheckOnce_Disabled_DoesNotRecover()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.IsEnabled = false;
        target.NativeTopmostSet = false;

        monitor.CheckOnce();

        Assert.Equal(0, target.RecoverCount);
    }

    [Fact]
    public void CheckOnce_AfterShutdown_DoesNotRecover()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.PrepareForShutdown();
        monitor.CheckOnce();

        Assert.Equal(0, target.RecoverCount);
    }

    [Fact]
    public void CheckOnce_AfterDispose_DoesNotRecover()
    {
        var (target, _, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.Dispose();
        monitor.CheckOnce();

        Assert.Equal(0, target.RecoverCount);
    }

    [Fact]
    public void Start_StartsTimerAndQueuesImmediateCheck()
    {
        var (target, dispatcher, timer, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.Start();

        Assert.True(timer.IsStarted);
        Assert.Equal(TimeSpan.FromSeconds(2), timer.Interval);
        Assert.Single(dispatcher.QueuedActions);

        dispatcher.ExecuteAll();

        Assert.Equal(1, target.RecoverCount);
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        var (_, _, timer, monitor) = CreateMonitor();

        monitor.Start();
        monitor.Start();

        Assert.Equal(1, timer.StartCount);
    }

    [Fact]
    public void Stop_StopsTimer()
    {
        var (_, _, timer, monitor) = CreateMonitor();

        monitor.Start();
        Assert.True(timer.IsStarted);

        monitor.Stop();
        Assert.False(timer.IsStarted);
    }

    [Fact]
    public void Stop_IsIdempotent()
    {
        var (_, _, timer, monitor) = CreateMonitor();

        monitor.Start();
        monitor.Stop();
        monitor.Stop();

        Assert.Equal(1, timer.StopCount);
    }

    [Fact]
    public void Start_AfterShutdown_DoesNothing()
    {
        var (_, _, timer, monitor) = CreateMonitor();

        monitor.PrepareForShutdown();
        monitor.Start();

        Assert.False(timer.IsStarted);
        Assert.False(monitor.IsRunningForTest);
    }

    [Fact]
    public void QueueCheck_CoalescesMultipleCallsIntoOneDispatch()
    {
        var (_, dispatcher, _, monitor) = CreateMonitor();

        monitor.QueueCheck();
        monitor.QueueCheck();
        monitor.QueueCheck();

        Assert.Single(dispatcher.QueuedActions);
    }

    [Fact]
    public void TimerTick_QueuesACheck()
    {
        var (_, dispatcher, timer, monitor) = CreateMonitor();
        monitor.Start();
        dispatcher.ExecuteAll();

        timer.FireTick();

        Assert.Single(dispatcher.QueuedActions);
    }

    [Fact]
    public void TimerTick_CoalescesWithPendingCheck()
    {
        var (_, dispatcher, timer, monitor) = CreateMonitor();
        monitor.Start();

        timer.FireTick();
        timer.FireTick();

        Assert.Single(dispatcher.QueuedActions);
    }

    [Fact]
    public void AfterDispatchExecutes_NextQueueCheckCanDispatchAgain()
    {
        var (target, dispatcher, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.QueueCheck();
        dispatcher.ExecuteAll();
        Assert.Equal(1, target.RecoverCount);

        monitor.QueueCheck();
        Assert.Single(dispatcher.QueuedActions);
        dispatcher.ExecuteAll();
        Assert.Equal(2, target.RecoverCount);
    }

    [Fact]
    public void RecoverTopmost_SelfMessage_DoesNotRecurse()
    {
        var (target, dispatcher, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        target.OnRecover = () =>
        {
            target.NativeTopmostSet = true;
            monitor.QueueCheck();
        };

        monitor.QueueCheck();
        dispatcher.ExecuteAll();

        Assert.Equal(1, target.RecoverCount);
        Assert.Empty(dispatcher.QueuedActions);
    }

    [Fact]
    public void RecoverTopmost_SelfMessage_EvenIfBitStaysMissing_OnlyOneRecoveryPerCycle()
    {
        var (target, dispatcher, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        target.OnRecover = () =>
        {
            monitor.QueueCheck();
        };

        monitor.QueueCheck();
        dispatcher.ExecuteAll();

        Assert.Equal(1, target.RecoverCount);
    }

    [Fact]
    public void Stop_AfterStart_PendingCheckStillDrainsButSkipsRecoveryWhenHidden()
    {
        var (target, dispatcher, _, monitor) = CreateMonitor();
        target.NativeTopmostSet = false;

        monitor.Start();
        target.IsVisible = false;
        monitor.Stop();

        dispatcher.ExecuteAll();

        Assert.Equal(0, target.RecoverCount);
    }

    [Fact]
    public void Dispose_StopsTimer()
    {
        var (_, _, timer, monitor) = CreateMonitor();
        monitor.Start();

        monitor.Dispose();

        Assert.False(timer.IsStarted);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (_, _, timer, monitor) = CreateMonitor();
        monitor.Start();

        monitor.Dispose();
        monitor.Dispose();

        Assert.Equal(1, timer.StopCount);
    }

    [Fact]
    public void PrepareForShutdown_AfterStart_StopsTimer()
    {
        var (_, _, timer, monitor) = CreateMonitor();
        monitor.Start();

        monitor.PrepareForShutdown();

        Assert.False(timer.IsStarted);
        Assert.False(monitor.IsRunningForTest);
    }

    [Fact]
    public void QueueCheck_AfterShutdown_DoesNotDispatch()
    {
        var (_, dispatcher, _, monitor) = CreateMonitor();

        monitor.PrepareForShutdown();
        monitor.QueueCheck();

        Assert.Empty(dispatcher.QueuedActions);
    }
}
