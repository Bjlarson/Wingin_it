"""
Builds the pilot and the parachute (Design/feedback/playtest-2026-10-27.md, G1: the figure that stands by the plane on the
menu stage, ejects and hangs under the canopy), and the pilot's moves, then exports them for Unity.

    blender --background --factory-startup --python ArtSource/Pilot/build_pilot.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/Pilot/Pilot.fbx          one object per piece, Unity axes
    Assets/_Project/Art/Models/Pilot/PilotMoves.json    the moves (keyframed joint poses), made into clips by Unity
    Assets/_Project/Art/Textures/PlanePalette.png       the shared palette atlas (rewritten, same as the planes')
    ArtSource/Pilot/Pilot.blend                         the generated scene: the pilot on its rig with every move on
                                                        the timeline (a marker per move), for scrubbing and renders

The figure (Unity space, metres; standing on the origin facing +z, about 1.85 m to the helmet's top) is a jet-age pilot:
flight suit, survival vest and harness, a hard helmet with a gold visor and an oxygen mask, an oxygen pack on the back,
gloves and boots. It's a rig of rigid pieces on JOINTS (hips, chest, head, upper arm / forearm / hand, thigh / shin /
foot), so a move is a few joint rotations and nothing deforms.

Objects, each with its origin on its joint:
    Body_<Joint>                  what's under the gear: the head (face, hair) and neck
    <Slot>_<Variant>_<Joint>      one piece of a gear variant (GEAR): Suit (Hips, Chest, arms, legs), Helmet (Head),
                                  Backpack (Chest), Gloves (HandL/R), Boots (FootL/R). Unity shows one variant per slot,
                                  so gear swaps without touching the rig
    Parachute                     the canopy's lines and the two risers, origin where the risers meet the harness: the
                                  pilot's PilotRiserAttach marker
    Canopy<Pattern>               one scalloped 12-gore canopy per pattern (Stripes, Quarters, Crown, Solid), same origin

Markers: Joint<Name> (every joint, Unity rebuilds the rig from them), PilotRiserAttach (the harness between the shoulders
the risers hook to), PilotSeat (the seat pan under a seated pilot's thighs, in the figure's space).

The suit's cloth and the helmet's shell are on the team material, the canopy's trim gores on Plane_Trim, so each tints on
its own (G4's suit, helmet, canopy and trim colours); everything else (vest, webbing, visor, gloves, boots) is on the
shared palette.

Moves (MOVES; Unity's World.PilotMove has the same names in the same order): a list of keys, each a full pose (every
joint's local Euler angles in Unity's convention, degrees, plus the hips' position), eased between keys.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Nature"))
sys.path.insert(0, os.path.join(HERE, "..", "City"))
from plane_kit import (MODELS_DIR, PALETTE_PATH, Model, Part, make_material, preview_arg, render_previews,  # noqa: E402
                       to_blender)
from build_nature import drop_unused_materials  # noqa: E402
from build_city import beam  # noqa: E402

FBX_PATH = os.path.join(MODELS_DIR, "Pilot", "Pilot.fbx")
MOVES_PATH = os.path.join(MODELS_DIR, "Pilot", "PilotMoves.json")
BLEND_PATH = os.path.join(HERE, "Pilot.blend")

INK = 0.014  # a person is ~1/5 a plane's size: thinner ink than the planes' 0.045
FINE = 0.007  # small kit (buckles, straps, pockets)

# Joint -> (parent, position at rest). Left is -x (the pilot faces +z).
JOINTS = {
    "Hips": (None, (0.0, 0.98, 0.0)),
    "Chest": ("Hips", (0.0, 1.12, 0.0)),
    "Head": ("Chest", (0.0, 1.55, 0.0)),
    "UpperArmL": ("Chest", (-0.215, 1.46, 0.0)),
    "ForearmL": ("UpperArmL", (-0.235, 1.18, 0.0)),
    "HandL": ("ForearmL", (-0.245, 0.94, 0.01)),
    "UpperArmR": ("Chest", (0.215, 1.46, 0.0)),
    "ForearmR": ("UpperArmR", (0.235, 1.18, 0.0)),
    "HandR": ("ForearmR", (0.245, 0.94, 0.01)),
    "ThighL": ("Hips", (-0.1, 0.94, 0.0)),
    "ShinL": ("ThighL", (-0.105, 0.53, 0.0)),
    "FootL": ("ShinL", (-0.11, 0.12, -0.01)),
    "ThighR": ("Hips", (0.1, 0.94, 0.0)),
    "ShinR": ("ThighR", (0.105, 0.53, 0.0)),
    "FootR": ("ShinR", (0.11, 0.12, -0.01)),
}
HEAD_C = (0.0, 1.7, 0.0)  # middle of the skull
RISER_ATTACH = (0.0, 1.5, -0.02)
RISER_SPREAD = 0.15  # each riser hooks on this far out, on top of the shoulder
SEAT = (0.0, 0.82, 0.0)  # under the thighs and seat of a seated pilot (Sit)
LINE_LENGTH = 5.2  # confluence to skirt, vertically
CANOPY_R = 3.4
GORES = 12


def J(name):
    return JOINTS[name][1]


def side_sign(side):
    return -1 if side == "L" else 1


# ---------------------------------------------------------------------------------------------- shape helpers


def ring(rx, rz, y, n=8, cx=0.0, cz=0.0, phase=None):
    """An n-gon ring at height y stretched to rx by rz (Unity x, z). Side i is centred on angle(i, n, phase)."""
    phase = math.pi / n if phase is None else phase
    return [(cx + rx * math.cos(phase + 2 * math.pi * k / n), y, cz + rz * math.sin(phase + 2 * math.pi * k / n))
            for k in range(n)]


def side_angle(i, n, phase=None):
    """The direction (degrees, 90 = +z, the front) of a ring's side i."""
    phase = math.pi / n if phase is None else phase
    return math.degrees(phase + 2 * math.pi * (i + 0.5) / n) % 360


def facing(i, n, centre, half_width):
    """True when ring side i points within half_width degrees of centre (90 front, 270 back, 0 right, 180 left)."""
    d = (side_angle(i, n) - centre + 180) % 360 - 180
    return abs(d) <= half_width


def ring_round(centre, axis, r, n=8):
    """An n-gon ring of radius r round ``axis`` through ``centre`` (Unity space)."""
    c, t = Vector(centre), Vector(axis).normalized()
    ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 0, 1))
    u = (ref - t * ref.dot(t)).normalized()
    v = t.cross(u)
    return [tuple(c + (u * math.cos(math.pi / n + 2 * math.pi * k / n) + v * math.sin(math.pi / n + 2 * math.pi * k / n)) * r)
            for k in range(n)]


