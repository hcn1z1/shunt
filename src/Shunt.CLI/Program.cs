using System;
using System.IO;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shunt.Compiler;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Engine;
using Shunt.Profiles;
using Shunt.Profiles.Models;

namespace Shunt.CLI;

internal class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("serve", StringComparison.OrdinalIgnoreCase))
        {
            await RunServerAsync(args.Length > 1 && int.TryParse(args[1], out var port) ? port : 35474);
            return;
        }

        if (args.Length > 0 && args[0].Equals("service", StringComparison.OrdinalIgnoreCase))
        {
            await RunWindowsServiceAsync(args[1..]);
            return;
        }

        if (args.Length > 0 && (args[0].Equals("install-service", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("uninstall-service", StringComparison.OrdinalIgnoreCase)))
        {
            int result = await WindowsServiceInstaller.RunAsync(args);
            if (result >= 0) Environment.ExitCode = result;
            return;
        }

        if (args.Length > 0 && args[0].Equals("compile", StringComparison.OrdinalIgnoreCase))
        {
            await RunCompileCommandAsync(args);
            return;
        }

        if (await TryRunServiceCommand(args)) return;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===================================================================");
        Console.WriteLine("   Shunt - Open-Source Controller Mapper & Emulation Engine   ");
        Console.WriteLine("===================================================================");
        Console.ResetColor();

        using var manager = new ViGEmGamepadManager();
        Console.WriteLine($"[Driver] ViGEmBus Available: {(manager.IsDriverAvailable ? "YES" : "NO (using Stub Simulation)")}");

        // Create sample default profile
        var profile = new RewasdProfileDocument
        {
            AppVersion = "1.0.0",
            Config = new ProfileConfig { AppName = "Shunt Demo", MinTimeUnit = 1 },
            Devices = new ProfileDevices
            {
                Hardware =
                [
                    new HardwareDevice { Id = 1, Name = "gamepad", Type = "standard" }
                ],
                Virtual = new VirtualDevices
                {
                    Gamepads = [new VirtualGamepadEntry { Id = 1, Type = "360" }]
                }
            },
            Shifts =
            [
                new ProfileShift { Id = 1, Type = "default", Description = "Primary Layer" }
            ]
        };

        // If a profile argument is provided, load it
        if (args.Length > 0 && File.Exists(args[0]))
        {
            Console.WriteLine($"[Profile] Loading file: {args[0]}");
            profile = await ProfileLoader.LoadFromFileAsync(args[0]);
        }

        using var engine = new ShuntEngineService(manager);
        engine.Start(profile);

        Console.WriteLine($"[Engine] Status: RUNNING");
        Console.WriteLine($"[Engine] Virtual Target: {engine.ActiveVirtualGamepad?.Type}");
        Console.WriteLine($"[Engine] Connected: {engine.ActiveVirtualGamepad?.IsConnected}");

        // Simulate an input test
        Console.WriteLine("[Test] Dispatching simulated inputs (Left Stick 50% X, Button A Pressed)...");
        var testInput = new GamepadState
        {
            Buttons = GamepadButtons.A,
            LeftStick = new StickPosition(0.5f, 0.0f)
        };
        engine.DispatchInput(testInput);

        Console.WriteLine("[Test] Dispatching simulated inputs (Left Stick Neutral, Button B Pressed)...");
        var testInput2 = new GamepadState
        {
            Buttons = GamepadButtons.B,
            LeftStick = StickPosition.Center
        };
        engine.DispatchInput(testInput2);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[Success] Engine cycle completed successfully.");
        Console.ResetColor();

        engine.Stop();
        Console.WriteLine("[Engine] Stopped.");
    }

    private static async Task RunServerAsync(int port)
    {
        var manager = new ViGEmGamepadManager();
        using var engine = new ShuntEngineService(manager);
        await using var server = new EngineHttpServer(engine, manager);
        await server.StartAsync(port);
        Console.WriteLine($"Shunt service listening at http://127.0.0.1:{port}/. Press Ctrl+C to stop.");

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        };
        Console.CancelKeyPress += handler;
        try
        {
            await shutdown.Task;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static async Task RunWindowsServiceAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows Service host can only run on Windows.");

        using var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = "Shunt Engine")
            .ConfigureServices(services => services.AddHostedService<EngineWindowsService>())
            .Build();
        await host.RunAsync();
    }

    private static async Task<bool> TryRunServiceCommand(string[] args)
    {
        if (args.Length == 0) return false;
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:35474/") };
        HttpResponseMessage response;
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "version":
                    response = await client.GetAsync("Version");
                    break;
                case "apply_config" when args.Length >= 2:
                    response = await client.PostAsJsonAsync("api/engine/apply", new { ConfigPath = Path.GetFullPath(args[1]), Slot = 0 });
                    break;
                case "stop":
                    response = await client.PostAsJsonAsync("api/engine/stop", new { });
                    break;
                case "select_slot" when args.Length >= 3 && int.TryParse(args[2], out var slot):
                    response = await client.PostAsJsonAsync("v2.6/ControllerService/SelectSlot", new { ControllerId = args[1], Slot = slot });
                    break;
                case "clear_slot" when args.Length >= 3 && int.TryParse(args[2], out var clearSlot):
                    response = await client.PostAsJsonAsync("v2.6/ControllerService/ClearSlot", new { ControllerId = args[1], Slot = clearSlot });
                    break;
                case "remap" when args.Length >= 3:
                    response = args[2].Equals("on", StringComparison.OrdinalIgnoreCase)
                        ? await client.PostAsJsonAsync("v2.6/ControllerService/EnableRemap", new { ControllerId = args[1], Slot = 0 })
                        : await client.PostAsJsonAsync("v2.6/ControllerService/DisableRemap", new { ControllerId = args[1] });
                    break;
                default:
                    return false;
            }
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("Could not connect to the Shunt engine at http://127.0.0.1:35474/.");
            Environment.ExitCode = 1;
            return true;
        }

        using (response)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            if (!response.IsSuccessStatusCode)
                Environment.ExitCode = 1;
        }
        return true;
    }

    private static async Task RunCompileCommandAsync(string[] args)
    {
        string? inputPath = null;
        string? outputPath = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-i" && i + 1 < args.Length) inputPath = args[++i];
            else if (args[i] == "-o" && i + 1 < args.Length) outputPath = args[++i];
            else if (inputPath == null && !args[i].StartsWith("-")) inputPath = args[i];
            else if (outputPath == null && !args[i].StartsWith("-")) outputPath = args[i];
        }

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.Error.WriteLine("Error: Please provide a valid input profile path with -i <profile.rewasd>");
            Environment.ExitCode = 1;
            return;
        }

        outputPath ??= Path.ChangeExtension(inputPath, ".bin");

        try
        {
            var doc = await ProfileLoader.LoadFromFileAsync(inputPath);
            var bytes = BytecodeWriter.CompileToBytes(doc);
            await File.WriteAllBytesAsync(outputPath, bytes);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Compiler] Successfully compiled '{inputPath}' -> '{outputPath}' ({bytes.Length} bytes, RWSD bytecode).");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Compiler Error] Compilation failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
