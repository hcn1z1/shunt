using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shunt.Core.Models;
using Shunt.Engine;
using Shunt.Profiles;
using Xunit;

namespace Shunt.Tests;

public class DS4AndMotionMappingTests
{
    [Fact]
    public void ButtonIdResolver_Resolves_DS4_Touchpad_And_Gyro_Codes()
    {
        Assert.Equal(GamepadButtons.TouchpadClick, ButtonIdResolver.ResolveButtonId(37));
        Assert.Equal(GamepadButtons.TouchpadTap, ButtonIdResolver.ResolveButtonId(38));
        Assert.Equal(GamepadButtons.SwipeUp, ButtonIdResolver.ResolveButtonId(41));
        Assert.Equal(GamepadButtons.SwipeDown, ButtonIdResolver.ResolveButtonId(42));
        Assert.Equal(GamepadButtons.SwipeLeft, ButtonIdResolver.ResolveButtonId(43));
        Assert.Equal(GamepadButtons.SwipeRight, ButtonIdResolver.ResolveButtonId(44));
        Assert.Equal(GamepadButtons.TiltLeft, ButtonIdResolver.ResolveButtonId(45));
        Assert.Equal(GamepadButtons.TiltRight, ButtonIdResolver.ResolveButtonId(46));
        Assert.Equal(GamepadButtons.TiltUp, ButtonIdResolver.ResolveButtonId(47));
        Assert.Equal(GamepadButtons.TiltDown, ButtonIdResolver.ResolveButtonId(48));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(49));
    }

    [Fact]
    public void ButtonIdResolver_Resolves_DS4_Touchpad_And_Gyro_Descriptions()
    {
        Assert.Equal(GamepadButtons.SwipeUp, ButtonIdResolver.ResolveButtonId(0, "DS4 Touchpad Swipe Up"));
        Assert.Equal(GamepadButtons.SwipeDown, ButtonIdResolver.ResolveButtonId(0, "Swipe Down"));
        Assert.Equal(GamepadButtons.SwipeLeft, ButtonIdResolver.ResolveButtonId(0, "Swipe Left"));
        Assert.Equal(GamepadButtons.SwipeRight, ButtonIdResolver.ResolveButtonId(0, "Swipe Right"));
        Assert.Equal(GamepadButtons.VGYRO, ButtonIdResolver.ResolveButtonId(0, "VGYRO Virtual Gyro Aim"));
        Assert.Equal(GamepadButtons.TiltLeft, ButtonIdResolver.ResolveButtonId(0, "Lean Left (Tilt Left)"));
        Assert.Equal(GamepadButtons.TiltRight, ButtonIdResolver.ResolveButtonId(0, "Lean Right (Tilt Right)"));
        Assert.Equal(GamepadButtons.TouchpadClick, ButtonIdResolver.ResolveButtonId(0, "Touchpad Click"));
        Assert.Equal(GamepadButtons.TouchpadTap, ButtonIdResolver.ResolveButtonId(0, "Touchpad Tap"));
    }

    [Fact]
    public void KeyboardKeyResolver_Resolves_Standard_DirectInput_ScanCodes()
    {
        var tab = KeyboardKeyResolver.ResolveKey("DIK_TAB");
        Assert.NotNull(tab);
        Assert.Equal(15, tab.ScanCode);
        Assert.Equal("Tab", tab.KeyName);

        var alt = KeyboardKeyResolver.ResolveKey("DIK_LMENU");
        Assert.NotNull(alt);
        Assert.Equal(56, alt.ScanCode);

        var pgUp = KeyboardKeyResolver.ResolveKey("DIK_PRIOR");
        Assert.NotNull(pgUp);
        Assert.Equal("PageUp", pgUp.KeyName);

        var pgDn = KeyboardKeyResolver.ResolveKey("DIK_NEXT");
        Assert.NotNull(pgDn);
        Assert.Equal("PageDown", pgDn.KeyName);

        var esc = KeyboardKeyResolver.ResolveKey("DIK_ESCAPE");
        Assert.NotNull(esc);
        Assert.Equal(1, esc.ScanCode);

        var enter = KeyboardKeyResolver.ResolveKey("DIK_RETURN");
        Assert.NotNull(enter);
        Assert.Equal(28, enter.ScanCode);
    }

    [Fact]
    public async Task ProfileCompiler_Compiles_Keyboard_And_Mouse_From_Real_Profile()
    {
        var fullPath = Path.GetFullPath(Path.Combine("../../../../../profiles/Gamepad to Keyboard and Mouse.rewasd"));
        if (!File.Exists(fullPath))
            fullPath = Path.GetFullPath(Path.Combine("../../../profiles/Gamepad to Keyboard and Mouse.rewasd"));

        if (File.Exists(fullPath))
        {
            var profile = await ProfileLoader.LoadFromFileAsync(fullPath);
            var compiled = ProfileCompiler.Compile(profile);

            Assert.NotEmpty(compiled.Rules);

            // Verify compiled rules include keyboard targets
            var keyboardRules = compiled.Rules.Where(r => r.TargetType == TargetOutputType.Keyboard).ToList();
            Assert.NotEmpty(keyboardRules);

            // Verify Tab key or LMenu key is compiled
            Assert.Contains(keyboardRules, r => r.OutputKey != null && (r.OutputKey.KeyName.Contains("Tab") || r.OutputKey.ScanCode == 15 || r.OutputKey.ScanCode == 56));

            // Verify compiled rules include mouse targets
            var mouseRules = compiled.Rules.Where(r => r.TargetType == TargetOutputType.Mouse).ToList();
            Assert.NotEmpty(mouseRules);
        }
    }

    [Fact]
    public void MappingPipeline_Executes_DS4_Swipe_To_Keyboard_Key()
    {
        var context = new MappingExecutionContext();
        var tabKey = KeyboardKeyResolver.ResolveKey("Tab")!;
        context.Rules.Add(new ResolvedMappingRule
        {
            TriggerButtons = GamepadButtons.SwipeUp,
            TargetType = TargetOutputType.Keyboard,
            OutputKey = tabKey,
            Activator = MappingActivator.Single,
            ShiftLayerId = 1
        });

        var pipeline = new MappingPipeline(context);

        // Input with SwipeUp active
        var inputPressed = new GamepadState { Buttons = GamepadButtons.SwipeUp };
        var output1 = pipeline.Process(inputPressed);
        Assert.NotNull(output1);

        // Input released
        var inputReleased = new GamepadState { Buttons = GamepadButtons.None };
        var output2 = pipeline.Process(inputReleased);
        Assert.NotNull(output2);
    }

    [Fact]
    public void MappingPipeline_Executes_VGYRO_To_Mouse()
    {
        var context = new MappingExecutionContext();
        context.Rules.Add(new ResolvedMappingRule
        {
            TriggerButtons = GamepadButtons.VGYRO,
            TargetType = TargetOutputType.Mouse,
            OutputMouse = MouseActionType.MoveAim,
            Activator = MappingActivator.Single,
            ShiftLayerId = 1
        });

        var pipeline = new MappingPipeline(context);

        var input = new GamepadState { Buttons = GamepadButtons.VGYRO };
        var output = pipeline.Process(input);
        Assert.NotNull(output);
    }
}
