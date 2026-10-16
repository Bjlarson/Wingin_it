# Weapons

Status: Draft
Last updated: 2026-10-16

## Summary

Four weapon types. Machine guns are always available. Rockets, missiles, and bombs are limited-ammo tools with distinct roles.

## Weapon Table

There are no weapon unlocks; each class has a fixed loadout from spawn (see [aircraft-classes.md](aircraft-classes.md)).

| Weapon | Ammo | Damage | Role | Classes |
|---|---|---|---|---|
| Machine guns | Unlimited (overheat proposed) | Low, sustained | Primary dogfighting | All |
| Rockets | Limited | High burst, unguided | Head-on passes, ground targets | Heavy, Support |
| Homing missiles | Limited | High | Lock-on finisher | Fighter, Interceptor |
| Bombs | Limited | Very high, area | Ground objectives, carriers | Heavy |

## Part Targeting

All weapons damage the **specific part they hit** (see [health-and-damage.md](health-and-damage.md)). Aim for the **body** to kill and the **wings** to cripple. Rockets and missiles also splash a little damage onto the body.

## Machine Guns

- Unlimited ammo
- **Decided:** an **overheat meter** instead of reloads. Holding the trigger builds heat; at full heat the guns lock until they cool back down to 30%
- **Wing-mounted:** two guns per wing, angled in to meet on the nose line ~350 m ahead. Losing a wing silences its guns (half firepower)
- Bullets are real projectiles (fast, straight, no drop) rather than instant hits, so leading a target matters
- A **lead pipper** on every plane in range shows where to point the nose; a hit marker flashes when bullets connect
- Bullets are swept as small spheres, so near-misses on thin wings and tails still count

First-pass tuning (one shared asset for all classes in `ScriptableObjects/Weapons/MachineGuns.asset`):

| Value | First pass |
|---|---|
| Damage per bullet | 2 |
| Fire rate | 6 rounds/s per gun (24/s with both wings) |
| Bullet speed | 550 m/s + the plane's own speed |
| Range | ~825 m (1.5 s lifetime) |
| Spread | 0.35° |
| Overheat | 4 s of continuous fire; cools at 40%/s after a 0.3 s pause |

With every bullet on target that's ~48 damage/s: about 1 s to shoot off a Fighter wing and 2 s to kill its body.

## Rockets

- Unguided, fast, a strong burst
- Fire in salvos: one press launches a salvo, holding keeps firing one salvo per cooldown
- **Wing-mounted:** one rail under each wing. A lost wing loses its rail; the whole salvo then comes off the other wing
- Fly straight (no drop), parallel to the nose, so they hit ~4 m either side of the nose line. A separate **orange diamond** pipper shows rocket lead
- Each rocket damages the part it hits directly, then explodes and splashes **body** damage onto every plane within the blast radius (falling off with distance). The shooter is never hurt by their own blast
- No proximity fuse: a near miss flies on and self-destructs at max range. Hitting the ground still explodes (for ground targets later)
- Limited ammo, restocked on respawn (and on repair, Phase 3)
- Great against slow Heavies and ground targets

First-pass tuning (one shared asset in `ScriptableObjects/Weapons/Rockets.asset`, linked by the Heavy and Support):

| Value | First pass |
|---|---|
| Ammo | 8 |
| Salvo | 2 rockets, 0.1 s apart; 1 s between salvos |
| Direct hit | 30 to the part hit |
| Splash | 10 to the body at the center, falling to 3 at the 10 m edge |
| Speed | 320 m/s + the plane's own speed |
| Range | ~800 m (2.5 s lifetime) |
| Spread | 0.6° |

A full salvo on a Fighter's wings takes each wing from 45 to ~15 HP and the body from 100 to ~80. Two salvos remove both wings.

## Homing Missiles

