using System;
using System.Linq;
using Shunt.Core.Scancodes;
using Xunit;

namespace Shunt.Tests;

public class MappingChoiceProviderTests
{
    [Fact]
    public void GetInputSources_ContainsStableLegacyIdsAndExtendedIndices()
    {
        var sources = MappingChoiceProvider.GetInputSources();

        // Must contain legacy gamepad buttons (1-11, 33-36, 51, 55)
        Assert.Contains(sources, s => s.Id == 1 && s.Name == "A / Cross" && s.Category == "Gamepad");
        Assert.Contains(sources, s => s.Id == 2 && s.Name == "B / Circle" && s.Category == "Gamepad");
        Assert.Contains(sources, s => s.Id == 5 && s.Name == "Left Shoulder (LB)" && s.Category == "Gamepad");
        Assert.Contains(sources, s => s.Id == 11 && s.Name == "Guide / Home" && s.Category == "Gamepad");
        Assert.Contains(sources, s => s.Id == 33 && s.Name == "D-pad Up" && s.Category == "Gamepad");
        Assert.Contains(sources, s => s.Id == 51 && s.Name == "Left Trigger (LT)" && s.Category == "Gamepad");

        // Must contain gestures and motion
        Assert.Contains(sources, s => s.Id == 37 && s.Name == "Touchpad Click");
        Assert.Contains(sources, s => s.Id == 41 && s.Name == "Touchpad Swipe Up");
        Assert.Contains(sources, s => s.Id == 45 && s.Name == "Tilt Left (Lean Left)");
        Assert.Contains(sources, s => s.Id == 49 && s.Name == "VGYRO (Virtual Gyro Aim)");

        // Must contain rear paddles
        Assert.Contains(sources, s => s.Id == 29 && s.Name == "Paddle P1");
        Assert.Contains(sources, s => s.Id == 30 && s.Name == "Paddle P2");
        Assert.Contains(sources, s => s.Id == 31 && s.Name == "Paddle P3");
        Assert.Contains(sources, s => s.Id == 32 && s.Name == "Paddle P4");

        // Must contain extended KeyScanCodeV2 gamepad indices (256-315)
        Assert.Contains(sources, s => s.Id == 256 && s.Category == "Extended Gamepad"); // BTN_A
        Assert.Contains(sources, s => s.Id == 267 && s.Category == "Extended Gamepad"); // BTN_CAPTURE
        Assert.Contains(sources, s => s.Id == 283 && s.Category == "Virtual Gamepad");  // Touchpad Tap Left
        Assert.Contains(sources, s => s.Id == 304 && s.Category == "Virtual Gamepad");  // Gyro Tilt Up
        Assert.Contains(sources, s => s.Id == 315 && s.Category == "Virtual Gamepad");  // Accel Z-
    }

    [Fact]
    public void GetInputSources_NamesAreUniquePerCategory()
    {
        var sources = MappingChoiceProvider.GetInputSources();
        var byCategory = sources.GroupBy(s => s.Category);

        foreach (var group in byCategory)
        {
            var names = group.Select(s => s.Name).ToList();
            var distinctNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(distinctNames.Count, names.Count);
        }
    }

