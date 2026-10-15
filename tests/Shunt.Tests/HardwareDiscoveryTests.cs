using System;
using System.Linq;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using Shunt.Engine.Hardware;
using Xunit;

namespace Shunt.Tests;

public class HardwareDiscoveryTests
{
    [Fact]
    public void ClassifyDevice_RecognizesStandardControllers()
    {
        var (gameSirName, gameSirType) = PnpDeviceEnumerator.ClassifyDevice(0x3537, 0x1004);
        Assert.Contains("GameSir", gameSirName);
        Assert.Equal("Xbox", gameSirType);

        var (xboxName, xboxType) = PnpDeviceEnumerator.ClassifyDevice(0x045E, 0x028E);
        Assert.Contains("Xbox 360", xboxName);
        Assert.Equal("Xbox", xboxType);

        var (ds4Name, ds4Type) = PnpDeviceEnumerator.ClassifyDevice(0x054C, 0x05C4);
        Assert.Contains("DualShock 4", ds4Name);
        Assert.Equal("DualShock4", ds4Type);

        var (dualsenseName, dualsenseType) = PnpDeviceEnumerator.ClassifyDevice(0x054C, 0x0CE6);
        Assert.Contains("DualSense", dualsenseName);
        Assert.Equal("DualSense", dualsenseType);

        var (switchName, switchType) = PnpDeviceEnumerator.ClassifyDevice(0x057E, 0x2009);
        Assert.Contains("Switch Pro", switchName);
        Assert.Equal("Switch", switchType);
    }

    [Fact]
    public void PnpDeviceEnumerator_FindsConnectedControllers()
    {
        var devices = PnpDeviceEnumerator.EnumerateControllers();
        Assert.NotNull(devices);
        // The host machine has a connected GameSir controller (VID 0x3537, PID 0x1004)
        var gameSir = devices.FirstOrDefault(d => d.VendorId == 0x3537 && d.ProductId == 0x1004);
        Assert.NotNull(gameSir);
        Assert.Contains("GameSir", gameSir.Name);
        Assert.Equal("Xbox", gameSir.HardwareType);
        Assert.True(gameSir.IsConnected);
    }

    [Fact]
    public async Task HardwareDiscoveryService_PopulatesControllersAndSelectsActive()
    {
        using var discovery = new HardwareDiscoveryService();
        await discovery.RefreshAsync();

        Assert.NotEmpty(discovery.DiscoveredControllers);
        Assert.NotNull(discovery.SelectedController);

        var selected = discovery.SelectedController;
        Assert.True(selected.IsConnected);
        Assert.False(string.IsNullOrWhiteSpace(selected.Name));
    }

    [Fact]
    public async Task HardwareDiscoveryService_CanChangeSelectedController()
    {
        using var discovery = new HardwareDiscoveryService();
        await discovery.RefreshAsync();

        if (discovery.DiscoveredControllers.Count > 0)
        {
            var first = discovery.DiscoveredControllers[0];
            discovery.SelectedController = first;
            Assert.Same(first, discovery.SelectedController);

            // Setting vibration on selected controller should not throw
            discovery.SelectedController.Vibrate(32768, 32768);
            discovery.SelectedController.Vibrate(0, 0);
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public HardwareDiscoveryTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void XInputNative_GetState_DoesNotThrow()
    {
        var state = new XInputNative.XINPUT_STATE();
        for (uint i = 0; i < 4; i++)
        {
            var result = XInputNative.GetState(i, ref state);
            _output.WriteLine($"Slot {i}: Result={result}, Buttons={state.Gamepad.wButtons}");
        }
    }
}

