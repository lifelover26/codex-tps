using System.Collections.Generic;

namespace CodexTPSTray;

/// <summary>
/// Resolves a stable physical monitor identifier that survives Windows "show
/// only on 1 / show only on 2" topology changes, where the GDI source name
/// (e.g. "\\.\DISPLAY1") is reused for different physical panels.
/// </summary>
public interface IDisplayIdentityProvider
{
    /// <summary>
    /// Builds a mapping from GDI view device name (the value exposed by
    /// <c>System.Windows.Forms.Screen.DeviceName</c>) to a stable physical
    /// monitor identifier. Returns an empty map when the native CCD API is
    /// unavailable; callers must fall back to the GDI device name in that case.
    /// </summary>
    IReadOnlyDictionary<string, string> BuildDeviceNameToStableIdMap();
}
