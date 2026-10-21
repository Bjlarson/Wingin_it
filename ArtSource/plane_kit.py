"""
Shared kit for the plane build scripts (ArtSource/<Class>/build_*.py): the palette atlas, a face-tagged mesh builder
with an ink hull, the section shapes, landing gear, broken-edge caps, materials, FBX export and preview renders.

Every coordinate is in Unity space (x right, y up, z forward/nose, metres), matching the placeholder prefab built by
FlightTestSceneBuilder.BuildPlanePrefab: every class shares that prefab, so each exported object's origin is its
part's pivot there (the pivots below) and the placeholder boxes stay as the hitboxes.

Style rules (Design/03-Art/asset-style-as-built.md): flat normals (smooth only on round, blunt things), chunky
chamfers, 3 tones per colour picked from the face normal (top light, side base, belly shadow), team colour on its own
material (tinted at runtime), an inverted-hull ink outline per part.
"""
import math
import os
import struct
import zlib

import bmesh
import bpy
from mathutils import Vector

PROJECT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
MODELS_DIR = os.path.join(PROJECT, "Assets", "_Project", "Art", "Models")
PALETTE_PATH = os.path.join(PROJECT, "Assets", "_Project", "Art", "Textures", "PlanePalette.png")

OUTLINE = 0.045  # ink hull thickness, metres

# Unity-space pivots, identical to FlightTestSceneBuilder.BuildPlanePrefab.
WING_L = (-2.65, 0.0, 0.4)
WING_R = (2.65, 0.0, 0.4)
HSTAB = (0.0, 0.2, -3.3)
VSTAB = (0.0, 1.0, -3.35)
ENGINE = (0.0, 0.0, 3.75)
PROP = (0.0, 0.0, 4.15)
GEAR_L = (-1.1, -0.55, 0.0)
GEAR_R = (1.1, -0.55, 0.0)
GEAR_NOSE = (0.0, -0.55, 2.7)
MAIN_LEG, MAIN_WHEEL = 1.2, 0.35  # the plane rests on these: LandingGear.wheelDrop is shared by every class
NOSE_LEG, NOSE_WHEEL = 1.25, 0.28

# ---------------------------------------------------------------------------------------------- palette

INK = "1E2433"
SHADOW_TARGET = (52, 46, 110)  # shadows drift toward blue-violet, never black
LIGHT_TARGET = (255, 250, 235)  # highlights drift toward warm white

# name -> base hex. "flat" swatches use the same value for all three tones.
COLORS = [
    ("body", "EFE6D2"),
    ("metal", "7E8898"),
    ("darkmetal", "4F5868"),
    ("glass", "8FE3FF"),
    ("tire", "2C3242"),
    ("white", "F4F2EC"),
    ("sun", "FFB21E"),
    ("char", "2B2E35"),
    ("ember", "FF5E1A"),
    ("glint", "FFFFFF"),
    ("ink", INK),
    ("team", "D7D7D7"),  # neutral ramp, multiplied by the team colour in Unity
    ("blue", "2F7DF6"),
    ("red", "F2463C"),
    ("flame", "FFC93A"),  # hot core of flames, tracers and muzzle flashes (flat, on the glow material)
    # Biomes (lowpoly-asset-style-guide.html): nature dressing, mesas, the islet, clouds (ArtSource/Nature).
    ("leaf", "3E9E52"),  # broadleaf canopies
    ("pine", "2F7D5B"),  # conifers: bluer and darker than leaf, so the mountain trees read apart
    ("grass", "5DBB63"),  # bushes, grass tufts, palm fronds
    ("bark", "8A5A3B"),
    ("drywood", "B89A74"),  # dead trees, dry brush, driftwood
    ("sand", "F4C27A"),
    ("sandstone", "E58A45"),  # mesa cliffs, desert rocks
    ("clay", "C25A3A"),  # the darker mesa bands
    ("stone", "8C96A6"),  # mountain and coast rock
    ("snow", "F3F6F8"),
    ("cactus", "4FA36B"),
    ("flower", "FF7BAC"),
    ("cloud", "F1F7FF"),  # its own ramp: white tops, blue-grey bellies
    # City (ArtSource/City): blues and greys, the biome swatch, with brick and the warm accents above.
    ("concrete", "C8CFDA"),  # towers, sidewalks, quay paving
    ("slate", "7F9CC4"),  # the city's blue-grey: spandrels, cladding, bridge girders
    ("window", "5FA8D8"),  # building glass: deeper than canopy glass so a whole facade doesn't glare
    ("brick", "B9654B"),
    ("asphalt", "434A5A"),
    ("water", "2EC4B6"),  # fountains (the sea itself is Unity's)
    # Airfield (ArtSource/Airfield): military kit, sandbags and the windsock.
    ("olive", "6E8050"),  # AA guns, crates, the radar cabin
    ("khaki", "C9B07C"),  # sandbags, canvas
    ("orange", "FF8A2A"),  # windsock, mechanics' overalls
    # Landmarks (ArtSource/Landmarks): the Pyramid's pale stone, so it stands off the sand and the orange mesas.
    ("limestone", "EBDDB9"),
    # Sea (living-world.md, section 2): the Water shader's colours, kept here so they stay in the shared palette.
    ("water_deep", "1E6E9C"),  # open sea
    ("water_shallow", "3FC1C9"),  # teal shallows by the beaches
    ("foam", "F4FBFF"),  # wave crests and the shore band
]
FLAT = {"glint", "ember", "ink", "flame"}
TEAM_KEYS = {"team"}
CELL = 32  # pixels per cell
CELLS = 16  # cells per side: 16 x 16, four colours per row, each taking 3 cells (light, base, shadow) and a spare


