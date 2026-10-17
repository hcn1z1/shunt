using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shunt.Core.Models;
using Shunt.Engine.Hardware;
using Shunt.UI.Views.Dialogs;

namespace Shunt.UI.Views.Tuners;

public partial class ShuntModeView : UserControl
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:35474/"),
        Timeout = TimeSpan.FromMilliseconds(1500)
    };

    private readonly DispatcherTimer _pollTimer;
    private bool _isDraggingGimbal;
    private Point _lastDragPoint;
    private float _simulatedPitch;
    private float _simulatedRoll;
    private float _simulatedYaw;
    private bool _isShuntEnabled;
    private bool _isDsuEnabled;

    public sealed record DeviceItem(string DisplayName, string DevicePath, string Details);

    public ShuntModeView()
    {
        InitializeComponent();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _pollTimer.Tick += PollTimer_Tick;

        App.LocalEngine.ChipSyncStatusChanged += result =>
        {
            Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    FlashStatusText.Text = $"✓ CHIP SYNC: {result.Message}";
                    FlashStatusText.Foreground = (Brush)FindResource("StatusSuccessBrush");
                }
                else
                {
                    FlashStatusText.Text = $"⚠️ CHIP NOTICE: {result.Message}";
                    FlashStatusText.Foreground = (Brush)FindResource("AccentCreamBrush");
                }
            });
        };

        Loaded += (_, _) =>
        {
            RefreshDevices();
            _pollTimer.Start();
            _ = PollStateAsync();
        };

        Unloaded += (_, _) => _pollTimer.Stop();
    }

    public void RefreshDevices()
    {
        var devices = HardwareImuDiscovery.Enumerate();
        var items = new List<DeviceItem>();

        foreach (var dev in devices)
        {
            items.Add(new DeviceItem(
                dev.Profile.Name,
                dev.DevicePath,
                $"VID 0x{dev.Profile.VendorId:X4} · PID 0x{dev.Profile.ProductId:X4} · {dev.Profile.ReportFormat}"));
        }

        // Always include universal virtual gyro for any Xbox controller
        items.Add(new DeviceItem(
            "Any Xbox Controller (Left Stick Tilt Gyro)",
            "virtual",
            "Universal Xbox Controller · Back + Left Stick activates 6-axis gyro tilt"));

        ImuDeviceCombo.ItemsSource = items;
        ImuDeviceCombo.SelectedIndex = 0;

        var hint = HardwareImuDiscovery.DetectControllerModeHint();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            TunerNoticeBorder.Visibility = Visibility.Visible;
            TunerNoticeText.Text = hint;
        }
        else
        {
            TunerNoticeBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e) => RefreshDevices();

    private void OnImuDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImuDeviceCombo.SelectedItem is DeviceItem item)
        {
            ImuDeviceNameText.Text = item.DisplayName;
            ImuDeviceDetailsText.Text = item.Details;
        }
    }

    private async void PollTimer_Tick(object? sender, EventArgs e) => await PollStateAsync();

    private async System.Threading.Tasks.Task PollStateAsync()
    {
        await System.Threading.Tasks.Task.Yield();
        try
        {
            var engine = App.LocalEngine;
            _isShuntEnabled = engine.IsShuntModeEnabled;
            var gyroEnabled = engine.IsGyroEnabled;
            _isDsuEnabled = engine.IsDsuRunning;

            UpdateMasterSwitchVisuals(_isShuntEnabled, gyroEnabled);
            UpdateDsuVisuals(_isDsuEnabled);

            var hint = HardwareImuDiscovery.DetectControllerModeHint();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                TunerNoticeBorder.Visibility = Visibility.Visible;
                TunerNoticeText.Text = hint;
            }
            else
            {
                TunerNoticeBorder.Visibility = Visibility.Collapsed;
            }

            // Touchpad action detection
            var action = engine.LastTouchpadAction;
            var actionAt = engine.LastTouchpadActionAt;
            var recent = action != null && actionAt.HasValue && DateTimeOffset.UtcNow - actionAt.Value < TimeSpan.FromMilliseconds(650);
            UpdateTouchpadQuads(recent ? action : null);

            // If gyro is active and we're not manually dragging, show real live motion feedback
            if (gyroEnabled && !_isDraggingGimbal)
            {
                float pitch = engine.CurrentPitch;
                float roll = engine.CurrentRoll;
                float yaw = engine.CurrentYaw;
                float angVel = engine.CurrentAngularVelocity;
                float grav = engine.CurrentGravityG;
                int activeSlot = engine.ActiveXInputSlot;

                UpdateGimbalVisuals(pitch, roll, yaw);
                PollRateText.Text = engine.HasHardwareImuActive ? "1000 Hz (Hardware IMU)" : "250 Hz (Stick Virtual Gyro)";
                AngularVelText.Text = $"{angVel:F2} °/s";
                GravityVectorText.Text = $"{grav:F3} G";
                SensorHealthText.Text = activeSlot >= 0 ? $"SLOT {activeSlot + 1} ACTIVE" : (engine.HasHardwareImuActive ? "HARDWARE IMU ACTIVE" : "OPTIMAL");
            }
            else if (!_isShuntEnabled && !_isDraggingGimbal)
            {
                PollRateText.Text = "Standby";
                AngularVelText.Text = "0.00 °/s";
                GravityVectorText.Text = "1.000 G";
                SensorHealthText.Text = "STANDBY";
            }
        }
        catch
        {
            UpdateMasterSwitchVisuals(false, false);
        }
    }

    private void UpdateMasterSwitchVisuals(bool enabled, bool gyroStreaming)
    {
        if (enabled)
        {
            MasterStatusDot.Fill = (Brush)FindResource("StatusSuccessBrush");
            MasterStatusText.Text = "SHUNT MODE: ACTIVE";
            MasterStatusText.Foreground = (Brush)FindResource("StatusSuccessBrush");
            MasterShuntButton.Content = "Disable Shunt Mode";
            MasterShuntButton.Style = (Style)FindResource("SecondaryButton");
        }
        else
        {
            MasterStatusDot.Fill = (Brush)FindResource("BorderNormalBrush");
            MasterStatusText.Text = "SHUNT MODE: DISABLED";
            MasterStatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            MasterShuntButton.Content = "Enable Shunt Mode";
            MasterShuntButton.Style = (Style)FindResource("PrimaryButton");
        }

        if (gyroStreaming)
        {
            GyroStreamBadge.Text = "GYRO: STREAMING";
            GyroStreamBadge.Foreground = (Brush)FindResource("StatusSuccessBrush");
        }
        else
        {
            GyroStreamBadge.Text = enabled ? "GYRO: PAUSED" : "GYRO: IDLE";
            GyroStreamBadge.Foreground = (Brush)FindResource("TextMutedBrush");
        }
    }

    private void UpdateDsuVisuals(bool enabled)
    {
        DsuServerToggleBtn.Content = enabled ? "DSU: Active" : "Enable DSU";
        DsuServerToggleBtn.Foreground = enabled ? (Brush)FindResource("StatusSuccessBrush") : (Brush)FindResource("TextPrimaryBrush");
    }

    private void UpdateTouchpadQuads(string? action)
    {
        var activeBrush = (Brush)FindResource("AccentCreamBrush");
        var idleBrush = (Brush)FindResource("SurfaceLevel4Brush");

        TouchQuadX.Background = action != null && (action.Contains("Left", StringComparison.OrdinalIgnoreCase) || action.Contains("Top", StringComparison.OrdinalIgnoreCase)) ? activeBrush : idleBrush;
        TouchQuadY.Background = action != null && (action.Contains("Right", StringComparison.OrdinalIgnoreCase) || action.Contains("Top", StringComparison.OrdinalIgnoreCase)) ? activeBrush : idleBrush;
        TouchQuadA.Background = action != null && (action.Contains("Left", StringComparison.OrdinalIgnoreCase) || action.Contains("Bottom", StringComparison.OrdinalIgnoreCase)) ? activeBrush : idleBrush;
        TouchQuadB.Background = action != null && (action.Contains("Right", StringComparison.OrdinalIgnoreCase) || action.Contains("Bottom", StringComparison.OrdinalIgnoreCase)) ? activeBrush : idleBrush;
    }

    private async void MasterShuntButton_Click(object sender, RoutedEventArgs e)
    {
        MasterShuntButton.IsEnabled = false;
        try
        {
            if (!_isShuntEnabled)
            {
                var choice = ImuDeviceCombo.SelectedItem as DeviceItem;
                var mode = ActivationModeCombo.SelectedIndex == 1 ? GyroActivatorMode.Hold : GyroActivatorMode.Toggle;

                HardwareImuDevice? targetDevice = null;
                if (choice != null && choice.DevicePath != "virtual")
                {
                    targetDevice = HardwareImuDiscovery.Enumerate()
                        .FirstOrDefault(d => string.Equals(d.DevicePath, choice.DevicePath, StringComparison.OrdinalIgnoreCase));
                }

                bool success = await App.LocalEngine.StartShuntModeAsync(targetDevice, mode);
                if (success)
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
                else
                {
                    MessageBox.Show(
                        "Could not start Shunt Mode. Please verify ViGEmBus driver is installed and running.",
                        "Shunt Mode Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                await App.LocalEngine.StopShuntModeAsync();
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }

            await PollStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to toggle Shunt Mode: {ex.Message}", "Shunt Mode Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            MasterShuntButton.IsEnabled = true;
        }
    }

    private async void OnDsuServerToggleClick(object sender, RoutedEventArgs e)
    {
        DsuServerToggleBtn.IsEnabled = false;
        try
        {
            if (!_isDsuEnabled)
                App.LocalEngine.StartDsuServer();
            else
                await App.LocalEngine.StopDsuServerAsync();

            await PollStateAsync();
        }
        catch { }
        finally
        {
            DsuServerToggleBtn.IsEnabled = true;
        }
    }

    private async void FlashChipButton_Click(object sender, RoutedEventArgs e)
    {
        FlashChipButton.IsEnabled = false;
        FlashProgressBar.Visibility = Visibility.Visible;
        FlashProgressBar.Value = 0;
        FlashStatusText.Text = "Writing 8 profile chunks to GameSir G7 Pro MCU flash...";
        FlashStatusText.Foreground = (Brush)FindResource("AccentCreamBrush");

        try
        {
            var targetStick = ChipTargetStickCombo.SelectedIndex == 1
                ? GameSirGyroTargetStick.RightStick
                : GameSirGyroTargetStick.LeftStick;

            bool isAlwaysActive = ChipActivationCombo.SelectedIndex == 0;

            var progress = new Progress<int>(percent =>
            {
                Dispatcher.Invoke(() => FlashProgressBar.Value = percent);
            });

            var result = await App.LocalEngine.FlashGameSirChipAsync(targetStick, alwaysActive: isAlwaysActive, progress);
            if (result.Success)
            {
                FlashStatusText.Text = $"✓ SUCCESS: Written {result.ChunksWritten}/8 chunks! {result.Message}";
                FlashStatusText.Foreground = (Brush)FindResource("StatusSuccessBrush");
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
            else
            {
                FlashStatusText.Text = $"⚠️ Notice: {result.Message}";
                FlashStatusText.Foreground = (Brush)FindResource("AccentCreamBrush");
            }
        }
        catch (Exception ex)
        {
            FlashStatusText.Text = $"Error: {ex.Message}";
            FlashStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
        }
        finally
        {
            FlashProgressBar.Visibility = Visibility.Collapsed;
            FlashChipButton.IsEnabled = true;
        }
    }

    private void AutoSyncChipCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        App.LocalEngine.AutoSyncChipOnGyroToggle = AutoSyncChipCheckBox.IsChecked == true;
    }

    private void OnLaunchWizardClick(object sender, RoutedEventArgs e)
    {
        var dlg = new GyroShadPs4WizardWindow { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        _ = PollStateAsync();
    }

    private void OnActivationModeChanged(object sender, SelectionChangedEventArgs e)
    {
        // Dynamic mode selection will be applied on the next enable cycle
    }

    private void OnZeroCalibrationClick(object sender, RoutedEventArgs e) => OnResetOrientationClick(sender, e);

    #region Interactive Mouse Simulation for 3D Gimbal

    private void OnGimbalMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingGimbal = true;
            _lastDragPoint = e.GetPosition(GimbalStageGrid);
            GimbalStageGrid.CaptureMouse();
        }
    }

    private void OnGimbalMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingGimbal) return;

        var currentPoint = e.GetPosition(GimbalStageGrid);
        var delta = currentPoint - _lastDragPoint;
        _lastDragPoint = currentPoint;

        _simulatedRoll = Math.Clamp(_simulatedRoll + (float)delta.X * 0.75f, -90f, 90f);
        _simulatedPitch = Math.Clamp(_simulatedPitch - (float)delta.Y * 0.75f, -60f, 60f);
        _simulatedYaw = (_simulatedYaw + (float)delta.X * 0.5f) % 360f;

        UpdateGimbalVisuals(_simulatedPitch, _simulatedRoll, _simulatedYaw);
    }

    private void OnGimbalMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingGimbal)
        {
            _isDraggingGimbal = false;
            GimbalStageGrid.ReleaseMouseCapture();
        }
    }

    private void OnResetOrientationClick(object sender, RoutedEventArgs e)
    {
        _simulatedPitch = 0f;
        _simulatedRoll = 0f;
        _simulatedYaw = 0f;
        UpdateGimbalVisuals(0f, 0f, 0f);
    }

    private void UpdateGimbalVisuals(float pitch, float roll, float yaw)
    {
        PitchReadoutText.Text = $"{(pitch >= 0 ? "+" : "")}{pitch:F1}°";
        RollReadoutText.Text = $"{(roll >= 0 ? "+" : "")}{roll:F1}°";
        YawReadoutText.Text = $"{yaw:F1}°";

        RollRotateTransform.Angle = roll;
        PitchTranslateTransform.Y = Math.Clamp(pitch * 1.33f, -110f, 110f);

        // Directional Tilt Badges
        const float threshold = 15f;
        UpdateTiltBadge(BadgeTiltUp, TextTiltUp, pitch > threshold);
        UpdateTiltBadge(BadgeTiltDown, TextTiltDown, pitch < -threshold);
        UpdateTiltBadge(BadgeTiltLeft, TextTiltLeft, roll < -threshold);
        UpdateTiltBadge(BadgeTiltRight, TextTiltRight, roll > threshold);
    }

    private void UpdateTiltBadge(Border badge, TextBlock text, bool active)
    {
        if (active)
        {
            badge.Background = (Brush)FindResource("AccentCreamBrush");
            badge.BorderBrush = (Brush)FindResource("AccentCreamBrush");
            text.Foreground = (Brush)FindResource("CanvasBackgroundBrush");
        }
        else
        {
            badge.Background = (Brush)FindResource("SurfaceLevel3Brush");
            badge.BorderBrush = (Brush)FindResource("BorderSubtleBrush");
            text.Foreground = (Brush)FindResource("TextMutedBrush");
        }
    }

    #endregion
}
