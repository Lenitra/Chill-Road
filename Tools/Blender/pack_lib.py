"""Pack de voitures réelles low poly pour le trafic ChillRoad (moteur commun).

Chaque voiture est une fiche (pack_cars.py) : courbes de profil vue de côté / de dessus -> sections de
carrosserie à 10 points (miroir), passages de roue par booléen, décors projetés sur les facettes, roues.
Cuisson : palette de dégradés verticaux partagée par tout le pack (style Polytope), peinture sur un
matériau séparé (teinte par voiture dans Unity), optiques émissives. Export FBX par modèle.
Axes Blender : +X avant, +Y gauche, Z haut, sol à z = 0, origine au milieu de l'empattement.
"""
import bpy, bmesh, math, os, bisect
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
import hc_lib as H

SCENE = "CarPack"
ROOT = "PK_Cars"
FAR = 8.0

# colonnes de la palette : (nom, bas sRGB, haut sRGB)
PALETTE = [
    ("paint", (0.58, 0.59, 0.61), (0.97, 0.97, 0.97)),   # teintée par voiture (_BaseColor)
    ("trim", (0.05, 0.05, 0.055), (0.19, 0.20, 0.21)),
    ("glass", (0.12, 0.26, 0.32), (0.36, 0.63, 0.68)),
    ("dark", (0.02, 0.02, 0.025), (0.07, 0.07, 0.08)),
    ("tire", (0.07, 0.07, 0.08), (0.17, 0.17, 0.19)),
    ("chrome", (0.50, 0.52, 0.55), (0.92, 0.93, 0.95)),
    ("rim", (0.42, 0.44, 0.47), (0.80, 0.81, 0.83)),
    ("lens", (0.50, 0.55, 0.60), (0.86, 0.90, 0.93)),
    ("red", (0.45, 0.05, 0.04), (0.85, 0.16, 0.12)),
    ("amber", (0.85, 0.45, 0.10), (1.0, 0.70, 0.30)),
    ("white", (0.76, 0.77, 0.79), (0.98, 0.98, 0.97)),
    ("black", (0.03, 0.03, 0.035), (0.17, 0.18, 0.20)),
    ("yellow", (0.84, 0.58, 0.06), (1.0, 0.84, 0.24)),
    ("plate", (0.82, 0.82, 0.80), (0.96, 0.96, 0.93)),
    ("blue", (0.05, 0.12, 0.40), (0.22, 0.38, 0.82)),
    ("grey", (0.26, 0.27, 0.29), (0.50, 0.51, 0.53)),
    ("orange", (0.78, 0.30, 0.04), (1.0, 0.56, 0.16)),
    ("green", (0.05, 0.24, 0.10), (0.20, 0.50, 0.24)),
]
NAMES = [p[0] for p in PALETTE]
CI = {n: i for i, n in enumerate(NAMES)}
NCOL = len(PALETTE)
PAL_W = 8
# optiques : (matériau, couleur linéaire, intensité)
EMISSIVE = {
    "led": ("PK_LED", (1.0, 0.92, 0.80), 4.0),
    "tail": ("PK_Tail", (1.0, 0.04, 0.02), 5.0),
    "siren_red": ("PK_SirenRed", (1.0, 0.03, 0.02), 8.0),
    "siren_blue": ("PK_SirenBlue", (0.05, 0.22, 1.0), 8.0),
}
CARS = {}

# Style cartoon (2026-11-15) : carrosserie arrondie par défaut (fiche smooth=False : ancien rendu facetté),
# normales lisses avec arêtes vives au-delà de SHARP_ANGLE, pièces rapportées (boîtes, prismes) chanfreinées,
# roues plus rondes (WHEEL_RES × le nombre de secteurs des rayons). Fiche flat=True : facettes à plat.
DEFAULT_SMOOTH = dict(dx=0.08, sub={2: 2, 3: 2, 4: 2, 5: 2, 6: 2, 7: 2, 8: 3})
SHARP_ANGLE = 42.0
BEVEL = True
WHEEL_RES = 2


DETAILS = True          # passe de détails automatique (2026-11-16) : joints de portes, poignées, joint de vitres,
                        # essuie-glaces, écrous de roue ; fiche no_auto_details=True pour l'éviter


def smooth_opts(car):
    sm = car.get("smooth", True)
    if not sm:
        return None
    return DEFAULT_SMOOTH if sm is True else sm


def bevel_bm(bm, offset, segments=2, min_angle=30.0):
    """Chanfreine les arêtes vives d'un petit solide (pièce rapportée) : bords adoucis, façon jouet."""
    if not BEVEL or offset <= 0.002:
        return
    edges = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > math.radians(min_angle)]
    if not edges:
        return
    verts = list({v for e in edges for v in e.verts})
    bmesh.ops.bevel(bm, geom=verts + edges, offset=offset, offset_type='OFFSET', segments=segments,
                    profile=0.5, affect='EDGES', clamp_overlap=True)


def merge_bm(dst, src):
    """Ajoute le contenu de src (libéré) à dst."""
    me = bpy.data.meshes.new("_pk_tmp")
    src.to_mesh(me)
    src.free()
    dst.from_mesh(me)
    bpy.data.meshes.remove(me)


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


def layer_col(name, lc=None):
    lc = lc or bpy.data.scenes[SCENE].view_layers[0].layer_collection
    if lc.name == name:
        return lc
    for c in lc.children:
        r = layer_col(name, c)
        if r:
            return r
    return None


def set_excluded(name, flag):
    lc = layer_col(name)
    if lc:
        lc.exclude = flag


# ----------------------------------------------------------------- matériaux / palette
def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def color_materials():
    M = {}
    for name, lo, hi in PALETTE:
        mid = [srgb_to_lin((a + b) / 2) for a, b in zip(lo, hi)]
        M[name], _ = H.principled("PK_c_" + name, tuple(mid), rough=0.7)
    for key, (mname, c, strength) in EMISSIVE.items():
        M[key], _ = H.principled(mname, tuple(0.6 * v for v in c), rough=0.4, emit=c, strength=strength)
    return M


def palette_image(save_path=None):
    w, h = PAL_W * NCOL, 64
    img = bpy.data.images.get("PK_Palette")
    if img is None or tuple(img.size) != (w, h):
        if img:
            bpy.data.images.remove(img)
        img = bpy.data.images.new("PK_Palette", w, h, alpha=False)
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


