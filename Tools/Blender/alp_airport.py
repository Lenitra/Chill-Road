"""Pack alpin : aéroport des Trois Vallons (aérogare, tour de contrôle, hangar, avions, hélicoptère, balisage).

Repère du pack : origine au sol au centre de l'emprise, façade avant = -Y (Unity +Z), mètres.
Aéronefs : nez vers -Y, envergure le long de X. Leur collision se fait sur le dernier niveau (collider « mesh_low ») :
ce niveau ne garde que des volumes pleins et simples (fuselage, voilure, empennage, blocs de train), sans pales.
Aucun texte : l'aéroport se reconnaît à sa silhouette d'avion blanche sur fond bleu (emblem)."""
import math
import bmesh
from mathutils import Matrix, Vector
import alp_lib as A
from alp_lib import reg, lv
import alp_kit as K
from alp_kit import Fac

BRAND = "sign_blue"


# ----------------------------------------------------------------- outils
def ring_pts(y, zc, hw, hh, n, p=2.0, x0=0.0):
    """Section d'un fuselage dans le plan y : super-ellipse (p = 2 : ellipse ; plus grand : plus carrée) de centre
    (x0, zc), demi-axes hw et hh. Sommet k à l'angle (k + 0,5) × 360 / n depuis +X, vers le haut."""
    e = 2.0 / p
    out = []
    for k in range(n):
        a = 2.0 * math.pi * (k + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        out.append((x0 + hw * math.copysign(abs(c) ** e, c), y, zc + hh * math.copysign(abs(s) ** e, s)))
    return out


def elev(k, n):
    """Élévation (degrés) du milieu de la facette k d'une section : 0 = flanc, 90 = dessus, -90 = dessous."""
    return math.degrees(math.asin(max(-1.0, min(1.0, math.sin(2.0 * math.pi * (k + 1) / n)))))


def loft(g, rings, color, cap0=True, cap1=True, paint=None):
    """Peau tendue entre des sections de même nombre de points (une section d'un seul point = pointe).
    paint(i, k) : couleur de la facette k entre les sections i et i + 1 (None : couleur de base)."""
    bm = g.bm
    mk = g.mark()
    V = [[bm.verts.new(Vector(p)) for p in r] for r in rings]
    quads = []
    for i in range(len(V) - 1):
        a, b = V[i], V[i + 1]
        if len(a) == 1 and len(b) == 1:
            continue
        n = max(len(a), len(b))
        for k in range(n):
            j = (k + 1) % n
            if len(a) == 1:
                f = bm.faces.new((a[0], b[j], b[k]))
            elif len(b) == 1:
                f = bm.faces.new((a[k], a[j], b[0]))
            else:
                f = bm.faces.new((a[k], a[j], b[j], b[k]))
            quads.append((f, i, k))
    if cap0 and len(V[0]) > 2:
        bm.faces.new(list(reversed(V[0])))
    if cap1 and len(V[-1]) > 2:
        bm.faces.new(V[-1])
    faces = g.since(mk)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    g.paint(faces, color)
    if paint:
        for f, i, k in quads:
            c = paint(i, k)
            if c:
                f[g.col] = A.CI[c]
    return faces


def body(g, stations, n, color, paint=None, p=2.0, nose=None, tail=None, x0=0.0):
    """Fuselage : stations (y, centre z, demi-largeur, demi-hauteur), pointe avant / arrière facultatives (y, z)."""
    rings = [ring_pts(y, zc, hw, hh, n, p, x0) for (y, zc, hw, hh) in stations]
    shift = 0
    if nose is not None:
        rings.insert(0, [(x0, nose[0], nose[1])])
        shift = 1
    if tail is not None:
        rings.append([(x0, tail[0], tail[1])])
    fn = (lambda i, k: paint(i - shift, k)) if paint else None
    return loft(g, rings, color, paint=fn)


PROFILE = {0: [(0.0, 0.0), (0.07, 0.4), (0.3, 0.58), (0.65, 0.4), (1.0, 0.03), (0.65, -0.2), (0.3, -0.42), (0.07, -0.32)],
           1: [(0.0, 0.0), (0.15, 0.5), (0.5, 0.52), (1.0, 0.03), (0.5, -0.3), (0.15, -0.38)],
           2: [(0.0, 0.0), (0.28, 0.56), (1.0, 0.03), (0.28, -0.42)]}
PROFILE_SYM = {0: [(0.0, 0.0), (0.07, 0.4), (0.3, 0.5), (0.65, 0.34), (1.0, 0.03), (0.65, -0.34), (0.3, -0.5), (0.07, -0.4)],
               1: [(0.0, 0.0), (0.15, 0.46), (0.5, 0.42), (1.0, 0.03), (0.5, -0.42), (0.15, -0.46)],
               2: [(0.0, 0.0), (0.28, 0.5), (1.0, 0.03), (0.28, -0.5)]}


def surf(g, stations, color, axis='z', sym=False, paint=None):
    """Voilure : stations ((x, y, z) du bord d'attaque, corde vers +Y, épaisseur). axis = direction de l'épaisseur :
    'z' aile ou plan horizontal (stations le long de X), 'x' dérive (stations le long de Z).
    paint(i) : couleur du tronçon entre les stations i et i + 1."""
    prof = (PROFILE_SYM if sym else PROFILE)[g.level]
    rings = []
    for (le, c, t) in stations:
        if axis == 'z':
            rings.append([(le[0], le[1] + u * c, le[2] + v * t) for (u, v) in prof])
        else:
            rings.append([(le[0] + v * t, le[1] + u * c, le[2]) for (u, v) in prof])
    return loft(g, rings, color, paint=(lambda i, k: paint(i)) if paint else None)


def wheel(g, c, r, w, n, color="rubber", hub="metal"):
    """Roue d'axe X : pneu, et moyeu clair au niveau détaillé."""
    f = g.limb((c[0] - w / 2, c[1], c[2]), (c[0] + w / 2, c[1], c[2]), r, r, n, color, cap0=True, cap1=True, rot=0.0,
               auto=False)
    if hub and g.hd:
        g.limb((c[0] - w / 2 - 0.012, c[1], c[2]), (c[0] + w / 2 + 0.012, c[1], c[2]), r * 0.5, r * 0.5, n, hub, cap0=True,
               cap1=True, rot=0.0, auto=False)
    return f


def hoop(g, st, n, color, r=0.022, grow=1.012, min_elev=10.0):
    """Arceau qui suit le haut d'une section de fuselage (cadre de verrière)."""
    y, zc, hw, hh = st
    pts = [q for q in ring_pts(y, zc, hw * grow, hh * grow, n) if q[2] > zc + hh * math.sin(math.radians(min_elev))]
    pts.sort(key=lambda q: -q[0])
    if len(pts) > 1:
        g.tube(pts, [r] * len(pts), 5, color, cap_start=True, cap_end=True)


AIRCRAFT_FUSE = [(-1.0, -0.07), (0.72, -0.1), (1.0, 0.0), (0.72, 0.1), (-1.0, 0.07)]
AIRCRAFT_WING = [(0.28, 0.09), (-0.3, 0.98), (-0.56, 0.98), (-0.2, 0.09)]
AIRCRAFT_TAIL = [(-0.7, 0.065), (-0.93, 0.42), (-1.08, 0.42), (-0.96, 0.065)]


def emblem(f, u, z, s, color="white", out=0.03, ang=25.0):
    """Silhouette d'avion vue de dessus sur une façade (Fac), centrée en (u, z), demi-longueur s, nez vers +u et relevé
    de « ang » degrés."""
    ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))

    def T(x, y):
        return (u + (x * ca - y * sa) * s, z + (x * sa + y * ca) * s)
    f.poly([T(x, y) for x, y in AIRCRAFT_FUSE], color, out + 0.013)
    for m in (1, -1):
        f.poly([T(x, m * y) for x, y in AIRCRAFT_WING], color, out)
        f.poly([T(x, m * y) for x, y in AIRCRAFT_TAIL], color, out)


