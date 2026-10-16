using System;
using System.Collections.Generic;
using System.Linq;

namespace Shunt.Core.Scancodes;

/// <summary>
/// Resolves hardware, poller, and scancode button names/aliases to standardized
/// canvas anchor identifiers on the virtual gamepad view (plan 09 §3, UI Phase 4).
/// </summary>
public static class CanvasButtonResolver
{
    private static readonly Dictionary<string, string[]> AnchorAliasesMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Face & Action Buttons
        ["A"] = ["A", "Cross", "BTN_A", "A / Cross", "A/Cross"],
        ["B"] = ["B", "Circle", "BTN_B", "B / Circle", "B/Circle"],
        ["X"] = ["X", "Square", "BTN_X", "X / Square", "X/Square"],
        ["Y"] = ["Y", "Triangle", "BTN_Y", "Y / Triangle", "Y/Triangle"],

        // Analog Sticks & Thumbs
        ["LS"] = ["LS", "L3", "LeftStick", "Left Stick", "LeftThumb", "Left Thumb", "BTN_LEFT_STICK", "BTN_LS", "BTN_THUMBL"],
        ["RS"] = ["RS", "R3", "RightStick", "Right Stick", "RightThumb", "Right Thumb", "BTN_RIGHT_STICK", "BTN_RS", "BTN_THUMBR"],

        // D-Pad
        ["DUp"] = ["DUp", "D-Up", "DpadUp", "D-pad Up", "DPadUp", "BTN_DPAD_UP", "D-Pad Up"],
        ["DDown"] = ["DDown", "D-Down", "DpadDown", "D-pad Down", "DPadDown", "BTN_DPAD_DOWN", "D-Pad Down"],
        ["DLeft"] = ["DLeft", "D-Left", "DpadLeft", "D-pad Left", "DPadLeft", "BTN_DPAD_LEFT", "D-Pad Left"],
        ["DRight"] = ["DRight", "D-Right", "DpadRight", "D-pad Right", "DPadRight", "BTN_DPAD_RIGHT", "D-Pad Right"],

        // Shoulders & Bumpers
        ["LB"] = ["LB", "L1", "LeftShoulder", "Left Shoulder", "LeftBumper", "Left Bumper", "BTN_LEFT_SHOULDER", "BTN_LEFT_BUMPER", "BTN_LB", "BTN_TL"],
        ["RB"] = ["RB", "R1", "RightShoulder", "Right Shoulder", "RightBumper", "Right Bumper", "BTN_RIGHT_SHOULDER", "BTN_RIGHT_BUMPER", "BTN_RB", "BTN_TR"],

        // Triggers
        ["LT"] = ["LT", "L2", "LeftTrigger", "Left Trigger", "BTN_LEFT_TRIGGER", "BTN_LT", "BTN_TL2"],
        ["RT"] = ["RT", "R2", "RightTrigger", "Right Trigger", "BTN_RIGHT_TRIGGER", "BTN_RT", "BTN_TR2"],

        // Center System Controls
        ["View"] = ["View", "Back", "Share", "Select", "BTN_VIEW", "BTN_BACK", "BTN_SHARE", "BTN_SELECT"],
        ["Menu"] = ["Menu", "Start", "Options", "BTN_MENU", "BTN_START", "BTN_OPTIONS"],
        ["Guide"] = ["Guide", "Home", "PS", "Logo", "BTN_GUIDE", "BTN_HOME", "BTN_MODE"],

        // Elite Rear Paddles (P1..P4)
        ["P1"] = ["P1", "Paddle P1", "PaddleP1", "Paddle 1", "Paddle1", "BTN_LEFT_UPPER_PADDLE", "BTN_P1"],
        ["P2"] = ["P2", "Paddle P2", "PaddleP2", "Paddle 2", "Paddle2", "BTN_RIGHT_UPPER_PADDLE", "BTN_P2"],
        ["P3"] = ["P3", "Paddle P3", "PaddleP3", "Paddle 3", "Paddle3", "BTN_LEFT_LOWER_PADDLE", "BTN_P3"],
        ["P4"] = ["P4", "Paddle P4", "PaddleP4", "Paddle 4", "Paddle4", "BTN_RIGHT_LOWER_PADDLE", "BTN_P4"],

