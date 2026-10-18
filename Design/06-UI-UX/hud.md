# HUD

Status: Draft
Last updated: 2026-10-17

## Summary

A minimal HUD that supports the core decision of fight or retreat. The pilot should always know their health, the nearest friendly airfield, and what's threatening them.

## HUD Elements

| Element | Purpose | Notes |
|---|---|---|
| Plane damage diagram | Top-down plane silhouette with each part colored by HP (green / yellow / red); missing parts greyed out | Body HP drives the overall state color; flashes in Critical. **Built:** left panel of the cluster, nose up; lost wings and tail are grey outlines, a dead engine is grey; HEALTHY / DAMAGED / CRITICAL under it |
| Invulnerability indicator | Shown while repairing | Shield icon + repair countdown. Prototype: gold bubble on the plane (visible to attackers), a pulsing gold ring around the damage diagram labeled SHIELDED (also for the Heavy's Armor Shield), and the repair countdown in the debug overlay |
| Speed / throttle | Current speed, throttle setting | **Built:** big km/h numeral (green / yellow against the touchdown limit on approach or with the gear out), altitude, THR bar, class name, flashing LOW SPEED |
| Boost meter | Remaining boost | **Built:** BST bar, orange while boosting |
| Ammo | Rockets / missiles / bombs remaining | Gun overheat bar. **Built:** right panel, only the weapons the class carries: GUN heat (HOT / JAM, guns left), RKT / MSL / BMB count (EMPTY, LOCK) |
| Ability icon | Cooldown state | **Built:** an ABL bar with the ability's name under it, READY / ON / seconds left |
| Crosshair + lead pipper | Aiming | **Built:** gapped boresight cross, green mouse-aim circle, a bracket on every visible plane in gun range (team-tinted for friendlies), a red pipper on enemies, an orange rocket diamond, hit marker, the bomb impact circle, and a readout of the enemy nearest the nose under the score bar |
| Lock-on indicator | Missile lock progress | **Built:** a bracket closing in on the target, red **LOCK** when ready |
| Incoming missile warning | Directional, loud | Critical for "easy to avoid if spotted". **Built:** LOCK WARNING / LOCKED ON / flashing MISSILE + distance, an arrow around the crosshair toward the missile, and an EVADING bar while you turn hard. No sound yet |
| AA fire warning | Top center, flashing | Prototype: "AA FIRE — GET OUT FAST OR LOW" while an enemy airfield's AA gun is shooting at you. No direction arrow or sound yet |
| Cloud cover | Shows you're in cloud and whether you're hidden | Prototype: white screen wash that thickens deeper in; **IN CLOUD: HIDDEN FROM LOCKS AND AA** or **CLOUD EDGE: STILL VISIBLE**. Enemies behind or deep in cloud lose their bracket, pipper and readout (radar diamonds still show) |
| Minimap | Map, objectives, friendlies, radar-revealed enemies | **Built:** top left, north up, the whole level inside the boundary ring: a top-down photo taken when the match starts (no clouds, fog or planes), airfields as team-colored squares (home fields outlined), friendlies and radar-revealed enemies as team-colored arrows, you as a white arrow. Radar-revealed enemies also get a green diamond + distance in view. Objectives come with Phase 8 |
| **Nearest airfield marker** | Arrow + distance to the closest friendly repair zone | Key to the retreat loop. **Built:** a chevron over the zone with the field's name and distance, or an arrow on the screen edge when it's out of view; bright, pulsing and labeled REPAIR once you're Damaged or missing a part (and its minimap square gets a pulsing ring). Hidden on approach (the landing assist takes over) and on the ground |
| Match timer + team score | Teams mode | Prototype (`MatchScreens`): a top-center bar, WEST score · clock · EAST score, the clock flashing gold in the last 30 s |
| Score popups | "+50 Kill", "+20 Assist", "WING SHOT OFF!" | The coin summary is shown after the match. **Built:** under the crosshair, rising and fading over 1.8 s, up to 4: your own awards from `MatchScore.Awarded` (+50 KILL, +20 ASSIST, objectives), and WING / TAIL / FIN SHOT OFF! or ENGINE KNOCKED OUT! when your hit takes a part off an enemy |

## Layout (built 2026-10-17)

`FlightHUD`, IMGUI in the glass style (shared with the match screens through `GlassGUI`), at 1080 px reference height:

- **Top left:** minimap. **Top center:** match clock + team score (`MatchScreens`), the target readout under it. **Top right:** kill feed (below the touch CLASS button when touch controls are on).
- **Center:** crosshair, pippers, lock bracket, missile warning, popups under the crosshair, the home marker or its edge arrow.
- **Bottom center:** the instrument cluster, three glass panels: damage diagram · speed / altitude / throttle / boost / gear · weapons + ability. With touch controls on it sits in the gap between the stick and the buttons, shrinking (never below half size) on narrow screens (`HudMath.ClusterRect`).
- **Above the cluster:** RETURN TO BASE when Critical (`DamageFeedback`, see [Damage Feedback](#damage-feedback)).
- **Under the score bar:** the center warning (death + respawn timer, combat area edge, altitude limit, AA fire), flashing; the missile warning under it.
- **Above the cluster:** a glass status toast: LANDED / BOUNCED, READY FOR TAKEOFF, REPAIRING with a bar and SHIELDED, REPAIRED / REPAIR CANCELLED. It moves up while RETURN TO BASE is showing.
- **Right of the crosshair on approach:** the ILS panel; the glide rings, aim bar and repair zone outline are drawn in the world (FlightHUD.Landing.cs). Approach-lane gates get a diamond with the lane name and distance.
- **Cloud:** the white wash under everything, and IN CLOUD / CLOUD EDGE under the crosshair.
- Only the dev readouts (bottom left) and the F1 help (hidden when a match starts) are still in the dev overlay (`FlightDebugHUD`), both behind the Developer setting.

## Post-Match Screen

- Scoreboard → personal score → **coins earned** (base + score + win bonus breakdown)
- "Watch ad to double coins" button (mobile, optional)
- Shortcut to the prize machine → requeue
| Kill feed | Recent kills | **Built:** top right, the last 5 for 8 s: KILLER DOWNED VICTIM in team colors (an AA kill names the team's AA, an uncredited crash says CRASHED, bombed ground targets say DESTROYED); lines with you in them get the selection rail. Keeps counting while you're dead |
| Score / objective status | Mode-specific | |

## Landing Assist (proposed)

Gear status icon is always visible when the gear is deploying or deployed: red = up, yellow = deploying, green = locked (safe to land). The flight panel always shows it as a GEAR light beside the throttle bar, flashing while deploying.

Touchdown feedback (status toast): a short "LANDED" (green) or "BOUNCED: TOO FAST / SINKING TOO FAST" (yellow) message on touchdown, and the crash message names the reason ("CRASHED: GEAR NOT DOWN", "BAD ANGLE"). While the gear is out, vertical speed (V/S) shows under the gear light; on the runway it says GROUND.

When on approach to a friendly runway:
- Glide-slope guide (target lines or rings onto the runway)
- Speed indicator turns green when slow enough to land
- Repair zone highlighted on the ground

**Built (`FlightHUD`, `GlideSlope` + `Airfield.GetApproach`):** the guide shows while the plane is **on approach**, not only once the gear is locked: the gear only locks ~30 m up, too late to line up. On approach means within 3 km of the aim point (or over the runway past it), inside a 30° cone widening from the runway edges, heading within 60° of the runway direction, and no more than 150 m above the glide path (so a high overflight or the air spawn doesn't trigger it).
- **Glide path:** 3° down to an aim point 60 m past the threshold (per airfield, on the `Airfield` component). On-path tolerance is ±6 m at the aim point, growing 2.5 m per 100 m out.
- **Rings:** the next six glide-path rings ahead, every 150 m, standing across the approach. Green while on the path, yellow when off it.
- **Aim bar:** a green dotted bar across the runway at the aim point, labeled AIM.
- **Repair zone:** a pulsing yellow outline labeled REPAIR on the approach and during the rollout, until the repair starts.
- **ILS panel** (glass) right of the crosshair: the dot is where the path is (dot low = you're high, dot left = you're right of the centerline), the inner square is the tolerance. Under it: ON GLIDE PATH / HIGH 40 m / LOW 12 m / FLY LEFT, the distance to the aim point, and SPEED OK or SLOW DOWN against the class's max touchdown speed.
- **Speed indicator:** the SPD readout turns green at or below the max touchdown speed, yellow above it, while on approach or with the gear out. V/S turns green below the bounce sink rate, yellow for a bounce, red for a crash.

## Damage Feedback

- Healthy: none
- Damaged: light screen-edge smoke, warning tone
- Critical: red vignette, alarm, sputtering audio, "RETURN TO BASE" prompt

**Built (2026-10-17), `DamageFeedback` on the Flight HUD object** (hides with it, so the alarm stops under the class picker). The level follows the **repair tiers** (`DamageFeedbackMath.Alert`), so what the pilot feels matches how long the fix takes:

| Level | When | Feedback |
|---|---|---|
| None | Light repair tier (body 60%+, nothing lost) | none |
| Warning | Heavy tier: body Damaged, a stabilizer lost, or the engine dead | dark smoke drifting in from the screen edges (two noisy masks breathing out of step); a two-beep tone when it starts |
| Critical | Critical tier: body Critical **or a wing lost** | the smoke plus a red edge vignette pulsing like a heartbeat (two beats every 0.9 s); a looping hi-lo alarm; flashing **RETURN TO BASE** above the instrument cluster with REPAIR AT *field* *distance* (the nearest friendly field) |

- **Every hit** flashes the screen edges red for 0.35 s, harder for bigger hits (25–75%), full strength when a part is shot off. Losing a part also beeps, unless it made the plane Critical (then the alarm says it).
- The alarm and prompt are off on the ground, while dead, and **on approach to a friendly runway** (the landing assist takes over; the pilot is already going home). The vignette stays.
- Sounds are synthesized placeholders (`HudTones`) until the audio pass. **Not yet:** sputtering engine audio (there's no engine sound at all yet).

## Open Questions

- [ ] Diegetic vs screen-space HUD? (e.g. health shown as cockpit gauges in first-person view)
- [ ] Camera: third-person chase only, or a cockpit view option?
