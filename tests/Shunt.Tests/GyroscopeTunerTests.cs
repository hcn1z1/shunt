using System;
using System.Collections.Generic;
using Shunt.Core.Models;
using Shunt.Engine;
using Shunt.Profiles;
using Shunt.Profiles.Models;
using Xunit;

namespace Shunt.Tests;

public class GyroscopeTunerTests
{
    [Fact]
    public void GyroscopeSettings_DefaultValues_AreValid()
    {
        var settings = new GyroscopeSettings();

        Assert.Equal(GyroOperatingMode.MouseAim, settings.OperatingMode);
        Assert.Equal(GyroActivatorMode.AlwaysOn, settings.ActivatorMode);
        Assert.Equal("LeftTrigger", settings.ActivatorButton);
        Assert.Equal(1.0f, settings.MultiplierX);
        Assert.Equal(1.0f, settings.MultiplierY);
        Assert.Equal(0.5f, settings.Deadzone);
        Assert.Equal(25.0f, settings.MaxDeflection);
        Assert.Equal(1.0f, settings.Acceleration);
        Assert.False(settings.InvertX);
        Assert.False(settings.InvertY);
        Assert.Equal(GyroHorizontalAxis.Yaw, settings.HorizontalAxis);
        Assert.Equal(270.0f, settings.SteeringRangeDegrees);
        Assert.Equal(15.0f, settings.TiltThresholdLow);
        Assert.False(settings.IsCalibrated);
    }

    [Fact]
    public void GyroscopeSettings_CloneAndReset_WorkCorrectly()
    {
        var settings = new GyroscopeSettings
        {
            OperatingMode = GyroOperatingMode.SteeringWheel,
            MultiplierX = 2.5f,
            Deadzone = 1.2f,
            InvertX = true,
            SteeringRangeDegrees = 360f,
            IsCalibrated = true
        };

        var clone = settings.Clone();
        Assert.Equal(GyroOperatingMode.SteeringWheel, clone.OperatingMode);
        Assert.Equal(2.5f, clone.MultiplierX);
        Assert.Equal(1.2f, clone.Deadzone);
        Assert.True(clone.InvertX);
        Assert.Equal(360f, clone.SteeringRangeDegrees);
        Assert.True(clone.IsCalibrated);

        clone.ResetToDefaults();
        Assert.Equal(GyroOperatingMode.MouseAim, clone.OperatingMode);
        Assert.Equal(1.0f, clone.MultiplierX);
        Assert.Equal(0.5f, clone.Deadzone);
        Assert.False(clone.InvertX);
        Assert.False(clone.IsCalibrated);
    }

    [Fact]
    public void ProfileCompiler_CompilesGyroSettings_Successfully()
    {
        var doc = new RewasdProfileDocument
        {
            Gyro = new ProfileGyroConfig
            {
                Mode = "wheel",
                Activator = "hold",
                ActivatorButton = "LeftTrigger",
                SensitivityX = 1.75f,
                SensitivityY = 1.25f,
                Deadzone = 0.8f,
                Acceleration = 1.5f,
                InvertX = true,
                InvertY = false,
                HorizontalAxis = "roll",
                SteeringRange = 360.0f,
                TiltThresholdLow = 20.0f
            }
        };

        var compiled = ProfileCompiler.Compile(doc);
        Assert.NotNull(compiled.GyroSettings);
        Assert.Equal(GyroOperatingMode.SteeringWheel, compiled.GyroSettings.OperatingMode);
        Assert.Equal(GyroActivatorMode.Hold, compiled.GyroSettings.ActivatorMode);
        Assert.Equal("LeftTrigger", compiled.GyroSettings.ActivatorButton);
        Assert.Equal(1.75f, compiled.GyroSettings.MultiplierX);
        Assert.Equal(1.25f, compiled.GyroSettings.MultiplierY);
        Assert.Equal(0.8f, compiled.GyroSettings.Deadzone);
        Assert.Equal(1.5f, compiled.GyroSettings.Acceleration);
        Assert.True(compiled.GyroSettings.InvertX);
        Assert.False(compiled.GyroSettings.InvertY);
        Assert.Equal(GyroHorizontalAxis.Roll, compiled.GyroSettings.HorizontalAxis);
        Assert.Equal(360.0f, compiled.GyroSettings.SteeringRangeDegrees);
        Assert.Equal(20.0f, compiled.GyroSettings.TiltThresholdLow);
    }

