"""
Builds the course markers and range targets (Design/03-Art/final-asset-list.md, Pack 7: the pieces the scene builders
place rather than the airfield prefab), then exports them for Unity.

    blender --background --factory-startup --python ArtSource/Markers/build_markers.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Markers/Markers.fbx      one object per asset, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png       the shared palette atlas (rewritten, same as the planes')
    ArtSource/Markers/Markers.blend                     the generated scene, for hand tweaks and renders

Objects (Unity space, metres):
    PylonBase          approach-lane gate pylon's footing, origin on the ground (reaches 3 m down, so it stands in the sea too)
    PylonPole          a plain white pole 1 m tall, origin at its foot: IslandSceneBuilder stretches it up to the top piece
                       (no caps, so its ink stays a ring when stretched)
    PylonTop           orange/white obstruction sleeve and the lane marker, origin on the gate point (the diamond's
                       middle). The diamond is on the glow material in the neutral team swatch: the builder swaps that
                       slot for a lane-coloured copy (magenta, cyan, lime, ...)
    Buoy               boundary buoy: orange float with a white band, a cage tower, a lantern and an X topmark, origin on
                       the waterline. BuoyLamp is its red lens (glow), origin on its pivot (0, 21.5, 0), so BoundaryBuoys
                       can blink it alone
    BoundaryPost       the buoy on land: a concrete footing (reaches 3 m down, so it sits into a slope), a banded mast
                       and the buoy's lantern top, origin on the ground; it takes BuoyLamp on the same pivot
    Ring               flight-test ring, radius 22 m round its local z, the tube in the team swatch (RingGate tints it
                       idle / passed) with white bands; RingCol is the plain tube for its mesh collider
    TargetTruck        olive army truck, nose +z, with a bullseye on its canvas, origin on the ground
    TargetRadar        olive radar post with a lattice mast on its roof; TargetRadarDish turns on top (origin (0, 14, 0))

Everything is on the shared plane materials (Plane_Paint, Plane_Team, Plane_Ink, Weapon_Glow) and palette.
"""
import math
import os
import sys

import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Nature"))
sys.path.insert(0, os.path.join(HERE, "..", "City"))
sys.path.insert(0, os.path.join(HERE, "..", "Airfield"))
from plane_kit import MODELS_DIR, Model, Part, preview_arg, render_previews, to_blender  # noqa: E402
from build_nature import drop_unused_materials  # noqa: E402
from build_city import at, beam, block, cylinder, ngon_pts, prism, quad, rect_pts, windows  # noqa: E402
from build_airfield import glow_material, xcyl  # noqa: E402

FBX_PATH = os.path.join(MODELS_DIR, "Markers", "Markers.fbx")
BLEND_PATH = os.path.join(HERE, "Markers.blend")

# Pylon (gate point = the PylonTop origin; the sleeve's foot is where the stretched pole meets it).
POLE_R = 0.75
SLEEVE_BOTTOM = -16.0
LAMP = (0.0, 21.5, 0.0)  # buoy lamp pivot
RING_R, TUBE_R = 22.0, 1.3  # RingGate's radius and the rim's thickness / 2 (FlightTestSceneBuilder.BuildRing)
DISH = (0.0, 14.0, 0.0)  # target radar dish pivot


# ---------------------------------------------------------------------------------------------- helpers


def ink_shell(p, sections, t, fan_tip=None, cap_start=True, cap_end=True):
    """
    A hand-made inverted hull round a loft: each ring pushed out from its own centre by ``t`` (the end rings also
    along the loft by ``t``), skinned in ink and turned inside out. Used where the kit's hull can't go: on glow faces,
    and on a pole that gets stretched (its rings only grow sideways, so the ink stays thin at any height).
    """
    rings = []
    for i, ring in enumerate(sections):
        c = sum((Vector(q) for q in ring), Vector()) / len(ring)
        pushed = [c + (Vector(q) - c).normalized() * ((Vector(q) - c).length + t) for q in ring]
        if (i == 0 and cap_start) or (i == len(sections) - 1 and cap_end and fan_tip is None):
            other = sections[1] if i == 0 else sections[-2]
            co = sum((Vector(q) for q in other), Vector()) / len(other)
            along = (c - co).normalized() * t
            pushed = [q + along for q in pushed]
        rings.append([tuple(q) for q in pushed])
    fan = None
    if fan_tip is not None:
        tip, last = Vector(fan_tip), sum((Vector(q) for q in sections[-1]), Vector()) / len(sections[-1])
        fan = (tuple(tip + (tip - last).normalized() * t * 1.5), "ink")
    faces = p.loft(rings, "ink", cap_start="ink" if cap_start else None, cap_end="ink" if cap_end and not fan else None,
                   fan_end=fan, outline=False)
    bmesh.ops.reverse_faces(p.bm, faces=faces)
    return faces


