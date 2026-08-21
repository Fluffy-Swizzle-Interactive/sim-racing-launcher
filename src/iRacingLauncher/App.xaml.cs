using System;
using System.Windows;
using iRacingLauncher.Services;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Tray.Controls;

namespace iRacingLauncher;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsService = new SettingsService(SettingsService.GetDefaultConfigPath());
        var config = settingsService.Load();

        ApplicationThemeManager.Apply(
            config.Theme == "Dark" ? ApplicationTheme.Dark : ApplicationTheme.Light);

        var processService = new ProcessService(new Win32ProcessGateway());
        var appFinder = new AppFinderService(new Win32FileProbe(), AppFinderService.GetDefaultSearchRoots());

        _mainWindow = new MainWindow(config, processService, settingsService, appFinder);
        MainWindow = _mainWindow;

        _notifyIcon = new NotifyIcon
        {
            TooltipText = "Sim Racing Launcher",
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/Assets/SimRacingLauncher.ico")),
            // MenuOnRightClick (and FocusOnLeftClick) are normally pushed into the tray
            // icon's internal manager by InitializeIcon(), which the explicit Register()
            // workaround below bypasses — changing either value here would silently have
            // no effect without further changes.
            MenuOnRightClick = true,
        };
        _notifyIcon.LeftClick += (_, _) => ShowMainWindow();

        var menu = new System.Windows.Controls.ContextMenu();
        var showItem = new System.Windows.Controls.MenuItem { Header = "Show" };
        showItem.Click += (_, _) => ShowMainWindow();
        var launchItem = new System.Windows.Controls.MenuItem { Header = "Launch Selected" };
        launchItem.Click += async (_, _) =>
        {
            try
            {
                await processService.LaunchSelectedAsync(config.ActiveProfile.Apps, config.LaunchDelaySeconds);
            }
            catch (Exception)
            {
                // There is no window to report to from the tray menu; a failed launch
                // must not bring the app down.
            }
        };
        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(showItem);
        menu.Items.Add(launchItem);
        menu.Items.Add(exitItem);
        _notifyIcon.Menu = menu;

        _mainWindow.Show();

        // NotifyIcon only auto-registers with the shell from its OnRender() override,
        // which never fires because this NotifyIcon is never added to a visual tree.
        // Register explicitly so the tray icon actually appears. This must happen
        // after Show(): registration resolves the parent HwndSource from
        // Application.Current.MainWindow via PresentationSource.FromVisual, which is
        // null (and registration silently no-ops) until the window's Win32 handle
        // has been created.
        _notifyIcon.Register();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
