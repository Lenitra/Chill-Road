"""Pack alpin v2 : niveaux détaillés (hd) des objets ruraux, du ski, du lac et des infrastructures restés trop simples.
Mêmes gabarits que les modèles d'origine (utilisés aux niveaux moyen et lointain)."""
import math, random
from mathutils import Vector, Matrix
import alp_lib as A
import alp_kit as K
from alp_kit import Fac
from alp_props_hd import lathe, bolts, ring_pts
from alp_nature_hd import rock, blob


def extra(iid):
    """La fonction AJOUTE des détails (niveau hd) au modèle d'origine."""
    def deco(fn):
        if iid not in A.ITEMS:
            return fn
        old = A.ITEMS[iid]["build"]

        def build(g):
            old(g)
            if g.hd:
                fn(g)
        A.ITEMS[iid]["build"] = build
        return fn
    return deco


def hd(iid):
    def deco(fn):
        if iid not in A.ITEMS:
            return fn
        old = A.ITEMS[iid]["build"]

        def build(g):
            if g.hd:
                fn(g)
            else:
                old(g)
        A.ITEMS[iid]["build"] = build
        return fn
    return deco


def post(g, x, y, z0, z1, r, color, n=8, top="cone"):
    g.tube([(x, y, z0), (x, y, z1)], [r, r * 0.95], n, color, cap_end=(top != "cone"))
    if top == "cone":
        g.frustum((x, y, z1), r * 0.95, 0.0, r * 1.2, n, color, auto=False)


def plank(g, p0, p1, w, h, color, rs=None, wobble=0.0):
    """Planche (section w × h) de p0 à p1, légèrement voilée."""
    p0, p1 = Vector(p0), Vector(p1)
    if wobble and rs:
        p0 = p0 + Vector((0, 0, rs.uniform(-wobble, wobble)))
        p1 = p1 + Vector((0, 0, rs.uniform(-wobble, wobble)))
    return g.beam(p0, p1, w, color, h)


# ----------------------------------------------------------------- clôtures, murets, portail
@hd("wood_fence")
def wood_fence(g):
    """Clôture de pâturage (module 3 m le long de X, poteau à l'extrémité x = -1,5 m Blender) : poteau fendu, lisses en
    demi-rondins clouées, contrefiche."""
    L = 3.0
    rs = random.Random(g.seed)
    post(g, -L / 2, 0, -0.5, 1.25, 0.065, "wood", 7)
    g.box((-L / 2, -0.07, 1.0), (0.05, 0.02, 0.04), "metal_dark")
    for z, r in ((0.45, 0.05), (0.95, 0.055)):
        g.tube([(-L / 2 - 0.02, -0.11, z + rs.uniform(-0.02, 0.02)), (0.0, -0.115, z + rs.uniform(-0.03, 0.03)),
                (L / 2 + 0.02, -0.11, z + rs.uniform(-0.02, 0.02))], [r, r * 0.95, r * 0.9], 7, "wood_honey" if z < 0.6 else "wood", cap_start=True)
        g.octa((-L / 2, -0.16, z), 0.018, "metal_dark")
    g.beam((-L / 2 + 0.06, 0.06, 1.05), (-L / 2 + 0.65, 0.07, 0.0), 0.06, "wood", 0.06)


@hd("stone_wall")
def stone_wall(g):
    """Muret de pierre sèche (module 4 m le long de X) : pierres individuelles en assises irrégulières, couvertine."""
    L = 4.0
    g.box((0, 0, 0.2), (L, 0.5, 1.4), "stone_dark")                                   # cœur (en retrait)
    rs = random.Random(g.seed)
    z = 0.0
    k = 0
    while z < 0.8:
        hh = rs.uniform(0.16, 0.24)
        x = -L / 2 + (0.0 if k % 2 else -0.15)
        while x < L / 2 - 0.05:
            w = min(rs.uniform(0.3, 0.55), L / 2 - x)
            for sy in (-1, 1):
                if w > 0.08:
                    rock(g, (x + w / 2, sy * 0.27, z + hh / 2), (w - 0.03, 0.14 + rs.uniform(0, 0.04), hh - 0.02),
                         ("wall_stone", "rock_light", "stone", "rock")[rs.randrange(4)], g.seed + k * 100 + int(x * 37) * sy, 1, 0.35)
            x += w
        z += hh
        k += 1
    for i in range(10):                               # couvertine : pierres posées de chant
        x = -L / 2 + L * (i + 0.5) / 10
        rock(g, (x, 0.0, 0.92), (0.36, 0.62, 0.22 + rs.uniform(0, 0.08)), "rock_light" if i % 2 else "wall_stone", g.seed + 900 + i, 1, 0.4,
             rot=rs.uniform(-0.15, 0.15))
    for i in range(4):                                # mousse
        blob(g, (rs.uniform(-1.8, 1.8), rs.uniform(-0.2, 0.2), 1.04), 0.12, 1, "moss", 0.4, g.seed + i, 0.2)