def torus(p, radius, tube, segments, sides, color, outline=True):
    """A ring round the local z axis (shared verts all round, so its ink hull has no seam). ``color(segment)``."""
    verts = []
    for i in range(segments):
        a = 2 * math.pi * i / segments
        ca, sa = math.cos(a), math.sin(a)
        ring = []
        for k in range(sides):
            b = math.pi / sides + 2 * math.pi * k / sides
            r = radius + tube * math.cos(b)
            ring.append(p.vert((r * ca, r * sa, tube * math.sin(b))))
        verts.append(ring)
    faces = []
    for i in range(segments):
        r0, r1 = verts[i], verts[(i + 1) % segments]
        c = color(i) if callable(color) else color
        for k in range(sides):
            j = (k + 1) % sides
            faces.append(p.face([r0[k], r0[j], r1[j], r1[k]], c))
    bmesh.ops.recalc_face_normals(p.bm, faces=faces)
    (p.hull_faces if outline else p.loose_faces).extend(faces)
    return faces


def lattice(p, legs, y0, y1, taper, levels, color, brace="white", r=0.16):
    """A tapering 4-leg lattice: legs from ``legs`` at y0 drawn in by ``taper`` at y1, rings and zigzag braces."""
    def leg(x, z, y):
        t = (y - y0) / (y1 - y0)
        return (x * (1 - taper * t), y, z * (1 - taper * t))

    for x, z in legs:
        beam(p, leg(x, z, y0), leg(x, z, y1), r, color, ink=0.04)
    ys = [y0 + (y1 - y0) * k / levels for k in range(levels + 1)]
    for ya, yb in zip(ys, ys[1:]):
        for (xa, za), (xb, zb) in zip(legs, legs[1:] + legs[:1]):
            beam(p, leg(xa, za, yb), leg(xb, zb, yb), r * 0.6, brace)
            beam(p, leg(xa, za, ya), leg(xb, zb, yb), r * 0.5, brace)


# ---------------------------------------------------------------------------------------------- lane pylon


def pylon():
    base = Part("PylonBase", outline=0.08)
    prism(base, ngon_pts(2.2, 8), -3.0, 0.6, "concrete", outline=0.08)
    prism(base, ngon_pts(1.5, 8), 0.6, 1.4, "darkmetal", outline=0.06)
    for k in range(8):  # hazard chevrons round the footing
        a = math.pi / 8 + 2 * math.pi * k / 8
        if k % 2:
            continue
        r = 2.2 * math.cos(math.pi / 8) + 0.03
        c, s = math.cos(a + math.pi / 8), math.sin(a + math.pi / 8)
        tx, tz = -s, c
        quad(base, [(r * c - tx * 0.7, -0.2, r * s - tz * 0.7), (r * c + tx * 0.7, -0.2, r * s + tz * 0.7),
                    (r * c + tx * 0.7, 0.4, r * s + tz * 0.7), (r * c - tx * 0.7, 0.4, r * s - tz * 0.7)], "sun", (c, 0, s))

    pole = Part("PylonPole")
    rings = [at(ngon_pts(POLE_R, 8), y) for y in (0.0, 1.0)]
    pole.loft(rings, "white", outline=False)
    ink_shell(pole, rings, 0.12, cap_start=False, cap_end=False)

    top = Part("PylonTop", outline=0.1)
    # Obstruction sleeve: orange and white bands down from the collar, a little wider than the pole.
    ys = [SLEEVE_BOTTOM + k * (-5.0 - SLEEVE_BOTTOM) / 5 for k in range(6)]
    top.loft([at(ngon_pts(0.95, 8), y) for y in ys], lambda b, s: "orange" if b % 2 == 0 else "white",
             cap_start="orange", cap_end="white")
    cylinder(top, 0.0, 0.0, -5.4, -4.2, 1.4, "darkmetal", sides=8, ink=0.08)  # collar
    # The marker: an 8-sided diamond 9 m tall, glowing in the lane's colour (team swatch on the glow material).
    diamond = [at(ngon_pts(1.0, 8), -4.2), at(ngon_pts(3.7, 8), 0.0)]
    tip = (0.0, 4.6, 0.0)
    top.loft(diamond, "team", cap_start="team", fan_end=(tip, "team"), glow=True)
    ink_shell(top, diamond, 0.16, fan_tip=tip, cap_end=False)
    cylinder(top, 0.0, 0.0, 4.4, 6.0, 0.3, "white", sides=6, top="red", ink=0.05)  # finial
    return [base, pole, top]


