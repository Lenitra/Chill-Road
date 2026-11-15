"""Pack alpin : signalisation (lot 7). Panneaux tournés vers -Y (Unity +Z), pied à l'origine, mât prolongé sous le sol.

Anti-z-fighting : le fond d'un panneau est découpé dans le plan même de la plaque (couronne de la bordure + champ
intérieur, sans superposition) ; les pictogrammes sont en relief sur deux niveaux (2 cm et 3,4 cm devant la plaque),
les traits qui se croisent (horizontaux / verticaux, traits consécutifs) étant sur des niveaux différents."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac

LVL = (0.02, 0.034)                                   # profondeur des deux niveaux de pictogrammes


def sign(iid, fname, build, lods=2, **kw):
    kw.setdefault("collider", "capsule")
    kw.setdefault("radius", 0.06)
    kw.setdefault("no_resolve", True)                 # propres par construction : pas de déplacement automatique
    reg(dict(id=iid, file=fname, cat="sign", lods=lods, build=build, **kw))


def ngon(r, n, rot=0.0, c=(0.0, 0.0), sx=1.0):
    return [(c[0] + math.cos(rot + 2 * math.pi * k / n) * r * sx, c[1] + math.sin(rot + 2 * math.pi * k / n) * r)
            for k in range(n)]


def round_ngon(r, n, rot, c, cr, seg=4):
    """Polygone régulier à angles arrondis (rayon cr)."""
    pts = []
    inner = r - cr / math.sin(math.pi / 2 - math.pi / n)
    for k in range(n):
        a = rot + 2 * math.pi * k / n
        cx, cz = c[0] + math.cos(a) * inner, c[1] + math.sin(a) * inner
        for i in range(seg + 1):
            b = a - math.pi / n + 2 * math.pi / n * i / seg
            pts.append((cx + math.cos(b) * cr, cz + math.sin(b) * cr))
    return pts


def rect(cx, cz, w, h):
    return [(cx - w / 2, cz - h / 2), (cx + w / 2, cz - h / 2), (cx + w / 2, cz + h / 2), (cx - w / 2, cz + h / 2)]


def mast(g, h, x=0.0, w=0.07):
    if g.hd:                                          # mât rond, platine boulonnée, bouchon
        g.tube([(x, 0.04, -0.5), (x, 0.04, h)], [w / 2, w / 2], 10, "metal")
        g.frustum((x, 0.04, h), w / 2 + 0.006, w / 2 + 0.006, 0.02, 10, "metal_dark", auto=False)
        g.frustum((x, 0.04, 0.0), w * 1.6, w * 1.6, 0.012, 8, "metal_dark", auto=False)
        for k in range(4):
            a = 0.785 + 1.5708 * k
            g.octa((x + math.cos(a) * w * 1.15, 0.04 + math.sin(a) * w * 1.15, 0.018), 0.014, "metal")
    else:
        g.box((x, 0.04, (h - 0.5) / 2), (w, w, h + 0.5), "metal")


def plate(g, pts, color, y0=-0.03, inner=None, inner_color=None, rivets=True):
    """Plaque : chant et dos en métal ; face avant de la couleur du panneau. inner (même nombre de points que pts) :
    champ intérieur d'une autre couleur, découpé dans le même plan (couronne + champ, sans recouvrement).
    Renvoie la façade (plan de la face avant) pour y poser les pictogrammes."""
    bm = g.bm
    n = len(pts)
    F = [bm.verts.new((x, y0, z)) for x, z in pts]
    Bk = [bm.verts.new((x, 0.0, z)) for x, z in pts]
    side = []
    for k in range(n):
        j = (k + 1) % n
        side.append(bm.faces.new((F[k], F[j], Bk[j], Bk[k])))
    side.append(bm.faces.new(Bk))
    g.paint(side, "metal")
    if inner is None:
        front = g.paint([bm.faces.new(F)], color)
    else:
        I = [bm.verts.new((x, y0, z)) for x, z in inner]
        front = g.paint([bm.faces.new((F[k], I[k], I[(k + 1) % n], F[(k + 1) % n])) for k in range(n)], color)
        front += g.paint([bm.faces.new(I)], inner_color)
    _front_fix(g, front)
    bmesh_fix(g, side, (sum(p[0] for p in pts) / n, y0 / 2, sum(p[1] for p in pts) / n))
    f = Fac(g, 'y', y0, -1)
    if g.hd and rivets:                               # brides de fixation au dos, rivets en façade
        xs, zs = [p[0] for p in pts], [p[1] for p in pts]
        cx, cz, hh = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2, (max(zs) - min(zs))
        for dz in (-hh * 0.22, hh * 0.22):
            g.box((cx, 0.03, cz + dz), (0.16, 0.06, 0.035), "metal_dark")
            for sx in (-1, 1):
                g.octa((cx + sx * 0.05, y0 - 0.008, cz + dz), 0.011, "metal")
    return f


def bmesh_fix(g, faces, center):
    """Oriente les faces du chant et du dos vers l'extérieur ; la face avant regarde -Y."""
    c = Vector(center)
    for f in faces:
        f.normal_update()
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()


