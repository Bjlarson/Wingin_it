"""
Builds the Heavy Fighter hero model (Design/03-Art/lowpoly-asset-style-guide.html: "chunky twin-engine") from scratch,
then exports it for Unity.

    blender --background --factory-startup --python ArtSource/HeavyFighter/build_heavy_fighter.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/HeavyFighter/HeavyFighter.fbx   one object per modular part + markers, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png              the shared palette (same as every plane writes)
    ArtSource/HeavyFighter/HeavyFighter.blend                  the generated scene, for hand tweaks and renders

A chunky twin laid out on the shared prefab's parts, so its hitboxes still line up. The two engines sit in nacelles on
the wings: their props (WingLeftProp / WingRightProp, origin on the hub) and blur discs are extra wing objects, so a
lost wing takes its engine with it, and Unity spins them like the Fighter's propeller. The prefab's Engine part, at the
nose, is an armoured nose with a chin radiator; there is no nose propeller. The Heavy carries guns, rockets and bombs:
a rocket pod hangs under each wing at the shared rocket rails, a bomb under the belly at the shared bomb bay, and the
guns sit either side of each prop arc (Muzzle* markers).
Shared helpers, pivots and style rules: ArtSource/plane_kit.py.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import (control_surface, fin_place, loft_planform, split_planform, tail_place,
                       wing_place, ENGINE, GEAR_L, GEAR_NOSE, GEAR_R, HSTAB, MAIN_LEG, MAIN_WHEEL, MODELS_DIR, NOSE_LEG,  # noqa: E402
                       NOSE_WHEEL, VSTAB, WING_L, WING_R, Model, Part, build_edge, build_gear, fin_section,
                       lerp_section, octagon, preview_arg, render_previews, ring, roundel, wing_section)
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "HeavyFighter", "HeavyFighter.fbx")
BLEND_PATH = os.path.join(HERE, "HeavyFighter.blend")


def wing_y(x):
    return -0.15 + math.tan(math.radians(3.0)) * (abs(x) - 0.55)  # mid-low wing, 3 degrees dihedral


WING_SECTIONS = [  # x, le, te, thickness: a thick, straight, broad-chord wing with blunt tips
    (0.55, 1.45, -0.85, 0.40),
    (2.00, 1.42, -0.75, 0.36),
    (3.00, 1.38, -0.62, 0.30),
    (4.35, 1.32, -0.45, 0.24),  # team zone starts here
    (5.10, 1.05, -0.20, 0.16),
]

HSTAB_SECTIONS = [  # x, le, te, thickness: wide, thick tailplane
    (-2.30, -3.00, -3.70, 0.10),
    (-2.00, -2.72, -3.85, 0.16),
    (0.00, -2.50, -3.95, 0.20),
    (2.00, -2.72, -3.85, 0.16),
    (2.30, -3.00, -3.70, 0.10),
]
HSTAB_Y = 0.32

FIN_SECTIONS = [  # y, le, te, thickness: big, broad fin with a rounded-off top
    (0.25, -1.90, -3.85, 0.20),
    (0.60, -2.35, -3.92, 0.20),
    (1.80, -2.95, -4.02, 0.16),
    (2.06, -3.20, -3.98, 0.10),
    (2.14, -3.50, -3.88, 0.06),
]

NACELLE_X = 2.45
HUB_Z = 2.62
GUN_X = (1.00, 3.95)  # either side of the prop arc: inner, outer
POD_X, POD_Y = 3.95, -0.32  # rocket pod under the shared rocket rail

# Control surfaces (span ranges): ailerons outboard of the nacelles, split elevators, a rudder above the tail.
AILERON = (3.05, 4.85)
ELEVATORS = [(-2.05, -0.35), (0.35, 2.05)]
RUDDER = (0.66, 1.96)


def nacelle_y():
    return wing_y(NACELLE_X) + 0.04


def hub(side):
    return (side * NACELLE_X, nacelle_y(), HUB_Z)


def gun_muzzle(x):
    """Unity-space tip of the wing gun at span ``x`` (signed)."""
    le = lerp_section(WING_SECTIONS, abs(x))[1]
    return (x, wing_y(x), le + 0.42)


def build_body():
    p = Part("Body")
    fus = [
        octagon(3.45, 0.70, 0.66, 0.62, 0.05),
        octagon(2.60, 0.84, 0.80, 0.74, 0.05),
        octagon(1.20, 0.88, 0.84, 0.78, 0.05),
        octagon(-0.40, 0.86, 0.82, 0.76, 0.06),
        octagon(-1.80, 0.66, 0.66, 0.56, 0.10),
        octagon(-3.00, 0.40, 0.46, 0.30, 0.22),
        octagon(-3.85, 0.22, 0.30, 0.14, 0.28),
    ]
    p.loft(fus, "body", cap_start="body", cap_end="body")

    # Long two-seat greenhouse: windscreen, pilot, a frame, the gunner, then a turtle deck.
    def arch(z, w, h, y0=0.84):
        return [(w, y0, z), (w * 0.82, y0 + h * 0.62, z), (w * 0.42, y0 + h, z),
                (-w * 0.42, y0 + h, z), (-w * 0.82, y0 + h * 0.62, z), (-w, y0, z)]

    canopy = [arch(2.55, 0.36, 0.08), arch(1.95, 0.52, 0.50), arch(1.85, 0.53, 0.51), arch(1.10, 0.53, 0.52),
              arch(1.00, 0.53, 0.52), arch(0.30, 0.50, 0.48), arch(-0.40, 0.42, 0.34), arch(-1.40, 0.26, 0.12)]
    band_color = ["glass", "metal", "glass", "metal", "glass", "glass", "body"]

    def canopy_color(band, side):
        if side == 5:
            return "body"  # bottom, buried in the fuselage
        if band == 2 and side in (1, 3):
            return "glint"
        return band_color[band]

    p.loft(canopy, canopy_color, cap_start="glass", cap_end="body")

    # A bomb on a belly rack, lined up with the shared bomb bay.
    p.box((0.0, -0.66, -1.50), (0.18, 0.16, 0.90), "darkmetal")
    by = -0.95
    p.loft([ring(-2.12, 0.12, 6, by), ring(-1.92, 0.22, 6, by), ring(-1.10, 0.22, 6, by), ring(-0.92, 0.15, 6, by)],
           lambda b, s: "sun" if b == 1 and s % 3 == 0 else "darkmetal", cap_start="darkmetal",
           fan_end=((0.0, by, -0.84), "darkmetal"))
    for a in range(2):  # tail fins: two crossed plates
        ang = math.pi / 4 + a * math.pi / 2
        dx, dy = math.cos(ang) * 0.30, math.sin(ang) * 0.30
        nx, ny = -math.sin(ang) * 0.025, math.cos(ang) * 0.025
        p.loft([[(dx + nx, by + dy + ny, z), (-dx + nx, by - dy + ny, z), (-dx - nx, by - dy - ny, z),
                 (dx - nx, by + dy - ny, z)] for z in (-2.38, -2.05)], "metal", cap_start="metal", cap_end="metal")

    # Roundel on each flat fuselage side, behind the wing.
    for side in (-1, 1):
        z, y = -1.10, 0.12
        w = 0.86 + (0.66 - 0.86) * (-0.40 - z) / 1.4
        roundel(p, (side * w, y, z), Vector((side * 1.0, 0.0, -0.14)), outer=0.26, inner=0.14)
    return p


def build_engine():
    """The prefab's Engine part sits at the nose: an armoured, blunt nose with a yellow band and a chin radiator."""
    p = Part("Engine", ENGINE)
    sections = [ring(3.40, 0.72, 8, 0.05), ring(3.80, 0.72, 8, 0.05), ring(3.92, 0.69, 8, 0.05),
                ring(4.20, 0.52, 8, 0.05), ring(4.40, 0.28, 8, 0.05)]
    p.loft(sections, lambda b, s: "sun" if b == 1 else "metal", cap_start="metal", cap_end="metal")
    # Chin radiator scoop with a dark mouth.
    p.loft([[(x, y, 4.02) for x, y in ((0.30, -0.78), (0.30, -0.50), (-0.30, -0.50), (-0.30, -0.78))],
            [(x, y, 3.30) for x, y in ((0.32, -0.74), (0.32, -0.46), (-0.32, -0.46), (-0.32, -0.74))]],
           "metal", cap_start="char", cap_end="metal")
    return p


