# Open Questions

Status: Living document
Last updated: 2026-10-17

## Decided

| Topic | Decision | Date |
|---|---|---|
| Dimension | Full 3D | 2026-10-13 |
| Platforms | PC and mobile from day one | 2026-10-13 |
| Cross-play | Yes, PC and mobile in the same matches | 2026-10-13 |
| Players per match | Up to 16 (8v8 in Teams) | 2026-10-13 |
| Match length | ~5 minutes | 2026-10-13 |
| Progression | **Players level, not planes.** All planes of a class are equal | 2026-10-13 |
| Persistent progression | Player level, coins, cosmetics; nothing that affects power | 2026-10-13 |
| Economy | Coins per match (scaled by score) → random cosmetic prize machine, Crossy Road style | 2026-10-13 |
| Business model | Free to play: cosmetic sales, coin sales, rewarded ads for coins | 2026-10-13 |
| Networking | Dedicated servers (server-authoritative) | 2026-10-13 |
| Damage model | Per-part (GTA style). Lost parts change flight until repair or crash; body at 0 = explode; lost parts can't be hit again | 2026-10-13 |
| Landing gear | Deploys automatically near the ground; landing before it's fully out = crash | 2026-10-13 |
| Repair | Fixed times (5 / 10 / 15 s); restores all parts | 2026-10-13 |
| Repair safety | Invulnerable while the repair timer runs | 2026-10-13 |
| Airfield defense | AA guns at home airfields | 2026-10-13 |
| First playable mode | Teams | 2026-10-13 |
| Team / scope | Solo developer, part-time | 2026-10-13 |
| PC flight controls | Mouse-aim is the default (plane steers toward the cursor); a direct mode (A/D roll, mouse/arrows pitch, Q/E yaw) is one key away (M). Keys override mouse-aim per axis | 2026-10-14 |
| Terrain collision | Instant crash, then respawn (prototype). Revisit once Phase 2 adds HP | 2026-10-14 |
| Part list | Six parts: body, L wing, R wing, horizontal stabilizer, vertical stabilizer, engine. The engine dies and smokes but never detaches (hitbox stays) | 2026-10-14 |
| Gun mounting | Wing-mounted: losing a wing halves firepower | 2026-10-14 |
| Class switching | On every respawn (pick while the respawn timer runs), or parked and repaired at a **home** airfield (not a captured one). The class picker shows how many teammates fly each class; no class limits → [aircraft-classes](../02-Mechanics/aircraft-classes.md#class-selection) | 2026-10-17 |
| Teams tie-breaker | A tie when time runs out is a **draw**. No overtime for now; revisit after playtests | 2026-10-17 |
| Spawning | Per level (`LevelSettings`): **Air** (over the home airfield at cruise) or **Runway** (parked at the threshold, take off yourself). After a repair, always a runway takeoff → [flight-model](../02-Mechanics/flight-model.md#spawning) | 2026-10-15 |

## Still Open

### Blocking for Prototype

1. **Netcode library** (NGO / Fish-Networking / Fusion) → [multiplayer](../08-Technical/multiplayer.md)

### Design

2. Weapon button mapping for rockets / bombs (proposed: bombs on their own button, B / gamepad Y); confirm per-class loadouts (implemented as proposed, awaiting a playtest).
3. Support class role in Free For All. Also: ability tuning (cooldowns, durations, strengths) is first-pass; Support radar is passive (proposed); friendly-fire rule for when teams exist → [aircraft-classes](../02-Mechanics/aircraft-classes.md#abilities-in-the-prototype)
4. Prize machine: spin price, duplicate rule, rarity tiers.

### Business / Scope

5. **Loot box compliance:** selling coins for random items needs odds disclosure and region handling → [economy](../01-Game-Design/economy-and-monetization.md#-loot-box-regulation)
6. Server hosting provider and regions.
7. PC storefront (Steam?).
8. Final title?
