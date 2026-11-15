"""Monuments et services du pack alpin (modèles sur mesure). Façade principale = -Y."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac
from alp_buildings import spread


def fronts(g, w, d):
    """Les quatre façades d'un volume centré : avant, arrière, gauche (-X), droite (+X)."""
    return Fac(g, 'y', -d / 2, -1), Fac(g, 'y', d / 2, 1), Fac(g, 'x', -w / 2, -1), Fac(g, 'x', w / 2, 1)


# ----------------------------------------------------------------- église
def church(g):
    W, L = 10.5, 20.0
    cy = 0.2                                   # centre de la nef
    wall = "plaster_white"
    T, ty, TH = 5.6, -11.6, 21.0               # clocher : côté, centre, hauteur du fût
    # nef
    K.foundation(g, W, L, "stone", center=(0, cy))
    zr = K.gable_body(g, W, L, 0.0, 8.5, 52, wall, 'y', center=(0, cy))
    K.roof_gable(g, W, L, 8.5, 52, "roof_slate", 'y', over_e=0.5, over_g=0.3, center=(0, cy))
    # chœur (abside) et sa toiture conique
    n = lod(g, 8, 8, 6)
    g.frustum((0, cy + L / 2, -3.0), 4.2, 4.2, 10.5, n, wall, rot=math.pi / n, cap0=False)
    g.frustum((0, cy + L / 2, 7.5), 4.6, 0.0, 4.6, n, "roof_slate", rot=math.pi / n)
    # clocher
    K.foundation(g, T, T, "stone", center=(0, ty))
    g.box((0, ty, TH / 2), (T, T, TH), wall)
    r = (T / 2 + 0.35) * math.sqrt(2)
    g.frustum((0, ty, TH + 0.3), r, 0.0, 13.0, 4, "copper", rot=math.pi / 4)
    if g.lod < 2:
        g.box((0, ty, TH + 0.15), (T + 0.6, T + 0.6, 0.4), "stone")
        g.ico((0, ty, TH + 13.4), 0.3, 1, "brass")
        K.cross(g, 0, ty, TH + 13.5, 1.8, "brass", 0.12)
    tf = [Fac(g, 'y', ty - T / 2, -1), Fac(g, 'y', ty + T / 2, 1), Fac(g, 'x', -T / 2, -1), Fac(g, 'x', T / 2, 1)]
    for k, f in enumerate(tf):
        c = 0.0 if f.axis == 'y' else ty
        if k != 1:
            f.clock(c, 13.6, 1.15)
        if g.lod < 2 or k == 0:
            f.arch(c, 16.4, 1.7, 3.4, "black")
        if g.lod == 0:
            f.arch(c, 16.2, 2.1, 3.8, "stone", out=0.02)
            f.slab(c, 16.3, 2.4, 0.2, 0.15, "stone")
    if g.lod == 0:
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.box((sx * (T / 2 - 0.2), ty + sy * (T / 2 - 0.2), TH / 2), (0.5, 0.5, TH), "stone_warm")
    # portail
    pf = tf[0]
    if g.lod == 0:
        pf.arch(0, 0.0, 2.9, 4.3, "stone", out=0.03)
        pf.prism([(0, 4.6), (1.3, 4.2), (1.3, 4.4), (0, 4.85)], -2.0, 2.0, "roof_slate")
    pf.arch(0, 0.0, 2.2, 3.7, "door", out=0.06)
    K.steps(g, 0, ty - T / 2, 3.4, 3, color="stone")
    if g.lod < 2:
        pf.arch(0, 7.0, 1.0, 2.4, "window", lit=0.5)
    # baies de la nef et contreforts
    for sx in (-1, 1):
        f = Fac(g, 'x', sx * W / 2, sx)
        us = spread(4, cy - L / 2 + 1.2, cy + L / 2 - 1.2)
        for u in us:
            if g.lod == 0:
                f.arch(u, 2.4, 1.7, 5.0, "stone", out=0.02)
            if g.lod < 2 or True:
                f.arch(u, 2.6, 1.3, 4.6, "window", out=0.05, lit=0.45)
        if g.lod == 0:
            for u in [cy - L / 2 + 0.4] + [(us[k] + us[k + 1]) / 2 for k in range(3)] + [cy + L / 2 - 0.4]:
                f.prism([(0, 0), (0.9, 0), (0.9, 4.5), (0.45, 6.5), (0, 6.5)], u - 0.3, u + 0.3, "stone_warm")
    # sacristie
    sx0, sy0 = W / 2 + 1.9, cy + 5.5
    K.foundation(g, 3.8, 5.0, "stone", center=(sx0, sy0))
    g.box((sx0, sy0, 1.7), (3.8, 5.0, 3.4), wall)
    K.roof_shed(g, 3.8, 5.0, 4.6, 3.4, "roof_slate", over=0.3, center=(sx0 + 0.1, sy0), low='+x')
    sf = Fac(g, 'y', sy0 - 2.5, -1)
    sf.door(sx0, 0.9, 2.0, "door", frame=None)
    Fac(g, 'x', sx0 + 1.9, 1).window(sy0, 1.8, 0.8, 1.0, None, frame="stone", keep=False)