# ---------------------------------------------------------------------------------------------- buoy


def buoy():
    p = Part("Buoy", outline=0.1)
    hull = [(2.6, -2.0), (3.6, -0.6), (3.6, 0.6), (3.6, 1.6), (3.6, 2.6), (3.0, 3.4)]
    p.loft([at(ngon_pts(r, 10), y) for r, y in hull], lambda b, s: "white" if b == 2 else "orange", cap_start="orange",
           cap_end="darkmetal")
    legs = [(-2.0, -2.0), (2.0, -2.0), (2.0, 2.0), (-2.0, 2.0)]
    lattice(p, legs, 3.4, 19.0, 0.6, 3, "orange", brace="white", r=0.2)
    lantern_top(p)

    lamp = Part("BuoyLamp", LAMP)
    lx, ly, lz = LAMP
    rings = [at(ngon_pts(0.7, 8, lx, lz), ly - 1.9), at(ngon_pts(1.15, 8, lx, lz), ly - 1.2),
             at(ngon_pts(1.15, 8, lx, lz), ly + 1.2), at(ngon_pts(0.7, 8, lx, lz), ly + 1.9)]
    lamp.loft(rings, "red", cap_start="red", cap_end="red", glow=True)
    return [p, lamp]


def lantern_top(p):
    """The buoy's top from its tower's head up: lantern deck and posts round the lamp, cap and X topmark."""
    block(p, (0.0, 19.25, 0.0), (2.8, 0.5, 2.8), "darkmetal", c=0.3, ink=0.05)
    for x, z in ((-1.25, -1.25), (1.25, -1.25), (1.25, 1.25), (-1.25, 1.25)):  # lantern posts round the lamp
        beam(p, (x, 19.5, z), (x, 23.6, z), 0.1, "darkmetal")
    cylinder(p, 0.0, 0.0, 23.5, 24.0, 1.8, "darkmetal", sides=8, cone=0.8, ink=0.06)
    # X topmark, two crossed pairs so it reads from every side.
    beam(p, (0.0, 24.6, 0.0), (0.0, 25.6, 0.0), 0.12, "darkmetal")
    for yaw in (0.0, math.pi / 2):
        c, s = math.cos(yaw), math.sin(yaw)
        beam(p, (-1.3 * c, 25.5, -1.3 * s), (1.3 * c, 28.1, 1.3 * s), 0.22, "orange", ink=0.04)
        beam(p, (1.3 * c, 25.5, 1.3 * s), (-1.3 * c, 28.1, -1.3 * s), 0.22, "orange", ink=0.04)


# ---------------------------------------------------------------------------------------------- boundary post


def boundary_post():
    """
    The buoy's land twin: a concrete footing that reaches 3 m down (so it stays planted on a slope), a banded orange and
    white mast and the buoy's lantern top, so the lamp sits on the same pivot and BuoyLamp serves both.
    """
    p = Part("BoundaryPost", outline=0.1)
    prism(p, ngon_pts(2.8, 8), -3.0, 0.5, "concrete", outline=0.08)
    prism(p, ngon_pts(1.9, 8), 0.5, 1.3, "darkmetal", outline=0.06)
    for k in range(0, 8, 2):  # hazard chevrons round the footing
        a = math.pi / 4 + 2 * math.pi * k / 8
        r = 2.8 * math.cos(math.pi / 8) + 0.03
        c, s = math.cos(a), math.sin(a)
        tx, tz = -s, c
        quad(p, [(r * c - tx * 0.8, -0.1, r * s - tz * 0.8), (r * c + tx * 0.8, -0.1, r * s + tz * 0.8),
                 (r * c + tx * 0.8, 0.4, r * s + tz * 0.8), (r * c - tx * 0.8, 0.4, r * s - tz * 0.8)], "sun", (c, 0, s))
    ys = [1.3 + k * (19.0 - 1.3) / 6 for k in range(7)]
    p.loft([at(ngon_pts(1.25 - 0.4 * (y - 1.3) / 17.7, 8), y) for y in ys],
           lambda b, s: "orange" if b % 2 == 0 else "white", cap_start="orange", cap_end="white")
    for yaw in (math.pi / 4, 3 * math.pi / 4, 5 * math.pi / 4, 7 * math.pi / 4):  # guy struts to the plinth
        c, s = math.cos(yaw), math.sin(yaw)
        beam(p, (1.75 * c, 1.3, 1.75 * s), (0.95 * c, 6.0, 0.95 * s), 0.14, "darkmetal")
    lantern_top(p)
    return p


