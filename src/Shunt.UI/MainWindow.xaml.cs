using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Microsoft.Win32;
using Shunt.Core.Models;
using Shunt.Core.Scancodes;
using Shunt.Engine;
using Shunt.UI.Services;
using Shunt.UI.Views.Inspector;
using Shunt.UI.Views.Workspace;

namespace Shunt.UI;

public partial class MainWindow : Window
{
    private static readonly Uri EngineAddress = new("http://127.0.0.1:35474/");
    private readonly HttpClient _httpClient = new() { BaseAddress = EngineAddress, Timeout = TimeSpan.FromSeconds(4) };
    private readonly ObservableCollection<ProfileFile> _profiles = [];
    private readonly ObservableCollection<MappingRow> _mappings = [];
    private readonly ObservableCollection<DiscoveredDeviceItem> _discoveredDevices = [];
    private bool _isDetectionModeActive;
    private CancellationTokenSource? _detectionCts;
    private EngineEventClient? _eventClient;
    private System.Windows.Threading.DispatcherTimer? _deviceRefreshTimer;
    private JsonObject? _profile;
    private string? _profilePath;
    private bool _isDirty;
    private bool _isLoading;
    private bool _refreshingProfileView;
    private bool _engineConnected;
    private bool _allowClose;
    private ProfileFile? _selectedProfileFile;
    private bool _isDrawerOpen;
    private bool _isAimXpTheme;
    private RadialMenuOverlay? _radialMenuOverlay;
    private readonly ICollectionView _profileTilesView;
    private string _currentTarget = "360";
    private string _canvasMode = "controller";
    private IReadOnlyList<MappingChoice> _outputTargets = MappingChoiceProvider.GetOutputTargets();

    public ObservableCollection<MappingRow> Mappings => _mappings;
    public MappingRow? SelectedMapping { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ProfileComboBox.ItemsSource = _profiles;
        _profileTilesView = CollectionViewSource.GetDefaultView(_profiles);
        _profileTilesView.Filter = FilterProfileTiles;
        GameTilesControl.ItemsSource = _profileTilesView;
        DeviceSelectorCombo.ItemsSource = _discoveredDevices;
        DeviceSelectorCombo.DisplayMemberPath = "Name";
        TargetTypeCombo.ItemsSource = TargetTypes;
        TargetButtonCombo.ItemsSource = _outputTargets;
        InputControlCombo.ItemsSource = InputSources;
        ActivatorCombo.ItemsSource = Activators;
        ModeCombo.ItemsSource = Modes;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        GamepadCanvas.BindingRequested += GamepadCanvas_BindingRequested;
        GamepadCanvas.ShiftChanged += GamepadCanvas_ShiftChanged;
        KeyboardCanvas.BindingRequested += ChassisCanvas_BindingRequested;
        MouseCanvas.BindingRequested += ChassisCanvas_BindingRequested;
        MouseCanvas.ZoneConfigurationChanged += MouseCanvas_ZoneConfigurationChanged;
        BindingFrame.ApplyRequested += BindingFrame_ApplyRequested;
        BindingFrame.LayerSelectionChanged += BindingFrame_LayerSelectionChanged;
        BindingFrame.OpenInListRequested += BindingFrame_OpenInListRequested;
        BindingFrame.ChordMappingRequested += BindingFrame_ChordMappingRequested;
        BindingFrame.CloseRequested += (_, _) => ToggleBindingFrame(false);
        BindingFrame.SetChordPartners(InputSources.Where(source => source.Category == "Gamepad"));
        StageGyro.SettingsChanged += StageGyro_SettingsChanged;
    }

    private static readonly Dictionary<string, string[]> CanvasButtonAliases =
        CanvasButtonResolver.AllAnchorAliases.ToDictionary(k => k.Key, v => v.Value);

    private void EnsureEngineEventClient()
    {
        if (_eventClient != null) return;
        _eventClient = new EngineEventClient(EngineAddress, Dispatcher);
        _eventClient.KeyPressedReceived += OnSseKeyPressed;
        _eventClient.VibrationFeedbackReceived += OnSseVibrationFeedback;
        _eventClient.Start();
    }

    private void OnSseVibrationFeedback(VibrationFeedbackEvent evt)
    {
        GamepadCanvas.ShowVibrationToast(evt.LargeMotor, evt.SmallMotor, evt.LedNumber);
    }

