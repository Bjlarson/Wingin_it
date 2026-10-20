"""
Builds the city pack (Design/03-Art/final-asset-list.md, Pack 5) from scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/City/build_city.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/City/City.fbx         one object per asset, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png    the shared palette atlas (rewritten, same as the planes')
    ArtSource/City/City.blend                        the generated scene, for hand tweaks and renders

Buildings stand on their origin (the middle of the footprint at street level), y up, in metres. IslandSceneBuilder
keeps its lot layout (sizes and heights) and fits one of these to each lot: the same scale across x and z so the plan
keeps its shape, and y stretched to the lot's height. Each building has a matching ``<Name>Col`` object, just its
solid masses (no ink, rooftop clutter or antennas), for the mesh collider:
    TowerGlass, TowerDeco, TowerRound, TowerBlue, TowerSlab      downtown, 160-250 m
    OfficeBands, OfficeGlass, Hotel                              mid-rise, 70-130 m
    BrickBlock, Apartments, Shops, Garage                        low-rise, 17-48 m
Rooftop clutter (AC units, water tanks, penthouses, antennas, helipads, billboards, a roof garden) is baked into each.

Street kit, laid on the city's flat ground (no ink, like terrain; the props on them have their own):
    CityBlock      90 x 90 m sidewalk under a block of buildings, street lamps and traffic lights at its edges
    Road           a 30 m wide, 90 m long street between two crossings, along z
    Crossing       30 x 30 m intersection with zebra crossings
    Plaza          90 x 90 m square with lawns and a fountain (Unity adds the trees)
    Quay           20 m of canal wall, face at x = 0 looking down -x, 16 m deep (origin at the top of the wall,
                   y = 0 = street level, water 6 m down), a 12 m promenade, bollards and a lamp
    QuayShort      7 m of the same, for the canal's round basin
Bridge (origin at the canal's middle at sea level, deck along x): a cable-stayed bridge, deck 34 m above the water,
230 m long, H-frame towers 150 m apart, plus BridgeCol. Vehicles (no colliders, nose +z, toy-sized like the trees):
    CarSun, CarWhite, CarTeal, Bus                on the roads
    Tugboat, Ferry                                in the canal, origin at the waterline

Everything is on the shared plane materials (Plane_Paint, Plane_Ink) and palette.
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Nature"))
from plane_kit import MODELS_DIR, Model, Part, preview_arg, render_previews, to_blender  # noqa: E402
from build_nature import drop_unused_materials, lump  # noqa: E402

FBX_PATH = os.path.join(MODELS_DIR, "City", "City.fbx")
BLEND_PATH = os.path.join(HERE, "City.blend")

# Island numbers these are built to (IslandSceneBuilder).
DECK_Y, SPAN, TOWER_X = 34.0, 230.0, 75.0  # bridge: deck height over the water, length, towers either side
QUAY_DEPTH = 16.0  # street level (LandHeight 6) down to the canal floor (-10)
PROMENADE = 12.0

SINK = -1.5  # buildings go this far below the street so no edge floats on a bump
OFF = 0.2  # windows and markings float this far off their wall or floor
PROP_INK = 0.1  # rooftop boxes, lamps, cars
FLOOR = 7.0  # toy-sized storeys: real ones are ~3.5 m, which turns to noise from the air


# ---------------------------------------------------------------------------------------------- shapes


def rect_pts(w, d, c, cx=0.0, cz=0.0):
    """A w (x) by d (z) rectangle with its corners cut by ``c``: 8 points round the vertical axis."""
    hw, hd = w * 0.5, d * 0.5
    pts = [(hw, -hd + c), (hw, hd - c), (hw - c, hd), (-hw + c, hd), (-hw, hd - c), (-hw, -hd + c), (-hw + c, -hd),
           (hw - c, -hd)]
    return [(cx + x, cz + z) for x, z in pts]


def ngon_pts(r, n, cx=0.0, cz=0.0, phase=None):
    phase = math.pi / n if phase is None else phase
    return [(cx + r * math.cos(phase + 2 * math.pi * k / n), cz + r * math.sin(phase + 2 * math.pi * k / n)) for k in range(n)]


def at(poly, y):
    return [(x, y, z) for x, z in poly]


def facets(poly, min_half=0.0):
    """The polygon's sides as (middle, outward normal, direction along it, half length), skipping short ones (chamfers)."""
    cx = sum(p[0] for p in poly) / len(poly)
    cz = sum(p[1] for p in poly) / len(poly)
    out = []
    for (ax, az), (bx, bz) in zip(poly, poly[1:] + poly[:1]):
        length = math.hypot(bx - ax, bz - az)
        if length * 0.5 < min_half:
            continue
        mx, mz = (ax + bx) * 0.5, (az + bz) * 0.5
        tx, tz = (bx - ax) / length, (bz - az) / length
        nx, nz = tz, -tx
        if nx * (mx - cx) + nz * (mz - cz) < 0:
            nx, nz = -nx, -nz
        out.append(((mx, mz), (nx, nz), (tx, tz), length * 0.5))
    return out


def quad(p, pts, color, normal):
    """A one-sided decal facing ``normal`` (Unity space), no outline."""
    f = p.face([p.vert(q) for q in pts], color)
    f.normal_update()
    if f.normal.dot(to_blender(Vector(normal))) < 0:
        f.normal_flip()
    p.loose_faces.append(f)
    return f


def wall_quad(p, facet, a0, a1, ya, yb, color, off=OFF):
    """A decal on one side of a mass: from ``a0`` to ``a1`` along it (0 = its middle), ``ya`` to ``yb`` up."""
    (mx, mz), (nx, nz), (tx, tz), _ = facet
    bx, bz = mx + nx * off, mz + nz * off
    pts = [(bx + tx * a0, ya, bz + tz * a0), (bx + tx * a1, ya, bz + tz * a1), (bx + tx * a1, yb, bz + tz * a1),
           (bx + tx * a0, yb, bz + tz * a0)]
    return quad(p, pts, color, (nx, 0.0, nz))


def flat(p, poly, y, color, layer=0):
    """A decal lying on a floor or roof at height ``y``, facing up; a higher ``layer`` sits on top of a lower one."""
    return quad(p, at(poly, y + OFF * (0.5 + layer)), color, (0.0, 1.0, 0.0))


