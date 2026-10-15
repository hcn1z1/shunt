using System.Collections.Generic;
using System.Threading.Tasks;
using Shunt.Core.Models;
using Shunt.Engine;
using Shunt.Profiles;
using Xunit;

namespace Shunt.Tests;

public class MappableSystemCommandsTests
{
    [Fact]
    public void ProfileCompiler_CompilesCommandMacro_Correctly()
    {
        var json = """
        {
          "schemaVersion": 3.0,
          "appVersion": "Shunt",
          "masks": [
            {
              "id": 1,
              "set": [
                { "deviceId": 1, "buttonId": 1, "description": "A / Cross" }
              ]
            }
          ],
          "mappings": [
            {
              "description": "A to Gyro Toggle",
              "condition": {
                "shiftId": 1,
                "mask": {
                  "id": 1,
                  "activator": { "type": "single", "mode": "hold_until_release" }
                }
              },
              "macros": [
                {
                  "command": {
                    "id": 6,
                    "description": "Toggle Gyro On/Off"
                  }
                }
              ]
            }
          ]
        }
        """;

        var profile = ProfileLoader.LoadFromString(json);
        var compiled = ProfileCompiler.Compile(profile);

        Assert.Single(compiled.Rules);
        var rule = compiled.Rules[0];
        Assert.Equal(GamepadButtons.A, rule.TriggerButtons);
        Assert.Equal(TargetOutputType.Command, rule.TargetType);
        Assert.Equal(6, rule.OutputCommandId);
    }

    [Fact]
    public async Task MappingPipeline_ExecutesCommandOnRisingEdgeOnly()
    {
        var executedCommands = new List<RewasdUserCommand>();
        var dispatcher = new CommandDispatcher();
        dispatcher.CommandExecuted += (cmd, success, _) =>
        {
            if (success) executedCommands.Add(cmd);
        };

        var rule = new ResolvedMappingRule
        {
            TriggerButtons = GamepadButtons.A,
            TargetType = TargetOutputType.Command,
            OutputCommandId = 6, // RewasdUserCommand.GyroTiltToggleOnOff
            Activator = MappingActivator.Single
        };

        var context = new MappingExecutionContext
        {
            Rules = [rule],
            CommandDispatcher = dispatcher
        };

        var pipeline = new MappingPipeline(context);

        // Frame 1: Press A (rising edge)
        var inputFrame1 = new GamepadState { Buttons = GamepadButtons.A };
        pipeline.Process(inputFrame1);

        // Allow async dispatcher to complete
        await Task.Delay(50);
        Assert.Single(executedCommands);
        Assert.Equal(RewasdUserCommand.GyroTiltToggleOnOff, executedCommands[0]);

        // Frame 2: Hold A (continuous hold - should NOT re-trigger)
        var inputFrame2 = new GamepadState { Buttons = GamepadButtons.A };
        pipeline.Process(inputFrame2);

        await Task.Delay(50);
        Assert.Single(executedCommands); // Still 1

        // Frame 3: Release A
        var inputFrame3 = new GamepadState { Buttons = GamepadButtons.None };
        pipeline.Process(inputFrame3);

        await Task.Delay(50);
        Assert.Single(executedCommands); // Still 1

        // Frame 4: Press A again (second rising edge)
        var inputFrame4 = new GamepadState { Buttons = GamepadButtons.A };
        pipeline.Process(inputFrame4);

        await Task.Delay(50);
        Assert.Equal(2, executedCommands.Count);
        Assert.Equal(RewasdUserCommand.GyroTiltToggleOnOff, executedCommands[1]);
    }

    [Fact]
    public void CommandDispatcher_ExecuteById_ValidatesAndDispatches()
    {
        var executed = new List<RewasdUserCommand>();
        var pipeline = new MappingPipeline(new MappingExecutionContext());
        var dispatcher = new CommandDispatcher { PipelineOverride = pipeline };
        dispatcher.CommandExecuted += (cmd, success, _) =>
        {
            if (success) executed.Add(cmd);
        };

        // Command 6 = GyroTiltToggleOnOff
        var success = dispatcher.Execute(6);
        Assert.True(success);
        Assert.Single(executed);
        Assert.Equal(RewasdUserCommand.GyroTiltToggleOnOff, executed[0]);

        // Invalid command ID (e.g. 999)
        var invalidSuccess = dispatcher.Execute(999);
        Assert.False(invalidSuccess);
        Assert.Single(executed);
    }
}