def on_floor(g, c, size, color):
    return g.slab(c, size, color, open='-z')


# ================================================================= bâtiments
HALL = 12.0                    # demi-largeur du hall central (x)
TZ0 = 0.35                     # dessus du soubassement
T_LOW, T_HIGH = 6.0, 8.0       # hauteur des murs du hall côté ville (-Y) et côté piste (+Y)
T_Y = 7.9                      # plan des façades du hall
WING_Y, WING_H = 7.2, 4.5      # ailes basses : demi-profondeur, hauteur


def _glazing(g, f, top, lit_top=3.1):
    """Mur-rideau d'une façade du hall : allège, bande basse éclairée la nuit, traverse, vitrage haut, poteaux de
    bois lamellé, sablière."""
    w = 2 * HALL - 0.5
    f.slab(0, TZ0 + 0.09, w, 0.18, 0.12, "stone")
    f.slab(0, (TZ0 + 0.18 + lit_top) / 2, w, lit_top - TZ0 - 0.18, 0.05, "window_lit")
    f.slab(0, lit_top + 0.1, w, 0.2, 0.13, "metal_dark")
    f.slab(0, (lit_top + 0.2 + top - 0.3) / 2, w, top - 0.3 - lit_top - 0.2, 0.05, "glass_blue")
    f.slab(0, top - 0.15, 2 * HALL + 0.3, 0.3, 0.16, "wood_dark")
    for k in range(6):
        x = -HALL + k * 2 * HALL / 5
        f.slab(x, (TZ0 + top - 0.3) / 2, 0.36, top - 0.3 - TZ0, 0.3, "wood")
    if g.hd:
        for k in range(20):
            x = -HALL + (k + 0.5) * 2 * HALL / 20
            f.slab(x, (TZ0 + 0.18 + lit_top) / 2, 0.06, lit_top - TZ0 - 0.18, 0.085, "metal_dark")
        for k in range(10):
            x = -HALL + (k + 0.5) * 2 * HALL / 10
            f.slab(x, (lit_top + 0.2 + top - 0.3) / 2, 0.06, top - 0.5 - lit_top, 0.085, "metal_dark")


def airport_terminal(g):
    """Aérogare régionale : hall vitré à toit monopente (bois, verre), deux ailes basses crépies à étage bardé,
    entrée sous auvent côté ville (-Y), baie et porte d'embarquement côté piste (+Y)."""
    slope = (T_HIGH - T_LOW) / (2 * T_Y)
    K.foundation(g, 41.8, 15.8, "stone", top=TZ0, depth=1.4)
    # hall : murs (pignons bardés de bois au-dessus des ailes) et toit monopente débordant
    g.prism([(-T_Y, TZ0), (T_Y, TZ0), (T_Y, T_HIGH - 0.02), (-T_Y, T_LOW - 0.02)], -HALL, HALL, "wood_honey", axis='x')
    ya, yb = -T_Y - 1.4, T_Y + 1.2
    za, zb = T_LOW + (ya + T_Y) * slope, T_LOW + (yb + T_Y) * slope
    g.prism([(ya, za), (yb, zb), (yb, zb + 0.3), (ya, za + 0.3)], -HALL - 0.7, HALL + 0.7, "roof_metal", axis='x')
    # ailes : rez-de-chaussée crépi, étage bardé, toit plat
    for s in (-1, 1):
        cx = s * (HALL + 21.0 - 0.1) / 2
        w = 21.0 - HALL - 0.1
        on_floor(g, (cx, 0, (TZ0 + 3.2) / 2), (w, 2 * WING_Y, 3.2 - TZ0), "plaster_white")
        g.box((cx + s * 0.03, 0, (3.2 + WING_H) / 2), (w, 2 * WING_Y + 0.12, WING_H - 3.2), "wood_honey")
        g.box((cx + s * 0.05, 0, WING_H + 0.1), (w + 0.2, 2 * WING_Y + 0.4, 0.2), "metal_dark")
    front = Fac(g, 'y', -T_Y, -1)
    back = Fac(g, 'y', T_Y, 1)
    if g.lod == 2:
        for f, top in ((front, T_LOW), (back, T_HIGH)):
            f.quad(0, (TZ0 + 3.1) / 2, 2 * HALL - 0.5, 3.1 - TZ0, "window_lit", 0.04)
            f.quad(0, (3.3 + top - 0.3) / 2, 2 * HALL - 0.5, top - 3.6, "glass_blue", 0.04)
        front.quad(0, 4.6, 8.4, 1.4, BRAND, 0.3)
        g.box((0, -10.1, 3.475), (14.0, 3.8, 0.25), "plaster_white")
        return
    _glazing(g, front, T_LOW)
    _glazing(g, back, T_HIGH)
    # entrée côté ville : portes vitrées coulissantes dans un portique, auvent sur poteaux de bois, bandeau à l'avion
    for s in (-1, 1):
        front.slab(s * 1.85, (TZ0 + 2.75) / 2, 0.22, 2.75 - TZ0, 0.36, "metal_dark")
        front.slab(s * 0.86, (TZ0 + 2.6) / 2, 1.6, 2.6 - TZ0, 0.1, "window_lit")
        if g.hd:
            front.slab(s * 0.12, 1.5, 0.05, 0.9, 0.05, "metal", out=0.1)
    front.slab(0, 2.9, 3.92, 0.3, 0.36, "metal_dark")
    g.box((0, -8.56, TZ0 / 2 - 0.21), (6.0, 1.2, TZ0 + 0.38), "stone")             # perron : deux marches
    g.box((0, -9.4, TZ0 / 4 - 0.2), (6.8, 0.6, TZ0 / 2 + 0.4), "stone")
    g.box((0, -10.1, 3.475), (14.0, 3.8, 0.25), "plaster_white")                 # auvent : y de -12,0 à -8,2
    g.box((0, -12.04, 3.475), (14.1, 0.1, 0.36), BRAND)
    for s in (-1, 1):
        g.box((s * 7.03, -10.15, 3.475), (0.08, 3.7, 0.32), BRAND)
        on_floor(g, (s * 6.4, -11.5, 3.35 / 2), (0.3, 0.3, 3.35), "wood")
        if g.hd:
            g.box((s * 6.4, -11.5, 0.12), (0.5, 0.5, 0.24), "stone")
    if g.hd:
        for y in (-11.2, -9.4):
            g.box((0, y, 3.33), (11.0, 0.18, 0.05), "lamp")
    front.slab(0, 4.6, 8.4, 1.4, 0.1, BRAND, out=0.3)
    f2 = Fac(g, 'y', -T_Y - 0.4, -1)
    emblem(f2, -2.7, 4.6, 0.56, "white", out=0.02)
    f2.slab(1.0, 4.8, 4.0, 0.2, 0.025, "white")
    f2.slab(0.3, 4.4, 2.6, 0.12, 0.025, "white")
    # côté piste : porte d'embarquement vitrée (x = +6) sous une petite marquise
    back.slab(6.0, (TZ0 + 2.5) / 2, 2.0, 2.5 - TZ0, 0.1, "window_lit")
    for s in (-1, 1):
        back.slab(6.0 + s * 1.1, (TZ0 + 2.65) / 2, 0.2, 2.65 - TZ0, 0.36, "metal_dark")
    back.slab(6.0, 2.75, 2.4, 0.22, 0.36, "metal_dark")
    g.box((6.0, T_Y + 0.75, 2.95), (3.4, 1.3, 0.14), "plaster_white")
    g.box((6.0, T_Y + 0.66, TZ0 / 2 - 0.21), (3.2, 1.2, TZ0 + 0.38), "stone")
    if g.hd:
        back.slab(6.0, 1.45, 0.05, 2.2, 0.03, "metal", out=0.1)
        g.box((6.0, T_Y + 0.75, 2.86), (2.4, 0.14, 0.04), "lamp")
    # ailes : fenêtres des bureaux, porte de service au pignon ouest
    for s in (-1, 1):
        for fac in (Fac(g, 'y', -WING_Y, -1, sash="metal_dark"), Fac(g, 'y', WING_Y, 1, sash="metal_dark")):
            for k in range(3):
                fac.window(s * (HALL + 1.9 + k * 2.6), 1.95, 1.7, 1.4, shutter=None, frame="wood", lit=0.55)
            if g.hd:
                for k in range(4):
                    fac.slab(s * (HALL + 1.1 + k * 2.2), 3.85, 1.2, 0.5, 0.03, "window", out=0.06)
                fac.slab(s * (HALL + 4.5), 3.2, 8.9, 0.1, 0.1, "wood_dark", out=0.03)
        end = Fac(g, 'x', s * 20.9, s, sash="metal_dark")
        for y in (-4.2, 4.2):
            end.window(y, 1.95, 1.7, 1.4, shutter=None, frame="wood", lit=0.5)
        if s < 0:
            end.door(0.0, 1.1, 2.2, "door", frame="wood", z0=TZ0)
        else:
            end.window(0.0, 1.95, 1.7, 1.4, shutter=None, frame="wood", lit=0.5)
    if g.hd:
        K.seams(g, (-HALL - 0.7, ya, za + 0.3), (HALL + 0.7, ya, za + 0.3), (HALL + 0.7, yb, zb + 0.3),
                (-HALL - 0.7, yb, zb + 0.3), "roof_metal_b", step=0.9)
        g.box((0, ya - 0.04, za + 0.13), (2 * HALL + 1.5, 0.08, 0.36), "wood_dark")           # rives du toit
        g.box((0, yb + 0.04, zb + 0.13), (2 * HALL + 1.5, 0.08, 0.36), "wood_dark")
        for s in (-1, 1):                                                                       # toits des ailes
            cx = s * (HALL + 21.0) / 2
            g.box((cx - s * 1.5, 2.5, WING_H + 0.2 + 0.5), (2.2, 1.5, 1.0), "metal")
            g.box((cx - s * 1.5, 1.74, WING_H + 0.75), (1.5, 0.02, 0.6), "metal_dark")
            g.box((cx + s * 1.8, -2.5, WING_H + 0.2 + 0.35), (1.4, 1.1, 0.7), "metal")
            g.tube([(s * 20.2, 6.9, WING_H), (s * 20.2, 6.9, 4.0), (s * 21.1, 7.3, 3.8), (s * 21.1, 7.3, 0.3)],
                   [0.06] * 4, 8, "roof_metal")