def hx(h):
    return [int(h[i:i + 2], 16) for i in (0, 2, 4)]


def mix(c, target, t):
    return [round(v + (target[i] - v) * t) for i, v in enumerate(c)]


def tones(name, h):
    c = hx(h)
    if name in FLAT:
        return [c, c, c]
    if name == "team":
        return [[250, 250, 250], c, mix(c, SHADOW_TARGET, 0.30)]
    if name == "cloud":
        return [[255, 255, 255], c, hx("D5E6F7")]  # style guide: bellies go soft blue, not violet
    return [mix(c, LIGHT_TARGET, 0.25), c, mix(c, SHADOW_TARGET, 0.30)]


def swatch_cell(index, tone):
    """Cell (column, row-from-bottom) of a colour's tone (0 light, 1 base, 2 shadow)."""
    return (index % 4) * 4 + tone, CELLS - 1 - index // 4


def swatch_uv(name, tone):
    index = [n for n, _ in COLORS].index(name)
    cx, cy = swatch_cell(index, tone)
    return ((cx + 0.5) / CELLS, (cy + 0.5) / CELLS)


def write_palette(path=PALETTE_PATH):
    size = CELLS * CELL
    rows = [[(255, 0, 255)] * size for _ in range(size)]  # magenta = unused, easy to spot
    for index, (name, h) in enumerate(COLORS):
        for tone, rgb in enumerate(tones(name, h)):
            cx, cy = swatch_cell(index, tone)
            for y in range(cy * CELL, cy * CELL + CELL):
                for x in range(cx * CELL, cx * CELL + CELL):
                    rows[y][x] = tuple(rgb)
    raw = b"".join(b"\x00" + bytes(v for px in rows[size - 1 - y] for v in px) for y in range(size))  # PNG is top-down

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


# ---------------------------------------------------------------------------------------------- mesh building


def to_blender(p):
    """Unity (x right, y up, z forward) -> Blender (x, z, y). The FBX export plus Unity's axis conversion maps it back."""
    return Vector((p[0], p[2], p[1]))