def palette_materials():
    """PK_Palette (tout sauf la peinture) et PK_Paint (même texture x couleur de l'objet pour l'aperçu)."""
    img = palette_image()
    out = {}
    for name, tinted in (("PK_Palette", False), ("PK_Paint", True)):
        m, b = H.principled(name, (1, 1, 1), rough=0.6)
        nt = m.node_tree
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = 'Linear'
        tex.extension = 'EXTEND'
        if tinted:
            oi = nt.nodes.new("ShaderNodeObjectInfo")
            mul = nt.nodes.new("ShaderNodeVectorMath")
            mul.operation = 'MULTIPLY'
            nt.links.new(tex.outputs["Color"], mul.inputs[0])
            nt.links.new(oi.outputs["Color"], mul.inputs[1])
            nt.links.new(mul.outputs[0], H.sock(b, "Base Color"))
        else:
            nt.links.new(tex.outputs["Color"], H.sock(b, "Base Color"))
        out[name] = m
    return out


# ----------------------------------------------------------------- courbes / sections
def curve(val):
    """Nombre -> constante ; liste de (x, v) -> interpolation linéaire (bornée aux extrémités)."""
    if isinstance(val, (int, float)):
        c = float(val)
        return lambda x: c
    ks = sorted((float(a), float(b)) for a, b in val)
    xs = [k[0] for k in ks]

    def f(x):
        if x <= ks[0][0]:
            return ks[0][1]
        if x >= ks[-1][0]:
            return ks[-1][1]
        i = bisect.bisect_right(xs, x) - 1
        (x0, y0), (x1, y1) = ks[i], ks[i + 1]
        return y0 + (y1 - y0) * (x - x0) / (x1 - x0) if x1 > x0 else y1
    return f


def smooth_curve(val):
    """Comme curve(), mais interpolation cubique monotone (Fritsch-Carlson) : profil arrondi sans dépassement entre
    les points clés (option « smooth » d'une fiche)."""
    if isinstance(val, (int, float)):
        return curve(val)
    ks = sorted((float(a), float(b)) for a, b in val)
    xs = [k[0] for k in ks]
    ys = [k[1] for k in ks]
    n = len(ks)
    if n < 3:
        return curve(val)
    d = [(ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]) if xs[i + 1] > xs[i] else 0.0 for i in range(n - 1)]
    m = [d[0]] + [0.0 if d[i - 1] * d[i] <= 0 else (d[i - 1] + d[i]) / 2 for i in range(1, n - 1)] + [d[-1]]
    for i in range(n - 1):
        if d[i] == 0.0:
            m[i] = m[i + 1] = 0.0
            continue
        a, b = m[i] / d[i], m[i + 1] / d[i]
        h = a * a + b * b
        if h > 9.0:
            t = 3.0 / math.sqrt(h)
            m[i], m[i + 1] = t * a * d[i], t * b * d[i]

    def f(x):
        if x <= xs[0]:
            return ys[0]
        if x >= xs[-1]:
            return ys[-1]
        i = min(bisect.bisect_right(xs, x) - 1, n - 2)
        h = xs[i + 1] - xs[i]
        if h <= 0:
            return ys[i + 1]
        t = (x - xs[i]) / h
        h00, h10, h01, h11 = 2 * t ** 3 - 3 * t ** 2 + 1, t ** 3 - 2 * t ** 2 + t, -2 * t ** 3 + 3 * t ** 2, t ** 3 - t ** 2
        return h00 * ys[i] + h10 * h * m[i] + h01 * ys[i + 1] + h11 * h * m[i + 1]
    return f


ZK = ("bot", "lo", "rk", "mx", "sh", "belt", "re", "top")
INSETS = dict(fl=0.10, lo=0.035, rk=0.012, sh=0.03, rail=0.10)


def prepare(car):
    cv = smooth_curve if smooth_opts(car) else curve
    car["_z"] = {k: cv(car["z"][k]) for k in ZK}
    car["_w"] = {k: cv(car["w"][k]) for k in ("W", "belt", "re")}
    ins = dict(INSETS)
    ins.update(car.get("inset", {}))
    car["_i"] = {k: curve(v) for k, v in ins.items()}
    car["_crown"] = curve(car.get("crown", 0.6))


def section(car, x):
    """Demi-section (y >= 0) : p0 plancher centre, p1 bord du plancher, p2 bas de caisse, p3 haut du bas de
    caisse, p4 largeur max, p5 épaule, p6 ceinture (bord du capot à l'avant), p7 bord du pavillon (joint du
    capot), p8 intérieur du montant / nervure, p9 sommet au centre."""
    z = {k: car["_z"][k](x) for k in ZK}
    W, wb, wr = (car["_w"][k](x) for k in ("W", "belt", "re"))
    i = {k: f(x) for k, f in car["_i"].items()}
    cr = car["_crown"](x)
    y5 = W - i["sh"]
    y6 = min(wb, y5 - 0.005)
    y7 = min(wr, y6 - 0.005)
    y8 = max(0.03, min(y7 - i["rail"], y7 - 0.01))
    pts = [(0.0, z["bot"]), (max(0.03, W - i["fl"]), z["bot"]), (W - i["lo"], z["lo"]), (W - i["rk"], z["rk"]),
           (W, z["mx"]), (y5, z["sh"]), (y6, z["belt"]), (y7, z["re"]),
           (y8, z["re"] + (z["top"] - z["re"]) * cr), (0.0, z["top"])]
    for k in range(2, 7):
        pts[k] = (pts[k][0], max(pts[k][1], pts[k - 1][1] + 0.003))
    return pts


def station_list(car):
    xs = set()
    for grp in ("z", "w"):
        for v in car[grp].values():
            if isinstance(v, (list, tuple)):
                xs |= {float(p[0]) for p in v}
    g = car["glass"]
    for k in ("cowl", "ws_top", "rw_top", "rw_bot"):
        if g.get(k) is not None:
            xs.add(float(g[k]))
    for rng in g.get("side", []) + g.get("pillars", []) + g.get("roof_bars", []):
        xs |= {float(rng[0]), float(rng[1])}
    for j, x0, x1, c in car.get("bands", []):
        xs |= {float(x0), float(x1)}
    xs |= {float(x) for x in car.get("stations", [])}
    sm = smooth_opts(car)
    if sm and sm.get("dx"):          # stations régulières en plus des points clés (profil arrondi)
        # pas plus de ~56 stations régulières (véhicules longs : bus, semi, car)
        n = int((car["front"] - car["rear"]) / max(sm["dx"], (car["front"] - car["rear"]) / 56.0))
        xs |= {car["rear"] + (car["front"] - car["rear"]) * k / n for k in range(1, n)}
    xs |= {car["front"], car["rear"]}
    xs = sorted((x for x in xs if car["rear"] - 1e-6 <= x <= car["front"] + 1e-6), reverse=True)
    out = []
    for x in xs:
        if not out or out[-1] - x > 0.012:
            out.append(x)
    return out


