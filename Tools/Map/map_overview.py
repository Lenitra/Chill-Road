"""Carte globale affichée en jeu (touche « , ») : vue de dessus stylisée, aplats de couleur comme le reste du jeu.

Teintes d'altitude en paliers (esprit low-poly), ombrage doux, courbes de niveau discrètes, forêts aux bords adoucis,
neige, eau avec un liseré de rive, routes par type (casing sombre + remplissage, tunnels en pointillés, ponts cerclés),
bâtiments. Loin des routes (hors de la zone où l'on roule) la carte s'éteint, puis se fond dans le fond du menu (BG).
Aucun texte dans l'image : les noms de lieux sont posés par le jeu (deux langues). Les couleurs des routes et de l'eau
sont reprises dans la légende de UI/WorldMap.cs : changer les deux ensemble.

La minicarte du HUD (map_minimap.png, niveaux de gris) est dessinée par map_minimap.py, appelé ici à chaque bake.
`from_export()` relit les données exportées dans Assets/ChillRoad/Resources/Map (utile sans bake).
"""
import json
import math
import os
import sys
import types
import numpy as np
from PIL import Image, ImageDraw
import map_lib as L

SIZE = 4096          # image finale (carrée, couvre tout le relief) : ~1,6 m par pixel, nette au zoom maximal
SS = 2               # suréchantillonnage du tracé des routes (anticrénelage)

def _roads(hw, ramp, main, mountain, lane, track):
    """Table des routes : type -> (largeur dessinée en mètres, remplissage, bord)."""
    return {"highway": (34.0,) + hw, "ramp": (12.0,) + ramp, "main": (22.0,) + main, "mountain": (18.0,) + mountain,
            "lane": (12.0,) + lane, "track": (9.0,) + track}


