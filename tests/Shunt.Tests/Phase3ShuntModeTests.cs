using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Engine;
using Shunt.Engine.Hardware;
using Xunit;

namespace Shunt.Tests;

public class Phase3ShuntModeTests
{
    // =========================================================================
    // M3.1: Hardware IMU Device Profile & Report Layout Tests
    // =========================================================================

    [Fact]
    public void ImuDeviceProfile_Find_Recognizes_GameSirG7Pro()
    {
        var profile8k = ImuDeviceProfile.Find(0x3537, 0x1004);
        Assert.NotNull(profile8k);
        Assert.Equal("GameSir G7 Pro / 8K", profile8k.Name);

        var profileStandard = ImuDeviceProfile.Find(0x3537, 0x100A);
        Assert.NotNull(profileStandard);
        Assert.Equal("GameSir G7 Pro", profileStandard.Name);
        Assert.Equal(0x12, profileStandard.ReportFormat.ReportId);
        Assert.Equal(16, profileStandard.ReportFormat.MinimumLength);
    }

    [Fact]
    public void ImuDeviceProfile_Find_Rejects_Unverified_Devices()
    {
        // Unverified / non-existent vendor product ID
        var unverified = ImuDeviceProfile.Find(0x3537, 0x9999);
        Assert.Null(unverified);

        // Standard Microsoft Xbox 360 controller lacks gyro hardware
        var xbox360 = ImuDeviceProfile.Find(0x045E, 0x028E);
        Assert.Null(xbox360);
    }

    [Fact]
    public void ImuDeviceProfile_KnownProfiles_OnlyContains_VerifiedHardware()
    {
        Assert.NotEmpty(ImuDeviceProfile.KnownProfiles);
        Assert.All(ImuDeviceProfile.KnownProfiles, p =>
        {
            Assert.True(p.VendorId != 0);
            Assert.True(p.ProductId != 0);
            Assert.NotNull(p.ReportFormat);
            Assert.True(p.ReportFormat.ReportId > 0);
            Assert.True(p.ReportFormat.AccelerationGPerLsb > 0);
            Assert.True(p.ReportFormat.GyroDegreesPerSecondPerLsb > 0);
        });
    }

