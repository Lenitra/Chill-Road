"""Végétation au sol « Flora » (v4) : herbes, fleurs, fougères, buissons bas et plantes de lac, dans la direction
artistique du champ d'herbe (shader NoHesi/Grass) : formes larges et simples, cartoon, qui se fondent dans le pré.
- brins et feuilles larges (4 à 6 cm), même profil arrondi que les brins du champ ; tiges épaisses ;
- fleurs à grosses têtes simples qui dépassent de l'herbe ; au loin, quelques taches de couleur élargies ;
- les parties vertes prennent la couleur du pré (UV2.x), les plantes s'éclairent comme lui, d'une nappe (UV2.y).

Chaîne propre, indépendante de la palette du pack alpin (AP_Palette) :
- palette dédiée AlpFlora_Palette.png (64 colonnes FIXES : on n'ajoute qu'à la fin), dégradé base → pointe ;
- UV0 = (colonne, t le long de l'élément : 0 à la base, 1 à la pointe) → chaque brin, pétale ou fronde a son dégradé ;
- UV1 = (souplesse au vent en m : flex × s², s = longueur depuis le sol le long de la tige ; phase 0-1) ;
- UV2 = (sol : part de la couleur du champ d'herbe ; vol : part de la normale propre dans l'éclairage) ;
- normales douces (dômes, sphères des buissons) écrites en normales personnalisées : aucun facettage ;
- faces simples : le shader NoHesi/Foliage dessine les deux côtés ;
- matériau AP_Flora → Unity : NoHesi/Foliage (aplats cartoon, couleur du pré, vent du champ d'herbe, fondu).
Mêmes ids et fichiers que le pack alpin (Models/AlpPack/<File>.fbx avec <File>_LOD0..2), plus de nouvelles plantes.

Usage (Blender, aussi en arrière-plan : blender -b --python ...) :
    import sys; sys.path.insert(0, r"E:\\ChillRoad\\Tools\\Blender")
    import alp_flora as F
    F.build_all(); F.export_all(); F.merge_manifest()      # ou F.deliver()
"""
import bpy, bmesh, math, os, random, json, zlib
from mathutils import Vector, Matrix

OUT = r"E:\ChillRoad\Assets\NoHesi\Models\AlpPack"
RENDERS = r"E:\ChillRoad\Tools\Blender\Rendus\Flora"
PALETTE_FILE = "AlpFlora_Palette.png"
MATERIAL = "AP_Flora"
ROOT = "FL_Items"
NCOL = 64
PAL_W = 8
UP = Vector((0.0, 0.0, 1.0))
TAU = 6.283185307

# colonnes : (nom, base sRGB, pointe sRGB). NE JAMAIS réordonner : ajouter à la fin.
PALETTE = [
    ("grass", (0.15, 0.33, 0.10), (0.47, 0.62, 0.25)),          # pied et pointe des brins du champ (NoHesi/Grass)
    ("grass_lush", (0.12, 0.28, 0.10), (0.40, 0.57, 0.24)),
    ("grass_yellow", (0.20, 0.35, 0.10), (0.60, 0.70, 0.28)),
    ("grass_dry", (0.33, 0.37, 0.16), (0.80, 0.80, 0.42)),
    ("grass_blue", (0.10, 0.24, 0.17), (0.42, 0.60, 0.46)),
    ("seed", (0.52, 0.44, 0.22), (0.93, 0.82, 0.50)),
    ("seed_purple", (0.42, 0.26, 0.34), (0.80, 0.56, 0.62)),
    ("stem", (0.15, 0.33, 0.11), (0.44, 0.64, 0.25)),
    ("leaf", (0.13, 0.31, 0.11), (0.42, 0.63, 0.25)),
    ("leaf_dark", (0.08, 0.23, 0.10), (0.29, 0.50, 0.21)),
    ("leaf_light", (0.20, 0.40, 0.12), (0.56, 0.74, 0.30)),
    ("leaf_grey", (0.38, 0.46, 0.38), (0.74, 0.82, 0.70)),
    ("fern", (0.08, 0.25, 0.10), (0.27, 0.50, 0.20)),
    ("fern_light", (0.13, 0.31, 0.11), (0.40, 0.62, 0.25)),
    ("petal_white", (0.86, 0.86, 0.80), (1.00, 1.00, 0.97)),
    ("petal_yellow", (0.96, 0.66, 0.04), (1.00, 0.88, 0.22)),
    ("petal_pink", (0.84, 0.36, 0.56), (1.00, 0.68, 0.82)),
    ("petal_magenta", (0.66, 0.12, 0.46), (0.94, 0.38, 0.70)),
    ("petal_blue", (0.10, 0.18, 0.62), (0.22, 0.38, 0.96)),
    ("petal_violet", (0.38, 0.28, 0.74), (0.70, 0.60, 0.98)),
    ("lupin_purple", (0.34, 0.18, 0.66), (0.74, 0.54, 0.96)),
    ("lupin_pink", (0.72, 0.20, 0.50), (1.00, 0.66, 0.84)),
    ("lupin_blue", (0.18, 0.26, 0.72), (0.56, 0.70, 1.00)),
    ("lupin_white", (0.80, 0.78, 0.84), (1.00, 0.98, 0.96)),
    ("clover_head", (0.74, 0.30, 0.52), (0.99, 0.70, 0.84)),
    ("clover_white", (0.82, 0.82, 0.74), (1.00, 0.98, 0.94)),
    ("center_yellow", (0.90, 0.56, 0.04), (1.00, 0.82, 0.16)),
    ("felt", (0.80, 0.82, 0.78), (0.99, 0.99, 0.96)),
    ("felt_center", (0.74, 0.68, 0.36), (0.96, 0.90, 0.60)),
    ("rhodo_leaf", (0.08, 0.24, 0.12), (0.30, 0.54, 0.24)),
    ("rhodo_flower", (0.76, 0.10, 0.38), (1.00, 0.44, 0.64)),
    ("juniper", (0.09, 0.26, 0.20), (0.36, 0.60, 0.46)),
    ("juniper_tip", (0.18, 0.38, 0.28), (0.58, 0.78, 0.58)),
    ("berry", (0.08, 0.12, 0.34), (0.32, 0.42, 0.74)),
    ("twig", (0.28, 0.19, 0.10), (0.50, 0.37, 0.20)),
    ("twig_red", (0.38, 0.14, 0.09), (0.62, 0.34, 0.17)),
    ("reed", (0.22, 0.36, 0.12), (0.62, 0.74, 0.32)),
    ("cattail", (0.26, 0.14, 0.06), (0.50, 0.30, 0.14)),
    ("lily_pad", (0.08, 0.26, 0.12), (0.30, 0.56, 0.24)),
    ("lily_white", (0.88, 0.86, 0.86), (1.00, 0.99, 0.98)),
    ("lily_pink", (0.88, 0.48, 0.64), (1.00, 0.84, 0.90)),
    ("clover_leaf", (0.13, 0.33, 0.12), (0.42, 0.66, 0.26)),
    ("bark", (0.20, 0.14, 0.08), (0.42, 0.30, 0.17)),
    ("poppy", (0.84, 0.10, 0.06), (1.00, 0.30, 0.14)),
    ("center_dark", (0.07, 0.06, 0.05), (0.24, 0.20, 0.14)),
    ("leaf_yellow", (0.34, 0.44, 0.10), (0.72, 0.80, 0.30)),
]
assert len(PALETTE) <= NCOL
CI = {p[0]: i for i, p in enumerate(PALETTE)}
ITEMS = {}


def item(iid, file, build, cat="nature", tags=("plant", "detail"), lods=3, **kw):
    d = dict(id=iid, file=file, build=build, cat=cat, tags=list(tags), lods=lods,
             seed=kw.pop("seed", zlib.crc32(iid.encode()) & 0xffff))
    d.update(kw)
    ITEMS[iid] = d
    return d


# ----------------------------------------------------------------- outils géométriques
def basis(n):
    n = Vector(n).normalized()
    a = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
    e1 = n.cross(a).normalized()
    return e1, n.cross(e1)


def arc(root, az, length, tilt, bend, segs, power=1.4):
    """Courbe dans un plan vertical d'azimut az : angle à la verticale de tilt (base) à tilt + bend (pointe)."""
    p = Vector(root)
    pts = [p.copy()]
    step = length / segs
    for i in range(segs):
        u = (i + 0.5) / segs
        th = tilt + bend * u ** power
        p = p + Vector((math.sin(th) * math.cos(az), math.sin(th) * math.sin(az), math.cos(th))) * step
        pts.append(p.copy())
    return pts


def sample(pts, u):
    """Point et tangente à la fraction u de la longueur d'une polyligne."""
    acc = [0.0]
    for a, b in zip(pts, pts[1:]):
        acc.append(acc[-1] + (b - a).length)
    s = max(0.0, min(1.0, u)) * acc[-1]
    for i in range(1, len(pts)):
        if acc[i] >= s or i == len(pts) - 1:
            seg = max(acc[i] - acc[i - 1], 1e-9)
            k = (s - acc[i - 1]) / seg
            return pts[i - 1].lerp(pts[i], k), (pts[i] - pts[i - 1]).normalized(), s
    return pts[-1], (pts[-1] - pts[-2]).normalized(), acc[-1]


def length_of(pts):
    return sum((b - a).length for a, b in zip(pts, pts[1:]))


# profils de largeur (fraction de la largeur) le long de l'élément, u de 0 (base) à 1 (pointe)
def p_grass(u):
    return (1.0 - u) ** 0.75 * (0.82 + 0.18 * min(1.0, u * 4.0))


def p_leaf(u):
    return max(math.sin(math.pi * (0.08 + 0.92 * u)), 0.0) ** 0.75


def p_lance(u):
    return max(math.sin(math.pi * (0.05 + 0.95 * u) ** 0.8), 0.0) ** 0.7


def p_obovate(u):
    return 0.18 + 0.82 * math.sin(math.pi * 0.62 * u) / math.sin(math.pi * 0.62)


def p_ligule(u):
    return 0.42 if u >= 0.999 else 0.45 + 0.55 * math.sin(math.pi * u)


def p_round(u):
    return 0.28 + 0.72 * math.sin(math.pi * 0.5 * min(u * 1.15, 1.0)) if u < 0.999 else 0.6


def p_pointed(u):
    return max(math.sin(math.pi * (0.1 + 0.9 * u) ** 0.9), 0.0) ** 0.6


def p_spindle(u):
    return math.sin(math.pi * u)


def p_needle(u):
    return 0.15 + 0.85 * (1.0 - u) ** 0.6 if u < 0.999 else 0.0


def p_strap(u):
    return 1.0 - 0.75 * u ** 2.2


def p_field(u):
    """Profil des brins du champ d'herbe (Grass.shader) : feuille effilée, un peu plus large au tiers de sa hauteur."""
    return max(0.0, 1.0 - u * u) * (0.85 + 0.6 * u)


def p_penne(u):
    return max(0.0, 1.0 - u ** 1.6)


def p_reed(u):
    return max(0.0, 1.0 - u ** 3)


# Rangées de sommets le long d'un brin large (fraction de la longueur) : serrées vers la pointe, comme le champ
ROWS = {4: (0.0, 0.35, 0.65, 0.88, 1.0), 3: (0.0, 0.4, 0.75, 1.0), 2: (0.0, 0.55, 1.0), 1: (0.0, 1.0)}


