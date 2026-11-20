"""Planches-contact d'un tournage : une ligne par plan, 5 images réparties sur sa durée.

    python Tools/Trailer/sheets.py DOSSIER [plan1 plan2 …]   → DOSSIER/_sheet_N.jpg (6 plans par planche)
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(sys.argv[1])
names = sys.argv[2:] or sorted(d.name for d in root.iterdir() if d.is_dir() and d.name != "outro")
TW, TH, COLS, ROWS = 384, 216, 5, 6
for page in range(0, len(names), ROWS):
    chunk = names[page:page + ROWS]
    sheet = Image.new("RGB", (TW * COLS, TH * len(chunk)), (20, 20, 20))
    d = ImageDraw.Draw(sheet)
    for r, name in enumerate(chunk):
        files = sorted((root / name).glob("*.jpg"))
        if not files:
            d.text((8, r * TH + 8), name + " : aucune image", fill=(255, 80, 80))
            continue
        for c in range(COLS):
            f = files[round(c * (len(files) - 1) / (COLS - 1))]
            im = Image.open(f)
            im.draft("RGB", (TW, TH))
            sheet.paste(im.convert("RGB").resize((TW, TH)), (c * TW, r * TH))
        d.rectangle((0, r * TH, 330, r * TH + 16), fill=(0, 0, 0))
        d.text((4, r * TH + 2), f"{name}  ({int(files[-1].stem) + 1} images)", fill=(255, 255, 0))
    out = root / f"_sheet_{page // ROWS}.jpg"
    sheet.save(out, quality=85)
    print(out)
