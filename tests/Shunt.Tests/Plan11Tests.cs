using System;
using System.Linq;
using System.Text.Json.Nodes;
using Shunt.Core.Models;
using Shunt.Core.Scancodes;
using Xunit;

namespace Shunt.Tests;

/// <summary>
/// Plan 11 (UI Multi-Chassis Canvas & Target Controller Switcher): target catalog,
/// target-aware output filtering, keyboard/mouse anchors and profile persistence.
/// </summary>
public class Plan11Tests
{
    // ---------------- Milestone 1: rich TargetControllerChoice catalog ----------------

    [Fact]
    public void GetTargetChoices_ReturnsFourFamilyChoicesInPlanOrder()
    {
        var choices = MappingChoiceProvider.GetTargetChoices();

        Assert.Equal(4, choices.Count);
        // Top-level choices are families; legacy target variants remain supported in profile data.
        Assert.Equal("360", choices[0].Value);
        Assert.Equal(VirtualGamepadType.Xbox360, choices[0].Target);
        Assert.Equal("Xbox Family", choices[0].Name);
        Assert.Equal("ds4", choices[1].Value);
        Assert.Equal("PlayStation Family", choices[1].Name);
        Assert.Equal("nsw", choices[2].Value);
        Assert.Equal("Nintendo Switch Pro", choices[2].Name);
        Assert.Equal("kbm", choices[3].Value);
        Assert.Equal(VirtualGamepadType.KeyboardAndMouse, choices[3].Target);
        Assert.Equal("Desktop Output", choices[3].Name);

        foreach (var choice in choices)
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.False(string.IsNullOrWhiteSpace(choice.Subtitle));
            Assert.False(string.IsNullOrWhiteSpace(choice.IconGlyph));
            Assert.Equal(choice.Name, choice.ToString());
        }
    }

    [Theory]
    [InlineData("360", "360")]
    [InlineData("xone", "360")]
    [InlineData("ds4", "ds4")]
    [InlineData("ds3", "ds4")]
    [InlineData("nsw", "nsw")]
    [InlineData("kbm", "kbm")]
    public void VirtualTargetCatalog_ResolvesVariantsToTheirFamily(string target, string expectedFamily)
    {
        Assert.Equal(expectedFamily, VirtualTargetCatalog.GetFamilyChoice(target)?.Value);
    }

    [Theory]
    [InlineData("ds4", VirtualGamepadType.DualShock4)]
    [InlineData("360", VirtualGamepadType.Xbox360)]
    [InlineData("xone", VirtualGamepadType.XboxOne)]
    [InlineData("nsw", VirtualGamepadType.SwitchPro)]
    [InlineData("ds3", VirtualGamepadType.DualShock3)]
    [InlineData("kbm", VirtualGamepadType.KeyboardAndMouse)]
    [InlineData("DS4", VirtualGamepadType.DualShock4)]
    [InlineData("DualShock4", VirtualGamepadType.DualShock4)]
    [InlineData("KeyboardAndMouse", VirtualGamepadType.KeyboardAndMouse)]
    public void VirtualTargetCatalog_Parses_Codes_And_EnumNames(string value, VirtualGamepadType expected)
    {
        Assert.Equal(expected, VirtualTargetCatalog.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("ps5")]
    public void VirtualTargetCatalog_Parse_ReturnsNull_ForUnknownValues(string? value)
    {
        Assert.Null(VirtualTargetCatalog.Parse(value));
    }

    [Fact]
    public void VirtualTargetCatalog_ProfileValue_RoundTrips_EveryTarget()
    {
        foreach (var target in Enum.GetValues<VirtualGamepadType>())
        {
            var code = VirtualTargetCatalog.ToProfileValue(target);
            Assert.NotNull(code);
            Assert.Equal(target, VirtualTargetCatalog.Parse(code));
        }
    }

    // ---------------- Milestone 2: target-aware GetOutputTargets filtering ----------------

    private static System.Collections.Generic.IReadOnlyList<MappingChoice> Targets(VirtualGamepadType target) =>
        MappingChoiceProvider.GetOutputTargets(target);

    private static void AssertAllPresent(string[] expected, System.Collections.Generic.HashSet<string> actual)
    {
        foreach (var name in expected)
            Assert.Contains(name, actual);
    }

    [Fact]
    public void GetOutputTargets_DualShock4_UsesPlayStationNomenclature_AndTouchpad()
    {
        var ds4 = Targets(VirtualGamepadType.DualShock4);
        var gamepadNames = ds4.Where(c => c.Category is "Gamepad" or "Touchpad" or "Gyro & Motion" or "Paddles")
            .Select(c => c.Name).ToHashSet();

        AssertAllPresent(["Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "Share", "Options", "PS Button"], gamepadNames);
        Assert.DoesNotContain("A", gamepadNames);
        Assert.DoesNotContain("LB", gamepadNames);

        // DS4 touchpad gestures and motion are target outputs
        Assert.Contains(ds4, c => c.Id == 37 && c.Category == "Touchpad");   // Touchpad Click
        Assert.Contains(ds4, c => c.Id == 41 && c.Category == "Touchpad");   // Swipe Up
        Assert.Contains(ds4, c => c.Id == 49 && c.Category == "Gyro & Motion"); // VGYRO
        // No Elite paddles on a DualShock 4
        Assert.DoesNotContain(ds4, c => c.Category == "Paddles");
    }

    [Fact]
    public void GetOutputTargets_Xbox360_UsesXboxNomenclature_WithoutTouchpad()
    {
        var xb360 = Targets(VirtualGamepadType.Xbox360);
        var gamepadNames = xb360.Where(c => c.Category == "Gamepad").Select(c => c.Name).ToHashSet();

        AssertAllPresent(["A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Guide", "Back", "Start"], gamepadNames);
        Assert.DoesNotContain("Cross", gamepadNames);
        Assert.DoesNotContain(xb360, c => c.Category == "Touchpad");
        Assert.DoesNotContain(xb360, c => c.Category == "Paddles");
        Assert.DoesNotContain(xb360, c => c.Category == "Gyro & Motion");
    }

    [Fact]
    public void GetOutputTargets_XboxOne_AddsElitePaddles()
    {
        var xone = Targets(VirtualGamepadType.XboxOne);

        Assert.Contains(xone, c => c.Id == 29 && c.Name == "Paddle P1" && c.Category == "Paddles");
        Assert.Contains(xone, c => c.Id == 32 && c.Name == "Paddle P4" && c.Category == "Paddles");
        Assert.DoesNotContain(xone, c => c.Category == "Touchpad");
    }

    [Fact]
    public void GetOutputTargets_SwitchPro_UsesNintendoNames_AndMotion()
    {
        var nsw = Targets(VirtualGamepadType.SwitchPro);
        var gamepadNames = nsw.Where(c => c.Category == "Gamepad").Select(c => c.Name).ToHashSet();

        AssertAllPresent(["B", "A", "Y", "X", "L", "R", "Minus", "Plus", "Home", "ZL", "ZR"], gamepadNames);
        Assert.Contains(nsw, c => c.Id == 49 && c.Category == "Gyro & Motion"); // motion aiming
        Assert.DoesNotContain(nsw, c => c.Category == "Touchpad");
        Assert.DoesNotContain(nsw, c => c.Category == "Paddles");
    }

    [Fact]
    public void GetOutputTargets_DualShock3_PlayStationNames_WithoutTouchpadOrPaddles()
    {
        var ds3 = Targets(VirtualGamepadType.DualShock3);
        var gamepadNames = ds3.Where(c => c.Category == "Gamepad").Select(c => c.Name).ToHashSet();

        AssertAllPresent(["Cross", "Circle", "Square", "Triangle", "L1", "L2", "Share", "Options"], gamepadNames);
        Assert.DoesNotContain(ds3, c => c.Category == "Touchpad");
        Assert.DoesNotContain(ds3, c => c.Category == "Paddles");
    }

    [Fact]
    public void GetOutputTargets_KeyboardAndMouse_CategorizesKeysAndMouseOnly()
    {
        var kbm = Targets(VirtualGamepadType.KeyboardAndMouse);

        Assert.DoesNotContain(kbm, c => c.Category is "Gamepad" or "Touchpad" or "Paddles" or "Gyro & Motion");
        Assert.Contains(kbm, c => c.Name == "Key: W" && c.Category == "Keyboard");
        Assert.Contains(kbm, c => c.Name == "Key: Space" && c.Category == "Keyboard");
        Assert.Contains(kbm, c => c.Name == "Mouse: Left Click" && c.Category == "Mouse");
        Assert.Contains(kbm, c => c.Name == "Mouse: Wheel Up" && c.Category == "Mouse");
        Assert.Contains(kbm, c => c.Category == "Command");
    }

    [Fact]
    public void GetOutputTargets_EveryGamepadTarget_KeepsKeyboardMouseAndCommands()
    {
        foreach (var target in Enum.GetValues<VirtualGamepadType>())
        {
            var list = Targets(target);
            Assert.Contains(list, c => c.Category == "Keyboard");
            Assert.Contains(list, c => c.Category == "Mouse");
            Assert.Contains(list, c => c.Category == "Command");
        }
    }

    [Fact]
    public void GetOutputTargets_PreservesStableLegacyGamepadIds()
    {
        var xb360 = Targets(VirtualGamepadType.Xbox360);

        Assert.Contains(xb360, c => c.Id == 1 && c.Name == "A");
        Assert.Contains(xb360, c => c.Id == 2 && c.Name == "B");
        Assert.Contains(xb360, c => c.Id == 51 && c.Name == "LT");
        Assert.Contains(xb360, c => c.Id == 55 && c.Name == "RT");
        Assert.Contains(xb360, c => c.Id == 11 && c.Name == "Guide");
    }

    // ---------------- Milestones 4-5: keyboard & mouse canvas anchors ----------------

    [Theory]
    [InlineData("Key: W", "KeyW")]
    [InlineData("Key: A", "KeyA")]
    [InlineData("Key: Space", "KeySpace")]
    [InlineData("Key: F5", "KeyF5")]
    [InlineData("Numpad 7", "KeyNum7")]
    [InlineData("Key: LShift", "KeyLShift")]
    [InlineData("Key: Numpad Enter", "KeyNumEnter")]
    [InlineData("Key: Page Up", "KeyPgUp")]
    public void CanvasButtonResolver_ResolvesKeyboardAnchors(string alias, string expectedAnchor)
    {
        Assert.Equal(expectedAnchor, CanvasButtonResolver.ResolveAnchor(alias));
    }

    [Theory]
    [InlineData("Mouse: Left Click", "LMB")]
    [InlineData("LMB", "LMB")]
    [InlineData("Mouse: Right Click", "RMB")]
    [InlineData("Mouse 4 (XButton 1)", "Mouse4")]
    [InlineData("MOUSE_BTN_5", "Mouse5")]
    [InlineData("Mouse: Wheel Up", "WheelUp")]
    [InlineData("MOUSE_WHEEL_DOWN", "WheelDown")]
    [InlineData("Mouse Deflection Left", "MDeflectLeft")]
    [InlineData("MOUSE_Y_DOWN", "MDeflectDown")]
    public void CanvasButtonResolver_ResolvesMouseAnchors(string alias, string expectedAnchor)
    {
        Assert.Equal(expectedAnchor, CanvasButtonResolver.ResolveAnchor(alias));
    }

    [Fact]
    public void CanvasButtonResolver_KeyboardAnchors_DoNotCollide_WithGamepadAnchors()
    {
        // Bare gamepad words keep resolving to gamepad anchors...
        Assert.Equal("Guide", CanvasButtonResolver.ResolveAnchor("Home"));
        Assert.Equal("Menu", CanvasButtonResolver.ResolveAnchor("Menu"));
        // ...while keyboard anchors stay distinct.
        Assert.Equal("KeyHome", CanvasButtonResolver.ResolveAnchor("KeyHome"));
        Assert.Equal("KeyMenu", CanvasButtonResolver.ResolveAnchor("Application"));
        Assert.Equal("KeyUp", CanvasButtonResolver.ResolveAnchor("Up Arrow"));
    }

    [Fact]
    public void GetKeyboardInputSources_ContainWASDClusterAndModifiers()
    {
        var sources = MappingChoiceProvider.GetKeyboardInputSources();

        Assert.Contains(sources, s => s.Id == 17 && s.Name == "Key: W" && s.Category == "Keyboard");
        Assert.Contains(sources, s => s.Id == 30 && s.Name == "Key: A" && s.Category == "Keyboard");
        Assert.Contains(sources, s => s.Id == 31 && s.Name == "Key: S" && s.Category == "Keyboard");
        Assert.Contains(sources, s => s.Id == 32 && s.Name == "Key: D" && s.Category == "Keyboard");
        Assert.Contains(sources, s => s.Name == "Key: Space");
        Assert.Contains(sources, s => s.Name == "Key: Enter");
        Assert.Contains(sources, s => s.Name == "Key: LShift");
    }

    [Fact]
    public void GetMouseInputSources_ContainButtonsWheelAndDeflection()
    {
        var sources = MappingChoiceProvider.GetMouseInputSources();

        Assert.Contains(sources, s => s.Id == 248 && s.Name == "Mouse: Left Click" && s.Category == "Mouse");
        Assert.Contains(sources, s => s.Id == 249 && s.Name == "Mouse: Right Click" && s.Category == "Mouse");
        Assert.Contains(sources, s => s.Id == 246 && s.Name == "Mouse: Wheel Up" && s.Category == "Mouse");
        Assert.Contains(sources, s => s.Id == 240 && s.Name == "Mouse: Mouse Deflection Left" && s.Category == "Mouse");
    }

    // ---------------- Milestone 6: profile persistence & target switching roundtrip ----------------

    [Fact]
    public void ApplyTarget_UpdatesExistingVirtualGamepadType()
    {
        var profile = JsonNode.Parse("""
            {
              "devices": {
                "hardware": [ { "id": 1, "name": "gamepad", "type": "standard" } ],
                "virtual": { "gamepads": [ { "id": 1, "type": "360" } ] }
              }
            }
            """) as JsonObject;

        var changed = VirtualTargetCatalog.ApplyTarget(profile!, "ds4");

        Assert.True(changed);
        Assert.Equal("ds4", profile!["devices"]?["virtual"]?["gamepads"]?[0]?["type"]?.GetValue<string>());

        // Re-applying the same target is a no-op
        Assert.False(VirtualTargetCatalog.ApplyTarget(profile!, "ds4"));
    }

    [Fact]
    public void ApplyTarget_CreatesMissingDeviceChain()
    {
        var profile = new JsonObject { ["schemaVersion"] = 3.0 };

        var changed = VirtualTargetCatalog.ApplyTarget(profile, "kbm");

        Assert.True(changed);
        var gamepad = profile["devices"]?["virtual"]?["gamepads"]?[0] as JsonObject;
        Assert.NotNull(gamepad);
        Assert.Equal(1, gamepad!["id"]?.GetValue<int>());
        Assert.Equal("kbm", gamepad["type"]?.GetValue<string>());
    }

    [Fact]
    public void TargetSwitch_RoundTripsThroughSerializedProfileJson()
    {
        var profile = JsonNode.Parse("""
            { "devices": { "virtual": { "gamepads": [ { "id": 1, "type": "360" } ] } } }
            """) as JsonObject;

        foreach (var expected in new[] { "ds4", "xone", "nsw", "kbm", "360" })
        {
            Assert.True(VirtualTargetCatalog.ApplyTarget(profile!, expected));
            var reparsed = JsonNode.Parse(profile!.ToJsonString()) as JsonObject;
            var stored = reparsed!["devices"]?["virtual"]?["gamepads"]?[0]?["type"]?.GetValue<string>();
            Assert.Equal(expected, stored);
            Assert.Equal(expected, VirtualTargetCatalog.ToProfileValue(VirtualTargetCatalog.Parse(stored)!.Value));
        }
    }

    [Fact]
    public void ApplyTarget_NullProfile_ReturnsFalse()
    {
        JsonObject? profile = null;
        Assert.False(VirtualTargetCatalog.ApplyTarget(profile!, "ds4"));
    }
}
