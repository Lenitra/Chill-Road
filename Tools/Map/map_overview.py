"""Carte globale affichée en jeu (touche « , ») : vue de dessus stylisée, aplats de couleur comme le reste du jeu.

Prés, forêts, roche, neige, eau, relief ombré léger, courbes de niveau discrètes, routes par type, bâtiments.
Aucun texte dans l'image : les noms de lieux sont posés par le jeu (deux langues).

La minicarte du HUD (map_minimap.png, niveaux de gris) est dessinée par map_minimap.py, appelé ici à chaque bake.
`from_export()` relit les données exportées dans Assets/NoHesi/Resources/Map (utile sans bake).
"""
import json
import math
import os
import sys
import types
import numpy as np
from PIL import Image, ImageDraw
import map_lib as L

SIZE = 2048          # image finale (carrée, couvre tout le relief)
SS = 2               # suréchantillonnage du tracé des routes (anticrénelage)

MEADOW = np.array([104, 152, 78], dtype=np.float64)
FOREST = np.array([52, 96, 62], dtype=np.float64)
DRY = np.array([156, 158, 98], dtype=np.float64)
ROCK = np.array([128, 130, 134], dtype=np.float64)
SNOW = np.array([238, 242, 248], dtype=np.float64)
PAVED = np.array([150, 146, 138], dtype=np.float64)
WATER = np.array([72, 138, 196], dtype=np.float64)

ROADS = {      # type : (largeur dessinée en mètres, couleur, liseré)
    "main": (26.0, (252, 250, 240), (40, 44, 52)),
    "mountain": (20.0, (255, 214, 110), (60, 48, 24)),
    "street": (13.0, (250, 250, 250), (60, 62, 68)),
    "lane": (12.0, (236, 232, 220), (60, 62, 68)),
    "track": (9.0, (188, 150, 106), (70, 52, 34)),
    "highway": (46.0, (255, 150, 80), (70, 34, 16)),
    "ramp": (11.0, (255, 176, 120), (70, 34, 16)),
}

# Palette de la carte globale (la minicarte a son propre rendu en niveaux de gris : map_minimap.py)
PALETTES = {
    "overview": dict(meadow=MEADOW, forest=FOREST, dry=DRY, rock=ROCK, snow=SNOW, paved=PAVED, water=WATER,
                     shore=(214, 232, 244), building=(122, 66, 52), roads=ROADS, shade=(0.62, 0.55), contour=0.9),
}


