"""Relief naturel de la carte : vallées glaciaires (minimum des profils en travers), sommets, cirque de bordure,
détail de bruit, cuvettes des lacs, replats et lits de rivières."""
import math
import numpy as np
import map_def as D
import map_lib as L


def profile_table(spec, dmax=6000.0, dd=5.0):
    """Intègre le profil (distance, pente) en hauteur ; renvoie (distances, hauteurs)."""
    d = np.arange(0.0, dmax + dd, dd)
    xs = [p[0] for p in spec]
    ss = [p[1] for p in spec]
    slope = np.interp(d, xs, ss)
    h = np.concatenate([[0.0], np.cumsum((slope[1:] + slope[:-1]) * 0.5 * dd)])
    return d, h


def valley_line(v, step=60.0):
    """Ligne de fond lissée : (x, z, altitude, demi-largeur)."""
    return L.catmull(v["pts"], step)


def stamp_polyline(g, pts, radius, fn):
    """Parcourt les segments d'une polyligne (n, k) et appelle fn(tranche_z, tranche_x, distance, t, i) sur la
    fenêtre de grille qui entoure chaque segment (t = position 0-1 le long du segment i)."""
    pts = np.asarray(pts, dtype=np.float64)
    for i in range(len(pts) - 1):
        ax, az = pts[i, 0], pts[i, 1]
        bx, bz = pts[i + 1, 0], pts[i + 1, 1]
        x0 = int(max(0, math.floor((min(ax, bx) - radius + g.half) / g.step)))
        x1 = int(min(g.n - 1, math.ceil((max(ax, bx) + radius + g.half) / g.step)))
        z0 = int(max(0, math.floor((min(az, bz) - radius + g.half) / g.step)))
        z1 = int(min(g.n - 1, math.ceil((max(az, bz) + radius + g.half) / g.step)))
        if x1 < x0 or z1 < z0:
            continue
        sz, sx = slice(z0, z1 + 1), slice(x0, x1 + 1)
        X, Z = g.X[sz, sx], g.Z[sz, sx]
        dx, dz = bx - ax, bz - az
        l2 = max(dx * dx + dz * dz, 1e-9)
        t = np.clip(((X - ax) * dx + (Z - az) * dz) / l2, 0.0, 1.0)
        d = np.hypot(X - ax - t * dx, Z - az - t * dz)
        fn(sz, sx, d, t, i)


def lake_r(lk, X, Z):
    """Rayon normalisé de la super-ellipse du lac (1 = rive théorique), bord irrégulier."""
    a = math.radians(lk["angle"])
    u = (X - lk["c"][0]) * math.cos(a) + (Z - lk["c"][1]) * math.sin(a)
    v = -(X - lk["c"][0]) * math.sin(a) + (Z - lk["c"][1]) * math.cos(a)
    r = (np.abs(u / lk["a"]) ** 2.4 + np.abs(v / lk["b"]) ** 2.4) ** (1 / 2.4)
    return r * (1.0 + 0.13 * L.fbm(X / 420.0, Z / 420.0, 3, seed=D.SEED + 40))


