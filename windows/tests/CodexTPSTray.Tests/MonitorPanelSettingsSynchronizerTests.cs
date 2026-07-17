using Xunit;

namespace CodexTPSTray.Tests;

public class MonitorPanelSettingsSynchronizerTests
{
    [Fact]
    public void Apply_BeforeAttach_LanguageChanged_ViewModelUpdated()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese });

        Assert.Equal(Language.Chinese, synchronizer.ViewModel.Language);
        Assert.Equal("等待数据", synchronizer.ViewModel.StatusText);
    }

    [Fact]
    public void Apply_BeforeAttach_MultipleSettingsChanged_ViewModelRetainsAll()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese });
        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese, SelectedWindow = MetricWindow.OneHour });
        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese, SelectedWindow = MetricWindow.OneHour, RefreshCadence = RefreshCadence.SixtySeconds });

        Assert.Equal(Language.Chinese, synchronizer.ViewModel.Language);
        Assert.Equal(MetricWindow.OneHour, synchronizer.ViewModel.SelectedWindow);
        Assert.Equal(RefreshCadence.SixtySeconds, synchronizer.ViewModel.SelectedCadence);
        Assert.Equal("1 小时", synchronizer.ViewModel.SelectedWindowDisplayName);
    }

    [Fact]
    public void Apply_AfterAttach_PanelUpdaterCalled()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);
        bool updaterCalled = false;
        TraySettings? receivedSettings = null;

        synchronizer.AttachPanelUpdater(settings =>
        {
            updaterCalled = true;
            receivedSettings = settings;
        });

        var newSettings = TraySettings.Default with { Language = Language.Chinese };
        synchronizer.Apply(newSettings);

        Assert.True(updaterCalled);
        Assert.Same(newSettings, receivedSettings);
    }

    [Fact]
    public void Apply_AfterAttach_PanelUpdaterCalledOnlyOnce()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);
        int callCount = 0;

        synchronizer.AttachPanelUpdater(_ => callCount++);

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Apply_AfterAttach_ViewModelUpdatedThroughPanelUpdater()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);
        int viewModelUpdateCount = 0;

        synchronizer.AttachPanelUpdater(settings =>
        {
            viewModelUpdateCount++;
            synchronizer.ViewModel.UpdateSettings(settings);
        });

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese });

        Assert.Equal(1, viewModelUpdateCount);
        Assert.Equal(Language.Chinese, synchronizer.ViewModel.Language);
    }

    [Fact]
    public void Apply_AfterAttach_ViewModelPropertyChangedFiresOnce()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);
        int languageChangedCount = 0;

        synchronizer.ViewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(synchronizer.ViewModel.Language))
                languageChangedCount++;
        };

        synchronizer.AttachPanelUpdater(settings =>
        {
            synchronizer.ViewModel.UpdateSettings(settings);
        });

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese });

        Assert.Equal(1, languageChangedCount);
    }

    [Fact]
    public void AttachPanelUpdater_Null_ThrowsArgumentNullException()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);

        Assert.Throws<ArgumentNullException>(() => synchronizer.AttachPanelUpdater(null!));
    }

    [Fact]
    public void Apply_BeforeAttachThenAttachThenApply_StateConsistent()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese });
        Assert.Equal(Language.Chinese, synchronizer.ViewModel.Language);

        int updaterCallCount = 0;
        synchronizer.AttachPanelUpdater(settings =>
        {
            updaterCallCount++;
            synchronizer.ViewModel.UpdateSettings(settings);
        });

        synchronizer.Apply(TraySettings.Default with { Language = Language.Chinese, SelectedWindow = MetricWindow.OneHour });

        Assert.Equal(1, updaterCallCount);
        Assert.Equal(Language.Chinese, synchronizer.ViewModel.Language);
        Assert.Equal(MetricWindow.OneHour, synchronizer.ViewModel.SelectedWindow);
    }

    [Fact]
    public void Apply_BeforeAttach_MetricWindowChanged_ViewModelUpdated()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);

        synchronizer.Apply(TraySettings.Default with { SelectedWindow = MetricWindow.FiveMinutes });

        Assert.Equal(MetricWindow.FiveMinutes, synchronizer.ViewModel.SelectedWindow);
        Assert.Equal("5 min", synchronizer.ViewModel.SelectedWindowDisplayName);
    }

    [Fact]
    public void Apply_BeforeAttach_RefreshCadenceChanged_ViewModelUpdated()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);

        synchronizer.Apply(TraySettings.Default with { RefreshCadence = RefreshCadence.FiveSeconds });

        Assert.Equal(RefreshCadence.FiveSeconds, synchronizer.ViewModel.SelectedCadence);
        Assert.Equal("5 Seconds", synchronizer.ViewModel.SelectedCadenceDisplayName);
    }

    [Fact]
    public void Apply_AfterAttach_MetricWindowChanged_PanelUpdaterCalled()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);
        int callCount = 0;

        synchronizer.AttachPanelUpdater(_ => callCount++);

        synchronizer.Apply(TraySettings.Default with { SelectedWindow = MetricWindow.FiveMinutes });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Apply_AfterAttach_RefreshCadenceChanged_PanelUpdaterCalled()
    {
        var synchronizer = new MonitorPanelSettingsSynchronizer(TraySettings.Default);
        int callCount = 0;

        synchronizer.AttachPanelUpdater(_ => callCount++);

        synchronizer.Apply(TraySettings.Default with { RefreshCadence = RefreshCadence.ThirtySeconds });

        Assert.Equal(1, callCount);
    }
}
