using System;
using System.Collections.Generic;
using System.Windows;

namespace CodexTPSTray;

/// <summary>
/// Pure, WPF-free coordinator for the custom overlay position model. Custom
/// positions are stored as normalized ratios relative to a monitor's work area
/// movable range, never as absolute physical pixels. Two modes are supported:
///
/// - KeepRelative: a single shared (XRatio, YRatio) applied to whatever
///   monitor the overlay currently occupies.
/// - RememberPerDisplay: a MonitorId -> (XRatio, YRatio) map. Each display
///   keeps its own record; a first-seen display with no record inherits the
///   current relative position and forms its own record.
///
/// All methods are pure: they take explicit inputs and return updated settings.
/// They never fire DragCompleted and never persist directly. Callers
/// (TrayIconManager / OverlayWindow) decide when to save.
/// </summary>
public static class OverlayCustomPositionCoordinator
{
    public const double DefaultXRatio = 1.0;
    public const double DefaultYRatio = 0.0;

    public static bool HasLegacyCoordinates(TraySettings settings)
        => settings.OverlayLeft.HasValue && settings.OverlayTop.HasValue;

    public static bool HasKeepRelativeRatios(TraySettings settings)
        => settings.OverlayXRatio.HasValue && settings.OverlayYRatio.HasValue;

    public static bool HasPerDisplayRecords(TraySettings settings)
        => settings.OverlayPerDisplayPositions != null && settings.OverlayPerDisplayPositions.Count > 0;

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
    /// Migrates legacy absolute OverlayLeft/OverlayTop coordinates into the
    /// normalized KeepRelative model. The owning monitor is resolved from the
    /// legacy coordinates and the ratios are computed against its work area.
    /// The legacy fields are cleared so they can never participate in later
    /// DPI or display-change calculations. The mode defaults to KeepRelative.
    /// OverlayCustomMonitorId is set to the owning monitor so later restores
    /// target the same display.
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

        double legacyLeft = settings.OverlayLeft!.Value;
        double legacyTop = settings.OverlayTop!.Value;

        MonitorInfo owner = OverlayPositionCalculator.FindBestMonitor(
            legacyLeft, legacyTop, windowWidth, windowHeight, monitors, primaryMonitor);

        var (xRatio, yRatio) = ToRatios(legacyLeft, legacyTop, windowWidth, windowHeight, owner.WorkingArea);

