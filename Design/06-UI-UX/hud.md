# HUD

Status: Draft
Last updated: 2026-10-13

## Summary

A minimal HUD that supports the core decision of fight or retreat. The pilot should always know their health, the nearest friendly airfield, and what's threatening them.

## HUD Elements

| Element | Purpose | Notes |
|---|---|---|
| Plane damage diagram | Top-down plane silhouette with each part colored by HP (green / yellow / red); missing parts greyed out | Body HP drives the overall state color; flashes in Critical |
| Invulnerability indicator | Shown while repairing | Shield icon + repair countdown |
| Speed / throttle | Current speed, throttle setting | |
| Boost meter | Remaining boost | |
| Ammo | Rockets / missiles / bombs remaining | Gun overheat bar |
| Ability icon | Cooldown state | |
| Crosshair + lead pipper | Aiming | |
| Lock-on indicator | Missile lock progress | |
| Incoming missile warning | Directional, loud | Critical for "easy to avoid if spotted" |
| Minimap | Map, objectives, friendlies, radar-revealed enemies | |
| **Nearest airfield marker** | Arrow + distance to the closest friendly repair zone | Key to the retreat loop |
| Match timer + team score | Teams mode | |
| Score popups | "+50 Kill", "+20 Assist", "WING SHOT OFF!" | The coin summary is shown after the match |

## Post-Match Screen

- Scoreboard → personal score → **coins earned** (base + score + win bonus breakdown)
- "Watch ad to double coins" button (mobile, optional)
- Shortcut to the prize machine → requeue
| Kill feed | Recent kills | |
| Score / objective status | Mode-specific | |

## Landing Assist (proposed)

Gear status icon is always visible when the gear is deploying or deployed: red = up, yellow = deploying, green = locked (safe to land).

When near a friendly airfield with the gear locked:
- Glide-slope guide (target lines or rings onto the runway)
- Speed indicator turns green when slow enough to land
- Repair zone highlighted on the ground

## Damage Feedback

- Healthy: none
- Damaged: light screen-edge smoke, warning tone
- Critical: red vignette, alarm, sputtering audio, "RETURN TO BASE" prompt

## Open Questions

- [ ] Diegetic vs screen-space HUD? (e.g. health shown as cockpit gauges in first-person view)
- [ ] Camera: third-person chase only, or a cockpit view option?
