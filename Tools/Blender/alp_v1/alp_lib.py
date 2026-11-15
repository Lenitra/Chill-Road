"""Pack alpin NoHesi « AlpPack » : bâtiments, décors, infrastructures, nature et animaux d'une ville de montagne.

Même direction artistique que env_lib (low poly à facettes, palette de dégradés verticaux), mais :
- échelle réelle 1:1 en mètres, origine au sol au centre de l'emprise, façade avant = -Y (Unity +Z) ;
- palette à 128 colonnes FIXES (u = (i + 0.5) / 128) : on n'ajoute des couleurs qu'à la fin, sans ré-export ;
- image d'émission de même disposition (fenêtres éclairées, lampes, néons), modulée par la nuit dans Unity ;
- manifeste JSON (catégorie, emprise, collision, lampes...) lu par le générateur de carte.

Usage dans Blender :
    import sys; sys.path.insert(0, r"E:\\ChillRoad\\Tools\\Blender")
    import alp_lib as A, alp_buildings, alp_landmarks, alp_props, alp_infra, alp_nature
    A.use_scene()                    # (appel seul)
    A.build("chalet_a"); A.export("chalet_a")      # un objet
    A.build_all(); A.export_all(); A.write_manifest()   # tout le pack
"""
import bpy, bmesh, math, os, random, json, zlib
from mathutils import Vector, Matrix

SCENE = "AlpPack"
ROOT = "AP_Items"
OUT = r"E:\ChillRoad\Assets\NoHesi\Models\AlpPack"
RENDERS = r"E:\ChillRoad\Tools\Blender\Rendus\AlpPack"
NCOL = 128
PAL_W = 8