def build(step=D.STEP, verbose=True):
    """Construit le relief naturel. Renvoie (grille, H, infos) ; infos : 'up' = hauteur au-dessus du fond de vallée,
    'rivers' = lits de rivières [(x, z, altitude de l'eau, largeur)]."""
    g = L.Grid(D.HALF, step)
    coarse = L.Grid(D.HALF, 16.0)
    Xc, Zc = coarse.X, coarse.Z
    Hc = np.full(Xc.shape, 1e9)
    Up = np.full(Xc.shape, 1e9)
    lines = {}
    for vi, v in enumerate(D.VALLEYS):
        line = valley_line(v)
        lines[v["id"]] = line
        pd, ph = profile_table(v["profile"])
        # Éperons et ravines : le versant avance ou recule (bruit du plan, continu partout)
        spur = L.fbm(Xc / 520.0, Zc / 520.0, 3, seed=D.SEED + 20 + vi) * 0.42
        Hv = np.full(Xc.shape, 1e9)
        Pv = np.full(Xc.shape, 1e9)
        # Minimum sur les segments de la ligne de fond : continu, sans saut au changement de segment le plus proche
        for i in range(len(line) - 1):
            ax, az, ea, wa = line[i]
            bx, bz, eb, wb = line[i + 1]
            dx, dz = bx - ax, bz - az
            tu = ((Xc - ax) * dx + (Zc - az) * dz) / max(dx * dx + dz * dz, 1e-9)
            t = np.clip(tu, 0.0, 1.0)
            d = np.hypot(Xc - ax - t * dx, Zc - az - t * dz)
            w = wa + t * (wb - wa)
            dd = np.maximum(0.0, d - w) * (1.0 + spur)
            P = np.interp(dd, pd, ph) + np.minimum(d, w) ** 2 * (1.5 / np.maximum(w, 1.0) ** 2)
            # Au-delà de son bout le plus haut, le fond du tronçon continue de monter : sans cela chaque tronçon
            # creuse un palier dans le suivant et les vallons raides deviennent des escaliers (une marche tous les
            # 60 m). Vers l'aval, pas de prolongement : il creuserait un trou au pied des raidillons.
            te = np.clip(tu, 0.0, 3.0) if eb >= ea else np.clip(tu, -2.0, 1.0)
            Hs = ea + te * (eb - ea) + P
            m = Hs < Hv
            Hv = np.where(m, Hs, Hv)
            Pv = np.where(m, P, Pv)
        Up = np.where(Hv < Hc, Pv, Up)
        Hc = L.smin(Hc, Hv, 22.0)
    # Sommets : plafond irrégulier
    cap = D.PEAK_BASE + D.PEAK_VAR * L.fbm(Xc / 1300.0, Zc / 1300.0, 3, seed=D.SEED + 60)         + 260.0 * (L.ridged(Xc / 800.0, Zc / 800.0, 4, seed=D.SEED + 61) - 0.5)
    Hc = L.smin(Hc, cap, 70.0)
    # Cirque de bordure (carré arrondi, bord irrégulier) : ferme les vallées qui sortent de la carte
    e = D.HALF - (Xc ** 4 + Zc ** 4) ** 0.25 + 240.0 * L.fbm(Xc / 800.0, Zc / 800.0, 3, seed=D.SEED + 62)
    f = 1.0 - L.smoothstep(0.0, D.RIM_WIDTH, e)
    rim = D.RIM_HEIGHT * f ** 1.5 + 0.5 * np.minimum(Hc, 600.0)
    Hc = Hc + (L.smax(Hc, rim, 80.0) - Hc) * L.smoothstep(0.0, 0.2, f)

    shape = (g.n, g.n)
    H = L.upsample(Hc, shape) if g.n != coarse.n else Hc.copy()
    up = L.upsample(np.minimum(Up, 3000.0), shape) if g.n != coarse.n else np.minimum(Up, 3000.0)
    if verbose:
        print("macro : %.0f..%.0f m" % (H.min(), H.max()))

    # Détail : presque rien au fond des vallées, vallonné sur les alpages, déchiqueté en altitude
    X, Z = g.X, g.Z
    amp = 1.2 + 11.0 * L.smoothstep(20.0, 260.0, up) + 22.0 * L.smoothstep(380.0, 700.0, up)
    H = H + amp * L.fbm(X / 240.0, Z / 240.0, 4, seed=D.SEED + 70)
    H = H + 70.0 * L.smoothstep(400.0, 760.0, up) * (L.ridged(X / 360.0, Z / 360.0, 4, seed=D.SEED + 71) - 0.55)

    # Cônes de déjection : le fond de vallée se relève en éventail au débouché des vallons
    for (fx, fz, fh, fr, fs) in getattr(D, "FANS", ()):
        sz, sx = g.window(fx - fr, fz - fr, fx + fr, fz + fr)
        d = np.hypot(X[sz, sx] - fx, Z[sz, sx] - fz)
        cone = fh - fs * d
        w = 1.0 - L.smoothstep(fr * 0.55, fr, d)
        cur = H[sz, sx]
        H[sz, sx] = np.where(cone > cur, cur + (cone - cur) * w, cur)

    # Replats
    for (px, pz, pr, blend, ph) in D.PADS:
        d = np.hypot(X - px, Z - pz)
        w = 1.0 - L.smoothstep(pr, pr + blend, d)
        if ph is None:
            ph = float(H[d < pr].mean())
        H = H * (1 - w) + ph * w

    # Cuvettes des lacs (les retenues de barrage n'en ont pas : la vallée est simplement noyée)
    for lk in D.LAKES:
        if lk["depth"] <= 0:
            continue
        r0 = max(lk["a"], lk["b"]) * 1.8
        sz, sx = g.window(lk["c"][0] - r0, lk["c"][1] - r0, lk["c"][0] + r0, lk["c"][1] + r0)
        r = lake_r(lk, X[sz, sx], Z[sz, sx])
        bowl = lk["level"] + np.where(r < 1.0, -lk["depth"] * (1.0 - r ** 2) - 0.8, 70.0 * (r - 1.0) - 0.8)
        w = 1.0 - L.smoothstep(1.05, 1.5, r)
        cur = H[sz, sx]
        H[sz, sx] = cur * (1 - w) + L.smin(cur, bowl, 4.0) * w

    # Lits de rivières : chenal creusé le long des lignes de fond
    rivers = []
    for v in D.VALLEYS:
        width = v.get("river", 0.0)
        if width <= 0:
            continue
        line = L.catmull(v["pts"], 12.0)
        i0 = v.get("river_from", 0)
        i1 = v.get("river_to", len(v["pts"]) - 1)
        # indices de la ligne fine correspondant aux points de contrôle i0..i1
        s_ctrl = [L.point_polyline(p[:2], line) for p in (v["pts"][i0], v["pts"][i1])]
        a, b = s_ctrl[0][1], s_ctrl[1][1] + 1
        seg = line[a:b + 1]
        depth = 1.6 + width * 0.12
        # fond du lit : sous le sol réel le long de la ligne (lissé), jamais au-dessus du fond théorique de la vallée
        ground = L.gauss1d(np.asarray(g.sample(H, seg[:, 0], seg[:, 1]), dtype=np.float64), 4.0)
        bed = np.minimum(seg[:, 2], ground) - depth

        # Chaque point prend le fond du tronçon le plus proche (et non le plus bas de tous : le bout de chaque
        # tronçon creusait une marche dans le suivant), puis les berges sont calculées une seule fois
        reach = width * 0.5 + 5.0 + width * 0.4
        best = np.full(H.shape, 1e9)
        floor = np.zeros(H.shape)

        def nearest(sz, sx, d, t, i, bed=bed):
            m = d < best[sz, sx]
            best[sz, sx] = np.where(m, d, best[sz, sx])
            floor[sz, sx] = np.where(m, bed[i] + t * (bed[i + 1] - bed[i]), floor[sz, sx])
        stamp_polyline(g, seg, reach + 8.0, nearest)
        f = 1.0 - L.smoothstep(width * 0.5, reach, best)
        H = np.where(f > 0, np.minimum(H, H * (1 - f) + floor * f), H)
        water = L.gauss1d(bed + depth * 0.5, 1.5)
        rivers.append(dict(id=v["id"], pts=np.column_stack([seg[:, 0], seg[:, 1], water, np.full(len(seg), width)])))
    if verbose:
        print("relief : %.0f..%.0f m" % (H.min(), H.max()))
    return g, H, dict(up=up, rivers=rivers, lines=lines)