def build_wing(side, model):
    name = "WingRight" if side > 0 else "WingLeft"
    p = Part(name, WING_R if side > 0 else WING_L)
    place = wing_place(side, wing_y)
    stations, (aileron,) = split_planform(WING_SECTIONS, [AILERON])
    loft_planform(p, place, stations, lambda b, s: "body" if b < 3 else "team", cap_start="body", cap_end="team")
    model.add([control_surface(model, name + "Aileron", place, aileron,
                               lambda s: "body" if s < 4.35 else "team")])

    # Engine nacelle: metal cowl ring at the front, body-coloured, tapering to a point behind the wing.
    x0, yc = side * NACELLE_X, nacelle_y()
    nac = [ring(2.50, 0.44, 8, yc, x0=x0), ring(1.85, 0.50, 8, yc, x0=x0), ring(0.40, 0.46, 8, yc, x0=x0),
           ring(-1.35, 0.20, 8, yc + 0.06, x0=x0)]
    p.loft(nac, lambda b, s: "metal" if b < 1 else "char" if b == 1 and s in (0, 3, 4, 7) else "body",
           cap_start="char", cap_end="body")

    # Wing guns either side of the prop arc; Unity moves the gun muzzles onto their tips.
    for x, which in zip(GUN_X, ("Inner", "Outer")):
        tip = gun_muzzle(side * x)
        y, z = tip[1], tip[2] - 0.42
        p.loft([ring(z - 0.25, 0.09, 6, y, x0=side * x), ring(z + 0.38, 0.08, 6, y, x0=side * x)], "darkmetal",
               cap_start="darkmetal", cap_end="char")
        model.mark("Muzzle" + ("Right" if side > 0 else "Left") + which, tip)

    # Rocket pod on a short pylon, under the shared rocket rail: four yellow rocket noses in a dark face.
    px = side * POD_X
    p.box((px, (POD_Y + wing_y(POD_X)) * 0.5 + 0.02, 0.45), (0.08, wing_y(POD_X) - POD_Y, 0.70), "darkmetal")
    p.loft([ring(-0.30, 0.10, 6, POD_Y, x0=px), ring(-0.10, 0.18, 6, POD_Y, x0=px), ring(1.18, 0.18, 6, POD_Y, x0=px)],
           lambda b, s: "metal" if b == 1 and s % 3 == 1 else "darkmetal",
           cap_start="darkmetal", cap_end="char")
    for dx, dy in ((0.07, 0.07), (-0.07, 0.07), (0.07, -0.07), (-0.07, -0.07)):
        p.loft([ring(1.17, 0.045, 6, POD_Y + dy, x0=px + dx)], "sun",
               fan_end=((px + dx, POD_Y + dy, 1.27), "sun"), outline=False)
    return p


