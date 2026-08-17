using System;
using System.IO;
using System.Text.Json;
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
            OverlayTop: 200.0,
            OverlayPosition: null
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
            OverlayTop: 500.5,
            OverlayPosition: null
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

    [Fact]
    public void Load_MissingFile_ReturnsThemeDefaults()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_LegacyJsonWithoutThemes_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"English\",\"OverlayEnabled\":true}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(Language.English, settings.Language);
        Assert.True(settings.OverlayEnabled);
        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_ValidApplicationThemeSystem_ReturnsSystem()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":\"System\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
    }

    [Fact]
    public void Load_ValidApplicationThemeLight_ReturnsLight()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":\"Light\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.Light, settings.ApplicationTheme);
    }

    [Fact]
    public void Load_ValidApplicationThemeDark_ReturnsDark()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":\"Dark\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.Dark, settings.ApplicationTheme);
    }

    [Fact]
    public void Load_ValidOverlayThemeFollowApplication_ReturnsFollowApplication()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":\"FollowApplication\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_ValidOverlayThemeSystem_ReturnsSystem()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":\"System\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.System, settings.OverlayTheme);
    }

    [Fact]
    public void Load_ValidOverlayThemeLight_ReturnsLight()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":\"Light\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.Light, settings.OverlayTheme);
    }

    [Fact]
    public void Load_ValidOverlayThemeDark_ReturnsDark()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":\"Dark\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.Dark, settings.OverlayTheme);
    }

    [Fact]
    public void Load_ThemesCaseInsensitive_ReturnsCorrectValue()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":\"lIgHt\",\"OverlayTheme\":\"fOlLoWApPlIcAtIoN\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.Light, settings.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_UnknownApplicationTheme_FallsBackToSystem()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":\"Unknown\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
    }

    [Fact]
    public void Load_UnknownOverlayTheme_FallsBackToFollowApplication()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":\"Unknown\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_NullApplicationTheme_FallsBackToSystem()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":null}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
    }

    [Fact]
    public void Load_NullOverlayTheme_FallsBackToFollowApplication()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":null}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_NumericApplicationTheme_FallsBackToSystem()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"ApplicationTheme\":\"0\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
    }

    [Fact]
    public void Load_NumericOverlayTheme_FallsBackToFollowApplication()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayTheme\":\"1\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayThemePreference.FollowApplication, settings.OverlayTheme);
    }

    [Fact]
    public void Load_OneThemeInvalid_OtherThemeAndOldSettingsPreserved()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"Chinese\",\"OverlayEnabled\":true,\"ApplicationTheme\":\"Invalid\",\"OverlayTheme\":\"Dark\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(Language.Chinese, settings.Language);
        Assert.True(settings.OverlayEnabled);
        Assert.Equal(ApplicationThemePreference.System, settings.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.Dark, settings.OverlayTheme);
    }

    [Fact]
    public void TrySave_WithThemes_SavesAndReloads()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.FiveMinutes,
            RefreshCadence.ThirtySeconds,
            Language.Chinese,
            OverlayEnabled: true,
            OverlayLocked: true,
            OverlayLeft: -1500.0,
            OverlayTop: 500.5,
            ApplicationTheme: ApplicationThemePreference.Dark,
            OverlayTheme: OverlayThemePreference.Light,
            OverlayPosition: null
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
        Assert.Equal(ApplicationThemePreference.Dark, loaded.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.Light, loaded.OverlayTheme);
    }

    [Fact]
    public void TrySave_ThemesUseNameStrings()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            ApplicationTheme: ApplicationThemePreference.Dark,
            OverlayTheme: OverlayThemePreference.Light
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        string json = File.ReadAllText(_settingsPath);
        Assert.Contains("ApplicationTheme", json);
        Assert.Contains("Dark", json);
        Assert.Contains("OverlayTheme", json);
        Assert.Contains("Light", json);
        Assert.DoesNotContain("ApplicationTheme\":2", json);
        Assert.DoesNotContain("OverlayTheme\":2", json);
    }

    [Fact]
    public void Load_LegacyJsonWithoutOverlayOpacity_ReturnsDefault()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"English\",\"OverlayEnabled\":true}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Default, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultOverlayOpacity()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Default, settings.OverlayOpacity);
    }

    [Fact]
    public void TrySave_WithOverlayOpacity_SavesAndReloads()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayOpacity: OverlayOpacityPreference.Percent40
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        var loaded = store.Load();
        Assert.Equal(OverlayOpacityPreference.Percent40, loaded.OverlayOpacity);
    }

    [Fact]
    public void Load_ValidOverlayOpacityPercent55_ReturnsPercent55()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":\"Percent55\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Percent55, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_ValidOverlayOpacityPercent70_ReturnsPercent70()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":\"Percent70\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Percent70, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_ValidOverlayOpacityPercent85_ReturnsPercent85()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":\"Percent85\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Percent85, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_ValidOverlayOpacityOpaque_ReturnsOpaque()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":\"Opaque\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Opaque, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_InvalidOverlayOpacity_FallsBackToDefault()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":\"Invalid\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Default, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_NullOverlayOpacity_FallsBackToDefault()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":null}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Default, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_OverlayOpacityCaseInsensitive_ReturnsCorrectValue()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayOpacity\":\"pErCeNt40\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayOpacityPreference.Percent40, settings.OverlayOpacity);
    }

    [Fact]
    public void Load_NewOpacityFieldDoesNotAffectExistingFields()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"FiveMinutes\",\"RefreshCadence\":30,\"Language\":\"Chinese\",\"OverlayEnabled\":true,\"OverlayOpacity\":\"Percent40\",\"ApplicationTheme\":\"Dark\",\"OverlayTheme\":\"Light\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(MetricWindow.FiveMinutes, settings.SelectedWindow);
        Assert.Equal(RefreshCadence.ThirtySeconds, settings.RefreshCadence);
        Assert.Equal(Language.Chinese, settings.Language);
        Assert.True(settings.OverlayEnabled);
        Assert.Equal(ApplicationThemePreference.Dark, settings.ApplicationTheme);
        Assert.Equal(OverlayThemePreference.Light, settings.OverlayTheme);
        Assert.Equal(OverlayOpacityPreference.Percent40, settings.OverlayOpacity);
    }

    [Fact]
    public void TrySave_OverlayOpacityUsesNameStrings()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayOpacity: OverlayOpacityPreference.Percent55
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        string json = File.ReadAllText(_settingsPath);
        Assert.Contains("OverlayOpacity", json);
        Assert.Contains("Percent55", json);
        Assert.DoesNotContain("OverlayOpacity\":1", json);
    }

    [Fact]
    public void TrySave_AllOverlayOpacityValues_Roundtrip()
    {
        foreach (OverlayOpacityPreference preference in Enum.GetValues<OverlayOpacityPreference>())
        {
            string testPath = Path.Combine(_tempDirectory, $"opacity_{preference}.json");
            var store = TraySettingsStore.CreateForTests(testPath);
            var settings = new TraySettings(
                MetricWindow.OneMinute,
                RefreshCadence.FifteenSeconds,
                OverlayOpacity: preference
            );

            bool result = store.TrySave(settings);
            Assert.True(result);

            var loaded = store.Load();
            Assert.Equal(preference, loaded.OverlayOpacity);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsTopRightPositionPreset()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.TopRight, settings.OverlayPosition);
        Assert.Null(settings.OverlayMonitorDeviceName);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void Load_LegacyJsonWithCoords_MigratesToCustom()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayLeft\":500.0,\"OverlayTop\":300.0}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Null(settings.OverlayPosition);
        Assert.Null(settings.OverlayMonitorDeviceName);
        Assert.Equal(500.0, settings.OverlayLeft);
        Assert.Equal(300.0, settings.OverlayTop);
    }

    [Fact]
    public void Load_LegacyJsonWithoutCoords_MigratesToTopRight()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.TopRight, settings.OverlayPosition);
        Assert.Null(settings.OverlayMonitorDeviceName);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void Load_LegacyJsonWithSingleCoord_SafeFallbackToTopRight()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayLeft\":500.0}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.TopRight, settings.OverlayPosition);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void TrySave_WithPositionPreset_SavesAndReloads()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayPosition: OverlayPositionPreset.BottomLeft,
            OverlayMonitorDeviceName: @"\\.\DISPLAY2"
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        var loaded = store.Load();
        Assert.Equal(OverlayPositionPreset.BottomLeft, loaded.OverlayPosition);
        Assert.Equal(@"\\.\DISPLAY2", loaded.OverlayMonitorDeviceName);
        Assert.Null(loaded.OverlayLeft);
        Assert.Null(loaded.OverlayTop);
    }

    [Fact]
    public void TrySave_PresetClearsAbsoluteCoords()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayLeft: 999.0,
            OverlayTop: 888.0,
            OverlayPosition: OverlayPositionPreset.TopRight,
            OverlayMonitorDeviceName: null
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        string json = File.ReadAllText(_settingsPath);
        using var document = JsonDocument.Parse(json);
        bool hasLeft = document.RootElement.TryGetProperty("OverlayLeft", out var leftElement);
        bool hasTop = document.RootElement.TryGetProperty("OverlayTop", out var topElement);
        Assert.True(hasLeft);
        Assert.True(hasTop);
        Assert.Equal(JsonValueKind.Null, leftElement.ValueKind);
        Assert.Equal(JsonValueKind.Null, topElement.ValueKind);

        var loaded = store.Load();
        Assert.Null(loaded.OverlayLeft);
        Assert.Null(loaded.OverlayTop);
    }

    [Fact]
    public void TrySave_CustomPreservesAbsoluteCoords()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayLeft: 400.0,
            OverlayTop: 500.0,
            OverlayPosition: null,
            OverlayMonitorDeviceName: null
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        var loaded = store.Load();
        Assert.Null(loaded.OverlayPosition);
        Assert.Equal(400.0, loaded.OverlayLeft);
        Assert.Equal(500.0, loaded.OverlayTop);
    }

    [Fact]
    public void Load_ValidOverlayPositionPreset_ReturnsPreset()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayPosition\":\"MiddleLeft\",\"OverlayMonitorDeviceName\":\"\\\\\\\\.\\\\DISPLAY1\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.MiddleLeft, settings.OverlayPosition);
        Assert.Equal(@"\\.\DISPLAY1", settings.OverlayMonitorDeviceName);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void Load_InvalidOverlayPositionPreset_SafeFallbackToTopRight()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayPosition\":\"Invalid\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.TopRight, settings.OverlayPosition);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void Load_PositionPresetCaseInsensitive_ReturnsCorrectValue()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayPosition\":\"bOtToMrIgHt\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.BottomRight, settings.OverlayPosition);
    }

    [Fact]
    public void TrySave_AllPresetValues_Roundtrip()
    {
        foreach (OverlayPositionPreset preset in Enum.GetValues<OverlayPositionPreset>())
        {
            string testPath = Path.Combine(_tempDirectory, $"position_{preset}.json");
            var store = TraySettingsStore.CreateForTests(testPath);
            var settings = new TraySettings(
                MetricWindow.OneMinute,
                RefreshCadence.FifteenSeconds,
                OverlayPosition: preset,
                OverlayMonitorDeviceName: "test-device"
            );

            bool result = store.TrySave(settings);
            Assert.True(result);

            var loaded = store.Load();
            Assert.Equal(preset, loaded.OverlayPosition);
            Assert.Equal("test-device", loaded.OverlayMonitorDeviceName);
        }
    }

    [Fact]
    public void Load_PositionPresetUsesNameStrings()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayPosition\":\"TopLeft\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.TopLeft, settings.OverlayPosition);
    }

    [Fact]
    public void TrySave_PositionPresetUsesNameStrings()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayPosition: OverlayPositionPreset.MiddleRight
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        string json = File.ReadAllText(_settingsPath);
        Assert.Contains("OverlayPosition", json);
        Assert.Contains("MiddleRight", json);
        Assert.DoesNotContain("OverlayPosition\":3", json);
    }

    [Fact]
    public void DefaultConstructor_OverlayPosition_IsTopRight()
    {
        var settings = new TraySettings(MetricWindow.OneMinute, RefreshCadence.FifteenSeconds);

        Assert.Equal(OverlayPositionPreset.TopRight, settings.OverlayPosition);
        Assert.Null(settings.OverlayMonitorDeviceName);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
    }

    [Fact]
    public void Load_InvalidPresetWithValidCoords_FallsBackToTopRight()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayPosition\":\"Invalid\",\"OverlayLeft\":500.0,\"OverlayTop\":300.0}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayPositionPreset.TopRight, settings.OverlayPosition);
        Assert.Null(settings.OverlayLeft);
        Assert.Null(settings.OverlayTop);
        Assert.Null(settings.OverlayMonitorDeviceName);
    }

    [Fact]
    public void TrySave_CustomMode_ClearsResidualMonitorDeviceName()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayLeft: 400.0,
            OverlayTop: 500.0,
            OverlayPosition: null,
            OverlayMonitorDeviceName: @"\\.\DISPLAY2"
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        string json = File.ReadAllText(_settingsPath);
        using var document = JsonDocument.Parse(json);
        bool hasDeviceName = document.RootElement.TryGetProperty("OverlayMonitorDeviceName", out var deviceElement);
        Assert.True(hasDeviceName);
        Assert.Equal(JsonValueKind.Null, deviceElement.ValueKind);

        var loaded = store.Load();
        Assert.Null(loaded.OverlayPosition);
        Assert.Null(loaded.OverlayMonitorDeviceName);
        Assert.Equal(400.0, loaded.OverlayLeft);
        Assert.Equal(500.0, loaded.OverlayTop);
    }

    [Fact]
    public void TrySave_PresetMode_PreservesMonitorDeviceName()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = new TraySettings(
            MetricWindow.OneMinute,
            RefreshCadence.FifteenSeconds,
            OverlayPosition: OverlayPositionPreset.BottomLeft,
            OverlayMonitorDeviceName: @"\\.\DISPLAY2"
        );

        bool result = store.TrySave(settings);
        Assert.True(result);

        var loaded = store.Load();
        Assert.Equal(OverlayPositionPreset.BottomLeft, loaded.OverlayPosition);
        Assert.Equal(@"\\.\DISPLAY2", loaded.OverlayMonitorDeviceName);
        Assert.Null(loaded.OverlayLeft);
        Assert.Null(loaded.OverlayTop);
    }

    [Fact]
    public void Load_MissingFile_DefaultCustomPositionModeIsKeepRelative()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayCustomPositionMode.KeepRelative, settings.OverlayCustomPositionMode);
        Assert.Null(settings.OverlayXRatio);
        Assert.Null(settings.OverlayYRatio);
        Assert.Null(settings.OverlayPerDisplayPositions);
    }

    [Fact]
    public void TrySave_KeepRelativeRatios_SavesAndReloads()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.3,
            OverlayYRatio = 0.7
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(OverlayCustomPositionMode.KeepRelative, loaded.OverlayCustomPositionMode);
        Assert.Equal(0.3, loaded.OverlayXRatio);
        Assert.Equal(0.7, loaded.OverlayYRatio);
        Assert.Null(loaded.OverlayLeft);
        Assert.Null(loaded.OverlayTop);
    }

    [Fact]
    public void TrySave_RememberPerDisplay_SavesAndReloadsRecords()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var positions = new Dictionary<string, DisplayRelativePosition>(StringComparer.OrdinalIgnoreCase)
        {
            [@"\\.\DISPLAY1"] = new DisplayRelativePosition(0.25, 0.5),
            [@"\\.\DISPLAY2"] = new DisplayRelativePosition(0.75, 0.2)
        };

        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay,
            OverlayPerDisplayPositions = positions
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, loaded.OverlayCustomPositionMode);
        Assert.NotNull(loaded.OverlayPerDisplayPositions);
        Assert.Equal(2, loaded.OverlayPerDisplayPositions!.Count);

        var display1 = loaded.OverlayPerDisplayPositions[@"\\.\DISPLAY1"];
        Assert.Equal(0.25, display1.XRatio);
        Assert.Equal(0.5, display1.YRatio);

        var display2 = loaded.OverlayPerDisplayPositions[@"\\.\DISPLAY2"];
        Assert.Equal(0.75, display2.XRatio);
        Assert.Equal(0.2, display2.YRatio);
    }

    [Fact]
    public void TrySave_AllCustomPositionModes_Roundtrip()
    {
        foreach (OverlayCustomPositionMode mode in Enum.GetValues<OverlayCustomPositionMode>())
        {
            string testPath = Path.Combine(_tempDirectory, $"custommode_{mode}.json");
            var store = TraySettingsStore.CreateForTests(testPath);
            var settings = TraySettings.Default with
            {
                OverlayPosition = null,
                OverlayCustomPositionMode = mode,
                OverlayXRatio = 0.4,
                OverlayYRatio = 0.6
            };

            Assert.True(store.TrySave(settings));

            var loaded = store.Load();
            Assert.Equal(mode, loaded.OverlayCustomPositionMode);
        }
    }

    [Fact]
    public void Load_InvalidCustomPositionMode_FallsBackToKeepRelative()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayCustomPositionMode\":\"Invalid\"}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(OverlayCustomPositionMode.KeepRelative, settings.OverlayCustomPositionMode);
    }

    [Fact]
    public void Load_RatiosClampedToUnitInterval()
    {
        File.WriteAllText(_settingsPath, "{\"SelectedWindow\":\"OneMinute\",\"RefreshCadence\":15,\"OverlayXRatio\":1.7,\"OverlayYRatio\":-0.4}");

        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = store.Load();

        Assert.Equal(1.0, settings.OverlayXRatio);
        Assert.Equal(0.0, settings.OverlayYRatio);
    }

    [Fact]
    public void TrySave_PresetMode_PreservesCustomPositionMemory()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var positions = new Dictionary<string, DisplayRelativePosition>(StringComparer.OrdinalIgnoreCase)
        {
            [@"\\.\DISPLAY2"] = new DisplayRelativePosition(0.8, 0.3)
        };

        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.TopRight,
            OverlayCustomPositionMode = OverlayCustomPositionMode.RememberPerDisplay,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.5,
            OverlayPerDisplayPositions = positions,
            OverlayCustomMonitorId = @"\\.\DISPLAY2"
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(OverlayPositionPreset.TopRight, loaded.OverlayPosition);
        Assert.Equal(OverlayCustomPositionMode.RememberPerDisplay, loaded.OverlayCustomPositionMode);
        Assert.Equal(0.5, loaded.OverlayXRatio);
        Assert.Equal(0.5, loaded.OverlayYRatio);
        Assert.NotNull(loaded.OverlayPerDisplayPositions);
        Assert.Single(loaded.OverlayPerDisplayPositions!);
        Assert.Equal(@"\\.\DISPLAY2", loaded.OverlayCustomMonitorId);
    }

    [Fact]
    public void TrySave_LegacyMigratedFormat_ReloadsWithoutLegacyCoords()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var migrated = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayLeft = null,
            OverlayTop = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.42,
            OverlayYRatio = 0.58
        };

        Assert.True(store.TrySave(migrated));

        var loaded = store.Load();
        Assert.Null(loaded.OverlayLeft);
        Assert.Null(loaded.OverlayTop);
        Assert.Equal(0.42, loaded.OverlayXRatio);
        Assert.Equal(0.58, loaded.OverlayYRatio);
        Assert.Equal(OverlayCustomPositionMode.KeepRelative, loaded.OverlayCustomPositionMode);
    }

    [Fact]
    public void TrySave_DoesNotPersistUserDataPaths()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.5,
            OverlayYRatio = 0.5
        };

        Assert.True(store.TrySave(settings));

        string json = File.ReadAllText(_settingsPath);
        Assert.DoesNotContain("CodexHome", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessions", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrySave_OverlayCustomMonitorId_Roundtrips()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = TraySettings.Default with
        {
            OverlayPosition = null,
            OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
            OverlayXRatio = 0.3,
            OverlayYRatio = 0.7,
            OverlayCustomMonitorId = @"\\.\DISPLAY2"
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(@"\\.\DISPLAY2", loaded.OverlayCustomMonitorId);
    }

    [Fact]
    public void TrySave_PresetMode_PreservesCustomMonitorId()
    {
        var store = TraySettingsStore.CreateForTests(_settingsPath);
        var settings = TraySettings.Default with
        {
            OverlayPosition = OverlayPositionPreset.TopLeft,
            OverlayCustomMonitorId = @"\\.\DISPLAY2"
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal(OverlayPositionPreset.TopLeft, loaded.OverlayPosition);
        Assert.Equal(@"\\.\DISPLAY2", loaded.OverlayCustomMonitorId);
    }
}