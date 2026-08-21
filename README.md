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

- **Profiles per sim** — each sim gets its own named profile with its own
  app list. Switch between them from a dropdown on the main window; nothing
  else needs reconfiguring. Ships with three ready-made profiles (see
  [Setup guide](#setup-guide) below).
- **Live status checklist** — every app in the active profile shows whether
  it's currently running, not running, or missing at its configured path,
  refreshed automatically.
- **Pick what to launch** — check only the apps you want, then hit
  **Launch Selected**. Apps that are already running, or whose path isn't
  valid, gray out automatically so they can't be selected. If anything
  fails to start, a banner tells you what and why.
- **Stop All** — stops every currently-running app in the active profile in
  one click, regardless of what's checked.
- **Per-app Start/Stop** — launch or close any single app individually.
- **Settings**
  - Add, rename, or delete profiles; each has its own fully editable app
    list (add, remove, reorder, rename entries).
  - **Auto-Find** — searches common install locations (Program Files,
    Program Files (x86), LocalAppData, ProgramData, Start Menu shortcuts)
    for an app's executable, so you don't have to know the exact path.
  - Configurable delay between staggered launches.
  - Optional "launch at Windows startup."
  - Optional "minimize to system tray" — on by default, but turn it off if
    you'd rather minimizing just go to the taskbar like any other app.
  - Light/dark theme.
- **System tray** (when enabled) — minimizes to the tray instead of the
  taskbar, with a quick-access menu (Show / Launch Selected / Exit).

## Setup guide

1. **Launch the app.** On first run it comes with three starter profiles —
   **iRacing**, **ACC**, and **AC** — each pre-filled with app *names* but
   no paths, since no two installs live in the same place.
2. **Pick a profile** from the dropdown at the top of the main window (or
   just use whichever is already active).
3. **Open Settings** (gear icon, bottom-left) and find that profile under
   **Profiles** → **Edit Apps**.
4. **Point each app at its executable.** For every entry, either:
   - Click **Auto-Find** to search common install locations automatically, or
   - Click **Browse** and pick the `.exe` yourself.

   Don't want an app in this profile? Delete its row. Want another
   companion tool? **Add App** and fill in a name, process name (used to
   detect if it's already running — no `.exe`), and path.
5. **Don't run a sim listed here, or run one that isn't?** Use **Add
   Profile** to create a new one, or delete a profile you don't need — you
   always need at least one.
6. **Save.** Back on the main window, check the apps you want and hit
   **Launch Selected** — or start/stop things individually with the
   per-row buttons.
7. Optional: in Settings, set a launch delay between apps, turn on **launch
   at Windows startup**, or turn off **minimize to system tray** if you'd
   rather it behave like a normal window.

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
