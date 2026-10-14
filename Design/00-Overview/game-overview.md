# Winging It — Game Overview

Status: Draft
Last updated: 2026-10-13

**Working title:** Winging It
**Alternatives:** Ace Skies, Dogfight Derby, Sky Havoc

## Pitch

A multiplayer, cartoon-style **3D** aerial combat game. Players fly around a large island, fight other pilots, and capture objectives. When a plane is badly damaged, the player has to land at a friendly airfield to repair it before getting back into the fight.

## At a Glance

| | |
|---|---|
| Perspective | Full 3D, third-person chase camera |
| Players | Up to 16 per match (8v8 in Teams) |
| Match length | ~5 minutes |
| Platforms | PC and mobile from day one, with cross-play |
| Progression | Players level up (not planes); match coins buy random cosmetics |
| Business model | Free to play: cosmetic sales, coin sales, rewarded ads |
| Networking | Dedicated servers |
| Damage | Per-part (GTA style): wings, tail, and engine can break off; a body hit kills |
| First mode | Teams |
| Team | Solo developer, part-time |

## Core Loop (one line)

**Fight → Damage → Retreat → Repair → Fight Again**

See [core-loop.md](../01-Game-Design/core-loop.md).

## Design Pillars

1. **Easy to fly, fun to master.** Flight feels arcadey, like GTA Online jets or Ace Combat, not like a simulator. A new player should be dogfighting within 30 seconds.
2. **Retreat is a real choice.** Health does not regenerate in the air. Players repair by landing at an airfield, which means deciding whether to keep fighting or fly back to safety.
3. **Skill wins, style shows.** Every plane of a class is equal; nobody out-levels you. Players progress by leveling themselves up and earning cosmetics that make them stand out in the sky.
4. **Loud, readable cartoon chaos.** Bright colors, big explosions, and thick smoke trails. You can tell an enemy's class and condition at a glance.

## What Makes It Different

Most dogfight games reward staying in combat forever. Here, the landing-and-repair mechanic creates a cycle of **Dogfight → Damage → Escape → Return**, which generates:

- Chases (finishing off a fleeing, smoking plane)
- Ambushes on airfield approach paths
- Airfield defense as a team activity
- Heroic escapes at 5% HP, or limping home on one wing
- Visible, dramatic damage: planes losing wings mid-fight, flying lopsided, gliding in with a dead engine
- Strategic decisions every few minutes

## Reference Points

| Aspect | Like | Not like |
|---|---|---|
| Flight feel | GTA Online jets, Ace Combat, Battlefield aircraft | Microsoft Flight Simulator, DCS |
| Progression | Cosmetic leveling (Fortnite, Rocket League) | Stat grinds, pay-to-win, in-match power tiers |
| Combat | War Thunder dogfights, simplified | Realistic ballistics |
| Visuals | Fortnite, Pilotwings, Sky Force Reloaded, Roblox aviation games | Realistic military |

## Target Platforms

PC (keyboard + mouse, gamepad) and mobile (touch), both from day one, **with cross-play**.

## Scope Guardrails (solo, part-time)

- One map, one mode (Teams) for the first playable
- Use asset packs for planes and environment during prototyping; replace them later
- Prefer Unity-provided services over custom backend work
- Bots to fill matches, so testing doesn't need 16 real people

## Document Map

- Game design: [core-loop](../01-Game-Design/core-loop.md), [progression](../01-Game-Design/progression.md), [economy-and-monetization](../01-Game-Design/economy-and-monetization.md), [match-modes](../01-Game-Design/match-modes.md)
- Mechanics: [flight-model](../02-Mechanics/flight-model.md), [controls](../02-Mechanics/controls.md), [health-and-damage](../02-Mechanics/health-and-damage.md), [repair-and-landing](../02-Mechanics/repair-and-landing.md), [weapons](../02-Mechanics/weapons.md), [aircraft-classes](../02-Mechanics/aircraft-classes.md), [objectives](../02-Mechanics/objectives.md)
- Art: [art-direction](../03-Art/art-direction.md)
- Levels: [island-map](../05-Levels/island-map.md)
- UI/UX: [hud](../06-UI-UX/hud.md)
- Technical: [multiplayer](../08-Technical/multiplayer.md)