# colonnes de la palette : (nom, bas sRGB, haut sRGB). NE JAMAIS réordonner : ajouter à la fin.
PALETTE = [
    ("pine", (0.06, 0.20, 0.19), (0.20, 0.50, 0.42)),
    ("leaf", (0.10, 0.27, 0.08), (0.36, 0.63, 0.20)),
    ("leaf_light", (0.22, 0.40, 0.09), (0.58, 0.76, 0.28)),
    ("grass", (0.12, 0.30, 0.09), (0.44, 0.70, 0.25)),
    ("dry", (0.42, 0.37, 0.16), (0.78, 0.70, 0.36)),
    ("bark", (0.18, 0.11, 0.07), (0.40, 0.27, 0.16)),
    ("dead", (0.26, 0.22, 0.18), (0.55, 0.49, 0.42)),
    ("wood_cut", (0.60, 0.44, 0.26), (0.86, 0.71, 0.48)),
    ("wood", (0.30, 0.20, 0.12), (0.56, 0.40, 0.25)),
    ("rock", (0.28, 0.29, 0.31), (0.60, 0.61, 0.62)),
    ("rock_light", (0.44, 0.43, 0.40), (0.70, 0.69, 0.66)),
    ("poppy", (0.55, 0.05, 0.04), (0.95, 0.20, 0.12)),
    ("center", (0.04, 0.03, 0.03), (0.14, 0.11, 0.08)),
    ("cap", (0.60, 0.14, 0.04), (0.97, 0.42, 0.10)),
    ("stem", (0.74, 0.70, 0.62), (0.97, 0.95, 0.88)),
    ("apple", (0.52, 0.05, 0.05), (0.90, 0.22, 0.14)),
    ("pear", (0.58, 0.52, 0.12), (0.92, 0.85, 0.35)),
    ("plum", (0.18, 0.07, 0.26), (0.46, 0.25, 0.56)),
    ("metal", (0.42, 0.44, 0.47), (0.78, 0.80, 0.83)),
    ("metal_dark", (0.10, 0.11, 0.12), (0.26, 0.27, 0.29)),
    ("sign_red", (0.52, 0.04, 0.04), (0.86, 0.12, 0.10)),
    ("sign_white", (0.80, 0.80, 0.78), (0.98, 0.98, 0.96)),
    ("sign_blue", (0.04, 0.16, 0.45), (0.12, 0.34, 0.78)),
    ("black", (0.03, 0.03, 0.035), (0.14, 0.14, 0.16)),
    ("lamp", (0.85, 0.82, 0.70), (1.0, 0.98, 0.90)),
    ("orange", (0.80, 0.30, 0.04), (1.0, 0.55, 0.14)),
    ("yellow", (0.80, 0.60, 0.06), (1.0, 0.84, 0.25)),
    ("wall", (0.70, 0.66, 0.58), (0.96, 0.93, 0.86)),
    ("wall_stone", (0.48, 0.44, 0.38), (0.78, 0.73, 0.64)),
    ("roof_tile", (0.42, 0.14, 0.08), (0.78, 0.34, 0.18)),
    ("roof_slate", (0.16, 0.18, 0.21), (0.38, 0.41, 0.45)),
    ("window", (0.10, 0.18, 0.24), (0.32, 0.52, 0.62)),
    ("shutter", (0.12, 0.30, 0.22), (0.30, 0.56, 0.42)),
    ("shutter_blue", (0.14, 0.24, 0.40), (0.34, 0.50, 0.70)),
    ("door", (0.28, 0.17, 0.10), (0.52, 0.34, 0.20)),
    ("barn", (0.40, 0.09, 0.06), (0.70, 0.20, 0.12)),
    # ---- pack alpin
    ("window_lit", (0.17, 0.22, 0.25), (0.46, 0.58, 0.62)),
    ("wood_dark", (0.15, 0.09, 0.055), (0.38, 0.23, 0.13)),
    ("wood_honey", (0.46, 0.28, 0.11), (0.80, 0.54, 0.26)),
    ("wood_grey", (0.30, 0.28, 0.25), (0.60, 0.56, 0.50)),
    ("wood_red", (0.32, 0.11, 0.07), (0.62, 0.26, 0.15)),
    ("plaster_cream", (0.74, 0.68, 0.52), (0.97, 0.92, 0.76)),
    ("plaster_ochre", (0.66, 0.48, 0.20), (0.93, 0.74, 0.38)),
    ("plaster_pink", (0.68, 0.42, 0.38), (0.94, 0.70, 0.64)),
    ("plaster_blue", (0.42, 0.52, 0.60), (0.70, 0.80, 0.88)),
    ("plaster_white", (0.78, 0.78, 0.75), (0.99, 0.99, 0.96)),
    ("plaster_green", (0.48, 0.58, 0.42), (0.76, 0.85, 0.66)),
    ("plaster_salmon", (0.72, 0.45, 0.30), (0.96, 0.72, 0.52)),
    ("plaster_yellow", (0.75, 0.62, 0.25), (0.98, 0.88, 0.48)),
    ("stone", (0.36, 0.35, 0.33), (0.66, 0.64, 0.60)),
    ("stone_dark", (0.22, 0.22, 0.22), (0.44, 0.43, 0.42)),
    ("stone_warm", (0.46, 0.40, 0.32), (0.76, 0.70, 0.58)),
    ("lauze", (0.24, 0.23, 0.22), (0.50, 0.48, 0.45)),
    ("roof_brown", (0.26, 0.14, 0.09), (0.52, 0.30, 0.20)),
    ("roof_red", (0.50, 0.12, 0.08), (0.82, 0.28, 0.18)),
    ("roof_metal", (0.34, 0.38, 0.42), (0.64, 0.68, 0.72)),
    ("copper", (0.18, 0.42, 0.36), (0.42, 0.72, 0.62)),
    ("geranium", (0.65, 0.04, 0.08), (0.98, 0.16, 0.20)),
    ("flower_pink", (0.72, 0.25, 0.45), (0.98, 0.52, 0.70)),
    ("flower_yellow", (0.80, 0.62, 0.05), (1.0, 0.88, 0.25)),
    ("flower_blue", (0.10, 0.14, 0.60), (0.25, 0.35, 0.95)),
    ("flower_white", (0.82, 0.82, 0.78), (1.0, 1.0, 0.96)),
    ("awning_red", (0.60, 0.10, 0.08), (0.90, 0.22, 0.16)),
    ("awning_green", (0.10, 0.36, 0.22), (0.24, 0.60, 0.38)),
    ("awning_cream", (0.80, 0.74, 0.60), (0.98, 0.95, 0.84)),
    ("awning_blue", (0.12, 0.25, 0.52), (0.26, 0.46, 0.80)),
    ("sign_yellow", (0.85, 0.65, 0.02), (1.0, 0.86, 0.10)),
    ("sign_green", (0.05, 0.36, 0.16), (0.12, 0.60, 0.28)),
    ("sign_brown", (0.36, 0.20, 0.10), (0.60, 0.36, 0.20)),
    ("concrete", (0.46, 0.46, 0.45), (0.76, 0.76, 0.74)),
    ("concrete_dark", (0.30, 0.30, 0.30), (0.52, 0.52, 0.51)),
    ("asphalt", (0.10, 0.10, 0.11), (0.24, 0.24, 0.26)),
    ("snow", (0.80, 0.86, 0.94), (1.0, 1.0, 1.0)),
    ("water", (0.10, 0.32, 0.46), (0.30, 0.62, 0.78)),
    ("glass_blue", (0.20, 0.36, 0.48), (0.50, 0.74, 0.86)),
    ("cow_brown", (0.36, 0.18, 0.08), (0.66, 0.40, 0.22)),
    ("cow_white", (0.74, 0.72, 0.66), (0.98, 0.96, 0.92)),
    ("cow_black", (0.05, 0.05, 0.05), (0.20, 0.19, 0.19)),
    ("fur_tan", (0.38, 0.28, 0.16), (0.68, 0.54, 0.36)),
    ("fur_grey", (0.30, 0.29, 0.28), (0.58, 0.56, 0.54)),
    ("horn", (0.50, 0.45, 0.36), (0.82, 0.76, 0.64)),
    ("skin_pink", (0.72, 0.48, 0.46), (0.95, 0.72, 0.70)),
    ("brass", (0.60, 0.44, 0.10), (0.92, 0.76, 0.30)),
    ("red", (0.62, 0.05, 0.05), (0.92, 0.14, 0.12)),
    ("blue", (0.06, 0.20, 0.55), (0.16, 0.40, 0.86)),
    ("green", (0.06, 0.38, 0.14), (0.16, 0.64, 0.28)),
    ("white", (0.82, 0.82, 0.80), (1.0, 1.0, 0.98)),
    ("larch", (0.22, 0.36, 0.10), (0.55, 0.72, 0.26)),
    ("spruce", (0.04, 0.16, 0.14), (0.14, 0.40, 0.32)),
    ("arolla", (0.07, 0.22, 0.16), (0.22, 0.48, 0.34)),
    ("birch_bark", (0.70, 0.70, 0.66), (0.95, 0.95, 0.92)),
    ("leaf_dark", (0.06, 0.20, 0.07), (0.22, 0.48, 0.16)),
    ("leaf_yellow", (0.40, 0.46, 0.08), (0.74, 0.80, 0.26)),
    ("rhodo", (0.70, 0.10, 0.35), (0.96, 0.30, 0.55)),
    ("reed", (0.36, 0.40, 0.14), (0.66, 0.70, 0.30)),
    ("reed_head", (0.28, 0.16, 0.08), (0.48, 0.30, 0.16)),
    ("hay", (0.66, 0.54, 0.20), (0.94, 0.84, 0.44)),
    ("canvas_green", (0.20, 0.36, 0.24), (0.40, 0.60, 0.42)),
    ("neon_red", (0.90, 0.15, 0.10), (1.0, 0.30, 0.20)),
    ("neon_green", (0.10, 0.80, 0.30), (0.30, 1.0, 0.50)),
    ("neon_blue", (0.15, 0.50, 0.95), (0.35, 0.70, 1.0)),
    ("rust", (0.40, 0.18, 0.08), (0.66, 0.34, 0.18)),
    ("dirt", (0.30, 0.22, 0.14), (0.52, 0.40, 0.28)),
    ("rubber", (0.05, 0.05, 0.055), (0.12, 0.12, 0.13)),
]
assert len(PALETTE) <= NCOL
NAMES = [p[0] for p in PALETTE]
CI = {n: i for i, n in enumerate(NAMES)}
# colonnes émissives (image d'émission) : couleur d'émission sRGB
EMISSIVE = {"window_lit": (1.0, 0.74, 0.36), "lamp": (1.0, 0.92, 0.72), "neon_red": (1.0, 0.18, 0.10),
            "neon_green": (0.15, 1.0, 0.35), "neon_blue": (0.2, 0.55, 1.0)}
