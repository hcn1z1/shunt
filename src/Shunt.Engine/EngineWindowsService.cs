using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shunt.Emulation;

namespace Shunt.Engine;

/// <summary>Windows Service lifetime for the local Shunt engine host.</summary>
public sealed class EngineWindowsService : BackgroundService
{
    private readonly ILogger<EngineWindowsService> _logger;
    private ViGEmGamepadManager? _gamepads;
    private ShuntEngineService? _engine;
    private EngineHttpServer? _server;

    public EngineWindowsService(ILogger<EngineWindowsService> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _gamepads = new ViGEmGamepadManager();
        _engine = new ShuntEngineService(_gamepads);
        _server = new EngineHttpServer(_engine, _gamepads);
        int port = int.TryParse(Environment.GetEnvironmentVariable("OPENREWASD_PORT"), out var configuredPort)
            && configuredPort is > 0 and <= 65535 ? configuredPort : 35474;
        await _server.StartAsync(port, stoppingToken);
        _logger.LogInformation("Shunt engine service listening on loopback port {Port}.", port);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_server is not null) await _server.DisposeAsync();
        _engine?.Dispose();
        _gamepads?.Dispose();
    }
}
