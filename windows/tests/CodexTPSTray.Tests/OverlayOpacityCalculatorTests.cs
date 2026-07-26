using System;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayOpacityCalculatorTests
{
    [Fact]
    public void ResolveAlpha_Default_Light_ReturnsLightDefaultAlpha()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Default, EffectiveTheme.Light);
        Assert.Equal(OverlayOpacityCalculator.LightDefaultAlpha, alpha);
        Assert.Equal(0xCC, alpha);
    }

    [Fact]
    public void ResolveAlpha_Default_Dark_ReturnsDarkDefaultAlpha()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Default, EffectiveTheme.Dark);
        Assert.Equal(OverlayOpacityCalculator.DarkDefaultAlpha, alpha);
        Assert.Equal(0x73, alpha);
    }

    [Fact]
    public void ResolveAlpha_Percent40_ReturnsCorrectAlpha()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Percent40, EffectiveTheme.Light);
        Assert.Equal((byte)Math.Round(255 * 0.40), alpha);
    }

    [Fact]
    public void ResolveAlpha_Percent55_ReturnsCorrectAlpha()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Percent55, EffectiveTheme.Light);
        Assert.Equal((byte)Math.Round(255 * 0.55), alpha);
    }

    [Fact]
    public void ResolveAlpha_Percent70_ReturnsCorrectAlpha()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Percent70, EffectiveTheme.Light);
        Assert.Equal((byte)Math.Round(255 * 0.70), alpha);
    }

    [Fact]
    public void ResolveAlpha_Percent85_ReturnsCorrectAlpha()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Percent85, EffectiveTheme.Light);
        Assert.Equal((byte)Math.Round(255 * 0.85), alpha);
    }

    [Fact]
    public void ResolveAlpha_Opaque_Returns255()
    {
        byte alpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Opaque, EffectiveTheme.Light);
        Assert.Equal(255, alpha);
    }

    [Fact]
    public void ResolveAlpha_CustomPreferences_IgnoreTheme()
    {
        byte lightAlpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Percent40, EffectiveTheme.Light);
        byte darkAlpha = OverlayOpacityCalculator.ResolveAlpha(OverlayOpacityPreference.Percent40, EffectiveTheme.Dark);

        Assert.Equal(lightAlpha, darkAlpha);
    }

    [Fact]
    public void GetBackgroundRgb_Light_ReturnsLightRgb()
    {
        var color = OverlayOpacityCalculator.GetBackgroundRgb(EffectiveTheme.Light);
        Assert.Equal(0xF0, color.R);
        Assert.Equal(0xF0, color.G);
        Assert.Equal(0xF0, color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void GetBackgroundRgb_Dark_ReturnsDarkRgb()
    {
        var color = OverlayOpacityCalculator.GetBackgroundRgb(EffectiveTheme.Dark);
        Assert.Equal(0x1C, color.R);
        Assert.Equal(0x1C, color.G);
        Assert.Equal(0x1C, color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void CreateBackgroundColor_Percent40_Light_OnlyAlphaChanges()
    {
        var color = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Percent40, EffectiveTheme.Light);
        Assert.Equal(0xF0, color.R);
        Assert.Equal(0xF0, color.G);
        Assert.Equal(0xF0, color.B);
        Assert.Equal((byte)Math.Round(255 * 0.40), color.A);
    }

    [Fact]
    public void CreateBackgroundColor_Percent40_Dark_OnlyAlphaChanges()
    {
        var color = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Percent40, EffectiveTheme.Dark);
        Assert.Equal(0x1C, color.R);
        Assert.Equal(0x1C, color.G);
        Assert.Equal(0x1C, color.B);
        Assert.Equal((byte)Math.Round(255 * 0.40), color.A);
    }

    [Fact]
    public void CreateBackgroundColor_Default_Light_PreservesThemeAlpha()
    {
        var color = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Default, EffectiveTheme.Light);
        Assert.Equal(0xF0, color.R);
        Assert.Equal(0xF0, color.G);
        Assert.Equal(0xF0, color.B);
        Assert.Equal(0xCC, color.A);
    }

    [Fact]
    public void CreateBackgroundColor_Default_Dark_PreservesThemeAlpha()
    {
        var color = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Default, EffectiveTheme.Dark);
        Assert.Equal(0x1C, color.R);
        Assert.Equal(0x1C, color.G);
        Assert.Equal(0x1C, color.B);
        Assert.Equal(0x73, color.A);
    }

    [Fact]
    public void CreateBackgroundColor_ThemeSwitch_PreservesCustomAlpha()
    {
        var lightColor = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Percent70, EffectiveTheme.Light);
        var darkColor = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Percent70, EffectiveTheme.Dark);

        Assert.Equal(lightColor.A, darkColor.A);
        Assert.NotEqual(lightColor.R, darkColor.R);
    }

    [Fact]
    public void CreateBackgroundColor_ResetToDefault_AfterCustom_UsesThemeDefault()
    {
        var customColor = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Percent40, EffectiveTheme.Dark);
        var defaultColor = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Default, EffectiveTheme.Dark);

        Assert.NotEqual(customColor.A, defaultColor.A);
        Assert.Equal(OverlayOpacityCalculator.DarkDefaultAlpha, defaultColor.A);
        Assert.Equal(0x1C, defaultColor.R);
    }

    [Fact]
    public void CreateBackgroundColor_Opaque_Returns255Alpha()
    {
        var color = OverlayOpacityCalculator.CreateBackgroundColor(OverlayOpacityPreference.Opaque, EffectiveTheme.Light);
        Assert.Equal(255, color.A);
    }
}