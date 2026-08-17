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
        Assert.Equal("Follow Windows", Localization.GetOverlayThemeDisplayName(OverlayThemePreference.System, Language.English));
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

    [Fact]
    public void PositionMenu_English()
    {
        Assert.Equal("Position", Localization.PositionMenu(Language.English));
    }

    [Fact]
    public void PositionMenu_Chinese()
    {
        Assert.Equal("快速定位", Localization.PositionMenu(Language.Chinese));
    }

    [Fact]
    public void GetPositionPresetDisplayName_TopLeft_English()
    {
        Assert.Equal("Top Left", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.TopLeft, Language.English));
    }

    [Fact]
    public void GetPositionPresetDisplayName_TopLeft_Chinese()
    {
        Assert.Equal("左上角", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.TopLeft, Language.Chinese));
    }

    [Fact]
    public void GetPositionPresetDisplayName_TopRight_English()
    {
        Assert.Equal("Top Right", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.TopRight, Language.English));
    }

    [Fact]
    public void GetPositionPresetDisplayName_TopRight_Chinese()
    {
        Assert.Equal("右上角", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.TopRight, Language.Chinese));
    }

    [Fact]
    public void GetPositionPresetDisplayName_MiddleLeft_English()
    {
        Assert.Equal("Middle Left", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.MiddleLeft, Language.English));
    }

    [Fact]
    public void GetPositionPresetDisplayName_MiddleLeft_Chinese()
    {
        Assert.Equal("左侧居中", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.MiddleLeft, Language.Chinese));
    }

    [Fact]
    public void GetPositionPresetDisplayName_MiddleRight_English()
    {
        Assert.Equal("Middle Right", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.MiddleRight, Language.English));
    }

    [Fact]
    public void GetPositionPresetDisplayName_MiddleRight_Chinese()
    {
        Assert.Equal("右侧居中", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.MiddleRight, Language.Chinese));
    }

    [Fact]
    public void GetPositionPresetDisplayName_BottomLeft_English()
    {
        Assert.Equal("Bottom Left", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.BottomLeft, Language.English));
    }

    [Fact]
    public void GetPositionPresetDisplayName_BottomLeft_Chinese()
    {
        Assert.Equal("左下角", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.BottomLeft, Language.Chinese));
    }

    [Fact]
    public void GetPositionPresetDisplayName_BottomRight_English()
    {
        Assert.Equal("Bottom Right", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.BottomRight, Language.English));
    }

    [Fact]
    public void GetPositionPresetDisplayName_BottomRight_Chinese()
    {
        Assert.Equal("右下角", Localization.GetPositionPresetDisplayName(OverlayPositionPreset.BottomRight, Language.Chinese));
    }

    [Fact]
    public void BackgroundOpacityMenu_English()
    {
        Assert.Equal("Background Opacity", Localization.BackgroundOpacityMenu(Language.English));
    }

    [Fact]
    public void BackgroundOpacityMenu_Chinese()
    {
        Assert.Equal("背景不透明度", Localization.BackgroundOpacityMenu(Language.Chinese));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Default_English()
    {
        Assert.Equal("Default", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Default, Language.English));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Default_Chinese()
    {
        Assert.Equal("默认", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Default, Language.Chinese));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Percent40()
    {
        Assert.Equal("40%", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Percent40, Language.English));
        Assert.Equal("40%", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Percent40, Language.Chinese));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Percent55()
    {
        Assert.Equal("55%", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Percent55, Language.English));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Percent70()
    {
        Assert.Equal("70%", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Percent70, Language.English));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Percent85()
    {
        Assert.Equal("85%", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Percent85, Language.English));
    }

    [Fact]
    public void GetOverlayOpacityDisplayName_Opaque()
    {
        Assert.Equal("100%", Localization.GetOverlayOpacityDisplayName(OverlayOpacityPreference.Opaque, Language.English));
    }

    [Fact]
    public void DataSourceMenu_English()
    {
        Assert.Equal("Data Source", Localization.DataSourceMenu(Language.English));
    }

    [Fact]
    public void DataSourceMenu_Chinese()
    {
        Assert.Equal("数据源", Localization.DataSourceMenu(Language.Chinese));
    }

    [Fact]
    public void DataSourceWindows_English()
    {
        Assert.Equal("Windows", Localization.DataSourceWindows(Language.English));
    }

    [Fact]
    public void DataSourceWindows_Chinese()
    {
        Assert.Equal("Windows", Localization.DataSourceWindows(Language.Chinese));
    }

    [Fact]
    public void DataSourceDetectingWsl_English()
    {
        Assert.Equal("Detecting WSL...", Localization.DataSourceDetectingWsl(Language.English));
    }

    [Fact]
    public void DataSourceDetectingWsl_Chinese()
    {
        Assert.Equal("正在检测 WSL...", Localization.DataSourceDetectingWsl(Language.Chinese));
    }

    [Fact]
    public void DataSourceNoWsl_English()
    {
        Assert.Equal("No WSL data sources", Localization.DataSourceNoWsl(Language.English));
    }

    [Fact]
    public void DataSourceNoWsl_Chinese()
    {
        Assert.Equal("未找到 WSL 数据源", Localization.DataSourceNoWsl(Language.Chinese));
    }

    [Fact]
    public void DataSourceSwitchFailed_English()
    {
        Assert.Equal("Failed to switch data source.", Localization.DataSourceSwitchFailed(Language.English));
    }

    [Fact]
    public void DataSourceSwitchFailed_Chinese()
    {
        Assert.Equal("切换数据源失败。", Localization.DataSourceSwitchFailed(Language.Chinese));
    }

    [Fact]
    public void DataSourceStartupWslFailed_English()
    {
        Assert.Equal("Failed to connect to WSL data source at startup, settings retained.", Localization.DataSourceStartupWslFailed(Language.English));
    }

    [Fact]
    public void DataSourceStartupWslFailed_Chinese()
    {
        Assert.Equal("启动时无法连接到 WSL 数据源，已保留设置。", Localization.DataSourceStartupWslFailed(Language.Chinese));
    }

    [Fact]
    public void DataSourceSaveFailed_English()
    {
        Assert.Equal("Failed to save data source settings.", Localization.DataSourceSaveFailed(Language.English));
    }

    [Fact]
    public void DataSourceSaveFailed_Chinese()
    {
        Assert.Equal("保存数据源设置失败。", Localization.DataSourceSaveFailed(Language.Chinese));
    }

    [Fact]
    public void CustomPositionMenu_English()
    {
        Assert.Equal("Custom Position", Localization.CustomPositionMenu(Language.English));
    }

    [Fact]
    public void CustomPositionMenu_Chinese()
    {
        Assert.Equal("自定义位置", Localization.CustomPositionMenu(Language.Chinese));
    }

    [Fact]
    public void GetCustomPositionModeDisplayName_KeepRelative_English()
    {
        Assert.Equal("Keep Relative Position", Localization.GetCustomPositionModeDisplayName(OverlayCustomPositionMode.KeepRelative, Language.English));
    }

    [Fact]
    public void GetCustomPositionModeDisplayName_KeepRelative_Chinese()
    {
        Assert.Equal("保持相对位置", Localization.GetCustomPositionModeDisplayName(OverlayCustomPositionMode.KeepRelative, Language.Chinese));
    }

    [Fact]
    public void GetCustomPositionModeDisplayName_RememberPerDisplay_English()
    {
        Assert.Equal("Remember per display", Localization.GetCustomPositionModeDisplayName(OverlayCustomPositionMode.RememberPerDisplay, Language.English));
    }

    [Fact]
    public void GetCustomPositionModeDisplayName_RememberPerDisplay_Chinese()
    {
        Assert.Equal("按显示器记忆", Localization.GetCustomPositionModeDisplayName(OverlayCustomPositionMode.RememberPerDisplay, Language.Chinese));
    }

    [Fact]
    public void CustomPositionModeLabels_AreConciseAndNonEmpty()
    {
        foreach (OverlayCustomPositionMode mode in Enum.GetValues<OverlayCustomPositionMode>())
        {
            string en = Localization.GetCustomPositionModeDisplayName(mode, Language.English);
            string cn = Localization.GetCustomPositionModeDisplayName(mode, Language.Chinese);
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.False(string.IsNullOrWhiteSpace(cn));
            Assert.True(en.Length <= 32, $"English label too long: {en}");
            Assert.True(cn.Length <= 16, $"Chinese label too long: {cn}");
        }
    }
}
