# Circa — design

**Goal.** A native macOS menu-bar app in the spirit of Iris/f.lux: shift screen
color temperature with the time of day at the user's location, reduce eye
strain and sleep disruption, plus PWM-flicker-safe dimming. Calm, smooth UI
using Liquid Glass where relevant.

## Decisions (from brainstorming)

- Location: **macOS Location Services**, with IP → cached fix → timezone
  fallbacks so the app always works.
- Scope: **core + comfort** — auto temperature curve, day/night sliders,
  extra software dimming, pause (1 h / until sunrise), launch at login —
  plus **flicker-free (PWM) dimming**.
- UI: status-bar **popover** (SwiftUI in an AppKit shell) rather than an
  NSMenu, so the surface can be calm and glassy. Design guided by the
  make-interfaces-feel-better and emil-design-eng skills: numeric-text
  transitions, tabular digits, zero-bounce springs ≤ 300 ms, symbol
  cross-fades, restrained glass (orb only — the popover chrome is already
  glass on macOS 26).

## Architecture

```
Solar clock (elevation) ──┐
Location chain ───────────┼─► CircaModel (5 s tick) ─► GammaController ─► display LUTs
Settings (UserDefaults) ──┘          │
                                     └─► Brightness (DisplayServices) for PWM mode
SwiftUI popover ◄─ @Published state ─┘
```

- **Temperature curve.** `blend = smoothstep((elevation + 6°) / 12°)`;
  `target = night + (day − night) · blend`. Ties the transition to civil
  twilight at any latitude/season, no sunrise tables needed.
- **Slew.** Periodic ticks move at most 150 K per 5 s toward target, so wake
  from sleep or location jumps drift like a sunset instead of snapping.
  Direct user input applies instantly.
- **Gamma.** Original per-display LUTs cached on first touch (preserves
  ColorSync calibration), then scaled by kelvin multipliers × dim. WindowServer
  auto-restores LUTs if the process dies; `restore()` on pause/quit.
- **Flicker-free mode.** On enable: fold current hardware brightness into a
  software `flickerComp` multiplier, pin backlight to 100%. Watchdog each tick
  absorbs brightness-key changes and re-pins. On disable/quit: hand perceived
  brightness back to the hardware. Floors at 15% comp / 12% total dim to keep
  the screen recoverable.
- **Failure handling.** No location → timezone estimate keeps a plausible
  curve. DisplayServices missing → toggle disabled with explanation. LUT calls
  failing on a display → that display is skipped.

## Testing

Manual + scripted verification: launch with a warm `dayTemp` override in
UserDefaults, read back the LUT from a helper process, assert the blue channel
max dropped while red stayed at 1.0; restore defaults after.
