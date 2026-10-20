"""
Builds the nature pack (Design/03-Art/final-asset-list.md, Pack 4, plus Pack 3's cloud meshes) from scratch, then
exports it for Unity.

    blender --background --factory-startup --python ArtSource/Nature/build_nature.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Nature/Nature.fbx     one object per asset, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png    the shared palette atlas (rewritten, same as the planes')
    ArtSource/Nature/Nature.blend                    the generated scene, for hand tweaks and renders

Props stand on their origin (the ground under the trunk or the rock's middle), y up, at their real size in metres;
Unity scatters them as terrain trees and details with a random yaw and a 0.8-1.3 scale:
    TreeOak, TreeOakTall                broadleaf, grassland        TreePine, TreePineTall, TreePineSnow  mountains
    TreePalm, TreePalmBent              coast                       TreeDead                              desert
    Bush, BushDry                       grassland / desert          GrassTuft, Flowers                    details, no ink
    Rock, RockTall, RockFlat            stone boulders              RockSnow, RockDesert                  variants
    CliffRock                           a tall crag for steep slopes
    BeachRocks, Driftwood               shoreline                   Cactus, CactusSmall                   desert
Hero meshes, at their place's real size (IslandSceneBuilder.Mesas / IsletRadius), with mesh colliders in Unity:
    Mesa1..Mesa3                        flat-topped sandstone buttes, origin at the lowland height (y = 0 = LandHeight)
    Islet                               Lighthouse Rock's base: cliffs from the sea to a grassy top at y = 24
Clouds, no ink, no shadows: Unity scales each by its puff's radii, so the mesh fits a unit sphere (the cover volume):
    CloudPuffA..CloudPuffC              lumpy faceted balls with flat-ish bellies

Everything is on the shared plane materials (Plane_Paint, Plane_Ink) and palette; the ink hull is scaled to each
asset's size, and objects without one export with the paint material only.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import MODELS_DIR, Model, Part, preview_arg, render_previews, to_blender  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "Nature", "Nature.fbx")
BLEND_PATH = os.path.join(HERE, "Nature.blend")

# Island layout these hero meshes are built to (IslandSceneBuilder): (radius, height above the lowland) per mesa,
# and the islet's top height and radius.
MESAS = [(130.0, 120.0, 11), (100.0, 90.0, 23), (80.0, 140.0, 37)]
ISLET_TOP, ISLET_RADIUS = 24.0, 90.0

PROP_INK = 0.06  # trees and rocks, ~0.5% of their height: enough to show when flying low past them


# ---------------------------------------------------------------------------------------------- shapes


def yring(y, r, sides, phase=0.0, cx=0.0, cz=0.0, jitter=0.0, rnd=None):
    """A horizontal ring round the vertical axis (Unity space), optionally with a jittered radius."""
    pts = []
    for k in range(sides):
        a = phase + 2 * math.pi * k / sides
        rr = r * (1.0 + (rnd.uniform(-jitter, jitter) if rnd else 0.0))
        pts.append((cx + rr * math.cos(a), y, cz + rr * math.sin(a)))
    return pts


def tube(p, path, radii, sides, color, cap_start=None, cap_end=None, fan_end=None, phase=0.0, outline=True):
    """Rings of ``radii`` swept along ``path`` (Unity points), framed so they don't twist."""
    path = [Vector(v) for v in path]
    rings, a_prev = [], None
    for i, pt in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        if a_prev is None:
            ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 0, 1))
            a = (ref - t * ref.dot(t)).normalized()
        else:
            a = (a_prev - t * a_prev.dot(t)).normalized()
        b = t.cross(a)
        a_prev = a
        r = radii[i]
        rings.append([tuple(pt + (a * math.cos(phase + 2 * math.pi * k / sides) + b * math.sin(phase + 2 * math.pi * k / sides)) * r)
                      for k in range(sides)])
    if fan_end is not None:
        fan_end = (tuple(fan_end[0]), fan_end[1])
    return p.loft(rings, color, cap_start=cap_start, cap_end=cap_end, fan_end=fan_end, outline=outline)