@hd("gate_wood")
def gate_wood(g):
    L = 3.0
    for sx in (-1, 1):
        post(g, sx * L / 2, 0, -0.5, 1.5, 0.1, "wood", 8)
    x0, x1 = -L / 2 + 0.14, L / 2 - 0.14
    for z in (0.3, 0.65, 1.0):
        g.beam((x0, 0, z), (x1, 0, z), 0.05, "wood_honey", 0.11)
    for x in (x0 + 0.05, x1 - 0.05):
        g.beam((x, 0.06, 0.2), (x, 0.06, 1.1), 0.05, "wood_honey", 0.09)
    g.beam((x0 + 0.08, -0.06, 0.32), (x1 - 0.08, -0.06, 0.98), 0.04, "wood_honey", 0.09)
    for z in (0.35, 0.95):                            # gonds, loquet
        g.tube([(-L / 2 + 0.08, -0.06, z), (-L / 2 + 0.4, -0.06, z)], [0.015, 0.015], 5, "metal_dark")
        g.tube([(-L / 2 + 0.1, -0.06, z - 0.06), (-L / 2 + 0.1, -0.06, z + 0.06)], [0.02, 0.02], 6, "metal_dark", cap_start=True)
    g.box((L / 2 - 0.2, -0.06, 0.8), (0.18, 0.02, 0.04), "metal_dark")
    g.tube([(L / 2 - 0.12, -0.08, 0.8), (L / 2 - 0.02, -0.12, 0.8)], [0.014, 0.014], 5, "metal_dark")


@hd("picnic_table")
def picnic_table(g):
    rs = random.Random(g.seed)
    for k in range(5):                                # plateau en lattes
        g.box((0, -0.32 + k * 0.16, 0.755), (1.8 + rs.uniform(-0.02, 0.02), 0.14, 0.045), "wood_honey" if k % 2 else "wood_cut")
    for sy in (-1, 1):
        for k in range(2):
            g.box((0, sy * (0.64 + k * 0.15), 0.455), (1.8, 0.13, 0.04), "wood_honey" if k else "wood_cut")
    for sx in (-1, 1):
        x = sx * 0.7
        g.beam((x, -0.82, 0.0), (x, 0.22, 0.73), 0.07, "wood", 0.06)
        g.beam((x, 0.82, 0.0), (x, -0.22, 0.73), 0.065, "wood", 0.06)
        g.box((x, 0, 0.405), (0.075, 1.75, 0.07), "wood")
        g.box((x, 0, 0.705), (0.07, 0.8, 0.05), "wood")
        bolts(g, [(x + sx * 0.04, yy, 0.405) for yy in (-0.7, 0.7)], 0.012, "metal")
    g.beam((-0.68, 0, 0.36), (0.68, 0, 0.36), 0.045, "wood", 0.05)


@hd("tent")
def tent(g):
    """Tente canadienne : double toit tendu, piquets, haubans, porte ouverte."""
    L, half, h = 2.4, 1.1, 1.3
    n = 6
    for k in range(n):                                # pans à léger affaissement entre les arceaux
        y0, y1 = -L / 2 + L * k / n, -L / 2 + L * (k + 1) / n
        sag = 0.06 if 0 < k < n - 1 else 0.0
        for s in (-1, 1):
            p = [(s * half, y0, 0.02), (s * half, y1, 0.02), (s * 0.0, y1, h - (0.0 if k == n - 1 else sag * 0.5)),
                 (0.0, y0, h - (0.0 if k == 0 else sag * 0.5))]
            mid = [(s * half * 0.5, (y0 + y1) / 2, h * 0.5 - sag)]
            f = g.face([p[0], p[1], mid[0]], "orange") + g.face([p[1], p[2], mid[0]], "orange") + \
                g.face([p[2], p[3], mid[0]], "orange") + g.face([p[3], p[0], mid[0]], "orange")
            for q in f:
                q.normal_update()
                if q.normal.x * s < 0:
                    q.normal_flip()
    for y in (-L / 2, L / 2):                         # pignons : toile, porte ouverte à l'avant
        f = g.face([(-half, y, 0.02), (half, y, 0.02), (0, y, h)], "orange" if y > 0 else "canvas_green")
        f[0].normal_update()
        if f[0].normal.y * y < 0:
            f[0].normal_flip()
    g.face([(-0.45, -L / 2 - 0.01, 0.02), (0.45, -L / 2 - 0.01, 0.02), (0, -L / 2 - 0.01, 1.0)], "black")
    g.face([(0.0, -L / 2 - 0.02, 0.02), (0.35, -L / 2 - 0.45, 0.02), (0.0, -L / 2 - 0.02, 1.0)], "orange")   # pan relevé
    g.face([(0.0, -L / 2 - 0.025, 1.0), (0.35, -L / 2 - 0.455, 0.02), (0.0, -L / 2 - 0.025, 0.02)], "orange")
    for y in (-L / 2 - 0.05, L / 2 + 0.05):           # mâts, haubans, piquets
        g.tube([(0, y, -0.1), (0, y, h + 0.12)], [0.012, 0.012], 5, "metal")
        g.tube([(0, y, h + 0.05), (0, y + math.copysign(0.7, y), -0.05)], [0.004, 0.004], 3, "white", cap_end=False)
    for s in (-1, 1):
        for y in (-L / 2, 0, L / 2):
            g.tube([(s * half, y, 0.05), (s * (half + 0.35), y, -0.05)], [0.004, 0.004], 3, "white", cap_end=False)
            g.frustum((s * (half + 0.35), y, -0.1), 0.012, 0.012, 0.16, 4, "metal", auto=False)
    g.box((1.6, 0.3, 0.2), (0.4, 0.6, 0.4), "sign_blue")
    g.box((1.6, 0.3, 0.45), (0.3, 0.2, 0.1), "black")


