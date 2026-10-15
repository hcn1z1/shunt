using System;
using System.Runtime.InteropServices;
using Shunt.Core.Models;
using Shunt.Core.Scancodes;

namespace Shunt.Engine;

/// <summary>
/// The 18 reWASD system commands (plan 08 §2.5, <c>RewasdUserCommand</c>).
/// Numeric ids match the commercial KeyScanCodeV2 command table.
/// </summary>
public enum RewasdUserCommand
{
    TurnRemapOff = 1,
    TurnWirelessGamepadOff = 2,
    CloseRewasdGui = 3,
    GyroSwitch = 4,
    TiltReset = 5,
    GyroTiltToggleOnOff = 6,
    Logout = 7,
    Shutdown = 8,
    Sleep = 9,
    AntibossMode = 10,
    Screenshot = 11,
    CloseActiveTask = 12,
    Hibernate = 13,
    ReconnectToExternalTarget = 14,
    GyroTiltTurnOn = 15,
    GyroTiltTurnOff = 16,
    RestartPC = 17,
    VirtualGyroReset = 18
}

/// <summary>
/// Executes all 18 reWASD system commands (plan 08 checklist): engine-level gyro
/// control, OS power actions, window management, and media capture.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly ShuntEngineService? _engine;

    /// <summary>Fired after a command executed (success or not) for SSE/UI feedback.</summary>
    public event Action<RewasdUserCommand, bool, string?>? CommandExecuted;

    public CommandDispatcher(ShuntEngineService? engine = null)
    {
        _engine = engine;
    }

    /// <summary>Resolves a command name or id (1-18) from the KeyScanCodeTable to the enum.</summary>
    public static RewasdUserCommand? Resolve(string? nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId)) return null;
        var trimmed = nameOrId.Trim();
        var entry = KeyScanCodeTable.GetByName(trimmed);
        if (entry != null && entry.Category == KeyScanCodeCategory.RewasdUserCommand &&
            Enum.IsDefined(typeof(RewasdUserCommand), entry.Index))
        {
            return (RewasdUserCommand)entry.Index;
        }
        if (int.TryParse(trimmed, out var id) && Enum.IsDefined(typeof(RewasdUserCommand), id))
        {
            return (RewasdUserCommand)id;
        }
        // Enum.TryParse accepts any numeric string, so verify the result is a defined member
        if (Enum.TryParse<RewasdUserCommand>(trimmed, ignoreCase: true, out var parsed)
            && Enum.IsDefined(typeof(RewasdUserCommand), parsed))
        {
            return parsed;
        }
        return null;
    }

    /// <summary>
    /// Executes the requested system command.
    /// Returns true when the command ran successfully.
    /// </summary>
    public bool Execute(RewasdUserCommand command)
    {
        bool success;
        string? detail = null;
        try
        {
            success = command switch
            {
                RewasdUserCommand.TurnRemapOff => ExecuteTurnRemapOff(),
                RewasdUserCommand.TurnWirelessGamepadOff => ExecuteTurnWirelessGamepadOff(),
                RewasdUserCommand.CloseRewasdGui => ExecuteCloseGui(),
                RewasdUserCommand.GyroSwitch => ExecuteGyroSwitch(),
                RewasdUserCommand.TiltReset => ExecuteTiltReset(),
                RewasdUserCommand.GyroTiltToggleOnOff => ExecuteGyroToggle(),
                RewasdUserCommand.Logout => ExecutePowerAction(PowerAction.Logout),
                RewasdUserCommand.Shutdown => ExecutePowerAction(PowerAction.Shutdown),
                RewasdUserCommand.Sleep => ExecutePowerAction(PowerAction.Sleep),
                RewasdUserCommand.Hibernate => ExecutePowerAction(PowerAction.Hibernate),
                RewasdUserCommand.RestartPC => ExecutePowerAction(PowerAction.Restart),
                RewasdUserCommand.AntibossMode => ExecuteAntibossMode(),
                RewasdUserCommand.Screenshot => ExecuteScreenshot(),
                RewasdUserCommand.CloseActiveTask => ExecuteCloseActiveTask(),
                RewasdUserCommand.ReconnectToExternalTarget => ExecuteReconnectExternalTarget(),
                RewasdUserCommand.GyroTiltTurnOn => ExecuteGyroSet(true),
                RewasdUserCommand.GyroTiltTurnOff => ExecuteGyroSet(false),
                RewasdUserCommand.VirtualGyroReset => ExecuteVirtualGyroReset(),
                _ => false
            };
        }
        catch (Exception ex)
        {
            success = false;
            detail = ex.Message;
        }

        CommandExecuted?.Invoke(command, success, detail);
        return success;
    }

    /// <summary>Executes the command identified by its 1-based KeyScanCode table index.</summary>
    public bool Execute(int commandId)
    {
        if (Enum.IsDefined(typeof(RewasdUserCommand), commandId))
        {
            return Execute((RewasdUserCommand)commandId);
        }
        return false;
    }

    /// <summary>Executes the command asynchronously (offloading power/process actions if needed).</summary>
    public Task<bool> ExecuteAsync(int commandId)
    {
        return Task.Run(() => Execute(commandId));
    }

    /// <summary>Executes the command asynchronously (offloading power/process actions if needed).</summary>
    public Task<bool> ExecuteAsync(RewasdUserCommand command)
    {
        return Task.Run(() => Execute(command));
    }

    /// <summary>Allows a MappingPipeline or test fixture to bind execution context directly.</summary>
    public MappingPipeline? PipelineOverride { get; set; }

    private MappingPipeline? Pipeline => PipelineOverride ?? _engine?.Pipeline;

    private bool ExecuteTurnRemapOff()
    {
        if (_engine is null) return false;
        _engine.Stop();
        return true;
    }

    private bool ExecuteTurnWirelessGamepadOff()
    {
        var controller = _engine?.HardwareDiscovery.SelectedController;
        if (controller is null) return false;
        controller.StopPolling();
        controller.Vibrate(0, 0);
        return true;
    }

    private bool ExecuteCloseGui()
    {
        GuiCloseRequested?.Invoke();
        return true;
    }

    /// <summary>UI subscribes to close the WPF shell (engine keeps running).</summary>
    public event Action? GuiCloseRequested;

    private bool ExecuteGyroSwitch()
    {
        var pipeline = Pipeline;
        if (pipeline is null) return false;
        var gyro = pipeline.Context.GyroSettings;
        gyro.HorizontalAxis = gyro.HorizontalAxis == GyroHorizontalAxis.Yaw
            ? GyroHorizontalAxis.Roll
            : GyroHorizontalAxis.Yaw;
        return true;
    }

    private bool ExecuteTiltReset()
    {
        var pipeline = Pipeline;
        if (pipeline is null) return false;
        var gyro = pipeline.Context.GyroSettings;
        gyro.BiasX = gyro.BiasY = gyro.BiasZ = 0f;
        gyro.IsCalibrated = true;
        return true;
    }

    private bool ExecuteGyroToggle()
    {
        var pipeline = Pipeline;
        if (pipeline is null) return false;
        pipeline.Context.GyroToggleActive = !pipeline.Context.GyroToggleActive;
        return true;
    }

    private bool ExecuteGyroSet(bool enabled)
    {
        var pipeline = Pipeline;
        if (pipeline is null) return false;
        pipeline.Context.GyroToggleActive = enabled;
        return true;
    }

    private bool ExecuteVirtualGyroReset()
    {
        var pipeline = Pipeline;
        if (pipeline is null) return false;
        var gyro = pipeline.Context.GyroSettings;
        gyro.BiasX = gyro.BiasY = gyro.BiasZ = 0f;
        gyro.IsCalibrated = false;
        return true;
    }

    private bool ExecuteReconnectExternalTarget() =>
        // External ESP32 / GIMX BT pairing handshake retry (plan 04, phase 5 hardware)
        _engine?.HardwareDiscovery.SelectedController != null && _engine.HardwareDiscovery.RefreshAsync().IsCompletedSuccessfully;

    private bool ExecuteAntibossMode()
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Minimize all windows (Win+M) and silence system audio (Volume Mute)
        const byte VK_LWIN = 0x5B, VK_M = 0x4D, VK_VOLUME_MUTE = 0xAD;
        const int KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
        keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, IntPtr.Zero);
        keybd_event(VK_M, 0, 0, IntPtr.Zero);
        keybd_event(VK_M, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_VOLUME_MUTE, 0, 0, IntPtr.Zero);
        keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        return true;
    }

    private bool ExecuteScreenshot()
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Captures the screen to the clipboard via the Print Screen key event
        const byte VK_SNAPSHOT = 0x2C;
        keybd_event(VK_SNAPSHOT, 0, 0, IntPtr.Zero);
        keybd_event(VK_SNAPSHOT, 0, 2, IntPtr.Zero);
        return true;
    }

    private bool ExecuteCloseActiveTask()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        // Never close system shell processes
        if (pid == 0 || pid == 4) return false;
        const uint WM_CLOSE = 0x0010;
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private bool ExecutePowerAction(PowerAction action)
    {
        if (!OperatingSystem.IsWindows()) return false;
        switch (action)
        {
            case PowerAction.Sleep:
                return SetSuspendState(hibernate: false, force: false, disableWakeEvent: false);
            case PowerAction.Hibernate:
                return SetSuspendState(hibernate: true, force: false, disableWakeEvent: false);
            case PowerAction.Logout:
                return ExitWindowsEx(EWX_LOGOFF, SHTDN_REASON_FLAG_PLANNED);
            case PowerAction.Shutdown:
                AcquireShutdownPrivilege();
                return ExitWindowsEx(EWX_SHUTDOWN | EWX_POWEROFF, SHTDN_REASON_FLAG_PLANNED);
            case PowerAction.Restart:
                AcquireShutdownPrivilege();
                return ExitWindowsEx(EWX_REBOOT, SHTDN_REASON_FLAG_PLANNED);
            default:
                return false;
        }
    }

    private static void AcquireShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
        try
        {
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = default,
                Attributes = SE_PRIVILEGE_ENABLED
            };
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out var luid)) return;
            tp.Luid = luid;
            _ = AdjustTokenPrivileges(token, disableAllPrivileges: false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private enum PowerAction { Logout, Shutdown, Sleep, Hibernate, Restart }

    // ---- Win32 interop ----
    private const int EWX_LOGOFF = 0x00, EWX_SHUTDOWN = 0x01, EWX_REBOOT = 0x02, EWX_POWEROFF = 0x08;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;
    private const int TOKEN_ADJUST_PRIVILEGES = 0x0020, TOKEN_QUERY = 0x0008;
    private const int SE_PRIVILEGE_ENABLED = 0x0002;
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID Luid;
        public int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string privilegeName, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
