using System;
using System.Collections.Generic;
using System.Windows;

namespace CodexTPSTray;

/// <summary>
/// Pure, WPF-free coordinator for the unified overlay position memory model.
/// A position is a complete <see cref="OverlayPositionState"/> — either a
/// quick preset or a custom normalized ratio pair, never both. The memory
/// strategy (<see cref="OverlayPositionMemoryMode"/>) decides whether that
/// state is shared across all displays or kept per physical display.
///
/// All methods are pure: they take explicit inputs and return updated settings.
/// They never fire DragCompleted and never persist directly. Callers
/// (TrayIconManager / OverlayWindow) decide when to save.
/// </summary>
public static class OverlayPositionCoordinator
{
    public const double DefaultXRatio = 1.0;
    public const double DefaultYRatio = 0.0;

    private static readonly OverlayPositionState DefaultState =
        new OverlayPositionState.Preset(OverlayPositionPreset.TopRight);

    private static IReadOnlyDictionary<string, OverlayPositionState> EmptyPositions
        => new Dictionary<string, OverlayPositionState>(0, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts an absolute window position to normalized [0,1] ratios relative
    /// to the given work area's movable range. The denominator uses max(1, range)
    /// so a window larger than the work area cannot divide by zero. Ratios are
    /// clamped to [0,1] before being returned.
    /// </summary>
    public static (double XRatio, double YRatio) ToRatios(
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        Rect workArea)
    {
        double xRange = Math.Max(1.0, workArea.Width - windowWidth);
        double yRange = Math.Max(1.0, workArea.Height - windowHeight);
        double xRatio = (windowLeft - workArea.Left) / xRange;
        double yRatio = (windowTop - workArea.Top) / yRange;
        return (Clamp01(xRatio), Clamp01(yRatio));
    }

    /// <summary>
    /// Restores an absolute window position from normalized ratios using the
    /// current work area and current window size. The movable range uses
    /// max(0, range) so a window larger than the work area pins to the origin
    /// instead of overshooting.
    /// </summary>
    public static (double Left, double Top) ToCoordinates(
        double xRatio,
        double yRatio,
        double windowWidth,
        double windowHeight,
        Rect workArea)
    {
        double xRange = Math.Max(0.0, workArea.Width - windowWidth);
        double yRange = Math.Max(0.0, workArea.Height - windowHeight);
        double left = workArea.Left + Clamp01(xRatio) * xRange;
        double top = workArea.Top + Clamp01(yRatio) * yRange;
        return (left, top);
    }

    /// <summary>
    /// Computes the absolute position for a complete state against a work area.
    /// Preset states use <see cref="OverlayPositionCalculator.CalculatePresetPosition"/>
    /// with the given physical margin; custom states use <see cref="ToCoordinates"/>.
    /// </summary>
    public static (double Left, double Top) ToCoordinates(
        OverlayPositionState state,
        double windowWidth,
        double windowHeight,
        Rect workArea,
        Thickness physicalMargin)
    {
        if (state is OverlayPositionState.Preset preset)
        {
            return OverlayPositionCalculator.CalculatePresetPosition(
                preset.Value,
                new System.Windows.Size(windowWidth, windowHeight),
                workArea,
                physicalMargin);
        }

        if (state is OverlayPositionState.Custom custom)
        {
            return ToCoordinates(custom.XRatio, custom.YRatio, windowWidth, windowHeight, workArea);
        }

        return OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopRight,
            new System.Windows.Size(windowWidth, windowHeight),
            workArea,
            physicalMargin);
    }

    /// <summary>
    /// Returns the preset currently active for the given monitor, or null when
    /// the effective state is custom. Used to set menu checkmarks: a custom
    /// position checks no preset item.
    /// </summary>
    public static OverlayPositionPreset? GetActivePreset(
        TraySettings settings,
        MonitorInfo monitor)
    {
        OverlayPositionState state = ResolveEffectiveState(settings, monitor, out _);
        return state is OverlayPositionState.Preset preset ? preset.Value : null;
    }