def windows(p, poly, y0, y1, color, floor=FLOOR, frac=0.55, cols=1, margin=1.5, gap=0.3, min_half=3.0):
    """
    Windows on every long side of a mass from ``y0`` to ``y1``: one storey every ~``floor`` m, ``frac`` of it glazed,
    as a ribbon (``cols`` = 1) or ``cols`` punched windows with ``gap`` of each bay left as wall.
    """
    n = max(1, round((y1 - y0) / floor))
    fh = (y1 - y0) / n
    for facet in facets(poly, min_half):
        h = facet[3] - margin
        if h < 1.0:
            continue
        bay = 2.0 * h / cols
        for k in range(n):
            ya = y0 + k * fh + fh * (1.0 - frac) * 0.5
            for c in range(cols):
                a0 = -h + c * bay + (bay * gap * 0.5 if cols > 1 else 0.0)
                a1 = a0 + bay - (bay * gap if cols > 1 else 0.0)
                wall_quad(p, facet, a0, a1, ya, ya + fh * frac, color)


def strips(p, poly, y0, y1, color, per_side, width, min_half=3.0, margin=1.0):
    """Vertical strips (pilasters, fins) evenly along every long side."""
    for facet in facets(poly, min_half):
        h = facet[3] - margin
        for k in range(per_side):
            a = -h + (k + 0.5) * 2.0 * h / per_side
            wall_quad(p, facet, a - width * 0.5, a + width * 0.5, y0, y1, color, off=OFF * 1.5)


def trim(p, poly, y, color, height=1.2):
    """A coloured band right round a mass (long sides and chamfers) at height ``y``."""
    for facet in facets(poly):
        wall_quad(p, facet, -facet[3], facet[3], y, y + height, color, off=OFF * 1.5)


def prism(p, poly, y0, y1, color, top=None, bottom=None, outline=True):
    return p.loft([at(poly, y0), at(poly, y1)], color, cap_start=bottom or color, cap_end=top or color, outline=outline)


def block(p, center, size, color, top=None, c=None, ink=PROP_INK):
    """A chamfered box (centre, size in Unity space) with its own thin ink."""
    cx, cy, cz = center
    w, h, d = size
    c = min(w, d) * 0.12 if c is None else c
    return prism(p, rect_pts(w, d, c, cx, cz), cy - h * 0.5, cy + h * 0.5, color, top=top, outline=ink)


def cylinder(p, x, z, y0, y1, r, color, sides=8, top=None, ink=PROP_INK, cone=None):
    """An upright n-gon cylinder, optionally closed by a cone rising ``cone`` m."""
    poly = ngon_pts(r, sides, x, z)
    if cone:
        return p.loft([at(poly, y0), at(poly, y1)], color, cap_start=color, fan_end=((x, y1 + cone, z), top or color),
                      outline=ink)
    return prism(p, poly, y0, y1, color, top=top, outline=ink)


def beam(p, a, b, r, color, ink=False, sides=4):
    """A thin straight member from ``a`` to ``b`` (cables, legs, masts)."""
    a, b = Vector(a), Vector(b)
    t = (b - a).normalized()
    ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 0, 1))
    u = (ref - t * ref.dot(t)).normalized()
    v = t.cross(u)
    rings = []
    for c in (a, b):
        rings.append([tuple(c + (u * math.cos(math.pi / sides + 2 * math.pi * k / sides)
                                 + v * math.sin(math.pi / sides + 2 * math.pi * k / sides)) * r) for k in range(sides)])
    return p.loft(rings, color, cap_start=color, cap_end=color, outline=ink)


# ---------------------------------------------------------------------------------------------- rooftop clutter


def ac_unit(p, x, z, y, yaw=0.0):
    w, d = (3.2, 2.2) if yaw == 0.0 else (2.2, 3.2)
    block(p, (x, y + 0.8, z), (w, 1.6, d), "metal", top="concrete")
    quad(p, at(ngon_pts(0.75, 6, x + (0.6 if yaw == 0.0 else 0.0), z + (0.0 if yaw == 0.0 else 0.6)), y + 1.6 + OFF * 0.5),
         "darkmetal", (0, 1, 0))


def water_tank(p, x, z, y):
    """A wooden tank on four legs with a cone roof: the brick-block skyline staple."""
    for dx, dz in ((-1.4, -1.4), (1.4, -1.4), (1.4, 1.4), (-1.4, 1.4)):
        beam(p, (x + dx, y, z + dz), (x + dx * 0.8, y + 3.2, z + dz * 0.8), 0.22, "darkmetal")
    cylinder(p, x, z, y + 3.2, y + 7.2, 2.2, "drywood", top="darkmetal", cone=1.4, ink=PROP_INK)
    trim(p, ngon_pts(2.2, 8, x, z), y + 4.4, "bark", 0.4)
    trim(p, ngon_pts(2.2, 8, x, z), y + 6.2, "bark", 0.4)


def penthouse(p, x, z, y, w, d, h, color="concrete"):
    block(p, (x, y + h * 0.5, z), (w, h, d), color, top="metal", ink=0.15)


def antenna(p, x, z, y, h, r=0.4):
    beam(p, (x, y, z), (x, y + h, z), r, "metal", ink=0.06)
    block(p, (x, y + h + 0.5, z), (1.2, 1.0, 1.2), "red", ink=0.06)


def helipad(p, y, r):
    flat(p, ngon_pts(r, 12), y, "sun")
    quad(p, at(ngon_pts(r - 1.2, 12), y + OFF), "darkmetal", (0, 1, 0))
    for x0, z0, x1, z1 in ((-3.0, -4.0, -1.8, 4.0), (1.8, -4.0, 3.0, 4.0), (-1.8, -0.6, 1.8, 0.6)):
        quad(p, at([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], y + OFF * 2), "white", (0, 1, 0))


