"""
Builds the VFX pack (Design/03-Art/final-asset-list.md, Pack 8) from scratch, then exports it for Unity.

    blender --background --factory-startup --python ArtSource/VFX/build_vfx.py -- [--preview <dir>]

Outputs:
    Assets/_Project/Art/Models/VFX/VFX.fbx          one object per effect mesh, Unity axes
    Assets/_Project/Art/Textures/PlanePalette.png   the shared palette atlas (rewritten, same as the planes')
    ArtSource/VFX/VFX.blend                         the generated scene, for hand tweaks and renders

Smoke is chunky low-poly puffs, never soft particles (lowpoly-asset-style-guide.html). Unity's particle systems emit
these meshes, so each is a single material (one submesh) on the palette:

    PuffA, PuffB, PuffC     lumpy smoke / fire / dust / spray puffs, ~1 m across, origin at the centre. Painted on the
                            neutral "team" ramp (top light, sides base, belly blue-violet shadow), so the particle colour
                            tints them: grey wisps, black smoke, orange fire, sand dust, white spray. Spin them about the
                            vertical axis only, or the baked tones turn over.
    ChunkA, ChunkB          debris chips (a faceted rock, a bent plate), ~0.5 m, neutral ramp, tinted per use
    Spark                   a white-hot shard 1 m long down +Z; particles align it to their velocity
    Sparkle                 a four-point star facing -Z, 1 m across (billboarded)
    Ring                    a flat shock ring in the XZ plane, 1 m across
    SplashCrown             a jagged water crown, 1 m across, base at y = 0
    Boom                    "BOOM!" in the game's display font on a jagged burst, with an ink outline, facing -Z
    Wrench                  a chunky wrench icon with an ink outline, facing -Z (repair sparkles)

Two more are ordinary mesh renderers on the shared materials:

    ShieldBubble            a faceted geodesic bubble, 1 m across, on the neutral ramp (FX_ShieldBubble, tinted)
    AfterburnerFlame        a jagged flame cone 1 m long from the nozzle at the origin back down -Z (Weapon_Glow)

FX get no ink outline (asset-style-as-built.md rule 4), except the two graphic icons (Boom, Wrench).
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from plane_kit import (COLORS, MODELS_DIR, PALETTE_PATH, PROJECT, Model, Part, make_material,  # noqa: E402
                       preview_arg, render_previews, to_blender)

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(MODELS_DIR, "VFX", "VFX.fbx")
BLEND_PATH = os.path.join(HERE, "VFX.blend")
FONT_PATH = os.path.join(PROJECT, "Assets", "Resources", "Fonts", "Display.ttf")

NEUTRAL = "team"  # the neutral grey ramp: tinted by the particle colour in Unity


# ---------------------------------------------------------------------------------------------- shapes


def blob(p, center, radius, seed, color=NEUTRAL, subdiv=2, lump=0.14, squash=1.0, smooth=False):
    """
    A lumpy, faceted ball: an icosphere (subdiv 1 = 20 faces, 2 = 80) with each vertex pushed in or out a little
    (deterministic per seed). No ink hull: FX are outline-free.
    """
    rnd = random.Random(seed)
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=1.0)
    verts = {}
    for v in tmp.verts:
        d = v.co.normalized()
        r = radius * (1.0 + rnd.uniform(-lump, lump))
        verts[v] = p.vert((center[0] + d.x * r, center[1] + d.z * r * squash, center[2] + d.y * r))
    faces = [p.face([verts[v] for v in f.verts], color, smooth=smooth) for f in tmp.faces]
    tmp.free()
    bmesh.ops.recalc_face_normals(p.bm, faces=faces)
    return faces


def star_points(n, r_out, r_in, phase=0.0, jitter=0.0, seed=0):
    rnd = random.Random(seed)
    pts = []
    for k in range(2 * n):
        r = (r_out if k % 2 == 0 else r_in) * (1.0 + rnd.uniform(-jitter, jitter))
        a = phase + math.pi * k / n
        pts.append((r * math.cos(a), r * math.sin(a)))
    return pts


def star_ring(z, r_out, r_in, points, phase=0.0):
    """A ring whose points alternate between two radii: a jagged, toy-like flame edge (as on the weapons' exhausts)."""
    return [((r_out if k % 2 == 0 else r_in) * math.cos(phase + math.pi * k / points),
             (r_out if k % 2 == 0 else r_in) * math.sin(phase + math.pi * k / points), z) for k in range(2 * points)]


def slab(p, outline_xy, z_front, z_back, front, side, back=None, outline=True):
    """Extrudes a flat outline (x, y) between two depths; the front cap faces -Z."""
    front_ring = [(x, y, z_front) for x, y in outline_xy]
    back_ring = [(x, y, z_back) for x, y in outline_xy]
    return p.loft([front_ring, back_ring], side, cap_start=front, cap_end=back or side, outline=outline)


# ---------------------------------------------------------------------------------------------- puffs and chunks


def build_puffs():
    """Three puff shapes so a cloud of them never looks stamped: a single lump, a pair, a cluster of three."""
    a = Part("PuffA")
    blob(a, (0, 0, 0), 0.5, 1)

    b = Part("PuffB")
    blob(b, (-0.08, -0.02, 0), 0.44, 2)
    blob(b, (0.24, 0.12, 0.06), 0.28, 3, subdiv=1, lump=0.1)

    c = Part("PuffC")
    blob(c, (0.0, -0.06, 0.0), 0.36, 4)
    blob(c, (-0.26, 0.10, 0.10), 0.27, 5, subdiv=1, lump=0.1)
    blob(c, (0.22, 0.14, -0.12), 0.25, 6, subdiv=1, lump=0.1)
    return [a, b, c]


def build_chunks():
    """Debris chips: a faceted lump and a bent plate (sheet metal), ~0.5 m."""
    a = Part("ChunkA")
    blob(a, (0, 0, 0), 0.25, 7, subdiv=1, lump=0.3, squash=0.7)

    b = Part("ChunkB")
    t = 0.03
    plate = [[(-0.25, t, -0.18), (0.0, t + 0.06, -0.22), (0.24, t, -0.12), (0.2, t - 0.04, 0.2), (-0.18, t + 0.02, 0.16)],
             [(-0.25, -t, -0.18), (0.0, -t + 0.06, -0.22), (0.24, -t, -0.12), (0.2, -t - 0.04, 0.2), (-0.18, -t + 0.02, 0.16)]]
    b.loft(plate, NEUTRAL, cap_start=NEUTRAL, cap_end=NEUTRAL, outline=False)
    return [a, b]


# ---------------------------------------------------------------------------------------------- glow shapes


def build_spark():
    """A stretched diamond down +Z, white-hot head and flame tail. Unity aligns it to the particle's velocity."""
    p = Part("Spark")
    w = 0.08
    p.loft([[(0.0, 0.0, -0.5)] * 4, [(w, 0, 0.15), (0, w, 0.15), (-w, 0, 0.15), (0, -w, 0.15)]],
           "flame", fan_end=((0.0, 0.0, 0.5), "glint"), glow=True)
    return p


def build_sparkle():
    """A fat four-point star facing -Z (both sides), 1 m across: repair sparkles and the respawn shimmer."""
    p = Part("Sparkle")
    pts = star_points(4, 0.5, 0.13, phase=math.pi / 2)
    rim = [p.vert((x, y, 0.0)) for x, y in pts]
    centre_front, centre_back = p.vert((0, 0, -0.04)), p.vert((0, 0, 0.04))
    n = len(rim)
    for i in range(n):
        j = (i + 1) % n
        color = "glint" if i % 2 == 0 else "flame"
        p.loose_faces.append(p.face([rim[i], centre_front, rim[j]], color, glow=True))
        p.loose_faces.append(p.face([rim[j], centre_back, rim[i]], color, glow=True))
    bmesh.ops.recalc_face_normals(p.bm, faces=p.loose_faces)
    return p


def build_ring():
    """A flat shock ring lying in XZ, 1 m across, a little thick so it reads edge-on. Flat white faces (pure tint), flame rim."""
    p = Part("Ring")
    sides, r_out, r_in, h = 20, 0.5, 0.40, 0.025

    def flat(r, y):
        return [(r * math.cos(2 * math.pi * k / sides), y, r * math.sin(2 * math.pi * k / sides)) for k in range(sides)]

    p.loft([flat(r_in, -h), flat(r_out, -h), flat(r_out, h), flat(r_in, h), flat(r_in, -h)],
           lambda b, s: ("glint", "flame", "glint", "flame")[b], glow=True)
    return p


def build_splash_crown():
    """A thick-walled water crown, 1 m across at the base: jagged spikes of different heights, white tips."""
    p = Part("SplashCrown")
    rnd = random.Random(11)
    sides = 14
    heights = [rnd.uniform(0.55, 1.0) if k % 2 == 0 else rnd.uniform(0.3, 0.45) for k in range(sides)]

    def crown(r_base, flare, y_scale):
        pts = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            r = r_base + flare * heights[k]
            pts.append((r * math.cos(a), heights[k] * y_scale, r * math.sin(a)))
        return pts

    outer_bottom = [(0.5 * math.cos(2 * math.pi * k / sides), 0.0, 0.5 * math.sin(2 * math.pi * k / sides)) for k in range(sides)]
    inner_bottom = [(0.38 * math.cos(2 * math.pi * k / sides), 0.0, 0.38 * math.sin(2 * math.pi * k / sides)) for k in range(sides)]
    p.loft([outer_bottom, crown(0.5, 0.25, 1.0), crown(0.42, 0.25, 0.92), inner_bottom, outer_bottom],
           lambda b, s: ("glass", "white", "glass", NEUTRAL)[b], outline=False)
    return p


# ---------------------------------------------------------------------------------------------- icons


def text_letters(text, size, extrude, resolution=1):
    """Each character of ``text`` in the display font as (Unity-space verts, faces), centred as a word, facing -Z."""
    font = bpy.data.fonts.load(FONT_PATH) if os.path.exists(FONT_PATH) else None
    letters, x = [], 0.0
    for ch in text:
        curve = bpy.data.curves.new("Char", "FONT")
        curve.body = ch
        if font:
            curve.font = font
        curve.size = size
        curve.extrude = extrude
        curve.resolution_u = resolution
        obj = bpy.data.objects.new("Char", curve)
        bpy.context.scene.collection.objects.link(obj)
        bpy.context.view_layer.update()
        mesh = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
        xs = [v.co.x for v in mesh.vertices]
        ys = [v.co.y for v in mesh.vertices]
        x0, x1 = min(xs), max(xs)
        # Blender text: x along the line, y up, extruded about z = 0. Unity: x right, y up, front (+z Blender) at -z.
        verts = [(v.co.x - x0 + x, v.co.y, -v.co.z) for v in mesh.vertices]
        faces = [list(f.vertices) for f in mesh.polygons]
        letters.append((verts, faces, (min(ys) + max(ys)) * 0.5))
        x += (x1 - x0) + size * 0.06
        obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
        bpy.data.objects.remove(obj)
        bpy.data.curves.remove(curve)
    width = x - size * 0.06
    return [([(vx - width * 0.5, vy, vz) for vx, vy, vz in verts], faces, cy) for verts, faces, cy in letters]


def add_letter(p, verts, faces, front, side, tilt, lift, pivot):
    """One letter, tilted about its own centre and lifted, so the word bounces. Colours by face direction."""
    ca, sa = math.cos(tilt), math.sin(tilt)
    px, py = pivot
    vs = []
    for x, y, z in verts:
        dx, dy = x - px, y - py
        vs.append(p.vert((px + dx * ca - dy * sa, py + dx * sa + dy * ca + lift, z)))
    made = []
    for f in faces:
        try:
            made.append(p.face([vs[i] for i in f], side))
        except ValueError:  # a duplicate face from the font fill
            continue
    bmesh.ops.recalc_face_normals(p.bm, faces=made)
    for f in made:
        f.normal_update()
        if f.normal.y < -0.9:  # Blender -y = Unity -z: the face toward the viewer
            f[p.col] = [n for n, _ in COLORS].index(front)
    p.hull_faces.extend(made)


def build_boom():
    """ "BOOM!" in yellow on a jagged ember burst, everything inked, ~3.4 m wide, facing -Z for a view-aligned particle."""
    p = Part("Boom", outline=0.06)
    # The burst sits behind the letters.
    burst = star_points(9, 1.7, 1.2, phase=0.17, jitter=0.1, seed=3)
    burst = [(x, y * 0.66) for x, y in burst]
    slab(p, burst, 0.08, 0.14, "ember", "ember")
    for k, (verts, faces, cy) in enumerate(text_letters("BOOM!", 1.55, 0.08)):
        xs = [v[0] for v in verts]
        cx = (min(xs) + max(xs)) * 0.5
        tilt = math.radians((-7, 5, -4, 8, -9)[k % 5])
        lift = (0.0, 0.07, -0.04, 0.06, 0.02)[k % 5] - cy
        add_letter(p, verts, faces, "sun", "char", tilt, lift, (cx, cy))
    return p


def build_wrench():
    """A chunky wrench icon (open jaw up, handle down), sun yellow with an ink outline, 1 m tall, facing -Z."""
    p = Part("Wrench", outline=0.035)
    d0, d1 = -0.05, 0.05
    # Handle: a rounded bar.
    slab(p, [(-0.08, -0.5), (0.08, -0.5), (0.1, -0.44), (0.1, 0.12), (-0.1, 0.12), (-0.1, -0.44)], d0, d1, "sun", "sun")
    # Head: a thick C round an open jaw at the top (one concave cap; the kit's triangulation fills it).
    cx, cy, r_out, r_in = 0.0, 0.26, 0.24, 0.115
    angles = [math.radians(125 + 290 * k / 7) for k in range(8)]
    head = [(cx + r_out * math.cos(a), cy + r_out * math.sin(a)) for a in angles]
    head += [(cx + r_in * math.cos(a), cy + r_in * math.sin(a)) for a in reversed(angles)]
    slab(p, head, d0 - 0.01, d1 + 0.01, "sun", "sun")
    return p


# ---------------------------------------------------------------------------------------------- mesh renderers


def build_shield_bubble():
    """A geodesic bubble, 1 m across, faceted: neutral faces with a scatter of bright glints. Tinted in Unity."""
    p = Part("ShieldBubble")
    rnd = random.Random(21)
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=3, radius=0.5)
    verts = {v: p.vert((v.co.x, v.co.z, v.co.y)) for v in tmp.verts}
    faces = [p.face([verts[v] for v in f.verts], "glint" if rnd.random() < 0.18 else NEUTRAL) for f in tmp.faces]
    tmp.free()
    bmesh.ops.recalc_face_normals(p.bm, faces=faces)
    p.loose_faces.extend(faces)
    return p


