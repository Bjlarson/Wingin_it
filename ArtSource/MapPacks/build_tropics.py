"""
The Tropics pack: The Island (v2)'s fishing villages, boats and jungle, so its coasts and plateau read as a lived-in
tropical island rather than bare green.

    blender --background --factory-startup --python ArtSource/MapPacks/build_tropics.py -- [--preview <dir>]

Writes Assets/_Project/Art/Models/Tropics/Tropics.fbx (and the shared palette). Each object stands on its origin:
    StiltHut          a thatched hut on stilts; origin at the water / sand line, the floor 3.5 m up
    BeachHut          a round hut with a cone of thatch
    LongHouse         the village's big house: a long thatched gable on a low plinth
    Jetty             a plank pier 40 m out along +z from its origin on the shore, deck at y = 1.8
    Outrigger         a canoe with its float and a crab-claw sail; origin at the waterline
    Sailboat          a white yacht (also sails the Isles); origin at the waterline
    StoneIdol         a 9 m stone head watching the sea from a headland
    Lookout           a bamboo lookout tower on stilts, 15 m
    TreeBanana        broad paddle leaves on a short trunk
    TreeJungle        a wide umbrella canopy tree, 16 m
    BushFlower        a hibiscus bush dotted with flowers
The Island also uses the nature pack's palms and oaks and the landmarks pack's lighthouse.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pack_kit import (PROP_INK, Part, beam, block, cylinder, door, export_pack, gable, gable_end, hip, lay_out_row, lump,
                      main, ngon_pts, prism, rect4, rect_pts, side, tube, yring)  # noqa: E402


def stilts(p, xs, zs, y0, y1, r=0.3, color="drywood"):
    for x in xs:
        for z in zs:
            cylinder(p, x, z, y0, y1, r, color, sides=5, ink=PROP_INK * 0.6)


def stilt_hut():
    p = Part("StiltHut", outline=PROP_INK)
    floor = 3.5
    stilts(p, (-3.2, 0.0, 3.2), (-2.6, 2.6), -3.0, floor)
    block(p, (0, floor + 0.25, 0.8), (8.4, 0.5, 8.6), "drywood", c=0.2)  # deck, a porch out front (+z)
    walls = rect4(6.4, 5.4)
    prism(p, walls, floor + 0.5, floor + 3.6, "khaki", outline=PROP_INK)
    door(p, side(walls, (0, 1)), 0.0, 1.4, 2.2, "timber", floor + 0.5)
    door(p, side(walls, (1, 0)), 0.0, 1.4, 1.0, "char", floor + 2.0)
    door(p, side(walls, (-1, 0)), 0.0, 1.4, 1.0, "char", floor + 2.0)
    hip(p, 6.4, 5.4, floor + 3.6, 3.8, "thatch", overhang=1.3)
    # Ladder down the front.
    for x in (-0.6, 0.6):
        beam(p, (x, -0.5, 6.2), (x, floor, 5.0), 0.12, "drywood", ink=PROP_INK * 0.4)
    for k in range(1, 5):
        y = k * floor / 5
        z = 6.2 - (6.2 - 5.0) * (y + 0.5) / (floor + 0.5)
        beam(p, (-0.6, y, z), (0.6, y, z), 0.08, "drywood")
    return p


def beach_hut():
    p = Part("BeachHut", outline=PROP_INK)
    walls = ngon_pts(3.0, 8)
    prism(p, walls, -0.8, 2.6, "khaki", outline=PROP_INK)
    door(p, side(walls, (0, 1)), 0.0, 1.3, 2.0, "timber")
    hip(p, 6.0, 6.0, 2.6, 3.4, "thatch", overhang=1.1, sides=9)
    return p


def long_house():
    p = Part("LongHouse", outline=PROP_INK)
    block(p, (0, 0.0, 0), (10.0, 1.6, 20.0), "stone", top="drywood", c=0.4)  # plinth
    walls = rect4(8.0, 18.0)
    prism(p, walls, 0.8, 3.8, "khaki", outline=PROP_INK)
    gable_end(p, 7.95, 17.95, 3.8, 4.5, "drywood")
    gable(p, 8.0, 18.0, 3.8, 4.5, "thatch", overhang=1.6)
    door(p, side(walls, (0, 1)), 0.0, 1.8, 2.4, "timber", 0.8)
    for a in (-6.0, -2.0, 2.0, 6.0):
        door(p, side(walls, (1, 0)), a, 1.4, 1.0, "char", 2.2)
        door(p, side(walls, (-1, 0)), a, 1.4, 1.0, "char", 2.2)
    # Carved posts at the gable's foot.
    for x in (-4.8, 4.8):
        cylinder(p, x, 10.0, 0.0, 5.0, 0.35, "timber", sides=6, top="sun")
    return p


def jetty():
    p = Part("Jetty", outline=PROP_INK)
    deck = 1.8
    length = 40.0
    block(p, (0, deck, length * 0.5), (4.0, 0.4, length), "drywood", c=0.1)
    for z in range(2, int(length) + 1, 5):
        for x in (-1.8, 1.8):
            cylinder(p, x, z, -6.0, deck + (1.2 if z % 10 == 2 else 0.0), 0.25, "timber", sides=5, ink=PROP_INK * 0.6)
    # A T at the end, with a little lamp post.
    block(p, (0, deck, length + 2.0), (12.0, 0.4, 4.0), "drywood", c=0.1)
    for x in (-5.5, 5.5):
        cylinder(p, x, length + 2.0, -6.0, deck, 0.25, "timber", sides=5, ink=PROP_INK * 0.6)
    cylinder(p, 5.0, length + 3.2, deck, deck + 4.0, 0.12, "darkmetal", sides=4)
    block(p, (5.0, deck + 4.3, length + 3.2), (0.6, 0.6, 0.6), "flame", c=0.1, ink=PROP_INK * 0.4)
    return p


def canoe_hull(p, length, width, depth, color, stripe=None, y=0.0):
    rings = []
    for t, w, d in ((0.0, 0.05, 0.6), (0.12, 0.6, 0.85), (0.5, 1.0, 1.0), (0.88, 0.6, 0.85), (1.0, 0.05, 0.6)):
        z = -length * 0.5 + length * t
        hw = width * 0.5 * w
        lift = (1.0 - d) * depth * 1.2
        rings.append([(hw, y + depth + lift, z), (hw * 0.8, y - depth * 0.2 + lift, z), (0, y - depth * d + lift, z),
                      (-hw * 0.8, y - depth * 0.2 + lift, z), (-hw, y + depth + lift, z)])
    p.loft(rings, lambda b, s: stripe if stripe and s in (0, 3) else color, cap_start=color, cap_end=color)


def outrigger():
    p = Part("Outrigger", outline=PROP_INK)
    canoe_hull(p, 9.0, 1.4, 0.6, "timber", stripe="red")
    tube(p, [(-3.4, 0.25, -3.0), (-3.4, 0.3, 0.0), (-3.4, 0.25, 3.0)], [0.15, 0.32, 0.15], 6, "drywood",
         cap_start="drywood", cap_end="drywood")  # the float, out to port
    for z in (-1.5, 1.5):
        beam(p, (0.6, 0.7, z), (-3.4, 0.5, z), 0.12, "drywood", ink=PROP_INK * 0.4)
    # Crab-claw sail.
    beam(p, (0, 0.4, 0.8), (0, 6.5, 0.0), 0.12, "timber", ink=PROP_INK * 0.4)
    sail = [(0, 1.0, 2.6), (0, 6.6, -0.2), (0, 3.2, -2.4)]
    p.loft([sail, [(0.12, y, z) for _, y, z in sail]], "white", cap_start="white", cap_end="white", outline=PROP_INK * 0.5)
    return p


def sailboat():
    p = Part("Sailboat", outline=PROP_INK)
    hull = []
    for y, w, l in ((-1.6, 1.2, 9.0), (-0.6, 3.4, 13.0), (0.4, 4.2, 14.5), (1.2, 4.4, 15.0)):
        hw, hl = w * 0.5, l * 0.5
        hull.append([(hw, y, -hl + 1.0), (hw, y, hl - 4.5), (0.0, y, hl), (-hw, y, hl - 4.5), (-hw, y, -hl + 1.0),
                     (-hw * 0.6, y, -hl), (hw * 0.6, y, -hl)])
    p.loft(hull, lambda b, s: "aegean" if b == 1 else "white", cap_start="white", cap_end="drywood")
    block(p, (0, 1.9, -1.5), (2.6, 1.4, 4.6), "white", top="concrete", c=0.5)
    cylinder(p, 0, 1.2, 1.2, 18.0, 0.18, "metal", sides=6)
    # Main sail aft of the mast, jib forward of it.
    main_sail = [(0, 2.8, 1.0), (0, 17.4, 1.0), (0, 2.8, -5.6)]
    p.loft([main_sail, [(0.12, y, z) for _, y, z in main_sail]], lambda b, s: "white", cap_start="white", cap_end="white",
           outline=PROP_INK * 0.5)
    jib = [(0, 2.4, 1.6), (0, 15.6, 1.4), (0, 1.6, 7.2)]
    p.loft([jib, [(0.12, y, z) for _, y, z in jib]], "sun", cap_start="sun", cap_end="sun", outline=PROP_INK * 0.5)
    beam(p, (0, 2.8, 1.0), (0, 2.8, -5.8), 0.12, "metal")
    return p


def stone_idol():
    p = Part("StoneIdol", outline=PROP_INK * 2)
    # Torso block sunk in the ground, the long head on it.
    block(p, (0, 0.5, 0), (4.6, 5.0, 3.4), "stone", c=0.8, ink=PROP_INK * 2)
    head = []
    for y, w, d, z in ((2.8, 3.6, 3.0, 0.0), (5.0, 3.8, 3.4, 0.2), (8.2, 3.6, 3.2, 0.1), (9.4, 3.2, 2.8, -0.1)):
        head.append([(x, y, z + zz) for x, zz in rect_pts(w, d, 0.6)])
    p.loft(head, "stone", cap_start="stone", cap_end="stone", outline=PROP_INK * 2)
    block(p, (0, 6.4, 1.95), (0.9, 2.6, 1.0), "stone", c=0.3, ink=PROP_INK)  # nose
    block(p, (0, 7.6, 1.6), (3.8, 0.6, 1.0), "stone", c=0.2, ink=PROP_INK)  # brow
    for x in (-0.9, 0.9):
        block(p, (x, 6.9, 1.75), (0.9, 0.5, 0.2), "char", c=0.1, ink=False)  # eyes in shadow
    block(p, (0, 4.6, 1.75), (1.6, 0.3, 0.2), "char", c=0.05, ink=False)
    block(p, (0, 10.0, -0.1), (3.0, 1.2, 2.6), "clay", c=0.5)  # topknot
    return p


def lookout():
    p = Part("Lookout", outline=PROP_INK)
    deck = 11.0
    for x in (-2.0, 2.0):
        for z in (-2.0, 2.0):
            beam(p, (x * 1.5, -1.0, z * 1.5), (x, deck, z), 0.28, "drywood", ink=PROP_INK * 0.6)
    for y in (3.0, 7.0):
        t = (y + 1.0) / (deck + 1.0)
        r = 3.0 - 1.0 * t
        for a, b in (((r, r), (-r, r)), ((-r, r), (-r, -r)), ((-r, -r), (r, -r)), ((r, -r), (r, r))):
            beam(p, (a[0], y, a[1]), (b[0], y, b[1]), 0.16, "drywood", ink=PROP_INK * 0.4)
    block(p, (0, deck + 0.2, 0), (5.6, 0.4, 5.6), "drywood", c=0.2)
    for x in (-2.6, 2.6):
        for z in (-2.6, 2.6):
            cylinder(p, x, z, deck, deck + 2.6, 0.15, "drywood", sides=4, ink=PROP_INK * 0.4)
    prism(p, rect4(5.4, 5.4), deck + 0.4, deck + 1.4, "khaki", outline=PROP_INK * 0.6)
    hip(p, 5.4, 5.4, deck + 2.6, 2.6, "thatch", overhang=0.9)
    cylinder(p, 0, 0, deck + 5.2, deck + 7.4, 0.08, "darkmetal", sides=4)
    block(p, (0.6, deck + 6.9, 0), (1.2, 0.7, 0.05), "red", c=0.0, ink=False)  # pennant
    return p


def tree_banana():
    p = Part("TreeBanana", outline=PROP_INK * 0.7)
    tube(p, [(0, -0.5, 0), (0.1, 2.0, 0), (0.2, 4.2, 0.1)], [0.42, 0.34, 0.28], 6, "crop", cap_start="crop", cap_end="crop")
    rnd = random.Random(7)
    for k in range(6):
        a = 2 * math.pi * k / 6 + rnd.uniform(-0.2, 0.2)
        d = (math.cos(a), math.sin(a))
        s = (-d[1], d[0])
        base = (0.2, 4.0, 0.1)
        rings = []
        for t, w, up in ((0.0, 0.1, 0.0), (0.35, 1.0, 1.0), (0.75, 0.9, 0.6), (1.0, 0.15, -0.6)):
            c = (base[0] + d[0] * 4.4 * t, base[1] + up * 1.4 + t * 0.6, base[2] + d[1] * 4.4 * t)
            hw = 0.75 * w
            rings.append([(c[0] + s[0] * hw, c[1], c[2] + s[1] * hw), (c[0], c[1] + 0.12, c[2]),
                          (c[0] - s[0] * hw, c[1], c[2] - s[1] * hw), (c[0], c[1] - 0.12, c[2])])
        p.loft(rings, "leaf" if k % 2 else "grass", cap_start="grass", cap_end="grass")
    lump(p, (0.5, 3.4, 0.4), (0.4, 0.6, 0.4), 9, "wheat", subdiv=1, bumps=0.1)  # a bunch of fruit
    return p


def tree_jungle():
    p = Part("TreeJungle", outline=PROP_INK)
    tube(p, [(0, -0.5, 0), (0.3, 4.0, 0), (0.6, 8.5, 0.3)], [1.0, 0.7, 0.55], 7, "bark", cap_start="bark", cap_end="bark")
    for path in (((0.5, 7.0, 0.2), (-2.5, 10.0, 1.2), (-4.8, 11.5, 1.8)), ((0.6, 7.5, 0.2), (3.0, 10.5, -1.4), (5.2, 11.6, -2.2)),
                 ((0.6, 8.0, 0.3), (1.0, 11.0, 3.0), (1.4, 12.2, 5.0))):
        tube(p, path, [0.4, 0.3, 0.22], 5, "bark", cap_end="bark")
    # Flat umbrella canopy in overlapping lumps.
    lump(p, (0.4, 13.2, 0.4), (7.0, 2.2, 6.6), 31, "leaf", bumps=0.14)
    lump(p, (-4.4, 12.2, 1.8), (3.6, 1.6, 3.4), 32, "pine", bumps=0.14)
    lump(p, (4.8, 12.4, -2.0), (3.6, 1.6, 3.4), 33, "leaf", bumps=0.14)
    lump(p, (1.4, 12.6, 5.0), (3.2, 1.5, 3.0), 34, "pine", bumps=0.14)
    # Buttress roots.
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        tube(p, [(math.cos(a) * 0.6, 2.2, math.sin(a) * 0.6), (math.cos(a) * 1.8, 0.4, math.sin(a) * 1.8),
                 (math.cos(a) * 2.2, -0.4, math.sin(a) * 2.2)], [0.3, 0.3, 0.25], 4, "bark", cap_end="bark")
    return p


def bush_flower():
    p = Part("BushFlower", outline=PROP_INK * 0.6)
    lump(p, (0, 1.0, 0), (1.8, 1.3, 1.7), 41, "leaf", floor=-0.9)
    lump(p, (1.3, 0.8, 0.6), (1.1, 1.0, 1.0), 42, "grass", floor=-0.7)
    rnd = random.Random(43)
    for k in range(9):
        a = rnd.uniform(0, 2 * math.pi)
        y = rnd.uniform(0.9, 2.1)
        r = 1.7 * math.sqrt(max(0.0, 1.0 - ((y - 1.0) / 1.4) ** 2)) + 0.05
        x, z = math.cos(a) * r, math.sin(a) * r
        lump(p, (x, y, z), (0.32, 0.32, 0.32), 44 + k, "flower" if k % 3 else "red", subdiv=1, bumps=0.05, outline=False)
    return p


PARTS = [stilt_hut, beach_hut, long_house, jetty, outrigger, sailboat, stone_idol, lookout, tree_banana,
         tree_jungle, bush_flower]


def build():
    export_pack("Tropics", [fn() for fn in PARTS])


def lay_out():
    lay_out_row(["StiltHut", "BeachHut", "LongHouse", "StoneIdol", "Lookout"], 22.0)
    lay_out_row(["TreeBanana", "TreeJungle", "BushFlower", "Outrigger", "Sailboat"], 18.0, z=40.0)
    import bpy
    from pack_kit import to_blender
    bpy.data.objects["Jetty"].location = to_blender((0.0, 0.0, 70.0))


SHOTS = [
    ("tropics_a", (-60.0, 50.0, -110.0), (0.0, 6.0, 20.0), 130.0),
    ("tropics_b", (60.0, 45.0, -60.0), (0.0, 4.0, 55.0), 110.0),
]

if __name__ == "__main__":
    main(build, lay_out, SHOTS)
