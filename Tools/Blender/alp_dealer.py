"""Pack alpin : concession automobile (hall d'exposition vitré et atelier), totem d'enseigne, podium d'exposition.

Repère du pack : origine au sol au centre de l'emprise, façade avant = -Y (Unity +Z), mètres.
Hall vitré à gauche (x de -19 à 7), atelier à droite (x de 7 à 19), profondeur 18 m (y de -9 à 9).
Le sol du hall est une dalle à +0,15 m (voitures exposées plus tard) ; l'intérieur du hall est vide.
Anti-z-fighting : vitrages, montants, traverses et rives à des profondeurs différentes (au moins 3 cm) ; les bouts de
vitrage et de murs se perdent dans les poteaux d'angle."""
import math
from mathutils import Matrix
import alp_lib as A
from alp_lib import reg, lv
import alp_kit as K
from alp_kit import Fac

Z0 = 0.12                      # dessus des fondations ; le sol fini du hall est à 0,15
HX0, HX1 = -19.0, 7.0          # hall
WX1 = 19.0                     # atelier de 7 à 19
D = 18.0
HH = 7.2                       # hauteur des vitrages (dessous de la dalle de toit du hall)
WH = 5.6                       # hauteur de l'atelier
ENTRY_X = -6.0                 # axe de l'entrée du hall
DOORS = (9.6, 14.4)            # portes sectionnelles de l'atelier (axe x)
RECEPTION_X = 17.6             # porte de la réception
BRAND = "sign_red"             # couleur de la marque (sans texte)


def disc(g, c, r, depth, n, color, facing=-1):
    """Disque (cylindre plat) plaqué sur un plan vertical, tourné vers -Y (facing = -1) ou +Y."""
    m = g.mark()
    g.frustum((0, 0, 0), r, r, depth, n, color, auto=False)
    rot = Matrix.Rotation(math.pi / 2 * (1 if facing < 0 else -1), 4, 'X')
    g.transform(g.since(m), Matrix.Translation(c) @ rot)


def column(g, x, y, z1, s=0.4, color="metal_dark"):
    g.slab((x, y, (Z0 + z1) / 2), (s, s, z1 - Z0), color)


def on_floor(g, c, size, color):
    """Pièce posée sur les fondations : sans face de dessous (les dessous confondus ne se voient pas, mais
    faussent le contrôle de z-fighting)."""
    return g.slab(c, size, color, open='-z')


def glazing_front(g, n_bays, y_glass=-8.95):
    """Façade vitrée avant du hall : vitrage bas (éclairé la nuit), traverse, vitrage haut, montants, allège."""
    x0, x1 = HX0 + 0.1, HX1 - 0.1
    on_floor(g, ((x0 + x1) / 2, y_glass, (Z0 + 3.1) / 2), (x1 - x0, 0.04, 3.1 - Z0), "window_lit")
    g.box(((x0 + x1) / 2, y_glass, (3.3 + HH + 0.05) / 2), (x1 - x0, 0.04, HH + 0.05 - 3.3), "glass_blue")
    g.box(((x0 + x1) / 2, -8.95, 3.2), (x1 - x0 - 0.04, 0.22, 0.2), "metal_dark")          # traverse (avant à -9,06)
    on_floor(g, ((x0 + x1) / 2, -8.99, Z0 + 0.12), (x1 - x0 - 0.08, 0.34, 0.24), "concrete_dark")  # allège
    step = (HX1 - HX0) / n_bays
    for k in range(1, n_bays):
        x = HX0 + k * step
        if abs(x - ENTRY_X) < 1.9:
            continue
        on_floor(g, (x, -8.97, (Z0 + HH + 0.13) / 2), (0.18, 0.3, HH + 0.13 - Z0), "metal_dark")   # montant


