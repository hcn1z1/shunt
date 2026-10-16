using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Shunt.Core.Models;

namespace Shunt.Core.Scancodes;

/// <summary>
/// A rich virtual target controller choice for the header <c>TargetTypeCombo</c>
/// (plan 11 §1): icon glyph, display name, marketing subtitle and the underlying
/// <see cref="VirtualGamepadType"/> selected by <see cref="Value"/> in the profile JSON.
/// </summary>
public record TargetControllerChoice(
    string Value,
    VirtualGamepadType Target,
    string Name,
    string Subtitle,
    string IconGlyph)
{
    public override string ToString() => Name;
}

/// <summary>
/// Registry of the virtual target controllers emulated by Shunt and the profile JSON
/// persistence rule for switching between them (plan 11 §1.1-1.2).
/// </summary>
public static class VirtualTargetCatalog
{
    /// <summary>Canonical profile codes mapped to <see cref="VirtualGamepadType"/>.
    /// Xbox targets first: Xbox 360 is the default emulated controller.</summary>
    private static readonly (string Value, VirtualGamepadType Target, string Name, string Subtitle, string Glyph)[] Definitions =
    [
        ("360", VirtualGamepadType.Xbox360, "Virtual Xbox 360",
            "Universal PC XInput, Dual Motor Haptics", "\uD83C\uDFAE"),
        ("xone", VirtualGamepadType.XboxOne, "Virtual Xbox One",
            "Impulse Triggers, Elite Paddles P1\u2013P4", "\uD83C\uDFAE"),
        ("ds4", VirtualGamepadType.DualShock4, "Virtual DualShock 4",
            "PlayStation Touchpad, 6-Axis IMU, Lightbar", "\uD83C\uDFAE"),
        ("ds3", VirtualGamepadType.DualShock3, "Virtual DualShock 3",
            "Pressure-Sensitive Face Buttons & D-Pad", "\uD83C\uDFAE"),
        ("nsw", VirtualGamepadType.SwitchPro, "Virtual Switch Pro",
            "Motion Aiming, Capture Button, HD Rumble", "\uD83C\uDFAE"),
        ("kbm", VirtualGamepadType.KeyboardAndMouse, "Virtual Keyboard & Mouse",
            "Desktop Keystrokes, Relative Cursor, Clicks", "\u2328\uFE0F")
    ];

    /// <summary>Standard-user target families shown in the top-level selector.</summary>
    public static IReadOnlyList<TargetControllerChoice> Choices { get; } =
    [
        new("360", VirtualGamepadType.Xbox360, "Xbox Family", "Xbox 360 / Xbox One / Elite", "🎮"),
        new("ds4", VirtualGamepadType.DualShock4, "PlayStation Family", "DualShock 4 / DualShock 3", "🎮"),
        new("nsw", VirtualGamepadType.SwitchPro, "Nintendo Switch Pro", "Motion aiming, capture button", "🎮"),
        new("kbm", VirtualGamepadType.KeyboardAndMouse, "Desktop Output", "Keyboard and mouse", "⌨️")
    ];

    /// <summary>Returns the family choice matching a persisted target variant.</summary>
    public static TargetControllerChoice? GetFamilyChoice(string? value)
    {
        var target = Parse(value);
        if (target is null) return null;
        var familyValue = target.Value switch
        {
            VirtualGamepadType.Xbox360 or VirtualGamepadType.XboxOne => "360",
            VirtualGamepadType.DualShock4 or VirtualGamepadType.DualShock3 => "ds4",
            VirtualGamepadType.SwitchPro => "nsw",
            VirtualGamepadType.KeyboardAndMouse => "kbm",
            _ => null
        };
        return Choices.FirstOrDefault(choice => choice.Value == familyValue);
    }

    /// <summary>Parses a canonical profile code ("ds4", "360", "xone", "nsw", "ds3", "kbm")
    /// (case-insensitive) into a <see cref="VirtualGamepadType"/>; returns null when unknown.</summary>
    public static VirtualGamepadType? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        foreach (var definition in Definitions)
        {
            if (string.Equals(definition.Value, trimmed, StringComparison.OrdinalIgnoreCase))
                return definition.Target;
        }

        // Also accept enum-name forms ("DualShock4", "KeyboardAndMouse", ...)
        foreach (var definition in Definitions)
        {
            if (string.Equals(definition.Target.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                return definition.Target;
        }
        return null;
    }

    /// <summary>Canonical profile code for a target type, or null for unknown types.</summary>
    public static string? ToProfileValue(VirtualGamepadType target) =>
        Definitions.FirstOrDefault(definition => definition.Target == target).Value;

    /// <summary>
    /// Persists a virtual target switch into <c>devices.virtual.gamepads[0].type</c>,
    /// creating the <c>devices/virtual/gamepads</c> chain when missing (plan 11 §1.2 step 1).
    /// Returns true when the profile JSON was modified.
    /// </summary>
    public static bool ApplyTarget(JsonObject profile, string targetValue)
    {
        if (profile is null) return false;
        if (profile["devices"] is not JsonObject devices)
        {
            devices = new JsonObject();
            profile["devices"] = devices;
        }
        if (devices["virtual"] is not JsonObject virtualDevices)
        {
            virtualDevices = new JsonObject();
            devices["virtual"] = virtualDevices;
        }
        if (virtualDevices["gamepads"] is not JsonArray gamepads)
        {
            gamepads = new JsonArray();
            virtualDevices["gamepads"] = gamepads;
        }

        if (gamepads.Count == 0)
        {
            gamepads.Add(new JsonObject { ["id"] = 1, ["type"] = targetValue });
            return true;
        }

        if (gamepads[0] is not JsonObject gamepad) return false;
        var current = gamepad["type"]?.GetValue<string>();
        if (string.Equals(current, targetValue, StringComparison.OrdinalIgnoreCase)) return false;
        gamepad["type"] = targetValue;
        return true;
    }
}
