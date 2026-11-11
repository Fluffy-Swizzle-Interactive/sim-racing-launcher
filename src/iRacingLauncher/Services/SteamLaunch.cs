using System;
using System.Linq;
using iRacingLauncher.Models;

namespace iRacingLauncher.Services;

/// <summary>
/// Rules for launching an app through Steam by its App ID instead of by exe path.
/// </summary>
public static class SteamLaunch
{
    /// <summary>True when the app has a Steam App ID and so launches through Steam.</summary>
    public static bool UsesSteam(AppDefinition app) => !string.IsNullOrWhiteSpace(app.SteamAppId);

    /// <summary>
    /// A Steam App ID is a plain number. Only ASCII digits are accepted (not
    /// char.IsDigit, which also allows full-width and other Unicode digits), so the
    /// ID can go straight into a steam:// URL with nothing else smuggled in.
    /// </summary>
    public static bool IsValidAppId(string id)
    {
        var trimmed = id?.Trim() ?? string.Empty;
        return trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// The steam:// URL that launches <paramref name="app"/>: Steam's VR launch when
    /// <see cref="AppDefinition.LaunchInVr"/> is set, a normal launch otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app's Steam App ID isn't a valid number.</exception>
    public static string BuildUrl(AppDefinition app)
    {
        if (!IsValidAppId(app.SteamAppId))
        {
            throw new InvalidOperationException($"\"{app.SteamAppId}\" is not a valid Steam App ID.");
        }
        var id = app.SteamAppId.Trim();
        return app.LaunchInVr ? $"steam://launch/{id}/vr" : $"steam://rungameid/{id}";
    }

    /// <summary>
    /// Whether Start can be offered for the app: Steam apps don't need a path on
    /// disk, everything else does.
    /// </summary>
    public static bool IsLaunchable(AppDefinition app, Func<string, bool> fileExists) =>
        UsesSteam(app) || fileExists(app.Path);
}
