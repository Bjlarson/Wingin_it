# Controls

Status: Draft
Last updated: 2026-10-14

## Summary

Keyboard + mouse is the baseline. Gamepad and mobile (touch) are required. All three should map to the same set of actions.

## Actions

| Action | Type |
|---|---|
| Throttle up / down | Axis |
| Pitch | Axis |
| Roll | Axis |
| Yaw (proposed) | Axis |
| Fire machine guns | Hold |
| Fire missiles | Press |
| Fire rockets / bombs (secondary) | Press (TBD) |
| Boost | Hold |
| Special ability | Press |
| Look around / free camera | Hold |

## Keyboard + Mouse

| Key | Action |
|---|---|
| W | Throttle up |
| S | Throttle down |
| A / D | Roll |
| Mouse Y or ↑/↓ | Pitch |
| Q / E | Yaw (proposed) |
| Space | Machine guns |
| Right Mouse | Missiles |
| Left Mouse | TBD (guns too? rockets?) |
| Shift | Boost |
| F (proposed) | Special ability |
| C (proposed) | Free look |

**Consider:** a mouse-aim mode (like War Thunder): the plane steers toward the mouse cursor and Left Mouse fires guns. It's much easier for new players and is common in the genre. It could ship as an option next to the direct-control scheme above.

## Gamepad (proposed)

| Input | Action |
|---|---|
| Left stick | Pitch / Roll |
| Right stick | Camera look |
| RT | Machine guns |
| LT | Missiles |
| RB | Boost |
| LB | Yaw modifier or secondary weapon |
| A / Cross | Special ability |
| Bumpers or D-pad | Throttle |

## Mobile (proposed)

- **Left side:** virtual joystick for pitch/roll, or tilt steering as an option
- **Right side:** large Fire button, Missile button, Boost button, and an Ability button
- **Throttle:** auto-managed by default, with an optional slider
- **Aim assist:** stronger on mobile, with a gentle magnetism toward targets

## Landing Gear (all platforms)

**Decided:** there is no landing gear button. The gear deploys automatically when the plane gets close to the ground, and it takes time to extend. Touching down before it's fully out is a crash. See [repair-and-landing.md](repair-and-landing.md#landing-gear).

## Open Questions

- [x] Mouse-aim mode as the default on PC? **Yes** (2026-10-14). M toggles direct mode; keys override mouse-aim per axis. Implemented bindings: `Assets/_Project/Settings/FlightControls.inputactions`. Gamepad yaw is on D-pad left/right and throttle on D-pad up/down; touch auto-throttles to cruise
- [x] Cross-play: yes. Mobile aim assist must be balanced so touch players can compete without being stronger than mouse aim. Consider letting PC players opt into an input-based queue later if it becomes a problem.
