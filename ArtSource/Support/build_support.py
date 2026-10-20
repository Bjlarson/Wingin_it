"""
Builds the Support hero model (Design/03-Art/lowpoly-asset-style-guide.html: "long high wing, radar dome") from
scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/Support/build_support.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Support/Support.fbx   one object per modular part + markers, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png    the shared palette (same as every plane writes)
    ArtSource/Support/Support.blend                  the generated scene, for hand tweaks and renders

A cabin utility plane: a long, slender high wing braced by a strut on each side, a glazed cabin under the wing, a
single three-blade nose prop, and a team-coloured radar rotodome on a pylon over the rear fuselage. The dome is an
extra body object (BodyRadar, origin on its centre) that Unity turns slowly about its vertical axis.

The wing sits on the cabin roof, well above the shared prefab's wing hitboxes and rocket rails, so the model carries
markers that move them for this class: WingHitboxLeft / WingHitboxRight (the wing hitboxes' centres) and
RailLeft / RailRight (rocket launch points at the noses of the under-wing pods), alongside the gun Muzzle* markers.
The Support carries guns and rockets: two guns in each wing's leading edge, a rocket pod under each wing.
Shared helpers, pivots and style rules: ArtSource/plane_kit.py.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import (ENGINE, GEAR_L, GEAR_NOSE, GEAR_R, HSTAB, MAIN_LEG, MAIN_WHEEL, MODELS_DIR, NOSE_LEG,  # noqa: E402
                       NOSE_WHEEL, PROP, VSTAB, WING_L, WING_R, Model, Part, build_edge, build_gear, fin_section,
                       lerp_section, octagon, preview_arg, render_previews, ring, roundel, to_blender,
                       wing_section)
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "Support", "Support.fbx")
BLEND_PATH = os.path.join(HERE, "Support.blend")


def wing_y(x):
    return 0.88 + math.tan(math.radians(1.5)) * abs(x)  # high wing on the cabin roof, a touch of dihedral


WING_SECTIONS = [  # x, le, te, thickness: long, slender and nearly straight, the two halves meeting over the cabin
    (0.06, 1.62, 0.02, 0.30),
    (2.60, 1.58, 0.08, 0.27),
    (4.75, 1.48, 0.28, 0.20),  # team zone starts here
    (5.60, 1.36, 0.42, 0.15),
    (5.90, 1.16, 0.62, 0.09),
]

HSTAB_SECTIONS = [  # x, le, te, thickness
    (-2.15, -3.25, -3.80, 0.07),
    (-1.85, -3.00, -3.90, 0.12),
    (0.00, -2.70, -4.00, 0.16),
    (1.85, -3.00, -3.90, 0.12),
    (2.15, -3.25, -3.80, 0.07),
]
HSTAB_Y = 0.30

FIN_SECTIONS = [  # y, le, te, thickness: tall fin with a long dorsal fillet
    (0.22, -1.80, -3.85, 0.16),
    (0.50, -2.55, -3.90, 0.16),
    (1.90, -3.30, -4.05, 0.12),
    (2.18, -3.58, -4.00, 0.07),
]

FUSELAGE = [  # z, half width, top, bottom, centre y: a cabin that rises under the wing
    (3.45, 0.62, 0.58, 0.56, 0.00),
    (2.90, 0.68, 0.62, 0.62, 0.00),
    (2.20, 0.72, 0.66, 0.66, 0.02),
    (1.40, 0.74, 0.86, 0.68, 0.04),
    (0.00, 0.72, 0.86, 0.66, 0.04),
    (-1.00, 0.58, 0.72, 0.52, 0.10),
    (-2.40, 0.36, 0.50, 0.30, 0.22),
    (-3.85, 0.18, 0.30, 0.12, 0.30),
]

GUN_X = (1.90, 2.90)  # inner, outer, in the leading edge (the placeholder's are at 2.1 and 3.2)
POD_X = 3.95  # rocket pods at the shared rail span
DOME = (0.0, 1.66, -1.10)  # rotodome centre
DOME_R = 1.15


def fuselage_at(z):
    """(half width, top, bottom, centre y) of the fuselage at ``z``."""
    for a, b in zip(FUSELAGE, FUSELAGE[1:]):
        if b[0] <= z <= a[0]:
            k = (a[0] - z) / (a[0] - b[0])
            return tuple(a[i] + (b[i] - a[i]) * k for i in range(1, 5))
    raise ValueError(f"{z} is off the fuselage")


def chamfer_point(z, t):
    """A point on the upper side chamfer of the fuselage at ``z`` (t = 0 at the side wall, 1 at the roof), x > 0."""
    w, top, _, yc = fuselage_at(z)
    a, b = Vector((w, yc + top * 0.42, z)), Vector((w * 0.55, yc + top, z))
    return a.lerp(b, t)


def gun_muzzle(x):
    """Unity-space tip of the wing gun at span ``x`` (signed)."""
    le = lerp_section(WING_SECTIONS, abs(x))[1]
    return (x, wing_y(x) - 0.05, le + 0.36)


def pod_y():
    _, _, _, t = lerp_section(WING_SECTIONS, POD_X)
    return wing_y(POD_X) - 0.42 * t - 0.30


def rail(side):
    """Where the rockets leave: just ahead of the pod's face."""
    return (side * POD_X, pod_y(), 1.32)