def limb(p, top, bottom, radii, color, n=6):
    """A tapering tube from ``top`` down to ``bottom`` through ``radii`` (one ring each, evenly spaced), closed."""
    a, b = Vector(top), Vector(bottom)
    rings = [ring_round(a + (b - a) * (k / (len(radii) - 1)), b - a, r, n) for k, r in enumerate(radii)]
    return p.loft(rings, color, cap_start=color, cap_end=color)


def jointed(p, joint, end, profile, color, n=6):
    """A limb hung from a ball joint: a ring at each (distance down the bone, radius) of ``profile``, the negative
    distances doming over ``joint`` so the piece turns about its own ball and never opens a gap at the joint."""
    a = Vector(joint)
    d = (Vector(end) - a).normalized()
    return p.loft([ring_round(a + d * s, d, r, n) for s, r in profile], color, cap_start=color, cap_end=color)


def dome(r):
    """The profile of a ball of radius r over the joint (for ``jointed``)."""
    return [(-0.92 * r, 0.4 * r), (-0.6 * r, 0.8 * r), (0.0, r)]


def band(p, rx, rz, y0, y1, color, n=8, cx=0.0, cz=0.0, outline=FINE):
    """An open sleeve round the body (a belt, a strap, a cuff): just its outside, with fine ink."""
    return p.loft([ring(rx, rz, y0, n, cx, cz), ring(rx, rz, y1, n, cx, cz)], color, outline=outline)


def arc_band(p, centre, rx, rz, y0, y1, a0, a1, steps, thick, color, outline=FINE):
    """A curved strip on an ellipse round ``centre`` from angle a0 to a1 (degrees, 90 = +z), y0..y1 high (a visor)."""
    cx, cy, cz = centre
    sections = []
    for k in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        c, s = math.cos(a), math.sin(a)
        sections.append([(cx + (rx + d) * c, cy + y, cz + (rz + d) * s) for y, d in
                         ((y0, 0.0), (y1, 0.0), (y1, thick), (y0, thick))])
    return p.loft(sections, color, cap_start=color, cap_end=color, outline=outline)


def strap(p, points, width, color, ink=FINE):
    """Webbing along a polyline (flat-ish 4-sided beams joined end to end)."""
    for a, b in zip(points, points[1:]):
        beam(p, a, b, width, color, ink=ink)


def pod(p, centre, w, h, d, color, n=8, cap=None):
    """A rounded box (pockets, packs, pads): an octagon in x/z, its top and bottom rings drawn in."""
    cx, cy, cz = centre
    rings = [ring(w * 0.4, d * 0.4, cy - h * 0.5, n, cx, cz), ring(w * 0.5, d * 0.5, cy - h * 0.38, n, cx, cz),
             ring(w * 0.5, d * 0.5, cy + h * 0.38, n, cx, cz), ring(w * 0.4, d * 0.4, cy + h * 0.5, n, cx, cz)]
    return p.loft(rings, color, cap_start=color, cap_end=cap or color)


def pad(p, centre, size, color, tilt=0.0):
    """A small box stuck on the outside (pockets, buckles, patches), with fine ink."""
    faces = p.box(centre, size, color, rot_x=tilt)
    for f in faces:
        p.hull_thickness[f] = FINE
    return faces


def piece(name, joint):
    return Part(name, J(joint), outline=INK)


# ---------------------------------------------------------------------------------------------- body


def body_head():
    """The bare head under every helmet: neck, face and hair (a helmet covers it, a future bare-headed look won't)."""
    p = piece("Body_Head", "Head")
    hx, hy, hz = HEAD_C
    p.loft([ring(0.05, 0.05, 1.5, 6), ring(0.05, 0.05, 1.63, 6)], "skin", cap_start="skin", cap_end="skin")
    rings = [ring(0.07, 0.08, hy - 0.1, 8), ring(0.092, 0.102, hy - 0.04, 8), ring(0.097, 0.107, hy + 0.04, 8),
             ring(0.083, 0.093, hy + 0.1, 8)]

    def color(band_, side):
        return "skin" if band_ < 2 and facing(side, 8, 90, 70) else "bark"

    p.loft(rings, color, cap_start="skin", fan_end=((hx, hy + 0.13, hz), "bark"))
    for s in (-1, 1):
        p.disc((s * 0.034, hy + 0.005, 0.104), (0, 0, 1), 0.011, 6, "ink")
    return p


# ---------------------------------------------------------------------------------------------- suits


def suit_hips(variant):
    p = piece("Suit_%s_Hips" % variant, "Hips")
    # Widest at the seat, drawn in underneath so the thighs' balls fill out the hips' line below it.
    p.loft([ring(0.12, 0.095, 0.84), ring(0.17, 0.12, 0.91), ring(0.165, 0.118, 1.02), ring(0.157, 0.113, 1.15)],
           "team", cap_start="team", cap_end="team")
    band(p, 0.172, 0.127, 0.99, 1.04, "webbing")  # harness waist belt
    pad(p, (0.0, 1.015, 0.13), (0.07, 0.05, 0.02), "metal")
    if variant == "Flight":  # the vest's skirt over the hips
        band(p, 0.168, 0.123, 1.04, 1.13, "olive")
    return p


def suit_chest(variant):
    p = piece("Suit_%s_Chest" % variant, "Chest")
    # The shoulders slope from the neck down into the upper arms' balls, which round them off.
    p.loft([ring(0.155, 0.11, 1.1), ring(0.176, 0.122, 1.24), ring(0.19, 0.13, 1.36), ring(0.178, 0.124, 1.43),
            ring(0.13, 0.1, 1.5), ring(0.075, 0.068, 1.54)], "team", cap_start="team", cap_end="team")
    p.box((0.0, 1.515, 0.08), (0.08, 0.04, 0.03), "white")  # undershirt at the neck
    if variant == "Flight":
        # Survival vest: olive front and back over the suit, grey yoke left showing on the shoulders.
        def vest(band_, side):
            return "olive" if not (facing(side, 8, 0, 25) or facing(side, 8, 180, 25)) else "sage"

        p.loft([ring(0.168, 0.122, 1.12), ring(0.188, 0.134, 1.24), ring(0.2, 0.142, 1.36), ring(0.188, 0.136, 1.43)],
               vest, outline=FINE)
        for s in (-1, 1):  # chest pockets, either side of the harness
            pad(p, (s * 0.128, 1.32, 0.112), (0.065, 0.085, 0.028), "sage")
    else:
        # Plain coverall: zip pockets on the chest and a name tag.
        for s in (-1, 1):
            pad(p, (s * 0.085, 1.31, 0.128), (0.075, 0.085, 0.02), "darkmetal")
        pad(p, (0.0, 1.4, 0.13), (0.07, 0.03, 0.012), "white")
    # Harness: shoulder straps over the top and down the front and back, a chest strap with its buckle.
    for s in (-1, 1):
        strap(p, [(s * 0.1, 1.4, -0.15), (s * 0.105, 1.522, 0.0), (s * 0.095, 1.497, 0.085), (s * 0.075, 1.42, 0.138),
                  (s * 0.068, 1.12, 0.132)], 0.018, "webbing")
    strap(p, [(-0.15, 1.24, 0.1), (-0.07, 1.24, 0.14), (0.07, 1.24, 0.14), (0.15, 1.24, 0.1)], 0.016, "webbing")
    pad(p, (0.0, 1.24, 0.15), (0.05, 0.04, 0.018), "metal")
    return p


