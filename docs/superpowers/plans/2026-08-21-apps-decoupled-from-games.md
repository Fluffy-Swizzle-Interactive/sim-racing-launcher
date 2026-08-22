# Apps Decoupled From Games Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-profile embedded app copies with a single shared app registry (name, process name, path, launch arguments) that each profile references with its own selection and launch order.

**Architecture:** `LauncherConfig.Apps` becomes a flat `List<AppDefinition>` (the registry). `Profile.AppRefs` becomes an ordered `List<ProfileAppEntry>` of `{ AppId, Selected }` pointing into that registry — order is still per-profile, so per-game launch order is unchanged. `AppEntry` is deleted; `AppRowViewModel` and the Settings UI work from the joined `(AppDefinition, ProfileAppEntry)` pair. A one-time migration on load merges today's duplicated per-profile app entries into the registry by `ProcessName`.

**Tech Stack:** C# / .NET (net10.0-windows), WPF, WPF-UI (`Wpf.Ui`), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-apps-decoupled-from-games-design.md`

## Global Constraints

- JSON on disk stays camelCase (`JsonNamingPolicy.CamelCase`), read back case-insensitively — matches existing `SettingsService.SerializerOptions`.
- `AppDefinition.Id` is a GUID string (`Guid.NewGuid().ToString("N")`), assigned at creation/migration, never shown in any UI field.
- Migration merge key is `ProcessName`, compared case-insensitively (`StringComparer.OrdinalIgnoreCase`).
- When a `ProcessName` group has more than one non-empty `Path` across profiles, the first one encountered (in profile order, then per-profile app order) wins — this only actually arises from hand-edited configs; the shipped defaults and the real-world config we inspected never have more than one non-empty path per group.
- A newly-added app defaults to `Selected = true` in every existing profile. A newly-added profile starts with every existing app `Selected = true`. (Both match today's "Add App" default of `Selected = true`.)
- Per-profile launch order stays per-profile (no shared/global order).
- `IProcessGateway.Start` takes `(string exePath, string arguments)`; `arguments` is passed to `ProcessStartInfo.Arguments` verbatim — no custom quoting/escaping.

---

## Task 1: Add `AppDefinition` and `ProfileAppEntry` models; update `Profile` and `LauncherConfig`

**Files:**
- Create: `src/iRacingLauncher/Models/AppDefinition.cs`
- Create: `src/iRacingLauncher/Models/ProfileAppEntry.cs`
- Modify: `src/iRacingLauncher/Models/Profile.cs`
- Modify: `src/iRacingLauncher/Models/LauncherConfig.cs`

**Interfaces:**
- Produces: `AppDefinition { Id, Name, ProcessName, Path, Arguments }`, `ProfileAppEntry { AppId, Selected }`, `Profile.AppRefs : List<ProfileAppEntry>`, `LauncherConfig.Apps : List<AppDefinition>`, `LauncherConfig.ResolveApps(IEnumerable<ProfileAppEntry>) : IEnumerable<AppDefinition>`.

This task intentionally breaks the build — every other file that references `AppEntry`, `Profile.Apps`, or `LauncherConfig` without `Apps` will fail to compile until later tasks fix them. That's expected; there is no dedicated test for these plain data classes (matching the existing convention — `Profile.cs` and the old `AppEntry.cs` have never had dedicated tests either, only exercised indirectly through `SettingsServiceTests`/`ProcessServiceTests`, which Tasks 2–6 update).

- [ ] **Step 1: Create `AppDefinition`**

```csharp
namespace iRacingLauncher.Models;

public class AppDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create `ProfileAppEntry`**

```csharp
namespace iRacingLauncher.Models;

public class ProfileAppEntry
{
    public string AppId { get; set; } = string.Empty;
    public bool Selected { get; set; } = true;
}
```

- [ ] **Step 3: Update `Profile.cs`**

Replace the whole file:

```csharp
using System.Collections.Generic;

namespace iRacingLauncher.Models;

public class Profile
{
    public string Name { get; set; } = string.Empty;
    public List<ProfileAppEntry> AppRefs { get; set; } = new();
}
```

- [ ] **Step 4: Update `LauncherConfig.cs`**

Add `Apps` and a `ResolveApps` helper. Replace the whole file:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace iRacingLauncher.Models;

public class LauncherConfig
{
    public int LaunchDelaySeconds { get; set; } = 2;
    public bool LaunchAtWindowsStartup { get; set; } = false;
    public bool CloseToTray { get; set; } = false;
    public string Theme { get; set; } = "Dark";
    public List<AppDefinition> Apps { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public string ActiveProfileName { get; set; } = string.Empty;

    /// <summary>
    /// The profile currently selected for launching. Falls back to the first profile
    /// when ActiveProfileName doesn't match any (e.g. its profile was deleted).
    /// Callers must ensure Profiles is never empty. Derived from Profiles/
    /// ActiveProfileName, so it must not round-trip through JSON itself.
    /// </summary>
    [JsonIgnore]
    public Profile ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfileName) ?? Profiles[0];

    /// <summary>Joins a profile's AppRefs against the registry, in the refs' order.</summary>
    public IEnumerable<AppDefinition> ResolveApps(IEnumerable<ProfileAppEntry> refs) =>
        refs.Select(r => Apps.First(a => a.Id == r.AppId));
}
```

- [ ] **Step 5: Confirm the build fails for the expected reason**

Run: `dotnet build iRacingLauncher.sln`
Expected: FAIL — errors about `AppEntry` not found, `Profile.Apps` not found, etc., in `SettingsService.cs`, `ProcessService.cs`, `AppRowViewModel.cs`, `MainWindow.xaml.cs`, `SettingsWindow.xaml.cs`, and the test project. This confirms Task 1's new shape compiles on its own; later tasks fix each dependent file.

- [ ] **Step 6: Commit**

```bash
git add src/iRacingLauncher/Models/AppDefinition.cs src/iRacingLauncher/Models/ProfileAppEntry.cs src/iRacingLauncher/Models/Profile.cs src/iRacingLauncher/Models/LauncherConfig.cs
git commit -m "Add AppDefinition/ProfileAppEntry models, decoupling apps from profiles"
```

---

## Task 2: Rewrite `SettingsService.CreateDefaultConfig()` for the new shape

**Files:**
- Modify: `src/iRacingLauncher/Services/SettingsService.cs` (only `CreateDefaultConfig` and the four `static` helper methods below it in this task — migration logic is Task 3)
- Modify: `src/iRacingLauncher.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: `AppDefinition`, `ProfileAppEntry`, `Profile.AppRefs`, `LauncherConfig.Apps` (Task 1).
- Produces: `SettingsService.CreateDefaultConfig()` returning a `LauncherConfig` with a populated `Apps` registry and profiles that reference it via `AppRefs`.

The default config ships 3 profiles (iRacing, ACC, AC) sharing 4 companion apps (CrewChiefV4, TradingPaints, RaceLab, CoachDave — the last only on iRacing/ACC) plus one sim app per profile. In the new shape that's 3 sim `AppDefinition`s + 4 companion `AppDefinition`s = 7 total, each profile referencing the ones that apply to it.

- [ ] **Step 1: Write the failing tests — replace the existing default-config assertions**

`CreateDefaultConfig_AcProfileLaunchesViaContentManager` and `CreateDefaultConfig_ShipsIRacingAccAndAcProfilesWithNoPaths` currently read `.Apps` off each profile directly. Replace both with versions that resolve through the registry. In `SettingsServiceTests.cs`, replace:

```csharp
    [Fact]
    public void CreateDefaultConfig_AcProfileLaunchesViaContentManager()
    {
        var config = SettingsService.CreateDefaultConfig();

        var acProfile = config.Profiles.Single(p => p.Name == "AC");
        var sim = acProfile.Apps[0];

        Assert.Equal("Content Manager", sim.ProcessName);
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
```

with:

```csharp
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
```

Also update the imports at the top of the file — add `using System.Linq;` if not already present (it already is, per the existing file).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~CreateDefaultConfig"`
Expected: compile errors (`Profile.Apps`/`AppRowViewModel` etc. don't exist yet elsewhere in the file) or, once other call sites are stubbed, FAIL on the new assertions — `CreateDefaultConfig` still returns the old shape. Don't worry about unrelated compile errors from other tests in this file yet; Tasks 3–6 fix those. If the whole test project won't compile because of untouched tests elsewhere in this same file, that's expected until Task 3 finishes this file's other tests.

- [ ] **Step 3: Rewrite `CreateDefaultConfig` in `SettingsService.cs`**

Replace the `CreateDefaultConfig` method and its local `static` helpers:

```csharp
    public static LauncherConfig CreateDefaultConfig()
    {
        var iRacingSim = Sim("iRacing", "iRacingUI");
        var accSim = Sim("Assetto Corsa Competizione", "AC2-Win64-Shipping");
        var acSim = Sim("Assetto Corsa (Content Manager)", "Content Manager");
        var crewChief = CrewChief();
        var tradingPaints = TradingPaints();
        var raceLab = RaceLab();
        var coachDave = CoachDave();

        const string defaultProfileName = "iRacing";
        return new LauncherConfig
        {
            LaunchDelaySeconds = 2,
            LaunchAtWindowsStartup = false,
            Theme = "Dark",
            ActiveProfileName = defaultProfileName,
            Apps = new List<AppDefinition> { iRacingSim, accSim, acSim, crewChief, tradingPaints, raceLab, coachDave },
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "iRacing",
                    AppRefs = Refs(iRacingSim, crewChief, tradingPaints, raceLab, coachDave),
                },
                new()
                {
                    // ACC shares iRacing's companion-tool lineup — all four support it.
                    Name = "ACC",
                    AppRefs = Refs(accSim, crewChief, tradingPaints, raceLab, coachDave),
                },
                new()
                {
                    // Vanilla AC has no Coach Dave Delta support, unlike iRacing/ACC.
                    Name = "AC",
                    AppRefs = Refs(acSim, crewChief, tradingPaints, raceLab),
                },
            },
        };

        static AppDefinition Sim(string name, string processName) => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = name, ProcessName = processName, Path = "", Arguments = "",
        };
        static AppDefinition CrewChief() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "CrewChiefV4", ProcessName = "CrewChiefV4", Path = "", Arguments = "",
        };
        static AppDefinition TradingPaints() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "TradingPaints", ProcessName = "Trading Paints", Path = "", Arguments = "",
        };
        static AppDefinition RaceLab() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "RaceLab", ProcessName = "RacelabApps", Path = "", Arguments = "",
        };
        static AppDefinition CoachDave() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "Coach David", ProcessName = "Coach Dave Delta", Path = "", Arguments = "",
        };
        static List<ProfileAppEntry> Refs(params AppDefinition[] apps) =>
            apps.Select(a => new ProfileAppEntry { AppId = a.Id, Selected = true }).ToList();
    }
```

Also update the legacy shape's helper (`LegacyLauncherConfig`) — leave it alone for now; it still deserializes into `List<AppEntry>`, which won't compile until Task 3 changes `MigrateLegacyFormat`. Task 3 handles that file's remaining compile errors together with the new migration logic, since they're the same method.

- [ ] **Step 4: Run the new/updated default-config tests**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~CreateDefaultConfig"`
Expected: still won't fully build (Task 3's migration code and other tests in the same files reference the old shape) — that's fine; move directly to Task 3, which finishes this file. Do not commit yet.

---

## Task 3: Migration logic for legacy configs

**Files:**
- Modify: `src/iRacingLauncher/Services/SettingsService.cs` (`Load`, `MigrateLegacyFormat`, `LegacyLauncherConfig`)
- Modify: `src/iRacingLauncher.Tests/SettingsServiceTests.cs` (remaining tests that reference `.Apps`)

**Interfaces:**
- Consumes: `AppDefinition`, `ProfileAppEntry`, `LauncherConfig.Apps`, `LauncherConfig.ResolveApps` (Task 1); `CreateDefaultConfig` (Task 2).
- Produces: `SettingsService.Load()` transparently migrating both the pre-profiles flat format and today's per-profile-embedded-apps format into the registry shape, re-saving the migrated config immediately.

Today's on-disk format (before this branch's architectural change ships) has `profiles: [{ name, apps: [{ name, processName, path, selected }] }]` — apps embedded per profile, no top-level `apps` array. That is now itself a legacy shape needing migration, in addition to the already-handled pre-`profiles` flat format. Both funnel into one merge step.

- [ ] **Step 1: Write the failing migration tests**

Add to `SettingsServiceTests.cs` (after the existing `Load_MigratesPreProfilesFlatAppsFormatIntoASingleProfile` test):

```csharp
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
```

- [ ] **Step 2: Update the remaining pre-existing tests in `SettingsServiceTests.cs` that reference the old shape**

These currently fail to compile because `Profile.Apps` and `AppEntry` no longer exist. Replace each:

`Load_WhenFileMissing_CreatesAndReturnsDefaults` — change:
```csharp
            Assert.Equal(5, config.ActiveProfile.Apps.Count);
```
to:
```csharp
            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());
```

`SaveThenLoad_RoundTripsConfig` — change:
```csharp
            config.ActiveProfile.Apps[0].Selected = false;

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(7, loaded.LaunchDelaySeconds);
            Assert.True(loaded.CloseToTray);
            Assert.False(loaded.ActiveProfile.Apps[0].Selected);
```
to:
```csharp
            config.ActiveProfile.AppRefs[0].Selected = false;

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(7, loaded.LaunchDelaySeconds);
            Assert.True(loaded.CloseToTray);
            Assert.False(loaded.ActiveProfile.AppRefs[0].Selected);
```

`SaveThenLoad_RoundTripsMultipleProfilesAndActiveSelection` — change:
```csharp
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
```
to:
```csharp
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
```

`Load_WhenFileIsCorruptJson_FallsBackToDefaultsAndOverwrites` — change both occurrences of:
```csharp
            Assert.Equal(5, config.ActiveProfile.Apps.Count);
```
/
```csharp
            Assert.Equal(5, reloaded.ActiveProfile.Apps.Count);
```
to:
```csharp
            Assert.Equal(5, config.ResolveApps(config.ActiveProfile.AppRefs).Count());
```
/
```csharp
            Assert.Equal(5, reloaded.ResolveApps(reloaded.ActiveProfile.AppRefs).Count());
```

`Load_ReadsCamelCaseConfigInTheDocumentedOnDiskFormat` — this test's fixture JSON still uses the current on-disk shape (embedded `apps` under one profile with no top-level `apps` array), which after this task becomes a migration input, not the steady-state format. Change its assertions from:
```csharp
            var app = Assert.Single(config.ActiveProfile.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.Equal("iRacingUI", app.ProcessName);
            Assert.Equal(@"C:\iRacing\ui\iRacingUI.exe", app.Path);
            Assert.False(app.Selected);
```
to:
```csharp
            var app = Assert.Single(config.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.Equal("iRacingUI", app.ProcessName);
            Assert.Equal(@"C:\iRacing\ui\iRacingUI.exe", app.Path);
            Assert.False(config.ActiveProfile.AppRefs.Single().Selected);
```
Also update its doc comment — the shape it writes is no longer "the documented on-disk format" but a migration input. Rename the test to `Load_MigratesTodaysPerProfileEmbeddedFormat` and change the leading comment to:
```csharp
            // The on-disk shape written by every build up to this branch's app-registry change.
```

`Load_MigratesPreProfilesFlatAppsFormatIntoASingleProfile` — change:
```csharp
            var app = Assert.Single(config.ActiveProfile.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.False(app.Selected);
```
to:
```csharp
            var app = Assert.Single(config.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.False(config.ActiveProfile.AppRefs.Single().Selected);
```

`Save_DoesNotWriteTheDerivedActiveProfileProperty` and `Save_WritesCamelCaseJson` need no changes — they don't touch `.Apps`.

- [ ] **Step 3: Run tests to verify they fail for the expected reason**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~SettingsServiceTests"`
Expected: the project now compiles (assuming Tasks 4–6 haven't been reverted), and the two new migration tests FAIL — `Load` doesn't merge embedded apps into a registry yet, it still expects a top-level `apps` array to be the only "new" format.

- [ ] **Step 4: Rewrite `Load`, `MigrateLegacyFormat`, and `LegacyLauncherConfig` in `SettingsService.cs`**

Replace from `Load()` through the end of `MigrateLegacyFormat`:

```csharp
    public LauncherConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return ResetToDefaults();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            // A current-format file has a top-level "apps" registry array. Anything
            // else — the pre-profiles flat-apps shape, or today's per-profile
            // embedded-apps shape — needs migrating into that registry.
            var isCurrentFormat = HasProperty(doc.RootElement, "apps");
            var needsMigration = !isCurrentFormat;

            LauncherConfig? config;
            if (isCurrentFormat)
            {
                config = JsonSerializer.Deserialize<LauncherConfig>(json, SerializerOptions);
            }
            else if (!HasProperty(doc.RootElement, "profiles"))
            {
                config = MigrateFlatAppsFormat(json);
            }
            else
            {
                config = MigrateEmbeddedPerProfileAppsFormat(json);
            }

            if (config is null || config.Profiles is null || config.Profiles.Count == 0)
            {
                return ResetToDefaults();
            }

            if (!config.Profiles.Any(p => p.Name == config.ActiveProfileName))
            {
                config.ActiveProfileName = config.Profiles[0].Name;
            }

            if (needsMigration)
            {
                // Persist the migrated shape now so future loads read the new
                // format directly instead of re-migrating every startup.
                TrySave(config);
            }

            return config;
        }
        catch (JsonException)
        {
            return ResetToDefaults();
        }
        catch (IOException)
        {
            // Locked or otherwise unreadable file.
            return ResetToDefaults();
        }
        catch (UnauthorizedAccessException)
        {
            return ResetToDefaults();
        }
    }

    private static bool HasProperty(JsonElement root, string name) =>
        root.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pre-profiles shape: a single flat "apps" array, no "profiles" key at all.</summary>
    private static LauncherConfig? MigrateFlatAppsFormat(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyFlatConfig>(json, SerializerOptions);
        if (legacy is null)
        {
            return null;
        }

        const string defaultProfileName = "Default";
        return MergeEmbeddedApps(
            legacy.LaunchDelaySeconds,
            legacy.LaunchAtWindowsStartup,
            closeToTray: false,
            legacy.Theme,
            defaultProfileName,
            new List<LegacyProfile> { new() { Name = defaultProfileName, Apps = legacy.Apps } });
    }

    /// <summary>Today's shape: "profiles" with each profile's apps embedded directly.</summary>
    private static LauncherConfig? MigrateEmbeddedPerProfileAppsFormat(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyProfilesConfig>(json, SerializerOptions);
        if (legacy is null)
        {
            return null;
        }

        return MergeEmbeddedApps(
            legacy.LaunchDelaySeconds,
            legacy.LaunchAtWindowsStartup,
            legacy.CloseToTray,
            legacy.Theme,
            legacy.ActiveProfileName,
            legacy.Profiles);
    }

    /// <summary>
    /// Groups every embedded app across all legacy profiles by ProcessName
    /// (case-insensitive), creating one AppDefinition per group — Path/Name/
    /// Arguments taken from the first group member with a non-empty Path, falling
    /// back to the first member overall. Each profile gets an AppRefs list
    /// preserving its original per-app order and Selected value.
    /// </summary>
    private static LauncherConfig MergeEmbeddedApps(
        int launchDelaySeconds,
        bool launchAtWindowsStartup,
        bool closeToTray,
        string theme,
        string activeProfileName,
        List<LegacyProfile> legacyProfiles)
    {
        var groups = legacyProfiles
            .SelectMany(p => p.Apps)
            .GroupBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appsByProcessName = new Dictionary<string, AppDefinition>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AppDefinition>();
        foreach (var group in groups)
        {
            var winner = group.FirstOrDefault(a => !string.IsNullOrEmpty(a.Path)) ?? group.First();
            var definition = new AppDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = winner.Name,
                ProcessName = group.First().ProcessName,
                Path = winner.Path,
                Arguments = string.Empty,
            };
            apps.Add(definition);
            appsByProcessName[group.Key] = definition;
        }

        var profiles = legacyProfiles.Select(p => new Profile
        {
            Name = p.Name,
            AppRefs = p.Apps
                .Select(a => new ProfileAppEntry { AppId = appsByProcessName[a.ProcessName].Id, Selected = a.Selected })
                .ToList(),
        }).ToList();

        return new LauncherConfig
        {
            LaunchDelaySeconds = launchDelaySeconds,
            LaunchAtWindowsStartup = launchAtWindowsStartup,
            CloseToTray = closeToTray,
            Theme = theme,
            ActiveProfileName = activeProfileName,
            Apps = apps,
            Profiles = profiles,
        };
    }
```

- [ ] **Step 5: Replace `LegacyLauncherConfig` at the bottom of the file**

Replace:

```csharp
    /// <summary>Shape of config.json before profiles existed — a single flat app list.</summary>
    private class LegacyLauncherConfig
    {
        public int LaunchDelaySeconds { get; set; } = 2;
        public bool LaunchAtWindowsStartup { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public List<AppEntry> Apps { get; set; } = new();
    }
```

with:

```csharp
    /// <summary>An embedded app entry as it appears in either legacy on-disk shape.</summary>
    private class LegacyAppEntry
    {
        public string Name { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool Selected { get; set; } = true;
    }

    private class LegacyProfile
    {
        public string Name { get; set; } = string.Empty;
        public List<LegacyAppEntry> Apps { get; set; } = new();
    }

    /// <summary>Shape of config.json before profiles existed — a single flat app list.</summary>
    private class LegacyFlatConfig
    {
        public int LaunchDelaySeconds { get; set; } = 2;
        public bool LaunchAtWindowsStartup { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public List<LegacyAppEntry> Apps { get; set; } = new();
    }

    /// <summary>Shape of config.json with profiles but apps still embedded per profile.</summary>
    private class LegacyProfilesConfig
    {
        public int LaunchDelaySeconds { get; set; } = 2;
        public bool LaunchAtWindowsStartup { get; set; } = false;
        public bool CloseToTray { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public string ActiveProfileName { get; set; } = string.Empty;
        public List<LegacyProfile> Profiles { get; set; } = new();
    }
```

- [ ] **Step 6: Run the full `SettingsServiceTests` suite**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~SettingsServiceTests"`
Expected: PASS — all tests in this file, including the two new migration tests.

- [ ] **Step 7: Commit**

```bash
git add src/iRacingLauncher/Services/SettingsService.cs src/iRacingLauncher.Tests/SettingsServiceTests.cs
git commit -m "Migrate config.json onto the shared app registry shape"
```

---

## Task 4: `IProcessGateway`/`Win32ProcessGateway` — add launch arguments

**Files:**
- Modify: `src/iRacingLauncher/Services/IProcessGateway.cs`
- Modify: `src/iRacingLauncher/Services/Win32ProcessGateway.cs`
- Modify: `src/iRacingLauncher.Tests/ProcessServiceTests.cs` (only the `FakeProcessGateway` test double in this task)

**Interfaces:**
- Produces: `IProcessGateway.Start(string exePath, string arguments) : bool`.

- [ ] **Step 1: Write the failing test — update `FakeProcessGateway`**

In `ProcessServiceTests.cs`, replace the `FakeProcessGateway` class:

```csharp
public class FakeProcessGateway : IProcessGateway
{
    public HashSet<string> RunningProcessNames { get; } = new();
    public List<string> StartedPaths { get; } = new();
    public List<string> StartedArguments { get; } = new();
    public List<string> KilledProcessNames { get; } = new();

    /// <summary>Paths for which Start() reports failure, simulating a missing/invalid exe.</summary>
    public HashSet<string> FailingPaths { get; } = new();

    public bool IsRunning(string processName) => RunningProcessNames.Contains(processName);

    public bool Start(string exePath, string arguments)
    {
        StartedPaths.Add(exePath);
        StartedArguments.Add(arguments);
        return !FailingPaths.Contains(exePath);
    }

    public void Kill(string processName) => KilledProcessNames.Add(processName);
}
```

This alone won't compile yet — `ProcessService` (Task 5) still calls the old one-argument `Start`. That's expected; Tasks 4 and 5 land together before the next full build check.

- [ ] **Step 2: Update `IProcessGateway.cs`**

Replace the whole file:

```csharp
namespace iRacingLauncher.Services;

public interface IProcessGateway
{
    bool IsRunning(string processName);

    /// <summary>
    /// Attempts to start the executable at <paramref name="exePath"/> with the given
    /// command-line <paramref name="arguments"/> (pass "" for none). Returns true if
    /// the process was started, false if it could not be launched for any reason
    /// (missing/blank path, UAC declined, etc.). Implementations must not throw for
    /// an unlaunchable path.
    /// </summary>
    bool Start(string exePath, string arguments);
    void Kill(string processName);
}
```

- [ ] **Step 3: Update `Win32ProcessGateway.cs`**

Replace the `Start` method:

```csharp
    public bool Start(string exePath, string arguments)
    {
        // A blank path throws InvalidOperationException and a missing file throws
        // Win32Exception, so screen both out before asking the shell to launch.
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Minimized,
                UseShellExecute = true,
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // File vanished between the check and the call, no shell association,
            // or the user declined the UAC elevation prompt.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
```

(`ProcessStartInfo.Arguments` defaults to `""` when unset, so passing through an empty string behaves exactly like today's no-arguments launch.)

- [ ] **Step 4: Commit**

Task 4 alone doesn't build (Task 5 is its other half) — stage and commit together with Task 5 instead of separately. Move directly to Task 5.

---

## Task 5: `ProcessService` works on `AppDefinition`

**Files:**
- Modify: `src/iRacingLauncher/Services/ProcessService.cs`
- Modify: `src/iRacingLauncher.Tests/ProcessServiceTests.cs`

**Interfaces:**
- Consumes: `AppDefinition` (Task 1), `IProcessGateway.Start(exePath, arguments)` (Task 4).
- Produces: `ProcessService.IsRunning(AppDefinition)`, `StartApp(AppDefinition)`, `StopApp(AppDefinition)`, `StopAll(IEnumerable<AppDefinition>)`, `LaunchAppsAsync(IEnumerable<AppDefinition>, int delaySeconds, IProgress<(int,int)>? progress = null) : Task<LaunchResult>`. `Selected` filtering is no longer `ProcessService`'s responsibility — callers (Task 7) pass an already-filtered list to `LaunchAppsAsync` and the full profile list to `StopAll`.

`LaunchSelectedAsync` is renamed to `LaunchAppsAsync` since it no longer filters by `Selected` itself — the old name would now be misleading.

- [ ] **Step 1: Rewrite `ProcessServiceTests.cs`**

Replace the whole `ProcessServiceTests` class (keep the updated `FakeProcessGateway` from Task 4 above it):

```csharp
public class ProcessServiceTests
{
    private static List<AppDefinition> ThreeApps() => new()
    {
        new AppDefinition { Id = "a", Name = "A", ProcessName = "procA", Path = @"C:\a.exe" },
        new AppDefinition { Id = "b", Name = "B", ProcessName = "procB", Path = @"C:\b.exe" },
        new AppDefinition { Id = "c", Name = "C", ProcessName = "procC", Path = @"C:\c.exe" },
    };

    [Fact]
    public async Task LaunchAppsAsync_SkipsAlreadyRunningApps()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procB");
        var delays = new List<int>();
        var service = new ProcessService(gateway, seconds => { delays.Add(seconds); return Task.CompletedTask; });

        var result = await service.LaunchAppsAsync(ThreeApps(), delaySeconds: 2);

        Assert.Equal(new[] { "A", "C" }, result.Launched);
        Assert.Equal(new[] { @"C:\a.exe", @"C:\c.exe" }, gateway.StartedPaths);
    }

    [Fact]
    public async Task LaunchAppsAsync_PassesEachAppsArgumentsToTheGateway()
    {
        var apps = ThreeApps();
        apps[0].Arguments = "--vr";
        apps[1].Arguments = "";
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway, _ => Task.CompletedTask);

        await service.LaunchAppsAsync(apps, delaySeconds: 2);

        Assert.Equal(new[] { "--vr", "", "" }, gateway.StartedArguments);
    }

    [Fact]
    public async Task LaunchAppsAsync_DelaysAfterEachStart()
    {
        var gateway = new FakeProcessGateway();
        var delayCalls = new List<int>();
        var service = new ProcessService(gateway, seconds => { delayCalls.Add(seconds); return Task.CompletedTask; });

        await service.LaunchAppsAsync(ThreeApps(), delaySeconds: 3);

        Assert.Equal(new[] { 3, 3, 3 }, delayCalls);
    }

    [Fact]
    public async Task LaunchAppsAsync_ContinuesBatchWhenOneAppFailsToStart()
    {
        // Spec contract: "one bad path doesn't block the batch".
        var gateway = new FakeProcessGateway();
        gateway.FailingPaths.Add(@"C:\b.exe");
        var delays = new List<int>();
        var service = new ProcessService(gateway, seconds => { delays.Add(seconds); return Task.CompletedTask; });

        var result = await service.LaunchAppsAsync(ThreeApps(), delaySeconds: 2);

        // The app that failed to start is not reported as launched...
        Assert.Equal(new[] { "A", "C" }, result.Launched);
        // ...but is reported as failed, so the caller can surface it to the user.
        Assert.Equal(new[] { "B" }, result.Failed);
        // ...and the batch kept going: C was still attempted after B failed.
        Assert.Equal(new[] { @"C:\a.exe", @"C:\b.exe", @"C:\c.exe" }, gateway.StartedPaths);
        // And no stagger delay is burned waiting on an app that never started.
        Assert.Equal(new[] { 2, 2 }, delays);
    }

    [Fact]
    public async Task LaunchAppsAsync_AlreadyRunningAppsAreNeitherLaunchedNorFailed()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procB");
        var service = new ProcessService(gateway, _ => Task.CompletedTask);

        var result = await service.LaunchAppsAsync(ThreeApps(), delaySeconds: 2);

        Assert.DoesNotContain("B", result.Launched);
        Assert.DoesNotContain("B", result.Failed);
    }

    /// <summary>
    /// Reports synchronously, unlike System.Progress&lt;T&gt; (which marshals via
    /// SynchronizationContext.Post — falling back to the ThreadPool with no context
    /// installed, as in a test host, which races a plain List across threads). This
    /// double isolates the test to "was Report called with the right values in the
    /// right order", not incidental async-marshaling behavior.
    /// </summary>
    private class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public SyncProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }

    [Fact]
    public async Task LaunchAppsAsync_ReportsProgressAcrossTheWholeBatch()
    {
        // Progress tracks position through the batch passed in (including apps
        // skipped as already-running) — this is what drives the "Launching N of
        // M..." UI during a staggered batch.
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procB");
        var service = new ProcessService(gateway, _ => Task.CompletedTask);
        var reported = new List<(int Current, int Total)>();
        var progress = new SyncProgress<(int, int)>(p => reported.Add(p));

        await service.LaunchAppsAsync(ThreeApps(), delaySeconds: 2, progress);

        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, reported);
    }

    [Fact]
    public void StopAll_StopsOnlyRunningApps()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procA");
        gateway.RunningProcessNames.Add("procC");
        var service = new ProcessService(gateway);

        var stopped = service.StopAll(ThreeApps());

        Assert.Equal(new[] { "A", "C" }, stopped);
        Assert.Equal(new[] { "procA", "procC" }, gateway.KilledProcessNames);
    }

    [Fact]
    public void StopApp_KillsByProcessName()
    {
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway);

        service.StopApp(new AppDefinition { Id = "a", Name = "A", ProcessName = "procA", Path = @"C:\a.exe" });

        Assert.Contains("procA", gateway.KilledProcessNames);
    }

    [Fact]
    public void IsRunning_ReturnsTrueWhenGatewayReportsRunning()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procA");
        var service = new ProcessService(gateway);

        var result = service.IsRunning(new AppDefinition { Id = "a", Name = "A", ProcessName = "procA", Path = @"C:\a.exe" });

        Assert.True(result);
    }

    [Fact]
    public void StartApp_StartsByPath()
    {
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway);

        service.StartApp(new AppDefinition { Id = "a", Name = "A", ProcessName = "procA", Path = @"C:\a.exe" });

        Assert.Contains(@"C:\a.exe", gateway.StartedPaths);
    }
}
```

Note what's gone: `LaunchAppsAsync_SkipsUnselectedApps` and `StopAll_IgnoresSelectedState` are removed rather than adapted — `AppDefinition` has no `Selected` field, so "selected" filtering is now entirely the caller's job (Task 7 tests that at the call-site level instead).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~ProcessServiceTests"`
Expected: compile errors — `ProcessService` still has the old `AppEntry`-based, `Selected`-filtering API.

- [ ] **Step 3: Rewrite `ProcessService.cs`**

Replace the whole file:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using iRacingLauncher.Models;

namespace iRacingLauncher.Services;

public class ProcessService
{
    private readonly IProcessGateway _gateway;
    private readonly Func<int, Task> _delay;

    public ProcessService(IProcessGateway gateway, Func<int, Task>? delay = null)
    {
        _gateway = gateway;
        _delay = delay ?? (seconds => Task.Delay(TimeSpan.FromSeconds(seconds)));
    }

    public bool IsRunning(AppDefinition app) => _gateway.IsRunning(app.ProcessName);

    /// <summary>
    /// Starts a single app. The gateway's success/failure result is discarded here:
    /// callers refresh statuses afterwards, which correctly leaves a failed launch
    /// showing as "Path not found" / "Not running".
    /// </summary>
    public void StartApp(AppDefinition app) => _gateway.Start(app.Path, app.Arguments);

    public void StopApp(AppDefinition app) => _gateway.Kill(app.ProcessName);

    /// <summary>
    /// Stops every currently-running app in <paramref name="apps"/> — pass the
    /// profile's full app list, not just the selected ones: "Stop All" means
    /// everything actually running, not just whatever's checked for the next launch.
    /// </summary>
    /// <returns>The names of the apps that were actually running and got stopped.</returns>
    public List<string> StopAll(IEnumerable<AppDefinition> apps)
    {
        var stopped = new List<string>();
        foreach (var app in apps)
        {
            if (!_gateway.IsRunning(app.ProcessName))
            {
                continue;
            }
            _gateway.Kill(app.ProcessName);
            stopped.Add(app.Name);
        }
        return stopped;
    }

    /// <summary>
    /// Launches every app in <paramref name="apps"/> not already running, staggered
    /// by <paramref name="delaySeconds"/>. Pass an already-filtered (e.g. selected)
    /// list — this method launches everything it's given.
    /// </summary>
    /// <param name="progress">
    /// Reports (current, total) position through <paramref name="apps"/> as each is
    /// reached — including ones skipped as already-running — so a caller can show
    /// "Launching N of M..." for the whole batch, not just successful starts.
    /// </param>
    public async Task<LaunchResult> LaunchAppsAsync(
        IEnumerable<AppDefinition> apps,
        int delaySeconds,
        IProgress<(int Current, int Total)>? progress = null)
    {
        var launched = new List<string>();
        var failed = new List<string>();
        var batch = apps.ToList();
        for (var i = 0; i < batch.Count; i++)
        {
            var app = batch[i];
            progress?.Report((i + 1, batch.Count));

            if (_gateway.IsRunning(app.ProcessName))
            {
                continue;
            }
            // A bad path must not block the batch: skip the bookkeeping and the
            // stagger delay for anything that failed to start, but keep going.
            if (!_gateway.Start(app.Path, app.Arguments))
            {
                failed.Add(app.Name);
                continue;
            }
            launched.Add(app.Name);
            await _delay(delaySeconds);
        }
        return new LaunchResult(launched, failed);
    }
}

/// <summary>
/// Outcome of a launch batch: the apps that started successfully, and the apps
/// that were passed in but failed to start (already-running apps are skipped
/// silently and appear in neither list).
/// </summary>
public record LaunchResult(List<string> Launched, List<string> Failed);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~ProcessServiceTests"`
Expected: PASS — all tests in `ProcessServiceTests.cs`.

- [ ] **Step 5: Commit (Tasks 4 and 5 together)**

```bash
git add src/iRacingLauncher/Services/IProcessGateway.cs src/iRacingLauncher/Services/Win32ProcessGateway.cs src/iRacingLauncher/Services/ProcessService.cs src/iRacingLauncher.Tests/ProcessServiceTests.cs
git commit -m "Add launch arguments; move Selected-filtering out of ProcessService"
```

---

## Task 6: `AppRowViewModel` joins `AppDefinition` + `ProfileAppEntry`

**Files:**
- Modify: `src/iRacingLauncher/ViewModels/AppRowViewModel.cs`
- Modify: `src/iRacingLauncher.Tests/AppRowViewModelTests.cs`

**Interfaces:**
- Consumes: `AppDefinition`, `ProfileAppEntry` (Task 1).
- Produces: `AppRowViewModel(AppDefinition definition, ProfileAppEntry entry)`, with `Definition` replacing the old `Model` property. `Name`/`IsRunning`/`PathValid`/`Selected`/`IsStartable`/`StatusText`/`StatusColor`/`Icon` keep their existing public shape and behavior (including the selection-survives-launch fix already shipped on this branch).

- [ ] **Step 1: Write the failing test**

Replace `AppRowViewModelTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~AppRowViewModelTests"`
Expected: compile error — `AppRowViewModel`'s constructor still takes a single `AppEntry`.

- [ ] **Step 3: Rewrite `AppRowViewModel.cs`**

Replace the whole file:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using iRacingLauncher.Models;

namespace iRacingLauncher.ViewModels;

public class AppRowViewModel : INotifyPropertyChanged
{
    public AppDefinition Definition { get; }
    private readonly ProfileAppEntry _entry;

    public AppRowViewModel(AppDefinition definition, ProfileAppEntry entry)
    {
        Definition = definition;
        _entry = entry;
    }

    public string Name => Definition.Name;

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsStartable));
        }
    }

    private bool _pathValid = true;
    public bool PathValid
    {
        get => _pathValid;
        set
        {
            if (_pathValid == value) return;
            _pathValid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsStartable));
            if (!IsStartable) Selected = false;
        }
    }

    public bool Selected
    {
        get => _entry.Selected;
        set
        {
            if (_entry.Selected == value) return;
            _entry.Selected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Start is offered only for an app that exists on disk and isn't already up.</summary>
    public bool IsStartable => PathValid && !IsRunning;

    public string StatusText => !PathValid ? "Path not found" : IsRunning ? "Running" : "Not running";

    public Brush StatusColor => !PathValid
        ? new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23))
        : IsRunning
            ? new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F))
            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj --filter "FullyQualifiedName~AppRowViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/iRacingLauncher/ViewModels/AppRowViewModel.cs src/iRacingLauncher.Tests/AppRowViewModelTests.cs
