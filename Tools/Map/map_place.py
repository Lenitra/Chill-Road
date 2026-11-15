"""Décor posé sur la carte : bâtiments le long des rues, monuments, hameaux, fermes, mobilier, signalisation.

Chaque objet est une ligne (item, x, y, z, yaw, scale, flags) lue par Unity (MapProps). Les emprises des bâtiments
sont notées dans un masque (pas d'arbres dessus). Façade des modèles = +Z Unity ; yaw en degrés autour de Y.
"""
import math
import random
import numpy as np
import map_lib as L
import map_manifest

INSTANCED, DETAIL = 1, 2


def yaw_of(dx, dz):
    return math.degrees(math.atan2(dx, dz))


class Placer:
    def __init__(self, g, H, edge_map, water, nodes, edges, rivers, seed=11):
        self.g, self.H, self.edge_map, self.water = g, H, edge_map, water
        self.nodes = nodes
        self.edges = {e["id"]: e for e in edges}
        self.edge_list = edges
        self.items = map_manifest.load()
        self.rng = random.Random(seed)
        self.places = []
        self.boxes = {}                      # grille de boîtes orientées (cx, cz, w, d, yaw)
        self.blocked = np.zeros(H.shape, dtype=bool)
        self.river = np.zeros(H.shape, dtype=bool)
        from map_terrain import stamp_polyline
        for rv in rivers:
            w = float(rv["pts"][0][3])

            def fn(sz, sx, d, t, i, w=w):
                self.river[sz, sx] |= d < w * 0.5 + 4.0
            stamp_polyline(g, rv["pts"][::2], w + 10.0, fn)
        self.count = {}

    # ----------------------------------------------------------- outils
    def h(self, x, z):
        return float(self.g.sample(self.H, x, z))

    def cell(self, A, x, z):
        fx, fz = self.g.index(x, z)
        ix = int(min(max(round(float(fx)), 0), self.g.n - 1))
        iz = int(min(max(round(float(fz)), 0), self.g.n - 1))
        return A[iz, ix]

    def frame(self, e, s):
        """Point de l'axe à l'abscisse s : (x, z, h, tangente, normale gauche)."""
        P, S = e["pts"], e["s"]
        s = min(max(s, 0.0), S[-1])
        i = int(np.searchsorted(S, s))
        i = min(max(i, 1), len(S) - 1)
        t = (s - S[i - 1]) / max(S[i] - S[i - 1], 1e-6)
        p = P[i - 1] + (P[i] - P[i - 1]) * t
        a, b = P[max(i - 2, 0)], P[min(i + 1, len(P) - 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]
        n = math.hypot(tx, tz) or 1.0
        tx, tz = tx / n, tz / n
        return p[0], p[1], p[2], (tx, tz), (-tz, tx)

    @staticmethod
    def corners(cx, cz, w, d, yaw):
        a = math.radians(yaw)
        rx, rz = math.cos(a), -math.sin(a)          # axe « droite » du modèle
        fx, fz = math.sin(a), math.cos(a)           # axe « façade »
        out = []
        for sx, sf in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            out.append((cx + rx * sx * w / 2 + fx * sf * d / 2, cz + rz * sx * w / 2 + fz * sf * d / 2))
        return out

    @staticmethod
    def overlap(A, B):
        """Test des axes séparateurs entre deux rectangles (listes de 4 coins)."""
        for poly in (A, B):
            for i in range(4):
                x0, z0 = poly[i]
                x1, z1 = poly[(i + 1) % 4]
                nx, nz = z1 - z0, x0 - x1
                a = [p[0] * nx + p[1] * nz for p in A]
                b = [p[0] * nx + p[1] * nz for p in B]
                if max(a) < min(b) or max(b) < min(a):
                    return False
        return True

    def free(self, cs, cx, cz, r):
        k = 48.0
        for bx in range(int((cx - r) // k) - 1, int((cx + r) // k) + 2):
            for bz in range(int((cz - r) // k) - 1, int((cz + r) // k) + 2):
                for other in self.boxes.get((bx, bz), ()):
                    if self.overlap(cs, other):
                        return False
        return True

    def claim(self, cs, cx, cz, margin=2.5):
        k = 48.0
        self.boxes.setdefault((int(cx // k), int(cz // k)), []).append(cs)
        xs = [p[0] for p in cs]
        zs = [p[1] for p in cs]
        sz, sx = self.g.window(min(xs) - margin, min(zs) - margin, max(xs) + margin, max(zs) + margin)
        self.blocked[sz, sx] = True

    def add(self, item, x, z, yaw=0.0, y=None, scale=1.0, flags=0, dy=0.0):
        if item not in self.items:
            return False
        if y is None:
            y = self.h(x, z)
        self.places.append(dict(item=item, x=round(x, 2), y=round(y + dy, 2), z=round(z, 2), yaw=round(yaw % 360.0, 1),
                                scale=round(scale, 3), flags=flags))
        self.count[item] = self.count.get(item, 0) + 1
        return True

    @staticmethod
    def box_size(it):
        """Encombrement réel au sol (débords de toit, balcons, auvents compris) : rien ne doit s'y superposer."""
        return max(it["footprint"][0], it["size"][0]), max(it["footprint"][1], it["size"][2])

    def building(self, item, cx, cz, yaw, max_drop=5.0, clear=0.4, shrink=0.25, force=False, margin=2.5):
        """Pose un bâtiment si la place est libre (ni route, ni eau, ni rivière, ni voisin, pente acceptable)."""
        it = self.items.get(item)
        if it is None:
            return False
        w, d = it["footprint"]
        w = max(w, it["size"][0] * 0.9)
        cs = self.corners(cx, cz, w - shrink, d - shrink, yaw)
        pts = cs + [(cx, cz)] + [((cs[i][0] + cs[(i + 1) % 4][0]) / 2, (cs[i][1] + cs[(i + 1) % 4][1]) / 2) for i in range(4)]
        hs = [self.h(x, z) for x, z in pts]
        if not force:
            lim = self.g.half - 420.0
            for (x, z) in pts:
                if abs(x) > lim or abs(z) > lim:
                    return False
                if self.cell(self.edge_map, x, z) < clear or self.cell(self.water, x, z) or self.cell(self.river, x, z):
                    return False
            if max(hs) - min(hs) > max_drop:
                return False
        bw, bd = self.box_size(it)
        box = self.corners(cx, cz, bw - 0.04, bd - 0.04, yaw)
        if not self.free(box, cx, cz, max(bw, bd)):
            if not force:
                return False
            # bâtiment imposé : posé quand même, mais signalé (il recouvre un autre objet)
            print("  ! %s impose en (%.0f, %.0f) recouvre un autre objet" % (item, cx, cz))
        a = math.radians(yaw)
        front = self.h(cx + math.sin(a) * d / 2, cz + math.cos(a) * d / 2)
        y = min(max(front, max(hs) - 2.4), min(hs) + 2.6)
        self.claim(box, cx, cz, margin)
        self.add(item, cx, cz, yaw, y=y)
        return True

    def beside(self, item, edge_id, s, side, setback=3.0, turn=0.0, **kw):
        """Bâtiment au bord d'une route : s = abscisse (ou fraction 0-1 de la longueur), side = +1 gauche / -1 droite."""
        e = self.edges[edge_id]
        if 0.0 < s <= 1.0:
            s = s * e["s"][-1]
        it = self.items.get(item)
        if it is None:
            return False
        x, z, h, t, n = self.frame(e, s)
        off = e["hw"] + e["sidewalk"] + setback + it["footprint"][1] / 2
        cx, cz = x + n[0] * side * off, z + n[1] * side * off
        return self.building(item, cx, cz, yaw_of(-n[0] * side, -n[1] * side) + turn, **kw)

    def first(self, item, edge_id, fractions, side, **kw):
        """Essaie plusieurs emplacements le long d'une route ; signale l'échec."""
        for f in fractions:
            for sd in ((side,) if side else (1, -1)):
                if self.beside(item, edge_id, f, sd, **kw):
                    return True
        print("  ! pas de place pour", item, "le long de", edge_id)
        return False

    def row(self, e, s0, s1, side, pick, setback=0.6, gap=(0.0, 0.0), alley=0.0, max_drop=3.6, turn_jitter=0.0):
        """Rangée de bâtiments le long d'une route, façades sur la rue. Renvoie le nombre posé."""
        s, n_placed = s0, 0
        while s < s1:
            name = pick()
            it = self.items.get(name)
            if it is None:
                break
            w, d = it["footprint"]
            w = self.box_size(it)[0]          # largeur réelle : pas de débord sur la maison voisine
            sc = s + w / 2
            if sc + w / 2 > s1:
                break
            x, z, h, t, n = self.frame(e, sc)
            off = e["hw"] + e["sidewalk"] + setback + d / 2
            cx, cz = x + n[0] * side * off, z + n[1] * side * off
            yaw = yaw_of(-n[0] * side, -n[1] * side) + self.rng.uniform(-turn_jitter, turn_jitter)
            if self.building(name, cx, cz, yaw, max_drop=max_drop):
                n_placed += 1
                s += w + self.rng.uniform(*gap) + (self.rng.uniform(3.0, 6.0) if self.rng.random() < alley else 0.0)
            else:
                s += 3.0
        return n_placed

    def roadside(self, item, e, s, side, offset, face="road", flags=0, dy=0.0, scale=1.0, y_from_road=True, turn=0.0, avoid=False):
        """Objet au bord de la chaussée. face : 'road' (regarde la route), 'along' (dans le sens a → b),
        'against' (regarde les véhicules qui arrivent de a), 'away'."""
        x, z, h, t, n = self.frame(e, s)
        px, pz = x + n[0] * side * offset, z + n[1] * side * offset
        if face == "road":
            yaw = yaw_of(-n[0] * side, -n[1] * side)
        elif face == "away":
            yaw = yaw_of(n[0] * side, n[1] * side)
        elif face == "along":
            yaw = yaw_of(t[0], t[1])
        else:
            yaw = yaw_of(-t[0], -t[1])
        y = h if y_from_road and offset < e["hw"] + e["sidewalk"] + 1.3 else None
        if avoid and self.cell(self.blocked, px, pz):
            return False
        return self.add(item, px, pz, yaw + turn, y=y, flags=flags, dy=dy, scale=scale)


# ----------------------------------------------------------------- composition de la carte
TOWNHOUSES = ["townhouse_a", "townhouse_b", "townhouse_c", "townhouse_d", "townhouse_e", "townhouse_f", "townhouse_g",
              "townhouse_h", "townhouse_i", "townhouse_j", "townhouse_k"]
PLAIN_HOUSES = ["townhouse_b", "townhouse_d", "townhouse_f", "townhouse_j"]
CHALETS = ["chalet_a", "chalet_b", "chalet_c", "chalet_d", "chalet_g", "chalet_h"]
FARM = ["barn_alpine", "mazot", "hayloft", "cowshed", "mazot"]

# Aires aplanies et pavées : (x, z, rayon). La hauteur est celle du relief au centre.
PADS = [
    (-340.0, -258.0, 34.0),     # place de la ville
    (-560.0, 2515.0, 26.0),     # parking du col
    (1262.0, 1668.0, 30.0),     # front de neige de la station
]

POIS = [
    dict(id="depart", fr="", en="", x=-470.0, z=-296.0, r=90.0),
    dict(id="ville", fr="Valombre", en="Valombre", x=-385.0, z=-300.0, r=330.0),
    dict(id="granges", fr="Les Granges", en="Les Granges", x=-2330.0, z=-270.0, r=190.0),
    dict(id="lac", fr="Lac de Valombre", en="Lake Valombre", x=1500.0, z=60.0, r=560.0),
    dict(id="col", fr="Col de la Combe · 1 420 m", en="Combe Pass · 1,420 m", x=-560.0, z=2480.0, r=170.0),
    dict(id="station", fr="Les Arolles · station", en="Les Arolles · ski resort", x=1250.0, z=1700.0, r=300.0),
    dict(id="barrage", fr="Barrage du Val Sud", en="Val Sud Dam", x=505.0, z=-1485.0, r=230.0),
    dict(id="hameau", fr="Le Plan", en="Le Plan", x=421.0, z=-867.0, r=150.0),
    dict(id="alpage", fr="Alpage des Crêtes", en="Crêtes Pasture", x=-1561.0, z=-1252.0, r=170.0),
]


def pads_with_height(g, H):
    return [(x, z, r, float(g.sample(H, x, z))) for (x, z, r) in PADS]


def populate(g, H, edge_map, water, nodes, edges, rivers, dam):
    P = Placer(g, H, edge_map, water, nodes, edges, rivers)
    rng = P.rng
    E = P.edges
    town = (-385.0, -300.0)

    def near_town(e, s, r=340.0):
        x, z, _, _, _ = P.frame(e, s)
        return math.hypot(x - town[0], z - town[1]) < r

    # ---------------- Place de la ville et monuments
    px, pz, pr = PADS[0]
    P.add("fountain_plaza", px, pz, 0.0)
    P.claim(Placer.corners(px, pz, 7, 7, 0), px, pz, 0.5)
    P.building("church", px - 4.0, pz + 52.0, 180.0, max_drop=6.0, clear=0.0, force=True)
    P.building("townhall", px + 44.0, pz - 2.0, 270.0, max_drop=6.0, clear=0.0, force=True)
    P.building("monument", px - 16.0, pz + 14.0, 135.0, force=True, margin=0.5)
    P.building("bandstand", px + 14.0, pz + 16.0, 200.0, force=True, margin=0.5)
    for k, (dx, dz, yaw) in enumerate(((-18, -14, 20), (-9, -17, 0), (10, -16, -15))):
        P.building("market_stall_a" if k % 2 == 0 else "market_stall_b", px + dx, pz + dz, yaw, force=True, margin=0.3)
    for k in range(6):
        a = k / 6.0 * math.tau + 0.4
        P.add("bench" if k % 2 == 0 else "planter", px + math.cos(a) * 9.0, pz + math.sin(a) * 9.0, yaw_of(-math.cos(a), -math.sin(a)) + 180)
    P.add("flagpole_red", px + 34.0, pz - 16.0)
    P.add("flagpole_blue", px + 34.0, pz + 12.0)
    P.add("clock_post", px - 26.0, pz - 4.0)
    P.add("info_board", px - 24.0, pz - 20.0, 90.0)
    # Grand hôtel face à la place, école, pompiers, services
    fr = (0.5, 0.35, 0.65, 0.2, 0.8, 0.1, 0.9)
    P.first("hotel_grand", "vallee_6", (0.75, 0.6, 0.5, 0.4), -1, setback=1.5)
    P.first("school", "rue_haute_3", fr, 1, setback=6.0)
    P.first("firestation", "rue_haute_1", (0.72, 0.6, 0.85, 0.5), 0, setback=5.0)
    P.first("supermarket", "vallee_9", (0.3, 0.4, 0.5, 0.6, 0.2), 0, setback=12.0)
    P.first("gasstation", "vallee_9", (0.6, 0.7, 0.8, 0.5), 0, setback=3.0, clear=0.2)
    P.first("garage", "vallee_8", (0.6, 0.75, 0.45, 0.3), 0, setback=6.0)
    P.first("dairy", "rive_gauche_3", fr, 0, setback=5.0)
    P.first("sawmill", "chemin_rive", (0.42, 0.35, 0.5, 0.6), 0, setback=8.0)
    P.first("watermill", "quai_1", (0.6, 0.5, 0.7, 0.4), -1, setback=1.0, clear=0.2)
    P.first("chapel", "rue_haute_1", (0.25, 0.15, 0.35), 0, setback=10.0)

    # ---------------- Ville : rangées de maisons le long des rues
    def pick_core():
        return rng.choice(TOWNHOUSES)

    def pick_outer():
        return rng.choice(PLAIN_HOUSES + CHALETS + CHALETS)

    for e in P.edge_list:
        if e["kind"] != "street" and not e["id"].startswith("vallee"):
            continue
        Ltot = e["s"][-1]
        s0, s1 = 12.0, Ltot - 12.0
        if s1 - s0 < 12:
            continue
        # découpe en tronçons de 40 m : cœur de ville (rangées serrées) ou faubourg (chalets espacés)
        s = s0
        while s < s1:
            s2 = min(s + 80.0, s1)
            x, z, _, _, _ = P.frame(e, (s + s2) / 2)
            dist = math.hypot(x - town[0], z - town[1])
            for side in (1, -1):
                if dist < 300.0:
                    P.row(e, s, s2, side, pick_core, setback=0.5 if e["sidewalk"] > 0 else 3.0, alley=0.18)
                elif dist < 620.0 and (e["kind"] == "street" or dist < 520.0):
                    P.row(e, s, s2, side, pick_outer, setback=rng.uniform(4.0, 8.0), gap=(5.0, 14.0), max_drop=4.5, turn_jitter=6.0)
            s = s2

    # ---------------- Station de ski
    sx, sz, sr = PADS[2]
    P.building("hotel_chalet", sx - 6.0, sz + 62.0, 180.0, max_drop=7.0, clear=0.0)
    # Remontées mécaniques d'abord : leurs gares sont imposées, les chalets se placent autour
    cable(P, (sx + 62.0, sz + 30.0), (1900.0, 2330.0), "cablecar_station_low", "cablecar_pylon", "cablecar_cabin",
          "cablecar_station_high", span=150.0, top=24.6, start_h=10.4)
    cable(P, (sx + 44.0, sz - 44.0), (1700.0, 1450.0), "chairlift_station", "chairlift_pylon", "chairlift_chair", None,
          span=70.0, top=11.9, start_h=4.7, every=24.0)
    for (eid, fr, side) in (("station_boucle", 0.18, 1), ("station_boucle", 0.5, 1), ("station_boucle", 0.82, -1)):
        P.beside("apartment_ski", eid, fr, side, setback=7.0, max_drop=7.0)
    for e in P.edge_list:
        if e["id"] in ("station_boucle", "combe_2") or e["id"].startswith("balcon"):
            s1 = min(e["s"][-1] - 10.0, 330.0 if e["id"].startswith("balcon") else 1e9)
            for side in (1, -1):
                P.row(e, 12.0, s1, side, lambda: rng.choice(CHALETS + ["chalet_e", "townhouse_g", "townhouse_i"]),
                      setback=rng.uniform(4.0, 7.0), gap=(6.0, 16.0), max_drop=5.0, turn_jitter=5.0)
    for k in range(5):
        P.building("ski_rack", sx - 12.0 + k * 5.0, sz - 18.0, 0.0, max_drop=2.0, clear=0.0, margin=0.3)
    for (cx_, cz_, yaw_) in ((sx + 20.0, sz - 22.0, 40.0), (sx + 70.0, sz - 70.0, 120.0)):
        P.building("snow_cannon", cx_, cz_, yaw_, max_drop=3.0, clear=0.5, margin=0.5)


    # ---------------- Col
    cx, cz, cr = PADS[1]
    P.building("restaurant_altitude", cx + 34.0, cz + 6.0, 250.0, max_drop=6.0, clear=0.0)
    P.building("refuge", cx - 34.0, cz + 20.0, 110.0, max_drop=6.0, clear=0.0)
    P.add("pass_sign", cx - 8.0, cz - 14.0, 180.0)
    P.add("sign_hiking", cx + 6.0, cz + 20.0, 200.0)
    P.add("picnic_table", cx - 14.0, cz + 12.0, 30.0)
    P.add("picnic_table", cx - 6.0, cz + 18.0, 100.0)
    P.add("flagpole_red", cx + 16.0, cz + 2.0)

    # ---------------- Hameaux : chalets et fermes le long des routes
    hamlets = [((-2330.0, -270.0), 190.0, 0.7), ((421.0, -867.0), 150.0, 0.8), ((2335.0, 335.0), 170.0, 0.8),
               ((1175.0, 370.0), 150.0, 0.6), ((-1561.0, -1252.0), 130.0, 0.5), ((-1100.0, -300.0), 120.0, 0.4)]
    for (hx, hz), hr, dens in hamlets:
        for e in P.edge_list:
            if e["kind"] == "highway":
                continue
            S = e["s"]
            d = np.hypot(e["pts"][:, 0] - hx, e["pts"][:, 1] - hz)
            inside = np.where(d < hr)[0]
            if len(inside) < 4:
                continue
            a, b = max(S[inside[0]], 10.0), min(S[inside[-1]], S[-1] - 10.0)
            for side in (1, -1):
                pool = CHALETS + (["chalet_f", "alpage_hut", "cowshed", "hayloft"] if hz < -1000 and hx < 0 else FARM[:2] + ["chalet_f"])
                P.row(e, a, b, side, lambda pool=pool: rng.choice(pool), setback=rng.uniform(4.0, 9.0),
                      gap=(8.0 / dens, 22.0 / dens), max_drop=5.5, turn_jitter=8.0)
    P.beside("chapel", "alpage", 0.93, 1, setback=12.0, max_drop=6.0)

    # ---------------- Fermes isolées le long des routes de la vallée et des versants
    for e in P.edge_list:
        if e["kind"] not in ("main", "lane", "mountain"):
            continue
        s = rng.uniform(80.0, 260.0)
        while s < e["s"][-1] - 60.0:
            x, z, h, t, n = P.frame(e, s)
            if h < 330.0 and rng.random() < (0.75 if e["kind"] != "mountain" else 0.35):
                side = rng.choice((1, -1))
                setback = rng.uniform(10.0, 26.0)
                if P.beside(rng.choice(CHALETS + ["chalet_f"]), e["id"], s, side, setback=setback, max_drop=4.5, turn=rng.uniform(-12, 12)):
                    if rng.random() < 0.8:
                        P.beside(rng.choice(FARM), e["id"], s + rng.uniform(16.0, 24.0), side, setback=setback + rng.uniform(2.0, 10.0),
                                 max_drop=4.5, turn=rng.choice((0, 90, -90)) + rng.uniform(-8, 8))
                    if rng.random() < 0.5:
                        P.beside("mazot", e["id"], s - rng.uniform(12.0, 18.0), side, setback=setback + rng.uniform(0.0, 8.0), max_drop=4.0,
                                 turn=rng.uniform(-20, 20))
            s += rng.uniform(170.0, 420.0)

    # ---------------- Lac : hangar à bateaux, pontons, bateaux
    lake_side(P, rng)

    # ---------------- Barrage, bout de la vallée
    (ax, az), (bx, bz) = dam
    P.add("reservoir_hut", ax - 14.0, az + 10.0, 150.0)
    P.add("info_board", bx + 10.0, bz - 6.0, 200.0)
    P.add("picnic_table", bx + 16.0, bz - 14.0, 40.0)
    if "est" in P.nodes:
        n = P.nodes["est"]
        e = P.edges["gorge"]
        _, _, _, t, _ = P.frame(e, e["s"][-1] - 2.0)
        P.add("tunnel_portal", n["x"] + t[0] * 9.0, n["z"] + t[1] * 9.0, yaw_of(-t[0], -t[1]), y=n["y"])
        P.add("road_barrier_closed", n["x"] + t[0] * 5.0, n["z"] + t[1] * 5.0, yaw_of(-t[0], -t[1]), y=n["y"])
    # Tour en ruine sur l'éperon au-dessus de la ville, croix et pylône radio sur les hauteurs
    P.building("tower_ruin", -1010.0, 120.0, 160.0, max_drop=14.0, clear=0.0)
    P.add("summit_cross", 320.0, 760.0, 200.0)
    P.add("radio_tower", 2100.0, 2420.0, 0.0)

    furniture(P, rng, near_town)
    return P


def cable(P, start, end, station, pylon, cabin, top_station, span, top, start_h, every=60.0):
    """Remontée mécanique : pylônes en ligne droite, cabines ou sièges pendus au câble (origine à la pince)."""
    x0, z0 = start
    x1, z1 = end
    Ltot = math.hypot(x1 - x0, z1 - z0)
    ux, uz = (x1 - x0) / Ltot, (z1 - z0) / Ltot
    line_yaw = yaw_of(ux, uz)
    # les câbles quittent la gare du bas par l'arrière du modèle
    P.building(station, x0, z0, line_yaw + 180.0, max_drop=9.0, clear=0.0, force=True)
    pts = [(0.0, P.h(x0, z0) + start_h)]
    d = span * 0.6
    while d < Ltot - span * 0.4:
        x, z = x0 + ux * d, z0 + uz * d
        if P.cell(P.edge_map, x, z) > 6.0 and not P.cell(P.water, x, z):
            P.add(pylon, x, z, line_yaw)
            P.claim(Placer.corners(x, z, 5, 5, 0), x, z, 1.5)
            pts.append((d, P.h(x, z) + top))
        d += span
    if top_station:
        P.building(top_station, x1, z1, line_yaw, max_drop=12.0, clear=0.0, force=True)
        pts.append((Ltot, P.h(x1, z1) + 10.0))
    else:
        pts.append((Ltot, P.h(x1, z1) + top))
    ds = [p[0] for p in pts]
    hs = [p[1] for p in pts]
    d = every * 0.5
    side = 1.0
    off = 3.0 if "cablecar" in pylon else 2.2
    while d < Ltot - 10.0:
        y = float(np.interp(d, ds, hs)) - 0.6
        x, z = x0 + ux * d - uz * off * side, z0 + uz * d + ux * off * side
        P.add(cabin, x, z, line_yaw if side > 0 else line_yaw + 180.0, y=y, flags=INSTANCED)
        side = -side
        d += every * 0.5


def lake_side(P, rng):
    """Rive du lac : cherche le bord de l'eau depuis quelques points de la rive nord et y pose pontons et bateaux."""
    spots = [(1260.0, 420.0, "boathouse"), (1420.0, 440.0, "pier"), (1700.0, 500.0, "pier"), (2180.0, 380.0, "pier"), (1010.0, 150.0, "pier")]
    for (x, z, what) in spots:
        # descend vers le lac jusqu'à l'eau
        dx, dz = 1520.0 - x, 30.0 - z
        n = math.hypot(dx, dz)
        dx, dz = dx / n, dz / n
        d = 0.0
        while d < 300.0 and not P.cell(P.water, x + dx * d, z + dz * d):
            d += 2.0
        if d >= 300.0:
            continue
        wx, wz = x + dx * (d + 2.0), z + dz * (d + 2.0)
        yaw = yaw_of(dx, dz)
        if what == "boathouse":
            P.add("boathouse", wx, wz, yaw + 180.0, y=0.0)
            P.claim(Placer.corners(wx, wz, 10.0, 14.0, yaw), wx, wz, 0.0)
        else:
            # deux modules bout à bout (module de 6 m le long de leur axe X), sans recouvrement
            for d6 in (3.0, 9.0):
                P.add("pier", wx + dx * d6, wz + dz * d6, yaw + 90.0, y=0.0)
            P.claim(Placer.corners(wx + dx * 6.0, wz + dz * 6.0, 4.0, 13.0, yaw), wx, wz, 0.0)
        for k in range(rng.randint(1, 3)):
            a = rng.uniform(0, math.tau)
            bx, bz = wx + dx * rng.uniform(10.0, 22.0) + math.cos(a) * 6.0, wz + dz * rng.uniform(10.0, 22.0) + math.sin(a) * 6.0
            boat = rng.choice(["rowboat", "pedalo", "rowboat"])
            hull = Placer.corners(bx, bz, 5.6, 5.6, 0.0)
            if P.cell(P.water, bx, bz) and P.free(hull, bx, bz, 6.0):
                P.add(boat, bx, bz, rng.uniform(0, 360), y=0.0)
                P.claim(hull, bx, bz, 0.0)
    for k in range(7):
        x, z = 1520.0 + rng.uniform(-380.0, 380.0), 30.0 + rng.uniform(-170.0, 170.0)
        if P.cell(P.water, x, z):
            P.add("sailboat" if k < 4 else "buoy", x, z, rng.uniform(0, 360), y=0.0)
    # Roseaux et nénuphars le long des rives
    for k in range(260):
        a = rng.uniform(0, math.tau)
        x, z = 1520.0 + math.cos(a) * rng.uniform(300.0, 700.0), 30.0 + math.sin(a) * rng.uniform(150.0, 380.0)
        if P.cell(P.water, x, z) and P.h(x, z) > -1.4:
            reed = rng.random() < 0.75
            P.add("reeds" if reed else "lily_pads", x, z, rng.uniform(0, 360), y=0.0 if reed else 0.04, flags=INSTANCED)


def furniture(P, rng, near_town):
    """Mobilier et signalisation le long des routes."""
    for e in P.edge_list:
        S = e["s"]
        Ltot = S[-1]
        hw, sw = e["hw"], e["sidewalk"]
        curv = L.curvature(e["pts"], S[1] - S[0]) if len(S) > 3 else np.zeros(len(S))
        kabs = L.gauss1d(np.abs(curv), 2.0)

        def bridge(s):
            return any(s0 - 3 <= s <= s1 + 3 for (s0, s1, k) in e["spans"])

        if e["kind"] == "street" or (e["id"].startswith("vallee") and near_town(e, Ltot / 2, 520.0)):
            # Lanternes en quinconce, bancs, jardinières, corbeilles
            off = hw + sw - 0.55 if sw > 0 else hw + 1.0
            s, k = 14.0, 0
            while s < Ltot - 12.0:
                side = 1 if k % 2 == 0 else -1
                if not bridge(s):
                    P.roadside("lamp_alpine", e, s, side, off, dy=0.14 if sw > 0 else 0.0)
                    r = rng.random()
                    if sw > 0 and r < 0.5:
                        P.roadside(rng.choice(["bench", "planter", "flower_pot", "bin", "planter", "bike_rack", "mailbox", "hydrant"]),
                                   e, s + 7.0, -side, hw + sw - 0.6, face="road", dy=0.14)
                k += 1
                s += 26.0
            if sw > 0 and Ltot > 150.0:
                P.roadside("busstop", e, Ltot * 0.5, 1, hw + sw + 1.2, face="road", avoid=True)
        elif e["kind"] in ("main", "mountain", "lane"):
            # Balises (ou jalons à neige en altitude) et bornes
            s, k = 10.0, 0
            step = 28.0
            while s < Ltot - 8.0:
                x, z, h, t, n = P.frame(e, s)
                if not bridge(s) and e["kind"] != "lane":
                    item = "snow_pole" if h > 250.0 else "delineator"
                    for side in (1, -1):
                        P.roadside(item, e, s, side, hw + 0.75, flags=INSTANCED | DETAIL)
                if e["kind"] == "main" and k % 18 == 9:
                    P.roadside("milestone", e, s + 4.0, -1, hw + 1.3)
                k += 1
                s += step
            # Lampadaires routiers aux carrefours
            for (ss, node) in ((16.0, e["a"]), (Ltot - 16.0, e["b"])):
                if e["kind"] == "main" and len(P.nodes[node]["edges"]) >= 3 and Ltot > 60.0:
                    P.roadside("lamp_road", e, ss, -1, hw + 1.6, face="road")
            # Virages : balises de virage à l'extérieur des épingles, panneaux avant
            i = 0
            n_pts = len(S)
            while i < n_pts:
                if kabs[i] > 1.0 / 40.0:
                    j = i
                    while j < n_pts - 1 and kabs[j] > 1.0 / 55.0:
                        j += 1
                    peak = float(kabs[i:j + 1].max())
                    mid = (i + j) // 2
                    sign = 1.0 if curv[mid] > 0 else -1.0          # courbure positive = virage à gauche
                    if peak > 1.0 / 30.0:
                        for q in range(i, j + 1, 3):
                            if not bridge(S[q]):
                                P.roadside("chevron", e, S[q], -sign, hw + 1.1, face="road")
                        item = "sign_hairpin" if peak > 1.0 / 22.0 else ("sign_curve_l" if sign > 0 else "sign_curve_r")
                        if S[i] > 70.0:
                            P.roadside(item, e, S[i] - 55.0, -1, hw + 1.2, face="against")
                        other = "sign_hairpin" if peak > 1.0 / 22.0 else ("sign_curve_r" if sign > 0 else "sign_curve_l")
                        if S[j] < Ltot - 70.0:
                            P.roadside(other, e, S[j] + 55.0, 1, hw + 1.2, face="along")
                    i = j + 1
                else:
                    i += 1
            # Limitations et panneaux divers
            if e["kind"] == "main" and Ltot > 300.0:
                P.roadside("sign_speed80", e, 60.0, -1, hw + 1.2, face="against")
                P.roadside("sign_speed80", e, Ltot - 60.0, 1, hw + 1.2, face="along")
            if e["kind"] == "mountain" and Ltot > 600.0:
                P.roadside("sign_steep", e, 40.0, -1, hw + 1.2, face="against")
                P.roadside("sign_rockfall", e, Ltot * 0.5, -1, hw + 1.2, face="against")
                P.roadside("sign_steep", e, Ltot - 40.0, 1, hw + 1.2, face="along")
            if e["kind"] == "lane" and Ltot > 500.0:
                P.roadside("sign_cattle", e, 50.0, -1, hw + 1.2, face="against")
                P.roadside("sign_cattle", e, Ltot - 50.0, 1, hw + 1.2, face="along")
    # Entrées de ville
    for (eid, s, side, face) in (("vallee_3", -90.0, -1, "against"), ("vallee_9", 90.0, 1, "along"), ("col", 120.0, 1, "along"),
                                 ("vallee_3", -70.0, -1, "against"), ("vallee_9", 70.0, 1, "along")):
        if eid in P.edges:
            e = P.edges[eid]
            ss = s if s > 0 else e["s"][-1] + s
            item = "sign_town" if abs(s) > 80 or eid == "col" else "sign_speed50"
            P.roadside(item, e, ss, side, e["hw"] + 1.3, face=face)
    # Panneaux de direction aux grands carrefours
    for name in ("lac_o", "lac_e", "sud_j", "ville_n", "col", "station", "barrage_o", "pont_alpage", "balcon_j"):
        if name in P.nodes:
            n = P.nodes[name]
            P.add("sign_direction", n["x"] + 11.0, n["z"] + 9.0, rng.uniform(0, 360))
    # Bottes de foin dans les prés du fond de vallée
    for k in range(60):
        x, z = rng.uniform(-2400.0, 900.0), rng.uniform(-650.0, -60.0)
        if P.cell(P.edge_map, x, z) < 14.0 or P.cell(P.water, x, z) or P.cell(P.blocked, x, z) or P.cell(P.river, x, z):
            continue
        for q in range(rng.randint(3, 8)):
            P.add("haybale_round", x + rng.uniform(-18, 18), z + rng.uniform(-18, 18), rng.uniform(0, 360), flags=INSTANCED)
    # Ligne à haute tension qui traverse la vallée, du barrage vers l'ouest
    x, z = 600.0, -1300.0
    for k in range(9):
        if P.cell(P.edge_map, x, z) > 8.0 and not P.cell(P.water, x, z) and not P.cell(P.blocked, x, z):
            P.add("power_pylon", x, z, yaw_of(-330.0, 60.0))
            P.claim(Placer.corners(x, z, 8, 8, 0), x, z, 1.0)
        x, z = x - 330.0, z + 60.0