ITEMS = {}
BUILDING_CATS = ("chalet", "townhouse", "landmark", "farm", "ski")


def reg(d):
    ITEMS[d["id"]] = d
    return d


def lod(g, *values):
    return values[min(g.lod, len(values) - 1)]


# ----------------------------------------------------------------- scène / collections
def use_scene():
    scn = bpy.data.scenes.get(SCENE) or bpy.data.scenes.new(SCENE)
    for win in bpy.context.window_manager.windows:
        if win.scene != scn:
            win.scene = scn
    return scn


def check_scene():
    if bpy.context.scene.name != SCENE:
        use_scene()
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


def get_col(name, parent=None):
    col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    parent = parent or bpy.data.scenes[SCENE].collection
    if col.name not in parent.children.keys():
        parent.children.link(col)
    return col


def clear_col(col):
    for o in list(col.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if isinstance(me, bpy.types.Mesh) and me.users == 0:
            bpy.data.meshes.remove(me)


# ----------------------------------------------------------------- palette
def _image(name, pixel, save_path=None):
    w, h = PAL_W * NCOL, 64
    img = bpy.data.images.get(name)
    if img is None or tuple(img.size) != (w, h):
        if img:
            bpy.data.images.remove(img)
        img = bpy.data.images.new(name, w, h, alpha=False)
    px = []
    for y in range(h):
        t = y / (h - 1)
        row = []
        for c in range(NCOL):
            row += (list(pixel(c, t)) + [1.0]) * PAL_W
        px += row
    img.pixels = px
    if save_path:
        img.filepath_raw = save_path
        img.file_format = 'PNG'
        img.save()
    return img


def palette_image(save_path=None):
    def px(c, t):
        if c >= len(PALETTE):
            return (1.0, 0.0, 1.0)
        _, lo, hi = PALETTE[c]
        return [lo[k] * (1 - t) + hi[k] * t for k in range(3)]
    return _image("AP_Palette", px, save_path)


def emission_image(save_path=None):
    def px(c, t):
        if c < len(PALETTE) and PALETTE[c][0] in EMISSIVE:
            return EMISSIVE[PALETTE[c][0]]
        return (0.0, 0.0, 0.0)
    return _image("AP_Emission", px, save_path)


def palette_material(refresh=False):
    m = bpy.data.materials.get("AP_Palette")
    if m is not None and not refresh:
        return m
    if m is None:
        m = bpy.data.materials.new("AP_Palette")
        m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    texs = [n for n in nt.nodes if n.type == 'TEX_IMAGE']
    while len(texs) < 2:
        texs.append(nt.nodes.new("ShaderNodeTexImage"))
    tex, em = texs[0], texs[1]
    tex.image = palette_image()
    em.image = emission_image()
    for t in (tex, em):
        t.interpolation = 'Linear'
        t.extension = 'EXTEND'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(em.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.8
    return m


def night(strength=4.0):
    """Aperçu de nuit : force d'émission des fenêtres et lampes (0 = jour)."""
    m = palette_material()
    bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs["Emission Strength"].default_value = strength


def save_textures(folder=OUT):
    os.makedirs(folder, exist_ok=True)
    palette_image(os.path.join(folder, "AlpPack_Palette.png"))
    emission_image(os.path.join(folder, "AlpPack_Emission.png"))


# ----------------------------------------------------------------- géométrie colorée
_DIRS = {'+x': Vector((1, 0, 0)), '-x': Vector((-1, 0, 0)), '+y': Vector((0, 1, 0)), '-y': Vector((0, -1, 0)),
         '+z': Vector((0, 0, 1)), '-z': Vector((0, 0, -1))}


class Geo:
    """bmesh + couleur de palette par face ; lod et hasard propres à la fiche."""

    def __init__(self, lod=0, seed=0):
        self.bm = bmesh.new()
        self.col = self.bm.faces.layers.int.new("col")
        self.lod = lod
        self.seed = seed
        self.rng = random.Random(seed)

    # --- utilitaires
    def R(self, a=0.0, b=1.0):
        return a + (b - a) * self.rng.random()

    def chance(self, p, *key):
        """Tirage stable d'un niveau de détail à l'autre (dépend de la clé, pas de l'ordre des appels)."""
        k = zlib.crc32(repr((self.seed,) + tuple(round(float(v), 2) for v in key)).encode())
        return (k % 1000) / 1000.0 < p

    def paint(self, faces, color):
        ci = CI[color]
        for f in faces:
            f[self.col] = ci
        return faces

    def mark(self):
        return set(self.bm.faces)

    def since(self, mark):
        return [f for f in self.bm.faces if f not in mark]

    new_faces = since

    def transform(self, faces, m):
        verts = {v for f in faces for v in f.verts}
        bmesh.ops.transform(self.bm, matrix=m, verts=list(verts))
        return faces

    def move(self, faces, loc=(0, 0, 0), rotz=0.0, scale=None):
        m = Matrix.Translation(Vector(loc)) @ Matrix.Rotation(rotz, 4, 'Z')
        if scale is not None:
            m = m @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
        return self.transform(faces, m)

    def jitter(self, faces, amount, keep_bottom=None):
        for v in {v for f in faces for v in f.verts}:
            d = Vector((self.R(-1, 1), self.R(-1, 1), self.R(-1, 1))) * amount
            if keep_bottom is not None and v.co.z <= keep_bottom + 1e-4:
                d.z = 0.0
            v.co += d

    # --- primitives (retournent les faces créées)
    def face(self, pts, color):
        f = self.bm.faces.new([self.bm.verts.new(Vector(p)) for p in pts])
        f[self.col] = CI[color]
        return [f]

    def quad(self, c, su, sv, facing, color):
        """Rectangle à une face, centré en c, normale « facing » ('-y', '+x', '+z'...). su = largeur, sv = hauteur
        (pour ±z : su le long de X, sv le long de Y)."""
        x, y, z = c
        a, b = su / 2, sv / 2
        if facing == '-y':
            pts = [(x - a, y, z - b), (x + a, y, z - b), (x + a, y, z + b), (x - a, y, z + b)]
        elif facing == '+y':
            pts = [(x + a, y, z - b), (x - a, y, z - b), (x - a, y, z + b), (x + a, y, z + b)]
        elif facing == '-x':
            pts = [(x, y + a, z - b), (x, y - a, z - b), (x, y - a, z + b), (x, y + a, z + b)]
        elif facing == '+x':
            pts = [(x, y - a, z - b), (x, y + a, z - b), (x, y + a, z + b), (x, y - a, z + b)]
        elif facing == '+z':
            pts = [(x - a, y - b, z), (x + a, y - b, z), (x + a, y + b, z), (x - a, y + b, z)]
        else:
            pts = [(x - a, y + b, z), (x + a, y + b, z), (x + a, y - b, z), (x - a, y - b, z)]
        return self.face(pts, color)

    def prism(self, poly, a0, a1, color, axis='y', caps=True):
        """Prisme : polygone (points 2D dans le plan perpendiculaire à « axis ») extrudé de a0 à a1.
        axis 'y' : points (x, z) ; 'x' : points (y, z) ; 'z' : points (x, y)."""
        before = set(self.bm.faces)

        def P(p, a):
            if axis == 'y':
                return Vector((p[0], a, p[1]))
            if axis == 'x':
                return Vector((a, p[0], p[1]))
            return Vector((p[0], p[1], a))
        A = [self.bm.verts.new(P(p, a0)) for p in poly]
        B = [self.bm.verts.new(P(p, a1)) for p in poly]
        n = len(poly)
        for k in range(n):
            j = (k + 1) % n
            self.bm.faces.new((A[k], A[j], B[j], B[k]))
        if caps:
            self.bm.faces.new(list(reversed(A)))
            self.bm.faces.new(B)
        faces = self.since(before)
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self.paint(faces, color)

    def ring(self, c, r, n, rot=0.0, z=0.0, sx=1.0, sy=1.0):
        return [self.bm.verts.new(Vector(c) + Vector((math.cos(rot + 2 * math.pi * k / n) * r * sx,
                                                      math.sin(rot + 2 * math.pi * k / n) * r * sy, z)))
                for k in range(n)]

    def frustum(self, c, r0, r1, h, n, color, rot=0.0, cap0=True, cap1=True, sx=1.0, sy=1.0):
        """Tronc de cône vertical (r1 = 0 : cône pointu)."""
        before = set(self.bm.faces)
        a = self.ring(c, r0, n, rot, 0.0, sx, sy)
        if r1 <= 1e-5:
            apex = self.bm.verts.new(Vector(c) + Vector((0, 0, h)))
            for k in range(n):
                self.bm.faces.new((a[k], a[(k + 1) % n], apex))
        else:
            b = self.ring(c, r1, n, rot, h, sx, sy)
            for k in range(n):
                self.bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
            if cap1:
                self.bm.faces.new(b)
        if cap0:
            self.bm.faces.new(list(reversed(a)))
        return self.paint(self.since(before), color)

    def ico(self, c, r, subdiv, color, scale=(1, 1, 1)):
        before = set(self.bm.faces)
        m = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
        bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=r, matrix=m)
        return self.paint(self.since(before), color)

    def octa(self, c, r, color, scale=(1, 1, 1)):
        c = Vector(c)
        v = [self.bm.verts.new(c + Vector(d) * r) for d in
             ((scale[0], 0, 0), (0, scale[1], 0), (-scale[0], 0, 0), (0, -scale[1], 0), (0, 0, scale[2]), (0, 0, -scale[2]))]
        faces = []
        for i in range(4):
            j = (i + 1) % 4
            faces.append(self.bm.faces.new((v[i], v[j], v[4])))
            faces.append(self.bm.faces.new((v[j], v[i], v[5])))
        return self.paint(faces, color)

    def box(self, c, size, color, rot=None, rotz=None):
        before = set(self.bm.faces)
        m = Matrix.Translation(Vector(c))
        if rot is not None:
            m = m @ rot.to_4x4()
        if rotz is not None:
            m = m @ Matrix.Rotation(rotz, 4, 'Z')
        m = m @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        return self.paint(self.since(before), color)

    def slab(self, c, size, color, open='-z'):
        """Boîte sans la face « open » (celle plaquée contre un mur ou posée au sol) : 10 triangles."""
        faces = self.box(c, size, color)
        d = _DIRS[open]
        for f in faces:
            f.normal_update()
        kill = [f for f in faces if f.normal.dot(d) > 0.9]
        bmesh.ops.delete(self.bm, geom=kill, context='FACES')
        return [f for f in faces if f.is_valid]

    def limb(self, p0, p1, r0, r1, n, color, cap0=False, cap1=True, rot=None):
        """Branche / tube : tronc de cône de p0 à p1 (n = 4 et rot = pi/4 : section carrée)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        faces = self.frustum((0, 0, 0), r0, r1, d.length, n, color, rot=self.R(0, 6.28) if rot is None else rot,
                             cap0=cap0, cap1=cap1)
        q = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        self.transform(faces, Matrix.Translation(p0) @ q)
        return faces

    def beam(self, p0, p1, w, color, h=None):
        """Poutre de section rectangulaire (w × h) de p0 à p1, faces latérales d'aplomb."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        h = h or w
        x = d.normalized()
        up = Vector((0, 0, 1))
        y = up.cross(x)
        if y.length < 1e-4:
            y = Vector((0, 1, 0))
        y.normalize()
        z = x.cross(y)
        m = Matrix((x, y, z)).transposed().to_4x4()
        m.translation = (p0 + p1) / 2
        before = set(self.bm.faces)
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=m @ Matrix.Diagonal((L, w, h, 1.0)))
        return self.paint(self.since(before), color)

    def blade(self, base, direction, length, width, color, bend=0.25, segs=2):
        """Brin d'herbe (ou pétale) à deux faces."""
        before = set(self.bm.faces)
        base, d = Vector(base), Vector(direction).normalized()
        side = d.cross(Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = Vector((1, 0, 0))
        side.normalize()
        out = Vector((d.x, d.y, 0.0))
        normal = side.cross(d).normalized()
        pts = []
        for i in range(segs + 1):
            t = i / segs
            p = base + d * length * t + out * bend * length * t * t
            w = width * (1.0 - t) * 0.5
            pts.append([p - side * w, p + side * w] if i < segs else [p])
        for sgn in (1, -1):
            rows = [[self.bm.verts.new(q - normal * (0.0005 if sgn < 0 else 0.0)) for q in row] for row in pts]
            for A, B in zip(rows, rows[1:]):
                f = (A[0], A[1], B[1], B[0]) if len(B) == 2 else (A[0], A[1], B[0])
                self.bm.faces.new(f if sgn > 0 else tuple(reversed(f)))
        return self.paint(self.since(before), color)

    def card(self, c, w, h, color, rotz=0.0):
        """Panneau vertical à deux faces (feuillage plat, drapeau...), centré en c."""
        c = Vector(c)
        dx = Vector((math.cos(rotz), math.sin(rotz), 0)) * w / 2
        dz = Vector((0, 0, h / 2))
        n = Vector((-math.sin(rotz), math.cos(rotz), 0)) * 0.001
        pts = [c - dx - dz, c + dx - dz, c + dx + dz, c - dx + dz]
        return self.face([p - n for p in pts], color) + self.face([p + n for p in reversed(pts)], color)


# ----------------------------------------------------------------- finition, construction, export
def finish(geo, name, col, grad_h=None, base=0.0):
    """Objet final : triangles, UV (colonne de palette, hauteur relative), matériau AP_Palette, facettes à plat."""
    bm = geo.bm
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    top = max(v.co.z for v in bm.verts)
    H = grad_h or max(top - base, 1e-3)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        u = (f[geo.col] + 0.5) / NCOL
        f.smooth = False
        f.material_index = 0
        for lp in f.loops:
            lp[uv].uv = (u, min(0.99, max(0.01, (lp.vert.co.z - base) / H)))
    me = bpy.data.meshes.new(name)
    me.materials.append(palette_material())
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    return ob, len(me.polygons)


def seed_of(it):
    return it.get("seed", zlib.crc32(it["id"].encode()) & 0xffff)


def build(iid):
    """Construit les niveaux de détail d'une fiche dans la collection AP_<id> ; renvoie les triangles par niveau."""
    check_scene()
    it = ITEMS[iid]
    root = get_col(ROOT)
    col = get_col("AP_" + iid, root)
    clear_col(col)
    tris = []
    lods = it.get("lods", 3)
    grad = it.get("grad_h")
    for l in range(lods):
        g = Geo(l, seed_of(it))
        it["build"](g)
        zmax = max(v.co.z for v in g.bm.verts)
        zmin = min(v.co.z for v in g.bm.verts)
        base = it.get("base", zmin if zmax <= 0.01 else 0.0)
        gh = grad
        if gh is None and it.get("cat") in BUILDING_CATS:
            gh = min(max(zmax, 1.0), 16.0)
        _, n = finish(g, "%s_LOD%d" % (it["file"], l), col, gh, base)
        tris.append(n)
    for n, ob in enumerate(sorted(col.objects, key=lambda o: o.name)):
        ob.hide_render = ob.hide_viewport = n > 0
    it["_tris"] = tris
    return tris


def build_all(ids=None):
    out = {}
    for iid in (ids or list(ITEMS)):
        out[iid] = build(iid)
    return out


def _include_items():
    root_lc = bpy.data.scenes[SCENE].view_layers[0].layer_collection
    for lc in root_lc.children:
        if lc.name == ROOT:
            lc.exclude = False
            for sub in lc.children:
                sub.exclude = False
    bpy.context.view_layer.update()


def export(iid, folder=OUT):
    """FBX : les niveaux <Fichier>_LOD0..2."""
    os.makedirs(folder, exist_ok=True)
    check_scene()
    it = ITEMS[iid]
    col = bpy.data.collections["AP_" + iid]
    obs = sorted(col.objects, key=lambda o: o.name)
    _include_items()
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.hide_viewport = False
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    path = os.path.join(folder, it["file"] + ".fbx")
    import io, contextlib
    with contextlib.redirect_stdout(io.StringIO()):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                                 axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                                 use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False,
                                 path_mode='AUTO', embed_textures=False)
    for k, o in enumerate(obs):
        o.hide_viewport = k > 0
    return path