def billboard(p, x, z, y, w, h, color, along_x=True):
    """A sign on two legs: a white frame with a coloured face both ways."""
    legs = [(-w * 0.35, 0.0), (w * 0.35, 0.0)] if along_x else [(0.0, -w * 0.35), (0.0, w * 0.35)]
    for dx, dz in legs:
        beam(p, (x + dx, y, z + dz), (x + dx, y + 3.0, z + dz), 0.3, "darkmetal", ink=0.06)
    size = (w, h, 0.8) if along_x else (0.8, h, w)
    block(p, (x, y + 3.0 + h * 0.5, z), size, "white", c=0.3, ink=0.12)
    n = (0.0, 0.0, 1.0) if along_x else (1.0, 0.0, 0.0)
    for s in (-1.0, 1.0):
        off = 0.4 + OFF
        if along_x:
            pts = [(x - w * 0.42, y + 3.6, z + s * off), (x + w * 0.42, y + 3.6, z + s * off),
                   (x + w * 0.42, y + 2.4 + h, z + s * off), (x - w * 0.42, y + 2.4 + h, z + s * off)]
        else:
            pts = [(x + s * off, y + 3.6, z - w * 0.42), (x + s * off, y + 3.6, z + w * 0.42),
                   (x + s * off, y + 2.4 + h, z + w * 0.42), (x + s * off, y + 2.4 + h, z - w * 0.42)]
        quad(p, pts, color, tuple(v * s for v in n))
        # a white stripe across the face, so it reads as a sign, not a coloured box
        mid = y + 3.0 + h * 0.5
        if along_x:
            stripe = [(x - w * 0.3, mid - 0.5, z + s * (off + 0.05)), (x + w * 0.1, mid - 0.5, z + s * (off + 0.05)),
                      (x + w * 0.1, mid + 0.5, z + s * (off + 0.05)), (x - w * 0.3, mid + 0.5, z + s * (off + 0.05))]
        else:
            stripe = [(x + s * (off + 0.05), mid - 0.5, z - w * 0.3), (x + s * (off + 0.05), mid - 0.5, z + w * 0.1),
                      (x + s * (off + 0.05), mid + 0.5, z + w * 0.1), (x + s * (off + 0.05), mid + 0.5, z - w * 0.3)]
        quad(p, stripe, "white", tuple(v * s for v in n))


def street_lamp(p, x, z, facing, h=9.0):
    """Pole, arm toward the street (``facing``: unit x/z), and a warm head."""
    fx, fz = facing
    beam(p, (x, 0.0, z), (x, h, z), 0.22, "darkmetal")  # no ink on the thin bits: it'd double their triangles for a hairline
    beam(p, (x, h - 0.2, z), (x + fx * 2.4, h - 0.2, z + fz * 2.4), 0.16, "darkmetal")
    block(p, (x + fx * 2.6, h - 0.5, z + fz * 2.6), (1.4, 0.6, 1.4), "sun", top="darkmetal", c=0.25, ink=0.05)


def traffic_light(p, x, z, facing):
    fx, fz = facing
    beam(p, (x, 0.0, z), (x, 7.0, z), 0.2, "darkmetal")
    beam(p, (x, 6.8, z), (x + fx * 5.0, 6.8, z + fz * 5.0), 0.15, "darkmetal")
    hx, hz = x + fx * 4.6, z + fz * 4.6
    block(p, (hx, 5.9, hz), (0.9, 2.4, 0.9), "darkmetal", c=0.15, ink=0.05)
    # the three lamps face back down the street (perpendicular to the arm), both ways
    nx, nz = -fz, fx
    for s in (-1.0, 1.0):
        for k, color in enumerate(("red", "sun", "leaf")):
            y = 6.7 - k * 0.75
            cx, cz = hx + nx * s * (0.45 + OFF * 0.3), hz + nz * s * (0.45 + OFF * 0.3)
            pts = [(cx - fx * 0.25, y - 0.25, cz - fz * 0.25), (cx + fx * 0.25, y - 0.25, cz + fz * 0.25),
                   (cx + fx * 0.25, y + 0.25, cz + fz * 0.25), (cx - fx * 0.25, y + 0.25, cz - fz * 0.25)]
            quad(p, pts, color, (nx * s, 0.0, nz * s))


# ---------------------------------------------------------------------------------------------- buildings


class Building:
    """A building Part plus its collider Part (the solid masses only)."""

    def __init__(self, name, ink):
        self.p = Part(name, outline=ink)
        self.col = Part(name + "Col")

    def mass(self, poly, y0, y1, color, roof="metal", solid=True, top_ring=None):
        """A straight-sided mass; ``top_ring`` replaces its top (e.g. a sloped roof)."""
        rings = [at(poly, y0), top_ring or at(poly, y1)]
        self.p.loft(rings, color, cap_start=color, cap_end=roof)
        if solid:
            self.col.loft(rings, "metal", cap_start="metal", cap_end="metal", outline=False)
        return poly

    def solid(self, poly, y0, y1):
        """Collider only (e.g. a crown the mesh builds in pieces)."""
        self.col.loft([at(poly, y0), at(poly, y1)], "metal", cap_start="metal", cap_end="metal", outline=False)

    @property
    def parts(self):
        return [self.p, self.col]


def roof_rim(p, w, d, c, y, rim=1.6, inner="metal"):
    """The parapet look from above: the roof cap is the rim colour, an inset decal the roof itself."""
    flat(p, rect_pts(w - 2 * rim, d - 2 * rim, max(0.5, c - rim * 0.4)), y, inner)


def tower_glass():
    """Glass shaft with concrete spandrels on a stone podium, two setbacks, a slate crown and a mast."""
    b = Building("TowerGlass", ink=0.45)
    p = b.p
    pod = b.mass(rect_pts(44, 44, 4), SINK, 12, "concrete", roof="concrete")
    windows(p, pod, 0.5, 10.5, "window", floor=10, frac=0.7, margin=3)
    roof_rim(p, 44, 44, 4, 12)
    shaft = b.mass(rect_pts(34, 34, 4), 12, 150, "window", roof="concrete")
    windows(p, shaft, 12, 150, "concrete", floor=8, frac=0.22, margin=0, min_half=6)
    roof_rim(p, 34, 34, 4, 150, rim=1.2)
    mid = b.mass(rect_pts(28, 28, 3.5), 150, 196, "window", roof="concrete")
    windows(p, mid, 150, 196, "concrete", floor=8, frac=0.22, margin=0, min_half=6)
    roof_rim(p, 28, 28, 3.5, 196, rim=1.2)
    crown = b.mass(rect_pts(20, 20, 3), 196, 214, "slate", roof="slate")
    strips(p, crown, 197, 213, "window", 3, 1.6)
    antenna(p, 0, 0, 214, 32, r=0.6)
    for x, z in ((-11, -11), (11, 11)):
        ac_unit(p, x, z, 150)
    return b.parts


