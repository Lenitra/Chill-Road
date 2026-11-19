"""Terrain d'essai (bac à sable et démo du jeu) : sur le replat de la vallée est, entre la route de la vallée et
l'ancien lit de la rivière, au sud des Granges. On y arrive par une route (carrefour sur la route de la vallée, passage
sous le portique). Tout est fait de vraies routes du réseau :
- un circuit de 12 m de large, couru comme une course du jeu (« Circuit d'essai », Races.cs), portique de départ,
  murs de pneus, banderoles ;
- sa ligne droite est porte un radar et un tremplin (sites ajoutés à map_radars / map_stunts) ;
- dans l'infield, une grande arène de drift ovale goudronnée, avec deux poteaux entourés de pneus pour tourner en 8.
Pas de leçons ni de missions : on y essaie librement. Le jeu ne fait que peindre le sol (ProvingGround.cs).
Les identifiants internes gardent le préfixe « ecole » / « school ».

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
RADAR = ("ecole", "Radar du circuit", "Circuit Speed Camera", (100.0, 85.0), 80.0, 500)
STUNT = ("ecole", "Saut du circuit", "Circuit Jump", (100.0, -55.0), 10.0, 300)
START = (-92.0, 0.0)               # ligne de départ de la course (point de départ dans Races.cs)
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

    # Entrée : portique sur l'accès, petit bâtiment d'accueil au bord
    A = s.access["pts"]
    i = len(A) // 3
    t = A[min(i + 2, len(A) - 1), :2] - A[max(i - 2, 0), :2]
    pl.add("school_arch", float(A[i, 0]), float(A[i, 1]), yaw_of(-t / np.hypot(*t)))
    put("school_kiosk", (-122.0, -28.0), S)
    put("school_flag", (-116.0, -22.0), S)
    put("school_flag", (-128.0, -22.0), S)
    # Portique de départ au-dessus de la ligne de la course, drapeaux de part et d'autre
    put("school_gantry", (START[0], START[1]), S)
    put("school_flag", (START[0] - 10.0, START[1] + 2.0), E)
    put("school_flag", (START[0] + 10.0, START[1] + 2.0), W)
    # Poteaux du 8 entourés de pneus
    for p in PYLONS:
        put("school_pylon", p, N)
        for k in range(10):
            a = 2.0 * math.pi * k / 10.0
            put("school_tires", (p[0] + TIRE_RING * math.cos(a), p[1] + TIRE_RING * math.sin(a)), N)
    # Mur de pneus autour de l'arène, ouvert côté ligne droite
    half = (PYLONS[1][1] - PYLONS[0][1]) / 2.0
    mid = (PYLONS[0][0], (PYLONS[0][1] + PYLONS[1][1]) / 2.0)
    rr = ARENA_R + 2.5
    outline = []
    for k in range(25):
        a = math.pi * k / 24.0                                     # bout nord (u > 0), de l'est vers l'ouest
        outline.append((mid[0] + rr * math.cos(a), mid[1] + half + rr * math.sin(a)))
    for k in range(25):
        a = math.pi + math.pi * k / 24.0                           # bout sud, de l'ouest vers l'est
        outline.append((mid[0] + rr * math.cos(a), mid[1] - half + rr * math.sin(a)))
    outline.append(outline[0])
    ring = []
    for p, q in zip(outline[:-1], outline[1:]):
        seg = math.hypot(q[0] - p[0], q[1] - p[1])
        for f in np.arange(0.0, seg, 4.0):
            ring.append((p[0] + (q[0] - p[0]) * f / seg, p[1] + (q[1] - p[1]) * f / seg, q[0] - p[0], q[1] - p[1]))
    for (v, u, dv, du) in ring:
        if v < mid[0] - rr * 0.55:                                 # côté ligne droite : l'arène reste ouverte
            continue
        inward = np.array([mid[0] - v, min(max(u, mid[1] - half), mid[1] + half) - u])     # vers l'axe des poteaux
        tang = np.array([dv, du]) / max(math.hypot(dv, du), 1e-6)
        nrm = np.array([-tang[1], tang[0]])
        if nrm @ inward < 0:
            nrm = -nrm
        x, z = loc((v, u))
        pl.add("school_tirewall", x, z, yaw_of(nrm))
    # Murs de pneus continus à l'extérieur des virages, tournés vers la piste
    P, SS = s.lap, s.lap_s
    T = np.gradient(P[:, :2], SS, axis=0)
    T /= np.maximum(np.hypot(T[:, 0], T[:, 1]), 1e-6)[:, None]
    ang = np.unwrap(np.arctan2(T[:, 1], T[:, 0]))
    k = np.convolve(np.gradient(ang, SS), np.ones(9) / 9.0, mode="same")
    at = 0.0
    while at < SS[-1]:
        j = int(np.searchsorted(SS, at))
        j = min(j, len(P) - 1)
        if abs(k[j]) > 1.0 / 70.0:
            n = np.array([-T[j, 1], T[j, 0]]) * (-1.0 if k[j] > 0 else 1.0)       # extérieur du virage
            q = P[j, :2] + n * (HW + 5.5)
            pl.add("school_tirewall", float(q[0]), float(q[1]), yaw_of(-n))
        at += 4.0
    # Banderoles le long des deux lignes droites, lampadaires le long de la ligne droite de départ
    for du in np.arange(30.0, 131.0, 20.0):
        put("school_banner", (-82.0, du), W)
    for du in np.arange(-20.0, 131.0, 22.0):
        put("school_banner", (110.0, du), W)
    for du in (-20.0, 40.0, 100.0):
        put("lamp_road", (-104.0, du), E)


def race_points(edges):
    """Points de la course « Circuit d'essai » (à reporter dans Races.cs)."""
    return [loc((-92.0, 0.0)), loc((100.0, 0.0)), loc((0.0, -178.0))]


def pois(edges):
    x, z = loc(ARENA)
    return [dict(id="school", fr="Terrain d'essai", en="Proving Ground", x=round(x, 1), z=round(z, 1), r=220.0, side=1)]
