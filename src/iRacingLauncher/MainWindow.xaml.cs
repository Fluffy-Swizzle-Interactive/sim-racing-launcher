using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using iRacingLauncher.ViewModels;
using Wpf.Ui.Appearance;
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
    private bool _isExiting;
    private System.Threading.CancellationTokenSource? _launchCts;

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

        TitleBarIcon.Source = TitleBarIconSource.ForTheme(ApplicationThemeManager.GetAppTheme());
        ApplicationThemeManager.Changed += OnAppThemeChanged;

        LoadProfileNames();
        LoadRowsFromActiveProfile();

        RefreshStatuses();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => RefreshStatuses();
        _statusTimer.Start();
    }

    private void OnAppThemeChanged(ApplicationTheme theme, System.Windows.Media.Color systemAccent) =>
        TitleBarIcon.Source = TitleBarIconSource.ForTheme(theme);

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
        foreach (var existingRow in _rows)
        {
            existingRow.PropertyChanged -= AppRow_PropertyChanged;
        }
        _rows.Clear();
        foreach (var entry in _config.ActiveProfile.AppRefs.Where(r => r.Selected))
        {
            // Cascading delete in Settings keeps AppRefs in sync with the registry,
            // but a hand-edited config could still carry a stale AppId — skip that
            // row rather than crashing the whole main window.
            var definition = _config.Apps.FirstOrDefault(a => a.Id == entry.AppId);
            if (definition is null) continue;
            var row = new AppRowViewModel(definition, entry) { Icon = TryLoadIcon(definition.Path) };
            row.PropertyChanged += AppRow_PropertyChanged;
            _rows.Add(row);
        }
        AppList.ItemsSource = _rows;
    }

    /// <summary>
    /// Only Selected apps are shown at all, so unchecking one here must remove its
    /// row immediately rather than leave an unchecked row behind — the app stays in
    /// the profile's AppRefs with Selected=false; Settings' Selected Apps checklist
    /// is the way back onto this list.
    /// </summary>
    private void AppRow_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppRowViewModel.Selected)) return;
        if (sender is not AppRowViewModel row || row.Selected) return;
        row.PropertyChanged -= AppRow_PropertyChanged;
        _rows.Remove(row);
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
        // Iterate a snapshot: a row's PropertyChanged handler removes it from _rows
        // when it becomes deselected, and this runs on a 2-second timer tick, so
        // enumerating the live collection would risk tearing down the whole window
        // with a "collection was modified" exception mid-refresh.
        foreach (var row in _rows.ToList())
        {
            // A blank path is invalid, not implicitly valid — an app with no path
            // configured must read "Path not found" and have Start disabled. A Steam
            // app launches by its App ID, so it needs no path at all.
            row.PathValid = SteamLaunch.IsLaunchable(row.Definition, File.Exists);
            row.IsRunning = _processService.IsRunning(row.Definition);
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

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) => await LaunchSelectedAsync();

    /// <summary>
    /// Launches every selected app in the active profile. Shared by the Launch
    /// Selected button and the tray menu's own "Launch Selected" item, so both go
    /// through the same cancellation-aware path — see <see cref="_launchCts"/>.
    /// </summary>
    public async Task LaunchSelectedAsync()
    {
        // Disabling the Launch button doesn't prevent re-entry: the tray menu's own
        // "Launch Selected" calls straight in here, and works while the window is
        // hidden. So supersede any launch already in flight rather than running two
        // batches over the same apps. Each invocation owns exactly one token source
        // and disposes it in its own finally, so cancelling `previous` here can
        // never touch one that's already been disposed.
        var previous = _launchCts;
        var cts = new System.Threading.CancellationTokenSource();
        _launchCts = cts;
        previous?.Cancel();

        LaunchButton.IsEnabled = false;
        LaunchInfoBar.IsOpen = false;
        var progress = new Progress<(int Current, int Total)>(p =>
        {
            // A superseded batch must not keep painting its own progress over the
            // batch that replaced it.
            if (ReferenceEquals(_launchCts, cts))
            {
                LaunchButton.Content = $"Launching {p.Current} of {p.Total}…";
            }
        });
        try
        {
            var selectedApps = _config.ResolveApps(_config.ActiveProfile.AppRefs.Where(r => r.Selected)).ToList();
            var result = await _processService.LaunchAppsAsync(selectedApps, _config.LaunchDelaySeconds, progress, cts.Token);
            if (result.Failed.Count > 0 && ReferenceEquals(_launchCts, cts))
            {
                LaunchInfoBar.Message = $"Couldn't start: {string.Join(", ", result.Failed)}. Check their paths in Settings.";
                LaunchInfoBar.IsOpen = true;
            }
        }
        catch (Exception)
        {
            // Never let a launch failure take the app down; the status refresh
            // below reports whatever actually ended up running.
        }
        finally
        {
            // Only the newest batch owns the button. A superseded one finishing
            // later must not re-enable it while its replacement is still launching.
            if (ReferenceEquals(_launchCts, cts))
            {
                _launchCts = null;
                RefreshStatuses();
                LaunchButton.Content = "Launch Selected";
                LaunchButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Cancel any in-progress launch batch first — otherwise it keeps staggering
        // through its remaining apps regardless of Stop All, and can even re-launch
        // one this just killed once the loop reaches it.
        _launchCts?.Cancel();
        _processService.StopAll(_config.ResolveApps(_config.ActiveProfile.AppRefs));
        RefreshStatuses();
    }

    private void AppRow_StartRequested(object? sender, AppRowViewModel row)
    {
        try
        {
            _processService.StartApp(row.Definition);
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
        _processService.StopApp(row.Definition);
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

    /// <summary>Called before a real shutdown (tray "Exit") so OnClosing lets it through instead of hiding to tray.</summary>
    public void PrepareForExit() => _isExiting = true;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_config.CloseToTray && !_isExiting)
        {
            // Persist here too — a Windows logoff/shutdown (or any other close path
            // that isn't tray "Exit") lands here and must not silently drop unsaved
            // changes just because the window is hiding instead of exiting.
            _settingsService.Save(_config);
            e.Cancel = true;
            Hide();
            return;
        }

        ApplicationThemeManager.Changed -= OnAppThemeChanged;
        _settingsService.Save(_config);
        base.OnClosing(e);
    }
}
