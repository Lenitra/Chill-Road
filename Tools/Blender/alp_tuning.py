"""Pack alpin : garage de tuning (atelier de préparation, bureau vitré), totem d'enseigne.

Repère du pack : origine au sol au centre de l'emprise, façade avant = -Y (Unity +Z), mètres.
Bureau vitré à gauche (x de -15 à -7), atelier à droite (x de -7 à 15), profondeur 16 m (y de -8 à 8).
Trois baies d'atelier ; celle du milieu est ouverte sur une alvéole éclairée (pont élévateur, servante, pneus),
les deux autres sont fermées par des portes sectionnelles. Atelier sombre à liserés néon, bandeau à damier, panneau de
marque sans texte (emblème : disque et clé).
Anti-z-fighting : pièces de façade à des profondeurs différentes (au moins 3 cm) ; les revêtements intérieurs de
l'alvéole se tiennent à 2 cm des volumes qui les portent."""
import math
from mathutils import Matrix, Vector
import alp_lib as A
from alp_lib import reg, lv
from alp_kit import Fac

Z0 = 0.12                      # dessus des fondations
X0, X1 = -15.0, 15.0
OX1 = -7.0                     # bureau de X0 à OX1, atelier de OX1 à X1
D = 16.0
YF = -D / 2                    # façade avant
OH = 4.6                       # hauteur du bureau
WH = 6.6                       # hauteur de l'atelier
BAYS = (-2.6, 3.6, 9.8)        # axes des baies (x)
OPEN = 1                       # baie ouverte (alvéole)
DW, DH = 4.6, 4.4              # ouverture des baies
AX0, AX1 = 0.7, 6.5            # alvéole : largeur intérieure
AY1 = 3.0                      # fond de l'alvéole
AZ = 5.3                       # plafond de l'alvéole
BODY = "metal_dark"            # bardage de l'atelier
BRAND = "orange"               # couleur de la marque (sans texte)
NEON = "neon_blue"


def disc(g, c, r, depth, n, color, facing=-1):
    """Disque (cylindre plat) plaqué sur un plan vertical, tourné vers -Y (facing = -1) ou +Y."""
    m = g.mark()
    g.frustum((0, 0, 0), r, r, depth, n, color, auto=False)
    rot = Matrix.Rotation(math.pi / 2 * (1 if facing < 0 else -1), 4, 'X')
    g.transform(g.since(m), Matrix.Translation(c) @ rot)


def on_floor(g, c, size, color):
    """Pièce posée sur les fondations, sans face de dessous."""
    return g.slab(c, size, color, open='-z')


def emblem(g, c, r, facing=-1, n=24):
    """Emblème de la marque : disque blanc, couronne à la couleur de la marque, clé plate en travers."""
    x, y, z = c
    f = facing
    disc(g, (x, y, z), r, 0.05, n, "white", facing=f)
    disc(g, (x, y + f * 0.05, z), r * 0.74, 0.04, n, BRAND, facing=f)
    if g.lod == 2:
        return
    # clé : manche en diagonale, deux mâchoires
    yk = y + f * 0.135
    rot = Matrix.Rotation(math.radians(38) * f, 3, 'Y')
    d = rot @ Vector((1.0, 0.0, 0.0))
    g.box((x, yk, z), (r * 1.25, 0.03, r * 0.2), "black", rot=rot)
    for s in (-1, 1):
        g.box((x + s * r * 0.5 * d.x, yk, z + s * r * 0.5 * d.z), (r * 0.36, 0.03, r * 0.36), "black", rot=rot)


def checker(g, x0, x1, z, h, y, facing=-1, step=0.5):
    """Bandeau à damier (deux rangées) plaqué en y, tourné vers facing."""
    n = int(round((x1 - x0) / step))
    step = (x1 - x0) / n
    for k in range(n):
        for row in (0, 1):
            col = "white" if (k + row) % 2 == 0 else "black"
            g.box((x0 + step * (k + 0.5), y, z + (row - 0.5) * h / 2), (step, 0.03, h / 2), col)