# ----------------------------------------------------------------- constructeur
class FGeo:
    """bmesh à couleur de palette par face ; par sommet : t (dégradé), souplesse au vent, phase, normale douce,
    style (sol : part de la couleur du champ d'herbe, vol : part de la normale propre dans l'éclairage)."""

    def __init__(self, level=0, seed=0):
        bm = self.bm = bmesh.new()
        self.lc = bm.faces.layers.int.new("col")
        self.lt = bm.verts.layers.float.new("t")
        self.lw = bm.verts.layers.float.new("w")
        self.lp = bm.verts.layers.float.new("ph")
        self.ln = [bm.verts.layers.float.new(k) for k in ("nx", "ny", "nz")]
        self.l_sol = bm.verts.layers.float.new("sol")
        self.l_vol = bm.verts.layers.float.new("vol")
        self.sol, self.vol = 0.0, 0.3                 # style courant (voir style())
        self.balls = []                               # boules des masses de buisson : (centre, rayon, aplat, centre de la masse)
        self.level = level
        self.seed = seed
        self.rng = random.Random(seed * 31 + level)
        self.dome = Vector((0, 0, -0.3))     # centre des normales douces (sous la touffe)
        self.up = 0.7                         # part de la verticale ajoutée aux normales douces
        self.soft = 0.85                      # 1 : normales douces seules, 0 : normales géométriques

    def R(self, a=0.0, b=1.0):
        return a + (b - a) * self.rng.random()

    def lv(self, *v):
        return v[min(self.level, len(v) - 1)]

    def style(self, sol, vol):
        """Style des sommets créés ensuite : sol = part de la couleur du champ d'herbe, vol = part de la normale
        propre dans l'éclairage (0 : éclairé comme le pré)."""
        self.sol, self.vol = sol, vol

    def soft_normal(self, co, geo=None, soft=None, nfix=None):
        s = self.soft if soft is None else soft
        if nfix is not None:
            n = Vector(nfix(co) if callable(nfix) else nfix).normalized()
        else:
            d = co - self.dome
            n = (d.normalized() if d.length > 1e-6 else UP.copy()) + UP * self.up
            n.normalize()
        if geo is not None and s < 0.999 and geo.length > 1e-6:
            g = geo.normalized()
            if g.dot(n) < 0.0:
                g = -g
            n = (g * (1.0 - s) + n * s).normalized()
        return n

    def vert(self, co, t, s, flex, ph, n):
        v = self.bm.verts.new(co)
        v[self.lt] = t
        v[self.lw] = flex * s * s
        v[self.lp] = ph
        for k in range(3):
            v[self.ln[k]] = n[k]
        v[self.l_sol] = self.sol
        v[self.l_vol] = self.vol
        return v

    def poly(self, verts, col):
        f = self.bm.faces.new(verts)
        f[self.lc] = CI[col]
        return f

    def bridge(self, A, B, col):
        fs = []
        if len(A) == 1 and len(B) == 1:
            return fs
        if len(A) == len(B):
            for k in range(len(A) - 1):
                fs.append(self.poly((A[k], A[k + 1], B[k + 1], B[k]), col))
        elif len(B) == 1:
            for k in range(len(A) - 1):
                fs.append(self.poly((A[k], A[k + 1], B[0]), col))
        elif len(A) == 1:
            for k in range(len(B) - 1):
                fs.append(self.poly((A[0], B[k + 1], B[k]), col))
        else:                                       # 3 → 2 ou 2 → 3 : on ramène sur les bords
            a, b = (A, B) if len(A) > len(B) else (B, A)
            fs.append(self.poly((a[0], a[1], b[0]), col))
            fs.append(self.poly((a[1], a[2], b[1], b[0]), col))
        return fs

    # --- rubans (brins, feuilles, pétales)
    def strip(self, pts, widths, sides, col, s0=0.0, flex=1.0, ph=0.0, t0=0.0, t1=1.0, tlist=None, fold=0.0,
              soft=None, nfix=None, min_w=1e-4):
        n = len(pts)
        acc = [0.0]
        for i in range(1, n):
            acc.append(acc[-1] + (pts[i] - pts[i - 1]).length)
        total = acc[-1] or 1.0
        rows = []
        for i, p in enumerate(pts):
            t = tlist[i] if tlist else t0 + (t1 - t0) * acc[i] / total
            tang = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)])
            tang = tang.normalized() if tang.length > 1e-9 else UP.copy()
            side = Vector(sides[i] if isinstance(sides, (list, tuple)) and not isinstance(sides[0], (int, float)) else sides)
            gn = tang.cross(side)
            s = s0 + acc[i]
            w = widths[i]
            if w <= min_w:
                rows.append([self.vert(p, t, s, flex, ph, self.soft_normal(p, gn, soft, nfix))])
                continue
            l, r = p - side * (w * 0.5), p + side * (w * 0.5)
            qs = [l, r]
            if fold > 0.0:
                gu = gn.normalized() if gn.length > 1e-9 else UP.copy()
                if gu.z < 0.0:
                    gu = -gu
                qs = [l + gu * fold * w, p, r + gu * fold * w]       # gouttière : bords relevés, nervure en creux
            rows.append([self.vert(q, t, s, flex, ph, self.soft_normal(q, gn, soft, nfix)) for q in qs])
        fs = []
        for A, B in zip(rows, rows[1:]):
            fs += self.bridge(A, B, col)
        return fs

    def blade(self, root, az, length, width, col, tilt=0.2, bend=0.5, segs=3, twist=0.0, profile=p_grass,
              s0=0.0, flex=1.0, ph=None, fold=0.0, soft=None, nfix=None, t0=0.0, t1=1.0, power=1.4):
        """Brin courbé dans un plan vertical, face tournée vers l'extérieur ; twist : torsion (rad) à la pointe."""
        pts = arc(root, az, length, tilt, bend, segs, power)
        side0 = Vector((-math.sin(az), math.cos(az), 0.0))
        sides, widths = [], []
        for i, p in enumerate(pts):
            u = i / segs
            tang = (pts[min(i + 1, segs)] - pts[max(i - 1, 0)]).normalized()
            sides.append((Matrix.Rotation(twist * u, 3, tang) @ side0).normalized() if twist else side0)
            widths.append(width * profile(u))
        if widths[-1] < width * 0.03:
            widths[-1] = 0.0
        return self.strip(pts, widths, sides, col, s0, flex, self.R() if ph is None else ph, t0, t1, fold=fold,
                          soft=soft, nfix=nfix)

    def brin(self, root, az, length, width, col, tilt=0.2, bend=0.5, rows=4, twist=0.0, profile=p_field, s0=0.0,
             flex=1.0, ph=None, soft=None, nfix=None, t0=0.0, t1=1.0, power=1.4):
        """Brin large du pré : même forme que les brins du champ d'herbe (rangées serrées vers la pointe, bout
        arrondi puis pointu), courbé dans un plan vertical d'azimut az, face tournée vers l'extérieur."""
        fine = arc(root, az, length, tilt, bend, 8, power)
        us = ROWS[rows]
        side0 = Vector((-math.sin(az), math.cos(az), 0.0))
        pts, sides, widths = [], [], []
        for u in us:
            p, tang, _ = sample(fine, u)
            pts.append(p)
            sides.append((Matrix.Rotation(twist * u, 3, tang) @ side0).normalized() if twist else side0)
            widths.append(width * profile(u))
        if widths[-1] < width * 0.03:
            widths[-1] = 0.0
        return self.strip(pts, widths, sides, col, s0, flex, self.R() if ph is None else ph,
                          tlist=[t0 + (t1 - t0) * u for u in us], soft=soft, nfix=nfix)

    def ribbon_dir(self, root, d, length, width, col, normal, segs=2, droop=0.0, curl=0.0, profile=p_leaf, s0=0.0,
                   flex=1.0, ph=0.0, fold=0.0, soft=None, nfix=None, t0=0.0, t1=1.0, min_w=1e-4):
        """Feuille / pétale à plat dans le plan (d, side) de normale « normal » ; droop : retombée vers -normal,
        curl : relevé vers +normal (cuillère)."""
        d = Vector(d).normalized()
        nrm = Vector(normal)
        nrm = (nrm - d * nrm.dot(d)).normalized()
        side = nrm.cross(d).normalized()
        root = Vector(root)
        pts, widths = [], []
        for i in range(segs + 1):
            u = i / segs
            pts.append(root + d * (length * u) + nrm * (length * (curl * u * u - droop * u * u)))
            widths.append(width * profile(u))
        if widths[-1] < width * 0.03:
            widths[-1] = 0.0
        return self.strip(pts, widths, [side] * len(pts), col, s0, flex, ph, t0, t1, fold=fold, soft=soft,
                          nfix=nfix, min_w=min_w)

    # --- volumes
    def tube(self, pts, radii, col, sides=3, s0=0.0, flex=1.0, ph=0.0, t0=0.0, t1=1.0, soft=0.6, cap=False, rot=0.0):
        """Tige : anneaux de « sides » sommets le long d'une polyligne ; normales radiales adoucies."""
        n = len(pts)
        acc = [0.0]
        for i in range(1, n):
            acc.append(acc[-1] + (pts[i] - pts[i - 1]).length)
        total = acc[-1] or 1.0
        rings = []
        px = None
        for i, p in enumerate(pts):
            tang = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            if px is None:
                ref = UP if abs(tang.z) < 0.9 else Vector((1, 0, 0))
                px = tang.cross(ref).normalized()
            else:
                px = (px - tang * px.dot(tang))
                px = px.normalized() if px.length > 1e-6 else tang.orthogonal().normalized()
            py = tang.cross(px)
            t = t0 + (t1 - t0) * acc[i] / total
            s = s0 + acc[i]
            if radii[i] <= 1e-5:
                rings.append([self.vert(p, t, s, flex, ph, self.soft_normal(p, tang, 0.3))])
                continue
            ring = []
            for k in range(sides):
                a = rot + TAU * k / sides
                rd = px * math.cos(a) + py * math.sin(a)
                q = p + rd * radii[i]
                ring.append(self.vert(q, t, s, flex, ph, self.soft_normal(q, rd, soft)))
            rings.append(ring)
        fs = []
        for A, B in zip(rings, rings[1:]):
            if len(A) == 1 and len(B) == 1:
                continue
            for k in range(sides):
                j = (k + 1) % sides
                if len(B) == 1:
                    fs.append(self.poly((A[k], A[j], B[0]), col))
                elif len(A) == 1:
                    fs.append(self.poly((A[0], B[j], B[k]), col))
                else:
                    fs.append(self.poly((A[k], A[j], B[j], B[k]), col))
        if cap and len(rings[-1]) > 2:
            fs.append(self.poly(rings[-1], col))
        return fs

    def ball(self, c, r, col, sub=1, s0=0.0, flex=1.0, ph=0.0, t=None, scale=(1, 1, 1), soft=0.95, tz=None, nfun=None):
        """Boule (sub 0 : octaèdre, 1 : icosaèdre, 2 : 80 faces) à normales sphériques : paraît ronde."""
        c = Vector(c)
        bm = self.bm
        before = set(bm.verts)
        if sub <= 0:
            vs = [bm.verts.new(c + Vector((d[0] * r * scale[0], d[1] * r * scale[1], d[2] * r * scale[2])))
                  for d in ((1, 0, 0), (0, 1, 0), (-1, 0, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))]
            faces = []
            for i in range(4):
                j = (i + 1) % 4
                faces.append(bm.faces.new((vs[i], vs[j], vs[4])))
                faces.append(bm.faces.new((vs[j], vs[i], vs[5])))
        else:
            m = Matrix.Translation(c) @ Matrix.Diagonal((r * scale[0], r * scale[1], r * scale[2], 1.0))
            res = bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0, matrix=m)
            vs = res["verts"]
            faces = list({f for v in vs for f in v.link_faces})
        zs = [v.co.z for v in vs]
        z0, z1 = min(zs), max(zs)
        for v in vs:
            d = v.co - c
            n = Vector((d.x / max(scale[0], 1e-3), d.y / max(scale[1], 1e-3), d.z / max(scale[2], 1e-3))).normalized()
            n = (n * soft + self.soft_normal(v.co) * (1 - soft)).normalized()
            if nfun is not None:
                n = nfun(v.co, n)
            tv = t if t is not None else (0.15 + 0.85 * (v.co.z - z0) / max(z1 - z0, 1e-6))
            if tz is not None:
                tv = tz(v.co)
            v[self.lt] = tv
            s = s0 + max(d.length, 0.0)
            v[self.lw] = flex * s * s
            v[self.lp] = ph
            for k in range(3):
                v[self.ln[k]] = n[k]
            v[self.l_sol] = self.sol
            v[self.l_vol] = self.vol
        for f in faces:
            f[self.lc] = CI[col]
        return faces

    def fan(self, c, nrm, r, n, col, height=0.0, s0=0.0, flex=1.0, ph=0.0, t_rim=0.2, t_mid=1.0, rot=0.0,
            star=0.0):
        """Disque bombé (cœur de fleur, nénuphar) : centre relevé de height ; star > 0 : bord étoilé."""
        c, nrm = Vector(c), Vector(nrm).normalized()
        e1, e2 = basis(nrm)
        sph = c - nrm * max(r, 1e-4) * 1.2
        mid = self.vert(c + nrm * height, t_mid, s0, flex, ph, nrm)
        rim = []
        for k in range(n):
            a = rot + TAU * k / n
            rr = r * (1.0 + (star if k % 2 == 0 else -star * 0.5)) if star else r
            q = c + (e1 * math.cos(a) + e2 * math.sin(a)) * rr
            rim.append(self.vert(q, t_rim, s0, flex, ph, ((q - sph).normalized() + nrm).normalized()))
        return [self.poly((rim[k], rim[(k + 1) % n], mid), col) for k in range(n)]

    def bell(self, c, axis, length, r0, r1, col, lobes=5, rings=3, flare=1.8, lobe=0.3, s0=0.0, flex=1.0, ph=0.0,
             t0=0.0, t1=1.0, rot=0.0, flat_rim=0.0):
        """Corolle en cloche / trompette ouverte vers « axis », bord à lobes pointus (2 × lobes sommets)."""
        c, ax = Vector(c), Vector(axis).normalized()
        e1, e2 = basis(ax)
        sides = lobes * 2
        rows = []
        for i in range(rings + 1):
            u = i / rings
            r = r0 + (r1 - r0) * u ** flare
            row = []
            for k in range(sides):
                a = rot + TAU * k / sides
                rd = e1 * math.cos(a) + e2 * math.sin(a)
                rr, du = r, 0.0
                if i == rings:
                    tip = k % 2 == 0
                    rr = r * (1.0 + lobe) if tip else r * (1.0 - lobe * 0.35)
                    du = length * (0.08 if tip else -0.04)
                    rr += r * flat_rim * (1.0 if tip else 0.4)
                q = c + ax * (length * u + du) + rd * rr
                n = (rd * 0.8 + ax * 0.6).normalized()
                row.append(self.vert(q, t0 + (t1 - t0) * u, s0 + length * u, flex, ph, n))
            rows.append(row)
        fs = []
        for A, B in zip(rows, rows[1:]):
            for k in range(sides):
                j = (k + 1) % sides
                fs.append(self.poly((A[k], A[j], B[j], B[k]), col))
        return fs

    def petals(self, c, nrm, n, length, width, col, cup=0.0, droop=0.0, segs=2, profile=p_ligule, s0=0.0, flex=1.0,
               ph=0.0, rot=0.0, inner=0.0, jitter=0.12, t0=0.0, t1=1.0, fold=0.0, tilt=0.0):
        """Couronne de pétales autour de c, ouverte vers nrm ; cup > 0 : coupe relevée ; tilt : inclinaison des
        pétales vers nrm dès la base (rad)."""
        c, nrm = Vector(c), Vector(nrm).normalized()
        e1, e2 = basis(nrm)
        fs = []
        for k in range(n):
            a = rot + TAU * k / n + self.R(-jitter, jitter)
            o = e1 * math.cos(a) + e2 * math.sin(a)
            d = (o * math.cos(tilt) + nrm * math.sin(tilt)).normalized()
            pn = (nrm * math.cos(tilt) - o * math.sin(tilt)).normalized()
            L = length * self.R(0.9, 1.08)
            fs += self.ribbon_dir(c + o * inner, d, L, width * self.R(0.9, 1.1), col, pn, segs, droop=droop, curl=cup,
                                  profile=profile, s0=s0, flex=flex, ph=ph, fold=fold, soft=0.75,
                                  nfix=(pn + nrm * 0.6).normalized(), t0=t0, t1=t1)
        return fs

    def clump_cloud(self, c, radii, n, ball_r, col, s0=0.3, flex=0.12, seed=0, zmin=-0.3, zrange=None, sub=1,
                    own=0.3, inset=0.8, core=True, squash=1.0):
        """Masse festonnée de boules (chou-fleur) dont les normales suivent surtout le nuage : silhouette bosselée,
        ombrage doux d'un seul tenant."""
        rr = random.Random(seed)
        c = Vector(c)
        z0, z1 = zrange or (c.z - radii[2], c.z + radii[2])
        tz = lambda co: max(0.02, min(0.98, (co.z - z0) / max(z1 - z0, 1e-6)))

        def nfun(co, n_own):
            d = co - c
            nc = Vector((d.x / radii[0] ** 2, d.y / radii[1] ** 2, d.z / radii[2] ** 2))
            nc = nc.normalized() if nc.length > 1e-9 else UP.copy()
            return (n_own * own + nc * (1.0 - own) + UP * 0.15).normalized()

        golden = math.pi * (3.0 - math.sqrt(5.0))
        fs = []
        for i in range(n):
            z = 1.0 - 2.0 * (i + 0.5) / n
            if z < zmin:
                continue
            rad = math.sqrt(max(0.0, 1.0 - z * z))
            phi = i * golden + rr.uniform(-0.35, 0.35)
            sn = Vector((math.cos(phi) * rad, math.sin(phi) * rad, z))
            k = inset + rr.uniform(-0.05, 0.08)
            p = c + Vector((sn.x * radii[0] * k, sn.y * radii[1] * k, sn.z * radii[2] * k))
            r = ball_r * rr.uniform(0.8, 1.2)
            self.balls.append((p.copy(), r, squash, c.copy()))
            fs += self.ball(p, r, col, sub, s0, flex, rr.random(), scale=(1, 1, squash), tz=tz, nfun=nfun)
        if core:
            fs += self.ball(c, 1.0, col, 1, s0, flex, rr.random(),
                            scale=(radii[0] * inset, radii[1] * inset, radii[2] * inset), tz=tz, nfun=nfun)
        return fs

    def rosette(self, c, nrm, r, n, col, cup=0.25, sinus=0.3, rot=0.0, tz=None, nfun=None, s0=0.0, flex=0.1,
                ph=0.0, width=0.5, depth=0.15):
        """Rosette de n feuilles pointues en un seul éventail (2 sommets par feuille : pointe, creux) ; cup relève
        les pointes, depth enfonce le centre. Silhouette feuillue pour très peu de triangles."""
        c, nrm = Vector(c), Vector(nrm).normalized()
        e1, e2 = basis(nrm)
        mid_co = c - nrm * r * depth
        n_mid = nfun(mid_co, nrm) if nfun else nrm
        mid = self.vert(mid_co, tz(mid_co) if tz else 0.2, s0, flex, ph, n_mid)
        rim = []
        for k in range(n):
            for half in (0, 1):
                a = rot + TAU * (k + 0.5 * half) / n + (self.R(-0.12, 0.12) if half == 0 else 0.0)
                o = e1 * math.cos(a) + e2 * math.sin(a)
                if half == 0:
                    q = c + o * r * self.R(0.85, 1.12) + nrm * r * cup
                else:
                    q = c + o * r * sinus * (0.7 + width * 0.6)
                nq = nfun(q, (nrm + o * 0.3).normalized()) if nfun else (nrm + o * 0.3).normalized()
                rim.append(self.vert(q, tz(q) if tz else (0.95 if half == 0 else 0.5), s0 + r, flex, ph, nq))
        m = len(rim)
        return [self.poly((rim[k], rim[(k + 1) % m], mid), col) for k in range(m)]

    def rosette_cloud(self, c, radii, n, r, cols, seed=0, zmin=-0.25, zrange=None, leaves=7, cup=0.25, sinus=0.3,
                      s0=0.3, flex=0.12, core=None, core_sub=2, inset=0.95, top_cols=None, own=0.25, width=0.5):
        """Buisson : rosettes de feuilles réparties sur un ellipsoïde (normales du nuage → ombrage doux), noyau
        sombre facultatif qui bouche les trous."""
        rr = random.Random(seed)
        c = Vector(c)
        z0, z1 = zrange or (c.z - radii[2], c.z + radii[2])
        tz = lambda co: max(0.02, min(0.98, (co.z - z0) / max(z1 - z0, 1e-6)))

        def nfun(co, n_own):
            d = co - c
            nc = Vector((d.x / radii[0] ** 2, d.y / radii[1] ** 2, d.z / radii[2] ** 2))
            nc = nc.normalized() if nc.length > 1e-9 else UP.copy()
            return (n_own * own + nc * (1.0 - own) + UP * 0.15).normalized()

        golden = math.pi * (3.0 - math.sqrt(5.0))
        fs = []
        for i in range(n):
            z = 1.0 - 2.0 * (i + 0.5) / n
            if z < zmin:
                continue
            rad = math.sqrt(max(0.0, 1.0 - z * z))
            phi = i * golden + rr.uniform(-0.3, 0.3)
            sn = Vector((math.cos(phi) * rad, math.sin(phi) * rad, z))
            k = inset + rr.uniform(-0.06, 0.06)
            p = c + Vector((sn.x * radii[0] * k, sn.y * radii[1] * k, sn.z * radii[2] * k))
            nsph = Vector((sn.x / radii[0], sn.y / radii[1], sn.z / radii[2])).normalized()
            tilt = Vector((rr.uniform(-0.35, 0.35), rr.uniform(-0.35, 0.35), rr.uniform(0.0, 0.3)))
            col = cols[rr.randrange(len(cols))]
            if top_cols and z > 0.4 and rr.random() < 0.45:
                col = top_cols[rr.randrange(len(top_cols))]
            fs += self.rosette(p, (nsph + tilt).normalized(), r * rr.uniform(0.85, 1.15), leaves, col, cup, sinus,
                               rr.uniform(0, TAU), tz, nfun, s0 + (p - c).length, flex, rr.random(), width)
        if core:
            fs += self.ball(c, 1.0, core, core_sub, s0, flex * 0.5, rr.random(),
                            scale=(radii[0] * 0.86, radii[1] * 0.86, radii[2] * 0.86), tz=tz, nfun=nfun)
        return fs

    def leaf_cloud(self, c, radii, n, size, width, cols, s0=0.3, flex=0.2, seed=0, core=None, core_sub=2,
                   zmin=-0.35, tilt=(0.35, 0.85), profile=p_leaf, zrange=None, core_scale=0.82, bump=0.12,
                   droop=0.15, top_cols=None):
        """Nuage de feuilles posées sur un ellipsoïde (normales sphériques) + noyau sombre qui bouche les trous.
        zrange : (zbas, zhaut) du dégradé de couleur commun à tout le buisson."""
        rr = random.Random(seed)
        c = Vector(c)
        golden = math.pi * (3.0 - math.sqrt(5.0))
        z0, z1 = zrange or (c.z - radii[2], c.z + radii[2])
        tz = lambda co: max(0.02, min(0.98, (co.z - z0) / max(z1 - z0, 1e-6)))
        fs = []
        for i in range(n):
            z = 1.0 - 2.0 * (i + 0.5) / n
            if z < zmin:
                continue
            rad = math.sqrt(max(0.0, 1.0 - z * z))
            phi = i * golden + rr.uniform(-0.25, 0.25)
            sn = Vector((math.cos(phi) * rad, math.sin(phi) * rad, z))
            k = 1.0 + bump * rr.uniform(-1, 1)
            p = c + Vector((sn.x * radii[0] * k, sn.y * radii[1] * k, sn.z * radii[2] * k))
            nsph = Vector((sn.x / radii[0], sn.y / radii[1], sn.z / radii[2])).normalized()
            tdir = nsph.orthogonal().normalized()
            tdir = Matrix.Rotation(rr.uniform(0, TAU), 3, nsph) @ tdir
            # les feuilles pointent plutôt vers le haut et l'extérieur
            tdir = (tdir + UP * 0.35).normalized()
            tdir = (tdir - nsph * tdir.dot(nsph)).normalized()
            ang = rr.uniform(*tilt)
            d = (tdir * math.cos(ang) + nsph * math.sin(ang)).normalized()
            pn = (nsph - d * nsph.dot(d)).normalized()
            L = size * rr.uniform(0.8, 1.2)
            col = cols[rr.randrange(len(cols))]
            if top_cols and z > 0.45 and rr.random() < 0.5:
                col = top_cols[rr.randrange(len(top_cols))]
            nn = (nsph * 0.85 + UP * 0.25).normalized()
            m = len(self.bm.verts)
            f = self.ribbon_dir(p - d * L * 0.25, d, L, width * rr.uniform(0.85, 1.15), col, pn, 2, droop=droop,
                                profile=profile, s0=s0 + (p - c).length, flex=flex, ph=rr.random(), soft=0.9,
                                nfix=nn)
            for q in f:
                for v in q.verts:
                    v[self.lt] = tz(v.co)
            fs += f
        if core:
            fs += self.ball(c, 1.0, core, core_sub, s0, flex * 0.5, rr.random(),
                            scale=(radii[0] * core_scale, radii[1] * core_scale, radii[2] * core_scale), tz=tz)
        return fs