- **Lock-on required.** Keep a plane within 10° of the nose (and within 900 m) for 1.5 s. A bracket closes in on it as the lock builds and turns red with **LOCK** when it's ready. Once locking, the target can drift out to 18° before the lock breaks. Terrain, buildings or thick cloud (40 m or more) between the planes break it too, so hills, towers and clouds are cover. A missile in flight also loses a target that hides in cloud (see [island-map](../05-Levels/island-map.md#clouds-built-2026-10-16))
- **One missile per press** of the missile button, only with a full lock. Pressing without a lock does nothing
- Launched from a **pylon under the fuselage**, so a lost wing doesn't cost you missiles
- The missile leaves at the plane's speed, accelerates to 240 m/s, and steers toward where the target will be (lead pursuit) at a limited turn rate. It explodes on contact, when it passes within 6 m of its target (proximity fuse), or after 6 s
- Damage works like rockets: the part hit takes direct damage, then the blast splashes the body of every plane nearby. A proximity burst is splash only. The shooter is never hurt by their own blast
- **"Easy to avoid if spotted":**
  - The target gets a **LOCK WARNING** while someone is locking it, **LOCKED ON** once they can fire, and a flashing **MISSILE — BREAK HARD** warning with the distance and an arrow pointing at the missile while one is chasing it.
  - **Decided: hard turns shake a missile.** Once the missile is within 400 m, keeping the flight path turning at 40°/s or more for 0.6 s breaks its lock for good. An **EVADING** bar fills while you do it. Turn too early (missile farther than 400 m) and nothing counts; turn too late and it hits before the bar fills. Every class can turn hard enough, but the Heavy only barely.
  - Getting the target out of the missile's 50° seeker cone also loses it (e.g. after it overshoots).
  - A **Barrel Roll** (Fighter ability) shakes off every missile chasing you and resets every lock on you to zero (see [aircraft-classes](aircraft-classes.md#abilities-in-the-prototype)).
  - **Proposed:** flares as a universal counter on a cooldown
- Limited ammo, restocked on respawn, at airfields, or from supply drops

Why an explicit hard-turn rule instead of pure physics: a missile closing from behind sees a turning plane at only a few degrees off its nose, so geometry alone almost never makes it miss. The rule keeps the dodge readable and learnable: spot it, wait until it's close, turn hard.

First-pass tuning (one shared asset in `ScriptableObjects/Weapons/Missiles.asset`, linked by the Fighter and Interceptor):

| Value | First pass |
|---|---|
| Ammo | 2 |
| Lock | 10° cone to start, 18° to hold, 900 m range, 1.5 s |
| Launch cooldown | 1 s |
| Speed | Plane's speed → 240 m/s (150 m/s² motor) |
| Turn rate | 32°/s; 50° seeker cone |
| Arming | 0.3 s after launch (no steering or proximity fuse before) |
| Lifetime | 6 s |
| Proximity fuse | 6 m |
| Direct hit | 40 to the part hit |
| Splash | 35 to the body at the center, falling to ~10 at the 12 m edge |
| Evade | ≥ 40°/s path turn for 0.6 s, missile within 400 m |

A direct hit on a Fighter's body takes it from 100 to ~25 HP (the edge of Critical); a proximity burst a few meters away does ~25–30. Two missiles finish almost anything but a Heavy.

**Prototype tools:** press **K** to make the nearest missile-carrying plane fire a missile at you, to practice dodging. Of the target drones the Fighter and the Interceptor carry missiles, so they're the ones that can lock you (you'll see the warning); they never fire on their own.

## Bombs

- Released from a **bay under the fuselage** (so a lost wing doesn't cost you bombs). One press drops one bomb; holding drops one every 0.5 s
- A bomb **keeps the plane's speed** and falls under gravity, so it lands well ahead of where you drop it. Diving shortens the throw; flying low and fast lengthens it
- **Impact preview:** a circle the size of the blast is drawn flat on whatever the bomb would hit right now, with the seconds until impact. It turns **red** when a ground target is inside it. The preview runs the exact trajectory and hit test the bomb uses, so what you see is where it lands
- Explodes on contact with anything (ground, buildings, planes). There's no air burst: a bomb that falls for 20 s without hitting anything is removed
- Big bonus damage vs ground objectives (convoys, radar stations, carriers): the blast deals 150 at the center to every ground target in range
- Little use against planes: a direct hit does 40 to the part hit and the blast only 15 to the body. One bomb never kills a healthy plane. The bomber is never hurt by their own blast
- Limited ammo, restocked on respawn (and on repair, Phase 3)

First-pass tuning (one shared asset in `ScriptableObjects/Weapons/Bombs.asset`, linked by the Heavy only):

| Value | First pass |
|---|---|
| Ammo | 4 |
| Drop interval | 0.5 s |
| Gravity | 12 m/s² (above real gravity so bombs land quickly enough to aim) |
| Release | Plane's velocity + 3 m/s straight down |
| Blast radius | 18 m (planes and ground targets) |
| Vs ground targets | 150 at the center, falling to 45 at the edge |
| Vs planes | 40 direct to the part hit; 15 splash to the body, falling to ~5 |

From 300 m at Fighter cruise a bomb falls for ~7 s and lands ~600 m ahead.

**Prototype ground targets:** the FlightTest scene has a bombing range ahead of the spawn (a four-truck convoy on a road, 100 HP each, and a radar station, 300 HP). A bomb within a few meters kills a truck; the radar takes two or three. Wrecks smoke and rebuild after 20 s. They stand in for the real [objectives](objectives.md) in Phase 8. Rockets don't damage ground targets yet.

## Prototype Target Drones

Until real bots arrive (Phase 5), the FlightTest scene has four enemy target drones (team 1), one per class, so every weapon has something to hit. They fly with the same autopilot and controller as the player, so lost parts and body damage change how they fly too, and they respawn at their start point 5 s after dying.

| Drone | Pattern | What it's good for |
|---|---|---|
| Heavy | Slow circle, 300 m radius | Big, easy target; rockets and bombs vs a Heavy |
| Support | Wide circle, 450 m radius | Long tracking shots |
| Interceptor | Weave: the circle swings ±120 m in and ±70 m up/down | Hard target: keeping the pipper on |
| Fighter | Racetrack: 1.6 km straight legs east–west, turning at each end | Lining up a part shot from behind |

Dev keys:

- **T**: the next drone (taking turns) appears 250 m ahead of you, fully repaired, flying your heading level for 20 s. The easy way to practice shooting a wing off. A tow run never evades.
- **Y**: drones **break when hit** (off at start). When another plane hits it, a drone banks hard away across the attacker's line of fire with boost for 2.5 s, then can't break again for 4 s.

## Ammo Restock

- Full restock when repairing at an airfield
- Partial restock from supply drops

## Open Questions

- [ ] Confirm the per-class loadouts above (implemented as proposed; needs a playtest). Should any class get its own ammo counts (e.g. more rockets for the Heavy)? Each class can link its own weapon asset.
- [ ] Which button fires rockets vs missiles vs bombs? Rockets are on the **secondary** button for now (R / middle mouse, gamepad X/Square, touch RKT button). **Proposed:** bombs get their own button (B, gamepad Y/Triangle, touch BOMB button), since the Heavy carries both rockets and bombs. With loadouts in, the Heavy is the only class with two limited weapons (rockets + bombs), so it's the one that needs the separate bomb button. A class only ever has one of rockets/missiles, so those could share one button later.
- [ ] Flares: yes or no? Hard turns may already be enough of a counter.
- [ ] Is 0.6 s of hard turning the right dodge window? Hands-on test against a K-launched missile.
- [ ] Friendly fire in Teams mode?