def _front_fix(g, faces):
    for f in faces:
        f.normal_update()
        if f.normal.y > 0:
            f.normal_flip()


def relief(f, pts, color, lvl=0):
    """Pictogramme (polygone (x, z)) en relief devant la plaque, au niveau lvl."""
    out = LVL[lvl]
    if f.g.hd:                                        # le dos de chaque niveau est à une profondeur différente
        f.plate(pts, -0.004 - 0.014 * lvl, out, color)
    else:
        f.poly(pts, color, out)


def stroke(f, a, b, w, color, lvl=0):
    """Trait épais de a à b (coordonnées (x, z) sur la plaque)."""
    ax, az = a
    bx, bz = b
    dx, dz = bx - ax, bz - az
    L = math.hypot(dx, dz) or 1.0
    nx, nz = -dz / L * w / 2, dx / L * w / 2
    relief(f, [(ax - nx, az - nz), (bx - nx, bz - nz), (bx + nx, bz + nz), (ax + nx, az + nz)], color, lvl)


def arrow_head(f, tip, direction, size, color, lvl=0):
    dx, dz = direction
    L = math.hypot(dx, dz)
    dx, dz = dx / L, dz / L
    bx, bz = tip[0] - dx * size, tip[1] - dz * size
    relief(f, [(tip[0], tip[1]), (bx - dz * size * 0.6, bz + dx * size * 0.6), (bx + dz * size * 0.6, bz - dx * size * 0.6)],
           color, lvl)


SEG = {'0': "abcdef", '1': "bc", '2': "abged", '3': "abgcd", '4': "fgbc", '5': "afgcd", '6': "afgedc", '7': "abc",
       '8': "abcdefg", '9': "abfgcd"}


def digits(f, text, cx, cz, h, color="black", out=None):
    """Chiffres à sept segments : segments horizontaux au niveau 0, verticaux au niveau 1 (ils se croisent aux angles)."""
    w = h * 0.5
    t = h * 0.16
    gap = w * 0.35
    total = len(text) * w + (len(text) - 1) * gap
    x = cx - total / 2
    for ch in text:
        i = 0.012                                     # retraits aux angles : aucune face dans le plan d'une autre
        segs = {"a": ((x + i, cz + h / 2 - t / 2), (x + w - i, cz + h / 2 - t / 2), 0),
                "d": ((x + i, cz - h / 2 + t / 2), (x + w - i, cz - h / 2 + t / 2), 0),
                "g": ((x + i, cz), (x + w - i, cz), 0),
                "f": ((x + t / 2, cz), (x + t / 2, cz + h / 2 - i), 1), "b": ((x + w - t / 2, cz), (x + w - t / 2, cz + h / 2 - i), 1),
                "e": ((x + t / 2, cz - h / 2 + i), (x + t / 2, cz), 1), "c": ((x + w - t / 2, cz - h / 2 + i), (x + w - t / 2, cz), 1)}
        for s in SEG[ch]:
            a, b, lv = segs[s]
            stroke(f, a, b, t, color, lv)
        x += w + gap


