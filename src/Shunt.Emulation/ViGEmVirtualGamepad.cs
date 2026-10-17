using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using ICoreVirtualGamepad = Shunt.Core.Interfaces.IVirtualGamepad;

namespace Shunt.Emulation;

public class ViGEmXbox360Gamepad : ICoreVirtualGamepad
{
    private readonly IXbox360Controller _controller;
    public VirtualGamepadType Type => VirtualGamepadType.Xbox360;
    public bool IsConnected { get; private set; }

    public event EventHandler<FeedbackEventArgs>? FeedbackReceived;

    public ViGEmXbox360Gamepad(ViGEmClient client)
    {
        _controller = client.CreateXbox360Controller();
        _controller.FeedbackReceived += (sender, args) =>
        {
            FeedbackReceived?.Invoke(this, new FeedbackEventArgs(args.LargeMotor, args.SmallMotor, args.LedNumber));
        };
    }

    public void Connect()
    {
        if (!IsConnected)
        {
            try
            {
                _controller.Connect();
                IsConnected = true;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                throw new InvalidOperationException($"Failed to connect virtual {Type} controller to ViGEmBus. Ensure ViGEmBus driver is running. (Error: {ex.Message})", ex);
            }
        }
    }

    public void Disconnect()
    {
        if (IsConnected)
        {
            _controller.Disconnect();
            IsConnected = false;
        }
    }

    public void UpdateState(GamepadState state)
    {
        if (!IsConnected) return;

        // Map buttons
        _controller.SetButtonState(Xbox360Button.A, state.HasButton(GamepadButtons.A));
        _controller.SetButtonState(Xbox360Button.B, state.HasButton(GamepadButtons.B));
        _controller.SetButtonState(Xbox360Button.X, state.HasButton(GamepadButtons.X));
        _controller.SetButtonState(Xbox360Button.Y, state.HasButton(GamepadButtons.Y));

        _controller.SetButtonState(Xbox360Button.Up, state.HasButton(GamepadButtons.DpadUp));
        _controller.SetButtonState(Xbox360Button.Down, state.HasButton(GamepadButtons.DpadDown));
        _controller.SetButtonState(Xbox360Button.Left, state.HasButton(GamepadButtons.DpadLeft));
        _controller.SetButtonState(Xbox360Button.Right, state.HasButton(GamepadButtons.DpadRight));

        _controller.SetButtonState(Xbox360Button.LeftShoulder, state.HasButton(GamepadButtons.LeftShoulder));
        _controller.SetButtonState(Xbox360Button.RightShoulder, state.HasButton(GamepadButtons.RightShoulder));
        _controller.SetButtonState(Xbox360Button.LeftThumb, state.HasButton(GamepadButtons.LeftThumb));
        _controller.SetButtonState(Xbox360Button.RightThumb, state.HasButton(GamepadButtons.RightThumb));

        _controller.SetButtonState(Xbox360Button.Start, state.HasButton(GamepadButtons.Start));
        _controller.SetButtonState(Xbox360Button.Back, state.HasButton(GamepadButtons.Back));
        _controller.SetButtonState(Xbox360Button.Guide, state.HasButton(GamepadButtons.Guide));

        // Map analog axes (-32768 to 32767)
        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftStick.ToShortX());
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftStick.ToShortY());
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, state.RightStick.ToShortX());
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, state.RightStick.ToShortY());

        // Map triggers (0 to 255)
        byte lt = (byte)Math.Clamp(state.LeftTrigger * 255f, 0f, 255f);
        byte rt = (byte)Math.Clamp(state.RightTrigger * 255f, 0f, 255f);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, lt);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, rt);

        _controller.SubmitReport();
    }

    public void SetVibration(byte largeMotor, byte smallMotor)
    {
        FeedbackReceived?.Invoke(this, new FeedbackEventArgs(largeMotor, smallMotor));
    }

    public void Dispose()
    {
        Disconnect();
    }
}

public class ViGEmDualShock4Gamepad : ICoreVirtualGamepad
{
    private readonly IDualShock4Controller _controller;
    private readonly object _stateLock = new();
    private readonly Queue<(TouchpadGesture Gesture, bool Click)> _pendingGestures = new();
    private readonly DualShock4TouchpadSynthesizer _touchpadSynthesizer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private GamepadState _latestState = new();
    private GamepadButtons _previousGestureButtons;
    private bool _gesturePumpRunning;
    private bool _extendedTouchpadDisabled;
    private ushort _reportTimestamp;
    private byte _touchPacketCounter;

    /// <summary>Last error from submitting an extended touchpad report, if any.</summary>
    public string? TouchpadOutputError { get; private set; }

    /// <summary>Whether ViGEm rejected an extended touchpad report and touchpad output was disabled.</summary>
    public bool ExtendedTouchpadDisabled => _extendedTouchpadDisabled;