def flood(g, H, lk, blocked=None):
    """Étendue réelle d'un plan d'eau : remplissage depuis le point de départ, sous le niveau (masque de grille)."""
    r = max(lk["a"], lk["b"]) * 1.7
    cx, cz = lk["c"]
    x0 = int(max(0, (cx - r + g.half) / g.step))
    x1 = int(min(g.n - 1, (cx + r + g.half) / g.step))
    z0 = int(max(0, (cz - r + g.half) / g.step))
    z1 = int(min(g.n - 1, (cz + r + g.half) / g.step))
    sub = H[z0:z1 + 1, x0:x1 + 1] < lk["level"]
    if blocked is None:
        blocked = np.zeros(H.shape, dtype=bool)
    for (pa, pb) in lk.get("stops", []):
        def block(sz_, sx_, d, t, i):
            blocked[sz_, sx_] |= d < g.step * 1.5
        stamp_polyline(g, np.array([pa, pb], dtype=np.float64), g.step * 3, block)
    sub &= ~blocked[z0:z1 + 1, x0:x1 + 1]
    fill = np.zeros_like(sub)
    sx = int(round((lk["seed"][0] + g.half) / g.step)) - x0
    sz = int(round((lk["seed"][1] + g.half) / g.step)) - z0
    fill[sz, sx] = True
    while True:
        grown = fill.copy()
        grown[1:, :] |= fill[:-1, :]
        grown[:-1, :] |= fill[1:, :]
        grown[:, 1:] |= fill[:, :-1]
        grown[:, :-1] |= fill[:, 1:]
        grown &= sub
        grown |= fill
        if (grown == fill).all():
            break
        fill = grown
    out = np.zeros(H.shape, dtype=bool)
    out[z0:z1 + 1, x0:x1 + 1] = fill
    return out


