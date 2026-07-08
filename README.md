# Circa

A calm macOS menu-bar app that warms and dims your screen with the sun — like
f.lux / Iris, built natively in Swift with a Liquid Glass popover UI.

**[Download for Mac](https://github.com/limone-eth/circa/releases/latest/download/Circa.zip)**
(macOS 14+, Apple silicon · ad-hoc signed, so first launch is right-click → Open)
· MIT licensed · [circa.watch](https://circa.watch)

## What it does

- **Follows the sun.** Computes solar elevation at your location every 5 s
  (no weather API — pure astronomy) and blends your screen between a day and
  a night color temperature across civil twilight (+6°…−6° sun elevation).
- **Location aware.** Uses macOS Location Services; falls back to IP
  geolocation, then a cached fix, then a timezone estimate. Nothing leaves
  your Mac except the optional IP lookup.
- **Night dim.** Software-dims below the hardware minimum, riding the same
  solar curve as the color: zero effect in daylight, full strength (default
  25%) at night. Defaults follow the circadian-hygiene playbook: untouched
  6500 K days, deep 2300 K amber nights (~85% of blue removed). A "Reset to
  ideal" button appears whenever you stray from the recommended curve.
- **Flicker-free dimming (PWM-safe).** Pins the LED backlight at 100% — where
  it doesn't strobe — and produces your chosen brightness in the gamma table
  instead. Brightness keys keep working; changes are absorbed into software
  dim and the backlight is re-pinned.
- **Pause** for an hour or until sunrise; **launch at login**; everything in a
  small glass popover off the menu bar.

## Build & install

```sh
./build.sh install    # builds, copies to ~/Applications, launches
```

Requires Xcode command-line tools. The app is ad-hoc signed; macOS will ask
for Location permission on first run (denying it just falls back to IP).

## Tips

- Turn **off** System Settings → Displays → Night Shift so the two don't fight.
- With flicker-free dimming on, also turn off "Automatically adjust brightness"
  (the ambient light sensor and the watchdog otherwise negotiate forever).
- Color changes happen in the display LUT, so screenshots and screen shares
  are unaffected (colleagues see normal colors).

## How the pieces fit

| File | Role |
| --- | --- |
| `Sources/Solar.swift` | Solar elevation + next-sunrise math (NOAA low-precision ephemeris) |
| `Sources/Gamma.swift` | Kelvin → RGB multipliers (Tanner Helland), applied to cached ColorSync gamma tables |
| `Sources/Brightness.swift` | Hardware backlight via private DisplayServices (dlopen, degrades gracefully) |
| `Sources/Location.swift` | CoreLocation → IP → cache → timezone fallback chain |
| `Sources/Model.swift` | The engine: 5 s tick, slewed transitions, pause, flicker watchdog |
| `Sources/ContentView.swift` | SwiftUI popover (Liquid Glass on macOS 26+) |
| `Sources/main.swift` | AppKit shell: status item, popover, right-click quit |
