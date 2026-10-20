"""
Builds the Fighter hero model (Design/03-Art/lowpoly-asset-style-guide.html) from scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/Fighter/build_fighter.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Fighter/Fighter.fbx   one object per modular part, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png    256x256 palette atlas shared by every plane
    ArtSource/Fighter/Fighter.blend                  the generated scene, for hand tweaks and renders

Shared helpers, pivots and style rules live in ArtSource/plane_kit.py. The Fighter: low wing, tricycle gear,
oversized two-blade prop with a blur disc, team colour on wingtips + tail.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import (ENGINE, GEAR_L, GEAR_NOSE, GEAR_R, HSTAB, MAIN_LEG, MAIN_WHEEL, MODELS_DIR, NOSE_LEG,  # noqa: E402
                       NOSE_WHEEL, PROP, VSTAB, WING_L, WING_R, Model, Part, belly_missile, build_edge, build_gear,
                       fin_section, octagon, preview_arg, render_previews, ring, roundel, wing_section)
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "Fighter", "Fighter.fbx")
BLEND_PATH = os.path.join(HERE, "Fighter.blend")


def wing_y(x):
    return -0.18 + math.tan(math.radians(5.0)) * (abs(x) - 0.5)  # low wing, 5 degrees dihedral


WING_SECTIONS = [  # x, le, te, thickness
    (0.50, 1.50, -0.70, 0.34),
    (2.00, 1.46, -0.42, 0.28),
    (4.20, 1.40, 0.05, 0.20),  # team zone starts here
    (4.75, 1.30, 0.12, 0.17),
    (5.02, 1.10, 0.32, 0.10),
]

HSTAB_SECTIONS = [  # x, le, te, thickness
    (-1.95, -3.28, -3.78, 0.07),
    (-1.65, -3.05, -3.88, 0.12),
    (0.00, -2.70, -3.98, 0.16),
    (1.65, -3.05, -3.88, 0.12),
    (1.95, -3.28, -3.78, 0.07),
]

FIN_SECTIONS = [  # y, le, te, thickness
    (0.22, -2.20, -3.80, 0.16),
    (0.48, -2.62, -3.84, 0.16),
    (1.70, -3.32, -3.98, 0.12),
    (1.93, -3.55, -3.94, 0.07),
]


def build_body():
    p = Part("Body")
    fus = [
        octagon(3.50, 0.72, 0.70, 0.66, 0.02),
        octagon(3.00, 0.79, 0.76, 0.72, 0.02),
        octagon(2.00, 0.81, 0.79, 0.76, 0.02),
        octagon(0.60, 0.80, 0.78, 0.75, 0.02),
        octagon(-0.60, 0.70, 0.71, 0.64, 0.06),
        octagon(-1.80, 0.50, 0.58, 0.44, 0.14),
        octagon(-3.00, 0.28, 0.43, 0.20, 0.26),
        octagon(-3.75, 0.15, 0.27, 0.09, 0.30),
    ]
    p.loft(fus, "body", cap_start="body", cap_end="body")

    # Faceted canopy: windscreen, frame bows, a band of hard white glint, then a body-coloured turtle deck.
    def arch(z, w, h, y0=0.72):
        return [(w, y0, z), (w * 0.82, y0 + h * 0.62, z), (w * 0.42, y0 + h, z),
                (-w * 0.42, y0 + h, z), (-w * 0.82, y0 + h * 0.62, z), (-w, y0, z)]

    canopy = [arch(2.15, 0.32, 0.08), arch(1.55, 0.47, 0.52), arch(1.45, 0.48, 0.53), arch(1.30, 0.48, 0.535),
              arch(0.95, 0.48, 0.54), arch(0.55, 0.48, 0.545), arch(0.45, 0.47, 0.54), arch(-0.35, 0.32, 0.30),
              arch(-0.80, 0.17, 0.06)]
    band_color = ["glass", "metal", "glass", "glass", "glass", "metal", "glass", "body"]

    def canopy_color(band, side):
        if side == 5:
            return "body"  # bottom, buried in the fuselage
        if band == 3 and side in (1, 3):
            return "glint"
        return band_color[band]

    p.loft(canopy, canopy_color, cap_start="glass", cap_end="body")

    # Exhaust stubs along the nose.
    for side in (-1, 1):
        for z in (2.95, 2.65, 2.35):
            p.box((side * 0.81, 0.08, z), (0.16, 0.12, 0.2), "char", rot_x=-0.3)

    # Radio mast behind the canopy.
    p.loft([
        [(x, 0.74, z) for x, z in ((0.04, -1.20), (0.04, -1.42), (-0.04, -1.42), (-0.04, -1.20))],
        [(x, 1.24, z) for x, z in ((0.025, -1.48), (0.025, -1.58), (-0.025, -1.58), (-0.025, -1.48))],
    ], "darkmetal", cap_start="darkmetal", cap_end="darkmetal")

    # Missile on a belly pylon (the Fighter carries missiles), lined up with the placeholder launch point.
    belly_missile(p, pylon_y=-0.80, my=-0.98)

    # Roundel on the flat decal panel behind the wing: white ring, team-coloured centre.
    for side in (-1, 1):
        z, y = -1.25, 0.12
        w = 0.70 + (0.50 - 0.70) * (-0.6 - z) / 1.2
        roundel(p, (side * w, y, z), Vector((side * 1.0, 0.0, -0.17)))
    return p


def build_engine():
    p = Part("Engine", ENGINE)
    sections = [ring(3.40, 0.78, 10, 0.02), ring(3.70, 0.86, 10, 0.02), ring(3.98, 0.82, 10, 0.02),
                ring(4.10, 0.68, 10, 0.02), ring(4.05, 0.50, 10, 0.02), ring(3.96, 0.34, 10, 0.02)]
    p.loft(sections, lambda b, s: "metal" if b < 3 else "char", cap_start="metal", cap_end="char")
    return p


def build_propeller():
    p = Part("Propeller", PROP)
    # Big blunt spinner (smooth shaded, per the guide).
    p.loft([ring(4.06, 0.40, 8), ring(4.32, 0.39, 8), ring(4.55, 0.27, 8), ring(4.70, 0.11, 8)], "metal",
           cap_start="metal", cap_end="metal", smooth=True)
    # Two chunky, twisted blades, ~1.4x real scale, with yellow tips.
    stations = [(0.30, 0.20, 0.11, 34), (0.80, 0.38, 0.09, 26), (1.42, 0.36, 0.07, 19), (1.62, 0.30, 0.06, 17),
                (1.74, 0.14, 0.05, 16)]
    for sign in (1, -1):
        rings = []
        for s, chord, thick, pitch in stations:
            a = math.radians(pitch)
            pts = []
            for cx, cz in ((0.5, 0), (0.22, 0.5), (-0.22, 0.5), (-0.5, 0), (-0.22, -0.5), (0.22, -0.5)):
                x, z = cx * chord, cz * thick
                # twist the section about the blade axis, then lay the blade along +/-y
                xr, zr = x * math.cos(a) - z * math.sin(a), x * math.sin(a) + z * math.cos(a)
                pts.append((sign * xr, sign * s, 4.15 + zr))
            rings.append(pts)
        p.loft(rings, lambda b, s: "darkmetal" if b < 2 else "sun", cap_start="darkmetal", cap_end="sun")
    return p


def build_prop_disc():
    """Translucent blur disc behind the blades (its own material)."""
    p = Part("PropDisc", PROP)
    inner = ring(4.10, 0.72, 20)
    outer = ring(4.10, 1.78, 20)
    vi = [p.vert(v) for v in inner]
    vo = [p.vert(v) for v in outer]
    for i in range(20):
        j = (i + 1) % 20
        p.face([vi[i], vi[j], vo[j], vo[i]], "ink")
    return p


def build_wing(side):
    name = "WingRight" if side > 0 else "WingLeft"
    p = Part(name, WING_R if side > 0 else WING_L)
    sections = [wing_section(side * x, le, te, t, wing_y(x)) for x, le, te, t in WING_SECTIONS]
    p.loft(sections, lambda b, s: "body" if b < 2 else "team", cap_start="body", cap_end="team")
    # Two guns per wing poking out of the leading edge, lined up with the placeholder muzzles.
    for x in (2.10, 3.20):
        y = wing_y(x) + 0.0
        le = 1.50 - 0.04 * (x - 0.5)
        p.loft([ring(le - 0.25, 0.08, 6, y, x0=side * x), ring(le + 0.22, 0.07, 6, y, x0=side * x),
                ring(le + 0.34, 0.07, 6, y, x0=side * x)], lambda b, s: "darkmetal" if b == 0 else "char",
               cap_start="darkmetal", cap_end="char")
    return p


def build_hstab():
    p = Part("HorizontalStabilizer", HSTAB)
    sections = [wing_section(x, le, te, t, 0.25) for x, le, te, t in HSTAB_SECTIONS]
    p.loft(sections, "team", cap_start="team", cap_end="team")
    return p


def build_vstab():
    p = Part("VerticalStabilizer", VSTAB)
    sections = [fin_section(y, le, te, t) for y, le, te, t in FIN_SECTIONS]
    p.loft(sections, "team", cap_start="team", cap_end="team")
    return p


def wing_stub(side):
    x0, x1 = 0.6, 1.08
    le0, te0, t0 = 1.50, -0.70, 0.34
    base = wing_section(side * x0, le0, te0, t0, wing_y(x0))
    outer = wing_section(side * x1, le0 - 0.01, te0 + 0.1, t0 * 0.96, wing_y(x1))
    return base, outer, side * 0.12, 0


def hstab_stub(side):
    base = wing_section(side * 0.15, -2.72, -3.97, 0.16, 0.25)
    outer = wing_section(side * 0.48, -2.80, -3.95, 0.15, 0.25)
    return base, outer, side * 0.09, 0


def vstab_stub():
    base = fin_section(0.25, -2.25, -3.80, 0.16)
    outer = fin_section(0.62, -2.68, -3.84, 0.16)
    return base, outer, 0.1, 1


def build():
    model = Model("Fighter")
    model.add([
        build_body(),
        build_engine(),
        build_propeller(),
        build_wing(-1),
        build_wing(1),
        build_hstab(),
        build_vstab(),
        build_gear("GearMainLeft", GEAR_L, MAIN_LEG, MAIN_WHEEL, 0.28),
        build_gear("GearMainRight", GEAR_R, MAIN_LEG, MAIN_WHEEL, 0.28),
        build_gear("GearNose", GEAR_NOSE, NOSE_LEG, NOSE_WHEEL, 0.22),
        build_edge("EdgeWingLeft", [wing_stub(-1)]),
        build_edge("EdgeWingRight", [wing_stub(1)]),
        build_edge("EdgeHorizontalStabilizer", [hstab_stub(-1), hstab_stub(1)]),
        build_edge("EdgeVerticalStabilizer", [vstab_stub()]),
    ])
    model.add_blur(build_prop_disc())
    model.export(FBX_PATH, BLEND_PATH)


SHOTS = [
    ("fighter_threequarter", (-9.5, 4.2, 10.5), (0, -0.2, 0.2), None),
    ("fighter_side", (-18, 0.2, 0.3), (0, 0.0, 0.3), 11.5),
    ("fighter_top", (0, 20, 0.2), (0, 0, 0.2), 14),
    ("fighter_rear", (7.5, 3.0, -11.0), (0, 0.0, -0.5), None),
    ("fighter_front_low", (5.5, -1.2, 9.5), (0, -0.4, 1.0), None),
    ("fighter_broken", (-9.5, 4.2, 10.5), (0, -0.2, 0.0), None, ("WingLeft", "HorizontalStabilizer")),
]

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        render_previews(out, SHOTS)