    public VirtualGamepadType Type => VirtualGamepadType.DualShock4;
    public bool IsConnected { get; private set; }

    public event EventHandler<FeedbackEventArgs>? FeedbackReceived;

    public ViGEmDualShock4Gamepad(ViGEmClient client)
    {
        _controller = client.CreateDualShock4Controller();
#pragma warning disable CS0618 // Type or member is obsolete in ViGEmClient SDK
        _controller.FeedbackReceived += (sender, args) =>
        {
            FeedbackReceived?.Invoke(this, new FeedbackEventArgs(args.LargeMotor, args.SmallMotor));
        };
#pragma warning restore CS0618
    }

    public void Connect()
    {
        if (!IsConnected)
        {
            try
            {
                _controller.Connect();
                IsConnected = true;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                throw new InvalidOperationException($"Failed to connect virtual {Type} controller to ViGEmBus. Ensure ViGEmBus driver is running. (Error: {ex.Message})", ex);
            }
        }
    }

    public void Disconnect()
    {
        _lifetime.Cancel();
        bool wasConnected;
        lock (_stateLock)
        {
            wasConnected = IsConnected;
            IsConnected = false;
            _pendingGestures.Clear();
            _gesturePumpRunning = false;
            _previousGestureButtons = GamepadButtons.None;
        }

        if (wasConnected)
        {
            _controller.Disconnect();
        }
    }

    public void UpdateState(GamepadState state)
    {
        if (!IsConnected) return;

        bool startGesturePump = false;
        lock (_stateLock)
        {
            _latestState = state;
            EnqueueRisingGestures(state.Buttons);

            if (!_gesturePumpRunning && _pendingGestures.Count > 0)
            {
                _gesturePumpRunning = true;
                startGesturePump = true;
            }
            else if (!_gesturePumpRunning)
            {
                if (_extendedTouchpadDisabled)
                {
                    SubmitStandardReport(state);
                }
                else
                {
                    try
                    {
                        SubmitExtendedReport(state, default, default);
                    }
                    catch (Exception ex)
                    {
                        TouchpadOutputError = ex.Message;
                        _extendedTouchpadDisabled = true;
                        SubmitStandardReport(state);
                    }
                }
            }
        }

        if (startGesturePump)
            _ = Task.Run(() => PlayPendingGesturesAsync(_lifetime.Token));
    }

    private void EnqueueRisingGestures(GamepadButtons buttons)
    {
        const GamepadButtons gestures = GamepadButtons.SwipeUp | GamepadButtons.SwipeDown |
                                        GamepadButtons.SwipeLeft | GamepadButtons.SwipeRight |
                                        GamepadButtons.TouchpadTap | GamepadButtons.TouchpadTapLeft |
                                        GamepadButtons.TouchpadTapRight | GamepadButtons.TouchpadTapTop |
                                        GamepadButtons.TouchpadTapBottom;
        var pressed = buttons & gestures;
        var rising = pressed & ~_previousGestureButtons;
        _previousGestureButtons = pressed;
        if (_extendedTouchpadDisabled) return;

        if ((rising & GamepadButtons.SwipeUp) != 0) _pendingGestures.Enqueue((TouchpadGesture.SwipeUp, false));
        if ((rising & GamepadButtons.SwipeDown) != 0) _pendingGestures.Enqueue((TouchpadGesture.SwipeDown, false));
        if ((rising & GamepadButtons.SwipeLeft) != 0) _pendingGestures.Enqueue((TouchpadGesture.SwipeLeft, false));
        if ((rising & GamepadButtons.SwipeRight) != 0) _pendingGestures.Enqueue((TouchpadGesture.SwipeRight, false));
        if ((rising & GamepadButtons.TouchpadTap) != 0) _pendingGestures.Enqueue((TouchpadGesture.TapCenter, true));
        if ((rising & GamepadButtons.TouchpadTapLeft) != 0) _pendingGestures.Enqueue((TouchpadGesture.TapLeft, true));
        if ((rising & GamepadButtons.TouchpadTapRight) != 0) _pendingGestures.Enqueue((TouchpadGesture.TapRight, true));
        if ((rising & GamepadButtons.TouchpadTapTop) != 0) _pendingGestures.Enqueue((TouchpadGesture.TapTop, true));
        if ((rising & GamepadButtons.TouchpadTapBottom) != 0) _pendingGestures.Enqueue((TouchpadGesture.TapBottom, true));
    }

    private async Task PlayPendingGesturesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TouchpadGesture gesture;
                bool click;
                lock (_stateLock)
                {
                    if (!IsConnected || _pendingGestures.Count == 0)
                    {
                        _gesturePumpRunning = false;
                        if (IsConnected)
                            SubmitStandardReport(_latestState);
                        return;
                    }

                    (gesture, click) = _pendingGestures.Dequeue();
                }

