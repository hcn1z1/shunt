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
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed record HardwareImuDevice(string DevicePath, ImuDeviceProfile Profile)
{
    public HardwareImuPoller CreatePoller() => new(DevicePath, Profile);
}
