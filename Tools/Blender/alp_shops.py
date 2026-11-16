"""Pack alpin : intérieurs des boutiques, filmés par les menus du jeu (concession et garage de tuning).

Ces décors ne sont pas posés sur la carte : chaque menu les construit à l'écart du monde (ShopStage.cs).
Repère : comme le reste du pack (mètres, origine au sol), mais pensé dans le repère du menu — x en travers, y vers le
fond de la pièce, z vers le haut. Unity les pose tournés de 180° : x et y du fichier deviennent x et z du menu.

- showroom_bay_a / _b / _c : travée du hall d'exposition, 7 m de large (x de -3,5 à 3,5), de y = -14 (côté caméra)
  à y = 9 (mur du fond). Un podium (car_podium) se pose au centre ; les trois variantes changent le mur du fond
  (vitrine, mur de marque, mur bois) et le mobilier qui le longe. Mises bout à bout, les demi-pilastres se rejoignent.
- showroom_end : mur de bout du hall (la pièce est du côté +x), accueil, entrée vitrée, plantes. Unity le pose tel quel
  à gauche et en miroir à droite.
- tuning_workshop : atelier du garage de tuning, 13 x 16 m (x de -6,5 à 6,5, y de -8 à 8), 5,6 m sous plafond, porte
  sectionnelle en y = 8. Pont ciseaux au centre (plateaux à 0,2 m : la voiture du joueur s'y pose), établi, panneau
  d'outils, râtelier de pneus, étagères, fûts, compresseur, servante, cric, grue d'atelier.
Mêmes couleurs que les façades : concession blanche à la marque rouge, garage sombre à la marque orange et néons bleus.
"""
import math
from mathutils import Matrix, Vector
import alp_lib as A
from alp_lib import reg

# ----------------------------------------------------------------- outils communs


def cyl(g, c, r, h, color, n=16, axis='z', cap0=True, cap1=True):
    """Cylindre de base c (axe z vers le haut), couché selon x ou y au besoin (c reste le centre de la base)."""
    m = g.mark()
    g.frustum((0, 0, 0), r, r, h, n, color, cap0=cap0, cap1=cap1, auto=False)
    if axis == 'x':
        rot = Matrix.Rotation(math.pi / 2, 4, 'Y')
    elif axis == 'y':
        rot = Matrix.Rotation(-math.pi / 2, 4, 'X')
    else:
        rot = Matrix.Identity(4)
    g.transform(g.since(m), Matrix.Translation(c) @ rot)


def disc_y(g, c, r, depth, color, facing, n=24):
    """Disque plaqué sur un mur perpendiculaire à y, tourné vers facing (-1 : vers -y, +1 : vers +y)."""
    m = g.mark()
    g.frustum((0, 0, 0), r, r, depth, n, color, auto=False)
    rot = Matrix.Rotation(math.pi / 2 * (1 if facing < 0 else -1), 4, 'X')
    g.transform(g.since(m), Matrix.Translation(c) @ rot)


def disc_x(g, c, r, depth, color, facing, n=24):
    """Disque plaqué sur un mur perpendiculaire à x, tourné vers facing (+1 : vers +x)."""
    m = g.mark()
    g.frustum((0, 0, 0), r, r, depth, n, color, auto=False)
    rot = Matrix.Rotation(math.pi / 2 * (1 if facing > 0 else -1), 4, 'Y')
    g.transform(g.since(m), Matrix.Translation(c) @ rot)


def plant(g, x, y, s=1.0, pot="concrete", leaf="leaf"):
    """Plante d'intérieur en pot : pot tronconique, terre, touffe de feuilles en boules."""
    g.frustum((x, y, 0.0), 0.32 * s, 0.38 * s, 0.62 * s, 28, pot, auto=False)
    g.frustum((x, y, 0.62 * s), 0.39 * s, 0.39 * s, 0.05 * s, 16, "black", auto=False)
    g.frustum((x, y, 0.6 * s), 0.34 * s, 0.34 * s, 0.03, 16, "dirt", cap0=False, auto=False)
    g.tube([(x, y, 0.6 * s), (x + 0.04 * s, y, 1.25 * s)], [0.04 * s, 0.03 * s], 6, "bark")
    for k, (dx, dy, dz, r) in enumerate(((0, 0, 1.45, 0.42), (0.26, 0.1, 1.2, 0.3), (-0.24, -0.08, 1.24, 0.32),
                                          (0.05, -0.24, 1.65, 0.26), (-0.06, 0.22, 1.7, 0.24))):
        g.ico((x + dx * s, y + dy * s, dz * s), r * s, 1, leaf if k % 2 == 0 else "leaf_dark")


def palm(g, x, y, s=1.0, pot="white"):
    """Grande plante à palmes (feuilles en lames) dans un pot cylindrique."""
    g.frustum((x, y, 0.0), 0.36 * s, 0.36 * s, 0.7 * s, 20, pot, auto=False)
    g.frustum((x, y, 0.7 * s), 0.38 * s, 0.38 * s, 0.04, 20, "metal_dark", auto=False)
    g.tube([(x, y, 0.7 * s), (x + 0.05, y - 0.03, 1.5 * s), (x + 0.02, y, 2.1 * s)], [0.06, 0.05, 0.04], 6, "bark")
    for k in range(9):
        a = k * 2 * math.pi / 9 + 0.3
        tilt = 0.55 + 0.25 * ((k * 37) % 3) / 2
        d = Vector((math.cos(a) * tilt, math.sin(a) * tilt, 0.55))
        g.blade((x + 0.02, y, 2.05 * s), d, 1.15 * s, 0.24 * s, "leaf" if k % 2 else "leaf_light", bend=0.45, segs=3)


# ================================================================= hall d'exposition
W = 7.0                          # largeur d'une travée
YB = 9.0                         # face du mur du fond
YC = -14.0                       # bord côté caméra
HC = 6.6                         # dessous du plafond
RED = "sign_red"                 # couleur de la marque (comme la façade de la concession)


def hall_floor(g, x0=-W / 2, x1=W / 2):
    """Dalle de joints sombres, carreaux clairs de 1,75 m (légèrement en relief), filets rouges devant le podium."""
    g.box(((x0 + x1) / 2, (YC + YB) / 2, -0.15), (x1 - x0, YB - YC, 0.3), "concrete_dark")
    step = 1.75
    nx = int(round((x1 - x0) / step))
    ny = int(round((YB - YC) / step))
    sy = (YB - YC) / ny
    for i in range(nx):
        for j in range(ny):
            cx = x0 + step * (i + 0.5)
            cy = YC + sy * (j + 0.5)
            light = (i + j) % 2 == 0
            g.slab((cx, cy, 0.01), (step - 0.03, sy - 0.03, 0.02), "plaster_white" if light else "sign_white")
    # filets rouges et blancs au sol, devant et derrière les podiums (lignes de la vitrine)
    g.slab(((x0 + x1) / 2, -3.9, 0.025), (x1 - x0, 0.1, 0.01), RED)
    g.slab(((x0 + x1) / 2, -4.1, 0.025), (x1 - x0, 0.04, 0.01), "white")
    g.slab(((x0 + x1) / 2, 3.9, 0.025), (x1 - x0, 0.06, 0.01), RED)


def hall_ceiling(g, x0=-W / 2, x1=W / 2):
    """Plafond blanc, caissons, dalles lumineuses, rail de spots tourné vers le podium."""
    g.box(((x0 + x1) / 2, (YC + YB) / 2 + 0.15, HC + 0.15), (x1 - x0, YB - YC + 0.3, 0.3), "plaster_white")
    for y in (-10.0, -6.0, -2.0, 2.0, 6.0):
        g.box(((x0 + x1) / 2, y, HC - 0.2), (x1 - x0, 0.25, 0.4), "plaster_white")              # retombées
    for y in (-8.0, -4.0, 0.0, 4.0, 7.6):
        for x in (-1.6, 1.6):
            g.box((x, y, HC - 0.03), (1.5, 1.1, 0.06), "metal")                                   # cadre
            g.box((x, y, HC - 0.065), (1.36, 0.96, 0.02), "lamp")                                 # dalle lumineuse
    # rail de spots à l'aplomb du bord avant du podium
    g.box((0, -3.2, HC - 0.06), (W - 0.6, 0.08, 0.06), "black")
    for x in (-2.2, -0.75, 0.75, 2.2):
        g.tube([(x, -3.2, HC - 0.09), (x, -3.05, HC - 0.32)], [0.03, 0.03], 6, "black")
        m = g.mark()
        g.frustum((0, 0, 0), 0.08, 0.12, 0.28, 12, "black", auto=False)
        g.transform(g.since(m), Matrix.Translation((x, -3.0, HC - 0.38)) @ Matrix.Rotation(math.radians(150), 4, 'X'))
        disc_lamp = Matrix.Translation((x, -2.87, HC - 0.6)) @ Matrix.Rotation(math.radians(150), 4, 'X')
        m = g.mark()
        g.frustum((0, 0, 0), 0.1, 0.1, 0.012, 12, "lamp", auto=False)
        g.transform(g.since(m), disc_lamp)


def pilasters(g):
    """Demi-pilastres blancs aux deux bords de la travée, liseré lumineux, socle noir."""
    for s in (-1, 1):
        x = s * (W / 2 - 0.2)
        g.box((x, YB - 0.35, HC / 2), (0.4, 0.7, HC), "plaster_white")
        g.box((s * (W / 2 - 0.02), YB - 0.72, HC / 2 + 0.3), (0.04, 0.04, HC - 1.4), "lamp")
        g.box((x, YB - 0.36, 0.08), (0.42, 0.74, 0.16), "black")


def back_wall(g, hole=None):
    """Mur du fond commun : voile blanc, plinthe noire, corniche lumineuse. hole = (x0, x1, z0, z1) : ouverture
    (vitrine) par laquelle le menu montre le vrai paysage du jeu."""
    if hole is None:
        g.box((0, YB + 0.15, HC / 2), (W, 0.3, HC), "plaster_white")
    else:
        x0, x1, z0, z1 = hole
        for (a, b) in ((-W / 2, x0), (x1, W / 2)):
            g.box(((a + b) / 2, YB + 0.15, HC / 2), (b - a, 0.3, HC), "plaster_white")
        g.box(((x0 + x1) / 2, YB + 0.15, z0 / 2), (x1 - x0, 0.3, z0), "plaster_white")
        g.box(((x0 + x1) / 2, YB + 0.15, (z1 + HC) / 2), (x1 - x0, 0.3, HC - z1), "plaster_white")
    g.box((0, YB - 0.02, 0.07), (W - 0.8, 0.04, 0.14), "black")
    g.box((0, YB - 0.25, HC - 0.5), (W - 0.8, 0.5, 0.12), "plaster_white")                        # corniche
    g.box((0, YB - 0.49, HC - 0.58), (W - 0.8, 0.02, 0.04), "lamp")


