"""Réseau routier : tracés (points de passage, lignes de pente tracées sur le relief, épingles), profils en long
(lissage + pente limitée), carrefours, ponts, et terrassement du relief le long des routes."""
import math
import numpy as np
import map_lib as L
from map_terrain import stamp_polyline

# Types de route : demi-largeur de chaussée, décalage de l'axe des voies, pente maximale, vitesse du trafic (m/s),
# trottoir (m, plus utilisé), terre battue, voies par sens, terre-plein, sens unique
def _kind(hw, lane, grade, speed, dirt=0, lanes=1, median=0.0, oneway=0):
    return dict(hw=hw, lane=lane, grade=grade, speed=speed, sidewalk=0.0, dirt=dirt, lanes=lanes, median=median,
                oneway=oneway)


KINDS = {
    "main": _kind(3.6, 1.7, 0.085, 22.0),            # route nationale / départementale, 7,2 m
    "mountain": _kind(3.4, 1.6, 0.10, 15.0),         # route de col
    "lane": _kind(2.6, 1.2, 0.12, 11.0),             # chemin goudronné
    "track": _kind(2.4, 1.05, 0.14, 8.0, dirt=1),    # chemin de terre
    # Autoroute 2 × 3 voies de 3,5 m : terre-plein (2,4 m de chaque côté de l'axe, glissière béton), bande d'arrêt
    # de 2,3 m. « lane » = axe de la voie de gauche ; les suivantes sont décalées de 3,5 m (RoadNetwork.LaneWidth).
    "highway": _kind(15.2, 4.15, 0.06, 31.0, lanes=3, median=2.4),
    # Bretelle d'échangeur : une voie à sens unique (parcourue de a vers b), on roule sur l'axe
    "ramp": _kind(2.75, 0.0, 0.07, 17.0, oneway=1),
}
# Tunnels : gabarit intérieur au-dessus de la chaussée (m) et couverture minimale pour ouvrir une tête de tunnel.
# Mêmes cotes que RoadNetwork (tube : demi-largeur hw + 1, coque extérieure + 6 m).
TUNNEL_H = {"highway": 6.4}
TUNNEL_H_DEFAULT = 5.0


def tunnel_height(kind):
    return TUNNEL_H.get(kind, TUNNEL_H_DEFAULT)


# Passage inférieur (Path.bridge(..., "underpass")) : tunnel court sous le remblai d'une autre route, gardé tel que
# tracé (pas de couverture minimale de relief). Sa coque n'a qu'une dalle de cette épaisseur au-dessus du gabarit
# (RoadNetwork.TunnelRoof pour un tunnel sous la montagne) : la route du dessus passe à gabarit + dalle + 0,3 m.
UNDERPASS_ROOF = 1.0


def underpass(e, s0, s1):
    """La travée [s0, s1] de l'arête est-elle un passage inférieur ?"""
    return any(abs(s0 - a) < 0.5 and abs(s1 - b) < 0.5 for (a, b) in e.get("underpass", ()))


DS = 2.5                      # pas des profils (m)
UNDER = 0.3                   # le relief passe sous la chaussée
CUT_FLAT, CUT_SLOPE = 4.6, 1.05      # plat > accotement + pas de grille : le sol ne remonte pas dessus
FILL_FLAT, FILL_SLOPE = 1.4, 0.6


class Net:
    """Réseau en construction : nœuds nommés (carrefours, extrémités) et chaînes de points."""

    def __init__(self, g, H):
        self.g = g
        self.H = H
        self.Hs = L.blur2d(H, 10.0 / g.step)        # relief lissé pour tracer les lignes de pente
        self.nodes = {}
        self.chains = []
        self.lines = {}
        self.fixed = {}              # nœuds à hauteur imposée
        self.merges = set()          # nœuds posés sur l'axe de l'autoroute, où s'attache une bretelle
        self.crossings = set()       # coupes de chaîne (Path.split) qui sont de vrais carrefours

    def node(self, name, x, z):
        self.nodes[name] = (float(x), float(z))

    def h(self, x, z):
        return float(self.g.sample(self.H, x, z))

    def road(self, rid, kind, start, **kw):
        p = Path(self, rid, kind, start, **kw)
        self.chains.append(p)
        return p


