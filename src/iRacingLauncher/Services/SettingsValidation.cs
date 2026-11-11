using System;
using System.Collections.Generic;
using System.Linq;
using iRacingLauncher.Models;

namespace iRacingLauncher.Services;

/// <summary>The tabs of the Settings window.</summary>
public enum SettingsTab { Games, Apps, General }

/// <summary>
/// Why Settings can't save, and where: the tab and the game or app to select so
/// the message never points at something the user can't see.
/// </summary>
public sealed record SettingsProblem(string Message, SettingsTab Tab, Profile? Game, AppDefinition? App);

/// <summary>Save's checks for the Settings window.</summary>
public static class SettingsValidation
{
    /// <summary>
    /// The first problem that should block Save, games first, or null when it's fine.
    /// <list type="bullet">
    /// <item>A blank game name shows as an empty row in the main window's dropdown.</item>
    /// <item>Two games with the same name make the second unreachable, because the
    /// active game is looked up by name.</item>
    /// <item>A Steam App ID goes into a steam:// URL, so it must be just the number. A
    /// pasted store URL is the likely mistake, hence the example in the message.</item>
    /// </list>
    /// </summary>
    public static SettingsProblem? Validate(IReadOnlyList<Profile> games, IReadOnlyList<AppDefinition> apps)
    {
        var blank = games.FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Name));
        if (blank is not null)
        {
            return new SettingsProblem(
                "Every game needs a name. Give the blank one a name, or delete it.",
                SettingsTab.Games, blank, null);
        }

        var duplicate = games
            .GroupBy(g => g.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            var first = duplicate.First();
            return new SettingsProblem(
                $"Two games are both named \"{first.Name.Trim()}\". Game names have to be unique.",
                SettingsTab.Games, duplicate.Skip(1).First(), null);
        }

        var badId = apps.FirstOrDefault(a => SteamLaunch.UsesSteam(a) && !SteamLaunch.IsValidAppId(a.SteamAppId));
        if (badId is not null)
        {
            return new SettingsProblem(
                $"The Steam App ID for \"{badId.Name}\" should be just the number, like 2399420.",
                SettingsTab.Apps, null, badId);
        }

        return null;
    }
}