def lounge_chair(g, x, y, rotz, color="black"):
    """Fauteuil club : assise, dossier, accoudoirs, pieds métal."""
    m = g.mark()
    g.box((0, 0, 0.28), (0.8, 0.75, 0.2), color)
    g.box((0, 0.3, 0.6), (0.8, 0.16, 0.5), color)
    for s in (-1, 1):
        g.box((s * 0.36, 0, 0.45), (0.1, 0.75, 0.22), color)
    g.box((0, -0.02, 0.4), (0.62, 0.62, 0.06), "white")                                            # coussin
    for s in (-1, 1):
        for e in (-1, 1):
            g.box((s * 0.33, e * 0.3, 0.09), (0.04, 0.04, 0.18), "metal")
    g.transform(g.since(m), Matrix.Translation((x, y, 0.02)) @ Matrix.Rotation(rotz, 4, 'Z'))


def coffee_table(g, x, y):
    g.frustum((x, y, 0.02), 0.08, 0.06, 0.38, 12, "metal", auto=False)
    g.frustum((x, y, 0.02), 0.3, 0.3, 0.03, 20, "metal_dark", auto=False)
    g.frustum((x, y, 0.4), 0.46, 0.46, 0.04, 24, "glass_blue", auto=False)
    g.box((x - 0.12, y + 0.05, 0.45), (0.3, 0.22, 0.02), RED)                                       # brochures
    g.box((x - 0.1, y + 0.07, 0.47), (0.28, 0.2, 0.02), "white")
    g.frustum((x + 0.18, y - 0.12, 0.44), 0.05, 0.045, 0.1, 10, "white", auto=False)              # tasse


def bay_window(g):
    """Variante A : grande vitrine (vitrage, meneaux, traverse, allège), jardinières de part et d'autre."""
    x0, x1 = -2.9, 2.9
    g.box((0, YB - 0.04, (0.55 + 5.7) / 2), (x1 - x0, 0.04, 5.15), "glass_blue")
    g.box((0, YB - 0.1, 0.45), (x1 - x0 + 0.2, 0.2, 0.22), "concrete_dark")                       # allège
    g.box((0, YB - 0.1, 5.75), (x1 - x0 + 0.2, 0.2, 0.14), "metal_dark")
    g.box((0, YB - 0.1, 3.3), (x1 - x0, 0.16, 0.12), "metal_dark")                                 # traverse
    for k in range(5):
        x = x0 + k * (x1 - x0) / 4
        g.box((x, YB - 0.1, (0.55 + 5.7) / 2), (0.1, 0.16, 5.15), "metal_dark")                   # meneaux
    for x in (-2.5, 2.5):
        g.box((x, YB - 0.75, 0.3), (1.2, 0.7, 0.56), "concrete")                                   # jardinière
        g.box((x, YB - 0.75, 0.6), (1.1, 0.6, 0.04), "dirt")
        for k in range(4):
            g.ico((x - 0.38 + k * 0.26, YB - 0.75 + (0.1 if k % 2 else -0.08), 0.78 + 0.06 * (k % 2)), 0.24, 1,
                  "leaf" if k % 2 else "leaf_light")
        g.ico((x, YB - 0.78, 1.0), 0.22, 1, "flower_pink" if x < 0 else "flower_yellow", scale=(1.3, 1, 0.7))


def bay_brand(g):
    """Variante B : panneau de marque rouge à emblème rond, bandeaux clairs, banquette et fauteuils."""
    g.box((0, YB - 0.1, 3.4), (5.4, 0.2, 2.8), RED)
    g.box((0, YB - 0.22, 4.86), (5.5, 0.06, 0.08), "lamp")
    g.box((0, YB - 0.22, 1.94), (5.5, 0.06, 0.08), "lamp")
    disc_y(g, (-1.6, YB - 0.2, 3.4), 0.95, 0.05, "white", -1, 32)
    disc_y(g, (-1.6, YB - 0.25, 3.4), 0.66, 0.04, RED, -1, 32)
    # silhouette de voiture dans l'emblème
    car = [(-2.1, 3.2), (-2.1, 3.38), (-1.85, 3.42), (-1.66, 3.62), (-1.36, 3.62), (-1.18, 3.42), (-1.1, 3.4),
           (-1.1, 3.2)]
    g.prism(car, YB - 0.31, YB - 0.29, "white", axis='y')
    for (x, z, w, h) in ((0.9, 3.85, 2.6, 0.34), (0.55, 3.3, 1.9, 0.16), (0.7, 2.95, 2.2, 0.16)):
        g.box((x, YB - 0.225, z), (w, 0.05, h), "white")
    # banquette basse et deux fauteuils autour d'une table
    g.box((0, YB - 0.7, 0.24), (3.0, 0.7, 0.44), "white")
    g.box((0, YB - 0.42, 0.62), (3.0, 0.16, 0.4), "white")
    for k in range(3):
        g.box((-1.0 + k * 1.0, YB - 0.72, 0.48), (0.92, 0.6, 0.08), "black")                        # coussins
    lounge_chair(g, -1.9, 6.4, math.radians(200))
    lounge_chair(g, 1.9, 6.4, math.radians(160))
    coffee_table(g, 0, 6.6)


def bay_wood(g):
    """Variante C : mur à tasseaux bois, affiches encadrées, étagère à trophées, borne d'information."""
    x0, x1 = -2.9, 2.9
    g.box((0, YB - 0.03, 2.75), (x1 - x0, 0.06, 5.3), "wood_dark")
    n = 28
    for k in range(n):
        x = x0 + (x1 - x0) * (k + 0.5) / n
        g.box((x, YB - 0.09, 2.75), (0.11, 0.06, 5.3), "wood_honey" if k % 3 else "wood_cut")
    for (x, col_a, col_b) in ((-1.9, "sign_blue", "orange"), (0.0, RED, "yellow"), (1.9, "green", "white")):
        g.box((x, YB - 0.15, 3.3), (1.3, 0.06, 1.7), "black")                                        # cadre
        g.box((x, YB - 0.19, 3.3), (1.14, 0.03, 1.54), "white")
        g.box((x, YB - 0.21, 3.55), (1.0, 0.02, 0.85), col_a)
        g.box((x, YB - 0.225, 3.35), (1.0, 0.02, 0.16), col_b)
        car = [(x - 0.4, 3.32 - 0.38), (x - 0.4, 3.32 - 0.22), (x - 0.18, 3.32 - 0.18), (x - 0.04, 3.32),
               (x + 0.22, 3.32), (x + 0.32, 3.32 - 0.18), (x + 0.42, 3.32 - 0.22), (x + 0.42, 3.32 - 0.38)]
        g.prism(car, YB - 0.245, YB - 0.235, "black", axis='y')
        g.box((x, YB - 0.21, 2.75), (1.0, 0.02, 0.1), "black")
    g.box((0, YB - 0.3, 1.6), (5.6, 0.4, 0.06), "wood_honey")                                       # étagère
    for k, x in enumerate((-2.2, -1.3, -0.4, 1.0, 2.1)):
        h = 0.3 + 0.12 * (k % 3)
        g.frustum((x, YB - 0.3, 1.63), 0.09, 0.07, 0.06, 12, "black", auto=False)
        g.frustum((x, YB - 0.3, 1.69), 0.03, 0.03, h * 0.5, 8, "brass", auto=False)
        g.frustum((x, YB - 0.3, 1.69 + h * 0.5), 0.05, 0.12, h * 0.5, 12, "brass", auto=False)
    # borne d'information : pied, écran incliné
    x, y = 2.2, 7.2
    g.box((x, y, 0.5), (0.36, 0.3, 1.0), "plaster_white")
    g.box((x, y, 0.06), (0.6, 0.5, 0.08), "black")
    m = g.mark()
    g.box((0, 0, 0), (0.62, 0.06, 0.44), "black")
    g.box((0, -0.035, 0), (0.54, 0.02, 0.36), "window_lit")
    g.transform(g.since(m), Matrix.Translation((x, y - 0.08, 1.18)) @ Matrix.Rotation(math.radians(-25), 4, 'X'))
    plant(g, -2.4, 7.3, 1.15)


def price_stand(g, x=2.75, y=-2.9):
    """Pupitre d'information à côté du podium : socle, mât, panneau incliné à bandeau rouge."""
    g.frustum((x, y, 0.02), 0.22, 0.22, 0.04, 16, "metal_dark", auto=False)
    g.box((x, y, 0.55), (0.06, 0.06, 1.0), "metal")
    m = g.mark()
    g.box((0, 0, 0), (0.5, 0.04, 0.36), "white")
    g.box((0, -0.025, 0.13), (0.5, 0.02, 0.1), RED)
    g.box((-0.05, -0.025, -0.02), (0.36, 0.02, 0.04), "black")
    g.box((-0.09, -0.025, -0.09), (0.28, 0.02, 0.03), "black")
    g.transform(g.since(m), Matrix.Translation((x, y - 0.05, 1.12)) @ Matrix.Rotation(math.radians(-30), 4, 'X'))


def showroom_bay(variant):
    def build(g):
        hall_floor(g)
        hall_ceiling(g)
        back_wall(g, (-2.9, 2.9, 0.56, 5.68) if variant == "a" else None)
        pilasters(g)
        price_stand(g)
        {"a": bay_window, "b": bay_brand, "c": bay_wood}[variant](g)
    return build


for _v, _seed in (("a", 540), ("b", 541), ("c", 542)):
    reg(dict(id="showroom_bay_" + _v, file="Showroom_Bay_" + _v.upper(), cat="interior", tags=["shop", "interior"],
             seed=_seed, fp=(W, YB - YC), collider="none", lods=1, grad_h=HC, base=0.0,
             build=showroom_bay(_v),
             note="travée du hall d'exposition (menu de la concession) : x -3,5..3,5, y -14..9 (fond), "
                  "plafond 6,6 m ; podium au centre"))