def lump(p, center, radii, seed, color, subdiv=1, bumps=0.18, floor=None, outline=True):
    """
    A faceted, lumpy ball (icosphere: subdiv 1 = 20 faces, 2 = 80) of ``radii``, each vertex pushed in or out a little.
    ``floor`` flattens everything below that height (relative to the centre) so it sits on the ground. ``color`` is a
    name or f(unity normal, unity face centre) -> name, e.g. snow on the faces that look up.
    """
    rnd = random.Random(seed)
    c = Vector(center)
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=1.0)
    pos = {}
    for v in tmp.verts:
        d = v.co.normalized()
        k = 1.0 + rnd.uniform(-bumps, bumps)
        off = Vector((d.x * radii[0], d.z * radii[1], d.y * radii[2])) * k
        if floor is not None and off.y < floor:
            off.y = floor
        pos[v] = c + off
    verts = {v: p.vert(pos[v]) for v in tmp.verts}
    faces = []
    for f in tmp.faces:
        pts = [pos[v] for v in f.verts]
        mid = sum(pts, Vector()) / len(pts)
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0]).normalized()
        if n.dot(mid - c) < 0:
            n = -n
        name = color(n, mid) if callable(color) else color
        faces.append(p.face([verts[v] for v in f.verts], name))
    tmp.free()
    bmesh.ops.recalc_face_normals(p.bm, faces=faces)
    (p.hull_faces if outline else p.loose_faces).extend(faces)
    return faces


def snow_on_top(base, threshold=0.45):
    return lambda n, mid: "snow" if n.y > threshold else base


def cone_tier(p, y0, y1, r, sides, seed, color="pine", snow=False):
    """One tier of a conifer: a short skirt, then a cone to the tip (its top half snowed on for the snowy pine)."""
    rnd = random.Random(seed)
    phase = rnd.uniform(0, math.pi)
    rim = yring(y0, r, sides, phase, jitter=0.08, rnd=rnd)
    lip = yring(y0 + 0.25, r * 0.94, sides, phase)
    mid = yring(y0 + (y1 - y0) * 0.5, r * 0.5, sides, phase)
    tip = (0.0, y1, 0.0)
    if snow:
        return p.loft([rim, lip, mid], color, cap_start=color, fan_end=(tip, "snow"))
    return p.loft([rim, lip, mid], color, cap_start=color, fan_end=(tip, color))


# ---------------------------------------------------------------------------------------------- trees


def trunk(p, top, r0, r1, sides=6, color="bark", lean=(0.0, 0.0)):
    return tube(p, [(0, -0.5, 0), (lean[0] * 0.5, top * 0.5, lean[1] * 0.5), (lean[0], top, lean[1])],
                [r0, (r0 + r1) * 0.5, r1], sides, color, cap_start=color, cap_end=color)


def build_oaks():
    a = Part("TreeOak", outline=PROP_INK)
    trunk(a, 4.6, 0.75, 0.45)
    tube(a, [(0, 3.2, 0), (1.0, 4.4, -0.4), (1.7, 5.4, -0.8)], [0.32, 0.26, 0.2], 5, "bark", cap_end="bark")
    lump(a, (0, 7.0, 0), (3.6, 2.9, 3.5), 1, "leaf")
    lump(a, (-2.3, 6.0, 0.9), (2.2, 1.9, 2.1), 2, "leaf")
    lump(a, (2.0, 6.4, -1.1), (2.3, 2.0, 2.2), 3, "leaf")

    b = Part("TreeOakTall", outline=PROP_INK)
    trunk(b, 6.0, 0.7, 0.4, lean=(0.3, 0.2))
    lump(b, (0.3, 8.6, 0.2), (3.1, 3.5, 3.0), 4, "leaf")
    lump(b, (0.6, 11.4, 0.0), (1.9, 1.8, 1.9), 5, "leaf")
    lump(b, (-1.6, 7.6, -1.2), (1.8, 1.6, 1.8), 6, "leaf")
    return [a, b]


