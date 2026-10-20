"""
Builds the Interceptor hero model (Design/03-Art/lowpoly-asset-style-guide.html: "sleek delta, canards, jet") from
scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/Interceptor/build_interceptor.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Interceptor/Interceptor.fbx   one object per modular part + muzzle markers, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png            the shared palette (same as every plane writes)
    ArtSource/Interceptor/Interceptor.blend                  the generated scene, for hand tweaks and renders

A nose-intake jet laid out on the shared prefab's parts, so its hitboxes still line up: the Engine is the intake (the
cowling box sits there), the "Propeller" is the compressor fan and striped shock cone that spin in it, the horizontal
stabilizer is a small swept tailplane behind the delta (MiG-21 style) and the canards are fixed to the body. The
guns sit in the wing roots, so the model exports Muzzle* markers that Unity moves the gun muzzles to.
Shared helpers, pivots and style rules: ArtSource/plane_kit.py.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import (ENGINE, GEAR_L, GEAR_NOSE, GEAR_R, HSTAB, MAIN_LEG, MAIN_WHEEL, MODELS_DIR, NOSE_LEG,  # noqa: E402
                       NOSE_WHEEL, PROP, VSTAB, WING_L, WING_R, Model, Part, airfoil, belly_missile, build_edge,
                       build_gear, fin_section, lerp_section, octagon, preview_arg, render_previews, ring, roundel,
                       wing_section)
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "Interceptor", "Interceptor.fbx")
BLEND_PATH = os.path.join(HERE, "Interceptor.blend")


def wing_y(x):
    return -0.10 - math.tan(math.radians(2.0)) * (abs(x) - 0.45)  # mid wing, 2 degrees anhedral


WING_SECTIONS = [  # x, le, te, thickness: a 46-degree delta with a cropped, chunky tip
    (0.45, 2.30, -2.65, 0.30),
    (1.60, 1.15, -2.58, 0.24),
    (3.50, -0.80, -2.42, 0.13),  # team zone starts here
    (4.30, -1.62, -2.34, 0.09),
    (4.62, -1.98, -2.30, 0.06),
]

HSTAB_SECTIONS = [  # x, le, te, thickness: small swept tailplane, low on the fuselage
    (-1.70, -3.50, -3.86, 0.06),
    (-1.45, -3.30, -3.92, 0.09),
    (0.00, -2.80, -3.98, 0.13),
    (1.45, -3.30, -3.92, 0.09),
    (1.70, -3.50, -3.86, 0.06),
]
HSTAB_Y = 0.10

FIN_SECTIONS = [  # y, le, te, thickness: tall, steeply swept fin
    (0.40, -1.60, -3.90, 0.16),
    (0.66, -2.05, -3.94, 0.16),
    (1.78, -3.25, -4.05, 0.11),
    (2.00, -3.50, -4.02, 0.07),
]

CANARD_SECTIONS = [  # x, le, te, thickness
    (-1.30, 2.30, 2.12, 0.05),
    (-0.50, 2.85, 2.05, 0.09),
    (0.50, 2.85, 2.05, 0.09),
    (1.30, 2.30, 2.12, 0.05),
]
CANARD_Y = 0.14

GUN_X = (1.05, 1.65)  # wing-root cannons: inner, outer


def gun_muzzle(x):
    """Unity-space tip of the wing-root cannon at span ``x`` (signed)."""
    le = lerp_section(WING_SECTIONS, abs(x))[1]
    return (x, wing_y(x), le + 0.36)


def build_body():
    p = Part("Body")
    fus = [
        octagon(3.45, 0.54, 0.54, 0.52, 0.02),
        octagon(2.80, 0.60, 0.60, 0.56, 0.02),
        octagon(1.60, 0.63, 0.63, 0.58, 0.02),
        octagon(0.30, 0.62, 0.62, 0.58, 0.02),
        octagon(-1.30, 0.57, 0.58, 0.53, 0.03),
        octagon(-2.90, 0.50, 0.52, 0.47, 0.04),
        octagon(-3.75, 0.44, 0.46, 0.42, 0.04),
    ]
    p.loft(fus, "body", cap_start="body", cap_end="char")

    # Bubble canopy well forward, then a dorsal spine running back to the fin.
    def arch(z, w, h, y0=0.60):
        return [(w, y0, z), (w * 0.82, y0 + h * 0.62, z), (w * 0.42, y0 + h, z),
                (-w * 0.42, y0 + h, z), (-w * 0.82, y0 + h * 0.62, z), (-w, y0, z)]

    canopy = [arch(3.05, 0.22, 0.06), arch(2.45, 0.36, 0.42), arch(2.35, 0.37, 0.43), arch(2.20, 0.37, 0.44),
              arch(1.75, 0.37, 0.45), arch(1.30, 0.36, 0.42), arch(1.20, 0.35, 0.40), arch(0.90, 0.30, 0.30),
              arch(0.30, 0.22, 0.20), arch(-1.50, 0.18, 0.14), arch(-2.60, 0.14, 0.05)]
    band_color = ["glass", "metal", "glass", "glass", "glass", "metal", "glass", "body", "body", "body"]

    def canopy_color(band, side):
        if side == 5:
            return "body"  # bottom, buried in the fuselage
        if band == 3 and side in (1, 3):
            return "glint"
        return band_color[band]

    p.loft(canopy, canopy_color, cap_start="glass", cap_end="body")

    # Nozzle: petals in alternating metal, a lip, and a dark recess.
    noz = [ring(-3.65, 0.43, 10, 0.04), ring(-4.05, 0.41, 10, 0.04), ring(-4.32, 0.36, 10, 0.04),
           ring(-4.32, 0.27, 10, 0.04), ring(-4.05, 0.27, 10, 0.04)]
    p.loft(noz, lambda b, s: ("metal" if s % 2 else "darkmetal") if b < 2 else "char", cap_start="darkmetal",
           cap_end="char")

    # Fixed canards behind the intake, team coloured (they stay with the body).
    p.loft([wing_section(x, le, te, t, CANARD_Y) for x, le, te, t in CANARD_SECTIONS], "team",
           cap_start="team", cap_end="team")

    # Missile on a belly pylon, lined up with the shared launch point.
    belly_missile(p, pylon_y=-0.66, my=-0.86)
    return p


def build_engine():
    """The intake: a chunky lip with a yellow ring, and a dark duct the fan spins in."""
    p = Part("Engine", ENGINE)
    sections = [ring(3.40, 0.55, 10, 0.02), ring(3.95, 0.55, 10, 0.02), ring(4.18, 0.53, 10, 0.02),
                ring(4.28, 0.47, 10, 0.02), ring(4.22, 0.40, 10, 0.02), ring(3.80, 0.40, 10, 0.02)]
    p.loft(sections, lambda b, s: "metal" if b < 1 else "sun" if b < 3 else "char", cap_start="metal",
           cap_end="char")
    return p


def build_propeller():
    """What spins in the intake: a striped shock cone (so the spin reads) and a ring of compressor blades."""
    p = Part("Propeller", PROP)
    p.loft([ring(3.84, 0.24, 8, 0.02), ring(4.18, 0.22, 8, 0.02), ring(4.48, 0.13, 8, 0.02)],
           lambda b, s: "sun" if s % 2 == 0 and b == 1 else "metal", cap_start="metal",
           fan_end=((0.0, 0.02, 4.66), lambda i: "sun" if i % 2 == 0 else "metal"), smooth=True)
    blades = 9
    for k in range(blades):
        a = 2 * math.pi * k / blades
        radial = Vector((math.cos(a), math.sin(a), 0.0))
        tangent = Vector((-math.sin(a), math.cos(a), 0.0))
        pitch = math.radians(38)
        chord = tangent * math.cos(pitch) * 0.075 + Vector((0, 0, 1)) * math.sin(pitch) * 0.075
        thick = radial.cross(chord).normalized() * 0.014
        rings = []
        for r in (0.20, 0.385):
            c = radial * r + Vector((0.0, 0.02, 3.90))
            rings.append([tuple(c + chord + thick), tuple(c - chord + thick), tuple(c - chord - thick),
                          tuple(c + chord - thick)])
        p.loft(rings, "metal", cap_start="metal", cap_end="metal", outline=False)  # too thin for an ink hull
    return p


def build_wing(side, model):
    name = "WingRight" if side > 0 else "WingLeft"
    p = Part(name, WING_R if side > 0 else WING_L)
    sections = [wing_section(side * x, le, te, t, wing_y(x)) for x, le, te, t in WING_SECTIONS]
    p.loft(sections, lambda b, s: "body" if b < 2 else "team", cap_start="body", cap_end="team")

    # Two cannons in the wing root's leading edge; Unity moves the gun muzzles onto their tips.
    for x, which in zip(GUN_X, ("Inner", "Outer")):
        tip = gun_muzzle(side * x)
        y, z = tip[1], tip[2] - 0.36
        p.loft([ring(z - 0.25, 0.075, 6, y, x0=side * x), ring(z + 0.20, 0.065, 6, y, x0=side * x),
                ring(z + 0.32, 0.065, 6, y, x0=side * x)], lambda b, s: "darkmetal" if b == 0 else "char",
               cap_start="darkmetal", cap_end="char")
        model.mark("Muzzle" + ("Right" if side > 0 else "Left") + which, tip)

    # Roundel on the flat upper facet, outboard: the face between the airfoil's 2nd and 3rd points.
    def upper(x, s):
        x_, le, te, t = lerp_section(WING_SECTIONS, x)
        (z1, o1), (z2, o2) = airfoil(le, te, t)[1:3]
        return Vector((side * x, wing_y(x) + o1 + (o2 - o1) * s, z1 + (z2 - z1) * s))

    x = 2.55
    centre = upper(x, 0.5)
    normal = (upper(x, 0.6) - upper(x, 0.4)).cross(upper(x + 0.1, 0.5) - upper(x - 0.1, 0.5))
    if normal.y < 0:
        normal = -normal
    roundel(p, centre, normal, outer=0.30, inner=0.17)
    return p


def build_hstab():
    p = Part("HorizontalStabilizer", HSTAB)
    p.loft([wing_section(x, le, te, t, HSTAB_Y) for x, le, te, t in HSTAB_SECTIONS], "team",
           cap_start="team", cap_end="team")
    return p


def build_vstab():
    p = Part("VerticalStabilizer", VSTAB)
    p.loft([fin_section(y, le, te, t) for y, le, te, t in FIN_SECTIONS], "team", cap_start="team", cap_end="team")
    return p


def wing_stub(side):
    def section(x, shrink=1.0):
        _, le, te, t = lerp_section(WING_SECTIONS, x)
        return wing_section(side * x, le, te, t * shrink, wing_y(x))

    return section(0.55), section(1.0, 0.96), side * 0.12, 0


def hstab_stub(side):
    halves = HSTAB_SECTIONS[2:]

    def section(x):
        _, le, te, t = lerp_section(halves, x)
        return wing_section(side * x, le, te, t, HSTAB_Y)

    return section(0.15), section(0.48), side * 0.09, 0


def vstab_stub():
    def section(y):
        _, le, te, t = lerp_section(FIN_SECTIONS, y)
        return fin_section(y, le, te, t)

    return section(0.45), section(0.80), 0.1, 1


def build():
    model = Model("Interceptor")
    model.add([
        build_body(),
        build_engine(),
        build_propeller(),
        build_wing(-1, model),
        build_wing(1, model),
        build_hstab(),
        build_vstab(),
        build_gear("GearMainLeft", GEAR_L, MAIN_LEG, MAIN_WHEEL, 0.24, strut=0.05, door=(-0.12, 0.85, 0.42)),
        build_gear("GearMainRight", GEAR_R, MAIN_LEG, MAIN_WHEEL, 0.24, strut=0.05, door=(0.12, 0.85, 0.42)),
        build_gear("GearNose", GEAR_NOSE, NOSE_LEG, NOSE_WHEEL, 0.20, strut=0.05),
        build_edge("EdgeWingLeft", [wing_stub(-1)]),
        build_edge("EdgeWingRight", [wing_stub(1)]),
        build_edge("EdgeHorizontalStabilizer", [hstab_stub(-1), hstab_stub(1)]),
        build_edge("EdgeVerticalStabilizer", [vstab_stub()]),
    ])
    model.export(FBX_PATH, BLEND_PATH)


SHOTS = [
    ("interceptor_threequarter", (-9.5, 4.2, 10.5), (0, -0.2, 0.0), None),
    ("interceptor_side", (-18, 0.2, 0.0), (0, 0.2, 0.0), 11.5),
    ("interceptor_top", (0, 20, 0.0), (0, 0, 0.0), 14),
    ("interceptor_rear", (7.5, 3.0, -11.0), (0, 0.0, -0.5), None),
    ("interceptor_front_low", (5.5, -1.2, 9.5), (0, -0.4, 1.0), None),
    ("interceptor_broken", (-9.5, 4.2, 10.5), (0, -0.2, 0.0), None, ("WingLeft", "HorizontalStabilizer")),
]

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        render_previews(out, SHOTS)
