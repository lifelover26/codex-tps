using CodexTPSCore;

namespace CodexTPSTray;

// The primary (positional) constructor below is the compatibility constructor:
// it carries every field the legacy call sites pass and implicitly defaults
// DataSource to Windows. A secondary constructor (at the bottom of the type)
// accepts an explicit DataSource for callers — notably TraySettingsStore —
// that need to materialize a persisted WSL selection.
//
// DataSource is a non-nullable init-only property backed by a private field.
// The field defaults to CodexDataSourceSelection.Windows, so every TraySettings
// — including those created via the compatibility constructor and Default —
// carries a valid Windows selection with no null state. There is no "null
// means Windows" runtime convention anywhere.
//
// The custom init accessor is the single null-rejection point: it throws
// ArgumentNullException for a null assignment, whether that assignment comes
// from the secondary constructor or from a `with { DataSource = ... }`
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
    // to the secondary constructor below AND to `with { DataSource = ... }`,
    // so `with { DataSource = null! }` throws rather than producing a null
    // field. A non-null, already-validated CodexDataSourceSelection is stored
    // directly (the selection itself enforces Kind/name invariants).
    public CodexDataSourceSelection DataSource
    {
        get => _dataSource;
        init => _dataSource =
            value ?? throw new ArgumentNullException(nameof(DataSource));
    }

    // Full constructor: lets TraySettingsStore (and any future caller) supply
    // an explicit DataSource. Forwards to the compatibility constructor for
    // every other field, then routes the argument through the init accessor so
    // null rejection lives in exactly one place (the accessor) instead of
    // being duplicated here.
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
    );
}
