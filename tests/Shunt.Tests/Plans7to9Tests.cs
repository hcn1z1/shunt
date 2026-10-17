using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using Shunt.Core.Scancodes;
using Shunt.Emulation;
using Shunt.Emulation.Targets;
using Shunt.Engine;
using Shunt.Engine.Hardware;
using Xunit;

namespace Shunt.Tests;

public class VirtualControllerTargetsTests
{
    [Fact]
    public void Xbox360Report_EncodesAllStandardButtonsAndAxes()
    {
        var state = new VirtualGamepadState
        {
            Buttons = GamepadDigitalButtons.A | GamepadDigitalButtons.DpadUp |
                      GamepadDigitalButtons.LeftBumper | GamepadDigitalButtons.Guide,
            LeftTrigger = 0.5f,
            RightTrigger = 1f,
            LeftStick = new StickPosition(1f, -1f)
        };

        var report = new Xbox360Target().BuildInputReport(state);

        Assert.Equal(12, report.Length);
        ushort buttons = (ushort)(report[0] | (report[1] << 8));
        Assert.True((buttons & 0x0001) != 0, "DpadUp bit");       // DPAD_UP
        Assert.True((buttons & 0x0100) != 0, "LeftBumper bit");   // LB
        Assert.True((buttons & 0x0400) != 0, "Guide bit");        // Guide
        Assert.True((buttons & 0x1000) != 0, "A bit");            // A
        Assert.Equal(127, report[2]);                              // LeftTrigger ~ 0.5 * 255
        Assert.Equal(255, report[3]);                              // RightTrigger full
        Assert.Equal(32767, BitConverter.ToInt16(report, 4));      // LeftStickX full right
        Assert.Equal(-32767, BitConverter.ToInt16(report, 6));     // LeftStickY full down
    }

    [Fact]
    public void DualShock4Report_PackTouchAndImu()
    {
        var state = new VirtualGamepadState
        {
            Buttons = GamepadDigitalButtons.A | GamepadDigitalButtons.Guide,
            Touch0 = new TouchPoint(true, 1, 960, 500),
            Motion = new MotionState(0f, 0f, 0f, 0f, 0f, 1f, 10f, 0f, 0f)
        };

        var report = new DualShock4Target().BuildInputReport(state, reportCounter: 3);

        Assert.Equal(0x01, report[0]);
        Assert.True((report[1] & (1 << 4)) != 0, "Cross bit in byte 1");
        Assert.True((report[3] & (1 << 4)) != 0, "PS button bit");
        Assert.Equal(3 & 0x0F, report[3] & 0x0F);                  // report counter
        // Touch slot 0 active with 12-bit X
        Assert.Equal(1, report[32 + 1] & 0x7F);
        Assert.Equal(960 & 0xFF, report[32 + 2]);
        // IMU: accel Z = 1G * 8192 big-endian
        Assert.Equal(0x20, report[25]);
        Assert.Equal(0x00, report[26]);
        // Gyro X = 10 deg/s * 16 = 160
        Assert.Equal(0, report[15]);
        Assert.Equal(160, report[16]);
    }

    [Fact]
    public void DualShock3Report_PressureSensitiveButtons()
    {
        var state = new VirtualGamepadState
        {
            Buttons = GamepadDigitalButtons.A,
            PressureCross = 0.5f,
            PressureTriangle = 1f
        };

        var report = new DualShock3Target().BuildInputReport(state);

        // Cross digital bit set + analog pressure ~127
        Assert.True((report[5] & (1 << 2)) != 0, "Cross digital bit");
        Assert.Equal(127, report[24]);  // PressureCross byte
        Assert.Equal(255, report[22]);  // PressureTriangle full
    }

