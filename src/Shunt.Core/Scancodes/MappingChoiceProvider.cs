using System;
using System.Collections.Generic;
using System.Linq;

namespace Shunt.Core.Scancodes;

/// <summary>
/// A selectable input control or output target choice for the UI pickers.
/// </summary>
/// <param name="Id">Underlying integer identifier (legacy 1-99 or KeyScanCodeV2 index / command id).</param>
/// <param name="Name">Display name formatted for picker lists and canvas bindings.</param>
/// <param name="Description">Technical description or scancode identifier.</param>
/// <param name="Category">Grouping category (Gamepad, Extended Gamepad, Virtual Gamepad, Keyboard, Mouse, Command).</param>
public record MappingChoice(int Id, string Name, string Description, string Category)
{
    public override string ToString() => Name;
}

/// <summary>
/// Testable registry-driven choice provider feeding the UI pickers from KeyScanCodeTable (plan 08 / Phase 1).
/// </summary>
public static class MappingChoiceProvider
{
    private static readonly Dictionary<int, string> LegacyKeyboardNames = new()
    {
        [15] = "Key: Tab",
        [56] = "Key: LAlt",
        [29] = "Key: LCtrl",
        [42] = "Key: LShift",
        [1] = "Key: Esc",
        [28] = "Key: Enter",
        [57] = "Key: Space",
        [14] = "Key: Backspace",
        [211] = "Key: Delete",
        [201] = "Key: Page Up",
        [209] = "Key: Page Down",
        [199] = "Key: Home",
        [207] = "Key: End",
        [17] = "Key: W",
        [30] = "Key: A",
        [31] = "Key: S",
        [32] = "Key: D",
        [18] = "Key: E",
        [16] = "Key: Q",
        [19] = "Key: R",
        [33] = "Key: F",
        [46] = "Key: C",
        [47] = "Key: V",
        [2] = "Key: 1",
        [3] = "Key: 2",
        [4] = "Key: 3",
        [5] = "Key: 4"
    };

    /// <summary>
    /// Returns all selectable input controls: stable legacy gamepad controls (1-99)
    /// plus the extended entries from KeyScanCodeTable (indices 256-315).
    /// </summary>
    public static IReadOnlyList<MappingChoice> GetInputSources()
    {
        var list = new List<MappingChoice>();

        // 1. Stable legacy gamepad controls verbatim (IDs 1-99)
        list.Add(new(1, "A / Cross", "XB, Stadia: A / DS4: Cross / Nintendo: B", "Gamepad"));
        list.Add(new(2, "B / Circle", "XB, Stadia: B / DS4: Circle / Nintendo: A", "Gamepad"));
        list.Add(new(3, "X / Square", "XB, Stadia: X / DS4: Square / Nintendo: Y", "Gamepad"));
        list.Add(new(4, "Y / Triangle", "XB, Stadia: Y / DS4: Triangle / Nintendo: X", "Gamepad"));
        list.Add(new(5, "Left Shoulder (LB)", "XB: LeftShoulder / DS4, Stadia: L1 / Nintendo: L", "Gamepad"));
        list.Add(new(6, "Right Shoulder (RB)", "XB: RightShoulder / DS4, Stadia: R1 / Nintendo: R", "Gamepad"));
        list.Add(new(7, "View / Share", "XB: View / XB360: Back / DS4: Share / Nintendo: Minus", "Gamepad"));
        list.Add(new(8, "Menu / Options", "XB, Stadia: Menu / XB360: Start / DS4: Options / Nintendo: Plus", "Gamepad"));
        list.Add(new(9, "Left Stick Click (LS)", "XB, Nintendo: LeftStick / DS4, Stadia: L3", "Gamepad"));
        list.Add(new(10, "Right Stick Click (RS)", "XB, Nintendo: RightStick / DS4, Stadia: R3", "Gamepad"));
        list.Add(new(11, "Guide / Home", "XB, Nintendo, Flydigi: Home / DS4: PS", "Gamepad"));
        list.Add(new(33, "D-pad Up", "DpadUp", "Gamepad"));
        list.Add(new(34, "D-pad Down", "DpadDown", "Gamepad"));
        list.Add(new(35, "D-pad Left", "DpadLeft", "Gamepad"));
        list.Add(new(36, "D-pad Right", "DpadRight", "Gamepad"));
        list.Add(new(51, "Left Trigger (LT)", "BtnLeftTrigger", "Gamepad"));
        list.Add(new(55, "Right Trigger (RT)", "BtnRightTrigger", "Gamepad"));

        // DS4 Touchpad Gestures
        list.Add(new(37, "Touchpad Click", "Touchpad Click", "Gamepad"));
        list.Add(new(38, "Touchpad Tap", "Touchpad Tap", "Gamepad"));
        list.Add(new(41, "Touchpad Swipe Up", "Touchpad Swipe Up", "Gamepad"));
        list.Add(new(42, "Touchpad Swipe Down", "Touchpad Swipe Down", "Gamepad"));
        list.Add(new(43, "Touchpad Swipe Left", "Touchpad Swipe Left", "Gamepad"));
        list.Add(new(44, "Touchpad Swipe Right", "Touchpad Swipe Right", "Gamepad"));

        // Motion Sensors & Gyroscope
        list.Add(new(49, "VGYRO (Virtual Gyro Aim)", "Virtual Gyro Aim", "Gamepad"));
        list.Add(new(45, "Tilt Left (Lean Left)", "Tilt Left", "Gamepad"));
        list.Add(new(46, "Tilt Right (Lean Right)", "Tilt Right", "Gamepad"));
        list.Add(new(47, "Tilt Up (Pitch Up)", "Tilt Up", "Gamepad"));
        list.Add(new(48, "Tilt Down (Pitch Down)", "Tilt Down", "Gamepad"));

        // Elite Rear Paddles
        list.Add(new(29, "Paddle P1", "XB: LeftUpperPaddle / JoyCon: Left SL / Flydigi: M4", "Gamepad"));
        list.Add(new(30, "Paddle P2", "XB: RightUpperPaddle / JoyCon: Right SR / Flydigi: M3", "Gamepad"));
        list.Add(new(31, "Paddle P3", "XB, Steam: LeftLowerPaddle / JoyCon: Left SR / NvidiaShield: Minus / Flydigi: M2", "Gamepad"));
        list.Add(new(32, "Paddle P4", "XB, Steam: RightLowerPaddle / JoyCon: Right SL / NvidiaShield: Plus / Flydigi: M1", "Gamepad"));

        // 2. Extended entries from KeyScanCodeTable (indices 256-315)
        foreach (var entry in KeyScanCodeTable.GetCategory(KeyScanCodeCategory.Gamepad))
        {
            list.Add(new(entry.Index, entry.FriendlyName, entry.Name, "Extended Gamepad"));
        }

        foreach (var entry in KeyScanCodeTable.GetCategory(KeyScanCodeCategory.VirtualGamepad))
        {
            list.Add(new(entry.Index, entry.FriendlyName, entry.Name, "Virtual Gamepad"));
        }

        return list;
    }

