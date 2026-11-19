"""Pack alpin : tremplins de cascade (Tools/Map/map_stunts.py les pose au bord des routes, StuntJumps.cs fait le reste).

Deux habillages sobres (2026-11-17, à la place du tremplin « chantier » rouge, blanc, jaune et noir), choisis par
map_stunts.py selon l'environnement :
  stunt_ramp_stone : remblai — murs de pierres sèches, piste de terre battue bordée d'herbe, traverse de bois au nez
                     (campagne, routes de col) ;
  stunt_ramp_road  : ouvrage routier — béton banché, enrobé, glissière mixte bois-métal, balises (autoroute, villages).
Un seul rappel discret de l'orange des cascades par modèle (fanion, têtes de balises).

Repère du pack : origine au sol au milieu du tablier, mètres. Les voitures roulent vers -Y (Unity +Z) : entrée au ras
du sol en y = +L/2, nez en y = -L/2.
Profil (commun aux deux) : arc de cercle de rayon R (départ tangent au sol, pour ne pas talonner à haute vitesse)
jusqu'à 12°, puis plan incliné droit jusqu'au nez (la voiture décolle sans vitesse de tangage). 17 m de long, 2,05 m
au nez. Le niveau lointain (LOD2) n'est que le volume plein (tablier + caisson) : c'est lui qui sert de collision dans
le jeu (collider « mesh_low »), les roues n'accrochent donc ni bordures ni décors.
Anti-z-fighting : bordures, bandes et moellons sont à 2-3 cm au moins des surfaces qui les portent."""
import math
from mathutils import Matrix
import alp_lib as A
from alp_lib import reg, lv

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


def volume(g, top, body):
    """Tablier courbe et caisson plein (enterré d'un mètre : le sol peut être plus bas que prévu)."""
    deck = [(L / 2 - s, h(s)) for s in STATIONS]
    g.prism(deck + [(y, z - DECK) for (y, z) in reversed(deck)], -WT / 2, WT / 2, top, axis='x')
    inner = [(L / 2 - s, h(s) - DECK) for s in STATIONS[:-1]] + [(-L / 2 + 0.15, h(L - 0.15) - DECK)]
    g.prism(inner + [(-L / 2 + 0.15, -1.0), (L / 2, -1.0)], -XB, XB, body, axis='x')


def wall_h(s):
    """Hauteur du flanc du caisson (sous le tablier) à l'abscisse s."""
    return max(h(s) - DECK, 0.0)


def y_of(s):
    return L / 2 - s


def stone_courses(g, sx, color_a, color_b, course=0.3, x_face=None, s_from=0.6, s_to=L - 0.2, z_max=HT):
    """Moellons sur un flanc du caisson (sx = ±1) : assises décalées, longueurs et saillies variées."""
    xf = XB if x_face is None else x_face
    z = -0.05
    row = 0
    while True:
        s = s_from - (0.35 if row % 2 else 0.0)
        top_ok = False
        while s < s_to:
            ln = 0.45 + 0.4 * g.h(sx, row, round(s, 2))
            hz = wall_h(max(s, s_from)) - 0.04        # le moellon ne dépasse jamais sous le tablier
            ht = min(course - 0.04, hz - z, z_max - z)
            if ht > 0.08 and s + ln > s_from:
                top_ok = True
                out = 0.04 + 0.05 * g.h(sx, row, round(s, 2), 2)
                c = (sx * (xf + out / 2), y_of(s + ln / 2), z + ht / 2)
                g.box(c, (out + 0.04, ln - 0.05, ht), color_a if g.h(row, round(s, 2), 3) < 0.6 else color_b)
            s += ln
        if not top_ok and z > 0.3:
            break
        z += course
        row += 1
        if z > min(HT, z_max - 0.08):
            break


def lantern_post(g, x, y, height, wood, cap):
    """Poteau de bois, petit fanion orange et lanterne (visible la nuit)."""
    g.box((x, y, 0.15), (0.3, 0.3, 0.3), "stone_dark")
    g.box((x, y, height / 2), (0.14, 0.14, height), wood)
    g.box((x, y, height + 0.04), (0.22, 0.22, 0.08), cap)
    g.box((x, y, height + 0.17), (0.14, 0.14, 0.18), "lamp")
    g.box((x, y, height + 0.31), (0.24, 0.24, 0.06), cap)
    # fanion côté entrée (face à l'arrivée des voitures), qui claque au vent (shader Toon, FlagWave)
    g.cloth((x, y + 0.07, height - 0.29), (0, 0.5, 0), (0, 0, -0.32), lv(g, 4, 3, 1), lv(g, 2, 1, 1), "orange", amp=0.06)


