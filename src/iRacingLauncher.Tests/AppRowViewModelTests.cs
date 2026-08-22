using iRacingLauncher.Models;
using iRacingLauncher.ViewModels;
using Xunit;

namespace iRacingLauncher.Tests;

public class AppRowViewModelTests
{
    [Fact]
    public void Selected_SurvivesTransitionToRunning()
    {
        var model = new AppEntry { Name = "A", ProcessName = "procA", Path = @"C:\a.exe", Selected = true };
        var row = new AppRowViewModel(model) { PathValid = true };

        row.IsRunning = true;

        Assert.True(row.Selected);
    }
}
