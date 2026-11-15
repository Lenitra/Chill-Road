"""Pack alpin, suite des monuments : hôtel-chalet, résidence de station, services (école, pompiers, station-service,
supermarché, garage, scierie, fromagerie), montagne (restaurant d'altitude, refuge, tour en ruine, pylône radio,
croix de sommet), place (monument, fontaine, kiosque), moulin. Façade principale = -Y."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac
from alp_buildings import spread, chalet
from alp_landmarks import fronts


def grid_windows(g, w, d, zs, nf=None, ns=None, nbk=None, ww=1.1, wh=1.4, shutter=None, frame="plaster_white",
                 lit=0.5, skip=(), flower=None, cx=0.0, cy=0.0):
    """Fenêtres en grille sur les quatre façades d'un volume (w × d) centré en (cx, cy) ; zs = hauteurs des centres.
    skip : couples (niveau, travée) à laisser libres sur la façade avant."""
    nf = nf or max(2, round(w / 3.0))
    ns = ns or max(1, round(d / 3.2))
    nbk = nbk or nf
    front, back = Fac(g, 'y', cy - d / 2, -1), Fac(g, 'y', cy + d / 2, 1)
    left, right = Fac(g, 'x', cx - w / 2, -1), Fac(g, 'x', cx + w / 2, 1)
    for L, z in enumerate(zs):
        for k, u in enumerate(spread(nf, cx - w / 2 + 0.4, cx + w / 2 - 0.4)):
            if (L, k) not in skip:
                front.window(u, z, ww, wh, shutter, frame=frame, lit=lit, flower=flower)
        for f in (left, right):
            for u in spread(ns, cy - d / 2 + 0.4, cy + d / 2 - 0.4):
                f.window(u, z, ww, wh, shutter, frame=frame, lit=lit, keep=False)
        for u in spread(nbk, cx - w / 2 + 0.4, cx + w / 2 - 0.4):
            back.window(u, z, ww, wh, None, flat=True)
    return front, back, left, right


# ================================================================= lot 2
def hotel_chalet(g):
    W, D = 24.0, 14.0
    chalet(g, w=W, d=D, floors=4, ground="plaster_white", wood="wood_honey", trim="wood_dark", roof="roof_brown",
           pitch=17, balconies=(1, 2, 3), over_f=2.3, over_s=1.5, knee=0.5, gh=3.3, fh=2.8, door_u=0.0,
           shutter="shutter", flowers="geranium")
    yf = -D / 2
    if g.lod < 2:
        g.box((0, yf - 2.0, 3.0), (5.0, 2.6, 0.2), "wood_dark")                  # auvent d'entrée
        for sx in (-1, 1):
            g.box((sx * 2.3, yf - 3.1, 1.2), (0.2, 0.2, 3.4), "wood_dark")
        g.box((0, yf - 3.25, 3.3), (4.2, 0.12, 0.6), "wood_dark")
        g.box((0, yf - 3.33, 3.3), (3.4, 0.06, 0.32), "neon_red")
        for sx, c in ((-1, "red"), (1, "white")):
            K.flag(g, sx * 6.5, yf - 2.2, -0.5, 7.5, c, (1.5, 1.0))
    K.steps(g, 0, yf, 4.0, 2, color="stone")


reg(dict(id="hotel_chalet", file="Hotel_Chalet", cat="landmark", tags=["hotel", "station"], seed=304,
         fp=(24.0, 14.0), build=hotel_chalet))


def apartment_ski(g):
    """Résidence de station des années 70 : béton clair, balcons filants en bois, toit à faible pente débordant."""
    W, D = 26.0, 13.0
    gh, fh, floors = 3.2, 2.8, 6
    H = gh + (floors - 1) * fh
    yf = -D / 2
    sx0 = W * 0.18                                # cage d'escalier
    K.foundation(g, W, D, "concrete", top=0.3)
    g.box((0, 0, gh / 2), (W, D, gh), "concrete")
    zr = K.gable_body(g, D, W, gh, H, 14, "plaster_white", 'x')
    K.roof_gable(g, D, W, H, 14, "roof_metal", 'x', over_e=1.6, over_g=0.9, thick=0.3, trim="wood_dark")
    front, back, left, right = fronts(g, W, D)
    g.box((sx0, yf - 0.4, (H + 1.2) / 2 - 1.5), (3.4, 1.0, H + 1.2 + 3.0), "wood_dark")
    tf = Fac(g, 'y', yf - 0.9, -1)
    if g.lod < 2:
        for L in range(floors):
            tf.quad(sx0, gh + (L - 1) * fh + 1.5 if L else 1.9, 1.2, 1.5, tf.glass(sx0, L, 0.7), 0.03)
        for f in (left, right):
            f.slab(0, (H + gh) / 2 + 0.5, D * 0.5, H - gh - 1.5, 0.08, "wood_dark")
    segs = [(-W / 2 + 0.2, sx0 - 1.9), (sx0 + 1.9, W / 2 - 0.2)]
    for L in range(1, floors):
        z0 = gh + (L - 1) * fh
        for a, b in segs:
            front.balcony(a, b, z0 + 0.1, 1.5, "wood_honey", rail_h=1.05, flowers=None, floor="concrete")
            n = max(1, round((b - a) / 3.2))
            if g.lod < 2:
                for u in spread(n, a, b):
                    front.quad(u, z0 + 1.25, 2.2, 2.1, front.glass(u, z0, 0.5), 0.03)
        for u in spread(8, -W / 2 + 0.5, W / 2 - 0.5):
            back.window(u, z0 + 1.5, 1.2, 1.2, None, flat=True)
        for f in (left, right):
            for u in (-D * 0.36, D * 0.36):
                f.window(u, z0 + 1.5, 1.0, 1.2, None, frame=None, keep=False)
    for a, b in segs:
        if g.lod < 2:
            front.quad((a + b) / 2, 1.5, b - a - 1.0, 2.3, "window_lit", 0.03)
            front.sign((a + b) / 2, gh - 0.25, b - a - 0.6, 0.45, "blue" if a < 0 else "red", "white")
    tf.door(sx0, 1.6, 2.3, "window", frame="concrete")
    for x in (-8.0, 6.0):
        K.chimney(g, x, 1.5, H + 1.0, zr + 0.8, "concrete", 0.9)


reg(dict(id="apartment_ski", file="Apartment_Ski", cat="ski", tags=["residence", "habitat"], seed=305,
         fp=(26.0, 14.0), grad_h=18.0, build=apartment_ski))


# ================================================================= lot 3
def onion(g, x, y, z, r, color="copper", n=None):
    """Bulbe : empilement de troncs de cône + flèche."""
    n = n or lod(g, 8, 6, 5)
    prof = [(0.62, 0.0), (1.0, 0.45), (1.05, 0.8), (0.7, 1.35), (0.22, 1.8), (0.0, 2.9)]
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        g.frustum((x, y, z + z0 * r), r0 * r, r1 * r, (z1 - z0) * r, n, color, cap0=False, cap1=False)


def chapel(g):
    W, L = 5.6, 8.5
    K.foundation(g, W, L, "stone")
    zr = K.gable_body(g, W, L, 0.0, 3.6, 50, "plaster_white", 'y')
    K.roof_gable(g, W, L, 3.6, 50, "roof_brown", 'y', over_e=0.45, over_g=0.7, thick=0.2)
    n = lod(g, 6, 6, 5)
    g.frustum((0, L / 2, -3.0), 2.3, 2.3, 6.2, n, "plaster_white", rot=math.pi / n, cap0=False)
    g.frustum((0, L / 2, 3.2), 2.6, 0.0, 2.8, n, "roof_brown", rot=math.pi / n)
    ty = -L / 2 + 1.4                               # clocheton à bulbe
    g.box((0, ty, zr + 0.6), (1.5, 1.5, 2.4), "plaster_white")
    onion(g, 0, ty, zr + 1.8, 1.0)
    if g.lod < 2:
        K.cross(g, 0, ty, zr + 4.6, 0.9, "brass", 0.07)
        for f in (Fac(g, 'y', ty - 0.75, -1), Fac(g, 'x', -0.75, -1), Fac(g, 'x', 0.75, 1)):
            f.arch(0 if f.axis == 'y' else ty, zr + 0.5, 0.6, 1.1, "black", out=0.03)
    front = Fac(g, 'y', -L / 2, -1)
    if g.hd:
        front.arch_window(0, 0.0, 1.3, 2.5, glass="door", surround="stone", bars=0, sash=None, depth=0.16)
        for uu in (-0.4, -0.13, 0.13, 0.4):
            front.quad(uu, 1.0, 0.016, 1.9, "wood_dark", 0.02)
        for zz in (0.5, 1.5):
            front.slab(0, zz, 1.15, 0.06, 0.02, "metal_dark", out=0.015)
        g.octa(front.P(0.4, 0.06, 1.05), 0.05, "brass")
        front.lantern(1.2, 2.3)
        K.cone_roofing(g, (0, L / 2, 3.2), 2.6, 0.0, 2.8, 2 * n, "roof_brown", rot=math.pi / n, cap=40)
    else:
        front.arch(0, 0.0, 1.3, 2.5, "door", out=0.05)
    if g.lod == 0 and not g.hd:
        front.arch(0, 0.0, 1.7, 2.8, "stone", out=0.02)
        front.poly([(-0.4, 4.0), (0.4, 4.0), (0.4, 4.8), (-0.4, 4.8)], "window", 0.04)
    for sx in (-1, 1):
        f = Fac(g, 'x', sx * W / 2, sx)
        for u in (-1.6, 1.6):
            if g.hd:
                f.arch_window(u, 1.3, 0.7, 1.9, surround="stone", lit=0.4, bars=2, depth=0.1)
            else:
                f.arch(u, 1.3, 0.7, 1.9, "window", out=0.04, lit=0.4)
    K.steps(g, 0, -L / 2, 2.0, 2, color="stone")


reg(dict(id="chapel", file="Chapel", cat="landmark", tags=["chapel"], seed=310, fp=(5.6, 10.8), build=chapel))


def school(g):
    W, D = 20.0, 10.0
    gh, fh = 3.6, 3.4
    H = gh + fh
    K.foundation(g, W, D, "stone", top=0.5)
    g.box((0, 0, H / 2), (W, D, H), "plaster_salmon")
    zr = K.roof_hip(g, W, D, H, 32, "roof_tile", over=0.7)
    front, back, left, right = grid_windows(g, W, D, [gh * 0.55, gh + fh * 0.5], nf=7, ww=1.6, wh=1.9,
                                            frame="plaster_white", lit=0.3, skip=((0, 3),))
    if g.lod == 0:
        front.slab(0, gh, W, 0.25, 0.12, "plaster_white")
    # entrée : porche à fronton ; fronton à horloge au-dessus de l'égout
    g.box((0, -D / 2 - 0.9, 1.7), (3.6, 1.8, 3.4), "plaster_white")
    g.prism([(-2.1, 3.4), (2.1, 3.4), (0, 4.7)], -D / 2 - 2.0, -D / 2, "roof_tile", axis='y')
    Fac(g, 'y', -D / 2 - 1.8, -1).door(0, 1.8, 2.5, "door", frame=None, glass=True)
    g.box((0, -D / 2 + 0.2, H + 0.3), (2.6, 0.5, 2.2), "plaster_salmon")
    g.prism([(-1.6, H + 1.4), (1.6, H + 1.4), (0, H + 2.5)], -D / 2 - 0.2, -D / 2 + 0.6, "roof_tile", axis='y')
    Fac(g, 'y', -D / 2 - 0.05, -1).clock(0, H + 0.45, 0.75)
    K.steps(g, 0, -D / 2 - 1.8, 3.0, 3, color="stone")
    K.flag(g, -3.2, -D / 2 - 1.5, -0.5, 7.5, "red", (1.4, 0.9))
    px = W / 2 + 3.5                                 # préau
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.box((px + sx * 3.0, sy * 2.6 + 1.0, 1.25), (0.2, 0.2, 3.5), "wood")
    K.roof_shed(g, 6.6, 5.8, 3.4, 2.6, "roof_tile", over=0.3, center=(px, 1.0), low='-y')
    K.chimney(g, -6.0, 1.0, H + 1.5, zr + 0.5, "plaster_salmon", 0.8)


reg(dict(id="school", file="School", cat="landmark", tags=["school"], seed=311, fp=(20.0, 10.0), build=school))


def garage_door(f, u, w, h, color="sign_red", glass=True):
    g = f.g
    if g.lod == 0:
        f.slab(u, h / 2, w + 0.3, h + 0.15, 0.09, "concrete_dark")
        f.slab(u, h / 2, w, h, 0.1, color)
        if glass:
            f.slab(u, h * 0.66, w * 0.86, h * 0.16, 0.12, "window")
    else:
        f.quad(u, h / 2, w, h, color, 0.05)


def firestation(g):
    W, D, H = 16.0, 11.0, 7.4
    K.foundation(g, W, D, "concrete", top=0.3)
    g.box((0, 0, H / 2), (W, D, H), "plaster_white")
    K.roof_hip(g, W, D, H, 16, "roof_metal", over=0.5)
    front, back, left, right = fronts(g, W, D)
    for u in (-5.6, -0.6):
        garage_door(front, u, 3.6, 3.9)
    front.door(5.0, 1.0, 2.2, "sign_red", frame="plaster_white", glass=True)
    front.window(6.8, 1.8, 1.2, 1.2, None, frame="concrete", lit=0.8)
    if g.lod < 2:
        front.slab(0, 4.45, W, 0.5, 0.08, "sign_red")
        front.slab(-3.1, 4.45, 3.0, 0.3, 0.1, "white")
    for u in spread(6, -W / 2 + 0.5, W / 2 - 0.5):
        front.window(u, 6.0, 1.2, 1.2, None, frame="concrete", lit=0.5)
        back.window(u, 6.0, 1.2, 1.2, None, flat=True)
    for f in (left, right):
        for u in spread(3, -D / 2 + 0.5, D / 2 - 0.5):
            f.window(u, 6.0, 1.2, 1.2, None, frame="concrete", keep=False)
    tx, ty = W / 2 - 1.7, D / 2 - 1.7              # tour de séchage des tuyaux
    g.box((tx, ty, 7.5), (3.2, 3.2, 15.0), "plaster_white")
    K.roof_pyramid(g, 3.2, 3.2, 15.0, 1.6, "sign_red", over=0.35, center=(tx, ty))
    if g.lod < 2:
        for f in (Fac(g, 'y', ty - 1.6, -1), Fac(g, 'x', tx + 1.6, 1)):
            c = tx if f.axis == 'y' else ty
            for z in (9.5, 12.5):
                f.quad(c, z, 1.6, 1.6, "metal_dark", 0.03)
        g.frustum((tx, ty, 16.6), 0.3, 0.45, 0.5, 6, "sign_red")


reg(dict(id="firestation", file="FireStation", cat="landmark", tags=["service"], seed=312, fp=(16.0, 11.0),
         grad_h=12.0, build=firestation))


def fuel_pump(g, x, y):
    g.box((x, y, 0.9), (0.6, 0.9, 1.8), "white")
    if g.lod < 2:
        g.box((x, y, 1.55), (0.64, 0.94, 0.4), "sign_red")
        g.box((x - 0.31, y, 1.1), (0.04, 0.5, 0.4), "black")
        g.box((x + 0.31, y, 1.1), (0.04, 0.5, 0.4), "black")


def gasstation(g):
    """Station-service : auvent sur deux piliers, deux îlots de pompes (les voitures passent sous l'auvent le long
    de X), boutique à l'arrière, totem des prix."""
    g.box((0, -2.5, 5.3), (14.0, 8.0, 0.7), "white")
    if g.lod < 2:
        g.box((0, -2.5, 5.3), (14.1, 8.1, 0.3), "sign_red")
        g.box((0, -2.5, 4.94), (11.0, 5.0, 0.04), "lamp")
    for x in (-4.0, 4.0):
        g.box((x, -2.5, 2.2), (0.5, 0.5, 5.9), "white")
        g.box((x, -2.5, 0.09), (4.2, 1.3, 0.18), "concrete")
        fuel_pump(g, x - 1.2, -2.5)
        fuel_pump(g, x + 1.2, -2.5)
    sw, sd, sh = 10.0, 6.0, 3.6
    cy = 5.0
    K.foundation(g, sw, sd, "concrete", top=0.2, center=(0, cy))
    g.box((0, cy, sh / 2), (sw, sd, sh), "plaster_white")
    g.box((0, cy, sh + 0.15), (sw + 0.5, sd + 0.5, 0.3), "sign_red")
    f = Fac(g, 'y', cy - sd / 2, -1)
    if g.lod < 2:
        f.quad(-1.2, 1.6, 6.4, 2.4, "window_lit", 0.03)
    f.door(3.4, 1.2, 2.3, "window_lit", frame="metal")
    g.box((-8.5, -6.0, 3.0), (0.3, 0.3, 7.0), "metal")                            # totem
    g.box((-8.5, -6.0, 5.0), (1.6, 0.35, 3.0), "white")
    if g.lod < 2:
        g.box((-8.5, -6.2, 6.0), (1.3, 0.06, 0.7), "neon_red")
        g.box((-8.5, -6.2, 4.6), (1.3, 0.06, 1.4), "black")
        g.box((6.5, 1.7, 0.5), (0.5, 0.5, 1.0), "sign_blue")
        for k in range(3):
            g.frustum((7.2, 2.6 + k * 0.7, 0.0), 0.3, 0.3, 0.9, 6, "sign_blue" if k % 2 else "sign_red")