reg(dict(id="church", file="Church", cat="landmark", tags=["church"], seed=301, fp=(10.5, 28.8), grad_h=22.0,
         build=church))


# ----------------------------------------------------------------- mairie
def townhall(g):
    W, D = 18.0, 12.0
    gh, fh = 4.2, 3.3
    H = gh + 2 * fh
    plaster, band = "plaster_ochre", "plaster_white"
    K.foundation(g, W, D, "stone", top=0.4)
    g.box((0, 0, H / 2), (W, D, H), plaster)
    zr = K.roof_hip(g, W, D, H, 38, "roof_tile", over=0.6)
    front, back, left, right = fronts(g, W, D)
    us = spread(5, -W / 2 + 0.7, W / 2 - 0.7)
    # arcades du rez-de-chaussée
    for u in us:
        front.arch(u, 0.0, 2.5, 3.5, "black", out=0.04)
        if g.lod == 0:
            front.arch(u, 0.0, 2.9, 3.75, band, out=0.02)
    if g.lod == 0:
        front.slab(0, gh, W + 0.1, 0.3, 0.14, band)
        front.slab(0, H - 0.2, W + 0.1, 0.4, 0.2, band)
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.box((sx * (W / 2 - 0.2), sy * (D / 2 - 0.2), H / 2), (0.55, 0.55, H), band)
        front.quad(0, 1.2, 1.6, 2.4, "door", out=0.05)
    # étages
    for L in (1, 2):
        z = gh + (L - 1) * fh + 1.7
        for k, u in enumerate(us):
            if L == 1 and k == 2:
                front.door(u, 1.3, 2.5, "window_lit", frame=band, z0=gh + 0.25)
            else:
                front.window(u, z, 1.15, 1.8, "barn", frame=band, cross=True)
        for f, n, c in ((left, 3, 0), (right, 3, 0)):
            for u in spread(n, -D / 2 + 0.6, D / 2 - 0.6):
                f.window(u, z, 1.1, 1.7, "barn", frame=band)
        for u in us:
            back.window(u, z, 1.1, 1.7, None, frame=None, keep=False)
    for f in (left, right):
        for u in spread(3, -D / 2 + 0.6, D / 2 - 0.6):
            f.window(u, 2.2, 1.1, 1.9, None, frame=band, keep=False)
    back.door(0, 1.2, 2.3, "door", frame=band)
    # balcon d'honneur et drapeaux
    front.balcony(us[2] - 1.9, us[2] + 1.9, gh + 0.25, 1.0, style="iron", rail_h=1.0, flowers="geranium")
    if g.lod < 2:
        for s, c in ((-1, "red"), (1, "blue")):
            m = g.mark()
            g.frustum((0, 0, 0), 0.04, 0.03, 3.0, 4, "metal", cap0=False)
            g.box((0.0, -0.02, 2.35), (0.05, 0.9, 1.3), c)
            g.transform(g.since(m), Matrix.Translation((s * 1.7, -D / 2 - 0.95, gh + 1.2)) @ Matrix.Rotation(math.radians(35), 4, 'X') @ Matrix.Translation((0, -0.45, 0)))
    # fronton à horloge et clocheton
    g.box((0, -D / 2 + 0.3, H + 1.6), (4.4, 1.0, 3.2), plaster)
    g.prism([(-2.5, H + 3.2), (2.5, H + 3.2), (0, H + 4.9)], -D / 2 - 0.25, -D / 2 + 0.9, band if g.lod == 0 else plaster, axis='y')
    Fac(g, 'y', -D / 2 - 0.2, -1).clock(0, H + 1.9, 1.05)
    bz = zr - 0.4
    g.box((0, 0, bz + 1.3), (1.9, 1.9, 2.6), band)
    K.roof_pyramid(g, 1.9, 1.9, bz + 2.6, 2.8, "copper", over=0.3)
    if g.lod < 2:
        for f in (Fac(g, 'y', -0.95, -1), Fac(g, 'y', 0.95, 1), Fac(g, 'x', -0.95, -1), Fac(g, 'x', 0.95, 1)):
            f.arch(0, bz + 1.0, 0.9, 1.4, "black", out=0.03)
        g.frustum((0, 0, bz + 5.4), 0.06, 0.02, 1.2, 4, "brass", cap0=False)
    K.chimney(g, -W / 2 + 2.5, 1.5, H + 1.5, zr + 0.3, plaster, 0.8)
    K.chimney(g, W / 2 - 2.5, 1.5, H + 1.5, zr + 0.3, plaster, 0.8)