class Path:
    """Chaîne : liste de points de contrôle, dont certains sont des nœuds du réseau."""

    def __init__(self, net, rid, kind, start, heading=None, **opts):
        self.net = net
        self.id = rid
        self.kind = kind
        self.opts = opts
        self.ctrl = []
        self.marks = {}
        self.bridges = []            # (indice de contrôle début, fin, type)
        self.splits = []             # nœuds posés sur la courbe hors points de contrôle : (nom, x, z)
        self.aux = []                # voies d'insertion / de sortie : (x, z départ pleine largeur, x, z pointe, côté, largeur)
        self._heading = None
        if isinstance(start, str):
            x, z = net.nodes[start]
            self.ctrl.append((x, z))
            self.marks[0] = start
        else:
            self.ctrl.append((float(start[0]), float(start[1])))
        if heading is not None:
            a = math.radians(heading)
            self._heading = (math.cos(a), math.sin(a))

    # --- état
    @property
    def pos(self):
        return self.ctrl[-1]

    def heading(self):
        if self._heading is not None:
            return self._heading
        if len(self.ctrl) < 2:
            return (1.0, 0.0)
        (ax, az), (bx, bz) = self.ctrl[-2], self.ctrl[-1]
        d = math.hypot(bx - ax, bz - az) or 1.0
        return ((bx - ax) / d, (bz - az) / d)

    def _add(self, x, z):
        self.ctrl.append((float(x), float(z)))
        self._heading = None

    # --- commandes
    def to(self, *pts):
        """Points de passage (x, z)."""
        for p in pts:
            self._add(p[0], p[1])
        return self

    def node(self, name):
        """Nœud du réseau : existant (on y va) ou nouveau (créé à la position courante)."""
        if name in self.net.nodes:
            x, z = self.net.nodes[name]
            if math.hypot(x - self.pos[0], z - self.pos[1]) > 0.5:
                self._add(x, z)
        else:
            self.net.nodes[name] = self.pos
        self.marks[len(self.ctrl) - 1] = name
        return self

    def mark(self, name):
        """Nomme le point courant comme extrémité d'arête sans déplacer le nœud (bretelle : son bout est décalé de
        l'axe de l'autoroute, où se trouve le nœud)."""
        self.marks[len(self.ctrl) - 1] = name
        return self

    def split(self, name, x, z):
        """Coupe la chaîne au point de sa courbe le plus proche de (x, z) : un nœud y est créé."""
        self.splits.append((name, float(x), float(z)))
        return self

    def spline(self):
        """Courbe de la chaîne telle qu'elle sera construite (points tous les DS mètres)."""
        return L.catmull(np.array(self.ctrl, dtype=np.float64), DS, closed=bool(self.opts.get("closed")))

    def route(self, goal, kind=None, goal_heading=None, **kw):
        """Tracé automatique jusqu'à goal = (x, z) ou nom de nœud, sur le relief (map_route : pente, versants,
        rivières, lacets). goal_heading : cap d'arrivée (vecteur), pour aborder un carrefour d'équerre."""
        net = self.net
        gx, gz = net.nodes[goal] if isinstance(goal, str) else goal
        roads = net.router.occupied(net, exclude=self)
        free_start = len(self.ctrl) < 2 and self._heading is None
        pts, head = net.router.route(self.pos, None if free_start else self.heading(), (gx, gz), kind or self.kind, goal_heading,
                                     roads=roads, label=kw.pop("label", self.id), **kw)
        for p in pts[:-1]:
            self._add(*p)
        if math.hypot(gx - self.pos[0], gz - self.pos[1]) > 0.5:
            self._add(gx, gz)
        self._heading = head
        return self

    def head(self, degrees):
        """Impose le cap (0 = est, 90 = nord) pour la commande suivante."""
        a = math.radians(degrees)
        self._heading = (math.cos(a), math.sin(a))
        return self

    def grade(self, gr, length=None, until_h=None, near=None, floor=None, rmin=30.0, step=6.0, log=False, aim=None):
        """Ligne de pente : avance en gardant la pente « gr » sur le relief (comme un tracé au compas sur les
        courbes de niveau), dans le sens le plus proche du cap courant. S'arrête après « length » mètres, à
        l'altitude « until_h », ou à moins de r mètres de « near » = (x, z, r). « aim » = point (ou fonction
        (x, z) → point) vers lequel se diriger : il départage les deux lignes de pente possibles à la place du cap."""
        net = self.net
        x, z = self.pos
        hx, hz = self.heading()
        travelled, since = 0.0, 0.0
        self.reached = False
        self.floored = False
        limit = length if length is not None else 4000.0
        h0 = float(net.g.sample(net.Hs, x, z))
        while travelled < limit:
            gx, gz = net.g.gradient(net.Hs, x, z, eps=8.0)
            gx, gz = float(gx), float(gz)
            m = math.hypot(gx, gz)
            rx, rz = hx, hz
            if aim is not None:
                ax, az = aim(x, z) if callable(aim) else aim
                dn = math.hypot(ax - x, az - z) or 1.0
                rx, rz = (ax - x) / dn, (az - z) / dn
            # choix entre les deux lignes de pente : le cap courant d'abord (pas d'aller-retour d'un pas au suivant),
            # la cible ensuite
            bx, bz = (rx + 0.35 * hx, rz + 0.35 * hz) if aim is not None else (hx, hz)
            if m > abs(gr) * 1.03 and m > 1e-4:
                ux, uz = gx / m, gz / m
                phi = math.acos(max(-1.0, min(1.0, gr / m)))
                best = None
                for sgn in (-1, 1):
                    c, s = math.cos(phi * sgn), math.sin(phi * sgn)
                    cx, cz = ux * c - uz * s, ux * s + uz * c
                    dot = cx * bx + cz * bz
                    if best is None or dot > best[0]:
                        best = (dot, cx, cz)
                cx, cz = best[1], best[2]
            elif m > 1e-4 and abs(gr) > 1e-4:
                # relief trop doux pour la pente voulue : on garde le cap, légèrement tiré vers la ligne de pente
                s = 1.0 if gr > 0 else -1.0
                cx, cz = rx + gx / m * s * 0.15, rz + gz / m * s * 0.15
                n = math.hypot(cx, cz)
                cx, cz = cx / n, cz / n
            else:
                cx, cz = rx, rz
            # rayon minimal : rotation limitée par pas
            ang = math.atan2(hx * cz - hz * cx, hx * cx + hz * cz)
            lim = step / rmin
            ang = max(-lim, min(lim, ang))
            c, s = math.cos(ang), math.sin(ang)
            hx, hz = hx * c - hz * s, hx * s + hz * c
            x, z = x + hx * step, z + hz * step
            travelled += step
            since += step
            if since >= 24.0:
                self._add(x, z)
                since = 0.0
            hh = float(net.g.sample(net.Hs, x, z))
            if until_h is not None and ((gr > 0 and hh >= until_h) or (gr < 0 and hh <= until_h)):
                self.reached = True
                break
            if near is not None and math.hypot(x - near[0], z - near[1]) < near[2]:
                self.reached = True
                break
            # torrent atteint : seulement là où le fond du vallon monte plus vite que la route (sinon elle le suit)
            if floor is not None and travelled > 80.0 and m > abs(gr) * 1.03 and L.point_polyline((x, z), net.lines[floor[0]])[0] < floor[1]:
                self.floored = True
                break
        if since > 14.0 or travelled < 24.0:
            self._add(x, z)
        elif since > 0.0:
            self.ctrl[-1] = (float(x), float(z))
        self._heading = (hx, hz)
        self.last_len = travelled
        if log:
            print("  %s grade %.3f : %.0f m, h %.0f -> %.0f, fin (%.0f, %.0f)" % (
                self.id, gr, travelled, h0, float(net.g.sample(net.Hs, x, z)), x, z))
        return self

    def hairpin(self, turn, R=15.0, angle=180.0):
        """Épingle : arc de rayon R, à gauche (turn = +1) ou à droite (-1)."""
        x, z = self.pos
        hx, hz = self.heading()
        lx, lz = -hz * turn, hx * turn            # vers le centre
        cx, cz = x + lx * R, z + lz * R
        a0 = math.atan2(z - cz, x - cx)
        n = max(3, int(angle / 30))
        for k in range(1, n + 1):
            a = a0 + turn * math.radians(angle) * k / n
            self._add(cx + math.cos(a) * R, cz + math.sin(a) * R)
        a = math.radians(angle) * turn
        c, s = math.cos(a), math.sin(a)
        self._heading = (hx * c - hz * s, hx * s + hz * c)
        return self

    def climb(self, gr, line, d_in=45.0, d_out=280.0, until_h=None, near=None, max_len=9000.0, R=15.0,
              min_leg=110.0, max_legs=16, log=False):
        """Monte (ou descend) une vallée en lacets : les traversées rebondissent entre le torrent (d_in) et le
        haut du versant (d_out), les épingles tournent vers l'amont. Là où le fond est trop plat pour la pente
        voulue, la route suit simplement l'axe de la vallée."""
        net = self.net
        pts = net.lines[line]
        total = 0.0
        legs = 0
        while total < max_len and legs < max_legs:
            legs += 1
            x, z = self.pos
            hx, hz = self.heading()
            travelled, since = 0.0, 0.0
            step, rmin = 6.0, 30.0
            done = False
            d_prev = L.point_polyline((x, z), pts)[0]
            while True:
                gx, gz = net.g.gradient(net.Hs, x, z, eps=8.0)
                gx, gz = float(gx), float(gz)
                m = math.hypot(gx, gz)
                if m > abs(gr) * 1.03:
                    ux, uz = gx / m, gz / m
                    phi = math.acos(max(-1.0, min(1.0, gr / m)))
                    best = None
                    for sgn in (-1, 1):
                        c, s_ = math.cos(phi * sgn), math.sin(phi * sgn)
                        cx, cz = ux * c - uz * s_, ux * s_ + uz * c
                        dot = cx * hx + cz * hz
                        if best is None or dot > best[0]:
                            best = (dot, cx, cz)
                    cx, cz = best[1], best[2]
                else:
                    # fond plat : on suit l'axe de la vallée, vers l'amont (ou l'aval en descente)
                    _, i, _ = L.point_polyline((x, z), pts)
                    tx, tz = pts[i + 1, 0] - pts[i, 0], pts[i + 1, 1] - pts[i, 1]
                    n = math.hypot(tx, tz) or 1.0
                    sgn = 1.0 if (pts[i + 1, 2] - pts[i, 2]) * gr >= 0 else -1.0
                    cx, cz = tx / n * sgn, tz / n * sgn
                ang = math.atan2(hx * cz - hz * cx, hx * cx + hz * cz)
                ang = max(-step / rmin, min(step / rmin, ang))
                c, s_ = math.cos(ang), math.sin(ang)
                hx, hz = hx * c - hz * s_, hx * s_ + hz * c
                x, z = x + hx * step, z + hz * step
                travelled += step
                since += step
                if since >= 24.0:
                    self._add(x, z)
                    since = 0.0
                hh = float(net.g.sample(net.Hs, x, z))
                if until_h is not None and ((gr > 0 and hh >= until_h) or (gr < 0 and hh <= until_h)):
                    done = True
                    break
                if near is not None and math.hypot(x - near[0], z - near[1]) < near[2]:
                    done = True
                    break
                d = L.point_polyline((x, z), pts)[0]
                # sens de la traversée par rapport à l'axe de la vallée : vers l'amont, on ne s'arrête qu'au torrent ;
                # vers l'aval, en haut du versant
                _, li, _ = L.point_polyline((x, z), pts)
                tx, tz = pts[li + 1, 0] - pts[li, 0], pts[li + 1, 1] - pts[li, 1]
                rising = (pts[li + 1, 2] - pts[li, 2]) * (1.0 if gr > 0 else -1.0) >= 0
                along = (hx * tx + hz * tz) * (1.0 if rising else -1.0)
                if travelled > min_leg and m > abs(gr):
                    if along > 0 and d < d_in + pts[li, 3] and d < d_prev:
                        break
                    if along <= 0 and d > d_out + pts[li, 3]:
                        break
                if total + travelled > max_len:
                    done = True
                    break
                d_prev = d
            if since > 14.0:
                self._add(x, z)
            elif since > 0.0:
                self.ctrl[-1] = (float(x), float(z))
            self._heading = (hx, hz)
            total += travelled
            if log:
                print("  %s lacet : %.0f m -> (%.0f, %.0f) h=%.0f d=%.0f" % (self.id, travelled, x, z, hh, L.point_polyline((x, z), pts)[0]))
            if done:
                break
            # épingle vers l'amont (pour monter) ou vers l'aval (pour descendre)
            gx, gz = net.g.gradient(net.Hs, x, z, eps=8.0)
            side = hx * float(gz) - hz * float(gx)
            turn = 1.0 if side > 0 else -1.0
            if gr < 0:
                turn = -turn
            self.hairpin(turn, R)
        return self

    def forward(self, dist):
        hx, hz = self.heading()
        keep = (hx, hz)
        self._add(self.pos[0] + hx * dist, self.pos[1] + hz * dist)
        self._heading = keep
        return self

    def bridge(self, back=1, kind="bridge"):
        """Marque le tronçon entre les « back » derniers points de contrôle comme pont (pas de terrassement), tunnel
        (kind = "tunnel" : calé ensuite là où le relief couvre le tube) ou passage inférieur (kind = "underpass" :
        tunnel gardé tel quel, sous le remblai d'une autre route ; voir UNDERPASS_ROOF)."""
        self.bridges.append((len(self.ctrl) - 1 - back, len(self.ctrl) - 1, kind))
        return self

    def here(self, label=""):
        print("  %s %s : (%.0f, %.0f) h=%.0f" % (self.id, label, self.pos[0], self.pos[1], self.net.h(*self.pos)))
        return self