    private void OnSseKeyPressed(KeyPressedEvent evt)
    {
        if (!_isDetectionModeActive) return;

        var buttonName = evt.ButtonName;
        var anchor = CanvasButtonResolver.ResolveAnchor(buttonName);

        // Route keyboard / mouse chassis events to their canvases (plan 11 §4.1, §5).
        var deviceType = evt.DeviceType?.ToLowerInvariant();
        if (anchor?.StartsWith("Key") == true || deviceType is "keyboard")
        {
            if (anchor is null) return;
            SetCanvasMode("keyboard");
            KeyboardCanvas.PulseGlow(anchor);
            KeyboardCanvas.SelectButton(anchor);
            ChassisCanvas_BindingRequested(KeyboardCanvas, anchor);
            SetFooter($"Detection mode (SSE): Detected physical key [{anchor}] press. Inspector opened.");
            return;
        }
        if (MouseZoneAnchors.Contains(anchor ?? string.Empty) || deviceType is "mouse")
        {
            var mouseAnchor = anchor ?? buttonName;
            if (!MouseZoneAnchors.Contains(mouseAnchor)) return;
            SetCanvasMode("mouse");
            MouseCanvas.PulseGlow(mouseAnchor);
            MouseCanvas.SelectButton(mouseAnchor);
            ChassisCanvas_BindingRequested(MouseCanvas, mouseAnchor);
            SetFooter($"Detection mode (SSE): Detected physical mouse [{mouseAnchor}] input. Inspector opened.");
            return;
        }

        if (anchor != null)
        {
            GamepadCanvas.PulseGlow(anchor);
            GamepadCanvas.SelectButton(anchor);
            GamepadCanvas_BindingRequested(this, anchor);
            SetFooter($"Detection mode (SSE): Detected physical [{anchor}] press ({buttonName}). Inspector opened.");
        }
        else
        {
            var match = _mappings.FirstOrDefault(row =>
                row.Source.Contains(buttonName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                SelectedMapping = match;
                MappingsGrid.SelectedItem = match;
                SelectedInputText.Text = match.Source;
                TargetButtonCombo.SelectedItem = OutputTargets.FirstOrDefault(button => button.Id == match.TargetButtonId && button.Category == "Gamepad")
                    ?? OutputTargets.FirstOrDefault(button => button.Id == match.TargetButtonId);
                ActivatorCombo.SelectedItem = Activators.FirstOrDefault(choice => choice.Value == match.Activator);
                ModeCombo.SelectedItem = Modes.FirstOrDefault(choice => choice.Value == match.Mode);
                UpdateMappingButton.IsEnabled = true;
            }
            else
            {
                SelectedMapping = null;
                MappingsGrid.SelectedItem = null;
            }

            var activeShiftId = GamepadCanvas.ActiveShiftId;
            BindingFrame.Populate(
                buttonName,
                match is null ? null : (match.Source, match.Target, match.Activator, match.Mode),
                OutputTargets,
                [.. Activators.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
                [.. Modes.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
                GetAvailableShiftLayers(),
                activeShiftId);
            ToggleBindingFrame(true);
            SetFooter($"Detection mode (SSE): Detected physical [{buttonName}] input. Inspector opened.");
        }
    }

    private void GamepadCanvas_BindingRequested(object? sender, string buttonName)
    {
        if (!CanvasButtonAliases.TryGetValue(buttonName, out var aliases)) return;
        var activeShiftId = GamepadCanvas.ActiveShiftId;
        var match = _mappings.FirstOrDefault(row =>
        {
            var shiftText = row.Shift.StartsWith("Layer ", StringComparison.Ordinal) ? row.Shift["Layer ".Length..] : "1";
            return int.TryParse(shiftText, out var rowShiftId) && rowShiftId == activeShiftId &&
                   aliases.Any(alias => row.Source.Contains(alias, StringComparison.OrdinalIgnoreCase));
        });

        if (match is not null)
        {
            SelectedMapping = match;
            MappingsGrid.SelectedItem = match;
            SelectedInputText.Text = match.Source;
            TargetButtonCombo.SelectedItem = OutputTargets.FirstOrDefault(button => button.Id == match.TargetButtonId && button.Category == "Gamepad")
                ?? OutputTargets.FirstOrDefault(button => button.Id == match.TargetButtonId);
            ActivatorCombo.SelectedItem = Activators.FirstOrDefault(choice => choice.Value == match.Activator);
            ModeCombo.SelectedItem = Modes.FirstOrDefault(choice => choice.Value == match.Mode);
            ShiftLayerCombo.SelectedValue = activeShiftId;
            UpdateMappingButton.IsEnabled = true;
        }
        else
        {
            SelectedMapping = null;
            MappingsGrid.SelectedItem = null;
            ShiftLayerCombo.SelectedValue = activeShiftId;
        }

        var anchorLabel = aliases is { Length: > 0 }
            ? aliases[0].Replace(" /", string.Empty, StringComparison.Ordinal)
            : buttonName;
        BindingFrame.Populate(
            anchorLabel,
            match is null ? null : (match.Source, match.Target, match.Activator, match.Mode),
            OutputTargets,
            [.. Activators.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
            [.. Modes.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
            GetAvailableShiftLayers(),
            activeShiftId);
        ToggleBindingFrame(true);
        SetFooter(match is null
            ? $"No mapping exists on Layer {activeShiftId} for {buttonName}. Choose a target output in the inspector to create one."
            : $"Editing {match.Source} on Layer {activeShiftId} in the binding frame inspector.");
    }

    // ----------------------- Multi-chassis canvas (plan 11 §2, §4-5) -----------------------

    private void SetCanvasMode(string mode)
    {
        _canvasMode = mode;
        GamepadCanvas.Visibility = mode == "controller" ? Visibility.Visible : Visibility.Collapsed;
        KeyboardCanvas.Visibility = mode == "keyboard" ? Visibility.Visible : Visibility.Collapsed;
        MouseCanvas.Visibility = mode == "mouse" ? Visibility.Visible : Visibility.Collapsed;
        if (mode == "keyboard")
        {
            KeyboardCanvas.SetMappings(_mappings.Select(row => (row.Source, row.Target)));
        }
        else if (mode == "mouse")
        {
            MouseCanvas.SetMappings(_mappings.Select(row => (row.Source, row.Target)));
        }
    }

    /// <summary>Shared binding-request flow for the keyboard and mouse canvases.</summary>
    private void ChassisCanvas_BindingRequested(object? sender, string anchor)
    {
        var aliases = CanvasButtonResolver.GetAliases(anchor);
        var match = _mappings.FirstOrDefault(row =>
            aliases.Any(alias => row.Source.Contains(alias, StringComparison.OrdinalIgnoreCase)));

        if (match is not null)
        {
            SelectedMapping = match;
            MappingsGrid.SelectedItem = match;
            SelectedInputText.Text = match.Source;
            TargetButtonCombo.SelectedItem = _outputTargets.FirstOrDefault(button => button.Id == match.TargetButtonId)
                ?? _outputTargets.FirstOrDefault(button => button.Name.Equals(match.Target, StringComparison.OrdinalIgnoreCase));
            ActivatorCombo.SelectedItem = Activators.FirstOrDefault(choice => choice.Value == match.Activator);
            ModeCombo.SelectedItem = Modes.FirstOrDefault(choice => choice.Value == match.Mode);
            UpdateMappingButton.IsEnabled = true;
        }
        else
        {
            SelectedMapping = null;
            MappingsGrid.SelectedItem = null;
        }

        var anchorLabel = sender == KeyboardCanvas && KeyboardSources.FirstOrDefault(source => aliases.Contains(source.Name)) is { } keySource
            ? keySource.Name
            : aliases.FirstOrDefault() ?? anchor;
        BindingFrame.Populate(
            anchorLabel,
            match is null ? null : (match.Source, match.Target, match.Activator, match.Mode),
            _outputTargets,
            [.. Activators.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
            [.. Modes.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
            GetAvailableShiftLayers(),
            GamepadCanvas.ActiveShiftId);
        ToggleBindingFrame(true);
        SetFooter(match is null
            ? $"No mapping exists for {anchorLabel}. Choose a target output in the inspector to create one."
            : $"Editing {match.Source} in the binding frame inspector.");
    }

    private void ToggleBindingFrame(bool open)    {
        try
        {
            if (open)
            {
                BindingFrame.Visibility = Visibility.Visible;
                double from = BindingFrame.ActualWidth > 0 ? BindingFrame.ActualWidth : 0.0;
                var widthAnimation = new DoubleAnimation(from, 320.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BindingFrame.BeginAnimation(WidthProperty, widthAnimation);
            }
            else
            {
                double from = BindingFrame.ActualWidth > 0 ? BindingFrame.ActualWidth : 320.0;
                var widthAnimation = new DoubleAnimation(from, 0.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                widthAnimation.Completed += (_, _) =>
                {
                    BindingFrame.Visibility = Visibility.Collapsed;
                };
                BindingFrame.BeginAnimation(WidthProperty, widthAnimation);
            }
        }
        catch
        {
            // Fallback without animation
            BindingFrame.Width = open ? 320.0 : 0.0;
            BindingFrame.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BindingFrame_ApplyRequested(object? sender, EventArgs e)
    {
        var (target, activator, mode, shiftId) = BindingFrame.ReadSelection();
        if (target is not MappingChoice selectedTarget)
        {
            SetFooter("Choose a target action before updating this control.", isError: true);
            return;
        }

        TargetButtonCombo.SelectedItem = OutputTargets.FirstOrDefault(button => button.Id == selectedTarget.Id && button.Category == selectedTarget.Category) ?? selectedTarget;
        ActivatorCombo.SelectedItem = Activators.FirstOrDefault(choice => choice.Value == activator);
        ModeCombo.SelectedItem = Modes.FirstOrDefault(choice => choice.Value == mode);
        ShiftLayerCombo.SelectedValue = shiftId;

        if (SelectedMapping is null)
        {
            if (_profile is null || GetInspectorSourceChoice() is not { } source ||
                GetSourceDeviceIdFor(source) is not int sourceDeviceId)
            {
                SetFooter("This control cannot be added from the inspector. Choose its source in the mappings list.", isError: true);
                return;
            }

            AddMapping(source, selectedTarget, sourceDeviceId,
                Activators.FirstOrDefault(item => item.Value == activator) ?? Activators[0],
                Modes.FirstOrDefault(item => item.Value == mode) ?? Modes[0],
                shiftId);
            BindingFrame.Populate(
                source.Name,
                (source.Name, selectedTarget.Name, activator, mode),
                OutputTargets,
                [.. Activators.Select(item => new BindingFrameView.Choice(item.Name, item.Value))],
                [.. Modes.Select(item => new BindingFrameView.Choice(item.Name, item.Value))],
                GetAvailableShiftLayers(),
                shiftId);
            return;
        }

        UpdateMappingButton_Click(this, new RoutedEventArgs());
        PushMappingsToCanvas();
        BindingFrame.Populate(
            BindingFrame.SelectedButtonLabel,
            (SelectedMapping.Source, SelectedMapping.Target, SelectedMapping.Activator, SelectedMapping.Mode),
            OutputTargets,
            [.. Activators.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
            [.. Modes.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
            GetAvailableShiftLayers(),
            shiftId);
    }

    private void GamepadCanvas_ShiftChanged(object? sender, int shift)
    {
        var activeShiftId = shift + 1;
        ShiftLayerCombo.SelectedValue = activeShiftId;
        if (BindingFrame.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(BindingFrame.SelectedButtonLabel))
        {
            GamepadCanvas_BindingRequested(this, BindingFrame.SelectedButtonLabel);
        }
    }

    private void BindingFrame_LayerSelectionChanged(object? sender, int shiftId)
    {
        GamepadCanvas.SetActiveShift(shiftId - 1);
    }

    private IReadOnlyList<BindingFrameView.ShiftChoice> GetAvailableShiftLayers()
    {
        var shifts = (_profile?["shifts"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var result = new List<BindingFrameView.ShiftChoice>
        {
            new("Layer 1 (Main)", 1)
        };
        for (int i = 2; i <= 5; i++)
        {
            var customShift = shifts.FirstOrDefault(s => s["id"]?.GetValue<int>() == i);
            var desc = customShift?["description"]?.GetValue<string>()
                       ?? (customShift?["type"]?.GetValue<string>() is { } t && t != "default" ? t : $"Shift {i - 1}");
            result.Add(new($"Layer {i} ({desc})", i));
        }
        return result;
    }

    private static readonly HashSet<string> MouseZoneAnchors =
    [
        "LMB", "RMB", "MMB", "Mouse4", "Mouse5",
        "WheelUp", "WheelDown", "MWheelLeft", "MWheelRight",
        "MDeflectUp", "MDeflectDown", "MDeflectLeft", "MDeflectRight"
    ];

    private MappingChoice? GetInspectorSourceChoice()
    {
        var anchor = CanvasButtonResolver.ResolveAnchor(BindingFrame.SelectedButtonLabel);
        if (anchor is null) return null;

        // Keyboard / mouse chassis anchors resolve to their scancode sources (plan 11 §4-5).
        if (anchor.StartsWith("Key", StringComparison.Ordinal))
        {
            var aliases = CanvasButtonResolver.GetAliases(anchor);
            return KeyboardSources.FirstOrDefault(source => aliases.Contains(source.Name));
        }
        if (MouseZoneAnchors.Contains(anchor))
        {
            var aliases = CanvasButtonResolver.GetAliases(anchor);
            return MouseSources.FirstOrDefault(source => aliases.Contains(source.Name));
        }

        var sourceId = anchor switch
        {
            "A" => 1, "B" => 2, "X" => 3, "Y" => 4,
            "LB" => 5, "RB" => 6, "View" => 7, "Menu" => 8,
            "LS" => 9, "RS" => 10, "Guide" => 11,
            "P1" => 29, "P2" => 30, "P3" => 31, "P4" => 32,
            "DUp" => 33, "DDown" => 34, "DLeft" => 35, "DRight" => 36,
            "TouchClick" => 37, "TouchTap" => 38,
            "SwipeUp" => 41, "SwipeDown" => 42, "SwipeLeft" => 43, "SwipeRight" => 44,
            "TiltLeft" => 45, "TiltRight" => 46, "TiltUp" => 47, "TiltDown" => 48,
            "LT" => 51, "RT" => 55,
            _ => 0
        };
        return sourceId == 0 ? null : InputSources.FirstOrDefault(source => source.Id == sourceId);
    }

    /// <summary>Hardware device id for a mapping source, following the bundled profile
    /// convention (gamepad=1, keyboard=11, mouse=23) when the profile declares no device.</summary>
    private int? GetSourceDeviceIdFor(MappingChoice source) => source.Category switch
    {
        "Keyboard" => GetHardwareDeviceId("keyboard") ?? 11,
        "Mouse" => GetHardwareDeviceId("mouse") ?? 23,
        _ => GetGamepadInputDeviceId()
    };

    private void BindingFrame_OpenInListRequested(object? sender, EventArgs e)
    {
        ToggleBindingFrame(false);
        _isLoading = true;
        NavMappingsItem.IsChecked = true;
        _isLoading = false;
        NavItem_Checked(NavMappingsItem, new RoutedEventArgs());
        if (SelectedMapping is { } row)
        {
            MappingsGrid.SelectedItem = row;
            MappingsGrid.ScrollIntoView(row);
        }
    }

    private void BindingFrame_ChordMappingRequested(MappingChoice partner)
    {
        var selection = BindingFrame.ReadSelection();
        if (_profile is null || GetInspectorSourceChoice() is not { } source ||
            selection.Target is not MappingChoice target ||
            GetSourceDeviceIdFor(source) is not int sourceDeviceId ||
            GetSourceDeviceIdFor(partner) is not int partnerDeviceId)
        {
            SetFooter("Choose a base control, chord partner, and target before adding the chord.", isError: true);
            return;
        }
        if (source.Id == partner.Id)
        {
            SetFooter("A chord needs a second, different control.", isError: true);
            return;
        }

        AddMapping(source, target, sourceDeviceId,
            Activators.FirstOrDefault(item => item.Value == selection.Activator) ?? Activators[0],
            Modes.FirstOrDefault(item => item.Value == selection.Mode) ?? Modes[0], selection.ShiftId,
            partner, partnerDeviceId);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R &&
            (e.KeyboardDevice.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            ToggleRadialMenuOverlay();
            e.Handled = true;
            return;
        }

        // Live keyboard canvas glow: physical presses illuminate the matching keycap (plan 11 §4.1).
        if (_canvasMode == "keyboard" && KeyboardCanvasView.KeyMap.TryGetValue(e.Key, out var kbAnchor))
        {
            KeyboardCanvas.PulseGlow(kbAnchor, 350);
            if (_isDetectionModeActive)
            {
                KeyboardCanvas.SelectButton(kbAnchor);
                ChassisCanvas_BindingRequested(KeyboardCanvas, kbAnchor);
                SetFooter($"Detection mode: Detected physical key [{kbAnchor}] press. Inspector opened.");
                e.Handled = true;
                return;
            }
        }

        // Hardware slot shortcuts Ctrl+1..Ctrl+4.
        if (e.KeyboardDevice.Modifiers != ModifierKeys.Control) return;
        var target = e.Key switch
        {
            Key.D1 or Key.NumPad1 => SlotPillById("1"),
            Key.D2 or Key.NumPad2 => SlotPillById("2"),
            Key.D3 or Key.NumPad3 => SlotPillById("3"),
            Key.D4 or Key.NumPad4 => SlotPillById("4"),
            _ => null
        };
        if (target is null) return;
        target.IsChecked = true;
        e.Handled = true;
    }

    private void ToggleRadialMenuOverlay()
    {
        if (_radialMenuOverlay is { IsVisible: true })
        {
            _radialMenuOverlay.Close();
            _radialMenuOverlay = null;
            return;
        }

        _radialMenuOverlay = new RadialMenuOverlay { Owner = this };
        _radialMenuOverlay.Closed += (_, _) => _radialMenuOverlay = null;
        _radialMenuOverlay.Show();
        _radialMenuOverlay.Activate();
    }

    private RadioButton? SlotPillById(string content) =>
        TryFindElement<RadioButton>(this, element => element is { GroupName: "Slots", Content: string slot } && slot == content);

    private static T? TryFindElement<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T matched && predicate(matched)) return matched;
            if (TryFindElement(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    // ----------------------- Shell chrome & navigation -----------------------

    private void BrandButton_Click(object sender, RoutedEventArgs e) => ToggleGamesDrawer();

    private void ToggleGamesDrawer()
    {
        _isDrawerOpen = !_isDrawerOpen;
        var targetHeight = _isDrawerOpen ? 160.0 : 0.0;
        if (_isDrawerOpen) GamesDrawer.Visibility = Visibility.Visible;

        try
        {
            double fromHeight = GamesDrawer.ActualHeight > 0 ? GamesDrawer.ActualHeight : (_isDrawerOpen ? 0.0 : 160.0);
            var heightAnimation = new DoubleAnimation(fromHeight, targetHeight, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            if (!_isDrawerOpen)
            {
                heightAnimation.Completed += (_, _) => GamesDrawer.Visibility = Visibility.Collapsed;
            }
            GamesDrawer.BeginAnimation(HeightProperty, heightAnimation);

            if (BrandButton.Template?.FindName("Chevron", BrandButton) is System.Windows.Shapes.Path chevron &&
                chevron.RenderTransform is RotateTransform rotation)
            {
                double currentAngle = rotation.Angle;
                rotation.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(currentAngle, _isDrawerOpen ? 0 : 180, TimeSpan.FromMilliseconds(220)));
            }
        }
        catch
        {
            GamesDrawer.Height = targetHeight;
            if (!_isDrawerOpen) GamesDrawer.Visibility = Visibility.Collapsed;
        }
    }

    private bool FilterProfileTiles(object item) =>
        string.IsNullOrWhiteSpace(SearchGamesBox.Text) ||
        (item as ProfileFile)?.Name.Contains(SearchGamesBox.Text, StringComparison.OrdinalIgnoreCase) == true;

    private void SearchGamesBox_TextChanged(object sender, TextChangedEventArgs e) => _profileTilesView.Refresh();

    private void GameTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProfileFile file)
            ProfileComboBox.SelectedItem = file;
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { CommandParameter: string stage }) return;
        var mapping = new Dictionary<string, UIElement>
        {
            ["gamepad"] = StageGamepad,
            ["mappings"] = StageMappings,
            ["mouse"] = StageMouse,
            ["sticks"] = StageSticks,
            ["triggers"] = StageTriggers,
            ["gyro"] = StageGyro,
            ["combo"] = StageCombo,
            ["radial"] = StageRadial,
            ["crosshair"] = StageCrosshair
        };
        if (!mapping.TryGetValue(stage, out var active)) return;
        foreach (var panel in mapping.Values) panel.Visibility = ReferenceEquals(panel, active) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ThemeSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        _isAimXpTheme = !_isAimXpTheme;
        App.ApplyTheme(_isAimXpTheme ? App.AimXPTheme : App.BaseTemplateTheme);
        ThemeSwitchButton.Content = _isAimXpTheme ? "Theme: AimXP" : "Theme: Dark";
        SetFooter(_isAimXpTheme ? "Switched to the AimXP theme." : "Switched to the BaseTemplate dark theme.");
    }

    private void RemapToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (EngineStatusText is null) return;
        SetFooter(RemapToggle.IsChecked == true
            ? "Master remap switch ON. Apply a profile to engage the virtual controller."
            : "Master remap switch OFF. Physical inputs pass through untouched.");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.Dialogs.PreferencesWindow { Owner = this };
        dlg.ShowDialog();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        if (sender is Button pin) pin.Opacity = Topmost ? 1.0 : 0.65;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null) return;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        // Keep the taskbar reachable while the borderless window is maximized.
        if (WindowState == WindowState.Maximized)
        {
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
        }
    }

    private IReadOnlyList<MappingChoice> OutputTargets => _outputTargets;
    private static readonly IReadOnlyList<MappingChoice> InputSources = MappingChoiceProvider.GetInputSources();
    private static readonly IReadOnlyList<MappingChoice> KeyboardSources = MappingChoiceProvider.GetKeyboardInputSources();
    private static readonly IReadOnlyList<MappingChoice> MouseSources = MappingChoiceProvider.GetMouseInputSources();
    private static readonly IReadOnlyList<TargetControllerChoice> TargetTypes = MappingChoiceProvider.GetTargetChoices();

    private static readonly Choice[] Activators =
    [
        new("Single press", "single"), new("Double press", "double"),
        new("Triple press", "triple"), new("Long press", "long"),
        new("Start press", "start"), new("Release press", "release")
    ];

    private static readonly Choice[] Modes =
    [
        new("Hold until release", "hold_until_release"), new("One time", "onetime"),
        new("Toggle", "toggle"), new("Turbo", "turbo")
    ];

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateProfiles();
        if (_profiles.Count > 0)
        {
            // Default to an Xbox Controller mapping profile (Native Behavior / Gamepad)
            int IndexOfProfile(string keyword)
            {
                for (var i = 0; i < _profiles.Count; i++)
                {
                    if (_profiles[i].Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
                return -1;
            }

            var defaultIdx = IndexOfProfile("Native Behavior.rewasd");
            if (defaultIdx < 0) defaultIdx = IndexOfProfile("Native");
            if (defaultIdx < 0) defaultIdx = IndexOfProfile("Gamepad to Keyboard");
            if (defaultIdx < 0) defaultIdx = IndexOfProfile("Gamepad");
            if (defaultIdx < 0) defaultIdx = 0;

            ProfileComboBox.SelectedIndex = defaultIdx;
        }
        else
            CreateNewProfile();

        await RefreshConnectionAsync();

        _deviceRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _deviceRefreshTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        _deviceRefreshTimer.Start();
    }

    private void PopulateProfiles()
    {
        _profiles.Clear();
        var directory = GetProfilesDirectory();
        Directory.CreateDirectory(directory);
        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "profiles");
        if (Directory.Exists(bundledDirectory))
        {
            foreach (var bundledPath in Directory.GetFiles(bundledDirectory, "*.rewasd"))
            {
                var targetPath = Path.Combine(directory, Path.GetFileName(bundledPath));
                if (!File.Exists(targetPath)) File.Copy(bundledPath, targetPath);
            }
        }

        foreach (var path in Directory.GetFiles(directory, "*.rewasd").OrderBy(Path.GetFileName))
            _profiles.Add(new ProfileFile(Path.GetFileName(path), path));
    }

    private static string GetProfilesDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Shunt", "Profiles");

    private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ProfileComboBox.SelectedItem is not ProfileFile selected) return;
        if (!await ConfirmDiscardOrSaveAsync())
        {
            _isLoading = true;
            ProfileComboBox.SelectedItem = _selectedProfileFile;
            _isLoading = false;
            return;
        }

        _selectedProfileFile = selected;
        await LoadProfileAsync(selected.Path);
    }

    private async Task LoadProfileAsync(string path)
    {
        try
        {
            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject
                ?? throw new InvalidDataException("The profile root must be a JSON object.");
            _profile = parsed;
            _profilePath = path;
            _isDirty = false;
            RefreshProfileView();
            SetFooter($"Loaded {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            SetFooter($"Could not open profile: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.Message, "Profile could not be opened", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshProfileView()
    {
        _refreshingProfileView = true;
        _mappings.Clear();
        SelectedMapping = null;
        MappingsGrid.SelectedItem = null;
        TargetButtonCombo.SelectedItem = null;
        ActivatorCombo.SelectedItem = null;
        ModeCombo.SelectedItem = null;
        UpdateMappingButton.IsEnabled = false;
        MappingHelpText.Text = "Select a mapping to edit. Saving updates only that mapping and preserves other profile data.";

        if (_profile is null)
        {
            _currentTarget = "360";
            TargetTypeCombo.SelectedItem = TargetTypes.FirstOrDefault(output => output.Value == "360");
            SetCanvasMode("controller");
            _outputTargets = MappingChoiceProvider.GetOutputTargets(VirtualGamepadType.Xbox360);
            TargetButtonCombo.ItemsSource = _outputTargets;
            GamepadCanvas.SetTargetNomenclature("360");
            MouseCanvas.SetZoneConfiguration(new MouseZoneConfig());
            ShiftLayerCombo.ItemsSource = null;
            ProfileInfoText.Text = "Open a profile to inspect its mappings.";
            MappingCountText.Text = "0 mappings";
            SaveProfileButton.IsEnabled = false;
            ApplyProfileButton.IsEnabled = false;
            AddMappingButton.IsEnabled = false;
            _refreshingProfileView = false;
            return;
        }

        ShiftLayerCombo.ItemsSource = GetAvailableShiftLayers();
        ShiftLayerCombo.SelectedValue = GamepadCanvas.ActiveShiftId;

        var target = _profile["devices"]?["virtual"]?["gamepads"]?[0]?["type"]?.GetValue<string>() ?? "360";
        _currentTarget = target;
        TargetTypeCombo.SelectedItem = VirtualTargetCatalog.GetFamilyChoice(target);
        _outputTargets = MappingChoiceProvider.GetOutputTargets(VirtualTargetCatalog.Parse(target) ?? VirtualGamepadType.Xbox360);
        TargetButtonCombo.ItemsSource = _outputTargets;
        GamepadCanvas.SetTargetNomenclature(target);
        SetCanvasMode(target.Equals("kbm", StringComparison.OrdinalIgnoreCase) ? "keyboard" : "controller");
        MouseCanvas.SetZoneConfiguration(ReadMouseZoneConfiguration(_profile));

        var maskDescriptions = BuildMaskDescriptions(_profile["masks"] as JsonArray);
        var sourceMappings = _profile["mappings"] as JsonArray;
        if (sourceMappings is not null)
        {
            for (var index = 0; index < sourceMappings.Count; index++)
            {
                if (sourceMappings[index] is not JsonObject mapping ||
                    mapping["condition"]?["mask"]?["id"] is not JsonValue maskIdNode ||
                    !maskIdNode.TryGetValue<int>(out var maskId) ||
                    mapping["macros"] is not JsonArray macros)
                    continue;

                var condition = mapping["condition"] as JsonObject;
                var mask = condition?["mask"] as JsonObject;
                var activator = mask?["activator"] as JsonObject;
                var type = activator?["type"]?.GetValue<string>() ?? "single";
                var mode = activator?["mode"]?.GetValue<string>() ?? "hold_until_release";
                var source = maskDescriptions.GetValueOrDefault(maskId, $"Mask {maskId}");
                var targetDescription = GetTargetDescription(mapping, macros, out var targetId);
                var label = mapping["description"]?.GetValue<string>();
                _mappings.Add(new MappingRow(
                    index,
                    mapping,
                    string.IsNullOrWhiteSpace(label) ? source : $"{source} · {label}",
                    targetDescription,
                    targetId,
                    $"Layer {condition?["shiftId"]?.ToString() ?? "1"}",
                    type,
                    mode));
            }
        }

        var mappingCount = sourceMappings?.Count ?? 0;
        MappingCountText.Text = $"{_mappings.Count} editable / {mappingCount} entries";
        var profileName = _profile["config"]?["appName"]?.GetValue<string>();
        ProfileInfoText.Text = $"{profileName ?? Path.GetFileNameWithoutExtension(_profilePath ?? "New profile")}\nSchema {_profile["schemaVersion"]?.ToString() ?? "unknown"}\n{mappingCount} profile entries";
        SaveProfileButton.IsEnabled = _isDirty;
        ApplyProfileButton.IsEnabled = _engineConnected && _profilePath is not null && !_isDirty;
        AddMappingButton.IsEnabled = GetGamepadInputDeviceId() is not null || GetHardwareDeviceId("keyboard") is not null;
        if (_profile is not null && AddMappingButton.IsEnabled == false)
            MappingHelpText.Text = "This profile has no gamepad or keyboard input device. Existing outputs can be edited; adding source controls is unavailable.";
        DirtyStateText.Text = _isDirty ? "Unsaved changes" : "Saved";
        DirtyStateText.Foreground = ThemeBrush(_isDirty ? "StatusWarningBrush" : "StatusSuccessBrush");

        if (_profile is not null && _profile["gyro"] is JsonObject gyroJson)
        {
            var gyroConfig = new GyroscopeSettings
            {
                MultiplierX = (float)(gyroJson["sensitivityX"]?.GetValue<double>() ?? 1.0),
                MultiplierY = (float)(gyroJson["sensitivityY"]?.GetValue<double>() ?? 1.0),
                Deadzone = (float)(gyroJson["deadzone"]?.GetValue<double>() ?? 0.5),
                Acceleration = (float)(gyroJson["acceleration"]?.GetValue<double>() ?? 1.0),
                InvertX = gyroJson["invertX"]?.GetValue<bool>() ?? false,
                InvertY = gyroJson["invertY"]?.GetValue<bool>() ?? false,
                SteeringRangeDegrees = (float)(gyroJson["steeringRange"]?.GetValue<double>() ?? 270.0),
                TiltThresholdLow = (float)(gyroJson["tiltThresholdLow"]?.GetValue<double>() ?? 15.0),
                TiltThresholdMed = (float)(gyroJson["tiltThresholdMed"]?.GetValue<double>() ?? 30.0),
                TiltThresholdHigh = (float)(gyroJson["tiltThresholdHigh"]?.GetValue<double>() ?? 45.0)
            };
            var modeStr = gyroJson["mode"]?.GetValue<string>()?.ToLowerInvariant();
            if (modeStr is not null)
            {
                gyroConfig.OperatingMode = modeStr switch
                {
                    "mouse" or "mouseaim" => GyroOperatingMode.MouseAim,
                    "right_stick" or "rightstick" => GyroOperatingMode.RightStick,
                    "left_stick" or "leftstick" => GyroOperatingMode.LeftStick,
                    "wheel" or "steeringwheel" => GyroOperatingMode.SteeringWheel,
                    "tilt" or "directionaltilt" => GyroOperatingMode.DirectionalTilt,
                    _ => GyroOperatingMode.MouseAim
                };
            }
            StageGyro.LoadSettings(gyroConfig);
        }

        _refreshingProfileView = false;
        PushMappingsToCanvas();
    }

    private void MouseCanvas_ZoneConfigurationChanged(MouseZoneConfig config)
    {
        if (_refreshingProfileView || _profile is null) return;
        var shifts = _profile["shifts"] as JsonArray;
        if (shifts is null)
        {
            shifts = new JsonArray();
            _profile["shifts"] = shifts;
        }
        var shift = shifts.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<int>() == 1);
        if (shift is null)
        {
            shift = new JsonObject { ["id"] = 1, ["type"] = "default" };
            shifts.Add(shift);
        }
        var mouse = shift["mouse"] as JsonObject;
        if (mouse is null) shift["mouse"] = mouse = new JsonObject();
        mouse["zones"] = new JsonArray
        {
            new JsonObject { ["name"] = "deadzone", ["radius"] = (int)Math.Round(config.DeadzoneRadius * 32767) },
            new JsonObject { ["name"] = "low", ["radius"] = (int)Math.Round(config.LowRadius * 32767), ["multiplier"] = config.LowMultiplier },
            new JsonObject { ["name"] = "med", ["radius"] = (int)Math.Round(config.MediumRadius * 32767), ["multiplier"] = config.MediumMultiplier },
            new JsonObject { ["name"] = "high", ["radius"] = (int)Math.Round(config.HighRadius * 32767), ["multiplier"] = config.HighMultiplier, ["action"] = config.EdgeAction }
        };
        MarkDirty();
    }

    private static MouseZoneConfig ReadMouseZoneConfiguration(JsonObject profile)
    {
        var zones = profile["shifts"]?.AsArray().OfType<JsonObject>()
            .FirstOrDefault(item => item["id"]?.GetValue<int>() == 1)?["mouse"]?["zones"]?.AsArray();
        if (zones is null) return new MouseZoneConfig();
        JsonObject? Find(string name) => zones.OfType<JsonObject>().FirstOrDefault(zone =>
            string.Equals(zone["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));
        static float Radius(JsonObject? zone, float fallback) => (float)(zone?["radius"]?.GetValue<double>() is double value
            ? Math.Clamp(value / 32767d, 0d, 1d) : fallback);
        static float Multiplier(JsonObject? zone, float fallback) => (float)(zone?["multiplier"]?.GetValue<double>() ?? fallback);
        var deadzone = Radius(Find("deadzone"), 0.05f);
        var low = Radius(Find("low"), 0.30f);
        var medium = Radius(Find("med"), 0.70f);
        var high = Radius(Find("high"), 0.92f);
        var edgeAction = Find("high")?["action"]?.GetValue<string>();
        try
        {
            var config = new MouseZoneConfig(deadzone, low, medium, high,
                Multiplier(Find("low"), 0.5f), Multiplier(Find("med"), 1f), Multiplier(Find("high"), 2.2f), edgeAction);
            config.Validate();
            return config;
        }
        catch (ArgumentOutOfRangeException)
        {
            return new MouseZoneConfig();
        }
    }

    private void StageGyro_SettingsChanged(object? sender, GyroscopeSettings settings)
    {
        if (_profile is null) return;
        var gyroObj = new JsonObject
        {
            ["mode"] = settings.OperatingMode.ToString().ToLowerInvariant(),
            ["activator"] = settings.ActivatorMode.ToString().ToLowerInvariant(),
            ["activatorButton"] = settings.ActivatorButton,
            ["sensitivityX"] = settings.MultiplierX,
            ["sensitivityY"] = settings.MultiplierY,
            ["deadzone"] = settings.Deadzone,
            ["acceleration"] = settings.Acceleration,
            ["invertX"] = settings.InvertX,
            ["invertY"] = settings.InvertY,
            ["horizontalAxis"] = settings.HorizontalAxis.ToString().ToLowerInvariant(),
            ["steeringRange"] = settings.SteeringRangeDegrees,
            ["tiltThresholdLow"] = settings.TiltThresholdLow,
            ["tiltThresholdMed"] = settings.TiltThresholdMed,
            ["tiltThresholdHigh"] = settings.TiltThresholdHigh
        };
        _profile["gyro"] = gyroObj;
        _isDirty = true;
        DirtyStateText.Text = "Unsaved changes";
        DirtyStateText.Foreground = ThemeBrush("StatusWarningBrush");
        SaveProfileButton.IsEnabled = true;
    }

    private void PushMappingsToCanvas()
    {
        var canvasMappings = _mappings.Select(row =>
        {
            var shiftText = row.Shift.StartsWith("Layer ", StringComparison.Ordinal) ? row.Shift["Layer ".Length..] : "1";
            _ = int.TryParse(shiftText, out var shift);
            return (row.Source, row.Target, row.Activator, shift);
        });
        GamepadCanvas.SetMappings(canvasMappings);
        KeyboardCanvas.SetMappings(_mappings.Select(row => (row.Source, row.Target)));
        MouseCanvas.SetMappings(_mappings.Select(row => (row.Source, row.Target)));
    }

    private void TargetTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingProfileView || _profile is null || TargetTypeCombo.SelectedItem is not TargetControllerChoice target) return;
        _currentTarget = target.Value;

        // 1. Profile persistence (plan 11 §1.2)
        if (VirtualTargetCatalog.ApplyTarget(_profile, target.Value))
            MarkDirty();

        // 2. KeyPicker re-indexing for the new target (plan 11 §1.2)
        RefreshOutputTargets();

        // 3. Canvas badge labels switch to target nomenclature, chassis follows the target family
        GamepadCanvas.SetTargetNomenclature(target.Value);

        // The selected family owns the visible canvas: desktop uses the keyboard, gamepad families use their chassis.
        SetCanvasMode(target.Value == "kbm" ? "keyboard" : "controller");

        // 4. Live engine re-targeting while a remap session is active (plan 11 §1.2 step 4)
        if (_engineConnected && RemapToggle.IsChecked == true && _profilePath is not null)
        {
            _ = ApplyConfigToEngineAsync($"Engine re-targeted to {target.Name} while remapping is active.");
        }

        SetFooter($"Virtual target switched to {target.Name}. Output targets and canvas labels updated.");
    }

    /// <summary>Rebuilds the output target pickers for the currently selected virtual target.</summary>
    private void RefreshOutputTargets()
    {
        var parsed = VirtualTargetCatalog.Parse(_currentTarget) ?? VirtualGamepadType.Xbox360;
        _outputTargets = MappingChoiceProvider.GetOutputTargets(parsed);
        TargetButtonCombo.ItemsSource = _outputTargets;
        if (TargetButtonCombo.SelectedItem is MappingChoice selected &&
            !_outputTargets.Any(choice => choice.Id == selected.Id && choice.Category == selected.Category))
        {
            TargetButtonCombo.SelectedItem = null;
        }

        // Repopulate an open inspector so its target list reflects the new nomenclature.
        if (BindingFrame.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(BindingFrame.SelectedButtonLabel))
        {
            if (SelectedMapping is { } row)
            {
                BindingFrame.Populate(
                    BindingFrame.SelectedButtonLabel,
                    (row.Source, row.Target, row.Activator, row.Mode),
                    _outputTargets,
                    [.. Activators.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
                    [.. Modes.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
                    GetAvailableShiftLayers(),
                    GamepadCanvas.ActiveShiftId);
            }
            else
            {
                var match = _mappings.FirstOrDefault(row => row.Source.Contains(BindingFrame.SelectedButtonLabel, StringComparison.OrdinalIgnoreCase));
                BindingFrame.Populate(
                    BindingFrame.SelectedButtonLabel,
                    match is null ? null : (match.Source, match.Target, match.Activator, match.Mode),
                    _outputTargets,
                    [.. Activators.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
                    [.. Modes.Select(choice => new BindingFrameView.Choice(choice.Name, choice.Value))],
                    GetAvailableShiftLayers(),
                    GamepadCanvas.ActiveShiftId);
            }
        }
    }

    /// <summary>POSTs the current profile to the engine to apply a live target switch.</summary>
    private async Task ApplyConfigToEngineAsync(string successMessage)
    {
        if (!_engineConnected || _profilePath is null) return;
        if (_isDirty && !await SaveCurrentProfileAsync()) return;

        var deviceId = (DeviceSelectorCombo.SelectedItem as DiscoveredDeviceItem)?.Id;
        var payload = new
        {
            ConfigPath = _profilePath,
            Path = _profilePath,
            Slot = 0,
            ControllerId = string.IsNullOrWhiteSpace(deviceId) ? "all_gamepad" : deviceId
        };
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v2.6/ControllerService/ApplyConfig", payload);
            if (response.IsSuccessStatusCode)
            {
                StopProfileButton.IsEnabled = true;
                SetFooter(successMessage);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SetFooter($"Could not re-apply profile after target switch: {ex.Message}", isError: true);
        }
    }

    private static Dictionary<int, string> BuildMaskDescriptions(JsonArray? masks)
    {
        var descriptions = new Dictionary<int, string>();
        if (masks is null) return descriptions;
        foreach (var item in masks.OfType<JsonObject>())
        {
            if (item["id"] is not JsonValue idNode || !idNode.TryGetValue<int>(out var id)) continue;
            var parts = (item["set"] as JsonArray)?
                .OfType<JsonObject>()
                .Select(trigger => trigger["description"]?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray() ?? [];
            descriptions[id] = parts.Length == 0 ? $"Mask {id}" : string.Join(" + ", parts);
        }
        return descriptions;
    }

    private static JsonObject CreateTargetMacro(MappingChoice target)
    {
        if (target.Category == "Command")
        {
            return new JsonObject
            {
                ["command"] = new JsonObject
                {
                    ["id"] = target.Id,
                    ["description"] = target.Description
                }
            };
        }

        if (target.Category == "Keyboard")
        {
            return new JsonObject
            {
                ["keyboard"] = new JsonObject
                {
                    ["buttonId"] = target.Id,
                    ["description"] = target.Description,
                    ["action"] = "down"
                }
            };
        }

        if (target.Category == "Mouse")
        {
            if (target.Description.StartsWith("wheel", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonObject
                {
                    ["mouse"] = new JsonObject
                    {
                        ["wheel"] = target.Description.EndsWith("up", StringComparison.OrdinalIgnoreCase) ? "up" : "down"
                    }
                };
            }

            return new JsonObject
            {
                ["mouse"] = new JsonObject
                {
                    ["buttonId"] = target.Id,
                    ["description"] = target.Description
                }
            };
        }

        return new JsonObject
        {
            ["gamepad"] = new JsonObject
            {
                ["id"] = 1,
                ["buttonId"] = target.Id,
                ["description"] = target.Description
            }
        };
    }

    private string GetTargetDescription(JsonObject mapping, JsonArray macros, out int? targetId)
    {
        targetId = null;
        if (macros.Count == 1 && macros[0] is JsonObject step)
        {
            if (step["command"] is JsonObject cmd &&
                cmd["id"] is JsonValue cmdIdNode && cmdIdNode.TryGetValue<int>(out var cId))
            {
                targetId = cId;
                var described = cmd["description"]?.GetValue<string>();
                return OutputTargets.FirstOrDefault(b => b.Id == cId && b.Category == "Command")?.Name
                    ?? (KeyScanCodeTable.All.FirstOrDefault(e => e.Category == KeyScanCodeCategory.RewasdUserCommand && e.Index == cId) is { } entry ? $"Cmd: {entry.FriendlyName}" : null)
                    ?? described
                    ?? $"Command {cId}";
            }

            if (step["gamepad"] is JsonObject gamepad &&
                gamepad["buttonId"] is JsonValue idNode && idNode.TryGetValue<int>(out var id))
            {
                targetId = id;
                var described = gamepad["description"]?.GetValue<string>();
                return OutputTargets.FirstOrDefault(button => button.Id == id && (button.Category == "Gamepad" || button.Category == "Touchpad"))?.Name
                    ?? (KeyScanCodeTable.GetByIndex(id) is { } entry ? entry.FriendlyName : null)
                    ?? described
                    ?? $"Gamepad control {id}";
            }

            if (step["keyboard"] is JsonObject keyboard)
            {
                int kbId = keyboard["buttonId"]?.GetValue<int>() ?? keyboard["keyId"]?.GetValue<int>() ?? 0;
                targetId = kbId;
                var described = keyboard["description"]?.GetValue<string>();
                return OutputTargets.FirstOrDefault(b => b.Id == kbId && b.Category == "Keyboard")?.Name
                    ?? (KeyScanCodeTable.GetByIndex(kbId) is { } entry ? $"Key: {entry.FriendlyName}" : null)
                    ?? described
                    ?? $"Key: {kbId}";
            }

            if (step["mouse"] is JsonObject mouse)
            {
                if (mouse["wheel"] is JsonValue wheelVal)
                {
                    var wheelStr = wheelVal.GetValue<string>();
                    return $"Mouse: Wheel {wheelStr}";
                }
                int mId = mouse["buttonId"]?.GetValue<int>() ?? 0;
                targetId = mId;
                return OutputTargets.FirstOrDefault(b => b.Id == mId && b.Category == "Mouse")?.Name
                    ?? (KeyScanCodeTable.GetByIndex(mId) is { } entry ? $"Mouse: {entry.FriendlyName}" : null)
                    ?? $"Mouse button {mId}";
            }
        }

        var mappingName = mapping["description"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(mappingName)) return mappingName;
        if (macros.Count == 0) return "No output";
        var firstMacro = macros[0] as JsonObject;
        return firstMacro?.FirstOrDefault().Key is { } kind ? $"{kind} macro ({macros.Count} steps)" : "Custom mapping";
    }

    private void MappingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MappingsGrid.SelectedItem is not MappingRow row)
        {
            SelectedMapping = null;
            SelectedInputText.Text = "Select a mapping";
            UpdateMappingButton.IsEnabled = false;
            return;
        }

        SelectedMapping = row;
        SelectedInputText.Text = row.Source;
        TargetButtonCombo.SelectedItem = OutputTargets.FirstOrDefault(button => button.Id == row.TargetButtonId && (
            row.Target.StartsWith("Key", StringComparison.OrdinalIgnoreCase) ? button.Category == "Keyboard" :
            row.Target.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) ? button.Category == "Mouse" :
            row.Target.StartsWith("Cmd", StringComparison.OrdinalIgnoreCase) ? button.Category == "Command" :
            button.Category == "Gamepad"))
            ?? OutputTargets.FirstOrDefault(button => button.Name.Equals(row.Target, StringComparison.OrdinalIgnoreCase));
        ActivatorCombo.SelectedItem = Activators.FirstOrDefault(choice => choice.Value == row.Activator);
        ModeCombo.SelectedItem = Modes.FirstOrDefault(choice => choice.Value == row.Mode);

        var shiftText = row.Shift.StartsWith("Layer ", StringComparison.Ordinal) ? row.Shift["Layer ".Length..] : "1";
        if (int.TryParse(shiftText, out var rowShiftId))
            ShiftLayerCombo.SelectedValue = rowShiftId;
        else
            ShiftLayerCombo.SelectedValue = 1;

        var replaceMacros = row.TargetButtonId is null
            ? "Updating this row replaces its current macro sequence with one gamepad/keyboard/mouse output."
            : "Changes stay in memory until you save the profile.";
        MappingHelpText.Text = replaceMacros;
        UpdateMappingButton.IsEnabled = true;
    }

    private void UpdateMappingButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMapping is not { } row || _profile is null || TargetButtonCombo.SelectedItem is not MappingChoice target)
            return;

        var activatorChoice = ActivatorCombo.SelectedItem as Choice ?? Activators[0];
        var modeChoice = ModeCombo.SelectedItem as Choice ?? Modes[0];
        var selectedShiftId = (ShiftLayerCombo.SelectedItem as BindingFrameView.ShiftChoice)?.Id
            ?? (ShiftLayerCombo.SelectedValue is int sid ? sid : 1);
        row.Node["macros"] = new JsonArray(CreateTargetMacro(target));

        var cond = row.Node["condition"] as JsonObject;
        if (cond is null)
        {
            cond = new JsonObject();
            row.Node["condition"] = cond;
        }
        cond["shiftId"] = selectedShiftId;

        var mask = cond["mask"] as JsonObject;
        if (mask is not null)
        {
            var activator = mask["activator"] as JsonObject;
            if (activator is null)
            {
                activator = new JsonObject();
                mask["activator"] = activator;
            }
            activator["type"] = activatorChoice.Value;
            activator["mode"] = modeChoice.Value;
        }

        row.TargetButtonId = target.Id;
        row.Target = target.Name;
        row.Shift = $"Layer {selectedShiftId}";
        row.Activator = activatorChoice.Value;
        row.Mode = modeChoice.Value;
        MappingsGrid.Items.Refresh();
        PushMappingsToCanvas();
        MarkDirty();
        SetFooter($"Updated {row.Source} → {target.Name} (Layer {selectedShiftId}). Save the profile to write it to disk.");
    }

    private void AddMappingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null || InputControlCombo.SelectedItem is not MappingChoice source ||
            TargetButtonCombo.SelectedItem is not MappingChoice target || GetSourceDeviceIdFor(source) is not int sourceDeviceId)
        {
            SetFooter("Choose a source control and a target output before adding a mapping.", isError: true);
            return;
        }

        var activatorChoice = ActivatorCombo.SelectedItem as Choice ?? Activators[0];
        var modeChoice = ModeCombo.SelectedItem as Choice ?? Modes[0];
        var selectedShiftId = (ShiftLayerCombo.SelectedItem as BindingFrameView.ShiftChoice)?.Id
            ?? (ShiftLayerCombo.SelectedValue is int sid ? sid : 1);
        AddMapping(source, target, sourceDeviceId, activatorChoice, modeChoice, selectedShiftId);
    }

    private void AddMapping(MappingChoice source, MappingChoice target, int sourceDeviceId, Choice activatorChoice, Choice modeChoice,
        int shiftId = 1, MappingChoice? chordPartner = null, int? chordPartnerDeviceId = null)
    {
        if (_profile is null) return;
        var masks = _profile["masks"] as JsonArray;
        if (masks is null)
        {
            masks = new JsonArray();
            _profile["masks"] = masks;
        }
        var nextMaskId = masks.OfType<JsonObject>()
            .Select(mask => mask["id"] is JsonValue value && value.TryGetValue<int>(out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        masks.Add(new JsonObject
        {
            ["id"] = nextMaskId,
            ["set"] = CreateInputMaskSet(source, sourceDeviceId, chordPartner, chordPartnerDeviceId)
        });

        if (shiftId <= 0) shiftId = 1;
        var mapping = new JsonObject
        {
            ["description"] = chordPartner is null
                ? $"{source.Name} → {target.Name}"
                : $"{source.Name} + {chordPartner.Name} → {target.Name}",
            ["condition"] = new JsonObject
            {
                ["shiftId"] = shiftId,
                ["mask"] = new JsonObject
                {
                    ["id"] = nextMaskId,
                    ["activator"] = new JsonObject
                    {
                        ["type"] = activatorChoice.Value,
                        ["mode"] = modeChoice.Value
                    }
                }
            },
            ["macros"] = new JsonArray(CreateTargetMacro(target))
        };
        var profileMappings = _profile["mappings"] as JsonArray;
        if (profileMappings is null)
        {
            profileMappings = new JsonArray();
            _profile["mappings"] = profileMappings;
        }
        var mappingIndex = profileMappings.Count;
        profileMappings.Add(mapping);
        var sourceDescription = chordPartner is null ? source.Name : $"{source.Name} + {chordPartner.Name}";
        var row = new MappingRow(mappingIndex, mapping, sourceDescription, target.Name, target.Id, $"Layer {shiftId}", activatorChoice.Value, modeChoice.Value);
        _mappings.Add(row);
        MappingsGrid.SelectedItem = row;
        MappingCountText.Text = $"{_mappings.Count} editable / {profileMappings.Count} entries";
        PushMappingsToCanvas();
        MarkDirty();
        SetFooter($"Added {sourceDescription} → {target.Name} on Layer {shiftId}. Save the profile to write it to disk.");
    }

    private static JsonArray CreateInputMaskSet(MappingChoice source, int sourceDeviceId, MappingChoice? partner, int? partnerDeviceId)
    {
        var set = new JsonArray
        {
            new JsonObject { ["deviceId"] = sourceDeviceId, ["buttonId"] = source.Id, ["description"] = source.Name }
        };
        if (partner is not null && partnerDeviceId is int deviceId)
            set.Add(new JsonObject { ["deviceId"] = deviceId, ["buttonId"] = partner.Id, ["description"] = partner.Name });
        return set;
    }

    private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return;
        _isLoading = true;
        ProfileComboBox.SelectedItem = null;
        _selectedProfileFile = null;
        _isLoading = false;
        _profilePath = null;
        _profile = new JsonObject
        {
            ["schemaVersion"] = 3.0,
            ["appVersion"] = "Shunt",
            ["config"] = new JsonObject { ["appName"] = "New Profile", ["minTimeUnit"] = 1 },
            ["devices"] = new JsonObject
            {
                ["hardware"] = new JsonArray(new JsonObject { ["id"] = 1, ["name"] = "gamepad", ["type"] = "standard" }),
                ["virtual"] = new JsonObject { ["gamepads"] = new JsonArray(new JsonObject { ["id"] = 1, ["type"] = "360" }) }
            },
            ["shifts"] = new JsonArray(new JsonObject { ["id"] = 1, ["type"] = "default", ["description"] = "Main" }),
            ["masks"] = new JsonArray(),
            ["mappings"] = new JsonArray()
        };
        _mappings.Clear();
        _isDirty = true;
        RefreshProfileView();
        SetFooter("Created a new unsaved profile. Use Save As to choose its file name.");
    }

    private async void OpenProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return;
        var dialog = new OpenFileDialog { Filter = "Shunt profiles (*.rewasd)|*.rewasd|JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            _isLoading = true;
            ProfileComboBox.SelectedItem = null;
            _selectedProfileFile = null;
            _isLoading = false;
            await LoadProfileAsync(dialog.FileName);
        }
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e) => await SaveCurrentProfileAsync();

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) => await SaveAsCurrentProfileAsync();

    private async Task<bool> SaveAsCurrentProfileAsync()
    {
        if (_profile is null) return false;
        var dialog = new SaveFileDialog
        {
            Filter = "Shunt profiles (*.rewasd)|*.rewasd|JSON files (*.json)|*.json",
            DefaultExt = ".rewasd",
            FileName = Path.GetFileName(_profilePath ?? "New Profile.rewasd")
        };
        if (dialog.ShowDialog(this) != true) return false;
        _profilePath = dialog.FileName;
        if (!await SaveCurrentProfileAsync()) return false;
        AddProfileToList(_profilePath);
        return true;
    }

    private async Task<bool> SaveCurrentProfileAsync()
    {
        if (_profile is null) return false;
        if (string.IsNullOrWhiteSpace(_profilePath))
            return await SaveAsCurrentProfileAsync();

        var temporaryPath = _profilePath + ".tmp";
        try
        {
            if (File.Exists(_profilePath)) File.Copy(_profilePath, _profilePath + ".bak", overwrite: true);
            await File.WriteAllTextAsync(temporaryPath, _profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _profilePath, overwrite: true);
            _isDirty = false;
            SaveProfileButton.IsEnabled = false;
            DirtyStateText.Text = "Saved";
            DirtyStateText.Foreground = ThemeBrush("StatusSuccessBrush");
            ApplyProfileButton.IsEnabled = _engineConnected;
            SetFooter($"Saved {Path.GetFileName(_profilePath)}. A .bak backup was kept when replacing a file.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
            SetFooter($"Could not save profile: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.Message, "Profile could not be saved", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void AddProfileToList(string path)
    {
        var existing = _profiles.FirstOrDefault(profile => string.Equals(profile.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _selectedProfileFile = existing;
            ProfileComboBox.SelectedItem = existing;
            return;
        }
        var added = new ProfileFile(Path.GetFileName(path), path);
        _profiles.Add(added);
        _isLoading = true;
        ProfileComboBox.SelectedItem = added;
        _isLoading = false;
        _selectedProfileFile = added;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        SaveProfileButton.IsEnabled = true;
        DirtyStateText.Text = "Unsaved changes";
        DirtyStateText.Foreground = ThemeBrush("StatusWarningBrush");
        ApplyProfileButton.IsEnabled = _engineConnected && _profilePath is not null;
    }

    private async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (!_isDirty) return true;
        var result = MessageBox.Show(this, "Save the changes to this profile before continuing?", "Unsaved profile changes",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.No) return true;
        return await SaveCurrentProfileAsync();
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e) => await RefreshConnectionAsync();

    private static string? TryFindCliExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Shunt.CLI.exe"),
            Path.Combine(AppContext.BaseDirectory, "Shunt.CLI"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\net8.0\Shunt.CLI.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Shunt.CLI\bin\Debug\net8.0\Shunt.CLI.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Shunt.CLI\bin\Release\net8.0\win-x64\Shunt.CLI.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\publish\ui\Shunt.CLI.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\net9.0\Shunt.CLI.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Shunt.CLI\bin\Debug\net9.0\Shunt.CLI.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool TryStartEngineService()
    {
        var cli = TryFindCliExecutable();
        if (cli is null) return false;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(cli, "serve")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RefreshConnectionAsync()
    {
        try
        {
            await AttemptConnectAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (TryStartEngineService())
            {
                await Task.Delay(1000);
                try
                {
                    await AttemptConnectAsync();
                    return;
                }
                catch { }
            }

            _engineConnected = false;
            EngineStatusText.Text = "Engine offline";
            EngineStatusDot.Fill = ThemeBrush("StatusErrorBrush");
            DriverStatusText.Text = "Driver status unavailable until the engine connects.";
            ApplyProfileButton.IsEnabled = false;
            StopProfileButton.IsEnabled = false;
            SetFooter("Start the Shunt engine service with `Shunt.CLI.exe serve`, then choose Reconnect.", isError: true);
        }
        finally
        {
            await RefreshDevicesAsync();
        }
    }

    private async Task AttemptConnectAsync()
    {
        using var versionResponse = await _httpClient.GetAsync("Version");
        versionResponse.EnsureSuccessStatusCode();
        using var versionJson = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync());
        var version = versionJson.RootElement.TryGetProperty("Version", out var versionValue) ? versionValue.GetString() : "connected";
        var isLocalEngine = versionJson.RootElement.TryGetProperty("Engine", out var eng) && string.Equals(eng.GetString(), "Shunt", StringComparison.OrdinalIgnoreCase);

        // The Version endpoint is the connection check. Driver and mapping state are
        // separate capabilities and must not turn a reachable engine into "offline".
        _engineConnected = true;
        EnsureEngineEventClient();
        EngineStatusText.Text = isLocalEngine ? $"Engine online · Shunt {version}" : $"Engine online · {version}";
        EngineStatusDot.Fill = ThemeBrush("StatusSuccessGlowBrush");
        ApplyProfileButton.IsEnabled = _profilePath is not null;

        try
        {
            using var driverResponse = await _httpClient.GetAsync("v2.6/EngineService/DriverInfo");
            driverResponse.EnsureSuccessStatusCode();
            using var driverJson = JsonDocument.Parse(await driverResponse.Content.ReadAsStringAsync());
            var installed = driverJson.RootElement.TryGetProperty("IsViGEmBusInstalled", out var driver) && driver.GetBoolean();
            DriverStatusText.Text = installed ? "ViGEmBus detected" : "ViGEmBus not detected · simulation may be active";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DriverStatusText.Text = "Driver status unavailable · engine is online";
        }

        try
        {
            using var stateResponse = await _httpClient.GetAsync("v2.6/ControllerService/RemapState/ui");
            if (stateResponse.IsSuccessStatusCode)
            {
                using var stateJson = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
                var running = stateJson.RootElement.TryGetProperty("IsRemaped", out var active) && active.GetBoolean();
                StopProfileButton.IsEnabled = running;
                EngineStatusText.Text += running ? " · profile active" : " · no profile active";
            }
            else
            {
                StopProfileButton.IsEnabled = false;
                EngineStatusText.Text += " · profile status unavailable";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            StopProfileButton.IsEnabled = false;
            EngineStatusText.Text += " · profile status unavailable";
        }
        SetFooter(isLocalEngine ? "Connected to Shunt engine." : "Connected to local engine.");
    }

    private async void ApplyProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_engineConnected || _profilePath is null) return;
        if (_isDirty)
        {
            var saved = await SaveCurrentProfileAsync();
            if (!saved) return;
        }

        var deviceId = (DeviceSelectorCombo.SelectedItem as DiscoveredDeviceItem)?.Id;
        var slot = 0;
        var payload = new
        {
            ConfigPath = _profilePath,
            Path = _profilePath,
            Slot = slot,
            ControllerId = string.IsNullOrWhiteSpace(deviceId) ? "all_gamepad" : deviceId
        };

        try
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.PostAsJsonAsync("v2.6/ControllerService/ApplyConfig", payload);
            }
            catch (HttpRequestException) { }

            if (response == null || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response?.Dispose();
                response = await _httpClient.PostAsJsonAsync("api/engine/apply", new { ConfigPath = _profilePath, Slot = slot });
            }

            if (!response.IsSuccessStatusCode)
            {
                var cliPath = @"C:\Program Files\reWASD\reWASDCommandLine.exe";
                if (File.Exists(cliPath))
                {
                    var idArg = string.IsNullOrWhiteSpace(deviceId) ? "all_gamepad" : deviceId;
                    var psi = new System.Diagnostics.ProcessStartInfo(cliPath, $"apply --id {idArg} --path \"{_profilePath}\" --slot slot{slot + 1}")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode == 0)
                        {
                            StopProfileButton.IsEnabled = true;
                            EngineStatusText.Text = "Engine online · profile accepted";
                            SetFooter($"Applied profile via reWASD CLI: {Path.GetFileName(_profilePath)}");
                            response?.Dispose();
                            return;
                        }
                    }
                }
            }

            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            StopProfileButton.IsEnabled = true;
            EngineStatusText.Text = "Engine online · profile accepted";
            SetFooter($"Engine accepted {Path.GetFileName(_profilePath)}. {body}");
            response.Dispose();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SetFooter($"Could not apply profile: {ex.Message}", isError: true);
        }
    }

    private async void StopProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var deviceId = (DeviceSelectorCombo.SelectedItem as DiscoveredDeviceItem)?.Id;
        try
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.PostAsJsonAsync("v2.6/ControllerService/DisableRemap", new { ControllerId = string.IsNullOrWhiteSpace(deviceId) ? "all" : deviceId });
            }
            catch (HttpRequestException) { }

            if (response == null || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response?.Dispose();
                response = await _httpClient.PostAsJsonAsync("api/engine/stop", new { });
            }

            if (!response.IsSuccessStatusCode)
            {
                var cliPath = @"C:\Program Files\reWASD\reWASDCommandLine.exe";
                if (File.Exists(cliPath))
                {
                    var idArg = string.IsNullOrWhiteSpace(deviceId) ? "all" : deviceId;
                    var psi = new System.Diagnostics.ProcessStartInfo(cliPath, $"remap --id {idArg} --state off")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode == 0)
                        {
                            StopProfileButton.IsEnabled = false;
                            EngineStatusText.Text = "Engine online · remapping stopped";
                            SetFooter("Remapping stopped via reWASD CLI.");
                            response?.Dispose();
                            return;
                        }
                    }
                }
            }

            response.EnsureSuccessStatusCode();
            StopProfileButton.IsEnabled = false;
            EngineStatusText.Text = "Engine online · remapping stopped";
            SetFooter("Remapping stopped.");
            response.Dispose();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SetFooter($"Could not stop remapping: {ex.Message}", isError: true);
        }
    }

    private void CreateNewProfile()
    {
        _profilePath = null;
        _profile = null;
        _isDirty = false;
        RefreshProfileView();
    }

    private int? GetGamepadInputDeviceId() => GetHardwareDeviceId("gamepad");

    private int? GetHardwareDeviceId(string deviceName)
    {
        if (_profile?["devices"]?["hardware"] is not JsonArray devices) return null;
        foreach (var device in devices.OfType<JsonObject>())
        {
            if (!string.Equals(device["name"]?.GetValue<string>(), deviceName, StringComparison.OrdinalIgnoreCase)) continue;
            if (device["id"] is JsonValue id && id.TryGetValue<int>(out var deviceId)) return deviceId;
        }
        return null;
    }

    private void SetFooter(string text, bool isError = false)
    {
        FooterStatusText.Text = text;
        FooterStatusText.Foreground = ThemeBrush(isError ? "StatusErrorBrush" : "TextMutedBrush");
    }

    private static SolidColorBrush ThemeBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
            return brush;
        return Brushes.Gray;
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = new List<DiscoveredDeviceItem>();

        if (_engineConnected)
        {
            try
            {
                HttpResponseMessage? resp = null;
                try
                {
                    resp = await _httpClient.GetAsync("v2.6/ControllerService/Controllers");
                }
                catch (HttpRequestException) { }

                if (resp == null || !resp.IsSuccessStatusCode)
                {
                    resp?.Dispose();
                    resp = await _httpClient.GetAsync("api/controllers");
                }

                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync();
                    using var jsonDoc = await JsonDocument.ParseAsync(stream);
                    if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in jsonDoc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("Id", out var idProp))
                            {
                                var id = idProp.GetString() ?? "";
                                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? "Controller" : "Controller";
                                var type = item.TryGetProperty("Type", out var typeProp) ? typeProp.GetString() ?? "Gamepad" : "Gamepad";
                                var conn = item.TryGetProperty("ConnectionType", out var connProp) ? connProp.GetString() ?? "USB" : "USB";
                                var batt = item.TryGetProperty("BatteryLevel", out var battProp) ? battProp.GetInt32() : -1;
                                var isCharging = item.TryGetProperty("IsCharging", out var chProp) && chProp.GetBoolean();
                                var connected = item.TryGetProperty("Connected", out var connBool) && connBool.GetBoolean();
                                bool isPhysical = !string.Equals(type, "Virtual", StringComparison.OrdinalIgnoreCase);

                                devices.Add(new DiscoveredDeviceItem(id, name, type, conn, batt, isCharging, connected, isPhysical));
                            }
                            else if (item.TryGetProperty("DisplayName", out var dispProp))
                            {
                                var name = dispProp.GetString() ?? "Controller";
                                var id = "";
                                if (item.TryGetProperty("Ids", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array && idsProp.GetArrayLength() > 0)
                                {
                                    id = idsProp[0].ToString();
                                }
                                else if (item.TryGetProperty("Guid", out var guidProp))
                                {
                                    id = guidProp.GetString() ?? "";
                                }
                                var family = item.TryGetProperty("Family", out var famProp) ? famProp.GetString() ?? "Gamepad" : "Gamepad";
                                var conn = item.TryGetProperty("ConnectionType", out var connProp) ? connProp.GetString() ?? "USB" : "USB";
                                devices.Add(new DiscoveredDeviceItem(id, name, family, conn, -1, false, true, true));
                            }
                        }
                    }
                }
                resp.Dispose();
            }
            catch
            {
                // Engine controller query failed, fallback to PnP
            }
        }

        if (devices.Count == 0)
        {
            try
            {
                var pnpList = Shunt.Engine.Hardware.PnpDeviceEnumerator.EnumerateControllers();
                foreach (var pnp in pnpList)
                {
                    devices.Add(new DiscoveredDeviceItem(
                        pnp.InstanceId,
                        pnp.Name,
                        pnp.HardwareType,
                        pnp.ConnectionType,
                        -1,
                        false,
                        pnp.IsConnected,
                        true));
                }
            }
            catch
            {
                // Ignore enumeration errors
            }
        }

        // Compare if devices changed to avoid unnecessary combo resets
        bool listChanged = devices.Count != _discoveredDevices.Count ||
            !devices.Select(d => d.Id).SequenceEqual(_discoveredDevices.Select(d => d.Id));

        if (listChanged)
        {
            var previousSelectedId = (DeviceSelectorCombo.SelectedItem as DiscoveredDeviceItem)?.Id;
            _discoveredDevices.Clear();
            foreach (var d in devices)
            {
                _discoveredDevices.Add(d);
            }

            var physicalList = _discoveredDevices.Where(d => d.IsPhysical).ToList();
            if (physicalList.Count > 0)
            {
                NoDevicesPanel.Visibility = Visibility.Collapsed;
                ConnectedDevicePanel.Visibility = Visibility.Visible;

                var restore = physicalList.FirstOrDefault(d => string.Equals(d.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase)) ?? physicalList[0];
                DeviceSelectorCombo.SelectedItem = restore;
                DeviceTypeText.Text = $"{restore.Type} • {restore.ConnectionType}";
            }
            else
            {
                NoDevicesPanel.Visibility = Visibility.Visible;
                ConnectedDevicePanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void DeviceSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceSelectorCombo.SelectedItem is not DiscoveredDeviceItem device) return;
        DeviceTypeText.Text = $"{device.Type} • {device.ConnectionType}";
        if (_engineConnected)
        {
            try
            {
                await _httpClient.PostAsync($"api/controllers/{Uri.EscapeDataString(device.Id)}/select", null);
            }
            catch
            {
                // Ignore failure
            }
        }
        SetFooter($"Active controller: {device.Name} ({device.ConnectionType}).");
    }

    private async void IdentifyDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceSelectorCombo.SelectedItem is not DiscoveredDeviceItem device) return;
        SetFooter($"Sending haptic rumble pulse to {device.Name}…");

