# Settings Tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Settings window's single long page of auto-opened expanders with three tabs (Games, Apps, General), where Games and Apps are list-on-the-left, details-on-the-right.

**Architecture:** Pure logic that the window needs (Save validation that also says *where* the problem is, picking a neighbour after a delete, the VR tag rule) moves into small testable pieces first. Then the XAML is restructured tab by tab inside `SettingsWindow`, keeping the existing working-copy model (`_workingApps`, `_workingProfiles`, `_currentEditingProfile`, `_workingActiveProfile`) unchanged.

**Tech Stack:** C# / .NET 10 WPF, WPF-UI (already referenced), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-10-settings-tabs-design.md`

## Global Constraints

- Selecting a game never changes the active game. `_currentEditingProfile` (selection) and `_workingActiveProfile` (active on Save) stay separate.
- Settings wording says "game", not "profile". The `Profile` type, config keys and the main window's "Profile" label do not change.
- Save/Cancel semantics unchanged: everything edits the working copy; Cancel discards; theme applies live and reverts on close without Save.
- The last game can't be deleted (existing rule).
- No new NuGet packages. No em dashes in new UI copy or README text.
- Commit messages: no Claude attribution trailers (user preference).

## Review Focus

1. **Renaming an app on the Apps tab, then going to Games:** the checklist must show the new name. The fix is to reload the checklist whenever the Games tab or a game is selected. Manual check in Task 4.
2. **Deleting the selected game or app:** a neighbour must be selected, and the details pane must never keep showing the deleted item. `ListSelection.AfterRemoval` is tested in Task 2. The wiring is checked by hand in Tasks 4 and 5.
3. **Adding an app on the Apps tab:** it must appear in every game's checklist (existing cascade) and be selected on the Apps tab. Manual check in Task 5.
4. **Auto-Find still running when the user picks another app:** the result must land on the app it was started for, and the button must come back. The handler already captures `row` from `Tag`, so keep that pattern. Manual check in Task 5.
5. **Save blocked by a problem on a tab you're not on:** Settings must switch to that tab and select that game or app. `SettingsValidation` is tested in Task 1. The jump is checked by hand in Task 6.

---

### Task 1: `SettingsValidation` with "game" wording and a location

**Files:**
- Create: `src/iRacingLauncher/Services/SettingsValidation.cs`
- Modify: `src/iRacingLauncher/SettingsWindow.xaml.cs` (replace `ValidateProfileNames` and `ValidateSteamAppIds`)
- Test: `src/iRacingLauncher.Tests/SettingsValidationTests.cs`

**Interfaces:**
- Produces:
  - `public enum SettingsTab { Games, Apps, General }`
  - `public sealed record SettingsProblem(string Message, SettingsTab Tab, Profile? Game, AppDefinition? App);`
  - `public static SettingsProblem? SettingsValidation.Validate(IReadOnlyList<Profile> games, IReadOnlyList<AppDefinition> apps)`: first problem found, game checks first (same order as today), or null.

- [ ] **Step 1: Write failing tests:**
  - `Validate_AllFine_ReturnsNull`
  - `Validate_BlankGameName_PointsAtThatGame`: message `"Every game needs a name. Give the blank one a name, or delete it."`, `Tab == Games`, `Game` is the blank one.
  - `Validate_DuplicateGameNames_PointsAtTheSecondOne`: names `"iRacing"`, `" iracing "` → message `"Two games are both named \"iRacing\". Game names have to be unique."`, `Game` is the second.
  - `Validate_BadSteamAppId_PointsAtThatApp`: app `"ACC"` with id `"abc"` → message `"The Steam App ID for \"ACC\" should be just the number, like 2399420."`, `Tab == Apps`, `App` is it.
  - `Validate_GameProblemWinsOverAppProblem`.
- [ ] **Step 2: Run** `dotnet test src/iRacingLauncher.Tests --filter SettingsValidationTests`. Expected: compile failure.
- [ ] **Step 3: Implement.** Move the two existing checks' logic over unchanged except the wording. Duplicate detection groups by `Name.Trim()`, case-insensitive, as today. The key shown is the first one's trimmed name, and `Game` is the second member of the group.
- [ ] **Step 4: Wire it.** `SaveButton_Click` calls `SettingsValidation.Validate(_workingProfiles, _workingApps)` and shows `problem.Message` in `ValidationInfoBar` (jumping to the tab comes in Task 6). Delete the two old private methods.
- [ ] **Step 5: Run** the full suite. Expected: all green. Build `dotnet build src/iRacingLauncher` → 0 errors.
- [ ] **Step 6: Commit** `Move Settings validation into a testable class and say "game"`.