# ----------------------------------------------------------------- bureau
def office(g):
    """Bureau d'accueil : volume blanc, grande vitrine éclairée, porte vitrée, auvent orange."""
    x0, x1 = X0, OX1
    on_floor(g, ((x0 + x1) / 2, 0.3, (Z0 + OH) / 2), (x1 - x0, D - 0.6, OH - Z0), "plaster_white")   # façade à -7,4
    yg = YF + 0.6                                                                                # vitrine en retrait
    on_floor(g, ((x0 + x1) / 2 - 0.05, yg - 0.02, (Z0 + 3.2) / 2), (x1 - x0 - 0.5, 0.04, 3.2 - Z0), "window_lit")
    g.box(((x0 + x1) / 2 - 0.05, yg - 0.02, (3.4 + OH - 0.3) / 2), (x1 - x0 - 0.5, 0.04, OH - 0.3 - 3.4), "glass_blue")
    if g.lod == 2:
        g.box(((x0 + x1) / 2, YF - 0.5, 3.6), (x1 - x0 + 0.2, 2.4, 0.2), BRAND)
        return
    g.box(((x0 + x1) / 2, yg - 0.08, 3.3), (x1 - x0 - 0.54, 0.14, 0.2), "metal_dark")            # traverse
    on_floor(g, ((x0 + x1) / 2, yg - 0.1, Z0 + 0.12), (x1 - x0 - 0.58, 0.18, 0.24), "concrete_dark")  # allège
    for x in (-13.0, -11.0, -9.0):
        on_floor(g, (x, yg - 0.09, (Z0 + OH - 0.3) / 2), (0.16, 0.16, OH - 0.3 - Z0), "metal_dark")  # montants
    # porte vitrée (entre deux montants), cadre
    xd = -12.0
    on_floor(g, (xd, yg - 0.16, (Z0 + 2.5) / 2), (1.6, 0.05, 2.5 - Z0), "window_lit")
    if g.hd:
        for s in (-1, 1):
            on_floor(g, (xd + s * 0.78, yg - 0.2, (Z0 + 2.5) / 2), (0.06, 0.04, 2.5 - Z0), "metal")
        g.box((xd, yg - 0.2, 2.47), (1.5, 0.04, 0.06), "metal")
        g.box((xd + 0.5, yg - 0.24, 1.3), (0.04, 0.05, 0.8), "metal")                            # poignée
        g.box((xd, YF - 0.5, 0.012), (2.2, 1.2, 0.024), "rubber")                                # tapis
    # auvent sur la vitrine : dalle, rive orange, deux tirants
    g.box(((x0 + x1) / 2, YF - 0.9, 3.65), (x1 - x0 + 0.2, 3.0, 0.22), "plaster_white")
    g.box(((x0 + x1) / 2, YF - 2.42, 3.65), (x1 - x0 + 0.3, 0.12, 0.36), BRAND)
    if g.hd:
        for x in (-14.0, -8.0):
            g.tube([(x, YF - 2.3, 3.78), (x, yg, 4.45)], [0.035, 0.035], 6, "metal")
        g.box(((x0 + x1) / 2, YF - 1.6, 3.53), (x1 - x0 - 1.4, 0.14, 0.03), "lamp")
    # acrotère, rive néon
    g.box(((x0 + x1) / 2, 0.3, OH + 0.25), (x1 - x0, D - 0.6, 0.5), "plaster_white")
    g.box(((x0 + x1) / 2, YF + 0.57, OH + 0.12), (x1 - x0 - 0.04, 0.06, 0.08), NEON)
    # côté gauche : deux fenêtres
    side = Fac(g, 'x', X0, -1)
    for y in (-2.5, 3.0):
        side.slab(y, 2.4, 2.0, 1.3, 0.05, "window_lit")
        if g.hd:
            side.slab(y, 1.7, 2.2, 0.08, 0.12, "concrete_dark")


