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

    [Fact]
    public void Clone_CopiesEverySettableProperty()
    {
        // Settings edits a copy so Cancel can discard changes. A property missed by
        // the copy would be silently wiped on every Save, so check them all by
        // reflection: adding a field to AppDefinition without copying it fails here.
        var original = new AppDefinition();
        var props = typeof(AppDefinition).GetProperties().Where(p => p.CanWrite).ToList();
        foreach (var p in props)
        {
            p.SetValue(original, p.PropertyType == typeof(bool) ? true : (object)$"value-{p.Name}");
        }

        var copy = original.Clone();

        Assert.NotSame(original, copy);
        Assert.All(props, p => Assert.Equal(p.GetValue(original), p.GetValue(copy)));
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

    // ---- Task 3: routing through the gateway ----

    private static ProcessService NoDelayService(FakeProcessGateway gateway) =>
        new(gateway, (_, _) => Task.CompletedTask);

    [Fact]
    public void StartApp_SteamApp_OpensSteamUrlAndNotPath()
    {
        var gateway = new FakeProcessGateway();
        var app = SteamApp("2399420", vr: true, path: @"C:\lmu.exe");
        app.Arguments = "--ignored";

        NoDelayService(gateway).StartApp(app);

        Assert.Equal(new[] { "steam://launch/2399420/vr" }, gateway.OpenedUrls);
        Assert.Empty(gateway.StartedPaths);
    }

    [Fact]
    public async Task LaunchAppsAsync_MixesSteamAndExeApps()
    {
        var gateway = new FakeProcessGateway();
        var exe = new AppDefinition { Id = "c", Name = "Crew Chief", ProcessName = "CrewChiefV4", Path = @"C:\cc.exe", Arguments = "-x" };
        var steam = SteamApp("2399420");

        var result = await NoDelayService(gateway).LaunchAppsAsync(new[] { exe, steam }, delaySeconds: 0);

        Assert.Equal(new[] { "Crew Chief", "LMU" }, result.Launched);
        Assert.Equal(new[] { @"C:\cc.exe" }, gateway.StartedPaths);
        Assert.Equal(new[] { "-x" }, gateway.StartedArguments);
        Assert.Equal(new[] { "steam://rungameid/2399420" }, gateway.OpenedUrls);
    }

    [Fact]
    public async Task LaunchAppsAsync_SteamOpenFails_ReportsFailed()
    {
        var gateway = new FakeProcessGateway { FailOpenUrls = true };
        var delays = 0;
        var service = new ProcessService(gateway, (_, _) => { delays++; return Task.CompletedTask; });

        var result = await service.LaunchAppsAsync(new[] { SteamApp("2399420") }, delaySeconds: 2);

        Assert.Equal(new[] { "LMU" }, result.Failed);
        Assert.Empty(result.Launched);
        Assert.Equal(0, delays);
    }

    [Fact]
    public async Task LaunchAppsAsync_SteamAppAlreadyRunning_IsSkipped()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("Le Mans Ultimate");

        await NoDelayService(gateway).LaunchAppsAsync(new[] { SteamApp("2399420") }, delaySeconds: 0);

        Assert.Empty(gateway.OpenedUrls);
    }

    [Fact]
    public async Task LaunchAppsAsync_InvalidSteamId_ReportsFailedInsteadOfThrowing()
    {
        // A hand-edited config.json can hold an ID the Settings validation never saw.
        var gateway = new FakeProcessGateway();

        var result = await NoDelayService(gateway).LaunchAppsAsync(new[] { SteamApp("abc") }, delaySeconds: 0);

        Assert.Equal(new[] { "LMU" }, result.Failed);
        Assert.Empty(gateway.OpenedUrls);
        Assert.Empty(gateway.StartedPaths);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("")]
    public void Win32Gateway_OpenSteamUrl_RejectsOtherSchemes(string url)
    {
        Assert.False(new Win32ProcessGateway().OpenSteamUrl(url));
    }
}
