"""Pack alpin : remontées mécaniques, lac, infrastructures (tunnel, galerie, pylônes, barrage).
Câbles et route : le long de Y (le câble quitte une gare vers +Y)."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac
from alp_buildings import spread
from alp_landmarks import fronts
from alp_services import lattice_tower, log_pile


def item(iid, fname, build, cat, lods=2, **kw):
    reg(dict(id=iid, file=fname, cat=cat, lods=lods, build=build, **kw))


def face_from(g, pts, color, inside, away=True):
    """Face orientée à l'opposé du point « inside » (ou vers lui si away = False)."""
    f = g.face(pts, color)[0]
    f.normal_update()
    d = f.calc_center_median() - Vector(inside)
    if (f.normal.dot(d) < 0) == away:
        f.normal_flip()
    return f


# ================================================================= remontées mécaniques
def bullwheel(g, x, y, z, r=2.0, color="sign_red"):
    n = lod(g, 12, 8, 6)
    g.frustum((x, y, z), r, r, 0.25, n, color)
    if g.lod < 2:
        g.frustum((x, y, z - 0.6), 0.25, 0.25, 1.4, 6, "metal_dark")


def cablecar_station_low(g):
    """Gare aval du téléphérique : socle béton, étage bardé de bois, grande baie de départ côté +Y, guichets en façade."""
    W, D = 14.0, 18.0
    K.foundation(g, W, D, "concrete", top=0.3)
    g.box((0, -2.0, 2.0), (W, D - 4.0, 4.0), "concrete")
    # étage : halle en bois, toit à un pan montant vers le départ des câbles
    g.prism([(-D / 2, 4.0), (D / 2, 4.0), (D / 2, 12.5), (-D / 2, 8.0)], -W / 2, W / 2, "wood_dark", axis='x')
    K.roof_shed(g, W, D, 12.6, 8.1, "roof_metal", over=0.8, thick=0.3, low='-y')
    for sx in (-1, 1):                               # piles de la baie de départ
        g.box((sx * (W / 2 - 0.6), D / 2 - 1.0, 0.5), (1.2, 2.0, 4.0 + 3.0), "concrete")
    back = Fac(g, 'y', D / 2, 1)
    back.quad(0, 7.6, 9.0, 6.4, "black", 0.04)                                     # baie des cabines
    if g.lod < 2:
        bullwheel(g, 0, D / 2 - 2.5, 10.4, 2.6)
        g.box((0, D / 2 + 1.5, 4.1), (9.0, 3.0, 0.3), "concrete")                   # quai
        for sx in (-1, 1):
            g.box((sx * 4.4, D / 2 + 1.5, 4.9), (0.08, 3.0, 1.1), "metal")
    front, _, left, right = fronts(g, W, D)
    front.door(-3.0, 2.2, 2.6, "window_lit", frame="metal")
    for u in (1.0, 3.2, 5.2):
        front.window(u, 1.9, 1.3, 1.3, None, frame="metal", lit=0.9)
    front.sign(0, 5.4, 9.0, 1.2, "sign_red", "white", depth=0.12)
    front.awning(2.9, 3.3, 6.6, 1.5, 0.4, "sign_red")
    for u in spread(4, -W / 2 + 1, W / 2 - 1):
        front.window(u, 7.0, 1.5, 1.0, None, frame=None, lit=0.4, keep=False)
    for f in (left, right):
        for u in spread(4, -D / 2 + 1, D / 2 - 5):
            f.window(u, 2.2, 1.4, 1.2, None, frame="metal", keep=False)
        for u in spread(3, -D / 2 + 1, D / 2 - 3):
            f.window(u, 6.0, 1.6, 1.0, None, frame=None, keep=False)


item("cablecar_station_low", "CableCar_Station_Low", cablecar_station_low, "ski", lods=3, tags=["cablecar", "station"],
     seed=501, fp=(14.0, 18.0), grad_h=13.0, note="cables leave toward Unity -Z (rear), from (0, 10.4, -6.5)")


def cablecar_station_high(g):
    """Gare amont : volume de béton et de tôle sur pilotis, baie des cabines côté +Y, terrasse panoramique."""
    W, D = 12.0, 14.0
    K.foundation(g, W, D, "concrete", top=1.0)
    g.box((0, 0, 4.5), (W, D, 7.0), "concrete")
    g.prism([(-D / 2, 8.0), (D / 2, 8.0), (D / 2, 12.0), (-D / 2, 9.5)], -W / 2, W / 2, "roof_metal", axis='x')
    K.roof_shed(g, W, D, 12.1, 9.6, "metal_dark", over=0.5, thick=0.25, low='-y')
    back = Fac(g, 'y', D / 2, 1)
    back.quad(0, 7.4, 8.5, 6.2, "black", 0.04)
    front, _, left, right = fronts(g, W, D)
    if g.lod < 2:
        bullwheel(g, 0, D / 2 - 2.5, 10.0, 2.4)
        g.box((0, D / 2 + 1.5, 4.2), (8.5, 3.0, 0.3), "concrete")
        g.box((0, -D / 2 - 2.0, 4.0), (W + 4.0, 4.0, 0.25), "concrete")             # terrasse
        g.box((0, -D / 2 - 3.95, 4.65), (W + 4.0, 0.06, 1.1), "glass_blue")
        for sx in (-1, 1):
            g.box((sx * (W / 2 + 1.97), -D / 2 - 2.0, 4.65), (0.06, 4.0, 1.1), "glass_blue")
            g.box((sx * (W / 2 + 1.5), -D / 2 - 3.5, 1.2), (0.4, 0.4, 5.4 + 3.0), "concrete")
    front.quad(0, 5.6, W - 2.0, 2.2, "window_lit", 0.04)                            # restaurant panoramique
    front.door(0, 1.6, 2.4, "window", frame="metal", z0=1.0)
    front.sign(0, 8.6, 6.0, 0.9, "sign_red", "white", depth=0.1)
    for f in (left, right):
        for u in spread(3, -D / 2 + 1, D / 2 - 3):
            f.window(u, 5.6, 1.8, 1.6, None, frame=None, lit=0.6, keep=False)
    if g.lod < 2:
        g.frustum((W / 2 - 1.5, -D / 2 + 1.5, 9.5), 0.06, 0.03, 6.0, 4, "metal", cap0=False)
        g.box((W / 2 - 1.5, -D / 2 + 1.5, 15.2), (0.3, 0.3, 0.3), "neon_red")


