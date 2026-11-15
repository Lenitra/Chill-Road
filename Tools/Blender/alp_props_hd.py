"""Pack alpin v2 : mobilier urbain détaillé (niveau hd). Chaque constructeur remplace le LOD0 de la fiche d'origine ;
les niveaux moyen et lointain gardent l'ancien modèle (mêmes gabarits, mêmes têtes de lampes)."""
import math, random
from mathutils import Vector, Matrix
import alp_lib as A
import alp_kit as K
from alp_kit import Fac


def hd(iid):
    """Décorateur : la fonction construit le niveau hd de la fiche, l'ancien constructeur sert aux autres niveaux."""
    def deco(fn):
        old = A.ITEMS[iid]["build"]

        def build(g):
            if g.hd:
                fn(g)
            else:
                old(g)
        A.ITEMS[iid]["build"] = build
        return fn
    return deco


def lathe(g, x, y, prof, n=12, cap=True):
    """Pièce tournée : prof = [(z, rayon, couleur du tronçon qui part de ce point)]."""
    for (z0, r0, c), (z1, r1, _) in zip(prof, prof[1:]):
        if r1 <= 1e-4:
            g.frustum((x, y, z0), r0, 0.0, z1 - z0, n, c, cap0=False, auto=False)
        else:
            g.frustum((x, y, z0), r0, r1, z1 - z0, n, c, cap0=False, cap1=False, auto=False)
    z, r, c = prof[-1]
    if cap and r > 1e-4:
        g.frustum((x, y, z), r, r, 0.001, n, prof[-2][2], cap0=False, cap1=True, auto=False)


def bolts(g, pts, r=0.02, color="metal_dark"):
    for p in pts:
        g.octa(p, r, color)


def ring_pts(c, r, n, axis='y', a0=0.0, a1=6.2832):
    """Points d'un arc de cercle dans le plan perpendiculaire à « axis »."""
    out = []
    for k in range(n + 1):
        a = a0 + (a1 - a0) * k / n
        if axis == 'y':
            out.append((c[0] + math.cos(a) * r, c[1], c[2] + math.sin(a) * r))
        elif axis == 'x':
            out.append((c[0], c[1] + math.cos(a) * r, c[2] + math.sin(a) * r))
        else:
            out.append((c[0] + math.cos(a) * r, c[1] + math.sin(a) * r, c[2]))
    return out


def flowers(g, c, r, colors=("geranium", "flower_pink", "flower_white"), n=14, leaf=5):
    """Massif : feuilles en touffes, fleurs une à une."""
    for k in range(leaf):
        a = 6.2832 * k / leaf
        d = 0.0 if k == 0 else r * 0.55
        f = g.ico((c[0] + math.cos(a) * d, c[1] + math.sin(a) * d, c[2] + r * 0.12), r * 0.52, 1, "leaf" if k % 2 else "leaf_dark",
                  scale=(1, 1, 0.6), auto=False)
        g.jitter(f, r * 0.05)
    for k in range(n):
        a = g.D(0, 6.28)
        d = r * 0.85 * math.sqrt(g.D())
        g.octa((c[0] + math.cos(a) * d, c[1] + math.sin(a) * d, c[2] + r * (0.42 - 0.2 * d / r) + g.D(0, 0.05)), r * g.D(0.13, 0.19),
               colors[k % len(colors)], scale=(1, 1, 0.8))


# ----------------------------------------------------------------- lampadaires
@hd("lamp_alpine")
def lamp_alpine(g):
    c = "metal_dark"
    lathe(g, 0, 0, [(-0.5, 0.19, c), (0.0, 0.19, c), (0.12, 0.17, c), (0.16, 0.13, c), (0.6, 0.11, c), (0.66, 0.14, c),
                    (0.72, 0.085, c), (1.25, 0.07, c), (1.3, 0.095, c), (1.36, 0.065, c), (2.95, 0.052, c), (3.0, 0.085, c),
                    (3.06, 0.05, c), (3.62, 0.045, c), (3.68, 0.1, c), (3.72, 0.1, c)], 12)
    for k in range(8):                                # cannelures du fût
        a = 6.2832 * k / 8
        g.beam((math.cos(a) * 0.072, math.sin(a) * 0.072, 0.74), (math.cos(a) * 0.066, math.sin(a) * 0.066, 1.24), 0.016, c)
    for sx in (-1, 1):                                # traverse d'échelle
        g.tube([(0, 0, 3.2), (sx * 0.32, 0, 3.2)], [0.018, 0.014], 6, c)
        g.octa((sx * 0.34, 0, 3.2), 0.03, c)
    # lanterne : socle, quatre montants, verre émissif, chapeau à égout, épi
    g.frustum((0, 0, 3.72), 0.1, 0.2, 0.1, 4, c, rot=math.pi / 4)
    g.frustum((0, 0, 3.84), 0.185, 0.25, 0.46, 4, "lamp", rot=math.pi / 4, cap0=True, cap1=False)
    for k in range(4):
        a = math.pi / 4 + math.pi / 2 * k
        g.beam((math.cos(a) * 0.2, math.sin(a) * 0.2, 3.82), (math.cos(a) * 0.27, math.sin(a) * 0.27, 4.32), 0.022, c)
        a2 = a + math.pi / 2
        g.beam((math.cos(a) * 0.27, math.sin(a) * 0.27, 4.31), (math.cos(a2) * 0.27, math.sin(a2) * 0.27, 4.31), 0.02, c)
        g.beam((math.cos(a) * 0.2, math.sin(a) * 0.2, 3.83), (math.cos(a2) * 0.2, math.sin(a2) * 0.2, 3.83), 0.02, c)
    g.frustum((0, 0, 4.32), 0.36, 0.1, 0.2, 4, c, rot=math.pi / 4, cap0=True, cap1=False)
    g.frustum((0, 0, 4.52), 0.1, 0.06, 0.08, 4, c, rot=math.pi / 4, cap0=False)
    lathe(g, 0, 0, [(4.6, 0.03, c), (4.66, 0.05, c), (4.72, 0.02, c), (4.8, 0.0, c)], 8)
    # potence en volute et suspension fleurie
    g.tube([(0, 0, 3.0), (0.2, 0, 3.08), (0.42, 0, 3.02), (0.56, 0, 2.86), (0.55, 0, 2.72)], [0.02, 0.018, 0.016, 0.014, 0.012], 6, c)
    g.tube([(0.0, 0, 2.6), (0.18, 0, 2.74), (0.36, 0, 2.94)], [0.012, 0.012, 0.012], 5, c, cap_end=False)
    for k in range(3):
        a = 6.2832 * k / 3
        g.tube([(0.55, 0, 2.72), (0.55 + math.cos(a) * 0.2, math.sin(a) * 0.2, 2.46)], [0.006, 0.006], 3, c, cap_end=False)
    lathe(g, 0.55, 0, [(2.22, 0.08, "wood_dark"), (2.26, 0.16, "wood_dark"), (2.46, 0.25, "wood_dark"), (2.47, 0.22, "dirt")], 10)
    flowers(g, (0.55, 0, 2.47), 0.27, n=16)
    for k in range(4):                                # retombées
        a = 6.2832 * k / 4 + 0.5
        g.octa((0.55 + math.cos(a) * 0.26, math.sin(a) * 0.26, 2.36), 0.06, "geranium" if k % 2 else "leaf", scale=(1, 1, 1.5))


