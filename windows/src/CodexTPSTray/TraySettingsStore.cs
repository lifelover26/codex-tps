using System;
using System.IO;
using System.Text.Json;

namespace CodexTPSTray;

public class TraySettingsStore
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private TraySettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public static TraySettingsStore CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string settingsPath = Path.Combine(localAppData, "CodexTPS", "settings.json");
        return new TraySettingsStore(settingsPath);
    }

    public static TraySettingsStore CreateForTests(string settingsPath)
    {
        return new TraySettingsStore(settingsPath);
    }

    public TraySettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return TraySettings.Default;
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            var raw = JsonSerializer.Deserialize<RawSettingsDto>(json);

            MetricWindow window = ParseMetricWindow(raw?.SelectedWindow);
            RefreshCadence cadence = ParseRefreshCadence(raw?.RefreshCadence);
            Language language = ParseLanguage(raw?.Language);
            bool overlayEnabled = ParseOverlayEnabled(raw?.OverlayEnabled);
            bool overlayLocked = ParseOverlayLocked(raw?.OverlayLocked);
            double? overlayLeft = ParseOverlayPosition(raw?.OverlayLeft);
            double? overlayTop = ParseOverlayPosition(raw?.OverlayTop);
            ApplicationThemePreference appTheme = ParseApplicationTheme(raw?.ApplicationTheme);
            OverlayThemePreference overlayTheme = ParseOverlayTheme(raw?.OverlayTheme);
            OverlayOpacityPreference overlayOpacity = ParseOverlayOpacity(raw?.OverlayOpacity);
            OverlayPositionPreset? overlayPosition = ParseOverlayPositionPreset(raw?.OverlayPosition);
            string? overlayMonitorDeviceName = raw?.OverlayMonitorDeviceName;

            bool positionKeyPresent = raw?.OverlayPosition != null;
            bool invalidPositionInJson = positionKeyPresent && !overlayPosition.HasValue;
            bool hasLegacyCoords = overlayLeft.HasValue && overlayTop.HasValue;

            // Invalid preset string: always fall back to TopRight, regardless of residual coords.
            if (invalidPositionInJson)
            {
                return new TraySettings(
                    SelectedWindow: window,
                    RefreshCadence: cadence,
                    Language: language,
                    OverlayEnabled: overlayEnabled,
                    OverlayLocked: overlayLocked,
                    OverlayLeft: null,
                    OverlayTop: null,
                    ApplicationTheme: appTheme,
                    OverlayTheme: overlayTheme,
                    OverlayOpacity: overlayOpacity,
                    OverlayPosition: OverlayPositionPreset.TopRight,
                    OverlayMonitorDeviceName: null
                );
            }

            // No position key and no device name: legacy migration.
            if (!positionKeyPresent && string.IsNullOrEmpty(overlayMonitorDeviceName))
            {
                if (hasLegacyCoords)
                {
                    return new TraySettings(
                        SelectedWindow: window,
                        RefreshCadence: cadence,
                        Language: language,
                        OverlayEnabled: overlayEnabled,
                        OverlayLocked: overlayLocked,
                        OverlayLeft: overlayLeft,
                        OverlayTop: overlayTop,
                        ApplicationTheme: appTheme,
                        OverlayTheme: overlayTheme,
                        OverlayOpacity: overlayOpacity,
                        OverlayPosition: null,
                        OverlayMonitorDeviceName: null
                    );
                }

                return new TraySettings(
                    SelectedWindow: window,
                    RefreshCadence: cadence,
                    Language: language,
                    OverlayEnabled: overlayEnabled,
                    OverlayLocked: overlayLocked,
                    OverlayLeft: null,
                    OverlayTop: null,
                    ApplicationTheme: appTheme,
                    OverlayTheme: overlayTheme,
                    OverlayOpacity: overlayOpacity,
                    OverlayPosition: OverlayPositionPreset.TopRight,
                    OverlayMonitorDeviceName: null
                );
            }

            // Valid preset: clear absolute coords.
            if (overlayPosition.HasValue)
            {
                return new TraySettings(
                    SelectedWindow: window,
                    RefreshCadence: cadence,
                    Language: language,
                    OverlayEnabled: overlayEnabled,
                    OverlayLocked: overlayLocked,
                    OverlayLeft: null,
                    OverlayTop: null,
                    ApplicationTheme: appTheme,
                    OverlayTheme: overlayTheme,
                    OverlayOpacity: overlayOpacity,
                    OverlayPosition: overlayPosition,
                    OverlayMonitorDeviceName: overlayMonitorDeviceName
                );
            }

            // No preset, but has legacy coords: Custom mode.
            if (hasLegacyCoords)
            {
                return new TraySettings(
                    SelectedWindow: window,
                    RefreshCadence: cadence,
                    Language: language,
                    OverlayEnabled: overlayEnabled,
                    OverlayLocked: overlayLocked,
                    OverlayLeft: overlayLeft,
                    OverlayTop: overlayTop,
                    ApplicationTheme: appTheme,
                    OverlayTheme: overlayTheme,
                    OverlayOpacity: overlayOpacity,
                    OverlayPosition: null,
                    OverlayMonitorDeviceName: null
                );
            }

            return new TraySettings(
                SelectedWindow: window,
                RefreshCadence: cadence,
                Language: language,
                OverlayEnabled: overlayEnabled,
                OverlayLocked: overlayLocked,
                OverlayLeft: null,
                OverlayTop: null,
                ApplicationTheme: appTheme,
                OverlayTheme: overlayTheme,
                OverlayOpacity: overlayOpacity,
                OverlayPosition: OverlayPositionPreset.TopRight,
                OverlayMonitorDeviceName: null
            );
        }
        catch
        {
            return TraySettings.Default;
        }
    }

    public bool TrySave(TraySettings settings)
    {
        string directory = Path.GetDirectoryName(_settingsPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
                return false;
            }
        }

        string tempPath = Path.Combine(directory, $"{Guid.NewGuid()}.tmp");
        bool success = false;

        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new RawSettingsDto(settings), SerializerOptions);

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, null);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }

            success = true;
        }
        catch
        {
            success = false;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        return success;
    }

    private static MetricWindow ParseMetricWindow(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.SelectedWindow;
        }

        if (Enum.TryParse<MetricWindow>(value, ignoreCase: true, out var window) && Enum.IsDefined(window))
        {
            return window;
        }

        return TraySettings.Default.SelectedWindow;
    }

    private static RefreshCadence ParseRefreshCadence(int? value)
    {
        if (value == null)
        {
            return TraySettings.Default.RefreshCadence;
        }

        return RefreshCadenceExtensions.FromSeconds(value.Value);
    }

    private static Language ParseLanguage(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.Language;
        }

        if (Enum.TryParse<Language>(value, ignoreCase: true, out var language) && Enum.IsDefined(language))
        {
            return language;
        }

        return TraySettings.Default.Language;
    }

    private static bool ParseOverlayEnabled(bool? value)
    {
        return value ?? TraySettings.Default.OverlayEnabled;
    }

    private static bool ParseOverlayLocked(bool? value)
    {
        return value ?? TraySettings.Default.OverlayLocked;
    }

    private static double? ParseOverlayPosition(double? value)
    {
        if (value == null)
        {
            return null;
        }

        double pos = value.Value;
        if (double.IsNaN(pos) || double.IsInfinity(pos))
        {
            return null;
        }

        return pos;
    }

    private static ApplicationThemePreference ParseApplicationTheme(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.ApplicationTheme;
        }

        return value.ToLowerInvariant() switch
        {
            "system" => ApplicationThemePreference.System,
            "light" => ApplicationThemePreference.Light,
            "dark" => ApplicationThemePreference.Dark,
            _ => TraySettings.Default.ApplicationTheme
        };
    }

    private static OverlayThemePreference ParseOverlayTheme(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.OverlayTheme;
        }

        return value.ToLowerInvariant() switch
        {
            "followapplication" => OverlayThemePreference.FollowApplication,
            "system" => OverlayThemePreference.System,
            "light" => OverlayThemePreference.Light,
            "dark" => OverlayThemePreference.Dark,
            _ => TraySettings.Default.OverlayTheme
        };
    }

    private static OverlayOpacityPreference ParseOverlayOpacity(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.OverlayOpacity;
        }

        if (Enum.TryParse<OverlayOpacityPreference>(value, ignoreCase: true, out var preference) && Enum.IsDefined(preference))
        {
            return preference;
        }

        return TraySettings.Default.OverlayOpacity;
    }

    private static OverlayPositionPreset? ParseOverlayPositionPreset(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (Enum.TryParse<OverlayPositionPreset>(value, ignoreCase: true, out var preset) && Enum.IsDefined(preset))
        {
            return preset;
        }

        return null;
    }

    private sealed class RawSettingsDto
    {
        public string? SelectedWindow { get; set; }
        public int? RefreshCadence { get; set; }
        public string? Language { get; set; }
        public bool? OverlayEnabled { get; set; }
        public bool? OverlayLocked { get; set; }
        public double? OverlayLeft { get; set; }
        public double? OverlayTop { get; set; }
        public string? ApplicationTheme { get; set; }
        public string? OverlayTheme { get; set; }
        public string? OverlayOpacity { get; set; }
        public string? OverlayPosition { get; set; }
        public string? OverlayMonitorDeviceName { get; set; }

        public RawSettingsDto()
        {
        }

        public RawSettingsDto(TraySettings settings)
        {
            bool isPresetMode = settings.OverlayPosition.HasValue;

            SelectedWindow = settings.SelectedWindow.ToString();
            RefreshCadence = (int)settings.RefreshCadence;
            Language = settings.Language.ToString();
            OverlayEnabled = settings.OverlayEnabled;
            OverlayLocked = settings.OverlayLocked;
            OverlayLeft = isPresetMode ? null : settings.OverlayLeft;
            OverlayTop = isPresetMode ? null : settings.OverlayTop;
            ApplicationTheme = settings.ApplicationTheme.ToString();
            OverlayTheme = settings.OverlayTheme.ToString();
            OverlayOpacity = settings.OverlayOpacity.ToString();
            OverlayPosition = settings.OverlayPosition?.ToString();
            // Custom mode must not persist a residual monitor device name.
            OverlayMonitorDeviceName = isPresetMode ? settings.OverlayMonitorDeviceName : null;
        }
    }
}