def showroom_end(g):
    """Mur de bout du hall (pièce du côté +x) : voile blanc, entrée vitrée, accueil, emblème, plantes."""
    t = 0.3
    yd = -6.5
    # voile percé derrière l'entrée vitrée (y = yd ± 1,65, jusqu'à 4,15 m)
    for (a, b) in ((YC, yd - 1.65), (yd + 1.65, YB + 0.3)):
        g.box((-t / 2, (a + b) / 2, (HC + 0.3) / 2), (t, b - a, HC + 0.3), "plaster_white")
    g.box((-t / 2, yd, (4.15 + HC + 0.3) / 2), (t, 3.3, HC + 0.3 - 4.15), "plaster_white")
    g.box((0.6, (YC + YB) / 2, -0.15), (1.2, YB - YC, 0.3), "concrete_dark")                           # dalle (bord)
    g.slab((0.6, (YC + YB) / 2, 0.01), (1.17, YB - YC - 0.03, 0.02), "plaster_white")
    g.box((0.6, (YC + YB) / 2 + 0.15, HC + 0.15), (1.2, YB - YC + 0.3, 0.3), "plaster_white")
    g.box((0.02, (YC + YB) / 2, 0.07), (0.04, YB - YC - 0.2, 0.14), "black")                         # plinthe
    # entrée vitrée (deux vantaux, imposte)
    g.box((0.04, yd, 1.55), (0.04, 3.2, 2.9), "glass_blue")
    g.box((0.08, yd, 1.55), (0.06, 0.1, 2.9), "metal_dark")
    for s in (-1, 1):
        g.box((0.08, yd + s * 1.6, 1.6), (0.08, 0.12, 3.2), "metal_dark")
        g.box((0.12, yd + s * 0.25, 1.2), (0.06, 0.04, 0.8), "metal")                                    # poignées
    g.box((0.08, yd, 3.1), (0.08, 3.3, 0.12), "metal_dark")
    g.box((0.04, yd, 3.6), (0.04, 3.2, 0.9), "glass_blue")
    g.box((0.08, yd, 4.1), (0.08, 3.3, 0.1), "metal_dark")
    g.box((0.6, yd, 0.022), (1.0, 2.2, 0.012), "rubber")                                                 # tapis
    # emblème mural et bandeau rouge
    g.box((0.06, 2.5, 4.4), (0.08, 9.0, 0.5), RED)
    disc_x(g, (0.1, 5.2, 2.8), 1.0, 0.05, "white", 1, 32)
    disc_x(g, (0.15, 5.2, 2.8), 0.7, 0.04, RED, 1, 32)
    # accueil : comptoir blanc à bandeau rouge, plateau bois, écran
    x, y = 1.6, 5.2
    g.box((x, y, 0.55), (0.7, 3.2, 1.1), "plaster_white")
    g.box((x + 0.36, y, 0.75), (0.03, 3.0, 0.16), RED)
    g.box((x, y, 1.13), (0.9, 3.4, 0.06), "wood_honey")
    m = g.mark()
    g.box((0, 0, 0), (0.04, 0.55, 0.35), "black")
    g.box((0.025, 0, 0), (0.01, 0.48, 0.28), "window_lit")
    g.transform(g.since(m), Matrix.Translation((x - 0.15, y + 0.6, 1.4)) @ Matrix.Rotation(math.radians(10), 4, 'Y'))
    g.box((x - 0.15, y + 0.6, 1.18), (0.18, 0.18, 0.04), "black")
    palm(g, 0.8, 8.2)
    palm(g, 0.8, -9.5, 0.9)
    plant(g, 0.7, -3.9, 1.1)


reg(dict(id="showroom_end", file="Showroom_End", cat="interior", tags=["shop", "interior"], seed=543,
         fp=(1.2, YB - YC), collider="none", lods=1, grad_h=HC, base=0.0, build=showroom_end,
         note="mur de bout du hall (pièce côté +x), y -14..9 ; Unity : à gauche tel quel, à droite en miroir"))


# ================================================================= atelier du garage de tuning
HX, HY, H = 6.5, 8.0, 5.6
LIFT = 0.2                       # dessus des plateaux du pont ciseaux
DADO = 1.4                       # haut du soubassement sombre
BRAND = "orange"                 # comme la façade du garage
NEON = "neon_blue"
DOOR_W, DOOR_H = 5.4, 4.3
WIN_Y = (-5.4, -1.8, 1.8, 5.4)   # fenêtres hautes des murs latéraux (axes)
WIN_W = 2.9
WIN_Z0, WIN_Z1 = 3.65, 4.65
WINDOWS = [(y - WIN_W / 2, y + WIN_W / 2) for y in WIN_Y]


def ws_floor(g):
    """Sol béton, aire du pont en béton foncé bordée de jaune, hachures devant la porte, caniveau."""
    g.box((0, 0, -0.15), (2 * HX + 0.6, 2 * HY + 0.6, 0.3), "concrete")
    g.slab((0, 0, 0.005), (3.6, 6.8, 0.01), "concrete_dark")
    for s in (-1, 1):
        g.slab((s * 1.85, 0, 0.012), (0.1, 7.0, 0.012), "sign_yellow")
        g.slab((0, s * 3.45, 0.012), (3.6, 0.1, 0.012), "sign_yellow")
    # hachures jaunes et noires devant la porte
    for k in range(14):
        x = -DOOR_W / 2 + (k + 0.5) * DOOR_W / 14
        g.slab((x, HY - 0.5, 0.012), (DOOR_W / 14 - 0.01, 0.6, 0.012), "sign_yellow" if k % 2 == 0 else "black")
    g.slab((0, -4.4, 0.006), (3.0, 0.3, 0.012), "metal_dark")                                        # caniveau
    for k in range(10):
        g.slab((-1.35 + k * 0.3, -4.4, 0.018), (0.05, 0.26, 0.012), "metal")


def checker_run(g, axis, pos, facing, a0, a1, z, step=0.3):
    """Bandeau à damier (deux rangées) sur un mur ; axis 'y' : mur perpendiculaire à y (le long de x)."""
    n = max(1, int(round((a1 - a0) / step)))
    step = (a1 - a0) / n
    off = pos + facing * 0.035
    for k in range(n):
        for row in (0, 1):
            col = "white" if (k + row) % 2 == 0 else "black"
            u = a0 + step * (k + 0.5)
            zc = z + (row - 0.5) * step / 2
            if axis == 'y':
                g.box((u, off, zc), (step, 0.03, step / 2), col)
            else:
                g.box((off, u, zc), (0.03, step, step / 2), col)


def ws_wall(g, axis, pos, facing, a0, a1, gap=None, windows=None):
    """Mur : voile blanc, soubassement sombre à nervures, bandeau orange, damier. gap = (u0, u1) : ouverture sur
    toute la hauteur ; windows = [(u0, u1)] : baies percées de WIN_Z0 à WIN_Z1 (vraie vue sur le monde)."""
    spans = [(a0, a1)] if gap is None else [(a0, gap[0]), (gap[1], a1)]
    t = 0.3
    out = pos - facing * t / 2                                                                         # axe du voile
    for (u0, u1) in spans:
        if u1 - u0 < 0.05:
            continue
        c, L = (u0 + u1) / 2, u1 - u0
        if axis == 'y':
            g.box((c, out, H / 2), (L, t, H), "plaster_white")
            g.box((c, pos + facing * 0.02, DADO / 2), (L, 0.04, DADO), "metal_dark")
            g.box((c, pos + facing * 0.03, DADO + 0.17), (L, 0.04, 0.3), BRAND)
        else:
            if windows:
                # voile en bandes : sous les baies, au-dessus, et trumeaux entre elles
                g.box((out, c, WIN_Z0 / 2), (t, L, WIN_Z0), "plaster_white")
                g.box((out, c, (WIN_Z1 + H) / 2), (t, L, H - WIN_Z1), "plaster_white")
                edges = [u0] + [v for w_ in sorted(windows) for v in w_] + [u1]
                for k in range(0, len(edges), 2):
                    if edges[k + 1] - edges[k] > 0.01:
                        g.box((out, (edges[k] + edges[k + 1]) / 2, (WIN_Z0 + WIN_Z1) / 2),
                              (t, edges[k + 1] - edges[k], WIN_Z1 - WIN_Z0), "plaster_white")
            else:
                g.box((out, c, H / 2), (t, L, H), "plaster_white")
            g.box((pos + facing * 0.02, c, DADO / 2), (0.04, L, DADO), "metal_dark")
            g.box((pos + facing * 0.03, c, DADO + 0.17), (0.04, L, 0.3), BRAND)
        n = int(L / 0.32)
        for k in range(n):
            u = u0 + (k + 0.5) * L / n
            if axis == 'y':
                g.box((u, pos + facing * 0.055, DADO / 2), (0.06, 0.03, DADO - 0.1), "roof_metal")
            else:
                g.box((pos + facing * 0.055, u, DADO / 2), (0.03, 0.06, DADO - 0.1), "roof_metal")
        checker_run(g, axis, pos, facing, u0, u1, DADO + 0.52)


def ws_ceiling(g):
    """Plafond sombre, fermes en treillis, réglettes lumineuses, gaine d'extraction."""
    g.box((0, 0, H + 0.15), (2 * HX + 0.6, 2 * HY + 0.6, 0.3), "metal_dark")
    for y in (-5.4, -1.8, 1.8, 5.4):
        g.box((0, y, H - 0.08), (2 * HX, 0.16, 0.16), "metal")                                       # membrure basse
        g.box((0, y, H - 0.75), (2 * HX, 0.12, 0.12), "metal")
        for k in range(int(round(2 * HX))):
            x0 = -HX + k * 1.0
            g.beam((x0, y, H - 0.75), (x0 + 1.0, y, H - 0.1), 0.06, "metal")                            # diagonales
    for y in (-4.0, 0.0, 4.0):
        for x in (-3.6, 0.0, 3.6):
            g.box((x, y, H - 0.55), (2.4, 0.42, 0.12), "black")
            g.box((x, y, H - 0.62), (2.2, 0.3, 0.02), "lamp")
            for s in (-1, 1):
                g.tube([(x + s * 1.0, y, H - 0.5), (x + s * 1.0, y, H - 0.02)], [0.012, 0.012], 4, "black")
    g.tube([(-HX + 0.2, -HY + 1.4, H - 0.55), (HX - 0.2, -HY + 1.4, H - 0.55)], [0.32, 0.32], 16, "roof_metal",
           cap_start=False, cap_end=False)
    for x in (-4.0, 0.0, 4.0):
        g.frustum((x, -HY + 1.4, H - 0.82), 0.36, 0.36, 0.08, 16, "metal", auto=False)               # bagues
        g.box((x, -HY + 1.4, H - 0.92), (0.5, 0.5, 0.12), "metal_dark")                                     # bouche


