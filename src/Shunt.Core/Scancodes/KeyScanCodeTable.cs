using System;
using System.Collections.Generic;
using System.Linq;

namespace Shunt.Core.Scancodes;

/// <summary>
/// Input category classification matching reWASD's
/// <c>reWASDCommon.Infrastructure.Enums.KeyScanCodeCategory</c> (plan 08 §1).
/// </summary>
public enum KeyScanCodeCategory
{
    KeyboardStandart,
    KeyboardConsumer,
    KeyboardExtended,
    MouseDigital,
    MouseAnalog,
    Gamepad,
    VirtualGamepad,
    RewasdUserCommand
}

/// <summary>
/// A single indexed scancode definition (DirectInput code, Virtual-Key code, friendly name).
/// </summary>
/// <param name="Index">reWASD KeyScanCodeV2 table index (equals the DirectInput scan code).</param>
public record KeyScanCodeEntry(
    int Index,
    string Name,
    string FriendlyName,
    KeyScanCodeCategory Category,
    ushort DirectInputCode,
    ushort VirtualKey)
{
    public bool IsInvalidKey => VirtualKey == 0;
}

/// <summary>
/// The exhaustive KeyScanCodeV2 registry: keyboard, mouse, controller outputs and
/// system commands indexed exactly like the official reWASD binaries (plan 08 §2).
/// </summary>
public static class KeyScanCodeTable
{
    private static readonly Dictionary<int, KeyScanCodeEntry> ByIndex;
    private static readonly Dictionary<string, KeyScanCodeEntry> ByName;

