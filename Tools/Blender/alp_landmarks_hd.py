"""Pack alpin v2 : compléments détaillés (niveau hd) des services et monuments dont le modèle d'origine ne passe pas
par les briques du kit : station-service, supermarché, garage, pompiers, scierie, tour en ruine, monument, fontaine,
kiosque, pylône radio, croix de sommet. Chaque fonction AJOUTE des détails au modèle existant (g.hd seulement)."""
import math, random
from mathutils import Vector, Matrix
import alp_lib as A
import alp_kit as K
from alp_kit import Fac
from alp_props_hd import lathe, bolts, flowers, ring_pts
from alp_nature_hd import rock, blob
from alp_signs import digits


def extra(iid):
    """Décorateur : la fonction ajoute les détails hd après le constructeur d'origine."""
    def deco(fn):
        old = A.ITEMS[iid]["build"]

        def build(g):
            old(g)
            if g.hd:
                fn(g)
        A.ITEMS[iid]["build"] = build
        return fn
    return deco


def hose(g, p0, p1, sag=0.5, r=0.018, color="black"):
    p0, p1 = Vector(p0), Vector(p1)
    pts = [p0.lerp(p1, t) - Vector((0, 0, sag * math.sin(math.pi * t))) for t in (0, 0.2, 0.4, 0.6, 0.8, 1.0)]
    g.tube(pts, [r] * 6, 5, color, cap_end=False)


# ----------------------------------------------------------------- station-service
@extra("gasstation")
def gasstation(g):
    for x in (-4.0, 4.0):
        # pilier : chasse-roues rayé, descente d'eau, numéro d'îlot
        for k in range(4):
            g.box((x, -2.5, 0.16 + k * 0.16), (0.72 - 0.0, 0.72, 0.15), "sign_yellow" if k % 2 == 0 else "black")
        g.tube([(x + 0.3, -2.5, 4.95), (x + 0.3, -2.5, 0.8)], [0.04, 0.04], 6, "metal")
        for sy in (-1, 1):
            Fac(g, 'y', -2.5 + sy * 0.25, sy).slab(x, 3.6, 0.4, 0.4, 0.02, "sign_red")
            Fac(g, 'y', -2.5 + sy * 0.25, sy).slab(x, 3.6, 0.3, 0.3, 0.03, "white")
        g.box((x, -2.5, 0.2), (4.3, 1.4, 0.02), "sign_yellow")                              # liseré de l'îlot
        g.box((x, -2.5, 0.19), (4.2, 1.3, 0.04), "concrete")
        lathe(g, x, -3.0, [(0.18, 0.16, "awning_green"), (0.9, 0.2, "awning_green"), (0.93, 0.21, "metal_dark"), (0.96, 0.21, "metal_dark")], 10, cap=False)
        g.box((x, -1.95, 0.75), (0.3, 0.2, 0.4), "sign_blue")                               # essuie-mains, seau
        lathe(g, x + 0.45, -1.95, [(0.18, 0.12, "sign_blue"), (0.42, 0.15, "sign_blue")], 8, cap=False)
        for dx in (-1.2, 1.2):                        # pompes : afficheur, pistolets, flexibles, enseigne
            px = x + dx
            g.box((px, -2.5, 0.22), (0.7, 1.0, 0.08), "metal_dark")
            g.box((px, -2.5, 1.86), (0.66, 0.96, 0.12), "sign_red")
            for sx in (-1, 1):
                f = Fac(g, 'x', px + sx * 0.3, sx)
                f.slab(-2.5, 1.36, 0.6, 0.42, 0.03, "black")
                for r_ in range(3):
                    f.slab(-2.5 + 0.08, 1.48 - r_ * 0.12, 0.34, 0.07, 0.036, "sign_white" if r_ < 2 else "sign_yellow")
                f.slab(-2.5, 0.95, 0.62, 0.3, 0.02, "metal")
                for k, c in enumerate(("sign_green", "black", "sign_yellow")):
                    yy = -2.5 - 0.24 + k * 0.24
                    f.slab(yy, 0.82, 0.12, 0.32, 0.07, "metal_dark")
                    g.box(f.P(yy, 0.1, 0.92), (0.05, 0.07, 0.2), c)
                    hose(g, f.P(yy, 0.08, 0.8), f.P(yy, 0.02, 1.7), 0.0, 0.016)
                    hose(g, f.P(yy, 0.02, 1.7), f.P(yy + 0.02, 0.2, 0.5), -0.25, 0.016)
    # auvent : bandeau lumineux, logo, projecteurs en sous-face
    for sy in (-1, 1):
        f = Fac(g, 'y', -2.5 + sy * 4.05, sy)
        f.slab(0, 5.3, 13.6, 0.1, 0.03, "lamp")
        f.slab(sy * 4.5, 5.32, 1.4, 0.5, 0.05, "white")
        f.poly([(sy * 4.5 + math.cos(6.2832 * k / 12) * 0.2, 5.32 + math.sin(6.2832 * k / 12) * 0.2) for k in range(12)], "sign_red", 0.06)
    for x in (-5.5, -1.8, 1.8, 5.5):
        for y in (-4.6, -0.4):
            g.box((x, y, 4.93), (0.7, 0.7, 0.05), "metal_dark")
            g.box((x, y, 4.9), (0.56, 0.56, 0.03), "lamp")
    # boutique : menuiseries, étalage, enseigne, cage à bouteilles, présentoir, climatiseur
    f = Fac(g, 'y', 2.0, -1)
    for k in range(6):
        f.slab(-4.4 + k * 1.28, 1.6, 0.07, 2.4, 0.06, "metal")
    for z in (0.42, 2.78, 2.2):
        f.slab(-1.2, z, 6.5, 0.07, 0.06, "metal")
    f.slab(-1.2, 0.2, 6.5, 0.4, 0.05, "concrete")
    for k in range(5):
        f.slab(-3.8 + k * 1.28, 1.0 + 0.2 * (k % 2), 0.8, 0.9, 0.012, ("sign_yellow", "sign_blue", "sign_red", "sign_green", "white")[k], out=0.035)
    f.sign(-0.4, 3.2, 8.6, 0.55, "sign_red", "white")
    g.box((-4.3, 1.55, 0.95), (1.2, 0.8, 1.9), "metal_dark")                                # cage à bouteilles de gaz
    for i in range(3):
        for j in range(2):
            lathe(g, -4.65 + i * 0.36, 1.4 + j * 0.34, [(0.1 + 0.9 * 0, 0.14, "sign_blue" if (i + j) % 2 else "sign_red"),
                                                      (0.62, 0.14, "sign_blue" if (i + j) % 2 else "sign_red"), (0.72, 0.05, "metal"), (0.78, 0.05, "metal")], 8)
    for k in range(5):
        g.box((-4.3, 1.14, 0.15 + k * 0.4), (1.22, 0.02, 0.03), "metal")
    g.box((1.9, 1.75, 0.6), (0.9, 0.4, 1.2), "wood")                                        # présentoir (bois de chauffage, lave-glace)
    for k in range(4):
        g.box((1.6 + k * 0.2, 1.7, 1.3), (0.16, 0.3, 0.25), "sign_blue" if k % 2 else "sign_yellow")
    g.box((4.2, 5.0, 3.95), (1.4, 1.0, 0.6), "metal")
    g.box((4.2, 4.49, 3.95), (1.0, 0.02, 0.4), "metal_dark")
    # totem : prix en chiffres à segments, logo
    tf = Fac(g, 'y', -6.24, -1)
    for k, (txt, c) in enumerate((("179", "sign_white"), ("185", "sign_yellow"), ("162", "sign_green"))):
        digits(tf, txt, -8.4, 5.05 - k * 0.42, 0.24, c, 0.01)
        tf.slab(-8.98, 5.05 - k * 0.42, 0.12, 0.2, 0.01, c)
    tf.slab(-8.5, 6.5, 1.5, 0.05, 0.03, "lamp")
    g.box((-8.5, -6.0, 0.25), (0.9, 0.7, 0.5), "concrete")
    # gonfleur : cadran, flexible
    g.box((6.5, 1.7, 1.05), (0.54, 0.54, 0.1), "metal_dark")
    Fac(g, 'y', 1.45, -1).poly([(6.5 + math.cos(6.2832 * k / 12) * 0.15, 0.7 + math.sin(6.2832 * k / 12) * 0.15) for k in range(12)], "white", 0.01)
    hose(g, (6.75, 1.6, 0.8), (6.9, 1.2, 0.05), 0.2, 0.014)
    for k in range(3):                                # fûts : cerclages
        for z in (0.3, 0.6):
            lathe(g, 7.2, 2.6 + k * 0.7, [(z, 0.31, "metal_dark"), (z + 0.03, 0.31, "metal_dark")], 12, cap=False)


