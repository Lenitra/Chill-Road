"""Masques du sol, données annexes et export de la carte vers Unity (Assets/NoHesi/Resources/Map)."""
import json
import math
import os
import numpy as np
import map_def as D
import map_lib as L
import map_roads as R
from map_terrain import stamp_polyline

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "NoHesi",
                                    "Resources", "Map"))


def street_distance(g, edges, kinds=("street",), radius=80.0):
    """Distance au réseau de rues (zones bâties)."""
    d = np.full(g.X.shape, radius, dtype=np.float32)
    for e in edges:
        if e["kind"] not in kinds:
            continue

        def fn(sz, sx, dist, t, i):
            d[sz, sx] = np.minimum(d[sz, sx], dist)
        stamp_polyline(g, e["pts"][::2], radius, fn)
    return d


def masks(g, H, info, edge, water, edges, urban_pts=(), blocked=None, pads=()):
    """Masques du sol : (RGBA sous-bois, herbe sèche, roche, terre) et (pavé, neige), plus densité de forêt."""
    X, Z = g.X, g.Z
    slope = L.slope_map(H, g.step)
    up = info["up"]
    fn = L.fbm(X / 420.0, Z / 420.0, 3, seed=D.SEED + 80)
    fn2 = L.fbm(X / 150.0, Z / 150.0, 2, seed=D.SEED + 81)
    treeline = 470.0 + 45.0 * L.fbm(X / 600.0, Z / 600.0, 2, seed=D.SEED + 82)

    # Zones bâties : rues de la ville et hameaux
    urban = 1.0 - L.smoothstep(22.0, 60.0, street_distance(g, edges))
    for (ux, uz, ur) in urban_pts:
        urban = np.maximum(urban, 1.0 - L.smoothstep(ur * 0.6, ur, np.hypot(X - ux, Z - uz)))

    # Forêt : versants (pas les fonds plats ni les parois), sous la limite des arbres, par grandes masses
    f_slope = L.smoothstep(0.10, 0.26, slope) * (1.0 - L.smoothstep(0.85, 1.15, slope))
    f_alt = 1.0 - L.smoothstep(treeline - 70.0, treeline, H)
    f_noise = L.smoothstep(-0.30, 0.02, fn + fn2 * 0.25)
    forest = f_slope * f_alt * f_noise
    # bosquets sur les fonds de vallée et les replats
    forest = np.maximum(forest, L.smoothstep(0.34, 0.5, fn - fn2 * 0.3) * f_alt * (1.0 - L.smoothstep(0.3, 0.5, slope)) * 0.85)
    forest *= 1.0 - urban
    forest *= L.smoothstep(3.0, 9.0, edge)
    wet = np.clip(L.blur2d((water > 0).astype(np.float64), 1.6) * 2.2, 0.0, 1.0)
    forest *= 1.0 - np.minimum(1.0, wet * 3.0)
    forest[water > 0] = 0.0

    dry = np.clip(L.smoothstep(360.0, 560.0, H + 40.0 * fn2) * 0.9 + L.smoothstep(0.45, 0.8, slope) * 0.3, 0, 1)
    rock = np.clip(L.smoothstep(0.8, 1.1, slope) + L.smoothstep(600.0, 800.0, H + 60.0 * fn) * 0.7, 0, 1)
    # Terre et gravier : bord immédiat des routes, rives, lits des torrents
    # (la bande de gravier des accotements est un maillage de la route : le sol ne garde de la terre que dessous)
    dirt = np.where(edge > -1.0, 1.0 - L.smoothstep(-0.2, 0.9, edge), 0.0)
    dirt = np.maximum(dirt, wet)
    for rv in info["rivers"]:
        w = float(rv["pts"][0][3])

        def fn_r(sz, sx, d, t, i, w=w):
            dirt[sz, sx] = np.maximum(dirt[sz, sx], 1.0 - L.smoothstep(w * 0.5 + 1.0, w * 0.5 + 5.0, d))
        stamp_polyline(g, rv["pts"][::2], w + 12.0, fn_r)
    paved = urban * L.smoothstep(34.0, 16.0, street_distance(g, edges, radius=60.0))
    from map_roads import pad_distance
    for (px, pz, pr, py) in pads:
        paved = np.maximum(paved, 1.0 - L.smoothstep(-3.0, 1.0, pad_distance(X, Z, px, pz, pr)))
    snow = L.smoothstep(690.0, 830.0, H + 70.0 * fn) * (1.0 - L.smoothstep(0.95, 1.35, slope))
    if blocked is not None:
        forest = np.where(blocked, 0.0, forest)

    def u8(a):
        return np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8)
    rgba = np.stack([u8(forest), u8(dry), u8(rock), u8(dirt)], axis=-1)
    return rgba, u8(paved), u8(snow), forest, urban, slope