def suit_upper_arm(variant, side):
    s = side_sign(side)
    p = piece("Suit_%s_UpperArm%s" % (variant, side), "UpperArm" + side)
    # The shoulder's ball rounds the top of the arm into the chest's slope, however the arm swings.
    jointed(p, J("UpperArm" + side), J("Forearm" + side), dome(0.068) + [(0.09, 0.06), (0.2, 0.054), (0.28, 0.05)],
            "team", n=8)
    if variant == "Flight":  # the light patch on the outside of each sleeve
        pad(p, (s * 0.284, 1.37, 0.0), (0.014, 0.07, 0.075), "concrete")
    return p


def suit_forearm(variant, side):
    wx, wy, wz = J("Hand" + side)
    p = piece("Suit_%s_Forearm%s" % (variant, side), "Forearm" + side)
    jointed(p, J("Forearm" + side), J("Hand" + side), [(-0.036, 0.036), (0.0, 0.052), (0.12, 0.047), (0.22, 0.04)],
            "team")
    if variant == "Flight":
        band(p, 0.046, 0.046, wy + 0.02, wy + 0.06, "sage", 6, wx, wz)
    return p


def suit_thigh(variant, side):
    s = side_sign(side)
    p = piece("Suit_%s_Thigh%s" % (variant, side), "Thigh" + side)
    # The hip's ball sits up inside the seat, so the leg comes out of the hips rather than hanging under them.
    jointed(p, J("Thigh" + side), J("Shin" + side), [(-0.07, 0.065), (0.0, 0.092), (0.13, 0.088), (0.3, 0.072),
                                                      (0.41, 0.064)], "team")
    band(p, 0.096, 0.093, 0.82, 0.86, "webbing", 6, s * 0.103, 0.0)  # harness leg loop
    if variant == "Flight":  # a pocket on the outside of the thigh
        pad(p, (s * 0.19, 0.7, 0.0), (0.03, 0.12, 0.09), "sage")
    return p


def suit_shin(variant, side):
    s = side_sign(side)
    p = piece("Suit_%s_Shin%s" % (variant, side), "Shin" + side)
    jointed(p, J("Shin" + side), J("Foot" + side), [(-0.048, 0.048), (0.0, 0.068), (0.15, 0.062), (0.35, 0.054)], "team")
    if variant == "Flight":  # knee pads and the shin pockets
        pad(p, (s * 0.105, 0.5, 0.07), (0.1, 0.12, 0.035), "sage")
        pad(p, (s * 0.108, 0.32, 0.062), (0.085, 0.14, 0.025), "olive")
    return p


def suit(variant):
    return ([suit_hips(variant), suit_chest(variant)]
            + [f(variant, side) for side in "LR" for f in (suit_upper_arm, suit_forearm, suit_thigh, suit_shin)])


# ---------------------------------------------------------------------------------------------- helmets


# The jet helmet's shell, bottom to crown: (height over HEAD_C, rx, rz, cz) rings.
JET_SHELL = ((-0.125, 0.106, 0.116, -0.012), (-0.06, 0.128, 0.139, -0.004), (0.02, 0.137, 0.148, 0.0),
             (0.09, 0.124, 0.135, 0.0), (0.14, 0.082, 0.091, 0.0))


def visor(p, shell, top=0.032, a0=28.0, a1=152.0, steps=8, levels=3, thick=0.008):
    """The lowered sun visor: a shield curving round the helmet ``shell`` (height over HEAD_C, rx, rz, cz rings), its
    lower edge sweeping down from the temples to a rounded point over the nose, standing further off the face the
    lower it goes so it bulges out like the real thing."""
    hx, hy, hz = HEAD_C

    def at(y):
        for (ya, *a), (yb, *b) in zip(shell, shell[1:]):
            if y <= yb:
                break
        t = max(0.0, min(1.0, (y - ya) / (yb - ya)))
        return [u + (v - u) * t for u, v in zip(a, b)]

    sections = []
    for k in range(steps + 1):
        a = a0 + (a1 - a0) * k / steps
        bottom = -0.005 - 0.07 * math.cos(math.radians(a - 90) * 90 / (90 - a0)) ** 2
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        outer, inner = [], []
        for v in range(levels):
            y = bottom + (top - bottom) * v / (levels - 1)
            rx, rz, cz = at(y)
            off = 0.006 + 0.018 * (top - y) / 0.1
            outer.append((hx + (rx + off) * c, hy + y, hz + cz + (rz + off) * s))
            inner.append((hx + (rx + off - thick) * c, hy + y, hz + cz + (rz + off - thick) * s))
        sections.append(outer + inner[::-1])
    return p.loft(sections, "visor", cap_start="visor", cap_end="visor", outline=FINE)


