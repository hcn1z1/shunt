using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Compiler;
using Shunt.Core.MathTransform;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Engine;
using Shunt.Profiles;
using Shunt.Profiles.Models;
using Xunit;

namespace Shunt.Tests;

public class AdvancedFeatureTests
{
    [Fact]
    public void CubicBezierCurve_Linear_EvaluatesIdentity()
    {
        var curve = CubicBezierCurve.Linear;
        Assert.Equal(0f, curve.Evaluate(0f));
        Assert.Equal(1f, curve.Evaluate(1f));
        Assert.InRange(curve.Evaluate(0.5f), 0.48f, 0.52f);
        Assert.InRange(curve.Evaluate(-0.5f), -0.52f, -0.48f);
    }

    [Fact]
    public void CubicBezierCurve_Instant_AcceleratesQuickly()
    {
        var curve = CubicBezierCurve.Instant;
        float outputAt20Percent = curve.Evaluate(0.20f);
        Assert.True(outputAt20Percent > 0.60f, $"Instant curve at 20% should be > 60%, but got {outputAt20Percent}");
    }

    [Fact]
    public void StickTransform_ToSquare_StretchesDiagonalsToCorners()
    {
        // Diagonal point at radius 1.0 (approx 0.7071, 0.7071)
        float invSqrt2 = 1.0f / MathF.Sqrt(2f);
        var circularDiag = new StickPosition(invSqrt2, invSqrt2);

        var squarePos = StickTransform.ToSquare(circularDiag);

        Assert.Equal(1.0f, squarePos.X, 3);
        Assert.Equal(1.0f, squarePos.Y, 3);

        // Center remains center
        Assert.Equal(StickPosition.Center, StickTransform.ToSquare(StickPosition.Center));

        // Axis point remains unscaled
        var axisPoint = new StickPosition(1.0f, 0.0f);
        var squareAxis = StickTransform.ToSquare(axisPoint);
        Assert.Equal(1.0f, squareAxis.X, 3);
        Assert.Equal(0.0f, squareAxis.Y, 3);
    }

    [Fact]
    public void DeflectionZones_ClassifiesCorrectly()
    {
        var thresholds = new ZoneThresholds(0.35f, 0.75f);

        Assert.Equal(DeflectionZone.None, thresholds.Classify(0.02f));
        Assert.Equal(DeflectionZone.Low, thresholds.Classify(0.20f));
        Assert.Equal(DeflectionZone.Medium, thresholds.Classify(0.50f));
        Assert.Equal(DeflectionZone.High, thresholds.Classify(0.85f));
    }

    [Fact]
    public void ChordSuppression_WhenChordHeld_SuppressesComponentButtons()
    {
        var context = new MappingExecutionContext
        {
            Rules =
            [
                // Chord: LB + RB -> Guide
                new ResolvedMappingRule
                {
                    TriggerButtons = GamepadButtons.LeftShoulder | GamepadButtons.RightShoulder,
                    OutputButtons = GamepadButtons.Guide
                },
                // Single button: LB -> A
                new ResolvedMappingRule
                {
                    TriggerButtons = GamepadButtons.LeftShoulder,
                    OutputButtons = GamepadButtons.A
                },
                // Single button: RB -> B
                new ResolvedMappingRule
                {
                    TriggerButtons = GamepadButtons.RightShoulder,
                    OutputButtons = GamepadButtons.B
                }
            ]
        };

        var pipeline = new MappingPipeline(context);

        // 1. Only LB is pressed -> should output A
        var outSingleLB = pipeline.Process(new GamepadState { Buttons = GamepadButtons.LeftShoulder });
        Assert.True(outSingleLB.HasButton(GamepadButtons.A));
        Assert.False(outSingleLB.HasButton(GamepadButtons.Guide));

        // 2. Only RB is pressed -> should output B
        var outSingleRB = pipeline.Process(new GamepadState { Buttons = GamepadButtons.RightShoulder });
        Assert.True(outSingleRB.HasButton(GamepadButtons.B));
        Assert.False(outSingleRB.HasButton(GamepadButtons.Guide));

        // 3. Both LB and RB held (Chord) -> should output Guide, and suppress A and B!
        var outChord = pipeline.Process(new GamepadState
        {
            Buttons = GamepadButtons.LeftShoulder | GamepadButtons.RightShoulder
        });
        Assert.True(outChord.HasButton(GamepadButtons.Guide), "Chord Guide should be output.");
        Assert.False(outChord.HasButton(GamepadButtons.A), "A should be suppressed by chord.");
        Assert.False(outChord.HasButton(GamepadButtons.B), "B should be suppressed by chord.");
    }

