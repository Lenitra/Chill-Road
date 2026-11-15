"""Pack alpin, lot 9 : arbres (jeune épicéa, arolles, bouleaux, feuillus, peuplier, saule), buissons, fleurs,
rochers et falaises, névé ; animaux (vaches, mouton, chèvre, bouquetin, marmotte, chien). Animaux tournés vers -Y."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
from alp_nature import nat, conifer, boulder


# ================================================================= arbres
nat("spruce_young", "Spruce_Young", 430, lambda g: conifer(g, 5.5, 1.5, 5, trunk=0.14, bare=0.08), radius=0.15,
    tags=["tree", "conifer", "small"])


def dead_spruce(g):
    H = 14.0
    g.frustum((0, 0, -0.5), 0.3, 0.05, H + 0.5, lod(g, 6, 5, 4), "dead", cap0=False)
    for k in range(lod(g, 16, 9, 0)):
        z = H * g.R(0.25, 0.92)
        a = g.R(0, 6.28)
        L = (1.0 - z / H) * 2.6 + 0.4
        p0 = Vector((0, 0, z))
        p1 = p0 + Vector((math.cos(a) * L, math.sin(a) * L, -L * g.R(0.15, 0.5)))
        g.limb(p0, p1, 0.06, 0.015, 3, "dead", cap1=False)


nat("dead_spruce", "Dead_Spruce", 431, dead_spruce, radius=0.3, tags=["tree", "dead"])


def swiss_pine(g, H, width):
    """Arolle : tronc trapu, houppier dense et arrondi, souvent penché."""
    conifer(g, H * 0.82, width, 4, color="arolla", trunk=0.34, bare=0.2, droop=0.08, taper=0.55)
    f = g.ico((g.R(-0.2, 0.2), g.R(-0.2, 0.2), H * 0.78), width * 0.62, lod(g, 2, 1, 1), "arolla", scale=(1, 1, 1.25))
    g.jitter(f, width * 0.07)
    g.transform(list(g.bm.faces), Matrix.Rotation(g.seed * 1.3, 4, 'Z') @ Matrix.Rotation(0.04 + (g.seed % 7) * 0.012, 4, 'X'))


nat("swisspine_a", "SwissPine_A", 432, lambda g: swiss_pine(g, 9.0, 2.6), radius=0.34, tags=["tree", "conifer"])
nat("swisspine_b", "SwissPine_B", 433, lambda g: swiss_pine(g, 12.0, 3.0), radius=0.4, tags=["tree", "conifer"])


def broadleaf(g, H, trunk_h, blobs, colors=("leaf", "leaf_light"), trunk=0.28, bark="bark", flat=0.9):
    """Feuillu : tronc, charpentières, houppier en boules facettées. blobs = [((x, y, z), r)] en fraction de H."""
    g.frustum((0, 0, -0.5), trunk, trunk * 0.7, trunk_h + 0.5, lod(g, 6, 5, 4), bark, cap0=False)
    use = blobs if g.lod == 0 else blobs[:max(3, len(blobs) * 2 // 3)] if g.lod == 1 else None
    if use is None:
        cz = sum(b[0][2] for b in blobs) / len(blobs)
        R = max(math.hypot(b[0][0], b[0][1]) + b[1] for b in blobs)
        zr = (max(b[0][2] + b[1] for b in blobs) - min(b[0][2] - b[1] for b in blobs)) / 2
        f = g.ico((0, 0, cz * H), R * H * 0.92, 1, colors[0], scale=(1, 1, zr / R))
        g.jitter(f, R * H * 0.08)
        return
    if g.lod < 2:
        for (c, r) in use[1:4]:
            g.limb((0, 0, trunk_h * 0.9), (c[0] * H * 0.7, c[1] * H * 0.7, c[2] * H - r * H * 0.4), trunk * 0.55, trunk * 0.25, 5, bark)
    for k, (c, r) in enumerate(use):
        f = g.ico((c[0] * H, c[1] * H, c[2] * H), r * H, 2 if (g.lod == 0 and k == 0) else 1, colors[k % len(colors)],
                  scale=(1, 1, flat))
        g.jitter(f, r * H * 0.1)


ROUND = [((0.0, 0.0, 0.66), 0.27), ((0.2, 0.08, 0.56), 0.2), ((-0.19, 0.1, 0.57), 0.2), ((0.04, -0.21, 0.58), 0.2),
         ((-0.05, -0.03, 0.82), 0.19), ((0.1, 0.18, 0.74), 0.16)]
TALL = [((0.0, 0.0, 0.6), 0.2), ((0.1, 0.05, 0.45), 0.17), ((-0.1, 0.06, 0.47), 0.17), ((0.02, -0.1, 0.5), 0.17),
        ((0.0, 0.0, 0.8), 0.16), ((0.05, 0.03, 0.9), 0.1)]
WIDE = [((0.0, 0.0, 0.62), 0.3), ((0.33, 0.1, 0.55), 0.22), ((-0.32, 0.12, 0.56), 0.22), ((0.05, -0.33, 0.56), 0.22),
        ((-0.08, 0.3, 0.6), 0.2), ((0.0, 0.0, 0.84), 0.2)]
nat("deciduous_a", "Deciduous_A", 440, lambda g: broadleaf(g, 10.0, 3.2, ROUND), radius=0.3, tags=["tree", "broadleaf"])
nat("deciduous_b", "Deciduous_B", 441, lambda g: broadleaf(g, 14.0, 4.5, TALL, ("leaf_dark", "leaf"), trunk=0.36),
    radius=0.38, tags=["tree", "broadleaf"])
nat("deciduous_c", "Deciduous_C", 442, lambda g: broadleaf(g, 8.0, 2.4, WIDE, ("leaf_light", "leaf"), flat=0.8),
    radius=0.3, tags=["tree", "broadleaf"])


def birch(g, H):
    n = lod(g, 5, 4, 4)
    g.frustum((0, 0, -0.5), 0.15, 0.05, H * 0.9 + 0.5, n, "birch_bark", cap0=False)
    if g.lod == 0:
        for k in range(5):
            z = H * g.R(0.08, 0.5)
            g.frustum((0, 0, z), 0.155 * (1 - z / H * 0.6), 0.15 * (1 - z / H * 0.6), 0.1, n, "black", cap0=False, cap1=False)
    blobs = [((0.0, 0.0, 0.7), 0.16), ((0.09, 0.04, 0.55), 0.13), ((-0.08, 0.05, 0.6), 0.12), ((0.0, -0.08, 0.5), 0.12),
             ((0.0, 0.02, 0.86), 0.11)]
    use = blobs[:lod(g, 5, 4, 1)]
    for k, (c, r) in enumerate(use):
        rr = r * H * (1.0 if g.lod < 2 else 1.6)
        f = g.ico((c[0] * H, c[1] * H, c[2] * H), rr, 1, "leaf_light" if k % 2 == 0 else "leaf_yellow", scale=(1, 1, 1.25))
        g.jitter(f, rr * 0.12)


nat("birch_a", "Birch_A", 445, lambda g: birch(g, 9.0), radius=0.15, tags=["tree", "broadleaf"])
nat("birch_b", "Birch_B", 446, lambda g: birch(g, 12.0), radius=0.18, tags=["tree", "broadleaf"])


def poplar(g):
    H = 19.0
    g.frustum((0, 0, -0.5), 0.3, 0.2, 3.5, lod(g, 6, 5, 4), "bark", cap0=False)
    parts = [(4.5, 1.5, 3.0), (8.5, 1.8, 3.6), (12.5, 1.6, 3.4), (16.0, 1.1, 3.0)]
    if g.lod == 2:
        f = g.ico((0, 0, 10.5), 1.8, 1, "leaf", scale=(1, 1, 4.6))
        return
    for k, (z, r, hz) in enumerate(parts):
        f = g.ico((g.R(-0.15, 0.15), g.R(-0.15, 0.15), z), r, 2 if (g.lod == 0 and k in (1, 2)) else 1,
                  "leaf" if k % 2 == 0 else "leaf_dark",
                  scale=(1, 1, hz / r))
        g.jitter(f, 0.16)


nat("poplar", "Poplar", 447, poplar, radius=0.3, tags=["tree", "broadleaf", "tall"])


def willow(g):
    """Saule pleureur : couronne large d'où retombent des rideaux de feuillage."""
    g.frustum((0, 0, -0.5), 0.4, 0.3, 3.2, lod(g, 6, 5, 4), "bark", cap0=False)
    f = g.ico((0, 0, 5.6), 3.0, lod(g, 2, 1, 1), "leaf_light", scale=(1, 1, 0.7))
    g.jitter(f, 0.25)
    n = lod(g, 9, 6, 5)
    rim0 = [Vector((math.cos(6.2832 * k / n) * 3.1, math.sin(6.2832 * k / n) * 3.1, 5.4)) for k in range(n)]
    for k in range(n):
        a, b = rim0[k], rim0[(k + 1) % n]
        drop = g.R(3.0, 4.4)
        mid = (a + b) / 2 * 1.08
        mid.z = 5.4 - drop
        g.face([a, b, mid], "leaf")
        g.face([b + Vector((0, 0, 0.01)), a + Vector((0, 0, 0.01)), mid * 0.995], "leaf")