# ----------------------------------------------------------------- atelier
def workshop_volume(g):
    """Volumes pleins de l'atelier autour de l'alvéole ouverte, et l'alvéole elle-même."""
    hz = (Z0 + WH) / 2
    on_floor(g, ((OX1 + AX0) / 2, 0, hz), (AX0 - OX1, D, WH - Z0), BODY)                       # gauche de l'alvéole
    on_floor(g, ((AX1 + X1) / 2, 0, hz), (X1 - AX1, D, WH - Z0), BODY)                         # droite
    on_floor(g, ((AX0 + AX1) / 2, (AY1 + D / 2) / 2, hz), (AX1 - AX0, D / 2 - AY1, WH - Z0), BODY)  # fond
    g.box(((AX0 + AX1) / 2, (YF + AY1) / 2, (AZ + WH) / 2), (AX1 - AX0, AY1 - YF, WH - AZ), BODY)   # plafond
    # retours de façade et linteau de la baie ouverte (la baie est plus étroite que l'alvéole)
    u = BAYS[OPEN]
    for a, b in ((AX0, u - DW / 2), (u + DW / 2, AX1)):
        on_floor(g, ((a + b) / 2, YF + 0.15, (Z0 + AZ) / 2), (b - a, 0.3, AZ - Z0), BODY)
    g.box((u, YF + 0.15, (Z0 + DH + AZ) / 2), (DW, 0.3, AZ - Z0 - DH), BODY)
    if g.lod == 2:
        on_floor(g, ((AX0 + AX1) / 2, YF + 1.2, (Z0 + AZ) / 2), (AX1 - AX0, 0.1, AZ - Z0), "window_lit")
        return
    # alvéole : sol époxy, murs clairs, rampes lumineuses
    on_floor(g, (u, (YF + 0.3 + AY1) / 2, (Z0 + 0.15) / 2), (AX1 - AX0 - 0.04, AY1 - YF - 0.34, 0.15 - Z0), "concrete")
    yl0, yl1 = YF + 0.32, AY1 - 0.02
    for x in (AX0 + 0.02, AX1 - 0.02):
        on_floor(g, (x, (yl0 + yl1) / 2, (0.15 + AZ - 0.05) / 2), (0.04, yl1 - yl0, AZ - 0.2), "plaster_white")
    on_floor(g, (u, AY1 - 0.02, (0.15 + AZ - 0.05) / 2), (AX1 - AX0 - 0.1, 0.04, AZ - 0.2), "plaster_white")
    for x in (u - 1.6, u + 1.6):
        g.box((x, (yl0 + yl1) / 2, AZ - 0.03), (0.2, yl1 - yl0 - 0.6, 0.06), "lamp")
    # bande orange à hauteur d'épaule sur le fond
    g.box((u, AY1 - 0.06, 1.4), (AX1 - AX0 - 0.2, 0.04, 0.3), BRAND)
    lift(g, u, -2.4)
    if g.hd:
        cabinet(g, AX0 + 0.5, 0.6)
        tire_rack(g, AX1 - 0.45, -0.8)
        for k, z in enumerate((0.35, 0.95)):                                                    # outils au mur du fond
            for j in range(6):
                g.box((u - 1.5 + j * 0.6, AY1 - 0.07, 2.6 + z), (0.06, 0.03, 0.42 - 0.05 * (j % 3)), "metal")
        g.box((u, AY1 - 0.05, 3.2), (3.8, 0.03, 1.5), "wood_grey")                              # panneau à outils