@hd("lamp_road")
def lamp_road(g):
    lathe(g, 0, 0, [(-0.5, 0.15, "metal_dark"), (0.02, 0.15, "metal_dark"), (0.04, 0.115, "metal"), (1.1, 0.105, "metal"),
                    (1.14, 0.09, "metal"), (4.5, 0.075, "metal"), (7.7, 0.058, "metal")], 12, cap=False)
    g.frustum((0, 0, 0.0), 0.24, 0.24, 0.03, 8, "metal_dark", auto=False)
    bolts(g, [(math.cos(a) * 0.19, math.sin(a) * 0.19, 0.045) for a in (0.4, 1.97, 3.54, 5.11)], 0.028)
    Fac(g, 'x', 0.105, 1).slab(0, 0.65, 0.12, 0.32, 0.012, "metal_dark")                     # trappe de visite
    g.tube([(0, 0, 7.7), (0, -0.06, 7.95), (0, -0.4, 8.12), (0, -1.0, 8.12), (0, -1.5, 8.08)], [0.058, 0.055, 0.05, 0.047, 0.045], 10,
           "metal")
    # tête : coque profilée, vasque émissive, ailettes
    g.prism([(-1.48, 8.02), (-1.55, 8.14), (-2.3, 8.12), (-2.42, 8.04), (-2.3, 8.0), (-1.55, 8.0)], -0.17, 0.17, "metal_dark", axis='x')
    g.box((0, -1.95, 7.985), (0.24, 0.62, 0.035), "lamp")
    for k in range(4):
        g.box((0, -1.62 - k * 0.07, 8.15), (0.2, 0.02, 0.04), "metal_dark")
    g.box((0.07, 0, 2.2), (0.012, 0.16, 0.22), "sign_white")                                 # plaque de numéro


# ----------------------------------------------------------------- assises, corbeilles, bornes
@hd("bench")
def bench(g):
    for sx in (-1, 1):                                # piétement en fonte (profil découpé)
        x = sx * 0.75
        g.prism([(-0.3, 0.0), (-0.2, 0.0), (-0.16, 0.36), (0.12, 0.36), (0.2, 0.0), (0.3, 0.0), (0.26, 0.42), (0.34, 0.92),
                 (0.27, 0.92), (0.17, 0.46), (-0.26, 0.46)], x - 0.03, x + 0.03, "metal_dark", axis='x')
        g.tube([(x, -0.26, 0.46), (x, -0.3, 0.62), (x, -0.05, 0.66), (x, 0.24, 0.64)], [0.022, 0.022, 0.022, 0.02], 6, "metal_dark")
    for k in range(5):                                # lattes d'assise puis de dossier
        g.box((0, -0.24 + k * 0.105, 0.475 + (0.01 if k in (0, 4) else 0.0)), (1.84, 0.085, 0.035), "wood_honey")
    for k in range(4):
        m = g.mark()
        g.box((0, 0, 0), (1.84, 0.03, 0.09), "wood_honey")
        g.move(g.since(m), (0, 0.255 + k * 0.022, 0.56 + k * 0.105))
    for sx in (-1, 1):
        for k in range(5):
            bolts(g, [(sx * 0.75, -0.24 + k * 0.105, 0.497)], 0.012, "metal")
        for k in range(4):
            bolts(g, [(sx * 0.75, 0.238 + k * 0.022, 0.56 + k * 0.105)], 0.012, "metal")