def band_color(car, j, xm):
    """Couleur d'une bande j (entre p_j et p_j+1) au milieu d'un intervalle de stations."""
    liv = car.get("livery")
    if liv:
        c = liv(j, xm)
        if c:
            return c
    for jj, x0, x1, c in car.get("bands", []):
        if jj == j and x0 >= xm >= x1:
            return c
    g = car["glass"]
    if j <= 1:
        return "dark"
    if j == 6:
        for x0, x1 in g.get("pillars", []):
            if x0 >= xm >= x1:
                return car.get("pillar_color", "trim")
        for x0, x1 in g.get("side", []):
            if x0 >= xm >= x1:
                return "glass"
        return "paint"
    if j == 8:
        if g["cowl"] >= xm >= g["ws_top"]:
            return "glass"
        if g.get("rw_top") is not None and g["rw_top"] >= xm >= g["rw_bot"]:
            return "glass"
        if g.get("rw_top") is not None and g["ws_top"] >= xm >= g["rw_top"]:
            for x0, x1 in g.get("roof_bars", []):
                if x0 >= xm >= x1:
                    return "paint"
            return car.get("roof", "paint")
        return "paint"
    if j == 7 and g["cowl"] >= xm >= g.get("rw_bot", g["ws_top"]):
        return car.get("rail_color", "paint")
    return "paint"


def _cr(p0, p1, p2, p3, t):
    """Catmull-Rom centripète (2D) entre p1 et p2 ; segment trop court : interpolation linéaire."""
    def tj(ti, a, b):
        return ti + max(math.hypot(b[0] - a[0], b[1] - a[1]), 1e-9) ** 0.5
    if math.hypot(p2[0] - p1[0], p2[1] - p1[1]) < 1e-3:
        return (p1[0] + (p2[0] - p1[0]) * t, p1[1] + (p2[1] - p1[1]) * t)
    t0 = 0.0
    t1 = tj(t0, p0, p1)
    t2 = tj(t1, p1, p2)
    t3 = tj(t2, p2, p3)
    u = t1 + (t2 - t1) * t

    def lerp(a, b, ta, tb):
        k = (u - ta) / (tb - ta)
        return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)
    A1, A2, A3 = lerp(p0, p1, t0, t1), lerp(p1, p2, t1, t2), lerp(p2, p3, t2, t3)
    B1, B2 = lerp(A1, A2, t0, t2), lerp(A2, A3, t1, t3)
    return lerp(B1, B2, t1, t2)


def refine_section(sec, sub):
    """Subdivise la demi-section (spline passant par les 10 points) : sub[j] morceaux pour la bande j. Renvoie les
    points et, pour chaque morceau, la bande d'origine (couleurs inchangées). Points fantômes symétriques aux
    extrémités : tangente horizontale au centre du pavillon et du plancher."""
    ext = [(-sec[1][0], sec[1][1])] + list(sec) + [(-sec[-2][0], sec[-2][1])]
    pts, bands = [sec[0]], []
    for j in range(len(sec) - 1):
        k = sub.get(j, 1)
        for i in range(1, k + 1):
            pts.append(sec[j + 1] if i == k else _cr(ext[j], ext[j + 1], ext[j + 2], ext[j + 3], i / k))
            bands.append(j)
    return pts, bands


def build_body(car, M, col):
    xs = station_list(car)
    secs = [section(car, x) for x in xs]
    bands = list(range(len(secs[0]) - 1))
    sub = (smooth_opts(car) or {}).get("sub")
    if sub:
        ref = [refine_section(s, sub) for s in secs]
        secs = [r[0] for r in ref]
        bands = ref[0][1]
    P = len(secs[0])
    bm = bmesh.new()
    V = [[bm.verts.new((x, y, z)) for (y, z) in sec] for x, sec in zip(xs, secs)]
    for s in range(len(xs) - 1):
        xm = (xs[s] + xs[s + 1]) / 2
        for j in range(P - 1):
            f = bm.faces.new((V[s][j], V[s][j + 1], V[s + 1][j + 1], V[s + 1][j]))
            f.material_index = CI[band_color(car, bands[j], xm)]
    bm.faces.new(V[0]).material_index = CI[car.get("front_cap", "paint")]
    bm.faces.new(list(reversed(V[-1]))).material_index = CI[car.get("rear_cap", "paint")]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    probe = max(bm.faces, key=lambda f: f.calc_center_median().y)
    if probe.normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "PK_%s_Body" % car["id"], col, [M[n] for n in NAMES])
    mr = ob.modifiers.new("Mirror", 'MIRROR')
    mr.use_axis = (False, True, False)
    mr.use_clip = True
    mr.use_mirror_merge = True
    return ob


def arch_cutters(car, M, col, body):
    cc = get_col("PK_%s_Cut" % car["id"], col)
    cc.hide_render = True
    n = car.get("arch_n", 6)
    sq = car.get("arch_sq", 1.0)
    for i, xa in enumerate(car["axles"]):
        if i not in car.get("arch_axles", range(len(car["axles"]))):
            continue
        Wd = axle_wheel(car, i)
        R0 = Wd["rt"] + car.get("arch_gap", 0.05)
        y_in = Wd["y"] - Wd["w"] / 2 - 0.035 - (Wd["w"] + 0.03 if Wd.get("twin") else 0.0)
        zc = Wd["rt"] + car.get("arch_dz", 0.0)
        pts = [(xa + R0, zc - 0.7)]
        for k in range(n + 1):
            a = math.pi * k / n
            pts.append((xa + R0 * math.cos(a), zc + R0 * math.sin(a) * sq))
        pts.append((xa - R0, zc - 0.7))
        bm = bmesh.new()
        H.extrude(bm, pts, 'y', y_in, 2.0)
        H.extrude(bm, pts, 'y', -2.0, -y_in)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        c = H.bm_to_obj(bm, "PK_%s_Cut%d" % (car["id"], i), cc, [M[car.get("well", "dark")]])
        c.display_type = 'WIRE'
        c.hide_render = True
    # découpes supplémentaires (benne de pick-up...) : (centre, taille, couleur des nouvelles faces)
    for k, (center, size, color) in enumerate(car.get("cut_boxes", [])):
        bm = bmesh.new()
        H.box(bm, Vector(center), size)
        c = H.bm_to_obj(bm, "PK_%s_CutBox%d" % (car["id"], k), cc, [M[color]])
        c.display_type = 'WIRE'
        c.hide_render = True
    b = body.modifiers.new("Arches", 'BOOLEAN')
    b.operand_type = 'COLLECTION'
    b.collection = cc
    b.operation = 'DIFFERENCE'
    b.solver = 'EXACT'
    b.material_mode = 'TRANSFER'
    return cc