    [Fact]
    public void SwitchProReport_IsFull0x30LayoutWithThreeImuSamples()
    {
        var state = new VirtualGamepadState
        {
            Buttons = GamepadDigitalButtons.A | GamepadDigitalButtons.Guide | GamepadDigitalButtons.Capture,
            LeftStick = new StickPosition(1f, 0f)
        };

        var report = new SwitchProTarget().BuildInputReport(state, reportCounter: 7);

        Assert.Equal(SwitchProTarget.ReportLength, report.Length);
        Assert.Equal(0x30, report[0]);
        Assert.Equal(7, report[1] & 0xFF);              // timer
        Assert.True((report[3] & (1 << 3)) != 0, "A bit");
        Assert.True((report[4] & (1 << 4)) != 0, "Home bit");
        Assert.True((report[4] & (1 << 5)) != 0, "Capture bit");
        // Left stick full right = center + 1462 = 3510 (12-bit)
        int lx = report[6] | ((report[7] & 0x0F) << 8);
        Assert.Equal(3510, lx);
    }

    [Fact]
    public void XboxOneReport_ExtendedPaddleBits()
    {
        var state = new VirtualGamepadState
        {
            Buttons = GamepadDigitalButtons.PaddleP1 | GamepadDigitalButtons.PaddleP4 | GamepadDigitalButtons.Capture
        };

        var report = new XboxOneTarget().BuildInputReport(state);

        Assert.True((report[2] & 0x01) != 0, "Paddle P1 in third button byte");
        Assert.True((report[2] & 0x08) != 0, "Paddle P4 in third button byte");
        Assert.True((report[1] & (1 << 3)) != 0, "Share button");
    }

    [Fact]
    public void Factory_CreatesTargetForEveryGamepadFamily()
    {
        Assert.IsType<Xbox360Target>(VirtualTargetFactory.Create(VirtualControllerTarget.The360));
        Assert.IsType<XboxOneTarget>(VirtualTargetFactory.Create(VirtualControllerTarget.XboxOneBT));
        Assert.IsType<DualShock4Target>(VirtualTargetFactory.Create(VirtualControllerTarget.Ds4));
        Assert.IsType<DualShock3Target>(VirtualTargetFactory.Create(VirtualControllerTarget.Ds3));
        Assert.IsType<SwitchProTarget>(VirtualTargetFactory.Create(VirtualControllerTarget.NintendoSwitchPro));
        Assert.Throws<NotSupportedException>(() => VirtualTargetFactory.Create(VirtualControllerTarget.KeyboardAndMouse));
    }

    [Fact]
    public void StateBridge_RoundTripsPipelineButtons()
    {
        var pipelineState = new GamepadState
        {
            Buttons = GamepadButtons.A | GamepadButtons.LeftShoulder | GamepadButtons.DpadLeft,
            LeftTrigger = 0.75f,
            GyroVelY = 42f
        };

        var virtualState = VirtualGamepadStateBridge.ToVirtual(pipelineState);
        Assert.True(virtualState.HasButton(GamepadDigitalButtons.A));
        Assert.True(virtualState.HasButton(GamepadDigitalButtons.LeftBumper));
        Assert.True(virtualState.HasButton(GamepadDigitalButtons.DpadLeft));
        Assert.Equal(0.75f, virtualState.LeftTrigger);
        Assert.Equal(42f, virtualState.Motion.GyroY);

        var back = VirtualGamepadStateBridge.ToPipeline(virtualState);
        Assert.True(back.HasButton(GamepadButtons.A));
        Assert.True(back.HasButton(GamepadButtons.LeftShoulder));
        Assert.True(back.HasButton(GamepadButtons.DpadLeft));
    }
}

public class TouchpadSynthesizerTests
{
    [Fact]
    public void SwipeUp_InterpolatesEightFramesThenLifts()
    {
        var synth = new DualShock4TouchpadSynthesizer();
        var frames = synth.Synthesize(TouchpadGesture.SwipeUp);

        Assert.True(frames.Count >= 9);                            // 8 movement frames + release
        var moving = frames.Take(frames.Count - 1).ToList();
        Assert.All(moving, f => Assert.True(f.Touch0.IsActive));
        Assert.False(frames[^1].Touch0.IsActive);                  // final lift
        // Y strictly decreases (upward on the touch surface)
        for (int i = 1; i < moving.Count; i++)
            Assert.True(moving[i].Touch0.Y < moving[i - 1].Touch0.Y);
    }

