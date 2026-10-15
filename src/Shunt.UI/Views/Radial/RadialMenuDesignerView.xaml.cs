using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shunt.UI.Views.Radial;

public partial class RadialMenuDesignerView : UserControl
{
    private int _sectorCount = 8;
    private int _selectedSector = 0;
    private readonly string[] _sectorNames = ["Medkit", "Shield Battery", "Grenade", "Ultimate", "Syringe", "Shield Cell", "Holster", "Emote"];
    private bool _initialized;

    public RadialMenuDesignerView()
    {
        InitializeComponent();
        _initialized = true;
        Loaded += (_, _) => RedrawWheel();
    }

    private void OnWheelCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized) return;
        RedrawWheel();
    }

    private void OnSectorCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || SectorCountCombo == null) return;
        _sectorCount = SectorCountCombo.SelectedIndex switch
        {
            0 => 4,
            1 => 6,
            _ => 8
        };
        RedrawWheel();
    }

    private void OnSectorNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        if (_selectedSector >= 0 && _selectedSector < _sectorNames.Length && SectorNameBox != null)
        {
            _sectorNames[_selectedSector] = SectorNameBox.Text;
            RedrawWheel();
        }
    }

    private void RedrawWheel()
    {
        if (!_initialized || WheelCanvas == null) return;
        WheelCanvas.Children.Clear();
        double w = WheelCanvas.ActualWidth;
        double h = WheelCanvas.ActualHeight;
        if (w < 20 || h < 20) return;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double outerR = Math.Min(cx, cy) * 0.85;
        double innerR = outerR * 0.32;

        double angleStep = 360.0 / _sectorCount;

        for (int i = 0; i < _sectorCount; i++)
        {
            int sectorIdx = i;
            double startAngle = -90.0 + i * angleStep - (angleStep / 2.0);
            double endAngle = startAngle + angleStep;

            var path = CreatePieSlice(cx, cy, innerR, outerR, startAngle, endAngle);
            bool isSelected = (i == _selectedSector);

            path.Fill = isSelected
                ? new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xE7, 0x95))
                : new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x3A, 0x3A));
            path.Stroke = isSelected
                ? (Brush)FindResource("AccentCreamBrush")
                : (Brush)FindResource("BorderSubtleBrush");
            path.StrokeThickness = isSelected ? 2 : 1;
            path.Cursor = Cursors.Hand;

            path.MouseEnter += (_, _) =>
            {
                if (sectorIdx != _selectedSector)
                    path.Fill = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x55, 0x55));
            };
            path.MouseLeave += (_, _) =>
            {
                if (sectorIdx != _selectedSector)
                    path.Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x3A, 0x3A));
            };
            path.MouseDown += (_, _) =>
            {
                _selectedSector = sectorIdx;
                SelectedSectorLabel.Text = $"Sector {sectorIdx + 1}";
                SectorNameBox.Text = sectorIdx < _sectorNames.Length ? _sectorNames[sectorIdx] : $"Action {sectorIdx + 1}";
                RedrawWheel();
            };

            WheelCanvas.Children.Add(path);

            // Sector Label text
            double midAngle = (startAngle + endAngle) / 2.0;
            double labelR = (innerR + outerR) / 2.0;
            double rad = midAngle * Math.PI / 180.0;
            double lx = cx + labelR * Math.Cos(rad);
            double ly = cy + labelR * Math.Sin(rad);

            var label = new TextBlock
            {
                Text = sectorIdx < _sectorNames.Length ? _sectorNames[sectorIdx] : $"{sectorIdx + 1}",
                Foreground = isSelected ? (Brush)FindResource("AccentCreamBrush") : (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, lx - label.DesiredSize.Width / 2.0);
            Canvas.SetTop(label, ly - label.DesiredSize.Height / 2.0);
            WheelCanvas.Children.Add(label);
        }

        // Center Deadzone Circle
        var centerCircle = new Ellipse
        {
            Width = innerR * 2,
            Height = innerR * 2,
            Fill = (Brush)FindResource("SurfaceLevel1Brush"),
            Stroke = (Brush)FindResource("BorderSubtleBrush"),
            StrokeThickness = 2
        };
        Canvas.SetLeft(centerCircle, cx - innerR);
        Canvas.SetTop(centerCircle, cy - innerR);
        WheelCanvas.Children.Add(centerCircle);

        var centerText = new TextBlock
        {
            Text = "Deadzone",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        centerText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(centerText, cx - centerText.DesiredSize.Width / 2.0);
        Canvas.SetTop(centerText, cy - centerText.DesiredSize.Height / 2.0);
        WheelCanvas.Children.Add(centerText);
    }

    private static Path CreatePieSlice(double cx, double cy, double innerR, double outerR, double startAngleDeg, double endAngleDeg)
    {
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = endAngleDeg * Math.PI / 180.0;

        Point p1 = new(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));
        Point p2 = new(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
        Point p3 = new(cx + outerR * Math.Cos(endRad), cy + outerR * Math.Sin(endRad));
        Point p4 = new(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));

        bool isLargeArc = (endAngleDeg - startAngleDeg) > 180.0;

        var geom = new PathGeometry();
        var figure = new PathFigure { StartPoint = p1, IsClosed = true };
        figure.Segments.Add(new LineSegment(p2, true));
        figure.Segments.Add(new ArcSegment(p3, new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(p4, true));
        figure.Segments.Add(new ArcSegment(p1, new Size(innerR, innerR), 0, isLargeArc, SweepDirection.Counterclockwise, true));
        geom.Figures.Add(figure);

        return new Path { Data = geom };
    }

    private void OnShowOverlayPreview(object sender, RoutedEventArgs e)
    {
        var overlay = new RadialMenuOverlay();
        overlay.Show();
    }

    private void OnSaveSector(object sender, RoutedEventArgs e)
    {
        MessageBox.Show($"Saved binding for Sector {_selectedSector + 1}: {SectorNameBox.Text}", "Radial Menu", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
