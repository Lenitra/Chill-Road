"""Station de ski « Les Combes » : neige, pistes, remontées, village du front de neige, congères des routes.

La station occupe la haute combe du massif de l'Ours (map_def : vallée « station », crêtes RIDGES). Autour d'elle la
limite de la neige descend (ubac froid) : le haut de la route d'accès et les rues sont enneigés. Le jeu lit la neige
dans map_info (octet 3) : sol, chaussées (shader de route), toits et sapins (shader des décors), adhérence.

Modèles : Tools/Blender/alp_ski.py (dameuse, motoneige, congères, magasin, caisses, totem, balisage) et le lot ski du
pack alpin (télésiège, téléphérique, résidence, hôtel, canons à neige, jalons).
"""
import math
import numpy as np
import map_lib as L
from map_terrain import stamp_polyline

INSTANCED, DETAIL = 1, 2

# Limite de la neige : « line » au cœur de la zone (rayon r0), limite générale au-delà de r1
SNOW_BASE = 760.0
SNOW = dict(c=(-950.0, 420.0), r0=650.0, r1=1250.0, line=255.0)
TREELINE_UP = 150.0            # la forêt monte plus haut autour de la station (sapins entre les pistes)

# Pistes, du haut vers le bas : (id, couleur, largeur, points)
PISTES = [
    ("combe", "blue", 52.0, [(-772, 945), (-800, 880), (-860, 720), (-900, 540), (-928, 430), (-936, 398)]),
    ("mur", "black", 30.0, [(-1020, 690), (-985, 610), (-945, 520), (-930, 440)]),
    ("soleil", "red", 32.0, [(-745, 650), (-800, 590), (-865, 505), (-915, 425)]),
    ("jardin", "green", 60.0, [(-870, 455), (-900, 415), (-925, 385)]),
]
# Remontées : (type, départ, arrivée)
LIFTS = [
    ("chairlift", (-893, 452), (-748, 905)),
    ("cablecar", (-986, 400), (-1088, 770)),
]
PARKING = (-872.0, 132.0)                 # centre du parking de la place
PARKING_LOT = (30.0, 17.0, -24.0)         # demi-longueur, demi-largeur, rotation (degrés)


def snow_line(X, Z):
    """Altitude de la limite de la neige en chaque point."""
    d = np.hypot(X - SNOW["c"][0], Z - SNOW["c"][1])
    w = 1.0 - L.smoothstep(SNOW["r0"], SNOW["r1"], d)
    return SNOW_BASE + (SNOW["line"] - SNOW_BASE) * w


def zone(X, Z):
    """Poids de la zone de la station (1 au cœur, 0 au-delà)."""
    d = np.hypot(X - SNOW["c"][0], Z - SNOW["c"][1])
    return 1.0 - L.smoothstep(SNOW["r0"], SNOW["r1"], d)


def snowy(g, H, x, z, margin=12.0):
    """Le point est-il franchement dans la neige ?"""
    return float(g.sample(H, x, z)) > float(snow_line(np.float64(x), np.float64(z))) + margin


def piste_mask(g):
    """Emprise des pistes (0-1, bords adoucis) et distance à l'axe le plus proche."""
    m = np.zeros(g.X.shape)
    for (pid, col, width, pts) in PISTES:
        P = L.catmull(np.array(pts, dtype=np.float64), 8.0)

        def fn(sz, sx, d, t, i, width=width):
            m[sz, sx] = np.maximum(m[sz, sx], 1.0 - L.smoothstep(width * 0.5 - 6.0, width * 0.5 + 4.0, d))
        stamp_polyline(g, P, width * 0.5 + 12.0, fn)
    return m


def groom(g, H, edge):
    """Pistes damées : le relief y est lissé (hors abords des routes)."""
    m = piste_mask(g) * L.smoothstep(6.0, 16.0, edge)
    if m.max() <= 0:
        return H
    sz, sx = g.window(-1150, 330, -650, 1000)
    sub = H[sz, sx]
    sm = L.blur2d(sub, 3.0)
    out = H.copy()
    out[sz, sx] = sub * (1 - m[sz, sx]) + sm * m[sz, sx]
    return out


def masks(g, H, snow, forest, rock, dry, fn):
    """Ajuste les masques du sol dans la station : pistes blanches et sans arbres."""
    pm = piste_mask(g)
    snow = np.maximum(snow, pm)
    forest = forest * (1.0 - np.minimum(1.0, pm * 1.6))
    rock = rock * (1.0 - pm)
    return snow, forest, rock, dry