    [Fact]
    public void MappingPipeline_ProcessesSteeringWheelMode()
    {
        var ctx = new MappingExecutionContext
        {
            GyroSettings = new GyroscopeSettings
            {
                OperatingMode = GyroOperatingMode.SteeringWheel,
                SteeringRangeDegrees = 180.0f, // Half-range is 90 deg
                InvertX = false
            }
        };

        var pipeline = new MappingPipeline(ctx);

        // Tilt 45 degrees to the right (+45 roll) -> expected LeftStick.X = +0.5
        var input = new GamepadState
        {
            GyroRoll = 45.0f
        };

        var output = pipeline.Process(input);
        Assert.InRange(output.LeftStick.X, 0.49f, 0.51f);

        // Tilt -90 degrees to the left (-90 roll) -> expected LeftStick.X = -1.0
        var inputFullLeft = new GamepadState
        {
            GyroRoll = -90.0f
        };

        var outputLeft = pipeline.Process(inputFullLeft);
        Assert.InRange(outputLeft.LeftStick.X, -1.0f, -0.99f);
    }

    [Fact]
    public void MappingPipeline_ProcessesGyroRightStickDeflection()
    {
        var ctx = new MappingExecutionContext
        {
            GyroSettings = new GyroscopeSettings
            {
                OperatingMode = GyroOperatingMode.RightStick,
                MaxDeflection = 50.0f,
                MultiplierX = 1.0f,
                MultiplierY = 1.0f,
                HorizontalAxis = GyroHorizontalAxis.Yaw
            }
        };

        var pipeline = new MappingPipeline(ctx);

        // Turn right at 25 deg/s yaw -> expected RightStick.X = 0.5
        var input = new GamepadState
        {
            GyroVelY = 25.0f,
            GyroVelX = 0.0f
        };

        var output = pipeline.Process(input);
        Assert.InRange(output.RightStick.X, 0.49f, 0.51f);
    }

    [Fact]
    public void MappingPipeline_ProcessesDirectionalTilt_Zones()
    {
        var ctx = new MappingExecutionContext
        {
            GyroSettings = new GyroscopeSettings
            {
                OperatingMode = GyroOperatingMode.DirectionalTilt,
                TiltThresholdLow = 15.0f
            }
        };

        var pipeline = new MappingPipeline(ctx);

        // Pitch up +25 deg -> TiltUp
        var inputUp = new GamepadState { GyroPitch = 25.0f };
        var outputUp = pipeline.Process(inputUp);
        Assert.True(outputUp.HasButton(GamepadButtons.TiltUp));
        Assert.False(outputUp.HasButton(GamepadButtons.TiltDown));

        // Pitch down -25 deg -> TiltDown
        var inputDown = new GamepadState { GyroPitch = -25.0f };
        var outputDown = pipeline.Process(inputDown);
        Assert.True(outputDown.HasButton(GamepadButtons.TiltDown));

        // Roll left -25 deg -> TiltLeft
        var inputLeft = new GamepadState { GyroRoll = -25.0f };
        var outputLeft = pipeline.Process(inputLeft);
        Assert.True(outputLeft.HasButton(GamepadButtons.TiltLeft));

        // Roll right +25 deg -> TiltRight
        var inputRight = new GamepadState { GyroRoll = 25.0f };
        var outputRight = pipeline.Process(inputRight);
        Assert.True(outputRight.HasButton(GamepadButtons.TiltRight));
    }

    [Fact]
    public void ButtonIdResolver_ResolvesGyroscopeButtonIdsAndDescriptions()
    {
        // Button IDs 68-76 from Native Behavior.rewasd
        Assert.Equal(GamepadButtons.TiltUp, ButtonIdResolver.ResolveButtonId(68));
        Assert.Equal(GamepadButtons.TiltDown, ButtonIdResolver.ResolveButtonId(69));
        Assert.Equal(GamepadButtons.TiltLeft, ButtonIdResolver.ResolveButtonId(70));
        Assert.Equal(GamepadButtons.TiltRight, ButtonIdResolver.ResolveButtonId(71));
        Assert.Equal(GamepadButtons.TiltLeft, ButtonIdResolver.ResolveButtonId(72));
        Assert.Equal(GamepadButtons.TiltRight, ButtonIdResolver.ResolveButtonId(73));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(74));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(75));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(76));

        // String descriptions
        Assert.Equal(GamepadButtons.TiltUp, ButtonIdResolver.ResolveButtonId(0, "Gyroscope up"));
        Assert.Equal(GamepadButtons.TiltDown, ButtonIdResolver.ResolveButtonId(0, "Gyroscope down"));
        Assert.Equal(GamepadButtons.TiltLeft, ButtonIdResolver.ResolveButtonId(0, "Gyroscope left"));
        Assert.Equal(GamepadButtons.TiltRight, ButtonIdResolver.ResolveButtonId(0, "Gyroscope right"));
        Assert.Equal(GamepadButtons.TiltLeft, ButtonIdResolver.ResolveButtonId(0, "Gyroscope lean left"));
        Assert.Equal(GamepadButtons.TiltRight, ButtonIdResolver.ResolveButtonId(0, "Gyroscope lean right"));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(0, "Gyroscope low"));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(0, "Gyroscope med"));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(0, "Gyroscope high"));
    }
}
