using System.Collections.Generic;
using iRacingLauncher;
using iRacingLauncher.Models;
using iRacingLauncher.ViewModels;
using Xunit;

namespace iRacingLauncher.Tests;

public class SettingsViewHelpersTests
{
    [Theory]
    [InlineData(0, 2, 0)]  // removed the first: the next one slides into its place
    [InlineData(2, 2, 1)]  // removed the last: select the new last
    [InlineData(1, 3, 1)]
    [InlineData(0, 0, -1)] // list is now empty
    public void AfterRemoval(int removedIndex, int countAfterRemoval, int expected)
    {
        Assert.Equal(expected, ListSelection.AfterRemoval(removedIndex, countAfterRemoval));
    }

    [Theory]
    [InlineData("", true, false)]
    [InlineData("805550", false, false)]
    [InlineData("805550", true, true)]
    public void ShowVrTag_NeedsSteamAndVr(string steamId, bool vr, bool expected)
    {
        var row = new AppEditRowViewModel(new AppDefinition { SteamAppId = steamId, LaunchInVr = vr });

        Assert.Equal(expected, row.ShowVrTag);
    }

    [Theory]
    [InlineData("ACC", "Apps launched for ACC, in order")]
    [InlineData("  iRacing ", "Apps launched for iRacing, in order")]
    [InlineData("", "Apps launched for this game, in order")]
    [InlineData("   ", "Apps launched for this game, in order")]
    public void ChecklistHeading_NeverShowsAnEmptyName(string name, string expected)
    {
        var row = new ProfileEditRowViewModel(new Profile { Name = name });

        Assert.Equal(expected, row.ChecklistHeading);
    }

    [Fact]
    public void ChecklistHeading_UpdatesWhenRenamed()
    {
        var row = new ProfileEditRowViewModel(new Profile { Name = "ACC" });
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.Name = "LMU";

        Assert.Contains(nameof(ProfileEditRowViewModel.ChecklistHeading), raised);
        Assert.Equal("Apps launched for LMU, in order", row.ChecklistHeading);
    }

    [Fact]
    public void IsSearching_IsPerAppAndRaisesChange()
    {
        // Auto-Find is one shared button in the details pane, so "a search is running"
        // has to live on the app, not the button: searching app A must not grey out
        // Auto-Find for app B.
        var a = new AppEditRowViewModel(new AppDefinition());
        var b = new AppEditRowViewModel(new AppDefinition());
        var raised = new List<string?>();
        a.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        a.IsSearching = true;

        Assert.True(a.IsSearching);
        Assert.False(b.IsSearching);
        Assert.Contains(nameof(AppEditRowViewModel.IsSearching), raised);
    }

    [Fact]
    public void ShowVrTag_RaisesChangeWhenIdOrVrChanges()
    {
        var row = new AppEditRowViewModel(new AppDefinition());
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.LaunchInVr = true;
        var afterVr = raised.FindAll(n => n == nameof(AppEditRowViewModel.ShowVrTag)).Count;
        row.SteamAppId = "805550";
        var afterId = raised.FindAll(n => n == nameof(AppEditRowViewModel.ShowVrTag)).Count;

        Assert.Equal(1, afterVr);
        Assert.Equal(2, afterId);
        Assert.True(row.ShowVrTag);
    }
}
