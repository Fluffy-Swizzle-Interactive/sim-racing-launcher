using System.Collections.Generic;

namespace iRacingLauncher.Models;

public class Profile
{
    public string Name { get; set; } = string.Empty;
    public List<AppEntry> Apps { get; set; } = new();
}
