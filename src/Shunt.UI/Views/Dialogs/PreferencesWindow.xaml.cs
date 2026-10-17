using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Shunt.Engine;
using Shunt.UI.Services;

namespace Shunt.UI.Views.Dialogs;

public partial class PreferencesWindow : Window
{
    private readonly ObservableCollection<string> _whitelistApps = [];
    private readonly HidHideService _hidHide = new();

    public PreferencesWindow()
    {
        InitializeComponent();
        WhitelistListBox.ItemsSource = _whitelistApps;
        Loaded += (_, _) => LoadSettings();
    }

    private void LoadSettings()
    {
        // Load Whitelist apps
        var currentPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(currentPath))
        {
            _whitelistApps.Add(currentPath);
        }

        // Check HidHide driver
        if (_hidHide.IsAvailable)
        {
            HidHideStatusText.Text = $"Status: ACTIVE (v{_hidHide.DriverVersion})";
            HidHideStatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusSuccessBrush");
        }
        else
        {
            HidHideStatusText.Text = "Status: NOT INSTALLED (Fallback In-Memory Stub)";
            HidHideStatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusWarningBrush");
        }

        // General tab toggles backed by persisted settings
        NotificationsToggle.IsChecked = AppSettings.Current.ControllerNotificationsEnabled;
        TrayToggle.IsChecked = AppSettings.Current.MinimizeToTray;

        // Appearance tab theme combo selection
        ThemeCombo.SelectedIndex = App.CurrentTheme == App.AimXPTheme ? 1 : 0;
    }

    private void OnAddWhitelistApp(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select Application to Whitelist",
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            if (!_whitelistApps.Contains(ofd.FileName))
            {
                _whitelistApps.Add(ofd.FileName);
                _hidHide.AddApplicationWhitelist(ofd.FileName);
            }
        }
    }

    private void OnRemoveWhitelistApp(object sender, RoutedEventArgs e)
    {
        if (WhitelistListBox.SelectedItem is string selected)
        {
            _whitelistApps.Remove(selected);
            _hidHide.RemoveApplicationWhitelist(selected);
        }
    }

    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeCombo == null || !IsLoaded) return;
        bool aimXp = ThemeCombo.SelectedIndex == 1;
        App.ApplyTheme(aimXp ? App.AimXPTheme : App.BaseTemplateTheme);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ControllerNotificationsEnabled = NotificationsToggle.IsChecked == true;
        AppSettings.Current.MinimizeToTray = TrayToggle.IsChecked == true;
        AppSettings.Current.Theme = ThemeCombo.SelectedIndex == 1 ? App.AimXPTheme : App.BaseTemplateTheme;
        AppSettings.Current.Save();
        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
