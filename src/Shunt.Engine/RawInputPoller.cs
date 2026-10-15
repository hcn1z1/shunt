using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Shunt.Engine;

/// <summary>Receives background mouse and keyboard Raw Input reports on a dedicated message thread.</summary>
public sealed class RawInputPoller : IDisposable
{
    private const uint WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const ushort HidUsagePageGeneric = 0x01;
    private const ushort HidUsageMouse = 0x02;
    private const ushort HidUsageKeyboard = 0x06;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const uint WsOverlapped = 0x00000000;

    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _startupError;
    private WindowProc? _windowProc;
    private IntPtr _window;
    private string? _className;

    public event Action<RawInputEvent>? InputReceived;

    public RawInputPoller()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Raw Input is available only on Windows.");
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Shunt Raw Input" };
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
        if (_startupError is not null) throw new InvalidOperationException("Could not start the Raw Input message window.", _startupError);
    }

    private void MessageLoop()
    {
        try
        {
            _className = $"ShuntRawInput-{Guid.NewGuid():N}";
            _windowProc = WindowProcedure;
            var wc = new WndClass
            {
                Size = (uint)Marshal.SizeOf<WndClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(_windowProc),
                Instance = GetModuleHandle(null),
                ClassName = _className
            };
            if (RegisterClassEx(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _window = CreateWindowEx(0, _className, "", WsOverlapped, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
            if (_window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

            RawInputDevice[] devices =
            [
                new() { UsagePage = HidUsagePageGeneric, Usage = HidUsageMouse, Flags = RidevInputSink, Target = _window },
                new() { UsagePage = HidUsagePageGeneric, Usage = HidUsageKeyboard, Flags = RidevInputSink, Target = _window }
            ];
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _ready.TrySetResult();
            while (!_stop.IsCancellationRequested && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            _ready.TrySetResult();
        }
        finally
        {
            if (_window != IntPtr.Zero) DestroyWindow(_window);
            if (_className is not null) UnregisterClass(_className, GetModuleHandle(null));
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x0010) { PostQuitMessage(0); return IntPtr.Zero; }
        if (message == WmInput)
        {
            try { ReadInput(lParam); }
            catch { /* A malformed or removed device must not kill the input thread. */ }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ReadInput(IntPtr rawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInput, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0) return;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInput, RidInput, buffer, ref size, headerSize) != size) return;
            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            IntPtr data = IntPtr.Add(buffer, (int)headerSize);
            if (header.Type == RimTypeMouse)
            {
                var mouse = Marshal.PtrToStructure<RawMouse>(data);
                if (mouse.LastX != 0 || mouse.LastY != 0 || mouse.ButtonFlags != 0 || mouse.ButtonData != 0)
                    InputReceived?.Invoke(new RawInputEvent("Mouse", header.Device.ToInt64().ToString("X"), DateTimeOffset.UtcNow,
                        DeltaX: mouse.LastX, DeltaY: mouse.LastY, ButtonFlags: mouse.ButtonFlags, WheelDelta: unchecked((short)mouse.ButtonData)));
            }
            else if (header.Type == RimTypeKeyboard)
            {
                var keyboard = Marshal.PtrToStructure<RawKeyboard>(data);
                InputReceived?.Invoke(new RawInputEvent("Keyboard", header.Device.ToInt64().ToString("X"), DateTimeOffset.UtcNow,
                    ScanCode: keyboard.MakeCode, VirtualKey: keyboard.VirtualKey, IsKeyUp: (keyboard.Flags & 0x01) != 0));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_window != IntPtr.Zero) PostMessage(_window, 0x0010, IntPtr.Zero, IntPtr.Zero);
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    public sealed record RawInputEvent(string DeviceType, string DeviceHandle, DateTimeOffset Timestamp,
        int DeltaX = 0, int DeltaY = 0, ushort ButtonFlags = 0, short WheelDelta = 0,
        ushort ScanCode = 0, ushort VirtualKey = 0, bool IsKeyUp = false);

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClass
    {
        public uint Size; public uint Style; public IntPtr Procedure; public int ClassExtra; public int WindowExtra;
        public IntPtr Instance; public IntPtr Icon; public IntPtr Cursor; public IntPtr Background;
        public string? MenuName; public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputHeader { public uint Type, Size; public IntPtr Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RawMouse
    {
        public ushort Flags; public uint Buttons; public ushort ButtonFlags, ButtonData; public uint RawButtons;
        public int LastX, LastY; public uint ExtraInformation;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Hwnd; public uint Value; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WndClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string className, IntPtr instance);
}