# ----------------------------------------------------------------- décors (contexte par voiture)
VIEW = {'front': Vector((-1, 0, 0)), 'rear': Vector((1, 0, 0)), 'side': Vector((0, -1, 0)),
        'rside': Vector((0, 1, 0)), 'top': Vector((0, 0, -1)), 'bottom': Vector((0, 0, 1))}
AXES = {'front': (1, 2), 'rear': (1, 2), 'side': (0, 2), 'rside': (0, 2), 'top': (0, 1), 'bottom': (0, 1)}


def inside(p, poly):
    x, y = p
    c = False
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        if (y1 > y) != (y2 > y):
            if x < x1 + (y - y1) * (x2 - x1) / (y2 - y1):
                c = not c
    return c


def mirror_bm(bm):
    ret = bmesh.ops.duplicate(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:])
    nv = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
    nf = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
    for v in nv:
        v.co.y = -v.co.y
    bmesh.ops.reverse_faces(bm, faces=nf)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)


class Ctx:
    """Outils de détail liés à une voiture : décors projetés, pièces, requêtes sur la carrosserie."""

    def __init__(self, car, M, col, body):
        self.car, self.M, self.col = car, M, col
        self.bm = bmesh.new()
        if body is not None:
            dg = bpy.context.evaluated_depsgraph_get()
            ev = body.evaluated_get(dg)
            me = ev.to_mesh()
            self.bm.from_mesh(me)
            ev.to_mesh_clear()
            self.bm.transform(body.matrix_world)
        self.bm.normal_update()
        self.bm.faces.ensure_lookup_table()
        self.bm.faces.index_update()
        self.bvh = BVHTree.FromBMesh(self.bm)
        self.count = 0

    def obj(self, bm, name, color, export_name=None):
        self.count += 1
        mat = self.M[color]
        ob = H.bm_to_obj(bm, "PK_%s_%s_%02d" % (self.car["id"], name, self.count), self.col, [mat])
        if export_name:
            ob["export_name"] = export_name
        return ob

    # --- requêtes
    def hit(self, origin, direction):
        loc, nrm, idx, d = self.bvh.ray_cast(Vector(origin), Vector(direction).normalized(), 2 * FAR)
        return loc, nrm

    def top(self, x, y=0.0):
        loc, _ = self.hit((x, y, FAR), (0, 0, -1))
        return loc.z if loc else None

    def side(self, x, z):
        loc, _ = self.hit((x, FAR, z), (0, -1, 0))
        return loc.y if loc else None

    def front(self, y, z):
        loc, _ = self.hit((FAR, y, z), (-1, 0, 0))
        return loc.x if loc else None

    def rear(self, y, z):
        loc, _ = self.hit((-FAR, y, z), (1, 0, 0))
        return loc.x if loc else None

    # --- décor projeté : découpe les facettes visibles selon le contour, décalées de `offset`
    def decal(self, pts, view, color, mirror=True, offset=0.005, name="Decal", export_name=None, min_facing=0.02):
        """min_facing : ignore les facettes trop rasantes vues de `view` (normale . direction > -min_facing)."""
        dm = self.cut(pts, view, offset, min_facing)
        if dm is None:
            print("décor vide :", name, view, pts[:2])
            return None
        if mirror:
            mirror_bm(dm)
        return self.obj(dm, name, color, export_name)

    def cut(self, pts, view, offset=0.005, min_facing=0.02):
        """Facettes de la carrosserie vues de `view`, découpées selon le contour et décalées de `offset`
        (bmesh en coordonnées monde, None si rien n'est dans le contour)."""
        d = VIEW[view]
        A, B = AXES[view]
        pts = [(float(a), float(b)) for a, b in pts]
        amin = min(p[0] for p in pts) - 0.01
        amax = max(p[0] for p in pts) + 0.01
        bmin = min(p[1] for p in pts) - 0.01
        bmax = max(p[1] for p in pts) + 0.01
        dm = bmesh.new()
        vmap = {}
        for f in self.bm.faces:
            if f.normal.dot(d) > -min_facing:
                continue
            fa = [v.co[A] for v in f.verts]
            fb = [v.co[B] for v in f.verts]
            if max(fa) < amin or min(fa) > amax or max(fb) < bmin or min(fb) > bmax:
                continue
            cen = f.calc_center_median()
            loc, nrm, idx, dist = self.bvh.ray_cast(cen - d * FAR, d, 2 * FAR)
            if idx != f.index and (loc is None or (loc - cen).length > 2e-3):
                continue
            vs = []
            for v in f.verts:
                if v.index not in vmap:
                    vmap[v.index] = dm.verts.new(v.co)
                vs.append(vmap[v.index])
            try:
                dm.faces.new(vs)
            except ValueError:
                pass
        n = len(pts)
        for k in range(n):
            p, q = pts[k], pts[(k + 1) % n]
            e3 = Vector((0.0, 0.0, 0.0))
            e3[A], e3[B] = q[0] - p[0], q[1] - p[1]
            if e3.length < 1e-7:
                continue
            co = Vector((0.0, 0.0, 0.0))
            co[A], co[B] = p[0], p[1]
            bmesh.ops.bisect_plane(dm, geom=dm.verts[:] + dm.edges[:] + dm.faces[:], dist=1e-6,
                                   plane_co=co, plane_no=e3.cross(d).normalized())
        kill = [f for f in dm.faces if not inside((f.calc_center_median()[A], f.calc_center_median()[B]), pts)]
        bmesh.ops.delete(dm, geom=kill, context='FACES')
        bmesh.ops.delete(dm, geom=[v for v in dm.verts if not v.link_faces], context='VERTS')
        if not dm.faces:
            dm.free()
            return None
        dm.normal_update()
        for v in dm.verts:
            nn = Vector((0.0, 0.0, 0.0))
            for f in v.link_faces:
                nn += f.normal
            if nn.length > 0:
                v.co += nn.normalized() * offset
        return dm

    # --- pièces rapportées
    def box(self, boxes, color, mirror=False, name="Part", export_name=None):
        """boxes : liste de (centre, taille, rotation 3x3 ou None)."""
        bm = bmesh.new()
        for c, s, r in boxes:
            tmp = bmesh.new()
            H.box(tmp, Vector(c), s, r)
            m = min(s)
            bevel_bm(tmp, min(0.035, 0.2 * m), 2 if m > 0.05 else 1)
            merge_bm(bm, tmp)
        if mirror:
            mirror_bm(bm)
        return self.obj(bm, name, color, export_name)

    def marker(self, name, loc, flow=(-1.0, 0.0, 0.0)):
        """Repère exporté (objet vide « name », ex. exhaust-left) : position et direction de flux (vers l'arrière
        par défaut). Dans Unity, son +Z local suit le flux."""
        e = bpy.data.objects.new("PK_%s_%s" % (self.car["id"], name), None)
        e.empty_display_type = 'SINGLE_ARROW'
        e.empty_display_size = 0.2
        e.location = loc
        e["export_name"] = name
        e["flow"] = tuple(float(v) for v in flow)
        self.col.objects.link(e)
        return e

    def prism(self, poly, axis, a0, a1, color, mirror=False, name="Prism", export_name=None):
        bm = bmesh.new()
        H.extrude(bm, poly, axis, a0, a1)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        xs = [p[0] for p in poly]
        ys = [p[1] for p in poly]
        m = min(abs(a1 - a0), max(xs) - min(xs), max(ys) - min(ys))
        bevel_bm(bm, min(0.025, 0.2 * m), 2 if m > 0.05 else 1)
        if mirror:
            mirror_bm(bm)
        return self.obj(bm, name, color, export_name)

    def flares(self, width=0.07, out=0.025, color="trim", ext=0.25):
        """Élargisseurs d'ailes (SUV, pick-up) : bord intérieur = contour exact du passage de roue."""
        car = self.car
        Wd = car["wheel"]
        R0 = Wd["rt"] + car.get("arch_gap", 0.05)
        n = car.get("arch_n", 6)
        sq = car.get("arch_sq", 1.0)
        zc = Wd["rt"] + car.get("arch_dz", 0.0)
        angles = [-ext] + [math.pi * k / n for k in range(n + 1)] + [math.pi + ext]
        for xa in car["axles"]:
            outer = [(xa + (R0 + width) * math.cos(a), zc + (R0 + width) * math.sin(a) * sq) for a in angles]
            inner = [(xa + R0 * math.cos(a), zc + R0 * math.sin(a) * sq) for a in angles]
            poly = outer + list(reversed(inner))
            y0 = self.side(xa, zc + R0 * sq + width / 2) or car["_w"]["W"](xa)
            self.prism(poly, 'y', y0 - 0.06, y0 + out, color, mirror=True, name="Flare")


