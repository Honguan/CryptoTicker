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
        base.OnStartup(eventArgs);

        _mainWindow = new MainWindow();
        _floatingWindow = new FloatingWindow();
        _floatingWindow.OpenRequested += ShowMainWindow;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("開啟", null, (_, _) => ShowMainWindow());
        menu.Items.Add("結束", null, (_, _) => ExitApplication());
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "app-icon.ico")),
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

    private void ExitApplication()
    {
        IsExiting = true;
        Shutdown();
    }
}