# ----------------------------------------------------------------- arêtes et profils
def _target(g, H, e):
    """Profil visé : le relief sous l'axe, les ouvrages (ponts, tunnels) tendus entre leurs extrémités."""
    tgt = e["ground"].copy()
    # un enchaînement viaduc - tunnel - viaduc est tendu d'un bout à l'autre : entre deux ouvrages la route ne
    # remonte pas chercher le versant
    spans = sorted(e["spans"])
    groups = []
    if any(k in ("tunnel", "viaduct") for (_, _, k) in spans):
        for (s0, s1, kind) in spans:
            if groups and s0 - groups[-1][1] < 170.0:
                groups[-1][1] = max(groups[-1][1], s1)
            else:
                groups.append([s0, s1, kind])
    else:
        groups = spans
    for (s0, s1, kind) in groups:
        i0 = int(np.searchsorted(e["s"], s0))
        i1 = int(np.searchsorted(e["s"], s1))
        i0, i1 = max(i0 - 1, 0), min(i1 + 1, len(tgt) - 1)
        tgt[i0:i1 + 1] = np.linspace(tgt[i0], tgt[i1], i1 - i0 + 1)
    tgt = np.maximum(tgt, e["opts"].get("min_h", -1e9))
    # surélévations : (x, z, hauteur, rayon du palier, longueur de rampe) — passage au-dessus d'une autre route,
    # viaduc au-dessus de l'eau
    P = e["pts"]
    for (lx, lz, dh, flat, ramp) in e["opts"].get("lift", ()):
        dist = np.hypot(P[:, 0] - lx, P[:, 1] - lz)
        tgt = tgt + dh * (1.0 - L.smoothstep(flat, flat + ramp, dist))
    return tgt


