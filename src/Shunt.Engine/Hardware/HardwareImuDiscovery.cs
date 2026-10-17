using System;
using System.Collections.Generic;

namespace Shunt.Engine.Hardware;

/// <summary>Discovers known HID sensor collections separately from the controller's XInput interface.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class HardwareImuDiscovery
{
    public static IReadOnlyList<HardwareImuDevice> Enumerate()
    {
        var devices = new List<HardwareImuDevice>();
        foreach (var (path, vendorId, productId) in HidNative.EnumerateKnownImuCollections())
        {
            var profile = ImuDeviceProfile.Find(vendorId, productId);
            if (profile is not null) devices.Add(new HardwareImuDevice(path, profile));
        }
        return devices;
    }

    /// <summary>Checks for connected hardware controllers that lack an active IMU HID endpoint (e.g. running in Xbox mode), and returns a helpful hint.</summary>
    public static string? DetectControllerModeHint()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var usbDevices = HidNative.EnumerateConnectedUsbDevices();
        foreach (var (_, vendorId, productId) in usbDevices)
        {
            if (vendorId == 0x3537 && (productId == 0x1004 || productId == 0x1003))
            {
                return "GameSir G7 Pro is connected in Xbox Mode (VID_3537&PID_1004). Xbox mode does not support gyro streaming. Switch your controller to PC Mode (toggle the physical mode switch to PC / 2.4G) to enable 6-axis gyro.";
            }
        }
        return null;
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed record HardwareImuDevice(string DevicePath, ImuDeviceProfile Profile)
{
    public HardwareImuPoller CreatePoller() => new(DevicePath, Profile);
}
