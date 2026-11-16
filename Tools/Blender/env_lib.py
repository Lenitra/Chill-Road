"""Pack d'environnement NoHesi (nature, mobilier routier, bâtiments), même direction artistique que les voitures.

Low poly à facettes, une palette de dégradés verticaux (bas sombre -> haut clair) partagée par tout le pack.
Chaque objet est construit directement en bmesh avec une couleur de palette par face ; la finition écrit les UV
(colonne de palette, hauteur relative dans l'objet) et un seul matériau, EV_Palette.
Niveaux de détail : chaque fiche est construite pour lod = 0, 1, 2 -> objets <Fichier>_LOD0..2, dont l'import
Unity fait un LODGroup (utilisé par le rendu instancié de la végétation).
Axes Blender : Z haut, sol à z = 0, origine au pied de l'objet ; « avant » des objets orientés = -Y
(devient +Z dans Unity).
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix

SCENE = "EnvPack"
ROOT = "EV_Items"

# colonnes de la palette : (nom, bas sRGB, haut sRGB)
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
    # mobilier routier
    ("metal", (0.42, 0.44, 0.47), (0.78, 0.80, 0.83)),
    ("metal_dark", (0.10, 0.11, 0.12), (0.26, 0.27, 0.29)),
    ("sign_red", (0.52, 0.04, 0.04), (0.86, 0.12, 0.10)),
    ("sign_white", (0.80, 0.80, 0.78), (0.98, 0.98, 0.96)),
    ("sign_blue", (0.04, 0.16, 0.45), (0.12, 0.34, 0.78)),
    ("black", (0.03, 0.03, 0.035), (0.14, 0.14, 0.16)),
    ("lamp", (0.85, 0.82, 0.70), (1.0, 0.98, 0.90)),
    ("orange", (0.80, 0.30, 0.04), (1.0, 0.55, 0.14)),
    ("yellow", (0.80, 0.60, 0.06), (1.0, 0.84, 0.25)),
    # bâtiments
    ("wall", (0.70, 0.66, 0.58), (0.96, 0.93, 0.86)),
    ("wall_stone", (0.48, 0.44, 0.38), (0.78, 0.73, 0.64)),
    ("roof_tile", (0.42, 0.14, 0.08), (0.78, 0.34, 0.18)),
    ("roof_slate", (0.16, 0.18, 0.21), (0.38, 0.41, 0.45)),
    ("window", (0.10, 0.18, 0.24), (0.32, 0.52, 0.62)),
    ("shutter", (0.12, 0.30, 0.22), (0.30, 0.56, 0.42)),
    ("shutter_blue", (0.14, 0.24, 0.40), (0.34, 0.50, 0.70)),
    ("door", (0.28, 0.17, 0.10), (0.52, 0.34, 0.20)),
    ("barn", (0.40, 0.09, 0.06), (0.70, 0.20, 0.12)),
]
NAMES = [p[0] for p in PALETTE]
CI = {n: i for i, n in enumerate(NAMES)}
NCOL = len(PALETTE)
PAL_W = 8
ITEMS = {}
SHARP_ANGLE = 40.0


def reg(d):
    ITEMS[d["id"]] = d
    return d


# ----------------------------------------------------------------- scène / collections
def use_scene():
    """Crée la scène du pack et l'affiche (à appeler seul : le contexte suit au prochain appel)."""
    scn = bpy.data.scenes.get(SCENE) or bpy.data.scenes.new(SCENE)
    for win in bpy.context.window_manager.windows:
        if win.scene != scn:
            win.scene = scn
    return scn


