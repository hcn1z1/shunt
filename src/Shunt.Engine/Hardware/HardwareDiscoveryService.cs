using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using SharpDX.DirectInput;

namespace Shunt.Engine.Hardware;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class HardwareDiscoveryService : IHardwareDiscoveryService
{
    private readonly object _lock = new();
    private readonly List<IHardwareController> _controllers = [];
    private readonly DirectInput _directInput = new();
    private Timer? _timer;
    private bool _isDisposed;
    private IHardwareController? _selectedController;

    public IReadOnlyList<IHardwareController> DiscoveredControllers
    {
        get
        {
            lock (_lock) return [.. _controllers];
        }
    }

    public IHardwareController? SelectedController
    {
        get => _selectedController;
        set
        {
            if (ReferenceEquals(_selectedController, value)) return;
            _selectedController?.StopPolling();
            _selectedController = value;
            _selectedController?.StartPolling();
            SelectedControllerChanged?.Invoke(_selectedController);
        }
    }

    public event Action<IHardwareController>? ControllerConnected;
    public event Action<IHardwareController>? ControllerDisconnected;
    public event Action<IHardwareController?>? SelectedControllerChanged;

    public HardwareDiscoveryService()
    {
    }

    public void Start()
    {
        if (_timer != null) return;
        _ = RefreshAsync();
        _timer = new Timer(_ => _ = RefreshAsync(), null, 1500, 2000);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public Task RefreshAsync()
    {
        if (_isDisposed) return Task.CompletedTask;

        var newlyFound = new List<IHardwareController>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Enumerate physical devices via Windows PnP (GIP, XboxComposite, USB Gamepad, HID)
        var pnpDevices = PnpDeviceEnumerator.EnumerateControllers();

        // 2. Scan XInput slots 0..3 for active state
        var xinputControllers = new List<XInputHardwareController>();
        for (uint i = 0; i < 4; i++)
        {
            var rawState = new XInputNative.XINPUT_STATE();
            if (XInputNative.GetState(i, ref rawState) == XInputNative.ERROR_SUCCESS)
            {
                var xctrl = new XInputHardwareController(i);
                xinputControllers.Add(xctrl);
            }
        }

        // 3. Scan DirectInput joysticks & gamepads
        var diControllers = new List<DirectInputHardwareController>();
        try
        {
            var diDevices = _directInput.GetDevices();
            foreach (var dev in diDevices)
            {
                // Only joysticks, gamepads, or 1st/3rd party controllers (filter out standard mice/keyboards)
                if (dev.Type is SharpDX.DirectInput.DeviceType.Gamepad or SharpDX.DirectInput.DeviceType.Joystick or SharpDX.DirectInput.DeviceType.FirstPerson or SharpDX.DirectInput.DeviceType.Flight or SharpDX.DirectInput.DeviceType.Driving)
                {
                    diControllers.Add(new DirectInputHardwareController(_directInput, dev));
                }
            }
        }
        catch
        {
            // Ignore DI enumeration failure
        }

        // 4. Merge PnP controllers with poller
        int xinputIndex = 0;
        foreach (var pnp in pnpDevices)
        {
            IHardwareController? poller = null;
            if (pnp.HardwareType == "Xbox" && xinputIndex < xinputControllers.Count)
            {
                poller = xinputControllers[xinputIndex++];
            }
            else if (diControllers.Count > 0)
            {
                poller = diControllers.FirstOrDefault(d => d.Name.Contains(pnp.Name, StringComparison.OrdinalIgnoreCase));
            }

            var ctrl = new PnpHardwareController(pnp, poller);
            newlyFound.Add(ctrl);
            seenIds.Add(ctrl.Id);
        }

        // Add remaining standalone XInput devices not matched by PnP
        for (; xinputIndex < xinputControllers.Count; xinputIndex++)
        {
            var xctrl = xinputControllers[xinputIndex];
            if (!seenIds.Contains(xctrl.Id))
            {
                newlyFound.Add(xctrl);
                seenIds.Add(xctrl.Id);
            }
        }

        // Add remaining standalone DirectInput devices not matched by PnP
        foreach (var di in diControllers)
        {
            if (!seenIds.Contains(di.Id))
            {
                newlyFound.Add(di);
                seenIds.Add(di.Id);
            }
        }

        // Reconcile with currently cached controllers under lock
        List<IHardwareController> connectedList = [];
        List<IHardwareController> disconnectedList = [];

        lock (_lock)
        {
            // Check disconnected
            for (int i = _controllers.Count - 1; i >= 0; i--)
            {
                var existing = _controllers[i];
                if (!seenIds.Contains(existing.Id))
                {
                    disconnectedList.Add(existing);
                    _controllers.RemoveAt(i);
                }
            }

            // Check newly connected
            foreach (var candidate in newlyFound)
            {
                if (!_controllers.Any(c => string.Equals(c.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _controllers.Add(candidate);
                    connectedList.Add(candidate);
                }
            }

            // If any PnpHardwareController has no poller or default state, attach an available XInput controller
            foreach (var pnp in _controllers.OfType<PnpHardwareController>())
            {
                if (xinputControllers.Count > 0)
                {
                    pnp.AttachPoller(xinputControllers[0]);
                }
            }

            // If selected controller was removed, null, or has default state, select active controller
            if (_selectedController == null || !_controllers.Contains(_selectedController))
            {
                SelectedController = _controllers.FirstOrDefault(c => c is XInputHardwareController) 
                                     ?? _controllers.FirstOrDefault(c => c is PnpHardwareController) 
                                     ?? _controllers.FirstOrDefault();
            }
        }

        // Fire events outside lock
        foreach (var dc in disconnectedList)
        {
            ControllerDisconnected?.Invoke(dc);
        }
        foreach (var c in connectedList)
        {
            ControllerConnected?.Invoke(c);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();

        lock (_lock)
        {
            foreach (var c in _controllers)
            {
                c.Dispose();
            }
            _controllers.Clear();
        }

        try
        {
            _directInput.Dispose();
        }
        catch
        {
            // Ignore
        }
    }
}