    /// <summary>
    /// Returns all selectable output targets: verbatim gamepad outputs (17),
    /// full keyboard set (118 standard + 18 consumer), full mouse set (15),
    /// and the 18 system commands as Category="Command".
    /// </summary>
    public static IReadOnlyList<MappingChoice> GetOutputTargets()
    {
        var list = new List<MappingChoice>();

        // 1. Current gamepad outputs verbatim (IDs 1-99)
        list.Add(new(1, "A / Cross", "XB, Stadia: A / DS4: Cross / Nintendo: B", "Gamepad"));
        list.Add(new(2, "B / Circle", "XB, Stadia: B / DS4: Circle / Nintendo: A", "Gamepad"));
        list.Add(new(3, "X / Square", "XB, Stadia: X / DS4: Square / Nintendo: Y", "Gamepad"));
        list.Add(new(4, "Y / Triangle", "XB, Stadia: Y / DS4: Triangle / Nintendo: X", "Gamepad"));
        list.Add(new(5, "Left Shoulder (LB)", "XB: LeftShoulder / DS4, Stadia: L1 / Nintendo: L", "Gamepad"));
        list.Add(new(6, "Right Shoulder (RB)", "XB: RightShoulder / DS4, Stadia: R1 / Nintendo: R", "Gamepad"));
        list.Add(new(7, "View / Share", "XB: View / XB360: Back / DS4: Share / Nintendo: Minus", "Gamepad"));
        list.Add(new(8, "Menu / Options", "XB, Stadia: Menu / XB360: Start / DS4: Options / Nintendo: Plus", "Gamepad"));
        list.Add(new(9, "Left Stick Click (LS)", "XB, Nintendo: LeftStick / DS4, Stadia: L3", "Gamepad"));
        list.Add(new(10, "Right Stick Click (RS)", "XB, Nintendo: RightStick / DS4, Stadia: R3", "Gamepad"));
        list.Add(new(11, "Guide / Home", "XB, Nintendo, Flydigi: Home / DS4: PS", "Gamepad"));
        list.Add(new(33, "D-pad Up", "DpadUp", "Gamepad"));
        list.Add(new(34, "D-pad Down", "DpadDown", "Gamepad"));
        list.Add(new(35, "D-pad Left", "DpadLeft", "Gamepad"));
        list.Add(new(36, "D-pad Right", "DpadRight", "Gamepad"));
        list.Add(new(51, "Left Trigger (LT)", "BtnLeftTrigger", "Gamepad"));
        list.Add(new(55, "Right Trigger (RT)", "BtnRightTrigger", "Gamepad"));

        // 2. DS4 Touchpad Actions (Outputs for games requiring touchpad gestures)
        list.Add(new(37, "Touchpad Click", "Touchpad Click", "Touchpad"));
        list.Add(new(38, "Touchpad Tap", "Touchpad Tap", "Touchpad"));
        list.Add(new(41, "Touchpad Swipe Up", "Touchpad Swipe Up", "Touchpad"));
        list.Add(new(42, "Touchpad Swipe Down", "Touchpad Swipe Down", "Touchpad"));
        list.Add(new(43, "Touchpad Swipe Left", "Touchpad Swipe Left", "Touchpad"));
        list.Add(new(44, "Touchpad Swipe Right", "Touchpad Swipe Right", "Touchpad"));

        // 3. Motion Sensors & Gyroscope Targets
        list.Add(new(49, "VGYRO (Virtual Gyro Aim)", "Virtual Gyro Aim", "Gyro & Motion"));
        list.Add(new(45, "Tilt Left (Lean Left)", "Tilt Left", "Gyro & Motion"));
        list.Add(new(46, "Tilt Right (Lean Right)", "Tilt Right", "Gyro & Motion"));
        list.Add(new(47, "Tilt Up (Pitch Up)", "Tilt Up", "Gyro & Motion"));
        list.Add(new(48, "Tilt Down (Pitch Down)", "Tilt Down", "Gyro & Motion"));
        list.Add(new(304, "Gyro Tilt Up", "BTN_GYRO_TILT_UP", "Gyro & Motion"));
        list.Add(new(305, "Gyro Tilt Down", "BTN_GYRO_TILT_DOWN", "Gyro & Motion"));
        list.Add(new(306, "Gyro Tilt Right", "BTN_GYRO_TILT_RIGHT", "Gyro & Motion"));
        list.Add(new(307, "Gyro Tilt Left", "BTN_GYRO_TILT_LEFT", "Gyro & Motion"));
        list.Add(new(308, "Gyro Lean Left", "BTN_GYRO_LEAN_LEFT", "Gyro & Motion"));
        list.Add(new(309, "Gyro Lean Right", "BTN_GYRO_LEAN_RIGHT", "Gyro & Motion"));

        // 4. Rear Paddles
        list.Add(new(29, "Paddle P1", "XB: LeftUpperPaddle / JoyCon: Left SL", "Paddles"));
        list.Add(new(30, "Paddle P2", "XB: RightUpperPaddle / JoyCon: Right SR", "Paddles"));
        list.Add(new(31, "Paddle P3", "XB: LeftLowerPaddle / JoyCon: Left SR", "Paddles"));
        list.Add(new(32, "Paddle P4", "XB: RightLowerPaddle / JoyCon: Right SL", "Paddles"));

        // 2. Full keyboard set (118 standard + 18 consumer) projected as "Key: ..."
        foreach (var entry in KeyScanCodeTable.GetCategory(KeyScanCodeCategory.KeyboardStandart))
        {
            string displayName = LegacyKeyboardNames.TryGetValue(entry.Index, out var legacy)
                ? legacy
                : $"Key: {entry.FriendlyName}";
            list.Add(new(entry.Index, displayName, entry.Name, "Keyboard"));
        }

        foreach (var entry in KeyScanCodeTable.GetCategory(KeyScanCodeCategory.KeyboardConsumer))
        {
            string friendly = entry.FriendlyName.Equals("Mute", StringComparison.OrdinalIgnoreCase)
                ? "Volume Mute"
                : entry.FriendlyName;
            list.Add(new(entry.Index, $"Key: {friendly}", entry.Name, "Keyboard"));
        }

        // 3. Full mouse set (15 entries from KeyScanCodeTable)
        var mouseEntries = KeyScanCodeTable.GetCategory(KeyScanCodeCategory.MouseDigital)
            .Concat(KeyScanCodeTable.GetCategory(KeyScanCodeCategory.MouseAnalog));
        foreach (var entry in mouseEntries)
        {
            string name = entry.FriendlyName switch
            {
                "Mouse Left Click" => "Mouse: Left Click",
                "Mouse Right Click" => "Mouse: Right Click",
                "Mouse Middle Click" => "Mouse: Middle Click",
                "Wheel Up" => "Mouse: Wheel Up",
                "Wheel Down" => "Mouse: Wheel Down",
                _ => $"Mouse: {entry.FriendlyName}"
            };
            string desc = entry.Name switch
            {
                "MOUSE_BTN_1" => "Mouse Left",
                "MOUSE_BTN_2" => "Mouse Right",
                "MOUSE_BTN_3" => "Mouse Middle",
                "MOUSE_WHEEL_UP" => "wheel up",
                "MOUSE_WHEEL_DOWN" => "wheel down",
                _ => entry.FriendlyName
            };
            list.Add(new(entry.Index, name, desc, "Mouse"));
        }

        // 4. System commands (18 entries as Category="Command")
        foreach (var cmd in KeyScanCodeTable.GetCategory(KeyScanCodeCategory.RewasdUserCommand))
        {
            list.Add(new(cmd.Index, $"Cmd: {cmd.FriendlyName}", cmd.Name, "Command"));
        }

        return list;
    }
}
