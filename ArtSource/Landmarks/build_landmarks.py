"""
Builds the landmarks (Design/03-Art/final-asset-list.md, Pack 6: one hero model per region), then exports them for Unity.

    blender --background --factory-startup --python ArtSource/Landmarks/build_landmarks.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Landmarks/Landmarks.fbx  one object per asset, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png       the shared palette atlas (rewritten, same as the planes')
    ArtSource/Landmarks/Landmarks.blend                 the generated scene, for hand tweaks and renders

Each landmark stands on its origin (IslandSceneBuilder puts that on the ground) at the greybox's size, so the island's
lanes, clouds and sight lines still fit. Each has a ``<Name>Col`` object, its plain masses, for the mesh collider:
    Spire                the city's ~545 m glass needle: slate podium, four glass stages with concrete collars, an
                         observation saucer above the first, a gold crown and a red-and-white needle
    SummitRadar          concrete pad sunk into the peak, a geodesic radome, an ops hut and a 140 m red-and-white
                         lattice mast; SummitRadarAntenna turns on top of it (origin on its pivot, the mast's axis)
    Pyramid              8 talud-tablero steps, 280 m across, stairs up all four faces, a shrine with a gold capstone
                         (it reaches 12 m below its origin, so no edge floats on the dunes)
    Lighthouse           red-and-white tower on a stone plinth, gallery, open lantern, keeper's cottage; stands on the
                         nature pack's Islet. LighthouseLamp (glow, origin on the lamp) is its beacon;
                         LighthouseLens (brass frame with two bullseyes, same origin) turns round it
    Beacon               the shared red beacon gem, 1 m across round its origin: Unity scales one onto each landmark's
                         top and Landmark blinks it
Markers (empties): SpireTop, SummitRadarTop, PyramidTop, where each beacon sits (Landmark.TopPosition).

Everything is on the shared plane materials (Plane_Paint, Plane_Ink, Weapon_Glow) and palette.
"""
import math
import os
import sys

from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Nature"))
sys.path.insert(0, os.path.join(HERE, "..", "City"))
sys.path.insert(0, os.path.join(HERE, "..", "Airfield"))
from plane_kit import MODELS_DIR, Model, Part, preview_arg, render_previews, to_blender  # noqa: E402
from build_nature import drop_unused_materials, lump  # noqa: E402
from build_city import at, beam, block, cylinder, facets, ngon_pts, prism, quad, rect_pts, wall_quad, windows  # noqa: E402
from build_airfield import glow_material  # noqa: E402

FBX_PATH = os.path.join(MODELS_DIR, "Landmarks", "Landmarks.fbx")
BLEND_PATH = os.path.join(HERE, "Landmarks.blend")

# Island numbers these are built to (IslandSceneBuilder's greybox landmarks).
SPIRE_STAGES = [(44.0, 12.0, 232.0), (34.0, 240.0, 352.0), (24.0, 352.0, 432.0), (14.0, 432.0, 472.0)]  # width, y0, y1
SPIRE_TOP = (0.0, 545.0, 0.0)
MAST_X, MAST_Z = 6.0, -6.0
RADAR_TOP = (MAST_X, 156.0, MAST_Z)
ANTENNA = (MAST_X, 145.0, MAST_Z)
RADOME, RADOME_R = (-10.0, 14.0, 6.0), 12.0
PYRAMID_BASE, STEP_H, STEPS, PYRAMID_SINK = 280.0, 26.0, 8, 12.0
PYRAMID_TOP = (0.0, 229.0, 0.0)
LAMP = (0.0, 68.0, 0.0)  # the lighthouse lamp's middle, 92 m above the sea on the islet


def tower_r(y):
    """The lighthouse tower's radius at height ``y``."""
    return 6.5 - (y - 1.5) / 60.5 * 2.2


# ---------------------------------------------------------------------------------------------- the Spire


