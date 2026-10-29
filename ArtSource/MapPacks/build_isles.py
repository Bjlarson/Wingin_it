"""
The Isles pack: the Shattered Archipelago's sea stacks, arch and whitewashed cliff villages, so its sounds are full
of rock to thread and its islands look settled.

    blender --background --factory-startup --python ArtSource/MapPacks/build_isles.py -- [--preview <dir>]

Writes Assets/_Project/Art/Models/Isles/Isles.fbx. Origins at sea level (rocks, boats) or on the ground:
    SeaStackA, SeaStackB, SeaStackC   rock pillars 50, 85 and 32 m tall (C is a twin), grass on their tops,
                                       reaching 30 m below the waves
    SeaArch           a rock arch, 150 m end to end: the gap under it 80 m wide and ~55 m high, along z
    Shipwreck         a broken, listing hull in the shallows
    CliffHouseA/B     whitewashed houses, blue doors and shutters (B: two storeys and a roof terrace)
    Chapel            a white chapel with a blue dome and a bell gable
    IslesMill         a round white windmill with a thatch cap; IslesMillSails (origin on the hub) turn about local y
    WatchtowerRuin    a crumbling round tower
    TreeCypress, TreeOlive
    FishingBoat       a small white caique with a blue stripe; origin at the waterline
(The Isles also sail the Tropics pack's yacht.)
"""
import math
import os
import random
import sys

from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pack_kit import (PROP_INK, Part, beam, block, cylinder, door, export_pack, hip, lay_out_row, lump, main,  # noqa: E402
                      ngon_pts, prism, rect4, rotor, side, tube, wall_quad)

ROCK_INK = 0.35


def stack(name, profile, seed, sides=9):
    """A sea stack: jittered rings (shared bays, so the faces drop straight) banded in stone, a grass cap on top."""
    p = Part(name, outline=ROCK_INK)
    rnd = random.Random(seed)
    phase = rnd.uniform(0, math.pi)
    bays = [rnd.uniform(-1.0, 1.0) for _ in range(sides)]
    lean = Vector((rnd.uniform(-0.06, 0.06), 0.0, rnd.uniform(-0.06, 0.06)))
    rings = []
    for y, r in profile:
        ring = []
        for k in range(sides):
            rr = r * (1.0 + bays[k] * 0.16) + rnd.uniform(-0.6, 0.6)
            a = phase + 2 * math.pi * k / sides
            ring.append((rr * math.cos(a) + lean.x * y, y, rr * math.sin(a) + lean.z * y))
        rings.append(ring)
    n = len(rings) - 1
    bands = lambda b, s: "grass" if b == n - 1 else ("limestone" if b % 3 == 1 else "stone")  # noqa: E731
    p.loft(rings, bands, cap_start="stone", cap_end="grass")
    return p


def build_stacks():
    a = stack("SeaStackA", [(-30, 22), (0, 20), (6, 18), (20, 17), (34, 15), (46, 13), (50, 11.5), (51, 10)], 11)
    b = stack("SeaStackB", [(-30, 17), (0, 15), (8, 13), (30, 11.5), (55, 10), (78, 8.5), (84, 7.0), (85.2, 6.0)], 22, 8)
    c = stack("SeaStackC", [(-30, 12), (0, 11), (10, 9), (22, 7.5), (30, 6.0), (31.5, 4.5)], 33, 7)
    twin = stack("SeaStackCTwin", [(-30, 9), (0, 8), (9, 6.5), (17, 5.0), (19, 3.8)], 34, 7)
    # Merge the twin into C, offset to one side.
    for v in twin.bm.verts:
        v.co.x += 17.0
        v.co.y += 6.0  # Blender y is Unity z
    _merge(c, twin)
    return [a, b, c]


