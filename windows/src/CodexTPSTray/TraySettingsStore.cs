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

            return new TraySettings(
                SelectedWindow: window,
                RefreshCadence: cadence,
                Language: language,
                OverlayEnabled: overlayEnabled,
                OverlayLocked: overlayLocked,
                OverlayLeft: overlayLeft,
                OverlayTop: overlayTop,
                ApplicationTheme: appTheme,
                OverlayTheme: overlayTheme
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
            return TraySettings.Default.OverlayLeft;
        }

        double pos = value.Value;
        if (double.IsNaN(pos) || double.IsInfinity(pos))
        {
            return TraySettings.Default.OverlayLeft;
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

        public RawSettingsDto()
        {
        }

        public RawSettingsDto(TraySettings settings)
        {
            SelectedWindow = settings.SelectedWindow.ToString();
            RefreshCadence = (int)settings.RefreshCadence;
            Language = settings.Language.ToString();
            OverlayEnabled = settings.OverlayEnabled;
            OverlayLocked = settings.OverlayLocked;
            OverlayLeft = settings.OverlayLeft;
            OverlayTop = settings.OverlayTop;
            ApplicationTheme = settings.ApplicationTheme.ToString();
            OverlayTheme = settings.OverlayTheme.ToString();
        }
    }
}