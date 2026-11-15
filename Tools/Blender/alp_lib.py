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
    # ---- v2 (détail) : seconds tons de couverture, de feuillage et d'écorce
    ("roof_tile_b", (0.36, 0.11, 0.07), (0.70, 0.28, 0.15)),
    ("roof_slate_b", (0.20, 0.22, 0.26), (0.44, 0.47, 0.52)),
    ("lauze_b", (0.30, 0.28, 0.25), (0.56, 0.53, 0.48)),
    ("roof_brown_b", (0.22, 0.11, 0.07), (0.46, 0.25, 0.16)),
    ("roof_red_b", (0.44, 0.10, 0.07), (0.74, 0.24, 0.15)),
    ("spruce_tip", (0.07, 0.24, 0.18), (0.22, 0.50, 0.36)),
    ("larch_tip", (0.30, 0.44, 0.12), (0.66, 0.80, 0.32)),
    ("moss", (0.20, 0.30, 0.08), (0.46, 0.58, 0.20)),
    ("bark_grey", (0.24, 0.21, 0.19), (0.48, 0.43, 0.38)),
    ("copper_b", (0.14, 0.36, 0.31), (0.36, 0.64, 0.55)),
    ("roof_metal_b", (0.30, 0.34, 0.38), (0.58, 0.62, 0.66)),
    ("arolla_tip", (0.10, 0.28, 0.20), (0.28, 0.56, 0.40)),
]
assert len(PALETTE) <= NCOL
NAMES = [p[0] for p in PALETTE]
CI = {n: i for i, n in enumerate(NAMES)}
# colonnes émissives (image d'émission) : couleur d'émission sRGB
EMISSIVE = {"window_lit": (1.0, 0.74, 0.36), "lamp": (1.0, 0.92, 0.72), "neon_red": (1.0, 0.18, 0.10),
            "neon_green": (0.15, 1.0, 0.35), "neon_blue": (0.2, 0.55, 1.0)}
ITEMS = {}
# Objets repris par une autre session (végétation au sol) : ni reconstruits, ni réexportés ; leur entrée de manifeste
# est recopiée telle quelle depuis le fichier existant.
FROZEN = {"grass_tall", "fern", "flowers_gentian", "flowers_edelweiss", "flowers_meadow", "bush_rhodo", "juniper",
          "reeds", "lily_pads"}
BUILDING_CATS = ("chalet", "townhouse", "landmark", "farm", "ski")


def reg(d):
    """Enregistre une fiche (les ids repris par une autre session, FROZEN, ne sont jamais enregistrés)."""
    if d["id"] in FROZEN:
        return d
    ITEMS[d["id"]] = d
    return d


def lod(g, *values):
    return values[min(g.lod, len(values) - 1)]