reg(dict(id="airport_terminal", file="Airport_Terminal", cat="landmark", tags=["airport", "service"], seed=700,
         fp=(42.0, 16.0), collider="box", build=airport_terminal,
         note="hall x -12..12 (entrée côté ville x = 0 en y = -7,9, auvent x -7..7 jusqu'à y = -12 ; porte "
              "d'embarquement côté piste x = +6 en y = +7,9), ailes x 12..21, toit monopente qui monte vers la piste"))


def airport_tower(g):
    """Tour de contrôle : socle technique, fût octogonal, corbeau, passerelle, vigie vitrée aux glaces inclinées
    vers l'extérieur, toit à antennes et feu rouge."""
    rot = math.pi / 8
    ap = math.cos(rot)                                    # apothème / rayon de l'octogone
    K.foundation(g, 7.7, 7.7, "stone", top=0.4, depth=1.4)
    on_floor(g, (0, 0, (0.4 + 3.6) / 2), (7.6, 7.6, 3.2), "plaster_white")
    g.box((0, 0, 3.7), (7.9, 7.9, 0.2), "metal_dark")
    R = 2.15
    g.frustum((0, 0, 3.8), R, R, 11.6, 8, "plaster_white", rot=rot, cap0=False, cap1=False, auto=False)   # → 15,4
    g.frustum((0, 0, 15.4), R, 3.5, 1.4, 8, "concrete", rot=rot, cap0=False, cap1=False, auto=False)      # → 16,8
    g.frustum((0, 0, 16.8), 4.0, 4.0, 0.25, 8, "metal_dark", rot=rot, auto=False)                          # passerelle
    g.frustum((0, 0, 17.05), 2.9, 3.0, 0.6, 8, "plaster_white", rot=rot, cap0=False, cap1=False, auto=False)
    g.frustum((0, 0, 17.65), 3.0, 3.16, 0.7, 8, "window_lit", rot=rot, cap0=False, cap1=False, auto=False)
    g.frustum((0, 0, 18.35), 3.16, 3.6, 1.85, 8, "glass_blue", rot=rot, cap0=False, cap1=False, auto=False)  # → 20,2
    g.frustum((0, 0, 20.2), 3.95, 3.95, 0.3, 8, "plaster_white", rot=rot, auto=False)
    g.frustum((0, 0, 20.5), 3.7, 2.4, 0.5, 8, "roof_metal", rot=rot, cap0=False, auto=False)               # → 21,0
    if g.lod == 2:
        g.box((0.7, 0.4, 21.25), (0.4, 0.4, 0.5), "neon_red")
        g.box((-0.9, -0.5, 22.0), (0.12, 0.12, 2.0), "metal")
        return
    # fentes vitrées du fût (escalier), bandeaux, porte et fenêtres du socle
    for sgn in (-1, 1):
        g.box((0, sgn * R * ap, 9.6), (0.55, 0.06, 9.4), "window")
        g.box((sgn * R * ap, 0, 9.6), (0.06, 0.55, 9.4), "window_lit" if sgn > 0 else "window")
    if g.hd:
        for z in (6.6, 9.6, 12.6):
            g.frustum((0, 0, z), R + 0.05, R + 0.05, 0.16, 8, "concrete", rot=rot, cap0=False, cap1=False, auto=False)
    f = Fac(g, 'y', -3.8, -1, sash="metal_dark")
    f.door(0.0, 1.2, 2.3, "metal_dark", frame="concrete", z0=0.4)
    for u in (-2.4, 2.4):
        f.window(u, 2.1, 1.2, 1.1, shutter=None, frame="concrete", lit=0.5)
    for fac in (Fac(g, 'x', 3.8, 1, sash="metal_dark"), Fac(g, 'x', -3.8, -1, sash="metal_dark"),
                Fac(g, 'y', 3.8, 1, sash="metal_dark")):
        for u in (-2.0, 2.0):
            fac.window(u, 2.1, 1.2, 1.1, shutter=None, frame="concrete", lit=0.4)
    # montants de la vigie, garde-corps de la passerelle
    for k in range(8):
        a = rot + k * math.pi / 4
        c, s = math.cos(a), math.sin(a)
        g.beam((c * 3.0, s * 3.0, 17.6), (c * 3.62, s * 3.62, 20.22), 0.12, "metal_dark")
        if g.hd:
            g.box((c * 3.88, s * 3.88, 17.6), (0.06, 0.06, 1.1), "metal")
    if g.hd:
        for z in (17.55, 18.12):
            pts = [(math.cos(rot + k * math.pi / 4) * 3.88, math.sin(rot + k * math.pi / 4) * 3.88, z) for k in range(9)]
            g.tube(pts, [0.03] * 9, 5, "metal", cap_end=False)
        g.frustum((0, 0, 18.33), 3.2, 3.2, 0.06, 8, "metal_dark", rot=rot, cap0=False, cap1=False, auto=False)
    # toit : feu rouge, antennes, capteur de vent
    g.frustum((0.7, 0.4, 21.0), 0.16, 0.16, 0.2, 8, "metal_dark", auto=False)
    g.frustum((0.7, 0.4, 21.2), 0.2, 0.14, 0.34, 8, "neon_red", auto=False)
    g.tube([(-0.9, -0.5, 20.9), (-0.9, -0.5, 23.3)], [0.05, 0.03], 6, "metal")
    g.tube([(0.2, -1.1, 20.9), (0.2, -1.1, 22.4)], [0.035, 0.02], 6, "metal")
    if g.hd:
        g.box((-0.9, -0.5, 22.6), (0.9, 0.04, 0.04), "metal")
        g.box((-0.9, -0.5, 22.9), (0.04, 0.6, 0.04), "metal")
        g.frustum((-0.3, 1.0, 21.0), 0.35, 0.35, 0.3, 10, "white", auto=False)
        g.ico((-0.3, 1.0, 21.35), 0.36, 2, "white", scale=(1, 1, 0.7), auto=False)
        g.frustum((0.2, -1.1, 22.4), 0.12, 0.0, 0.12, 6, "neon_red", auto=False)


