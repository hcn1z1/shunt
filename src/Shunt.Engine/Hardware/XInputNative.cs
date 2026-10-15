using System;
using System.Runtime.InteropServices;

namespace Shunt.Engine.Hardware;

public static class XInputNative
{
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_CAPABILITIES
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XINPUT_GAMEPAD Gamepad;
        public XINPUT_VIBRATION Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    public const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;
    public const byte BATTERY_TYPE_DISCONNECTED = 0x00;
    public const byte BATTERY_TYPE_WIRED = 0x01;
    public const byte BATTERY_TYPE_ALKALINE = 0x02;
    public const byte BATTERY_TYPE_NIMH = 0x03;
    public const byte BATTERY_TYPE_UNKNOWN = 0xFF;

    public const byte BATTERY_LEVEL_EMPTY = 0x00;
    public const byte BATTERY_LEVEL_LOW = 0x01;
    public const byte BATTERY_LEVEL_MEDIUM = 0x02;
    public const byte BATTERY_LEVEL_FULL = 0x03;

    // Button bitmasks
    public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
    public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
    public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    public const ushort XINPUT_GAMEPAD_START = 0x0010;
    public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
    public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
    public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
    public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
    public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    public const ushort XINPUT_GAMEPAD_A = 0x1000;
    public const ushort XINPUT_GAMEPAD_B = 0x2000;
    public const ushort XINPUT_GAMEPAD_X = 0x4000;
    public const ushort XINPUT_GAMEPAD_GUIDE = 0x0400;
    public const ushort XINPUT_GAMEPAD_Y = 0x8000;

    [DllImport("xinput1_4.dll", EntryPoint = "#100", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetStateEx14(uint dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetState14(uint dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput1_3.dll", EntryPoint = "#100", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetStateEx13(uint dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetState13(uint dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputSetState14(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputSetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputSetState13(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetCapabilities14(uint dwUserIndex, uint dwFlags, ref XINPUT_CAPABILITIES pCapabilities);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetCapabilities", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetCapabilities13(uint dwUserIndex, uint dwFlags, ref XINPUT_CAPABILITIES pCapabilities);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetBatteryInformation14(uint dwUserIndex, byte devType, ref XINPUT_BATTERY_INFORMATION pBatteryInformation);

    private static bool _use14 = true;

    public static uint GetState(uint userIndex, ref XINPUT_STATE state)
    {
        try
        {
            if (_use14)
            {
                try
                {
                    var res = XInputGetStateEx14(userIndex, ref state);
                    if (res == ERROR_SUCCESS) return res;
                }
                catch { }

                return XInputGetState14(userIndex, ref state);
            }
        }
        catch (DllNotFoundException)
        {
            _use14 = false;
        }

        try
        {
            try
            {
                var res = XInputGetStateEx13(userIndex, ref state);
                if (res == ERROR_SUCCESS) return res;
            }
            catch { }

            return XInputGetState13(userIndex, ref state);
        }
        catch
        {
            return ERROR_DEVICE_NOT_CONNECTED;
        }
    }

    public static uint SetState(uint userIndex, ref XINPUT_VIBRATION vibration)
    {
        try
        {
            if (_use14) return XInputSetState14(userIndex, ref vibration);
        }
        catch (DllNotFoundException)
        {
            _use14 = false;
        }

        try
        {
            return XInputSetState13(userIndex, ref vibration);
        }
        catch
        {
            return ERROR_DEVICE_NOT_CONNECTED;
        }
    }

    public static uint SetVibration(uint userIndex, ushort leftMotorSpeed, ushort rightMotorSpeed)
    {
        var vibration = new XINPUT_VIBRATION { wLeftMotorSpeed = leftMotorSpeed, wRightMotorSpeed = rightMotorSpeed };
        return SetState(userIndex, ref vibration);
    }

    public static uint GetCapabilities(uint userIndex, ref XINPUT_CAPABILITIES caps)
    {
        try
        {
            if (_use14) return XInputGetCapabilities14(userIndex, 0, ref caps);
        }
        catch (DllNotFoundException)
        {
            _use14 = false;
        }

        try
        {
            return XInputGetCapabilities13(userIndex, 0, ref caps);
        }
        catch
        {
            return ERROR_DEVICE_NOT_CONNECTED;
        }
    }

    public static uint GetBatteryInformation(uint userIndex, ref XINPUT_BATTERY_INFORMATION battery)
    {
        try
        {
            if (_use14) return XInputGetBatteryInformation14(userIndex, BATTERY_DEVTYPE_GAMEPAD, ref battery);
        }
        catch
        {
            // fallback
        }
        return ERROR_DEVICE_NOT_CONNECTED;
    }
}