def lv(g, *values):
    """Valeur selon le niveau exporté : 0 = détaillé (hd), 1 = moyen, 2 = lointain."""
    return values[min(g.level, len(values) - 1)]


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

    def __init__(self, lod=0, seed=0, hd=False, level=None):
        self.bm = bmesh.new()
        self.col = self.bm.faces.layers.int.new("col")
        self.part = self.bm.faces.layers.int.new("part")   # pièce (primitive) d'origine, pour le contrôle de z-fighting
        self.np = 0
        self.lod = lod                    # palier « historique » des fiches (0 complet, 1 réduit, 2 volumes)
        self.hd = hd                      # niveau détaillé v2 (LOD0 exporté) : lod = 0 et hd = True
        self.level = lod if level is None else level   # niveau exporté : 0 hd, 1 moyen, 2 lointain
        self.seed = seed
        self.rng = random.Random(seed)
        self.drng = random.Random(seed * 7919 + 13)    # hasard réservé aux détails hd (ne décale pas rng)

    def D(self, a=0.0, b=1.0):
        return a + (b - a) * self.drng.random()

    def h(self, *key):
        """Nombre stable dans [0, 1) pour une clé (identique à tous les niveaux)."""
        k = zlib.crc32(repr((self.seed,) + tuple(round(float(v), 3) for v in key)).encode())
        return (k % 100003) / 100003.0

    # --- utilitaires
    def R(self, a=0.0, b=1.0):
        return a + (b - a) * self.rng.random()

    def chance(self, p, *key):
        """Tirage stable d'un niveau de détail à l'autre (dépend de la clé, pas de l'ordre des appels)."""
        k = zlib.crc32(repr((self.seed,) + tuple(round(float(v), 2) for v in key)).encode())
        return (k % 1000) / 1000.0 < p

    def paint(self, faces, color):
        ci = CI[color]
        newp = 0
        for f in faces:
            f[self.col] = ci
            if f[self.part] == 0:
                if not newp:
                    self.np += 1
                    newp = self.np
                f[self.part] = newp
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
        return self.paint([f], color)

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

    def frustum(self, c, r0, r1, h, n, color, rot=0.0, cap0=True, cap1=True, sx=1.0, sy=1.0, auto=True):
        """Tronc de cône vertical (r1 = 0 : cône pointu). Niveau hd : les sections rondes (5 à 10 côtés)
        sont doublées (auto = False pour garder n)."""
        if self.hd and auto and 5 <= n <= 10:
            n *= 2
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

    def ico(self, c, r, subdiv, color, scale=(1, 1, 1), auto=True):
        if self.hd and auto and subdiv < 3:
            subdiv += 1
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

    def caps(self, faces):
        """Faces de bout (polygones à plus de 4 côtés) d'un tronc de cône ou d'un tube."""
        return [f for f in faces if len(f.verts) > 4]

    def limb(self, p0, p1, r0, r1, n, color, cap0=False, cap1=True, rot=None, auto=True):
        """Branche / tube : tronc de cône de p0 à p1 (n = 4 et rot = pi/4 : section carrée)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        faces = self.frustum((0, 0, 0), r0, r1, d.length, n, color, rot=self.R(0, 6.28) if rot is None else rot,
                             cap0=cap0, cap1=cap1, auto=auto)
        q = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        self.transform(faces, Matrix.Translation(p0) @ q)
        return faces

    def tube(self, pts, radii, n, color, cap_start=False, cap_end=True):
        """Tube à section ronde le long d'une polyligne (anneaux reliés) ; rayon nul en bout = pointe."""
        pts = [Vector(p) for p in pts]
        bm = self.bm
        rings = []
        px = None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if px is None:
                ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
                x = t.cross(ref).normalized()
            else:
                x = (px - t * px.dot(t))
                x = x.normalized() if x.length > 1e-5 else t.orthogonal().normalized()
            y = t.cross(x)
            px = x
            if radii[i] <= 1e-4:
                rings.append([bm.verts.new(p)])
            else:
                rings.append([bm.verts.new(p + (x * math.cos(6.2832 * k / n) + y * math.sin(6.2832 * k / n)) * radii[i])
                              for k in range(n)])
        faces = []
        for a, b in zip(rings, rings[1:]):
            if len(a) == 1 and len(b) == 1:
                continue
            for k in range(n):
                j = (k + 1) % n
                if len(b) == 1:
                    faces.append(bm.faces.new((a[k], a[j], b[0])))
                elif len(a) == 1:
                    faces.append(bm.faces.new((a[0], b[j], b[k])))
                else:
                    faces.append(bm.faces.new((a[k], a[j], b[j], b[k])))
        if cap_start and len(rings[0]) > 2:
            faces.append(bm.faces.new(list(reversed(rings[0]))))
        if cap_end and len(rings[-1]) > 2:
            faces.append(bm.faces.new(rings[-1]))
        return self.paint(faces, color)

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
def _pairs_bm(bm, play, tol=0.01, skip_below=0.0, min_area=1e-4, cell=1.0):
    """Paires de faces (triangles) de pièces différentes, de même orientation, à moins de « tol » l'une de l'autre et
    dont les projections se recouvrent : (face a, face b, normale, distance a, distance b, aire)."""
    groups = {}
    for f in bm.faces:
        n = f.normal
        if n.length < 0.5:
            continue
        zmax = max(v.co.z for v in f.verts)
        if skip_below is not None:
            if zmax < skip_below - 0.02:
                continue
            if n.z < -0.9 and zmax <= skip_below + 0.01:
                continue
        groups.setdefault((round(n.x * 40), round(n.y * 40), round(n.z * 40)), []).append(f)
    out = []
    for key, fs in groups.items():
        if len(fs) < 2:
            continue
        n0 = fs[0].normal.normalized()
        u = n0.orthogonal().normalized()
        v = n0.cross(u)
        data = {}
        for f in fs:
            pts = [q.co for q in f.verts]
            d = sum(n0.dot(q) for q in pts) / len(pts)
            pp = [(u.dot(q), v.dot(q)) for q in pts]
            if _area(pp) < 0:
                pp.reverse()
            xs, ys = [q[0] for q in pp], [q[1] for q in pp]
            data[f] = (d, pp, (min(xs), min(ys), max(xs), max(ys)))
        grid = {}
        for f, (d, pp, bb) in data.items():
            b = math.floor(d / tol)
            for cx in range(math.floor(bb[0] / cell), math.floor(bb[2] / cell) + 1):
                for cy in range(math.floor(bb[1] / cell), math.floor(bb[3] / cell) + 1):
                    grid.setdefault((b, cx, cy), []).append(f)
        seen = set()
        for f, (d, pp, bb) in data.items():
            b = math.floor(d / tol)
            pf = f[play]
            for cx in range(math.floor(bb[0] / cell), math.floor(bb[2] / cell) + 1):
                for cy in range(math.floor(bb[1] / cell), math.floor(bb[3] / cell) + 1):
                    for bj in (b, b + 1):
                        for h in grid.get((bj, cx, cy), ()):
                            if h is f or h[play] == pf:
                                continue
                            k = (f.index, h.index) if f.index < h.index else (h.index, f.index)
                            if k in seen:
                                continue
                            seen.add(k)
                            dh, ph, bh = data[h]
                            if abs(dh - d) >= tol or bh[0] >= bb[2] or bh[2] <= bb[0] or bh[1] >= bb[3] or bh[3] <= bb[1]:
                                continue
                            inter = _clip(pp, ph)
                            if len(inter) >= 3:
                                ar = _area(inter)
                                if ar > min_area:
                                    out.append((f, h, n0, d, dh, ar))
    return out