def glazing_side(g, n_bays, x_glass=-18.95):
    y0, y1 = -8.9, 8.9
    on_floor(g, (x_glass, 0, (Z0 + 3.1) / 2), (0.04, y1 - y0, 3.1 - Z0), "window_lit")
    g.box((x_glass, 0, (3.3 + HH + 0.05) / 2), (0.04, y1 - y0, HH + 0.05 - 3.3), "glass_blue")
    g.box((-18.95, 0, 3.2), (0.22, y1 - y0 - 0.04, 0.24), "metal_dark")
    on_floor(g, (-18.99, 0, Z0 + 0.13), (0.34, y1 - y0 - 0.08, 0.26), "concrete_dark")
    step = D / n_bays
    for k in range(1, n_bays):
        on_floor(g, (-18.97, -9.0 + k * step, (Z0 + HH + 0.13) / 2), (0.3, 0.18, HH + 0.13 - Z0), "metal_dark")


def entrance(g):
    """Entrée du hall : portique, deux vantaux vitrés coulissants, poignées, tapis."""
    x = ENTRY_X
    for s in (-1, 1):
        on_floor(g, (x + s * 1.85, -9.08, (Z0 + 2.85) / 2), (0.3, 0.44, 2.85 - Z0), "metal_dark")  # jambages
    g.box((x, -9.08, 3.0), (4.0, 0.44, 0.3), "metal_dark")                                   # linteau
    for s in (-1, 1):
        on_floor(g, (x + s * 0.85, -9.07, (Z0 + 2.85) / 2), (1.5, 0.06, 2.85 - Z0), "window_lit")  # vantaux
        if g.hd:
            for e in (-1, 1):                                                                # cadres des vantaux
                on_floor(g, (x + s * 0.85 + e * 0.72, -9.12, (Z0 + 2.85) / 2), (0.06, 0.04, 2.85 - Z0), "metal")
            g.box((x + s * 0.85, -9.1125, 2.82), (1.38, 0.025, 0.06), "metal")
            on_floor(g, (x + s * 0.85, -9.1125, Z0 + 0.1), (1.38, 0.025, 0.2), "metal")
            g.box((x + s * 0.2, -9.17, 1.3), (0.04, 0.06, 0.9), "metal")                     # poignées
    if g.hd:
        g.box((x, -9.9, 0.012), (3.2, 1.4, 0.024), "rubber")                                # tapis, devant la dalle


def canopy(g):
    """Auvent profond au-dessus de l'entrée, rive à la couleur de la marque, deux poteaux, bandeaux lumineux."""
    x0, x1, y0, y1 = ENTRY_X - 6.5, ENTRY_X + 6.5, -14.0, -8.6
    g.box(((x0 + x1) / 2, (y0 + y1) / 2, 4.45), (x1 - x0, y1 - y0, 0.3), "plaster_white")
    g.box(((x0 + x1) / 2, y0 + 0.05, 4.45), (x1 - x0 + 0.1, 0.2, 0.4), BRAND)                # rive avant (avant -14,05)
    if g.lod == 2:
        return
    for s in (-1, 1):
        g.box((ENTRY_X + s * 6.49, (y0 + y1) / 2 - 0.05, 4.45), (0.08, y1 - y0 - 0.3, 0.36), BRAND)  # rives latérales
        if g.hd:
            g.tube([(ENTRY_X + s * 5.9, -13.4, -0.4), (ENTRY_X + s * 5.9, -13.4, 4.3)], [0.15, 0.15], 12, "metal")
            g.frustum((ENTRY_X + s * 5.9, -13.4, 0.0), 0.3, 0.26, 0.12, 12, "concrete", auto=False)
        else:
            g.box((ENTRY_X + s * 5.9, -13.4, 2.0), (0.26, 0.26, 4.6), "metal")
    if g.hd:
        for y in (-12.6, -10.6):                                                             # bandeaux lumineux
            g.box((ENTRY_X, y, 4.27), (10.0, 0.18, 0.06), "lamp")