def build_wing_prop(side):
    """Three chunky blades and a blunt spinner, origin on the hub so Unity can spin it."""
    name = ("WingRight" if side > 0 else "WingLeft") + "Prop"
    h = hub(side)
    p = Part(name, h)
    hx_, hy, hz = h
    p.loft([ring(hz - 0.10, 0.30, 6, hy, x0=hx_), ring(hz + 0.14, 0.28, 6, hy, x0=hx_)], "metal",
           cap_start="metal", fan_end=((hx_, hy, hz + 0.42), "metal"), smooth=True)
    stations = [(0.24, 0.20, 0.10, 34), (0.70, 0.34, 0.08, 25), (1.12, 0.30, 0.065, 18), (1.28, 0.16, 0.05, 16)]
    for k in range(3):
        a = math.pi / 2 + 2 * math.pi * k / 3
        radial = Vector((math.cos(a), math.sin(a), 0.0))
        tangent = Vector((-math.sin(a), math.cos(a), 0.0))
        rings = []
        for s, chord, thick, pitch in stations:
            t = math.radians(pitch)
            pts = []
            for cx, cz in ((0.5, 0), (0, 0.5), (-0.5, 0), (0, -0.5)):  # chunky diamond section
                x, z = cx * chord, cz * thick
                xr, zr = x * math.cos(t) - z * math.sin(t), x * math.sin(t) + z * math.cos(t)
                v = Vector(h) + radial * s + tangent * xr + Vector((0, 0, zr))
                pts.append(tuple(v))
            rings.append(pts)
        p.loft(rings, lambda b, s: "darkmetal" if b < 2 else "sun", cap_start="darkmetal", cap_end="sun")
    return p


