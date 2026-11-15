"""Nature du pack d'environnement : conifères, arbres fruitiers, arbres morts, buissons, herbe, coquelicots,
rochers, bûches et souches, champignons, clôture. Tailles réelles en mètres ; « scale » de la fiche adapte
l'objet exporté à l'échelle attendue par le placement du jeu (TerrainStreamer)."""
import math
from mathutils import Vector, Matrix
import env_lib as E
from env_lib import reg


def lod(g, *values):
    return values[min(g.lod, len(values) - 1)]


# ----------------------------------------------------------------- conifères
def conifer(g, H, width, tiers, color="pine", trunk=0.26):
    """Conifère : tronc et étages en cônes qui se chevauchent ; dessous concave (ombré par le dégradé)."""
    n = lod(g, 7, 5, 4)
    T = lod(g, tiers, max(3, tiers - 1), 3)
    g.frustum((0, 0, 0), trunk, trunk * 0.35, H * 0.5, lod(g, 6, 5, 4), "bark", cap0=False)
    z0, z1 = H * 0.13, H * 0.7
    for k in range(T):
        t = k / max(T - 1, 1)
        zb = z0 + (z1 - z0) * t
        r = width * (1.0 - 0.72 * t) + 0.15
        h = (H - zb) if k == T - 1 else H * (0.36 - 0.14 * t)
        rot = g.R(0, 6.28)
        rim = [g.bm.verts.new(Vector((math.cos(rot + 6.2832 * i / n) * r * g.R(0.88, 1.1),
                                      math.sin(rot + 6.2832 * i / n) * r * g.R(0.88, 1.1),
                                      zb - 0.1 * r + g.R(-0.06, 0.06)))) for i in range(n)]
        inner = [g.bm.verts.new(Vector((math.cos(rot + 6.2832 * (i + 0.5) / n) * r * 0.32,
                                        math.sin(rot + 6.2832 * (i + 0.5) / n) * r * 0.32, zb + h * 0.1)))
                 for i in range(n)]
        apex = g.bm.verts.new(Vector((g.R(-0.05, 0.05), g.R(-0.05, 0.05), zb + h)))
        faces = []
        for i in range(n):
            j = (i + 1) % n
            faces.append(g.bm.faces.new((rim[i], rim[j], apex)))              # dessus
            faces.append(g.bm.faces.new((rim[j], rim[i], inner[i])))          # dessous
            faces.append(g.bm.faces.new((rim[i], inner[i - 1], inner[i])))
        faces.append(g.bm.faces.new(list(reversed(inner))))
        g.paint(faces, color)


# Hauteurs cibles (target_h) : hauteur hors sol des modèles Polytope remplacés, pour lesquels les échelles
# de placement de TerrainStreamer ont été réglées (sapin 8,6 m, fruitier 6,3 m, buisson 1,6 m...).
reg(dict(id="pine_a", file="Pine_A", group="pines", seed=11, target_h=8.7,
         build=lambda g: conifer(g, 9.2, 1.45, 5)))
reg(dict(id="pine_b", file="Pine_B", group="pines", seed=12, target_h=9.6,
         build=lambda g: conifer(g, 10.2, 1.3, 6)))
reg(dict(id="pine_c", file="Pine_C", group="pines", seed=13, target_h=7.6,
         build=lambda g: conifer(g, 8.0, 1.6, 4)))


def dead_pine(g):
    H = 10.5
    g.frustum((0, 0, 0), 0.25, 0.06, H, lod(g, 6, 5, 4), "dead", cap0=False)
    count = lod(g, 12, 7, 0)
    for k in range(count):
        z = H * g.R(0.28, 0.9)
        a = g.R(0, 6.28)
        r_trunk = 0.25 * (1 - z / H) + 0.05
        L = (1.0 - z / H) * 2.2 + 0.4
        p0 = Vector((math.cos(a) * r_trunk * 0.6, math.sin(a) * r_trunk * 0.6, z))
        p1 = p0 + Vector((math.cos(a) * L, math.sin(a) * L, -L * g.R(0.1, 0.45)))
        g.limb(p0, p1, 0.07, 0.015, 4, "dead", cap1=False)


reg(dict(id="pine_dead", file="DeadPine", group="deadTrees", build=dead_pine, seed=21, target_h=8.5))


