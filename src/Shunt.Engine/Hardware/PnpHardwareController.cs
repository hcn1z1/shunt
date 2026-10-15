using System;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;

namespace Shunt.Engine.Hardware;

public sealed class PnpHardwareController : IHardwareController
{
    private readonly DiscoveredDeviceInfo _info;
    private IHardwareController? _innerPoller;
    private bool _isDisposed;
    private GamepadState _currentState = new();

    public string Id => _info.InstanceId;
    public string Name => _info.Name;
    public string HardwareType => _info.HardwareType;
    public string ConnectionType => _info.ConnectionType;
    public int BatteryLevel => _innerPoller?.BatteryLevel ?? -1;
    public bool IsCharging => _innerPoller?.IsCharging ?? false;
    public bool IsConnected { get; private set; } = true;
    public GamepadState CurrentState => _innerPoller?.CurrentState ?? _currentState;

    public event Action<IHardwareController, GamepadState>? InputReceived;

    public PnpHardwareController(DiscoveredDeviceInfo info, IHardwareController? innerPoller = null)
    {
        _info = info;
        _innerPoller = innerPoller;
        if (_innerPoller != null)
        {
            _innerPoller.InputReceived += (sender, state) =>
            {
                _currentState = state;
                InputReceived?.Invoke(this, state);
            };
        }
    }

    public void AttachPoller(IHardwareController poller)
    {
        _innerPoller?.StopPolling();
        _innerPoller = poller;
        _innerPoller.InputReceived += (sender, state) =>
        {
            _currentState = state;
            InputReceived?.Invoke(this, state);
        };
        _innerPoller.StartPolling();
    }

    public void Vibrate(ushort leftMotor, ushort rightMotor)
    {
        _innerPoller?.Vibrate(leftMotor, rightMotor);
    }

    public void StartPolling()
    {
        _innerPoller?.StartPolling();
    }

    public void StopPolling()
    {
        _innerPoller?.StopPolling();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _innerPoller?.Dispose();
    }
}
