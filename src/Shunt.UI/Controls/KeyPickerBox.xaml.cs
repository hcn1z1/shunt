using System;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Shunt.Core.Scancodes;

namespace Shunt.UI.Controls;

public partial class KeyPickerBox : UserControl
{
    private ICollectionView? _view;
    private string _filterText = string.Empty;
    private bool _isUpdatingTextInternally;

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(KeyPickerBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(KeyPickerBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public event EventHandler? SelectionChanged;

    public KeyPickerBox()
    {
        InitializeComponent();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyPickerBox picker)
        {
            picker.SyncSearchBoxToSelectedItem();
            picker.SelectionChanged?.Invoke(picker, EventArgs.Empty);
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyPickerBox picker)
        {
            picker.SetupCollectionView(e.NewValue as IEnumerable);
        }
    }

    private void SetupCollectionView(IEnumerable? items)
    {
        if (items is null)
        {
            _view = null;
            PickerListBox.ItemsSource = null;
            return;
        }

        _view = CollectionViewSource.GetDefaultView(items);
        _view.GroupDescriptions.Clear();
        _view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        _view.Filter = FilterItem;
        PickerListBox.ItemsSource = _view;
        SyncSearchBoxToSelectedItem();
    }

    private bool FilterItem(object item)
    {
        if (string.IsNullOrWhiteSpace(_filterText)) return true;
        if (item is MappingChoice choice)
        {
            return choice.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                   choice.Description.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                   choice.Category.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
        }
        return item.ToString()?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void SyncSearchBoxToSelectedItem()
    {
        _isUpdatingTextInternally = true;
        try
        {
            if (SelectedItem is MappingChoice choice)
            {
                SearchBox.Text = choice.Name;
                PickerListBox.SelectedItem = choice;
            }
            else if (SelectedItem is not null)
            {
                SearchBox.Text = SelectedItem.ToString();
                PickerListBox.SelectedItem = SelectedItem;
            }
            else
            {
                SearchBox.Text = string.Empty;
                PickerListBox.SelectedItem = null;
            }
        }
        finally
        {
            _isUpdatingTextInternally = false;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingTextInternally) return;

        _filterText = SearchBox.Text;
        if (_view != null)
        {
            _view.Refresh();
            if (!PickerPopup.IsOpen && SearchBox.IsFocused)
            {
                PickerPopup.IsOpen = true;
            }
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_view != null)
        {
            _filterText = string.Empty;
            _view.Refresh();
            PickerPopup.IsOpen = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (!PickerPopup.IsOpen)
            {
                PickerPopup.IsOpen = true;
            }
            else if (PickerListBox.Items.Count > 0)
            {
                PickerListBox.Focus();
                if (PickerListBox.SelectedIndex < 0)
                    PickerListBox.SelectedIndex = 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PickerPopup.IsOpen = false;
            SyncSearchBoxToSelectedItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (PickerPopup.IsOpen && PickerListBox.SelectedItem != null)
            {
                CommitSelection(PickerListBox.SelectedItem);
                e.Handled = true;
            }
        }
    }

    private void PickerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PickerListBox.SelectedItem != null && PickerPopup.IsOpen)
        {
            CommitSelection(PickerListBox.SelectedItem);
        }
    }

    private void CommitSelection(object item)
    {
        SelectedItem = item;
        PickerPopup.IsOpen = false;
        SyncSearchBoxToSelectedItem();
    }

    private void PickerPopup_Opened(object? sender, EventArgs e)
    {
        _filterText = string.Empty;
        _view?.Refresh();
        SearchBox.SelectAll();
    }

    private void PickerPopup_Closed(object? sender, EventArgs e)
    {
        SyncSearchBoxToSelectedItem();
    }
}
