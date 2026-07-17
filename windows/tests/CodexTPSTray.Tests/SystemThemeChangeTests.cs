using System;
using System.Threading;
using System.Threading.Tasks;
using CodexTPSCore;
using Microsoft.Win32;
using Xunit;

namespace CodexTPSTray.Tests;

public class SystemEventsThemeChangeSourceTests
{
    [Fact]
    public void Subscribe_AddsHandler()
    {
        UserPreferenceChangedEventHandler? addedHandler = null;
        var source = new SystemEventsThemeChangeSource(
            handler => addedHandler = handler,
            _ => { }
        );

        int callbackCount = 0;
        source.Subscribe(() => callbackCount++);

        Assert.NotNull(addedHandler);
        addedHandler!.Invoke(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void Unsubscribe_RemovesSameHandler()
    {
        UserPreferenceChangedEventHandler? addedHandler = null;
        UserPreferenceChangedEventHandler? removedHandler = null;
        var source = new SystemEventsThemeChangeSource(
            handler => addedHandler = handler,
            handler => removedHandler = handler
        );

        source.Subscribe(() => { });
        source.Unsubscribe();

        Assert.NotNull(addedHandler);
        Assert.Same(addedHandler, removedHandler);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        int removeCount = 0;
        var source = new SystemEventsThemeChangeSource(
            _ => { },
            _ => removeCount++
        );

        source.Subscribe(() => { });
        source.Dispose();
        source.Dispose();

        Assert.Equal(1, removeCount);
    }

    [Fact]
    public void Subscribe_AddThrows_ReturnsFalse()
    {
        var source = new SystemEventsThemeChangeSource(
            _ => throw new InvalidOperationException("test"),
            _ => { }
        );

        bool result = source.Subscribe(() => { });

        Assert.False(result);
    }

    [Fact]
    public void Unsubscribe_RemoveThrows_DoesNotThrow()
    {
        var source = new SystemEventsThemeChangeSource(
            _ => { },
            _ => throw new InvalidOperationException("test")
        );

        source.Subscribe(() => { });

        source.Unsubscribe();
    }

    [Fact]
    public void Unsubscribe_RemoveThrows_SecondUnsubscribeDoesNotRemove()
    {
        int removeCount = 0;
        var source = new SystemEventsThemeChangeSource(
            _ => { },
            _ =>
            {
                removeCount++;
                if (removeCount == 1)
                    throw new InvalidOperationException("test");
            }
        );

        source.Subscribe(() => { });
        source.Unsubscribe();
        source.Unsubscribe();

        Assert.Equal(1, removeCount);
    }

    [Fact]
    public void Subscribe_NullCallback_ReturnsFalse()
    {
        var source = new SystemEventsThemeChangeSource(
            _ => { },
            _ => { }
        );

        bool result = source.Subscribe(null!);

        Assert.False(result);
    }

    [Fact]
    public void Dispose_PreventsResubscribe()
    {
        var source = new SystemEventsThemeChangeSource(
            _ => { },
            _ => { }
        );

        source.Subscribe(() => { });
        source.Dispose();

        bool result = source.Subscribe(() => { });

        Assert.False(result);
    }

    [Fact]
    public void Constructor_NullSubscribeAction_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemEventsThemeChangeSource(null!, _ => { }));
    }

    [Fact]
    public void Constructor_NullUnsubscribeAction_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemEventsThemeChangeSource(_ => { }, null!));
    }

    [Fact]
    public async Task Concurrent_SubscribeAndDispose_NoResidualHandler()
    {
        int subscribeCount = 0;
        int unsubscribeCount = 0;
        using var startEvent = new ManualResetEventSlim(false);
        using var disposeEnteredEvent = new ManualResetEventSlim(false);
        using var continueEvent = new ManualResetEventSlim(false);

        var source = new SystemEventsThemeChangeSource(
            _ =>
            {
                Interlocked.Increment(ref subscribeCount);
                startEvent.Set();
                continueEvent.Wait();
            },
            _ => Interlocked.Increment(ref unsubscribeCount)
        );

        try
        {
            var subscribeTask = Task.Run(() => source.Subscribe(() => { }));

            Assert.True(startEvent.Wait(TimeSpan.FromSeconds(5)));

            var disposeTask = Task.Run(() =>
            {
                disposeEnteredEvent.Set();
                source.Dispose();
            });

            Assert.True(disposeEnteredEvent.Wait(TimeSpan.FromSeconds(5)));

            continueEvent.Set();

            bool subscribeResult = await subscribeTask.WaitAsync(TimeSpan.FromSeconds(5));
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(subscribeResult);
            Assert.Equal(1, subscribeCount);
            Assert.Equal(1, unsubscribeCount);

            bool retryResult = source.Subscribe(() => { });
            Assert.False(retryResult);
        }
        finally
        {
            continueEvent.Set();
        }
    }
}

