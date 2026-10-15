using System.Collections.Generic;
using Shunt.Core.MathTransform;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Engine;
using Shunt.Profiles;
using Shunt.Profiles.Models;
using Xunit;

namespace Shunt.Tests;

public class EngineAndProfileTests
{
    [Fact]
    public void Deadzone_WithinInnerThreshold_ReturnsZero()
    {
        var config = new DeadzoneConfig { InnerDeadzone = 0.1f, OuterDeadzone = 0.05f, Circular = true };
        var input = new StickPosition(0.05f, 0.05f); // Magnitude = ~0.07 < 0.1

        var output = StickTransform.ApplyDeadzone(input, config);

        Assert.Equal(0f, output.X);
        Assert.Equal(0f, output.Y);
    }

    [Fact]
    public void Deadzone_BeyondOuterThreshold_ClampsToUnit()
    {
        var config = new DeadzoneConfig { InnerDeadzone = 0.1f, OuterDeadzone = 0.05f, Circular = true };
        var input = new StickPosition(0.98f, 0.0f); // Magnitude >= 0.95

        var output = StickTransform.ApplyDeadzone(input, config);

        Assert.Equal(1f, output.X);
        Assert.Equal(0f, output.Y);
    }

    [Fact]
    public void ProfileLoader_DeserializesSampleJson_Correctly()
    {
        const string json = """
        {
          "schemaVersion": 3.0,
          "appVersion": "6.4.0",
          "devices": {
            "hardware": [
              {
                "id": 1,
                "name": "gamepad"
              }
            ],
            "virtual": {
              "gamepads": [
                {
                  "id": 1,
                  "type": "ds4"
                }
              ]
            }
          },
          "shifts": [
            {
              "id": 1,
              "type": "default",
              "description": "Default Shift"
            }
          ]
        }
        """;

        var doc = ProfileLoader.LoadFromString(json);

        Assert.Equal(3.0, doc.SchemaVersion);
        Assert.Single(doc.Devices.Hardware);
        Assert.Equal("gamepad", doc.Devices.Hardware[0].Name);
        Assert.Equal(VirtualGamepadType.DualShock4, ProfileLoader.DetermineTargetType(doc));
    }

    [Fact]
    public void Engine_WithStubManager_DispatchesState()
    {
        using var manager = new StubVirtualGamepadManager();
        using var engine = new ShuntEngineService(manager);

        var profile = new RewasdProfileDocument
        {
            Devices = new ProfileDevices
            {
                Virtual = new VirtualDevices
                {
                    Gamepads = [new VirtualGamepadEntry { Id = 1, Type = "360" }]
                }
            }
        };

        engine.Start(profile);
        Assert.True(engine.IsRunning);
        Assert.NotNull(engine.ActiveVirtualGamepad);

        var testState = new GamepadState
        {
            Buttons = GamepadButtons.A | GamepadButtons.RightShoulder,
            LeftStick = new StickPosition(0.5f, -0.5f)
        };

        engine.DispatchInput(testState);

        if (engine.ActiveVirtualGamepad is StubVirtualGamepad stub)
        {
            Assert.True(stub.LastReport.HasButton(GamepadButtons.A));
            Assert.True(stub.LastReport.HasButton(GamepadButtons.RightShoulder));
        }

        engine.Stop();
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void ApplyProfileRequest_EffectivePath_ResolvesConfigPathOrPath()
    {
        var req1 = new ApplyProfileRequest(ConfigPath: "C:\\path1.rewasd");
        Assert.Equal("C:\\path1.rewasd", req1.EffectivePath);

        var req2 = new ApplyProfileRequest(Path: "C:\\path2.rewasd");
        Assert.Equal("C:\\path2.rewasd", req2.EffectivePath);

        var req3 = new ApplyProfileRequest(ConfigPath: "C:\\primary.rewasd", Path: "C:\\fallback.rewasd");
        Assert.Equal("C:\\primary.rewasd", req3.EffectivePath);

        var req4 = new ApplyProfileRequest();
        Assert.Equal(string.Empty, req4.EffectivePath);
    }

    [Fact]
    public void ApplyProfileRequest_DeserializesFromBothPropertyNames()
    {
        var jsonWithConfigPath = """{"ConfigPath": "C:\\test\\profile.rewasd", "Slot": 1}""";
        var parsed1 = System.Text.Json.JsonSerializer.Deserialize<ApplyProfileRequest>(jsonWithConfigPath);
        Assert.NotNull(parsed1);
        Assert.Equal("C:\\test\\profile.rewasd", parsed1.EffectivePath);
        Assert.Equal(1, parsed1.Slot);

        var jsonWithPath = """{"Path": "C:\\test\\profile2.rewasd", "Slot": 0}""";
        var parsed2 = System.Text.Json.JsonSerializer.Deserialize<ApplyProfileRequest>(jsonWithPath);
        Assert.NotNull(parsed2);
        Assert.Equal("C:\\test\\profile2.rewasd", parsed2.EffectivePath);
        Assert.Equal(0, parsed2.Slot);
    }
}