# ----------------------------------------------------------------- détails automatiques
def _bar(ctx, p0, p1, w, h, color, name):
    """Barre fine orientée de p0 à p1 (essuie-glace, baguette)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    X = d.normalized()
    Z = Vector((0, 0, 1))
    Y = Z.cross(X).normalized()
    Z = X.cross(Y).normalized()
    ctx.box([((p0 + p1) / 2, (d.length, w, h), Matrix((X, Y, Z)).transposed())], color, name=name)


def door_seams(car):
    """Abscisses des joints de portes : avant de la première vitre latérale, puis milieu de chaque montant."""
    g = car["glass"]
    side = g.get("side") or []
    if not side:
        return []
    xs = [side[0][0] + 0.02]
    for x0, x1 in g.get("pillars", []):
        xm = (x0 + x1) / 2
        if side[-1][1] - 0.05 < xm < side[0][0]:
            xs.append(xm)
    if len(xs) == 1:
        xs.append(side[0][1] - 0.02)
    return sorted(set(round(x, 3) for x in xs), reverse=True)


def auto_details(ctx):
    """Détails ajoutés à toutes les voitures à carrosserie : ce que la fiche fait déjà (poignées, essuie-glaces)
    n'est pas doublé."""
    car = ctx.car
    names = " ".join(o.name for o in ctx.col.objects)
    z, w = car["_z"], car["_w"]
    g = car["glass"]
    Wd = car.get("wheel")
    # véhicules longs (bus, car, poids lourds) : pas de joints ni de poignées déduits des vitres
    seams = door_seams(car) if car["front"] - car["rear"] < 6.0 else []
    # Joints de portes : traits sombres du bas de caisse à la ceinture, en évitant les passages de roue
    for x in seams:
        lo = z["rk"](x) + 0.035
        if Wd:
            R0 = Wd["rt"] + car.get("arch_gap", 0.05)
            for xa in car["axles"]:
                if abs(x - xa) < R0 + 0.02:
                    lo = max(lo, Wd["rt"] + math.sqrt(max(R0 * R0 - (x - xa) ** 2, 0.0)) + 0.05)
        hi = z["belt"](x) - 0.015
        if hi - lo > 0.12:
            ctx.decal([(x - 0.007, lo), (x + 0.007, lo), (x + 0.007, hi), (x - 0.007, hi)], 'side', 'trim',
                      offset=0.004, name="DoorSeam", min_facing=0.15)
    # Poignées : à l'arrière de chaque porte, sous la ceinture
    if "Handle" not in names:
        for i in range(len(seams) - 1):
            xb = seams[i + 1]
            if seams[i] - xb < 0.5:
                continue
            xh = xb + 0.2
            zh = z["belt"](xh) - 0.075
            ctx.decal([(xh - 0.13, zh - 0.014), (xh, zh - 0.014), (xh, zh + 0.014), (xh - 0.13, zh + 0.014)], 'side',
                      'chrome' if car.get("chrome_handles") else 'trim', offset=0.007, name="AutoHandle", min_facing=0.15)
    # Joint sous les vitres latérales (baguette sombre le long de la ceinture)
    side = g.get("side") or []
    if side:
        x0, x1 = side[0][0], side[-1][1]
        n = max(2, int((x0 - x1) / 0.15))
        xs = [x0 + (x1 - x0) * k / n for k in range(n + 1)]
        top = [(x, z["belt"](x) + 0.006) for x in xs]
        bot = [(x, z["belt"](x) - 0.016) for x in reversed(xs)]
        ctx.decal(top + bot, 'side', 'trim', offset=0.004, name="BeltSeal", min_facing=0.1)
    # Essuie-glaces au pied du pare-brise
    if "Wiper" not in names and g.get("cowl") is not None:
        xc = g["cowl"] - 0.03
        Wc = w["W"](xc)
        for y0, y1 in ((-0.62, -0.06), (0.02, 0.58)):
            ya, yb = y0 * Wc, y1 * Wc
            za, zb = ctx.top(xc + 0.02, ya), ctx.top(xc - 0.06, yb)
            if za is None or zb is None:
                continue
            _bar(ctx, (xc + 0.02, ya, za + 0.018), (xc - 0.06, yb, zb + 0.018), 0.022, 0.016, 'black', "AutoWiper")


# ----------------------------------------------------------------- roues
SPOKES = {"alloy5": 15, "alloy6": 12, "multi": 16, "steel": 12, "hubcap": 12, "truck": 12, "tractor": 20}


def axle_wheel(car, i):
    """Réglages de roue de l'essieu i : car["wheel"] complété par car["axle_wheels"][i] (jumelage, taille...)."""
    Wd = dict(car["wheel"])
    per = car.get("axle_wheels")
    if per and i < len(per) and per[i]:
        Wd.update(per[i])
    return Wd


