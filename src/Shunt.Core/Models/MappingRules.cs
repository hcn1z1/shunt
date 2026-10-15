using System;
using System.Collections.Generic;

namespace Shunt.Core.Models;

/// <summary>
/// Activator modes matching the 6 reWASD activator conditions.
/// </summary>
public enum MappingActivator
{
    Single,
    Double,
    Triple,
    Long,
    Start,
    Release
}

/// <summary>
/// Output destination category for a remapping rule.
/// </summary>
public enum TargetOutputType
{
    Gamepad,
    Keyboard,
    Mouse,
    Command
}

/// <summary>
/// Executable keyboard key definition with scan code and friendly key name.
/// </summary>
public record KeyboardKeyAction(int ScanCode, int VirtualKey, string KeyName);

/// <summary>
/// Executable mouse action types.
/// </summary>
public enum MouseActionType
{
    None,
    LeftClick,
    RightClick,
    MiddleClick,
    WheelUp,
    WheelDown,
    MoveAim
}

/// <summary>
/// A resolved, executable remapping rule mapping source trigger buttons/chords
/// to output buttons (Xbox/DS4), keyboard keys, mouse clicks/aim, shift changes, or turbo/toggle modifiers.
/// </summary>
public record ResolvedMappingRule
{
    public GamepadButtons TriggerButtons { get; init; } = GamepadButtons.None;
    public GamepadButtons OutputButtons { get; init; } = GamepadButtons.None;
    public TargetOutputType TargetType { get; init; } = TargetOutputType.Gamepad;
    public KeyboardKeyAction? OutputKey { get; init; }
    public MouseActionType OutputMouse { get; init; } = MouseActionType.None;
    public int? OutputCommandId { get; init; }
    public bool Toggle { get; init; } = false;
    public bool Turbo { get; init; } = false;
    public int? TargetShiftId { get; init; }
    public MappingActivator Activator { get; init; } = MappingActivator.Single;
    public int HoldThresholdMs { get; init; } = 400;
    public int TapWindowMs { get; init; } = 200;
    public int ShiftLayerId { get; init; } = 1;
    public string? Description { get; init; }
}