@hd("bin")
def bin_(g):
    lathe(g, 0, 0, [(-0.3, 0.05, "metal_dark"), (0.18, 0.05, "metal_dark")], 8, cap=False)
    lathe(g, 0, 0, [(0.15, 0.24, "awning_green"), (0.2, 0.27, "awning_green"), (0.86, 0.31, "awning_green"),
                    (0.9, 0.33, "metal_dark"), (0.94, 0.33, "metal_dark"), (0.98, 0.28, "metal_dark"), (1.06, 0.12, "metal_dark"),
                    (1.08, 0.0, "metal_dark")], 16)
    g.frustum((0, 0, 0.15), 0.24, 0.24, 0.001, 16, "metal_dark", auto=False)
    for k in range(16):                               # lattes
        a = 6.2832 * (k + 0.5) / 16
        g.beam((math.cos(a) * 0.277, math.sin(a) * 0.277, 0.24), (math.cos(a) * 0.312, math.sin(a) * 0.312, 0.84), 0.02, "awning_green", 0.07)
    for z, r in ((0.3, 0.287), (0.76, 0.312)):
        lathe(g, 0, 0, [(z, r, "metal_dark"), (z + 0.035, r + 0.002, "metal_dark")], 16, cap=False)
    Fac(g, 'y', -0.3, -1).slab(0, 0.98, 0.24, 0.07, 0.03, "black")                           # ouverture


@hd("bollard")
def bollard(g):
    c = "metal_dark"
    lathe(g, 0, 0, [(-0.5, 0.1, c), (0.0, 0.1, c), (0.03, 0.115, c), (0.08, 0.115, c), (0.12, 0.085, c), (0.6, 0.075, c),
                    (0.62, 0.082, "white"), (0.7, 0.082, c), (0.72, 0.075, c), (0.8, 0.072, c), (0.83, 0.095, c), (0.87, 0.095, c),
                    (0.9, 0.06, c), (0.94, 0.09, c), (1.0, 0.1, c), (1.06, 0.07, c), (1.1, 0.0, c)], 12)
    for k in range(6):
        a = 6.2832 * k / 6
        g.beam((math.cos(a) * 0.085, math.sin(a) * 0.085, 0.14), (math.cos(a) * 0.077, math.sin(a) * 0.077, 0.58), 0.014, c)


@hd("mailbox")
def mailbox(g):
    lathe(g, 0, 0, [(-0.5, 0.05, "metal_dark"), (0.9, 0.05, "metal_dark")], 10)
    g.frustum((0, 0, 0.0), 0.13, 0.11, 0.03, 10, "metal_dark", auto=False)
    g.box((0, 0, 1.18), (0.46, 0.3, 0.5), "yellow")
    pts = [(-0.24, 1.43)] + [(math.cos(math.pi - math.pi * k / 8) * 0.24, 1.43 + math.sin(math.pi * k / 8) * 0.1) for k in range(9)] + [(0.24, 1.43)]
    g.prism(pts, -0.16, 0.16, "yellow", axis='y')
    g.box((0, 0, 1.43), (0.5, 0.34, 0.03), "yellow")
    f = Fac(g, 'y', -0.15, -1)
    f.slab(0, 1.36, 0.32, 0.05, 0.02, "black")                                              # fente et son volet
    f.prism([(0.0, 1.4), (0.05, 1.37), (0.0, 1.37)], -0.17, 0.17, "yellow")
    f.slab(0, 1.14, 0.3, 0.3, 0.012, "sign_yellow")                                         # portillon, serrure, plaque
    f.slab(0.1, 1.14, 0.03, 0.05, 0.02, "metal_dark")
    f.slab(-0.03, 1.2, 0.14, 0.1, 0.016, "white")
    f.poly([(-0.08, 1.02), (0.02, 1.02), (0.06, 1.08), (-0.04, 1.08)], "black", 0.016)       # cor postal stylisé
    g.box((0, 0, 0.92), (0.2, 0.2, 0.04), "metal_dark")


@hd("hydrant")
def hydrant(g):
    r = "sign_red"
    lathe(g, 0, 0, [(-0.3, 0.12, r), (0.0, 0.12, r), (0.0, 0.17, r), (0.04, 0.17, r), (0.06, 0.125, r), (0.5, 0.115, r),
                    (0.52, 0.14, r), (0.56, 0.14, r), (0.6, 0.12, r), (0.68, 0.085, r), (0.73, 0.04, r), (0.73, 0.045, "metal"),
                    (0.79, 0.045, "metal"), (0.8, 0.0, "metal")], 12)
    bolts(g, [(math.cos(a) * 0.145, math.sin(a) * 0.145, 0.045) for a in [6.2832 * k / 6 for k in range(6)]], 0.018, "metal")
    for sx in (-1, 1):
        g.tube([(sx * 0.1, 0, 0.42), (sx * 0.2, 0, 0.42)], [0.06, 0.06], 10, r)
        g.tube([(sx * 0.2, 0, 0.42), (sx * 0.235, 0, 0.42)], [0.07, 0.07], 6, "metal", cap_start=True)
    g.tube([(0, -0.1, 0.36), (0, -0.21, 0.36)], [0.075, 0.075], 10, r)
    g.tube([(0, -0.21, 0.36), (0, -0.25, 0.36)], [0.088, 0.088], 6, "metal", cap_start=True)
    g.tube([(0, -0.24, 0.3), (0.02, -0.2, 0.2), (0.1, -0.1, 0.16), (0.12, 0, 0.3)], [0.006] * 4, 3, "metal_dark", cap_end=False)


# ----------------------------------------------------------------- vélos
def wheel(g, c, r, axis='x', tyre=0.024, spokes=8, color="rubber", rim="metal"):
    n = 14
    g.tube(ring_pts(c, r, n, axis), [tyre] * (n + 1), 4, color, cap_end=False)
    g.tube(ring_pts(c, r - tyre * 1.2, n, axis), [tyre * 0.45] * (n + 1), 3, rim, cap_end=False)
    for k in range(spokes):
        a = 6.2832 * k / spokes
        p = ring_pts(c, r - tyre * 1.4, 1, axis, a, a)[0]
        g.tube([c, p], [0.004, 0.004], 3, rim, cap_end=False)
    d = (0.05, 0, 0) if axis == 'x' else (0, 0.05, 0)
    g.tube([Vector(c) - Vector(d), Vector(c) + Vector(d)], [0.022, 0.022], 6, rim, cap_start=True)


