"""Montage de la bande-annonce de Chill Road (60 s, 1080p60) à partir des images filmées par TrailerDirector.cs.

    python Tools/Trailer/edit.py [--frames DOSSIER] [--out FICHIER.mp4] [--lang fr|en] [--preview] [--sheet]

- DOSSIER : un sous-dossier par plan (00000.jpg…), 3840x2160 (tournage) ou 960x540 une image sur deux (essai).
- --preview : 960x540 à 30 images/s, encodage rapide (pour vérifier le montage).
- --sheet : planche des images du montage (une par seconde), sans encoder la vidéo.
Le montage est calé sur la musique (music/make_music.py) : 120 à la minute, une mesure = 2 s.
Légendes en verre dépoli (flou du fond, comme l'interface du jeu), logo « Ligne de crête » tracé à la fin.
--outro : images de la fin rendue dans Blender (0000.jpg…, 1920x1080) ; sans elles, la fin est dessinée ici.
"""
import argparse
import math
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
FONTS = ROOT / "Assets/ChillRoad/Resources/Fonts"
FPS = 60
GOLD = (255, 194, 51)
CYAN = (89, 217, 255)
DARK = (14, 18, 24)

# ---------------------------------------------------------------- montage : (plan, début dans le plan (s), durée (s), options)
# fondu : fondu enchaîné depuis le plan précédent (s) ; flash : éclair blanc au début (temps forts de la musique)
CUTS = [
    ("a1_village_haut", 0.7, 4.0, {}),
    ("a2_col_lac", 0.3, 4.0, {"fondu": 0.6}),
    ("b1_village", 0.5, 2.0, {}),
    ("b2_rives", 0.6, 2.0, {}),
    ("b3_lacets", 0.6, 2.0, {}),
    ("b4_tunnel", 1.2, 2.0, {}),
    ("c1_trafic_c", 0.7, 4.0, {"flash": 1.0}),
    ("c2_drift_a", 0.6, 2.0, {}),
    ("c3_saut", 0.2, 2.0, {}),
    ("c4_orage", 0.8, 2.0, {}),
    ("c5_neige", 0.3, 2.0, {}),
    ("c6_course", 1.55, 2.0, {}),
    ("c7_nuit", 0.8, 2.0, {}),
    ("d1_voiture_0_miata", 0.15, 0.5, {"flash": 1.0}),
    ("d1_voiture_1_ae86", 0.15, 0.5, {}),
    ("d1_voiture_2_dmc12", 0.15, 0.5, {}),
    ("d1_voiture_3_mustang", 0.15, 0.5, {}),
    ("d1_voiture_4_a110", 0.15, 0.5, {}),
    ("d1_voiture_5_focusst", 0.15, 0.5, {}),
    ("d1_voiture_6_valkyrie", 0.15, 0.5, {}),
    ("d1_voiture_7_vision", 0.15, 0.5, {}),
    ("d2_garage", 0.4, 2.0, {}),
    ("d3_tuning", 0.5, 1.5, {}),
    ("d4_carte", 0.5, 1.5, {}),
    ("d5_jour_nuit", 0.9, 3.0, {}),
    ("d6_aeroport", 0.6, 2.0, {}),
    ("d7_terre", 0.3, 1.0, {}),
    ("d8_peel", 0.5, 1.0, {}),
    ("e1_couchant", 0.6, 4.0, {"fondu": 0.4}),
    ("OUTRO", 0.0, 8.0, {"fondu": 0.6}),
]

# Version anglaise : les plans d'interface sont retournés en anglais (--frames-ui) ; le trafic n'étant pas le même d'un
# tournage à l'autre, le plan de trafic et son début sont choisis à part
CUTS_EN = {"c1_trafic_c": ("c1_trafic_a", 2.4)}