def lift(g, u, y):
    """Pont élévateur à deux colonnes (bras repliés au sol)."""
    n = lv(g, 8, 6, 4)
    for s in (-1, 1):
        x = u + s * 1.75
        on_floor(g, (x, y, 0.2), (0.7, 0.7, 0.1), "metal_dark")                                 # platine
        on_floor(g, (x, y, (0.25 + 3.9) / 2), (0.34, 0.42, 3.9 - 0.25), "yellow")               # colonne
        g.box((x - s * 0.24, y, 1.0), (0.14, 0.3, 0.4), "metal_dark")                           # chariot
        if g.hd:
            for e in (-1, 1):                                                                    # bras
                g.box((x - s * 0.75, y + e * 0.55, 0.95), (1.2, 0.12, 0.1), "metal",
                      rotz=math.radians(e * s * 28))
                g.frustum((x - s * 1.3, y + e * 0.85, 1.0), 0.12, 0.12, 0.06, n, "rubber", auto=False)
            g.box((x, y - 0.22, 3.4), (0.24, 0.03, 0.5), "black")                               # boîtier de commande
    g.box((u, y, 3.95), (3.7, 0.3, 0.2), "yellow")                                             # traverse haute


def cabinet(g, x, y):
    """Servante d'atelier rouge (tiroirs) contre le mur de gauche de l'alvéole."""
    on_floor(g, (x, y, (0.15 + 1.1) / 2), (0.6, 1.2, 0.95), "sign_red")
    for k in range(4):
        g.box((x + 0.31, y, 0.35 + k * 0.2), (0.03, 1.0, 0.03), "metal")
    g.box((x, y, 1.12), (0.66, 1.26, 0.04), "black")


def tire_rack(g, x, y):
    """Étagère à pneus contre le mur de droite."""
    for z in (0.6, 1.6):
        g.box((x, y, z), (0.6, 2.4, 0.05), "metal")
    for s in (-1, 1):
        for e in (-1, 1):
            on_floor(g, (x + s * 0.27, y + e * 1.17, (0.15 + 2.2) / 2), (0.05, 0.05, 2.05), "metal_dark")
    for z in (0.62, 1.62):
        for k in range(4):
            m = g.mark()
            g.frustum((0, 0, -0.11), 0.33, 0.33, 0.22, 12, "rubber", auto=False)
            g.transform(g.since(m), Matrix.Translation((x, y - 0.9 + k * 0.6, z + 0.35)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))


def bay_doors(g):
    """Baies : tableaux, deux portes sectionnelles fermées, liserés néon, appliques."""
    f = Fac(g, 'y', YF, -1)
    for i, u in enumerate(BAYS):
        closed = i != OPEN
        if g.lod == 2:
            if closed:
                f.quad(u, Z0 + DH / 2, DW, DH, "roof_metal", 0.04)
            continue
        for s in (-1, 1):
            f.slab(u + s * (DW / 2 + 0.09), Z0 + (DH + 0.18) / 2, 0.18, DH + 0.18, 0.16, "concrete_dark")   # tableau
        f.slab(u, Z0 + DH + 0.09, DW + 0.36, 0.18, 0.16, "concrete_dark")
        f.slab(u, Z0 + DH + 0.32, DW + 0.2, 0.06, 0.05, NEON, out=0.0)                          # liseré néon
        if closed:
            f.slab(u, Z0 + DH / 2, DW, DH, 0.06, "roof_metal_b" if g.hd else "roof_metal")       # tablier
            f.slab(u, Z0 + 3.1, DW - 0.6, 0.45, 0.02, "window_lit", out=0.06)                    # hublots
            if g.hd:
                for k in range(1, 12):
                    z = Z0 + 0.36 * k
                    if abs(z - (Z0 + 3.1)) < 0.32:
                        continue
                    f.slab(u, z, DW - 0.04, 0.035, 0.02, "metal_dark", out=0.06)                 # lames
                f.box(u + 1.8, Z0 + 1.2, 0.3, 0.12, 0.05, "metal", out=0.08)                     # poignée
        if g.hd:
            f.box(u, Z0 + DH + 0.62, 0.6, 0.14, 0.26, "metal_dark")                              # applique
            f.box(u, Z0 + DH + 0.54, 0.5, 0.03, 0.2, "lamp")