def boulder(g, c, r, color="rock", squash=0.6):
    f = g.ico((c[0], c[1], c[2] + r * squash * 0.55), r, 1, color, scale=(1.0, 0.85, squash))
    g.jitter(f, r * 0.12, keep_bottom=None)
    return f


# -------------------------------------------------------------------------------- remblai de pierre
def stunt_ramp_stone(g):
    volume(g, "dirt", "stone_warm")
    if g.lod == 2:
        return
    # piste de terre battue au milieu, accotements herbeux sur les rives
    for k, (s, ln, a, hm) in enumerate(segments()):
        for sx in (-1, 1):
            c = (sx * (WT / 2 - 0.42 - 0.02 * (k % 2)), y_of(s) + math.sin(a) * 0.01, hm - math.cos(a) * 0.01)
            g.box(c, (0.84, ln + 0.01, 0.06), "grass", rot=tilt(a))
    if g.hd:
        # touffes d'herbe le long des rives
        for k in range(26):
            s = 0.8 + (L - 1.6) * (k + g.h(k, 7) * 0.6) / 26
            for sx in (-1, 1):
                x = sx * (WT / 2 - 0.25 - 0.3 * g.h(k, sx))
                a = slope(s)
                base = (x, y_of(s) + math.sin(a) * 0.02, h(s) + 0.02)
                for b in range(3):
                    ang = 2.1 * b + g.h(k, sx, b) * 1.5
                    g.blade(base, (math.cos(ang) * 0.35, math.sin(ang) * 0.35, 1.0), 0.28 + 0.12 * g.h(b, k),
                            0.09, "grass", bend=0.3)
        # ornières plus sombres (terre tassée) au milieu de la piste
        for sx in (-1, 1):
            for k, (s, ln, a, hm) in enumerate(segments()):
                up = 0.005 + 0.02 * (k % 2)               # bouts qui se recouvrent : 2 cm d'écart en hauteur
                c = (sx * 0.9, y_of(s) + math.sin(a) * up, hm + math.cos(a) * up)
                g.box(c, (0.55 + 0.03 * (k % 2), ln + 0.01, 0.07), "bark", rot=tilt(a))
    # murs de pierres sèches
    for sx in (-1, 1):
        if g.hd:
            stone_courses(g, sx, "stone_warm", "stone")
        else:
            for row in range(3):
                g.box((sx * (XB + 0.04), y_of(L * 0.62), 0.15 + 0.35 * row), (0.08, L * 0.7 - row * 3.5, 0.3),
                      "stone" if row % 2 else "stone_warm")
    # face arrière : mur de pierre plein, chaperon
    y = -L / 2 + 0.15
    top = h(L) - DECK
    if g.hd:
        z, row = 0.0, 0
        while z < top - 0.12:
            x = -XB - (0.3 if row % 2 else 0.0)
            while x < XB:
                ln = 0.5 + 0.4 * g.h(row, round(x, 2), 5)
                x1 = min(x + ln, XB)
                x0 = max(x, -XB)
                ht = min(0.28, top - 0.04 - z)
                if x1 - x0 > 0.15 and ht > 0.08:
                    out = 0.05 + 0.05 * g.h(row, round(x, 2), 6)
                    g.box(((x0 + x1) / 2, y - out / 2, z + ht / 2), (x1 - x0 - 0.05, out + 0.04, ht),
                          "stone_warm" if g.h(row, round(x, 2), 8) < 0.6 else "stone")
                x += ln
            z += 0.3
            row += 1
    # traverse de chemin de fer au nez (bois sombre), posée sur la terre
    on_deck(g, L - 0.2, 0.0, (WT - 0.2, 0.34, 0.16), "wood_dark", lift=-0.06)
    # entrée : la terre se raccorde au sol, gros blocs de part et d'autre
    for sx in (-1, 1):
        boulder(g, (sx * (WT / 2 + 0.8), L / 2 + 0.6, 0.0), 0.75, "rock")
        boulder(g, (sx * (WT / 2 + 1.4), L / 2 - 1.2, 0.0), 0.5, "rock_light")
        boulder(g, (sx * (WT / 2 + 0.65), -L / 2 + 1.6, 0.0), 0.6, "rock")
    # repère : un seul poteau de bois au nez (fanion orange, lanterne)
    lantern_post(g, -(WT / 2 + 0.4), -L / 2 + 0.6, HT + 1.4, "wood", "wood_dark")