def build_pines():
    a = Part("TreePine", outline=PROP_INK)
    trunk(a, 3.0, 0.5, 0.35)
    for i, (y0, y1, r) in enumerate([(2.0, 8.0, 3.4), (5.2, 11.0, 2.6), (8.4, 14.0, 1.8)]):
        cone_tier(a, y0, y1, r, 7, 10 + i)

    b = Part("TreePineTall", outline=PROP_INK)
    trunk(b, 3.0, 0.45, 0.3)
    for i, (y0, y1, r) in enumerate([(2.0, 7.0, 2.6), (4.8, 9.8, 2.2), (7.6, 12.6, 1.7), (10.4, 16.0, 1.2)]):
        cone_tier(b, y0, y1, r, 6, 20 + i)

    c = Part("TreePineSnow", outline=PROP_INK)
    trunk(c, 3.0, 0.5, 0.35)
    for i, (y0, y1, r) in enumerate([(2.0, 8.0, 3.4), (5.2, 11.0, 2.6), (8.4, 14.0, 1.8)]):
        cone_tier(c, y0, y1, r, 7, 30 + i, snow=True)
    return [a, b, c]


def frond(p, base, yaw, length, droop, width, seed):
    """A palm frond: a flat, pointed blade that arcs out and droops, chunky enough to keep an outline."""
    rnd = random.Random(seed)
    yaw += rnd.uniform(-0.15, 0.15)
    d = Vector((math.cos(yaw), 0.0, math.sin(yaw)))
    side = Vector((-d.z, 0.0, d.x))
    b = Vector(base)
    sections = []
    for t, w in ((0.0, 0.25), (0.45, 1.0), (0.8, 0.7)):
        c = b + d * (length * t) + Vector((0.0, length * 0.25 * math.sin(t * math.pi * 0.8) - droop * t * t, 0.0))
        hw, th = width * w * 0.5, 0.1
        sections.append([tuple(c + side * hw), tuple(c + Vector((0, th, 0))), tuple(c - side * hw), tuple(c - Vector((0, th, 0)))])
    tip = b + d * length + Vector((0.0, -droop * 1.15, 0.0))
    p.loft(sections, "grass", cap_start="grass", fan_end=(tuple(tip), "grass"))


def palm(name, height, bend, fronds, seed):
    p = Part(name, outline=PROP_INK)
    rnd = random.Random(seed)
    segs = 5
    path, radii = [], []
    for i in range(segs + 1):
        t = i / segs
        path.append((bend * t * t, -0.4 + (height + 0.4) * t, 0.0))
        radii.append(0.55 - 0.22 * t)
    tube(p, path, radii, 5, lambda b, s: "bark" if b % 2 == 0 else "drywood", cap_start="bark", cap_end="bark")
    top = Vector(path[-1])
    for k in range(fronds):
        frond(p, top + Vector((0, 0.1, 0)), 2 * math.pi * k / fronds + rnd.uniform(0, 0.3), 4.6, 2.4, 1.3, seed + k)
    for k in range(2):
        a = 2 * math.pi * k / 2 + 0.6
        lump(p, (top.x + 0.45 * math.cos(a), top.y - 0.35, 0.45 * math.sin(a)), (0.38, 0.38, 0.38), seed + 50 + k, "bark",
             subdiv=1, bumps=0.05)
    return p


def build_palms():
    return [palm("TreePalm", 9.5, 1.6, 6, 40), palm("TreePalmBent", 8.0, 3.4, 6, 60)]


def build_dead_tree():
    p = Part("TreeDead", outline=PROP_INK)
    trunk(p, 3.6, 0.5, 0.32, sides=5, color="drywood", lean=(0.2, 0.0))
    for path, r in (([(0.15, 3.0, 0), (-0.9, 4.4, 0.3), (-1.6, 5.8, 0.2)], 0.24),
                    ([(0.2, 3.5, 0), (1.2, 4.6, -0.5), (1.9, 5.2, -0.9)], 0.22),
                    ([(0.2, 3.6, 0), (0.4, 5.0, 0.6), (0.2, 6.4, 0.8)], 0.2)):
        tube(p, path, [r, r * 0.75, r * 0.45], 5, "drywood", cap_end="drywood")
    return p


