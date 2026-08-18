using System;

namespace CodexTPSTray;

[Obsolete("Use OverlayPositionState.Custom; this type is only a legacy migration shape.")]
public sealed record DisplayRelativePosition(double XRatio, double YRatio);