# ---------------------------------------------------------------------------------------------- ring


def ring_gate():
    p = Part("Ring", outline=0.12)
    torus(p, RING_R, TUBE_R, 32, 8, lambda i: "white" if i % 8 in (0, 1) else "team")
    col = Part("RingCol")
    torus(col, RING_R, TUBE_R, 20, 6, "metal", outline=False)
    return [p, col]


# ---------------------------------------------------------------------------------------------- targets


def target_truck():
    """Army truck, nose +z: olive cab and bed, khaki canvas tilt with a bullseye on top (it's a range target)."""
    p = Part("TargetTruck", outline=0.06)
    block(p, (0.0, 1.05, 0.2), (2.2, 0.4, 7.8), "darkmetal", c=0.1, ink=0.05)  # chassis
    prism(p, rect_pts(2.6, 2.1, 0.25, 0.0, 2.6), 1.25, 2.75, "olive", top="olive", outline=0.06)  # cab
    block(p, (0.0, 1.75, 4.1), (2.3, 1.0, 1.1), "olive", top="olive", c=0.2, ink=0.05)  # bonnet
    quad(p, [(-0.8, 1.4, 4.67), (0.8, 1.4, 4.67), (0.8, 2.1, 4.67), (-0.8, 2.1, 4.67)], "darkmetal", (0, 0, 1))  # grille
    quad(p, [(-1.0, 2.3, 3.67), (1.0, 2.3, 3.67), (1.0, 2.62, 3.67), (-1.0, 2.62, 3.67)], "window", (0, 0, 1))
    for s in (-1.0, 1.0):
        quad(p, [(s * 1.32, 2.0, 2.0), (s * 1.32, 2.0, 3.2), (s * 1.32, 2.55, 3.2), (s * 1.32, 2.55, 2.0)], "window", (s, 0, 0))
        quad(p, [(s * 1.32, 1.45, 2.25), (s * 1.32, 1.45, 2.95), (s * 1.32, 1.85, 2.95), (s * 1.32, 1.85, 2.25)], "white",
             (s, 0, 0))  # door number plate
    block(p, (0.0, 1.55, -1.1), (2.8, 0.6, 5.0), "olive", top="darkmetal", c=0.1, ink=0.05)  # bed
    tilt = [(1.35, 1.85), (1.35, 2.95), (0.95, 3.4), (-0.95, 3.4), (-1.35, 2.95), (-1.35, 1.85)]
    zs = [-3.55, -2.3, -2.1, -0.4, -0.2, 1.3]
    p.loft([[(x, y, z) for x, y in tilt] for z in zs], lambda b, s: "drywood" if b in (1, 3) else "khaki",
           cap_start="khaki", cap_end="khaki")
    for r, color, lift in ((1.0, "red", 0.02), (0.66, "white", 0.04), (0.33, "red", 0.06)):
        p.disc((0.0, 3.4 + lift, -1.1), (0, 1, 0), r, 10, color)
    for z in (3.0, -1.7, -2.95):
        for s in (-1.0, 1.0):
            xcyl(p, s * 1.0, s * 1.4, 0.55, z, 0.55, "tire", sides=8, cap="metal", outline=0.03)
    return p