def build_wing_prop_disc(side):
    name = ("WingRight" if side > 0 else "WingLeft") + "PropDisc"
    h = hub(side)
    p = Part(name, h)
    inner = ring(h[2] - 0.04, 0.42, 20, h[1], x0=h[0])
    outer = ring(h[2] - 0.04, 1.32, 20, h[1], x0=h[0])
    vi = [p.vert(v) for v in inner]
    vo = [p.vert(v) for v in outer]
    for i in range(20):
        j = (i + 1) % 20
        p.face([vi[i], vi[j], vo[j], vo[i]], "ink")
    return p


def build_hstab(model):
    p = Part("HorizontalStabilizer", HSTAB)
    place = tail_place(HSTAB_Y)
    stations, (left, right) = split_planform(HSTAB_SECTIONS, ELEVATORS)
    loft_planform(p, place, stations, "team", cap_start="team", cap_end="team")
    model.add([control_surface(model, "HorizontalStabilizerElevatorLeft", place, left, "team"),
               control_surface(model, "HorizontalStabilizerElevatorRight", place, right, "team")])
    return p


def build_vstab(model):
    p = Part("VerticalStabilizer", VSTAB)
    stations, (rudder,) = split_planform(FIN_SECTIONS, [RUDDER])
    loft_planform(p, fin_place, stations, "team", cap_start="team", cap_end="team")
    model.add([control_surface(model, "VerticalStabilizerRudder", fin_place, rudder, "team")])
    return p


def wing_stub(side):
    def section(x, shrink=1.0):
        _, le, te, t = lerp_section(WING_SECTIONS, x)
        return wing_section(side * x, le, te, t * shrink, wing_y(x))

    return section(0.70), section(1.20, 0.96), side * 0.14, 0


def hstab_stub(side):
    halves = HSTAB_SECTIONS[2:]

    def section(x):
        _, le, te, t = lerp_section(halves, x)
        return wing_section(side * x, le, te, t, HSTAB_Y)

    return section(0.15), section(0.50), side * 0.10, 0


def vstab_stub():
    def section(y):
        _, le, te, t = lerp_section(FIN_SECTIONS, y)
        return fin_section(y, le, te, t)

    return section(0.40), section(0.80), 0.11, 1


def build():
    model = Model("HeavyFighter")
    model.add([
        build_body(),
        build_engine(),
        build_wing(-1, model),
        build_wing(1, model),
        build_wing_prop(-1),
        build_wing_prop(1),
        build_hstab(model),
        build_vstab(model),
        build_gear("GearMainLeft", GEAR_L, MAIN_LEG, MAIN_WHEEL, 0.34, strut=0.08, door=(-0.16, 0.85, 0.50), sides=8),
        build_gear("GearMainRight", GEAR_R, MAIN_LEG, MAIN_WHEEL, 0.34, strut=0.08, door=(0.16, 0.85, 0.50), sides=8),
        build_gear("GearNose", GEAR_NOSE, NOSE_LEG, NOSE_WHEEL, 0.26, strut=0.07, sides=8),
        build_edge("EdgeWingLeft", [wing_stub(-1)]),
        build_edge("EdgeWingRight", [wing_stub(1)]),
        build_edge("EdgeHorizontalStabilizer", [hstab_stub(-1), hstab_stub(1)]),
        build_edge("EdgeVerticalStabilizer", [vstab_stub()]),
    ])
    model.add_blur(build_wing_prop_disc(-1))
    model.add_blur(build_wing_prop_disc(1))
    model.export(FBX_PATH, BLEND_PATH)


SHOTS = [
    ("heavy_threequarter", (-11.0, 4.8, 12.0), (0, -0.2, 0.0), None),
    ("heavy_side", (-18, 0.2, 0.0), (0, 0.2, 0.0), 11.5),
    ("heavy_top", (0, 20, 0.0), (0, 0, 0.0), 14),
    ("heavy_rear", (8.5, 3.4, -12.5), (0, 0.0, -0.5), None),
    ("heavy_front_low", (6.5, -1.4, 11.0), (0, -0.4, 1.0), None),
    ("heavy_broken", (-11.0, 4.8, 12.0), (0, -0.2, 0.0), None, ("WingLeft", "HorizontalStabilizer")),
]

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        render_previews(out, SHOTS)
