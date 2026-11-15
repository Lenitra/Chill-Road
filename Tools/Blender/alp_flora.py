"""Végétation au sol « Flora » (v3) : herbes, fleurs, fougères, buissons bas et plantes de lac, détaillés et doux.

Chaîne propre, indépendante de la palette du pack alpin (AP_Palette) :
- palette dédiée AlpFlora_Palette.png (64 colonnes FIXES : on n'ajoute qu'à la fin), dégradé base → pointe ;
- UV0 = (colonne, t le long de l'élément : 0 à la base, 1 à la pointe) → chaque brin, pétale ou fronde a son dégradé ;
- UV1 = (souplesse au vent en m : flex × s², s = longueur depuis le sol le long de la tige ; phase 0-1) ;
- normales douces (dômes, sphères des buissons) écrites en normales personnalisées : éclairage velouté, sans facettes ;
- faces simples : le shader NoHesi/Foliage dessine les deux côtés ;
- matériau AP_Flora → Unity : NoHesi/Foliage (vent, translucidité, fondu à distance).
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
    ("grass", (0.19, 0.37, 0.11), (0.53, 0.75, 0.27)),
    ("grass_lush", (0.13, 0.32, 0.09), (0.42, 0.70, 0.22)),
    ("grass_yellow", (0.27, 0.37, 0.10), (0.74, 0.78, 0.34)),
    ("grass_dry", (0.34, 0.34, 0.14), (0.92, 0.84, 0.50)),
    ("grass_blue", (0.09, 0.21, 0.15), (0.44, 0.60, 0.48)),
    ("seed", (0.36, 0.33, 0.18), (0.80, 0.70, 0.46)),
    ("seed_purple", (0.28, 0.20, 0.24), (0.66, 0.48, 0.52)),
    ("stem", (0.18, 0.36, 0.10), (0.42, 0.64, 0.22)),
    ("leaf", (0.12, 0.30, 0.08), (0.40, 0.66, 0.22)),
    ("leaf_dark", (0.07, 0.20, 0.07), (0.26, 0.48, 0.18)),
    ("leaf_light", (0.21, 0.42, 0.11), (0.56, 0.78, 0.30)),
    ("leaf_grey", (0.30, 0.36, 0.28), (0.68, 0.74, 0.64)),
    ("fern", (0.10, 0.30, 0.07), (0.46, 0.74, 0.20)),
    ("fern_light", (0.20, 0.42, 0.10), (0.70, 0.88, 0.34)),
    ("petal_white", (0.78, 0.78, 0.72), (1.00, 1.00, 0.97)),
    ("petal_yellow", (0.86, 0.58, 0.03), (1.00, 0.90, 0.28)),
    ("petal_pink", (0.72, 0.30, 0.48), (0.99, 0.66, 0.80)),
    ("petal_magenta", (0.52, 0.08, 0.36), (0.90, 0.34, 0.66)),
    ("petal_blue", (0.10, 0.22, 0.30), (0.16, 0.30, 0.92)),
    ("petal_violet", (0.28, 0.22, 0.60), (0.64, 0.56, 0.96)),
    ("lupin_purple", (0.26, 0.14, 0.56), (0.78, 0.56, 0.94)),
    ("lupin_pink", (0.58, 0.14, 0.40), (0.99, 0.70, 0.84)),
    ("lupin_blue", (0.14, 0.20, 0.60), (0.58, 0.70, 0.99)),
    ("lupin_white", (0.70, 0.68, 0.74), (1.00, 0.98, 0.96)),
    ("clover_head", (0.62, 0.24, 0.42), (0.97, 0.72, 0.84)),
    ("clover_white", (0.74, 0.74, 0.66), (1.00, 0.98, 0.94)),
    ("center_yellow", (0.78, 0.46, 0.02), (1.00, 0.82, 0.16)),
    ("felt", (0.72, 0.74, 0.70), (0.98, 0.98, 0.95)),
    ("felt_center", (0.66, 0.62, 0.36), (0.94, 0.90, 0.66)),
    ("rhodo_leaf", (0.07, 0.20, 0.09), (0.30, 0.52, 0.22)),
    ("rhodo_flower", (0.64, 0.06, 0.30), (0.99, 0.42, 0.62)),
    ("juniper", (0.08, 0.22, 0.17), (0.38, 0.58, 0.46)),
    ("juniper_tip", (0.16, 0.34, 0.25), (0.58, 0.74, 0.56)),
    ("berry", (0.06, 0.08, 0.22), (0.30, 0.38, 0.64)),
    ("twig", (0.24, 0.16, 0.08), (0.46, 0.34, 0.18)),
    ("twig_red", (0.30, 0.10, 0.06), (0.56, 0.30, 0.14)),
    ("reed", (0.26, 0.34, 0.10), (0.66, 0.72, 0.32)),
    ("cattail", (0.20, 0.10, 0.04), (0.44, 0.26, 0.12)),
    ("lily_pad", (0.07, 0.22, 0.09), (0.30, 0.54, 0.20)),
    ("lily_white", (0.84, 0.80, 0.80), (1.00, 0.99, 0.98)),
    ("lily_pink", (0.80, 0.42, 0.56), (0.99, 0.84, 0.90)),
    ("clover_leaf", (0.12, 0.32, 0.09), (0.42, 0.68, 0.24)),
    ("bark", (0.18, 0.12, 0.07), (0.40, 0.28, 0.16)),
    ("poppy", (0.70, 0.06, 0.04), (0.98, 0.24, 0.12)),
    ("center_dark", (0.06, 0.05, 0.03), (0.22, 0.18, 0.10)),
    ("leaf_yellow", (0.36, 0.44, 0.08), (0.80, 0.84, 0.30)),
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


# ----------------------------------------------------------------- constructeur
class FGeo:
    """bmesh à couleur de palette par face ; par sommet : t (dégradé), souplesse au vent, phase, normale douce."""

    def __init__(self, level=0, seed=0):
        bm = self.bm = bmesh.new()
        self.lc = bm.faces.layers.int.new("col")
        self.lt = bm.verts.layers.float.new("t")
        self.lw = bm.verts.layers.float.new("w")
        self.lp = bm.verts.layers.float.new("ph")
        self.ln = [bm.verts.layers.float.new(k) for k in ("nx", "ny", "nz")]
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
def tuft(g, n, radius, lmin, lmax, wmin, wmax, cols, tilt=(0.05, 0.5), bend=(0.2, 1.0), twist=0.7, seed=0,
         center_tall=0.25, z=-0.02):
    """Paramètres d'une touffe d'herbe (identiques à tous les niveaux : les niveaux en prennent un sous-ensemble)."""
    rs = random.Random(seed)
    out = []
    for k in range(n):
        a = rs.uniform(0, TAU)
        d = radius * math.sqrt(rs.random())
        o = d / max(radius, 1e-6)
        out.append(dict(root=Vector((math.cos(a) * d, math.sin(a) * d, z)), az=a + rs.uniform(-0.7, 0.7),
                        L=rs.uniform(lmin, lmax) * (1.0 - center_tall * o), W=rs.uniform(wmin, wmax),
                        tilt=rs.uniform(*tilt) * (0.45 + 0.9 * o), bend=rs.uniform(*bend),
                        twist=rs.uniform(-twist, twist), col=cols[rs.randrange(len(cols))], ph=rs.random()))
    return out


def emit_blades(g, blades, keep=(1.0, 0.42, 0.16), segs=(4, 2, 1), widen=(1.0, 1.45, 2.3), flex=1.2,
                profile=p_grass, fold=0.0):
    L = g.level
    step = max(1, int(round(1.0 / keep[L])))
    for b in blades[::step]:
        g.blade(b["root"], b["az"], b["L"], b["W"] * widen[L], b["col"], b["tilt"], b["bend"], segs[L],
                b["twist"] if L == 0 else 0.0, profile, flex=flex, ph=b["ph"], fold=fold if L == 0 else 0.0)


def stem_path(root, az, H, tilt, bend, segs):
    pts = arc(root, az, H, tilt, bend, segs, 1.6)
    return pts, (pts[-1] - pts[-2]).normalized()


def stem(g, pts, r0, r1, col="stem", flex=1.0, ph=0.0, sides=3, s0=0.0):
    n = len(pts)
    radii = [r0 + (r1 - r0) * i / max(n - 1, 1) for i in range(n)]
    return g.tube(pts, radii, col, sides, s0, flex, ph, 0.0, 1.0, soft=0.55)


def seed_stalk(g, root, az, H, ph, kind="panicle", col="seed", flex=1.0, tilt=None, bend=None):
    """Hampe de graminée : panicule (épillets en losange qui pendent) ou épi (cylindre arrondi)."""
    L = g.level
    tilt = g.R(0.04, 0.2) if tilt is None else tilt
    bend = g.R(0.15, 0.45) if bend is None else bend
    pts = arc(root, az, H, tilt, bend, g.lv(6, 3, 2), 1.5)
    if L < 2:
        stem(g, pts, 0.0032, 0.0018, "stem" if L == 0 else col, flex, ph, 3)
    else:
        side = Vector((-math.sin(az), math.cos(az), 0))
        g.strip(pts, [0.006, 0.004, 0.0], [side] * 3, col, flex=flex, ph=ph)
        return
    if kind == "panicle":
        n = g.lv(13, 5)
        for k in range(n):
            u = 0.68 + 0.32 * k / n
            p, tang, s = sample(pts, u)
            e1, e2 = basis(tang)
            a = k * 2.4
            o = e1 * math.cos(a) + e2 * math.sin(a)
            d = (o * 0.55 + tang * 0.45 - UP * 0.25).normalized()
            g.ribbon_dir(p, d, g.R(0.03, 0.05) * (1.15 - 0.4 * (u - 0.68) / 0.32), g.lv(0.009, 0.014), col,
                         o.cross(d).cross(d) * -1.0, 2, profile=p_spindle, s0=s, flex=flex, ph=ph, soft=0.7,
                         t0=0.4, t1=1.0)
    else:                                           # épi (fléole) : cylindre arrondi
        p0, tang, s = sample(pts, 0.8)
        spike = [p0 + tang * (0.12 * H * k / 4) for k in range(5)]
        rad = [0.0035, 0.0065, 0.007, 0.0062, 0.0]
        g.tube(spike, rad, col, g.lv(5, 3), s, flex, ph, 0.3, 1.0, soft=0.8)


def basal_leaves(g, n, L, W, col, seed, profile=p_leaf, tilt=(0.9, 1.25), bend=(0.2, 0.6), fold=0.3, segs=None,
                 radius=0.03, flex=0.6, cols=None):
    rs = random.Random(seed)
    for k in range(n):
        a = TAU * k / n + rs.uniform(-0.3, 0.3)
        root = Vector((math.cos(a) * radius, math.sin(a) * radius, 0.0))
        g.blade(root, a, L * rs.uniform(0.8, 1.15), W * rs.uniform(0.85, 1.15),
                cols[rs.randrange(len(cols))] if cols else col, rs.uniform(*tilt), rs.uniform(*bend),
                segs or g.lv(4, 2, 1), 0.0, profile, flex=flex, ph=rs.random(), fold=fold if g.level == 0 else 0.0,
                soft=0.7)


def flower_head_daisy(g, top, facing, r, s0, ph, flex, petal_col="petal_white", center="center_yellow", n=None):
    L = g.level
    n = n or g.lv(15, 8)
    g.petals(top, facing, n, r, r * 0.34, petal_col, cup=-0.05, droop=0.25, segs=g.lv(2, 1), profile=p_ligule,
             s0=s0, flex=flex, ph=ph, rot=g.R(0, TAU), inner=r * 0.18)
    g.fan(top + facing * r * 0.02, facing, r * 0.28, g.lv(8, 5), center, r * 0.16, s0, flex, ph, 0.25, 1.0)


def flower_cup(g, top, facing, r, col, s0, ph, flex, n=5, center="center_yellow", cup=0.9, tilt=0.45):
    """Renoncule, coquelicot : pétales ronds en coupe."""
    g.petals(top, facing, n, r, r * 1.05, col, cup=cup, segs=g.lv(2, 1), profile=p_round, s0=s0, flex=flex, ph=ph,
             rot=g.R(0, TAU), jitter=0.05, tilt=tilt)
    g.fan(top + facing * r * 0.12, facing, r * 0.3, g.lv(6, 4), center, r * 0.18, s0, flex, ph, 0.2, 1.0)


def trefoil(g, top, r, col, s0, ph, flex, rot=0.0):
    nrm = UP
    for k in range(3):
        a = rot + TAU * k / 3
        d = Vector((math.cos(a), math.sin(a), 0.12)).normalized()
        g.ribbon_dir(top, d, r, r * 0.85, col, nrm, g.lv(2, 1), droop=0.12, profile=p_obovate, s0=s0, flex=flex,
                     ph=ph, soft=0.65, nfix=UP, t0=0.35, t1=1.0)


def palmate(g, top, n, r, col, s0, ph, flex, rot=0.0):
    for k in range(n):
        a = rot + TAU * k / n
        d = Vector((math.cos(a), math.sin(a), 0.18)).normalized()
        g.ribbon_dir(top, d, r * g.R(0.85, 1.05), r * 0.24, col, UP, g.lv(3, 1), droop=0.35, profile=p_lance,
                     s0=s0, flex=flex, ph=ph, fold=0.25 if g.level == 0 else 0.0, soft=0.65,
                     nfix=(UP + d * 0.3).normalized(), t0=0.3, t1=1.0)


def frond(g, root, az, Lf, tilt, bend, col, pairs, plen, pw, ph, flex=0.9, rachis="stem", sweep=0.5, tbase=0.15):
    """Fronde de fougère : rachis arqué, pennes appariées dont la longueur suit un profil lancéolé."""
    L = g.level
    pts = arc(root, az, Lf, tilt, bend, g.lv(8, 4, 2), 1.3)
    side0 = Vector((-math.sin(az), math.cos(az), 0.0))
    if L == 2:                                     # lointain : la fronde entière en un ruban
        n = len(pts)
        g.strip(pts, [plen * 1.2 * math.sin(math.pi * min(1.0, 0.15 + i / (n - 1))) ** 0.7 for i in range(n)],
                [side0] * n, col, flex=flex, ph=ph, t0=tbase, t1=1.0, soft=0.8)
        return
    n = len(pts)
    g.strip(pts, [0.009 * (1 - i / n) + 0.0015 for i in range(n)], [side0] * n, rachis, flex=flex, ph=ph, t0=0.1,
            t1=0.9, soft=0.7)
    start = 0.16
    for j in range(pairs):
        u = start + (0.98 - start) * (j + 0.5) / pairs
        p, tang, s = sample(pts, u)
        nrm = tang.cross(side0).normalized()
        if nrm.z < 0:
            nrm = -nrm
        env = max(math.sin(math.pi * min(1.0, (u - start * 0.6) / (1.0 - start * 0.6))), 0.0) ** 0.75
        pl = plen * env * (0.92 + 0.16 * g.h(j))
        if pl < 0.012:
            continue
        for sgn in (-1, 1):
            dirp = (side0 * sgn * math.cos(sweep) + tang * math.sin(sweep)).normalized()
            g.ribbon_dir(p, dirp, pl, pw * (0.55 + 0.45 * env), col, nrm, g.lv(2, 1), droop=0.25, curl=0.0,
                         profile=p_lance, s0=s, flex=flex, ph=ph, soft=0.7, nfix=(nrm + UP * 0.5).normalized(),
                         t0=tbase + (1 - tbase) * u * 0.9, t1=tbase + (1 - tbase) * min(1.0, u * 0.9 + 0.1),
                         fold=0.15 if L == 0 else 0.0)


def fiddlehead(g, root, H, col, ph, flex=0.5):
    pts = []
    for k in range(10):
        u = k / 9
        if u < 0.6:
            pts.append(Vector(root) + Vector((0.01 * u, 0, H * u / 0.6)))
        else:
            a = (u - 0.6) / 0.4 * 4.2
            rr = 0.03 * (1.0 - 0.6 * (u - 0.6) / 0.4)
            pts.append(Vector(root) + Vector((0.006 + rr * (1 - math.cos(a)) - 0.0, 0, H + rr * math.sin(a))))
    g.tube(pts, [0.007 - 0.004 * k / 9 for k in range(10)], col, 3, 0.0, flex, ph, 0.2, 1.0, soft=0.7)


# FGeo.h : hasard stable par clé
def _h(self, *key):
    k = zlib.crc32(repr((self.seed,) + tuple(round(float(v), 3) for v in key)).encode())
    return (k % 100003) / 100003.0


FGeo.h = _h


# ================================================================= herbes
def grass_short(g):
    g.dome, g.up = Vector((0, 0, -0.35)), 0.9
    b = tuft(g, 84, 0.18, 0.15, 0.36, 0.012, 0.02, ["grass"] * 6 + ["grass_yellow", "grass_lush", "grass_lush"],
             seed=g.seed, tilt=(0.05, 0.55), bend=(0.2, 1.1))
    emit_blades(g, b, keep=(1.0, 0.4, 0.15), segs=(3, 2, 1), widen=(1.0, 1.5, 2.6), flex=1.6)


def grass_tall(g):
    g.dome, g.up = Vector((0, 0, -0.6)), 0.9
    b = tuft(g, 54, 0.2, 0.42, 0.86, 0.012, 0.02, ["grass"] * 5 + ["grass_lush", "grass_yellow", "grass_lush"],
             seed=g.seed, tilt=(0.04, 0.4), bend=(0.25, 1.2), twist=0.9)
    emit_blades(g, b, keep=(1.0, 0.4, 0.18), segs=(4, 2, 1), widen=(1.0, 1.5, 2.4), flex=1.0)
    rs = random.Random(g.seed + 5)
    n = g.lv(6, 3, 2)
    for k in range(6):
        a = rs.uniform(0, TAU)
        H = rs.uniform(0.7, 1.05)
        kind = "panicle" if k % 3 else "spike"
        ph = rs.random()
        if k < n:
            seed_stalk(g, (math.cos(a) * 0.05, math.sin(a) * 0.05, -0.02), a, H, ph, kind,
                       "seed" if k % 2 else "seed_purple", 0.9)


def grass_dry(g):
    g.dome, g.up = Vector((0, 0, -0.45)), 0.75
    b = tuft(g, 96, 0.15, 0.24, 0.5, 0.009, 0.015, ["grass_dry"] * 5 + ["grass_yellow"] * 2 + ["grass"],
             seed=g.seed, tilt=(0.15, 0.9), bend=(0.6, 1.7), twist=0.5, center_tall=0.35)
    emit_blades(g, b, keep=(1.0, 0.38, 0.14), segs=(4, 2, 1), widen=(1.0, 1.6, 2.8), flex=1.3)
    rs = random.Random(g.seed + 9)
    for k in range(g.lv(3, 2, 0)):
        a = rs.uniform(0, TAU)
        seed_stalk(g, (math.cos(a) * 0.03, math.sin(a) * 0.03, -0.02), a, rs.uniform(0.45, 0.65), rs.random(),
                   "spike", "seed", 1.0)


# ================================================================= fleurs
def flowers_meadow(g):
    """Bouquet de prairie : marguerites, renoncules, trèfle des prés, campanules, un coquelicot, sur un fond d'herbe."""
    g.dome, g.up = Vector((0, 0, -0.4)), 0.8
    L = g.level
    b = tuft(g, 20, 0.2, 0.12, 0.3, 0.009, 0.014, ["grass", "grass", "grass_lush"], seed=g.seed + 1)
    emit_blades(g, b, keep=(1.0, 0.4, 0.2), segs=(3, 2, 1), widen=(1.0, 1.5, 2.4), flex=1.4)
    rs = random.Random(g.seed)
    plan = ["daisy"] * 4 + ["butter"] * 4 + ["clover"] * 2 + ["bell"] * 2 + ["poppy"]
    for k, kind in enumerate(plan):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.02, 0.2)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        az = a + rs.uniform(-0.5, 0.5)
        ph = rs.random()
        H = {"daisy": rs.uniform(0.3, 0.48), "butter": rs.uniform(0.25, 0.42), "clover": rs.uniform(0.14, 0.24),
             "bell": rs.uniform(0.28, 0.4), "poppy": rs.uniform(0.38, 0.5)}[kind]
        tilt, bend = rs.uniform(0.04, 0.22), rs.uniform(0.05, 0.3)
        if L == 2:
            col = {"daisy": "petal_white", "butter": "petal_yellow", "clover": "clover_head", "bell": "petal_violet",
                   "poppy": "poppy"}[kind]
            if k % 2 == 0 or kind == "poppy":
                pts, tang = stem_path(root, az, H, tilt, bend, 1)
                g.ball(pts[-1], 0.028, col, 0, H, 1.0, ph, t=0.8, scale=(1, 1, 0.45))
            continue
        pts, tang = stem_path(root, az, H, tilt, bend, g.lv(4, 2))
        if kind != "bell":
            stem(g, pts, 0.0026, 0.0018, "stem", 1.0, ph, 3)
        top = pts[-1]
        facing = (tang * 0.35 + UP * 0.65 + Vector((rs.uniform(-0.2, 0.2), rs.uniform(-0.2, 0.2), 0))).normalized()
        if kind == "daisy":
            flower_head_daisy(g, top, facing, rs.uniform(0.03, 0.036), H, ph, 1.0)
            if L == 0:
                p, t2, s = sample(pts, 0.35)
                g.ribbon_dir(p, (t2 + Vector((math.cos(az + 1.6), math.sin(az + 1.6), 0))).normalized(), 0.05, 0.012,
                             "leaf", UP, 3, droop=0.3, profile=p_lance, s0=s, flex=1.0, ph=ph, soft=0.6)
        elif kind == "butter":
            flower_cup(g, top, facing, rs.uniform(0.016, 0.02), "petal_yellow", H, ph, 1.0)
            if L == 0:                              # seconde fleur sur un rameau
                p, t2, s = sample(pts, 0.6)
                br = arc(p, az + 2.2, 0.08, 0.6, 0.2, 2)
                stem(g, br, 0.0016, 0.0012, "stem", 1.0, ph, 3, s)
                flower_cup(g, br[-1], (UP + (br[-1] - br[-2]).normalized() * 0.4).normalized(), 0.014,
                           "petal_yellow", s + 0.08, ph, 1.0)
                p, t2, s = sample(pts, 0.2)
                palmate(g, p, 5, 0.03, "leaf", s, ph, 1.0, rs.uniform(0, TAU))
        elif kind == "clover":
            g.ball(top + UP * 0.012, 0.018, "clover_head", g.lv(1, 0), H, 1.0, ph, scale=(1, 1, 1.15))
            if L == 0:
                p, t2, s = sample(pts, 0.5)
                trefoil(g, p, 0.022, "clover_leaf", s, ph, 1.0, rs.uniform(0, TAU))
        elif kind == "bell":                        # campanule : tige arquée, clochettes pendantes
            pts, tang = stem_path(root, az, H, rs.uniform(0.1, 0.25), rs.uniform(0.7, 1.1), g.lv(5, 2))
            stem(g, pts, 0.0022, 0.0015, "stem", 1.2, ph, 3)
            for j, u in enumerate((1.0, 0.82, 0.66)[:g.lv(3, 1)]):
                p, t2, s = sample(pts, u)
                ax = (t2 * 0.4 - UP * 0.9 + Vector((math.cos(az + j), math.sin(az + j), 0)) * 0.25).normalized()
                g.bell(p, ax, 0.03, 0.004, 0.014, "petal_violet", 5, g.lv(3, 1), 1.6, 0.35, s, 1.2, ph, 0.2, 1.0,
                       rot=rs.uniform(0, TAU))
        elif kind == "poppy":
            flower_cup(g, top, facing, rs.uniform(0.036, 0.042), "poppy", H, ph, 1.0, n=4, center="center_dark",
                       cup=0.6, tilt=0.5)