def target_radar():
    """Radar post: an olive blockhouse with a lattice mast on its roof; TargetRadarDish turns on top."""
    p = Part("TargetRadar", outline=0.12)
    prism(p, rect_pts(15.0, 11.0, 0.8), -0.4, 0.3, "concrete", outline=0.08)
    walls = rect_pts(14.0, 10.0, 0.6)
    prism(p, walls, 0.3, 5.5, "olive", top="khaki")
    windows(p, walls, 1.2, 5.0, "window", floor=3.8, frac=0.3, cols=3, margin=1.6, gap=0.4, min_half=3.0)
    quad(p, [(-1.2, 0.3, -5.04), (1.2, 0.3, -5.04), (1.2, 3.2, -5.04), (-1.2, 3.2, -5.04)], "darkmetal", (0, 0, -1))  # door
    prism(p, rect_pts(14.6, 10.6, 0.7), 5.5, 6.0, "concrete", outline=0.08)  # roof slab
    block(p, (4.5, 6.6, 2.5), (2.4, 1.2, 1.8), "metal", top="concrete", ink=0.05)  # generator
    block(p, (8.2, 1.0, 1.5), (2.0, 2.0, 3.2), "olive", top="khaki", c=0.2, ink=0.05)  # fuel bowser by the wall
    beam(p, (-5.5, 6.0, 3.5), (-5.5, 10.5, 3.5), 0.08, "metal", ink=0.03)  # whip antenna
    lattice(p, [(-1.6, -1.6), (1.6, -1.6), (1.6, 1.6), (-1.6, 1.6)], 6.0, 13.4, 0.6, 2, "white", r=0.16)
    block(p, (0.0, 13.6, 0.0), (1.8, 0.4, 1.8), "darkmetal", c=0.2, ink=0.04)

    dish = Part("TargetRadarDish", DISH, outline=0.06)
    rx, ry, rz = DISH
    cylinder(dish, rx, rz, ry - 0.2, ry + 0.5, 0.55, "darkmetal", sides=6, ink=0.04)
    block(dish, (rx, ry + 0.9, rz - 0.3), (0.6, 0.8, 1.4), "darkmetal", c=0.1, ink=0.04)
    sections = []
    for k in range(9):  # 9 m wide, 4 m tall, curved side to side and tipped back
        x = -4.5 + k * 1.125
        bow = 0.05 * x * x
        sections.append([(x, ry + 1.0 + y, rz + 0.5 + bow - y * 0.2 + t)
                         for y, t in ((-2.0, 0.0), (2.0, 0.0), (2.0, -0.16), (-2.0, -0.16))])
    dish.loft(sections, lambda b, s: "metal" if s == 2 else "white", cap_start="white", cap_end="white")
    beam(dish, (rx, ry + 0.8, rz + 0.5), (rx, ry + 0.4, rz + 3.0), 0.08, "darkmetal")
    block(dish, (rx, ry + 0.35, rz + 3.1), (0.4, 0.4, 0.5), "red", c=0.06, ink=0.03)
    return [p, dish]


# ---------------------------------------------------------------------------------------------- build


def build():
    model = Model("Markers")
    materials = model.materials + [glow_material()]
    parts = pylon() + buoy() + [boundary_post()] + ring_gate() + [target_truck()] + target_radar()
    for part in parts:
        obj = part.finish(model.collection, materials)
        drop_unused_materials(obj)
        model.objects.append(obj)
    model.export(FBX_PATH, BLEND_PATH)


# ---------------------------------------------------------------------------------------------- previews


def lay_out():
    import bpy
    for o in [o for o in bpy.data.objects if o.name.endswith("Col")]:
        bpy.data.objects.remove(o)
    # A 60 m pylon (pole stretched to the sleeve), a buoy, a ring, the truck and the radar post in a row.
    pole_top = 60.0
    bpy.data.objects["PylonTop"].location = to_blender((0.0, pole_top, 0.0))
    bpy.data.objects["PylonPole"].scale = (1.0, 1.0, pole_top + SLEEVE_BOTTOM + 0.5)
    bpy.data.objects["Buoy"].location = to_blender((30.0, 0.0, 0.0))
    bpy.data.objects["BuoyLamp"].location = to_blender((30.0 + LAMP[0], LAMP[1], LAMP[2]))
    bpy.data.objects["BoundaryPost"].location = to_blender((45.0, 0.0, 0.0))
    bpy.data.objects["Ring"].location = to_blender((80.0, 30.0, 0.0))
    bpy.data.objects["TargetTruck"].location = to_blender((-25.0, 0.0, -10.0))
    bpy.data.objects["TargetRadar"].location = to_blender((-25.0, 0.0, 20.0))
    bpy.data.objects["TargetRadarDish"].location = to_blender((-25.0 + DISH[0], DISH[1], 20.0 + DISH[2]))


SHOTS = [
    ("markers_overview", (10.0, 40.0, -150.0), (25.0, 25.0, 0.0), None),
    ("markers_pylon_top", (14.0, 58.0, -26.0), (0.0, 54.0, 0.0), None),
    ("markers_buoy", (44.0, 10.0, -30.0), (30.0, 13.0, 0.0), None),
    ("markers_post", (70.0, 6.0, -55.0), (45.0, 13.0, 0.0), None),
    ("markers_ring", (60.0, 38.0, -60.0), (80.0, 30.0, 0.0), None),
    ("markers_targets", (-5.0, 22.0, -40.0), (-25.0, 4.0, 5.0), None),
]


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
