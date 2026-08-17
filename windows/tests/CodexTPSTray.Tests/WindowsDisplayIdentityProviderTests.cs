using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using CodexTPSTray;
using Native = CodexTPSTray.DisplayConfigNativeMethods;

namespace CodexTPSTray.Tests;

public sealed class WindowsDisplayIdentityProviderTests
{
    private static readonly Native.LUID Adapter = new()
    {
        LowPart = 0x12345678,
        HighPart = 0x00000001
    };

    [Fact]
    public void BuildMap_PairsSourceGdiNameWithTargetMonitorDevicePath()
    {
        var path = CreatePath(sourceId: 7, targetId: 41);
        var api = CreateApiWithSuccess(path);
        api.SourceNames[7] = @"\\.\DISPLAY2";
        api.TargetReplies[41] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Office Panel",
            @"\\?\DISPLAY#MONITOR-A");

        var map = new WindowsDisplayIdentityProvider(api).BuildDeviceNameToStableIdMap();

        Assert.Equal(@"\\?\DISPLAY#MONITOR-A", map[@"\\.\DISPLAY2"]);
        Assert.Equal(
            Native.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
            api.SourceRequests[0].header.type);
        Assert.Equal(
            Native.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
            api.TargetRequests[0].header.type);
        uint sourcePacketSize = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
        Assert.Equal(84u, sourcePacketSize);
        Assert.Equal(sourcePacketSize, api.SourceRequests[0].header.size);
        Assert.Equal(
            (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
            api.TargetRequests[0].header.size);
    }