reg(dict(id="airport_tower", file="Airport_Tower", cat="landmark", tags=["airport", "service"], seed=701,
         fp=(8.0, 8.0), collider="box", build=airport_tower, grad_h=21.0,
         note="socle 7,6 m, fût octogonal, vigie de 17,6 à 20,2 m, feu rouge à 21,4 m, antenne à 23,3 m"))


H_HW, H_WALL, H_RISE, H_Y = 13.0, 4.2, 4.8, 11.7       # hangar : demi-largeur, murs, flèche de la voûte, pignons


def _arch(n, grow=0.0):
    return [((H_HW + grow) * math.cos(math.pi * k / n), H_WALL + (H_RISE + grow) * math.sin(math.pi * k / n))
            for k in range(n + 1)]


def airport_hangar(g):
    """Hangar d'aéroclub à voûte en tôle : grandes portes coulissantes entrouvertes côté -Y, bandeau bleu à l'avion,
    fenêtres en bandeau sur les longs pans."""
    n = lv(g, 18, 10, 6)
    arc = _arch(n)
    faces = g.prism([(H_HW, 0.0)] + arc + [(-H_HW, 0.0)], -H_Y, H_Y, "roof_metal", axis='y')
    for f in faces:
        f.normal_update()
        if len(f.verts) > 4 or abs(f.normal.x) > 0.995:
            f[g.col] = A.CI["plaster_white"]
    front = Fac(g, 'y', -H_Y, -1)
    if g.lod == 2:
        front.quad(0, 3.0, 18.8, 6.0, "roof_metal_b", 0.05)
        front.quad(0, 6.9, 18.0, 0.8, BRAND, 0.06)
        return
    # portes : deux vantaux de chaque côté, entrouvertes sur l'intérieur sombre
    front.quad(0, 3.0, 1.7, 6.0, "black", 0.02)
    for s in (-1, 1):
        for k, (u, d) in enumerate(((s * 3.0, 0.1), (s * 7.3, 0.2))):
            front.slab(u, 3.0 - 0.03 * k, 4.4, 6.0 - 0.06 * k, d, "roof_metal_b")
            front.slab(u, 4.3, 3.7, 0.75, 0.025, "window", out=d)
            if g.hd:
                for j in range(5):
                    front.slab(u - 1.76 + j * 0.88, 1.9, 0.07, 3.6, 0.03, "metal_dark", out=d)
                front.slab(u, 0.2, 4.4, 0.3, 0.03, "metal_dark", out=d)
    front.slab(0, 6.2, 19.6, 0.36, 0.32, "metal_dark")                                    # rail haut
    front.slab(0, 6.95, 18.0, 0.8, 0.07, BRAND)
    f2 = Fac(g, 'y', -H_Y - 0.07, -1)
    emblem(f2, -6.6, 6.95, 0.33, "white", out=0.015, ang=18.0)
    f2.slab(1.0, 6.95, 11.0, 0.14, 0.02, "white")
    if g.hd:
        for u in (-6.0, 0.0, 6.0):
            front.box(u, 6.48, 0.7, 0.12, 0.3, "metal_dark")
            front.box(u, 6.4, 0.6, 0.04, 0.24, "lamp")
        front.slab(0, 0.05, 19.4, 0.1, 0.5, "concrete")                                    # seuil et rail bas
    # longs pans : fenêtres en bandeau, soubassement ; pignon arrière : porte et fenêtres
    for s in (-1, 1):
        side = Fac(g, 'x', s * H_HW, s)
        for u in (-8.2, -2.8, 2.8, 8.2):
            side.slab(u, 2.9, 3.4, 0.9, 0.05, "window")
        side.slab(0, 0.25, 2 * H_Y - 0.1, 0.5, 0.06, "concrete")
        if g.hd:
            K.gutter(g, 'y', s * (H_HW + 0.02), -H_Y, H_Y, H_WALL - 0.12, s)
    rear = Fac(g, 'y', H_Y, 1, sash="metal_dark")
    rear.door(-9.5, 1.1, 2.2, "metal_dark", frame="concrete", z0=0.0)
    for u in (-4.0, 1.0, 6.0):
        rear.slab(u, 2.9, 3.2, 0.9, 0.05, "window")
    # arcs de rive, et nervures de la voûte au niveau détaillé
    lo, hi = _arch(n, -0.02), _arch(n, 0.1)
    ys = [-H_Y - 0.05, H_Y + 0.05] + ([-H_Y + (k + 1) * 2 * H_Y / 8 for k in range(7)] if g.hd else [])
    for y in ys:
        loft(g, [[(a[0], y - 0.11, a[1]), (a[0], y + 0.11, a[1]), (b[0], y + 0.11, b[1]), (b[0], y - 0.11, b[1])]
                 for a, b in zip(lo, hi)], "roof_metal_b")


reg(dict(id="airport_hangar", file="Airport_Hangar", cat="landmark", tags=["airport", "service"], seed=702,
         fp=(26.0, 24.0), collider="box", build=airport_hangar,
         note="voûte de 26 m, faîte à 9 m ; portes coulissantes côté -Y (x -9,5..9,5, hautes de 6 m), entrouvertes de "
              "1,7 m au centre ; porte de service au pignon arrière, x = -9,5"))


# ================================================================= aéronefs
def _spinner(g, y0, z, r, length, color, n):
    g.limb((0, y0 + 0.02, z), (0, y0 - length, z), r, r * 0.12, n, color, cap0=False, cap1=True, rot=0.0, auto=False)


def _propeller(g, y, z, diam, tilt=25.0):
    """Hélice bipale à l'arrêt (absente du niveau lointain)."""
    if g.lod == 2:
        return
    rot = Matrix.Rotation(math.radians(tilt), 3, 'Y')
    g.box((0, y, z), (0.13, 0.035, diam), "black", rot=rot)
    if g.hd:
        for s in (-1, 1):
            d = rot @ Vector((0, 0, s * (diam / 2 - 0.07)))
            g.box((d.x, y - 0.004, z + d.z), (0.135, 0.04, 0.14), "yellow", rot=rot)


def _gear(g, x, y, z_top, r, w, n, spat=None, leg="metal", fork=0.05):
    """Jambe de train et roue (axe X) ; spat = couleur du carénage de roue. Niveau lointain : un bloc."""
    if g.lod == 2:
        g.box((x, y, z_top / 2), (max(w + 0.08, 0.2), 2.2 * r, z_top), spat or "metal_dark")
        return
    g.box((x, y, (z_top + r) / 2), (fork, 0.09, z_top - r), leg)
    wheel(g, (x, y, r), r, w, n)
    if spat:
        g.ico((x, y + 0.04, r + 0.07), 1.0, 2 if g.hd else 1, spat, scale=(w * 0.5 + 0.06, r * 2.3, r * 0.95), auto=False)


