"""Aperçus de la carte en PNG : relief ombré, courbes de niveau, eau, routes, bâtiments, quadrillage coté."""
import numpy as np
from PIL import Image, ImageDraw
import map_lib as L

RAMP = [(-60, (60, 96, 120)), (0, (120, 160, 96)), (60, (132, 170, 92)), (200, (110, 150, 84)), (330, (150, 168, 104)),
        (480, (156, 150, 124)), (650, (150, 146, 142)), (820, (200, 200, 204)), (1000, (244, 246, 250))]

KIND_COLOR = {"highway": (240, 120, 70), "ramp": (250, 170, 120), "main": (250, 240, 210), "mountain": (255, 214, 120), "street": (255, 255, 255),
              "track": (200, 150, 100), "lane": (235, 235, 235)}


def render(g, H, path, roads=None, water=None, rivers=None, buildings=None, nodes=None, size=1400, window=None,
           contours=50.0, labels=True, extra=None):
    """window = (x0, z0, x1, z1) en mètres pour un agrandissement."""
    if window is None:
        window = (-g.half, -g.half, g.half, g.half)
    x0, z0, x1, z1 = window
    ix0, iz0 = [int(round(float(v))) for v in g.index(x0, z0)]
    ix1, iz1 = [int(round(float(v))) for v in g.index(x1, z1)]
    ix0, iz0 = max(ix0, 0), max(iz0, 0)
    ix1, iz1 = min(ix1, g.n - 1), min(iz1, g.n - 1)
    sub = H[iz0:iz1 + 1, ix0:ix1 + 1]
    shade = L.hillshade(sub, g.step)
    xs = [r[0] for r in RAMP]
    rgb = np.stack([np.interp(sub, xs, [r[1][k] for r in RAMP]) for k in range(3)], axis=-1)
    rgb = rgb * (0.45 + 0.75 * shade[..., None])
    if contours:
        c = np.floor(sub / contours)
        edge = np.zeros(sub.shape, dtype=bool)
        edge[:-1, :] |= c[:-1, :] != c[1:, :]
        edge[:, :-1] |= c[:, :-1] != c[:, 1:]
        rgb[edge] *= 0.82
    if water is not None:
        w = water[iz0:iz1 + 1, ix0:ix1 + 1] > 0
        rgb[w] = (70, 130, 190)
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)[::-1])     # nord en haut
    scale = size / max(img.size)
    img = img.resize((int(img.size[0] * scale), int(img.size[1] * scale)), Image.BILINEAR)
    dr = ImageDraw.Draw(img)
    W, Hh = img.size

    def px(x, z):
        return ((x - x0) / (x1 - x0) * W, (1 - (z - z0) / (z1 - z0)) * Hh)

    span = x1 - x0
    tick = 500 if span > 3000 else 250 if span > 1500 else 100 if span > 600 else 50
    if labels:
        v = np.ceil(x0 / tick) * tick
        while v < x1:
            dr.line([px(v, z0), px(v, z1)], fill=(0, 0, 0, 60), width=1)
            dr.text((px(v, z1)[0] + 2, 2), "%d" % v, fill=(0, 0, 0))
            v += tick
        v = np.ceil(z0 / tick) * tick
        while v < z1:
            dr.line([px(x0, v), px(x1, v)], fill=(0, 0, 0, 60), width=1)
            dr.text((2, px(x0, v)[1] + 2), "%d" % v, fill=(0, 0, 0))
            v += tick
    for rv in rivers or []:
        p = rv["pts"]
        dr.line([px(a[0], a[1]) for a in p], fill=(60, 120, 200), width=max(1, int(p[0][3] * W / span)))
    for r in roads or []:
        p = r["pts"]
        col = KIND_COLOR.get(r.get("kind", "main"), (255, 255, 255))
        wpx = max(2, int(r.get("hw", 4) * 2 * W / span))
        dr.line([px(a[0], a[1]) for a in p], fill=(40, 40, 40), width=wpx + 2)
        dr.line([px(a[0], a[1]) for a in p], fill=col, width=wpx)
        for (s0, s1, kind) in r.get("spans", []):
            seg = [px(a[0], a[1]) for a, s in zip(p, r["s"]) if s0 <= s <= s1]
            if len(seg) > 1:
                dr.line(seg, fill=(200, 60, 60) if kind == "bridge" else (70, 70, 200) if kind == "tunnel" else (60, 60, 60), width=wpx)
    for b in buildings or []:
        c = b.get("corners")
        if c:
            dr.polygon([px(q[0], q[1]) for q in c], fill=b.get("color", (150, 80, 60)), outline=(40, 20, 10))
    for n in nodes or []:
        q = px(n["x"], n["z"])
        dr.ellipse([q[0] - 3, q[1] - 3, q[0] + 3, q[1] + 3], fill=(220, 40, 40))
        if labels:
            dr.text((q[0] + 5, q[1] - 5), n["id"], fill=(120, 0, 0))
    if extra:
        extra(dr, px)
    img.save(path)
    return path