def facade_top(g):
    """Haut de façade de l'atelier : bandeau à damier, panneau de marque, rive néon, acrotère."""
    x0, x1 = OX1, X1
    # bandeau orange sur toute la largeur, damier par-dessus (hd)
    g.box(((x0 + x1) / 2, YF - 0.04, 5.55), (x1 - x0 - 0.04, 0.08, 0.5), BRAND)
    if g.hd:
        checker(g, x0 + 0.3, x1 - 0.3, 5.55, 0.36, YF - 0.095, step=0.4)
    # panneau de marque au-dessus de la baie ouverte, sur l'acrotère
    u = BAYS[OPEN]
    g.box((u, YF - 0.12, WH + 0.95), (10.0, 0.24, 1.9), "black")                                # avant à -8,24
    if g.lod == 2:
        return
    g.box((u, YF - 0.12, WH + 1.95), (10.1, 0.28, 0.1), NEON)                                  # néons haut et bas
    g.box((u, YF - 0.12, WH - 0.05), (10.1, 0.28, 0.1), NEON)
    emblem(g, (u - 3.6, YF - 0.24, WH + 0.95), 0.72, n=lv(g, 32, 20))
    g.box((u + 1.1, YF - 0.255, WH + 1.2), (5.6, 0.03, 0.42), BRAND)                            # bandeaux (sans texte)
    g.box((u + 0.5, YF - 0.255, WH + 0.62), (4.4, 0.03, 0.2), "white")
    # acrotère
    g.box(((x0 + x1) / 2, YF + 0.125, WH + 0.25), (x1 - x0, 0.25, 0.5), BODY)
    g.box(((x0 + x1) / 2, -YF - 0.125, WH + 0.25), (x1 - x0, 0.25, 0.5), BODY)
    g.box((X1 - 0.125, 0, WH + 0.25), (0.25, D - 0.5, 0.5), BODY)
    g.box((x0 + 0.125, 0, WH + 0.25), (0.25, D - 0.5, 0.5), BODY)
    g.box((X1 + 0.03, 0, WH - 0.2), (0.06, D - 0.2, 0.08), NEON)                               # rive néon latérale
    if g.hd:
        for (x, y, w, d, h) in ((-4.0, 3.0, 2.4, 1.6, 1.1), (11.0, 4.5, 1.8, 1.2, 0.9)):
            g.box((x, y, WH + h / 2), (w, d, h), "metal")                                       # groupes de clim
            g.box((x, y - d / 2 - 0.01, WH + h * 0.55), (w * 0.7, 0.02, h * 0.6), "metal_dark")
        g.tube([(8.0, 2.0, WH), (8.0, 2.0, WH + 2.2)], [0.25, 0.25], 10, "metal")                # extracteur
        g.frustum((8.0, 2.0, WH + 2.2), 0.4, 0.4, 0.12, 10, "metal_dark", auto=False)
        for x in (OX1 + 0.15, X1 - 0.15):                                                        # descentes d'eau
            g.tube([(x, -YF + 0.1, WH), (x, -YF + 0.1, 0.2)], [0.07, 0.07], 8, "roof_metal")


def yard(g):
    """Abords : piles de pneus, fûts, borne de gonflage (hd)."""
    n = 12
    for (x, y, k) in ((-6.3, YF - 1.0, 4), (-5.55, YF - 1.3, 3), (14.2, YF - 1.2, 5)):
        for j in range(k):
            g.frustum((x, y, 0.02 + j * 0.24), 0.34, 0.34, 0.23, n, "rubber", auto=False)
            g.frustum((x, y, 0.02 + j * 0.24 + 0.23), 0.2, 0.2, 0.005, n, "metal_dark", cap0=False, auto=False)
    for (x, y, c) in ((X1 + 0.6, 2.0, "sign_blue"), (X1 + 0.6, 2.75, "sign_red"), (X1 + 0.55, 6.6, BRAND)):
        g.frustum((x, y, 0.0), 0.3, 0.3, 0.9, n, c, auto=False)
        g.frustum((x, y, 0.9), 0.31, 0.31, 0.04, n, "metal_dark", auto=False)
    g.box((-6.6, YF - 2.6, 0.7), (0.4, 0.3, 1.4), "sign_red")                                   # borne de gonflage
    g.box((-6.6, YF - 2.77, 1.0), (0.26, 0.04, 0.2), "window_lit")