# ---------------------------------------------------------------------------------------------- bushes and details


def build_bushes():
    a = Part("Bush", outline=PROP_INK * 0.6)
    lump(a, (0, 0.9, 0), (1.5, 1.2, 1.4), 70, "grass", floor=-0.8)
    lump(a, (1.1, 0.7, 0.5), (1.0, 0.9, 1.0), 71, "grass", floor=-0.6)
    lump(a, (-0.9, 0.6, -0.6), (0.9, 0.8, 0.9), 72, "leaf", floor=-0.5)

    b = Part("BushDry", outline=PROP_INK * 0.6)
    lump(b, (0, 0.6, 0), (1.3, 0.8, 1.2), 75, "drywood", bumps=0.35, floor=-0.5)
    lump(b, (0.8, 0.5, -0.4), (0.8, 0.6, 0.8), 76, "drywood", bumps=0.35, floor=-0.4)
    return [a, b]


def build_grass_tuft():
    """Five chunky blades leaning out; a terrain detail, so no ink (it would never show at that size)."""
    p = Part("GrassTuft")
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        d = Vector((math.cos(a), 0, math.sin(a)))
        h = 1.1 + 0.25 * (k % 3)
        base = [tuple(d * 0.18 + Vector((math.cos(a + o) * 0.12, 0.0, math.sin(a + o) * 0.12))) for o in (0.0, 2.1, 4.2)]
        tip = d * 0.55 + Vector((0, h, 0))
        p.loft([base], "grass", cap_start="grass", fan_end=(tuple(tip), "grass"), outline=False)
    return p


def build_flowers():
    p = Part("Flowers")
    for k, (x, z, h, color) in enumerate(((0.0, 0.0, 0.9, "flower"), (0.45, 0.3, 0.7, "sun"), (-0.35, 0.35, 0.8, "white"),
                                          (0.2, -0.45, 0.65, "flower"))):
        tube(p, [(x, 0.0, z), (x, h, z)], [0.04, 0.04], 3, "grass", outline=False)
        head = yring(h, 0.22, 5, phase=k * 0.7, cx=x, cz=z)
        p.loft([head], color, cap_start=color, fan_end=((x, h + 0.12, z), color), outline=False)
    return p


# ---------------------------------------------------------------------------------------------- rocks


def build_rocks():
    rock = Part("Rock", outline=PROP_INK)
    lump(rock, (0, 1.1, 0), (2.6, 1.7, 2.0), 80, "stone", bumps=0.32, floor=-1.3)

    tall = Part("RockTall", outline=PROP_INK)
    lump(tall, (0, 2.0, 0), (1.5, 2.6, 1.4), 81, "stone", bumps=0.22, floor=-2.3)
    lump(tall, (1.3, 0.8, 0.6), (1.0, 1.0, 0.9), 82, "stone", bumps=0.2, floor=-0.9)

    flat = Part("RockFlat", outline=PROP_INK)
    lump(flat, (0, 0.6, 0), (3.2, 1.0, 2.4), 83, "stone", bumps=0.3, floor=-0.8)

    snow = Part("RockSnow", outline=PROP_INK)
    lump(snow, (0, 1.2, 0), (2.5, 1.8, 2.1), 84, snow_on_top("stone"), bumps=0.24, floor=-1.5)
    lump(snow, (-1.8, 0.7, 0.9), (1.2, 1.0, 1.1), 85, snow_on_top("stone"), bumps=0.2, floor=-0.8)

    desert = Part("RockDesert", outline=PROP_INK)
    desert_color = lambda n, mid: "sand" if n.y > 0.6 else ("clay" if mid.y < 0.9 else "sandstone")  # noqa: E731
    lump(desert, (0, 1.3, 0), (2.6, 1.8, 2.0), 86, desert_color, bumps=0.32, floor=-1.4)
    lump(desert, (1.9, 0.7, -0.9), (1.3, 1.1, 1.2), 89, desert_color, bumps=0.3, floor=-0.8)

    cliff = Part("CliffRock", outline=PROP_INK * 1.5)
    rnd = random.Random(87)
    rings = [yring(y, r, 6, 0.3, jitter=0.15, rnd=rnd) for y, r in ((-3.0, 4.2), (3.0, 3.8), (8.0, 3.0), (11.5, 2.0))]
    cliff.loft(rings, lambda b, s: "stone", cap_start="stone", cap_end="stone")
    lump(cliff, (2.6, 0.8, 1.6), (2.0, 1.8, 1.8), 88, "stone", floor=-1.6)
    return [rock, tall, flat, snow, desert, cliff]


