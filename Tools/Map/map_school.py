"""Auto-école (zone de tutoriel) : un vrai petit circuit goudronné posé sur le relief, au sud de la route de la vallée,
à l'ouest du départ. On y arrive par une route (carrefour sur la route de la vallée, passage sous le portique) ; une
longue ligne droite sert aux leçons d'accélération et de freinage, puis la piste enchaîne un virage en épingle, une
chicane et une grande courbe. Au bord de la ligne droite, une aire ronde goudronnée accueille le drift.

Le tracé est fait de routes du réseau (arêtes ecole_acces, ecole_piste_*) : terrassement, chaussée, carte et minicarte
viennent d'eux-mêmes. Le marquage et les leçons sont dans le jeu (DrivingSchool.cs), qui suit ces arêtes.

Repère local : u le long de la route de la vallée (vers l'est), v vers elle ; la route est en v = ROAD_V.
"""
import math
import numpy as np

SEED = (-281.1, -1810.0)          # carrefour sur la route de la vallée
ROAD_V = 80.0
NODE_S = (-85.0, 32.0)            # carrefour d'arrivée sur le circuit
NODE_M = (-12.0, -30.0)           # nœud intermédiaire (une boucle d'une seule arête n'est pas gérée par le réseau)
ACCESS = [(0.0, 68.0), (0.0, 58.0), (-6.0, 49.5), (-20.0, 45.5), (-48.0, 46.0), (-66.0, 47.0), (-78.0, 44.5), (-84.5, 39.0)]
LAP_A = [(-45.0, 33.0), (0.0, 33.0), (50.0, 33.0), (88.0, 31.0), (112.0, 18.0), (120.0, -8.0), (104.0, -30.0),
         (76.0, -36.0), (50.0, -26.0), (28.0, -12.0), (6.0, -14.0)]
LAP_B = [(-40.0, -46.0), (-78.0, -50.0), (-112.0, -38.0), (-126.0, -12.0), (-120.0, 14.0), (-106.0, 27.0)]
DRIFT = (-40.0, 8.0)              # centre de l'aire de drift
DRIFT_R = 22.0
# Leçons, en mètres depuis le carrefour d'arrivée (comme DrivingSchool.cs)
GREEN_END, BOX_FROM, BOX_TO = 95.0, 130.0, 146.0
ZONE_R = 150.0


class Site:
    pass


def frame(P, t):
    t = np.array(t, dtype=np.float64)
    t /= np.hypot(*t)
    if t[0] < 0:
        t = -t
    s = Site()
    s.t = t
    s.n = np.array([-t[1], t[0]])
    s.road = np.array(P, dtype=np.float64)
    s.o = s.road - s.n * ROAD_V
    return s


def loc(s, u, v):
    p = s.o + s.t * u + s.n * v
    return float(p[0]), float(p[1])


def roads(net, vallee, VP, VT):
    """Carrefour sur la route de la vallée, route d'accès, boucle du circuit."""
    i = int(np.argmin(np.hypot(VP[:, 0] - SEED[0], VP[:, 1] - SEED[1])))
    s = frame(VP[i], VT[i])
    x, z = float(VP[i, 0]), float(VP[i, 1])
    net.node("r_ecole", x, z)
    net.crossings.add("r_ecole")
    vallee.split("r_ecole", x, z)
    net.node("ecole_s", *loc(s, *NODE_S))
    a = net.road("ecole_acces", "lane", "r_ecole", smooth=12.0)
    a.to(*[loc(s, u, v) for (u, v) in ACCESS]).node("ecole_s")
    net.node("ecole_m", *loc(s, *NODE_M))
    c = net.road("ecole_piste", "main", "ecole_s", closed=True, smooth=12.0)
    c.to(*[loc(s, u, v) for (u, v) in LAP_A]).node("ecole_m")
    c.to(*[loc(s, u, v) for (u, v) in LAP_B])


def resolve(edges):
    a = next(e for e in edges if e["id"] == "ecole_acces")
    P = a["pts"]
    road = P[0, :2]
    # la route de la vallée, au carrefour : perpendiculaire à l'axe du début de l'accès
    d = P[6, :2] - P[0, :2]
    t = (-d[1], d[0])
    s = frame(road, t)
    if s.n @ (P[6, :2] - road) > 0:
        s = frame(road, (d[1], -d[0]))
    s.access = a
    laps = sorted((e for e in edges if e["id"].startswith("ecole_piste")), key=lambda e: e["id"])
    s.lap = np.vstack([laps[0]["pts"]] + [e["pts"][1:] for e in laps[1:]])
    s.lap_s = np.concatenate([[0.0], np.cumsum(np.hypot(*np.diff(s.lap[:, :2], axis=0).T))])
    return s