def export_all(ids=None, folder=OUT):
    ids = ids or [i for i in ITEMS if bpy.data.collections.get("AP_" + i)]
    for iid in ids:
        export(iid, folder)
    return len(ids)


def bounds(iid):
    col = bpy.data.collections["AP_" + iid]
    ob = sorted(col.objects, key=lambda o: o.name)[0]
    xs = [v.co.x for v in ob.data.vertices]
    ys = [v.co.y for v in ob.data.vertices]
    zs = [v.co.z for v in ob.data.vertices]
    return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))


def write_manifest(folder=OUT):
    """Manifeste de tous les objets construits (repère Unity : x largeur, y hauteur, z profondeur)."""
    items = []
    for iid, it in ITEMS.items():
        col = bpy.data.collections.get("AP_" + iid)
        if not col or not col.objects:
            continue
        lo, hi = bounds(iid)
        w, d, h = hi.x - lo.x, hi.y - lo.y, hi.z
        cat = it.get("cat", "street")
        e = {"id": iid, "file": it["file"], "category": cat, "tags": list(it.get("tags", [])),
             "size": [round(w, 2), round(h, 2), round(d, 2)], "ymin": round(lo.z, 2),
             "footprint": [round(v, 2) for v in it.get("fp", (w, d))],
             "lods": len(col.objects),
             "tris": [len(o.data.polygons) for o in sorted(col.objects, key=lambda o: o.name)],
             "collider": it.get("collider", "box" if cat in BUILDING_CATS else "none")}
        if "radius" in it:
            e["radius"] = it["radius"]
        if "lights" in it:
            e["lights"] = [[round(-p[0], 3), round(p[2], 3), round(-p[1], 3)] for p in it["lights"]]
        if "module" in it:
            e["module"] = it["module"]
        if "note" in it:
            e["note"] = it["note"]
        items.append(e)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, "AlpPack_manifest.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"version": 1, "items": items}, f, indent=1, ensure_ascii=False)
    return len(items)


