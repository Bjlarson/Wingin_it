# HUD

Status: Draft
Last updated: 2026-10-16

## Summary

A minimal HUD that supports the core decision of fight or retreat. The pilot should always know their health, the nearest friendly airfield, and what's threatening them.

## HUD Elements

| Element | Purpose | Notes |
|---|---|---|
| Plane damage diagram | Top-down plane silhouette with each part colored by HP (green / yellow / red); missing parts greyed out | Body HP drives the overall state color; flashes in Critical |
| Invulnerability indicator | Shown while repairing | Shield icon + repair countdown. Prototype: gold bubble on the plane (visible to attackers) + "SHIELDED" line under the debug-HUD repair bar |
| Speed / throttle | Current speed, throttle setting | |
| Boost meter | Remaining boost | |
| Ammo | Rockets / missiles / bombs remaining | Gun overheat bar |
| Ability icon | Cooldown state | Prototype: an ABL bar with the ability's name, READY / ON / seconds left |
| Crosshair + lead pipper | Aiming | |
| Lock-on indicator | Missile lock progress | Prototype: a bracket closing in on the target, red **LOCK** when ready |
| Incoming missile warning | Directional, loud | Critical for "easy to avoid if spotted". Prototype: LOCK WARNING / LOCKED ON / flashing MISSILE + distance, an arrow toward the missile, and an EVADING bar while you turn hard. No sound yet |
| AA fire warning | Top center, flashing | Prototype: "AA FIRE — GET OUT FAST OR LOW" while an enemy airfield's AA gun is shooting at you. No direction arrow or sound yet |
| Cloud cover | Shows you're in cloud and whether you're hidden | Prototype: white screen wash that thickens deeper in; **IN CLOUD: HIDDEN FROM LOCKS AND AA** or **CLOUD EDGE: STILL VISIBLE**. Enemies behind or deep in cloud lose their bracket, pipper and readout (radar diamonds still show) |
| Minimap | Map, objectives, friendlies, radar-revealed enemies | Prototype: no minimap yet; radar-revealed enemies get a green diamond + distance in view |
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

Gear status icon is always visible when the gear is deploying or deployed: red = up, yellow = deploying, green = locked (safe to land). The prototype debug HUD always shows it as a GEAR light beside the throttle bar, flashing while deploying.

Touchdown feedback (prototype debug HUD): a short "LANDED" (green) or "BOUNCED: TOO FAST / SINKING TOO FAST" (yellow) message on touchdown, and the crash message names the reason ("CRASHED: GEAR NOT DOWN", "BAD ANGLE"). While the gear is out, vertical speed (V/S) shows under the gear light; on the runway it says GROUND.

When on approach to a friendly runway:
- Glide-slope guide (target lines or rings onto the runway)
- Speed indicator turns green when slow enough to land
- Repair zone highlighted on the ground

**Prototype (debug HUD, `GlideSlope` + `Airfield.GetApproach`):** the guide shows while the plane is **on approach**, not only once the gear is locked: the gear only locks ~30 m up, too late to line up. On approach means within 3 km of the aim point (or over the runway past it), inside a 30° cone widening from the runway edges, heading within 60° of the runway direction, and no more than 150 m above the glide path (so a high overflight or the air spawn doesn't trigger it).
- **Glide path:** 3° down to an aim point 60 m past the threshold (per airfield, on the `Airfield` component). On-path tolerance is ±6 m at the aim point, growing 2.5 m per 100 m out.
- **Rings:** the next six glide-path rings ahead, every 150 m, standing across the approach. Green while on the path, yellow when off it.
- **Aim bar:** a green dotted bar across the runway at the aim point, labeled AIM.
- **Repair zone:** a pulsing yellow outline labeled REPAIR on the approach and during the rollout, until the repair starts.
- **ILS box** right of the crosshair: the dot is where the path is (dot low = you're high, dot left = you're right of the centerline), the inner square is the tolerance. Under it: ON GLIDE PATH / HIGH 40 m / LOW 12 m / FLY LEFT, the distance to the aim point, and SPEED OK or SLOW DOWN against the class's max touchdown speed.
- **Speed indicator:** the SPD readout turns green at or below the max touchdown speed, yellow above it, while on approach or with the gear out. V/S turns green below the bounce sink rate, yellow for a bounce, red for a crash.

## Damage Feedback

- Healthy: none
- Damaged: light screen-edge smoke, warning tone
- Critical: red vignette, alarm, sputtering audio, "RETURN TO BASE" prompt

## Open Questions

- [ ] Diegetic vs screen-space HUD? (e.g. health shown as cockpit gauges in first-person view)
- [ ] Camera: third-person chase only, or a cockpit view option?
