using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexTPSCore;

namespace CodexTPSTray;

public class TraySettingsStore : ITraySettingsStore
{
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DataSourceSerializerOptions = new();

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

    internal static TraySettingsStore CreateForTests(string settingsPath)
    {
        return new TraySettingsStore(settingsPath);
    }

    internal static ITraySettingsStore CreateFromSettings(TraySettings settings)
    {
        return new InMemoryTraySettingsStore(settings);
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
            RawSettingsDto? raw = JsonSerializer.Deserialize<RawSettingsDto>(json, SerializerOptions);
            if (raw == null)
            {
                return TraySettings.Default;
            }

            CodexDataSourceSelection dataSource = ParseDataSource(raw.DataSource);
            PositionMigration position = HasNewPositionFields(raw)
                ? ParseNewPosition(raw)
                : ParseLegacyPosition(raw);
            AppearanceMigration appearance = HasNewAppearanceFields(raw)
                ? ParseNewAppearance(raw)
                : ParseLegacyAppearance(raw);

            return CreateSettings(raw, position, appearance, dataSource);
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

        string tempPath = Path.Combine(
            directory,
            Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                new RawSettingsDto(settings),
                SerializerOptions);

            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
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

            return true;
        }
        catch
        {
            return false;
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
    }

    private sealed class InMemoryTraySettingsStore : ITraySettingsStore
    {
        private TraySettings _settings;

        public InMemoryTraySettingsStore(TraySettings settings)
        {
            _settings = settings;
        }

        public TraySettings Load() => _settings;

        public bool TrySave(TraySettings settings)
        {
            _settings = settings;
            return true;
        }
    }

    private sealed record PositionMigration(
        OverlayPositionMemoryMode PositionMemoryMode,
        OverlayPositionState? SharedPosition,
        IReadOnlyDictionary<string, OverlayPositionState>? PerDisplayPositions,
        string? OverlayTargetMonitorId,
        string? PendingPresetMigrationTarget,
        double? LegacyLeft,
        double? LegacyTop);

    private sealed record AppearanceMigration(
        OverlayAppearanceMemoryMode AppearanceMemoryMode,
        OverlayAppearanceState? SharedAppearance,
        IReadOnlyDictionary<string, OverlayAppearanceState>? PerDisplayAppearances);

    private static TraySettings CreateSettings(
        RawSettingsDto raw,
        PositionMigration position,
        AppearanceMigration appearance,
        CodexDataSourceSelection dataSource)
    {
        bool hasLegacyCoordinates = position.LegacyLeft.HasValue && position.LegacyTop.HasValue;
        OverlayPositionState? sharedPosition = position.SharedPosition;
        if (sharedPosition == null && !hasLegacyCoordinates)
        {
            sharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.TopRight);
        }

        double? legacyLeft = sharedPosition == null
            ? position.LegacyLeft
            : null;
        double? legacyTop = sharedPosition == null
            ? position.LegacyTop
            : null;
        string? pendingPresetTarget = sharedPosition is OverlayPositionState.Preset
            ? position.PendingPresetMigrationTarget
            : null;

        OverlayThemePreference overlayTheme = ParseOverlayTheme(raw.OverlayTheme);
        OverlayOpacityPreference overlayOpacity = ParseOverlayOpacity(raw.OverlayOpacity);

        // A pre-v0.4.2 file has no appearance memory fields. Migrate
        // SharedAppearance from the scalar theme/opacity so the visual effect
        // is unchanged and per-display mode has an inheritance seed.
        OverlayAppearanceState? sharedAppearance = appearance.SharedAppearance;
        if (sharedAppearance == null)
        {
            sharedAppearance = new OverlayAppearanceState(overlayTheme, overlayOpacity);
        }

