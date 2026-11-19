"""Placement de l'herbe des prés (GrassField) : carte d'herbe cuite d'après la carte exportée.

Lit Resources/Map (map.json, hauteurs, masques, annexes) et le manifeste du pack, écrit Resources/Map/map_grass.bytes
(4 octets par nœud de la grille, même grille que map_masks) :
  0 : couverture du milieu (0-1) : herbe drue au pré, clairsemée sous bois, sur la roche, les pentes raides, la neige ;
      la route n'y est pas (GrassField la lit au plus fin dans map_info), les bâtiments non plus (canal 3)
  1 : hauteur de l'herbe du milieu en cm (bits 0-6, jusqu'à 1,27 m) : pâture, friche haute en lisière et au bord de
      l'eau, gazon des villages, accotements fauchés, bande tondue de l'aéroport, pelouse rase de l'alpage ;
      bit 7 = herbe entretenue (gazon, accotement, aérodrome) : régulière et plus claire, sans fleurs hautes
  2 : part de prés de fauche (0-1) : fonds de vallée et replats exploités ; GrassField y découpe des parcelles
      (fauchée, repousse, foin haut) qui remplacent la hauteur du milieu
  3 : distance au bâti (m, (d + 2) × 8, de -2 à 29,9) : murs, aires de l'aéroport, tremplins ; distance signée,
      lissée entre les nœuds, elle donne un bord net au pied des murs

Lancé à la fin de map_bake.py ; seul : python map_grass.py [--preview]  (aperçu dans out/grass_*.png)
"""
import json
import math
import os
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
import sys
import time
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import map_manifest  # noqa: E402

RES = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Resources", "Map"))
OUT = os.path.join(HERE, "out")

# Objets posés au sol qui chassent l'herbe (emprise au sol du manifeste) ; le reste (panneaux, balises, mâts, sièges
# de télésiège, avions dont les ailes passent au-dessus de l'herbe) la laisse pousser
CLEAR_CATEGORIES = ("chalet", "townhouse", "landmark", "farm", "ski", "stunt")
CLEAR_ITEMS = ("airport_fuel", "busstop", "car_podium", "school_kiosk", "school_tirewall", "school_arch",
               "school_gantry", "fountain_plaza", "helicopter")
NEVER_CLEAR = ("chairlift_chair", "cablecar_cabin", "piste_marker", "piste_net", "slalom_gate_red", "slalom_gate_blue",
               "piste_sign_black", "piste_sign_blue", "piste_sign_green", "piste_sign_red", "ski_rack", "snowman",
               "chairlift_pylon", "cablecar_pylon", "snow_cannon")
