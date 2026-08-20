using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace iRacingLauncher.Models;

public class LauncherConfig
{
    public int LaunchDelaySeconds { get; set; } = 2;
    public bool LaunchAtWindowsStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public List<Profile> Profiles { get; set; } = new();
    public string ActiveProfileName { get; set; } = string.Empty;

    /// <summary>
    /// The profile currently selected for launching. Falls back to the first profile
    /// when ActiveProfileName doesn't match any (e.g. its profile was deleted).
    /// Callers must ensure Profiles is never empty. Derived from Profiles/
    /// ActiveProfileName, so it must not round-trip through JSON itself.
    /// </summary>
    [JsonIgnore]
    public Profile ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfileName) ?? Profiles[0];
}
