using Shunt.Core.Models;
using Shunt.Profiles;
using Shunt.Profiles.Models;
using Xunit;

namespace Shunt.Tests;

public class ExtendedInputResolutionTests
{
    [Theory]
    [InlineData(256, GamepadButtons.A)]
    [InlineData(257, GamepadButtons.B)]
    [InlineData(258, GamepadButtons.X)]
    [InlineData(259, GamepadButtons.Y)]
    [InlineData(260, GamepadButtons.LeftShoulder)]
    [InlineData(261, GamepadButtons.RightShoulder)]
    [InlineData(262, GamepadButtons.Back)]
    [InlineData(263, GamepadButtons.Start)]
    [InlineData(264, GamepadButtons.LeftThumb)]
    [InlineData(265, GamepadButtons.RightThumb)]
    [InlineData(266, GamepadButtons.Guide)]
    [InlineData(267, GamepadButtons.Share)]
    [InlineData(269, GamepadButtons.DpadUp)]
    [InlineData(270, GamepadButtons.DpadDown)]
    [InlineData(271, GamepadButtons.DpadLeft)]
    [InlineData(272, GamepadButtons.DpadRight)]
    [InlineData(283, GamepadButtons.TouchpadTap)]
    [InlineData(289, GamepadButtons.SwipeUp)]
    [InlineData(290, GamepadButtons.SwipeDown)]
    [InlineData(291, GamepadButtons.SwipeLeft)]
    [InlineData(292, GamepadButtons.SwipeRight)]
    [InlineData(303, GamepadButtons.TouchpadClick)]
    [InlineData(304, GamepadButtons.TiltUp)]
    [InlineData(305, GamepadButtons.TiltDown)]
    [InlineData(306, GamepadButtons.TiltRight)]
    [InlineData(307, GamepadButtons.TiltLeft)]
    [InlineData(308, GamepadButtons.TiltLeft)]
    [InlineData(309, GamepadButtons.TiltRight)]
    [InlineData(310, GamepadButtons.VGYRO)]
    public void ResolveButtonId_ResolvesKeyScanCodeV2GamepadIndices(int index, GamepadButtons expected)
    {
        var resolved = ButtonIdResolver.ResolveButtonId(index);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(1, GamepadButtons.A)]
    [InlineData(2, GamepadButtons.B)]
    [InlineData(5, GamepadButtons.LeftShoulder)]
    [InlineData(29, GamepadButtons.PaddleP1)]
    [InlineData(30, GamepadButtons.PaddleP2)]
    [InlineData(31, GamepadButtons.PaddleP3)]
    [InlineData(32, GamepadButtons.PaddleP4)]
    [InlineData(37, GamepadButtons.TouchpadClick)]
    [InlineData(41, GamepadButtons.SwipeUp)]
    [InlineData(45, GamepadButtons.TiltLeft)]
    [InlineData(49, GamepadButtons.VGYRO)]
    public void ResolveButtonId_PreservesLegacyAndPaddleIds(int index, GamepadButtons expected)
    {
        var resolved = ButtonIdResolver.ResolveButtonId(index);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("Paddle P1", GamepadButtons.PaddleP1)]
    [InlineData("LeftUpperPaddle", GamepadButtons.PaddleP1)]
    [InlineData("Paddle P2", GamepadButtons.PaddleP2)]
    [InlineData("RightUpperPaddle", GamepadButtons.PaddleP2)]
    [InlineData("Paddle P3", GamepadButtons.PaddleP3)]
    [InlineData("LeftLowerPaddle", GamepadButtons.PaddleP3)]
    [InlineData("Paddle P4", GamepadButtons.PaddleP4)]
    [InlineData("RightLowerPaddle", GamepadButtons.PaddleP4)]
    public void ResolveButtonId_ResolvesPaddleDescriptions(string desc, GamepadButtons expected)
    {
        var resolved = ButtonIdResolver.ResolveButtonId(0, desc);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ProfileCompiler_CompilesPaddleAndExtendedInputMappingsEndToEnd()
    {
        var json = """
        {
          "schemaVersion": 3.0,
          "appVersion": "Shunt",
          "masks": [
            {
              "id": 1,
              "set": [
                { "deviceId": 1, "buttonId": 29, "description": "Paddle P1" }
              ]
            },
            {
              "id": 2,
              "set": [
                { "deviceId": 1, "buttonId": 289, "description": "1-Finger Swipe Up" }
              ]
            }
          ],
          "mappings": [
            {
              "description": "Paddle P1 to BTN_A",
              "condition": {
                "shiftId": 1,
                "mask": {
                  "id": 1,
                  "activator": { "type": "single", "mode": "hold_until_release" }
                }
              },
              "macros": [
                {
                  "gamepad": { "id": 1, "buttonId": 256, "description": "BTN_A" }
                }
              ]
            },
            {
              "description": "Swipe Up to Paddle P2",
              "condition": {
                "shiftId": 1,
                "mask": {
                  "id": 2,
                  "activator": { "type": "single", "mode": "hold_until_release" }
                }
              },
              "macros": [
                {
                  "gamepad": { "id": 1, "buttonId": 30, "description": "Paddle P2" }
                }
              ]
            }
          ]
        }
        """;

        var profile = ProfileLoader.LoadFromString(json);
        var compiled = ProfileCompiler.Compile(profile);

        Assert.Equal(2, compiled.Rules.Count);

        var rule1 = compiled.Rules[0];
        Assert.Equal(GamepadButtons.PaddleP1, rule1.TriggerButtons);
        Assert.Equal(GamepadButtons.A, rule1.OutputButtons);
        Assert.Equal(TargetOutputType.Gamepad, rule1.TargetType);

        var rule2 = compiled.Rules[1];
        Assert.Equal(GamepadButtons.SwipeUp, rule2.TriggerButtons);
        Assert.Equal(GamepadButtons.PaddleP2, rule2.OutputButtons);
        Assert.Equal(TargetOutputType.Gamepad, rule2.TargetType);
    }
}
