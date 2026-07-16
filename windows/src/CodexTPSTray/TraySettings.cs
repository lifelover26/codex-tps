namespace CodexTPSTray;

public record TraySettings(
    MetricWindow SelectedWindow,
    RefreshCadence RefreshCadence
)
{
    public static TraySettings Default { get; } = new(
        SelectedWindow: MetricWindow.OneMinute,
        RefreshCadence: RefreshCadence.FifteenSeconds
    );
}