using System;
using System.Windows.Forms;

namespace CodexTPSTray;

internal enum EffectiveTheme
{
    Light,
    Dark
}

internal interface ISystemThemeSource
{
    EffectiveTheme GetCurrentTheme();
}

internal sealed class WindowsSystemThemeSource : ISystemThemeSource
{
    private readonly Func<SystemColorMode> _getColorMode;

    public WindowsSystemThemeSource()
        : this(() => Application.SystemColorMode)
    {
    }

    internal WindowsSystemThemeSource(Func<SystemColorMode> getColorMode)
    {
        _getColorMode = getColorMode;
    }

    public EffectiveTheme GetCurrentTheme()
    {
        try
        {
            SystemColorMode colorMode = _getColorMode();
            return colorMode switch
            {
                SystemColorMode.Dark => EffectiveTheme.Dark,
                SystemColorMode.Classic => EffectiveTheme.Light,
                _ => EffectiveTheme.Light
            };
        }
        catch
        {
            return EffectiveTheme.Light;
        }
    }
}

internal sealed class ResolvedThemes
{
    public EffectiveTheme ApplicationTheme { get; }
    public EffectiveTheme OverlayTheme { get; }

    public ResolvedThemes(EffectiveTheme applicationTheme, EffectiveTheme overlayTheme)
    {
        ApplicationTheme = applicationTheme;
        OverlayTheme = overlayTheme;
    }
}

internal sealed class ThemeResolver
{
    private readonly ISystemThemeSource _systemThemeSource;

    public ThemeResolver(ISystemThemeSource systemThemeSource)
    {
        _systemThemeSource = systemThemeSource;
    }

    public EffectiveTheme ResolveApplication(ApplicationThemePreference preference)
    {
        return preference switch
        {
            ApplicationThemePreference.Light => EffectiveTheme.Light,
            ApplicationThemePreference.Dark => EffectiveTheme.Dark,
            _ => _systemThemeSource.GetCurrentTheme()
        };
    }

    public EffectiveTheme ResolveOverlay(
        OverlayThemePreference overlayPreference,
        ApplicationThemePreference applicationPreference)
    {
        return overlayPreference switch
        {
            OverlayThemePreference.Light => EffectiveTheme.Light,
            OverlayThemePreference.Dark => EffectiveTheme.Dark,
            OverlayThemePreference.System => _systemThemeSource.GetCurrentTheme(),
            _ => ResolveApplication(applicationPreference)
        };
    }

    public ResolvedThemes ResolveBoth(
        ApplicationThemePreference applicationPreference,
        OverlayThemePreference overlayPreference)
    {
        EffectiveTheme? systemTheme = null;

        EffectiveTheme applicationTheme = ResolveApplicationTheme(applicationPreference, ref systemTheme);
        EffectiveTheme overlayTheme = ResolveOverlayTheme(overlayPreference, applicationTheme, ref systemTheme);

        return new ResolvedThemes(applicationTheme, overlayTheme);
    }

    private EffectiveTheme ResolveApplicationTheme(ApplicationThemePreference preference, ref EffectiveTheme? systemTheme)
    {
        return preference switch
        {
            ApplicationThemePreference.Light => EffectiveTheme.Light,
            ApplicationThemePreference.Dark => EffectiveTheme.Dark,
            _ => GetSystemTheme(ref systemTheme)
        };
    }

    private EffectiveTheme ResolveOverlayTheme(
        OverlayThemePreference overlayPreference,
        EffectiveTheme resolvedApplicationTheme,
        ref EffectiveTheme? systemTheme)
    {
        return overlayPreference switch
        {
            OverlayThemePreference.Light => EffectiveTheme.Light,
            OverlayThemePreference.Dark => EffectiveTheme.Dark,
            OverlayThemePreference.System => GetSystemTheme(ref systemTheme),
            _ => resolvedApplicationTheme
        };
    }

    private EffectiveTheme GetSystemTheme(ref EffectiveTheme? systemTheme)
    {
        if (systemTheme == null)
        {
            systemTheme = _systemThemeSource.GetCurrentTheme();
        }
        return systemTheme.Value;
    }
}