    [Fact]
    public void TapCenter_HoldsThenReleases()
    {
        var synth = new DualShock4TouchpadSynthesizer();
        var frames = synth.Synthesize(TouchpadGesture.TapCenter);

        Assert.True(frames.Count >= 2);
        Assert.All(frames.Take(frames.Count - 1), f =>
        {
            Assert.True(f.Touch0.IsActive);
            Assert.True(f.Touch0.X == 960 || f.Touch0.X == DualShock4TouchpadSynthesizer.MaxX / 2);
        });
        Assert.False(frames[^1].Touch0.IsActive);
    }

    [Fact]
    public void PinchZoomIn_TwoFingersMoveApart()
    {
        var synth = new DualShock4TouchpadSynthesizer();
        var frames = synth.Synthesize(TouchpadGesture.PinchZoomIn);
        var moving = frames.Take(frames.Count - 1).ToList();

        Assert.All(moving, f => Assert.True(f.Touch1.IsActive));
        int firstSpan = moving[0].Touch1.X - moving[0].Touch0.X;
        int lastSpan = moving[^1].Touch1.X - moving[^1].Touch0.X;
        Assert.True(lastSpan > firstSpan, "fingers must separate");
    }

    [Fact]
    public void PinchZoomOut_TwoFingersConverge()
    {
        var synth = new DualShock4TouchpadSynthesizer();
        var frames = synth.Synthesize(TouchpadGesture.PinchZoomOut);
        var moving = frames.Take(frames.Count - 1).ToList();

        int firstSpan = moving[0].Touch1.X - moving[0].Touch0.X;
        int lastSpan = moving[^1].Touch1.X - moving[^1].Touch0.X;
        Assert.True(lastSpan < firstSpan, "fingers must converge");
    }

    [Fact]
    public void TwoFingerSwipe_HasBothContacts()
    {
        var synth = new DualShock4TouchpadSynthesizer();
        var frames = synth.Synthesize(TouchpadGesture.TwoFingerSwipeLeft);
        var moving = frames.Take(frames.Count - 1).ToList();

        Assert.All(moving, f => Assert.True(f.Touch0.IsActive && f.Touch1.IsActive));
        for (int i = 1; i < moving.Count; i++)
            Assert.True(moving[i].Touch0.X < moving[i - 1].Touch0.X);
    }
}

public class VirtualMotionSynthesizerTests
{
    [Fact]
    public void FeedMouseDelta_MovesYawAndPitch()
    {
        var synth = new VirtualMotionSynthesizer();

        var motion = synth.FeedMouseDelta(100, 50, deltaTimeSeconds: 0.1f);

        Assert.True(motion.Yaw > 0f);
        Assert.True(motion.Pitch > 0f);
        Assert.Equal(0f, motion.Roll);
    }

    [Fact]
    public void FeedGyroVelocity_IntegratesOrientation()
    {
        var synth = new VirtualMotionSynthesizer();

        var first = synth.FeedGyroVelocity(gyroX: 0f, gyroY: 90f, gyroZ: 0f, deltaTimeSeconds: 1f);

        // Orientation integrates the full angular displacement in one step
        Assert.True(MathF.Abs(first.Yaw - 90f) < 1f);
        // Angular velocity is low-pass filtered toward the input (halfway on the first frame)
        Assert.Equal(45f, first.GyroY);

        var second = synth.FeedGyroVelocity(0f, 90f, 0f, deltaTimeSeconds: 1f);
        Assert.True(second.GyroY > first.GyroY);
        Assert.True(MathF.Abs(second.Yaw - 180f) < 1f);
    }

    [Fact]
    public void Reset_ZeroesAllAxes()
    {
        var synth = new VirtualMotionSynthesizer();
        synth.FeedMouseDelta(500, 500, 1f);
        synth.Reset();

        var idle = synth.FeedMouseDelta(0, 0, 0.1f);
        Assert.Equal(0f, idle.Pitch);
        Assert.Equal(0f, idle.Roll);
        Assert.Equal(0f, idle.Yaw);
    }
}