@hd("haybale_round")
def haybale_round(g):
    m = g.mark()
    n = 20
    f = g.frustum((0, 0, -0.6), 0.7, 0.7, 1.2, n, "hay", cap0=True, cap1=True, auto=False)
    g.paint(g.caps(f), "dry")
    g.jitter(f, 0.02)
    for k in range(3):                                # filet / ficelles
        g.tube(ring_pts((0, 0, -0.4 + 0.4 * k), 0.71, n, 'z'), [0.012] * (n + 1), 3, "wood_cut", cap_end=False)
    for r in (0.25, 0.48):                            # spirale de paille sur les faces
        for s in (-1, 1):
            g.tube(ring_pts((0, 0, s * 0.605), r, 14, 'z'), [0.02] * 15, 3, "hay", cap_end=False)
    g.transform(g.since(m), Matrix.Translation((0, 0, 0.68)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))


@hd("haybale_stack")
def haybale_stack(g):
    rs = random.Random(g.seed)
    for k, (x, y, z, r) in enumerate(((-0.55, 0, 0.25, 0.0), (0.55, 0.05, 0.25, 0.05), (0, 0.9, 0.25, 1.57),
                                      (0.0, 0.0, 0.75, 0.1), (0.1, 0.85, 0.75, 1.5), (0.3, 0.3, 1.25, 0.4))):
        m = g.mark()
        f = g.box((0, 0, 0), (1.0, 0.5, 0.48), "hay" if k % 2 else "dry")
        g.jitter(f, 0.012)
        for sx in (-0.25, 0.25):                      # ficelles
            for a, b in (((sx, -0.255, -0.245), (sx, -0.255, 0.245)), ((sx, -0.255, 0.245), (sx, 0.255, 0.245)),
                         ((sx, 0.255, 0.245), (sx, 0.255, -0.245))):
                g.beam(a, b, 0.012, "wood_cut", 0.012)
        for i in range(6):                            # brins qui dépassent
            p = Vector((rs.uniform(-0.5, 0.5), rs.uniform(-0.25, 0.25), 0.24))
            g.blade(p, (rs.uniform(-1, 1), rs.uniform(-1, 1), 0.5), 0.12, 0.03, "hay", bend=0.2, segs=1)
        g.transform(g.since(m), Matrix.Translation((x, y, z)) @ Matrix.Rotation(r, 4, 'Z'))


