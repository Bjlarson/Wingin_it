# Health & Damage

Status: Draft
Last updated: 2026-10-14

## Summary

Planes use **per-part damage**, like GTA aircraft. Each plane is made of parts that take damage separately. When a part's health hits zero it **breaks off**, and the plane keeps flying with that handicap until the pilot lands to repair or crashes. The **body (fuselage)** is the core: if it takes too many hits, the plane explodes.

**Health never regenerates in the air.** Lost parts only come back by landing and repairing.

## Goals

- Damage is readable from a distance: smoke, missing wings, and planes flying lopsided.
- Losing a part feels dramatic and changes how you fly, which pushes the retreat decision.
- A skilled pilot can limp home on one wing for a heroic landing.
- Aiming matters: going for the body kills, going for a wing cripples.

## Plane Parts

| Part | Can break off? | When destroyed |
|---|---|---|
| **Body (fuselage)** | No | **Plane explodes** |
| Left wing | Yes | Hard to fly, **loses its guns** (see below) |
| Right wing | Yes | Hard to fly, **loses its guns** (see below) |
| Horizontal stabilizer (elevator) | Yes | Unstable pitch |
| Vertical stabilizer (fin + rudder) | Yes | Unstable yaw |
| Engine | **No: dies and smokes** | No thrust: glide only |

**Decided (2026-10-14):** six parts. The tail is split into horizontal and vertical stabilizers. The engine never detaches: at 0 HP it dies, the prop stops, and it trails smoke. Its hitbox stays, so a dead engine still soaks hits that would otherwise reach the body. Machine guns are **wing-mounted**, half per wing.

Each part has its own HP pool, set per class. First-pass values (tune once guns exist; they live in `ScriptableObjects/Durability/`):

| Class | Body | Each wing | H stab | V stab | Engine |
|---|---|---|---|---|---|
| Fighter | 100 | 45 | 30 | 30 | 50 |
| Interceptor | 75 | 34 | 22 | 22 | 38 |
| Heavy Fighter | 160 | 70 | 45 | 45 | 80 |
| Support | 110 | 45 | 30 | 30 | 55 |

Hitboxes match each part's shape, but thin parts (wings, stabilizers) are padded to at least 0.6 m thick so they can be hit edge-on.

### Hit Rules