public class KeyScanCodeTableTests
{
    [Fact]
    public void Table_RegistersAllKeyboardFamilies()
    {
        Assert.Equal(118, KeyScanCodeTable.GetCategory(KeyScanCodeCategory.KeyboardStandart).Count);
        Assert.Equal(18, KeyScanCodeTable.GetCategory(KeyScanCodeCategory.KeyboardConsumer).Count);
        Assert.Equal(15, KeyScanCodeTable.GetCategory(KeyScanCodeCategory.MouseAnalog).Count
                       + KeyScanCodeTable.GetCategory(KeyScanCodeCategory.MouseDigital).Count);
        Assert.Equal(57, KeyScanCodeTable.GetCategory(KeyScanCodeCategory.Gamepad).Count
                       + KeyScanCodeTable.GetCategory(KeyScanCodeCategory.VirtualGamepad).Count);
        Assert.Equal(18, KeyScanCodeTable.GetCategory(KeyScanCodeCategory.RewasdUserCommand).Count);
    }

    [Theory]
    [InlineData(1, "DIK_ESCAPE", 0x1B)]       // Esc
    [InlineData(28, "DIK_RETURN", 0x0D)]      // Enter
    [InlineData(57, "DIK_SPACE", 0x20)]       // Space
    [InlineData(87, "DIK_F11", 0x7A)]
    [InlineData(156, "DIK_NUMPADENTER", 0x0D)]
    [InlineData(183, "DIK_SYSRQ", 0x00)]      // Print Screen has no VK
    [InlineData(219, "DIK_LWIN", 0x5B)]
    public void Table_ResolvesStandardKeysByIndex(int index, string expectedName, ushort vk)
    {
        var entry = KeyScanCodeTable.GetByIndex(index);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(vk, entry.VirtualKey);
    }

    [Theory]
    [InlineData("BTN_A", 256)]
    [InlineData("BTN_CAPTURE", 267)]
    [InlineData("BTN_TOUCHPAD_ZOOM_IN", 287)]
    [InlineData("BTN_GYRO_TILT_UP", 304)]
    [InlineData("MOUSE_BTN_1", 248)]
    [InlineData("DIK_MUTE", 160)]
    public void Table_ResolvesByName(string name, int expectedIndex)
    {
        var entry = KeyScanCodeTable.GetByName(name);
        Assert.NotNull(entry);
        Assert.Equal(expectedIndex, entry.Index);
    }

    [Fact]
    public void Table_NoDuplicateIndicesInScancodeSpace()
    {
        // RewasdUserCommand ids intentionally share the 1-18 range with scancodes; each space must be unique.
        var indices = KeyScanCodeTable.All
            .Where(e => e.Category != KeyScanCodeCategory.RewasdUserCommand)
            .Select(e => e.Index).ToList();
        Assert.Equal(indices.Count, indices.Distinct().Count());

        var commandIds = KeyScanCodeTable.GetCategory(KeyScanCodeCategory.RewasdUserCommand)
            .Select(e => e.Index).ToList();
        Assert.Equal(commandIds.Count, commandIds.Distinct().Count());
    }
}

public class ActivatorEvaluatorTests
{
    private static long Ms(int milliseconds) => (long)(milliseconds * (System.Diagnostics.Stopwatch.Frequency / 1000d));