def flowers_daisy(g):
    """Touffe de marguerites : capitules blancs à cœur jaune, feuilles basales dentées."""
    g.dome, g.up = Vector((0, 0, -0.45)), 0.8
    L = g.level
    b = tuft(g, 14, 0.15, 0.12, 0.26, 0.009, 0.014, ["grass", "grass_lush"], seed=g.seed + 1)
    emit_blades(g, b, keep=(1.0, 0.4, 0.2), segs=(3, 2, 1), widen=(1.0, 1.5, 2.4), flex=1.4)
    basal_leaves(g, g.lv(9, 4, 0), 0.11, 0.028, "leaf", g.seed + 2, p_lance, tilt=(0.8, 1.2), bend=(0.3, 0.7))
    rs = random.Random(g.seed)
    for k in range(10):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.0, 0.15)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        az = a + rs.uniform(-0.4, 0.4)
        H = rs.uniform(0.3, 0.56)
        ph = rs.random()
        tilt, bend = rs.uniform(0.04, 0.25), rs.uniform(0.05, 0.35)
        if L == 2:
            if k % 2 == 0:
                pts, tang = stem_path(root, az, H, tilt, bend, 1)
                g.ball(pts[-1], 0.034, "petal_white", 0, H, 1.0, ph, t=0.9, scale=(1, 1, 0.45))
            continue
        if L == 1 and k % 2:
            continue
        pts, tang = stem_path(root, az, H, tilt, bend, g.lv(4, 2))
        stem(g, pts, 0.0028, 0.0018, "stem", 1.0, ph, 3)
        facing = (tang * 0.3 + UP * 0.7 + Vector((rs.uniform(-0.25, 0.25), rs.uniform(-0.25, 0.25), 0))).normalized()
        flower_head_daisy(g, pts[-1], facing, rs.uniform(0.032, 0.04), H, ph, 1.0)
        if L == 0 and k % 3 == 0:                   # bouton fermé
            p, t2, s = sample(pts, 0.7)
            br = arc(p, az - 2.0, 0.07, 0.5, 0.1, 2)
            stem(g, br, 0.0016, 0.0013, "stem", 1.0, ph, 3, s)
            g.ball(br[-1], 0.008, "stem", 1, s + 0.07, 1.0, ph, t=0.8, scale=(1, 1, 0.8))