    [Fact]
    public void ImuReportFormat_TryParse_ParsesValid_GameSirG7ProReport()
    {
        var format = ImuReportFormat.GameSirG7Pro;
        var report = new byte[16];
        report[0] = 0x12; // ReportId
        report[1] = 42;   // Sequence

        // Accel: 8192 LSB per 1.0G
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(4, 2), 8192);   // AccelX = +1.0g
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(6, 2), -4096);  // AccelY = -0.5g
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(8, 2), 0);      // AccelZ = 0.0g

        // Gyro: 16 LSB per 1 deg/s
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(10, 2), 160);   // GyroPitch = +10.0 deg/s
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(12, 2), -320);  // GyroRoll = -20.0 deg/s
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(14, 2), 480);   // GyroYaw = +30.0 deg/s

        bool success = format.TryParse(report, 123456, out var sample);

        Assert.True(success);
        Assert.Equal(123456, sample.TimestampMilliseconds);
        Assert.Equal(42, sample.Sequence);
        Assert.Equal(1.0f, sample.AccelX, 3);
        Assert.Equal(-0.5f, sample.AccelY, 3);
        Assert.Equal(0.0f, sample.AccelZ, 3);
        Assert.Equal(10.0f, sample.GyroX, 3);
        Assert.Equal(-20.0f, sample.GyroY, 3);
        Assert.Equal(30.0f, sample.GyroZ, 3);
    }

    [Fact]
    public void ImuReportFormat_TryParse_Rejects_InvalidReports()
    {
        var format = ImuReportFormat.GameSirG7Pro;

        // Buffer too short (< 16)
        var shortBuffer = new byte[15];
        shortBuffer[0] = 0x12;
        Assert.False(format.TryParse(shortBuffer, 0, out _));

        // Wrong report ID
        var wrongId = new byte[16];
        wrongId[0] = 0xFF;
        Assert.False(format.TryParse(wrongId, 0, out _));
    }

    // =========================================================================
    // M3.2: "Gyro ShadPS4" Test Runner Pass & Evaluation Criteria Tests
    // =========================================================================

    [Fact]
    public void GyroShadPs4TestRunner_Evaluate_Passes_WhenCriteriaMet()
    {
        var runner = new GyroShadPs4TestRunner();
        var samples = new List<ImuSensorSample>();

        // 600 samples across 3 seconds (200Hz)
        for (int i = 0; i < 600; i++)
        {
            long t = (long)(i * 5); // 5ms step -> 200Hz
            float gyroRate = i < 80 ? 0f : (float)Math.Sin(i * 0.1) * 80f; // Baseline still for 400ms, then delta >= 120
            samples.Add(new ImuSensorSample(
                TimestampMilliseconds: t,
                Sequence: (ushort)(i % 256),
                AccelX: 0f,
                AccelY: 0f,
                AccelZ: 1.0f, // 1G gravity vector
                GyroX: gyroRate,
                GyroY: 0f,
                GyroZ: 0f));
        }

        var result = runner.Evaluate(samples, TimeSpan.FromSeconds(3));

        Assert.Equal(600, result.PacketCount);
        Assert.True(result.PollingRateHz >= 200, $"PollingRate was {result.PollingRateHz}");
        Assert.True(result.AngularVelocityDeltaDegreesPerSecond >= 120, $"AngularDelta was {result.AngularVelocityDeltaDegreesPerSecond}");
        Assert.True(result.GravityMagnitudeG >= 0.75 && result.GravityMagnitudeG <= 1.25, $"Gravity was {result.GravityMagnitudeG}");
        Assert.True(result.NoiseFloorDegreesPerSecond <= 1.5, $"NoiseFloor was {result.NoiseFloorDegreesPerSecond}");
        Assert.True(result.Passed, $"Passed was false: {result.Message}");
        Assert.Contains("verified", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GyroShadPs4TestRunner_Evaluate_Fails_WhenMotionless()
    {
        var runner = new GyroShadPs4TestRunner();
        var samples = new List<ImuSensorSample>();

        // 600 samples, but controller remained still (delta = 0)
        for (int i = 0; i < 600; i++)
        {
            samples.Add(new ImuSensorSample(i * 5, (ushort)i, 0f, 0f, 1.0f, 0f, 0f, 0f));
        }

        var result = runner.Evaluate(samples, TimeSpan.FromSeconds(3));

        Assert.False(result.Passed);
        Assert.Contains("Not enough motion", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GyroShadPs4TestRunner_Evaluate_Fails_WhenPacketRateTooLow()
    {
        var runner = new GyroShadPs4TestRunner();
        var samples = new List<ImuSensorSample>();

        // Only 100 packets over 3 seconds (33Hz)
        for (int i = 0; i < 100; i++)
        {
            samples.Add(new ImuSensorSample(i * 30, (ushort)i, 0f, 0f, 1.0f, 50f, 0f, 0f));
        }

        var result = runner.Evaluate(samples, TimeSpan.FromSeconds(3));

        Assert.False(result.Passed);
        Assert.Contains("sensor stream is too slow", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GyroShadPs4TestRunner_Evaluate_Fails_WhenGravityPlausibilityFails()
    {
        var runner = new GyroShadPs4TestRunner();
        var samples = new List<ImuSensorSample>();

        // 600 samples, but gravity magnitude is 3.0G (unrealistic for stationary/gently tilted)
        for (int i = 0; i < 600; i++)
        {
            float gyro = i < 60 ? 0f : (float)Math.Sin(i * 0.1) * 80f;
            samples.Add(new ImuSensorSample(i * 5, (ushort)i, 0f, 0f, 3.0f, gyro, 0f, 0f));
        }

        var result = runner.Evaluate(samples, TimeSpan.FromSeconds(3));

        Assert.False(result.Passed);
        Assert.Contains("gravity vector", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GyroShadPs4TestRunner_Evaluate_Handles_EmptySamples()
    {
        var runner = new GyroShadPs4TestRunner();
        var result = runner.Evaluate(Array.Empty<ImuSensorSample>(), TimeSpan.FromSeconds(3));

        Assert.False(result.Passed);
        Assert.Equal(0, result.PacketCount);
    }

    // =========================================================================
    // M3.3: Shunt Mode Core State Machine & L3+R3 Gyro Gating Tests
    // =========================================================================

    [Fact]
    public void ShuntService_L3_R3_SimultaneousPress_TogglesGyroOnAndOff()
    {
        var service = new XboxToDualShock4ShuntService();
        Assert.False(service.IsGyroEnabled);

        // Frame 1: Simultaneous press of L3 + R3 at t=100ms
        var inputChord = new GamepadState { Buttons = GamepadButtons.LeftThumb | GamepadButtons.RightThumb };
        var out1 = service.Process(inputChord, 100);

        Assert.True(service.IsGyroEnabled);
        // The stick clicks must be suppressed so they don't leak into the game
        Assert.False(out1.HasButton(GamepadButtons.LeftThumb));
        Assert.False(out1.HasButton(GamepadButtons.RightThumb));

        // Frame 2: Release sticks at t=200ms
        var inputRelease = new GamepadState { Buttons = GamepadButtons.None };
        var out2 = service.Process(inputRelease, 200);
        Assert.True(service.IsGyroEnabled); // Stays toggled on

        // Frame 3: Second simultaneous press at t=300ms toggles it OFF
        var out3 = service.Process(inputChord, 300);
        Assert.False(service.IsGyroEnabled);
        Assert.False(out3.HasButton(GamepadButtons.LeftThumb));
        Assert.False(out3.HasButton(GamepadButtons.RightThumb));
    }

    [Fact]
    public void ShuntService_IsolatedThumbClick_PassesThrough_AfterWindow()
    {
        var service = new XboxToDualShock4ShuntService();

        // Frame 1: L3 pressed alone at t=100ms
        var inputL3 = new GamepadState { Buttons = GamepadButtons.LeftThumb };
        var out1 = service.Process(inputL3, 100);
        // Held within chord window (40ms)
        Assert.False(out1.HasButton(GamepadButtons.LeftThumb));
        Assert.False(service.IsGyroEnabled);

        // Frame 2: L3 still held alone past 40ms window at t=150ms
        var out2 = service.Process(inputL3, 150);
        Assert.True(out2.HasButton(GamepadButtons.LeftThumb));
        Assert.False(service.IsGyroEnabled);
    }

    [Fact]
    public void ShuntService_HoldMode_EnablesGyroOnlyWhileHeld()
    {
        var service = new XboxToDualShock4ShuntService { ActivationMode = GyroActivatorMode.Hold };

        var inputChord = new GamepadState { Buttons = GamepadButtons.LeftThumb | GamepadButtons.RightThumb };
        service.Process(inputChord, 100);
        Assert.True(service.IsGyroEnabled);

        var inputRelease = new GamepadState { Buttons = GamepadButtons.None };
        service.Process(inputRelease, 150);
        Assert.False(service.IsGyroEnabled);
    }

    [Fact]
    public void ShuntService_ImuTransposition_Follows_SonyConvention()
    {
        var service = new XboxToDualShock4ShuntService();
        // Turn gyro ON
        service.Process(new GamepadState { Buttons = GamepadButtons.LeftThumb | GamepadButtons.RightThumb }, 100);
        Assert.True(service.IsGyroEnabled);

        // Vendor report order: Pitch (X), Roll (Y), Yaw (Z)
        // DS4 expects: Pitch (X), Yaw (Y), Roll (Z)
        var sample = new ImuSensorSample(
            TimestampMilliseconds: 200,
            Sequence: 1,
            AccelX: 0.1f,
            AccelY: 0.2f,
            AccelZ: 0.98f,
            GyroX: 15.0f,  // Pitch
            GyroY: 25.0f,  // Roll
            GyroZ: 35.0f); // Yaw

        var outActive = service.Process(new GamepadState(), 200, sample);
        Assert.Equal(15.0f, outActive.GyroVelX); // Pitch -> X
        Assert.Equal(35.0f, outActive.GyroVelY); // Yaw -> Y
        Assert.Equal(25.0f, outActive.GyroVelZ); // Roll -> Z
        Assert.Equal(0.1f, outActive.AccelX);
        Assert.Equal(0.2f, outActive.AccelY);
        Assert.Equal(0.98f, outActive.AccelZ);

        // Turn gyro OFF
        service.Process(new GamepadState { Buttons = GamepadButtons.LeftThumb | GamepadButtons.RightThumb }, 300);
        Assert.False(service.IsGyroEnabled);

        var outGated = service.Process(new GamepadState(), 400, sample);
        // Angular velocities gated to 0 when Gyro is toggled off
        Assert.Equal(0.0f, outGated.GyroVelX);
        Assert.Equal(0.0f, outGated.GyroVelY);
        Assert.Equal(0.0f, outGated.GyroVelZ);
        // Gravity accelerometer preserved
        Assert.Equal(0.1f, outGated.AccelX);
        Assert.Equal(0.2f, outGated.AccelY);
        Assert.Equal(0.98f, outGated.AccelZ);
    }

    // =========================================================================
    // M3.4: Touchpad Chords & Gesture Synthesis Tests
    // =========================================================================

    [Theory]
    [InlineData(GamepadButtons.X, GamepadButtons.TouchpadTapLeft)]
    [InlineData(GamepadButtons.B, GamepadButtons.TouchpadTapRight)]
    [InlineData(GamepadButtons.Y, GamepadButtons.TouchpadTapTop)]
    [InlineData(GamepadButtons.A, GamepadButtons.TouchpadTapBottom)]
    public void ShuntService_TouchpadQuadrantClicks_TranslateCorrectly(GamepadButtons faceButton, GamepadButtons expectedTap)
    {
        var service = new XboxToDualShock4ShuntService();
        var input = new GamepadState { Buttons = GamepadButtons.Back | faceButton };

        var output = service.Process(input, 100);

        Assert.True(output.HasButton(expectedTap));
        // Back and face button must be suppressed
        Assert.False(output.HasButton(GamepadButtons.Back));
        Assert.False(output.HasButton(faceButton));
    }

    [Fact]
    public void ShuntService_Back_Standalone_GeneratesCenterTapOnRelease()
    {
        var service = new XboxToDualShock4ShuntService();

        // Frame 1: Back pressed alone at t=100ms
        var f1 = service.Process(new GamepadState { Buttons = GamepadButtons.Back }, 100);
        Assert.False(f1.HasButton(GamepadButtons.Back));
        Assert.False(f1.HasButton(GamepadButtons.TouchpadTap));

        // Frame 2: Back released at t=150ms without chord partner
        var f2 = service.Process(new GamepadState { Buttons = GamepadButtons.None }, 150);
        Assert.True(f2.HasButton(GamepadButtons.TouchpadTap));

        // Frame 3: Next frame clears the tap
        var f3 = service.Process(new GamepadState { Buttons = GamepadButtons.None }, 160);
        Assert.False(f3.HasButton(GamepadButtons.TouchpadTap));
    }

    [Fact]
    public void ShuntService_Back_RightStick_SynthesizesDirectionalSwipes_AndCentersStick()
    {
        var service = new XboxToDualShock4ShuntService();

        // Hold Back + deflect Right Stick right (RX = 0.85 >= 0.70)
        var inputRight = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            RightStick = new StickPosition(0.85f, 0.0f)
        };
        var outRight = service.Process(inputRight, 100);
        Assert.True(outRight.HasButton(GamepadButtons.SwipeRight));
        // Camera right-stick movement must be zeroed out to prevent violent aim jerk
        Assert.Equal(0.0f, outRight.RightStick.X);
        Assert.Equal(0.0f, outRight.RightStick.Y);

        // Hold Back + deflect Right Stick left (RX = -0.85)
        var inputLeft = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            RightStick = new StickPosition(-0.85f, 0.0f)
        };
        var outLeft = service.Process(inputLeft, 110);
        Assert.True(outLeft.HasButton(GamepadButtons.SwipeLeft));
        Assert.Equal(0.0f, outLeft.RightStick.X);

        // Hold Back + deflect Right Stick up (RY = 0.85)
        var inputUp = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            RightStick = new StickPosition(0.0f, 0.85f)
        };
        var outUp = service.Process(inputUp, 120);
        Assert.True(outUp.HasButton(GamepadButtons.SwipeUp));
        Assert.Equal(0.0f, outUp.RightStick.Y);

        // Hold Back + deflect Right Stick down (RY = -0.85)
        var inputDown = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            RightStick = new StickPosition(0.0f, -0.85f)
        };
        var outDown = service.Process(inputDown, 130);
        Assert.True(outDown.HasButton(GamepadButtons.SwipeDown));
        Assert.Equal(0.0f, outDown.RightStick.Y);

        // Stick returns to deadzone (< 0.30)
        var inputCenter = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            RightStick = new StickPosition(0.1f, 0.1f)
        };
        var outCenter = service.Process(inputCenter, 140);
        Assert.False(outCenter.HasButton(GamepadButtons.SwipeUp));
        Assert.False(outCenter.HasButton(GamepadButtons.SwipeDown));
        Assert.False(outCenter.HasButton(GamepadButtons.SwipeLeft));
        Assert.False(outCenter.HasButton(GamepadButtons.SwipeRight));
    }

    [Fact]
    public void DualShock4TouchpadSynthesizer_SynthesizesExactCoordinates()
    {
        var synth = new DualShock4TouchpadSynthesizer();

        // Plan 03 §3.1 exact coordinates:
        // TapLeft: (480, 471)
        var leftFrames = synth.Synthesize(TouchpadGesture.TapLeft);
        Assert.Equal(480, leftFrames[0].Touch0.X);
        Assert.Equal(471, leftFrames[0].Touch0.Y);

        // TapRight: (1440, 471)
        var rightFrames = synth.Synthesize(TouchpadGesture.TapRight);
        Assert.Equal(1440, rightFrames[0].Touch0.X);
        Assert.Equal(471, rightFrames[0].Touch0.Y);

        // TapCenter: (960, 471)
        var centerFrames = synth.Synthesize(TouchpadGesture.TapCenter);
        Assert.Equal(960, centerFrames[0].Touch0.X);
        Assert.Equal(471, centerFrames[0].Touch0.Y);

        // TapTop: (960, 235)
        var topFrames = synth.Synthesize(TouchpadGesture.TapTop);
        Assert.Equal(960, topFrames[0].Touch0.X);
        Assert.Equal(235, topFrames[0].Touch0.Y);

        // TapBottom: (960, 706)
        var bottomFrames = synth.Synthesize(TouchpadGesture.TapBottom);
        Assert.Equal(960, bottomFrames[0].Touch0.X);
        Assert.Equal(706, bottomFrames[0].Touch0.Y);

        // SwipeRight: starts at 100, moves +640 -> ends at 740
        var swipeFrames = synth.Synthesize(TouchpadGesture.SwipeRight);
        Assert.Equal(100, swipeFrames[0].Touch0.X);
        Assert.Equal(471, swipeFrames[0].Touch0.Y);
        var activeSwipe = swipeFrames.Where(f => f.Touch0.IsActive).ToList();
        Assert.Equal(740, activeSwipe[^1].Touch0.X);
    }

    // =========================================================================
    // M3.5: DualShock 4 63-Byte Raw Motion Report Layout Tests
    // =========================================================================

    [Fact]
    public void DualShock4ReportFormatter_WriteMotion_EncodesScaledValuesAtCorrectOffsets()
    {
        var report = new byte[63];
        var motion = new MotionState(
            Pitch: 0, Roll: 0, Yaw: 0,
            AccelX: 1.0f,  // 8192 LSB
            AccelY: -0.5f, // -4096 LSB
            AccelZ: 0.25f, // 2048 LSB
            GyroX: 10.0f,  // 160 LSB
            GyroY: -20.0f, // -320 LSB
            GyroZ: 30.0f); // 480 LSB

        DualShock4ReportFormatter.WriteMotion(report, motion);

        // Offsets 12..17: Gyro Pitch (X), Yaw (Y), Roll (Z)
        Assert.Equal(160, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(12, 2)));
        Assert.Equal(-320, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(14, 2)));
        Assert.Equal(480, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(16, 2)));

        // Offsets 18..23: Accel X, Y, Z
        Assert.Equal(8192, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(18, 2)));
        Assert.Equal(-4096, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(20, 2)));
        Assert.Equal(2048, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(22, 2)));
    }

    [Fact]
    public void DualShock4ReportFormatter_WriteMotion_ClampsAndHandlesNaNGracefully()
    {
        var report = new byte[63];
        var extremeMotion = new MotionState(
            Pitch: 0, Roll: 0, Yaw: 0,
            AccelX: 1000f, // Beyond short.MaxValue -> clamped
            AccelY: -1000f,
            AccelZ: float.NaN, // Non-finite -> 0
            GyroX: float.PositiveInfinity,
            GyroY: -50000f,
            GyroZ: 0f);

        DualShock4ReportFormatter.WriteMotion(report, extremeMotion);

        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(18, 2)));
        Assert.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(20, 2)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(22, 2)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(12, 2)));
    }

    [Fact]
    public void DualShock4ReportFormatter_WriteMotion_ThrowsWhenReportTooShort()
    {
        var shortReport = new byte[23];
        var motion = new MotionState();

        Assert.Throws<ArgumentException>(() => DualShock4ReportFormatter.WriteMotion(shortReport, motion));
    }

    // =========================================================================
    // M3.6: DSU Protocol & HidHide Service Isolation Tests
    // =========================================================================

    [Fact]
    public async Task DsuMotionServer_CanStartAndDisposeCleanly()
    {
        // Use custom high port to avoid local port collisions in CI
        var server = new DsuMotionServer(26768);
        try
        {
            server.Start();
            Assert.True(server.IsRunning);
            Assert.Equal(26768, server.Port);

            // Updating state should not throw
            server.UpdateState(new GamepadState
            {
                Buttons = GamepadButtons.A,
                GyroVelX = 12.5f,
                AccelZ = 1.0f
            });
        }
        finally
        {
            await server.DisposeAsync();
            Assert.False(server.IsRunning);
        }
    }

    [Fact]
    public void HidHideService_PathTranslation_And_InMemoryFallback()
    {
        using var hider = new HidHideService();

        // In-memory application whitelist
        const string testApp = @"C:\Program Files\Shunt\Shunt.Engine.exe";
        bool added = hider.AddApplicationWhitelist(testApp);
        Assert.True(added);

        var list = hider.GetWhitelistedApplications();
        Assert.Contains(list, path => path.Equals(testApp, StringComparison.OrdinalIgnoreCase));

        bool removed = hider.RemoveApplicationWhitelist(testApp);
        Assert.True(removed);

        // In-memory blocked device instance path
        const string testDevice = @"USB\VID_3537&PID_1004\123456";
        bool blocked = hider.BlockDevice(testDevice);
        Assert.True(blocked);

        var blockedList = hider.GetBlockedDevices();
        Assert.Contains(blockedList, dev => dev.Equals(testDevice, StringComparison.OrdinalIgnoreCase));

        bool unblocked = hider.UnblockDevice(testDevice);
        Assert.True(unblocked);
    }

    [Fact]
    public void HardwareImuDiscovery_Enumerate_ExecutesWithoutException()
    {
        var devices = HardwareImuDiscovery.Enumerate();
        Assert.NotNull(devices);
        // On dev/CI machines without physical GameSir plugged in, devices is an empty or populated list
        foreach (var dev in devices)
        {
            Assert.False(string.IsNullOrWhiteSpace(dev.DevicePath));
            Assert.NotNull(dev.Profile);
            Assert.Equal(0x3537, dev.Profile.VendorId);
            Assert.Equal(0x1004, dev.Profile.ProductId);
        }
    }

    [Fact]
    public void HardwareImuDevice_Record_InitializesAndReadsCorrectly()
    {
        var profile = ImuDeviceProfile.KnownProfiles[0];
        var dev = new HardwareImuDevice(@"\\?\hid#vid_3537&pid_1004#12345", profile);

        Assert.Equal(@"\\?\hid#vid_3537&pid_1004#12345", dev.DevicePath);
        Assert.Equal(profile, dev.Profile);
        Assert.Equal("GameSir G7 Pro / 8K", dev.Profile.Name);
    }

    [Fact]
    public void GameSirG7Pro_KnownProfiles_IncludesAllModes()
    {
        var profile1004 = ImuDeviceProfile.Find(0x3537, 0x1004);
        var profile100A = ImuDeviceProfile.Find(0x3537, 0x100A);
        var profile1010 = ImuDeviceProfile.Find(0x3537, 0x1010);

        Assert.NotNull(profile1004);
        Assert.NotNull(profile100A);
        Assert.NotNull(profile1010);
        Assert.Equal("GameSir G7 Pro", profile100A.Name);
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_3537&pid_100a&mi_01&col01#8&d45f578&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", false)] // Keyboard
    [InlineData(@"\\?\hid#vid_3537&pid_100a&mi_01&col02#8&d45f578&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}", false)] // Consumer control
    [InlineData(@"\\?\hid#vid_3537&pid_100a&mi_01&col03#8&d45f578&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", false)] // Mouse
    [InlineData(@"\\?\hid#vid_3537&pid_100a&ig_01#9&226cc2c6&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", false)]      // Standard gamepad
    [InlineData(@"\\?\hid#vid_3537&pid_100a&mi_01&col04#8&d45f578&0&0003#{4d1e55b2-f16f-11cf-88cb-001111000030}", true)]  // Motion collection
    public void HidNative_IsSupportedImuCollectionPath_FiltersCompositeEndpoints(string path, bool expectedValid)
    {
        Assert.Equal(expectedValid, HidNative.IsSupportedImuCollectionPath(path));
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x12)]
    public void GameSirG7Pro_TryParse_SupportsReportIds(byte reportId)
    {
        var format = ImuReportFormat.GameSirG7Pro;
        var packet = new byte[64];
        packet[0] = reportId;
        packet[1] = 42; // Sequence
        // Accel Z = 8192 (1.0G)
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(8, 2), 8192);
        // Gyro X = 160 (10 deg/s)
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(10, 2), 160);

        bool parsed = format.TryParse(packet, 1000, out var sample);

        Assert.True(parsed);
        Assert.Equal(42, sample.Sequence);
        Assert.Equal(1.0f, sample.AccelZ, 0.01f);
        Assert.Equal(10.0f, sample.GyroX, 0.01f);
    }

    [Fact]
    public void ShuntService_Back_LeftStick_ActivatesSimulatedGyroTilt_AndCentersStick()
    {
        var service = new XboxToDualShock4ShuntService();
        var capturedAction = string.Empty;
        service.TouchpadActionChanged += action => capturedAction = action;

        // Hold Back + deflect Left Stick to the right (Roll tilt)
        var inputRoll = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            LeftStick = new StickPosition(0.8f, 0f)
        };

        var outputRoll = service.Process(inputRoll, 100);

        Assert.Equal("Left stick gyro tilt", capturedAction);
        Assert.Equal(StickPosition.Center, outputRoll.LeftStick); // Output Left Stick centered to prevent character walk
        Assert.True(outputRoll.GyroVelZ > 0f, $"GyroVelZ was {outputRoll.GyroVelZ}"); // Positive roll velocity
        Assert.True(outputRoll.GyroVelY > 0f, $"GyroVelY was {outputRoll.GyroVelY}"); // Positive yaw velocity
        Assert.True(outputRoll.AccelX > 0f, $"AccelX was {outputRoll.AccelX}");       // Positive lateral gravity
        Assert.True(outputRoll.AccelZ > 0f, $"AccelZ was {outputRoll.AccelZ}");

        // Now deflect Left Stick upwards (Pitch tilt)
        var inputPitch = new GamepadState
        {
            Buttons = GamepadButtons.Back,
            LeftStick = new StickPosition(0f, 0.8f)
        };

        var outputPitch = service.Process(inputPitch, 150);

        Assert.Equal(StickPosition.Center, outputPitch.LeftStick);
        Assert.True(outputPitch.GyroVelX > 0f, $"GyroVelX was {outputPitch.GyroVelX}"); // Positive pitch velocity
        Assert.True(outputPitch.AccelY < 0f, $"AccelY was {outputPitch.AccelY}");       // Negative Ay for upward pitch tilt

        // Release Back: because Back was partnered with Left Stick tilt, center tap is NOT triggered!
        var release = service.Process(new GamepadState(), 200);
        Assert.False(release.HasButton(GamepadButtons.TouchpadTap), "Center tap should not be triggered after stick tilt");
    }

    [Fact]
    public void ShuntService_VirtualGyro_WithoutHardwareImu_FollowsLeftStick_WhenGyroEnabled()
    {
        var service = new XboxToDualShock4ShuntService();

        // Toggle gyro ON via L3 + R3 chord
        var chord = new GamepadState { Buttons = GamepadButtons.LeftThumb | GamepadButtons.RightThumb };
        service.Process(chord, 100);
        Assert.True(service.IsGyroEnabled);

        // Standard controller without hardware IMU (imuSample = null)
        var stickInput = new GamepadState
        {
            LeftStick = new StickPosition(0.6f, 0.4f)
        };

        var output = service.Process(stickInput, 200, imuSample: null);

        Assert.True(output.GyroVelX > 0f, "GyroVelX should be active");
        Assert.True(output.GyroVelY > 0f, "GyroVelY should be active");
        Assert.True(output.GyroVelZ > 0f, "GyroVelZ should be active");
        Assert.True(output.AccelX > 0f, "AccelX should reflect roll");
    }

    [Fact]
    public void ShuntService_VirtualGyro_WithoutHardwareImu_FollowsRightStick_WhenGameSirNexusMapped()
    {
        var service = new XboxToDualShock4ShuntService();
        service.SetGyroEnabled(true);
        Assert.True(service.IsGyroEnabled);

        // When GameSir Nexus maps gyro to right stick deflection:
        var rightStickInput = new GamepadState
        {
            RightStick = new StickPosition(0.5f, -0.7f)
        };

        var output = service.Process(rightStickInput, 200, imuSample: null);

        Assert.True(output.GyroVelX < 0f, "GyroVelX should reflect downward pitch");
        Assert.True(output.GyroVelY > 0f, "GyroVelY should reflect positive roll");
        Assert.True(output.AccelY > 0f, "AccelY should reflect pitch angle");
    }

    [Fact]
    public async Task ShuntEngineService_StartShuntMode_AutoStartsDsuServer_AndEnablesGyroImmediately()
    {
        var stubGamepadManager = new StubVirtualGamepadManager();
        var stubDiscovery = new StubHardwareDiscoveryService();
        var service = new ShuntEngineService(stubGamepadManager, stubDiscovery, null);

        Assert.False(service.IsShuntModeEnabled);
        Assert.False(service.IsDsuServerEnabled);

        var started = await service.StartShuntModeAsync(null, GyroActivatorMode.Toggle);

        Assert.True(started);
        Assert.True(service.IsShuntModeEnabled);
        await service.StopShuntModeAsync();
        Assert.False(service.IsShuntModeEnabled);
    }

    [Fact]
    public void GameSirG7ProChipWriter_BuildProfilePayload_ConfiguresOffsetsCorrectly()
    {
        var payloadLeft = GameSirG7ProChipWriter.BuildProfilePayload(GameSirGyroTargetStick.LeftStick, alwaysActive: true);
        Assert.Equal(GameSirG7ProChipWriter.ProfileLength, payloadLeft.Length);
        Assert.Equal(0x07, payloadLeft[GameSirG7ProChipWriter.OffsetActivationBehavior]); // Always active
        Assert.Equal(0x01, payloadLeft[GameSirG7ProChipWriter.OffsetOutputStick]);        // Left stick

        var payloadRight = GameSirG7ProChipWriter.BuildProfilePayload(GameSirGyroTargetStick.RightStick, alwaysActive: false);
        Assert.Equal(0x00, payloadRight[GameSirG7ProChipWriter.OffsetActivationBehavior]); // Disabled
        Assert.Equal(0x03, payloadRight[GameSirG7ProChipWriter.OffsetOutputStick]);        // Right stick
    }

    [Fact]
    public void GameSirG7ProChipWriter_BuildWritePackets_SegmentsInto8ExactChunks()
    {
        var packets = GameSirG7ProChipWriter.BuildWritePackets(profileIndex: 1, GameSirGyroTargetStick.LeftStick, alwaysActive: true);
        Assert.Equal(8, packets.Count);

        // Verify headers: [0x03 (Command), 0x01 (Profile1), offsetHigh, offsetLow, length]
        Assert.Equal(0x03, packets[0][0]);
        Assert.Equal(0x01, packets[0][1]);
        Assert.Equal(0x00, packets[0][2]);
        Assert.Equal(0x30, packets[0][3]); // Offset 0x0030 (48)
        Assert.Equal(55, packets[0][4]);   // Length 55
        Assert.Equal(60, packets[0].Length); // 5 header + 55 payload

        // Verify last chunk: offset 0x01B1 (433), length 47
        Assert.Equal(0x03, packets[7][0]);
        Assert.Equal(0x01, packets[7][1]);
        Assert.Equal(0x01, packets[7][2]);
        Assert.Equal(0xB1, packets[7][3]);
        Assert.Equal(47, packets[7][4]);
        Assert.Equal(52, packets[7].Length); // 5 header + 47 payload
    }

    [Fact]
    public async Task GameSirG7ProChipWriter_FlashProfileToChipAsync_ReportsProgress()
    {
        int lastProgress = 0;
        var progress = new Progress<int>(pct => lastProgress = pct);

        var result = await GameSirG7ProChipWriter.FlashProfileToChipAsync(GameSirGyroTargetStick.LeftStick, alwaysActive: true, progress: progress);

        Assert.True(result.Success);
        Assert.Equal(8, result.ChunksWritten);
        Assert.True(lastProgress >= 90);
    }

    [Fact]
    public void XboxToDualShock4ShuntService_LeftStickGyroActive_CentersLeftStick_ToPreventCharacterWalking()
    {
        var service = new XboxToDualShock4ShuntService();
        service.SetGyroEnabled(true);
        Assert.True(service.IsGyroEnabled);

        // When physical gyro tilt moves the left stick on the GameSir controller:
        var inputWithLeftStickTilt = new GamepadState
        {
            LeftStick = new StickPosition(0.45f, -0.6f)
        };

        var output = service.Process(inputWithLeftStickTilt, 100, imuSample: null);

        // Left stick MUST be centered in the output report so character does not walk
        Assert.Equal(StickPosition.Center, output.LeftStick);

        // Gyro velocity and accelerometer MUST reflect the motion
        Assert.True(output.GyroVelX < 0f, "Pitch gyro velocity should be negative");
        Assert.True(output.GyroVelY > 0f, "Roll gyro velocity should be positive");
    }

    [Fact]
    public void XboxToDualShock4ShuntService_GyroDisabled_PreservesLeftStickForWalking()
    {
        var service = new XboxToDualShock4ShuntService();
        service.SetGyroEnabled(false);
        Assert.False(service.IsGyroEnabled);

        // Normal player walking with left stick:
        var walkingInput = new GamepadState
        {
            LeftStick = new StickPosition(0.45f, -0.6f)
        };

        var output = service.Process(walkingInput, 100, imuSample: null);

        // Left stick MUST NOT be suppressed when gyro is disabled:
        Assert.Equal(0.45f, output.LeftStick.X);
        Assert.Equal(-0.6f, output.LeftStick.Y);

        // Gyro velocity MUST be zero when disabled:
        Assert.Equal(0f, output.GyroVelX);
        Assert.Equal(0f, output.GyroVelY);
    }

    [Fact]
    public async Task GameSirG7ProChipWriter_FlashProfileToChipAsync_AlwaysActiveFalse_WritesDisabledOffset()
    {
        var payloadDisabled = GameSirG7ProChipWriter.BuildProfilePayload(GameSirGyroTargetStick.LeftStick, alwaysActive: false);
        Assert.Equal(0x00, payloadDisabled[GameSirG7ProChipWriter.OffsetActivationBehavior]);

        var result = await GameSirG7ProChipWriter.FlashProfileToChipAsync(GameSirGyroTargetStick.LeftStick, alwaysActive: false);
        Assert.True(result.Success);
        Assert.Equal(8, result.ChunksWritten);
    }

    [Fact]
    public async Task ShuntEngineService_L3R3Chord_TogglesGyroAndTriggersChipSync()
    {
        var stubGamepadManager = new StubVirtualGamepadManager();
        var stubDiscovery = new StubHardwareDiscoveryService();
        var service = new ShuntEngineService(stubGamepadManager, stubDiscovery, null);

        Assert.True(service.AutoSyncChipOnGyroToggle);

        var tcs = new TaskCompletionSource<bool>();
        service.ChipSyncStatusChanged += result => tcs.TrySetResult(true);

        service.EnableShuntMode();

        // Initially gyro is enabled on Shunt start
        Assert.True(service.IsShuntGyroEnabled);

        // Toggle gyro off (simulating L3+R3 toggle off) to verify event pipeline and chip sync
        service.SetShuntGyroEnabled(false);
        Assert.False(service.IsShuntGyroEnabled);

        // Await background chip sync event (packet transmission takes ~200ms)
        var finishedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, finishedTask);
        Assert.True(await tcs.Task, "ChipSyncStatusChanged should fire upon gyro toggle");
    }
}