# ----------------------------------------------------------------- formes de base
def round_sign(g, zc=2.3, r=0.36, rim="sign_red", face="sign_white", h=None):
    mast(g, h or zc + r)
    n = 28 if g.hd else lod(g, 12, 8)
    if face == rim:
        return plate(g, ngon(r, n, c=(0, zc)), rim)
    return plate(g, ngon(r, n, c=(0, zc)), rim, inner=ngon(r * 0.76, n, c=(0, zc)), inner_color=face)


def tri_sign(g, zc=2.25, r=0.5, down=False):
    """Triangle de danger (pointe en haut) ou cédez-le-passage (pointe en bas)."""
    mast(g, zc + r * 0.6)
    rot = -math.pi / 2 if down else math.pi / 2
    if g.hd:
        return plate(g, round_ngon(r, 3, rot, (0, zc), 0.05), "sign_red", inner=round_ngon(r * 0.66, 3, rot, (0, zc), 0.03),
                     inner_color="sign_white")
    return plate(g, ngon(r, 3, rot, (0, zc)), "sign_red", inner=ngon(r * 0.66, 3, rot, (0, zc)), inner_color="sign_white")


def speed(g, n):
    f = round_sign(g)
    digits(f, n, 0, 2.3, 0.26)


sign("sign_speed30", "Sign_Speed30", lambda g: speed(g, "30"), tags=["speed"])
sign("sign_speed50", "Sign_Speed50", lambda g: speed(g, "50"), tags=["speed"])
sign("sign_speed80", "Sign_Speed80", lambda g: speed(g, "80"), tags=["speed"])


def u_turn(cx, z0, z1, r, w, n):
    """Contour d'un demi-tour (épingle) d'un seul tenant : branche gauche, arc, branche droite, épaisseur w."""
    ro, ri = r + w / 2, r - w / 2
    outer = [(cx - ro, z0)] + [(cx - ro * math.cos(math.pi * k / n), z1 + ro * math.sin(math.pi * k / n)) for k in range(n + 1)] + [(cx + ro, z0)]
    inner = [(cx + ri, z0)] + [(cx + ri * math.cos(math.pi * k / n), z1 + ri * math.sin(math.pi * k / n)) for k in range(n + 1)] + [(cx - ri, z0)]
    return outer[::-1] + inner[::-1]


def sign_hairpin(g):
    f = tri_sign(g)
    z = 2.25
    relief(f, u_turn(0.0, z - 0.1, z + 0.05, 0.1, 0.05, 10 if g.hd else 4), "black", 0)
    stroke(f, (-0.1, z - 0.2), (-0.1, z - 0.1), 0.05, "black", 1)
    arrow_head(f, (0.1, z - 0.2), (0, -1), 0.1, "black", 1)


sign("sign_hairpin", "Sign_Hairpin", sign_hairpin, tags=["warning", "curve"])


def sign_curve(g, s):
    f = tri_sign(g)
    z = 2.25
    stroke(f, (-s * 0.08, z - 0.2), (-s * 0.08, z - 0.02), 0.055, "black", 0)
    stroke(f, (-s * 0.08, z - 0.02), (s * 0.06, z + 0.1), 0.055, "black", 1)
    arrow_head(f, (s * 0.16, z + 0.19), (s, 0.85), 0.11, "black", 0)


sign("sign_curve_l", "Sign_Curve_L", lambda g: sign_curve(g, -1), tags=["warning", "curve"])
sign("sign_curve_r", "Sign_Curve_R", lambda g: sign_curve(g, 1), tags=["warning", "curve"])


def sign_steep(g):
    f = tri_sign(g)
    z = 2.25
    relief(f, [(-0.22, z - 0.18), (0.22, z - 0.18), (0.22, z + 0.02)], "black", 0)
    digits(f, "10", -0.06, z + 0.07, 0.13)


sign("sign_steep", "Sign_Steep", sign_steep, tags=["warning"])


