using System;
using System.Collections.Generic;
using System.Linq;
using Shunt.Core.MathTransform;
using Shunt.Core.Models;
using Shunt.Profiles.Models;

namespace Shunt.Profiles;

/// <summary>
/// Compiled profile state ready for zero-overhead execution in the mapping engine.
/// </summary>
public record CompiledProfile
{
    public DeadzoneConfig LeftStickDeadzone { get; init; } = new();
    public DeadzoneConfig RightStickDeadzone { get; init; } = new();
    public List<ResolvedMappingRule> Rules { get; init; } = [];
    public Dictionary<int, int> ShiftParentMap { get; init; } = new();
    public VirtualGamepadType TargetType { get; init; } = VirtualGamepadType.Xbox360;
    public GyroscopeSettings GyroSettings { get; init; } = new();
}

/// <summary>
/// Compiles high-level .rewasd JSON documents into optimized rule tables and deadzones.
/// </summary>
public static class ProfileCompiler
{
    public static CompiledProfile Compile(RewasdProfileDocument doc)
    {
        var targetType = ProfileLoader.DetermineTargetType(doc);
        var maskLookup = doc.Masks.ToDictionary(m => m.Id, m => m.Set);

        var rules = new List<ResolvedMappingRule>();

        if (doc.Mappings != null)
        {
            foreach (var mapping in doc.Mappings)
            {
                var cond = mapping.Condition;
                int shiftId = cond?.ShiftId ?? 1;

                GamepadButtons triggerButtons = GamepadButtons.None;
                if (cond?.Mask != null && maskLookup.TryGetValue(cond.Mask.Id, out var triggers))
                {
                    foreach (var trig in triggers)
                    {
                        var btn = ButtonIdResolver.ResolveButtonId(trig.ButtonId, trig.Description);
                        triggerButtons |= btn;
                    }
                }

                if (triggerButtons == GamepadButtons.None) continue;

                var activator = MappingActivator.Single;
                var modeStr = cond?.Mask?.Activator?.Mode?.ToLowerInvariant() ?? "hold_until_release";
                var typeStr = cond?.Mask?.Activator?.Type?.ToLowerInvariant() ?? "single";

                activator = typeStr switch
                {
                    "double" => MappingActivator.Double,
                    "triple" => MappingActivator.Triple,
                    "long" or "long_press" => MappingActivator.Long,
                    "start" or "rising" => MappingActivator.Start,
                    "release" or "falling" => MappingActivator.Release,
                    _ => MappingActivator.Single
                };

                bool toggle = modeStr.Contains("toggle");
                bool turbo = modeStr.Contains("turbo");

                GamepadButtons outputButtons = GamepadButtons.None;
                TargetOutputType targetTypeOutput = TargetOutputType.Gamepad;
                KeyboardKeyAction? outputKey = null;
                MouseActionType outputMouse = MouseActionType.None;
                int? targetShift = null;
                int? outputCommandId = null;

                if (mapping.Macros != null)
                {
                    foreach (var macro in mapping.Macros)
                    {
                        if (macro.Gamepad != null)
                        {
                            var outBtn = ButtonIdResolver.ResolveButtonId(macro.Gamepad.ButtonId, macro.Gamepad.Description);
                            outputButtons |= outBtn;
                            targetTypeOutput = TargetOutputType.Gamepad;
                        }

                        if (macro.Keyboard != null)
                        {
                            targetTypeOutput = TargetOutputType.Keyboard;
                            string desc = macro.Keyboard.Description ?? macro.Keyboard.ButtonId?.ToString() ?? macro.Keyboard.KeyId.ToString();
                            outputKey = KeyboardKeyResolver.ResolveKey(desc) ?? new KeyboardKeyAction(macro.Keyboard.ButtonId ?? macro.Keyboard.KeyId, macro.Keyboard.KeyId, desc);
                        }

                        if (macro.Mouse != null)
                        {
                            targetTypeOutput = TargetOutputType.Mouse;
                            if (macro.Mouse.ButtonId == 1) outputMouse = MouseActionType.LeftClick;
                            else if (macro.Mouse.ButtonId == 2) outputMouse = MouseActionType.RightClick;
                            else if (macro.Mouse.ButtonId == 3) outputMouse = MouseActionType.MiddleClick;
                            else if (string.Equals(macro.Mouse.Wheel, "up", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.WheelUp;
                            else if (string.Equals(macro.Mouse.Wheel, "down", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.WheelDown;
                        }

                        if (macro.Command != null)
                        {
                            targetTypeOutput = TargetOutputType.Command;
                            outputCommandId = macro.Command.Id;
                        }
                    }
                }

                // If output buttons weren't specified in macro, check description
                if (outputButtons == GamepadButtons.None && outputKey == null && outputMouse == MouseActionType.None && outputCommandId == null && !string.IsNullOrEmpty(mapping.Description))
                {
                    var keyResolved = KeyboardKeyResolver.ResolveKey(mapping.Description);
                    if (keyResolved != null)
                    {
                        targetTypeOutput = TargetOutputType.Keyboard;
                        outputKey = keyResolved;
                    }
                    else if (mapping.Description.Contains("mouse", StringComparison.OrdinalIgnoreCase))
                    {
                        targetTypeOutput = TargetOutputType.Mouse;
                        if (mapping.Description.Contains("left", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.LeftClick;
                        else if (mapping.Description.Contains("right", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.RightClick;
                        else if (mapping.Description.Contains("middle", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.MiddleClick;
                        else if (mapping.Description.Contains("wheel up", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.WheelUp;
                        else if (mapping.Description.Contains("wheel down", StringComparison.OrdinalIgnoreCase)) outputMouse = MouseActionType.WheelDown;
                    }
                    else
                    {
                        outputButtons = ButtonIdResolver.ResolveButtonId(0, mapping.Description);
                    }
                }

                rules.Add(new ResolvedMappingRule
                {
                    TriggerButtons = triggerButtons,
                    OutputButtons = outputButtons,
                    TargetType = targetTypeOutput,
                    OutputKey = outputKey,
                    OutputMouse = outputMouse,
                    OutputCommandId = outputCommandId,
                    Activator = activator,
                    Toggle = toggle,
                    Turbo = turbo,
                    TargetShiftId = targetShift,
                    ShiftLayerId = shiftId,
                    Description = mapping.Description
                });
            }
        }

        // Extract stick settings if available
        var leftDeadzone = new DeadzoneConfig();
        var rightDeadzone = new DeadzoneConfig();

        var hw = doc.Devices?.Hardware?.FirstOrDefault();
        var stickShift = hw?.Shifts?.FirstOrDefault();
        if (stickShift?.Sticks != null)
        {
            foreach (var stick in stickShift.Sticks)
            {
                if (stick.Name.Contains("left", StringComparison.OrdinalIgnoreCase) && stick.Sensitivity != null)
                {
                    float factor = Math.Clamp(stick.Sensitivity.X / 32767f, 0.1f, 1.0f);
                    leftDeadzone = leftDeadzone with { InnerDeadzone = 0.08f * (1f / factor) };
                }
                else if (stick.Name.Contains("right", StringComparison.OrdinalIgnoreCase) && stick.Sensitivity != null)
                {
                    float factor = Math.Clamp(stick.Sensitivity.X / 32767f, 0.1f, 1.0f);
                    rightDeadzone = rightDeadzone with { InnerDeadzone = 0.08f * (1f / factor) };
                }
            }
        }

        var shiftParentMap = new Dictionary<int, int>();
        if (doc.Shifts != null)
        {
            foreach (var s in doc.Shifts)
            {
                if (s.ParentId != 0 && s.ParentId != s.Id)
                {
                    shiftParentMap[s.Id] = s.ParentId;
                }
                else if (s.Id != 1)
                {
                    shiftParentMap[s.Id] = 1; // Default fallback to layer 1 (base layer)
                }
            }
        }

        var gyroSettings = new GyroscopeSettings();
        if (doc.Gyro != null)
        {
            var g = doc.Gyro;
            gyroSettings.OperatingMode = g.Mode.ToLowerInvariant() switch
            {
                "mouse" or "mouseaim" => GyroOperatingMode.MouseAim,
                "right_stick" or "rightstick" => GyroOperatingMode.RightStick,
                "left_stick" or "leftstick" => GyroOperatingMode.LeftStick,
                "wheel" or "steeringwheel" => GyroOperatingMode.SteeringWheel,
                "tilt" or "directionaltilt" => GyroOperatingMode.DirectionalTilt,
                _ => GyroOperatingMode.MouseAim
            };

            gyroSettings.ActivatorMode = g.Activator.ToLowerInvariant() switch
            {
                "hold" => GyroActivatorMode.Hold,
                "toggle" => GyroActivatorMode.Toggle,
                "inverted_hold" or "invertedhold" => GyroActivatorMode.InvertedHold,
                _ => GyroActivatorMode.AlwaysOn
            };

            gyroSettings.ActivatorButton = g.ActivatorButton ?? "LeftTrigger";
            gyroSettings.MultiplierX = g.SensitivityX;
            gyroSettings.MultiplierY = g.SensitivityY;
            gyroSettings.Deadzone = g.Deadzone;
            gyroSettings.Acceleration = g.Acceleration;
            gyroSettings.InvertX = g.InvertX;
            gyroSettings.InvertY = g.InvertY;
            gyroSettings.HorizontalAxis = g.HorizontalAxis.Equals("roll", StringComparison.OrdinalIgnoreCase)
                ? GyroHorizontalAxis.Roll
                : GyroHorizontalAxis.Yaw;
            gyroSettings.SteeringRangeDegrees = g.SteeringRange;
            gyroSettings.TiltThresholdLow = g.TiltThresholdLow;
            gyroSettings.TiltThresholdMed = g.TiltThresholdMed;
            gyroSettings.TiltThresholdHigh = g.TiltThresholdHigh;
        }

        return new CompiledProfile
        {
            TargetType = targetType,
            LeftStickDeadzone = leftDeadzone,
            RightStickDeadzone = rightDeadzone,
            Rules = rules,
            ShiftParentMap = shiftParentMap,
            GyroSettings = gyroSettings
        };
    }
}