def _refine_spans(g, H, e, ha, hb):
    """Ajuste les ouvrages au relief. Tunnel : il ne commence que là où le terrain couvre le tube sur toute sa
    largeur (avant, la route est en tranchée). Viaduc tracé à la main : il ne commence que là où le tablier est
    nettement au-dessus du sol (avant, la route est en remblai). Les bornes sont calées sur des points du profil ;
    à la fin un viaduc devient un pont ordinaire, et un pont s'arrête à la tête du tunnel qui le suit."""
    if not any(k in ("tunnel", "viaduct") for (_, _, k) in e["spans"]):
        return
    P, s = e["pts"], e["s"]
    t = L.tangents(P)
    left = np.stack([-t[:, 1], t[:, 0]], axis=1)
    win = e["hw"] + 1.0
    need = tunnel_height(e["kind"]) + 2.4
    low = e["ground"].copy()
    high = e["ground"].copy()
    for sgn in (-1.0, 1.0):
        side = g.sample(H, P[:, 0] + left[:, 0] * sgn * win, P[:, 1] + left[:, 1] * sgn * win)
        low = np.minimum(low, side)
        high = np.maximum(high, side)
    for _ in range(3):
        e["target"] = _target(g, H, e)
        prof = solve_profile(e, ha, hb, pin=False)
        spans = []
        for (s0, s1, kind) in e["spans"]:
            if kind not in ("tunnel", "viaduct") or underpass(e, s0, s1):
                spans.append([s0, s1, kind])
                continue
            # ce qui manque encore pour être un tunnel (couverture) ou un viaduc (hauteur libre)
            ok = (low - prof >= need) if kind == "tunnel" else (prof - high >= 3.0)
            i0 = int(np.searchsorted(s, s0))
            i1 = min(int(np.searchsorted(s, s1)), len(s) - 1)
            while i0 < i1 and not ok[i0]:
                i0 += 1
            while i1 > i0 and not ok[i1]:
                i1 -= 1
            if s[i1] - s[i0] > (40.0 if kind == "tunnel" else 12.0):
                spans.append([float(s[i0]), float(s[i1]), kind])
        e["spans"] = spans
    tunnels = [(s0, s1) for (s0, s1, k) in e["spans"] if k == "tunnel"]
    out = []
    for (s0, s1, kind) in e["spans"]:
        if kind == "tunnel":
            out.append([s0, s1, kind])
            continue
        pieces = [(s0, s1)]
        for (t0, t1) in tunnels:
            pieces = [q for (a, b) in pieces for q in ((a, min(b, t0)), (max(a, t1), b)) if q[1] - q[0] > 8.0]
        out.extend([a, b, kind] for (a, b) in pieces)
    e["spans"] = sorted(out)


def _cover_crossings(e, edges, reach=45.0, margin=6.0):
    """Un pont dont la culée tombe sur une route qui passe dessous est prolongé jusqu'au-delà de cette route :
    _refine_spans ne regarde que le relief, or une route à flanc de versant passe là où le tablier est encore
    près du sol. On prolonge le tablier sur toute l'emprise croisée (plus une marge), si elle est à moins de
    « reach » mètres du bout du pont."""
    spans = [sp for sp in e["spans"] if sp[2] != "tunnel"]
    if not spans:
        return
    P, s = e["pts"], e["s"]
    t = L.tangents(P)
    left = np.stack([-t[:, 1], t[:, 0]], axis=1)
    tunnels = [(s0, s1) for (s0, s1, k) in e["spans"] if k == "tunnel"]
    for o in edges:
        if o is e or o["chain"] == e["chain"] or {o["a"], o["b"]} & {e["a"], e["b"]}:
            continue
        Q = o["pts"]
        if (Q[:, 0].min() > P[:, 0].max() + 40 or Q[:, 0].max() < P[:, 0].min() - 40 or
                Q[:, 1].min() > P[:, 1].max() + 40 or Q[:, 1].max() < P[:, 1].min() - 40):
            continue
        # points de l'ouvrage dont la largeur recouvre la chaussée de l'autre route
        near = np.where(np.hypot(P[:, None, 0] - Q[None, ::2, 0], P[:, None, 1] - Q[None, ::2, 1]).min(axis=1)
                        < e["hw"] + o["hw"] + 8.0)[0]
        hit = []
        for i in near:
            for off in np.linspace(-e["hw"] - 1.0, e["hw"] + 1.0, 9):
                q = P[i, :2] + left[i] * off
                if np.hypot(Q[:, 0] - q[0], Q[:, 1] - q[1]).min() < o["hw"] + 2.5:
                    hit.append(i)
                    break
        if not hit:
            continue
        for grp in np.split(np.array(hit), np.where(np.diff(hit) > 6)[0] + 1):
            c0, c1 = s[grp[0]] - margin, s[grp[-1]] + margin
            for sp in spans:
                if c0 < sp[0] <= c1 + reach:
                    sp[0] = max(c0, 0.0, max([t1 for (_, t1) in tunnels if t1 <= sp[0]], default=0.0))
                if c1 > sp[1] >= c0 - reach:
                    sp[1] = min(c1, s[-1], min([t0 for (t0, _) in tunnels if t0 >= sp[1]], default=s[-1]))