public class SystemThemeChangeCoordinatorTests
{
    [Fact]
    public void Start_SubscribesOnce()
    {
        int subscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(() => { subscribeCount++; return true; });
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        bool result = coordinator.Start();

        Assert.Equal(1, subscribeCount);
        Assert.True(result);
    }

    [Fact]
    public void Event_QueuesRefresh_DoesNotApplyImmediately()
    {
        bool applyCalled = false;
        int getSettingsCallCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () =>
            {
                getSettingsCallCount++;
                return TraySettings.Default;
            },
            _ => applyCalled = true
        );

        coordinator.Start();
        source.RaiseEvent();

        Assert.False(applyCalled);
        Assert.Equal(0, getSettingsCallCount);
        Assert.Single(dispatcher.QueuedActions);
    }

    [Fact]
    public void Event_AppSystem_Applies()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(1, applyCount);
    }

    [Fact]
    public void Event_AppDarkOverlaySystem_Applies()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.Dark, OverlayThemePreference.System
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(1, applyCount);
    }

    [Fact]
    public void Event_AppDarkOverlayFollowApplication_DoesNotApply()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.Dark, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void Event_BothExplicit_DoesNotApply()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.Light, OverlayThemePreference.Dark
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void Event_UnknownAppValue_TreatedAsSystem()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            (ApplicationThemePreference)42, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(1, applyCount);
    }

    [Fact]
    public void MultipleEvents_MergedIntoOne()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        source.RaiseEvent();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(1, applyCount);
    }

    [Fact]
    public void AfterExecution_EventCanQueueAgain()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        dispatcher.ExecuteAll();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(2, applyCount);
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        int unsubscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(unsubscribeAction: () => unsubscribeCount++);
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.Dispose();

        Assert.Equal(1, unsubscribeCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        int unsubscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(unsubscribeAction: () => unsubscribeCount++);
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.Dispose();
        coordinator.Dispose();

        Assert.Equal(1, unsubscribeCount);
    }

    [Fact]
    public void Dispose_EventIgnored()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        coordinator.Dispose();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void Dispose_PendingCallbackNotApplied()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        coordinator.Dispose();
        dispatcher.ExecuteAll();

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void SubscribeFailure_DoesNotThrow()
    {
        var source = new FakeSystemThemeChangeSource(subscribeAction: () => throw new InvalidOperationException("test"));
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        bool result = coordinator.Start();

        Assert.False(result);
    }

    [Fact]
    public void PrepareForShutdown_ImmediatelyUnsubscribes()
    {
        int unsubscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(unsubscribeAction: () => unsubscribeCount++);
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.PrepareForShutdown();

        Assert.Equal(1, unsubscribeCount);
    }

    [Fact]
    public void PrepareForShutdown_IsIdempotent()
    {
        int unsubscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(unsubscribeAction: () => unsubscribeCount++);
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.PrepareForShutdown();
        coordinator.PrepareForShutdown();

        Assert.Equal(1, unsubscribeCount);
    }

    [Fact]
    public void PrepareForShutdown_EventIgnored()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        coordinator.PrepareForShutdown();
        source.RaiseEvent();
        dispatcher.ExecuteAll();

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void PrepareForShutdown_PendingCallbackNotApplied()
    {
        int applyCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var settings = new TraySettings(
            MetricWindow.OneMinute, RefreshCadence.FiveSeconds,
            Language.English, false, false, null, null,
            ApplicationThemePreference.System, OverlayThemePreference.FollowApplication
        );
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => settings,
            _ => applyCount++
        );

        coordinator.Start();
        source.RaiseEvent();
        coordinator.PrepareForShutdown();
        dispatcher.ExecuteAll();

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void PrepareForShutdown_StartDoesNotResubscribe()
    {
        int subscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(() => { subscribeCount++; return true; });
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.PrepareForShutdown();
        bool result = coordinator.Start();

        Assert.Equal(1, subscribeCount);
        Assert.False(result);
    }

    [Fact]
    public void Dispose_UsesPrepareForShutdown()
    {
        int unsubscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(unsubscribeAction: () => unsubscribeCount++);
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.Dispose();

        Assert.Equal(1, unsubscribeCount);
    }

    [Fact]
    public void RefreshPending_ExceptionClearsPending()
    {
        int getSettingsCallCount = 0;
        var source = new FakeSystemThemeChangeSource();
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () =>
            {
                getSettingsCallCount++;
                throw new InvalidOperationException("test");
            },
            _ => { }
        );

        coordinator.Start();
        source.RaiseEvent();

        try
        {
            dispatcher.ExecuteAll();
        }
        catch (InvalidOperationException)
        {
        }

        source.RaiseEvent();

        try
        {
            dispatcher.ExecuteAll();
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal(2, getSettingsCallCount);
    }

    [Fact]
    public void PrepareForShutdown_BeforeStart_StartReturnsFalse()
    {
        int subscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(() => { subscribeCount++; return true; });
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.PrepareForShutdown();
        bool result = coordinator.Start();

        Assert.False(result);
        Assert.Equal(0, subscribeCount);
    }

    [Fact]
    public void Start_PrepareForShutdown_Start_SecondStartReturnsFalse()
    {
        int subscribeCount = 0;
        int unsubscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(
            () => { Interlocked.Increment(ref subscribeCount); return true; },
            () => Interlocked.Increment(ref unsubscribeCount)
        );
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Start();
        coordinator.PrepareForShutdown();
        bool result = coordinator.Start();

        Assert.False(result);
        Assert.Equal(1, subscribeCount);
        Assert.Equal(1, unsubscribeCount);
    }

    [Fact]
    public void Dispose_BeforeStart_StartReturnsFalse()
    {
        int subscribeCount = 0;
        var source = new FakeSystemThemeChangeSource(() => { subscribeCount++; return true; });
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        coordinator.Dispose();
        bool result = coordinator.Start();

        Assert.False(result);
        Assert.Equal(0, subscribeCount);
    }

    [Fact]
    public void SubscribeFailure_BeforeShutdown_CanRetry()
    {
        int subscribeAttempt = 0;
        var source = new FakeSystemThemeChangeSource(() =>
        {
            subscribeAttempt++;
            if (subscribeAttempt == 1)
                throw new InvalidOperationException("test");
            return true;
        });
        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        bool firstResult = coordinator.Start();
        bool secondResult = coordinator.Start();

        Assert.False(firstResult);
        Assert.True(secondResult);
    }

    [Fact]
    public async Task Concurrent_StartAndPrepareForShutdown_NoResidualSubscription()
    {
        int subscribeCount = 0;
        int unsubscribeCount = 0;
        using var startEvent = new ManualResetEventSlim(false);
        using var prepareEnteredEvent = new ManualResetEventSlim(false);
        using var continueEvent = new ManualResetEventSlim(false);

        var source = new FakeSystemThemeChangeSource(
            () =>
            {
                Interlocked.Increment(ref subscribeCount);
                startEvent.Set();
                continueEvent.Wait();
                return true;
            },
            () => Interlocked.Increment(ref unsubscribeCount)
        );

        var dispatcher = new FakeDispatcher();
        var coordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => TraySettings.Default,
            _ => { }
        );

        try
        {
            var startTask = Task.Run(() => coordinator.Start());

            Assert.True(startEvent.Wait(TimeSpan.FromSeconds(5)));

            var prepareTask = Task.Run(() =>
            {
                prepareEnteredEvent.Set();
                coordinator.PrepareForShutdown();
            });

            Assert.True(prepareEnteredEvent.Wait(TimeSpan.FromSeconds(5)));

            continueEvent.Set();

            bool startResult = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
            await prepareTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(startResult);
            Assert.Equal(1, subscribeCount);
            Assert.Equal(1, unsubscribeCount);

            bool retryResult = coordinator.Start();
            Assert.False(retryResult);
        }
        finally
        {
            continueEvent.Set();
        }
    }
}