# ---------------------------------------------------------------------------------------------- desert and beach


def build_cacti():
    big = Part("Cactus", outline=PROP_INK * 0.8)
    tube(big, [(0, -0.4, 0), (0, 2.5, 0), (0, 5.2, 0)], [0.55, 0.52, 0.5], 8, "cactus", cap_start="cactus",
         fan_end=((0, 5.7, 0), "cactus"))
    for side, y, up in ((-1, 2.0, 1.9), (1, 2.9, 1.4)):
        x = side * 1.3
        tube(big, [(0, y, 0), (x * 0.6, y, 0), (x, y + 0.4, 0), (x, y + up, 0)], [0.32, 0.32, 0.32, 0.3], 6, "cactus",
             fan_end=((x, y + up + 0.35, 0), "cactus"))

    small = Part("CactusSmall", outline=PROP_INK * 0.6)
    tube(small, [(0, -0.3, 0), (0, 1.0, 0), (0, 1.8, 0)], [0.6, 0.6, 0.55], 8, "cactus", cap_start="cactus",
         fan_end=((0, 2.15, 0), "cactus"))
    tube(small, [(0, 0.8, 0), (0.5, 0.8, 0.3), (0.75, 1.1, 0.45), (0.75, 1.6, 0.45)], [0.28] * 4, 6, "cactus",
         fan_end=((0.75, 1.85, 0.45), "cactus"))
    small.loft([yring(2.12, 0.22, 5, 0.4)], "flower", cap_start="flower", fan_end=((0, 2.32, 0), "flower"))
    return [big, small]


def build_beach():
    rocks = Part("BeachRocks", outline=PROP_INK * 0.8)
    for k, (x, z, r, h) in enumerate(((0.0, 0.0, 1.6, 1.1), (1.9, 0.8, 1.1, 0.8), (-1.5, 1.0, 0.9, 0.6), (0.6, -1.6, 0.8, 0.5))):
        lump(rocks, (x, h * 0.55, z), (r, h, r * 0.9), 90 + k, "stone", floor=-h * 0.6)

    wood = Part("Driftwood", outline=PROP_INK * 0.6)
    tube(wood, [(-2.6, 0.3, 0.2), (-0.8, 0.35, -0.1), (1.0, 0.32, 0.1), (2.6, 0.28, -0.2)], [0.36, 0.34, 0.3, 0.24], 6,
         "drywood", cap_start="drywood", cap_end="drywood")
    tube(wood, [(-0.4, 0.4, -0.1), (0.1, 0.8, -0.7), (0.4, 1.0, -1.2)], [0.16, 0.12, 0.09], 5, "drywood", cap_end="drywood")
    lump(wood, (-2.7, 0.4, 0.2), (0.6, 0.55, 0.6), 95, "drywood", bumps=0.3)
    return [rocks, wood]


# ---------------------------------------------------------------------------------------------- hero rock