def render(g, H, rgba, paved, snow, water, edges, rivers, buildings, out_path, palette="overview"):
    pal = PALETTES[palette]
    n = g.n
    f = rgba[..., 0][..., None] / 255.0
    dry = rgba[..., 1][..., None] / 255.0
    rock = rgba[..., 2][..., None] / 255.0
    meadow, forest_c, dry_c, rock_c, snow_c, paved_c, water_c = (
        np.array(pal[k], dtype=np.float64) for k in ("meadow", "forest", "dry", "rock", "snow", "paved", "water"))
    col = np.broadcast_to(meadow, (n, n, 3)).copy()
    col = col * (1 - np.clip(dry * 1.2, 0, 1)) + dry_c * np.clip(dry * 1.2, 0, 1)
    fm = (f > 0.45).astype(np.float64)
    col = col * (1 - fm) + forest_c * fm
    rm = (rock > 0.5).astype(np.float64)
    col = col * (1 - rm) + rock_c * rm
    pm = (paved[..., None] / 255.0 > 0.5).astype(np.float64)
    col = col * (1 - pm) + paved_c * pm
    sm = (snow[..., None] / 255.0 > 0.45).astype(np.float64)
    col = col * (1 - sm) + snow_c * sm
    # relief : ombrage doux, courbes de niveau tous les 100 m
    shade = L.hillshade(L.blur2d(H, 1.5), g.step, az=315.0, alt=50.0)
    base, amount = pal["shade"]
    col *= (base + amount * shade)[..., None]
    c = np.floor(H / 100.0)
    line = np.zeros((n, n), dtype=bool)
    line[:-1, :] |= c[:-1, :] != c[1:, :]
    line[:, :-1] |= c[:, :-1] != c[:, 1:]
    col[line] *= pal["contour"]
    wm = water > 0
    col[wm] = water_c
    # rive : liseré clair
    shore = np.zeros((n, n), dtype=bool)
    shore[1:, :] |= wm[1:, :] != wm[:-1, :]
    shore[:, 1:] |= wm[:, 1:] != wm[:, :-1]
    col[shore] = pal["shore"]

    img = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)[::-1])        # nord en haut
    big = SIZE * SS
    img = img.resize((big, big), Image.BICUBIC)
    dr = ImageDraw.Draw(img)
    k = big / (2 * g.half)

    def px(x, z):
        return ((x + g.half) * k, (g.half - z) * k)

    # traits épais sans entailles (segments + disques) : même tracé que la minicarte
    from map_minimap import stroke
    for rv in rivers:
        p = rv["pts"]
        w = max(2, int(float(p[0][3]) * 1.6 * k))
        stroke(dr, [px(a[0], a[1]) for a in p], w, tuple(int(v) for v in water_c))
    # bâtiments sous les routes : les rues restent lisibles
    for b in buildings:
        dr.polygon([px(q[0], q[1]) for q in b], fill=tuple(pal["building"]))
    order = ["track", "lane", "mountain", "main", "ramp", "highway"]
    for outline in (True, False):
        for kind in order:
            w_m, fill, edge_col = pal["roads"][kind]
            for e in edges:
                if e["kind"] != kind:
                    continue
                w = max(2, int(round(w_m * k)))
                # en tunnel la route ne se voit pas : seulement un pointillé discret
                for (seg, tunnel) in pieces(e):
                    pts = [px(a[0], a[1]) for a in seg]
                    if len(pts) < 2:
                        continue
                    if tunnel:
                        if not outline:
                            for j in range(0, len(pts) - 6, 12):
                                dr.line(pts[j:j + 6], fill=edge_col, width=max(2, w // 3))
                    elif outline:
                        stroke(dr, pts, w + max(2, int(8 * k)), edge_col)
                    else:
                        stroke(dr, pts, w, fill)
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path)
    # Au bake, la minicarte suit la carte globale
    if palette == "overview" and os.path.basename(out_path) == "map_overview.png":
        import map_minimap
        map_minimap.render(g, H, rgba, snow, water, edges, rivers, os.path.join(os.path.dirname(out_path), "map_minimap.png"))
    return out_path


def pieces(e):
    """Découpe une arête en tronçons à l'air libre et en tunnel : [(points, en tunnel)]."""
    P = e["pts"]
    n = len(P)
    tun = np.zeros(n, dtype=bool)
    for (i0, i1) in e.get("tunnels", ()):
        tun[i0:i1 + 1] = True
    out, start = [], 0
    for i in range(1, n + 1):
        if i == n or tun[i] != tun[start]:
            out.append((P[start:min(i + 1, n)], bool(tun[start])))
            start = i
    return out


BUILDING_CATEGORIES = ("chalet", "townhouse", "landmark", "farm", "ski")


def from_export(res_dir):
    """Entrées de render() relues dans les fichiers exportés pour Unity (mêmes données que le jeu)."""
    with open(os.path.join(res_dir, "map.json"), encoding="utf-8") as fh:
        d = json.load(fh)
    n = d["n"]
    g = types.SimpleNamespace(n=n, step=d["step"], half=d["half"])
    raw = np.fromfile(os.path.join(res_dir, "map_heights.bytes"), dtype="<u2").reshape(n, n)
    H = d["hmin"] + raw.astype(np.float64) * (d["hmax"] - d["hmin"]) / 65535.0
    rgba = np.fromfile(os.path.join(res_dir, "map_masks.bytes"), dtype=np.uint8).reshape(n, n, 4)
    info = np.fromfile(os.path.join(res_dir, "map_info.bytes"), dtype=np.uint8).reshape(n, n, 4)
    paved, snow, water = info[..., 2], info[..., 3], info[..., 1] & 7
    # (x, y, z) à plat dans le JSON → (x, z, …) comme pendant le bake
    edges = []
    for e in d["edges"]:
        p = e["pts"]
        P3 = np.array(p, dtype=np.float64).reshape(-1, 3)
        s3 = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P3, axis=0), axis=1))])
        tunnels = [(int(np.searchsorted(s3, sp["s0"])), int(np.searchsorted(s3, sp["s1"]))) for sp in e.get("spans", [])
                   if sp["kind"] == "tunnel"]
        edges.append(dict(kind=e["kind"], pts=[(p[i], p[i + 2]) for i in range(0, len(p), 3)], tunnels=tunnels))
    rivers = [dict(pts=[(p[i], p[i + 2], p[i + 1], r["width"]) for i in range(0, len(p), 3)]) for r in d["rivers"] for p in [r["pts"]]]
    buildings = []
    items = {}
    if d["places"]:
        import map_manifest
        items = map_manifest.load()
    for pl in d["places"]:
        it = items.get(pl["item"])
        if not it or it["category"] not in BUILDING_CATEGORIES or it["footprint"][0] <= 3.0:
            continue
        w, dp = it["footprint"][0], it["footprint"][1]
        a = math.radians(pl["yaw"])
        rx, rz, fx, fz = math.cos(a), -math.sin(a), math.sin(a), math.cos(a)
        buildings.append([(pl["x"] + rx * sx * w / 2 + fx * sf * dp / 2, pl["z"] + rz * sx * w / 2 + fz * sf * dp / 2)
                          for sx, sf in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    return g, H, rgba, paved, snow, water, edges, rivers, buildings


if __name__ == "__main__":
    res = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "NoHesi", "Resources", "Map")
    res = os.path.normpath(res)
    if "--minimap" in sys.argv:
        import map_minimap
        out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(res, "map_minimap.png")
        g, H, rgba, paved, snow, water, edges, rivers, buildings = from_export(res)
        print(map_minimap.render(g, H, rgba, snow, water, edges, rivers, out))
    else:
        print(__doc__)