def door_wall(g):
    """Mur de la porte (y = HY, tourné vers -y) : porte sectionnelle à hublots, néon, porte de service, horloge."""
    ws_wall(g, 'y', HY, -1, -HX, HX, gap=(-DOOR_W / 2 - 0.2, DOOR_W / 2 + 0.2))
    g.box((0, HY + 0.15, (DOOR_H + H) / 2 + 0.1), (DOOR_W + 0.4, 0.3, H - DOOR_H - 0.2), "plaster_white")
    g.box((0, HY - 0.06, DOOR_H + 0.25), (DOOR_W + 0.4, 0.12, 0.3), "concrete_dark")                  # linteau
    for s in (-1, 1):
        g.box((s * (DOOR_W / 2 + 0.1), HY - 0.06, DOOR_H / 2), (0.2, 0.12, DOOR_H), "concrete_dark")
    g.box((0, HY - 0.14, DOOR_H + 0.5), (DOOR_W + 0.3, 0.05, 0.08), NEON)
    # tablier : lames, hublots, poignée, rail
    g.box((0, HY + 0.03, DOOR_H / 2), (DOOR_W, 0.06, DOOR_H), "roof_metal_b")
    for k in range(1, 12):
        z = k * DOOR_H / 12
        g.box((0, HY - 0.015, z), (DOOR_W - 0.04, 0.03, 0.035), "metal_dark")
    for k in range(5):
        x = -DOOR_W / 2 + (k + 0.5) * DOOR_W / 5
        g.box((x, HY - 0.02, 2.85), (0.8, 0.03, 0.28), "window_lit")
    g.box((1.9, HY - 0.05, 1.0), (0.3, 0.06, 0.06), "metal")
    for s in (-1, 1):
        g.box((s * (DOOR_W / 2 - 0.05), HY - 0.25, DOOR_H + 0.05), (0.06, 0.4, 0.06), "metal_dark")
    # porte de service vitrée, à droite
    xd = 5.3
    g.box((xd, HY - 0.03, 1.05), (1.0, 0.06, 2.1), "metal")
    g.box((xd, HY - 0.07, 1.5), (0.6, 0.03, 0.8), "window_lit")
    g.box((xd + 0.35, HY - 0.09, 1.0), (0.12, 0.05, 0.04), "black")
    g.box((xd, HY - 0.05, 2.15), (1.2, 0.1, 0.1), "metal_dark")
    g.box((xd, HY - 0.05, 2.45), (0.5, 0.06, 0.18), "neon_green")                                     # sortie
    # horloge et extincteur
    x, z = -HX + 0.7, 3.7
    disc_y(g, (x, HY - 0.02, z), 0.42, 0.06, "white", -1, 28)
    disc_y(g, (x, HY - 0.08, z), 0.44, 0.02, "black", -1, 28)
    g.box((x, HY - 0.1, z + 0.12), (0.04, 0.02, 0.26), "black")
    g.box((x + 0.1, HY - 0.1, z), (0.2, 0.02, 0.04), "black")
    g.frustum((3.7, HY - 0.15, 0.9), 0.1, 0.1, 0.5, 12, "red", auto=False)
    g.frustum((3.7, HY - 0.15, 1.4), 0.1, 0.04, 0.08, 12, "black", auto=False)
    g.box((3.7, HY - 0.03, 1.15), (0.3, 0.04, 0.7), "white")


def emblem_panel(g):
    """Mur du fond : grand panneau noir à néons, emblème (disque, couronne orange, clé), bandeaux."""
    y = -HY
    g.box((0, y + 0.08, 3.9), (6.6, 0.16, 2.2), "black")
    for z in (2.76, 5.04):
        g.box((0, y + 0.18, z), (6.7, 0.06, 0.07), NEON)
    for s in (-1, 1):
        g.box((s * 3.33, y + 0.18, 3.9), (0.07, 0.06, 2.35), NEON)
    cx, cz = -2.1, 3.9
    disc_y(g, (cx, y + 0.16, cz), 0.82, 0.05, "white", 1, 32)
    disc_y(g, (cx, y + 0.21, cz), 0.6, 0.04, BRAND, 1, 32)
    rot = Matrix.Rotation(math.radians(-38), 3, 'Y')
    d = rot @ Vector((1.0, 0.0, 0.0))
    g.box((cx, y + 0.27, cz), (1.0, 0.03, 0.16), "black", rot=rot)
    for s in (-1, 1):
        g.box((cx + s * 0.4 * d.x, y + 0.27, cz + s * 0.4 * d.z), (0.28, 0.03, 0.28), "black", rot=rot)
    for (x, z, w, h, c) in ((0.9, 4.35, 3.4, 0.4, BRAND), (0.5, 3.75, 2.6, 0.2, "white"), (0.75, 3.35, 3.0, 0.2, "white")):
        g.box((x, y + 0.18, z), (w, 0.04, h), c)


def back_bench(g):
    """Mur du fond : meubles bas rouges, plan de travail, moteur sur le plan, cartons, étagère murale."""
    y = -HY + 0.35
    g.box((0, y, 0.45), (7.0, 0.62, 0.9), "sign_red")
    for k in range(7):
        x = -3.0 + k * 1.0
        g.box((x, y - 0.315, 0.45), (0.94, 0.02, 0.84), "red")
        for d in range(3):
            g.box((x, y - 0.335, 0.2 + d * 0.27), (0.5, 0.03, 0.04), "metal")
    g.box((0, y + 0.02, 0.93), (7.2, 0.72, 0.06), "metal")
    # moteur (bloc, culasse orange, cornets, collecteur)
    x = -2.3
    g.box((x, y, 1.26), (0.8, 0.5, 0.6), "metal_dark")
    g.box((x, y, 1.62), (0.72, 0.42, 0.14), BRAND)
    for k in range(4):
        g.frustum((x - 0.27 + k * 0.18, y - 0.05, 1.69), 0.05, 0.06, 0.16, 10, "metal", auto=False)
    g.tube([(x + 0.4, y - 0.15, 1.35), (x + 0.6, y - 0.25, 1.15), (x + 0.62, y - 0.25, 0.97)], [0.04, 0.04, 0.04],
           8, "metal")
    cyl(g, (x - 0.42, y, 1.1), 0.18, 0.06, "metal", 20, axis='x')
    # cartons, bidons, caisse à outils
    for (bx, w, h, c) in ((0.0, 0.6, 0.4, "wood_cut"), (0.55, 0.45, 0.3, "wood_cut"), (0.25, 0.4, 0.25, "wood_honey")):
        g.box((bx, y + 0.05, 0.96 + h / 2 + (0.4 if c == "wood_honey" else 0.0)), (w, 0.45, h), c)
    g.box((1.6, y, 1.1), (0.7, 0.36, 0.26), "red")
    g.box((1.6, y, 1.26), (0.5, 0.06, 0.06), "black")
    for k, c in enumerate(("sign_blue", "yellow", "sign_blue")):
        g.frustum((2.5 + k * 0.28, y, 0.96), 0.1, 0.1, 0.32, 12, c, auto=False)
        g.frustum((2.5 + k * 0.28, y, 1.28), 0.04, 0.04, 0.06, 8, "black", auto=False)
    # étagère murale à pièces
    for z in (2.0, 2.45):
        g.box((2.3, -HY + 0.18, z), (2.4, 0.32, 0.04), "metal")
        for k in range(5):
            g.box((1.3 + k * 0.48, -HY + 0.2, z + 0.12), (0.36, 0.24, 0.2), ("wood_cut", "sign_blue", "orange")[k % 3])


def left_bench(g):
    """Mur de gauche : établi bois sur caissons rouges, panneau à outils, étau, lampe."""
    x = -HX + 0.4
    y0, y1 = -3.6, 1.6
    g.box((x, (y0 + y1) / 2, 0.47), (0.66, y1 - y0, 0.9), "sign_red")
    for k in range(4):
        yc = y0 + (k + 0.5) * (y1 - y0) / 4
        g.box((x + 0.335, yc, 0.47), (0.02, (y1 - y0) / 4 - 0.08, 0.82), "red")
        for d in range(3):
            g.box((x + 0.355, yc, 0.22 + d * 0.26), (0.03, 0.5, 0.04), "metal")
    g.box((x + 0.05, (y0 + y1) / 2, 0.96), (0.82, y1 - y0 + 0.1, 0.08), "wood_honey")
    # panneau à outils
    px = -HX + 0.04
    g.box((px, (y0 + y1) / 2, 2.25), (0.04, y1 - y0, 1.6), "wood_grey")
    for k in range(10):
        yy = y0 + 0.4 + k * 0.48
        L = 0.35 + 0.08 * (k % 4)
        if k % 3 == 0:                                                                                  # clé plate
            g.box((px + 0.05, yy, 2.55 - L / 2), (0.03, 0.05, L), "metal")
            g.box((px + 0.05, yy, 2.58), (0.03, 0.12, 0.08), "metal")
        elif k % 3 == 1:                                                                                # tournevis
            g.box((px + 0.05, yy, 2.5), (0.04, 0.06, 0.18), "yellow" if k % 2 else "red")
            g.box((px + 0.05, yy, 2.28), (0.02, 0.02, 0.26), "metal")
        else:                                                                                           # marteau
            g.box((px + 0.05, yy, 2.4), (0.03, 0.04, 0.4), "wood_honey")
            g.box((px + 0.05, yy, 2.62), (0.06, 0.18, 0.06), "metal_dark")
        g.box((px + 0.05, yy, 1.85), (0.06, 0.16, 0.12), ("sign_blue", "red", "metal_dark")[k % 3])     # pinces
    g.box((px + 0.04, (y0 + y1) / 2, 3.12), (0.06, y1 - y0, 0.06), "metal_dark")
    # étau, lampe articulée
    g.box((x + 0.1, y1 - 0.5, 1.1), (0.26, 0.3, 0.2), "sign_blue")
    g.box((x + 0.1, y1 - 0.5, 1.24), (0.36, 0.12, 0.1), "metal")
    g.tube([(x - 0.15, y0 + 0.4, 1.0), (x + 0.1, y0 + 0.4, 1.6), (x + 0.35, y0 + 0.5, 1.75)], [0.02, 0.02, 0.02],
           6, "black")
    g.frustum((x + 0.4, y0 + 0.5, 1.5), 0.13, 0.06, 0.2, 12, "yellow", auto=False)
    # bidons et seau sous l'établi, au bout
    drum(g, x, y1 + 0.6, "sign_blue", 0.85, 0.26)
    drum(g, x + 0.05, y1 + 1.2, RED_DRUM, 0.85, 0.26)


RED_DRUM = "sign_red"