def build_mesa(index, radius, height, seed):
    """
    A flat-topped butte the size of IslandSceneBuilder's mesa ``index``: a talus skirt, then cliffs in thick
    sandstone and clay strata that step in at two ledges, and a flat sand top. The outline wanders in deep bays and
    buttresses (shared by every ring, so the cliffs drop straight), and a lower shoulder bulges out on one side.
    Origin at the lowland height; the skirt sinks below it into the dunes.
    """
    p = Part(f"Mesa{index}", outline=0.6)
    rnd = random.Random(seed)
    sides = 20
    phase = rnd.uniform(0, math.pi)
    bays = [rnd.uniform(-1.0, 1.0) for _ in range(sides)]
    shoulder = rnd.randrange(sides)  # the angle the lower shoulder bulges toward

    def bulge(k, amount):
        d = min((k - shoulder) % sides, (shoulder - k) % sides)
        return amount * max(0.0, 1.0 - d / 3.5)

    H, R = height, radius
    # (height, radius, bay depth as a share of R, shoulder bulge in m)
    profile = [
        (-10.0, R + 55, 0.10, 30), (0.0, R + 46, 0.10, 30), (H * 0.10, R + 26, 0.12, 30), (H * 0.14, R + 17, 0.14, 28),
        (H * 0.40, R + 14, 0.14, 26), (H * 0.44, R + 8, 0.13, 6), (H * 0.80, R + 5, 0.12, 0), (H * 0.84, R + 1, 0.11, 0),
        (H, R - 1, 0.10, 0),
    ]
    rings = []
    for y, r, depth, bump in profile:
        ring = []
        for k in range(sides):
            rr = r + bays[k] * R * depth + bulge(k, bump) + rnd.uniform(-2.0, 2.0)
            a = phase + 2 * math.pi * k / sides
            ring.append((rr * math.cos(a), y, rr * math.sin(a)))
        rings.append(ring)
    # bands: talus, talus, ledge, lower strata, ledge, upper strata, ledge, cap rock
    bands = ["sand", "sand", "sandstone", "clay", "clay", "sandstone", "sandstone", "clay"]
    p.loft(rings, lambda b, s: bands[b], cap_end="sand")
    # A few fallen blocks round the foot.
    for k in range(6):
        a = phase + rnd.uniform(0, 2 * math.pi)
        d = R + rnd.uniform(40, 58)
        s = rnd.uniform(6, 13)
        lump(p, (d * math.cos(a), s * 0.4, d * math.sin(a)), (s, s * 0.8, s * 0.9), seed * 10 + k,
             lambda n, mid: "sand" if n.y > 0.6 else "sandstone", bumps=0.3, floor=-s * 0.6)
    return p


def build_islet():
    """
    Lighthouse Rock's base: grey cliffs from below the waterline to a grassy, flat top at y = 24 (the lighthouse stands
    in the middle), with boulders round the foot. Origin at sea level under the lighthouse.
    """
    p = Part("Islet", outline=0.35)
    rnd = random.Random(101)
    sides = 16
    bays = [rnd.uniform(-1.0, 1.0) for _ in range(sides)]
    # Steep cliffs round the heightmap's mound (IslandSceneBuilder keeps it inside): a wave-cut foot, then near-sheer rock.
    profile = [(-8.0, 90, 4), (2.0, 83, 4), (10.0, 78, 4), (18.0, 72, 3), (22.5, 67, 3), (ISLET_TOP, 64, 3)]
    rings = []
    for y, r, depth in profile:
        rings.append([((r + bays[k] * depth) * math.cos(2 * math.pi * k / sides), y, (r + bays[k] * depth) * math.sin(2 * math.pi * k / sides))
                      for k in range(sides)])
    p.loft(rings, lambda b, s: "stone" if b < 4 else "grass", cap_end="grass")
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.25, 0.25)
        d = rnd.uniform(86, 96)
        s = rnd.uniform(6, 11)
        lump(p, (d * math.cos(a), s * 0.2, d * math.sin(a)), (s, s * 0.9, s), 110 + k, "stone", floor=-s * 0.7)
    return p


# ---------------------------------------------------------------------------------------------- clouds