def flowers_lupin(g):
    """Lupins : épis de fleurs en spirale (dégradé le long de l'épi), feuilles palmées."""
    g.dome, g.up = Vector((0, 0, -0.7)), 0.7
    L = g.level
    rs = random.Random(g.seed)
    cols = ["lupin_purple", "lupin_pink", "lupin_blue", "lupin_purple", "lupin_white"]
    for k in range(g.lv(7, 4, 2)):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.03, 0.14)
        root = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
        pts = [root, root + Vector((math.cos(a) * 0.04, math.sin(a) * 0.04, 0.12)),
               root + Vector((math.cos(a) * 0.08, math.sin(a) * 0.08, 0.2))]
        if L < 2:
            stem(g, pts, 0.003, 0.0022, "stem", 0.5, rs.random(), 3)
        palmate(g, pts[-1], g.lv(9, 6, 5), rs.uniform(0.07, 0.09), "leaf", 0.2, rs.random(), 0.5, rs.uniform(0, TAU))
    for k in range(5):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.0, 0.08)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        H = rs.uniform(0.62, 0.95)
        ph = rs.random()
        col = cols[k % len(cols)]
        pts, tang = stem_path(root, a, H, rs.uniform(0.02, 0.12), rs.uniform(0.02, 0.15), g.lv(7, 3, 1))
        if L == 2:
            p0, t0_, s0_ = sample(pts, 0.5)
            g.strip([p0, pts[-1] + UP * 0.02], [0.05, 0.0], [Vector((-math.sin(a), math.cos(a), 0))] * 2, col,
                    flex=0.5, ph=ph, t0=0.2, t1=1.0, soft=0.9)
            g.strip([p0, pts[-1] + UP * 0.02], [0.05, 0.0], [Vector((math.cos(a), math.sin(a), 0))] * 2, col,
                    flex=0.5, ph=ph, t0=0.2, t1=1.0, soft=0.9)
            continue
        stem(g, pts, 0.0045, 0.002, "stem", 0.5, ph, 3)
        # fleurons en spirale sur la moitié haute ; boutons verts serrés à la pointe
        n = g.lv(44, 14)
        for j in range(n):
            u = 0.45 + 0.55 * j / n
            p, t2, s = sample(pts, u)
            e1, e2 = basis(t2)
            ang = j * 2.4 + k
            o = e1 * math.cos(ang) + e2 * math.sin(ang)
            size = 0.034 * (1.0 - 0.55 * (u - 0.45) / 0.55)
            tu = (u - 0.45) / 0.55
            if tu > 0.82:
                g.ball(p + o * 0.006, size * 0.45, "stem", 0, s, 0.5, ph, t=0.7 + 0.3 * tu, scale=(1, 1, 1.3))
                continue
            base = p + o * 0.007
            dn = (o * 0.8 + t2 * 0.35).normalized()
            # étendard dressé (pétale rond replié vers le haut) + carène en cuillère
            g.ribbon_dir(base, (dn + t2 * 0.9).normalized(), size, size * g.lv(0.9, 1.4), col, o, g.lv(2, 1),
                         curl=-0.35, profile=p_round, s0=s, flex=0.5, ph=ph, soft=0.6, nfix=(o + UP * 0.3).normalized(),
                         t0=tu * 0.85, t1=tu * 0.85 + 0.12)
            if L == 0:
                g.ribbon_dir(base, (dn - t2 * 0.15).normalized(), size * 0.75, size * 0.5, col, t2, 1, curl=0.4,
                             profile=p_spindle, s0=s, flex=0.5, ph=ph, soft=0.6, nfix=(o + UP * 0.3).normalized(),
                             t0=tu * 0.85, t1=tu * 0.85 + 0.08)


