"""
Shared pieces for the map packs (one per shipped map: Tropics, Prairie, Isles, Alpine), on top of the plane kit and
the nature and city scripts' shapes. Each pack script builds its parts and calls ``export_pack``.

Every piece is in Unity space (x right, y up, z forward), at its real (toy) size in metres, standing on its origin.
"""
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "..")
for sub in ("", "Nature", "City"):
    sys.path.insert(0, os.path.join(ART, sub))

from plane_kit import MODELS_DIR, Model, Part, preview_arg, render_previews, to_blender  # noqa: E402,F401
from build_nature import cone_tier, drop_unused_materials, lump, snow_on_top, trunk, tube, yring  # noqa: E402,F401
from build_city import at, beam, block, cylinder, facets, ngon_pts, prism, quad, rect_pts, trim, wall_quad, windows  # noqa: E402,F401

PROP_INK = 0.08  # houses and props: ~0.5-1% of their size, so the outline shows from a low pass
OFF = 0.15  # doors and shutters float this far off their wall


def rect4(w, d, cx=0.0, cz=0.0):
    """A plain w (x) by d (z) rectangle, 4 points round the vertical axis (rect_pts with no chamfer repeats its corners)."""
    hw, hd = w * 0.5, d * 0.5
    return [(cx + hw, cz - hd), (cx + hw, cz + hd), (cx - hw, cz + hd), (cx - hw, cz - hd)]


def gable(p, w, d, y0, rise, color, under=None, overhang=0.8, ink=PROP_INK, cx=0.0, cz=0.0, snow=None):
    """
    A gable roof over a w (x) by d (z) footprint from eave height ``y0``, its ridge ``rise`` above along z, eaves
    ``overhang`` out: two 0.4 m slabs (each convex, so their end caps triangulate cleanly), meeting at the ridge.
    ``snow`` colours their tops, ``under`` their soffits and ends.
    """
    hw, hd = w * 0.5 + overhang, d * 0.5 + overhang
    t = 0.4
    slope = rise / (w * 0.5)
    eave = y0 - overhang * slope  # the eave drops with the overhang, along the roof's pitch
    faces = []
    for sgn in (-1.0, 1.0):
        def ring(z):
            return [(cx + sgn * hw, eave, cz + z), (cx + sgn * hw, eave + t, cz + z), (cx, y0 + rise + t, cz + z),
                    (cx, y0 + rise, cz + z)]
        # side 1 is the top face (outer edge up to the ridge); the rest are soffit and edges
        color_of = lambda b, s: (snow or color) if s == 1 else (under or color)  # noqa: E731
        ring_a, ring_b = ring(-hd), ring(hd)
        if sgn > 0:  # keep the winding the same way round on both sides
            ring_a, ring_b = ring_b, ring_a
        faces += p.loft([ring_a, ring_b], color_of, cap_start=under or color, cap_end=under or color, outline=ink)
    return faces


def gable_end(p, w, d, y0, rise, color, cx=0.0, cz=0.0, ink=PROP_INK):
    """The triangular wall under a gable roof, closing both ends of a w x d mass from ``y0`` to the ridge."""
    hw, hd = w * 0.5, d * 0.5
    tri = lambda z: [(cx - hw, y0, cz + z), (cx, y0 + rise, cz + z), (cx + hw, y0, cz + z)]  # noqa: E731
    return p.loft([tri(-hd), tri(hd)], color, cap_start=color, cap_end=color, outline=ink)


def house(p, w, d, h, wall, roof, rise, cx=0.0, cz=0.0, under=None, snow=None, overhang=0.8, base=-1.0):
    """Walls (sunk ``base`` below the ground) with gable ends and a gable roof along z."""
    prism(p, rect4(w, d, cx, cz), base, h, wall, outline=PROP_INK)
    gable_end(p, w - 0.05, d - 0.05, h, rise, wall, cx, cz)
    gable(p, w, d, h, rise, roof, under=under, overhang=overhang, cx=cx, cz=cz, snow=snow)


def hip(p, w, d, y0, rise, color, overhang=0.8, cx=0.0, cz=0.0, ink=PROP_INK, snow=None, sides=4):
    """A pyramid (hip) roof: a low eave ring and a fan to the apex. ``sides`` > 4 makes a cone (round huts)."""
    if sides == 4:
        base = rect4(w + 2 * overhang, d + 2 * overhang, cx, cz)
    else:
        base = ngon_pts(w * 0.5 + overhang, sides, cx, cz)
    rim = at(base, y0)
    lip = at(base, y0 + 0.4)
    return p.loft([rim, lip], color, cap_start=color, fan_end=((cx, y0 + rise, cz), snow or color), outline=ink)


