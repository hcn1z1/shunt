using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Shunt.Core.Scancodes;
using Shunt.Engine;

namespace Shunt.UI.Views.Workspace;

/// <summary>
/// Interactive gaming mouse canvas (plan 11 §5): clickable buttons, wheel actions and
/// optical sensor deflection zones, with mapped-target badges and live glow.
/// </summary>
public partial class MouseCanvasView : UserControl
{
    private static readonly string[] ZoneAnchors =
    [
        "LMB", "RMB", "MMB", "Mouse4", "Mouse5",
        "WheelUp", "WheelDown", "MWheelLeft", "MWheelRight",
        "MDeflectUp", "MDeflectDown", "MDeflectLeft", "MDeflectRight"
    ];

    private static readonly (string Anchor, string Label, double RailY)[] BadgeRails =
    [
        ("LMB", "Left Click", 170),
        ("MMB", "Middle Click", 220),
        ("WheelUp", "Wheel Up", 130),
        ("WheelDown", "Wheel Down", 340),
        ("Mouse4", "Mouse 4 / Back", 400),
        ("Mouse5", "Mouse 5 / Forward", 450),
        ("RMB", "Right Click", 170),
        ("MWheelLeft", "Wheel Tilt Left", 260),
        ("MWheelRight", "Wheel Tilt Right", 260),
        ("MDeflectLeft", "Deflect Left", 560),
        ("MDeflectRight", "Deflect Right", 560),
        ("MDeflectUp", "Deflect Up", 620),
        ("MDeflectDown", "Deflect Down", 620)
    ];

    private readonly Dictionary<string, Border> _zones = [];
    private readonly Dictionary<string, (Brush Fill, Brush Stroke, System.Windows.Media.Effects.Effect? Effect)> _defaults = [];
    private readonly Dictionary<string, string> _bindings = [];
    private readonly Dictionary<MouseZone, Ellipse> _zoneRings = [];
    private readonly Dictionary<MouseZone, Ellipse> _zoneHandles = [];
    private readonly HashSet<string> _glowing = [];
    private string? _selectedAnchor;
    private double _sensitivity = 1.0;
    private MouseZoneConfig _zoneConfig = new();
    private MouseZone? _draggingZone;
    private Ellipse? _reticle;
    private ComboBox? _edgeActionPicker;

    private static readonly Brush GlowStroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#81CF66"));
    private static readonly Brush GlowFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334DC81F"));
    private static readonly System.Windows.Media.Effects.DropShadowEffect GlowEffect = new()
    {
        Color = (Color)ColorConverter.ConvertFromString("#81CF66"),
        BlurRadius = 14,
        ShadowDepth = 0,
        Opacity = 1.0
    };

    public event EventHandler<string>? BindingRequested;
    public event Action<MouseZoneConfig>? ZoneConfigurationChanged;

    public MouseCanvasView()
    {
        InitializeComponent();
        AttachZones();
        RebuildBadges();
        BuildZoneRadar();
    }

    private void AttachZones()
    {
        foreach (var anchor in ZoneAnchors)
        {
            if (FindName("Btn_" + anchor) is not Border zone) continue;
            zone.Cursor = Cursors.Hand;
            zone.Tag = anchor;
            zone.MouseDown += Zone_MouseDown;
            _zones[anchor] = zone;
            _defaults[anchor] = (zone.Background, zone.BorderBrush, zone.Effect);
        }
    }