    [Fact]
    public void BuildMap_UsesDevicePathWhenFriendlyNamesMatch()
    {
        var first = CreatePath(sourceId: 1, targetId: 101);
        var second = CreatePath(sourceId: 2, targetId: 102);
        var api = CreateApiWithSuccess(first, second);
        api.SourceNames[1] = @"\\.\DISPLAY1";
        api.SourceNames[2] = @"\\.\DISPLAY2";
        api.TargetReplies[101] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Identical Friendly Name",
            @"\\?\DISPLAY#MONITOR-A");
        api.TargetReplies[102] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Identical Friendly Name",
            @"\\?\DISPLAY#MONITOR-B");

        var map = new WindowsDisplayIdentityProvider(api).BuildDeviceNameToStableIdMap();

        Assert.Equal(@"\\?\DISPLAY#MONITOR-A", map[@"\\.\DISPLAY1"]);
        Assert.Equal(@"\\?\DISPLAY#MONITOR-B", map[@"\\.\DISPLAY2"]);
        Assert.NotEqual(map[@"\\.\DISPLAY1"], map[@"\\.\DISPLAY2"]);
    }

    [Fact]
    public void BuildMap_SameGdiNameAcrossExclusiveTopologiesUsesEachTargetPath()
    {
        var topologyOne = CreatePath(sourceId: 0, targetId: 201);
        var topologyTwo = CreatePath(sourceId: 0, targetId: 202);
        var api = new FakeDisplayConfigApi();
        api.AddSuccessfulQuery(topologyOne);
        api.AddSuccessfulQuery(topologyTwo);
        api.SourceNames[0] = @"\\.\DISPLAY1";
        api.TargetReplies[201] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Same Friendly Name",
            @"\\?\DISPLAY#INTERNAL");
        api.TargetReplies[202] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Same Friendly Name",
            @"\\?\DISPLAY#EXTERNAL");

        var provider = new WindowsDisplayIdentityProvider(api);

        var firstMap = provider.BuildDeviceNameToStableIdMap();
        var secondMap = provider.BuildDeviceNameToStableIdMap();

        Assert.Equal(@"\\?\DISPLAY#INTERNAL", firstMap[@"\\.\DISPLAY1"]);
        Assert.Equal(@"\\?\DISPLAY#EXTERNAL", secondMap[@"\\.\DISPLAY1"]);
    }

    [Fact]
    public void EmptyTargetPath_FallsBackToDeviceName()
    {
        var path = CreatePath(sourceId: 3, targetId: 301);
        var api = CreateApiWithSuccess(path);
        api.SourceNames[3] = @"\\.\DISPLAY1";
        api.TargetReplies[301] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Friendly Name",
            "\0");

        var map = new WindowsDisplayIdentityProvider(api).BuildDeviceNameToStableIdMap();
        var monitor = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true);

        Assert.Empty(map);
        Assert.Equal(@"\\.\DISPLAY1", MonitorId.Resolve(monitor));
    }

    [Fact]
    public void TargetApiFailure_FallsBackToDeviceName()
    {
        var path = CreatePath(sourceId: 4, targetId: 401);
        var api = CreateApiWithSuccess(path);
        api.SourceNames[4] = @"\\.\DISPLAY1";
        api.TargetReplies[401] = new TargetReply(5, null, null);

        var map = new WindowsDisplayIdentityProvider(api).BuildDeviceNameToStableIdMap();
        var monitor = new MonitorInfo(
            DeviceName: @"\\.\DISPLAY1",
            WorkingArea: new Rect(0, 0, 1920, 1080),
            IsPrimary: true);

        Assert.Empty(map);
        Assert.Equal(@"\\.\DISPLAY1", MonitorId.Resolve(monitor));
    }

    [Fact]
    public void InsufficientBuffer_RereadsSizesBeforeRetrying()
    {
        var first = CreatePath(sourceId: 11, targetId: 501);
        var second = CreatePath(sourceId: 12, targetId: 502);
        var api = new FakeDisplayConfigApi();
        api.AddBufferSize(pathCount: 1, modeCount: 1);
        api.QueryResponses.Enqueue(new QueryResponse(
            Native.ERROR_INSUFFICIENT_BUFFER,
            Array.Empty<Native.DISPLAYCONFIG_PATH_INFO>(),
            Array.Empty<Native.DISPLAYCONFIG_MODE_INFO>(),
            ReturnedPathCount: 99,
            ReturnedModeCount: 99));
        api.AddBufferSize(pathCount: 2, modeCount: 1);
        api.QueryResponses.Enqueue(new QueryResponse(
            Native.ERROR_SUCCESS,
            new[] { first, second },
            new[] { new Native.DISPLAYCONFIG_MODE_INFO() },
            ReturnedPathCount: 2,
            ReturnedModeCount: 1));
        api.SourceNames[11] = @"\\.\DISPLAY1";
        api.SourceNames[12] = @"\\.\DISPLAY2";
        api.TargetReplies[501] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Panel 1",
            @"\\?\DISPLAY#MONITOR-1");
        api.TargetReplies[502] = new TargetReply(
            Native.ERROR_SUCCESS,
            "Panel 2",
            @"\\?\DISPLAY#MONITOR-2");

        var map = new WindowsDisplayIdentityProvider(api).BuildDeviceNameToStableIdMap();

        Assert.Equal(2, api.BufferSizeCalls);
        Assert.Equal(2, api.QueryCalls);
        Assert.Equal(1, api.QueryArrayLengths[0].PathCount);
        Assert.Equal(1, api.QueryArrayLengths[0].ModeCount);
        Assert.Equal(2, api.QueryArrayLengths[1].PathCount);
        Assert.Equal(1, api.QueryArrayLengths[1].ModeCount);
        Assert.All(api.BufferFlags, flags => Assert.Equal(Native.QDC_ONLY_ACTIVE_PATHS, flags));
        Assert.All(api.QueryFlags, flags => Assert.Equal(Native.QDC_ONLY_ACTIVE_PATHS, flags));
        Assert.Equal(@"\\?\DISPLAY#MONITOR-1", map[@"\\.\DISPLAY1"]);
        Assert.Equal(@"\\?\DISPLAY#MONITOR-2", map[@"\\.\DISPLAY2"]);
    }

    [Fact]
    public void InsufficientBuffer_RetryIsBounded()
    {
        var api = new FakeDisplayConfigApi();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            api.AddBufferSize(pathCount: 1, modeCount: 0);
            api.QueryResponses.Enqueue(new QueryResponse(
                Native.ERROR_INSUFFICIENT_BUFFER,
                Array.Empty<Native.DISPLAYCONFIG_PATH_INFO>(),
                Array.Empty<Native.DISPLAYCONFIG_MODE_INFO>(),
                ReturnedPathCount: 100,
                ReturnedModeCount: 100));
        }

        var map = new WindowsDisplayIdentityProvider(api).BuildDeviceNameToStableIdMap();

        Assert.Empty(map);
        Assert.Equal(3, api.BufferSizeCalls);
        Assert.Equal(3, api.QueryCalls);
    }

    [Fact]
    public void CcdStructures_MatchWin32Abi()
    {
        Assert.Equal(8, Marshal.SizeOf<Native.LUID>());
        Assert.Equal(4, Marshal.SizeOf<Native.DISPLAYCONFIG_PATH_SOURCE_INFO_MODE_INFO>());
        Assert.Equal(20, Marshal.SizeOf<Native.DISPLAYCONFIG_PATH_SOURCE_INFO>());
        Assert.Equal(4, Marshal.SizeOf<Native.DISPLAYCONFIG_PATH_TARGET_INFO_MODE_INFO>());
        Assert.Equal(48, Marshal.SizeOf<Native.DISPLAYCONFIG_PATH_TARGET_INFO>());
        Assert.Equal(72, Marshal.SizeOf<Native.DISPLAYCONFIG_PATH_INFO>());
        Assert.Equal(8, Marshal.SizeOf<Native.DISPLAYCONFIG_RATIONAL>());
        Assert.Equal(8, Marshal.SizeOf<Native.DISPLAYCONFIG_2DREGION>());
        Assert.Equal(4, Marshal.SizeOf<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO_UNION>());
        Assert.Equal(48, Marshal.SizeOf<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>());
        Assert.Equal(48, Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_MODE>());
        Assert.Equal(8, Marshal.SizeOf<Native.POINTL>());
        Assert.Equal(16, Marshal.SizeOf<Native.RECTL>());
        Assert.Equal(20, Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_MODE>());
        Assert.Equal(40, Marshal.SizeOf<Native.DISPLAYCONFIG_DESKTOP_IMAGE_INFO>());
        Assert.Equal(48, Marshal.SizeOf<Native.DISPLAYCONFIG_MODE_INFO_UNION>());
        Assert.Equal(64, Marshal.SizeOf<Native.DISPLAYCONFIG_MODE_INFO>());
        Assert.Equal(20, Marshal.SizeOf<Native.DISPLAYCONFIG_DEVICE_INFO_HEADER>());
        Assert.Equal(4, Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS>());
        Assert.Equal(84, Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>());
        Assert.Equal(420, Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>());

        AssertOffset<Native.DISPLAYCONFIG_PATH_SOURCE_INFO>(nameof(Native.DISPLAYCONFIG_PATH_SOURCE_INFO.adapterId), 0);
        AssertOffset<Native.DISPLAYCONFIG_PATH_SOURCE_INFO>(nameof(Native.DISPLAYCONFIG_PATH_SOURCE_INFO.id), 8);
        AssertOffset<Native.DISPLAYCONFIG_PATH_SOURCE_INFO>(nameof(Native.DISPLAYCONFIG_PATH_SOURCE_INFO.modeInfoIdx), 12);
        AssertOffset<Native.DISPLAYCONFIG_PATH_SOURCE_INFO>(nameof(Native.DISPLAYCONFIG_PATH_SOURCE_INFO.statusFlags), 16);

        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.adapterId), 0);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.id), 8);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.modeInfoIdx), 12);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.outputTechnology), 16);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.rotation), 20);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.scaling), 24);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.refreshRate), 28);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.scanLineOrdering), 36);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.targetAvailable), 40);
        AssertOffset<Native.DISPLAYCONFIG_PATH_TARGET_INFO>(nameof(Native.DISPLAYCONFIG_PATH_TARGET_INFO.statusFlags), 44);

        AssertOffset<Native.DISPLAYCONFIG_PATH_INFO>(nameof(Native.DISPLAYCONFIG_PATH_INFO.sourceInfo), 0);
        AssertOffset<Native.DISPLAYCONFIG_PATH_INFO>(nameof(Native.DISPLAYCONFIG_PATH_INFO.targetInfo), 20);
        AssertOffset<Native.DISPLAYCONFIG_PATH_INFO>(nameof(Native.DISPLAYCONFIG_PATH_INFO.flags), 68);

        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.pixelRate), 0);
        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.hSyncFreq), 8);
        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.vSyncFreq), 16);
        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.activeSize), 24);
        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.totalSize), 32);
        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.additionalSignalInfo), 40);
        AssertOffset<Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO>(nameof(Native.DISPLAYCONFIG_VIDEO_SIGNAL_INFO.scanLineOrdering), 44);

        AssertOffset<Native.DISPLAYCONFIG_SOURCE_MODE>(nameof(Native.DISPLAYCONFIG_SOURCE_MODE.width), 0);
        AssertOffset<Native.DISPLAYCONFIG_SOURCE_MODE>(nameof(Native.DISPLAYCONFIG_SOURCE_MODE.height), 4);
        AssertOffset<Native.DISPLAYCONFIG_SOURCE_MODE>(nameof(Native.DISPLAYCONFIG_SOURCE_MODE.pixelFormat), 8);
        AssertOffset<Native.DISPLAYCONFIG_SOURCE_MODE>(nameof(Native.DISPLAYCONFIG_SOURCE_MODE.position), 12);

        AssertOffset<Native.DISPLAYCONFIG_MODE_INFO>(nameof(Native.DISPLAYCONFIG_MODE_INFO.infoType), 0);
        AssertOffset<Native.DISPLAYCONFIG_MODE_INFO>(nameof(Native.DISPLAYCONFIG_MODE_INFO.id), 4);
        AssertOffset<Native.DISPLAYCONFIG_MODE_INFO>(nameof(Native.DISPLAYCONFIG_MODE_INFO.adapterId), 8);
        AssertOffset<Native.DISPLAYCONFIG_MODE_INFO>(nameof(Native.DISPLAYCONFIG_MODE_INFO.info), 16);
        Assert.Null(typeof(Native.DISPLAYCONFIG_MODE_INFO).GetField("modeInfoIdx"));

        AssertOffset<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME.header), 0);
        AssertOffset<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME.viewGdiDeviceName), 20);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.header), 0);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.flags), 20);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.outputTechnology), 24);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.edidManufactureId), 28);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.edidProductCodeId), 30);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.connectorInstance), 32);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorFriendlyDeviceName), 36);
        AssertOffset<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(nameof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath), 164);
    }

    private static void AssertOffset<T>(string fieldName, int expected)
        where T : struct
    {
        Assert.Equal(expected, Marshal.OffsetOf<T>(fieldName).ToInt32());
    }

    private static Native.DISPLAYCONFIG_PATH_INFO CreatePath(uint sourceId, uint targetId)
    {
        return new Native.DISPLAYCONFIG_PATH_INFO
        {
            sourceInfo = new Native.DISPLAYCONFIG_PATH_SOURCE_INFO
            {
                adapterId = Adapter,
                id = sourceId
            },
            targetInfo = new Native.DISPLAYCONFIG_PATH_TARGET_INFO
            {
                adapterId = Adapter,
                id = targetId,
                targetAvailable = true
            }
        };
    }

    private static FakeDisplayConfigApi CreateApiWithSuccess(
        params Native.DISPLAYCONFIG_PATH_INFO[] paths)
    {
        var api = new FakeDisplayConfigApi();
        api.AddSuccessfulQuery(paths);
        return api;
    }

    private sealed record TargetReply(int Status, string? FriendlyName, string? DevicePath);

    private sealed record QueryResponse(
        int Status,
        Native.DISPLAYCONFIG_PATH_INFO[] Paths,
        Native.DISPLAYCONFIG_MODE_INFO[] Modes,
        uint ReturnedPathCount,
        uint ReturnedModeCount);

    private sealed class FakeDisplayConfigApi : IDisplayConfigApi
    {
        public Dictionary<uint, string> SourceNames { get; } = new();
        public Dictionary<uint, TargetReply> TargetReplies { get; } = new();
        public Dictionary<uint, int> SourceStatuses { get; } = new();
        public Dictionary<uint, int> TargetStatuses { get; } = new();
        public Queue<(uint PathCount, uint ModeCount)> BufferSizes { get; } = new();
        public Queue<QueryResponse> QueryResponses { get; } = new();
        public List<uint> BufferFlags { get; } = new();
        public List<uint> QueryFlags { get; } = new();
        public List<(int PathCount, int ModeCount)> QueryArrayLengths { get; } = new();
        public List<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME> SourceRequests { get; } = new();
        public List<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME> TargetRequests { get; } = new();
        public int BufferSizeCalls { get; private set; }
        public int QueryCalls { get; private set; }

        public void AddBufferSize(uint pathCount, uint modeCount)
        {
            BufferSizes.Enqueue((pathCount, modeCount));
        }

        public void AddSuccessfulQuery(params Native.DISPLAYCONFIG_PATH_INFO[] paths)
        {
            AddBufferSize((uint)paths.Length, 0);
            QueryResponses.Enqueue(new QueryResponse(
                Native.ERROR_SUCCESS,
                paths,
                Array.Empty<Native.DISPLAYCONFIG_MODE_INFO>(),
                (uint)paths.Length,
                0));
        }

        public int GetDisplayConfigBufferSizes(
            uint flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements)
        {
            BufferSizeCalls++;
            BufferFlags.Add(flags);
            (numPathArrayElements, numModeInfoArrayElements) = BufferSizes.Dequeue();
            return Native.ERROR_SUCCESS;
        }

        public int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            Native.DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            Native.DISPLAYCONFIG_MODE_INFO[] modeInfoArray)
        {
            QueryCalls++;
            QueryFlags.Add(flags);
            QueryArrayLengths.Add((pathArray.Length, modeInfoArray.Length));
            QueryResponse response = QueryResponses.Dequeue();

            if (response.Status == Native.ERROR_SUCCESS)
            {
                Array.Copy(response.Paths, pathArray, response.Paths.Length);
                Array.Copy(response.Modes, modeInfoArray, response.Modes.Length);
            }

            numPathArrayElements = response.ReturnedPathCount;
            numModeInfoArrayElements = response.ReturnedModeCount;
            return response.Status;
        }

        public int GetSourceDeviceName(
            ref Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket)
        {
            SourceRequests.Add(requestPacket);
            if (SourceStatuses.TryGetValue(requestPacket.header.id, out int status))
                return status;

            if (!SourceNames.TryGetValue(requestPacket.header.id, out string? name))
                return 1;

            requestPacket.viewGdiDeviceName = name;
            return Native.ERROR_SUCCESS;
        }

        public int GetTargetDeviceName(
            ref Native.DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket)
        {
            TargetRequests.Add(requestPacket);
            if (TargetStatuses.TryGetValue(requestPacket.header.id, out int status))
                return status;

            if (!TargetReplies.TryGetValue(requestPacket.header.id, out TargetReply? reply))
                return 1;

            if (reply.Status != Native.ERROR_SUCCESS)
                return reply.Status;

            requestPacket.monitorFriendlyDeviceName = reply.FriendlyName;
            requestPacket.monitorDevicePath = reply.DevicePath;
            return Native.ERROR_SUCCESS;
        }
    }
}
