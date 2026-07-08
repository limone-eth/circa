# Circa

A calm menu-bar / tray app for **macOS and Windows** that warms and dims your
screen with the sun — like f.lux / Iris, built natively (Swift on macOS with a
Liquid Glass popover, C#/.NET on Windows).

**[Download for Mac](https://github.com/limone-eth/circa/releases/latest/download/Circa.zip)**
(macOS 14+, Apple silicon · ad-hoc signed, so first launch is right-click → Open)

**[Download for Windows](https://github.com/limone-eth/circa/releases/latest/download/Circa-windows-x64.zip)**
(Windows 10/11 x64 · unsigned, so SmartScreen asks once: More info → Run anyway)

MIT licensed · [circa.watch](https://circa.watch)

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
  instead, controlled by an in-app Brightness slider. Off by default; when on,
  it suspends itself on battery ("only on power adapter", default on) because
  a pinned backlight costs real runtime, and it parks macOS auto-brightness
  while engaged, restoring it after.
- **Pause** for an hour or until sunrise; **launch at login**; everything in a
  small glass popover off the menu bar (macOS) or tray panel (Windows).

## Build & install

```sh
./build.sh install    # builds, copies to ~/Applications, launches
```

Requires Xcode command-line tools. The app is ad-hoc signed; macOS will ask
for Location permission on first run (denying it just falls back to IP).

## Tips

- Turn **off** Night Shift (macOS) / Night Light (Windows) so the two don't fight.
- Flicker-free mode manages the ambient light sensor for you on macOS (off
  while engaged, restored after); its Brightness slider replaces the keys
  while the backlight is pinned.
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

## Windows version

`windows/` contains a C#/.NET 8 port with the same engine: solar-elevation
curve, gamma ramps via `SetDeviceGammaRamp`, night-scaled dim, and
battery-aware flicker-free mode (backlight pinned via WMI). Function-first
WinForms tray app; IP-based location (city-level is plenty for sunrise math).

Build: `dotnet publish windows/Circa.Win.csproj -c Release -r win-x64
--self-contained -p:PublishSingleFile=true` — or grab the artifact from the
"Windows build" GitHub Action. Note: deep-amber settings below ~3400 K may be
clamped by the default Windows gamma limit; the app degrades gracefully, and
the f.lux-style `GdiIcmGammaRange` registry unlock removes the limit.