# ----------------------------------------------------------------- éléments composés
# Styles (UV2 = sol, vol) :
# - sol : part de la couleur du champ d'herbe (dégradé pied → pointe que le shader calcule à partir de la couleur du
#   sol, comme NoHesi/Grass) ; 0 = couleur de la palette seule (pétales, buissons, plantes de lac) ;
# - vol : part de la normale propre dans l'éclairage ; 0 = la plante s'éclaire comme le pré, d'une seule nappe.
S_HERBE = (1.0, 0.15)
S_FEUILLE = (0.7, 0.25)
S_TIGE = (0.6, 0.25)
S_FLEUR = (0.0, 0.5)
S_FOUGERE = (0.0, 0.45)
S_BUISSON = (0.0, 1.0)
S_BAIE = (0.0, 0.8)
S_LAC = (0.0, 0.5)


def lin(a, b, n):
    return [a + (b - a) * i / max(n - 1, 1) for i in range(n)]


def sur_boule(g, rs, k, out=1.0, up=0.6):
    """Point à la surface d'une boule de buisson (FGeo.balls), du côté extérieur et plutôt vers le haut, et sa
    normale : pour y poser fleurs et baies bien en vue."""
    p, r, sq, c = g.balls[k % len(g.balls)]
    d = p - c
    n = (d.normalized() if d.length > 1e-6 else UP.copy()) + UP * up
    n += Vector((rs.uniform(-0.5, 0.5), rs.uniform(-0.5, 0.5), rs.uniform(-0.2, 0.4)))
    n.normalize()
    return p + Vector((n.x * r, n.y * r, n.z * r * sq)) * out, n