item("cablecar_station_high", "CableCar_Station_High", cablecar_station_high, "ski", lods=3,
     tags=["cablecar", "station", "summit"], seed=502, fp=(12.0, 14.0), grad_h=12.0,
     note="cables leave toward Unity -Z (rear), from (0, 10.0, -4.5)")


def cablecar_pylon(g):
    """Pylône de téléphérique : treillis de 25 m, tête à deux sabots (câbles à x = ±3 m, le long de Y)."""
    H = 25.0
    lattice_tower(g, H, 2.6, 0.9, 5, colors=("metal", "metal"), leg=0.26)
    g.box((0, 0, H + 0.4), (8.0, 1.2, 0.8), "metal")
    for sx in (-1, 1):
        g.box((sx * 3.0, 0, H - 0.2), (0.5, 4.0, 0.5), "metal_dark")
        if g.lod < 2:
            g.beam((sx * 0.9, 0, H - 3.0), (sx * 3.6, 0, H + 0.2), 0.2, "metal")
    if g.lod < 2:
        g.box((0, 0, H + 1.3), (1.6, 1.6, 0.1), "metal_dark")
        g.box((0, 0, H + 1.5), (0.25, 0.25, 0.3), "neon_red")
        g.box((0, -2.7, 1.1), (0.9, 0.06, 0.6), "sign_yellow")


item("cablecar_pylon", "CableCar_Pylon", cablecar_pylon, "ski", lods=3, tags=["cablecar", "pylon"], seed=503,
     fp=(5.2, 5.2), collider="box", grad_h=25.0, note="cables at Unity x = ±3, y = 24.6, along Z")


def cablecar_cabin(g):
    """Cabine : origine au point d'accroche sur le câble (z = 0), la cabine pend dessous."""
    g.box((0, 0, 0.05), (0.5, 1.6, 0.3), "metal_dark")                              # chariot
    g.beam((0, 0, -0.1), (0, 0, -3.2), 0.16, "metal_dark")
    zc = -4.6
    g.box((0, 0, zc), (2.6, 3.6, 2.6), "sign_red")
    g.box((0, 0, zc + 1.38), (2.8, 3.8, 0.16), "white")
    if g.lod == 0:
        g.box((0, 0, zc - 1.35), (2.7, 3.7, 0.12), "metal_dark")
        for sx in (-1, 1):
            Fac(g, 'x', sx * 1.3, sx).slab(0, zc + 0.35, 3.1, 1.2, 0.03, "window")
        for sy in (-1, 1):
            Fac(g, 'y', sy * 1.8, sy).slab(0, zc + 0.35, 2.1, 1.2, 0.03, "window")
        Fac(g, 'x', 1.3, 1).slab(0, zc - 0.7, 3.4, 0.25, 0.04, "white")
        Fac(g, 'x', -1.3, -1).slab(0, zc - 0.7, 3.4, 0.25, 0.04, "white")
    else:
        for sx in (-1, 1):
            Fac(g, 'x', sx * 1.3, sx).quad(0, zc + 0.35, 3.1, 1.2, "window", 0.02)
        for sy in (-1, 1):
            Fac(g, 'y', sy * 1.8, sy).quad(0, zc + 0.35, 2.1, 1.2, "window", 0.02)


item("cablecar_cabin", "CableCar_Cabin", cablecar_cabin, "ski", tags=["cablecar", "hanging"], seed=504, collider="none",
     fp=(2.6, 3.6), base=-6.0, note="origin at cable grip, cabin hangs below (y from -5.9 to 0.2)")


def chairlift_pylon(g):
    """Pylône de télésiège : fût tubulaire de 12 m, potence en T, balanciers (câbles à x = ±2,2 m, le long de Y)."""
    H = 12.0
    n = lod(g, 8, 6)
    g.frustum((0, 0, -0.5), 0.42, 0.3, H + 0.5, n, "metal", cap0=False)
    g.box((0, 0, 0.2), (1.4, 1.4, 0.5), "concrete")
    g.box((0, 0, H + 0.15), (5.2, 0.4, 0.4), "metal")
    for sx in (-1, 1):
        g.box((sx * 2.2, 0, H - 0.25), (0.3, 2.6, 0.3), "metal_dark")
    if g.lod == 0:
        g.box((0, 0, H + 0.7), (0.8, 0.8, 0.06), "metal_dark")
        for k in range(6):
            g.box((0.3, 0.05, 1.5 + k * 0.5), (0.3, 0.04, 0.04), "metal_dark")
        g.box((0, -0.45, 2.2), (0.5, 0.05, 0.5), "sign_yellow")


