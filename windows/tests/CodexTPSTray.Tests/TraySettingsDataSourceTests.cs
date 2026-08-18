using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

// Phase 2 tests for the DataSource field on TraySettings and its persistence.
// Covers: source-compat defaults, save/load round-trip, legacy migration,
// per-field fallback (other settings preserved when DataSource is invalid),
// no path leakage, and DataSource preservation across every OverlayPosition
// migration branch in TraySettingsStore.Load.
public class TraySettingsDataSourceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsPath;

    public TraySettingsDataSourceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CodexTPSTest_{Guid.NewGuid()}");
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

    // ---------------------------------------------------------------------
    // TraySettings defaults & source compatibility
    // ---------------------------------------------------------------------

    [Fact]
    public void LegacyTwoArgConstructor_DefaultsToWindowsDataSource()
    {
        // The pre-Phase-2 call shape `new TraySettings(window, cadence)` must
        // keep compiling and yield a non-null Windows DataSource.
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds);

        Assert.NotNull(settings.DataSource);
        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Default_IsWindowsDataSource()
    {
        Assert.Equal(CodexDataSourceKind.Windows, TraySettings.Default.DataSource.Kind);
        Assert.Null(TraySettings.Default.DataSource.WslDistributionName);
    }

    [Fact]
    public void FullConstructor_SetsWslDataSource()
    {
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            Language.English,
            OverlayEnabled: false,
            OverlayLocked: false,
            OverlayLeft: null,
            OverlayTop: null,
            ApplicationTheme: ApplicationThemePreference.System,
            OverlayTheme: OverlayThemePreference.FollowApplication,
            OverlayOpacity: OverlayOpacityPreference.Default,
            PositionMemoryMode: OverlayPositionMemoryMode.SharedAcrossDisplays,
            SharedPosition: new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
            PerDisplayPositions: null,
            OverlayTargetMonitorId: null,
            DataSource: CodexDataSourceSelection.ForWsl("Ubuntu"));

        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void FullConstructor_RejectsNullDataSource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TraySettings(
                MetricWindow.OneMinute,
                RefreshCadence.FifteenSeconds,
                Language.English,
                OverlayEnabled: false,
                OverlayLocked: false,
                OverlayLeft: null,
                OverlayTop: null,
                ApplicationTheme: ApplicationThemePreference.System,
                OverlayTheme: OverlayThemePreference.FollowApplication,
                OverlayOpacity: OverlayOpacityPreference.Default,
                PositionMemoryMode: OverlayPositionMemoryMode.SharedAcrossDisplays,
                SharedPosition: new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
                PerDisplayPositions: null,
                OverlayTargetMonitorId: null,
                DataSource: null!));
    }

    [Fact]
    public void WithExpression_CanChangeDataSourceAndPreservesOtherFields()
    {
        var windows = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds);
        var wsl = windows with { DataSource = CodexDataSourceSelection.ForWsl("Ubuntu") };

        Assert.Equal(CodexDataSourceKind.Windows, windows.DataSource.Kind);
        Assert.Equal(CodexDataSourceKind.Wsl, wsl.DataSource.Kind);
        Assert.Equal("Ubuntu", wsl.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.OneMinute, wsl.SelectedWindow);
        Assert.Equal(RefreshCadence.FifteenSeconds, wsl.RefreshCadence);
    }

    [Fact]
    public void WithExpression_NullDataSource_ThrowsArgumentNullException()
    {
        // The custom init accessor must reject null, closing the
        // `with { DataSource = null! }` bypass that an auto-implemented init
        // accessor would otherwise allow.
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds);

        Assert.Throws<ArgumentNullException>(() => settings with { DataSource = null! });
    }

    [Fact]
    public void WithExpression_NullDataSource_LeavesOriginalUntouched()
    {
        // `with` operates on a copy, so a failed assignment must never mutate
        // the source object. The original keeps its Windows selection.
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds);

        try
        {
            _ = settings with { DataSource = null! };
        }
        catch (ArgumentNullException)
        {
            // expected
        }

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
    }

    // ---------------------------------------------------------------------
    // Save / load round-trip
    // ---------------------------------------------------------------------

    [Fact]
    public void TrySave_WindowsDataSource_RoundTrips()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.ThirtySeconds)
            with
        {
            DataSource = CodexDataSourceSelection.Windows
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(CodexDataSourceKind.Windows, loaded.DataSource.Kind);
        Assert.Null(loaded.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.FiveMinutes, loaded.SelectedWindow);
    }

    [Fact]
    public void TrySave_WslDataSource_RoundTrips()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.ThirtySeconds)
            with
        {
            DataSource = CodexDataSourceSelection.ForWsl("Ubuntu")
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(CodexDataSourceKind.Wsl, loaded.DataSource.Kind);
        Assert.Equal("Ubuntu", loaded.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.FiveMinutes, loaded.SelectedWindow);
    }

    [Fact]
    public void TrySave_WslDataSource_PersistsOnlyKindAndDistributionName()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds)
            with
        {
            DataSource = CodexDataSourceSelection.ForWsl("Ubuntu")
        };

        Assert.True(store.TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        using var doc = JsonDocument.Parse(json);
        var ds = doc.RootElement.GetProperty("DataSource");
        var names = ds.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "Kind", "WslDistributionName" }, names);
        Assert.Equal("Wsl", ds.GetProperty("Kind").GetString());
        Assert.Equal("Ubuntu", ds.GetProperty("WslDistributionName").GetString());
    }

    [Fact]
    public void TrySave_WslDataSource_StoresNormalizedDistributionName()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        // ForWsl trims, so "  Ubuntu  " is stored as "Ubuntu" on disk.
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds)
            with
        {
            DataSource = CodexDataSourceSelection.ForWsl("  Ubuntu  ")
        };

        Assert.True(store.TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Ubuntu", doc.RootElement.GetProperty("DataSource")
            .GetProperty("WslDistributionName").GetString());
    }

    // ---------------------------------------------------------------------
    // Legacy migration
    // ---------------------------------------------------------------------

    [Fact]
    public void Load_LegacyJsonWithoutDataSource_MigratesToWindows()
    {
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"Chinese\"}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(Language.Chinese, settings.Language);
    }

    // ---------------------------------------------------------------------
    // Per-field fallback: invalid DataSource → Windows only, others preserved
    // ---------------------------------------------------------------------

    [Fact]
    public void Load_UnknownKind_FallsBackToWindows_PreservesOtherSettings()
    {
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"Chinese\"," +
            "\"DataSource\":{\"Kind\":\"MacOS\",\"WslDistributionName\":null}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(Language.Chinese, settings.Language);
    }

    [Fact]
    public void Load_WindowsWithDistributionName_FallsBackToWindows()
    {
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"DataSource\":{\"Kind\":\"Windows\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.OneMinute, settings.SelectedWindow);
    }

    [Fact]
    public void Load_WslWithoutDistributionName_FallsBackToWindows()
    {
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":null}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.OneMinute, settings.SelectedWindow);
    }

    [Fact]
    public void Load_WslWithInvalidDistributionName_FallsBackToWindows_PreservesOtherSettings()
    {
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"foo/bar\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Null(settings.DataSource.WslDistributionName);
        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_KindCaseInsensitive_AcceptsLowercaseWsl()
    {
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"DataSource\":{\"Kind\":\"wSl\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Load_DataSourceNotAnObject_FallsBackToWindows_PreservesOtherSettings()
    {
        // DataSource set to a bare string: must not crash the whole load.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"DataSource\":\"bogus\"}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_NumericKind_FallsBackToWindows_PreservesOtherSettings()
    {
        // Kind supplied as a number. Because the DTO stores DataSource as a raw
        // JsonElement, this must not crash the surrounding deserialization;
        // only DataSource falls back to Windows.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30," +
            "\"DataSource\":{\"Kind\":0,\"WslDistributionName\":null}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Equal(CodexDataSourceKind.Windows, settings.DataSource.Kind);
        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
    }

    // ---------------------------------------------------------------------
    // No path leakage
    // ---------------------------------------------------------------------

    [Fact]
    public void TrySave_NeverPersistsCodexHomeSessionsRootOrUncPaths()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds)
            with
        {
            DataSource = CodexDataSourceSelection.ForWsl("Ubuntu")
        };

        Assert.True(store.TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        Assert.DoesNotContain("CodexHome", json);
        Assert.DoesNotContain("SessionsRoot", json);
        Assert.DoesNotContain("wsl.localhost", json);
        Assert.DoesNotContain("/home/", json);
    }

    // ---------------------------------------------------------------------
    // DataSource preserved across every OverlayPosition migration branch
    // ---------------------------------------------------------------------

    [Fact]
    public void Load_InvalidPositionPreset_PreservesWslDataSource()
    {
        // Branch: OverlayPosition is an invalid string → TopRight fallback.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"OverlayPosition\":\"Invalid\"," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        var invalidPreset = Assert.IsType<OverlayPositionState.Preset>(settings.SharedPosition);
        Assert.Equal(OverlayPositionPreset.TopRight, invalidPreset.Value);
        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Load_LegacyCoordsMigration_PreservesWslDataSource()
    {
        // Branch: no OverlayPosition key, no device name, has coords → Custom.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"OverlayLeft\":400.0,\"OverlayTop\":500.0," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Null(settings.SharedPosition);
        Assert.Equal(400.0, settings.OverlayLeft);
        Assert.Equal(500.0, settings.OverlayTop);
        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Load_LegacyNoCoordsMigration_PreservesWslDataSource()
    {
        // Branch: no OverlayPosition key, no device name, no coords → TopRight.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"Debian\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        var defaultPreset = Assert.IsType<OverlayPositionState.Preset>(settings.SharedPosition);
        Assert.Equal(OverlayPositionPreset.TopRight, defaultPreset.Value);
        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Debian", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Load_ValidPositionPreset_PreservesWslDataSource()
    {
        // Branch: valid OverlayPosition preset → preset mode.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"OverlayPosition\":\"BottomLeft\"," +
            "\"OverlayMonitorDeviceName\":\"DISPLAY2\"," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        var preset = Assert.IsType<OverlayPositionState.Preset>(settings.SharedPosition);
        Assert.Equal(OverlayPositionPreset.BottomLeft, preset.Value);
        Assert.Equal("DISPLAY2", settings.OverlayTargetMonitorId);
        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Load_CustomModeWithDeviceNameAndCoords_PreservesWslDataSource()
    {
        // Branch: no OverlayPosition key, non-empty device name, has coords →
        // Custom mode (branch reached after the legacy-migration guard).
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"OverlayLeft\":100.0,\"OverlayTop\":200.0," +
            "\"OverlayMonitorDeviceName\":\"DISPLAY1\"," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        Assert.Null(settings.SharedPosition);
        Assert.Equal(100.0, settings.OverlayLeft);
        Assert.Equal(200.0, settings.OverlayTop);
        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }

    [Fact]
    public void Load_FinalFallbackWithDeviceNameNoCoords_PreservesWslDataSource()
    {
        // Branch: no OverlayPosition key, non-empty device name, no coords →
        // final TopRight fallback.
        File.WriteAllText(_settingsPath,
            "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15," +
            "\"OverlayMonitorDeviceName\":\"DISPLAY1\"," +
            "\"DataSource\":{\"Kind\":\"Wsl\",\"WslDistributionName\":\"Ubuntu\"}}");

        var settings = TraySettingsStore.CreateForTests(_settingsPath).Load();

        var fallbackPreset = Assert.IsType<OverlayPositionState.Preset>(settings.SharedPosition);
        Assert.Equal(OverlayPositionPreset.TopRight, fallbackPreset.Value);
        Assert.Equal(CodexDataSourceKind.Wsl, settings.DataSource.Kind);
        Assert.Equal("Ubuntu", settings.DataSource.WslDistributionName);
    }
}
