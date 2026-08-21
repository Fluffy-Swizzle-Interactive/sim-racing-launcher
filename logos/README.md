# Sim Racing Launcher — logo package

Mark: **Stagger** — three queued entries, the selected one firing right.

## Files
- `SimRacingLauncher.ico` — Windows app icon (256, 128, 64, 48, 32, 24, 16 px; the 16 px frame uses the simplified mono drawing)
- `mark-two-tone.svg` — primary, for dark grounds (#161826)
- `mark-accent-mono.svg` — single-color accent
- `mark-light-ground.svg` — for light backgrounds
- `mark-greyscale.svg` — print / disabled states
- `mark-16px.svg` — tray glyph, thicker bars, mono
- `png/` — rasters at each icon size

## Colors
- Accent (active row + arrow): `#9184d9`
- Queued rows: `#423a6a`
- Ground: `#161826`

## Construction
120-unit grid. Bars 14u tall, 15u gutters, fully rounded ends. Arrow apex at x=108, vertically centered on the middle bar. Clear space: 8u on all sides.

## Usage
- Below 24 px, use the mono drawing — the two-tone contrast disappears.
- Don't recolor the arrow separately from the active bar; they are one signal.
- Don't add a background plate on dark UI; the mark sits directly on the ground.