def _merge(dst, src):
    """Copies src's faces (and colours) into dst, keeping hull membership."""
    vmap = {v: dst.bm.verts.new(v.co) for v in src.bm.verts}
    hull = set(src.hull_faces)
    for f in src.bm.faces:
        nf = dst.bm.faces.new([vmap[v] for v in f.verts])
        nf[dst.col] = f[src.col]
        nf[dst.smooth] = f[src.smooth]
        nf[dst.glow] = f[src.glow]
        (dst.hull_faces if f in hull else dst.loose_faces).append(nf)
    src.bm.free()


def sea_arch():
    """
    The arch spans along z: two legs 60 m either side of the origin, joined 70 m up. Its rings stand across the path
    (the arch's plane is yz), with their top faces grassed where the span is level.
    """
    p = Part("SeaArch", outline=ROCK_INK * 1.3)
    rnd = random.Random(55)
    sides = 8
    jitter = [rnd.uniform(-1.0, 1.0) for _ in range(sides)]
    path = []
    for k in range(13):
        t = k / 12.0
        ang = math.pi * t
        z = -60.0 * math.cos(ang) * (1.0 + 0.1 * math.sin(ang * 2))
        y = -30.0 + 100.0 * math.sin(ang) ** 0.6
        path.append(Vector((0.0, y, z)))
    rings, ups = [], []
    for i, c in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        n = Vector((0.0, -t.z, t.y))  # in the arch's plane, perpendicular to the path, pointing away from its middle
        if n.dot(c - Vector((0.0, -30.0, 0.0))) < 0:
            n = -n
        x_axis = Vector((1.0, 0.0, 0.0))
        span = abs(i - 6) / 6.0
        r_in = 13.0 + 10.0 * span ** 1.5  # thick legs, a slimmer span
        depth = 16.0 + 8.0 * span
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides + math.pi / sides
            rr = 1.0 + jitter[k] * 0.12
            ring.append(tuple(c + (n * math.cos(a) * r_in + x_axis * math.sin(a) * depth) * rr))
        rings.append(ring)
        ups.append([math.cos(2 * math.pi * k / sides + math.pi / sides) * n.y for k in range(sides)])
    color = lambda b, s: ("grass" if ups[b][s] > 0.6 and 3 <= b <= 8 else ("limestone" if b % 4 == 1 else "stone"))  # noqa: E731
    p.loft(rings, color, cap_start="stone", cap_end="stone")
    # Fallen blocks at the legs' feet.
    for k in range(6):
        z = rnd.choice((-1, 1)) * rnd.uniform(70.0, 85.0)
        x = rnd.uniform(-20.0, 20.0)
        s = rnd.uniform(5.0, 9.0)
        lump(p, (x, s * 0.1, z), (s, s * 0.8, s), 60 + k, "stone", bumps=0.25, floor=-s * 0.8)
    return p


def shipwreck():
    p = Part("Shipwreck", outline=PROP_INK * 2)
    tilt = math.radians(20.0)
    pitch = math.radians(-6.0)

    def tx(pt):
        x, y, z = pt
        x, y = x * math.cos(tilt) - y * math.sin(tilt), x * math.sin(tilt) + y * math.cos(tilt)
        y, z = y * math.cos(pitch) - z * math.sin(pitch), y * math.sin(pitch) + z * math.cos(pitch)
        return (x, y - 1.5, z)

    hull = []
    for y, w, l in ((-5.0, 3.0, 18.0), (-2.0, 7.0, 26.0), (1.5, 9.0, 30.0), (4.0, 9.4, 32.0)):
        hw, hl = w * 0.5, l * 0.5
        hull.append([tx(q) for q in ((hw, y, -hl + 2.0), (hw, y, hl - 7.0), (0.0, y, hl), (-hw, y, hl - 7.0),
                                     (-hw, y, -hl + 2.0), (-hw * 0.8, y, -hl), (hw * 0.8, y, -hl))])
    p.loft(hull, lambda b, s: "drywood" if b == 2 else "timber", cap_start="timber", cap_end="char")
    block(p, tx((0, 6.0, -11.0)), (8.0, 4.0, 6.0), "timber", top="drywood", c=0.4)  # stern castle
    for z, h in ((6.0, 14.0), (-3.0, 9.0)):
        beam(p, tx((0, 3.0, z)), tx((0, 3.0 + h, z + 0.5)), 0.4, "drywood", ink=PROP_INK)
    beam(p, tx((-5.0, 13.0, 6.2)), tx((5.0, 13.6, 6.2)), 0.25, "drywood", ink=PROP_INK)  # yard, still up
    sail = [tx((-4.5, 13.0, 6.3)), tx((4.0, 13.4, 6.3)), tx((2.0, 7.0, 7.0))]
    p.loft([sail, [(x + 0.1, y, z + 0.15) for x, y, z in sail]], "khaki", cap_start="khaki", cap_end="khaki",
           outline=PROP_INK * 0.5)
    for k in range(5):  # broken ribs where the planking's gone
        z = 2.0 + k * 2.2
        beam(p, tx((4.6, 4.0, z)), tx((4.0, 8.5 - k * 0.6, z)), 0.3, "drywood", ink=PROP_INK * 0.6)
    return p


