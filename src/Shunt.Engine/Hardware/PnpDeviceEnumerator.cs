using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Shunt.Engine.Hardware;

public record DiscoveredDeviceInfo(
    string InstanceId,
    string DevicePath,
    string Name,
    string HardwareType,
    string ConnectionType,
    int VendorId,
    int ProductId,
    bool IsConnected
);

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class PnpDeviceEnumerator
{
    // Well-known interface GUIDs
    public static readonly Guid GUID_DEVINTERFACE_GIP = new("020bc73c-0dca-4ee3-96d5-ab006ada5938");
    public static readonly Guid GUID_DEVINTERFACE_HID = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new("a5dcbf10-6530-11d2-901f-00c04fb951ed");

    // PnP SetupAPI flags
    private const uint DIGCF_DEFAULT = 0x00000001;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_ALLCLASSES = 0x00000004;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        byte[] PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_MFG = 0x0000000B;

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint pdnDevInst, string pDeviceID, int ulFlags);

    private static readonly Regex VidPidRegex = new(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.Compiled);

    /// <summary>
    /// Scans the system for all connected gaming controllers across GIP, USB, and HID.
    /// </summary>
    public static List<DiscoveredDeviceInfo> EnumerateControllers()
    {
        var results = new Dictionary<string, DiscoveredDeviceInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan Windows Registry for active XboxComposite and Gamepad USB devices first (provides rich bus descriptions)
        EnumerateFromRegistry(results);

        // 2. Enumerate GIP (Xbox One / Series / GameSir) interfaces
        EnumerateByInterfaceGuid(GUID_DEVINTERFACE_GIP, "Xbox", results);

        // 3. Scan HID interfaces for PlayStation, Nintendo, and generic gamepads
        EnumerateByInterfaceGuid(GUID_DEVINTERFACE_HID, "DirectInput", results, onlyGamingPeripherals: true);

        return [.. results.Values];
    }

    private static void EnumerateByInterfaceGuid(Guid interfaceGuid, string defaultType, Dictionary<string, DiscoveredDeviceInfo> results, bool onlyGamingPeripherals = false)
    {
        var hDevInfo = SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1)) return;

        try
        {
            var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            uint index = 0;

            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref interfaceGuid, index++, ref ifData))
            {
                var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0, out uint reqSize, ref devInfo);

                if (reqSize == 0) continue;

                var detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, detailBuffer, reqSize, out _, ref devInfo))
                    {
                        var pathPtr = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringAuto(pathPtr) ?? string.Empty;

                        int vid = 0, pid = 0;
                        var match = VidPidRegex.Match(devicePath);
                        if (match.Success)
                        {
                            vid = Convert.ToInt32(match.Groups[1].Value, 16);
                            pid = Convert.ToInt32(match.Groups[2].Value, 16);
                        }

                        // Ignore invalid / dummy device nodes without real VID/PID
                        if (vid == 0 && pid == 0) continue;

                        // Check if this is a gaming device if required
                        var (classifiedName, hwType) = ClassifyDevice(vid, pid);
                        if (onlyGamingPeripherals && hwType == "DirectInput" && !IsKnownGamingVidPid(vid, pid))
                        {
                            continue; // Skip non-gaming HID devices (like mice/keyboards)
                        }

                        string friendlyName = GetDevicePropertyString(hDevInfo, ref devInfo, SPDRP_FRIENDLYNAME);
                        if (string.IsNullOrWhiteSpace(friendlyName))
                        {
                            friendlyName = GetDevicePropertyString(hDevInfo, ref devInfo, SPDRP_DEVICEDESC);
                        }

                        string displayName = classifiedName;
                        if (!string.IsNullOrWhiteSpace(friendlyName) && !friendlyName.Equals(classifiedName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (classifiedName != "USB Gaming Controller" && classifiedName != "Xbox Controller")
                            {
                                displayName = $"{classifiedName} ({friendlyName})";
                            }
                            else
                            {
                                displayName = friendlyName;
                            }
                        }

                        string connType = devicePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                                          devicePath.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
                            ? "Bluetooth" : "USB";

                        string instanceKey = $"VID_{vid:X4}&PID_{pid:X4}";
                        if (!results.ContainsKey(instanceKey))
                        {
                            results[instanceKey] = new DiscoveredDeviceInfo(
                                InstanceId: instanceKey,
                                DevicePath: devicePath,
                                Name: displayName,
                                HardwareType: hwType != "Generic" ? hwType : defaultType,
                                ConnectionType: connType,
                                VendorId: vid,
                                ProductId: pid,
                                IsConnected: true
                            );
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        catch
        {
            // Fallback gracefully on query error
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    private static void EnumerateFromRegistry(Dictionary<string, DiscoveredDeviceInfo> results)
    {
        try
        {
            using var usbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbKey == null) return;

            foreach (var vidPidSubKeyName in usbKey.GetSubKeyNames())
            {
                var match = VidPidRegex.Match(vidPidSubKeyName);
                if (!match.Success) continue;

                int vid = Convert.ToInt32(match.Groups[1].Value, 16);
                int pid = Convert.ToInt32(match.Groups[2].Value, 16);

                using var devKey = usbKey.OpenSubKey(vidPidSubKeyName);
                if (devKey == null) continue;

                foreach (var instanceName in devKey.GetSubKeyNames())
                {
                    using var instKey = devKey.OpenSubKey(instanceName);
                    if (instKey == null) continue;

                    string fullInstanceId = $"USB\\{vidPidSubKeyName}\\{instanceName}";
                    var service = instKey.GetValue("Service") as string;
                    var configFlags = instKey.GetValue("ConfigFlags") as int? ?? -1;

                    // Only present/active devices
                    if (configFlags != 0) continue;

                    // Match Xbox / Gamepad services or known gaming controllers
                    bool isGamingDevice = IsKnownGamingVidPid(vid, pid) ||
                                          string.Equals(service, "dc1-controller", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(service, "xusb22", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(service, "xusb21", StringComparison.OrdinalIgnoreCase);

                    if (!isGamingDevice) continue;

                    var (classifiedName, hwType) = ClassifyDevice(vid, pid);

                    var busDesc = instKey.GetValue("BusReportedDeviceDesc") as string;
                    var devDesc = instKey.GetValue("DeviceDesc") as string;
                    if (!string.IsNullOrWhiteSpace(devDesc) && devDesc.Contains(';'))
                    {
                        devDesc = devDesc.Split(';')[^1];
                    }

                    string resolvedName = classifiedName;
                    if (!string.IsNullOrWhiteSpace(busDesc))
                    {
                        resolvedName = busDesc;
                    }
                    else if (!string.IsNullOrWhiteSpace(devDesc) && !devDesc.Equals("Xbox Gaming Device", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedName = devDesc;
                    }
                    else if (classifiedName != "USB Gaming Controller")
                    {
                        resolvedName = !string.IsNullOrWhiteSpace(devDesc) ? $"{classifiedName} ({devDesc})" : classifiedName;
                    }

                    string instanceKey = $"VID_{vid:X4}&PID_{pid:X4}";
                    if (!results.ContainsKey(instanceKey))
                    {
                        results[instanceKey] = new DiscoveredDeviceInfo(
                            InstanceId: $"USB\\{vidPidSubKeyName}\\{instanceName}",
                            DevicePath: $@"\\?\USB#{vidPidSubKeyName}#{instanceName}#{{{GUID_DEVINTERFACE_GIP}}}",
                            Name: resolvedName,
                            HardwareType: hwType,
                            ConnectionType: "USB",
                            VendorId: vid,
                            ProductId: pid,
                            IsConnected: true
                        );
                    }
                }
            }
        }
        catch
        {
            // Graceful fallback
        }
    }

    private static string GetDevicePropertyString(IntPtr hDevInfo, ref SP_DEVINFO_DATA devInfo, uint property)
    {
        byte[] buffer = new byte[512];
        if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfo, property, out _, buffer, (uint)buffer.Length, out uint reqSize))
        {
            int length = (int)Math.Min(reqSize, (uint)buffer.Length);
            var str = Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0');
            if (str.Contains('\0')) str = str.Split('\0', StringSplitOptions.RemoveEmptyEntries)[0];
            if (str.Contains(';')) str = str.Split(';')[^1];
            return str.Trim();
        }
        return string.Empty;
    }

    public static (string Name, string HardwareType) ClassifyDevice(int vid, int pid)
    {
        return (vid, pid) switch
        {
            // GameSir controllers
            (0x3537, 0x1004) => ("GameSir-G7 Pro / T4 Kaleid", "Xbox"),
            (0x3537, _) => ("GameSir Controller", "Xbox"),

            // Microsoft Xbox controllers
            (0x045E, 0x028E) => ("Xbox 360 Controller", "Xbox"),
            (0x045E, 0x028F) => ("Xbox 360 Wireless Controller", "Xbox"),
            (0x045E, 0x02D1) => ("Xbox One Controller", "Xbox"),
            (0x045E, 0x02DD) => ("Xbox One Controller (Audio)", "Xbox"),
            (0x045E, 0x02E3) => ("Xbox Elite Wireless Controller", "Xbox"),
            (0x045E, 0x0B00) => ("Xbox Elite Series 2 Controller", "Xbox"),
            (0x045E, 0x0B12) => ("Xbox Series X|S Wireless Controller", "Xbox"),
            (0x045E, 0x0B13) => ("Xbox Series X|S Controller (BLE)", "Xbox"),
            (0x045E, _) => ("Xbox Controller", "Xbox"),

            // Sony PlayStation controllers
            (0x054C, 0x05C4) => ("Sony DualShock 4 (v1)", "DualShock4"),
            (0x054C, 0x09CC) => ("Sony DualShock 4 (v2)", "DualShock4"),
            (0x054C, 0x0CE6) => ("Sony DualSense Wireless Controller", "DualSense"),
            (0x054C, 0x0DF2) => ("Sony DualSense Edge Controller", "DualSense"),
            (0x054C, _) => ("Sony PlayStation Controller", "DualShock4"),

            // Nintendo Switch controllers
            (0x057E, 0x2006) => ("Joy-Con (L)", "Switch"),
            (0x057E, 0x2007) => ("Joy-Con (R)", "Switch"),
            (0x057E, 0x2009) => ("Nintendo Switch Pro Controller", "Switch"),
            (0x057E, 0x200E) => ("Joy-Con (Grip)", "Switch"),
            (0x057E, _) => ("Nintendo Switch Controller", "Switch"),

            // 8BitDo controllers
            (0x2DC8, _) => ("8BitDo Controller", "Xbox"),

            // Razer gaming controllers
            (0x1532, _) => ("Razer Gamepad", "Xbox"),

            // Logitech controllers
            (0x046D, 0xC216) => ("Logitech Dual Action", "DirectInput"),
            (0x046D, 0xC218) => ("Logitech RumblePad 2", "DirectInput"),
            (0x046D, 0xC21D) => ("Logitech Gamepad F310", "Xbox"),
            (0x046D, 0xC21E) => ("Logitech Gamepad F510", "Xbox"),
            (0x046D, 0xC21F) => ("Logitech Gamepad F710", "Xbox"),
            (0x046D, _) => ("Logitech Gamepad", "DirectInput"),

            _ => ("USB Gaming Controller", "Generic")
        };
    }

    public static bool IsKnownGamingVidPid(int vid, int pid)
    {
        return vid switch
        {
            0x3537 => true, // GameSir
            0x045E => true, // Microsoft
            0x054C => true, // Sony
            0x057E => true, // Nintendo
            0x2DC8 => true, // 8BitDo
            0x1532 => true, // Razer
            0x046D when pid is 0xC216 or 0xC218 or 0xC21D or 0xC21E or 0xC21F => true, // Logitech Gamepads
            0x0738 => true, // Mad Catz
            0x0E6F => true, // PDP
            0x24C6 => true, // PowerA
            0x146B => true, // Bigben / Nacon
            0x044F => true, // Thrustmaster
            _ => false
        };
    }
}