def build_edges(net, rivers, lakes_level, verbose=True):
    """Découpe les chaînes en arêtes entre nœuds, calcule les profils en long. Renvoie (nœuds, arêtes)."""
    g, H = net.g, net.H
    edges = []
    for ch in net.chains:
        ctrl = np.array(ch.ctrl, dtype=np.float64)
        if len(ctrl) < 2:
            continue
        closed = bool(ch.opts.get("closed"))
        P = L.catmull(ctrl, DS, closed=closed)
        s = L.arclen(P)
        # abscisse des points de contrôle sur la courbe (recherche monotone)
        cs = []
        last = 0
        prev = ctrl[0]
        for c in ctrl:
            d = np.hypot(P[last:, 0] - c[0], P[last:, 1] - c[1])
            # premier minimum local proche : on cherche jusqu'à un peu plus loin que la distance entre les deux points
            # de contrôle (ils peuvent être très espacés : viaduc)
            reach = max(8, int((math.hypot(c[0] - prev[0], c[1] - prev[1]) * 1.6 + 400.0) / DS))
            i = last + int(np.argmin(d[:reach])) if len(d) > 8 else last + int(np.argmin(d))
            cs.append(i)
            last = i
            prev = c
        if not closed:
            cs[-1] = len(P) - 1
        marks = sorted(ch.marks.items())
        if not marks or marks[0][0] != 0:
            name = "_%s_a" % ch.id
            net.nodes[name] = tuple(ctrl[0])
            marks.insert(0, (0, name))
        if not closed and marks[-1][0] != len(ctrl) - 1:
            name = "_%s_b" % ch.id
            net.nodes[name] = tuple(ctrl[-1])
            marks.append((len(ctrl) - 1, name))
        # coupes : indices sur la courbe
        cuts = [(cs[i], name) for (i, name) in marks]
        if closed:
            cuts.append((len(P) - 1, marks[0][1]))        # la boucle revient à son point de départ
        for (name, x, z) in ch.splits:
            i = int(np.argmin(np.hypot(P[:, 0] - x, P[:, 1] - z)))
            net.nodes[name] = (float(P[i, 0]), float(P[i, 1]))
            if name not in net.crossings:          # carrefour ordinaire, pas une bretelle collée à l'autoroute
                net.merges.add(name)
            cuts.append((i, name))
        cuts.sort()
        # un pont tracé à la main est un « viaduc » jusqu'à son ajustement au relief (_refine_spans)
        spans_p = [(cs[c0], cs[c1], "viaduct" if kind == "bridge" else kind) for (c0, c1, kind) in ch.bridges]
        aux_p = []
        for (ax, az, bx, bz, side, width) in ch.aux:
            aux_p.append((int(np.argmin(np.hypot(P[:, 0] - ax, P[:, 1] - az))),
                          int(np.argmin(np.hypot(P[:, 0] - bx, P[:, 1] - bz))), side, width))
        for k in range(len(cuts) - 1):
            (a, na), (b, nb) = cuts[k], cuts[k + 1]
            if b - a < 2:
                continue
            pts = P[a:b + 1].copy()
            spans = []
            under = []
            for (p0, p1, kind) in spans_p:
                q0, q1 = max(p0, a), min(p1, b)
                if q1 > q0:
                    span = [float(s[q0] - s[a]), float(s[q1] - s[a]), kind]
                    if kind == "underpass":            # un tunnel pour tout le reste (tube, terrassement, trous)
                        span[2] = "tunnel"
                        under.append((span[0], span[1]))
                    spans.append(span)
            aux = [[float(s[ia] - s[a]), float(s[ib] - s[a]), side, width] for (ia, ib, side, width) in aux_p
                   if a <= min(ia, ib) and max(ia, ib) <= b]
            eid = ch.id if len(cuts) == 2 else "%s_%d" % (ch.id, k + 1)
            edges.append(dict(id=eid, chain=ch.id, kind=ch.kind, a=na, b=nb, pts=pts, s=L.arclen(pts), spans=spans,
                              underpass=under, aux=aux, opts=ch.opts, **{k2: v for k2, v in KINDS[ch.kind].items()}))

    # Ponts automatiques : franchissement des rivières
    for e in edges:
        P = e["pts"]
        for rv in rivers:
            R = rv["pts"]
            w = float(R[0][3])
            # distance de chaque point de la route à la rivière
            d = np.array([L.point_polyline(p, R)[0] for p in P[::2]])
            inside = d < w * 0.5 + 2.0
            if not inside.any():
                continue
            idx = np.where(inside)[0] * 2
            # regroupe les indices contigus
            groups = np.split(idx, np.where(np.diff(idx) > 12)[0] + 1)
            for grp in groups:
                s0 = e["s"][grp[0]] - 5.0
                s1 = e["s"][min(grp[-1] + 2, len(P) - 1)] + 5.0
                if s1 - s0 > 90.0:
                    continue                      # la route longe la rivière : pas un pont
                e["spans"].append([float(max(s0, 0)), float(min(s1, e["s"][-1])), "bridge"])
    # Fusion des ponts qui se chevauchent (un petit pont de rivière se fond dans le viaduc qui le contient) ; un
    # tunnel reste à part
    for e in edges:
        merged = []
        for a in sorted(e["spans"]):
            last = next((m for m in reversed(merged) if (m[2] == "tunnel") == (a[2] == "tunnel")), None)
            if last is not None and a[0] <= last[1] + 6.0:
                last[1] = max(last[1], a[1])
                if a[2] == "viaduct":
                    last[2] = "viaduct"
            else:
                merged.append(list(a))
        e["spans"] = merged

    # Hauteur des nœuds : relief lissé au nœud
    node_h = {}
    for name, (x, z) in net.nodes.items():
        node_h[name] = net.fixed.get(name, float(g.sample(net.Hs, x, z)))
    for e in edges:
        P = e["pts"]
        e["ground"] = np.array(g.sample(H, P[:, 0], P[:, 1]), dtype=np.float64)
        _refine_spans(g, H, e, node_h[e["a"]], node_h[e["b"]])
        _cover_crossings(e, edges)
        e["target"] = _target(g, H, e)
    # Deux passes : les nœuds prennent la moyenne des profils lissés qui y arrivent
    for it in range(2):
        acc = {}
        for e in edges:
            prof = solve_profile(e, node_h[e["a"]], node_h[e["b"]], pin=(it == 1))
            e["h"] = prof
            # le bout d'une bretelle collé à l'autoroute ne pèse pas sur la hauteur du nœud
            at = e["opts"].get("attach", {}).get("end")
            if at != "a":
                acc.setdefault(e["a"], []).append(prof[0])
            if at != "b":
                acc.setdefault(e["b"], []).append(prof[-1])
        if it == 0:
            for name, v in acc.items():
                node_h[name] = net.fixed.get(name, float(np.mean(v)))
    # Bretelles : près de l'autoroute, elles en prennent exactement la hauteur (les deux chaussées se touchent)
    by_chain = {}
    for e in edges:
        by_chain.setdefault(e["chain"], []).append(e)
    for e in edges:
        at = e["opts"].get("attach")
        if not at:
            continue
        M = np.vstack([np.column_stack([m["pts"][:, 0], m["pts"][:, 1], m["h"]]) for m in by_chain[at["chain"]]])
        s = e["s"]
        from_end = s if at["end"] == "a" else s[-1] - s
        w = 1.0 - L.smoothstep(75.0, 180.0, from_end)
        hm = e["h"].copy()
        for i in np.where(w > 0)[0]:
            _, j, t = L.point_polyline(e["pts"][i], M)
            hm[i] = M[j, 2] + t * (M[j + 1, 2] - M[j, 2])
        e["h"] = e["h"] * (1 - w) + hm * w
    nodes = {}
    for name, (x, z) in net.nodes.items():
        if name in acc:
            nodes[name] = dict(id=name, x=x, z=z, y=node_h[name], edges=[], merge=int(name in net.merges))
    for e in edges:
        for end in ("a", "b"):
            if e[end] not in nodes:        # nœud atteint seulement par des bouts de bretelle
                x, z = net.nodes[e[end]]
                nodes[e[end]] = dict(id=e[end], x=x, z=z, y=node_h[e[end]], edges=[], merge=int(e[end] in net.merges))
        nodes[e["a"]]["edges"].append(e["id"])
        nodes[e["b"]]["edges"].append(e["id"])
        e["pts"] = np.column_stack([e["pts"][:, 0], e["pts"][:, 1], e["h"]])
    for e in edges:
        e["spans"] = [[s0, s1, "bridge" if kind == "viaduct" else kind] for (s0, s1, kind) in e["spans"]]
    gores(edges, by_chain)
    if verbose:
        report(edges)
    return nodes, edges


