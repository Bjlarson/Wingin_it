# Repair & Landing

Status: Draft
Last updated: 2026-10-13

## Summary

This is the signature mechanic. Planes do not regenerate health in the air. To repair, a pilot has to fly to a friendly airfield, lower the gear, land safely, and stop in the repair zone. The time and risk of retreating creates the central decision of the game: **stay and risk dying, or retreat and repair?**

## Goals

- Retreating is a meaningful, tense choice.
- Retreats create chases, ambushes, and airfield defense.
- Landing is easy enough that it never feels like a chore, but a sloppy landing costs time.

## Repair Flow

1. Reach a friendly airfield (or a carrier in Carrier War).
2. Descend. The landing gear deploys automatically near the ground.
3. Wait for the gear to fully extend, then land safely: touch down below a max speed and angle.
4. Stop in the repair zone (a marked area on the runway or apron).
5. Hold in place while the repair runs.
6. **Proposed:** switch class here if wanted (see [aircraft-classes.md](aircraft-classes.md)).
7. Take off and rejoin.

## Repair Times (decided: fixed)

| Repair tier | Condition at touchdown | Repair time |
|---|---|---|
| Light | Body 60–99% and no parts lost | 5 sec |
| Heavy | Body 25–59%, or tail/engine lost | 10 sec |
| Critical | Body 1–24%, or a wing lost | 15 sec |

The tier is the **worst** of body HP and lost parts (see [health-and-damage.md](health-and-damage.md#damage-state-for-repair-timing)). A repair always restores **all parts, 100% HP, and full ammo**. Lost wings, tails, and engines are reattached.

**Proposed:** leaving the repair zone before the timer finishes cancels the repair (no partial repair). This keeps fixed times meaningful: commit or don't.

## Invulnerability While Repairing (decided)

A plane is **invulnerable while its repair timer is running** in the repair zone.

- Invulnerability starts when the plane stops in the zone and the repair begins.
- It **ends the moment the repair finishes or the plane leaves the zone**. You can't hide there indefinitely.
- Visual: a bubble shield or wrench-icon shimmer, so attackers know not to waste ammo.
- The danger is now the **approach and takeoff**, not the parked plane. That's where ambushes happen, and AA guns help cover it.

## Landing Gear

**Decided:** fully automatic on all platforms. There is no gear button.

- **Deploy trigger** (proposed): the plane is below a height above ground (e.g. ~30 m) **and** descending.
- **Deploy time** (proposed): ~1.5 s from start to fully locked.
- **Touching down before the gear is fully out = crash.** Players have to slow their approach and give the gear time, which turns landing into a small skill check without needing an extra input.
- **Retract** happens automatically after takeoff, once the plane climbs above the trigger height.
- **Feedback:** a gear icon on the HUD (red = up, yellow = deploying, green = locked), a mechanical clunk sound, and visible wheels on the model.
- **Side effect (proposed):** deployed gear adds drag, which slightly lowers top speed. Skimming low for cover then costs something.

Edge case: gear will pop out when flying low over terrain or the city, not only at airfields. That's fine; it's readable and adds flavor, and the drag makes low flying a tradeoff.

## Landing Rules (proposed)

| Check | Pass | Fail |
|---|---|---|
| Gear fully deployed | Lands | **Crash** |
| Touchdown speed | Below threshold | Bounces / damage |
| Touchdown angle | Within ±15° pitch/roll | Crash |
| Stopped in zone | Repair starts | Nothing |

Generous thresholds, a landing-guide UI, and auto-braking on the runway keep this approachable.

## Airfield Safety

**Decided:** home airfields have **AA guns** that shoot enemies within a radius, so players can't camp repair zones.

- AA guns are strong enough to punish lingering, but a fast diving pass can still get through. They're a deterrent, not a wall.
- AA can't be destroyed at home airfields (proposed).
- Captured or neutral airfields have **no AA** (proposed), which makes them closer but riskier.
- Tuning: range, damage per second, and lock-on delay are all TBD.

## Interactions

- **Support plane** Repair Aura heals in the air, but can't reattach lost parts. It's the one exception to "no air regen" and makes Support valuable.
- **Crippled landings:** landing with a missing wing or a dead engine (gliding in) is hard but allowed. It's one of the game's big hero moments.
- **Carrier War:** the carrier is the repair zone. Landing on a moving deck is harder.
- **Airfield capture** adds forward repair points for the team.

## Edge Cases

- Repairing plane gets shot: no effect, it's invulnerable (decided).
- Two planes in the same repair zone. Is there a queue or can both repair at once?
- Player lands at full HP. Restocks ammo only (proposed: 5 sec).
- Enemy lands at your airfield. Is that blocked or does it just not repair?

## Open Questions

- [x] Fixed time buckets (5 / 10 / 15 s)
- [x] Airfield protection: AA guns at home airfields
- [x] Shot while repairing: invulnerable during the repair timer
- [ ] Can two planes repair in the same zone at once?