def flowers_gentian(g):
    """Gentianes : grandes trompettes bleu profond presque sans tige, petites gentianes printanières, rosette de
    feuilles plaquées au sol."""
    g.dome, g.up = Vector((0, 0, -0.2)), 0.8
    L = g.level
    rs = random.Random(g.seed)
    b = tuft(g, 12, 0.18, 0.06, 0.14, 0.006, 0.01, ["grass", "grass_yellow"], seed=g.seed + 1, tilt=(0.2, 0.8))
    emit_blades(g, b, keep=(1.0, 0.4, 0.0001), segs=(3, 1, 1), widen=(1.0, 1.5, 2.0), flex=1.4)
    for k in range(4):
        a = TAU * k / 4 + rs.uniform(-0.4, 0.4)
        d = rs.uniform(0.04, 0.13)
        c = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
        basal_leaves_at(g, c, g.lv(6, 3, 0), 0.04, 0.016, "leaf_dark", g.seed + 10 + k)
        H = rs.uniform(0.025, 0.05)
        ax = (UP + Vector((math.cos(a), math.sin(a), 0)) * rs.uniform(0.1, 0.35)).normalized()
        ph = rs.random()
        if L == 2:
            g.ball(c + ax * (H + 0.03), 0.022, "petal_blue", 0, 0.08, 0.6, ph, t=0.9, scale=(1, 1, 1.3))
            continue
        stem(g, [c, c + ax * H], 0.004, 0.004, "stem", 0.6, ph, 3)
        g.bell(c + ax * H, ax, 0.065, 0.009, 0.028, "petal_blue", 5, g.lv(3, 1), 1.9, 0.38, H, 0.6, ph, 0.05, 1.0,
               rot=rs.uniform(0, TAU), flat_rim=0.25)
    if L == 0:                                      # gentianes printanières : étoiles à 5 pétales
        for k in range(5):
            a = rs.uniform(0, TAU)
            d = rs.uniform(0.08, 0.2)
            c = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
            H = rs.uniform(0.04, 0.07)
            ph = rs.random()
            stem(g, [c, c + UP * H], 0.002, 0.0018, "stem", 1.0, ph, 3)
            g.bell(c + UP * H, UP, 0.012, 0.002, 0.003, "petal_blue", 5, 1, 1.0, 0.0, H, 1.0, ph, 0.1, 0.6)
            g.petals(c + UP * (H + 0.012), UP, 5, 0.017, 0.014, "petal_blue", cup=0.15, segs=1, profile=p_round,
                     s0=H, flex=1.0, ph=ph, jitter=0.05, t0=0.7, t1=1.0)