def tuning_garage(g):
    g.box((0, 0, (Z0 - 0.9) / 2), (X1 - X0 + 0.3, D + 0.3, Z0 + 0.9), "concrete")             # fondations
    office(g)
    workshop_volume(g)
    bay_doors(g)
    facade_top(g)
    if g.lod < 2:
        side = Fac(g, 'x', X1, 1)
        for y in (-4.0, 1.0, 5.5):
            side.slab(y, 4.4, 2.4, 0.8, 0.05, "window")
        side.slab(0.0, 2.6, D - 2.0, 0.12, 0.05, BRAND)
        back = Fac(g, 'y', -YF, 1)                                                              # façade arrière
        back.slab((OX1 + X1) / 2, 2.6, X1 - OX1 - 1.0, 0.12, 0.05, BRAND)
        for x in (-3.0, 5.0, 11.0):
            back.slab(x, 4.4, 2.4, 0.8, 0.05, "window")
        back.slab(1.0, Z0 + 1.1, 1.0, 2.2, 0.06, "metal")                                       # porte de service
        if g.hd:
            back.slab(1.0, Z0 + 2.45, 1.4, 0.1, 0.5, "metal_dark")                              # marquise
    if g.hd:
        yard(g)


reg(dict(id="tuning_garage", file="Tuning_Garage", cat="landmark", tags=["service", "shop", "car"], seed=530,
         fp=(30.0, 16.0), collider="box", build=tuning_garage,
         note="bureau x -15..-7 (porte x=-12, auvent 2,4 m), atelier x -7..15 (baies x=-2,6 / 3,6 ouverte / 9,8), "
              "alvéole ouverte x 0,7..6,5, y -8..3"))


def tuning_sign(g):
    """Totem d'enseigne : socle, mât central, panneau noir à liserés néon, emblème, bandeau à damier."""
    g.box((0, 0, 0.0), (1.8, 0.9, 0.8), "concrete")                                          # socle (-0,4 à 0,4)
    g.box((0, 0, 1.6), (0.36, 0.36, 2.4), "metal_dark")                                       # mât (0,4 à 2,8)
    g.box((0, 0, 4.9), (2.4, 0.4, 4.2), "black")                                              # panneau (2,8 à 7,0)
    if g.lod == 2:
        return
    for f in (-1, 1):
        emblem(g, (0, f * 0.2, 6.0), 0.72, facing=f, n=lv(g, 28, 18))
        g.box((0, f * 0.215, 4.6), (1.8, 0.03, 0.34), BRAND)
        g.box((0, f * 0.215, 4.1), (1.3, 0.03, 0.16), "white")
        if g.hd:
            for k in range(6):
                for row in (0, 1):
                    col = "white" if (k + row) % 2 == 0 else "black"
                    g.box((-1.08 + 0.36 * (k + 0.5), f * 0.215, 3.25 + row * 0.18),
                          (0.36, 0.03, 0.18), col)
    for s in (-1, 1):
        g.box((s * 1.23, 0, 4.9), (0.06, 0.44, 4.2), NEON)                                      # liserés latéraux
    g.box((0, 0, 7.03), (2.52, 0.44, 0.06), NEON)


reg(dict(id="tuning_sign", file="Tuning_Sign", cat="street", tags=["car", "sign"], seed=531, fp=(2.4, 0.9),
         collider="box", build=tuning_sign))
