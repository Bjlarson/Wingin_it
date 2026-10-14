# Open Questions

Status: Living document
Last updated: 2026-10-13

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

## Still Open

### Blocking for Prototype

1. **Netcode library** (NGO / Fish-Networking / Fusion) → [multiplayer](../08-Technical/multiplayer.md)
2. **Part list.** Is one "tail" part enough? Does the engine break off or just die? → [health-and-damage](../02-Mechanics/health-and-damage.md)

### Design

3. Wing-mounted guns (losing a wing halves firepower)?
4. Class switching: on respawn only, or also at the airfield?
5. Weapon button mapping for rockets / bombs; confirm per-class loadouts.
6. Yaw control and mouse-aim mode on PC?
7. Support class role in Free For All.
8. Prize machine: spin price, duplicate rule, rarity tiers.

### Business / Scope

9. **Loot box compliance:** selling coins for random items needs odds disclosure and region handling → [economy](../01-Game-Design/economy-and-monetization.md#-loot-box-regulation)
10. Server hosting provider and regions.
11. PC storefront (Steam?).
12. Final title?
