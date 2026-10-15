using System;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using SharpDX.DirectInput;

namespace Shunt.Engine.Hardware;

public sealed class DirectInputHardwareController : IHardwareController
{
    private readonly DirectInput _directInput;
    private readonly DeviceInstance _instance;
    private Joystick? _joystick;
    private bool _isDisposed;
    private CancellationTokenSource? _pollingCts;
    private GamepadState _currentState = new();

    public string Id { get; }
    public string Name { get; }
    public string HardwareType => "DirectInput";
    public string ConnectionType => "USB";
    public int BatteryLevel => -1;
    public bool IsCharging => false;
    public bool IsConnected { get; private set; } = true;
    public GamepadState CurrentState => _currentState;

    public event Action<IHardwareController, GamepadState>? InputReceived;

    public DirectInputHardwareController(DirectInput directInput, DeviceInstance instance)
    {
        _directInput = directInput;
        _instance = instance;
        Id = $"DirectInput_{instance.InstanceGuid}";
        Name = !string.IsNullOrWhiteSpace(instance.ProductName) ? instance.ProductName : instance.InstanceName;
    }

    public void Vibrate(ushort leftMotor, ushort rightMotor)
    {
        // DirectInput force-feedback could be added here if supported by device
    }

    public void StartPolling()
    {
        if (_pollingCts != null || _isDisposed) return;
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        Task.Run(async () =>
        {
            try
            {
                _joystick = new Joystick(_directInput, _instance.InstanceGuid);
                _joystick.Acquire();
            }
            catch
            {
                IsConnected = false;
                return;
            }

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    _joystick.Poll();
                    var state = _joystick.GetCurrentState();
                    var converted = ConvertState(state);
                    _currentState = converted;
                    InputReceived?.Invoke(this, converted);
                    IsConnected = true;
                }
                catch
                {
                    IsConnected = false;
                    try
                    {
                        _joystick.Acquire();
                    }
                    catch
                    {
                        // Device might be disconnected
                    }
                }

                try
                {
                    await Task.Delay(4, token); // ~250Hz polling
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

        try
        {
            _joystick?.Unacquire();
            _joystick?.Dispose();
            _joystick = null;
        }
        catch
        {
            // Ignore on shutdown
        }
    }

    private static GamepadState ConvertState(JoystickState state)
    {
        var buttons = GamepadButtons.None;
        var rawButtons = state.Buttons;
        if (rawButtons != null)
        {
            if (rawButtons.Length > 0 && rawButtons[0]) buttons |= GamepadButtons.A;
            if (rawButtons.Length > 1 && rawButtons[1]) buttons |= GamepadButtons.B;
            if (rawButtons.Length > 2 && rawButtons[2]) buttons |= GamepadButtons.X;
            if (rawButtons.Length > 3 && rawButtons[3]) buttons |= GamepadButtons.Y;
            if (rawButtons.Length > 4 && rawButtons[4]) buttons |= GamepadButtons.LeftShoulder;
            if (rawButtons.Length > 5 && rawButtons[5]) buttons |= GamepadButtons.RightShoulder;
            if (rawButtons.Length > 6 && rawButtons[6]) buttons |= GamepadButtons.Back;
            if (rawButtons.Length > 7 && rawButtons[7]) buttons |= GamepadButtons.Start;
            if (rawButtons.Length > 8 && rawButtons[8]) buttons |= GamepadButtons.LeftThumb;
            if (rawButtons.Length > 9 && rawButtons[9]) buttons |= GamepadButtons.RightThumb;
        }

        // POV hat switch to D-pad
        if (state.PointOfViewControllers is { Length: > 0 } pov && pov[0] >= 0)
        {
            int deg = pov[0] / 100;
            if (deg is >= 315 or <= 45) buttons |= GamepadButtons.DpadUp;
            if (deg is >= 45 and <= 135) buttons |= GamepadButtons.DpadRight;
            if (deg is >= 135 and <= 225) buttons |= GamepadButtons.DpadDown;
            if (deg is >= 225 and <= 315) buttons |= GamepadButtons.DpadLeft;
        }

        float lx = Math.Clamp((state.X - 32767) / 32767f, -1.0f, 1.0f);
        float ly = Math.Clamp(-(state.Y - 32767) / 32767f, -1.0f, 1.0f);
        float rx = Math.Clamp((state.Z - 32767) / 32767f, -1.0f, 1.0f);
        float ry = Math.Clamp(-(state.RotationZ - 32767) / 32767f, -1.0f, 1.0f);

        return new GamepadState
        {
            Buttons = buttons,
            LeftStick = new StickPosition(lx, ly),
            RightStick = new StickPosition(rx, ry),
            LeftTrigger = 0f,
            RightTrigger = 0f
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopPolling();
    }
}
