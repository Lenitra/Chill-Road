"""Le Plan : le village (maisons le long de la route, place de l'église et de la mairie, café, fermes) et la
concession automobile de la sortie d'autoroute (parking, hall d'exposition, totem, podiums).

Village-rue savoyard : maisons de bourg serrées autour du carrefour central (la place), chalets plus espacés en
s'éloignant, fermes en bordure. Les modèles viennent du pack alpin ; la pose et les contrôles (pas sur la route, pas
dans la rivière, pas les uns sur les autres, pente acceptable) sont ceux de map_place.Placer.
"""
import math
from map_place import Placer, yaw_of, TOWNHOUSES, CHALETS, PLAIN_HOUSES

# Concession : parking goudronné rectangulaire (centre x, z, demi-côtés) au bord de la route d'accès à l'autoroute,
# entre la rivière et le village ; le hall est au fond, façade (et auvent) tournée vers la route
DEALER = (402.0, -1870.0, 50.0)
LOT = (48.0, 40.0, 0.0)
SHOWROOM = (434.0, -1870.0, -90.0)          # origine du modèle (centre du hall), cap : façade vers l'ouest
VILLAGE = "r_ech_s"                         # la place du village : le carrefour central du Plan


def pads(g, H, edges=()):
    """Aires aplanies et goudronnées : (x, z, forme, altitude). Le parking est à la hauteur de la route qui le borde
    (pas de talus ni de glissière entre les deux)."""
    import numpy as np
    x, z, r = DEALER
    y = float(g.sample(H, x, z))
    best = 1e9
    for e in edges:
        P = e["pts"]
        d = np.hypot(P[:, 0] - (x - LOT[0]), P[:, 1] - z)
        i = int(np.argmin(d))
        if d[i] < best and d[i] < 30.0:
            best, y = d[i], float(P[i, 2])
    return [(x, z, LOT, y)]


def pois():
    x, z, r = DEALER
    return [dict(id="dealer", fr="Concession", en="Car dealer", x=x + 30.0, z=z, r=r + 20.0, side=1)]


def _edge(edges, a, b):
    for e in edges:
        if (e["a"], e["b"]) == (a, b):
            return e, 1
        if (e["a"], e["b"]) == (b, a):
            return e, -1
    return None, 0


