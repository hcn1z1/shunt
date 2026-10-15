using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shunt.Core.Models;
using Shunt.Emulation;
using Shunt.Engine;

namespace Shunt.UI.Views.Macro;

public partial class MacroTimelineView : UserControl
{
    private readonly ObservableCollection<MacroStepInstruction> _steps = [];

    public MacroTimelineView()
    {
        InitializeComponent();
        Loaded += (_, _) => SeedDefaultSteps();
    }

    private void SeedDefaultSteps()
    {
        if (_steps.Count > 0) return;
        _steps.Add(new MacroStepInstruction(MacroOpCode.KeyDown, GamepadButtons.A));
        _steps.Add(new MacroStepInstruction(MacroOpCode.Delay, DelayMs: 50));
        _steps.Add(new MacroStepInstruction(MacroOpCode.KeyUp, GamepadButtons.A));
        _steps.Add(new MacroStepInstruction(MacroOpCode.Delay, DelayMs: 100));
        _steps.Add(new MacroStepInstruction(MacroOpCode.KeyDown, GamepadButtons.RightShoulder));
        _steps.Add(new MacroStepInstruction(MacroOpCode.Delay, DelayMs: 50));
        _steps.Add(new MacroStepInstruction(MacroOpCode.KeyUp, GamepadButtons.RightShoulder));
        _steps.Add(new MacroStepInstruction(MacroOpCode.RumblePulse, DelayMs: 80));
        RefreshTracks();
    }

    private void RefreshTracks()
    {
        ButtonTrackItems.Items.Clear();
        DelayTrackItems.Items.Clear();
        RumbleTrackItems.Items.Clear();

        int totalMs = 0;
        foreach (var step in _steps)
        {
            switch (step.Op)
            {
                case MacroOpCode.KeyDown:
                case MacroOpCode.KeyUp:
                    var btnBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
                        BorderBrush = (Brush)FindResource("AccentCreamBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    btnBorder.Child = new TextBlock
                    {
                        Text = $"{step.Button} {(step.Op == MacroOpCode.KeyDown ? "↓" : "↑")}",
                        Foreground = (Brush)FindResource("AccentCreamBrush"),
                        FontWeight = FontWeights.SemiBold
                    };
                    ButtonTrackItems.Items.Add(btnBorder);
                    break;

                case MacroOpCode.Delay:
                    totalMs += step.DelayMs;
                    var delayBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
                        BorderBrush = (Brush)FindResource("Shift3Brush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12, 6, 12, 6),
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    delayBorder.Child = new TextBlock
                    {
                        Text = $"Wait {step.DelayMs}ms",
                        Foreground = (Brush)FindResource("Shift3Brush")
                    };
                    DelayTrackItems.Items.Add(delayBorder);
                    break;

                case MacroOpCode.RumblePulse:
                    totalMs += step.DelayMs > 0 ? step.DelayMs : 100;
                    var rumbleBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x35, 0x20, 0x20)),
                        BorderBrush = (Brush)FindResource("StatusErrorBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    rumbleBorder.Child = new TextBlock
                    {
                        Text = "Vibrate Pulse",
                        Foreground = (Brush)FindResource("StatusErrorBrush")
                    };
                    RumbleTrackItems.Items.Add(rumbleBorder);
                    break;
            }
        }

        TimelineStatusText.Text = $"Timeline: {_steps.Count} steps (Total duration: ~{totalMs}ms)";
    }

    private void OnAddButtonStep(object sender, RoutedEventArgs e)
    {
        _steps.Add(new MacroStepInstruction(MacroOpCode.KeyDown, GamepadButtons.X));
        _steps.Add(new MacroStepInstruction(MacroOpCode.Delay, DelayMs: 50));
        _steps.Add(new MacroStepInstruction(MacroOpCode.KeyUp, GamepadButtons.X));
        RefreshTracks();
    }

    private void OnAddDelayStep(object sender, RoutedEventArgs e)
    {
        _steps.Add(new MacroStepInstruction(MacroOpCode.Delay, DelayMs: 100));
        RefreshTracks();
    }

    private void OnAddRumbleStep(object sender, RoutedEventArgs e)
    {
        _steps.Add(new MacroStepInstruction(MacroOpCode.RumblePulse, DelayMs: 100));
        RefreshTracks();
    }

    private void OnClearSteps(object sender, RoutedEventArgs e)
    {
        _steps.Clear();
        RefreshTracks();
    }

    private async void OnTestRun(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) return;
        var stub = new StubVirtualGamepad(VirtualGamepadType.Xbox360);
        stub.Connect();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            TimelineStatusText.Text = "Executing macro in test VM...";
            await MacroEngine.ExecuteSequenceAsync(_steps, stub, cts.Token);
            TimelineStatusText.Text = $"Test run completed successfully! (Steps: {_steps.Count})";
        }
        catch (Exception ex)
        {
            TimelineStatusText.Text = $"Execution error: {ex.Message}";
        }
        finally
        {
            stub.Disconnect();
        }
    }
}
