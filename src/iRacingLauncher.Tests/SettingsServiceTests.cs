using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

public class SettingsServiceTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"iRacingLauncherTest_{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileMissing_CreatesAndReturnsDefaults()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            var config = service.Load();

            Assert.Equal(2, config.LaunchDelaySeconds);
            Assert.Equal(3, config.Profiles.Count);
            Assert.Equal("iRacing", config.ActiveProfileName);
            Assert.Equal(5, config.ActiveProfile.Apps.Count);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateDefaultConfig_ShipsIRacingAccAndAcProfilesWithNoPaths()
    {
        var config = SettingsService.CreateDefaultConfig();

        Assert.Equal(new[] { "iRacing", "ACC", "AC" }, config.Profiles.Select(p => p.Name));
        Assert.All(
            config.Profiles.SelectMany(p => p.Apps),
            app => Assert.Equal(string.Empty, app.Path));

        var iRacingCompanions = config.Profiles[0].Apps.Select(a => a.Name).Skip(1);
        var accCompanions = config.Profiles[1].Apps.Select(a => a.Name).Skip(1);
        var acCompanions = config.Profiles[2].Apps.Select(a => a.Name).Skip(1);

        // iRacing and ACC carry the same companion-tool lineup, just a different sim entry.
        Assert.Equal(iRacingCompanions, accCompanions);
        Assert.Contains("Coach David", iRacingCompanions);

        // Vanilla AC drops Coach Dave Delta, which doesn't support it.
        Assert.DoesNotContain("Coach David", acCompanions);
        Assert.Equal(acCompanions, iRacingCompanions.Where(n => n != "Coach David"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsConfig()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            var config = SettingsService.CreateDefaultConfig();
            config.LaunchDelaySeconds = 7;
            config.ActiveProfile.Apps[0].Selected = false;

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(7, loaded.LaunchDelaySeconds);
            Assert.False(loaded.ActiveProfile.Apps[0].Selected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMultipleProfilesAndActiveSelection()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            var config = SettingsService.CreateDefaultConfig();
            var presetCount = config.Profiles.Count;
            config.Profiles.Add(new Profile
            {
                Name = "rFactor2",
                Apps = new List<AppEntry>
                {
                    new() { Name = "rFactor 2", ProcessName = "rFactor2", Path = @"C:\rf2.exe", Selected = true },
                },
            });
            config.ActiveProfileName = "rFactor2";

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(presetCount + 1, loaded.Profiles.Count);
            Assert.Equal("rFactor2", loaded.ActiveProfileName);
            Assert.Equal("rFactor2", loaded.ActiveProfile.Name);
            Assert.Single(loaded.ActiveProfile.Apps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenFileIsCorruptJson_FallsBackToDefaultsAndOverwrites()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, "{ not valid json ");
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(5, config.ActiveProfile.Apps.Count);

            // Verify the corrupt file on disk was actually overwritten, not just
            // that Load() returns in-memory defaults regardless of disk state.
            var rawContentAfterFirstLoad = File.ReadAllText(path);
            Assert.DoesNotContain("not valid json", rawContentAfterFirstLoad);
            var reparsed = JsonSerializer.Deserialize<LauncherConfig>(rawContentAfterFirstLoad);
            Assert.NotNull(reparsed);

            var reloaded = service.Load();
            Assert.Equal(5, reloaded.ActiveProfile.Apps.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ReadsCamelCaseConfigInTheDocumentedOnDiskFormat()
    {
        var path = TempConfigPath();
        try
        {
            // Exactly the shape documented in the design spec's Data & Config section.
            File.WriteAllText(path, """
                {
                  "launchDelaySeconds": 9,
                  "launchAtWindowsStartup": true,
                  "theme": "Light",
                  "activeProfileName": "iRacing",
                  "profiles": [
                    {
                      "name": "iRacing",
                      "apps": [
                        {
                          "name": "iRacing",
                          "processName": "iRacingUI",
                          "path": "C:\\iRacing\\ui\\iRacingUI.exe",
                          "selected": false
                        }
                      ]
                    }
                  ]
                }
                """);
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(9, config.LaunchDelaySeconds);
            Assert.True(config.LaunchAtWindowsStartup);
            Assert.Equal("Light", config.Theme);
            var app = Assert.Single(config.ActiveProfile.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.Equal("iRacingUI", app.ProcessName);
            Assert.Equal(@"C:\iRacing\ui\iRacingUI.exe", app.Path);
            Assert.False(app.Selected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MigratesPreProfilesFlatAppsFormatIntoASingleProfile()
    {
        var path = TempConfigPath();
        try
        {
            // The on-disk shape written by every build before profiles existed.
            File.WriteAllText(path, """
                {
                  "launchDelaySeconds": 3,
                  "launchAtWindowsStartup": true,
                  "theme": "Light",
                  "apps": [
                    {
                      "name": "iRacing",
                      "processName": "iRacingUI",
                      "path": "C:\\iRacing\\ui\\iRacingUI.exe",
                      "selected": false
                    }
                  ]
                }
                """);
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(3, config.LaunchDelaySeconds);
            Assert.True(config.LaunchAtWindowsStartup);
            Assert.Equal("Light", config.Theme);
            Assert.Single(config.Profiles);
            Assert.Equal(config.Profiles[0].Name, config.ActiveProfileName);
            var app = Assert.Single(config.ActiveProfile.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.False(app.Selected);

            // The migrated shape is persisted so the next load reads it directly.
            var raw = File.ReadAllText(path);
            Assert.Contains("\"profiles\"", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_WritesCamelCaseJson()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            service.Save(SettingsService.CreateDefaultConfig());

            var raw = File.ReadAllText(path);

            Assert.Contains("\"launchDelaySeconds\"", raw);
            Assert.Contains("\"profiles\"", raw);
            Assert.Contains("\"processName\"", raw);
            Assert.DoesNotContain("\"LaunchDelaySeconds\"", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_DoesNotWriteTheDerivedActiveProfileProperty()
    {
        // ActiveProfile is computed from Profiles/ActiveProfileName — it must not
        // round-trip through JSON as a redundant, duplicated copy of the same data.
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            service.Save(SettingsService.CreateDefaultConfig());

            var raw = File.ReadAllText(path);

            Assert.DoesNotContain("\"activeProfile\"", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetDefaultConfigPath_ReturnsAppDataPathAndCreatesDirectory()
    {
        var path = SettingsService.GetDefaultConfigPath();

        var expectedSuffix = Path.Combine("iRacingLauncher", "config.json");
        Assert.EndsWith(expectedSuffix, path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }
}
