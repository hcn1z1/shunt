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
        engine.ShuntGyroStateChanged += enabled => events.Publish(new { Type = "ShuntGyroStateChanged", Enabled = enabled });
        engine.ShuntTouchpadActionChanged += action => events.Publish(new { Type = "ShuntTouchpadAction", Action = action });
        engine.ShuntModeError += message => events.Publish(new { Type = "ShuntModeError", Message = message });
        app.MapGet("/api/shunt/state", () => Results.Ok(new
        {
            Enabled = engine.IsShuntModeEnabled,
            GyroEnabled = engine.IsShuntGyroEnabled,
            Target = engine.ActiveVirtualGamepad?.Type.ToString(),
            DsuEnabled = engine.IsDsuServerEnabled,
            TouchpadAction = engine.LastTouchpadAction,
            TouchpadActionAt = engine.LastTouchpadActionAt
        }));
        app.MapPost("/api/shunt/enable", (ShuntModeRequest request) => OperatingSystem.IsWindows()
            ? StartAsync(engine, events, request)
            : Task.FromResult<IResult>(Results.Problem("Shunt Mode HID input is available on Windows only.", statusCode: 501)));
        app.MapPost("/api/shunt/disable", () => OperatingSystem.IsWindows()
            ? StopAsync(engine, events)
            : Task.FromResult<IResult>(Results.Problem("Shunt Mode HID input is available on Windows only.", statusCode: 501)));
        app.MapPost("/api/shunt/dsu/enable", () =>
        {
            var started = engine.StartDsuServer();
            if (!started) return Results.Conflict(new { Error = "The DSU port is already in use." });
            events.Publish(new { Type = "DsuServerChanged", Enabled = true, Port = DsuMotionServer.DefaultPort });
            return Results.Ok(new { Enabled = true, Port = DsuMotionServer.DefaultPort });
        });
        app.MapPost("/api/shunt/dsu/disable", async () =>
        {
            await engine.StopDsuServerAsync();
            events.Publish(new { Type = "DsuServerChanged", Enabled = false });
            return Results.Ok(new { Enabled = false });
        });
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