# Légendes : (début, fin, français, anglais[, "top"])
CAPTIONS = [
    (1.0, 3.7, "Une vallée alpine, un col, des lacets", "An alpine valley, a mountain pass, hairpins"),
    (4.6, 7.6, "Roule à ton rythme", "Drive at your own pace"),
    (8.4, 11.7, "55 km de routes", "55 km of roads"),
    (12.3, 15.6, "Villages, lac, cols, autoroute", "Villages, lake, passes, motorway"),
    (16.5, 19.7, "Slalome dans le trafic", "Weave through traffic"),
    (20.25, 21.8, "Drift", "Drift"),
    (22.25, 23.8, "Tremplins", "Stunt jumps"),
    (24.25, 25.8, "Orages", "Storms"),
    (26.25, 27.8, "Neige", "Snow"),
    (28.25, 29.8, "Courses", "Races"),
    (30.25, 31.8, "Nuit", "Night"),
    (32.4, 35.7, "14 voitures", "14 cars"),
    (36.25, 37.8, "Ton garage", "Your garage", "top"),
    (38.2, 39.3, "Tuning", "Tuning", "top"),
    (39.7, 40.8, "Carte et GPS", "Map and GPS", "top"),
    (41.3, 43.7, "Jour, nuit, météo", "Day, night, weather"),
    (44.3, 47.6, "Seul ou entre amis, en ligne", "Alone or online with friends"),
]
END_LINE = ("Gratuit sur PC", "Free on PC")


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


class Source:
    """Images d'un tournage : dossier/plan/00012.jpg ; en essai, seules les images paires existent."""

    def __init__(self, root, size, alt=None):
        self.alt = Path(alt) if alt else None   # dossier prioritaire (plans d'interface en anglais)
        self.root = Path(root)
        self.size = size
        self.counts = {}

    def dir(self, shot):
        if self.alt and (self.alt / shot / "_done.txt").exists():
            return self.alt / shot
        return self.root / shot

    def count(self, shot):
        if shot not in self.counts:
            files = sorted(self.dir(shot).glob("*.jpg"))
            self.counts[shot] = int(files[-1].stem) + 1 if files else 0
        return self.counts[shot]

    def frame(self, shot, k):
        n = self.count(shot)
        if n == 0:
            return placeholder(self.size, shot)
        k = max(0, min(k, n - 1))
        for j in (k, k - 1, k + 1):
            p = self.dir(shot) / f"{j:05d}.jpg"
            if p.exists():
                im = Image.open(p)
                im.draft("RGB", self.size)
                im = im.convert("RGB")
                if im.size != self.size:
                    im = im.resize(self.size, Image.LANCZOS)
                return im
        return placeholder(self.size, shot)


