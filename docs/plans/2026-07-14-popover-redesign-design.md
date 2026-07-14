# Popover redesign: autopilot first, levers in Advanced

*2026-07-14 · applies to both apps (mac SwiftUI, Windows WinForms)*

## Problem

The popover leads with three tuning sliders (Day, Night, Night dim). Circa's
pitch is "set and forget", but the first thing the UI says is "here are the
knobs" — it reads as an equalizer, not an autopilot.

## Main view

Top-to-bottom: **status card → flicker-free card → pause row → Advanced →
footer**. The only slider left on main is flicker-free brightness (a genuine
"now" control: brightness keys are overridden while the backlight is pinned).

Status card:

1. Phase orb · phase title · master on/off switch (unchanged).
2. *Now* line: `6500 K · dimmed 12 % · flicker-free` — only active parts.
3. Sun track: slim capsule, ☀ left, 🌙 right; the dot's position maps the
   current solar elevation through the same `solarBlend` curve the engine
   uses, so dot and screen warmth always agree.
4. *Next* line, phase-aware; times are the engine's own ±6° twilight
   crossings, formatted per system clock style:
   - Day → `Tonight: 2300 K · dim 25 % · from ~20:47`
   - Twilight (evening) → `Settling to 2300 K · dim 25 % by ~21:30`
   - Night / morning twilight → `Morning: 6500 K from ~07:12`
   - Paused → `Resumes 13:04 — screen at system default`
   - Dim mentioned only when > 0; no crossing (polar day/night) → no time.

Flicker-free card: toggle + brightness slider only; the explainer paragraph
becomes a hover tooltip. Update banner and footer stay where they are.

## Advanced (collapsed by default, one flat list)

1. **Tune the curve**: Day / Night / Night dim sliders + *Reset to ideal*
   (shown only when customized). Caption: "Circa follows the sun on its own;
   these set the endpoints it moves between."
2. Divider, then switches: flicker-free only on power, auto-adjust brightness
   (mac only), launch at login, update automatically.

## Implementation

- Each `Solar` gains `nextPhaseChange(after:)`: scan elevation forward in
  10-minute steps over 26 h for a phase flip, bisect to the minute; nil when
  the sun never crosses (polar). Engine/model exposes the result; UI renders.
- Mac: restructure `ContentView`; sun track is a `Capsule` + positioned dot.
- Windows: `PopoverForm` reflows; sun track is one `Paint` handler; Advanced
  has no WinForms disclosure control, so it reuses the update-banner trick —
  controls authored below the fold, clicking "Advanced" grows/shrinks
  `ClientSize` (runtime positions use already-scaled bounds only). The reset
  link moves from the footer into the tune group.
- No settings schema changes. Ships as v1.0.5 on both — first end-to-end
  exercise of the self-updater.