def spire():
    p = Part("Spire", outline=0.45)
    col = Part("SpireCol")

    # Podium: slate with a white cornice and a gold band, a glazed lobby under a canopy in each side.
    pod = rect_pts(70.0, 70.0, 8.0)
    prism(p, pod, -1.5, 10.5, "slate", top="concrete")
    prism(p, rect_pts(74.0, 74.0, 9.0), 10.5, 12.0, "white", outline=0.25)
    for f in facets(pod, 10.0):
        wall_quad(p, f, -15.0, 15.0, 0.0, 7.0, "glass", off=0.3)
        wall_quad(p, f, -f[3], f[3], 9.0, 9.9, "sun", off=0.3)
        (mx, mz), (nx, nz), (tx, tz), _ = f
        cx, cz = mx + nx * 2.5, mz + nz * 2.5
        block(p, (cx, 7.6, cz), (abs(tx) * 34.0 + abs(nx) * 5.0, 0.8, abs(tz) * 34.0 + abs(nz) * 5.0), "white", c=0.4,
              ink=0.1)
    prism(col, rect_pts(74.0, 74.0, 9.0), -1.5, 12.0, "metal", outline=False)

    # Four glass stages: blue glass on the faces, white fins on the cut corners and white spandrels every three
    # storeys (so it stands off the sky), a collar with a gold band on top of each (the first carries the deck).
    for i, (w, y0, y1) in enumerate(SPIRE_STAGES):
        poly = rect_pts(w, w, w * 0.22)
        p.loft([at(poly, y0), at(poly, y1)], lambda b, s: "window" if s % 2 == 0 else "white", cap_end="concrete",
               outline=0.35)
        prism(col, poly, y0, y1, "metal", outline=False)
        for f in facets(poly, w * 0.2):
            half = f[3] - 0.6
            y = y0 + 6.0
            while y < y1 - 8.0:
                wall_quad(p, f, -half, half, y, y + 2.4, "white", off=0.25)
                y += 12.0
        if i == 0:
            continue
        collar = rect_pts(w + 4.0, w + 4.0, (w + 4.0) * 0.22)
        prism(p, collar, y1 - 4.0, y1, "white", outline=0.25)
        for f in facets(collar):
            wall_quad(p, f, -f[3], f[3], y1 - 2.7, y1 - 1.5, "sun", off=0.15)

    # Observation deck: a 16-sided saucer round the top of the first stage, glazed all round.
    deck = [(20.0, 225.0), (31.0, 232.0), (31.0, 238.5), (24.0, 242.5)]
    p.loft([at(ngon_pts(r, 16), y) for r, y in deck], lambda b, s: ("white", "glass", "white")[b],
           cap_end="concrete", outline=0.35)
    for y in (231.6, 238.5):  # gold rims above and below the glazing
        p.loft([at(ngon_pts(31.25, 16), y), at(ngon_pts(31.25, 16), y + 0.9)], "sun", outline=False)
    col.loft([at(ngon_pts(r, 12), y) for r, y in deck], "metal", cap_start="metal", cap_end="metal", outline=False)

    # Crown: two gold steps, then the needle in red and white bands, a ring platform and the beacon's housing.
    prism(p, rect_pts(17.0, 17.0, 3.7), 472.0, 476.0, "sun", outline=0.2)
    prism(p, rect_pts(11.0, 11.0, 2.4), 476.0, 479.0, "sun", outline=0.2)
    needle = [479.0 + k * 6.0 for k in range(11)]  # 479 .. 539

    def needle_r(y):
        return 3.0 - (y - 479.0) / 60.0 * 1.9

    p.loft([at(ngon_pts(needle_r(y), 8), y) for y in needle], lambda b, s: "red" if b % 2 == 0 else "white",
           cap_end="darkmetal", outline=0.15)
    prism(p, ngon_pts(4.6, 8), 503.5, 504.7, "darkmetal", outline=0.1)
    cylinder(p, 0.0, 0.0, 539.0, 541.0, 1.4, "darkmetal", sides=8, ink=0.08)
    col.loft([at(ngon_pts(r, 8), y) for r, y in ((8.5, 472.0), (3.0, 479.0), (1.4, 541.0))], "metal",
             cap_start="metal", cap_end="metal", outline=False)
    return [p, col]