def hring(y, r, sides, cx=0.0, cz=0.0):
    """A horizontal ring (Unity y up)."""
    return [(cx + r * math.cos(math.pi / sides + 2 * math.pi * k / sides), y,
             cz + r * math.sin(math.pi / sides + 2 * math.pi * k / sides)) for k in range(sides)]


def panel(p, corners, normal, color, lift=0.015):
    """A one-sided flat quad floated just off a surface, facing ``normal``."""
    n = Vector(normal).normalized()
    verts = [p.vert(Vector(c) + n * lift) for c in corners]
    f = p.face(verts, color)
    f.normal_update()
    if f.normal.dot(to_blender(n)) < 0:
        f.normal_flip()
    p.loose_faces.append(f)


def build_body():
    p = Part("Body")
    p.loft([octagon(*s) for s in FUSELAGE], "body", cap_start="body", cap_end="body")

    # Glazed windscreen rising from the cowl into the wing's leading edge.
    def arch(z, w, h, y0=0.50):
        return [(w, y0, z), (w * 0.82, y0 + h * 0.62, z), (w * 0.42, y0 + h, z),
                (-w * 0.42, y0 + h, z), (-w * 0.82, y0 + h * 0.62, z), (-w, y0, z)]

    canopy = [arch(2.80, 0.40, 0.10), arch(2.25, 0.55, 0.32), arch(2.15, 0.56, 0.34), arch(1.35, 0.58, 0.44)]
    band_color = ["glass", "metal", "glass"]

    def canopy_color(band, side):
        if side == 5:
            return "body"
        if band == 2 and side in (1, 3):
            return "glint"
        return band_color[band]

    p.loft(canopy, canopy_color, cap_start="glass", cap_end="body")

    # Cabin side windows on the upper chamfers, under the wing: two panes a side.
    for side in (-1, 1):
        for z0, z1 in ((1.30, 0.70), (0.58, 0.00)):
            quad = [chamfer_point(z0, 0.18), chamfer_point(z1, 0.18), chamfer_point(z1, 0.78), chamfer_point(z0, 0.78)]
            quad = [(side * v.x, v.y, v.z) for v in quad]
            w, top, _, yc = fuselage_at((z0 + z1) * 0.5)
            normal = Vector((side * top * 0.58, w * 0.45, 0.0))
            panel(p, quad, normal, "glass")
            mid = [quad[0], quad[1]]
            glint = [Vector(mid[0]).lerp(Vector(quad[3]), 0.55), Vector(mid[1]).lerp(Vector(quad[2]), 0.55),
                     Vector(mid[1]).lerp(Vector(quad[2]), 0.70), Vector(mid[0]).lerp(Vector(quad[3]), 0.70)]
            panel(p, glint, normal, "glint", lift=0.022)

    # Gear sponsons: stubby fairings out to the main legs.
    for side in (-1, 1):
        p.loft([[(side * x, y, z) for x, y in ((0.50, -0.05), (0.50, -0.62), (1.24, -0.62), (1.24, -0.14))]
                for z in (0.45, -0.45)], "body", cap_start="body", cap_end="body")

    # Radar pylon over the rear fuselage, carrying the rotodome (BodyRadar).
    p.loft([fin_section(y, le, te, t) for y, le, te, t in ((0.60, -0.45, -1.80, 0.18), (1.56, -0.80, -1.42, 0.12))],
           "body", cap_start="body", cap_end="darkmetal")

    # Exhaust stubs under the cowl, a belly antenna.
    for side in (-1, 1):
        p.box((side * 0.55, -0.42, 3.05), (0.14, 0.12, 0.30), "char", rot_x=-0.35)
    p.loft([[(x, -0.62, z) for x, z in ((0.03, -1.70), (0.03, -1.90), (-0.03, -1.90), (-0.03, -1.70))],
            [(x, -0.98, z) for x, z in ((0.02, -1.96), (0.02, -2.04), (-0.02, -2.04), (-0.02, -1.96))]],
           "darkmetal", cap_start="darkmetal", cap_end="darkmetal")

    # Roundel on each flat fuselage side, behind the wing.
    for side in (-1, 1):
        z = -1.55
        w, _, _, yc = fuselage_at(z)
        roundel(p, (side * w, yc, z), Vector((side * 1.0, 0.0, -0.16)), outer=0.24, inner=0.13)
    return p