/// <summary>
/// Helper to convert reWASD button IDs and description strings into GamepadButtons.
/// </summary>
public static class ButtonIdResolver
{
    public static GamepadButtons ResolveButtonId(int buttonId, string? description = null)
    {
        var resolved = buttonId switch
        {
            1 => GamepadButtons.A,
            2 => GamepadButtons.B,
            3 => GamepadButtons.X,
            4 => GamepadButtons.Y,
            5 => GamepadButtons.LeftShoulder,
            6 => GamepadButtons.RightShoulder,
            7 => GamepadButtons.Back,
            8 => GamepadButtons.Start,
            9 => GamepadButtons.LeftThumb,
            10 => GamepadButtons.RightThumb,
            11 => GamepadButtons.DpadUp,
            12 => GamepadButtons.DpadDown,
            13 => GamepadButtons.DpadLeft,
            14 => GamepadButtons.DpadRight,
            17 => GamepadButtons.Guide,
            29 => GamepadButtons.PaddleP1,
            30 => GamepadButtons.PaddleP2,
            31 => GamepadButtons.PaddleP3,
            32 => GamepadButtons.PaddleP4,
            33 => GamepadButtons.DpadUp,
            34 => GamepadButtons.DpadDown,
            35 => GamepadButtons.DpadLeft,
            36 => GamepadButtons.DpadRight,
            37 => GamepadButtons.TouchpadClick,
            38 => GamepadButtons.TouchpadTap,
            41 => GamepadButtons.SwipeUp,
            42 => GamepadButtons.SwipeDown,
            43 => GamepadButtons.SwipeLeft,
            44 => GamepadButtons.SwipeRight,
            45 => GamepadButtons.TiltLeft,
            46 => GamepadButtons.TiltRight,
            47 => GamepadButtons.TiltUp,
            48 => GamepadButtons.TiltDown,
            49 => GamepadButtons.VGYRO,
            57 => GamepadButtons.Touchpad,
            68 => GamepadButtons.TiltUp,
            69 => GamepadButtons.TiltDown,
            70 => GamepadButtons.TiltLeft,
            71 => GamepadButtons.TiltRight,
            72 => GamepadButtons.TiltLeft,
            73 => GamepadButtons.TiltRight,
            74 => GamepadButtons.VGYRO,
            75 => GamepadButtons.VGYRO,
            76 => GamepadButtons.VGYRO,
            99 => GamepadButtons.TouchpadClick,

            // KeyScanCodeV2 extended gamepad & virtual indices (256–315)
            256 => GamepadButtons.A,
            257 => GamepadButtons.B,
            258 => GamepadButtons.X,
            259 => GamepadButtons.Y,
            260 => GamepadButtons.LeftShoulder,
            261 => GamepadButtons.RightShoulder,
            262 => GamepadButtons.Back,
            263 => GamepadButtons.Start,
            264 => GamepadButtons.LeftThumb,
            265 => GamepadButtons.RightThumb,
            266 => GamepadButtons.Guide,
            267 => GamepadButtons.Share,
            269 => GamepadButtons.DpadUp,
            270 => GamepadButtons.DpadDown,
            271 => GamepadButtons.DpadLeft,
            272 => GamepadButtons.DpadRight,
            273 => GamepadButtons.LeftShoulder,
            274 => GamepadButtons.RightShoulder,
            275 => GamepadButtons.DpadUp,
            276 => GamepadButtons.DpadDown,
            277 => GamepadButtons.DpadLeft,
            278 => GamepadButtons.DpadRight,
            279 => GamepadButtons.DpadUp,
            280 => GamepadButtons.DpadDown,
            281 => GamepadButtons.DpadLeft,
            282 => GamepadButtons.DpadRight,
            283 or 284 or 285 or 286 => GamepadButtons.TouchpadTap,
            287 or 288 => GamepadButtons.Touchpad,
            289 or 293 => GamepadButtons.SwipeUp,
            290 or 294 => GamepadButtons.SwipeDown,
            291 or 295 => GamepadButtons.SwipeLeft,
            292 or 296 => GamepadButtons.SwipeRight,
            297 or 298 or 299 or 300 => GamepadButtons.Touchpad,
            303 => GamepadButtons.TouchpadClick,
            304 => GamepadButtons.TiltUp,
            305 => GamepadButtons.TiltDown,
            306 => GamepadButtons.TiltRight,
            307 => GamepadButtons.TiltLeft,
            308 => GamepadButtons.TiltLeft,
            309 => GamepadButtons.TiltRight,
            310 or 311 or 312 or 313 or 314 or 315 => GamepadButtons.VGYRO,

            _ => GamepadButtons.None
        };

        if (resolved != GamepadButtons.None) return resolved;

        if (string.IsNullOrEmpty(description)) return GamepadButtons.None;

        var lower = description.ToLowerInvariant();
        if (lower.Contains("paddle 1") || lower.Contains("paddle1") || lower.Contains("leftupperpaddle") || lower.Contains("paddle p1") || lower == "p1") return GamepadButtons.PaddleP1;
        if (lower.Contains("paddle 2") || lower.Contains("paddle2") || lower.Contains("rightupperpaddle") || lower.Contains("paddle p2") || lower == "p2") return GamepadButtons.PaddleP2;
        if (lower.Contains("paddle 3") || lower.Contains("paddle3") || lower.Contains("leftlowerpaddle") || lower.Contains("paddle p3") || lower == "p3") return GamepadButtons.PaddleP3;
        if (lower.Contains("paddle 4") || lower.Contains("paddle4") || lower.Contains("rightlowerpaddle") || lower.Contains("paddle p4") || lower == "p4") return GamepadButtons.PaddleP4;

        if (lower.Contains("gyroscope up") || lower.Contains("gyro up") || lower.Contains("tiltup") || lower.Contains("tilt up") || lower.Contains("pitch up")) return GamepadButtons.TiltUp;
        if (lower.Contains("gyroscope down") || lower.Contains("gyro down") || lower.Contains("tiltdown") || lower.Contains("tilt down") || lower.Contains("pitch down")) return GamepadButtons.TiltDown;
        if (lower.Contains("gyroscope left") || lower.Contains("gyro left") || lower.Contains("tiltleft") || lower.Contains("tilt left") || lower.Contains("lean left")) return GamepadButtons.TiltLeft;
        if (lower.Contains("gyroscope right") || lower.Contains("gyro right") || lower.Contains("tiltright") || lower.Contains("tilt right") || lower.Contains("lean right")) return GamepadButtons.TiltRight;
        if (lower.Contains("gyroscope") || lower.Contains("vgyro") || lower.Contains("virtual gyro") || lower.Contains("gyro aim")) return GamepadButtons.VGYRO;
        if (lower.Contains("swipeup") || lower.Contains("swipe up")) return GamepadButtons.SwipeUp;
        if (lower.Contains("swipedown") || lower.Contains("swipe down")) return GamepadButtons.SwipeDown;
        if (lower.Contains("swipeleft") || lower.Contains("swipe left")) return GamepadButtons.SwipeLeft;
        if (lower.Contains("swiperight") || lower.Contains("swipe right")) return GamepadButtons.SwipeRight;
        if (lower.Contains("touchpadclick") || lower.Contains("touch click") || lower.Contains("touchpad click")) return GamepadButtons.TouchpadClick;
        if (lower.Contains("touchpadtap") || lower.Contains("touch tap") || lower.Contains("touchpad tap")) return GamepadButtons.TouchpadTap;

        if (lower.Contains("dpadup") || lower.Contains("d-pad up")) return GamepadButtons.DpadUp;
        if (lower.Contains("dpaddown") || lower.Contains("d-pad down")) return GamepadButtons.DpadDown;
        if (lower.Contains("dpadleft") || lower.Contains("d-pad left")) return GamepadButtons.DpadLeft;
        if (lower.Contains("dpadright") || lower.Contains("d-pad right")) return GamepadButtons.DpadRight;
        if (lower.Contains("leftshoulder") || lower.Contains("l1") || lower.Contains("lb")) return GamepadButtons.LeftShoulder;
        if (lower.Contains("rightshoulder") || lower.Contains("r1") || lower.Contains("rb")) return GamepadButtons.RightShoulder;
        if (lower.Contains("back") || lower.Contains("view") || lower.Contains("share")) return GamepadButtons.Back;
        if (lower.Contains("start") || lower.Contains("menu") || lower.Contains("options")) return GamepadButtons.Start;
        if (lower.Contains("guide") || lower.Contains("home") || lower.Contains("ps")) return GamepadButtons.Guide;
        if (lower.Contains("touchpad")) return GamepadButtons.Touchpad;
        if (lower.Contains("leftthumb") || lower.Contains("leftstickclick") || lower.Contains("l3")) return GamepadButtons.LeftThumb;
        if (lower.Contains("rightthumb") || lower.Contains("rightstickclick") || lower.Contains("r3")) return GamepadButtons.RightThumb;
        if (lower.Contains("a /") || lower.Contains(": a") || lower.Contains("cross")) return GamepadButtons.A;
        if (lower.Contains("b /") || lower.Contains(": b") || lower.Contains("circle")) return GamepadButtons.B;
        if (lower.Contains("x /") || lower.Contains(": x") || lower.Contains("square")) return GamepadButtons.X;
        if (lower.Contains("y /") || lower.Contains(": y") || lower.Contains("triangle")) return GamepadButtons.Y;

        return GamepadButtons.None;
    }
}

