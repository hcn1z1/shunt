using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Engine.Hardware;
using Shunt.Profiles;
using Shunt.Profiles.Models;

namespace Shunt.Engine;

public class ShuntEngineService : IDisposable
{
    private readonly IVirtualGamepadManager _gamepadManager;
    private readonly IHardwareDiscoveryService _hardwareDiscovery;
    private readonly IHidHideService? _hidHideService;
    private IVirtualGamepad? _activeVirtualGamepad;
    private MappingPipeline? _pipeline;
    private CancellationTokenSource? _cts;
    private readonly RewasdProfileDocument?[] _slotProfiles = new RewasdProfileDocument?[4];
    private readonly CommandDispatcher _commandDispatcher;
    private readonly XboxToDualShock4ShuntService _shuntRuntime = new();
    private readonly object _imuLock = new();
    private ImuSensorSample? _latestImuSample;
    private HardwareImuPoller? _activeImuPoller;
    private DsuMotionServer? _dsuServer;
    private string? _shuntBlockedDevice;
    private bool _shuntAddedDeviceBlock;
    private bool _filterWasActive;
    private ushort _reportCounter;

    public bool IsRunning { get; private set; }
    public bool IsShuntModeEnabled { get; private set; }
    public bool IsShuntGyroEnabled => _shuntRuntime.IsGyroEnabled;
    public int CurrentSlot { get; private set; } = 0;
    public RewasdProfileDocument? ActiveProfile => _slotProfiles[CurrentSlot];
    public IVirtualGamepad? ActiveVirtualGamepad => _activeVirtualGamepad;
    public MappingPipeline? Pipeline => _pipeline;
    public IHardwareDiscoveryService HardwareDiscovery => _hardwareDiscovery;
    public CommandDispatcher CommandDispatcher => _commandDispatcher;

    /// <summary>
    /// Fired when the game sends rumble or lightbar feedback to the virtual controller
    /// so the UI can visualize real-time haptics over SSE (plan 07 §6).
    /// </summary>
    public event EventHandler<FeedbackEventArgs>? VirtualGamepadFeedback;
    public event Action<bool>? ShuntGyroStateChanged;
    public event Action<string>? ShuntModeError;

    /// <summary>Last formatted HID output report for the active virtual target (diagnostics).</summary>
    public byte[]? LastOutputReport { get; private set; }

    /// <summary>Target of the currently active virtual controller.</summary>
    public VirtualControllerTarget ActiveTarget { get; private set; } = VirtualControllerTarget.The360;

    public ShuntEngineService(IVirtualGamepadManager gamepadManager, IHardwareDiscoveryService? hardwareDiscovery = null, IHidHideService? hidHideService = null)
    {
        _gamepadManager = gamepadManager;
        _hidHideService = hidHideService ?? (OperatingSystem.IsWindows() ? new HidHideService() : null);
        _commandDispatcher = new CommandDispatcher(this);
        _hardwareDiscovery = hardwareDiscovery ?? (gamepadManager is Emulation.StubVirtualGamepadManager
            ? new Emulation.StubHardwareDiscoveryService()
            : OperatingSystem.IsWindows() 
                ? new Hardware.HardwareDiscoveryService() 
                : throw new PlatformNotSupportedException("Hardware discovery is only supported on Windows."));
        _hardwareDiscovery.SelectedControllerChanged += OnSelectedControllerChanged;
        if (_hardwareDiscovery.SelectedController != null)
        {
            _hardwareDiscovery.SelectedController.InputReceived += OnHardwareInputReceived;
        }
    }

    private void OnSelectedControllerChanged(IHardwareController? controller)
    {
        if (controller != null)
        {
            controller.InputReceived += OnHardwareInputReceived;
        }
    }

    private void OnHardwareInputReceived(IHardwareController controller, GamepadState rawInput)
    {
        if (IsRunning)
        {
            DispatchInput(rawInput);
        }
    }

    /// <summary>
    /// Loads a profile and starts the mapping engine on the specified slot (0..3).
    /// </summary>
    public void Start(RewasdProfileDocument profile, int slot = 0)
    {
        if (slot is < 0 or > 3) slot = 0;
        CurrentSlot = slot;
        _slotProfiles[slot] = profile;

        if (IsRunning)
            StopVirtualGamepad();

        var compiled = ProfileCompiler.Compile(profile);
        _activeVirtualGamepad = _gamepadManager.CreateGamepad(compiled.TargetType);
        _activeVirtualGamepad.FeedbackReceived += OnVirtualGamepadFeedback;
        _activeVirtualGamepad.Connect();
        ActiveTarget = VirtualTargetMapper.ToTarget(compiled.TargetType);
        _reportCounter = 0;
        LastOutputReport = null;

        var ctx = new MappingExecutionContext
        {
            Rules = compiled.Rules,
            LeftStickDeadzone = compiled.LeftStickDeadzone,
            RightStickDeadzone = compiled.RightStickDeadzone,
            GyroSettings = compiled.GyroSettings.Clone(),
            MouseZones = CompileMouseZones(compiled.MouseZones),
            CommandDispatcher = _commandDispatcher
        };
        foreach (var kvp in compiled.ShiftParentMap)
        {
            ctx.ShiftParentMap[kvp.Key] = kvp.Value;
        }
        _pipeline = new MappingPipeline(ctx);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _hardwareDiscovery.Start();
        _hardwareDiscovery.SelectedController?.StartPolling();
        _shuntRuntime.GyroStateChanged += OnShuntGyroStateChanged;
    }

