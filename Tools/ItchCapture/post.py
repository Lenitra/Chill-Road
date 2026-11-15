"""Post-traitement des captures : incrustation du HUD (rendus fond noir / fond blanc) et planche contact."""
import sys, os, glob
import numpy as np
from PIL import Image, ImageDraw

def to_lin(a):
    a = a / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)

def to_srgb(l):
    l = np.clip(l, 0, 1)
    s = np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)
    return (s * 255 + 0.5).astype(np.uint8)

def composite(world_png, uib_png, uiw_png):
    w = to_lin(np.asarray(Image.open(world_png).convert("RGB"), dtype=np.float64))
    b = to_lin(np.asarray(Image.open(uib_png).convert("RGB"), dtype=np.float64))
    wh = to_lin(np.asarray(Image.open(uiw_png).convert("RGB"), dtype=np.float64))
    # I_noir = a*C ; I_blanc = a*C + (1-a)  →  (1-a) = I_blanc - I_noir
    t = np.clip((wh - b).mean(axis=2, keepdims=True), 0, 1)
    return Image.fromarray(to_srgb(b + t * w))

def contact(folder, out, pattern="*.png", thumb=480):
    files = sorted(f for f in glob.glob(os.path.join(folder, pattern)) if not any(k in f for k in ("_uib", "_uiw", "_4k", "contact")))
    if not files:
        return
    cols = 4
    th = thumb * 9 // 16
    rows = (len(files) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * thumb, rows * (th + 18)), (20, 20, 20))
    d = ImageDraw.Draw(sheet)
    for i, f in enumerate(files):
        im = Image.open(f).convert("RGB")
        im.thumbnail((thumb, th))
        x, y = (i % cols) * thumb, (i // cols) * (th + 18)
        sheet.paste(im, (x, y))
        d.text((x + 4, y + th + 2), os.path.basename(f)[:-4], fill=(230, 230, 230))
    sheet.save(out)
    print("contact", out, len(files))

if __name__ == "__main__":
    folder = sys.argv[1]
    # Incruste le HUD quand les rendus d'interface existent
    for uib in glob.glob(os.path.join(folder, "*_uib.png")):
        base = uib[:-8]
        uiw = base + "_uiw.png"
        if os.path.exists(base + ".png") and os.path.exists(uiw):
            composite(base + ".png", uib, uiw).save(base + "_hud.png")
    contact(folder, os.path.join(folder, "contact_world.png"), "*.png")
