"""Pack alpin : tremplin de cascade (Tools/Map/map_stunts.py le pose au bord des routes, StuntJumps.cs fait le reste).

Repère du pack : origine au sol au milieu du tablier, mètres. Les voitures roulent vers -Y (Unity +Z) : entrée au ras
du sol en y = +L/2, nez en y = -L/2.
Profil : arc de cercle de rayon R (départ tangent au sol, pour ne pas talonner à haute vitesse) jusqu'à 12°, puis
plan incliné droit jusqu'au nez (la voiture décolle sans vitesse de tangage). 17 m de long, 2,05 m au nez.
Le niveau lointain (LOD2) n'est que le volume plein (tablier + caisson) : c'est lui qui sert de collision dans le jeu
(collider « mesh_low »), les roues n'accrochent donc ni bordures ni décors.
Anti-z-fighting : bordures, bandes et chevrons sont à 2-3 cm au moins des surfaces qui les portent."""
import math
from mathutils import Matrix
import alp_lib as A
from alp_lib import reg

L = 17.0                       # longueur du tablier (comme map_stunts.RAMP_L)
WT = 5.6                       # largeur hors tout (comme map_stunts.RAMP_W)
R = 70.0                       # rayon de l'arc
TH = math.radians(12.0)        # pente au nez
S1 = R * math.sin(TH)          # fin de l'arc (abscisse horizontale depuis l'entrée)
H1 = R * (1.0 - math.cos(TH))
HT = H1 + (L - S1) * math.tan(TH)      # hauteur du nez
DECK = 0.14                    # épaisseur du tablier
XB = WT / 2 - 0.15             # demi-largeur du caisson (le tablier déborde)
NA = 12                        # segments de l'arc
STATIONS = [S1 * k / NA for k in range(NA + 1)] + [(S1 + L) / 2, L]


def h(s):
    """Hauteur du tablier à l'abscisse s (0 = entrée, L = nez)."""
    s = min(max(s, 0.0), L)
    return R - math.sqrt(R * R - s * s) if s <= S1 else H1 + (s - S1) * math.tan(TH)


def slope(s):
    return math.asin(min(max(s, 0.0), S1) / R)


def tilt(a):
    """Rotation qui couche une pièce (longueur le long de Y) sur une pente d'angle a montant vers -Y."""
    return Matrix.Rotation(-a, 3, 'X')


def on_deck(g, s, x, size, color, lift=0.0, yaw=0.0):
    """Boîte posée sur le tablier à l'abscisse s (size : largeur, longueur le long de la pente, épaisseur)."""
    a = slope(s)
    up = lift + size[2] / 2
    c = (x, L / 2 - s + math.sin(a) * up, h(s) + math.cos(a) * up)
    rot = tilt(a) @ Matrix.Rotation(yaw, 3, 'Z')
    return g.box(c, size, color, rot=rot)


def segments():
    """Tronçons du profil : (abscisse du milieu, longueur le long de la pente, angle)."""
    for sa, sb in zip(STATIONS, STATIONS[1:]):
        dz = h(sb) - h(sa)
        yield (sa + sb) / 2, math.hypot(sb - sa, dz), math.atan2(dz, sb - sa), (h(sa) + h(sb)) / 2


def volume(g):
    """Tablier courbe et caisson plein (enterré d'un mètre : le sol peut être plus bas que prévu)."""
    top = [(L / 2 - s, h(s)) for s in STATIONS]
    g.prism(top + [(y, z - DECK) for (y, z) in reversed(top)], -WT / 2, WT / 2, "wood_honey", axis='x')
    inner = [(L / 2 - s, h(s) - DECK) for s in STATIONS[:-1]] + [(-L / 2 + 0.15, h(L - 0.15) - DECK)]
    g.prism(inner + [(-L / 2 + 0.15, -1.0), (L / 2, -1.0)], -XB, XB, "metal_dark", axis='x')


def kerbs(g):
    """Bordures rouges et blanches le long des deux rives du tablier."""
    for k, (s, ln, a, hm) in enumerate(segments()):
        for sx in (-1, 1):
            # une bordure sur deux rentrée de 2,5 cm : les bouts qui se recouvrent ne sont jamais coplanaires
            c = (sx * (WT / 2 - 0.18 - 0.025 * (k % 2)), L / 2 - s - math.sin(a) * 0.06, hm + math.cos(a) * 0.06)
            g.box(c, (0.3, ln + 0.01, 0.12), "sign_red" if k % 2 == 0 else "sign_white", rot=tilt(a))
            if g.hd and k % 2 == 1:                         # plots lumineux (visibles la nuit)
                g.box((c[0], c[1], c[2] + 0.09), (0.14, 0.14, 0.07), "lamp", rot=tilt(a))


