using System.Collections.Generic;
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
// from a secondary constructor or from a `with { DataSource = ... }`
// expression. This closes the `with { DataSource = null! }` bypass that an
// auto-implemented init accessor would otherwise allow. A legitimate
// `with { DataSource = CodexDataSourceSelection.ForWsl("Ubuntu") }` continues
// to work, and a failed `with` leaves the source object untouched because
// `with` operates on a copy.
//
// Overlay position model (single authority):
// - PositionMemoryMode: SharedAcrossDisplays or RememberPerDisplay.
// - SharedPosition: the single full OverlayPositionState used by every
//   display in shared mode, and the inheritance source for first-seen
//   displays and deferred v0.4.0 preset migration in per-display mode.
// - PerDisplayPositions: MonitorId -> full OverlayPositionState per physical
//   display (preset OR custom), used in per-display mode.
// - OverlayTargetMonitorId: the display the overlay last occupied; restore
//   targets it and falls back to the current monitor when it is absent
//   (never overwriting the stored id, so a reconnected display is restored).
//
// OverlayLeft/OverlayTop are NOT authoritative position state: they are a
// pre-v0.4.0 migration payload only. TraySettingsStore keeps them when an
// old file has no other position data, and OverlayPositionCoordinator
// converts them into a SharedPosition Custom state at first restore, then
// clears them forever. All v0.4.0 position fields (OverlayPosition,
// OverlayMonitorDeviceName, OverlayCustomPositionMode, OverlayXRatio,
// OverlayYRatio, OverlayPerDisplayPositions, OverlayCustomMonitorId) exist
// only as on-disk keys read by TraySettingsStore's one-time migration; they
// are gone from the in-memory model so they can never compete with the new
// fields as a second authority.
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
    OverlayPositionMemoryMode PositionMemoryMode = OverlayPositionMemoryMode.SharedAcrossDisplays,
    OverlayPositionState? SharedPosition = null,
    IReadOnlyDictionary<string, OverlayPositionState>? PerDisplayPositions = null,
    string? OverlayTargetMonitorId = null,
    string? PendingPresetMigrationTarget = null
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

    // Full constructor: every positional field plus an explicit DataSource.
    // Used by TraySettingsStore when materializing persisted settings and by
    // tests that need a WSL selection. Forwards to the primary constructor
    // then sets DataSource through its init accessor so the null-rejection
    // logic lives in exactly one place.
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
        OverlayPositionMemoryMode PositionMemoryMode,
        OverlayPositionState? SharedPosition,
        IReadOnlyDictionary<string, OverlayPositionState>? PerDisplayPositions,
        string? OverlayTargetMonitorId,
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
            PositionMemoryMode,
            SharedPosition,
            PerDisplayPositions,
            OverlayTargetMonitorId)
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
        PositionMemoryMode: OverlayPositionMemoryMode.SharedAcrossDisplays,
        SharedPosition: new OverlayPositionState.Preset(OverlayPositionPreset.TopRight),
        PerDisplayPositions: null,
        OverlayTargetMonitorId: null
    );
}