def tower_deco():
    """Art deco: four setbacks with gold trim, tall punched windows, a gold stepped crown and a needle."""
    b = Building("TowerDeco", ink=0.45)
    p = b.p
    steps = [(40, 3.0, SINK, 120, 4), (32, 3.0, 120, 175, 3), (24, 2.5, 175, 210, 2), (16, 2.0, 210, 228, 1)]
    for w, c, y0, y1, cols in steps:
        poly = b.mass(rect_pts(w, w, c), y0, y1, "concrete", roof="concrete")
        windows(p, poly, max(y0, 6.0), y1 - 3.0, "slate", floor=8, frac=0.62, cols=cols, margin=1.2, gap=0.35)
        trim(p, poly, y1 - 2.2, "sun", 1.4)
        roof_rim(p, w, w, c, y1, rim=1.4, inner="slate")
    windows(p, rect_pts(40, 40, 3), 0.5, 5.5, "window", floor=6, frac=0.8, margin=4)  # lobby
    crown = [at(rect_pts(16, 16, 2), 228), at(rect_pts(12, 12, 1.5), 236)]
    p.loft(crown, "sun", cap_start="sun", fan_end=((0, 250, 0), "sun"))
    b.solid(rect_pts(12, 12, 1.5), 228, 240)
    antenna(p, 0, 0, 249, 26, r=0.5)
    return b.parts


def tower_round():
    """A 12-sided drum with ribbon windows, a slate crown ring and a helipad."""
    b = Building("TowerRound", ink=0.4)
    p = b.p
    drum = b.mass(ngon_pts(20, 12), SINK, 176, "concrete", roof="slate")
    windows(p, drum, 0.5, 8.5, "window", floor=8, frac=0.8, margin=0.4, min_half=2)
    windows(p, drum, 8.5, 172, "window", floor=7.5, frac=0.5, margin=0.5, min_half=2)
    ring = b.mass(ngon_pts(21.5, 12), 172, 180, "slate", roof="slate")
    trim(p, ring, 175.4, "white", 1.0)
    helipad(p, 180, 13)
    penthouse(p, 15, 0, 180, 3.5, 6, 3.5, "slate")
    return b.parts


def tower_blue():
    """All-glass tower whose roof is cut on a slope: the downtown silhouette you can name."""
    b = Building("TowerBlue", ink=0.4)
    p = b.p
    poly = rect_pts(38, 38, 5)
    top = [(x, 156 + 0.75 * x, z) for x, z in poly]  # 141.75 .. 170.25
    b.mass(poly, SINK, None, "window", roof="slate", top_ring=top)
    windows(p, poly, 8, 136, "slate", floor=8, frac=0.18, margin=0, min_half=6)
    strips(p, poly, 8, 136, "concrete", 4, 1.2, min_half=6)
    windows(p, poly, 0.5, 7.5, "concrete", floor=8, frac=0.25, margin=0, min_half=6)
    trim(p, poly, 138, "white", 1.2)
    beam(p, (-12, 147, 0), (-12, 160, 0), 0.4, "metal", ink=0.06)  # a window-cleaning crane on the slope
    beam(p, (-12, 160, 0), (4, 160, 0), 0.35, "metal", ink=0.06)
    return b.parts


def tower_slab():
    """A long concrete slab on a slate podium, ribbon windows, end fins, and a busy roof."""
    b = Building("TowerSlab", ink=0.4)
    p = b.p
    pod = b.mass(rect_pts(52, 34, 3), SINK, 10, "slate", roof="concrete")
    windows(p, pod, 0.5, 9, "window", floor=9, frac=0.75, margin=2)
    roof_rim(p, 52, 34, 3, 10)
    slab = b.mass(rect_pts(44, 24, 3), 10, 160, "concrete", roof="concrete")
    windows(p, slab, 11, 159, "window", floor=7, frac=0.5, margin=2, min_half=8)
    for s in (-1.0, 1.0):  # a slate stripe up each end, the full height
        x = s * (22.0 + OFF * 1.5)
        quad(p, [(x, 10, -6), (x, 10, 6), (x, 160, 6), (x, 160, -6)], "slate", (s, 0, 0))
    roof_rim(p, 44, 24, 3, 160)
    penthouse(p, -8, 0, 160, 12, 9, 6)
    water_tank(p, 12, -4, 160)
    for x in (2.0, 6.0):
        ac_unit(p, x, 6, 160)
    antenna(p, -14, 6, 160, 18)
    antenna(p, -8, 0, 166, 10, r=0.3)
    return b.parts


def office_bands():
    b = Building("OfficeBands", ink=0.3)
    p = b.p
    poly = b.mass(rect_pts(40, 40, 3), SINK, 96, "concrete", roof="concrete")
    windows(p, poly, 0.5, 6, "window", floor=6, frac=0.8, margin=3)
    windows(p, poly, 6, 95, "window", floor=6.5, frac=0.48, margin=1.2)
    trim(p, poly, 93.5, "slate", 2.0)
    roof_rim(p, 40, 40, 3, 96)
    penthouse(p, -6, -4, 96, 12, 10, 5)
    for x, z in ((8, -10), (8, -4), (8, 2)):
        ac_unit(p, x, z, 96)
    antenna(p, -8, 10, 96, 16)
    return b.parts