def build_afterburner():
    """A jagged flame 1 m long from the nozzle at the origin back down -Z: white core, flame, ember tips."""
    p = Part("AfterburnerFlame")
    p.loft([star_ring(0.0, 0.42, 0.34, 6), star_ring(-0.22, 0.5, 0.36, 6, 0.26)], "flame", cap_start="glint", glow=True)
    p.loft([star_ring(-0.22, 0.5, 0.36, 6, 0.26), star_ring(-0.6, 0.32, 0.18, 6)],
           lambda b, s: "flame" if s % 2 == 0 else "ember", glow=True, fan_end=((0.0, 0.0, -1.0), "ember"))
    p.loft([star_ring(0.02, 0.24, 0.2, 6, 0.26), star_ring(-0.3, 0.2, 0.14, 6, 0.26)], "glint", glow=True,
           fan_end=((0.0, 0.0, -0.55), "flame"))
    return p


# ---------------------------------------------------------------------------------------------- build


def single_material(obj, material):
    """Particle meshes render one submesh: put every face (ink hull included) on ``material``."""
    for poly in obj.data.polygons:
        poly.material_index = 0
    obj.data.materials.clear()
    obj.data.materials.append(material)


def build():
    model = Model("VFX")
    palette = bpy.data.images[os.path.basename(PALETTE_PATH)]
    fx = make_material("FX_Mesh", (1, 1, 1), palette)
    # Emissive in the previews, as the particle shader is unlit.
    nt = fx.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 0.6
    bubble = make_material("FX_ShieldBubble", (1, 1, 1), palette, alpha=0.35, cull=False)
    glow = make_material("Weapon_Glow", (1, 1, 1), palette, cull=False)

    particles = build_puffs() + build_chunks() + [build_spark(), build_sparkle(), build_ring(), build_splash_crown(),
                                                   build_boom(), build_wrench()]
    for part in particles:
        obj = part.finish(model.collection, model.materials + [glow])
        single_material(obj, fx)
        model.objects.append(obj)
    for part, material in ((build_shield_bubble(), bubble), (build_afterburner(), glow)):
        obj = part.finish(model.collection, model.materials + [glow])
        single_material(obj, material)
        model.objects.append(obj)
    model.export(FBX_PATH, BLEND_PATH)


