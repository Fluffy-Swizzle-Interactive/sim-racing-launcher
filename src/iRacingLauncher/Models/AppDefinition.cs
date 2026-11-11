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
}
