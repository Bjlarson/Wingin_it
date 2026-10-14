# Aircraft Classes & Abilities

Status: Draft
Last updated: 2026-10-13

## Summary

Four classes. Each has a clear silhouette, a role, a fixed loadout, and one cooldown-based special ability. **Planes don't level up (decided).** Every player gets the same plane for a given class, available from spawn. Players set themselves apart with cosmetics, not stats (see [progression.md](../01-Game-Design/progression.md)).

## Class Selection

- Pick a class before spawning.
- **Proposed:** switch class on respawn, or while repaired and landed at a home airfield.
- **Proposed (Teams):** no hard class limits, but the spawn screen shows the team composition.

## Class Overview

| Class | Role | Health | Speed | Turn | Loadout (proposed) | Ability |
|---|---|---|---|---|---|---|
| Fighter | Balanced | Medium | Medium | Good | Guns + homing missiles | Barrel Roll |
| Interceptor | Hunter | Low | Very fast | Medium | Guns + homing missiles | Afterburner |
| Heavy Fighter | Tank | High | Slow | Poor | Guns + rockets + bombs | Armor Shield |
| Support | Team utility | Medium | Medium | Medium | Guns + rockets | Repair Aura (+ radar) |

## Fighter: Balanced

- Medium health, medium speed
- **Barrel Roll:** a quick evasive roll that breaks missile locks. Cooldown TBD.

## Interceptor: Hunter

- Very fast, low health
- Ideal for chasing down retreating, smoking planes
- **Afterburner:** a huge speed boost for a few seconds. Cooldown TBD.

## Heavy Fighter: Tank

- Lots of armor, slow turns
- Strong against ground objectives
- **Armor Shield:** temporary damage reduction (proposed: −50% for 4s). Cooldown TBD.

## Support: Team Utility

- **Repair Aura:** heals nearby allies in the air. This is the only in-air healing, so it should be valuable but limited (e.g. cannot heal itself, or heals itself slowly).
- **Radar:** reveals nearby enemies on teammates' minimaps.
- Has no role in Free For All. Should it be disabled in FFA, or get an FFA alternative?

## Counter Relationships (proposed)

```
Interceptor ──beats──► Support / fleeing planes
Heavy ───────beats───► Interceptor (out-tanks the burst)
Fighter ─────beats───► Heavy (out-turns it)
Support ─────enables─► Team sustain, counters attrition
```

## Open Questions

- [ ] Ability cooldowns and durations?
- [ ] Support in FFA?
- [ ] Is "Boost" (Shift) separate from the Interceptor's Afterburner? If so, how do they differ?
- [ ] More classes later, or more cosmetic variety within these four?