def touffe(g, n, radius, lmin, lmax, wmin, wmax, cols, seed, keep=(1.0, 0.46, 0.22), rows=(4, 2, 1),
           widen=(1.0, 1.5, 2.4), flex=2.6, spread=0.5, arch=(0.2, 0.75), z=-0.02, twist=0.5):
    """Touffe de brins larges ouverte vers l'extérieur, comme les touffes du champ d'herbe (NoHesi/Grass) : plus
    haute au centre, brins tournés vers le dehors. Les niveaux lointains gardent une partie des mêmes brins, élargis."""
    rs = random.Random(seed)
    L = g.level
    blades = []
    for k in range(n):
        a = rs.uniform(0, TAU)
        d = radius * math.sqrt(rs.random())
        blades.append((a, d, [rs.random() for _ in range(6)], rs.randrange(len(cols))))
    step = max(1, int(round(1.0 / keep[L])))
    for a, d, h, ci in blades[::step]:
        rel = d / max(radius, 1e-6)
        length = (lmin + (lmax - lmin) * h[0]) * (1.05 - 0.4 * rel)
        g.brin((math.cos(a) * d, math.sin(a) * d, z), a + (h[1] - 0.5) * 1.6, length,
               (wmin + (wmax - wmin) * h[2]) * widen[L], cols[ci],
               tilt=0.04 + spread * (0.15 + 0.5 * rel) * (0.6 + 0.8 * h[3]),
               bend=arch[0] + (arch[1] - arch[0]) * h[4], rows=rows[L],
               twist=(h[5] - 0.5) * 2.0 * twist if L == 0 else 0.0, flex=flex, ph=h[5])


def stem_path(root, az, H, tilt, bend, segs):
    pts = arc(root, az, H, tilt, bend, segs, 1.6)
    return pts, (pts[-1] - pts[-2]).normalized()


def tige(g, pts, r0, r1, col="stem", flex=1.0, ph=0.0, s0=0.0, t0=0.1, t1=0.75, style=S_TIGE):
    """Tige cartoon, épaisse : tube à 3 pans ; rien au niveau lointain."""
    if g.level == 2:
        return
    g.style(*style)
    g.tube(pts, lin(r0, r1, len(pts)), col, 3, s0, flex, ph, t0, t1, soft=0.55)


def tache(g, p, r, col, s0, ph, flex=1.0, flat=0.55):
    """Niveau lointain : une tache de couleur (octaèdre aplati) qui tient lieu de plusieurs fleurs."""
    g.style(*S_FLEUR)
    g.ball(p, r, col, 0, s0, flex, ph, t=0.85, scale=(1, 1, flat))


def epi(g, root, az, H, tilt, bend, ph, col="seed", flex=1.3):
    """Épi de graminée cartoon : tige fine, épi en fuseau (deux rubans croisés)."""
    L = g.level
    if L == 2:
        return
    pts = arc(root, az, H, tilt, bend, g.lv(4, 2), 1.5)
    tige(g, pts, 0.005, 0.003, "stem", flex, ph, t0=0.2, t1=0.8)
    g.style(*S_FLEUR)
    p, tang, s = sample(pts, 0.76)
    e1, e2 = basis(tang)
    for nrm in (e1, e2)[:g.lv(2, 1)]:
        g.ribbon_dir(p, tang, H * 0.26, 0.042 * g.lv(1.0, 1.3), col, nrm, g.lv(3, 2), profile=p_spindle, s0=s,
                     flex=flex, ph=ph, soft=0.6, t0=0.2, t1=1.0)


def feuilles_basales(g, n, length, width, col, seed, profile=p_leaf, tilt=(0.6, 1.05), bend=(0.2, 0.6), radius=0.025,
                     flex=0.9, cols=None, c=(0.0, 0.0, 0.0), style=S_FEUILLE):
    """Rosette de feuilles larges au pied de la plante (même langage que les brins du pré)."""
    if n <= 0:
        return
    rs = random.Random(seed)
    g.style(*style)
    c = Vector(c)
    for k in range(n):
        a = TAU * k / n + rs.uniform(-0.3, 0.3)
        root = c + Vector((math.cos(a) * radius, math.sin(a) * radius, 0.0))
        g.brin(root, a, length * rs.uniform(0.8, 1.15), width * rs.uniform(0.85, 1.15) * g.lv(1.0, 1.3, 1.8),
               cols[rs.randrange(len(cols))] if cols else col, tilt=rs.uniform(*tilt), bend=rs.uniform(*bend),
               rows=g.lv(3, 2, 1), profile=profile, flex=flex, ph=rs.random(), soft=0.7)


def tete_marguerite(g, top, facing, r, s0, ph, flex, petal="petal_white", centre="center_yellow", rot=0.0):
    """Grande marguerite : peu de pétales, larges et arrondis, cœur bombé."""
    g.style(*S_FLEUR)
    k = g.lv(1.0, 1.3)
    g.petals(top, facing, g.lv(9, 6), r * k, r * k * g.lv(0.52, 0.85), petal, cup=-0.04, droop=0.16, segs=g.lv(2, 1),
             profile=p_ligule, s0=s0, flex=flex, ph=ph, rot=rot, inner=r * 0.12, jitter=0.05, t0=0.3, t1=1.0)
    g.fan(top + facing * r * 0.06, facing, r * k * 0.36, g.lv(8, 5), centre, r * 0.24, s0, flex, ph, 0.3, 1.0)


def tete_coupe(g, top, facing, r, col, s0, ph, flex, n=5, centre="center_yellow", cup=0.9, tilt=0.45, rot=0.0,
               wide=1.08):
    """Bouton d'or, coquelicot : pétales ronds en coupe."""
    g.style(*S_FLEUR)
    k = g.lv(1.0, 1.3)
    g.petals(top, facing, n, r * k, r * k * wide, col, cup=cup, segs=g.lv(2, 1), profile=p_round, s0=s0, flex=flex,
             ph=ph, rot=rot, jitter=0.05, tilt=tilt, t0=0.15, t1=1.0)
    g.fan(top + facing * r * 0.14, facing, r * k * 0.32, g.lv(6, 4), centre, r * 0.2, s0, flex, ph, 0.2, 1.0)


def trefle(g, top, r, col, s0, ph, flex, rot=0.0):
    g.style(*S_FEUILLE)
    for k in range(3):
        a = rot + TAU * k / 3
        d = Vector((math.cos(a), math.sin(a), 0.15)).normalized()
        g.ribbon_dir(top, d, r, r * 0.95, col, UP, g.lv(2, 1), droop=0.12, profile=p_obovate, s0=s0, flex=flex,
                     ph=ph, soft=0.65, nfix=UP, t0=0.4, t1=1.0)


def palmee(g, top, n, r, col, s0, ph, flex, rot=0.0):
    g.style(*S_FEUILLE)
    for k in range(n):
        a = rot + TAU * k / n
        d = Vector((math.cos(a), math.sin(a), 0.22)).normalized()
        g.ribbon_dir(top, d, r * (0.88 + 0.16 * g.h(k, rot)), r * 0.36, col, UP, g.lv(2, 1), droop=0.3,
                     profile=p_lance, s0=s0, flex=flex, ph=ph, soft=0.65, nfix=(UP + d * 0.3).normalized(),
                     t0=0.35, t1=1.0)


def fronde(g, root, az, Lf, tilt, bend, col, pairs, plen, pw, ph, flex=0.8, sweep=0.55, tbase=0.15):
    """Fronde de fougère cartoon : nervure arquée, pennes larges et pointues en dents de scie (peu nombreuses)."""
    L = g.level
    g.style(*S_FOUGERE)
    pts = arc(root, az, Lf, tilt, bend, g.lv(8, 4, 2), 1.3)
    side0 = Vector((-math.sin(az), math.cos(az), 0.0))
    n = len(pts)
    if L == 2:                                     # lointain : la fronde entière en un ruban
        g.strip(pts, [plen * 1.5 * math.sin(math.pi * min(1.0, 0.15 + i / (n - 1))) ** 0.7 for i in range(n)],
                [side0] * n, col, flex=flex, ph=ph, t0=tbase, t1=1.0, soft=0.8)
        return
    g.strip(pts, [0.018 * (1 - i / n) + 0.004 for i in range(n)], [side0] * n, col, flex=flex, ph=ph, t0=0.05,
            t1=0.7, soft=0.7)
    start = 0.14
    for j in range(pairs):
        u = start + (0.97 - start) * (j + 0.5) / pairs
        p, tang, s = sample(pts, u)
        nrm = tang.cross(side0).normalized()
        if nrm.z < 0:
            nrm = -nrm
        env = max(math.sin(math.pi * min(1.0, (u - start * 0.5) / (1.0 - start * 0.5))), 0.0) ** 0.7
        pl = plen * env
        if pl < 0.02:
            continue
        t0 = tbase + (1 - tbase) * u * 0.8
        for sgn in (-1, 1):
            dirp = (side0 * sgn * math.cos(sweep) + tang * math.sin(sweep)).normalized()
            g.ribbon_dir(p, dirp, pl, pw * (0.6 + 0.4 * env), col, nrm, g.lv(2, 1), droop=0.22, profile=p_penne,
                         s0=s, flex=flex, ph=ph, soft=0.7, nfix=(nrm + UP * 0.5).normalized(), t0=t0,
                         t1=min(1.0, t0 + 0.3))
    p, tang, s = sample(pts, 0.96)                  # penne terminale
    nrm = tang.cross(side0).normalized()
    if nrm.z < 0:
        nrm = -nrm
    g.ribbon_dir(p, tang, plen * 0.55, pw * 0.8, col, nrm, 1, profile=p_penne, s0=s, flex=flex, ph=ph, soft=0.7,
                 nfix=(nrm + UP * 0.5).normalized(), t0=0.8, t1=1.0)


def crosse(g, root, H, col, ph, flex=0.5):
    """Jeune fronde enroulée en crosse."""
    g.style(*S_FOUGERE)
    pts = []
    for k in range(10):
        u = k / 9
        if u < 0.6:
            pts.append(Vector(root) + Vector((0.012 * u, 0, H * u / 0.6)))
        else:
            a = (u - 0.6) / 0.4 * 4.2
            rr = 0.045 * (1.0 - 0.6 * (u - 0.6) / 0.4)
            pts.append(Vector(root) + Vector((0.008 + rr * (1 - math.cos(a)), 0, H + rr * math.sin(a))))
    g.tube(pts, [0.012 - 0.006 * k / 9 for k in range(10)], col, 3, 0.0, flex, ph, 0.2, 1.0, soft=0.7)


# FGeo.h : hasard stable par clé
def _h(self, *key):
    k = zlib.crc32(repr((self.seed,) + tuple(round(float(v), 3) for v in key)).encode())
    return (k % 100003) / 100003.0


FGeo.h = _h


# ================================================================= herbes
# Touffes dans le langage du champ d'herbe (brins larges, pied sombre, pointe claire) : elles le remplacent là où il
# ne pousse pas (roche) et sur les machines sans champ d'herbe.
def grass_short(g):
    g.dome, g.up = Vector((0, 0, -0.35)), 0.9
    g.style(*S_HERBE)
    touffe(g, 26, 0.2, 0.3, 0.55, 0.042, 0.058, ["grass"] * 4 + ["grass_lush", "grass_yellow"], g.seed, flex=2.0)


def grass_tall(g):
    g.dome, g.up = Vector((0, 0, -0.6)), 0.9
    g.style(*S_HERBE)
    touffe(g, 22, 0.2, 0.5, 0.9, 0.045, 0.062, ["grass"] * 4 + ["grass_lush", "grass_yellow"], g.seed, flex=1.3,
           spread=0.4, arch=(0.25, 0.9), twist=0.8)
    rs = random.Random(g.seed + 5)
    for k in range(4):
        a = rs.uniform(0, TAU)
        H = rs.uniform(0.9, 1.2)
        tilt, bend, ph = rs.uniform(0.03, 0.15), rs.uniform(0.2, 0.5), rs.random()
        if k < g.lv(4, 2, 0):
            epi(g, (math.cos(a) * 0.05, math.sin(a) * 0.05, -0.02), a, H, tilt, bend, ph,
                "seed" if k % 2 else "seed_purple", 1.0)