def build_clouds():
    """
    Unit-radius puffs (Unity scales each by its cover puff's radii). Faceted and lumpy, mostly inside the sphere so
    what you see is what hides you, with a flattened belly. No ink: from inside a cloud the hull would black out the view.
    """
    parts = []
    for k, (seed, bumps) in enumerate(((201, 0.07), (202, 0.09), (203, 0.08))):
        p = Part(f"CloudPuff{'ABC'[k]}")
        rnd = random.Random(seed)
        tmp = bmesh.new()
        bmesh.ops.create_icosphere(tmp, subdivisions=2, radius=1.0)
        pos = {}
        for v in tmp.verts:
            d = v.co.normalized()
            off = Vector((d.x, d.z, d.y)) * (0.98 + rnd.uniform(-bumps, bumps * 0.4))
            off.y = max(off.y, -0.82)
            pos[v] = off
        verts = {v: p.vert(pos[v]) for v in tmp.verts}
        faces = [p.face([verts[v] for v in f.verts], "cloud") for f in tmp.faces]
        tmp.free()
        bmesh.ops.recalc_face_normals(p.bm, faces=faces)
        p.loose_faces.extend(faces)
        parts.append(p)
    return parts


# ---------------------------------------------------------------------------------------------- build


def drop_unused_materials(obj):
    """Keeps only the material slots some face uses (a prop without ink exports with Plane_Paint alone)."""
    mesh = obj.data
    used = sorted({poly.material_index for poly in mesh.polygons})
    remap = {old: new for new, old in enumerate(used)}
    mats = [mesh.materials[i] for i in used]
    indices = [remap[poly.material_index] for poly in mesh.polygons]
    mesh.materials.clear()  # also drops every face's material index, so they're set again after
    for m in mats:
        mesh.materials.append(m)
    mesh.polygons.foreach_set("material_index", indices)


def build():
    model = Model("Nature")
    parts = (build_oaks() + build_pines() + build_palms() + [build_dead_tree()] + build_bushes()
             + [build_grass_tuft(), build_flowers()] + build_rocks() + build_cacti() + build_beach()
             + [build_mesa(i + 1, r, h, s) for i, (r, h, s) in enumerate(MESAS)] + [build_islet()] + build_clouds())
    model.add(parts)
    for obj in model.objects:
        drop_unused_materials(obj)
    model.export(FBX_PATH, BLEND_PATH)


# Preview layout: Unity positions per object (they all export at the origin).
PROPS = [
    ["TreeOak", "TreeOakTall", "TreePine", "TreePineTall", "TreePineSnow", "TreePalm", "TreePalmBent", "TreeDead"],
    ["Bush", "BushDry", "GrassTuft", "Flowers", "Cactus", "CactusSmall", "BeachRocks", "Driftwood"],
    ["Rock", "RockTall", "RockFlat", "RockSnow", "RockDesert", "CliffRock", "CloudPuffA", "CloudPuffB"],
]


def lay_out():
    for row, names in enumerate(PROPS):
        for col, name in enumerate(names):
            o = bpy.data.objects[name]
            o.location = to_blender((col * 10.0 - 35.0, 0.0, row * 14.0))
            if name.startswith("CloudPuff"):
                o.location = to_blender((col * 10.0 - 35.0, 4.0, row * 14.0))
                o.scale = (4.0, 4.0, 3.0)  # Blender axes: x, depth, height
    for i, name in enumerate(("Mesa1", "Mesa2", "Mesa3")):
        bpy.data.objects[name].location = to_blender((-450.0 + i * 420.0, 0.0, 600.0))
    bpy.data.objects["Islet"].location = to_blender((900.0, 0.0, 600.0))
    bpy.data.objects["CloudPuffC"].location = to_blender((0.0, 330.0, 600.0))
    bpy.data.objects["CloudPuffC"].scale = (120.0, 100.0, 60.0)


SHOTS = [
    ("nature_props", (0.0, 60.0, -75.0), (0.0, 3.0, 12.0), None),
    ("nature_props_top", (0.0, 120.0, 12.0), (0.0, 0.0, 12.01), 90.0),
    ("nature_hero", (220.0, 520.0, -1100.0), (220.0, 60.0, 600.0), None),
]


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
