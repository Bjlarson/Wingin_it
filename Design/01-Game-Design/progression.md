# Progression

Status: Draft
Last updated: 2026-10-13

## Summary

**Players level up, not planes (decided).** Every plane of a class is equally powerful for everyone, so matches come down to skill. Players earn XP from matches, which raises a persistent **player level** that unlocks **cosmetics**. Cosmetics are how players show progress and set themselves apart.

> This replaces the earlier in-match plane-tier model (Trainer → Mk II → Elite). Earlier we said "nothing carries over between matches." That still holds for **power**: no stats, weapons, or abilities carry over. Cosmetics and player level are the only things that persist.

## Goals

- Skill decides fights, not time played or levels.
- A new player in their first match has the same plane as a veteran.
- Players always have something to show off and something to work toward.
- Cosmetics are visible to others in the air: paint, trails, explosions.

## Non-Goals

- In-match plane upgrades or stat tiers
- Persistent stat boosts, weapon unlocks, or pay-to-win items
- A .io-style "start weak, grow big" curve

## In-Match

- Pick any class at spawn. **Proposed:** you can switch class on respawn or while repairing at a home airfield.
- Every class has its full loadout and ability from spawn (see [aircraft-classes.md](../02-Mechanics/aircraft-classes.md)).
- Kills, assists, and objectives earn **team score** (to win the match) and **personal XP** (for player level). Team score values: kill 50, assist 20, objectives set per objective (see [match-modes](match-modes.md#team-score-built-2026-10-17)); a pilot's own share is their **personal score**.
- **Death penalty:** the respawn timer, the flight time back to the fight, and score for the enemy. Nothing else. Retreating to repair is still the smart move, because dying wastes a big chunk of a 5-minute match.

## Player XP Sources

| Source | XP | Notes |
|---|---|---|
| Kill | TBD | |
| Assist | TBD | |
| Destroy AI convoy | TBD | |
| Collect supply drop | TBD | |
| Capture radar station / airfield | TBD | |
| Successful repair landing | TBD (small) | Teaches the core mechanic |
| Heal teammates (Support) | TBD | So Support players level too |
| Match win | TBD bonus | |
| Match completion | TBD | So losing still progresses |

## Player Level

- Persistent across matches and platforms (cross-play, so the account should follow the player)
- Shown next to the player's name in the scoreboard and kill feed
- **Proposed:** each level-up awards bonus coins (or a free prize-machine spin). A level curve is TBD.

## Earning Cosmetics (decided)

Players earn **coins** at the end of each match (more for a better score) and spend them on a **random cosmetic prize machine**, Crossy Road style. Cosmetics can also be bought directly. See [economy-and-monetization.md](economy-and-monetization.md).

## Cosmetics

| Category | Examples | Visible to others? |
|---|---|---|
| Plane skins / paint jobs | Camo, chrome, candy stripes, shark mouth | Yes |
| Decals / nose art | Emblems, numbers, cartoon mascots | Yes |
| Smoke trail color | Rainbow, team-tinted, sparkle | Yes |
| Destruction effect | Confetti explosion, cartoon "POOF" | Yes |
| Pilot avatar / helmet | Shown in kill feed and scoreboard | Yes |
| Titles / badges | "Ace", "Mechanic", "Untouchable" | Yes |
| Kill banner / calling card | Shown to the victim | Yes |

**Readability rule:** cosmetics must never hide team color, class silhouette, or damage smoke. Team color stays on a fixed area (e.g. wingtips + tail), and damage smoke overrides custom trails. See [art-direction.md](../03-Art/art-direction.md).

## Challenges (idea, post-prototype)

Cosmetics can also come from challenges ("Land with under 5% HP", "Shoot down a plane while it's landing", "10 Barrel Roll missile dodges"). These reward the signature mechanics and give players goals beyond leveling.

## Technical Implications

Persistent player level and cosmetics need **player accounts and cloud save** (e.g. Unity Authentication + Cloud Save). See [multiplayer.md](../08-Technical/multiplayer.md).

## Open Questions

- [ ] Is there a level cap, or prestige/seasons?
- [x] Unlock style: coins → random prize machine (+ direct-buy shop)
- [x] Monetization: free to play, cosmetic + coin sales, rewarded ads
- [ ] Does player level do anything beyond display + bonus coins?
- [ ] Class switching: on respawn only, or also at the airfield?