### Task 2: Small view helpers

**Files:**
- Create: `src/iRacingLauncher/ViewModels/ListSelection.cs`
- Modify: `src/iRacingLauncher/SettingsWindow.xaml.cs` (`AppEditRowViewModel`)
- Test: `src/iRacingLauncher.Tests/SettingsViewHelpersTests.cs`

**Interfaces:**
- Produces:
  - `public static int ListSelection.AfterRemoval(int removedIndex, int countAfterRemoval)`: the index to select after removing `removedIndex`. Same index if still in range, else the last item, or `-1` when the list is now empty.
  - `AppEditRowViewModel.ShowVrTag : bool` = `UsesSteam && LaunchInVr`. Raises a change notification whenever `SteamAppId` or `LaunchInVr` changes. `UsesSteam` doubles as the Steam tag.

- [ ] **Step 1: Write failing tests:**
  - `[Theory] AfterRemoval`: (0,2)→0, (2,2)→1, (1,3)→1, (0,0)→-1.
  - `ShowVrTag_NeedsSteamAndVr`: id blank and VR on → false; id set and VR off → false; both → true.
  - `ShowVrTag_RaisesChangeWhenIdOrVrChanges`: subscribe to `PropertyChanged`, set `LaunchInVr`, then `SteamAppId`. `ShowVrTag` is raised both times.
- [ ] **Step 2: Run** filter → compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the full suite → green.
- [ ] **Step 5: Commit** `Add list-selection and VR tag helpers for the tabbed Settings`.

### Task 3: Tab shell and General tab

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`

**Interfaces:**
- Produces: `TabControl x:Name="SettingsTabs"` with three `TabItem`s named `GamesTab`, `AppsTab`, `GeneralTab`, in that order. Task 6 switches tabs by setting `SettingsTabs.SelectedItem`.

- [ ] **Step 1: Restructure.** Replace the outer `ScrollViewer`/`StackPanel` of `CardExpander`s with the `TabControl`, in the grid row the `ScrollViewer` occupies now (the title bar, InfoBar and Save/Cancel rows stay as they are). For now, move the existing Profiles and Selected Apps content into `GamesTab` and the existing Apps content into `AppsTab`, still in their own inner `ScrollViewer`, so this task is just the shell. Use WPF's `TabControl`, which WPF-UI styles.
- [ ] **Step 2: General tab.** Put Launch delay, Launch at Windows startup, Close to tray and Theme flat on one page as plain rows, with each control's label and help text kept as-is. Remove their `CardExpander`s. Keep the controls' `x:Name`s (`DelayBox`, `StartupToggle`, `CloseToTrayToggle`, `ThemeToggle`) so the code-behind doesn't change. Rewrite the `DelayBox` comment in the constructor so it no longer mentions a collapsed "Launch Behavior" section.
- [ ] **Step 3: Window size.** Raise `Width` from 620 to 860 (and `MinWidth="760"`), keeping `Height`, so list + details fit later with no horizontal scroll.
- [ ] **Step 4: Build** → 0 errors. Run the full suite → green.
- [ ] **Step 5: Manual check (UI Automation script, as used earlier this session, against a backed-up config):** three tabs present; Games is selected on open; the General tab shows the four settings with their current values; toggling the theme on General and pressing Cancel restores the theme; the config hash is unchanged after Cancel.
- [ ] **Step 6: Commit** `Split Settings into Games, Apps and General tabs`.

### Task 4: Games tab as list and details

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `ListSelection.AfterRemoval`.
- Produces: `ListBox x:Name="GameList"` (ItemsSource `_profileEditRows`). Task 6 selects a game by setting `GameList.SelectedItem` to the row whose `Model` is the problem's `Game`.

- [ ] **Step 1: Layout.**
  - **Left column:** `GameList` showing each name, with Move up / Move down / Delete beside the selected row or on every row (builder's choice), and **Add game** below.
  - **Right column:** a name `ui:TextBox` bound to the selected row's `Name`, a label "Apps launched for {name}, in order", then the existing `ProfileAppList` checklist unchanged. Drop the `EditingProfileLabel` "Selected for:" text in favor of that label.
  - **Remove** the "Edit Apps" button and its handler, plus the old Profiles help text.
- [ ] **Step 2: Selection.** `GameList.SelectionChanged` sets `_currentEditingProfile` to the selected row's `Model` and calls `LoadProfileAppRowsFromCurrentProfile()`. It never touches `_workingActiveProfile`. In the constructor, select the row for `_workingActiveProfile`. Also reload the checklist when `GamesTab` becomes selected, so app names and new apps from the Apps tab show up (Review Focus 1).
- [ ] **Step 3: Add, delete, move.**
  - **Add game:** creates `"New game"` (was `"New Profile"`) and selects it.
  - **Delete:** keeps the at-least-one rule, then selects `ListSelection.AfterRemoval(...)` and keeps today's `_workingActiveProfile` fallback.
  - **Move up / Move down:** keep the moved game selected.
- [ ] **Step 4: Build** → 0 errors; full suite → green.
- [ ] **Step 5: Manual check (UIA, backed-up config):**
  - Settings opens with your active game selected and its checklist showing.
  - Selecting ACC shows ACC's apps.
  - Renaming a game updates its list row live.
  - Add game selects "New game". Delete selects a neighbour, and the last game can't be deleted.
  - After Save, the main window's active game is unchanged.
  - The config is restored afterwards.
- [ ] **Step 6: Commit** `Make the Games tab a list with the selected game's apps beside it`.