    [Fact]
    public void GetOutputTargets_ExactCountsAndLegacyPreserved()
    {
        var targets = MappingChoiceProvider.GetOutputTargets();

        var gamepad = targets.Where(t => t.Category == "Gamepad").ToList();
        var touchpad = targets.Where(t => t.Category == "Touchpad").ToList();
        var gyro = targets.Where(t => t.Category == "Gyro & Motion").ToList();
        var paddles = targets.Where(t => t.Category == "Paddles").ToList();
        var keyboard = targets.Where(t => t.Category == "Keyboard").ToList();
        var mouse = targets.Where(t => t.Category == "Mouse").ToList();
        var commands = targets.Where(t => t.Category == "Command").ToList();

        // Counts: 17 gamepad + 6 touchpad + 11 gyro + 4 paddles + 136 keyboard + 15 mouse + 18 commands = 207
        Assert.Equal(17, gamepad.Count);
        Assert.Equal(6, touchpad.Count);
        Assert.Equal(11, gyro.Count);
        Assert.Equal(4, paddles.Count);
        Assert.Equal(136, keyboard.Count);
        Assert.Equal(15, mouse.Count);
        Assert.Equal(18, commands.Count);
        Assert.Equal(207, targets.Count);

        // Touchpad output targets present
        Assert.Contains(touchpad, t => t.Id == 37 && t.Name == "Touchpad Click");
        Assert.Contains(touchpad, t => t.Id == 38 && t.Name == "Touchpad Tap");
        Assert.Contains(touchpad, t => t.Id == 41 && t.Name == "Touchpad Swipe Up");
        Assert.Contains(touchpad, t => t.Id == 42 && t.Name == "Touchpad Swipe Down");
        Assert.Contains(touchpad, t => t.Id == 43 && t.Name == "Touchpad Swipe Left");
        Assert.Contains(touchpad, t => t.Id == 44 && t.Name == "Touchpad Swipe Right");

        // Gyro and paddle output targets present
        Assert.Contains(gyro, t => t.Id == 49 && t.Name == "VGYRO (Virtual Gyro Aim)");
        Assert.Contains(gyro, t => t.Id == 45 && t.Name == "Tilt Left (Lean Left)");
        Assert.Contains(gyro, t => t.Id == 304 && t.Name == "Gyro Tilt Up");
        Assert.Contains(paddles, t => t.Id == 29 && t.Name == "Paddle P1");
        Assert.Contains(paddles, t => t.Id == 32 && t.Name == "Paddle P4");

        // Stable legacy gamepad outputs preserved verbatim
        Assert.Contains(gamepad, t => t.Id == 1 && t.Name == "A / Cross");
        Assert.Contains(gamepad, t => t.Id == 6 && t.Name == "Right Shoulder (RB)");
        Assert.Contains(gamepad, t => t.Id == 33 && t.Name == "D-pad Up");
        Assert.Contains(gamepad, t => t.Id == 55 && t.Name == "Right Trigger (RT)");

        // Stable legacy keyboard entries preserved
        Assert.Contains(keyboard, t => t.Id == 28 && t.Name == "Key: Enter");
        Assert.Contains(keyboard, t => t.Id == 57 && t.Name == "Key: Space");
        Assert.Contains(keyboard, t => t.Id == 15 && t.Name == "Key: Tab");
        Assert.Contains(keyboard, t => t.Id == 1 && t.Name == "Key: Esc");

        // New keyboard keys present
        Assert.Contains(keyboard, t => t.Id == 100 && t.Name == "Key: F13");
        Assert.Contains(keyboard, t => t.Id == 160 && t.Name == "Key: Volume Mute");
        Assert.Contains(keyboard, t => t.Id == 162 && t.Name == "Key: Play / Pause");

        // Full mouse set present with new indices
        Assert.Contains(mouse, t => t.Id == 248 && t.Name == "Mouse: Left Click");
        Assert.Contains(mouse, t => t.Id == 249 && t.Name == "Mouse: Right Click");
        Assert.Contains(mouse, t => t.Id == 246 && t.Name == "Mouse: Wheel Up");
        Assert.Contains(mouse, t => t.Id == 251 && t.Name.Contains("4 (XButton 1)"));
        Assert.Contains(mouse, t => t.Id == 301 && t.Name == "Mouse: Flick Stick Left");

        // System commands present (18)
        Assert.Contains(commands, t => t.Id == 11 && t.Name == "Cmd: Take a screenshot");
        Assert.Contains(commands, t => t.Id == 10 && t.Name == "Cmd: Turn Anti-boss mode");
        Assert.Contains(commands, t => t.Id == 6 && t.Name == "Cmd: Toggle Gyro On/Off");
    }

    [Fact]
    public void GetOutputTargets_NamesAreUniquePerCategory()
    {
        var targets = MappingChoiceProvider.GetOutputTargets();
        var byCategory = targets.GroupBy(t => t.Category);

        foreach (var group in byCategory)
        {
            var names = group.Select(t => t.Name).ToList();
            var distinctNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(distinctNames.Count, names.Count);
        }
    }
}