git commit -m "Rebuild AppRowViewModel against AppDefinition/ProfileAppEntry"
```

---

## Task 7: `MainWindow` and `App.xaml.cs` resolve through the registry

**Files:**
- Modify: `src/iRacingLauncher/MainWindow.xaml.cs`
- Modify: `src/iRacingLauncher/App.xaml.cs`

**Interfaces:**
- Consumes: `LauncherConfig.Apps`, `LauncherConfig.ResolveApps` (Task 1); `AppRowViewModel(AppDefinition, ProfileAppEntry)` (Task 6); `ProcessService.IsRunning/StartApp/StopApp/StopAll/LaunchAppsAsync` (Task 5).

No dedicated tests exist for `MainWindow`/`App.xaml.cs` today (no test harness for WPF windows in this repo — consistent with the rest of this branch's UI-only changes), so this task is implement-and-manually-verify, same as Tasks 8–9. Manual verification happens in Task 10 once the Settings UI (Tasks 8–9) can actually produce a populated registry to look at.

- [ ] **Step 1: Update `MainWindow.xaml.cs`**

In `LoadRowsFromActiveProfile`, replace:

```csharp
    private void LoadRowsFromActiveProfile()
    {
        _rows.Clear();
        foreach (var app in _config.ActiveProfile.Apps)
        {
            _rows.Add(new AppRowViewModel(app) { Icon = TryLoadIcon(app.Path) });
        }
        AppList.ItemsSource = _rows;
    }