    static KeyScanCodeTable()
    {
        var entries = new List<KeyScanCodeEntry>();
        entries.AddRange(BuildStandardKeyboard());
        entries.AddRange(BuildConsumerKeys());
        entries.AddRange(BuildMouse());
        entries.AddRange(BuildGamepadOutputs());
        entries.AddRange(BuildSystemCommands());

        // RewasdUserCommand ids (1-18) live in their own ID space and deliberately collide
        // with keyboard scancode indices, so they are excluded from the index map.
        ByIndex = entries
            .Where(e => e.Category != KeyScanCodeCategory.RewasdUserCommand)
            .ToDictionary(e => e.Index);
        ByName = new Dictionary<string, KeyScanCodeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            ByName[e.Name] = e;
            if (!ByName.ContainsKey(e.FriendlyName)) ByName[e.FriendlyName] = e;
        }
    }

    /// <summary>All registered scancode entries.</summary>
    public static IReadOnlyList<KeyScanCodeEntry> All { get; } = BuildAll();

    private static IReadOnlyList<KeyScanCodeEntry> BuildAll()
    {
        var entries = new List<KeyScanCodeEntry>();
        entries.AddRange(BuildStandardKeyboard());
        entries.AddRange(BuildConsumerKeys());
        entries.AddRange(BuildMouse());
        entries.AddRange(BuildGamepadOutputs());
        entries.AddRange(BuildSystemCommands());
        return entries;
    }

    /// <summary>Resolves an entry by its table index.</summary>
    public static KeyScanCodeEntry? GetByIndex(int index) => ByIndex.GetValueOrDefault(index);

    /// <summary>Resolves an entry by scancode name or friendly name (case-insensitive).</summary>
    public static KeyScanCodeEntry? GetByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return ByName.GetValueOrDefault(name.Trim());
    }

    /// <summary>All entries in a category.</summary>
    public static IReadOnlyList<KeyScanCodeEntry> GetCategory(KeyScanCodeCategory category) =>
        All.Where(e => e.Category == category).ToList();

    /// <summary>Resolves a system command id (1-18) to its entry.</summary>
    public static KeyScanCodeEntry? GetCommand(int commandId) =>
        All.FirstOrDefault(e => e.Category == KeyScanCodeCategory.RewasdUserCommand && e.Index == commandId);

    // ------------------------------------------------------------------
    // 2.1 Standard keyboard (118 keys): Esc..Apps, F1..F24
    // ------------------------------------------------------------------
    private static IEnumerable<KeyScanCodeEntry> BuildStandardKeyboard()
    {
        const KeyScanCodeCategory cat = KeyScanCodeCategory.KeyboardStandart;

        yield return new(1, "DIK_ESCAPE", "Esc", cat, 0x01, 0x1B);
        var digits = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        var digitVks = new ushort[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 };
        for (int i = 0; i < digits.Length; i++)
            yield return new(2 + i, $"DIK_{digits[i]}", digits[i], cat, (ushort)(0x02 + i), digitVks[i]);
        yield return new(12, "DIK_MINUS", "-", cat, 0x0C, 0xBD);
        yield return new(13, "DIK_EQUALS", "=", cat, 0x0D, 0xBB);
        yield return new(14, "DIK_BACK", "Backspace", cat, 0x0E, 0x08);
        yield return new(15, "DIK_TAB", "Tab", cat, 0x0F, 0x09);
        var upperRow = ("QWERTYUIOP", new ushort[] { 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50 });
        foreach (var (key, i) in upperRow.Item1.Select((k, i) => (k, i)))
            yield return new(16 + i, $"DIK_{key}", key.ToString(), cat, (ushort)(0x10 + i), upperRow.Item2[i]);
        yield return new(26, "DIK_LBRACKET", "[", cat, 0x1A, 0xDB);
        yield return new(27, "DIK_RBRACKET", "]", cat, 0x1B, 0xDD);
        yield return new(28, "DIK_RETURN", "Enter", cat, 0x1C, 0x0D);
        yield return new(29, "DIK_LCONTROL", "Left Ctrl", cat, 0x1D, 0xA2);
        var homeRow = ("ASDFGHJKL", new ushort[] { 0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C });
        foreach (var (key, i) in homeRow.Item1.Select((k, i) => (k, i)))
            yield return new(30 + i, $"DIK_{key}", key.ToString(), cat, (ushort)(0x1E + i), homeRow.Item2[i]);
        yield return new(39, "DIK_SEMICOLON", ";", cat, 0x27, 0xBA);
        yield return new(40, "DIK_APOSTROPHE", "'", cat, 0x28, 0xDE);
        yield return new(41, "DIK_GRAVE", "`", cat, 0x29, 0xC0);
        yield return new(42, "DIK_LSHIFT", "Left Shift", cat, 0x2A, 0xA0);
        yield return new(43, "DIK_BACKSLASH", "\\", cat, 0x2B, 0xDC);
        var lowerRow = ("ZXCVBNM", new ushort[] { 0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D });
        foreach (var (key, i) in lowerRow.Item1.Select((k, i) => (k, i)))
            yield return new(44 + i, $"DIK_{key}", key.ToString(), cat, (ushort)(0x2C + i), lowerRow.Item2[i]);
        yield return new(51, "DIK_COMMA", ",", cat, 0x33, 0xBC);
        yield return new(52, "DIK_PERIOD", ".", cat, 0x34, 0xBE);
        yield return new(53, "DIK_SLASH", "/", cat, 0x35, 0xBF);
        yield return new(54, "DIK_RSHIFT", "Right Shift", cat, 0x36, 0xA1);
        yield return new(55, "DIK_MULTIPLY", "Numpad *", cat, 0x37, 0x6A);
        yield return new(56, "DIK_LMENU", "Left Alt", cat, 0x38, 0xA4);
        yield return new(57, "DIK_SPACE", "Spacebar", cat, 0x39, 0x20);
        yield return new(58, "DIK_CAPITAL", "Caps Lock", cat, 0x3A, 0x14);
        for (int i = 0; i < 10; i++)
            yield return new(59 + i, $"DIK_F{i + 1}", $"F{i + 1}", cat, (ushort)(0x3B + i), (ushort)(0x70 + i));
        yield return new(69, "DIK_NUMLOCK", "NumLock", cat, 0x45, 0x90);
        yield return new(70, "DIK_SCROLL", "Scroll Lock", cat, 0x46, 0x91);
        yield return new(71, "DIK_NUMPAD7", "Numpad 7", cat, 0x47, 0x67);
        yield return new(72, "DIK_NUMPAD8", "Numpad 8", cat, 0x48, 0x68);
        yield return new(73, "DIK_NUMPAD9", "Numpad 9", cat, 0x49, 0x69);
        yield return new(74, "DIK_SUBTRACT", "Numpad -", cat, 0x4A, 0x6D);
        yield return new(75, "DIK_NUMPAD4", "Numpad 4", cat, 0x4B, 0x64);
        yield return new(76, "DIK_NUMPAD5", "Numpad 5", cat, 0x4C, 0x65);
        yield return new(77, "DIK_NUMPAD6", "Numpad 6", cat, 0x4D, 0x66);
        yield return new(78, "DIK_ADD", "Numpad +", cat, 0x4E, 0x6B);
        yield return new(79, "DIK_NUMPAD1", "Numpad 1", cat, 0x4F, 0x61);
        yield return new(80, "DIK_NUMPAD2", "Numpad 2", cat, 0x50, 0x62);
        yield return new(81, "DIK_NUMPAD3", "Numpad 3", cat, 0x51, 0x63);
        yield return new(82, "DIK_NUMPAD0", "Numpad 0", cat, 0x52, 0x60);
        yield return new(83, "DIK_DECIMAL", "Numpad .", cat, 0x53, 0x6E);
        yield return new(86, "DIK_OEM_102", "<> (ISO)", cat, 0x56, 0xE2);
        yield return new(87, "DIK_F11", "F11", cat, 0x57, 0x7A);
        yield return new(88, "DIK_F12", "F12", cat, 0x58, 0x7B);
        for (int i = 0; i < 11; i++)
            yield return new(100 + i, $"DIK_F{i + 13}", $"F{i + 13}", cat, (ushort)(0x64 + i), (ushort)(0x7C + i));
        yield return new(118, "DIK_F24", "F24", cat, 0x76, 0x87);
        yield return new(156, "DIK_NUMPADENTER", "Numpad Enter", cat, 0x9C, 0x0D);
        yield return new(157, "DIK_RCONTROL", "Right Ctrl", cat, 0x9D, 0xA3);
        yield return new(181, "DIK_DIVIDE", "Numpad /", cat, 0xB5, 0x6F);
        yield return new(183, "DIK_SYSRQ", "Print Screen", cat, 0xB7, 0x00);
        yield return new(184, "DIK_RMENU", "Right Alt (AltGr)", cat, 0xB8, 0xA5);
        yield return new(197, "DIK_PAUSE", "Pause", cat, 0xC5, 0x13);
        yield return new(198, "DIK_BREAK", "Break", cat, 0xC6, 0x00);
        yield return new(199, "DIK_HOME", "Home", cat, 0xC7, 0x24);
        yield return new(200, "DIK_UP", "Up Arrow", cat, 0xC8, 0x26);
        yield return new(201, "DIK_PRIOR", "Page Up", cat, 0xC9, 0x21);
        yield return new(203, "DIK_LEFT", "Left Arrow", cat, 0xCB, 0x25);
        yield return new(205, "DIK_RIGHT", "Right Arrow", cat, 0xCD, 0x27);
        yield return new(207, "DIK_END", "End", cat, 0xCF, 0x23);
        yield return new(208, "DIK_DOWN", "Down Arrow", cat, 0xD0, 0x28);
        yield return new(209, "DIK_NEXT", "Page Down", cat, 0xD1, 0x22);
        yield return new(210, "DIK_INSERT", "Insert", cat, 0xD2, 0x2D);
        yield return new(211, "DIK_DELETE", "Delete", cat, 0xD3, 0x2E);
        yield return new(219, "DIK_LWIN", "Left Windows", cat, 0xDB, 0x5B);
        yield return new(220, "DIK_RWIN", "Right Windows", cat, 0xDC, 0x5C);
        yield return new(221, "DIK_APPS", "Application / Menu", cat, 0xDD, 0x5D);
    }

    // ------------------------------------------------------------------
    // 2.2 Consumer & multimedia keys (18 keys)
    // ------------------------------------------------------------------
    private static IEnumerable<KeyScanCodeEntry> BuildConsumerKeys()
    {
        const KeyScanCodeCategory cat = KeyScanCodeCategory.KeyboardConsumer;
        return new[]
        {
            new KeyScanCodeEntry(144, "DIK_PREVTRACK", "Previous Track", cat, 0x90, 0xB1),
            new KeyScanCodeEntry(153, "DIK_NEXTTRACK", "Next Track", cat, 0x99, 0xB0),
            new KeyScanCodeEntry(160, "DIK_MUTE", "Mute", cat, 0xA0, 0xAD),
            new KeyScanCodeEntry(161, "DIK_CALCULATOR", "Calculator", cat, 0xA1, 0x00),
            new KeyScanCodeEntry(162, "DIK_PLAYPAUSE", "Play / Pause", cat, 0xA2, 0xB3),
            new KeyScanCodeEntry(164, "DIK_MEDIASTOP", "Stop Media", cat, 0xA4, 0xB2),
            new KeyScanCodeEntry(174, "DIK_VOLUMEDOWN", "Volume Down", cat, 0xAE, 0xAE),
            new KeyScanCodeEntry(176, "DIK_VOLUMEUP", "Volume Up", cat, 0xB0, 0xAF),
            new KeyScanCodeEntry(178, "DIK_WEBHOME", "Browser Home", cat, 0xB2, 0xAC),
            new KeyScanCodeEntry(229, "DIK_WEBSEARCH", "Browser Search", cat, 0xE5, 0xAA),
            new KeyScanCodeEntry(230, "DIK_WEBFAVORITES", "Browser Favorites", cat, 0xE6, 0xAB),
            new KeyScanCodeEntry(231, "DIK_WEBREFRESH", "Browser Refresh", cat, 0xE7, 0xA8),
            new KeyScanCodeEntry(232, "DIK_WEBSTOP", "Browser Stop", cat, 0xE8, 0xA9),
            new KeyScanCodeEntry(233, "DIK_WEBFORWARD", "Browser Forward", cat, 0xE9, 0xA7),
            new KeyScanCodeEntry(234, "DIK_WEBBACK", "Browser Back", cat, 0xEA, 0xA6),
            new KeyScanCodeEntry(235, "DIK_MYCOMPUTER", "My Computer", cat, 0xEB, 0x00),
            new KeyScanCodeEntry(236, "DIK_MAIL", "Mail", cat, 0xEC, 0xB4),
            new KeyScanCodeEntry(237, "DIK_MEDIASELECT", "Media Select", cat, 0xED, 0xB5)
        };
    }

    // ------------------------------------------------------------------
    // 2.3 Mouse buttons & movement (15 items)
    // ------------------------------------------------------------------
    private static IEnumerable<KeyScanCodeEntry> BuildMouse()
    {
        return new[]
        {
            new KeyScanCodeEntry(240, "MOUSE_X_LEFT", "Mouse Deflection Left", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(241, "MOUSE_X_RIGHT", "Mouse Deflection Right", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(242, "MOUSE_Y_UP", "Mouse Deflection Up", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(243, "MOUSE_Y_DOWN", "Mouse Deflection Down", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(244, "MOUSE_WHEEL_LEFT", "Wheel Tilt Left", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(245, "MOUSE_WHEEL_RIGHT", "Wheel Tilt Right", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(246, "MOUSE_WHEEL_UP", "Wheel Up", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(247, "MOUSE_WHEEL_DOWN", "Wheel Down", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(248, "MOUSE_BTN_1", "Mouse Left Click", KeyScanCodeCategory.MouseDigital, 0, 0x01),
            new KeyScanCodeEntry(249, "MOUSE_BTN_2", "Mouse Right Click", KeyScanCodeCategory.MouseDigital, 0, 0x02),
            new KeyScanCodeEntry(250, "MOUSE_BTN_3", "Mouse Middle Click", KeyScanCodeCategory.MouseDigital, 0, 0x04),
            new KeyScanCodeEntry(251, "MOUSE_BTN_4", "Mouse 4 (XButton 1)", KeyScanCodeCategory.MouseDigital, 0, 0x05),
            new KeyScanCodeEntry(252, "MOUSE_BTN_5", "Mouse 5 (XButton 2)", KeyScanCodeCategory.MouseDigital, 0, 0x06),
            new KeyScanCodeEntry(301, "MOUSE_FLICK_LEFT", "Flick Stick Left", KeyScanCodeCategory.MouseAnalog, 0, 0),
            new KeyScanCodeEntry(302, "MOUSE_FLICK_RIGHT", "Flick Stick Right", KeyScanCodeCategory.MouseAnalog, 0, 0)
        };
    }

    // ------------------------------------------------------------------
    // 2.4 Controller outputs & virtual motion (57 items)
    // ------------------------------------------------------------------
    private static IEnumerable<KeyScanCodeEntry> BuildGamepadOutputs()
    {
        var gamepad = new (int Index, string Name, string Friendly)[]
        {
            (256, "BTN_A", "Face Button A / Cross"),
            (257, "BTN_B", "Face Button B / Circle"),
            (258, "BTN_X", "Face Button X / Square"),
            (259, "BTN_Y", "Face Button Y / Triangle"),
            (260, "BTN_LEFT_SHOULDER", "Left Bumper (LB/L1)"),
            (261, "BTN_RIGHT_SHOULDER", "Right Bumper (RB/R1)"),
            (262, "BTN_BACK", "View / Share / Minus"),
            (263, "BTN_START", "Menu / Options / Plus"),
            (264, "BTN_LEFT_THUMB", "Left Stick Click (L3)"),
            (265, "BTN_RIGHT_THUMB", "Right Stick Click (R3)"),
            (266, "BTN_XBOX", "Guide / PS / Home"),
            (267, "BTN_CAPTURE", "Capture / Share Button"),
            (269, "BTN_DPAD_UP", "D-Pad Up"),
            (270, "BTN_DPAD_DOWN", "D-Pad Down"),
            (271, "BTN_DPAD_LEFT", "D-Pad Left"),
            (272, "BTN_DPAD_RIGHT", "D-Pad Right"),
            (273, "BTN_LEFT_TRIGGER", "Left Trigger"),
            (274, "BTN_RIGHT_TRIGGER", "Right Trigger"),
            (275, "BTN_LEFT_THUMBSTICK_UP", "Left Stick Up"),
            (276, "BTN_LEFT_THUMBSTICK_DOWN", "Left Stick Down"),
            (277, "BTN_LEFT_THUMBSTICK_LEFT", "Left Stick Left"),
            (278, "BTN_LEFT_THUMBSTICK_RIGHT", "Left Stick Right"),
            (279, "BTN_RIGHT_THUMBSTICK_UP", "Right Stick Up"),
            (280, "BTN_RIGHT_THUMBSTICK_DOWN", "Right Stick Down"),
            (281, "BTN_RIGHT_THUMBSTICK_LEFT", "Right Stick Left"),
            (282, "BTN_RIGHT_THUMBSTICK_RIGHT", "Right Stick Right"),
            (303, "BTN_TOUCH_CLICK", "Touchpad Click"),
            (283, "BTN_TOUCHPAD_TAP_LEFT", "Touchpad Tap Left"),
            (284, "BTN_TOUCHPAD_TAP_RIGHT", "Touchpad Tap Right"),
            (285, "BTN_TOUCHPAD_TAP_CENTER", "Touchpad Tap Center"),
            (286, "BTN_TOUCHPAD_MULTI_TAP", "Touchpad Multi Tap"),
            (287, "BTN_TOUCHPAD_ZOOM_IN", "Touchpad Zoom In"),
            (288, "BTN_TOUCHPAD_ZOOM_OUT", "Touchpad Zoom Out"),
            (289, "BTN_TOUCHPAD_SWIPE_ONE_FINGER_UP", "1-Finger Swipe Up"),
            (290, "BTN_TOUCHPAD_SWIPE_ONE_FINGER_DOWN", "1-Finger Swipe Down"),
            (291, "BTN_TOUCHPAD_SWIPE_ONE_FINGER_LEFT", "1-Finger Swipe Left"),
            (292, "BTN_TOUCHPAD_SWIPE_ONE_FINGER_RIGHT", "1-Finger Swipe Right"),
            (293, "BTN_TOUCHPAD_SWIPE_TWO_FINGERS_UP", "2-Finger Swipe Up"),
            (294, "BTN_TOUCHPAD_SWIPE_TWO_FINGERS_DOWN", "2-Finger Swipe Down"),
            (295, "BTN_TOUCHPAD_SWIPE_TWO_FINGERS_LEFT", "2-Finger Swipe Left"),
            (296, "BTN_TOUCHPAD_SWIPE_TWO_FINGERS_RIGHT", "2-Finger Swipe Right"),
            (297, "BTN_TOUCH_LEFT", "Touch Surface Left"),
            (298, "BTN_TOUCH_UP", "Touch Surface Up"),
            (299, "BTN_TOUCH_RIGHT", "Touch Surface Right"),
            (300, "BTN_TOUCH_DOWN", "Touch Surface Down"),
            (304, "BTN_GYRO_TILT_UP", "Gyro Tilt Up"),
            (305, "BTN_GYRO_TILT_DOWN", "Gyro Tilt Down"),
            (306, "BTN_GYRO_TILT_RIGHT", "Gyro Tilt Right"),
            (307, "BTN_GYRO_TILT_LEFT", "Gyro Tilt Left"),
            (308, "BTN_GYRO_LEAN_LEFT", "Gyro Lean Left"),
            (309, "BTN_GYRO_LEAN_RIGHT", "Gyro Lean Right"),
            (310, "BTN_ACCEL_X_PLUS", "Accelerometer X+ Shake"),
            (311, "BTN_ACCEL_X_MINUS", "Accelerometer X- Shake"),
            (312, "BTN_ACCEL_Y_PLUS", "Accelerometer Y+ Shake"),
            (313, "BTN_ACCEL_Y_MINUS", "Accelerometer Y- Shake"),
            (314, "BTN_ACCEL_Z_PLUS", "Accelerometer Z+ Shake"),
            (315, "BTN_ACCEL_Z_MINUS", "Accelerometer Z- Shake")
        };
        foreach (var (index, name, friendly) in gamepad)
        {
            var cat = name.StartsWith("BTN_TOUCH") || name.Contains("GYRO") || name.Contains("ACCEL")
                ? KeyScanCodeCategory.VirtualGamepad
                : KeyScanCodeCategory.Gamepad;
            yield return new KeyScanCodeEntry(index, name, friendly, cat, 0, 0);
        }
    }

    // ------------------------------------------------------------------
    // 2.5 System commands (18 commands)
    // ------------------------------------------------------------------
    private static IEnumerable<KeyScanCodeEntry> BuildSystemCommands()
    {
        const KeyScanCodeCategory cat = KeyScanCodeCategory.RewasdUserCommand;
        return new[]
        {
            new KeyScanCodeEntry(1, "TurnRemapOff", "Turn remap OFF", cat, 0, 0),
            new KeyScanCodeEntry(2, "TurnWirelessGamepadOff", "Turn wireless gamepad off", cat, 0, 0),
            new KeyScanCodeEntry(3, "CloseRewasdGui", "Close reWASD GUI", cat, 0, 0),
            new KeyScanCodeEntry(4, "GyroSwitch", "Switch Gyro steering axes", cat, 0, 0),
            new KeyScanCodeEntry(5, "TiltReset", "Reset Tilt", cat, 0, 0),
            new KeyScanCodeEntry(6, "GyroTiltToggleOnOff", "Toggle Gyro On/Off", cat, 0, 0),
            new KeyScanCodeEntry(7, "Logout", "Log out", cat, 0, 0),
            new KeyScanCodeEntry(8, "Shutdown", "Shut down", cat, 0, 0),
            new KeyScanCodeEntry(9, "Sleep", "Sleep", cat, 0, 0),
            new KeyScanCodeEntry(10, "AntibossMode", "Turn Anti-boss mode", cat, 0, 0),
            new KeyScanCodeEntry(11, "Screenshot", "Take a screenshot", cat, 0, 0),
            new KeyScanCodeEntry(12, "CloseActiveTask", "Close active task", cat, 0, 0),
            new KeyScanCodeEntry(13, "Hibernate", "Hibernate", cat, 0, 0),
            new KeyScanCodeEntry(14, "ReconnectToExternalTarget", "Reconnect to Target via BT", cat, 0, 0),
            new KeyScanCodeEntry(15, "GyroTiltTurnOn", "Turn Gyro On", cat, 0, 0),
            new KeyScanCodeEntry(16, "GyroTiltTurnOff", "Turn Gyro Off", cat, 0, 0),
            new KeyScanCodeEntry(17, "RestartPC", "Restart PC", cat, 0, 0),
            new KeyScanCodeEntry(18, "VirtualGyroReset", "Reset Virtual Gyro", cat, 0, 0)
        };
    }
}