    [Fact]
    public void SingleActivator_ActiveWhileHeld()
    {
        var state = new ActivatorEvaluationState();
        long now = 0;

        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Single, state, true, now));
        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Single, state, true, Ms(100)));
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Single, state, false, Ms(110)));
    }

    [Fact]
    public void LongActivator_FiresAfterHoldThreshold()
    {
        var state = new ActivatorEvaluationState();
        long start = 0;

        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Long, state, true, start, holdThresholdMs: 350));
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Long, state, true, Ms(300), 350));
        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Long, state, true, Ms(400), 350));
    }

    [Fact]
    public void StartActivator_FiresOnlyOnRisingEdge()
    {
        var state = new ActivatorEvaluationState();

        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Start, state, true, 0));
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Start, state, true, Ms(10)));
    }

    [Fact]
    public void ReleaseActivator_FiresOnlyOnFallingEdge()
    {
        var state = new ActivatorEvaluationState();

        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Release, state, true, 0));
        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Release, state, false, Ms(10)));
    }

    [Fact]
    public void DoubleActivator_RequiresTwoTapsInWindow()
    {
        var state = new ActivatorEvaluationState();

        // First tap-release: no fire
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, true, 0));
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, false, Ms(50)));
        // Second tap-release within 150ms window: fires
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, true, Ms(100)));
        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, false, Ms(150)));
    }

    [Fact]
    public void TripleActivator_RequiresThreeTaps()
    {
        var state = new ActivatorEvaluationState();

        for (int tap = 0; tap < 2; tap++)
        {
            Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Triple, state, true, Ms(tap * 100)));
            Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Triple, state, false, Ms(tap * 100 + 50)));
        }
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Triple, state, true, Ms(200)));
        Assert.True(ActivatorEvaluator.Evaluate(ActivatorType.Triple, state, false, Ms(250)));
    }

    [Fact]
    public void DoubleActivator_TapsOutsideWindowDoNotAccumulate()
    {
        var state = new ActivatorEvaluationState();

        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, true, 0));
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, false, Ms(50)));
        // Second tap 500ms later: window expired, counter restarts
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, true, Ms(500)));
        Assert.False(ActivatorEvaluator.Evaluate(ActivatorType.Double, state, false, Ms(550)));
    }
}

public class CommandDispatcherTests
{
    [Theory]
    [InlineData("TurnRemapOff", RewasdUserCommand.TurnRemapOff)]
    [InlineData("AntibossMode", RewasdUserCommand.AntibossMode)]
    [InlineData("VirtualGyroReset", RewasdUserCommand.VirtualGyroReset)]
    [InlineData("11", RewasdUserCommand.Screenshot)]
    [InlineData("4", RewasdUserCommand.GyroSwitch)]
    public void Resolve_AcceptsNamesAndIds(string input, RewasdUserCommand expected)
    {
        Assert.Equal(expected, CommandDispatcher.Resolve(input));
    }

    [Fact]
    public void Resolve_RejectsUnknownCommands()
    {
        Assert.Null(CommandDispatcher.Resolve("NotACommand"));
        Assert.Null(CommandDispatcher.Resolve("999"));
        Assert.Null(CommandDispatcher.Resolve(null));
    }

    [Fact]
    public void GyroCommands_ManipulatePipelineContext()
    {
        using var engine = new ShuntEngineService(new Shunt.Emulation.StubVirtualGamepadManager());
        var dispatcher = new CommandDispatcher(engine);

        var profile = new Shunt.Profiles.Models.RewasdProfileDocument
        {
            Devices = new Shunt.Profiles.Models.ProfileDevices
            {
                Virtual = new Shunt.Profiles.Models.VirtualDevices
                {
                    Gamepads = [new Shunt.Profiles.Models.VirtualGamepadEntry { Id = 1, Type = "360" }]
                }
            }
        };
        try
        {
            engine.Start(profile);
            var gyro = engine.Pipeline!.Context.GyroSettings;

            Assert.Equal(GyroHorizontalAxis.Yaw, gyro.HorizontalAxis);
            Assert.True(dispatcher.Execute(RewasdUserCommand.GyroSwitch));
            Assert.Equal(GyroHorizontalAxis.Roll, gyro.HorizontalAxis);

            Assert.True(dispatcher.Execute(RewasdUserCommand.TiltReset));
            Assert.True(gyro.IsCalibrated);

            Assert.True(dispatcher.Execute(RewasdUserCommand.GyroTiltTurnOff));
            Assert.False(engine.Pipeline.Context.IsGyroToggleActive);
            Assert.True(dispatcher.Execute(RewasdUserCommand.GyroTiltTurnOn));
            Assert.True(engine.Pipeline.Context.IsGyroToggleActive);

            Assert.True(dispatcher.Execute(RewasdUserCommand.TurnRemapOff));
            Assert.False(engine.IsRunning);
        }
        finally
        {
            engine.Dispose();
        }
    }