reg(dict(id="gasstation", file="GasStation", cat="landmark", tags=["service", "fuel"], seed=313, fp=(18.0, 16.0),
         collider="mesh", lights=[(-3.5, -2.5, 4.9), (3.5, -2.5, 4.9)], build=gasstation))


def supermarket(g):
    W, D, H = 24.0, 16.0, 5.6
    K.foundation(g, W, D, "concrete", top=0.2)
    g.box((0, 0, H / 2), (W, D, H), "plaster_white")
    g.box((0, 0, H + 0.25), (W + 0.3, D + 0.3, 0.5), "concrete")
    front, back, left, right = fronts(g, W, D)
    if g.lod < 2:
        front.quad(-2.5, 1.7, 15.0, 2.8, "window_lit", 0.03)
        if g.lod == 0:
            for u in (-7.0, -4.0, -1.0, 2.0):
                front.slab(u, 1.7, 0.1, 2.8, 0.06, "metal")
    front.sign(-2.5, 4.3, 12.0, 1.3, "green", "yellow", depth=0.15)
    front.door(8.5, 2.4, 2.6, "window_lit", frame="metal")
    if g.lod < 2:
        g.box((8.5, -D / 2 - 1.4, 3.1), (4.4, 2.8, 0.2), "green")
        for sx in (-1, 1):
            g.box((8.5 + sx * 2.0, -D / 2 - 2.6, 1.3), (0.14, 0.14, 3.6), "metal")
        g.box((-9.0, -D / 2 - 2.0, 2.2), (4.0, 2.2, 0.1), "glass_blue")           # abri à chariots
        for sx in (-1, 1):
            g.box((-9.0 + sx * 1.9, -D / 2 - 2.0, 0.85), (0.1, 2.0, 2.7), "metal")
        g.box((-9.0, -D / 2 - 1.9, 0.5), (3.0, 1.2, 0.8), "metal")
        for x, y in ((-6, 2), (3, -3), (7, 4)):
            g.box((x, y, H + 1.0), (2.2, 1.6, 1.0), "metal")
    back.door(-6.0, 3.0, 3.2, "metal", frame=None)