def grass_dry(g):
    g.dome, g.up = Vector((0, 0, -0.45)), 0.75
    g.style(0.0, 0.15)                                  # herbe sèche : garde sa couleur (palette)
    touffe(g, 24, 0.17, 0.24, 0.48, 0.034, 0.048, ["grass_dry"] * 4 + ["grass_yellow"] * 2, g.seed, flex=2.2,
           spread=0.75, arch=(0.5, 1.2))
    rs = random.Random(g.seed + 9)
    for k in range(3):
        a = rs.uniform(0, TAU)
        H = rs.uniform(0.48, 0.66)
        tilt, bend, ph = rs.uniform(0.03, 0.15), rs.uniform(0.2, 0.5), rs.random()
        if k < g.lv(3, 2, 0):
            epi(g, (math.cos(a) * 0.03, math.sin(a) * 0.03, -0.02), a, H, tilt, bend, ph, "seed", 1.2)


# ================================================================= fleurs
# Grandes têtes simples qui dépassent de l'herbe (0,3 à 0,55 m), tiges épaisses, feuilles larges ; au loin, quelques
# taches de couleur élargies (comme le champ d'herbe : moins d'éléments, plus gros).
def flowers_meadow(g):
    """Bouquet de prairie : marguerites, boutons d'or, un coquelicot, une campanule, du trèfle des prés."""
    g.dome, g.up = Vector((0, 0, -0.4)), 0.8
    L = g.level
    rs = random.Random(g.seed)
    feuilles_basales(g, g.lv(6, 3, 0), 0.17, 0.05, "leaf", g.seed + 2)
    plan = ["daisy", "butter", "poppy", "butter", "bell", "daisy", "clover", "butter"]
    cols = {"daisy": "petal_white", "butter": "petal_yellow", "clover": "clover_head", "bell": "petal_violet",
            "poppy": "poppy"}
    heights = {"daisy": (0.4, 0.56), "butter": (0.32, 0.46), "clover": (0.22, 0.32), "bell": (0.4, 0.5),
               "poppy": (0.5, 0.62)}
    for k, kind in enumerate(plan):
        a = TAU * k / len(plan) + rs.uniform(-0.35, 0.35)
        d = rs.uniform(0.03, 0.2)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        az = a + rs.uniform(-0.5, 0.5)
        ph = rs.random()
        H = rs.uniform(*heights[kind])
        tilt, bend = rs.uniform(0.04, 0.2), rs.uniform(0.05, 0.3)
        size, spin = rs.uniform(0.92, 1.1), rs.uniform(0, TAU)
        lean = Vector((rs.uniform(-0.2, 0.2), rs.uniform(-0.2, 0.2), 0))
        atilt, abend = rs.uniform(0.1, 0.25), rs.uniform(0.7, 1.1)
        if L == 2:
            if k in (0, 2, 3):                          # trois taches : blanc, rouge, jaune
                tache(g, root + UP * H, 0.12, cols[kind], H, ph)
            continue
        if L == 1 and k >= 5:
            continue
        if kind == "bell":                              # campanule : tige arquée, clochettes pendantes
            pts, tang = stem_path(root, az, H, atilt, abend, g.lv(5, 2))
            tige(g, pts, 0.008, 0.005, flex=1.2, ph=ph)
            g.style(*S_FLEUR)
            for j, u in enumerate((1.0, 0.8, 0.62)[:g.lv(3, 2)]):
                p, t2, s = sample(pts, u)
                ax = (t2 * 0.4 - UP * 0.9 + Vector((math.cos(az + j), math.sin(az + j), 0)) * 0.25).normalized()
                kk = g.lv(1.0, 1.3)
                g.bell(p, ax, 0.07 * kk, 0.01, 0.034 * kk, "petal_violet", 5, g.lv(2, 1), 1.6, 0.35, s, 1.2, ph,
                       0.2, 1.0, rot=spin + j)
            continue
        pts, tang = stem_path(root, az, H, tilt, bend, g.lv(4, 2))
        tige(g, pts, 0.009, 0.006, flex=1.0, ph=ph)
        top = pts[-1]
        facing = (tang * 0.35 + UP * 0.65 + lean).normalized()
        if kind == "daisy":
            tete_marguerite(g, top, facing, 0.066 * size, H, ph, 1.0, rot=spin)
        elif kind == "butter":
            tete_coupe(g, top, facing, 0.048 * size, "petal_yellow", H, ph, 1.0, cup=0.5, tilt=0.25, rot=spin,
                       wide=1.2)
        elif kind == "poppy":
            tete_coupe(g, top, facing, 0.078 * size, "poppy", H, ph, 1.0, n=4, centre="center_dark", cup=0.4,
                       tilt=0.3, rot=spin, wide=1.35)
        elif kind == "clover":
            g.style(*S_FLEUR)
            g.ball(top + UP * 0.018, 0.032 * g.lv(1.0, 1.3), "clover_head", g.lv(1, 0), H, 1.0, ph, scale=(1, 1, 1.1))
            if L == 0:
                p, t2, s = sample(pts, 0.55)
                trefle(g, p, 0.05, "clover_leaf", s, ph, 1.0, spin)
        if L == 0 and kind in ("daisy", "poppy"):       # une feuille sur la tige
            p, t2, s = sample(pts, 0.35)
            g.style(*S_FEUILLE)
            g.ribbon_dir(p, (t2 + Vector((math.cos(az + 1.6), math.sin(az + 1.6), 0))).normalized(), 0.1, 0.035,
                         "leaf", UP, 2, droop=0.3, profile=p_lance, s0=s, flex=1.0, ph=ph, soft=0.6, t0=0.4, t1=1.0)


def flowers_daisy(g):
    """Touffe de grandes marguerites : capitules blancs à cœur jaune, rosette de feuilles larges."""
    g.dome, g.up = Vector((0, 0, -0.45)), 0.8
    L = g.level
    feuilles_basales(g, g.lv(7, 4, 0), 0.16, 0.05, "leaf", g.seed + 2, p_lance, tilt=(0.7, 1.1), bend=(0.3, 0.7))
    rs = random.Random(g.seed)
    for k in range(6):
        a = TAU * k / 6 + rs.uniform(-0.4, 0.4)
        d = rs.uniform(0.02, 0.17)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        az = a + rs.uniform(-0.4, 0.4)
        H = rs.uniform(0.36, 0.58)
        ph = rs.random()
        tilt, bend = rs.uniform(0.04, 0.25), rs.uniform(0.05, 0.35)
        size, spin = rs.uniform(0.9, 1.12), rs.uniform(0, TAU)
        lean = Vector((rs.uniform(-0.25, 0.25), rs.uniform(-0.25, 0.25), 0))
        if L == 2:
            if k % 3 == 0:
                tache(g, root + UP * H, 0.13, "petal_white", H, ph)
            continue
        if L == 1 and k % 2:
            continue
        pts, tang = stem_path(root, az, H, tilt, bend, g.lv(4, 2))
        tige(g, pts, 0.009, 0.006, flex=1.0, ph=ph)
        facing = (tang * 0.3 + UP * 0.7 + lean).normalized()
        tete_marguerite(g, pts[-1], facing, 0.07 * size, H, ph, 1.0, rot=spin)
        if L == 0 and k % 3 == 0:                   # bouton fermé sur un rameau
            p, t2, s = sample(pts, 0.65)
            br = arc(p, az - 2.0, 0.11, 0.5, 0.1, 2)
            tige(g, br, 0.004, 0.0035, flex=1.0, ph=ph, s0=s)
            g.style(*S_TIGE)
            g.ball(br[-1], 0.017, "stem", 1, s + 0.11, 1.0, ph, t=0.8, scale=(1, 1, 0.85))


def massif_coupes(g, n, col, r, heights, seed, petals=5, centre="center_yellow", cup=0.5, tilt=0.25, wide=1.2,
                  radius=0.18, tache_r=0.11, feuilles=6, feuille=(0.15, 0.05)):
    """Touffe d'une seule espèce à fleurs en coupe (boutons d'or, coquelicots) : pour des dérives d'une couleur."""
    L = g.level
    feuilles_basales(g, g.lv(feuilles, feuilles // 2, 0), feuille[0], feuille[1], "leaf", seed + 2, p_lance,
                     tilt=(0.7, 1.1), bend=(0.3, 0.7))
    rs = random.Random(seed)
    for k in range(n):
        a = TAU * k / n + rs.uniform(-0.4, 0.4)
        d = rs.uniform(0.02, radius)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        az = a + rs.uniform(-0.4, 0.4)
        H = rs.uniform(*heights)
        ph = rs.random()
        tl, bd = rs.uniform(0.04, 0.25), rs.uniform(0.05, 0.35)
        size, spin = rs.uniform(0.88, 1.12), rs.uniform(0, TAU)
        lean = Vector((rs.uniform(-0.25, 0.25), rs.uniform(-0.25, 0.25), 0))
        if L == 2:
            if k % 3 == 0:
                tache(g, root + UP * H, tache_r, col, H, ph)
            continue
        if L == 1 and k % 2:
            continue
        pts, tang = stem_path(root, az, H, tl, bd, g.lv(4, 2))
        tige(g, pts, 0.008, 0.0055, flex=1.0, ph=ph)
        facing = (tang * 0.3 + UP * 0.7 + lean).normalized()
        tete_coupe(g, pts[-1], facing, r * size, col, H, ph, 1.0, n=petals, centre=centre, cup=cup, tilt=tilt,
                   rot=spin, wide=wide)


def flowers_buttercup(g):
    """Boutons d'or : touffe de petites coupes jaune vif."""
    g.dome, g.up = Vector((0, 0, -0.4)), 0.8
    massif_coupes(g, 7, "petal_yellow", 0.048, (0.3, 0.48), g.seed)


def flowers_poppy(g):
    """Coquelicots : grandes fleurs rouges à cœur noir sur de hautes tiges."""
    g.dome, g.up = Vector((0, 0, -0.5)), 0.8
    massif_coupes(g, 5, "poppy", 0.08, (0.45, 0.68), g.seed, petals=4, centre="center_dark", cup=0.4, tilt=0.3,
                  wide=1.35, tache_r=0.13, feuilles=5, feuille=(0.2, 0.05))


def epi_lupin(g, pts, H, col, ph, k, flex=0.55):
    """Épi de lupin : étages de fleurons ronds en gradins, de plus en plus petits vers la pointe."""
    L = g.level
    u0 = 0.42
    if L == 2:
        p0 = sample(pts, u0)[0]
        tip = pts[-1] + UP * 0.04
        g.style(*S_FLEUR)
        for ang in (0.0, math.pi / 2):
            side = Vector((math.cos(ang), math.sin(ang), 0))
            g.strip([p0, p0.lerp(tip, 0.3), tip], [0.13, 0.2, 0.0], [side] * 3, col, s0=H * u0, flex=flex, ph=ph,
                    t0=0.1, t1=1.0, soft=0.9)
        return
    tige(g, pts, 0.011, 0.005, flex=flex, ph=ph, t1=0.6)
    g.style(*S_FLEUR)
    if L == 1:                                      # épi : fuseau à 5 pans
        ring = [sample(pts, u)[0] for u in (u0, u0 + 0.12, 0.8, 1.0)]
        ring[-1] = ring[-1] + UP * 0.03
        g.tube(ring, [0.03, 0.085, 0.055, 0.0], col, 5, H * u0, flex, ph, 0.05, 1.0, soft=0.85)
        return
    whorls = 10
    for j in range(whorls):
        tu = j / (whorls - 1)
        p, t2, s = sample(pts, u0 + (0.97 - u0) * tu)
        e1, e2 = basis(t2)
        size = 0.074 * (1.0 - 0.62 * tu)
        n = 6 if tu < 0.5 else 5 if tu < 0.8 else 4
        for i in range(n):
            ang = TAU * i / n + j * 0.63 + k
            o = e1 * math.cos(ang) + e2 * math.sin(ang)
            d = (o * 0.78 + t2 * 0.62).normalized()
            g.ribbon_dir(p + o * 0.006, d, size, size * 1.2, col, t2, 1, curl=0.3, profile=p_round, s0=s, flex=flex,
                         ph=ph, soft=0.6, nfix=(o + UP * 0.5).normalized(), t0=0.1 + 0.6 * tu, t1=0.35 + 0.6 * tu)
    tip = [pts[-1], pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.04]
    g.tube(tip, [0.016, 0.0], col, 4, H, flex, ph, 0.9, 1.0, soft=0.8)


def flowers_lupin(g):
    """Lupins : grands épis en gradins (violet, rose, bleu, blanc), feuilles palmées larges."""
    g.dome, g.up = Vector((0, 0, -0.7)), 0.7
    L = g.level
    rs = random.Random(g.seed)
    cols = ["lupin_purple", "lupin_pink", "lupin_blue", "lupin_purple", "lupin_white"]
    for k in range(5):
        a = TAU * k / 5 + rs.uniform(-0.3, 0.3)
        d = rs.uniform(0.08, 0.2)
        h = rs.uniform(0.2, 0.34)
        r = rs.uniform(0.15, 0.19)
        ph, rot = rs.random(), rs.uniform(0, TAU)
        if k >= g.lv(5, 3, 2):
            continue
        root = Vector((math.cos(a) * d * 0.25, math.sin(a) * d * 0.25, 0.0))
        top = Vector((math.cos(a) * d, math.sin(a) * d, h))
        tige(g, [root, (root + top) / 2 + UP * 0.03, top], 0.008, 0.005, flex=0.6, ph=ph)
        palmee(g, top, g.lv(7, 5, 4), r * g.lv(1.0, 1.2, 1.6), "leaf", h, ph, 0.6, rot)
    for k in range(4):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.0, 0.09)
        H = rs.uniform(0.72, 1.05)
        ph = rs.random()
        tilt, bend = rs.uniform(0.02, 0.12), rs.uniform(0.02, 0.14)
        if k >= g.lv(4, 3, 2):
            continue
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        pts, tang = stem_path(root, a, H, tilt, bend, g.lv(6, 3, 1))
        epi_lupin(g, pts, H, cols[k % len(cols)], ph, k)