def resolve_coplanar(geo, tol=0.01, gap=0.022, skip_below=0.0, xlim=None, max_area=2.5, thin=0.35, thin_area=16.0,
                     passes=5):
    """Écarte les recouvrements coplanaires (z-fighting) en déplaçant la plus petite des deux pièces le long de la
    normale commune, de façon à laisser « gap » entre les deux faces (vers l'avant si elle était devant ou au même
    niveau, vers l'arrière si elle était derrière). Ne déplace jamais une pièce de plus de max_area m², ni au-delà de
    |x| = xlim (maisons en rangée). Renvoie le nombre de recouvrements restants."""
    bm = geo.bm
    play = geo.part
    left = 0
    for it in range(passes):
        bm.faces.ensure_lookup_table()
        bm.faces.index_update()
        pairs = _pairs_bm(bm, play, tol, skip_below)
        if not pairs:
            return 0
        area, verts = {}, {}
        for f in bm.faces:
            p = f[play]
            area[p] = area.get(p, 0.0) + f.calc_area()
            verts.setdefault(p, set()).update(f.verts)
        movable = {}
        for p, vs in verts.items():                   # petites pièces, ou pièces minces (moulures, planches, décors)
            lo = [min(v.co[k] for v in vs) for k in range(3)]
            hi = [max(v.co[k] for v in vs) for k in range(3)]
            movable[p] = area[p] <= max_area or (min(hi[k] - lo[k] for k in range(3)) <= thin and area[p] <= thin_area)
        need = {}
        for fa, fb, n0, da, db, ar in pairs:
            for (pm, dm), (po, do) in sorted((((fa[play], da), (fb[play], db)), ((fb[play], db), (fa[play], da))),
                                             key=lambda t: area[t[0][0]]):
                if not movable[pm]:
                    continue
                sep = dm - do
                opts = [(gap - sep), -(gap + sep)] if sep >= -1e-6 else [-(gap + sep), (gap - sep)]
                sh = None
                for cand in opts:                         # vers l'avant d'abord ; vers l'arrière si |x| > xlim
                    mv = n0 * cand
                    if xlim is not None:
                        xs = [v.co.x + mv.x for v in verts[pm]]
                        if max(abs(x) for x in xs) > xlim + 1e-4 >= max(abs(v.co.x) for v in verts[pm]):
                            continue
                    sh = cand
                    break
                if sh is None:
                    continue
                cur = need.setdefault(pm, [])
                for i, (n1, s1) in enumerate(cur):
                    c = n1.dot(n0)
                    if c > 0.99:                          # même sens : la plus forte exigence
                        if abs(sh) > abs(s1):
                            cur[i] = (n0, sh)
                        break
                    if c < -0.99:                         # sens opposé (pièces de même épaisseur qui se croisent) :
                        break                             # un seul déplacement règle les deux faces
                else:
                    cur.append((n0, sh))
                break
        if not need:
            return len(pairs)
        for pm, req in need.items():
            mv = Vector((0, 0, 0))
            for n0, sh in req:
                mv += n0 * sh
            bmesh.ops.translate(bm, verts=list(verts[pm]), vec=mv)
        left = len(pairs)
    bm.faces.index_update()
    return len(_pairs_bm(bm, play, tol, skip_below))