def basal_leaves_at(g, c, n, L, W, col, seed, flex=0.3):
    rs = random.Random(seed)
    for k in range(n):
        a = TAU * k / max(n, 1) + rs.uniform(-0.3, 0.3)
        g.ribbon_dir(Vector(c) + Vector((0, 0, 0.004)), Vector((math.cos(a), math.sin(a), 0.25)), L * rs.uniform(0.8, 1.2),
                     W, col, UP, g.lv(3, 1), droop=0.2, profile=p_leaf, s0=0.0, flex=flex, ph=rs.random(),
                     fold=0.2 if g.level == 0 else 0.0, soft=0.6, nfix=UP, t0=0.2, t1=1.0)


def flowers_edelweiss(g):
    """Edelweiss : étoiles de bractées feutrées blanches autour de capitules jaunâtres, feuilles grises étroites."""
    g.dome, g.up = Vector((0, 0, -0.2)), 0.8
    L = g.level
    rs = random.Random(g.seed)
    b = tuft(g, 10, 0.16, 0.05, 0.12, 0.006, 0.009, ["grass_dry", "grass_yellow", "grass"], seed=g.seed + 1,
             tilt=(0.2, 0.8))
    emit_blades(g, b, keep=(1.0, 0.4, 0.0001), segs=(3, 1, 1), widen=(1.0, 1.5, 2.0), flex=1.4)
    for k in range(5):
        a = TAU * k / 5 + rs.uniform(-0.4, 0.4)
        d = rs.uniform(0.03, 0.14)
        c = Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
        basal_leaves_at(g, c, g.lv(7, 3, 0), 0.05, 0.011, "leaf_grey", g.seed + 20 + k)
        H = rs.uniform(0.07, 0.15)
        ph = rs.random()
        pts, tang = stem_path(c, a, H, rs.uniform(0.0, 0.2), rs.uniform(0.0, 0.2), g.lv(3, 1, 1))
        top = pts[-1]
        facing = (UP * 0.85 + tang * 0.15).normalized()
        if L == 2:
            g.fan(top, facing, 0.03, 8, "felt", 0.004, H, 0.8, ph, 0.9, 0.95, star=0.45)
            continue
        stem(g, pts, 0.003, 0.0025, "leaf_grey", 0.8, ph, 3)
        if L == 0:                                  # deux feuilles caulinaires
            for j in (0.4, 0.7):
                p, t2, s = sample(pts, j)
                g.ribbon_dir(p, (Vector((math.cos(a + j * 5), math.sin(a + j * 5), 0.6))).normalized(), 0.028, 0.007,
                             "leaf_grey", UP, 2, droop=0.2, profile=p_lance, s0=s, flex=0.8, ph=ph, soft=0.6)
        g.petals(top, facing, g.lv(9, 6), rs.uniform(0.032, 0.042), 0.016, "felt", cup=0.12, droop=0.05,
                 segs=g.lv(2, 1), profile=p_pointed, s0=H, flex=0.8, ph=ph, rot=rs.uniform(0, TAU), inner=0.003,
                 fold=0.18 if L == 0 else 0.0, t0=0.25, t1=1.0, tilt=0.12)
        if L == 0:
            for j in range(6):
                aa = TAU * j / 6 + 0.3
                rr = 0.0 if j == 0 else 0.009
                g.ball(top + facing * 0.005 + Vector((math.cos(aa) * rr, math.sin(aa) * rr, 0)), 0.006,
                       "felt_center", 0, H, 0.8, ph, scale=(1, 1, 0.8))
        else:
            g.ball(top + facing * 0.004, 0.008, "felt_center", 0, H, 0.8, ph, scale=(1, 1, 0.6))


