using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;

namespace Shunt.Emulation;

/// <summary>
/// Mock hardware discovery service for unit testing and headless environments without physical controllers.
/// </summary>
public sealed class StubHardwareDiscoveryService : IHardwareDiscoveryService
{
    public IReadOnlyList<IHardwareController> DiscoveredControllers { get; set; } = Array.Empty<IHardwareController>();
    public IHardwareController? SelectedController { get; set; }

    public event Action<IHardwareController>? ControllerConnected
    {
        add { }
        remove { }
    }

    public event Action<IHardwareController>? ControllerDisconnected
    {
        add { }
        remove { }
    }

    public event Action<IHardwareController?>? SelectedControllerChanged
    {
        add { }
        remove { }
    }

    public Task RefreshAsync() => Task.CompletedTask;
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