class Part:
    """One exported object: faces tagged with a palette colour, plus pieces that get an ink hull."""

    def __init__(self, name, origin=(0.0, 0.0, 0.0), outline=OUTLINE):
        self.name = name
        self.origin = Vector(origin)
        self.outline = outline  # ink hull thickness: scale it with the object (asset-style-as-built.md)
        self.bm = bmesh.new()
        self.col = self.bm.faces.layers.int.new("col")
        self.smooth = self.bm.faces.layers.int.new("smooth")
        self.glow = self.bm.faces.layers.int.new("glow")  # unlit faces (flames, tracers) on the 4th material
        self.hull_faces = []  # closed pieces that get an outline
        self.hull_thickness = {}  # face -> its own ink thickness, for pieces whose loft passed one (else self.outline)
        self.loose_faces = []  # one-sided decals, oriented by hand, no outline

    def vert(self, p):
        return self.bm.verts.new(to_blender(Vector(p) - self.origin))

    def face(self, verts, color, smooth=False, glow=False):
        f = self.bm.faces.new(verts)
        f[self.col] = [n for n, _ in COLORS].index(color)
        f[self.smooth] = 1 if smooth else 0
        f[self.glow] = 1 if glow else 0
        return f

    def loft(self, sections, color, cap_start=None, cap_end=None, smooth=False, fan_end=None, outline=True, glow=False):
        """
        Skins rings of points (same count, same winding) into quads. ``color`` is a colour name or
        f(band, side) -> name. Caps close the ends (``fan_end`` closes the last ring with a fan to that point).
        ``glow`` puts the faces on the unlit glow material (no outline). ``outline`` may be a number: this piece's own
        ink thickness (e.g. a small rooftop box on a big building).
        """
        if glow:
            outline = False
        rings = [[self.vert(p) for p in ring] for ring in sections]
        n = len(sections[0])
        faces = []
        for b, (r0, r1) in enumerate(zip(rings, rings[1:])):
            for i in range(n):
                j = (i + 1) % n
                c = color(b, i) if callable(color) else color
                faces.append(self.face([r0[i], r0[j], r1[j], r1[i]], c, smooth, glow))
        if cap_start:
            faces.append(self.face(list(reversed(rings[0])), cap_start, smooth, glow))
        if fan_end:
            tip = self.vert(fan_end[0])
            for i in range(n):
                c = fan_end[1](i) if callable(fan_end[1]) else fan_end[1]
                faces.append(self.face([rings[-1][i], rings[-1][(i + 1) % n], tip], c, smooth, glow))
        elif cap_end:
            faces.append(self.face(rings[-1], cap_end, smooth, glow))
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        (self.hull_faces if outline else self.loose_faces).extend(faces)
        if outline and outline is not True:
            for f in faces:
                self.hull_thickness[f] = float(outline)
        return faces

    def box(self, center, size, color, rot_x=0.0):
        cx, cy, cz = center
        sx, sy, sz = (s * 0.5 for s in size)
        ca, sa = math.cos(rot_x), math.sin(rot_x)

        def ring(z):
            pts = [(-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy)]
            return [(cx + x, cy + y * ca - z * sa, cz + y * sa + z * ca) for x, y in pts]

        return self.loft([ring(-sz), ring(sz)], color, cap_start=color, cap_end=color)

    def disc(self, center, normal, radius, sides, color, glow=False):
        """A one-sided flat polygon facing ``normal`` (Unity space)."""
        n = Vector(normal).normalized()
        a = n.cross(Vector((0, 1, 0)))
        if a.length < 1e-3:
            a = n.cross(Vector((1, 0, 0)))
        a.normalize()
        b = n.cross(a)
        c = Vector(center)
        verts = [self.vert(c + (a * math.cos(t) + b * math.sin(t)) * radius)
                 for t in (2 * math.pi * k / sides + math.pi / sides for k in range(sides))]
        f = self.face(verts, color, glow=glow)
        f.normal_update()
        if f.normal.dot(to_blender(n)) < 0:
            f.normal_flip()
        self.loose_faces.append(f)
        return f

    def finish(self, collection, materials):
        bm = self.bm
        bm.normal_update()
        key_index = {n: i for i, (n, _) in enumerate(COLORS)}

        # Ink hull: copy each closed piece, push it out along the averaged vertex normals, flip it.
        hull_src = [f for f in self.hull_faces if f.is_valid]
        vmap = {}
        for f in hull_src:
            thickness = self.hull_thickness.get(f, self.outline)
            for v in f.verts:
                if v not in vmap:
                    vmap[v] = bm.verts.new(v.co + v.normal * thickness)
        ink = key_index["ink"]
        for f in hull_src:
            nf = bm.faces.new([vmap[v] for v in reversed(f.verts)])
            nf[self.col] = ink
            nf[self.smooth] = 0

        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
        bm.normal_update()

        # Palette UVs: every face sits on one swatch cell, tone picked from its normal (Blender z = Unity up).
        uv = bm.loops.layers.uv.new("Palette")
        names = [n for n, _ in COLORS]
        for f in bm.faces:
            name = names[f[self.col]]
            up = f.normal.z
            tone = 0 if up > 0.7 else 2 if up < -0.5 else 1
            u = swatch_uv(name, tone)
            f.material_index = 3 if f[self.glow] else 2 if name == "ink" else 1 if name in TEAM_KEYS else 0
            f.smooth = bool(f[self.smooth])
            for loop in f.loops:
                loop[uv].uv = u

        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        for m in materials:
            mesh.materials.append(m)
        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = to_blender(self.origin)
        collection.objects.link(obj)

        # Second UV set for skins and decals: an ordinary unwrap.
        mesh.uv_layers.new(name="Decal")
        mesh.uv_layers.active = mesh.uv_layers["Decal"]
        bpy.context.view_layer.objects.active = obj
        for o in bpy.context.selected_objects:
            o.select_set(False)
        obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02)
        bpy.ops.object.mode_set(mode="OBJECT")
        mesh.uv_layers.active = mesh.uv_layers["Palette"]
        mesh.uv_layers["Palette"].active_render = True
        return obj


