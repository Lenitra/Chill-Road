"""Pack alpin : signalisation (lot 7). Panneaux tournés vers -Y (Unity +Z), pied à l'origine, mât prolongé sous le sol."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac


def sign(iid, fname, build, lods=2, **kw):
    kw.setdefault("collider", "capsule")
    kw.setdefault("radius", 0.06)
    reg(dict(id=iid, file=fname, cat="sign", lods=lods, build=build, **kw))


def ngon(r, n, rot=0.0, c=(0.0, 0.0), sx=1.0):
    return [(c[0] + math.cos(rot + 2 * math.pi * k / n) * r * sx, c[1] + math.sin(rot + 2 * math.pi * k / n) * r)
            for k in range(n)]


def mast(g, h, x=0.0, w=0.07):
    g.box((x, 0.04, (h - 0.5) / 2), (w, w, h + 0.5), "metal")


def plate(g, pts, color, y0=-0.03):
    """Plaque : dos et chant en métal, face avant colorée. Renvoie la façade pour dessiner dessus."""
    g.prism(pts, y0, 0.0, "metal", axis='y')
    f = Fac(g, 'y', y0, -1)
    f.poly(pts, color, 0.004)
    return f


def stroke(f, a, b, w, color, out=0.012):
    """Trait épais de a à b (coordonnées (x, z) sur la plaque)."""
    ax, az = a
    bx, bz = b
    dx, dz = bx - ax, bz - az
    L = math.hypot(dx, dz) or 1.0
    nx, nz = -dz / L * w / 2, dx / L * w / 2
    f.poly([(ax - nx, az - nz), (bx - nx, bz - nz), (bx + nx, bz + nz), (ax + nx, az + nz)], color, out)


def arrow_head(f, tip, direction, size, color, out=0.012):
    dx, dz = direction
    L = math.hypot(dx, dz)
    dx, dz = dx / L, dz / L
    bx, bz = tip[0] - dx * size, tip[1] - dz * size
    f.poly([(tip[0], tip[1]), (bx - dz * size * 0.6, bz + dx * size * 0.6), (bx + dz * size * 0.6, bz - dx * size * 0.6)],
           color, out)


SEG = {'0': "abcdef", '1': "bc", '2': "abged", '3': "abgcd", '4': "fgbc", '5': "afgcd", '6': "afgedc", '7': "abc",
       '8': "abcdefg", '9': "abfgcd"}


def digits(f, text, cx, cz, h, color="black", out=0.012):
    """Chiffres à sept segments (lisibles de loin sans texture)."""
    w = h * 0.5
    t = h * 0.16
    gap = w * 0.35
    total = len(text) * w + (len(text) - 1) * gap
    x = cx - total / 2
    for ch in text:
        segs = {"a": ((x, cz + h / 2 - t / 2), (x + w, cz + h / 2 - t / 2)), "d": ((x, cz - h / 2 + t / 2), (x + w, cz - h / 2 + t / 2)),
                "g": ((x, cz), (x + w, cz)),
                "f": ((x + t / 2, cz), (x + t / 2, cz + h / 2)), "b": ((x + w - t / 2, cz), (x + w - t / 2, cz + h / 2)),
                "e": ((x + t / 2, cz - h / 2), (x + t / 2, cz)), "c": ((x + w - t / 2, cz - h / 2), (x + w - t / 2, cz))}
        for s in SEG[ch]:
            stroke(f, segs[s][0], segs[s][1], t, color, out)
        x += w + gap


# ----------------------------------------------------------------- formes de base
def round_sign(g, zc=2.3, r=0.36, rim="sign_red", face="sign_white", h=None):
    mast(g, h or zc + r)
    n = lod(g, 12, 8)
    f = plate(g, ngon(r, n, c=(0, zc)), rim)
    f.poly(ngon(r * 0.76, n, c=(0, zc)), face, 0.008)
    return f


def tri_sign(g, zc=2.25, r=0.5, down=False):
    """Triangle de danger (pointe en haut) ou cédez-le-passage (pointe en bas)."""
    mast(g, zc + r * 0.6)
    rot = -math.pi / 2 if down else math.pi / 2
    f = plate(g, ngon(r, 3, rot, (0, zc)), "sign_red")
    f.poly(ngon(r * 0.66, 3, rot, (0, zc)), "sign_white", 0.008)
    return f


def speed(g, n):
    f = round_sign(g)
    digits(f, n, 0, 2.3, 0.26)


sign("sign_speed30", "Sign_Speed30", lambda g: speed(g, "30"), tags=["speed"])
sign("sign_speed50", "Sign_Speed50", lambda g: speed(g, "50"), tags=["speed"])
sign("sign_speed80", "Sign_Speed80", lambda g: speed(g, "80"), tags=["speed"])


def sign_hairpin(g):
    f = tri_sign(g)
    z = 2.25
    stroke(f, (-0.1, z - 0.2), (-0.1, z + 0.05), 0.05, "black")
    stroke(f, (-0.1, z + 0.05), (0.0, z + 0.14), 0.05, "black")
    stroke(f, (0.0, z + 0.14), (0.1, z + 0.05), 0.05, "black")
    stroke(f, (0.1, z + 0.05), (0.1, z - 0.1), 0.05, "black")
    arrow_head(f, (0.1, z - 0.2), (0, -1), 0.1, "black")


sign("sign_hairpin", "Sign_Hairpin", sign_hairpin, tags=["warning", "curve"])


def sign_curve(g, s):
    f = tri_sign(g)
    z = 2.25
    stroke(f, (-s * 0.08, z - 0.2), (-s * 0.08, z - 0.02), 0.055, "black")
    stroke(f, (-s * 0.08, z - 0.02), (s * 0.06, z + 0.1), 0.055, "black")
    arrow_head(f, (s * 0.16, z + 0.19), (s, 0.85), 0.11, "black")


sign("sign_curve_l", "Sign_Curve_L", lambda g: sign_curve(g, -1), tags=["warning", "curve"])
sign("sign_curve_r", "Sign_Curve_R", lambda g: sign_curve(g, 1), tags=["warning", "curve"])


def sign_steep(g):
    f = tri_sign(g)
    z = 2.25
    f.poly([(-0.22, z - 0.18), (0.22, z - 0.18), (0.22, z + 0.02)], "black", 0.012)
    digits(f, "10", -0.06, z + 0.07, 0.13)


sign("sign_steep", "Sign_Steep", sign_steep, tags=["warning"])


def sign_rockfall(g):
    f = tri_sign(g)
    z = 2.25
    f.poly([(0.02, z - 0.2), (0.24, z - 0.2), (0.07, z + 0.2)], "black", 0.012)
    for x, zz, s in ((-0.12, z - 0.14, 0.05), (-0.05, z - 0.02, 0.04), (-0.18, z - 0.02, 0.035)):
        f.poly(ngon(s, 5, 0.3, (x, zz)), "black", 0.012)


sign("sign_rockfall", "Sign_Rockfall", sign_rockfall, tags=["warning"])


def sign_cattle(g):
    f = tri_sign(g)
    z = 2.2
    f.poly([(-0.16, z - 0.02), (0.12, z - 0.02), (0.12, z + 0.11), (-0.16, z + 0.11)], "black", 0.012)   # corps
    f.poly([(0.1, z + 0.04), (0.22, z + 0.04), (0.22, z + 0.15), (0.1, z + 0.15)], "black", 0.012)      # tête
    for x in (-0.13, -0.03, 0.02, 0.1):
        stroke(f, (x, z - 0.14), (x, z), 0.035, "black")
    stroke(f, (-0.16, z + 0.1), (-0.2, z - 0.02), 0.02, "black")


sign("sign_cattle", "Sign_Cattle", sign_cattle, tags=["warning"])
sign("sign_yield", "Sign_Yield", lambda g: tri_sign(g, 2.3, 0.5, True), tags=["priority"])


def sign_stop(g):
    mast(g, 2.6)
    f = plate(g, ngon(0.4, 8, math.pi / 8, (0, 2.3)), "sign_white")
    f.poly(ngon(0.36, 8, math.pi / 8, (0, 2.3)), "sign_red", 0.008)
    f.poly([(-0.24, 2.24), (0.24, 2.24), (0.24, 2.36), (-0.24, 2.36)], "sign_white", 0.012)


sign("sign_stop", "Sign_Stop", sign_stop, tags=["priority"])


def sign_noentry(g):
    f = round_sign(g, rim="sign_red", face="sign_red")
    f.poly([(-0.26, 2.24), (0.26, 2.24), (0.26, 2.36), (-0.26, 2.36)], "sign_white", 0.012)


sign("sign_noentry", "Sign_NoEntry", sign_noentry, tags=["priority"])


def sign_parking(g):
    mast(g, 2.7)
    z = 2.3
    f = plate(g, [(-0.35, z - 0.35), (0.35, z - 0.35), (0.35, z + 0.35), (-0.35, z + 0.35)], "sign_white")
    f.poly([(-0.32, z - 0.32), (0.32, z - 0.32), (0.32, z + 0.32), (-0.32, z + 0.32)], "sign_blue", 0.008)
    stroke(f, (-0.1, z - 0.2), (-0.1, z + 0.2), 0.08, "sign_white")
    stroke(f, (-0.1, z + 0.17), (0.12, z + 0.17), 0.07, "sign_white")
    stroke(f, (0.1, z + 0.2), (0.1, z), 0.07, "sign_white")
    stroke(f, (-0.1, z + 0.02), (0.12, z + 0.02), 0.07, "sign_white")


sign("sign_parking", "Sign_Parking", sign_parking, tags=["info"])


def sign_town(g):
    """Panneau d'entrée d'agglomération : cartouche blanc à liseré rouge sur deux mâts (sans texte lisible)."""
    for x in (-0.75, 0.75):
        mast(g, 2.3, x)
    z = 1.95
    f = plate(g, [(-1.0, z - 0.35), (1.0, z - 0.35), (1.0, z + 0.35), (-1.0, z + 0.35)], "sign_red")
    f.poly([(-0.94, z - 0.29), (0.94, z - 0.29), (0.94, z + 0.29), (-0.94, z + 0.29)], "sign_white", 0.008)
    if g.lod == 0:
        for k, (a, b) in enumerate(((-0.7, -0.45), (-0.38, -0.05), (0.02, 0.3), (0.37, 0.7))):
            f.poly([(a, z - 0.1), (b, z - 0.1), (b, z + 0.1), (a, z + 0.1)], "black", 0.012)
    else:
        f.poly([(-0.7, z - 0.09), (0.7, z - 0.09), (0.7, z + 0.09), (-0.7, z + 0.09)], "black", 0.012)


sign("sign_town", "Sign_Town", sign_town, tags=["info", "town"], fp=(2.0, 0.2))


def arrow_plate(g, z, s, color, text="sign_white", L=1.3, h=0.26, y0=-0.03):
    """Flèche directionnelle partant du mât vers s (±1)."""
    pts = [(s * 0.08, z - h / 2), (s * (L - 0.18), z - h / 2), (s * L, z), (s * (L - 0.18), z + h / 2), (s * 0.08, z + h / 2)]
    f = plate(g, pts, color, y0)
    if g.lod == 0:
        f.poly([(s * 0.22, z - 0.04), (s * (L - 0.35), z - 0.04), (s * (L - 0.35), z + 0.04), (s * 0.22, z + 0.04)], text, 0.01)
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
            f.poly([(s * (L - 0.26), z - 0.085), (s * (L - 0.15), z - 0.085), (s * (L - 0.15), z + 0.085), (s * (L - 0.26), z + 0.085)],
                   "sign_red", 0.012)
    if g.lod == 0:
        g.box((0, -0.01, 1.6), (0.12, 0.04, 0.12), "sign_white")


sign("sign_hiking", "Sign_Hiking", sign_hiking, tags=["info", "hiking"], fp=(1.8, 0.2))


def chevron(g):
    """Balise de virage : panneau blanc à chevrons rouges pointant vers +X (retourner pour l'autre sens)."""
    mast(g, 1.5)
    z = 1.2
    f = plate(g, [(-0.3, z - 0.3), (0.3, z - 0.3), (0.3, z + 0.3), (-0.3, z + 0.3)], "sign_white")
    for x in (-0.2, 0.0):
        f.poly([(x, z + 0.24), (x + 0.12, z + 0.24), (x + 0.32, z), (x + 0.12, z - 0.24), (x, z - 0.24), (x + 0.2, z)],
               "sign_red", 0.01)


sign("chevron", "Chevron", chevron, tags=["curve", "small"], lods=1)


def delineator(g):
    g.prism([(-0.06, -0.035), (0.06, -0.035), (0.045, 0.035), (-0.045, 0.035)], -0.5, 1.05, "sign_white", axis='z')
    g.prism([(-0.062, -0.037), (0.062, -0.037), (0.047, 0.037), (-0.047, 0.037)], 0.72, 0.95, "black", axis='z')
    g.quad((0, -0.04, 0.835), 0.05, 0.14, '-y', "orange")
    g.quad((0, 0.04, 0.835), 0.05, 0.1, '+y', "sign_white")


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
    n = lod(g, 8, 5)
    pts = [(math.cos(math.pi * k / n) * 0.21, 0.5 + math.sin(math.pi * k / n) * 0.2) for k in range(n + 1)]
    g.prism(pts, -0.13, 0.13, "sign_red", axis='y')
    if g.lod == 0:
        f = Fac(g, 'y', -0.13, -1)
        digits(f, "7", 0, 0.28, 0.16, "black", 0.01)


sign("milestone", "Milestone", milestone, tags=["roadside", "small"], lods=2, collider="none")


def pass_sign(g):
    """Panneau de col : grand cartouche brun et bleu (altitude en chiffres) sur un socle de pierres."""
    for k in range(lod(g, 9, 5)):
        x = g.R(-1.3, 1.3)
        f = g.ico((x, g.R(-0.3, 0.4), 0.2), g.R(0.35, 0.6), 1, "rock" if k % 2 else "rock_light", scale=(1.2, 1, 0.8))
        g.jitter(f, 0.06)
    for x in (-0.9, 0.9):
        g.box((x, 0.05, 1.2), (0.14, 0.14, 2.8), "wood_dark")
    z = 2.0
    f = plate(g, [(-1.3, z - 0.55), (1.3, z - 0.55), (1.3, z + 0.55), (-1.3, z + 0.55)], "sign_brown", -0.02)
    f.poly([(-1.22, z - 0.0), (1.22, z - 0.0), (1.22, z + 0.47), (-1.22, z + 0.47)], "sign_blue", 0.008)
    f.poly([(-0.9, z + 0.16), (0.9, z + 0.16), (0.9, z + 0.32), (-0.9, z + 0.32)], "sign_white", 0.012)
    digits(f, "2108", 0.0, z - 0.27, 0.3, "sign_white")
    if g.lod == 0:
        f.poly([(-1.15, z - 0.47), (-0.75, z - 0.47), (-0.95, z - 0.12)], "sign_white", 0.012)       # petit sommet
        K.roof_gable(g, 0.5, 2.9, 2.62, 25, "wood_dark", 'x', over_e=0.05, over_g=0.1, thick=0.06)


sign("pass_sign", "Pass_Sign", pass_sign, tags=["info", "summit"], collider="box", fp=(2.8, 1.0))


def road_barrier_closed(g):
    """Barrière « route fermée » : lisse rayée rouge et blanc sur deux chevalets, disque sens interdit."""
    L = 4.0
    for x in (-L / 2 + 0.4, L / 2 - 0.4):
        g.beam((x, -0.4, 0.0), (x, 0.0, 1.05), 0.07, "metal")
        g.beam((x, 0.4, 0.0), (x, 0.0, 1.05), 0.07, "metal")
    n = 8 if g.lod == 0 else 1
    for k in range(n):
        x0 = -L / 2 + L * k / n
        g.box((x0 + L / n / 2, -0.03, 0.95), (L / n, 0.06, 0.26), "sign_red" if k % 2 == 0 else "sign_white")
    f = plate(g, ngon(0.3, lod(g, 10, 8), c=(0, 1.5)), "sign_red", -0.07)
    f.poly([(-0.2, 1.45), (0.2, 1.45), (0.2, 1.55), (-0.2, 1.55)], "sign_white", 0.01)
    g.box((0, -0.02, 1.2), (0.06, 0.06, 0.4), "metal")
    if g.lod == 0:
        for x in (-L / 2 + 0.15, L / 2 - 0.15):
            g.box((x, -0.03, 1.16), (0.14, 0.1, 0.14), "orange")


sign("road_barrier_closed", "Road_Barrier_Closed", road_barrier_closed, tags=["closed"], collider="box",
     fp=(4.0, 0.8))