def clover(g):
    """Tapis de trèfle : feuilles à trois folioles, têtes roses et blanches."""
    g.dome, g.up = Vector((0, 0, -0.3)), 1.0
    L = g.level
    rs = random.Random(g.seed)
    for k in range(g.lv(60, 18, 7)):
        a = rs.uniform(0, TAU)
        d = 0.3 * math.sqrt(rs.random())
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        H = rs.uniform(0.04, 0.11)
        ph = rs.random()
        top = root + Vector((rs.uniform(-0.02, 0.02), rs.uniform(-0.02, 0.02), H))
        if L == 0:
            side = Vector((-math.sin(a), math.cos(a), 0))
            g.strip([root, (root + top) / 2 + side * 0.004, top], [0.003, 0.0025, 0.002], [side] * 3, "stem", flex=1.2,
                    ph=ph, soft=0.6)
        trefoil(g, top, rs.uniform(0.022, 0.03) * g.lv(1.0, 1.4, 1.9), "clover_leaf" if k % 4 else "leaf", H, ph,
                1.2, rs.uniform(0, TAU))
    for k in range(g.lv(9, 4, 2)):
        a = rs.uniform(0, TAU)
        d = 0.26 * math.sqrt(rs.random())
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.01))
        H = rs.uniform(0.1, 0.18)
        ph = rs.random()
        col = "clover_head" if k % 3 == 0 else "clover_white"
        pts, tang = stem_path(root, a, H, rs.uniform(0.0, 0.2), rs.uniform(0.0, 0.3), g.lv(3, 1, 1))
        if L < 2:
            stem(g, pts, 0.0022, 0.0018, "stem", 1.2, ph, 3)
        g.ball(pts[-1] + UP * 0.01, 0.017, col, g.lv(1, 0, 0), H, 1.2, ph, scale=(1, 1, 0.9))


# ================================================================= sous-bois
def fern(g):
    g.dome, g.up = Vector((0, 0, -0.5)), 0.6
    rs = random.Random(g.seed)
    n = g.lv(9, 7, 5)
    for k in range(n):
        a = TAU * k / n + rs.uniform(-0.25, 0.25)
        g.lv(0, 0)
        frond(g, (math.cos(a) * 0.03, math.sin(a) * 0.03, 0.0), a, rs.uniform(0.78, 1.0), rs.uniform(0.3, 0.55),
              rs.uniform(0.7, 1.0), "fern" if k % 3 else "fern_light", 16, 0.15, 0.03, rs.random(), 0.7)
    if g.level == 0:
        for k in range(2):
            fiddlehead(g, (rs.uniform(-0.03, 0.03), rs.uniform(-0.03, 0.03), 0.0), rs.uniform(0.14, 0.2),
                       "fern_light", rs.random())


def fern_young(g):
    g.dome, g.up = Vector((0, 0, -0.35)), 0.6
    rs = random.Random(g.seed)
    n = g.lv(6, 5, 4)
    for k in range(n):
        a = TAU * k / n + rs.uniform(-0.3, 0.3)
        frond(g, (math.cos(a) * 0.02, math.sin(a) * 0.02, 0.0), a, rs.uniform(0.38, 0.5), rs.uniform(0.12, 0.3),
              rs.uniform(0.35, 0.6), "fern_light", 13, 0.085, 0.022, rs.random(), 0.9, tbase=0.3)
    if g.level == 0:
        for k in range(3):
            fiddlehead(g, (rs.uniform(-0.04, 0.04), rs.uniform(-0.04, 0.04), 0.0), rs.uniform(0.1, 0.16),
                       "fern_light", rs.random())


