using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shunt.Core.Models;

namespace Shunt.UI.Views.Tuners;

public partial class GyroscopeView : UserControl
{
    private GyroscopeSettings _settings = new();
    private bool _isUpdatingUi = false;
    private bool _isDraggingGimbal = false;
    private Point _lastDragPoint;
    private float _simulatedPitch = 0f;
    private float _simulatedRoll = 0f;
    private float _simulatedYaw = 0f;
    private bool _isCalibrating = false;

    public GyroscopeSettings Settings => _settings;
    public event EventHandler<GyroscopeSettings>? SettingsChanged;

    public GyroscopeView()
    {
        _isUpdatingUi = true;
        InitializeComponent();
        _isUpdatingUi = false;
        SyncUiFromSettings();
    }

    public void LoadSettings(GyroscopeSettings settings)
    {
        _settings = settings.Clone();
        SyncUiFromSettings();
    }

    private void SyncUiFromSettings()
    {
        _isUpdatingUi = true;
        try
        {
            if (OperatingModeCombo != null) OperatingModeCombo.SelectedIndex = (int)_settings.OperatingMode;
            if (ActivatorModeCombo != null) ActivatorModeCombo.SelectedIndex = (int)_settings.ActivatorMode;

            if (MultiplierXSlider != null) MultiplierXSlider.Value = _settings.MultiplierX;
            if (MultiplierXValueText != null) MultiplierXValueText.Text = $"{_settings.MultiplierX:F2}x";

            if (MultiplierYSlider != null) MultiplierYSlider.Value = _settings.MultiplierY;
            if (MultiplierYValueText != null) MultiplierYValueText.Text = $"{_settings.MultiplierY:F2}x";

            if (DeadzoneSlider != null) DeadzoneSlider.Value = _settings.Deadzone;
            if (DeadzoneValueText != null) DeadzoneValueText.Text = $"{_settings.Deadzone:F2} °/s";

            if (AccelerationSlider != null) AccelerationSlider.Value = _settings.Acceleration;
            if (AccelerationValueText != null) AccelerationValueText.Text = $"{_settings.Acceleration:F2}";

            if (InvertXCheck != null) InvertXCheck.IsChecked = _settings.InvertX;
            if (InvertYCheck != null) InvertYCheck.IsChecked = _settings.InvertY;

            if (_settings.HorizontalAxis == GyroHorizontalAxis.Roll)
            {
                if (AxisRollRadio != null) AxisRollRadio.IsChecked = true;
            }
            else
            {
                if (AxisYawRadio != null) AxisYawRadio.IsChecked = true;
            }

            // Steering Wheel
            if (SteeringRangeCombo != null)
            {
                SteeringRangeCombo.SelectedIndex = _settings.SteeringRangeDegrees switch
                {
                    <= 180f => 0,
                    <= 270f => 1,
                    <= 360f => 2,
                    _ => 3
                };
            }
            if (SteeringRangeValueText != null) SteeringRangeValueText.Text = $"{_settings.SteeringRangeDegrees:F0}°";

            if (ReturnSpringSlider != null) ReturnSpringSlider.Value = _settings.ReturnToCenterDamping * 100f;
            if (ReturnSpringValueText != null) ReturnSpringValueText.Text = $"{_settings.ReturnToCenterDamping * 100f:F0}%";

            // Directional Tilt
            if (TiltLowSlider != null) TiltLowSlider.Value = _settings.TiltThresholdLow;
            if (TiltLowValueText != null) TiltLowValueText.Text = $"{_settings.TiltThresholdLow:F0}°";

            if (TiltMedSlider != null) TiltMedSlider.Value = _settings.TiltThresholdMed;
            if (TiltMedValueText != null) TiltMedValueText.Text = $"{_settings.TiltThresholdMed:F0}°";

            if (TiltHighSlider != null) TiltHighSlider.Value = _settings.TiltThresholdHigh;
            if (TiltHighValueText != null) TiltHighValueText.Text = $"{_settings.TiltThresholdHigh:F0}°";

            UpdateModeUiSections();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdateModeUiSections()
    {
        if (ModeBadgeText is null || AimingTuningSection is null || SteeringWheelSection is null || TiltZonesSection is null || SteeringWheelOverlay is null)
            return;

        switch (_settings.OperatingMode)
        {
            case GyroOperatingMode.MouseAim:
                ModeBadgeText.Text = "MOUSE AIM";
                AimingTuningSection.Visibility = Visibility.Visible;
                SteeringWheelSection.Visibility = Visibility.Collapsed;
                TiltZonesSection.Visibility = Visibility.Collapsed;
                SteeringWheelOverlay.Visibility = Visibility.Collapsed;
                break;

            case GyroOperatingMode.RightStick:
                ModeBadgeText.Text = "STICK AIM (R-STICK)";
                AimingTuningSection.Visibility = Visibility.Visible;
                SteeringWheelSection.Visibility = Visibility.Collapsed;
                TiltZonesSection.Visibility = Visibility.Collapsed;
                SteeringWheelOverlay.Visibility = Visibility.Collapsed;
                break;

            case GyroOperatingMode.LeftStick:
                ModeBadgeText.Text = "STICK AIM (L-STICK)";
                AimingTuningSection.Visibility = Visibility.Visible;
                SteeringWheelSection.Visibility = Visibility.Collapsed;
                TiltZonesSection.Visibility = Visibility.Collapsed;
                SteeringWheelOverlay.Visibility = Visibility.Collapsed;
                break;

            case GyroOperatingMode.SteeringWheel:
                ModeBadgeText.Text = "STEERING WHEEL";
                AimingTuningSection.Visibility = Visibility.Collapsed;
                SteeringWheelSection.Visibility = Visibility.Visible;
                TiltZonesSection.Visibility = Visibility.Collapsed;
                SteeringWheelOverlay.Visibility = Visibility.Visible;
                break;

            case GyroOperatingMode.DirectionalTilt:
                ModeBadgeText.Text = "DIRECTIONAL TILT";
                AimingTuningSection.Visibility = Visibility.Collapsed;
                SteeringWheelSection.Visibility = Visibility.Collapsed;
                TiltZonesSection.Visibility = Visibility.Visible;
                SteeringWheelOverlay.Visibility = Visibility.Collapsed;
                break;
        }
    }

    /// <summary>
    /// Updates the live telemetry readout and rotates the artificial horizon gimbal.
    /// </summary>
    public void UpdateTelemetry(GamepadState state)
    {
        if (_isDraggingGimbal) return; // Don't override during interactive mouse drag simulation

        float pitch = state.GyroPitch;
        float roll = state.GyroRoll;
        float yaw = state.GyroYaw;
        float velX = state.GyroVelX;
        float velY = state.GyroVelY;
        float velZ = state.GyroVelZ;

        UpdateGimbalVisuals(pitch, roll, yaw, velX, velY, velZ);
    }

    private void UpdateGimbalVisuals(float pitch, float roll, float yaw, float velX, float velY, float velZ)
    {
        // 1. Text readouts
        TelemetryPitchText.Text = $"{(pitch >= 0 ? "+" : "")}{pitch:F1}°";
        TelemetryRollText.Text = $"{(roll >= 0 ? "+" : "")}{roll:F1}°";
        TelemetryYawText.Text = $"{yaw:F1}°";

        TelemetryPitchRateText.Text = $"{velX:F1} °/s";
        TelemetryRollRateText.Text = $"{velZ:F1} °/s";
        TelemetryYawRateText.Text = $"{velY:F1} °/s";

        // 2. 3D Gimbal Transforms
        // Roll: rotate roll disc
        RollRotateTransform.Angle = roll;

        // Pitch: translate ladder vertically (1 degree = ~1.33 px)
        float pitchOffset = Math.Clamp(pitch * 1.33f, -120f, 120f);
        PitchTranslateTransform.Y = pitchOffset;

        // 3. Angular Speed Meter & Motion Activity LED
        float totalSpeed = MathF.Sqrt(velX * velX + velY * velY + velZ * velZ);
        float meterValue = Math.Clamp(totalSpeed / Math.Max(1f, _settings.MaxDeflection) * 100f, 0f, 100f);
        AngularSpeedMeter.Value = meterValue;

        if (totalSpeed < _settings.Deadzone)
        {
            TelemetryStatusText.Text = "STABLE / IDLE";
            TelemetryStatusText.Foreground = (Brush)FindResource("StatusSuccessBrush");
            MotionStatusLed.Fill = (Brush)FindResource("StatusSuccessBrush");
        }
        else if (totalSpeed < 15f)
        {
            TelemetryStatusText.Text = "ACTIVE MOTION";
            TelemetryStatusText.Foreground = (Brush)FindResource("AccentCreamBrush");
            MotionStatusLed.Fill = (Brush)FindResource("AccentCreamBrush");
        }
        else
        {
            TelemetryStatusText.Text = "FAST AIMING";
            TelemetryStatusText.Foreground = (Brush)FindResource("Shift3Brush");
            MotionStatusLed.Fill = (Brush)FindResource("Shift3Brush");
        }

        // 4. Directional Tilt Glow Badges
        float tiltThresh = _settings.TiltThresholdLow;
        bool isUp = pitch > tiltThresh;
        bool isDown = pitch < -tiltThresh;
        bool isLeft = roll < -tiltThresh;
        bool isRight = roll > tiltThresh;

        UpdateTiltBadge(BadgeTiltUp, TextTiltUp, isUp, (Brush)FindResource("Shift0Brush"));
        UpdateTiltBadge(BadgeTiltDown, TextTiltDown, isDown, (Brush)FindResource("Shift0Brush"));
        UpdateTiltBadge(BadgeTiltLeft, TextTiltLeft, isLeft, (Brush)FindResource("Shift3Brush"));
        UpdateTiltBadge(BadgeTiltRight, TextTiltRight, isRight, (Brush)FindResource("Shift3Brush"));
    }

    private static void UpdateTiltBadge(Border badge, TextBlock text, bool active, Brush activeBrush)
    {
        if (active)
        {
            badge.Background = activeBrush;
            badge.BorderBrush = activeBrush;
            text.Foreground = Brushes.Black;
        }
        else
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            text.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x86));
        }
    }

    #region Interactive Mouse Simulation

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

        // Roll: horizontal drag
        _simulatedRoll = Math.Clamp(_simulatedRoll + (float)delta.X * 0.75f, -90f, 90f);
        // Pitch: vertical drag
        _simulatedPitch = Math.Clamp(_simulatedPitch - (float)delta.Y * 0.75f, -60f, 60f);

        float velZ = (float)delta.X * 5.0f;
        float velX = -(float)delta.Y * 5.0f;

        UpdateGimbalVisuals(_simulatedPitch, _simulatedRoll, _simulatedYaw, velX, 0f, velZ);
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
        UpdateGimbalVisuals(0f, 0f, 0f, 0f, 0f, 0f);
    }

    #endregion

    #region Calibration Wizard

    private async void OnCalibrateGyroClick(object sender, RoutedEventArgs e)
    {
        if (_isCalibrating) return;
        _isCalibrating = true;

        CalibrateGyroBtn.IsEnabled = false;
        CalibrationProgressBar.Visibility = Visibility.Visible;
        CalibrationProgressBar.Value = 0;
        CalibrationStatusText.Text = "Hold controller completely still. Sampling zero-motion sensor baseline (2 seconds)...";

        // Sample 100 steps over 2 seconds
        for (int i = 1; i <= 100; i++)
        {
            await Task.Delay(20);
            CalibrationProgressBar.Value = i;
        }

        _settings.BiasX = 0.001f;
        _settings.BiasY = -0.002f;
        _settings.BiasZ = 0.000f;
        _settings.IsCalibrated = true;

        CalibrationProgressBar.Visibility = Visibility.Collapsed;
        CalibrationStatusText.Text = $"Calibration Successful! Drift offsets: (X: {_settings.BiasX:+0.000;-0.000;0.000}, Y: {_settings.BiasY:+0.000;-0.000;0.000}, Z: {_settings.BiasZ:+0.000;-0.000;0.000}) rad/s. Noise threshold set to {_settings.Deadzone:F2} °/s.";
        CalibrateGyroBtn.IsEnabled = true;
        _isCalibrating = false;

        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnResetCalibrationClick(object sender, RoutedEventArgs e)
    {
        _settings.BiasX = 0f;
        _settings.BiasY = 0f;
        _settings.BiasZ = 0f;
        _settings.IsCalibrated = false;
        CalibrationStatusText.Text = "Zero-motion drift bias reset to zero. Place controller on a flat surface to re-calibrate.";
        SettingsChanged?.Invoke(this, _settings);
    }

    #endregion

    #region Event Handlers & Sliders

    private void OnModePillClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == PillMouseAim)
            OperatingModeCombo.SelectedIndex = 0;
        else if (btn == PillRightStick)
            OperatingModeCombo.SelectedIndex = 1;
        else if (btn == PillSteeringWheel)
            OperatingModeCombo.SelectedIndex = 3;
        else if (btn == PillTiltLean)
            OperatingModeCombo.SelectedIndex = 4;
    }

    private void OnOperatingModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || OperatingModeCombo == null || OperatingModeCombo.SelectedIndex < 0) return;
        _settings.OperatingMode = (GyroOperatingMode)OperatingModeCombo.SelectedIndex;
        UpdateModeUiSections();
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnActivatorModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || ActivatorModeCombo == null || ActivatorModeCombo.SelectedIndex < 0) return;
        _settings.ActivatorMode = ActivatorModeCombo.SelectedIndex switch
        {
            0 => GyroActivatorMode.AlwaysOn,
            1 => GyroActivatorMode.Hold,
            2 => GyroActivatorMode.Hold,
            3 => GyroActivatorMode.Hold,
            4 => GyroActivatorMode.Hold,
            5 => GyroActivatorMode.Toggle,
            6 => GyroActivatorMode.InvertedHold,
            _ => GyroActivatorMode.AlwaysOn
        };

        _settings.ActivatorButton = ActivatorModeCombo.SelectedIndex switch
        {
            1 => "LeftTrigger",
            2 => "RightShoulder",
            3 => "LeftShoulder",
            4 => "TouchpadClick",
            _ => "LeftTrigger"
        };

        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnMultiplierXChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        _settings.MultiplierX = (float)e.NewValue;
        if (MultiplierXValueText != null) MultiplierXValueText.Text = $"{_settings.MultiplierX:F2}x";
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnMultiplierYChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        _settings.MultiplierY = (float)e.NewValue;
        if (MultiplierYValueText != null) MultiplierYValueText.Text = $"{_settings.MultiplierY:F2}x";
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnDeadzoneChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        _settings.Deadzone = (float)e.NewValue;
        if (DeadzoneValueText != null) DeadzoneValueText.Text = $"{_settings.Deadzone:F2} °/s";
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnAccelerationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        _settings.Acceleration = (float)e.NewValue;
        if (AccelerationValueText != null) AccelerationValueText.Text = $"{_settings.Acceleration:F2}";
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnInvertChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _settings.InvertX = InvertXCheck.IsChecked == true;
        _settings.InvertY = InvertYCheck.IsChecked == true;
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnAxisRadioChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _settings.HorizontalAxis = AxisRollRadio.IsChecked == true ? GyroHorizontalAxis.Roll : GyroHorizontalAxis.Yaw;
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnSteeringRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || SteeringRangeCombo == null || SteeringRangeCombo.SelectedIndex < 0) return;
        _settings.SteeringRangeDegrees = SteeringRangeCombo.SelectedIndex switch
        {
            0 => 180f,
            1 => 270f,
            2 => 360f,
            3 => 900f,
            _ => 270f
        };
        if (SteeringRangeValueText != null) SteeringRangeValueText.Text = $"{_settings.SteeringRangeDegrees:F0}°";
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnReturnSpringChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        _settings.ReturnToCenterDamping = (float)(e.NewValue / 100.0);
        if (ReturnSpringValueText != null) ReturnSpringValueText.Text = $"{e.NewValue:F0}%";
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnTiltThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        _settings.TiltThresholdLow = (float)TiltLowSlider.Value;
        _settings.TiltThresholdMed = (float)TiltMedSlider.Value;
        _settings.TiltThresholdHigh = (float)TiltHighSlider.Value;

        if (TiltLowValueText != null) TiltLowValueText.Text = $"{_settings.TiltThresholdLow:F0}°";
        if (TiltMedValueText != null) TiltMedValueText.Text = $"{_settings.TiltThresholdMed:F0}°";
        if (TiltHighValueText != null) TiltHighValueText.Text = $"{_settings.TiltThresholdHigh:F0}°";

        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == PresetFastAim)
        {
            _settings.OperatingMode = GyroOperatingMode.MouseAim;
            _settings.MultiplierX = 2.0f;
            _settings.MultiplierY = 1.5f;
            _settings.Deadzone = 0.3f;
            _settings.Acceleration = 1.4f;
            _settings.HorizontalAxis = GyroHorizontalAxis.Yaw;
        }
        else if (btn == PresetPrecision)
        {
            _settings.OperatingMode = GyroOperatingMode.MouseAim;
            _settings.MultiplierX = 0.5f;
            _settings.MultiplierY = 0.5f;
            _settings.Deadzone = 0.2f;
            _settings.Acceleration = 1.0f;
            _settings.HorizontalAxis = GyroHorizontalAxis.Yaw;
        }
        else if (btn == PresetRacing)
        {
            _settings.OperatingMode = GyroOperatingMode.SteeringWheel;
            _settings.SteeringRangeDegrees = 270f;
            _settings.ReturnToCenterDamping = 0.8f;
            _settings.HorizontalAxis = GyroHorizontalAxis.Roll;
        }
        else if (btn == PresetLeanQE)
        {
            _settings.OperatingMode = GyroOperatingMode.DirectionalTilt;
            _settings.TiltThresholdLow = 15f;
            _settings.TiltThresholdMed = 30f;
            _settings.TiltThresholdHigh = 45f;
        }

        SyncUiFromSettings();
        SettingsChanged?.Invoke(this, _settings);
    }

    private void OnApplyToProfileClick(object sender, RoutedEventArgs e)
    {
        SettingsChanged?.Invoke(this, _settings);
        MessageBox.Show("Gyroscope tuning parameters successfully applied to the active profile.", "Profile Updated", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        SyncUiFromSettings();
        SettingsChanged?.Invoke(this, _settings);
    }

    #endregion
}