def check_scene():
    if bpy.context.scene.name != SCENE:
        raise RuntimeError("scène active : %s (appeler use_scene() d'abord)" % bpy.context.scene.name)
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
def palette_image(save_path=None):
    w, h = PAL_W * NCOL, 64
    img = bpy.data.images.get("EV_Palette")
    if img is None or tuple(img.size) != (w, h):
        if img:
            bpy.data.images.remove(img)
        img = bpy.data.images.new("EV_Palette", w, h, alpha=False)
    px = []
    for y in range(h):
        t = y / (h - 1)
        for x in range(w):
            _, lo, hi = PALETTE[x // PAL_W]
            px += [lo[k] * (1 - t) + hi[k] * t for k in range(3)] + [1.0]
    img.pixels = px
    if save_path:
        img.filepath_raw = save_path
        img.file_format = 'PNG'
        img.save()
    return img


def palette_material():
    m = bpy.data.materials.get("EV_Palette")
    if m is None:
        m = bpy.data.materials.new("EV_Palette")
        m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = 'Linear'
    tex.extension = 'EXTEND'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    return m


# ----------------------------------------------------------------- géométrie colorée
class Geo:
    """bmesh + couleur de palette par face (couche entière « col ») ; lod et hasard propres à la fiche."""

    def __init__(self, lod=0, seed=0):
        self.bm = bmesh.new()
        self.col = self.bm.faces.layers.int.new("col")
        self.lod = lod
        self.rng = random.Random(seed)

    # --- utilitaires
    def R(self, a=0.0, b=1.0):
        return a + (b - a) * self.rng.random()

    def paint(self, faces, color):
        for f in faces:
            f[self.col] = CI[color]
        return faces

    def new_faces(self, before):
        return [f for f in self.bm.faces if f not in before]

    def transform(self, faces, m):
        verts = {v for f in faces for v in f.verts}
        bmesh.ops.transform(self.bm, matrix=m, verts=list(verts))

    def jitter(self, faces, amount, keep_bottom=None):
        """Déplace chaque sommet au hasard (bosselures) ; les sommets sous keep_bottom ne bougent pas en z."""
        for v in {v for f in faces for v in f.verts}:
            d = Vector((self.R(-1, 1), self.R(-1, 1), self.R(-1, 1))) * amount
            if keep_bottom is not None and v.co.z <= keep_bottom + 1e-4:
                d.z = 0.0
            v.co += d

    # --- primitives (retournent les faces créées)
    def prism(self, poly, a0, a1, color, axis='y'):
        """Prisme : polygone convexe ou non (liste de points 2D dans le plan perpendiculaire à « axis »),
        extrudé de a0 à a1 le long de l'axe. axis 'y' : points (x, z) ; 'x' : points (y, z)."""
        before = set(self.bm.faces)

        def P(p, a):
            return Vector((p[0], a, p[1])) if axis == 'y' else Vector((a, p[0], p[1]))
        A = [self.bm.verts.new(P(p, a0)) for p in poly]
        B = [self.bm.verts.new(P(p, a1)) for p in poly]
        n = len(poly)
        for k in range(n):
            j = (k + 1) % n
            self.bm.faces.new((A[k], A[j], B[j], B[k]))
        self.bm.faces.new(list(reversed(A)))
        self.bm.faces.new(B)
        faces = self.new_faces(before)
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
        return self.paint(self.new_faces(before), color)

    def ico(self, c, r, subdiv, color, scale=(1, 1, 1)):
        before = set(self.bm.faces)
        m = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
        bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=r, matrix=m)
        return self.paint(self.new_faces(before), color)

    def octa(self, c, r, color, scale=(1, 1, 1)):
        """Octaèdre (8 faces) : fruits, petits éclats."""
        c = Vector(c)
        v = [self.bm.verts.new(c + Vector(d) * r) for d in
             ((scale[0], 0, 0), (0, scale[1], 0), (-scale[0], 0, 0), (0, -scale[1], 0), (0, 0, scale[2]), (0, 0, -scale[2]))]
        faces = []
        for i in range(4):
            j = (i + 1) % 4
            faces.append(self.bm.faces.new((v[i], v[j], v[4])))
            faces.append(self.bm.faces.new((v[j], v[i], v[5])))
        return self.paint(faces, color)

    def box(self, c, size, color, rot=None):
        before = set(self.bm.faces)
        m = Matrix.Translation(Vector(c))
        if rot is not None:
            m = m @ rot.to_4x4()
        m = m @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        return self.paint(self.new_faces(before), color)

    def limb(self, p0, p1, r0, r1, n, color, cap0=False, cap1=True):
        """Branche : tronc de cône de p0 à p1."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        faces = self.frustum((0, 0, 0), r0, r1, d.length, n, color, rot=self.R(0, 6.28), cap0=cap0, cap1=cap1)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        self.transform(faces, Matrix.Translation(p0) @ rot)
        return faces

    def blade(self, base, direction, length, width, color, bend=0.25, segs=2):
        """Brin d'herbe (ou pétale) à deux faces : bande effilée qui se courbe. La face arrière a ses propres
        sommets, décalés d'un millimètre (BMesh refuse deux faces sur les mêmes sommets)."""
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
        return self.paint(self.new_faces(before), color)


# ----------------------------------------------------------------- finition, construction, export
def finish(geo, name, col, height=None):
    """Objet final : triangles, UV (colonne de palette, hauteur relative), matériau EV_Palette, facettes à plat."""
    bm = geo.bm
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    zmin = min(v.co.z for v in bm.verts)
    top = height or max(v.co.z for v in bm.verts)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        u = (f[geo.col] + 0.5) / NCOL
        f.smooth = True                 # style cartoon (2026-11-15) : normales lisses, arêtes vives > SHARP_ANGLE
        f.material_index = 0
        for lp in f.loops:
            lp[uv].uv = (u, min(0.99, max(0.01, (lp.vert.co.z - min(zmin, 0.0)) / max(top, 1e-3))))
    lim = math.radians(SHARP_ANGLE)
    for e in bm.edges:
        e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0) <= lim
    me = bpy.data.meshes.new(name)
    me.materials.append(palette_material())
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    return ob, len(me.polygons)


