using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayCustomPositionCoordinatorTests
{
    private const double WindowWidth = 200.0;
    private const double WindowHeight = 120.0;
    private static readonly Thickness Margin = new(16);

    private static MonitorInfo Primary() => new(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(0, 0, 1920, 1080),
        IsPrimary: true,
        StableId: "physical-primary");

    private static MonitorInfo Secondary() => new(
        DeviceName: @"\\.\DISPLAY2",
        WorkingArea: new Rect(1920, 0, 1920, 1080),
        IsPrimary: false,
        StableId: "physical-secondary");

    private static MonitorInfo SameDeviceNamePrimary() => new(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(0, 0, 1920, 1080),
        IsPrimary: true,
        StableId: "physical-internal");

    private static MonitorInfo SameDeviceNameExternal() => new(
        DeviceName: @"\\.\DISPLAY1",
        WorkingArea: new Rect(1920, 0, 1920, 1080),
        IsPrimary: false,
        StableId: "physical-external");

    private static List<MonitorInfo> Both(MonitorInfo primary, MonitorInfo secondary) =>
        new() { primary, secondary };

    private static TraySettings Shared(OverlayPositionState state) =>
        TraySettings.Default with
        {
            PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays,
            SharedPosition = state,
            PerDisplayPositions = null,
            OverlayTargetMonitorId = null,
            PendingPresetMigrationTarget = null,
            OverlayLeft = null,
            OverlayTop = null
        };

    private static TraySettings PerDisplay(
        OverlayPositionState sharedState,
        IReadOnlyDictionary<string, OverlayPositionState>? records = null) =>
        TraySettings.Default with
        {
            PositionMemoryMode = OverlayPositionMemoryMode.RememberPerDisplay,
            SharedPosition = sharedState,
            PerDisplayPositions = records,
            OverlayTargetMonitorId = null,
            PendingPresetMigrationTarget = null,
            OverlayLeft = null,
            OverlayTop = null
        };

    private static OverlayPositionState StateFor(
        TraySettings settings,
        MonitorInfo monitor)
    {
        return OverlayPositionCoordinator.ResolveEffectiveState(
            settings,
            monitor,
            out _);
    }

    private static void AssertPreset(
        OverlayPositionState state,
        OverlayPositionPreset expected)
    {
        var preset = Assert.IsType<OverlayPositionState.Preset>(state);
        Assert.Equal(expected, preset.Value);
    }

    private static void AssertCustom(
        OverlayPositionState state,
        double expectedX,
        double expectedY)
    {
        var custom = Assert.IsType<OverlayPositionState.Custom>(state);
        Assert.Equal(expectedX, custom.XRatio, 6);
        Assert.Equal(expectedY, custom.YRatio, 6);
    }

    [Fact]
    public void Default_UsesSharedModeAndFullPresetState()
    {
        Assert.Equal(
            OverlayPositionMemoryMode.SharedAcrossDisplays,
            TraySettings.Default.PositionMemoryMode);
        AssertPreset(
            TraySettings.Default.SharedPosition!,
            OverlayPositionPreset.TopRight);
    }

    [Fact]
    public void SharedMode_UsesTheSameFullStateOnEveryDisplay()
    {
        var primary = Primary();
        var secondary = Secondary();
        var settings = Shared(
            new OverlayPositionState.Preset(OverlayPositionPreset.BottomLeft));

        AssertPreset(StateFor(settings, primary), OverlayPositionPreset.BottomLeft);
        AssertPreset(StateFor(settings, secondary), OverlayPositionPreset.BottomLeft);

        settings = Shared(new OverlayPositionState.Custom(0.25, 0.75));
        AssertCustom(StateFor(settings, primary), 0.25, 0.75);
        AssertCustom(StateFor(settings, secondary), 0.25, 0.75);
    }

    [Fact]
    public void PerDisplay_A_CustomThenPresetThenBThenA_ReturnsAFullPreset()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings, 300, 240, WindowWidth, WindowHeight, monitors, primary);
        AssertCustom(StateFor(settings, primary), 300.0 / 1720.0, 240.0 / 960.0);

        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.BottomRight,
            300,
            240,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);
        AssertPreset(StateFor(settings, primary), OverlayPositionPreset.BottomRight);

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings, 2200, 360, WindowWidth, WindowHeight, monitors, primary);
        AssertCustom(StateFor(settings, secondary), 280.0 / 1720.0, 360.0 / 960.0);
        AssertPreset(StateFor(settings, primary), OverlayPositionPreset.BottomRight);
    }

    [Fact]
    public void PerDisplay_DragUpdatesInheritanceSeedForFirstSeenMonitor()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings,
            300,
            240,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        AssertCustom(settings.SharedPosition!, 300.0 / 1720.0, 240.0 / 960.0);

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings with { OverlayTargetMonitorId = null },
            2200,
            100,
            WindowWidth,
            WindowHeight,
            monitors,
            primary,
            Margin);

        Assert.NotNull(updated);
        AssertCustom(
            updated!.PerDisplayPositions![MonitorId.Resolve(secondary)],
            300.0 / 1720.0,
            240.0 / 960.0);
    }

    [Fact]
    public void PerDisplay_PresetUpdatesInheritanceSeedForFirstSeenMonitor()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.BottomLeft,
            300,
            240,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        AssertPreset(settings.SharedPosition!, OverlayPositionPreset.BottomLeft);

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings with { OverlayTargetMonitorId = null },
            2200,
            100,
            WindowWidth,
            WindowHeight,
            monitors,
            primary,
            Margin);

        Assert.NotNull(updated);
        AssertPreset(
            updated!.PerDisplayPositions![MonitorId.Resolve(secondary)],
            OverlayPositionPreset.BottomLeft);
    }

    [Fact]
    public void PerDisplay_ExistingMonitorRecordIsUnaffectedByLaterChangeToA()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        var (_, _, inherited) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            2200,
            100,
            WindowWidth,
            WindowHeight,
            monitors,
            primary,
            Margin);
        settings = inherited!;
        AssertPreset(StateFor(settings, secondary), OverlayPositionPreset.TopRight);

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings,
            300,
            240,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        AssertCustom(settings.SharedPosition!, 300.0 / 1720.0, 240.0 / 960.0);
        AssertPreset(StateFor(settings, secondary), OverlayPositionPreset.TopRight);
    }

    [Fact]
    public void UserPositionChange_CancelsPendingPresetMigrationTarget()
    {
        var primary = Primary();
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight)) with
        {
            PendingPresetMigrationTarget = "legacy-target"
        };

        var afterDrag = OverlayPositionCoordinator.SaveFromDragEnd(
            settings,
            300,
            240,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary);
        Assert.Null(afterDrag.PendingPresetMigrationTarget);
        AssertCustom(afterDrag.SharedPosition!, 300.0 / 1720.0, 240.0 / 960.0);

        var afterPreset = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.BottomLeft,
            300,
            240,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary);
        Assert.Null(afterPreset.PendingPresetMigrationTarget);
        AssertPreset(afterPreset.SharedPosition!, OverlayPositionPreset.BottomLeft);
    }

    [Fact]
    public void PerDisplay_TopRightAndCustomRemainIndependentAcrossSwitches()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.TopRight,
            300,
            240,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);
        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings,
            2200,
            360,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        AssertPreset(StateFor(settings, primary), OverlayPositionPreset.TopRight);
        AssertCustom(StateFor(settings, secondary), 280.0 / 1720.0, 360.0 / 960.0);
    }

    [Fact]
    public void PerDisplay_TwoPresetDisplaysKeepDifferentPresets()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.BottomLeft,
            300,
            240,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);
        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.TopRight,
            2200,
            360,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        AssertPreset(StateFor(settings, primary), OverlayPositionPreset.BottomLeft);
        AssertPreset(StateFor(settings, secondary), OverlayPositionPreset.TopRight);
    }

    [Fact]
    public void PerDisplay_PresetOverwritesCustom_AndDragOverwritesPreset()
    {
        var primary = Primary();
        var monitors = new List<MonitorInfo> { primary };
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings, 400, 300, WindowWidth, WindowHeight, monitors, primary);
        Assert.IsType<OverlayPositionState.Custom>(StateFor(settings, primary));
        AssertCustom(settings.SharedPosition!, 400.0 / 1720.0, 300.0 / 960.0);

        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.TopLeft,
            400,
            300,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);
        AssertPreset(StateFor(settings, primary), OverlayPositionPreset.TopLeft);
        AssertPreset(settings.SharedPosition!, OverlayPositionPreset.TopLeft);

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings, 700, 500, WindowWidth, WindowHeight, monitors, primary);
        AssertCustom(StateFor(settings, primary), 700.0 / 1720.0, 500.0 / 960.0);
        AssertCustom(settings.SharedPosition!, 700.0 / 1720.0, 500.0 / 960.0);
    }

    [Fact]
    public void PerDisplay_NewMonitorInheritsPresetAndPersistsRecord()
    {
        var primary = Primary();
        var secondary = Secondary();
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.MiddleLeft));

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            2200,
            100,
            WindowWidth,
            WindowHeight,
            Both(primary, secondary),
            primary,
            Margin);

        Assert.NotNull(updated);
        AssertPreset(StateFor(updated!, secondary), OverlayPositionPreset.MiddleLeft);
        Assert.True(updated!.PerDisplayPositions!.ContainsKey(MonitorId.Resolve(secondary)));
    }

    [Fact]
    public void PerDisplay_NewMonitorInheritsCustomAndPersistsRecord()
    {
        var primary = Primary();
        var secondary = Secondary();
        var settings = PerDisplay(new OverlayPositionState.Custom(0.3, 0.7));

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            2200,
            100,
            WindowWidth,
            WindowHeight,
            Both(primary, secondary),
            primary,
            Margin);

        Assert.NotNull(updated);
        AssertCustom(StateFor(updated!, secondary), 0.3, 0.7);
        Assert.True(updated!.PerDisplayPositions!.ContainsKey(MonitorId.Resolve(secondary)));
    }

    [Fact]
    public void PerDisplay_DisconnectKeepsRecordAndReconnectRestoresIt()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings, 2350, 400, WindowWidth, WindowHeight, monitors, primary);
        string secondaryId = MonitorId.Resolve(secondary);
        Assert.Equal(secondaryId, settings.OverlayTargetMonitorId);
        AssertCustom(StateFor(settings, secondary), 430.0 / 1720.0, 400.0 / 960.0);

        var (_, _, disconnectedUpdate) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            100,
            100,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary,
            Margin);

        TraySettings disconnected = disconnectedUpdate ?? settings;
        Assert.Equal(secondaryId, disconnected.OverlayTargetMonitorId);
        Assert.True(disconnected.PerDisplayPositions!.ContainsKey(secondaryId));

        var (left, top, _) = OverlayPositionCoordinator.ResolveRestorePosition(
            disconnected,
            100,
            100,
            WindowWidth,
            WindowHeight,
            monitors,
            primary,
            Margin);

        Assert.Equal(2350, left, 5);
        Assert.Equal(400, top, 5);
    }

    [Fact]
    public void StableIdsKeepTwoDisplay1TopologiesIndependent()
    {
        var internalMonitor = SameDeviceNamePrimary();
        var externalMonitor = SameDeviceNameExternal();
        var monitors = Both(internalMonitor, externalMonitor);
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings, 300, 250, WindowWidth, WindowHeight, monitors, internalMonitor);
        settings = OverlayPositionCoordinator.SaveFromPresetSelection(
            settings,
            OverlayPositionPreset.BottomLeft,
            2200,
            300,
            WindowWidth,
            WindowHeight,
            monitors,
            internalMonitor);

        Assert.Equal("physical-internal", MonitorId.Resolve(internalMonitor));
        Assert.Equal("physical-external", MonitorId.Resolve(externalMonitor));
        Assert.NotEqual(
            MonitorId.Resolve(internalMonitor),
            MonitorId.Resolve(externalMonitor));
        AssertCustom(StateFor(settings, internalMonitor), 300.0 / 1720.0, 250.0 / 960.0);
        AssertPreset(StateFor(settings, externalMonitor), OverlayPositionPreset.BottomLeft);
        Assert.Equal(2, settings.PerDisplayPositions!.Count);
    }

    [Fact]
    public void LegacyDeviceNameRecordMigratesToStableIdAndDropsLegacyKey()
    {
        var monitor = Primary();
        var legacyDeviceName = monitor.DeviceName;
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
            new Dictionary<string, OverlayPositionState>
            {
                [legacyDeviceName] = new OverlayPositionState.Custom(0.2, 0.8)
            });

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            400,
            300,
            WindowWidth,
            WindowHeight,
            new[] { monitor },
            monitor,
            Margin);

        Assert.NotNull(updated);
        AssertCustom(updated!.PerDisplayPositions![MonitorId.Resolve(monitor)], 0.2, 0.8);
        Assert.False(updated.PerDisplayPositions.ContainsKey(legacyDeviceName));
    }

    [Fact]
    public void AmbiguousLegacyDeviceNameRecordRemainsReadOnlyUntilNameIsUnique()
    {
        var internalMonitor = SameDeviceNamePrimary();
        var externalMonitor = SameDeviceNameExternal();
        var legacyDeviceName = internalMonitor.DeviceName;
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
            new Dictionary<string, OverlayPositionState>
            {
                [legacyDeviceName] = new OverlayPositionState.Custom(0.2, 0.8)
            });

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            400,
            300,
            WindowWidth,
            WindowHeight,
            new[] { internalMonitor, externalMonitor },
            internalMonitor,
            Margin);

        Assert.Null(updated);
        AssertCustom(
            OverlayPositionCoordinator.ResolveEffectiveState(settings, internalMonitor, out _),
            0.2,
            0.8);
        Assert.True(settings.PerDisplayPositions!.ContainsKey(legacyDeviceName));
        Assert.False(settings.PerDisplayPositions.ContainsKey(internalMonitor.StableId!));
        Assert.False(settings.PerDisplayPositions.ContainsKey(externalMonitor.StableId!));
    }

    [Fact]
    public void SwitchingSharedToPerDisplayPreservesCurrentFullStateWithoutJump()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var settings = Shared(new OverlayPositionState.Custom(0.3, 0.7));

        var switched = OverlayPositionCoordinator.SwitchMemoryMode(
            settings,
            OverlayPositionMemoryMode.RememberPerDisplay,
            2436,
            672,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        Assert.Equal(OverlayPositionMemoryMode.RememberPerDisplay, switched.PositionMemoryMode);
        AssertCustom(StateFor(switched, secondary), 0.3, 0.7);

        var (left, top, _) = OverlayPositionCoordinator.ResolveRestorePosition(
            switched,
            2436,
            672,
            WindowWidth,
            WindowHeight,
            monitors,
            primary,
            Margin);

        Assert.Equal(2436, left, 5);
        Assert.Equal(672, top, 5);
    }

    [Fact]
    public void SwitchingPerDisplayToSharedUsesCurrentDisplayAndPreservesRecords()
    {
        var primary = Primary();
        var secondary = Secondary();
        var monitors = Both(primary, secondary);
        var records = new Dictionary<string, OverlayPositionState>
        {
            [MonitorId.Resolve(primary)] =
                new OverlayPositionState.Preset(OverlayPositionPreset.TopLeft),
            [MonitorId.Resolve(secondary)] =
                new OverlayPositionState.Custom(0.8, 0.2)
        };
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
            records);

        var switched = OverlayPositionCoordinator.SwitchMemoryMode(
            settings,
            OverlayPositionMemoryMode.SharedAcrossDisplays,
            3296,
            192,
            WindowWidth,
            WindowHeight,
            monitors,
            primary);

        Assert.Equal(OverlayPositionMemoryMode.SharedAcrossDisplays, switched.PositionMemoryMode);
        AssertCustom(switched.SharedPosition!, 0.8, 0.2);
        Assert.Equal(2, switched.PerDisplayPositions!.Count);
        AssertPreset(
            switched.PerDisplayPositions[MonitorId.Resolve(primary)],
            OverlayPositionPreset.TopLeft);
    }

    [Fact]
    public void GetActivePreset_ReturnsNullForCustomAndPresetForCurrentRecord()
    {
        var primary = Primary();
        var settings = PerDisplay(
            new OverlayPositionState.Preset(OverlayPositionPreset.TopRight));

        Assert.Equal(
            OverlayPositionPreset.TopRight,
            OverlayPositionCoordinator.GetActivePreset(settings, primary));

        settings = OverlayPositionCoordinator.SaveFromDragEnd(
            settings,
            500,
            300,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary);

        Assert.Null(OverlayPositionCoordinator.GetActivePreset(settings, primary));
    }

    [Fact]
    public void LegacyAbsoluteCoordinatesMigrateToSharedCustomState()
    {
        var primary = Primary();
        var settings = TraySettings.Default with
        {
            SharedPosition = null,
            OverlayLeft = 500,
            OverlayTop = 300
        };

        var migrated = OverlayPositionCoordinator.MigrateLegacy(
            settings,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary);

        AssertCustom(migrated.SharedPosition!, 500.0 / 1720.0, 300.0 / 960.0);
        Assert.Null(migrated.OverlayLeft);
        Assert.Null(migrated.OverlayTop);
        Assert.Equal(MonitorId.Resolve(primary), migrated.OverlayTargetMonitorId);
    }

    [Fact]
    public void RestoreOfExistingStateDoesNotWriteSettings()
    {
        var primary = Primary();
        var settings = Shared(new OverlayPositionState.Custom(0.5, 0.25));

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            500,
            300,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary,
            Margin);

        Assert.Null(updated);
    }

    [Fact]
    public void RepeatedRestoreIsIdempotentAndDoesNotCreateDragSemantics()
    {
        var primary = Primary();
        var settings = Shared(new OverlayPositionState.Custom(0.5, 0.25));

        var first = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            500,
            300,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary,
            Margin);
        var second = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            500,
            300,
            WindowWidth,
            WindowHeight,
            new[] { primary },
            primary,
            Margin);

        Assert.Equal(first.Left, second.Left);
        Assert.Equal(first.Top, second.Top);
        Assert.Null(first.UpdatedSettings);
        Assert.Null(second.UpdatedSettings);
    }

    [Fact]
    public void CustomStateRatiosClampToUnitInterval()
    {
        var state = new OverlayPositionState.Custom(-1.0, 2.0);

        AssertCustom(state, 0.0, 1.0);
    }

    [Fact]
    public void CustomStateNonFiniteRatiosFallBackSafely()
    {
        var state = new OverlayPositionState.Custom(
            double.NaN,
            double.PositiveInfinity);

        AssertCustom(state, 0.0, 0.0);
    }

    [Fact]
    public void SameDeviceNameWithoutStableIdFallsBackToDeviceName()
    {
        var monitor = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true,
            StableId: null);

        Assert.Equal(@"\\.\DISPLAY1", MonitorId.Resolve(monitor));
    }
}
