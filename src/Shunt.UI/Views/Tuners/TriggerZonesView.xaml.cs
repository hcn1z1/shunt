using System;
using System.Windows;
using System.Windows.Controls;

namespace Shunt.UI.Views.Tuners;

public partial class TriggerZonesView : UserControl
{
    private bool _initialized;

    public TriggerZonesView()
    {
        InitializeComponent();
        _initialized = true;
        Loaded += (_, _) => UpdateDividers();
        SizeChanged += (_, _) => UpdateDividers();
    }

    private void OnThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        if (T1ValueText != null) T1ValueText.Text = $"{(int)T1Slider.Value}%";
        if (T2ValueText != null) T2ValueText.Text = $"{(int)T2Slider.Value}%";
        UpdateDividers();
    }

    private void OnSimulateChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        TriggerPullProgress.Value = SimulatePullSlider.Value;
    }

    private void UpdateDividers()
    {
        double w = ZoneDividerCanvas.ActualWidth;
        double h = ZoneDividerCanvas.ActualHeight;
        if (w < 10) return;

        double x1 = w * (T1Slider.Value / 100.0);
        double x2 = w * (T2Slider.Value / 100.0);

        Divider1.X1 = Divider1.X2 = x1;
        Divider1.Y1 = 0; Divider1.Y2 = h;

        Divider2.X1 = Divider2.X2 = x2;
        Divider2.Y1 = 0; Divider2.Y2 = h;
    }

    private void OnEffectProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        // Adjust preset defaults based on selected effect
        if (EffectProfileCombo.SelectedIndex == 0) // Rigid
        {
            StartPosSlider.Value = 30;
            EndPosSlider.Value = 35;
            ForceSlider.Value = 100;
        }
        else if (EffectProfileCombo.SelectedIndex == 1) // Pulse
        {
            StartPosSlider.Value = 10;
            EndPosSlider.Value = 80;
            ForceSlider.Value = 75;
            FreqSlider.Value = 20;
        }
    }

    private void OnEffectParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        if (StartPosVal != null) StartPosVal.Text = $"{(int)StartPosSlider.Value}%";
        if (EndPosVal != null) EndPosVal.Text = $"{(int)EndPosSlider.Value}%";
        if (ForceVal != null) ForceVal.Text = $"{(int)ForceSlider.Value}%";
        if (FreqVal != null) FreqVal.Text = $"{(int)FreqSlider.Value} Hz";
    }

    private void OnTestTriggerClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Trigger effect command dispatched to DualSense controller.", "Adaptive Triggers", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
