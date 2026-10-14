# Core Loop

Status: Draft
Last updated: 2026-10-13

## Summary

Winging It has two loops. The **primary loop** is objective-based dogfighting where planes repair at airfields; there are no in-match plane upgrades. The **secondary loop** is a stripped-down "last plane standing" ramming mode. Across matches, players level up and earn cosmetics.

## Primary Loop

```
 Pick class → Spawn at team airfield
        │
        ▼
 Fly out ──► Fight enemies / complete objectives ──► Team score + player XP
        ▲                    │
        │                    ▼
        │             Take damage
        │                    │
        │        ┌───────────┴───────────┐
        │        ▼                       ▼
        │   Stay and fight          Retreat to airfield
        │   (risk death)            Land → Repair (+ ammo)
        │        │                       │
        │        ▼                       │
        │   Destroyed → Respawn ─────────┤
        └────────────────────────────────┘
```

### Steps

1. **Pick a class** (Fighter / Interceptor / Heavy / Support) and spawn at the team airfield.
2. **Fly out** and look for enemies or objectives.
3. **Fight**: shoot enemies, destroy convoys, grab supply drops, capture points.
4. **Earn** team score and personal XP.
5. **Take damage.** The plane's performance gets visibly worse (see [health-and-damage.md](../02-Mechanics/health-and-damage.md)).
6. **Decide** whether to stay and risk dying or retreat and repair.
7. **Repair** by landing at a friendly airfield (see [repair-and-landing.md](../02-Mechanics/repair-and-landing.md)).
8. **Rejoin** the battle.

### Moment-to-Moment Loop (seconds)

Spot target → Close distance → Get behind → Fire → Evade → Repeat.

### Session Loop (~60–90 seconds)

**Fight → Damage → Retreat → Repair → Fight again.** That's 3–4 cycles per ~5-minute match (proposed target).

### Match Loop (~5 minutes)

Pick class → fight / repair cycles → match ends → scoreboard + XP summary → level-up and cosmetic unlocks → requeue.

### Meta Loop (across matches)

Earn XP → **player level** up → unlock **cosmetics** → show them off in the next match. Nothing that affects power carries over. See [progression.md](progression.md).

## Secondary Loop — Ram Mode

Players crash into each other; the last plane standing wins. There are no repairs. It's a short, chaotic party mode. See [match-modes.md](match-modes.md#ram-royale-secondary-loop).

## Tension Sources

| Tension | Created by |
|---|---|
| Greed vs safety | No air regen, so every extra kill while damaged is a risk |
| Hunter vs hunted | Damaged planes are visibly smoking and become priority targets |
| Offense vs defense | Teams must protect their airfields and approach lanes |
| Kills vs objectives | Objectives give score without needing to win duels |