def rails(g, H, e):
    """Glissières : là où le terrain plonge au bord de la chaussée. Bit 0 = gauche, bit 1 = droite, bit 2/3 = paroi
    (déblai) à gauche / à droite."""
    P = e["pts"]
    t = L.tangents(P)
    left = np.stack([-t[:, 1], t[:, 0]], axis=1)
    out = np.zeros(len(P), dtype=np.uint8)
    off = e["hw"] + e["sidewalk"]
    for bit, sgn in ((0, 1.0), (1, -1.0)):
        drop = np.zeros(len(P))
        rise = np.zeros(len(P))
        for d in (3.0, 6.0, 10.0):
            x = P[:, 0] + left[:, 0] * sgn * (off + d)
            z = P[:, 1] + left[:, 1] * sgn * (off + d)
            h = g.sample(H, x, z)
            drop = np.maximum(drop, (P[:, 2] - h) / d)
            rise = np.maximum(rise, (h - P[:, 2]) / d)
        need = (drop > 0.38)
        # lisse : pas de bouts de glissière isolés ni de trous courts
        k = max(1, int(12.0 / R.DS))
        need = np.convolve(need.astype(float), np.ones(2 * k + 1), mode='same') > k * 0.9
        out |= (need.astype(np.uint8) << bit)
        wall = rise > 0.6
        out |= (wall.astype(np.uint8) << (bit + 2))
    # ponts : parapets des deux côtés
    for (s0, s1, kind) in e["spans"]:
        if kind == "tunnel":
            continue
        m = (e["s"] >= s0 - 4.0) & (e["s"] <= s1 + 4.0)
        out[m] |= 3
    # musoirs : ni accotement ni glissière du côté où une autre chaussée touche celle-ci (bits 4 et 5)
    if "bare" in e:
        out |= (e["bare"].astype(np.uint8) & 3) << 4
    return out


def fix_rivers(g, H, edge, rivers, edges=(), water=None):
    """Ajuste les rivières au relief final : l'eau reste sous les berges (0,4 m au-dessus du fond au plus près des
    routes) et le ruban est coupé là où une route en remblai a comblé le lit (hors ponts, où le lit subsiste)."""
    out = []
    # points des tabliers de pont : là, le lit passe sous la route
    B = []
    for e in edges:
        for (s0, s1, kind) in e["spans"]:
            m = (e["s"] >= s0 - 2.0) & (e["s"] <= s1 + 2.0)
            B.extend(e["pts"][m, :2].tolist())
    B = np.array(B, dtype=np.float64).reshape(-1, 2)
    for rv in rivers:
        P = np.array(rv["pts"], dtype=np.float64)
        ground = np.asarray(g.sample(H, P[:, 0], P[:, 1]), dtype=np.float64)
        ed = np.asarray(g.sample(edge.astype(np.float64), P[:, 0], P[:, 1]), dtype=np.float64)
        P[:, 2] = np.minimum(P[:, 2], ground + 0.45)
        # lit comblé : au contact d'une plate-forme routière, le sol est remonté au niveau de l'eau d'origine
        filled = ed < 1.5
        # dans un lac (et jusqu'à 10 m de sa nappe) : c'est le plan d'eau du lac qui se voit
        if water is not None:
            near_lake = L.blur2d((water > 0).astype(np.float64), 2.5) > 0.002
            filled |= np.asarray(g.sample(near_lake.astype(np.float64), P[:, 0], P[:, 1])) > 0.01
        # confluent : l'affluent s'arrête au bord du ruban de la rivière principale (pas deux nappes superposées)
        for other in rivers:
            if other is rv or float(other["pts"][0][3]) < float(rv["pts"][0][3]):
                continue
            O = np.array(other["pts"], dtype=np.float64)
            lim = float(O[0][3]) * 0.5 + 2.5 + float(rv["pts"][0][3]) * 0.5 + 2.5 + 1.0
            for k in range(len(P)):
                if L.point_polyline(P[k, :2], O)[0] < lim:
                    filled[k] = True
        if len(B):
            dmin = np.sqrt(((P[:, None, 0] - B[None, :, 0]) ** 2 + (P[:, None, 1] - B[None, :, 1]) ** 2).min(axis=1))
            filled &= dmin > 9.0
        k = 0
        start = None
        for i in range(len(P) + 1):
            ok = i < len(P) and not filled[i]
            if ok and start is None:
                start = i
            if not ok and start is not None:
                if i - start >= 3:
                    out.append(dict(id="%s_%d" % (rv["id"], k), pts=P[start:i]))
                    k += 1
                start = None
    return out