item("chairlift_pylon", "Chairlift_Pylon", chairlift_pylon, "ski", tags=["chairlift", "pylon"], seed=505,
     fp=(1.4, 1.4), collider="capsule", radius=0.45, note="cables at Unity x = ±2.2, y = 11.9, along Z")


def chairlift_chair(g):
    """Siège quatre places : origine à la pince sur le câble (z = 0)."""
    g.box((0, 0, 0.0), (0.2, 0.5, 0.2), "metal_dark")
    g.beam((0, 0, -0.05), (0, 0.15, -2.3), 0.07, "metal")
    zc = -3.0
    g.box((0, 0.15, -2.3), (2.3, 0.07, 0.07), "metal")
    for sx in (-1, 1):
        g.beam((sx * 1.15, 0.15, -2.3), (sx * 1.15, 0.3, zc), 0.06, "metal")
    g.box((0, 0.0, zc), (2.4, 0.6, 0.1), "sign_blue")
    g.box((0, 0.33, zc + 0.35), (2.4, 0.1, 0.65), "sign_blue")
    if g.lod == 0:
        g.box((0, -0.45, zc + 0.55), (2.4, 0.05, 0.05), "metal")                    # garde-corps
        g.box((0, -0.5, zc - 0.5), (2.2, 0.05, 0.05), "metal")                      # repose-pieds
        for sx in (-1, 1):
            g.box((sx * 1.15, -0.2, zc + 0.6), (0.05, 0.55, 0.05), "metal")


item("chairlift_chair", "Chairlift_Chair", chairlift_chair, "ski", tags=["chairlift", "hanging"], seed=506, collider="none",
     fp=(2.4, 1.0), base=-3.6, note="origin at cable grip, chair hangs below (y from -3.5 to 0.1), faces Unity +Z")


def chairlift_station(g):
    """Gare de télésiège : portique et poulie motrice sous capot, cabane de l'opérateur ; câbles vers +Y."""
    g.box((0, 0, 0.1), (7.0, 9.0, 0.2 + 1.0), "concrete")
    for sx in (-1, 1):
        g.box((sx * 2.9, -2.0, 2.5), (0.5, 0.5, 5.0), "metal")
    g.box((0, -2.0, 5.1), (6.6, 0.6, 0.6), "metal")
    g.box((0, 0.5, 5.2), (0.6, 6.0, 0.5), "metal")
    bullwheel(g, 0, 2.5, 4.55, 2.2, "sign_yellow")
    g.prism([(-2.9, 5.5), (2.9, 5.5), (2.4, 6.5), (-2.4, 6.5)], -3.0, 4.5, "sign_blue", axis='y')   # capot
    # cabane
    hx, hy = 5.2, -2.5
    g.box((hx, hy, 1.2 - 0.5), (2.6, 3.0, 2.4 + 1.0), "wood_honey")
    K.roof_shed(g, 2.6, 3.0, 3.0, 2.5, "roof_metal", over=0.3, center=(hx, hy), low='-x')
    f = Fac(g, 'x', hx - 1.3, -1)
    f.window(hy, 1.5, 1.8, 1.0, None, frame="white", lit=0.8)
    Fac(g, 'y', hy - 1.5, -1).door(hx, 0.8, 2.0, "door", frame=None)
    if g.lod < 2:
        for sx in (-1, 1):                           # barrières d'accès
            g.box((sx * 1.8, -3.2, 0.9), (0.06, 2.0, 0.9), "sign_red")
        g.box((-4.5, -3.8, 1.6), (0.08, 0.08, 3.2), "metal")
        g.box((-4.5, -3.86, 2.7), (1.4, 0.05, 0.9), "white")
        g.box((-4.5, -3.9, 2.9), (1.1, 0.04, 0.3), "sign_blue")


item("chairlift_station", "Chairlift_Station", chairlift_station, "ski", lods=2, tags=["chairlift", "station"],
     seed=507, fp=(10.0, 9.0), collider="mesh", note="cables leave toward Unity -Z at x = ±2.2, y = 4.7")


def snow_cannon(g):
    g.box((0, 0, 0.25), (1.6, 1.2, 0.3), "metal_dark")
    for sx in (-1, 1):
        g.limb((sx * 0.7, 0.5, 0.0), (sx * 0.9, 0.9, -0.4), 0.05, 0.05, 4, "metal_dark")
        g.limb((sx * 0.7, -0.5, 0.0), (sx * 0.9, -0.9, -0.4), 0.05, 0.05, 4, "metal_dark")
    g.frustum((0, 0, 0.4), 0.12, 0.1, 1.5, 6, "metal")
    m = g.mark()
    n = lod(g, 10, 7)
    g.frustum((0, 0, -0.8), 0.6, 0.72, 1.6, n, "yellow", cap0=True, cap1=False)
    g.frustum((0, 0, 0.75), 0.6, 0.6, 0.01, n, "black")
    if g.lod == 0:
        g.frustum((0, 0, 0.8), 0.76, 0.76, 0.08, n, "metal", cap0=False, cap1=False)
    g.transform(g.since(m), Matrix.Translation((0, 0, 2.3)) @ Matrix.Rotation(math.radians(65), 4, 'X'))