# ---------------------------------------------------------------------------------------------- Summit Radar


def mast_corner(k, y, y0=2.0, y1=140.0, half0=5.0, half1=1.6):
    t = (y - y0) / (y1 - y0)
    h = half0 + (half1 - half0) * t
    sx, sz = ((-1, -1), (1, -1), (1, 1), (-1, 1))[k]
    return (MAST_X + sx * h, y, MAST_Z + sz * h)


def summit_radar():
    p = Part("SummitRadar", outline=0.3)
    col = Part("SummitRadarCol")

    # Concrete pad sunk into the peak, a yellow kerb round its edge.
    pad = rect_pts(46.0, 46.0, 6.0)
    prism(p, pad, -28.0, 2.0, "concrete")
    for f in facets(pad):
        wall_quad(p, f, -f[3], f[3], 0.6, 1.8, "sun", off=0.2)
    prism(col, pad, -28.0, 2.0, "metal", outline=False)

    # Radome: a geodesic ball on a short drum.
    rx, ry, rz = RADOME
    cylinder(p, rx, rz, 2.0, 7.0, 10.4, "concrete", sides=12, top="concrete", ink=0.2)
    lump(p, (rx, ry + 1.0, rz), (RADOME_R, RADOME_R, RADOME_R), 7, "white", subdiv=2, bumps=0.0, floor=-7.0)
    lump(col, (rx, ry + 1.0, rz), (RADOME_R, RADOME_R, RADOME_R), 7, "metal", subdiv=1, bumps=0.0, floor=-7.0,
         outline=False)
    prism(col, ngon_pts(10.4, 8, rx, rz), 2.0, 8.0, "metal", outline=False)

    # Ops hut: white walls, ribbon windows, a door toward the mast, AC unit and a whip antenna on the roof.
    hut = rect_pts(12.0, 14.0, 0.8, 15.0, 13.0)
    prism(p, hut, 2.0, 8.0, "white", top="metal", outline=0.15)
    windows(p, hut, 2.0, 8.0, "window", floor=6.0, frac=0.35, cols=2, margin=1.4, gap=0.3, min_half=3.0)
    quad(p, [(13.8, 2.0, 5.95), (16.2, 2.0, 5.95), (16.2, 5.2, 5.95), (13.8, 5.2, 5.95)], "darkmetal", (0, 0, -1))
    block(p, (17.5, 8.8, 16.0), (3.2, 1.6, 2.2), "metal", top="concrete")
    beam(p, (11.0, 8.0, 18.0), (11.0, 15.0, 18.0), 0.1, "metal", ink=0.03)
    prism(col, hut, 2.0, 8.0, "metal", outline=False)

    # The mast: a tapering 4-leg lattice in red and white bands (aviation marking), a rest platform halfway, a top
    # platform carrying the turning antenna's pedestal, and the beacon's pole through its hub.
    levels = 12
    ys = [2.0 + (140.0 - 2.0) * i / levels for i in range(levels + 1)]
    for i, (ya, yb) in enumerate(zip(ys, ys[1:])):
        c = "red" if i % 2 else "white"
        for k in range(4):
            beam(p, mast_corner(k, ya), mast_corner(k, yb), 0.55, c, ink=0.08)
            beam(p, mast_corner(k, yb), mast_corner((k + 1) % 4, yb), 0.28, c)
            beam(p, mast_corner(k, ya), mast_corner((k + 1) % 4, yb), 0.22, c)
    for y, size in ((71.0, 8.6), (140.0, 7.0)):
        block(p, (MAST_X, y, MAST_Z), (size, 0.8, size), "darkmetal", top="metal", c=0.4, ink=0.08)
    cylinder(p, MAST_X, MAST_Z, 140.4, 143.6, 1.3, "darkmetal", sides=8, ink=0.06)
    cylinder(p, MAST_X, MAST_Z, 143.6, 151.0, 0.45, "white", sides=6, ink=0.05)
    cylinder(p, MAST_X, MAST_Z, 151.0, 152.6, 1.1, "darkmetal", sides=8, ink=0.06)
    col.loft([[mast_corner(k, y) for k in range(4)] for y in (2.0, 140.0)]
             + [[mast_corner(k, 152.6, y1=140.0) for k in range(4)]], "metal", cap_start="metal", cap_end="metal",
             outline=False)

    ant = Part("SummitRadarAntenna", ANTENNA, outline=0.08)
    ax, ay, az = ANTENNA
    cylinder(ant, ax, az, ay - 1.2, ay + 0.8, 1.4, "darkmetal", sides=8, ink=0.06)  # hub, round the beacon pole
    block(ant, (ax, ay, az + 1.8), (1.2, 1.2, 2.6), "darkmetal", c=0.15, ink=0.05)  # arm out to the reflector
    sections = []
    for k in range(11):  # 20 m wide, 4 m tall, curved side to side and tipped back
        x = -10.0 + k * 2.0
        bow = 0.035 * x * x
        sections.append([(ax + x, ay + y, az + 3.0 + bow - y * 0.2 + t)
                         for y, t in ((-2.0, 0.0), (2.0, 0.0), (2.0, -0.25), (-2.0, -0.25))])
    ant.loft(sections, lambda b, s: "metal" if s == 2 else "white", cap_start="white", cap_end="white")
    beam(ant, (ax, ay - 0.2, az + 3.0), (ax, ay - 0.6, az + 6.5), 0.12, "darkmetal")  # feed horn
    block(ant, (ax, ay - 0.6, az + 6.7), (0.6, 0.6, 0.7), "red", c=0.08, ink=0.04)
    return [p, col, ant]


