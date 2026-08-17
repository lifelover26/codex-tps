using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CodexTPSTray;

/// <summary>
/// Resolves stable physical monitor identifiers via the Win32 CCD API.
/// The monitor device path is the stable key; a missing path or native failure
/// leaves the caller to fall back to the GDI device name.
/// </summary>
public sealed class WindowsDisplayIdentityProvider : IDisplayIdentityProvider
{
    private const int MaxQueryAttempts = 3;

    private readonly IDisplayConfigApi _api;

    public WindowsDisplayIdentityProvider()
        : this(new NativeDisplayConfigApi())
    {
    }

    internal WindowsDisplayIdentityProvider(IDisplayConfigApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public IReadOnlyDictionary<string, string> BuildDeviceNameToStableIdMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!TryQueryActivePaths(out var paths))
                return map;

            foreach (var path in paths)
            {
                string? sourceName = TryGetSourceName(path.sourceInfo);
                if (string.IsNullOrWhiteSpace(sourceName))
                    continue;

                string? monitorDevicePath = TryGetTargetDevicePath(path.targetInfo);
                if (string.IsNullOrWhiteSpace(monitorDevicePath))
                    continue;

                map[sourceName] = monitorDevicePath;
            }
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return map;
    }

    private bool TryQueryActivePaths(
        out DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[] paths)
    {
        paths = Array.Empty<DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO>();

        for (int attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            int hr = _api.GetDisplayConfigBufferSizes(
                DisplayConfigNativeMethods.QDC_ONLY_ACTIVE_PATHS,
                out uint numPaths,
                out uint numModes);
            if (hr != DisplayConfigNativeMethods.ERROR_SUCCESS || numPaths == 0)
                return false;

            if (numPaths > int.MaxValue || numModes > int.MaxValue)
                return false;

            var pathArray = new DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[(int)numPaths];
            var modeArray = new DisplayConfigNativeMethods.DISPLAYCONFIG_MODE_INFO[(int)numModes];
            uint pathCount = numPaths;
            uint modeCount = numModes;

            hr = _api.QueryDisplayConfig(
                DisplayConfigNativeMethods.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount,
                pathArray,
                ref modeCount,
                modeArray);

            if (hr == DisplayConfigNativeMethods.ERROR_SUCCESS)
            {
                if (pathCount > (uint)pathArray.Length || modeCount > (uint)modeArray.Length)
                    return false;

                paths = CopyPrefix(pathArray, pathCount);
                return true;
            }

            if (hr != DisplayConfigNativeMethods.ERROR_INSUFFICIENT_BUFFER)
                return false;

            // QueryDisplayConfig can race a topology change. Its returned
            // counts are hints only; obtain a fresh pair before retrying.
        }

        return false;
    }

    private static DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[] CopyPrefix(
        DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[] source,
        uint count)
    {
        int length = checked((int)count);
        if (length == source.Length)
            return source;

        var result = new DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[length];
        Array.Copy(source, result, length);
        return result;
    }

    private string? TryGetSourceName(
        DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        var packet = new DisplayConfigNativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DisplayConfigNativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayConfigNativeMethods.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)Marshal.SizeOf<DisplayConfigNativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = source.adapterId,
                id = source.id
            },
            viewGdiDeviceName = string.Empty
        };

        int hr = _api.GetSourceDeviceName(ref packet);
        if (hr != DisplayConfigNativeMethods.ERROR_SUCCESS)
            return null;

        return TrimAtNull(packet.viewGdiDeviceName);
    }

    private string? TryGetTargetDevicePath(
        DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_TARGET_INFO target)
    {
        var packet = new DisplayConfigNativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DisplayConfigNativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayConfigNativeMethods.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DisplayConfigNativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = target.adapterId,
                id = target.id
            },
            monitorFriendlyDeviceName = string.Empty,
            monitorDevicePath = string.Empty
        };

        int hr = _api.GetTargetDeviceName(ref packet);
        if (hr != DisplayConfigNativeMethods.ERROR_SUCCESS)
            return null;

        return TrimAtNull(packet.monitorDevicePath);
    }

    private static string? TrimAtNull(string? value)
    {
        string result = value ?? string.Empty;
        int nullIndex = result.IndexOf('\0');
        if (nullIndex >= 0)
            result = result[..nullIndex];

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
