using System;
using System.Windows.Forms;

namespace CodexTPSTray;

internal interface IWinFormsThemeApplier
{
    bool TryApply(EffectiveTheme theme);
}

internal sealed class WindowsFormsThemeApplier : IWinFormsThemeApplier
{
    private readonly Action<SystemColorMode> _setColorMode;

    public WindowsFormsThemeApplier() : this(Application.SetColorMode)
    {
    }

    internal WindowsFormsThemeApplier(Action<SystemColorMode> setColorMode)
    {
        _setColorMode = setColorMode ?? throw new ArgumentNullException(nameof(setColorMode));
    }

    public bool TryApply(EffectiveTheme theme)
    {
        var colorMode = theme switch
        {
            EffectiveTheme.Dark => SystemColorMode.Dark,
            _ => SystemColorMode.Classic
        };

        try
        {
            _setColorMode(colorMode);
            return true;
        }
        catch
        {
            return false;
        }
    }
}