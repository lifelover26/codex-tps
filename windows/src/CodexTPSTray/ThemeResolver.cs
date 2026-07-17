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
}
