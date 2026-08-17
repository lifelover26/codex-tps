using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayCustomPositionCoordinatorTests
{
    private const double WindowWidth = 200.0;
    private const double WindowHeight = 120.0;

    private static MonitorInfo Primary1920() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(0, 0, 1920, 1080),
        IsPrimary: true);

    private static MonitorInfo Secondary1920() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY2",
        WorkingArea: new Rect(1920, 0, 1920, 1080),
        IsPrimary: false);

    private static MonitorInfo NegativeMonitor() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY3",
        WorkingArea: new Rect(-1920, -200, 1920, 1080),
        IsPrimary: false);

    // =========================================================================
    // Scenario 1: defaults and mode switch
    // =========================================================================

    [Fact]
    public void DefaultMode_IsKeepRelative()
    {
        Assert.Equal(OverlayCustomPositionMode.KeepRelative, TraySettings.Default.OverlayCustomPositionMode);
    }

    [Fact]
    public void ApplyModeSwitch_ToRememberPerDisplay_ChangesMode()
    {
        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative
        };

        var result = OverlayCustomPositionCoordinator.ApplyModeSwitch(settings, OverlayCustomPositionMode.RememberPerDisplay);

        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, result.OverlayCustomPositionMode);
        Assert.Null(result.OverlayPosition);
    }

    [Fact]
    public void ApplyModeSwitch_SameMode_AlreadyInCustomMode_IsNoOp()
    {
        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative
        };

        var result = OverlayCustomPositionCoordinator.ApplyModeSwitch(settings, OverlayCustomPositionMode.KeepRelative);

        Assert.Same(settings, result);
    }

    // =========================================================================
    // Scenario 2: normalized coords restore to the same relative position across resolutions
    // =========================================================================

    [Fact]
    public void KeepRelative_Restore_AcrossResolutions_PreservesRatio()
    {
        var primary1080 = Primary1920();
        var monitors = new List<MonitorInfo> { primary1080 };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.25,
            OverlayYRatio = 0.5
        };

        var (left1080, top1080, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 400, 200, WindowWidth, WindowHeight, monitors, primary1080);

        var primary1440 = new MonitorInfo(@"\\.\DISPLAY1", new Rect(0, 0, 2560, 1440), true);
        var monitors1440 = new List<MonitorInfo> { primary1440 };

        var (left1440, top1440, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 400, 200, WindowWidth, WindowHeight, monitors1440, primary1440);

        var (ratio1080X, ratio1080Y) = OverlayCustomPositionCoordinator.ToRatios(
            left1080, top1080, WindowWidth, WindowHeight, primary1080.WorkingArea);
        var (ratio1440X, ratio1440Y) = OverlayCustomPositionCoordinator.ToRatios(
            left1440, top1440, WindowWidth, WindowHeight, primary1440.WorkingArea);

        Assert.Equal(ratio1080X, ratio1440X, 5);
        Assert.Equal(ratio1080Y, ratio1440Y, 5);
        Assert.Equal(0.25, ratio1080X, 5);
        Assert.Equal(0.5, ratio1080Y, 5);
    }

    // =========================================================================
    // Scenario 3: same monitor DPI change keeps the relative position stable
    // =========================================================================

    [Fact]
    public void KeepRelative_SameMonitorDpiChange_RatioStable()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.3,
            OverlayYRatio = 0.7
        };

        var (left, top, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight, monitors, primary);

        var (ratioX, ratioY) = OverlayCustomPositionCoordinator.ToRatios(
            left, top, WindowWidth, WindowHeight, primary.WorkingArea);

        Assert.Equal(0.3, ratioX, 5);
        Assert.Equal(0.7, ratioY, 5);
    }

    // =========================================================================
    // Scenario 4: negative-coordinate monitor works
    // =========================================================================

    [Fact]
    public void KeepRelative_NegativeCoordinateMonitor_RestoresCorrectly()
    {
        var negative = NegativeMonitor();
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary, negative };

        double savedLeft = -1500;
        double savedTop = 100;

        var (xRatio, yRatio) = OverlayCustomPositionCoordinator.ToRatios(
            savedLeft, savedTop, WindowWidth, WindowHeight, negative.WorkingArea);

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = xRatio,
            OverlayYRatio = yRatio
        };

        var (left, top, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, savedLeft, savedTop, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(savedLeft, left, 5);
        Assert.Equal(savedTop, top, 5);
    }

    // =========================================================================
    // Scenario 5: Remember per display saves and restores different positions per monitor
    // =========================================================================

    [Fact]
    public void RememberPerDisplay_DragOnAAndB_RestoresEachIndependently()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var afterA = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 500, 300, WindowWidth, WindowHeight, monitors, primary);
        var afterB = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            afterA, 2200, 400, WindowWidth, WindowHeight, monitors, primary);

        // Both records must exist independently in afterB
        Assert.NotNull(afterB.OverlayPerDisplayPositions);
        Assert.Equal(2, afterB.OverlayPerDisplayPositions!.Count);
        string primaryId = MonitorId.Resolve(primary);
        string secondaryId = MonitorId.Resolve(secondary);
        Assert.True(afterB.OverlayPerDisplayPositions.ContainsKey(primaryId));
        Assert.True(afterB.OverlayPerDisplayPositions.ContainsKey(secondaryId));

        // afterB's target is B (secondary). Explicitly target A by setting
        // OverlayCustomMonitorId to primary, even with currentLeft/currentTop
        // deliberately on B (2200,400) — the explicit target ID must win.
        var targetA = afterB with { OverlayCustomMonitorId = primaryId };
        var (restoreALeft, restoreATop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            targetA, 2200, 400, WindowWidth, WindowHeight, monitors, primary);

        // Explicitly target B; currentLeft/currentTop on A (500,300) must
        // not cause a fallback to A's record.
        var targetB = afterB with { OverlayCustomMonitorId = secondaryId };
        var (restoreBLeft, restoreBTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            targetB, 500, 300, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(500, restoreALeft, 5);
        Assert.Equal(300, restoreATop, 5);
        Assert.Equal(2200, restoreBLeft, 5);
        Assert.Equal(400, restoreBTop, 5);
    }

    // =========================================================================
    // Scenario 6: first-seen display inherits the current ratio
    // =========================================================================

    [Fact]
    public void RememberPerDisplay_FirstSeenDisplay_InheritsCurrentRatio_NoJump()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        double currentLeft = 2200;
        double currentTop = 400;

        var (left, top, updated) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);

        Assert.NotNull(updated);
        Assert.Equal(currentLeft, left, 5);
        Assert.Equal(currentTop, top, 5);

        string secondaryId = MonitorId.Resolve(secondary);
        Assert.True(updated!.OverlayPerDisplayPositions != null);
        Assert.True(OverlayCustomPositionCoordinator.HasPerDisplayRecords(updated));
    }

    // =========================================================================
    // Scenario 7: cross-display drag overwrites that display's existing record
    // =========================================================================

    [Fact]
    public void RememberPerDisplay_DragOntoDisplayWithRecord_OverwritesRecord()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var afterFirst = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 2200, 400, WindowWidth, WindowHeight, monitors, primary);
        var afterSecond = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            afterFirst, 2400, 600, WindowWidth, WindowHeight, monitors, primary);

        var (restoreLeft, restoreTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            afterSecond, 2400, 600, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(2400, restoreLeft, 5);
        Assert.Equal(600, restoreTop, 5);
    }

    // =========================================================================
    // Scenario 8: display temporarily disconnected does not lose the original record
    // =========================================================================

    [Fact]
    public void RememberPerDisplay_DisplayDisconnected_KeepsRecord_ForReconnect()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var withRecords = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 500, 300, WindowWidth, WindowHeight, monitors, primary);
        withRecords = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            withRecords, 2200, 400, WindowWidth, WindowHeight, monitors, primary);

        var disconnected = new List<MonitorInfo> { primary };

        var (restoreOnPrimaryLeft, restoreOnPrimaryTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            withRecords, 500, 300, WindowWidth, WindowHeight, disconnected, primary);

        Assert.Equal(500, restoreOnPrimaryLeft, 5);
        Assert.Equal(300, restoreOnPrimaryTop, 5);

        string secondaryId = MonitorId.Resolve(secondary);
        Assert.NotNull(withRecords.OverlayPerDisplayPositions);
        Assert.True(TryFind(withRecords.OverlayPerDisplayPositions!, secondaryId, out _));

        var (restoreOnSecondaryLeft, restoreOnSecondaryTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            withRecords, 2200, 400, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(2200, restoreOnSecondaryLeft, 5);
        Assert.Equal(400, restoreOnSecondaryTop, 5);
    }

    // =========================================================================
    // Scenario 9: legacy OverlayLeft/OverlayTop migration
    // =========================================================================

    [Fact]
    public void MigrateLegacy_AbsoluteCoords_BecomesKeepRelativeRatios()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var legacy = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayLeft = 500.0,
            OverlayTop = 300.0
        };

        var migrated = OverlayCustomPositionCoordinator.MigrateLegacy(
            legacy, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(OverlayCustomPositionMode.KeepRelative, migrated.OverlayCustomPositionMode);
        Assert.Null(migrated.OverlayLeft);
        Assert.Null(migrated.OverlayTop);
        Assert.True(migrated.OverlayXRatio.HasValue);
        Assert.True(migrated.OverlayYRatio.HasValue);

        var (left, top, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            migrated, 500, 300, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(500, left, 5);
        Assert.Equal(300, top, 5);
    }

    [Fact]
    public void ResolveRestorePosition_LegacyCoords_MigratesAndReturnsUpdatedSettings()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var legacy = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayLeft = 500.0,
            OverlayTop = 300.0
        };

        var (left, top, updated) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            legacy, 500, 300, WindowWidth, WindowHeight, monitors, primary);

        Assert.NotNull(updated);
        Assert.Null(updated!.OverlayLeft);
        Assert.True(updated.OverlayXRatio.HasValue);
        Assert.Equal(500, left, 5);
        Assert.Equal(300, top, 5);
    }

    // =========================================================================
    // Scenario 10: auto-reposition is idempotent — no cumulative drift
    // =========================================================================

    [Fact]
    public void ResolveRestorePosition_RepeatedCalls_IdempotentNoDrift()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.25
        };

        var (left1, top1, updated1) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight, monitors, primary);
        var (left2, top2, updated2) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(left1, left2);
        Assert.Equal(top1, top2);
        Assert.Null(updated1);
        Assert.Null(updated2);
    }

    // =========================================================================
    // Scenario 11: presets unaffected — drag exits preset, preset calculator untouched
    // =========================================================================

    [Fact]
    public void SaveFromDragEnd_ClearsPresetSelection()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var presetSettings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.TopRight,
            OverlayMonitorDeviceName = @"\\.\DISPLAY1"
        };

        var result = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            presetSettings, 500, 300, WindowWidth, WindowHeight, monitors, primary);

        Assert.Null(result.OverlayPosition);
        Assert.Null(result.OverlayMonitorDeviceName);
    }

    [Fact]
    public void PresetPositionCalculator_IsUnchangedByCoordinator()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var size = new System.Windows.Size(200, 120);
        var margin = new Thickness(16);

        var (left, top) = OverlayPositionCalculator.CalculatePresetPosition(
            OverlayPositionPreset.TopRight, size, workArea, margin);

        Assert.Equal(1920 - 200 - 16, left);
        Assert.Equal(16, top);
    }

    // =========================================================================
    // Ratio math edge cases
    // =========================================================================

    [Fact]
    public void ToRatios_ClampsToUnitInterval()
    {
        var workArea = new Rect(0, 0, 1920, 1080);

        var (xRatio, yRatio) = OverlayCustomPositionCoordinator.ToRatios(
            5000, 5000, WindowWidth, WindowHeight, workArea);

        Assert.Equal(1.0, xRatio);
        Assert.Equal(1.0, yRatio);
    }

    [Fact]
    public void ToCoordinates_WindowLargerThanWorkArea_PinsToOrigin()
    {
        var workArea = new Rect(10, 20, 100, 80);

        var (left, top) = OverlayCustomPositionCoordinator.ToCoordinates(
            0.5, 0.5, 200, 120, workArea);

        Assert.Equal(10, left);
        Assert.Equal(20, top);
    }

    // =========================================================================
    // Scenario A: secondary display saved, HWND starts on primary, restores to secondary
    // =========================================================================

    [Fact]
    public void KeepRelative_TargetSecondary_RestoresToSecondaryEvenFromPrimaryHwnd()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.5,
            OverlayCustomMonitorId = MonitorId.Resolve(secondary)
        };

        // HWND is initially on the primary monitor (e.g. WPF default placement).
        var (left, top, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight, monitors, primary);

        // Should restore to the secondary monitor because OverlayCustomMonitorId
        // targets it and it still exists.
        Assert.True(left >= secondary.WorkingArea.Left);
        Assert.True(left < secondary.WorkingArea.Right);
    }

    // =========================================================================
    // Scenario B: target display disconnected — fallback without overwriting ID
    // =========================================================================

    [Fact]
    public void KeepRelative_TargetDisconnected_FallsBackWithoutOverwritingId()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var bothMonitors = new List<MonitorInfo> { primary, secondary };

        var saved = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            TraySettings.Default with { OverlayPosition = null },
            2200, 400, WindowWidth, WindowHeight, bothMonitors, primary);

        string secondaryId = MonitorId.Resolve(secondary);
        Assert.Equal(secondaryId, saved.OverlayCustomMonitorId);

        // Secondary is disconnected — only primary remains.
        var primaryOnly = new List<MonitorInfo> { primary };

        var (fallbackLeft, fallbackTop, updated) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            saved, 100, 100, WindowWidth, WindowHeight, primaryOnly, primary);

        // Fallback lands on the primary monitor.
        Assert.True(fallbackLeft >= primary.WorkingArea.Left);
        Assert.True(fallbackLeft < primary.WorkingArea.Right);

        // The updated settings must NOT overwrite the target monitor id.
        if (updated != null)
            Assert.Equal(secondaryId, updated.OverlayCustomMonitorId);

        // When the secondary reconnects, restore targets it again.
        var (reconnectedLeft, reconnectedTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            saved, 100, 100, WindowWidth, WindowHeight, bothMonitors, primary);

        Assert.True(reconnectedLeft >= secondary.WorkingArea.Left);
        Assert.True(reconnectedLeft < secondary.WorkingArea.Right);
    }

    // =========================================================================
    // Scenario C: new display connected, current target exists — no jump
    // =========================================================================

    [Fact]
    public void KeepRelative_NewDisplayConnected_CurrentTargetExists_NoJump()
    {
        var primary = Primary1920();
        var primaryOnly = new List<MonitorInfo> { primary };

        var saved = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            TraySettings.Default with { OverlayPosition = null },
            500, 300, WindowWidth, WindowHeight, primaryOnly, primary);

        // A new secondary display is connected. The target (primary) still exists.
        var secondary = Secondary1920();
        var bothMonitors = new List<MonitorInfo> { primary, secondary };

        var (left, top, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            saved, 500, 300, WindowWidth, WindowHeight, bothMonitors, primary);

        // Should still restore to the primary, not jump to the new secondary.
        Assert.Equal(500, left, 5);
        Assert.Equal(300, top, 5);
    }

    // =========================================================================
    // Scenario D: SwitchMode KeepRelative -> RememberPerDisplay uses current position
    // =========================================================================

    [Fact]
    public void SwitchMode_ToRememberPerDisplay_WritesCurrentMonitorRecord()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.3,
            OverlayYRatio = 0.7
        };

        // Window is currently on the secondary display at a specific position.
        double currentLeft = 2200;
        double currentTop = 400;

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings,
            OverlayCustomPositionMode.RememberPerDisplay,
            currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, result.OverlayCustomPositionMode);
        Assert.Equal(MonitorId.Resolve(secondary), result.OverlayCustomMonitorId);
        Assert.NotNull(result.OverlayPerDisplayPositions);
        Assert.True(result.OverlayPerDisplayPositions!.Count >= 1);

        // The secondary's record should match the current position, not the old
        // shared ratios.
        string secondaryId = MonitorId.Resolve(secondary);
        Assert.True(TryFind(result.OverlayPerDisplayPositions, secondaryId, out var record));
        var (expectedLeft, expectedTop) = OverlayCustomPositionCoordinator.ToCoordinates(
            record.XRatio, record.YRatio, WindowWidth, WindowHeight, secondary.WorkingArea);
        Assert.Equal(currentLeft, expectedLeft, 5);
        Assert.Equal(currentTop, expectedTop, 5);
    }

    // =========================================================================
    // Scenario E: SwitchMode RememberPerDisplay -> KeepRelative uses current position,
    //             not the first dictionary record
    // =========================================================================

    [Fact]
    public void SwitchMode_ToKeepRelative_UsesCurrentPosition_NotFirstRecord()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        // Per-display records exist for both monitors with different ratios.
        var positions = new Dictionary<string, DisplayRelativePosition>(StringComparer.OrdinalIgnoreCase)
        {
            [MonitorId.Resolve(primary)] = new DisplayRelativePosition(0.1, 0.1),
            [MonitorId.Resolve(secondary)] = new DisplayRelativePosition(0.9, 0.9)
        };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay,
            OverlayPerDisplayPositions = positions
        };

        // Window is currently on the secondary display at a specific position.
        double currentLeft = 2200;
        double currentTop = 400;

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings,
            OverlayCustomPositionMode.KeepRelative,
            currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(OverlayCustomPositionMode.KeepRelative, result.OverlayCustomPositionMode);
        Assert.Equal(MonitorId.Resolve(secondary), result.OverlayCustomMonitorId);

        // The shared ratios should match the current position on the secondary,
        // NOT the first record in the dictionary (which could be primary's 0.1/0.1).
        var (restoreLeft, restoreTop) = OverlayCustomPositionCoordinator.ToCoordinates(
            result.OverlayXRatio!.Value, result.OverlayYRatio!.Value,
            WindowWidth, WindowHeight, secondary.WorkingArea);
        Assert.Equal(currentLeft, restoreLeft, 5);
        Assert.Equal(currentTop, restoreTop, 5);

        // Explicitly verify it's not the primary's record.
        Assert.NotEqual(0.1, result.OverlayXRatio!.Value);
        Assert.NotEqual(0.1, result.OverlayYRatio!.Value);
    }

    // =========================================================================
    // Scenario F: SwitchMode same mode is a no-op
    // =========================================================================

    [Fact]
    public void SwitchMode_SameMode_IsNoOp()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.5
        };

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings,
            OverlayCustomPositionMode.KeepRelative,
            500, 300, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(settings, result);
    }

    // =========================================================================
    // Scenario G: SwitchMode visual position does not jump
    // =========================================================================

    [Fact]
    public void SwitchMode_BothDirections_NoVisualJump()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        double currentLeft = 2200;
        double currentTop = 400;

        // KeepRelative -> RememberPerDisplay
        var keepRelSettings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.3,
            OverlayYRatio = 0.7
        };

        var afterSwitch = OverlayCustomPositionCoordinator.SwitchMode(
            keepRelSettings,
            OverlayCustomPositionMode.RememberPerDisplay,
            currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);

        var (leftAfterSwitch, topAfterSwitch, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            afterSwitch, currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);
        Assert.Equal(currentLeft, leftAfterSwitch, 5);
        Assert.Equal(currentTop, topAfterSwitch, 5);

        // RememberPerDisplay -> KeepRelative
        var perDisplaySettings = afterSwitch;
        var afterSwitchBack = OverlayCustomPositionCoordinator.SwitchMode(
            perDisplaySettings,
            OverlayCustomPositionMode.KeepRelative,
            currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);

        var (leftAfterSwitchBack, topAfterSwitchBack, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            afterSwitchBack, currentLeft, currentTop, WindowWidth, WindowHeight, monitors, primary);
        Assert.Equal(currentLeft, leftAfterSwitchBack, 5);
        Assert.Equal(currentTop, topAfterSwitchBack, 5);
    }

    // =========================================================================
    // Scenario H: SaveFromDragEnd sets OverlayCustomMonitorId
    // =========================================================================

    [Fact]
    public void SaveFromDragEnd_SetsOverlayCustomMonitorId()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with { OverlayPosition = null };

        var result = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 2200, 400, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(MonitorId.Resolve(secondary), result.OverlayCustomMonitorId);
    }

    // =========================================================================
    // Scenario I: MigrateLegacy sets OverlayCustomMonitorId
    // =========================================================================

    [Fact]
    public void MigrateLegacy_SetsOverlayCustomMonitorId()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var legacy = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayLeft = 500.0,
            OverlayTop = 300.0
        };

        var migrated = OverlayCustomPositionCoordinator.MigrateLegacy(
            legacy, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(MonitorId.Resolve(primary), migrated.OverlayCustomMonitorId);
    }

    // =========================================================================
    // Scenario J: RememberPerDisplay target disconnected keeps record, reconnect restores
    // =========================================================================

    [Fact]
    public void RememberPerDisplay_TargetDisconnected_KeepsRecord_ReconnectRestores()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var bothMonitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var saved = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 2200, 400, WindowWidth, WindowHeight, bothMonitors, primary);

        string secondaryId = MonitorId.Resolve(secondary);

        // Secondary disconnected.
        var primaryOnly = new List<MonitorInfo> { primary };
        var (fallbackLeft, fallbackTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            saved, 100, 100, WindowWidth, WindowHeight, primaryOnly, primary);

        // Fallback to primary, but record for secondary is preserved.
        Assert.True(fallbackLeft >= primary.WorkingArea.Left);
        Assert.True(fallbackLeft < primary.WorkingArea.Right);
        Assert.NotNull(saved.OverlayPerDisplayPositions);
        Assert.True(TryFind(saved.OverlayPerDisplayPositions!, secondaryId, out _));

        // Secondary reconnected — restore to the saved position.
        var (restoredLeft, restoredTop, _) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            saved, 100, 100, WindowWidth, WindowHeight, bothMonitors, primary);

        Assert.Equal(2200, restoredLeft, 5);
        Assert.Equal(400, restoredTop, 5);
    }

    // =========================================================================
    // Scenario K: Repeated ResolveRestorePosition is idempotent with target monitor
    // =========================================================================

    [Fact]
    public void ResolveRestorePosition_WithTargetMonitor_RepeatedCalls_Idempotent()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.5,
            OverlayCustomMonitorId = MonitorId.Resolve(secondary)
        };

        var (left1, top1, updated1) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight, monitors, primary);
        var (left2, top2, updated2) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight, monitors, primary);

        Assert.Equal(left1, left2);
        Assert.Equal(top1, top2);
        Assert.Null(updated1);
        Assert.Null(updated2);
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

    // =========================================================================
    // Scenario: Same custom mode selected while still in preset mode exits preset
    // =========================================================================

    [Fact]
    public void SwitchMode_RememberPerDisplay_WhileInPreset_ExitsPresetAndWritesRecord()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.BottomRight,
            OverlayMonitorDeviceName = @"\\.\DISPLAY1",
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        double winLeft = 860;
        double winTop = 230;

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings, OverlayCustomPositionMode.RememberPerDisplay,
            winLeft, winTop, WindowWidth, WindowHeight, monitors, primary);

        Assert.Null(result.OverlayPosition);
        Assert.Null(result.OverlayMonitorDeviceName);
        Assert.Null(result.OverlayLeft);
        Assert.Null(result.OverlayTop);
        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, result.OverlayCustomPositionMode);

        string primaryId = MonitorId.Resolve(primary);
        Assert.NotNull(result.OverlayPerDisplayPositions);
        Assert.True(result.OverlayPerDisplayPositions!.ContainsKey(primaryId));
        Assert.Equal(primaryId, result.OverlayCustomMonitorId);
    }

    [Fact]
    public void SwitchMode_KeepRelative_WhileInPreset_ExitsPresetAndWritesRatios()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.BottomRight,
            OverlayMonitorDeviceName = @"\\.\DISPLAY1",
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative
        };

        double winLeft = 860;
        double winTop = 230;

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings, OverlayCustomPositionMode.KeepRelative,
            winLeft, winTop, WindowWidth, WindowHeight, monitors, primary);

        Assert.Null(result.OverlayPosition);
        Assert.Null(result.OverlayMonitorDeviceName);
        Assert.Null(result.OverlayLeft);
        Assert.Null(result.OverlayTop);
        Assert.Equal(OverlayCustomPositionMode.KeepRelative, result.OverlayCustomPositionMode);
        Assert.NotNull(result.OverlayXRatio);
        Assert.NotNull(result.OverlayYRatio);
        Assert.Equal(MonitorId.Resolve(primary), result.OverlayCustomMonitorId);
    }

    [Fact]
    public void SwitchMode_FromPresetToDifferentCustomMode_ClearsPresetFields()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.TopRight,
            OverlayMonitorDeviceName = @"\\.\DISPLAY1",
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative
        };

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings, OverlayCustomPositionMode.RememberPerDisplay,
            860, 230, WindowWidth, WindowHeight, monitors, primary);

        Assert.Null(result.OverlayPosition);
        Assert.Null(result.OverlayMonitorDeviceName);
        Assert.Null(result.OverlayLeft);
        Assert.Null(result.OverlayTop);
        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, result.OverlayCustomPositionMode);
    }

    [Fact]
    public void SwitchMode_PositionUnchanged_AfterSwitchFromPreset()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        double winLeft = 860;
        double winTop = 230;

        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.BottomRight,
            OverlayMonitorDeviceName = @"\\.\DISPLAY1",
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative
        };

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings, OverlayCustomPositionMode.KeepRelative,
            winLeft, winTop, WindowWidth, WindowHeight, monitors, primary);

        // Compute expected position from the same ratios
        var (expectedLeft, expectedTop) = OverlayCustomPositionCoordinator.ToCoordinates(
            result.OverlayXRatio!.Value, result.OverlayYRatio!.Value,
            WindowWidth, WindowHeight, primary.WorkingArea);

        Assert.Equal(winLeft, expectedLeft, 5);
        Assert.Equal(winTop, expectedTop, 5);
    }

    [Fact]
    public void SwitchMode_RememberPerDisplay_PreservesOtherDisplayRecords()
    {
        var primary = Primary1920();
        var secondary = Secondary1920();
        var monitors = new List<MonitorInfo> { primary, secondary };

        var existingPerDisplay = new Dictionary<string, DisplayRelativePosition>(StringComparer.OrdinalIgnoreCase)
        {
            [MonitorId.Resolve(secondary)] = new DisplayRelativePosition(0.3, 0.7)
        };

        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.BottomRight,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay,
            OverlayPerDisplayPositions = existingPerDisplay
        };

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings, OverlayCustomPositionMode.RememberPerDisplay,
            860, 230, WindowWidth, WindowHeight, monitors, primary);

        // Primary record must be written
        Assert.NotNull(result.OverlayPerDisplayPositions);
        Assert.Equal(2, result.OverlayPerDisplayPositions!.Count);
        Assert.True(result.OverlayPerDisplayPositions.ContainsKey(MonitorId.Resolve(primary)));
        Assert.True(result.OverlayPerDisplayPositions.ContainsKey(MonitorId.Resolve(secondary)));

        // Secondary record must be unchanged
        var secondaryRecord = result.OverlayPerDisplayPositions[MonitorId.Resolve(secondary)];
        Assert.Equal(0.3, secondaryRecord.XRatio, 5);
        Assert.Equal(0.7, secondaryRecord.YRatio, 5);
    }

    [Fact]
    public void SwitchMode_SameModeAlreadyInCustomMode_IsIdempotent()
    {
        var primary = Primary1920();
        var monitors = new List<MonitorInfo> { primary };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.25,
            OverlayCustomMonitorId = MonitorId.Resolve(primary)
        };

        var result = OverlayCustomPositionCoordinator.SwitchMode(
            settings, OverlayCustomPositionMode.KeepRelative,
            860, 230, WindowWidth, WindowHeight, monitors, primary);

        Assert.Same(settings, result);
    }

    [Fact]
    public void ApplyModeSwitch_WhileInPreset_ClearsPresetFields()
    {
        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.BottomRight,
            OverlayMonitorDeviceName = @"\\.\DISPLAY1",
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var result = OverlayCustomPositionCoordinator.ApplyModeSwitch(
            settings, OverlayCustomPositionMode.RememberPerDisplay);

        Assert.Null(result.OverlayPosition);
        Assert.Null(result.OverlayMonitorDeviceName);
        Assert.Null(result.OverlayLeft);
        Assert.Null(result.OverlayTop);
        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, result.OverlayCustomPositionMode);
    }

    [Fact]
    public void ApplyModeSwitch_SameModeAlreadyInCustomMode_IsIdempotent()
    {
        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.25
        };

        var result = OverlayCustomPositionCoordinator.ApplyModeSwitch(
            settings, OverlayCustomPositionMode.KeepRelative);

        Assert.Same(settings, result);
    }

    // =========================================================================
    // Display identity: StableId distinguishes physical monitors that share
    // the same GDI source name under "show only on 1/2" topology.
    // =========================================================================

    private static MonitorInfo InternalDisplay() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(0, 0, 1920, 1080),
        IsPrimary: true,
        StableId: "internal-id");

    private static MonitorInfo ExternalDisplay() => new MonitorInfo(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(0, 0, 2560, 1440),
        IsPrimary: true,
        StableId: "external-id");

    [Fact]
    public void StableId_DistinguishesMonitors_WithSameDeviceName()
    {
        var internalMon = InternalDisplay();
        var externalMon = ExternalDisplay();

        string internalId = MonitorId.Resolve(internalMon);
        string externalId = MonitorId.Resolve(externalMon);

        Assert.NotEqual(internalId, externalId);
        Assert.Equal("internal-id", internalId);
        Assert.Equal("external-id", externalId);
    }

    [Fact]
    public void RememberPerDisplay_OnlyOn1_ThenOnlyOn2_RestoresEachIndependently()
    {
        var internalMon = InternalDisplay();
        var externalMon = ExternalDisplay();

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var afterInternal = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 500, 300, WindowWidth, WindowHeight,
            new List<MonitorInfo> { internalMon }, internalMon);

        var afterExternal = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            afterInternal, 1000, 500, WindowWidth, WindowHeight,
            new List<MonitorInfo> { externalMon }, externalMon);

        Assert.NotNull(afterExternal.OverlayPerDisplayPositions);
        Assert.Equal(2, afterExternal.OverlayPerDisplayPositions!.Count);
        Assert.True(afterExternal.OverlayPerDisplayPositions.ContainsKey("internal-id"));
        Assert.True(afterExternal.OverlayPerDisplayPositions.ContainsKey("external-id"));

        var restoreInternal = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            afterExternal with { OverlayCustomMonitorId = "internal-id" },
            0, 0, WindowWidth, WindowHeight,
            new List<MonitorInfo> { internalMon }, internalMon);
        Assert.Equal(500, restoreInternal.Left, 5);
        Assert.Equal(300, restoreInternal.Top, 5);

        var restoreExternal = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            afterExternal with { OverlayCustomMonitorId = "external-id" },
            0, 0, WindowWidth, WindowHeight,
            new List<MonitorInfo> { externalMon }, externalMon);
        Assert.Equal(1000, restoreExternal.Left, 5);
        Assert.Equal(500, restoreExternal.Top, 5);
    }

    [Fact]
    public void StableId_SurvivesDeviceNameChange()
    {
        var displayOldName = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true,
            StableId: "physical-monitor-A");

        var displayNewName = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY2",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true,
            StableId: "physical-monitor-A");

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay
        };

        var afterSave = OverlayCustomPositionCoordinator.SaveFromDragEnd(
            settings, 500, 300, WindowWidth, WindowHeight,
            new List<MonitorInfo> { displayOldName }, displayOldName);

        var restore = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            afterSave, 0, 0, WindowWidth, WindowHeight,
            new List<MonitorInfo> { displayNewName }, displayNewName);

        Assert.Equal(500, restore.Left, 5);
        Assert.Equal(300, restore.Top, 5);
    }

    [Fact]
    public void StableId_DoesNotRelyOnFriendlyName()
    {
        var monitorA = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true,
            StableId: "path-A");

        var monitorB = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY2",
            WorkingArea: new Rect(1920, 0, 1920, 1080),
            IsPrimary: false,
            StableId: "path-B");

        string idA = MonitorId.Resolve(monitorA);
        string idB = MonitorId.Resolve(monitorB);

        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public void MonitorId_FallsBackToDeviceName_WhenStableIdIsNull()
    {
        var monitor = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true,
            StableId: null);

        Assert.Equal(@"\\.\DISPLAY1", MonitorId.Resolve(monitor));
    }

    [Fact]
    public void LegacyDeviceNameRecord_MigratesToStableId_WithoutOverwritingOtherStableId()
    {
        var internalMon = InternalDisplay();
        var externalMon = ExternalDisplay();

        var legacyPerDisplay = new Dictionary<string, DisplayRelativePosition>(System.StringComparer.OrdinalIgnoreCase)
        {
            [@"\\.\DISPLAY1"] = new DisplayRelativePosition(0.2, 0.3),
            ["external-id"] = new DisplayRelativePosition(0.8, 0.9)
        };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay,
            OverlayPerDisplayPositions = legacyPerDisplay,
            OverlayCustomMonitorId = "internal-id"
        };

        var (left, top, updated) = OverlayCustomPositionCoordinator.ResolveRestorePosition(
            settings, 100, 100, WindowWidth, WindowHeight,
            new List<MonitorInfo> { internalMon }, internalMon);

        Assert.NotNull(updated);
        var result = updated!;
        Assert.NotNull(result.OverlayPerDisplayPositions);
        Assert.True(result.OverlayPerDisplayPositions!.ContainsKey("internal-id"));

        var internalRecord = result.OverlayPerDisplayPositions["internal-id"];
        Assert.Equal(0.2, internalRecord.XRatio, 5);
        Assert.Equal(0.3, internalRecord.YRatio, 5);

        var externalRecord = result.OverlayPerDisplayPositions["external-id"];
        Assert.Equal(0.8, externalRecord.XRatio, 5);
        Assert.Equal(0.9, externalRecord.YRatio, 5);
    }

    [Fact]
    public void NativeApiFailure_FallsBackToDeviceName_Safely()
    {
        var provider = new WindowsDisplayIdentityProvider();
        var map = provider.BuildDeviceNameToStableIdMap();

        Assert.NotNull(map);

        var monitor = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true,
            StableId: null);

        string resolved = MonitorId.Resolve(monitor);
        Assert.Equal(@"\\.\DISPLAY1", resolved);
    }
}