def wheel_name(i, side):
    tag = "front" if i == 0 else ("back" if i == 1 else "back%d" % i)
    return "wheel-%s-%s" % (tag, "center" if side == 0 else ("left" if side > 0 else "right"))


def build_wheel(car, xa, side, M, col, export_name, Wd=None):
    """side = 1 gauche, -1 droite, 0 roue seule dans l'axe (tricycle : jante visible des deux côtés)."""
    Wd = Wd or car["wheel"]
    rt, rr, w, yc = Wd["rt"], Wd["rr"], Wd["w"], Wd["y"]
    style = Wd.get("style", "alloy5")
    res = 1 if Wd.get("nseg") else WHEEL_RES
    nseg = Wd.get("nseg") or SPOKES.get(style, 12) * res     # nseg : roue plus ronde (option)
    names = ["tire", Wd.get("rim", "rim"), "dark", "chrome", "grey"]      # rim : couleur des rayons (option)
    bm = bmesh.new()
    s0 = len(bm.faces)
    # pneu à flancs bombés (épaules arrondies, style cartoon)
    sh = min(0.05, w * 0.22)
    tire_prof = [(rr, w / 2 - 0.01), (rt - 0.06, w / 2), (rt - sh * 0.3, w / 2 - sh * 0.3), (rt, w / 2 - sh),
                 (rt, -(w / 2 - sh)), (rt - sh * 0.3, -(w / 2 - sh * 0.3)), (rt - 0.06, -w / 2),
                 (rr, -(w / 2 - 0.01))]
    H.revolve(bm, tire_prof, (0, 0, 0), side or 1, nseg=nseg)
    if Wd.get("twin"):          # roue jumelée : second pneu côté intérieur
        H.revolve(bm, tire_prof, (0, -side * (w + 0.03), 0), side, nseg=nseg)
        H.revolve(bm, [(rr * 0.98, w / 2), (rr * 0.98, -(w / 2 + 0.03)), (rr * 0.6, -(w / 2 + 0.03)),
                       (rr * 0.6, w / 2)], (0, -side * (w + 0.03) / 2, 0), side, nseg=nseg)
    bm.faces.ensure_lookup_table()
    tire = [bm.faces[i] for i in range(s0, len(bm.faces))]
    for f in tire:
        f.material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=tire)
    lugs = Wd.get("lugs", 0)
    if lugs:                    # pneu agricole : crampons en chevrons, demi-crampons décalés d'un demi-pas
        before = set(bm.faces)
        delta = (w / 2) / rt * 0.9
        for k in range(lugs):
            for sgn, off in ((1, 0.0), (-1, 0.5)):
                th0 = 2 * math.pi * (k + off) / lugs
                pts = []
                for th, a in ((th0, 0.0), (th0 + delta, sgn * (w / 2 - 0.02))):
                    pts.append(Vector((rt * math.cos(th), side * a, rt * math.sin(th))))
                X = (pts[1] - pts[0]).normalized()
                mid = (pts[0] + pts[1]) / 2
                Z = Vector((mid.x, 0.0, mid.z)).normalized()
                Y = Z.cross(X).normalized()
                Z = X.cross(Y).normalized()
                H.box(bm, mid + Z * 0.025, ((pts[1] - pts[0]).length, 0.075 * rt / 0.9, 0.05),
                      Matrix((X, Y, Z)).transposed())
        for f in bm.faces:
            if f not in before:
                f.material_index = 0
    # jante : disque plat, rayons obtenus en alternant les couleurs des secteurs (des deux côtés si roue centrale)
    rh = rr * 0.30
    disc = []
    for sd in ((1, -1) if side == 0 else (side,)):
        a = sd * (w / 2 - 0.02)
        ring_in, ring_out = [], []
        for k in range(nseg):
            th = 2 * math.pi * (k + 0.5) / nseg
            c, s = math.cos(th), math.sin(th)
            ring_in.append(bm.verts.new((rh * c, a, rh * s)))
            ring_out.append(bm.verts.new((rr * c, a, rr * s)))
        hub = bm.verts.new((0, a + sd * 0.012, 0))
        for k in range(nseg):
            k2 = (k + 1) % nseg
            f = bm.faces.new((ring_in[k], ring_out[k], ring_out[k2], ring_in[k2]))
            kk = k // res           # secteur d'origine (le motif des rayons ne change pas avec la résolution)
            if style == "alloy5":
                f.material_index = 1 if kk % 3 == 0 else 2
            elif style in ("alloy6", "multi"):
                f.material_index = 1 if kk % 2 == 0 else 2
            elif style == "steel":
                f.material_index = 4
            elif style == "truck":
                f.material_index = 4 if kk % 2 == 0 else 2
            elif style == "tractor":
                f.material_index = 4 if kk % 4 else 2
            else:
                f.material_index = 1
            disc.append((f, sd))
            g = bm.faces.new((hub, ring_in[k2], ring_in[k]))
            g.material_index = 3 if style in ("hubcap", "steel", "truck") else 2
            disc.append((g, sd))
        if DETAILS and style != "tractor":       # écrous de roue (détail, 2026-11-16)
            nn = 6 if style == "truck" else 5
            before = set(bm.faces)
            for k in range(nn):
                th = 2 * math.pi * (k + 0.25) / nn
                rn = rh * 0.62
                H.box(bm, Vector((rn * math.cos(th), a + sd * 0.014, rn * math.sin(th))),
                      (rh * 0.16, 0.022, rh * 0.16), Matrix.Rotation(th, 3, 'Y'))
            for f in bm.faces:
                if f not in before:
                    f.material_index = 3
    bm.normal_update()
    for f, sd in disc:
        if f.normal.y * sd < 0:
            f.normal_flip()
    ob = H.bm_to_obj(bm, "PK_%s_%s" % (car["id"], export_name), col, [M[n] for n in names])
    ob.location = (xa, side * yc, rt)
    ob["export_name"] = export_name
    return ob


