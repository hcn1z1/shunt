using System;
using System.Drawing;
using System.Windows.Forms;

namespace Shunt.UI.Services;

/// <summary>
/// Windows notification-area icon with a context menu (Open / Settings / Exit)
/// and balloon-tip notifications, which render as native Windows toasts on
/// Windows 10/11 and are archived in the Action Center.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService(Icon icon)
    {
        _icon = icon;
        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Shunt — Open Buttons Router",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Shunt", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void ShowNotification(string title, string message, bool isError = false)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(5000, title, message, isError ? ToolTipIcon.Error : ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