def roof_hall(g):
    g.box(((HX0 - 0.2 + HX1) / 2, 0, (HH + 7.6) / 2), (HX1 - HX0 + 0.2, D + 0.4, 7.6 - HH), "plaster_white")
    if g.lod == 2:
        return
    # acrotère (bord du toit) ; le côté droit domine l'atelier
    x0, x1 = HX0 - 0.2, HX1
    g.box(((x0 + x1) / 2, -9.075, 7.95), (x1 - x0, 0.25, 0.7), "plaster_white")
    g.box(((x0 + x1) / 2, 9.075, 7.95), (x1 - x0, 0.25, 0.7), "plaster_white")
    g.box((x0 + 0.125, 0, 7.95), (0.25, D - 0.1, 0.7), "plaster_white")
    g.box((x1 - 0.125, 0, 7.95), (0.25, D - 0.1, 0.7), "plaster_white")
    g.quad(((x0 + 0.25 + x1 - 0.25) / 2, 0, 7.63), x1 - x0 - 0.5, D - 0.1, '+z', "concrete_dark")   # gravillons
    if g.hd:
        for (x, y, w, d, h) in ((-14.0, 3.0, 2.4, 1.6, 1.1), (-9.0, 4.5, 1.8, 1.2, 0.9), (1.0, 2.0, 2.4, 1.6, 1.1)):
            g.box((x, y, 7.64 + h / 2), (w, d, h), "metal")                                   # groupes de clim
            g.box((x, y - d / 2 - 0.01, 7.64 + h * 0.55), (w * 0.7, 0.02, h * 0.6), "metal_dark")
        for k in range(3):                                                                     # lanterneaux
            g.box((-12.0 + k * 6.5, -2.5, 7.75), (2.2, 2.2, 0.24), "glass_blue")


def brand_panel(g):
    """Panneau de marque sur l'acrotère avant, au-dessus de l'entrée : à plat, sans texte, emblème rond."""
    g.box((ENTRY_X, -9.3, 8.15), (11.0, 0.2, 1.5), BRAND)                                     # avant à -9,40
    if g.lod == 2:
        return
    disc(g, (ENTRY_X - 3.6, -9.4, 8.15), 0.58, 0.05, lv(g, 32, 20), "white")
    disc(g, (ENTRY_X - 3.6, -9.45, 8.15), 0.4, 0.04, lv(g, 32, 20), BRAND)
    g.box((ENTRY_X + 1.2, -9.425, 8.32), (6.4, 0.05, 0.34), "white")                          # bandeaux clairs
    g.box((ENTRY_X + 0.4, -9.425, 7.9), (4.8, 0.05, 0.16), "white")