# Preview layout: Unity positions, scales, and a tint per object (as Unity's particle colour would).
LAYOUT = {
    "PuffA": ((-3.0, 2.2, 0.0), 1.4, (0.32, 0.32, 0.35)),
    "PuffB": ((-1.5, 2.2, 0.0), 1.4, (1.0, 0.55, 0.12)),
    "PuffC": ((0.0, 2.2, 0.0), 1.4, (0.85, 0.85, 0.88)),
    "ChunkA": ((1.4, 2.2, 0.0), 1.4, (0.45, 0.48, 0.55)),
    "ChunkB": ((2.4, 2.2, 0.0), 1.4, (0.75, 0.6, 0.4)),
    "Spark": ((3.6, 2.2, 0.0), 1.2, None),
    "Sparkle": ((-3.0, 0.4, 0.0), 1.0, None),
    "Ring": ((-1.5, 0.3, 0.0), 1.2, None),
    "SplashCrown": ((0.0, -0.2, 0.0), 1.4, None),
    "Wrench": ((1.5, 0.4, 0.0), 1.2, None),
    "ShieldBubble": ((3.3, 0.4, 0.0), 1.6, (0.45, 0.75, 1.0)),
    "AfterburnerFlame": ((-2.3, -1.6, 0.0), 1.6, None),
    "Boom": ((1.6, -2.0, 0.0), 1.0, None),
}

