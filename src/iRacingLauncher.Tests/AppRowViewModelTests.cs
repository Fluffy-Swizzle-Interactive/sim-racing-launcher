using iRacingLauncher.Models;
using iRacingLauncher.ViewModels;
using Xunit;

namespace iRacingLauncher.Tests;

public class AppRowViewModelTests
{
    [Fact]
    public void Selected_SurvivesTransitionToRunning()
    {
        var definition = new AppDefinition { Id = "a", Name = "A", ProcessName = "procA", Path = @"C:\a.exe" };
        var entry = new ProfileAppEntry { AppId = "a", Selected = true };
        var row = new AppRowViewModel(definition, entry) { PathValid = true };

        row.IsRunning = true;

        Assert.True(row.Selected);
    }

    [Fact]
    public void Selected_ReadsAndWritesThroughToTheProfileAppEntry()
    {
        var definition = new AppDefinition { Id = "a", Name = "A", ProcessName = "procA", Path = @"C:\a.exe" };
        var entry = new ProfileAppEntry { AppId = "a", Selected = true };
        var row = new AppRowViewModel(definition, entry);

        row.Selected = false;

        Assert.False(entry.Selected);
    }
}