def gores(edges, by_chain):
    """Là où une bretelle longe l'autoroute de tout près (musoir), aucune des deux n'a d'accotement ni de glissière
    de ce côté : e["bare"] par point, bit 0 = gauche, bit 1 = droite."""
    for e in edges:
        e["bare"] = np.zeros(len(e["pts"]), dtype=np.uint8)
    for e in edges:
        at = e["opts"].get("attach")
        if not at:
            continue
        for m in by_chain[at["chain"]]:
            M = m["pts"]
            tm = L.tangents(M)
            for i, p in enumerate(e["pts"]):
                d, j, t = L.point_polyline(p, M)
                gap = d - m["hw"] - e["hw"]
                if gap > 3.6 or (j == 0 and t <= 0.0) or (j == len(M) - 2 and t >= 1.0):
                    continue
                # côté de l'autoroute où se trouve la bretelle (droite = (tz, -tx))
                side = (p[0] - M[j, 0]) * tm[j, 1] - (p[1] - M[j, 1]) * tm[j, 0]
                m["bare"][max(j - 1, 0):j + 3] |= 2 if side > 0 else 1
                # l'autoroute est toujours à gauche d'une bretelle (circulation à droite)
                e["bare"][i] |= 1


def interchange(net, hwy, name, at, kind="lane", dj=75.0, reach=280.0, aux=130.0, lift=8.3):
    """Échangeur en losange sur l'autoroute « hwy », au point de sa courbe le plus proche de « at » : une route
    passe au-dessus par un pont, quatre bretelles à sens unique la relient aux deux chaussées, avec voies de
    décélération et d'insertion. Crée les nœuds <name>_l (côté gauche de l'autoroute dans son sens de tracé) et
    <name>_r (côté droit), à raccorder au reste du réseau. Renvoie la fonction repère(u, v) → (x, z) : u mètres le
    long de l'autoroute depuis le croisement, v mètres vers la droite."""
    P = hwy.spline()
    s = L.arclen(P)
    T = L.tangents(P)
    i0 = int(np.argmin(np.hypot(P[:, 0] - at[0], P[:, 1] - at[1])))
    hw_h, rw = KINDS["highway"]["hw"], KINDS["ramp"]["hw"]

    def frame(u, v=0.0):
        i = int(np.clip(np.searchsorted(s, s[i0] + u), 0, len(P) - 1))
        return (float(P[i, 0] + T[i, 1] * v), float(P[i, 1] - T[i, 0] * v))

    cx, cz = frame(0.0)
    h0 = float(net.g.sample(net.Hs, cx, cz))
    for side, tag in ((-1.0, "l"), (1.0, "r")):
        net.node("%s_%s" % (name, tag), *frame(0.0, side * dj))
        net.fixed["%s_%s" % (name, tag)] = h0 + lift * (1.0 - float(L.smoothstep(28.0, 158.0, dj)))
    # Pont au-dessus de l'autoroute (ses culées sont hors des talus de l'autoroute)
    span = hw_h + 15.0
    over = net.road(name + "_pont", kind, name + "_l", lift=[(cx, cz, lift, 28.0, 130.0)], smooth=14.0)
    over.to(frame(0.0, -span)).to(frame(0.0, span)).bridge(1).node(name + "_r")
    o = hw_h + rw
    shape = ((25.0, 0.3), (55.0, 1.6), (90.0, 5.0))        # (distance depuis le nœud de l'autoroute, écart en plus)
    # Deux nœuds sur l'axe de l'autoroute, avant et après le pont : chacun sert une sortie d'un côté et une entrée
    # de l'autre
    hwy.split(name + "_a", *frame(-reach))
    hwy.split(name + "_b", *frame(reach))
    for side, tag in ((-1.0, "l"), (1.0, "r")):
        d = side                                           # sens de circulation de la chaussée de ce côté
        # Sortie : quitte l'autoroute avant le pont, arrive au carrefour
        ux = -d * reach
        x_out = name + ("_a" if ux < 0 else "_b")
        r = net.road("%s_sortie_%s" % (name, tag), "ramp", frame(ux, side * o), attach=dict(end="a", chain=hwy.id),
                     smooth=14.0)
        r.mark(x_out)
        for (du, dv) in shape:
            r.to(frame(ux + d * du, side * (o + dv)))
        r.node("%s_%s" % (name, tag))
        hwy.aux.append(frame(ux) + frame(ux - d * aux) + (int(side), 2.0 * rw))
        # Entrée : part du carrefour, rejoint l'autoroute après le pont
        ue = d * reach
        x_in = name + ("_a" if ue < 0 else "_b")
        r = net.road("%s_entree_%s" % (name, tag), "ramp", "%s_%s" % (name, tag), attach=dict(end="b", chain=hwy.id),
                     smooth=14.0)
        for (du, dv) in reversed(shape):
            r.to(frame(ue - d * du, side * (o + dv)))
        r.to(frame(ue, side * o)).mark(x_in)
        hwy.aux.append(frame(ue) + frame(ue + d * aux) + (int(side), 2.0 * rw))
    return frame


def solve_profile(e, ha, hb, pin=True):
    """Profil en long : relief lissé le long du tracé, pente limitée (déblai et remblai équilibrés), raccords."""
    s = e["s"]
    n = len(s)
    gmax = e["grade"] * e["opts"].get("grade_k", 1.0)
    sigma = e["opts"].get("smooth", 26.0) / DS
    h = L.gauss1d(e["target"], sigma)

    def repin(h):
        if not pin:
            return h
        Lc = min(140.0, s[-1] * 0.5)
        wa = np.clip(1.0 - s / Lc, 0, 1) ** 2
        wb = np.clip(1.0 - (s[-1] - s) / Lc, 0, 1) ** 2
        return h + (ha - h[0]) * wa + (hb - h[-1]) * wb
    h = repin(h)
    step = gmax * DS
    for _ in range(3):
        up = h.copy()
        for i in range(1, n):
            up[i] = min(up[i], up[i - 1] + step)
        for i in range(n - 2, -1, -1):
            up[i] = min(up[i], up[i + 1] + step)
        lo = h.copy()
        for i in range(1, n):
            lo[i] = max(lo[i], lo[i - 1] - step)
        for i in range(n - 2, -1, -1):
            lo[i] = max(lo[i], lo[i + 1] - step)
        h = repin((up + lo) * 0.5)
    h = repin(L.gauss1d(h, 9.0 / DS))
    # crête de barrage : parfaitement réglée entre ses deux extrémités
    for (s0, s1, kind) in e["spans"]:
        if kind == "dam":
            m = (s >= s0) & (s <= s1)
            h[m] = np.interp(s[m], [s[0], s[-1]], [ha, hb])
    return h