def blueberry(g):
    """Myrtillier : dôme festonné de petites feuilles ovales sur rameaux verts et rouges, baies bleues."""
    L = g.level
    rs = random.Random(g.seed)
    g.dome, g.up = Vector((0, 0, 0.0)), 0.4
    zr = (0.0, 0.38)
    if L == 2:
        g.ball((0, 0, 0.16), 1.0, "leaf", 1, 0.2, 0.2, 0.0, scale=(0.32, 0.32, 0.2),
               tz=lambda co: max(0.05, min(0.95, co.z / 0.36)))
        return
    for k in range(g.lv(9, 4)):
        a = rs.uniform(0, TAU)
        pts = arc((math.cos(a) * 0.04, math.sin(a) * 0.04, -0.01), a, rs.uniform(0.28, 0.38), rs.uniform(0.2, 0.6),
                  rs.uniform(0.0, 0.3), g.lv(3, 2))
        stem(g, pts, 0.004, 0.002, "twig_red" if k % 2 else "stem", 0.3, rs.random(), 3)
    spots = [((0, 0, 0.22), (0.2, 0.2, 0.15)), ((0.17, 0.08, 0.16), (0.13, 0.13, 0.11)),
             ((-0.16, 0.1, 0.15), (0.13, 0.12, 0.1)), ((0.02, -0.18, 0.15), (0.12, 0.12, 0.1))]
    for i, (c, rad) in enumerate(spots if L == 0 else spots[:2]):
        g.rosette_cloud(c, rad, g.lv(int(64 * rad[0] / 0.2), int(16 * rad[0] / 0.2)), g.lv(0.045, 0.08),
                        ["leaf", "leaf", "leaf_light"], g.seed + i, zmin=-0.3, zrange=zr,
                        leaves=g.lv(5, 4), cup=0.2, sinus=0.6, core="leaf", core_sub=g.lv(2, 1), width=0.9,
                        flex=0.25, s0=0.15)
    for k in range(g.lv(16, 6)):
        a = rs.uniform(0, TAU)
        e = rs.uniform(-0.1, 0.9)
        c, r = spots[k % len(spots)]
        p = Vector(c) + Vector((math.cos(a) * math.cos(e) * r[0], math.sin(a) * math.cos(e) * r[1],
                                math.sin(e) * r[2])) * 1.0
        g.ball(p, 0.011, "berry", g.lv(1, 0), 0.3, 0.25, rs.random())


# ================================================================= buissons (dispersés sur toute la carte : LOD2 très léger)
def bush_rhodo(g):
    """Rhododendron ferrugineux (rose des Alpes) : coussin festonné de feuilles lancéolées, bouquets de fleurs roses."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -0.2)), 0.35
    spots = [((0, 0, 0.45), 0.62), ((0.6, 0.2, 0.36), 0.46), ((-0.55, 0.28, 0.36), 0.46), ((0.1, -0.55, 0.34), 0.44),
             ((-0.2, 0.6, 0.3), 0.38), ((0.5, -0.45, 0.28), 0.34)]
    zr = (0.0, 1.0)
    rs = random.Random(g.seed)
    if L == 2:
        g.ball((0, 0, 0.4), 1.0, "rhodo_leaf", 1, 0.3, 0.1, 0.0, scale=(1.0, 0.95, 0.55),
               tz=lambda co: max(0.05, min(0.95, co.z / 0.95)))
        for k in range(3):
            a = TAU * k / 3 + 0.4
            g.ball((math.cos(a) * 0.45, math.sin(a) * 0.45, 0.72), 0.2, "rhodo_flower", 0, 0.8, 0.1, 0.0, t=0.7,
                   scale=(1, 1, 0.5))
        return
    use = spots if L == 0 else spots[:4]
    for k, (c, r) in enumerate(use):
        rad = (r * 1.12, r * 1.12, r * 0.82)
        g.rosette_cloud(c, rad, g.lv(int(56 * (r / 0.62) ** 1.6) + 8, int(16 * (r / 0.62) ** 1.6) + 4),
                        g.lv(0.16, 0.26), ["rhodo_leaf", "rhodo_leaf", "leaf_dark"], g.seed + k, zmin=-0.3, zrange=zr,
                        leaves=g.lv(7, 6), cup=0.3, sinus=0.28, core="leaf_dark", core_sub=g.lv(2, 1), top_cols=["leaf"])
    # bouquets de fleurs (ombelles de petites trompettes étoilées) sur le dessus
    nb = g.lv(16, 9)
    for j in range(nb):
        c, r = use[j % len(use)]
        a = rs.uniform(0, TAU)
        e = rs.uniform(0.4, 1.3)
        sn = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
        p = Vector(c) + Vector((sn.x * r * 1.12, sn.y * r * 1.12, sn.z * r * 0.82)) * 1.02
        ph = rs.random()
        if L == 1:
            g.ball(p, 0.07, "rhodo_flower", 0, 0.9, 0.12, ph, t=0.7, scale=(1, 1, 0.7))
            continue
        for f in range(6):
            aa = TAU * f / 6 + rs.uniform(-0.2, 0.2)
            o = Vector((math.cos(aa), math.sin(aa), 0.0))
            q = p + o * 0.04 + sn * 0.01
            facing = (sn + o * 0.6).normalized()
            g.fan(q, facing, 0.026, 10, "rhodo_flower", -0.01, 0.9, 0.12, ph, 1.0, 0.25, rot=rs.uniform(0, TAU),
                  star=0.35)
        g.fan(p + sn * 0.014, sn, 0.024, 10, "rhodo_flower", -0.008, 0.9, 0.12, ph, 1.0, 0.25, star=0.35)


def juniper(g):
    """Genévrier nain : tapis étalé et festonné de rameaux à aiguilles glauques, pousses dressées, baies bleu-gris."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -0.4)), 0.4
    rs = random.Random(g.seed)
    zr = (0.0, 0.7)
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
        g.rosette_cloud(c, rad, g.lv(int(46 * rad[0] / 0.65), int(16 * rad[0] / 0.65)), g.lv(0.17, 0.27),
                        ["juniper", "juniper", "juniper_tip"], g.seed + k, zmin=-0.25, zrange=zr, leaves=g.lv(8, 6),
                        cup=0.22, sinus=0.22, core="juniper", core_sub=g.lv(2, 1), top_cols=["juniper_tip"], width=0.2)
    if L == 0:
        for k in range(14):                          # pousses dressées
            c, rad = use[k % len(use)]
            a = rs.uniform(0, TAU)
            p = Vector(c) + Vector((math.cos(a) * rad[0] * 0.6, math.sin(a) * rad[1] * 0.6, rad[2] * 0.75))
            for j in range(4):
                aa = a + TAU * j / 4
                g.blade(p, aa, rs.uniform(0.12, 0.2), 0.03, "juniper_tip", 0.3, 0.35, 2, 0.0, p_needle, 0.4, 0.25,
                        rs.random(), soft=0.85)
        for k in range(12):
            c, rad = use[k % len(use)]
            a = rs.uniform(0, TAU)
            e = rs.uniform(0.0, 0.9)
            p = Vector(c) + Vector((math.cos(a) * math.cos(e) * rad[0], math.sin(a) * math.cos(e) * rad[1],
                                    math.sin(e) * rad[2])) * 0.98
            g.ball(p, 0.018, "berry", 1, 0.3, 0.15, rs.random())


