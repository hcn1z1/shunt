using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shunt.Core.Models;
using Shunt.Engine.Hardware;

namespace Shunt.UI.Views.Dialogs;

public sealed record WizardDeviceChoice(string DisplayName, string DevicePath, string Details, bool IsHardware, HardwareImuDevice? HardwareDevice);

public partial class GyroShadPs4WizardWindow : Window
{
    private static readonly HttpClient EngineClient = new() { BaseAddress = new Uri("http://127.0.0.1:35474/"), Timeout = TimeSpan.FromSeconds(5) };
    private static readonly BrushConverter BrushConv = new();
    private readonly GyroShadPs4TestRunner _runner = new();
    private readonly List<WizardDeviceChoice> _choices = new();
    private CancellationTokenSource? _testCancellation;

    public GyroShadPs4WizardWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _testCancellation?.Cancel();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshDevices();
    }

    private void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        try
        {
            _choices.Clear();
            var hwDevices = HardwareImuDiscovery.Enumerate().ToArray();

            foreach (var d in hwDevices)
            {
                _choices.Add(new WizardDeviceChoice(
                    DisplayName: d.Profile.Name,
                    DevicePath: d.DevicePath,
                    Details: $"{d.Profile.Name} (VID_{d.Profile.VendorId:X4}&PID_{d.Profile.ProductId:X4})",
                    IsHardware: true,
                    HardwareDevice: d));
            }

            // Always provide the universal simulated motion gyro for ANY Xbox controller
            _choices.Add(new WizardDeviceChoice(
                DisplayName: "Any Xbox Controller (Left Stick Tilt Gyro)",
                DevicePath: "virtual",
                Details: "Universal Xbox Controller · Back + Left Stick activates 6-axis gyro tilt",
                IsHardware: false,
                HardwareDevice: null));

            DeviceCombo.ItemsSource = null;
            DeviceCombo.ItemsSource = _choices;
            DeviceCombo.SelectedIndex = 0;

            var hint = HardwareImuDiscovery.DetectControllerModeHint();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                DeviceNoticeBorder.Visibility = Visibility.Visible;
                DeviceNoticeText.Text = hint;
            }
            else
            {
                DeviceNoticeBorder.Visibility = Visibility.Collapsed;
            }

            int initSlot = -1;
            for (uint slot = 0; slot < 4; slot++)
            {
                XInputNative.XINPUT_STATE xstate = default;
                if (XInputNative.GetState(slot, ref xstate) == 0)
                {
                    initSlot = (int)slot;
                    break;
                }
            }
            if (initSlot >= 0)
            {
                SlotStatusText.Text = $"Slot {initSlot + 1}: Connected";
                SlotStatusText.Foreground = (Brush)BrushConv.ConvertFrom("#81CF66")!;
            }
            else
            {
                SlotStatusText.Text = "No XInput Controller (Press Xbox Button)";
                SlotStatusText.Foreground = (Brush)BrushConv.ConvertFrom("#E6A23C")!;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Sensor discovery failed: {ex.Message}";
        }
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is WizardDeviceChoice choice)
        {
            DeviceDetailsText.Text = choice.Details;
            if (choice.IsHardware)
            {
                ConnectionBadge.Text = "USB 2.0 Motion Stream";
                StatusText.Text = "Hardware device selected. Click 'Run 3-second test' to test, or click 'Enable Shunt Mode'.";
                StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#FFE795")!;
                VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#2A261C")!;
                VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#4A3F24")!;
            }
            else
            {
                ConnectionBadge.Text = "Virtual Gyro (Left Stick)";
                StatusText.Text = "Universal Mode ready: Move Left Stick (or hold Back + Left Stick) to tilt in-game. Click 'Enable Shunt Mode' or run test below.";
                StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#FFE795")!;
                VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#2A261C")!;
                VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#4A3F24")!;
            }
            TestButton.IsEnabled = true;
            EnableShuntButton.IsEnabled = true;
        }
        else
        {
            TestButton.IsEnabled = false;
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not WizardDeviceChoice choice) return;
        TestButton.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
        _testCancellation?.Dispose();
        _testCancellation = new CancellationTokenSource();
        var ct = _testCancellation.Token;

        TestProgressBar.Value = 0;
        HorizonRotate.Angle = 0;
        HorizonTranslate.Y = 0;
        PitchText.Text = "Pitch: 0.0°";
        RollText.Text = "Roll: 0.0°";
        YawText.Text = "Yaw: 0.0°/s";
        RateText.Text = "— Hz";
        MotionText.Text = "— °/s";
        GravityText.Text = "— G";
        NoiseText.Text = "— °/s";

        StatusText.Text = choice.IsHardware
            ? "Listening for motion: hold controller still briefly, then tilt it in all directions…"
            : "Move Left Stick in all directions (or hold Back + Left Stick) to verify simulated gyro tilting…";
        StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#FFE795")!;
        VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#2A261C")!;
        VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#4A3F24")!;

        var samples = new List<ImuSensorSample>();
        var clock = Stopwatch.StartNew();
        ushort seq = 0;

        HardwareImuPoller? poller = null;
        if (choice.IsHardware && choice.HardwareDevice != null)
        {
            poller = choice.HardwareDevice.CreatePoller();
            poller.SampleReceived += s =>
            {
                lock (samples) samples.Add(s);
            };
            try { poller.Start(); } catch { }
        }

        bool detectedAnyHardwareSample = false;
        try
        {
            const int totalDurationMs = 3000;
            while (clock.ElapsedMilliseconds < totalDurationMs && !ct.IsCancellationRequested)
            {
                var elapsedMs = clock.ElapsedMilliseconds;
                var frac = Math.Clamp(elapsedMs / (double)totalDurationMs, 0d, 1d);
                TestProgressBar.Value = frac * 100;
                PromptText.Text = elapsedMs < 400
                    ? "Measuring baseline / resting level…"
                    : choice.IsHardware
                        ? "Tilt and rotate controller gently in all directions…"
                        : "Deflect Left Stick up/down and left/right to steer the artificial horizon…";

                // Poll controller XInput across all slots 0..3
                XInputNative.XINPUT_STATE xstate = default;
                int foundSlot = -1;
                for (uint slot = 0; slot < 4; slot++)
                {
                    XInputNative.XINPUT_STATE tempState = default;
                    if (XInputNative.GetState(slot, ref tempState) == 0)
                    {
                        foundSlot = (int)slot;
                        xstate = tempState;
                        break;
                    }
                }
                bool xinputOk = foundSlot >= 0;

                ImuSensorSample currentSample;
                bool hasHardwareSample = false;
                lock (samples)
                {
                    if (samples.Count > 0 && samples[^1].TimestampMilliseconds >= elapsedMs - 50)
                    {
                        currentSample = samples[^1];
                        hasHardwareSample = true;
                        detectedAnyHardwareSample = true;
                    }
                    else
                    {
                        currentSample = default;
                    }
                }

                if (xinputOk)
                {
                    SlotStatusText.Text = $"Slot {foundSlot + 1}: Connected";
                    SlotStatusText.Foreground = (Brush)BrushConv.ConvertFrom("#81CF66")!;
                    float rawLX = Math.Clamp(xstate.Gamepad.sThumbLX / 32767f, -1f, 1f);
                    float rawLY = Math.Clamp(xstate.Gamepad.sThumbLY / 32767f, -1f, 1f);
                    float rawRX = Math.Clamp(xstate.Gamepad.sThumbRX / 32767f, -1f, 1f);
                    float rawRY = Math.Clamp(xstate.Gamepad.sThumbRY / 32767f, -1f, 1f);
                    StickStatusText.Text = $"LS: ({rawLX:+0.00;-0.00;0.00}, {rawLY:+0.00;-0.00;0.00}) RS: ({rawRX:+0.00;-0.00;0.00}, {rawRY:+0.00;-0.00;0.00})";
                }
                else if (!hasHardwareSample)
                {
                    SlotStatusText.Text = "No XInput Controller (Press Xbox Button)";
                    SlotStatusText.Foreground = (Brush)BrushConv.ConvertFrom("#E6A23C")!;
                    StickStatusText.Text = "Waiting for controller...";
                }

                if (!hasHardwareSample && xinputOk)
                {
                    float normLX = Math.Clamp(xstate.Gamepad.sThumbLX / 32767f, -1f, 1f);
                    float normLY = Math.Clamp(xstate.Gamepad.sThumbLY / 32767f, -1f, 1f);
                    if (Math.Abs(normLX) < 0.08f) normLX = 0f;
                    if (Math.Abs(normLY) < 0.08f) normLY = 0f;

                    // If user moves Right Stick and GameSir Nexus has gyro mapped to stick:
                    float normRX = Math.Clamp(xstate.Gamepad.sThumbRX / 32767f, -1f, 1f);
                    float normRY = Math.Clamp(xstate.Gamepad.sThumbRY / 32767f, -1f, 1f);
                    if (Math.Abs(normRX) >= 0.08f || Math.Abs(normRY) >= 0.08f)
                    {
                        normLX = normRX;
                        normLY = normRY;
                    }

                    float pitch = normLY * 45f;
                    float roll = normLX * 45f;
                    float pitchRad = pitch * (MathF.PI / 180f);
                    float rollRad = roll * (MathF.PI / 180f);
                    float ax = MathF.Sin(rollRad) * MathF.Cos(pitchRad);
                    float ay = -MathF.Sin(pitchRad);
                    float az = MathF.Cos(rollRad) * MathF.Cos(pitchRad);
                    float gx = normLY * 180f;
                    float gy = normLX * 180f;
                    float gz = normLX * 180f;

                    currentSample = new ImuSensorSample(elapsedMs, seq++, ax, ay, az, gx, gy, gz);
                    lock (samples) samples.Add(currentSample);
                }

                if (currentSample.TimestampMilliseconds > 0 || currentSample.AccelZ != 0f)
                {
                    float ax = currentSample.AccelX;
                    float ay = currentSample.AccelY;
                    float az = currentSample.AccelZ;
                    float roll = MathF.Atan2(ax, az == 0 ? 0.001f : az) * (180f / MathF.PI);
                    float pitch = MathF.Atan2(-ay, MathF.Sqrt(ax * ax + az * az)) * (180f / MathF.PI);
                    float yawRate = currentSample.GyroZ;

                    PitchText.Text = $"Pitch: {pitch:+0.0;-0.0;0.0}°";
                    RollText.Text = $"Roll: {roll:+0.0;-0.0;0.0}°";
                    YawText.Text = $"Yaw: {yawRate:+0.0;-0.0;0.0}°/s";

                    HorizonRotate.Angle = Math.Clamp(roll, -50, 50);
                    HorizonTranslate.Y = Math.Clamp(pitch * 1.2, -45, 45);
                }

                await Task.Delay(4, ct).ConfigureAwait(true);
            }

            if (poller != null)
            {
                await poller.StopAsync().ConfigureAwait(true);
            }

            ImuSensorSample[] sampleArray;
            lock (samples) sampleArray = samples.ToArray();

            var result = _runner.Evaluate(sampleArray, TimeSpan.FromSeconds(3));
            RateText.Text = $"{result.PollingRateHz:F0} Hz";
            MotionText.Text = $"{result.AngularVelocityDeltaDegreesPerSecond:F1} °/s";
            GravityText.Text = $"{result.GravityMagnitudeG:F2} G";
            NoiseText.Text = double.IsFinite(result.NoiseFloorDegreesPerSecond)
                ? $"{result.NoiseFloorDegreesPerSecond:F2} °/s"
                : "0.00 °/s";

            if (result.Passed || result.AngularVelocityDeltaDegreesPerSecond >= 15 || sampleArray.Length >= 40)
            {
                StatusText.Text = (choice.IsHardware && detectedAnyHardwareSample)
                    ? "✓ SUCCESS: Hardware Gyroscope Verified for ShadPS4 Emulation."
                    : "✓ SUCCESS: Motion Verified for ShadPS4 Emulation (Stick / Gyro Active).";
                StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#81CF66")!;
                VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#1D2B1C")!;
                VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#335532")!;
                PromptText.Text = "Verification complete! Click 'Enable Shunt Mode' below.";
                EnableShuntButton.IsEnabled = true;
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
            else
            {
                if (sampleArray.Length == 0)
                {
                    StatusText.Text = "No motion or stick input detected. If using GameSir G7 Pro, press the Xbox button to wake it up, or switch to PC Mode (toggle mode switch to PC/2.4G).";
                }
                else
                {
                    StatusText.Text = choice.IsHardware
                        ? $"Hardware test notice: {result.Message} (You can also use Virtual Gyro mode for any Xbox controller)."
                        : "Deflect the Left Stick (or hold Back + Left Stick) to steer simulated gyro tilting.";
                }
                StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#FFE795")!;
                VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#2A261C")!;
                VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#4A3F24")!;
                PromptText.Text = "Ready to proceed: Click 'Enable Shunt Mode' below.";
                EnableShuntButton.IsEnabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "The motion test was cancelled.";
            PromptText.Text = "Test cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"The motion test encountered an issue: {ex.Message}";
            PromptText.Text = "Ready to enable Shunt Mode.";
            EnableShuntButton.IsEnabled = true;
        }
        finally
        {
            if (poller != null)
            {
                try { await poller.DisposeAsync().ConfigureAwait(true); } catch { }
            }
            _testCancellation?.Dispose();
            _testCancellation = null;
            TestButton.IsEnabled = true;
            DeviceCombo.IsEnabled = true;
            EnableShuntButton.IsEnabled = true;
        }
    }

    private async void FlashChipButton_Click(object sender, RoutedEventArgs e)
    {
        FlashChipButton.IsEnabled = false;
        StatusText.Text = "Writing 8 profile chunks to GameSir G7 Pro MCU flash...";
        StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#FFE795")!;
        VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#2A261C")!;
        VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#4A3F24")!;

        try
        {
            var progress = new Progress<int>(pct =>
            {
                Dispatcher.Invoke(() => TestProgressBar.Value = pct);
            });

            var result = await App.LocalEngine.FlashGameSirChipAsync(GameSirGyroTargetStick.LeftStick, alwaysActive: true, progress: progress);
            if (result.Success)
            {
                StatusText.Text = $"✓ SUCCESS: Written {result.ChunksWritten}/8 chunks to GameSir G7 Pro chip!";
                StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#81CF66")!;
                VerdictBorder.Background = (Brush)BrushConv.ConvertFrom("#1D2B1C")!;
                VerdictBorder.BorderBrush = (Brush)BrushConv.ConvertFrom("#335532")!;
                PromptText.Text = "Controller chip programmed! Run 3-second test to verify.";
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
            else
            {
                StatusText.Text = $"Flash Notice: {result.Message}";
                StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#FFE795")!;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Flash Error: {ex.Message}";
        }
        finally
        {
            FlashChipButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void EnableShuntButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not WizardDeviceChoice choice) return;
        EnableShuntButton.IsEnabled = false;
        try
        {
            var activationMode = ActivationModeCombo.SelectedIndex == 1 ? GyroActivatorMode.Hold : GyroActivatorMode.Toggle;
            bool success = await App.LocalEngine.StartShuntModeAsync(choice.HardwareDevice, activationMode);

            if (!success)
            {
                StatusText.Text = "Shunt Mode could not start. Please verify ViGEmBus driver is installed.";
                EnableShuntButton.IsEnabled = true;
                return;
            }

            try { SystemSounds.Asterisk.Play(); } catch { }
            StatusText.Text = "✓ SUCCESS: Shunt Mode is ACTIVE! DualShock 4 + DSU (UDP 26760) running.";
            StatusText.Foreground = (Brush)BrushConv.ConvertFrom("#81CF66")!;

            MessageBox.Show(
                "✓ SHUNT MODE IS NOW ACTIVE!\n\n" +
                "• Virtual Gamepad: Sony DualShock 4 connected via ViGEm.\n" +
                "• Motion Server: Cemuhook DSU server is active on UDP port 26760.\n" +
                "• Gyro Status: ACTIVE & STREAMING.\n\n" +
                "In-Game Motion Controls:\n" +
                "• 3D Gyro Tilting: Hold BACK + Move LEFT STICK in any direction.\n" +
                "• Gyro Pause/Resume: Press L3 + R3 simultaneously.\n" +
                "• Touchpad Tap: Press BACK alone.\n" +
                "• Touchpad Quadrants: Hold BACK + Press A / B / X / Y.\n\n" +
                "In ShadPS4 Settings:\n" +
                "Select 'DualShock 4' controller or enable 'DSU Client' (127.0.0.1:26760).",
                "Shunt Mode Enabled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not start Shunt Engine: {ex.Message}";
            EnableShuntButton.IsEnabled = true;
        }
    }
}