# Styles de la carte globale. hypso : teintes d'altitude (m au-dessus du plus bas point du relief) interpolées puis
# posées en paliers de `band` m ; shade : (base, amplitude) de l'ombrage, `toon` le pose en 3 niveaux ; contours :
# (tous les n m, facteur) ; far : (part de gris, part du fond) loin des routes ; bg : fond du menu (WorldMap.cs, Back).
# Les couleurs des routes, de l'eau, de la forêt et des bâtiments sont reprises dans la légende de UI/WorldMap.cs.
STYLES = {
    "alpin": dict(       # retenu le 2026-11-16 : couleurs naturelles, fond et hors-zone sombres comme le style « nuit »
        bg=(9, 11, 15), band=45.0, toon=False, shade=(0.80, 0.34), contours=((50.0, 0.93), (250.0, 0.84)), far=(0.75, 0.74),
        hypso=[(0, (128, 172, 96)), (140, (140, 178, 98)), (300, (164, 182, 106)), (480, (184, 178, 122)),
               (680, (170, 162, 146)), (900, (190, 188, 186))],
        forest=(62, 112, 74), dry=(186, 176, 112), rock=(150, 146, 142), snow=(236, 241, 248), paved=(206, 198, 186),
        water=(78, 150, 212), water_deep=(52, 112, 178), shore=(206, 232, 246), building=(104, 88, 84), bridge=(34, 36, 44),
        casing=7.0,
        roads=_roads(((255, 168, 60), (96, 44, 14)), ((255, 192, 110), (96, 44, 14)), ((255, 255, 252), (66, 70, 80)),
                     ((255, 222, 128), (104, 78, 34)), ((238, 234, 224), (92, 92, 98)), ((212, 176, 128), (104, 76, 48)))),
    "nuit": dict(
        bg=(9, 11, 15), band=60.0, toon=False, shade=(0.78, 0.5), contours=((50.0, 1.12), (250.0, 1.3)), far=(0.7, 0.55),
        hypso=[(0, (40, 52, 54)), (300, (48, 58, 62)), (600, (60, 66, 74)), (900, (82, 88, 98))],
        forest=(30, 46, 42), dry=(56, 60, 54), rock=(58, 60, 68), snow=(84, 94, 112), paved=(70, 72, 80),
        water=(22, 50, 80), water_deep=(14, 34, 60), shore=(66, 120, 160), building=(128, 118, 112), bridge=(4, 5, 8),
        casing=6.0,
        roads=_roads(((255, 186, 64), (60, 34, 6)), ((255, 204, 110), (60, 34, 6)), ((232, 238, 246), (14, 16, 22)),
                     ((96, 214, 255), (10, 36, 52)), ((160, 168, 180), (14, 16, 22)), ((156, 132, 100), (20, 16, 12)))),
    "papier": dict(
        bg=(16, 19, 25), band=60.0, toon=False, shade=(0.93, 0.18), contours=((50.0, 0.95), (250.0, 0.88)), far=(0.4, 0.3),
        hypso=[(0, (238, 234, 214)), (300, (230, 228, 204)), (600, (220, 212, 194)), (900, (226, 222, 216))],
        forest=(184, 210, 164), dry=(234, 224, 184), rock=(208, 202, 194), snow=(250, 250, 252), paved=(226, 216, 202),
        water=(154, 200, 232), water_deep=(128, 182, 224), shore=(84, 136, 186), building=(196, 122, 110), bridge=(40, 40, 46),
        casing=5.0,
        roads=_roads(((228, 56, 44), (118, 22, 18)), ((236, 104, 84), (118, 22, 18)), ((255, 212, 76), (132, 98, 26)),
                     ((255, 255, 255), (112, 112, 116)), ((255, 255, 255), (150, 150, 154)), ((206, 170, 126), (150, 118, 86)))),
    "cartoon": dict(
        bg=(16, 19, 25), band=90.0, toon=True, shade=(0.86, 0.3), contours=((90.0, 0.8),), far=(0.55, 0.38),
        width=1.35, outline=0.55,
        hypso=[(0, (134, 208, 70)), (220, (178, 218, 78)), (440, (224, 212, 108)), (660, (204, 172, 128)),
               (880, (238, 234, 238))],
        forest=(28, 130, 98), dry=(232, 206, 96), rock=(168, 150, 146), snow=(248, 250, 255), paved=(226, 212, 188),
        water=(40, 192, 232), water_deep=(22, 142, 212), shore=(250, 252, 255), building=(222, 92, 70), bridge=(24, 24, 30),
        casing=9.0,
        roads=_roads(((255, 138, 36), (24, 24, 30)), ((255, 170, 90), (24, 24, 30)), ((255, 255, 255), (24, 24, 30)),
                     ((255, 214, 56), (24, 24, 30)), ((236, 236, 236), (24, 24, 30)), ((214, 160, 96), (24, 24, 30)))),
    "epure": dict(
        bg=(16, 19, 25), band=200.0, toon=False, shade=(0.96, 0.14), contours=(), far=(0.5, 0.35),
        hypso=[(0, (222, 228, 218)), (900, (234, 234, 232))],
        forest=(200, 216, 196), dry=(226, 228, 214), rock=(216, 216, 216), snow=(248, 250, 252), paved=(232, 228, 222),
        water=(172, 208, 234), water_deep=(154, 196, 230), shore=(206, 226, 240), building=(190, 184, 182), bridge=(150, 150, 156),
        casing=5.0,
        roads=_roads(((255, 150, 60), (226, 118, 40)), ((255, 178, 110), (226, 118, 40)), ((255, 255, 255), (178, 182, 190)),
                     ((255, 212, 122), (208, 168, 88)), ((255, 255, 255), (196, 196, 200)), ((222, 202, 172), (196, 176, 150)))),
}
STYLE = "alpin"      # style de map_overview.png (le jeu)


ORDER = ["track", "lane", "mountain", "main", "ramp", "highway"]   # du dessous vers le dessus


