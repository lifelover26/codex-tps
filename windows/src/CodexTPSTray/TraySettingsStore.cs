using System;
using System.IO;
using System.Text.Json;
using CodexTPSCore;

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

            // Parse DataSource independently of the other fields. Any invalid
            // combination (unknown Kind, malformed name, Windows carrying a
            // name, WSL missing a name) falls back to Windows WITHOUT touching
            // the rest of the settings, satisfying the per-field fallback
            // contract. Parsed here once so every return branch below threads
            // the same value through the full constructor and cannot drop it.
            CodexDataSourceSelection dataSource = ParseDataSource(raw?.DataSource);

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
                    OverlayMonitorDeviceName: null,
                    DataSource: dataSource
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
                        OverlayMonitorDeviceName: null,
                        DataSource: dataSource
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
                    OverlayMonitorDeviceName: null,
                    DataSource: dataSource
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
                    OverlayMonitorDeviceName: overlayMonitorDeviceName,
                    DataSource: dataSource
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
                    OverlayMonitorDeviceName: null,
                    DataSource: dataSource
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
                OverlayMonitorDeviceName: null,
                DataSource: dataSource
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

    // Parses the persisted DataSource object into a CodexDataSourceSelection.
    // Reads from a JsonElement? (not a typed DTO) so that a tampered or
    // corrupted DataSource field — e.g. Kind supplied as a number, or the
    // whole field set to a string — can never crash the surrounding
    // RawSettingsDto deserialization. Every invalid shape degrades to Windows
    // without touching the other settings that were already parsed.
    //
    // On-disk contract: { "Kind": "Windows"|"Wsl", "WslDistributionName": string|null }.
    // Kind is matched case-insensitively. The distribution name is normalized
    // and validated by CodexDataSourceSelection.ForWsl; an invalid name falls
    // back to Windows. Windows must not carry a name; WSL must carry a valid
    // one. Anything else is Windows.
    private static CodexDataSourceSelection ParseDataSource(JsonElement? element)
    {
        if (!element.HasValue || element.Value.ValueKind == JsonValueKind.Null)
        {
            // Legacy files written before DataSource existed migrate to Windows.
            return CodexDataSourceSelection.Windows;
        }

        var ds = element.Value;
        if (ds.ValueKind != JsonValueKind.Object)
        {
            return CodexDataSourceSelection.Windows;
        }

        string? kind = null;
        if (ds.TryGetProperty("Kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String)
        {
            kind = kindElement.GetString();
        }

        string? distroName = null;
        if (ds.TryGetProperty("WslDistributionName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            distroName = nameElement.GetString();
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            return CodexDataSourceSelection.Windows;
        }

        if (string.Equals(kind, "Windows", StringComparison.OrdinalIgnoreCase))
        {
            // Windows must not carry a distribution name. If one is present
            // (corrupted/tampered file) it is ignored: the selection falls back
            // to a clean Windows selection with a null name.
            return CodexDataSourceSelection.Windows;
        }

        if (string.Equals(kind, "Wsl", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(distroName))
            {
                return CodexDataSourceSelection.Windows;
            }

            try
            {
                // ForWsl trims and validates; throws ArgumentException on an
                // illegal name (slashes, control chars, traversal, etc.).
                return CodexDataSourceSelection.ForWsl(distroName);
            }
            catch (ArgumentException)
            {
                return CodexDataSourceSelection.Windows;
            }
        }

        // Unknown Kind value.
        return CodexDataSourceSelection.Windows;
    }

    // Builds the on-disk DataSource element from a selection. The shape is
    // exactly { "Kind": <string>, "WslDistributionName": <string|null> }.
    // Only Kind and the normalized distribution name are emitted; CodexHome,
    // sessions paths, Linux user directories, and UNC paths are never written.
    private static JsonElement BuildDataSourceElement(CodexDataSourceSelection selection)
    {
        var json = JsonSerializer.Serialize(new
        {
            Kind = selection.Kind.ToString(),
            WslDistributionName = selection.WslDistributionName
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
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

        // Stored as a raw JsonElement so a malformed DataSource field can never
        // destabilize deserialization of the rest of the DTO. ParseDataSource
        // inspects the element defensively (see Load).
        public JsonElement? DataSource { get; set; }

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
            // Only Kind and the normalized WslDistributionName are persisted.
            // No CodexHome, sessions path, or UNC path is ever written.
            DataSource = BuildDataSourceElement(settings.DataSource);
        }
    }
}