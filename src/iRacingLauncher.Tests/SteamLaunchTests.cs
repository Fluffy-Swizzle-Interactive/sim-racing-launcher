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

    // ---- Task 2: SteamLaunch helper ----

    private static AppDefinition SteamApp(string id, bool vr = false, string path = "") => new()
    {
        Id = "s", Name = "LMU", ProcessName = "Le Mans Ultimate", Path = path, SteamAppId = id, LaunchInVr = vr,
    };

    [Theory]
    [InlineData("2399420", true)]
    [InlineData(" 2399420 ", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("abc", false)]
    [InlineData("-1", false)]
    [InlineData("2399420/", false)]
    [InlineData("https://store.steampowered.com/app/2399420/", false)]
    [InlineData("２３９", false)] // full-width digits
    public void IsValidAppId(string id, bool expected)
    {
        Assert.Equal(expected, SteamLaunch.IsValidAppId(id));
    }

    [Fact]
    public void BuildUrl_VrOff_UsesRunGameId()
    {
        Assert.Equal("steam://rungameid/2399420", SteamLaunch.BuildUrl(SteamApp("2399420")));
    }

    [Fact]
    public void BuildUrl_VrOn_UsesLaunchVr()
    {
        Assert.Equal("steam://launch/2399420/vr", SteamLaunch.BuildUrl(SteamApp("2399420", vr: true)));
    }

    [Fact]
    public void BuildUrl_TrimsId()
    {
        Assert.Equal("steam://rungameid/2399420", SteamLaunch.BuildUrl(SteamApp(" 2399420 ")));
    }

    [Fact]
    public void BuildUrl_InvalidId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SteamLaunch.BuildUrl(SteamApp("2399420/../evil")));
    }

    [Fact]
    public void UsesSteam_BlankId_IsFalse()
    {
        Assert.False(SteamLaunch.UsesSteam(SteamApp("  ")));
        Assert.True(SteamLaunch.UsesSteam(SteamApp("2399420")));
    }

    [Fact]
    public void IsLaunchable_SteamAppWithNoPath_IsTrue()
    {
        Assert.True(SteamLaunch.IsLaunchable(SteamApp("2399420"), _ => false));
    }

    [Fact]
    public void IsLaunchable_ExeAppWithMissingPath_IsFalse()
    {
        Assert.False(SteamLaunch.IsLaunchable(SteamApp("", path: @"C:\missing.exe"), _ => false));
        Assert.True(SteamLaunch.IsLaunchable(SteamApp("", path: @"C:\there.exe"), p => p == @"C:\there.exe"));
    }
}
