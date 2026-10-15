using System;
using Shunt.Core.Models;

namespace Shunt.Core.Interfaces;

/// <summary>
/// Event arguments for controller vibration / rumble feedback received from games.
/// </summary>
public record FeedbackEventArgs(byte LargeMotor, byte SmallMotor, byte LedNumber = 0);

/// <summary>
/// Represents a virtual output controller (e.g. Xbox 360 or DualShock 4).
/// </summary>
public interface IVirtualGamepad : IDisposable
{
    /// <summary>
    /// Type of the virtual controller.
    /// </summary>
    VirtualGamepadType Type { get; }

    /// <summary>
    /// Whether the virtual controller is currently connected to the OS.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Fired when the game sends rumble/haptic feedback to this controller.
    /// </summary>
    event EventHandler<FeedbackEventArgs>? FeedbackReceived;

    /// <summary>
    /// Connects the virtual device to the system bus.
    /// </summary>
    void Connect();

    /// <summary>
    /// Disconnects the virtual device from the system bus.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Sends an updated controller state report to the OS.
    /// </summary>
    void UpdateState(GamepadState state);

    /// <summary>
    /// Sets output rumble / vibration on the virtual controller.
    /// </summary>
    void SetVibration(byte largeMotor, byte smallMotor);
}

/// <summary>
/// Factory and manager for creating virtual controllers.
/// </summary>
public interface IVirtualGamepadManager : IDisposable
{
    /// <summary>
    /// Checks if the underlying driver (e.g. ViGEmBus) is installed and available.
    /// </summary>
    bool IsDriverAvailable { get; }

    /// <summary>
    /// Creates a virtual gamepad target.
    /// </summary>
    IVirtualGamepad CreateGamepad(VirtualGamepadType type);
}
