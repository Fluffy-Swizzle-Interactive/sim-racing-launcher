# iRacing Launcher

A small Windows desktop app for starting your racing-sim toolchain — iRacing
plus whatever companion apps you run alongside it (voice coaching, paint
apps, telemetry overlays, etc.) — without hunting down each one manually or
accidentally launching something that's already running.

Extended off a simple launcher SwizzleShizzle made.

## Features

- **Live status checklist** — every configured app shows whether it's
  currently running, not running, or missing at its configured path,
  refreshed automatically.
- **Pick what to launch** — check only the apps you want, then hit
  **Launch Selected**. Already-running apps are skipped automatically.
- **Per-app Start/Stop** — launch or close any single app individually.
- **Settings**
  - Fully editable app list: add, remove, reorder, and rename entries.
  - **Auto-Find** — searches common install locations (Program Files,
    Program Files (x86), LocalAppData, ProgramData, Start Menu shortcuts)
    for an app's executable, so you don't have to know the exact path.
  - Configurable delay between staggered launches.
  - Optional "launch at Windows startup."
  - Light/dark theme.
- **System tray** — minimizes to the tray instead of the taskbar, with a
  quick-access menu (Show / Launch Selected / Exit).

## Getting started

On first run, every app in the list has a blank path — nothing is
preconfigured to someone else's machine. Open **Settings**, add the apps
you want to launch, and either **Browse** to their `.exe` or click
**Auto-Find** to search for it automatically.

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
