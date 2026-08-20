using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using iRacingLauncher.ViewModels;
using Wpf.Ui.Controls;

namespace iRacingLauncher;

public partial class MainWindow : FluentWindow
{
    private readonly LauncherConfig _config;
    private readonly ProcessService _processService;
    private readonly SettingsService _settingsService;
    private readonly AppFinderService _appFinder;
    private readonly ObservableCollection<AppRowViewModel> _rows = new();
    private readonly DispatcherTimer _statusTimer;

    public MainWindow(
        LauncherConfig config,
        ProcessService processService,
        SettingsService settingsService,
        AppFinderService appFinder)
    {
        InitializeComponent();
        _config = config;
        _processService = processService;
        _settingsService = settingsService;
        _appFinder = appFinder;

        foreach (var app in _config.Apps)
        {
            var row = new AppRowViewModel(app) { Icon = TryLoadIcon(app.Path) };
            _rows.Add(row);
        }
        AppList.ItemsSource = _rows;

        RefreshStatuses();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => RefreshStatuses();
        _statusTimer.Start();
    }

    private void RefreshStatuses()
    {
        foreach (var row in _rows)
        {
            // A blank path is invalid, not implicitly valid — an app with no path
            // configured must read "Path not found" and have Start disabled.
            row.PathValid = File.Exists(row.Model.Path);
            row.IsRunning = _processService.IsRunning(row.Model);
        }
    }

    private static System.Windows.Media.ImageSource? TryLoadIcon(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }
            return Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchButton.IsEnabled = false;
        var progress = new Progress<(int Current, int Total)>(p =>
            LaunchButton.Content = $"Launching {p.Current} of {p.Total}…");
        try
        {
            await _processService.LaunchSelectedAsync(_config.Apps, _config.LaunchDelaySeconds, progress);
        }
        catch (Exception)
        {
            // Never let a launch failure take the app down; the status refresh in
            // the finally block reports whatever actually ended up running.
        }
        finally
        {
            RefreshStatuses();
            LaunchButton.Content = "Launch Selected";
            LaunchButton.IsEnabled = true;
        }
    }

    private void AppRow_StartRequested(object? sender, AppRowViewModel row)
    {
        try
        {
            _processService.StartApp(row.Model);
        }
        catch (Exception)
        {
            // Defense in depth — the gateway already reports launch failures as a
            // false return rather than throwing. RefreshStatuses() below shows the
            // row as still not running either way.
        }
        RefreshStatuses();
    }

    private void AppRow_StopRequested(object? sender, AppRowViewModel row)
    {
        _processService.StopApp(row.Model);
        RefreshStatuses();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_config, _settingsService, _appFinder) { Owner = this };
        settingsWindow.ShowDialog();

        _rows.Clear();
        foreach (var app in _config.Apps)
        {
            _rows.Add(new AppRowViewModel(app) { Icon = TryLoadIcon(app.Path) });
        }
        AppList.ItemsSource = _rows;
        RefreshStatuses();
    }

    private void FluentWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _settingsService.Save(_config);
        base.OnClosing(e);
    }
}
