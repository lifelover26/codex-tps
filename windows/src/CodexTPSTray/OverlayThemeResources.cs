using System;
using System.Windows;

namespace CodexTPSTray;

internal static class OverlayThemeResources
{
    private const string LightThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/OverlayLight.xaml";
    private const string DarkThemeSource = "pack://application:,,,/CodexTPSTray;component/Themes/OverlayDark.xaml";

    public static string GetThemeSource(EffectiveTheme theme)
    {
        return theme switch
        {
            EffectiveTheme.Dark => DarkThemeSource,
            _ => LightThemeSource
        };
    }

    public static void Apply(ResourceDictionary resources, EffectiveTheme theme)
    {
        if (resources == null)
            throw new ArgumentNullException(nameof(resources));

        string newSource = GetThemeSource(theme);

        RemoveManagedThemeDictionaries(resources);

        var themeDictionary = new ResourceDictionary { Source = new Uri(newSource, UriKind.Absolute) };
        resources.MergedDictionaries.Add(themeDictionary);
    }

    private static void RemoveManagedThemeDictionaries(ResourceDictionary resources)
    {
        for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var dict = resources.MergedDictionaries[i];
            if (IsManagedThemeSource(dict.Source))
            {
                resources.MergedDictionaries.RemoveAt(i);
            }
        }
    }

    private static bool IsManagedThemeSource(Uri? uri)
    {
        if (uri == null)
            return false;

        return IsManagedThemeSource(uri.OriginalString);
    }

    internal static bool IsManagedThemeSource(string? source)
    {
        if (source == null)
            return false;

        return source.Equals(LightThemeSource, StringComparison.OrdinalIgnoreCase)
            || source.Equals(DarkThemeSource, StringComparison.OrdinalIgnoreCase);
    }
}
