using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Shunt.Core.MathTransform;

namespace Shunt.UI.Views.Tuners;

public partial class StickCurvesVisualizer : UserControl
{
    private CubicBezierCurve _curve = CubicBezierCurve.Linear;
    private bool _initialized;

    public StickCurvesVisualizer()
    {
        InitializeComponent();
        _initialized = true;
        Loaded += (_, _) => RedrawCurve();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawCurve();
    }

    private void OnParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _curve = new CubicBezierCurve(
            (float)P1XSlider.Value,
            (float)P1YSlider.Value,
            (float)P2XSlider.Value,
            (float)P2YSlider.Value);
        RedrawCurve();
    }

    private void OnDeadzoneChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        if (InnerDeadzoneVal != null)
            InnerDeadzoneVal.Text = $"{(int)(InnerDeadzoneSlider.Value * 100)}%";
        if (OuterDeadzoneVal != null)
            OuterDeadzoneVal.Text = $"{(int)(OuterDeadzoneSlider.Value * 100)}%";
        RedrawCurve();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        switch (btn.Name)
        {
            case "LinearPresetBtn":
                SetCurve(CubicBezierCurve.Linear);
                break;
            case "InstantPresetBtn":
                SetCurve(CubicBezierCurve.Instant);
                break;
            case "SmoothPresetBtn":
                SetCurve(CubicBezierCurve.Smooth);
                break;
            case "AggressivePresetBtn":
                SetCurve(CubicBezierCurve.Aggressive);
                break;
        }
    }

    private void SetCurve(CubicBezierCurve preset)
    {
        _curve = preset;
        P1XSlider.Value = preset.X1;
        P1YSlider.Value = preset.Y1;
        P2XSlider.Value = preset.X2;
        P2YSlider.Value = preset.Y2;
        RedrawCurve();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        SetCurve(CubicBezierCurve.Linear);
        InnerDeadzoneSlider.Value = 0.08;
        OuterDeadzoneSlider.Value = 0.02;
    }

    public void RedrawCurve()
    {
        double w = PlotCanvas.ActualWidth;
        double h = PlotCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        // Grid lines
        GridLineX25.X1 = GridLineX25.X2 = w * 0.25; GridLineX25.Y1 = 0; GridLineX25.Y2 = h;
        GridLineX50.X1 = GridLineX50.X2 = w * 0.50; GridLineX50.Y1 = 0; GridLineX50.Y2 = h;
        GridLineX75.X1 = GridLineX75.X2 = w * 0.75; GridLineX75.Y1 = 0; GridLineX75.Y2 = h;

        GridLineY25.Y1 = GridLineY25.Y2 = h * 0.75; GridLineY25.X1 = 0; GridLineY25.X2 = w;
        GridLineY50.Y1 = GridLineY50.Y2 = h * 0.50; GridLineY50.X1 = 0; GridLineY50.X2 = w;
        GridLineY75.Y1 = GridLineY75.Y2 = h * 0.25; GridLineY75.X1 = 0; GridLineY75.X2 = w;

        // Deadzone overlays
        double innerW = w * InnerDeadzoneSlider.Value;
        DeadzoneRect.Width = innerW;
        DeadzoneRect.Height = h;
        Canvas.SetLeft(DeadzoneRect, 0);
        Canvas.SetTop(DeadzoneRect, 0);

        double outerW = w * OuterDeadzoneSlider.Value;
        MaxThresholdRect.Width = outerW;
        MaxThresholdRect.Height = h;
        Canvas.SetLeft(MaxThresholdRect, w - outerW);
        Canvas.SetTop(MaxThresholdRect, 0);

        // Linear reference line
        RefLine.X1 = 0; RefLine.Y1 = h;
        RefLine.X2 = w; RefLine.Y2 = 0;

        // Curve geometry
        var p0 = new Point(0, h);
        var p1 = new Point(_curve.X1 * w, h - _curve.Y1 * h);
        var p2 = new Point(_curve.X2 * w, h - _curve.Y2 * h);
        var p3 = new Point(w, 0);

        var geom = new PathGeometry();
        var figure = new PathFigure { StartPoint = p0, IsClosed = false };
        figure.Segments.Add(new BezierSegment(p1, p2, p3, isStroked: true));
        geom.Figures.Add(figure);
        CurvePath.Data = geom;

        // Handle lines
        Handle1Line.X1 = p0.X; Handle1Line.Y1 = p0.Y;
        Handle1Line.X2 = p1.X; Handle1Line.Y2 = p1.Y;

        Handle2Line.X1 = p3.X; Handle2Line.Y1 = p3.Y;
        Handle2Line.X2 = p2.X; Handle2Line.Y2 = p2.Y;

        // Update live GraphDot at 65% sample
        float samplePhys = 0.65f;
        float sampleVirt = _curve.Evaluate(samplePhys);
        double dotX = samplePhys * w;
        double dotY = h - sampleVirt * h;
        Canvas.SetLeft(GraphDot, dotX - 7);
        Canvas.SetTop(GraphDot, dotY - 7);

        TelemetryText.Text = $"Hardware Deflection: {samplePhys:P0} -> Output: {sampleVirt:P0}";
    }
}
