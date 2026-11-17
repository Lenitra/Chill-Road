"""Masques du sol, données annexes et export de la carte vers Unity (Assets/ChillRoad/Resources/Map)."""
import json
import math
import os
import numpy as np
import map_def as D
import map_lib as L
import map_roads as R
from map_terrain import stamp_polyline

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "ChillRoad",
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
    import map_station as SK
    treeline = 470.0 + 45.0 * L.fbm(X / 600.0, Z / 600.0, 2, seed=D.SEED + 82) + SK.TREELINE_UP * SK.zone(X, Z)

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
    # Grève irrégulière : bancs de galets larges par endroits, herbe jusqu'à l'eau ailleurs (pas une bande régulière)
    shore = np.clip(L.fbm(X / 45.0, Z / 45.0, 2, seed=D.SEED + 310) * 2.0 + 0.5, 0.0, 1.0)
    for rv in info["rivers"]:
        P = np.asarray(rv["pts"], dtype=np.float64)
        hw = np.asarray(rv.get("hw", np.full(len(P), float(P[0][3]) * 0.5)))

        def fn_r(sz, sx, d, t, i, hw=hw):
            h = hw[i] + t * (hw[i + 1] - hw[i])
            band = h + 0.4 + 4.5 * shore[sz, sx]
            dirt[sz, sx] = np.maximum(dirt[sz, sx], 1.0 - L.smoothstep(band - 1.5, band + 1.5, d))
        stamp_polyline(g, P, float(hw.max()) + 12.0, fn_r)
    paved = urban * L.smoothstep(34.0, 16.0, street_distance(g, edges, radius=60.0))
    from map_roads import pad_distance
    for (px, pz, pr, py) in pads:
        paved = np.maximum(paved, 1.0 - L.smoothstep(-3.0, 1.0, pad_distance(X, Z, px, pz, pr)))
    # limite de la neige : 760 m en général, plus basse autour de la station (map_station.SNOW)
    line = SK.snow_line(X, Z)
    snow = L.smoothstep(line - 70.0, line + 70.0, H + 70.0 * fn) * (1.0 - L.smoothstep(0.95, 1.35, slope))
    snow, forest, rock, dry = SK.masks(g, H, snow, forest, rock, dry, fn)
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


def _descending(v):
    """Régression isotone décroissante (les remontées sont moyennées avec ce qui précède)."""
    vals, wts = [], []
    for x in v:
        vals.append(float(x))
        wts.append(1)
        while len(vals) > 1 and vals[-1] > vals[-2]:
            w = wts[-1] + wts[-2]
            vals[-2] = (vals[-2] * wts[-2] + vals[-1] * wts[-1]) / w
            wts[-2] = w
            vals.pop()
            wts.pop()
    return np.repeat(np.array(vals), wts)