def finish(geo, name, col, grad_h=None, base=0.0, resolve=None):
    """Objet final : triangles, UV (colonne de palette, hauteur relative), matériau AP_Palette, facettes à plat.
    resolve : paramètres de resolve_coplanar (None : pas de correction automatique du z-fighting)."""
    bm = geo.bm
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    geo.left = resolve_coplanar(geo, **resolve) if resolve is not None else -1
    if resolve is not None and resolve.get("xlim") is not None:   # rangée : rien au-delà de |x| = w/2
        xl = resolve["xlim"]
        for v in bm.verts:
            if abs(v.co.x) > xl:
                v.co.x = math.copysign(xl, v.co.x)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
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
    plan = {3: [(0, True), (0, False), (2, False)], 2: [(0, True), (0, False), (1, False)],
            1: [(0, True), (0, False)]}[lods]
    if it.get("flat_lods"):                      # objets sans niveau hd distinct (herbes) : paliers historiques
        plan = [(k, False) for k in range(lods)]
    for l, (old, hd) in enumerate(plan):
        g = Geo(old, seed_of(it), hd, l)
        it["build"](g)
        zmax = max(v.co.z for v in g.bm.verts)
        zmin = min(v.co.z for v in g.bm.verts)
        base = it.get("base", zmin if zmax <= 0.01 else 0.0)
        gh = grad
        if gh is None and it.get("cat") in BUILDING_CATS:
            gh = min(max(zmax, 1.0), 16.0)
        rs = None
        if l < 2 and not it.get("no_resolve"):
            rs = dict(skip_below=None if it.get("base", 0.0) < -0.5 or "hanging" in it.get("tags", []) else 0.0,
                      xlim=it["fp"][0] / 2 if it.get("cat") == "townhouse" else None)
        _, n = finish(g, "%s_LOD%d" % (it["file"], l), col, gh, base, rs)
        it.setdefault("_left", {})[l] = g.left
        tris.append(n)
    for n, ob in enumerate(sorted(col.objects, key=lambda o: o.name)):
        ob.hide_render = ob.hide_viewport = n > 0
    it["_tris"] = tris
    return tris


def build_all(ids=None):
    out = {}
    for iid in (ids or list(ITEMS)):
        if iid in FROZEN:
            continue
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
    ids = [i for i in ids if i not in FROZEN]
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
    path = os.path.join(folder, "AlpPack_manifest.json")
    old = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            old = {e["id"]: e for e in json.load(f).get("items", [])}
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
    # entrées des autres sessions (plantes AP_Flora, ids repris) : conservées telles quelles
    mine = {e["id"] for e in items}
    for iid, e in old.items():
        if iid not in mine and (iid not in ITEMS or e.get("material") == "AP_Flora"):
            items.append(e)
    os.makedirs(folder, exist_ok=True)
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