# ---------------------------------------------------------------------------------------------- the Pyramid

FACES = ((0.0, -1.0), (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0))  # outward normals, x/z


def on_face(k, d, y, a):
    """A point on face ``k``: ``d`` out from the middle, ``y`` up, ``a`` along the face."""
    nx, nz = FACES[k]
    tx, tz = -nz, nx
    return (nx * d + tx * a, y, nz * d + tz * a)


STAIR_HALF = 13.0


def stair_d(y):
    """How far out the stairs' slope is at height ``y``: just outside every step's top edge."""
    return 140.0 + (STEP_H - y) * (122.5 / 182.0)


def pyramid():
    p = Part("Pyramid", outline=0.45)
    col = Part("PyramidCol")

    # Eight talud-tablero steps: a sloping skirt, a shadowed lip, an upright panel face with carved panels.
    for i in range(STEPS):
        w = PYRAMID_BASE * (1.0 - i / STEPS)
        y0 = i * STEP_H
        ya = y0 + STEP_H * 0.4
        y1 = y0 + STEP_H
        c = 2.5
        rings = [at(rect_pts(w, w, c), y0), at(rect_pts(w - 5.0, w - 5.0, c), ya),
                 at(rect_pts(w - 2.0, w - 2.0, c), ya + 0.01), at(rect_pts(w - 2.0, w - 2.0, c), y1)]
        if i == 0:
            rings.insert(0, at(rect_pts(w, w, c), -PYRAMID_SINK))
        first = 1 if i == 0 else 0
        p.loft(rings, lambda b, s: "sandstone" if b == first + 1 else "limestone", cap_end="sand", outline=0.4)
        prism(col, rect_pts(w - 2.0, w - 2.0, c), -PYRAMID_SINK if i == 0 else y0, y1, "metal", outline=False)

        # Tablero: a clay frieze along the top, carved panels below it, left clear behind the stairs.
        face = rect_pts(w - 2.0, w - 2.0, c)
        for f in facets(face, 10.0):
            half = f[3] - 1.0
            wall_quad(p, f, -half, half, y1 - 3.6, y1 - 1.2, "clay", off=0.25)
            n = max(1, int((half - STAIR_HALF - 3.0) / 11.0))
            for side in (-1.0, 1.0):
                for j in range(n):
                    a = side * (STAIR_HALF + 4.0 + j * 11.0)
                    a0, a1 = sorted((a, a + side * 7.0))
                    wall_quad(p, f, a0, a1, ya + 1.2, y1 - 5.0, "sandstone", off=0.25)
                    wall_quad(p, f, a0 + 1.6, a1 - 1.6, ya + 2.8, y1 - 6.6, "clay", off=0.4)

    # Stairs up the middle of every face, between serpent balustrades, treads drawn in on the slope.
    top_y = STEPS * STEP_H
    profile = [(stair_d(-4.0), -4.0), (stair_d(top_y), top_y), (10.0, top_y - 8.0), (110.0, -4.0)]
    slope = Vector((profile[1][0] - profile[0][0], profile[1][1] - profile[0][1]))
    normal = Vector((slope.y, -slope.x)).normalized()
    if normal.x < 0:
        normal = -normal
    treads = 40
    for k in range(4):
        rings = [[on_face(k, d, y, a) for d, y in profile] for a in (-STAIR_HALF, STAIR_HALF)]
        p.loft(rings, "sandstone", cap_start="limestone", cap_end="limestone", outline=0.3)
        col.loft(rings, "metal", cap_start="metal", cap_end="metal", outline=False)
        for t in range(treads):
            y = t * top_y / treads + 1.0
            ya, yb = y, y + top_y / treads * 0.35
            da, db = stair_d(ya) + normal.x * 0.3, stair_d(yb) + normal.x * 0.3
            ya += normal.y * 0.3
            yb += normal.y * 0.3
            quad(p, [on_face(k, da, ya, -STAIR_HALF + 0.8), on_face(k, da, ya, STAIR_HALF - 0.8),
                     on_face(k, db, yb, STAIR_HALF - 0.8), on_face(k, db, yb, -STAIR_HALF + 0.8)], "clay",
                 (FACES[k][0] * normal.x, normal.y, FACES[k][1] * normal.x))
        for s in (-1.0, 1.0):
            a = s * (STAIR_HALF + 1.4)
            beam(p, on_face(k, stair_d(-2.0) + 1.4, -2.0 + 1.2, a), on_face(k, stair_d(top_y) + 1.4, top_y + 1.2, a),
                 1.6, "limestone", ink=0.2)
            head = on_face(k, stair_d(0.0) + 2.0, 2.2, a)
            block(p, head, (5.0, 4.4, 5.0), "clay", top="sandstone", c=0.8, ink=0.2)  # serpent heads at the foot
            nx, nz = FACES[k]
            quad(p, [(head[0] + nx * 2.55 + nz * 1.4 - 0.0, 2.6, head[2] + nz * 2.55 - nx * 1.4),
                     (head[0] + nx * 2.55 - nz * 1.4, 2.6, head[2] + nz * 2.55 + nx * 1.4),
                     (head[0] + nx * 2.55 - nz * 1.4, 3.4, head[2] + nz * 2.55 + nx * 1.4),
                     (head[0] + nx * 2.55 + nz * 1.4, 3.4, head[2] + nz * 2.55 - nx * 1.4)], "char", (nx, 0, nz))

    # Shrine on the top: limestone walls with a dark doorway each way, a sandstone roof, a clay crest and a gold
    # capstone under the beacon.
    walls = rect_pts(20.0, 20.0, 1.2)
    prism(p, walls, top_y - 0.5, top_y + 7.5, "limestone", outline=0.25)
    for f in facets(walls, 5.0):
        wall_quad(p, f, -2.4, 2.4, top_y, top_y + 5.4, "char", off=0.2)
        wall_quad(p, f, -f[3], f[3], top_y + 6.0, top_y + 7.0, "clay", off=0.2)
    prism(p, rect_pts(24.0, 24.0, 1.6), top_y + 7.5, top_y + 9.0, "sandstone", outline=0.25)
    prism(p, rect_pts(15.0, 15.0, 1.0), top_y + 9.0, top_y + 10.8, "clay", outline=0.2)
    p.loft([at(rect_pts(11.0, 11.0, 0.6), top_y + 10.8), at(rect_pts(10.0, 10.0, 0.5), top_y + 11.6)], "sun",
           fan_end=((0.0, top_y + 17.0, 0.0), "sun"), outline=0.2)
    prism(col, rect_pts(24.0, 24.0, 1.6), top_y - 0.5, top_y + 9.0, "metal", outline=False)
    col.loft([at(rect_pts(15.0, 15.0, 1.0), top_y + 9.0), at(rect_pts(11.0, 11.0, 0.6), top_y + 10.8)], "metal",
             cap_start="metal", fan_end=((0.0, top_y + 17.0, 0.0), "metal"), outline=False)
    return [p, col]


