using System.Windows;

namespace Shunt.UI;

public partial class RadialMenuOverlay : Window
{
    public RadialMenuOverlay()
    {
        InitializeComponent();
    }

    public void SetActiveSlot(int slotIndex, string label)
    {
        SelectedSlotText.Text = $"Slot {slotIndex}: {label}";
    }
}
