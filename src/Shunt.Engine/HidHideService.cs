using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Shunt.Core.Interfaces;

namespace Shunt.Engine;

/// <summary>
/// Service communicating with the Nefarius HidHide kernel driver (\\.\HidHide)
/// for physical device isolation, with graceful in-memory stub fallback when the driver is absent.
/// </summary>
public sealed class HidHideService : IHidHideService, IDisposable
{
    private const string ControlDevicePath = @"\\.\HidHide";
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;

    // HidHide IOCTL definitions
    private const uint IOCTL_GET_WHITELIST = 0x80016000;
    private const uint IOCTL_SET_WHITELIST = 0x80016004;
    private const uint IOCTL_GET_BLACKLIST = 0x80016008;
    private const uint IOCTL_SET_BLACKLIST = 0x8001600C;
    private const uint IOCTL_GET_ACTIVE    = 0x80016010;
    private const uint IOCTL_SET_ACTIVE    = 0x80016014;
    private const uint IOCTL_GET_INVERSE   = 0x80016018;
    private const uint IOCTL_SET_INVERSE   = 0x8001601C;
    private const int DriverListBufferSize = 1024 * 1024;
    private readonly object _settingsLock = new();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, StringBuilder targetPath, int maxChars);

    private readonly HashSet<string> _inMemoryWhitelist = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inMemoryBlacklist = new(StringComparer.OrdinalIgnoreCase);
    private bool _inMemoryInverse = true;
    private bool _inMemoryActive;
    private readonly bool _driverAvailable;

    public bool IsAvailable => _driverAvailable;
    public string? DriverVersion => _driverAvailable ? "1.5.210.0" : null;

    public bool IsActive
    {
        get => _driverAvailable && OperatingSystem.IsWindows() ? GetBoolean(IOCTL_GET_ACTIVE, _inMemoryActive) : _inMemoryActive;
        set
        {
            _inMemoryActive = value;
            if (_driverAvailable && OperatingSystem.IsWindows()) _ = SetBoolean(IOCTL_SET_ACTIVE, value);
        }
    }

    public bool IsAppListInverted
    {
        get => _driverAvailable && OperatingSystem.IsWindows() ? GetBoolean(IOCTL_GET_INVERSE, _inMemoryInverse) : _inMemoryInverse;
        set
        {
            _inMemoryInverse = value;
            if (_driverAvailable && OperatingSystem.IsWindows()) _ = SetBoolean(IOCTL_SET_INVERSE, value);
        }
    }

    public HidHideService()
    {
        _driverAvailable = ProbeDriver();
        if (_driverAvailable && OperatingSystem.IsWindows())
        {
            // Auto-whitelist current process
            using var currentProcess = Process.GetCurrentProcess();
            var mainModule = currentProcess.MainModule;
            if (!string.IsNullOrEmpty(mainModule?.FileName))
            {
                AddApplicationWhitelist(mainModule.FileName);
            }
        }
    }

    private static bool ProbeDriver()
    {
        try
        {
            var handle = CreateFileW(ControlDevicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle != IntPtr.Zero && handle != (IntPtr)(-1))
            {
                CloseHandle(handle);
                return true;
            }
        }
        catch
        {
            // Driver not accessible or non-Windows platform
        }
        return false;
    }

    public bool AddApplicationWhitelist(string applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath)) return false;
        var fullPath = ToDevicePath(Path.GetFullPath(applicationPath));
        lock (_settingsLock)
        {
            if (!_driverAvailable || !OperatingSystem.IsWindows()) return _inMemoryWhitelist.Add(fullPath);
            if (!TryReadStringList(IOCTL_GET_WHITELIST, out var current)) return false;
            if (!current.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) current.Add(fullPath);
            if (!WriteStringList(IOCTL_SET_WHITELIST, current)) return false;
            _inMemoryWhitelist.Clear();
            foreach (var item in current) _inMemoryWhitelist.Add(item);
            return true;
        }
    }

    public bool RemoveApplicationWhitelist(string applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath)) return false;
        var fullPath = ToDevicePath(Path.GetFullPath(applicationPath));
        lock (_settingsLock)
        {
            if (!_driverAvailable || !OperatingSystem.IsWindows()) return _inMemoryWhitelist.Remove(fullPath);
            if (!TryReadStringList(IOCTL_GET_WHITELIST, out var current)) return false;
            current.RemoveAll(item => item.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
            if (!WriteStringList(IOCTL_SET_WHITELIST, current)) return false;
            _inMemoryWhitelist.Clear();
            foreach (var item in current) _inMemoryWhitelist.Add(item);
            return true;
        }
    }

    public IReadOnlyList<string> GetWhitelistedApplications()
    {
        lock (_settingsLock)
            return _driverAvailable && OperatingSystem.IsWindows()
                ? ReadStringList(IOCTL_GET_WHITELIST, _inMemoryWhitelist)
                : [.. _inMemoryWhitelist];
    }

    public bool BlockDevice(string deviceInstancePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInstancePath)) return false;
        lock (_settingsLock)
        {
            if (!_driverAvailable || !OperatingSystem.IsWindows()) return _inMemoryBlacklist.Add(deviceInstancePath);
            if (!TryReadStringList(IOCTL_GET_BLACKLIST, out var current)) return false;
            if (!current.Contains(deviceInstancePath, StringComparer.OrdinalIgnoreCase)) current.Add(deviceInstancePath);
            if (!WriteStringList(IOCTL_SET_BLACKLIST, current)) return false;
            _inMemoryBlacklist.Clear();
            foreach (var item in current) _inMemoryBlacklist.Add(item);
            return true;
        }
    }

    public bool UnblockDevice(string deviceInstancePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInstancePath)) return false;
        lock (_settingsLock)
        {
            if (!_driverAvailable || !OperatingSystem.IsWindows()) return _inMemoryBlacklist.Remove(deviceInstancePath);
            if (!TryReadStringList(IOCTL_GET_BLACKLIST, out var current)) return false;
            current.RemoveAll(item => item.Equals(deviceInstancePath, StringComparison.OrdinalIgnoreCase));
            if (!WriteStringList(IOCTL_SET_BLACKLIST, current)) return false;
            _inMemoryBlacklist.Clear();
            foreach (var item in current) _inMemoryBlacklist.Add(item);
            return true;
        }
    }

    public IReadOnlyList<string> GetBlockedDevices()
    {
        lock (_settingsLock)
            return _driverAvailable && OperatingSystem.IsWindows()
                ? ReadStringList(IOCTL_GET_BLACKLIST, _inMemoryBlacklist)
                : [.. _inMemoryBlacklist];
    }

    public bool ClearAll()
    {
        lock (_settingsLock)
        {
            _inMemoryBlacklist.Clear();
            _inMemoryWhitelist.Clear();
            if (!_driverAvailable || !OperatingSystem.IsWindows()) return true;
            var blackOk = WriteStringList(IOCTL_SET_BLACKLIST, []);
            var whiteOk = WriteStringList(IOCTL_SET_WHITELIST, []);
            var activeOk = SetBoolean(IOCTL_SET_ACTIVE, false);
            _inMemoryActive = false;
            return blackOk && whiteOk && activeOk;
        }
    }

    private List<string> ReadStringList(uint code, HashSet<string> fallback)
    {
        return TryReadStringList(code, out var result) ? result : [.. fallback];
    }

    private bool TryReadStringList(uint code, out List<string> result)
    {
        result = [];
        var handle = CreateFileW(ControlDevicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        var buffer = Marshal.AllocHGlobal(DriverListBufferSize);
        try
        {
            if (!DeviceIoControl(handle, code, IntPtr.Zero, 0, buffer, DriverListBufferSize, out var returned, IntPtr.Zero) || returned > DriverListBufferSize)
                return false;
            var text = Marshal.PtrToStringUni(buffer, (int)returned / sizeof(char)) ?? string.Empty;
            result.AddRange(text.Split('\0', StringSplitOptions.RemoveEmptyEntries));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(handle);
        }
    }

    private bool WriteStringList(uint code, IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var multiString = items.Length == 0 ? "\0\0" : string.Join('\0', items) + "\0\0";
        var bytes = Encoding.Unicode.GetBytes(multiString);
        var input = Marshal.AllocHGlobal(bytes.Length);
        var handle = CreateFileW(ControlDevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            Marshal.FreeHGlobal(input);
            return false;
        }

        try
        {
            Marshal.Copy(bytes, 0, input, bytes.Length);
            return DeviceIoControl(handle, code, input, (uint)bytes.Length, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            CloseHandle(handle);
        }
    }

    private bool GetBoolean(uint code, bool fallback)
    {
        var handle = CreateFileW(ControlDevicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return fallback;
        var output = Marshal.AllocHGlobal(1);
        try
        {
            if (!DeviceIoControl(handle, code, IntPtr.Zero, 0, output, 1, out _, IntPtr.Zero)) return fallback;
            return Marshal.ReadByte(output) != 0;
        }
        finally { Marshal.FreeHGlobal(output); CloseHandle(handle); }
    }

    private bool SetBoolean(uint code, bool value)
    {
        var handle = CreateFileW(ControlDevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        var input = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(input, value ? (byte)1 : (byte)0);
            return DeviceIoControl(handle, code, input, 1, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        finally { Marshal.FreeHGlobal(input); CloseHandle(handle); }
    }

    private static string ToDevicePath(string path)
    {
        if (!OperatingSystem.IsWindows() || path.StartsWith("\\\\?\\", StringComparison.Ordinal)) return path;
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return path;
        var target = new StringBuilder(1024);
        if (QueryDosDeviceW(root[..2], target, target.Capacity) == 0) return path;
        return target.ToString().TrimEnd('\\') + path[root.Length..];
    }

    public void Dispose()
    {
        // HidHide settings are system-wide and must outlive this short-lived control client.
    }
}