item("snow_cannon", "Snow_Cannon", snow_cannon, "ski", tags=["piste"], seed=508, collider="capsule", radius=0.8)


def ski_rack(g):
    for sx in (-1, 1):
        g.box((sx * 1.4, 0, 0.5), (0.1, 0.5, 1.5 + 0.5), "wood")
    g.box((0, 0, 1.2), (3.0, 0.08, 0.1), "wood")
    g.box((0, 0, 0.35), (3.0, 0.3, 0.06), "wood")
    cols = ("sign_red", "sign_blue", "yellow", "white", "green", "orange", "black", "sign_red")
    for k in range(lod(g, 8, 5)):
        x = -1.15 + k * 0.33
        s = 1 if k % 2 else -1
        g.box((x, s * 0.1, 1.05), (0.09, 0.03, 1.75), cols[k], rot=Matrix.Rotation(s * 0.1, 3, 'X'))
        if g.lod == 0:
            g.box((x + 0.1, s * 0.1, 1.05), (0.09, 0.03, 1.75), cols[k], rot=Matrix.Rotation(s * 0.1, 3, 'X'))


item("ski_rack", "Ski_Rack", ski_rack, "ski", tags=["piste", "prop"], seed=509, collider="box")


def piste_marker(g):
    g.frustum((0, 0, -0.5), 0.035, 0.03, 2.6, 5, "orange", cap0=False)
    g.frustum((0, 0, 1.2), 0.04, 0.04, 0.35, 5, "black", cap0=False, cap1=False)
    m = g.mark()
    g.frustum((0, 0, 0), 0.22, 0.22, 0.03, lod(g, 10, 6), "sign_red")
    g.frustum((0, 0, 0.03), 0.13, 0.13, 0.005, lod(g, 8, 6), "white")
    g.transform(g.since(m), Matrix.Translation((0, -0.05, 1.85)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))


item("piste_marker", "Piste_Marker", piste_marker, "ski", tags=["piste", "prop"], seed=510, collider="none")


# ================================================================= lac
def boathouse(g):
    """Hangar à bateaux sur pilotis : ouverture côté lac (-Y), ponton latéral. Origine au niveau de l'eau."""
    W, D = 6.5, 9.0
    for sx in (-1, 1):
        for y in spread(3, -D / 2, D / 2):
            g.frustum((sx * (W / 2 - 0.2), y, -4.0), 0.16, 0.16, 4.6, lod(g, 6, 4, 4), "wood_dark", cap0=False)
    g.box((0, D / 2 - 2.0, 0.55), (W, 4.0, 0.14), "wood")                           # plancher du fond
    for sx in (-1, 1):
        g.box((sx * (W / 2 - 0.6), -2.0, 0.55), (1.2, D - 4.0, 0.14), "wood")
        g.box((sx * (W / 2 - 0.05), 0, 2.0), (0.1, D, 2.8), "wood_red")
    g.box((0, D / 2 - 0.05, 2.0), (W - 0.2, 0.1, 2.8), "wood_red")
    g.prism([(-W / 2, 3.4), (W / 2, 3.4), (0, 5.3)], D / 2 - 0.1, D / 2, "wood_red", axis='y')
    g.prism([(-W / 2, 3.4), (W / 2, 3.4), (0, 5.3)], -D / 2, -D / 2 + 0.1, "wood_red", axis='y')
    front = Fac(g, 'y', -D / 2, -1)
    front.quad(0, 1.9, W - 2.0, 2.6, "black", 0.02)
    K.roof_gable(g, W, D, 3.4, 30, "roof_brown", 'y', over_e=0.5, over_g=0.6, thick=0.16, trim="white")
    if g.lod < 2:
        Fac(g, 'x', W / 2, 1).window(1.0, 2.2, 0.9, 0.9, None, frame="white")
        g.box((0, -D / 2 - 0.02, 4.2), (0.9, 0.05, 0.6), "white")
        g.box((-W / 2 - 0.9, -1.0, 0.5), (1.6, D + 2.0, 0.12), "wood")              # ponton latéral
        for y in (-D / 2 - 1.5, 0, D / 2):
            g.frustum((-W / 2 - 1.6, y, -4.0), 0.12, 0.12, 5.2, 5, "wood_dark", cap0=False)


item("boathouse", "Boathouse", boathouse, "lake", lods=3, tags=["building"], seed=520, fp=(8.2, 11.0), collider="box",
     base=0.0, note="origin at water level; boat door faces the lake (Unity +Z); piles reach 4 m below")


def pier(g):
    """Ponton en bois : module de 6 m le long de X, largeur 2 m, tablier à 0,5 m au-dessus de l'eau."""
    L = 6.0
    if g.lod == 0:
        n = 12
        for k in range(n):
            g.box((-L / 2 + L * (k + 0.5) / n, 0, 0.5), (L / n - 0.04, 2.0, 0.08), "wood" if k % 3 else "wood_honey")
    else:
        g.box((0, 0, 0.5), (L, 2.0, 0.08), "wood")
    for sy in (-1, 1):
        g.box((0, sy * 0.85, 0.36), (L, 0.12, 0.2), "wood_dark")
        for x in (-L / 2 + 0.2, L / 2 - 0.2):
            g.frustum((x, sy * 0.95, -4.0), 0.11, 0.11, 5.0, lod(g, 6, 4), "wood_dark", cap0=False)


