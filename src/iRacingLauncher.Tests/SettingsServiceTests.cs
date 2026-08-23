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
            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateDefaultConfig_AcProfileLaunchesViaContentManager()
    {
        var config = SettingsService.CreateDefaultConfig();

        var acProfile = config.Profiles.Single(p => p.Name == "AC");
        var sim = config.ResolveApps(acProfile.AppRefs).First();

        Assert.Equal("Content Manager", sim.ProcessName);
    }

    [Fact]
    public void CreateDefaultConfig_ShipsIRacingAccAndAcProfilesWithNoPaths()
    {
        var config = SettingsService.CreateDefaultConfig();

        Assert.Equal(new[] { "iRacing", "ACC", "AC" }, config.Profiles.Select(p => p.Name));
        Assert.All(config.Apps, app => Assert.Equal(string.Empty, app.Path));

        var iRacingCompanions = config.ResolveApps(config.Profiles[0].AppRefs).Select(a => a.Name).Skip(1);
        var accCompanions = config.ResolveApps(config.Profiles[1].AppRefs).Select(a => a.Name).Skip(1);
        var acCompanions = config.ResolveApps(config.Profiles[2].AppRefs).Select(a => a.Name).Skip(1);

        // iRacing and ACC carry the same companion-tool lineup, just a different sim entry.
        Assert.Equal(iRacingCompanions, accCompanions);
        Assert.Contains("Coach David", iRacingCompanions);

        // Vanilla AC drops Coach Dave Delta, which doesn't support it.
        Assert.DoesNotContain("Coach David", acCompanions);
        Assert.Equal(acCompanions, iRacingCompanions.Where(n => n != "Coach David"));
    }

    [Fact]
    public void CreateDefaultConfig_SharesOneAppDefinitionPerCompanionAcrossProfiles()
    {
        var config = SettingsService.CreateDefaultConfig();

        // 3 sims (one per profile) + 4 distinct companions = 7 total, not 3*5=15.
        Assert.Equal(7, config.Apps.Count);
        Assert.Single(config.Apps, a => a.ProcessName == "CrewChiefV4");

        var crewChief = config.Apps.Single(a => a.ProcessName == "CrewChiefV4");
        var profilesReferencingIt = config.Profiles
            .Where(p => p.AppRefs.Any(r => r.AppId == crewChief.Id))
            .Select(p => p.Name);
        Assert.Equal(new[] { "iRacing", "ACC", "AC" }, profilesReferencingIt);
    }

    [Fact]
    public void CreateDefaultConfig_DefaultsCloseToTrayToFalse()
    {
        var config = SettingsService.CreateDefaultConfig();

        Assert.False(config.CloseToTray);
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
            config.CloseToTray = true;
            config.ActiveProfile.AppRefs[0].Selected = false;

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(7, loaded.LaunchDelaySeconds);
            Assert.True(loaded.CloseToTray);
            Assert.False(loaded.ActiveProfile.AppRefs[0].Selected);
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
            var rf2 = new AppDefinition { Id = Guid.NewGuid().ToString("N"), Name = "rFactor 2", ProcessName = "rFactor2", Path = @"C:\rf2.exe" };
            config.Apps.Add(rf2);
            config.Profiles.Add(new Profile
            {
                Name = "rFactor2",
                AppRefs = new List<ProfileAppEntry> { new() { AppId = rf2.Id, Selected = true } },
            });
            config.ActiveProfileName = "rFactor2";

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(presetCount + 1, loaded.Profiles.Count);
            Assert.Equal("rFactor2", loaded.ActiveProfileName);
            Assert.Equal("rFactor2", loaded.ActiveProfile.Name);
            Assert.Single(loaded.ActiveProfile.AppRefs);
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

            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());

            // Verify the corrupt file on disk was actually overwritten, not just
            // that Load() returns in-memory defaults regardless of disk state.
            var rawContentAfterFirstLoad = File.ReadAllText(path);
            Assert.DoesNotContain("not valid json", rawContentAfterFirstLoad);
            var reparsed = JsonSerializer.Deserialize<LauncherConfig>(rawContentAfterFirstLoad);
            Assert.NotNull(reparsed);

            var reloaded = service.Load();
            Assert.Equal(5, reloaded.ResolveApps(reloaded.ActiveProfile.AppRefs).Count());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MigratesTodaysPerProfileEmbeddedFormat()
    {
        var path = TempConfigPath();
        try
        {
            // The on-disk shape written by every build up to this branch's app-registry change.
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
            var app = Assert.Single(config.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.Equal("iRacingUI", app.ProcessName);
            Assert.Equal(@"C:\iRacing\ui\iRacingUI.exe", app.Path);
            Assert.False(config.ActiveProfile.AppRefs.Single().Selected);
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
            var app = Assert.Single(config.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.False(config.ActiveProfile.AppRefs.Single().Selected);

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
    public void Load_MigratesEmbeddedPerProfileAppsIntoASharedRegistry()
    {
        var path = TempConfigPath();
        try
        {
            // Today's on-disk shape: apps embedded per profile, no top-level "apps".
            // CrewChiefV4 appears in both profiles; only iRacing has a path.
            File.WriteAllText(path, """
                {
                  "launchDelaySeconds": 2,
                  "activeProfileName": "iRacing",
                  "profiles": [
                    {
                      "name": "iRacing",
                      "apps": [
                        { "name": "iRacing", "processName": "iRacingUI", "path": "C:\\iRacing\\ui.exe", "selected": true },
                        { "name": "CrewChiefV4", "processName": "CrewChiefV4", "path": "C:\\CrewChief.exe", "selected": false }
                      ]
                    },
                    {
                      "name": "ACC",
                      "apps": [
                        { "name": "Assetto Corsa Competizione", "processName": "AC2-Win64-Shipping", "path": "", "selected": true },
                        { "name": "CrewChiefV4", "processName": "CrewChiefV4", "path": "", "selected": true }
                      ]
                    }
                  ]
                }
                """);
            var service = new SettingsService(path);

            var config = service.Load();

            // iRacingUI and AC2-Win64-Shipping each appear in exactly one profile — stay distinct.
            Assert.Equal(3, config.Apps.Count);

            var crewChief = Assert.Single(config.Apps, a => a.ProcessName == "CrewChiefV4");
            // The iRacing-profile copy had the real path; that wins over ACC's blank one.
            Assert.Equal(@"C:\CrewChief.exe", crewChief.Path);
            Assert.False(string.IsNullOrEmpty(crewChief.Id));

            var iRacingProfile = config.Profiles.Single(p => p.Name == "iRacing");
            var accProfile = config.Profiles.Single(p => p.Name == "ACC");

            // Original per-profile order and Selected values are preserved.
            Assert.Equal(2, iRacingProfile.AppRefs.Count);
            Assert.False(iRacingProfile.AppRefs.Single(r => r.AppId == crewChief.Id).Selected);
            Assert.True(accProfile.AppRefs.Single(r => r.AppId == crewChief.Id).Selected);

            // The migrated shape is persisted so the next load reads it directly.
            var raw = File.ReadAllText(path);
            Assert.Contains("\"appRefs\"", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MigrationMergesByProcessNameCaseInsensitively()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, """
                {
                  "activeProfileName": "A",
                  "profiles": [
                    { "name": "A", "apps": [ { "name": "Tool", "processName": "MyTool", "path": "", "selected": true } ] },
                    { "name": "B", "apps": [ { "name": "Tool", "processName": "mytool", "path": "C:\\tool.exe", "selected": true } ] }
                  ]
                }
                """);
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Single(config.Apps);
            Assert.Equal(@"C:\tool.exe", config.Apps[0].Path);
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
    public void Save_LeavesNoTempFileBehindInTheConfigDirectory()
    {
        // Save swaps a temp file into place so an interrupted write can't truncate
        // the real config — but a completed save must clean up after itself rather
        // than littering the config directory.
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            service.Save(SettingsService.CreateDefaultConfig());

            var dir = Path.GetDirectoryName(path)!;
            var leftovers = Directory.GetFiles(dir, Path.GetFileName(path) + "*")
                .Where(f => !string.Equals(f, path, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(leftovers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_FullyReplacesALargerExistingConfig()
    {
        // The swap must replace the old file outright, not overlay it — otherwise a
        // shorter config could leave trailing bytes of the previous, longer one and
        // produce unparseable JSON.
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            var large = SettingsService.CreateDefaultConfig();
            service.Save(large);
            var largeLength = new FileInfo(path).Length;

            var small = SettingsService.CreateDefaultConfig();
            small.Apps.Clear();
            small.Profiles.RemoveRange(1, small.Profiles.Count - 1);
            small.Profiles[0].AppRefs.Clear();
            service.Save(small);

            Assert.True(new FileInfo(path).Length < largeLength);
            var reloaded = service.Load();
            Assert.Single(reloaded.Profiles);
            Assert.Empty(reloaded.Profiles[0].AppRefs);
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
    public void Load_WhenProfilesIsNull_FallsBackToDefaultsInsteadOfThrowing()
    {
        var path = TempConfigPath();
        try
        {
            // Embedded-per-profile shape (has "profiles" key, no top-level "apps"),
            // but "profiles" is JSON null rather than an array.
            File.WriteAllText(path, """{ "profiles": null }""");
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenAppsIsNull_FallsBackToDefaultsInsteadOfThrowing()
    {
        var path = TempConfigPath();
        try
        {
            // Current-format shape (has both "apps" and "profiles", so it deserializes
            // directly with no migration) but "apps" is JSON null rather than an array —
            // config.Apps ends up null, which must be caught here rather than crashing
            // later the first time a caller resolves a profile's apps against it.
            File.WriteAllText(path, """{ "apps": null, "profiles": [ { "name": "iRacing", "appRefs": [] } ] }""");
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.NotNull(config.Apps);
            Assert.Equal(7, config.Apps.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenEmbeddedAppHasNullProcessName_MigratesWithoutThrowing()
    {
        // The null-guard normalizes a null ProcessName to "" before it ever reaches
        // the GroupBy/dictionary-indexer merge logic, so this specific input migrates
        // successfully (no data loss) rather than needing the catch-all reset — the
        // important behavior under test is that Load() never throws.
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, """
                {
                  "activeProfileName": "iRacing",
                  "profiles": [
                    {
                      "name": "iRacing",
                      "apps": [
                        { "name": "iRacing", "processName": null, "path": "", "selected": true }
                      ]
                    }
                  ]
                }
                """);
            var service = new SettingsService(path);

            var config = service.Load();

            var app = Assert.Single(config.Apps);
            Assert.Equal(string.Empty, app.ProcessName);
            Assert.Single(config.Profiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenJsonRootIsNotAnObject_FallsBackToDefaultsInsteadOfThrowing()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, "null");
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenJsonRootIsAnArray_FallsBackToDefaultsInsteadOfThrowing()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, "[]");
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());
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