def white_box(p, w, d, h, cx=0.0, cz=0.0, y0=-5.0, parapet=True):
    walls = rect4(w, d, cx, cz)
    prism(p, walls, y0, h, "white", top="limestone", outline=PROP_INK)
    if parapet:
        for f in ((w, 0.4, 0, d * 0.5 - 0.2), (w, 0.4, 0, -d * 0.5 + 0.2), (0.4, d, w * 0.5 - 0.2, 0), (0.4, d, -w * 0.5 + 0.2, 0)):
            block(p, (cx + f[2], h + 0.4, cz + f[3]), (f[0], 0.8, f[1]), "white", c=0.0, ink=PROP_INK * 0.5)
    return walls


def cliff_houses():
    a = Part("CliffHouseA", outline=PROP_INK)
    walls = white_box(a, 9.0, 8.0, 6.0)
    door(a, side(walls, (0, 1)), -2.0, 1.4, 2.4, "aegean")
    for n, offs in (((0, 1), (1.8,)), ((1, 0), (-2.0, 2.0)), ((-1, 0), (0.0,))):
        for o in offs:
            door(a, side(walls, n), o, 1.4, 1.4, "aegean", 2.8)
    block(a, (2.5, 7.4, -1.5), (1.2, 2.0, 1.2), "white", c=0.2, ink=PROP_INK * 0.5)  # chimney
    lump(a, (-4.8, 1.0, 4.2), (1.4, 1.4, 1.2), 70, "flower", outline=PROP_INK * 0.5)  # bougainvillea

    b = Part("CliffHouseB", outline=PROP_INK)
    walls = white_box(b, 11.0, 9.0, 5.5, parapet=False)
    upper = white_box(b, 7.0, 7.0, 10.5, cx=-2.0, cz=-1.0, y0=5.5)
    door(b, side(walls, (0, 1)), 2.5, 1.4, 2.4, "aegean")
    for n in ((0, 1), (1, 0)):
        door(b, side(upper, n), 0.0, 1.4, 1.6, "aegean", 7.6)
        door(b, side(walls, n), -2.0, 1.4, 1.4, "aegean", 2.6)
    # Roof terrace: a pergola of timber and vines over the low part.
    for x, z in ((2.2, 2.8), (5.0, 2.8), (2.2, -3.8), (5.0, -3.8)):
        cylinder(b, x, z, 5.5, 8.0, 0.15, "timber", sides=4, ink=PROP_INK * 0.4)
    block(b, (3.6, 8.1, -0.5), (3.6, 0.25, 7.4), "grass", c=0.2, ink=PROP_INK * 0.4)
    hip(b, 7.0, 7.0, 10.5, 1.2, "terracotta", overhang=0.5, cx=-2.0, cz=-1.0)
    return [a, b]


