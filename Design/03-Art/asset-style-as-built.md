# Asset Style: As Built (from the Fighter)

Status: Draft
Last updated: 2026-10-19

The Fighter ([ArtSource/Fighter/build_fighter.py](../../ArtSource/Fighter/build_fighter.py)) is the reference asset; the Interceptor ([build_interceptor.py](../../ArtSource/Interceptor/build_interceptor.py)), Heavy Fighter ([build_heavy_fighter.py](../../ArtSource/HeavyFighter/build_heavy_fighter.py)) and Support ([build_support.py](../../ArtSource/Support/build_support.py)) follow it. All build on the shared kit [ArtSource/plane_kit.py](../../ArtSource/plane_kit.py) (palette, mesh builder with ink hull, section shapes, gear, broken edges, export, previews). This page records the exact values it uses so props, terrain, clouds, and later planes match it. The visual spec is [lowpoly-asset-style-guide.html](lowpoly-asset-style-guide.html); [art-direction.md](art-direction.md) has the intent. If this page and the script disagree, the script wins; update this page.

## The look in six rules

1. **Flat facets.** Hard normals everywhere. Smooth shading only on round, blunt things (the spinner). No texture detail beyond the palette.
2. **Three tones per colour, picked from the face normal.** Top-facing = light, side = base, belly = shadow. No lighting tricks baked in beyond that.
3. **Hue-shifted tones.** Light = base mixed 25% toward warm white `(255,250,235)`. Shadow = base mixed 30% toward blue-violet `(52,46,110)`. Never pure black, never grey-darkened.
4. **Bold ink outline.** Inverted hull, `1E2433`, **0.045 m** thick on a plane-sized object. Scale it with the object (small props use a thinner hull, large ones a thicker one, roughly 0.3% of the object's longest dimension). Not applied to FX, blur discs, or terrain.
5. **Chunky and toy-like.** Chamfer every corner (at least 8% of edge length). Exaggerate: oversized props, blunt noses, fat wheels. No sharp points, no fine detail that disappears at range.
6. **Squint test.** At 32 px as a solid shape, you can tell what it is.

## Palette (one shared 256x256 atlas)

Atlas is `Assets/_Project/Art/Textures/PlanePalette.png` (512x512), rewritten by every build script from `plane_kit.COLORS`. 16x16 cells of 32 px, four colours per row; each colour takes three cells (light, base, shadow) and leaves the fourth spare. Unused cells are magenta so mistakes are visible. Point sampling (`Closest`), no mipmap blur into neighbours. The layout lives in `plane_kit.swatch_cell`: changing it means re-running every build script (all are deterministic).

| Name | Base | Use |
|---|---|---|
| body | `EFE6D2` | Cream hull and fuselage |
| metal | `7E8898` | Panels, frames, engine |
| darkmetal | `4F5868` | Ordnance, pylons, recesses |
| glass | `8FE3FF` | Canopies, windows |
| tire | `2C3242` | Wheels, rubber |
| white | `F4F2EC` | Roundels, markings |
| sun | `FFB21E` | Accents, prop tips, warning stripes |
| char | `2B2E35` | Exhaust, scorch |
| ember | `FF5E1A` | Fire, glow (flat, no tones) |
| glint | `FFFFFF` | Specular glint on glass (flat) |
| ink | `1E2433` | Outline (flat) |
| team | `D7D7D7` | Neutral ramp, multiplied by team colour at runtime |
| blue / red | `2F7DF6` / `F2463C` | Team colours |
| flame | `FFC93A` | Hot core of flames, tracers, muzzle flashes (flat) |
| leaf | `3E9E52` | Broadleaf canopies |
| pine | `2F7D5B` | Conifers (bluer and darker than leaf) |
| grass | `5DBB63` | Bushes, grass tufts, palm fronds, the islet's top |
| bark | `8A5A3B` | Trunks |
| drywood | `B89A74` | Dead trees, dry brush, driftwood, palm trunk bands |
| sand | `F4C27A` | Mesa tops and talus, desert rock tops |
| sandstone | `E58A45` | Mesa cliffs, desert rocks |
| clay | `C25A3A` | The darker mesa strata |
| stone | `8C96A6` | Mountain, coast and islet rock |
| snow | `F3F6F8` | Snow caps |
| cactus | `4FA36B` | Cacti |
| flower | `FF7BAC` | Flowers, cactus blooms |
| cloud | `F1F7FF` | Clouds: its own ramp, `FFFFFF` light, `D5E6F7` shadow (soft blue, not violet) |
| concrete | `C8CFDA` | Towers, curbs, quay paving, spandrels on glass |
| slate | `7F9CC4` | The city's blue-grey: cladding, deco windows, bridge girders |
| window | `5FA8D8` | Building glass (deeper than canopy `glass`, so a whole facade doesn't glare) |
| brick | `B9654B` | Brick walk-ups |
| asphalt | `434A5A` | Roads, garage decks, the bridge deck |
| water | `2EC4B6` | Fountains (the sea is Unity's own material) |

New asset families add colours to this table in the same three-tone format rather than inventing a second atlas (30 slots are free).

## Materials

| Material | For | Notes |
|---|---|---|
| `Plane_Paint` | Everything coloured | Palette atlas, roughness 0.85, backface culling on |
| `Plane_Team` | Team zones only | Same atlas, multiplied by team tint at runtime (`PlaneVisuals.TeamColors`) |
| `Plane_Ink` | Outline hull | Flat `1E2433`, culled so only the inverted hull shows |
| `Plane_PropBlur` | Blur discs, soft FX | Alpha about 0.22, no culling, no outline |
| `Weapon_Glow` | Flames, tracers, muzzle flashes | Palette atlas, **unlit**, no culling, no outline. Faces go on it with `glow=True` in the kit |
| `FX_Mesh` | Every particle mesh (puffs, chips, sparks, rings, icons) | URP Particles/Unlit on the palette, opaque, culled: palette × particle colour. One submesh per mesh, ink faces included |
| `FX_ShieldBubble` | Shield and repair bubbles | URP Unlit on the palette, alpha-blended, tinted per use with `_BaseColor` |
| `Env_Cloud` | Cloud puffs | URP Unlit on the palette, opaque, culled: the baked tones are the whole shading, so clouds stay white from below and against the sun |

Non-plane assets should reuse `Paint` and `Ink` semantics (a matching `Prop_*` / `Env_*` pair is fine) so the shading and outline read the same.

## Modelling and export conventions

- Geometry in Unity space: x right, y up, z forward, **1 unit = 1 m**.
- One exported object per part that can move, break, or collide; origin on the part's pivot.
- UV set 1 `Palette`: every face sits on one swatch cell, tone chosen from its normal. UV set 2: an ordinary unwrap for skins and decals.
- FBX export: `axis_forward=-Z`, `axis_up=Y`, `bake_space_transform`, face smoothing, no leaf bones, no animation, textures not embedded.
- Generated by script (`ArtSource/<Asset>/build_*.py`), never hand-edited in the exported FBX. Hand tweaks go in the `.blend`, then back into the script.
- Every class shares one plane prefab, so a class model sits on the same part pivots and the placeholder boxes stay as its hitboxes. Lay a new class out so each part covers its hitbox (the Interceptor's engine is its nose intake, its horizontal stabilizer a small tailplane, because those boxes are at the nose and tail).
- Positions Unity needs that differ from the placeholder's are exported as empties, which `PlaneVisuals` applies while the class is flown: `MuzzleLeftInner/Outer`, `MuzzleRightInner/Outer` move the gun muzzles onto the model's barrels, `RailLeft/Right` the rocket rails onto its pods, `WingHitboxLeft/Right` the wing hitboxes onto its wings (`ClassModel.muzzlePositions`, `railPositions`, `wingHitboxPositions`). A model without them keeps the placeholder's. The Support's high wing sits ~0.95 m above the placeholder's, so it moves its wing hitboxes and rails up rather than hang its pods on 1 m pylons.
- Things a part carries are extra objects named after it, origin on their own pivot: a twin's `WingLeftProp` (spins with the engine) and `WingLeftPropDisc` mount on the wing and break off with it; the Support's `BodyRadar` rotodome mounts on the body and turns slowly about its vertical axis (any extra object ending `Radar`). The nose `Propeller` and `PropDisc` are optional (the jet and the twin have none).
- Control surfaces (2026-10-28) ride their part the same way: `WingLeftAileron`/`WingRightAileron`, `HorizontalStabilizerElevatorLeft`/`Right` (split either side of the fin) and `VerticalStabilizerRudder`. The kit's `split_planform` cuts the aft 30% of the chord out over a span range (the fixed surface ends in a blunt face there, with a small spanwise gap at each end), and `control_surface` lofts the moving wedge with its origin on the hinge line and a `<name>Hinge` marker at the far end; `ClassModels` turns the two into the hinge axis. `PlaneVisuals` deflects them with the plane's turn rates as a share of the class's full rate: 20° ailerons and elevators, 25° rudder. A lost part takes its surfaces with it.
- The weapons themselves (`ArtSource/Weapons`) fly nose down +Z with the origin mid-length. Unity scales ordnance uniformly so the nose tip sits at half the stats' length, and tracers (built as a unit box) by the stats' tracer width and length.
- Model the weapons a class carries where the prefab launches them: rocket pods at the rocket rails (wing x ±3.95, y −0.3, or your own `Rail*` markers), a bomb at the bomb bay (0, −0.95, −1.5), a missile at the missile pylon.
- A new class: add `ArtSource/<Class>/build_<class>.py` on the kit, then add it to `ClassModels.Models` (FlightStats asset name, model folder) and rebuild the plane prefab.
- `--preview <dir>` renders fixed shots; broken-edge caps only show in the `*_broken` shot (their hulls poke through the skin when the part is attached, as they are hidden in game).

## VFX (ArtSource/VFX)

- Smoke, fire, dust and spray are **chunky faceted puffs**, never soft or alpha particles. Three puff shapes (lump, pair, cluster of three), ~1 m, on the neutral `team` ramp so the particle colour tints them while the baked top-light / side / belly-shadow tones stay. Particles spin them about the vertical only, so the tones never turn over.
- A fireball is the same puff run through a colour-over-life gradient: white-hot, orange, red, then char as it shrinks away. Puffs grow fast and shrink to nothing instead of fading (they're opaque).
- FX have no ink outline, except graphic icons that must read as UI-like pops (the "BOOM!" burst, the repair wrench): those keep a hull, on the same single material.
- Particle meshes are one submesh on `FX_Mesh`; view-aligned icons face -Z in Unity; sparks point down +Z and are velocity-aligned; rings lie flat in XZ (stood up with a 90° X start rotation to face the camera).
- `VfxBuilder` holds the effect recipes (counts, sizes, speeds, colours); `RenderContactSheet` renders every effect as tiles for a quick look without entering play mode.

## Nature (ArtSource/Nature)

- Props stand on their origin (the ground under the trunk, or the rock's middle), y up, at life size; the Island scatters them as terrain trees at about twice that, with a random yaw. Anything that would be lost from a plane (shells, single flowers) is skipped or grouped.
- Canopies, rocks and puffs are lumpy icospheres (20 faces for a rock or canopy lump, 80 for a cloud puff) with each vertex pushed in or out; rocks get a flattened bottom so they sit. Conifers are stacked 6-7 sided cones with a short skirt; palms and branches are swept tubes.
- Colour by face normal where nature does it: snow on faces that look up (`RockSnow`), on each pine tier's upper half (`TreePineSnow`); desert rock tops in sand.
- Ink: ~0.06 m on trees and rocks (about 0.5% of their height; a little more than the 0.3% rule so it shows when you fly low past them), 0.6 m on the mesas, 0.35 m on the islet. None on grass tufts and flowers (terrain details) or clouds. Objects without ink export with `Plane_Paint` alone (`drop_unused_materials`).
- Hero pieces are built to the Island's numbers (`MESAS`, the islet's top at y = 24) and placed by `IslandSceneBuilder` with mesh colliders. The heightmap keeps a lower, narrower mound inside each so terrain never pokes through the cliffs; change one and check the other.
- Cloud puffs fit a unit sphere (mostly just inside it, belly flattened at y = -0.82): Unity scales each by its cover puff's radii.

## City (ArtSource/City)

- Buildings are whole models standing on their origin (footprint middle at street level, sunk 1.5 m), not stacked floor modules: Unity scales them evenly across x/z to fit a lot and stretches y up to 1.3× to its height, so they're authored at round sizes (~40 m footprints) and their windows survive the stretch.
- Masses are chamfered prisms (`rect_pts`, corners cut 2-5 m) or 12-gons with the ink hull; windows, spandrels, trim, fins and roof insets are **one-sided decal quads 0.2 m off the surface** (`windows`, `strips`, `trim`, `flat`), no hull, so a 200 m facade costs a quad per storey per side. Storeys are toy-sized (~7 m) so they read from the air.
- Roofs read as a light rim round a darker inset decal (a parapet without the geometry). Rooftop clutter is baked in with its own thin ink (`loft(..., outline=0.1)`): a building's 0.3-0.45 m hull would swamp a 3 m AC unit.
- Each building exports a `<Name>Col` twin: just its masses, for the mesh collider. Antennas, billboards and clutter don't collide.
- Ground pieces (sidewalks, roads, crossings, plazas, quays) have no ink, like terrain; the lamps and bollards on them do (lamp poles don't: a hairline isn't worth doubling their triangles).
- Vehicles are ~1.4× life like the trees, nose +z; boats stand on the waterline.

## Budgets

| Asset | Triangles (incl. outline hull) |
|---|---|
| Plane | 1.5k to 3k (Fighter 2.9k, Interceptor 2.9k, Heavy 3.0k, Support 2.9k, each incl. ~220 in hidden broken-edge caps) |
| Pilot, as worn | under 3.8k (default gear 3.7k: ball-jointed limbs and the jet helmet's visor shield; parachute and one canopy under 800) |
| Ordnance in flight (rocket, missile, bomb) | 250 to 550 (rocket 330, missile 526, bomb 268) |
| Tracer, muzzle flash (glow only) | under 40 |
| Large prop (tower, hangar, carrier) | 300 to 1.5k (city buildings 250-1.1k, deco tower 1.1k) |
| Street piece (block, plaza, quay) | under 1.1k (sidewalk block 1k with its lamps, road 50, quay 240) |
| Bridge | 2.5k |
| Vehicle | under 400 (cars ~200, ferry 380) |
| Small prop (tree, rock, crate) | 40 to 300 (rocks 40-160, bushes 80-120, oaks ~200, pines ~300-340; palms are over at 456, from their fronds) |
| Terrain chunk | 2k to 5k |
| Cloud | 80 to 400 (one 80-tri puff per cover puff, ~10 per bank) |
| Hero rock (mesa, islet) | under 1k (mesas ~900, islet ~670) |
| Particle puff / chip / spark | 12 to 120 (puffs 80, 100, 120) |
| VFX icon (BOOM!, wrench) | under 1.2k (BOOM 1.1k, wrench 160) |

## Before you call an asset done

- [ ] Reads as a solid silhouette at 32 px.
- [ ] Only palette colours; no stray materials or textures.
- [ ] Three-tone shading visible from a top-down and a side view (use the script's `--preview` renders).
- [ ] Outline present and not clipping through thin parts.
- [ ] Within triangle budget.
- [ ] Next to the Fighter in one frame, it looks like the same game.

## Open

- [x] Biome colours are in the atlas (see Palette). Terrain tint rules (the terrain layers are still tinted checkers) are Pack 3's.
- [x] Props need a shared outline-thickness rule: `Part(..., outline=)`. The weapons use 0.022 to 0.028 m, about 1.5 to 2% of their length, well over the 0.3% rule so the hull still shows at combat range; small fast things want a thicker share than big ones.
- [x] Clouds: no ink outline. From inside a cloud (they're cover) the inverted hull would black out the view.