def plane_tourer(g):
    """Avion de tourisme d'aéroclub, quadriplace à aile basse : verrière bulle, bouts d'aile relevés, train tricycle
    caréné, hélice bipale. Blanc à filet rouge."""
    n = lv(g, 12, 10, 8)
    st = [(-3.12, 1.02, 0.36, 0.34), (-2.25, 1.05, 0.52, 0.46), (-1.6, 1.08, 0.56, 0.5), (-0.95, 1.2, 0.56, 0.64),
          (0.2, 1.22, 0.55, 0.64), (1.0, 1.14, 0.42, 0.46), (2.2, 1.18, 0.22, 0.28), (3.3, 1.22, 0.07, 0.16)]

    def paint(i, k):
        e = elev(k, n)
        if 2 <= i <= 4 and e > 10:
            return "glass_blue"
        if abs(e) < 1:
            return "red"
        return None
    body(g, st, n, "white", paint)
    _spinner(g, -3.12, 1.02, 0.2, 0.38, "red", lv(g, 10, 8, 6))
    _propeller(g, -3.25, 1.02, 1.8)
    # aile basse d'une pièce : plan central horizontal, bouts relevés (dièdre), saumons rouges
    half = [((4.35, -1.42, 1.19), 1.3, 0.15), ((3.95, -1.45, 1.09), 1.39, 0.17), ((2.55, -1.55, 0.74), 1.72, 0.24)]
    ws = [((-x, y, z), c, t) for ((x, y, z), c, t) in half] + [a for a in reversed(half)]
    surf(g, ws, "white", paint=lambda i: "red" if i in (0, len(ws) - 2) else None)
    # empennages : plan horizontal monobloc, dérive à sommet rouge
    surf(g, [((-1.6, 2.82, 1.2), 0.68, 0.07), ((0, 2.72, 1.2), 0.86, 0.1), ((1.6, 2.82, 1.2), 0.68, 0.07)], "white",
         sym=True)
    surf(g, [((0, 2.2, 1.3), 1.18, 0.12), ((0, 2.62, 1.92), 0.8, 0.09), ((0, 2.8, 2.23), 0.6, 0.07)], "white", axis='x',
         sym=True, paint=lambda i: "red" if i == 1 else None)
    # train tricycle caréné
    for s in (-1, 1):
        _gear(g, s * 1.25, -0.5, 0.66, 0.2, 0.13, lv(g, 12, 8), spat="white")
    _gear(g, 0.0, -2.55, 0.62, 0.18, 0.11, lv(g, 12, 8), spat="white")
    if g.hd:
        hoop(g, st[3], n, "white")
        hoop(g, st[4], n, "white")
        for s in (-1, 1):
            g.box((s * 0.22, -3.13, 1.1), (0.18, 0.02, 0.09), "black")                    # entrées d'air
            g.box((s * 4.36, -0.75, 1.2), (0.03, 0.06, 0.05), "neon_red" if s < 0 else "neon_green")
        g.tube([(0.3, -2.6, 0.62), (0.3, -2.3, 0.55)], [0.035, 0.035], 6, "metal_dark")   # échappement
        g.tube([(0, 0.6, 1.72), (0, 0.75, 2.05)], [0.012, 0.008], 4, "metal")              # antenne
        g.tube([(-2.9, -1.3, 0.72), (-2.9, -1.75, 0.72)], [0.012, 0.012], 4, "metal")      # pitot


reg(dict(id="plane_tourer", file="Plane_Tourer", cat="street", tags=["airport", "aircraft"], seed=703,
         fp=(8.7, 7.0), collider="mesh_low", build=plane_tourer))


def plane_trainer(g):
    """Avion-école quadriplace à aile haute haubanée, train tricycle fixe. Blanc à filet bleu."""
    n = lv(g, 12, 10, 8)
    st = [(-3.85, 1.15, 0.4, 0.37), (-2.95, 1.18, 0.54, 0.48), (-2.35, 1.2, 0.58, 0.52), (-1.6, 1.4, 0.58, 0.72),
          (-0.1, 1.4, 0.57, 0.72), (0.75, 1.38, 0.47, 0.6), (2.4, 1.32, 0.22, 0.32), (3.95, 1.32, 0.07, 0.18)]

    def paint(i, k):
        e = elev(k, n)
        if i == 2 and e > 10:
            return "glass_blue"
        if i in (3, 4) and 10 < e < 50:
            return "glass_blue"
        if abs(e) < 1:
            return "blue"
        return None
    body(g, st, n, "white", paint, p=2.25)
    _spinner(g, -3.85, 1.15, 0.19, 0.3, "blue", lv(g, 10, 8, 6))
    _propeller(g, -3.97, 1.15, 1.9, tilt=-20.0)
    # aile haute d'une pièce, bouts bleus
    half = [((5.5, -1.78, 2.27), 1.12, 0.13), ((5.1, -1.8, 2.26), 1.18, 0.14), ((2.6, -1.95, 2.2), 1.63, 0.2)]
    ws = [((-x, y, z), c, t) for ((x, y, z), c, t) in half] + [a for a in reversed(half)]
    surf(g, ws, "white", paint=lambda i: "blue" if i in (0, len(ws) - 2) else None)
    surf(g, [((-1.7, 3.3, 1.32), 0.66, 0.07), ((0, 3.05, 1.32), 1.02, 0.1), ((1.7, 3.3, 1.32), 0.66, 0.07)], "white",
         sym=True)
    surf(g, [((0, 2.25, 1.45), 1.9, 0.1), ((0, 3.0, 2.0), 1.15, 0.1), ((0, 3.5, 2.7), 0.68, 0.07)], "white", axis='x',
         sym=True, paint=lambda i: "blue" if i == 1 else None)
    # train : lames du train principal, roue avant
    if g.lod == 2:
        for s in (-1, 1):
            g.box((s * 1.15, -0.8, 0.4), (0.3, 0.5, 0.8), "white")
        g.box((0, -3.2, 0.35), (0.2, 0.4, 0.7), "metal_dark")
        return
    for s in (-1, 1):
        g.beam((s * 0.42, -0.8, 0.82), (s * 1.27, -0.78, 0.27), 0.16, "white", 0.05)
        wheel(g, (s * 1.33, -0.78, 0.2), 0.2, 0.13, lv(g, 12, 8))
        g.beam((s * 0.5, -1.25, 0.82), (s * 2.55, -1.25, 2.13), 0.12, "white", 0.045)       # hauban
    g.box((0, -3.2, 0.5), (0.05, 0.09, 0.56), "metal")
    wheel(g, (0, -3.2, 0.18), 0.18, 0.11, lv(g, 12, 8))
    if g.hd:
        for s in (-1, 1):
            g.box((s * 0.2, -3.86, 1.24), (0.2, 0.02, 0.09), "black")
            g.box((s * 5.51, -1.2, 2.28), (0.03, 0.06, 0.05), "neon_red" if s < 0 else "neon_green")
        g.tube([(0.28, -3.3, 0.72), (0.28, -3.0, 0.64)], [0.035, 0.035], 6, "metal_dark")
        g.tube([(0, -0.6, 2.3), (0, -0.4, 2.62)], [0.012, 0.008], 4, "metal")
        g.frustum((0, 3.9, 2.72), 0.035, 0.03, 0.08, 6, "neon_red", auto=False)


reg(dict(id="plane_trainer", file="Plane_Trainer", cat="street", tags=["airport", "aircraft"], seed=704,
         fp=(11.0, 8.3), collider="mesh_low", build=plane_trainer))


