using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        LoadProfileNames();
        LoadRowsFromActiveProfile();

        RefreshStatuses();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => RefreshStatuses();
        _statusTimer.Start();
    }

    /// <summary>
    /// Populates the profile switcher from the config's profile list, selecting
    /// whichever one is active. Set before wiring SelectionChanged so populating it
    /// doesn't itself trigger a reload.
    /// </summary>
    private void LoadProfileNames()
    {
        ProfileComboBox.SelectionChanged -= ProfileComboBox_SelectionChanged;
        ProfileComboBox.ItemsSource = _config.Profiles.Select(p => p.Name).ToList();
        ProfileComboBox.SelectedItem = _config.ActiveProfile.Name;
        ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
    }

    private void LoadRowsFromActiveProfile()
    {
        _rows.Clear();
        foreach (var app in _config.ActiveProfile.Apps)
        {
            _rows.Add(new AppRowViewModel(app) { Icon = TryLoadIcon(app.Path) });
        }
        AppList.ItemsSource = _rows;
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileComboBox.SelectedItem is not string profileName)
        {
            return;
        }
        _config.ActiveProfileName = profileName;
        LoadRowsFromActiveProfile();
        RefreshStatuses();
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
        LaunchInfoBar.IsOpen = false;
        var progress = new Progress<(int Current, int Total)>(p =>
            LaunchButton.Content = $"Launching {p.Current} of {p.Total}…");
        try
        {
            var result = await _processService.LaunchSelectedAsync(_config.ActiveProfile.Apps, _config.LaunchDelaySeconds, progress);
            if (result.Failed.Count > 0)
            {
                LaunchInfoBar.Message = $"Couldn't start: {string.Join(", ", result.Failed)}. Check their paths in Settings.";
                LaunchInfoBar.IsOpen = true;
            }
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

    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        _processService.StopAll(_config.ActiveProfile.Apps);
        RefreshStatuses();
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

        // Settings can add, rename, delete or reorder profiles, and can change which
        // one is active — reload the switcher itself, not just the app rows.
        LoadProfileNames();
        LoadRowsFromActiveProfile();
        RefreshStatuses();
    }

    private void FluentWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _config.MinimizeToTray)
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