        return new TraySettings(
            SelectedWindow: ParseMetricWindow(raw.SelectedWindow),
            RefreshCadence: ParseRefreshCadence(raw.RefreshCadence),
            Language: ParseLanguage(raw.Language),
            OverlayEnabled: raw.OverlayEnabled ?? TraySettings.Default.OverlayEnabled,
            OverlayLocked: raw.OverlayLocked ?? TraySettings.Default.OverlayLocked,
            OverlayLeft: legacyLeft,
            OverlayTop: legacyTop,
            ApplicationTheme: ParseApplicationTheme(raw.ApplicationTheme),
            OverlayTheme: overlayTheme,
            OverlayOpacity: overlayOpacity,
            PositionMemoryMode: position.PositionMemoryMode,
            SharedPosition: sharedPosition,
            PerDisplayPositions: position.PerDisplayPositions,
            OverlayTargetMonitorId: position.OverlayTargetMonitorId,
            PendingPresetMigrationTarget: pendingPresetTarget,
            AppearanceMemoryMode: appearance.AppearanceMemoryMode,
            SharedAppearance: sharedAppearance,
            PerDisplayAppearances: appearance.PerDisplayAppearances)
        {
            DataSource = dataSource
        };
    }

    private static bool HasNewPositionFields(RawSettingsDto raw)
    {
        return HasJsonValue(raw.PositionMemoryMode)
            || HasJsonValue(raw.SharedPosition)
            || HasJsonValue(raw.PerDisplayPositions)
            || HasJsonValue(raw.OverlayTargetMonitorId)
            || HasJsonValue(raw.PendingPresetMigrationTarget);
    }

    private static PositionMigration ParseNewPosition(RawSettingsDto raw)
    {
        OverlayPositionState? shared = ParsePositionState(raw.SharedPosition);
        IReadOnlyDictionary<string, OverlayPositionState>? perDisplay =
            ParsePositionStates(raw.PerDisplayPositions);

        double? legacyLeft = ParseFiniteDouble(raw.OverlayLeft);
        double? legacyTop = ParseFiniteDouble(raw.OverlayTop);
        if (shared != null)
        {
            legacyLeft = null;
            legacyTop = null;
        }

        return new PositionMigration(
            PositionMemoryMode: ParsePositionMemoryMode(raw.PositionMemoryMode),
            SharedPosition: shared,
            PerDisplayPositions: perDisplay,
            OverlayTargetMonitorId: ParseText(raw.OverlayTargetMonitorId),
            PendingPresetMigrationTarget: ParseText(raw.PendingPresetMigrationTarget),
            LegacyLeft: legacyLeft.HasValue && legacyTop.HasValue ? legacyLeft : null,
            LegacyTop: legacyLeft.HasValue && legacyTop.HasValue ? legacyTop : null);
    }

    private static PositionMigration ParseLegacyPosition(RawSettingsDto raw)
    {
        OverlayPositionPreset? preset = ParsePositionPreset(raw.OverlayPosition);
        bool hasPresetKey = HasJsonValue(raw.OverlayPosition);
        OverlayPositionMemoryMode mode = ParseLegacyMemoryMode(raw.OverlayCustomPositionMode);
        IReadOnlyDictionary<string, OverlayPositionState>? perDisplay =
            ParseLegacyPerDisplayPositions(raw.OverlayPerDisplayPositions);

        double? xRatio = ParseFiniteRatio(raw.OverlayXRatio);
        double? yRatio = ParseFiniteRatio(raw.OverlayYRatio);
        OverlayPositionState? shared = null;
        if (preset.HasValue)
        {
            shared = new OverlayPositionState.Preset(preset.Value);
        }
        else if (xRatio.HasValue && yRatio.HasValue)
        {
            shared = new OverlayPositionState.Custom(xRatio.Value, yRatio.Value);
        }

        double? left = ParseFiniteDouble(raw.OverlayLeft);
        double? top = ParseFiniteDouble(raw.OverlayTop);
        if (shared != null || hasPresetKey || xRatio.HasValue || yRatio.HasValue)
        {
            left = null;
            top = null;
        }

        string? target = ParseText(raw.OverlayCustomMonitorId)
            ?? ParseText(raw.OverlayMonitorDeviceName);
        string? pendingPresetTarget = mode == OverlayPositionMemoryMode.RememberPerDisplay
            && preset.HasValue
            ? target
            : null;

        return new PositionMigration(
            PositionMemoryMode: mode,
            SharedPosition: shared,
            PerDisplayPositions: perDisplay,
            OverlayTargetMonitorId: target,
            PendingPresetMigrationTarget: pendingPresetTarget,
            LegacyLeft: left.HasValue && top.HasValue ? left : null,
            LegacyTop: left.HasValue && top.HasValue ? top : null);
    }

    private static OverlayPositionMemoryMode ParsePositionMemoryMode(JsonElement? element)
    {
        string? value = ParseText(element);
        if (value != null
            && Enum.TryParse<OverlayPositionMemoryMode>(value, true, out var mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        return OverlayPositionMemoryMode.SharedAcrossDisplays;
    }

    private static OverlayPositionMemoryMode ParseLegacyMemoryMode(JsonElement? element)
    {
        string? value = ParseText(element);
        if (string.Equals(
                value,
                nameof(OverlayCustomPositionMode.RememberPerDisplay),
                StringComparison.OrdinalIgnoreCase))
        {
            return OverlayPositionMemoryMode.RememberPerDisplay;
        }

        return OverlayPositionMemoryMode.SharedAcrossDisplays;
    }

    private static bool HasNewAppearanceFields(RawSettingsDto raw)
    {
        return HasJsonValue(raw.AppearanceMemoryMode)
            || HasJsonValue(raw.SharedAppearance)
            || HasJsonValue(raw.PerDisplayAppearances);
    }

    private static AppearanceMigration ParseNewAppearance(RawSettingsDto raw)
    {
        return new AppearanceMigration(
            AppearanceMemoryMode: ParseAppearanceMemoryMode(raw.AppearanceMemoryMode),
            SharedAppearance: ParseAppearanceState(raw.SharedAppearance),
            PerDisplayAppearances: ParseAppearanceStates(raw.PerDisplayAppearances));
    }

    private static AppearanceMigration ParseLegacyAppearance(RawSettingsDto raw)
    {
        return new AppearanceMigration(
            OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            null,
            null);
    }

    private static OverlayAppearanceMemoryMode ParseAppearanceMemoryMode(JsonElement? element)
    {
        string? value = ParseText(element);
        if (value != null
            && Enum.TryParse<OverlayAppearanceMemoryMode>(value, true, out var mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        return OverlayAppearanceMemoryMode.SharedAcrossDisplays;
    }

    private static OverlayAppearanceState? ParseAppearanceState(JsonElement? element)
    {
        if (!TryGetObject(element, out var state))
        {
            return null;
        }

        OverlayThemePreference? theme = TryParseOverlayTheme(
            GetStringProperty(state, "ThemePreference"));
        OverlayOpacityPreference? opacity = TryParseOverlayOpacity(
            GetStringProperty(state, "OpacityPreference"));
        if (theme == null || opacity == null)
        {
            return null;
        }

        return new OverlayAppearanceState(theme.Value, opacity.Value);
    }

    private static IReadOnlyDictionary<string, OverlayAppearanceState>? ParseAppearanceStates(
        JsonElement? element)
    {
        if (!TryGetObject(element, out var appearances))
        {
            return null;
        }

        var result = new Dictionary<string, OverlayAppearanceState>(
            StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in appearances.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            OverlayAppearanceState? state = ParseAppearanceState(property.Value);
            if (state != null)
            {
                result[property.Name] = state;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static OverlayThemePreference? TryParseOverlayTheme(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "followapplication" => OverlayThemePreference.FollowApplication,
            "system" => OverlayThemePreference.System,
            "light" => OverlayThemePreference.Light,
            "dark" => OverlayThemePreference.Dark,
            _ => null
        };
    }

    private static OverlayOpacityPreference? TryParseOverlayOpacity(string? value)
    {
        if (value != null
            && Enum.TryParse<OverlayOpacityPreference>(value, true, out var preference)
            && Enum.IsDefined(preference))
        {
            return preference;
        }

        return null;
    }

    private static OverlayPositionState? ParsePositionState(JsonElement? element)
    {
        if (!TryGetObject(element, out var state))
        {
            return null;
        }

        string? kind = GetStringProperty(state, "Kind");
        if (string.Equals(kind, "Preset", StringComparison.OrdinalIgnoreCase))
        {
            OverlayPositionPreset? preset = ParsePositionPreset(GetProperty(state, "Preset"));
            return preset.HasValue ? new OverlayPositionState.Preset(preset.Value) : null;
        }

        if (string.Equals(kind, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            double? xRatio = ParseFiniteRatio(GetProperty(state, "XRatio"));
            double? yRatio = ParseFiniteRatio(GetProperty(state, "YRatio"));
            return xRatio.HasValue && yRatio.HasValue
                ? new OverlayPositionState.Custom(xRatio.Value, yRatio.Value)
                : null;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, OverlayPositionState>? ParsePositionStates(
        JsonElement? element)
    {
        if (!TryGetObject(element, out var positions))
        {
            return null;
        }

        var result = new Dictionary<string, OverlayPositionState>(
            StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in positions.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            OverlayPositionState? state = ParsePositionState(property.Value);
            if (state != null)
            {
                result[property.Name] = state;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static IReadOnlyDictionary<string, OverlayPositionState>? ParseLegacyPerDisplayPositions(
        JsonElement? element)
    {
        if (!TryGetObject(element, out var positions))
        {
            return null;
        }

        var result = new Dictionary<string, OverlayPositionState>(
            StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in positions.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name)
                || !TryGetObject(property.Value, out var legacy))
            {
                continue;
            }

            double? xRatio = ParseFiniteRatio(GetProperty(legacy, "XRatio"));
            double? yRatio = ParseFiniteRatio(GetProperty(legacy, "YRatio"));
            if (xRatio.HasValue && yRatio.HasValue)
            {
                result[property.Name] = new OverlayPositionState.Custom(
                    xRatio.Value,
                    yRatio.Value);
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static OverlayPositionPreset? ParsePositionPreset(JsonElement? element)
    {
        string? value = ParseText(element);
        if (value != null
            && Enum.TryParse<OverlayPositionPreset>(value, true, out var preset)
            && Enum.IsDefined(preset))
        {
            return preset;
        }

        return null;
    }

    private static double? ParseFiniteRatio(JsonElement? element)
    {
        double? value = ParseFiniteDouble(element);
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value < 0.0
            ? 0.0
            : value.Value > 1.0
                ? 1.0
                : value.Value;
    }

    private static double? ParseFiniteDouble(JsonElement? element)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (!element.Value.TryGetDouble(out double value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return null;
        }

        return value;
    }

    private static string? ParseText(JsonElement? element)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return ParseText(element.Value.GetString());
    }

    private static string? ParseText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool HasJsonValue(JsonElement? element)
    {
        return element.HasValue && element.Value.ValueKind != JsonValueKind.Null;
    }

    private static bool TryGetObject(JsonElement? element, out JsonElement value)
    {
        value = default;
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = element.Value;
        return true;
    }

    private static JsonElement? GetProperty(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        return ParseText(GetProperty(element, name));
    }

    private static MetricWindow ParseMetricWindow(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.SelectedWindow;
        }

        if (Enum.TryParse<MetricWindow>(value, true, out var window) && Enum.IsDefined(window))
        {
            return window;
        }

        return TraySettings.Default.SelectedWindow;
    }

    private static RefreshCadence ParseRefreshCadence(int? value)
    {
        return value.HasValue
            ? RefreshCadenceExtensions.FromSeconds(value.Value)
            : TraySettings.Default.RefreshCadence;
    }

    private static Language ParseLanguage(string? value)
    {
        if (value != null
            && Enum.TryParse<Language>(value, true, out var language)
            && Enum.IsDefined(language))
        {
            return language;
        }

        return TraySettings.Default.Language;
    }

    private static ApplicationThemePreference ParseApplicationTheme(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "system" => ApplicationThemePreference.System,
            "light" => ApplicationThemePreference.Light,
            "dark" => ApplicationThemePreference.Dark,
            _ => TraySettings.Default.ApplicationTheme
        };
    }

    private static OverlayThemePreference ParseOverlayTheme(string? value)
    {
        return value?.ToLowerInvariant() switch
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
        if (value != null
            && Enum.TryParse<OverlayOpacityPreference>(value, true, out var preference)
            && Enum.IsDefined(preference))
        {
            return preference;
        }

        return TraySettings.Default.OverlayOpacity;
    }

    private static CodexDataSourceSelection ParseDataSource(JsonElement? element)
    {
        if (!TryGetObject(element, out var dataSource))
        {
            return CodexDataSourceSelection.Windows;
        }

        string? kind = GetStringProperty(dataSource, "Kind");
        string? distroName = GetStringProperty(dataSource, "WslDistributionName");

        if (string.Equals(kind, nameof(CodexDataSourceKind.Windows), StringComparison.OrdinalIgnoreCase))
        {
            return CodexDataSourceSelection.Windows;
        }

        if (string.Equals(kind, nameof(CodexDataSourceKind.Wsl), StringComparison.OrdinalIgnoreCase)
            && distroName != null)
        {
            try
            {
                return CodexDataSourceSelection.ForWsl(distroName);
            }
            catch (ArgumentException)
            {
                return CodexDataSourceSelection.Windows;
            }
        }

        return CodexDataSourceSelection.Windows;
    }

    private static JsonElement BuildPositionStateElement(OverlayPositionState state)
    {
        PositionStateDto dto = state switch
        {
            OverlayPositionState.Preset preset => new PositionStateDto
            {
                Kind = nameof(OverlayPositionState.Preset),
                Preset = preset.Value.ToString()
            },
            OverlayPositionState.Custom custom => new PositionStateDto
            {
                Kind = nameof(OverlayPositionState.Custom),
                XRatio = custom.XRatio,
                YRatio = custom.YRatio
            },
            _ => new PositionStateDto
            {
                Kind = nameof(OverlayPositionState.Preset),
                Preset = nameof(OverlayPositionPreset.TopRight)
            }
        };

        return JsonSerializer.SerializeToElement(dto, SerializerOptions);
    }

    private static JsonElement? BuildPositionStates(
        IReadOnlyDictionary<string, OverlayPositionState>? positions)
    {
        if (positions == null || positions.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in positions)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            {
                result[pair.Key] = BuildPositionStateElement(pair.Value);
            }
        }

        return result.Count == 0
            ? null
            : JsonSerializer.SerializeToElement(result, SerializerOptions);
    }

    private static JsonElement BuildAppearanceStateElement(OverlayAppearanceState state)
    {
        var dto = new AppearanceStateDto
        {
            ThemePreference = state.ThemePreference.ToString(),
            OpacityPreference = state.OpacityPreference.ToString()
        };

        return JsonSerializer.SerializeToElement(dto, SerializerOptions);
    }

    private static JsonElement? BuildAppearanceStates(
        IReadOnlyDictionary<string, OverlayAppearanceState>? appearances)
    {
        if (appearances == null || appearances.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in appearances)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            {
                result[pair.Key] = BuildAppearanceStateElement(pair.Value);
            }
        }

        return result.Count == 0
            ? null
            : JsonSerializer.SerializeToElement(result, SerializerOptions);
    }

    private static JsonElement? BuildDataSourceElement(CodexDataSourceSelection selection)
    {
        return JsonSerializer.SerializeToElement(new
        {
            Kind = selection.Kind.ToString(),
            WslDistributionName = selection.WslDistributionName
        }, DataSourceSerializerOptions);
    }

    private sealed class PositionStateDto
    {
        public string? Kind { get; set; }
        public string? Preset { get; set; }
        public double? XRatio { get; set; }
        public double? YRatio { get; set; }
    }

    private sealed class AppearanceStateDto
    {
        public string? ThemePreference { get; set; }
        public string? OpacityPreference { get; set; }
    }

    private sealed class RawSettingsDto
    {
        public string? SelectedWindow { get; set; }
        public int? RefreshCadence { get; set; }
        public string? Language { get; set; }
        public bool? OverlayEnabled { get; set; }
        public bool? OverlayLocked { get; set; }

        // Kept only as a migration payload for pre-v0.4.0 absolute coordinates.
        public JsonElement? OverlayLeft { get; set; }
        public JsonElement? OverlayTop { get; set; }

        public string? ApplicationTheme { get; set; }
        public string? OverlayTheme { get; set; }
        public string? OverlayOpacity { get; set; }

        // Legacy position keys are read but never populated by RawSettingsDto(TraySettings).
        public JsonElement? OverlayPosition { get; set; }
        public JsonElement? OverlayMonitorDeviceName { get; set; }
        public JsonElement? OverlayCustomPositionMode { get; set; }
        public JsonElement? OverlayXRatio { get; set; }
        public JsonElement? OverlayYRatio { get; set; }
        public JsonElement? OverlayPerDisplayPositions { get; set; }
        public JsonElement? OverlayCustomMonitorId { get; set; }

        public JsonElement? PositionMemoryMode { get; set; }
        public JsonElement? SharedPosition { get; set; }
        public JsonElement? PerDisplayPositions { get; set; }
        public JsonElement? OverlayTargetMonitorId { get; set; }
        public JsonElement? PendingPresetMigrationTarget { get; set; }

        public JsonElement? AppearanceMemoryMode { get; set; }
        public JsonElement? SharedAppearance { get; set; }
        public JsonElement? PerDisplayAppearances { get; set; }

        public JsonElement? DataSource { get; set; }

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
            OverlayLeft = settings.SharedPosition == null
                ? BuildNumberElement(settings.OverlayLeft)
                : null;
            OverlayTop = settings.SharedPosition == null
                ? BuildNumberElement(settings.OverlayTop)
                : null;
            ApplicationTheme = settings.ApplicationTheme.ToString();
            OverlayTheme = settings.OverlayTheme.ToString();
            OverlayOpacity = settings.OverlayOpacity.ToString();

            PositionMemoryMode = JsonSerializer.SerializeToElement(
                settings.PositionMemoryMode.ToString(),
                SerializerOptions);
            SharedPosition = settings.SharedPosition == null
                ? null
                : BuildPositionStateElement(settings.SharedPosition);
            PerDisplayPositions = BuildPositionStates(settings.PerDisplayPositions);
            OverlayTargetMonitorId = BuildTextElement(settings.OverlayTargetMonitorId);
            PendingPresetMigrationTarget = BuildTextElement(settings.PendingPresetMigrationTarget);
            AppearanceMemoryMode = JsonSerializer.SerializeToElement(
                settings.AppearanceMemoryMode.ToString(),
                SerializerOptions);
            SharedAppearance = settings.SharedAppearance == null
                ? null
                : BuildAppearanceStateElement(settings.SharedAppearance);
            PerDisplayAppearances = BuildAppearanceStates(settings.PerDisplayAppearances);
            DataSource = BuildDataSourceElement(settings.DataSource);
        }
    }

    private static JsonElement? BuildTextElement(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : JsonSerializer.SerializeToElement(value.Trim(), SerializerOptions);
    }

    private static JsonElement? BuildNumberElement(double? value)
    {
        return value.HasValue
            ? JsonSerializer.SerializeToElement(value.Value, SerializerOptions)
            : null;
    }
}