reg(dict(id="supermarket", file="Supermarket", cat="landmark", tags=["service", "shop"], seed=314, fp=(24.0, 16.0),
         build=supermarket))


def garage(g):
    """Garage automobile : atelier à deux portes, bureau vitré, pile de pneus, bidons."""
    W, D, H = 14.0, 10.0, 5.0
    K.foundation(g, W, D, "concrete", top=0.2)
    g.box((0, 0, H / 2), (W, D, H), "plaster_blue")
    g.prism([(-D / 2, H), (D / 2, H), (-D / 2, H + 1.2)], -W / 2, W / 2, "plaster_blue", axis='x')
    K.roof_shed(g, W, D, H + 1.2, H, "roof_metal", over=0.4, low='+y')
    front, back, left, right = fronts(g, W, D)
    garage_door(front, -4.2, 3.4, 3.6, "white")
    if g.lod < 2:
        front.quad(0.1, 1.8, 3.4, 3.6, "black", 0.03)                               # porte ouverte
    front.window(4.3, 1.9, 2.4, 1.6, None, frame="white", lit=0.8)
    front.door(6.3, 0.9, 2.1, "white", frame=None, glass=True)
    front.sign(0, 4.9, 9.0, 0.9, "yellow", "black", depth=0.1)
    if g.lod == 0:
        for k in range(4):
            g.frustum((W / 2 + 0.7, -3.0, k * 0.24), 0.36, 0.36, 0.24, 8, "rubber")
        for k, c in enumerate(("sign_blue", "rust", "sign_blue")):
            g.frustum((W / 2 + 0.6, -1.4 + k * 0.7, 0.0), 0.3, 0.3, 0.9, 7, c)
    for u in (-3.0, 3.0):
        right.window(u, 3.0, 1.6, 1.0, None, frame=None, keep=False)
        left.window(u, 3.0, 1.6, 1.0, None, frame=None, keep=False)