def hypso(h, st):
    """Couleur d'altitude en paliers (h relatif, m)."""
    band, table = st["band"], st["hypso"]
    hq = np.floor(h / band) * band + band * 0.5
    xs = [a for a, _ in table]
    return np.stack([np.interp(hq, xs, [c[i] for _, c in table]) for i in range(3)], axis=-1)


def soft(mask, lo, hi):
    """Masque [0, 1] adouci (seuil progressif) : bords nets mais sans escalier."""
    return np.clip((mask - lo) / (hi - lo), 0.0, 1.0)


def render(g, H, rgba, paved, snow, water, edges, rivers, buildings, out_path, palette="overview"):
    st = STYLES[STYLE if palette == "overview" else palette]
    C = {k: np.array(st[k], dtype=np.float64) for k in ("forest", "dry", "rock", "snow", "paved", "water", "water_deep")}
    n = g.n
    h = H - np.percentile(H, 2)
    col = hypso(h, st)
    dry = soft(L.blur2d(rgba[..., 1] / 255.0, 1.0), 0.45, 0.75)[..., None]
    col = col * (1 - dry * 0.6) + C["dry"] * dry * 0.6
    forest = soft(L.blur2d(rgba[..., 0] / 255.0, 1.2), 0.40, 0.55)[..., None]
    col = col * (1 - forest) + C["forest"] * forest
    if st.get("outline"):
        # trait sombre au bord des forêts (style dessin animé)
        fb = soft(L.blur2d(rgba[..., 0] / 255.0, 1.2), 0.36, 0.59)
        col[(fb > 0.05) & (fb < 0.6)] *= st["outline"]
    rock = soft(L.blur2d(rgba[..., 2] / 255.0, 1.0), 0.45, 0.6)[..., None]
    col = col * (1 - rock) + C["rock"] * rock
    pv = soft(L.blur2d(paved / 255.0, 0.8), 0.4, 0.6)[..., None]
    col = col * (1 - pv) + C["paved"] * pv
    sn = soft(L.blur2d(snow / 255.0, 1.2), 0.35, 0.55)[..., None]
    # sous la neige la forêt reste visible (sapins enneigés) : les pistes de ski s'y lisent en couloirs blancs
    snow_col = C["snow"] * (1 - forest) + (C["forest"] * 0.6 + C["snow"] * 0.4) * forest
    col = col * (1 - sn) + snow_col * sn
    # relief : ombrage doux (en 3 niveaux pour le style toon ; la neige le garde un peu bleuté), courbes de niveau
    shade = L.hillshade(L.blur2d(H, 2.0), g.step, az=315.0, alt=45.0)
    if st["toon"]:
        shade = np.where(shade > 0.78, 0.85, np.where(shade > 0.55, 0.5, 0.15))
    base, amount = st["shade"]
    lit = base + amount * (shade - 0.5)
    col *= lit[..., None]
    col += (np.array([-6.0, 0.0, 14.0]) * (1 - lit)[..., None] * sn * 2.0)
    for every, k in st["contours"]:
        c = np.floor(H / every)
        line = np.zeros((n, n), dtype=bool)
        line[:-1, :] |= c[:-1, :] != c[1:, :]
        line[:, :-1] |= c[:, :-1] != c[:, 1:]
        col[line] *= k
    # eau : plus sombre au large, liseré clair sur la rive
    wm = water > 0
    depth = soft(L.blur2d(wm.astype(np.float64), 6.0), 0.5, 1.0)[..., None]
    wc = C["water"] * (1 - depth) + C["water_deep"] * depth
    col[wm] = wc[wm]

    img = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)[::-1])        # nord en haut
    big = SIZE * SS
    img = img.resize((big, big), Image.BICUBIC)
    # rives nettes et lisses : masque de l'eau agrandi puis seuillé (pas d'escalier de la grille), liseré clair autour
    from PIL import ImageFilter
    wbig = Image.fromarray((L.blur2d(wm.astype(np.float64), 0.8)[::-1] * 255).astype(np.uint8)).resize((big, big), Image.BICUBIC)
    wbig = wbig.point(lambda v: 255 if v >= 128 else 0)
    img.paste(tuple(st["shore"]), mask=wbig.filter(ImageFilter.MaxFilter(2 * max(1, int(2.5 * big / (2 * g.half))) + 1)))
    img.paste(Image.fromarray(np.clip(wc, 0, 255).astype(np.uint8)[::-1]).resize((big, big), Image.BICUBIC), mask=wbig)
    dr = ImageDraw.Draw(img)
    k = big / (2 * g.half)

    def px(x, z):
        return ((x + g.half) * k, (g.half - z) * k)

    # traits épais sans entailles (segments + disques) : même tracé que la minicarte
    from map_minimap import stroke
    for rv in rivers:
        p = rv["pts"]
        w = max(3, int(float(p[0][3]) * 1.4 * k))
        stroke(dr, [px(a[0], a[1]) for a in p], w + max(2, int(3 * k)), tuple(st["shore"]))
        stroke(dr, [px(a[0], a[1]) for a in p], w, tuple(st["water"]))
    # bâtiments sous les routes : les rues restent lisibles
    for b in buildings:
        dr.polygon([px(q[0], q[1]) for q in b], fill=tuple(st["building"]))
    cas = max(2, int(round(st["casing"] * k)))
    # aéroport : piste et voies de circulation à leur largeur réelle, sous les routes
    import map_airport as AP
    AP.draw(dr, px, k, st["roads"]["main"][1], st["roads"]["main"][2], cas)
    for layer in ("bridge", "casing", "fill"):
        for kind in ORDER:
            w_m, fill, edge_col = st["roads"][kind]
            w = max(3, int(round(w_m * st.get("width", 1.0) * k)))
            for e in edges:
                if e["kind"] != kind:
                    continue
                for (seg, part) in pieces(e):
                    pts = [px(a[0], a[1]) for a in seg]
                    if len(pts) < 2:
                        continue
                    if part == "tunnel":
                        # en tunnel la route ne se voit pas : pointillé du bord, cœur clair
                        if layer == "fill":
                            dash = max(4, int(36 * k))
                            for d0 in _dashes(pts, dash, dash * 0.8):
                                stroke(dr, d0, max(3, w // 2) + cas, edge_col)
                                stroke(dr, d0, max(2, w // 3), fill)
                    elif layer == "bridge":
                        if part == "bridge":
                            stroke(dr, pts, w + 2 * cas + max(4, int(10 * k)), tuple(st["bridge"]))
                    elif layer == "casing":
                        stroke(dr, pts, w + 2 * cas, edge_col)
                    else:
                        stroke(dr, pts, w, fill)
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    # le domaine skiable reste éclairé comme les abords des routes (sinon la carte s'y éteint)
    import map_station as SK
    ski = [dict(pts=[(float(q[0]), float(q[1])) for q in pts]) for (_, _, _, pts) in SK.PISTES]
    runway = [dict(pts=[AP.loc(-AP.HALF, 0.0), AP.loc(AP.HALF, 0.0)])]
    img = _focus(img, list(edges) + ski + runway, g, st)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path, optimize=True)
    # Au bake, la minicarte suit la carte globale
    if palette == "overview" and os.path.basename(out_path) == "map_overview.png":
        import map_minimap
        map_minimap.render(g, H, rgba, snow, water, edges, rivers, os.path.join(os.path.dirname(out_path), "map_minimap.png"))
    return out_path


def _dashes(pts, on, off):
    """Découpe une polyligne (px) en tirets de longueur `on` séparés de `off`."""
    out, cur, acc, drawing = [], [pts[0]], 0.0, True
    for a, b in zip(pts[:-1], pts[1:]):
        seg = math.hypot(b[0] - a[0], b[1] - a[1])
        t0 = 0.0
        while seg - t0 > 1e-6:
            left = (on if drawing else off) - acc
            step = min(left, seg - t0)
            t1 = t0 + step
            q = (a[0] + (b[0] - a[0]) * t1 / seg, a[1] + (b[1] - a[1]) * t1 / seg)
            if drawing:
                cur.append(q)
            acc += step
            t0 = t1
            if acc >= (on if drawing else off) - 1e-6:
                if drawing and len(cur) > 1:
                    out.append(cur)
                drawing, acc, cur = not drawing, 0.0, [q]
    if drawing and len(cur) > 1:
        out.append(cur)
    return out


def _focus(img, edges, g, st):
    """Loin des routes, la carte s'éteint (désaturée, assombrie) puis se fond dans le fond du menu près des bords."""
    from PIL import ImageFilter
    lo = 512
    m = Image.new("L", (lo, lo), 0)
    dr = ImageDraw.Draw(m)
    k = lo / (2 * g.half)
    for e in edges:
        pts = [((a[0] + g.half) * k, (g.half - a[1]) * k) for a in e["pts"]]
        if len(pts) > 1:
            dr.line(pts, fill=255, width=max(1, int(260 * k)))
    m = m.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(lo * 0.035))
    near = np.asarray(m.resize(img.size, Image.BICUBIC), dtype=np.float64) / 255.0
    near = soft(near, 0.05, 0.55)[..., None]
    a = np.asarray(img, dtype=np.float64)
    grey = a.mean(axis=2, keepdims=True)
    bg = np.array(st["bg"], dtype=np.float64)
    grey_part, bg_part = st["far"]
    far = (a * (1 - grey_part) + grey * grey_part) * (1 - bg_part) + bg * bg_part
    a = far * (1 - near) + a * near
    # fondu carré vers BG sur les derniers 9 % de l'image
    s = img.size[0]
    t = np.abs(np.linspace(-1.0, 1.0, s))
    edge = soft(np.maximum(t[:, None], t[None, :]), 0.91, 0.995)
    edge = (edge * edge * (3 - 2 * edge))[..., None]
    a = a * (1 - edge) + bg * edge
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def pieces(e):
    """Découpe une arête en tronçons : [(points, "open" | "tunnel" | "bridge")] (index de points dans e["tunnels"] et
    e["bridges"], ce dernier facultatif)."""
    P = e["pts"]
    n = len(P)
    part = np.zeros(n, dtype=np.int8)
    for (i0, i1) in e.get("bridges", ()):
        part[i0:i1 + 1] = 2
    for (i0, i1) in e.get("tunnels", ()):
        part[i0:i1 + 1] = 1
    names = ("open", "tunnel", "bridge")
    out, start = [], 0
    for i in range(1, n + 1):
        if i == n or part[i] != part[start]:
            out.append((P[start:min(i + 1, n)], names[part[start]]))
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
        spans = {kind: [(int(np.searchsorted(s3, sp["s0"])), int(np.searchsorted(s3, sp["s1"]))) for sp in e.get("spans", [])
                        if sp["kind"] == kind] for kind in ("tunnel", "bridge")}
        edges.append(dict(kind=e["kind"], pts=[(p[i], p[i + 2]) for i in range(0, len(p), 3)], tunnels=spans["tunnel"],
                          bridges=spans["bridge"]))
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
    res = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "ChillRoad", "Resources", "Map")
    res = os.path.normpath(res)
    if "--minimap" in sys.argv:
        import map_minimap
        out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(res, "map_minimap.png")
        g, H, rgba, paved, snow, water, edges, rivers, buildings = from_export(res)
        print(map_minimap.render(g, H, rgba, snow, water, edges, rivers, out))
    elif "--overview" in sys.argv:
        # refait la carte globale (et la minicarte) à partir des données exportées, sans bake
        out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(res, "map_overview.png")
        style = sys.argv[sys.argv.index("--style") + 1] if "--style" in sys.argv else "overview"
        print(render(*from_export(res), out, palette=style))
    else:
        print(__doc__)
