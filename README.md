<p align="center">
  <img src="logos/lockup-dark.svg#gh-dark-mode-only" alt="Sim Racing Launcher" />
  <img src="logos/lockup-light.svg#gh-light-mode-only" alt="Sim Racing Launcher" />
</p>

A small Windows desktop app for starting your sim racing toolchain —
whichever sim you run (iRacing, Assetto Corsa Competizione, Assetto Corsa,
or anything else) plus whatever companion apps go with it (voice coaching,
paint apps, telemetry overlays, etc.) — without hunting down each one
manually or accidentally launching something that's already running.

Extended off a simple launcher SwizzleShizzle made.

## Features

- **Apps registry, shared across profiles** — configure each companion app
  (path, process name, launch arguments) once on the **Apps** tab in
  Settings, then tick it for whichever games need it on the **Games** tab.
  Editing an app's path in one place updates it everywhere it's used.
- **Steam games and VR**: give an app its Steam App ID and it launches
  through Steam. Tick **Launch in VR** to start it in Steam's VR mode.
  Pick a game's exe from a Steam library and the App ID fills itself in.
- **Profiles per sim** — each sim gets its own named profile with its own
  selected-apps checklist and launch order. Switch between them from a
  dropdown on the main window; nothing else needs reconfiguring. Ships with
  three ready-made profiles (see [Setup guide](#setup-guide) below).
- **Live status checklist** — every app in the active profile shows whether
  it's currently running, not running, or missing at its configured path,
  refreshed automatically.
- **Pick what to launch** — the main window only lists apps currently
  selected for the active profile. Uncheck one to drop it from the launch
  set for **Launch Selected**; unchecking removes its row immediately
  rather than leaving it there unchecked, since it stays selected for every
  other game that uses it. Bring it back onto this list any time by
  ticking it for that game on the **Games** tab in Settings. An app whose path isn't
  valid, or one that's already running, has its checkbox (and per-row
  Start) grayed out, so you can't uncheck it from here directly — stop a
  running app first if you want to hide it this way. If anything fails to
  start, a banner tells you what and why.
- **Stop All** — stops every currently-running app in the active profile in
  one click, regardless of what's checked.
- **Per-app Start/Stop** — launch or close any single app individually.
- **Settings**, split into three tabs:
  - **Games**: your games (profiles) on the left; add, rename, reorder or
    delete them. Pick one to see its apps on the right, tick the ones it
    uses and set their launch order with the up/down arrows. Picking a game
    here only chooses what you're editing; the main window stays on its
    current game.
  - **Apps**: your apps on the left, tagged **Steam** or **VR** when they
    launch that way. Pick one to edit its name, process name, path, Steam
    App ID and launch arguments. Deleting an app removes it from every game;
    to drop it from just one game, untick it on the **Games** tab instead.
    **Auto-Find** searches common install locations (Program Files,
    Program Files (x86), LocalAppData, ProgramData, Start Menu shortcuts)
    for an app's executable, so you don't have to know the exact path.
  - **General**:
    - Delay between staggered launches.
    - Optional "launch at Windows startup".
    - Optional "close to tray", off by default. When on, closing the main
      window (the X button) hides it to the tray instead of exiting; use the
      tray icon's **Exit** to actually quit. When off, closing the window
      exits the app as normal.
    - Light/dark theme.
  - If something stops Settings from saving, it switches to the tab and
    the game or app with the problem.
- **System tray** (when "close to tray" is enabled) — the window hides to
  the tray instead of exiting, with a quick-access menu (Show / Launch
  Selected / Exit). Tray **Exit** always fully quits, regardless of the
  setting.

## Setup guide

1. **Launch the app.** On first run it comes with three starter profiles —
   **iRacing**, **ACC**, and **AC** — and a shared **Apps** registry
   pre-filled with app *names* but no paths, since no two installs live in
   the same place.
2. **Pick a profile** from the dropdown at the top of the main window (or
   just use whichever is already active).
3. **Open Settings** (gear icon, bottom-left). It opens on the **Games**
   tab with your current game picked.
4. **Point each app at its executable**, on the **Apps** tab. Pick each
   app in the list, then either:
   - Click **Auto-Find** to search common install locations automatically, or
   - Click **Browse** and pick the `.exe` yourself.

   This is configured once per app and applies to every profile that uses
   it. Want another companion tool? **Add app** and fill in a name, process
   name (used to detect if it's already running — no `.exe`), path, and
   optionally launch arguments.

   **Steam games (and VR):** if the path you pick is inside a Steam
   library, the **Steam App ID** box fills in by itself and the game will
   launch through Steam. (It's left alone if the app already has launch
   arguments, or if you picked Content Manager for Assetto Corsa, which
   keeps launching directly; press **Find ID** to fill it anyway.) You can also type the
   ID yourself: it's the number in the game's Steam store URL, like
   `2399420` for Le Mans Ultimate. Then:
   - Tick **Launch in VR** to start the game in Steam's VR mode.
   - Keep the **process name** set to the game's own exe name, not Steam,
     so the launcher can tell when the game is already running.
   - Extra launch options for a Steam game go in Steam itself (right-click
     the game, Properties, Launch Options). The launcher's arguments box
     is turned off for Steam games.
   - Clear the App ID to go back to launching the exe directly.
5. **Choose which apps each game uses**, on the **Games** tab: pick the
   game, tick the apps you want, untick the ones you don't, and use the
   up/down arrows to set their launch order. Unticking an app here only
   affects that game; it stays set up for any other game that uses it.
   (Deleting an app on the **Apps** tab is different: that removes it from
   every game.)
6. **Don't run a sim listed here, or run one that isn't?** Use **Add game**
   on the **Games** tab to create a new one, or delete one you don't need.
   You always need at least one.
7. **Save.** Back on the main window, check the apps you want and hit
   **Launch Selected** — or start/stop things individually with the
   per-row buttons.
8. Optional: on the **General** tab, set a launch delay between apps, turn on **launch
   at Windows startup**, or turn on **close to tray** if you'd rather the
   X button hide the window instead of exiting (use the tray icon's Exit
   to quit when that's on).

## Requirements

- Windows 10/11 (x64)
- Nothing else — the published build is self-contained and includes its
  own .NET runtime.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet test
```

To produce a standalone, self-contained `.exe`:

```bash
dotnet publish src/iRacingLauncher/iRacingLauncher.csproj -c Release
```

The published executable will be under
`src/iRacingLauncher/bin/Release/net10.0-windows/win-x64/publish/`.

## Tech stack

.NET 10, WPF, and [WPF-UI](https://github.com/lepoco/wpfui) for the Fluent
Design interface.

## Code signing

Releases aren't code-signed yet, which is why Windows shows a SmartScreen
warning on first run. This project is applying for free code signing
through the [SignPath Foundation](https://signpath.org) for open-source
projects. A GitHub Actions workflow
(`.github/workflows/build-and-sign.yml`) is already wired up for it; once
the application is approved, finish the setup by:

1. Creating a project in the SignPath dashboard and a signing policy
   named `release-signing` (and optionally `test-signing` for non-release
   builds).
2. Adding `SIGNPATH_API_TOKEN` as a repo secret and
   `SIGNPATH_ORGANIZATION_ID` as a repo variable
   (Settings → Secrets and variables → Actions).

The workflow signs automatically from there — nothing else to change.

## Privacy

This app makes no network requests and collects no data of any kind. Its
only I/O is starting/stopping the apps you configure and reading/writing its
own local settings file.

## License

[MIT](LICENSE)