nat("willow", "Willow", 448, willow, radius=0.4, tags=["tree", "broadleaf", "water"])


# ================================================================= buissons, herbes, fleurs
def bush_rhodo(g):
    blobs = [((0, 0, 0.45), 0.6), ((0.55, 0.2, 0.35), 0.45), ((-0.5, 0.25, 0.35), 0.45), ((0.1, -0.5, 0.33), 0.42)]
    for k, (c, r) in enumerate(blobs[:lod(g, 4, 3, 1)]):
        rr = r if g.lod < 2 else 0.9
        f = g.ico(c, rr, 2 if (g.lod == 0 and k == 0) else 1, "leaf_dark", scale=(1, 1, 0.75))
        g.jitter(f, rr * 0.1)
        if g.lod < 2:
            for j in range(lod(g, 6, 3)):
                a, e = g.R(0, 6.28), g.R(0.2, 1.3)
                d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e) * 0.75))
                g.octa(Vector(c) + d * rr * 0.97, 0.11, "rhodo", scale=(1, 1, 0.8))
        else:
            g.ico((c[0], c[1], c[2] + rr * 0.45), rr * 0.55, 1, "rhodo", scale=(1, 1, 0.5))


nat("bush_rhodo", "Bush_Rhodo", 450, bush_rhodo, tags=["bush", "flowers"])


