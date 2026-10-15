using System.Windows;
using System.Windows.Controls;

namespace Shunt.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public const string BaseTemplateTheme = "BaseTemplate";
    public const string AimXPTheme = "AimXP";

    /// <summary>
    /// Swaps the active color dictionary between the BaseTemplate (classic dark)
    /// and AimXP (midnight neon) palettes. Control templates resolve their
    /// brushes dynamically, so all open views restyle immediately.
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        if (Current?.Resources is not ResourceDictionary appResources) return;
        var source = themeName == AimXPTheme
            ? new Uri("Themes/AimXP/Colors.xaml", UriKind.Relative)
            : new Uri("Themes/BaseTemplate/Colors.xaml", UriKind.Relative);

        var merged = appResources.MergedDictionaries;
        for (var index = merged.Count - 1; index >= 0; index--)
        {
            var path = merged[index].Source?.OriginalString ?? string.Empty;
            if (path.EndsWith("/Colors.xaml", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("Themes/BaseTemplate/Colors.xaml", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("Themes/AimXP/Colors.xaml", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(index);
        }
        merged.Insert(0, new ResourceDictionary { Source = source });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogFatalCrash(args.ExceptionObject as Exception, "AppDomain Unhandled Exception");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogFatalCrash(args.Exception, "WPF Dispatcher Unhandled Exception");
        };

        base.OnStartup(e);
    }

    private static void LogFatalCrash(Exception? ex, string source)
    {
        try
        {
            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            var errorText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL CRASH ({source}):\n{ex}\n\n";
            System.IO.File.AppendAllText(logPath, errorText);

            MessageBox.Show(
                $"A critical error occurred causing Shunt to crash:\n\n{ex?.Message}\n\nDetails have been written to:\n{logPath}",
                "Shunt - Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Suppress fallback exceptions during crash reporting
        }
    }
}
