using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shunt.Core.Models;

namespace Shunt.Core.Interfaces;

/// <summary>
/// Represents a physical input controller connected to the host machine.
/// </summary>
public interface IHardwareController : IDisposable
{
    /// <summary>
    /// Unique hardware identifier (e.g. Device Instance ID, PnP Device ID, or MAC).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Friendly display name of the controller (e.g. "GameSir-G7 Pro", "Xbox Wireless Controller", "DualSense").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Classification of the controller ("Xbox", "DualShock4", "DualSense", "Switch", "DirectInput", "Generic").
    /// </summary>
    string HardwareType { get; }

    /// <summary>
    /// Connection transport ("USB", "Bluetooth", "WirelessDongle").
    /// </summary>
    string ConnectionType { get; }

    /// <summary>
    /// Battery level percentage (0-100), or -1 if wired / not supported.
    /// </summary>
    int BatteryLevel { get; }

    /// <summary>
    /// Whether the device is actively charging over wire.
    /// </summary>
    bool IsCharging { get; }

    /// <summary>
    /// Whether the physical controller is currently plugged in and responsive.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The most recent input snapshot polled from the hardware.
    /// </summary>
    GamepadState CurrentState { get; }

    /// <summary>
    /// Triggers motor vibration on the physical controller for haptic identification or in-game rumble.
    /// </summary>
    /// <param name="leftMotor">Low-frequency heavy motor intensity (0 - 65535).</param>
    /// <param name="rightMotor">High-frequency light motor intensity (0 - 65535).</param>
    void Vibrate(ushort leftMotor, ushort rightMotor);

    /// <summary>
    /// Starts the high-frequency polling loop for physical inputs.
    /// </summary>
    void StartPolling();

    /// <summary>
    /// Pauses or stops the polling loop.
    /// </summary>
    void StopPolling();

    /// <summary>
    /// Event raised when new physical inputs are polled from the hardware.
    /// </summary>
    event Action<IHardwareController, GamepadState>? InputReceived;
}

/// <summary>
/// Service responsible for discovering physical gaming controllers and monitoring hotplug/unplug events.
/// </summary>
public interface IHardwareDiscoveryService : IDisposable
{
    /// <summary>
    /// All physical gamepads currently discovered on the machine.
    /// </summary>
    IReadOnlyList<IHardwareController> DiscoveredControllers { get; }

    /// <summary>
    /// The currently active/focused hardware controller targeted for mapping.
    /// </summary>
    IHardwareController? SelectedController { get; set; }

    /// <summary>
    /// Fired when a new physical controller is connected/detected.
    /// </summary>
    event Action<IHardwareController>? ControllerConnected;

    /// <summary>
    /// Fired when a physical controller is disconnected.
    /// </summary>
    event Action<IHardwareController>? ControllerDisconnected;

    /// <summary>
    /// Fired when the active controller selection changes.
    /// </summary>
    event Action<IHardwareController?>? SelectedControllerChanged;

    /// <summary>
    /// Scans the system for connected controllers.
    /// </summary>
    Task RefreshAsync();

    /// <summary>
    /// Starts background monitoring for device connection and disconnection.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops background monitoring.
    /// </summary>
    void Stop();
}