def river_beds(g, H, edge, rivers, water=None):
    """Lits des rivières sur le relief final (après le terrassement des routes, qui comblait parfois le lit : nappe
    posée sur le pré). Pour chaque cours :
    - sens de l'amont vers l'aval (certains tracés étaient donnés depuis l'embouchure : le courant remontait) ;
    - largeur du lit qui varie le long du cours (resserrements, élargissements), au lieu d'un canal régulier ;
    - niveau de l'eau sous les deux berges et jamais en remontée ;
    - lit creusé sous l'eau (fond 0,35-0,9 m dessous, berges en pente), hors des plates-formes routières et des lacs.
    Renvoie (H creusé, rivières : pts = (x, z, eau, largeur), hw = demi-largeur du lit à chaque point)."""
    H = H.copy()
    keep = edge >= 3.0
    if water is not None:
        keep &= water == 0
    keep_f = keep.astype(np.float64)
    out = []
    for k, rv in enumerate(rivers):
        P = np.array(rv["pts"], dtype=np.float64)
        if P[0, 2] < P[-1, 2]:
            P = P[::-1].copy()
        width = float(P[0, 3])
        n = L.fbm(P[:, 0] / 90.0, P[:, 1] / 90.0, 2, seed=D.SEED + 300 + k)
        hw = width * 0.5 * np.clip(1.0 + n, 0.65, 1.45)
        t = L.tangents(P[:, :2])
        nx, nz = -t[:, 1], t[:, 0]
        # Seuls les points hors lac et hors plate-forme routière imposent leur niveau (le fond d'un lac ou un déblai
        # de route tirait sinon toute la suite du cours des dizaines de mètres sous le sol)
        free = np.asarray(g.sample(keep_f, P[:, 0], P[:, 1])) > 0.5
        caps = np.where(free, np.minimum(P[:, 2], np.asarray(g.sample(H, P[:, 0], P[:, 1])) + 0.45), P[:, 2])
        for sgn in (1.0, -1.0):
            bx, bz = P[:, 0] + nx * sgn * (hw + 1.5), P[:, 1] + nz * sgn * (hw + 1.5)
            gb = np.asarray(g.sample(H, bx, bz))
            ok = (np.asarray(g.sample(keep_f, bx, bz)) > 0.5) & free
            caps = np.where(ok, np.minimum(caps, gb - 0.2), caps)
        idx = np.where(free)[0]
        if len(idx) < 2:
            out.append(dict(id=rv["id"], pts=P, hw=hw))
            continue
        wf = np.minimum.accumulate(np.minimum(_descending(caps[idx]), caps[idx]))
        w = np.interp(np.arange(len(P)), idx, wf)
        best = np.full(H.shape, 1e9)
        level = np.zeros(H.shape)
        half = np.ones(H.shape)

        def nearest(sz, sx, d, tt, i):
            m = d < best[sz, sx]
            best[sz, sx] = np.where(m, d, best[sz, sx])
            level[sz, sx] = np.where(m, w[i] + tt * (w[i + 1] - w[i]), level[sz, sx])
            half[sz, sx] = np.where(m, hw[i] + tt * (hw[i + 1] - hw[i]), half[sz, sx])
        stamp_polyline(g, P[:, :2], float(hw.max()) + 4.0, nearest)
        u = best / np.maximum(half, 0.1)
        bed = np.where(u < 1.0, level - 0.35 - 0.55 * (1.0 - u ** 2), level - 0.35 + (best - half) * 0.9)
        # garde-fou : jamais plus de 2,5 m de déblai (au-delà, c'est le niveau qui serait faux)
        H = np.where((best < 1e8) & keep, np.maximum(np.minimum(H, bed), H - 2.5), H)
        P[:, 2] = w
        out.append(dict(id=rv["id"], pts=P, hw=hw))
    return H, out


def fix_rivers(g, H, edge, rivers, edges=(), water=None):
    """Coupe le ruban des rivières là où une route en remblai a comblé le lit (hors ponts, où le lit subsiste), près
    des lacs et aux confluents. Le niveau de l'eau a déjà été réglé sur les berges par river_beds."""
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
        ed = np.asarray(g.sample(edge.astype(np.float64), P[:, 0], P[:, 1]), dtype=np.float64)
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
           tunnels=None, stunts=()):
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

        def span(a, b, k):
            d = dict(s0=st(a), s1=st(b), kind=k)
            if k == "tunnel" and R.underpass(e, a, b):
                d["roof"] = R.UNDERPASS_ROOF       # coque mince (RoadNetwork : dalle au-dessus du gabarit)
            return d
        return dict(id=e["id"], kind=e["kind"], a=e["a"], b=e["b"], hw=e["hw"], lane=e["lane"], sidewalk=e["sidewalk"],
                    dirt=e["dirt"], speed=e["speed"], lanes=e.get("lanes", 1), median=e.get("median", 0.0),
                    oneway=e.get("oneway", 0),
                    pts=[r2(v) for q in P for v in (q[0], q[2], q[1])],
                    rail=[int(v) for v in e["rail"]],
                    spans=[span(a, b, k) for (a, b, k) in e["spans"]],
                    aux=[dict(sa=st(sa), sb=st(sb), side=int(side), width=r2(w)) for (sa, sb, side, w) in e.get("aux", ())])
    data = dict(
        n=g.n, step=g.step, half=g.half, hmin=hmin, hmax=hmax,
        waters=[dict(id=lk["id"], level=float(lk["level"]), x=float(lk["c"][0]), z=float(lk["c"][1]),
                     r=float(max(lk["a"], lk["b"]) * 1.8)) for lk in D.LAKES],
        nodes=[dict(id=n["id"], x=r2(n["x"]), y=r2(n["y"]), z=r2(n["z"]), merge=int(n.get("merge", 0))) for n in nodes.values()],
        edges=[edge_json(e) for e in edges],
        rivers=[dict(id=rv["id"], width=float(rv["pts"][0][3]),
                     pts=[r2(v) for q in rv["pts"] for v in (q[0], q[2], q[1])]) for rv in fix_rivers(g, H, edge, info["rivers"], edges, water)],
        places=list(places), pois=list(pois), stunts=list(stunts))
    with open(os.path.join(OUT, "map.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"))
    print("export : %s (%d aretes, %d objets)" % (OUT, len(edges), len(places)))