SHOTS = [
    ("vfx_front", (0.3, 0.6, -14.0), (0.3, 0.2, 0.0), None),
    ("vfx_threequarter", (-8.0, 5.0, -10.0), (0.3, 0.2, 0.0), None),
    ("vfx_top", (0.3, 14.0, -0.01), (0.3, 0.0, 0.0), 10.0),
]


def lay_out():
    """Places each mesh for the preview, tinting the neutral ones through a per-object copy of its material."""
    for o in bpy.data.objects:
        if o.name not in LAYOUT:
            continue
        pos, scale, tint = LAYOUT[o.name]
        o.location = to_blender(pos)
        o.scale = (scale, scale, scale)
        if o.name == "AfterburnerFlame":
            o.rotation_euler = (0, 0, math.radians(90))  # show it side-on
        if tint:
            m = o.data.materials[0].copy()
            bsdf = m.node_tree.nodes.get("Principled BSDF")
            mul = m.node_tree.nodes.new("ShaderNodeMix")
            mul.data_type = "RGBA"
            mul.blend_type = "MULTIPLY"
            mul.inputs["Factor"].default_value = 1.0
            tex = next(n for n in m.node_tree.nodes if n.type == "TEX_IMAGE")
            m.node_tree.links.new(tex.outputs["Color"], mul.inputs[6])
            mul.inputs[7].default_value = (*tint, 1.0)
            m.node_tree.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
            if "Emission Color" in bsdf.inputs and bsdf.inputs["Emission Color"].is_linked:
                m.node_tree.links.new(mul.outputs[2], bsdf.inputs["Emission Color"])
            o.data.materials[0] = m


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    out = preview_arg(argv)
    if out:
        lay_out()
        render_previews(out, SHOTS)
