using System;
using System.Collections.Generic;

namespace CodexTPSTray;

/// <summary>
/// Pure, WPF-free coordinator for the overlay appearance memory model.
/// An appearance is a complete <see cref="OverlayAppearanceState"/> — theme
/// preference plus opacity preference, always both. The memory strategy
/// (<see cref="OverlayAppearanceMemoryMode"/>) decides whether that state is
/// shared across all displays or kept per physical display.
///
/// All methods are pure: they take explicit inputs and return updated settings.
/// They never persist directly. Callers (TrayIconManager) decide when to save.
///
/// The <see cref="TraySettings.OverlayTheme"/> and
/// <see cref="TraySettings.OverlayOpacity"/> scalar fields are kept in sync
/// with the effective appearance for the current monitor so the existing theme
/// application pipeline and downgrade compatibility continue to work.
/// </summary>
public static class OverlayAppearanceCoordinator
{
    private static readonly OverlayAppearanceState DefaultAppearance =
        new(OverlayThemePreference.FollowApplication, OverlayOpacityPreference.Default);

    private static IReadOnlyDictionary<string, OverlayAppearanceState> EmptyAppearances
        => new Dictionary<string, OverlayAppearanceState>(0, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the effective appearance state for a monitor without mutating
    /// settings. In shared mode this is <see cref="TraySettings.SharedAppearance"/>.
    /// In per-display mode it is the monitor's stored record if present,
    /// otherwise the inheritance source (<see cref="TraySettings.SharedAppearance"/>
    /// or the default). The <paramref name="inherited"/> out parameter is true
    /// when the monitor had no stored record and would inherit on restore.
    /// </summary>
    public static OverlayAppearanceState ResolveEffectiveAppearance(
        TraySettings settings,
        MonitorInfo monitor,
        out bool inherited)
    {
        inherited = false;

        if (settings.AppearanceMemoryMode == OverlayAppearanceMemoryMode.SharedAcrossDisplays)
        {
            return settings.SharedAppearance ?? DefaultAppearance;
        }

        string monitorId = MonitorId.Resolve(monitor);
        if (TryFind(settings.PerDisplayAppearances, monitorId, out var existing))
        {
            return existing;
        }

        inherited = true;
        return settings.SharedAppearance ?? DefaultAppearance;
    }

    /// <summary>
    /// Returns the theme preference currently active for the given monitor.
    /// Used to set menu checkmarks.
    /// </summary>
    public static OverlayThemePreference GetActiveThemePreference(
        TraySettings settings,
        MonitorInfo monitor)
        => ResolveEffectiveAppearance(settings, monitor, out _).ThemePreference;

    /// <summary>
    /// Returns the opacity preference currently active for the given monitor.
    /// Used to set menu checkmarks.
    /// </summary>
    public static OverlayOpacityPreference GetActiveOpacityPreference(
        TraySettings settings,
        MonitorInfo monitor)
        => ResolveEffectiveAppearance(settings, monitor, out _).OpacityPreference;

    /// <summary>
    /// Saves a theme selection from the user. The current monitor's opacity is
    /// preserved. <see cref="TraySettings.SharedAppearance"/> becomes the latest
    /// inheritance seed in both modes; in per-display mode the current monitor's
    /// record is also overwritten. The scalar OverlayTheme/OverlayOpacity fields
    /// are synced to the effective appearance.
    /// </summary>
    public static TraySettings SaveFromThemeSelection(
        TraySettings settings,
        OverlayThemePreference preference,
        MonitorInfo currentMonitor)
    {
        OverlayAppearanceState current = ResolveEffectiveAppearance(settings, currentMonitor, out _);
        var newState = current with { ThemePreference = preference };
        return ApplyAppearanceChange(settings, newState, currentMonitor);
    }

    /// <summary>
    /// Saves an opacity selection from the user. The current monitor's theme is
    /// preserved. <see cref="TraySettings.SharedAppearance"/> becomes the latest
    /// inheritance seed in both modes; in per-display mode the current monitor's
    /// record is also overwritten. The scalar OverlayTheme/OverlayOpacity fields
    /// are synced to the effective appearance.
    /// </summary>
    public static TraySettings SaveFromOpacitySelection(
        TraySettings settings,
        OverlayOpacityPreference preference,
        MonitorInfo currentMonitor)
    {
        OverlayAppearanceState current = ResolveEffectiveAppearance(settings, currentMonitor, out _);
        var newState = current with { OpacityPreference = preference };
        return ApplyAppearanceChange(settings, newState, currentMonitor);
    }

    /// <summary>
    /// Switches the appearance memory mode. The window's visual appearance does
    /// not jump because the current display's effective state is preserved.
    ///
    /// - Shared -> RememberPerDisplay: writes the current monitor's record from
    ///   the current effective state. Other per-display records are preserved.
    ///   SharedAppearance is left unchanged as the inheritance seed.
    /// - RememberPerDisplay -> Shared: sets SharedAppearance to the current
    ///   display's effective state. Per-display records are preserved so
    ///   switching back resumes them.
    /// - Same mode: true no-op.
    /// </summary>
    public static TraySettings SwitchMemoryMode(
        TraySettings settings,
        OverlayAppearanceMemoryMode mode,
        MonitorInfo currentMonitor)
    {
        if (settings.AppearanceMemoryMode == mode)
            return settings;

        OverlayAppearanceState current = ResolveEffectiveAppearance(settings, currentMonitor, out _);
        string monitorId = MonitorId.Resolve(currentMonitor);

        if (mode == OverlayAppearanceMemoryMode.RememberPerDisplay)
        {
            var perDisplay = WithRecord(settings.PerDisplayAppearances, monitorId, current);
            return settings with
            {
                AppearanceMemoryMode = mode,
                PerDisplayAppearances = perDisplay,
                OverlayTheme = current.ThemePreference,
                OverlayOpacity = current.OpacityPreference
            };
        }

        return settings with
        {
            AppearanceMemoryMode = mode,
            SharedAppearance = current,
            OverlayTheme = current.ThemePreference,
            OverlayOpacity = current.OpacityPreference
        };
    }

    /// <summary>
    /// Applies a mode switch without monitor context. The current display's
    /// effective state cannot be computed, so SharedAppearance is used as the
    /// inheritance source. Use <see cref="SwitchMemoryMode"/> when the live
    /// monitor is available so the window does not jump.
    /// </summary>
    public static TraySettings ApplyMemoryModeSwitch(
        TraySettings settings,
        OverlayAppearanceMemoryMode mode)
    {
        if (settings.AppearanceMemoryMode == mode)
            return settings;

        return settings with { AppearanceMemoryMode = mode };
    }

    /// <summary>
    /// Restores the effective appearance for a target monitor, syncing the
    /// scalar OverlayTheme/OverlayOpacity fields. In per-display mode, a
    /// first-seen monitor inherits from SharedAppearance and gets its own
    /// record so future restores are stable. An existing record is never
    /// overwritten by the shared state. Returns updated settings; the caller
    /// compares with the original to skip a redundant save.
    /// </summary>
    public static TraySettings RestoreForMonitor(
        TraySettings settings,
        MonitorInfo targetMonitor)
    {
        OverlayAppearanceState appearance = ResolveEffectiveAppearance(
            settings,
            targetMonitor,
            out bool inherited);

        if (inherited
            && settings.AppearanceMemoryMode == OverlayAppearanceMemoryMode.RememberPerDisplay)
        {
            string monitorId = MonitorId.Resolve(targetMonitor);
            var perDisplay = WithRecord(settings.PerDisplayAppearances, monitorId, appearance);
            return settings with
            {
                PerDisplayAppearances = perDisplay,
                OverlayTheme = appearance.ThemePreference,
                OverlayOpacity = appearance.OpacityPreference
            };
        }

        return settings with
        {
            OverlayTheme = appearance.ThemePreference,
            OverlayOpacity = appearance.OpacityPreference
        };
    }

    private static TraySettings ApplyAppearanceChange(
        TraySettings settings,
        OverlayAppearanceState newState,
        MonitorInfo currentMonitor)
    {
        if (settings.AppearanceMemoryMode == OverlayAppearanceMemoryMode.RememberPerDisplay)
        {
            string monitorId = MonitorId.Resolve(currentMonitor);
            var perDisplay = WithRecord(settings.PerDisplayAppearances, monitorId, newState);
            return settings with
            {
                SharedAppearance = newState,
                PerDisplayAppearances = perDisplay,
                OverlayTheme = newState.ThemePreference,
                OverlayOpacity = newState.OpacityPreference
            };
        }

        return settings with
        {
            SharedAppearance = newState,
            OverlayTheme = newState.ThemePreference,
            OverlayOpacity = newState.OpacityPreference
        };
    }

    private static IReadOnlyDictionary<string, OverlayAppearanceState> WithRecord(
        IReadOnlyDictionary<string, OverlayAppearanceState>? existing,
        string key,
        OverlayAppearanceState state)
    {
        var result = new Dictionary<string, OverlayAppearanceState>(
            existing ?? EmptyAppearances,
            StringComparer.OrdinalIgnoreCase)
        {
            [key] = state
        };
        return result;
    }

    private static bool TryFind(
        IReadOnlyDictionary<string, OverlayAppearanceState>? perDisplay,
        string monitorId,
        out OverlayAppearanceState appearance)
    {
        appearance = null!;
        if (perDisplay == null)
            return false;

        foreach (var kvp in perDisplay)
        {
            if (MonitorId.Equals(kvp.Key, monitorId))
            {
                appearance = kvp.Value;
                return true;
            }
        }

        return false;
    }
}