# ----------------------------------------------------------------- supermarché
def trolley(g, x, y, rotz=0.0):
    m = g.mark()
    c = "metal"
    for sx in (-1, 1):
        g.beam((sx * 0.25, -0.4, 0.55), (sx * 0.28, 0.4, 0.6), 0.015, c)
        g.beam((sx * 0.25, -0.4, 0.95), (sx * 0.28, 0.4, 1.0), 0.015, c)
        g.beam((sx * 0.25, -0.4, 0.55), (sx * 0.25, -0.4, 0.95), 0.015, c)
        g.beam((sx * 0.28, 0.4, 0.6), (sx * 0.28, 0.4, 1.0), 0.015, c)
        g.beam((sx * 0.28, 0.45, 0.2), (sx * 0.28, 0.45, 1.08), 0.02, c)
        g.beam((sx * 0.22, -0.4, 0.2), (sx * 0.28, 0.45, 0.2), 0.02, c)
        for yy in (-0.4, 0.42):
            lathe(g, sx * 0.24, yy, [(0.0, 0.05, "rubber"), (0.1, 0.05, "rubber")], 6)
        for k in range(3):
            g.beam((sx * 0.26, -0.2 + k * 0.2, 0.56), (sx * 0.26, -0.2 + k * 0.2, 0.97), 0.008, c)
    g.box((0, 0, 0.56), (0.5, 0.82, 0.012), c)
    g.box((0, -0.4, 0.75), (0.5, 0.012, 0.4), c)
    g.box((0, 0.48, 1.08), (0.6, 0.035, 0.035), "sign_red")
    g.move(g.since(m), (x, y, 0), rotz)


