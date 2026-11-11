# Launch Through Steam (with VR) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an app be launched through Steam by its Steam App ID, optionally in Steam's VR mode, instead of by its exe path.

**Architecture:** Two new fields on the shared `AppDefinition` (`SteamAppId`, `LaunchInVr`). A small pure helper (`SteamLaunch`) validates IDs and builds the `steam://` URL. `ProcessService` routes Steam apps to a new `IProcessGateway.OpenSteamUrl` and everything else to the existing `Start(path, args)`. Settings gets an App ID box, a "Launch in VR" checkbox and a "Find ID" button that reads Steam's own manifest files.

**Tech Stack:** C# / .NET 10 WPF, WPF-UI, xUnit.

**Spec:** None separate. The user skipped the design Q&A ("plan it"), so the design below is Claude's call and is the thing to review.

## Design (Claude's calls, review these)

- **Trigger:** an app launches through Steam when its **Steam App ID** is filled in. Blank = today's exe launch, unchanged.
- **URLs:** VR off → `steam://rungameid/<id>`. VR on → `steam://launch/<id>/vr`. Community reports show `/vr` works for most titles. One ACC thread says only `/VR` worked. Lowercase is the default, and Task 6's manual check confirms it on the user's own games.
- **Arguments:** ignored for Steam launches (Steam URLs can't carry both VR mode and arguments). The Arguments box is disabled for a Steam app, with the hint "Steam games: set launch options in Steam (Properties > Launch Options)". Steam applies those itself.
- **Path for a Steam app:** optional. Still used for the row icon and for Find ID. A Steam app with no path is launchable and never shows "Path not found".
- **Running detection:** unchanged, still by Process name. The user must enter the game's process name, not Steam's.
- **App ID validation:** digits only after trimming. A bad ID blocks Save with a message naming the app.
- **Find ID:** given the app's Path inside `<library>\steamapps\common\<installdir>\...`, read `<library>\steamapps\appmanifest_*.acf` and return the `appid` whose `installdir` matches.
- **Auto-fill (user asked for it):** whenever an app's Path changes in Settings (Browse, Auto-Find or typing), if the App ID box is blank **and** Arguments is blank, run Find ID and fill the box, with the status "Steam game detected (App ID <id>). It will launch through Steam." Skipped when Arguments is set, because a Steam launch ignores arguments and would silently break that setup. Apps already configured are not changed when Settings opens. The Find ID button stays for the skipped cases.
- **No Oculus `/othervr` option** for now. Easy to add later as a dropdown if asked.

## Global Constraints

- Existing configs must load unchanged: missing `steamAppId`/`launchInVr` keys mean `""`/`false`. An explicit `null` App ID loads as `""`, matching what `Arguments` already does.
- The gateway only ever opens URLs that start with `steam://`; anything else returns false without launching.
- Prose in UI copy and README: no em dashes.
- Commit messages: no Claude attribution or trailers (user preference overrides the default trailer).

## Review Focus

1. **Settings deep copy drops the new fields:** `SettingsWindow.xaml.cs:177-179` copies each `AppDefinition` field by hand. If the new fields aren't added there, every Save silently wipes Steam settings. Task 4 adds them, and its manual check reopens Settings after Save.
2. **Steam not installed:** opening `steam://` with no handler throws `Win32Exception`. The gateway must return false so the app lands in the "failed to launch" list instead of crashing. Pinned by the gateway test in Task 3 (rejects non-steam scheme) plus a manual check.
3. **App ID with spaces or pasted URL** (`" 2399420 "`, `https://store.steampowered.com/app/2399420/`): trimmed ID is accepted. A URL is rejected with a clear message, not stored. Pinned in Task 2's validity tests.
4. **Launch All skips an already-running game:** unchanged behavior, but it depends on Process name being the game's. Covered in README copy (Task 6).
5. **Malformed or foreign `.acf` files in Find ID:** unreadable, missing keys, or a Path outside any `steamapps\common` must return null, not throw. Pinned in Task 5's tests.

---

### Task 0: Commit the pending launch-arguments tests

The working tree already holds `LaunchArgumentsTests.cs` and the `AppDefinition.Arguments` null-normalization (58 tests green). Commit them first so this feature starts from a clean tree. Message: `Add end-to-end tests for per-app launch arguments`.

### Task 1: Model fields

**Files:**
- Modify: `src/iRacingLauncher/Models/AppDefinition.cs`
- Test: `src/iRacingLauncher.Tests/SteamLaunchTests.cs` (create)

**Interfaces:**
- Produces: `AppDefinition.SteamAppId : string` (never null, default `""`), `AppDefinition.LaunchInVr : bool` (default `false`). JSON keys `steamAppId`, `launchInVr` (existing camelCase options).

- [ ] **Step 1: Write failing tests** in `SteamLaunchTests`, reusing the temp-file pattern from `LaunchArgumentsTests`:
  - `SaveThenLoad_PreservesSteamAppIdAndVrFlag`: set `Apps[0].SteamAppId = "2399420"`, `LaunchInVr = true`; save, reload with a new `SettingsService`; assert both equal.
  - `Load_WhenSteamKeysMissing_DefaultsToExeLaunch`: current-format JSON with no steam keys → `SteamAppId == ""`, `LaunchInVr == false`.
  - `Load_WhenSteamAppIdIsNull_TreatsAsEmpty`: `"steamAppId": null` → `""`.
- [ ] **Step 2: Run** `dotnet test src/iRacingLauncher.Tests --filter SteamLaunchTests`. Expected: compile failure (properties don't exist).
- [ ] **Step 3: Implement** the two properties. `SteamAppId` uses the same null-coalescing backing field as `Arguments`.
- [ ] **Step 4: Run** the filter again → PASS, then the full suite → all green.
- [ ] **Step 5: Commit** `Add Steam App ID and VR flag to app definitions`.

### Task 2: `SteamLaunch` helper

**Files:**
- Create: `src/iRacingLauncher/Services/SteamLaunch.cs`
- Test: `src/iRacingLauncher.Tests/SteamLaunchTests.cs`

**Interfaces:**
- Produces (all `public static` on `static class SteamLaunch`):
  - `bool UsesSteam(AppDefinition app)`: true when `SteamAppId` is non-blank.
  - `bool IsValidAppId(string id)`: true when `id.Trim()` is 1+ ASCII digits.
  - `string BuildUrl(AppDefinition app)`: `steam://launch/{id}/vr` or `steam://rungameid/{id}`, id trimmed. Throws `InvalidOperationException` if the id is invalid.
  - `bool IsLaunchable(AppDefinition app, Func<string, bool> fileExists)`: `UsesSteam(app) || fileExists(app.Path)`.

- [ ] **Step 1: Write failing tests:**
  - `[Theory] IsValidAppId` true for `"2399420"`, `" 2399420 "`; false for `""`, `"  "`, `"abc"`, `"-1"`, `"2399420/"`, `"https://store.steampowered.com/app/2399420/"`, `"２３９"` (full-width digits).
  - `BuildUrl_VrOff_UsesRunGameId` → `"steam://rungameid/2399420"`.
  - `BuildUrl_VrOn_UsesLaunchVr` → `"steam://launch/2399420/vr"`.
  - `BuildUrl_TrimsId` (`" 2399420 "` → `"steam://rungameid/2399420"`).
  - `BuildUrl_InvalidId_Throws`.
  - `IsLaunchable_SteamAppWithNoPath_IsTrue` (fileExists always false).
  - `IsLaunchable_ExeAppWithMissingPath_IsFalse`.
- [ ] **Step 2: Run** filter → compile failure.
- [ ] **Step 3: Implement.** Use `char.IsAsciiDigit`, not `char.IsDigit` (the latter accepts full-width digits).
- [ ] **Step 4: Run** → PASS; full suite green.
- [ ] **Step 5: Commit** `Add SteamLaunch helper for Steam URLs and App ID validation`.

### Task 3: Route Steam apps through the gateway

**Files:**
- Modify: `src/iRacingLauncher/Services/IProcessGateway.cs`, `Win32ProcessGateway.cs`, `ProcessService.cs` (lines 28 and 93)
- Modify: `src/iRacingLauncher.Tests/ProcessServiceTests.cs` (`FakeProcessGateway`)
- Test: `src/iRacingLauncher.Tests/SteamLaunchTests.cs`

**Interfaces:**
- Consumes: `SteamLaunch.UsesSteam`, `SteamLaunch.BuildUrl`.
- Produces: `IProcessGateway.OpenSteamUrl(string url) : bool`. Returns false (never throws) for a non-`steam://` URL, no URL handler, or any launch failure. `FakeProcessGateway.OpenedUrls : List<string>`, plus `FailOpenUrls : bool` to simulate failure.

- [ ] **Step 1: Write failing tests:**
  - `StartApp_SteamApp_OpensSteamUrlAndNotPath`: app with Path `C:\lmu.exe`, Arguments `--ignored`, SteamAppId `2399420`, VR on → `OpenedUrls == ["steam://launch/2399420/vr"]`, `StartedPaths` empty.
  - `LaunchAppsAsync_MixesSteamAndExeApps`: one exe app and one Steam app in a batch → each goes down its own path, both in `Launched`, order preserved.
  - `LaunchAppsAsync_SteamOpenFails_ReportsFailed`: `FailOpenUrls = true` → app name in `Failed`, no delay awaited for it.
  - `LaunchAppsAsync_SteamAppAlreadyRunning_IsSkipped`: process name in `RunningProcessNames` → no URL opened.
  - `Win32Gateway_OpenSteamUrl_RejectsOtherSchemes`: `new Win32ProcessGateway().OpenSteamUrl("https://example.com")` and `"C:\\Windows\\notepad.exe"` both return false (and launch nothing).
- [ ] **Step 2: Run** filter → compile failure on `OpenSteamUrl`/`OpenedUrls`.
- [ ] **Step 3: Implement.** Gateway: case-insensitive `steam://` prefix check, then `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`, catching `Win32Exception` and `InvalidOperationException` like `Start`. `ProcessService`: one private `bool Launch(AppDefinition app)` used by both `StartApp` and `LaunchAppsAsync`.
- [ ] **Step 4: Run** → PASS; full suite green.
- [ ] **Step 5: Commit** `Launch apps with a Steam App ID through Steam`.

### Task 4: Settings UI and main window status

No automated UI tests exist in this project. This task's checks are manual, with the app run via `dotnet run --project src\iRacingLauncher` against a backed-up config.

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml` (app card, after the Arguments box)
- Modify: `src/iRacingLauncher/SettingsWindow.xaml.cs` (`AppEditRowViewModel`, deep copy at 177-179, `AddAppButton_Click` at ~342, Save validation)
- Modify: `src/iRacingLauncher/MainWindow.xaml.cs:127`

**Interfaces:**
- Consumes: `SteamLaunch.UsesSteam`, `SteamLaunch.IsValidAppId`, `SteamLaunch.IsLaunchable`.
- Produces: `AppEditRowViewModel.SteamAppId : string`, `.LaunchInVr : bool`, `.UsesSteam : bool` (raises change for `UsesSteam` whenever `SteamAppId` changes).

- [ ] **Step 1: View model + copy.** Add the three properties. Add `SteamAppId` and `LaunchInVr` to the deep copy and to the new-app initializer.
- [ ] **Step 2: XAML.** In each app card, add a row with: `ui:TextBox` bound to `SteamAppId`, placeholder `Steam App ID (optional, from the store page URL)`; a `CheckBox` "Launch in VR" bound to `LaunchInVr`, `IsEnabled` bound to `UsesSteam`. Disable the Arguments box when `UsesSteam` (use the existing `InverseBooleanConverter`), and add a small hint `TextBlock` visible only when `UsesSteam`: "Steam games: set launch options in Steam (Properties > Launch Options)."
- [ ] **Step 3: Save validation.** Next to `ValidateProfileNames`, add `ValidateSteamAppIds()`. For any app with a non-blank, invalid ID, return `"The Steam App ID for \"{name}\" should be just the number, like 2399420."` On save, store the trimmed ID.
- [ ] **Step 4: Main window.** Replace `File.Exists(row.Definition.Path)` with `SteamLaunch.IsLaunchable(row.Definition, File.Exists)`.
- [ ] **Step 5: Build** `dotnet build src\iRacingLauncher`. Expected: 0 errors. Full test suite green.
- [ ] **Step 6: Manual check:** add App ID + VR to an app, Save, close and reopen Settings → values still there. Remove the path → main window row shows "Not running" (not "Path not found") and Start is enabled. Type `abc` as ID → Save blocked with the message. Cancel after editing → changes discarded.
- [ ] **Step 7: Commit** `Add Steam App ID and VR options to Settings`.

### Task 5: Find and auto-fill the App ID from the app's path

**Files:**
- Create: `src/iRacingLauncher/Services/SteamAppIdFinder.cs`
- Modify: `src/iRacingLauncher/SettingsWindow.xaml(.cs)` (Find ID button handler)
- Test: `src/iRacingLauncher.Tests/SteamAppIdFinderTests.cs`

**Interfaces:**
- Produces: `static string? SteamAppIdFinder.FindAppId(string exePath)`. Walks up from `exePath` to the `steamapps\common\<installdir>` segment (case-insensitive), scans `steamapps\appmanifest_*.acf`, and returns the `appid` whose `installdir` equals `<installdir>` (case-insensitive). Returns null for anything else. Never throws on IO or parse problems.

- [ ] **Step 1: Write failing tests** against a real temp directory tree:
  - `FindAppId_ExeInsideSteamLibrary_ReturnsAppId`: build `tmp\steamapps\common\Le Mans Ultimate\Le Mans Ultimate.exe` plus `tmp\steamapps\appmanifest_2399420.acf` containing `"appid"		"2399420"` and `"installdir"		"Le Mans Ultimate"` → `"2399420"`.
  - `FindAppId_PicksMatchingManifestAmongSeveral`: two manifests, different installdirs → the matching one.
  - `FindAppId_ExeInNestedSubfolder_StillMatches`: exe at `common\Game\bin\x64\game.exe`.
  - `FindAppId_PathOutsideSteam_ReturnsNull` (`C:\iRacing\ui\iRacingUI.exe`).
  - `FindAppId_ManifestMissingAppId_ReturnsNull`; `FindAppId_GarbageManifest_ReturnsNull`; `FindAppId_BlankPath_ReturnsNull`.
- [ ] **Step 2: Run** filter → compile failure.
- [ ] **Step 3: Implement.** Parse with a regex for `"key"\s+"value"` pairs; read only the top-level `appid` and `installdir`.
- [ ] **Step 4: Run** → PASS; full suite green.
- [ ] **Step 5: Add and wire the button.** Add a `Find ID` `ui:Button` (icon `Search20`, `Tag="{Binding}"`) next to the Steam App ID box. Pressing it sets `SteamAppId` and `FindStatus = "Found Steam App ID. Review and Save to confirm."`, or `FindStatus = "Couldn't find a Steam App ID for this path. Copy it from the game's store page URL."`.
- [ ] **Step 6: Auto-fill.** Add `static bool SteamAppIdFinder.ShouldAutoFill(string steamAppId, string arguments)` (true when both are blank) with tests `ShouldAutoFill_BothBlank_IsTrue`, `ShouldAutoFill_ArgumentsSet_IsFalse`, `ShouldAutoFill_IdAlreadySet_IsFalse`. In `AppEditRowViewModel.Path`'s setter, when `ShouldAutoFill` is true, call `FindAppId(value)`. On a hit, set `SteamAppId` and `FindStatus = $"Steam game detected (App ID {id}). It will launch through Steam."`. Do not touch apps in the constructor (opening Settings must not change anything).
- [ ] **Step 7: Manual check** on the user's real LMU (or any Steam game) install: Browse to the exe with a blank ID → ID fills in with the status line. Same with Arguments set → stays blank, Find ID button fills it. Reopen Settings on an existing app → nothing changes.
- [ ] **Step 8: Commit** `Find and auto-fill a Steam game's App ID from its install path`.

### Task 6: README and end-to-end check

**Files:**
- Modify: `README.md` (Features list and Setup guide step that describes app fields)

- [ ] **Step 1: README.** Add a short "Steam games and VR" note: fill in the Steam App ID (or press Find ID) to launch through Steam; tick Launch in VR to start in SteamVR; set the Process name to the game's own exe name; extra launch options go in Steam. No em dashes.
- [ ] **Step 2: Full suite + build** green.
- [ ] **Step 3: Manual end-to-end** (user's machine, headset on): LMU with ID 2399420 + VR → Steam starts it in VR mode; VR off → normal launch; main window shows Running once the game is up; Launch All skips it while running. If `/vr` fails for a game, try `/VR` by hand (`start steam://launch/<id>/VR`) and report back before changing the URL.
- [ ] **Step 4: Commit** `Document launching Steam games and VR through Steam`.