def sign_rockfall(g):
    f = tri_sign(g)
    z = 2.25
    relief(f, [(0.02, z - 0.2), (0.24, z - 0.2), (0.07, z + 0.2)], "black", 0)
    for x, zz, s in ((-0.12, z - 0.14, 0.05), (-0.05, z - 0.02, 0.04), (-0.18, z - 0.02, 0.035)):
        relief(f, ngon(s, 5, 0.3, (x, zz)), "black", 0)


sign("sign_rockfall", "Sign_Rockfall", sign_rockfall, tags=["warning"])


def sign_cattle(g):
    f = tri_sign(g)
    z = 2.2
    relief(f, rect(-0.02, z + 0.045, 0.28, 0.13), "black", 0)                       # corps
    relief(f, rect(0.16, z + 0.095, 0.12, 0.11), "black", 1)                        # tête
    for x in (-0.13, -0.03, 0.02, 0.085):
        stroke(f, (x, z - 0.14), (x, z + 0.01), 0.035, "black", 1)
    stroke(f, (-0.16, z + 0.1), (-0.2, z - 0.02), 0.02, "black", 1)


sign("sign_cattle", "Sign_Cattle", sign_cattle, tags=["warning"])
sign("sign_yield", "Sign_Yield", lambda g: tri_sign(g, 2.3, 0.5, True), tags=["priority"])


def sign_stop(g):
    mast(g, 2.6)
    f = plate(g, ngon(0.4, 8, math.pi / 8, (0, 2.3)), "sign_white", inner=ngon(0.36, 8, math.pi / 8, (0, 2.3)),
              inner_color="sign_red")
    if g.hd:                                          # « STOP » en lettres à segments
        x = -0.2875
        for segs in ((((0.11, 0.09), (0, 0.09)), ((0, 0.09), (0, 0)), ((0, 0), (0.11, 0)), ((0.11, 0), (0.11, -0.09)), ((0.11, -0.09), (0, -0.09))),
                     (((0, 0.09), (0.11, 0.09)), ((0.055, 0.09), (0.055, -0.09))),
                     (((0, 0.09), (0.11, 0.09)), ((0.11, 0.09), (0.11, -0.09)), ((0.11, -0.09), (0, -0.09)), ((0, -0.09), (0, 0.09))),
                     (((0, -0.09), (0, 0.09)), ((0, 0.09), (0.11, 0.09)), ((0.11, 0.09), (0.11, 0)), ((0.11, 0), (0, 0)))):
            for a, b in segs:
                stroke(f, (x + a[0], 2.3 + a[1]), (x + b[0], 2.3 + b[1]), 0.035, "sign_white", 0 if a[1] == b[1] else 1)
            x += 0.155
    else:
        relief(f, rect(0, 2.3, 0.48, 0.12), "sign_white", 0)


sign("sign_stop", "Sign_Stop", sign_stop, tags=["priority"])


def sign_noentry(g):
    f = round_sign(g, rim="sign_red", face="sign_red")
    relief(f, rect(0, 2.3, 0.52, 0.12), "sign_white", 0)


sign("sign_noentry", "Sign_NoEntry", sign_noentry, tags=["priority"])


def sign_parking(g):
    mast(g, 2.7)
    z = 2.3
    f = plate(g, rect(0, z, 0.7, 0.7), "sign_white", inner=rect(0, z, 0.64, 0.64), inner_color="sign_blue")
    stroke(f, (-0.1, z - 0.2), (-0.1, z + 0.17), 0.08, "sign_white", 1)
    stroke(f, (-0.1, z + 0.17), (0.12, z + 0.17), 0.07, "sign_white", 0)
    stroke(f, (0.1, z + 0.17), (0.1, z + 0.02), 0.07, "sign_white", 1)
    stroke(f, (-0.1, z + 0.02), (0.12, z + 0.02), 0.07, "sign_white", 0)


sign("sign_parking", "Sign_Parking", sign_parking, tags=["info"])


