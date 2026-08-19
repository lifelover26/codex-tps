using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

public class TraySettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsPath;

    public TraySettingsStoreTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "CodexTPSTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsPath = Path.Combine(_tempDirectory, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private TraySettings Load()
    {
        return TraySettingsStore.CreateForTests(_settingsPath).Load();
    }

    private void Write(string json)
    {
        File.WriteAllText(_settingsPath, json);
    }

    private static void AssertPreset(
        OverlayPositionState? state,
        OverlayPositionPreset expected)
    {
        var preset = Assert.IsType<OverlayPositionState.Preset>(state);
        Assert.Equal(expected, preset.Value);
    }

    private static void AssertCustom(
        OverlayPositionState? state,
        double expectedX,
        double expectedY)
    {
        var custom = Assert.IsType<OverlayPositionState.Custom>(state);
        Assert.Equal(expectedX, custom.XRatio, 6);
        Assert.Equal(expectedY, custom.YRatio, 6);
    }

    [Fact]
    public void MissingFile_ReturnsCompleteDefaultPositionState()
    {
        var settings = Load();

        Assert.Equal(
            OverlayPositionMemoryMode.SharedAcrossDisplays,
            settings.PositionMemoryMode);
        AssertPreset(settings.SharedPosition, OverlayPositionPreset.TopRight);
        Assert.Null(settings.PerDisplayPositions);
    }

    [Fact]
    public void GeneralSettings_LoadAndSaveRoundTrip()
    {
        Write(
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30," +
            "\"Language\":\"Chinese\",\"OverlayEnabled\":true,\"OverlayLocked\":true," +
            "\"ApplicationTheme\":\"Dark\",\"OverlayTheme\":\"Light\"," +
            "\"OverlayOpacity\":\"Percent70\"}");

        var loaded = Load();

        Assert.Equal(MetricWindow.FiveMinutes, loaded.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, loaded.RefreshCadence);
        Assert.Equal(Language.Chinese, loaded.Language);
        Assert.True(loaded.OverlayEnabled);
        Assert.True(loaded.OverlayLocked);
        Assert.Equal(ApplicationThemePreference.Dark, loaded.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.Light, loaded.OverlayTheme);
        Assert.Equal(OverlayOpacityPreference.Percent70, loaded.OverlayOpacity);

        var saveSettings = loaded with { SelectedWindow = MetricWindow.OneHour };
        Assert.True(TraySettingsStore.CreateForTests(_settingsPath).TrySave(saveSettings));
        Assert.Equal(MetricWindow.OneHour, Load().SelectedWindow);
    }

    [Fact]
    public void InvalidJson_ReturnsDefaultsWithoutThrowing()
    {
        Write("{invalid json");

        var settings = Load();

        Assert.Equal(TraySettings.Default.SelectedWindow, settings.SelectedWindow);
        AssertPreset(settings.SharedPosition, OverlayPositionPreset.TopRight);
    }

    [Fact]
    public void UnknownGeneralValues_FallBackPerField()
    {
        Write(
            "{\"SelectedWindow\":\"Unknown\",\"RefreshCadence\":999," +
            "\"Language\":\"Unknown\",\"ApplicationTheme\":\"Unknown\"," +
            "\"OverlayTheme\":\"Unknown\",\"OverlayOpacity\":\"Unknown\"}");

        var settings = Load();

        Assert.Equal(TraySettings.Default.SelectedWindow, settings.SelectedWindow);
        Assert.Equal(TraySettings.Default.RefreshCadence, settings.RefreshCadence);
        Assert.Equal(TraySettings.Default.Language, settings.Language);
        Assert.Equal(TraySettings.Default.ApplicationTheme, settings.ApplicationTheme);
        Assert.Equal(TraySettings.Default.OverlayTheme, settings.OverlayTheme);
        Assert.Equal(TraySettings.Default.OverlayOpacity, settings.OverlayOpacity);
    }

    [Fact]
    public void NewModel_PresetAndPerDisplayStatesRoundTrip()
    {
        var primaryId = "physical-primary";
        var secondaryId = "physical-secondary";
        var settings = TraySettings.Default with
        {
            PositionMemoryMode = OverlayPositionMemoryMode.RememberPerDisplay,
            SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.BottomRight),
            PerDisplayPositions = new Dictionary<string, OverlayPositionState>
            {
                [primaryId] = new OverlayPositionState.Custom(0.2, 0.8),
                [secondaryId] = new OverlayPositionState.Preset(OverlayPositionPreset.TopLeft)
            },
            OverlayTargetMonitorId = secondaryId
        };

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        Assert.True(store.TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        Assert.Contains("\"PositionMemoryMode\"", json);
        Assert.Contains("\"SharedPosition\"", json);
        Assert.Contains("\"PerDisplayPositions\"", json);
        Assert.Contains("\"Kind\": \"Preset\"", json);
        Assert.Contains("\"Preset\": \"BottomRight\"", json);
        Assert.Contains("\"Kind\": \"Custom\"", json);
        Assert.DoesNotContain("\"OverlayPosition\"", json);
        Assert.DoesNotContain("\"OverlayCustomPositionMode\"", json);
        Assert.DoesNotContain("\"OverlayPerDisplayPositions\"", json);

        var loaded = Load();

        Assert.Equal(
            OverlayPositionMemoryMode.RememberPerDisplay,
            loaded.PositionMemoryMode);
        AssertPreset(loaded.SharedPosition, OverlayPositionPreset.BottomRight);
        AssertCustom(loaded.PerDisplayPositions![primaryId], 0.2, 0.8);
        AssertPreset(
            loaded.PerDisplayPositions[secondaryId],
            OverlayPositionPreset.TopLeft);
        Assert.Equal(secondaryId, loaded.OverlayTargetMonitorId);
    }

    [Fact]
    public void NewModel_CustomSharedStateRoundTripsWithoutPreset()
    {
        var settings = TraySettings.Default with
        {
            SharedPosition = new OverlayPositionState.Custom(0.35, 0.65),
            PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays
        };

        Assert.True(
            TraySettingsStore.CreateForTests(_settingsPath).TrySave(settings));

        var loaded = Load();

        AssertCustom(loaded.SharedPosition, 0.35, 0.65);
        Assert.Null(loaded.PendingPresetMigrationTarget);
    }

    [Fact]
    public void NewModel_InvalidStateFallsBackSafelyAndPreservesOtherFields()
    {
        Write(
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30," +
            "\"PositionMemoryMode\":123,\"SharedPosition\":\"bad\"," +
            "\"PerDisplayPositions\":[]," +
            "\"OverlayTargetMonitorId\":42," +
            "\"PendingPresetMigrationTarget\":false}");

        var settings = Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(
            OverlayPositionMemoryMode.SharedAcrossDisplays,
            settings.PositionMemoryMode);
        AssertPreset(settings.SharedPosition, OverlayPositionPreset.TopRight);
        Assert.Null(settings.OverlayTargetMonitorId);
        Assert.Null(settings.PendingPresetMigrationTarget);
    }

    [Fact]
    public void NewModel_NonFiniteRatioTextFallsBackSafely()
    {
        Write(
            "{\"PositionMemoryMode\":\"SharedAcrossDisplays\"," +
            "\"SharedPosition\":{" +
            "\"Kind\":\"Custom\",\"XRatio\":\"NaN\",\"YRatio\":\"Infinity\"}}");

        var settings = Load();

        AssertPreset(settings.SharedPosition, OverlayPositionPreset.TopRight);
    }

    [Fact]
    public void LegacyKeepRelative_GlobalRatiosMigrateToSharedCustom()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"KeepRelative\"," +
            "\"OverlayXRatio\":0.25,\"OverlayYRatio\":0.75}");

        var settings = Load();

        Assert.Equal(
            OverlayPositionMemoryMode.SharedAcrossDisplays,
            settings.PositionMemoryMode);
        AssertCustom(settings.SharedPosition, 0.25, 0.75);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void LegacyRememberPerDisplay_RatioRecordsBecomeFullCustomStates()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"RememberPerDisplay\"," +
            "\"OverlayPerDisplayPositions\":{" +
            "\"physical-a\":{\"XRatio\":0.1,\"YRatio\":0.2}," +
            "\"physical-b\":{\"XRatio\":0.8,\"YRatio\":0.9}}}");

        var settings = Load();

        Assert.Equal(
            OverlayPositionMemoryMode.RememberPerDisplay,
            settings.PositionMemoryMode);
        AssertCustom(settings.PerDisplayPositions!["physical-a"], 0.1, 0.2);
        AssertCustom(settings.PerDisplayPositions["physical-b"], 0.8, 0.9);
        AssertPreset(settings.SharedPosition, OverlayPositionPreset.TopRight);
    }

    [Fact]
    public void LegacyRememberPresetWinsForTargetAfterLiveIdentityMatches()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"RememberPerDisplay\"," +
            "\"OverlayPosition\":\"BottomLeft\"," +
            "\"OverlayCustomMonitorId\":\"physical-a\"," +
            "\"OverlayPerDisplayPositions\":{" +
            "\"physical-a\":{\"XRatio\":0.1,\"YRatio\":0.2}}}");

        var settings = Load();

        Assert.Equal(
            OverlayPositionMemoryMode.RememberPerDisplay,
            settings.PositionMemoryMode);
        AssertPreset(settings.SharedPosition, OverlayPositionPreset.BottomLeft);
        Assert.Equal("physical-a", settings.PendingPresetMigrationTarget);
        AssertCustom(settings.PerDisplayPositions!["physical-a"], 0.1, 0.2);

        var monitor = new MonitorInfo(
            @"\\.\DISPLAY1",
            new Rect(0, 0, 1920, 1080),
            true,
            StableId: "physical-a");

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            100,
            100,
            200,
            120,
            new[] { monitor },
            monitor,
            new Thickness(16));

        Assert.NotNull(updated);
        Assert.Null(updated!.PendingPresetMigrationTarget);
        AssertPreset(
            updated.PerDisplayPositions!["physical-a"],
            OverlayPositionPreset.BottomLeft);
    }

    [Fact]
    public void LegacyPresetWithAmbiguousDeviceNameDefersWithoutWritingWrongRecord()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"RememberPerDisplay\"," +
            "\"OverlayPosition\":\"TopLeft\"," +
            "\"OverlayMonitorDeviceName\":\"\\\\\\\\.\\\\DISPLAY1\"}");

        var settings = Load();
        var internalMonitor = new MonitorInfo(
            @"\\.\DISPLAY1",
            new Rect(0, 0, 1920, 1080),
            true,
            StableId: "physical-internal");
        var externalMonitor = new MonitorInfo(
            @"\\.\DISPLAY1",
            new Rect(1920, 0, 1920, 1080),
            false,
            StableId: "physical-external");

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            2200,
            100,
            200,
            120,
            new[] { internalMonitor, externalMonitor },
            internalMonitor,
            new Thickness(16));

        Assert.Null(updated);
        Assert.Equal(
            @"\\.\DISPLAY1",
            settings.PendingPresetMigrationTarget);
        Assert.Null(settings.PerDisplayPositions);
    }

    [Fact]
    public void LegacyPresetDeviceNameRecordMigratesToStableKey()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"RememberPerDisplay\"," +
            "\"OverlayPosition\":\"TopLeft\"," +
            "\"OverlayMonitorDeviceName\":\"\\\\\\\\.\\\\DISPLAY1\"," +
            "\"OverlayPerDisplayPositions\":{" +
            "\"\\\\\\\\.\\\\DISPLAY1\":{\"XRatio\":0.1,\"YRatio\":0.2}}}");

        var settings = Load();
        var monitor = new MonitorInfo(
            @"\\.\DISPLAY1",
            new Rect(0, 0, 1920, 1080),
            true,
            StableId: "physical-a");

        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            100,
            100,
            200,
            120,
            new[] { monitor },
            monitor,
            new Thickness(16));

        Assert.NotNull(updated);
        AssertPreset(updated!.PerDisplayPositions!["physical-a"], OverlayPositionPreset.TopLeft);
        Assert.False(updated.PerDisplayPositions!.ContainsKey(@"\\.\DISPLAY1"));
    }

    [Fact]
    public void LegacyAbsoluteCoordinatesRemainUntilLiveMigration()
    {
        Write(
            "{\"OverlayLeft\":400,\"OverlayTop\":500}");

        var settings = Load();

        Assert.Null(settings.SharedPosition);
        Assert.Equal(400, settings.OverlayLeft);
        Assert.Equal(500, settings.OverlayTop);

        var monitor = new MonitorInfo(
            @"\\.\DISPLAY1",
            new Rect(0, 0, 1920, 1080),
            true,
            StableId: "physical-primary");
        var (_, _, updated) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            400,
            500,
            200,
            120,
            new[] { monitor },
            monitor,
            new Thickness(16));

        Assert.NotNull(updated);
        AssertCustom(updated!.SharedPosition, 400.0 / 1720.0, 500.0 / 960.0);
        Assert.Null(updated.OverlayLeft);
        Assert.Null(updated.OverlayTop);
    }

    [Fact]
    public void LegacyRatiosAreClampedAndInvalidRecordsAreSkipped()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"RememberPerDisplay\"," +
            "\"OverlayXRatio\":-1,\"OverlayYRatio\":2," +
            "\"OverlayPerDisplayPositions\":{" +
            "\"valid\":{\"XRatio\":-1,\"YRatio\":2}," +
            "\"invalid\":{\"XRatio\":\"bad\",\"YRatio\":0.2}}}");

        var settings = Load();

        AssertCustom(settings.SharedPosition, 0, 1);
        AssertCustom(settings.PerDisplayPositions!["valid"], 0, 1);
        Assert.False(settings.PerDisplayPositions!.ContainsKey("invalid"));
    }

    [Fact]
    public void SaveOfMigratedLegacySettingsUsesOnlyNewPositionAuthority()
    {
        Write(
            "{\"OverlayCustomPositionMode\":\"RememberPerDisplay\"," +
            "\"OverlayXRatio\":0.4,\"OverlayYRatio\":0.6," +
            "\"OverlayCustomMonitorId\":\"physical-a\"}");

        var settings = Load();
        Assert.True(
            TraySettingsStore.CreateForTests(_settingsPath).TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        Assert.Contains("\"PositionMemoryMode\"", json);
        Assert.Contains("\"SharedPosition\"", json);
        Assert.DoesNotContain("\"OverlayCustomPositionMode\"", json);
        Assert.DoesNotContain("\"OverlayXRatio\"", json);
        Assert.DoesNotContain("\"OverlayYRatio\"", json);
        Assert.DoesNotContain("\"OverlayCustomMonitorId\"", json);
    }

    [Fact]
    public void SaveWithPendingAbsoluteCoordinatesKeepsMigrationPayload()
    {
        var settings = TraySettings.Default with
        {
            SharedPosition = null,
            OverlayLeft = 111,
            OverlayTop = 222
        };

        Assert.True(
            TraySettingsStore.CreateForTests(_settingsPath).TrySave(settings));

        var loaded = Load();
        Assert.Null(loaded.SharedPosition);
        Assert.Equal(111, loaded.OverlayLeft);
        Assert.Equal(222, loaded.OverlayTop);
    }

    [Fact]
    public void AtomicSaveLeavesNoTemporaryFiles()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        Assert.True(store.TrySave(TraySettings.Default));
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp"));
    }

    [Fact]
    public void AppearanceMemory_DefaultIsSharedAcrossDisplays()
    {
        var settings = Load();

        Assert.Equal(
            OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            settings.AppearanceMemoryMode);
        Assert.Null(settings.PerDisplayAppearances);
    }

    [Fact]
    public void AppearanceMemory_RoundTrip()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = new OverlayAppearanceState(
                OverlayThemePreference.Dark,
                OverlayOpacityPreference.Percent70)
        };

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        Assert.True(store.TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        Assert.Contains("\"AppearanceMemoryMode\"", json);
        Assert.Contains("\"SharedAppearance\"", json);
        Assert.Contains("\"ThemePreference\"", json);
        Assert.Contains("\"OpacityPreference\"", json);

        var loaded = Load();

        Assert.Equal(
            OverlayAppearanceMemoryMode.RememberPerDisplay,
            loaded.AppearanceMemoryMode);
        Assert.NotNull(loaded.SharedAppearance);
        Assert.Equal(
            OverlayThemePreference.Dark,
            loaded.SharedAppearance!.ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Percent70,
            loaded.SharedAppearance.OpacityPreference);
    }

    [Fact]
    public void AppearanceMemory_PerDisplayAppearancesRoundTrip()
    {
        var primaryId = "physical-primary";
        var secondaryId = "physical-secondary";
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = new OverlayAppearanceState(
                OverlayThemePreference.Light,
                OverlayOpacityPreference.Default),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                [primaryId] = new OverlayAppearanceState(
                    OverlayThemePreference.Dark,
                    OverlayOpacityPreference.Percent85),
                [secondaryId] = new OverlayAppearanceState(
                    OverlayThemePreference.FollowApplication,
                    OverlayOpacityPreference.Opaque)
            }
        };

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        Assert.True(store.TrySave(settings));

        var loaded = Load();

        Assert.Equal(
            OverlayAppearanceMemoryMode.RememberPerDisplay,
            loaded.AppearanceMemoryMode);
        Assert.NotNull(loaded.PerDisplayAppearances);
        Assert.Equal(
            OverlayThemePreference.Dark,
            loaded.PerDisplayAppearances![primaryId].ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Percent85,
            loaded.PerDisplayAppearances[primaryId].OpacityPreference);
        Assert.Equal(
            OverlayThemePreference.FollowApplication,
            loaded.PerDisplayAppearances[secondaryId].ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Opaque,
            loaded.PerDisplayAppearances[secondaryId].OpacityPreference);
    }

    [Fact]
    public void AppearanceMemory_LegacyV041_MigratesSharedAppearanceFromScalars()
    {
        Write(
            "{\"OverlayTheme\":\"Dark\",\"OverlayOpacity\":\"Percent70\"}");

        var settings = Load();

        Assert.Equal(
            OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            settings.AppearanceMemoryMode);
        Assert.NotNull(settings.SharedAppearance);
        Assert.Equal(
            OverlayThemePreference.Dark,
            settings.SharedAppearance!.ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Percent70,
            settings.SharedAppearance.OpacityPreference);
        Assert.Null(settings.PerDisplayAppearances);
    }

    [Fact]
    public void AppearanceMemory_LegacyV041_FollowApplicationAndSystem_PreservedAsPreferences()
    {
        Write(
            "{\"OverlayTheme\":\"FollowApplication\",\"OverlayOpacity\":\"Default\"}");

        var settings = Load();

        Assert.NotNull(settings.SharedAppearance);
        Assert.Equal(
            OverlayThemePreference.FollowApplication,
            settings.SharedAppearance!.ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Default,
            settings.SharedAppearance.OpacityPreference);
    }

    [Fact]
    public void AppearanceMemory_InvalidModeFallsBackToSharedAcrossDisplays()
    {
        Write(
            "{\"AppearanceMemoryMode\":\"Bogus\"," +
            "\"SharedAppearance\":{\"ThemePreference\":\"Light\",\"OpacityPreference\":\"Percent40\"}}");

        var settings = Load();

        Assert.Equal(
            OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            settings.AppearanceMemoryMode);
        Assert.NotNull(settings.SharedAppearance);
        Assert.Equal(
            OverlayThemePreference.Light,
            settings.SharedAppearance!.ThemePreference);
    }

    [Fact]
    public void AppearanceMemory_InvalidSharedAppearanceFallsBackToScalarMigration()
    {
        Write(
            "{\"OverlayTheme\":\"Dark\",\"OverlayOpacity\":\"Opaque\"," +
            "\"SharedAppearance\":{\"ThemePreference\":\"NotATheme\"}}");

        var settings = Load();

        Assert.Equal(
            OverlayThemePreference.Dark,
            settings.OverlayTheme);
        Assert.Equal(
            OverlayOpacityPreference.Opaque,
            settings.OverlayOpacity);
        Assert.NotNull(settings.SharedAppearance);
        Assert.Equal(
            OverlayThemePreference.Dark,
            settings.SharedAppearance!.ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Opaque,
            settings.SharedAppearance.OpacityPreference);
    }

    [Fact]
    public void AppearanceMemory_InvalidPerDisplayEntriesAreSkipped()
    {
        Write(
            "{\"AppearanceMemoryMode\":\"RememberPerDisplay\"," +
            "\"SharedAppearance\":{\"ThemePreference\":\"Light\",\"OpacityPreference\":\"Default\"}," +
            "\"PerDisplayAppearances\":{" +
            "\"valid\":{\"ThemePreference\":\"Dark\",\"OpacityPreference\":\"Percent70\"}," +
            "\"invalid\":{\"ThemePreference\":\"NotATheme\",\"OpacityPreference\":\"Percent70\"}}}");

        var settings = Load();

        Assert.Equal(
            OverlayAppearanceMemoryMode.RememberPerDisplay,
            settings.AppearanceMemoryMode);
        Assert.NotNull(settings.PerDisplayAppearances);
        Assert.True(settings.PerDisplayAppearances!.ContainsKey("valid"));
        Assert.False(settings.PerDisplayAppearances.ContainsKey("invalid"));
        Assert.Equal(
            OverlayThemePreference.Dark,
            settings.PerDisplayAppearances["valid"].ThemePreference);
    }

    [Fact]
    public void AppearanceMemory_SaveRoundTripPreservesFollowApplicationPreference()
    {
        var settings = TraySettings.Default with
        {
            SharedAppearance = new OverlayAppearanceState(
                OverlayThemePreference.FollowApplication,
                OverlayOpacityPreference.Default)
        };

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        Assert.True(store.TrySave(settings));

        var loaded = Load();

        Assert.NotNull(loaded.SharedAppearance);
        Assert.Equal(
            OverlayThemePreference.FollowApplication,
            loaded.SharedAppearance!.ThemePreference);
        Assert.Equal(
            OverlayOpacityPreference.Default,
            loaded.SharedAppearance.OpacityPreference);
    }
}