def build_radar():
    """The rotodome: a team-coloured lens with a white rim and two white bars on top, so its turning shows."""
    cx, cy, cz = DOME
    p = Part("BodyRadar", DOME)
    sides = 16
    rings = [hring(cy - 0.15, 0.34, sides, cx, cz), hring(cy - 0.06, DOME_R, sides, cx, cz),
             hring(cy + 0.06, DOME_R, sides, cx, cz), hring(cy + 0.15, 0.38, sides, cx, cz)]
    bars = {3, 4, 11, 12}  # two opposite sectors
    p.loft(rings, lambda b, s: "white" if b == 1 or (b == 2 and s in bars) else "team",
           cap_start="darkmetal", fan_end=((cx, cy + 0.17, cz), lambda s: "white" if s in bars else "team"))
    return p


def build_engine():
    p = Part("Engine", ENGINE)
    sections = [ring(3.40, 0.64, 10), ring(3.72, 0.68, 10), ring(3.98, 0.64, 10), ring(4.10, 0.52, 10),
                ring(4.06, 0.36, 10)]
    p.loft(sections, lambda b, s: "white" if b < 1 else "sun" if b == 1 else "char" if b == 3 else "metal",
           cap_start="white", cap_end="char")
    # Chin air intake.
    p.loft([[(x, y, 4.00) for x, y in ((0.24, -0.70), (0.24, -0.46), (-0.24, -0.46), (-0.24, -0.70))],
            [(x, y, 3.40) for x, y in ((0.26, -0.64), (0.26, -0.40), (-0.26, -0.40), (-0.26, -0.64))]],
           "metal", cap_start="char", cap_end="metal")
    return p


def build_propeller():
    """Three chunky blades and a pointed spinner, origin on the hub so Unity can spin it."""
    p = Part("Propeller", PROP)
    hx_, hy, hz = PROP
    p.loft([ring(hz - 0.08, 0.34, 8), ring(hz + 0.16, 0.33, 8), ring(hz + 0.38, 0.20, 8)], "white",
           cap_start="white", fan_end=((0.0, 0.0, hz + 0.56), "white"), smooth=True)
    stations = [(0.26, 0.20, 0.10, 34), (0.75, 0.34, 0.08, 25), (1.35, 0.30, 0.065, 18), (1.52, 0.15, 0.05, 16)]
    for k in range(3):
        a = math.pi / 2 + 2 * math.pi * k / 3
        radial = Vector((math.cos(a), math.sin(a), 0.0))
        tangent = Vector((-math.sin(a), math.cos(a), 0.0))
        rings = []
        for s, chord, thick, pitch in stations:
            t = math.radians(pitch)
            pts = []
            for cx, cz in ((0.5, 0), (0, 0.5), (-0.5, 0), (0, -0.5)):
                x, z = cx * chord, cz * thick
                xr, zr = x * math.cos(t) - z * math.sin(t), x * math.sin(t) + z * math.cos(t)
                pts.append(tuple(Vector(PROP) + radial * s + tangent * xr + Vector((0, 0, zr))))
            rings.append(pts)
        p.loft(rings, lambda b, s: "darkmetal" if b < 2 else "sun", cap_start="darkmetal", cap_end="sun")
    return p


def build_prop_disc():
    p = Part("PropDisc", PROP)
    inner = ring(PROP[2] - 0.04, 0.46, 20)
    outer = ring(PROP[2] - 0.04, 1.56, 20)
    vi = [p.vert(v) for v in inner]
    vo = [p.vert(v) for v in outer]
    for i in range(20):
        j = (i + 1) % 20
        p.face([vi[i], vi[j], vo[j], vo[i]], "ink")
    return p