def flowers_gentian(g):
    """Gentianes : grandes trompettes bleu profond presque sans tige sur une rosette de feuilles, petites étoiles
    de gentianes printanières."""
    g.dome, g.up = Vector((0, 0, -0.2)), 0.8
    L = g.level
    rs = random.Random(g.seed)
    for k in range(4):
        a = TAU * k / 4 + rs.uniform(-0.4, 0.4)
        d = rs.uniform(0.05, 0.15)
        H = rs.uniform(0.04, 0.09)
        lean = rs.uniform(0.1, 0.4)
        ph, rot, sz = rs.random(), rs.uniform(0, TAU), rs.uniform(0.9, 1.15)
        c = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
        ax = (UP + Vector((math.cos(a), math.sin(a), 0)) * lean).normalized()
        if L == 2:
            if k % 2 == 0:
                tache(g, c + ax * (H + 0.06), 0.085, "petal_blue", 0.1, ph, 0.6, flat=0.8)
            continue
        if L == 1 and k == 3:
            continue
        feuilles_basales(g, g.lv(5, 3), 0.085, 0.04, "leaf_dark", g.seed + 10 + k, tilt=(1.0, 1.3), bend=(0.1, 0.3),
                         radius=0.01, flex=0.3, c=c)
        tige(g, [c, c + ax * H], 0.008, 0.008, flex=0.6, ph=ph)
        g.style(*S_FLEUR)
        kk = g.lv(1.0, 1.25)
        g.bell(c + ax * H, ax, 0.1 * sz * kk, 0.012, 0.036 * sz * kk, "petal_blue", 5, g.lv(3, 1), 1.9, 0.4, H, 0.6,
               ph, 0.05, 1.0, rot=rot, flat_rim=0.3)
    if L == 0:                                      # gentianes printanières : étoiles à 5 pétales
        for k in range(3):
            a = rs.uniform(0, TAU)
            d = rs.uniform(0.1, 0.22)
            c = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
            H = rs.uniform(0.07, 0.11)
            ph = rs.random()
            tige(g, [c, c + UP * H], 0.004, 0.0035, flex=1.0, ph=ph)
            g.style(*S_FLEUR)
            g.petals(c + UP * H, UP, 5, 0.03, 0.024, "petal_blue", cup=0.15, segs=1, profile=p_round, s0=H, flex=1.0,
                     ph=ph, jitter=0.05, t0=0.6, t1=1.0)
            g.fan(c + UP * (H + 0.003), UP, 0.007, 4, "petal_white", 0.004, H, 1.0, ph, 0.8, 1.0)


def flowers_edelweiss(g):
    """Edelweiss : grandes étoiles de bractées feutrées blanches autour d'un cœur jaunâtre, feuilles grises."""
    g.dome, g.up = Vector((0, 0, -0.2)), 0.8
    L = g.level
    rs = random.Random(g.seed)
    gris = (0.0, 0.3)
    for k in range(4):
        a = TAU * k / 4 + rs.uniform(-0.4, 0.4)
        d = rs.uniform(0.04, 0.15)
        H = rs.uniform(0.12, 0.22)
        ph = rs.random()
        tilt, bend = rs.uniform(0.0, 0.2), rs.uniform(0.0, 0.2)
        r, rot = rs.uniform(0.05, 0.062), rs.uniform(0, TAU)
        c = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
        pts, tang = stem_path(c, a, H, tilt, bend, g.lv(3, 1, 1))
        top = pts[-1]
        facing = (UP * 0.85 + tang * 0.15).normalized()
        if L == 2:
            if k % 2 == 0:
                g.style(*S_FLEUR)
                g.fan(top, facing, 0.1, 8, "felt", 0.012, H, 0.8, ph, 0.9, 0.95, rot=rot, star=0.45)
            continue
        if L == 1 and k == 3:
            continue
        feuilles_basales(g, g.lv(5, 3), 0.085, 0.028, "leaf_grey", g.seed + 20 + k, p_lance, tilt=(0.9, 1.25),
                         bend=(0.1, 0.4), radius=0.008, flex=0.4, c=c, style=gris)
        tige(g, pts, 0.007, 0.006, "leaf_grey", 0.8, ph, style=gris)
        if L == 0:                                  # deux feuilles sur la tige
            g.style(*gris)
            for j in (0.4, 0.7):
                p, t2, s = sample(pts, j)
                g.ribbon_dir(p, Vector((math.cos(a + j * 5), math.sin(a + j * 5), 0.6)).normalized(), 0.05, 0.018,
                             "leaf_grey", UP, 2, droop=0.2, profile=p_lance, s0=s, flex=0.8, ph=ph, soft=0.6)
        g.style(*S_FLEUR)
        kk = g.lv(1.0, 1.25)
        g.petals(top, facing, g.lv(8, 6), r * kk, r * kk * 0.44, "felt", cup=0.1, droop=0.05, segs=g.lv(2, 1),
                 profile=p_pointed, s0=H, flex=0.8, ph=ph, rot=rot, inner=0.004, t0=0.25, t1=1.0, tilt=0.12)
        g.ball(top + facing * 0.012, r * kk * 0.26, "felt_center", g.lv(1, 0), H, 0.8, ph, scale=(1, 1, 0.6))
        if L == 0:
            for j in range(5):
                aa = TAU * j / 5 + rot
                g.ball(top + facing * 0.01 + Vector((math.cos(aa), math.sin(aa), 0)) * r * 0.32, r * 0.13,
                       "felt_center", 0, H, 0.8, ph, scale=(1, 1, 0.8))


def clover(g):
    """Tapis de trèfle : grandes feuilles à trois folioles, têtes roses et blanches."""
    g.dome, g.up = Vector((0, 0, -0.3)), 1.0
    L = g.level
    rs = random.Random(g.seed)
    for k in range(24):
        a = rs.uniform(0, TAU)
        d = 0.3 * math.sqrt(rs.random())
        H = rs.uniform(0.07, 0.16)
        ph, r, rot = rs.random(), rs.uniform(0.042, 0.055), rs.uniform(0, TAU)
        jx, jy = rs.uniform(-0.02, 0.02), rs.uniform(-0.02, 0.02)
        if k % g.lv(1, 2, 8):
            continue
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        top = root + Vector((jx, jy, H))
        if L == 0:
            side = Vector((-math.sin(a), math.cos(a), 0))
            g.style(*S_TIGE)
            g.strip([root, top], [0.008, 0.006], [side] * 2, "stem", flex=1.2, ph=ph, soft=0.6, t0=0.1, t1=0.6)
        trefle(g, top, r * g.lv(1.0, 1.35, 2.0), "clover_leaf" if k % 4 else "leaf", H, ph, 1.2, rot)
    for k in range(5):
        a = rs.uniform(0, TAU)
        d = 0.24 * math.sqrt(rs.random())
        H = rs.uniform(0.16, 0.28)
        ph = rs.random()
        tilt, bend = rs.uniform(0.0, 0.2), rs.uniform(0.0, 0.3)
        col = "clover_white" if k % 3 == 1 else "clover_head"
        if k >= g.lv(5, 3, 2):
            continue
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        pts, tang = stem_path(root, a, H, tilt, bend, g.lv(3, 1, 1))
        if L == 2:
            tache(g, pts[-1], 0.07, col, H, ph, 1.2, flat=0.8)
            continue
        tige(g, pts, 0.006, 0.005, flex=1.2, ph=ph)
        g.style(*S_FLEUR)
        g.ball(pts[-1] + UP * 0.015, 0.027 * g.lv(1.0, 1.3), col, g.lv(1, 0), H, 1.2, ph, scale=(1, 1, 1.1))


# ================================================================= sous-bois
def fern(g):
    """Fougère : grandes frondes arquées en couronne, pennes larges."""
    g.dome, g.up = Vector((0, 0, -0.5)), 0.6
    rs = random.Random(g.seed)
    for k in range(9):
        a = TAU * k / 9 + rs.uniform(-0.25, 0.25)
        Lf, tilt, bend, ph = rs.uniform(0.78, 1.0), rs.uniform(0.3, 0.55), rs.uniform(0.7, 1.0), rs.random()
        if k % g.lv(1, 1, 2) or (g.level == 1 and k >= 7):
            continue
        fronde(g, (math.cos(a) * 0.03, math.sin(a) * 0.03, 0.0), a, Lf, tilt, bend, "fern" if k % 3 else "fern_light",
               g.lv(9, 6), 0.17 * g.lv(1.0, 1.15), 0.085 * g.lv(1.0, 1.4), ph, 0.7)
    if g.level == 0:
        for k in range(2):
            crosse(g, (rs.uniform(-0.03, 0.03), rs.uniform(-0.03, 0.03), 0.0), rs.uniform(0.16, 0.24), "fern_light",
                   rs.random())


def fern_young(g):
    """Jeune fougère : frondes dressées vert tendre, crosses."""
    g.dome, g.up = Vector((0, 0, -0.35)), 0.6
    rs = random.Random(g.seed)
    for k in range(6):
        a = TAU * k / 6 + rs.uniform(-0.3, 0.3)
        Lf, tilt, bend, ph = rs.uniform(0.4, 0.54), rs.uniform(0.12, 0.3), rs.uniform(0.35, 0.6), rs.random()
        if k >= g.lv(6, 5, 4):
            continue
        fronde(g, (math.cos(a) * 0.02, math.sin(a) * 0.02, 0.0), a, Lf, tilt, bend, "fern_light", g.lv(7, 5),
               0.095 * g.lv(1.0, 1.15), 0.06 * g.lv(1.0, 1.4), ph, 0.9, tbase=0.3)
    if g.level == 0:
        for k in range(3):
            crosse(g, (rs.uniform(-0.04, 0.04), rs.uniform(-0.04, 0.04), 0.0), rs.uniform(0.12, 0.18), "fern_light",
                   rs.random())


def blueberry(g):
    """Myrtillier : coussin de petites feuilles ovales sur rameaux verts et rouges, grosses baies bleues."""
    L = g.level
    rs = random.Random(g.seed)
    g.dome, g.up = Vector((0, 0, 0.0)), 0.4
    zr = (0.0, 0.4)
    g.style(*S_BUISSON)
    if L == 2:
        g.ball((0, 0, 0.16), 1.0, "leaf_dark", 1, 0.2, 0.2, 0.0, scale=(0.34, 0.34, 0.21),
               tz=lambda co: max(0.05, min(0.95, co.z / 0.36)))
        return
    for k in range(g.lv(7, 3)):
        a = rs.uniform(0, TAU)
        pts = arc((math.cos(a) * 0.04, math.sin(a) * 0.04, -0.01), a, rs.uniform(0.28, 0.38), rs.uniform(0.2, 0.6),
                  rs.uniform(0.0, 0.3), g.lv(3, 2))
        g.tube(pts, lin(0.007, 0.004, len(pts)), "twig_red" if k % 2 else "stem", 3, 0.0, 0.3, rs.random(), 0.0, 1.0,
               soft=0.55)
    spots = [((0, 0, 0.22), (0.21, 0.21, 0.16)), ((0.17, 0.08, 0.16), (0.14, 0.14, 0.12)),
             ((-0.16, 0.1, 0.15), (0.14, 0.13, 0.11)), ((0.02, -0.18, 0.15), (0.13, 0.13, 0.11))]
    for i, (c, rad) in enumerate(spots if L == 0 else spots[:2]):
        g.clump_cloud(c, rad, g.lv(7, 4), rad[0] * 0.5, "leaf_dark", 0.15, 0.25, g.seed + i, zmin=-0.3, zrange=zr,
                      sub=g.lv(2, 1), own=0.3, inset=0.72)
        g.rosette_cloud(c, (rad[0] * 1.06, rad[1] * 1.06, rad[2] * 1.06), g.lv(int(12 * rad[0] / 0.2), int(5 * rad[0] / 0.2)),
                        g.lv(0.07, 0.095), ["leaf", "leaf_dark"], g.seed + i, zmin=-0.1, zrange=zr,
                        leaves=g.lv(5, 4), cup=0.35, sinus=0.4, core=None, width=0.6, flex=0.25, s0=0.15)
    g.style(*S_BAIE)
    for k in range(g.lv(14, 6)):
        p, n = sur_boule(g, rs, k * 3 + 1, 1.0, 0.3)
        g.ball(p, 0.021, "berry", g.lv(1, 0), 0.3, 0.25, rs.random())


