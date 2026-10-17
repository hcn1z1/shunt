using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shunt.Core.Models;
using Shunt.Engine.Hardware;

namespace Shunt.Engine;

/// <summary>HTTP control surface for the local Shunt Mode runtime.</summary>
public static class ShuntModeEndpoints
{
    public static void Map(WebApplication app, ShuntEngineService engine, EngineEventBroker events)
    {
        app.MapGet("/api/shunt/state", () => Results.Ok(new
        {
            Enabled = engine.IsShuntModeEnabled,
            GyroEnabled = engine.IsShuntGyroEnabled,
            Target = engine.ActiveVirtualGamepad?.Type.ToString()
        }));
        app.MapPost("/api/shunt/enable", (ShuntModeRequest request) => OperatingSystem.IsWindows()
            ? StartAsync(engine, events, request)
            : Task.FromResult<IResult>(Results.Problem("Shunt Mode HID input is available on Windows only.", statusCode: 501)));
        app.MapPost("/api/shunt/disable", () => OperatingSystem.IsWindows()
            ? StopAsync(engine, events)
            : Task.FromResult<IResult>(Results.Problem("Shunt Mode HID input is available on Windows only.", statusCode: 501)));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<IResult> StartAsync(ShuntEngineService engine, EngineEventBroker events, ShuntModeRequest request)
    {
        var device = HardwareImuDiscovery.Enumerate().FirstOrDefault(candidate =>
            string.Equals(candidate.DevicePath, request.DevicePath, StringComparison.OrdinalIgnoreCase));
        if (device is null) return Results.NotFound(new { Error = "The selected motion sensor is no longer connected." });
        var mode = request.ActivationMode == "hold" ? GyroActivatorMode.Hold : GyroActivatorMode.Toggle;
        var enabled = await engine.StartShuntModeAsync(device, mode);
        if (!enabled) return Results.BadRequest(new { Error = "Load a DualShock 4 virtual target before enabling Shunt Mode." });
        events.Publish(new { Type = "ShuntModeChanged", Enabled = true, GyroEnabled = false });
        return Results.Ok(new { Enabled = true, Device = device.Profile.Name });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<IResult> StopAsync(ShuntEngineService engine, EngineEventBroker events)
    {
        await engine.StopShuntModeAsync();
        events.Publish(new { Type = "ShuntModeChanged", Enabled = false, GyroEnabled = false });
        return Results.Ok(new { Enabled = false });
    }
}

public sealed record ShuntModeRequest(string DevicePath, string ActivationMode = "toggle");