def populate(g, H, edge_map, water, nodes, edges, rivers, seed=23):
    P = Placer(g, H, edge_map, water, nodes, edges, rivers, seed=seed)
    rng = P.rng

    # ---------------- Concession (posée la première : sa place est réservée)
    x, z, r = DEALER
    P.claim(Placer.corners(x, z, LOT[0] * 2, LOT[1] * 2, 0.0), x, z, 0.0)
    P.boxes.clear()                          # la réserve ne bloque que la végétation ; les objets se posent dessus
    sx, sz, yaw = SHOWROOM
    y = float(g.sample(H, sx, sz))
    P.add("car_showroom", sx, sz, yaw, y=y)
    P.claim(Placer.corners(sx - 2.5, sz, 23.5, 38.6, yaw + 90.0), sx, sz, 1.0)
    P.add("car_podium", sx - 30.0, sz - 11.0, 70.0, y=y)
    P.add("car_podium", sx - 30.0, sz + 11.0, 110.0, y=y)
    P.add("dealer_sign", x - 36.0, z + 34.0, -90.0, y=float(g.sample(H, x - 36.0, z + 34.0)))
    for k in range(4):
        P.add("lamp_road", x + 2.0, z - 30.0 + k * 20.0, 90.0, y=y)

    # ---------------- Place du village : église, mairie, fontaine, café, arrêt de car
    sq = nodes[VILLAGE]
    west, sw = _edge(edges, "r_col_s", VILLAGE)     # route de la vallée, vers l'ouest
    east, se = _edge(edges, VILLAGE, "r_plage")     # vers l'est
    north, sn = _edge(edges, VILLAGE, "h_hauts")    # route des hameaux (balcon)
    L1 = west["s"][-1]

    def at_square(e, d):
        """Abscisse à d mètres de la place, sur l'arête e."""
        return d if e["a"] == VILLAGE else e["s"][-1] - d

    # côté gauche d'une arête = +1 ; « nord » de la route de la vallée = gauche dans son sens (vers l'est)
    P.first("church", west["id"], (at_square(west, 46.0), at_square(west, 60.0), at_square(west, 75.0)), 1, setback=7.0,
            max_drop=7.0)
    P.first("townhall", east["id"], (at_square(east, 40.0), at_square(east, 55.0), at_square(east, 70.0)), 1, setback=5.0,
            max_drop=6.0)
    P.first("restaurant_altitude", west["id"], (at_square(west, 40.0), at_square(west, 55.0)), -1, setback=6.0)
    P.first("fountain_plaza", north["id"], (at_square(north, 26.0), at_square(north, 34.0)), -1, setback=3.0, clear=0.0)
    P.first("busstop", east["id"], (at_square(east, 75.0), at_square(east, 90.0)), -1, setback=1.0, clear=0.0)

    # ---------------- Maisons : bourg serré autour de la place, chalets plus loin, fermes en bordure
    def pick_core():
        return rng.choice(TOWNHOUSES)

    def pick_outer():
        return rng.choice(CHALETS + CHALETS + PLAIN_HOUSES)

    def street(e, d0, d1, core, sides=(1, -1)):
        """Rangées entre d0 et d1 mètres de la place, le long de l'arête e."""
        s0, s1 = sorted((at_square(e, d0), at_square(e, d1)))
        s0, s1 = max(s0, 14.0), min(s1, e["s"][-1] - 14.0)
        if s1 - s0 < 10.0:
            return
        for side in sides:
            if core:
                P.row(e, s0, s1, side, pick_core, setback=2.5, alley=0.25, max_drop=4.0)
            else:
                P.row(e, s0, s1, side, pick_outer, setback=rng.uniform(5.0, 9.0), gap=(6.0, 16.0), max_drop=5.0,
                      turn_jitter=8.0)

    street(west, 30.0, 160.0, True)
    street(west, 160.0, L1 - 40.0, False)
    street(east, 30.0, 150.0, True)
    street(east, 150.0, 300.0, False)
    street(north, 30.0, 110.0, True)
    street(north, 110.0, 260.0, False)
    acc, _ = _edge(edges, "ech_s_l", VILLAGE)
    if acc is not None:
        street(acc, 30.0, 90.0, True, sides=(1,))     # côté ouest seulement : la concession est à l'est
    col, _ = _edge(edges, "r_col_s", "col")
    if col is not None:
        P.row(col, 70.0, 260.0, 1, lambda: rng.choice(CHALETS), setback=6.0, gap=(10.0, 24.0), max_drop=5.0,
              turn_jitter=10.0)
        P.row(col, 90.0, 300.0, -1, lambda: rng.choice(["barn_alpine", "cowshed", "hayloft", "mazot"]), setback=10.0,
              gap=(14.0, 30.0), max_drop=5.0, turn_jitter=12.0)
    P.row(west, 20.0, 90.0, -1, lambda: rng.choice(["barn_alpine", "hayloft", "mazot"]), setback=12.0, gap=(12.0, 26.0),
          max_drop=5.0, turn_jitter=12.0)

    # ---------------- Mobilier : lampadaires dans la traversée, panneaux d'entrée d'agglomération
    for e, d0, d1 in ((west, 20.0, 200.0), (east, 20.0, 220.0), (north, 20.0, 120.0)):
        d, k = d0, 0
        while d < d1:
            P.roadside("lamp_alpine", e, at_square(e, d), 1 if k % 2 == 0 else -1, e["hw"] + 1.6, face="road")
            d += 28.0
            k += 1
    # entrée d'agglomération : à droite de la voie qui arrive, face aux conducteurs
    for e, d in ((west, 230.0), (east, 300.0), (north, 170.0)):
        s = at_square(e, d)
        if 20.0 < s < e["s"][-1] - 20.0:
            toward = 1 if e["b"] == VILLAGE else -1        # sens de marche vers la place
            if toward > 0:
                P.roadside("sign_town", e, s, -1, e["hw"] + 1.4, face="against")
            else:
                P.roadside("sign_town", e, s, 1, e["hw"] + 1.4, face="along")
    n_build = sum(1 for p_ in P.places if P.items[p_["item"]]["category"] in ("townhouse", "chalet", "landmark", "farm"))
    print("village du Plan : %d bâtiments, %d objets" % (n_build, len(P.places)))
    return P