def tire(g, x, y, z, axis='z', r=0.34, w=0.24, rim="metal"):
    """Pneu détaillé (flancs arrondis, sculptures, liseré blanc, jante à cinq rayons, moyeu) ; base en (x, y, z),
    largeur w le long de axis."""
    m = g.mark()
    n = 28
    g.frustum((0, 0, 0), r * 0.93, r, w * 0.14, n, "rubber", cap0=False, cap1=False, auto=False)
    g.frustum((0, 0, w * 0.14), r, r, w * 0.72, n, "rubber", cap0=False, cap1=False, auto=False)
    g.frustum((0, 0, w * 0.86), r, r * 0.93, w * 0.14, n, "rubber", cap0=False, cap1=False, auto=False)
    if r > 0.2:
        for k in range(16):                                                                  # sculptures
            a = k * 2 * math.pi / 16
            g.box((math.cos(a) * (r + 0.004), math.sin(a) * (r + 0.004), w / 2), (0.018, r * 0.22, w * 0.62), "black",
                  rotz=a)
    for side in (1, -1):
        z0 = w if side > 0 else 0.0
        def ring(rad, t, color, depth):
            zz = z0 + depth if side > 0 else z0 - depth - t
            g.frustum((0, 0, zz), rad, rad, t, n, color, cap0=side < 0, cap1=side > 0, auto=False)
        ring(r * 0.93, 0.004, "rubber", 0.0)
        if r > 0.2:
            ring(r * 0.84, 0.004, "white", 0.004)                                           # liseré blanc
            ring(r * 0.79, 0.004, "rubber", 0.008)
        ring(r * 0.64, 0.004, rim, 0.012)                                                     # jante
        ring(r * 0.52, 0.004, "metal_dark", 0.016)                                            # creux de jante
        ring(r * 0.16, 0.01, rim, 0.02)                                                       # moyeu
        if r > 0.2:
            for k in range(5):                                                                # rayons
                a = k * 2 * math.pi / 5 + 0.3
                zz = z0 + 0.022 if side > 0 else z0 - 0.03
                g.box((math.cos(a) * r * 0.34, math.sin(a) * r * 0.34, zz + 0.004), (r * 0.38, r * 0.1, 0.008), rim,
                      rotz=a)
    if axis == 'x':
        rot = Matrix.Rotation(math.pi / 2, 4, 'Y')
    elif axis == 'y':
        rot = Matrix.Rotation(-math.pi / 2, 4, 'X')
    else:
        rot = Matrix.Identity(4)
    g.transform(g.since(m), Matrix.Translation((x, y, z)) @ rot)


def drum(g, x, y, color, h=0.9, r=0.3):
    """Fût de 200 L : corps lisse, cerclages en relief, couvercle à bonde et rebord."""
    n = 28
    g.frustum((x, y, 0.0), r, r, h, n, color, cap1=False, auto=False)
    for zz in (0.02, h * 0.33, h * 0.66):
        g.frustum((x, y, zz - 0.02), r + 0.012, r + 0.012, 0.04, n, color, auto=False)
    g.frustum((x, y, h - 0.03), r + 0.015, r + 0.015, 0.05, n, "metal_dark", cap0=False, auto=False)
    g.frustum((x, y, h - 0.01), r - 0.01, r - 0.01, 0.012, n, color, cap0=False, auto=False)
    g.frustum((x + r * 0.5, y, h), 0.05, 0.05, 0.03, 12, "black", auto=False)
    g.frustum((x - r * 0.45, y + r * 0.2, h), 0.03, 0.03, 0.02, 10, "metal", auto=False)
    g.box((x, y - r - 0.005, h * 0.5), (r * 0.9, 0.01, h * 0.24), "white")                   # étiquette


def right_wall_storage(g):
    """Mur de droite : râtelier de pneus sur deux niveaux, rayonnage de cartons, fûts, compresseur."""
    x = HX - 0.45
    y0, y1 = -5.4, -1.2
    for z in (0.55, 1.65):
        g.box((x, (y0 + y1) / 2, z), (0.7, y1 - y0, 0.05), "metal")
        g.box((x - 0.33, (y0 + y1) / 2, z + 0.08), (0.04, y1 - y0, 0.1), BRAND)
        for k in range(8):
            yy = y0 + 0.3 + k * 0.52
            tire(g, x - 0.12, yy - 0.12, z + 0.37, axis='y', r=0.33, w=0.24, rim=("metal", "black", "brass")[k % 3])
    for yy in (y0, (y0 + y1) / 2, y1):
        for s in (-1, 1):
            g.box((x + s * 0.32, yy, 1.4), (0.06, 0.06, 2.8), "metal_dark")
    # rayonnage de cartons
    y0, y1 = 0.6, 4.4
    for z in (0.35, 1.15, 1.95, 2.75):
        g.box((x, (y0 + y1) / 2, z), (0.7, y1 - y0, 0.05), "metal")
        for k in range(6):
            yy = y0 + 0.35 + k * 0.62
            if (k + int(z * 3)) % 4 == 3:
                continue
            h = 0.3 + 0.12 * ((k * 7 + int(z * 10)) % 3)
            c = ("wood_cut", "wood_cut", "sign_blue", "orange", "white")[(k + int(z * 5)) % 5]
            g.box((x, yy, z + 0.025 + h / 2), (0.5, 0.5, h), c)
    for yy in (y0, y1):
        for s in (-1, 1):
            g.box((x + s * 0.32, yy, 1.5), (0.06, 0.06, 3.0), "orange")
    # fûts d'huile
    for (fx, fy, c) in ((HX - 0.55, 5.95, "sign_blue"), (HX - 0.55, 6.65, BRAND), (HX - 1.2, 6.3, "sign_red")):
        drum(g, fx, fy, c)
    # compresseur : cuve couchée rouge, moteur, roues, manomètre
    cx, cy = HX - 0.7, -HY + 1.15
    cyl(g, (cx, cy - 0.65, 0.55), 0.3, 1.3, "sign_red", 32, axis='y')
    for e in (-1, 1):                                                                                   # fonds bombés
        cyl(g, (cx, cy + e * 0.65 - (0.04 if e < 0 else 0.0), 0.55), 0.26, 0.04, "red", 28, axis='y')
    for e in (-1, 1):                                                                                   # pieds
        g.box((cx, cy + e * 0.4, 0.15), (0.5, 0.06, 0.3), "metal_dark")
    g.box((cx, cy, 1.0), (0.45, 0.55, 0.3), "black")
    g.tube([(cx, cy + 0.3, 1.05), (cx, cy + 0.45, 0.9)], [0.03, 0.03], 6, "metal")
    for s in (-1, 1):
        cyl(g, (cx - 0.3, cy + s * 0.5, 0.12), 0.12, 0.06, "rubber", 12, axis='x')
    disc_x(g, (cx - 0.34, cy + 0.2, 1.0), 0.08, 0.03, "white", -1, 12)


def tool_chest(g, x, y, rotz):
    """Servante roulante haute (rouge), tiroirs, poignée, outils posés dessus."""
    m = g.mark()
    g.box((0, 0, 0.62), (0.75, 0.48, 1.0), "sign_red")
    for k in range(6):
        g.box((0, -0.245, 0.25 + k * 0.15), (0.66, 0.02, 0.12), "red")
        g.box((0, -0.26, 0.25 + k * 0.15), (0.4, 0.02, 0.025), "metal")
    g.box((0, 0, 1.13), (0.78, 0.5, 0.03), "black")
    g.tube([(-0.42, -0.2, 0.9), (-0.5, -0.2, 0.9), (-0.5, 0.2, 0.9), (-0.42, 0.2, 0.9)], [0.015] * 4, 6, "metal")
    for s in (-1, 1):
        for e in (-1, 1):
            g.frustum((s * 0.3, e * 0.18, 0.0), 0.05, 0.05, 0.1, 8, "rubber", auto=False)
    g.box((0.12, 0.05, 1.18), (0.32, 0.14, 0.06), "metal_dark")
    g.box((-0.18, -0.06, 1.16), (0.04, 0.24, 0.03), "metal")
    g.transform(g.since(m), Matrix.Translation((x, y, 0.0)) @ Matrix.Rotation(rotz, 4, 'Z'))


def floor_jack(g, x, y, rotz):
    """Cric rouleur rouge et son manche."""
    m = g.mark()
    g.box((0, 0, 0.12), (0.34, 0.75, 0.14), "sign_red")
    g.box((0, -0.32, 0.24), (0.14, 0.14, 0.14), "metal")
    g.frustum((0, -0.32, 0.31), 0.08, 0.08, 0.03, 12, "black", auto=False)
    g.tube([(0, 0.35, 0.18), (0, 0.9, 0.62), (0, 1.0, 0.7)], [0.025, 0.025, 0.025], 6, "metal")
    for s in (-1, 1):
        for e in (-1, 1):
            g.frustum((s * 0.15, e * 0.3, 0.0), 0.05, 0.05, 0.06, 8, "metal_dark", auto=False)
    g.transform(g.since(m), Matrix.Translation((x, y, 0.0)) @ Matrix.Rotation(rotz, 4, 'Z'))


def engine_crane(g, x, y, rotz):
    """Grue d'atelier jaune repliée (pieds, mât, flèche, chaîne)."""
    m = g.mark()
    for s in (-1, 1):
        g.box((s * 0.35, 0.4, 0.06), (0.1, 1.3, 0.08), "yellow")
        g.frustum((s * 0.35, 1.0, 0.0), 0.04, 0.04, 0.03, 8, "rubber", auto=False)
    g.box((0, -0.2, 0.06), (0.8, 0.1, 0.08), "yellow")
    g.box((0, -0.2, 0.95), (0.12, 0.12, 1.75), "yellow")
    g.beam((0, -0.2, 1.75), (0, 1.1, 1.9), 0.1, "yellow")
    g.beam((0, -0.2, 0.7), (0, 0.4, 1.78), 0.07, "metal_dark")
    g.tube([(0, 1.1, 1.85), (0, 1.1, 1.3)], [0.012, 0.012], 4, "metal")
    g.box((0, 1.1, 1.25), (0.08, 0.04, 0.12), "metal_dark")
    g.transform(g.since(m), Matrix.Translation((x, y, 0.0)) @ Matrix.Rotation(rotz, 4, 'Z'))