def deliver(ids):
    """Construit, exporte, réécrit le manifeste ; renvoie les triangles par objet."""
    t = build_all(ids)
    save_textures()
    export_all(ids)
    write_manifest()
    return t


# ----------------------------------------------------------------- présentation (aperçus)
def show(ids, gap=1.5, row_w=60.0):
    """Présente des objets (instances de collections) en rangées le long de X, tassés selon leur taille."""
    check_scene()
    root_lc = bpy.data.scenes[SCENE].view_layers[0].layer_collection
    for lc in root_lc.children:
        if lc.name == ROOT:
            lc.exclude = True
    col = get_col("AP_Show")
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    x, y, row_d = 0.0, 0.0, 0.0
    for iid in ids:
        lo, hi = bounds(iid)
        w, d = hi.x - lo.x, hi.y - lo.y
        if x > 0 and x + w > row_w:
            x = 0.0
            y += row_d + gap * 2
            row_d = 0.0
        e = bpy.data.objects.new("AP_Show_" + iid, None)
        e.instance_type = 'COLLECTION'
        e.instance_collection = bpy.data.collections["AP_" + iid]
        e.empty_display_size = 0.2
        e.location = (x - lo.x, y - lo.y, 0)          # façades (-Y) alignées sur le devant de la rangée
        col.objects.link(e)
        x += w + gap
        row_d = max(row_d, d)
    return col