def sign_town(g):
    """Panneau d'entrée d'agglomération : cartouche blanc à liseré rouge sur deux mâts (sans texte lisible)."""
    for x in (-0.75, 0.75):
        mast(g, 2.3, x)
    z = 1.95
    f = plate(g, rect(0, z, 2.0, 0.7), "sign_red", inner=rect(0, z, 1.88, 0.58), inner_color="sign_white")
    if g.lod == 0:
        for a, b in ((-0.7, -0.45), (-0.38, -0.05), (0.02, 0.3), (0.37, 0.7)):
            relief(f, rect((a + b) / 2, z, b - a, 0.2), "black", 0)
    else:
        relief(f, rect(0, z, 1.4, 0.18), "black", 0)


sign("sign_town", "Sign_Town", sign_town, tags=["info", "town"], fp=(2.0, 0.2))


def arrow_plate(g, z, s, color, text="sign_white", L=1.3, h=0.26, y0=-0.03):
    """Flèche directionnelle partant du mât vers s (±1)."""
    pts = [(s * 0.08, z - h / 2), (s * (L - 0.18), z - h / 2), (s * L, z), (s * (L - 0.18), z + h / 2), (s * 0.08, z + h / 2)]
    if s < 0:
        pts.reverse()
    f = plate(g, pts, color, y0)
    if g.lod == 0:
        relief(f, rect(s * (0.22 + L - 0.35) / 2, z, (L - 0.35) - 0.22, 0.08), text, 0)
    return f


def sign_direction(g):
    mast(g, 3.2, w=0.09)
    arrow_plate(g, 2.95, 1, "sign_blue")
    arrow_plate(g, 2.62, -1, "sign_white", "black")
    arrow_plate(g, 2.29, 1, "sign_green")
    arrow_plate(g, 1.96, -1, "sign_brown", L=1.1)


sign("sign_direction", "Sign_Direction", sign_direction, tags=["info"], fp=(2.6, 0.2))


def sign_hiking(g):
    """Poteau de randonnée : flèches jaunes à pointe blanc-rouge-blanc."""
    mast(g, 2.6, w=0.08)
    for z, s, L in ((2.4, 1, 0.9), (2.18, -1, 0.8), (1.96, 1, 0.7)):
        f = arrow_plate(g, z, s, "sign_yellow", "black", L, 0.17)
        if g.lod == 0:
            relief(f, rect(s * (L - 0.205), z, 0.11, 0.14), "sign_red", 0)
    if g.hd:
        g.box((0, -0.01, 1.6), (0.12, 0.04, 0.12), "sign_white")


sign("sign_hiking", "Sign_Hiking", sign_hiking, tags=["info", "hiking"], fp=(1.8, 0.2))


def chevron(g):
    """Balise de virage : panneau blanc à chevrons rouges pointant vers +X (retourner pour l'autre sens)."""
    mast(g, 1.5)
    z = 1.2
    f = plate(g, rect(0, z, 0.6, 0.6), "sign_white", rivets=False)
    for k, x in enumerate((-0.2, 0.0)):
        relief(f, [(x, z + 0.24), (x + 0.12, z + 0.24), (x + 0.32, z), (x + 0.12, z - 0.24), (x, z - 0.24), (x + 0.2, z)][::-1],
               "sign_red", k % 2)


sign("chevron", "Chevron", chevron, tags=["curve", "small"], lods=1)


def delineator(g):
    """Balise de bord de route : fût blanc, bague noire, catadioptre orange (avant) et blanc (arrière), en relief."""
    sec = [(-0.06, -0.035), (0.06, -0.035), (0.045, 0.035), (-0.045, 0.035)]
    for z0, z1, c in ((-0.5, 0.72, "sign_white"), (0.72, 0.95, "black"), (0.95, 1.05, "sign_white")):
        g.prism(sec, z0, z1, c, axis='z')
    if g.hd:
        g.prism([(-0.045, 1.05), (0.045, 1.05), (0.02, 1.1), (-0.045, 1.12)], -0.035, 0.035, "sign_white", axis='x')
    g.box((0, -0.045, 0.835), (0.05, 0.03, 0.15), "orange")
    g.box((0, 0.044, 0.835), (0.04, 0.03, 0.1), "sign_white")


