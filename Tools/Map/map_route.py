"""Tracé automatique des routes sur le relief, à la manière d'un projeteur de routes de montagne.

Recherche du meilleur tracé (A* « hybride ») : la route avance par tronçons de 14 m, droits ou en courbe de rayon
borné, et peut faire demi-tour par un lacet. Chaque tronçon est évalué sur le relief réel :
- pente en long : la route vise environ 85 % de la pente maximale de son type ; au-delà, le coût grimpe vite ;
- pente en travers (versant que la route coupe en écharpe) : déblais et remblais, donc coûteux ; falaises interdites ;
- rivières : franchies vite (donc presque d'équerre, sur un pont) ; on évite d'en longer le lit de trop près ;
- lacs, routes déjà tracées : interdits (sauf aux abords des extrémités) ;
- courbes : un peu plus chères que la ligne droite, changements de courbure brusques pénalisés ;
- lacets : chers, et d'autant plus que le site est raide (plate-forme à terrasser) : ils se placent sur les replats
  et les croupes, comme dans la réalité ;
- un léger bruit à grande échelle donne aux routes de plaine leurs inflexions (champs, haies, bosquets).
Le tracé obtenu est une suite d'arcs et de droites, ajoutée telle quelle aux points de contrôle d'un map_roads.Path.
"""
import heapq
import math
import time
import numpy as np
import map_lib as L

# Réglages par type de route. gmax : pente visée au plus (le profil en long final en garde une marge) ; rmin : rayon de
# courbure minimal hors lacets ; hair : rayons des lacets possibles ; hair_cost : coût d'un lacet (m équivalents) ;
# cross : poids de la pente en travers ; near_river : coût au mètre aux abords d'une rivière ; noise : inflexions
KIND = {
    # route nationale / départementale : jusqu'à 7-8 %, courbes de 60 m et plus, inflexions à deux échelles
    "main": dict(gmax=0.072, rmin=60.0, hair=(), hair_cost=0.0, cross=7.0, near_river=0.45, noise=0.30, wiggle=0.22),
    "mountain": dict(gmax=0.085, rmin=32.0, hair=(11.0, 15.0), hair_cost=80.0, cross=5.0, near_river=0.25, noise=0.15),
    "lane": dict(gmax=0.095, rmin=24.0, hair=(9.5, 13.0), hair_cost=50.0, cross=4.0, near_river=0.2, noise=0.2),
    "track": dict(gmax=0.105, rmin=18.0, hair=(8.5, 11.0), hair_cost=32.0, cross=3.0, near_river=0.1, noise=0.25),
}
STEP = 14.0          # longueur d'un tronçon élémentaire (m)
CELL = 6.0           # finesse de l'ensemble fermé (m)
NHEAD = 36           # finesse des caps (10°)
A_GRADE = 1.5        # coût de la pente : 1 + A r² par mètre (r = pente / gmax) : optimum vers r = 0,82
K_OVER = 14.0        # au-delà de gmax
R_LIM = 2.0          # pente interdite au-delà de 2 gmax ; au-delà de 1,3 gmax, réservé aux courts passages
R_SOFT, K_HARD = 1.3, 60.0
RIVER_IN = 9.0       # coût au mètre dans le lit (pont)
HAIR_SLACK = 4.5     # dénivelé (m) qu'un lacet peut rattraper au-delà de sa pente propre (terrassement)
TWO_PI = 2.0 * math.pi


class RouteError(RuntimeError):
    pass