def shown_bounds():
    bpy.context.view_layer.update()
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for e in bpy.data.collections["AP_Show"].objects:
        for o in e.instance_collection.all_objects:
            if o.type != 'MESH' or o.hide_render:
                continue
            for c in o.bound_box:
                w = e.matrix_world @ o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
    lo.z = max(lo.z, 0.0)
    return lo, hi


def studio():
    """Sol, ciel, soleil et caméra."""
    check_scene()
    scn = bpy.context.scene
    col = get_col("AP_Studio")
    clear_col(col)
    w = bpy.data.worlds.get("AP_World") or bpy.data.worlds.new("AP_World")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (0.55, 0.72, 0.92, 1)
    bg.inputs[1].default_value = 1.0
    scn.world = w
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=600)
    me = bpy.data.meshes.new("AP_Floor")
    bm.to_mesh(me)
    bm.free()
    fm = bpy.data.materials.get("AP_Floor") or bpy.data.materials.new("AP_Floor")
    fm.use_nodes = True
    b = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs["Base Color"].default_value = (0.22, 0.33, 0.13, 1)
    b.inputs["Roughness"].default_value = 0.95
    me.materials.append(fm)
    col.objects.link(bpy.data.objects.new("AP_Floor", me))
    ld = bpy.data.lights.get("AP_Sun") or bpy.data.lights.new("AP_Sun", 'SUN')
    ld.energy = 3.6
    ld.angle = math.radians(3)
    ld.color = (1.0, 0.97, 0.92)
    sun = bpy.data.objects.new("AP_Sun", ld)
    col.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    cam = bpy.data.objects.new("AP_Camera", bpy.data.cameras.new("AP_Camera"))
    cam.data.clip_end = 3000
    col.objects.link(cam)
    scn.camera = cam
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            pass
    try:
        scn.eevee.taa_render_samples = 24
    except Exception:
        pass
    scn.render.image_settings.file_format = 'PNG'
    try:
        scn.view_settings.view_transform = 'AgX'
    except TypeError:
        pass
    return cam