def marker(collection, name, unity_pos):
    """An empty exported with the meshes: Unity reads its position (e.g. a gun muzzle) off the imported model."""
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_size = 0.2
    obj.location = to_blender(unity_pos)
    collection.objects.link(obj)
    return obj


# ---------------------------------------------------------------------------------------------- shapes


def octagon(z, w, top, bot, yc):
    """Fuselage section: a box with big 45-degree-ish chamfers."""
    return [(w * 0.55, yc + top, z), (w, yc + top * 0.42, z), (w, yc - bot * 0.42, z), (w * 0.55, yc - bot, z),
            (-w * 0.55, yc - bot, z), (-w, yc - bot * 0.42, z), (-w, yc + top * 0.42, z), (-w * 0.55, yc + top, z)]


def ring(z, r, sides, yc=0.0, phase=None, x0=0.0):
    phase = math.pi / sides if phase is None else phase
    return [(x0 + r * math.cos(phase + 2 * math.pi * k / sides), yc + r * math.sin(phase + 2 * math.pi * k / sides), z)
            for k in range(sides)]


def airfoil(le, te, t):
    """Chunky 6-point airfoil as (chord position z, thickness offset) pairs, LE first, upper surface next."""
    c = le - te
    return [(le, 0.0), (le - 0.12 * c, 0.5 * t), (te + 0.38 * c, 0.45 * t), (te, 0.04 * t),
            (te + 0.35 * c, -0.38 * t), (le - 0.12 * c, -0.42 * t)]


def wing_section(x, le, te, t, y):
    return [(x, y + off, z) for z, off in airfoil(le, te, t)]


def fin_section(y, le, te, t):
    return [(off, y, z) for z, off in airfoil(le, te, t)]


def lerp_section(sections, x):
    """Interpolates a (x, le, te, t) planform table at span station ``x``."""
    for a, b in zip(sections, sections[1:]):
        if a[0] <= x <= b[0]:
            k = (x - a[0]) / (b[0] - a[0])
            return tuple(a[i] + (b[i] - a[i]) * k for i in range(4))
    raise ValueError(f"{x} is outside the planform")


# ---------------------------------------------------------------------------------------------- shared parts