def helmet_jet():
    """Hard shell helmet: gold visor under its housing, ear cups, an oxygen mask with its hose."""
    p = piece("Helmet_Jet_Head", "Head")
    hx, hy, hz = HEAD_C
    n = 10
    rings = [ring(rx, rz, hy + y, n, 0, cz) for y, rx, rz, cz in JET_SHELL]

    def color(band_, side):
        return "char" if band_ < 2 and facing(side, n, 90, 50) else "team"  # the face opening, mostly under the mask

    p.loft(rings, color, cap_start="char", fan_end=((hx, hy + 0.163, hz), "team"))
    visor(p, JET_SHELL[:-1])
    arc_band(p, HEAD_C, 0.141, 0.152, 0.03, 0.05, 24, 156, 6, 0.012, "team")  # visor housing
    for s in (-1, 1):
        p.loft([ring_round((s * 0.13, hy - 0.02, -0.01), (1, 0, 0), 0.045, 6),
                ring_round((s * 0.148, hy - 0.02, -0.01), (1, 0, 0), 0.04, 6)], "concrete", cap_end="concrete",
               outline=FINE)
    # Oxygen mask: a cone from the face out to the valve, straps back to the helmet, the hose dropping to the chest.
    mouth = Vector((0.0, hy - 0.085, 0.1))
    axis = Vector((0.0, -0.35, 1.0))
    mask = [ring_round(mouth, axis, 0.064, 6), ring_round(mouth + axis.normalized() * 0.05, axis, 0.054, 6),
            ring_round(mouth + axis.normalized() * 0.085, axis, 0.032, 6)]
    p.loft(mask, "sage", cap_start="sage", cap_end="darkmetal")
    for s in (-1, 1):
        strap(p, [(s * 0.055, hy - 0.06, 0.11), (s * 0.12, hy - 0.04, 0.05)], 0.008, "darkmetal")
    tip = mouth + axis.normalized() * 0.085
    strap(p, [tuple(tip) , (0.035, hy - 0.16, 0.15), (0.06, hy - 0.22, 0.12)], 0.016, "sage", ink=FINE)
    return p


def helmet_leather():
    """Leather flying cap: ear flaps, chin strap and goggles pushed up, the face showing."""
    p = piece("Helmet_Leather_Head", "Head")
    hx, hy, hz = HEAD_C
    rings = [ring(0.078, 0.088, hy - 0.12, 8, 0, 0.012), ring(0.104, 0.114, hy - 0.05, 8, 0, 0.008),
             ring(0.11, 0.12, hy + 0.03, 8), ring(0.1, 0.11, hy + 0.1, 8), ring(0.066, 0.076, hy + 0.145, 8)]

    def color(band_, side):
        return "skin" if band_ < 2 and facing(side, 8, 90, 70) else "team"

    p.loft(rings, color, cap_start="skin", fan_end=((hx, hy + 0.162, hz), "team"))
    for s in (-1, 1):
        p.box((s * 0.105, hy - 0.04, -0.005), (0.03, 0.11, 0.08), "team")
        p.disc((s * 0.035, hy - 0.005, 0.117), (0, 0, 1), 0.012, 6, "ink")
    beam(p, (-0.098, hy - 0.11, 0.035), (0.098, hy - 0.11, 0.035), 0.011, "leather")
    for s in (-1, 1):  # goggles on the forehead
        c = (s * 0.046, hy + 0.06, 0.103)
        rim = ring_round(c, (0, 0.25, 1), 0.036, 8)
        p.loft([[(x, y, z - 0.02) for x, y, z in rim], rim], "darkmetal", cap_start="darkmetal", outline=FINE)
        p.disc((c[0], c[1], c[2] + 0.002), (0, 0.25, 1), 0.028, 8, "glass")
    return p


# ---------------------------------------------------------------------------------------------- backpacks


def backpack_oxygen():
    """The reference's canister pack: a rounded olive pod on the back, two light straps round it, a valve on top."""
    p = piece("Backpack_Oxygen_Chest", "Chest")
    pod(p, (0.0, 1.25, -0.215), 0.25, 0.4, 0.13, "olive", n=8, cap="sage")
    for y in (1.15, 1.35):
        band(p, 0.13, 0.07, y - 0.025, y + 0.025, "concrete", 8, 0.0, -0.215)
    p.loft([ring(0.028, 0.028, 1.45, 6, 0, -0.215), ring(0.028, 0.028, 1.49, 6, 0, -0.215)], "metal",
           cap_end="darkmetal", outline=FINE)
    return p


def backpack_chute():
    """The old seat-pack style: a khaki canvas box with leather straps and the red ripcord handle on the chest strap."""
    p = piece("Backpack_Chute_Chest", "Chest")
    p.box((0.0, 1.25, -0.205), (0.33, 0.4, 0.13), "khaki")
    for y in (1.14, 1.36):
        p.loft([ring(0.172, 0.072, y - 0.02, 4, 0, -0.205, math.pi / 4),
                ring(0.172, 0.072, y + 0.02, 4, 0, -0.205, math.pi / 4)], "leather", outline=FINE)
    pad(p, (-0.1, 1.28, 0.17), (0.05, 0.05, 0.035), "red")
    return p


# ---------------------------------------------------------------------------------------------- gloves and boots


def glove(variant, side):
    s = side_sign(side)
    wx, wy, wz = J("Hand" + side)
    color = "olive" if variant == "Flight" else "leather"
    p = piece("Gloves_%s_Hand%s" % (variant, side), "Hand" + side)
    cuff = 0.05 if variant == "Flight" else 0.062  # leather gauntlets flare out over the sleeve
    p.loft([ring(cuff, cuff, wy + 0.07, 6, wx, wz), ring(0.045, 0.045, wy - 0.01, 6, wx, wz)], color,
           cap_start=color, cap_end=color)
    p.box((wx + s * 0.005, wy - 0.06, wz + 0.01), (0.055, 0.1, 0.09), color)  # palm and back of the hand
    p.box((wx + s * 0.008, wy - 0.135, wz + 0.02), (0.048, 0.06, 0.08), color, rot_x=0.25)  # curled fingers
    p.box((wx - s * 0.012, wy - 0.06, wz + 0.06), (0.026, 0.06, 0.028), color, rot_x=0.4)  # thumb
    return p


def boot(variant, side):
    s = side_sign(side)
    ax, ay, az = J("Foot" + side)
    color = "webbing" if variant == "Combat" else "leather"
    p = piece("Boots_%s_Foot%s" % (variant, side), "Foot" + side)
    top = 0.29 if variant == "Combat" else 0.25
    p.loft([ring(0.072, 0.072, top, 6, ax, az), ring(0.07, 0.074, 0.08, 6, ax, az)], color,
           cap_start=color, cap_end=color)
    if variant == "Combat":
        band(p, 0.075, 0.075, top - 0.03, top, "darkmetal", 6, ax, az)
    p.box((ax, 0.06, 0.055), (0.12, 0.095, 0.26), color)
    p.box((ax, 0.04, 0.17), (0.11, 0.06, 0.05), color)  # rounded toe
    p.box((ax, 0.013, 0.06), (0.128, 0.026, 0.3), "tire")
    return p


