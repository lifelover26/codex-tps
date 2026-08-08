namespace CodexTPSTray;

/// <summary>
/// The axis a Shift-drag is currently constrained to.
/// <see cref="None"/> means the gesture has not completed its first lock or
/// has reset near the immutable origin.
/// </summary>
internal enum AxisLockDirection
{
    None = 0,
    Horizontal,
    Vertical
}

/// <summary>
/// Direction state returned by the pure evaluator.
/// </summary>
internal readonly record struct AxisEvaluation(
    AxisLockDirection Direction,
    bool AxisChanged);

/// <summary>
/// A single origin-absolute projection result. The direction and coordinates
/// are produced together so the caller cannot update a diagnostic state while
/// accidentally using a different positioning algorithm.
/// </summary>
internal readonly record struct AxisProjection(
    AxisLockDirection Direction,
    int Left,
    int Top,
    double TransitionProgress);

/// <summary>
/// State machine and projection for temporary Shift-drag axis locking.
///
/// The mouse and window origins never change during a Shift segment. A strong
/// direction uses a hard rail through that origin. When the opposite axis
/// leads by more than <see cref="SwitchMargin"/>, a short fixed-pixel blend
/// moves from the old rail to the new rail. The blend is the deliberate,
/// visible meaning of the threshold; it is not an accumulated/re-anchored
/// position and therefore returns exactly to the original point at (0, 0).
/// </summary>
internal static class AxisLockCalculator
{
    /// <summary>
    /// Movement required before a first direction can begin entering a lock.
    /// </summary>
    public const double EnterThreshold = 6.0;

    /// <summary>
    /// Returning within this square resets the current direction and permits a
    /// new direction without releasing Shift.
    /// </summary>
    public const double ResetThreshold = 8.0;

    /// <summary>
    /// Opposite-axis advantage required to start a direction change.
    /// </summary>
    public const double SwitchMargin = 8.0;

    /// <summary>
    /// Pixel width of a direction transition after the switch margin. Keeping
    /// this finite makes the change feel smooth without softening the whole
    /// drag or introducing distance-dependent drift.
    /// </summary>
    public const double TransitionWidth = 12.0;