def closeup(ids, name, dist=11.0, eye=1.7, look=2.6, az=-78, lens=26, gap=1.5, res=(1600, 900), focus=None):
    """Gros plan à hauteur d'homme : caméra à « eye » m du sol, à « dist » m devant les objets présentés."""
    os.makedirs(RENDERS, exist_ok=True)
    show(ids, gap, 400.0)
    if not bpy.data.objects.get("AP_Camera"):
        studio()
    cam = bpy.context.scene.camera
    lo, hi = shown_bounds()
    cx = (lo.x + hi.x) / 2 if focus is None else focus
    a = math.radians(az)
    target = Vector((cx, lo.y + 1.0, look))
    cam.location = Vector((cx + math.cos(a) * dist, lo.y + math.sin(a) * dist, eye))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    return render(os.path.join(RENDERS, name + ".png"), res)


# ----------------------------------------------------------------- contrôle de z-fighting
def _clip(subject, clip):
    """Intersection de deux polygones convexes 2D orientés dans le sens direct (Sutherland-Hodgman)."""
    out = subject
    n = len(clip)
    for i in range(n):
        if not out:
            break
        ax, ay = clip[i]
        bx, by = clip[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        inp, out = out, []
        m = len(inp)
        for j in range(m):
            px, py = inp[j]
            qx, qy = inp[(j + 1) % m]
            sp = ex * (py - ay) - ey * (px - ax)
            sq = ex * (qy - ay) - ey * (qx - ax)
            if sp >= 0:
                out.append((px, py))
                if sq < 0:
                    t = sp / (sp - sq)
                    out.append((px + (qx - px) * t, py + (qy - py) * t))
            elif sq >= 0:
                t = sp / (sp - sq)
                out.append((px + (qx - px) * t, py + (qy - py) * t))
    return out


def _area(p):
    a = 0.0
    for i in range(len(p)):
        x0, y0 = p[i]
        x1, y1 = p[(i + 1) % len(p)]
        a += x0 * y1 - x1 * y0
    return a * 0.5


def coplanar_report(obj, tol=0.01, min_area=1e-4, skip_below=0.0, cell=1.0, detail=12):
    """Faces de même orientation, de pièces différentes, dont les plans sont à moins de « tol » (m) et dont les
    projections se recouvrent sur plus de « min_area » (m²) : risque de z-fighting.
    Les faces tournées vers le bas posées au sol, et les faces entièrement enterrées (sous skip_below, None : aucune)
    sont ignorées. Renvoie {'pairs', 'area', 'items'} (items : les plus grands recouvrements, avec couleurs et position)."""
    me = obj.data
    me.calc_loop_triangles()
    verts = [v.co.copy() for v in me.vertices]
    pa = me.attributes.get("part")
    ca = me.attributes.get("col")
    parts = [d.value for d in pa.data] if pa else None
    cols = [d.value for d in ca.data] if ca else [0] * len(me.polygons)
    if parts is None:                                  # repli : îlots connexes
        par = list(range(len(verts)))

        def find(x):
            while par[x] != x:
                par[x] = par[par[x]]
                x = par[x]
            return x
        for p in me.polygons:
            vs = list(p.vertices)
            for v in vs[1:]:
                a, b = find(vs[0]), find(v)
                if a != b:
                    par[a] = b
        parts = [find(p.vertices[0]) for p in me.polygons]
    groups = {}
    for p in me.polygons:
        n = p.normal
        if n.length < 0.5:
            continue
        pts = [verts[i] for i in p.vertices]
        zmax = max(q.z for q in pts)
        if skip_below is not None:
            if zmax < skip_below - 0.02:
                continue
            if n.z < -0.9 and zmax <= skip_below + 0.01:
                continue
        key = (round(n.x * 40), round(n.y * 40), round(n.z * 40))
        groups.setdefault(key, []).append(p.index)
    found = []
    for key, idx in groups.items():
        if len(idx) < 2:
            continue
        n0 = me.polygons[idx[0]].normal.normalized()
        u = n0.orthogonal().normalized()
        v = n0.cross(u)
        data = {}
        for i in idx:
            p = me.polygons[i]
            pts = [verts[k] for k in p.vertices]
            d = sum(n0.dot(q) for q in pts) / len(pts)
            pp = [(u.dot(q), v.dot(q)) for q in pts]
            if _area(pp) < 0:
                pp.reverse()
            xs, ys = [q[0] for q in pp], [q[1] for q in pp]
            data[i] = (d, pp, (min(xs), min(ys), max(xs), max(ys)))
        grid = {}
        for i, (d, pp, bb) in data.items():
            b = math.floor(d / tol)
            for cx in range(math.floor(bb[0] / cell), math.floor(bb[2] / cell) + 1):
                for cy in range(math.floor(bb[1] / cell), math.floor(bb[3] / cell) + 1):
                    grid.setdefault((b, cx, cy), []).append(i)
        seen = set()
        for i, (d, pp, bb) in data.items():
            b = math.floor(d / tol)
            for cx in range(math.floor(bb[0] / cell), math.floor(bb[2] / cell) + 1):
                for cy in range(math.floor(bb[1] / cell), math.floor(bb[3] / cell) + 1):
                    for bj in (b, b + 1):
                        for j in grid.get((bj, cx, cy), ()):
                            if j == i or parts[i] == parts[j]:
                                continue
                            k = (i, j) if i < j else (j, i)
                            if k in seen:
                                continue
                            seen.add(k)
                            dj, pj, bj_ = data[j]
                            if abs(dj - d) >= tol:
                                continue
                            if bj_[0] >= bb[2] or bj_[2] <= bb[0] or bj_[1] >= bb[3] or bj_[3] <= bb[1]:
                                continue
                            inter = _clip(pp, pj)
                            if len(inter) < 3:
                                continue
                            ar = _area(inter)
                            if ar > min_area:
                                c = me.polygons[i].center
                                found.append((ar, abs(dj - d), NAMES[cols[i]] if cols[i] < len(NAMES) else cols[i],
                                              NAMES[cols[j]] if cols[j] < len(NAMES) else cols[j], tuple(round(x, 2) for x in c),
                                              tuple(round(x, 2) for x in n0)))
    found.sort(key=lambda t: -t[0])
    return {"pairs": len(found), "area": round(sum(f[0] for f in found), 4), "items": found[:detail]}


def check(ids=None, lods=(0, 1), tol=0.01, verbose=True, detail=6):
    """Contrôle de z-fighting des objets construits ; renvoie {id: [paires par niveau]}."""
    res = {}
    for iid in (ids or list(ITEMS)):
        if iid in FROZEN:
            continue
        col = bpy.data.collections.get("AP_" + iid)
        if not col:
            continue
        it = ITEMS[iid]
        skip = None if it.get("base", 0.0) < -0.5 or "hanging" in it.get("tags", []) else 0.0
        obs = sorted(col.objects, key=lambda o: o.name)
        row = []
        for l in lods:
            if l >= len(obs):
                continue
            r = coplanar_report(obs[l], tol, skip_below=skip, detail=detail)
            row.append(r["pairs"])
            if verbose and r["pairs"]:
                print("%s LOD%d : %d paires, %.3f m2" % (iid, l, r["pairs"], r["area"]))
                for f in r["items"]:
                    print("   %.4f m2  dz=%.3f  %s / %s  @%s  n=%s" % f)
        res[iid] = row
    return res


def run_queue(ids=None, budget=38.0, do_export=False):
    """Construit (et exporte) une file d'objets par tranches de « budget » secondes (appels courts via MCP).
    Renvoie (restants, terminés dans cet appel)."""
    import time
    ns = bpy.app.driver_namespace
    if ids is not None:
        ns["ap_queue"] = [i for i in ids if i not in FROZEN]
        ns["ap_done"] = []
    q = ns.get("ap_queue", [])
    t0 = time.time()
    done = []
    while q and time.time() - t0 < budget:
        iid = q.pop(0)
        build(iid)
        if do_export:
            export(iid)
        done.append(iid)
    ns["ap_done"] = ns.get("ap_done", []) + done
    return len(q), done
