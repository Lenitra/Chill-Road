"""Minicarte du HUD : la carte vue de dessus, très sobre, sans bâtiment ni texte (le jeu pose par-dessus la flèche du
joueur, la circulation et l'itinéraire du GPS).

Plusieurs styles (STYLES), celui du jeu est STYLE : « epure » (fond crème presque sans relief, eau bleu pâle, routes
blanches bordées de beige, autoroute orange, routes de montagne jaunes, comme la carte globale en plus doux ; la largeur
dit l'importance d'une route, tunnels en pointillés). « gris » est la version précédente, en niveaux de gris. Un style
peut laisser le sol transparent (ground_alpha < 255) : le jeu se voit alors sous les routes.

Écrite à côté de map_overview.png à chaque bake (map_overview.render l'appelle). Sans bake :
`python map_minimap.py` la refait à partir des données déjà exportées dans Assets/ChillRoad/Resources/Map
(`--out chemin.png` pour l'écrire ailleurs, `--style nom` pour un autre style).
"""
import os
import sys
import numpy as np
from PIL import Image, ImageDraw
import map_lib as L

SIZE = 4096          # image finale (carrée, couvre ±half, nord en haut) : ~1,6 m par pixel, routes nettes de près
SS = 2               # suréchantillonnage du tracé (anticrénelage)

WIDTHS = {"track": 6.0, "lane": 8.0, "mountain": 11.0, "main": 13.0, "ramp": 9.0, "highway": 24.0}   # m
ORDER = ["track", "lane", "mountain", "main", "ramp", "highway"]   # du dessous vers le dessus


def _grey(v):
    return (v, v, v)


# ground : sol ; relief : amplitude de l'ombrage ; forest / snow : couleur des forêts et de la neige (mélangée au sol
# selon `forest_mix`) ; water / shore : eau et liseré des rives ; outline : bord des routes ; tunnel : pointillé ;
# roads : remplissage par type ; ground_alpha / water_alpha : opacité du sol et de l'eau (255 = opaque).
STYLES = {
    "gris": dict(ground=_grey(184), relief=40.0, forest=_grey(172), forest_mix=1.0, snow=_grey(202), water=_grey(134),
                 shore=_grey(108), outline=_grey(96), tunnel=_grey(96),
                 roads={"track": _grey(214), "lane": _grey(232), "mountain": _grey(246), "main": _grey(252), "ramp": _grey(250),
                        "highway": _grey(255)}),
    "alpin": dict(ground=(150, 186, 108), relief=34.0, forest=(84, 132, 88), forest_mix=1.0, snow=(232, 238, 246),
                  water=(84, 158, 216), shore=(214, 236, 248), outline=(52, 56, 52), tunnel=(52, 56, 52),
                  roads={"track": (214, 180, 132), "lane": (240, 236, 226), "mountain": (255, 222, 128), "main": (255, 255, 252),
                         "ramp": (255, 196, 120), "highway": (255, 168, 60)}),
    "nuit": dict(ground=(44, 50, 58), relief=20.0, forest=(34, 44, 46), forest_mix=1.0, snow=(92, 100, 116),
                 water=(22, 52, 86), shore=(62, 108, 146), outline=(10, 12, 16), tunnel=(130, 132, 142),
                 roads={"track": (128, 118, 102), "lane": (168, 174, 184), "mountain": (204, 210, 218), "main": (236, 240, 246),
                        "ramp": (255, 204, 110), "highway": (255, 186, 64)}),
    "epure": dict(ground=(238, 234, 224), relief=10.0, forest=(220, 228, 206), forest_mix=1.0, snow=(250, 250, 250),
                  water=(172, 208, 234), shore=(148, 188, 220), outline=(188, 182, 172), tunnel=(170, 164, 156),
                  roads={"track": (224, 202, 172), "lane": (255, 255, 255), "mountain": (255, 212, 122), "main": (255, 255, 255),
                         "ramp": (255, 182, 112), "highway": (255, 150, 60)}),
    "vitre": dict(ground=(8, 10, 14), relief=0.0, forest=(8, 10, 14), forest_mix=0.0, snow=(8, 10, 14), water=(40, 120, 200),
                  shore=(120, 190, 240), outline=(6, 8, 12), tunnel=(200, 204, 210), ground_alpha=70, water_alpha=170,
                  roads={"track": (170, 160, 146), "lane": (210, 214, 220), "mountain": (236, 240, 246), "main": (255, 255, 255),
                         "ramp": (255, 204, 110), "highway": (255, 186, 64)}),
}
STYLE = "epure"      # style de map_minimap.png (le jeu), choisi le 2026-11-16 parmi 5 propositions


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


