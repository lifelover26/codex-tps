using System;

namespace CodexTPSTray;

internal static class OverlayOpacityCalculator
{
    private static readonly System.Windows.Media.Color LightBackgroundRgb = System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly System.Windows.Media.Color DarkBackgroundRgb = System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C);

    public const byte LightDefaultAlpha = 0xCC;
    public const byte DarkDefaultAlpha = 0x73;

    public static byte ResolveAlpha(OverlayOpacityPreference preference, EffectiveTheme theme)
    {
        if (preference == OverlayOpacityPreference.Default)
        {
            return theme == EffectiveTheme.Dark ? DarkDefaultAlpha : LightDefaultAlpha;
        }

        return preference switch
        {
            OverlayOpacityPreference.Percent40 => (byte)Math.Round(255 * 0.40),
            OverlayOpacityPreference.Percent55 => (byte)Math.Round(255 * 0.55),
            OverlayOpacityPreference.Percent70 => (byte)Math.Round(255 * 0.70),
            OverlayOpacityPreference.Percent85 => (byte)Math.Round(255 * 0.85),
            OverlayOpacityPreference.Opaque => 255,
            _ => theme == EffectiveTheme.Dark ? DarkDefaultAlpha : LightDefaultAlpha
        };
    }

    public static System.Windows.Media.Color GetBackgroundRgb(EffectiveTheme theme)
    {
        return theme == EffectiveTheme.Dark ? DarkBackgroundRgb : LightBackgroundRgb;
    }

    public static System.Windows.Media.Color CreateBackgroundColor(OverlayOpacityPreference preference, EffectiveTheme theme)
    {
        System.Windows.Media.Color rgb = GetBackgroundRgb(theme);
        byte alpha = ResolveAlpha(preference, theme);
        return System.Windows.Media.Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
    }
}