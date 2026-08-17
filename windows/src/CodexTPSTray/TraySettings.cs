using System.Collections.Generic;
using CodexTPSCore;

namespace CodexTPSTray;

// The primary (positional) constructor below is the compatibility constructor:
// it carries every field the legacy call sites pass and implicitly defaults
// DataSource to Windows. A secondary constructor (at the bottom of the type)
// accepts an explicit DataSource for callers — notably TraySettingsStore —
// that need to materialize a persisted WSL selection. A third constructor
// accepts all fields including the custom-position ratios, per-display map,
// and target-monitor ID added for normalized overlay positioning.
//
// DataSource is a non-nullable init-only property backed by a private field.
// The field defaults to CodexDataSourceSelection.Windows, so every TraySettings
// — including those created via the compatibility constructor and Default —
// carries a valid Windows selection with no null state. There is no "null
// means Windows" runtime convention anywhere.
//
// The custom init accessor is the single null-rejection point: it throws
// ArgumentNullException for a null assignment, whether that assignment comes
// from a secondary constructor or from a `with { DataSource = ... }`
// expression. This closes the `with { DataSource = null! }` bypass that an
// auto-implemented init accessor would otherwise allow. A legitimate
// `with { DataSource = CodexDataSourceSelection.ForWsl("Ubuntu") }` continues
// to work, and a failed `with` leaves the source object untouched because
// `with` operates on a copy.
public record TraySettings(
    MetricWindow SelectedWindow,
    RefreshCadence RefreshCadence,
    Language Language = Language.English,
    bool OverlayEnabled = false,
    bool OverlayLocked = false,
    double? OverlayLeft = null,
    double? OverlayTop = null,
    ApplicationThemePreference ApplicationTheme = ApplicationThemePreference.System,
    OverlayThemePreference OverlayTheme = OverlayThemePreference.FollowApplication,
    OverlayOpacityPreference OverlayOpacity = OverlayOpacityPreference.Default,
    OverlayPositionPreset? OverlayPosition = OverlayPositionPreset.TopRight,
    string? OverlayMonitorDeviceName = null
)
{
    // Backing field for DataSource. The initializer guarantees a valid Windows
    // selection before any constructor body or init accessor runs, so the
    // compatibility constructor and Default need no explicit assignment.
    private CodexDataSourceSelection _dataSource = CodexDataSourceSelection.Windows;

    // The custom init accessor is the single null-rejection point. It applies
    // to the secondary constructors below AND to `with { DataSource = ... }`,
    // so `with { DataSource = null! }` throws rather than producing a null
    // field. A non-null, already-validated CodexDataSourceSelection is stored
    // directly (the selection itself enforces Kind/name invariants).
    public CodexDataSourceSelection DataSource
    {
        get => _dataSource;
        init => _dataSource =
            value ?? throw new ArgumentNullException(nameof(DataSource));
    }

    // Custom-position properties (normalized coordinate model). These are
    // init-only auto-properties with safe defaults so that legacy call sites
    // that use the 12-parameter primary constructor automatically get the
    // KeepRelative default with no ratios/per-display map/target monitor.
    public OverlayCustomPositionMode OverlayCustomPositionMode { get; init; } = OverlayCustomPositionMode.KeepRelative;
    public double? OverlayXRatio { get; init; } = null;
    public double? OverlayYRatio { get; init; } = null;
    public IReadOnlyDictionary<string, DisplayRelativePosition>? OverlayPerDisplayPositions { get; init; } = null;
    public string? OverlayCustomMonitorId { get; init; } = null;

    // Compatibility constructor: 12 original parameters + explicit DataSource.
    // Used by TraySettingsDataSourceTests.FullConstructor_* and any other legacy
    // call site that names the DataSource parameter. Custom-position fields
    // receive their property defaults (KeepRelative, null ratios, null map,
    // null target monitor).
    public TraySettings(
        MetricWindow SelectedWindow,
        RefreshCadence RefreshCadence,
        Language Language,
        bool OverlayEnabled,
        bool OverlayLocked,
        double? OverlayLeft,
        double? OverlayTop,
        ApplicationThemePreference ApplicationTheme,
        OverlayThemePreference OverlayTheme,
        OverlayOpacityPreference OverlayOpacity,
        OverlayPositionPreset? OverlayPosition,
        string? OverlayMonitorDeviceName,
        CodexDataSourceSelection DataSource)
        : this(
            SelectedWindow,
            RefreshCadence,
            Language,
            OverlayEnabled,
            OverlayLocked,
            OverlayLeft,
            OverlayTop,
            ApplicationTheme,
            OverlayTheme,
            OverlayOpacity,
            OverlayPosition,
            OverlayMonitorDeviceName)
    {
        this.DataSource = DataSource;
    }

    // Full constructor: accepts every field including the five custom-position
    // properties and an explicit DataSource. Used by TraySettingsStore when
    // materializing persisted settings and by OverlayCustomPositionCoordinator
    // when producing updated settings after migration or first-seen
    // inheritance. Forwards to the 12-parameter primary constructor then sets
    // the remaining properties through their init accessors so that the
    // DataSource null-rejection logic lives in exactly one place.
    public TraySettings(
        MetricWindow SelectedWindow,
        RefreshCadence RefreshCadence,
        Language Language,
        bool OverlayEnabled,
        bool OverlayLocked,
        double? OverlayLeft,
        double? OverlayTop,
        ApplicationThemePreference ApplicationTheme,
        OverlayThemePreference OverlayTheme,
        OverlayOpacityPreference OverlayOpacity,
        OverlayPositionPreset? OverlayPosition,
        string? OverlayMonitorDeviceName,
        OverlayCustomPositionMode OverlayCustomPositionMode,
        double? OverlayXRatio,
        double? OverlayYRatio,
        IReadOnlyDictionary<string, DisplayRelativePosition>? OverlayPerDisplayPositions,
        string? OverlayCustomMonitorId,
        CodexDataSourceSelection DataSource)
        : this(
            SelectedWindow,
            RefreshCadence,
            Language,
            OverlayEnabled,
            OverlayLocked,
            OverlayLeft,
            OverlayTop,
            ApplicationTheme,
            OverlayTheme,
            OverlayOpacity,
            OverlayPosition,
            OverlayMonitorDeviceName)
    {
        this.OverlayCustomPositionMode = OverlayCustomPositionMode;
        this.OverlayXRatio = OverlayXRatio;
        this.OverlayYRatio = OverlayYRatio;
        this.OverlayPerDisplayPositions = OverlayPerDisplayPositions;
        this.OverlayCustomMonitorId = OverlayCustomMonitorId;
        this.DataSource = DataSource;
    }

    public static TraySettings Default { get; } = new(
        SelectedWindow: MetricWindow.OneMinute,
        RefreshCadence: RefreshCadence.FifteenSeconds,
        Language: Language.English,
        OverlayEnabled: false,
        OverlayLocked: false,
        OverlayLeft: null,
        OverlayTop: null,
        ApplicationTheme: ApplicationThemePreference.System,
        OverlayTheme: OverlayThemePreference.FollowApplication,
        OverlayOpacity: OverlayOpacityPreference.Default,
        OverlayPosition: OverlayPositionPreset.TopRight,
        OverlayMonitorDeviceName: null
    )
    {
        OverlayCustomPositionMode = OverlayCustomPositionMode.KeepRelative,
        OverlayXRatio = null,
        OverlayYRatio = null,
        OverlayPerDisplayPositions = null,
        OverlayCustomMonitorId = null
    };
}