def office_glass():
    """A glass box on a concrete podium, with a roof garden."""
    b = Building("OfficeGlass", ink=0.3)
    p = b.p
    pod = b.mass(rect_pts(42, 42, 3), SINK, 8, "concrete", roof="concrete")
    windows(p, pod, 0.5, 7, "window", floor=7, frac=0.8, margin=2.5)
    body = b.mass(rect_pts(36, 36, 3.5), 8, 68, "window", roof="concrete")
    windows(p, body, 8, 68, "slate", floor=6, frac=0.25, margin=0, min_half=6)
    flat(p, rect_pts(26, 18, 2, 0, -5), 68, "grass")
    for k, (x, z, r) in enumerate(((-7, -8, 2.4), (5, -3, 2.0), (8, -10, 1.8))):
        for f in lump(p, (x, 68 + r * 0.6, z), (r, r * 0.9, r), 300 + k, "leaf", floor=-r * 0.5):
            p.hull_thickness[f] = PROP_INK
    penthouse(p, 0, 12, 68, 14, 7, 4)
    return b.parts


def hotel():
    """Cream slab with punched windows, a setback and a big rooftop sign."""
    b = Building("Hotel", ink=0.35)
    p = b.p
    poly = b.mass(rect_pts(40, 30, 3), SINK, 104, "body", roof="concrete")
    windows(p, poly, 0.5, 6, "window", floor=6, frac=0.8, margin=3)
    windows(p, poly, 7, 102, "window", floor=7, frac=0.55, cols=4, margin=1.2, gap=0.35)
    trim(p, poly, 6.0, "sun", 1.0)
    roof_rim(p, 40, 30, 3, 104)
    top = b.mass(rect_pts(28, 20, 2.5), 104, 124, "body", roof="concrete")
    windows(p, top, 105, 122, "window", floor=7, frac=0.55, cols=3, margin=1.2, gap=0.35)
    roof_rim(p, 28, 20, 2.5, 124)
    billboard(p, 0, -12.5, 104, 22, 7, "flower")
    ac_unit(p, -7, 4, 124)
    ac_unit(p, 6, 4, 124)
    return b.parts


def brick_block():
    """Brick walk-up: shopfronts, punched windows, a stone cornice, a water tank on the roof."""
    b = Building("BrickBlock", ink=0.2)
    p = b.p
    poly = b.mass(rect_pts(40, 40, 2), SINK, 30, "brick", roof="darkmetal")
    windows(p, poly, 0.6, 5.2, "window", floor=5, frac=0.85, cols=3, margin=2, gap=0.15)
    windows(p, poly, 6.5, 28, "window", floor=5.4, frac=0.55, cols=5, margin=2, gap=0.4)
    cornice = rect_pts(41.6, 41.6, 2.4)
    prism(p, cornice, 28.6, 30.8, "concrete", top="concrete", outline=0.2)
    b.solid(cornice, 28.6, 30.8)
    roof_rim(p, 41.6, 41.6, 2.4, 30.8, rim=1.3, inner="darkmetal")
    trim(p, poly, 5.4, "concrete", 0.8)
    water_tank(p, 9, 9, 30.8)
    ac_unit(p, -8, -6, 30.8)
    penthouse(p, -9, 9, 30.8, 6, 6, 3.5, "brick")
    return b.parts


def apartments():
    """Cream block with balcony strips, two water tanks."""
    b = Building("Apartments", ink=0.25)
    p = b.p
    poly = b.mass(rect_pts(40, 40, 3), SINK, 46, "body", roof="concrete")
    windows(p, poly, 0.6, 5.0, "window", floor=5, frac=0.8, cols=2, margin=3, gap=0.2)
    windows(p, poly, 6.0, 45, "slate", floor=5.6, frac=0.5, cols=4, margin=1.5, gap=0.3)
    n = 7
    for k in range(n):  # balcony slabs under every other storey's windows
        y = 6.0 + k * 5.57
        for facet in facets(poly, 6):
            wall_quad(p, facet, -facet[3] + 2.5, facet[3] - 2.5, y, y + 0.8, "white", off=OFF * 2)
    roof_rim(p, 40, 40, 3, 46)
    water_tank(p, -9, 8, 46)
    water_tank(p, 8, 9, 46)
    penthouse(p, 0, -8, 46, 8, 6, 3.5)
    return b.parts


def shops():
    """Two storeys: shopfronts under striped awnings, offices above, AC units and a billboard on the roof."""
    b = Building("Shops", ink=0.18)
    p = b.p
    poly = b.mass(rect_pts(40, 40, 2), SINK, 17, "concrete", roof="concrete")
    windows(p, poly, 0.6, 5.4, "window", floor=6, frac=0.85, cols=3, margin=1.5, gap=0.12)
    windows(p, poly, 8.5, 15.5, "window", floor=7, frac=0.6, cols=5, margin=1.5, gap=0.35)
    trim(p, poly, 6.4, "slate", 1.0)
    colors = ["sun", "flower", "leaf", "slate"]
    for i, facet in enumerate(facets(poly, 6)):  # an awning over each shop: a sloped slab out from the wall
        (mx, mz), (nx, nz), (tx, tz), half = facet
        bay = 2 * (half - 1.5) / 3
        for k in range(3):
            a0 = -half + 1.5 + k * bay + 0.4
            a1 = a0 + bay - 0.8
            color = colors[(i + k) % len(colors)]
            rings = []
            for out, y in ((0.0, 6.4), (2.6, 5.2)):
                rings.append([(mx + nx * out + tx * a, y + dy, mz + nz * out + tz * a)
                              for a, dy in ((a0, 0.0), (a1, 0.0), (a1, -0.25), (a0, -0.25))])
            p.loft(rings, lambda band, side, c=color: c if side == 0 else "white", cap_start="white",
                   cap_end=color, outline=0.06)
    roof_rim(p, 40, 40, 2, 17)
    for x, z in ((-10, -10), (-4, -10), (8, 8)):
        ac_unit(p, x, z, 17)
    billboard(p, 4, -6, 17, 16, 6, "sun")
    return b.parts