def frame(az=-60, el=22, fill=0.9, lens=50):
    """Place la caméra pour que les objets présentés occupent « fill » de l'image (az -90 = face aux façades)."""
    from bpy_extras.object_utils import world_to_camera_view
    scn = bpy.context.scene
    cam = scn.camera
    lo, hi = shown_bounds()
    target = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) * 0.45))
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    dist = (hi - lo).length * 1.5
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    for _ in range(6):
        cam.location = target + d * dist
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        bpy.context.view_layer.update()
        pts = [world_to_camera_view(scn, cam, c) for c in corners]
        span = max(max(p.x for p in pts) - min(p.x for p in pts), max(p.y for p in pts) - min(p.y for p in pts))
        dist *= span / fill
    cam.location = target + d * dist
    return cam


def render(path, res=(1600, 900)):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def sheet(ids, name, az=-62, el=20, gap=1.5, row_w=60.0, res=(1600, 900), fill=0.92):
    """Planche-contact : présente, cadre et rend dans Rendus/AlpPack/<name>.png."""
    os.makedirs(RENDERS, exist_ok=True)
    show(ids, gap, row_w)
    if not bpy.data.objects.get("AP_Camera"):
        studio()
    frame(az, el, fill)
    return render(os.path.join(RENDERS, name + ".png"), res)


