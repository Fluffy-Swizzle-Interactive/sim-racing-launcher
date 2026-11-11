# Apps decoupled from games, with custom launch parameters

Date: 2026-08-21
Branch: `app-improvements`

## Background

Reddit feedback on the launcher (see conversation context) asked for
apps (Crew Chief, Trading Paints, etc.) to be configured once instead
of once per game/profile, and for custom launch parameters (e.g. a VR
flag for LMU). Three smaller, independent pieces of the same feedback
batch (selection surviving launch, Close to tray, reordering games)
already shipped as bounded changes earlier on this branch. This spec
covers the remaining, architectural piece: decoupling apps from games
and adding per-app launch arguments.

### Current state

`Profile.Apps` is a `List<AppEntry>`, and `AppEntry` embeds
`Name`/`ProcessName`/`Path`/`Selected` directly. Every profile that
uses the same companion app (Crew Chief, Trading Paints, RaceLab,
Coach Dave Delta) carries its own full copy. In the shipping default
config, and confirmed against the real user's live `config.json`, the
same `ProcessName` appears in multiple profiles but with inconsistent
`Name` casing/spelling and with `Path` populated in at most one
profile — the others are blank.

## Goals

- Configure an app's name, process name, path, and launch arguments
  in exactly one place.
- Each game/profile independently selects which apps apply to it, and
  keeps its own launch order for its selected apps (unchanged from
  today).
- Support custom launch parameters per app.
- Migrate existing `config.json` files (both the current per-profile
  format and the older pre-profiles flat format) without losing data,
  in a single automatic pass on first load.

## Non-goals

- No change to how a profile's launch order is edited (still Move
  Up/Down in a per-profile list).
- No UI for detecting/merging "probably the same app" apps a user
  added separately by hand — migration only merges what the on-disk
  data already implies (matching `ProcessName` within one legacy
  config file).
- No shared/global launch order across profiles.

## Data model

```
AppDefinition
    Id          string   // GUID, assigned at creation, never shown in UI
    Name        string
    ProcessName string
    Path        string
    Arguments   string   // default "", raw command-line string

ProfileAppEntry
    AppId       string   // AppDefinition.Id
    Selected    bool

Profile
    Name        string
    AppRefs     List<ProfileAppEntry>   // order = this profile's launch order

LauncherConfig
    ...existing fields (LaunchDelaySeconds, LaunchAtWindowsStartup, CloseToTray, Theme)...
    Apps        List<AppDefinition>
    Profiles    List<Profile>
    ActiveProfileName
    ActiveProfile  // derived, unchanged
```

`AppEntry` is removed. `AppRowViewModel` (main window) and the
settings-edit view models are rebuilt against the
`(AppDefinition, ProfileAppEntry)` pair instead of a single `AppEntry`.

## Migration

Runs once, inside `SettingsService.Load()`, whenever the loaded JSON
predates the `apps` top-level array (i.e. it has `profiles` with
embedded per-profile app objects, or it's the older pre-`profiles`
flat-`apps` shape already handled today). Both existing legacy paths
feed into the same new migration step rather than being separate
special cases:

1. If the file has no top-level `profiles` key (oldest format), first
   apply today's existing `MigrateLegacyFormat` to fold it into a
   single `Default` profile with embedded apps, exactly as now.
2. Given a config whose profiles embed apps directly (either freshly
   produced by step 1, or read straight from an on-disk
   `profiles`-shaped file with embedded apps): group every embedded
   app, across all profiles, by `ProcessName` using a case-insensitive
   comparison.
3. For each group, create one `AppDefinition` with a fresh GUID `Id`.
   - `Path`: the first non-empty `Path` found in the group, in
     profile order; empty string if none.
   - `Name`, `Arguments`: taken from whichever group member supplied
     that winning `Path` (or, if no member had a path, the first
     member overall).
   - `ProcessName`: the first member's `ProcessName` (preserves its
     original casing).
