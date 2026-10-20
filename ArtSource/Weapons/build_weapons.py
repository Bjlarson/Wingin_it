"""
Builds the weapons pack (Design/03-Art/final-asset-list.md, Pack 2) from scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/Weapons/build_weapons.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Weapons/Weapons.fbx   one object per weapon, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png    the shared palette atlas (rewritten, same as the planes')
    ArtSource/Weapons/Weapons.blend                  the generated scene, for hand tweaks and renders

Every object points its nose down +Z (Unity) with its origin at the middle of its length, as the code flies them:
    Rocket, Missile, Bomb   flying ordnance, sized to the RocketStats / MissileStats / BombStats defaults; Unity scales
                            each so its nose tip sits at half the stats' length. Rocket and Missile carry their exhaust
                            flame on the glow material.
    Tracer, FlakShell       gun and AA tracers, a unit box (Unity scales them by the tracer width and length), glow only
    MuzzleFlash             a star with three blades, 1 m long from the muzzle, glow only (two-sided)

Glow faces sit on a fourth material, Weapon_Glow (unlit palette, two-sided, no outline). Ordnance keeps the plane
materials (Plane_Paint, Plane_Ink) with a thinner ink hull scaled to its size.
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import (MODELS_DIR, PALETTE_PATH, Model, Part, make_material, preview_arg, render_previews,  # noqa: E402
                       ring, to_blender)

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "Weapons", "Weapons.fbx")
BLEND_PATH = os.path.join(HERE, "Weapons.blend")

SIDES = 8  # ordnance cross-section


def star_ring(z, r_out, r_in, points, phase=0.0):
    """A ring whose points alternate between two radii: a jagged, toy-like flame edge."""
    return [((r_out if k % 2 == 0 else r_in) * math.cos(phase + math.pi * k / points),
             (r_out if k % 2 == 0 else r_in) * math.sin(phase + math.pi * k / points), z) for k in range(2 * points)]


def fins(p, count, r_root, r_tip, root, tip, thick, color, phase=math.pi / 4):
    """``count`` flat fins round the z axis: a root chord (z0, z1) on the body and a shorter, swept tip chord."""
    for a in range(count):
        ang = phase + a * 2 * math.pi / count
        dx, dy = math.cos(ang), math.sin(ang)
        nx, ny = -dy * thick, dx * thick
        sections = []
        for r, (z0, z1) in ((r_root, root), (r_tip, tip)):
            cx, cy = dx * r, dy * r
            sections.append([(cx + nx, cy + ny, z0), (cx + nx, cy + ny, z1), (cx - nx, cy - ny, z1), (cx - nx, cy - ny, z0)])
        p.loft(sections, color, cap_start=color, cap_end=color)


def exhaust(p, z0, r, length, points=5):
    """Exhaust flame behind a nozzle at ``z0``: a hot core that flares, then a jagged ember cone to a point."""
    p.loft([star_ring(z0 + 0.02, r * 0.9, r * 0.75, points), star_ring(z0 - length * 0.25, r * 1.35, r * 0.9, points)],
           "flame", glow=True)
    p.loft([star_ring(z0 - length * 0.25, r * 1.35, r * 0.9, points), star_ring(z0 - length * 0.55, r * 1.0, r * 0.55, points)],
           lambda b, s: "flame" if s % 2 == 0 else "ember", glow=True,
           fan_end=((0.0, 0.0, z0 - length), "ember"))


# ---------------------------------------------------------------------------------------------- ordnance


def build_rocket():
    """1.6 m unguided rocket (RocketStats.rocketLength): dark body, white band, yellow nose, four swept fins."""
    p = Part("Rocket", outline=0.022)
    r = 0.12
    p.loft([ring(-0.80, 0.085, SIDES), ring(-0.70, r, SIDES), ring(0.28, r, SIDES), ring(0.40, r, SIDES),
            ring(0.64, 0.085, SIDES), ring(0.80, 0.035, SIDES)],
           lambda b, s: ("char", "darkmetal", "white", "sun", "sun")[b],
           cap_start="char", cap_end="sun")
    fins(p, 4, r - 0.01, 0.30, (-0.76, -0.40), (-0.76, -0.60), 0.022, "metal")
    exhaust(p, -0.80, 0.08, 0.55)
    return p


def build_missile():
    """2.4 m homing missile (MissileStats.missileLength): white body, yellow nose, glass seeker, canards + tail fins."""
    p = Part("Missile", outline=0.028)
    r = 0.15
    p.loft([ring(-1.20, 0.10, SIDES), ring(-1.12, 0.125, SIDES), ring(-1.06, r, SIDES), ring(0.05, r, SIDES),
            ring(0.17, r, SIDES), ring(0.62, r, SIDES), ring(0.86, 0.125, SIDES), ring(1.04, 0.075, SIDES),
            ring(1.13, 0.045, SIDES)],
           lambda b, s: ("char", "darkmetal", "white", "darkmetal", "white", "sun", "sun", "sun")[b],
           cap_start="char", fan_end=((0.0, 0.0, 1.20), "glass"))
    fins(p, 4, r - 0.01, 0.42, (-1.15, -0.62), (-1.15, -0.92), 0.025, "metal")
    fins(p, 4, r - 0.01, 0.27, (0.30, 0.52), (0.32, 0.42), 0.02, "metal")
    exhaust(p, -1.20, 0.10, 0.80)
    return p


def build_bomb():
    """1.4 m bomb (BombStats.bombLength): fat dark teardrop with a yellow band and crossed tail plates (as on the Heavy)."""
    p = Part("Bomb", outline=0.028)
    p.loft([ring(-0.42, 0.07, SIDES), ring(-0.18, 0.16, SIDES), ring(0.08, 0.21, SIDES), ring(0.32, 0.21, SIDES),
            ring(0.42, 0.195, SIDES), ring(0.55, 0.16, SIDES), ring(0.64, 0.10, SIDES)],
           lambda b, s: ("darkmetal", "darkmetal", "darkmetal", "sun", "darkmetal", "darkmetal")[b],
           cap_start="darkmetal", fan_end=((0.0, 0.0, 0.70), "darkmetal"))
    for a in range(2):  # two crossed plates, like the bomb slung under the Heavy Fighter
        ang = math.pi / 4 + a * math.pi / 2
        dx, dy = math.cos(ang) * 0.27, math.sin(ang) * 0.27
        nx, ny = -math.sin(ang) * 0.025, math.cos(ang) * 0.025
        p.loft([[(dx + nx, dy + ny, z), (-dx + nx, -dy + ny, z), (-dx - nx, -dy - ny, z), (dx - nx, dy - ny, z)]
                for z in (-0.70, -0.36)], "metal", cap_start="metal", cap_end="metal")
    return p


# ---------------------------------------------------------------------------------------------- glow FX


def build_tracer(name, body, head):
    """A unit-box bolt (Unity scales it by the tracer's width and length): white-hot head, coloured body, thin tail."""
    p = Part(name)
    p.loft([ring(-0.5, 0.07, 6), ring(-0.15, 0.30, 6), ring(0.22, 0.5, 6)],
           lambda b, s: body, cap_start=body, fan_end=((0.0, 0.0, 0.5), head), glow=True)
    return p


def build_muzzle_flash():
    """A star facing forward with three blades shooting ahead of it, 1 m long. Glow, two-sided, no outline."""
    p = Part("MuzzleFlash")
    points, r_out, r_in, z = 7, 0.5, 0.17, 0.02
    inner = [p.vert((r_in * math.cos(2 * math.pi * k / points + math.pi / points),
                     r_in * math.sin(2 * math.pi * k / points + math.pi / points), z)) for k in range(points)]
    outer = [p.vert((r_out * math.cos(2 * math.pi * k / points), r_out * math.sin(2 * math.pi * k / points), z))
             for k in range(points)]
    faces = [p.face(list(reversed(inner)), "glint", glow=True)]
    for k in range(points):
        faces.append(p.face([inner[k - 1], outer[k], inner[k]][::-1], "flame", glow=True))
    for a in range(3):  # blades: diamonds in planes through the z axis
        ang = a * math.pi / 3 + math.pi / 6
        ux, uy = math.cos(ang), math.sin(ang)
        blade = [(0.0, 0.0, -0.04), (ux * 0.2, uy * 0.2, 0.32), (0.0, 0.0, 1.0), (-ux * 0.2, -uy * 0.2, 0.32)]
        faces.append(p.face([p.vert(v) for v in blade], "flame", glow=True))
        core = [(0.0, 0.0, 0.0), (ux * 0.09, uy * 0.09, 0.2), (0.0, 0.0, 0.55), (-ux * 0.09, -uy * 0.09, 0.2)]
        n = (-uy * 0.004, ux * 0.004, 0.0)  # just off the blade so it doesn't z-fight
        faces.append(p.face([p.vert(tuple(c + o for c, o in zip(v, n))) for v in core], "glint", glow=True))
        faces.append(p.face([p.vert(tuple(c - o for c, o in zip(v, n))) for v in core], "glint", glow=True))
    p.loose_faces.extend(faces)
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
    model = Model("Weapons")
    materials = model.materials + [glow_material()]
    for part in (build_rocket(), build_missile(), build_bomb(),
                 build_tracer("Tracer", "flame", "glint"), build_tracer("FlakShell", "ember", "flame"),
                 build_muzzle_flash()):
        obj = part.finish(model.collection, materials)
        bpy.ops.object.material_slot_remove_unused()  # finish() leaves it active; drop slots it doesn't use
        model.objects.append(obj)
    model.export(FBX_PATH, BLEND_PATH)


# Preview layout: Unity x offsets, with the tracers at their in-game GunStats / AAStats sizes.
LAYOUT = {"Bomb": (-1.6, None), "Rocket": (0.0, None), "Missile": (1.6, None),
          "Tracer": (3.0, (0.25, 0.25, 6.0)), "FlakShell": (4.0, (0.35, 0.35, 8.0)), "MuzzleFlash": (-3.2, None)}

SHOTS = [
    ("weapons_threequarter", (-6.0, 3.2, 6.5), (0.4, 0.0, 0.0), None),
    ("weapons_front", (0.4, 1.2, 9.0), (0.4, 0.0, 0.0), None),
    ("weapons_top", (0.4, 12.0, 0.0), (0.4, 0.0, 0.0), 10.0),
    ("weapons_rear", (3.5, 2.0, -7.5), (0.4, 0.0, 0.0), None),
    ("weapons_flash", (-2.3, 0.7, 2.4), (-3.2, 0.0, 0.3), None),
]


def lay_out():
    for o in bpy.data.objects:
        if o.name in LAYOUT:
            x, scale = LAYOUT[o.name]
            o.location = to_blender((x, 0.0, 0.0))
            if scale:
                o.scale = to_blender(scale)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