def side_bands(g):
    """Flancs du caisson : bande de danger jaune et noire sous le tablier, montants, chevrons près du nez."""
    for k, (s, ln, a, hm) in enumerate(segments()):
        for sx in (-1, 1):
            # un tronçon sur deux en relief de 2,5 cm : les bouts qui se recouvrent ne sont jamais coplanaires
            c = (sx * (XB + 0.03 + 0.025 * (k % 2)), L / 2 - s, hm - DECK - 0.2)
            g.box(c, (0.05, ln + 0.01, 0.36), "sign_yellow" if k % 2 == 0 else "black", rot=tilt(a))
    for s in STATIONS[4::2]:                                # montants (là où le flanc est assez haut)
        top = h(s) - DECK - 0.4
        if top < 0.25:
            continue
        for sx in (-1, 1):
            g.box((sx * (XB + 0.035), L / 2 - s, top / 2), (0.06, 0.1, top), "metal")
    # chevrons blancs pointés vers le nez
    arm, ang = 0.55, math.radians(35)
    for s in (14.85, 16.15):
        zc = (h(s) - DECK - 0.4) / 2 + 0.02
        for sx in (-1, 1):
            for e in (-1, 1):
                c = (sx * (XB + 0.03), L / 2 - s + math.cos(ang) * arm / 2, zc + e * math.sin(ang) * arm / 2)
                g.box(c, (0.05, arm, 0.17), "sign_white", rot=Matrix.Rotation(e * ang, 3, 'X'))


def deck_paint(g):
    """Chevrons peints sur le tablier (pointe vers le nez), tôle d'entrée et cornière du nez."""
    arm, ang = 1.9, math.radians(40)
    for s in (3.6, 6.8, 10.0, 13.2):
        for e in (-1, 1):
            on_deck(g, s - math.cos(ang) * arm / 2, e * math.sin(ang) * arm / 2, (0.42, arm, 0.02), "sign_white",
                    lift=0.03, yaw=-e * ang)
    on_deck(g, L - 0.22, 0.0, (WT - 0.72, 0.4, 0.03), "metal", lift=0.025)             # cornière du nez
    on_deck(g, 16.0, 0.0, (WT - 0.72, 0.3, 0.02), "sign_yellow", lift=0.03)            # ligne d'envol


def back(g):
    """Face arrière (sous le nez) : croix de contreventement, poteaux, bande de danger."""
    y = -L / 2 + 0.15 - 0.10                               # les deux diagonales et les poteaux à des profondeurs différentes
    top = h(L) - DECK - 0.5
    for e in (-1, 1):
        ye = y - 0.03 * (e > 0)
        g.beam((-e * (XB - 0.25), ye, 0.12), (e * (XB - 0.25), ye, top), 0.1, "metal", h=0.14)
    for x in (-(XB - 0.1), 0.0, XB - 0.1):
        g.box((x, y + 0.05, (top + 0.3) / 2), (0.14, 0.12, top + 0.3), "metal_dark")
    n = 8
    w = (2 * XB) / n
    for k in range(n):
        g.box((-XB + w * (k + 0.5), -L / 2 + 0.15 - 0.03, h(L) - DECK - 0.24), (w, 0.05, 0.36),
              "sign_yellow" if k % 2 == 0 else "black")


def flags(g):
    """Mâts aux coins du nez : lanterne en tête, drapeau à damier (visible de loin, éclairé la nuit)."""
    y = -L / 2 + 0.35
    top = HT + 3.3
    for sx in (-1, 1):
        x = sx * (WT / 2 + 0.3)
        g.box((x, y, (top - 0.6) / 2), (0.1, 0.1, top + 0.6), "metal")
        g.box((x, y, 0.2), (0.34, 0.34, 0.4), "concrete_dark")
        g.box((x, y, top + 0.12), (0.2, 0.2, 0.24), "lamp")
        cw, ch = 0.3, 0.26
        for i in range(4):
            for j in range(3):
                g.card((x, y + 0.06 + cw * (i + 0.5), top - 0.12 - ch * (j + 0.5)), cw, ch,
                       "white" if (i + j) % 2 == 0 else "black", rotz=math.pi / 2)


def cones(g):
    """Cônes de chantier de part et d'autre de l'entrée."""
    n = 8
    for sx in (-1, 1):
        for (dx, dy) in ((0.55, 0.4), (0.9, 2.6)):
            c = (sx * (WT / 2 + dx), L / 2 + dy, 0.0)
            g.box((c[0], c[1], 0.02), (0.42, 0.42, 0.04), "orange")
            g.frustum(c, 0.17, 0.05, 0.62, n, "orange", auto=False)
            g.frustum((c[0], c[1], 0.26), 0.135, 0.105, 0.14, n, "sign_white", cap0=False, cap1=False, auto=False)


def stunt_ramp(g):
    volume(g)
    if g.lod == 2:
        return
    kerbs(g)
    side_bands(g)
    deck_paint(g)
    back(g)
    flags(g)
    cones(g)


reg(dict(id="stunt_ramp", file="Stunt_Ramp", cat="stunt", tags=["stunt", "ramp"], seed=540, fp=(WT, L),
         collider="mesh_low", grad_h=3.2, build=stunt_ramp,
         note="tremplin de cascade : entrée au sol à l'arrière (-Z Unity), nez à l'avant (+Z Unity) à 2,05 m ; "
              "tablier 17 x 5,6 m ; collision = niveau lointain (volume plein)"))
