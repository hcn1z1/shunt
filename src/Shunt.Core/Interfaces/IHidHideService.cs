using System.Collections.Generic;

namespace Shunt.Core.Interfaces;

/// <summary>
/// Abstraction for the Nefarius HidHide kernel filter driver integration,
/// isolating physical gamepads from Windows games to prevent double-input.
/// </summary>
public interface IHidHideService
{
    /// <summary>Whether the HidHide kernel driver is installed and accessible.</summary>
    bool IsAvailable { get; }

    /// <summary>The installed version of the HidHide driver, or null if not installed.</summary>
    string? DriverVersion { get; }

    /// <summary>Whether HidHide is currently filtering the configured device list.</summary>
    bool IsActive { get; set; }

    /// <summary>Whether inverse whitelisting (blocking all unapproved apps) is active.</summary>
    bool IsAppListInverted { get; set; }

    /// <summary>Adds an application executable path to the HidHide whitelist.</summary>
    bool AddApplicationWhitelist(string applicationPath);

    /// <summary>Removes an application executable path from the HidHide whitelist.</summary>
    bool RemoveApplicationWhitelist(string applicationPath);

    /// <summary>Returns the current list of whitelisted application executable paths.</summary>
    IReadOnlyList<string> GetWhitelistedApplications();

    /// <summary>Blocks a physical device instance path from unapproved applications.</summary>
    bool BlockDevice(string deviceInstancePath);

    /// <summary>Unblocks a physical device instance path.</summary>
    bool UnblockDevice(string deviceInstancePath);

    /// <summary>Returns the list of currently blocked device instance paths.</summary>
    IReadOnlyList<string> GetBlockedDevices();

    /// <summary>Clears all filter settings and unblocks all devices.</summary>
    bool ClearAll();
}