# ================================================================= buissons (dispersés sur toute la carte : LOD2 très léger)
def bush_rhodo(g):
    """Rhododendron ferrugineux (rose des Alpes) : coussin rebondi de grandes feuilles, gros bouquets de fleurs roses."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -0.2)), 0.35
    spots = [((0, 0, 0.45), 0.62), ((0.6, 0.2, 0.36), 0.46), ((-0.55, 0.28, 0.36), 0.46), ((0.1, -0.55, 0.34), 0.44),
             ((-0.2, 0.6, 0.3), 0.38), ((0.5, -0.45, 0.28), 0.34)]
    zr = (0.0, 1.0)
    rs = random.Random(g.seed)
    g.style(*S_BUISSON)
    if L == 2:
        g.ball((0, 0, 0.4), 1.0, "rhodo_leaf", 1, 0.3, 0.1, 0.0, scale=(1.0, 0.95, 0.55),
               tz=lambda co: max(0.05, min(0.95, co.z / 0.95)))
        g.style(*S_BAIE)
        for k in range(3):
            a = TAU * k / 3 + 0.4
            g.ball((math.cos(a) * 0.45, math.sin(a) * 0.45, 0.72), 0.24, "rhodo_flower", 0, 0.8, 0.1, 0.0, t=0.7,
                   scale=(1, 1, 0.5))
        return
    use = spots if L == 0 else spots[:4]
    for k, (c, r) in enumerate(use):
        rad = (r * 1.12, r * 1.12, r * 0.82)
        g.clump_cloud(c, rad, g.lv(7, 4), r * 0.5, "rhodo_leaf", 0.3, 0.12, g.seed + k, zmin=-0.3, zrange=zr,
                      sub=g.lv(2, 1), own=0.3, inset=0.74)
        g.rosette_cloud(c, (rad[0] * 1.05, rad[1] * 1.05, rad[2] * 1.05),
                        g.lv(int(13 * (r / 0.62) ** 1.6) + 3, int(5 * (r / 0.62) ** 1.6) + 2), g.lv(0.19, 0.27),
                        ["rhodo_leaf", "leaf"], g.seed + k, zmin=-0.1, zrange=zr, leaves=g.lv(6, 5), cup=0.36,
                        sinus=0.32, core=None)
    # bouquets de fleurs (ombelles de corolles étoilées) sur le dessus
    g.style(*S_BAIE)
    for j in range(g.lv(12, 8)):
        p, sn = sur_boule(g, rs, j * 3, 1.0)
        ph = rs.random()
        spins = [rs.uniform(0, TAU) for _ in range(6)]
        if L == 1:
            g.ball(p, 0.1, "rhodo_flower", 0, 0.9, 0.12, ph, t=0.7, scale=(1, 1, 0.7))
            continue
        for f in range(5):
            aa = TAU * f / 5 + spins[f] * 0.06
            o = Vector((math.cos(aa), math.sin(aa), 0.0))
            o = (o - sn * o.dot(sn))
            o = o.normalized() if o.length > 1e-4 else sn.orthogonal().normalized()
            q = p + o * 0.07 + sn * 0.012
            g.fan(q, (sn + o * 0.6).normalized(), 0.05, 10, "rhodo_flower", -0.018, 0.9, 0.12, ph, 1.0, 0.25,
                  rot=spins[f], star=0.35)
        g.fan(p + sn * 0.026, sn, 0.046, 10, "rhodo_flower", -0.016, 0.9, 0.12, ph, 1.0, 0.25, rot=spins[5], star=0.35)


def juniper(g):
    """Genévrier nain : tapis étalé et rebondi de rameaux glauques, pousses dressées, baies bleu-gris."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -0.4)), 0.4
    rs = random.Random(g.seed)
    zr = (0.0, 0.7)
    g.style(*S_BUISSON)
    if L == 2:
        g.ball((0, 0, 0.18), 1.0, "juniper", 1, 0.2, 0.1, 0.0, scale=(1.15, 1.25, 0.42),
               tz=lambda co: max(0.05, min(0.95, co.z / 0.6)))
        return
    clouds = []
    for k in range(7):
        a = TAU * k / 6 + rs.uniform(-0.3, 0.3)
        d = 0.0 if k == 0 else rs.uniform(0.5, 0.95)
        r = rs.uniform(0.45, 0.68)
        clouds.append(((math.cos(a) * d, math.sin(a) * d, r * 0.32), (r * 1.15, r * 1.0, r * 0.5)))
    use = clouds if L == 0 else clouds[:5]
    for k, (c, rad) in enumerate(use):
        g.clump_cloud(c, rad, g.lv(6, 4), rad[0] * 0.42, "juniper", 0.3, 0.12, g.seed + k, zmin=-0.2, zrange=zr,
                      sub=g.lv(2, 1), own=0.3, inset=0.74, squash=0.62)
        g.rosette_cloud(c, (rad[0] * 1.05, rad[1] * 1.05, rad[2] * 1.12), g.lv(int(11 * rad[0] / 0.65), int(4 * rad[0] / 0.65)),
                        g.lv(0.2, 0.3), ["juniper_tip", "juniper"], g.seed + k, zmin=0.0, zrange=zr, leaves=g.lv(7, 6),
                        cup=0.3, sinus=0.24, core=None, width=0.25)
    if L == 0:
        for k in range(10):                          # pousses dressées
            p, n = sur_boule(g, rs, k * 5 + 2, 0.9)
            a = rs.uniform(0, TAU)
            g.style(*S_BUISSON)
            for j in range(4):
                aa = a + TAU * j / 4
                g.blade(p, aa, rs.uniform(0.16, 0.26), 0.06, "juniper_tip", 0.3, 0.35, 2, 0.0, p_needle, 0.4, 0.25,
                        rs.random(), soft=0.85)
        g.style(*S_BAIE)
        for k in range(12):
            p, n = sur_boule(g, rs, k * 3 + 1, 1.0, 0.3)
            g.ball(p, 0.03, "berry", 1, 0.3, 0.15, rs.random())


# ================================================================= lac
def reeds(g):
    """Roselière : larges feuilles en lanière, grosses massettes brunes. Origine à la ligne d'eau, racines 0,6 m
    dessous."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -1.2)), 0.6
    rs = random.Random(g.seed)
    g.style(*S_LAC)
    for k in range(18):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.04, 0.7)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.6))
        H = rs.uniform(1.8, 2.9)
        ph = rs.random()
        az, w = a + rs.uniform(-0.6, 0.6), rs.uniform(0.05, 0.07)
        tilt, bend, tw = rs.uniform(0.02, 0.18), rs.uniform(0.2, 0.75), rs.uniform(-0.6, 0.6)
        if k % g.lv(1, 2, 3):
            continue
        g.brin(root, az, H, w * g.lv(1.0, 1.35, 1.9), "reed" if k % 3 else "leaf", tilt=tilt, bend=bend,
               rows=g.lv(4, 2, 1), twist=tw if L == 0 else 0.0, profile=p_reed, flex=0.12, ph=ph, soft=0.75)
    for k in range(6):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.05, 0.55)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.6))
        H = rs.uniform(2.0, 2.6)
        ph = rs.random()
        tilt, bend = rs.uniform(0.0, 0.08), rs.uniform(0.0, 0.1)
        if k >= g.lv(6, 4, 2):
            continue
        pts = arc(root, a, H, tilt, bend, g.lv(5, 2, 1))
        if L == 2:
            side = Vector((-math.sin(a), math.cos(a), 0))
            g.strip(pts, [0.03, 0.0], [side] * 2, "reed", flex=0.1, ph=ph)
            p, t2, s = sample(pts, 0.82)
            g.strip([p, p + t2 * 0.3], [0.085, 0.085], [side] * 2, "cattail", flex=0.1, ph=ph, t0=0.3, t1=0.8,
                    soft=0.9)
            continue
        g.tube(pts, lin(0.012, 0.007, len(pts)), "reed", 3, 0.0, 0.1, ph, 0.0, 1.0, soft=0.55)
        p, t2, s = sample(pts, 0.78)
        head = [p + t2 * (0.27 * i / 4) for i in range(5)]
        g.tube(head, [0.016, 0.03, 0.032, 0.029, 0.012], "cattail", g.lv(6, 4), s, 0.1, ph, 0.2, 0.9, soft=0.85,
               cap=True)
        if L == 0:
            tip = [head[-1], head[-1] + t2 * 0.1, head[-1] + t2 * 0.2]
            g.tube(tip, [0.006, 0.004, 0.0], "reed", 3, s + 0.27, 0.1, ph, 0.6, 1.0)


def lily_pads(g):
    """Nénuphars : feuilles rondes échancrées au bord relevé, grandes fleurs à pétales étagés, un bouton. Origine à
    la ligne d'eau."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -2.0)), 1.0
    rs = random.Random(g.seed)
    g.style(*S_LAC)
    pads = []
    for k in range(10):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.0, 1.3) if k else 0.0
        pads.append((Vector((math.cos(a) * d, math.sin(a) * d, 0.02)), rs.uniform(0.2, 0.36), rs.uniform(0, TAU)))
    for k, (c, r, rot) in enumerate(pads if L < 2 else pads[::2]):
        n = g.lv(14, 9, 6)
        notch = 0.5
        col = "lily_pad"
        mid = g.vert(c + Vector((0, 0, 0.004)), 0.25, 0.0, 0.0, 0.0, UP)
        rings = []
        for ring_r, lift, tt in ((0.6, 0.0, 0.55), (1.0, 0.02, 1.0))[(0 if L == 0 else 1):]:
            ring = []
            for i in range(n + 1):
                a = rot + notch / 2 + (TAU - notch) * i / n
                wav = 0.006 * math.sin(a * 5 + k) if ring_r == 1.0 else 0.0
                q = c + Vector((math.cos(a) * r * ring_r, math.sin(a) * r * ring_r, lift + wav))
                ring.append(g.vert(q, tt, 0.0, 0.0, 0.0,
                                   (UP + Vector((math.cos(a), math.sin(a), 0)) * 0.15 * ring_r).normalized()))
            rings.append(ring)
        first = rings[0]
        for i in range(n):
            g.poly((mid, first[i], first[i + 1]), col)
        for A, B in zip(rings, rings[1:]):
            for i in range(n):
                g.poly((A[i], B[i], B[i + 1], A[i + 1]), col)
    flowers = [(pads[1][0] + Vector((0.12, 0.05, 0.0)), "lily_white"), (pads[4][0] + Vector((-0.1, 0.08, 0.0)), "lily_pink")]
    for c, col in flowers:
        ph = rs.random()
        if L == 2:
            g.ball(c + UP * 0.06, 0.12, col, 0, 0.0, 0.0, ph, t=0.8, scale=(1, 1, 0.6))
            continue
        layers = ((7, 0.15, 0.25), (7, 0.125, 0.6), (6, 0.095, 1.0)) if L == 0 else ((7, 0.15, 0.45),)
        for li, (n, ln, tilt) in enumerate(layers):
            g.petals(c + UP * (0.02 + 0.012 * li), UP, n, ln, ln * 0.46, col, cup=0.25, segs=g.lv(2, 1),
                     profile=p_pointed, flex=0.0, ph=ph, rot=li * 0.4, inner=0.014, tilt=tilt, t0=0.15, t1=1.0)
        g.fan(c + UP * 0.045, UP, 0.03, g.lv(8, 5), "center_yellow", 0.02, 0.0, 0.0, ph, 0.4, 1.0)
    if L == 0:                                      # bouton fermé
        c = pads[7][0] + Vector((0.05, -0.1, 0.0))
        g.ball(c + UP * 0.06, 0.045, "lily_pink", 1, 0.0, 0.0, 0.3, scale=(1, 1, 1.6))


