"""Chill Road — icônes du logo « Ligne de crête » (proposition 01, 2026-11-19).

Tuile sombre arrondie, ligne de crête blanche qui se termine en route dorée.
Génère l'icône de l'application (Unity PlayerSettings → Resources/Logo/ChillRoadLogo.png)
et les icônes du site (docs/img). Dessin vectoriel tracé avec PIL en sur-échantillonnage x8.

    python Tools/Logo/make_logo_icons.py
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
TILE = (0x1A, 0x1F, 0x27)
WHITE = (0xFF, 0xFF, 0xFF)
GOLD = (0xFF, 0xC2, 0x33)

# Tracés dans le repère 96 x 60 de l'icône (identiques à Marketing/logo/ligne_de_crete.svg)
RIDGE = [(4, 52), (22, 30), (34, 38), (52, 10), (66, 34)]
ROAD = ((66, 34), (74, 50), (82, 22), (92, 30))   # Bézier cubique


def bezier(p0, p1, p2, p3, n=80):
    out = []
    for i in range(n + 1):
        t = i / n
        a, b, c, d = (1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out


def stroke(draw, pts, width, color):
    draw.line(pts, fill=color, width=round(width), joint="curve")
    r = width / 2
    for x, y in (pts[0], pts[-1]):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=color)


def icon(size, ridge_w, road_w, radius=0.234, border=True, full_bleed=False, art=0.75):
    """size px ; ridge_w / road_w = épaisseurs dans le repère 96 ; art = largeur du dessin / largeur de tuile."""
    ss = 8
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if full_bleed:
        d.rectangle((0, 0, S, S), fill=TILE + (255,))
    else:
        d.rounded_rectangle((0, 0, S - 1, S - 1), radius=radius * S, fill=TILE + (255,))
        if border:
            bw = max(ss, round(S / 128))
            d.rounded_rectangle((0, 0, S - 1, S - 1), radius=radius * S, outline=(255, 255, 255, 52), width=bw)
    k = art * S / 96
    ox, oy = (S - 96 * k) / 2, (S - 60 * k) / 2 + 1.5 * k

    def tr(pts):
        return [(ox + x * k, oy + y * k) for x, y in pts]

    stroke(d, tr(RIDGE), ridge_w * k, WHITE + (255,))
    stroke(d, tr(bezier(*ROAD)), road_w * k, GOLD + (255,))
    return img.resize((size, size), Image.LANCZOS)


def main():
    app = icon(1024, 2.6, 4.6)
    app.save(ROOT / "Assets/ChillRoad/Resources/Logo/ChillRoadLogo.png")
    img = ROOT / "docs/img"
    icon(192, 2.8, 4.8).save(img / "logo-192.png")
    icon(96, 3.2, 5.4).save(img / "logo-96.png")
    icon(32, 5.0, 8.0, border=False, art=0.86).save(img / "favicon-32.png")
    icon(180, 2.8, 4.8, full_bleed=True, art=0.7).convert("RGB").save(img / "apple-touch-icon.png")
    print("ok")


if __name__ == "__main__":
    main()
