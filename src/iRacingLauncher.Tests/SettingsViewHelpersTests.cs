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
