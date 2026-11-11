using System.Collections.Generic;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

/// <summary>
/// The banner shown when Launch Selected couldn't start something. It should point
/// at the likely cause: a bad path for exe apps, Steam or the App ID for Steam games.
/// </summary>
public class LaunchMessagesTests
{
    private static AppDefinition Exe(string name) => new() { Name = name, Path = $@"C:\{name}.exe" };
    private static AppDefinition Steam(string name, string id) => new() { Name = name, SteamAppId = id };

    [Fact]
    public void CouldNotStart_ExeAppsOnly_PointsAtPaths()
    {
        var message = LaunchMessages.CouldNotStart(new[] { "Crew Chief", "RaceLab" },
            new List<AppDefinition> { Exe("Crew Chief"), Exe("RaceLab"), Exe("Trading Paints") });

        Assert.Equal("Couldn't start: Crew Chief, RaceLab. Check their paths in Settings.", message);
    }

    [Fact]
    public void CouldNotStart_SteamGamesOnly_PointsAtSteam()
    {
        var message = LaunchMessages.CouldNotStart(new[] { "ACC" },
            new List<AppDefinition> { Steam("ACC", "805550"), Exe("Crew Chief") });

        Assert.Equal("Couldn't start: ACC. Check that Steam is installed and the Steam App ID is right.", message);
    }

    [Fact]
    public void CouldNotStart_Mixed_MentionsBoth()
    {
        var message = LaunchMessages.CouldNotStart(new[] { "Crew Chief", "ACC" },
            new List<AppDefinition> { Exe("Crew Chief"), Steam("ACC", "805550") });

        Assert.Equal(
            "Couldn't start: Crew Chief, ACC. Check their paths in Settings, and for Steam games that Steam is installed and the App ID is right.",
            message);
    }
}
