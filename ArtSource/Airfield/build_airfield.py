"""
Builds the airfield pack (Design/03-Art/final-asset-list.md, Pack 7) from scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/Airfield/build_airfield.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Airfield/Airfield.fbx    one object per asset, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png       the shared palette atlas (rewritten, same as the planes')
    ArtSource/Airfield/Airfield.blend                   the generated scene, for hand tweaks and renders

Built to FlightTestSceneBuilder.BuildAirfieldPrefab's layout (Unity space, metres, the airfield's origin in the middle
of the runway, landing heading +z, buildings off to +x):
    Runway             40 x 600 m slab (top at y 0.1) with shoulders, blast pads with chevrons, centreline, edge lines,
                       threshold piano keys, 36 / 18 numbers, touchdown and aiming-point bars, edge, threshold and
                       approach lights and a PAPI (lights on the glow material). Origin = the airfield's.
    RepairPad          the repair zone's paint (30 x 200 m border, hatched ends, a giant wrench), origin on the zone
                       (0, 0.1, 140)
    Apron              concrete pad between the runway and the buildings with taxi lines, origin = the airfield's
    Hangar             arched hangar, 40 deep (x) x 32 wide (z), doors open toward -x (the runway), team-coloured roof
                       with white ribs, a lit workshop inside (the main menu backdrop). Origin on the floor's middle.
                       HangarCol: its solid walls, roof and doors
    ControlTower       ops block, shaft, glass cab, origin at the shaft's foot; ControlTowerBeacon spins on its roof
                       (origin on its pivot). ControlTowerCol: the plain masses
    AAGunBase          concrete pad, sandbag ring and pedestal (origin on the ground); AAGunHead turns on it (yaw,
                       origin (0, 3, 0)), AAGunCradle tilts in it (pitch, origin (0, 3.2, 0)), AAGunBarrelLeft/Right
                       recoil in the cradle (origin at their breech, x -/+0.45, z 0.9). Marker AAGunMuzzle.
    RadarMast          lattice mast on a cabin; RadarDish turns on top (origin (0, 11.6, 0))
    WindsockPole       10 m striped pole; Windsock is the sock, origin on its hoop (0, 9.8, 0.6), streaming +z
    FuelTank, FuelTruck, RepairCrane, ToolCart, CrateStack, Barrels, Mechanic, MechanicClipboard   props on the ground

Everything is on the shared plane materials (Plane_Paint, Plane_Team, Plane_Ink, Weapon_Glow for lights) and palette.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Nature"))
sys.path.insert(0, os.path.join(HERE, "..", "City"))
from plane_kit import (MODELS_DIR, PALETTE_PATH, Model, Part, make_material, preview_arg, render_previews,  # noqa: E402
                       ring, roundel, to_blender)
from build_nature import drop_unused_materials, tube  # noqa: E402
from build_city import at, beam, block, cylinder, facets, ngon_pts, prism, quad, rect_pts, wall_quad, windows  # noqa: E402

FBX_PATH = os.path.join(MODELS_DIR, "Airfield", "Airfield.fbx")
BLEND_PATH = os.path.join(HERE, "Airfield.blend")

# FlightTestSceneBuilder numbers these are built to.
RW_W, RW_L = 40.0, 600.0
HW, HL = RW_W * 0.5, RW_L * 0.5
SURFACE = 0.1  # the runway collider's top
PAINT = SURFACE + 0.06  # markings float this far up so they don't flicker from a kilometre out
ZONE_Z, ZONE_W, ZONE_L = 140.0, 30.0, 200.0  # repair zone: centre, size
SHOULDER = 4.0

# Where the prefab puts things (only used to lay out the previews; the prefab builder owns the layout).
LAYOUT = {
    "Hangar": (75.0, 0.0, -40.0), "ControlTower": (65.0, 0.0, 120.0), "RadarMast": (98.0, 0.0, 160.0),
    "WindsockPole": (35.0, 0.0, -240.0), "FuelTank": (110.0, 0.0, -6.0), "FuelTruck": (94.0, 0.0, 6.0),
    "RepairCrane": (32.0, 0.0, 110.0), "ToolCart": (28.0, 0.0, 170.0), "CrateStack": (60.0, 0.0, -76.0),
    "Barrels": (58.0, 0.0, -8.0), "Mechanic": (83.0, 0.0, -51.0), "MechanicClipboard": (30.0, 0.0, 120.0),
    "AAGunBase": (-70.0, 0.0, -220.0),
}

# AA gun pivots (gun-local, Unity): the head yaws, the cradle pitches, the barrels recoil.
AA_HEAD = (0.0, 3.0, 0.0)
AA_CRADLE = (0.0, 3.2, 0.0)
AA_BARREL_X, AA_BARREL_Z = 0.45, 0.9
AA_MUZZLE = (0.0, 3.2, 4.5)
BEACON = (0.0, 25.3, 0.0)
RADAR = (0.0, 11.6, 0.0)
SOCK = (0.0, 9.8, 0.6)


# ---------------------------------------------------------------------------------------------- helpers


def rect(x0, z0, x1, z1):
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


def paint(p, poly, y, color):
    """A decal lying flat at height ``y``, facing up."""
    return quad(p, at(poly, y), color, (0.0, 1.0, 0.0))


def glow_quad(p, pts, color, normal):
    """A one-sided light on the glow material (unlit, no ink)."""
    f = p.face([p.vert(q) for q in pts], color, glow=True)
    f.normal_update()
    if f.normal.dot(to_blender(Vector(normal))) < 0:
        f.normal_flip()
    p.loose_faces.append(f)
    return f


def glow_box(p, center, size, color):
    cx, cy, cz = center
    sx, sy, sz = (s * 0.5 for s in size)
    poly = rect(cx - sx, cz - sz, cx + sx, cz + sz)
    return p.loft([at(poly, cy - sy), at(poly, cy + sy)], color, cap_start=color, cap_end=color, glow=True)


def box(p, x0, y0, z0, x1, y1, z1, color, top=None, outline=True):
    """An axis-aligned box from corner to corner."""
    return p.loft([at(rect(x0, z0, x1, z1), y0), at(rect(x0, z0, x1, z1), y1)], color, cap_start=color,
                  cap_end=top or color, outline=outline)


def band(p, poly, y, h, color, off=0.04):
    """A coloured band right round an upright mass, just off its sides (build_city's trim floats too far for props)."""
    for facet in facets(poly):
        wall_quad(p, facet, -facet[3], facet[3], y, y + h, color, off=off)


def solid_box(vis, col, x0, y0, z0, x1, y1, z1, color, top=None):
    box(vis, x0, y0, z0, x1, y1, z1, color, top)
    box(col, x0, y0, z0, x1, y1, z1, "metal", outline=False)


def solid_loft(vis, col, rings, color, cap_start, cap_end):
    vis.loft(rings, color, cap_start=cap_start, cap_end=cap_end)
    col.loft(rings, "metal", cap_start="metal", cap_end="metal", outline=False)


def zcyl(p, x, y, z0, z1, r, color, sides=8, cap=None, outline=True):
    """A cylinder lying along z (tanks, wheels turned 90 degrees use xcyl)."""
    return p.loft([ring(z0, r, sides, y, x0=x), ring(z1, r, sides, y, x0=x)], color, cap_start=cap or color,
                  cap_end=cap or color, outline=outline)


def xcyl(p, x0, x1, y, z, r, color, sides=8, cap=None, outline=True):
    """A cylinder lying along x: wheels."""
    rings = []
    for x in (x0, x1):
        rings.append([(x, y + r * math.sin(math.pi / sides + 2 * math.pi * k / sides),
                       z + r * math.cos(math.pi / sides + 2 * math.pi * k / sides)) for k in range(sides)])
    return p.loft(rings, color, cap_start=cap or color, cap_end=cap or color, outline=outline)


# ---------------------------------------------------------------------------------------------- runway


SEG_W, SEG_H, SEG_T = 7.0, 16.0, 2.2  # runway digits: cell width, height, stroke
SEGMENTS = {
    "a": (0, SEG_H - SEG_T, SEG_W, SEG_H), "b": (SEG_W - SEG_T, SEG_H / 2, SEG_W, SEG_H),
    "c": (SEG_W - SEG_T, 0, SEG_W, SEG_H / 2), "d": (0, 0, SEG_W, SEG_T), "e": (0, 0, SEG_T, SEG_H / 2),
    "f": (0, SEG_H / 2, SEG_T, SEG_H), "g": (0, SEG_H / 2 - SEG_T / 2, SEG_W, SEG_H / 2 + SEG_T / 2),
}
DIGITS = {"0": "abcdef", "3": "abcdg", "6": "acdefg", "8": "abcdefg", "9": "abcdfg", "2": "abdeg", "7": "abc"}


def runway_number(p, text, z0, heading):
    """Blocky 7-segment numbers read on the approach: ``heading`` +1 lands toward +z from ``z0``, -1 toward -z."""
    gap = 3.0
    total = len(text) * SEG_W + (len(text) - 1) * gap
    for i, ch in enumerate(text):
        u0 = -total / 2 + i * (SEG_W + gap)
        rects = ([(SEG_W / 2 - SEG_T / 2, 0, SEG_W / 2 + SEG_T / 2, SEG_H)] if ch == "1"
                 else [SEGMENTS[s] for s in DIGITS[ch]])
        for a0, b0, a1, b1 in rects:
            pts = [(heading * (u0 + a), z0 + heading * b) for a, b in ((a0, b0), (a1, b0), (a1, b1), (a0, b1))]
            paint(p, pts, PAINT, "white")


def chevrons(p, z_end, out):
    """Yellow chevrons on a blast pad pointing back at the threshold at ``z_end``; ``out`` is +1/-1 away from it."""
    for k in range(3):
        apex = z_end + out * (6.0 + k * 9.0)
        for s in (-1.0, 1.0):
            tip = apex + out * 7.0
            pts = [(0.0, apex), (s * 17.0, tip), (s * 17.0, tip + out * 1.8), (0.0, apex + out * 1.8)]
            paint(p, pts, 0.08 + 0.05, "sun")


def runway():
    p = Part("Runway")
    prism(p, rect(-HW, -HL, HW, HL), -0.6, SURFACE, "asphalt", outline=False)
    for s in (-1.0, 1.0):  # shoulders, a step down and lighter
        x0, x1 = sorted((s * HW, s * (HW + SHOULDER)))
        prism(p, rect(x0, -HL - 30.0, x1, HL + 30.0), -0.6, 0.06, "concrete", outline=False)
    for out in (-1.0, 1.0):  # blast pads past each end
        z0, z1 = sorted((out * HL, out * (HL + 30.0)))
        prism(p, rect(-HW, z0, HW, z1), -0.6, 0.08, "asphalt", outline=False)
        chevrons(p, out * HL, out)

    # Edge lines and the centreline (broken through the repair zone, whose paint says enough there).
    for s in (-1.0, 1.0):
        paint(p, rect(s * (HW - 1.0) - 0.45, -HL + 3.0, s * (HW - 1.0) + 0.45, HL - 3.0), PAINT, "white")
    z = -HL + 75.0
    while z < HL - 60.0:
        if not (ZONE_Z - ZONE_L / 2 - 10.0 < z < ZONE_Z + ZONE_L / 2 + 20.0):
            paint(p, rect(-0.45, z - 7.5, 0.45, z + 7.5), PAINT, "white")
        z += 30.0

    for end in (-1.0, 1.0):  # threshold piano keys at both ends: 4 bars either side of the centreline
        for s in (-1.0, 1.0):
            for i in range(4):
                x = s * (3.5 + i * 4.0)
                z0, z1 = sorted((end * (HL - 7.0), end * (HL - 37.0)))
                paint(p, rect(x - 1.0, z0, x + 1.0, z1), PAINT, "white")
    runway_number(p, "36", -HL + 41.0, 1.0)
    runway_number(p, "18", HL - 42.0, -1.0)
    for s in (-1.0, 1.0):  # touchdown zone bars and the big aiming-point blocks, approach end only (the far half is the repair zone)
        for i in range(3):
            x = s * (6.0 + i * 2.6)
            paint(p, rect(x - 0.8, -HL + 75.0, x + 0.8, -HL + 90.0), PAINT, "white")
        x0, x1 = sorted((s * 7.0, s * 12.0))
        paint(p, rect(x0, -HL + 125.0, x1, -HL + 170.0), PAINT, "white")

    lights(p)
    return p


def light_post(p, x, z, color, h=0.5):
    box(p, x - 0.12, 0.0, z - 0.12, x + 0.12, h, z + 0.12, "darkmetal", outline=False)
    glow_box(p, (x, h + 0.25, z), (0.55, 0.5, 0.55), color)


def lights(p):
    """Edge lights every 50 m, green threshold bars, an approach light run past each end, and a PAPI."""
    for s in (-1.0, 1.0):
        for k in range(13):
            light_post(p, s * (HW + 1.6), -HL + k * 50.0, "glint")
    for end in (-1.0, 1.0):
        for k in range(10):
            light_post(p, -18.0 + k * 4.0, end * (HL + 0.8), "leaf")
        for k in range(6):  # approach lights: a centre row, with a crossbar halfway
            z = end * (HL + 36.0 + k * 12.0)
            light_post(p, 0.0, z, "glint", h=1.0)
            if k == 2:
                box(p, -9.0, 1.0, z - 0.15, 9.0, 1.25, z + 0.15, "darkmetal", outline=False)
                for x in (-8.0, -4.0, 4.0, 8.0):
                    glow_box(p, (x, 1.5, z), (0.6, 0.5, 0.6), "glint")
    # PAPI: four boxes left of the touchdown zone, each showing white over red toward the approach.
    for k in range(4):
        x = -HW - 9.0 - k * 3.0
        z = -HL + 140.0
        box(p, x - 0.8, 0.5, z - 0.5, x + 0.8, 1.4, z + 0.5, "darkmetal", top="metal", outline=False)
        for dx in (-0.6, 0.6):
            box(p, x + dx - 0.08, 0.0, z - 0.08, x + dx + 0.08, 0.5, z + 0.08, "darkmetal", outline=False)
        glow_quad(p, [(x - 0.6, 0.95, z - 0.52), (x + 0.6, 0.95, z - 0.52), (x + 0.6, 1.3, z - 0.52), (x - 0.6, 1.3, z - 0.52)],
                  "glint", (0, 0, -1))
        glow_quad(p, [(x - 0.6, 0.6, z - 0.52), (x + 0.6, 0.6, z - 0.52), (x + 0.6, 0.95, z - 0.52), (x - 0.6, 0.95, z - 0.52)],
                  "red", (0, 0, -1))


# ---------------------------------------------------------------------------------------------- repair pad


def annulus(p, cx, cz, r_in, r_out, sectors, skip, y, color):
    """Paint ring (octagon by default) with some sectors left out."""
    for k in range(sectors):
        if k in skip:
            continue
        a0 = math.pi / sectors + 2 * math.pi * k / sectors
        a1 = a0 + 2 * math.pi / sectors
        pts = [(cx + r_in * math.cos(a0), cz + r_in * math.sin(a0)), (cx + r_out * math.cos(a0), cz + r_out * math.sin(a0)),
               (cx + r_out * math.cos(a1), cz + r_out * math.sin(a1)), (cx + r_in * math.cos(a1), cz + r_in * math.sin(a1))]
        paint(p, pts, y, color)


def repair_pad():
    """The zone's paint, relative to the zone (on the runway surface): a yellow border, hatched ends, a giant wrench."""
    p = Part("RepairPad")
    y = 0.1
    hw, hl, line = ZONE_W * 0.5, ZONE_L * 0.5, 1.5
    for s in (-1.0, 1.0):
        paint(p, rect(s * hw - line / 2, -hl - line / 2, s * hw + line / 2, hl + line / 2), y, "sun")
        paint(p, rect(-hw, s * hl - line / 2, hw, s * hl + line / 2), y, "sun")
    for s in (-1.0, 1.0):  # 45-degree hatching across each end
        z0, z1 = sorted((s * (hl - 2.0), s * (hl - 12.0)))
        x = -hw + 1.5
        while x + 10.0 + 2.2 < hw - 1.0:
            paint(p, [(x, z0), (x + 2.2, z0), (x + 12.2, z1), (x + 10.0, z1)], y, "sun")
            x += 5.0
    # The wrench, 60 m long down the middle: ring spanner at -z, open jaws at +z.
    paint(p, rect(-3.0, -22.0, 3.0, 19.0), y, "sun")
    annulus(p, 0.0, -28.0, 3.4, 7.0, 8, (), y, "sun")
    annulus(p, 0.0, 26.0, 4.0, 8.0, 8, (1, 2), y, "sun")
    return p


# ---------------------------------------------------------------------------------------------- apron


def apron():
    """Concrete between the runway's shoulder and the buildings, with yellow taxi lines (no ink, like the runway)."""
    p = Part("Apron")
    prism(p, rect_pts(104.0, 285.0, 10.0, HW + SHOULDER + 52.0, 42.5), -0.6, 0.04, "stone", outline=False)
    y = 0.09
    # From the runway to the hangar doors, then along the apron to the tower's stands.
    paint(p, rect(HW + SHOULDER, -40.3, 55.0, -39.7), y, "sun")
    paint(p, rect(39.7, -40.0, 40.3, 150.0), y, "sun")
    for z in (40.0, 85.0):  # two parking stands off the taxi line: line in, T to stop on
        paint(p, rect(40.0, z - 0.3, 52.0, z + 0.3), y, "sun")
        paint(p, rect(51.7, z - 4.0, 52.3, z + 4.0), y, "sun")
    # Hold-short bars where the line meets the runway: two solid, two dashed.
    for k, dashed in ((0, False), (1, False), (2, True), (3, True)):
        x = HW + SHOULDER + 1.0 + k * 0.9
        if dashed:
            for z0 in (-48.0, -43.0, -38.0, -33.0):
                paint(p, rect(x - 0.25, z0, x + 0.25, z0 + 3.0), y, "sun")
        else:
            paint(p, rect(x - 0.25, -48.0, x + 0.25, -32.0), y, "sun")
    # A safety border round the fuel tanks.
    for x0, z0, x1, z1 in ((101.0, -16.0, 119.0, -15.4), (101.0, 3.4, 119.0, 4.0), (101.0, -16.0, 101.6, 4.0),
                           (118.4, -16.0, 119.0, 4.0)):
        paint(p, rect(x0, z0, x1, z1), y, "sun")
    return p


# ---------------------------------------------------------------------------------------------- hangar


H_FRONT, H_BACK, H_SIDE, H_WALL, H_RISE = -20.0, 20.0, 16.0, 10.0, 4.8  # hangar: x extent, half width, wall, arch rise
ARCH_N = 8


def arch(r, rise, y0=H_WALL, n=ARCH_N):
    """(z, y) points over the hangar from +z to -z."""
    return [(r * math.cos(math.pi * k / n), y0 + rise * math.sin(math.pi * k / n)) for k in range(n + 1)]


def hangar():
    p = Part("Hangar", outline=0.18)
    col = Part("HangarCol")

    # Side walls with a clerestory of windows inside and out.
    for s in (-1.0, 1.0):
        z0, z1 = sorted((s * (H_SIDE - 0.5), s * (H_SIDE + 0.5)))
        solid_box(p, col, H_FRONT, -0.4, z0, H_BACK, H_WALL, z1, "body", top="white")
        for face, normal in ((s * (H_SIDE + 0.5 + 0.08), s), (s * (H_SIDE - 0.5 - 0.08), -s)):
            for k in range(6):
                x = -15.0 + k * 6.0
                quad(p, [(x - 2.0, 6.4, face), (x + 2.0, 6.4, face), (x + 2.0, 8.6, face), (x - 2.0, 8.6, face)],
                     "window", (0, 0, normal))
            quad(p, [(H_FRONT + 0.6, 0.0, face), (H_BACK - 0.6, 0.0, face), (H_BACK - 0.6, 1.2, face),
                     (H_FRONT + 0.6, 1.2, face)], "slate", (0, 0, normal))  # a dark plinth band

    # Back wall: a rectangle with the arch on top. Front: an arched header over the door opening, pillars beside it.
    back = [(H_SIDE + 0.5, -0.4)] + arch(H_SIDE + 0.4, H_RISE) + [(-H_SIDE - 0.5, -0.4)]
    solid_loft(p, col, [[(x, y, z) for z, y in back] for x in (H_BACK - 1.0, H_BACK)], "body", "body", "body")
    header = arch(H_SIDE + 0.4, H_RISE)
    solid_loft(p, col, [[(x, y, z) for z, y in header] for x in (H_FRONT - 0.4, H_FRONT + 0.6)], "body", "body", "body")
    fx = H_FRONT - 0.4 - 0.08  # the header's face: a team band over the doors
    quad(p, [(fx, H_WALL + 0.3, -12.0), (fx, H_WALL + 0.3, 12.0), (fx, H_WALL + 1.5, 12.0), (fx, H_WALL + 1.5, -12.0)],
         "team", (-1, 0, 0))
    for s in (-1.0, 1.0):
        z0, z1 = sorted((s * 13.0, s * (H_SIDE + 0.5)))
        solid_box(p, col, H_FRONT - 0.4, -0.4, z0, H_FRONT + 1.2, H_WALL, z1, "slate")

    # The roof: a curved shell of slabs (each one convex, so its end caps triangulate cleanly), team coloured outside,
    # metal under, white along the eaves and ends, with white ribs over it.
    outer, inner = arch(H_SIDE + 0.6, H_RISE + 0.6), arch(H_SIDE - 0.2, H_RISE - 0.2)
    for k in range(len(outer) - 1):
        slab = [outer[k], outer[k + 1], inner[k + 1], inner[k]]

        def roof_color(b, i, k=k):
            return "team" if i == 0 else "metal" if i == 2 else "white"

        solid_loft(p, col, [[(x, y, z) for z, y in slab] for x in (H_FRONT - 0.8, H_BACK + 0.8)], roof_color, "white", "white")
    for x in (-16.0, -8.0, 0.0, 8.0, 16.0):
        for (z0, y0), (z1, y1) in zip(outer, outer[1:]):
            nz, ny = y1 - y0, -(z1 - z0)  # outward: the segment turned a quarter, away from the arch's middle
            ln = math.hypot(nz, ny)
            nz, ny = nz / ln * 0.08, ny / ln * 0.08
            if nz * (z0 + z1) + ny * (y0 + y1 - 2 * H_WALL) < 0:
                nz, ny = -nz, -ny
            quad(p, [(x - 0.5, y0 + ny, z0 + nz), (x + 0.5, y0 + ny, z0 + nz), (x + 0.5, y1 + ny, z1 + nz),
                     (x - 0.5, y1 + ny, z1 + nz)], "white", (0, ny, nz))

    # Sliding doors, pushed open past the pillars, and their track across the front.
    for s in (-1.0, 1.0):
        z0, z1 = sorted((s * 15.0, s * 24.5))
        solid_box(p, col, H_FRONT - 1.3, 0.0, z0, H_FRONT - 0.8, H_WALL - 0.1, z1, "concrete")
        face = H_FRONT - 1.3 - 0.06
        for k in range(1, 4):  # panel seams
            z = z0 + k * (z1 - z0) / 4
            quad(p, [(face, 0.3, z - 0.12), (face, 0.3, z + 0.12), (face, H_WALL - 0.4, z + 0.12),
                     (face, H_WALL - 0.4, z - 0.12)], "metal", (-1, 0, 0))
        quad(p, [(face - 0.02, 4.2, z0 + 0.4), (face - 0.02, 4.2, z1 - 0.4), (face - 0.02, 5.2, z1 - 0.4),
                 (face - 0.02, 5.2, z0 + 0.4)], "team", (-1, 0, 0))
    box(p, H_FRONT - 1.5, H_WALL - 0.1, -25.5, H_FRONT - 0.6, H_WALL + 0.4, 25.5, "darkmetal", outline=0.05)

    hangar_inside(p)
    return [p, col]


def hangar_inside(p):
    """Floor paint, trusses, lamps and the workshop along the back wall (the main menu looks at all of it)."""
    back = H_BACK - 1.0
    paint(p, rect(H_FRONT + 1.2, -H_SIDE + 0.5, back, H_SIDE - 0.5), 0.1, "metal")
    paint(p, rect(-13.0, -0.25, 9.0, 0.25), 0.16, "sun")  # parking T under the plane
    paint(p, rect(-12.25, -4.0, -11.75, 4.0), 0.16, "sun")
    for k in range(13):  # hazard stripes across the doorway
        z = -12.0 + k * 2.0
        paint(p, [(H_FRONT + 1.3, z), (H_FRONT + 1.3, z + 1.0), (H_FRONT + 2.3, z + 2.0), (H_FRONT + 2.3, z + 1.0)], 0.16,
              "sun")

    # Arched trusses with a tie beam, under the roof.
    for x in (-14.0, -6.0, 2.0, 10.0, 17.0):
        pts = arch(H_SIDE - 0.6, H_RISE - 0.8)
        for (z0, y0), (z1, y1) in zip(pts, pts[1:]):
            beam(p, (x, y0, z0), (x, y1, z1), 0.22, "darkmetal")
        beam(p, (x, H_WALL - 0.2, -H_SIDE + 0.6), (x, H_WALL - 0.2, H_SIDE - 0.6), 0.18, "darkmetal")
        beam(p, (x, H_WALL - 0.2, 0.0), (x, H_WALL + H_RISE - 0.9, 0.0), 0.14, "darkmetal")

    # Lamps (Unity hangs point lights just under them).
    for x in (H_FRONT + 8.0, 0.0, H_BACK - 8.0):
        beam(p, (x, H_WALL + 0.4, 0.0), (x, H_WALL + H_RISE - 0.8, 0.0), 0.05, "darkmetal")
        box(p, x - 0.8, H_WALL + 0.2, -0.8, x + 0.8, H_WALL + 0.6, 0.8, "darkmetal", outline=0.04)
        glow_quad(p, [(x - 0.65, H_WALL + 0.18, -0.65), (x + 0.65, H_WALL + 0.18, -0.65), (x + 0.65, H_WALL + 0.18, 0.65),
                      (x - 0.65, H_WALL + 0.18, 0.65)], "flame", (0, -1, 0))

    # Team flag with a roundel on the back wall, a pegboard over the bench.
    wall = back - 0.06
    quad(p, [(wall, 3.6, -5.0), (wall, 3.6, 5.0), (wall, 9.4, 5.0), (wall, 9.4, -5.0)], "team", (-1, 0, 0))
    roundel(p, (wall - 0.01, 6.5, 0.0), (-1, 0, 0), outer=2.2, inner=1.2)
    quad(p, [(wall, 1.5, -11.8), (wall, 1.5, -4.2), (wall, 3.4, -4.2), (wall, 3.4, -11.8)], "drywood", (-1, 0, 0))
    for k, (z, h) in enumerate(((-11.0, 1.2), (-10.0, 1.4), (-8.6, 1.0), (-7.4, 1.3), (-6.0, 1.1), (-5.0, 0.9))):
        quad(p, [(wall - 0.03, 2.4 - h / 2, z - 0.15), (wall - 0.03, 2.4 - h / 2, z + 0.15),
                 (wall - 0.03, 2.4 + h / 2, z + 0.15), (wall - 0.03, 2.4 + h / 2, z - 0.15)],
             "darkmetal" if k % 2 else "red", (-1, 0, 0))

    # Workbench with a vice, a red tool chest, crates, oil drums, a tyre stack.
    block(p, (back - 1.2, 0.9, -8.0), (1.6, 0.15, 7.0), "drywood", c=0.1, ink=0.04)
    for z in (-11.0, -5.0):
        block(p, (back - 1.2, 0.45, z), (1.2, 0.9, 0.2), "darkmetal", c=0.05, ink=0.03)
    block(p, (back - 1.3, 1.15, -10.5), (0.5, 0.35, 0.4), "darkmetal", c=0.08, ink=0.03)
    block(p, (back - 1.0, 0.75, -3.0), (1.2, 1.5, 2.0), "red", c=0.15, ink=0.04)
    for k in range(4):
        y = 0.35 + k * 0.32
        quad(p, [(back - 1.62, y, -3.8), (back - 1.62, y, -2.2), (back - 1.62, y + 0.06, -2.2), (back - 1.62, y + 0.06, -3.8)],
             "darkmetal", (-1, 0, 0))
    for (x, y, z, s) in ((back - 2.0, 0.75, 9.0, 1.5), (back - 2.0, 2.1, 9.3, 1.2), (back - 4.0, 0.6, 11.0, 1.2)):
        block(p, (x, y, z), (s, s, s), "drywood", top="bark", c=0.12, ink=0.04)
    for (x, z, color) in ((H_FRONT + 6.0, H_SIDE - 2.5, "red"), (H_FRONT + 7.3, H_SIDE - 2.2, "sun"),
                          (H_FRONT + 6.5, H_SIDE - 3.8, "olive")):
        cylinder(p, x, z, 0.0, 0.95, 0.42, color, sides=8, ink=0.03)
        band(p, ngon_pts(0.42, 8, x, z), 0.3, 0.06, "darkmetal")
        band(p, ngon_pts(0.42, 8, x, z), 0.65, 0.06, "darkmetal")
    for k in range(3):
        cylinder(p, back - 3.0, -H_SIDE + 3.0, k * 0.4, k * 0.4 + 0.38, 0.55, "tire", sides=8, ink=0.03)


# ---------------------------------------------------------------------------------------------- control tower


def control_tower():
    p = Part("ControlTower", outline=0.14)
    col = Part("ControlTowerCol")
    ops = rect_pts(14.0, 10.0, 0.6, 9.5, 0.0)
    solid_loft(p, col, [at(ops, -0.5), at(ops, 7.5)], "body", "body", "metal")
    windows(p, ops, 1.0, 7.0, "window", floor=3.2, frac=0.5, cols=4, margin=1.0, gap=0.3)
    band(p, ops, 6.4, 0.6, "team")
    paint(p, rect_pts(12.6, 8.6, 0.4, 9.5, 0.0), 7.5 + 0.05, "darkmetal")
    block(p, (12.0, 8.2, 2.0), (2.4, 1.4, 1.8), "metal", top="concrete", ink=0.05)  # AC unit

    shaft = rect_pts(6.5, 6.5, 0.8)
    solid_loft(p, col, [at(shaft, -0.5), at(shaft, 20.5)], "body", "body", "body")
    windows(p, shaft, 8.0, 18.0, "window", floor=3.4, frac=0.3, margin=1.2, min_half=2.0)
    band(p, shaft, 18.6, 1.0, "team")

    deck = rect_pts(10.4, 10.4, 1.3)
    solid_loft(p, col, [at(deck, 20.5), at(deck, 21.3)], "white", "white", "white")
    cab = [at(rect_pts(8.4, 8.4, 1.0), 21.3), at(rect_pts(10.0, 10.0, 1.2), 24.6)]
    p.loft(cab, "glass", cap_start="glass", cap_end="glass")
    col.loft([at(rect_pts(10.0, 10.0, 1.2), 21.3), at(rect_pts(10.0, 10.0, 1.2), 24.6)], "metal", cap_start="metal",
             cap_end="metal", outline=False)
    # Mullions at the cab's corners, leaning out with the glass.
    for (x0, z0), (x1, z1) in zip(rect_pts(8.4, 8.4, 1.0), rect_pts(10.0, 10.0, 1.2)):
        beam(p, (x0 * 1.01, 21.3, z0 * 1.01), (x1 * 1.01, 24.6, z1 * 1.01), 0.09, "darkmetal")
    roof = rect_pts(11.2, 11.2, 1.4)
    solid_loft(p, col, [at(roof, 24.6), at(roof, 25.3)], "white", "white", "metal")
    beam(p, (4.0, 25.3, 4.0), (4.0, 31.0, 4.0), 0.1, "metal", ink=0.04)
    block(p, (4.0, 31.2, 4.0), (0.45, 0.45, 0.45), "red", c=0.08, ink=0.04)
    beam(p, (-4.2, 25.3, 3.8), (-4.2, 28.0, 3.8), 0.07, "metal")
    beam(p, (-4.2, 27.7, 3.0), (-4.2, 27.7, 4.6), 0.05, "metal")
    return [p, col]


def tower_beacon():
    """The rotating airfield beacon: a lamp housing with a green and a white lens back to back."""
    p = Part("ControlTowerBeacon", BEACON, outline=0.05)
    bx, by, bz = BEACON
    cylinder(p, bx, bz, by, by + 0.8, 0.35, "darkmetal", sides=6, ink=0.04)
    block(p, (bx, by + 1.3, bz), (1.2, 0.9, 1.0), "darkmetal", top="metal", c=0.15, ink=0.05)
    for s, color in ((1.0, "leaf"), (-1.0, "glint")):
        z = bz + s * 0.56
        glow_quad(p, [(bx - 0.45, by + 0.95, z), (bx + 0.45, by + 0.95, z), (bx + 0.45, by + 1.65, z), (bx - 0.45, by + 1.65, z)],
                  color, (0, 0, s))
    return p


# ---------------------------------------------------------------------------------------------- AA gun


def aa_gun():
    """Bofors-ish twin AA gun in a sandbag ring: base, yawing head, pitching cradle, recoiling barrels."""
    base = Part("AAGunBase", outline=0.08)
    prism(base, ngon_pts(4.6, 12), -0.4, 0.25, "concrete", outline=0.08)
    # Sandbag ring, open at the back (-z), its top stepped bag by bag.
    r_in, r_out, steps = 3.2, 4.2, 18
    a0, a1 = math.radians(-60.0), math.radians(240.0)
    rings = []
    for k in range(steps + 1):
        a = a0 + (a1 - a0) * k / steps
        h = 1.35 if k % 2 == 0 else 1.2
        sec = [(r_in, 0.25), (r_out, 0.25), (r_out - 0.18, 0.25 + h * 0.55), (r_out - 0.4, 0.25 + h),
               (r_in + 0.3, 0.25 + h), (r_in + 0.12, 0.25 + h * 0.55)]
        rings.append([(r * math.cos(a), y, r * math.sin(a)) for r, y in sec])
    # the ring runs from -60 to 240 degrees: in Unity x = cos, z = sin, so the gap faces -z (behind the gun)
    base.loft(rings, "khaki", cap_start="khaki", cap_end="khaki")
    for k in range(steps):  # seams between bags on the outside
        a = a0 + (a1 - a0) * (k + 0.5) / steps
        c, s = math.cos(a), math.sin(a)
        r = r_out + 0.04
        quad(base, [(r * c - 0.05 * s, 0.35, r * s + 0.05 * c), (r * c + 0.05 * s, 0.35, r * s - 0.05 * c),
                    ((r - 0.18) * c + 0.05 * s, 0.95, (r - 0.18) * s - 0.05 * c),
                    ((r - 0.18) * c - 0.05 * s, 0.95, (r - 0.18) * s + 0.05 * c)], "drywood", (c, 0, s))
    cylinder(base, 0.0, 0.0, 0.25, 0.55, 1.1, "darkmetal", sides=8, ink=0.05)
    cylinder(base, 0.0, 0.0, 0.55, 2.6, 0.7, "olive", sides=8, ink=0.06)
    for x, z in ((-2.2, -1.2), (2.2, -1.4)):  # ammo boxes inside the ring
        block(base, (x, 0.6, z), (0.8, 0.7, 1.2), "olive", top="khaki", c=0.08, ink=0.04)

    head = Part("AAGunHead", AA_HEAD, outline=0.06)
    hy = AA_HEAD[1]
    cylinder(head, 0.0, 0.0, hy - 0.4, hy - 0.15, 1.25, "darkmetal", sides=10, ink=0.05)
    for s in (-1.0, 1.0):  # carriage side plates either side of the cradle
        x0, x1 = sorted((s * 0.75, s * 0.95))
        head.loft([at([(x0, -0.8), (x1, -0.8), (x1, 0.7), (x0, 0.7)], hy - 0.15),
                   at([(x0, -0.4), (x1, -0.4), (x1, 0.35), (x0, 0.35)], hy + 0.55)], "olive", cap_start="olive",
                  cap_end="olive")
    shield = [at([(-1.35, 1.0), (1.35, 1.0), (1.45, 1.25), (-1.45, 1.25)], hy - 0.2),
              at([(-1.1, 1.0), (1.1, 1.0), (1.2, 1.2), (-1.2, 1.2)], hy + 0.45)]
    head.loft(shield, "olive", cap_start="olive", cap_end="olive")
    for s in (-1.0, 1.0):  # seats either side behind the breech: aimer and layer
        block(head, (s * 1.25, hy + 0.05, -0.9), (0.5, 0.12, 0.5), "tire", c=0.06, ink=0.03)
        beam(head, (s * 0.95, hy - 0.15, -0.6), (s * 1.25, hy, -0.9), 0.05, "darkmetal")
        beam(head, (s * 1.25, hy, -1.15), (s * 1.25, hy + 0.55, -1.2), 0.05, "darkmetal")
        block(head, (s * 1.25, hy + 0.4, -0.75), (0.08, 0.35, 0.35), "darkmetal", c=0.02, ink=0.02)  # sight / handwheel

    cradle = Part("AAGunCradle", AA_CRADLE, outline=0.05)
    cy = AA_CRADLE[1]
    block(cradle, (0.0, cy, 0.15), (1.35, 0.6, 1.5), "darkmetal", top="olive", c=0.12, ink=0.05)
    for s in (-1.0, 1.0):
        zcyl(cradle, s * AA_BARREL_X, cy, 0.9, 2.1, 0.17, "olive", sides=8)  # jackets the barrels slide in
        block(cradle, (s * AA_BARREL_X, cy + 0.42, 0.0), (0.22, 0.28, 0.4), "sun", top="sun", c=0.04, ink=0.03)  # clip
    zcyl(cradle, 0.0, cy, -0.75, -0.35, 0.28, "darkmetal", sides=6)  # trunnion

    barrels = []
    for name, s in (("AAGunBarrelLeft", -1.0), ("AAGunBarrelRight", 1.0)):
        b = Part(name, (s * AA_BARREL_X, cy, AA_BARREL_Z), outline=0.035)
        x = s * AA_BARREL_X
        zcyl(b, x, cy, AA_BARREL_Z, 4.05, 0.1, "darkmetal", sides=6)
        b.loft([ring(4.05, 0.13, 6, cy, x0=x), ring(4.45, 0.17, 6, cy, x0=x)], "char", cap_start="char", cap_end="char")
        barrels.append(b)
    return [base, head, cradle] + barrels


# ---------------------------------------------------------------------------------------------- radar, windsock


def radar():
    mast = Part("RadarMast", outline=0.06)
    block(mast, (0.0, 1.3, 0.0), (3.4, 2.6, 3.0), "olive", top="khaki", c=0.3, ink=0.06)
    quad(mast, [(-0.6, 0.0, -1.52), (0.6, 0.0, -1.52), (0.6, 2.0, -1.52), (-0.6, 2.0, -1.52)], "darkmetal", (0, 0, -1))
    top = RADAR[1] - 0.6
    legs = [(-1.3, -1.2), (1.3, -1.2), (1.3, 1.2), (-1.3, 1.2)]
    for x, z in legs:
        beam(mast, (x, 2.6, z), (x * 0.35, top, z * 0.35), 0.1, "white")
    def leg(x, z, y):
        t = (y - 2.6) / (top - 2.6)
        return (x * (1 - 0.65 * t), y, z * (1 - 0.65 * t))

    for y0, y1 in ((2.6, 6.0), (6.0, top)):  # cross braces, zigzagging up between neighbouring legs
        for (xa, za), (xb, zb) in zip(legs, legs[1:] + legs[:1]):
            beam(mast, leg(xa, za, y0), leg(xb, zb, y1), 0.06, "white")
    block(mast, (0.0, top + 0.2, 0.0), (1.6, 0.4, 1.6), "darkmetal", c=0.2, ink=0.04)

    dish = Part("RadarDish", RADAR, outline=0.05)
    rx, ry, rz = RADAR
    cylinder(dish, rx, rz, ry - 0.4, ry + 0.2, 0.45, "darkmetal", sides=6, ink=0.04)
    block(dish, (rx, ry + 0.4, rz - 0.3), (0.5, 0.6, 1.4), "darkmetal", c=0.1, ink=0.04)
    # The reflector: a wide strip curved side to side, tipped back, white in front and grey behind.
    sections = []
    for k in range(7):
        x = -3.6 + k * 1.2
        bow = 0.06 * x * x
        sections.append([(x, ry + 0.4 + y, rz + 0.5 + bow - y * 0.25 + t) for y, t in ((-1.5, 0.0), (1.6, 0.0), (1.6, -0.14),
                                                                                         (-1.5, -0.14))])
    dish.loft(sections, lambda b, s: "metal" if s == 2 else "white", cap_start="white", cap_end="white")
    beam(dish, (rx, ry + 0.2, rz + 0.5), (rx, ry - 0.2, rz + 2.6), 0.07, "darkmetal")  # feed horn arm
    block(dish, (rx, ry - 0.25, rz + 2.7), (0.35, 0.35, 0.45), "red", c=0.06, ink=0.03)
    return [mast, dish]


def windsock():
    pole = Part("WindsockPole", outline=0.05)
    block(pole, (0.0, 0.2, 0.0), (1.4, 0.4, 1.4), "concrete", c=0.2, ink=0.05)
    rings = [[(x, 0.4 + k * 1.9, y) for x, y, _ in ring(0.0, 0.16, 6)] for k in range(6)]
    pole.loft(rings, lambda b, s: "red" if b % 2 else "white", cap_start="white", cap_end="red")
    beam(pole, (0.0, SOCK[1], 0.0), (0.0, SOCK[1], SOCK[2] - 0.05), 0.06, "darkmetal")

    sock = Part("Windsock", SOCK, outline=0.05)
    sx, sy, sz = SOCK
    rings = []
    for k in range(6):
        t = k / 5.0
        r = 0.75 - 0.38 * t
        rings.append(ring(sz + t * 5.5, r, 8, sy - 0.75 - 0.5 * t * t, x0=sx))  # hangs from the hoop's top
    sock.loft(rings, lambda b, s: "orange" if b % 2 == 0 else "white", cap_start="orange", cap_end="white")
    return [pole, sock]


# ---------------------------------------------------------------------------------------------- props


def fuel_tank():
    p = Part("FuelTank", outline=0.08)
    prism(p, ngon_pts(3.8, 12), -0.3, 0.3, "concrete", outline=0.06)
    cylinder(p, 0.0, 0.0, 0.3, 7.0, 3.2, "white", sides=12, top="metal", cone=0.9, ink=0.08)
    band(p, ngon_pts(3.2, 12), 5.6, 0.6, "red")
    band(p, ngon_pts(3.2, 12), 1.0, 0.3, "red")
    for z in (-0.35, 0.35):  # ladder up the side facing +x
        beam(p, (3.32, 0.3, z), (3.32, 7.0, z), 0.05, "darkmetal")
    for k in range(10):
        y = 0.9 + k * 0.62
        beam(p, (3.32, y, -0.35), (3.32, y, 0.35), 0.03, "darkmetal")
    for k in range(10):  # railing round the top
        a = 2 * math.pi * k / 10
        beam(p, (3.0 * math.cos(a), 7.0, 3.0 * math.sin(a)), (3.0 * math.cos(a), 7.9, 3.0 * math.sin(a)), 0.04, "darkmetal")
    pts = ngon_pts(3.0, 10)
    for (xa, za), (xb, zb) in zip(pts, pts[1:] + pts[:1]):
        beam(p, (xa, 7.9, za), (xb, 7.9, zb), 0.04, "darkmetal")
    return p


def fuel_truck():
    """Toy refueller, nose +z: red cab, white tank with a red band, six wheels."""
    p = Part("FuelTruck", outline=0.06)
    block(p, (0.0, 0.95, 0.0), (2.4, 0.5, 9.0), "darkmetal", c=0.1, ink=0.05)
    block(p, (0.0, 2.15, 3.3), (2.5, 2.0, 2.3), "red", top="red", c=0.3, ink=0.06)
    quad(p, [(-1.0, 2.2, 4.48), (1.0, 2.2, 4.48), (1.0, 3.0, 4.48), (-1.0, 3.0, 4.48)], "window", (0, 0, 1))
    for s in (-1.0, 1.0):
        quad(p, [(s * 1.28, 2.2, 2.8), (s * 1.28, 2.2, 4.0), (s * 1.28, 3.0, 4.0), (s * 1.28, 3.0, 2.8)], "window", (s, 0, 0))
    p.loft([ring(-4.3, 1.1, 10, 2.35), ring(-4.0, 1.25, 10, 2.35), ring(1.6, 1.25, 10, 2.35), ring(1.9, 1.1, 10, 2.35)],
           "white", cap_start="white", cap_end="white")
    p.loft([ring(-1.6, 1.29, 10, 2.35), ring(-1.0, 1.29, 10, 2.35)], "red", outline=False)
    for z in (-3.3, -1.9, 3.3):
        for s in (-1.0, 1.0):
            xcyl(p, s * 1.0, s * 1.35, 0.55, z, 0.55, "tire", sides=8, cap="metal", outline=False)
    beam(p, (-0.8, 3.65, -3.5), (0.8, 3.65, -3.5), 0.06, "darkmetal")  # hose reel on the back
    xcyl(p, -0.6, 0.6, 1.5, -4.75, 0.4, "sun", sides=8, outline=0.03)
    return p


def repair_crane():
    """A yellow mobile gantry, its beam along z, a hook hanging off a trolley."""
    p = Part("RepairCrane", outline=0.06)
    for z in (-4.5, 4.5):
        for s in (-1.0, 1.0):
            beam(p, (s * 1.6, 0.7, z), (0.0, 7.6, z), 0.16, "sun", ink=0.05)
        block(p, (0.0, 0.55, z), (3.8, 0.4, 0.6), "sun", c=0.1, ink=0.05)
        for s in (-1.0, 1.0):
            xcyl(p, s * 1.75, s * 1.95, 0.35, z, 0.35, "tire", sides=6, outline=False)
        beam(p, (-0.8, 3.6, z), (0.8, 3.6, z), 0.1, "sun")
    block(p, (0.0, 7.9, 0.0), (0.7, 0.7, 10.6), "sun", c=0.12, ink=0.06)
    for k in range(6):  # hazard stripes at the beam's ends
        z = (-5.2 + k * 0.5) if k < 3 else (3.7 + (k - 3) * 0.5)
        for s in (-1.0, 1.0):
            quad(p, [(s * 0.37, 7.56, z), (s * 0.37, 7.56, z + 0.25), (s * 0.37, 8.24, z + 0.5), (s * 0.37, 8.24, z + 0.25)],
                 "char", (s, 0, 0))
    block(p, (0.0, 7.2, 1.0), (0.9, 0.6, 1.0), "darkmetal", c=0.1, ink=0.04)
    beam(p, (0.0, 6.9, 1.0), (0.0, 3.4, 1.0), 0.04, "darkmetal")
    block(p, (0.0, 3.2, 1.0), (0.35, 0.4, 0.2), "red", c=0.05, ink=0.03)
    return p


def tool_cart():
    p = Part("ToolCart", outline=0.04)
    block(p, (0.0, 0.55, 0.0), (1.2, 0.12, 2.0), "darkmetal", c=0.05, ink=0.03)
    for x in (-0.55, 0.55):
        for z in (-0.75, 0.75):
            xcyl(p, x - 0.05, x + 0.05, 0.22, z, 0.22, "tire", sides=6, outline=False)
    block(p, (0.0, 1.05, -0.2), (1.0, 0.9, 1.3), "red", top="red", c=0.1, ink=0.03)
    for k in range(3):
        y = 0.75 + k * 0.28
        quad(p, [(-0.4, y, 0.46), (0.4, y, 0.46), (0.4, y + 0.05, 0.46), (-0.4, y + 0.05, 0.46)], "darkmetal", (0, 0, 1))
    beam(p, (-0.5, 0.6, -1.0), (-0.5, 1.4, -1.25), 0.04, "darkmetal")
    beam(p, (0.5, 0.6, -1.0), (0.5, 1.4, -1.25), 0.04, "darkmetal")
    beam(p, (-0.5, 1.4, -1.25), (0.5, 1.4, -1.25), 0.04, "darkmetal")
    xcyl(p, -0.3, 0.3, 0.9, 0.7, 0.3, "sun", sides=8, outline=0.02)  # air hose reel
    block(p, (0.1, 1.55, -0.3), (0.12, 0.06, 0.9), "metal", c=0.02, ink=0.02)  # a big spanner on top
    block(p, (0.1, 1.55, 0.18), (0.32, 0.06, 0.22), "metal", c=0.04, ink=0.02)
    return p


def crate_stack():
    p = Part("CrateStack", outline=0.05)
    for (x, y, z, w, h, d) in ((0.0, 0.8, 0.0, 1.6, 1.6, 1.6), (1.8, 0.8, 0.2, 1.6, 1.6, 1.6), (0.9, 2.4, 0.1, 1.6, 1.6, 1.6),
                               (-0.6, 0.45, 1.9, 3.2, 0.9, 1.1)):
        block(p, (x, y, z), (w, h, d), "olive", top="khaki", c=0.1, ink=0.04)
        quad(p, [(x - w * 0.3, y - h * 0.15, z - d / 2 - 0.03), (x + w * 0.3, y - h * 0.15, z - d / 2 - 0.03),
                 (x + w * 0.3, y + h * 0.15, z - d / 2 - 0.03), (x - w * 0.3, y + h * 0.15, z - d / 2 - 0.03)], "white", (0, 0, -1))
    return p


def barrels():
    p = Part("Barrels", outline=0.04)
    for x, z, color in ((0.0, 0.0, "red"), (0.95, 0.15, "sun"), (0.45, 0.9, "olive"), (-0.6, 0.85, "blue"), (1.3, 1.0, "red")):
        cylinder(p, x, z, 0.0, 1.15, 0.42, color, sides=8, top="darkmetal", ink=0.03)
        band(p, ngon_pts(0.42, 8, x, z), 0.35, 0.07, "darkmetal")
        band(p, ngon_pts(0.42, 8, x, z), 0.8, 0.07, "darkmetal")
    p.loft([ring(-0.6, 0.42, 8, 0.42, x0=-0.4 + 0.0), ring(0.55, 0.42, 8, 0.42, x0=-0.4)], "red", cap_start="darkmetal",
           cap_end="darkmetal", outline=0.03)  # one on its side
    return p


def mechanic(name, pose):
    """A chunky ground crew figure, 2 m tall, facing +z: orange overalls, red cap, a spanner or a clipboard."""
    p = Part(name, outline=0.035)
    for s in (-1.0, 1.0):
        block(p, (s * 0.17, 0.1, 0.05), (0.24, 0.2, 0.42), "tire", c=0.04, ink=0.02)
        block(p, (s * 0.17, 0.6, 0.0), (0.26, 0.85, 0.3), "orange", c=0.06, ink=0.025)
    block(p, (0.0, 1.38, 0.0), (0.66, 0.78, 0.4), "orange", c=0.1, ink=0.03)
    block(p, (0.0, 1.06, 0.0), (0.68, 0.1, 0.42), "darkmetal", c=0.04, ink=0.02)  # belt
    block(p, (0.0, 1.95, 0.0), (0.38, 0.4, 0.38), "sand", c=0.08, ink=0.025)
    block(p, (0.0, 2.17, 0.0), (0.42, 0.12, 0.42), "red", c=0.06, ink=0.02)
    block(p, (0.0, 2.13, 0.27), (0.36, 0.05, 0.2), "red", c=0.03, ink=0.015)
    if pose == "wrench":  # right arm up, spanner in hand; left arm down
        beam(p, (0.4, 1.65, 0.0), (0.62, 2.15, 0.25), 0.09, "orange")
        block(p, (0.64, 2.2, 0.28), (0.14, 0.14, 0.14), "sand", c=0.03, ink=0.015)
        block(p, (0.64, 2.5, 0.3), (0.07, 0.5, 0.05), "metal", c=0.01, ink=0.015)
        block(p, (0.64, 2.75, 0.3), (0.18, 0.12, 0.05), "metal", c=0.02, ink=0.015)
        beam(p, (-0.4, 1.65, 0.0), (-0.45, 1.05, 0.05), 0.09, "orange")
        block(p, (-0.46, 0.98, 0.06), (0.14, 0.14, 0.14), "sand", c=0.03, ink=0.015)
    else:  # both forearms forward holding a clipboard
        for s in (-1.0, 1.0):
            beam(p, (s * 0.4, 1.65, 0.0), (s * 0.38, 1.25, 0.1), 0.09, "orange")
            beam(p, (s * 0.38, 1.25, 0.1), (s * 0.18, 1.35, 0.42), 0.08, "orange")
        block(p, (0.0, 1.45, 0.48), (0.42, 0.55, 0.04), "drywood", c=0.02, ink=0.015)
        quad(p, [(-0.16, 1.24, 0.505), (0.16, 1.24, 0.505), (0.16, 1.64, 0.505), (-0.16, 1.64, 0.505)], "white", (0, 0, 1))
    return p


# ---------------------------------------------------------------------------------------------- build


def glow_material():
    """Unlit palette, two-sided: Unity's Weapon_Glow. Emissive here so the previews read like the game."""
    m = make_material("Weapon_Glow", (1, 1, 1), bpy.data.images[os.path.basename(PALETTE_PATH)], cull=False)
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 1.0
    return m


def build():
    model = Model("Airfield")
    materials = model.materials + [glow_material()]
    parts = [runway(), repair_pad(), apron()] + hangar() + control_tower() + [tower_beacon()] + aa_gun() + radar()
    parts += windsock() + [fuel_tank(), fuel_truck(), repair_crane(), tool_cart(), crate_stack(), barrels(),
                           mechanic("Mechanic", "wrench"), mechanic("MechanicClipboard", "clipboard")]
    for part in parts:
        obj = part.finish(model.collection, materials)
        drop_unused_materials(obj)
        model.objects.append(obj)
    model.mark("AAGunMuzzle", AA_MUZZLE)
    model.export(FBX_PATH, BLEND_PATH)


# ---------------------------------------------------------------------------------------------- previews


def lay_out():
    for o in [o for o in bpy.data.objects if o.name.endswith("Col")]:
        bpy.data.objects.remove(o)
    pivots = {"ControlTowerBeacon": ("ControlTower", BEACON), "Windsock": ("WindsockPole", SOCK),
              "RadarDish": ("RadarMast", RADAR), "AAGunHead": ("AAGunBase", AA_HEAD),
              "AAGunCradle": ("AAGunBase", AA_CRADLE),
              "AAGunBarrelLeft": ("AAGunBase", (-AA_BARREL_X, AA_CRADLE[1], AA_BARREL_Z)),
              "AAGunBarrelRight": ("AAGunBase", (AA_BARREL_X, AA_CRADLE[1], AA_BARREL_Z))}
    for name, pos in LAYOUT.items():
        bpy.data.objects[name].location = to_blender(pos)
    for name, (parent, pivot) in pivots.items():
        base = LAYOUT[parent]
        bpy.data.objects[name].location = to_blender(tuple(a + b for a, b in zip(base, pivot)))
    bpy.data.objects["RepairPad"].location = to_blender((0.0, SURFACE, ZONE_Z))
    bpy.data.objects["Windsock"].rotation_euler = (0.0, 0.0, math.radians(35.0))


SHOTS = [
    ("airfield_overview", (-260.0, 260.0, -480.0), (40.0, 0.0, -20.0), None),
    ("airfield_top", (40.0, 900.0, 0.01), (40.0, 0.0, 0.0), 760.0),
    ("airfield_buildings", (10.0, 45.0, -140.0), (75.0, 4.0, 0.0), None),
    ("airfield_hangar", (25.0, 9.0, -70.0), (75.0, 6.0, -40.0), None),
    ("airfield_hangar_inside", (60.0, 4.0, -33.0), (93.0, 3.0, -44.0), None),
    ("airfield_tower", (35.0, 18.0, 92.0), (72.0, 13.0, 125.0), None),
    ("airfield_aa", (-62.0, 6.0, -232.0), (-70.0, 2.4, -220.0), None),
    ("airfield_props", (80.0, 12.0, 30.0), (104.0, 2.0, -2.0), None),
    ("airfield_repair", (-45.0, 45.0, 60.0), (5.0, 0.0, 140.0), None),
    ("airfield_approach", (0.0, 30.0, -470.0), (0.0, 0.0, -250.0), None),
]


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