# ----------------------------------------------------------------- cuisson
def bake(car, col):
    """Applique les modificateurs, triangule les n-gones, remplace les couleurs par la palette
    (UV = colonne x hauteur), peinture sur PK_Paint, facettes à plat."""
    dg = bpy.context.evaluated_depsgraph_get()
    zmax = car["height"]
    pal = bpy.data.materials["PK_Palette"]
    paint = bpy.data.materials["PK_Paint"]
    paint_as = car.get("paint_as")
    flat = bool(car.get("flat"))
    for ob in list(col.all_objects):
        if ob.type != 'MESH' or ob.hide_render or ob.get("prebaked"):
            continue
        ev = ob.evaluated_get(dg)
        me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
        old = [m.name if m else "" for m in me.materials]
        bm = bmesh.new()
        bm.from_mesh(me)
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method='BEAUTY', ngon_method='BEAUTY')
        uv = bm.loops.layers.uv.verify()
        slots = []

        def slot(m):
            if m not in slots:
                slots.append(m)
            return slots.index(m)
        zoff = ob.matrix_world.translation.z
        for f in bm.faces:
            n = old[f.material_index] if f.material_index < len(old) else ""
            key = n[5:] if n.startswith("PK_c_") else None
            if key == "paint" and paint_as:
                key = paint_as
            if key is None:
                f.material_index = slot(bpy.data.materials.get(n))
                u = None
            elif key == "paint":
                f.material_index = slot(paint)
                u = CI["paint"]
            else:
                f.material_index = slot(pal)
                u = CI[key]
            f.smooth = not flat
            for lp in f.loops:
                if u is None:
                    lp[uv].uv = (0.5, 0.5)
                else:
                    v = min(0.99, max(0.01, (lp.vert.co.z + zoff) / zmax))
                    lp[uv].uv = ((u + 0.5) / NCOL, v)
        if not flat:            # normales lisses, arêtes vives au-delà de SHARP_ANGLE
            lim = math.radians(SHARP_ANGLE)
            for e in bm.edges:
                e.smooth = not (e.is_manifold and e.calc_face_angle(0) > lim) and len(e.link_faces) == 2
        me.materials.clear()
        for m in slots:
            me.materials.append(m)
        bm.to_mesh(me)
        bm.free()
        ob.modifiers.clear()
        old_me = ob.data
        ob.data = me
        if old_me.users == 0:
            bpy.data.meshes.remove(old_me)
    cc = bpy.data.collections.get("PK_%s_Cut" % car["id"])
    if cc:
        for o in list(cc.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(cc)


# ----------------------------------------------------------------- construction
def resolve(cid):
    spec = CARS[cid]
    if "base" in spec:
        base = resolve(spec["base"])
        car = dict(base)
        car.update({k: v for k, v in spec.items() if k not in ("base", "extras")})
        car["detail_fns"] = base["detail_fns"] + ([spec["extras"]] if spec.get("extras") else [])
    else:
        car = dict(spec)
        car["detail_fns"] = [spec["details"]] if spec.get("details") else []
    car["id"] = cid
    return car


def lin_color(c):
    return (c[0], c[1], c[2], 1.0)


def build(cid):
    check_scene()
    car = resolve(cid)
    M = color_materials()
    palette_materials()
    if car.get("no_emissive"):      # véhicule chargé sur un autre : optiques éteintes (couleurs de palette)
        M = dict(M, tail=M["red"], led=M["lens"], siren_red=M["red"], siren_blue=M["blue"])
    root = get_col(ROOT)
    col = get_col("PK_" + cid, root)
    # (ne jamais réactiver ROOT : cela réactive aussi toutes les collections enfants)
    set_excluded(col.name, False)
    H.clear_col(col.name)
    body = None
    if "z" in car:
        prepare(car)
        body = build_body(car, M, col)
        arch_cutters(car, M, col, body)
    ctx = Ctx(car, M, col, body)
    for fn in car["detail_fns"]:
        fn(ctx)
    if DETAILS and body is not None and not car.get("no_auto_details"):
        auto_details(ctx)
    ctx.bm.free()
    if "wheel" in car:
        for i, xa in enumerate(car["axles"]):
            Wd = axle_wheel(car, i)
            for side in ((0,) if Wd.get("single") else (1, -1)):      # single : roue seule dans l'axe (tricycle)
                build_wheel(car, xa, side, M, col, wheel_name(i, side), Wd)
    for o in col.all_objects:
        o.color = lin_color(car.get("color", (0.6, 0.6, 0.6)))
    bake(car, col)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in col.all_objects if o.type == 'MESH')
    set_excluded(col.name, True)
    return tris


# ----------------------------------------------------------------- présentation / rendu
def studio():
    check_scene()
    scn = bpy.context.scene
    col = get_col("PK_Studio")
    H.clear_col("PK_Studio")
    w = bpy.data.worlds.get("PK_World") or bpy.data.worlds.new("PK_World")
    scn.world = w
    w.use_nodes = True
    nt = w.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    mr = nt.nodes.new("ShaderNodeMapRange")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs[2], mr.inputs[0])
    mr.inputs[1].default_value = -1.0
    mr.inputs[2].default_value = 1.0
    nt.links.new(mr.outputs[0], ramp.inputs[0])
    el = ramp.color_ramp.elements
    el[0].position = 0.5
    el[0].color = (0.62, 0.68, 0.74, 1)
    el[1].position = 1.0
    el[1].color = (0.20, 0.36, 0.62, 1)
    nt.links.new(ramp.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = 0.9
    nt.links.new(bg.outputs[0], out.inputs[0])
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60)
    fm, _ = H.principled("PK_Floor", (0.16, 0.17, 0.17), rough=0.9)
    H.bm_to_obj(bm, "PK_Floor", col, [fm])
    ld = bpy.data.lights.get("PK_Sun") or bpy.data.lights.new("PK_Sun", 'SUN')
    ld.energy = 3.8
    ld.angle = math.radians(3)
    ld.color = (1.0, 0.97, 0.92)
    sun = bpy.data.objects.new("PK_Sun", ld)
    col.objects.link(sun)
    sun.rotation_euler = (math.radians(42), 0, math.radians(125))
    cam = bpy.data.objects.new("PK_Camera", bpy.data.cameras.new("PK_Camera"))
    col.objects.link(cam)
    scn.camera = cam
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            pass
    scn.eevee.taa_render_samples = 24
    scn.render.resolution_x, scn.render.resolution_y = 1600, 900
    scn.render.resolution_percentage = 100
    scn.render.image_settings.file_format = 'PNG'
    try:
        scn.view_settings.view_transform = 'AgX'
    except TypeError:
        pass
    return cam


def hide_sources():
    for c in bpy.data.collections[ROOT].children:
        lc = layer_col(c.name)
        if lc and not lc.exclude:
            lc.exclude = True


def show(ids, spacing=2.9, rows=1):
    """Présente des voitures (instances de collections) en rangées le long de Y."""
    check_scene()
    hide_sources()
    col = get_col("PK_Show")
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    per_row = math.ceil(len(ids) / rows)
    for i, cid in enumerate(ids):
        r, k = divmod(i, per_row)
        n = min(per_row, len(ids) - r * per_row)
        e = bpy.data.objects.new("PK_Show_" + cid, None)
        e.instance_type = 'COLLECTION'
        e.instance_collection = bpy.data.collections["PK_" + cid]
        e.color = lin_color(resolve(cid).get("color", (0.6, 0.6, 0.6)))
        e.location = (-r * 7.0, (k - (n - 1) / 2) * spacing, 0)
        col.objects.link(e)
    set_excluded(col.name, False)       # (déjà trouvée exclue : rendus vides)


