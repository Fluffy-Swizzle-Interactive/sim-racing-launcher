using System.Collections.Generic;
using System.Linq;
using iRacingLauncher.Models;

namespace iRacingLauncher.Services;

/// <summary>Text for the main window's launch banner.</summary>
public static class LaunchMessages
{
    /// <summary>
    /// "Couldn't start" banner text that points at the likely cause: the path for
    /// an exe app, or Steam and the App ID for a game launched through Steam.
    /// </summary>
    /// <param name="failedNames">Names from <c>LaunchResult.Failed</c>.</param>
    /// <param name="attempted">The apps that were launched, to tell exe apps from Steam games.</param>
    public static string CouldNotStart(IReadOnlyList<string> failedNames, IReadOnlyList<AppDefinition> attempted)
    {
        var failed = attempted.Where(a => failedNames.Contains(a.Name)).ToList();
        var anySteam = failed.Any(SteamLaunch.UsesSteam);
        var anyExe = failed.Any(a => !SteamLaunch.UsesSteam(a));

        var hint = (anySteam, anyExe) switch
        {
            (true, false) => "Check that Steam is installed and the Steam App ID is right.",
            (true, true) => "Check their paths in Settings, and for Steam games that Steam is installed and the App ID is right.",
            _ => "Check their paths in Settings.",
        };
        return $"Couldn't start: {string.Join(", ", failedNames)}. {hint}";
    }
}