- Bullets and direct hits damage **the part they hit**.
- **Once a part breaks off, its hitbox is removed.** Shots pass through that space and can't damage it again, so the remaining parts are harder to hit.
- Rockets and missiles damage the part they hit plus a **small splash to the body**. Rockets do this now: the splash hits the body of every plane in the blast radius, falling off with distance (see [weapons.md](weapons.md#rockets)).
- Collisions damage the parts that make contact (wing clips a building = wing damage).

## Effects of Losing Parts

| Lost part | Flight effect | Still flyable? |
|---|---|---|
| One wing | Constant roll toward the missing side. ~40% less lift. Auto-stabilization weakened. Must constantly counter-steer. **Half firepower** | Yes, but **way harder** |
| Horizontal stabilizer | Nose porpoises up and down. Pitch authority much lower. Pitch auto-leveling off | Yes, hard |
| Vertical stabilizer | Nose drifts and fishtails side to side. Yaw authority much lower. Turns get sloppy | Yes, manageable |
| Both stabilizers | Pitch and yaw wobble together. No auto-stabilization | Yes, hard |
| Engine (dead) | No thrust, no boost. Glide only, losing altitude. Smoke trail | Glide to an airfield for a dead-stick landing |
| Wing + any stabilizer | Near-uncontrollable spiral | Barely, until the crash |
| Both wings | Falls | No, it's going down |

**Implemented (2026-10-14), first pass.** `PartFlightEffects` (tuned on the plane's `PlaneHealth`) rebuilds the modifiers from a lost-part bitmask whenever a part is destroyed or restored. Multipliers compound when several parts are gone:

| Lost part | Modifiers |
|---|---|
| One wing | Roll bias toward the missing side = 35% of the class roll rate. Lift ×0.6 (steady sink of up to 10 m/s, plus a mild nose drop). Roll rate ×0.75. Roll auto-level ×0.4 |
| Horizontal stabilizer | Pitch rate ×0.45. Pitch auto-level off. ±40°/s pitch wobble |
| Vertical stabilizer | Yaw rate ×0.35. Roll auto-level ×0.7. Grip ×0.6 (slides through turns). ±30°/s yaw wobble |
| Both stabilizers | Roll auto-level off as well |
| Wing + any stabilizer | Roll bias ×1.5 |
| Both wings | Lift 0: noses down and sinks at 25 m/s |
| Engine | Thrust 0: no boost, and speed bleeds at 3 m/s² in level flight, so a shallow dive holds speed |

Losing a part never kills you directly. The **body** reaching 0 HP, **a crash**, or **a collision** does. Like GTA, a crippled plane keeps flying until the pilot gets it home or hits something.

**Decided:** guns are mounted on the wings, so losing a wing halves your firepower. Losing both wings means no guns, but you're going down anyway.

## Body Damage States

The body's HP drives the overall condition, smoke, and performance:

| State | Body HP | Effects |
|---|---|---|
| Healthy | 100–60% | Normal |
| Damaged | 59–25% | Light smoke trail, top speed −15% (proposed) |
| Critical | 24–1% | Heavy black smoke and fire, engine sputters, guns may jam, climb −30% (proposed), alarm + red vignette |
| Destroyed | 0% | **Explodes** |

Part damage stacks on top of these. A Critical body with a missing wing is in deep trouble.

**Implemented (2026-10-14), first pass.** `BodyDamageEffects` (tuned on the plane's `PlaneHealth`) maps body HP to a state and rebuilds its modifiers on every state change, so they stack with lost-part effects and a server only needs to sync body HP:

- **Damaged:** top speed ×0.85, light grey smoke trail.
- **Critical:** top speed ×0.85, climb ×0.7, the engine coughs (random thrust dips of up to 50%), heavy black smoke + fire, guns jam at 0.3 chance per second of firing for 1.2 s (`GunStats`). The debug HUD flashes "CRITICAL DAMAGE — RETURN TO BASE" and pulses a red vignette (placeholder for the Phase 6 vignette; no alarm sound yet).

## Destruction

- Explosive cartoon crash, with parts scattering as big chunky pieces
- The killer gets team score + coins for the match (see [economy-and-monetization.md](../01-Game-Design/economy-and-monetization.md))
- **Kill credit for crashes:** if a crippled plane crashes, the last player to damage it gets the kill
- Custom destruction cosmetics (confetti, "POOF") play here, but must still read clearly as a kill
- Respawn after a delay (TBD, proposed 5s)

**Implemented (2026-10-14), first pass.** Every death goes through `PlaneController.Crash` with a `DeathCause`:

- **Shot down** (body at 0 HP): a mid-air explosion (white-hot core → orange fireball → dark smoke cloud that hangs ~2.5 s). Every remaining part becomes wreckage that keeps ~70% of the plane's velocity and falls trailing smoke; the body and engine also trail fire. Wreckage lives 12 s, long enough to reach the ground.
- **Collision** (terrain, buildings): the old break-apart. Pieces bounce off the impact point with a small flash.
- **Respawn:** 5 s after either kind of death (`PlaneRespawner`), at the spawn point, fully repaired. The camera holds on the death spot and eases back to 3× its chase distance. The debug HUD shows "SHOT DOWN" or "CRASHED" with a countdown.
- A wreck can't take damage, so a plane can only die once per life.

**Kill credit implemented (2026-10-14), first pass.** `PlaneHealth` remembers the last damage source that wasn't the plane itself (dev tools and self-damage never count):

- **Shot down:** whoever fired the fatal hit gets the kill.
- **Crash:** the last attacker gets the kill if they hit within **20 s (proposed)**, or at any time if the plane is **crippled** (a lost part or a Critical body), because they're the reason it can't fly properly.
- Repair or respawn clears the attacker, so a fixed plane starts clean.
- Every death raises `PlaneHealth.AnyKilled` with a `KillInfo` (victim, killer plane or none, source, cause). Scoring, coins, and the real kill feed will listen to it. A non-plane source (future AA guns) is recorded as the source with no killer plane.
- The debug HUD shows "SHOT DOWN BY X" or "CRASHED (X gets the kill)" and a placeholder kill feed in the top-left.

## Damage State for Repair Timing

The repair time is set by the **worst** of body HP and lost parts (proposed):

| Repair tier | Condition | Repair time |
|---|---|---|
| Light | Body 60–99% and no parts lost | 5 s |
| Heavy | Body 25–59%, **or** a stabilizer lost, **or** engine dead | 10 s |
| Critical | Body 1–24%, **or** a wing lost | 15 s |

See [repair-and-landing.md](repair-and-landing.md).

## Interactions

- **Support Repair Aura** heals body HP and part HP, but **cannot reattach lost parts or restart a dead engine** (in the prototype). Only landing does that. Healing can lift the body out of Critical/Damaged.
- **Heavy Armor Shield** halves damage to every part while it's up (in the prototype).
- **Landing a crippled plane** is possible if it's within the touchdown thresholds. Landing with one wing should be hard but doable.
- **Landing gear** always deploys, even with parts missing (keep it simple).

## Open Questions

- [x] Final part list → six parts: body, L/R wing, horizontal stabilizer, vertical stabilizer, engine
- [x] Does the engine break off? → No, it dies and smokes; hitbox stays
- [x] Wing-mounted guns? → Yes, half the guns per wing
- [x] Base HP per part per class? → first pass in the table above; retune with real gun damage
- [ ] Do gun jams feel fun or just frustrating? Playtest.
- [ ] Respawn delay length?
