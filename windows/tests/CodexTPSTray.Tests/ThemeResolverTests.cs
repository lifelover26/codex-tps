using System.Windows.Forms;
using Xunit;

namespace CodexTPSTray.Tests;

public class ThemeResolverTests
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

    private readonly TrackingSystemThemeSource _systemSource = new();

    [Fact]
    public void ResolveApplication_Light_ReturnsLight()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveApplication(ApplicationThemePreference.Light);

        Assert.Equal(EffectiveTheme.Light, result);
        Assert.Equal(0, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveApplication_Dark_ReturnsDark()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveApplication(ApplicationThemePreference.Dark);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(0, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveApplication_System_Light_ReturnsLight()
    {
        _systemSource.ReturnValue = EffectiveTheme.Light;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveApplication(ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Light, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveApplication_System_Dark_ReturnsDark()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveApplication(ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveApplication_UnknownEnum_FallsBackToSystem()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveApplication((ApplicationThemePreference)99);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_Light_ReturnsLight()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.Light, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Light, result);
        Assert.Equal(0, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_Dark_ReturnsDark()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.Dark, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(0, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_System_Light_ReturnsLight()
    {
        _systemSource.ReturnValue = EffectiveTheme.Light;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.System, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Light, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_System_Dark_ReturnsDark()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.System, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_FollowApplication_AppLight_ReturnsLight()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.FollowApplication, ApplicationThemePreference.Light);

        Assert.Equal(EffectiveTheme.Light, result);
        Assert.Equal(0, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_FollowApplication_AppDark_ReturnsDark()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.FollowApplication, ApplicationThemePreference.Dark);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(0, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_FollowApplication_AppSystem_Light_ReturnsLight()
    {
        _systemSource.ReturnValue = EffectiveTheme.Light;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.FollowApplication, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Light, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_FollowApplication_AppSystem_Dark_ReturnsDark()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay(OverlayThemePreference.FollowApplication, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_UnknownEnum_FallsBackToFollowApplication()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveOverlay((OverlayThemePreference)99, ApplicationThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveApplication_System_CallsSourceOnce()
    {
        _systemSource.ReturnValue = EffectiveTheme.Light;
        var resolver = new ThemeResolver(_systemSource);

        resolver.ResolveApplication(ApplicationThemePreference.System);

        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_System_CallsSourceOnce()
    {
        _systemSource.ReturnValue = EffectiveTheme.Light;
        var resolver = new ThemeResolver(_systemSource);

        resolver.ResolveOverlay(OverlayThemePreference.System, ApplicationThemePreference.Light);

        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveOverlay_FollowApplication_AppSystem_CallsSourceOnce()
    {
        _systemSource.ReturnValue = EffectiveTheme.Light;
        var resolver = new ThemeResolver(_systemSource);

        resolver.ResolveOverlay(OverlayThemePreference.FollowApplication, ApplicationThemePreference.System);

        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveBoth_AppSystem_OverlaySystem_Dark_DarkDark_OneCall()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveBoth(ApplicationThemePreference.System, OverlayThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result.ApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, result.OverlayTheme);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveBoth_AppLight_OverlaySystem_Dark_LightDark_OneCall()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveBoth(ApplicationThemePreference.Light, OverlayThemePreference.System);

        Assert.Equal(EffectiveTheme.Light, result.ApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, result.OverlayTheme);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveBoth_AppUnknown_OverlayFollowApplication_Dark_DarkDark_OneCall()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveBoth((ApplicationThemePreference)99, OverlayThemePreference.FollowApplication);

        Assert.Equal(EffectiveTheme.Dark, result.ApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, result.OverlayTheme);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveBoth_AppUnknown_OverlaySystem_Dark_DarkDark_OneCall()
    {
        _systemSource.ReturnValue = EffectiveTheme.Dark;
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveBoth((ApplicationThemePreference)99, OverlayThemePreference.System);

        Assert.Equal(EffectiveTheme.Dark, result.ApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, result.OverlayTheme);
        Assert.Equal(1, _systemSource.CallCount);
    }

    [Fact]
    public void ResolveBoth_AppDark_OverlayUnknown_DarkDark_NoCall()
    {
        var resolver = new ThemeResolver(_systemSource);

        var result = resolver.ResolveBoth(ApplicationThemePreference.Dark, (OverlayThemePreference)99);

        Assert.Equal(EffectiveTheme.Dark, result.ApplicationTheme);
        Assert.Equal(EffectiveTheme.Dark, result.OverlayTheme);
        Assert.Equal(0, _systemSource.CallCount);
    }
}

public class WindowsSystemThemeSourceTests
{
    [Fact]
    public void GetCurrentTheme_Dark_ReturnsDark()
    {
        var source = new WindowsSystemThemeSource(() => SystemColorMode.Dark);

        var result = source.GetCurrentTheme();

        Assert.Equal(EffectiveTheme.Dark, result);
    }

    [Fact]
    public void GetCurrentTheme_Classic_ReturnsLight()
    {
        var source = new WindowsSystemThemeSource(() => SystemColorMode.Classic);

        var result = source.GetCurrentTheme();

        Assert.Equal(EffectiveTheme.Light, result);
    }

    [Fact]
    public void GetCurrentTheme_UnknownEnum_ReturnsLight()
    {
        var source = new WindowsSystemThemeSource(() => (SystemColorMode)99);

        var result = source.GetCurrentTheme();

        Assert.Equal(EffectiveTheme.Light, result);
    }

    [Fact]
    public void GetCurrentTheme_ProviderThrows_ReturnsLight()
    {
        var source = new WindowsSystemThemeSource(() => throw new InvalidOperationException());

        var result = source.GetCurrentTheme();

        Assert.Equal(EffectiveTheme.Light, result);
    }
}
