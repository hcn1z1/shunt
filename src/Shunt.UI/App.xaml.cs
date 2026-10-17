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

    public static string CurrentTheme { get; private set; } = BaseTemplateTheme;

    public static event EventHandler<string>? ThemeChanged;

    /// <summary>
    /// Swaps the active color dictionary between the BaseTemplate (classic dark)
    /// and AimXP (midnight neon) palettes. Control templates resolve their
    /// brushes dynamically, so all open views restyle immediately.
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        CurrentTheme = themeName == AimXPTheme ? AimXPTheme : BaseTemplateTheme;
        if (Current?.Resources is not ResourceDictionary appResources) return;
        var source = CurrentTheme == AimXPTheme
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
        ThemeChanged?.Invoke(null, CurrentTheme);
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

        if (!string.IsNullOrEmpty(Services.AppSettings.Current.Theme))
        {
            ApplyTheme(Services.AppSettings.Current.Theme);
        }

        base.OnStartup(e);
    }

    private static Shunt.Engine.ShuntEngineService? _localEngine;
    public static Shunt.Engine.ShuntEngineService LocalEngine
    {
        get
        {
            if (_localEngine == null)
            {
                Shunt.Core.Interfaces.IVirtualGamepadManager manager;
                try
                {
                    manager = new Shunt.Emulation.ViGEmGamepadManager();
                }
                catch
                {
                    manager = new Shunt.Emulation.StubVirtualGamepadManager();
                }
                _localEngine = new Shunt.Engine.ShuntEngineService(manager);
            }
            return _localEngine;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_localEngine != null && OperatingSystem.IsWindows())
        {
            try { _localEngine.StopShuntModeAsync().GetAwaiter().GetResult(); } catch { }
            try { _localEngine.Dispose(); } catch { }
        }
        base.OnExit(e);
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