# ----------------------------------------------------------------- arbres fruitiers
FRUIT_BLOBS = [((0.0, 0.0, 3.5), 1.55), ((1.1, 0.5, 3.0), 1.1), ((-1.0, 0.6, 3.1), 1.15),
               ((0.3, -1.1, 3.1), 1.15), ((-0.3, -0.2, 4.3), 1.05)]


def fruit_tree(g, fruit=None):
    # tronc et trois charpentières
    g.frustum((0, 0, 0), 0.2, 0.15, 1.8, lod(g, 6, 5, 4), "bark", cap0=False)
    if g.lod < 2:
        for c, _ in FRUIT_BLOBS[1:4]:
            g.limb((0, 0, 1.6), (c[0] * 0.7, c[1] * 0.7, c[2] - 0.3), 0.13, 0.06, 5, "bark")
    blobs = FRUIT_BLOBS if g.lod == 0 else FRUIT_BLOBS[:4] if g.lod == 1 else [((0, 0, 3.4), 1.9)]
    for k, (c, r) in enumerate(blobs):
        f = g.ico(c, r, lod(g, 2, 1, 1), "leaf" if k % 2 == 0 else "leaf_light", scale=(1, 1, 0.85))
        g.jitter(f, r * 0.1)
    if fruit and g.lod < 2:
        for k in range(lod(g, 16, 8)):
            c, r = FRUIT_BLOBS[k % len(FRUIT_BLOBS)]
            a, e = g.R(0, 6.28), g.R(-0.5, 0.6)
            d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e) * 0.85))
            g.octa(Vector(c) + d * r * 0.98, 0.12 if fruit != "plum" else 0.1, fruit,
                   scale=(1, 1, 1.3 if fruit == "pear" else 1))


reg(dict(id="fruit_green", file="FruitTree", group="fruitTrees", build=lambda g: fruit_tree(g), seed=31,
         target_h=6.3))
reg(dict(id="fruit_apple", file="AppleTree", group="fruitTrees", build=lambda g: fruit_tree(g, "apple"), seed=32,
         target_h=6.3))
reg(dict(id="fruit_pear", file="PearTree", group="fruitTrees", build=lambda g: fruit_tree(g, "pear"), seed=33,
         target_h=6.3))
reg(dict(id="fruit_plum", file="PlumTree", group="fruitTrees", build=lambda g: fruit_tree(g, "plum"), seed=34,
         target_h=6.3))


def dead_fruit_tree(g):
    g.frustum((0, 0, 0), 0.2, 0.14, 1.8, lod(g, 6, 5, 4), "dead", cap0=False)
    for k in range(3):
        a = 2.1 * k + g.R(-0.3, 0.3)
        tip = Vector((math.cos(a) * 1.3, math.sin(a) * 1.3, 3.6 + g.R(-0.3, 0.4)))
        g.limb((0, 0, 1.6), tip, 0.13, 0.05, lod(g, 5, 4, 3), "dead")
        if g.lod < 2:
            for s in (-1, 1):
                b = a + s * 0.6
                end = tip + Vector((math.cos(b) * 0.8, math.sin(b) * 0.8, g.R(0.3, 0.8)))
                g.limb(tip - (tip - Vector((0, 0, 1.6))) * 0.3, end, 0.06, 0.015, 4, "dead", cap1=False)


reg(dict(id="fruit_dead", file="DeadFruitTree", group="deadTrees", build=dead_fruit_tree, seed=35, target_h=5.2))


# ----------------------------------------------------------------- buissons
def shrub(g, blobs):
    use = blobs if g.lod == 0 else blobs[:3] if g.lod == 1 else [((0, 0, 0.5), 0.75)]
    for k, (c, r) in enumerate(use):
        f = g.ico(c, r, lod(g, 2, 1, 1), "leaf" if k % 2 == 0 else "leaf_light", scale=(1, 1, 1.05))
        g.jitter(f, r * 0.12)


reg(dict(id="shrub_a", file="Shrub_A", group="shrubs", seed=41, target_h=1.6,
         build=lambda g: shrub(g, [((0, 0, 0.5), 0.6), ((0.45, 0.2, 0.4), 0.45), ((-0.4, 0.25, 0.38), 0.45),
                                   ((0.05, -0.45, 0.4), 0.45), ((0.0, 0.1, 0.85), 0.4)])))