def bicycle(g, x=0.0, color="sign_red", lean=0.12):
    m = g.mark()
    R = 0.33
    wf, wr = (0, -0.52, R), (0, 0.52, R)
    wheel(g, wf, R)
    wheel(g, wr, R)
    bb, seat, head, bar = Vector((0, 0.08, 0.3)), Vector((0, 0.2, 0.86)), Vector((0, -0.36, 0.84)), Vector((0, -0.4, 1.0))
    for a, b, rr in ((bb, seat, 0.02), (bb, head, 0.024), (seat, head, 0.02), (bb, Vector(wr), 0.014), (seat - Vector((0, 0, 0.08)), Vector(wr), 0.012),
                     (head, Vector(wf), 0.016), (head, bar, 0.016), (seat, seat + Vector((0, 0.03, 0.1)), 0.014)):
        g.tube([a, b], [rr, rr], 6, color)
    g.tube([bar + Vector((-0.27, 0.03, 0.0)), bar + Vector((-0.1, -0.02, 0.02)), bar + Vector((0.1, -0.02, 0.02)), bar + Vector((0.27, 0.03, 0.0))],
           [0.012] * 4, 5, "metal_dark")
    for sx in (-1, 1):
        g.tube([bar + Vector((sx * 0.2, 0.02, 0.0)), bar + Vector((sx * 0.29, 0.035, 0.0))], [0.018, 0.018], 6, "black")
    g.prism([(0.05, 0.94), (0.3, 0.96), (0.33, 1.0), (0.05, 0.99)], -0.06, 0.06, "black", axis='x')   # selle
    g.tube([(-0.04, 0.08, 0.3), (0.04, 0.08, 0.3)], [0.09, 0.09], 10, "metal", cap_start=True)       # plateau
    g.tube([(0.05, 0.08, 0.3), (0.05, 0.02, 0.14)], [0.01, 0.01], 4, "metal_dark")
    g.tube([(-0.05, 0.08, 0.3), (-0.05, 0.14, 0.46)], [0.01, 0.01], 4, "metal_dark")
    g.box((0.09, 0.02, 0.14), (0.09, 0.06, 0.018), "black")
    g.box((-0.09, 0.14, 0.46), (0.09, 0.06, 0.018), "black")
    for wc, a0, a1 in ((wr, 0.3, 2.3), (wf, 1.2, 2.9)):                                        # garde-boue
        pts = ring_pts(wc, R + 0.035, 6, 'x', a0, a1)
        for p, q in zip(pts, pts[1:]):
            g.beam(p, q, 0.05, "metal", 0.008)
    g.box((0, 0.5, 0.74), (0.14, 0.34, 0.012), "metal_dark")                                 # porte-bagages
    g.tube([(0, 0.62, 0.74), (0, 0.52, R)], [0.006, 0.006], 3, "metal_dark")
    g.transform(g.since(m), Matrix.Translation((x, 0, 0)) @ Matrix.Rotation(lean, 4, 'Y'))


@hd("bicycle")
def bicycle_item(g):
    bicycle(g)
    g.tube([(0.08, 0.3, 0.34), (0.3, 0.36, 0.0)], [0.01, 0.01], 4, "metal_dark")              # béquille


@hd("bike_rack")
def bike_rack(g):
    for x in (-0.9, -0.3, 0.3, 0.9):                  # arceaux
        g.tube([(x, -0.02, -0.5), (x, -0.02, 0.7), (x, 0.06, 0.84), (x, 0.3, 0.84), (x, 0.38, 0.7), (x, 0.38, -0.5)],
               [0.022] * 6, 8, "metal")
        for y in (-0.02, 0.38):
            g.frustum((x, y, 0.0), 0.06, 0.06, 0.012, 8, "metal_dark", auto=False)
    bicycle(g, -0.62, "sign_blue", 0.08)
    bicycle(g, 0.6, "sign_green", -0.06)


# ----------------------------------------------------------------- fleurs
@hd("planter")
def planter(g):
    for sy in (-1, 1):                                # bac en planches clouées
        for k in range(3):
            g.box((0, sy * 0.3, 0.1 + k * 0.2), (1.6, 0.04, 0.18), "wood" if k != 1 else "wood_honey")
    for sx in (-1, 1):
        for k in range(3):
            g.box((sx * 0.8, 0, 0.1 + k * 0.2), (0.04, 0.6, 0.18), "wood" if k != 1 else "wood_honey")
        for sy in (-1, 1):
            g.box((sx * 0.79, sy * 0.29, 0.31), (0.09, 0.09, 0.66), "wood_dark")
            g.octa((sx * 0.79, sy * 0.29, 0.67), 0.055, "wood_dark")
    g.box((0, 0, 0.54), (1.56, 0.56, 0.04), "dirt")
    for k, x in enumerate((-0.52, 0.0, 0.52)):
        flowers(g, (x, 0, 0.56), 0.3, (("geranium", "flower_white"), ("flower_pink", "flower_yellow"), ("geranium", "flower_pink"))[k], n=14)
    for x in (-0.26, 0.26):
        for k in range(5):
            a = 6.2832 * k / 5
            g.blade((x, 0, 0.56), (math.cos(a) * 0.5, math.sin(a) * 0.5, 1), 0.5, 0.07, "grass", bend=0.4, segs=2)