reg(dict(id="garage", file="Garage", cat="landmark", tags=["service"], seed=315, fp=(14.0, 10.0), build=garage))


def log_pile(g, x, y, n_base, L, r=0.28, rotz=0.0, color="bark"):
    """Pile de grumes (pyramide) le long de Y."""
    m = g.mark()
    if g.lod == 2:
        g.prism([(-n_base * r, 0), (n_base * r, 0), (r, n_base * r * 1.7), (-r, n_base * r * 1.7)], -L / 2, L / 2,
                color, axis='y')
    else:
        n = lod(g, 6, 5)
        for row in range(n_base):
            for k in range(n_base - row):
                cx = (k - (n_base - row - 1) / 2) * r * 2.05
                z = r + row * r * 1.75
                f = g.limb((cx, -L / 2 + g.R(-0.2, 0.2), z), (cx, L / 2 + g.R(-0.2, 0.2), z), r, r * 0.92, n, color,
                           cap0=True, cap1=True)
                g.paint(g.caps(f), "wood_cut")
    g.move(g.since(m), (x, y, 0), rotz)


def plank_stack(g, x, y, w=1.2, L=4.0, h=1.2, rotz=0.0):
    m = g.mark()
    if g.lod == 0:
        layers = 5
        for k in range(layers):
            g.box((0, 0, h * (k + 0.5) / layers), (w, L - (k % 2) * 0.3, h / layers - 0.04),
                  "wood_cut" if k % 2 == 0 else "wood_honey")
    else:
        g.box((0, 0, h / 2), (w, L, h), "wood_cut")
    g.move(g.since(m), (x, y, 0), rotz)


def sawmill(g):
    W, D, H = 16.0, 8.0, 4.6
    for x in spread(4, -W / 2 - 2.6, W / 2 + 2.6):
        for y in (-D / 2, D / 2):
            g.box((x, y, H / 2 - 0.25), (0.3, 0.3, H + 0.5), "wood")
    K.roof_gable(g, D, W, H, 20, "rust", 'x', over_e=0.8, over_g=0.6, thick=0.14)
    g.box((0, D / 2, H * 0.5), (W, 0.14, H), "wood_grey")                          # fond bardé
    g.box((-W / 2 + 0.1, 0.5, H * 0.5), (0.14, D - 1.0, H), "wood_grey")
    K.foundation(g, W, D, "concrete", top=0.12)
    g.box((0, 0, 0.55), (9.0, 1.2, 0.5), "metal_dark")                             # banc de scie
    if g.lod < 2:
        g.limb((-3.5, 0, 1.15), (1.5, 0, 1.15), 0.36, 0.33, lod(g, 7, 5), "bark", cap0=True, cap1=True)
        g.box((2.6, 0, 1.3), (1.5, 0.06, 1.5), "metal", rot=Matrix.Rotation(0.78, 3, 'Y'))
    plank_stack(g, 5.5, 1.8, 1.3, 4.5, 1.3, 1.57)
    plank_stack(g, 5.5, -1.6, 1.3, 4.5, 0.9, 1.57)
    plank_stack(g, W / 2 + 2.2, -1.0, 1.3, 4.0, 1.5)
    g.frustum((-5.5, 2.4, 0), 1.4, 0.0, 1.1, lod(g, 7, 5, 4), "wood_cut")           # tas de sciure
    log_pile(g, -W / 2 - 3.2, -0.5, 4, 6.0, 0.32)
    log_pile(g, -W / 2 - 6.0, 0.5, 3, 5.5, 0.3)


