using System;
using System.IO;
using Xunit;

namespace CodexTPSTray.Tests;

public class TraySettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsPath;

    public TraySettingsStoreTests()
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

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(TraySettings.Default.SelectedWindow, settings.SelectedWindow);
        Assert.Equal(TraySettings.Default.RefreshCadence, settings.RefreshCadence);
    }

    [Fact]
    public void Load_ValidFile_ReturnsSettings()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "{invalid json}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(TraySettings.Default.SelectedWindow, settings.SelectedWindow);
        Assert.Equal(TraySettings.Default.RefreshCadence, settings.RefreshCadence);
    }

    [Fact]
    public void Load_UnknownWindow_ReturnsOneMinute()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"UnknownWindow\",\"RefreshCadence\":15}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.OneMinute, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.FifteenSeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_InvalidCadence_ReturnsFifteenSeconds()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":999}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.FifteenSeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_NullWindow_ReturnsDefault()
    {
        File.WriteAllText(_settingsPath, "{\"RefreshCadence\":30}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.OneMinute, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_NullCadence_ReturnsDefault()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.FifteenSeconds, settings.RefreshCadence);
    }

    [Fact]
    public void Load_NumericWindowString_FallsBackToDefault()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"999\",\"RefreshCadence\":30}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.OneMinute, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
    }

    [Fact]
    public void TrySave_ValidSettings_WritesFile()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.ThirtyMinutes, RefreshCadence.SixtySeconds);

        bool result = store.TrySave(settings);

        Assert.True(result);
        Assert.True(File.Exists(_settingsPath));

        var loaded = store.Load();
        Assert.Equal(MetricWindow.ThirtyMinutes, loaded.SelectedWindow);
        Assert.Equal(RefreshCadence.SixtySeconds, loaded.RefreshCadence);
    }

    [Fact]
    public void TrySave_FailedReplacement_LeavesOldFile()
    {
        string oldContent = "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15}";
        File.WriteAllText(_settingsPath, oldContent);

        var store = TraySettingsStore.CreateForTests(_settingsPath);

        using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var newSettings = new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.ThirtySeconds);
        bool result = store.TrySave(newSettings);

        Assert.False(result);

        stream.Close();
        string currentContent = File.ReadAllText(_settingsPath);
        Assert.Equal(oldContent, currentContent);
    }

    [Fact]
    public void TrySave_ReturnsFalseOnFailure()
    {
        string lockedPath = Path.Combine(_tempDirectory, "locked_settings.json");
        File.WriteAllText(lockedPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15}");

        var store = TraySettingsStore.CreateForTests(lockedPath);

        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var settings = new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.ThirtySeconds);
        bool result = store.TrySave(settings);

        Assert.False(result);
    }

    [Fact]
    public void TrySave_CreatesDirectory()
    {
        string nestedPath = Path.Combine(_tempDirectory, "nested", "subdir", "settings.json");
        var store = TraySettingsStore.CreateForTests(nestedPath);

        var settings = new TraySettings(MetricWindow.OneHour, RefreshCadence.FiveSeconds);
        bool result = store.TrySave(settings);

        Assert.True(result);
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void TrySave_AtomicWrite_NoTempFilesLeft()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.FiveMinutes, RefreshCadence.FifteenSeconds);

        bool result = store.TrySave(settings);

        Assert.True(result);

        string[] tempFiles = Directory.GetFiles(_tempDirectory, "*.tmp");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public void Load_LegacyJsonWithoutLanguage_ReturnsEnglish()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(Language.English, settings.Language);
    }

    [Fact]
    public void Load_ValidEnglishLanguage_ReturnsEnglish()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"English\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(Language.English, settings.Language);
    }

    [Fact]
    public void Load_ValidChineseLanguage_ReturnsChinese()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"Chinese\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(Language.Chinese, settings.Language);
    }

    [Fact]
    public void Load_InvalidLanguage_FallsBackToEnglish()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"Invalid\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(Language.English, settings.Language);
    }

    [Fact]
    public void Load_NullLanguage_FallsBackToEnglish()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":null}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(Language.English, settings.Language);
    }

    [Fact]
    public void TrySave_WithLanguage_SavesAndReloadsEnglish()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.ThirtyMinutes, RefreshCadence.SixtySeconds, Language.English);

        bool result = store.TrySave(settings);

        Assert.True(result);

        var loaded = store.Load();
        Assert.Equal(Language.English, loaded.Language);
    }

    [Fact]
    public void TrySave_WithLanguage_SavesAndReloadsChinese()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(MetricWindow.ThirtyMinutes, RefreshCadence.SixtySeconds, Language.Chinese);

        bool result = store.TrySave(settings);

        Assert.True(result);

        var loaded = store.Load();
        Assert.Equal(Language.Chinese, loaded.Language);
    }

    [Fact]
    public void Load_LegacyJsonWithoutOverlaySettings_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"English\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.False(settings.OverlayEnabled);
        Assert.False(settings.OverlayLocked);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void Load_ValidOverlayEnabled_ReturnsEnabled()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayEnabled\":true}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.True(settings.OverlayEnabled);
    }

    [Fact]
    public void Load_ValidOverlayLocked_ReturnsLocked()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayLocked\":true}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.True(settings.OverlayLocked);
    }

    [Fact]
    public void Load_ValidOverlayPosition_ReturnsPosition()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayLeft\":500.5,\"OverlayTop\":300.25}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(500.5, settings.OverlayLeft);
        Assert.Equal(300.25, settings.OverlayTop);
    }

    [Fact]
    public void Load_NullOverlayPosition_ReturnsNull()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayLeft\":null,\"OverlayTop\":null}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void TrySave_WithOverlaySettings_SavesAndReloads()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            Language.English,
            OverlayEnabled: true,
            OverlayLocked: false,
            OverlayLeft: 500.0,
            OverlayTop: 200.0
        );

        bool result = store.TrySave(settings);

        Assert.True(result);

        var loaded = store.Load();
        Assert.True(loaded.OverlayEnabled);
        Assert.False(loaded.OverlayLocked);
        Assert.Equal(500.0, loaded.OverlayLeft);
        Assert.Equal(200.0, loaded.OverlayTop);
    }

    [Fact]
    public void TrySave_WithAllOverlaySettings_SavesAndReloads()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.FiveMinutes,
            RefreshCadence.ThirtySeconds,
            Language.Chinese,
            OverlayEnabled: true,
            OverlayLocked: true,
            OverlayLeft: -1500.0,
            OverlayTop: 500.5
        );

        bool result = store.TrySave(settings);

        Assert.True(result);

        var loaded = store.Load();
        Assert.Equal(MetricWindow.FiveMinutes, loaded.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, loaded.RefreshCadence);
        Assert.Equal(Language.Chinese, loaded.Language);
        Assert.True(loaded.OverlayEnabled);
        Assert.True(loaded.OverlayLocked);
        Assert.Equal(-1500.0, loaded.OverlayLeft);
        Assert.Equal(500.5, loaded.OverlayTop);
    }
}