namespace CodexTPSTray;

/// <summary>
/// How the overlay's saved appearance (theme + opacity) is shared across
/// displays: one state for every display, or one full state per physical
/// display. This is independent of <see cref="OverlayPositionMemoryMode"/>
/// so appearance and position memory can use different strategies at the same
/// time. The JSON field name and values are distinct from the position enum
/// to keep the two persisted authorities from colliding.
/// </summary>
public enum OverlayAppearanceMemoryMode
{
    SharedAcrossDisplays,
    RememberPerDisplay
}

/// <summary>
/// A complete, immutable overlay appearance: the user's theme preference and
/// opacity preference. The preferences (e.g. FollowApplication, Follow Windows)
/// are stored as-is — never the resolved EffectiveTheme — so a "Follow Windows"
/// selection survives a system theme change and is not written back as Light or
/// Dark. Both fields are always present; the record is the unit of per-display
/// memory, so modifying one field never drops the other.
/// </summary>
public sealed record OverlayAppearanceState(
    OverlayThemePreference ThemePreference,
    OverlayOpacityPreference OpacityPreference
);