@extra("supermarket")
def supermarket(g):
    W, D, H = 24.0, 16.0, 5.6
    f = Fac(g, 'y', -D / 2, -1)
    for k in range(11):                               # menuiseries du vitrage, soubassement, affiches promotionnelles
        f.slab(-10.0 + k * 1.5, 1.7, 0.08, 2.8, 0.13, "metal")
    for z in (0.33, 3.07, 2.45):
        f.slab(-2.5, z, 15.1, 0.08, 0.07, "metal")
    f.slab(-2.5, 0.15, 15.1, 0.3, 0.09, "concrete_dark")
    for k in range(6):
        c = ("sign_red", "sign_yellow", "white", "sign_green", "sign_red", "sign_yellow")[k]
        f.slab(-8.5 + k * 3.0 - (0.75 if k % 2 else 0), 1.5, 1.1, 1.5, 0.04, c, out=0.04)
        f.slab(-8.5 + k * 3.0 - (0.75 if k % 2 else 0), 1.75, 0.8, 0.5, 0.06, "black" if c != "black" else "white", out=0.04)
    for k in range(7):                                # étals intérieurs devinés derrière la vitre
        f.slab(-9.2 + k * 2.1, 0.8, 1.4, 0.7, 0.015, ("apple", "leaf", "orange", "pear", "leaf_light", "apple", "yellow")[k], out=0.045)
    for x in (-7.5, -2.5, 2.5):
        g.box((x, -D / 2 - 0.25, 3.4), (0.5, 0.3, 0.14), "metal_dark")                      # appliques
        g.box((x, -D / 2 - 0.25, 3.32), (0.4, 0.22, 0.03), "lamp")
    # entrée : sas vitré, capteur, paillasson ; potelets ; chariots sous abri
    g.box((8.5, -D / 2 - 0.12, 2.75), (0.3, 0.12, 0.1), "black")
    g.box((8.5, -D / 2 - 0.8, 0.01), (2.6, 1.4, 0.02), "rubber")
    for sx in (-1, 1):
        g.tube([(8.5 + sx * 2.0, -D / 2 - 2.6, -0.5), (8.5 + sx * 2.0, -D / 2 - 2.6, 3.0)], [0.07, 0.07], 8, "metal")
        g.box((8.5 + sx * 2.0, -D / 2 - 1.3, 3.0), (0.08, 2.6, 0.12), "metal")
    for k in range(6):
        lathe(g, -4.0 + k * 2.0, -D / 2 - 3.2, [(-0.5, 0.08, "metal"), (0.8, 0.08, "metal"), (0.82, 0.09, "sign_yellow"), (0.92, 0.09, "sign_yellow"),
                                              (0.94, 0.06, "metal"), (0.98, 0.0, "metal")], 10)
    for k in range(5):
        trolley(g, -10.2 + k * 0.28, -D / 2 - 2.0, math.pi / 2)
    trolley(g, -6.2, -D / 2 - 2.6, 0.5)
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.tube([(-9.0 + sx * 1.9, -D / 2 - 2.0 + sy * 1.0, -0.5), (-9.0 + sx * 1.9, -D / 2 - 2.0 + sy * 1.0, 2.2)], [0.04, 0.04], 6, "metal")
    g.box((-9.0, -D / 2 - 3.1, 1.9), (1.2, 0.04, 0.5), "sign_blue")
    g.box((-9.0, -D / 2 - 3.13, 1.9), (0.9, 0.02, 0.3), "white")
    # toiture : acrotère, lanterneaux, groupes froids, gaines ; descentes d'eau ; quai de livraison
    for s in (-1, 1):
        g.box((0, s * (D / 2 + 0.12), H + 0.53), (W + 0.4, 0.2, 0.06), "roof_metal")
        g.box((s * (W / 2 + 0.12), 0, H + 0.53), (0.2, D + 0.4, 0.06), "roof_metal")
        g.tube([(s * (W / 2 - 0.3), -D / 2 - 0.08, H), (s * (W / 2 - 0.3), -D / 2 - 0.08, 0.2)], [0.06, 0.06], 8, "roof_metal")
        g.tube([(s * (W / 2 - 0.3), D / 2 + 0.08, H), (s * (W / 2 - 0.3), D / 2 + 0.08, 0.2)], [0.06, 0.06], 8, "roof_metal")
    for x, y in ((-6, 2), (3, -3), (7, 4)):
        for k in range(4):
            g.box((x, y - 0.81, H + 0.7 + k * 0.18), (1.9, 0.02, 0.06), "metal_dark")
        lathe(g, x, y, [(H + 1.5, 0.5, "metal_dark"), (H + 1.62, 0.5, "metal_dark")], 10)
        g.box((x + 1.6, y, H + 0.75), (1.2, 0.4, 0.4), "metal")
    for k in range(4):
        g.box((-9.0 + k * 3.5, 0.0, H + 0.75), (1.4, 1.4, 0.3), "glass_blue")
        g.box((-9.0 + k * 3.5, 0.0, H + 0.6), (1.6, 1.6, 0.14), "concrete")
    bf = Fac(g, 'y', D / 2, 1)
    for k in range(8):
        bf.slab(-6.0, 0.2 + k * 0.4, 2.9, 0.34, 0.03 + 0.02 * (k % 2), "metal" if k % 2 else "roof_metal", out=0.1)
    g.box((-6.0, D / 2 + 1.0, 0.5), (4.5, 2.0, 1.0), "concrete")
    for sx in (-1, 1):
        g.box((-6.0 + sx * 1.7, D / 2 + 2.05, 0.75), (0.3, 0.12, 0.5), "rubber")
    for k in range(3):                                # palettes et conteneurs
        g.box((2.0 + k * 1.4, D / 2 + 0.9, 0.08), (1.2, 1.0, 0.14), "wood_cut")
        g.box((2.0 + k * 1.4, D / 2 + 0.9, 0.6 + 0.1 * k), (1.1, 0.9, 0.9 + 0.2 * k), ("wood", "awning_cream", "sign_blue")[k])
    g.box((9.0, D / 2 + 1.0, 0.7), (1.8, 1.2, 1.3), "sign_green")
    g.box((9.0, D / 2 + 1.0, 1.4), (1.9, 1.3, 0.1), "metal_dark")