# ================================================================= lac
def reeds(g):
    """Roselière : feuilles de laîche en gouttière, massettes brunes. Origine à la ligne d'eau, racines 0,6 m dessous."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -1.2)), 0.6
    rs = random.Random(g.seed)
    for k in range(28):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.04, 0.7)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.6))
        H = rs.uniform(1.8, 2.9)
        ph = rs.random()
        if k % g.lv(1, 2, 4):
            continue
        g.blade(root, a + rs.uniform(-0.6, 0.6), H, rs.uniform(0.022, 0.034) * g.lv(1.0, 1.4, 2.2),
                "reed" if k % 3 else "leaf", rs.uniform(0.02, 0.18), rs.uniform(0.2, 0.75), g.lv(5, 2, 1),
                rs.uniform(-0.6, 0.6) if L == 0 else 0.0, p_strap, flex=0.12, ph=ph, fold=0.3 if L == 0 else 0.0,
                soft=0.75)
    for k in range(g.lv(7, 4, 2)):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.05, 0.55)
        root = Vector((math.cos(a) * d, math.sin(a) * d, -0.6))
        H = rs.uniform(2.0, 2.6)
        ph = rs.random()
        pts = arc(root, a, H, rs.uniform(0.0, 0.08), rs.uniform(0.0, 0.1), g.lv(5, 2, 1))
        if L == 2:
            side = Vector((-math.sin(a), math.cos(a), 0))
            g.strip(pts, [0.02, 0.0], [side] * 2, "reed", flex=0.1, ph=ph)
            p, t2, s = sample(pts, 0.85)
            g.strip([p, p + t2 * 0.22], [0.045, 0.045], [side] * 2, "cattail", flex=0.1, ph=ph, t0=0.3, t1=0.8,
                    soft=0.9)
            continue
        stem(g, pts, 0.007, 0.004, "reed", 0.1, ph, 3)
        p, t2, s = sample(pts, 0.8)
        head = [p + t2 * (0.2 * i / 4) for i in range(5)]
        g.tube(head, [0.012, 0.018, 0.019, 0.017, 0.008], "cattail", g.lv(7, 4), s, 0.1, ph, 0.2, 0.9, soft=0.85,
               cap=True)
        if L == 0:
            tip = [head[-1], head[-1] + t2 * 0.08, head[-1] + t2 * 0.16]
            g.tube(tip, [0.004, 0.0025, 0.0], "reed", 3, s + 0.2, 0.1, ph, 0.6, 1.0)


def lily_pads(g):
    """Nénuphars : feuilles rondes échancrées au bord relevé, fleurs à pétales étagés, un bouton. Origine à la ligne d'eau."""
    L = g.level
    g.dome, g.up = Vector((0, 0, -2.0)), 1.0
    rs = random.Random(g.seed)
    pads = []
    for k in range(10):
        a = rs.uniform(0, TAU)
        d = rs.uniform(0.0, 1.3) if k else 0.0
        pads.append((Vector((math.cos(a) * d, math.sin(a) * d, 0.02)), rs.uniform(0.18, 0.34), rs.uniform(0, TAU)))
    for k, (c, r, rot) in enumerate(pads if L < 2 else pads[::2]):
        n = g.lv(16, 9, 6)
        notch = 0.42
        col = "lily_pad"
        mid = g.vert(c + Vector((0, 0, 0.004)), 0.25, 0.0, 0.0, 0.0, UP)
        rings = []
        for ring_r, lift, tt in ((0.6, 0.0, 0.55), (1.0, 0.018, 1.0))[(0 if L == 0 else 1):]:
            ring = []
            for i in range(n + 1):
                a = rot + notch / 2 + (TAU - notch) * i / n
                wav = 0.006 * math.sin(a * 5 + k) if ring_r == 1.0 else 0.0
                q = c + Vector((math.cos(a) * r * ring_r, math.sin(a) * r * ring_r, lift + wav))
                ring.append(g.vert(q, tt, 0.0, 0.0, 0.0, (UP + Vector((math.cos(a), math.sin(a), 0)) * 0.15 * ring_r).normalized()))
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
            g.ball(c + UP * 0.05, 0.08, col, 0, 0.0, 0.0, ph, t=0.8, scale=(1, 1, 0.6))
            continue
        layers = ((8, 0.11, 0.25), (8, 0.095, 0.6), (7, 0.075, 1.0)) if L == 0 else ((7, 0.11, 0.45),)
        for li, (n, ln, tilt) in enumerate(layers):
            g.petals(c + UP * (0.02 + 0.01 * li), UP, n, ln, ln * 0.38, col, cup=0.25, segs=g.lv(2, 1),
                     profile=p_pointed, flex=0.0, ph=ph, rot=li * 0.4, inner=0.012, tilt=tilt, t0=0.15, t1=1.0)
        if L == 0:
            for j in range(10):
                aa = TAU * j / 10
                g.blade(c + UP * 0.03 + Vector((math.cos(aa), math.sin(aa), 0)) * 0.012, aa, 0.03, 0.008,
                        "center_yellow", 0.45, 0.2, 1, 0.0, p_spindle, flex=0.0, ph=ph, soft=0.8)
            g.fan(c + UP * 0.032, UP, 0.014, 8, "center_yellow", 0.006, 0.0, 0.0, ph, 0.4, 1.0)
    if L == 0:                                      # bouton fermé
        c = pads[7][0] + Vector((0.05, -0.1, 0.0))
        g.ball(c + UP * 0.05, 0.035, "lily_pink", 1, 0.0, 0.0, 0.3, scale=(1, 1, 1.6))


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


def material():
    m = bpy.data.materials.get(MATERIAL)
    if m is None:
        m = bpy.data.materials.new(MATERIAL)
        m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = 'Linear'
    tex.extension = 'EXTEND'
    uvn = next((n for n in nt.nodes if n.type == 'UVMAP'), None) or nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "UVMap"
    nt.links.new(uvn.outputs[0], tex.inputs[0])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.7
    for key in ("Subsurface Weight", "Subsurface"):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = 0.0
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
    for f in bm.faces:
        u = (f[g.lc] + 0.5) / NCOL
        f.smooth = True
        f.material_index = 0
        for lp in f.loops:
            v = lp.vert
            lp[uv0].uv = (u, min(0.995, max(0.005, v[g.lt])))
            lp[uv1].uv = (v[g.lw], v[g.lp])
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
