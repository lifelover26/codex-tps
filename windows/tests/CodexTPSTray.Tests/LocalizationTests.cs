using Xunit;

namespace CodexTPSTray.Tests;

public class LocalizationTests
{
    [Fact]
    public void ThemeMenu_English()
    {
        Assert.Equal("Theme", Localization.ThemeMenu(Language.English));
    }

    [Fact]
    public void ThemeMenu_Chinese()
    {
        Assert.Equal("主题", Localization.ThemeMenu(Language.Chinese));
    }

    [Fact]
    public void OverlayThemeMenu_English()
    {
        Assert.Equal("Theme", Localization.OverlayThemeMenu(Language.English));
    }

    [Fact]
    public void OverlayThemeMenu_Chinese()
    {
        Assert.Equal("主题", Localization.OverlayThemeMenu(Language.Chinese));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_System_English()
    {
        Assert.Equal("System", Localization.GetApplicationThemeDisplayName(ApplicationThemePreference.System, Language.English));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_System_Chinese()
    {
        Assert.Equal("跟随系统", Localization.GetApplicationThemeDisplayName(ApplicationThemePreference.System, Language.Chinese));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_Light_English()
    {
        Assert.Equal("Light", Localization.GetApplicationThemeDisplayName(ApplicationThemePreference.Light, Language.English));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_Light_Chinese()
    {
        Assert.Equal("浅色", Localization.GetApplicationThemeDisplayName(ApplicationThemePreference.Light, Language.Chinese));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_Dark_English()
    {
        Assert.Equal("Dark", Localization.GetApplicationThemeDisplayName(ApplicationThemePreference.Dark, Language.English));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_Dark_Chinese()
    {
        Assert.Equal("深色", Localization.GetApplicationThemeDisplayName(ApplicationThemePreference.Dark, Language.Chinese));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_Unknown_English()
    {
        Assert.Equal("System", Localization.GetApplicationThemeDisplayName((ApplicationThemePreference)99, Language.English));
    }

    [Fact]
    public void GetApplicationThemeDisplayName_Unknown_Chinese()
    {
        Assert.Equal("跟随系统", Localization.GetApplicationThemeDisplayName((ApplicationThemePreference)99, Language.Chinese));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_FollowApplication_English()
    {
        Assert.Equal("Follow Application", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.FollowApplication, Language.English));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_FollowApplication_Chinese()
    {
        Assert.Equal("跟随应用", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.FollowApplication, Language.Chinese));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_System_English()
    {
        Assert.Equal("System", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.System, Language.English));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_System_Chinese()
    {
        Assert.Equal("跟随系统", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.System, Language.Chinese));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_Light_English()
    {
        Assert.Equal("Light", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.Light, Language.English));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_Light_Chinese()
    {
        Assert.Equal("浅色", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.Light, Language.Chinese));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_Dark_English()
    {
        Assert.Equal("Dark", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.Dark, Language.English));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_Dark_Chinese()
    {
        Assert.Equal("深色", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.Dark, Language.Chinese));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_Unknown_English()
    {
        Assert.Equal("Follow Application", Localization.GetOverlayThemeDisplayName((OverlayThemePreference)99, Language.English));
    }

    [Fact]
    public void GetOverlayThemeDisplayName_Unknown_Chinese()
    {
        Assert.Equal("跟随应用", Localization.GetOverlayThemeDisplayName((OverlayThemePreference)99, Language.Chinese));
    }
}