sign("delineator", "Delineator", delineator, tags=["roadside", "small"], lods=1, collider="none")


def snow_pole(g):
    n = lod(g, 5, 4)
    bands = [(-0.5, 0.5, "orange"), (0.5, 0.9, "black"), (0.9, 1.5, "orange"), (1.5, 1.9, "black"), (1.9, 2.5, "orange")]
    for z0, z1, c in bands:
        g.frustum((0, 0, z0), 0.035, 0.035, z1 - z0, n, c, cap0=False, cap1=z1 > 2.4)


sign("snow_pole", "Snow_Pole", snow_pole, tags=["roadside", "small"], lods=1, collider="none")


def milestone(g):
    """Borne kilométrique : pierre blanche à tête rouge arrondie."""
    g.box((0, 0, 0.05), (0.42, 0.26, 0.9), "sign_white")
    n = 14 if g.hd else lod(g, 8, 5)
    pts = [(math.cos(math.pi * k / n) * 0.21, 0.5 + math.sin(math.pi * k / n) * 0.2) for k in range(n + 1)]
    g.prism(pts, -0.13, 0.13, "sign_red", axis='y')
    if g.lod == 0:
        digits(Fac(g, 'y', -0.13, -1), "7", 0, 0.28, 0.16, "black")


sign("milestone", "Milestone", milestone, tags=["roadside", "small"], lods=2, collider="none")


def pass_sign(g):
    """Panneau de col : grand cartouche brun et bleu (altitude en chiffres) sur un socle de pierres."""
    for k in range(lod(g, 9, 5)):
        x = g.R(-1.3, 1.3)
        f = g.ico((x, g.R(-0.3, 0.4), 0.2), g.R(0.35, 0.6), 1, "rock" if k % 2 else "rock_light", scale=(1.2, 1, 0.8))
        g.jitter(f, 0.06)
    for x in (-0.9, 0.9):                             # poteaux derrière la plaque
        g.box((x, 0.08, 1.2), (0.14, 0.14, 2.8), "wood_dark")
    z = 2.0
    f = plate(g, rect(0, z, 2.6, 1.1), "sign_brown", -0.045)
    relief(f, rect(0, z + 0.235, 2.44, 0.47), "sign_blue", 0)
    relief(f, rect(0, z + 0.24, 1.8, 0.16), "sign_white", 1)
    digits(f, "2108", 0.0, z - 0.27, 0.3, "sign_white")
    if g.lod == 0:
        relief(f, [(-1.15, z - 0.47), (-0.75, z - 0.47), (-0.95, z - 0.12)], "sign_white", 0)       # petit sommet
        K.roof_gable(g, 0.5, 2.9, 2.62, 25, "wood_dark", 'x', over_e=0.05, over_g=0.1, thick=0.06, gutters=False)


sign("pass_sign", "Pass_Sign", pass_sign, tags=["info", "summit"], collider="box", fp=(2.8, 1.0))