    /// <summary>
    /// Resolves the effective position state for a monitor without mutating
    /// settings. In shared mode this is <see cref="TraySettings.SharedPosition"/>.
    /// In per-display mode it is the monitor's stored record if present,
    /// otherwise the inheritance source (<see cref="TraySettings.SharedPosition"/>
    /// or the default). The <paramref name="inherited"/> out parameter is true
    /// when the monitor had no stored record and would inherit on restore.
    /// </summary>
    public static OverlayPositionState ResolveEffectiveState(
        TraySettings settings,
        MonitorInfo monitor,
        out bool inherited)
    {
        inherited = false;

        if (settings.PositionMemoryMode == OverlayPositionMemoryMode.SharedAcrossDisplays)
        {
            return settings.SharedPosition ?? DefaultState;
        }

        if (TryResolvePendingPreset(settings, monitor, out var pendingPreset))
        {
            return pendingPreset;
        }

        string monitorId = MonitorId.Resolve(monitor);
        if (TryFind(settings.PerDisplayPositions, monitorId, out var existing))
        {
            return existing;
        }

        if (TryFindByDeviceName(
                settings.PerDisplayPositions,
                monitor,
                monitorId,
                out var legacy,
                out _))
        {
            return legacy;
        }

        inherited = true;
        return settings.SharedPosition ?? DefaultState;
    }

    /// <summary>
    /// Migrates legacy absolute OverlayLeft/OverlayTop coordinates into a
    /// shared Custom state. The owning monitor is resolved from the legacy
    /// coordinates and the ratios are computed against its work area. The
    /// legacy fields are cleared so they can never participate in later
    /// DPI or display-change calculations. This runs once at first restore
    /// when no SharedPosition has been established yet.
    /// </summary>
    public static TraySettings MigrateLegacy(
        TraySettings settings,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor)
    {
        if (!HasLegacyCoordinates(settings))
            return settings;

        if (settings.SharedPosition != null)
        {
            return settings with { OverlayLeft = null, OverlayTop = null };
        }

        double legacyLeft = settings.OverlayLeft!.Value;
        double legacyTop = settings.OverlayTop!.Value;

        MonitorInfo owner = OverlayPositionCalculator.FindBestMonitor(
            legacyLeft, legacyTop, windowWidth, windowHeight, monitors, primaryMonitor);

        var (xRatio, yRatio) = ToRatios(legacyLeft, legacyTop, windowWidth, windowHeight, owner.WorkingArea);

        return settings with
        {
            OverlayLeft = null,
            OverlayTop = null,
            SharedPosition = new OverlayPositionState.Custom(xRatio, yRatio),
            OverlayTargetMonitorId = MonitorId.Resolve(owner)
        };
    }

    /// <summary>
    /// Resolves the restore position (startup, DPI change, display topology
    /// change). Never fires DragCompleted. Returns the target physical
    /// coordinates and, if state changed (legacy migration or a first-seen
    /// display inheriting its own record), the updated settings to persist.
    /// When the state is unchanged, UpdatedSettings is null so callers can
    /// skip a redundant save.
    ///
    /// The target monitor is resolved from OverlayTargetMonitorId. If that
    /// monitor still exists, it is used. If it does not (display disconnected),
    /// the current window's monitor is used as a temporary fallback — the
    /// fallback never overwrites OverlayTargetMonitorId so the original target
    /// is restored when it reappears.
    /// </summary>
    public static (double Left, double Top, TraySettings? UpdatedSettings) ResolveRestorePosition(
        TraySettings settings,
        double currentLeft,
        double currentTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor,
        Thickness dipMargin)
    {
        TraySettings effective = settings;

        if (HasLegacyCoordinates(effective) && effective.SharedPosition == null)
        {
            effective = MigrateLegacy(effective, windowWidth, windowHeight, monitors, primaryMonitor);
        }

        MonitorInfo currentMonitor = OverlayPositionCalculator.FindBestMonitor(
            currentLeft, currentTop, windowWidth, windowHeight, monitors, primaryMonitor);

        MonitorInfo targetMonitor = ResolveTargetMonitor(effective, currentMonitor, monitors);
        bool hasPendingPreset = effective.PositionMemoryMode == OverlayPositionMemoryMode.RememberPerDisplay
            && effective.SharedPosition is OverlayPositionState.Preset
            && !string.IsNullOrWhiteSpace(effective.PendingPresetMigrationTarget);

        // v0.4.0 could persist a preset separately from per-display custom
        // ratios. When that file is first opened, the preset must win for its
        // original display, but only after a live monitor identity matches the
        // deferred target. This prevents writing the preset to an unrelated
        // display while the native identity is temporarily unavailable.
        if (TryResolvePendingPresetTarget(effective, monitors, out var pendingTarget)
            && effective.SharedPosition is OverlayPositionState.Preset pendingPreset)
        {
            targetMonitor = pendingTarget;
            effective = effective with
            {
                PerDisplayPositions = WithPendingPresetRecord(
                    effective.PerDisplayPositions,
                    effective.PendingPresetMigrationTarget!,
                    pendingTarget,
                    MonitorId.Resolve(pendingTarget),
                    pendingPreset),
                OverlayTargetMonitorId = MonitorId.Resolve(pendingTarget),
                PendingPresetMigrationTarget = null
            };
        }

        // A legacy DeviceName can be ambiguous when Windows reports two
        // logical displays with the same name. Keep the pending migration
        // untouched and resolve the current context read-only until a stable
        // identity (or a unique DeviceName) is available.
        if (hasPendingPreset && effective.PendingPresetMigrationTarget != null)
        {
            OverlayPositionState state = ResolveEffectiveState(
                effective,
                targetMonitor,
                out _);
            Thickness pendingMargin = ConvertDipMarginToPhysical(
                dipMargin,
                targetMonitor.DpiX,
                targetMonitor.DpiY);
            var (pendingLeft, pendingTop) = ToCoordinates(
                state,
                windowWidth,
                windowHeight,
                targetMonitor.WorkingArea,
                pendingMargin);
            return (pendingLeft, pendingTop, null);
        }

        Thickness physicalMargin = ConvertDipMarginToPhysical(dipMargin, targetMonitor.DpiX, targetMonitor.DpiY);

        if (effective.PositionMemoryMode == OverlayPositionMemoryMode.SharedAcrossDisplays)
        {
            return ResolveShared(effective, windowWidth, windowHeight, targetMonitor, physicalMargin, settings);
        }

        return ResolvePerDisplay(
            effective,
            windowWidth,
            windowHeight,
            targetMonitor,
            monitors,
            physicalMargin,
            settings);
    }

