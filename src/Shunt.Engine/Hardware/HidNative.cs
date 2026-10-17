using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Shunt.Engine.Hardware;

/// <summary>Minimal SetupAPI/HID declarations used by the phase 3 motion reader.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static partial class HidNative
{
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, IntPtr detail, uint detailSize, out uint requiredSize, ref DeviceInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    private static readonly Regex VidPidPattern = new(@"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<(string Path, int VendorId, int ProductId)> EnumerateKnownImuCollections()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("HID sensor discovery requires Windows.");
        var devices = new List<(string, int, int)>();
        var interfaceGuid = HidInterfaceGuid;
        var set = SetupDiGetClassDevsW(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData)) break;

                var infoData = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                _ = SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, IntPtr.Zero, 0, out var required, ref infoData);
                if (required == 0) continue;
                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, detail, required, out _, ref infoData)) continue;
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var match = VidPidPattern.Match(path);
                    if (!match.Success) continue;
                    var vid = Convert.ToInt32(match.Groups[1].Value, 16);
                    var pid = Convert.ToInt32(match.Groups[2].Value, 16);
                    if (ImuDeviceProfile.Find(vid, pid) is not null) devices.Add((path, vid, pid));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { _ = SetupDiDestroyDeviceInfoList(set); }
        return devices;
    }

    public static SafeFileHandle OpenRead(string path)
    {
        var handle = CreateFileW(path, GenericRead, 0x1 | 0x2, IntPtr.Zero, OpenExisting, FileAttributeNormal | FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Could not open the controller's HID sensor collection.");
        }
        return handle;
    }
}