def export(g, H, info, nodes, edges, water, edge, rgba, paved, snow, places=(), pois=(), blocked=None, holes=None,
           tunnels=None):
    os.makedirs(OUT, exist_ok=True)
    hmin = float(math.floor(H.min() - 1.0))
    hmax = float(math.ceil(H.max() + 1.0))
    h16 = np.clip((H - hmin) / (hmax - hmin) * 65535.0 + 0.5, 0, 65535).astype('<u2')
    h16.tofile(os.path.join(OUT, "map_heights.bytes"))
    rgba.astype(np.uint8).tofile(os.path.join(OUT, "map_masks.bytes"))
    e8 = np.clip((edge + 1.0) * 4.0, 0, 255).astype(np.uint8)          # (distance au bord + 1 m) × 4
    # drapeaux : plan d'eau (bits 0-2), zone occupée (bit 3), maille de terrain supprimée — tête de tunnel (bit 4)
    flags = water.astype(np.uint8) & 7
    if blocked is not None:
        flags = flags | (blocked.astype(np.uint8) << 3)
    if holes is not None:
        flags = flags | (holes.astype(np.uint8) << 4)
    # au-dessus d'un tunnel (bit 5) : pas de jupe sous les bords de tuile
    if tunnels is not None:
        flags = flags | (tunnels.astype(np.uint8) << 5)
    np.stack([e8, flags, paved, snow], axis=-1).astype(np.uint8).tofile(os.path.join(OUT, "map_info.bytes"))

    def r2(v):
        return round(float(v), 2)

    def edge_json(e):
        P = e["pts"]
        # abscisses en longueur réelle (3D), comme RoadNetwork.Edge.dist
        s3 = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])

        def st(v):
            return r2(np.interp(v, e["s"], s3))
        return dict(id=e["id"], kind=e["kind"], a=e["a"], b=e["b"], hw=e["hw"], lane=e["lane"], sidewalk=e["sidewalk"],
                    dirt=e["dirt"], speed=e["speed"], lanes=e.get("lanes", 1), median=e.get("median", 0.0),
                    oneway=e.get("oneway", 0),
                    pts=[r2(v) for q in P for v in (q[0], q[2], q[1])],
                    rail=[int(v) for v in e["rail"]],
                    spans=[dict(s0=st(a), s1=st(b), kind=k) for (a, b, k) in e["spans"]],
                    aux=[dict(sa=st(sa), sb=st(sb), side=int(side), width=r2(w)) for (sa, sb, side, w) in e.get("aux", ())])
    data = dict(
        n=g.n, step=g.step, half=g.half, hmin=hmin, hmax=hmax,
        waters=[dict(id=lk["id"], level=float(lk["level"]), x=float(lk["c"][0]), z=float(lk["c"][1]),
                     r=float(max(lk["a"], lk["b"]) * 1.8)) for lk in D.LAKES],
        nodes=[dict(id=n["id"], x=r2(n["x"]), y=r2(n["y"]), z=r2(n["z"]), merge=int(n.get("merge", 0))) for n in nodes.values()],
        edges=[edge_json(e) for e in edges],
        rivers=[dict(id=rv["id"], width=float(rv["pts"][0][3]),
                     pts=[r2(v) for q in rv["pts"] for v in (q[0], q[2], q[1])]) for rv in fix_rivers(g, H, edge, info["rivers"], edges, water)],
        places=list(places), pois=list(pois))
    with open(os.path.join(OUT, "map.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"))
    print("export : %s (%d aretes, %d objets)" % (OUT, len(edges), len(places)))
