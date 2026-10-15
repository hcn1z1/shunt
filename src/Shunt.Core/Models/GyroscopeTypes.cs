using System;

namespace Shunt.Core.Models;

/// <summary>
/// Primary operating modes for the controller gyroscope.
/// </summary>
public enum GyroOperatingMode
{
    /// <summary>
    /// Translates angular velocity into Windows mouse cursor delta movement.
    /// </summary>
    MouseAim = 0,

    /// <summary>
    /// Maps gyroscope pitch and yaw into virtual Right Stick deflection.
    /// </summary>
    RightStick = 1,

    /// <summary>
    /// Maps gyroscope pitch and yaw into virtual Left Stick deflection.
    /// </summary>
    LeftStick = 2,

    /// <summary>
    /// Translates controller roll into an analog virtual steering wheel axis.
    /// </summary>
    SteeringWheel = 3,

    /// <summary>
    /// Triggers discrete directional actions when tilting beyond configurable degree thresholds.
    /// </summary>
    DirectionalTilt = 4
}

/// <summary>
/// Controls when the gyroscope is actively engaged.
/// </summary>
public enum GyroActivatorMode
{
    AlwaysOn = 0,
    Hold = 1,
    Toggle = 2,
    InvertedHold = 3
}

/// <summary>
/// Axis used to calculate horizontal aiming deflection.
/// </summary>
public enum GyroHorizontalAxis
{
    Yaw = 0,
    Roll = 1
}

/// <summary>
/// Complete hardware & software configuration for the controller gyroscope.
/// </summary>
public class GyroscopeSettings
{
    public GyroOperatingMode OperatingMode { get; set; } = GyroOperatingMode.MouseAim;
    public GyroActivatorMode ActivatorMode { get; set; } = GyroActivatorMode.AlwaysOn;
    public string ActivatorButton { get; set; } = "LeftTrigger";

    // Sensitivity & Deflection Tuning
    public float MultiplierX { get; set; } = 1.0f;
    public float MultiplierY { get; set; } = 1.0f;
    public float Deadzone { get; set; } = 0.5f;           // deg/s noise threshold
    public float MaxDeflection { get; set; } = 25.0f;     // deg/s for 100% stick deflection
    public float Acceleration { get; set; } = 1.0f;
    public bool InvertX { get; set; } = false;
    public bool InvertY { get; set; } = false;
    public GyroHorizontalAxis HorizontalAxis { get; set; } = GyroHorizontalAxis.Yaw;

    // Steering Wheel Specific
    public float SteeringRangeDegrees { get; set; } = 270.0f;
    public float ReturnToCenterDamping { get; set; } = 0.8f;

    // Directional Tilt Specific (degrees)
    public float TiltThresholdLow { get; set; } = 15.0f;
    public float TiltThresholdMed { get; set; } = 30.0f;
    public float TiltThresholdHigh { get; set; } = 45.0f;

    // Calibration Zero-Offsets (drift compensation)
    public float BiasX { get; set; } = 0.0f;
    public float BiasY { get; set; } = 0.0f;
    public float BiasZ { get; set; } = 0.0f;
    public bool IsCalibrated { get; set; } = false;

    public GyroscopeSettings Clone() => (GyroscopeSettings)MemberwiseClone();

    public void ResetToDefaults()
    {
        OperatingMode = GyroOperatingMode.MouseAim;
        ActivatorMode = GyroActivatorMode.AlwaysOn;
        ActivatorButton = "LeftTrigger";
        MultiplierX = 1.0f;
        MultiplierY = 1.0f;
        Deadzone = 0.5f;
        MaxDeflection = 25.0f;
        Acceleration = 1.0f;
        InvertX = false;
        InvertY = false;
        HorizontalAxis = GyroHorizontalAxis.Yaw;
        SteeringRangeDegrees = 270.0f;
        ReturnToCenterDamping = 0.8f;
        TiltThresholdLow = 15.0f;
        TiltThresholdMed = 30.0f;
        TiltThresholdHigh = 45.0f;
        BiasX = 0f;
        BiasY = 0f;
        BiasZ = 0f;
        IsCalibrated = false;
    }
}