reg(dict(id="sawmill", file="Sawmill", cat="landmark", tags=["industry", "wood"], seed=316, fp=(26.0, 9.0),
         collider="box", build=sawmill))


def dairy(g):
    """Fromagerie : chalet à rez enduit, vitrine sous store, bidons de lait, meule en enseigne."""
    W, D = 12.0, 10.0
    chalet(g, w=W, d=D, floors=2, ground="plaster_white", wood="wood_dark", roof="roof_red", pitch=24,
           balconies=(1,), shutter="barn", knee=0.8, door_u=3.8, flowers="geranium")
    f = Fac(g, 'y', -D / 2, -1)
    if g.lod < 2:
        f.quad(-1.6, 1.45, 5.6, 1.7, "window_lit", 0.13)
    f.awning(-1.6, 2.5, 6.0, 1.2, 0.45, "awning_cream", "awning_red")
    if g.lod < 2:
        m = g.mark()
        g.frustum((0, 0, 0), 0.7, 0.7, 0.28, lod(g, 10, 8), "yellow")
        g.transform(g.since(m), Matrix.Translation((5.2, -D / 2 - 1.5, 3.4)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
        g.box((5.2, -D / 2 - 0.9, 4.2), (0.06, 1.6, 0.06), "metal_dark")
        for k in range(3):
            x = -5.4 + k * 0.6
            g.frustum((x, -D / 2 - 1.9, 0), 0.22, 0.22, 0.55, 6, "metal")
            g.frustum((x, -D / 2 - 1.9, 0.55), 0.22, 0.12, 0.2, 6, "metal")


reg(dict(id="dairy", file="Dairy", cat="landmark", tags=["shop", "farm"], seed=317, fp=(12.0, 10.0), build=dairy))


def parasol(g, x, y, color="awning_red", r=1.4, h=2.3, z=0.0):
    if g.hd:                                          # mât, pied lesté, baleines, toile à pans et lambrequin
        g.tube([(x, y, z), (x, y, z + h + 0.08)], [0.022, 0.02], 8, "wood_honey")
        g.frustum((x, y, z), 0.24, 0.2, 0.07, 10, "concrete_dark", auto=False)
        g.octa((x, y, z + h + 0.1), 0.04, "wood_honey")
        n = 8
        zt, zr = z + h + 0.03, z + h - 0.45
        rim = [Vector((x + math.cos(6.2832 * k / n) * r, y + math.sin(6.2832 * k / n) * r, zr)) for k in range(n)]
        top = Vector((x, y, zt))
        for k in range(n):
            a, b = rim[k], rim[(k + 1) % n]
            g.face([a, b, top], color)
            g.face([top - Vector((0, 0, 0.01)), b - Vector((0, 0, 0.01)), a - Vector((0, 0, 0.01))], color)
            mid = (a + b) / 2
            g.face([a, mid - Vector((0, 0, 0.18)), b], color)                # lambrequin en pointe
            g.face([b + (top - b) * 0.004, mid - Vector((0, 0, 0.18)) + (top - mid) * 0.004, a + (top - a) * 0.004], color)
            g.tube([Vector((x, y, zr + 0.3)), a], [0.008, 0.006], 3, "wood_honey", cap_end=False)
        return
    g.frustum((x, y, z), 0.03, 0.03, h, 4, "metal", cap0=False)
    g.frustum((x, y, z + h - 0.45), r, 0.05, 0.5, lod(g, 8, 6, 4), color)


def table_set(g, x, y, chairs=4, wood="wood_honey", z=0.0):
    if g.hd:                                          # guéridon à piétement tripode, chaises à lattes
        g.frustum((x, y, z + 0.7), 0.45, 0.45, 0.04, 20, wood, auto=False)
        g.frustum((x, y, z + 0.67), 0.4, 0.43, 0.03, 20, "metal_dark", auto=False)
        g.tube([(x, y, z + 0.08), (x, y, z + 0.68)], [0.03, 0.025], 8, "metal_dark")
        for k in range(3):
            a = 2.094 * k + 0.3
            g.tube([(x, y, z + 0.14), (x + math.cos(a) * 0.2, y + math.sin(a) * 0.2, z + 0.06), (x + math.cos(a) * 0.3, y + math.sin(a) * 0.3, z + 0.0)],
                   [0.02, 0.018, 0.016], 5, "metal_dark")
        for k in range(chairs):
            a = 6.2832 * k / chairs + 0.5
            m = g.mark()
            for i in range(4):
                g.box((-0.15 + 0.1 * i, 0, 0.45), (0.085, 0.38, 0.02), wood)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    g.tube([(sx * 0.17, sy * 0.17, 0.0), (sx * 0.17, sy * 0.17, 0.44 if sx < 0 else 0.92)], [0.013, 0.013], 5, "metal_dark")
            for i in range(3):
                g.box((0.18, 0, 0.62 + 0.12 * i), (0.018, 0.36, 0.07), wood)
            g.transform(g.since(m), Matrix.Translation((x + math.cos(a) * 0.82, y + math.sin(a) * 0.82, z)) @ Matrix.Rotation(a, 4, 'Z'))
        return
    g.frustum((x, y, z + 0.7), 0.45, 0.45, 0.05, lod(g, 8, 6, 4), wood)
    if g.lod < 2:
        g.frustum((x, y, z), 0.05, 0.05, 0.7, 4, "metal_dark", cap0=False)
        for k in range(chairs):
            a = 6.2832 * k / chairs + 0.5
            cx, cy = x + math.cos(a) * 0.8, y + math.sin(a) * 0.8
            g.box((cx, cy, z + 0.24), (0.38, 0.38, 0.48), wood, rotz=a)
            if g.lod == 0:
                g.box((cx + math.cos(a) * 0.19, cy + math.sin(a) * 0.19, z + 0.68), (0.06, 0.38, 0.45), wood, rotz=a)


def restaurant_altitude(g):
    W, D = 14.0, 9.0
    chalet(g, w=W, d=D, floors=2, ground="stone", wood="wood_dark", roof="lauze", pitch=20, balconies=(),
           shutter="barn", knee=0.9, door_u=0.0, stones=True, flowers="geranium")
    ty = -D / 2 - 3.6                                # terrasse panoramique avec parasols
    g.box((0, ty, -1.2), (W + 2.0, 7.0, 3.3), "stone")
    g.box((0, ty, 0.5), (W + 2.06, 7.06, 0.12), "wood_honey")
    if g.lod < 2:
        g.box((0, ty - 3.45, 1.05), (W + 2.0, 0.08, 1.0), "wood_dark")
        for sx in (-1, 1):
            g.box((sx * (W / 2 + 0.96), ty, 1.05), (0.08, 7.0, 1.0), "wood_dark")
    for k, x in enumerate((-5.0, 0.0, 5.0)):
        table_set(g, x, ty - 0.6, z=0.56)
        parasol(g, x, ty - 0.6, ("awning_red", "awning_cream", "awning_red")[k], z=0.56)
    Fac(g, 'y', -D / 2, -1).sign(3.5, 2.2, 2.4, 0.5, "wood_dark", "brass")


reg(dict(id="restaurant_altitude", file="Restaurant_Altitude", cat="landmark", tags=["restaurant", "summit"],
         seed=318, fp=(16.0, 16.0), build=restaurant_altitude))


def refuge(g):
    """Refuge de montagne : cabane de pierre, toit de tôle, volets rouges à bande blanche, panneau solaire."""
    W, D = 9.0, 7.0
    gh = 2.6
    pitch = 28
    K.foundation(g, W, D, "stone_dark")
    zr = K.gable_body(g, W, D, 0.0, gh + 2.2, pitch, "wall_stone", 'y')
    K.roof_gable(g, W, D, gh + 2.2, pitch, "roof_metal", 'y', over_e=0.5, over_g=0.7, thick=0.14)
    front, back, left, right = fronts(g, W, D)

    def win(f, u, z):
        f.window(u, z, 0.8, 0.9, "sign_red", frame="plaster_white")
        if g.lod == 0:
            for s in (-1, 1):
                f.slab(u + s * 0.7, z, 0.4, 0.2, 0.075, "white")
    front.door(-1.5, 1.0, 2.0, "sign_red", frame="plaster_white")
    for u in (1.0, 3.0):
        win(front, u, 1.5)
    for u in (-2.2, 0.0, 2.2):
        win(front, u, gh + 1.3)
    for f in (left, right):
        for u in (-1.8, 1.8):
            win(f, u, 1.5)
    if g.lod < 2:
        m = g.mark()
        g.box((0, 0, 0), (2.4, 1.6, 0.06), "sign_blue")
        s = math.tan(math.radians(pitch))
        g.transform(g.since(m), Matrix.Translation((-W * 0.25, 0.5, zr - W * 0.25 * s + 0.32))
                    @ Matrix.Rotation(-math.radians(pitch), 4, 'Y'))
        g.box((W / 2 + 0.5, -1.0, 0.2), (0.6, 1.8, 0.5), "wood")                   # banc
        log_pile(g, -W / 2 - 0.6, 1.0, 3, 1.0, 0.16, 1.57)
    K.flag(g, W / 2 + 1.5, -D / 2 - 1.0, -0.5, 6.5, "red", (1.2, 0.8))
    K.chimney(g, 2.0, 1.0, gh + 2.2, zr + 0.6, "wall_stone", 0.7)


reg(dict(id="refuge", file="Refuge", cat="landmark", tags=["refuge", "summit"], seed=319, fp=(9.0, 7.0), build=refuge))


def tower_ruin(g):
    """Tour médiévale en ruine : fût rond ébréché, pan de courtine, éboulis, lierre."""
    n = lod(g, 10, 8, 6)
    R, H = 4.0, 15.0
    m = g.mark()
    g.frustum((0, 0, -3.0), R + 0.4, R, H + 3.0, n, "wall_stone", cap0=False, cap1=False)
    f = g.since(m)
    for v in {v for q in f for v in q.verts}:                    # sommet ébréché
        if v.co.z > H - 0.1:
            a = math.atan2(v.co.y, v.co.x)
            v.co.z -= max(0.0, math.sin(a + 0.6)) * 5.0 + (2.0 if g.chance(0.4, a) else 0.0)
    m = g.mark()
    g.frustum((0, 0, 3.0), R - 0.9, R - 0.9, 9.0, n, "stone_dark", cap1=False)   # intérieur sombre
    for q in g.since(m):
        q.normal_flip()
    for k in range(4):                                           # courtine en escalier
        h = 6.5 - k * 1.4
        g.box((R + 1.2 + k * 2.4, 0.3, h / 2 - 1.5), (2.4, 1.6, h + 3.0), "wall_stone")
        if g.lod == 0 and k < 3:
            g.box((R + 0.6 + k * 2.4, 0.3, h + 0.4), (0.9, 1.6, 0.8), "wall_stone")
    if g.lod < 2:
        for a, z in ((-1.57, 6.0), (-1.57, 10.0), (0.6, 8.0), (2.6, 7.0)):
            g.box((math.cos(a) * (R + 0.12), math.sin(a) * (R + 0.12), z), (0.25, 0.25, 1.4), "black", rotz=a)
        g.box((0, -R - 0.15, 1.3), (1.3, 0.4, 2.6), "black")
        for k in range(lod(g, 7, 4)):
            a = g.R(0, 6.28)
            d = R + g.R(1.0, 3.5)
            q = g.ico((math.cos(a) * d, math.sin(a) * d - 0.5, 0.15), g.R(0.4, 0.9), 1, "wall_stone", scale=(1.2, 1, 0.6))
            g.jitter(q, 0.1)
        for a, z, s in ((2.2, 2.5, 2.4), (3.4, 5.0, 2.0), (0.9, 1.5, 1.8)):
            q = g.ico((math.cos(a) * (R + 0.15), math.sin(a) * (R + 0.15), z), s, 1, "leaf_dark", scale=(0.9, 0.9, 1.3))
            g.jitter(q, 0.25)


reg(dict(id="tower_ruin", file="Tower_Ruin", cat="landmark", tags=["ruin", "viewpoint"], seed=320, fp=(18.0, 9.0),
         collider="capsule", radius=4.3, grad_h=15.0, build=tower_ruin))


def monument(g):
    """Monument de place : emmarchement, socle, obélisque de pierre, plaque de bronze, bornes à chaînes."""
    g.box((0, 0, -0.1), (5.0, 5.0, 1.0), "stone")
    g.box((0, 0, 0.55), (3.8, 3.8, 0.3), "stone")
    g.box((0, 0, 1.4), (2.0, 2.0, 1.4), "stone_warm")
    g.box((0, 0, 2.2), (2.3, 2.3, 0.2), "stone")
    g.frustum((0, 0, 2.3), 0.95, 0.45, 6.0, 4, "stone_warm", rot=math.pi / 4)
    g.frustum((0, 0, 8.3), 0.45, 0.0, 0.7, 4, "stone_warm", rot=math.pi / 4)
    if g.lod < 2:
        Fac(g, 'y', -1.0, -1).slab(0, 1.4, 1.2, 0.8, 0.05, "brass")
        g.ico((0, -1.25, 0.95), 0.32, 1, "leaf", scale=(1, 0.5, 1))               # couronne
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.frustum((sx * 3.1, sy * 3.1, -0.5), 0.12, 0.09, 1.3, 6, "stone_dark")
        if g.lod == 0:
            for sx in (-1, 1):
                g.box((sx * 3.1, 0, 0.5), (0.04, 6.2, 0.05), "metal_dark")
            g.box((0, 3.1, 0.5), (6.2, 0.04, 0.05), "metal_dark")


reg(dict(id="monument", file="Monument", cat="landmark", tags=["plaza"], seed=321, fp=(5.0, 5.0), lods=2,
         grad_h=9.0, build=monument))


def fountain_plaza(g):
    n = 8
    g.frustum((0, 0, -0.5), 2.9, 2.9, 0.7, n, "stone", rot=math.pi / n)             # marche
    g.frustum((0, 0, 0.2), 2.5, 2.6, 0.75, n, "stone_warm", rot=math.pi / n, cap1=False)
    g.frustum((0, 0, 0.75), 2.4, 2.4, 0.01, n, "water", rot=math.pi / n)
    g.frustum((0, 0, 0.2), 0.45, 0.32, 2.2, 6, "stone")
    g.frustum((0, 0, 2.3), 0.4, 1.0, 0.35, n, "stone_warm", rot=math.pi / n)
    g.frustum((0, 0, 2.6), 0.85, 0.85, 0.01, n, "water", rot=math.pi / n)
    g.frustum((0, 0, 2.6), 0.2, 0.12, 1.0, 6, "stone")
    g.ico((0, 0, 3.8), 0.28, 1, "brass")
    if g.lod == 0:
        for k in range(4):
            a = math.pi / 4 + math.pi / 2 * k
            g.limb((math.cos(a) * 0.9, math.sin(a) * 0.9, 2.6), (math.cos(a) * 1.5, math.sin(a) * 1.5, 0.8), 0.04,
                   0.07, 4, "water")
            q = g.ico((math.cos(a) * 2.5, math.sin(a) * 2.5, 1.12), 0.3, 1, "geranium", scale=(1, 1, 0.6))
            g.jitter(q, 0.04)


reg(dict(id="fountain_plaza", file="Fountain_Plaza", cat="landmark", tags=["plaza", "fountain"], seed=322,
         fp=(5.8, 5.8), lods=2, collider="capsule", radius=2.7, grad_h=4.0, build=fountain_plaza))


def bandstand(g):
    """Kiosque à musique octogonal."""
    n = 8
    R = 3.6
    g.frustum((0, 0, -0.5), R + 0.2, R + 0.2, 1.3, n, "stone", rot=math.pi / n)
    g.frustum((0, 0, 0.8), R, R, 0.1, n, "wood_honey", rot=math.pi / n)
    for k in range(n):
        a = 2 * math.pi * k / n + math.pi / n
        x, y = math.cos(a) * (R - 0.15), math.sin(a) * (R - 0.15)
        g.frustum((x, y, 0.9), 0.08, 0.07, 3.2, lod(g, 5, 4, 3), "white", cap0=False)
        if g.lod < 2 and k != 5:
            a2 = 2 * math.pi * (k + 1) / n + math.pi / n
            x2, y2 = math.cos(a2) * (R - 0.15), math.sin(a2) * (R - 0.15)
            g.beam((x, y, 1.75), (x2, y2, 1.75), 0.06, "white", 0.08)
            g.beam((x, y, 1.3), (x2, y2, 1.3), 0.03, "metal_dark", 0.7)
    g.frustum((0, 0, 4.1), R + 0.6, 0.5, 1.7, n, "copper", rot=math.pi / n)
    g.frustum((0, 0, 5.8), 0.5, 0.0, 0.9, n, "copper", rot=math.pi / n, cap0=False)
    K.steps(g, 0, -R * 0.92 - 0.2, 1.8, 4, rise=0.2, color="stone")


reg(dict(id="bandstand", file="Bandstand", cat="landmark", tags=["plaza"], seed=323, fp=(7.6, 7.6), lods=2,
         collider="mesh", grad_h=6.0, build=bandstand))


def wheel(g, r, w, n=12, color="wood"):
    """Roue à aubes dans le plan XZ (axe le long de Y), centrée à l'origine."""
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        p0 = Vector((math.cos(a0) * r, 0, math.sin(a0) * r))
        p1 = Vector((math.cos(a1) * r, 0, math.sin(a1) * r))
        for sy in ((-1, 1) if g.lod == 0 else (0,)):
            o = Vector((0, sy * w / 2, 0))
            g.beam(p0 + o, p1 + o, 0.1 if g.lod == 0 else w, color, 0.22)
        if g.lod < 2:
            mid = (p0 + p1) / 2
            g.box(mid * 0.93, (0.05, w, 0.5), "wood_dark", rot=Matrix.Rotation(-(a0 + a1) / 2, 3, 'Y'))
            if k % 2 == 0:
                g.beam((0, 0, 0), p0, 0.1, color, 0.1)
    g.limb((0, -w / 2 - 0.3, 0), (0, w / 2 + 0.3, 0), 0.14, 0.14, 6, "wood_dark", cap0=True)


def watermill(g):
    W, D = 8.0, 6.5
    gh = 3.0
    K.foundation(g, W, D, "stone")
    zr = K.gable_body(g, D, W, 0.0, gh + 1.6, 40, "wall_stone", 'x')
    K.roof_gable(g, D, W, gh + 1.6, 40, "roof_brown", 'x', over_e=0.6, over_g=0.5)
    front, back, left, right = fronts(g, W, D)
    front.door(-1.5, 1.0, 2.0, "door", frame=None)
    for u in (1.0, 2.8):
        front.window(u, 1.6, 0.8, 1.0, "shutter", frame="plaster_white", flower="geranium")
    for u in (-2.0, 1.0):
        front.window(u, gh + 0.8, 0.7, 0.8, "shutter", frame="plaster_white")
    m = g.mark()                                     # roue sur le pignon +X et son chenal
    wheel(g, 2.5, 1.0, 12 if g.lod == 0 else 8)
    g.move(g.since(m), (W / 2 + 1.0, 0.5, 2.2))
    if g.lod < 2:
        g.box((W / 2 + 1.0, 5.0, 4.95), (1.1, 8.5, 0.12), "wood")
        for sx in (-1, 1):
            g.box((W / 2 + 1.0 + sx * 0.55, 5.0, 5.15), (0.08, 8.5, 0.4), "wood")
        for y in (3.0, 6.0, 9.0):
            g.box((W / 2 + 1.0, y, 2.2), (0.2, 0.2, 5.4), "wood_dark")
        g.box((W / 2 + 1.0, 5.0, 5.06), (0.9, 8.4, 0.05), "water")
    K.chimney(g, -2.0, 0.6, gh + 2.5, zr + 0.7, "wall_stone", 0.7)


reg(dict(id="watermill", file="WaterMill", cat="landmark", tags=["mill", "river"], seed=324, fp=(11.0, 6.5),
         build=watermill))


def lattice_tower(g, H, base, top, sections, colors=("sign_red", "white"), leg=0.22, brace=True):
    """Pylône treillis carré : montants effilés, ceintures et croisillons par tronçon."""
    def hw(z):
        return base + (top - base) * z / H
    if g.lod == 2:
        for k in range(sections):
            z0, z1 = H * k / sections, H * (k + 1) / sections
            zz = z0 if k else -0.5
            g.frustum((0, 0, zz), hw(z0) * 1.414, hw(z1) * 1.414, z1 - zz, 4, colors[k % len(colors)],
                      rot=math.pi / 4, cap0=False, cap1=k == sections - 1)
        return
    for k in range(sections):
        z0, z1 = H * k / sections, H * (k + 1) / sections
        c = colors[k % len(colors)]
        a, b = hw(z0), hw(z1)
        c0 = [(-a, -a), (a, -a), (a, a), (-a, a)]
        c1 = [(-b, -b), (b, -b), (b, b), (-b, b)]
        for i in range(4):
            j = (i + 1) % 4
            zz = z0 if k else -0.5
            g.beam((c0[i][0], c0[i][1], zz), (c1[i][0], c1[i][1], z1), leg, c)
            g.beam((c1[i][0], c1[i][1], z1), (c1[j][0], c1[j][1], z1), leg * 0.7, c)
            if brace and g.lod == 0:
                g.beam((c0[i][0], c0[i][1], z0), (c1[j][0], c1[j][1], z1), leg * 0.5, c)
                g.beam((c0[j][0], c0[j][1], z0), (c1[i][0], c1[i][1], z1), leg * 0.5, c)


def radio_tower(g):
    H = 38.0
    lattice_tower(g, H, 2.4, 0.45, 6)
    g.frustum((0, 0, H), 0.08, 0.03, 5.0, 4, "white", cap0=False)
    g.box((0, 0, H + 0.2), (0.3, 0.3, 0.3), "neon_red")
    if g.lod < 2:
        for z, a, r in ((30.0, 0.6, 0.9), (26.0, 2.8, 1.1), (33.0, 4.2, 0.7)):
            m = g.mark()
            g.frustum((0, 0, 0), r, r * 0.35, r * 0.4, 8, "white")
            g.transform(g.since(m), Matrix.Translation((math.cos(a) * 1.5, math.sin(a) * 1.5, z))
                        @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(-math.pi / 2, 4, 'Y'))
        g.box((3.5, 0, 2.7), (3.0, 2.6, 0.2), "roof_metal")
    g.box((3.5, 0, 0.8), (2.6, 2.2, 3.6), "concrete")                               # local technique


reg(dict(id="radio_tower", file="Radio_Tower", cat="landmark", tags=["tower", "summit"], seed=325, fp=(7.6, 5.0),
         collider="box", grad_h=38.0, build=radio_tower))


def summit_cross(g):
    """Croix de sommet en bois sur un cairn, haubans."""
    for k in range(lod(g, 9, 6, 3)):
        a = g.R(0, 6.28)
        d = g.R(0.0, 1.1)
        f = g.ico((math.cos(a) * d, math.sin(a) * d, 0.2 + (1.1 - d) * 0.5), g.R(0.4, 0.7), 1,
                  "rock" if k % 2 else "rock_light", scale=(1.2, 1, 0.7))
        g.jitter(f, 0.08)
    g.box((0, 0, 2.6), (0.28, 0.28, 6.2), "wood_dark")
    g.box((0, 0, 4.4), (2.8, 0.26, 0.26), "wood_dark")
    if g.lod == 0:
        for sx in (-1, 1):
            g.limb((sx * 1.3, 0, 4.4), (sx * 2.6, 0.4, 0.2), 0.015, 0.015, 3, "metal", cap1=False)
        g.box((0, -0.16, 3.2), (0.4, 0.04, 0.5), "brass")


reg(dict(id="summit_cross", file="Summit_Cross", cat="landmark", tags=["summit"], seed=326, fp=(3.0, 3.0), lods=2,
         collider="capsule", radius=1.0, build=summit_cross))