def door(p, wall_facet, a, w, h, color, y0=0.0):
    wall_quad(p, wall_facet, a - w * 0.5, a + w * 0.5, y0, y0 + h, color, off=OFF)


def side(poly, normal):
    """The facet of ``poly`` facing (closest to) the unity xz ``normal``."""
    return max(facets(poly), key=lambda f: f[1][0] * normal[0] + f[1][1] * normal[1])


def rotor(p, hub, radius, blades, color, width, color_hub="darkmetal", sail=None, tilt=0.0):
    """
    Blades turning about Unity +y (Unity stands the part up on its side, so a Spinner turns it): flat paddles from the
    hub in the xz plane. ``sail`` (a colour) adds a lattice-and-cloth sail on each arm (windmills).
    """
    hx, hy, hz = hub
    cylinder(p, hx, hz, hy - 0.4, hy + 0.6, width * 0.9, color_hub, sides=8, ink=PROP_INK)
    for k in range(blades):
        a = 2 * math.pi * k / blades
        d = (math.cos(a), math.sin(a))
        s = (-d[1], d[0])
        r0 = width * 0.8
        if sail:
            beam(p, (hx + d[0] * r0, hy, hz + d[1] * r0), (hx + d[0] * radius, hy, hz + d[1] * radius), width * 0.25,
                 color, ink=PROP_INK, sides=4)
            w0, w1 = radius * 0.25, radius * 0.98
            sw = radius * 0.2
            pts = [(hx + d[0] * w0, hy + 0.1, hz + d[1] * w0), (hx + d[0] * w1, hy + 0.1, hz + d[1] * w1),
                   (hx + d[0] * w1 + s[0] * sw, hy + 0.1, hz + d[1] * w1 + s[1] * sw),
                   (hx + d[0] * w0 + s[0] * sw, hy + 0.1, hz + d[1] * w0 + s[1] * sw)]
            p.loft([pts, [(x, y + 0.25, z) for x, y, z in pts]], sail, cap_start=sail, cap_end=sail, outline=PROP_INK * 0.6)
        else:
            tw = width * (1.0 + tilt)
            pts = [(hx + d[0] * r0 - s[0] * width * 0.5, hy, hz + d[1] * r0 - s[1] * width * 0.5),
                   (hx + d[0] * radius - s[0] * tw * 0.5, hy, hz + d[1] * radius - s[1] * tw * 0.5),
                   (hx + d[0] * radius + s[0] * tw * 0.5, hy, hz + d[1] * radius + s[1] * tw * 0.5),
                   (hx + d[0] * r0 + s[0] * width * 0.5, hy, hz + d[1] * r0 + s[1] * width * 0.5)]
            p.loft([pts, [(x, y + 0.2, z) for x, y, z in pts]], color, cap_start=color, cap_end=color, outline=PROP_INK * 0.6)


def lattice(p, y0, y1, r0, r1, color, sides=4, bays=4, member=0.18):
    """A tapering lattice tower (legs, rings and cross braces) of ``sides`` legs from radius ``r0`` at ``y0`` to ``r1``."""
    def corner(k, t):
        r = r0 + (r1 - r0) * t
        a = math.pi / sides + 2 * math.pi * k / sides
        return (r * math.cos(a), y0 + (y1 - y0) * t, r * math.sin(a))

    for k in range(sides):
        beam(p, corner(k, 0.0), corner(k, 1.0), member * 1.3, color, ink=PROP_INK * 0.5)
        for b in range(bays):
            t0, t1 = b / bays, (b + 1) / bays
            beam(p, corner(k, t1), corner((k + 1) % sides, t1), member, color, ink=PROP_INK * 0.5)
            beam(p, corner(k, t0), corner((k + 1) % sides, t1), member * 0.8, color, ink=PROP_INK * 0.5)


def export_pack(name, parts, fbx_dir_name=None, markers=()):
    """Exports the parts as Art/Models/<name>/<name>.fbx (+ ArtSource/MapPacks/<name>.blend). ``markers``: (name, unity pos)."""
    model = Model(name)
    model.add(parts)
    for marker_name, pos in markers:
        model.mark(marker_name, pos)
    for obj in model.objects:
        drop_unused_materials(obj)
    folder = fbx_dir_name or name
    model.export(os.path.join(MODELS_DIR, folder, f"{folder}.fbx"), os.path.join(HERE, f"{name}.blend"))
    return model


def lay_out_row(names, spacing, z=0.0, y=0.0):
    """Lines objects up along x for the preview renders (they all export at the origin)."""
    for i, n in enumerate(names):
        bpy.data.objects[n].location = to_blender(((i - (len(names) - 1) * 0.5) * spacing, y, z))


def main(build, lay_out, shots):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, shots)