def build(iid):
    """Construit les 3 niveaux de détail d'une fiche dans la collection EV_<id> ; renvoie les triangles par niveau."""
    check_scene()
    it = ITEMS[iid]
    root = get_col(ROOT)
    col = get_col("EV_" + iid, root)
    clear_col(col)
    tris = []
    lods = it.get("lods", 3)
    k = it.get("scale", 1.0)
    for lod in range(lods):
        g = Geo(lod, it.get("seed", hash(iid) & 0xffff))
        it["build"](g)
        # Échelle attendue par le placement du jeu : « target_h » = hauteur hors sol du modèle d'origine
        # (le niveau 0 la fixe, les autres niveaux suivent)
        if lod == 0 and "target_h" in it:
            k = it["target_h"] / max(max(v.co.z for v in g.bm.verts), 1e-3)
        if abs(k - 1.0) > 1e-4:
            bmesh.ops.scale(g.bm, vec=(k, k, k), verts=g.bm.verts)
        _, n = finish(g, "%s_LOD%d" % (it["file"], lod), col, it.get("height"))
        tris.append(n)
    for n, ob in enumerate(sorted(col.objects, key=lambda o: o.name)):
        ob.hide_render = ob.hide_viewport = n > 0          # aperçu : niveau 0 seul
    it["_scale"] = k
    return tris


def export(iid, folder):
    """FBX : les niveaux <Fichier>_LOD0..2 (le premier seul si l'objet n'a pas de niveaux)."""
    os.makedirs(folder, exist_ok=True)
    it = ITEMS[iid]
    col = bpy.data.collections["EV_" + iid]
    obs = sorted(col.objects, key=lambda o: o.name)
    # la collection des objets est exclue pendant les aperçus : on la réinclut pour pouvoir sélectionner
    root_lc = bpy.data.scenes[SCENE].view_layers[0].layer_collection
    for lc in root_lc.children:
        if lc.name == ROOT:
            lc.exclude = False
            for sub in lc.children:
                sub.exclude = False
    bpy.context.view_layer.update()
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.hide_viewport = False
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    path = os.path.join(folder, it["file"] + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='AUTO', embed_textures=False)
    for k, o in enumerate(obs):
        o.hide_viewport = k > 0
    return path


# ----------------------------------------------------------------- présentation (aperçus)
def show(ids, spacing=3.0, rows=1):
    """Présente des objets (instances de collections) en rangées le long de X."""
    check_scene()
    root_lc = bpy.data.scenes[SCENE].view_layers[0].layer_collection
    for lc in root_lc.children:
        if lc.name == ROOT:
            lc.exclude = True
    col = get_col("EV_Show")
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    per_row = math.ceil(len(ids) / rows)
    x = 0.0
    for i, iid in enumerate(ids):
        r, k = divmod(i, per_row)
        e = bpy.data.objects.new("EV_Show_" + iid, None)
        e.instance_type = 'COLLECTION'
        e.instance_collection = bpy.data.collections["EV_" + iid]
        e.location = ((k - (per_row - 1) / 2) * spacing, r * spacing * 1.5, 0)
        col.objects.link(e)
    return col


def shown_bounds():
    bpy.context.view_layer.update()
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for e in bpy.data.collections["EV_Show"].objects:
        for o in e.instance_collection.all_objects:
            if o.type != 'MESH' or o.hide_render:
                continue
            for c in o.bound_box:
                w = e.matrix_world @ o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
    return lo, hi


def studio():
    """Sol, ciel dégradé, soleil et caméra (même ambiance que les rendus du pack de voitures)."""
    check_scene()
    scn = bpy.context.scene
    col = get_col("EV_Studio")
    clear_col(col)
    w = bpy.data.worlds.get("PK_World")
    if w:
        scn.world = w
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=120)
    me = bpy.data.meshes.new("EV_Floor")
    bm.to_mesh(me)
    bm.free()
    fm = bpy.data.materials.get("EV_Floor") or bpy.data.materials.new("EV_Floor")
    fm.use_nodes = True
    b = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs["Base Color"].default_value = (0.20, 0.30, 0.12, 1)
    b.inputs["Roughness"].default_value = 0.95
    me.materials.append(fm)
    col.objects.link(bpy.data.objects.new("EV_Floor", me))
    ld = bpy.data.lights.get("EV_Sun") or bpy.data.lights.new("EV_Sun", 'SUN')
    ld.energy = 3.6
    ld.angle = math.radians(3)
    ld.color = (1.0, 0.97, 0.92)
    sun = bpy.data.objects.new("EV_Sun", ld)
    col.objects.link(sun)
    sun.rotation_euler = (math.radians(45), 0, math.radians(140))
    cam = bpy.data.objects.new("EV_Camera", bpy.data.cameras.new("EV_Camera"))
    col.objects.link(cam)
    scn.camera = cam
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            pass
    scn.eevee.taa_render_samples = 32
    scn.render.image_settings.file_format = 'PNG'
    try:
        scn.view_settings.view_transform = 'AgX'
    except TypeError:
        pass
    return cam


def frame(az, el, fill=0.8, lens=50):
    """Place la caméra pour que les objets présentés occupent « fill » de l'image."""
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
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
