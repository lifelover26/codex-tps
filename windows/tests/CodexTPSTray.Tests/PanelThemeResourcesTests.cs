using System;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace CodexTPSTray.Tests;

public class PanelThemeResourcesTests
{
    private const string LightThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/PanelLight.xaml";
    private const string DarkThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/PanelDark.xaml";

    [Fact]
    public void GetThemeSource_Light_ReturnsLightSource()
    {
        string source = PanelThemeResources.GetThemeSource(EffectiveTheme.Light);

        Assert.NotNull(source);
        Assert.Contains("PanelLight", source);
    }

    [Fact]
    public void GetThemeSource_Dark_ReturnsDarkSource()
    {
        string source = PanelThemeResources.GetThemeSource(EffectiveTheme.Dark);

        Assert.NotNull(source);
        Assert.Contains("PanelDark", source);
    }

    [Fact]
    public void GetThemeSource_UnknownEnum_FallsBackToLight()
    {
        string source = PanelThemeResources.GetThemeSource((EffectiveTheme)99);

        Assert.NotNull(source);
        Assert.Contains("PanelLight", source);
    }

    [Fact]
    public void Apply_NullResources_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PanelThemeResources.Apply(null!, EffectiveTheme.Light));
    }

    [Fact]
    public void Apply_Light_AddsOneManagedDictionary()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            PanelThemeResources.Apply(resources, EffectiveTheme.Light);

            Assert.Single(resources.MergedDictionaries);
            Assert.NotNull(resources.MergedDictionaries[0].Source);
            Assert.Equal(LightThemeSource, resources.MergedDictionaries[0].Source!.OriginalString);
        });
    }

    [Fact]
    public void Apply_Light_Twice_NoAccumulation()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary();

            PanelThemeResources.Apply(resources, EffectiveTheme.Light);
            PanelThemeResources.Apply(resources, EffectiveTheme.Light);

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

            PanelThemeResources.Apply(resources, EffectiveTheme.Light);
            PanelThemeResources.Apply(resources, EffectiveTheme.Dark);

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

            PanelThemeResources.Apply(resources, EffectiveTheme.Dark);
            PanelThemeResources.Apply(resources, EffectiveTheme.Light);

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

            PanelThemeResources.Apply(resources, EffectiveTheme.Light);
            PanelThemeResources.Apply(resources, EffectiveTheme.Dark);

            Assert.Equal(2, resources.MergedDictionaries.Count);
            Assert.Contains(resources.MergedDictionaries, d => d == externalDict);
        });
    }

    [Fact]
    public void LightTheme_ComboBoxSelectedForeground_IsReadable()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary { Source = new Uri(LightThemeSource) };

            var accentForeground = (SolidColorBrush)resources["PanelAccentForegroundBrush"];
            var selectedForeground = (SolidColorBrush)resources["PanelComboBoxSelectedForegroundBrush"];

            Assert.NotNull(selectedForeground);
            Assert.Equal(Colors.White, accentForeground.Color);
            Assert.NotEqual(Colors.White, selectedForeground.Color);
        });
    }

    [Fact]
    public void LightTheme_ComboBoxSelectedForeground_MatchesPrimaryText()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary { Source = new Uri(LightThemeSource) };

            var primaryText = (SolidColorBrush)resources["PanelPrimaryTextBrush"];
            var selectedForeground = (SolidColorBrush)resources["PanelComboBoxSelectedForegroundBrush"];

            Assert.NotNull(selectedForeground);
            Assert.Equal(primaryText.Color, selectedForeground.Color);
        });
    }

    [Fact]
    public void DarkTheme_ComboBoxSelectedForeground_IsLight()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var resources = new ResourceDictionary { Source = new Uri(DarkThemeSource) };

            var selectedForeground = (SolidColorBrush)resources["PanelComboBoxSelectedForegroundBrush"];
            var selectedBackground = (SolidColorBrush)resources["PanelComboBoxSelectedBackgroundBrush"];
            var accentBrush = (SolidColorBrush)resources["PanelAccentBrush"];

            Assert.NotNull(selectedForeground);
            Assert.NotNull(selectedBackground);
            Assert.Equal(Colors.White, selectedForeground.Color);
            Assert.Equal(accentBrush.Color, selectedBackground.Color);
        });
    }

    [Fact]
    public void ComboBoxSelectedResources_Defined_In_Both_Themes()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var lightResources = new ResourceDictionary { Source = new Uri(LightThemeSource) };
            var darkResources = new ResourceDictionary { Source = new Uri(DarkThemeSource) };

            Assert.True(lightResources.Contains("PanelComboBoxSelectedBackgroundBrush"));
            Assert.True(lightResources.Contains("PanelComboBoxSelectedForegroundBrush"));
            Assert.True(darkResources.Contains("PanelComboBoxSelectedBackgroundBrush"));
            Assert.True(darkResources.Contains("PanelComboBoxSelectedForegroundBrush"));
        });
    }
}