    /// <summary>
    /// Selects an active slot. If a profile is assigned to that slot, starts it.
    /// </summary>
    public bool SelectSlot(int slot)
    {
        if (slot is < 0 or > 3) return false;
        CurrentSlot = slot;
        var prof = _slotProfiles[slot];
        if (prof != null)
        {
            Start(prof, slot);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears the profile stored in a given slot.
    /// </summary>
    public void ClearSlot(int slot)
    {
        if (slot is >= 0 and <= 3)
        {
            _slotProfiles[slot] = null;
            if (CurrentSlot == slot && IsRunning)
            {
                Stop();
            }
        }
    }

    /// <summary>
    /// Feeds incoming hardware input directly into the pipeline and pushes output to the virtual controller.
    /// </summary>
    public void DispatchInput(GamepadState rawInput)
    {
        if (!IsRunning || _pipeline == null || _activeVirtualGamepad == null)
            return;

        var mapped = _pipeline.Process(rawInput);
        if (IsShuntModeEnabled)
        {
            ImuSensorSample? sample;
            lock (_imuLock) sample = _latestImuSample;
            mapped = _shuntRuntime.Process(mapped, Environment.TickCount64, sample);
        }
        _activeVirtualGamepad.UpdateState(mapped);
        _dsuServer?.UpdateState(mapped);

        // Format the device-specific HID report from the high-capacity virtual state (plan 07 §4)
        if (_activeVirtualGamepad is StubVirtualGamepad stub)
        {
            var virtualState = VirtualGamepadStateBridge.ToVirtual(mapped);
            stub.SubmitVirtualState(virtualState);
            try
            {
                var target = Emulation.Targets.VirtualTargetFactory.Create(ActiveTarget);
                LastOutputReport = target.BuildInputReport(virtualState, _reportCounter++);
            }
            catch (NotSupportedException) { }
        }
    }

    private void OnVirtualGamepadFeedback(object? sender, FeedbackEventArgs e)
    {
        VirtualGamepadFeedback?.Invoke(this, e);
    }

    private static MouseZoneConfig? CompileMouseZones(ProfileMouseSettings? settings)
    {
        if (settings is null || settings.Zones.Count == 0) return null;
        var zones = settings.Zones;
        var deadzone = zones.FirstOrDefault(zone => zone.Name.Equals("deadzone", StringComparison.OrdinalIgnoreCase));
        var low = zones.FirstOrDefault(zone => zone.Name.Equals("low", StringComparison.OrdinalIgnoreCase));
        var medium = zones.FirstOrDefault(zone => zone.Name is "med" or "medium");
        var high = zones.FirstOrDefault(zone => zone.Name.Equals("high", StringComparison.OrdinalIgnoreCase));
        var config = new MouseZoneConfig(
            deadzone is null ? 0.05f : deadzone.Radius / 32767f,
            low is null ? 0.30f : low.Radius / 32767f,
            medium is null ? 0.70f : medium.Radius / 32767f,
            high is null ? 0.92f : high.Radius / 32767f,
            low?.Multiplier ?? 0.5f,
            medium?.Multiplier ?? 1f,
            high?.Multiplier ?? 2.2f,
            high?.Action);
        try
        {
            config.Validate();
            return config;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends rumble haptic vibration to the virtual gamepad and physical controller.
    /// </summary>
    public void Vibrate(ushort leftMotor, ushort rightMotor)
    {
        _activeVirtualGamepad?.SetVibration((byte)(leftMotor >> 8), (byte)(rightMotor >> 8));
        _hardwareDiscovery.SelectedController?.Vibrate(leftMotor, rightMotor);
    }

    /// <summary>Starts Xbox-to-DS4 runtime filtering after the UI completes the hardware check.</summary>
    public bool EnableShuntMode(GyroActivatorMode activationMode = GyroActivatorMode.Toggle)
    {
        if (!IsRunning || _activeVirtualGamepad?.Type != VirtualGamepadType.DualShock4) return false;
        _shuntRuntime.ActivationMode = activationMode;
        _shuntRuntime.Reset();
        IsShuntModeEnabled = true;
        return true;
    }

    public void DisableShuntMode()
    {
        IsShuntModeEnabled = false;
        _shuntRuntime.Reset();
        lock (_imuLock) _latestImuSample = null;
        RestoreDeviceIsolation();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task<bool> StartShuntModeAsync(HardwareImuDevice device, GyroActivatorMode activationMode = GyroActivatorMode.Toggle)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!EnableShuntMode(activationMode)) return false;
        if (!TryEnableDeviceIsolation())
        {
            DisableShuntMode();
            ShuntModeError?.Invoke("HidHide is unavailable or the selected physical controller could not be cloaked.");
            return false;
        }
        await StopImuPollerAsync().ConfigureAwait(false);

        var poller = device.CreatePoller();
        poller.SampleReceived += UpdateImuSample;
        poller.Faulted += OnImuPollerFaulted;
        _activeImuPoller = poller;
        try
        {
            poller.Start();
            return true;
        }
        catch (Exception ex)
        {
            poller.SampleReceived -= UpdateImuSample;
            poller.Faulted -= OnImuPollerFaulted;
            await poller.DisposeAsync().ConfigureAwait(false);
            _activeImuPoller = null;
            DisableShuntMode();
            ShuntModeError?.Invoke(ex.Message);
            return false;
        }
    }

    private bool TryEnableDeviceIsolation()
    {
        var controller = _hardwareDiscovery.SelectedController;
        var hider = _hidHideService;
        if (controller is null || hider is null || !hider.IsAvailable) return false;
        var executable = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executable) || !hider.AddApplicationWhitelist(executable)) return false;

        _filterWasActive = hider.IsActive;
        var blocked = hider.GetBlockedDevices();
        _shuntAddedDeviceBlock = !blocked.Contains(controller.Id, StringComparer.OrdinalIgnoreCase);
        if (!hider.BlockDevice(controller.Id)) return false;
        _shuntBlockedDevice = controller.Id;
        hider.IsActive = true;
        return hider.IsActive;
    }

    private void RestoreDeviceIsolation()
    {
        var hider = _hidHideService;
        if (hider is null) return;
        if (_shuntAddedDeviceBlock && !string.IsNullOrWhiteSpace(_shuntBlockedDevice))
            _ = hider.UnblockDevice(_shuntBlockedDevice);
        if (_shuntBlockedDevice is not null) hider.IsActive = _filterWasActive;
        _shuntBlockedDevice = null;
        _shuntAddedDeviceBlock = false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task StopShuntModeAsync()
    {
        DisableShuntMode();
        await StopImuPollerAsync().ConfigureAwait(false);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task StopImuPollerAsync()
    {
        var poller = _activeImuPoller;
        _activeImuPoller = null;
        if (poller is null) return;
        poller.SampleReceived -= UpdateImuSample;
        poller.Faulted -= OnImuPollerFaulted;
        await poller.DisposeAsync().ConfigureAwait(false);
    }

    private void OnImuPollerFaulted(object? sender, Exception error)
    {
        DisableShuntMode();
        ShuntModeError?.Invoke(error.Message);
    }

    /// <summary>Supplies the most recent physical HID IMU sample to the running Shunt Mode frame loop.</summary>
    public void UpdateImuSample(ImuSensorSample sample)
    {
        lock (_imuLock) _latestImuSample = sample;
    }

    private void OnShuntGyroStateChanged(bool enabled) => ShuntGyroStateChanged?.Invoke(enabled);

    public bool StartDsuServer(int port = DsuMotionServer.DefaultPort)
    {
        if (_dsuServer?.IsRunning == true) return true;
        try
        {
            _dsuServer = new DsuMotionServer(port);
            _dsuServer.Start();
            return true;
        }
        catch (SocketException)
        {
            _dsuServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dsuServer = null;
            return false;
        }
    }

    public async Task StopDsuServerAsync()
    {
        var server = _dsuServer;
        _dsuServer = null;
        if (server is not null) await server.DisposeAsync().ConfigureAwait(false);
    }

    private void StopVirtualGamepad()
    {
        if (OperatingSystem.IsWindows())
            StopShuntModeAsync().GetAwaiter().GetResult();
        else
            DisableShuntMode();
        _shuntRuntime.GyroStateChanged -= OnShuntGyroStateChanged;
        if (_activeVirtualGamepad != null)
        {
            _activeVirtualGamepad.FeedbackReceived -= OnVirtualGamepadFeedback;
            _activeVirtualGamepad.Disconnect();
            _activeVirtualGamepad.Dispose();
        }
        _activeVirtualGamepad = null;
        _pipeline = null;
        LastOutputReport = null;
    }

    /// <summary>
    /// Stops the mapping engine and disconnects virtual controllers.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        StopVirtualGamepad();
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
        StopDsuServerAsync().GetAwaiter().GetResult();
        _hardwareDiscovery.Dispose();
        _gamepadManager.Dispose();
    }
}
