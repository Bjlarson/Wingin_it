# Living World

Status: Proposed (nothing here is built yet)
Last updated: 2026-10-20

## Summary

The Island is beautiful but still. This doc covers the pass that makes it feel alive: five changes, each small on its own, that add up to a world that reacts to the plane and moves when nobody is touching it.

| # | Change | Reacts to the player? | Gameplay impact |
|---|---|---|---|
| 1 | Ground-hugging dust and spray, coloured by the surface | Yes | None (visual) |
| 2 | Little waves on the sea and canal | No | None: the sea collider stays flat |
| 3 | Clouds that drift, clump and breathe | No | **Yes**: clouds are cover, so the cover maths must move with them |
| 4 | Cars and boats that move | No | None (no colliders) |
| 5 | Smoother shadows | No | None |

All of it follows [art-direction.md](art-direction.md) and [asset-style-as-built.md](asset-style-as-built.md): chunky faceted shapes, palette-based colour, no soft alpha particles, hue-shifted tones. "Alive" here means a toy-like diorama in motion, not realism.

## Design rules for everything in this doc

- **Faceted, not soft.** Dust, mist, spray and wakes are the same chunky puffs the VFX pack already uses (lump, pair, cluster of three, on the neutral ramp, tinted by particle colour, spinning about the vertical only). No alpha-faded billboard smoke.
- **Cheap by default.** Anything the player can't see isn't simulated. Prefer closed-form motion (position = function of time) to per-frame simulation: it costs nothing when culled and gives the same result on every client in multiplayer.
- **Never hurts readability.** Ambient motion is lower contrast than planes, tracers and damage smoke. Dust and mist stay under the player's plane silhouette, never in front of the camera's view of an enemy.
- **Same wind everywhere.** One match-wide wind vector (direction plus speed) drives cloud drift, dust and mist drift, smoke lean, and the cloth of the towed banner. Set once per match.

---

## 1. Ground-effect dust, mist and spray

Low flying is the most exciting thing a plane does. It should kick the world up.

### Behaviour