def plane_jet(g):
    """Jet d'affaires léger : fuselage rond, aile basse en légère flèche à ailettes, deux réacteurs à l'arrière,
    empennage en T, train tricycle. Blanc à filet sombre."""
    n = lv(g, 14, 10, 8)
    st = [(-6.75, 1.5, 0.3, 0.28), (-5.95, 1.6, 0.6, 0.55), (-5.05, 1.72, 0.78, 0.76), (-4.2, 1.75, 0.82, 0.82),
          (3.0, 1.75, 0.82, 0.82), (4.8, 1.95, 0.55, 0.58), (6.3, 2.2, 0.14, 0.2)]

    def paint(i, k):
        e = elev(k, n)
        if i == 1 and 14 < e < 80:
            return "window"
        if abs(e) < 1 and 0 <= i <= 5:
            return "metal_dark"
        return None
    body(g, st, n, "white", paint, nose=(-7.25, 1.45))
    # aile : emplanture sous la cabine, flèche légère, dièdre, ailettes de bout
    half = [((7.0, 1.05, 1.5), 1.05, 0.11), ((1.0, -0.85, 1.12), 2.7, 0.3)]
    ws = [((-x, y, z), c, t) for ((x, y, z), c, t) in half] + [a for a in reversed(half)]
    surf(g, ws, "white")
    for s in (-1, 1):
        surf(g, [((s * 6.97, 1.15, 1.5), 0.95, 0.07), ((s * 7.0, 1.75, 2.35), 0.45, 0.05)], "white", axis='x', sym=True,
             paint=lambda i: "metal_dark")
    # réacteurs sur pylônes
    m = lv(g, 12, 8, 6)
    for s in (-1, 1):
        ns = [(2.3, 2.32, 0.4, 0.4), (2.75, 2.32, 0.5, 0.5), (4.2, 2.32, 0.5, 0.5), (4.95, 2.32, 0.3, 0.3)]
        body(g, ns, m, "white", (lambda i, k: "metal" if i == 0 else "metal_dark" if i == 2 else None), x0=s * 1.5)
        g.box((s * 0.9, 3.5, 2.28), (0.9, 1.7, 0.2), "white")
        if g.lod < 2:
            g.limb((s * 1.5, 2.28, 2.32), (s * 1.5, 2.3, 2.32), 0.33, 0.33, m, "black", cap0=True, cap1=False, rot=0.0,
                   auto=False)
    # empennage en T
    surf(g, [((0, 3.9, 2.4), 2.9, 0.22), ((0, 5.95, 4.45), 1.32, 0.14)], "white", axis='x', sym=True)
    surf(g, [((-2.7, 6.55, 4.5), 0.68, 0.07), ((0, 5.9, 4.5), 1.36, 0.12), ((2.7, 6.55, 4.5), 0.68, 0.07)], "white",
         sym=True)
    # train
    if g.lod == 2:
        g.box((0, -5.4, 0.55), (0.3, 0.45, 1.1), "metal_dark")
        for s in (-1, 1):
            g.box((s * 1.3, 0.9, 0.5), (0.3, 0.6, 1.0), "metal_dark")
        return
    g.box((0, -5.4, 0.7), (0.07, 0.1, 1.0), "metal")
    for s in (-1, 1):
        wheel(g, (s * 0.11, -5.4, 0.2), 0.2, 0.11, lv(g, 12, 8))
        g.box((s * 1.3, 0.9, 0.65), (0.09, 0.12, 0.8), "metal")
        wheel(g, (s * 1.3, 0.9, 0.28), 0.28, 0.2, lv(g, 12, 8))
        # hublots de cabine, plaqués sur le flanc
        for k in range(5):
            y = -3.3 + k * 1.05
            a = math.radians(22.0)
            g.box((s * 0.82 * math.cos(a), y, 1.75 + 0.82 * math.sin(a)), (0.03, 0.36, 0.27), "window",
                  rot=Matrix.Rotation(-s * a, 3, 'Y'))
    if g.hd:
        g.frustum((0, 6.55, 4.62), 0.04, 0.03, 0.09, 6, "neon_red", auto=False)
        for s in (-1, 1):
            g.box((s * 7.0, 1.55, 1.52), (0.04, 0.08, 0.06), "neon_red" if s < 0 else "neon_green")
        g.tube([(0, -2.0, 2.57), (0, -1.8, 2.9)], [0.015, 0.01], 4, "metal")
        g.tube([(0, 1.2, 0.93), (0, 1.35, 0.68)], [0.015, 0.01], 4, "metal")


reg(dict(id="plane_jet", file="Plane_Jet", cat="street", tags=["airport", "aircraft"], seed=705,
         fp=(14.0, 14.5), collider="mesh_low", build=plane_jet))


def plane_glider(g):
    """Planeur monoplace au sol, ailes à plat (béquilles de bout d'aile) : fuselage fin, longue verrière, grande
    envergure, empennage en T. Blanc à bouts orange. Construit à l'horizontale puis basculé sur sa roue."""
    n = lv(g, 12, 8, 6)
    mk = g.mark()
    st = [(-2.95, 0.78, 0.18, 0.2), (-2.2, 0.85, 0.3, 0.38), (-1.2, 0.9, 0.33, 0.46), (-0.3, 0.9, 0.3, 0.44),
          (0.6, 0.9, 0.19, 0.27), (3.05, 0.85, 0.07, 0.1)]

    def paint(i, k):
        return "glass_blue" if 0 <= i <= 2 and elev(k, n) > 25 else None
    body(g, st, n, "white", paint, nose=(-3.3, 0.74))
    half = [((7.5, -0.42, 1.24), 0.36, 0.05), ((6.6, -0.5, 1.21), 0.5, 0.07), ((3.8, -0.68, 1.14), 0.78, 0.11),
            ((0.25, -0.75, 1.1), 0.95, 0.15)]
    ws = [((-x, y, z), c, t) for ((x, y, z), c, t) in half] + [a for a in reversed(half)]
    surf(g, ws, "white", paint=lambda i: "orange" if i in (0, len(ws) - 2) else None)
    surf(g, [((0, 2.3, 0.88), 0.92, 0.09), ((0, 2.62, 1.95), 0.6, 0.07)], "white", axis='x', sym=True)
    surf(g, [((-1.35, 2.78, 1.98), 0.36, 0.04), ((0, 2.6, 1.98), 0.62, 0.07), ((1.35, 2.78, 1.98), 0.36, 0.04)],
         "white", sym=True)
    if g.lod == 2:
        g.box((0, -0.9, 0.25), (0.2, 0.5, 0.5), "metal_dark")
    else:
        wheel(g, (0, -0.9, 0.2), 0.2, 0.1, lv(g, 12, 8))
        g.box((0, -0.9, 0.4), (0.2, 0.62, 0.2), "white")                                    # carénage de roue
        wheel(g, (0, 2.95, 0.68), 0.08, 0.05, 8, hub=None)
        g.box((0, 2.95, 0.75), (0.04, 0.05, 0.12), "metal")
    if g.hd:
        hoop(g, st[1], n, "white", r=0.018, min_elev=25.0)
        hoop(g, st[3], n, "white", r=0.018, min_elev=25.0)
        g.tube([(0, -3.3, 0.74), (0, -3.42, 0.74)], [0.012, 0.012], 4, "metal")
    # bascule : le planeur repose sur sa roue principale et sa roulette de queue
    ang = math.atan2(0.6, 3.85)
    piv = Matrix.Translation((0, -0.9, 0.0)) @ Matrix.Rotation(-ang, 4, 'X') @ Matrix.Translation((0, 0.9, 0.0))
    g.transform(g.since(mk), piv)
    if g.lod < 2:
        for s in (-1, 1):                                                                    # béquilles de bout d'aile
            g.tube([(s * 6.9, -0.2, 0.0), (s * 6.9, -0.2, 1.1)], [0.02, 0.02], 5, "metal")
            g.frustum((s * 6.9, -0.2, 0.0), 0.13, 0.1, 0.04, 6, "orange", auto=False)


reg(dict(id="plane_glider", file="Plane_Glider", cat="street", tags=["airport", "aircraft"], seed=706,
         fp=(15.0, 6.6), collider="mesh_low", build=plane_glider))


