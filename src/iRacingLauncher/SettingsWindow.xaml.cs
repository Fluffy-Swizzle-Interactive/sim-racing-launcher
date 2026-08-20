using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace iRacingLauncher;

public class AppEditRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public AppEntry Model { get; }

    public AppEditRowViewModel(AppEntry model) => Model = model;

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public string ProcessName
    {
        get => Model.ProcessName;
        set { Model.ProcessName = value; OnPropertyChanged(); }
    }

    public string Path
    {
        get => Model.Path;
        set { Model.Path = value; OnPropertyChanged(); }
    }

    private string _findStatus = string.Empty;
    public string FindStatus
    {
        get => _findStatus;
        set { _findStatus = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public partial class SettingsWindow : FluentWindow
{
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "iRacingLauncher";

    private readonly LauncherConfig _config;
    private readonly SettingsService _settingsService;
    private readonly AppFinderService _appFinder;
    private readonly ObservableCollection<AppEditRowViewModel> _editRows = new();
    private readonly List<AppEntry> _workingApps;
    private readonly string _originalTheme;

    public SettingsWindow(LauncherConfig config, SettingsService settingsService, AppFinderService appFinder)
    {
        InitializeComponent();
        _config = config;
        _settingsService = settingsService;
        _appFinder = appFinder;

        // The theme toggle applies live, so remember what we came in with to restore
        // it on any path that closes without saving.
        _originalTheme = _config.Theme;

        // Work on a deep copy so Cancel can genuinely discard edits — see task-7 review fix.
        _workingApps = _config.Apps
            .Select(a => new AppEntry { Name = a.Name, ProcessName = a.ProcessName, Path = a.Path, Selected = a.Selected })
            .ToList();

        foreach (var app in _workingApps)
        {
            _editRows.Add(new AppEditRowViewModel(app));
        }
        AppEditList.ItemsSource = _editRows;

        DelayBox.Value = _config.LaunchDelaySeconds;
        StartupToggle.IsChecked = _config.LaunchAtWindowsStartup;
        ThemeToggle.IsChecked = _config.Theme == "Dark";
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppEntry { Name = "New App", ProcessName = string.Empty, Path = string.Empty, Selected = true };
        _workingApps.Add(app);
        _editRows.Add(new AppEditRowViewModel(app));
    }

    private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        _workingApps.Remove(row.Model);
        _editRows.Remove(row);
    }

    private void MoveAppUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        var index = _editRows.IndexOf(row);
        if (index <= 0) return;
        _editRows.Move(index, index - 1);
        _workingApps.Remove(row.Model);
        _workingApps.Insert(index - 1, row.Model);
    }

    private void MoveAppDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        var index = _editRows.IndexOf(row);
        if (index < 0 || index >= _editRows.Count - 1) return;
        _editRows.Move(index, index + 1);
        _workingApps.Remove(row.Model);
        _workingApps.Insert(index + 1, row.Model);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        var dialog = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = $"Locate {row.Name}" };
        if (dialog.ShowDialog() == true)
        {
            row.Path = dialog.FileName;
            row.FindStatus = string.Empty;
        }
    }

    private async void AutoFindButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button) return;
        if (button.Tag is not AppEditRowViewModel row) return;

        // A full Program Files walk takes tens of seconds — never on the UI thread.
        button.IsEnabled = false;
        row.FindStatus = "Searching...";
        try
        {
            var name = row.Name;
            var path = row.Path;
            var found = await Task.Run(() => _appFinder.FindPath(name, path));
            if (found is not null)
            {
                row.Path = found;
                row.FindStatus = "Found — review and Save to confirm.";
            }
            else
            {
                row.FindStatus = "Not found — browse manually.";
            }
        }
        catch (System.Exception)
        {
            // Spec: Auto-Find surfaces no exception to the user.
            row.FindStatus = "Not found — browse manually.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        var theme = ThemeToggle.IsChecked == true ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Must mutate the existing List<AppEntry> in place rather than reassigning
        // _config.Apps: App.xaml.cs's tray "Launch Selected" closure captures config
        // and reads config.Apps lazily, so swapping the list reference would silently
        // leave the tray menu launching a stale app list.
        _config.Apps.Clear();
        _config.Apps.AddRange(_workingApps);

        _config.LaunchDelaySeconds = (int)(DelayBox.Value ?? 2);
        _config.LaunchAtWindowsStartup = StartupToggle.IsChecked == true;
        _config.Theme = ThemeToggle.IsChecked == true ? "Dark" : "Light";

        ApplyStartupRegistration(_config.LaunchAtWindowsStartup);
        _settingsService.Save(_config);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        // Any close that isn't a Save (Cancel, the title-bar close button, Alt+F4)
        // must undo the live theme preview — otherwise the visible theme and the
        // persisted _config.Theme disagree until the app restarts.
        if (DialogResult != true)
        {
            ApplicationThemeManager.Apply(
                _originalTheme == "Dark" ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        base.OnClosed(e);
    }

    /// <summary>
    /// Writes/removes the HKCU Run entry. Registry access and MainModule can both be
    /// denied by policy or AV; a failure here is non-fatal and must never block the
    /// rest of Save.
    /// </summary>
    private static void ApplyStartupRegistration(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                if (key.GetValue(StartupValueName) is not null)
                {
                    key.DeleteValue(StartupValueName);
                }
            }
        }
        catch (System.Security.SecurityException)
        {
            // Startup registration blocked by policy — skip it silently.
        }
        catch (System.UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // MainModule can be denied by AV/anti-cheat drivers.
        }
    }
}
