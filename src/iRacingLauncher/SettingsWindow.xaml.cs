using System;
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
    public AppDefinition Model { get; }

    public AppEditRowViewModel(AppDefinition model) => Model = model;

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

    public string Arguments
    {
        get => Model.Arguments;
        set { Model.Arguments = value; OnPropertyChanged(); }
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

public class ProfileEditRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public Profile Model { get; }

    public ProfileEditRowViewModel(Profile model) => Model = model;

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class ProfileAppSelectionRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public AppDefinition Definition { get; }
    public ProfileAppEntry Entry { get; }

    public ProfileAppSelectionRowViewModel(AppDefinition definition, ProfileAppEntry entry)
    {
        Definition = definition;
        Entry = entry;
    }

    public string Name => Definition.Name;

    public bool Selected
    {
        get => Entry.Selected;
        set { Entry.Selected = value; OnPropertyChanged(); }
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
    private readonly ObservableCollection<ProfileEditRowViewModel> _profileEditRows = new();
    private readonly ObservableCollection<AppEditRowViewModel> _appEditRows = new();
    private readonly ObservableCollection<ProfileAppSelectionRowViewModel> _profileAppRows = new();
    private readonly List<AppDefinition> _workingApps;
    private readonly List<Profile> _workingProfiles;
    private readonly string _originalTheme;

    /// <summary>
    /// The profile whose apps the "App List" card is currently showing/editing.
    /// Tracked by reference (not name) so renaming it in the Profiles list doesn't
    /// lose track of it.
    /// </summary>
    private Profile _currentEditingProfile;

    /// <summary>
    /// The profile that will become ActiveProfileName on Save. Tracked by reference
    /// for the same reason — a rename must not silently fall back to the default.
    /// </summary>
    private Profile _workingActiveProfile;

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
            .Select(a => new AppDefinition { Id = a.Id, Name = a.Name, ProcessName = a.ProcessName, Path = a.Path, Arguments = a.Arguments })
            .ToList();
        _workingProfiles = _config.Profiles
            .Select(p => new Profile
            {
                Name = p.Name,
                AppRefs = p.AppRefs.Select(r => new ProfileAppEntry { AppId = r.AppId, Selected = r.Selected }).ToList(),
            })
            .ToList();

        foreach (var profile in _workingProfiles)
        {
            _profileEditRows.Add(new ProfileEditRowViewModel(profile));
        }
        ProfileEditList.ItemsSource = _profileEditRows;

        foreach (var app in _workingApps)
        {
            _appEditRows.Add(new AppEditRowViewModel(app));
        }
        AppEditList.ItemsSource = _appEditRows;
        ProfileAppList.ItemsSource = _profileAppRows;

        _workingActiveProfile = _workingProfiles.FirstOrDefault(p => p.Name == _config.ActiveProfileName) ?? _workingProfiles[0];
        _currentEditingProfile = _workingActiveProfile;
        LoadProfileAppRowsFromCurrentProfile();

        // Plain ui:TextBox, not ui:NumberBox: NumberBox has a real WPF-UI rendering bug
        // where text set before the control's first layout pass never paints (root-caused
        // via its actual XAML template/TextBox.cs source — the placeholder-visibility
        // sync in OnTextChanged, and the underlying text rendering itself, both depend on
        // the TextEditor already existing, which it doesn't yet here since "Launch
        // Behavior" starts collapsed). ui:TextBox has none of that; parsed manually below.
        DelayBox.Text = _config.LaunchDelaySeconds.ToString();
        StartupToggle.IsChecked = _config.LaunchAtWindowsStartup;
        CloseToTrayToggle.IsChecked = _config.CloseToTray;
        ThemeToggle.IsChecked = _config.Theme == "Dark";

        TitleBarIcon.Source = TitleBarIconSource.ForTheme(ApplicationThemeManager.GetAppTheme());
        ApplicationThemeManager.Changed += OnAppThemeChanged;
    }

    private void OnAppThemeChanged(ApplicationTheme theme, System.Windows.Media.Color systemAccent) =>
        TitleBarIcon.Source = TitleBarIconSource.ForTheme(theme);

    private void LoadProfileAppRowsFromCurrentProfile()
    {
        _profileAppRows.Clear();
        foreach (var entry in _currentEditingProfile.AppRefs)
        {
            var definition = _workingApps.First(a => a.Id == entry.AppId);
            _profileAppRows.Add(new ProfileAppSelectionRowViewModel(definition, entry));
        }
        EditingProfileLabel.Text = $"Selected for: {_currentEditingProfile.Name}";
    }

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        // A new profile starts with every existing app selected, same default as
        // a newly-added app being selected in every existing profile.
        var profile = new Profile
        {
            Name = "New Profile",
            AppRefs = _workingApps.Select(a => new ProfileAppEntry { AppId = a.Id, Selected = true }).ToList(),
        };
        _workingProfiles.Add(profile);
        _profileEditRows.Add(new ProfileEditRowViewModel(profile));

        _currentEditingProfile = profile;
        LoadProfileAppRowsFromCurrentProfile();
    }

    private void RemoveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        // Always leave at least one profile — there must always be something to launch.
        if (_workingProfiles.Count <= 1) return;

        _workingProfiles.Remove(row.Model);
        _profileEditRows.Remove(row);

        if (ReferenceEquals(_currentEditingProfile, row.Model))
        {
            _currentEditingProfile = _workingProfiles[0];
            LoadProfileAppRowsFromCurrentProfile();
        }
        if (ReferenceEquals(_workingActiveProfile, row.Model))
        {
            _workingActiveProfile = _workingProfiles[0];
        }
    }

    private void MoveProfileUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        var index = _profileEditRows.IndexOf(row);
        if (index <= 0) return;
        _profileEditRows.Move(index, index - 1);
        _workingProfiles.Remove(row.Model);
        _workingProfiles.Insert(index - 1, row.Model);
    }

    private void MoveProfileDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        var index = _profileEditRows.IndexOf(row);
        if (index < 0 || index >= _profileEditRows.Count - 1) return;
        _profileEditRows.Move(index, index + 1);
        _workingProfiles.Remove(row.Model);
        _workingProfiles.Insert(index + 1, row.Model);
    }

    private void EditProfileAppsButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        _currentEditingProfile = row.Model;
        LoadProfileAppRowsFromCurrentProfile();
    }

    private void MoveProfileAppUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileAppSelectionRowViewModel row) return;
        var index = _profileAppRows.IndexOf(row);
        if (index <= 0) return;
        _profileAppRows.Move(index, index - 1);
        _currentEditingProfile.AppRefs.Remove(row.Entry);
        _currentEditingProfile.AppRefs.Insert(index - 1, row.Entry);
    }

    private void MoveProfileAppDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileAppSelectionRowViewModel row) return;
        var index = _profileAppRows.IndexOf(row);
        if (index < 0 || index >= _profileAppRows.Count - 1) return;
        _profileAppRows.Move(index, index + 1);
        _currentEditingProfile.AppRefs.Remove(row.Entry);
        _currentEditingProfile.AppRefs.Insert(index + 1, row.Entry);
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppDefinition { Id = Guid.NewGuid().ToString("N"), Name = "New App", ProcessName = string.Empty, Path = string.Empty, Arguments = string.Empty };
        _workingApps.Add(app);
        _appEditRows.Add(new AppEditRowViewModel(app));

        // A newly-added app starts selected in every existing profile.
        foreach (var profile in _workingProfiles)
        {
            profile.AppRefs.Add(new ProfileAppEntry { AppId = app.Id, Selected = true });
        }
        LoadProfileAppRowsFromCurrentProfile();
    }

    private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        _workingApps.Remove(row.Model);
        _appEditRows.Remove(row);

        // Deleting an app from the registry cascades to every profile referencing it.
        foreach (var profile in _workingProfiles)
        {
            profile.AppRefs.RemoveAll(r => r.AppId == row.Model.Id);
        }
        LoadProfileAppRowsFromCurrentProfile();
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
        // Must mutate the existing lists in place rather than reassigning
        // _config.Apps/_config.Profiles: App.xaml.cs's tray "Launch Selected" closure
        // captures config and reads config.ActiveProfile lazily, so swapping either
        // list reference would silently leave the tray menu launching stale data.
        _config.Apps.Clear();
        _config.Apps.AddRange(_workingApps);
        _config.Profiles.Clear();
        _config.Profiles.AddRange(_workingProfiles);
        _config.ActiveProfileName = _workingActiveProfile.Name;

        // DelayBox is a plain ui:TextBox (see constructor comment) — parse and clamp
        // manually, matching the 0-30 range the old NumberBox enforced. An unparseable
        // value falls back to the prior default of 2 rather than rejecting the save.
        _config.LaunchDelaySeconds = int.TryParse(DelayBox.Text, out var delaySeconds)
            ? Math.Clamp(delaySeconds, 0, 30)
            : 2;
        _config.LaunchAtWindowsStartup = StartupToggle.IsChecked == true;
        _config.CloseToTray = CloseToTrayToggle.IsChecked == true;
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
        ApplicationThemeManager.Changed -= OnAppThemeChanged;
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