def scissor_lift(g):
    """Pont ciseaux : deux plateaux sombres à rives jaunes (dessus à LIFT), ciseaux, rampes, boîtier de commande."""
    L = 5.0
    for s in (-1, 1):
        x = s * 0.8
        g.box((x, 0, LIFT - 0.05), (0.62, L, 0.1), "metal_dark")
        for e in (-1, 1):
            g.box((x + e * 0.3, 0, LIFT - 0.04), (0.04, L, 0.12), "yellow")
        for k in range(12):                                                                            # stries
            g.slab((x, -L / 2 + 0.3 + k * 0.4, LIFT + 0.002), (0.5, 0.06, 0.006), "metal")
        for e in (-1, 1):                                                                              # rampes
            y = e * (L / 2 + 0.45)
            pts = [(e * 0.45, 0.0), (-e * 0.45, LIFT - 0.02), (-e * 0.45, 0.0)]
            g.prism([(pp[0] + y, pp[1]) for pp in pts], x - 0.3, x + 0.3, "metal_dark", axis='x')
        # ciseaux
        for e in (-1, 1):
            g.beam((x + e * 0.22, -1.6, 0.02), (x + e * 0.22, 1.6, LIFT - 0.1), 0.06, "yellow")
            g.beam((x + e * 0.22, 1.6, 0.02), (x + e * 0.22, -1.6, LIFT - 0.1), 0.06, "yellow")
        g.box((x, 0, 0.015), (0.66, L - 0.4, 0.03), "metal_dark")
    # boîtier de commande sur pied
    g.box((2.45, -2.9, 0.55), (0.12, 0.12, 1.1), "metal_dark")
    g.box((2.45, -2.9, 1.2), (0.32, 0.22, 0.4), "yellow")
    g.box((2.45, -3.02, 1.25), (0.2, 0.02, 0.12), "black")
    g.frustum((2.38, -3.03, 1.08), 0.03, 0.03, 0.02, 8, "red", auto=False)
    g.box((2.45, -2.9, 0.03), (0.4, 0.4, 0.06), "metal_dark")


def corners(g):
    """Coins de l'atelier : piles de pneus, servante, cric, grue, équilibreuse, plots."""
    for k in range(4):
        tire(g, -HX + 0.8, -HY + 1.4, 0.02 + k * 0.25, r=0.34, w=0.24)
    for k in range(3):
        tire(g, -HX + 1.55, -HY + 1.0, 0.02 + k * 0.25, r=0.32, w=0.24, rim="black")
    tire(g, -HX + 1.2, -HY + 2.15, 0.02, r=0.34, w=0.24, rim="brass")
    tool_chest(g, 4.3, -HY + 1.2, math.radians(-8))
    floor_jack(g, 2.4, HY - 1.6, math.radians(-60))
    engine_crane(g, -HX + 1.0, 4.6, math.radians(-90))
    # équilibreuse de roues : socle, capot, axe, roue montée, écran
    x, y = HX - 1.0, 4.95
    g.box((x, y, 0.5), (0.7, 0.8, 1.0), "sign_blue")
    g.box((x, y, 1.05), (0.72, 0.82, 0.1), "metal")
    m = g.mark()
    g.box((0, 0, 0), (0.06, 0.34, 0.22), "black")
    g.box((-0.032, 0, 0), (0.01, 0.28, 0.16), "window_lit")
    g.transform(g.since(m), Matrix.Translation((x - 0.2, y, 1.45)) @ Matrix.Rotation(math.radians(-12), 4, 'Y'))
    g.box((x - 0.2, y, 1.2), (0.04, 0.04, 0.2), "metal_dark")
    cyl(g, (x - 0.6, y - 0.1, 0.72), 0.05, 0.3, "metal", 10, axis='x')
    tire(g, x - 0.75, y - 0.1, 0.72, axis='x', r=0.33, w=0.22, rim="brass")
    for (cx, cy) in ((3.4, HY - 0.9), (3.95, HY - 0.6), (-2.5, HY - 0.8)):
        g.frustum((cx, cy, 0.0), 0.22, 0.22, 0.04, 4, "orange", rot=math.pi / 4, auto=False)
        g.frustum((cx, cy, 0.04), 0.13, 0.03, 0.6, 12, "orange", auto=False)
        g.frustum((cx, cy, 0.24), 0.1, 0.085, 0.1, 12, "white", auto=False)


def poster(g, axis, pos, facing, u, z, w, h, colors, seed=0):
    """Affiche encadrée sur un mur : fond, bandes de couleur, silhouette de voiture, damier en pied."""
    def B(du, dz, su, sz, depth, c):
        if axis == 'y':
            g.box((u + du, pos + facing * depth, z + dz), (su, 0.02, sz), c)
        else:
            g.box((pos + facing * depth, u + du, z + dz), (0.02, su, sz), c)
    B(0, 0, w + 0.08, h + 0.08, 0.02, "black")
    B(0, 0, w, h, 0.04, colors[0])
    B(0, h * 0.18, w, h * 0.16, 0.06, colors[1])
    B(0, h * 0.02, w * 0.8, h * 0.06, 0.06, colors[2])
    # silhouette de voiture
    s = w * 0.36
    for (du, dz, su, sz) in ((0, -h * 0.16, s * 2, h * 0.1), (-s * 0.1, -h * 0.07, s * 1.1, h * 0.09)):
        B(du, dz, su, sz, 0.075, "black")
    n = 6
    for k in range(n):
        B(-w / 2 + w * (k + 0.5) / n, -h * 0.42, w / n, h * 0.08, 0.06, "white" if k % 2 else "black")


def high_windows(g, x, facing):
    """Bandeau de fenêtres hautes (jour) sur un mur latéral."""
    for y in WIN_Y:
        g.box((x + facing * 0.02, y, 4.15), (0.04, 2.9, 1.0), "glass_blue")
        g.box((x + facing * 0.05, y, 4.15), (0.04, 0.08, 1.0), "metal_dark")
        for z in (3.62, 4.68):
            g.box((x + facing * 0.06, y, z), (0.08, 3.0, 0.08), "metal_dark")
        for s in (-1, 1):
            g.box((x + facing * 0.06, y + s * 1.47, 4.15), (0.08, 0.08, 1.14), "metal_dark")


def services(g):
    """Réseaux le long des murs : chemin de câbles, tuyau d'air comprimé (bleu), gaz (jaune), prises."""
    for (x, f) in ((-HX, 1), (HX, -1)):
        g.box((x + f * 0.2, 0, 5.12), (0.3, 2 * HY - 0.8, 0.06), "metal")
        g.box((x + f * 0.34, 0, 5.17), (0.02, 2 * HY - 0.8, 0.1), "metal")
        for (z, c) in ((4.95, "sign_blue"), (4.84, "yellow")):
            g.tube([(x + f * 0.1, -HY + 0.4, z), (x + f * 0.1, HY - 0.4, z)], [0.035, 0.035], 8, c, cap_end=False)
        for y in (-6.5, -1.0, 4.5):
            g.box((x + f * 0.05, y, 1.7), (0.08, 0.3, 0.2), "sign_blue")                             # prises
            g.box((x + f * 0.1, y, 1.7), (0.02, 0.2, 0.1), "white")
    # enrouleur d'air au plafond, au-dessus du pont
    g.frustum((3.4, -3.4, H - 0.75), 0.25, 0.25, 0.18, 16, "sign_red", auto=False)
    g.tube([(3.4, -3.4, H - 0.75), (3.35, -3.3, 3.0), (3.25, -3.2, 2.7)], [0.018, 0.018, 0.018], 6, "yellow")
    g.box((3.25, -3.2, 2.62), (0.06, 0.06, 0.16), "black")


def lounge(g):
    """Coin pause contre le mur de la porte, à gauche : canapé, frigo, distributeur, machine à café."""
    y = HY - 0.5
    g.box((-5.2, y, 0.25), (2.0, 0.85, 0.36), "wood_red")                                           # canapé
    g.box((-5.2, y + 0.33, 0.68), (2.0, 0.2, 0.6), "wood_red")
    for s in (-1, 1):
        g.box((-5.2 + s * 0.92, y, 0.55), (0.18, 0.85, 0.3), "wood_red")
    for k in range(2):
        g.box((-5.65 + k * 0.9, y - 0.05, 0.47), (0.84, 0.7, 0.1), "barn")
    for s in (-1, 1):
        for e in (-1, 1):
            g.box((-5.2 + s * 0.9, y + e * 0.35, 0.04), (0.06, 0.06, 0.08), "black")
    g.box((-5.2, y - 1.0, 0.24), (1.0, 0.5, 0.06), "wood_honey")                                    # table basse
    for s in (-1, 1):
        g.box((-5.2 + s * 0.42, y - 1.0, 0.11), (0.06, 0.42, 0.2), "metal_dark")
    tire(g, -5.6, y - 1.0, 0.27, r=0.14, w=0.04, rim="metal")                                     # cendrier pneu
    # distributeur de boissons
    x = -3.5
    g.box((x, y - 0.05, 0.95), (0.9, 0.75, 1.9), "red")
    g.box((x - 0.12, y - 0.43, 1.15), (0.5, 0.02, 1.2), "window_lit")
    for k in range(4):
        g.box((x - 0.12, y - 0.445, 0.7 + k * 0.28), (0.46, 0.02, 0.03), "metal")
    g.box((x + 0.3, y - 0.435, 1.2), (0.16, 0.02, 0.6), "black")
    g.box((x, y - 0.43, 0.25), (0.6, 0.02, 0.16), "black")
    g.box((x, y - 0.43, 1.78), (0.8, 0.02, 0.18), "white")


def wall_rims(g):
    """Jantes exposées au mur de droite, au-dessus du râtelier."""
    Z = 3.0
    for k in range(5):
        y = -5.0 + k * 0.95
        c = ("metal", "brass", "black", "metal", "brass")[k]
        disc_x(g, (HX - 0.04, y, Z), 0.34, 0.08, "rubber", -1, 20)
        disc_x(g, (HX - 0.12, y, Z), 0.24, 0.03, c, -1, 20)
        for j in range(5):
            a = j * 2 * math.pi / 5
            g.box((HX - 0.16, y + math.cos(a) * 0.12, Z + math.sin(a) * 0.12), (0.03, 0.05, 0.16), c,
                  rot=Matrix.Rotation(a, 3, 'X'))
        disc_x(g, (HX - 0.17, y, Z), 0.05, 0.03, "black", -1, 10)


def floor_marks(g):
    """Taches d'huile, tapis anti-fatigue devant les établis, plaque d'égout."""
    for (x, y, r, sx) in ((0.4, -1.6, 0.42, 1.4), (-0.6, 1.4, 0.3, 1.0), (3.6, -5.4, 0.35, 1.6), (-4.5, 0.4, 0.28, 1.2),
                          (5.2, 3.0, 0.22, 1.0), (-1.4, 5.6, 0.25, 1.3)):
        g.frustum((x, y, 0.001), r, r, 0.004, 10, "black", rot=x, sx=sx, auto=False)
    g.slab((-HX + 1.15, -1.0, 0.01), (0.7, 4.6, 0.02), "rubber")
    g.slab((0, -HY + 1.1, 0.01), (6.6, 0.7, 0.02), "rubber")
    g.frustum((-2.6, -1.8, 0.001), 0.3, 0.3, 0.012, 16, "metal_dark", auto=False)