```

with:

```csharp
    private void LoadRowsFromActiveProfile()
    {
        _rows.Clear();
        foreach (var entry in _config.ActiveProfile.AppRefs)
        {
            var definition = _config.Apps.First(a => a.Id == entry.AppId);
            _rows.Add(new AppRowViewModel(definition, entry) { Icon = TryLoadIcon(definition.Path) });
        }
        AppList.ItemsSource = _rows;
    }
```

In `RefreshStatuses`, replace:

```csharp
    private void RefreshStatuses()
    {
        foreach (var row in _rows)
        {
            // A blank path is invalid, not implicitly valid — an app with no path
            // configured must read "Path not found" and have Start disabled.
            row.PathValid = File.Exists(row.Model.Path);
            row.IsRunning = _processService.IsRunning(row.Model);
        }
    }
```

with:

```csharp
    private void RefreshStatuses()
    {
        foreach (var row in _rows)
        {
            // A blank path is invalid, not implicitly valid — an app with no path
            // configured must read "Path not found" and have Start disabled.
            row.PathValid = File.Exists(row.Definition.Path);
            row.IsRunning = _processService.IsRunning(row.Definition);
        }
    }
```

In `LaunchButton_Click`, replace:

```csharp
            var result = await _processService.LaunchSelectedAsync(_config.ActiveProfile.Apps, _config.LaunchDelaySeconds, progress);
