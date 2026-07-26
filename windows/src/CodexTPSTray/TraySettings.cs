namespace CodexTPSTray;

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