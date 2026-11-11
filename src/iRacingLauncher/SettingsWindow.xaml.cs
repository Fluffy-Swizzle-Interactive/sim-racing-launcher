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
        set
        {
            Model.Path = value;
            OnPropertyChanged();
            // Only reacts to a path the user just set (Browse, Auto-Find, typing);
            // the constructor goes through Model, so opening Settings changes nothing.
            if (SteamAppIdFinder.ShouldAutoFill(SteamAppId, Arguments, value)
                && SteamAppIdFinder.FindAppId(value) is { } id)
            {
                SteamAppId = id;
                FindStatus = $"Steam game detected (App ID {id}). It will launch through Steam.";
            }
        }
    }

    // Status is set before Path so that, when the new path auto-fills a Steam App
    // ID, the setter's "Steam game detected" message is the one left showing.

    /// <summary>The Browse button picked <paramref name="path"/>.</summary>
    public void SetPathFromBrowse(string path)
    {
        FindStatus = string.Empty;
        Path = path;
    }

    /// <summary>Auto-Find located the app at <paramref name="path"/>.</summary>
    public void SetPathFromAutoFind(string path)
    {
        FindStatus = "Found — review and Save to confirm.";
        Path = path;
    }

    /// <summary>The Find ID button: fills the App ID from the path even when auto-fill held back.</summary>
    public void FindSteamAppId()
    {
        if (SteamAppIdFinder.FindAppId(Path) is { } id)
        {
            SteamAppId = id;
            FindStatus = "Found Steam App ID. Review and Save to confirm.";
        }
        else
        {
            FindStatus = "Couldn't find a Steam App ID for this path. Copy it from the game's store page URL.";
        }
    }

    public string Arguments
    {
        get => Model.Arguments;
        set { Model.Arguments = value; OnPropertyChanged(); }
    }

    public string SteamAppId
    {
        get => Model.SteamAppId;
        set
        {
            Model.SteamAppId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsesSteam));
            OnPropertyChanged(nameof(ShowVrTag));
        }
    }

    public bool LaunchInVr
    {
        get => Model.LaunchInVr;
        set
        {
            Model.LaunchInVr = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowVrTag));
        }
    }

    /// <summary>
    /// Drives the VR checkbox, the disabled Arguments box and the Steam hint, and is
    /// the Steam tag in the Apps list.
    /// </summary>
    public bool UsesSteam => SteamLaunch.UsesSteam(Model);

    /// <summary>The VR tag in the Apps list: only when the app really launches in VR.</summary>
    public bool ShowVrTag => UsesSteam && LaunchInVr;

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