def chapel():
    p = Part("Chapel", outline=PROP_INK)
    walls = white_box(p, 8.0, 12.0, 7.0, parapet=False)
    door(p, side(walls, (0, 1)), 0.0, 1.8, 3.0, "aegean")
    for n in ((1, 0), (-1, 0)):
        for a in (-3.0, 3.0):
            door(p, side(walls, n), a, 1.0, 2.0, "aegean", 3.0)
    # Drum and dome over the middle.
    poly = ngon_pts(3.4, 12)
    p.loft([[(x, y, z) for x, z in poly] for y in (7.0, 9.0)], "white", outline=PROP_INK)
    dome = [[(x * f, 9.0 + h, z * f) for x, z in poly] for f, h in ((1.05, 0.0), (0.92, 1.6), (0.65, 3.0), (0.32, 3.8))]
    p.loft(dome, "aegean", fan_end=((0, 4.1 + 9.0, 0), "aegean"), outline=PROP_INK)
    cylinder(p, 0, 0, 13.0, 14.6, 0.1, "sun", sides=4)
    block(p, (0, 14.1, 0), (0.9, 0.18, 0.18), "sun", c=0.0, ink=False)
    # Bell gable over the front.
    block(p, (0, 9.2, 5.6), (4.6, 4.4, 0.8), "white", c=0.0)
    door(p, side(rect4(4.6, 0.8, 0.0, 5.6), (0, 1)), 0.0, 1.4, 1.8, "char", 8.4)
    block(p, (0, 9.0, 6.05), (0.6, 0.8, 0.1), "sun", c=0.0, ink=False)
    return p


def isles_mill():
    p = Part("IslesMill", outline=PROP_INK)
    poly = ngon_pts(4.2, 12)
    p.loft([[(x, y, z) for x, z in poly] for y in (-1.0, 10.0)], "white", outline=PROP_INK)
    door(p, side(poly, (0, -1)), 0.0, 1.6, 2.4, "aegean")
    door(p, side(poly, (1, 0)), 0.0, 1.0, 1.2, "aegean", 5.5)
    hip(p, 8.4, 8.4, 10.0, 4.2, "thatch", overhang=0.4, sides=12)
    beam(p, (0, 11.8, 0.0), (0, 11.8, 4.8), 0.3, "timber", ink=PROP_INK * 0.5)  # windshaft
    return p


def isles_mill_sails():
    hub = (0.0, 11.8, 5.0)
    p = Part("IslesMillSails", origin=hub, outline=PROP_INK)
    rotor(p, hub, 7.5, 8, "drywood", 0.6, sail="white")
    return p


def watchtower_ruin():
    p = Part("WatchtowerRuin", outline=PROP_INK * 1.5)
    rnd = random.Random(81)
    poly = ngon_pts(5.0, 12)
    p.loft([[(x * f, y, z * f) for x, z in poly] for y, f in ((-2.0, 1.15), (2.0, 1.1), (14.0, 1.0))],
           lambda b, s: "stone" if b == 0 else ("limestone" if s % 3 else "stone"), cap_start="stone", cap_end="char")
    for k in range(12):  # broken crenellations: a ring of blocks at uneven heights, some fallen
        if k in (2, 3, 8):
            continue
        a = 2 * math.pi * k / 12 + math.pi / 12
        h = rnd.uniform(1.0, 4.5)
        block(p, (4.4 * math.cos(a), 14.0 + h * 0.5, 4.4 * math.sin(a)), (2.4, h, 1.4), "limestone", c=0.2,
              ink=PROP_INK)
    for a in (0.0, 2.1, 4.2):
        f = side(poly, (math.cos(a), math.sin(a)))
        wall_quad(p, f, -0.3, 0.3, 6.0, 8.0, "char", off=0.3)
    for k in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        s = rnd.uniform(1.2, 2.2)
        lump(p, (8.0 * math.cos(a), s * 0.3, 8.0 * math.sin(a)), (s, s * 0.7, s), 90 + k, "limestone", floor=-s * 0.5)
    return p