```

with:

```csharp
            var selectedApps = _config.ResolveApps(_config.ActiveProfile.AppRefs.Where(r => r.Selected)).ToList();
            var result = await _processService.LaunchAppsAsync(selectedApps, _config.LaunchDelaySeconds, progress);
```

In `StopAllButton_Click`, replace:

```csharp
    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        _processService.StopAll(_config.ActiveProfile.Apps);
        RefreshStatuses();
    }
```

with:

```csharp
    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        _processService.StopAll(_config.ResolveApps(_config.ActiveProfile.AppRefs));
        RefreshStatuses();
    }
```

Replace `AppRow_StartRequested` and `AppRow_StopRequested`:

```csharp
    private void AppRow_StartRequested(object? sender, AppRowViewModel row)
    {
        try
        {
            _processService.StartApp(row.Model);
        }
        catch (Exception)
        {
            // Defense in depth — the gateway already reports launch failures as a
            // false return rather than throwing. RefreshStatuses() below shows the
            // row as still not running either way.
        }
        RefreshStatuses();
    }

    private void AppRow_StopRequested(object? sender, AppRowViewModel row)
    {
        _processService.StopApp(row.Model);
        RefreshStatuses();
    }
```

with:

```csharp
    private void AppRow_StartRequested(object? sender, AppRowViewModel row)
    {
        try
        {
            _processService.StartApp(row.Definition);
        }
        catch (Exception)
        {
            // Defense in depth — the gateway already reports launch failures as a
            // false return rather than throwing. RefreshStatuses() below shows the
            // row as still not running either way.
        }
        RefreshStatuses();
    }

    private void AppRow_StopRequested(object? sender, AppRowViewModel row)
    {
        _processService.StopApp(row.Definition);
        RefreshStatuses();
    }