A looping emitter, `GroundKickup`, rides under each plane (the player's, and bots/remote planes within about 300 m of the camera).

- **Trigger:** a plane is within **40 m** of the surface directly below it **and** moving faster than about **60% of stall speed**. Slow taxiing on a runway gives tyre smoke instead (already built: `Touchdown`).
- **Strength:** rises smoothly as the plane drops. Rate and puff size scale with `1 - height / 40` and with speed. At 5 m it's a thick plume, at 35 m a few wisps.
- **Shape:** two trails streaming back from under the wings (so a banked plane kicks up one side more), plus a puff cluster right under the nose. They are spawned in world space and left behind, so they hang in the air and fade, forming a wake. The shared wind nudges them sideways.
- **Lifetime:** 1.5 to 2.5 s, shrinking and fading by scale, not alpha, to stay in the chunky style.

### Colour by surface

The emitter does a short raycast straight down about 10 times a second (not every frame) and picks a colour from what it hits. The colour blends over about half a second when the surface changes, so crossing a beach looks like a smooth shift from sand to water spray.

| Surface | How it's detected | Effect | Proposed colour |
|---|---|---|---|
| **Sea / canal** | Hit collider is on the Water layer (`PlaneController.IsWater`) | White water mist: bright, a little taller, with a few chunky droplets thrown up | `F4FBFF` (light blue-white) |
| **Sand** (beaches, desert, seabed) | Terrain splat weight for the Sand layer is highest | Sand-coloured dust | `E6C88A` |
| **Grass** | Splat weight for Grass | **Brown dirt cloud** (grass itself doesn't puff, the soil under it does) | `8B6B4A` |
| **Rock** (steep or high) | Splat weight for Rock | Grey dust and the odd chip | `A39E96` |
| **Snow** (peaks) | Splat weight for Snow | White powder | `F2F6FA` |
| **Paved** (city, runways, pads) | Splat weight for Paved, or a collider tagged as a pad | Light grey dust, much less of it | `B9B9C2` |
| **Roofs, buildings, bridges** | Any other collider | Nothing | n/a |

Notes:
- Terrain colour comes from the **splat map**, not the material. At load, `EffectsSurface` reads the terrain's alpha maps once into a small lookup (the builder already writes them at 512x512: grass, sand, rock, snow, paved) and answers "what's at this x,z" with no per-frame terrain calls.
- Over shallow water where the seabed is just below the sea, the raycast hits the ocean first, so it is correctly water mist.
- Each hue is the light tone; the VFX material supplies the side and belly tones, the same way smoke already does.

### Details worth doing

- **Prop wash on takeoff and landing:** a short burst of surface-coloured dust as a plane accelerates down a runway or flares out. Uses the same emitter at low speed.
- **Wing-tip curl:** a thin white vapour streak off the wingtips in a hard turn, only when `g` is high. Optional, later.
- **Bomb and rocket impacts** already use `ExplosionGround`; give it the same surface colour table so a bomb on sand kicks up sand and on grass kicks up dirt.

### Build

- New `VfxKind.GroundKickup` (looping) in `VfxBuilder.cs`, new `GroundKickup.cs` component on the plane prefabs (or added by `PlaneVisuals`), `EffectsSurface.cs` for the lookup.
- Particle budget: **40 live puffs per plane**, only the 4 nearest planes to the camera emit. LOD: beyond 300 m skip.

---

## 2. Water with little waves

The sea is currently a flat 30 km `Plane` with the `Greybox_Water` material. Replace it with a stylised, faceted wave surface.

### Look

- Small, chunky, **flat-shaded** wave facets that catch the light in three tones (matching the three-tone palette look), not realistic shimmering water.
- Teal-to-deep-blue gradient: lighter where it's shallow near beaches, darker in open ocean.
- **Foam**: white crests on the tallest facets and a foamy band where the water meets the shore, the quays and the islet.
- A slow drift of the whole pattern along the wind, plus a second layer moving the other way so it never looks like a scrolling texture.

### Hard constraint: the collider stays flat

Hitting the sea is a crash and touching down is a ditching. Wave **height is cosmetic only**: keep the amplitude at about **0.5 m** at most so the visual surface never disagrees noticeably with the flat `y = 0` collider.

### How

- A custom **Shader Graph** (URP, mobile-safe) with **vertex displacement** from three summed sine waves (wavelengths roughly 8 to 25 m, different directions) and **flat normals** from screen-space derivatives, so each triangle is one tone.
- Fade the waves to flat and to the fog colour with distance so the horizon stays clean.
- A subdivided mesh. The current 10x10 Unity plane is far too coarse. Use a ring-grid mesh that is dense near the camera and coarse far out, **snapped to the camera in whole grid steps** so the pattern doesn't swim and the 30 km edge is never visible.
- Depth-based shore foam needs the camera depth texture. Check this against the Mobile renderer; if that costs too much, bake the shoreline into a distance-to-shore texture instead of using depth.
- The **canal** uses the same material; its pattern scrolls along the channel.
- Boats (section 4) and plane ditchings spawn a short foam ring on this surface.

### Build

- `ArtSource`: no Blender work. A shader asset, a material replacing `Greybox_Water`, a ring-grid mesh generator in the editor builder, and `BuildOcean` in `IslandSceneBuilder.cs` swapped to use them.
- Keep the Water layer, the 10 m thick `BoxCollider` and `isStatic` exactly as they are.
- Palette: add `water_deep`, `water_shallow`, `foam` to `plane_kit.COLORS` so the colours stay in the shared palette.

---

## 3. Clouds that move, clump and float through the level

Clouds today are 12 mirrored banks fixed in place. They're **cover**: `CloudCover` blocks missile locks, AA and HUD markers through 40 m or more of cloud, and the cover volume is exactly the rendered puffs. Moving them means moving the cover too, so the visuals and the maths must never disagree.

### Behaviour

- **Drift:** every bank moves along the match wind at about **6 to 10 m/s**, with a small altitude bob (about 10 m, slow). Faster, lower banks sit under the high ones for parallax.
- **Breathe:** each puff's radii swell and shrink a few percent on its own slow cycle, so banks look like they're churning.
- **Clump:** banks that drift close should join up. Because cover is the union of puffs, overlapping banks already combine for free. On top of that, nearby banks get a gentle pull toward each other so they bunch into big storm fronts, then slowly drift apart again into scattered small ones. The cycle is long (several minutes) so the sky differs between the start and end of a match.
- **Float through the level:** a bank that drifts past the map boundary shrinks away out in the fog and reappears at the upwind edge growing back in, so the sky stays populated. Never a pop in view of the player.
- **Keep-out zones:** the existing rules still hold, but as live rules instead of fixed positions: no cloud is allowed to sit over **home AA range**, **air spawns**, **landmark tops** or **approach lanes**. A puff that drifts into one is dissolved smoothly (its radii shrink to zero over a few seconds) rather than blocked, so the rule is enforced without banks bouncing around.
- **High decorative layer:** a sparse layer of slow, high, non-cover clouds above the play space (and the odd low one far below) for depth and parallax. Cheap meshes, no `CloudBank`, no gameplay effect.

### Keeping cover honest (the important part)

- `CloudBank` stays the single source of truth. Each frame it writes the animated centres and radii into both its `CloudPuff[]` (used by `CloudCover`) and the puff transforms (used for rendering), then refreshes its bounds. It is today's "don't move, rotate or scale the bank" rule becoming "only `CloudBank` moves it".
- Bounds are recalculated only when something moved enough to matter (every 0.25 s is fine), not every call.
- Moving **inside** a cloud shouldn't feel different: the white-out and the **IN CLOUD / CLOUD EDGE** labels read from `CloudCover.Immersion` as they do now.
- **Multiplayer:** make cloud state a **pure function of the match seed and match time**, not a simulation each client steps. Then every client agrees exactly on where cover is, with nothing to sync. Breathing and bob are sine functions of time. Drift is `start + wind * t`, wrapped. Clumping is scripted as paired banks on converging paths whose meeting times come from the seed, not a live attraction sim.
- **Bots** already use `CloudCover.CanSee` and `IsHidden`; they just see moving cover. Their hide-in-cloud choice should prefer a cloud with plenty of life left before it dissolves or leaves the map.

### Extra touch (optional)

- **Cloud shadows crossing the ground.** A scrolling noise cookie on the sun light, moving at wind speed, puts drifting shadow patches across the terrain and sea. One cheap texture; it makes the clouds feel like they're really in the world. Check it doesn't fight the shadow work in section 5.

### Tests that change

- `CloudCoverTests` and `IslandSceneTests` today assume fixed mirrored positions: mirrored pairs, a bank centre hides a plane, 60 m+ clearance under every puff, nothing in AA range, a cloud within 10 s of every fight area. Keep the maths tests (they're about the puffs), and turn the placement tests into **sampled-over-time** tests: step the cloud function across a whole match and assert the rules hold at every sample. Add a determinism test (same seed and time, same puffs, run twice).

---

## 4. Moving cars and boats

Cars and boats are static today: `PlaceVehicle` in `CityModels.cs` drops `CarSun`, `CarWhite`, `CarTeal` and `Bus` (up to three per road, in their lanes, right-hand traffic) and `Tugboat` and `Ferry` (five on the canal). They have no colliders and are culled when tiny. Make them move.

### Cars

- Each road lane becomes a **path** (the lane centre line, in the lane's direction). A car's position is `path(distance)` where `distance = (offset + speed * time) mod pathLength`. No physics and no per-frame sim.
- **Speed:** 8 to 14 m/s for cars (a bit faster in the long avenues), buses about 7. Cars in the same lane share a speed so they never overlap.
- **Where they go:** v1 runs each car end to end along its street and loops: at the city edge it slides under cover of a corner or building and reappears at the start. v2 chains lanes through intersections with a seeded turn choice, so traffic flows round the blocks.
- **Intersections:** a simple time-sliced rule ("north-south goes for 8 s, then east-west") with cars slowing to a stop and the existing traffic lights switching colour. Optional, and the first thing to cut if it's costly.
- **Looks:** keep the single mesh per vehicle. A tiny body sway at speed and a very subtle exhaust puff on the buses is enough at the viewing distance (planes see them from 30 m up and higher).

### Boats

- `Tugboat` and `Ferry` follow the **canal centre line**, heading up or down, at 4 to 6 m/s. At the sea end they sail out into the fog and re-enter at the city end, so there is always traffic.
- They pass **under the bridges** with the deck 34 m above, so keep the boats' mast height well below that.
- Gentle **bob and roll** from the same wave function as the water (section 2), so boats ride the waves. A white **bow wave and wake**: a V of chunky foam puffs behind the hull, the same water-mist puff from section 1 at a smaller size, left on the surface and fading.
- Ferries could carry a small bounce on the horn timing if audio wants a cue (Pack 9 Ambient: "city hum", "sea waves").

### Build

- A `PathFollower` component (path, speed, offset, loop mode) added by `PlaceVehicle`, plus a `TrafficPaths` data object the builder writes alongside the roads and the canal. Replace "up to three vehicles in lanes" placement with offsets along each path so cars start spread out.
- Paths come straight from where `BuildStreets` already knows the lanes are, and from the canal centre in `BuildCanal` (the code near the "Boats" comment), so this is data the builder already has.
- **Cost:** the update is one cheap path evaluation per vehicle, skipped entirely beyond about 800 m or when the LOD group has culled it. Because it's a function of time, the vehicle is in the right place the instant it comes back into range.
- **Tests:** EditMode, pure maths: a path follower never leaves its path, wraps cleanly, two cars in a lane never overlap, a boat stays clear of the bridge towers. PlayMode: vehicles have moved after one second and are always on the road or in the canal.

---

## 5. Smoother shadows

Shadows currently look blocky. Here is what the project has now and what to try.

### What's set today

| Setting | PC asset | Mobile asset | Light |
|---|---|---|---|
| Shadow map | 2048 | 1024 | Directional sun, **soft shadows**, bias 0.05, normal bias 0.4 |
| Shadow distance | **50 m** | **50 m** | Quality levels also say 40 m |
| Cascades | 4 | **1** | |
| Soft shadows | on (quality 3) | **off** | |

### Likely causes (in the order to check them)

1. **Mobile settings reaching PC play.** A 1024 map, a single cascade and soft shadows off is exactly the blocky look. First confirm which asset the Editor and the build are actually using for the quality level in use (the quality list and the renderer assets don't quite agree).
2. **Low-poly shadow casters.** The planes and buildings are deliberately faceted, so their shadows are too. That part is the style and stays. What shouldn't be blocky is the *edge* of the shadow.
3. **Short distance, big spread.** At 50 m with the cascades split as they are, the farthest texels are coarse; and a plane flying 100 m or more above the ground throws a shadow *outside* the shadow distance entirely, so planes get either a blocky shadow or none.
4. **Inverted-hull ink outlines.** Make sure the ink meshes (the outline hulls on planes, trees, rocks, mesas) don't cast shadows. If they do, they add a jagged double edge to every shadow. Check `ShadowCastingMode.Off` on the ink renderers.
5. **Bias.** Too much normal bias shrinks and steps shadow edges; too little gives acne. Tune after the rest.

### The plan

- **Planes get a projected "blob" shadow instead of relying on the shadow map.** A soft round-ish shadow, raycast onto the ground below each plane, scaled by altitude (smaller and fainter the higher you are). It is smooth by construction, always visible, and the classic flight-game altitude cue, which is a real gameplay help here. It follows terrain slope. It also draws on the sea.
- **Real shadow maps for what is near:** buildings, trees, planes on the ground.
  - PC: **4096** map, distance **120 to 150 m**, 4 cascades with tighter splits (about 0.05, 0.15, 0.40), soft shadows at the highest quality, cascade border widened so the blends are invisible.
  - Mobile: **2048** map, **2** cascades, distance about 80 m, soft shadows low quality or on if it fits the budget.
- **Stable fitting and tuned bias** so edges don't shimmer as the camera moves.
- **Trees and details:** keep grass and flowers casting nothing (already so); give the tree LOD groups a shadow-casting level so far trees don't pay for it.
- If the blob shadows work well, **drop plane shadows from the shadow map** (cast-shadows off on the planes) and use the saved budget for the buildings and trees.
- **Cloud shadows** (section 3) are a separate light cookie, so they shouldn't affect these numbers.

### How to judge it

Take Editor captures before and after at the same fixed spots: a plane low over the airfield, a city street at dusk angle, a tree line, a high pass over grass. Blocky edges, shimmer and missing plane shadows are the three things to look for. Capture with the PC and Mobile quality levels both.

---

## Order of work and rough size

| Order | Item | Why this order | Size |
|---|---|---|---|
| 1 | Shadows (check, tune, blob shadows) | Mostly settings; fixes the thing that is bothering you most right now | Small to medium |
| 2 | Waves | Huge visual win; the sea is most of the screen | Medium |
| 3 | Ground dust and spray | The most reactive of the lot; needs the surface lookup | Medium |
| 4 | Moving cars and boats | Pure data and a small component | Small to medium |
| 5 | Moving clouds | Biggest risk (touches cover rules, tests, multiplayer), so last and carefully | Medium to large |

## Performance budget

Target the existing frame budget with no visible change in the Mobile tier.

- Waves: vertex shader only on Mobile, no transparency, no refraction.
- Dust and spray: at most 40 puffs per emitting plane, the 4 nearest planes.
- Traffic: no per-frame work beyond path evaluation, skipped beyond 800 m.
- Clouds: bounds refreshed at 4 Hz, puff transforms updated for banks within view distance only.
- Shadows: PC and Mobile tiers as in section 5; blob shadow is one quad per plane.

## Open questions

- [ ] Wind: one fixed direction for the whole match, or a slow shift over the match? (A shift is livelier but changes where cover is over time.)
- [ ] Should cloud cover also clump deliberately toward the contested middle, or stay uniform?
- [ ] Brown dirt for grass: is the soil-cloud right for the stylised look, or should grass kick up green blades too?
- [ ] Traffic: loop end to end (cheap) or flow through intersections with lights (livelier)?
- [ ] Do car and boat hits matter (bullets, bombs destroy them)? Proposed: yes later, as a scoring-free "wreck" effect.
- [ ] Day length: still fixed sunny, or does a slow sun move make shadows and clouds change through the match?