def report(edges):
    total = 0.0
    print("%-22s %-9s %7s %6s %6s %6s %6s" % ("arête", "type", "long.", "pente", "déblai", "rembl.", "Rmin"))
    for e in edges:
        s, h = e["s"], e["pts"][:, 2]
        gr = np.abs(np.gradient(h, s)).max() if len(s) > 2 else 0.0
        dev = h - e["ground"]
        mask = np.ones(len(s), dtype=bool)
        for (s0, s1, kind) in e["spans"]:
            mask &= ~((s >= s0) & (s <= s1))
        cut = -dev[mask].min() if mask.any() else 0.0
        fill = dev[mask].max() if mask.any() else 0.0
        k = np.abs(L.curvature(e["pts"], DS))
        rmin = 1.0 / max(k.max(), 1e-4)
        total += s[-1]
        flag = " !" if gr > e["grade"] * 1.15 or cut > 9 or fill > 9 or rmin < 9 else ""
        print("%-22s %-9s %7.0f %5.1f%% %6.1f %6.1f %6.0f%s" % (e["id"], e["kind"], s[-1], gr * 100, cut, fill, rmin, flag))
    print("total : %.1f km" % (total / 1000.0))


# ----------------------------------------------------------------- terrassement

def pad_distance(X, Z, px, pz, pr):
    """Distance au bord d'une aire (négative dedans) : disque de rayon pr, ou rectangle pr = (demi-côté x, demi-côté z,
    rotation en degrés)."""
    if isinstance(pr, (tuple, list)):
        hx, hz, rot = pr
        a = np.radians(rot)
        u = (X - px) * np.cos(a) + (Z - pz) * np.sin(a)
        v = -(X - px) * np.sin(a) + (Z - pz) * np.cos(a)
        return np.maximum(np.abs(u) - hx, np.abs(v) - hz)
    return np.hypot(X - px, Z - pz) - pr


def pad_extent(pr):
    return float(max(pr[0], pr[1]) * 1.42) if isinstance(pr, (tuple, list)) else float(pr)

def aux_width(e, s):
    """Largeur de la voie d'insertion ou de sortie à l'abscisse s : (gauche, droite). Pleine largeur sur 65 % de sa
    longueur depuis le nœud, puis biseau (même loi que RoadNetwork.AuxWidth)."""
    s = np.asarray(s, dtype=np.float64)
    out = [np.zeros(s.shape), np.zeros(s.shape)]
    for (sa, sb, side, width) in e.get("aux", ()):
        u = np.abs(s - sa) / max(abs(sb - sa), 1e-6)
        inside = ((s - sa) * (sb - sa) >= 0) & (u <= 1.0)
        w = np.where(inside, width * np.clip((1.0 - u) / 0.35, 0.0, 1.0), 0.0)
        k = 1 if side > 0 else 0
        out[k] = np.maximum(out[k], w)
    return out[0], out[1]


def tunnel_mask(e):
    """Points de l'arête situés dans un tunnel."""
    s = e["s"]
    m = np.zeros(len(s), dtype=bool)
    for (s0, s1, kind) in e["spans"]:
        if kind == "tunnel":
            m |= (s >= s0 - 1e-6) & (s <= s1 + 1e-6)
    return m


def carve(g, H, nodes, edges, pads=(), strips=()):
    """Adapte le relief aux routes : plate-forme sous la chaussée, talus de déblai et de remblai à pente fixe.
    strips : bandes aplanies le long d'un axe (points [x, z, altitude du sol], demi-largeur), talus comme les routes.
    Renvoie (H modifié, distance au bord de chaussée la plus proche [m, plafonnée à 80])."""
    H = H.copy()
    edge = np.full(H.shape, 80.0, dtype=np.float32)
    lo = np.full(H.shape, -1e9)
    hi = np.full(H.shape, 1e9)
    for e in edges:
        P = e["pts"]
        s = e["s"]
        al, ar = aux_width(e, s)
        hw = e["hw"] + e["sidewalk"] + np.maximum(al, ar)
        radius = max(46.0, e["hw"] + 42.0)
        bridge = np.zeros(len(P), dtype=bool)
        for (s0, s1, kind) in e["spans"]:
            if kind != "tunnel":
                bridge |= (s >= s0) & (s <= s1)
        tun = tunnel_mask(e)
        # têtes de tunnel : point et direction vers l'intérieur ; rien n'est terrassé au-delà de leur plan
        T = L.tangents(P)
        # têtes regroupées par tunnel (entrée puis sortie) : une maille n'est épargnée que si elle est du côté
        # intérieur de TOUTES les têtes de son tunnel. (Avec un « et » sur toutes les têtes, un tunnel court, dont les
        # deux têtes sont à portée, interdisait aussi le déblai devant chaque entrée : le remblai de la route du
        # dessus enterrait les abords du passage inférieur.)
        tunnels = []
        for i in range(len(P) - 1):
            if tun[i + 1] and not tun[i]:
                tunnels.append([(i + 1, T[i + 1, 0], T[i + 1, 1])])
            if tun[i] and not tun[i + 1]:
                if not tunnels or len(tunnels[-1]) == 2:
                    tunnels.append([])
                tunnels[-1].append((i, -T[i, 0], -T[i, 1]))

        def fn(sz, sx, d, t, i, P=P, hw=hw, bridge=bridge, tun=tun, tunnels=tunnels, s=s, radius=radius):
            if tun[i] and tun[i + 1]:
                return                      # en tunnel : le relief reste intact au-dessus
            ok = None
            for heads in tunnels:
                inside = None
                for (ip, tx, tz) in heads:
                    if abs(s[ip] - s[i]) < radius + 12.0:
                        behind = (g.X[sz, sx] - P[ip, 0]) * tx + (g.Z[sz, sx] - P[ip, 1]) * tz >= 0.0
                        inside = behind if inside is None else inside & behind
                if inside is not None:
                    ok = ~inside if ok is None else ok & ~inside
            ed = d - (hw[i] + t * (hw[i + 1] - hw[i]))
            if ok is not None:
                ed = np.where(ok, ed, 1e9)
            edge[sz, sx] = np.minimum(edge[sz, sx], np.minimum(ed, 80.0))
            if bridge[i] and bridge[i + 1]:
                # sous un pont : le terrain passe sous le tablier (jamais à travers), rien n'est remblayé
                yb = P[i, 2] + t * (P[i + 1, 2] - P[i, 2]) - 1.7
                hi[sz, sx] = np.minimum(hi[sz, sx], yb + CUT_SLOPE * np.maximum(0.0, ed - 0.6))
                return
            y = P[i, 2] + t * (P[i + 1, 2] - P[i, 2]) - UNDER
            lo[sz, sx] = np.maximum(lo[sz, sx], y - FILL_SLOPE * np.maximum(0.0, ed - FILL_FLAT))
            hi[sz, sx] = np.minimum(hi[sz, sx], y + CUT_SLOPE * np.maximum(0.0, ed - CUT_FLAT))
        stamp_polyline(g, P, radius, fn)
    # Carrefours : disque à la hauteur du nœud
    radius = 46.0
    by_id = {e["id"]: e for e in edges}
    for n in nodes.values():
        if n.get("merge"):
            continue                        # sur l'axe de l'autoroute : déjà terrassé
        if len(n["edges"]) < 2:
            # aire de demi-tour (même rayon que RoadNetwork.DeadEndRadius)
            e0 = by_id[n["edges"][0]]
            r = e0["hw"] + e0["sidewalk"] + 5.0
        else:
            r = 12.0                        # chaussées reculées de 8 à 9 m, angles arrondis (RoadNetwork.JunctionFlare)
        sz, sx = g.window(n["x"] - radius, n["z"] - radius, n["x"] + radius, n["z"] + radius)
        d = np.hypot(g.X[sz, sx] - n["x"], g.Z[sz, sx] - n["z"]) - r
        y = n["y"] - UNDER
        lo[sz, sx] = np.maximum(lo[sz, sx], y - FILL_SLOPE * np.maximum(0.0, d - FILL_FLAT))
        hi[sz, sx] = np.minimum(hi[sz, sx], y + CUT_SLOPE * np.maximum(0.0, d - CUT_FLAT))
        edge[sz, sx] = np.minimum(edge[sz, sx], d)
    # Aires aménagées (places, parkings) : (x, z, rayon, altitude)
    for (px, pz, pr, py) in pads:
        ext = pad_extent(pr)
        sz, sx = g.window(px - ext - radius, pz - ext - radius, px + ext + radius, pz + ext + radius)
        d = pad_distance(g.X[sz, sx], g.Z[sz, sx], px, pz, pr)
        y = py - UNDER
        lo[sz, sx] = np.maximum(lo[sz, sx], y - FILL_SLOPE * np.maximum(0.0, d))
        hi[sz, sx] = np.minimum(hi[sz, sx], y + CUT_SLOPE * np.maximum(0.0, d))
        edge[sz, sx] = np.minimum(edge[sz, sx], np.maximum(d, 0.0) + 1.0)
    # Bandes (élan et réception des tremplins) : le sol suit l'altitude de l'axe sur toute la largeur
    for (Q, half) in strips:
        def fn(sz, sx, d, t, i, Q=Q, half=half):
            ed = d - half
            y = Q[i, 2] + t * (Q[i + 1, 2] - Q[i, 2])
            lo[sz, sx] = np.maximum(lo[sz, sx], y - FILL_SLOPE * np.maximum(0.0, ed))
            hi[sz, sx] = np.minimum(hi[sz, sx], y + CUT_SLOPE * np.maximum(0.0, ed))
            edge[sz, sx] = np.minimum(edge[sz, sx], np.maximum(ed, 0.0) + 1.0)
        stamp_polyline(g, Q, half + radius, fn)
    # Le remblai d'abord, le déblai ensuite : la chaussée la plus basse n'est jamais enterrée
    H = np.maximum(H, lo)
    H = np.minimum(H, hi)
    return H, edge