        return settings with
        {
            OverlayLeft = null,
            OverlayTop = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = xRatio,
            OverlayYRatio = yRatio,
            OverlayCustomMonitorId = MonitorId.Resolve(owner)
        };
    }

    /// <summary>
    /// Resolves the restore position for custom mode (startup, DPI change,
    /// display topology change). Never fires DragCompleted. Returns the target
    /// physical coordinates and, if state changed (legacy migration or a
    /// first-seen display inheriting its own record), the updated settings to
    /// persist. When the state is unchanged, UpdatedSettings is null so callers
    /// can skip a redundant save.
    ///
    /// The target monitor is resolved from OverlayCustomMonitorId. If that
    /// monitor still exists, it is used. If it does not (display disconnected),
    /// the current window's monitor is used as a temporary fallback — the
    /// fallback never overwrites OverlayCustomMonitorId so the original target
    /// is restored when it reappears.
    /// </summary>
    public static (double Left, double Top, TraySettings? UpdatedSettings) ResolveRestorePosition(
        TraySettings settings,
        double currentLeft,
        double currentTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor)
    {
        TraySettings effective = settings;

        if (HasLegacyCoordinates(effective) && !HasKeepRelativeRatios(effective))
        {
            effective = MigrateLegacy(effective, windowWidth, windowHeight, monitors, primaryMonitor);
        }

        MonitorInfo currentMonitor = OverlayPositionCalculator.FindBestMonitor(
            currentLeft, currentTop, windowWidth, windowHeight, monitors, primaryMonitor);

        MonitorInfo targetMonitor = ResolveTargetMonitor(effective, currentMonitor, monitors);

        if (effective.OverlayCustomPositionMode == OverlayCustomPositionMode.RememberPerDisplay)
        {
            return ResolveRememberPerDisplay(effective, currentLeft, currentTop, windowWidth, windowHeight, targetMonitor, settings);
        }

        return ResolveKeepRelative(effective, windowWidth, windowHeight, targetMonitor, settings);
    }

    /// <summary>
    /// Resolves the target monitor from OverlayCustomMonitorId. If the stored
    /// id matches a monitor in the list, that monitor is returned. Otherwise
    /// the fallback (current window monitor or primary) is returned. The
    /// caller must never write the fallback id back into settings.
    /// </summary>
    private static MonitorInfo ResolveTargetMonitor(
        TraySettings settings,
        MonitorInfo currentMonitor,
        IReadOnlyList<MonitorInfo> monitors)
    {
        string? customId = settings.OverlayCustomMonitorId;
        if (!string.IsNullOrWhiteSpace(customId))
        {
            foreach (MonitorInfo m in monitors)
            {
                if (MonitorId.Equals(MonitorId.Resolve(m), customId))
                    return m;
            }
        }

        return currentMonitor;
    }

    private static (double Left, double Top, TraySettings? UpdatedSettings) ResolveKeepRelative(
        TraySettings effective,
        double windowWidth,
        double windowHeight,
        MonitorInfo currentMonitor,
        TraySettings original)
    {
        double xRatio = effective.OverlayXRatio ?? DefaultXRatio;
        double yRatio = effective.OverlayYRatio ?? DefaultYRatio;

        var (left, top) = ToCoordinates(xRatio, yRatio, windowWidth, windowHeight, currentMonitor.WorkingArea);

        TraySettings? updated = effective == original ? null : effective;
        return (left, top, updated);
    }

    private static (double Left, double Top, TraySettings? UpdatedSettings) ResolveRememberPerDisplay(
        TraySettings effective,
        double currentLeft,
        double currentTop,
        double windowWidth,
        double windowHeight,
        MonitorInfo currentMonitor,
        TraySettings original)
    {
        string monitorId = MonitorId.Resolve(currentMonitor);
        var perDisplay = effective.OverlayPerDisplayPositions;

        if (perDisplay != null && TryFind(perDisplay, monitorId, out var existing))
        {
            var (left, top) = ToCoordinates(existing.XRatio, existing.YRatio, windowWidth, windowHeight, currentMonitor.WorkingArea);
            TraySettings? effectiveUpdated = effective == original ? null : effective;
            return (left, top, effectiveUpdated);
        }

        DisplayRelativePosition? legacyRecord = TryMigrateLegacyRecord(perDisplay, currentMonitor, monitorId);

        if (legacyRecord is not null)
        {
            var (left, top) = ToCoordinates(legacyRecord.XRatio, legacyRecord.YRatio, windowWidth, windowHeight, currentMonitor.WorkingArea);
            var migrated = new Dictionary<string, DisplayRelativePosition>(perDisplay ?? EmptyPositions, StringComparer.OrdinalIgnoreCase)
            {
                [monitorId] = legacyRecord
            };
            TraySettings withRecord = effective with { OverlayPerDisplayPositions = migrated };
            TraySettings? updated = withRecord == original ? null : withRecord;
            return (left, top, updated);
        }

        var (xRatio, yRatio) = ToRatios(currentLeft, currentTop, windowWidth, windowHeight, currentMonitor.WorkingArea);
        var (newLeft, newTop) = ToCoordinates(xRatio, yRatio, windowWidth, windowHeight, currentMonitor.WorkingArea);

        var inherited = new Dictionary<string, DisplayRelativePosition>(perDisplay ?? EmptyPositions, StringComparer.OrdinalIgnoreCase)
        {
            [monitorId] = new DisplayRelativePosition(xRatio, yRatio)
        };

        TraySettings withInherited = effective with { OverlayPerDisplayPositions = inherited };
        TraySettings? updatedSettings = withInherited == original ? null : withInherited;
        return (newLeft, newTop, updatedSettings);
    }

    /// <summary>
    /// When the stable id has no record but the GDI source name (DeviceName)
    /// has a legacy record, returns that record so it can be copied to the
    /// stable id key. This handles the upgrade path from DeviceName-keyed
    /// records to StableId-keyed records without losing the user's saved
    /// position. Returns null when there is nothing to migrate.
    /// </summary>
    private static DisplayRelativePosition? TryMigrateLegacyRecord(
        IReadOnlyDictionary<string, DisplayRelativePosition>? perDisplay,
        MonitorInfo currentMonitor,
        string stableKey)
    {
        if (perDisplay == null || string.IsNullOrWhiteSpace(currentMonitor.StableId))
            return null;

        string deviceName = currentMonitor.DeviceName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceName))
            return null;

        if (MonitorId.Equals(deviceName, stableKey))
            return null;

        if (TryFind(perDisplay, deviceName, out var legacy))
            return legacy;

        return null;
    }

    /// <summary>
    /// Saves the custom position from a user drag end. The landed monitor is
    /// resolved from the final window rect. KeepRelative writes the shared
    /// ratios; RememberPerDisplay writes (overwrites) the landed monitor's
    /// record. Legacy absolute coordinates are always cleared. The preset
    /// selection and preset monitor device name are cleared because a manual
    /// drag exits preset mode. OverlayCustomMonitorId is set to the landed
    /// monitor so later restores target the display the user chose.
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

        if (settings.OverlayCustomPositionMode == OverlayCustomPositionMode.RememberPerDisplay)
        {
            var perDisplay = new Dictionary<string, DisplayRelativePosition>(
                settings.OverlayPerDisplayPositions ?? EmptyPositions,
                StringComparer.OrdinalIgnoreCase)
            {
                [landedId] = new DisplayRelativePosition(xRatio, yRatio)
            };

            return settings with
            {
                OverlayLeft = null,
                OverlayTop = null,
                OverlayPosition = null,
                OverlayMonitorDeviceName = null,
                OverlayPerDisplayPositions = perDisplay,
                OverlayCustomMonitorId = landedId
            };
        }

        return settings with
        {
            OverlayLeft = null,
            OverlayTop = null,
            OverlayPosition = null,
            OverlayMonitorDeviceName = null,
            OverlayXRatio = xRatio,
            OverlayYRatio = yRatio,
            OverlayCustomMonitorId = landedId
        };
    }

    /// <summary>
    /// Applies a mode switch without window context. Changes the mode
    /// flag and clears all preset-mode fields (OverlayPosition,
    /// OverlayMonitorDeviceName, OverlayLeft, OverlayTop) so the overlay
    /// truly enters custom-position mode. Use SwitchMode when the current
    /// window rect and monitors are available — it computes ratios from
    /// the live position so the window does not jump.
    ///
    /// If the settings are already in the requested mode AND not in
    /// preset mode (OverlayPosition is null), this is a true no-op.
    /// If OverlayPosition.HasValue, the settings are still in preset mode
    /// and must transition to custom mode even if the mode flag matches.
    /// </summary>
    public static TraySettings ApplyModeSwitch(
        TraySettings settings,
        OverlayCustomPositionMode mode)
    {
        if (settings.OverlayCustomPositionMode == mode && !settings.OverlayPosition.HasValue)
            return settings;

        return settings with
        {
            OverlayCustomPositionMode = mode,
            OverlayPosition = null,
            OverlayMonitorDeviceName = null,
            OverlayLeft = null,
            OverlayTop = null
        };
    }

    /// <summary>
    /// Switches the custom position mode using the current window's physical
    /// position and monitor layout. Ratios are computed from the live position
    /// against the current monitor's work area so the window does not jump.
    ///
    /// - KeepRelative -> RememberPerDisplay: writes the current monitor's
    ///   record from the current position and sets OverlayCustomMonitorId.
    /// - RememberPerDisplay -> KeepRelative: writes the shared ratios from
    ///   the current position and sets OverlayCustomMonitorId.
    /// - Same mode but still in preset mode (OverlayPosition.HasValue):
    ///   exits preset mode and initializes ratios from the current position.
    /// - Same mode and already in custom mode (OverlayPosition is null):
    ///   true no-op.
    ///
    /// Always clears OverlayPosition, OverlayMonitorDeviceName, OverlayLeft,
    /// and OverlayTop so the overlay enters custom-position mode.
    ///
    /// The result is independent of dictionary enumeration order because the
    /// ratios are always derived from the current window position, never from
    /// a pre-existing record.
    /// </summary>
    public static TraySettings SwitchMode(
        TraySettings settings,
        OverlayCustomPositionMode mode,
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primaryMonitor)
    {
        // True no-op: same mode AND already in custom mode (not preset).
        if (settings.OverlayCustomPositionMode == mode && !settings.OverlayPosition.HasValue)
            return settings;

        MonitorInfo current = OverlayPositionCalculator.FindBestMonitor(
            windowLeft, windowTop, windowWidth, windowHeight, monitors, primaryMonitor);

        var (xRatio, yRatio) = ToRatios(windowLeft, windowTop, windowWidth, windowHeight, current.WorkingArea);
        string currentId = MonitorId.Resolve(current);

        if (mode == OverlayCustomPositionMode.RememberPerDisplay)
        {
            var perDisplay = new Dictionary<string, DisplayRelativePosition>(
                settings.OverlayPerDisplayPositions ?? EmptyPositions,
                StringComparer.OrdinalIgnoreCase)
            {
                [currentId] = new DisplayRelativePosition(xRatio, yRatio)
            };

            return settings with
            {
                OverlayCustomPositionMode = mode,
                OverlayPosition = null,
                OverlayMonitorDeviceName = null,
                OverlayLeft = null,
                OverlayTop = null,
                OverlayPerDisplayPositions = perDisplay,
                OverlayCustomMonitorId = currentId
            };
        }

        return settings with
        {
            OverlayCustomPositionMode = mode,
            OverlayPosition = null,
            OverlayMonitorDeviceName = null,
            OverlayLeft = null,
            OverlayTop = null,
            OverlayXRatio = xRatio,
            OverlayYRatio = yRatio,
            OverlayCustomMonitorId = currentId
        };
    }

    private static bool TryFind(
        IReadOnlyDictionary<string, DisplayRelativePosition> perDisplay,
        string monitorId,
        out DisplayRelativePosition position)
    {
        foreach (var kvp in perDisplay)
        {
            if (MonitorId.Equals(kvp.Key, monitorId))
            {
                position = kvp.Value;
                return true;
            }
        }

        position = default!;
        return false;
    }

    private static IReadOnlyDictionary<string, DisplayRelativePosition> EmptyPositions
        => new Dictionary<string, DisplayRelativePosition>(0, StringComparer.OrdinalIgnoreCase);

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
}
