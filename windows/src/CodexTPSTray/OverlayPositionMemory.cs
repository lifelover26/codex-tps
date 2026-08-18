namespace CodexTPSTray;

/// <summary>
/// How the overlay's saved position state is shared across displays:
/// one state for every display, or one full state per physical display.
/// This replaces the v0.4.0 OverlayCustomPositionMode, which only described
/// where CUSTOM ratios were kept and could not remember that a display's
/// last used position was a quick preset.
/// The closed position-state hierarchy is the persisted authority for both
/// strategies; legacy fields are read only by the settings migration layer.
/// </summary>
public enum OverlayPositionMemoryMode
{
    SharedAcrossDisplays,
    RememberPerDisplay
}

/// <summary>
/// A complete overlay position: either a quick preset or a custom
/// normalized ratio pair — never both, never neither. The private base
/// constructor makes the closed record hierarchy the only construction
/// path, so an invalid mixed/empty state cannot exist.
/// </summary>
public abstract record OverlayPositionState
{
    private OverlayPositionState()
    {
    }

    /// <summary>A quick position preset. Carries no ratios.</summary>
    public sealed record Preset : OverlayPositionState
    {
        public Preset(OverlayPositionPreset preset)
        {
            Value = Enum.IsDefined(preset) ? preset : OverlayPositionPreset.TopRight;
        }

        public OverlayPositionPreset Value { get; }
    }

    /// <summary>A custom position as normalized [0,1] ratios of a work area's
    /// movable range. Carries no preset.</summary>
    public sealed record Custom : OverlayPositionState
    {
        public Custom(double xRatio, double yRatio)
        {
            XRatio = NormalizeRatio(xRatio);
            YRatio = NormalizeRatio(yRatio);
        }

        public double XRatio { get; }
        public double YRatio { get; }

        private static double NormalizeRatio(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                return 0.0;
            if (value > 1.0)
                return 1.0;
            return value;
        }
    }
}