4. For each profile, replace its embedded app list with an
   `AppRefs` list of `ProfileAppEntry { AppId, Selected }`, one per
   original embedded app, in that profile's original order, each
   pointing at the `AppDefinition` its group produced. `Selected`
   carries over unchanged from the embedded app it came from.
5. Persist the migrated config immediately (`TrySave`), same as the
   existing legacy-format migration does, so subsequent loads read the
   new shape directly.

A config that already has a top-level `apps` array is the current
format and needs no migration.

### Worked example

Given the real-world shape (iRacing profile has real paths, ACC/AC
have blank paths for the same `ProcessName`s):

- `CrewChiefV4` (`ProcessName: "CrewChiefV4"`) appears in all three
  profiles. Only the iRacing profile has a path. Migration produces
  one `AppDefinition` with that path, and three `ProfileAppEntry`s (one
  per profile) pointing at it.
- `iRacingUI`, `AC2-Win64-Shipping`, and `acs` each appear in exactly
  one profile — each becomes its own single-profile `AppDefinition`,
  same as today's per-sim entries.
- Where `Name` differs across the group (e.g. `"Trading Paints"` vs
  `"TradingPaints"`, `"Coach Dave Delta"` vs `"Coach David"`), the
  winning member (the one with the non-empty path, or the first
  member if none has one) supplies the merged `Name`.

## Settings UI

**Apps card** (new, replaces the current "App List" card): a flat,
editable list of every `AppDefinition` — Name, Process name, Path
(Browse/Auto-Find), and a new Arguments text field, with Add/Remove,
matching today's per-profile app-edit form but unscoped from any
profile.

**Profiles card**: unchanged list of profiles with Move Up/Down (added
earlier on this branch) and Delete. "Edit Apps" now opens a per-profile
checklist: one row per `AppDefinition` (name + a Selected checkbox for
this profile), with Move Up/Down reordering that profile's `AppRefs`
list — visually similar to the main window's app rows, without the
running-status/start/stop controls.

**Cascading delete**: removing an app from the Apps card removes its
`ProfileAppEntry` from every profile that referenced it.

**Defaults**: a newly-added app is selected in every existing profile.
A newly-added profile starts with every existing app selected. Both
match today's "Add App" behavior of defaulting `Selected = true`.

## Launch parameters

`IProcessGateway.Start(string exePath)` becomes
`Start(string exePath, string arguments)`. `Win32ProcessGateway.Start`
passes `arguments` straight into
`ProcessStartInfo.Arguments` — a raw command-line string, relying on
Windows' own argv parsing (no custom quoting/escaping logic on our
side). `ProcessService.StartApp` and `LaunchSelectedAsync` pass
`app.Arguments` through unchanged; a blank `Arguments` behaves exactly
as today (no arguments passed).

## Error handling

No new failure modes beyond what migration already handles today:
`SettingsService.Load()` still falls back to
`CreateDefaultConfig()`/`ResetToDefaults()` for unreadable, corrupt, or
unparseable files — the new migration step runs inside that same
try/catch, so a bug in the merge logic surfaces as a fallback to
defaults rather than a crash, matching the existing contract.

## Testing

- `SettingsServiceTests`: new cases for the migration — dedup by
  `ProcessName` (case-insensitive), non-empty-path-wins, `Name`
  following the winning path, per-profile order and `Selected`
  preserved, and the worked example above (sims stay distinct,
  companions merge).
- `ProcessServiceTests`: update `FakeProcessGateway`/call sites for the
  new `Start(path, arguments)` signature; new case asserting the
  gateway receives the app's `Arguments`.
- New tests for the Settings view models covering cascading delete and
  the "selected everywhere by default" behavior for new apps/profiles.
- `AppRowViewModelTests`: adapted to construct from an
  `AppDefinition`/`ProfileAppEntry` pair instead of `AppEntry`.

## Rollout

Ships as one more commit (or small set of commits) on the existing
`app-improvements` branch, after the three bounded pieces already on
it (selection-survives-launch fix, Close to tray, profile reordering).
