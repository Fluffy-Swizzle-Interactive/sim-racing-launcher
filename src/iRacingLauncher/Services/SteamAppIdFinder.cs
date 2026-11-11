using System;
using System.IO;
using System.Text.RegularExpressions;

namespace iRacingLauncher.Services;

/// <summary>
/// Works out a Steam game's App ID from its exe path. Steam installs every game at
/// <c>&lt;library&gt;\steamapps\common\&lt;installdir&gt;\...</c> and keeps an
/// <c>appmanifest_&lt;id&gt;.acf</c> per game in <c>&lt;library&gt;\steamapps</c>
/// whose "installdir" names that folder.
/// </summary>
public static class SteamAppIdFinder
{
    private static readonly Regex AppIdKey = new("\"appid\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
    private static readonly Regex InstallDirKey = new("\"installdir\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);

    /// <summary>
    /// The App ID of the Steam game whose install folder contains
    /// <paramref name="exePath"/>, or null if the path isn't inside a Steam library
    /// or no manifest matches. Never throws.
    /// </summary>
    public static string? FindAppId(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            var parts = Path.GetFullPath(exePath).Split(Path.DirectorySeparatorChar);
            // Last "steamapps\common\<installdir>" in the path, with something below it.
            for (var i = parts.Length - 3; i >= 0; i--)
            {
                if (!parts[i].Equals("steamapps", StringComparison.OrdinalIgnoreCase)
                    || !parts[i + 1].Equals("common", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var installDir = parts[i + 2];
                var steamApps = string.Join(Path.DirectorySeparatorChar, parts, 0, i + 1);
                return MatchManifest(steamApps, installDir);
            }
            return null;
        }
        catch (Exception)
        {
            // Bad path characters, unreadable folders, anything else: just no match.
            return null;
        }
    }

    private static string? MatchManifest(string steamAppsDir, string installDir)
    {
        if (!Directory.Exists(steamAppsDir))
        {
            return null;
        }

        foreach (var manifest in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
        {
            string text;
            try
            {
                text = File.ReadAllText(manifest);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // The top-level keys come before any nested block, so the first match of
            // each is the game's own.
            var dir = InstallDirKey.Match(text);
            var id = AppIdKey.Match(text);
            if (dir.Success && id.Success
                && dir.Groups[1].Value.Equals(installDir, StringComparison.OrdinalIgnoreCase)
                && SteamLaunch.IsValidAppId(id.Groups[1].Value))
            {
                return id.Groups[1].Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether picking a path should fill in the App ID by itself: only when the box
    /// is empty, and only when the app has no launch arguments, because a Steam
    /// launch ignores them and would quietly break that setup.
    /// </summary>
    public static bool ShouldAutoFill(string steamAppId, string arguments) =>
        string.IsNullOrWhiteSpace(steamAppId) && string.IsNullOrWhiteSpace(arguments);
}
