# Health & Damage

Status: Draft
Last updated: 2026-10-13

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
| Left wing | Yes | Hard to fly (see below) |
| Right wing | Yes | Hard to fly (see below) |
| Tail (stabilizers + rudder) | Yes | Unstable pitch and yaw |
| Engine | Yes (or stops, smoking) | No thrust: glide only |

Each part has its own HP pool. The per-class values are TBD; for example, a Heavy has a tougher body and wings than an Interceptor.

### Hit Rules

- Bullets and direct hits damage **the part they hit**.
- **Once a part breaks off, its hitbox is removed.** Shots pass through that space and can't damage it again, so the remaining parts are harder to hit.
- Rockets and missiles damage the part they hit plus a **small splash to the body** (proposed).
- Collisions damage the parts that make contact (wing clips a building = wing damage).

## Effects of Losing Parts (proposed)

| Lost part | Flight effect | Still flyable? |
|---|---|---|
| One wing | Constant roll toward the missing side. ~40% less lift. Auto-stabilization weakened. Must constantly counter-steer | Yes, but **way harder** |
| Tail | Nose wobbles and drifts. Pitch and yaw authority much lower. No auto-stabilization | Yes, hard |
| Engine | No thrust, no boost. Glide only, losing altitude | Glide to an airfield for a dead-stick landing |
| Wing + tail | Near-uncontrollable spiral | Barely, until the crash |
| Both wings | Falls | No, it's going down |

Losing a part never kills you directly. The **body** reaching 0 HP, **a crash**, or **a collision** does. Like GTA, a crippled plane keeps flying until the pilot gets it home or hits something.

**Idea:** guns are mounted on the wings, so losing a wing halves your firepower. That adds a second meaningful consequence. Decide during prototyping.

## Body Damage States

The body's HP drives the overall condition, smoke, and performance:

| State | Body HP | Effects |
|---|---|---|
| Healthy | 100–60% | Normal |
| Damaged | 59–25% | Light smoke trail, top speed −15% (proposed) |
| Critical | 24–1% | Heavy black smoke and fire, engine sputters, guns may jam, climb −30% (proposed), alarm + red vignette |
| Destroyed | 0% | **Explodes** |

Part damage stacks on top of these. A Critical body with a missing wing is in deep trouble.

## Destruction

- Explosive cartoon crash, with parts scattering as big chunky pieces
- The killer gets team score + coins for the match (see [economy-and-monetization.md](../01-Game-Design/economy-and-monetization.md))
- **Kill credit for crashes:** if a crippled plane crashes, the last player to damage it gets the kill
- Custom destruction cosmetics (confetti, "POOF") play here, but must still read clearly as a kill
- Respawn after a delay (TBD, proposed 5s)

## Damage State for Repair Timing

The repair time is set by the **worst** of body HP and lost parts (proposed):

| Repair tier | Condition | Repair time |
|---|---|---|
| Light | Body 60–99% and no parts lost | 5 s |
| Heavy | Body 25–59%, **or** tail/engine lost | 10 s |
| Critical | Body 1–24%, **or** a wing lost | 15 s |

See [repair-and-landing.md](repair-and-landing.md).

## Interactions

- **Support Repair Aura** heals body HP and part HP, but **cannot reattach lost parts** (proposed). Only landing does that.
- **Landing a crippled plane** is possible if it's within the touchdown thresholds. Landing with one wing should be hard but doable.
- **Landing gear** always deploys, even with parts missing (keep it simple).

## Open Questions

- [ ] Final part list: is one "tail" part enough, or split it into horizontal and vertical stabilizers?
- [ ] Does the engine break off, or just die and smoke?
- [ ] Wing-mounted guns (losing a wing halves firepower)?
- [ ] Base HP per part per class?
- [ ] Do gun jams feel fun or just frustrating? Playtest.
- [ ] Respawn delay length?
