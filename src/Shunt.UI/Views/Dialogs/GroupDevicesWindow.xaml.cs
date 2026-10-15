using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Shunt.UI.Views.Dialogs;

public partial class GroupDevicesWindow : Window
{
    public sealed class GroupDeviceEntry : INotifyPropertyChanged
    {
        private bool _isIncluded;

        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public string ConnectionType { get; }

        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded != value)
                {
                    _isIncluded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public GroupDeviceEntry(string id, string name, string type, string connectionType, bool isIncluded = true)
        {
            Id = id;
            Name = name;
            Type = type;
            ConnectionType = connectionType;
            _isIncluded = isIncluded;
        }
    }

    public ObservableCollection<GroupDeviceEntry> GroupEntries { get; } = [];
    public string GroupName => GroupNameBox.Text.Trim();
    public IReadOnlyList<GroupDeviceEntry> SelectedEntries => GroupEntries.Where(e => e.IsIncluded).ToList();

    public GroupDevicesWindow(IEnumerable<MainWindow.DiscoveredDeviceItem> availableDevices)
    {
        InitializeComponent();
        foreach (var dev in availableDevices)
        {
            GroupEntries.Add(new GroupDeviceEntry(dev.Id, dev.Name, dev.Type, dev.ConnectionType, isIncluded: true));
        }
        DevicesListBox.ItemsSource = GroupEntries;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (SelectedEntries.Count == 0)
        {
            MessageBox.Show(this, "Please select at least one physical device to form a group.", "No Devices Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