```

- [ ] **Step 2: Update `App.xaml.cs`**

In the tray "Launch Selected" handler, replace:

```csharp
                await processService.LaunchSelectedAsync(config.ActiveProfile.Apps, config.LaunchDelaySeconds);
```

with:

```csharp
                var selectedApps = config.ResolveApps(config.ActiveProfile.AppRefs.Where(r => r.Selected)).ToList();
                await processService.LaunchAppsAsync(selectedApps, config.LaunchDelaySeconds);
```

Add `using System.Linq;` to the top of `App.xaml.cs` if not already present (it currently only has `System`, `System.Windows`, `iRacingLauncher.Services`, `Wpf.Ui`, `Wpf.Ui.Appearance`, `Wpf.Ui.Tray.Controls` — add `using System.Linq;`).

- [ ] **Step 3: Build**

Run: `dotnet build iRacingLauncher.sln`
Expected: `iRacingLauncher.csproj` now builds cleanly (0 errors) — only `SettingsWindow.xaml.cs` still references the old shape, which is Tasks 8–9. If the whole solution build fails only on `SettingsWindow.xaml`/`.xaml.cs`, that's expected; move to Task 8.

- [ ] **Step 4: Commit**

```bash
git add src/iRacingLauncher/MainWindow.xaml.cs src/iRacingLauncher/App.xaml.cs
git commit -m "Resolve main window and tray launch through the app registry"
```

---

## Task 8: Settings — global "Apps" registry card

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml.cs` (`AppEditRowViewModel`, fields, constructor's app-related setup, `AddAppButton_Click`/`RemoveAppButton_Click`/`BrowseButton_Click`/`AutoFindButton_Click`, `SaveButton_Click`'s Apps sync — the profile-checklist half of the constructor and `EditProfileAppsButton_Click`/move-up/down are Task 9)
- Modify: `src/iRacingLauncher/SettingsWindow.xaml` (rename the "App List" card to "Apps", add the Arguments field; the new per-profile checklist card is Task 9)