def _local_distance(x0, z0, step, nx, nz, polylines, radius):
    """Distance (plafonnée à radius) de chaque nœud d'une grille locale aux polylignes [(pts (n, 2), demi-largeur)],
    moins leur demi-largeur."""
    d = np.full((nz, nx), radius, dtype=np.float64)
    X = x0 + np.arange(nx) * step
    Z = z0 + np.arange(nz) * step
    for pts, hw in polylines:
        P = np.asarray(pts, dtype=np.float64)
        if len(P) < 2:
            continue
        r = radius + hw
        for i in range(len(P) - 1):
            ax, az = P[i]
            bx, bz = P[i + 1]
            i0 = int(max(0, math.floor((min(ax, bx) - r - x0) / step)))
            i1 = int(min(nx - 1, math.ceil((max(ax, bx) + r - x0) / step)))
            j0 = int(max(0, math.floor((min(az, bz) - r - z0) / step)))
            j1 = int(min(nz - 1, math.ceil((max(az, bz) + r - z0) / step)))
            if i1 < i0 or j1 < j0:
                continue
            gx, gz = np.meshgrid(X[i0:i1 + 1], Z[j0:j1 + 1])
            dx, dz = bx - ax, bz - az
            l2 = max(dx * dx + dz * dz, 1e-9)
            t = np.clip(((gx - ax) * dx + (gz - az) * dz) / l2, 0.0, 1.0)
            dd = np.hypot(gx - ax - t * dx, gz - az - t * dz) - hw
            d[j0:j1 + 1, i0:i1 + 1] = np.minimum(d[j0:j1 + 1, i0:i1 + 1], dd)
    return d