    /// <summary>
    /// Saves a custom position from a user drag end. The landed monitor is
    /// resolved from the final window rect. SharedPosition becomes the latest
    /// Custom inheritance seed in both modes; in per-display mode the landed
    /// monitor's record is also overwritten with Custom (covering any prior
    /// preset). Legacy absolute coordinates and deferred preset migration are
    /// cleared. OverlayTargetMonitorId is set to the landed monitor so later
    /// restores target the display the user chose.
    /// </summary>
    public static TraySettings SaveFromDragEnd(
        TraySettings settings,
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor)
    {
        MonitorInfo landed = OverlayPositionCalculator.FindBestMonitor(
            windowLeft, windowTop, windowWidth, windowHeight, monitors, primaryMonitor);

        var (xRatio, yRatio) = ToRatios(windowLeft, windowTop, windowWidth, windowHeight, landed.WorkingArea);
        string landedId = MonitorId.Resolve(landed);
        var customState = new OverlayPositionState.Custom(xRatio, yRatio);

        if (settings.PositionMemoryMode == OverlayPositionMemoryMode.RememberPerDisplay)
        {
            var perDisplay = WithRecord(settings.PerDisplayPositions, landedId, customState);
            return settings with
            {
                OverlayLeft = null,
                OverlayTop = null,
                SharedPosition = customState,
                PerDisplayPositions = perDisplay,
                OverlayTargetMonitorId = landedId,
                PendingPresetMigrationTarget = null
            };
        }

        return settings with
        {
            OverlayLeft = null,
            OverlayTop = null,
            SharedPosition = customState,
            OverlayTargetMonitorId = landedId,
            PendingPresetMigrationTarget = null
        };
    }

    /// <summary>
    /// Saves a preset selection from the user. SharedPosition becomes the
    /// latest Preset inheritance seed in both modes; in per-display mode the
    /// current monitor's record is also overwritten with Preset (covering any
    /// prior custom). Legacy absolute coordinates and deferred preset
    /// migration are cleared. OverlayTargetMonitorId is set to the current
    /// monitor so later restores target the display the user chose.
    /// </summary>
    public static TraySettings SaveFromPresetSelection(
        TraySettings settings,
        OverlayPositionPreset preset,
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor)
    {
        MonitorInfo current = OverlayPositionCalculator.FindBestMonitor(
            windowLeft, windowTop, windowWidth, windowHeight, monitors, primaryMonitor);
        string currentId = MonitorId.Resolve(current);
        var presetState = new OverlayPositionState.Preset(preset);

        if (settings.PositionMemoryMode == OverlayPositionMemoryMode.RememberPerDisplay)
        {
            var perDisplay = WithRecord(settings.PerDisplayPositions, currentId, presetState);
            return settings with
            {
                OverlayLeft = null,
                OverlayTop = null,
                SharedPosition = presetState,
                PerDisplayPositions = perDisplay,
                OverlayTargetMonitorId = currentId,
                PendingPresetMigrationTarget = null
            };
        }

        return settings with
        {
            OverlayLeft = null,
            OverlayTop = null,
            SharedPosition = presetState,
            OverlayTargetMonitorId = currentId,
            PendingPresetMigrationTarget = null
        };
    }

