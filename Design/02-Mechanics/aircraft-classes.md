# Aircraft Classes & Abilities

Status: Draft
Last updated: 2026-10-14

## Summary

Four classes. Each has a clear silhouette, a role, a fixed loadout, and one cooldown-based special ability. **Planes don't level up (decided).** Every player gets the same plane for a given class, available from spawn. Players set themselves apart with cosmetics, not stats (see [progression.md](../01-Game-Design/progression.md)).

## Class Selection

- Pick a class before spawning.
- **Proposed:** switch class on respawn, or while repaired and landed at a home airfield.
- **Proposed (Teams):** no hard class limits, but the spawn screen shows the team composition.

## Class Overview

| Class | Role | Health | Speed | Turn | Loadout | Ability |
|---|---|---|---|---|---|---|
| Fighter | Balanced | Medium | Medium | Good | Guns + homing missiles | Barrel Roll |
| Interceptor | Hunter | Low | Very fast | Medium | Guns + homing missiles | Afterburner |
| Heavy Fighter | Tank | High | Slow | Poor | Guns + rockets + bombs | Armor Shield |
| Support | Team utility | Medium | Medium | Medium | Guns + rockets | Repair Aura (+ radar) |

**Loadouts are in the prototype** exactly as in the table (still to confirm with a playtest). Every class has machine guns; the rest is set by which weapon assets the class's `FlightStats` links (an empty slot = the class doesn't carry it). All classes share one tuning per weapon for now: 8 rockets, 2 missiles, 4 bombs. The HUD and the touch buttons only show the weapons the current class carries.

## Abilities (in the prototype)

Press **F** (gamepad A, touch ABILITY button) to activate. Each ability lasts a few seconds, then recharges. **The cooldown counts from when the effect ends.** Dying ends an active ability; respawning or switching class makes it ready again. Each class's `FlightStats` links one ability asset in `ScriptableObjects/Abilities/`. The debug HUD shows an **ABL** bar (draining while active, refilling while it recharges) with READY / ON / seconds left.

First-pass tuning (all **proposed**, to confirm in a playtest):

| Ability | Class | Lasts | Cooldown | Effect |
|---|---|---|---|---|
| Barrel Roll | Fighter | 0.8 s | 10 s | A full 360° roll toward the stick (right by default). Every missile chasing you loses you for good, and every lock on you starts over from zero |
| Afterburner | Interceptor | 4 s | 18 s | Min and max speed ×1.4, acceleration ×2.5. Needs a working engine |
| Armor Shield | Heavy Fighter | 4 s | 20 s | Every part takes 50% damage. A blue bubble shows it, and the HUD shows SHIELD −50% |
| Repair Aura | Support | 6 s | 25 s | Allies within 80 m heal 6% of each part's max HP per second; the Support heals itself at half that rate. Lost parts stay lost and a dead engine stays dead. A green disc shows the range |

Notes:
- The barrel roll holds its heading (no bank-turn while rolling) and ends wings-level, so it's a pure dodge.
- **Teams are a placeholder:** each plane has a team number. The player is team 0 and the test dummies are team 1, so in the test scene the aura only heals the Support itself. Weapons don't check teams yet (no friendly-fire rule).
- Bots don't use abilities yet (Phase 5).

## Fighter: Balanced

- Medium health, medium speed
- **Barrel Roll:** a quick evasive roll that breaks missile locks and shakes off incoming missiles. 10 s cooldown (proposed).

## Interceptor: Hunter

- Very fast, low health
- Ideal for chasing down retreating, smoking planes
- **Afterburner:** a huge speed boost for a few seconds (×1.4 speed for 4 s). 18 s cooldown (proposed).

## Heavy Fighter: Tank

- Lots of armor, slow turns
- Strong against ground objectives
- **Armor Shield:** temporary damage reduction (−50% for 4 s). 20 s cooldown (proposed).

## Support: Team Utility

- **Repair Aura:** heals nearby allies in the air. This is the only in-air healing, so it should be valuable but limited. **Proposed:** the Support heals itself at half the ally rate.
- **Radar:** reveals nearby enemies on teammates' minimaps. **Proposed:** passive and always on, 700 m around the Support. Until the minimap exists (Phase 6), the debug HUD marks revealed enemies with green diamonds and their distance.
- Has no role in Free For All. Should it be disabled in FFA, or get an FFA alternative?

## Counter Relationships (proposed)

```
Interceptor ──beats──► Support / fleeing planes
Heavy ───────beats───► Interceptor (out-tanks the burst)
Fighter ─────beats───► Heavy (out-turns it)
Support ─────enables─► Team sustain, counters attrition
```

## Open Questions

- [x] Ability cooldowns and durations? → first-pass values above (proposed, needs a playtest)
- [ ] Support in FFA?
- [x] Is "Boost" (Shift) separate from the Interceptor's Afterburner? → **Yes (proposed).** Boost is every class's short, metered burst; Afterburner is a longer, bigger burst on a cooldown that doesn't touch the boost meter. The two stack.
- [ ] Is radar passive (as built) or part of the Repair Aura activation?
- [ ] Friendly fire: can teammates' guns and blasts hurt each other? (Needed once real teams exist.)
- [ ] More classes later, or more cosmetic variety within these four?