class Router:
    """Relief lissé, pentes, plans d'eau, rivières et bruit, partagés par tous les tracés d'un même bake."""

    def __init__(self, g, H, water, rivers, seed=29, play=2650.0):
        t = time.time()
        self.g = g
        self.H = L.blur2d(H, 8.0 / g.step)                 # le relief vu à l'échelle d'une chaussée
        gz, gx = np.gradient(self.H, g.step)
        self.gx, self.gz = gx, gz
        self.water = np.asarray(water) > 0
        self.rivers = [(np.asarray(rv["pts"])[:, :2], float(rv["pts"][0][3]) * 0.5) for rv in rivers]
        self.noise = (L.fbm(g.X / 230.0, g.Z / 230.0, 3, seed=seed) + 1.0) * 0.5
        self.noise2 = (L.fbm(g.X / 95.0, g.Z / 95.0, 2, seed=seed + 7) + 1.0) * 0.5
        self.play = play
        self.cache = {}
        self.stats = []
        # Tracés déjà calculés (même relief, mêmes routes voisines, mêmes réglages) : relus au lieu d'être recherchés
        import hashlib, os, pickle
        self.digest = hashlib.md5(self.H[::8, ::8].astype(np.float32).tobytes()).hexdigest()
        self.store = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "route_cache.pkl")
        self.saved = {}
        if os.path.exists(self.store):
            try:
                with open(self.store, "rb") as f:
                    self.saved = pickle.load(f)
            except Exception:
                self.saved = {}
        print("routeur prêt (%.1f s, %d tracés en cache)" % (time.time() - t, len(self.saved)))

    def _save(self):
        import pickle
        with open(self.store, "wb") as f:
            pickle.dump(self.saved, f)

    def site(self, x, z, r=150.0, hmin=-1e9, hmax=1e9, smooth=30.0):
        """Replat le plus doux à moins de r mètres de (x, z), entre hmin et hmax (hameau, carrefour)."""
        g = self.g
        sz, sx = g.window(x - r, z - r, x + r, z + r)
        k = max(1, int(smooth / g.step))
        Hs = L.blur2d(self.H[sz, sx], k * 0.5)
        gz, gx = np.gradient(Hs, g.step)
        slope = np.hypot(gx, gz)
        X, Z = g.X[sz, sx], g.Z[sz, sx]
        d = np.hypot(X - x, Z - z)
        bad = (d > r) | (Hs < hmin) | (Hs > hmax) | self.water[sz, sx]
        score = np.where(bad, 1e9, slope + d / r * 0.04)
        j, i = np.unravel_index(int(np.argmin(score)), score.shape)
        return float(X[j, i]), float(Z[j, i])

    # ------------------------------------------------------------------ routes déjà tracées
    def occupied(self, net, exclude=None, tail=80.0):
        """Routes déjà définies dans le réseau (axes et demi-largeurs) ; pour la chaîne en cours, tout sauf ses
        derniers mètres."""
        import map_roads as R
        out = []
        for ch in net.chains:
            if len(ch.ctrl) < 2:
                continue
            key = (ch.id, len(ch.ctrl), tuple(ch.ctrl[-1]), len(ch.bridges))
            if key not in self.cache:
                # une boucle en cours de tracé n'est pas encore refermée : courbe ouverte
                P = L.catmull(np.array(ch.ctrl, dtype=np.float64), 2.5) if ch is exclude else ch.spline()
                keep = np.ones(len(P), dtype=bool)
                ctrl = np.array(ch.ctrl)
                for (c0, c1, kind) in ch.bridges:
                    # tronçon en pont ou en tunnel (entre deux points de contrôle) : franchissable
                    i0 = int(np.argmin(np.hypot(P[:, 0] - ctrl[c0, 0], P[:, 1] - ctrl[c0, 1])))
                    i1 = int(np.argmin(np.hypot(P[:, 0] - ctrl[c1, 0], P[:, 1] - ctrl[c1, 1])))
                    keep[min(i0, i1) + 6:max(i0, i1) - 6] = False
                pieces = []
                cur = []
                for k in range(0, len(P), 4):
                    if keep[k]:
                        cur.append(P[k, :2])
                    elif len(cur) > 1:
                        pieces.append(np.array(cur))
                        cur = []
                    else:
                        cur = []
                if len(cur) > 1:
                    pieces.append(np.array(cur))
                self.cache[key] = pieces
            hw = R.KINDS[ch.kind]["hw"] + (6.0 if ch.kind == "highway" else 0.0)
            for P in self.cache[key]:
                if ch is exclude:
                    s = L.arclen(P)
                    P = P[s < s[-1] - tail] if ch.ctrl and np.hypot(*(P[-1] - np.array(ch.ctrl[-1]))) < 30.0 else P
                    if len(P) < 2:
                        continue
                out.append((P, hw))
        return out

    # ------------------------------------------------------------------ recherche
    def route(self, start, heading, goal, kind="mountain", goal_heading=None, roads=(), margin=None, free=70.0,
              clear=7.0, budget=1500000, weight=1.15, label="", gmax=None, hair_cost=None, log=True, **over):
        """Tracé de start (x, z), cap heading (vecteur), à goal (x, z), arrivée au cap goal_heading (vecteur) si donné.
        Renvoie (points [(x, z)] après le départ, cap final)."""
        import map_roads as R
        import hashlib
        sig = hashlib.md5(repr((self.digest, [round(float(v), 1) for v in (*start, *heading, *goal)],
                                None if goal_heading is None else [round(float(v), 3) for v in goal_heading], kind,
                                margin, free, clear, weight, gmax, hair_cost, sorted(over.items()))).encode())
        for (pts_, hw_) in roads:
            sig.update(np.round(np.asarray(pts_), 0).astype(np.int32).tobytes())
            sig.update(str(hw_).encode())
        sig = sig.hexdigest()
        if sig in self.saved:
            pts, head, info = self.saved[sig]
            self.stats.append((label, kind) + info)
            if log:
                print("  tracé %-22s %-8s %6.0f m, %2d lacets (cache)" % (label, kind, info[0], info[1]))
            return list(pts), head
        P = dict(KIND[kind])
        if gmax is not None:
            P["gmax"] = gmax
        if hair_cost is not None:
            P["hair_cost"] = hair_cost
        P.update(over)
        g = self.g
        t0 = time.time()
        sx, sz = float(start[0]), float(start[1])
        ex, ez = float(goal[0]), float(goal[1])
        dist = math.hypot(ex - sx, ez - sz)
        if margin is None:
            margin = min(900.0, max(260.0, dist * 0.55))
        lim = self.play
        bx0, bz0 = max(min(sx, ex) - margin, -lim), max(min(sz, ez) - margin, -lim)
        bx1, bz1 = min(max(sx, ex) + margin, lim), min(max(sz, ez) + margin, lim)
        szl, sxl = g.window(bx0, bz0, bx1, bz1)
        Hl = self.H[szl, sxl]
        nz, nx = Hl.shape
        ox, oz = float(g.X[szl, sxl][0, 0]), float(g.Z[szl, sxl][0, 0])
        step = g.step
        hw = R.KINDS[kind]["hw"]
        # Coût au mètre indépendant du cap : bruit, abords et lit des rivières
        base = P["noise"] * self.noise[szl, sxl]
        if P.get("wiggle"):
            base = base + P["wiggle"] * self.noise2[szl, sxl]
        rd = _local_distance(ox, oz, step, nx, nz, self.rivers, 40.0)
        base = base + np.where(rd < 3.0, RIVER_IN, 0.0) + np.where((rd >= 3.0) & (rd < 28.0), P["near_river"], 0.0)
        # Interdits : lacs, routes existantes (hors abords des extrémités)
        blocked = self.water[szl, sxl].copy()
        if roads:
            cd = _local_distance(ox, oz, step, nx, nz, roads, 60.0)
            X, Z = np.meshgrid(ox + np.arange(nx) * step, oz + np.arange(nz) * step)
            near_end = (np.hypot(X - sx, Z - sz) < free) | (np.hypot(X - ex, Z - ez) < free)
            blocked |= (cd < hw + clear) & ~near_end
        Hf = Hl.ravel().tolist()
        Gx = self.gx[szl, sxl].ravel().tolist()
        Gz = self.gz[szl, sxl].ravel().tolist()
        Bf = base.ravel().tolist()
        Kf = blocked.ravel().tolist()
        # lit et berges d'une rivière : la route y passe en pont, à la hauteur de la berge qu'elle quitte
        Rf = (rd < 7.0).ravel().tolist()
        inv = 1.0 / step

        def height(x, z):
            fx = (x - ox) * inv
            fz = (z - oz) * inv
            i = int(fx)
            j = int(fz)
            if fx < 0 or fz < 0 or i >= nx - 1 or j >= nz - 1:
                return None
            u = fx - i
            v = fz - j
            k = j * nx + i
            return (Hf[k] * (1 - u) + Hf[k + 1] * u) * (1 - v) + (Hf[k + nx] * (1 - u) + Hf[k + nx + 1] * u) * v

        def cell(x, z):
            i = int((x - ox) * inv + 0.5)
            j = int((z - oz) * inv + 0.5)
            if i < 0 or j < 0 or i >= nx or j >= nz:
                return -1
            return j * nx + i

        gmax = P["gmax"]
        cw = P["cross"]
        rmin = P["rmin"]
        # Tronçons : courbures 0, ±1/(2 rmin), ±1/rmin ; coût de courbure
        curv = [(0, 0.0, 0.0), (1, 0.5 / rmin, 0.8), (-1, -0.5 / rmin, 0.8), (2, 1.0 / rmin, 3.0), (-2, -1.0 / rmin, 3.0)]
        prims = []
        for (ci, k, kc) in curv:
            prims.append((ci, k, kc))
        hairs = []
        for R_ in P["hair"]:
            for side in (1, -1):
                hairs.append((R_, side))
        kh = 2.0 * math.sqrt(A_GRADE)
        h_goal = height(ex, ez)
        h_start = height(sx, sz)
        if h_goal is None or h_start is None:
            raise RouteError("%s : départ ou arrivée hors de la zone" % label)
        if Kf[cell(ex, ez)]:
            raise RouteError("%s : arrivée sur une zone interdite" % label)

        def heur(x, z, h):
            d = math.hypot(ex - x, ez - z)
            return max(d, kh * abs(h_goal - h) / gmax)

        th0 = math.atan2(heading[1], heading[0])
        if goal_heading is not None:
            thg = math.atan2(goal_heading[1], goal_heading[0])
        tol = STEP * 0.85
        # Nœuds : listes parallèles
        NX, NZ, NT, NG, NH, NP, NM, NS = [sx], [sz], [th0], [0.0], [h_start], [-1], [0], [9]
        best = {}
        openl = [(heur(sx, sz, h_start) * weight, 0)]
        found = -1
        expanded = 0
        cell_k = 1.0 / CELL
        hbin = NHEAD / TWO_PI
        closest = (1e18, 0)
        while openl:
            f, n = heapq.heappop(openl)
            x, z, th, gc, h = NX[n], NZ[n], NT[n], NG[n], NH[n]
            key = (int(x * cell_k), int(z * cell_k), int(round(th * hbin)) % NHEAD)
            if best.get(key, 1e18) < gc - 1e-6:
                continue
            dgoal = math.hypot(ex - x, ez - z)
            if dgoal < closest[0]:
                closest = (dgoal, n)
            if dgoal < tol:
                if goal_heading is None or abs((th - thg + math.pi) % TWO_PI - math.pi) < 0.7:
                    found = n
                    break
            expanded += 1
            if expanded > budget:
                break
            prev_c = NM[n]
            since = NS[n]
            ch, sh = math.cos(th), math.sin(th)
            for (ci, k, kc) in prims:
                if k == 0.0:
                    x2, z2, t2 = x + ch * STEP, z + sh * STEP, th
                    xm, zm = x + ch * STEP * 0.5, z + sh * STEP * 0.5
                else:
                    t2 = th + k * STEP
                    x2 = x + (math.sin(t2) - sh) / k
                    z2 = z - (math.cos(t2) - ch) / k
                    tm = th + k * STEP * 0.5
                    xm = x + (math.sin(tm) - sh) / k
                    zm = z - (math.cos(tm) - ch) / k
                h2 = height(x2, z2)
                if h2 is None:
                    continue
                c2 = cell(x2, z2)
                cm = cell(xm, zm)
                if c2 < 0 or cm < 0 or Kf[c2] or Kf[cm]:
                    continue
                tmid = th + k * STEP * 0.5
                if Rf[c2] or Rf[cm]:
                    h2 = h                      # pont : tablier à niveau
                    r = 0.0
                    cross = 0.0
                else:
                    r = abs(h2 - h) / (STEP * gmax)
                    if r > R_LIM:
                        continue
                    cross = abs(-Gx[cm] * math.sin(tmid) + Gz[cm] * math.cos(tmid))
                    if cross > 1.05:
                        continue
                cost = STEP * (1.0 + A_GRADE * r * r + Bf[cm] + cw * max(0.0, cross - 0.12) ** 2)
                if r > 1.0:
                    cost += STEP * K_OVER * (r - 1.0) ** 2
                    if r > R_SOFT:
                        cost += STEP * K_HARD * (r - R_SOFT) ** 2
                cost += kc * STEP / 14.0 + 1.2 * abs(ci - prev_c) if prev_c < 10 else kc
                g2 = gc + cost
                key2 = (int(x2 * cell_k), int(z2 * cell_k), int(round(t2 * hbin)) % NHEAD)
                if best.get(key2, 1e18) <= g2:
                    continue
                best[key2] = g2
                NX.append(x2); NZ.append(z2); NT.append(t2); NG.append(g2); NH.append(h2); NP.append(n)
                NM.append(ci); NS.append(since + 1)
                heapq.heappush(openl, (g2 + weight * heur(x2, z2, h2), len(NX) - 1))
            if since < 3:
                continue
            for (R_, side) in hairs:
                # Demi-tour : demi-cercle de rayon R_ à gauche (side = 1) ou à droite
                nxl, nzl = -sh * side, ch * side
                x2, z2 = x + nxl * 2 * R_, z + nzl * 2 * R_
                xa, za = x + nxl * R_ + ch * R_, z + nzl * R_ + sh * R_
                h2 = height(x2, z2)
                if h2 is None:
                    continue
                c2 = cell(x2, z2)
                ca = cell(xa, za)
                if c2 < 0 or ca < 0 or Kf[c2] or Kf[ca] or Rf[c2] or Rf[ca]:
                    continue
                arc = math.pi * R_
                dh = abs(h2 - h)
                if dh > gmax * 1.25 * arc + HAIR_SLACK:
                    continue
                site = math.hypot(Gx[ca], Gz[ca])
                if site > 0.85:
                    continue
                excess = max(0.0, dh - gmax * arc)
                cost = arc * (1.0 + Bf[ca]) + P["hair_cost"] * (1.0 + (site / 0.32) ** 2) + excess / gmax
                g2 = gc + cost
                t2 = th + math.pi
                key2 = (int(x2 * cell_k), int(z2 * cell_k), int(round(t2 * hbin)) % NHEAD)
                if best.get(key2, 1e18) <= g2:
                    continue
                best[key2] = g2
                NX.append(x2); NZ.append(z2); NT.append(t2); NG.append(g2); NH.append(h2); NP.append(n)
                NM.append(10 + (1 if side > 0 else 2) + (2 if R_ != P["hair"][0] else 0)); NS.append(0)
                heapq.heappush(openl, (g2 + weight * heur(x2, z2, h2), len(NX) - 1))
        dt = time.time() - t0
        if found < 0:
            n = closest[1]
            raise RouteError("%s : pas de tracé (%d nœuds développés, %.0f s) ; au plus près à %.0f m, en (%.0f, %.0f) h=%.0f"
                             % (label, expanded, dt, closest[0], NX[n], NZ[n], NH[n]))
        # Reconstruction : géométrie exacte de chaque tronçon
        chain = []
        n = found
        while n > 0:
            chain.append(n)
            n = NP[n]
        chain.reverse()
        pts = []
        hairpins = 0
        for n in chain:
            p = NP[n]
            x, z, th = NX[p], NZ[p], NT[p]
            m = NM[n]
            if m >= 10:
                hairpins += 1
                side = 1 if (m - 10) in (1, 3) else -1
                R_ = P["hair"][0] if (m - 10) < 3 else P["hair"][1]
                cx, cz = x - math.sin(th) * side * R_, z + math.cos(th) * side * R_
                a0 = math.atan2(z - cz, x - cx)
                for q in range(1, 7):
                    a = a0 + side * math.pi * q / 6
                    pts.append((cx + math.cos(a) * R_, cz + math.sin(a) * R_))
            else:
                k = [c for c in curv if c[0] == m][0][1]
                for s in (STEP * 0.5, STEP):
                    if k == 0.0:
                        pts.append((x + math.cos(th) * s, z + math.sin(th) * s))
                    else:
                        t2 = th + k * s
                        pts.append((x + (math.sin(t2) - math.sin(th)) / k, z - (math.cos(t2) - math.cos(th)) / k))
        pts.append((ex, ez))
        length = sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))
        length += math.hypot(pts[0][0] - sx, pts[0][1] - sz)
        self.stats.append((label, kind, length, hairpins, expanded, dt))
        if log:
            print("  tracé %-22s %-8s %6.0f m, %2d lacets, h %4.0f -> %4.0f, %7d nœuds, %5.1f s"
                  % (label, kind, length, hairpins, h_start, h_goal, expanded, dt))
        th = NT[found]
        head = (math.cos(th), math.sin(th))
        self.saved[sig] = (pts, head, (length, hairpins, expanded, dt))
        self._save()
        return pts, head
