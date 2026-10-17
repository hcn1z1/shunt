using System;
using System.IO;
using System.Text.Json;

namespace Shunt.UI.Services;

/// <summary>
/// User preferences persisted to <c>%APPDATA%\Shunt\settings.json</c>.
/// Access <see cref="Current"/>; call <see cref="Save"/> after mutating.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shunt", "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    /// <summary>Show Windows toast notifications when a controller connects or disconnects.</summary>
    public bool ControllerNotificationsEnabled { get; set; } = true;

    /// <summary>Closing the main window hides it to the notification area instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Active color theme name: "BaseTemplate" or "AimXP".</summary>
    public string Theme { get; set; } = "BaseTemplate";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort persistence; the app keeps running on in-memory defaults.
        }
    }
}
