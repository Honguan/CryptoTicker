using System.Windows;
using Forms = System.Windows.Forms;

namespace CryptoTicker.Desktop;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        RegisterCrashLogging();
        base.OnStartup(eventArgs);

        _mainWindow = new MainWindow();
        _floatingWindow = new FloatingWindow();
        _floatingWindow.OpenRequested += ShowMainWindow;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("開啟", null, (_, _) => ShowMainWindow());
        menu.Items.Add("結束", null, (_, _) => ExitApplication());
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "加密貨幣行情",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        _mainWindow.PriceChanged += _floatingWindow.SetQuote;
        _mainWindow.Show();
        _floatingWindow.Show();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _trayIcon?.Dispose();
        _mainWindow?.Stop();
        base.OnExit(eventArgs);
    }

    private void ShowMainWindow()
    {
        _mainWindow!.Show();
        _mainWindow.Activate();
    }

    public void Notify(string title, string message) => _trayIcon?.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath);
            }

            LogCrash(new System.IO.FileNotFoundException("Tray icon not found.", iconPath));
        }
        catch (Exception exception)
        {
            LogCrash(exception);
        }

        return System.Drawing.SystemIcons.Application;
    }

    private static void RegisterCrashLogging()
    {
        Current.DispatcherUnhandledException += (_, eventArgs) => LogCrash(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                LogCrash(exception);
            }
            else
            {
                LogCrash(new Exception(eventArgs.ExceptionObject?.ToString() ?? "Unknown unhandled exception"));
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => LogCrash(eventArgs.Exception);
    }

    private static void LogCrash(Exception exception)
    {
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CryptoTicker");
            System.IO.Directory.CreateDirectory(directory);
            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
            var text = $"[{DateTimeOffset.Now:O}] Version {version}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "crash.log"), text);
        }
        catch
        {
        }
    }

    private void ExitApplication()
    {
        IsExiting = true;
        Shutdown();
    }
}
