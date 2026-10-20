# Front-End Menus

Status: Built (offline vertical slice), 2026-10-17
Last updated: 2026-10-17

## Summary

The screens outside the cockpit: the main menu, class select, the in-match menu, settings and the post-match screen. Rule of thumb: **get the player into the air in as few presses as possible, and keep the world on screen behind every menu.** Menus are glass panels (see the [glass menu style guide](glass-menu-style-guide.png)) floating over the live island, never a blank screen.

## Goals

- **Three presses to fly** from launch: PLAY → confirm the game mode → confirm a class (Enter on the highlighted one each time).
- One look for every screen: the glass panel, the same row height, the same select / confirm hints.
- Every screen works with mouse, keyboard, gamepad and touch.
- Sensible per-platform defaults, so most players never open Settings.
- Nothing pauses an online match. Offline behaves the same way, so playtests now match the online game later.

## Non-Goals

- Accounts, garage / locker, prize machine, shop, coin breakdown: Phase 9–10. This doc only reserves their place in the main menu.
- Matchmaking UI: Phase 7. Reserved as the FINDING MATCH… state on the game mode card.
- Tutorial / onboarding: Phase 12 has its own doc.
- Key rebinding UI (see [Open Questions](#open-questions)).

## Current UI (audit, 2026-10-17)

| Element | What it does | How opened | Pauses? | Target |
|---|---|---|---|---|
| Lobby (`MatchScreens`) | Both rosters, bot difficulty, Start | Level load | Yes (world frozen) | **Merge**: bot difficulty moves to the game mode screen, rosters to class select and the in-match scoreboard |
| Class select (pre-match) | Four class cards | Start | Yes | **Keep**, plus the picked plane shown in the world behind it (later) |
| Respawn / airfield pickers | Class cards over the running match | Death / Tab when parked + repaired at home | No | **Keep** as is |
| 3-2-1 countdown | Frozen start | After class select | Yes | **Keep** (it's the match start, not a menu) |
| Score bar | Clock + team scores | Always in play | No | **Keep** (HUD) |
| End scoreboard | Result, pilots' K / A / D / score, Play Again | Time's up | Yes | **Keep**, add MAIN MENU |
| Esc | Only frees the mouse cursor | Esc | No | **Becomes** the in-match menu |
| F1 help, F2 touch toggle, dev keys | Dev overlay | Keys | No | **Move** to Settings → Developer (dev builds only); F-keys stay as shortcuts |
| Main menu, settings | Don't exist | — | — | **New** |

Before: 6 screens, no settings, no way to leave a match except quitting. After: 7 screens (main menu, game mode select, class select, in-match menu, settings, scoreboard, pickers), the lobby gone, nothing new that pauses.

## Screen Flow

```
Launch ─▶ MAIN MENU ──PLAY──▶ GAME MODE ─▶ CLASS SELECT ─▶ 3-2-1 ─▶ MATCH ─▶ SCOREBOARD ─┬─PLAY AGAIN─▶ CLASS SELECT
             │  (gear: SETTINGS)                                       │ Esc               └─MAIN MENU──▶ MAIN MENU
             │       ▲                                           IN-MATCH MENU ─LEAVE MATCH─▶ MAIN MENU
             └───────┴── SETTINGS also opens from the in-match menu
```
```

Back is always Esc / gamepad B / the panel's back arrow, and it never skips a level.

## Main Menu

**Where:** the Island scene itself, as a new first phase before the match (`MatchPhase.Menu`), not a separate scene, so PLAY needs no load. The backdrop is the player's plane **parked inside a hangar**, with the camera slowly circling it, so the first frame is already the game's look (and the plane is the player's own). It needs an interior: the airfield's existing hangar gets an open front, a floor, lights and a workshop prop or two, with the menu camera inside it. When PLAY is pressed, the camera eases back out through the doors (a short pull-back, under 2 s, skippable) toward the runway; the match itself starts as it does today. A separate front-end scene can wait until there's more than one map (or a server to connect to).

**Panel (left third of the screen, glass):** just one row.

| Row | Does | Notes |
|---|---|---|
| **PLAY** | Opens the game mode screen | Online (Phase 7): PLAY still opens game mode select; the chosen mode's card then shows FINDING MATCH… with a cancel |

Title "WINGING IT" above the panel; the build version in the bottom corner. No splash screens beyond the Unity one.

Everything else stays out of the way: a small **gear** icon bottom right opens Settings, and on PC a small **QUIT** text sits beside it (mobile has no quit). **HANGAR** (cosmetics, prize machine) joins this menu in Phase 9, when the garage screen exists; that's a later change, nothing here depends on it.

## Game Mode Select

PLAY opens a **horizontal scroll of game mode cards**, laid out like the class select (same card size, ←/→ or swipe to scroll, Enter / tap to confirm, Esc / B back to the main menu). Today players see **one card: TEAMS** (8v8, 5 minutes, bots fill both teams). The Phase 6.5 prototypes (Capture the Banner, Airfield Domination, Ram Royale) have cards too, but until their rules exist they show only in the Editor and development builds, tagged IN DEVELOPMENT in place of PLAY. Players never see a locked or "coming soon" card, so the row never looks empty or broken. With one card, it's centered and highlighted already, so confirming is one press.

Each card shows: the mode's name and one-line pitch, a simple picture (a top-down team-colored sketch), players per match, match length, and the mode's **settings row**. For Teams that is **BOTS: EASY / NORMAL / HARD** (←/→ on the highlighted card, or tap to cycle), remembered between sessions. Settings are per mode, so a future mode can have its own (or none).

A mode is a data entry (`GameModeInfo`: name, pitch, players, length, settings, the scene and `MatchManager` setup to start). Adding one means adding an entry and its card art, not changing the menu.

Online (Phase 7) this same screen is where the player picks what to queue for, and the bot difficulty row becomes the backfill setting or goes away.

## Class Select

The existing four cards (stats, loadout, ability, ON YOUR TEAM n), restyled to the game mode screen's card layout. Adds:

- The cards are centred on screen; no roster column (team lineups show on the in-match scoreboard).
- The highlighted class's plane replaces the one parked in the hangar, so the pick is seen in the world, not just on a card. **Built:** the cards sit in the lower half (shorter than the in-play picker's, no dark wash) and the camera eases in to 26 m and tilts down so the plane shows above them; BACK puts the class picked last back in the hangar.
- Esc / B goes back to the game mode screen.

The respawn and home-airfield pickers stay exactly as built ([aircraft-classes](../02-Mechanics/aircraft-classes.md#class-selection)).

## In-Match Menu

**Esc** (gamepad **Start**, touch: a small ☰ button top right, above the kill feed) opens it. **The match keeps running**: flight controls are released (the plane holds its current stick at neutral, like letting go) and the flight HUD stays visible behind the panel. This is honest about what online play will do, and it means opening it is a real risk: don't open it mid-dogfight.

| Row | Does |
|---|---|
| **RESUME** | Closes it (also Esc / Start / B) |
| **SETTINGS** | Opens Settings over the match |
| **LEAVE MATCH** | Confirm row ("Leave? Your team keeps playing with a bot") → main menu |

Right of the panel: the **live scoreboard** (both teams, K / A / D / score), the same layout as the end screen. This is the only place to see it mid-match, so there's no separate hold-to-show scoreboard key.

Mouse: the cursor is free while the menu is open; closing it relocks the cursor on the next click, as today.

## Settings

One panel, four tabs (Q / E or LB / RB switch tabs). Every change **applies immediately** and is saved; there is no Apply button. **RESET TAB** restores that tab's platform defaults. Settings are per device (`PlayerPrefs`), not cloud-saved: a phone and a PC want different values.

### Controls

| Setting | Default | Range / options | Platforms | Wires to |
|---|---|---|---|---|
| Control mode | Mouse aim | Mouse aim / Direct | PC | `PlayerFlightInput.controlMode` (M still toggles) |
| Mouse aim sensitivity | 1.0× | 0.25–3× | PC | `FlightCameraRig.mouseAimSensitivity` |
| Free look sensitivity | 1.0× | 0.25–3× | PC, gamepad | `freeLookMouseSensitivity`, `stickLookSpeed` |
| Invert pitch | Off | On / Off | Direct mode, gamepad, touch | `PlayerFlightInput.invertPitch` |
| Touch controls | Auto | Auto / On / Off | All (Auto = on for touch screens) | `TouchControlsUI` (F2 still toggles) |
| Auto throttle (touch) | On | On / Off | Touch | `PlayerFlightInput.autoThrottleOnTouch` |
| Touch button size | 100% | 75–130% | Touch | `TouchControlsUI` |
| Bindings | — | Read-only list of every action per device | All | `FlightControls.inputactions` |

### Graphics

| Setting | Default | Range / options | Platforms |
|---|---|---|---|
| Quality | Mobile: Mobile; PC: PC | Low / Mobile / PC (quality levels) | All |
| Resolution + window mode | Native, fullscreen | Resolution list; Fullscreen / Windowed / Borderless | PC |
| Frame rate cap | PC: 144; mobile: 60 | 30 / 60 / 120 / 144 / Unlimited | All (mobile offers 30 / 60, 120 if the screen supports it) |
| Render scale | PC: 100%; mobile: 80% | 50–100% | All |
| Field of view | 60° | 50–80° | All (`FlightCameraRig.baseFov`; the top-speed and boost widening keeps its offset) |
| Speed FOV effect | On | On / Off | All (Off = no widening with speed or boost; motion comfort) |

### Audio

| Setting | Default | Range |
|---|---|---|
| Master | 80% | 0–100% |
| Effects (guns, engines, explosions) | 100% | 0–100% |
| Alerts (lock warning, damage alarm, HUD tones) | 100% | 0–100%. Separate so a player can turn the world down without losing warnings |
| Music | 60% | 0–100% (no music yet) |
| Mute in background | On | On / Off (PC) |

### HUD & Accessibility

| Setting | Default | Range / options | Notes |
|---|---|---|---|
| HUD scale | 100% | 75–125% | Scales `FlightHUD`'s reference height. Touch HUD still never shrinks below half size |
| Damage screen effects | 100% | 0–100% | Smoke, vignette and hit flash strength (`DamageFeedback`). The alarm and RETURN TO BASE always stay |
| Reduce flashing | Off | On / Off | Steady instead of flashing: Critical diagram, clock, LOCK WARNING, RETURN TO BASE, heartbeat vignette |
| Team colors | Default | Default / High contrast (blue vs orange) | Colorblind-safe pair everywhere team color appears (HUD, minimap, kill feed, plane markers) |
| Minimap | North up | North up / Heading up | Heading up turns the whole-level photo about its center; N rides the rim |
| Developer (dev builds only) | — | F1 help, debug overlay, dev keys on / off | Not in release builds |

## Post-Match Screen

Today's scoreboard (VICTORY / DEFEAT / DRAW, final score, pilots' K / A / D / score), plus:

- Two buttons side by side along the bottom, under the scoreboard:
  - **PLAY AGAIN** (Enter / A): straight to class select, keeping difficulty and class.
  - **MAIN MENU** (Esc / B).
- Arrow keys or the D-pad move between them.
- Phase 9 adds the coin breakdown, the "watch ad to double coins" button and the prize machine shortcut from [hud](hud.md#post-match-screen).

## Input on Menus

| | Move | Confirm | Back | Tabs |
|---|---|---|---|---|
| Mouse | Hover | Click | Esc / back arrow | Click tab |
| Keyboard | ↑ ↓ (← → on inline options) | Enter | Esc | Q / E |
| Gamepad | D-pad / left stick | A | B (Start also closes the in-match menu) | LB / RB |
| Touch | — | Tap | Back arrow top left (Android back too) | Tap tab |

- The highlighted row has the selection rail (style guide #4); the hint strip at the bottom shows the glyphs for the **last device used** (keys, Xbox-style buttons, or nothing on touch).
- Touch rows are at least 56 px tall at 1080p reference (the style guide's row height), which also clears the 44 pt minimum on phones.
- Sliders: ← → steps 5%, gamepad hold repeats; touch drags.
- Opening a panel raises the edge glow to 14% for 180 ms (style guide #3). Panel changes cross-fade in 120 ms; nothing slides.

## As Built (2026-10-17)

Everything above is in, with these differences and leftovers:

- **Code:** `MatchManager` (phase `Menu` replaced `Lobby`; `OpenMenu` / `CloseMenu`, `PlayAgain`, `ReturnToMainMenu`, hangar parking), `MatchScreens` (every screen), `SettingsPanel`, `MenuNav` + `MenuMath` (focus, key repeat, last device, slider notches), `MenuCamera` (hangar orbit and pull-back), `GameModeInfo` (the mode catalog), `GameSettings` + `SettingsApplier` (in `Scripts/Settings`). Pure parts are EditMode-tested (`GameSettingsTests`, `MenuMathTests`); the flow is PlayMode-tested in `MatchFlowTests`.
- **Hangar:** the airfield prefab's hangar is now an interior (open doors facing the runway, door panels slid open on their tracks, painted floor, roof trusses, three warm work lamps, a workbench, tool chest, crates, oil drums, tyres, a yellow parking T) with a `Hangar Spot` the airfield exposes. The player's plane parks there in the class picked last. The menu camera drives the main camera directly (the Cinemachine brain is off in the menus) on unscaled time, since the world is frozen.
- **Pull-back path:** the camera swings round to the plane's nose first, then backs out through the doors, so it never cuts through the plane or the walls. It holds outside (38 m out, 5.5 m up) for game mode and class select.
- **Main menu focus:** PLAY, then the gear and QUIT can also be reached with ↓ (keyboard / gamepad have no other way to them).
- **Game mode card:** ← / → change the bots while there is one card; with more cards they scroll, and ↑ / ↓ moves to the BOTS row. The mouse highlights the card under it; a click or tap anywhere on a card picks it. Each mode's bot difficulty is its own, remembered between sessions (`PlayerPrefs`). The row of cards shrinks to fit a narrow screen.
- **Display changes:** a resolution / window mode change adds a KEEP row on top of the Graphics tab and reverts after 10 s; leaving the tab keeps it.
- **Settings in the Editor:** resolution, window mode and render scale only apply in builds (the URP asset is a project file); quality only changes once the player picks one. Quality lists the project's levels (Mobile, PC); there is no Low level yet.
- **Audio:** Master drives `AudioListener.volume`; Alerts scales the damage tones; Effects and Music are saved but have nothing to drive yet (no game audio / music).
- **Developer:** hides the dev panel, F1 help and damage keys (5–9); the landing assist and center messages are part of the real HUD and stay.
- **Panel change:** the new screen fades in over 120 ms (`GlassGUI.Opacity`); the old one cuts, since IMGUI can't keep drawing a screen that's gone. The dark backdrop, score bar and ☰ button don't fade.
- **Heading-up minimap:** the photo is redrawn turned each frame into a second render texture (`FlightHUD.TurnMap`), so it stays inside its rounded frame.
- **Not built yet:** interactive key rebinding (Bindings is a read-only list).

## Technical Notes

- **Stay on IMGUI + `GlassGUI` for the vertical slice.** It's what the HUD and match screens already use, so one look and no new UI stack. Add a small focus system to `GlassGUI` (row list + highlighted index) for keyboard / gamepad navigation, since IMGUI has none built in.
- Revisit UI Toolkit in Phase 11/12 if IMGUI costs too much on mobile or the Hangar needs scrolling lists and animation.
- A `GameSettings` class (pure, EditMode-tested: defaults per platform, clamping, save / load as one JSON string in `PlayerPrefs`) raises `Changed`; each system reads its value on start and on change. Nothing reads `PlayerPrefs` directly.
- `MatchPhase.Menu` comes before `ClassSelect`; `MatchManager.Dismiss()` (free flight for tests) still skips everything. FlightTest keeps starting straight into free flight.

## Numbers / Tuning

| Value | Default | Notes |
|---|---|---|
| Presses from launch to flying | 3 | PLAY, confirm the game mode, confirm the highlighted class |
| Menu orbit camera | ~12 m from the parked plane, inside the hangar, 1 lap / 60 s | Pull-back through the doors on PLAY: under 2 s |
| Row height | 56 px @ 1080p | Style guide |
| Panel cross-fade | 120 ms | Fade-in of the new panel (`MenuMath.PanelFadeTime`) |
| Slider step | 5% | |
| Leave-match confirm | Inline second press | No modal dialog |

## Edge Cases

- **Esc while a class picker is open:** closes the picker (today's behavior), not the menu. A second Esc opens the menu.
- **Esc during the 3-2-1:** opens the menu; the countdown keeps going.
- **Dying with the menu open:** the menu stays; closing it shows the respawn picker if the timer is still running.
- **Time runs out with the menu open:** the menu closes and the scoreboard shows.
- **Leaving a match:** offline it just returns to the main menu. Online (Phase 7) a bot of the same class takes the slot, so the team isn't a plane short.
- **Gamepad disconnected mid-match:** opens the in-match menu, like the platform guidelines expect.
- **App backgrounded (mobile):** opens the in-match menu and mutes; the match keeps running (offline included).
- **Resolution change** that leaves the screen blank: reverts after 10 s unless KEEP is pressed.
- **Touch controls set to Off on a phone:** allowed, but the ☰ menu button always shows on touch screens so the setting can be turned back on.

## Open Questions

- [ ] Should offline matches pause on Esc after all? Proposed: no, to match online. Revisit if solo playtesters find it annoying.
- [ ] Key rebinding: ship with fixed bindings + a read-only list (proposed for the slice) or full rebinding (Input System interactive rebinding) before launch? Accessibility says rebinding should exist by launch.
- [x] Hangar interior: **decided (2026-10-17)** modify the existing airfield hangar (open front, floor, lights) rather than building a separate set.
- [ ] With only Teams in the list, should the game mode screen be skipped until a second mode exists? (Proposed: keep it, so the flow doesn't change when modes are added.)
- [ ] Does the team-color high-contrast pair conflict with the cosmetics' team color zone (Phase 9)?

## References

- [Glass menu style guide](glass-menu-style-guide.png) · [HUD](hud.md) · [Controls](../02-Mechanics/controls.md) · [Match modes](../01-Game-Design/match-modes.md#match-flow-offline-prototype-built-2026-10-17) · [Aircraft classes](../02-Mechanics/aircraft-classes.md#class-selection)
