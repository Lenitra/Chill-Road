"""Couverture itch.io (ratio 630x500) à partir d'une capture 4K, avec variante titrée.
Usage : python make_cover.py <source_4k.png> <centre_x 0-1> <dossier_sortie> [centre_y 0-1] [zoom]"""
import sys, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

src, cx, out = sys.argv[1], float(sys.argv[2]), sys.argv[3]
cy = float(sys.argv[4]) if len(sys.argv) > 4 else 0.5
zoom = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0
im = Image.open(src).convert("RGB")
W, H = im.size
ratio = 630 / 500
ch = int(H / zoom)
cw = int(ch * ratio)
if cw > W:
    cw = W
    ch = int(cw / ratio)
x0 = int(min(max(cx * W - cw / 2, 0), W - cw))
y0 = int(min(max(cy * H - ch / 2, 0), H - ch))
crop = im.crop((x0, y0, x0 + cw, y0 + ch))
big = crop.resize((1260, 1000), Image.LANCZOS)
small = crop.resize((630, 500), Image.LANCZOS)
os.makedirs(out, exist_ok=True)
big.save(os.path.join(out, "cover_1260x1000.png"))
small.save(os.path.join(out, "cover_630x500.png"))

# Variante titrée : « CHILL ROAD » en italique gras, blanc cerné d'orange (style du logo en jeu)
def title(base, scale):
    img = base.convert("RGBA")
    w, h = img.size
    # Dégradé sombre en haut pour la lisibilité
    shade = Image.new("L", (1, h))
    for y in range(h):
        shade.putpixel((0, y), int(max(0, 1 - y / (h * 0.42)) ** 1.6 * 150))
    dark = Image.new("RGBA", (w, h), (8, 10, 18, 255))
    dark.putalpha(shade.resize((w, h)))
    img = Image.alpha_composite(img, dark)

    font = ImageFont.truetype(r"C:\Windows\Fonts\bahnschrift.ttf", int(150 * scale))
    try:
        font.set_variation_by_name("Bold")
    except Exception:
        pass
    text = "CHILL ROAD"
    tw = int(font.getlength(text)) + int(80 * scale)
    th = int(200 * scale)
    layer = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ox, oy = int(40 * scale), int(20 * scale)
    # Ombre néon rose/cyan décalée, puis contour orange, puis blanc
    glow = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.text((ox - 5 * scale, oy), text, font=font, fill=(255, 46, 140, 200))
    gd.text((ox + 5 * scale, oy), text, font=font, fill=(26, 230, 255, 200))
    glow = glow.filter(ImageFilter.GaussianBlur(10 * scale))
    layer = Image.alpha_composite(layer, glow)
    d = ImageDraw.Draw(layer)
    d.text((ox + 6 * scale, oy + 6 * scale), text, font=font, fill=(255, 150, 30, 150))
    d.text((ox, oy), text, font=font, fill=(248, 248, 248, 255), stroke_width=max(1, int(2 * scale)), stroke_fill=(255, 140, 20, 255))
    # Italique : cisaillement horizontal
    shear = 0.18
    layer = layer.transform((tw + int(th * shear), th), Image.AFFINE, (1, shear, -th * shear, 0, 1, 0), Image.BICUBIC)
    img.alpha_composite(layer, ((w - layer.size[0]) // 2, int(40 * scale)))
    return img.convert("RGB")

title(big, 1.0).save(os.path.join(out, "cover_1260x1000_titre.png"))
title(small, 0.5).save(os.path.join(out, "cover_630x500_titre.png"))
print("cover", x0, y0, cw, ch)