def holes(g, H, edges, edge=None):
    """Trous du terrain aux têtes de tunnel : mailles (repérées par leur nœud sud-ouest) dont la surface traverserait
    l'intérieur du tube. Ailleurs le relief passe au-dessus du tunnel, intact. Si « edge » est fourni, la distance
    à la route y est mise à -1 autour des trous (ni arbres ni herbes au bord du vide).
    Une maille est ouverte si son centre est à moins de (demi-largeur du tube + une demi-diagonale) de l'axe : le bord
    du trou reste alors sous la coque extérieure du tube (RoadNetwork.TunnelShell = 6 m)."""
    dist = np.full(H.shape, 1e9)
    y = np.zeros(H.shape)
    top = np.zeros(H.shape)
    lim = np.zeros(H.shape)
    for e in edges:
        tun = tunnel_mask(e)
        if not tun.any():
            continue
        P = e["pts"]
        reach = e["hw"] + 1.0 + 2.9
        hin = tunnel_height(e["kind"]) + 0.5
        idx = np.where(tun)[0]
        for grp in np.split(idx, np.where(np.diff(idx) > 1)[0] + 1):
            Q = P[grp[0]:grp[-1] + 1]
            if len(Q) < 2:
                continue

            def fn(sz, sx, d, t, i, Q=Q, reach=reach, hin=hin):
                m = d < dist[sz, sx]
                yy = Q[i, 2] + t * (Q[i + 1, 2] - Q[i, 2])
                dist[sz, sx] = np.where(m, d, dist[sz, sx])
                y[sz, sx] = np.where(m, yy, y[sz, sx])
                top[sz, sx] = np.where(m, hin, top[sz, sx])
                lim[sz, sx] = np.where(m, reach, lim[sz, sx])
            stamp_polyline(g, Q, reach + 3.0 * g.step, fn)
    out = np.zeros(H.shape, dtype=bool)
    if dist.min() > 1e8:
        return out

    def corners(A):
        return A[:-1, :-1], A[:-1, 1:], A[1:, :-1], A[1:, 1:]
    near = np.maximum.reduce(corners(dist)) < 1e8                 # les quatre coins connaissent le tunnel
    centre = sum(corners(np.minimum(dist, 1e3))) * 0.25
    road = sum(corners(y)) * 0.25
    cmin = np.minimum.reduce(corners(H))
    cmax = np.maximum.reduce(corners(H))
    out[:-1, :-1] = near & (centre < np.maximum.reduce(corners(lim))) & (cmax > road - 0.1) & (cmin < road + np.maximum.reduce(corners(top)))
    if edge is not None:
        near = out.copy()
        near[1:, :] |= out[:-1, :]
        near[:, 1:] |= out[:, :-1]
        near[1:, 1:] |= out[:-1, :-1]
        edge[near] = -1.0
    return out


def tunnel_zone(g, edges, margin=7.0):
    """Nœuds de la grille situés au-dessus d'un tunnel (et de ses têtes) : les tuiles du terrain n'y descendent pas
    leurs jupes de raccord, qui traverseraient le tube."""
    out = np.zeros((g.n, g.n), dtype=bool)
    for e in edges:
        tun = tunnel_mask(e)
        if not tun.any():
            continue
        P = e["pts"]
        reach = e["hw"] + 1.0 + margin
        idx = np.where(tun)[0]
        for grp in np.split(idx, np.where(np.diff(idx) > 1)[0] + 1):
            Q = P[max(grp[0] - 6, 0):min(grp[-1] + 7, len(P))]
            if len(Q) < 2:
                continue

            def fn(sz, sx, d, t, i, reach=reach):
                out[sz, sx] |= d < reach
            stamp_polyline(g, Q, reach + g.step, fn)
    return out
