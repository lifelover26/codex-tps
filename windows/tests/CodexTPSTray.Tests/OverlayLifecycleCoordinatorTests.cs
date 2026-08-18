using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayLifecycleCoordinatorTests
{
    private class SynchronousDispatcher : IDispatcher
    {
        public Action? QueuedAction { get; private set; }

        public void BeginInvoke(Action action)
        {
            QueuedAction = action;
        }

        public void ExecuteQueued()
        {
            QueuedAction?.Invoke();
        }
    }

    private class TrackedWindowAdapter : IOverlayWindowAdapter
    {
        public bool IsVisible { get; set; }
        public int ShowCallCount { get; private set; }
        public int HideCallCount { get; private set; }
        public int ResetPositionCallCount { get; private set; }
        public int UpdateSettingsCallCount { get; private set; }
        public TraySettings? LastResetSettings { get; private set; }
        public TraySettings? LastUpdatedSettings { get; private set; }
        public List<string> CallOrder { get; } = new();

        public void Show()
        {
            ShowCallCount++;
            IsVisible = true;
            CallOrder.Add("Show");
        }

        public void Hide()
        {
            HideCallCount++;
            IsVisible = false;
            CallOrder.Add("Hide");
        }

        public void UpdateSettings(TraySettings settings)
        {
            UpdateSettingsCallCount++;
            LastUpdatedSettings = settings;
            CallOrder.Add("UpdateSettings");
        }

        public void ResetPosition(TraySettings settings)
        {
            ResetPositionCallCount++;
            LastResetSettings = settings;
            CallOrder.Add("ResetPosition");
        }
    }

    private readonly TrackedWindowAdapter _windowAdapter = new();
    private readonly SynchronousDispatcher _dispatcher = new();
    private readonly TraySettings _enabledSettings = TraySettings.Default with { OverlayEnabled = true };
    private readonly TraySettings _disabledSettings = TraySettings.Default with { OverlayEnabled = false };

    [Fact]
    public void Initialize_Enabled_QueuesFirstShow()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.Initialize(_enabledSettings);

        Assert.NotNull(_dispatcher.QueuedAction);
        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void Initialize_Disabled_DoesNotQueue()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.Initialize(_disabledSettings);

        Assert.Null(_dispatcher.QueuedAction);
        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void ExecuteQueuedFirstShow_Enabled_ShowsAndResetsPosition()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 100.0, OverlayTop = 200.0 };

        coordinator.Initialize(settings);
        _dispatcher.ExecuteQueued();

        Assert.Equal(1, _windowAdapter.ShowCallCount);
        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
        Assert.Equal(100.0, _windowAdapter.LastResetSettings?.OverlayLeft);
        Assert.Equal(200.0, _windowAdapter.LastResetSettings?.OverlayTop);
    }

    [Fact]
    public void ExecuteQueuedFirstShow_AfterSetEnabledFalse_DoesNotShow()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.Initialize(_enabledSettings);
        coordinator.SetEnabled(_disabledSettings);
        _dispatcher.ExecuteQueued();

        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void ExecuteQueuedFirstShow_AfterPrepareForShutdown_DoesNotShow()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.Initialize(_enabledSettings);
        coordinator.PrepareForShutdown();
        _dispatcher.ExecuteQueued();

        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void EnsureVisible_EnabledAndNotVisible_ShowsAndResetsPosition()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 50.0, OverlayTop = 75.0 };

        coordinator.EnsureVisible(settings);

        Assert.Equal(1, _windowAdapter.ShowCallCount);
        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
        Assert.Equal(50.0, _windowAdapter.LastResetSettings?.OverlayLeft);
        Assert.Equal(75.0, _windowAdapter.LastResetSettings?.OverlayTop);
    }

    [Fact]
    public void EnsureVisible_EnabledAndVisible_DoesNotReShow()
    {
        _windowAdapter.IsVisible = true;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.EnsureVisible(_enabledSettings);

        Assert.Equal(0, _windowAdapter.ShowCallCount);
        Assert.Equal(0, _windowAdapter.ResetPositionCallCount);
    }

    [Fact]
    public void EnsureVisible_Disabled_DoesNotShow()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.EnsureVisible(_disabledSettings);

        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void EnsureVisible_AfterShutdown_DoesNotShow()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.PrepareForShutdown();
        coordinator.EnsureVisible(_enabledSettings);

        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void SetEnabled_True_ImmediatelyShows()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 300.0, OverlayTop = 400.0 };

        coordinator.SetEnabled(settings);

        Assert.Equal(1, _windowAdapter.ShowCallCount);
        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
        Assert.Equal(300.0, _windowAdapter.LastResetSettings?.OverlayLeft);
        Assert.Equal(400.0, _windowAdapter.LastResetSettings?.OverlayTop);
    }

    [Fact]
    public void SetEnabled_False_ImmediatelyHides()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.SetEnabled(_disabledSettings);

        Assert.Equal(1, _windowAdapter.HideCallCount);
    }

    [Fact]
    public void SetEnabled_AfterShutdown_DoesNothing()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.PrepareForShutdown();
        coordinator.SetEnabled(_enabledSettings);

        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void Initialize_DisabledThenEnabled_ViaSetEnabledImmediatelyShows()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.Initialize(_disabledSettings);
        coordinator.SetEnabled(_enabledSettings);

        Assert.Equal(1, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void ResetPosition_PassesSavedCoordinates()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 123.0, OverlayTop = 456.0 };

        coordinator.SetEnabled(settings);

        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
        Assert.Equal(123.0, _windowAdapter.LastResetSettings?.OverlayLeft);
        Assert.Equal(456.0, _windowAdapter.LastResetSettings?.OverlayTop);
    }

    [Fact]
    public void EnsureVisible_BeforeQueuedCallback_NoDuplicateShow()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 10.0, OverlayTop = 20.0 };

        coordinator.Initialize(settings);
        coordinator.EnsureVisible(settings);
        _dispatcher.ExecuteQueued();

        Assert.Equal(1, _windowAdapter.ShowCallCount);
        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
    }

    [Fact]
    public void QueuedCallback_UsesLatestSettings_NotInitial()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var initialSettings = _enabledSettings with { OverlayLeft = 1.0, OverlayTop = 2.0 };
        var updatedSettings = _enabledSettings with { OverlayLeft = 100.0, OverlayTop = 200.0 };

        coordinator.Initialize(initialSettings);
        coordinator.EnsureVisible(updatedSettings);

        Assert.Equal(100.0, _windowAdapter.LastResetSettings?.OverlayLeft);
        Assert.Equal(200.0, _windowAdapter.LastResetSettings?.OverlayTop);
    }

    [Fact]
    public void FirstShow_CallOrder_IsUpdateSettingsThenShowThenResetPosition()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 10.0, OverlayTop = 20.0 };

        coordinator.Initialize(settings);
        _dispatcher.ExecuteQueued();

        Assert.Equal(3, _windowAdapter.CallOrder.Count);
        Assert.Equal("UpdateSettings", _windowAdapter.CallOrder[0]);
        Assert.Equal("Show", _windowAdapter.CallOrder[1]);
        Assert.Equal("ResetPosition", _windowAdapter.CallOrder[2]);
    }

    [Fact]
    public void SetEnabled_True_CallOrder_IsUpdateSettingsThenShowThenResetPosition()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with { OverlayLeft = 30.0, OverlayTop = 40.0 };

        coordinator.SetEnabled(settings);

        Assert.Equal(3, _windowAdapter.CallOrder.Count);
        Assert.Equal("UpdateSettings", _windowAdapter.CallOrder[0]);
        Assert.Equal("Show", _windowAdapter.CallOrder[1]);
        Assert.Equal("ResetPosition", _windowAdapter.CallOrder[2]);
    }

    [Fact]
    public void SetEnabled_False_CallsUpdateSettingsBeforeHide()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.SetEnabled(_disabledSettings);

        Assert.Equal(2, _windowAdapter.CallOrder.Count);
        Assert.Equal("UpdateSettings", _windowAdapter.CallOrder[0]);
        Assert.Equal("Hide", _windowAdapter.CallOrder[1]);
    }

    [Fact]
    public void QueuedCallback_AfterSettingsUpdated_UsesNewPosition()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var initialSettings = _enabledSettings with { OverlayLeft = 1.0, OverlayTop = 2.0 };
        var newSettings = _enabledSettings with { OverlayLeft = 99.0, OverlayTop = 88.0 };

        coordinator.Initialize(initialSettings);
        coordinator.SetEnabled(newSettings);
        _dispatcher.ExecuteQueued();

        Assert.Equal(99.0, _windowAdapter.LastResetSettings?.OverlayLeft);
        Assert.Equal(88.0, _windowAdapter.LastResetSettings?.OverlayTop);
    }

    [Fact]
    public void QueuedCallback_DisabledOrShutdown_UsesLatestState()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);

        coordinator.Initialize(_enabledSettings);
        coordinator.PrepareForShutdown();
        _dispatcher.ExecuteQueued();

        Assert.Equal(0, _windowAdapter.ShowCallCount);
    }

    [Fact]
    public void ResetPosition_ReceivesFullTraySettings_IncludingPresetAndDeviceName()
    {
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with
        {
            SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.BottomRight),
            OverlayTargetMonitorId = @"\\.\DISPLAY2",
            OverlayLeft = null,
            OverlayTop = null
        };

        coordinator.SetEnabled(settings);

        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
        Assert.NotNull(_windowAdapter.LastResetSettings);
        var preset = Assert.IsType<OverlayPositionState.Preset>(_windowAdapter.LastResetSettings!.SharedPosition);
        Assert.Equal(OverlayPositionPreset.BottomRight, preset.Value);
        Assert.Equal(@"\\.\DISPLAY2", _windowAdapter.LastResetSettings.OverlayTargetMonitorId);
        Assert.Null(_windowAdapter.LastResetSettings.OverlayLeft);
        Assert.Null(_windowAdapter.LastResetSettings.OverlayTop);
    }

    [Fact]
    public void EnsureVisible_ReShow_PassesFullTraySettings_ToResetPosition()
    {
        _windowAdapter.IsVisible = false;
        var coordinator = new OverlayLifecycleCoordinator(_windowAdapter, _dispatcher);
        var settings = _enabledSettings with
        {
            SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.MiddleLeft),
            OverlayTargetMonitorId = @"\\.\DISPLAY3"
        };

        coordinator.EnsureVisible(settings);

        Assert.Equal(1, _windowAdapter.ResetPositionCallCount);
        Assert.NotNull(_windowAdapter.LastResetSettings);
        var preset = Assert.IsType<OverlayPositionState.Preset>(_windowAdapter.LastResetSettings!.SharedPosition);
        Assert.Equal(OverlayPositionPreset.MiddleLeft, preset.Value);
        Assert.Equal(@"\\.\DISPLAY3", _windowAdapter.LastResetSettings.OverlayTargetMonitorId);
    }
}