def render(g, H, rgba, snow, water, edges, rivers, out_path, style=None):
    import map_overview as OV
    st = STYLES[style or STYLE]
    n = g.n
    shade = L.hillshade(L.blur2d(H, 2.0), g.step, az=315.0, alt=45.0)
    col = np.broadcast_to(np.array(st["ground"], dtype=np.float64), (n, n, 3)).copy()
    fm = (rgba[..., 0] / 255.0 > 0.45)[..., None] * st["forest_mix"]
    col = col * (1 - fm) + np.array(st["forest"], dtype=np.float64) * fm
    sm = np.clip((snow / 255.0 - 0.3) / 0.4, 0, 1)[..., None]
    col = col * (1 - sm) + np.array(st["snow"], dtype=np.float64) * sm
    col += (st["relief"] * (shade - shade.mean()))[..., None]
    alpha = np.full((n, n), float(st.get("ground_alpha", 255)))
    wm = water > 0
    col[wm] = st["water"]
    alpha[wm] = st.get("water_alpha", 255)
    shore = np.zeros((n, n), dtype=bool)
    shore[1:, :] |= wm[1:, :] != wm[:-1, :]
    shore[:, 1:] |= wm[:, 1:] != wm[:, :-1]
    col[shore] = st["shore"]
    alpha[shore] = 255
    rgba_img = np.concatenate([np.clip(col, 0, 255), alpha[..., None]], axis=-1).astype(np.uint8)[::-1]   # nord en haut
    img = Image.fromarray(rgba_img, "RGBA")
    big = SIZE * SS
    img = img.resize((big, big), Image.BICUBIC)
    dr = ImageDraw.Draw(img)
    k = big / (2 * g.half)

    def px(x, z):
        return ((x + g.half) * k, (g.half - z) * k)

    def c4(rgb, a=255):
        return tuple(int(v) for v in rgb) + (a,)

    for rv in rivers:
        p = rv["pts"]
        w = max(2, int(float(p[0][3]) * 1.2 * k))
        stroke(dr, [px(a[0], a[1]) for a in p], w, c4(st["water"], st.get("water_alpha", 255)))
    # Aéroport : piste et voies de circulation à leur largeur réelle, sous les routes
    import map_airport as AP
    AP.draw(dr, px, k, c4(st["roads"]["main"]), c4(st["outline"]), max(2, int(6 * k)) / 2.0)
    # Bords d'abord (toutes les routes), puis le corps, de la moins importante à la plus importante
    for outline in (True, False):
        for kind in ORDER:
            w = max(2, int(round(WIDTHS[kind] * k)))
            for e in edges:
                if e["kind"] != kind:
                    continue
                for seg, part in OV.pieces(e):
                    pts = [px(a[0], a[1]) for a in seg]
                    if len(pts) < 2:
                        continue
                    if part == "tunnel":
                        if not outline:
                            for j in range(0, len(pts) - 4, 10):
                                dr.line(pts[j:j + 5], fill=c4(st["tunnel"]), width=max(2, w // 3))
                    elif outline:
                        stroke(dr, pts, w + max(2, int(6 * k)), c4(st["outline"]))
                    else:
                        stroke(dr, pts, w, c4(st["roads"][kind]))
    # Routes d'un type inconnu (ajouté plus tard au bake) : couleur des chemins plutôt que rien
    for e in edges:
        if e["kind"] in WIDTHS:
            continue
        pts = [px(a[0], a[1]) for a in e["pts"]]
        if len(pts) >= 2:
            stroke(dr, pts, max(2, int(round(9.0 * k))), c4(st["roads"]["lane"]))
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    # RVB quand tout est opaque : un PNG gris s'importerait en rouge seul, et sans canal alpha la texture est plus légère
    if st.get("ground_alpha", 255) >= 255 and st.get("water_alpha", 255) >= 255:
        img = img.convert("RGB")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path)
    return out_path


if __name__ == "__main__":
    import map_overview as OV
    res = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "ChillRoad", "Resources", "Map"))
    out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(res, "map_minimap.png")
    style = sys.argv[sys.argv.index("--style") + 1] if "--style" in sys.argv else None
    g, H, rgba, paved, snow, water, edges, rivers, buildings = OV.from_export(res)
    print(render(g, H, rgba, snow, water, edges, rivers, out, style))