item("pier", "Pier", pier, "lake", tags=["linear"], seed=521, fp=(6.0, 2.0), module=6.0, collider="box", base=0.0,
     note="origin at water level, deck top at y = 0.54")


def hull(g, L, W, H, color, inside="wood_honey", z0=-0.15):
    """Coque de barque pointue à l'avant (-Y), tableau à l'arrière ; ligne de flottaison à z = 0."""
    sec = [(-L / 2, 0.02, 1.25), (-L * 0.25, 0.72, 1.05), (L * 0.1, 1.0, 1.0), (L / 2, 0.72, 1.0)]
    rows = []
    for y, wf, hf in sec:
        w, h = W / 2 * wf, H * hf
        rows.append([Vector((-w, y, z0 + h)), Vector((-w * 0.55, y, z0)), Vector((w * 0.55, y, z0)), Vector((w, y, z0 + h))])
    c = Vector((0, 0, z0 + H * 1.3))
    for a, b in zip(rows, rows[1:]):
        for k in range(3):
            face_from(g, [a[k], a[k + 1], b[k + 1], b[k]], color, c)
            face_from(g, [p + Vector((0, 0, 0.05)) for p in (b[k], b[k + 1], a[k + 1], a[k])], inside, c, away=False)
    face_from(g, list(rows[-1]), color, c)
    face_from(g, list(rows[0]), color, c)
    return rows


def rowboat(g):
    hull(g, 4.2, 1.5, 0.6, "wood_red" if g.seed % 2 else "sign_blue")
    for y in (-0.6, 0.7):
        g.box((0, y, 0.25), (1.3, 0.28, 0.05), "wood_cut")
    if g.lod == 0:
        for sx in (-1, 1):
            g.limb((sx * 0.75, 0.1, 0.45), (sx * 2.2, 0.5, 0.15), 0.03, 0.03, 4, "wood_cut")
            g.box((sx * 2.3, 0.53, 0.13), (0.5, 0.16, 0.03), "wood_cut", rotz=sx * 0.25)


item("rowboat", "Rowboat", rowboat, "lake", tags=["boat"], seed=523, fp=(1.5, 4.2), collider="box", base=-0.2,
     note="origin at waterline")


def sailboat(g):
    hull(g, 5.6, 1.9, 0.8, "white", inside="wood_cut")
    g.box((0, 0.4, 0.45), (1.2, 2.0, 0.5), "white")                                # rouf
    g.frustum((0, -0.6, 0.3), 0.06, 0.04, 7.0, lod(g, 5, 4), "metal", cap0=False)
    g.box((0, 0.9, 1.2), (0.06, 3.0, 0.06), "metal")                               # bôme
    g.face([(0.0, -0.5, 1.35), (0.0, 2.3, 1.35), (0.0, -0.5, 7.0)], "sign_white")   # grand-voile
    g.face([(0.01, -0.5, 7.0), (0.01, 2.3, 1.35), (0.01, -0.5, 1.35)], "sign_white")
    g.face([(0.0, -0.7, 6.2), (0.0, -2.7, 0.9), (0.0, -0.75, 0.9)], "awning_red")   # foc
    g.face([(0.01, -0.75, 0.9), (0.01, -2.7, 0.9), (0.01, -0.7, 6.2)], "awning_red")
    if g.lod == 0:
        g.box((0, 0.4, 0.55), (1.24, 1.4, 0.2), "window")
        g.limb((0, -2.75, 0.75), (0, -0.6, 7.2), 0.012, 0.012, 3, "metal_dark", cap1=False)


item("sailboat", "Sailboat", sailboat, "lake", tags=["boat"], seed=524, fp=(1.9, 5.6), collider="box", base=-0.2,
     grad_h=5.0, note="origin at waterline")


def pedalo(g):
    for sx in (-1, 1):
        g.box((sx * 0.75, 0, 0.05), (0.5, 3.2, 0.4), "white")
    g.box((0, 0.1, 0.3), (1.9, 2.2, 0.1), "yellow")
    for sx in (-1, 1):
        g.box((sx * 0.45, 0.5, 0.55), (0.6, 0.6, 0.4), "sign_red")
        g.box((sx * 0.45, 0.85, 0.85), (0.6, 0.12, 0.6), "sign_red")
    if g.lod == 0:
        g.box((0, -0.5, 0.55), (0.1, 0.1, 0.5), "metal_dark")
        g.box((0, -0.5, 0.82), (0.5, 0.06, 0.06), "black")
        m = g.mark()                                 # toboggan
        g.box((0, 0, 0), (0.6, 1.6, 0.08), "sign_blue")
        g.transform(g.since(m), Matrix.Translation((0, 1.7, 0.75)) @ Matrix.Rotation(0.5, 4, 'X'))


item("pedalo", "Pedalo", pedalo, "lake", tags=["boat"], seed=525, fp=(2.0, 3.4), collider="box", base=-0.2,
     note="origin at waterline")


def buoy(g):
    n = lod(g, 8, 6)
    g.frustum((0, 0, -0.5), 0.2, 0.55, 0.5, n, "sign_red", cap0=True)
    g.frustum((0, 0, 0.0), 0.55, 0.5, 0.35, n, "sign_red", cap0=False)
    g.frustum((0, 0, 0.35), 0.5, 0.12, 0.9, n, "white", cap0=False)
    g.frustum((0, 0, 1.25), 0.12, 0.12, 0.35, 5, "sign_red")
    g.box((0, 0, 1.7), (0.16, 0.16, 0.16), "neon_red")


