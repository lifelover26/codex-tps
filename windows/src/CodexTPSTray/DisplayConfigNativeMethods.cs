using System;
using System.Runtime.InteropServices;

namespace CodexTPSTray;

/// <summary>
/// Isolated P/Invoke declarations for the Win32 CCD (Configuration and
/// Connectivity Database) API used to resolve stable physical monitor
/// identifiers. The layouts mirror the corresponding wingdi.h definitions.
/// </summary>
internal static class DisplayConfigNativeMethods
{
    public const uint QDC_ALL_PATHS = 0x00000001;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_SUCCESS = 0;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
    public static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
    public static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : int
    {
        DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
        DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2
    }

    public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : int
    {
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = -1
    }

    public enum DISPLAYCONFIG_ROTATION : int
    {
        DISPLAYCONFIG_ROTATION_IDENTITY = 1
    }

    public enum DISPLAYCONFIG_SCALING : int
    {
        DISPLAYCONFIG_SCALING_IDENTITY = 1
    }

    public enum DISPLAYCONFIG_SCANLINE_ORDERING : int
    {
        DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0
    }

    public enum DISPLAYCONFIG_PIXELFORMAT : int
    {
        DISPLAYCONFIG_PIXELFORMAT_8BPP = 1
    }

    public enum DISPLAYCONFIG_MODE_INFO_TYPE : int
    {
        DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
        DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2,
        DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO_MODE_INFO
    {
        [FieldOffset(0)]
        public uint modeInfoIdx;

        [FieldOffset(0)]
        public ushort cloneGroupId;

        [FieldOffset(2)]
        public ushort sourceModeInfoIdx;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public DISPLAYCONFIG_PATH_SOURCE_INFO_MODE_INFO modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO_MODE_INFO
    {
        [FieldOffset(0)]
        public uint modeInfoIdx;

        [FieldOffset(0)]
        public ushort desktopModeInfoIdx;

        [FieldOffset(2)]
        public ushort targetModeInfoIdx;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public DISPLAYCONFIG_PATH_TARGET_INFO_MODE_INFO modeInfoIdx;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public DISPLAYCONFIG_ROTATION rotation;
        public DISPLAYCONFIG_SCALING scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO_ADDITIONAL_SIGNAL_INFO
    {
        [FieldOffset(0)]
        public uint value;

        public ushort VideoStandard
        {
            readonly get => (ushort)(value & 0xFFFFu);
            set => this.value = (this.value & 0xFFFF0000u) | ((uint)value & 0xFFFFu);
        }

        public byte VSyncFreqDivider
        {
            readonly get => (byte)((value >> 16) & 0x3Fu);
            set => this.value = (this.value & ~(0x3Fu << 16)) | (((uint)value & 0x3Fu) << 16);
        }

        public uint Reserved
        {
            readonly get => value >> 22;
            set => this.value = (this.value & 0x003FFFFFu) | ((value & 0x3FFu) << 22);
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO_UNION
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO_ADDITIONAL_SIGNAL_INFO AdditionalSignalInfo;

        [FieldOffset(0)]
        public uint videoStandard;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO_UNION additionalSignalInfo;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct RECTL
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public RECTL DesktopImageRegion;
        public RECTL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION info;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS_BITS
    {
        [FieldOffset(0)]
        public uint value;

        public bool FriendlyNameFromEdid
        {
            readonly get => (value & 0x00000001u) != 0;
            set => this.value = value ? this.value | 0x00000001u : this.value & ~0x00000001u;
        }

        public bool FriendlyNameForced
        {
            readonly get => (value & 0x00000002u) != 0;
            set => this.value = value ? this.value | 0x00000002u : this.value & ~0x00000002u;
        }

        public bool EdidIdsValid
        {
            readonly get => (value & 0x00000004u) != 0;
            set => this.value = value ? this.value | 0x00000004u : this.value & ~0x00000004u;
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS_BITS bits;

        [FieldOffset(0)]
        public uint value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? monitorDevicePath;
    }
}