### Task 5: Apps tab as list and details

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `ListSelection.AfterRemoval`, `AppEditRowViewModel.UsesSteam`, `.ShowVrTag`.
- Produces: `ListBox x:Name="AppList"` (ItemsSource `_appEditRows`; rename the old `AppEditList`). Task 6 selects an app by setting `AppList.SelectedItem` to the row whose `Model` is the problem's `App`.

- [ ] **Step 1: Layout.**
  - **Left column:** `AppList` rows show `Name`, a small "Steam" tag when `UsesSteam` and a "VR" tag when `ShowVrTag`, with **Add app** below.
  - **Right column:** a `ContentControl` (or `Grid` with `DataContext="{Binding SelectedItem, ElementName=AppList}"`) holding today's per-app fields with a small label above each: Name, Process name, Path (Browse, Auto-Find), Steam App ID (Find ID, Launch in VR), Launch arguments, FindStatus line, Steam hint, and a **Delete app** button. Every existing binding, converter, and `Tag="{Binding}"` handler wiring stays as it is.
- [ ] **Step 2: Selection.** Select the first app on open. **Add app** selects the new app. **Delete app** selects `ListSelection.AfterRemoval(...)`. When nothing is selected (zero apps), the right side shows "No apps yet. Press Add app to set one up."
- [ ] **Step 3: Build** → 0 errors; full suite → green.
- [ ] **Step 4: Manual check (UIA, backed-up config):**
  - Picking each app shows its fields.
  - The Steam tag shows on iRacing once its ID is set, and the VR tag shows on ACC with VR ticked.
  - Typing ACC's exe path still auto-fills 805550 and shows the "Steam game detected" line.
  - Renaming an app, then opening Games, shows the new name (Review Focus 1).
  - Add app selects it, and it appears in the Games checklist (Review Focus 3).
  - Delete app selects a neighbour.
  - Start Auto-Find on one app, switch to another, and the result lands on the first while the button comes back (Review Focus 4).
  - The config is restored afterwards.
- [ ] **Step 5: Commit** `Make the Apps tab a list with the selected app's fields beside it`.

### Task 6: Jump to the problem when Save is blocked, README, final check

**Files:**
- Modify: `src/iRacingLauncher/SettingsWindow.xaml.cs` (`SaveButton_Click`), `README.md`

**Interfaces:**
- Consumes: `SettingsProblem`, `SettingsTabs`, `GamesTab`, `AppsTab`, `GameList`, `AppList`.

- [ ] **Step 1: Jump.** When `Validate` returns a problem:
  - Set `SettingsTabs.SelectedItem` to the tab for `problem.Tab`.
  - Select the row whose `Model` is `problem.Game` or `problem.App`, and scroll it into view.
  - Then show the message, as today.
- [ ] **Step 2: README.** Update the Setup guide and Features text that mention the "Apps card", "Selected Apps", "Profiles" section and "Edit Apps". The new wording is: Settings has Games, Apps and General tabs; pick a game to tick its apps and set their order; pick an app to edit it. No em dashes in new text.
- [ ] **Step 3: Build** → 0 errors; full suite → green.
- [ ] **Step 4: Manual check (UIA, backed-up config):**
  - On the General tab, blank a game's name via Games, switch to General, and press Save. Settings switches to Games with that game selected, and the message names games.
  - Put `abc` as an App ID, go to Games, and press Save. Settings switches to Apps with that app selected.
  - Fix both, and Save closes. Restore the config.
  - With the default window size and 8 apps and 3 games, neither tab needs a scrollbar for the lists in normal use, or the lists scroll on their own without the whole page scrolling.
- [ ] **Step 5: Commit** `Jump to the problem when Settings can't save, and update the README`.