# ---------------------------------------------------------------------------------- ouvrage routier
def stunt_ramp_road(g):
    volume(g, "asphalt", "concrete")
    if g.lod == 2:
        return
    # bordures de béton (rive) au lieu des bordures rouges et blanches
    for k, (s, ln, a, hm) in enumerate(segments()):
        for sx in (-1, 1):
            c = (sx * (WT / 2 - 0.16 - 0.02 * (k % 2)), y_of(s) - math.sin(a) * 0.07, hm + math.cos(a) * 0.07)
            g.box(c, (0.32, ln + 0.01, 0.14), "concrete", rot=tilt(a))
    # joints de banchage : bandes horizontales en léger retrait (béton plus sombre)
    for sx in (-1, 1):
        for z in (0.45, 0.95, 1.45):
            s0 = next((s for s in [x * 0.1 for x in range(171)] if wall_h(s) > z + 0.12), None)
            if s0 is None:
                continue
            g.box((sx * (XB + 0.008), y_of((s0 + L - 0.2) / 2), z), (0.03, L - 0.2 - s0, 0.05), "concrete_dark")
        # semelle en pied (débord de 8 cm)
        g.box((sx * (XB + 0.05), y_of(L / 2), 0.07), (0.12, L - 0.4, 0.2), "concrete_dark")
    # face arrière : béton, joints
    y = -L / 2 + 0.15
    for z in (0.45, 0.95, 1.45):
        g.box((0.0, y - 0.008, z), (2 * XB - 0.1, 0.03, 0.05), "concrete_dark")
    # nez : cornière d'acier, ligne de rive blanche usée sur l'enrobé (seule peinture)
    on_deck(g, L - 0.18, 0.0, (WT - 0.66, 0.32, 0.04), "metal", lift=0.02)
    for sx in (-1, 1):
        for k in range(lv(g, 14, 7)):
            s = 0.6 + (L - 1.4) * k / lv(g, 14, 7)
            on_deck(g, s + 0.45, sx * (WT / 2 - 0.42), (0.12, 0.9, 0.02), "sign_white", lift=0.0)
    # glissière mixte bois-métal sur la partie haute (s > 6), poteaux d'acier, lisse en rondin
    post = lv(g, 1.6, 3.2)
    ss = [6.0 + post * i for i in range(int((L - 6.4) / post) + 1)] + [L - 0.3]
    for sx in (-1, 1):
        x = sx * (WT / 2 - 0.16)
        pts = []
        for s in ss:
            a = slope(s)
            base = (x, y_of(s), h(s) + 0.12)
            g.box((x, y_of(s), h(s) + 0.4), (0.12, 0.12, 0.7), "metal_dark")
            pts.append((x - sx * 0.05, y_of(s), h(s) + 0.62))
        for p0, p1 in zip(pts, pts[1:]):
            g.limb(p0, p1, 0.1, 0.1, lv(g, 8, 6), "wood", cap0=True)
        # bout de glissière abaissé vers l'entrée
        p0 = pts[0]
        g.limb((p0[0], p0[1] + 1.6, h(4.4) + 0.1), p0, 0.1, 0.1, lv(g, 8, 6), "wood", cap0=True)
    # balises de bord de route (blanches, tête orange) à l'entrée ; tête orange sur les derniers poteaux du nez
    for sx in (-1, 1):
        for y in (L / 2 + 0.6, L / 2 - 4.0):
            x = sx * (WT / 2 + 0.5)
            g.box((x, y, 0.55), (0.12, 0.12, 1.1), "sign_white")
            g.box((x, y + 0.065, 0.9), (0.08, 0.02, 0.18), "orange")
            g.box((x, y, 1.12), (0.14, 0.14, 0.06), "black")
        s = L - 0.3
        g.box((sx * (WT / 2 - 0.16), y_of(s), h(s) + 0.78), (0.14, 0.14, 0.1), "orange")
    # blocs de béton anti-sortie à l'entrée
    for sx in (-1, 1):
        g.box((sx * (WT / 2 + 1.25), L / 2 + 1.2, 0.3), (0.6, 1.2, 0.6), "concrete_dark")


for iid, name, fn, note in (
        ("stunt_ramp_stone", "Stunt_Ramp_Stone", stunt_ramp_stone, "remblai de pierres sèches, piste de terre"),
        ("stunt_ramp_road", "Stunt_Ramp_Road", stunt_ramp_road, "ouvrage routier en béton, enrobé, glissière")):
    reg(dict(id=iid, file=name, cat="stunt", tags=["stunt", "ramp"], seed=540, fp=(WT, L), collider="mesh_low",
             grad_h=3.2, build=fn,
             note="tremplin de cascade (" + note + ") : entrée au sol à l'arrière (-Z Unity), nez à l'avant "
                  "(+Z Unity) à 2,05 m ; tablier 17 x 5,6 m ; collision = niveau lointain (volume plein)"))