def side_neon(g):
    """Néon décoratif sur le mur de la porte, au-dessus du coin pause : éclair stylisé dans un cadre."""
    yw = HY - 0.06
    x0, z0 = -4.6, 2.85
    for (a, b) in (((-0.6, 0.45), (0.1, 0.45)), ((0.1, 0.45), (-0.2, 0.0)), ((-0.2, 0.0), (0.6, 0.0)),
                   ((0.6, 0.0), (-0.3, -0.55))):
        g.beam((x0 + a[0], yw, z0 + a[1]), (x0 + b[0], yw, z0 + b[1]), 0.07, NEON)
    for (a, b) in (((-1.0, 0.75), (1.0, 0.75)), ((1.0, 0.75), (1.0, -0.75)), ((1.0, -0.75), (-1.0, -0.75)),
                   ((-1.0, -0.75), (-1.0, 0.75))):
        g.beam((x0 + a[0], yw, z0 + a[1]), (x0 + b[0], yw, z0 + b[1]), 0.05, BRAND)


def wall_posts(g):
    """Ossature : poteaux métalliques entre les fenêtres hautes, lisse sous les fenêtres, poteaux du mur du fond."""
    for (x, f) in ((-HX, 1), (HX, -1)):
        g.box((x + f * 0.03, 0, 3.47), (0.06, 2 * HY - 0.6, 0.06), "metal_dark")
        for y in (0.0, -3.6, 3.6):
            g.box((x + f * 0.07, y, (3.3 + H) / 2), (0.14, 0.26, H - 3.3), "metal_dark")
            g.box((x + f * 0.15, y, (3.3 + H) / 2), (0.02, 0.1, H - 3.3), "metal")
    for x in (-4.25, 4.25):
        g.box((x, -HY + 0.07, (2.15 + H) / 2), (0.26, 0.14, H - 2.15), "metal_dark")
        g.box((x, -HY + 0.15, (2.15 + H) / 2), (0.1, 0.02, H - 2.15), "metal")


def exhaust_rack(g):
    """Mur du fond, au-dessus de l'établi : échappements suspendus (silencieux, sorties chromées)."""
    y = -HY + 0.16
    for x in (-3.3, -1.5, 0.3):
        g.box((x, -HY + 0.06, 2.3), (0.06, 0.12, 0.5), "metal_dark")                          # consoles
    for k, (z, r, c) in enumerate(((2.12, 0.045, "metal"), (2.32, 0.04, "brass"), (2.5, 0.045, "metal"))):
        x0 = -3.5 + 0.15 * k
        cyl(g, (x0, y, z), r, 3.9 - 0.3 * k, c, 16, axis='x')
        cyl(g, (x0 + 1.1, y, z), r * 2.4, 0.7, c, 20, axis='x')                               # silencieux
        cyl(g, (x0 + 3.9 - 0.3 * k, y, z), r * 1.5, 0.12, "brass" if c == "metal" else "metal", 16, axis='x')
        for xx in (-3.3, -1.5, 0.3):
            g.box((xx, y, z + r + 0.01), (0.03, 0.08, 0.02), "black")                            # crochets


def diag_screen(g):
    """Établi du fond : poste de diagnostic (écran incliné, clavier, valise OBD)."""
    x, y = -1.15, -HY + 0.4
    g.box((x, y + 0.05, 1.0), (0.3, 0.2, 0.02), "black")
    g.box((x, y + 0.08, 1.2), (0.05, 0.04, 0.38), "metal_dark")
    m = g.mark()
    g.box((0, 0, 0), (0.62, 0.04, 0.4), "black")
    g.box((0, -0.025, 0.0), (0.56, 0.01, 0.34), "window_lit")
    for k in range(4):                                                                           # courbes à l'écran
        g.box((-0.18 + k * 0.12, -0.032, -0.08 + 0.05 * (k % 2)), (0.1, 0.004, 0.012), "neon_green")
    g.box((0.0, -0.032, 0.12), (0.5, 0.004, 0.04), BRAND)
    g.transform(g.since(m), Matrix.Translation((x, y + 0.02, 1.42)) @ Matrix.Rotation(math.radians(-8), 4, 'X'))
    g.box((x, y - 0.18, 0.975), (0.46, 0.15, 0.02), "black")                                  # clavier
    for k in range(4):
        g.box((x, y - 0.24 + k * 0.035, 0.988), (0.42, 0.02, 0.006), "metal_dark")
    g.box((x + 0.42, y - 0.12, 1.05), (0.3, 0.22, 0.16), "sign_yellow")                     # valise OBD
    g.tube([(x + 0.42, y - 0.24, 1.08), (x + 0.6, y - 0.36, 0.98), (x + 0.75, y - 0.3, 0.97)], [0.012] * 3, 6, "black")


def electrical_panel(g):
    """Mur de gauche, angle du fond : armoire électrique (disjoncteurs), gaines montant au chemin de câbles."""
    x, y = -HX + 0.1, -5.3
    g.box((x, y, 2.2), (0.18, 0.9, 1.1), "roof_metal")
    g.box((x + 0.095, y, 2.2), (0.01, 0.84, 1.04), "metal")
    for row in range(3):
        for k in range(8):
            g.box((x + 0.11, y - 0.33 + k * 0.095, 2.5 - row * 0.3), (0.03, 0.06, 0.12), "black" if (k + row) % 3 else "red")
    g.box((x + 0.11, y + 0.3, 1.78), (0.02, 0.18, 0.12), "sign_yellow")                        # pictogramme
    for k in range(3):
        g.tube([(x - 0.03, y - 0.25 + k * 0.25, 2.75), (x - 0.03, y - 0.25 + k * 0.25, 5.0)], [0.025, 0.025], 8, "metal",
               cap_end=False)


def tv_corner(g):
    """Mur de gauche, près du coin pause : écran (course en cours) sur potence."""
    x, y, z = -HX + 0.12, 6.2, 2.55
    g.box((x - 0.06, y, z), (0.06, 0.3, 0.3), "metal_dark")
    g.box((x, y, z), (0.08, 1.46, 0.86), "black")
    g.box((x + 0.045, y, z), (0.01, 1.36, 0.76), "window")
    g.box((x + 0.05, y, z - 0.2), (0.008, 1.36, 0.34), "asphalt")                              # route
    g.box((x + 0.054, y + 0.05, z - 0.2), (0.006, 0.5, 0.02), "white")
    g.box((x + 0.056, y - 0.15, z - 0.12), (0.006, 0.34, 0.14), BRAND)                        # voiture
    g.box((x + 0.056, y - 0.08, z - 0.03), (0.006, 0.18, 0.08), "black")
    g.box((x + 0.054, y + 0.45, z + 0.27), (0.006, 0.36, 0.1), "white")                        # chrono
    g.box((x + 0.056, y + 0.45, z + 0.27), (0.006, 0.3, 0.05), "black")


def whiteboard(g):
    """Mur de droite, entre râtelier et rayonnage : tableau des travaux, chariot de diagnostic dessous."""
    x, y = HX - 0.05, -0.3
    g.box((x, y, 2.6), (0.04, 1.4, 0.95), "metal")
    g.box((x - 0.025, y, 2.6), (0.01, 1.32, 0.87), "white")
    for k in range(5):
        g.box((x - 0.032, y - 0.45 + 0.04 * (k % 2), 2.92 - k * 0.15), (0.006, 0.5 + 0.12 * (k % 3), 0.03),
              ("sign_blue", "red", "black", "green", "sign_blue")[k])
        g.box((x - 0.032, y + 0.45, 2.92 - k * 0.15), (0.006, 0.08, 0.06), "green" if k % 2 else "red")
    g.box((x - 0.04, y, 2.1), (0.06, 1.2, 0.04), "metal_dark")                                # auget
    g.box((x - 0.05, y - 0.3, 2.13), (0.02, 0.12, 0.02), "red")
    # chariot de diagnostic
    cx, cy = HX - 0.6, -0.3
    for s_ in (-1, 1):
        for e in (-1, 1):
            g.frustum((cx + s_ * 0.2, cy + e * 0.25, 0.0), 0.04, 0.04, 0.06, 10, "rubber", auto=False)
    g.box((cx, cy, 0.12), (0.5, 0.6, 0.04), "metal_dark")
    g.box((cx, cy, 0.52), (0.5, 0.6, 0.04), "metal_dark")
    for s_ in (-1, 1):
        for e in (-1, 1):
            g.box((cx + s_ * 0.22, cy + e * 0.27, 0.55), (0.03, 0.03, 1.0), "metal")
    g.box((cx, cy, 1.04), (0.52, 0.62, 0.04), "red")
    g.box((cx, cy - 0.05, 0.3), (0.4, 0.45, 0.32), "black")                                    # unité
    g.box((cx - 0.2, cy - 0.05, 0.3), (0.01, 0.3, 0.08), "neon_green")
    m = g.mark()
    g.box((0, 0, 0), (0.04, 0.5, 0.34), "black")
    g.box((-0.025, 0, 0), (0.01, 0.44, 0.28), "window_lit")
    g.transform(g.since(m), Matrix.Translation((cx, cy, 1.28)) @ Matrix.Rotation(math.radians(10), 4, 'Y'))
    for k in range(3):
        g.tube([(cx - 0.25, cy - 0.2 + k * 0.08, 0.9), (cx - 0.45, cy - 0.25 + k * 0.1, 0.5),
                (cx - 0.3, cy - 0.3 + k * 0.1, 0.13)], [0.012] * 3, 6, ("red", "black", "sign_yellow")[k])


def pennants(g):
    """Fanions orange, bleus et blancs tendus sous les fermes, deux guirlandes."""
    cols = (BRAND, "white", NEON, "white")
    for y in (-3.6, 3.6):
        n = 28
        prev = None
        for k in range(n + 1):
            t = k / n
            x = -HX + 0.3 + t * (2 * HX - 0.6)
            z = H - 0.7 - 0.35 * math.sin(t * math.pi)
            if prev is not None:
                g.beam(prev, (x, y, z), 0.012, "black")
            prev = (x, y, z)
            if 0 < k < n:
                g.prism([(x - 0.17, z), (x + 0.17, z), (x, z - 0.32)], y - 0.005, y + 0.005, cols[k % 4], axis='y')


