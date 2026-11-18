"""Radars de vitesse : tourelles posées au bord des routes (modèle « speed_camera » du pack alpin,
Tools/Blender/alp_radar.py).

Un radar contrôle toute la largeur de la route, dans les deux sens. Le jeu (SpeedCameras.cs) le déclenche quand la
voiture passe à « target » km/h ou plus : flash, et la prime « reward » la première fois.

Un site est donné par un point de l'axe de la route et un cap approximatif : l'arête et l'abscisse sont retrouvées sur
le réseau, le radar se pose à droite de ce cap. Les seuils tiennent compte de la vitesse que la route permet d'atteindre
(lignes droites, virages avant le radar) : du premier, à la portée de la voiture de départ, au dernier, sur l'autoroute.
"""
import math
import numpy as np

ITEM = "speed_camera"
SHOULDER = 2.6           # entre le bord de la chaussée et le mât
GATE = 4.0               # marge de la ligne de contrôle au-delà du bord de la chaussée
FLASH_H = 4.06           # hauteur des lampes au-dessus du pied (comme alp_radar.FLASH_Z)
CLEAR = 3.5              # rayon autour du mât sans autre objet ni végétation
MAX_DROP = 1.0           # écart toléré entre le sol au pied du mât et la chaussée

# id, noms, point de l'axe (x, z), cap (dx, dz), seuil (km/h), prime ($)
SITES = [
    ("plan", "Radar du Plan", "Le Plan Speed Camera", (-320.5, -1784.0), (0.76, -0.66), 40.0, 300),
    ("bois", "Radar du Bois", "Woods Speed Camera", (735.4, -2214.9), (1.0, 0.1), 70.0, 600),
    ("hauts", "Radar des Hauts", "Les Hauts Speed Camera", (824.4, -982.7), (0.04, -1.0), 90.0, 1000),
    ("col", "Radar du Col", "Pass Speed Camera", (53.6, 109.6), (0.17, 0.99), 110.0, 1500),
    ("combes", "Radar des Combes", "Les Combes Speed Camera", (-1567.9, 409.8), (-0.2, 0.98), 120.0, 2000),
    ("envers", "Radar de l'Envers", "Envers Speed Camera", (1639.7, -949.9), (-0.38, -0.92), 140.0, 3000),
    ("granges", "Radar des Granges", "Les Granges Speed Camera", (2071.5, 678.8), (0.2, 0.98), 150.0, 4000),
    ("petit_pas", "Radar du Petit Pas", "Petit Pas Speed Camera", (1081.8, 1764.9), (1.0, -0.02), 170.0, 5000),
    ("praz", "Radar de Praz", "Praz Speed Camera", (-744.4, 1772.5), (-1.0, 0.07), 200.0, 6500),
    ("villard", "Radar du Villard", "Le Villard Speed Camera", (-1869.3, -602.6), (-0.07, -1.0), 230.0, 8000),
    ("plaine", "Radar de la Plaine", "Lowland Speed Camera", (752.6, -2079.0), (0.99, 0.14), 260.0, 10000),
    ("ouest", "Radar de l'Autoroute", "Motorway Speed Camera", (-2100.0, -269.1), (0.3, -0.95), 320.0, 15000),
]


def _tangent(P, i):
    a, b = P[max(i - 2, 0)], P[min(i + 2, len(P) - 1)]
    tx, tz = b[0] - a[0], b[1] - a[1]
    n = math.hypot(tx, tz) or 1.0
    return tx / n, tz / n


def resolve(edges):
    """Sites retrouvés sur le réseau : axe de la route (cx, cy, cz), cap (tx, tz), pied du mât (px, pz)."""
    out = []
    for (rid, fr, en, (x, z), (hx, hz), target, reward) in SITES:
        best = None
        for e in edges:
            if e["kind"] == "ramp":
                continue
            P = e["pts"]
            d = np.hypot(P[:, 0] - x, P[:, 1] - z)
            i = int(d.argmin())
            if best is None or d[i] < best[0]:
                best = (d[i], e, i)
        dist, e, i = best
        if dist > 12.0:
            raise ValueError("radar %s : route introuvable (%.1f m)" % (rid, dist))
        P = e["pts"]
        s = e["s"][i]
        for (s0, s1, kind) in e["spans"]:
            if s0 - 30.0 < s < s1 + 30.0:
                raise ValueError("radar %s : sur un ouvrage (%s)" % (rid, kind))
        tx, tz = _tangent(P, i)
        if tx * hx + tz * hz < 0.0:
            tx, tz = -tx, -tz
        edge_w = e["hw"] + e["sidewalk"]
        off = edge_w + SHOULDER
        rx, rz = tz, -tx                               # droite du cap
        out.append(dict(id=rid, fr=fr, en=en, target=target, reward=reward, edge=e, cx=float(P[i, 0]), cz=float(P[i, 1]),
                        cy=float(P[i, 2]), tx=tx, tz=tz, px=float(P[i, 0]) + rx * off, pz=float(P[i, 1]) + rz * off,
                        half=edge_w + GATE))
    return out


def populate(P, sites):
    """Pose les radars (objets de la carte) ; ni arbres ni buissons autour du mât."""
    from map_place import yaw_of
    for f in sites:
        r = CLEAR
        cs = [(f["px"] + a, f["pz"] + b) for (a, b) in ((-r, -r), (r, -r), (r, r), (-r, r))]
        P.claim(cs, f["px"], f["pz"], 0.5)
        f["y"] = P.h(f["px"], f["pz"])
        if abs(f["y"] - f["cy"]) > MAX_DROP:
            raise ValueError("radar %s : sol à %.2f m de la chaussée" % (f["id"], f["y"] - f["cy"]))
        f["yaw"] = yaw_of(f["tx"], f["tz"]) % 360.0
        P.add(ITEM, f["px"], f["pz"], f["yaw"], y=f["y"])
    return sites


def clear(places, sites):
    """Retire de la liste les objets (panneaux, bornes, arbres) posés contre un mât."""
    def near(p):
        return any(math.hypot(p["x"] - f["px"], p["z"] - f["pz"]) < CLEAR for f in sites)
    keep = [p for p in places if p["item"] == ITEM or not near(p)]
    return keep, len(places) - len(keep)


def export(sites):
    """Radars pour le jeu (map.json) : axe de la route (x, y, z), cap, demi-largeur contrôlée, lampes, seuil, prime."""
    out = []
    for f in sites:
        out.append(dict(id=f["id"], fr=f["fr"], en=f["en"], x=round(f["cx"], 2), y=round(f["cy"], 2), z=round(f["cz"], 2),
                        yaw=round(f["yaw"], 1), halfWidth=round(f["half"], 2), flashX=round(f["px"], 2),
                        flashY=round(f["y"] + FLASH_H, 2), flashZ=round(f["pz"], 2), target=float(f["target"]),
                        reward=int(f["reward"])))
    return out