def workshop(g):
    """Atelier : bardage, deux portes sectionnelles, réception, bande à la couleur de la marque."""
    on_floor(g, ((HX1 + WX1) / 2, 0, (Z0 + WH) / 2), (WX1 - HX1, D, WH - Z0), "metal")
    g.box(((HX1 + WX1 + 0.2) / 2, 0, WH + 0.15), (WX1 - HX1 + 0.2, D + 0.4, 0.3), "plaster_white")
    f = Fac(g, 'y', -D / 2, -1)
    for u in DOORS:
        if g.lod == 2:
            f.quad(u, Z0 + 2.1, 4.0, 4.2, "metal_dark", 0.04)
            continue
        for s in (-1, 1):
            f.slab(u + s * 2.075, Z0 + 2.1, 0.15, 4.2, 0.14, "concrete_dark")                   # tableau
        f.slab(u, Z0 + 4.275, 4.3, 0.15, 0.14, "concrete_dark")
        f.slab(u, Z0 + 2.1, 4.0, 4.2, 0.06, "metal_dark" if not g.hd else "roof_metal_b")      # tablier
        f.slab(u, Z0 + 3.05, 3.6, 0.4, 0.02, "window", out=0.06)                                 # hublots
        if g.hd:
            for k in range(1, 12):
                z = Z0 + 0.35 * k
                if abs(z - (Z0 + 3.05)) < 0.3:
                    continue
                f.slab(u, z, 3.96, 0.035, 0.02, "metal_dark", out=0.06)                          # lames
            f.box(u + 1.6, Z0 + 1.2, 0.3, 0.12, 0.05, "metal", out=0.08)                         # poignée
            f.box(u, Z0 + 4.65, 0.5, 0.12, 0.25, "metal_dark")                                   # applique
            f.box(u, Z0 + 4.58, 0.4, 0.03, 0.18, "lamp")
    f.slab((HX1 + WX1) / 2, 4.95, WX1 - HX1 - 0.1, 0.5, 0.05, BRAND)
    if g.lod == 2:
        f.quad(RECEPTION_X, Z0 + 1.15, 1.1, 2.3, "window_lit", 0.04)
        return
    f.door(RECEPTION_X, 1.1, 2.3, "window_lit", frame="metal_dark", z0=Z0)
    g.box((RECEPTION_X, -D / 2 - 0.65, 2.75), (2.0, 1.3, 0.12), "plaster_white")              # marquise de la réception
    side = Fac(g, 'x', WX1, 1)
    for y in (-5.0, 0.0, 5.0):
        side.slab(y, 3.9, 2.2, 0.8, 0.05, "window")
    # acrotère de l'atelier
    g.box(((HX1 + WX1 + 0.2) / 2, -9.075, WH + 0.5), (WX1 - HX1 + 0.2, 0.25, 0.4), "plaster_white")
    g.box(((HX1 + WX1 + 0.2) / 2, 9.075, WH + 0.5), (WX1 - HX1 + 0.2, 0.25, 0.4), "plaster_white")
    g.box((WX1 + 0.075, 0, WH + 0.5), (0.25, D - 0.1, 0.4), "plaster_white")
    if g.hd:
        g.box((13.0, 3.0, WH + 0.3 + 0.45), (3.0, 1.6, 0.9), "metal")                          # extracteur
        g.tube([(WX1 + 0.12, 8.6, WH), (WX1 + 0.12, 8.6, 0.2)], [0.07, 0.07], 8, "roof_metal")
        g.tube([(WX1 + 0.12, -8.6, WH), (WX1 + 0.12, -8.6, 0.2)], [0.07, 0.07], 8, "roof_metal")


def car_showroom(g):
    g.box((0, 0, (Z0 - 0.9) / 2), (WX1 - HX0 + 0.3, D + 0.3, Z0 + 0.9), "concrete")         # fondations (dessus 0,12)
    if g.lod == 2:
        on_floor(g, ((HX0 + HX1) / 2, 0, (Z0 + HH) / 2), (HX1 - HX0, D, HH - Z0), "glass_blue")
        g.quad(((HX0 + HX1) / 2, -D / 2 - 0.03, (Z0 + 3.1) / 2), HX1 - HX0 - 0.4, 3.1 - Z0, '-y', "window_lit")
        roof_hall(g)
        brand_panel(g)
        canopy(g)
        workshop(g)
        return
    # dalle du hall (sol fini à +0,15), mur du fond, mur mitoyen de l'atelier, poteaux d'angle
    on_floor(g, ((HX0 + HX1) / 2, -0.1, (Z0 + 0.15) / 2), (HX1 - HX0 - 0.4, D - 0.4, 0.15 - Z0), "plaster_white")
    on_floor(g, ((HX0 + 0.1 + HX1 - 0.1) / 2, 8.85, (Z0 + HH + 0.1) / 2), (HX1 - HX0 - 0.2, 0.3, HH + 0.1 - Z0), "plaster_white")
    on_floor(g, (HX1 - 0.125, 0.0, (Z0 + HH) / 2), (0.25, D - 0.4, HH - Z0), "plaster_white")
    for x in (HX0, HX1 - 0.2):
        for y in (-9.0, 9.0):
            column(g, x, y, HH)
    glazing_front(g, lv(g, 10, 5))
    glazing_side(g, lv(g, 6, 3))
    entrance(g)
    roof_hall(g)
    brand_panel(g)
    canopy(g)
    workshop(g)
    if g.hd:
        for (x, y) in ((HX0 - 0.12, 8.6), (HX1 - 0.4, 9.12)):                                  # descentes d'eau
            g.tube([(x, y, HH + 0.3), (x, y, 0.2)], [0.07, 0.07], 8, "roof_metal")
        for x in (-17.5, -15.0, 2.0, 4.5):                                                      # potelets devant le vitrage
            g.tube([(x, -10.4, -0.3), (x, -10.4, 0.85)], [0.08, 0.08], 8, "metal")


