# Repair & Landing

Status: Draft
Last updated: 2026-10-15

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
4. Stop in the repair zone (a marked area on the runway or apron). **Greybox:** a 30 × 200 m yellow box on the far half of a 40 × 600 m runway, so a plane lands, rolls out, and stops in it without taxiing.
5. Hold in place while the repair runs.
6. **Proposed:** switch class here if wanted (see [aircraft-classes.md](aircraft-classes.md)).
7. Take off and rejoin.

## Repair Times (decided: fixed)

| Repair tier | Condition at touchdown | Repair time |
|---|---|---|
| Light | Body 60–99% and no parts lost | 5 sec |
| Heavy | Body 25–59%, or a stabilizer lost, or engine dead | 10 sec |
| Critical | Body 1–24%, or a wing lost | 15 sec |

The tier is the **worst** of body HP and lost parts (see [health-and-damage.md](health-and-damage.md#damage-state-for-repair-timing)). A repair always restores **all parts, 100% HP, and full ammo**. Lost wings and stabilizers are reattached, and a dead engine is restarted.

**Proposed:** leaving the repair zone before the timer finishes cancels the repair (no partial repair). This keeps fixed times meaningful: commit or don't.

**Implemented (first pass, `RepairRules` + `PlaneRepair`):**

- **Starts** when the plane is on its wheels, slower than **0.5 m/s**, and inside the repair zone of an airfield friendly to its team. The auto-brake parks it on the zone's cross, so a normal landing starts the repair by itself. Enemy and neutral zones do nothing.
- **Tier** is the worst of the body damage state (Healthy / Damaged / Critical, the same 60% / 25% thresholds) and the lost-parts mask. It's locked when the repair **starts**, so damage taken during the rollout counts. A plane at full HP still gets a Light (5 s) stop that restocks ammo.
- **Finishes** after the tier's time: `PlaneHealth.RestoreAll` reattaches lost parts, refills all HP, restarts the engine, clears kill credit, and rockets / missiles / bombs restock.
- **Cancels** if the plane leaves the zone or takes off first. Nothing is repaired. Moving around inside the zone is fine.
- After a repair, the plane has to leave the zone (or take off) before it can repair again.
- **Debug HUD:** "REPAIRING (HEAVY)  7.3 s" with a progress bar, then a green REPAIRED or yellow REPAIR CANCELLED flash.
- Each plane repairs on its own, so two planes in one zone currently both repair at once (still an open question below).

## Invulnerability While Repairing (decided)

A plane is **invulnerable while its repair timer is running** in the repair zone.

- Invulnerability starts when the plane stops in the zone and the repair begins.
- It **ends the moment the repair finishes or the plane leaves the zone**. You can't hide there indefinitely.
- Visual: a bubble shield or wrench-icon shimmer, so attackers know not to waste ammo.
- The danger is now the **approach and takeoff**, not the parked plane. That's where ambushes happen, and AA guns help cover it.

**Implemented (first pass):** `PlaneRepair` sets `PlaneHealth.IsInvulnerable` for exactly as long as `IsRepairing` (start, finish, cancel, crash and respawn all go through one setter). An invulnerable plane takes no damage from any source (guns, rockets, missiles, bombs, splash), can't lose parts, and a blocked hit records no attacker, so it can't set up kill credit. It stacks cleanly with the Armor Shield (separate flag, not the damage multiplier). Visual: a gold, collider-less bubble around the plane that shimmers faster as the repair nears its end (placeholder until the Phase 11 VFX pass). Debug HUD: "SHIELDED: INVULNERABLE UNTIL REPAIRED" under the repair bar. Attackers get no hit marker on a shielded plane; a distinct "blocked" marker is a possible later tweak.

## Landing Gear

**Decided:** fully automatic on all platforms. There is no gear button.

- **Deploy trigger** (proposed): the plane is below a height above ground (e.g. ~30 m) **and** descending.
- **Deploy time** (proposed): ~1.5 s from start to fully locked.
- **Touching down before the gear is fully out = crash.** Players have to slow their approach and give the gear time, which turns landing into a small skill check without needing an extra input.
- **Retract** happens automatically after takeoff, once the plane climbs above the trigger height.
- **Feedback:** a gear icon on the HUD (red = up, yellow = deploying, green = locked), a mechanical clunk sound, and visible wheels on the model.
- **Side effect (proposed):** deployed gear adds drag, which slightly lowers top speed. Skimming low for cover then costs something.

**Implemented (first pass, `LandingGear`):**

| Value | Current | Notes |
|---|---|---|
| Deploy trigger | below **30 m** above ground **and** sinking faster than 0.5 m/s | Height is a straight-down probe from the plane that ignores planes and debris |
| Retract trigger | above **35 m** | 5 m above the deploy height so the gear doesn't flicker. Between 30 and 35 m the gear keeps doing what it was doing |
| Deploy / retract time | **1.5 s** / 1.5 s | Descending again mid-retract reverses from where it is |
| Drag | top speed **×0.9** while the gear is out at all | A stat modifier, so it stacks with damage |
| Respawn | gear snaps up (air spawn) or locked (runway spawn) | Each level picks its spawn mode; see [flight-model](flight-model.md#spawning) |

Once the gear starts down it keeps coming down while the plane stays under 35 m, even in level flight, so a low approach that levels off still locks. Placeholder model: tricycle gear on the fuselage (never detaches, so one-winged landings keep all wheels). The mains fold inward, the nose wheel folds back. Debug HUD: GEAR light next to the throttle bar (red up, flashing yellow deploying, yellow retracting, green locked). No clunk sound yet (no audio system).

Edge case: gear will pop out when flying low over terrain or the city, not only at airfields. That's fine; it's readable and adds flavor, and the drag makes low flying a tradeoff.

## Landing Rules

| Check | Pass | Fail |
|---|---|---|
| Gear fully deployed | Lands | **Crash** |
| Touchdown speed | Below threshold | Bounces / damage |
| Touchdown angle | Within ±15° pitch/roll | Crash |
| Stopped in zone | Repair starts | Nothing |

Generous thresholds, a landing-guide UI, and auto-braking on the runway (see [Auto-Braking](#auto-braking)) keep this approachable.

**Implemented (first pass, `LandingRules` + `PlaneController`):** a touchdown is the locked wheels reaching any **flat** surface (normal within ~25° of up), not only runways. The checks run in this order and the first failure decides:

| Check | Value | Fail |
|---|---|---|
| Gear locked | Up or still deploying | **Crash** ("GEAR NOT DOWN") |
| Pitch and roll | within **±15°** | **Crash** ("BAD ANGLE") |
| Sink rate | over **15 m/s** | **Crash** ("DESCENDING TOO FAST") |
| Sink rate | over **6 m/s** | **Bounce** ("SINKING TOO FAST") |
| Forward speed | over **1.6 × class min speed** (Fighter 64, Interceptor 80, Heavy 56, Support 61 m/s) | **Bounce** ("TOO FAST") |

- **Bounce:** −10% body max HP (self-inflicted, so no kill credit), the nose kicks up 5° and the plane hops, then settles for another try. A bounce that would kill is a crash instead. Repeated too-fast touchdowns keep bouncing until the plane slows.
- **Water:** the sea is solid but never landable. Wheels or airframe touching it while nearly level is a crash reported as "DITCHED IN THE SEA"; the auto-brake, runway spawns and ground rolling all ignore it.
- **Airframe strikes:** a wing or tail can hit the ground before the wheels (banked past ~21°). Hitting flat ground with the airframe while within ±30° of level reports as a failed landing (GEAR NOT DOWN or BAD ANGLE); a steeper dive is just a crash.
- **On the wheels:** the plane rolls along the surface. Throttle sets the target roll speed (~4 m/s² of rolling drag when above it), yaw or roll input steers (30°/s), and pitch-up at flying speed (the class stall speed) raises the nose; at 4° it lifts off. Only pitch-up input lifts off: a nose-high touchdown (a flare, or a one-winged plane holding its nose up) stays on the wheels (fixed 2026-10-16; before, any touchdown above 4° at flying speed hopped straight back into the air). Rolling off an edge falls back into flight. Below crash speed (8 m/s), bumping into a hangar just stops the plane instead of passing through it. At idle the auto-brake takes over (below).
- **Auto-throttle (touch):** idles while the gear is locked, so touch players slow down on approach without touching the throttle. Proposed; it also slows them when skimming low with the gear out.
- **Feedback (debug HUD):** a green "LANDED" or yellow "BOUNCED: reason" flash, V/S readout under the gear light while it's out, GROUND when rolling; a touchdown crash says why ("CRASHED: GEAR NOT DOWN").
- **Dev key L:** puts you 560 m before the aim point on your home runway's glide path (3°, aiming 60 m past the threshold), gear locked, at idle (class min speed). Hands off in mouse-aim, it lands by itself. Damage parts after (5-0) to practice crippled landings.
- **Landing assist:** glide-path rings, an aim bar, a repair zone outline, an ILS-style box and green/yellow speed and sink readouts while on approach to a friendly runway. See [hud.md](../06-UI-UX/hud.md#landing-assist-proposed).

## Auto-Braking

**Implemented (first pass, `AutoBrake` + `PlaneController`):** landing should end in the repair zone without fiddly throttle work.

- **Touchdown pulls the throttle to idle**, unless the pilot is holding throttle-up or boost (a touch-and-go).
- **At idle on the wheels, the auto-brake drives.** On one of our runways with the repair zone ahead, it stops the plane on the zone's **center cross**: it keeps rolling at touchdown speed (or taxis up to **15 m/s** if slower, engine permitting) until a steady **8 m/s²** braking curve ends on the cross, then brakes along it. Landed long and can't make the curve? It brakes up to **14 m/s²** to catch up, and if it's already past the cross but inside the zone it stops as hard as it can.
- **No stop point** (off-runway field landing, an enemy or neutral runway, or the zone is behind): it just brakes to a stop at 8 m/s².
- **Parked:** the brake holds the plane at 0 m/s until the pilot throttles up or boosts, which releases it instantly. Back to idle re-engages it (and taxis into the zone if it's ahead).
- Steering still works while braking.
- **Feedback (debug HUD):** BRAKE / PARKED under the gear light instead of GROUND, and "zone stop N m" on the home airfield line while rolling in.

| Value | Current |
|---|---|
| Braking curve | 8 m/s² |
| Max braking | 14 m/s² |
| Taxi speed / acceleration | 15 m/s / 4 m/s² |
| Stop point | Center of our repair zone (440 m from the threshold on the greybox airfield) |

A by-the-book Fighter approach (dev key **L**) touches down near the threshold at ~40 m/s and is parked on the cross about **13 s** later: ~7.5 s rolling, ~5 s braking. A long, floaty landing that touches down mid-zone still stops inside it. **Tuning lever:** most of that time is the roll to the zone, so moving the zone toward the threshold (or shortening the runway) is the cheapest way to cut it. Wait for a hands-on feel test before deciding.

## Airfield Safety

**Decided:** home airfields have **AA guns** that shoot enemies within a radius, so players can't camp repair zones.

- AA guns are strong enough to punish lingering, but a fast diving pass can still get through. They're a deterrent, not a wall.
- AA can't be destroyed at home airfields (proposed).
- Captured or neutral airfields have **no AA** (proposed), which makes them closer but riskier.
- Shells pass through friendly planes, so home AA never hurts its own team (proposed).

**Built (first pass, all values proposed):** four sandbagged twin-barrel turrets (`AAGun`), one off each corner of the runway, 50–90 m clear of it. Each picks the nearest enemy in range and line of sight (terrain, buildings and thick cloud block it, planes don't), swings toward its lead point, and fires straight-flying tracer shells that damage whichever part they hit, like plane guns. Three things make it a deterrent instead of a wall:

1. **Lock-on delay:** 1 s of tracking a new target before the first shot.
2. **Traverse limit:** the turret swings at most 45°/s. A camper circling 400 m out at cruise crosses at ~10°/s and gets tracked; an Interceptor at top speed passing 50 m from a gun crosses at ~150°/s and outruns it.
3. **Spread tightens while it lingers:** 4° on the first shot, down to 0.9° after 4 s on the same target.

| Value | Current |
|---|---|
| Range | 800 m per turret |
| Traverse rate | 45°/s |
| Lock-on delay | 1 s |
| Fire cone (must be this close to the lead point to fire) | 3° |
| Fire rate / damage | 4 shells/s per turret, 2 HP each |
| Shell speed / lifetime | 650 m/s, 1.4 s |
| Spread | 4° → 0.9° over 4 s |

Measured in the test scene: a Fighter flying straight out of the field at 300 m and cruise speed loses ~22% body plus a few wing/tail hits before it's out of range. An Interceptor circling 350 m out at 300 m loses ~1.2 body HP/s and is nearly a wing down after 30 s. The turrets lead a straight-line target, so a turning plane is harder for them to hit. A first tune, 6 shells/s at 2.5 HP with 0.6° spread, killed a straight-flying Fighter in ~6 s: a wall, not a deterrent.

- Kills: an AA kill is credited to the gun as the source with **no killer plane** (kill feed: "AA Gun 2 shot down X"; death screen: "SHOT DOWN BY AA").
- Feedback: "AA FIRE — GET OUT FAST OR LOW" warning when a turret is shooting at you; "Home AA engaging X" under the home airfield line on the debug HUD.
- Dev key **J** makes every AA gun, your own included, treat you as an enemy, to feel what attacking an airfield is like. **T** tows a drone in front of you: do it over the field to watch your AA work. The test drones were moved out of AA range.
- Turrets are on the airfield prefab and only armed while it's a home airfield (`Airfield.IsHome`), so a neutral or captured field's guns stay idle.

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
- [ ] AA tuning: does a lingering camper die fast enough, and can a fast pass really get through? (needs a hands-on test with J)
- [ ] Should AA shells hit friendly planes? (Currently no; waits on the friendly-fire decision.)
- [ ] Should touch auto-throttle only idle near a friendly runway, rather than whenever the gear is locked?
- [ ] Off-airfield landings are allowed on any flat ground (no repair there). Keep, or crash outside runways?
- [ ] Repair zone position: auto-brake rollout is ~13 s from a normal touchdown. Move the zone nearer the threshold?