def juniper(g):
    for k in range(lod(g, 5, 3, 1)):
        a = 6.2832 * k / 5 + g.R(-0.3, 0.3)
        d = 0.0 if k == 0 else g.R(0.5, 0.9)
        r = g.R(0.45, 0.7) if g.lod < 2 else 1.1
        f = g.ico((math.cos(a) * d, math.sin(a) * d, r * 0.3), r, 1, "spruce" if k % 2 else "arolla", scale=(1.2, 1.0, 0.5))
        g.jitter(f, r * 0.14)


nat("juniper", "Juniper", 451, juniper, tags=["bush"])


def fern(g):
    n = lod(g, 9, 6, 4)
    for k in range(n):
        a = 6.2832 * k / n + g.R(-0.2, 0.2)
        d = Vector((math.cos(a) * 0.75, math.sin(a) * 0.75, 0.75))
        g.blade((0, 0, 0), d, g.R(0.7, 1.0), g.R(0.26, 0.34), "leaf" if k % 2 else "leaf_light", bend=g.R(0.3, 0.5),
                segs=2 if g.lod == 0 else 1)


nat("fern", "Fern", 452, fern, tags=["plant", "detail"])


def grass_tall(g):
    count = lod(g, 16, 10, 6)
    for k in range(count):
        a = 6.2832 * k / count + g.R(-0.3, 0.3)
        tilt = g.R(0.08, 0.4)
        d = Vector((math.cos(a) * tilt, math.sin(a) * tilt, 1.0))
        rr = 0.35 * math.sqrt(g.R(0.05, 1.0))
        g.blade((math.cos(a) * rr, math.sin(a) * rr, 0), d, g.R(0.5, 0.95), g.R(0.09, 0.13),
                "dry" if k % 3 == 0 else "grass", bend=g.R(0.15, 0.35), segs=2 if g.lod == 0 else 1)