reg(dict(id="car_showroom", file="Car_Showroom", cat="landmark", tags=["service", "shop", "car"], seed=520,
         fp=(38.0, 18.0), collider="box", build=car_showroom,
         note="hall vitré x -19..7 (entrée x=-6, sol +0,15), atelier x 7..19 (portes x=9,6 et 14,4, réception x=17,6), "
              "auvent jusqu'à 5 m devant la façade"))


def dealer_sign(g):
    """Totem d'enseigne : socle, deux mâts, grand panneau à la couleur de la marque (emblème rond, sans texte)."""
    g.box((0, 0, 0.0), (2.2, 0.9, 0.8), "concrete")                                          # socle (de -0,4 à 0,4)
    for s in (-1, 1):
        g.box((s * 0.7, 0, 4.35), (0.18, 0.3, 7.9), "metal_dark")                            # mâts (0,4 à 8,3)
    g.box((0, 0, 5.3), (1.7, 0.35, 5.8), BRAND)                                               # panneau (2,4 à 8,2)
    g.box((0, 0, 8.3), (1.85, 0.45, 0.2), "metal_dark")                                       # chapeau (8,2 à 8,4)
    if g.lod == 2:
        return
    for f in (-1, 1):
        disc(g, (0, f * 0.175, 7.25), 0.5, 0.04, lv(g, 28, 18), "white", facing=f)
        disc(g, (0, f * 0.215, 7.25), 0.34, 0.03, lv(g, 28, 18), BRAND, facing=f)
        g.box((0, f * 0.19, 5.9), (1.3, 0.03, 0.2), "white")
        g.box((0, f * 0.19, 5.5), (0.9, 0.03, 0.12), "white")
        if g.hd:
            g.box((0, f * 0.24, 8.13), (1.6, 0.04, 0.06), "lamp")
            for k in range(3):                                                                # services (pictos neutres)
                g.box((-0.45 + k * 0.45, f * 0.19, 3.4), (0.32, 0.03, 0.32), "white")


reg(dict(id="dealer_sign", file="Dealer_Sign", cat="street", tags=["car", "sign"], seed=521, fp=(2.2, 0.9),
         collider="box", build=dealer_sign))


def car_podium(g):
    """Podium d'exposition : galette ronde biseautée de 5,6 m, dessus clair, plateau central légèrement en relief."""
    n = lv(g, 48, 32, 16)
    g.frustum((0, 0, -0.15), 2.8, 2.8, 0.35, n, "metal_dark", cap1=False, auto=False)        # flanc (-0,15 à 0,20)
    g.frustum((0, 0, 0.2), 2.8, 2.66, 0.08, n, "metal", cap1=True, auto=False)              # biseau, dessus à 0,28
    if g.lod < 2:
        g.frustum((0, 0, 0.28), 2.36, 2.3, 0.02, n, "plaster_white", auto=False)            # plateau, dessus à 0,30
    if g.hd:
        for k in range(12):                                                                  # spots encastrés du biseau
            a = 2 * math.pi * k / 12
            g.box((math.cos(a) * 2.74, math.sin(a) * 2.74, 0.24), (0.12, 0.12, 0.03), "lamp", rotz=a)


reg(dict(id="car_podium", file="Car_Podium", cat="street", tags=["car"], seed=522, fp=(5.6, 5.6), collider="none",
         build=car_podium))