item("buoy", "Buoy", buoy, "lake", tags=["prop"], seed=526, base=-0.5, note="origin at waterline")


def reeds(g):
    for k in range(lod(g, 16, 9)):
        a = g.R(0, 6.28)
        d = g.R(0.05, 0.75)
        x, y = math.cos(a) * d, math.sin(a) * d
        h = g.R(1.4, 2.3)
        top = (x + g.R(-0.2, 0.2), y + g.R(-0.2, 0.2), h)
        g.blade((x, y, -0.6), (top[0] - x, top[1] - y, h + 0.6), h + 0.6, 0.09, "reed", bend=g.R(0.0, 0.12), segs=1)
        if k % 3 == 0:
            g.limb((x, y, -0.3), (top[0], top[1], h * 0.85), 0.012, 0.012, 3, "reed", cap1=False)
            g.limb((top[0], top[1], h * 0.85), (top[0], top[1], h * 0.85 + 0.28), 0.04, 0.035, 4, "reed_head", cap0=True)


item("reeds", "Reeds", reeds, "lake", tags=["plant"], seed=527, base=0.0, note="origin at waterline, roots 0.6 m below")


def lily_pads(g):
    for k in range(lod(g, 8, 5)):
        a = g.R(0, 6.28)
        d = g.R(0.0, 1.3)
        x, y = math.cos(a) * d, math.sin(a) * d
        r = g.R(0.22, 0.36)
        rot = g.R(0, 6.28)
        n = 7
        pts = [(x, y, 0.03)] + [(x + math.cos(rot + 5.4 * i / (n - 1)) * r, y + math.sin(rot + 5.4 * i / (n - 1)) * r, 0.03)
                                 for i in range(n)]
        g.face(pts, "leaf" if k % 2 else "leaf_light")
        if k % 3 == 0 and g.lod == 0:
            g.frustum((x, y, 0.03), 0.05, 0.12, 0.08, 6, "flower_white", cap0=False)
            g.octa((x, y, 0.1), 0.04, "flower_yellow")


item("lily_pads", "Lily_Pads", lily_pads, "lake", tags=["plant"], seed=528, base=0.0, grad_h=0.1,
     note="origin at waterline")


# ================================================================= infrastructures
def arch_profile(w, h, n):
    """Gabarit cintré : piédroits puis voûte en anse (demi-ellipse) ; points de gauche à droite."""
    r = w / 2
    hs = h - r * 0.62                               # naissance de la voûte
    pts = []
    for k in range(n + 1):
        a = math.pi - math.pi * k / n
        pts.append((math.cos(a) * r, hs + math.sin(a) * (h - hs)))
    return hs, pts


def tunnel_portal(g):
    """Portail de tunnel : la route passe le long de Y par l'origine, façade tournée vers -Y, gabarit libre
    10 m × 6 m. Mur de tête en béton, bandeau de pierre autour de la voûte, murs en aile."""
    W, H = 10.0, 6.0
    T = 1.6                                         # épaisseur (y de 0 à T)
    HW, HT = 9.0, 9.6                               # demi-largeur et hauteur du mur de tête
    n = lod(g, 10, 6)
    hs, arc = arch_profile(W, H, n)
    for sx in (-1, 1):                              # piédroits
        g.box((sx * (W / 2 + HW) / 2, T / 2, hs / 2 - 1.5), (HW - W / 2, T, hs + 3.0), "concrete")
    for k in range(n):                              # tympan au-dessus de la voûte
        (x0, z0), (x1, z1) = arc[k], arc[k + 1]
        g.prism([(x0, z0), (x1, z1), (x1, HT), (x0, HT)], 0.0, T, "concrete", axis='y')
    for sx in (-1, 1):
        g.box((sx * (W / 2 + HW) / 2, T / 2, (hs + HT) / 2), (HW - W / 2, T, HT - hs), "concrete")
    g.box((0, T / 2 - 0.1, HT + 0.2), (2 * HW + 0.4, T + 0.4, 0.4), "stone")        # couronnement
    if g.lod == 0:
        ring = [(x * 1.09, hs + (z - hs) * 1.12) for x, z in arc]
        for k in range(n):                          # bandeau de claveaux
            g.prism([arc[k], arc[k + 1], ring[k + 1], ring[k]], -0.12, 0.05, "stone_warm" if k % 2 else "stone", axis='y')
        for sx in (-1, 1):
            g.box((sx * (W / 2 + 0.45), -0.04, hs / 2), (0.9, 0.16, hs), "stone")
        g.box((0, -0.08, H + 1.9), (2.2, 0.1, 0.9), "sign_blue")                    # plaque
        g.box((0, -0.1, H + 1.9), (1.8, 0.1, 0.5), "white")
    # amorce de la voûte (revêtement intérieur sur 6 m)
    for k in range(n):
        (x0, z0), (x1, z1) = arc[k], arc[k + 1]
        face_from(g, [(x0, T, z0), (x1, T, z1), (x1, T + 6.0, z1), (x0, T + 6.0, z0)], "concrete_dark", (0, T + 3, 2.5), away=False)
    for sx in (-1, 1):
        a, b = (sx * W / 2, T, -0.5), (sx * W / 2, T + 6.0, -0.5)
        face_from(g, [a, b, (b[0], b[1], hs), (a[0], a[1], hs)], "concrete_dark", (0, T + 3, 2.5), away=False)
    # murs en aile, évasés et descendants
    for sx in (-1, 1):
        m = g.mark()
        g.prism([(0, -3.0), (9.0, -3.0), (9.0, 2.2), (0, HT - 0.4)], -0.5, 0.5, "concrete", axis='x')
        f = g.since(m)
        g.transform(f, Matrix.Translation((sx * (HW - 0.5), 0.3, 0)) @ Matrix.Rotation(math.pi + sx * math.radians(25), 4, 'Z'))


