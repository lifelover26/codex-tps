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
}