def road_barrier_closed(g):
    """Barrière « route fermée » : lisse rayée rouge et blanc sur deux chevalets, disque sens interdit."""
    L = 4.0
    for x in (-L / 2 + 0.4, L / 2 - 0.4):              # chevalets : jambes d'épaisseurs différentes (pas de faces confondues)
        g.beam((x, -0.4, 0.0), (x, 0.0, 1.05), 0.07, "metal")
        g.beam((x, 0.4, 0.0), (x, 0.0, 1.05), 0.04, "metal", 0.07)
        if g.hd:                                      # entretoise (plat), patins, lest
            g.beam((x, -0.22, 0.48), (x, 0.22, 0.48), 0.012, "metal", 0.05)
            for sy in (-1, 1):
                g.box((x, sy * 0.42, 0.015), (0.3, 0.14, 0.03), "rubber")
            g.box((x, 0.0, 0.1), (0.36, 0.5, 0.12), "concrete_dark")
    n = 8 if g.lod == 0 else 1
    for k in range(n):
        x0 = -L / 2 + L * k / n
        g.box((x0 + L / n / 2, -0.03, 0.95), (L / n, 0.06, 0.26), "sign_red" if k % 2 == 0 else "sign_white")
    f = plate(g, ngon(0.3, lod(g, 16, 10), c=(0, 1.5)), "sign_red", -0.07, rivets=False)
    relief(f, rect(0, 1.5, 0.4, 0.1), "sign_white", 0)
    g.box((0, 0.0, 1.2), (0.06, 0.06, 0.4), "metal")
    if g.hd:                                          # feux de chantier à lentille
        for x in (-L / 2 + 0.15, L / 2 - 0.15):
            g.box((x, -0.03, 1.15), (0.16, 0.1, 0.12), "yellow")
            m = g.mark()
            g.frustum((0, 0, 0), 0.075, 0.06, 0.03, 12, "orange", auto=False)
            g.transform(g.since(m), Matrix.Translation((x, -0.08, 1.3)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
            g.box((x, -0.03, 1.3), (0.17, 0.07, 0.17), "yellow")
    elif g.lod == 0:
        for x in (-L / 2 + 0.15, L / 2 - 0.15):
            g.box((x, -0.03, 1.16), (0.14, 0.1, 0.14), "orange")


sign("road_barrier_closed", "Road_Barrier_Closed", road_barrier_closed, tags=["closed"], collider="box",
     fp=(4.0, 0.8))


# ----------------------------------------------------------------- autoroute (échangeurs)
def sign_exit(g):
    """Annonce de sortie : grand panneau bleu sur deux mâts, flèche oblique vers la droite, cartouche sans texte."""
    for x in (-1.0, 1.0):
        mast(g, 3.45, x, w=0.1)
    z = 2.8
    f = plate(g, rect(0, z, 2.7, 1.25), "sign_white", inner=rect(0, z, 2.58, 1.13), inner_color="sign_blue")
    stroke(f, (-0.2, z - 0.36), (0.72, z + 0.14), 0.15, "sign_white", 0)
    arrow_head(f, (1.02, z + 0.3), (0.92, 0.5), 0.36, "sign_white", 1)
    if g.lod == 0:
        relief(f, rect(-0.62, z + 0.3, 1.0, 0.13), "sign_white", 1)
        relief(f, rect(-0.82, z + 0.06, 0.6, 0.1), "sign_white", 1)


sign("sign_exit", "Sign_Exit", sign_exit, tags=["motorway", "info"], fp=(2.7, 0.2), radius=0.08)


def sign_gore(g):
    """Balise de sortie au musoir : panneau bleu bas, flèche oblique vers la droite."""
    mast(g, 2.25, w=0.09)
    z = 1.8
    f = plate(g, rect(0, z, 1.3, 0.85), "sign_white", inner=rect(0, z, 1.2, 0.75), inner_color="sign_blue")
    stroke(f, (-0.36, z - 0.22), (0.2, z + 0.1), 0.12, "sign_white", 0)
    arrow_head(f, (0.44, z + 0.235), (0.87, 0.5), 0.27, "sign_white", 1)


sign("sign_gore", "Sign_Gore", sign_gore, tags=["motorway", "info"], fp=(1.3, 0.2))


def sign_motorway(g):
    """Entrée d'autoroute : carré bleu, deux chaussées qui se rapprochent sous un pont."""
    mast(g, 2.85, w=0.09)
    z = 2.38
    f = plate(g, rect(0, z, 0.92, 0.92), "sign_white", inner=rect(0, z, 0.84, 0.84), inner_color="sign_blue")
    stroke(f, (-0.22, z - 0.33), (-0.11, z + 0.31), 0.09, "sign_white", 0)
    stroke(f, (0.22, z - 0.33), (0.11, z + 0.31), 0.09, "sign_white", 0)
    relief(f, rect(0, z + 0.1, 0.64, 0.085), "sign_white", 1)


sign("sign_motorway", "Sign_Motorway", sign_motorway, tags=["motorway", "info"])
