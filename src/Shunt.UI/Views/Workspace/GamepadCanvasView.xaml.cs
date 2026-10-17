using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shunt.Core.Scancodes;
using Shunt.Engine.Hardware;
using Shunt.UI.Views.Dialogs;

namespace Shunt.UI.Views.Workspace;

public partial class GamepadCanvasView : UserControl
{
    // Note: Shunt mode stage has moved to the main navigation menu on the left rail.

    private enum PinSide { Left, Right, Top, Bottom }

    private sealed record ButtonAnchor(
        string Name, string Label, Point Center, PinSide Side, bool RearOnly = false, string[]? Aliases = null)
    {
        public string[] Aliases { get; init; } = Aliases ?? CanvasButtonResolver.GetAliases(Name).ToArray();
    }

    private sealed record BindingDisplay(string Source, string Target, string Activator);

    // High-precision anchor coordinates matching the authentic Xbox One controller figure
    // on the 1200x750 virtual stage.
    private static readonly ButtonAnchor[] Anchors =
    [
        // Gamepad Face & Action Buttons
        new("A", "A", new Point(783, 302), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("A").ToArray()),
        new("B", "B", new Point(831, 256), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("B").ToArray()),
        new("X", "X", new Point(735, 256), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("X").ToArray()),
        new("Y", "Y", new Point(783, 208), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("Y").ToArray()),
        new("LS", "LS", new Point(418, 251), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("LS").ToArray()),
        new("RS", "RS", new Point(695, 367), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("RS").ToArray()),
        new("DUp", "D-Up", new Point(504, 335), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("DUp").ToArray()),
        new("DDown", "D-Down", new Point(504, 402), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("DDown").ToArray()),
        new("DLeft", "D-Left", new Point(471, 368), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("DLeft").ToArray()),
        new("DRight", "D-Right", new Point(538, 368), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("DRight").ToArray()),
        new("LB", "LB", new Point(442, 141), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("LB").ToArray()),
        new("RB", "RB", new Point(759, 141), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("RB").ToArray()),
        new("LT", "LT", new Point(389, 120), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("LT").ToArray()),
        new("RT", "RT", new Point(812, 120), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("RT").ToArray()),
        new("View", "View", new Point(549, 254), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("View").ToArray()),
        new("Menu", "Menu", new Point(653, 254), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("Menu").ToArray()),
        new("Guide", "Guide", new Point(600, 179), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("Guide").ToArray()),
        new("P1", "Paddle P1", new Point(474, 444), PinSide.Bottom, RearOnly: true, Aliases: CanvasButtonResolver.GetAliases("P1").ToArray()),
        new("P2", "Paddle P2", new Point(726, 444), PinSide.Bottom, RearOnly: true, Aliases: CanvasButtonResolver.GetAliases("P2").ToArray()),
        new("P3", "Paddle P3", new Point(527, 458), PinSide.Bottom, RearOnly: true, Aliases: CanvasButtonResolver.GetAliases("P3").ToArray()),
        new("P4", "Paddle P4", new Point(673, 458), PinSide.Bottom, RearOnly: true, Aliases: CanvasButtonResolver.GetAliases("P4").ToArray()),

        // DualShock 4 Touchpad Gestures
        new("TouchClick", "Touch Click", new Point(510, 205), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("TouchClick").ToArray()),
        new("TouchTap", "Touch Tap", new Point(690, 205), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("TouchTap").ToArray()),
        new("SwipeUp", "Swipe Up", new Point(600, 235), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("SwipeUp").ToArray()),
        new("SwipeDown", "Swipe Down", new Point(600, 310), PinSide.Bottom, Aliases: CanvasButtonResolver.GetAliases("SwipeDown").ToArray()),
        new("SwipeLeft", "Swipe Left", new Point(480, 270), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("SwipeLeft").ToArray()),
        new("SwipeRight", "Swipe Right", new Point(720, 270), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("SwipeRight").ToArray()),

        // Motion & Gyroscope
        new("VGYRO", "VGYRO", new Point(600, 205), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("VGYRO").ToArray()),
        new("TiltUp", "Tilt Up", new Point(600, 245), PinSide.Top, Aliases: CanvasButtonResolver.GetAliases("TiltUp").ToArray()),
        new("TiltDown", "Tilt Down", new Point(600, 310), PinSide.Bottom, Aliases: CanvasButtonResolver.GetAliases("TiltDown").ToArray()),
        new("TiltLeft", "Tilt Left", new Point(480, 270), PinSide.Left, Aliases: CanvasButtonResolver.GetAliases("TiltLeft").ToArray()),
        new("TiltRight", "Tilt Right", new Point(720, 270), PinSide.Right, Aliases: CanvasButtonResolver.GetAliases("TiltRight").ToArray()),
    ];

