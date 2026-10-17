using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Shunt.Core.Scancodes;

namespace Shunt.UI.Views.Workspace;

/// <summary>
/// Full QWERTY interactive keyboard canvas (plan 11 §4): 104 clickable keycaps with
/// click-to-bind, live detection glow, mapped-key pips and the WASD cluster highlight.
/// </summary>
public partial class KeyboardCanvasView : UserControl
{
    private sealed record KeyDef(string Anchor, string Label, double Width = 1);

    private const double Unit = 44;      // standard 1u keycap
    private const double Gap = 4;
    private const double KeyHeight = 44;
    private const double RowStep = 48;
    private const double MainX = 160;    // keycaps centered on the 1000 x 360 plate
    private const double NavX = 900;
    private const double TopY = 233;

    private static readonly string[] UpperRow = ["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"];
    private static readonly string[] HomeRow = ["A", "S", "D", "F", "G", "H", "J", "K", "L"];
    private static readonly string[] LowerRow = ["Z", "X", "C", "V", "B", "N", "M"];

    /// <summary>Physical <see cref="Key"/> to canvas anchor mapping for live detection polling.</summary>
    public static readonly IReadOnlyDictionary<Key, string> KeyMap = BuildKeyMap();

    private readonly Dictionary<string, Border> _keycaps = [];
    private readonly Dictionary<string, Ellipse> _pips = [];
    private readonly Dictionary<string, (Brush Fill, Brush Stroke, double StrokeThickness, System.Windows.Media.Effects.Effect? Effect)> _defaults = [];
    private readonly HashSet<string> _glowing = [];
    private string? _selectedAnchor;

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

    public KeyboardCanvasView()
    {
        InitializeComponent();
        BuildLayout();
    }

    // ------------------------------ Layout ------------------------------

    private void BuildLayout()
    {
        var keyboardPlate = new Border
        {
            Width = 1000,
            Height = 360,
            CornerRadius = new CornerRadius(18),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#99FFFFFF")),
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(keyboardPlate, 100);
        Canvas.SetTop(keyboardPlate, 195);
        StageCanvas.Children.Add(keyboardPlate);

        var rows = new KeyDef[][]
        {
            RowOf(new KeyDef("KeyGrave", "`"), new KeyDef("Key1", "1"), new KeyDef("Key2", "2"), new KeyDef("Key3", "3"),
                new KeyDef("Key4", "4"), new KeyDef("Key5", "5"), new KeyDef("Key6", "6"), new KeyDef("Key7", "7"),
                new KeyDef("Key8", "8"), new KeyDef("Key9", "9"), new KeyDef("Key0", "0"), new KeyDef("KeyMinus", "-"),
                new KeyDef("KeyEquals", "="), new KeyDef("KeyBackspace", "Backspace", 2)),
            RowOf(new KeyDef("KeyTab", "Tab", 1.5))
                .Concat(CreateLetterKeys(UpperRow))
                .Concat(RowOf(new KeyDef("KeyLBracket", "["), new KeyDef("KeyRBracket", "]"), new KeyDef("KeyBackslash", "\\", 1.5)))
                .ToArray(),
            RowOf(new KeyDef("KeyCaps", "Caps", 1.75))
                .Concat(CreateLetterKeys(HomeRow))
                .Concat(RowOf(new KeyDef("KeySemicolon", ";"), new KeyDef("KeyApostrophe", "'"), new KeyDef("KeyEnter", "Enter", 2.25)))
                .ToArray(),
            RowOf(new KeyDef("KeyLShift", "Shift", 2.25))
                .Concat(CreateLetterKeys(LowerRow))
                .Concat(RowOf(new KeyDef("KeyComma", ","), new KeyDef("KeyPeriod", "."), new KeyDef("KeySlash", "/"), new KeyDef("KeyRShift", "Shift", 2.75)))
                .ToArray(),
            RowOf(new KeyDef("KeyLCtrl", "Ctrl", 1.25), new KeyDef("KeyLWin", "Win", 1.25), new KeyDef("KeyLAlt", "Alt", 1.25),
                new KeyDef("KeySpace", "Spacebar", 6.25), new KeyDef("KeyRAlt", "Alt", 1.25), new KeyDef("KeyRWin", "Win", 1.25),
                new KeyDef("KeyMenu", "Menu", 1.25), new KeyDef("KeyRCtrl", "Ctrl", 1.25))
        };

        // Function row and ANSI/TKL main typing block.
        var y = TopY;
        AddKeycap(MainX, y, "KeyEsc", "Esc", Unit);
        var fx = MainX + 2 * Unit + 3 * Gap;
        for (var f = 1; f <= 12; f++)
        {
            AddKeycap(fx, y, $"KeyF{f}", $"F{f}", Unit);
            fx += Unit + Gap;
            if (f is 4 or 8) fx += 2 * Gap;
        }
        var px = NavX;
        AddKeycap(px, y, "KeyPrtSc", "PrtSc", Unit);
        AddKeycap(px + Unit + Gap, y, "KeyScrLk", "ScrLk", Unit);
        AddKeycap(px + 2 * (Unit + Gap), y, "KeyPause", "Pause", Unit);

        y += RowStep;
        foreach (var row in rows)
        {
            var x = MainX;
            foreach (var key in row)
            {
                var width = key.Width * Unit + (key.Width - 1) * Gap;
                AddKeycap(x, y, key.Anchor, key.Label, width);
                x += width + Gap;
            }
            y += RowStep;
        }

        // TKL navigation block: six-key editing cluster plus inverted-T arrows.
        AddKeycap(NavX, TopY + RowStep, "KeyIns", "Ins", Unit);
        AddKeycap(NavX + Unit + Gap, TopY + RowStep, "KeyHome", "Home", Unit);
        AddKeycap(NavX + 2 * (Unit + Gap), TopY + RowStep, "KeyPgUp", "PgUp", Unit);
        AddKeycap(NavX, TopY + RowStep * 2, "KeyDel", "Del", Unit);
        AddKeycap(NavX + Unit + Gap, TopY + RowStep * 2, "KeyEnd", "End", Unit);
        AddKeycap(NavX + 2 * (Unit + Gap), TopY + RowStep * 2, "KeyPgDn", "PgDn", Unit);
        AddKeycap(NavX + Unit + Gap, TopY + RowStep * 4, "KeyUp", "\u2191", Unit);
        AddKeycap(NavX, TopY + RowStep * 5, "KeyLeft", "\u2190", Unit);
        AddKeycap(NavX + Unit + Gap, TopY + RowStep * 5, "KeyDown", "\u2193", Unit);
        AddKeycap(NavX + 2 * (Unit + Gap), TopY + RowStep * 5, "KeyRight", "\u2192", Unit);

        // WASD movement cluster highlight (plan 11 §4.1 feature 4)
        var wasdBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4D81CF66")),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#144DC81F")),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(wasdBorder, MainX + 84 - 5);
        Canvas.SetTop(wasdBorder, TopY + 2 * RowStep - 5);
        wasdBorder.Width = 3 * Unit + 2 * Gap + 10;
        wasdBorder.Height = 2 * RowStep + 10;
        Panel.SetZIndex(wasdBorder, -1);
        StageCanvas.Children.Add(wasdBorder);
        var wasdLabel = new TextBlock
        {
            Text = "WASD \u2192 Left Stick synthesis",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#81CF66")),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(wasdLabel, MainX + 84 + 2);
        Canvas.SetTop(wasdLabel, TopY + 2 * RowStep - 18);
        StageCanvas.Children.Add(wasdLabel);
    }

