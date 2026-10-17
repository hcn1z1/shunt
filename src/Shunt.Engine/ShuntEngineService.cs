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
    private CancellationTokenSource? _shuntFrameCts;
    private Task? _shuntFrameTask;
    private GamepadState _latestShuntInput = new();
    private string? _shuntBlockedDevice;
    private bool _shuntAddedDeviceBlock;
    private bool _filterWasActive;
    private CancellationTokenSource? _gyroFeedbackCts;
    private int _gyroFeedbackVersion;
    private ushort _reportCounter;

    public bool IsRunning { get; private set; }
    public bool IsShuntModeEnabled { get; private set; }
    public bool IsShuntGyroEnabled => _shuntRuntime.IsGyroEnabled;
    public bool IsGyroEnabled => IsShuntGyroEnabled;
    public bool IsDsuServerEnabled => _dsuServer?.IsRunning == true;
    public bool IsDsuRunning => IsDsuServerEnabled;
    public string? LastTouchpadAction { get; private set; }
    public DateTimeOffset? LastTouchpadActionAt { get; private set; }
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
    public event Action<string>? ShuntTouchpadActionChanged;
    public event Action<string>? ShuntModeError;
    public event Action<Hardware.ChipFlashResult>? ChipSyncStatusChanged;
    public bool AutoSyncChipOnGyroToggle { get; set; } = true;

    /// <summary>Last formatted HID output report for the active virtual target (diagnostics).</summary>
    public byte[]? LastOutputReport { get; private set; }

    /// <summary>Target of the currently active virtual controller.</summary>
    public VirtualControllerTarget ActiveTarget { get; private set; } = VirtualControllerTarget.The360;

    public float CurrentPitch { get; private set; }
    public float CurrentRoll { get; private set; }
    public float CurrentYaw { get; private set; }
    public float CurrentAngularVelocity { get; private set; }
    public float CurrentGravityG { get; private set; } = 1.0f;
    public int ActiveXInputSlot { get; private set; } = -1;
    public bool HasHardwareImuActive => OperatingSystem.IsWindows() && _activeImuPoller != null && _activeImuPoller.IsRunning;

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
        _shuntRuntime.GyroStateChanged += OnShuntGyroStateChanged;
        _shuntRuntime.TouchpadActionChanged += OnShuntTouchpadActionChanged;
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
            lock (_imuLock) _latestShuntInput = mapped;
            return;
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

    /// <summary>Programmatically sets the Shunt gyro state, triggering haptic feedback and dynamic chip sync.</summary>
    public void SetShuntGyroEnabled(bool enabled) => _shuntRuntime.SetGyroEnabled(enabled);

    /// <summary>Starts Xbox-to-DS4 runtime filtering after the UI completes the hardware check.</summary>
    public bool EnableShuntMode(GyroActivatorMode activationMode = GyroActivatorMode.Toggle)
    {
        if (_activeVirtualGamepad?.Type != VirtualGamepadType.DualShock4)
        {
            if (_activeVirtualGamepad != null)
            {
                StopVirtualGamepad();
            }
            try
            {
                _activeVirtualGamepad = _gamepadManager.CreateGamepad(VirtualGamepadType.DualShock4);
                _activeVirtualGamepad.FeedbackReceived += OnVirtualGamepadFeedback;
                _activeVirtualGamepad.Connect();
                ActiveTarget = VirtualControllerTarget.Ds4;
                if (_pipeline == null)
                {
                    var ctx = new MappingExecutionContext
                    {
                        CommandDispatcher = _commandDispatcher
                    };
                    _pipeline = new MappingPipeline(ctx);
                }
                IsRunning = true;
            }
            catch
            {
                return false;
            }
        }
        _shuntRuntime.ActivationMode = activationMode;
        _shuntRuntime.Reset(defaultGyroEnabled: true);
        lock (_imuLock) _latestShuntInput = new GamepadState();
        IsShuntModeEnabled = true;
        StartShuntFrameLoop();
        return true;
    }

    public void DisableShuntMode()
    {
        IsShuntModeEnabled = false;
        _shuntFrameCts?.Cancel();
        _shuntRuntime.Reset();
        lock (_imuLock) _latestImuSample = null;
        RestoreDeviceIsolation();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task<bool> StartShuntModeAsync(HardwareImuDevice? device, GyroActivatorMode activationMode = GyroActivatorMode.Toggle)
    {
        if (!EnableShuntMode(activationMode)) return false;

        // Auto-start Cemuhook DSU motion server so ShadPS4 / emulators receive motion on UDP 26760 immediately
        StartDsuServer();

        if (!TryEnableDeviceIsolation())
        {
            ShuntModeError?.Invoke("Notice: Physical controller cloaking (HidHide) is inactive or non-elevated. Shunt Mode will continue without device isolation.");
        }
        await StopImuPollerAsync().ConfigureAwait(false);

        // Always ensure the physical controller input polling is active
        _hardwareDiscovery.Start();
        _hardwareDiscovery.SelectedController?.StartPolling();

        if (device is null)
        {
            // Shunt Mode active with Left Stick simulated gyro for any standard Xbox controller
            return true;
        }

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
            // Graceful fallback to virtual motion mode if the hardware HID endpoint cannot be read directly
            ShuntModeError?.Invoke($"Hardware IMU reader failed ({ex.Message}). Operating in virtual Left Stick gyro mode.");
            return true;
        }
    }

    private bool TryEnableDeviceIsolation()
    {
        var controller = _hardwareDiscovery.SelectedController;
        var hider = _hidHideService;
        if (hider is null || !hider.IsAvailable) return true;
        if (controller is null) return true;
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
        if (_shuntFrameTask is not null)
        {
            try { await _shuntFrameTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _shuntFrameTask = null;
        }
        _shuntFrameCts?.Dispose();
        _shuntFrameCts = null;
        await StopImuPollerAsync().ConfigureAwait(false);
        if (AutoSyncChipOnGyroToggle)
        {
            try { _ = FlashGameSirChipAsync(Hardware.GameSirGyroTargetStick.LeftStick, alwaysActive: false); } catch { }
        }
    }

    private void StartShuntFrameLoop()
    {
        _shuntFrameCts?.Cancel();
        _shuntFrameCts?.Dispose();
        _shuntFrameCts = new CancellationTokenSource();
        _shuntFrameTask = Task.Run(() => ShuntFrameLoopAsync(_shuntFrameCts.Token));
    }

    private async Task ShuntFrameLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rawXState = new Hardware.XInputNative.XINPUT_STATE();
            while (!cancellationToken.IsCancellationRequested && IsRunning && IsShuntModeEnabled)
            {
                GamepadState input;
                ImuSensorSample? sample;
                lock (_imuLock)
                {
                    input = _latestShuntInput;
                    sample = _latestImuSample;
                }

                if (OperatingSystem.IsWindows())
                {
                    int detectedSlot = -1;
                    for (uint slot = 0; slot < 4; slot++)
                    {
                        if (Hardware.XInputNative.GetState(slot, ref rawXState) == Hardware.XInputNative.ERROR_SUCCESS)
                        {
                            detectedSlot = (int)slot;
                            var converted = Hardware.XInputHardwareController.ConvertState(rawXState.Gamepad);
                            if (converted.LeftStick.X != 0f || converted.LeftStick.Y != 0f ||
                                converted.RightStick.X != 0f || converted.RightStick.Y != 0f ||
                                converted.Buttons != GamepadButtons.None ||
                                (input.LeftStick.X == 0f && input.LeftStick.Y == 0f && input.Buttons == GamepadButtons.None))
                            {
                                input = converted;
                            }
                            break;
                        }
                    }
                    ActiveXInputSlot = detectedSlot;
                }

                var output = _shuntRuntime.Process(input, Environment.TickCount64, sample);
                _activeVirtualGamepad?.UpdateState(output);
                _dsuServer?.UpdateState(output);

                CurrentPitch = output.GyroPitch;
                CurrentRoll = output.GyroRoll;
                CurrentYaw = output.GyroYaw;
                CurrentAngularVelocity = MathF.Sqrt(output.GyroVelX * output.GyroVelX + output.GyroVelY * output.GyroVelY + output.GyroVelZ * output.GyroVelZ);
                CurrentGravityG = MathF.Sqrt(output.AccelX * output.AccelX + output.AccelY * output.AccelY + output.AccelZ * output.AccelZ);

                await Task.Delay(4, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
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

    private void OnShuntGyroStateChanged(bool enabled)
    {
        ShuntGyroStateChanged?.Invoke(enabled);
        PlayGyroHapticFeedback(enabled);

        if (AutoSyncChipOnGyroToggle)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await FlashGameSirChipAsync(Hardware.GameSirGyroTargetStick.LeftStick, alwaysActive: enabled).ConfigureAwait(false);
                    ChipSyncStatusChanged?.Invoke(result);
                }
                catch (Exception ex)
                {
                    ChipSyncStatusChanged?.Invoke(new Hardware.ChipFlashResult(false, $"Auto chip sync failed: {ex.Message}", 0));
                }
            });
        }
    }

    private void PlayGyroHapticFeedback(bool enabled)
    {
        var controller = _hardwareDiscovery.SelectedController;
        if (controller is null) return;

        var version = Interlocked.Increment(ref _gyroFeedbackVersion);
        _gyroFeedbackCts?.Cancel();
        _gyroFeedbackCts?.Dispose();
        var cts = new CancellationTokenSource();
        _gyroFeedbackCts = cts;
        controller.Vibrate(0, 0);
        _ = RunGyroHapticAsync(controller, enabled, version, cts.Token);
    }

    private async Task RunGyroHapticAsync(IHardwareController controller, bool enabled, int version, CancellationToken token)
    {
        try
        {
            if (enabled)
            {
                controller.Vibrate(0, 52428);
                await Task.Delay(60, token).ConfigureAwait(false);
            }
            else
            {
                for (var pulse = 0; pulse < 2; pulse++)
                {
                    controller.Vibrate(26214, 26214);
                    await Task.Delay(35, token).ConfigureAwait(false);
                    controller.Vibrate(0, 0);
                    if (pulse == 0) await Task.Delay(35, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == Volatile.Read(ref _gyroFeedbackVersion)) controller.Vibrate(0, 0);
        }
    }

    private void OnShuntTouchpadActionChanged(string action)
    {
        LastTouchpadAction = action;
        LastTouchpadActionAt = DateTimeOffset.UtcNow;
        ShuntTouchpadActionChanged?.Invoke(action);
    }

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

    /// <summary>
    /// Writes gyro mapping configuration directly to the GameSir G7 Pro onboard chip (Profile 1).
    /// </summary>
    public async Task<ChipFlashResult> FlashGameSirChipAsync(GameSirGyroTargetStick stick = GameSirGyroTargetStick.LeftStick, bool alwaysActive = true, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var res = await GameSirG7ProChipWriter.FlashProfileToChipAsync(stick, alwaysActive, progress, ct).ConfigureAwait(false);
        ChipSyncStatusChanged?.Invoke(res);
        return res;
    }

    private void StopVirtualGamepad()
    {
        if (OperatingSystem.IsWindows())
            StopShuntModeAsync().GetAwaiter().GetResult();
        else
            DisableShuntMode();
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