                var frames = _touchpadSynthesizer.Synthesize(gesture);
                bool hasHoldTarget = TryGetSwipeButton(gesture, out var swipeButton);
                var activeFrames = hasHoldTarget
                    ? frames.Where(f => f.Touch0.IsActive || f.Touch1.IsActive).ToList()
                    : frames;
                var playbackFrames = activeFrames.Count > 0 ? activeFrames : frames;

                foreach (var frame in playbackFrames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_stateLock)
                    {
                        if (!IsConnected) return;
                        SubmitExtendedReport(click ? WithTouchpadClick(_latestState) : _latestState, frame.Touch0, frame.Touch1);
                    }
                    await Task.Delay(frame.DurationMs, cancellationToken).ConfigureAwait(false);
                }

                // A mapped swipe is an active control: keep the virtual finger at
                // the swipe endpoint until the mapping releases it. This supports
                // games that require a swipe-and-hold before another button press.
                if (hasHoldTarget && playbackFrames.Count > 0)
                {
                    var endpoint = playbackFrames[^1].Touch0;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        bool held;
                        lock (_stateLock)
                        {
                            if (!IsConnected) return;
                            held = (_latestState.Buttons & swipeButton) != 0;
                            if (!held)
                            {
                                SubmitExtendedReport(_latestState, default, default);
                                break;
                            }
                            SubmitExtendedReport(_latestState, endpoint, default);
                        }

                        await Task.Delay(_touchpadSynthesizer.ReportIntervalMs, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The controller is disconnecting.
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                TouchpadOutputError = ex.Message;
                _extendedTouchpadDisabled = true;
                _pendingGestures.Clear();
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _gesturePumpRunning = false;
                if (IsConnected && !cancellationToken.IsCancellationRequested)
                    SubmitStandardReport(_latestState);
            }
        }
    }

    private static bool TryGetSwipeButton(TouchpadGesture gesture, out GamepadButtons button)
    {
        button = gesture switch
        {
            TouchpadGesture.SwipeUp => GamepadButtons.SwipeUp,
            TouchpadGesture.SwipeDown => GamepadButtons.SwipeDown,
            TouchpadGesture.SwipeLeft => GamepadButtons.SwipeLeft,
            TouchpadGesture.SwipeRight => GamepadButtons.SwipeRight,
            _ => GamepadButtons.None
        };
        return button != GamepadButtons.None;
    }

    private static GamepadState WithTouchpadClick(GamepadState state) =>
        state with { Buttons = state.Buttons | GamepadButtons.TouchpadClick };

    private void SubmitStandardReport(GamepadState state)
    {
        _controller.SetButtonState(DualShock4Button.Cross, state.HasButton(GamepadButtons.A));
        _controller.SetButtonState(DualShock4Button.Circle, state.HasButton(GamepadButtons.B));
        _controller.SetButtonState(DualShock4Button.Square, state.HasButton(GamepadButtons.X));
        _controller.SetButtonState(DualShock4Button.Triangle, state.HasButton(GamepadButtons.Y));
        _controller.SetButtonState(DualShock4Button.ShoulderLeft, state.HasButton(GamepadButtons.LeftShoulder));
        _controller.SetButtonState(DualShock4Button.ShoulderRight, state.HasButton(GamepadButtons.RightShoulder));
        _controller.SetButtonState(DualShock4Button.ThumbLeft, state.HasButton(GamepadButtons.LeftThumb));
        _controller.SetButtonState(DualShock4Button.ThumbRight, state.HasButton(GamepadButtons.RightThumb));
        _controller.SetButtonState(DualShock4Button.Share, state.HasButton(GamepadButtons.Back));
        _controller.SetButtonState(DualShock4Button.Options, state.HasButton(GamepadButtons.Start));

        _controller.SetDPadDirection(ToDPadDirection(state.Buttons));
        _controller.SetAxisValue(DualShock4Axis.LeftThumbX, state.LeftStick.ToByteX());
        _controller.SetAxisValue(DualShock4Axis.LeftThumbY, state.LeftStick.ToByteY());
        _controller.SetAxisValue(DualShock4Axis.RightThumbX, state.RightStick.ToByteX());
        _controller.SetAxisValue(DualShock4Axis.RightThumbY, state.RightStick.ToByteY());
        _controller.SetSliderValue(DualShock4Slider.LeftTrigger, ToTriggerByte(state.LeftTrigger));
        _controller.SetSliderValue(DualShock4Slider.RightTrigger, ToTriggerByte(state.RightTrigger));
        _controller.SubmitReport();
    }

    private void SubmitExtendedReport(GamepadState state, TouchPoint touch0, TouchPoint touch1)
    {
        // ViGEm's DS4_REPORT_EX is the 63-byte report payload without HID report ID 0x01.
        var report = new byte[63];
        report[0] = state.LeftStick.ToByteX();
        report[1] = state.LeftStick.ToByteY();
        report[2] = state.RightStick.ToByteX();
        report[3] = state.RightStick.ToByteY();

        ushort buttons = ToDPadDirection(state.Buttons).Value;
        if (state.HasButton(GamepadButtons.A)) buttons |= 1 << 5;  // Cross
        if (state.HasButton(GamepadButtons.B)) buttons |= 1 << 6;  // Circle
        if (state.HasButton(GamepadButtons.X)) buttons |= 1 << 4;  // Square
        if (state.HasButton(GamepadButtons.Y)) buttons |= 1 << 7;  // Triangle
        if (state.HasButton(GamepadButtons.LeftShoulder)) buttons |= 1 << 8;
        if (state.HasButton(GamepadButtons.RightShoulder)) buttons |= 1 << 9;
        if (state.HasButton(GamepadButtons.LeftThumb)) buttons |= 1 << 14;
        if (state.HasButton(GamepadButtons.RightThumb)) buttons |= 1 << 15;
        if (state.HasButton(GamepadButtons.Back)) buttons |= 1 << 12; // Share
        if (state.HasButton(GamepadButtons.Start)) buttons |= 1 << 13; // Options
        if (state.LeftTrigger > 0f) buttons |= 1 << 10;
        if (state.RightTrigger > 0f) buttons |= 1 << 11;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(4, 2), buttons);

        byte special = 0;
        if (state.HasButton(GamepadButtons.Guide)) special |= 1 << 0; // PS
        if (state.HasButton(GamepadButtons.Touchpad) || state.HasButton(GamepadButtons.TouchpadClick)) special |= 1 << 1;
        report[6] = special;
        report[7] = ToTriggerByte(state.LeftTrigger);
        report[8] = ToTriggerByte(state.RightTrigger);
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(9, 2), _reportTimestamp++);
        report[11] = 0xFF; // Full battery
        DualShock4ReportFormatter.WriteMotion(report, new MotionState(
            state.GyroPitch, state.GyroRoll, state.GyroYaw,
            state.AccelX, state.AccelY, state.AccelZ,
            state.GyroVelX, state.GyroVelY, state.GyroVelZ));
        report[29] = 0x1A; // Wired, full battery
        report[32] = 1; // One touch sample in this report

        WriteTouch(report, 33, touch0, touch1, _touchPacketCounter++);
        _controller.SubmitRawReport(report);
    }

    private static void WriteTouch(byte[] report, int offset, TouchPoint touch0, TouchPoint touch1, byte packetCounter)
    {
        report[offset] = packetCounter;
        WriteFinger(report, offset + 1, touch0);
        WriteFinger(report, offset + 5, touch1);
    }

    private static void WriteFinger(byte[] report, int offset, TouchPoint touch)
    {
        if (!touch.IsActive)
        {
            report[offset] = 0x80; // Active-low DS4 tracking byte: 0x80 means lifted.
            return;
        }

        report[offset] = (byte)(touch.Id & 0x7F);
        ushort x = Math.Min(touch.X, DualShock4TouchpadSynthesizer.MaxX);
        ushort y = Math.Min(touch.Y, DualShock4TouchpadSynthesizer.MaxY);
        report[offset + 1] = (byte)x;
        report[offset + 2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        report[offset + 3] = (byte)(y >> 4);
    }

    private static byte ToTriggerByte(float value) => (byte)Math.Clamp(value * 255f, 0f, 255f);

    private static DualShock4DPadDirection ToDPadDirection(GamepadButtons buttons)
    {
        bool up = (buttons & GamepadButtons.DpadUp) != 0;
        bool down = (buttons & GamepadButtons.DpadDown) != 0;
        bool left = (buttons & GamepadButtons.DpadLeft) != 0;
        bool right = (buttons & GamepadButtons.DpadRight) != 0;

        if (up && right) return DualShock4DPadDirection.Northeast;
        if (up && left) return DualShock4DPadDirection.Northwest;
        if (down && right) return DualShock4DPadDirection.Southeast;
        if (down && left) return DualShock4DPadDirection.Southwest;
        if (up) return DualShock4DPadDirection.North;
        if (down) return DualShock4DPadDirection.South;
        if (left) return DualShock4DPadDirection.West;
        if (right) return DualShock4DPadDirection.East;
        return DualShock4DPadDirection.None;
    }

    public void SetVibration(byte largeMotor, byte smallMotor)
    {
        FeedbackReceived?.Invoke(this, new FeedbackEventArgs(largeMotor, smallMotor));
    }

    public void Dispose()
    {
        Disconnect();
        _lifetime.Dispose();
    }
}
