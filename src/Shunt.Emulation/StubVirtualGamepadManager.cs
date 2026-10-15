using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using ICoreVirtualGamepad = Shunt.Core.Interfaces.IVirtualGamepad;

namespace Shunt.Emulation;

/// <summary>
/// Mock manager that always produces in-memory stub virtual controllers.
/// Ideal for testing, continuous integration, and driverless execution.
/// </summary>
public class StubVirtualGamepadManager : IVirtualGamepadManager
{
    public bool IsDriverAvailable => false;

    public ICoreVirtualGamepad CreateGamepad(VirtualGamepadType type)
    {
        return new StubVirtualGamepad(type);
    }

    public void Dispose()
    {
    }
}
