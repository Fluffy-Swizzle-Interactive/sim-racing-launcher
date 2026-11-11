using System.IO;
using System.Linq;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

/// <summary>
/// Launching an app through Steam by its App ID, optionally in Steam's VR mode,
/// instead of by its exe path.
/// </summary>
public class SteamLaunchTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"iRacingLauncherSteamTest_{Guid.NewGuid():N}.json");

    private const string CurrentFormatWithOneApp = """
        {
          "apps": [ { "id": "x", "name": "LMU", "processName": "Le Mans Ultimate", "path": "C:\\LMU\\LMU.exe"__EXTRA__ } ],
          "profiles": [ { "name": "LMU", "appRefs": [ { "appId": "x", "selected": true } ] } ],
          "activeProfileName": "LMU"
        }
        """;

    private static AppDefinition LoadSingleApp(string extraAppJson)
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, CurrentFormatWithOneApp.Replace("__EXTRA__", extraAppJson));
            return new SettingsService(path).Load().Apps.Single();
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Task 1: model fields ----

    [Fact]
    public void SaveThenLoad_PreservesSteamAppIdAndVrFlag()
    {
        var path = TempConfigPath();
        try
        {
            var config = SettingsService.CreateDefaultConfig();
            config.Apps[0].SteamAppId = "2399420";
            config.Apps[0].LaunchInVr = true;

            new SettingsService(path).Save(config);
            var loaded = new SettingsService(path).Load().Apps.Single(a => a.Id == config.Apps[0].Id);

            Assert.Equal("2399420", loaded.SteamAppId);
            Assert.True(loaded.LaunchInVr);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenSteamKeysMissing_DefaultsToExeLaunch()
    {
        var app = LoadSingleApp("");

        Assert.Equal("", app.SteamAppId);
        Assert.False(app.LaunchInVr);
    }

    [Fact]
    public void Load_WhenSteamAppIdIsNull_TreatsAsEmpty()
    {
        var app = LoadSingleApp(", \"steamAppId\": null");

        Assert.Equal("", app.SteamAppId);
    }
}