    /// <summary>
    /// Switches the position memory mode using the current window's physical
    /// position and monitor layout. The window does not jump because the
    /// current display's effective state is preserved.
    ///
    /// - Shared -> RememberPerDisplay: writes the current monitor's record
    ///   from the current effective state and sets OverlayTargetMonitorId.
    ///   Other per-display records are preserved.
    /// - RememberPerDisplay -> Shared: sets SharedPosition to the current
    ///   display's effective state. Per-display records are preserved so
    ///   switching back resumes them.
    /// - Same mode: true no-op.
    /// </summary>
    public static TraySettings SwitchMemoryMode(
        TraySettings settings,
        OverlayPositionMemoryMode mode,
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor)
    {
        if (settings.PositionMemoryMode == mode)
            return settings;

        MonitorInfo current = OverlayPositionCalculator.FindBestMonitor(
            windowLeft, windowTop, windowWidth, windowHeight, monitors, primaryMonitor);
        string currentId = MonitorId.Resolve(current);

        OverlayPositionState currentState = ResolveEffectiveState(settings, current, out _);

        if (mode == OverlayPositionMemoryMode.RememberPerDisplay)
        {
            var perDisplay = WithRecord(settings.PerDisplayPositions, currentId, currentState);
            return settings with
            {
                PositionMemoryMode = mode,
                PerDisplayPositions = perDisplay,
                OverlayTargetMonitorId = currentId
            };
        }

        return settings with
        {
            PositionMemoryMode = mode,
            SharedPosition = currentState,
            OverlayTargetMonitorId = currentId
        };
    }

    /// <summary>
    /// Applies a mode switch without window context. The current display's
    /// effective state cannot be computed, so SharedPosition is used as the
    /// inheritance source. Use <see cref="SwitchMemoryMode"/> when the live
    /// window rect is available so the window does not jump.
    /// </summary>
    public static TraySettings ApplyMemoryModeSwitch(
        TraySettings settings,
        OverlayPositionMemoryMode mode)
    {
        if (settings.PositionMemoryMode == mode)
            return settings;

        return settings with { PositionMemoryMode = mode };
    }

    public static bool HasLegacyCoordinates(TraySettings settings)
        => settings.OverlayLeft.HasValue && settings.OverlayTop.HasValue;

    /// <summary>
    /// Resolves the target monitor from OverlayTargetMonitorId. If the stored
    /// id matches a monitor in the list, that monitor is returned. Otherwise
    /// the fallback (current window monitor or primary) is returned. The
    /// caller must never write the fallback id back into settings.
    /// </summary>
    private static MonitorInfo ResolveTargetMonitor(
        TraySettings settings,
        MonitorInfo currentMonitor,
        IReadOnlyList<MonitorInfo> monitors)
    {
        string? targetId = settings.OverlayTargetMonitorId;
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            foreach (MonitorInfo m in monitors)
            {
                if (!string.IsNullOrWhiteSpace(m.StableId)
                    && MonitorId.Equals(m.StableId, targetId))
                    return m;
            }

            MonitorInfo? uniqueDeviceMatch = null;
            foreach (MonitorInfo m in monitors)
            {
                if (!MonitorId.Equals(m.DeviceName, targetId))
                    continue;

                if (uniqueDeviceMatch != null)
                {
                    uniqueDeviceMatch = null;
                    break;
                }

                uniqueDeviceMatch = m;
            }

            if (uniqueDeviceMatch != null)
                return uniqueDeviceMatch;
        }