def crossed_flags(g):
    """Mur du fond : drapeaux à damier croisés au-dessus des affiches."""
    for cx in (-5.2, 5.2):
        for s_ in (-1, 1):
            m = g.mark()
            g.box((0, 0, 0.55), (0.03, 0.03, 1.3), "metal_dark")
            for i in range(6):
                for j in range(4):
                    g.box((0.07 + 0.12 * i, 0, 1.12 - 0.12 * j), (0.12, 0.012, 0.12), "white" if (i + j) % 2 else "black")
            rot = Matrix.Rotation(math.radians(-30 * s_), 4, 'Y')
            flip = Matrix.Diagonal((s_, 1, 1, 1))
            g.transform(g.since(m), Matrix.Translation((cx, -HY + 0.08 + 0.02 * (s_ + 1), 4.3)) @ flip @ rot)


def pegboard_extras(g):
    """Panneau d'outils : jeu de clés graduées, pince, scie, tuyau enroulé ; étagère haute à aérosols."""
    px = -HX + 0.09
    for k in range(7):                                                                         # jeu de clés
        L = 0.18 + k * 0.035
        g.box((px, -3.35 + k * 0.09, 2.95 - L / 2), (0.02, 0.035, L), "metal")
    g.box((px, -3.08, 2.98), (0.02, 0.7, 0.03), "metal_dark")
    g.beam((px, 0.3, 2.0), (px, 0.55, 2.35), 0.04, "red")                                     # scie
    g.box((px, 0.75, 2.17), (0.015, 0.5, 0.12), "metal")
    for k in range(10):                                                                         # tuyau enroulé
        a = k * 2 * math.pi / 10
        b = (k + 1) * 2 * math.pi / 10
        for rr in (0.2, 0.24):
            g.beam((px + 0.03, 1.25 + math.cos(a) * rr, 2.4 + math.sin(a) * rr),
                   (px + 0.03, 1.25 + math.cos(b) * rr, 2.4 + math.sin(b) * rr), 0.03, "sign_yellow")
    g.box((-HX + 0.2, -1.0, 3.26), (0.32, 5.0, 0.04), "wood_honey")                           # étagère haute
    for k in range(9):
        c = (BRAND, "red", "sign_blue", "white", "black", "yellow")[k % 6]
        yy = -3.2 + k * 0.5
        g.frustum((-HX + 0.2, yy, 3.28), 0.04, 0.04, 0.2, 12, c, auto=False)
        g.frustum((-HX + 0.2, yy, 3.48), 0.03, 0.02, 0.04, 10, "white", auto=False)


def bench_parts(g):
    """Établi de gauche : turbo, boîte à vis, chiffon, bombe de peinture."""
    x = -HX + 0.45
    y = -1.6
    cyl(g, (x, y - 0.1, 1.15), 0.15, 0.2, "metal", 24, axis='y')                              # volute
    cyl(g, (x, y - 0.16, 1.15), 0.07, 0.06, "metal_dark", 16, axis='y')
    cyl(g, (x + 0.1, y + 0.1, 1.25), 0.06, 0.18, "metal", 16, axis='x')
    cyl(g, (x - 0.05, y + 0.12, 1.08), 0.1, 0.14, "brass", 20, axis='y')
    g.box((x, y + 0.7, 1.04), (0.3, 0.2, 0.08), "sign_blue")
    for k in range(3):
        g.box((x - 0.08 + k * 0.08, y + 0.7, 1.09), (0.06, 0.16, 0.01), "metal")
    g.box((x + 0.05, y - 0.7, 1.01), (0.35, 0.3, 0.02), "white")                               # chiffon


def door_motor(g):
    """Porte sectionnelle : moteur, chaîne de manœuvre, autocollants de sécurité."""
    x = DOOR_W / 2 + 0.45
    g.box((x, HY - 0.25, 4.65), (0.4, 0.3, 0.3), "metal_dark")
    g.box((x, HY - 0.41, 4.65), (0.3, 0.02, 0.06), "sign_yellow")
    g.tube([(x - 0.08, HY - 0.3, 4.5), (x - 0.08, HY - 0.3, 1.3)], [0.008, 0.008], 4, "metal")
    g.tube([(x + 0.08, HY - 0.3, 4.5), (x + 0.08, HY - 0.3, 1.5)], [0.008, 0.008], 4, "metal")
    g.box((x, HY - 0.06, 1.6), (0.18, 0.08, 0.3), "metal_dark")                                 # boîtier
    g.frustum((x, HY - 0.11, 1.66), 0.035, 0.035, 0.02, 10, "green", auto=False)
    for (bx, c) in ((-1.2, "sign_yellow"), (1.2, "sign_yellow")):
        g.box((bx, HY - 0.005, 2.05), (0.32, 0.01, 0.32), c, rot=Matrix.Rotation(math.radians(45), 3, 'Y'))
        g.box((bx, HY - 0.012, 2.05), (0.04, 0.01, 0.16), "black")


def floor_kit(g):
    """Au sol, près du pont (objets bas : la caméra passe au-dessus) : chariot de visite, bac de vidange, tuyau,
    roue couchée, caisse à outils, cales."""
    m = g.mark()                                                                                 # chariot de visite
    g.box((0, 0, 0.1), (0.48, 1.05, 0.04), "red")
    g.box((0, 0.42, 0.14), (0.4, 0.2, 0.05), "black")                                            # appui-tête
    for s_ in (-1, 1):
        for e in (-1, 1):
            g.frustum((s_ * 0.18, e * 0.42, 0.0), 0.035, 0.035, 0.08, 10, "black", auto=False)
    g.transform(g.since(m), Matrix.Translation((-1.75, 0.6, 0.0)) @ Matrix.Rotation(math.radians(8), 4, 'Z'))
    g.frustum((1.55, -1.5, 0.0), 0.36, 0.4, 0.12, 28, "sign_yellow", cap1=False, auto=False)       # bac de vidange
    g.frustum((1.55, -1.5, 0.0), 0.34, 0.34, 0.08, 28, "black", cap0=False, auto=False)
    g.frustum((1.55, -1.1, 0.06), 0.06, 0.06, 0.1, 12, "black", auto=False)
    for loop in range(2):                                                                        # tuyau d'air enroulé
        rr = 0.32 + loop * 0.05
        for k in range(18):
            a = k * 2 * math.pi / 18
            b = (k + 1) * 2 * math.pi / 18
            g.beam((-2.3 + math.cos(a) * rr, -2.1 + math.sin(a) * rr, 0.03 + loop * 0.04),
                   (-2.3 + math.cos(b) * rr, -2.1 + math.sin(b) * rr, 0.03 + loop * 0.04), 0.035, "sign_blue")
    tire(g, 2.35, 2.55, 0.0, r=0.33, w=0.23, rim="brass")                                        # roue couchée
    g.box((-2.05, 2.75, 0.13), (0.55, 0.26, 0.26), "red")                                       # caisse à outils
    g.box((-2.05, 2.75, 0.27), (0.4, 0.06, 0.03), "black")
    for (cx, cy) in ((0.8, 2.75), (-0.8, 2.75)):                                                 # cales de roue
        g.prism([(cy - 0.15, 0.0), (cy + 0.15, 0.0), (cy - 0.15, 0.14)], cx - 0.1, cx + 0.1, "sign_yellow", axis='x')


def lift_details(g):
    """Pont ciseaux : vérins hydrauliques, butées, flexible vers le boîtier, patins de levage."""
    for s_ in (-1, 1):
        x = s_ * 0.8
        for e in (-1, 1):
            g.tube([(x, e * 0.9, 0.04), (x, e * 0.35, LIFT - 0.06)], [0.05, 0.035], 12, "metal")    # vérins
            g.frustum((x, e * 0.9, 0.03), 0.07, 0.07, 0.04, 12, "metal_dark", auto=False)
        for e in (-1, 1):
            g.box((x, e * 2.25, LIFT + 0.03), (0.5, 0.06, 0.06), "sign_red")                         # butées
        for e in (-1.5, 1.5):
            g.frustum((x - s_ * 0.36, e, LIFT - 0.05), 0.07, 0.07, 0.06, 12, "rubber", auto=False)  # patins
    g.tube([(0.8, -2.2, 0.03), (1.6, -2.6, 0.03), (2.4, -2.85, 0.03)], [0.02, 0.02, 0.02], 8, "black")


def details(g):
    wall_posts(g)
    exhaust_rack(g)
    diag_screen(g)
    electrical_panel(g)
    tv_corner(g)
    whiteboard(g)
    pennants(g)
    crossed_flags(g)
    pegboard_extras(g)
    bench_parts(g)
    door_motor(g)
    floor_kit(g)
    lift_details(g)


def tuning_workshop(g):
    ws_floor(g)
    floor_marks(g)
    services(g)
    high_windows(g, -HX, 1)
    high_windows(g, HX, -1)
    poster(g, 'y', -HY, 1, 5.2, 3.6, 1.1, 1.5, ("sign_blue", BRAND, "white"))
    poster(g, 'y', -HY, 1, -5.3, 3.6, 1.1, 1.5, ("black", "yellow", "red"))
    poster(g, 'x', -HX, 1, 4.0, 2.4, 1.0, 1.4, ("orange", "black", "white"))
    poster(g, 'x', HX, -1, -HY + 1.0, 2.4, 1.0, 1.4, ("red", "white", "sign_blue"))
    side_neon(g)
    wall_rims(g)
    lounge(g)
    ws_ceiling(g)
    ws_wall(g, 'y', -HY, 1, -HX, HX)
    ws_wall(g, 'x', -HX, 1, -HY, HY, windows=WINDOWS)
    ws_wall(g, 'x', HX, -1, -HY, HY, windows=WINDOWS)
    for (sx, sy) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):                                             # poteaux d'angle
        g.box((sx * (HX - 0.15), sy * (HY - 0.15), H / 2), (0.3, 0.3, H), "metal_dark")
    door_wall(g)
    emblem_panel(g)
    back_bench(g)
    left_bench(g)
    right_wall_storage(g)
    scissor_lift(g)
    corners(g)
    details(g)


reg(dict(id="tuning_workshop", file="Tuning_Workshop", cat="interior", tags=["shop", "interior"], seed=544,
         fp=(2 * HX, 2 * HY), collider="none", lods=1, grad_h=H, base=0.0, build=tuning_workshop,
         note="atelier (menu du garage de tuning) : x -6,5..6,5, y -8..8 (porte en y = 8), plafond 5,6 m ; "
              "plateaux du pont à 0,2 m"))
