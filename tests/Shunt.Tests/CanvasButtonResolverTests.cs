using Shunt.Core.Scancodes;
using Xunit;

namespace Shunt.Tests;

public class CanvasButtonResolverTests
{
    [Theory]
    [InlineData("A", "A")]
    [InlineData("Cross", "A")]
    [InlineData("BTN_A", "A")]
    [InlineData("A / Cross", "A")]
    [InlineData("B", "B")]
    [InlineData("Circle", "B")]
    [InlineData("BTN_B", "B")]
    [InlineData("X", "X")]
    [InlineData("Square", "X")]
    [InlineData("BTN_X", "X")]
    [InlineData("Y", "Y")]
    [InlineData("Triangle", "Y")]
    [InlineData("BTN_Y", "Y")]
    public void ResolveAnchor_StandardActionButtons_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("LS", "LS")]
    [InlineData("LeftStick", "LS")]
    [InlineData("Left Stick", "LS")]
    [InlineData("LeftThumb", "LS")]
    [InlineData("L3", "LS")]
    [InlineData("BTN_LEFT_STICK", "LS")]
    [InlineData("RS", "RS")]
    [InlineData("RightStick", "RS")]
    [InlineData("Right Stick", "RS")]
    [InlineData("RightThumb", "RS")]
    [InlineData("R3", "RS")]
    [InlineData("BTN_RIGHT_STICK", "RS")]
    public void ResolveAnchor_Sticks_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("DUp", "DUp")]
    [InlineData("D-Up", "DUp")]
    [InlineData("DpadUp", "DUp")]
    [InlineData("D-pad Up", "DUp")]
    [InlineData("BTN_DPAD_UP", "DUp")]
    [InlineData("DDown", "DDown")]
    [InlineData("D-Down", "DDown")]
    [InlineData("DpadDown", "DDown")]
    [InlineData("BTN_DPAD_DOWN", "DDown")]
    [InlineData("DLeft", "DLeft")]
    [InlineData("D-Left", "DLeft")]
    [InlineData("DpadLeft", "DLeft")]
    [InlineData("BTN_DPAD_LEFT", "DLeft")]
    [InlineData("DRight", "DRight")]
    [InlineData("D-Right", "DRight")]
    [InlineData("DpadRight", "DRight")]
    [InlineData("BTN_DPAD_RIGHT", "DRight")]
    public void ResolveAnchor_Dpad_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("LB", "LB")]
    [InlineData("LeftShoulder", "LB")]
    [InlineData("Left Bumper", "LB")]
    [InlineData("L1", "LB")]
    [InlineData("BTN_LEFT_SHOULDER", "LB")]
    [InlineData("RB", "RB")]
    [InlineData("RightShoulder", "RB")]
    [InlineData("Right Bumper", "RB")]
    [InlineData("R1", "RB")]
    [InlineData("BTN_RIGHT_SHOULDER", "RB")]
    [InlineData("LT", "LT")]
    [InlineData("LeftTrigger", "LT")]
    [InlineData("Left Trigger", "LT")]
    [InlineData("L2", "LT")]
    [InlineData("BTN_LEFT_TRIGGER", "LT")]
    [InlineData("RT", "RT")]
    [InlineData("RightTrigger", "RT")]
    [InlineData("Right Trigger", "RT")]
    [InlineData("R2", "RT")]
    [InlineData("BTN_RIGHT_TRIGGER", "RT")]
    public void ResolveAnchor_BumpersAndTriggers_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("View", "View")]
    [InlineData("Back", "View")]
    [InlineData("Share", "View")]
    [InlineData("BTN_VIEW", "View")]
    [InlineData("Menu", "Menu")]
    [InlineData("Start", "Menu")]
    [InlineData("Options", "Menu")]
    [InlineData("BTN_MENU", "Menu")]
    [InlineData("Guide", "Guide")]
    [InlineData("Home", "Guide")]
    [InlineData("PS", "Guide")]
    [InlineData("BTN_GUIDE", "Guide")]
    public void ResolveAnchor_CenterControls_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("P1", "P1")]
    [InlineData("Paddle P1", "P1")]
    [InlineData("PaddleP1", "P1")]
    [InlineData("Paddle 1", "P1")]
    [InlineData("BTN_LEFT_UPPER_PADDLE", "P1")]
    [InlineData("P2", "P2")]
    [InlineData("Paddle P2", "P2")]
    [InlineData("PaddleP2", "P2")]
    [InlineData("BTN_RIGHT_UPPER_PADDLE", "P2")]
    [InlineData("P3", "P3")]
    [InlineData("Paddle P3", "P3")]
    [InlineData("PaddleP3", "P3")]
    [InlineData("BTN_LEFT_LOWER_PADDLE", "P3")]
    [InlineData("P4", "P4")]
    [InlineData("Paddle P4", "P4")]
    [InlineData("PaddleP4", "P4")]
    [InlineData("BTN_RIGHT_LOWER_PADDLE", "P4")]
    public void ResolveAnchor_RearPaddles_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("TouchClick", "TouchClick")]
    [InlineData("TouchpadClick", "TouchClick")]
    [InlineData("Touchpad Click", "TouchClick")]
    [InlineData("Touchpad", "TouchClick")]
    [InlineData("BTN_TOUCH_CLICK", "TouchClick")]
    [InlineData("TouchTap", "TouchTap")]
    [InlineData("TouchpadTap", "TouchTap")]
    [InlineData("Touchpad Tap", "TouchTap")]
    [InlineData("SwipeUp", "SwipeUp")]
    [InlineData("Swipe Up", "SwipeUp")]
    [InlineData("TouchpadSwipeUp", "SwipeUp")]
    [InlineData("SwipeDown", "SwipeDown")]
    [InlineData("SwipeLeft", "SwipeLeft")]
    [InlineData("SwipeRight", "SwipeRight")]
    public void ResolveAnchor_TouchpadGestures_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("VGYRO", "VGYRO")]
    [InlineData("Gyro", "VGYRO")]
    [InlineData("GyroAim", "VGYRO")]
    [InlineData("Virtual Gyroscope", "VGYRO")]
    [InlineData("TiltUp", "TiltUp")]
    [InlineData("Tilt Up", "TiltUp")]
    [InlineData("PitchUp", "TiltUp")]
    [InlineData("TiltDown", "TiltDown")]
    [InlineData("TiltLeft", "TiltLeft")]
    [InlineData("LeanLeft", "TiltLeft")]
    [InlineData("TiltRight", "TiltRight")]
    [InlineData("LeanRight", "TiltRight")]
    public void ResolveAnchor_MotionAndGyro_ResolvesCorrectly(string input, string expected)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Key: F13")]
    [InlineData("MouseLeft")]
    [InlineData("Take a screenshot")]
    [InlineData("UnknownButtonXYZ")]
    public void ResolveAnchor_UnknownOrNonCanvas_ReturnsNull(string? input)
    {
        var resolved = CanvasButtonResolver.ResolveAnchor(input);
        Assert.Null(resolved);
    }

    [Fact]
    public void AllAnchors_ContainsAllExpectedKeys()
    {
        var anchors = CanvasButtonResolver.AllAnchors;
        Assert.Contains("A", anchors);
        Assert.Contains("B", anchors);
        Assert.Contains("X", anchors);
        Assert.Contains("Y", anchors);
        Assert.Contains("LS", anchors);
        Assert.Contains("RS", anchors);
        Assert.Contains("DUp", anchors);
        Assert.Contains("DDown", anchors);
        Assert.Contains("DLeft", anchors);
        Assert.Contains("DRight", anchors);
        Assert.Contains("LB", anchors);
        Assert.Contains("RB", anchors);
        Assert.Contains("LT", anchors);
        Assert.Contains("RT", anchors);
        Assert.Contains("View", anchors);
        Assert.Contains("Menu", anchors);
        Assert.Contains("Guide", anchors);
        Assert.Contains("P1", anchors);
        Assert.Contains("P2", anchors);
        Assert.Contains("P3", anchors);
        Assert.Contains("P4", anchors);
        Assert.Contains("TouchClick", anchors);
        Assert.Contains("TouchTap", anchors);
        Assert.Contains("SwipeUp", anchors);
        Assert.Contains("SwipeDown", anchors);
        Assert.Contains("SwipeLeft", anchors);
        Assert.Contains("SwipeRight", anchors);
        Assert.Contains("VGYRO", anchors);
        Assert.Contains("TiltUp", anchors);
        Assert.Contains("TiltDown", anchors);
        Assert.Contains("TiltLeft", anchors);
        Assert.Contains("TiltRight", anchors);
    }
}