        return currentMonitor;
    }

    private static (double Left, double Top, TraySettings? UpdatedSettings) ResolveShared(
        TraySettings effective,
        double windowWidth,
        double windowHeight,
        MonitorInfo targetMonitor,
        Thickness physicalMargin,
        TraySettings original)
    {
        OverlayPositionState state = effective.SharedPosition ?? DefaultState;
        var (left, top) = ToCoordinates(state, windowWidth, windowHeight, targetMonitor.WorkingArea, physicalMargin);
        TraySettings? updated = effective == original ? null : effective;
        return (left, top, updated);
    }

    private static (double Left, double Top, TraySettings? UpdatedSettings) ResolvePerDisplay(
        TraySettings effective,
        double windowWidth,
        double windowHeight,
        MonitorInfo targetMonitor,
        IReadOnlyList<MonitorInfo> monitors,
        Thickness physicalMargin,
        TraySettings original)
    {
        string monitorId = MonitorId.Resolve(targetMonitor);
        var perDisplay = effective.PerDisplayPositions;

        if (perDisplay != null && TryFind(perDisplay, monitorId, out var existing))
        {
            var (left, top) = ToCoordinates(existing, windowWidth, windowHeight, targetMonitor.WorkingArea, physicalMargin);
            TraySettings? updated = effective == original ? null : effective;
            return (left, top, updated);
        }

        if (perDisplay != null
            && !HasAmbiguousDeviceName(targetMonitor, monitors)
            && TryFindByDeviceName(
                perDisplay,
                targetMonitor,
                monitorId,
                out var legacy,
                out string legacyKey))
        {
            var (left, top) = ToCoordinates(legacy, windowWidth, windowHeight, targetMonitor.WorkingArea, physicalMargin);
            var migrated = WithMigratedDeviceNameRecord(
                perDisplay,
                legacyKey,
                monitorId,
                legacy);
            TraySettings withRecord = effective with { PerDisplayPositions = migrated };
            TraySettings? updated = withRecord == original ? null : withRecord;
            return (left, top, updated);
        }

        // A legacy DeviceName key cannot be assigned to one StableId when the
        // live topology exposes the same GDI name more than once. Preserve the
        // old record read-only until the name becomes unique instead of writing
        // an inheritance state to an arbitrary physical display.
        if (perDisplay != null
            && HasAmbiguousDeviceName(targetMonitor, monitors)
            && TryFind(perDisplay, targetMonitor.DeviceName, out var ambiguousLegacy))
        {
            var (left, top) = ToCoordinates(
                ambiguousLegacy,
                windowWidth,
                windowHeight,
                targetMonitor.WorkingArea,
                physicalMargin);
            TraySettings? updated = effective == original ? null : effective;
            return (left, top, updated);
        }

        OverlayPositionState inherited = effective.SharedPosition ?? DefaultState;
        var (newLeft, newTop) = ToCoordinates(inherited, windowWidth, windowHeight, targetMonitor.WorkingArea, physicalMargin);

        var withInherited = WithRecord(perDisplay, monitorId, inherited);
        TraySettings updatedSettings = effective with { PerDisplayPositions = withInherited };
        TraySettings? result = updatedSettings == original ? null : updatedSettings;
        return (newLeft, newTop, result);
    }

    /// <summary>
    /// When the stable id has no record but the GDI source name (DeviceName)
    /// has a legacy record, returns that record so it can be copied to the
    /// stable id key. This handles the upgrade path from DeviceName-keyed
    /// records to StableId-keyed records without losing the user's saved
    /// position. Returns false when there is nothing to migrate.
    /// </summary>
    private static bool TryFindByDeviceName(
        IReadOnlyDictionary<string, OverlayPositionState>? perDisplay,
        MonitorInfo currentMonitor,
        string stableKey,
        out OverlayPositionState position,
        out string legacyKey)
    {
        position = null!;
        legacyKey = string.Empty;
        if (perDisplay == null || string.IsNullOrWhiteSpace(currentMonitor.StableId))
            return false;

        string deviceName = currentMonitor.DeviceName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceName))
            return false;

        if (MonitorId.Equals(deviceName, stableKey))
            return false;

        if (!TryFind(perDisplay, deviceName, out position))
            return false;

        foreach (string key in perDisplay.Keys)
        {
            if (MonitorId.Equals(key, deviceName))
            {
                legacyKey = key;
                break;
            }
        }

        return !string.IsNullOrWhiteSpace(legacyKey);
    }

    private static bool HasAmbiguousDeviceName(
        MonitorInfo targetMonitor,
        IReadOnlyList<MonitorInfo> monitors)
    {
        if (string.IsNullOrWhiteSpace(targetMonitor.StableId)
            || string.IsNullOrWhiteSpace(targetMonitor.DeviceName))
        {
            return false;
        }

        int matches = 0;
        foreach (MonitorInfo monitor in monitors)
        {
            if (!MonitorId.Equals(monitor.DeviceName, targetMonitor.DeviceName))
                continue;

            matches++;
            if (matches > 1)
                return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, OverlayPositionState> WithMigratedDeviceNameRecord(
        IReadOnlyDictionary<string, OverlayPositionState> existing,
        string legacyKey,
        string stableKey,
        OverlayPositionState state)
    {
        var result = new Dictionary<string, OverlayPositionState>(
            existing,
            StringComparer.OrdinalIgnoreCase);
        result.Remove(legacyKey);
        result[stableKey] = state;
        return result;
    }

    private static IReadOnlyDictionary<string, OverlayPositionState> WithPendingPresetRecord(
        IReadOnlyDictionary<string, OverlayPositionState>? existing,
        string pendingKey,
        MonitorInfo targetMonitor,
        string stableKey,
        OverlayPositionState state)
    {
        var result = new Dictionary<string, OverlayPositionState>(
            existing ?? EmptyPositions,
            StringComparer.OrdinalIgnoreCase);
        if (MonitorId.Equals(pendingKey, targetMonitor.DeviceName)
            && !MonitorId.Equals(pendingKey, stableKey))
        {
            result.Remove(pendingKey);
        }

        result[stableKey] = state;
        return result;
    }

    private static bool TryResolvePendingPreset(
        TraySettings settings,
        MonitorInfo monitor,
        out OverlayPositionState.Preset preset)
    {
        preset = null!;
        if (settings.PositionMemoryMode != OverlayPositionMemoryMode.RememberPerDisplay
            || settings.SharedPosition is not OverlayPositionState.Preset sharedPreset
            || string.IsNullOrWhiteSpace(settings.PendingPresetMigrationTarget))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(monitor.StableId)
            || !MonitorId.Equals(monitor.StableId, settings.PendingPresetMigrationTarget))
        {
            return false;
        }

        preset = sharedPreset;
        return true;
    }

    private static bool TryResolvePendingPresetTarget(
        TraySettings settings,
        IReadOnlyList<MonitorInfo> monitors,
        out MonitorInfo target)
    {
        target = null!;
        if (settings.PositionMemoryMode != OverlayPositionMemoryMode.RememberPerDisplay
            || settings.SharedPosition is not OverlayPositionState.Preset
            || string.IsNullOrWhiteSpace(settings.PendingPresetMigrationTarget))
        {
            return false;
        }

        foreach (MonitorInfo monitor in monitors)
        {
            if (!string.IsNullOrWhiteSpace(monitor.StableId)
                && MonitorId.Equals(monitor.StableId, settings.PendingPresetMigrationTarget))
            {
                target = monitor;
                return true;
            }
        }

        MonitorInfo? uniqueDeviceMatch = null;
        foreach (MonitorInfo monitor in monitors)
        {
            if (!MonitorId.Equals(monitor.DeviceName, settings.PendingPresetMigrationTarget))
                continue;

            if (uniqueDeviceMatch != null)
                return false;

            uniqueDeviceMatch = monitor;
        }

        if (uniqueDeviceMatch != null)
        {
            target = uniqueDeviceMatch;
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, OverlayPositionState> WithRecord(
        IReadOnlyDictionary<string, OverlayPositionState>? existing,
        string key,
        OverlayPositionState state)
    {
        var result = new Dictionary<string, OverlayPositionState>(
            existing ?? EmptyPositions,
            StringComparer.OrdinalIgnoreCase)
        {
            [key] = state
        };
        return result;
    }

    private static bool TryFind(
        IReadOnlyDictionary<string, OverlayPositionState>? perDisplay,
        string monitorId,
        out OverlayPositionState position)
    {
        position = null!;
        if (perDisplay == null)
            return false;

        foreach (var kvp in perDisplay)
        {
            if (MonitorId.Equals(kvp.Key, monitorId))
            {
                position = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.0;
        if (value < 0.0)
            return 0.0;
        if (value > 1.0)
            return 1.0;
        return value;
    }

    private static Thickness ConvertDipMarginToPhysical(Thickness dip, double dpiX, double dpiY)
    {
        double scaleX = dpiX / 96.0;
        double scaleY = dpiY / 96.0;
        return new Thickness(
            dip.Left * scaleX,
            dip.Top * scaleY,
            dip.Right * scaleX,
            dip.Bottom * scaleY);
    }
}