# Slot -> variant -> its pieces. The first variant of each is the default look (the reference's).
GEAR = {
    "Suit": {"Flight": lambda: suit("Flight"), "Coverall": lambda: suit("Coverall")},
    "Helmet": {"Jet": lambda: [helmet_jet()], "Leather": lambda: [helmet_leather()]},
    "Backpack": {"Oxygen": lambda: [backpack_oxygen()], "Chute": lambda: [backpack_chute()]},
    "Gloves": {"Flight": lambda: [glove("Flight", s) for s in "LR"], "Leather": lambda: [glove("Leather", s) for s in "LR"]},
    "Boots": {"Combat": lambda: [boot("Combat", s) for s in "LR"], "Leather": lambda: [boot("Leather", s) for s in "LR"]},
}


# ---------------------------------------------------------------------------------------------- parachute


# Canopy patterns (G4): which gores take the main colour ("team") and which the trim ("trim"), by band (0 at the skirt,
# 3 by the vent) and gore; then the vent cap's colour. One object each, so Unity swaps pattern by showing another.
PATTERNS = [
    ("Stripes", lambda band_, gore: "team" if gore % 2 == 0 else "trim", "trim"),
    ("Quarters", lambda band_, gore: "team" if (gore // 3) % 2 == 0 else "trim", "trim"),
    ("Crown", lambda band_, gore: "team" if band_ >= 2 else "trim", "team"),
    ("Solid", lambda band_, gore: "team", "trim"),
]


def riser_tops():
    ax, ay, az = RISER_ATTACH
    return [(ax + s * 0.27, ay + 0.62, az) for s in (-1, 1)]


def canopy_rings(inset=0.0):
    """
    The dome's rings from the skirt up to the vent, ``inset`` in (for the inside shell). Each gore bulges out between
    its seams and the skirt scallops up between them, so it reads as cloth filled with air.
    """
    ax, ay, az = RISER_ATTACH
    skirt = ay + LINE_LENGTH
    profile = [(CANOPY_R, skirt, 0.32), (CANOPY_R * 0.98, skirt + 0.8, 0.1), (CANOPY_R * 0.8, skirt + 1.6, 0.05),
               (CANOPY_R * 0.46, skirt + 2.15, 0.02), (CANOPY_R * 0.14, skirt + 2.33, 0.0)]
    rings = []
    for r, y, scallop in profile:
        pts = []
        for k in range(GORES * 2):
            a = math.pi * k / GORES
            mid = k % 2 == 1  # halfway across a gore: bulged out, the skirt lifted
            rr = (r * (1.07 if mid else 1.0)) - inset
            pts.append((ax + rr * math.cos(a), y + (scallop if mid else 0.0) - inset * 0.5, az + rr * math.sin(a)))
        rings.append(pts)
    return rings


def canopy(name, gore, vent):
    """One pattern's dome, with an inside shell so it isn't hollow seen from under it (no ink of its own)."""
    p = Part("Canopy" + name, RISER_ATTACH, outline=0.05)

    def color(band_, side):
        return gore(band_, side // 2)

    p.loft(canopy_rings(), color, cap_end=vent)
    faces = p.loft(canopy_rings(0.04), color, cap_end=vent, outline=False)
    bmesh.ops.reverse_faces(p.bm, faces=faces)
    return p


def parachute():
    """The rigging every canopy shares: lines from every other seam to the riser tops, the risers to the shoulders."""
    p = Part("Parachute", RISER_ATTACH, outline=0.03)
    ax, ay, az = RISER_ATTACH
    skirt = ay + LINE_LENGTH
    tops = riser_tops()
    for k in range(0, GORES, 2):
        a = 2 * math.pi * k / GORES  # on a seam, between two bulges
        seam = (ax + CANOPY_R * math.cos(a), skirt, az + CANOPY_R * math.sin(a))
        beam(p, seam, tops[0] if seam[0] < ax else tops[1], 0.012, "white", sides=3)
    for s, top in zip((-1, 1), tops):
        beam(p, top, (ax + s * RISER_SPREAD, ay, az), 0.022, "webbing", ink=0.008)
        # Steering toggle where the hands hold on under the canopy.
        p.box((top[0] - s * 0.03, ay + 0.45, az + 0.02), (0.035, 0.06, 0.035), "red")
    return p


# ---------------------------------------------------------------------------------------------- moves


def euler_matrix(e):
    """Unity's Quaternion.Euler(x, y, z) as a matrix in Unity space: z, then x, then y (extrinsic)."""
    x, y, z = (math.radians(v) for v in e)
    rx = Matrix(((1, 0, 0), (0, math.cos(x), -math.sin(x)), (0, math.sin(x), math.cos(x))))
    ry = Matrix(((math.cos(y), 0, math.sin(y)), (0, 1, 0), (-math.sin(y), 0, math.cos(y))))
    rz = Matrix(((math.cos(z), -math.sin(z), 0), (math.sin(z), math.cos(z), 0), (0, 0, 1)))
    return ry @ rx @ rz


SWAP = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))  # Unity <-> Blender axes (a mirror: its own inverse)


def mirror(e):
    """A left joint's angles for the right one (and back): the x pitch stays, the yaw and roll flip."""
    return (e[0], -e[1], -e[2])


REST = {
    "UpperArmL": (2, 0, -6), "ForearmL": (-10, 0, 0), "HandL": (0, 0, 4),
    "ThighL": (0, 0, -1), "ShinL": (2, 0, 0), "FootL": (-2, 0, 0),
}

REST_R = {name[:-1] + "R": mirror(e) for name, e in REST.items()}


def pose(hips=(0.0, 0.0, 0.0), **joints):
    """A full pose: REST, with every given joint set; a left joint given alone sets its right one mirrored."""
    out = {name: (0.0, 0.0, 0.0) for name in JOINTS}
    for name, e in REST.items():
        out[name] = e
        out[name[:-1] + "R"] = mirror(e)
    for name, e in joints.items():
        out[name] = e
        if name.endswith("L") and name[:-1] + "R" not in joints:
            out[name[:-1] + "R"] = mirror(e)
    out["_hips"] = tuple(J("Hips")[i] + hips[i] for i in range(3))
    return out


def world_joints(p):
    """Each joint's world (figure-space) rotation and position under pose p (Unity space)."""
    out = {}
    for name, (parent, rest) in JOINTS.items():
        local = euler_matrix(p[name])
        if parent is None:
            out[name] = (local, Vector(p["_hips"]))
        else:
            prot, ppos = out[parent]
            out[name] = (prot @ local, ppos + prot @ (Vector(rest) - Vector(JOINTS[parent][1])))
    return out


SOLE = [(x, -0.12, z) for x in (-0.064, 0.064) for z in (-0.08, 0.22)]  # boot sole corners, from the ankle


def lowest_sole(p):
    w = world_joints(p)
    return min((w["Foot" + s][0] @ Vector(c) + w["Foot" + s][1]).y for s in "LR" for c in SOLE)


def planted(p):
    """p with the hips lowered (and shifted) so the lower sole stands on the ground under the rest ankle: a crouch."""
    w = world_joints(p)
    low = min((w["Foot" + s][1] for s in "LR"), key=lambda v: v.y)
    q = dict(p)
    q["_hips"] = (p["_hips"][0], p["_hips"][1] - lowest_sole(p), p["_hips"][2] + J("FootL")[2] - low.z)
    return q


STAND = pose()
HANG = pose(UpperArmL=(-160, 0, -5), ForearmL=(-28, 0, 0), HandL=(-15, 0, 0), Head=(-6, 0, 0),
            ThighL=(-10, 0, -3), ShinL=(16, 0, 0), FootL=(28, 0, 0), ThighR=(2, 0, 3), ShinR=(9, 0, 0), FootR=(26, 0, 0))
FREEFALL = pose(UpperArmL=(-15, 0, -100), ForearmL=(-35, 0, 0), Chest=(8, 0, 0), Head=(-20, 0, 0),
                ThighL=(-12, 0, -16), ShinL=(38, 0, 0), FootL=(20, 0, 0))
SIT = pose(Chest=(-8, 0, 0), Head=(6, 0, 0),
           ThighL=(-86, -6, 0), ShinL=(80, 0, 0), FootL=(8, 0, 0),
           UpperArmL=(-22, 0, -14), ForearmL=(-62, 0, 0), HandL=(0, 0, 0),  # left hand forward on the throttle
           UpperArmR=(-32, 0, 3), ForearmR=(-52, 0, 0), HandR=(10, 0, 0))  # right hand on the stick between the knees


def keyed(*keys):
    return [{"t": t, "pose": p} for t, p in keys]


def slope(times, values, k):
    """
    A key's tangent, as Unity's PilotModels sets it: flat at the ends and where the key is a peak or a dip (so nothing
    overshoots a pose), else the slope from the key before to the key after (so a move flows through its middle keys).
    """
    if k == 0 or k == len(values) - 1 or (values[k] - values[k - 1]) * (values[k + 1] - values[k]) <= 0:
        return 0.0
    return (values[k + 1] - values[k - 1]) / (times[k + 1] - times[k - 1])


def sample(keys, t):
    """The pose at time t: each number Hermite-interpolated between the keys either side, with slope()'s tangents."""
    if t <= keys[0]["t"]:
        return keys[0]["pose"]
    if t >= keys[-1]["t"]:
        return keys[-1]["pose"]
    k = max(i for i in range(len(keys)) if keys[i]["t"] <= t)
    times = [key["t"] for key in keys]
    dt = times[k + 1] - times[k]
    u = (t - times[k]) / dt
    h00, h10, h01, h11 = 2 * u ** 3 - 3 * u ** 2 + 1, u ** 3 - 2 * u ** 2 + u, -2 * u ** 3 + 3 * u ** 2, u ** 3 - u ** 2
    out = {}
    for j in keys[0]["pose"]:
        vals = []
        for a in range(3):
            series = [key["pose"][j][a] for key in keys]
            vals.append(h00 * series[k] + h10 * dt * slope(times, series, k)
                        + h01 * series[k + 1] + h11 * dt * slope(times, series, k + 1))
        out[j] = tuple(vals)
    return out


def replant(keys, times):
    """Adds a planted key at each time (sampled, then its hips put back on the ground), so a crouch doesn't sink."""
    for t in times:
        keys.append({"t": t, "pose": planted(sample(keys, t))})
        keys.sort(key=lambda k: k["t"])
    return keys


# Name -> (loops, keys). The order is World.PilotMove's.
MOVES = [
    ("Idle", True, keyed(
        (0.0, STAND),
        (2.0, pose(Chest=(-1.5, 2, 0), Head=(1, 4, 0), UpperArmL=(2, 0, -8), ForearmL=(-13, 0, 0))),
        (4.0, STAND))),
    ("LookAround", False, keyed(
        (0.0, STAND),
        (0.9, pose(Chest=(0, 10, 0), Head=(-4, 38, 0))),
        (2.0, pose(Chest=(0, 12, 0), Head=(-6, 42, 0))),
        (3.0, pose(Chest=(0, -10, 0), Head=(2, -38, 0))),
        (3.9, pose(Chest=(0, -11, 0), Head=(4, -40, 0))),
        (5.0, STAND))),
    ("Stretch", False, keyed(
        (0.0, STAND),
        (1.1, pose(Chest=(-9, 0, 0), Head=(-16, 0, 0), UpperArmL=(-172, 0, -16), ForearmL=(-26, 0, 0), HandL=(-30, 0, 0))),
        (1.6, pose(Chest=(-13, 0, 0), Head=(-20, 0, 0), UpperArmL=(-176, 0, -22), ForearmL=(-14, 0, 0), HandL=(-35, 0, 0))),
        (2.6, pose(Chest=(-11, 0, 0), Head=(-18, 0, 0), UpperArmL=(-174, 0, -19), ForearmL=(-20, 0, 0), HandL=(-30, 0, 0))),
        (4.0, STAND))),
    ("CheckWatch", False, keyed(
        (0.0, STAND),
        (0.7, pose(Chest=(4, -8, 0), Head=(26, -22, 0), UpperArmL=(-40, 0, 18), ForearmL=(-100, 0, 0), HandL=(0, 0, -60),
                   UpperArmR=REST_R["UpperArmR"], ForearmR=REST_R["ForearmR"], HandR=REST_R["HandR"])),
        (2.4, pose(Chest=(5, -9, 0), Head=(28, -20, 0), UpperArmL=(-42, 0, 18), ForearmL=(-102, 0, 0), HandL=(0, 0, -60),
                   UpperArmR=REST_R["UpperArmR"], ForearmR=REST_R["ForearmR"], HandR=REST_R["HandR"])),
        (3.5, STAND))),
    ("Wave", False, keyed(
        (0.0, STAND),
        (0.5, pose(Head=(0, 12, -4), UpperArmR=(-20, 0, 145), ForearmR=(-20, 0, 25))),
        (0.85, pose(Head=(0, 12, -4), UpperArmR=(-20, 0, 145), ForearmR=(-20, 0, -22))),
        (1.2, pose(Head=(0, 12, -4), UpperArmR=(-20, 0, 145), ForearmR=(-20, 0, 25))),
        (1.55, pose(Head=(0, 12, -4), UpperArmR=(-20, 0, 145), ForearmR=(-20, 0, -22))),
        (1.9, pose(Head=(0, 12, -4), UpperArmR=(-20, 0, 145), ForearmR=(-20, 0, 25))),
        (2.3, pose(Head=(0, 10, -2), UpperArmR=(-20, 0, 140), ForearmR=(-20, 0, 5))),
        (3.2, STAND))),
    ("Sit", True, keyed(
        (0.0, SIT),
        (2.0, dict(SIT, Chest=(-9.5, 0, 0), Head=(5, 8, 0))),
        (4.0, SIT))),
    ("Freefall", True, keyed(
        (0.0, FREEFALL),
        (0.6, pose(UpperArmL=(-25, 0, -112), ForearmL=(-48, 0, 0), Chest=(10, 0, 0), Head=(-22, 0, 0),
                   ThighL=(-18, 0, -21), ShinL=(46, 0, 0), FootL=(20, 0, 0), ThighR=(-8, 0, 21), ShinR=(30, 0, 0))),
        (1.2, FREEFALL))),
    ("Deploy", False, keyed(
        (0.0, FREEFALL),
        (0.25, pose(UpperArmL=(-60, 0, -140), ForearmL=(-20, 0, 0), Chest=(14, 0, 0), Head=(-10, 0, 0),
                    ThighL=(-28, 0, -6), ShinL=(30, 0, 0), FootL=(25, 0, 0))),
        (0.7, HANG))),
    ("Hang", True, keyed(
        (0.0, HANG),
        (1.5, dict(HANG, ThighL=(2, 0, -3), ShinL=(9, 0, 0), ThighR=(-10, 0, 3), ShinR=(16, 0, 0), Head=(-6, 16, 0))),
        (3.0, HANG))),
    ("Land", False, replant(keyed(
        (0.0, planted(pose(UpperArmL=(-150, 0, -14), ForearmL=(-30, 0, 0), ThighL=(-12, 0, -3), ShinL=(16, 0, 0),
                           FootL=(-4, 0, 0)))),
        (0.08, planted(pose(Chest=(10, 0, 0), UpperArmL=(-110, 0, -16), ForearmL=(-35, 0, 0), ThighL=(-35, 0, -4),
                            ShinL=(60, 0, 0), FootL=(-25, 0, 0)))),
        (0.2, planted(pose(Chest=(26, 0, 0), Head=(-18, 0, 0), UpperArmL=(-55, 0, -18), ForearmL=(-40, 0, 0),
                           ThighL=(-68, 0, -4), ShinL=(116, 0, 0), FootL=(-48, 0, 0)))),
        (0.65, planted(pose(Chest=(18, 0, 0), Head=(-10, 0, 0), UpperArmL=(-35, 0, -14), ForearmL=(-30, 0, 0),
                            ThighL=(-45, 0, -3), ShinL=(80, 0, 0), FootL=(-35, 0, 0)))),
        (1.4, STAND)), (0.14, 0.42, 0.9, 1.15))),
    ("Risers", True, keyed(
        (0.0, dict(HANG, **{k: STAND[k] for k in STAND if k.startswith(("Thigh", "Shin", "Foot")) or k == "_hips"})),
        (1.6, dict(HANG, Head=(-10, 10, 0), **{k: STAND[k] for k in STAND if k.startswith(("Thigh", "Shin", "Foot"))
                                                 or k == "_hips"})),
        (3.2, dict(HANG, **{k: STAND[k] for k in STAND if k.startswith(("Thigh", "Shin", "Foot")) or k == "_hips"})))),
]


def check_feet():
    """Prints how far the soles stray from the ground through each on-foot move (Unity's PilotMoveTests holds 5 cm)."""
    for name, _, keys in MOVES:
        if name in ("Sit", "Freefall", "Deploy", "Hang"):
            continue
        lows = [lowest_sole(sample(keys, t / 20)) for t in range(int(keys[-1]["t"] * 20) + 1)]
        print("[pilot] %-10s soles %.3f .. %.3f" % (name, min(lows), max(lows)))


def write_moves(path):
    """
    The moves for Unity (plain lists, for JsonUtility): the joints and their parents, then per move whether it loops and
    its keys: time, the hips' position, and every joint's Euler angles in ``joints`` order, three numbers each.
    """
    out = []
    for name, loops, keys in MOVES:
        out.append({"name": name, "loop": loops, "keys": [
            {"t": k["t"], "hips": [round(v, 4) for v in k["pose"]["_hips"]],
             "euler": [round(v, 3) for j in JOINTS for v in k["pose"][j]]} for k in keys]})
    with open(path, "w") as f:
        json.dump({"joints": list(JOINTS), "parents": [parent or "" for parent, _ in JOINTS.values()], "moves": out}, f,
                  indent=1)
    print("[pilot] wrote", path)


# ---------------------------------------------------------------------------------------------- build


def build():
    model = Model("Pilot", team_hex="8E9A86")  # previews in the reference's grey-green suit
    model.materials.append(make_material("Plane_Trim", (1, 1, 1), bpy.data.images.load(PALETTE_PATH)))
    parts = [body_head()]
    for slot, variants in GEAR.items():
        for make in variants.values():
            parts += make()
    parts += [parachute()] + [canopy(name, gore, vent) for name, gore, vent in PATTERNS]
    for part in parts:
        obj = part.finish(model.collection, model.materials)
        drop_unused_materials(obj)
        model.objects.append(obj)
    for name, (_, pos) in JOINTS.items():
        model.mark("Joint" + name, pos)
    model.mark("PilotRiserAttach", RISER_ATTACH)
    model.mark("PilotSeat", SEAT)
    model.export(FBX_PATH, BLEND_PATH)
    write_moves(MOVES_PATH)
    check_feet()
    default = [o for o in model.objects if o.name.startswith("Body_")
               or any(o.name.startswith("%s_%s_" % (slot, next(iter(v)))) for slot, v in GEAR.items())]
    print("[pilot] default outfit triangles:", sum(len(p.vertices) - 2 for o in default for p in o.data.polygons))
    return model


# ---------------------------------------------------------------------------------------------- rig (in the .blend)


def is_default(name):
    return name.startswith("Body_") or any(name.startswith("%s_%s_" % (s, next(iter(v)))) for s, v in GEAR.items())


def joint_of(name):
    return name.rsplit("_", 1)[1]


def make_rig(pieces, name, offset=(0.0, 0.0, 0.0)):
    """Joint empties (Blender space) with ``pieces`` (objects whose origin is their joint) parented on, at ``offset``."""
    empties = {}
    root = bpy.data.objects.new(name, None)
    root.location = to_blender(offset)
    bpy.context.scene.collection.objects.link(root)
    for joint, (parent, pos) in JOINTS.items():
        e = bpy.data.objects.new("%s_%s" % (name, joint), None)
        e.empty_display_size = 0.05
        e.rotation_mode = "QUATERNION"
        bpy.context.scene.collection.objects.link(e)
        e.parent = empties[parent] if parent else root
        base = Vector((0, 0, 0)) if parent is None else Vector(JOINTS[parent][1])
        e.location = to_blender(Vector(pos) - base)
        empties[joint] = e
    for obj in pieces:
        obj.parent = empties[obj.get("joint") or joint_of(obj.name)]
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.location = (0, 0, 0)
    return empties


def apply_pose(empties, p):
    for joint, e in empties.items():
        e.rotation_quaternion = (SWAP @ euler_matrix(p[joint]) @ SWAP).to_quaternion()
    empties["Hips"].location = to_blender(p["_hips"])


def keyframe_moves(empties, fps=30):
    """Every move on the timeline one after another, a marker at each start, so the .blend can be scrubbed through."""
    scene = bpy.context.scene
    scene.render.fps = fps
    frame = 1
    for name, _, keys in MOVES:
        scene.timeline_markers.new(name, frame=frame)
        for k in keys:
            apply_pose(empties, k["pose"])
            f = frame + round(k["t"] * fps)
            for e in empties.values():
                e.keyframe_insert("rotation_quaternion", frame=f)
            empties["Hips"].keyframe_insert("location", frame=f)
        frame += round(keys[-1]["t"] * fps) + fps // 2
    scene.frame_start, scene.frame_end = 1, frame


def rig_blend(model):
    """The pilot in its default gear on a rig, the other variants hidden, every move keyframed. Saves the .blend."""
    pieces = [o for o in model.objects if o.name.startswith(("Body_", "Suit_", "Helmet_", "Backpack_", "Gloves_", "Boots_"))]
    empties = make_rig(pieces, "PilotRig")
    for o in pieces:
        o.hide_set(not is_default(o.name))
        o.hide_render = not is_default(o.name)
    keyframe_moves(empties)
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print("[pilot] saved the rig and moves in", BLEND_PATH)
    return empties


# ---------------------------------------------------------------------------------------------- previews


def outfit(model, choice):
    """Copies of the pieces for one outfit: choice = {slot: variant}, anything left out takes the default."""
    out = []
    for o in model.objects:
        if not o.name.startswith(("Body_", "Suit_", "Helmet_", "Backpack_", "Gloves_", "Boots_")):
            continue
        slot = o.name.split("_")[0]
        variant = o.name.split("_")[1]
        if slot != "Body" and variant != choice.get(slot, next(iter(GEAR[slot]))):
            continue
        c = o.copy()
        c.name = "Copy_" + o.name
        c["joint"] = joint_of(o.name)
        c.hide_render = False
        bpy.context.scene.collection.objects.link(c)
        out.append(c)
    return out


def figure(model, name, offset, p, choice=None):
    empties = make_rig(outfit(model, choice or {}), name, offset)
    apply_pose(empties, p)
    return empties


def lay_out(model, rig):
    """Poses in a row, the alternate gear beside them, and a pilot hanging under each canopy pattern."""
    rig["Hips"].parent.location = (0.0, 0.0, -1000.0)  # the rigged original (every variant) out of the shots
    moves = {name: keys for name, _, keys in MOVES}
    row = [("Idle", 0.0), ("LookAround", 1.5), ("Stretch", 1.6), ("CheckWatch", 1.5), ("Wave", 0.5),
           ("Sit", 0.0), ("Land", 0.2)]
    for k, (name, t) in enumerate(row):
        figure(model, "Pose" + name, (1.0 * k, 0.0, 0.0), sample(moves[name], t))
    figure(model, "Alt", (-7.0, 0.0, 0.0), STAND,
           {"Suit": "Coverall", "Helmet": "Leather", "Backpack": "Chute", "Gloves": "Leather", "Boots": "Leather"})
    figure(model, "Front", (-4.0, 0.0, 0.0), STAND)
    figure(model, "Freefall", (12.0, 6.0, 0.0), sample(moves["Freefall"], 0.0))
    for k, (name, _, _) in enumerate(PATTERNS):
        shift = Vector((30.0 + 9.0 * k, 1.0, 0.0))
        figure(model, "Hang" + name, tuple(shift), sample(moves["Hang"], 0.0))
        canopy_obj = bpy.data.objects["Canopy" + name]
        canopy_obj.location = to_blender(shift + Vector(RISER_ATTACH))
        if k == 0:
            bpy.data.objects["Parachute"].location = to_blender(shift + Vector(RISER_ATTACH))


SHOTS = [
    ("pilot_front", (-4.0 + 1.2, 1.4, 4.0), (-4.0, 0.95, 0.0), None),
    ("pilot_side", (-4.0 + 2.5, 1.3, 0.6), (-4.0, 0.95, 0.0), None),
    ("pilot_back", (-4.0 - 1.6, 1.6, -3.4), (-4.0, 0.95, 0.0), None),
    ("pilot_face", (-4.0 + 0.4, 1.8, 0.9), (-4.0, 1.62, 0.0), None),
    ("pilot_alt", (-7.0 + 1.2, 1.4, 4.0), (-7.0, 0.95, 0.0), None),
    ("pilot_poses", (3.0, 1.4, 7.5), (3.0, 1.0, 0.0), None),
    ("pilot_freefall", (12.0 + 2.0, 7.0, 3.5), (12.0, 7.0, 0.0), None),
    ("pilot_chute", (30.0 + 9.0, 4.0, 10.0), (30.0, 4.5, 0.0), None),
    ("pilot_chute_hands", (30.0 + 1.2, 2.6, 2.4), (30.0, 2.4, 0.0), None),
    ("pilot_chute_under", (30.0 + 3.0, -1.0, 5.0), (30.0, 5.5, 0.0), None),
    ("pilot_patterns", (43.5, 12.0, 48.0), (43.5, 7.5, 0.0), None),
]


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    built = build()
    rig = rig_blend(built)
    out = preview_arg(argv)
    if out:
        lay_out(built, rig)
        render_previews(out, SHOTS)
