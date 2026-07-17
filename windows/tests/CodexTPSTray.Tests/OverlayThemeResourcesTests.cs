using System;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayThemeResourcesTests
{
    private const string LightThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/OverlayLight.xaml";
    private const string DarkThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/OverlayDark.xaml";

    [Fact]
    public void GetThemeSource_Light_ReturnsLightSource()
    {
        string source = OverlayThemeResources.GetThemeSource(EffectiveTheme.Light);

        Assert.NotNull(source);
        Assert.Contains("OverlayLight", source);
    }

    [Fact]
    public void GetThemeSource_Dark_ReturnsDarkSource()
    {
        string source = OverlayThemeResources.GetThemeSource(EffectiveTheme.Dark);

        Assert.NotNull(source);
        Assert.Contains("OverlayDark", source);
    }

    [Fact]
    public void GetThemeSource_UnknownEnum_FallsBackToLight()
    {
        string source = OverlayThemeResources.GetThemeSource((EffectiveTheme)99);

        Assert.NotNull(source);
        Assert.Contains("OverlayLight", source);
    }

    [Fact]
    public void Apply_NullResources_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OverlayThemeResources.Apply(null!, EffectiveTheme.Light));
    }

    [Fact]
    public void Apply_Light_AddsOneManagedDictionary()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            OverlayThemeResources.Apply(resources, EffectiveTheme.Light);

            Assert.Single(resources.MergedDictionaries);
            Assert.NotNull(resources.MergedDictionaries[0].Source);
            Assert.Equal(LightThemeSource, resources.MergedDictionaries[0].Source!.OriginalString);
        });
    }

    [Fact]
    public void Apply_Dark_AddsOneManagedDictionary()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            OverlayThemeResources.Apply(resources, EffectiveTheme.Dark);

            Assert.Single(resources.MergedDictionaries);
            Assert.NotNull(resources.MergedDictionaries[0].Source);
            Assert.Equal(DarkThemeSource, resources.MergedDictionaries[0].Source!.OriginalString);
        });
    }

    [Fact]
    public void Apply_Light_Twice_NoAccumulation()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            OverlayThemeResources.Apply(resources, EffectiveTheme.Light);
            OverlayThemeResources.Apply(resources, EffectiveTheme.Light);

            Assert.Single(resources.MergedDictionaries);
            Assert.Equal(LightThemeSource, resources.MergedDictionaries[0].Source!.OriginalString);
        });
    }

    [Fact]
    public void Apply_LightThenDark_ReplacesWithDark()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            OverlayThemeResources.Apply(resources, EffectiveTheme.Light);
            OverlayThemeResources.Apply(resources, EffectiveTheme.Dark);

            Assert.Single(resources.MergedDictionaries);
            Assert.Equal(DarkThemeSource, resources.MergedDictionaries[0].Source!.OriginalString);
        });
    }

    [Fact]
    public void Apply_DarkThenLight_ReplacesWithLight()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            OverlayThemeResources.Apply(resources, EffectiveTheme.Dark);
            OverlayThemeResources.Apply(resources, EffectiveTheme.Light);

            Assert.Single(resources.MergedDictionaries);
            Assert.Equal(LightThemeSource, resources.MergedDictionaries[0].Source!.OriginalString);
        });
    }

    [Fact]
    public void Apply_PreservesExternalDictionaryWithoutSource()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();
            var externalDict = new ResourceDictionary();
            resources.MergedDictionaries.Add(externalDict);

            OverlayThemeResources.Apply(resources, EffectiveTheme.Light);
            OverlayThemeResources.Apply(resources, EffectiveTheme.Dark);

            Assert.Equal(2, resources.MergedDictionaries.Count);
            Assert.Contains(resources.MergedDictionaries, d => d == externalDict);
        });
    }
}