def garage():
    """A parking garage: open decks (dark slots between concrete slabs) and parked cars on the roof."""
    b = Building("Garage", ink=0.2)
    p = b.p
    poly = b.mass(rect_pts(40, 40, 2), SINK, 20, "darkmetal", roof="asphalt")
    windows(p, poly, 0.0, 20, "concrete", floor=5, frac=0.28, margin=0, min_half=4)
    trim(p, poly, 19.0, "concrete", 1.0)
    for k in range(6):  # bay lines on the roof deck
        x = -15 + k * 6
        for z0 in (-16.0, 4.0):
            quad(p, at([(x - 0.15, z0), (x + 0.15, z0), (x + 0.15, z0 + 12), (x - 0.15, z0 + 12)], 20 + OFF * 0.5),
                 "white", (0, 1, 0))
    for x, z, color in ((-12, -10, "sun"), (0, -10, "white"), (6, 10, "water"), (-6, 10, "flower")):
        block(p, (x, 20.75, z), (2.6, 1.5, 5.0), color, top=color, c=0.6, ink=0.06)
        block(p, (x, 21.8, z - 0.3), (2.2, 0.7, 2.6), "window", top=color, c=0.4, ink=0.04)
    stair = b.mass(rect_pts(7, 7, 1, 15, 15), 20, 24, "concrete", roof="metal")
    trim(p, stair, 22.0, "sun", 0.6)
    return b.parts


BUILDINGS = [tower_glass, tower_deco, tower_round, tower_blue, tower_slab, office_bands, office_glass, hotel,
             brick_block, apartments, shops, garage]


# ---------------------------------------------------------------------------------------------- street kit


def city_block():
    """Sidewalk slab under a block, curbs, lamps on every side and traffic lights at two corners."""
    p = Part("CityBlock", outline=PROP_INK)
    prism(p, rect_pts(90, 90, 4), -0.6, 0.35, "concrete", outline=False)
    flat(p, rect_pts(86, 86, 3), 0.35, "metal")  # grey sidewalk inside a light curb
    for (nx, nz) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for along in (-22.0, 22.0):
            x = nx * 42.5 + (along if nx == 0 else 0)
            z = nz * 42.5 + (along if nz == 0 else 0)
            street_lamp(p, x, z, (nx, nz))
    for (cx, cz), facing in (((42.5, 42.5), (0, 1)), ((-42.5, -42.5), (0, -1))):
        traffic_light(p, cx, cz, facing)
    return p


def road():
    """A 30 m street, 90 m long along z: asphalt with a dashed centre line and solid edge lines."""
    p = Part("Road")
    prism(p, rect_pts(30, 90.4, 0), -0.6, 0.1, "asphalt", outline=False)
    for k in range(9):
        z = -40 + k * 10
        flat(p, [(-0.3, z - 2.5), (0.3, z - 2.5), (0.3, z + 2.5), (-0.3, z + 2.5)], 0.1, "white")
    for x in (-13.0, 13.0):
        flat(p, [(x - 0.25, -45), (x + 0.25, -45), (x + 0.25, 45), (x - 0.25, 45)], 0.1, "white")
    return p


def crossing():
    """A 30 x 30 m intersection with zebra crossings across each of its four sides."""
    p = Part("Crossing")
    prism(p, rect_pts(30.4, 30.4, 0), -0.6, 0.1, "asphalt", outline=False)
    for k in range(7):
        a = -10.5 + k * 3.5
        for sx, sz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if sx:  # stripes run along x on the east/west sides
                pts = [(sx * 9.5, a - 0.8), (sx * 14.5, a - 0.8), (sx * 14.5, a + 0.8), (sx * 9.5, a + 0.8)]
            else:
                pts = [(a - 0.8, sz * 9.5), (a + 0.8, sz * 9.5), (a + 0.8, sz * 14.5), (a - 0.8, sz * 14.5)]
            flat(p, pts, 0.1, "white")
    return p


def plaza():
    """A square: paving, four lawns, a fountain in the middle. Unity plants trees on the lawns."""
    p = Part("Plaza", outline=PROP_INK * 2)
    prism(p, rect_pts(90, 90, 4), -0.6, 0.35, "concrete", outline=False)
    flat(p, rect_pts(86, 86, 3), 0.35, "metal")
    for sx in (-1, 1):
        for sz in (-1, 1):
            flat(p, rect_pts(30, 30, 6, sx * 23, sz * 23), 0.35, "grass", layer=1)
    flat(p, ngon_pts(17, 12), 0.35, "concrete", layer=2)  # paved circle round the fountain
    basin = ngon_pts(10, 12)
    prism(p, basin, -0.3, 1.2, "concrete", top="concrete", outline=0.15)
    flat(p, ngon_pts(8.8, 12), 1.0, "water")
    cylinder(p, 0, 0, 1.0, 3.6, 1.0, "concrete", sides=8, ink=0.08)
    cylinder(p, 0, 0, 3.6, 4.2, 3.0, "concrete", sides=10, ink=0.08)
    flat(p, ngon_pts(2.6, 10), 4.2, "water")
    lump(p, (0, 5.0, 0), (0.9, 1.2, 0.9), 410, "water", subdiv=1, bumps=0.1, outline=False)
    for (x, z) in ((-12, 0), (12, 0), (0, -12), (0, 12)):  # lamps on the paved circle
        street_lamp(p, x, z, (x / 12.0, z / 12.0), h=6.0)
    return p


def quay(name, length, lamp):
    """
    One length of canal wall: face at x = 0 looking down -x into the canal, origin at the top of the wall at street
    level. Stone face 16 m deep (the water is 6 m down), a 12 m promenade behind it, bollards on the edge.
    """
    p = Part(name, outline=0.06)
    hl = length * 0.5 + 0.15  # a little overlap so the joints don't gap on the outer bends
    section = [(0.0, -QUAY_DEPTH), (0.0, 0.0), (PROMENADE, 0.0), (PROMENADE, -QUAY_DEPTH)]
    rings = [[(x, y, z) for x, y in section] for z in (-hl, hl)]
    p.loft(rings, lambda b, s: "concrete" if s == 1 else "stone", cap_start="stone", cap_end="stone", outline=False)
    quad(p, [(-OFF, -1.0, -hl), (-OFF, -1.0, hl), (-OFF, 0.0, hl), (-OFF, 0.0, -hl)], "concrete", (-1, 0, 0))
    quad(p, [(-OFF, -6.6, -hl), (-OFF, -6.6, hl), (-OFF, -5.6, hl), (-OFF, -5.6, -hl)], "slate", (-1, 0, 0))
    flat(p, [(0.0, -hl), (1.6, -hl), (1.6, hl), (0.0, hl)], 0.0, "slate")  # the edge stones
    n = max(1, round(length / 7.0))
    for k in range(n):
        z = -length * 0.5 + (k + 0.5) * length / n
        cylinder(p, 0.8, z, 0.0, 0.9, 0.35, "darkmetal", sides=6, ink=0.05)
    if lamp:
        street_lamp(p, 2.2, 0.0, (-1.0, 0.0), h=7.0)
    return p


