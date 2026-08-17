using System;

namespace CodexTPSTray;

internal interface IDisplayConfigApi
{
    int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        DisplayConfigNativeMethods.DISPLAYCONFIG_MODE_INFO[] modeInfoArray);

    int GetSourceDeviceName(
        ref DisplayConfigNativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    int GetTargetDeviceName(
        ref DisplayConfigNativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
}

internal sealed class NativeDisplayConfigApi : IDisplayConfigApi
{
    public int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements)
        => DisplayConfigNativeMethods.GetDisplayConfigBufferSizes(
            flags,
            out numPathArrayElements,
            out numModeInfoArrayElements);

    public int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        DisplayConfigNativeMethods.DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        DisplayConfigNativeMethods.DISPLAYCONFIG_MODE_INFO[] modeInfoArray)
        => DisplayConfigNativeMethods.QueryDisplayConfig(
            flags,
            ref numPathArrayElements,
            pathArray,
            ref numModeInfoArrayElements,
            modeInfoArray,
            IntPtr.Zero);

    public int GetSourceDeviceName(
        ref DisplayConfigNativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket)
        => DisplayConfigNativeMethods.DisplayConfigGetDeviceInfo(ref requestPacket);

    public int GetTargetDeviceName(
        ref DisplayConfigNativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket)
        => DisplayConfigNativeMethods.DisplayConfigGetDeviceInfo(ref requestPacket);
}
