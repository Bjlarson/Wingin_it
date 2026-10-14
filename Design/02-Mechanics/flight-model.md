# Flight Model

Status: Draft
Last updated: 2026-10-13

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

| Value | Fighter | Interceptor | Heavy | Support |
|---|---|---|---|---|
| Max speed | TBD | Highest | Low | TBD |
| Pitch rate (°/s) | TBD | TBD | Low | TBD |
| Roll rate (°/s) | TBD | TBD | Low | TBD |
| Yaw rate (°/s) | TBD | TBD | TBD | TBD |
| Boost multiplier | TBD | TBD | TBD | TBD |

Damage states reduce these values; see [health-and-damage.md](health-and-damage.md).

## Part Damage Effects on Flight

Planes take **per-part damage**, and losing a part changes the handling until the plane is repaired or crashes (GTA style). The controller has to support asymmetric modifiers:

| Modifier | Driven by |
|---|---|
| Roll bias (constant torque toward one side) | Lost wing |
| Lift multiplier | Lost wing(s) |
| Pitch/yaw authority + random wobble | Lost tail |
| Auto-stabilization strength | Lost wing / tail weakens or disables it |
| Thrust | Lost engine = 0 (glide) |
| Top speed / climb | Body damage state |

Implement these as a **modifier stack** on the base class stats so effects combine cleanly and can be synced from the server as a compact part-state bitmask.

Tuning target: one wing missing should be **way harder** but survivable for a focused player. Constant counter-steering on one side is the feel to aim for.

## Edge Cases

- What happens at the map boundary? (Proposed: soft turn-back plus a warning, not an invisible wall.)
- Is there a ceiling altitude?
- Flying upside down: should auto-level fight the player?
- Takeoff: does a plane spawn already in the air or on the runway?

## Open Questions

- [ ] Is there a yaw (rudder) control? The original control list has roll + pitch only.
- [ ] Is terrain collision instant death or heavy damage?
- [ ] Spawn on the runway (taxi and take off) or air-spawn above the airfield? (Proposed: air-spawn for speed, with runway takeoff as an option after a repair.)