# Bâtiments d'habitation : jardin en gazon autour
LAWN_CATEGORIES = ("chalet", "townhouse", "landmark")


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def fbm(X, Z, scale, octaves, seed):
    """Bruit de valeur lissé (sans dépendance), -0,5..0,5 environ."""
    rng = np.random.default_rng(seed)
    out = np.zeros(X.shape, dtype=np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        s = scale / (2 ** o)
        n = 64
        tab = rng.random((n + 1, n + 1)).astype(np.float32)
        tab[n, :] = tab[0, :]
        tab[:, n] = tab[:, 0]
        u = (X / s) % n
        v = (Z / s) % n
        i, j = u.astype(int), v.astype(int)
        fu, fv = u - i, v - j
        fu = fu * fu * (3 - 2 * fu)
        fv = fv * fv * (3 - 2 * fv)
        a = tab[j, i] * (1 - fu) + tab[j, i + 1] * fu
        b = tab[j + 1, i] * (1 - fu) + tab[j + 1, i + 1] * fu
        out += ((a * (1 - fv) + b * fv - 0.5) * amp).astype(np.float32)
        tot += amp
        amp *= 0.5
    return out / tot


def chamfer(mask, cells, step):
    """Distance approchée (m) aux nœuds du masque, jusqu'à « cells » nœuds (au-delà : cells × step)."""
    big = cells * step
    d = np.where(mask, 0.0, big).astype(np.float32)
    diag = step * math.sqrt(2.0)
    for _ in range(cells):
        nd = d.copy()
        np.minimum(nd[1:, :], d[:-1, :] + step, out=nd[1:, :])
        np.minimum(nd[:-1, :], d[1:, :] + step, out=nd[:-1, :])
        np.minimum(nd[:, 1:], d[:, :-1] + step, out=nd[:, 1:])
        np.minimum(nd[:, :-1], d[:, 1:] + step, out=nd[:, :-1])
        np.minimum(nd[1:, 1:], d[:-1, :-1] + diag, out=nd[1:, 1:])
        np.minimum(nd[:-1, :-1], d[1:, 1:] + diag, out=nd[:-1, :-1])
        np.minimum(nd[1:, :-1], d[:-1, 1:] + diag, out=nd[1:, :-1])
        np.minimum(nd[:-1, 1:], d[1:, :-1] + diag, out=nd[:-1, 1:])
        d = nd
    return np.minimum(d, big)


class Map:
    def __init__(self):
        with open(os.path.join(RES, "map.json"), encoding="utf-8") as f:
            self.json = json.load(f)
        j = self.json
        self.n, self.step, self.half = j["n"], float(j["step"]), float(j["half"])
        n = self.n
        raw = np.fromfile(os.path.join(RES, "map_heights.bytes"), "<u2").reshape(n, n).astype(np.float32)
        self.H = j["hmin"] + raw * ((j["hmax"] - j["hmin"]) / 65535.0)
        self.masks = np.fromfile(os.path.join(RES, "map_masks.bytes"), np.uint8).reshape(n, n, 4).astype(np.float32) / 255.0
        self.info = np.fromfile(os.path.join(RES, "map_info.bytes"), np.uint8).reshape(n, n, 4)
        c = np.arange(n, dtype=np.float32) * self.step - self.half
        self.X, self.Z = np.meshgrid(c, c)

    def window(self, x0, z0, x1, z1):
        s, h, n = self.step, self.half, self.n
        i0 = max(0, int(math.floor((x0 + h) / s)))
        i1 = min(n, int(math.ceil((x1 + h) / s)) + 1)
        k0 = max(0, int(math.floor((z0 + h) / s)))
        k1 = min(n, int(math.ceil((z1 + h) / s)) + 1)
        return slice(k0, k1), slice(i0, i1)

    def stamp(self, pts, radius, fn):
        """Appelle fn(sz, sx, d, t, i) sur les fenêtres autour de chaque segment d'une polyligne (x, z)."""
        P = np.asarray(pts, dtype=np.float64)
        for i in range(len(P) - 1):
            ax, az = P[i]
            bx, bz = P[i + 1]
            sz, sx = self.window(min(ax, bx) - radius, min(az, bz) - radius, max(ax, bx) + radius, max(az, bz) + radius)
            X, Z = self.X[sz, sx], self.Z[sz, sx]
            dx, dz = bx - ax, bz - az
            L2 = dx * dx + dz * dz
            t = np.clip(((X - ax) * dx + (Z - az) * dz) / max(L2, 1e-6), 0.0, 1.0)
            d = np.hypot(X - (ax + t * dx), Z - (az + t * dz))
            fn(sz, sx, d, t, i)


def box_distance(X, Z, cx, cz, hw, hd, yaw_deg):
    """Distance signée à un rectangle orienté (demi-côtés hw le long de x local, hd le long de z local)."""
    a = math.radians(yaw_deg)
    ca, sa = math.cos(a), math.sin(a)
    dx, dz = X - cx, Z - cz
    # yaw autour de Y (Unity) : x local = (cos, -sin), z local = (sin, cos)
    lx = dx * ca - dz * sa
    lz = dx * sa + dz * ca
    qx, qz = np.abs(lx) - hw, np.abs(lz) - hd
    out = np.hypot(np.maximum(qx, 0.0), np.maximum(qz, 0.0))
    return out + np.minimum(np.maximum(qx, qz), 0.0)


def bake(preview=False):
    t0 = time.time()
    m = Map()
    j = m.json
    X, Z, H, step = m.X, m.Z, m.H, m.step
    forest, dry, rock, dirt = (m.masks[..., k] for k in range(4))
    edge = m.info[..., 0].astype(np.float32) * 0.25 - 1.0
    flags = m.info[..., 1]
    water = (flags & 7) != 0
    hole = (flags & 16) != 0
    paved = m.info[..., 2].astype(np.float32) / 255.0
    snow = m.info[..., 3].astype(np.float32) / 255.0
    gz, gx = np.gradient(H, step)
    slope = np.hypot(gx, gz)

    big = fbm(X, Z, 260.0, 3, 501)          # grandes nappes (prés de fauche ou pâtures, friches)
    mid = fbm(X, Z, 70.0, 3, 502)           # nappes moyennes (hautes herbes, trouées)
    fine = fbm(X, Z, 18.0, 2, 503)          # bords irréguliers

    # ------------------------------------------------------------------ bâti : distance signée aux emprises
    items = map_manifest.load()
    clear = np.full(H.shape, 29.9, dtype=np.float32)
    lawn_d = np.full(H.shape, 60.0, dtype=np.float32)
    farm_d = np.full(H.shape, 60.0, dtype=np.float32)
    counted = 0
    for p in j["places"]:
        it = items.get(p["item"])
        if not it or p["item"] in NEVER_CLEAR:
            continue
        cat = it.get("category")
        if cat not in CLEAR_CATEGORIES and p["item"] not in CLEAR_ITEMS:
            continue
        w, d = it["footprint"]
        s = float(p.get("scale", 1.0))
        hw, hd = w * 0.5 * s + 0.25, d * 0.5 * s + 0.25
        r = math.hypot(hw, hd) + 62.0
        sz, sx = m.window(p["x"] - r, p["z"] - r, p["x"] + r, p["z"] + r)
        dist = box_distance(X[sz, sx], Z[sz, sx], p["x"], p["z"], hw, hd, p["yaw"])
        clear[sz, sx] = np.minimum(clear[sz, sx], dist)
        if cat in LAWN_CATEGORIES:
            lawn_d[sz, sx] = np.minimum(lawn_d[sz, sx], dist)
        if cat == "farm":
            farm_d[sz, sx] = np.minimum(farm_d[sz, sx], dist)
        counted += 1

    # Aéroport : piste, voie de circulation, bretelles, aires en béton, hélisurface ; tout autour, herbe tondue
    airfield_mown = np.zeros(H.shape, dtype=np.float32)
    af = j.get("airfield")
    if af:
        ax, az = af["ax"], af["az"]
        nx, nz = -az, ax
        U = (X - af["x"]) * ax + (Z - af["z"]) * az
        V = (X - af["x"]) * nx + (Z - af["z"]) * nz
        near = (np.abs(U) < af["half"] + 400.0) & (np.abs(V) < 400.0)
        rects = [(-af["half"], -af["hw"], af["half"], af["hw"]),
                 (-af["taxiHalf"] - af["taxiHw"], af["taxiV"] - af["taxiHw"], af["taxiHalf"] + af["taxiHw"], af["taxiV"] + af["taxiHw"])]
        for u in af.get("links", ()):
            rects.append((u - af["taxiHw"], af["taxiV"], u + af["taxiHw"], -af["hw"]))
        for key in ("apron", "park", "gate"):
            r_ = af.get(key)
            if r_:
                rects.append(tuple(r_))
        for (u0, v0, u1, v1) in rects:
            cu, cv = (u0 + u1) * 0.5, (v0 + v1) * 0.5
            hu, hv = abs(u1 - u0) * 0.5 + 0.3, abs(v1 - v0) * 0.5 + 0.3
            qx, qz = np.abs(U - cu) - hu, np.abs(V - cv) - hv
            dd = np.hypot(np.maximum(qx, 0.0), np.maximum(qz, 0.0)) + np.minimum(np.maximum(qx, qz), 0.0)
            clear = np.where(near, np.minimum(clear, dd), clear)
        heli = af.get("heli")
        if heli:
            hx = af["x"] + heli[0] * ax + heli[1] * nx
            hz = af["z"] + heli[0] * az + heli[1] * nz
            clear = np.minimum(clear, np.hypot(X - hx, Z - hz) - heli[2] - 0.3)
        # bande de piste et abords de l'aérogare tondus ras (les aérodromes sont fauchés), raccord doux au pré
        strip = np.maximum(np.abs(U) - af["half"] - 30.0, np.maximum(V - 30.0 - af["hw"], af["taxiV"] - 60.0 - V))
        apron_zone = np.maximum(np.abs(U - (-300.0)) - 150.0, np.maximum(np.abs(V - (-130.0)) - 75.0, 0.0))
        airfield_mown = np.maximum(1.0 - smooth(0.0, 25.0, strip + fine * 10.0), 1.0 - smooth(0.0, 25.0, apron_zone))
        airfield_mown = np.where(near, airfield_mown, 0.0)

    # ------------------------------------------------------------------ routes : accotements fauchés
    highway = np.zeros(H.shape, dtype=bool)
    for e in j["edges"]:
        if e["kind"] not in ("highway", "ramp"):
            continue
        P = np.asarray(e["pts"], dtype=np.float64).reshape(-1, 3)[:, [0, 2]]
        hw = float(e["hw"]) + 9.0

        def fn(sz, sx, d, t, i, hw=hw):
            highway[sz, sx] |= d < hw
        m.stamp(P[::2], hw, fn)
    # largeur fauchée : 2,8 m au bord des routes, 7 m le long de l'autoroute (avec un peu de jeu)
    mow_w = np.where(highway, 7.0, 2.8) + fine * 1.2
    verge = 1.0 - smooth(mow_w - 0.8, mow_w + 0.8, edge)

    # ------------------------------------------------------------------ eau : rives en herbes hautes
    # (le masque de terre couvre aussi le pied des routes : seules l'eau et les grèves loin d'une chaussée comptent)
    wet = water | ((dirt > 0.55) & (edge > 4.0))
    d_water = chamfer(wet, 6, step)
    bank = (1.0 - smooth(8.0, 16.0, d_water + mid * 10.0)) * smooth(0.0, 3.0, d_water)

    # ------------------------------------------------------------------ milieux
    village = 1.0 - smooth(18.0, 34.0, lawn_d + fine * 6.0)      # jardins autour des maisons
    lisiere = smooth(0.08, 0.3, forest) * (1.0 - smooth(0.45, 0.7, forest))
    under = smooth(0.45, 0.75, forest)
    alpine = smooth(0.35, 0.65, dry)
    high = smooth(260.0, 420.0, H)

    # Couverture
    cover = np.ones(H.shape, dtype=np.float32)
    cover *= 1.0 - smooth(0.3, 0.55, rock + fine * 0.2)
    cover *= 1.0 - smooth(0.35, 0.65, dirt + fine * 0.25)
    cover *= 1.0 - smooth(0.12, 0.28, snow + fine * 0.08)
    cover *= 1.0 - smooth(0.15, 0.4, paved + fine * 0.1)
    # pentes : clairsemée puis rien là où le terrain se peint en roche
    cover *= 1.0 - 0.45 * smooth(0.45, 0.65, slope) - 0.55 * smooth(0.62, 0.85, slope + fine * 0.15)
    # sous-bois : quelques touffes, des clairières plus herbeuses
    cover *= 1.0 - under * (0.78 - 0.35 * smooth(0.05, 0.25, mid))
    # alpage : gazon ras, trouées de cailloux
    cover *= 1.0 - alpine * 0.35 * smooth(0.0, 0.2, fine + mid * 0.5)
    # cour de ferme piétinée
    cover *= 1.0 - 0.6 * (1.0 - smooth(2.0, 9.0, farm_d + fine * 4.0))
    cover[water | hole] = 0.0
    cover = np.clip(cover, 0.0, 1.0)

    # Hauteur du milieu : pâture (0,3-0,45), nappes de hautes herbes (0,6-0,75)
    tall = smooth(0.08, 0.22, mid + big * 0.4)
    height = 0.4 + 0.14 * smooth(-0.15, 0.15, big) + 0.3 * tall
    height = height * (1.0 - 0.35 * high)
    # friche haute en lisière, rives
    height = np.maximum(height, 0.78 * lisiere * (1.0 - alpine))
    height = height + (0.88 - height) * bank * (1.0 - alpine)
    # sous-bois, alpage, pentes fortes : herbe courte
    height = height + (0.26 - height) * under
    height = height + (0.24 + 0.06 * smooth(-0.2, 0.2, mid) - height) * alpine
    height = height + (0.26 - height) * smooth(0.55, 0.8, slope) * (1.0 - bank)
    # neige proche, roche : rase
    height = height + (0.18 - height) * smooth(0.04, 0.16, snow)
    # gestion humaine : gazon des jardins, accotements, aérodrome (assez haute pour se lire comme de l'herbe : la
    # différence avec le pré se fait par la régularité et la teinte)
    height = height + (0.15 - height) * village
    height = height + (0.18 - height) * verge
    height = height + (0.18 - height) * airfield_mown
    height = np.clip(height, 0.1, 1.2)
    managed = np.maximum(np.maximum(village, verge), airfield_mown) > 0.5

    # Prés de fauche : fonds de vallée et replats sous l'alpage, une bonne part seulement (le reste reste en pâture)
    field = (1.0 - smooth(0.16, 0.3, slope)) * (1.0 - smooth(300.0, 380.0, H + big * 80.0))
    field *= 1.0 - smooth(0.05, 0.2, forest) * 1.0
    field *= 1.0 - alpine
    field *= smooth(-0.08, 0.06, big + mid * 0.35)
    field *= 1.0 - smooth(0.02, 0.15, snow)
    field *= 1.0 - bank
    field *= 1.0 - village
    field *= 1.0 - airfield_mown
    field *= 1.0 - verge
    field *= smooth(1.0, 4.0, d_water)
    field = np.clip(field, 0.0, 1.0)
    field[cover < 0.05] = 0.0

    def u8(a):
        return np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8)
    h8 = np.clip(height * 100.0 + 0.5, 0, 127).astype(np.uint8) | (managed.astype(np.uint8) << 7)
    out = np.stack([u8(cover), h8, u8(field),
                    np.clip((clear + 2.0) * 8.0 + 0.5, 0, 255).astype(np.uint8)], axis=-1)
    path = os.path.join(os.environ.get("GRASS_OUT", RES), "map_grass.bytes")
    out.tofile(path)
    road = smooth(0.6, 1.4, edge)
    grassy = cover * road * smooth(0.1, 0.6, clear) > 0.5
    print("herbe : %s (%d emprises, %.0f %% du sol en herbe, %.0f %% de pres de fauche, %.1f s)"
          % (path, counted, 100.0 * grassy.mean(), 100.0 * (grassy & (field > 0.5)).mean(), time.time() - t0))
    if preview:
        previews(m, cover * road * smooth(0.1, 0.6, clear), height, field)
    return out