def placeholder(size, label):
    im = Image.new("RGB", size, (40, 44, 52))
    ImageDraw.Draw(im).text((40, 40), label, fill=(255, 255, 255), font=font("Inter-Medium.ttf", max(16, size[1] // 20)))
    return im


def ease(u):
    u = min(1.0, max(0.0, u))
    return u * u * (3 - 2 * u)


def out_cubic(u):
    u = min(1.0, max(0.0, u))
    return 1 - (1 - u) ** 3


# ---------------------------------------------------------------- verre dépoli

def glass(frame, box, radius, k, tint=0.5, blur=None):
    """Panneau de verre sur l'image : fond flouté et assombri dans box, coins arrondis, liseré clair ; k = opacité."""
    x0, y0, x1, y1 = [int(round(v)) for v in box]
    w, h = x1 - x0, y1 - y0
    if w <= 2 or h <= 2 or k <= 0:
        return
    s = frame.size[1] / 1080
    region = frame.crop((x0, y0, x1, y1)).filter(ImageFilter.GaussianBlur(blur if blur else 22 * s))
    region = Image.blend(region, Image.new("RGB", (w, h), DARK), tint)
    ss = 3
    mask = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius * ss, fill=255)
    mask = mask.resize((w, h), Image.LANCZOS)
    edge = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(edge).rounded_rectangle((ss // 2, ss // 2, w * ss - 1 - ss // 2, h * ss - 1 - ss // 2), radius * ss, outline=255, width=max(2, int(1.4 * s * ss)))
    edge = edge.resize((w, h), Image.LANCZOS)
    region = Image.composite(Image.new("RGB", (w, h), (255, 255, 255)), region, edge.point(lambda v: int(v * 0.22)))
    frame.paste(region, (x0, y0), mask.point(lambda v: int(v * k)))


def spaced(draw, xy, text, fnt, fill, spacing):
    """Texte aux lettres espacées (spacing en pixels) ; renvoie la largeur."""
    x, y = xy
    for ch in text:
        draw.text((x, y), ch, font=fnt, fill=fill)
        x += fnt.getlength(ch) + spacing
    return x - xy[0] - spacing


def spaced_width(text, fnt, spacing):
    return sum(fnt.getlength(ch) for ch in text) + spacing * (len(text) - 1)


def caption(frame, text, k, top=False):
    """Légende en bas au centre (en haut sur les menus, dont le bas est occupé) : pastille de verre, point doré, texte blanc espacé."""
    W, H = frame.size
    s = H / 1080
    fnt = font("Inter-Medium.ttf", int(round(46 * s)))
    sp = 4.0 * s
    text = text.upper()
    tw = spaced_width(text, fnt, sp)
    ph, pad, dot = 94 * s, 46 * s, 13 * s
    pw = tw + pad * 2 + dot + 20 * s
    cx = W / 2
    cy = 96 * s - (1 - k) * 18 * s if top else H - 118 * s + (1 - k) * 18 * s
    box = (cx - pw / 2, cy - ph / 2, cx + pw / 2, cy + ph / 2)
    glass(frame, box, ph / 2, k, tint=0.56)
    layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    a = int(255 * k)
    x = box[0] + pad
    d.ellipse((x, cy - dot / 2, x + dot, cy + dot / 2), fill=GOLD + (a,))
    asc, desc = fnt.getmetrics()
    spaced(d, (x + dot + 20 * s, cy - (asc + desc) / 2 - 1 * s), text, fnt, (255, 255, 255, a), sp)
    frame.paste(layer, (0, 0), layer)


def end_line(frame, text, k):
    """Ligne de fin sous le logo : texte doré espacé, centré, sans panneau (le bas de l'image est sombre)."""
    W, H = frame.size
    s = H / 1080
    fnt = font("Inter-Medium.ttf", int(round(40 * s)))
    sp = 10 * s
    text = text.upper()
    tw = spaced_width(text, fnt, sp)
    layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    spaced(ImageDraw.Draw(layer), (W / 2 - tw / 2, H - 196 * s + (1 - k) * 10 * s), text, fnt, GOLD + (int(255 * k),), sp)
    frame.paste(layer, (0, 0), layer)


# ---------------------------------------------------------------- logo « Ligne de crête » (Marketing/logo/ligne_de_crete.svg)

RIDGE = [(2, 74), (62, 46), (96, 58), (158, 14), (204, 48), (240, 34), (296, 66)]


def bezier(p0, p1, p2, p3, n=40):
    out = []
    for i in range(n + 1):
        t = i / n
        a, b, c, d = (1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out


# « C 340 92, 372 34, 412 48 S 476 78, 518 56 » : le deuxième arc reprend la tangente du premier
ROAD = bezier((296, 66), (340, 92), (372, 34), (412, 48)) + bezier((412, 48), (452, 62), (476, 78), (518, 56))[1:]


def partial(points, u):
    """Début d'une ligne brisée, sur la fraction u de sa longueur."""
    if u <= 0:
        return []
    seg = [math.dist(points[i], points[i + 1]) for i in range(len(points) - 1)]
    want = sum(seg) * min(u, 1.0)
    out = [points[0]]
    for i, L in enumerate(seg):
        if want >= L:
            out.append(points[i + 1])
            want -= L
        else:
            t = want / L if L > 0 else 0
            out.append((points[i][0] + (points[i + 1][0] - points[i][0]) * t, points[i][1] + (points[i + 1][1] - points[i][1]) * t))
            break
    return out


def stroke(draw, pts, width, color):
    if len(pts) < 2:
        return
    draw.line(pts, fill=color, width=max(1, int(round(width))), joint="curve")
    r = width / 2
    for x, y in (pts[0], pts[-1]):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=color)


def logo_layer(size, t, line):
    """Logo tracé au temps t (s) : crête blanche (0-1,1 s), route dorée (0,9-2,1 s), CHILL ROAD (1,5-2,7 s), ligne du bas."""
    W, H = size
    s = H / 1080
    ss = 2
    layer = Image.new("RGBA", (W * ss, H * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    k = 1.75 * s * ss                       # repère du SVG (520 de large) → pixels
    ox, oy = W * ss / 2 - 260 * k, H * ss * 0.375 - 45 * k

    def T(p):
        return (ox + p[0] * k, oy + p[1] * k)

    ridge = [T(p) for p in partial(RIDGE, out_cubic(t / 1.1))]
    stroke(d, ridge, 2.4 * k, (255, 255, 255, 255))
    road = [T(p) for p in partial(ROAD, ease((t - 0.9) / 1.2))]
    stroke(d, road, 4.4 * k, GOLD + (255,))
    if t > 1.95:
        r = 5.2 * k * out_cubic((t - 1.95) / 0.3)
        x, y = T(ROAD[-1])
        d.ellipse((x - r, y - r, x + r, y + r), fill=GOLD + (255,))
    # CHILL ROAD : Inter Light, lettres espacées à 0,3 em, qui se resserrent en apparaissant
    a = ease((t - 1.5) / 1.0)
    if a > 0:
        fs = 72 * k * 0.98
        fnt = font("Inter-Light.ttf", int(round(fs)))
        sp = fs * (0.3 + 0.12 * (1 - out_cubic((t - 1.5) / 1.6)))
        text = "CHILL ROAD"
        tw = spaced_width(text, fnt, sp)
        spaced(d, (W * ss / 2 - tw / 2, oy + 118 * k), text, fnt, (255, 255, 255, int(255 * a)), sp)
    b = ease((t - 2.6) / 0.8)
    if b > 0 and line:
        fnt = font("Inter-Medium.ttf", int(round(32 * s * ss)))
        sp = 7 * s * ss
        text = line.upper()
        tw = spaced_width(text, fnt, sp)
        y = oy + 250 * k
        spaced(d, (W * ss / 2 - tw / 2, y), text, fnt, GOLD + (int(255 * b),), sp)
    return layer.resize((W, H), Image.LANCZOS)


def outro_frame(src, size, i, lang):
    """Fin dessinée ici (sans rendu Blender) : le viaduc au couchant, flouté et assombri, carte de verre et logo tracé."""
    t = i / FPS
    bg = src.frame("e2_fond_logo", 30 + i)
    k = ease(t / 1.4)
    s = size[1] / 1080
    if k > 0:
        soft = bg.filter(ImageFilter.GaussianBlur(14 * s * k))
        bg = Image.blend(soft, Image.new("RGB", size, DARK), 0.2 * k)
    W, H = size
    glass(bg, (W / 2 - 640 * s, H * 0.375 - 190 * s, W / 2 + 640 * s, H * 0.375 + 380 * s), 44 * s, ease((t - 0.2) / 1.0) * 0.9, tint=0.4, blur=30 * s)
    layer = logo_layer(size, t - 0.5, END_LINE[1 if lang == "en" else 0])
    bg.paste(layer, (0, 0), layer)
    return bg


# ---------------------------------------------------------------- image par image

def build(args):
    size = (960, 540) if args.preview else (1920, 1080)
    step = 2 if args.preview else 1
    src = Source(args.frames, size, args.frames_ui)
    # Fin rendue dans Blender (Tools/Trailer/outro) : images nommées par leur numéro (0000.jpg…0479.jpg), certaines
    # peuvent manquer (rendu une image sur 2 ou 4) : fondu entre les deux images rendues les plus proches
    outro_dir = Path(args.outro)
    outro_nums = sorted(int(f.stem) for f in outro_dir.glob("[0-9][0-9][0-9][0-9].jpg")) if outro_dir.exists() and not args.no_outro else []
    outro_files = outro_nums if len(outro_nums) >= 100 else []
    outro_cache = {}

    def outro_image(n):
        if n not in outro_cache:
            if len(outro_cache) > 6:
                outro_cache.clear()
            im = Image.open(outro_dir / f"{n:04d}.jpg").convert("RGB")
            outro_cache[n] = im if im.size == size else im.resize(size, Image.LANCZOS)
        return outro_cache[n]

    def outro_render(j):
        import bisect
        j = min(j, outro_nums[-1])
        i = bisect.bisect_right(outro_nums, j) - 1
        lo = outro_nums[max(i, 0)]
        hi = outro_nums[min(i + 1, len(outro_nums) - 1)]
        if hi == lo or j <= lo:
            return outro_image(lo).copy()
        return Image.blend(outro_image(lo), outro_image(hi), (j - lo) / (hi - lo))
    lang = 1 if args.lang == "en" else 0

    clips, t = [], 0
    for shot, start, dur, opt in CUTS:
        if lang and shot in CUTS_EN:
            shot, start = CUTS_EN[shot]
        n = int(round(dur * FPS))
        clips.append({"shot": shot, "src": int(round(start * FPS)), "t0": t, "n": n, "opt": opt})
        t += n
    total = t
    assert total == 60 * FPS, f"montage de {total / FPS} s"

    def clip_frame(c, j):
        """Image j du plan c (j peut dépasser sa durée : fondu vers le plan suivant)."""
        if c["shot"] == "OUTRO":
            if outro_files:
                im = outro_render(j)
                b = ease((j / FPS - 4.3) / 0.8)
                if b > 0:
                    end_line(im, END_LINE[lang], b)
                return im
            return outro_frame(src, size, j, args.lang)
        return src.frame(c["shot"], c["src"] + j)

    def render(f):
        ci = max(i for i, c in enumerate(clips) if c["t0"] <= f)
        c = clips[ci]
        j = f - c["t0"]
        im = clip_frame(c, j)
        fondu = int(round(c["opt"].get("fondu", 0) * FPS))
        if ci > 0 and j < fondu:
            p = clips[ci - 1]
            im = Image.blend(clip_frame(p, p["n"] + j), im, ease((j + 1) / (fondu + 1)))
        tsec = f / FPS
        for t0, t1, fr, en, *where in CAPTIONS:
            if t0 <= tsec < t1:
                k = min(out_cubic((tsec - t0) / 0.3), ease((t1 - tsec) / 0.25))
                caption(im, en if lang else fr, k, top=bool(where))
        flash = c["opt"].get("flash", 0)
        if flash and j < 14:
            a = flash * (1 - j / 14) ** 2 * 0.85
            im = Image.blend(im, Image.new("RGB", size, (255, 255, 255)), a)
        # Fin du plan qui précède un éclair : montée vers le blanc sur 6 images
        if ci + 1 < len(clips) and clips[ci + 1]["opt"].get("flash") and c["n"] - j <= 6:
            im = Image.blend(im, Image.new("RGB", size, (255, 255, 255)), 0.5 * (1 - (c["n"] - j - 1) / 6))
        if f < 36:
            im = Image.blend(Image.new("RGB", size, (0, 0, 0)), im, ease(f / 36))
        if f >= total - 40:
            im = Image.blend(im, Image.new("RGB", size, (0, 0, 0)), ease((f - (total - 40)) / 34))
        return im

    if args.sheet:
        cols, tw, th = 6, 320, 180
        times = [x * 0.5 + 0.25 for x in range(120)] if args.sheet == "dense" else [x + 0.5 for x in range(60)]
        rows = math.ceil(len(times) / cols)
        sheet = Image.new("RGB", (cols * tw, rows * th))
        d = ImageDraw.Draw(sheet)
        for i, ts in enumerate(times):
            im = render(int(ts * FPS)).resize((tw, th), Image.LANCZOS)
            sheet.paste(im, ((i % cols) * tw, (i // cols) * th))
            d.text(((i % cols) * tw + 6, (i // cols) * th + 4), f"{ts:.1f}", fill=(255, 255, 0), font=font("Inter-SemiBold.ttf", 15))
        out = Path(args.out).with_suffix(".jpg")
        sheet.save(out, quality=88)
        print(out)
        return

    import imageio_ffmpeg
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    fps_out = FPS // step
    cmd = [ffmpeg, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{size[0]}x{size[1]}", "-r", str(fps_out), "-i", "-",
           "-i", str(args.music), "-map", "0:v", "-map", "1:a",
           "-vf", "scale=out_color_matrix=bt709:out_range=tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
           "-c:v", "libx264", "-preset", "veryfast" if args.preview else "slow", "-crf", "23" if args.preview else str(args.crf), "-pix_fmt", "yuv420p",
           "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", "-t", "60", str(args.out)]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(0, total, step):
        proc.stdin.write(render(f).tobytes())
        if f % 300 == 0:
            print(f"{f / FPS:.0f} s", flush=True)
    proc.stdin.close()
    if proc.wait() != 0:
        sys.exit("ffmpeg : échec")
    print(args.out)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", default="E:/ChillRoad_Capture/trailer/final")
    ap.add_argument("--out", default=str(ROOT / "Marketing/trailer/ChillRoad_trailer_fr.mp4"))
    ap.add_argument("--frames-ui", default=None)
    ap.add_argument("--outro", default="E:/ChillRoad_Capture/trailer/outro")
    ap.add_argument("--no-outro", action="store_true")
    ap.add_argument("--music", default=str(HERE / "music/trailer_music.wav"))
    ap.add_argument("--lang", default="fr")
    ap.add_argument("--crf", type=int, default=17)
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--sheet", nargs="?", const="1", default=None)
    build(ap.parse_args())
