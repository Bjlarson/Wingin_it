# Flight Model

Status: Draft
Last updated: 2026-10-15

## Summary

Arcade flight that feels like GTA Online jets, Ace Combat, or Battlefield aircraft. Planes are easy to turn, stabilize on their own, have mild momentum, and never stall in a frustrating way.

## Goals

- A new player can take off, turn, and shoot within 30 seconds.
- Turning feels responsive, with only mild momentum.
- The plane levels out by itself when the player lets go of the controls.
- Speed and agility differ clearly between classes.

## Non-Goals

- Realistic aerodynamics, stalls, spins, or G-force blackouts
- Fuel management
- Realistic takeoff and landing requirements beyond the repair flow

## Characteristics

| Trait | Behavior |
|---|---|
| Turning | Easy, high turn rate; pitch and roll respond quickly |
| Stability | Auto-levels roll and pitch back toward horizontal when there's no input |
| Stalls | No hard stall. At very low speed the plane gently noses down and loses lift. |
| Momentum | Mild. Speed bleeds slowly in climbs and builds in dives |
| Throttle | Persistent: W/S set a target speed rather than needing to be held |
| Boost | Short burst of speed on a meter that recharges |
| Ground | Crashing into terrain or buildings at speed is lethal (or heavy damage, TBD) |

## Proposed Implementation Approach

A **non-physical, velocity-driven controller** (not a full rigidbody aerodynamics sim):

- Rotation comes directly from input × class turn rates, with smoothing.
- Velocity points along the plane's forward vector at the current speed, with a small amount of lag for drift and momentum.
- Gravity has a light influence: climbing reduces speed and diving increases it.
- Auto-leveling slowly blends rotation back toward level flight when there's no input.
- This approach is also easier to network, because state is predictable from inputs.

## Tuning Values (per class)

First-pass values (2026-10-14), from `Assets/_Project/ScriptableObjects/FlightStats/`. The assets are the source of truth; tune them there.

| Value | Fighter | Interceptor | Heavy | Support |
|---|---|---|---|---|
| Max speed (m/s) | 110 | 140 | 85 | 100 |
| Pitch rate (°/s) | 85 | 70 | 55 | 78 |
| Roll rate (°/s) | 190 | 230 | 110 | 170 |
| Yaw rate (°/s) | 35 | 30 | 25 | 35 |
| Responsiveness (1/s) | 6 | 7 | 3.5 | 5.5 |
| Boost multiplier | 1.5 | 1.6 | 1.35 | 1.4 |

Damage states reduce these values; see [health-and-damage.md](health-and-damage.md).

## Part Damage Effects on Flight

Planes take **per-part damage**, and losing a part changes the handling until the plane is repaired or crashes (GTA style). The controller has to support asymmetric modifiers:

| Modifier | Driven by |
|---|---|
| Roll bias (constant torque toward one side) | Lost wing |
| Lift multiplier | Lost wing(s) |
| Pitch authority + pitch wobble | Lost horizontal stabilizer |
| Yaw authority + yaw wobble | Lost vertical stabilizer |
| Roll / pitch auto-stabilization strength | Lost wing / stabilizer weakens or disables it |
| Thrust | Lost engine = 0 (glide) |
| Top speed / climb | Body damage state |

Implement these as a **modifier stack** on the base class stats so effects combine cleanly and can be synced from the server as a compact part-state bitmask.

Implemented via `PartFlightEffects`; current values are in [health-and-damage.md](health-and-damage.md#effects-of-losing-parts). Lift lost to damage settles into a steady sink rather than the free fall of a stall, so a pilot can hold the nose up to fly against it. A dead engine uses a low glide drag instead of the normal deceleration.

Tuning target: one wing missing should be **way harder** but survivable for a focused player. Constant counter-steering on one side is the feel to aim for.

## Spawning

**Decided (2026-10-15):** each level picks its spawn mode with a `LevelSettings` object in the scene (no object = Air).

| Mode | Where you appear | Use it for |
|---|---|---|
| **Air** | 300 m up on the home airfield's approach, at cruise speed, gear up | Fast modes: straight back into the fight. The FlightTest scene uses this |
| **Runway** | Parked at the runway threshold, gear locked, engine idle, brake on | Slower or more immersive modes: throttle up, roll, pull up at flying speed |

- The mode covers respawns at an airfield and, in Runway levels, the player's first spawn. A plane with its own spawn point (the test drones) always air-spawns there.
- **Takeoff slots:** a runway spawn takes the first free one of 9 slots: rows of three side by side (center, 13 m left, 13 m right), each row 50 m further down the runway. A slot is taken when another live plane is within 10 m of it.
- A runway-spawned plane **stays parked** at idle. The auto-brake only taxis planes that have landed to the repair zone.
- Takeoff is the same as after a repair: throttle up (W) or boost, then pitch up once at stall speed. The gear retracts above 35 m.
- **Touch:** touch has no throttle control and auto-throttle idles while the gear is locked. Boosting on the wheels starts a *takeoff run*, which holds auto-throttle at cruise until the gear is up, so the plane doesn't fall back to idle on the climb-out. This also fixes the touch takeoff after a repair.
- Debug HUD: "READY FOR TAKEOFF" while parked after a runway spawn. Dev key **N** flips the level between Air and Runway and respawns you with the new mode.
- Open for later: a match-start group takeoff for Teams (Phase 6) can reuse the Runway mode and the slots.

## Edge Cases

- Map boundary: **soft turn-back** plus a warning. An autopilot takes over outside the radius (implemented).
- Ceiling altitude: **yes**, 1500 m in the test scene; above it the nose is pushed down.
- Flying upside down: auto-level rolls the plane upright only when roll input is released.
- Takeoff: **set per level** (see [Spawning](#spawning) below).

## Open Questions

- [x] Is there a yaw (rudder) control? **Yes**: Q/E (D-pad on gamepad); mouse-aim uses it automatically
- [x] Is terrain collision instant death or heavy damage? **Instant crash** for now (2026-10-14)
- [x] Spawn on the runway or air-spawn above the airfield? **Both, chosen per level** (2026-10-15), so different game modes can play differently. After a repair you always take off from the runway