    private void Zone_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string anchor })
        {
            SelectButton(anchor);
            BindingRequested?.Invoke(this, anchor);
        }
    }

    // ------------------------------ Public API ------------------------------

    /// <summary>Feeds profile mappings in; sources matching mouse zones display their bound target.</summary>
    public void SetMappings(IEnumerable<(string Source, string Target)> mappings)
    {
        _bindings.Clear();
        foreach (var (source, target) in mappings)
        {
            foreach (var anchor in ZoneAnchors)
            {
                var aliases = CanvasButtonResolver.GetAliases(anchor);
                if (aliases.Any(alias => source.Contains(alias, StringComparison.OrdinalIgnoreCase)))
                {
                    _bindings[anchor] = target;
                    break;
                }
            }
        }
        RebuildBadges();
    }

    public void SelectButton(string anchor)
    {
        foreach (var (name, zone) in _zones)
        {
            var def = _defaults[name];
            zone.Background = def.Fill;
            zone.BorderBrush = def.Stroke;
            zone.Effect = def.Effect;
        }

        _selectedAnchor = anchor;
        if (_zones.TryGetValue(anchor, out var selected))
        {
            selected.BorderBrush = (Brush)Application.Current.FindResource("StatusSuccessBrush");
            selected.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#4DC81F"),
                BlurRadius = 12,
                ShadowDepth = 0
            };
        }
    }

    public void ApplyButtonGlow(string anchor)
    {
        if (!_glowing.Add(anchor)) return;
        if (_zones.TryGetValue(anchor, out var zone))
        {
            zone.BorderBrush = GlowStroke;
            zone.Background = GlowFill;
            zone.Effect = GlowEffect;
        }
    }

    public void RemoveButtonGlow(string anchor)
    {
        _glowing.Remove(anchor);
        if (!_zones.TryGetValue(anchor, out var zone)) return;
        if (anchor == _selectedAnchor)
        {
            zone.BorderBrush = (Brush)Application.Current.FindResource("StatusSuccessBrush");
            zone.Background = (Brush)Application.Current.FindResource("SurfaceLevel2Brush");
            return;
        }
        var def = _defaults[anchor];
        zone.Background = def.Fill;
        zone.BorderBrush = def.Stroke;
        zone.Effect = def.Effect;
    }

    public void PulseGlow(string anchor, int durationMs = 450)
    {
        ApplyButtonGlow(anchor);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            RemoveButtonGlow(anchor);
        };
        timer.Start();
    }

    public void ClearAllGlows()
    {
        foreach (var anchor in _glowing.ToList())
            RemoveButtonGlow(anchor);
        _glowing.Clear();
    }

    /// <summary>Ballistic sensitivity multiplier shown on the sensor pad (plan 11 §5).</summary>
    public void SetSensitivity(double multiplier)
    {
        _sensitivity = multiplier;
        if (SensorSensitivityText != null)
            SensorSensitivityText.Text = $"Ballistic Sensitivity: {multiplier:0.0}x";
    }

    public void SetZoneConfiguration(MouseZoneConfig config)
    {
        _zoneConfig = config;
        UpdateZoneRadar();
        if (_edgeActionPicker is not null)
        {
            var label = config.EdgeAction?.Replace('_', ' ').ToLowerInvariant();
            _edgeActionPicker.SelectedItem = label switch
            {
                "sprint" => "Sprint",
                "quick turn" => "Quick turn",
                "layer shift" => "Layer shift",
                _ => "None"
            };
        }
    }

    public void SetLiveDeflection(float x, float y)
    {
        if (_reticle is null) return;
        const double radius = 78;
        Canvas.SetLeft(_reticle, 130 + Math.Clamp(x, -1f, 1f) * radius - 6);
        Canvas.SetTop(_reticle, 130 + Math.Clamp(y, -1f, 1f) * radius - 6);
    }

    private void BuildZoneRadar()
    {
        var radar = new Canvas { Width = 260, Height = 260, Background = Brushes.Transparent };
        Canvas.SetLeft(radar, 830);
        Canvas.SetTop(radar, 220);
        StageCanvas.Children.Add(radar);

        var face = new Ellipse
        {
            Width = 180, Height = 180, Fill = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(face, 40); Canvas.SetTop(face, 40); radar.Children.Add(face);
        foreach (var zone in new[] { MouseZone.Deadzone, MouseZone.Low, MouseZone.Medium, MouseZone.High })
        {
            var ring = new Ellipse
            {
                Fill = new SolidColorBrush(zone switch
                {
                    MouseZone.Deadzone => Color.FromArgb(38, 77, 200, 31),
                    MouseZone.Low => Color.FromArgb(20, 77, 200, 31),
                    MouseZone.Medium => Color.FromArgb(18, 70, 150, 220),
                    _ => Color.FromArgb(15, 240, 170, 60)
                }),
                Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            _zoneRings[zone] = ring;
            radar.Children.Add(ring);

            if (zone == MouseZone.Deadzone) continue;
            var handle = new Ellipse
            {
                Width = 12, Height = 12, Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromArgb(220, 77, 200, 31)), StrokeThickness = 2,
                Cursor = Cursors.SizeWE, ToolTip = $"Drag to adjust {zone} zone boundary"
            };
            handle.MouseLeftButtonDown += (_, e) => { _draggingZone = zone; handle.CaptureMouse(); e.Handled = true; };
            handle.MouseMove += (_, e) =>
            {
                if (_draggingZone != zone || e.LeftButton != MouseButtonState.Pressed) return;
                var point = e.GetPosition(radar);
                UpdateZoneBoundary(zone, (float)Math.Clamp((point.X - 130) / 78, 0, 1));
            };
            handle.MouseLeftButtonUp += (_, e) => { _draggingZone = null; handle.ReleaseMouseCapture(); e.Handled = true; };
            _zoneHandles[zone] = handle;
            radar.Children.Add(handle);
        }

        _reticle = new Ellipse
        {
            Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(129, 207, 102)),
            Stroke = Brushes.White, StrokeThickness = 1,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(129, 207, 102), BlurRadius = 12, ShadowDepth = 0 },
            IsHitTestVisible = false
        };
        radar.Children.Add(_reticle);
        radar.MouseMove += (_, e) =>
        {
            var point = e.GetPosition(radar);
            if (point.Y < 208)
                SetLiveDeflection((float)((point.X - 130) / 78), (float)((point.Y - 130) / 78));
        };

        var title = new TextBlock { Text = "MOUSE ZONE RADAR", FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, IsHitTestVisible = false };
        Canvas.SetLeft(title, 40); Canvas.SetTop(title, 8); radar.Children.Add(title);
        var actionLabel = new TextBlock { Text = "EDGE ACTION", FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        Canvas.SetLeft(actionLabel, 34); Canvas.SetTop(actionLabel, 224); radar.Children.Add(actionLabel);
        var action = new ComboBox { Width = 126, Height = 24, FontSize = 10, ItemsSource = new[] { "None", "Sprint", "Quick turn", "Layer shift" },
            SelectedIndex = 0, Style = (Style)Application.Current.FindResource("DarkComboBox"),
            ItemContainerStyle = (Style)Application.Current.FindResource("DarkComboBoxItem") };
        _edgeActionPicker = action;
        action.SelectionChanged += (_, _) =>
        {
            var value = action.SelectedItem?.ToString();
            _zoneConfig = _zoneConfig with { EdgeAction = value == "None" ? null : value?.ToLowerInvariant().Replace(' ', '_') };
            ZoneConfigurationChanged?.Invoke(_zoneConfig);
        };
        Canvas.SetLeft(action, 118); Canvas.SetTop(action, 218); radar.Children.Add(action);
        UpdateZoneRadar();
    }

    private void UpdateZoneBoundary(MouseZone zone, float value)
    {
        const float gap = 0.04f;
        _zoneConfig = zone switch
        {
            MouseZone.Low => _zoneConfig with { LowRadius = Math.Clamp(value, _zoneConfig.DeadzoneRadius + gap, _zoneConfig.MediumRadius - gap) },
            MouseZone.Medium => _zoneConfig with { MediumRadius = Math.Clamp(value, _zoneConfig.LowRadius + gap, _zoneConfig.HighRadius - gap) },
            MouseZone.High => _zoneConfig with { HighRadius = Math.Clamp(value, _zoneConfig.MediumRadius + gap, 1f) },
            _ => _zoneConfig
        };
        UpdateZoneRadar();
        ZoneConfigurationChanged?.Invoke(_zoneConfig);
    }

    private void UpdateZoneRadar()
    {
        const double maxRadius = 78;
        var radii = new Dictionary<MouseZone, float>
        {
            [MouseZone.Deadzone] = _zoneConfig.DeadzoneRadius,
            [MouseZone.Low] = _zoneConfig.LowRadius,
            [MouseZone.Medium] = _zoneConfig.MediumRadius,
            [MouseZone.High] = _zoneConfig.HighRadius
        };
        foreach (var (zone, ring) in _zoneRings)
        {
            var diameter = radii[zone] * maxRadius * 2;
            ring.Width = diameter; ring.Height = diameter;
            Canvas.SetLeft(ring, 130 - diameter / 2); Canvas.SetTop(ring, 130 - diameter / 2);
            if (_zoneHandles.TryGetValue(zone, out var handle))
            {
                Canvas.SetLeft(handle, 130 + radii[zone] * maxRadius - handle.Width / 2);
                Canvas.SetTop(handle, 130 - handle.Height / 2);
            }
        }
    }

    // ------------------------------ Badges ------------------------------

    private void RebuildBadges()
    {
        if (BadgeLayer == null) return;
        BadgeLayer.Children.Clear();
        LeaderLineLayer.Children.Clear();

        foreach (var (anchor, label, railY) in BadgeRails)
        {
            if (!_zones.TryGetValue(anchor, out var zone)) continue;
            var hasBinding = _bindings.TryGetValue(anchor, out var target);
            var isHighlighted = anchor == _selectedAnchor;
            var leftSide = !zone.Equals(_zones.GetValueOrDefault("RMB")) &&
                           zone.GetValue(Canvas.LeftProperty) is double lx && lx < 600;

            double badgeLeft = leftSide ? 40 : 1090;
            var badge = new Border
            {
                Width = 130,
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                Background = (Brush)Application.Current.FindResource("SurfaceLevel2Brush"),
                BorderBrush = isHighlighted
                    ? (Brush)Application.Current.FindResource("StatusSuccessBrush")
                    : (Brush)Application.Current.FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"{label}: {target ?? "not mapped"}"
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("TextMutedBrush")
            });
            stack.Children.Add(new TextBlock
            {
                Text = target ?? "unmapped",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.FindResource(
                    hasBinding ? "TextPrimaryBrush" : "TextDisabledBrush")
            });
            badge.Child = stack;

            var badgeAnchorPoint = new Point(leftSide ? badgeLeft + 130 : badgeLeft, railY + 18);
            badge.MouseDown += (_, _) =>
            {
                SelectButton(anchor);
                BindingRequested?.Invoke(this, anchor);
            };
            Canvas.SetLeft(badge, badgeLeft);
            Canvas.SetTop(badge, railY);
            BadgeLayer.Children.Add(badge);

            // Leader line from zone edge to badge
            var zoneLeft = (double)zone.GetValue(Canvas.LeftProperty);
            var zoneTop = (double)zone.GetValue(Canvas.TopProperty);
            var zoneWidth = zone.ActualWidth > 0 ? zone.ActualWidth : zone.Width;
            var zoneHeight = zone.ActualHeight > 0 ? zone.ActualHeight : zone.Height;
            var start = new Point(
                leftSide ? zoneLeft : zoneLeft + zoneWidth,
                zoneTop + zoneHeight / 2);

            var figure = new PathFigure { StartPoint = start, IsFilled = false };
            var mid = new Point((start.X + badgeAnchorPoint.X) / 2, badgeAnchorPoint.Y);
            figure.Segments.Add(new BezierSegment(
                new Point(start.X + (leftSide ? -40 : 40), start.Y),
                new Point(mid.X, badgeAnchorPoint.Y),
                badgeAnchorPoint, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            LeaderLineLayer.Children.Add(new Path
            {
                Data = geometry,
                Stroke = isHighlighted
                    ? (Brush)Application.Current.FindResource("StatusSuccessBrush")
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66FFFFFF")),
                StrokeThickness = isHighlighted ? 2 : 1.2,
                StrokeDashArray = hasBinding ? null : new DoubleCollection { 3, 3 }
            });
        }

        SetSensitivity(_sensitivity);
    }
}
