using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Profiles;

namespace Shunt.Engine;

/// <summary>Loopback REST and SSE endpoint for the engine process.</summary>
public sealed class EngineHttpServer : IAsyncDisposable
{
    private readonly ShuntEngineService _engine;
    private readonly IVirtualGamepadManager _gamepads;
    private readonly IHidHideService _hidHide;
    private readonly string _profilesRoot;
    private readonly EngineEventBroker _events = new();
    private readonly CommandDispatcher _commands;
    private KeyPressedPollerService? _poller;
    private RawInputPoller? _rawInput;
    private MdnsServiceAdvertiser? _mdns;
    private WebApplication? _application;

    public EngineHttpServer(ShuntEngineService engine, IVirtualGamepadManager gamepads, IHidHideService? hidHide = null, string? profilesRoot = null)
    {
        _engine = engine;
        _gamepads = gamepads;
        _hidHide = hidHide ?? new HidHideService();
        _commands = new CommandDispatcher(engine);
        _profilesRoot = profilesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Shunt", "Profiles");
    }

    public async Task StartAsync(int port = 35474, CancellationToken cancellationToken = default)
    {
        if (_application is not null) throw new InvalidOperationException("The HTTP server is already running.");

        var builder = WebApplication.CreateSlimBuilder();
        bool lanDiscovery = string.Equals(Environment.GetEnvironmentVariable("OPENREWASD_LAN_DISCOVERY"), "1", StringComparison.Ordinal);
        builder.WebHost.UseUrls(lanDiscovery ? $"http://0.0.0.0:{port}" : $"http://127.0.0.1:{port}");
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNamingPolicy = null);
        var app = builder.Build();
        if (lanDiscovery)
        {
            _mdns = new MdnsServiceAdvertiser(port);
            _mdns.Start();
        }
        Directory.CreateDirectory(_profilesRoot);

        _engine.HardwareDiscovery.Start();
        _engine.HardwareDiscovery.ControllerConnected += c =>
            _events.Publish(new { Type = "ControllerConnected", Id = c.Id, Name = c.Name, ConnectionType = c.ConnectionType, BatteryLevel = c.BatteryLevel });
        _engine.HardwareDiscovery.ControllerDisconnected += c =>
            _events.Publish(new { Type = "ControllerDisconnected", Id = c.Id });

        // Real-time haptic/lightbar feedback visualization over SSE (plan 07 §6)
        _engine.VirtualGamepadFeedback += (_, args) =>
        {
            _events.Publish(new
            {
                Type = "VibrationFeedback",
                LargeMotor = args.LargeMotor,
                SmallMotor = args.SmallMotor,
                LedNumber = args.LedNumber,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            if (args.LedNumber != 0)
            {
                _events.Publish(new { Type = "LightbarChanged", LedNumber = args.LedNumber });
            }
        };

        app.MapGet("/Version", () => Results.Ok(new { Version = "v2.6", Engine = "Shunt" }));

        // Hardware button auto-focus sniffer -> SSE "KeyPressed" events (plan 09 §3)
        _poller = new KeyPressedPollerService(_engine.HardwareDiscovery, new BrokerPublisher(_events));
        _poller.StartPolling();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _rawInput = new RawInputPoller();
                _rawInput.InputReceived += input => _events.Publish(new
                {
                    Type = "RawInput",
                    input.DeviceType,
                    input.DeviceHandle,
                    input.Timestamp,
                    input.DeltaX,
                    input.DeltaY,
                    input.ButtonFlags,
                    input.WheelDelta,
                    input.ScanCode,
                    input.VirtualKey,
                    input.IsKeyUp
                });
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Raw Input capture could not be started.");
            }
        }

        object GetControllersList()
        {
            var list = new List<object>();
            var selectedId = _engine.HardwareDiscovery.SelectedController?.Id;
            foreach (var c in _engine.HardwareDiscovery.DiscoveredControllers)
            {
                list.Add(new
                {
                    Id = c.Id,
                    Name = c.Name,
                    Type = c.HardwareType,
                    ConnectionType = c.ConnectionType,
                    BatteryLevel = c.BatteryLevel,
                    IsCharging = c.IsCharging,
                    Connected = c.IsConnected,
                    IsSelected = string.Equals(c.Id, selectedId, StringComparison.OrdinalIgnoreCase)
                });
            }
            if (_engine.ActiveVirtualGamepad is { } pad)
            {
                list.Add(new
                {
                    Id = "virtual",
                    Name = pad.Type.ToString(),
                    Type = "Virtual",
                    ConnectionType = "Virtual",
                    BatteryLevel = -1,
                    IsCharging = false,
                    Connected = pad.IsConnected,
                    IsSelected = false
                });
            }
            return list;
        }

