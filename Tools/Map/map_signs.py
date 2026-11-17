"""Signalisation de la carte : cédez-le-passage et sens interdits aux carrefours, panneaux et balises de virage,
balises de bord de route, bornes, panneaux de col. Modèles du pack alpin (catégorie « sign »).

Chaque objet est un dict (item, x, y, z, yaw, scale, flags) lu par Unity (MapProps). Face des panneaux = +Z ; yaw en
degrés autour de Y. Côté : +1 = à gauche de l'arête dans son sens a → b, -1 = à droite.
"""
import json
import math
import os
import numpy as np
import map_lib as L
import map_roads as R

INSTANCED, DETAIL = 1, 2
MANIFEST = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "ChillRoad",
                                         "Models", "AlpPack", "AlpPack_manifest.json"))


def yaw_of(dx, dz):
    return math.degrees(math.atan2(dx, dz))


def known_items():
    try:
        d = json.load(open(MANIFEST, encoding="utf-8"))
    except OSError:
        return None
    items = d["items"] if isinstance(d, dict) and "items" in d else d
    return set(items.keys()) if isinstance(items, dict) else set(it["id"] for it in items)


class Signs:
    def __init__(self, g, H, nodes, edges):
        self.g, self.H = g, H
        self.nodes, self.edges = nodes, edges
        self.by_id = {e["id"]: e for e in edges}
        self.places = []
        self.known = known_items()

    def frame(self, e, s):
        """Point de l'axe à l'abscisse s : (x, z, h, tangente, normale à gauche)."""
        S, P = e["s"], e["pts"]
        s = min(max(s, 0.0), S[-1])
        i = int(min(max(np.searchsorted(S, s), 1), len(S) - 1))
        t = (s - S[i - 1]) / max(S[i] - S[i - 1], 1e-6)
        q = P[i - 1] + (P[i] - P[i - 1]) * t
        d = P[min(i + 1, len(P) - 1)] - P[max(i - 2, 0)]
        n = math.hypot(d[0], d[1]) or 1.0
        tx, tz = d[0] / n, d[1] / n
        return q[0], q[1], q[2], (tx, tz), (-tz, tx)

    def covered(self, e, s, margin=4.0):
        """Sur un pont ou dans un tunnel ?"""
        return any(s0 - margin <= s <= s1 + margin for (s0, s1, _) in e["spans"])

    def add(self, item, x, z, yaw, y=None, flags=0):
        if self.known is not None and item not in self.known:
            return False
        if y is None:
            y = float(self.g.sample(self.H, x, z))
        self.places.append(dict(item=item, x=round(float(x), 2), y=round(float(y), 2), z=round(float(z), 2),
                                yaw=round(yaw % 360.0, 1), scale=1.0, flags=int(flags)))
        return True

    def roadside(self, item, e, s, side, offset, face="road", flags=0):
        """Objet au bord de la chaussée. face : 'road' (regarde la route), 'along' (dans le sens a → b),
        'against' (regarde les véhicules qui arrivent de a)."""
        if self.covered(e, s):
            return False
        x, z, h, t, n = self.frame(e, s)
        px, pz = x + n[0] * side * offset, z + n[1] * side * offset
        if face == "road":
            yaw = yaw_of(-n[0] * side, -n[1] * side)
        elif face == "along":
            yaw = yaw_of(t[0], t[1])
        else:
            yaw = yaw_of(-t[0], -t[1])
        # au pied de l'accotement : jamais au-dessus de la chaussée, jamais enterré
        y = min(float(self.g.sample(self.H, px, pz)), h)
        return self.add(item, px, pz, yaw, y=y, flags=flags)

    # ------------------------------------------------------------------ carrefours
    def arms(self, n):
        out = []
        for eid in n["edges"]:
            e = self.by_id[eid]
            for end in ("a", "b"):
                if e[end] != n["id"]:
                    continue
                P = e["pts"] if end == "a" else e["pts"][::-1]
                if math.hypot(P[0, 0] - n["x"], P[0, 1] - n["z"]) > 3.0:
                    continue
                i = min(6, len(P) - 1)
                d = math.hypot(P[i, 0] - P[0, 0], P[i, 1] - P[0, 1]) or 1.0
                out.append(dict(e=e, end=end, dir=((P[i, 0] - P[0, 0]) / d, (P[i, 1] - P[0, 1]) / d), w=e["hw"]))
        return out

    def junctions(self):
        for n in self.nodes.values():
            if n.get("merge"):
                continue
            arms = self.arms(n)
            if len(arms) < 3:
                continue
            # route principale : la paire de branches la plus alignée, à égalité la plus large (même règle que
            # RoadNetwork.GiveWay)
            best, pair = 1e9, None
            for i in range(len(arms)):
                for j in range(i + 1, len(arms)):
                    dot = arms[i]["dir"][0] * arms[j]["dir"][0] + arms[i]["dir"][1] * arms[j]["dir"][1]
                    score = dot - 0.05 * (arms[i]["w"] + arms[j]["w"])
                    if score < best:
                        best, pair = score, (i, j, dot)
            through = pair is not None and pair[2] < -0.7
            for k, a in enumerate(arms):
                e, end = a["e"], a["end"]
                Ltot = e["s"][-1]
                leaving = e.get("oneway") and end == "a"          # bretelle qui part du carrefour
                arriving = e.get("oneway") and end == "b"
                s = 13.0 if end == "a" else Ltot - 13.0
                if arriving:
                    # sens interdit des deux côtés, face au carrefour
                    for side in (1, -1):
                        self.roadside("sign_noentry", e, Ltot - 9.0, side, e["hw"] + 1.1, face="along")
                if e["kind"] == "highway" or leaving or Ltot < 40.0:
                    continue
                if through and k not in pair[:2]:
                    # cédez-le-passage à droite de la voie qui arrive, face aux conducteurs
                    if end == "a":
                        self.roadside("sign_yield", e, s, 1, e["hw"] + 1.3, face="along")
                    else:
                        self.roadside("sign_yield", e, s, -1, e["hw"] + 1.3, face="against")

    # ------------------------------------------------------------------ le long des routes
    def along(self):
        for e in self.edges:
            kind = e["kind"]
            if kind not in ("main", "mountain", "lane"):
                continue
            S, P = e["s"], e["pts"]
            Ltot = S[-1]
            hw = e["hw"]
            # Balises (jalons à neige en altitude) : pas aux abords des carrefours
            if kind != "lane":
                s, k = 26.0, 0
                while s < Ltot - 26.0:
                    x, z, h, t, n = self.frame(e, s)
                    item = "snow_pole" if h > 300.0 else "delineator"
                    if any(math.hypot(x - vx, z - vz) < vr for (vx, vz, vr) in getattr(self, "villages", ())):
                        k += 1
                        s += 30.0
                        continue
                    for side in (1, -1):
                        self.roadside(item, e, s, side, hw + 0.8, flags=INSTANCED | DETAIL)
                    if kind == "main" and k % 20 == 10:
                        self.roadside("milestone", e, s + 5.0, -1, hw + 1.3)
                    k += 1
                    s += 30.0
            # Virages : balises à l'extérieur des épingles, panneau avant le virage dans chaque sens
            curv = L.curvature(P, R.DS)
            kabs = np.abs(curv)
            i, n_pts = 0, len(S)
            while i < n_pts:
                if kabs[i] > 1.0 / 40.0:
                    j = i
                    while j < n_pts - 1 and kabs[j] > 1.0 / 55.0:
                        j += 1
                    peak = float(kabs[i:j + 1].max())
                    mid = (i + j) // 2
                    sign = 1.0 if curv[mid] > 0 else -1.0          # courbure positive = virage à gauche
                    if peak > 1.0 / 30.0 and S[i] > 30.0 and S[j] < Ltot - 30.0:
                        for q in range(i, j + 1, 3):
                            self.roadside("chevron", e, S[q], -sign, hw + 1.2, face="road", flags=INSTANCED)
                        hair = peak > 1.0 / 22.0
                        if S[i] > 90.0:
                            self.roadside("sign_hairpin" if hair else ("sign_curve_l" if sign > 0 else "sign_curve_r"),
                                          e, S[i] - 50.0, -1, hw + 1.3, face="against")
                        if S[j] < Ltot - 90.0:
                            self.roadside("sign_hairpin" if hair else ("sign_curve_r" if sign > 0 else "sign_curve_l"),
                                          e, S[j] + 50.0, 1, hw + 1.3, face="along")
                    i = j + 1
                else:
                    i += 1

    def passes(self, names=("col", "col_ne")):
        for name in names:
            n = self.nodes.get(name)
            if not n:
                continue
            arms = self.arms(n)
            if len(arms) >= 3:
                # dans l'angle entre deux branches, tourné vers le carrefour
                a = sorted(arms, key=lambda q: math.atan2(q["dir"][1], q["dir"][0]))
                d0, d1 = a[0]["dir"], a[1]["dir"]
                bx, bz = d0[0] + d1[0], d0[1] + d1[1]
                m = math.hypot(bx, bz) or 1.0
                x, z = n["x"] + bx / m * 13.0, n["z"] + bz / m * 13.0
                self.add("pass_sign", x, z, yaw_of(-bx, -bz))
            elif arms:
                e, end = arms[0]["e"], arms[0]["end"]
                self.roadside("pass_sign", e, 8.0 if end == "a" else e["s"][-1] - 8.0, -1, e["hw"] + 2.2, face="road")


    # ------------------------------------------------------------------ échangeurs
    def motorway(self):
        for e in self.edges:
            Ltot = e["s"][-1]
            hw = e["hw"]
            if e["kind"] == "highway":
                # annonce de sortie, 120 m avant le début de la voie de décélération, au bord droit de la chaussée
                for (sa, sb, side, width) in e.get("aux", ()):
                    exit_lane = (side > 0 and sa > Ltot * 0.5) or (side < 0 and sa < Ltot * 0.5)
                    if not exit_lane:
                        continue
                    back = abs(sb - sa) + 120.0
                    if side > 0:
                        self.roadside("sign_exit", e, sa - back, -1, hw + 2.2, face="against")
                    else:
                        self.roadside("sign_exit", e, sa + back, 1, hw + 2.2, face="along")
            elif e["kind"] == "ramp":
                at = e["opts"].get("attach", {}).get("end")
                if at == "a" and Ltot > 120.0:
                    # balise de sortie dans le musoir (l'autoroute est à gauche de la bretelle)
                    self.roadside("sign_gore", e, 78.0, 1, hw + 1.3, face="against")
                elif at == "b" and Ltot > 80.0:
                    # entrée d'autoroute, à droite au départ de la bretelle
                    self.roadside("sign_motorway", e, 34.0, -1, hw + 1.3, face="against")


def build(g, H, nodes, edges, villages=()):
    """Renvoie la liste des objets de signalisation. villages : [(x, z, rayon)] où l'on ne pose ni balises ni bornes."""
    sg = Signs(g, H, nodes, edges)
    sg.villages = list(villages)
    sg.junctions()
    sg.motorway()
    sg.along()
    sg.passes()
    return sg.places
