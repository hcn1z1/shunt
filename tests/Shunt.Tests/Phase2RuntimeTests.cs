using Shunt.Core.Models;
using Shunt.Core.Interfaces;
using Shunt.Emulation;
using Shunt.Engine;
using Shunt.Profiles;
using Shunt.Profiles.Models;
using Xunit;

namespace Shunt.Tests;

public sealed class Phase2RuntimeTests
{
    [Fact]
    public void ChordStateMachine_SuppressesSoloButton_WhenChordTriggers()
    {
        var machine = new ChordStateMachine();
        var chord = GamepadButtons.Back | GamepadButtons.B;

        var pending = machine.Resolve(GamepadButtons.Back, [chord], timestampMilliseconds: 0);
        Assert.Equal(GamepadButtons.Back, pending.PendingButtons);
        Assert.Equal(GamepadButtons.None, pending.FilteredButtons);

        var resolved = machine.Resolve(GamepadButtons.Back | GamepadButtons.B, [chord], timestampMilliseconds: 30);
        Assert.Equal(chord, resolved.ActiveChordButtons);
        Assert.Equal(GamepadButtons.None, resolved.FilteredButtons);
    }

    [Fact]
    public void ChordStateMachine_ReleasesSoloButton_WhenWindowExpires()
    {
        var machine = new ChordStateMachine();
        var chord = GamepadButtons.Back | GamepadButtons.B;

        machine.Resolve(GamepadButtons.Back, [chord], timestampMilliseconds: 100);
        var resolved = machine.Resolve(GamepadButtons.Back, [chord], timestampMilliseconds: 151);

        Assert.Equal(GamepadButtons.None, resolved.PendingButtons);
        Assert.Equal(GamepadButtons.Back, resolved.FilteredButtons);
    }

    [Fact]
    public void ChordStateMachine_DoesNotFormChord_AfterWindowExpires()
    {
        var machine = new ChordStateMachine();
        var chord = GamepadButtons.Back | GamepadButtons.B;
        machine.Resolve(GamepadButtons.Back, [chord], timestampMilliseconds: 0);

        var latePartner = machine.Resolve(chord, [chord], timestampMilliseconds: 51);

        Assert.Equal(GamepadButtons.None, latePartner.ActiveChordButtons);
        Assert.Equal(GamepadButtons.Back, latePartner.FilteredButtons);
    }

    [Fact]
    public void MouseZoneEvaluator_Classifies_Deadzone_Correctly()
    {
        var result = MouseZoneEvaluator.Evaluate(0.02f, 0.01f);
        Assert.Equal(MouseZone.Deadzone, result.Zone);
        Assert.Equal(0f, result.SensitivityMultiplier);
        Assert.Equal(MouseDirection.Center, result.Direction);
    }

    [Fact]
    public void MouseZoneEvaluator_Applies_LowZone_SensitivityMultiplier()
    {
        var result = MouseZoneEvaluator.Evaluate(0.1f, 0f, new MouseZoneConfig(LowMultiplier: 0.4f));
        Assert.Equal(MouseZone.Low, result.Zone);
        Assert.Equal(0.4f, result.SensitivityMultiplier);
        Assert.Equal(MouseDirection.Right, result.Direction);
    }

    [Fact]
    public void MouseZoneEvaluator_Triggers_HighZone_EdgeAction()
    {
        var result = MouseZoneEvaluator.Evaluate(0.95f, 0f, new MouseZoneConfig(EdgeAction: "sprint"));
        Assert.Equal(MouseZone.High, result.Zone);
        Assert.Equal("sprint", result.EdgeAction);
        Assert.True(result.IsEdge);
    }

    [Theory]
    [InlineData(0f, -0.8f, MouseDirection.Up)]
    [InlineData(0.8f, 0f, MouseDirection.Right)]
    [InlineData(0f, 0.8f, MouseDirection.Down)]
    [InlineData(-0.8f, 0f, MouseDirection.Left)]
    [InlineData(0.8f, 0.8f, MouseDirection.DownRight)]
    public void MouseZoneEvaluator_Calculates_DirectionalSweep_Accurately(float x, float y, MouseDirection expected)
    {
        Assert.Equal(expected, MouseZoneEvaluator.Evaluate(x, y).Direction);
    }

    [Fact]
    public void ProfileCompiler_Loads_MouseZoneConfiguration_FromShift()
    {
        var profile = new RewasdProfileDocument
        {
            Shifts =
            [
                new ProfileShift
                {
                    Id = 1,
                    Mouse = new ProfileMouseSettings
                    {
                        Zones =
                        [
                            new ProfileMouseZone { Name = "deadzone", Radius = 1600 },
                            new ProfileMouseZone { Name = "low", Radius = 8000, Multiplier = 0.5f },
                            new ProfileMouseZone { Name = "med", Radius = 20000, Multiplier = 1f },
                            new ProfileMouseZone { Name = "high", Radius = 30000, Multiplier = 2.2f, Action = "sprint" }
                        ]
                    }
                }
            ]
        };

        var compiled = ProfileCompiler.Compile(profile);
        Assert.NotNull(compiled.MouseZones);
        Assert.Equal(30000, compiled.MouseZones!.Zones.Single(zone => zone.Name == "high").Radius);
    }

    [Fact]
    public void MappingPipeline_Applies_MouseZone_Sensitivity_To_RightStick()
    {
        var pipeline = new MappingPipeline(new MappingExecutionContext
        {
            MouseZones = new MouseZoneConfig(DeadzoneRadius: 0.03f, LowRadius: 0.5f,
                MediumRadius: 0.75f, HighRadius: 0.9f, LowMultiplier: 0.5f)
        });

        var output = pipeline.Process(new GamepadState { RightStick = new StickPosition(0.3f, 0f) });

        Assert.Equal(MouseZone.Low, pipeline.LastMouseZone!.Value.Zone);
        Assert.InRange(output.RightStick.X, 0.11f, 0.13f);
    }

    [Fact]
    public void HapticDispatcher_Routes_BothMotors_ToPhysicalAndVirtualControllers()
    {
        using var manager = new StubVirtualGamepadManager();
        var physical = new FakePhysicalController();
        using var discovery = new StubHardwareDiscoveryService { SelectedController = physical };
        using var engine = new ShuntEngineService(manager, discovery);
        engine.Start(new RewasdProfileDocument());

        engine.Vibrate(0xABCD, 0x1234);

        Assert.Equal((0xABCD, 0x1234), physical.LastVibration);
        Assert.IsType<StubVirtualGamepad>(engine.ActiveVirtualGamepad);
        var virtualPad = (StubVirtualGamepad)engine.ActiveVirtualGamepad!;
        Assert.Equal(((byte)0xAB, (byte)0x12), virtualPad.LastVibration);
    }

    private sealed class FakePhysicalController : IHardwareController
    {
        public string Id => "phase2-test";
        public string Name => "Phase 2 Test Pad";
        public string HardwareType => "Generic";
        public string ConnectionType => "USB";
        public int BatteryLevel => -1;
        public bool IsCharging => false;
        public bool IsConnected => true;
        public GamepadState CurrentState => new();
        public (ushort Left, ushort Right) LastVibration { get; private set; }
        public event Action<IHardwareController, GamepadState>? InputReceived { add { } remove { } }
        public void Vibrate(ushort leftMotor, ushort rightMotor) => LastVibration = (leftMotor, rightMotor);
        public void StartPolling() { }
        public void StopPolling() { }
        public void Dispose() { }
    }
}