def build_gear(name, pivot, leg, wheel, width, strut=0.06, door=None, sides=10):
    """Strut from inside the wing/fuselage down to the axle, a fat tyre with metal hubs, optionally a gear door."""
    p = Part(name, pivot)
    x, y, z = pivot
    axle = y - leg
    s0, s1 = strut, strut - 0.01
    p.loft([
        [(x + dx, y + 0.35, z + dz) for dx, dz in ((s0, s0), (s0, -s0), (-s0, -s0), (-s0, s0))],
        [(x + dx, axle, z + dz) for dx, dz in ((s1, s1), (s1, -s1), (-s1, -s1), (-s1, s1))],
    ], "darkmetal", cap_start="darkmetal", cap_end="darkmetal")

    def wring(dx, r):
        return [(x + dx, axle + r * math.sin(a), z + r * math.cos(a))
                for a in (math.pi / sides + 2 * math.pi * k / sides for k in range(sides))]

    hw = width * 0.5
    p.loft([wring(-hw, wheel * 0.62), wring(-hw * 0.6, wheel), wring(hw * 0.6, wheel), wring(hw, wheel * 0.62)],
           "tire", cap_start="metal", cap_end="metal")
    if door:
        dx, length, chord = door  # a flat panel beside the strut, on the side ``dx`` points to
        p.box((x + dx, y + 0.3 - length * 0.5, z), (0.05, length, chord), "body")
    return p


def build_edge(name, stubs):
    """Broken-edge cap: a short scorched stub per side ending in a jagged, glowing fan."""
    p = Part(name)
    for base, outer, jag, axis in stubs:
        rim = []
        for i, v in enumerate(outer):
            off = jag if i % 2 == 0 else -jag * 0.4
            rim.append(tuple(v[k] + (off if k == axis else 0.0) for k in range(3)))
        tip = [sum(v[k] for v in outer) / len(outer) for k in range(3)]
        tip[axis] += jag * 1.2
        p.loft([base, rim], "char", cap_start="char", fan_end=(tuple(tip), lambda i: "ember" if i % 2 == 0 else "char"))
    return p


def belly_missile(p, pylon_y, my, mz0=-0.65, mz1=1.30):
    """The missile slung under the fuselage (it launches from the body's launch point, so it survives a lost wing)."""
    p.box((0.0, pylon_y, (mz0 + mz1) * 0.5 + 0.025), (0.12, 0.22, 1.0), "darkmetal")
    p.loft([ring(mz0, 0.10, 8, my), ring(mz0 + 0.1, 0.15, 8, my), ring(mz1, 0.15, 8, my), ring(mz1 + 0.18, 0.12, 8, my)],
           lambda b, s: "white" if b < 2 else "sun", cap_start="darkmetal", fan_end=((0, my, mz1 + 0.34), "sun"))
    for a in range(4):
        ang = math.pi / 4 + a * math.pi / 2
        dx, dy = math.cos(ang), math.sin(ang)
        fin = []
        for r, z0, z1 in ((0.12, mz0 + 0.03, mz0 + 0.40), (0.34, mz0 + 0.03, mz0 + 0.20)):
            fin.append([(dx * r - dy * 0.02, my + dy * r + dx * 0.02, z0), (dx * r - dy * 0.02, my + dy * r + dx * 0.02, z1),
                        (dx * r + dy * 0.02, my + dy * r - dx * 0.02, z1), (dx * r + dy * 0.02, my + dy * r - dx * 0.02, z0)])
        p.loft(fin, "metal", cap_start="metal", cap_end="metal")


def roundel(p, center, normal, outer=0.22, inner=0.12):
    """White ring with a team-coloured centre, floated just off a flat panel."""
    n = Vector(normal).normalized()
    c = Vector(center)
    p.disc(c + n * 0.012, normal, outer, 10, "white")
    p.disc(c + n * 0.020, normal, inner, 10, "team")


# ---------------------------------------------------------------------------------------------- materials


def make_material(name, color, image=None, alpha=1.0, cull=True, tint=None):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if image is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.interpolation = "Closest"
        out = tex.outputs["Color"]
        if tint is not None:
            mul = nt.nodes.new("ShaderNodeMix")
            mul.data_type = "RGBA"
            mul.blend_type = "MULTIPLY"
            mul.inputs["Factor"].default_value = 1.0
            nt.links.new(out, mul.inputs[6])
            mul.inputs[7].default_value = (*tint, 1.0)
            out = mul.outputs[2]
        nt.links.new(out, bsdf.inputs["Base Color"])
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED"
        except Exception:
            m.blend_method = "BLEND"
    m.use_backface_culling = cull
    m.diffuse_color = (*color, alpha)
    return m


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_linear(h):
    return tuple(srgb_to_linear(v / 255.0) for v in hx(h))


