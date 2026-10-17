using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Shunt.Core.MathTransform;
using Shunt.Core.Models;
using Shunt.Core.Scancodes;

namespace Shunt.Engine;

/// <summary>
/// State tracking for shift layers, deadzones, and activator states.
/// </summary>
public class MappingExecutionContext
{
    public int ActiveShiftId { get; set; } = 1; // Default layer
    public int BaseShiftId { get; set; } = 1;
    public Dictionary<int, int> ShiftParentMap { get; } = new(); // ChildShiftId -> ParentShiftId
    public DeadzoneConfig LeftStickDeadzone { get; set; } = new();
    public DeadzoneConfig RightStickDeadzone { get; set; } = new();
    public bool UseSquareStick { get; set; } = false;
    public ZoneThresholds TriggerZones { get; set; } = new(0.35f, 0.75f);
    public ZoneThresholds StickZones { get; set; } = new(0.35f, 0.75f);
    public MouseZoneConfig? MouseZones { get; set; }
    public List<ResolvedMappingRule> Rules { get; set; } = [];
    public ChordStateMachine ChordStateMachine { get; } = new();
    internal Dictionary<ResolvedMappingRule, ActivatorState> ActivatorStates { get; } = new();
    public MacroEngine MacroEngine { get; } = new();
    public GyroscopeSettings GyroSettings { get; set; } = new();
    public CommandDispatcher? CommandDispatcher { get; set; }
    internal bool GyroToggleActive { get; set; } = true;

    /// <summary>Exposes the gyro toggle state for engine commands and diagnostics.</summary>
    public bool IsGyroToggleActive => GyroToggleActive;
    internal bool LastActivatorPressed { get; set; } = false;
}

internal sealed class ActivatorState : ActivatorEvaluationState
{
}

/// <summary>
/// Maps the compiled-rule activator enum onto the polymorphic evaluator (plan 08 §3).
/// </summary>
internal static class ActivatorMapping
{
    public static ActivatorType ToActivatorType(this MappingActivator activator) => activator switch
    {
        MappingActivator.Double => ActivatorType.Double,
        MappingActivator.Triple => ActivatorType.Triple,
        MappingActivator.Long => ActivatorType.Long,
        MappingActivator.Start => ActivatorType.Start,
        MappingActivator.Release => ActivatorType.Release,
        _ => ActivatorType.Single
    };
}

/// <summary>
/// High-performance mapping pipeline executing real-time controller transforms,
/// multi-button chord resolution with component suppression, and zone partition evaluation.
/// </summary>
public class MappingPipeline
{
    public MappingExecutionContext Context { get; }
    public GamepadButtons LastInputButtons { get; private set; }
    public GamepadButtons LastPressedInputButtons { get; private set; }
    public DateTimeOffset? LastInputPressAt { get; private set; }
    public GamepadButtons LastOutputButtons { get; private set; }
    public int KeyboardOutputAttempts { get; private set; }
    public string? LastKeyboardKey { get; private set; }
    public string? LastKeyboardRule { get; private set; }
    public bool? LastKeyboardSendSucceeded { get; private set; }
    public int? LastKeyboardSendErrorCode { get; private set; }
    public MouseZoneResult? LastMouseZone { get; private set; }
    public event Action<MouseZoneResult>? EdgeZoneActionTriggered;
    private string? _lastEdgeAction;

    public MappingPipeline(MappingExecutionContext context)
    {
        Context = context;
    }