    [Fact]
    public void EngineCommands_WithoutEngine_ReturnFalse()
    {
        var dispatcher = new CommandDispatcher(engine: null);
        Assert.False(dispatcher.Execute(RewasdUserCommand.GyroSwitch));
        Assert.False(dispatcher.Execute(RewasdUserCommand.TiltReset));
        Assert.False(dispatcher.Execute(RewasdUserCommand.TurnRemapOff));
    }

    [Fact]
    public void CommandExecuted_EventCarriesResult()
    {
        var dispatcher = new CommandDispatcher(engine: null);
        RewasdUserCommand? seen = null;
        bool? success = null;
        dispatcher.CommandExecuted += (c, s, _) => { seen = c; success = s; };

        dispatcher.Execute(RewasdUserCommand.GyroSwitch);

        Assert.Equal(RewasdUserCommand.GyroSwitch, seen);
        Assert.False(success);
    }
}

public class HardwareDecoderTests
{
    [Fact]
    public void ElitePaddleDecoder_DecodesPaddleBits()
    {
        Assert.Equal(ElitePaddleMask.P1 | ElitePaddleMask.P3, XboxElitePaddleDecoder.Decode([0x05]));
        Assert.Equal(ElitePaddleMask.P2 | ElitePaddleMask.P4, XboxElitePaddleDecoder.Decode([0x0A]));
        Assert.Equal(ElitePaddleMask.None, XboxElitePaddleDecoder.Decode([0x00]));
        Assert.Equal(ElitePaddleMask.None, XboxElitePaddleDecoder.Decode([]));
    }

    [Fact]
    public void ElitePaddleDecoder_BuildsVendorUnclampReports()
    {
        var usb = XboxElitePaddleDecoder.BuildUnclampFeatureReport(bluetooth: false);
        var bt = XboxElitePaddleDecoder.BuildUnclampFeatureReport(bluetooth: true);
        Assert.Equal(XboxElitePaddleDecoder.UsbUnclampReportId, usb[0]);
        Assert.Equal(XboxElitePaddleDecoder.BluetoothUnclampReportId, bt[0]);
    }

    [Fact]
    public void DualSenseDecoder_ExtractsEdgeButtons()
    {
        var report = new byte[64];
        report[0] = 0x31;
        report[DualSenseFeatureDecoder.EdgeButtonsOffset] = 0x1C; // Fn L + Fn R + back paddle L bits

        var state = DualSenseFeatureDecoder.Decode(report, bluetooth: false);

        Assert.True(state.EdgeButtons.HasFlag(DualSenseEdgeButtons.ProfileLeftFn));
        Assert.True(state.EdgeButtons.HasFlag(DualSenseEdgeButtons.ProfileRightFn));
        Assert.True(state.EdgeButtons.HasFlag(DualSenseEdgeButtons.BackPaddleLeft));
        Assert.False(state.EdgeButtons.HasFlag(DualSenseEdgeButtons.BackPaddleRight));
    }

    [Fact]
    public void SwitchUartClient_Parses0x30InputReport()
    {
        var report = new byte[64];
        report[0] = 0x30;
        report[3] = 0x08; // A pressed
        report[4] = 0x10; // Home
        report[6] = 0xFF; report[7] = 0x0F; // left stick full right (4095)

        var parsed = NintendoSwitchUartClient.ParseInputReport(report, bluetooth: false);

        Assert.NotNull(parsed);
        Assert.True(parsed.A);
        Assert.True(parsed.Home);
        Assert.False(parsed.B);
        Assert.True(parsed.LeftStickX > 0.99f);
    }

    [Fact]
    public void SwitchUartClient_RejectsWrongReportId()
    {
        var report = new byte[64];
        report[0] = 0x3F;
        Assert.Null(NintendoSwitchUartClient.ParseInputReport(report, bluetooth: false));
    }