**Interfaces:**
- Consumes: `AppDefinition` (Task 1).
- Produces: `AppEditRowViewModel` wrapping `AppDefinition` (was `AppEntry`) with an added `Arguments` property; `_workingApps : List<AppDefinition>`.

This task and Task 9 both touch `SettingsWindow.xaml.cs`/`.xaml` and are easiest done as one sitting since they share the constructor and `SaveButton_Click`, but are split here so each has its own test/verify cycle. Do Task 8 first, then Task 9 immediately after — the file won't fully compile between them (Task 9's `_currentEditingProfile`/`_workingProfiles` wiring still expects the old per-profile `Apps` shape until it's updated).

There is no dedicated test file for `SettingsWindow` today (no WPF window test harness in this repo). Manual verification for both tasks happens together in Task 10.

- [ ] **Step 1: Replace `AppEditRowViewModel`**

In `SettingsWindow.xaml.cs`, replace:

```csharp
public class AppEditRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public AppEntry Model { get; }

    public AppEditRowViewModel(AppEntry model) => Model = model;

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public string ProcessName
    {
        get => Model.ProcessName;
        set { Model.ProcessName = value; OnPropertyChanged(); }
    }

    public string Path
    {
        get => Model.Path;
        set { Model.Path = value; OnPropertyChanged(); }
    }

    private string _findStatus = string.Empty;
    public string FindStatus
    {
        get => _findStatus;
        set { _findStatus = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
```

with:

```csharp
public class AppEditRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public AppDefinition Model { get; }

    public AppEditRowViewModel(AppDefinition model) => Model = model;

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public string ProcessName
    {
        get => Model.ProcessName;
        set { Model.ProcessName = value; OnPropertyChanged(); }
    }

    public string Path
    {
        get => Model.Path;
        set { Model.Path = value; OnPropertyChanged(); }
    }

    public string Arguments
    {
        get => Model.Arguments;
        set { Model.Arguments = value; OnPropertyChanged(); }
    }

    private string _findStatus = string.Empty;
    public string FindStatus
    {
        get => _findStatus;
        set { _findStatus = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
```

- [ ] **Step 2: Add a `_workingApps` field and build it in the constructor**

Replace the field block:

```csharp
    private readonly LauncherConfig _config;
    private readonly SettingsService _settingsService;
    private readonly AppFinderService _appFinder;
    private readonly ObservableCollection<ProfileEditRowViewModel> _profileEditRows = new();
    private readonly ObservableCollection<AppEditRowViewModel> _editRows = new();
    private readonly List<Profile> _workingProfiles;
    private readonly string _originalTheme;
```

with:

```csharp
    private readonly LauncherConfig _config;
    private readonly SettingsService _settingsService;
    private readonly AppFinderService _appFinder;
    private readonly ObservableCollection<ProfileEditRowViewModel> _profileEditRows = new();
    private readonly ObservableCollection<AppEditRowViewModel> _appEditRows = new();
    private readonly List<AppDefinition> _workingApps;
    private readonly List<Profile> _workingProfiles;
    private readonly string _originalTheme;
```

(`_editRows` is renamed to `_appEditRows` here since it now backs the global Apps card, not a per-profile list — Task 9 introduces a separate `_profileAppRows` for the per-profile checklist.)

In the constructor, replace the deep-copy block:

```csharp
        // Work on a deep copy so Cancel can genuinely discard edits — see task-7 review fix.
        _workingProfiles = _config.Profiles
            .Select(p => new Profile
            {
                Name = p.Name,
                Apps = p.Apps.Select(a => new AppEntry { Name = a.Name, ProcessName = a.ProcessName, Path = a.Path, Selected = a.Selected }).ToList(),
            })
            .ToList();

        foreach (var profile in _workingProfiles)
        {
            _profileEditRows.Add(new ProfileEditRowViewModel(profile));
        }
        ProfileEditList.ItemsSource = _profileEditRows;

        _workingActiveProfile = _workingProfiles.FirstOrDefault(p => p.Name == _config.ActiveProfileName) ?? _workingProfiles[0];
        _currentEditingProfile = _workingActiveProfile;
        AppEditList.ItemsSource = _editRows;
        LoadEditRowsFromCurrentProfile();
```

with:

```csharp
        // Work on a deep copy so Cancel can genuinely discard edits — see task-7 review fix.
        _workingApps = _config.Apps
            .Select(a => new AppDefinition { Id = a.Id, Name = a.Name, ProcessName = a.ProcessName, Path = a.Path, Arguments = a.Arguments })
            .ToList();
        _workingProfiles = _config.Profiles
            .Select(p => new Profile
            {
                Name = p.Name,
                AppRefs = p.AppRefs.Select(r => new ProfileAppEntry { AppId = r.AppId, Selected = r.Selected }).ToList(),
            })
            .ToList();

        foreach (var profile in _workingProfiles)
        {
            _profileEditRows.Add(new ProfileEditRowViewModel(profile));
        }
        ProfileEditList.ItemsSource = _profileEditRows;

        foreach (var app in _workingApps)
        {
            _appEditRows.Add(new AppEditRowViewModel(app));
        }
        AppEditList.ItemsSource = _appEditRows;

        _workingActiveProfile = _workingProfiles.FirstOrDefault(p => p.Name == _config.ActiveProfileName) ?? _workingProfiles[0];
        _currentEditingProfile = _workingActiveProfile;
        LoadProfileAppRowsFromCurrentProfile();
```