def aim(az, el, dist, target=(0, 0, 0.7), lens=50):
    cam = bpy.data.objects["PK_Camera"]
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    return cam


def render_car(cid, path_prefix, views=((35, 9), (215, 9)), res=(1200, 675)):
    """Rendus 3/4 d'une voiture seule ; renvoie les chemins."""
    car = resolve(cid)
    show([cid])
    L = car["front"] - car["rear"]
    cx = (car["front"] + car["rear"]) / 2
    out = []
    for k, (az, el) in enumerate(views):
        out.append(render("%s_%d.png" % (path_prefix, k), az=az, el=el, dist=L * 1.75 + 1.0,
                          target=(cx, 0, car["height"] * 0.45), lens=45, res=res))
    return out


def render(path, az=35, el=10, dist=9.0, target=(0, 0, 0.7), lens=50, res=(1600, 900)):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = res
    aim(az, el, dist, target, lens)
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


# ----------------------------------------------------------------- catalogue de rendus
# (nom, azimut, élévation, part de l'image occupée par la boîte englobante)
CATALOG_VIEWS = [("1_avant", 35, 9, 0.74), ("2_arriere", 215, 9, 0.74), ("3_profil", 90, 2, 0.74),
                 ("4_face", 0, 4, 0.64), ("5_dessus", 50, 32, 0.86)]


def show_collection(name, color=None, location=(0, 0, 0)):
    """Instance d'une collection quelconque (ex. V74_Low) dans la présentation."""
    col = get_col("PK_Show")
    e = bpy.data.objects.new("PK_Show_" + name, None)
    e.instance_type = 'COLLECTION'
    e.instance_collection = bpy.data.collections[name]
    e.location = location
    if color:
        e.color = lin_color(color)
    col.objects.link(e)
    return e


def shown_bounds():
    """Boîte englobante (monde) des voitures présentées."""
    bpy.context.view_layer.update()      # matrices des instances tout juste créées
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for e in bpy.data.collections["PK_Show"].objects:
        for o in e.instance_collection.all_objects:
            if o.type != 'MESH':
                continue
            for c in o.bound_box:
                w = e.matrix_world @ o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
    return lo, hi


def frame(az, el, fill=0.72, lens=50):
    """Place la caméra (azimut, élévation) pour que les voitures présentées occupent `fill` de l'image."""
    from bpy_extras.object_utils import world_to_camera_view
    scn = bpy.context.scene
    lo, hi = shown_bounds()
    target = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) * 0.45))
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    dist = (hi - lo).length * 1.5
    for _ in range(6):
        cam = aim(az, el, dist, target, lens)
        bpy.context.view_layer.update()
        pts = [world_to_camera_view(scn, cam, c) for c in corners]
        span = max(max(p.x for p in pts) - min(p.x for p in pts), max(p.y for p in pts) - min(p.y for p in pts))
        dist *= span / fill
    return aim(az, el, dist, target, lens)


def catalog_settings(samples=64, res=(1920, 1080)):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    ee = scn.eevee
    ee.taa_render_samples = samples
    for attr, val in (("use_raytracing", True), ("ray_tracing_method", 'SCREEN'), ("use_fast_gi", True),
                      ("use_shadows", True)):
        try:
            setattr(ee, attr, val)
        except Exception:
            pass
    for cand in ('AgX - Medium High Contrast', 'Medium High Contrast'):
        try:
            scn.view_settings.look = cand
            break
        except TypeError:
            pass
    fm = bpy.data.materials.get("PK_Floor")
    if fm:
        H.sock(next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), "Base Color").default_value = \
            (0.32, 0.33, 0.34, 1)


def render_views(name, folder, views=CATALOG_VIEWS):
    out = []
    for tag, az, el, fill in views:
        frame(az, el, fill=fill)
        path = os.path.join(folder, "%s_%s.png" % (name, tag))
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out.append(path)
    return out


def render_catalog(folder, ids):
    """Rendus de chaque voiture du pack (nom de fichier = champ `file` de la fiche)."""
    check_scene()
    os.makedirs(folder, exist_ok=True)
    catalog_settings()
    out = []
    for cid in ids:
        car = resolve(cid)
        show([cid])
        out += render_views(car.get("file", cid), folder)
    return out


# ----------------------------------------------------------------- export Unity
def export(cid, folder):
    """FBX : carrosserie jointe « body », roues « wheel-* » (pivot au moyeu), pièces nommées (gyrophares),
    repères vides (Ctx.marker, ex. sorties d'échappement « exhaust-* »)."""
    os.makedirs(folder, exist_ok=True)
    src = bpy.data.collections["PK_" + cid]
    R = Matrix.Rotation(math.radians(-90), 4, 'Z')
    exp = get_col("PK_Export")
    for o in list(exp.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    parts, named, markers = [], [], []
    for ob in src.all_objects:
        if ob.type == 'EMPTY' and ob.get("export_name"):
            # repère : +Z local Unity = flux (Unity +Z = Blender -Y après R), Y local = haut
            f = (R.to_3x3() @ Vector(ob.get("flow", (-1.0, 0.0, 0.0)))).normalized()
            y = -f
            x = y.cross(Vector((0.0, 0.0, 1.0)))
            x = x.normalized() if x.length > 1e-6 else Vector((1.0, 0.0, 0.0))
            z = x.cross(y).normalized()
            e = bpy.data.objects.new(ob["export_name"], None)
            e.matrix_world = Matrix.Translation(R @ ob.matrix_world.translation) @ \
                Matrix((x, y, z)).transposed().to_4x4()
            exp.objects.link(e)
            markers.append(e)
            continue
        if ob.type != 'MESH' or ob.hide_render:
            continue
        me = ob.data.copy()
        en = ob.get("export_name")
        if en:
            me.transform(R)
            o = bpy.data.objects.new(en, me)
            o.location = R @ ob.matrix_world.translation
            named.append(o)
        else:
            me.transform(R @ ob.matrix_world)
            o = bpy.data.objects.new(ob.name + "_x", me)
            parts.append(o)
        exp.objects.link(o)
    with bpy.context.temp_override(active_object=parts[0], selected_objects=parts,
                                   selected_editable_objects=parts, object=parts[0]):
        bpy.ops.object.join()
    body = parts[0]
    body.name = "body"
    body.data.name = CARS[cid].get("mesh_name", cid) + "_Body"
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in [body] + named + markers:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    path = os.path.join(folder, CARS[cid].get("file", cid) + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='AUTO', embed_textures=False)
    stats = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in [body] + named}
    stats.update({o.name: "repère" for o in markers})
    for o in list(exp.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    bpy.data.collections.remove(exp)
    return path, stats