@hd("trough")
def trough(g):
    """Bassin en tronc creusé, goulot de mélèze, chèvre (poteau) et filet d'eau."""
    n = 16
    m = g.mark()
    f = g.frustum((0, 0, -1.4), 0.42, 0.42, 2.8, n, "bark", cap0=True, cap1=True, auto=False)
    g.paint(g.caps(f), "wood_cut")
    g.transform(g.since(m), Matrix.Translation((0, 0, 0.42)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    g.box((0, 0, 0.82), (2.5, 0.52, 0.06), "wood_cut")
    g.box((0, 0, 0.83), (2.3, 0.4, 0.06), "water")
    for x in (-0.9, 0.9):
        g.box((x, 0, 0.1), (0.3, 0.9, 0.3), "wood")
        g.box((x, 0, 0.27), (0.34, 0.94, 0.04), "wood_dark")
    post(g, -1.55, 0, -0.5, 1.5, 0.1, "wood", 8, top="flat")
    g.tube([(-1.5, 0, 1.3), (-1.2, 0, 1.25), (-0.9, 0, 1.2)], [0.05, 0.045, 0.04], 8, "wood_dark", cap_start=True)
    g.tube([(-0.9, 0, 1.2), (-0.88, 0, 1.0), (-0.86, 0, 0.87)], [0.022, 0.026, 0.03], 6, "water", cap_end=False)
    g.frustum((-0.86, 0, 0.86), 0.06, 0.1, 0.004, 10, "sign_white", auto=False)
    blob(g, (1.45, 0.3, 0.25), 0.26, 2, "leaf", 0.6, g.seed, 0.2)
    for k in range(8):
        g.octa((1.45 + g.D(-0.2, 0.2), 0.3 + g.D(-0.2, 0.2), 0.35 + g.D(0, 0.1)), 0.05, "geranium")
    for k in range(5):                                # mousse sur le tronc
        a = g.D(0.5, 2.6)
        blob(g, (g.D(-1.2, 1.2), math.cos(a) * 0.43, 0.42 + math.sin(a) * 0.43), 0.08, 1, "moss", 0.5, g.seed + k, 0.2)


# ----------------------------------------------------------------- remontées
@hd("cablecar_cabin")
def cablecar_cabin(g):
    """Cabine de téléphérique : chariot à galets, suspente cintrée, caisse à pans coupés vitrée, toit, plancher."""
    g.box((0, 0, 0.05), (0.5, 1.6, 0.3), "metal_dark")
    for sy in (-0.55, -0.18, 0.18, 0.55):
        m = g.mark()
        g.frustum((0, 0, -0.04), 0.12, 0.12, 0.08, 12, "black", auto=False)
        g.transform(g.since(m), Matrix.Translation((0.27, sy, 0.12)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    g.tube([(0, 0, -0.1), (0.12, 0, -1.0), (0.1, 0, -2.4), (0, 0, -3.25)], [0.08, 0.075, 0.07, 0.07], 8, "metal_dark")
    g.tube([(-0.6, 0, -3.2), (0.6, 0, -3.2)], [0.05, 0.05], 8, "metal_dark", cap_start=True)
    zc, hw, hd_, hh = -4.6, 1.3, 1.8, 1.3
    ch = 0.25                                         # pans coupés aux angles
    oct_ = [(-hw + ch, -hd_), (hw - ch, -hd_), (hw, -hd_ + ch), (hw, hd_ - ch), (hw - ch, hd_), (-hw + ch, hd_), (-hw, hd_ - ch), (-hw, -hd_ + ch)]
    g.prism(oct_, zc - 1.3, zc - 0.55, "sign_red", axis='z')
    g.prism([(x * 0.98, y * 0.98) for x, y in oct_], zc - 0.55, zc + 0.95, "glass_blue", axis='z')
    g.prism([(x * 1.03, y * 1.03) for x, y in oct_], zc + 0.95, zc + 1.3, "sign_red", axis='z')
    g.prism([(x * 1.06, y * 1.06) for x, y in oct_], zc + 1.3, zc + 1.42, "white", axis='z')
    g.prism([(x * 0.7, y * 0.7) for x, y in oct_], zc + 1.42, zc + 1.6, "metal_dark", axis='z')
    for (x0, y0), (x1, y1) in zip(oct_, oct_[1:] + oct_[:1]):                         # montants
        g.tube([(x0, y0, zc - 0.55), (x0, y0, zc + 0.95)], [0.035, 0.035], 6, "metal_dark")
    for z in (zc - 0.56, zc + 0.96):
        g.prism([(x * 1.0, y * 1.0) for x, y in oct_], z - 0.03, z + 0.03, "metal_dark", axis='z')
    g.prism([(-0.6, -hd_ - 0.01), (0.6, -hd_ - 0.01), (0.6, -hd_ - 0.03), (-0.6, -hd_ - 0.03)], zc - 1.2, zc + 0.85, "sign_red", axis='z')
    g.box((0, 0, zc - 1.36), (1.6, 2.4, 0.1), "metal_dark")
    for sx in (-1, 1):                                # bandeau blanc et numéro
        g.box((sx * (hw + 0.02), 0, zc - 0.85), (0.04, 2.6, 0.2), "white")
        g.box((sx * (hw + 0.05), 1.2, zc - 1.05), (0.03, 0.3, 0.3), "white")
    for k in range(3):                                # passagers devinés
        g.ico((g.D(-0.6, 0.6), g.D(-1.0, 1.0), zc + 0.2), 0.16, 1, ("sign_blue", "orange", "green")[k], scale=(1, 1, 1.6), auto=False)


@hd("chairlift_chair")
def chairlift_chair(g):
    g.box((0, 0, 0.0), (0.2, 0.5, 0.2), "metal_dark")
    g.tube([(0, 0, -0.05), (0, 0.1, -1.2), (0, 0.15, -2.3)], [0.035, 0.035, 0.035], 8, "metal")
    zc = -3.0
    g.tube([(-1.15, 0.15, -2.3), (1.15, 0.15, -2.3)], [0.03, 0.03], 8, "metal", cap_start=True)
    for sx in (-1, 1):
        g.tube([(sx * 1.15, 0.15, -2.3), (sx * 1.15, 0.3, zc + 0.5), (sx * 1.15, 0.3, zc)], [0.028] * 3, 8, "metal")
    for k in range(4):                                # quatre places galbées
        x = -0.9 + 0.6 * k
        g.box((x, 0.0, zc), (0.56, 0.6, 0.1), "sign_blue")
        g.box((x, 0.0, zc + 0.06), (0.5, 0.54, 0.04), "black")
        g.box((x, 0.33, zc + 0.38), (0.56, 0.1, 0.62), "sign_blue")
        g.box((x, 0.27, zc + 0.4), (0.5, 0.04, 0.5), "black")
    g.tube([(-1.2, -0.48, zc + 0.6), (1.2, -0.48, zc + 0.6)], [0.025, 0.025], 8, "metal", cap_start=True)
    for sx in (-1, 1):
        g.tube([(sx * 1.2, -0.48, zc + 0.6), (sx * 1.18, 0.1, zc + 0.75)], [0.025, 0.025], 8, "metal")
    g.tube([(-1.1, -0.52, zc - 0.5), (1.1, -0.52, zc - 0.5)], [0.02, 0.02], 6, "metal", cap_start=True)
    for x in (-0.6, 0.6):
        g.tube([(x, -0.48, zc + 0.6), (x, -0.52, zc - 0.5)], [0.018, 0.018], 6, "metal")


@hd("chairlift_pylon")
def chairlift_pylon(g):
    H = 12.0
    lathe(g, 0, 0, [(-0.5, 0.42, "metal"), (0.4, 0.4, "metal"), (H * 0.5, 0.36, "metal"), (H, 0.3, "metal")], 16, cap=False)
    g.box((0, 0, 0.2), (1.4, 1.4, 0.5), "concrete")
    bolts(g, [(sx * 0.55, sy * 0.55, 0.47) for sx in (-1, 1) for sy in (-1, 1)], 0.04, "metal_dark")
    g.box((0, 0, H + 0.15), (5.2, 0.4, 0.4), "metal")
    for sx in (-1, 1):
        g.beam((sx * 0.25, 0, H - 1.2), (sx * 2.0, 0, H - 0.02), 0.14, "metal")
        g.box((sx * 2.2, 0, H - 0.25), (0.3, 2.6, 0.3), "metal_dark")
        for k in range(6):                            # galets du balancier
            m = g.mark()
            g.frustum((0, 0, -0.04), 0.13, 0.13, 0.08, 12, "black", auto=False)
            g.transform(g.since(m), Matrix.Translation((sx * 2.2 + sx * 0.18, -1.05 + 0.42 * k, H - 0.45)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    for k in range(14):                               # échelons
        g.tube([(0.36, -0.1, 1.5 + k * 0.7), (0.36, 0.1, 1.5 + k * 0.7)], [0.012, 0.012], 4, "metal_dark")
    for sx in (-1, 1):
        g.tube([(0.36, sx * 0.1, 1.2), (0.36, sx * 0.1, H - 0.4)], [0.014, 0.014], 4, "metal_dark")
    g.box((0, 0, H + 0.7), (0.8, 0.8, 0.06), "metal_dark")
    g.box((0, -0.45, 2.2), (0.5, 0.05, 0.5), "sign_yellow")
    g.box((0, -0.48, 2.2), (0.3, 0.02, 0.06), "black")


# ----------------------------------------------------------------- lac
def hull_hd(g, L, W, H, color, inside="wood_honey", rim="wood", strakes=3, z0=-0.15):
    """Coque à bordés : sections arrondies, étrave pointue, tableau arrière ; plat-bord, bancs."""
    nsec = 9
    secs = []
    for i in range(nsec):
        t = i / (nsec - 1)
        y = -L / 2 + L * t
        wf = math.sin(math.pi * min(1.0, (t + 0.02) * 1.6) / 2) ** 0.8 if t < 0.62 else 1.0 - (t - 0.62) * 0.75
        hf = 1.25 - 0.25 * min(1.0, t * 2.0)
        secs.append((y, max(0.03, W / 2 * wf), H * hf))
    n = 8
    rings = []
    for y, hw, h in secs:
        ring = []
        for k in range(n + 1):                        # demi-section en U, du plat-bord bâbord au plat-bord tribord
            a = math.pi * k / n
            ring.append(Vector((-math.cos(a) * hw, y, z0 + h - math.sin(a) * h * (0.9 if hw > 0.05 else 0.5))))
        rings.append(ring)
    bm = g.bm
    outer, innerf = [], []
    for ra, rb in zip(rings, rings[1:]):
        for k in range(n):
            qa = [bm.verts.new(p) for p in (ra[k], ra[k + 1], rb[k + 1], rb[k])]
            f = bm.faces.new(qa)
            f.normal_update()
            if f.normal.z > 0.2 and abs(f.normal.x) < 0.5:
                f.normal_flip()
            c = Vector((0, f.calc_center_median().y, z0 + H * 2))
            if f.normal.dot(f.calc_center_median() - c) < 0:
                f.normal_flip()
            outer.append(f)
            qi = [bm.verts.new(p + Vector((0, 0, 0.0)) - (p - Vector((0, p.y, z0 + H * 1.3))).normalized() * 0.04) for p in (rb[k], rb[k + 1], ra[k + 1], ra[k])]
            innerf.append(bm.faces.new(qi))
    for i, f in enumerate(outer):
        band = (i % n) * strakes // n
        g.paint([f], color if band % 2 == 0 else ("white" if color != "white" else "sign_blue"))
    g.paint(innerf, inside)
    for q in innerf:
        q.normal_update()
        if q.normal.z < 0:
            q.normal_flip()
    tr = rings[-1]                                    # tableau
    g.face([tr[k] for k in range(n + 1)], color)
    for side in (0, n):                               # plats-bords
        pts = [r[side] + Vector((0, 0, 0.02)) for r in rings]
        g.tube(pts, [0.035] * len(pts), 5, rim)
    return rings


@hd("rowboat")
def rowboat(g):
    hull_hd(g, 4.2, 1.5, 0.6, "wood_red" if g.seed % 2 else "sign_blue")
    for y in (-0.6, 0.7):
        g.box((0, y, 0.22), (1.3, 0.28, 0.05), "wood_cut")
    for sx in (-1, 1):                                # dames de nage, avirons
        g.tube([(sx * 0.72, 0.1, 0.42), (sx * 0.72, 0.1, 0.55)], [0.02, 0.02], 5, "metal_dark")
        g.tube([(sx * 0.72, 0.1, 0.5), (sx * 1.6, 0.4, 0.2), (sx * 2.2, 0.55, 0.13)], [0.03, 0.03, 0.03], 6, "wood_cut")
        g.box((sx * 2.32, 0.58, 0.12), (0.45, 0.16, 0.025), "wood_cut", rotz=sx * 0.3)
    g.tube([(0, -2.1, 0.55), (0, -2.4, 0.5), (0.3, -2.6, 0.0)], [0.012] * 3, 4, "white", cap_end=False)


@hd("sailboat")
def sailboat(g):
    hull_hd(g, 5.6, 1.9, 0.8, "white", inside="wood_cut", rim="wood_honey", strakes=2)
    g.box((0, 0.4, 0.45), (1.2, 2.0, 0.5), "white")
    g.box((0, 0.4, 0.71), (1.24, 2.04, 0.04), "wood_honey")
    for sx in (-1, 1):
        g.box((sx * 0.61, 0.4, 0.5), (0.03, 1.2, 0.18), "window")
    lathe(g, 0, -0.6, [(0.3, 0.06, "metal"), (4.0, 0.05, "metal"), (7.3, 0.035, "metal")], 10)
    g.tube([(0, -0.55, 1.2), (0, 2.4, 1.25)], [0.04, 0.035], 8, "metal", cap_start=True)
    n = 6                                             # voiles en panneaux galbés
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        lo0 = Vector((0.0, -0.5, 1.35 + 5.6 * t0)); lo1 = Vector((0.0, -0.5, 1.35 + 5.6 * t1))
        hi0 = Vector((0.05 + 0.12 * math.sin(math.pi * (t0 + 0.2)), -0.5 + 2.8 * (1 - t0), 1.35 + 5.6 * t0 * (1 - t0) * 0.0 + 5.6 * t0))
        hi1 = Vector((0.05 + 0.12 * math.sin(math.pi * (t1 + 0.2)), -0.5 + 2.8 * (1 - t1), 1.35 + 5.6 * t1))
        q = [lo0, hi0, hi1, lo1]
        g.face(q, "sign_white")
        g.face([p + Vector((0.006, 0, 0)) for p in reversed(q)], "sign_white")
    for i in range(4):
        t0, t1 = i / 4, (i + 1) / 4
        a0 = Vector((0.0, -0.7 - 2.0 * (1 - t0) * 0.0, 6.2 - 5.3 * t0)); a1 = Vector((0.0, -0.7, 6.2 - 5.3 * t1))
        b0 = Vector((0.08 * math.sin(math.pi * t0), -0.7 - 2.0 * t0, 6.2 - 5.3 * t0)); b1 = Vector((0.08 * math.sin(math.pi * t1), -0.7 - 2.0 * t1, 6.2 - 5.3 * t1))
        q = [a0, b0, b1, a1]
        g.face(q, "awning_red")
        g.face([p + Vector((0.006, 0, 0)) for p in reversed(q)], "awning_red")
    for p in ((0, -2.75, 0.75), (0.85, 1.0, 0.8), (-0.85, 1.0, 0.8)):
        g.tube([p, (0, -0.6, 7.25)], [0.006, 0.006], 3, "metal_dark", cap_end=False)


@hd("pedalo")
def pedalo(g):
    for sx in (-1, 1):                                # flotteurs effilés
        A_ = g.mark()
        secs = [((sx * 0.75, -1.6 + 3.2 * t, 0.02 + 0.06 * (t - 0.5) ** 2), 0.25 * (1 - abs(2 * t - 1) ** 3) + 0.04, 0.2 * (1 - abs(2 * t - 1) ** 3) + 0.04)
                for t in [k / 8 for k in range(9)]]
        from alp_animals_hd import loft
        loft(g, secs, 12, "white", frame=(Vector((0, 1, 0)), Vector((0, 0, 1))), expo=2.0)
    g.box((0, 0.1, 0.3), (1.9, 2.2, 0.1), "yellow")
    for sx in (-1, 1):
        g.box((sx * 0.45, 0.5, 0.52), (0.6, 0.6, 0.35), "sign_red")
        g.box((sx * 0.45, 0.82, 0.85), (0.6, 0.12, 0.6), "sign_red")
        g.box((sx * 0.45, 0.48, 0.71), (0.5, 0.5, 0.04), "black")
    g.tube([(0, -0.5, 0.35), (0, -0.5, 0.85)], [0.04, 0.04], 8, "metal_dark")
    g.tube(ring_pts((0, -0.5, 0.95), 0.18, 12, 'y'), [0.02] * 13, 5, "black", cap_end=False)
    for sx in (-1, 1):                                # pédaliers
        g.tube([(sx * 0.45, -0.35, 0.42), (sx * 0.45, -0.55, 0.32)], [0.015, 0.015], 4, "metal")
        g.box((sx * 0.45, -0.56, 0.32), (0.15, 0.08, 0.03), "black")
    m = g.mark()                                      # toboggan
    g.box((0, 0, 0), (0.6, 1.6, 0.08), "sign_blue")
    for sx in (-1, 1):
        g.box((sx * 0.32, 0, 0.06), (0.04, 1.6, 0.12), "sign_blue")
    g.transform(g.since(m), Matrix.Translation((0, 1.7, 0.75)) @ Matrix.Rotation(0.5, 4, 'X'))


@hd("pier")
def pier(g):
    L = 6.0
    rs = random.Random(g.seed)
    for k in range(20):                               # lames disjointes, légèrement irrégulières
        x = -L / 2 + L * (k + 0.5) / 20
        g.box((x, rs.uniform(-0.02, 0.02), 0.5 + rs.uniform(-0.004, 0.004)), (L / 20 - 0.03, 2.0, 0.07), ("wood", "wood_honey", "wood_grey")[k % 3])
    for sy in (-1, 1):
        g.box((0, sy * 0.85, 0.37), (L, 0.12, 0.18), "wood_dark")
        for x in (-L / 2 + 0.2, L / 2 - 0.2):
            g.tube([(x, sy * 0.97, -4.0), (x, sy * 0.97, 0.75)], [0.12, 0.11], 10, "wood_dark")
            g.frustum((x, sy * 0.97, 0.75), 0.11, 0.0, 0.08, 10, "wood_dark", auto=False)
            g.tube(ring_pts((x, sy * 0.97, 0.05), 0.125, 10, 'z'), [0.02] * 11, 4, "moss", cap_end=False)
        g.box((0, sy * 0.85, 0.05), (L - 0.4, 0.06, 0.2), "wood_dark")
    g.beam((-L / 2 + 0.2, 0.85, 0.3), (L / 2 - 0.2, 0.85, -0.6), 0.06, "wood_dark", 0.12)
    g.box((L / 2 - 0.5, 0.9, 0.62), (0.2, 0.15, 0.1), "metal_dark")                    # taquet


# ----------------------------------------------------------------- infrastructures
@extra("tunnel_portal")
def tunnel_portal(g):
    W, H, T, HW, HT = 10.0, 6.0, 1.6, 9.0, 9.6
    f = Fac(g, 'y', 0.0, -1)
    rs = random.Random(g.seed)
    for z in [0.9 * k for k in range(1, 11)]:         # parement : bossages en relief sur les piédroits et le tympan
        for sx in (-1, 1):
            u = sx * (W / 2 + 0.25)
            while abs(u) < HW - 0.4:
                w = rs.uniform(0.6, 1.1)
                cu = u + sx * w / 2
                ins = abs(cu) < 0.2 + W / 2 + 0.95 and z < H + 1.2
                if not ins and abs(cu) + w / 2 < HW - 0.05 and z < HT - 0.4:
                    f.slab(cu, z - 0.45, w - 0.05, 0.85, 0.04 + rs.uniform(0, 0.03), ("concrete", "stone", "concrete_dark")[rs.randrange(3)])
                u += sx * w
    for sx in (-1, 1):                                # chasse-roues, éclairage d'entrée, drain
        g.box((sx * (W / 2 - 0.3), 0.6, 0.2), (0.6, 1.4, 0.4), "concrete_dark")
        g.box((sx * (W / 2 - 0.3), -0.12, 0.2), (0.55, 0.06, 0.35), "sign_yellow")
        g.box((sx * (W / 2 - 0.15), 2.5, 4.6), (0.2, 0.6, 0.12), "metal_dark")
        g.box((sx * (W / 2 - 0.22), 2.5, 4.55), (0.06, 0.5, 0.06), "lamp")
    g.box((0, -0.3, HT + 0.5), (3.0, 0.08, 0.7), "sign_blue")
    g.box((0, -0.35, HT + 0.5), (2.6, 0.04, 0.3), "white")
    g.tube([(-HW + 0.3, -0.35, HT + 0.65), (HW - 0.3, -0.35, HT + 0.65)], [0.03, 0.03], 6, "metal")
    for x in [-HW + 0.3 + (2 * HW - 0.6) * k / 8 for k in range(9)]:                       # garde-corps du couronnement
        g.tube([(x, -0.35, HT + 0.4), (x, -0.35, HT + 1.4)], [0.025, 0.025], 6, "metal")
    g.tube([(-HW + 0.3, -0.35, HT + 1.4), (HW - 0.3, -0.35, HT + 1.4)], [0.03, 0.03], 6, "metal")


@extra("gallery_module")
def gallery_module(g):
    L = 8.0
    for y in (-2.0, 2.0):                             # chanfreins et consoles des piliers
        g.prism([(5.2, 6.3), (6.0, 6.3), (6.0, 5.9)], y - 0.45, y + 0.45, "concrete", axis='y')
        g.box((5.6, y, 0.15), (1.1, 1.2, 0.3), "concrete_dark")
    for k in range(6):                                # nervures du mur, joints de coffrage
        g.box((-5.12, -L / 2 + L * (k + 0.5) / 6, 3.0), (0.04, 0.12, 6.0), "concrete_dark")
    g.box((-5.12, 0, 1.0), (0.04, L, 0.1), "concrete_dark")
    rs = random.Random(g.seed)
    for k in range(6):                                # végétation sur la dalle
        blob(g, (rs.uniform(-4, 4), rs.uniform(-3.5, 3.5), 7.4), rs.uniform(0.4, 0.8), 1, "grass" if k % 2 else "leaf", 0.4, g.seed + k, 0.2)
    g.tube([(5.95, -L / 2, 1.0), (5.95, L / 2, 1.0)], [0.05, 0.05], 8, "metal", cap_start=True)                 # main courante
    g.box((-1.0, 0, 6.27), (0.3, L, 0.02), "metal_dark")


@hd("avalanche_barrier")
def avalanche_barrier(g):
    L = 4.0
    for x in (-1.3, 1.3):
        g.beam((x, 0.0, -0.5), (x, -0.9, 3.0), 0.14, "rust", 0.18)
        g.beam((x, -0.75, 2.4), (x, -2.4, -0.4), 0.1, "rust", 0.12)
        g.box((x, -2.35, -0.25), (0.4, 0.4, 0.3), "concrete")
        g.box((x, 0.05, -0.3), (0.45, 0.45, 0.4), "concrete")
        bolts(g, [(x + sx * 0.12, -0.82, 2.75) for sx in (-1, 1)], 0.03, "metal_dark")
    for k in range(9):                                # grille de poutrelles
        t = 0.08 + 0.9 * k / 8
        g.box((0, -0.9 * t + 0.1, 3.0 * t), (L, 0.07, 0.16), "metal")
        g.box((0, -0.9 * t + 0.14, 3.0 * t + 0.09), (L, 0.02, 0.02), "rust")
    for x in (-0.6, 0.0, 0.6):
        g.beam((x, 0.06, 0.1), (x, -0.84, 2.95), 0.05, "rust", 0.05)


@hd("bridge_pier")
def bridge_pier(g):
    """Pile de pont en pierre (sommet à z = 0) : appareil de moellons en assises, chapiteau mouluré, avant-becs."""
    g.box((0, 0, -0.3), (4.6, 2.5, 0.6), "stone")
    g.box((0, 0, -0.69), (4.3, 2.25, 0.12), "stone_warm")
    g.prism([(-2.0, -1.0), (2.0, -1.0), (2.0, 1.0), (-2.0, 1.0)], -12.0, -0.6, "wall_stone", axis='z')
    rs = random.Random(g.seed)
    for k in range(18):                               # assises de moellons en relief
        z = -11.8 + k * 0.62
        for f, a, b in ((Fac(g, 'y', -1.0, -1), -2.0, 2.0), (Fac(g, 'y', 1.0, 1), -2.0, 2.0)):
            u = a + (0.0 if k % 2 else 0.3)
            while u < b - 0.2:
                w = min(rs.uniform(0.5, 0.9), b - u)
                f.slab(u + w / 2, z + 0.29, w - 0.04, 0.56, 0.03 + rs.uniform(0, 0.03), ("wall_stone", "stone", "stone_warm")[rs.randrange(3)])
                u += w
    for sy in (-1, 1):
        g.prism([(-1.2, sy * 1.0), (1.2, sy * 1.0), (0, sy * 2.2)], -12.0, -5.0, "wall_stone", axis='z')
        cap = [(-1.2, sy * 1.0, -5.0), (1.2, sy * 1.0, -5.0), (0, sy * 2.2, -5.0)]
        top = (0, sy * 1.0, -3.8)
        for i in range(3):
            q = g.face([cap[i], cap[(i + 1) % 3], top], "stone")[0]
            q.normal_update()
            if q.normal.z < 0:
                q.normal_flip()
    g.box((0, 0, -12.6), (5.2, 4.8, 1.2), "stone_dark")
    for k in range(4):                                # lichens, coulures
        blob(g, (rs.uniform(-1.8, 1.8), rs.choice((-1, 1)) * 1.03, rs.uniform(-11, -2)), 0.3, 1, "moss", 0.3, g.seed + k, 0.2)

