# Settings window: tabs instead of one long page

Date: 2026-11-10
Branch: `app-improvements`

## Why

The Settings window is one long scrolling page of expandable sections. The
three big ones (Profiles, Selected Apps, Apps) open by themselves, and the
Apps section repeats a six-field card for every app (eight in the user's
real config), so finding anything means scrolling. The user's words: "the
auto opened sections and long scrolling".

## Goal

Each job in Settings gets its own tab, so you only see what you came for.
Nothing opens by itself and no tab needs a long scroll.

## Layout (chosen by the user from mockups)

Three tabs across the top: **Games**, **Apps**, **General**. Save and
Cancel stay at the bottom of the window and apply to all tabs at once, as
today.

### Games tab: list on the left, details on the right

- Left: the list of games (profiles) in their saved order, with **Add
  game**, and per-game **Move up / Move down** and **Delete**, as today.
- Right: for the selected game, its name (editable, as today's inline
  name box) and its app checklist with launch order: checkbox per app plus
  Move up / Move down, exactly what today's "Selected Apps" section does.
- Selecting a game in the list replaces the "Edit Apps" button and the
  separate "Selected Apps" section. Both go away.
- The UI says "game" where today's says "profile" (Add game, Delete game).
  The data model and config keep the name Profile. (Claude's call: the
  mockups the user picked said "Games"; the main window's "Profile" label
  is out of scope.)

### Apps tab: list on the left, details on the right

- Left: one row per app with its name, plus a small **Steam** tag when it
  has a Steam App ID and a **VR** tag when Launch in VR is on. **Add app**
  below the list.
- Right: the selected app's fields, each with a small label: Name, Process
  name, Path (Browse, Auto-Find), Steam App ID (Find ID, Launch in VR),
  Launch arguments, and a **Delete app** button. The status line
  (FindStatus) and the Steam hint stay under the fields, as today.
- Field behavior is unchanged: auto-fill of the Steam App ID, the
  Content Manager exception, Arguments disabled for Steam apps, etc.
- Adding an app selects it so its fields are ready to fill. Deleting an
  app selects a neighbour (or nothing if the list is empty).
- The list row's name and tags update live as the fields are edited.

### General tab

Launch delay, Launch at Windows startup, Close to tray, Theme, laid out
flat on one page (no expanders). The theme toggle still applies live and
is still reverted on Cancel/close without saving.

## Behavior decided by the user or implied by their choice

- Settings opens on the **Games** tab with the main window's active game
  selected (today's "Selected for:" defaults to the active profile).
  (Claude's call on which tab opens first.)
- **Selecting a game only picks which game you're editing. It never
  changes the active game** (the user's answer). Saving leaves the main
  window on whatever game it was on, exactly like today's "Edit Apps".
  Keep the active game and the selected game as separate state; no
  "active" marker is needed in the list.
- Wording switches from "profile" to "game" everywhere in Settings,
  including Save's validation messages ("Every game needs a name...",
  "Two games are both named... Game names have to be unique."), the
  default name for a new game ("New game"), and help text. Meaning and
  wording are otherwise unchanged. (Claude's call.)
- The Apps tab opens with the first app selected. (Claude's call.)
- Nothing else about what Settings does changes: same fields, same
  validation rules, same Save/Cancel semantics (edits a copy; Cancel
  discards).

## Constraints

- **Validation must not point at something hidden.** Save's existing
  checks name a game (blank/duplicate name) or an app (bad Steam App ID).
  When Save is blocked, switch to the tab that holds the problem and
  select that game or app, as well as showing the existing message.
- No horizontal scroll at the window's size; the window may grow wider to
  fit list + details side by side (today it's 620 x 560).
- Keyboard: the tab strip and lists are reachable with Tab/arrow keys
  (standard WPF controls give this; don't break it).
- Must keep working with zero apps and with one game (today you can't
  delete the last profile; keep that rule).
- Copy has no em dashes. Existing on-screen strings can keep theirs.
- Use the existing WPF-UI library (already a dependency) for tabs and
  lists; no new packages.

## Non-goals

- No change to the main window.
- No new settings, no change to what any field does.
- No renaming of the Profile type, config keys, or the main window label.

## Anti-goals (would make this a failure even if it works)

- A tab that still needs a long scroll in normal use (eight apps, three
  games).
- Losing an unsaved edit by switching tabs or switching which game/app is
  selected. Edits live in the working copy until Save or Cancel, exactly
  as today.

## Left to the builder

- Exact control choices (WPF-UI `TabControl` vs `NavigationView` in top
  mode, `ListBox` vs `ListView`), spacing, window size, list widths.
- How the Steam/VR tags look.
- Whether the details pane shows a short empty-state line when nothing is
  selected.