def helicopter(g):
    """Hélicoptère léger de secours en montagne, sur patins : bulle vitrée, capot moteur, rotor quadripale à
    l'arrêt, poutre de queue et fenestron. Rouge à bande blanche."""
    n = lv(g, 12, 10, 8)
    st = [(-3.5, 1.3, 0.45, 0.42), (-2.7, 1.5, 0.85, 0.78), (-1.6, 1.65, 0.98, 0.95), (-0.2, 1.7, 0.98, 0.95),
          (0.8, 1.8, 0.7, 0.75), (1.6, 1.98, 0.3, 0.34), (5.5, 2.15, 0.14, 0.17)]

    def paint(i, k):
        e = elev(k, n)
        if i in (0, 1) and e > -5:
            return "glass_blue"
        if i == 2 and 5 < e < 62:
            return "glass_blue"
        if abs(e) < 1 and i >= 2:
            return "white"
        return None
    body(g, st, n, "red", paint, nose=(-3.85, 1.22))
    # capot moteur et tête de rotor
    cw = [(-1.5, 2.55, 0.5, 0.22), (-0.9, 2.7, 0.62, 0.4), (0.6, 2.72, 0.6, 0.4), (1.6, 2.5, 0.3, 0.24)]
    body(g, cw, lv(g, 10, 8, 6), "red", (lambda i, k: "metal_dark" if i == 2 and elev(k, lv(g, 10, 8, 6)) > 30 else None))
    g.frustum((0, -0.5, 3.0), 0.16, 0.12, 0.34, lv(g, 10, 8, 6), "metal_dark", auto=False)
    g.frustum((0, -0.5, 3.3), 0.3, 0.24, 0.1, lv(g, 10, 8, 6), "metal", auto=False)
    # queue : dérive haute et basse, fenestron, plan fixe
    surf(g, [((0, 5.0, 1.25), 1.2, 0.12), ((0, 4.75, 2.1), 1.75, 0.2), ((0, 5.75, 3.25), 0.75, 0.1)], "red", axis='x',
         sym=True, paint=lambda i: "white" if i == 1 else None)
    surf(g, [((-1.1, 4.25, 2.1), 0.5, 0.06), ((0, 4.15, 2.1), 0.7, 0.08), ((1.1, 4.25, 2.1), 0.5, 0.06)], "red", sym=True)
    # patins
    for s in (-1, 1):
        if g.lod == 2:
            g.box((s * 1.08, -0.9, 0.07), (0.14, 4.2, 0.14), "metal_dark")
            for y in (-2.0, -0.2):
                g.beam((s * 1.08, y, 0.1), (s * 0.4, y, 0.9), 0.1, "metal_dark")
            continue
        g.tube([(s * 1.08, -3.05, 0.3), (s * 1.08, -2.7, 0.08), (s * 1.08, 1.3, 0.08)], [0.05, 0.05, 0.05],
               lv(g, 8, 6), "metal_dark", cap_start=True)
        for y in (-2.0, -0.2):
            g.tube([(s * 1.08, y, 0.08), (s * 0.95, y, 0.5), (s * 0.4, y, 0.9)], [0.04, 0.04, 0.04], lv(g, 8, 6),
                   "metal_dark", cap_end=False)
    if g.lod == 2:
        return
    # rotor principal : quatre pales en croix, légèrement tombantes ; rotor caréné dans la dérive
    for k in range(4):
        a = math.radians(45.0 + 90.0 * k)
        c, s = math.cos(a), math.sin(a)
        g.beam((c * 0.25, -0.5 + s * 0.25, 3.35), (c * 5.3, -0.5 + s * 5.3, 3.2), 0.3, "metal_dark", 0.04)
        if g.hd:
            g.beam((c * 4.9, -0.5 + s * 4.9, 3.226), (c * 5.31, -0.5 + s * 5.31, 3.214), 0.31, "yellow", 0.045)
    for s in (-1, 1):
        g.limb((s * 0.09, 5.55, 2.2), (s * 0.115, 5.55, 2.2), 0.5, 0.5, lv(g, 14, 10), "black", cap0=False, cap1=True,
               rot=0.0, auto=False)
    if g.hd:
        for s in (-1, 1):
            g.limb((s * 0.11, 5.55, 2.2), (s * 0.13, 5.55, 2.2), 0.12, 0.12, 8, "metal", cap0=False, cap1=True, rot=0.0,
                   auto=False)
            g.box((s * 0.99, -1.05, 1.2), (0.03, 0.05, 0.9), "white")                       # montant de porte
            g.box((s * 0.55, -3.3, 0.92), (0.14, 0.06, 0.1), "lamp")                        # phares
        hoop(g, st[2], n, "red", r=0.03, min_elev=5.0)
        g.tube([(0.35, 0.9, 2.95), (0.35, 1.75, 2.75)], [0.07, 0.07], 6, "metal_dark")       # échappement
        g.frustum((0, 5.9, 3.27), 0.04, 0.03, 0.09, 6, "neon_red", auto=False)
        g.tube([(0, 3.0, 2.25), (0, 3.1, 2.6)], [0.012, 0.008], 4, "metal")


reg(dict(id="helicopter", file="Helicopter", cat="street", tags=["airport", "aircraft"], seed=707,
         fp=(7.6, 10.5), collider="mesh_low", build=helicopter))