def bridge():
    """
    The canal's cable-stayed bridge: a 230 m deck 34 m over the water (its middle), H-frame towers 150 m apart standing
    on the promenades either side, a fan of stays from each tower top, stone abutments at the ends.
    """
    p = Part("Bridge", outline=0.25)
    col = Part("BridgeCol")
    half = SPAN * 0.5
    # Deck: a chamfered girder along x, asphalt on top, slate sides.
    sec = [(9.0, 35.5), (7.0, 32.5), (-7.0, 32.5), (-9.0, 35.5)]  # (z, y)
    rings = [[(x, y, z) for z, y in sec] for x in (-half, half)]
    p.loft(rings, lambda b, s: "asphalt" if s == 3 else "slate", cap_start="slate", cap_end="slate")
    col.loft(rings, "metal", cap_start="metal", cap_end="metal", outline=False)
    for k in range(int(SPAN // 10)):
        x = -half + 5 + k * 10
        quad(p, at([(x - 2.5, -0.25), (x + 2.5, -0.25), (x + 2.5, 0.25), (x - 2.5, 0.25)], 35.5 + OFF * 0.5), "white",
             (0, 1, 0))
    for z in (-8.6, 8.6):  # railings
        block(p, (0, 36.6, z), (SPAN - 2, 0.5, 0.35), "white", c=0.1, ink=0.06)
        for k in range(int(SPAN // 6)):
            x = -half + 3 + k * 6
            beam(p, (x, 35.5, z), (x, 36.5, z), 0.12, "white")
    # Towers: two tapering legs either side of the deck, a crossbeam under the deck and one at the top.
    for tx in (-TOWER_X, TOWER_X):
        for z in (-12.0, 12.0):
            leg = [at(rect_pts(4.6, 4.6, 1.0, tx, z), 2.0), at(rect_pts(3.0, 3.0, 0.7, tx, z), 92.0)]
            p.loft(leg, "concrete", cap_start="concrete", cap_end="concrete")
            col.loft(leg, "metal", cap_start="metal", cap_end="metal", outline=False)
            block(p, (tx, 93.0, z), (2.0, 2.0, 2.0), "red", ink=0.08)
        for y, h in ((30.5, 3.0), (86.0, 4.0)):
            beam_poly = rect_pts(3.0, 24.0, 0.6, tx, 0.0)
            p.loft([at(beam_poly, y - h * 0.5), at(beam_poly, y + h * 0.5)], "concrete", cap_start="concrete",
                   cap_end="concrete")
            col.loft([at(beam_poly, y - h * 0.5), at(beam_poly, y + h * 0.5)], "metal", cap_start="metal",
                     cap_end="metal", outline=False)
        # Stays: from each leg top to the deck edge, a fan toward the middle and two outward.
        out = math.copysign(1.0, tx)
        ends = [tx - out * d for d in (60.0, 42.0, 24.0)] + [tx + out * d for d in (18.0, 33.0)]
        for z in (-12.0, 12.0):
            for x in ends:
                beam(p, (tx, 82.0, z), (x, 35.8, math.copysign(8.2, z)), 0.3, "white")
    # Abutments: stepped stone blocks at each end with a lamp on top.
    for sx in (-1.0, 1.0):
        ax = sx * (half - 7.0)
        poly = rect_pts(14.0, 22.0, 2.0, ax, 0.0)
        p.loft([at(poly, -2.0), at(poly, 32.5)], "stone", cap_start="stone", cap_end="stone")
        col.loft([at(poly, -2.0), at(poly, 32.5)], "metal", cap_start="metal", cap_end="metal", outline=False)
        trim(p, poly, 3.0, "slate", 2.0)
        for z in (-9.5, 9.5):
            cylinder(p, ax, z, 35.5, 38.5, 0.9, "concrete", sides=6, ink=0.08)
            cylinder(p, ax, z, 38.5, 39.7, 1.1, "sun", sides=6, top="concrete", ink=0.08)
    return [p, col]


# ---------------------------------------------------------------------------------------------- vehicles


def car(name, color, length=6.4, width=2.8):
    """A toy car (about 1.4x life, like the trees): body, glass cabin, roof in the body colour. Nose +z."""
    p = Part(name, outline=0.06)
    hl, hw = length * 0.5, width * 0.5
    body = [at(rect_pts(width, length, 0.6), 0.25), at(rect_pts(width, length, 0.6), 1.3)]
    p.loft(body, color, cap_start="tire", cap_end=color)
    cab = [at(rect_pts(width - 0.3, length * 0.55, 0.5, 0, -0.3), 1.3), at(rect_pts(width - 0.7, length * 0.4, 0.4, 0, -0.4), 2.25)]
    p.loft(cab, "window", cap_start=color, cap_end=color)
    for sx in (-1, 1):
        for sz in (-1, 1):
            cylinder(p, sx * (hw - 0.15), sz * (hl - 1.2), 0.0, 0.6, 0.5, "tire", sides=6, ink=False)
    quad(p, [(-hw + 0.3, 0.6, hl + OFF * 0.3), (-hw + 0.8, 0.6, hl + OFF * 0.3), (-hw + 0.8, 1.0, hl + OFF * 0.3),
             (-hw + 0.3, 1.0, hl + OFF * 0.3)], "sun", (0, 0, 1))
    quad(p, [(hw - 0.8, 0.6, hl + OFF * 0.3), (hw - 0.3, 0.6, hl + OFF * 0.3), (hw - 0.3, 1.0, hl + OFF * 0.3),
             (hw - 0.8, 1.0, hl + OFF * 0.3)], "sun", (0, 0, 1))
    return p


def bus():
    p = Part("Bus", outline=0.08)
    body = [at(rect_pts(3.4, 15.0, 0.6), 0.5), at(rect_pts(3.4, 15.0, 0.6), 4.2)]
    p.loft(body, "sun", cap_start="tire", cap_end="white")
    windows(p, rect_pts(3.4, 15.0, 0.6), 2.0, 3.6, "window", floor=1.6, frac=0.9, cols=5, margin=0.8, gap=0.15, min_half=1.0)
    trim(p, rect_pts(3.4, 15.0, 0.6), 1.2, "white", 0.4)
    for sz in (-5.0, 5.0):
        for sx in (-1.6, 1.6):
            cylinder(p, sx, sz, 0.0, 1.0, 0.6, "tire", sides=6, ink=False)
    return p


def tugboat():
    """A stubby tug: dark hull with a red waterline, white cabin, yellow funnel. Origin at the waterline."""
    p = Part("Tugboat", outline=0.08)
    hull = []
    for y, w, l in ((-1.2, 3.2, 12.0), (0.0, 5.2, 15.0), (0.6, 5.4, 15.4), (1.6, 5.6, 16.0)):
        hw, hl = w * 0.5, l * 0.5
        hull.append([(hw, y, -hl + 1.0), (hw, y, hl - 3.0), (0.0, y, hl), (-hw, y, hl - 3.0), (-hw, y, -hl + 1.0),
                     (-hw * 0.7, y, -hl), (hw * 0.7, y, -hl)])
    p.loft(hull, lambda b, s: "red" if b == 1 else "darkmetal", cap_start="darkmetal", cap_end="metal")
    block(p, (0, 3.1, -1.5), (3.6, 3.0, 5.5), "white", top="slate", ink=0.06)
    windows(p, rect_pts(3.6, 5.5, 0.66, 0, -1.5), 3.0, 4.2, "window", floor=1.2, frac=0.9, cols=3, margin=0.3, gap=0.2, min_half=1.0)
    cylinder(p, 0, -5.0, 1.6, 6.0, 0.9, "sun", sides=8, top="char", ink=0.06)
    trim(p, ngon_pts(0.9, 8, 0, -5.0), 5.0, "char", 0.6)
    return p


def ferry():
    """A canal water bus: white hull with a slate stripe, a long glazed cabin, a sun deck on top."""
    p = Part("Ferry", outline=0.1)
    hull = []
    for y, w, l in ((-1.0, 6.0, 30.0), (0.0, 8.0, 34.0), (0.7, 8.15, 34.8), (1.8, 8.4, 36.0)):
        hw, hl = w * 0.5, l * 0.5
        hull.append([(hw, y, -hl + 2.0), (hw, y, hl - 6.0), (hw * 0.4, y, hl), (-hw * 0.4, y, hl), (-hw, y, hl - 6.0),
                     (-hw, y, -hl + 2.0), (-hw * 0.6, y, -hl), (hw * 0.6, y, -hl)])
    p.loft(hull, lambda b, s: "slate" if b == 1 else "white", cap_start="slate", cap_end="concrete")
    cabin = rect_pts(6.4, 22.0, 1.4, 0, -2.0)
    prism(p, cabin, 1.8, 5.0, "white", top="concrete", outline=0.08)
    windows(p, cabin, 2.6, 4.6, "window", floor=2.0, frac=0.9, cols=6, margin=0.8, gap=0.15, min_half=2.0)
    for x in (-3.0, 3.0):
        block(p, (x, 5.6, -2.0), (0.2, 1.0, 20.0), "white", c=0.05, ink=0.04)
    block(p, (0, 6.2, 9.2), (3.0, 2.4, 3.0), "white", top="slate", ink=0.06)
    trim(p, cabin, 5.0, "sun", 0.4)
    return p


# ---------------------------------------------------------------------------------------------- build


def build():
    model = Model("City")
    parts = []
    for fn in BUILDINGS:
        parts += fn()
    parts += [city_block(), road(), crossing(), plaza(), quay("Quay", 20.0, True), quay("QuayShort", 7.0, False)]
    parts += bridge()
    parts += [car("CarSun", "sun"), car("CarWhite", "white"), car("CarTeal", "water"), bus(), tugboat(), ferry()]
    model.add(parts)
    for obj in model.objects:
        drop_unused_materials(obj)
    model.export(FBX_PATH, BLEND_PATH)


# Preview layout: Unity positions per object (they all export at the origin).
ROW = ["TowerGlass", "TowerDeco", "TowerRound", "TowerBlue", "TowerSlab", "OfficeBands", "OfficeGlass", "Hotel",
       "BrickBlock", "Apartments", "Shops", "Garage"]


def lay_out():
    for o in [o for o in bpy.data.objects if o.name.endswith("Col")]:  # exported already; they'd all stack at the origin
        bpy.data.objects.remove(o)
    for i, name in enumerate(ROW):
        bpy.data.objects[name].location = to_blender((i * 60.0 - 330.0, 0.0, 0.0))
    street = {"CityBlock": (0, 0, -200), "Road": (60, 0, -200), "Crossing": (60, 0, -260), "Plaza": (120 + 0, 0, -200),
              "Quay": (-80, 0, -200), "QuayShort": (-80, 0, -216), "CarSun": (55, 0.1, -210), "CarWhite": (65, 0.1, -190),
              "CarTeal": (55, 0.1, -180), "Bus": (65, 0.1, -225), "Tugboat": (-100, -6, -200), "Ferry": (-110, -6, -170)}
    for name, pos in street.items():
        bpy.data.objects[name].location = to_blender(pos)
    bpy.data.objects["Bridge"].location = to_blender((0.0, 0.0, -600.0))


SHOTS = [
    ("city_buildings", (0.0, 380.0, -900.0), (0.0, 110.0, 0.0), 740.0),
    ("city_towers", (-230.0, 330.0, -330.0), (-230.0, 120.0, 0.0), None),
    ("city_lowrise", (210.0, 55.0, -120.0), (210.0, 15.0, 0.0), None),
    ("city_top", (0.0, 600.0, 0.01), (0.0, 0.0, 0.0), 720.0),
    ("city_street", (40.0, 45.0, -300.0), (20.0, 0.0, -200.0), None),
    ("city_bridge", (180.0, 60.0, -760.0), (0.0, 40.0, -600.0), None),
]


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
