using System;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;

namespace Shunt.Engine.Hardware;

public sealed class XInputHardwareController : IHardwareController
{
    private readonly uint _userIndex;
    private bool _isDisposed;
    private CancellationTokenSource? _pollingCts;
    private GamepadState _currentState = new();

    public string Id { get; }
    public string Name { get; }
    public string HardwareType => "Xbox";
    public string ConnectionType { get; private set; } = "USB";
    public int BatteryLevel { get; private set; } = -1;
    public bool IsCharging { get; private set; }
    public bool IsConnected { get; private set; } = true;
    public GamepadState CurrentState => _currentState;

    public event Action<IHardwareController, GamepadState>? InputReceived;

    public XInputHardwareController(uint userIndex, string? name = null)
    {
        _userIndex = userIndex;
        Id = $"XInput_Slot_{userIndex}";
        Name = name ?? $"Xbox Controller (Slot {userIndex + 1})";
        UpdateBattery();
    }

    public void Vibrate(ushort leftMotor, ushort rightMotor)
    {
        if (_isDisposed) return;
        var vib = new XInputNative.XINPUT_VIBRATION
        {
            wLeftMotorSpeed = leftMotor,
            wRightMotorSpeed = rightMotor
        };
        XInputNative.SetState(_userIndex, ref vib);
    }

    public void StartPolling()
    {
        if (_pollingCts != null || _isDisposed) return;
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        Task.Run(async () =>
        {
            var rawState = new XInputNative.XINPUT_STATE();
            uint lastPacketNumber = uint.MaxValue;

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                uint res = XInputNative.GetState(_userIndex, ref rawState);
                if (res == XInputNative.ERROR_SUCCESS)
                {
                    IsConnected = true;
                    if (rawState.dwPacketNumber != lastPacketNumber)
                    {
                        lastPacketNumber = rawState.dwPacketNumber;
                        var converted = ConvertState(rawState.Gamepad);
                        _currentState = converted;
                        InputReceived?.Invoke(this, converted);
                    }
                }
                else
                {
                    IsConnected = false;
                }

                // Poll at ~250Hz - 500Hz
                try
                {
                    await Task.Delay(2, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
    }

    public void UpdateBattery()
    {
        var bat = new XInputNative.XINPUT_BATTERY_INFORMATION();
        if (XInputNative.GetBatteryInformation(_userIndex, ref bat) == XInputNative.ERROR_SUCCESS)
        {
            ConnectionType = bat.BatteryType == XInputNative.BATTERY_TYPE_WIRED ? "USB" : "Wireless";
            BatteryLevel = bat.BatteryLevel switch
            {
                XInputNative.BATTERY_LEVEL_EMPTY => 10,
                XInputNative.BATTERY_LEVEL_LOW => 30,
                XInputNative.BATTERY_LEVEL_MEDIUM => 70,
                XInputNative.BATTERY_LEVEL_FULL => 100,
                _ => -1
            };
            IsCharging = bat.BatteryType == XInputNative.BATTERY_TYPE_WIRED;
        }
    }

    private static GamepadState ConvertState(XInputNative.XINPUT_GAMEPAD pad)
    {
        var buttons = GamepadButtons.None;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_A) != 0) buttons |= GamepadButtons.A;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_B) != 0) buttons |= GamepadButtons.B;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_X) != 0) buttons |= GamepadButtons.X;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_Y) != 0) buttons |= GamepadButtons.Y;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_DPAD_UP) != 0) buttons |= GamepadButtons.DpadUp;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_DPAD_DOWN) != 0) buttons |= GamepadButtons.DpadDown;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_DPAD_LEFT) != 0) buttons |= GamepadButtons.DpadLeft;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_DPAD_RIGHT) != 0) buttons |= GamepadButtons.DpadRight;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) buttons |= GamepadButtons.LeftShoulder;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) buttons |= GamepadButtons.RightShoulder;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_LEFT_THUMB) != 0) buttons |= GamepadButtons.LeftThumb;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_RIGHT_THUMB) != 0) buttons |= GamepadButtons.RightThumb;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_START) != 0) buttons |= GamepadButtons.Start;
        if ((pad.wButtons & XInputNative.XINPUT_GAMEPAD_BACK) != 0) buttons |= GamepadButtons.Back;

        float lx = Math.Clamp(pad.sThumbLX / 32767f, -1.0f, 1.0f);
        float ly = Math.Clamp(pad.sThumbLY / 32767f, -1.0f, 1.0f);
        float rx = Math.Clamp(pad.sThumbRX / 32767f, -1.0f, 1.0f);
        float ry = Math.Clamp(pad.sThumbRY / 32767f, -1.0f, 1.0f);

        float lt = Math.Clamp(pad.bLeftTrigger / 255.0f, 0.0f, 1.0f);
        float rt = Math.Clamp(pad.bRightTrigger / 255.0f, 0.0f, 1.0f);

        return new GamepadState
        {
            Buttons = buttons,
            LeftStick = new StickPosition(lx, ly),
            RightStick = new StickPosition(rx, ry),
            LeftTrigger = lt,
            RightTrigger = rt
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopPolling();
        Vibrate(0, 0);
    }
}