    // DualShock 4 anchor registry on the 1200x750 stage (plan 11 §3.2). Canonical anchor
    // names are reused so bindings/resolution stay shared; labels use PlayStation nomenclature.
    private static readonly ButtonAnchor[] Ds4Anchors =
    [
        // Face Buttons (geometrical PlayStation cluster)
        new("A", "Cross", new Point(805, 401), PinSide.Right),
        new("B", "Circle", new Point(855, 351), PinSide.Right),
        new("X", "Square", new Point(756, 351), PinSide.Right),
        new("Y", "Triangle", new Point(805, 302), PinSide.Right),

        // Classic PlayStation D-Pad
        new("DUp", "D-Up", new Point(396, 319), PinSide.Left),
        new("DDown", "D-Down", new Point(396, 386), PinSide.Left),
        new("DLeft", "D-Left", new Point(362, 352), PinSide.Left),
        new("DRight", "D-Right", new Point(429, 352), PinSide.Left),

        // Symmetrical thumbsticks
        new("LS", "L3", new Point(496, 441), PinSide.Bottom),
        new("RS", "R3", new Point(705, 441), PinSide.Bottom),

        // Shoulders & Triggers
        new("LB", "L1", new Point(399, 239), PinSide.Left),
        new("RB", "R1", new Point(802, 239), PinSide.Right),
        new("LT", "L2", new Point(394, 210), PinSide.Left),
        new("RT", "R2", new Point(807, 210), PinSide.Right),

        // Touchpad & system
        new("TouchClick", "Touchpad Click", new Point(600, 320), PinSide.Top),
        new("TouchTap", "Touchpad Tap", new Point(690, 205), PinSide.Top),
        new("SwipeUp", "Swipe Up", new Point(560, 160), PinSide.Top),
        new("SwipeDown", "Swipe Down", new Point(600, 250), PinSide.Bottom),
        new("SwipeLeft", "Swipe Left", new Point(530, 205), PinSide.Left),
        new("SwipeRight", "Swipe Right", new Point(670, 205), PinSide.Right),
        new("View", "Share", new Point(464, 283), PinSide.Top),
        new("Menu", "Options", new Point(735, 283), PinSide.Top),
        new("Guide", "PS Button", new Point(600, 452), PinSide.Bottom),

        // Gyroscope & motion
        new("VGYRO", "VGYRO", new Point(600, 175), PinSide.Top),
        new("TiltUp", "Tilt Up", new Point(640, 160), PinSide.Top),
        new("TiltDown", "Tilt Down", new Point(640, 250), PinSide.Bottom),
        new("TiltLeft", "Tilt Left", new Point(545, 250), PinSide.Left),
        new("TiltRight", "Tilt Right", new Point(655, 175), PinSide.Right),
    ];

