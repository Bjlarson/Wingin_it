# Living World

Status: In progress. Sections 5 (shadows), 2 (waves) and 1 (ground dust and spray) are built; sections 3 and 4 are proposed.
Last updated: 2026-10-21

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

**Built (2026-10-21).** See "As built" at the end of this section. The rest of the section is the original proposal.

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

### As built

- **Wind** (`World/Wind.cs`): the match wind, a constant for now: heading 60° (the sea's waves now take theirs from it, `OceanWaves.WindHeading = Wind.Heading`), 4 m/s. It drifts the kick-up puffs.
- **Surface lookup** (`Effects/EffectsSurface.cs`): `Classify(collider, point)` gives a `GroundSurface`: Water (the Water layer), Paved (a runway: a collider under an `Airfield`, inside its runway rectangle), the terrain's strongest splat layer (each terrain's alpha maps are read once, on first use, into a per-texel lookup; layers are matched by name: Grass, Sand, Rock, Snow, Paved), else None (roofs, bridges, quays). `Color` is the table above; `Amount` is how much it kicks up (paved 0.4, rock 0.8, none 0, the rest 1). `Below(point)` raycasts for callers without a hit.
- **Effect** (`VfxKind.GroundKickup`, `VfxBuilder`): four looping systems, 40 puffs in all: **Left** and **Right** wing trails (14 each, 11 a second at full strength), **Nose** (7, 0.8 size) and water **Droplets** (5 chunks thrown up, falling back). Puffs are 4.5 to 7.5 m, live 0.9 to 1.5 s, bloom fast then shrink away by scale. They start with **45% of the plane's velocity** (inherit velocity; 85% hung round the plane, so it was lowered on 2026-10-21 to leave them streaming 20 to 60 m behind) and a plain drag of 1.6 (not Unity's default, which multiplies drag by size and speed and stopped the big puffs dead), so they stay in view of the chase camera for a moment before dragging behind; the wind carries them sideways.
- **Component** (`Flight/GroundKickup.cs`, on the plane prefab, so bots get it too): every frame it raycasts down (skipping rigidbodies), looks the surface up 10 times a second, and eases the colour and amount toward it (about 0.5 s). Strength is `StrengthAt(height, speed)`: 0 at 40 m or below 60% of the stall speed, then `(1 - h/40)` times 35% to 100% from the slowest speed to top speed. Each wing trail uses **its own wingtip's height**, so a bank kicks up the low side more. The trails sit **6 m out** (times the class's visual scale) and spray **35° outward**, out past the wingtips: the chase camera sits above and behind, so the ground right under the plane is hidden by the plane itself. Over the sea the emitters sit on `OceanWaves.Height`.
- **Readability:** puff size also shrinks below 8 m of height (`SizeAt`, down to 30% on the deck), so the dust stays under the plane's silhouette. Without this, a takeoff roll buried the chase camera in 7 m puffs; now the runway gives a low grey prop wash.
- **Budget:** only the 4 nearest planes to the camera that want to emit, within 300 m (`IsAmongNearest`); the rest stop their emitter and let the puffs finish.
- **Prop wash** on takeoff comes for free: the trigger is speed, not "airborne", so a takeoff roll past 60% of stall kicks up the runway (paved, so a little).
- **Blasts and hits** use the same table: `Explosions.PlayGround` tints a ground blast's Dust and Dirt (bombs, rockets, crashes via `EffectsSurface.Below`), and bullet hits tint their dust. Over roofs and buildings they keep the old colours. `VfxEffect` gained `IndexOf`, `Tune` (rate and size against the played scale) and `SetColor`, and resets rate and colour each time it's played from the pool.
- **Tests:** `GroundKickupTests` (EditMode: the height and speed limits, thicker lower and faster, size by strength and by height, the nearest four, the splat lookup, layer names, the colour table, the wind) and in `IslandSceneTests` (PlayMode): `EffectsSurface_MatchesTheSplatMap_TheSeaAndTheRunways` (the lookup agrees with the terrain's own alpha maps across a 19×19 grid, finds sea, grass and sand, and every runway is paved) and `FlyingLow_KicksUpTheSurface_AndClimbingStopsIt` (spray at 8 m over the sea, none at 300 m, dust off a runway).

Checked in play (Editor stills): spray over the sea and sand dust read from the chase camera as chunky puffs under the plane; from the side, the plume reads well at range (how you see bots); grass gives the brown soil cloud; the runway roll gives low grey puffs.

Not done yet:
- Wing-tip vapour in hard turns (optional in the proposal).
- Rock chips (rock gives grey dust only).
- The puffs are opaque and solid, so the brown grass dirt can read as clods more than a cloud; see the open question on brown dirt.
- A Mobile-tier frame-time check with four planes kicking up at once.

---

## 2. Water with little waves

**Built (2026-10-21).** See "As built" at the end of this section. The rest of the section is the original proposal.

The sea was a flat 30 km `Plane` with the `Greybox_Water` material. Replace it with a stylised, faceted wave surface.

### Look

- Small, chunky, **flat-shaded** wave facets that catch the light in three tones (matching the three-tone palette look), not realistic shimmering water.
- Teal-to-deep-blue gradient: lighter where it's shallow near beaches, darker in open ocean.
- **Foam**: white crests on the tallest facets and a foamy band where the water meets the shore, the quays and the islet.
- A slow drift of the whole pattern along the wind, plus a second layer moving the other way so it never looks like a scrolling texture.

### Hard constraint: the collider stays flat

Hitting the sea is a crash and touching down is a ditching. Wave **height is cosmetic only**: keep the amplitude at about **0.5 m** at most so the visual surface never disagrees noticeably with the flat `y = 0` collider. (Relaxed to **1.45 m** on 2026-10-21 for visible swells, at the user's choice: the collider still stays flat, so a plane skimming the sea can dip a little into a crest before it crashes.)

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

### As built

Two objects under World:
- **Ocean**: what you hit. Unchanged in effect: a static `BoxCollider` (30 km, 10 m thick, top at y = 0) on the Water layer. It no longer draws.
- **Ocean Surface**: what you see. No collider, no shadow casting. `OceanSurface` keeps it under `Camera.main` in whole 4 m steps (play mode only; in the Editor it sits at the origin).

The pieces:
- **Waves** (`OceanWaves`, runtime C#): five summed sines, the single source of truth. `OceanWaves.Apply` pushes them to the shader as global arrays; `OceanWaves.Height(x, z, t)` gives the same open-sea surface in C#, for boats to ride later (section 4; it leaves out the shader's calming in shallow water). The wind heading is 60°, a constant until the match has a wind.
  - **Swells** (added 2026-10-21, so the waves are visible from the air): 70 m and 45 m long, 0.65 and 0.45 m tall, rolling down the wind at 8 and 6 m/s.
  - **Chop** on top: 12, 18 and 28 m long, 0.15, 0.12 and 0.08 m tall. Two run down the wind and one crosses back against it.
  - **Perlin noise** (added 2026-10-21, so the sea isn't a regular lattice of sines). A slow, low "roughness" (180 m features, drifting 1.5 m/s down the wind) scales every wave between 0.55 and 1, so some patches are calm and some rough. On top is a bumpy height noise of its own: two octaves, 40 m features, ±0.25 m, drifting 3 m/s. `OceanWaves.Perlin` uses its own integer hash, and the shader's `Perlin` matches it line for line, so C# and the shader agree (Unity's `Mathf.PerlinNoise` has no shader twin).
  - Together they reach `MaxHeight` = 1.7 m: 1.45 m of waves plus 0.25 m of noise. The waves were first built at 2 m, then made a little less bumpy.
- **Mesh** (`OceanSurface.BuildMesh`, saved as `Terrain/OceanSurface.asset`): a full grid, 4 m apart out to 200 m from the middle, then each gap 8% wider than the last out to 15 km. It is a full grid in both directions, so it has no T-junctions and no cracks. It stays under 65,535 vertices, so it keeps 16-bit indices. Each vertex's `uv.x` is the grid spacing there.
- **Shader** `Winging It/Water` (hand-written HLSL like the blob shadow, not Shader Graph; opaque, vertex displacement only, no depth texture):
  - Each wave fades out where the grid is too coarse to carry it (under 3 cells per wavelength), so nothing aliases or crawls as the surface moves. All waves fade to flat between 400 and 1500 m from the camera, so the horizon is clean. The swells calm by 85% in shallow water (the shore map's R), so the canal only ripples and the waves die out at the beaches.
  - **Solid blue with white caps** (changed 2026-10-21 at the user's request: the first version's three-tone facets, teal shallows and sun glints looked pixelated). The water is one blue (`water_deep`). The swells give it shape without facets: a smooth per-pixel normal from the waves' slope, steepened 2.5x for the sun shading, and crests tinted 30% lighter, troughs 30% darker. **White caps are textured froth** (2026-10-21). Above 35% of the maximum height, the foam texture starts to show. The higher the crest, the more of it shows: all of it at 70%. So caps grow from the peaks as froth, not flat blobs, anti-aliased over one pixel. The foam texture is `Art/Textures/WaterFoam.asset`, made once by `IslandSceneBuilder.FoamTexture`: tileable Worley cell borders, 256², big cells plus a finer layer. It tiles every 14 m and drifts with the noise. Its low values (the cell borders) foam first, so a cap starts as a web of lines and fills in. There is also a band of foam along every shore and quay; its edge rises and falls with the waves.
  - Receives sun shadows, takes fog, and has DepthOnly and DepthNormals passes (the PC renderer's SSAO needs the second).
- **Shore map** (`IslandSceneBuilder.BakeShore`, `Terrain/OceanShore.asset`): baked from the heightmap over the 5.6 km terrain square at 1024² (RG8). G is shore foam (1 on land, fading to 0 at 16 m out). R is shallowness (0 at 20 m deep, 1 at the waterline), which calms the swells. This replaces the depth-texture idea, so it costs the Mobile tier nothing extra. The canal is the same sea, so it gets the calm water and the quay foam for free.
- **Palette**: `water_deep` `1E6E9C`, `water_shallow` `3FC1C9` and `foam` `F4FBFF` were added to `plane_kit.COLORS` (and `PlanePalette.png`). `water_shallow` is unused for now. The Water material (`Art/Materials/Water.mat`) is created with them and then kept between builds for hand tuning.
- **Blob shadow** over the sea sits `OceanWaves.MaxHeight` (1.7 m) higher, so wave crests never cut holes in it.
- **Tests**: `OceanTests` (EditMode: the height bound, swells long and carrying most of the height, Perlin range / smoothness / determinism, roughness and noise breaking up the sea, waves move, every wave spans 3 cells, grid lines, mesh fits 16-bit indices and carries the spacing, snapping) and `IslandSceneTests.Sea_IsFlatToHit_AndWavesFollowTheCamera` (PlayMode).

Not done yet:
- The canal's own along-channel scroll.
- Foam rings for ditchings and boats.
- The boundary buoys don't ride the swells: they're static at y = 0, so a big swell can look like it floods or lifts them.
- A Mobile-tier frame-time check (the fragment stage now sums five waves and three Perlin lookups per pixel).

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

**Built (2026-10-20).** See "As built" at the end of this section. The rest of the section is the original analysis.

### What was set before

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

### As built

What the audit found:
- The Editor and Standalone use the **PC** asset (quality level 1), so mobile settings weren't leaking into PC play. The PC map was simply too short (50 m), so most of what the chase camera sees had no shadows at all, then popped in close.
- **Ink hulls are not the problem.** Every pack remaps its outline to the one `Plane_Ink` material, which uses URP Unlit, and that shader has no ShadowCaster pass, so the hulls never cast.
- **Full-strength shadows on dim ambient go near black.** The sky ambient is only about (0.14, 0.22, 0.38), so grass in full shadow turned into black blotches near the camera. The sun's shadow strength is now **0.65** (`SetUpAtmosphere`), which gives the soft, coloured, toy-like shadow on every surface.

**Shadows setting** (Settings > Graphics > SHADOWS, `ShadowProfile`, applied by `SettingsApplier` at start, on change and on every scene load). Edge softness comes from the soft-shadow filter, which is a fixed number of shadow-map texels wide, so the near cascades are deliberately generous: edges stay a little soft rather than razor sharp.

| Level | Map | Distance | Cascades (splits) | Soft filter |
|---|---|---|---|---|
| Off | none | none | n/a | n/a |
| Low | 1024 | 60 m | 1 | low |
| Medium (phone default) | 2048 | 100 m | 2 (0.25) | medium |
| High (PC default) | 2048 | 150 m | 4 (0.12 / 0.30 / 0.55) | high |

The PC and Mobile URP assets ship with the High and Medium values, so the Editor's Scene view matches the defaults. In play mode in the Editor the applier writes to the URP asset and puts the shipped values back when play mode ends. Sun shadow strength is 0.65 on every level. `RenderSettings.sun` is set by `SetUpAtmosphere` so the applier and the planes can find the sun.

Planes:
- Planes cast their **real silhouette** into the shadow map, as before.
- `PlaneShadow` (on the plane prefab) fills in where that shadow can't reach: past the shadow distance from the camera (it fades in over the last 20%), or everywhere with Shadows set to Off. It raycasts straight down each frame, skipping anything with a rigidbody (planes, debris), and lays a soft ellipse on whatever it hits: terrain, roofs, runways, the sea. It lies flat on the slope, turns with the heading and narrows with bank and pitch (never below 15%). It is 10 x 8.5 m times the class's visual scale, shrinks to 55% and fades out by **300 m** above the ground (`1 - t²`).
- Shader `Winging It/Blob Shadow` (material `FX_BlobShadow`): a multiply toward a cool blue-grey (`52,5C,85`), depth offset so it never z-fights, fading into the fog.
- Tests: `PlaneShadowTests` (height curve, hand-over, footprint) and `GameSettingsTests` (defaults, clamp, reset, levels increasing), both EditMode.

Not done yet: a Mobile-tier capture pass, and a frame-time check of High on a low-end PC.

---

## Order of work and rough size

| Order | Item | Why this order | Size |
|---|---|---|---|
| 1 | Shadows (check, tune, blob shadows) **Done** | Mostly settings; fixes the thing that is bothering you most right now | Small to medium |
| 2 | Waves **Done** | Huge visual win; the sea is most of the screen | Medium |
| 3 | Ground dust and spray **Done** | The most reactive of the lot; needs the surface lookup | Medium |
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
- [ ] Brown dirt for grass: is the soil-cloud right for the stylised look, or should grass kick up green blades too? (Built as brown soil, 2026-10-21; the solid puffs can read as clods. Needs a look in play.)
- [ ] Traffic: loop end to end (cheap) or flow through intersections with lights (livelier)?
- [ ] Do car and boat hits matter (bullets, bombs destroy them)? Proposed: yes later, as a scoring-free "wreck" effect.
- [ ] Day length: still fixed sunny, or does a slow sun move make shadows and clouds change through the match?
