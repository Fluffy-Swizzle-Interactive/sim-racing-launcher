using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

/// <summary>
/// Per-app launch arguments, end to end: typed once in Settings, persisted in
/// config.json, and handed verbatim to the launched process. The motivating case
/// is VR users who need a flag like "--vr" or "+VR" on a sim's exe, but the same
/// path carries any switch (fullscreen, config files with spaces in the path, ...).
/// </summary>
public class LaunchArgumentsTests
{
    // Realistic shapes users type: bare flags, plus-prefixed flags, several flags,
    // and a quoted value containing spaces (e.g. a config file under Program Files).
    public static TheoryData<string> VrStyleArguments => new()
    {
        "--vr",
        "+VR",
        "-vr -fullscreen",
        "--openxr --config \"C:\\Program Files\\My Sim\\vr settings.json\"",
    };

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"iRacingLauncherArgsTest_{Guid.NewGuid():N}{extension}");

    [Theory]
    [MemberData(nameof(VrStyleArguments))]
    public void SaveThenLoad_PreservesArgumentsExactly(string arguments)
    {
        var path = TempPath(".json");
        try
        {
            var service = new SettingsService(path);
            var config = SettingsService.CreateDefaultConfig();
            config.Apps[0].Arguments = arguments;

            service.Save(config);
            var loaded = new SettingsService(path).Load();

            Assert.Equal(arguments, loaded.Apps.Single(a => a.Id == config.Apps[0].Id).Arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoad_KeepsArgumentsOnTheAppTheyBelongTo()
    {
        var path = TempPath(".json");
        try
        {
            var service = new SettingsService(path);
            var config = SettingsService.CreateDefaultConfig();
            config.Apps[0].Arguments = "--vr";
            config.Apps[2].Arguments = "-silent";

            service.Save(config);
            var loaded = new SettingsService(path).Load();

            var argsById = loaded.Apps.ToDictionary(a => a.Id, a => a.Arguments);
            Assert.Equal("--vr", argsById[config.Apps[0].Id]);
            Assert.Equal("", argsById[config.Apps[1].Id]);
            Assert.Equal("-silent", argsById[config.Apps[2].Id]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_WritesArgumentsToConfigJson()
    {
        var path = TempPath(".json");
        try
        {
            var config = SettingsService.CreateDefaultConfig();
            config.Apps[0].Arguments = "--vr";

            new SettingsService(path).Save(config);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var firstApp = doc.RootElement.GetProperty("apps")[0];
            Assert.Equal("--vr", firstApp.GetProperty("arguments").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenAppHasNoArgumentsKey_TreatsArgumentsAsEmpty()
    {
        var path = TempPath(".json");
        try
        {
            // A current-format file written before arguments existed, or hand-edited.
            File.WriteAllText(path, """
                {
                  "apps": [ { "id": "x", "name": "LMU", "processName": "Le Mans Ultimate", "path": "C:\\LMU\\LMU.exe" } ],
                  "profiles": [ { "name": "LMU", "appRefs": [ { "appId": "x", "selected": true } ] } ],
                  "activeProfileName": "LMU"
                }
                """);

            var config = new SettingsService(path).Load();

            Assert.Equal("", config.Apps.Single().Arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenArgumentsIsNull_TreatsArgumentsAsEmpty()
    {
        var path = TempPath(".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "apps": [ { "id": "x", "name": "LMU", "processName": "Le Mans Ultimate", "path": "C:\\LMU\\LMU.exe", "arguments": null } ],
                  "profiles": [ { "name": "LMU", "appRefs": [ { "appId": "x", "selected": true } ] } ],
                  "activeProfileName": "LMU"
                }
                """);

            var config = new SettingsService(path).Load();

            Assert.Equal("", config.Apps.Single().Arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MigratingALegacyConfig_GivesEveryAppEmptyArguments()
    {
        var path = TempPath(".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "activeProfileName": "iRacing",
                  "profiles": [
                    { "name": "iRacing", "apps": [ { "name": "Crew Chief", "processName": "CrewChiefV4", "path": "C:\\cc.exe", "selected": true } ] },
                    { "name": "LMU", "apps": [ { "name": "CrewChief", "processName": "crewchiefv4", "path": "", "selected": true } ] }
                  ]
                }
                """);

            var config = new SettingsService(path).Load();

            Assert.All(config.Apps, a => Assert.Equal("", a.Arguments));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StartApp_PassesTheAppsArguments()
    {
        // The per-row Start button goes through StartApp, not LaunchAppsAsync.
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway);

        service.StartApp(new AppDefinition { Id = "a", Name = "LMU", ProcessName = "LMU", Path = @"C:\LMU.exe", Arguments = "--vr" });

        Assert.Equal(new[] { "--vr" }, gateway.StartedArguments);
    }

    [Fact]
    public async Task LaunchAppsAsync_SharedAppUsesTheSameArgumentsInEveryProfile()
    {
        // Arguments live on the shared AppDefinition, so a VR flag set once applies
        // whichever game profile launches that app.
        var config = SettingsService.CreateDefaultConfig();
        var sharedId = config.Profiles[0].AppRefs
            .Select(r => r.AppId)
            .First(id => config.Profiles.Skip(1).Any(p => p.AppRefs.Any(r => r.AppId == id)));
        config.Apps.Single(a => a.Id == sharedId).Arguments = "--vr";

        foreach (var profile in config.Profiles.Where(p => p.AppRefs.Any(r => r.AppId == sharedId)))
        {
            var gateway = new FakeProcessGateway();
            var service = new ProcessService(gateway, (_, _) => Task.CompletedTask);
            var shared = config.ResolveApps(profile.AppRefs).Where(a => a.Id == sharedId);

            await service.LaunchAppsAsync(shared, delaySeconds: 0);

            Assert.Equal(new[] { "--vr" }, gateway.StartedArguments);
        }
    }

    [Theory]
    [MemberData(nameof(VrStyleArguments))]
    public void Win32Gateway_RealProcessReceivesArgumentsExactly(string arguments)
    {
        // Launch a real file through the production gateway and have it report the
        // arguments it was given. A batch script is used because it always exits on
        // its own: if a regression drops the arguments it writes an empty line and
        // quits, rather than leaving an interactive shell window open on the desktop.
        // %* is the raw argument tail, quotes included.
        var outFile = TempPath(".txt");
        var script = TempPath(".cmd");
        try
        {
            File.WriteAllText(script, $"@echo off\r\n>\"{outFile}\" echo(%*\r\n");
            var gateway = new Win32ProcessGateway();

            var started = gateway.Start(script, arguments);

            Assert.True(started);
            var sw = Stopwatch.StartNew();
            while (!File.Exists(outFile) && sw.Elapsed < TimeSpan.FromSeconds(20))
            {
                Thread.Sleep(100);
            }
            Assert.True(File.Exists(outFile), "launched process never reported its arguments");
            Thread.Sleep(200);
            Assert.Equal(arguments, File.ReadAllText(outFile).Trim());
        }
        finally
        {
            File.Delete(outFile);
            File.Delete(script);
        }
    }
}
