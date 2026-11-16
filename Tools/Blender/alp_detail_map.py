"""Carte des motifs de détail du pack alpin (style cartoon, 2026-11-16) : une image 128 x 1 dont chaque pixel donne
le motif dessiné par le shader NoHesi/Toon sur la colonne de palette correspondante (traits sombres fins, sans
texture) : 1 = planches horizontales (bois), 2 = appareil de pierre (moellons), 3 = rangs de tuiles / lauzes,
4 = écorce (stries verticales), 0 = rien.

python Tools/Blender/alp_detail_map.py      (sans Blender ; lit les noms de colonnes dans alp_lib.py)
"""
import os
import re

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "NoHesi", "Models", "AlpPack", "AlpPack_Detail.png")
NCOL = 128

PATTERNS = {
    1: ["wood", "barn", "wood_dark", "wood_honey", "wood_grey", "wood_red"],
    2: ["wall_stone", "stone", "stone_dark", "stone_warm"],
    3: ["roof_tile", "roof_tile_b", "roof_slate", "roof_slate_b", "lauze", "lauze_b", "roof_brown", "roof_brown_b",
        "roof_red", "roof_red_b"],
    4: ["bark", "bark_grey"],
}


def palette_names():
    src = open(os.path.join(HERE, "alp_lib.py"), encoding="utf-8").read()
    i = src.index("PALETTE = [")
    j = src.index("]\n", i)
    return re.findall(r'\("([a-z_0-9]+)",', src[i:j])


def main():
    names = palette_names()
    img = Image.new("RGBA", (NCOL, 1), (0, 0, 0, 255))
    used = 0
    for pid, cols in PATTERNS.items():
        for c in cols:
            if c in names:
                img.putpixel((names.index(c), 0), (pid, 0, 0, 255))
                used += 1
    img.save(os.path.abspath(OUT))
    print("motifs", used, "->", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