def _road_y(edges, x, z, default):
    best, y = 1e9, default
    for e in edges:
        P = e["pts"]
        d = np.hypot(P[:, 0] - x, P[:, 1] - z)
        i = int(np.argmin(d))
        if d[i] < best and d[i] < 60.0:
            best, y = d[i], float(P[i, 2])
    return y


def lot_at(u, v):
    a = math.radians(PARKING_LOT[2])
    return PARKING[0] + u * math.cos(a) - v * math.sin(a), PARKING[1] + u * math.sin(a) + v * math.cos(a)


def pads(g, H, edges):
    """Parking de la place (aplani, à la hauteur de la rue)."""
    x, z = PARKING
    ex, ez = lot_at(-PARKING_LOT[0], 0.0)
    return [(x, z, PARKING_LOT, _road_y(edges, ex, ez, float(g.sample(H, x, z))))]


def pois():
    return [dict(id="pistes", fr="Tête de l'Ours", en="Tête de l'Ours", x=-1010.0, z=1150.0, r=260.0, side=1)]


def populate(P, g, H, nodes, edges):
    """Pose la station sur la carte (P : map_place.Placer déjà rempli par le village du Plan)."""
    from map_place import Placer, yaw_of, cable, CHALETS
    rng = P.rng
    E = P.edges
    n0 = len(P.places)
    if "rue_station" not in E:
        print("station : rues absentes")
        return P
    main, front, loop, place, gare, access = (E[k] for k in ("rue_station", "rue_front", "rue_chalets", "rue_place",
                                                              "rue_gare", "route_station"))

    # ---------------- Parking de la place : réservé (pas d'arbres), voitures absentes, tas de neige aux angles
    x, z = PARKING
    P.claim(Placer.corners(x, z, PARKING_LOT[0] * 2, PARKING_LOT[1] * 2, -PARKING_LOT[2]), x, z, 0.0)
    P.boxes.clear()
    py = P.h(x, z)
    for (u, v) in ((PARKING_LOT[0] - 4.0, PARKING_LOT[1] - 3.5), (PARKING_LOT[0] - 5.0, -PARKING_LOT[1] + 3.0)):
        qx, qz = lot_at(u, v)
        P.add("snow_pile", qx, qz, rng.uniform(0, 360), y=py - 0.1)
    for k in range(4):
        qx, qz = lot_at(-22.0 + k * 15.0, PARKING_LOT[1] + 1.2)
        P.add("lamp_road", qx, qz, 180.0 - PARKING_LOT[2], y=py)
    qx, qz = lot_at(-PARKING_LOT[0] + 3.0, -PARKING_LOT[1] - 1.5)
    P.add("sign_parking", qx, qz, 270.0 - PARKING_LOT[2])

    # ---------------- Remontées : gares imposées d'abord, le village se range autour
    for (kind, a, b) in LIFTS:
        if kind == "chairlift":
            cable(P, a, b, "chairlift_station", "chairlift_pylon", "chairlift_chair", None, span=62.0, top=11.9,
                  start_h=4.7, every=22.0)
        else:
            cable(P, a, b, "cablecar_station_low", "cablecar_pylon", "cablecar_cabin", "cablecar_station_high",
                  span=130.0, top=24.6, start_h=10.4)

    # ---------------- Front de neige : caisses, magasin, restaurant, hôtel, résidences
    P.first("ticket_office", gare["id"], (0.55, 0.4, 0.7), -1, setback=5.0, max_drop=5.0)
    P.first("ski_shop", front["id"], (0.72, 0.6, 0.5, 0.85), -1, setback=4.0, max_drop=5.0)
    P.first("hotel_chalet", main["id"], (0.55, 0.45, 0.65), 1, setback=5.0, max_drop=7.0)
    P.first("restaurant_altitude", front["id"], (0.45, 0.3, 0.6), 1, setback=5.0, max_drop=6.0)
    P.first("apartment_ski", loop["id"], (0.3, 0.4, 0.2, 0.5), -1, setback=7.0, max_drop=8.0)
    P.first("apartment_ski", front["id"], (0.2, 0.3, 0.12), -1, setback=7.0, max_drop=8.0)
    P.first("chapel", place["id"], (0.5, 0.7, 0.35), 1, setback=7.0, max_drop=6.0)

    def chalet():
        return rng.choice(CHALETS + ["chalet_e", "chalet_f"])

    for e, s0, s1 in ((main, 14.0, main["s"][-1] - 14.0), (front, 14.0, front["s"][-1] - 14.0),
                      (loop, 16.0, loop["s"][-1] - 16.0), (place, 14.0, place["s"][-1] - 20.0),
                      (access, access["s"][-1] - 150.0, access["s"][-1] - 20.0)):
        for side in (1, -1):
            P.row(e, s0, s1, side, chalet, setback=rng.uniform(4.5, 7.5), gap=(5.0, 13.0), max_drop=5.5, turn_jitter=6.0)
    n_build = sum(1 for p_ in P.places[n0:] if P.items[p_["item"]]["category"] in ("chalet", "landmark", "ski", "farm"))

    # ---------------- Mobilier des rues : lanternes, râteliers, bancs, tas de neige des impasses
    for e in (main, front, loop, place, gare):
        s, k = 12.0, 0
        while s < e["s"][-1] - 8.0:
            P.roadside("lamp_alpine", e, s, 1 if k % 2 == 0 else -1, e["hw"] + 1.5, face="road")
            s += 30.0
            k += 1
    for name in ("st_parking", "st_gare"):
        n = nodes[name]
        e = E["rue_place" if name == "st_parking" else "rue_gare"]
        _, _, _, t, nl = P.frame(e, e["s"][-1] - 1.0)
        P.add("snow_pile", n["x"] + t[0] * 11.0 + nl[0] * 3.0, n["z"] + t[1] * 11.0 + nl[1] * 3.0, rng.uniform(0, 360),
              y=n["y"] - 0.2)

    # ---------------- Pied des pistes : dameuse, motoneiges, râteliers, bonhomme de neige, igloo, luges
    gx, gz = nodes["st_gare"]["x"], nodes["st_gare"]["z"]

    def prop(item, dx, dz, yaw, **kw):
        x_, z_ = gx + dx, gz + dz
        if P.cell(P.edge_map, x_, z_) < 2.5 or P.cell(P.blocked, x_, z_):
            return False
        ok = P.add(item, x_, z_, yaw, **kw)
        it = P.items.get(item)
        if ok and it and it.get("collider", "none") != "none":
            w_, d_ = Placer.box_size(it)
            P.claim(Placer.corners(x_, z_, w_, d_, yaw), x_, z_, 0.5)
        return ok

    prop("snow_groomer", -34.0, 26.0, 150.0)
    prop("snowmobile", -26.0, 16.0, 200.0)
    prop("snowmobile", -23.0, 17.5, 215.0)
    for k in range(4):
        prop("ski_rack", 12.0 + k * 4.0, -6.0, 20.0)
    prop("igloo", 34.0, 30.0, 220.0)
    prop("snowman", 27.0, 22.0, 200.0)
    prop("snowman", -12.0, 30.0, 160.0)
    for k in range(4):
        prop("sled", 29.0 + rng.uniform(-3, 3), 26.0 + rng.uniform(-3, 3), rng.uniform(0, 360))
    prop("info_board", 6.0, -9.0, 200.0)
    prop("flagpole_red", -8.0, 8.0, 0.0)
    prop("flagpole_blue", -5.0, 9.5, 0.0)

    # ---------------- Pistes : jalons sur les deux rives, panneaux de couleur en haut et en bas, canons, filets, slalom
    for (pid, col, width, pts) in PISTES:
        C = L.catmull(np.array(pts, dtype=np.float64), 4.0)
        S_ = L.arclen(C)
        T = L.tangents(C)
        s = 6.0
        k = 0
        while s < S_[-1] - 4.0:
            i = int(np.searchsorted(S_, s))
            for side in (1, -1):
                x_, z_ = C[i, 0] - T[i, 1] * side * (width * 0.5 + 1.0), C[i, 1] + T[i, 0] * side * (width * 0.5 + 1.0)
                if P.cell(P.edge_map, x_, z_) > 4.0 and not P.cell(P.blocked, x_, z_):
                    P.add("piste_marker", x_, z_, yaw_of(T[i, 0], T[i, 1]), flags=INSTANCED | DETAIL)
                    if col != "green" and k % 5 == 2 and side > 0:
                        P.add("snow_cannon", x_ - T[i, 1] * 3.0, z_ + T[i, 0] * 3.0, yaw_of(-T[i, 1] * -1.0, T[i, 0] * -1.0) + 180.0)
            s += 34.0
            k += 1
        for (i, off) in ((2, -1.0), (len(C) - 3, 1.0)):
            x_, z_ = C[i, 0] - T[i, 1] * (width * 0.5 + 2.5), C[i, 1] + T[i, 0] * (width * 0.5 + 2.5)
            if P.cell(P.edge_map, x_, z_) > 4.0 and not P.cell(P.blocked, x_, z_):
                P.add("piste_sign_" + col, x_, z_, yaw_of(T[i, 0], T[i, 1]))
        # filets en bas de piste, sur la rive aval
        i = len(C) - 1
        for k in range(-2, 3):
            x_, z_ = C[i, 0] + T[i, 0] * 5.0 - T[i, 1] * k * 6.0, C[i, 1] + T[i, 1] * 5.0 + T[i, 0] * k * 6.0
            if pid in ("mur", "soleil") and P.cell(P.edge_map, x_, z_) > 5.0 and not P.cell(P.blocked, x_, z_):
                P.add("piste_net", x_, z_, yaw_of(-T[i, 0], -T[i, 1]), flags=INSTANCED)
    # slalom sur la piste bleue
    C = L.catmull(np.array(PISTES[0][3], dtype=np.float64), 4.0)
    S_, T = L.arclen(C), L.tangents(C)
    for k in range(12):
        i = int(np.searchsorted(S_, S_[-1] * 0.45 + k * 16.0))
        if i >= len(C) - 2:
            break
        off = (8.0 if k % 2 == 0 else -8.0) - 10.0
        P.add("slalom_gate_red" if k % 2 == 0 else "slalom_gate_blue", C[i, 0] - T[i, 1] * off, C[i, 1] + T[i, 0] * off,
              yaw_of(-T[i, 0], -T[i, 1]), flags=INSTANCED)

    # ---------------- Route d'accès : panneaux à l'entrée de la neige, totem à l'entrée de la station
    S_ = access["s"]
    snow_i = next((i for i in range(len(S_)) if snowy(g, H, access["pts"][i, 0], access["pts"][i, 1], 0.0)), None)
    if snow_i is not None and S_[snow_i] > 120.0:
        s0 = S_[snow_i]
        P.roadside("sign_snow", access, s0 - 90.0, -1, access["hw"] + 1.3, face="against")
        P.roadside("sign_chains", access, s0 - 40.0, -1, access["hw"] + 1.3, face="against")
        print("station : neige sur la route a partir de s = %.0f m (%.0f m de route enneigee)" % (s0, S_[-1] - s0))
    P.roadside("resort_totem", access, S_[-1] - 185.0, -1, access["hw"] + 5.5, face="against")

    # ---------------- Congères : le long des chaussées enneigées, des deux côtés, hors carrefours, ponts et façades
    n_bank = 0
    for e in edges:
        if e["kind"] in ("highway", "ramp"):
            continue
        Pt, S_ = e["pts"], e["s"]
        if not any(snowy(g, H, Pt[i, 0], Pt[i, 1]) for i in range(0, len(Pt), 8)):
            continue
        for side in (1, -1):
            s = 26.0 + rng.uniform(0.0, 6.0)
            while s < S_[-1] - 26.0:
                x_, z_, h_, t, nl = P.frame(e, s)
                long_ = rng.random() < 0.65
                off = e["hw"] + 2.6
                px, pz = x_ + nl[0] * side * off, z_ + nl[1] * side * off
                span = any(s0 - 8.0 <= s <= s1 + 8.0 for (s0, s1, kd) in e["spans"])
                clear = all(not P.cell(P.blocked, px + nl[0] * side * d_, pz + nl[1] * side * d_) for d_ in (0.0, 2.5))
                drop = abs(P.h(px, pz) - h_)
                if snowy(g, H, x_, z_) and not span and clear and drop < 1.6 and P.cell(P.edge_map, px, pz) > 1.2 \
                        and rng.random() < 0.82:
                    P.add("snow_bank_a" if long_ else "snow_bank_b", px, pz, yaw_of(t[0], t[1]) + 90.0 + (180.0 if rng.random() < 0.5 else 0.0),
                          y=min(P.h(px, pz), h_) - 0.12, scale=rng.uniform(0.85, 1.15), flags=INSTANCED)
                    n_bank += 1
                s += (9.4 if long_ else 6.6) + rng.uniform(0.0, 5.0)
    print("station : %d batiments, %d congeres, %d objets" % (n_build, n_bank, len(P.places) - n0))
    return P