    [Fact]
    public void SwitchUartClient_BuildsSpiFlashRead()
    {
        var packet = NintendoSwitchUartClient.ReadSpiFlash(NintendoSwitchUartClient.LeftStickCalibrationAddress, 9);
        Assert.Equal(0x01, packet[0]);
        Assert.Equal(0x21, packet[10]);           // SPI flash read subcommand
        Assert.Equal(0x3D, packet[11]);           // address low byte 0x603D
        Assert.Equal(9, packet[15]);              // length
    }
}

public class KeyPressedPollerTests
{
    [Fact]
    public void DetectPressedButtons_ReportsOnlyRisingEdges()
    {
        var before = new GamepadState { Buttons = GamepadButtons.A | GamepadButtons.B };
        var current = new GamepadState { Buttons = GamepadButtons.A | GamepadButtons.X | GamepadButtons.Y };

        var rising = KeyPressedPollerService.DetectPressedButtons(before, current);

        Assert.Equal(2, rising.Count);
        Assert.Contains(rising, r => r.ButtonName == "X");
        Assert.Contains(rising, r => r.ButtonName == "Y");
    }

    [Fact]
    public void DetectPressedButtons_EmptyFrames_ProduceNoEvents()
    {
        Assert.Empty(KeyPressedPollerService.DetectPressedButtons(null, null));
        Assert.Empty(KeyPressedPollerService.DetectPressedButtons(
            new GamepadState { Buttons = GamepadButtons.A },
            new GamepadState { Buttons = GamepadButtons.A }));
    }

    [Fact]
    public async Task PollingLoop_PublishesKeyPressedEvents()
    {
        var publisher = new CollectingPublisher();
        var controller = new FakePollingController();
        var discovery = new FakeDiscovery(new[] { controller });
        using var poller = new KeyPressedPollerService(discovery, publisher) { PollIntervalMs = 5 };

        var received = new TaskCompletionSource();
        poller.KeyPressed += _ => received.TrySetResult();
        poller.StartPolling();

        // Simulate a physical A press on the next poll frame
        controller.SetButtons(GamepadButtons.A);

        await Task.WhenAny(publisher.EventPublished.Task, Task.Delay(2000));
        poller.StopPolling();

        Assert.True(publisher.Events.Count > 0, "poller must publish at least one KeyPressed event");
        Assert.Contains(publisher.Events, e => e.Item1 == "KeyPressed");
    }

    private sealed class CollectingPublisher : ISSEEventPublisher
    {
        public List<(string EventName, object Payload)> Events { get; } = new();
        public TaskCompletionSource EventPublished { get; } = new();
        public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        {
            lock (Events) Events.Add((eventName, payload));
            EventPublished.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FakePollingController : IHardwareController
    {
        private GamepadState _state = new();
        public string Id => "fake-1";
        public string Name => "Fake Controller";
        public string HardwareType => "Generic";
        public string ConnectionType => "USB";
        public int BatteryLevel => -1;
        public bool IsCharging => false;
        public bool IsConnected => true;
        public GamepadState CurrentState => _state;
#pragma warning disable CS0067
        public event Action<IHardwareController, GamepadState>? InputReceived;
#pragma warning restore CS0067
        public void Vibrate(ushort left, ushort right) { }
        public void StartPolling() { }
        public void StopPolling() { }
        public void SetButtons(GamepadButtons buttons) => _state = new GamepadState { Buttons = buttons };
        public void Dispose() { }
    }

    private sealed class FakeDiscovery : IHardwareDiscoveryService
    {
        private readonly IReadOnlyList<IHardwareController> _controllers;
        public FakeDiscovery(IReadOnlyList<IHardwareController> controllers) => _controllers = controllers;
        public IReadOnlyList<IHardwareController> DiscoveredControllers => _controllers;
        public IHardwareController? SelectedController { get; set; }
#pragma warning disable CS0067
        public event Action<IHardwareController>? ControllerConnected;
        public event Action<IHardwareController>? ControllerDisconnected;
        public event Action<IHardwareController?>? SelectedControllerChanged;
#pragma warning restore CS0067
        public Task RefreshAsync() => Task.CompletedTask;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
