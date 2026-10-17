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
        engine.ChipSyncStatusChanged += result => events.Publish(new { Type = "ChipSyncStatusChanged", Success = result.Success, Message = result.Message, ChunksWritten = result.ChunksWritten });
        app.MapGet("/api/shunt/state", () => Results.Ok(new
        {
            Enabled = engine.IsShuntModeEnabled,
            GyroEnabled = engine.IsShuntGyroEnabled,
            AutoSyncChip = engine.AutoSyncChipOnGyroToggle,
            Target = engine.ActiveVirtualGamepad?.Type.ToString(),
            DsuEnabled = engine.IsDsuServerEnabled,
            TouchpadAction = engine.LastTouchpadAction,
            TouchpadActionAt = engine.LastTouchpadActionAt,
            Pitch = engine.CurrentPitch,
            Roll = engine.CurrentRoll,
            Yaw = engine.CurrentYaw,
            AngularVelocity = engine.CurrentAngularVelocity,
            GravityG = engine.CurrentGravityG,
            ActiveSlot = engine.ActiveXInputSlot,
            HasHardwareImu = engine.HasHardwareImuActive,
            ModeHint = OperatingSystem.IsWindows() ? HardwareImuDiscovery.DetectControllerModeHint() : null
        }));
        app.MapPost("/api/shunt/enable", (ShuntModeRequest request) => OperatingSystem.IsWindows()
            ? StartAsync(engine, events, request)
            : Task.FromResult<IResult>(Results.Problem("Shunt Mode HID input is available on Windows only.", statusCode: 501)));
        app.MapPost("/api/shunt/disable", () => OperatingSystem.IsWindows()
            ? StopAsync(engine, events)
            : Task.FromResult<IResult>(Results.Problem("Shunt Mode HID input is available on Windows only.", statusCode: 501)));
        app.MapPost("/api/shunt/chip/flash", async (ChipFlashRequest request) =>
        {
            var targetStick = request.TargetStick == 3 ? GameSirGyroTargetStick.RightStick : GameSirGyroTargetStick.LeftStick;
            var result = await engine.FlashGameSirChipAsync(targetStick, alwaysActive: request.AlwaysActive);
            return Results.Ok(result);
        });
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
        HardwareImuDevice? device = null;
        if (!string.IsNullOrWhiteSpace(request.DevicePath) && !string.Equals(request.DevicePath, "virtual", StringComparison.OrdinalIgnoreCase))
        {
            device = HardwareImuDiscovery.Enumerate().FirstOrDefault(candidate =>
                string.Equals(candidate.DevicePath, request.DevicePath, StringComparison.OrdinalIgnoreCase));
        }

        var mode = request.ActivationMode == "hold" ? GyroActivatorMode.Hold : GyroActivatorMode.Toggle;
        var enabled = await engine.StartShuntModeAsync(device, mode);
        if (!enabled) return Results.BadRequest(new { Error = "Load a DualShock 4 virtual target before enabling Shunt Mode." });
        events.Publish(new { Type = "ShuntModeChanged", Enabled = true, GyroEnabled = engine.IsShuntGyroEnabled, DsuEnabled = engine.IsDsuServerEnabled });
        return Results.Ok(new { Enabled = true, GyroEnabled = engine.IsShuntGyroEnabled, DsuEnabled = engine.IsDsuServerEnabled, Device = device?.Profile.Name ?? "Any Xbox Controller (Left Stick Tilt)" });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<IResult> StopAsync(ShuntEngineService engine, EngineEventBroker events)
    {
        await engine.StopShuntModeAsync();
        events.Publish(new { Type = "ShuntModeChanged", Enabled = false, GyroEnabled = false });
        return Results.Ok(new { Enabled = false });
    }
}

public sealed record ShuntModeRequest(string? DevicePath = null, string ActivationMode = "toggle");
public sealed record ChipFlashRequest(bool AlwaysActive = true, int TargetStick = 1);