# ---------------------------------------------------------------------------------------------- Lighthouse Rock


def tower_decal(p, angle, y0, y1, half, color):
    """A decal on the round tower facing ``angle`` (radians round y, 0 = +x), just off its 16 sides."""
    r = tower_r((y0 + y1) * 0.5) * math.cos(math.pi / 16) + 0.12
    c, s = math.cos(angle), math.sin(angle)
    tx, tz = -s, c
    pts = [(c * r - tx * half, y0, s * r - tz * half), (c * r + tx * half, y0, s * r + tz * half),
           (c * r + tx * half, y1, s * r + tz * half), (c * r - tx * half, y1, s * r - tz * half)]
    return quad(p, pts, color, (c, 0.0, s))


def lighthouse():
    p = Part("Lighthouse", outline=0.15)
    col = Part("LighthouseCol")

    # Stone plinth, then the tower: four 15 m bands, white at the foot, tapering to the gallery.
    prism(p, ngon_pts(10.0, 8), -3.0, 1.5, "stone", top="concrete", outline=0.15)
    ys = [1.5, 16.5, 31.5, 46.5, 61.5]
    p.loft([at(ngon_pts(tower_r(y), 16), y) for y in ys], lambda b, s: "white" if b % 2 == 0 else "red", smooth=True)
    tower_decal(p, 0.0, 1.5, 5.5, 1.1, "darkmetal")  # door, toward the cottage
    for k, y in enumerate((10.0, 22.0, 37.0, 52.0)):
        tower_decal(p, math.pi * 0.5 + k * 2.4, y, y + 2.2, 0.55, "window")
    prism(col, ngon_pts(10.0, 8), -3.0, 1.5, "metal", outline=False)
    col.loft([at(ngon_pts(tower_r(y), 8), y) for y in (1.5, 61.5)], "metal", cap_start="metal", cap_end="metal",
             outline=False)

    # Gallery on corbels, with a rail of posts.
    p.loft([at(ngon_pts(r, 16), y) for r, y in ((4.3, 60.5), (6.7, 63.0), (6.7, 64.0))],
           lambda b, s: "white" if b == 0 else "darkmetal", cap_end="darkmetal", outline=0.12)
    for k in range(16):
        a0, a1 = 2 * math.pi * k / 16, 2 * math.pi * (k + 1) / 16
        beam(p, (6.4 * math.cos(a0), 64.0, 6.4 * math.sin(a0)), (6.4 * math.cos(a0), 65.6, 6.4 * math.sin(a0)), 0.09,
             "darkmetal")
        beam(p, (6.4 * math.cos(a0), 65.6, 6.4 * math.sin(a0)), (6.4 * math.cos(a1), 65.6, 6.4 * math.sin(a1)), 0.1,
             "darkmetal")
    prism(col, ngon_pts(6.7, 8), 60.5, 64.0, "metal", outline=False)

    # Lantern: a low wall, open glazing bars round the lamp, a red domed roof with a vent ball and a lightning rod.
    prism(p, ngon_pts(3.8, 8), 64.0, 65.6, "red", top="darkmetal", outline=0.1)
    prism(p, ngon_pts(3.75, 8), 70.6, 71.0, "darkmetal", outline=0.08)
    for x, z in ngon_pts(3.6, 8):
        beam(p, (x, 65.6, z), (x, 70.6, z), 0.14, "darkmetal", ink=0.04)
    p.loft([at(ngon_pts(r, 8), y) for r, y in ((4.3, 71.0), (3.4, 72.4), (2.0, 74.0))], "red", cap_start="darkmetal",
           fan_end=((0.0, 75.2, 0.0), "red"), outline=0.12)
    lump(p, (0.0, 75.8, 0.0), (0.7, 0.7, 0.7), 3, "darkmetal", subdiv=1, bumps=0.0)
    beam(p, (0.0, 76.3, 0.0), (0.0, 79.0, 0.0), 0.08, "darkmetal")
    col.loft([at(ngon_pts(r, 8), y) for r, y in ((3.8, 64.0), (3.8, 71.0), (2.0, 74.0))], "metal", cap_start="metal",
             fan_end=((0.0, 75.2, 0.0), "metal"), outline=False)

    # Keeper's cottage beside it: white walls, a red gable roof, a brick chimney.
    gable = [(-4.5, -0.5), (4.5, -0.5), (4.5, 4.2), (0.0, 7.2), (-4.5, 4.2)]  # (z, y)
    rings = [[(x, y, z + 6.0) for z, y in gable] for x in (10.0, 22.0)]
    p.loft(rings, "white", cap_start="white", cap_end="white", outline=0.1)
    col.loft(rings, "metal", cap_start="metal", cap_end="metal", outline=False)
    roof = [(-5.4, 3.7), (0.0, 7.6), (5.4, 3.7), (5.4, 4.3), (0.0, 8.2), (-5.4, 4.3)]
    p.loft([[(x, y, z + 6.0) for z, y in roof] for x in (9.4, 22.6)], "red", cap_start="red", cap_end="red",
           outline=0.08)
    block(p, (19.5, 8.0, 4.0), (1.2, 3.0, 1.2), "brick", top="char", c=0.1, ink=0.05)
    quad(p, [(9.95, -0.5, 5.0), (9.95, -0.5, 7.0), (9.95, 3.0, 7.0), (9.95, 3.0, 5.0)], "darkmetal", (-1, 0, 0))
    for x in (13.0, 19.0):
        for s in (-1.0, 1.0):
            z = 6.0 + s * 4.55
            quad(p, [(x - 0.9, 1.3, z), (x + 0.9, 1.3, z), (x + 0.9, 3.0, z), (x - 0.9, 3.0, z)], "window", (0, 0, s))
    block(p, (-9.0, 1.2, 13.0), (5.0, 3.0, 4.0), "white", top="red", c=0.3, ink=0.06)  # oil store

    lamp = Part("LighthouseLamp", LAMP)
    lx, ly, lz = LAMP
    lamp.loft([at(ngon_pts(r, 8, lx, lz), ly + dy) for r, dy in ((1.3, -2.1), (2.1, -1.1), (2.1, 1.1), (1.3, 2.1))],
              "flame", cap_start="flame", cap_end="flame", glow=True)

    lens = Part("LighthouseLens", LAMP, outline=0.05)
    cylinder(lens, lx, lz, ly - 2.4, ly - 2.2, 2.9, "sun", sides=8, ink=0.04)  # brass ring under the panels
    cylinder(lens, lx, lz, 64.0, ly - 2.4, 0.35, "sun", sides=6, ink=0.03)  # spindle
    for k in range(4):  # two glowing bullseyes back to back with two brass shutters, so the light sweeps as it turns
        a = math.pi * 0.5 * k
        c, s = math.cos(a), math.sin(a)
        centre = (lx + c * 2.7, ly, lz + s * 2.7)
        if k % 2 == 0:
            lens.disc(centre, (c, 0.0, s), 1.6, 8, "glint", glow=True)
            lens.disc((lx + c * 2.5, ly, lz + s * 2.5), (-c, 0.0, -s), 1.6, 8, "sun")
        else:
            tx, tz = -s, c
            lens.loft([[(centre[0] + tx * u + c * v, ly + w, centre[2] + tz * u + s * v)
                        for u, w, v in ((-1.4, -1.8, 0.0), (1.4, -1.8, 0.0), (1.4, 1.8, 0.0), (-1.4, 1.8, 0.0))],
                       [(centre[0] + tx * u - c * 0.15, ly + w, centre[2] + tz * u - s * 0.15)
                        for u, w in ((-1.4, -1.8), (1.4, -1.8), (1.4, 1.8), (-1.4, 1.8))]],
                      "sun", cap_start="sun", cap_end="sun")
    return [p, col, lamp, lens]