nat("grass_tall", "Grass_Tall", 453, grass_tall, tags=["plant", "detail"])


def flowers(g, kind):
    """Touffe de fleurs d'alpage : gentianes (trompettes bleues), edelweiss (étoiles blanches), prairie (mélange)."""
    n = lod(g, 6, 4, 3) if kind != "meadow" else lod(g, 5, 4, 3)
    cols = {"gentian": ("flower_blue",), "edelweiss": ("flower_white",),
            "meadow": ("flower_yellow", "flower_pink", "flower_white", "poppy", "flower_blue")}[kind]
    for k in range(n):
        a = g.R(0, 6.28)
        d = g.R(0.03, 0.3)
        base = Vector((math.cos(a) * d, math.sin(a) * d, 0))
        h = g.R(0.1, 0.18) if kind != "meadow" else g.R(0.25, 0.5)
        top = base + Vector((g.R(-0.04, 0.04), g.R(-0.04, 0.04), h))
        g.limb(base, top, 0.012, 0.009, 3, "grass", cap1=False)
        c = cols[k % len(cols)]
        if kind == "gentian":
            g.frustum(top, 0.018, 0.06, 0.09, 5, c, rot=g.R(0, 6.28), cap0=False, cap1=False)
            g.frustum(top + Vector((0, 0, 0.0)), 0.018, 0.0, 0.05, 5, "flower_white", cap0=False)
        elif kind == "edelweiss":
            pts = []
            rot = g.R(0, 6.28)
            for i in range(10):
                r = 0.075 if i % 2 == 0 else 0.03
                pts.append((top.x + math.cos(rot + 0.6283 * i) * r, top.y + math.sin(rot + 0.6283 * i) * r, top.z))
            g.face(pts, c)
            g.octa(top + Vector((0, 0, 0.012)), 0.022, "flower_yellow", scale=(1, 1, 0.5))
        else:
            g.frustum(top - Vector((0, 0, 0.01)), 0.02, 0.06, 0.04, 5, c, rot=g.R(0, 6.28), cap0=False)
            g.octa(top + Vector((0, 0, 0.03)), 0.02, "flower_yellow" if c != "flower_yellow" else "center", scale=(1, 1, 0.5))
    if g.lod < 2:
        for k in range(lod(g, 6, 3)):
            a = g.R(0, 6.28)
            g.blade((math.cos(a) * 0.1, math.sin(a) * 0.1, 0), (math.cos(a) * 0.4, math.sin(a) * 0.4, 1), g.R(0.12, 0.3),
                    0.07, "grass" if kind != "edelweiss" else "dry", bend=0.3, segs=1)


nat("flowers_gentian", "Flowers_Gentian", 455, lambda g: flowers(g, "gentian"), tags=["flowers", "detail"])
nat("flowers_edelweiss", "Flowers_Edelweiss", 456, lambda g: flowers(g, "edelweiss"), tags=["flowers", "detail"])
nat("flowers_meadow", "Flowers_Meadow", 457, lambda g: flowers(g, "meadow"), tags=["flowers", "detail"])


