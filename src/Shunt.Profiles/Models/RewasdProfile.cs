using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shunt.Profiles.Models;

public record RewasdProfileDocument
{
    [JsonPropertyName("schemaVersion")]
    public double SchemaVersion { get; init; } = 3.0;

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; init; } = "6.4.0";

    [JsonPropertyName("config")]
    public ProfileConfig? Config { get; init; }

    [JsonPropertyName("devices")]
    public ProfileDevices Devices { get; init; } = new();

    [JsonPropertyName("shifts")]
    public List<ProfileShift> Shifts { get; init; } = [];

    [JsonPropertyName("masks")]
    public List<ProfileMask> Masks { get; init; } = [];

    [JsonPropertyName("mappings")]
    public List<ProfileMapping>? Mappings { get; init; } = [];

    [JsonPropertyName("gyro")]
    public ProfileGyroConfig? Gyro { get; init; }
}

public record ProfileConfig
{
    [JsonPropertyName("appName")]
    public string? AppName { get; init; }

    [JsonPropertyName("minTimeUnit")]
    public int MinTimeUnit { get; init; } = 1;
}

public record ProfileDevices
{
    [JsonPropertyName("groups")]
    public List<DeviceGroup>? Groups { get; init; }

    [JsonPropertyName("hardware")]
    public List<HardwareDevice> Hardware { get; init; } = [];

    [JsonPropertyName("virtual")]
    public VirtualDevices? Virtual { get; init; }
}

public record DeviceGroup
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public record HardwareDevice
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("groupId")]
    public int? GroupId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("shifts")]
    public List<DeviceShift>? Shifts { get; init; }
}

public record DeviceShift
{
    [JsonPropertyName("shiftId")]
    public int ShiftId { get; init; }

    [JsonPropertyName("sticks")]
    public List<StickConfig>? Sticks { get; init; }
}

public record StickConfig
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("sensitivity")]
    public StickSensitivity? Sensitivity { get; init; }
}

public record StickSensitivity
{
    [JsonPropertyName("x")]
    public int X { get; init; } = 32767;

    [JsonPropertyName("y")]
    public int Y { get; init; } = 32767;
}

public record VirtualDevices
{
    [JsonPropertyName("gamepads")]
    public List<VirtualGamepadEntry>? Gamepads { get; init; }
}

public record VirtualGamepadEntry
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "360";
}

public record ProfileShift
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "default";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parentId")]
    public int ParentId { get; init; } = 0;
}

public record ProfileMask
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("set")]
    public List<MaskTrigger> Set { get; init; } = [];
}

public record MaskTrigger
{
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; init; }

    [JsonPropertyName("buttonId")]
    public int ButtonId { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public record ProfileMapping
{
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("condition")]
    public MappingCondition Condition { get; init; } = new();

    [JsonPropertyName("macros")]
    public List<MacroStep>? Macros { get; init; } = [];
}

public record MappingCondition
{
    [JsonPropertyName("shiftId")]
    public int ShiftId { get; init; } = 1;

    [JsonPropertyName("mask")]
    public ConditionMask? Mask { get; init; }
}

public record ConditionMask
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("activator")]
    public ConditionActivator? Activator { get; init; }
}

public record ConditionActivator
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "single"; // single, double, long, start, release

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "hold_until_release"; // hold_until_release, toggle, turbo
}

public record MacroStep
{
    [JsonPropertyName("gamepad")]
    public GamepadMacroTarget? Gamepad { get; init; }

    [JsonPropertyName("keyboard")]
    public KeyboardMacroTarget? Keyboard { get; init; }

    [JsonPropertyName("mouse")]
    public MouseMacroTarget? Mouse { get; init; }

    [JsonPropertyName("command")]
    public CommandMacroTarget? Command { get; init; }

    [JsonPropertyName("delay")]
    public int? DelayMs { get; init; }
}

public record CommandMacroTarget
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public record GamepadMacroTarget
{
    [JsonPropertyName("id")]
    public int Id { get; init; } = 1;

    [JsonPropertyName("buttonId")]
    public int ButtonId { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public record KeyboardMacroTarget
{
    [JsonPropertyName("buttonId")]
    public int? ButtonId { get; init; }

    [JsonPropertyName("keyId")]
    public int KeyId { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("isDown")]
    public bool IsDown { get; init; }
}

public record MouseMacroTarget
{
    [JsonPropertyName("buttonId")]
    public int? ButtonId { get; init; }

    [JsonPropertyName("wheel")]
    public string? Wheel { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public record ProfileGyroConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "mouse"; // mouse, right_stick, left_stick, wheel, tilt

    [JsonPropertyName("activator")]
    public string Activator { get; init; } = "always_on"; // always_on, hold, toggle, inverted_hold

    [JsonPropertyName("activatorButton")]
    public string? ActivatorButton { get; init; } = "LeftTrigger";

    [JsonPropertyName("sensitivityX")]
    public float SensitivityX { get; init; } = 1.0f;

    [JsonPropertyName("sensitivityY")]
    public float SensitivityY { get; init; } = 1.0f;

    [JsonPropertyName("deadzone")]
    public float Deadzone { get; init; } = 0.5f;

    [JsonPropertyName("acceleration")]
    public float Acceleration { get; init; } = 1.0f;

    [JsonPropertyName("invertX")]
    public bool InvertX { get; init; } = false;

    [JsonPropertyName("invertY")]
    public bool InvertY { get; init; } = false;

    [JsonPropertyName("horizontalAxis")]
    public string HorizontalAxis { get; init; } = "yaw"; // yaw, roll

    [JsonPropertyName("steeringRange")]
    public float SteeringRange { get; init; } = 270.0f;

    [JsonPropertyName("tiltThresholdLow")]
    public float TiltThresholdLow { get; init; } = 15.0f;

    [JsonPropertyName("tiltThresholdMed")]
    public float TiltThresholdMed { get; init; } = 30.0f;

    [JsonPropertyName("tiltThresholdHigh")]
    public float TiltThresholdHigh { get; init; } = 45.0f;
}