    [Fact]
    public void MouseToStickConverter_AppliesSmoothingAndDecay()
    {
        var converter = new MouseToStickConverter
        {
            SensitivityX = 0.1f,
            SensitivityY = 0.1f,
            SmoothingAlpha = 1.0f // direct for testing
        };

        // Sub-threshold jitter is filtered
        var posZero = converter.Update(1, 0);
        Assert.Equal(0f, posZero.X);

        // Meaningful mouse motion produces deflection
        var posMoved = converter.Update(10, 0);
        Assert.True(posMoved.X > 0f);

        // When mouse stops, position decays towards zero
        var posDecayed = converter.Update(0, 0);
        Assert.True(posDecayed.X < posMoved.X);
    }

    [Fact]
    public async Task MacroEngine_ExecutesStepSequence_Successfully()
    {
        using var stub = new StubVirtualGamepad(VirtualGamepadType.Xbox360);
        stub.Connect();

        var steps = new[]
        {
            new MacroStepInstruction(MacroOpCode.KeyDown, GamepadButtons.A),
            new MacroStepInstruction(MacroOpCode.Delay, DelayMs: 10),
            new MacroStepInstruction(MacroOpCode.KeyUp, GamepadButtons.A),
            new MacroStepInstruction(MacroOpCode.RumblePulse, DelayMs: 10)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await MacroEngine.ExecuteSequenceAsync(steps, stub, cts.Token);

        // After KeyUp, button A should be released
        Assert.False(stub.LastReport.HasButton(GamepadButtons.A));
    }

    [Fact]
    public void HidHideService_WhitelistAndBlacklist_RoundtripsInMemory()
    {
        using var hidHide = new HidHideService();

        string fakeApp = @"C:\Games\TestGame\game.exe";
        Assert.True(hidHide.AddApplicationWhitelist(fakeApp));
        Assert.Contains(fakeApp, hidHide.GetWhitelistedApplications());

        Assert.True(hidHide.RemoveApplicationWhitelist(fakeApp));
        Assert.DoesNotContain(fakeApp, hidHide.GetWhitelistedApplications());

        string fakeDevice = @"HID\VID_045E&PID_028E\1&2&3";
        Assert.True(hidHide.BlockDevice(fakeDevice));
        Assert.Contains(fakeDevice, hidHide.GetBlockedDevices());

        Assert.True(hidHide.UnblockDevice(fakeDevice));
        Assert.DoesNotContain(fakeDevice, hidHide.GetBlockedDevices());
    }

    [Fact]
    public void BytecodeWriter_SerializesAndHeaderMatchesSpec()
    {
        var doc = new RewasdProfileDocument
        {
            Config = new ProfileConfig { AppName = "Bytecode Test" },
            Devices = new ProfileDevices
            {
                Virtual = new VirtualDevices
                {
                    Gamepads = [new VirtualGamepadEntry { Id = 1, Type = "ds4" }]
                }
            },
            Shifts = [new ProfileShift { Id = 1, Type = "default" }],
            Masks =
            [
                new ProfileMask
                {
                    Id = 1,
                    Set = [new MaskTrigger { DeviceId = 1, ButtonId = 1, Description = "Cross" }]
                }
            ],
            Mappings =
            [
                new ProfileMapping
                {
                    Condition = new MappingCondition { ShiftId = 1, Mask = new ConditionMask { Id = 1 } },
                    Macros = [new MacroStep { Gamepad = new GamepadMacroTarget { ButtonId = 2 } }]
                }
            ]
        };

        byte[] bytecode = BytecodeWriter.CompileToBytes(doc);
        Assert.NotNull(bytecode);
        Assert.True(bytecode.Length > 28);

        using var ms = new MemoryStream(bytecode);
        var header = BytecodeWriter.ReadHeader(ms);

        Assert.Equal(BytecodeConstants.Magic, header.Magic);
        Assert.Equal(BytecodeConstants.Version, header.Version);
        Assert.Equal(BytecodeConstants.TargetDualShock4, header.TargetControllerType);
        Assert.Equal(1u, header.ShiftsCount);
        Assert.True(header.ShiftTableOffset > 0);
        Assert.True(header.MaskTableOffset > 0);
        Assert.True(header.MacroBlockOffset > 0);
    }

    [Fact]
    public void ShiftLayerHierarchy_InheritsAndOverridesCorrectly()
    {
        var ctx = new MappingExecutionContext
        {
            ActiveShiftId = 2,
            BaseShiftId = 1
        };
        ctx.ShiftParentMap[2] = 1; // Shift 2 inherits from Shift 1

        // Base Layer (Shift 1): A -> B, X -> Y
        ctx.Rules.Add(new ResolvedMappingRule
        {
            ShiftLayerId = 1,
            TriggerButtons = GamepadButtons.A,
            OutputButtons = GamepadButtons.B,
            Activator = MappingActivator.Single
        });
        ctx.Rules.Add(new ResolvedMappingRule
        {
            ShiftLayerId = 1,
            TriggerButtons = GamepadButtons.X,
            OutputButtons = GamepadButtons.Y,
            Activator = MappingActivator.Single
        });

        // Layer 2: A -> RightShoulder (Overrides Shift 1 for button A)
        // Note: X is not defined in Shift 2, so it should be inherited from Shift 1
        ctx.Rules.Add(new ResolvedMappingRule
        {
            ShiftLayerId = 2,
            TriggerButtons = GamepadButtons.A,
            OutputButtons = GamepadButtons.RightShoulder,
            Activator = MappingActivator.Single
        });

        var pipeline = new MappingPipeline(ctx);

        // Test 1: Button A pressed in Shift 2 -> Outputs RightShoulder (overridden)
        var inputA = new GamepadState { Buttons = GamepadButtons.A };
        var outputA = pipeline.Process(inputA);
        Assert.True(outputA.HasButton(GamepadButtons.RightShoulder));
        Assert.False(outputA.HasButton(GamepadButtons.B));

        // Test 2: Button X pressed in Shift 2 -> Outputs Y (inherited from Shift 1)
        var inputX = new GamepadState { Buttons = GamepadButtons.X };
        var outputX = pipeline.Process(inputX);
        Assert.True(outputX.HasButton(GamepadButtons.Y));

        // Test 3: Switch back to Shift 1 -> Button A now outputs B
        ctx.ActiveShiftId = 1;
        var outputA_Base = pipeline.Process(inputA);
        Assert.True(outputA_Base.HasButton(GamepadButtons.B));
        Assert.False(outputA_Base.HasButton(GamepadButtons.RightShoulder));
    }

    [Fact]
    public void MouseToStickConverter_BallisticPresets_AdjustParameters()
    {
        var converter = new MouseToStickConverter();

        converter.ApplyPreset(BallisticPreset.Linear);
        Assert.Equal(1.0f, converter.BallisticGamma);

        converter.ApplyPreset(BallisticPreset.CallOfDuty);
        Assert.Equal(1.45f, converter.BallisticGamma);
        Assert.Equal(0.06f, converter.SensitivityX);

        converter.ApplyPreset(BallisticPreset.ApexLegends);
        Assert.Equal(1.25f, converter.BallisticGamma);

        converter.ApplyPreset(BallisticPreset.Aggressive);
        Assert.Equal(1.75f, converter.BallisticGamma);

        // Verify output deflection with preset
        var stick = converter.Update(10, 0);
        Assert.True(stick.X > 0f);
    }
}
