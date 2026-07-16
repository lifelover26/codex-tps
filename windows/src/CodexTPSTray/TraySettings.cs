namespace CodexTPSTray;

public record TraySettings(
    MetricWindow SelectedWindow,
    RefreshCadence RefreshCadence,
    Language Language = Language.English,
    bool OverlayEnabled = false,
    bool OverlayLocked = false,
    double? OverlayLeft = null,
    double? OverlayTop = null
)
{
    public static TraySettings Default { get; } = new(
        SelectedWindow: MetricWindow.OneMinute,
        RefreshCadence: RefreshCadence.FifteenSeconds,
        Language: Language.English,
        OverlayEnabled: false,
        OverlayLocked: false,
        OverlayLeft: null,
        OverlayTop: null
    );
}