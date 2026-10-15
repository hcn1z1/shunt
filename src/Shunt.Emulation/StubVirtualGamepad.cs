using System;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;

namespace Shunt.Emulation;

/// <summary>
/// In-memory virtual controller for testing, simulation, and environments without ViGEmBus driver installed.
/// </summary>
public class StubVirtualGamepad : IVirtualGamepad
{
    public VirtualGamepadType Type { get; }
    public bool IsConnected { get; private set; }
    public GamepadState LastReport { get; private set; } = new();

    public event EventHandler<FeedbackEventArgs>? FeedbackReceived;

    public StubVirtualGamepad(VirtualGamepadType type)
    {
        Type = type;
    }

    public void Connect()
    {
        IsConnected = true;
    }

    public void Disconnect()
    {
        IsConnected = false;
    }

    public void UpdateState(GamepadState state)
    {
        if (!IsConnected) return;
        LastReport = state;
    }

    /// <summary>
    /// Last high-capacity virtual state submitted through <see cref="SubmitVirtualState"/> (plan 07 §3).
    /// </summary>
    public VirtualGamepadState LastVirtualReport { get; private set; } = new();

    /// <summary>
    /// Submits a high-capacity virtual state, storing it for diagnostics and tests.
    /// </summary>
    public void SubmitVirtualState(VirtualGamepadState state)
    {
        if (!IsConnected) return;
        LastVirtualReport = state;
        LastReport = VirtualGamepadStateBridge.ToPipeline(state);
    }

    public (byte Large, byte Small) LastVibration { get; private set; }

    public void SetVibration(byte largeMotor, byte smallMotor)
    {
        LastVibration = (largeMotor, smallMotor);
        FeedbackReceived?.Invoke(this, new FeedbackEventArgs(largeMotor, smallMotor));
    }

    public void SimulateFeedback(byte largeMotor, byte smallMotor, byte led = 0)
    {
        FeedbackReceived?.Invoke(this, new FeedbackEventArgs(largeMotor, smallMotor, led));
    }

    public void Dispose()
    {
        Disconnect();
    }
}
