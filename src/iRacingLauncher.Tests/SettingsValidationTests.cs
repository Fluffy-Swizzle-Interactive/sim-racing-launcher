using System.Collections.Generic;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

/// <summary>
/// Save's checks in Settings. Each problem says which tab and which game or app it
/// is about, so the window can jump there instead of pointing at something hidden.
/// </summary>
public class SettingsValidationTests
{
    private static Profile Game(string name) => new() { Name = name };
    private static AppDefinition App(string name, string steamId = "") => new() { Id = name, Name = name, SteamAppId = steamId };

    [Fact]
    public void Validate_AllFine_ReturnsNull()
    {
        var problem = SettingsValidation.Validate(
            new List<Profile> { Game("iRacing"), Game("ACC") },
            new List<AppDefinition> { App("ACC", "805550"), App("Crew Chief") });

        Assert.Null(problem);
    }

    [Fact]
    public void Validate_BlankGameName_PointsAtThatGame()
    {
        var blank = Game("  ");

        var problem = SettingsValidation.Validate(new List<Profile> { Game("iRacing"), blank }, new List<AppDefinition>());

        Assert.NotNull(problem);
        Assert.Equal("Every game needs a name. Give the blank one a name, or delete it.", problem!.Message);
        Assert.Equal(SettingsTab.Games, problem.Tab);
        Assert.Same(blank, problem.Game);
        Assert.Null(problem.App);
    }

    [Fact]
    public void Validate_DuplicateGameNames_PointsAtTheSecondOne()
    {
        var second = Game(" iracing ");

        var problem = SettingsValidation.Validate(new List<Profile> { Game("iRacing"), Game("ACC"), second }, new List<AppDefinition>());

        Assert.NotNull(problem);
        Assert.Equal("Two games are both named \"iRacing\". Game names have to be unique.", problem!.Message);
        Assert.Equal(SettingsTab.Games, problem.Tab);
        Assert.Same(second, problem.Game);
    }

    [Fact]
    public void Validate_BadSteamAppId_PointsAtThatApp()
    {
        var acc = App("ACC", "abc");

        var problem = SettingsValidation.Validate(new List<Profile> { Game("iRacing") }, new List<AppDefinition> { App("Crew Chief"), acc });

        Assert.NotNull(problem);
        Assert.Equal("The Steam App ID for \"ACC\" should be just the number, like 2399420.", problem!.Message);
        Assert.Equal(SettingsTab.Apps, problem.Tab);
        Assert.Same(acc, problem.App);
        Assert.Null(problem.Game);
    }

    [Fact]
    public void Validate_GameProblemWinsOverAppProblem()
    {
        var problem = SettingsValidation.Validate(new List<Profile> { Game("") }, new List<AppDefinition> { App("ACC", "abc") });

        Assert.Equal(SettingsTab.Games, problem!.Tab);
    }
}