# ================================================================= équipements
def airport_windsock(g):
    """Manche à air : mât à anneaux rouges et blancs, cerceau, manche conique rayée orange et blanc (deux étoffes
    croisées, effilées, qui ondulent ensemble : shader Toon, FlagWave)."""
    n = lv(g, 10, 8, 5)
    g.frustum((0, 0, 0), 0.24, 0.2, 0.14, n, "concrete", auto=False)
    z = 0.14
    for k in range(6):
        g.frustum((0, 0, z), 0.075 - k * 0.004, 0.071 - k * 0.004, 1.02, n, "sign_red" if k % 2 == 0 else "white",
                  cap0=False, cap1=k == 5, auto=False)
        z += 1.02
    zc = 6.5 - 0.36
    g.tube([(0, 0, z - 0.02), (0, 0, 6.5)], [0.03, 0.03], 5, "metal")
    R = 0.34
    if g.lod < 2:
        g.beam((0, 0, zc + R), (0.3, 0, zc + R), 0.03, "metal")
        g.beam((0, 0, zc - R * 0.2), (0.3, 0, zc - R), 0.03, "metal")
    if g.hd:
        pts = [(0.3, math.cos(6.2832 * k / 10) * R, zc + math.sin(6.2832 * k / 10) * R) for k in range(11)]
        g.tube(pts, [0.016] * 11, 4, "metal", cap_end=False)
    m = lv(g, 2, 1, 1)
    bands = 5
    stripe = lambda i, j: "orange" if (i // m) % 2 == 0 else "white"
    g.h = lambda *key: 0.37                      # même phase pour les deux étoffes : elles ondulent d'un seul bloc
    try:
        g.cloth((0.3, 0, zc + R), (2.5, 0, 0), (0, 0, -2 * R), bands * m, 2, stripe, amp=0.22, tip=0.42, sag=0.5)
        if g.lod < 2:
            g.cloth((0.3, -R, zc), (2.5, 0, 0), (0, 2 * R, 0), bands * m, 2, stripe, amp=0.22, tip=0.42, sag=0.5)
    finally:
        del g.h


reg(dict(id="airport_windsock", file="Airport_Windsock", cat="street", tags=["airport"], seed=708, fp=(0.5, 0.5),
         collider="capsule", radius=0.12, build=airport_windsock))


def _light(color):
    def build(g):
        """Feu de balisage bas : embase jaune, pied, lanterne colorée."""
        if g.level == 2:
            g.frustum((0, 0, 0), 0.1, 0.06, 0.4, 4, color, cap0=False, auto=False)
            return
        n = lv(g, 6, 5)
        g.frustum((0, 0, 0), 0.13, 0.11, 0.05, n, "yellow", cap0=False, auto=False)
        g.frustum((0, 0, 0.05), 0.036, 0.036, 0.2, lv(g, 5, 4), "metal_dark", cap0=False, cap1=False, auto=False)
        g.frustum((0, 0, 0.25), 0.085, 0.055, 0.15, n, color, auto=False)
    return build


for _name, _color, _seed in (("white", "lamp", 709), ("blue", "neon_blue", 710), ("green", "neon_green", 711),
                             ("red", "neon_red", 712)):
    reg(dict(id="airfield_light_" + _name, file="Airfield_Light" + _name.capitalize(), cat="street",
             tags=["airport", "light"], seed=_seed, fp=(0.3, 0.3), collider="none", no_resolve=True,
             build=_light(_color)))


def airport_papi(g):
    """Indicateur de pente d'approche (PAPI) : barre de quatre projecteurs sur pieds, deux rouges et deux blancs,
    tournés vers -Y."""
    for k, x in enumerate((-3.9, -1.3, 1.3, 3.9)):
        lamp = "neon_red" if k < 2 else "lamp"
        if g.lod == 2:
            g.box((x, 0, 0.4), (0.9, 0.7, 0.8), "orange")
            g.quad((x, -0.36, 0.62), 0.6, 0.2, '-y', lamp)
            continue
        g.box((x, 0, 0.62), (0.9, 0.7, 0.36), "orange")
        g.box((x, -0.36, 0.62), (0.62, 0.03, 0.2), lamp)
        g.box((x, 0, 0.04), (0.8, 0.7, 0.08), "concrete")
        for (dx, dy) in ((-0.32, -0.25), (0.32, -0.25), (0, 0.27)):
            g.box((x + dx, dy, 0.26), (0.05, 0.05, 0.36), "metal_dark")
        if g.hd:
            g.box((x, -0.42, 0.74), (0.7, 0.14, 0.03), "metal_dark")                        # visière


reg(dict(id="airport_papi", file="Airport_Papi", cat="street", tags=["airport", "light"], seed=713, fp=(9.0, 1.0),
         collider="none", build=airport_papi))


def airport_fence(g):
    """Module de clôture grillagée de 4 m (le long de X), haut de 2,2 m : poteau au bout -X (le module voisin apporte
    l'autre), lisses, grillage évoqué par des fils, bavolet à trois rangs de barbelés."""
    g.box((-2.0, 0, 1.1), (0.07, 0.07, 2.2), "metal")
    g.box((0, 0, 2.02), (4.0, 0.035, 0.035), "metal")
    g.box((0, 0, 0.1), (4.0, 0.03, 0.03), "metal")
    if g.lod == 2:
        g.box((0, 0, 1.06), (4.0, 0.03, 0.03), "metal")
        return
    g.box((0, 0, 1.06), (4.0, 0.025, 0.025), "metal")
    cols = lv(g, 9, 4)
    for k in range(cols):
        x = -2.0 + 4.0 * (k + 1) / (cols + 1)
        g.quad((x, -0.002, 1.06), 0.03, 1.9, '-y', "metal")
        g.quad((x, 0.002, 1.06), 0.03, 1.9, '+y', "metal")
    if g.hd:
        for z in (0.58, 1.54):
            g.box((0, 0, z), (4.0, 0.028, 0.02), "metal")
        g.beam((-2.0, 0, 2.18), (-2.0, -0.28, 2.42), 0.04, "metal")
        for t in (0.25, 0.6, 0.95):
            g.box((0, -0.28 * t, 2.2 + 0.24 * t), (4.0, 0.015, 0.015), "metal_dark")


reg(dict(id="airport_fence", file="Airport_Fence", cat="street", tags=["airport"], seed=714, fp=(4.0, 0.12),
         collider="box", no_resolve=True, build=airport_fence))


def airport_sign(g):
    """Totem d'entrée de l'aéroport : socle, deux mâts, grand panneau bleu à l'avion blanc (sans texte), deux faces."""
    g.box((0, 0, 0.0), (3.2, 0.9, 0.8), "concrete")
    for s in (-1, 1):
        g.box((s * 1.0, 0, 3.2), (0.18, 0.3, 5.6), "metal_dark")
    g.box((0, 0, 4.1), (2.3, 0.34, 3.4), BRAND)
    g.box((0, 0, 5.95), (2.6, 0.44, 0.2), "metal_dark")
    if g.lod == 2:
        return
    for s in (-1, 1):
        f = Fac(g, 'y', s * 0.17, s)
        emblem(f, 0.0, 4.75, 0.8, "white", out=0.02, ang=35.0)
        f.slab(0.0, 3.25, 1.7, 0.16, 0.025, "white")
        f.slab(-0.25, 2.95, 1.2, 0.1, 0.025, "white")
        if g.hd:
            f.slab(0.0, 5.78, 2.1, 0.06, 0.05, "lamp")


reg(dict(id="airport_sign", file="Airport_Sign", cat="street", tags=["airport", "sign"], seed=715, fp=(3.2, 0.9),
         collider="box", build=airport_sign))


def airport_fuel(g):
    """Point d'avitaillement : cuve blanche couchée sur berceaux dans une cuvette de rétention en béton, armoire de
    distribution à bandeau rouge, enrouleur de tuyau."""
    n = lv(g, 16, 10, 8)
    g.box((0, 0, 0.09), (6.0, 3.0, 0.18), "concrete")
    if g.lod < 2:
        for s in (-1, 1):
            g.box((0, s * 1.43, 0.3), (6.0, 0.14, 0.26), "concrete")
            g.box((s * 2.93, 0, 0.3), (0.14, 2.72, 0.26), "concrete")
    ty, tz, tr = 0.3, 1.42, 0.82
    g.limb((-2.45, ty, tz), (0.85, ty, tz), tr, tr, n, "white", cap0=True, cap1=True, rot=0.0, auto=False)
    for x in (-1.9, 0.3):
        g.box((x, ty, 0.48), (0.3, 1.5, 0.6), "concrete_dark")
    g.box((2.1, -0.4, 1.03), (0.9, 0.62, 1.7), "white")
    g.box((2.1, -0.4, 1.72), (0.93, 0.65, 0.2), "sign_red")
    if g.lod == 2:
        return
    g.limb((-1.0, ty, tz), (-0.6, ty, tz), tr + 0.012, tr + 0.012, n, "sign_red", cap0=False, cap1=False, rot=0.0,
           auto=False)
    g.box((2.1, -0.72, 1.3), (0.5, 0.02, 0.32), "black")
    g.box((2.1, -0.4, 1.92), (1.1, 0.8, 0.07), "metal_dark")
    g.limb((2.57, -0.4, 0.75), (2.72, -0.4, 0.75), 0.33, 0.33, lv(g, 12, 8), "metal_dark", cap0=True, cap1=True,
           rot=0.0, auto=False)
    if g.hd:
        for x in (-2.45, 0.85):                                                            # fonds bombés de la cuve
            s = -1 if x < 0 else 1
            g.limb((x, ty, tz), (x + s * 0.16, ty, tz), tr, tr * 0.55, n, "white", cap0=False, cap1=True, rot=0.0,
                   auto=False)
        g.frustum((-0.8, ty, tz + tr - 0.03), 0.26, 0.26, 0.14, 10, "metal", auto=False)     # trou d'homme
        g.tube([(0.5, ty, tz + tr - 0.02), (0.5, ty, tz + tr + 0.2), (2.1, -0.2, tz + tr + 0.2), (2.1, -0.2, 1.95)],
               [0.04] * 4, 6, "metal")
        g.limb((2.72, -0.4, 0.75), (2.75, -0.4, 0.75), 0.24, 0.24, 10, "black", cap0=False, cap1=True, rot=0.0,
               auto=False)
        g.tube([(2.65, -0.72, 0.75), (2.65, -0.95, 0.4), (2.45, -1.1, 0.24)], [0.03] * 3, 5, "black")
        g.frustum((2.7, 0.9, 0.18), 0.08, 0.08, 0.42, 8, "red", auto=False)                 # extincteur
        g.frustum((2.7, 0.9, 0.6), 0.03, 0.03, 0.1, 6, "black", auto=False)
        for k in range(4):                                                                   # échelle de la cuve
            g.box((-2.0, ty - tr - 0.06, 0.6 + k * 0.4), (0.4, 0.03, 0.03), "metal")
        for s in (-1, 1):
            g.box((-2.0 + s * 0.2, ty - tr - 0.06, 1.2), (0.03, 0.03, 1.9), "metal")


reg(dict(id="airport_fuel", file="Airport_Fuel", cat="street", tags=["airport"], seed=716, fp=(6.0, 3.0),
         collider="box", build=airport_fuel))

IDS = ["airport_terminal", "airport_tower", "airport_hangar", "plane_tourer", "plane_trainer", "plane_jet",
       "plane_glider", "helicopter", "airport_windsock", "airfield_light_white", "airfield_light_blue",
       "airfield_light_green", "airfield_light_red", "airport_papi", "airport_fence", "airport_sign", "airport_fuel"]