# ---------------------------------------------------------------------------------------------- beacon


def beacon():
    """The shared beacon: a red faceted gem 1 m across, unlit so it reads at range. Unity scales it per landmark."""
    p = Part("Beacon")
    p.loft([at(ngon_pts(r, 8), y) for r, y in ((0.28, -0.46), (0.5, -0.16), (0.5, 0.16), (0.28, 0.46))], "red",
           cap_start="red", cap_end="red", glow=True)
    return p


# ---------------------------------------------------------------------------------------------- build


def build():
    model = Model("Landmarks")
    materials = model.materials + [glow_material()]
    parts = spire() + summit_radar() + pyramid() + lighthouse() + [beacon()]
    for part in parts:
        obj = part.finish(model.collection, materials)
        drop_unused_materials(obj)
        model.objects.append(obj)
    model.mark("SpireTop", SPIRE_TOP)
    model.mark("SummitRadarTop", RADAR_TOP)
    model.mark("PyramidTop", PYRAMID_TOP)
    model.export(FBX_PATH, BLEND_PATH)


# ---------------------------------------------------------------------------------------------- previews


LAYOUT = {"Spire": (0.0, 0.0, 0.0), "SummitRadar": (200.0, 0.0, 0.0), "Pyramid": (500.0, 0.0, 0.0),
          "Lighthouse": (200.0, 0.0, 200.0)}