# ----------------------------------------------------------------- garage
@extra("garage")
def garage(g):
    W, D, H = 14.0, 10.0, 5.0
    f = Fac(g, 'y', -D / 2, -1)
    for k in range(6):                                # porte sectionnelle : panneaux nervurés
        f.slab(-4.2, 0.3 + k * 0.6, 3.36, 0.03, 0.02, "metal", out=0.1)
    for k in range(4):
        f.slab(-5.45 + k * 0.84, 2.38, 0.62, 0.36, 0.02, "window", out=0.12)
    # atelier ouvert : pont élévateur et voiture levée, établi, servante
    for sx in (-1, 1):
        f.box(0.1 + sx * 1.25, 1.5, 0.22, 3.0, 0.3, "sign_yellow", out=0.06)
        f.box(0.1 + sx * 0.95, 1.3, 0.5, 0.1, 0.5, "sign_yellow", out=0.1)
    f.prism([(0.1, 1.45), (0.55, 1.45), (0.55, 1.75), (0.45, 1.8), (0.1, 1.8)], -0.9, 1.1, "sign_red")
    f.prism([(0.12, 1.8), (0.5, 1.8), (0.44, 2.2), (0.12, 2.2)], -0.5, 0.75, "sign_red")
    f.slab(0.12, 2.0, 1.0, 0.3, 0.012, "window", out=0.5)
    for u in (-0.55, 0.75):
        m = g.mark()
        g.frustum((0, 0, -0.09), 0.3, 0.3, 0.18, 12, "rubber", auto=False)
        g.frustum((0, 0, 0.09), 0.17, 0.17, 0.01, 10, "metal", auto=False)
        g.transform(g.since(m), Matrix.Translation((u, -D / 2 - 0.56, 1.42)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    g.box((-1.3, -D / 2 - 0.5, 0.5), (0.6, 0.45, 1.0), "sign_red")                           # servante d'atelier
    for k in range(4):
        g.box((-1.3, -D / 2 - 0.735, 0.2 + k * 0.22), (0.5, 0.012, 0.16), "metal")
    g.box((7.4, -D / 2 - 0.4, 0.45), (0.0, 0.0, 0.0), "metal") if False else None
    # enseigne, appliques, pneus à jantes, fûts cerclés, compresseur
    for x in (-4.2, 0.1, 4.3):
        g.box((x, -D / 2 - 0.2, 4.1), (0.5, 0.25, 0.12), "metal_dark")
        g.box((x, -D / 2 - 0.2, 4.03), (0.4, 0.18, 0.03), "lamp")
    for k in range(4):
        lathe(g, W / 2 + 0.7, -3.0, [(k * 0.24 + 0.02, 0.2, "metal"), (k * 0.24 + 0.2, 0.2, "metal")], 10, cap=(k == 3))
        g.tube(ring_pts((W / 2 + 0.7, -3.0, k * 0.24 + 0.12), 0.3, 12, 'z'), [0.085] * 13, 5, "rubber", cap_end=False)
    for k in range(3):
        for z in (0.28, 0.6):
            lathe(g, W / 2 + 0.6, -1.4 + k * 0.7, [(z, 0.31, "metal_dark"), (z + 0.03, 0.31, "metal_dark")], 12, cap=False)
    m = g.mark()
    g.frustum((0, 0, -0.5), 0.28, 0.28, 1.0, 12, "sign_blue", auto=False)
    g.transform(g.since(m), Matrix.Translation((-W / 2 - 0.6, -2.0, 0.5)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    g.box((-W / 2 - 0.6, -2.0, 0.95), (0.4, 0.5, 0.3), "metal_dark")
    for sy in (-1, 1):
        g.box((-W / 2 - 0.6, -2.0 + sy * 0.35, 0.1), (0.3, 0.06, 0.2), "metal_dark")
    hd_h = [(-W / 2 - 0.35, -2.5, 0.9), (-W / 2 - 0.2, -3.6, 0.3), (-W / 2 - 0.3, -4.4, 0.05)]
    g.tube(hd_h, [0.015] * 3, 4, "sign_red", cap_end=False)


# ----------------------------------------------------------------- pompiers
@extra("firestation")
def firestation(g):
    W, D, H = 16.0, 11.0, 7.4
    f = Fac(g, 'y', -D / 2, -1)
    for u in (-5.6, -0.6):                            # portes sectionnelles : panneaux, hublots, poignée
        for k in range(1, 6):
            f.slab(u, k * 0.65, 3.6, 0.035, 0.02, "sign_white", out=0.1)
        for k in range(4):
            f.slab(u - 1.3 + k * 0.87, 2.6, 0.06, 0.6, 0.02, "sign_red", out=0.12)
        f.slab(u, 0.35, 0.5, 0.06, 0.04, "metal_dark", out=0.1)
        g.box((u, -D / 2 - 0.3, 4.15), (0.5, 0.3, 0.12), "metal_dark")
        g.box((u, -D / 2 - 0.3, 4.08), (0.4, 0.22, 0.03), "lamp")
        g.box((u, -D / 2 - 2.5, 0.006), (3.4, 4.6, 0.012), "concrete")                       # tablier bétonné
        for sx in (-1, 1):
            g.box((u + sx * 1.7, -D / 2 - 2.5, 0.014), (0.1, 4.6, 0.012), "sign_yellow")
    # écusson, lettrage, sirène, cloche d'alarme, colonne sèche, banc
    f.poly([(-3.5, 5.3), (-2.7, 5.3), (-2.7, 4.95), (-3.1, 4.6), (-3.5, 4.95)], "sign_red", 0.1)
    f.poly([(-3.4, 5.22), (-2.8, 5.22), (-2.8, 4.97), (-3.1, 4.72), (-3.4, 4.97)], "brass", 0.11)
    for k in range(6):
        f.slab(-1.9 + k * 0.42, 4.45, 0.28, 0.22, 0.02, "sign_red", out=0.1)
    lathe(g, -6.0, 0.0, [(H + 1.0, 0.1, "metal"), (H + 2.2, 0.08, "metal"), (H + 2.25, 0.3, "sign_red"), (H + 2.5, 0.42, "sign_red"),
                         (H + 2.56, 0.44, "metal_dark"), (H + 2.7, 0.2, "sign_red"), (H + 2.8, 0.0, "sign_red")], 12)
    g.box((4.2, -D / 2 - 0.12, 2.9), (0.3, 0.2, 0.3), "sign_red")
    g.octa((4.2, -D / 2 - 0.26, 2.9), 0.12, "brass")
    for k in range(2):
        g.tube([(7.5, -D / 2 - 0.1, 0.5 + k * 0.4), (7.5, -D / 2 - 0.3, 0.5 + k * 0.4)], [0.06, 0.06], 8, "sign_red", cap_start=True)
    g.box((7.5, -D / 2 - 0.05, 0.75), (0.5, 0.06, 0.9), "sign_red")
    # tour de séchage : persiennes, garde-corps, antenne
    tx, ty = W / 2 - 1.7, D / 2 - 1.7
    for tf in (Fac(g, 'y', ty - 1.6, -1), Fac(g, 'x', tx + 1.6, 1), Fac(g, 'y', ty + 1.6, 1), Fac(g, 'x', tx - 1.6, -1)):
        c = tx if tf.axis == 'y' else ty
        for z in (9.5, 12.5):
            for k in range(6):
                tf.face3([(c - 0.8, 0.03, z - 0.75 + k * 0.26 + 0.2), (c + 0.8, 0.03, z - 0.75 + k * 0.26 + 0.2),
                          (c + 0.8, 0.12, z - 0.75 + k * 0.26), (c - 0.8, 0.12, z - 0.75 + k * 0.26)], "metal")
            for s in (-1, 1):
                tf.slab(c + s * 0.86, z, 0.1, 1.8, 0.14, "plaster_white")
                tf.slab(c, z + s * 0.86, 1.82, 0.1, 0.14, "plaster_white")
        tf.slab(c, 14.6, 3.3, 0.25, 0.08, "sign_red")
    g.tube([(tx, ty, 16.6), (tx, ty, 19.5)], [0.03, 0.01], 5, "metal")
    K.flag(g, -W / 2 - 1.2, -D / 2 - 1.5, -0.5, 8.0, "red", (1.5, 1.0))


# ----------------------------------------------------------------- scierie
@extra("sawmill")
def sawmill(g):
    W, D, H = 16.0, 8.0, 4.6
    xs = [-W / 2 - 2.6 + (W + 5.2) * (k + 0.5) / 4 for k in range(4)]
    for x in xs:                                      # fermes : entraits, poinçons, aisseliers
        g.beam((x, -D / 2, H - 0.1), (x, D / 2, H - 0.1), 0.2, "wood", 0.22)
        g.beam((x, 0, H), (x, 0, H + D / 2 * math.tan(math.radians(20)) - 0.1), 0.16, "wood")
        for sy in (-1, 1):
            g.beam((x, sy * D / 2, H - 1.2), (x, sy * (D / 2 - 1.1), H - 0.1), 0.14, "wood")
            g.beam((x, sy * D / 2, H - 0.05), (x, 0, H + D / 2 * math.tan(math.radians(20)) - 0.08), 0.14, "wood", 0.18)
            g.box((x, sy * D / 2, 0.2), (0.5, 0.5, 0.4), "concrete")
    for sy in (-1, 1):
        g.beam((xs[0], sy * D / 2, H - 0.02), (xs[-1], sy * D / 2, H - 0.02), 0.2, "wood", 0.22)
        for a, b in zip(xs, xs[1:]):
            g.beam((a, sy * D / 2, H - 1.1), (a + 1.0, sy * D / 2, H - 0.1), 0.12, "wood")
            g.beam((b, sy * D / 2, H - 1.1), (b - 1.0, sy * D / 2, H - 0.1), 0.12, "wood")
    for k in range(13):                               # bardage du fond : planches et couvre-joints
        g.box((-W / 2 + 0.6 + k * 1.24, D / 2 - 0.09, H * 0.5), (0.1, 0.05, H), "wood_dark")
    # scie : volant à dents, chariot sur rails, grume griffée, bâti, transmission
    pts = []
    for k in range(40):
        a = 6.2832 * k / 40
        r = 0.95 if k % 2 == 0 else 0.84
        pts.append((2.6 + math.cos(a) * r, 1.35 + math.sin(a) * r))
    g.prism(pts, -0.012, 0.012, "metal", axis='y')
    g.tube([(2.6, -0.2, 1.35), (2.6, 0.5, 1.35)], [0.08, 0.08], 8, "metal_dark", cap_start=True)
    g.box((2.6, 0.55, 1.0), (0.9, 0.5, 1.2), "awning_green")
    g.tube(ring_pts((2.6, 0.82, 1.35), 0.4, 12, 'y'), [0.035] * 13, 4, "rubber", cap_end=False)
    for sy in (-1, 1):
        g.box((-0.5, sy * 0.45 - 0.6, 0.86), (9.0, 0.06, 0.1), "metal")
    for k in range(10):
        g.box((-4.5 + k * 0.9, -0.6, 0.8), (0.14, 1.2, 0.06), "wood_dark")
    for x in (-3.0, 0.5):
        g.box((x, -0.6, 1.05), (0.2, 1.1, 0.3), "metal_dark")
        g.box((x, -0.15, 1.45), (0.12, 0.1, 0.5), "sign_red")
    # planches sur lattes, madriers, tas de sciure, brouette, lampe
    rs = random.Random(g.seed)
    for (x, y, n) in ((5.5, 1.8, 6), (5.5, -1.6, 4)):
        for k in range(n):
            for j in range(5):
                g.box((x - 1.8 + j * 0.9, y, 0.14 + k * 0.215), (4.5 if False else 0.0001, 0.0001, 0.0001), "wood_cut") if False else None
            for j in range(4):
                g.box((x, y - 0.5 + j * 0.33 + rs.uniform(-0.02, 0.02), 0.14 + k * 0.215), (4.5 + rs.uniform(-0.15, 0.15), 0.3, 0.05), "wood_cut" if (j + k) % 3 else "wood_honey")
            for j in range(3):
                g.box((x - 1.7 + j * 1.7, y, 0.14 + k * 0.215 - 0.04), (0.06, 1.3, 0.03), "wood_dark")
    for k in range(5):
        g.box((W / 2 + 2.2 + rs.uniform(-0.05, 0.05), -1.0 - 1.8 + k * 0.05 * 0, 1.56 + k * 0.0), (0.0001, 0.0001, 0.0001), "wood") if False else None
    for k in range(6):
        g.box((W / 2 + 2.2 - 0.5 + (k % 3) * 0.5, -1.0, 1.56 + (k // 3) * 0.2), (0.42, 4.0 + rs.uniform(-0.2, 0.2), 0.16), "wood_honey" if k % 2 else "wood_cut")
    blob(g, (-5.5, 2.4, 0.3), 1.3, 2, "wood_cut", 0.5, g.seed, 0.1)
    m = g.mark()                                      # brouette
    g.prism([(-0.35, 0.3), (0.35, 0.3), (0.45, 0.62), (-0.45, 0.62)], -0.5, 0.4, "awning_green", axis='y')
    g.tube([(0.3, 0.4, 0.5), (0.36, 1.2, 0.55)], [0.02, 0.02], 4, "wood")
    g.tube([(-0.3, 0.4, 0.5), (-0.36, 1.2, 0.55)], [0.02, 0.02], 4, "wood")
    g.tube(ring_pts((0, -0.65, 0.2), 0.2, 10, 'x'), [0.05] * 11, 4, "rubber", cap_end=False)
    g.move(g.since(m), (-3.6, 3.0, 0), 0.6)
    g.box((0, 0, H + 0.7), (0.1, 0.1, 0.9), "metal_dark")
    g.frustum((0, 0, H + 0.05), 0.12, 0.3, 0.22, 10, "metal_dark", auto=False)
    g.octa((0, 0, H + 0.05), 0.1, "lamp")


# ----------------------------------------------------------------- tour en ruine
@extra("tower_ruin")
def tower_ruin(g):
    R, H = 4.0, 15.0
    rs = random.Random(g.seed + 3)

    def top(a):
        return H - max(0.0, math.sin(a + 0.6)) * 5.0 - 2.3

    for k in range(150):                              # moellons en relief sur le fût
        a = rs.uniform(0, 6.2832)
        z = rs.uniform(-0.2, top(a))
        r = R + 0.4 * (1.0 - (z + 3.0) / 18.0) - 0.02
        w, hh = rs.uniform(0.5, 1.0), rs.uniform(0.28, 0.45)
        g.box((math.cos(a) * r, math.sin(a) * r, z), (0.16, w, hh), ("wall_stone", "stone", "stone_warm", "rock_light")[k % 4], rotz=a)
    for k in range(9):                                # arase ébréchée : blocs disjoints
        a = 0.2 + k * 0.7
        z = top(a) + 2.2
        r = R + 0.05
        g.box((math.cos(a) * r, math.sin(a) * r, z + 0.2), (0.9, rs.uniform(0.7, 1.2), rs.uniform(0.5, 1.1)), "wall_stone", rotz=a)
    # porte : arc en plein cintre, vantail effondré ; archères encadrées
    for k in range(9):
        a = math.pi * k / 8
        g.box((math.cos(a) * 0.95, -R - 0.32, 2.0 + math.sin(a) * 0.95), (0.34, 0.3, 0.4), "stone_warm",
              rot=Matrix.Rotation(-(a - math.pi / 2), 3, 'Y'))
    for sx in (-1, 1):
        for k in range(4):
            g.box((sx * 0.95, -R - 0.32, 0.25 + k * 0.5), (0.4, 0.3, 0.46), "stone_warm" if k % 2 else "stone")
    g.box((0.9, -R - 0.9, 0.4), (0.08, 1.2, 1.9), "wood_dark", rot=Matrix.Rotation(0.5, 3, 'X'))
    for a, z in ((-1.57, 6.0), (-1.57, 10.0), (0.6, 8.0), (2.6, 7.0)):
        for dz in (-0.85, 0.85):
            g.box((math.cos(a) * (R + 0.2), math.sin(a) * (R + 0.2), z + dz), (0.3, 0.7, 0.3), "stone_warm", rotz=a)
    # courtine : parement, créneaux ; éboulis ; lierre et broussailles
    for k in range(4):
        hh = 6.5 - k * 1.4
        x0 = R + 1.2 + k * 2.4
        for sy in (-1, 1):
            f = Fac(g, 'y', 0.3 + sy * 0.8, sy)
            f.stonework(x0 - 1.2, x0 + 1.2, 0.0, hh, ("wall_stone", "stone", "stone_warm"), row=0.45, fill=0.6, depth=0.07)
    for k in range(12):
        a = rs.uniform(0, 6.28)
        d = R + rs.uniform(0.8, 4.5)
        rock(g, (math.cos(a) * d, math.sin(a) * d - 0.4, 0.12), (rs.uniform(0.6, 1.5), rs.uniform(0.5, 1.2), rs.uniform(0.4, 0.9)),
             "rock_light" if k % 2 else "rock", g.seed + k, 2, 0.55, rot=rs.uniform(0, 3))
    for a, z, s in ((2.2, 2.5, 2.4), (3.4, 5.0, 2.0), (0.9, 1.5, 1.8), (2.7, 7.0, 1.5), (4.3, 1.2, 1.6)):
        for j in range(4):
            aa = a + rs.uniform(-0.3, 0.3)
            blob(g, (math.cos(aa) * (R + 0.25), math.sin(aa) * (R + 0.25), z + rs.uniform(-s, s) * 0.7), s * rs.uniform(0.3, 0.5), 1,
                 "leaf_dark" if j % 2 else "leaf", 1.2, g.seed + j, 0.25)
    for k in range(6):
        a = rs.uniform(0, 6.28)
        blob(g, (math.cos(a) * (R + 1.5), math.sin(a) * (R + 1.5), 0.3), rs.uniform(0.5, 0.9), 1, "leaf" if k % 2 else "grass", 0.7, g.seed + 20 + k, 0.2)


# ----------------------------------------------------------------- monument
@extra("monument")
def monument(g):
    for z, s in ((0.42, 5.08), (0.72, 3.88)):         # nez de marches
        g.box((0, 0, z - 0.03), (s, s, 0.05), "rock_light")
    g.box((0, 0, 0.78), (2.2, 2.2, 0.14), "stone")                                           # moulures du socle
    g.box((0, 0, 2.05), (2.14, 2.14, 0.1), "stone")
    for f in (Fac(g, 'y', -1.0, -1), Fac(g, 'y', 1.0, 1), Fac(g, 'x', -1.0, -1), Fac(g, 'x', 1.0, 1)):
        f.slab(0, 1.4, 1.5, 1.0, 0.03, "stone")
        f.slab(0, 1.4, 1.3, 0.84, 0.05, "brass" if f.pos < 0 and f.axis == 'y' else "stone_dark")
        for k in range(5):
            f.slab(0, 1.68 - k * 0.14, 1.0 - 0.1 * (k % 2), 0.05, 0.06, "black" if f.pos < 0 and f.axis == 'y' else "stone")
    lathe(g, 0, 0, [(2.2, 1.2, "stone"), (2.3, 1.05, "stone")], 4, cap=False)
    for k in range(4):                                # arêtes de l'obélisque, pyramidion doré
        a = math.pi / 4 + math.pi / 2 * k
        g.beam((math.cos(a) * 0.95, math.sin(a) * 0.95, 2.3), (math.cos(a) * 0.45, math.sin(a) * 0.45, 8.3), 0.06, "stone")
    g.frustum((0, 0, 8.72), 0.2, 0.0, 0.3, 4, "brass", rot=math.pi / 4)
    for k in range(14):                               # couronne de laurier
        a = 6.2832 * k / 14
        g.octa((math.cos(a) * 0.36, -1.14, 0.98 + math.sin(a) * 0.36), 0.09, "leaf" if k % 2 else "leaf_dark", scale=(1, 0.5, 1))
    for k in range(3):
        g.octa((-0.1 + 0.1 * k, -1.18, 0.64), 0.05, "red")
    for sx in (-1, 1):                                # bornes tournées et chaînes en guirlande
        for sy in (-1, 1):
            lathe(g, sx * 3.1, sy * 3.1, [(0.78, 0.1, "stone_dark"), (0.84, 0.14, "stone_dark"), (0.9, 0.08, "stone_dark"), (0.98, 0.12, "stone_dark"),
                                          (1.06, 0.0, "stone_dark")], 8)
    for a, b in (((-3.1, -3.1), (-3.1, 3.1)), ((3.1, -3.1), (3.1, 3.1)), ((-3.1, 3.1), (3.1, 3.1))):
        pts = [(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, 0.86 - 0.3 * math.sin(math.pi * t)) for t in [k / 8 for k in range(9)]]
        g.tube(pts, [0.02] * 9, 4, "metal_dark", cap_end=False)
    flowers(g, (-1.6, -2.0, 0.42), 0.4, ("geranium", "flower_white"), n=14)
    flowers(g, (1.6, -2.0, 0.42), 0.4, ("flower_blue", "flower_white"), n=14)


# ----------------------------------------------------------------- fontaine
@extra("fountain_plaza")
def fountain_plaza(g):
    n = 16
    for k in range(n):                                # margelle en pierres de taille, panneaux de la cuve
        a = 6.2832 * (k + 0.5) / n + math.pi / 8
        g.box((math.cos(a) * 2.5, math.sin(a) * 2.5, 0.99), (0.5, 0.98, 0.12), "stone" if k % 2 else "rock_light", rotz=a)
        g.box((math.cos(a) * 2.52, math.sin(a) * 2.52, 0.55), (0.06, 0.7, 0.42), "stone", rotz=a)
    lathe(g, 0, 0, [(0.76, 0.6, "stone"), (0.9, 0.45, "stone"), (1.0, 0.5, "stone_warm"), (1.08, 0.36, "stone"), (2.1, 0.3, "stone"),
                    (2.2, 0.44, "stone_warm"), (2.3, 0.4, "stone_warm")], 12, cap=False)
    lathe(g, 0, 0, [(2.62, 0.24, "stone"), (2.8, 0.18, "stone"), (3.3, 0.14, "stone"), (3.4, 0.24, "stone_warm"), (3.5, 0.16, "stone_warm"),
                    (3.56, 0.0, "stone")], 10)
    for k in range(4):                                # mascarons et jets
        a = math.pi / 4 + math.pi / 2 * k
        c, s = math.cos(a), math.sin(a)
        g.box((c * 0.42, s * 0.42, 1.75), (0.22, 0.26, 0.26), "brass", rotz=a)
        pts = [(c * (0.5 + 1.3 * t), s * (0.5 + 1.3 * t), 1.75 + 0.5 * t - 1.5 * t * t) for t in [j / 6 for j in range(7)]]
        g.tube(pts, [0.025, 0.028, 0.032, 0.036, 0.042, 0.05, 0.06], 5, "water", cap_end=False)
        g.frustum((pts[-1][0], pts[-1][1], 0.765), 0.22, 0.3, 0.004, 10, "sign_white", auto=False)
        a2 = a + math.pi / 4
        pts = [(math.cos(a2) * (0.86 + 0.4 * t), math.sin(a2) * (0.86 + 0.4 * t), 2.6 - 1.84 * t * t) for t in [j / 5 for j in range(6)]]
        g.tube(pts, [0.02, 0.022, 0.025, 0.03, 0.035, 0.04], 4, "water", cap_end=False)
    for r in (1.0, 1.6):
        g.tube(ring_pts((0, 0, 0.762), r, 20, 'z'), [0.02] * 21, 3, "sign_white", cap_end=False)
    for k in range(4):
        a = math.pi / 2 * k
        lathe(g, math.cos(a) * 3.2, math.sin(a) * 3.2, [(0.2, 0.22, "cap"), (0.6, 0.32, "cap"), (0.64, 0.36, "cap"), (0.66, 0.3, "dirt")], 10)
        flowers(g, (math.cos(a) * 3.2, math.sin(a) * 3.2, 0.66), 0.34, ("geranium", "flower_white", "flower_pink"), n=12)


# ----------------------------------------------------------------- kiosque à musique
@extra("bandstand")
def bandstand(g):
    n, R = 8, 3.6
    K.cone_roofing(g, (0, 0, 4.1), R + 0.6, 0.5, 1.7, 16, "copper", rot=math.pi / n, cap=24)
    for k in range(n):
        a = 2 * math.pi * k / n + math.pi / n
        a2 = 2 * math.pi * (k + 1) / n + math.pi / n
        p = Vector((math.cos(a) * (R - 0.15), math.sin(a) * (R - 0.15), 0))
        q = Vector((math.cos(a2) * (R - 0.15), math.sin(a2) * (R - 0.15), 0))
        lathe(g, p.x, p.y, [(0.9, 0.14, "white"), (1.0, 0.14, "white"), (1.06, 0.09, "white")], 8, cap=False)       # base et chapiteau
        lathe(g, p.x, p.y, [(3.86, 0.075, "white"), (3.94, 0.13, "white"), (4.1, 0.15, "white")], 8, cap=False)
        g.beam(p + Vector((0, 0, 4.0)), q + Vector((0, 0, 4.0)), 0.1, "white", 0.2)                               # sablière
        for t in (0.0, 1.0):                          # consoles en volute
            b = p if t == 0.0 else q
            d = (q - p).normalized() * (1 if t == 0.0 else -1)
            g.tube([b + Vector((0, 0, 3.3)), b + d * 0.25 + Vector((0, 0, 3.55)), b + d * 0.7 + Vector((0, 0, 3.86))], [0.025, 0.022, 0.018], 4, "white")
        for j in range(1, 5):                         # frise découpée
            m = p.lerp(q, j / 5)
            g.box((m.x, m.y, 3.78), (0.04, 0.3, 0.26), "white", rotz=math.atan2(q.y - p.y, q.x - p.x) + math.pi / 2)
        if k != 5:                                    # garde-corps : barreaux tournés
            for j in range(1, 8):
                m = p.lerp(q, j / 8)
                lathe(g, m.x, m.y, [(0.9, 0.03, "white"), (1.2, 0.045, "white"), (1.5, 0.025, "white"), (1.72, 0.03, "white")], 5, cap=False)
        g.beam(Vector((math.cos(a) * (R + 0.55), math.sin(a) * (R + 0.55), 4.12)), Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 5.82)), 0.08, "copper_b")
    lathe(g, 0, 0, [(6.6, 0.1, "brass"), (6.7, 0.16, "brass"), (6.82, 0.06, "brass"), (7.2, 0.0, "brass")], 8)
    g.tube([(0, 0, 4.9), (0, 0, 4.2)], [0.012, 0.012], 4, "metal_dark")                      # lustre
    g.frustum((0, 0, 3.95), 0.22, 0.1, 0.25, 8, "lamp", auto=False)
    for k in range(n):                                # treillage du soubassement
        a = 2 * math.pi * k / n + math.pi / n
        a2 = 2 * math.pi * (k + 1) / n + math.pi / n
        p = Vector((math.cos(a) * (R + 0.22), math.sin(a) * (R + 0.22), 0))
        q = Vector((math.cos(a2) * (R + 0.22), math.sin(a2) * (R + 0.22), 0))
        for j in range(6):
            g.beam(p.lerp(q, j / 6) + Vector((0, 0, 0.05)), p.lerp(q, (j + 1) / 6) + Vector((0, 0, 0.75)), 0.03, "wood_honey")
            g.beam(p.lerp(q, (j + 1) / 6) + Vector((0, 0, 0.05)), p.lerp(q, j / 6) + Vector((0, 0, 0.75)), 0.03, "wood_honey")
    for sx in (-1, 1):                                # rampes de l'escalier
        g.tube([(sx * 0.95, -R * 0.92 - 1.4, 0.9), (sx * 0.95, -R * 0.92 - 0.2, 1.75)], [0.03, 0.03], 6, "white")
        g.tube([(sx * 0.95, -R * 0.92 - 1.4, 0.0), (sx * 0.95, -R * 0.92 - 1.4, 0.92)], [0.04, 0.04], 6, "white")


# ----------------------------------------------------------------- pylône radio
@extra("radio_tower")
def radio_tower(g):
    H = 38.0

    def hw(z):
        return 2.4 + (0.45 - 2.4) * z / H
    for k in range(76):                               # échelle à crinoline sur la face -Y
        z = 0.6 + k * 0.48
        g.tube([(-0.2, -hw(z) - 0.12, z), (0.2, -hw(z) - 0.12, z)], [0.012, 0.012], 3, "metal", cap_end=False)
    for sx in (-1, 1):
        g.beam((sx * 0.2, -hw(0.4) - 0.12, 0.4), (sx * 0.2, -hw(37.0) - 0.12, 37.0), 0.03, "metal")
    for k in range(10):
        z = 3.5 + k * 3.4
        g.tube(ring_pts((0, -hw(z) - 0.42, z), 0.34, 8, 'z', 3.3, 6.2), [0.012] * 9, 3, "metal", cap_end=False)
    for z in (12.7, 25.3, 34.0):                      # passerelles à garde-corps
        w = hw(z) + 0.7
        g.box((0, 0, z), (2 * w, 2 * w, 0.06), "metal_dark")
        for sx in (-1, 1):
            g.box((sx * w, 0, z + 1.0), (0.04, 2 * w, 0.04), "metal")
            g.box((0, sx * w, z + 1.0), (2 * w, 0.04, 0.04), "metal")
            g.box((sx * w, 0, z + 0.5), (0.03, 2 * w, 0.03), "metal")
            g.box((0, sx * w, z + 0.5), (2 * w, 0.03, 0.03), "metal")
            for sy in (-1, 1):
                g.box((sx * w, sy * w, z + 0.5), (0.05, 0.05, 1.0), "metal")
        g.box((0, 0, z + 0.25), (0.25, 0.25, 0.25), "neon_red")
    for z, a in ((31.5, 0.3), (31.5, 2.4), (31.5, 4.5), (28.0, 1.3), (28.0, 3.4), (28.0, 5.5), (22.0, 0.9), (22.0, 4.0)):   # antennes panneaux
        r = hw(z) + 0.5
        g.box((math.cos(a) * r, math.sin(a) * r, z), (0.14, 0.34, 2.0), "white", rotz=a)
        g.beam((math.cos(a) * (r - 0.45), math.sin(a) * (r - 0.45), z), (math.cos(a) * r, math.sin(a) * r, z), 0.04, "metal")
    for z, a in ((36.0, 1.0), (36.6, 3.6)):           # yagis
        g.tube([(0, 0, z), (math.cos(a) * 1.6, math.sin(a) * 1.6, z)], [0.02, 0.02], 4, "metal")
        for j in range(5):
            c = Vector((math.cos(a) * (0.4 + j * 0.28), math.sin(a) * (0.4 + j * 0.28), z))
            g.tube([c - Vector((0, 0, 0.35 - j * 0.04)), c + Vector((0, 0, 0.35 - j * 0.04))], [0.008, 0.008], 3, "metal")
    g.tube([(0.3, 0.3, 0.0), (0.3, 0.3, 30.0)], [0.05, 0.05], 5, "black")                    # chemin de câbles
    g.tube([(0.3, 0.3, 1.2), (2.2, 0.3, 1.6)], [0.05, 0.05], 5, "black")
    # local technique : porte, ventilation, climatiseur ; clôture grillagée
    f = Fac(g, 'y', -1.1, -1)
    f.door(3.5, 0.9, 2.0, "metal_dark", frame=None)
    for k in range(5):
        Fac(g, 'x', 4.8, 1).slab(0.3, 1.9 + k * 0.1, 0.8, 0.05, 0.03, "metal_dark")
    g.box((5.1, -0.4, 0.6), (0.5, 0.9, 0.8), "metal")
    P = [(-4.2, -4.2), (6.2, -4.2), (6.2, 4.2), (-4.2, 4.2)]
    for k in range(4):
        a, b = Vector((P[k][0], P[k][1], 0)), Vector((P[(k + 1) % 4][0], P[(k + 1) % 4][1], 0))
        nseg = 5
        for j in range(nseg):
            p0 = a.lerp(b, j / nseg)
            g.tube([p0 - Vector((0, 0, 0.5)), p0 + Vector((0, 0, 2.2))], [0.035, 0.035], 5, "metal")
        if k == 0:
            continue                                  # accès côté façade
        for z in (0.1, 1.1, 2.1):
            g.beam(a + Vector((0, 0, z)), b + Vector((0, 0, z)), 0.025, "metal")
        g.face([a + Vector((0, 0, 0.1)), b + Vector((0, 0, 0.1)), b + Vector((0, 0, 2.1)), a + Vector((0, 0, 2.1))], "roof_metal_b") if False else None
    g.box((-4.2 + 0.6, -4.24, 1.5), (0.6, 0.02, 0.4), "sign_yellow")


# ----------------------------------------------------------------- croix de sommet
@extra("summit_cross")
def summit_cross(g):
    rs = random.Random(g.seed)
    for k in range(10):
        a = rs.uniform(0, 6.28)
        d = rs.uniform(0.3, 1.5)
        rock(g, (math.cos(a) * d, math.sin(a) * d, 0.25 + (1.5 - d) * 0.3), (rs.uniform(0.6, 1.1), rs.uniform(0.5, 0.9), rs.uniform(0.4, 0.7)),
             "rock" if k % 2 else "rock_light", g.seed + k, 2, 0.6, rot=rs.uniform(0, 3))
    for z in (1.2, 4.0, 4.8, 5.5):                    # ferrures de la croix, coiffes de zinc
        g.box((0, 0, z), (0.31, 0.31, 0.1), "metal_dark")
    for sx in (-1, 1):
        g.box((sx * 0.9, 0, 4.4), (0.1, 0.29, 0.29), "metal_dark")
        g.box((sx * 1.41, 0, 4.4), (0.03, 0.3, 0.3), "roof_metal")
        g.tube([(sx * 1.3, 0, 4.4), (sx * 2.0, 0.3, 2.1), (sx * 2.6, 0.4, 0.2)], [0.012] * 3, 4, "metal", cap_end=False)
        g.box((sx * 2.0, 0.3, 2.1), (0.05, 0.05, 0.3), "metal_dark", rot=Matrix.Rotation(sx * 0.3, 3, 'Y'))
    g.box((0, 0, 5.72), (0.34, 0.34, 0.03), "roof_metal")
    g.box((0, -0.17, 3.2), (0.46, 0.03, 0.56), "metal_dark")
    g.box((0, -0.19, 3.2), (0.4, 0.02, 0.5), "brass")
    for k in range(4):
        g.box((0, -0.205, 3.36 - k * 0.1), (0.3, 0.01, 0.04), "black")
    g.box((0.7, -0.5, 0.9), (0.08, 0.08, 1.0), "metal")                                      # livre d'or du sommet
    g.box((0.7, -0.55, 1.45), (0.4, 0.26, 0.3), "metal")
    g.box((0.7, -0.55, 1.62), (0.46, 0.32, 0.04), "sign_red")
    p0, p1 = Vector((0, 0, 4.1)), Vector((-2.2, -1.2, 0.6))                                    # fanions
    for k in range(6):
        c = p0.lerp(p1, (k + 0.6) / 7) - Vector((0, 0, 0.25 * math.sin(math.pi * (k + 0.6) / 7)))
        col = ("sign_blue", "white", "sign_red", "sign_green", "sign_yellow", "sign_blue")[k]
        g.cloth(c + Vector((-0.12, 0, 0)), (0, 0, -0.26), (0.24, 0, 0), 2, 1, col, amp=0.08, swing=True, tip=0.83)
    g.tube([p0.lerp(p1, t) - Vector((0, 0, 0.25 * math.sin(math.pi * t))) for t in [k / 6 for k in range(7)]], [0.005] * 7, 3, "white", cap_end=False)
