# Art Direction

Status: Draft
Last updated: 2026-10-13

## Summary

Cartoon but readable. The look is bright, playful, and exaggerated, but gameplay information (class, team, damage state) has to be readable at a distance.

## Visual Inspiration

- Fortnite
- Sky Force Reloaded
- Pilotwings
- Roblox aviation games

## Key Features

- Bright, saturated colors
- Puffy, stylized clouds (also usable as cover)
- Exaggerated explosions
- Big, thick smoke trails
- Easy-to-identify aircraft silhouettes

## Readability Rules

| Info | How it's shown |
|---|---|
| Team | Strong team colors (Blue / Red) on wings and tail, plus outline or nameplate |
| Class | Unique silhouette per class (e.g. Heavy = chunky twin-engine, Interceptor = sleek delta wing) |
| Damage state | Smoke color and density: none, then grey wisps, then thick black smoke + fire |
| Player identity | Cosmetics (skins, decals, trails), never at the cost of the rows above |

## Style Notes (proposed)

- **Shading:** toon or soft-gradient shading with stylized outlines on planes
- **Proportions:** chunky and slightly toy-like planes (big props, rounded noses)
- **Explosions:** comic-style: big puffs, debris, a brief flash, optional "BOOM" text pop
- **Destruction:** planes break into a few large chunky pieces rather than realistic wreckage
- **Environment:** low-poly or stylized terrain, with biome identity carried by color (city = blues/greys, desert = oranges, mountains = greens/whites, ocean = teal)

## Cosmetics (decided: the main player reward)

Cosmetics are what players earn and how they differentiate themselves. See [progression.md](../01-Game-Design/progression.md) for the full list.

Rules so cosmetics never hurt readability:
- **Team color zone:** wingtips and tail are always team-colored and can't be overridden by skins.
- **Silhouette is locked:** skins change paint and decals, never the plane's shape.
- **Damage smoke wins:** custom trails switch to standard damage smoke in the Damaged and Critical states.
- **Plane models need UV layouts and material slots** that support swappable paint and decals from day one.

## Modular Planes (required by part damage)

Planes take per-part damage and can lose wings, tails, and engines, so every plane model must be built **modular**:

- Separate meshes for **body, left wing, right wing, tail, engine/propeller**, each with its own collider/hitbox.
- **Broken-edge caps** where parts detach (jagged stumps, sparks, small fire).
- **Damage decals** per part (bullet holes, scorch marks) that layer over cosmetic skins.
- Detached parts become **falling physics debris** (cartoon tumble + smoke), despawned after a few seconds.
- Cosmetics (paint and decals) must look right on each part independently, including on falling debris.

## Open Questions

- [ ] Render pipeline: URP (recommended for mobile + PC)?
- [ ] Asset approach: custom models, or start with an asset pack for prototyping?
- [ ] Time of day: fixed sunny, or a day/night cycle?