reg(dict(id="townhall", file="TownHall", cat="landmark", tags=["townhall"], seed=302, fp=(18.0, 12.0), build=townhall))


# ----------------------------------------------------------------- grand hôtel
def hotel_grand(g):
    W, D = 30.0, 14.0
    gh, fh, floors = 4.2, 3.1, 5
    H = gh + (floors - 1) * fh
    plaster, band, base = "plaster_cream", "plaster_white", "stone_warm"
    yf = -D / 2
    K.foundation(g, W, D, "stone", top=0.4)
    g.box((0, 0, gh / 2), (W, D, gh), base)
    g.box((0, 0, (H + gh) / 2), (W, D, H - gh), plaster)
    K.roof_frustum(g, W, D, H, 3.0, 1.5, "roof_slate", over=0.35, cap="roof_metal")
    # avant-corps : pavillon central et pavillons d'angle, toits plus hauts
    pav = [(0.0, 8.4, 5.6), (-(W / 2 - 2.9), 5.8, 4.6), (W / 2 - 2.9, 5.8, 4.6)]
    for x, pw, rh in pav:
        if g.lod < 2:
            g.box((x, yf - 0.45, gh / 2 - 1.5), (pw, 1.0, gh + 3.0), base)
            g.box((x, yf - 0.45, (H + gh) / 2), (pw, 1.0, H - gh), plaster)
        else:
            g.box((x, yf - 0.45, H / 2 - 1.5), (pw, 1.0, H + 3.0), plaster)
        K.roof_frustum(g, pw, 6.0, H, rh, 1.7, "roof_slate", over=0.35, cap="roof_metal", center=(x, yf + 2.5))
        if g.lod == 0:
            g.box((x, yf - 0.5, H - 0.2), (pw + 0.3, 1.2, 0.4), band)
    front = Fac(g, 'y', yf, -1)
    if g.lod == 0:
        front.slab(0, gh, W, 0.3, 0.14, band)
        front.slab(0, H - 0.2, W, 0.4, 0.2, band)
    bays = []                                   # (u, façade)
    pf = Fac(g, 'y', yf - 0.95, -1)
    for x, pw, rh in pav:
        n = 3 if pw > 7 else 2
        bays += [(u, pf, x == 0.0) for u in spread(n, x - pw / 2 + 0.3, x + pw / 2 - 0.3)]
    for s in (-1, 1):
        bays += [(s * u, front, False) for u in spread(3, 4.5, W / 2 - 6.1)]
    for L in range(1, floors):
        z0 = gh + (L - 1) * fh
        if g.lod == 2:
            # au loin : un bandeau vitré par étage et par corps de façade
            for x, pw, rh in pav:
                pf.quad(x, z0 + 1.55, pw - 1.6, 1.9, "window", 0.05)
            for s in (-1, 1):
                front.quad(s * (4.5 + W / 2 - 6.1) / 2, z0 + 1.55, W / 2 - 6.1 - 4.5 - 0.6, 1.9, "window", 0.05)
            continue
        for u, f, central in bays:
            f.window(u, z0 + 1.55, 1.15, 2.0, None, frame=band, lit=0.55)
            if f is front and L == 1:
                f.balcony(u - 0.9, u + 0.9, z0 + 0.4, 0.6, style="iron", rail_h=0.9, flowers="geranium")
        pf.balcony(-3.9, 3.9, z0 + 0.4, 0.9, style="iron", rail_h=0.95, flowers="geranium" if L < 3 else None)
        if L in (2, 4):
            for x, pw, rh in pav[1:]:
                pf.balcony(x - 2.6, x + 2.6, z0 + 0.4, 0.8, style="iron", rail_h=0.95, flowers=None)
    # rez-de-chaussée : grandes baies cintrées, entrée sous marquise
    for u, f, central in bays:
        if (central and abs(u) < 0.1) or g.lod == 2:
            continue
        f.arch(u, 0.7, 1.6, 3.0, "window", out=0.05, lit=0.8)
    pf = Fac(g, 'y', yf - 0.95, -1)
    pf.door(0, 2.2, 3.0, "window_lit", frame=band)
    if g.lod < 2:
        g.box((0, yf - 2.6, 3.75), (5.6, 3.4, 0.22), "roof_metal")
        for sx in (-1, 1):
            g.box((sx * 2.6, yf - 4.1, 1.85), (0.14, 0.14, 3.7), "metal_dark")
        g.box((0, yf - 4.3, 3.45), (5.6, 0.06, 0.45), "awning_red")
    K.steps(g, 0, yf - 0.95, 4.6, 3, color="stone")
    # enseigne lumineuse sur le pavillon central
    if g.lod < 2:
        g.box((0, yf + 0.9, H + 5.6 + 0.75), (6.0, 0.14, 1.1), "metal_dark")
        g.box((0, yf + 0.8, H + 5.6 + 0.75), (5.2, 0.1, 0.55), "neon_red")
        K.flag(g, 0, yf + 2.5, H + 5.6, 4.5, "red", (1.6, 1.0))
    # lucarnes du brisis
    if g.lod < 2:
        for s in (-1, 1):
            for u in spread(3, 4.5, W / 2 - 6.1):
                g.box((s * u, yf + 0.75, H + 1.25), (1.2, 1.3, 1.5), band)
                g.box((s * u, yf + 0.65, H + 2.05), (1.5, 1.5, 0.12), "roof_metal")
                Fac(g, 'y', yf + 0.1, -1).quad(s * u, H + 1.25, 0.8, 1.0, front.glass(s * u, H + 1.25, 0.5), 0.02)
    # côtés et arrière
    back, left, right = Fac(g, 'y', D / 2, 1), Fac(g, 'x', -W / 2, -1), Fac(g, 'x', W / 2, 1)
    for L in range(0, floors):
        z = (gh + (L - 1) * fh + 1.55) if L else 2.2
        for f in (left, right):
            for u in spread(4, -D / 2 + 0.8, D / 2 - 0.8):
                f.window(u, z, 1.1, 1.9, None, frame=None, lit=0.5, keep=False)
        for u in spread(11, -W / 2 + 0.6, W / 2 - 0.6):
            back.window(u, z, 1.1, 1.8, None, flat=True)
    for x in (-9.0, -4.0, 4.0, 9.0)[:lod(g, 4, 4, 0)]:
        K.chimney(g, x, 2.0, H + 2.4, H + 4.6, plaster, 0.8)


reg(dict(id="hotel_grand", file="Hotel_Grand", cat="landmark", tags=["hotel"], seed=303, fp=(30.0, 15.0),
         grad_h=20.0, build=hotel_grand))