/// <summary>
/// DirectInput scan code and virtual key mapper for keyboard mappings.
/// </summary>
public static class KeyboardKeyResolver
{
    private static readonly Dictionary<string, KeyboardKeyAction> KeyLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tab"] = new(0x0F, 0x09, "Tab"),
        ["DIK_TAB"] = new(0x0F, 0x09, "Tab"),
        ["LAlt"] = new(0x38, 0x12, "LAlt"),
        ["DIK_LMENU"] = new(0x38, 0x12, "LAlt"),
        ["Alt"] = new(0x38, 0x12, "Alt"),
        ["RAlt"] = new(0xB8, 0x12, "RAlt"),
        ["LCtrl"] = new(0x1D, 0x11, "LCtrl"),
        ["DIK_LCONTROL"] = new(0x1D, 0x11, "LCtrl"),
        ["Ctrl"] = new(0x1D, 0x11, "Ctrl"),
        ["RCtrl"] = new(0x9D, 0x11, "RCtrl"),
        ["LShift"] = new(0x2A, 0x10, "LShift"),
        ["DIK_LSHIFT"] = new(0x2A, 0x10, "LShift"),
        ["Shift"] = new(0x2A, 0x10, "Shift"),
        ["RShift"] = new(0x36, 0x10, "RShift"),
        ["Esc"] = new(0x01, 0x1B, "Esc"),
        ["DIK_ESCAPE"] = new(0x01, 0x1B, "Esc"),
        ["Enter"] = new(0x1C, 0x0D, "Enter"),
        ["DIK_RETURN"] = new(0x1C, 0x0D, "Enter"),
        ["Space"] = new(0x39, 0x20, "Space"),
        ["DIK_SPACE"] = new(0x39, 0x20, "Space"),
        ["Backspace"] = new(0x0E, 0x08, "Backspace"),
        ["DIK_BACK"] = new(0x0E, 0x08, "Backspace"),
        ["Delete"] = new(0xD3, 0x2E, "Delete"),
        ["DIK_DELETE"] = new(0xD3, 0x2E, "Delete"),
        ["PageUp"] = new(0xC9, 0x21, "PageUp"),
        ["PgUp"] = new(0xC9, 0x21, "PageUp"),
        ["DIK_PRIOR"] = new(0xC9, 0x21, "PageUp"),
        ["PageDown"] = new(0xD1, 0x22, "PageDown"),
        ["PgDn"] = new(0xD1, 0x22, "PageDown"),
        ["DIK_NEXT"] = new(0xD1, 0x22, "PageDown"),
        ["Home"] = new(0xC7, 0x24, "Home"),
        ["DIK_HOME"] = new(0xC7, 0x24, "Home"),
        ["End"] = new(0xCF, 0x23, "End"),
        ["DIK_END"] = new(0xCF, 0x23, "End"),
        ["W"] = new(0x11, 0x57, "W"),
        ["A"] = new(0x1E, 0x41, "A"),
        ["S"] = new(0x1F, 0x53, "S"),
        ["D"] = new(0x20, 0x44, "D"),
        ["E"] = new(0x12, 0x45, "E"),
        ["Q"] = new(0x10, 0x51, "Q"),
        ["R"] = new(0x13, 0x52, "R"),
        ["F"] = new(0x21, 0x46, "F"),
        ["C"] = new(0x2E, 0x43, "C"),
        ["V"] = new(0x2F, 0x56, "V"),
        ["Z"] = new(0x2C, 0x5A, "Z"),
        ["X"] = new(0x2D, 0x58, "X"),
        ["1"] = new(0x02, 0x31, "1"),
        ["2"] = new(0x03, 0x32, "2"),
        ["3"] = new(0x04, 0x33, "3"),
        ["4"] = new(0x05, 0x34, "4"),
        ["5"] = new(0x06, 0x35, "5"),
    };

    public static KeyboardKeyAction? ResolveKey(string? nameOrScanCode)
    {
        if (string.IsNullOrWhiteSpace(nameOrScanCode)) return null;

        if (KeyLookup.TryGetValue(nameOrScanCode.Trim(), out var action))
            return action;

        if (int.TryParse(nameOrScanCode, out var code))
        {
            foreach (var kvp in KeyLookup.Values)
            {
                if (kvp.ScanCode == code || kvp.VirtualKey == code)
                    return kvp;
            }
            return new KeyboardKeyAction(code, code, $"Key_{code}");
        }

        return null;
    }
}
