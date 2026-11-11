# Changelog

## v1.1.0

Thanks to everyone on Reddit who tried v1.0.0 and sent feedback. Most of
this release comes straight from those suggestions.

### New

- **Set up each app once.** Apps like Crew Chief, Trading Paints and RaceLab
  are now configured in one place (Settings > Apps) and shared by every game.
  Each game just ticks the apps it uses and sets their launch order. Your
  existing setup is converted automatically the first time you open v1.1.0.
- **Launch arguments.** Give any app its own command-line options, such as a
  config file or a flag your tool needs.
- **Steam games and VR.**
  - Give a game its Steam App ID and it launches through Steam.
  - Tick **Launch in VR** to start it in Steam's VR mode (for games that
    use it, such as ACC).
  - Pick a game's exe from a Steam library and the App ID fills itself in.
    Apps you already set up inside a Steam library get it filled in the next
    time you open Settings (press Save to keep it).
  - Assetto Corsa's Content Manager keeps launching directly, so Steam
    doesn't skip it.
- **New Settings layout.** Settings is split into **Games**, **Apps** and
  **General** tabs instead of one long scrolling page.
  - Pick a game to see and order its apps.
  - Pick an app to edit it. Apps launched through Steam or in VR are tagged
    in the list.
  - If something stops Settings from saving, it takes you straight to the
    game or app with the problem.
- **Reorder your games** in Settings.
- **Close to tray** replaces Minimize to tray. When it's on, the X button
  hides the launcher to the tray instead of quitting. It's off by default.

### Changed

- The main window only lists the apps you've selected for the current game.
  Unticking one there hides it; tick it again for that game in Settings to
  bring it back.
- New installs launch Assetto Corsa through Content Manager by default.
- A flatter grey look, with a blue **Launch Selected** button.
- When a Steam game fails to start, the message tells you to check Steam and
  the App ID instead of the file path.

### Fixed

- Launching apps no longer unticks them for next time.
- **Stop All** now also stops a launch that's still in progress, instead of
  the launcher carrying on and restarting what you just stopped.
- Reordering apps for a game could save them in the wrong order.
- A damaged or hand-edited settings file no longer crashes the launcher.
- Settings are saved safely, so a power cut or forced shutdown mid-save
  can't wipe your configuration.
- Apps are started from their own folder, which some sim tools need to find
  their files.