def lay_out():
    import bpy
    for o in [o for o in bpy.data.objects if o.name.endswith("Col")]:
        bpy.data.objects.remove(o)
    for name, pos in LAYOUT.items():
        bpy.data.objects[name].location = to_blender(pos)
    for name, (parent, pivot) in {"SummitRadarAntenna": ("SummitRadar", ANTENNA), "LighthouseLamp": ("Lighthouse", LAMP),
                                  "LighthouseLens": ("Lighthouse", LAMP)}.items():
        base = LAYOUT[parent]
        bpy.data.objects[name].location = to_blender(tuple(a + b for a, b in zip(base, pivot)))
    bpy.data.objects["SummitRadarAntenna"].rotation_euler = (0.0, 0.0, math.radians(30.0))
    beacon_obj = bpy.data.objects["Beacon"]
    beacon_obj.location = to_blender(SPIRE_TOP)
    beacon_obj.scale = (9.0, 9.0, 9.0)


SHOTS = [
    ("landmarks_overview", (250.0, 420.0, -900.0), (250.0, 200.0, 0.0), None),
    ("landmarks_spire_top", (60.0, 520.0, -110.0), (0.0, 480.0, 0.0), None),
    ("landmarks_spire_deck", (90.0, 250.0, -150.0), (0.0, 240.0, 0.0), None),
    ("landmarks_spire_base", (90.0, 40.0, -140.0), (0.0, 15.0, 0.0), None),
    ("landmarks_radar", (260.0, 90.0, -150.0), (200.0, 70.0, 0.0), None),
    ("landmarks_radar_top", (225.0, 150.0, -40.0), (206.0, 142.0, -6.0), None),
    ("landmarks_pyramid", (750.0, 260.0, -350.0), (500.0, 90.0, 0.0), None),
    ("landmarks_pyramid_top", (560.0, 240.0, -70.0), (500.0, 210.0, 0.0), None),
    ("landmarks_lighthouse", (250.0, 60.0, 130.0), (205.0, 38.0, 200.0), None),
    ("landmarks_lantern", (214.0, 72.0, 186.0), (200.0, 68.0, 200.0), None),
]


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