def on_line(P, S, at, side):
    """Point à l'abscisse at d'une ligne (x, z, y), décalé de side mètres à gauche ; tangente."""
    i = int(np.clip(np.searchsorted(S, at), 1, len(P) - 2))
    t = P[i + 1, :2] - P[i - 1, :2]
    t = t / np.hypot(*t)
    n = np.array([-t[1], t[0]])
    return P[i, :2] + n * side, t


def pads(edges):
    s = resolve(edges)
    x, z = loc(s, *DRIFT)
    d = np.hypot(s.lap[:, 0] - x, s.lap[:, 1] - z)
    return [(x, z, DRIFT_R, float(s.lap[int(np.argmin(d)), 2]))]


def zone(edges):
    """Zone traitée comme un village : ni glissières ni balises le long du circuit, pas de forêt."""
    s = resolve(edges)
    return [(*loc(s, -5.0, 0.0), ZONE_R)]


def yaw_of(d):
    return math.degrees(math.atan2(d[0], d[1])) % 360.0


def populate(pl, edges):
    from map_place import Placer
    s = resolve(edges)
    S, P = s.lap_s, s.lap
    dx, dz = loc(s, *DRIFT)
    pl.claim(Placer.corners(dx, dz, DRIFT_R * 2.0, DRIFT_R * 2.0, 0.0), dx, dz, 1.0)
    A, AS = s.access["pts"], s.access["s"]

    def line(item, Q, QS, at, side, face=-1.0):
        """Objet le long d'une ligne, côté side ; façade tournée vers le conducteur qui arrive (face = -1)."""
        p, t = on_line(Q, QS, at, side)
        return pl.add(item, float(p[0]), float(p[1]), yaw_of(t * face))

    # Portique au début de l'accès, guichet entre l'accès et la ligne droite
    line("school_arch", A, AS, 12.0, 0.0)
    kx, kz = loc(s, 30.0, 54.0)
    pl.add("school_kiosk", kx, kz, yaw_of(-s.n))
    # Panneaux, dans l'ordre lu par le jeu : accueil, accélérer, freiner, circuit, drift
    line("school_board", A, AS, 42.0, -6.5)
    line("school_board", P, S, 16.0, -7.0)
    line("school_board", P, S, GREEN_END - 16.0, -7.0)
    line("school_board", P, S, BOX_TO + 22.0, -7.0)
    bx, bz = loc(s, DRIFT[0] - DRIFT_R - 4.0, DRIFT[1])
    pl.add("school_board", bx, bz, yaw_of(s.t))
    # Cadre d'arrêt
    for at in (BOX_FROM, BOX_TO):
        for side in (-4.6, 4.6):
            line("school_cone", P, S, at, side)
    # Virages : cônes à la corde, piles de pneus à l'extérieur
    T = np.gradient(P[:, :2], S, axis=0)
    T /= np.maximum(np.hypot(T[:, 0], T[:, 1]), 1e-6)[:, None]
    ang = np.unwrap(np.arctan2(T[:, 1], T[:, 0]))
    k = np.gradient(ang, S)
    k = np.convolve(k, np.ones(9) / 9.0, mode="same")
    at = BOX_TO + 45.0
    last_cone = last_tire = -1e9
    while at < S[-1] - 25.0:
        ki = float(k[int(np.searchsorted(S, at))])
        if abs(ki) > 1.0 / 75.0:
            inner = 1.0 if ki > 0 else -1.0                 # k > 0 : la route tourne vers sa gauche
            if at - last_cone >= 11.0:
                line("school_cone", P, S, at, inner * 5.2, 1.0)
                last_cone = at
            if at - last_tire >= 15.0:
                line("school_tires", P, S, at, -inner * 8.5, 1.0)
                last_tire = at
        at += 3.0


def pois(edges):
    s = resolve(edges)
    x, z = loc(s, *DRIFT)
    return [dict(id="school", fr="Auto-école", en="Driving school", x=round(x, 1), z=round(z, 1), r=135.0, side=1)]