def previews(m, cover, height, field):
    from PIL import Image
    os.makedirs(OUT, exist_ok=True)
    # couverture en vert, hauteur en luminosité, prés de fauche en jaune
    img = np.zeros(cover.shape + (3,), dtype=np.float32)
    h = np.clip(height / 0.9, 0, 1)
    img[..., 1] = cover * (0.35 + 0.65 * h)
    img[..., 0] = cover * field * 0.8
    img[..., 2] = (m.info[..., 2] / 255.0) * 0.6 + ((m.info[..., 1] & 7) != 0) * 0.8
    img = (np.clip(img, 0, 1) * 255).astype(np.uint8)[::-1]
    Image.fromarray(img).resize((1200, 1200), Image.BILINEAR).save(os.path.join(OUT, "grass_map.png"))
    for name, (x, z, r) in dict(depart=(126, -1856, 160), airport=(700, 1880, 320), granges=(1790, 120, 260),
                                lac=(1140, -1740, 200), col=(50, 420, 260), station=(-1000, 230, 300)).items():
        sz, sx = m.window(x - r, z - r, x + r, z + r)
        Image.fromarray(img[::-1][sz, sx][::-1]).resize((600, 600), Image.NEAREST).save(os.path.join(OUT, "grass_%s.png" % name))


if __name__ == "__main__":
    bake(preview="--preview" in sys.argv)