    private static KeyDef[] RowOf(params KeyDef[] keys) => keys;

    private static KeyDef[] CreateLetterKeys(string[] letters) =>
        letters.Select(letter => new KeyDef($"Key{letter}", letter)).ToArray();

    private void AddKeycap(double x, double y, string anchor, string label, double width, double height = KeyHeight)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.2),
            Cursor = Cursors.Hand,
            ToolTip = $"{label} · {anchor}"
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = label.Length > 2 ? 10 : 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        border.Child = stack;

        border.MouseEnter += (_, _) =>
        {
            if (_selectedAnchor == anchor || _glowing.Contains(anchor)) return;
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE795"));
            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF"));
        };
        border.MouseLeave += (_, _) =>
        {
            if (_selectedAnchor == anchor || _glowing.Contains(anchor)) return;
            var appearance = _defaults[anchor];
            border.Background = appearance.Fill;
            border.BorderBrush = appearance.Stroke;
        };

        border.MouseDown += (_, _) =>
        {
            SelectButton(anchor);
            BindingRequested?.Invoke(this, anchor);
        };

        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        StageCanvas.Children.Add(border);
        _keycaps[anchor] = border;
        _defaults[anchor] = (border.Background, border.BorderBrush, border.BorderThickness.Left, border.Effect);
    }

    // ------------------------------ Interaction & glow ------------------------------

    public void SelectButton(string anchor)
    {
        foreach (var (name, cap) in _keycaps)
        {
            var def = _defaults[name];
            cap.Background = def.Fill;
            cap.BorderBrush = def.Stroke;
            cap.Effect = def.Effect;
        }

        _selectedAnchor = anchor;
        if (_keycaps.TryGetValue(anchor, out var selected))
        {
            selected.BorderBrush = (Brush)Application.Current.FindResource("StatusSuccessBrush");
            selected.BorderThickness = new Thickness(2);
            selected.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#4DC81F"),
                BlurRadius = 12,
                ShadowDepth = 0
            };
        }
        UpdatePips();
    }

    public void ApplyButtonGlow(string anchor)
    {
        if (!_glowing.Add(anchor)) return;
        if (_keycaps.TryGetValue(anchor, out var cap))
        {
            cap.BorderBrush = GlowStroke;
            cap.Background = GlowFill;
            cap.Effect = GlowEffect;
        }
    }

    public void RemoveButtonGlow(string anchor)
    {
        _glowing.Remove(anchor);
        if (!_keycaps.TryGetValue(anchor, out var cap)) return;
        if (anchor == _selectedAnchor)
        {
            cap.BorderBrush = (Brush)Application.Current.FindResource("StatusSuccessBrush");
            cap.Background = (Brush)Application.Current.FindResource("SurfaceLevel2Brush");
            cap.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#4DC81F"),
                BlurRadius = 12,
                ShadowDepth = 0
            };
            return;
        }
        var def = _defaults[anchor];
        cap.Background = def.Fill;
        cap.BorderBrush = def.Stroke;
        cap.Effect = def.Effect;
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

    // ------------------------------ Mapped key pips ------------------------------

    private List<(string Source, string Target)> _rawMappings = [];

    /// <summary>Feeds profile mappings in to mark keycaps that have active bindings.</summary>
    public void SetMappings(IEnumerable<(string Source, string Target)> mappings)
    {
        _rawMappings = mappings.ToList();
        UpdatePips();
    }

    private void UpdatePips()
    {
        foreach (var pip in _pips.Values)
            pip.Visibility = Visibility.Collapsed;

        foreach (var (source, _) in _rawMappings)
        {
            foreach (var (anchor, cap) in _keycaps)
            {
                var aliases = CanvasButtonResolver.GetAliases(anchor);
                if (!aliases.Any(alias => source.Contains(alias, StringComparison.OrdinalIgnoreCase))) continue;

                if (!_pips.TryGetValue(anchor, out var pip))
                {
                    pip = new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Fill = (Brush)Application.Current.FindResource("StatusSuccessBrush"),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(pip, Canvas.GetLeft(cap) + cap.Width - 10);
                    Canvas.SetTop(pip, Canvas.GetTop(cap) + 4);
                    StageCanvas.Children.Add(pip);
                    _pips[anchor] = pip;
                }
                pip.Visibility = Visibility.Visible;
            }
        }
    }

    // ------------------------------ Physical key mapping ------------------------------

    private static IReadOnlyDictionary<Key, string> BuildKeyMap()
    {
        var map = new Dictionary<Key, string>();
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            map[(Key)Enum.Parse(typeof(Key), letter.ToString())] = $"Key{letter}";
        for (var digit = 0; digit <= 9; digit++)
            map[digit == 0 ? Key.D0 : (Key)((int)Key.D1 + digit - 1)] = $"Key{digit}";
        for (var f = 1; f <= 12; f++)
            map[(Key)((int)Key.F1 + f - 1)] = $"KeyF{f}";
        for (var n = 0; n <= 9; n++)
            map[(Key)((int)Key.NumPad0 + n)] = $"KeyNum{n}";

        map[Key.Space] = "KeySpace";
        map[Key.Enter] = "KeyEnter";
        map[Key.Tab] = "KeyTab";
        map[Key.Escape] = "KeyEsc";
        map[Key.Back] = "KeyBackspace";
        map[Key.CapsLock] = "KeyCaps";
        map[Key.LeftShift] = "KeyLShift";
        map[Key.RightShift] = "KeyRShift";
        map[Key.LeftCtrl] = "KeyLCtrl";
        map[Key.RightCtrl] = "KeyRCtrl";
        map[Key.LeftAlt] = "KeyLAlt";
        map[Key.RightAlt] = "KeyRAlt";
        map[Key.LWin] = "KeyLWin";
        map[Key.RWin] = "KeyRWin";
        map[Key.Apps] = "KeyMenu";
        map[Key.Up] = "KeyUp";
        map[Key.Down] = "KeyDown";
        map[Key.Left] = "KeyLeft";
        map[Key.Right] = "KeyRight";
        map[Key.Insert] = "KeyIns";
        map[Key.Delete] = "KeyDel";
        map[Key.Home] = "KeyHome";
        map[Key.End] = "KeyEnd";
        map[Key.Prior] = "KeyPgUp";
        map[Key.Next] = "KeyPgDn";
        map[Key.PrintScreen] = "KeyPrtSc";
        map[Key.Scroll] = "KeyScrLk";
        map[Key.Pause] = "KeyPause";
        map[Key.NumLock] = "KeyNumLock";
        map[Key.Divide] = "KeyNumDiv";
        map[Key.Multiply] = "KeyNumMul";
        map[Key.Subtract] = "KeyNumSub";
        map[Key.Add] = "KeyNumAdd";
        map[Key.Decimal] = "KeyNumDec";
        map[Key.Separator] = "KeyNumDec";
        map[Key.OemMinus] = "KeyMinus";
        map[Key.OemPlus] = "KeyEquals";
        map[Key.OemOpenBrackets] = "KeyLBracket";
        map[Key.OemCloseBrackets] = "KeyRBracket";
        map[Key.OemPipe] = "KeyBackslash";
        map[Key.OemSemicolon] = "KeySemicolon";
        map[Key.OemQuotes] = "KeyApostrophe";
        map[Key.OemTilde] = "KeyGrave";
        map[Key.OemComma] = "KeyComma";
        map[Key.OemPeriod] = "KeyPeriod";
        map[Key.OemQuestion] = "KeySlash";
        return map;
    }
}
