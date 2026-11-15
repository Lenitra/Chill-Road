"""Minicarte du HUD : la carte en niveaux de gris (noir, gris, blanc), très sobre.

Version claire : fond gris clair avec un relief doux, eau gris moyen cernée d'un fin liseré, routes blanches bordées de
gris (la largeur dit leur importance : autoroute la plus large, pistes un peu grises), tunnels en pointillés gris.
Aucune autre couleur, aucun bâtiment ni texte : le jeu pose par-dessus la flèche du joueur et la circulation.

Écrite à côté de map_overview.png à chaque bake (map_overview.render l'appelle). Sans bake :
`python map_minimap.py` la refait à partir des données déjà exportées dans Assets/NoHesi/Resources/Map
(`--out chemin.png` pour l'écrire ailleurs).
"""
import os
import sys
import numpy as np
from PIL import Image, ImageDraw
import map_lib as L

SIZE = 4096          # image finale (carrée, couvre ±half, nord en haut) : ~1,6 m par pixel, routes nettes de près
SS = 2               # suréchantillonnage du tracé (anticrénelage)

GROUND = 184.0       # sol (0-255)
RELIEF = 40.0        # amplitude de l'ombrage du relief autour du sol
FOREST = -12.0       # forêts : un peu plus sombres
SNOW = 18.0          # neige des sommets : un peu plus claire
WATER = 134          # lacs et rivières
SHORE = 108          # liseré des rives
OUTLINE = 96         # bord des routes (les détache du sol, aux croisements et sur les ponts)
TUNNEL = 96          # pointillé des tunnels

ROADS = {            # type : (largeur dessinée en mètres, gris)
    "track": (6.0, 214),
    "lane": (8.0, 232),
    "mountain": (11.0, 246),
    "main": (13.0, 252),
    "ramp": (9.0, 250),
    "highway": (24.0, 255),
}
ORDER = ["track", "lane", "mountain", "main", "ramp", "highway"]   # du dessous vers le dessus


def stroke(dr, pts, width, fill):
    """Trait épais sans entailles : segments plus un disque à chaque point (ImageDraw laisse des fentes entre des
    segments très courts devant leur largeur, d'où des traits sombres en travers des routes larges)."""
    if len(pts) < 2:
        return
    # points rééchantillonnés tous les quarts de largeur : assez de disques pour boucher les joints, pas trop
    step = max(1.5, width * 0.25)
    out = [pts[0]]
    for q in pts[1:]:
        x0, y0 = out[-1]
        dx, dy = q[0] - x0, q[1] - y0
        d = (dx * dx + dy * dy) ** 0.5
        if d < step:
            continue
        m = int(d // step)
        for i in range(1, m + 1):
            out.append((x0 + dx * i / m, y0 + dy * i / m))
    if out[-1] != pts[-1]:
        out.append(pts[-1])
    dr.line(out, fill=fill, width=int(width))
    r = width / 2.0
    for x, y in out:
        dr.ellipse((x - r, y - r, x + r, y + r), fill=fill)


def render(g, H, rgba, snow, water, edges, rivers, out_path):
    import map_overview as OV
    n = g.n
    shade = L.hillshade(L.blur2d(H, 2.0), g.step, az=315.0, alt=45.0)
    v = GROUND + RELIEF * (shade - shade.mean())
    v += FOREST * (rgba[..., 0] / 255.0 > 0.45)
    v += SNOW * np.clip((snow / 255.0 - 0.3) / 0.4, 0, 1)
    wm = water > 0
    v[wm] = WATER
    shore = np.zeros((n, n), dtype=bool)
    shore[1:, :] |= wm[1:, :] != wm[:-1, :]
    shore[:, 1:] |= wm[:, 1:] != wm[:, :-1]
    v[shore] = SHORE
    img = Image.fromarray(np.clip(v, 0, 255).astype(np.uint8)[::-1], "L")      # nord en haut
    big = SIZE * SS
    img = img.resize((big, big), Image.BICUBIC)
    dr = ImageDraw.Draw(img)
    k = big / (2 * g.half)

    def px(x, z):
        return ((x + g.half) * k, (g.half - z) * k)

    for rv in rivers:
        p = rv["pts"]
        w = max(2, int(float(p[0][3]) * 1.2 * k))
        stroke(dr, [px(a[0], a[1]) for a in p], w, WATER)
    # Bords d'abord (toutes les routes), puis le corps, de la moins importante à la plus importante
    for outline in (True, False):
        for kind in ORDER:
            if kind not in ROADS:
                continue
            w_m, grey = ROADS[kind]
            w = max(2, int(round(w_m * k)))
            for e in edges:
                if e["kind"] != kind:
                    continue
                for seg, tunnel in OV.pieces(e):
                    pts = [px(a[0], a[1]) for a in seg]
                    if len(pts) < 2:
                        continue
                    if tunnel:
                        if not outline:
                            for j in range(0, len(pts) - 4, 10):
                                dr.line(pts[j:j + 5], fill=TUNNEL, width=max(2, w // 3))
                    elif outline:
                        stroke(dr, pts, w + max(2, int(6 * k)), OUTLINE)
                    else:
                        stroke(dr, pts, w, grey)
    # Routes d'un type inconnu (ajouté plus tard au bake) : gris très clair plutôt que rien
    for e in edges:
        if e["kind"] in ROADS:
            continue
        pts = [px(a[0], a[1]) for a in e["pts"]]
        if len(pts) >= 2:
            stroke(dr, pts, max(2, int(round(9.0 * k))), 236)
    img = img.resize((SIZE, SIZE), Image.LANCZOS).convert("RGB")   # RVB : un PNG gris s'importerait en rouge seul
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path)
    return out_path


if __name__ == "__main__":
    import map_overview as OV
    res = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "NoHesi", "Resources", "Map"))
    out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(res, "map_minimap.png")
    g, H, rgba, paved, snow, water, edges, rivers, buildings = OV.from_export(res)
    print(render(g, H, rgba, snow, water, edges, rivers, out))