# ================================================================= fiches
item("grass_short", "Grass_Short", grass_short)
item("grass_tall", "Grass_Tall", grass_tall)
item("grass_dry", "Grass_Dry", grass_dry)
item("flowers_meadow", "Flowers_Meadow", flowers_meadow, tags=("flowers", "detail"))
item("flowers_daisy", "Flowers_Daisy", flowers_daisy, tags=("flowers", "detail"))
item("flowers_lupin", "Flowers_Lupin", flowers_lupin, tags=("flowers", "detail"))
item("flowers_gentian", "Flowers_Gentian", flowers_gentian, tags=("flowers", "detail"))
item("flowers_edelweiss", "Flowers_Edelweiss", flowers_edelweiss, tags=("flowers", "detail"))
item("clover", "Clover", clover, tags=("flowers", "detail"))
item("fern", "Fern", fern)
item("fern_young", "Fern_Young", fern_young)
item("blueberry", "Blueberry", blueberry, tags=("bush", "detail"))
item("bush_rhodo", "Bush_Rhodo", bush_rhodo, tags=("bush", "flowers"))
item("juniper", "Juniper", juniper, tags=("bush",))
item("reeds", "Reeds", reeds, cat="lake", tags=("plant",), note="origin at waterline, roots 0.6 m below")
item("lily_pads", "Lily_Pads", lily_pads, cat="lake", tags=("plant",), note="origin at waterline")
item("flowers_buttercup", "Flowers_Buttercup", flowers_buttercup, tags=("flowers", "detail"))
item("flowers_poppy", "Flowers_Poppy", flowers_poppy, tags=("flowers", "detail"))


def claim():
    """Si le pack alpin est chargé (alp_all), lui retire nos fiches : ses build_all / export_all / write_manifest ne
    reconstruisent plus ces plantes avec les anciens constructeurs (elles vivent ici, avec leur propre matériau)."""
    import sys
    A = sys.modules.get("alp_lib")
    if A is not None:
        for iid in ITEMS:
            A.ITEMS.pop(iid, None)


claim()


# ----------------------------------------------------------------- palette, matériau
def palette_image(save_path=None):
    w, h = PAL_W * NCOL, 64
    img = bpy.data.images.get("AP_Flora")
    if img is None or tuple(img.size) != (w, h):
        if img:
            bpy.data.images.remove(img)
        img = bpy.data.images.new("AP_Flora", w, h, alpha=False)
    px = []
    for y in range(h):
        t = y / (h - 1)
        row = []
        for c in range(NCOL):
            if c < len(PALETTE):
                _, lo, hi = PALETTE[c]
                col = [lo[k] * (1 - t) + hi[k] * t for k in range(3)]
            else:
                col = [1.0, 0.0, 1.0]
            row += (col + [1.0]) * PAL_W
        px += row
    img.pixels = px
    if save_path:
        img.filepath_raw = save_path
        img.file_format = 'PNG'
        img.save()
    return img


# Couleurs du champ d'herbe pour l'aperçu Blender (sRGB) : sol du pré (terrain, _Grass), pied et pointe des brins
# (Resources/GrassField.mat : _RootColor, _TipColor, _TipAmount, _TipBright). En jeu, le shader les calcule.
FIELD_GROUND = (0.33, 0.56, 0.22)
FIELD_ROOT = (0.15, 0.33, 0.10)
FIELD_TIP = (0.47, 0.62, 0.25)
TOON_SHADE = (0.60, 0.64, 0.80)                 # teinte de l'ombre cartoon (terrain, _ShadeColor)


def srgb2lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def _sock(sockets, identifier):
    return next(s for s in sockets if s.identifier == identifier)


def toon_nodes(nt, albedo, vol=None):
    """Termine un matériau d'aperçu : aplats à deux tons (ombre teintée) à partir d'une sortie couleur « albedo ».
    vol (sortie 0-1) : part de la normale propre ; le reste vient de la verticale (la plante s'éclaire comme le pré)."""
    N = nt.nodes.new
    diff = N("ShaderNodeBsdfDiffuse")
    if vol is not None:
        geo = N("ShaderNodeNewGeometry")
        flip = N("ShaderNodeMath")                  # faces vues de dos : Blender retourne la normale, pas le jeu
        flip.operation = 'MULTIPLY_ADD'
        flip.inputs[1].default_value = -2.0
        flip.inputs[2].default_value = 1.0
        own = N("ShaderNodeVectorMath")
        own.operation = 'SCALE'
        nt.links.new(_sock(geo.outputs, "Backfacing"), flip.inputs[0])
        nt.links.new(_sock(geo.outputs, "Normal"), own.inputs[0])
        nt.links.new(flip.outputs[0], _sock(own.inputs, "Scale"))
        nmix = N("ShaderNodeMix")
        nmix.data_type = 'VECTOR'
        _sock(nmix.inputs, "A_Vector").default_value = (0.0, 0.0, 1.0)
        nrm = N("ShaderNodeVectorMath")
        nrm.operation = 'NORMALIZE'
        nt.links.new(vol, _sock(nmix.inputs, "Factor_Float"))
        nt.links.new(own.outputs[0], _sock(nmix.inputs, "B_Vector"))
        nt.links.new(_sock(nmix.outputs, "Result_Vector"), nrm.inputs[0])
        nt.links.new(nrm.outputs[0], _sock(diff.inputs, "Normal"))
    s2rgb = N("ShaderNodeShaderToRGB")
    ramp = N("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.3
    ramp.color_ramp.elements[0].color = srgb2lin(tuple(c * 0.86 for c in TOON_SHADE)) + (1.0,)
    ramp.color_ramp.elements[1].position = 0.46
    ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
    mul = N("ShaderNodeMix")
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    _sock(mul.inputs, "Factor_Float").default_value = 1.0
    emi = N("ShaderNodeEmission")
    out = N("ShaderNodeOutputMaterial")
    nt.links.new(diff.outputs[0], s2rgb.inputs[0])
    nt.links.new(s2rgb.outputs[0], ramp.inputs[0])
    nt.links.new(albedo, _sock(mul.inputs, "A_Color"))
    nt.links.new(ramp.outputs[0], _sock(mul.inputs, "B_Color"))
    nt.links.new(_sock(mul.outputs, "Result_Color"), emi.inputs[0])
    nt.links.new(emi.outputs[0], out.inputs[0])


def material():
    """Matériau d'aperçu (EEVEE), proche du rendu en jeu : aplats cartoon, parties « sol » (UV2.x) aux couleurs du
    champ d'herbe (pied sombre, couleur du sol, pointe claire). En jeu, c'est le shader NoHesi/Foliage qui fait foi."""
    m = bpy.data.materials.get(MATERIAL)
    if m is None:
        m = bpy.data.materials.new(MATERIAL)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    N = nt.nodes.new
    uv0 = N("ShaderNodeUVMap")
    uv0.uv_map = "UVMap"
    uv2 = N("ShaderNodeUVMap")
    uv2.uv_map = "UVStyle"
    tex = N("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = 'Linear'
    tex.extension = 'EXTEND'
    sep0 = N("ShaderNodeSeparateXYZ")
    sep2 = N("ShaderNodeSeparateXYZ")
    field = N("ShaderNodeValToRGB")                 # dégradé du champ d'herbe le long du brin
    field.color_ramp.elements[0].position = 0.0
    field.color_ramp.elements[0].color = srgb2lin(FIELD_ROOT) + (1.0,)
    field.color_ramp.elements[1].position = 1.0
    field.color_ramp.elements[1].color = srgb2lin(FIELD_TIP) + (1.0,)
    mid = field.color_ramp.elements.new(0.5)
    mid.color = srgb2lin(FIELD_GROUND) + (1.0,)
    mix = N("ShaderNodeMix")
    mix.data_type = 'RGBA'
    nt.links.new(uv0.outputs[0], tex.inputs[0])
    nt.links.new(uv0.outputs[0], sep0.inputs[0])
    nt.links.new(uv2.outputs[0], sep2.inputs[0])
    nt.links.new(sep0.outputs[1], field.inputs[0])
    nt.links.new(sep2.outputs[0], _sock(mix.inputs, "Factor_Float"))
    nt.links.new(tex.outputs[0], _sock(mix.inputs, "A_Color"))
    nt.links.new(field.outputs[0], _sock(mix.inputs, "B_Color"))
    toon_nodes(nt, _sock(mix.outputs, "Result_Color"), sep2.outputs[1])
    m.use_backface_culling = False
    return m


# ----------------------------------------------------------------- finition, construction, export
def get_col(name, parent=None):
    col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    parent = parent or bpy.context.scene.collection
    if col.name not in parent.children.keys():
        parent.children.link(col)
    return col


def clear_col(col):
    for o in list(col.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if isinstance(me, bpy.types.Mesh) and me.users == 0:
            bpy.data.meshes.remove(me)


def finish(g, name, col):
    bm = g.bm
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bm.verts.index_update()
    uv0 = bm.loops.layers.uv.new("UVMap")
    uv1 = bm.loops.layers.uv.new("UVWind")
    uv2 = bm.loops.layers.uv.new("UVStyle")
    for f in bm.faces:
        u = (f[g.lc] + 0.5) / NCOL
        f.smooth = True
        f.material_index = 0
        for lp in f.loops:
            v = lp.vert
            lp[uv0].uv = (u, min(0.995, max(0.005, v[g.lt])))
            lp[uv1].uv = (v[g.lw], v[g.lp])
            lp[uv2].uv = (v[g.l_sol], v[g.l_vol])
    normals = [Vector((v[g.ln[0]], v[g.ln[1]], v[g.ln[2]])).normalized() for v in bm.verts]
    me = bpy.data.meshes.new(name)
    me.materials.append(material())
    bm.to_mesh(me)
    bm.free()
    me.normals_split_custom_set_from_vertices(normals)
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    return ob, len(me.polygons)


def build(iid):
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    it = ITEMS[iid]
    col = get_col("FL_" + iid, get_col(ROOT))
    clear_col(col)
    tris = []
    for l in range(it["lods"]):
        g = FGeo(l, it["seed"])
        it["build"](g)
        _, n = finish(g, "%s_LOD%d" % (it["file"], l), col)
        tris.append(n)
    for n, ob in enumerate(sorted(col.objects, key=lambda o: o.name)):
        ob.hide_render = ob.hide_viewport = n > 0
    it["_tris"] = tris
    return tris


def build_all(ids=None):
    return {iid: build(iid) for iid in (ids or list(ITEMS))}


def export(iid, folder=OUT):
    os.makedirs(folder, exist_ok=True)
    it = ITEMS[iid]
    col = bpy.data.collections["FL_" + iid]
    obs = sorted(col.objects, key=lambda o: o.name)
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
                                 use_mesh_modifiers=False, mesh_smooth_type='OFF', add_leaf_bones=False,
                                 path_mode='AUTO', embed_textures=False)
    for k, o in enumerate(obs):
        o.hide_viewport = k > 0
        o.select_set(False)
    return path


def export_all(ids=None, folder=OUT):
    ids = ids or [i for i in ITEMS if bpy.data.collections.get("FL_" + i)]
    for iid in ids:
        export(iid, folder)
    palette_image(os.path.join(folder, PALETTE_FILE))
    return len(ids)


def bounds(iid):
    col = bpy.data.collections["FL_" + iid]
    ob = sorted(col.objects, key=lambda o: o.name)[0]
    xs = [v.co.x for v in ob.data.vertices]
    ys = [v.co.y for v in ob.data.vertices]
    zs = [v.co.z for v in ob.data.vertices]
    return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))


def manifest_entries():
    out = []
    for iid, it in ITEMS.items():
        col = bpy.data.collections.get("FL_" + iid)
        if not col or not col.objects:
            continue
        lo, hi = bounds(iid)
        w, d, h = hi.x - lo.x, hi.y - lo.y, hi.z
        e = {"id": iid, "file": it["file"], "category": it["cat"], "tags": list(it["tags"]),
             "size": [round(w, 2), round(h, 2), round(d, 2)], "ymin": round(lo.z, 2),
             "footprint": [round(w, 2), round(d, 2)], "lods": len(col.objects),
             "tris": [len(o.data.polygons) for o in sorted(col.objects, key=lambda o: o.name)],
             "collider": "none", "material": MATERIAL}
        if "note" in it:
            e["note"] = it["note"]
        out.append(e)
    return out


def merge_manifest(folder=OUT):
    """Remplace / ajoute les fiches Flora dans AlpPack_manifest.json sans toucher aux autres."""
    path = os.path.join(folder, "AlpPack_manifest.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    mine = {e["id"]: e for e in manifest_entries()}
    items = [mine.pop(e["id"]) if e["id"] in mine else e for e in data["items"]]
    items += list(mine.values())
    data["items"] = items
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
    return len(items)


def deliver(ids=None):
    t = build_all(ids)
    export_all(ids)
    merge_manifest()
    return t