    /// <summary>
    /// Processes physical input through the current profile and outputs the synthesized virtual state.
    /// </summary>
    public GamepadState Process(GamepadState input)
    {
        var newlyPressed = input.Buttons & ~LastInputButtons;
        if (newlyPressed != GamepadButtons.None)
        {
            LastPressedInputButtons = newlyPressed;
            LastInputPressAt = DateTimeOffset.UtcNow;
        }
        LastInputButtons = input.Buttons;
        var output = new GamepadState();

        // 1. Apply deadzones and stick transforms
        var processedLeftStick = StickTransform.ApplyDeadzone(input.LeftStick, Context.LeftStickDeadzone);
        var processedRightStick = StickTransform.ApplyDeadzone(input.RightStick, Context.RightStickDeadzone);

        if (Context.MouseZones is { } mouseZones)
        {
            var evaluation = MouseZoneEvaluator.Evaluate(processedRightStick.X, processedRightStick.Y, mouseZones);
            LastMouseZone = evaluation;
            processedRightStick = evaluation.Zone == MouseZone.Deadzone
                ? StickPosition.Center
                : new StickPosition(
                    Math.Clamp(processedRightStick.X * evaluation.SensitivityMultiplier, -1f, 1f),
                    Math.Clamp(processedRightStick.Y * evaluation.SensitivityMultiplier, -1f, 1f));
            if (evaluation.IsEdge && evaluation.EdgeAction != _lastEdgeAction)
                EdgeZoneActionTriggered?.Invoke(evaluation);
            _lastEdgeAction = evaluation.IsEdge ? evaluation.EdgeAction : null;
        }

        if (Context.UseSquareStick)
        {
            processedLeftStick = StickTransform.ToSquare(processedLeftStick);
            processedRightStick = StickTransform.ToSquare(processedRightStick);
        }

        output = output with
        {
            LeftStick = processedLeftStick,
            RightStick = processedRightStick,
            LeftTrigger = input.LeftTrigger,
            RightTrigger = input.RightTrigger,
            GyroPitch = input.GyroPitch,
            GyroRoll = input.GyroRoll,
            GyroYaw = input.GyroYaw,
            GyroVelX = input.GyroVelX,
            GyroVelY = input.GyroVelY,
            GyroVelZ = input.GyroVelZ,
            AccelX = input.AccelX,
            AccelY = input.AccelY,
            AccelZ = input.AccelZ
        };

        // 2. Evaluate deflection zones
        float leftStickMag = MathF.Sqrt(input.LeftStick.X * input.LeftStick.X + input.LeftStick.Y * input.LeftStick.Y);
        float rightStickMag = MathF.Sqrt(input.RightStick.X * input.RightStick.X + input.RightStick.Y * input.RightStick.Y);
        var leftTriggerZone = Context.TriggerZones.Classify(input.LeftTrigger);
        var rightTriggerZone = Context.TriggerZones.Classify(input.RightTrigger);

        // 3. Evaluate Gyroscope motion processing
        var gyro = Context.GyroSettings;
        bool isGyroActive = gyro.ActivatorMode switch
        {
            GyroActivatorMode.AlwaysOn => true,
            GyroActivatorMode.Hold => EvaluateGyroActivator(input, gyro.ActivatorButton),
            GyroActivatorMode.InvertedHold => !EvaluateGyroActivator(input, gyro.ActivatorButton),
            GyroActivatorMode.Toggle => Context.GyroToggleActive,
            _ => true
        };

        if (gyro.ActivatorMode == GyroActivatorMode.Toggle)
        {
            bool pressed = EvaluateGyroActivator(input, gyro.ActivatorButton);
            if (pressed && !Context.LastActivatorPressed)
            {
                Context.GyroToggleActive = !Context.GyroToggleActive;
            }
            Context.LastActivatorPressed = pressed;
        }

        var outputButtons = GamepadButtons.None;

        if (isGyroActive)
        {
            float velX = input.GyroVelX - gyro.BiasX;
            float velY = input.GyroVelY - gyro.BiasY;
            float velZ = input.GyroVelZ - gyro.BiasZ;

            switch (gyro.OperatingMode)
            {
                case GyroOperatingMode.MouseAim:
                    float horiz = (gyro.HorizontalAxis == GyroHorizontalAxis.Roll) ? velZ : velY;
                    float vert = velX;
                    float speed = MathF.Sqrt(horiz * horiz + vert * vert);
                    if (speed > gyro.Deadzone)
                    {
                        float accel = MathF.Pow(Math.Max(1f, speed / Math.Max(1f, gyro.MaxDeflection)), Math.Max(0f, gyro.Acceleration - 1f));
                        float dx = horiz * gyro.MultiplierX * accel * (gyro.InvertX ? -1f : 1f);
                        float dy = -vert * gyro.MultiplierY * accel * (gyro.InvertY ? -1f : 1f);
                        try
                        {
                            VirtualInputSimulator.SendMouseMove((int)MathF.Round(dx * 0.25f), (int)MathF.Round(dy * 0.25f));
                        }
                        catch {}
                    }
                    break;

                case GyroOperatingMode.RightStick:
                    float rHoriz = (gyro.HorizontalAxis == GyroHorizontalAxis.Roll) ? velZ : velY;
                    float rDeflectX = Math.Clamp(rHoriz / Math.Max(1f, gyro.MaxDeflection) * gyro.MultiplierX * (gyro.InvertX ? -1f : 1f), -1f, 1f);
                    float rDeflectY = Math.Clamp(velX / Math.Max(1f, gyro.MaxDeflection) * gyro.MultiplierY * (gyro.InvertY ? -1f : 1f), -1f, 1f);
                    output = output with { RightStick = new StickPosition(rDeflectX, rDeflectY) };
                    break;

                case GyroOperatingMode.LeftStick:
                    float lHoriz = (gyro.HorizontalAxis == GyroHorizontalAxis.Roll) ? velZ : velY;
                    float lDeflectX = Math.Clamp(lHoriz / Math.Max(1f, gyro.MaxDeflection) * gyro.MultiplierX * (gyro.InvertX ? -1f : 1f), -1f, 1f);
                    float lDeflectY = Math.Clamp(velX / Math.Max(1f, gyro.MaxDeflection) * gyro.MultiplierY * (gyro.InvertY ? -1f : 1f), -1f, 1f);
                    output = output with { LeftStick = new StickPosition(lDeflectX, lDeflectY) };
                    break;

                case GyroOperatingMode.SteeringWheel:
                    float halfRange = Math.Max(10f, gyro.SteeringRangeDegrees * 0.5f);
                    float steerX = Math.Clamp(input.GyroRoll / halfRange * (gyro.InvertX ? -1f : 1f), -1f, 1f);
                    output = output with { LeftStick = new StickPosition(steerX, output.LeftStick.Y) };
                    break;

                case GyroOperatingMode.DirectionalTilt:
                    if (input.GyroPitch > gyro.TiltThresholdLow) outputButtons |= GamepadButtons.TiltUp;
                    if (input.GyroPitch < -gyro.TiltThresholdLow) outputButtons |= GamepadButtons.TiltDown;
                    if (input.GyroRoll < -gyro.TiltThresholdLow) outputButtons |= GamepadButtons.TiltLeft;
                    if (input.GyroRoll > gyro.TiltThresholdLow) outputButtons |= GamepadButtons.TiltRight;
                    break;
            }
        }

        // 4. Button rules with multi-button chord priority and component suppression
        var now = Stopwatch.GetTimestamp();
        var chordResolution = Context.ChordStateMachine.Resolve(
            input.Buttons,
            Context.Rules.Select(rule => rule.TriggerButtons),
            (long)(now * 1000d / Stopwatch.Frequency));

        // 3. Resolve shift layer inheritance hierarchy (ActiveShiftId -> parents -> BaseShiftId -> 0)
        var shiftHierarchy = new List<int> { Context.ActiveShiftId };
        int curr = Context.ActiveShiftId;
        while (curr != Context.BaseShiftId && Context.ShiftParentMap.TryGetValue(curr, out int parent) && !shiftHierarchy.Contains(parent))
        {
            shiftHierarchy.Add(parent);
            curr = parent;
        }
        if (!shiftHierarchy.Contains(Context.BaseShiftId))
            shiftHierarchy.Add(Context.BaseShiftId);
        if (!shiftHierarchy.Contains(0))
            shiftHierarchy.Add(0);

        // Child shift layers override parent shift layers for the same trigger combination and activator
        var resolvedRules = new Dictionary<(GamepadButtons Triggers, MappingActivator Activator), ResolvedMappingRule>();
        for (int i = shiftHierarchy.Count - 1; i >= 0; i--)
        {
            int shiftId = shiftHierarchy[i];
            foreach (var r in Context.Rules)
            {
                if (r.ShiftLayerId == shiftId)
                {
                    resolvedRules[(r.TriggerButtons, r.Activator)] = r;
                }
            }
        }

        // Sort rules: chords first (most buttons), then singles
        var activeRules = resolvedRules.Values
            .OrderByDescending(r => BitOperations.PopCount((ulong)r.TriggerButtons))
            .ToList();

        var suppressedButtons = GamepadButtons.None;
        var mappedTriggerButtons = GamepadButtons.None;

        foreach (var rule in activeRules)
        {
            if (rule.TriggerButtons == GamepadButtons.None) continue;

            // If any trigger button of this rule was consumed by a higher-priority chord, suppress it
            if ((rule.TriggerButtons & suppressedButtons) != 0)
            {
                continue;
            }

            var isChord = BitOperations.PopCount((ulong)rule.TriggerButtons) > 1;
            var pressed = isChord
                ? (chordResolution.ActiveChordButtons & rule.TriggerButtons) == rule.TriggerButtons
                : (chordResolution.FilteredButtons & rule.TriggerButtons) == rule.TriggerButtons;

            // If a chord (2+ buttons) is actively pressed, suppress its component buttons from single rules
            if (pressed && isChord)
            {
                suppressedButtons |= rule.TriggerButtons;
            }

            if (!Context.ActivatorStates.TryGetValue(rule, out var state))
                Context.ActivatorStates[rule] = state = new ActivatorState();

            var rising = pressed && !state.WasPressed;
            var active = ActivatorEvaluator.Evaluate(
                rule.Activator.ToActivatorType(), state, pressed, now,
                rule.HoldThresholdMs, rule.TapWindowMs);

            var shouldOutput = active;
            if (rule.Toggle)
                shouldOutput = Context.MacroEngine.EvaluateToggle(rule.TriggerButtons, rising, state.WasPressed);
            if (rule.Turbo)
                shouldOutput = Context.MacroEngine.EvaluateTurbo(rule.TriggerButtons, active, (long)(now * 1000d / Stopwatch.Frequency));

            if (shouldOutput)
            {
                if (rule.TargetShiftId.HasValue)
                {
                    Context.ActiveShiftId = rule.TargetShiftId.Value;
                }

                outputButtons |= rule.OutputButtons;
            }

            // Keyboard, Mouse, and Command output actions
            if (shouldOutput != state.WasOutputting)
            {
                if (rule.TargetType == TargetOutputType.Keyboard && rule.OutputKey != null)
                {
                    KeyboardOutputAttempts++;
                    LastKeyboardKey = rule.OutputKey.KeyName;
                    LastKeyboardRule = rule.Description;
                    try
                    {
                        LastKeyboardSendSucceeded = VirtualInputSimulator.SendKey(rule.OutputKey, shouldOutput, out var errorCode);
                        LastKeyboardSendErrorCode = errorCode;
                    }
                    catch (Exception ex)
                    {
                        LastKeyboardSendSucceeded = false;
                        LastKeyboardSendErrorCode = ex.HResult;
                    }
                }
                else if (rule.TargetType == TargetOutputType.Mouse && rule.OutputMouse != MouseActionType.None)
                {
                    try { VirtualInputSimulator.SendMouse(rule.OutputMouse, shouldOutput); } catch {}
                }
                else if (rule.TargetType == TargetOutputType.Command && rule.OutputCommandId.HasValue && shouldOutput)
                {
                    if (Context.CommandDispatcher is { } dispatcher)
                    {
                        dispatcher.PipelineOverride ??= this;
                        _ = dispatcher.ExecuteAsync(rule.OutputCommandId.Value);
                    }
                }
                state.WasOutputting = shouldOutput;
            }

            mappedTriggerButtons |= rule.TriggerButtons;
        }

        // 4. Pass through any unmapped buttons that are not suppressed by an active chord
        var unmappedButtons = chordResolution.FilteredButtons & ~suppressedButtons & ~mappedTriggerButtons;
        if (Context.Rules.Count == 0)
        {
            outputButtons |= input.Buttons;
        }
        else
        {
            outputButtons |= unmappedButtons;
        }

        LastOutputButtons = outputButtons;
        return output with { Buttons = outputButtons };
    }

    private static bool EvaluateGyroActivator(GamepadState input, string activatorButton)
    {
        return activatorButton.ToLowerInvariant() switch
        {
            "lefttrigger" or "lt" => input.LeftTrigger > 0.25f,
            "righttrigger" or "rt" => input.RightTrigger > 0.25f,
            "leftshoulder" or "lb" => input.HasButton(GamepadButtons.LeftShoulder),
            "rightshoulder" or "rb" => input.HasButton(GamepadButtons.RightShoulder),
            "touchpadclick" or "touchpad" => input.HasButton(GamepadButtons.TouchpadClick) || input.HasButton(GamepadButtons.Touchpad),
            "rightthumb" or "r3" => input.HasButton(GamepadButtons.RightThumb),
            "leftthumb" or "l3" => input.HasButton(GamepadButtons.LeftThumb),
            _ => input.LeftTrigger > 0.25f
        };
    }
}