    /// <summary>
    /// Smooth Hermite interpolation from 0 to 1, clamped.
    /// </summary>
    public static double SmoothStep(double edge0, double edge1, double x)
    {
        if (x <= edge0)
            return 0.0;
        if (x >= edge1)
            return 1.0;

        double t = (x - edge0) / (edge1 - edge0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// Signed dominance in [-1, 1]. Positive values favor horizontal motion.
    /// </summary>
    public static double Dominance(double dx, double dy)
    {
        double absX = Math.Abs(dx);
        double absY = Math.Abs(dy);
        double sum = absX + absY;
        return sum <= 0.0 ? 0.0 : (absX - absY) / sum;
    }

    /// <summary>
    /// Computes coordinates and next direction from the same immutable-origin
    /// state. This is the only per-frame projection entry point used by the
    /// window.
    /// </summary>
    public static AxisProjection Project(
        double dx,
        double dy,
        int originLeft,
        int originTop,
        AxisLockDirection current)
    {
        double absX = Math.Abs(dx);
        double absY = Math.Abs(dy);

        if (absX <= ResetThreshold && absY <= ResetThreshold)
        {
            return Free(originLeft, originTop, dx, dy, AxisLockDirection.None);
        }

        return current switch
        {
            AxisLockDirection.Horizontal => ProjectFromHorizontal(
                dx, dy, originLeft, originTop),
            AxisLockDirection.Vertical => ProjectFromVertical(
                dx, dy, originLeft, originTop),
            _ => ProjectFromFree(dx, dy, originLeft, originTop)
        };
    }

    /// <summary>
    /// Compatibility projection for callers that only have the initial state.
    /// </summary>
    public static (int Left, int Top) ComputeTarget(
        double dx,
        double dy,
        int originLeft,
        int originTop)
    {
        AxisProjection projection = Project(
            dx, dy, originLeft, originTop, AxisLockDirection.None);
        return (projection.Left, projection.Top);
    }

    /// <summary>
    /// Explicit-state projection used by pure tests and diagnostics.
    /// </summary>
    public static (int Left, int Top) ComputeTarget(
        double dx,
        double dy,
        int originLeft,
        int originTop,
        AxisLockDirection current)
    {
        AxisProjection projection = Project(
            dx, dy, originLeft, originTop, current);
        return (projection.Left, projection.Top);
    }

    /// <summary>
    /// Evaluates only the direction while preserving the public pure helper
    /// used by existing tests. Coordinates should use <see cref="Project"/>.
    /// </summary>
    public static AxisEvaluation EvaluateDirection(
        double dx,
        double dy,
        AxisLockDirection current)
    {
        AxisProjection projection = Project(dx, dy, 0, 0, current);
        return new AxisEvaluation(
            projection.Direction,
            AxisChanged: projection.Direction != current);
    }

    private static AxisProjection ProjectFromFree(
        double dx,
        double dy,
        int originLeft,
        int originTop)
    {
        double absX = Math.Abs(dx);
        double absY = Math.Abs(dy);
        double horizontalLead = absX - absY;
        double verticalLead = absY - absX;

        if (Math.Max(absX, absY) <= EnterThreshold)
            return Free(originLeft, originTop, dx, dy, AxisLockDirection.None);

        if (horizontalLead > SwitchMargin)
        {
            double progress = TransitionProgress(horizontalLead);
            return Blend(
                dx,
                dy,
                originLeft,
                originTop,
                AxisLockDirection.None,
                AxisLockDirection.Horizontal,
                progress,
                progress >= 1.0
                    ? AxisLockDirection.Horizontal
                    : AxisLockDirection.None);
        }

        if (verticalLead > SwitchMargin)
        {
            double progress = TransitionProgress(verticalLead);
            return Blend(
                dx,
                dy,
                originLeft,
                originTop,
                AxisLockDirection.None,
                AxisLockDirection.Vertical,
                progress,
                progress >= 1.0
                    ? AxisLockDirection.Vertical
                    : AxisLockDirection.None);
        }

        return Free(originLeft, originTop, dx, dy, AxisLockDirection.None);
    }

    private static AxisProjection ProjectFromHorizontal(
        double dx,
        double dy,
        int originLeft,
        int originTop)
    {
        double verticalLead = Math.Abs(dy) - Math.Abs(dx);
        if (verticalLead <= SwitchMargin)
        {
            return Rail(
                originLeft,
                originTop,
                dx,
                dy,
                AxisLockDirection.Horizontal);
        }

        double progress = TransitionProgress(verticalLead);
        return Blend(
            dx,
            dy,
            originLeft,
            originTop,
            AxisLockDirection.Horizontal,
            AxisLockDirection.Vertical,
            progress,
            progress >= 1.0
                ? AxisLockDirection.Vertical
                : AxisLockDirection.Horizontal);
    }

    private static AxisProjection ProjectFromVertical(
        double dx,
        double dy,
        int originLeft,
        int originTop)
    {
        double horizontalLead = Math.Abs(dx) - Math.Abs(dy);
        if (horizontalLead <= SwitchMargin)
        {
            return Rail(
                originLeft,
                originTop,
                dx,
                dy,
                AxisLockDirection.Vertical);
        }

        double progress = TransitionProgress(horizontalLead);
        return Blend(
            dx,
            dy,
            originLeft,
            originTop,
            AxisLockDirection.Vertical,
            AxisLockDirection.Horizontal,
            progress,
            progress >= 1.0
                ? AxisLockDirection.Horizontal
                : AxisLockDirection.Vertical);
    }

    private static double TransitionProgress(double lead)
        => SmoothStep(SwitchMargin, SwitchMargin + TransitionWidth, lead);

    private static AxisProjection Free(
        int originLeft,
        int originTop,
        double dx,
        double dy,
        AxisLockDirection direction)
    {
        return new AxisProjection(
            direction,
            originLeft + (int)Math.Round(dx),
            originTop + (int)Math.Round(dy),
            0.0);
    }

    private static AxisProjection Rail(
        int originLeft,
        int originTop,
        double dx,
        double dy,
        AxisLockDirection direction)
    {
        return direction == AxisLockDirection.Horizontal
            ? new AxisProjection(
                direction,
                originLeft + (int)Math.Round(dx),
                originTop,
                0.0)
            : new AxisProjection(
                direction,
                originLeft,
                originTop + (int)Math.Round(dy),
                0.0);
    }

    private static AxisProjection Blend(
        double dx,
        double dy,
        int originLeft,
        int originTop,
        AxisLockDirection from,
        AxisLockDirection to,
        double progress,
        AxisLockDirection direction)
    {
        (double fromX, double fromY) = OffsetFor(from, dx, dy);
        (double toX, double toY) = OffsetFor(to, dx, dy);

        return new AxisProjection(
            direction,
            originLeft + (int)Math.Round(fromX + ((toX - fromX) * progress)),
            originTop + (int)Math.Round(fromY + ((toY - fromY) * progress)),
            progress);
    }

    private static (double X, double Y) OffsetFor(
        AxisLockDirection direction,
        double dx,
        double dy)
    {
        return direction switch
        {
            AxisLockDirection.Horizontal => (dx, 0.0),
            AxisLockDirection.Vertical => (0.0, dy),
            _ => (dx, dy)
        };
    }
}
