"""Centre de pilotage (auto-école) : un terrain d'essai pour les nouveaux pilotes, sur le replat de la vallée est, entre
la route de la vallée et l'ancien lit de la rivière, au sud des Granges. On y arrive par une route (carrefour sur la
route de la vallée, passage sous le portique). Tout est fait de vraies routes du réseau :
- un circuit de 12 m de large, couru comme une course du jeu (« Circuit de l'école », Races.cs) ;
- sa ligne droite ouest sert aux leçons d'accélération et de freinage (marquage posé par le jeu) ;
- sa ligne droite est porte un radar et un tremplin (sites ajoutés à map_radars / map_stunts) ;
- dans l'infield, une grande arène de drift ovale goudronnée, avec deux poteaux entourés de pneus pour tourner en 8.
Les leçons sont dans le jeu (DrivingSchool.cs).

Repère local : v vers l'est, u vers le nord, en mètres depuis ORIGIN.
"""
import math
import numpy as np

ORIGIN = (1830.0, -830.0)
SEED = (1668.0, -881.0)            # carrefour sur la route de la vallée
HW, LANE = 6.0, 3.0                # demi-largeur et voie du circuit (route « main » élargie)
NODE_S = (-92.0, -60.0)            # arrivée de l'accès sur la ligne droite ouest (départ des leçons, vers le nord)
NODE_M = (100.0, 20.0)             # nœud au milieu de la ligne droite est (une boucle à une seule arête n'est pas gérée)
ACCESS = [(-130.0, -51.0), (-112.0, -56.0), (-100.0, -59.0)]
LAP_A = [(-92.0, -10.0), (-92.0, 60.0), (-91.0, 125.0), (-84.0, 155.0), (-62.0, 172.0), (-30.0, 174.0), (-8.0, 160.0),
         (8.0, 142.0), (30.0, 136.0), (52.0, 148.0), (72.0, 166.0), (92.0, 160.0), (100.0, 135.0), (100.0, 80.0)]
LAP_B = [(100.0, -40.0), (100.0, -100.0), (95.0, -140.0), (75.0, -168.0), (40.0, -180.0), (0.0, -176.0),
         (-35.0, -160.0), (-65.0, -150.0), (-86.0, -125.0), (-92.0, -95.0)]
# Arène de drift : ovale (rectangle et deux demi-cercles) contre la ligne droite ouest, deux poteaux à ses foyers
ARENA = (-58.0, -35.0)
ARENA_HALF, ARENA_R = 27.0, 30.0   # demi-longueur de la partie droite (le long de u), rayon des bouts
PYLONS = [(-58.0, -63.0), (-58.0, -7.0)]
TIRE_RING = 3.4
# Radar et tremplin sur la ligne droite est (parcourue vers le sud)
RADAR = ("ecole", "Radar de l'école", "School Speed Camera", (100.0, 85.0), 80.0, 500)
STUNT = ("ecole", "Saut de l'école", "School Jump", (100.0, -55.0), 10.0, 300)
ZONE_R = 260.0


def loc(u_v):
    v, u = u_v
    return float(ORIGIN[0] + v), float(ORIGIN[1] + u)


def register_sites():
    """Radar et tremplin de l'école, ajoutés aux sites des modules qui les posent (une seule fois)."""
    import map_radars as RD
    import map_stunts as ST
    sid, fr, en, at, target, reward = RADAR
    if not any(s[0] == sid for s in RD.SITES):
        RD.SITES.append((sid, fr, en, loc(at), (0.0, -1.0), target, reward))
    sid, fr, en, at, target, reward = STUNT
    if not any(s[0] == sid for s in ST.SITES):
        ST.SITES.append((sid, fr, en, loc(at), (0.0, -1.0), target, reward, "road"))


register_sites()


def roads(net, vallee, VP, VT):
    """Carrefour sur la route de la vallée, route d'accès, boucle du circuit (élargie)."""
    i = int(np.argmin(np.hypot(VP[:, 0] - SEED[0], VP[:, 1] - SEED[1])))
    x, z = float(VP[i, 0]), float(VP[i, 1])
    net.node("r_ecole", x, z)
    net.crossings.add("r_ecole")
    vallee.split("r_ecole", x, z)
    net.node("ecole_s", *loc(NODE_S))
    a = net.road("ecole_acces", "lane", "r_ecole", smooth=12.0)
    a.to(*[loc(p) for p in ACCESS]).node("ecole_s")
    net.node("ecole_m", *loc(NODE_M))
    wide = dict(hw=HW, lane=LANE)
    c = net.road("ecole_piste", "main", "ecole_s", closed=True, smooth=12.0, kind_over=wide)
    c.to(*[loc(p) for p in LAP_A]).node("ecole_m")
    c.to(*[loc(p) for p in LAP_B])


class Site:
    pass