def dam_line(g, H):
    """Crête du barrage : perpendiculaire à la vallée au site choisi, prolongée jusqu'aux versants (au-dessus de la
    crête). Renvoie (a, b) en xz."""
    v = next(v for v in D.VALLEYS if v["id"] == "sud")
    line = L.catmull(v["pts"], 12.0)
    site = D.DAM["site"]
    _, i, _ = L.point_polyline(site, line)
    t = line[min(i + 3, len(line) - 1), :2] - line[max(i - 3, 0), :2]
    t /= np.linalg.norm(t)
    nrm = np.array([-t[1], t[0]])
    ends = []
    for sgn in (-1, 1):
        d = 0.0
        while d < 500 and float(g.sample(H, site[0] + nrm[0] * sgn * d, site[1] + nrm[1] * sgn * d)) < D.DAM["crest"] + 1.0:
            d += 2.0
        d += 6.0
        ends.append((site[0] + nrm[0] * sgn * d, site[1] + nrm[1] * sgn * d))
    return ends[0], ends[1]


def cached(step=D.STEP, path="out/terrain_cache.npz"):
    """Relief naturel mis en cache (recalculé si map_def ou map_terrain ont changé)."""
    import hashlib, os, pickle
    here = os.path.dirname(os.path.abspath(__file__))
    key = hashlib.md5()
    src = open(os.path.join(here, "map_def.py"), "rb").read()
    key.update(src.split(b"# ===== ROUTES")[0])          # seule la partie relief compte
    key.update(open(os.path.join(here, "map_terrain.py"), "rb").read())
    key.update(str(step).encode())
    full = os.path.join(here, path.replace(".npz", "_%g.pkl" % step))
    if os.path.exists(full):
        k, data = pickle.load(open(full, "rb"))
        if k == key.hexdigest():
            g = L.Grid(D.HALF, step)
            return g, data[0], data[1]
    g, H, info = build(step)
    pickle.dump((key.hexdigest(), (H, info)), open(full, "wb"))
    return g, H, info
