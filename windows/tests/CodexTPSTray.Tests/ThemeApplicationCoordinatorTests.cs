using Xunit;

namespace CodexTPSTray.Tests;

public class ThemeApplicationCoordinatorTests
{
    private class TrackingSystemThemeSource : ISystemThemeSource
    {
        public EffectiveTheme ReturnValue { get; set; }
        public int CallCount { get; private set; }

        public EffectiveTheme GetCurrentTheme()
        {
            CallCount++;
            return ReturnValue;
        }
    }

    private class FakeThemeApplicationTarget : IThemeApplicationTarget
    {
        public EffectiveTheme? AppliedApplicationTheme { get; private set; }
        public int ApplicationApplyCount { get; private set; }
        public EffectiveTheme? AppliedOverlayTheme { get; private set; }
        public int OverlayApplyCount { get; private set; }

        public void ApplyApplicationTheme(EffectiveTheme theme)
        {
            AppliedApplicationTheme = theme;
            ApplicationApplyCount++;
        }

        public void ApplyOverlayTheme(EffectiveTheme theme)
        {
            AppliedOverlayTheme = theme;
            OverlayApplyCount++;
        }
    }

    [Fact]
    public void Apply_AppLight_FollowApplication_LightLight()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Light,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedOverlayTheme);
        Assert.Equal(0, systemSource.CallCount);
    }

    [Fact]
    public void Apply_AppDark_FollowApplication_DarkDark()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Dark,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
        Assert.Equal(0, systemSource.CallCount);
    }

    [Fact]
    public void Apply_SystemDark_FollowApplication_DarkDark_OneSystemCall()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Dark };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
        Assert.Equal(1, systemSource.CallCount);
    }

    [Fact]
    public void Apply_SystemLight_FollowApplication_LightLight_OneSystemCall()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Light };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedOverlayTheme);
        Assert.Equal(1, systemSource.CallCount);
    }

    [Fact]
    public void Apply_SystemSystem_OneSystemCall()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Dark };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.System
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(1, systemSource.CallCount);
    }

    [Fact]
    public void Apply_AppLight_OverlaySystemDark_LightDark()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Dark };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Light,
            OverlayTheme: OverlayThemePreference.System
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
        Assert.Equal(1, systemSource.CallCount);
    }

    [Fact]
    public void Apply_AppDark_OverlayLight_DarkLight()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Dark,
            OverlayTheme: OverlayThemePreference.Light
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedOverlayTheme);
        Assert.Equal(0, systemSource.CallCount);
    }

    [Fact]
    public void Apply_ExplicitOppositeThemes()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Light,
            OverlayTheme: OverlayThemePreference.Dark
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Light, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
        Assert.Equal(0, systemSource.CallCount);
    }

    [Fact]
    public void Apply_EachTargetCalledOnce()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Dark };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
    }

    [Fact]
    public void CurrentThemes_DefaultToLight()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Light, coordinator.CurrentOverlayTheme);
    }

    [Fact]
    public void Constructor_NullResolver_ThrowsArgumentNullException()
    {
        var target = new FakeThemeApplicationTarget();

        Assert.Throws<ArgumentNullException>(() => new ThemeApplicationCoordinator(null!, target));
    }

    [Fact]
    public void Constructor_NullTarget_ThrowsArgumentNullException()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);

        Assert.Throws<ArgumentNullException>(() => new ThemeApplicationCoordinator(resolver, null!));
    }

    [Fact]
    public void Apply_NullSettings_ThrowsArgumentNullException()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        Assert.Throws<ArgumentNullException>(() => coordinator.Apply(null!));
    }

    [Fact]
    public void StartupEquivalentPath_AppDark_FollowApplication_OverlayGetsDark()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Dark,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
    }

    [Fact]
    public void StartupEquivalentPath_SystemDark_FollowApplication_OverlayGetsDark()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Dark };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, coordinator.CurrentOverlayTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
        Assert.Equal(1, systemSource.CallCount);
    }

    [Fact]
    public void Apply_SameSettingsRepeated_DoesNotIncreaseCallCount()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Dark };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);

        coordinator.Apply(settings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);

        coordinator.Apply(settings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
    }

    [Fact]
    public void Apply_AppSystemOverlayDark_SystemUnchanged_NeitherTargetRecalled()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Light };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.Dark
        );

        coordinator.Apply(settings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
        Assert.Equal(EffectiveTheme.Light, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);

        coordinator.Apply(settings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
    }

    [Fact]
    public void Apply_AppSystemOverlayDark_SystemLightToDark_OnlyApplicationRecalled()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Light };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.Dark
        );

        coordinator.Apply(settings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
        Assert.Equal(EffectiveTheme.Light, target.AppliedApplicationTheme);

        systemSource.ReturnValue = EffectiveTheme.Dark;
        coordinator.Apply(settings);

        Assert.Equal(2, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);
    }

    [Fact]
    public void Apply_OverlayRealChange_OnlyOverlayRecalled()
    {
        var systemSource = new TrackingSystemThemeSource();
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var darkSettings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Light,
            OverlayTheme: OverlayThemePreference.Dark
        );

        coordinator.Apply(darkSettings);
        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
        Assert.Equal(EffectiveTheme.Dark, target.AppliedOverlayTheme);

        var lightSettings = darkSettings with { OverlayTheme = OverlayThemePreference.Light };
        coordinator.Apply(lightSettings);

        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(2, target.OverlayApplyCount);
        Assert.Equal(EffectiveTheme.Light, target.AppliedOverlayTheme);
    }

    [Fact]
    public void Apply_FirstApply_AlwaysCallsBothTargetsEvenForDefaultLight()
    {
        var systemSource = new TrackingSystemThemeSource { ReturnValue = EffectiveTheme.Light };
        var resolver = new ThemeResolver(systemSource);
        var target = new FakeThemeApplicationTarget();
        var coordinator = new ThemeApplicationCoordinator(resolver, target);

        var settings = new TraySettings(
            SelectedWindow: MetricWindow.OneMinute,
            RefreshCadence: RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Light,
            OverlayTheme: OverlayThemePreference.FollowApplication
        );

        coordinator.Apply(settings);

        Assert.Equal(1, target.ApplicationApplyCount);
        Assert.Equal(1, target.OverlayApplyCount);
    }
}