    /// <summary>PlayStation nomenclature for the shared canonical anchors (plan 11 §1.1 step 3).</summary>
    private static readonly Dictionary<string, string> PlayStationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "Cross", ["B"] = "Circle", ["X"] = "Square", ["Y"] = "Triangle",
        ["LB"] = "L1", ["RB"] = "R1", ["LT"] = "L2", ["RT"] = "R2",
        ["View"] = "Share", ["Menu"] = "Options", ["Guide"] = "PS Button",
        ["LS"] = "L3", ["RS"] = "R3"
    };

    private const double MinBadgeSpacing = 40;
    private const double BadgeWidth = 118;
    private const double LeftRailX = 128;   // badge right edge
    private const double RightRailX = 1072; // badge left edge
    private const double TopRailY = 74;
    private const double BottomRailY = 700;

    private readonly Dictionary<string, BindingDisplay> _bindings = [];
    private List<(string Source, string Target, string Activator, int Shift)> _rawMappings = [];
    private string? _selectedButton;
    private int _activeShift;
    private bool _isRear;
    private string _currentSubmode = "gamepad";
    private string _chassis = "xbox";
    private bool _playStationNomenclature;

    private ButtonAnchor[] CurrentAnchors => _chassis == "ds4" ? Ds4Anchors : Anchors;

    private DispatcherTimer? _inputGlowTimer;
    private DispatcherTimer? _shuntHudTimer;
    private bool? _lastShuntHudGyroState;
    private string? _lastShuntHudTouchAction;
    private static readonly HttpClient ShuntHudClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:35474/"),
        Timeout = TimeSpan.FromSeconds(2)
    };
    private readonly HashSet<string> _currentlyGlowingButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Brush? Fill, Brush? Stroke, double StrokeThickness, System.Windows.Media.Effects.Effect? Effect)> _defaultButtonAppearances = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Brush GlowStroke = ColorBrush("#81CF66");
    private static readonly Brush GlowFill = ColorBrush("#334DC81F");
    private static readonly System.Windows.Media.Effects.DropShadowEffect GlowEffect = new()
    {
        Color = (Color)ColorConverter.ConvertFromString("#81CF66"),
        BlurRadius = 16,
        ShadowDepth = 0,
        Opacity = 1.0
    };

    public event EventHandler<string>? BindingRequested;
    public event EventHandler<int>? ShiftChanged;

    public int ActiveShift => _activeShift;
    public int ActiveShiftId => _activeShift + 1;

    public GamepadCanvasView()
    {
        InitializeComponent();
        AttachButtonInteractivity();
        CacheDefaultAppearances();
        RebuildAnnotations();
        Loaded += (_, _) => StartInputGlowPolling();
        Unloaded += (_, _) => StopInputGlowPolling();
        Loaded += (_, _) => StartShuntHudPolling();
        Unloaded += (_, _) => StopShuntHudPolling();
    }

    private void StartShuntHudPolling()
    {
        if (_shuntHudTimer != null) return;
        _shuntHudTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _shuntHudTimer.Tick += ShuntHudTimer_Tick;
        _shuntHudTimer.Start();
        _ = RefreshShuntHudAsync();
    }

    private void StopShuntHudPolling()
    {
        if (_shuntHudTimer == null) return;
        _shuntHudTimer.Stop();
        _shuntHudTimer.Tick -= ShuntHudTimer_Tick;
        _shuntHudTimer = null;
    }

    private async void ShuntHudTimer_Tick(object? sender, EventArgs e) => await RefreshShuntHudAsync();

    private async System.Threading.Tasks.Task RefreshShuntHudAsync()
    {
        await System.Threading.Tasks.Task.Yield();
        try
        {
            var engine = App.LocalEngine;
            var enabled = engine.IsShuntModeEnabled;
            if (enabled && ShuntHud.Visibility != Visibility.Visible)
            {
                ShuntActiveDot.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(700))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
            else if (!enabled)
            {
                ShuntActiveDot.BeginAnimation(UIElement.OpacityProperty, null);
                ShuntActiveDot.Opacity = 1;
            }
            ShuntHud.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (!enabled)
            {
                _lastShuntHudGyroState = null;
                return;
            }

            var gyroEnabled = engine.IsGyroEnabled;
            if (_lastShuntHudGyroState.HasValue && _lastShuntHudGyroState.Value != gyroEnabled)
            {
                if (gyroEnabled) SystemSounds.Asterisk.Play();
                else SystemSounds.Exclamation.Play();
            }
            _lastShuntHudGyroState = gyroEnabled;
            ShuntGyroStatus.Text = gyroEnabled ? "GYRO: STREAMING" : "GYRO: PAUSED";
            ShuntGyroStatus.Foreground = gyroEnabled ? ColorBrush("#81CF66") : ColorBrush("#A0A0A0");

            var action = engine.LastTouchpadAction;
            var actionAt = engine.LastTouchpadActionAt;
            var recent = action != null && actionAt.HasValue && DateTimeOffset.UtcNow - actionAt.Value < TimeSpan.FromMilliseconds(700);
            var displayedAction = recent ? FormatShuntTouchAction(action!) : "Back + face: touchpad click";
            ShuntTouchAction.Text = displayedAction;
            if (recent && !string.Equals(_lastShuntHudTouchAction, action, StringComparison.Ordinal))
            {
                ShuntTouchAction.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(240)) { AutoReverse = true });
            }
            _lastShuntHudTouchAction = recent ? action : null;
            var activeFill = ColorBrush("#AAFFE795");
            var idleFill = ColorBrush("#303030");
            ShuntTouchLeftTop.Background = recent && action!.Contains("Left", StringComparison.OrdinalIgnoreCase) || recent && action!.Contains("Top", StringComparison.OrdinalIgnoreCase) ? activeFill : idleFill;
            ShuntTouchRightTop.Background = recent && action!.Contains("Right", StringComparison.OrdinalIgnoreCase) || recent && action!.Contains("Top", StringComparison.OrdinalIgnoreCase) ? activeFill : idleFill;
            ShuntTouchLeftBottom.Background = recent && action!.Contains("Left", StringComparison.OrdinalIgnoreCase) || recent && action!.Contains("Bottom", StringComparison.OrdinalIgnoreCase) ? activeFill : idleFill;
            ShuntTouchRightBottom.Background = recent && action!.Contains("Right", StringComparison.OrdinalIgnoreCase) || recent && action!.Contains("Bottom", StringComparison.OrdinalIgnoreCase) ? activeFill : idleFill;
        }
        catch { ShuntHud.Visibility = Visibility.Collapsed; }
    }

    private static string FormatShuntTouchAction(string action) => action switch
    {
        "Swipe up" => "↑  Swipe up",
        "Swipe down" => "↓  Swipe down",
        "Swipe left" => "←  Swipe left",
        "Swipe right" => "→  Swipe right",
        _ => $"●  {action}"
    };

    // ------------------------------ Public API ------------------------------

    /// <summary>Feeds current profile mappings into the canvas badges.</summary>
    /// <param name="mappings">Tuples of (source mask description, target description, activator, shift layer 1-based).</param>
    public void SetMappings(IEnumerable<(string Source, string Target, string Activator, int Shift)> mappings)
    {
        _rawMappings = mappings.ToList();
        UpdateBindingsForCurrentShift();
    }

    private void UpdateBindingsForCurrentShift()
    {
        _bindings.Clear();
        foreach (var (source, target, activator, shift) in _rawMappings)
        {
            if (shift - 1 != _activeShift) continue;
            foreach (var anchor in CurrentAnchors)
            {
                if (!anchor.Aliases.Any(alias =>
                        source.Contains(alias, StringComparison.OrdinalIgnoreCase) &&
                        !source.Contains("not " + alias, StringComparison.OrdinalIgnoreCase))) continue;

                var label = activator is "single" or "" ? target : $"{target} [{activator}]";
                _bindings[anchor.Name] = new BindingDisplay(anchor.Label, label, activator);
                break;
            }
        }
        RebuildAnnotations();
    }

    public void SetActiveShift(int shift)
    {
        _activeShift = shift;
        if (shift == 0) ShiftMainPill.IsChecked = true;
        else if (shift == 1) ShiftS1Pill.IsChecked = true;
        else if (shift == 2) ShiftS2Pill.IsChecked = true;
        else if (shift == 3) ShiftS3Pill.IsChecked = true;
        else if (shift == 4) ShiftS4Pill.IsChecked = true;
        UpdateBindingsForCurrentShift();
    }

    // ------------------------------ Interaction ------------------------------

    private void AttachButtonInteractivity()
    {
        foreach (var anchor in Anchors)
        {
            if (FindName("Btn_" + anchor.Name) is FrameworkElement element) Attach(element, anchor.Name);
        }
        foreach (var anchor in Ds4Anchors)
        {
            if (FindName("BtnDS4_" + anchor.Name) is FrameworkElement element) Attach(element, anchor.Name);
        }
    }

    private void Attach(FrameworkElement element, string name)
    {
        element.Cursor = Cursors.Hand;
        element.Tag = name;
        element.MouseDown += ButtonShape_MouseDown;
    }

    private void ButtonShape_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
        {
            SelectButton(name);
            BindingRequested?.Invoke(this, name);
        }
    }

    public void SelectButton(string name)
    {
        // Clear previous highlight
        foreach (var anchor in CurrentAnchors)
        {
            if (TryGetElement(anchor.Name) is { } prev)
            {
                if (TryGetDefaultAppearance(anchor.Name, out var def))
                {
                    if (prev is Shape shape)
                    {
                        shape.Fill = def.Fill ?? Brushes.Transparent;
                        shape.Stroke = def.Stroke;
                        shape.StrokeThickness = def.StrokeThickness;
                        shape.Effect = def.Effect;
                    }
                    else if (prev is Border border)
                    {
                        border.Background = def.Fill ?? Brushes.Transparent;
                        border.BorderBrush = def.Stroke;
                        border.Effect = def.Effect;
                    }
                }
                else
                {
                    if (prev is Shape shape)
                    {
                        shape.Stroke = IdleButtonStroke;
                        shape.StrokeThickness = 1.5;
                        shape.Fill = Brushes.Transparent;
                        shape.Effect = null;
                    }
                    else if (prev is Border border)
                    {
                        border.Background = Brushes.Transparent;
                        border.BorderBrush = IdleButtonStroke;
                        border.Effect = null;
                    }
                }
            }
        }

        _selectedButton = name;
        if (TryGetElement(name) is { } selected)
        {
            if (selected is Shape shape)
            {
                shape.Stroke = (Brush)Application.Current.FindResource("StatusSuccessBrush");
                shape.StrokeThickness = 3;
                shape.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#4DC81F"),
                    BlurRadius = 12,
                    ShadowDepth = 0
                };
            }
            else if (selected is Border border)
            {
                border.BorderBrush = (Brush)Application.Current.FindResource("StatusSuccessBrush");
                border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#4DC81F"),
                    BlurRadius = 12,
                    ShadowDepth = 0
                };
            }
        }
        RebuildAnnotations(highlightBadge: name);
    }

    private FrameworkElement? TryGetElement(string name)
    {
        if (_isRear && !RearOnlyTarget(name))
            return null;

        // Chassis-prefixed element first, then the shared overlay elements (touchpad/gyro).
        var prefixed = FindName((_chassis == "ds4" ? "BtnDS4_" : "Btn_") + name) as FrameworkElement;
        return prefixed ?? FindName("Btn_" + name) as FrameworkElement;
    }

    private FrameworkElement? FindElementForChassis(string chassis, string name)
    {
        var prefixed = FindName((chassis == "ds4" ? "BtnDS4_" : "Btn_") + name) as FrameworkElement;
        return prefixed ?? FindName("Btn_" + name) as FrameworkElement;
    }

    private static bool RearOnlyTarget(string name) => name is "P1" or "P2" or "P3" or "P4" or "LTStop" or "RTStop";

    private static bool IsTouchpadTarget(string name) =>
        name is "TouchClick" or "TouchTap" or "SwipeUp" or "SwipeDown" or "SwipeLeft" or "SwipeRight";

    private static bool IsGyroTarget(string name) =>
        name is "VGYRO" or "TiltUp" or "TiltDown" or "TiltLeft" or "TiltRight";

    // ------------------------------ Annotations ------------------------------

    private void RebuildAnnotations(string? highlightBadge = null)
    {
        LeaderLineLayer.Children.Clear();
        BadgeLayer.Children.Clear();

        var activeAnchors = CurrentAnchors.Where(anchor =>
        {
            if (_currentSubmode == "touchpad")
                return IsTouchpadTarget(anchor.Name);
            if (_currentSubmode == "gyro")
                return IsGyroTarget(anchor.Name);

            // Gamepad default sub-mode
            if (IsTouchpadTarget(anchor.Name) || IsGyroTarget(anchor.Name))
                return false;

            return anchor.RearOnly == _isRear;
        }).ToArray();

        foreach (PinSide side in Enum.GetValues<PinSide>())
        {
            var sideAnchors = activeAnchors
                .Where(anchor => anchor.Side == side)
                .OrderBy(anchor => side is PinSide.Left or PinSide.Right ? anchor.Center.Y : anchor.Center.X)
                .ToArray();
            if (sideAnchors.Length == 0) continue;

            var railPositions = ComputeRailPositions(sideAnchors, side);
            for (var index = 0; index < sideAnchors.Length; index++)
                AddBadge(sideAnchors[index], railPositions[index], highlightBadge);
        }
    }

    private double[] ComputeRailPositions(ButtonAnchor[] anchors, PinSide side)
    {
        var span = (anchors.Length - 1) * MinBadgeSpacing;
        var railStart = side switch
        {
            PinSide.Left or PinSide.Right => 180,
            PinSide.Top => 90,
            _ => BottomRailY - 90
        };
        var railLength = side switch
        {
            PinSide.Left or PinSide.Right => 480,
            PinSide.Top => 400,
            _ => 260
        };
        var start = span > railLength
            ? railStart
            : railStart + (railLength - span) / 2 - span / 2;
        start = Math.Max(railStart, Math.Min(start, railStart + railLength - span));

        return Enumerable.Range(0, anchors.Length).Select(i => start + i * MinBadgeSpacing).ToArray();
    }

    private void AddBadge(ButtonAnchor anchor, double railPosition, string? highlightBadge)
    {
        var hasBinding = _bindings.TryGetValue(anchor.Name, out var binding);
        var isHighlighted = anchor.Name == highlightBadge || anchor.Name == _selectedButton;
        var anchorLabel = AnchorLabel(anchor);

        var badge = new Border
        {
            Width = BadgeWidth,
            Style = (Style)Application.Current.Resources["BindingBadgeStyle"],
            ToolTip = $"{anchorLabel}: {binding?.Target ?? "not mapped"}"
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = anchorLabel,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("TextMutedBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = binding?.Target ?? "unmapped",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.FindResource(
                hasBinding ? "TextPrimaryBrush" : "TextDisabledBrush")
        });
        badge.Child = stack;

        var (badgeLeft, badgeTop, badgeAnchor) = PositionBadge(anchor.Side, railPosition);
        Canvas.SetLeft(badge, badgeLeft);
        Canvas.SetTop(badge, badgeTop);

        badge.Cursor = Cursors.Hand;
        badge.MouseDown += (_, _) =>
        {
            SelectButton(anchor.Name);
            BindingRequested?.Invoke(this, anchor.Name);
        };
        badge.MouseEnter += (_, _) =>
        {
            if (TryGetElement(anchor.Name) is Shape s) s.Stroke = (Brush)Application.Current.FindResource("StatusSuccessBrush");
            RebuildAnnotations(highlightBadge: anchor.Name);
        };
        badge.MouseLeave += (_, _) =>
        {
            if (TryGetElement(anchor.Name) is Shape s && anchor.Name != _selectedButton)
            {
                if (TryGetDefaultAppearance(anchor.Name, out var def) && def.Stroke != null)
                    s.Stroke = def.Stroke;
                else
                    s.Stroke = IdleButtonStroke;
            }
            RebuildAnnotations();
        };

        BadgeLayer.Children.Add(badge);
        DrawLeaderLine(anchor, badgeAnchor, hasBinding, isHighlighted);
    }

    private (double Left, double Top, Point Anchor) PositionBadge(PinSide side, double railPosition)
    {
        const double badgeHeight = 36;
        return side switch
        {
            PinSide.Left => (LeftRailX - BadgeWidth, railPosition - badgeHeight / 2,
                new Point(LeftRailX, railPosition)),
            PinSide.Right => (RightRailX, railPosition - badgeHeight / 2,
                new Point(RightRailX, railPosition)),
            PinSide.Top => (railPosition - BadgeWidth / 2, TopRailY - badgeHeight,
                new Point(railPosition, TopRailY)),
            _ => (railPosition - BadgeWidth / 2, BottomRailY,
                new Point(railPosition, BottomRailY))
        };
    }

    private void DrawLeaderLine(ButtonAnchor anchor, Point badgeAnchor, bool hasBinding, bool isHighlighted)
    {
        var dx = badgeAnchor.X - anchor.Center.X;
        var dy = badgeAnchor.Y - anchor.Center.Y;
        Point p1, p2;

        if (anchor.Side is PinSide.Left or PinSide.Right)
        {
            p1 = new Point(anchor.Center.X + dx * 0.45, anchor.Center.Y);
            p2 = new Point(anchor.Center.X + dx * 0.55, badgeAnchor.Y);
        }
        else
        {
            p1 = new Point(anchor.Center.X, anchor.Center.Y + dy * 0.45);
            p2 = new Point(badgeAnchor.X, anchor.Center.Y + dy * 0.55);
        }

        var figure = new PathFigure { StartPoint = anchor.Center, IsFilled = false };
        figure.Segments.Add(new BezierSegment(p1, p2, badgeAnchor, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        LeaderLineLayer.Children.Add(new Path
        {
            Data = geometry,
            Stroke = isHighlighted
                ? (Brush)Application.Current.FindResource("StatusSuccessBrush")
                : ShiftBrush(0.55),
            StrokeThickness = isHighlighted ? 2 : 1.2,
            StrokeDashArray = hasBinding ? null : new DoubleCollection { 3, 3 }
        });
    }

    private static readonly Brush IdleButtonStroke = ColorBrush("#FFFFFF");

    private static SolidColorBrush ColorBrush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private Brush ShiftBrush(double opacity = 1)
    {
        var brush = (SolidColorBrush)Application.Current.FindResource($"Shift{_activeShift}Brush");
        if (opacity == 1) return brush;
        var clone = brush.Clone();
        clone.Opacity = opacity;
        return clone;
    }

    // ------------------------------ Chassis & nomenclature (plan 11 §2-3) ------------------------------

    /// <summary>Display label for an anchor honoring the active chassis and PlayStation nomenclature.</summary>
    private string AnchorLabel(ButtonAnchor anchor)
    {
        if (_chassis == "ds4" || _playStationNomenclature)
            return PlayStationNames.TryGetValue(anchor.Name, out var psName) ? psName : anchor.Label;
        return anchor.Label;
    }

    /// <summary>Switches the vector silhouette between the Xbox One and DualShock 4 chassis.</summary>
    public void SetChassis(string chassis)
    {
        chassis = chassis == "ds4" ? "ds4" : "xbox";
        if (_chassis == chassis) return;

        _chassis = chassis;
        ApplyChassisVisibility();
        if (_selectedButton is not null && TryGetElement(_selectedButton) is null)
            _selectedButton = null;
        RebuildAnnotations();
    }

    private void ApplyChassisVisibility()
    {
        var ds4 = _chassis == "ds4";
        if (FrontGroup != null) FrontGroup.Visibility = ds4 ? Visibility.Collapsed : Visibility.Visible;
        if (RearGroup != null) RearGroup.Visibility = ds4 || !_isRear ? Visibility.Collapsed : Visibility.Visible;
        if (Ds4Group != null) Ds4Group.Visibility = ds4 ? Visibility.Visible : Visibility.Collapsed;
        if (FlipToggle != null) FlipToggle.IsEnabled = !ds4;
        if (ds4 && _isRear) SetRearView(false);
    }

    private void ChassisPill_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
            SetChassis(tag);
    }

    /// <summary>
    /// Aligns the canvas with the profile's virtual target (plan 11 §1.1 step 3): DS4/DS3 targets
    /// switch to the DualShock 4 silhouette with PlayStation badge labels; other gamepad targets
    /// use the Xbox One silhouette with Xbox labels.
    /// </summary>
    public void SetTargetNomenclature(string target)
    {
        _playStationNomenclature = target is "ds4" or "ds3";
        SetChassis(_playStationNomenclature ? "ds4" : "xbox");
        RebuildAnnotations();
    }

    // ------------------------------ Sub-mode, Flip & shift ------------------------------

    public void SetSubmode(string tag)
    {
        _currentSubmode = tag;
        if (SubmodeGamepad != null) SubmodeGamepad.IsChecked = tag == "gamepad";
        if (SubmodeTouchpad != null) SubmodeTouchpad.IsChecked = tag == "touchpad";
        if (SubmodeGyro != null) SubmodeGyro.IsChecked = tag == "gyro";
        if (TouchpadOverlay != null)
            TouchpadOverlay.Visibility = tag == "touchpad" ? Visibility.Visible : Visibility.Collapsed;
        if (GyroOverlay != null)
            GyroOverlay.Visibility = tag == "gyro" ? Visibility.Visible : Visibility.Collapsed;
        RebuildAnnotations();
    }

    public void SetRearView(bool rear)
    {
        if (_chassis == "ds4") return; // DualShock 4 has no rear paddles
        if (_isRear == rear) return;
        _isRear = rear;
        if (FlipToggle != null) FlipToggle.IsChecked = rear;
        if (FrontGroup != null) FrontGroup.Visibility = rear ? Visibility.Collapsed : Visibility.Visible;
        if (RearGroup != null) RearGroup.Visibility = rear ? Visibility.Visible : Visibility.Collapsed;
        if (CurrentAnchors.FirstOrDefault(a => a.Name == _selectedButton) is { } selected &&
            selected.RearOnly != _isRear)
            _selectedButton = null;
        RebuildAnnotations();
    }

    private void SubmodePill_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            SetSubmode(tag);
        }
    }

    private void TouchpadAction_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            SelectButton(tag);
            BindingRequested?.Invoke(this, tag);
        }
    }

    private void FlipToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRear == (FlipToggle.IsChecked == true))
        {
            RebuildAnnotations();
            return;
        }
        _isRear = FlipToggle.IsChecked == true;

        var collapse = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        collapse.Completed += (_, _) =>
        {
            FrontGroup.Visibility = _isRear ? Visibility.Collapsed : Visibility.Visible;
            RearGroup.Visibility = _isRear ? Visibility.Visible : Visibility.Collapsed;
            if (CurrentAnchors.FirstOrDefault(a => a.Name == _selectedButton) is { } selected &&
                selected.RearOnly != _isRear)
                _selectedButton = null;

            var expand = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            StageCanvas.RenderTransform = new ScaleTransform(0, 1);
            StageCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
            StageCanvas.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
            RebuildAnnotations();
        };
        StageCanvas.RenderTransform = new ScaleTransform(1, 1);
        StageCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
        StageCanvas.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, collapse);
    }

    private void ShiftPill_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var shift))
        {
            _activeShift = shift;
            UpdateBindingsForCurrentShift();
            ShiftChanged?.Invoke(this, _activeShift);
        }
    }

    // ------------------------------ Real-Time Input Visualizer ------------------------------

    private void CacheDefaultAppearances()
    {
        foreach (var chassis in new[] { "xbox", "ds4" })
        {
            foreach (var anchor in chassis == "ds4" ? Ds4Anchors : Anchors)
            {
                if (FindElementForChassis(chassis, anchor.Name) is not { } element) continue;
                _defaultButtonAppearances[$"{chassis}:{anchor.Name}"] = element switch
                {
                    Shape shape => (shape.Fill, shape.Stroke, shape.StrokeThickness, (System.Windows.Media.Effects.Effect?)shape.Effect),
                    Border border => (border.Background, border.BorderBrush, border.BorderThickness.Left, (System.Windows.Media.Effects.Effect?)border.Effect),
                    _ => (default(Brush?), default(Brush?), default(double), default(System.Windows.Media.Effects.Effect?))
                };
            }
        }
    }

    private bool TryGetDefaultAppearance(string name,
        out (Brush? Fill, Brush? Stroke, double StrokeThickness, System.Windows.Media.Effects.Effect? Effect) def) =>
        _defaultButtonAppearances.TryGetValue($"{_chassis}:{name}", out def);

    private void InputGlowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (InputGlowToggle.IsChecked == true)
        {
            StartInputGlowPolling();
        }
        else
        {
            StopInputGlowPolling();
        }
    }

    public void StartInputGlowPolling()
    {
        if (_inputGlowTimer != null) return;
        _inputGlowTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _inputGlowTimer.Tick += InputGlowTimer_Tick;
        _inputGlowTimer.Start();
    }

    public void StopInputGlowPolling()
    {
        _inputGlowTimer?.Stop();
        _inputGlowTimer = null;
        ClearAllGlows();
    }

    private void InputGlowTimer_Tick(object? sender, EventArgs e)
    {
        if (InputGlowToggle.IsChecked != true)
        {
            ClearAllGlows();
            return;
        }

        var state = new Shunt.Engine.Hardware.XInputNative.XINPUT_STATE();
        bool found = false;

        for (uint slot = 0; slot < 4; slot++)
        {
            if (Shunt.Engine.Hardware.XInputNative.GetState(slot, ref state) == Shunt.Engine.Hardware.XInputNative.ERROR_SUCCESS)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            ClearAllGlows();
            return;
        }

        var wButtons = state.Gamepad.wButtons;
        var activeNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_A) != 0) activeNow.Add("A");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_B) != 0) activeNow.Add("B");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_X) != 0) activeNow.Add("X");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_Y) != 0) activeNow.Add("Y");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) activeNow.Add("LB");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) activeNow.Add("RB");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_LEFT_THUMB) != 0) activeNow.Add("LS");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_RIGHT_THUMB) != 0) activeNow.Add("RS");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_START) != 0) activeNow.Add("Menu");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_BACK) != 0) activeNow.Add("View");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_GUIDE) != 0) activeNow.Add("Guide");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_UP) != 0) activeNow.Add("DUp");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_DOWN) != 0) activeNow.Add("DDown");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_LEFT) != 0) activeNow.Add("DLeft");
        if ((wButtons & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_RIGHT) != 0) activeNow.Add("DRight");

        // Analog triggers
        if (state.Gamepad.bLeftTrigger > 30) activeNow.Add("LT");
        if (state.Gamepad.bRightTrigger > 30) activeNow.Add("RT");

        // Apply glow to newly pressed buttons
        foreach (var btn in activeNow)
        {
            ApplyButtonGlow(btn);
        }

        // Remove glow from released buttons
        var released = _currentlyGlowingButtons.Except(activeNow).ToList();
        foreach (var btn in released)
        {
            RemoveButtonGlow(btn);
        }

        // Deflect analog stick thumbs
        UpdateStickDeflection(FindName(_chassis == "ds4" ? "Thumb_DS4_LS" : "Thumb_LS") as Ellipse, state.Gamepad.sThumbLX, state.Gamepad.sThumbLY);
        UpdateStickDeflection(FindName(_chassis == "ds4" ? "Thumb_DS4_RS" : "Thumb_RS") as Ellipse, state.Gamepad.sThumbRX, state.Gamepad.sThumbRY);
    }

    public void ApplyButtonGlow(string buttonName)
    {
        if (_currentlyGlowingButtons.Contains(buttonName)) return;
        _currentlyGlowingButtons.Add(buttonName);

        if (TryGetElement(buttonName) is { } element)
        {
            if (element is Shape shape)
            {
                shape.Stroke = GlowStroke;
                shape.StrokeThickness = 2.5;
                shape.Fill = GlowFill;
                shape.Effect = GlowEffect;
            }
            else if (element is Border border)
            {
                border.BorderBrush = GlowStroke;
                border.Background = GlowFill;
                border.Effect = GlowEffect;
            }
        }
    }

    public void RemoveButtonGlow(string buttonName)
    {
        _currentlyGlowingButtons.Remove(buttonName);

        if (TryGetElement(buttonName) is { } element)
        {
            if (buttonName == _selectedButton)
            {
                if (element is Shape shape)
                {
                    shape.Stroke = (Brush)Application.Current.FindResource("StatusSuccessBrush");
                    shape.StrokeThickness = 3;
                    shape.Fill = Brushes.Transparent;
                    shape.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = (Color)ColorConverter.ConvertFromString("#4DC81F"),
                        BlurRadius = 12,
                        ShadowDepth = 0
                    };
                }
                else if (element is Border border)
                {
                    border.BorderBrush = (Brush)Application.Current.FindResource("StatusSuccessBrush");
                    border.Background = Brushes.Transparent;
                }
            }
            else if (TryGetDefaultAppearance(buttonName, out var def))
            {
                if (element is Shape shape)
                {
                    shape.Fill = def.Fill ?? Brushes.Transparent;
                    shape.Stroke = def.Stroke;
                    shape.StrokeThickness = def.StrokeThickness;
                    shape.Effect = def.Effect;
                }
                else if (element is Border border)
                {
                    border.Background = def.Fill ?? Brushes.Transparent;
                    border.BorderBrush = def.Stroke;
                    border.Effect = def.Effect;
                }
            }
            else
            {
                if (element is Shape shape)
                {
                    shape.Fill = Brushes.Transparent;
                    shape.Stroke = IdleButtonStroke;
                    shape.StrokeThickness = 1.5;
                    shape.Effect = null;
                }
                else if (element is Border border)
                {
                    border.Background = Brushes.Transparent;
                    border.BorderBrush = IdleButtonStroke;
                    border.Effect = null;
                }
            }
        }
    }

    public void PulseGlow(string buttonName, int durationMs = 450)
    {
        var anchor = CanvasButtonResolver.ResolveAnchor(buttonName) ?? buttonName;
        if (RearOnlyTarget(anchor))
        {
            SetRearView(true);
        }
        else if (IsTouchpadTarget(anchor))
        {
            SetRearView(false);
            SetSubmode("touchpad");
        }
        else if (IsGyroTarget(anchor))
        {
            SetRearView(false);
            SetSubmode("gyro");
        }
        else
        {
            SetRearView(false);
            SetSubmode("gamepad");
        }

        ApplyButtonGlow(anchor);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            RemoveButtonGlow(anchor);
        };
        timer.Start();
    }

    private DispatcherTimer? _vibrationToastTimer;

    public void ShowVibrationToast(byte largeMotor, byte smallMotor, int ledNumber = 0)
    {
        if (VibrationToast == null || VibrationToastText == null) return;
        if (largeMotor == 0 && smallMotor == 0 && ledNumber == 0) return;

        var ledInfo = ledNumber > 0 ? $" · LED {ledNumber}" : "";
        VibrationToastText.Text = $"Haptic Rumble: L {largeMotor} · R {smallMotor}{ledInfo}";

        VibrationToast.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
        VibrationToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _vibrationToastTimer?.Stop();
        _vibrationToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _vibrationToastTimer.Tick += (_, _) =>
        {
            _vibrationToastTimer?.Stop();
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) => VibrationToast.Visibility = Visibility.Collapsed;
            VibrationToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        _vibrationToastTimer.Start();
    }

    private void ClearAllGlows()
    {
        foreach (var btn in _currentlyGlowingButtons.ToList())
        {
            RemoveButtonGlow(btn);
        }
        _currentlyGlowingButtons.Clear();

        UpdateStickDeflection(FindName(_chassis == "ds4" ? "Thumb_DS4_LS" : "Thumb_LS") as Ellipse, 0, 0);
        UpdateStickDeflection(FindName(_chassis == "ds4" ? "Thumb_DS4_RS" : "Thumb_RS") as Ellipse, 0, 0);
    }

    private static void UpdateStickDeflection(Ellipse? thumb, short rawX, short rawY)
    {
        if (thumb == null) return;
        double normX = Math.Clamp(rawX / 32767.0, -1.0, 1.0);
        double normY = Math.Clamp(rawY / 32767.0, -1.0, 1.0);

        if (Math.Abs(normX) < 0.08) normX = 0;
        if (Math.Abs(normY) < 0.08) normY = 0;

        if (thumb.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            thumb.RenderTransform = tt;
        }

        tt.X = normX * 7.5;
        tt.Y = -normY * 7.5;
    }
}
