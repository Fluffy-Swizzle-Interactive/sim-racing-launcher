using System.Collections.Generic;

namespace iRacingLauncher.Models;

public class LauncherConfig
{
    public int LaunchDelaySeconds { get; set; } = 2;
    public bool LaunchAtWindowsStartup { get; set; } = false;
    public string Theme { get; set; } = "Dark";
    public List<AppEntry> Apps { get; set; } = new();
}