/// <summary>
/// One row in the "Selected Apps" checklist for whichever profile is currently
/// being edited. An app the profile doesn't reference yet (no ProfileAppEntry in
/// its AppRefs) still gets a row here — unchecked, with a null Entry — so the user
/// has a way to enable it. Checking that row lazily creates a real ProfileAppEntry
/// and appends it to the owning profile's AppRefs; unchecking an already-referenced
/// row just flips its existing Entry.Selected rather than removing it, so its
/// position in AppRefs (and thus its place in per-profile launch order) survives
/// being unchecked and re-checked later.
/// </summary>
public class ProfileAppSelectionRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public AppDefinition Definition { get; }
    private readonly Profile _owningProfile;
    private ProfileAppEntry? _entry;

    /// <summary>Null until this row's app has been checked at least once for this profile.</summary>
    public ProfileAppEntry? Entry => _entry;

    /// <summary>Whether this profile already has a ProfileAppEntry for this app (vs. this row existing only to offer the option).</summary>
    public bool IsReferenced => _entry is not null;

    public ProfileAppSelectionRowViewModel(AppDefinition definition, ProfileAppEntry? entry, Profile owningProfile)
    {
        Definition = definition;
        _entry = entry;
        _owningProfile = owningProfile;
    }

    public string Name => Definition.Name;

    public bool Selected
    {
        get => _entry?.Selected ?? false;
        set
        {
            if (_entry is null)
            {
                // Unchecking a row that was never referenced (already false) is a no-op —
                // don't create an entry just to record "not selected".
                if (!value) return;

                _entry = new ProfileAppEntry { AppId = Definition.Id, Selected = true };
                _owningProfile.AppRefs.Add(_entry);
                OnPropertyChanged(nameof(Entry));
                OnPropertyChanged(nameof(IsReferenced));
            }
            else
            {
                if (_entry.Selected == value) return;
                _entry.Selected = value;
            }
            OnPropertyChanged();
        }
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
    /// The profile whose apps the "Selected Apps" card is currently showing/editing
    /// (checked/unchecked against the shared "Apps" registry card). Tracked by
    /// reference (not name) so renaming it in the Profiles list doesn't lose track
    /// of it.
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
            .Select(a => a.Clone())
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
        var referencedAppIds = new HashSet<string>();
        foreach (var entry in _currentEditingProfile.AppRefs)
        {
            // Cascading delete keeps AppRefs in sync with the registry, but a
            // hand-edited config could still carry a stale AppId — skip that row
            // rather than crashing Settings.
            var definition = _workingApps.FirstOrDefault(a => a.Id == entry.AppId);
            if (definition is null) continue;
            referencedAppIds.Add(entry.AppId);
            _profileAppRows.Add(new ProfileAppSelectionRowViewModel(definition, entry, _currentEditingProfile));
        }

        // Apps this profile doesn't reference yet are appended unchecked, so there's
        // always a way to enable any app for any profile — see finding 3.
        foreach (var app in _workingApps)
        {
            if (referencedAppIds.Contains(app.Id)) continue;
            _profileAppRows.Add(new ProfileAppSelectionRowViewModel(app, entry: null, _currentEditingProfile));
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
        // A not-yet-referenced row has no AppRefs position to move — no-op until it's checked.
        if (row.Entry is not { } entry) return;
        // Index within AppRefs itself, not within _profileAppRows: that list also
        // holds not-yet-referenced rows interleaved with referenced ones, so its
        // indices don't correspond 1:1 to AppRefs positions once both kinds are
        // present — using it here silently mis-ordered (or no-opped) a save.
        var index = _currentEditingProfile.AppRefs.IndexOf(entry);
        if (index <= 0) return;
        _currentEditingProfile.AppRefs.RemoveAt(index);
        _currentEditingProfile.AppRefs.Insert(index - 1, entry);
        LoadProfileAppRowsFromCurrentProfile();
    }

    private void MoveProfileAppDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileAppSelectionRowViewModel row) return;
        // A not-yet-referenced row has no AppRefs position to move — no-op until it's checked.
        if (row.Entry is not { } entry) return;
        var index = _currentEditingProfile.AppRefs.IndexOf(entry);
        if (index < 0 || index >= _currentEditingProfile.AppRefs.Count - 1) return;
        _currentEditingProfile.AppRefs.RemoveAt(index);
        _currentEditingProfile.AppRefs.Insert(index + 1, entry);
        LoadProfileAppRowsFromCurrentProfile();
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppDefinition
        {
            Id = Guid.NewGuid().ToString("N"), Name = "New App", ProcessName = string.Empty, Path = string.Empty,
            Arguments = string.Empty, SteamAppId = string.Empty, LaunchInVr = false,
        };
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
            row.SetPathFromBrowse(dialog.FileName);
        }
    }

    private void FindSteamIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AppEditRowViewModel row)
        {
            row.FindSteamAppId();
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
            // Prefer ProcessName: it's the exe/shortcut base name we actually expect
            // to find on disk (e.g. "Content Manager"), whereas Name is a free-text
            // display label that can diverge from it (e.g. "Assetto Corsa (Content
            // Manager)"). Fall back to Name only when ProcessName hasn't been filled
            // in yet, such as for a brand-new user-added app.
            var name = string.IsNullOrWhiteSpace(row.ProcessName) ? row.Name : row.ProcessName;
            var path = row.Path;
            var found = await Task.Run(() => _appFinder.FindPath(name, path));
            if (found is not null)
            {
                row.SetPathFromAutoFind(found);
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
        var problem = SettingsValidation.Validate(_workingProfiles, _workingApps);
        if (problem is not null)
        {
            ValidationInfoBar.Message = problem.Message;
            ValidationInfoBar.IsOpen = true;
            return;
        }
        ValidationInfoBar.IsOpen = false;

        // Names are stored trimmed so " iRacing" and "iRacing" can't round-trip into
        // the duplicate case the check above just rejected.
        foreach (var profile in _workingProfiles)
        {
            profile.Name = profile.Name.Trim();
        }
        foreach (var app in _workingApps)
        {
            app.SteamAppId = app.SteamAppId.Trim();
        }

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