def resolve(edges):
    s = Site()
    s.access = next(e for e in edges if e["id"] == "ecole_acces")
    laps = sorted((e for e in edges if e["id"].startswith("ecole_piste")), key=lambda e: e["id"])
    s.lap = np.vstack([laps[0]["pts"]] + [e["pts"][1:] for e in laps[1:]])
    s.lap_s = np.concatenate([[0.0], np.cumsum(np.hypot(*np.diff(s.lap[:, :2], axis=0).T))])
    return s


def height_near(s, x, z):
    d = np.hypot(s.lap[:, 0] - x, s.lap[:, 1] - z)
    return float(s.lap[int(np.argmin(d)), 2])


def pads(edges):
    """Arène de drift : rectangle et deux disques, à la hauteur de la ligne droite qu'elle borde."""
    s = resolve(edges)
    ax, az = loc(ARENA)
    y = height_near(s, *loc((ARENA[0] - ARENA_R, ARENA[1])))
    out = [(ax, az, (ARENA_R, ARENA_HALF, 0.0), y)]
    for du in (-ARENA_HALF, ARENA_HALF):
        out.append((ax, az + du, ARENA_R, y))
    return out


def zone(edges=None):
    """Zone traitée comme un village : ni glissières ni balises le long du circuit, pas de forêt."""
    return [(*loc((0.0, 0.0)), ZONE_R)]


def yaw_of(d):
    return math.degrees(math.atan2(d[0], d[1])) % 360.0


def populate(pl, edges):
    from map_place import Placer
    s = resolve(edges)
    ax, az = loc(ARENA)
    pl.claim(Placer.corners(ax, az, ARENA_R * 2.0, (ARENA_HALF + ARENA_R) * 2.0, 0.0), ax, az, 1.0)
    N, S, E, W = (0.0, 1.0), (0.0, -1.0), (1.0, 0.0), (-1.0, 0.0)

    def put(item, p, face):
        x, z = loc(p)
        return pl.add(item, x, z, yaw_of(face))

    # Accueil : portique sur l'accès, guichet au bord
    A = s.access["pts"]
    i = len(A) // 3
    t = A[min(i + 2, len(A) - 1), :2] - A[max(i - 2, 0), :2]
    pl.add("school_arch", float(A[i, 0]), float(A[i, 1]), yaw_of(-t / np.hypot(*t)))
    put("school_kiosk", (-122.0, -28.0), S)
    # Panneaux, dans l'ordre lu par le jeu : accueil, accélérer, freiner, radar, tremplin, drift, course
    put("school_board", (-112.0, -72.0), W)
    put("school_board", (-102.0, -40.0), S)
    put("school_board", (-102.0, 52.0), S)
    put("school_board", (90.0, 120.0), N)
    put("school_board", (90.0, 20.0), N)
    put("school_board", (-58.0, 25.0), S)
    put("school_board", (-102.0, -105.0), S)
    # Poteaux du 8 entourés de pneus
    for p in PYLONS:
        put("school_pylon", p, N)
        for k in range(10):
            a = 2.0 * math.pi * k / 10.0
            put("school_tires", (p[0] + TIRE_RING * math.cos(a), p[1] + TIRE_RING * math.sin(a)), N)
    # Cadre d'arrêt de la leçon de freinage (ligne droite ouest, de la sortie du carrefour vers le nord)
    for du in (140.0, 155.0):
        for dv in (-HW - 0.8, HW + 0.8):
            put("school_cone", (NODE_S[0] + dv, NODE_S[1] + du), N)
    # Piles de pneus à l'extérieur des virages
    P, SS = s.lap, s.lap_s
    T = np.gradient(P[:, :2], SS, axis=0)
    T /= np.maximum(np.hypot(T[:, 0], T[:, 1]), 1e-6)[:, None]
    ang = np.unwrap(np.arctan2(T[:, 1], T[:, 0]))
    k = np.convolve(np.gradient(ang, SS), np.ones(9) / 9.0, mode="same")
    last = -1e9
    for j in range(0, len(P), 2):
        if abs(k[j]) > 1.0 / 60.0 and SS[j] - last >= 13.0:
            n = np.array([-T[j, 1], T[j, 0]]) * (-1.0 if k[j] > 0 else 1.0)       # extérieur du virage
            q = P[j, :2] + n * (HW + 6.5)
            pl.add("school_tires", float(q[0]), float(q[1]), 0.0)
            last = SS[j]
    # Lampadaires le long de la ligne droite ouest
    for du in (-20.0, 40.0, 100.0):
        put("lamp_road", (-104.0, du), E)


def race_points(edges):
    """Points de la course « Circuit de l'école » (à reporter dans Races.cs)."""
    return [loc((-92.0, 0.0)), loc((100.0, 0.0)), loc((0.0, -178.0))]


def pois(edges):
    x, z = loc(ARENA)
    return [dict(id="school", fr="Auto-école", en="Driving school", x=round(x, 1), z=round(z, 1), r=220.0, side=1)]