item("tunnel_portal", "Tunnel_Portal", tunnel_portal, "infra", lods=2, tags=["tunnel"], seed=540, fp=(18.0, 1.6),
     collider="mesh", grad_h=10.0, note="road along Z through origin; face toward Unity +Z; clear opening 10 x 6 m; "
     "lining stub extends 7.6 m behind (Unity -Z)")


def gallery_module(g):
    """Paravalanche : module de 8 m le long de Y (la route passe le long de Y par l'origine). Mur plein côté -X
    (montagne), piliers côté +X (vallée), dalle couverte de terre."""
    L = 8.0
    g.box((-5.6, 0, 3.4 - 1.5), (1.0, L, 6.8 + 3.0), "concrete")
    g.box((0.25, 0, 6.65), (12.5, L, 0.7), "concrete")
    g.box((-0.4, 0, 7.15), (11.6, L - 0.06, 0.4), "grass")
    for y in (-2.0, 2.0):
        g.box((5.6, y, 3.15 - 1.5), (0.8, 0.9, 6.3 + 3.0), "concrete")
        if g.lod == 0:
            g.prism([(4.2, 6.3), (5.2, 6.3), (5.2, 5.3)], y - 0.3, y + 0.3, "concrete", axis='y')
    g.box((5.7, 0, 0.45), (0.5, L, 0.9), "concrete_dark")                           # muret de rive
    if g.lod == 0:
        g.box((-5.05, 0, 3.0), (0.06, L, 0.5), "concrete_dark")
        for y in (-3.0, 1.0):
            g.box((-1.0, y, 6.26), (1.2, 0.4, 0.08), "lamp")


item("gallery_module", "Gallery_Module", gallery_module, "infra", lods=2, tags=["gallery", "linear"], seed=541,
     fp=(12.6, 8.0), module=8.0, collider="mesh", grad_h=7.5,
     note="module repeats along Z (road axis through origin); solid wall on Unity +X (mountain), pillars on Unity -X (valley)",
     lights=[(-1.0, -3.0, 6.2), (-1.0, 1.0, 6.2)])


def power_pylon(g):
    """Pylône haute tension : treillis de 30 m, trois consoles (câbles le long de Y)."""
    H = 30.0
    lattice_tower(g, H, 3.0, 0.5, 5, colors=("metal", "metal"), leg=0.24)
    for z, half in ((19.0, 6.5), (24.0, 5.0), (28.5, 3.5)):
        g.prism([(-half, z), (half, z), (half * 0.2, z + 1.2), (-half * 0.2, z + 1.2)], -0.2, 0.2, "metal", axis='y')
        if g.lod < 2:
            for sx in (-1, 1):
                g.frustum((sx * (half - 0.3), 0, z - 1.5), 0.1, 0.1, 1.5, 5, "glass_blue")
    g.frustum((0, 0, H), 0.1, 0.02, 2.0, 4, "metal", cap0=False)


item("power_pylon", "Power_Pylon", power_pylon, "infra", lods=3, tags=["pylon", "power"], seed=542, fp=(6.0, 6.0),
     collider="box", grad_h=30.0, note="conductors along Z at Unity x = ±6.2 / ±4.7 / ±3.2, y = 17.5 / 22.5 / 27")


def antenna_mast(g):
    H = 22.0
    n = lod(g, 6, 5, 4)
    g.frustum((0, 0, -0.5), 0.3, 0.14, H + 0.5, n, "metal", cap0=False)
    g.box((0, 0, 0.2), (1.2, 1.2, 0.4), "concrete")
    for z, c in ((H - 1.5, "white"), (H - 4.0, "white"), (H - 6.5, "white")):
        for k in range(3):
            a = 2.094 * k + 0.4
            g.box((math.cos(a) * 0.5, math.sin(a) * 0.5, z), (0.12, 0.4, 1.8), c, rotz=a)
    if g.lod < 2:
        g.box((1.6, 0, 1.0), (1.4, 1.0, 2.0), "metal")
        g.box((0, 0, H + 0.2), (0.2, 0.2, 0.2), "neon_red")
        for k in range(3):
            a = 2.094 * k
            g.limb((0, 0, H * 0.7), (math.cos(a) * 7.0, math.sin(a) * 7.0, -0.3), 0.015, 0.015, 3, "metal_dark", cap1=False)


item("antenna_mast", "Antenna_Mast", antenna_mast, "infra", lods=3, tags=["tower"], seed=543, fp=(1.2, 1.2),
     collider="capsule", radius=0.4, grad_h=22.0)