def build_wing(side, model):
    name = "WingRight" if side > 0 else "WingLeft"
    p = Part(name, WING_R if side > 0 else WING_L)
    sections = [wing_section(side * x, le, te, t, wing_y(x)) for x, le, te, t in WING_SECTIONS]
    p.loft(sections, lambda b, s: "body" if b < 2 else "team", cap_start="body", cap_end="team")

    # Bracing strut from the cabin's lower side up to the wing, faired like a thin airfoil.
    s0, s1 = (0.62, -0.28, 0.80), (2.75, wing_y(2.75) - 0.06, 0.80)
    pts0 = [(side * s0[0] + ox, s0[1] + oy, s0[2] + oz) for ox, oy, oz in
            ((0, 0, 0.14), (0.035, -0.03, 0), (0, 0, -0.12), (-0.035, 0.03, 0))]
    pts1 = [(side * s1[0] + ox, s1[1] + oy, s1[2] + oz) for ox, oy, oz in
            ((0, 0, 0.14), (0.035, -0.03, 0), (0, 0, -0.12), (-0.035, 0.03, 0))]
    p.loft([pts0, pts1], "metal", cap_start="metal", cap_end="metal")

    # Guns in the leading edge; Unity moves the gun muzzles onto their tips.
    for x, which in zip(GUN_X, ("Inner", "Outer")):
        tip = gun_muzzle(side * x)
        y, z = tip[1], tip[2] - 0.36
        p.loft([ring(z - 0.30, 0.08, 6, y, x0=side * x), ring(z + 0.24, 0.07, 6, y, x0=side * x),
                ring(z + 0.36, 0.07, 6, y, x0=side * x)], lambda b, s: "darkmetal" if b == 0 else "char",
               cap_start="darkmetal", cap_end="char")
        model.mark("Muzzle" + ("Right" if side > 0 else "Left") + which, tip)

    # Rocket pod on a short pylon under the wing, four yellow rocket noses in its face.
    px, py = side * POD_X, pod_y()
    wy = wing_y(POD_X)
    p.box((px, (py + wy) * 0.5, 0.60), (0.08, wy - py, 0.70), "darkmetal")
    p.loft([ring(-0.12, 0.10, 6, py, x0=px), ring(0.06, 0.18, 6, py, x0=px), ring(1.15, 0.18, 6, py, x0=px)],
           lambda b, s: "metal" if b == 1 and s % 3 == 1 else "darkmetal", cap_start="darkmetal", cap_end="char")
    for dx, dy in ((0.07, 0.07), (-0.07, 0.07), (0.07, -0.07), (-0.07, -0.07)):
        p.loft([ring(1.14, 0.045, 6, py + dy, x0=px + dx)], "sun",
               fan_end=((px + dx, py + dy, 1.24), "sun"), outline=False)
    model.mark("Rail" + ("Right" if side > 0 else "Left"), rail(side))
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

    return section(0.30), section(0.80, 0.96), side * 0.14, 0


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

    return section(0.30), section(0.70), 0.1, 1


def build():
    model = Model("Support")
    model.add([
        build_body(),
        build_radar(),
        build_engine(),
        build_propeller(),
        build_wing(-1, model),
        build_wing(1, model),
        build_hstab(),
        build_vstab(),
        build_gear("GearMainLeft", GEAR_L, MAIN_LEG, MAIN_WHEEL, 0.30, strut=0.07, sides=8),
        build_gear("GearMainRight", GEAR_R, MAIN_LEG, MAIN_WHEEL, 0.30, strut=0.07, sides=8),
        build_gear("GearNose", GEAR_NOSE, NOSE_LEG, NOSE_WHEEL, 0.24, sides=8),
        build_edge("EdgeWingLeft", [wing_stub(-1)]),
        build_edge("EdgeWingRight", [wing_stub(1)]),
        build_edge("EdgeHorizontalStabilizer", [hstab_stub(-1), hstab_stub(1)]),
        build_edge("EdgeVerticalStabilizer", [vstab_stub()]),
    ])
    model.add_blur(build_prop_disc())
    # The wing hitboxes, moved up onto the high wing and back to its chord.
    _, le, te, _ = lerp_section(WING_SECTIONS, WING_R[0])
    for side in (-1, 1):
        model.mark("WingHitbox" + ("Right" if side > 0 else "Left"),
                   (side * WING_R[0], wing_y(WING_R[0]) + 0.02, (le + te) * 0.5))
    model.export(FBX_PATH, BLEND_PATH)


SHOTS = [
    ("support_threequarter", (-11.0, 4.8, 12.0), (0, 0.2, 0.0), None),
    ("support_side", (-18, 0.4, 0.0), (0, 0.4, 0.0), 11.5),
    ("support_top", (0, 20, 0.0), (0, 0, 0.0), 14),
    ("support_rear", (8.5, 3.4, -12.5), (0, 0.4, -0.5), None),
    ("support_front_low", (6.5, -1.4, 11.0), (0, 0.0, 1.0), None),
    ("support_broken", (-11.0, 4.8, 12.0), (0, 0.2, 0.0), None, ("WingLeft", "HorizontalStabilizer")),
]

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        render_previews(out, SHOTS)