# ================================================================= rochers, falaises, névé
def rock_slab(g):
    f = g.box((0, 0, 0.15), (3.4, 2.2, 0.9), "rock")
    g.jitter(f, 0.22)
    g.transform(f, Matrix.Rotation(0.14, 4, 'Y') @ Matrix.Rotation(0.08, 4, 'X'))
    if g.lod < 2:
        f = g.box((1.2, -0.9, 0.3), (1.4, 1.0, 0.5), "rock_light")
        g.jitter(f, 0.15)


nat("rock_slab", "Rock_Slab", 460, rock_slab, radius=1.3, tags=["rock"])


def crag(g, c, size, color="rock", sub=2, block=0.55, jit=0.12, rot=0.0):
    """Bloc rocheux anguleux : icosphère « cubifiée » (faces franches), bosselée, étirée."""
    m = g.mark()
    f = g.ico((0, 0, 0), 1.0, sub, color)
    for v in {v for q in f for v in q.verts}:
        for i in range(3):
            a = v.co[i]
            v.co[i] = math.copysign(abs(a) ** block, a)
    g.jitter(f, jit)
    g.transform(f, Matrix.Translation(Vector(c)) @ Matrix.Rotation(rot, 4, 'Z')
                @ Matrix.Diagonal((size[0] / 2, size[1] / 2, size[2] / 2, 1.0)))
    return f