(`LoadProfileAppRowsFromCurrentProfile` doesn't exist yet — Task 9 adds it. The file won't compile until Task 9 lands; that's expected for this split.)

- [ ] **Step 3: Replace `AddAppButton_Click`/`RemoveAppButton_Click`/`MoveAppUpButton_Click`/`MoveAppDownButton_Click`**

The move-up/down handlers for the old per-profile app list no longer apply to the global Apps card (registry order is display-only — Task 9 adds different move handlers for the per-profile checklist). Replace:

```csharp
    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppEntry { Name = "New App", ProcessName = string.Empty, Path = string.Empty, Selected = true };
        _currentEditingProfile.Apps.Add(app);
        _editRows.Add(new AppEditRowViewModel(app));
    }

    private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        _currentEditingProfile.Apps.Remove(row.Model);
        _editRows.Remove(row);
    }

    private void MoveAppUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        var index = _editRows.IndexOf(row);
        if (index <= 0) return;
        _editRows.Move(index, index - 1);
        _currentEditingProfile.Apps.Remove(row.Model);
        _currentEditingProfile.Apps.Insert(index - 1, row.Model);
    }

    private void MoveAppDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        var index = _editRows.IndexOf(row);
        if (index < 0 || index >= _editRows.Count - 1) return;
        _editRows.Move(index, index + 1);
        _currentEditingProfile.Apps.Remove(row.Model);
        _currentEditingProfile.Apps.Insert(index + 1, row.Model);
    }
```

with:

```csharp
    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppDefinition { Id = Guid.NewGuid().ToString("N"), Name = "New App", ProcessName = string.Empty, Path = string.Empty, Arguments = string.Empty };
        _workingApps.Add(app);
        _appEditRows.Add(new AppEditRowViewModel(app));

        // A newly-added app starts selected in every existing profile.
        foreach (var profile in _workingProfiles)
        {
            profile.AppRefs.Add(new ProfileAppEntry { AppId = app.Id, Selected = true });
        }
        LoadProfileAppRowsFromCurrentProfile();
    }

    private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AppEditRowViewModel row) return;
        _workingApps.Remove(row.Model);
        _appEditRows.Remove(row);

        // Deleting an app from the registry cascades to every profile referencing it.
        foreach (var profile in _workingProfiles)
        {
            profile.AppRefs.RemoveAll(r => r.AppId == row.Model.Id);
        }
        LoadProfileAppRowsFromCurrentProfile();
    }
```

(`Guid` needs `using System;`, already present in this file. `LoadProfileAppRowsFromCurrentProfile` is added in Task 9 — this file doesn't compile standalone until then.)

- [ ] **Step 4: Update `SaveButton_Click`**

Replace:

```csharp
        // Must mutate the existing List<Profile> in place rather than reassigning
        // _config.Profiles: App.xaml.cs's tray "Launch Selected" closure captures
        // config and reads config.ActiveProfile.Apps lazily, so swapping the list
        // reference would silently leave the tray menu launching a stale app list.
        _config.Profiles.Clear();
        _config.Profiles.AddRange(_workingProfiles);
        _config.ActiveProfileName = _workingActiveProfile.Name;
```

with:

```csharp
        // Must mutate the existing lists in place rather than reassigning
        // _config.Apps/_config.Profiles: App.xaml.cs's tray "Launch Selected" closure
        // captures config and reads config.ActiveProfile lazily, so swapping either
        // list reference would silently leave the tray menu launching stale data.
        _config.Apps.Clear();
        _config.Apps.AddRange(_workingApps);
        _config.Profiles.Clear();
        _config.Profiles.AddRange(_workingProfiles);
        _config.ActiveProfileName = _workingActiveProfile.Name;
```

- [ ] **Step 5: Update `SettingsWindow.xaml`'s "App List" card**

Rename the card and add the Arguments field. Replace:

```xml
                <ui:CardExpander Header="App List" Margin="0,4" IsExpanded="True">
                    <StackPanel>
                        <TextBlock x:Name="EditingProfileLabel" Text="Editing:" FontSize="12" FontWeight="SemiBold" Margin="0,0,0,8" />
                        <ItemsControl x:Name="AppEditList">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <ui:Card Margin="0,4">
                                        <StackPanel>
                                            <Grid Margin="0,0,0,4">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <TextBox Grid.Column="0" Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" />
                                                <ui:Button Grid.Column="1" Icon="{ui:SymbolIcon ArrowUp20}" ToolTip="Move up" Margin="8,0,0,0"
                                                           Click="MoveAppUpButton_Click" Tag="{Binding}" />
                                                <ui:Button Grid.Column="2" Icon="{ui:SymbolIcon ArrowDown20}" ToolTip="Move down" Margin="4,0,0,0"
                                                           Click="MoveAppDownButton_Click" Tag="{Binding}" />
                                                <ui:Button Grid.Column="3" Icon="{ui:SymbolIcon Delete20}" ToolTip="Remove" Margin="8,0,0,0"
                                                           Click="RemoveAppButton_Click" Tag="{Binding}" />
                                            </Grid>
                                            <ui:TextBox Text="{Binding ProcessName, UpdateSourceTrigger=PropertyChanged}"
                                                     PlaceholderText="Process name (no .exe)" Margin="0,0,0,4" />
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <ui:TextBox Grid.Column="0" Text="{Binding Path, UpdateSourceTrigger=PropertyChanged}"
                                                         PlaceholderText="Path to executable" />
                                                <ui:Button Grid.Column="1" Content="Browse" Icon="{ui:SymbolIcon FolderOpen20}" Margin="8,0,0,0"
                                                           Click="BrowseButton_Click" Tag="{Binding}" />
                                                <ui:Button Grid.Column="2" Content="Auto-Find" Icon="{ui:SymbolIcon Search20}" Margin="8,0,0,0"
                                                           Click="AutoFindButton_Click" Tag="{Binding}" />
                                            </Grid>
                                            <TextBlock Text="{Binding FindStatus}" FontSize="11" Opacity="0.7" Margin="0,4,0,0" />
                                        </StackPanel>
                                    </ui:Card>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                            <ui:Button Content="Add App" Icon="Add20" Click="AddAppButton_Click" />
                        </StackPanel>
                    </StackPanel>
                </ui:CardExpander>
```

with:

```xml
                <ui:CardExpander Header="Apps" Margin="0,4" IsExpanded="True">
                    <StackPanel>
                        <TextBlock Text="Configure each app once here, then enable it per game below."
                                   FontSize="11" Opacity="0.6" Margin="0,0,0,8" TextWrapping="Wrap" />
                        <ItemsControl x:Name="AppEditList">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <ui:Card Margin="0,4">
                                        <StackPanel>
                                            <Grid Margin="0,0,0,4">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <TextBox Grid.Column="0" Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" />
                                                <ui:Button Grid.Column="1" Icon="{ui:SymbolIcon Delete20}" ToolTip="Remove" Margin="8,0,0,0"
                                                           Click="RemoveAppButton_Click" Tag="{Binding}" />
                                            </Grid>
                                            <ui:TextBox Text="{Binding ProcessName, UpdateSourceTrigger=PropertyChanged}"
                                                     PlaceholderText="Process name (no .exe)" Margin="0,0,0,4" />
                                            <Grid Margin="0,0,0,4">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <ui:TextBox Grid.Column="0" Text="{Binding Path, UpdateSourceTrigger=PropertyChanged}"
                                                         PlaceholderText="Path to executable" />
                                                <ui:Button Grid.Column="1" Content="Browse" Icon="{ui:SymbolIcon FolderOpen20}" Margin="8,0,0,0"
                                                           Click="BrowseButton_Click" Tag="{Binding}" />
                                                <ui:Button Grid.Column="2" Content="Auto-Find" Icon="{ui:SymbolIcon Search20}" Margin="8,0,0,0"
                                                           Click="AutoFindButton_Click" Tag="{Binding}" />
                                            </Grid>
                                            <ui:TextBox Text="{Binding Arguments, UpdateSourceTrigger=PropertyChanged}"
                                                     PlaceholderText="Launch arguments (optional)" />
                                            <TextBlock Text="{Binding FindStatus}" FontSize="11" Opacity="0.7" Margin="0,4,0,0" />
                                        </StackPanel>
                                    </ui:Card>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                            <ui:Button Content="Add App" Icon="Add20" Click="AddAppButton_Click" />
                        </StackPanel>
                    </StackPanel>
                </ui:CardExpander>
```

(`EditingProfileLabel` moves to the new per-profile card in Task 9 — it's removed from here since this card is no longer profile-scoped.)

- [ ] **Step 6: Commit**

The file doesn't build standalone yet (`LoadProfileAppRowsFromCurrentProfile`, `_currentEditingProfile.AppRefs` wiring, `EditingProfileLabel` reference are Task 9). Do not commit separately — stage together with Task 9's changes and commit once at the end of Task 9.

---

## Task 9: Settings — per-profile app checklist, cascading delete, new-profile defaults

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml.cs` (new `ProfileAppSelectionRowViewModel`, `_profileAppRows` field, `LoadProfileAppRowsFromCurrentProfile`, `EditProfileAppsButton_Click`, new move handlers, `AddProfileButton_Click`)
- Modify: `src/iRacingLauncher/SettingsWindow.xaml` (new per-profile checklist card)

**Interfaces:**
- Consumes: `_workingApps`, `AppEditRowViewModel` (Task 8); `AppDefinition`, `ProfileAppEntry`, `Profile.AppRefs` (Task 1).
- Produces: `ProfileAppSelectionRowViewModel(AppDefinition definition, ProfileAppEntry entry)`; `SettingsWindow.LoadProfileAppRowsFromCurrentProfile()` (referenced by Task 8's `AddAppButton_Click`/`RemoveAppButton_Click` and the constructor).

- [ ] **Step 1: Add `ProfileAppSelectionRowViewModel`**

In `SettingsWindow.xaml.cs`, add this class after `ProfileEditRowViewModel` (before `public partial class SettingsWindow`):

```csharp
public class ProfileAppSelectionRowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public AppDefinition Definition { get; }
    public ProfileAppEntry Entry { get; }

    public ProfileAppSelectionRowViewModel(AppDefinition definition, ProfileAppEntry entry)
    {
        Definition = definition;
        Entry = entry;
    }

    public string Name => Definition.Name;

    public bool Selected
    {
        get => Entry.Selected;
        set { Entry.Selected = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
```

- [ ] **Step 2: Add the `_profileAppRows` field**

Replace:

```csharp
    private readonly ObservableCollection<AppEditRowViewModel> _appEditRows = new();
```

with:

```csharp
    private readonly ObservableCollection<AppEditRowViewModel> _appEditRows = new();
    private readonly ObservableCollection<ProfileAppSelectionRowViewModel> _profileAppRows = new();
```

- [ ] **Step 3: Add `LoadProfileAppRowsFromCurrentProfile` and wire it up in the constructor**

Replace `LoadEditRowsFromCurrentProfile`:

```csharp
    private void LoadEditRowsFromCurrentProfile()
    {
        _editRows.Clear();
        foreach (var app in _currentEditingProfile.Apps)
        {
            _editRows.Add(new AppEditRowViewModel(app));
        }
        EditingProfileLabel.Text = $"Editing: {_currentEditingProfile.Name}";
    }
```

with:

```csharp
    private void LoadProfileAppRowsFromCurrentProfile()
    {
        _profileAppRows.Clear();
        foreach (var entry in _currentEditingProfile.AppRefs)
        {
            var definition = _workingApps.First(a => a.Id == entry.AppId);
            _profileAppRows.Add(new ProfileAppSelectionRowViewModel(definition, entry));
        }
        EditingProfileLabel.Text = $"Selected for: {_currentEditingProfile.Name}";
    }
```

In the constructor, right after `AppEditList.ItemsSource = _appEditRows;` (added in Task 8 Step 2), add:

```csharp
        ProfileAppList.ItemsSource = _profileAppRows;
```

so the constructor's tail now reads:

```csharp
        foreach (var app in _workingApps)
        {
            _appEditRows.Add(new AppEditRowViewModel(app));
        }
        AppEditList.ItemsSource = _appEditRows;
        ProfileAppList.ItemsSource = _profileAppRows;

        _workingActiveProfile = _workingProfiles.FirstOrDefault(p => p.Name == _config.ActiveProfileName) ?? _workingProfiles[0];
        _currentEditingProfile = _workingActiveProfile;
        LoadProfileAppRowsFromCurrentProfile();
```

- [ ] **Step 4: Update `EditProfileAppsButton_Click`**

Replace:

```csharp
    private void EditProfileAppsButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        _currentEditingProfile = row.Model;
        LoadEditRowsFromCurrentProfile();
    }
```

with:

```csharp
    private void EditProfileAppsButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        _currentEditingProfile = row.Model;
        LoadProfileAppRowsFromCurrentProfile();
    }
```

- [ ] **Step 5: Add move-up/down handlers for the per-profile checklist**

Add after `EditProfileAppsButton_Click`:

```csharp
    private void MoveProfileAppUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileAppSelectionRowViewModel row) return;
        var index = _profileAppRows.IndexOf(row);
        if (index <= 0) return;
        _profileAppRows.Move(index, index - 1);
        _currentEditingProfile.AppRefs.Remove(row.Entry);
        _currentEditingProfile.AppRefs.Insert(index - 1, row.Entry);
    }

    private void MoveProfileAppDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileAppSelectionRowViewModel row) return;
        var index = _profileAppRows.IndexOf(row);
        if (index < 0 || index >= _profileAppRows.Count - 1) return;
        _profileAppRows.Move(index, index + 1);
        _currentEditingProfile.AppRefs.Remove(row.Entry);
        _currentEditingProfile.AppRefs.Insert(index + 1, row.Entry);
    }
