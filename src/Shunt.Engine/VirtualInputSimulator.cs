using System;
using System.Runtime.InteropServices;
using Shunt.Core.Models;

namespace Shunt.Engine;

/// <summary>
/// Hardware-level Windows Win32 SendInput simulator for keyboard keys,
/// mouse buttons, mouse wheel, and mouse cursor deflection.
/// </summary>
public static class VirtualInputSimulator
{
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUT_UNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static bool SendKey(KeyboardKeyAction key, bool isDown)
        => SendKey(key, isDown, out _);

    public static bool SendKey(KeyboardKeyAction key, bool isDown, out int errorCode)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUT_UNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)key.VirtualKey,
                    wScan = (ushort)key.ScanCode,
                    dwFlags = (key.ScanCode > 0 ? KEYEVENTF_SCANCODE : 0) | (isDown ? 0 : KEYEVENTF_KEYUP),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>()) > 0;
        errorCode = sent ? 0 : Marshal.GetLastWin32Error();
        return sent;
    }

    public static bool SendMouse(MouseActionType action, bool isDown)
    {
        uint flags = 0;
        uint data = 0;

        switch (action)
        {
            case MouseActionType.LeftClick:
                flags = isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                break;
            case MouseActionType.RightClick:
                flags = isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                break;
            case MouseActionType.MiddleClick:
                flags = isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                break;
            case MouseActionType.WheelUp:
                if (isDown)
                {
                    flags = MOUSEEVENTF_WHEEL;
                    data = 120;
                }
                break;
            case MouseActionType.WheelDown:
                if (isDown)
                {
                    flags = MOUSEEVENTF_WHEEL;
                    data = unchecked((uint)-120);
                }
                break;
        }

        if (flags == 0) return false;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUT_UNION
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = data,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) > 0;
    }

    public static bool SendMouseMove(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0) return true;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUT_UNION
            {
                mi = new MOUSEINPUT
                {
                    dx = deltaX,
                    dy = deltaY,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) > 0;
    }
}