        // DualShock 4 Touchpad Gestures
        ["TouchClick"] = ["TouchClick", "TouchpadClick", "Touchpad Click", "Touchpad", "Touch Click", "BTN_TOUCH_CLICK", "BTN_TOUCHPAD"],
        ["TouchTap"] = ["TouchTap", "TouchpadTap", "Touchpad Tap", "Touch Tap", "BTN_TOUCH_TAP"],
        ["SwipeUp"] = ["SwipeUp", "Swipe Up", "TouchpadSwipeUp", "Touchpad Swipe Up", "BTN_TOUCH_SWIPE_UP"],
        ["SwipeDown"] = ["SwipeDown", "Swipe Down", "TouchpadSwipeDown", "Touchpad Swipe Down", "BTN_TOUCH_SWIPE_DOWN"],
        ["SwipeLeft"] = ["SwipeLeft", "Swipe Left", "TouchpadSwipeLeft", "Touchpad Swipe Left", "BTN_TOUCH_SWIPE_LEFT"],
        ["SwipeRight"] = ["SwipeRight", "Swipe Right", "TouchpadSwipeRight", "Touchpad Swipe Right", "BTN_TOUCH_SWIPE_RIGHT"],

        // Motion & Virtual Gyroscope
        ["VGYRO"] = ["VGYRO", "Gyro", "GyroAim", "Gyro Aim", "Virtual Gyroscope", "Virtual Gyro", "BTN_GYRO"],
        ["TiltUp"] = ["TiltUp", "Tilt Up", "PitchUp", "Pitch Up", "BTN_GYRO_TILT_UP"],
        ["TiltDown"] = ["TiltDown", "Tilt Down", "PitchDown", "Pitch Down", "BTN_GYRO_TILT_DOWN"],
        ["TiltLeft"] = ["TiltLeft", "Tilt Left", "LeanLeft", "Lean Left", "BTN_GYRO_TILT_LEFT"],
        ["TiltRight"] = ["TiltRight", "Tilt Right", "LeanRight", "Lean Right", "BTN_GYRO_TILT_RIGHT"]
    };

    private static readonly Dictionary<string, string> NormalizedLookup = new(StringComparer.OrdinalIgnoreCase);

    static CanvasButtonResolver()
    {
        RegisterKeyboardAndMouseAnchors();

        foreach (var (anchor, aliases) in AnchorAliasesMap)
        {
            // Register anchor itself
            RegisterAlias(anchor, anchor);

            foreach (var alias in aliases)
            {
                RegisterAlias(alias, anchor);
            }
        }
    }

    /// <summary>
    /// Keyboard (plan 11 §4) and mouse (plan 11 §5) canvas anchors. Aliases deliberately avoid
    /// bare single letters and words that already belong to gamepad anchors ("Home", "Back", "Menu").
    /// </summary>
    private static void RegisterKeyboardAndMouseAnchors()
    {
        // Letters A-Z ("Key: W" matches the keyboard output naming from KeyScanCodeTable).
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            AnchorAliasesMap[$"Key{letter}"] = [$"Key: {letter}", $"Key{letter}", $"KB_{letter}", $"BTN_KEY_{letter}"];
        }

        // Digit row
        foreach (var digit in "1234567890")
        {
            AnchorAliasesMap[$"Key{digit}"] = [$"Key: {digit}", $"Key{digit}", $"KB_{digit}", $"BTN_KEY_{digit}"];
        }

        // Function row
        for (var f = 1; f <= 12; f++)
        {
            AnchorAliasesMap[$"KeyF{f}"] = [$"Key: F{f}", $"KeyF{f}", $"KB_F{f}", $"BTN_KEY_F{f}"];
        }

        // Modifier, control and navigation keys
        AnchorAliasesMap["KeyEsc"] = ["Key: Esc", "KeyEsc", "Esc", "BTN_KEY_ESC"];
        AnchorAliasesMap["KeySpace"] = ["Key: Spacebar", "Key: Space", "Space", "KeySpace", "BTN_KEY_SPACE"];
        AnchorAliasesMap["KeyEnter"] = ["Key: Enter", "Enter", "KeyEnter", "BTN_KEY_ENTER"];
        AnchorAliasesMap["KeyBackspace"] = ["Key: Backspace", "Backspace", "KeyBackspace"];
        AnchorAliasesMap["KeyTab"] = ["Key: Tab", "Tab", "KeyTab"];
        AnchorAliasesMap["KeyCaps"] = ["Key: Caps Lock", "Caps Lock", "CapsLock", "KeyCaps"];
        AnchorAliasesMap["KeyLShift"] = ["Key: Left Shift", "Left Shift", "LShift", "KeyLShift"];
        AnchorAliasesMap["KeyRShift"] = ["Key: Right Shift", "Right Shift", "RShift", "KeyRShift"];
        AnchorAliasesMap["KeyLCtrl"] = ["Key: Left Ctrl", "Left Ctrl", "LCtrl", "KeyLCtrl"];
        AnchorAliasesMap["KeyRCtrl"] = ["Key: Right Ctrl", "Right Ctrl", "RCtrl", "KeyRCtrl"];
        AnchorAliasesMap["KeyLAlt"] = ["Key: Left Alt", "Left Alt", "LAlt", "KeyLAlt"];
        AnchorAliasesMap["KeyRAlt"] = ["Key: Right Alt", "Right Alt (AltGr)", "Right Alt", "AltGr", "RAlt", "KeyRAlt"];
        AnchorAliasesMap["KeyLWin"] = ["Key: Left Windows", "Left Windows", "LWin", "KeyLWin"];
        AnchorAliasesMap["KeyRWin"] = ["Key: Right Windows", "Right Windows", "RWin", "KeyRWin"];
        AnchorAliasesMap["KeyMenu"] = ["Key: Application / Menu", "Application", "Apps", "KeyMenu"];

        AnchorAliasesMap["KeyUp"] = ["Key: Up Arrow", "Up Arrow", "KeyUp", "BTN_KEY_UP"];
        AnchorAliasesMap["KeyDown"] = ["Key: Down Arrow", "Down Arrow", "KeyDown", "BTN_KEY_DOWN"];
        AnchorAliasesMap["KeyLeft"] = ["Key: Left Arrow", "Left Arrow", "KeyLeft", "BTN_KEY_LEFT"];
        AnchorAliasesMap["KeyRight"] = ["Key: Right Arrow", "Right Arrow", "KeyRight", "BTN_KEY_RIGHT"];

        AnchorAliasesMap["KeyIns"] = ["Key: Insert", "Insert", "KeyIns"];
        AnchorAliasesMap["KeyDel"] = ["Key: Delete", "Delete", "KeyDel"];
        AnchorAliasesMap["KeyHome"] = ["Key: Home", "KeyHome", "BTN_KEY_HOME"];
        AnchorAliasesMap["KeyEnd"] = ["Key: End", "KeyEnd"];
        AnchorAliasesMap["KeyPgUp"] = ["Key: Page Up", "Page Up", "KeyPgUp"];
        AnchorAliasesMap["KeyPgDn"] = ["Key: Page Down", "Page Down", "KeyPgDn"];
        AnchorAliasesMap["KeyPrtSc"] = ["Key: Print Screen", "Print Screen", "PrtSc", "KeyPrtSc"];
        AnchorAliasesMap["KeyScrLk"] = ["Key: Scroll Lock", "Scroll Lock", "ScrLk", "KeyScrLk"];
        AnchorAliasesMap["KeyPause"] = ["Key: Pause", "Pause", "KeyPause"];

        // Punctuation & OEM row
        AnchorAliasesMap["KeyMinus"] = ["Key: -", "KeyMinus"];
        AnchorAliasesMap["KeyEquals"] = ["Key: =", "KeyEquals"];
        AnchorAliasesMap["KeyLBracket"] = ["Key: [", "KeyLBracket"];
        AnchorAliasesMap["KeyRBracket"] = ["Key: ]", "KeyRBracket"];
        AnchorAliasesMap["KeySemicolon"] = ["Key: ;", "KeySemicolon"];
        AnchorAliasesMap["KeyApostrophe"] = ["Key: '", "KeyApostrophe"];
        AnchorAliasesMap["KeyGrave"] = ["Key: `", "KeyGrave"];
        AnchorAliasesMap["KeyBackslash"] = ["Key: \\", "KeyBackslash"];
        AnchorAliasesMap["KeyComma"] = ["Key: ,", "KeyComma"];
        AnchorAliasesMap["KeyPeriod"] = ["Key: .", "KeyPeriod"];
        AnchorAliasesMap["KeySlash"] = ["Key: /", "KeySlash"];
        AnchorAliasesMap["KeyIso"] = ["Key: <> (ISO)", "KeyIso"];

        // Numpad cluster
        AnchorAliasesMap["KeyNumLock"] = ["Key: NumLock", "NumLock", "KeyNumLock"];
        AnchorAliasesMap["KeyNumDiv"] = ["Key: Numpad /", "Numpad /", "KeyNumDiv"];
        AnchorAliasesMap["KeyNumMul"] = ["Key: Numpad *", "Numpad *", "KeyNumMul"];
        AnchorAliasesMap["KeyNumSub"] = ["Key: Numpad -", "Numpad -", "KeyNumSub"];
        AnchorAliasesMap["KeyNumAdd"] = ["Key: Numpad +", "Numpad +", "KeyNumAdd"];
        AnchorAliasesMap["KeyNumEnter"] = ["Key: Numpad Enter", "Numpad Enter", "KeyNumEnter"];
        AnchorAliasesMap["KeyNumDec"] = ["Key: Numpad .", "Numpad .", "KeyNumDec"];
        foreach (var n in "0123456789")
        {
            AnchorAliasesMap[$"KeyNum{n}"] = [$"Key: Numpad {n}", $"Numpad {n}", $"KeyNum{n}"];
        }

        // Mouse buttons, wheel and deflection (plan 11 §5)
        AnchorAliasesMap["LMB"] = ["Mouse: Left Click", "Mouse Left Click", "LMB", "MOUSE_BTN_1", "Mouse 1"];
        AnchorAliasesMap["RMB"] = ["Mouse: Right Click", "Mouse Right Click", "RMB", "MOUSE_BTN_2", "Mouse 2"];
        AnchorAliasesMap["MMB"] = ["Mouse: Middle Click", "Mouse Middle Click", "MMB", "MOUSE_BTN_3", "Middle Click"];
        AnchorAliasesMap["Mouse4"] = ["Mouse: Mouse 4 (XButton 1)", "Mouse 4 (XButton 1)", "Mouse 4", "Mouse4", "MOUSE_BTN_4", "XButton1"];
        AnchorAliasesMap["Mouse5"] = ["Mouse: Mouse 5 (XButton 2)", "Mouse 5 (XButton 2)", "Mouse 5", "Mouse5", "MOUSE_BTN_5", "XButton2"];
        AnchorAliasesMap["WheelUp"] = ["Mouse: Wheel Up", "Wheel Up", "WheelUp", "MOUSE_WHEEL_UP"];
        AnchorAliasesMap["WheelDown"] = ["Mouse: Wheel Down", "Wheel Down", "WheelDown", "MOUSE_WHEEL_DOWN"];
        AnchorAliasesMap["MWheelLeft"] = ["Mouse: Wheel Tilt Left", "Wheel Tilt Left", "MWheelLeft", "MOUSE_WHEEL_LEFT"];
        AnchorAliasesMap["MWheelRight"] = ["Mouse: Wheel Tilt Right", "Wheel Tilt Right", "MWheelRight", "MOUSE_WHEEL_RIGHT"];
        AnchorAliasesMap["MDeflectLeft"] = ["Mouse: Mouse Deflection Left", "Mouse Deflection Left", "MDeflectLeft", "MOUSE_X_LEFT"];
        AnchorAliasesMap["MDeflectRight"] = ["Mouse: Mouse Deflection Right", "Mouse Deflection Right", "MDeflectRight", "MOUSE_X_RIGHT"];
        AnchorAliasesMap["MDeflectUp"] = ["Mouse: Mouse Deflection Up", "Mouse Deflection Up", "MDeflectUp", "MOUSE_Y_UP"];
        AnchorAliasesMap["MDeflectDown"] = ["Mouse: Mouse Deflection Down", "Mouse Deflection Down", "MDeflectDown", "MOUSE_Y_DOWN"];
    }

    private static void RegisterAlias(string alias, string anchor)
    {
        NormalizedLookup[alias] = anchor;
        var stripped = StripPunctuationAndSpaces(alias);
        if (!string.IsNullOrEmpty(stripped))
        {
            NormalizedLookup[stripped] = anchor;
        }
    }

    private static string StripPunctuationAndSpaces(string input)
    {
        return new string(input.Where(c => char.IsLetterOrDigit(c)).ToArray());
    }

    /// <summary>
    /// Resolves an incoming hardware or event button name to a standard canvas anchor key.
    /// Returns null if the button does not have a designated anchor on the virtual canvas.
    /// </summary>
    public static string? ResolveAnchor(string? buttonName)
    {
        if (string.IsNullOrWhiteSpace(buttonName))
            return null;

        var trimmed = buttonName.Trim();

        // Direct lookup
        if (NormalizedLookup.TryGetValue(trimmed, out var anchor))
            return anchor;

        // Stripped lookup (ignoring spaces, dashes, slashes, underscores)
        var stripped = StripPunctuationAndSpaces(trimmed);
        if (!string.IsNullOrEmpty(stripped) && NormalizedLookup.TryGetValue(stripped, out anchor))
            return anchor;

        return null;
    }

    /// <summary>
    /// Attempts to resolve a button name to a canvas anchor.
    /// </summary>
    public static bool TryResolveAnchor(string? buttonName, out string? anchor)
    {
        anchor = ResolveAnchor(buttonName);
        return anchor != null;
    }

    /// <summary>
    /// Retrieves all recognized aliases for a given anchor key.
    /// </summary>
    public static IReadOnlyList<string> GetAliases(string anchor)
    {
        return AnchorAliasesMap.TryGetValue(anchor, out var aliases) ? aliases : [];
    }

    /// <summary>
    /// Returns all known canvas anchor identifiers.
    /// </summary>
    public static IReadOnlyCollection<string> AllAnchors => AnchorAliasesMap.Keys;

    /// <summary>
    /// Returns the full map of anchors to their recognized alias names.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> AllAnchorAliases => AnchorAliasesMap;
}
