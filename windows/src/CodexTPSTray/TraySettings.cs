namespace CodexTPSTray;

public record TraySettings(
    MetricWindow SelectedWindow,
    RefreshCadence RefreshCadence,
    Language Language = Language.English
)
{
    public static TraySettings Default { get; } = new(
        SelectedWindow: MetricWindow.OneMinute,
        RefreshCadence: RefreshCadence.FifteenSeconds,
        Language: Language.English
    );
}