reg(dict(id="shrub_b", file="Shrub_B", group="shrubs", seed=42, target_h=1.35,
         build=lambda g: shrub(g, [((0, 0, 0.42), 0.5), ((0.5, -0.1, 0.35), 0.4), ((-0.35, -0.3, 0.33), 0.38),
                                   ((-0.2, 0.4, 0.35), 0.35)])))


def dead_shrub(g):
    for k in range(lod(g, 11, 7, 4)):
        a = g.R(0, 6.28)
        L = g.R(0.7, 1.2)
        tip = Vector((math.cos(a) * L * 0.55, math.sin(a) * L * 0.55, L * 0.85))
        g.limb((0, 0, 0), tip, 0.035, 0.008, 3, "dead", cap1=False)


reg(dict(id="shrub_dead", file="DeadShrub", group="shrubs", build=dead_shrub, seed=43, target_h=1.55))


# ----------------------------------------------------------------- herbe et fleurs
def grass(g, color="grass", n=None, spread=0.3):
    count = n or lod(g, 18, 11, 6)
    for k in range(count):
        a = 6.2832 * k / count + g.R(-0.3, 0.3)
        tilt = g.R(0.1, 0.45)
        d = Vector((math.cos(a) * tilt, math.sin(a) * tilt, 1.0))
        rr = spread * math.sqrt(g.R(0.05, 1.0))
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        tone = "dry" if color == "grass" and k % 4 == 0 else color     # un brin sur quatre sec
        g.blade(base, d, g.R(0.24, 0.42), g.R(0.07, 0.1), tone, bend=g.R(0.2, 0.4), segs=2 if g.lod == 0 else 1)


reg(dict(id="grass", file="GrassTuft", group="grass", build=lambda g: grass(g), seed=51, target_h=0.46))


def poppies(g):
    for k in range(lod(g, 4, 3, 2)):
        a = g.R(0, 6.28)
        base = Vector((math.cos(a) * g.R(0.03, 0.12), math.sin(a) * g.R(0.03, 0.12), 0))
        top = base + Vector((g.R(-0.06, 0.06), g.R(-0.06, 0.06), g.R(0.32, 0.48)))
        g.limb(base, top, 0.012, 0.009, 3, "grass", cap1=False)
        g.frustum(top - Vector((0, 0, 0.01)), 0.025, 0.065, 0.05, 5, "poppy", rot=g.R(0, 6.28), cap0=False)
        g.octa(top + Vector((0, 0, 0.04)), 0.022, "center", scale=(1, 1, 0.45))
    if g.lod < 2:
        grass(g, n=lod(g, 5, 3), spread=0.15)


reg(dict(id="poppies", file="Poppies", group="flowers", build=poppies, seed=52, target_h=0.78))


# ----------------------------------------------------------------- rochers
def rock(g, r=0.55, scale=(1.25, 0.95, 0.68), color="rock", bury=0.08):
    f = g.ico((0, 0, 0), r, lod(g, 2, 1, 1), color, scale=scale)
    g.jitter(f, r * 0.1)
    floor = -r * scale[2] * 0.35
    for v in {v for x in f for v in x.verts}:
        v.co.z = max(v.co.z, floor)
    g.transform(f, Matrix.Translation((0, 0, -floor - bury)))


def rock_pile(g):
    stones = [((0, 0), 0.2), ((0.3, 0.1), 0.15), ((-0.25, 0.18), 0.14), ((0.08, -0.28), 0.16), ((-0.12, -0.05), 0.12)]
    for k, ((x, y), r) in enumerate(stones[:lod(g, 5, 4, 3)]):
        f = g.ico((x, y, r * 0.35), r, 1, "rock_light" if k % 2 == 0 else "rock",
                  scale=(1.1, 0.9, 0.6))
        g.jitter(f, r * 0.1)


def menhir(g):
    rock(g, r=0.7, scale=(0.95, 0.75, 1.55), bury=0.2)
    top = max(v.co.z for v in g.bm.verts)
    for v in g.bm.verts:                                   # sommet émoussé
        if v.co.z > top * 0.86:
            v.co.z = top * 0.86 + (v.co.z - top * 0.86) * 0.35


