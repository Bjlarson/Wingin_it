"""
The Prairie pack: Broad Reach's farms, so its long valley and the West Plains read as patchwork farmland with
homesteads, rather than one sheet of grass.

    blender --background --factory-startup --python ArtSource/MapPacks/build_prairie.py -- [--preview <dir>]

Writes Assets/_Project/Art/Models/Prairie/Prairie.fbx. Each object stands on its origin (y = 0 is the ground):
    Barn              a red barn with a hayloft and white-trimmed doors, 26 x 16 m, 17 m to the ridge
    Farmhouse         a white house with a porch and chimney
    Church            a white chapel with a steeple, for the hamlets (33 m to the spire's tip)
    Silo              a metal grain silo with a domed cap, 24 m
    WaterTower        a tank on four legs, 26 m
    Windpump          a lattice wind pump, 15 m; WindpumpRotor (its wheel, origin on the hub) turns about its local y
    Windmill          a timber smock mill, 20 m; WindmillSails (origin on the hub) turn about their local y
                      (Unity stands each rotor up so its y faces +z, out of the tower's front)
    CropWheat, CropGreen, CropPlowed   a 40 x 40 m field of rows, its soil slab sunk 1.5 m for gentle slopes
    Fence             a 12 m run of posts and rails along x, centred
    HayBale, HayStack round bales and a loose stack
    Cow               a toy cow, 3.6 m long, facing +z
    HotAirBalloon     a striped balloon, 22 m tall, origin at the basket's floor
    TreePoplar        a tall narrow windbreak tree, 17 m
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pack_kit import (PROP_INK, Part, beam, block, cylinder, door, export_pack, gable, gable_end, hip, house,  # noqa: E402
                      lattice, lay_out_row, lump, main, ngon_pts, prism, rect4, rotor, side, to_blender, tube, wall_quad)


def barn():
    p = Part("Barn", outline=PROP_INK * 1.3)
    w, d, h = 16.0, 26.0, 9.0
    walls = rect4(w, d)
    prism(p, walls, -1.0, h, "barnred", outline=PROP_INK * 1.3)
    gable_end(p, w - 0.05, d - 0.05, h, 7.5, "barnred")
    gable(p, w, d, h, 7.5, "darkmetal", under="white", overhang=1.0)
    front, back = side(walls, (0, 1)), side(walls, (0, -1))
    for f in (front, back):
        door(p, f, 0.0, 7.0, 7.0, "white")  # white frame, red X doors inside it
        door(p, f, 0.0, 6.0, 6.4, "barnred")
        wall_quad(p, f, -0.4, 0.4, 0.2, 6.2, "white", off=0.3)
        door(p, f, 0.0, 3.0, 2.6, "white", 10.6)  # hayloft
        door(p, f, 0.0, 2.4, 2.0, "char", 10.9)
    for f in (side(walls, (1, 0)), side(walls, (-1, 0))):
        for a in (-8.0, 0.0, 8.0):
            door(p, f, a, 2.2, 1.6, "white", 4.2)
            door(p, f, a, 1.8, 1.2, "char", 4.4)
    cylinder(p, 0, 6.0, h + 7.4, h + 9.2, 0.9, "metal", sides=6, top="darkmetal", cone=1.4)  # roof vent
    return p


def farmhouse():
    p = Part("Farmhouse", outline=PROP_INK)
    w, d, h = 10.0, 13.0, 7.5
    walls = rect4(w, d)
    house(p, w, d, h, "white", "terracotta", 4.5, under="white")
    front = side(walls, (1, 0))
    door(p, front, 0.0, 1.6, 2.6, "timber")
    for a in (-4.0, -2.2, 2.2, 4.0):
        door(p, front, a, 1.4, 1.6, "window", 1.2 if abs(a) < 3 else 4.6)
    for f in (side(walls, (-1, 0)),):
        for a in (-3.0, 3.0):
            door(p, f, a, 1.4, 1.6, "window", 1.2)
            door(p, f, a, 1.4, 1.6, "window", 4.6)
    # Porch along the front (+x).
    block(p, (w * 0.5 + 1.8, 0.2, 0), (3.6, 0.6, 9.0), "drywood", c=0.1)
    for z in (-4.2, 0.0, 4.2):
        cylinder(p, w * 0.5 + 3.3, z, 0.4, 3.4, 0.15, "white", sides=4, ink=PROP_INK * 0.5)
    block(p, (w * 0.5 + 1.9, 3.6, 0), (4.2, 0.35, 9.6), "terracotta", c=0.1)
    block(p, (-2.5, h + 4.0, -4.0), (1.4, 4.0, 1.4), "brick", top="char", c=0.1)  # chimney
    return p


def church():
    p = Part("Church", outline=PROP_INK)
    w, d, h = 9.0, 16.0, 7.0
    walls = rect4(w, d, 0.0, -2.0)
    house(p, w, d, h, "white", "slate", 4.5, cz=-2.0, under="white")
    for a in (-5.0, -1.5, 2.0, 5.5):
        for f in (side(walls, (1, 0)), side(walls, (-1, 0))):
            door(p, f, a, 1.2, 2.8, "aegean", 2.4)
    # Steeple at the front (+z): a square tower, a belfry and a spire.
    block(p, (0, 6.0, 7.5), (4.4, 14.0, 4.4), "white", c=0.0)
    door(p, side(rect4(4.4, 4.4, 0.0, 7.5), (0, 1)), 0.0, 1.8, 3.0, "timber")
    block(p, (0, 14.8, 7.5), (3.6, 3.6, 3.6), "white", top="slate", c=0.3)
    for n in ((0, 1), (1, 0), (-1, 0), (0, -1)):
        door(p, side(rect4(3.6, 3.6, 0.0, 7.5), n), 0.0, 1.4, 2.2, "char", 13.8)
    hip(p, 3.6, 3.6, 16.6, 13.0, "slate", overhang=0.4, cz=7.5, sides=8)
    cylinder(p, 0, 7.5, 29.6, 32.4, 0.12, "sun", sides=4)
    block(p, (0, 31.4, 7.5), (1.6, 0.3, 0.3), "sun", c=0.0, ink=False)
    return p


def silo():
    p = Part("Silo", outline=PROP_INK)
    r, h = 4.2, 20.0
    poly = ngon_pts(r, 12)
    p.loft([[(x, y, z) for x, z in poly] for y in (-1.0, h)], lambda b, s: "metal" if s % 2 else "white",
           cap_start="metal", outline=PROP_INK)
    dome = [[(x * f, h + rise, z * f) for x, z in poly] for f, rise in ((1.04, 0.0), (0.8, 2.2), (0.45, 3.6))]
    p.loft(dome, "white", fan_end=((0, h + 4.2, 0), "white"), outline=PROP_INK)
    for y in (5.0, 10.0, 15.0):
        p.loft([[(x * 1.02, yy, z * 1.02) for x, z in poly] for yy in (y, y + 0.5)], "darkmetal", outline=PROP_INK * 0.5)
    beam(p, (r + 0.4, 0.0, 0), (r + 0.4, h + 2.0, 0), 0.12, "darkmetal")  # ladder rail
    block(p, (r + 1.8, 2.0, 0), (2.4, 4.0, 2.4), "darkmetal", c=0.2)  # auger housing
    return p


def water_tower():
    p = Part("WaterTower", outline=PROP_INK)
    legs = 16.0
    for x, z in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        beam(p, (x * 5.0, -1.0, z * 5.0), (x * 3.4, legs, z * 3.4), 0.35, "slate", ink=PROP_INK * 0.6)
    for y in (6.0, 11.0):
        t = (y + 1.0) / (legs + 1.0)
        r = 5.0 - 1.6 * t
        for a, b in (((r, r), (-r, r)), ((-r, r), (-r, -r)), ((-r, -r), (r, -r)), ((r, -r), (r, r))):
            beam(p, (a[0], y, a[1]), (b[0], y, b[1]), 0.18, "slate", ink=PROP_INK * 0.4)
    poly = ngon_pts(5.2, 14)
    p.loft([[(x * f, y, z * f) for x, z in poly] for f, y in ((0.6, legs - 0.5), (1.0, legs + 1.0), (1.0, legs + 7.0))],
           lambda b, s: "white" if b == 0 or s % 7 not in (0, 1) else "sun", cap_start="slate", outline=PROP_INK)
    p.loft([[(x * 1.06, legs + 7.0, z * 1.06) for x, z in poly]], "slate", cap_start="slate",
           fan_end=((0, legs + 10.0, 0), "slate"))
    cylinder(p, 0, 0, legs + 9.6, legs + 10.8, 0.12, "darkmetal", sides=4)
    return p


def windpump():
    p = Part("Windpump", outline=PROP_INK)
    lattice(p, -0.5, 13.2, 2.2, 0.5, "metal", sides=4, bays=4, member=0.12)
    block(p, (0, 13.6, 0), (1.0, 1.0, 1.6), "darkmetal", c=0.15)
    beam(p, (0, 13.6, -0.6), (0, 13.8, -4.6), 0.12, "darkmetal")  # tail boom
    vane = [(0, 12.8, -3.6), (0, 15.4, -4.8), (0, 13.6, -6.2)]
    p.loft([vane, [(0.12, y, z) for _, y, z in vane]], "red", cap_start="red", cap_end="red", outline=PROP_INK * 0.5)
    return p


def windpump_rotor():
    hub = (0.0, 13.6, 1.1)
    p = Part("WindpumpRotor", origin=hub, outline=PROP_INK)
    # The wheel lies in the xz plane round the hub (Unity stands it up): 14 narrow blades and a rim.
    rotor(p, hub, 2.6, 14, "white", 0.5, tilt=0.6)
    rim = [(hub[0] + 2.6 * math.cos(2 * math.pi * k / 14), hub[1] + 0.25, hub[2] + 2.6 * math.sin(2 * math.pi * k / 14))
           for k in range(14)]
    for a, b in zip(rim, rim[1:] + rim[:1]):
        beam(p, a, b, 0.07, "red")
    return p


def windmill():
    p = Part("Windmill", outline=PROP_INK * 1.2)
    block(p, (0, 0.5, 0), (11.0, 3.0, 11.0), "brick", top="drywood", c=1.6)  # brick base
    rings = [[(x, y, z) for x, z in ngon_pts(r, 8)] for y, r in ((2.0, 5.0), (15.0, 3.2))]
    p.loft(rings, lambda b, s: "timber" if s % 2 else "drywood", outline=PROP_INK * 1.2)
    tower = ngon_pts(4.6, 8)
    for a in (0.0,):
        door(p, side(tower, (0, 1)), a, 1.6, 2.6, "white", 2.0)
    wall_quad(p, side(ngon_pts(3.6, 8), (0, 1)), -0.7, 0.7, 9.0, 10.6, "white", off=0.4)
    # Cap: a boat-shaped hood turned into the wind (+z), and a stage round the tower.
    p.loft([[(x, y, z) for x, z in ngon_pts(r, 8)] for y, r in ((14.8, 3.6), (16.0, 3.8))], "white", cap_start="white",
           fan_end=((0, 19.0, 0), "white"), outline=PROP_INK)
    block(p, (0, 7.0, 0), (10.0, 0.4, 10.0), "drywood", c=2.6, ink=PROP_INK * 0.7)
    block(p, (0, 16.6, 3.2), (1.2, 1.2, 2.0), "timber", c=0.2)  # windshaft housing
    return p


def windmill_sails():
    hub = (0.0, 16.6, 4.4)
    p = Part("WindmillSails", origin=hub, outline=PROP_INK)
    rotor(p, hub, 10.0, 4, "timber", 0.9, sail="white")
    return p


def crop(name, row_color, soil, row_h=0.9, row_w=2.0, spacing=3.3, size=40.0):
    p = Part(name, outline=PROP_INK * 0.6)
    half = size * 0.5
    block(p, (0, -0.65, 0), (size, 1.7, size), soil, c=0.4, ink=PROP_INK * 0.6)
    n = int(size / spacing)
    for k in range(n):
        z = -half + spacing * (k + 0.5)
        tri = lambda x: [(x, 0.15, z - row_w * 0.5), (x, 0.15 + row_h, z), (x, 0.15, z + row_w * 0.5)]  # noqa: E731
        p.loft([tri(-half + 0.8), tri(half - 0.8)], row_color, cap_start=row_color, cap_end=row_color, outline=False)
    return p


def fence():
    p = Part("Fence", outline=PROP_INK * 0.5)
    for x in (-6.0, -2.0, 2.0, 6.0):
        block(p, (x, 0.6, 0), (0.3, 2.4, 0.3), "timber", c=0.05, ink=PROP_INK * 0.5)
    for y in (0.7, 1.4):
        block(p, (0, y, 0), (12.2, 0.22, 0.14), "drywood", c=0.03, ink=PROP_INK * 0.4)
    return p


def hay():
    bale = Part("HayBale", outline=PROP_INK * 0.6)
    poly = ngon_pts(1.3, 10)
    bale.loft([[(x, z + 1.25, y) for y, z in poly] for x in (-1.1, 1.1)], "wheat", cap_start="drywood", cap_end="drywood")
    bale_b = [[(x, z + 1.25, y + 3.0) for y, z in poly] for x in (-1.1, 1.1)]
    bale.loft(bale_b, "wheat", cap_start="drywood", cap_end="drywood")
    stack = Part("HayStack", outline=PROP_INK * 0.6)
    lump(stack, (0, 1.6, 0), (3.4, 2.6, 3.0), 61, "wheat", bumps=0.12, floor=-1.8)
    lump(stack, (0.4, 3.8, 0.2), (2.0, 1.4, 1.8), 62, "wheat", bumps=0.1)
    beam(stack, (0, 2.0, -3.4), (0.3, 5.4, -2.0), 0.08, "drywood")  # a pitchfork left in it
    return [bale, stack]


def cow():
    p = Part("Cow", outline=PROP_INK * 0.5)
    block(p, (0, 1.9, 0), (1.5, 1.4, 2.8), "white", c=0.3, ink=PROP_INK * 0.5)
    for (x, y, z, sx, sz) in ((0.76, 2.0, 0.3, 0.05, 1.0), (-0.76, 1.8, -0.6, 0.05, 0.9)):
        block(p, (x, y, z), (sx, 0.8, sz), "char", c=0.0, ink=False)
    block(p, (0.3, 2.62, -0.4), (0.7, 0.05, 1.0), "char", c=0.0, ink=False)
    block(p, (0, 2.3, 1.75), (0.8, 0.8, 0.9), "white", c=0.15, ink=PROP_INK * 0.5)  # head
    block(p, (0, 2.05, 2.25), (0.7, 0.45, 0.3), "flower", c=0.1, ink=False)  # muzzle
    for x in (-0.3, 0.3):
        block(p, (x * 1.6, 2.85, 1.65), (0.15, 0.35, 0.15), "drywood", c=0.0, ink=False)  # horns
    for x in (-0.5, 0.5):
        for z in (-1.0, 1.0):
            block(p, (x, 0.6, z), (0.32, 1.4, 0.32), "white", c=0.05, ink=PROP_INK * 0.4)
            block(p, (x, 0.0, z), (0.34, 0.2, 0.34), "char", c=0.0, ink=False)
    beam(p, (0, 2.3, -1.4), (0, 1.2, -1.6), 0.07, "char")  # tail
    return p


def balloon():
    p = Part("HotAirBalloon", outline=PROP_INK)
    stripes = ["red", "sun", "white", "aegean", "sun", "red", "white", "aegean", "red", "sun", "white", "aegean"]
    path, radii = [], []
    for y, r in ((7.0, 1.2), (8.5, 3.6), (11.0, 6.6), (14.5, 8.6), (18.0, 8.8), (21.0, 7.4), (23.2, 4.4), (24.3, 1.0)):
        path.append((0.0, y, 0.0))
        radii.append(r)
    rings = [[(r * math.cos(2 * math.pi * k / 12), y, r * math.sin(2 * math.pi * k / 12)) for k in range(12)]
             for (_, y, _), r in zip(path, radii)]
    p.loft(rings, lambda b, s: stripes[s], cap_start="char", cap_end="red")
    block(p, (0, 0.6, 0), (2.4, 1.6, 2.4), "drywood", top="timber", c=0.2)  # basket
    for x, z in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        beam(p, (x * 1.1, 1.4, z * 1.1), (x * 0.85, 7.1, z * 0.85), 0.05, "char")
    cylinder(p, 0, 0, 4.0, 5.0, 0.5, "darkmetal", sides=6)  # burner
    block(p, (0, 5.6, 0), (0.5, 0.8, 0.5), "flame", c=0.1, ink=False)
    return p


def poplar():
    p = Part("TreePoplar", outline=PROP_INK * 0.7)
    tube(p, [(0, -0.5, 0), (0, 3.0, 0)], [0.45, 0.35], 5, "bark", cap_start="bark", cap_end="bark")
    lump(p, (0, 8.0, 0), (2.2, 6.0, 2.1), 71, "leaf", bumps=0.12)
    lump(p, (0.2, 13.4, 0.1), (1.5, 3.4, 1.4), 72, "crop", bumps=0.1)
    lump(p, (-0.3, 5.0, 0.4), (1.6, 2.4, 1.5), 73, "leaf", bumps=0.1)
    return p


def build():
    parts = [barn(), farmhouse(), church(), silo(), water_tower(), windpump(), windpump_rotor(), windmill(),
             windmill_sails(), crop("CropWheat", "wheat", "bark"), crop("CropGreen", "crop", "bark"),
             crop("CropPlowed", "bark", "timber", row_h=0.6), fence(), *hay(), cow(), balloon(), poplar()]
    export_pack("Prairie", parts)


def lay_out():
    import bpy
    lay_out_row(["Barn", "Farmhouse", "Church", "Silo", "WaterTower", "Windpump", "Windmill"], 30.0)
    lay_out_row(["CropWheat", "CropGreen", "CropPlowed"], 46.0, z=60.0)
    lay_out_row(["Fence", "HayBale", "HayStack", "Cow", "TreePoplar", "HotAirBalloon"], 16.0, z=-40.0)
    for rotor_name, body in (("WindpumpRotor", "Windpump"), ("WindmillSails", "Windmill")):
        o = bpy.data.objects[rotor_name]
        o.location = bpy.data.objects[body].location + o.location
        o.rotation_euler = (0.0, 0.0, 0.0)
        o.rotation_euler[1] = 0.0
        o.rotation_euler[0] = math.radians(90.0)  # Blender x: stands the rotor up to face Unity +z (-Blender y)
    _ = to_blender


SHOTS = [
    ("prairie_a", (-80.0, 70.0, -150.0), (0.0, 8.0, 0.0), 210.0),
    ("prairie_b", (90.0, 40.0, -90.0), (60.0, 10.0, 0.0), 80.0),
]

if __name__ == "__main__":
    main(build, lay_out, SHOTS)