@hd("flower_pot")
def flower_pot(g):
    lathe(g, 0, 0, [(0.0, 0.26, "cap"), (0.04, 0.29, "cap"), (0.5, 0.4, "cap"), (0.52, 0.45, "cap"), (0.6, 0.45, "cap"),
                    (0.61, 0.39, "dirt"), (0.6, 0.0, "dirt")], 14, cap=False)
    g.frustum((0, 0, 0.0), 0.26, 0.26, 0.001, 14, "cap", auto=False)
    flowers(g, (0, 0, 0.6), 0.46, ("geranium", "geranium", "flower_white"), n=22, leaf=6)
    for k in range(5):
        a = 6.2832 * k / 5 + 0.3
        g.octa((math.cos(a) * 0.47, math.sin(a) * 0.47, 0.5), 0.07, "geranium" if k % 2 else "leaf", scale=(1, 1, 1.6))


# ----------------------------------------------------------------- abribus
@hd("busstop")
def busstop(g):
    W = 3.6
    c = "metal_dark"
    for sx in (-1, 1):
        for sy in (0.0, 1.3):
            g.tube([(sx * W / 2, sy, -0.5), (sx * W / 2, sy, 2.46)], [0.04, 0.04], 8, c)
            g.frustum((sx * W / 2, sy, 0.0), 0.09, 0.09, 0.015, 8, c, auto=False)
    # toit cintré, rives, éclairage
    arc = [(-0.45 + 2.0 * k / 6, 2.46 + 0.16 * math.sin(math.pi * k / 6)) for k in range(7)]
    g.prism(arc + [(y, z + 0.05) for y, z in reversed(arc)], -W / 2 - 0.25, W / 2 + 0.25, c, axis='x')
    g.box((0, 0.6, 2.44), (2.0, 0.16, 0.04), "lamp")
    for sx in (-1, 1):
        g.box((sx * (W / 2 + 0.22), 0.55, 2.47), (0.06, 2.0, 0.1), c)

    def pane(f, u, z, w, h):                          # vitrage dans son cadre, bandes de repérage
        f.quad(u, z, w, h, "glass_blue", 0.0)
        g.quad(f.P(u, -0.004, z), w, h, f.open_dir, "glass_blue")
        for s in (-1, 1):
            f.box(u + s * (w / 2 - 0.02), z, 0.04, h, 0.03, c, out=-0.015)
            f.box(u, z + s * (h / 2 - 0.02), w, 0.04, 0.03, c, out=-0.015)
        f.box(u, z + 0.15, w - 0.08, 0.06, 0.012, "white", out=-0.006)
    back = Fac(g, 'y', 1.3, -1)
    for k in range(3):
        pane(back, -W / 2 + W * (k + 0.5) / 3, 1.3, W / 3 - 0.06, 1.9)
    pane(Fac(g, 'x', W / 2, -1), 0.65, 1.3, 1.22, 1.9)
    pane(Fac(g, 'x', -W / 2, 1), 0.85, 1.3, 0.82, 1.9)
    g.box((W / 2 - 0.03, 0.65, 1.45), (0.03, 0.9, 1.3), "awning_cream")                      # affiche
    g.box((W / 2 - 0.05, 0.65, 1.7), (0.02, 0.7, 0.5), "sign_blue")
    g.box((W / 2 - 0.05, 0.65, 1.15), (0.02, 0.6, 0.12), "sign_red")
    for k in range(4):                                # banc à lattes sur consoles
        g.box((0, 0.84 + k * 0.1, 0.46), (2.4, 0.08, 0.035), "wood_honey")
    for x in (-1.0, 0.0, 1.0):
        g.prism([(1.28, 0.2), (1.28, 0.44), (0.8, 0.44), (0.8, 0.4)], x - 0.02, x + 0.02, c, axis='x')
    # poteau d'arrêt : disque, fiche horaire, corbeille
    px = -W / 2 - 0.9
    g.tube([(px, 0, -0.5), (px, 0, 2.7)], [0.035, 0.035], 8, "metal")
    m = g.mark()
    g.frustum((0, 0, -0.015), 0.28, 0.28, 0.03, 16, "sign_blue", auto=False)
    g.frustum((0, 0, 0.016), 0.2, 0.2, 0.002, 16, "white", auto=False)
    g.frustum((0, 0, -0.018), 0.2, 0.2, 0.002, 16, "white", auto=False)
    g.transform(g.since(m), Matrix.Translation((px, -0.05, 2.4)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    g.box((px, -0.05, 2.4), (0.22, 0.04, 0.1), "sign_blue")
    g.box((px, -0.05, 1.5), (0.32, 0.03, 0.45), "white")
    g.box((px, -0.066, 1.5), (0.26, 0.004, 0.36), "awning_cream")
    lathe(g, px + 0.2, 0, [(0.7, 0.1, "awning_green"), (1.05, 0.13, "awning_green"), (1.07, 0.14, c), (1.09, 0.14, c)], 10, cap=False)


# ----------------------------------------------------------------- pavoisement, horloge
def wavy_flag(g, x0, zt, w, h, color, device=None, n=7, rows=5):
    """Étoffe ondulée (double face) ; device : couleur d'une croix centrée."""
    def P(i, j):
        u = i / n
        return Vector((x0 + w * u, 0.11 * math.sin(u * 5.2) * (0.3 + u), zt - h * j / rows - 0.06 * u * (1 + 0.5 * math.sin(u * 3))))
    for i in range(n):
        for j in range(rows):
            c = color
            if device:
                u, v = (i + 0.5) / n, (j + 0.5) / rows
                if (abs(v - 0.5) < 0.12 and abs(u - 0.5) < 0.32) or (abs(u - 0.5) < 0.1 and abs(v - 0.5) < 0.34):
                    c = device
            p = [P(i, j + 1), P(i + 1, j + 1), P(i + 1, j), P(i, j)]
            g.face(p, c)
            g.face([q + Vector((0, 0.004, 0)) for q in reversed(p)], c)


def flagpole(g, color, device):
    lathe(g, 0, 0, [(-0.5, 0.11, "stone"), (0.0, 0.17, "stone"), (0.28, 0.12, "stone"), (0.3, 0.075, "white"), (4.0, 0.06, "white"),
                    (8.96, 0.04, "white"), (8.98, 0.07, "brass"), (9.02, 0.07, "brass"), (9.04, 0.03, "brass"), (9.1, 0.075, "brass"),
                    (9.17, 0.0, "brass")], 12)
    wavy_flag(g, 0.06, 8.9, 1.6, 1.6, color, device)
    g.tube([(0.08, 0, 8.9), (0.1, 0, 4.0), (0.09, 0, 1.3)], [0.006] * 3, 3, "white", cap_end=False)   # drisse
    g.box((0.08, 0, 1.25), (0.05, 0.03, 0.14), "metal")                                      # taquet
    for z in (8.9, 7.3):
        g.box((0.06, 0, z), (0.04, 0.02, 0.03), "metal")


hd("flagpole_red")(lambda g: flagpole(g, "red", "white"))
hd("flagpole_blue")(lambda g: flagpole(g, "blue", "yellow"))


@hd("clock_post")
def clock_post(g):
    c = "metal_dark"
    lathe(g, 0, 0, [(-0.5, 0.16, c), (0.0, 0.16, c), (0.1, 0.14, c), (0.14, 0.1, c), (0.72, 0.085, c), (0.78, 0.12, c),
                    (0.84, 0.07, c), (3.2, 0.055, c), (3.26, 0.09, c), (3.32, 0.05, c), (3.42, 0.05, c)], 12)
    for k in range(8):
        a = 6.2832 * k / 8
        g.beam((math.cos(a) * 0.09, math.sin(a) * 0.09, 0.18), (math.cos(a) * 0.08, math.sin(a) * 0.08, 0.7), 0.016, c)
    # boîtier à deux cadrans, consoles en volute, fronton
    m = g.mark()
    g.frustum((0, 0, -0.16), 0.5, 0.5, 0.32, 24, c, auto=False)
    g.transform(g.since(m), Matrix.Translation((0, 0, 3.92)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    for s in (-1, 1):
        Fac(g, 'y', s * 0.16, s).clock(0, 3.92, 0.4, out=0.004)
        g.tube([(s * 0.06, 0, 3.3), (s * 0.3, 0, 3.4), (s * 0.44, 0, 3.6)], [0.02, 0.018, 0.012], 5, c)
    g.frustum((0, 0, 4.4), 0.12, 0.0, 0.3, 4, c, rot=math.pi / 4)
    g.octa((0, 0, 4.74), 0.05, "brass")
    g.box((0, 0, 4.4), (0.5, 0.2, 0.05), c)


# ----------------------------------------------------------------- conteneurs, réseaux
@hd("recycling_bins")
def recycling_bins(g):
    for k, c in enumerate(("sign_green", "sign_blue", "yellow")):
        x = (k - 1) * 1.5
        g.box((x, 0, 0.78), (1.35, 1.2, 1.44), c)
        pts = [(-0.6, 1.5)] + [(-0.6 + 1.2 * (0.5 - 0.5 * math.cos(math.pi * i / 8)), 1.5 + 0.34 * math.sin(math.pi * i / 8)) for i in range(1, 8)] + [(0.6, 1.5)]
        g.prism(pts, x - 0.675, x + 0.675, c, axis='x')
        g.box((x, 0, 0.05), (1.25, 1.1, 0.1), "metal_dark")
        for sy in (-1, 1):                            # trappes rondes à bavette, étiquettes, anneau de levage
            m = g.mark()
            g.frustum((0, 0, 0), 0.2, 0.2, 0.035, 14, "black", auto=False)
            g.frustum((0, 0, 0.035), 0.15, 0.15, 0.002, 14, "rubber", auto=False)
            g.transform(g.since(m), Matrix.Translation((x, sy * 0.6, 1.2)) @ Matrix.Rotation(-sy * math.pi / 2, 4, 'X'))
            Fac(g, 'y', sy * 0.6, sy).slab(x, 0.75, 0.5, 0.36, 0.006, "white")
            Fac(g, 'y', sy * 0.6, sy).slab(x, 0.75, 0.3, 0.2, 0.01, c)
        g.tube([(x - 0.12, 0, 1.82), (x - 0.1, 0, 1.98), (x + 0.1, 0, 1.98), (x + 0.12, 0, 1.82)], [0.02] * 4, 6, "metal", cap_end=False)
        for sx in (-1, 1):
            g.box((x + sx * 0.676, 0, 0.9), (0.012, 1.0, 0.05), "metal_dark")


@hd("utility_pole")
def utility_pole(g):
    lathe(g, 0, 0, [(-0.5, 0.16, "wood_dark"), (0.3, 0.15, "wood_dark"), (4.0, 0.125, "wood_dark"), (8.5, 0.1, "wood_dark")], 10)
    g.box((0, 0.06, 7.9), (2.2, 0.1, 0.13), "wood_dark")
    for sx in (-1, 1):
        g.beam((sx * 0.08, 0.06, 7.15), (sx * 0.75, 0.06, 7.85), 0.04, "metal_dark", 0.02)
    for x in (-0.95, -0.35, 0.35, 0.95):              # isolateurs à jupes
        lathe(g, x, 0.06, [(7.96, 0.018, "metal_dark"), (8.02, 0.018, "sign_white"), (8.03, 0.055, "sign_white"), (8.06, 0.03, "sign_white"),
                           (8.08, 0.055, "sign_white"), (8.11, 0.03, "sign_white"), (8.13, 0.05, "sign_white"), (8.17, 0.0, "sign_white")], 8)
    bolts(g, [(0, -0.01, 7.9), (0, 0.0, 7.2)], 0.03, "metal")
    for k in range(7):                                # échelons
        s = 1 if k % 2 else -1
        g.tube([(s * 0.1, 0, 2.6 + k * 0.42), (s * 0.3, 0, 2.6 + k * 0.42)], [0.012, 0.012], 4, "metal_dark")
    g.box((0.0, -0.15, 1.7), (0.16, 0.012, 0.22), "sign_yellow")
    g.box((0.0, -0.157, 1.7), (0.03, 0.004, 0.14), "black")
    g.box((0, -0.145, 4.5), (0.26, 0.012, 0.03), "metal")                                    # cerclages
    lathe(g, 0, 0, [(4.48, 0.128, "metal"), (4.52, 0.128, "metal")], 10, cap=False)
    # éclairage public : crosse et lanterne
    g.tube([(0, -0.1, 6.2), (0, -0.4, 6.45), (0, -0.9, 6.52)], [0.03, 0.028, 0.025], 6, "metal")
    g.prism([(-0.82, 6.5), (-0.86, 6.58), (-1.2, 6.57), (-1.28, 6.5), (-1.2, 6.46), (-0.86, 6.46)], -0.12, 0.12, "metal_dark", axis='x')
    g.box((0, -1.05, 6.45), (0.16, 0.3, 0.03), "lamp")
    g.box((0.2, 0.0, 7.0), (0.3, 0.34, 0.5), "metal")                                        # coffret
    g.tube([(0.2, 0, 6.75), (0.2, 0, 0.0)], [0.02, 0.02], 5, "metal_dark")


@hd("phone_booth")
def phone_booth(g):
    r = "sign_red"
    g.box((0, 0, -0.15), (1.06, 1.06, 0.5), "metal_dark")
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.box((sx * 0.47, sy * 0.47, 1.2), (0.08, 0.08, 2.2), r)
    g.box((0, 0, 2.38), (1.08, 1.08, 0.2), r)
    pts = [(-0.54, 2.48)] + [(-0.54 + 1.08 * i / 6, 2.48 + 0.14 * math.sin(math.pi * i / 6)) for i in range(1, 6)] + [(0.54, 2.48)]
    g.prism(pts, -0.54, 0.54, r, axis='y')
    for f in (Fac(g, 'y', -0.5, -1), Fac(g, 'y', 0.5, 1), Fac(g, 'x', -0.5, -1), Fac(g, 'x', 0.5, 1)):
        f.quad(0, 1.25, 0.86, 1.9, "glass_blue", 0.0)
        g.quad(f.P(0, -0.004, 1.25), 0.86, 1.9, f.open_dir, "glass_blue")
        for k in range(1, 6):                         # petits bois
            f.box(0, 0.3 + k * 0.32, 0.86, 0.03, 0.03, r, out=-0.015)
        for u in (-0.2, 0.2):
            f.box(u, 1.25, 0.03, 1.9, 0.03, r, out=-0.015)
        f.box(0, 0.25, 0.86, 0.5, 0.03, r, out=-0.015)
        f.slab(0, 2.38, 0.74, 0.13, 0.012, "white", out=0.04)
    Fac(g, 'y', -0.5, -1).slab(0.36, 1.2, 0.04, 0.3, 0.05, "brass", out=0.01)                # poignée
    g.box((0, 0.36, 1.35), (0.4, 0.14, 0.55), "metal")                                       # appareil
    g.box((0, 0.28, 1.45), (0.24, 0.03, 0.2), "black")
    g.tube([(-0.12, 0.27, 1.25), (-0.16, 0.2, 0.9), (-0.1, 0.26, 1.1)], [0.01] * 3, 3, "black", cap_end=False)


@hd("billboard")
def billboard(g):
    for sx in (-1, 1):                                # pieds en treillis, jambes de force
        for dx in (-0.15, 0.15):
            g.beam((sx * 2.0 + dx, 0.1, -0.5), (sx * 2.0 + dx, 0.1, 5.7), 0.07, "metal")
        for k in range(7):
            z = 0.2 + k * 0.4
            g.beam((sx * 2.0 - 0.15, 0.1, z), (sx * 2.0 + 0.15, 0.1, z + 0.4), 0.03, "metal")
        g.beam((sx * 2.0, 0.12, 4.6), (sx * 2.0, 1.5, 0.0), 0.06, "metal")
    g.box((0, 0, 4.2), (6.0, 0.14, 3.0), "metal_dark")
    g.box((0, -0.075, 4.2), (5.7, 0.02, 2.7), "awning_cream")
    f = Fac(g, 'y', -0.085, -1)
    for s in (-1, 1):                                 # cadre
        f.slab(0, 4.2 + s * 1.44, 6.0, 0.12, 0.04, "metal")
        f.slab(s * 2.94, 4.2, 0.12, 3.0, 0.04, "metal")
    f.slab(-1.2, 4.4, 2.6, 1.6, 0.004, "sign_blue")                                          # affiche : sommets, soleil, texte
    f.poly([(-2.45, 3.62), (-1.9, 4.5), (-1.6, 4.2), (-1.2, 5.05), (-0.7, 4.1), (-0.3, 4.6), (0.08, 3.62)], "white", 0.008)
    f.poly([(-2.45, 3.62), (-1.9, 4.1), (-1.5, 3.9), (-1.0, 4.3), (-0.5, 3.85), (0.08, 3.62)], "leaf", 0.012)
    f.poly([(0.08 - 0.6 + math.cos(6.2832 * k / 12) * 0.2, 4.9 + math.sin(6.2832 * k / 12) * 0.2) for k in range(12)], "yellow", 0.012)
    f.slab(1.6, 4.7, 1.7, 0.5, 0.004, "sign_red")
    for k in range(5):
        f.slab(0.95 + k * 0.33, 4.7, 0.22, 0.26, 0.008, "white")
    for k in range(3):
        f.slab(1.6, 4.15 - k * 0.24, 1.9 - k * 0.3, 0.1, 0.006, "black")
    g.box((0, -0.75, 2.62), (6.2, 0.9, 0.05), "metal")                                       # passerelle
    for x in (-3.0, -1.5, 0, 1.5, 3.0):
        g.box((x, -1.18, 3.1), (0.04, 0.04, 0.95), "metal")
        g.beam((x, -0.3, 2.6), (x, -0.05, 2.9), 0.04, "metal")
    g.box((0, -1.18, 3.56), (6.2, 0.04, 0.04), "metal")
    for x in (-1.8, 0, 1.8):                          # projecteurs sur potence
        g.tube([(x, -0.07, 5.72), (x, -0.3, 5.95), (x, -0.55, 5.9)], [0.02] * 3, 5, "metal")
        g.box((x, -0.6, 5.84), (0.34, 0.16, 0.1), "metal_dark")
        g.box((x, -0.6, 5.785), (0.28, 0.12, 0.02), "lamp")


# ----------------------------------------------------------------- étals de marché
def stall(g, canopy, canopy2, goods):
    W, D = 3.0, 1.6
    for k in range(4):                                # comptoir en planches, jupe de toile
        g.box((0, -D * 0.375 + 0.02, 0.12 + k * 0.215), (W, 0.035, 0.2), "wood" if k % 2 else "wood_honey")
    g.box((0, 0.02, 0.45), (W - 0.06, D * 0.7, 0.86), "wood_dark")
    g.box((0, -0.05, 0.93), (W + 0.1, D * 0.8, 0.05), "wood_honey")
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.box((sx * (W / 2 + 0.05), sy * D / 2, 1.0), (0.07, 0.07, 3.0), "wood")
        g.beam((sx * (W / 2 + 0.05), -D / 2, 2.2), (sx * (W / 2 + 0.05), D / 2, 2.3), 0.05, "wood")
        g.beam((sx * (W / 2 + 0.05), -D / 2, 1.8), (sx * (W / 2 + 0.05), -D / 2 - 0.45, 2.2), 0.04, "wood")
    n = 8
    seg = (W + 0.6) / n
    for k in range(n):                                # toile rayée, lambrequin festonné
        x0 = -W / 2 - 0.3 + seg * k
        c = canopy2 if (canopy2 and k % 2) else canopy
        g.prism([(-D / 2 - 0.5, 2.2), (0, 2.75), (D / 2 + 0.3, 2.3), (D / 2 + 0.3, 2.33), (0, 2.78), (-D / 2 - 0.5, 2.23)],
                x0, x0 + seg, c, axis='x')
        yv = -D / 2 - 0.5
        g.face([(x0, yv, 2.21), (x0 + seg, yv, 2.21), (x0 + seg, yv - 0.01, 2.07), (x0 + seg / 2, yv - 0.012, 1.99), (x0, yv - 0.01, 2.07)], c)
        g.face([(x0, yv + 0.004, 2.07), (x0 + seg / 2, yv + 0.004, 1.99), (x0 + seg, yv + 0.004, 2.07), (x0 + seg, yv + 0.004, 2.21), (x0, yv + 0.004, 2.21)], c)
    g.box((0, 0, 2.77), (W + 0.6, 0.05, 0.05), "wood")
    for k, (col, dx) in enumerate(goods):             # cagettes garnies, étiquettes
        m = g.mark()
        g.box((0, 0, 0.0), (0.56, 0.44, 0.02), "wood_cut")
        for s in (-1, 1):
            g.box((s * 0.27, 0, 0.07), (0.02, 0.44, 0.12), "wood_cut")
            g.box((0, s * 0.21, 0.07), (0.56, 0.02, 0.12), "wood_cut")
        for i in range(4):
            for j in range(3):
                g.octa((-0.19 + 0.127 * i + (0.03 if j % 2 else 0), -0.13 + 0.13 * j, 0.11 + 0.02 * ((i + j) % 2)), 0.068, col)
        g.transform(g.since(m), Matrix.Translation((dx, -0.3, 0.97)) @ Matrix.Rotation(0.22, 4, 'X'))
        g.box((dx + 0.18, -0.56, 1.06), (0.12, 0.01, 0.09), "sign_white")
    lathe(g, 1.2, 0.2, [(0.955, 0.12, "metal"), (0.99, 0.1, "metal"), (1.0, 0.03, "metal"), (1.22, 0.03, "metal")], 8)   # balance
    g.frustum((1.2, 0.2, 1.22), 0.06, 0.2, 0.06, 10, "brass", auto=False)
    g.box((-1.2, 0.3, 1.2), (0.5, 0.35, 0.5), "wood_cut")                                    # caisses en réserve
    g.box((-1.25, 0.32, 1.52), (0.42, 0.3, 0.14), "wood")


hd("market_stall_a")(lambda g: stall(g, "awning_red", "awning_cream", [("apple", -1.0), ("pear", -0.3), ("orange", 0.4), ("leaf", 1.05)]))
hd("market_stall_b")(lambda g: stall(g, "awning_green", None, [("yellow", -1.0), ("wood_cut", -0.3), ("yellow", 0.4), ("plum", 1.05)]))