        app.MapGet("/api/controllers", () => Results.Ok(GetControllersList()));
        app.MapGet("/v2.6/ControllerService/GetControllers", () => Results.Ok(GetControllersList()));
        app.MapGet("/v2.6/ControllerService/Controllers", () => Results.Ok(GetControllersList()));

        app.MapPost("/api/controllers/{id}/identify", (string id) =>
        {
            var target = _engine.HardwareDiscovery.DiscoveredControllers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                         ?? _engine.HardwareDiscovery.SelectedController;
            target?.Vibrate(65535, 65535);
            _engine.Vibrate(65535, 65535);
            _ = Task.Delay(300).ContinueWith(_ =>
            {
                target?.Vibrate(0, 0);
                _engine.Vibrate(0, 0);
            });
            return Results.Ok(new { Result = true, Id = id });
        });

        app.MapPost("/api/controllers/{id}/select", (string id) =>
        {
            var match = _engine.HardwareDiscovery.DiscoveredControllers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _engine.HardwareDiscovery.SelectedController = match;
                return Results.Ok(new { Result = true, Id = id, Name = match.Name });
            }
            return Results.NotFound(new { Error = "Controller not found." });
        });
        app.MapGet("/api/games", () => Results.Ok(EnumerateGames()));
        app.MapGet("/api/games/{id}/profiles", (string id) => Results.Ok(EnumerateProfiles(id)));
        app.MapPost("/api/engine/apply", async (ApplyProfileRequest request) =>
        {
            var targetPath = request.EffectivePath;
            if (string.IsNullOrWhiteSpace(targetPath))
                return Results.BadRequest(new { Error = "ConfigPath is required." });
            var path = Path.GetFullPath(targetPath);
            if (!File.Exists(path)) return Results.NotFound(new { Error = "Profile file not found." });
            var profile = await ProfileLoader.LoadFromFileAsync(path);
            _engine.Start(profile, request.Slot);
            _events.Publish(new { Type = "ProfileApplied", ProfileName = Path.GetFileNameWithoutExtension(path), Slot = request.Slot });
            return Results.Ok(new { Status = "Success", Slot = request.Slot });
        });
        app.MapPost("/api/engine/stop", () =>
        {
            var wasRunning = _engine.IsRunning;
            _engine.Stop();
            if (wasRunning) _events.Publish(new { Type = "ProfileUnapplied" });
            return Results.Ok(new { Result = true });
        });
        app.MapPost("/api/engine/shutdown", (IHostApplicationLifetime lifetime) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                lifetime.StopApplication();
            });
            return Results.Ok(new { Status = "ShuttingDown" });
        });

        app.MapPost("/v2.6/ControllerService/ApplyConfig", async (ApplyProfileRequest request) =>
        {
            var targetPath = request.EffectivePath;
            if (string.IsNullOrWhiteSpace(targetPath)) return Results.BadRequest(new { Error = "ConfigPath is required." });
            var path = Path.GetFullPath(targetPath);
            if (!File.Exists(path)) return Results.NotFound(new { Error = "Profile file not found." });
            var profile = await ProfileLoader.LoadFromFileAsync(path);
            _engine.Start(profile, request.Slot);
            _events.Publish(new { Type = "RemapStateChanged", Id = request.ControllerId, Slot = request.Slot, State = "Enabled", ProfileName = Path.GetFileNameWithoutExtension(path) });
            return Results.Ok(new { Status = "Success", Slot = request.Slot });
        });
        app.MapPost("/v2.6/ControllerService/EnableRemap", (ControllerRequest _) =>
            Results.Ok(new { Result = _engine.IsRunning }));
        app.MapPost("/v2.6/ControllerService/DisableRemap", (ControllerRequest request) =>
        {
            _engine.Stop();
            _events.Publish(new { Type = "RemapStateChanged", Id = request.ControllerId, State = "Disabled" });
            return Results.Ok(new { Result = true });
        });
        app.MapGet("/v2.6/ControllerService/RemapState/{id}", (string id) => Results.Ok(new
        {
            IsRemaped = _engine.IsRunning,
            CurrentSlot = _engine.CurrentSlot,
            VirtualGamepadType = _engine.ActiveVirtualGamepad?.Type.ToString(),
            ActiveProfileName = _engine.ActiveProfile?.Config?.AppName,
            ControllerId = id
        }));
        app.MapPost("/v2.6/ControllerService/SelectSlot", (ControllerRequest request) =>
        {
            var success = _engine.SelectSlot(request.Slot);
            _events.Publish(new { Type = "SlotChanged", Id = request.ControllerId, Slot = request.Slot, Result = success });
            return Results.Ok(new { Result = success, request.Slot });
        });
        app.MapPost("/v2.6/ControllerService/ClearSlot", (ControllerRequest request) =>
        {
            _engine.ClearSlot(request.Slot);
            _events.Publish(new { Type = "SlotCleared", Id = request.ControllerId, Slot = request.Slot });
            return Results.Ok(new { Result = true });
        });
        app.MapPost("/v2.6/ControllerService/SendGamepadVibration", (VibrationRequest req) =>
        {
            _engine.Vibrate(req.LeftMotor, req.RightMotor);
            return Results.Ok(new { Result = true });
        });

        app.MapGet("/v2.6/GameService/GetGames", () => Results.Ok(EnumerateGames()));
        app.MapGet("/v2.6/GameService/GetConfig", async (string gameName, string configName) =>
        {
            var path = SafeProfilePath(gameName, configName);
            return path is null || !File.Exists(path) ? Results.NotFound() : Results.Content(await File.ReadAllTextAsync(path), "application/json");
        });
        app.MapPost("/v2.6/GameService/SaveConfig", async (SaveProfileRequest request) =>
        {
            var path = SafeProfilePath(request.GameName, request.ConfigName);
            if (path is null) return Results.BadRequest(new { Error = "Invalid profile name." });
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, request.Content);
            return Results.Ok(new { Result = true });
        });
        app.MapGet("/v2.6/EngineService/DriverInfo", () => Results.Ok(new
        {
            IsViGEmBusInstalled = _gamepads.IsDriverAvailable,
            ViGEmBusVersion = _gamepads.IsDriverAvailable ? "1.21.442.0" : null,
            IsHidHideInstalled = _hidHide.IsAvailable,
            HidHideVersion = _hidHide.DriverVersion
        }));

        // Diagnostics for the extended virtual targets (Switch Pro 0x30 / DS3 pressure reports)
        app.MapGet("/api/engine/output-report", () =>
        {
            var report = _engine.LastOutputReport;
            return Results.Ok(new
            {
                Target = _engine.ActiveTarget.ToString(),
                IsRunning = _engine.IsRunning,
                ReportLength = report?.Length ?? 0,
                ReportBase64 = report == null ? null : Convert.ToBase64String(report)
            });
        });

        app.MapGet("/api/engine/touchpad-status", () =>
        {
            var ds4 = _engine.ActiveVirtualGamepad as ViGEmDualShock4Gamepad;
            return Results.Ok(new
            {
                Target = _engine.ActiveVirtualGamepad?.Type.ToString(),
                EngineRunning = _engine.IsRunning,
                IsConnected = ds4?.IsConnected ?? false,
                ExtendedReportsDisabled = ds4?.ExtendedTouchpadDisabled ?? false,
                LastError = ds4?.TouchpadOutputError
            });
        });

        app.MapGet("/api/engine/mapping-status", () =>
        {
            var pipeline = _engine.Pipeline;
            var controller = _engine.HardwareDiscovery.SelectedController;
            var relevantRules = pipeline?.Context.Rules
                .Where(rule => rule.TriggerButtons.HasFlag(GamepadButtons.Back) || rule.TargetType == TargetOutputType.Keyboard)
                .Select(rule => new
                {
                    TriggerButtons = rule.TriggerButtons.ToString(),
                    OutputButtons = rule.OutputButtons.ToString(),
                    Target = rule.TargetType.ToString(),
                    Key = rule.OutputKey?.KeyName,
                    rule.ShiftLayerId,
                    rule.Activator,
                    rule.Description
                }).ToArray() ?? [];
            return Results.Ok(new
            {
                EngineRunning = _engine.IsRunning,
                Target = _engine.ActiveVirtualGamepad?.Type.ToString(),
                ProfileName = _engine.ActiveProfile?.Config?.AppName,
                ControllerId = controller?.Id,
                ControllerName = controller?.Name,
                ActiveShiftId = pipeline?.Context.ActiveShiftId,
                BaseShiftId = pipeline?.Context.BaseShiftId,
                RelevantRules = relevantRules,
                LastInputButtons = pipeline?.LastInputButtons.ToString(),
                LastPressedInputButtons = pipeline?.LastPressedInputButtons.ToString(),
                LastInputPressAt = pipeline?.LastInputPressAt,
                LastOutputButtons = pipeline?.LastOutputButtons.ToString(),
                KeyboardOutputAttempts = pipeline?.KeyboardOutputAttempts ?? 0,
                LastKeyboardKey = pipeline?.LastKeyboardKey,
                LastKeyboardRule = pipeline?.LastKeyboardRule,
                LastKeyboardSendSucceeded = pipeline?.LastKeyboardSendSucceeded,
                LastKeyboardSendErrorCode = pipeline?.LastKeyboardSendErrorCode
            });
        });

        // System command execution (plan 08 §2.5: all 18 RewasdUserCommand actions)
        _commands.CommandExecuted += (command, success, detail) =>
            _events.Publish(new { Type = "CommandExecuted", Command = command.ToString(), Success = success, Detail = detail });
        app.MapPost("/api/commands/{name}", (string name) =>
        {
            var command = CommandDispatcher.Resolve(name);
            if (command is null)
                return Results.BadRequest(new { Error = $"Unknown command '{name}'. See KeyScanCodeTable RewasdUserCommand entries." });
            var success = _commands.Execute(command.Value);
            return Results.Ok(new { Command = command.ToString(), Result = success });
        });
        app.MapGet("/v2.6/Events", async (HttpContext context) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            using var writeGate = new SemaphoreSlim(1, 1);
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var heartbeat = Task.Run(async () =>
            {
                while (!heartbeatCts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), heartbeatCts.Token);
                    await writeGate.WaitAsync(heartbeatCts.Token);
                    try
                    {
                        await context.Response.WriteAsync($"data: {{\"Type\":\"Heartbeat\",\"Timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}\n\n", heartbeatCts.Token);
                        await context.Response.Body.FlushAsync(heartbeatCts.Token);
                    }
                    finally { writeGate.Release(); }
                }
            }, heartbeatCts.Token);
            try
            {
                await foreach (var item in _events.Subscribe(context.RequestAborted))
                {
                    await writeGate.WaitAsync(context.RequestAborted);
                    try
                    {
                        await context.Response.WriteAsync($"data: {item}\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                    finally { writeGate.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                heartbeatCts.Cancel();
                try { await heartbeat; } catch (OperationCanceledException) { }
            }
        });

        ShuntModeEndpoints.Map(app, _engine, _events);
        await app.StartAsync(cancellationToken);
        _application = app;
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) =>
        _application?.WaitForShutdownAsync(cancellationToken) ?? Task.CompletedTask;

    private object[] EnumerateGames() => Directory.Exists(_profilesRoot)
        ? Directory.GetDirectories(_profilesRoot).Select(path => new
        {
            Id = Path.GetFileName(path),
            Name = Path.GetFileName(path),
            BoxArtPath = (string?)null,
            Configs = Directory.GetFiles(path, "*.rewasd").Select(Path.GetFileName).ToArray()
        }).Cast<object>().ToArray()
        : [];

    private string[] EnumerateProfiles(string id)
    {
        var directory = SafeProfileDirectory(id);
        return directory is null || !Directory.Exists(directory)
            ? []
            : Directory.GetFiles(directory, "*.rewasd").Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToArray();
    }

    private string? SafeProfilePath(string gameName, string configName)
    {
        if (Path.GetFileName(gameName) != gameName || Path.GetFileName(configName) != configName || !configName.EndsWith(".rewasd", StringComparison.OrdinalIgnoreCase)) return null;
        return Path.Combine(_profilesRoot, gameName, configName);
    }

    private string? SafeProfileDirectory(string gameName) => Path.GetFileName(gameName) == gameName
        ? Path.Combine(_profilesRoot, gameName)
        : null;

    public async ValueTask DisposeAsync()
    {
        _poller?.Dispose();
        _rawInput?.Dispose();
        if (_mdns is not null) await _mdns.DisposeAsync();
        if (_application is not null)
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
            _application = null;
        }
    }
}

/// <summary>Adapts the event broker to the poller's SSE publisher abstraction.</summary>
internal sealed class BrokerPublisher : ISSEEventPublisher
{
    private readonly EngineEventBroker _broker;
    public BrokerPublisher(EngineEventBroker broker) => _broker = broker;

    public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        _broker.Publish(payload);
        return Task.CompletedTask;
    }
}

public sealed record ApplyProfileRequest(string? ConfigPath = null, string? Path = null, int Slot = 0, string? ControllerId = null)
{
    public string EffectivePath => !string.IsNullOrWhiteSpace(ConfigPath) ? ConfigPath : (Path ?? string.Empty);
}
public sealed record ControllerRequest(string ControllerId, int Slot = 0);
public sealed record VibrationRequest(string ControllerId, ushort LeftMotor, ushort RightMotor, int DurationMs = 250);
public sealed record SaveProfileRequest(string GameName, string ConfigName, string Content);