internal sealed class FakeSystemThemeChangeSource : ISystemThemeChangeSource
{
    private readonly object _sync = new();
    private readonly Func<bool>? _subscribeAction;
    private readonly Action? _unsubscribeAction;
    private Action? _callback;
    private bool _isSubscribed;

    public FakeSystemThemeChangeSource(Func<bool>? subscribeAction = null, Action? unsubscribeAction = null)
    {
        _subscribeAction = subscribeAction;
        _unsubscribeAction = unsubscribeAction;
    }

    public bool Subscribe(Action callback)
    {
        lock (_sync)
        {
            if (_isSubscribed)
                return true;

            if (_subscribeAction != null)
            {
                try
                {
                    if (!_subscribeAction())
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
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
            if (!_isSubscribed)
                return;

            _unsubscribeAction?.Invoke();
            _callback = null;
            _isSubscribed = false;
        }
    }

    public void RaiseEvent()
    {
        lock (_sync)
        {
            _callback?.Invoke();
        }
    }
}

internal sealed class FakeDispatcher : IDispatcher
{
    private readonly object _sync = new();
    private readonly System.Collections.Generic.List<Action> _queuedActions = new();

    public System.Collections.Generic.List<Action> QueuedActions
    {
        get
        {
            lock (_sync)
            {
                return new System.Collections.Generic.List<Action>(_queuedActions);
            }
        }
    }

    public void BeginInvoke(Action action)
    {
        lock (_sync)
        {
            _queuedActions.Add(action);
        }
    }

    public void ExecuteAll()
    {
        Action[] actions;
        lock (_sync)
        {
            actions = _queuedActions.ToArray();
            _queuedActions.Clear();
        }

        foreach (var action in actions)
        {
            action();
        }
    }
}