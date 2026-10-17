# Final Asset List (replace all greybox/placeholder content)

Status: Draft
Last updated: 2026-10-17

## Summary

Everything needed to swap the current greybox (`Greybox_*` / `Placeholder_*` / `FX_*` materials, primitive-built planes and city, generated terrain textures, no audio, debug-style UI) for finished art. Grouped into asset packs so each can be bought, commissioned, or made as one unit. Style targets: [art-direction.md](art-direction.md), [lowpoly-asset-style-guide.html](lowpoly-asset-style-guide.html), [glass-menu-style-guide.html](../06-UI-UX/glass-menu-style-guide.html).

Counts are for the current scope (4 classes, Island map, Teams mode). Items marked *(later)* are for modes/features that are designed but not built (Carrier War, King of the Sky, Ram Royale, cosmetics shop).

Hard requirements that apply to every pack:
- Planes are **modular** (body, L/R wing, horizontal + vertical stabilizer, engine/prop), each with its own mesh, UV space, and material slot for paint/decals. See [art-direction.md](art-direction.md#modular-planes-required-by-part-damage).
- Silhouettes stay locked per class; team color zone (wingtips + tail) is a separate material/mask.
- Mobile + PC: low-poly budgets, atlased textures, URP-compatible shaders.

---

## Pack 1: Aircraft (hero assets)

| Asset | Count | Notes |
|---|---|---|
| Fighter model (modular, 6 parts) | 1 | Balanced, medium |
| Interceptor model | 1 | Sleek delta wing |
| Heavy Fighter model | 1 | Chunky twin-engine |
| Support model | 1 | Utility/radar look |
| Broken-edge caps per detachable part | ~4 sets | Jagged stumps with sparks/fire hookup; can be shared across classes |
| Damaged-part variants (dented body, bullet holes) | optional | Or handled by decals |
| Landing gear (animated deploy/retract) | 4 | One per class, with wheel meshes |
| Propeller / jet exhaust meshes + blur | 4 | Spinning prop, motion-blur disc or afterburner cone |
| Cockpit canopy glass | 4 | Needs glass shader with tint |
| Pilot figure (visible in canopy) | 1 | Optional, ties into helmet cosmetics |
| Armor Shield bubble mesh/shader | 1 | Repair invulnerability + Heavy ability |
| Plane materials: base paint, trim, glass, team-color mask | 1 set | Replaces `Placeholder_PlaneBody/Dark/Glass` |
| Base skin texture sets / UV templates | 4 | Atlas layout so cosmetics can be swapped |

## Pack 2: Weapons & Projectiles

| Asset | Count | Notes |
|---|---|---|
| Machine-gun barrel meshes (wing-mounted) | 1-2 variants | Shown on wings; lost with the wing |
| Rocket model | 1 | Plus pod/rail if visible |
| Homing missile model | 1 | Fins, exhaust nozzle |
| Bomb model | 1 | Dropped from Heavy |
| Tracer/bullet mesh or sprite | 1 | Replaces `FX_Tracer` |
| AA flak shell | 1 | Replaces `FX_FlakTracer` |
| Muzzle flash sprites/mesh | 2-3 | Gun, rocket, missile |
| Lock-on bracket, pipper, crosshair | (see Pack 9) | UI side |

## Pack 3: Island Terrain & Environment Base

| Asset | Count | Notes |
|---|---|---|
| Terrain texture sets (PBR or stylized hand-painted): grass, sand, rock, snow, paved | 5 (+normals) | Replaces `Terrain_*.png` and terrain layers |
| Detail/blend textures (dirt, cliff, beach foam edge) | 3-4 | |
| Ocean water shader + normal maps + foam | 1 | Replaces `Greybox_Water`; shallow-to-deep gradient, shoreline foam |
| Skybox / sky gradient + sun | 1-3 | Replaces `Greybox_Sky`; optional sunset/night variants |
| Cloud meshes/sprites (puffy, usable as cover) | 4-6 | Volumetric-look low-poly or billboard; must match cover gameplay volumes |
| Distant haze/fog settings, post-processing profile | 1 | Bloom, color grading, vignette (URP volume) |
| Reflection probe / lighting setup | 1 | |

## Pack 4: Nature & Terrain Dressing

| Asset | Count | Notes |
|---|---|---|
| Trees: deciduous, pine, palm/dry | 6-9 | LOD'd, wind-sway shader |
| Bushes, grass tufts, flowers | 4-6 | |
| Rocks / boulders / cliff pieces | 6-8 | Mountains + coast |
| Desert: dune details, cacti, dead bushes | 5-6 | |
| Mesa rock set (3 flat-topped mesas) | 3 | Unique hero meshes |
| Islet rock (Lighthouse Rock base) | 1 | |
| Snow caps / snowy variants of rocks and pines | 3-4 | |
| Beach props: driftwood, shells, shoreline rocks | 3-5 | |

## Pack 5: City & Architecture

| Asset | Count | Notes |
|---|---|---|
| Skyscraper set (modular floors, tops, bases) | 10-15 variations | Glass, concrete, brick; windows with emissive night option |
| Mid/low-rise buildings (edge of city) | 8-10 | ~40 m tall |
| Plazas: fountain, trees, benches, statues | 5-8 | |
| Roads, sidewalks, road markings, intersections | 1 kit | Streets are 30 m wide; tiling kit |
| Street props: lamps, signs, traffic lights, billboards | 8-10 | |
| Bridges (2): decks, towers, cables | 1-2 | Deck 34 m above water, 150 m between towers |
| Canal walls, quay, docks | 1 kit | |
| Rooftop props: AC units, antennas, helipads, water tanks | 6-8 | |
| Vehicles (ambient cars, boats under bridges) | 4-6 | Static or simple animated |

## Pack 6: Landmarks (unique hero models)

| Asset | Notes |
|---|---|
| **The Spire**: stepped glass needle tower, observation deck, blinking beacon | ~545 m tall |
| **Summit Radar**: lattice mast + radome, beacon | 150 m mast |
| **The Pyramid**: 8-step stepped pyramid with detailing | 280 m base, 210 m tall |
| **Lighthouse Rock**: red/white lighthouse + rocky islet, rotating lamp | ~92 m |
| Beacon light effect (shared) | Blinking, readable at distance |

## Pack 7: Airfield & Military Props

| Asset | Count | Notes |
|---|---|---|
| Runway: asphalt texture, centerline, numbers, threshold stripes, edge lights | 1 kit | Replaces `Greybox_Runway/RunwayMarking` |
| Repair zone pad: markings, tool props, glow ring | 1 | Replaces `Greybox_RepairMarking` |
| Hangars | 2-3 | Per team color |
| Control tower | 1-2 | |
| Fuel tanks, crates, barrels, tool carts, repair crane | 8-10 | |
| Windsock, beacon, approach lighting (PAPI/strobes) | 3-4 | |
| AA gun turret (animated: rotates, recoils, destroyed state) | 1 | Replaces primitive AA guns |
| Radar dish (animated) | 1 | |
| Ground crew / mechanic figures | 2 | Optional, adds life to repair |
| Approach lane gate pylons | 3 colors | Magenta/cyan/lime; can be material swaps |
| Boundary buoys (ring around map) | 1-2 | Replaces `Greybox_BuoyFloat/Mast` |
| Target dummies / drone planes | 1-2 | Replaces `Greybox_Target` |
| Rings for the flight-test range (optional) | 1 | `Greybox_Ring` |

## Pack 8: VFX

| Effect | Notes |
|---|---|
| Damage smoke: wisps → thick black → fire | 3 tiers; team-agnostic; overrides cosmetic trails |
| Engine trail / contrail | Per class; recolorable |
| Boost / afterburner flame | |
| Explosion (plane): comic puff, debris, flash, "BOOM" pop | Big, exaggerated |
| Explosion (rocket/bomb/missile splash) | Smaller variants + ground impact dust |
| Bullet impact sparks, hit flash, debris chip | On planes and terrain |
| Water splash / ditching | For crash into the sea |
| Ground crash dust/fire | |
| Part detachment sparks + tumble smoke | |
| Landing: tire smoke, runway sparks, dust | |
| Repair: sparkles, wrench icons, progress ring | Replaces `FX_Ember` repair-related uses |
| Shield bubble hit ripples | Replaces `FX_Shield` |
| Missile smoke trail + flare | |
| AA flak burst puffs | |
| Cloud-entry wash / vapor streaks | |
| Barrel-roll streak / speed lines | |
| Respawn / spawn-in shimmer | |
| Pickup/confetti (cosmetic explosion) *(later)* | |

## Pack 9: HUD & In-Game UI

| Asset | Notes |
|---|---|
| Crosshair, lead pipper, lock-on bracket (open/closing/locked) | Vector sprites |
| Plane damage diagram: 4 class silhouettes with 6 parts each | Top-down, colorable per HP |
| Speed/throttle gauge, boost bar, ability bar, ammo icons | Rockets / missiles / bombs / guns / overheat |
| Warning icons: incoming missile, AA fire, lock warning, evading | Directional arrow |
| Nearest-airfield marker + landing assist (glide slope, speed, gear) | |
| Minimap frame + icons (planes, airfields, objectives, radar diamonds) | |
| Cloud-cover indicator | |
| Match timer/score bar (team-colored) | |
| Score popups ("+50 Kill", "WING SHOT OFF!") | Font + style |
| Kill feed frames, pilot avatar frames, badges/titles | |
| Mobile touch controls: virtual stick, fire/missile/boost/ability buttons | Replaces prototype touch overlay |
| Ability icons (Barrel Roll, Afterburner, Armor Shield, Repair Aura) | 4 |
| Weapon icons | 5 |
| Team icons / flags (Blue, Red) | |
| Fonts: display, body, numeric | 2-3 families |

## Pack 10: Menus & Meta UI

Per [glass-menu-style-guide.html](../06-UI-UX/glass-menu-style-guide.html).

| Asset | Notes |
|---|---|
| Main menu background (scene or hero plane render) | |
| Glass panel / button / tab / toggle / slider / dropdown set | 9-slice sprites or shaders |
| Class select screen: 4 hero renders + stat bars | |
| Team select / match setup / lobby | |
| Post-match scoreboard + coin summary | |
| Settings: controls, graphics, audio | |
| Prize machine UI (Crossy Road style) + reveal animation *(later)* | |
| Shop UI, coin/currency icons *(later)* | |
| Loading screen art | |
| Pause menu | |
| Game logo ("Winging It") + app icon + splash | |

## Pack 11: Cosmetics *(later, but plan UVs now)*

| Asset | Notes |
|---|---|
| Plane skins/paint jobs (camo, chrome, candy stripes, shark mouth...) | Texture-only, per class UV |
| Decals / nose art / numbers | Atlas |
| Smoke trail variants (rainbow, sparkle, team-tint) | Particle materials |
| Destruction effects (confetti, "POOF") | |
| Pilot avatars / helmets | |
| Titles / badges ("Ace", "Mechanic", "Untouchable") | |
| Kill banners / calling cards | |

## Pack 12: Audio

Nothing exists yet (`Audio/` is empty).

| Group | Items |
|---|---|
| Aircraft | Engine loops per class (idle → full throttle pitch ramp), boost/afterburner, prop spin, wind rush, gear deploy/retract, stall warning, flyby doppler |
| Weapons | Machine gun fire + overheat/jam, rocket launch, missile launch + flight whoosh, bomb drop/whistle, AA flak fire |
| Impacts | Bullet hit (body/wing/ground), explosions (small/large/plane), part tearing off, crash into ground/sea, water splash |
| Damage | Smoke/fire crackle, engine sputter/dead, critical alarm |
| Warnings (cockpit) | Missile lock tone, incoming missile alarm, AA fire alarm, low altitude, boundary warning, ability ready |
| Landing/repair | Touchdown (good/bounce), tire screech, runway rumble, repair tick/wrench loop, repair complete jingle |
| Abilities | Barrel roll swoosh, shield on/off, repair aura hum, radar ping |
| UI | Button click/hover, menu open/close, score popup, kill confirm, coin/prize machine sounds, countdown beeps |
| Match | Match start/end stingers, victory/defeat jingles, last-30-seconds tension |
| Ambient | Sea waves, wind, city hum, desert wind, airfield chatter |
| Music | Main menu theme, in-match loops (calm + intense layers), results theme, victory/defeat |
| Voice (optional) | Announcer ("Enemy down!", "Wing lost!", "Incoming missile!"), radio chatter |

## Pack 13: Animation

| Asset | Notes |
|---|---|
| Landing gear deploy/retract (x4 classes) | |
| Control surface motion (ailerons, rudder, elevators) | If modeled separately |
| AA turret rotate/fire/destroyed | |
| Radar dish spin, lighthouse lamp rotate, windsock, beacons | |
| Part detachment tumble (or physics only) | |
| Ground crew repair loop | |
| Menu transitions / UI tweens | |
| Camera shake presets | Hits, explosions |

## Pack 14: Carrier War & Other Modes *(later)*

| Asset | Notes |
|---|---|
| Aircraft carrier (x2 team variants), deck markings, island superstructure, arrestor wires | Spawn + repair zone, destructible |
| Carrier AA guns, escort ships | |
| Ocean wake / bow spray VFX | |
| King of the Sky zone volumes (3D cylinder shader, capture rings, team tints) | |
| Ram Royale shrinking-boundary visual (storm wall / energy dome) | |

---

## Suggested Order

1. **Aircraft + Weapons + VFX + Audio**: biggest feel change; most reused.
2. **HUD**: so gameplay stays readable with final art.
3. **Terrain, Nature, Water, Sky**: sets the look of the Island.
4. **City, Landmarks, Airfield**: fills in the world.
5. **Menus, Cosmetics, Carrier**: later phases.

## Totals (rough)

~300-400 individual items: ~10 hero models, ~80 environment/prop models, ~25 VFX, ~100 UI sprites, ~120 audio clips.

## Placeholders Replaced

| Current | Replaced by |
|---|---|
| `Placeholder_PlaneBody/Dark/Glass.mat`, `Plane.prefab` | Pack 1 |
| `FX_*.mat` | Pack 8 |
| `Greybox_Water/Sky/Cloud/Terrain/Ground/Mountain/Sandstone` | Packs 3-4 |
| `Greybox_Concrete/ConcreteDark/SpireGlass` | Pack 5-6 |
| `Greybox_Runway/RunwayMarking/RepairMarking`, `Airfield.prefab` | Pack 7 |
| `Greybox_Lane1-3/LanePole/Beacon/BuoyFloat/BuoyMast/Target/Ring/RadarWhite/Lighthouse*` | Packs 6-7 |
| `Terrain_*.png`, `Greybox_Checker.png`, `*.terrainlayer` | Pack 3 |
| `FlightDebugHUD`, `MatchScreens` (code-drawn UI) | Packs 9-10 |