def avalanche_barrier(g):
    """Râtelier paravalanche en acier : module de 4 m le long de X, face amont côté +Y."""
    L = 4.0
    for x in (-1.3, 1.3):
        g.beam((x, 0.0, -0.5), (x, -0.9, 3.0), 0.14, "rust")
        g.beam((x, -0.75, 2.4), (x, -2.4, -0.4), 0.1, "rust")
    for k in range(lod(g, 6, 4)):
        t = 0.2 + 0.8 * k / (lod(g, 6, 4) - 1)
        g.box((0, -0.9 * t + 0.08, 3.0 * t), (L, 0.06, 0.16), "metal")


item("avalanche_barrier", "Avalanche_Barrier", avalanche_barrier, "infra", tags=["linear", "slope"], seed=544,
     fp=(4.0, 2.6), module=4.0, note="uphill side = Unity -Z")


def bridge_pier(g):
    """Pile de pont en pierre : 4 × 2 m, 12 m de haut, sommet à z = 0 (origine en haut), avant-becs triangulaires."""
    g.box((0, 0, -0.3), (4.6, 2.5, 0.6), "stone")                                   # chapiteau
    g.prism([(-2.0, -1.0), (2.0, -1.0), (2.0, 1.0), (-2.0, 1.0)], -12.0, -0.6, "wall_stone", axis='z')
    if g.lod == 0:
        for z in (-3.5, -6.5, -9.5):
            g.box((0, 0, z), (4.12, 2.12, 0.25), "stone")
    for sy in (-1, 1):                               # avant-becs
        g.prism([(-1.2, sy * 1.0), (1.2, sy * 1.0), (0, sy * 2.2)], -12.0, -5.0, "wall_stone", axis='z')
        face_from(g, [(1.2, sy * 1.0, -5.0), (0, sy * 2.2, -5.0), (0, sy * 1.0, -3.8)], "stone", (0, 0, -6))
        face_from(g, [(0, sy * 2.2, -5.0), (-1.2, sy * 1.0, -5.0), (0, sy * 1.0, -3.8)], "stone", (0, 0, -6))
    g.box((0, 0, -12.6), (5.2, 4.8, 1.2), "stone_dark")                             # semelle


item("bridge_pier", "Bridge_Pier", bridge_pier, "infra", tags=["bridge"], seed=545, fp=(4.0, 2.0), collider="box",
     base=-13.2, note="origin at TOP (deck underside); pier spans y from -13.2 to 0; long side along X")


def dam_tower(g):
    """Tour de prise d'eau d'un barrage : fût rond en béton, lanterne vitrée, passerelle vers la rive (-Y).
    Origine au niveau de l'eau ; le fût descend 12 m dessous."""
    n = lod(g, 10, 8, 6)
    g.frustum((0, 0, -12.0), 3.2, 2.8, 20.0, n, "concrete", cap0=False)
    g.frustum((0, 0, 8.0), 3.3, 3.3, 0.4, n, "concrete_dark")
    g.frustum((0, 0, 8.4), 2.6, 2.6, 2.6, n, "glass_blue", cap0=False)
    g.frustum((0, 0, 11.0), 3.4, 0.4, 1.6, n, "copper")
    if g.lod < 2:
        g.frustum((0, 0, 12.6), 0.05, 0.02, 2.0, 4, "metal", cap0=False)
        for k in range(4):
            a = math.pi / 4 + math.pi / 2 * k
            g.box((math.cos(a) * 3.0, math.sin(a) * 3.0, 3.5), (0.3, 0.9, 1.4), "black", rotz=a)
    g.box((0, -9.0, 7.6), (2.2, 13.0, 0.3), "concrete")                            # passerelle
    if g.lod < 2:
        for sx in (-1, 1):
            g.box((sx * 1.05, -9.0, 8.25), (0.05, 13.0, 1.0), "metal")
    g.box((0, -9.0, -2.5), (0.9, 0.9, 20.0), "concrete")


item("dam_tower", "Dam_Tower", dam_tower, "infra", lods=3, tags=["dam", "lake"], seed=546, fp=(6.4, 6.4),
     collider="capsule", radius=3.2, base=0.0, grad_h=13.0,
     note="origin at water level; shaft reaches 12 m below; footbridge (deck y = 7.75) toward Unity +Z, 15.5 m long")


def reservoir_hut(g):
    W, D, H = 4.0, 3.2, 2.8
    K.foundation(g, W, D, "concrete", top=0.2)
    g.box((0, 0, H / 2), (W, D, H), "concrete")
    g.box((0, 0, H + 0.12), (W + 0.5, D + 0.5, 0.24), "concrete_dark")
    f = Fac(g, 'y', -D / 2, -1)
    f.door(-0.8, 1.0, 2.1, "sign_green", frame=None)
    if g.lod < 2:
        f.quad(1.0, 2.0, 0.9, 0.5, "metal_dark", 0.03)
        g.limb((W / 2 + 0.4, 0, 0.5), (W / 2 + 2.6, 0, 0.5), 0.3, 0.3, 7, "sign_green", cap0=True)   # conduite
        m = g.mark()
        g.frustum((0, 0, 0), 0.45, 0.45, 0.06, 8, "sign_red")
        g.frustum((0, 0, -0.5), 0.05, 0.05, 0.5, 4, "metal_dark")
        g.move(g.since(m), (W / 2 + 1.5, 0, 1.35))
        g.box((-W / 2 - 0.02, 0, 1.6), (0.04, 0.6, 0.6), "sign_yellow")


item("reservoir_hut", "Reservoir_Hut", reservoir_hut, "infra", tags=["dam", "building"], seed=547, fp=(4.0, 3.2),
     collider="box")