reg(dict(id="rock", file="Rock", group="smallRocks", build=lambda g: rock(g), seed=61, target_h=0.22))
reg(dict(id="rock_pile", file="RockPile", group="smallRocks", build=rock_pile, seed=62, target_h=0.2))
reg(dict(id="menhir", file="Menhir", group="bigRocks", build=menhir, seed=63, target_h=2.7))


# ----------------------------------------------------------------- bûches et souches
def log(g, p0, p1, r, n):
    faces = g.limb(p0, p1, r, r * 0.95, n, "bark", cap0=True, cap1=True)
    g.paint([f for f in faces if len(f.verts) == n], "wood_cut")


def pine_logs(g):
    n = lod(g, 7, 6, 5)
    for (x0, y0), (x1, y1), r in (((-0.5, -1.6), (-0.3, 1.5), 0.21), ((0.1, -1.5), (0.0, 1.6), 0.2),
                                  ((0.6, -1.3), (1.0, 1.4), 0.19)):
        log(g, (x0, y0, r), (x1, y1, r), r, n)


def stump(g, r=0.34, h=0.45, roots=4):
    n = lod(g, 8, 6, 5)
    faces = g.frustum((0, 0, 0), r, r * 0.85, h, n, "bark", cap0=False)
    g.paint([f for f in faces if len(f.verts) == n], "wood_cut")
    if g.lod < 2:
        for k in range(roots):
            a = 6.2832 * k / roots + g.R(-0.3, 0.3)
            g.limb((math.cos(a) * r * 0.5, math.sin(a) * r * 0.5, h * 0.35),
                   (math.cos(a) * r * 1.7, math.sin(a) * r * 1.7, -0.02), r * 0.35, 0.03, 4, "bark", cap1=False)


def fruit_logs(g):
    n = lod(g, 6, 5, 4)
    log(g, (-0.15, -1.1, 0.14), (0.2, 1.1, 0.14), 0.14, n)
    log(g, (0.25, -1.0, 0.13), (0.3, 1.0, 0.13), 0.13, n)


reg(dict(id="pine_logs", file="PineLogs", group="logsAndStumps", build=pine_logs, seed=71, target_h=0.43))
reg(dict(id="pine_stump", file="PineStump", group="logsAndStumps", build=lambda g: stump(g, 0.3, 0.6), seed=72))
reg(dict(id="fruit_logs", file="FruitLogs", group="logsAndStumps", build=fruit_logs, seed=73, target_h=0.3))
reg(dict(id="fruit_stump", file="FruitStump", group="logsAndStumps", build=lambda g: stump(g, 0.26, 0.55, 3),
         seed=74))


# ----------------------------------------------------------------- champignons
def mushrooms(g):
    for (x, y, s) in ((0, 0, 1.0), (0.12, 0.07, 0.7))[:lod(g, 2, 2, 1)]:
        g.frustum((x, y, 0), 0.028 * s, 0.022 * s, 0.12 * s, 6, "stem", cap0=False)
        g.frustum((x, y, 0.105 * s), 0.09 * s, 0.075 * s, 0.02 * s, 8, "stem", cap1=False)     # lamelles
        cap = g.ico((x, y, 0.125 * s), 0.09 * s, 1, "cap", scale=(1, 1, 0.55))
        for v in {v for f in cap for v in f.verts}:
            v.co.z = max(v.co.z, 0.118 * s)


reg(dict(id="mushrooms", file="Mushrooms", group="mushrooms", build=mushrooms, seed=81, target_h=0.28))


# ----------------------------------------------------------------- clôture (tronçon posé bout à bout)
def fence(g, length=2.2):
    """Tronçon de 2,2 m le long de -X (Blender) depuis l'origine : un poteau, deux lisses (le poteau suivant ferme)."""
    # (Blender -X = Unity +X : le tronçon s'étend vers l'avant du placement, comme le modèle d'origine)
    g.box((0.0, 0, 0.6), (0.12, 0.12, 1.4), "wood")
    g.frustum((0.0, 0, 1.3), 0.085, 0.0, 0.12, 4, "wood", rot=math.pi / 4)
    for z in (0.5, 1.0):
        g.box((-length / 2, 0, z), (length, 0.05, 0.13), "wood")


reg(dict(id="fence", file="Fence", group="fence", build=fence, seed=91, lods=1))

NATURE = list(E.ITEMS)