def cliff(g, parts, tufts=()):
    """Affleurement : blocs (centre, taille) empilés ; base prolongée sous le sol, touffes d'herbe sur les replats."""
    use = parts if g.lod == 0 else parts[:max(2, len(parts) * 2 // 3)] if g.lod == 1 else parts[:2]
    for k, (c, s) in enumerate(use):
        crag(g, c, s, "rock" if k % 3 else "rock_light", sub=lod(g, 2, 1, 1), jit=lod(g, 0.12, 0.16, 0.16),
             rot=((k * 37) % 50) / 100.0 - 0.25)
    if g.lod == 0:
        for (x, y, z, r) in tufts:
            f = g.ico((x, y, z), r, 1, "grass", scale=(1.3, 1.0, 0.3))
            g.jitter(f, r * 0.1)


nat("cliff_a", "Cliff_A", 461,
    lambda g: cliff(g, [((0, 0, 2.0), (9.0, 5.0, 12.0)), ((-3.8, 0.4, 0.8), (5.0, 4.5, 8.5)), ((3.9, 0.6, 0.0), (4.5, 4.0, 7.0)),
                        ((1.0, -1.6, -1.2), (4.0, 3.0, 5.0))], [(-3.8, 0.4, 5.1, 1.4), (3.9, 0.6, 3.6, 1.2)]),
    radius=4.0, tags=["rock", "cliff"], base=0.0, note="base buried 4 m; push the back (Unity -Z) into the slope")
nat("cliff_b", "Cliff_B", 462,
    lambda g: cliff(g, [((0, 0.5, 4.0), (10.0, 6.0, 16.0)), ((-5.5, 0.3, 2.5), (6.5, 5.0, 12.0)), ((5.5, 0.6, 1.5), (6.0, 5.5, 10.0)),
                        ((-1.5, -2.2, -0.5), (5.0, 3.5, 7.0)), ((3.0, -2.0, -1.5), (4.0, 3.0, 5.0)), ((-7.5, -0.5, -0.5), (3.5, 3.5, 5.5))],
                    [(-5.5, 0.3, 8.6, 1.8), (5.5, 0.6, 6.6, 1.6), (-1.5, -2.2, 3.1, 1.2)]),
    radius=6.0, tags=["rock", "cliff", "large"], base=0.0, note="base buried 4 m; push the back (Unity -Z) into the slope")
nat("cliff_c", "Cliff_C", 463,
    lambda g: cliff(g, [((0, 0, 5.5), (5.5, 5.0, 19.0)), ((3.2, 0.8, 2.0), (4.0, 4.0, 12.0)), ((-2.8, -0.5, 0.5), (3.6, 3.5, 9.0)),
                        ((0.8, -2.4, -1.0), (3.0, 2.8, 6.0))], [(3.2, 0.8, 8.1, 1.0)]),
    radius=3.4, tags=["rock", "cliff", "pinnacle"], base=0.0, note="rock pinnacle, base buried 4 m")


def scree(g):
    for k in range(lod(g, 14, 8, 4)):
        a = g.R(0, 6.28)
        d = 2.8 * math.sqrt(g.R(0, 1))
        r = g.R(0.2, 0.55) * (1.0 if g.lod < 2 else 1.8)
        f = g.ico((math.cos(a) * d, math.sin(a) * d, r * 0.15), r, 1, "rock" if k % 2 else "rock_light",
                  scale=(g.R(0.9, 1.3), g.R(0.8, 1.1), g.R(0.5, 0.75)))
        g.jitter(f, r * 0.12)


nat("scree", "Scree", 464, scree, tags=["rock", "scatter"])


def snow_patch(g):
    f = g.ico((0, 0, 0.0), 1.0, lod(g, 2, 1, 1), "snow", scale=(4.2, 2.8, 0.28))
    g.jitter(f, 0.06)
    for v in {v for q in f for v in q.verts}:
        v.co.x += math.sin(v.co.y * 1.3) * 0.5
        v.co.z = max(v.co.z, -0.12)


nat("snow_patch", "Snow_Patch", 465, snow_patch, tags=["snow"], grad_h=0.4, base=-0.2)


# ================================================================= animaux
def quadruped(g, L, H, W, coat, head_c=None, leg_c=None, pose="stand", legs=0.5, neck=0.45, head=(0.26, 0.46, 0.28),
              spots=None, tail=True, fluffy=False):
    """Quadrupède facetté tourné vers -Y. L = longueur du corps, H = hauteur au garrot, W = largeur.
    pose : 'stand', 'graze' (tête au sol), 'lie' (couché). Renvoie la position de la tête."""
    head_c = head_c or coat
    leg_c = leg_c or coat
    leg_h = H * legs if pose != "lie" else 0.0
    bh = H - H * legs                                # hauteur du corps
    zc = leg_h + bh / 2
    if fluffy:
        f = g.ico((0, 0, zc), 0.5, lod(g, 2, 1), coat, scale=(W * 1.15, L * 1.05, bh * 1.1))
        g.jitter(f, 0.03)
    else:
        # corps : prisme à section octogonale aplatie, plus étroit à l'arrière
        f = g.frustum((0, 0, 0), 0.5, 0.44, 1.0, lod(g, 8, 6), coat, rot=math.pi / 8)
        g.transform(f, Matrix.Translation((0, L / 2, zc)) @ Matrix.Rotation(math.pi / 2, 4, 'X')
                    @ Matrix.Diagonal((W, bh, L, 1.0)))
        if spots and g.lod == 0:
            for (y, z, sx, sy, sz, c) in spots:
                q = g.ico((sx * W * 0.38, y * L, zc + z * bh), 0.5, 1, c, scale=(W * 0.36, sy * L, sz * bh))
    if pose != "lie":
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.frustum((sx * W * 0.3, sy * L * 0.36, -0.1), W * 0.1, W * 0.14, leg_h + 0.2, 4, leg_c, rot=math.pi / 4, cap0=False)
    elif g.lod == 0:
        for sx in (-1, 1):
            g.box((sx * W * 0.35, -L * 0.4, 0.07), (W * 0.2, L * 0.4, 0.14), leg_c)
    # encolure et tête
    sh = Vector((0, -L * 0.42, zc + bh * 0.2))
    if pose == "graze":
        hp = Vector((0, -L * 0.5 - neck * 0.7, H * 0.12 + head[2] * 0.5))
    elif pose == "lie":
        hp = Vector((0, -L * 0.5 - neck * 0.55, zc + bh * 0.45))
    else:
        hp = Vector((0, -L * 0.5 - neck * 0.55, zc + bh * 0.2 + neck * 0.62))
    g.beam(sh, hp, W * 0.5, coat, bh * 0.55)
    tilt = -0.9 if pose == "graze" else -0.25
    g.box(hp + Vector((0, -head[1] * 0.3, 0)), head, head_c, rot=Matrix.Rotation(tilt, 3, 'X'))
    if tail and g.lod < 2:
        g.limb((0, L / 2, zc + bh * 0.35), (0, L / 2 + 0.12, zc - bh * 0.6), 0.03, 0.02, 3, leg_c)
    return hp


def cow(g, pose, coat, patch, bell=True):
    spots = [(-0.2, 0.15, 1, 0.3, 0.6, patch), (0.2, 0.2, -1, 0.34, 0.5, patch), (0.05, 0.35, 1, 0.2, 0.3, patch)]
    hp = quadruped(g, 1.9, 1.38, 0.7, coat, head_c=patch, leg_c=coat, pose=pose, legs=0.48, neck=0.5,
                   head=(0.3, 0.5, 0.3), spots=spots)
    if g.lod < 2:
        for sx in (-1, 1):
            g.limb(hp + Vector((sx * 0.13, 0.05, 0.12)), hp + Vector((sx * 0.3, -0.02, 0.3)), 0.03, 0.008, 4, "horn")
            g.box(hp + Vector((sx * 0.22, 0.1, 0.05)), (0.16, 0.05, 0.09), coat)
        g.box(hp + Vector((0, -0.42 if pose != "graze" else -0.3, -0.08 if pose != "graze" else -0.22)), (0.24, 0.12, 0.16),
              "skin_pink")
        if pose != "lie":
            g.ico((0, 0.45, 1.38 * 0.48 - 0.02), 0.16, 1, "skin_pink", scale=(1, 1.2, 0.8))
        if bell:
            g.frustum(hp + Vector((0, 0.3, -0.5)), 0.1, 0.06, 0.16, 5, "brass")
            g.box(hp + Vector((0, 0.3, -0.3)), (0.36, 0.3, 0.07), "wood_red")


reg(dict(id="cow_a", file="Cow_A", cat="animal", tags=["cow"], seed=470, lods=3, collider="box", fp=(0.8, 2.6),
         build=lambda g: cow(g, "stand", "cow_brown", "cow_white")))
reg(dict(id="cow_b", file="Cow_B", cat="animal", tags=["cow"], seed=471, lods=3, collider="box", fp=(0.8, 2.6),
         build=lambda g: cow(g, "graze", "cow_white", "cow_black")))
reg(dict(id="cow_lying", file="Cow_Lying", cat="animal", tags=["cow"], seed=472, lods=3, collider="box", fp=(0.9, 2.6),
         build=lambda g: cow(g, "lie", "cow_brown", "cow_white", bell=False)))


def sheep(g):
    hp = quadruped(g, 1.0, 0.75, 0.5, "cow_white", head_c="cow_black", leg_c="cow_black", legs=0.4, neck=0.25,
                   head=(0.16, 0.26, 0.17), fluffy=True, tail=False)
    if g.lod < 2:
        for sx in (-1, 1):
            g.box(hp + Vector((sx * 0.12, 0.03, 0.03)), (0.1, 0.04, 0.06), "cow_black")


reg(dict(id="sheep", file="Sheep", cat="animal", tags=["sheep"], seed=473, lods=3, fp=(0.6, 1.3), build=sheep))


def goat(g):
    hp = quadruped(g, 1.0, 0.8, 0.36, "white", legs=0.52, neck=0.3, head=(0.14, 0.28, 0.16))
    if g.lod < 2:
        for sx in (-1, 1):
            g.limb(hp + Vector((sx * 0.05, 0.05, 0.08)), hp + Vector((sx * 0.1, 0.22, 0.3)), 0.025, 0.006, 4, "horn")
        g.limb(hp + Vector((0, -0.16, -0.08)), hp + Vector((0, -0.14, -0.26)), 0.03, 0.005, 3, "white")


reg(dict(id="goat", file="Goat", cat="animal", tags=["goat"], seed=474, lods=3, fp=(0.5, 1.3), build=goat))


def ibex(g):
    hp = quadruped(g, 1.3, 0.95, 0.46, "fur_tan", leg_c="bark", legs=0.46, neck=0.4, head=(0.17, 0.33, 0.2))
    for sx in (-1, 1):                               # grandes cornes annelées recourbées vers l'arrière
        p0 = hp + Vector((sx * 0.06, 0.06, 0.1))
        p1 = p0 + Vector((sx * 0.08, 0.2, 0.42))
        p2 = p1 + Vector((sx * 0.1, 0.36, 0.1))
        g.limb(p0, p1, 0.045, 0.035, 4, "horn", cap1=False)
        g.limb(p1, p2, 0.035, 0.008, 4, "horn")
    if g.lod == 0:
        g.limb(hp + Vector((0, -0.18, -0.1)), hp + Vector((0, -0.16, -0.26)), 0.03, 0.005, 3, "bark")


reg(dict(id="ibex", file="Ibex", cat="animal", tags=["ibex", "summit"], seed=475, lods=3, fp=(0.6, 1.7), build=ibex))


def marmot(g):
    """Marmotte dressée en sentinelle."""
    f = g.ico((0, 0.02, 0.2), 0.2, 1, "fur_tan", scale=(1.0, 1.0, 1.35))
    g.ico((0, -0.03, 0.47), 0.11, 1, "fur_tan", scale=(1, 1.15, 1))
    g.box((0, -0.14, 0.44), (0.07, 0.06, 0.05), "bark")
    if g.lod == 0:
        for sx in (-1, 1):
            g.box((sx * 0.07, 0.0, 0.57), (0.04, 0.03, 0.04), "bark")
            g.limb((sx * 0.1, -0.1, 0.3), (sx * 0.05, -0.18, 0.24), 0.03, 0.02, 3, "bark")
            g.box((sx * 0.1, -0.08, 0.03), (0.08, 0.16, 0.06), "bark")
        g.limb((0, 0.15, 0.06), (0.05, 0.4, 0.03), 0.05, 0.02, 4, "bark")


reg(dict(id="marmot", file="Marmot", cat="animal", tags=["marmot", "small"], seed=476, lods=2, build=marmot))


def dog(g):
    """Bouvier (chien de montagne) : robe noire, poitrail blanc, pattes fauves."""
    hp = quadruped(g, 0.95, 0.68, 0.34, "cow_black", head_c="cow_black", leg_c="fur_tan", legs=0.45, neck=0.24,
                   head=(0.2, 0.3, 0.2), tail=False)
    if g.lod < 2:
        g.box(hp + Vector((0, -0.24, -0.05)), (0.1, 0.16, 0.1), "white")
        g.box((0, -0.48, 0.5), (0.2, 0.06, 0.26), "white")
        for sx in (-1, 1):
            g.box(hp + Vector((sx * 0.12, 0.06, 0.02)), (0.05, 0.1, 0.14), "cow_black")
        g.limb((0, 0.47, 0.6), (0, 0.72, 0.78), 0.05, 0.02, 4, "cow_black")
        g.box(hp + Vector((0, 0.16, -0.18)), (0.22, 0.2, 0.05), "red")


reg(dict(id="dog", file="Dog", cat="animal", tags=["dog"], seed=477, lods=3, fp=(0.4, 1.3), build=dog))
