# Match Modes

Status: Draft
Last updated: 2026-10-17

## Summary

Four core modes use the primary loop (fight + repair). Every mode awards player XP. One party mode uses the secondary "ram" loop.

## Free For All

Every pilot for themselves.

- **Win condition:** TBD (highest score at time limit? First to X kills?)
- **Airfields:** neutral; anyone can repair. Need to define what stops spawn-camping at repair zones.

## Teams (Blue vs Red) — FIRST PLAYABLE

Team deathmatch with objectives. **This mode gets built first (decided).**

- **Size:** up to 8v8, with bots filling empty slots (proposed)
- **Length:** ~5 minutes
- **Win condition:** the highest team score when the timer ends. Kills and objectives both give score. **A tie is a draw (decided 2026-10-17; revisit after playtests).**
- **Airfields:** each team has a home airfield. Neutral airfields can be captured.

### Match Flow (offline prototype, built 2026-10-17)

`MatchManager` + `MatchScreens` (IMGUI placeholder screens) on the Island:

1. **Lobby:** both rosters (You + 7 bots for West, 8 bots for East, with the classes the bots will fly), a bot difficulty pick (Easy / Normal / Hard), and **Start** (Enter). The world is frozen behind it and no bots exist yet.
2. **Class select:** four cards (speed, turn rates, body HP, weapons, ability); click **Fly**, press 1–4, or Enter for the highlighted one. Esc goes back. The player spawns fresh over home in that class and the bots join.
3. **Countdown:** 3-2-1 in real time with everyone frozen in place.
4. **Play:** a 5:00 game-time clock with both team scores at the top center (it flashes in the last 30 s). Controls, the flight HUD and touch controls only run in this phase.
5. **End:** time's up freezes the world. VICTORY / DEFEAT / DRAW, the final score, and each team's pilots with kills, assists, deaths and score (sorted by score). **Play Again** (Enter) reloads the level to the lobby, keeping the difficulty and class.

### Team Score (built 2026-10-17)

Every point a pilot earns goes to their team's score and their own (personal score, later the basis for coins and XP). Highest team score at the whistle wins; a tie is a draw.

| Event | Points | Notes |
|---|---|---|
| Kill | **50** | An enemy plane brought down by a pilot, or by the team's airfield AA (team points only, no pilot credit). Crash kill credit as in [health-and-damage](../02-Mechanics/health-and-damage.md#destruction) |
| Assist | **20** | Per other enemy pilot who helped: shot off a part this life, **or** did ≥ 20% of the victim's body max HP in damage (any parts) with a hit in the last 20 s. An AA kill still pays assists |
| Objective | Set by each objective | `MatchManager.AwardObjective(team, points, label, pilot)`: for Phase 8 convoys, supply drops, radar and airfield captures |
| Crash nobody caused, team kill | 0 | The death still counts on the scoreboard |

Assists count toward team score (so focus fire pays a little more than a duel: a kill with two helpers is worth 90). **Revisit after playtests** along with the point values. Every award raises `MatchScore.Awarded` (team, pilot, points, kind, label) for the score popups and kill feed still to come.

## King of the Sky

Capture and hold airspace zones.

- Zones are 3D volumes in the sky (cylinders or spheres).
- Holding a zone ticks score for your team.
- Zones could rotate over time to move the fight around the map.

## Carrier War

Each team has an aircraft carrier at sea. Destroy the enemy carrier to win.

- Carriers are both the spawn point and the repair zone.
- Bombs and rockets do bonus damage to carriers.
- Landing on a moving carrier is harder, which raises the skill ceiling for repairs.

## Ram Royale (Secondary Loop)

*Working name.* Players crash into each other, and the last plane standing wins.

- No guns, or guns disabled
- No repairs
- A shrinking play area to force contact (proposed)
- Short rounds (~2–3 min), meant as a party or warm-up mode

## Mode Comparison

| Mode | Teams | Repair | Primary focus |
|---|---|---|---|
| Free For All | No | Neutral airfields | Kills |
| Teams | 2 | Home + captured | Kills + objectives |
| King of the Sky | 2 | Home | Zone control |
| Carrier War | 2 | Carrier | Destroy carrier |
| Ram Royale | No | No | Survival |

## Open Questions

- [x] Players per match: up to 16
- [x] Match length: ~5 minutes (Ram Royale shorter)
- [x] First mode: Teams
- [x] Overtime / tie-breaker rules: none for now; a tie is a draw (2026-10-17)
