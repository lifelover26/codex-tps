using System;
using System.Collections.Generic;
using CodexTPSCore;

namespace CodexTPSTray;

internal static class OverlayMenuDefinition
{
    public static IReadOnlyList<OverlayPositionPreset> PositionPresets { get; } =
        Enum.GetValues<OverlayPositionPreset>();

    public static IReadOnlyList<OverlayOpacityPreference> OpacityPreferences { get; } =
        Enum.GetValues<OverlayOpacityPreference>();

    public static IReadOnlyList<OverlayThemePreference> ThemePreferences { get; } =
        Enum.GetValues<OverlayThemePreference>();

    public static IReadOnlyList<OverlayCustomPositionMode> CustomPositionModes { get; } =
        Enum.GetValues<OverlayCustomPositionMode>();

    public static string GetPositionText(OverlayPositionPreset preset, Language language)
        => Localization.GetPositionPresetDisplayName(preset, language);

    public static string GetOpacityText(OverlayOpacityPreference opacity, Language language)
        => Localization.GetOverlayOpacityDisplayName(opacity, language);

    public static string GetThemeText(OverlayThemePreference theme, Language language)
        => Localization.GetOverlayThemeDisplayName(theme, language);

    public static string GetCustomPositionModeText(OverlayCustomPositionMode mode, Language language)
        => Localization.GetCustomPositionModeDisplayName(mode, language);
}