def trees():
    cyp = Part("TreeCypress", outline=PROP_INK * 0.7)
    tube(cyp, [(0, -0.5, 0), (0, 2.0, 0)], [0.4, 0.3], 5, "bark", cap_start="bark", cap_end="bark")
    lump(cyp, (0, 6.5, 0), (1.6, 5.2, 1.5), 91, "pine", bumps=0.1)
    lump(cyp, (0, 11.5, 0), (0.9, 2.4, 0.9), 92, "pine", bumps=0.08)
    olive = Part("TreeOlive", outline=PROP_INK * 0.7)
    tube(olive, [(0, -0.5, 0), (0.5, 1.5, 0.2), (-0.2, 3.0, 0.4)], [0.7, 0.5, 0.45], 6, "drywood", cap_start="drywood",
         cap_end="drywood")
    for path in (((-0.2, 2.8, 0.4), (-1.8, 4.0, 0.6)), ((-0.1, 2.9, 0.3), (1.6, 4.2, -0.6))):
        tube(olive, path, [0.28, 0.2], 5, "drywood", cap_end="drywood")
    lump(olive, (0.0, 5.0, 0.2), (3.4, 1.8, 3.0), 93, "olive", bumps=0.22)
    lump(olive, (-2.0, 4.4, 0.8), (1.8, 1.3, 1.6), 94, "olive", bumps=0.2)
    lump(olive, (1.9, 4.6, -0.6), (1.8, 1.3, 1.6), 95, "leaf", bumps=0.2)
    return [cyp, olive]


def fishing_boat():
    p = Part("FishingBoat", outline=PROP_INK)
    hull = []
    for y, w, l in ((-1.0, 1.0, 6.0), (-0.3, 2.8, 8.4), (0.5, 3.3, 9.0), (1.0, 3.4, 9.4)):
        hw, hl = w * 0.5, l * 0.5
        hull.append([(hw, y, -hl + 1.2), (hw, y, hl - 2.4), (0.0, y, hl + 0.4), (-hw, y, hl - 2.4), (-hw, y, -hl + 1.2),
                     (0.0, y, -hl)])
    p.loft(hull, lambda b, s: "aegean" if b == 2 else ("red" if b == 0 else "white"), cap_start="red", cap_end="drywood")
    block(p, (0, 1.8, -1.6), (1.8, 1.6, 2.2), "white", top="aegean", c=0.2)
    cylinder(p, 0, 1.6, 1.0, 6.0, 0.1, "timber", sides=4)
    beam(p, (0, 5.8, 1.6), (0, 1.2, 4.6), 0.05, "char")
    return p


def build():
    parts = build_stacks() + [sea_arch(), shipwreck()] + cliff_houses() + [chapel(), isles_mill(), isles_mill_sails(),
                                                                           watchtower_ruin()] + trees() + [fishing_boat()]
    export_pack("Isles", parts)


def lay_out():
    import bpy
    lay_out_row(["SeaStackA", "SeaStackB", "SeaStackC", "Shipwreck"], 70.0, z=0.0)
    bpy.data.objects["SeaArch"].location = (0.0, 180.0, 0.0)
    lay_out_row(["CliffHouseA", "CliffHouseB", "Chapel", "IslesMill", "WatchtowerRuin", "TreeCypress", "TreeOlive",
                 "FishingBoat"], 18.0, z=-70.0)
    o = bpy.data.objects["IslesMillSails"]
    o.location = bpy.data.objects["IslesMill"].location + o.location
    o.rotation_euler[0] = math.radians(90.0)


SHOTS = [
    ("isles_rocks", (-260.0, 140.0, -260.0), (0.0, 25.0, 80.0), 420.0),
    ("isles_village", (-50.0, 40.0, -130.0), (0.0, 5.0, -70.0), 150.0),
]

if __name__ == "__main__":
    main(build, lay_out, SHOTS)
