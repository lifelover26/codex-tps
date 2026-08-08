namespace CodexTPSTray;

/// <summary>
/// The axis a Shift-drag is constrained to while Shift is held.
/// <see cref="None"/> means the direction has not been decided yet and the
/// window follows the mouse freely.
/// </summary>
internal enum AxisLockDirection
{
    None = 0,
    Horizontal,
    Vertical
}

/// <summary>
/// Pure, desktop-free helper that decides whether a Shift-drag should lock to
/// the horizontal or vertical axis. Kept side-effect free so the direction rule
/// can be unit tested without a real window.
/// </summary>
internal static class AxisLockCalculator
{
    /// <summary>
    /// Movement in screen pixels on the dominant axis below this value does not
    /// yet commit to an axis, so tiny jitter at press time cannot choose the
    /// wrong constraint.
    /// </summary>
    public const double DefaultThreshold = 4.0;

    /// <summary>
    /// Returns the axis-lock direction for the accumulated mouse displacement
    /// since Shift was pressed. Once a direction has been chosen it is returned
    /// unchanged for as long as Shift stays held, so the user cannot switch
    /// axes mid-drag. Releasing Shift clears the direction externally.
    /// The threshold is applied as a 1-D check on the dominant axis:
    /// |dominant| must strictly exceed <paramref name="threshold"/>.
    /// Horizontal wins only when |deltaX| is strictly greater than |deltaY|;
    /// a tie locks vertically, which is deterministic.
    /// </summary>
    public static AxisLockDirection DetermineDirection(
        double deltaX,
        double deltaY,
        double threshold,
        AxisLockDirection current)
    {
        if (current != AxisLockDirection.None)
            return current;

        double absX = Math.Abs(deltaX);
        double absY = Math.Abs(deltaY);

        if (absX > absY)
        {
            if (absX > threshold)
                return AxisLockDirection.Horizontal;
            return AxisLockDirection.None;
        }

        if (absY > threshold)
            return AxisLockDirection.Vertical;
        return AxisLockDirection.None;
    }
}