```

- [ ] **Step 6: Update `AddProfileButton_Click`**

Replace:

```csharp
    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = new Profile { Name = "New Profile", Apps = new List<AppEntry>() };
        _workingProfiles.Add(profile);
        _profileEditRows.Add(new ProfileEditRowViewModel(profile));

        _currentEditingProfile = profile;
        LoadEditRowsFromCurrentProfile();
    }
```

with:

```csharp
    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        // A new profile starts with every existing app selected, same default as
        // a newly-added app being selected in every existing profile.
        var profile = new Profile
        {
            Name = "New Profile",
            AppRefs = _workingApps.Select(a => new ProfileAppEntry { AppId = a.Id, Selected = true }).ToList(),
        };
        _workingProfiles.Add(profile);
        _profileEditRows.Add(new ProfileEditRowViewModel(profile));

        _currentEditingProfile = profile;
        LoadProfileAppRowsFromCurrentProfile();
    }
```

- [ ] **Step 7: Update `RemoveProfileButton_Click`**

Replace:

```csharp
    private void RemoveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        // Always leave at least one profile — there must always be something to launch.
        if (_workingProfiles.Count <= 1) return;

        _workingProfiles.Remove(row.Model);
        _profileEditRows.Remove(row);

        if (ReferenceEquals(_currentEditingProfile, row.Model))
        {
            _currentEditingProfile = _workingProfiles[0];
            LoadEditRowsFromCurrentProfile();
        }
        if (ReferenceEquals(_workingActiveProfile, row.Model))
        {
            _workingActiveProfile = _workingProfiles[0];
        }
    }
```

with:

```csharp
    private void RemoveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProfileEditRowViewModel row) return;
        // Always leave at least one profile — there must always be something to launch.
        if (_workingProfiles.Count <= 1) return;

        _workingProfiles.Remove(row.Model);
        _profileEditRows.Remove(row);

        if (ReferenceEquals(_currentEditingProfile, row.Model))
        {
            _currentEditingProfile = _workingProfiles[0];
            LoadProfileAppRowsFromCurrentProfile();
        }
        if (ReferenceEquals(_workingActiveProfile, row.Model))
        {
            _workingActiveProfile = _workingProfiles[0];
        }
    }
```

- [ ] **Step 8: Add the per-profile checklist card to `SettingsWindow.xaml`**

Insert a new `ui:CardExpander` between the "Profiles" card and the "Apps" card (renamed in Task 8):

```xml
                <ui:CardExpander Header="Selected Apps" Margin="0,4" IsExpanded="True">
                    <StackPanel>
                        <TextBlock x:Name="EditingProfileLabel" Text="Selected for:" FontSize="12" FontWeight="SemiBold" Margin="0,0,0,8" />
                        <ItemsControl x:Name="ProfileAppList">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <ui:Card Margin="0,4">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="Auto" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <CheckBox Grid.Column="0" VerticalAlignment="Center" Margin="0,0,12,0"
                                                      IsChecked="{Binding Selected, Mode=TwoWay}" />
                                            <TextBlock Grid.Column="1" Text="{Binding Name}" VerticalAlignment="Center" />
                                            <ui:Button Grid.Column="2" Icon="{ui:SymbolIcon ArrowUp20}" ToolTip="Move up" Margin="8,0,0,0"
                                                       Click="MoveProfileAppUpButton_Click" Tag="{Binding}" />
                                            <ui:Button Grid.Column="3" Icon="{ui:SymbolIcon ArrowDown20}" ToolTip="Move down" Margin="4,0,0,0"
                                                       Click="MoveProfileAppDownButton_Click" Tag="{Binding}" />
                                        </Grid>
                                    </ui:Card>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ui:CardExpander>
```

- [ ] **Step 9: Build**

Run: `dotnet build iRacingLauncher.sln`
Expected: 0 errors. Fix any straggler compile error before proceeding — likely candidates are a stale `_editRows`/`LoadEditRowsFromCurrentProfile` reference or a missing `using System;` for `Guid`.

- [ ] **Step 10: Commit (Tasks 8 and 9 together)**

```bash
git add src/iRacingLauncher/SettingsWindow.xaml.cs src/iRacingLauncher/SettingsWindow.xaml
git commit -m "Split Settings into a global Apps registry and per-profile checklist"
```

---

## Task 10: Delete `AppEntry`; full verification pass

**Files:**
- Delete: `src/iRacingLauncher/Models/AppEntry.cs`

**Interfaces:** none — this is cleanup and verification only.

- [ ] **Step 1: Confirm nothing still references `AppEntry`**

Run: `grep -rn "AppEntry" src --include="*.cs" --include="*.xaml"`
Expected: no matches outside `obj/`/`bin/` build artifacts. If anything real turns up, fix it before deleting the file.

- [ ] **Step 2: Delete the file**

```bash
git rm src/iRacingLauncher/Models/AppEntry.cs
```

- [ ] **Step 3: Full build and test run**

Run: `dotnet build iRacingLauncher.sln`
Expected: 0 errors.

Run: `dotnet test src/iRacingLauncher.Tests/iRacingLauncher.Tests.csproj`
Expected: PASS, all tests (the existing `AppFinderServiceTests` plus everything touched in Tasks 2, 3, 5, 6).

- [ ] **Step 4: Manual verification — back up the real config first**

This app is the developer's actual daily-use launcher; `%APPDATA%\iRacingLauncher\config.json` is real, in-use data. Back it up before running the built exe, and restore it afterward regardless of outcome:

```bash
cp "$APPDATA/iRacingLauncher/config.json" "$TEMP/config.json.pre-registry-migration.bak"
```

- [ ] **Step 5: Launch and verify migration**

Build and run `src/iRacingLauncher/bin/Debug/net10.0-windows/win-x64/SimRacingLauncher.exe`. Confirm:
- It starts without error against the real (pre-migration) config.
- `%APPDATA%\iRacingLauncher\config.json` now has a top-level `"apps"` array and each profile has `"appRefs"` instead of embedded `"apps"`.
- The real CrewChiefV4/Trading Paints/RaceLab/Coach Dave Delta entries collapsed to one `AppDefinition` each (not one per profile) — check the `apps` array length is much smaller than 3× the old per-profile count.
- Open Settings: the "Apps" card lists the merged registry (with a working Arguments field per app); the "Selected Apps" card reflects each profile's original selections when switching between profiles in the "Profiles" list.
- Add a launch argument to one app (e.g. `--vr` on some entry), Save, reopen Settings, confirm it persisted.
- Toggle an app's checkbox off for one profile, Save, switch profiles and back, confirm the toggle stuck (this is the same class of regression Task 6's `Selected_SurvivesTransitionToRunning`-style bug covered — verify it manually here since there's no window-level test).

- [ ] **Step 6: Restore the real config**

```bash
cp "$TEMP/config.json.pre-registry-migration.bak" "$APPDATA/iRacingLauncher/config.json"
diff "$APPDATA/iRacingLauncher/config.json" "$TEMP/config.json.pre-registry-migration.bak" && echo "restored"
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Remove AppEntry now that apps live in the shared registry"
```
