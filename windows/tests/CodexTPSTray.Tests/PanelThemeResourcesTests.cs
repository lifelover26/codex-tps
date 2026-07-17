using System;
using System.Threading;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class PanelThemeResourcesTests
{
    private const string LightThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/PanelLight.xaml";
    private const string DarkThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/PanelDark.xaml";

    private static readonly object _wpfLock = new();
    private static bool _wpfInitialized;

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
        RunInStaWithWpf(() =>
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
        RunInStaWithWpf(() =>
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
        RunInStaWithWpf(() =>
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
        RunInStaWithWpf(() =>
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
        RunInStaWithWpf(() =>
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

    private static void RunInStaWithWpf(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && System.Windows.Application.Current != null)
        {
            action();
            return;
        }

        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                InitializeWpfOnce();
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }

    private static void InitializeWpfOnce()
    {
        lock (_wpfLock)
        {
            if (!_wpfInitialized && System.Windows.Application.Current == null)
            {
                _ = new System.Windows.Application();
                _wpfInitialized = true;
            }
        }
    }
}