        bool sentViaEngine = false;
        if (_engineConnected)
        {
            try
            {
                using var response = await _httpClient.PostAsync($"api/controllers/{Uri.EscapeDataString(device.Id)}/identify", null);
                if (response.IsSuccessStatusCode)
                {
                    sentViaEngine = true;
                }
            }
            catch
            {
                sentViaEngine = false;
            }
        }

        if (!sentViaEngine)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    for (uint i = 0; i < 4; i++)
                    {
                        Shunt.Engine.Hardware.XInputNative.SetVibration(i, 65535, 65535);
                    }
                    Thread.Sleep(300);
                    for (uint i = 0; i < 4; i++)
                    {
                        Shunt.Engine.Hardware.XInputNative.SetVibration(i, 0, 0);
                    }
                }
                catch { /* ignore */ }
            });
        }

        SetFooter($"Identified {device.Name} via motor vibration pulse.");
    }

    private void GroupDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        var physicalDevices = _discoveredDevices.Where(d => d.IsPhysical).ToList();
        if (physicalDevices.Count == 0)
        {
            MessageBox.Show(this, "No physical devices detected to group. Connect your controllers first.", "Device Grouping", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.Dialogs.GroupDevicesWindow(physicalDevices) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var count = dialog.SelectedEntries.Count;
            SetFooter($"Group '{dialog.GroupName}' configured with {count} device(s). Composite aggregation active.");
        }
    }

    private void DetectionModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDetectionModeActive = !_isDetectionModeActive;

        if (_isDetectionModeActive)
        {
            DetectionModeButton.Content = "🎯 Listening…";
            DetectionModeButton.BorderBrush = ThemeBrush("Shift1Brush");

            if (_engineConnected)
            {
                EnsureEngineEventClient();
                SetFooter("Detection mode active (SSE): Press any button on your physical controller to auto-focus and map it.");
            }
            else
            {
                SetFooter("Detection mode active (Offline XInput): Press any button on your physical controller to auto-focus and map it.");

                _detectionCts = new CancellationTokenSource();
                var token = _detectionCts.Token;

                _ = Task.Run(async () =>
                {
                    ushort lastButtons = 0;
                    byte lastLt = 0, lastRt = 0;
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            for (uint slot = 0; slot < 4; slot++)
                            {
                                var state = new Shunt.Engine.Hardware.XInputNative.XINPUT_STATE();
                                if (Shunt.Engine.Hardware.XInputNative.GetState(slot, ref state) == Shunt.Engine.Hardware.XInputNative.ERROR_SUCCESS)
                                {
                                    var wButtons = state.Gamepad.wButtons;
                                    var newlyPressed = (ushort)(wButtons & ~lastButtons);
                                    lastButtons = wButtons;

                                    string? detected = null;
                                    if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_A) != 0) detected = "A";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_B) != 0) detected = "B";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_X) != 0) detected = "X";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_Y) != 0) detected = "Y";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) detected = "LB";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) detected = "RB";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_LEFT_THUMB) != 0) detected = "LS";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_RIGHT_THUMB) != 0) detected = "RS";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_START) != 0) detected = "Menu";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_BACK) != 0) detected = "View";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_GUIDE) != 0) detected = "Guide";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_UP) != 0) detected = "DUp";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_DOWN) != 0) detected = "DDown";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_LEFT) != 0) detected = "DLeft";
                                    else if ((newlyPressed & Shunt.Engine.Hardware.XInputNative.XINPUT_GAMEPAD_DPAD_RIGHT) != 0) detected = "DRight";

                                    if (detected == null && state.Gamepad.bLeftTrigger > 128 && lastLt <= 128) detected = "LT";
                                    if (detected == null && state.Gamepad.bRightTrigger > 128 && lastRt <= 128) detected = "RT";
                                    lastLt = state.Gamepad.bLeftTrigger;
                                    lastRt = state.Gamepad.bRightTrigger;

                                    if (detected != null)
                                    {
                                        var captured = detected;
                                        Dispatcher.Invoke(() =>
                                        {
                                            var anchor = CanvasButtonResolver.ResolveAnchor(captured) ?? captured;
                                            GamepadCanvas.PulseGlow(anchor);
                                            GamepadCanvas.SelectButton(anchor);
                                            GamepadCanvas_BindingRequested(this, anchor);
                                            SetFooter($"Detection mode (Offline): Detected physical [{anchor}] press. Inspector opened.");
                                        });
                                    }
                                }
                            }

                            await Task.Delay(20, token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch { await Task.Delay(50, token); }
                    }
                }, token);
            }
        }
        else
        {
            _detectionCts?.Cancel();
            _detectionCts = null;
            DetectionModeButton.Content = "Detection mode";
            DetectionModeButton.ClearValue(BorderBrushProperty);
            SetFooter("Detection mode deactivated.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _deviceRefreshTimer?.Stop();
        _detectionCts?.Cancel();
        _eventClient?.Dispose();
        _httpClient.Dispose();
        base.OnClosed(e);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_isDirty) return;
        e.Cancel = true;
        if (!await ConfirmDiscardOrSaveAsync()) return;
        _allowClose = true;
        Close();
    }

    public sealed record DiscoveredDeviceItem(
        string Id,
        string Name,
        string Type,
        string ConnectionType,
        int BatteryLevel,
        bool IsCharging,
        bool Connected,
        bool IsPhysical)
    {
        public override string ToString() => Name;
    }

    private sealed record ProfileFile(string Name, string Path)
    {
        public string Initials
        {
            get
            {
                var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (words.Length == 0) return "?";
                return words.Length == 1
                    ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
                    : $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
            }
        }
    }
    private sealed record Choice(string Name, string Value);

    public sealed class MappingRow(int index, JsonObject node, string source, string target, int? targetButtonId, string shift, string activator, string mode)
    {
        public int Index { get; } = index;
        public JsonObject Node { get; } = node;
        public string Source { get; } = source;
        public string Target { get; set; } = target;
        public int? TargetButtonId { get; set; } = targetButtonId;
        public string Shift { get; set; } = shift;
        public string Activator { get; set; } = activator;
        public string Mode { get; set; } = mode;
    }
}