def show_groups(groups, gap=2.0, row_w=120.0, group_gap=8.0):
    """Présentation rangée : chaque groupe d'objets commence une nouvelle bande de rangées (du plus proche -Y au fond)."""
    check_scene()
    root_lc = bpy.data.scenes[SCENE].view_layers[0].layer_collection
    for lc in root_lc.children:
        if lc.name == ROOT:
            lc.exclude = True
    col = get_col("AP_Show")
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    y = 0.0
    for ids in groups:
        x, row_d = 0.0, 0.0
        for iid in ids:
            lo, hi = bounds(iid)
            w, d = hi.x - lo.x, hi.y - lo.y
            if x > 0 and x + w > row_w:
                x = 0.0
                y += row_d + gap * 2
                row_d = 0.0
            e = bpy.data.objects.new("AP_Show_" + iid, None)
            e.instance_type = 'COLLECTION'
            e.instance_collection = bpy.data.collections["AP_" + iid]
            e.empty_display_size = 0.2
            e.location = (x - lo.x, y - lo.y, 0)
            col.objects.link(e)
            x += w + gap
            row_d = max(row_d, d)
        y += row_d + group_gap
    return col


def by_cat(*cats):
    return [i for i, it in ITEMS.items() if it.get("cat") in cats]


def showcase():
    """Tout le pack rangé par catégorie (laissé dans la scène à la sauvegarde)."""
    small = by_cat("street", "sign")
    groups = [small, by_cat("rural", "animal"), by_cat("lake") + by_cat("infra"), by_cat("nature"), by_cat("farm") + by_cat("ski"),
              by_cat("chalet"), by_cat("townhouse"), by_cat("landmark")]
    return show_groups(groups)
