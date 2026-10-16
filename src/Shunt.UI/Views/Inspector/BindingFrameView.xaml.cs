using System.Windows;
using System.Windows.Controls;
using System.Collections;
using Shunt.Core.Scancodes;

namespace Shunt.UI.Views.Inspector;

public partial class BindingFrameView : UserControl
{
    public event EventHandler? ApplyRequested;
    public event EventHandler? OpenInListRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<int>? LayerSelectionChanged;
    public event Action<MappingChoice>? ChordMappingRequested;

    private bool _updatingSelection;

    public BindingFrameView()
    {
        InitializeComponent();
    }

    public string SelectedButtonLabel => SelectedButtonTitle.Text;

    public void SetChordPartners(IEnumerable partners) => ChordPartnerCombo.ItemsSource = partners;

    /// <summary>Choice option carrying a display name and an optional value string.</summary>
    public sealed record Choice(string Name, string? Value = null);

    /// <summary>Shift layer option carrying a display name and numeric shift ID.</summary>
    public sealed record ShiftChoice(string Name, int Id);

    public (object? Target, string Activator, string Mode, int ShiftId) ReadSelection() =>
        (TargetCombo.SelectedItem,
         (ActivatorCombo.SelectedItem as Choice)?.Value ?? "single",
         (ModeCombo.SelectedItem as Choice)?.Value ?? "hold_until_release",
         (ShiftLayerCombo.SelectedItem as ShiftChoice)?.Id ?? (ShiftLayerCombo.SelectedValue is int sid ? sid : 1));

    public void Populate(
        string buttonLabel,
        (string Source, string Target, string Activator, string Mode)? currentMapping,
        System.Collections.IEnumerable targets,
        IReadOnlyList<Choice> activators,
        IReadOnlyList<Choice> modes,
        IReadOnlyList<ShiftChoice> layers,
        int activeShiftId)
    {
        _updatingSelection = true;
        try
        {
            SelectedButtonTitle.Text = buttonLabel;

            if (currentMapping is { } mapping)
            {
                CurrentMappingText.Text = $"{mapping.Source}  →  {mapping.Target}";
                CurrentActivatorText.Text = $"Activator: {mapping.Activator} · Mode: {mapping.Mode} · Layer: {activeShiftId}";
            }
            else
            {
                CurrentMappingText.Text = "No mapping for this control yet.";
                CurrentActivatorText.Text = $"Choose a target below, then Update mapping to add to Layer {activeShiftId}.";
            }

            TargetCombo.ItemsSource = targets;
            ActivatorCombo.ItemsSource = activators;
            ModeCombo.ItemsSource = modes;
            ShiftLayerCombo.ItemsSource = layers;

            TargetCombo.SelectedItem = null;
            ActivatorCombo.SelectedItem = currentMapping is { } m
                ? activators.FirstOrDefault(choice => choice.Value == m.Activator)
                : activators.FirstOrDefault();
            ModeCombo.SelectedItem = currentMapping is { } m2
                ? modes.FirstOrDefault(choice => choice.Value == m2.Mode)
                : modes.FirstOrDefault();
            ShiftLayerCombo.SelectedValue = activeShiftId;

            // Targets are plain names or MappingChoice; preselect current target text when present.
            if (currentMapping is { } m3)
            {
                foreach (var item in targets)
                {
                    if (item is Shunt.Core.Scancodes.MappingChoice mc && mc.Name == m3.Target)
                    {
                        TargetCombo.SelectedItem = mc;
                        break;
                    }
                    if (item is Choice c && c.Name == m3.Target)
                    {
                        TargetCombo.SelectedItem = c;
                        break;
                    }
                }
            }
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void ShiftLayerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection) return;
        if (ShiftLayerCombo.SelectedItem is ShiftChoice sc)
        {
            LayerSelectionChanged?.Invoke(this, sc.Id);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => ApplyRequested?.Invoke(this, e);

    private void AddChordButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChordPartnerCombo.SelectedItem is MappingChoice partner)
            ChordMappingRequested?.Invoke(partner);
    }

    private void OpenInListButton_Click(object sender, RoutedEventArgs e) => OpenInListRequested?.Invoke(this, e);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
}
