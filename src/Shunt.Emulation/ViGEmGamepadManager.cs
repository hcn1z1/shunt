using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using ICoreVirtualGamepad = Shunt.Core.Interfaces.IVirtualGamepad;

namespace Shunt.Emulation;

public class ViGEmGamepadManager : IVirtualGamepadManager
{
    private ViGEmClient? _client;

    public bool IsDriverAvailable { get; }
    public string? InitializationError { get; }

    public ViGEmGamepadManager()
    {
        try
        {
            _client = new ViGEmClient();
            IsDriverAvailable = true;
        }
        catch (VigemBusNotFoundException)
        {
            IsDriverAvailable = false;
            InitializationError = "ViGEmBus driver not found. Install ViGEmBus to enable virtual controllers.";
        }
        catch (Exception ex)
        {
            IsDriverAvailable = false;
            InitializationError = ex.Message;
        }
    }

    public ICoreVirtualGamepad CreateGamepad(VirtualGamepadType type)
    {
        if (IsDriverAvailable && _client != null)
        {
            return type switch
            {
                // ViGEmBus + official client only expose Xbox 360 and DualShock 4 targets.
                // Xbox One, DS3 and Switch Pro route through the stub device and the
                // Shunt.Emulation.Targets report formatters (plan 07 §4.2-4.5).
                VirtualGamepadType.DualShock4 => new ViGEmDualShock4Gamepad(_client),
                VirtualGamepadType.Xbox360 => new ViGEmXbox360Gamepad(_client),
                _ => new StubVirtualGamepad(type)
            };
        }

        // Graceful fallback: return stub gamepad for development, testing & headless simulation
        return new StubVirtualGamepad(type);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
