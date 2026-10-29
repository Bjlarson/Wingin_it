"""
The Alpine pack: the Mountain Range's ski village, cable car and frozen woods, so its valleys read as a winter
resort among the peaks rather than empty snow.

    blender --background --factory-startup --python ArtSource/MapPacks/build_alpine.py -- [--preview <dir>]

Writes Assets/_Project/Art/Models/Alpine/Alpine.fbx. Each object stands on its origin (y = 0 is the ground):
    Chalet            a big ski lodge: stone ground floor, timber above, a snowy gable roof and a balcony, 19 m
    LogCabin          a small log cabin with a snowy roof
    CableStation      the cable car's end station; marker CableStationCable is where the two cables leave it (+z)
    CablePylon        a 28 m lattice pylon; marker CablePylonCable is the cables' height over its origin
    Gondola           a cabin on its hanger arm; origin at the grip, which rides the cable
    RangerTower       a timber lookout on four legs, 17 m
    TreeFirTall       a tall, snow-laden fir, 21 m
    TreeBirchWinter   a bare white birch
    IceShards         a cluster of blue ice crystals for the high ridges
    FrozenPond        a 70 m disc of ice with a snow rim, its slab sunk 1.5 m
    Snowman, Signpost
Cables run ±3 m either side of the line (x), so one track goes up and the other comes down.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pack_kit import (PROP_INK, Part, beam, block, cone_tier, cylinder, door, export_pack, gable, gable_end,  # noqa: E402
                      lattice, lay_out_row, lump, main, ngon_pts, prism, rect4, side, snow_on_top, tube, wall_quad)

CABLE_X = 3.0
PYLON_H = 28.0
STATION_CABLE = (0.0, 9.0, 8.0)


def chalet():
    p = Part("Chalet", outline=PROP_INK * 1.2)
    w, d = 14.0, 18.0
    walls = rect4(w, d)
    prism(p, walls, -1.5, 3.5, "stone", outline=PROP_INK * 1.2)
    prism(p, walls, 3.5, 10.0, "timber", outline=PROP_INK * 1.2)
    gable_end(p, w - 0.05, d - 0.05, 10.0, 6.5, "timber")
    gable(p, w, d, 10.0, 6.5, "timber", under="drywood", overhang=2.0, snow="snow")
    for n in ((1, 0), (-1, 0)):
        f = side(walls, n)
        for a in (-6.0, -2.0, 2.0, 6.0):
            door(p, f, a, 1.6, 1.8, "window", 1.0)
            door(p, f, a, 1.6, 1.8, "window", 5.2)
            wall_quad(p, f, a - 1.4, a - 0.8, 5.2, 7.0, "barnred")
            wall_quad(p, f, a + 0.8, a + 1.4, 5.2, 7.0, "barnred")
    front = side(walls, (0, 1))
    door(p, front, 0.0, 2.6, 2.8, "drywood")
    for a in (-4.5, 4.5):
        door(p, front, a, 2.0, 2.0, "window", 5.0)
    door(p, front, 0.0, 2.4, 2.4, "window", 11.2)
    # Balcony round the front gable, on brackets.
    block(p, (0, 4.4, d * 0.5 + 1.4), (w + 1.0, 0.4, 2.8), "drywood", c=0.1)
    block(p, (0, 5.3, d * 0.5 + 2.7), (w + 1.0, 1.4, 0.2), "timber", c=0.0, ink=PROP_INK * 0.5)
    for x in (-6.5, 6.5):
        beam(p, (x, 2.4, d * 0.5), (x, 4.2, d * 0.5 + 2.6), 0.18, "timber", ink=PROP_INK * 0.4)
    block(p, (4.0, 16.0, -5.0), (1.8, 5.0, 1.8), "stone", top="snow", c=0.2)  # chimney
    return p


def log_cabin():
    p = Part("LogCabin", outline=PROP_INK)
    w, d, h = 8.0, 10.0, 4.2
    walls = rect4(w, d)
    prism(p, walls, -1.0, h, "timber", outline=PROP_INK)
    for y in (0.6, 1.5, 2.4, 3.3):  # log courses, poking out at the corners
        for x in (-w * 0.5, w * 0.5):
            tube(p, [(x, y, -d * 0.5 - 0.5), (x, y, d * 0.5 + 0.5)], [0.28, 0.28], 5, "bark", cap_start="drywood",
                 cap_end="drywood", outline=PROP_INK * 0.4)
    gable_end(p, w - 0.05, d - 0.05, h, 3.4, "timber")
    gable(p, w, d, h, 3.4, "timber", under="timber", overhang=1.0, snow="snow")
    door(p, side(walls, (0, 1)), -1.6, 1.4, 2.4, "drywood")
    door(p, side(walls, (0, 1)), 1.8, 1.4, 1.2, "flame", 1.4)  # a lit window
    door(p, side(walls, (1, 0)), 0.0, 1.4, 1.2, "flame", 1.4)
    block(p, (-2.4, 7.0, -2.0), (1.2, 4.0, 1.2), "stone", top="snow", c=0.15)
    block(p, (2.2, 0.6, 6.6), (3.0, 1.2, 1.4), "drywood", top="snow", c=0.2)  # woodpile
    return p


def cable_station():
    p = Part("CableStation", outline=PROP_INK * 1.2)
    walls = rect4(14.0, 12.0)
    prism(p, walls, -1.5, 6.0, "concrete", outline=PROP_INK * 1.2)
    for a in (-4.0, 0.0, 4.0):
        door(p, side(walls, (1, 0)), a, 2.2, 2.0, "window", 2.6)
        door(p, side(walls, (-1, 0)), a, 2.2, 2.0, "window", 2.6)
    door(p, side(walls, (0, -1)), 0.0, 3.0, 3.0, "slate")
    # Open-fronted wheel house on top, the bull wheel inside, the cables leaving out of +z.
    block(p, (0, 6.3, 0), (15.0, 0.6, 13.0), "snow", c=0.4)
    for x in (-7.0, 7.0):
        block(p, (x, 9.0, 1.0), (0.8, 5.0, 11.0), "barnred", c=0.1)
    block(p, (0, 11.8, 1.0), (15.2, 0.8, 12.0), "barnred", top="snow", c=0.3)
    wheel = ngon_pts(CABLE_X, 14)
    p.loft([[(x, y, z + 4.0) for x, z in wheel] for y in (8.6, 9.4)], "darkmetal", cap_start="darkmetal",
           cap_end="metal", outline=PROP_INK * 0.6)
    cylinder(p, 0, 4.0, 6.6, 9.4, 0.5, "darkmetal", sides=6)
    block(p, (5.0, 13.4, -3.0), (4.0, 2.4, 0.3), "sun", c=0.0, ink=PROP_INK * 0.5)  # sign
    return p


def cable_pylon():
    p = Part("CablePylon", outline=PROP_INK)
    lattice(p, -1.0, PYLON_H - 1.0, 3.2, 1.0, "metal", sides=4, bays=5, member=0.2)
    block(p, (0, PYLON_H - 0.6, 0), (CABLE_X * 2 + 2.4, 0.8, 1.4), "sun", c=0.1)  # crossarm
    for x in (-CABLE_X, CABLE_X):
        cylinder(p, x, 0, PYLON_H - 0.2, PYLON_H + 0.5, 0.25, "darkmetal", sides=4)  # sheave heads
        block(p, (x, PYLON_H + 0.6, 0), (0.5, 0.5, 2.6), "darkmetal", c=0.1, ink=PROP_INK * 0.5)
    return p


def gondola():
    p = Part("Gondola", outline=PROP_INK * 0.8)
    block(p, (0, 0.0, 0), (0.6, 0.5, 1.2), "darkmetal", c=0.1)  # grip on the cable
    beam(p, (0, -0.2, 0), (0, -3.0, 0), 0.12, "darkmetal")
    cabin = rect4(2.6, 3.0)
    prism(p, cabin, -6.4, -3.6, "red", top="white", bottom="char", outline=PROP_INK * 0.8)
    for n in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        door(p, side(cabin, n), 0.0, 2.2 if n[0] else 1.8, 1.2, "window", -5.2)
    block(p, (0, -3.4, 0), (2.0, 0.4, 2.4), "white", c=0.3, ink=PROP_INK * 0.5)
    return p


def ranger_tower():
    p = Part("RangerTower", outline=PROP_INK)
    deck = 12.0
    for x in (-1, 1):
        for z in (-1, 1):
            beam(p, (x * 3.4, -1.0, z * 3.4), (x * 2.2, deck, z * 2.2), 0.3, "timber", ink=PROP_INK * 0.6)
    for y in (4.0, 8.0):
        t = (y + 1.0) / (deck + 1.0)
        r = 3.4 - 1.2 * t
        for a, b in (((r, r), (-r, r)), ((-r, r), (-r, -r)), ((-r, -r), (r, -r)), ((r, -r), (r, r))):
            beam(p, (a[0], y, a[1]), (b[0], y, b[1]), 0.16, "timber", ink=PROP_INK * 0.4)
    block(p, (0, deck + 0.2, 0), (6.0, 0.4, 6.0), "drywood", c=0.2)
    cab = rect4(4.8, 4.8)
    prism(p, cab, deck + 0.4, deck + 3.2, "timber", outline=PROP_INK)
    for n in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        door(p, side(cab, n), 0.0, 3.4, 1.4, "window", deck + 1.5)
    from pack_kit import hip
    hip(p, 4.8, 4.8, deck + 3.2, 2.2, "timber", overhang=0.8, snow="snow")
    return p


def fir_tall():
    p = Part("TreeFirTall", outline=PROP_INK * 0.7)
    tube(p, [(0, -0.5, 0), (0, 2.5, 0)], [0.55, 0.42], 6, "bark", cap_start="bark", cap_end="bark")
    tiers = [(1.6, 7.0, 3.6), (4.6, 10.0, 3.0), (7.6, 13.2, 2.5), (10.6, 16.4, 1.9), (13.6, 21.0, 1.3)]
    for i, (y0, y1, r) in enumerate(tiers):
        cone_tier(p, y0, y1, r, 8, 140 + i, snow=True)
        # A snow skirt along each tier's rim.
        p.loft([[(x, y0 + 0.3, z) for x, z in ngon_pts(r * 1.02, 8)], [(x, y0 + 0.9, z) for x, z in ngon_pts(r * 0.8, 8)]],
               "snow", outline=PROP_INK * 0.5)
    return p


def birch_winter():
    p = Part("TreeBirchWinter", outline=PROP_INK * 0.6)
    tube(p, [(0, -0.5, 0), (0.2, 4.0, 0.1), (0.1, 9.0, 0.3)], [0.38, 0.3, 0.16], 6,
         lambda b, s: "char" if (b + s) % 5 == 0 else "white", cap_start="white", cap_end="white")
    rnd = random.Random(151)
    for k in range(7):
        y = 3.5 + k * 0.8
        a = rnd.uniform(0, 2 * math.pi)
        L = 2.8 - k * 0.25
        tip = (math.cos(a) * L, y + 1.6, math.sin(a) * L)
        tube(p, [(0.15, y, 0.15), ((tip[0] + 0.15) * 0.5, y + 1.0, (tip[2] + 0.15) * 0.5), tip], [0.14, 0.1, 0.05], 4,
             "white", cap_end="white", outline=PROP_INK * 0.4)
        lump(p, (tip[0] * 0.7, y + 1.4, tip[2] * 0.7), (0.4, 0.15, 0.3), 160 + k, "snow", subdiv=1, bumps=0.05,
             outline=False)
    return p


def ice_shards():
    p = Part("IceShards", outline=PROP_INK)
    rnd = random.Random(171)
    for k in range(7):
        a = rnd.uniform(0, 2 * math.pi)
        d = 0.0 if k == 0 else rnd.uniform(1.5, 4.0)
        h = 9.0 if k == 0 else rnd.uniform(3.0, 6.5)
        r = 1.4 if k == 0 else rnd.uniform(0.6, 1.1)
        lean = (math.cos(a) * h * 0.25, math.sin(a) * h * 0.25)
        base = (math.cos(a) * d, math.sin(a) * d)
        ring = [(x, -0.5, z) for x, z in ngon_pts(r, 6, base[0], base[1])]
        mid = [(x + lean[0] * 0.6, h * 0.65, z + lean[1] * 0.6) for x, z in ngon_pts(r * 0.85, 6, base[0], base[1])]
        tip = (base[0] + lean[0], h, base[1] + lean[1])
        p.loft([ring, mid], lambda b, s: "ice" if s % 2 else "glass", cap_start="ice", fan_end=(tip, "white"))
    lump(p, (0, 0.2, 0), (4.0, 1.0, 3.6), 179, "snow", bumps=0.2, floor=-0.6)
    return p


def frozen_pond():
    p = Part("FrozenPond", outline=PROP_INK * 0.6)
    poly = ngon_pts(35.0, 18)
    rnd = random.Random(181)
    poly = [(x * rnd.uniform(0.9, 1.05), z * rnd.uniform(0.9, 1.05)) for x, z in poly]
    prism(p, poly, -1.5, 0.15, "ice", top="glass", outline=PROP_INK * 0.6)
    for k, (x, z) in enumerate(poly):
        if k % 2 == 0:
            lump(p, (x, 0.2, z), (5.0, 1.0, 4.0), 182 + k, "snow", bumps=0.2, floor=-0.6, outline=False)
    for k in range(3):  # cracks / skate marks
        a = rnd.uniform(0, math.pi)
        dx, dz = math.cos(a) * 18.0, math.sin(a) * 18.0
        ox, oz = rnd.uniform(-8, 8), rnd.uniform(-8, 8)
        w = (-dz / 18.0 * 0.3, dx / 18.0 * 0.3)
        from pack_kit import quad
        quad(p, [(ox - dx + w[0], 0.2, oz - dz + w[1]), (ox + dx + w[0], 0.2, oz + dz + w[1]),
                 (ox + dx - w[0], 0.2, oz + dz - w[1]), (ox - dx - w[0], 0.2, oz - dz - w[1])], "white", (0, 1, 0))
    return p


def snowman():
    p = Part("Snowman", outline=PROP_INK * 0.5)
    lump(p, (0, 0.9, 0), (1.3, 1.1, 1.3), 191, "snow", subdiv=2, bumps=0.04, floor=-0.8)
    lump(p, (0, 2.5, 0), (0.95, 0.85, 0.95), 192, "snow", subdiv=2, bumps=0.04)
    lump(p, (0, 3.7, 0), (0.65, 0.6, 0.65), 193, "snow", subdiv=2, bumps=0.04)
    p.loft([[(x, 3.75, z + 0.55) for x, z in ngon_pts(0.12, 5)]], "orange", cap_start="orange",
           fan_end=((0, 3.7, 1.25), "orange"))  # carrot
    cylinder(p, 0, 0, 4.15, 4.25, 0.6, "char", sides=8)  # hat
    cylinder(p, 0, 0, 4.25, 4.9, 0.42, "char", sides=8)
    block(p, (0, 3.2, 0), (1.5, 0.3, 1.5), "red", c=0.4, ink=PROP_INK * 0.4)  # scarf
    for x in (-0.25, 0.25):
        block(p, (x, 3.9, 0.58), (0.12, 0.12, 0.1), "char", c=0.0, ink=False)
    return p


def signpost():
    p = Part("Signpost", outline=PROP_INK * 0.5)
    cylinder(p, 0, 0, -0.5, 4.0, 0.15, "timber", sides=4, top="snow", ink=PROP_INK * 0.5)
    for y, yaw, color in ((3.3, 0.3, "sun"), (2.6, -0.5, "red"), (1.9, 1.2, "aegean")):
        dx, dz = math.cos(yaw) * 1.1, math.sin(yaw) * 1.1
        block(p, (dx, y, dz), (2.2 * abs(math.cos(yaw)) + 0.15, 0.5, 2.2 * abs(math.sin(yaw)) + 0.15), color, c=0.05,
              ink=PROP_INK * 0.4)
    return p


def build():
    parts = [chalet(), log_cabin(), cable_station(), cable_pylon(), gondola(), ranger_tower(), fir_tall(), birch_winter(),
             ice_shards(), frozen_pond(), snowman(), signpost()]
    export_pack("Alpine", parts, markers=[("CableStationCable", STATION_CABLE), ("CablePylonCable", (0.0, PYLON_H + 0.7, 0.0))])


def lay_out():
    lay_out_row(["Chalet", "LogCabin", "CableStation", "CablePylon", "RangerTower"], 26.0)
    lay_out_row(["TreeFirTall", "TreeBirchWinter", "IceShards", "Snowman", "Signpost", "Gondola"], 12.0, z=-30.0)
    import bpy
    from pack_kit import to_blender
    bpy.data.objects["Gondola"].location = to_blender((30.0, 8.0, -30.0))
    bpy.data.objects["FrozenPond"].location = to_blender((0.0, 0.0, 60.0))
    _ = snow_on_top


SHOTS = [
    ("alpine_a", (-70.0, 60.0, -120.0), (0.0, 8.0, 0.0), 150.0),
    ("alpine_b", (50.0, 30.0, -80.0), (10.0, 6.0, -20.0), 70.0),
]

if __name__ == "__main__":
    main(build, lay_out, SHOTS)