class Model:
    """A fresh scene for one class: writes the shared palette and makes the shared materials."""

    def __init__(self, name, team_hex="2F7DF6"):
        self.name = name
        write_palette()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1.0
        self.collection = bpy.data.collections.new(name)
        scene.collection.children.link(self.collection)

        palette = bpy.data.images.load(PALETTE_PATH)
        self.materials = [
            make_material("Plane_Paint", (1, 1, 1), palette),
            make_material("Plane_Team", (1, 1, 1), palette, tint=hex_linear(team_hex)),
            make_material("Plane_Ink", hex_linear(INK)),
        ]
        self.blur = make_material("Plane_PropBlur", hex_linear("2C3242"), alpha=0.22, cull=False)
        self.objects = []  # meshes, in export order
        self.markers = []

    def add(self, parts):
        self.objects += [part.finish(self.collection, self.materials) for part in parts]

    def add_blur(self, part):
        """A translucent, outline-free part on the blur material (e.g. a propeller disc)."""
        part.hull_faces = []
        self.objects.append(part.finish(self.collection, [self.blur]))

    def mark(self, name, unity_pos):
        self.markers.append(marker(self.collection, name, unity_pos))

    def export(self, fbx_path, blend_path):
        tag = "[" + self.name.lower() + "]"
        tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in self.objects}
        print(tag, "triangles per object:", tris)
        print(tag, "total triangles:", sum(tris.values()),
              " without outline:", sum(sum(1 for p in o.data.polygons if p.material_index != 2) for o in self.objects))

        os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
        for o in bpy.context.selected_objects:
            o.select_set(False)
        for o in self.objects + self.markers:
            o.select_set(True)
        bpy.ops.export_scene.fbx(
            filepath=fbx_path, use_selection=True, object_types={"MESH", "EMPTY"},
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True,
            mesh_smooth_type="FACE", use_mesh_modifiers=True, use_tspace=False,
            add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
        print(tag, "exported", fbx_path)

        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
        print(tag, "saved", blend_path)


# ---------------------------------------------------------------------------------------------- preview renders


def render_previews(out_dir, shots):
    """
    ``shots``: (file name, unity camera position, unity target, ortho scale or None[, parts shown broken off]).
    A broken part hides every object whose name starts with it.
    The broken-edge caps only show for the parts a shot breaks off, as in the game (their hulls poke through the skin).
    """
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    try:
        world.use_nodes = True
    except Exception:
        pass
    world.node_tree.nodes["Background"].inputs[0].default_value = (*hex_linear("BFE6FF"), 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    try:
        scene.view_settings.view_transform = "Standard"
    except Exception:
        pass

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4.5
    # key light from the upper left, a little in front (style guide), in Unity terms
    sun.rotation_euler = to_blender((1.0, -1.6, -0.9)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(sun)

    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = 50
    cam.data.clip_end = 10000.0  # hero environment pieces are hundreds of metres across
    scene.collection.objects.link(cam)
    scene.camera = cam

    os.makedirs(out_dir, exist_ok=True)
    for name, unity_pos, unity_target, ortho, *rest in shots:
        broken = set(rest[0]) if rest else set()
        for o in bpy.data.objects:
            if o.type == "MESH" and o.name not in ("Sun", "Cam"):
                off = any(o.name.startswith(b) for b in broken)  # a part and what it carries (e.g. WingLeftProp)
                o.hide_render = off or (o.name.startswith("Edge") and o.name[4:] not in broken)
        pos, target = to_blender(unity_pos), to_blender(unity_target)
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        if ortho:
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = ortho
        else:
            cam.data.type = "PERSP"
        scene.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)


def preview_arg(argv):
    """The --preview <dir> argument after Blender's ``--``, or None."""
    return argv[argv.index("--preview") + 1] if "--preview" in argv else None
