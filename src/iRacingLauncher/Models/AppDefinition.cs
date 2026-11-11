namespace iRacingLauncher.Models;

public class AppDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    private string _arguments = string.Empty;

    /// <summary>
    /// Raw command-line string passed to the exe. Never null: a hand-edited
    /// config.json with "arguments": null loads as "no arguments".
    /// </summary>
    public string Arguments
    {
        get => _arguments;
        set => _arguments = value ?? string.Empty;
    }

    private string _steamAppId = string.Empty;

    /// <summary>
    /// Steam App ID (e.g. "2399420"). When set, the app launches through Steam
    /// instead of by <see cref="Path"/>, and <see cref="Arguments"/> is not used.
    /// Never null, for the same reason as <see cref="Arguments"/>.
    /// </summary>
    public string SteamAppId
    {
        get => _steamAppId;
        set => _steamAppId = value ?? string.Empty;
    }

    /// <summary>Launch through Steam's VR mode. Only applies when <see cref="SteamAppId"/> is set.</summary>
    public bool LaunchInVr { get; set; }

    /// <summary>
    /// An independent copy, used by Settings so Cancel can discard edits. Every
    /// field is a string or bool, so a shallow copy is a full copy, and new fields
    /// are copied automatically.
    /// </summary>
    public AppDefinition Clone() => (AppDefinition)MemberwiseClone();
}
