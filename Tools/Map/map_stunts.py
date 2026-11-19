"""Cascades : tremplins posés au bord des routes (modèles « stunt_ramp_stone » et « stunt_ramp_road » du pack alpin,
Tools/Blender/alp_stunt.py).

Chaque tremplin est sur une bande de lancement goudronnée, à droite de la chaussée dans le sens de la marche : on quitte
la route, on prend le tremplin et on retombe plus loin sur l'accotement aplani. Le jeu (StuntJumps.cs) valide la
cascade quand la distance parcourue en l'air atteint « target » ; la prime « reward » n'est versée qu'une fois.

Un site est donné par un point proche de la route et un cap approximatif : l'arête, l'abscisse et le sens sont
retrouvés sur le réseau (les identifiants d'arêtes peuvent changer, pas les lieux).

Habillage selon l'environnement (style) : ouvrage routier en béton au bord de l'autoroute et près des villages, remblai
de pierres sèches et de terre ailleurs (campagne, routes de col). Un site peut imposer le sien.
"""
import math
import numpy as np

RAMPS = {"stone": "stunt_ramp_stone", "road": "stunt_ramp_road"}
URBAN = 250.0            # en deçà de cette distance du bord d'un village : habillage routier
RAMP_L = 17.0            # longueur du tablier (m) : entrée au sol → nez (comme alp_stunt.py)
RAMP_W = 5.6             # largeur hors tout
GAP = 2.2                # entre le bord de la chaussée et le flanc du tremplin (le décor du modèle, rochers et blocs
                         # sans collision, va jusqu'à 4,7 m de l'axe : il reste hors de la chaussée)
RUNIN = 70.0             # bande d'élan goudronnée avant le tremplin
HALF = 4.8               # demi-largeur de la bande aplanie
PAVED_AFTER = 14.0       # goudron après le nez (le reste de la réception est en herbe, aplanie)
PAVED_EDGE = 0.8         # le goudron de la bande s'arrête à cette distance du bord de la chaussée
UNDER = 0.3              # comme map_roads.UNDER : le sol passe 30 cm sous la chaussée
OPEN_BEFORE = 30.0       # glissière ouverte avant la bande d'élan (de quoi quitter la route en biais)
OPEN_AFTER = 20.0        # et après la réception (de quoi revenir sur la route)

# Pack de contenu de chaque tremplin ajouté par un pack (Scripts/Core/ContentPacks.cs) : id du site -> id du pack.
# Absent : jeu de base (la clé « pack » n'est alors pas écrite dans map.json).
PACK = {}

# id, noms, point près de la route (x, z), cap approximatif (dx, dz), distance à franchir (m), prime ($)
# (+ habillage imposé en option : "stone" ou "road" ; sinon choisi par style())
SITES = [
    ("plan", "Saut du Plan", "Le Plan Jump", (716.8, -1754.4), (1.0, 0.1), 8.0, 500),
    ("pres", "Saut des Prés", "Meadows Jump", (2067.0, 441.3), (0.0, 1.0), 15.0, 900),
    ("chenal", "Saut de La Chenal", "La Chenal Jump", (-1810.8, 595.5), (0.0, 1.0), 20.0, 1200),
    ("granges", "Saut des Granges", "Les Granges Jump", (1696.9, -409.3), (0.2, 1.0), 25.0, 1500),
    ("col", "Saut du Col", "Pass Jump", (440.8, 551.8), (-1.0, -0.1), 30.0, 2000),
    ("petit_pas", "Saut du Petit Pas", "Petit Pas Jump", (1460.9, 1749.6), (-1.0, 0.0), 40.0, 3000),
    ("port", "Saut du Port", "Le Port Jump", (1564.0, -1926.2), (0.9, 0.4), 45.0, 3500),
    ("gorge", "Saut de la Gorge", "Gorge Jump", (-1893.6, -1019.0), (0.0, -1.0), 60.0, 6000),
    ("plaine", "Saut de la Plaine", "Lowland Jump", (-619.9, -1751.3), (1.0, -0.1), 75.0, 9000),
    ("autoroute", "Saut de l'Autoroute", "Motorway Jump", (-497.4, 2130.9), (1.0, -0.1), 90.0, 12000),
]


def landing(target):
    """Longueur aplanie après le nez : de quoi retomber bien au-delà de la distance demandée."""
    return target * 1.7 + 45.0


def _tangent(P, i):
    a, b = P[max(i - 2, 0)], P[min(i + 2, len(P) - 1)]
    tx, tz = b[0] - a[0], b[1] - a[1]
    n = math.hypot(tx, tz) or 1.0
    return tx / n, tz / n


def frame(e, i, direction, target):
    """Géométrie d'un site au point i de l'arête e, parcourue dans le sens direction (+1 : de a vers b).
    Renvoie un dict : entrée du tremplin (ox, oz), cap (tx, tz), axe de la bande (u, x, z, altitude de la route)."""
    P = e["pts"]
    tx, tz = _tangent(P, i)
    tx, tz = tx * direction, tz * direction
    rx, rz = tz, -tx                                   # droite du sens de marche
    off = e["hw"] + e["sidewalk"] + GAP + RAMP_W / 2
    ox, oz = P[i, 0] + rx * off, P[i, 1] + rz * off
    u = np.arange(-RUNIN, RAMP_L + landing(target) + 0.1, 2.0)
    cx, cz = ox + tx * u, oz + tz * u
    # altitude et distance de la route au droit de chaque point de l'axe
    d = np.hypot(P[None, :, 0] - cx[:, None], P[None, :, 1] - cz[:, None])
    j = d.argmin(axis=1)
    road_y, road_d = P[j, 2], d[np.arange(len(u)), j]
    return dict(edge=e, i=i, direction=direction, ox=float(ox), oz=float(oz), tx=tx, tz=tz, rx=rx, rz=rz, u=u, cx=cx,
                cz=cz, road_i=j, road_y=road_y, road_d=road_d, off=off, target=target)


def profile(f):
    """Altitude du sol de la bande : celle de la route au droit (moins UNDER) sur l'élan, de niveau sous le tremplin,
    puis retour progressif à celle de la route sur la réception."""
    u, y = f["u"], f["road_y"] - UNDER
    y0 = float(np.interp(0.0, u, y))
    k = np.clip((u - (RAMP_L + 4.0)) / 30.0, 0.0, 1.0)
    k = k * k * (3.0 - 2.0 * k)
    return np.where(u < 0.0, y, y0 + (y - y0) * k), y0


def evaluate(g, H, edges, f, river=None, water=None):
    """Mesures d'un site (pour le choisir) : dict, ou None s'il est impossible."""
    e, i, u = f["edge"], f["i"], f["u"]
    S = e["s"]
    s = S[i]
    # loin des carrefours, des ponts et des tunnels
    a0, a1 = (s - RUNIN - 60.0, s + RAMP_L + landing(f["target"]) + 40.0) if f["direction"] > 0 else \
             (s - RAMP_L - landing(f["target"]) - 40.0, s + RUNIN + 60.0)
    if a0 < 60.0 or a1 > S[-1] - 60.0:
        return None
    for (s0, s1, kind) in e["spans"]:
        if s1 > a0 - 20.0 and s0 < a1 + 20.0:
            return None
    edge_w = e["hw"] + e["sidewalk"]
    # la bande ne mord pas sur la chaussée, et l'élan reste collé à la route
    clear = f["road_d"] - edge_w - RAMP_W / 2
    if clear.min() < 0.6:
        return None
    run = (u <= RAMP_L)
    if clear[run].max() > 5.0:
        return None
    # aucune autre route dans la bande
    for o in edges:
        if o is e:
            continue
        Q = o["pts"]
        if (np.abs(Q[:, 0] - f["ox"]).min() > 400.0) or (np.abs(Q[:, 1] - f["oz"]).min() > 400.0):
            continue
        d = np.hypot(Q[None, :, 0] - f["cx"][:, None], Q[None, :, 1] - f["cz"][:, None]).min()
        if d < o["hw"] + o["sidewalk"] + HALF + 6.0:
            return None
    y, y0 = profile(f)
    # terrassement : écart entre le relief et la bande, sur l'axe et sur son bord extérieur
    h_c = np.asarray(g.sample(H, f["cx"], f["cz"]), dtype=np.float64)
    h_o = np.asarray(g.sample(H, f["cx"] + f["rx"] * HALF, f["cz"] + f["rz"] * HALF), dtype=np.float64)
    work = float(max(np.abs(h_c - y).max(), np.abs(h_o - y).max()))
    for mask in (river, water):
        if mask is not None:
            for off in (-HALF, 0.0, HALF + 3.0):
                fx, fz = g.index(f["cx"] + f["rx"] * off, f["cz"] + f["rz"] * off)
                ix = np.clip(np.round(fx).astype(int), 0, g.n - 1)
                iz = np.clip(np.round(fz).astype(int), 0, g.n - 1)
                if np.any(mask[iz, ix]):
                    return None
    grade = float(np.abs(np.gradient(f["road_y"], 2.0))[u < RAMP_L + 4.0].max())
    drop = float(y0 - y[-1])                            # dénivelé de la réception (positif : en descente)
    return dict(work=work, grade=grade, clear_max=float(clear.max()), drop=drop, s=float(s))


def style(f):
    """Habillage d'un site : « road » au bord de l'autoroute ou près d'un village, « stone » ailleurs (hameaux
    compris : quelques fermes ne font pas un environnement bâti)."""
    import map_def as D
    if f["edge"]["kind"] == "highway":
        return "road"
    for v in D.VILLAGES:
        if math.hypot(f["ox"] - v[2], f["oz"] - v[3]) < v[4] + URBAN:
            return "road"
    return "stone"


def resolve(edges):
    """Sites de SITES retrouvés sur le réseau : liste de cadres (frame) avec id, noms, target, reward, item."""
    out = []
    for (sid, fr, en, (x, z), (hx, hz), target, reward, *forced) in SITES:
        best = None
        for e in edges:
            if e["kind"] in ("ramp", "track"):
                continue
            P = e["pts"]
            d = np.hypot(P[:, 0] - x, P[:, 1] - z)
            i = int(d.argmin())
            if best is None or d[i] < best[0]:
                best = (d[i], e, i)
        _, e, i = best
        tx, tz = _tangent(e["pts"], i)
        f = frame(e, i, 1 if tx * hx + tz * hz >= 0 else -1, target)
        f.update(id=sid, fr=fr, en=en, reward=reward)
        f["style"] = forced[0] if forced else style(f)
        f["item"] = RAMPS[f["style"]]
        out.append(f)
    return out


def strips(sites):
    """Bandes à aplanir (map_roads.carve) : (axe [x, z, altitude du sol], demi-largeur)."""
    out = []
    for f in sites:
        y, _ = profile(f)
        out.append((np.stack([f["cx"], f["cz"], y], axis=1), HALF))
    return out


def paved(sites):
    """Aires goudronnées (map_export.masks) : rectangles (x, z, (demi-longueur, demi-largeur, rotation), altitude).
    Côté route, le goudron va presque jusqu'à la chaussée : la bande se lit comme un accès depuis la route."""
    out = []
    b0, b1 = PAVED_EDGE - GAP - RAMP_W / 2, RAMP_W / 2 + 0.6
    b = (b0 + b1) / 2
    for f in sites:
        u0, u1 = -RUNIN, RAMP_L + PAVED_AFTER
        m = (u0 + u1) / 2
        rot = math.degrees(math.atan2(f["tz"], f["tx"]))
        out.append((f["ox"] + f["tx"] * m + f["rx"] * b, f["oz"] + f["tz"] * m + f["rz"] * b,
                    ((u1 - u0) / 2, (b1 - b0) / 2, rot), 0.0))
    return out


def open_rails(sites):
    """Retire les glissières du côté du tremplin (e["rail"], bits 0-1), de l'avant de l'élan à la fin de la réception :
    rien ne sépare la route de la bande, à l'aller comme au retour."""
    for f in sites:
        e = f["edge"]
        S = e["s"]
        s = S[f["road_i"]]
        a, b = float(s.min()), float(s.max())
        if f["direction"] > 0:
            a, b = a - OPEN_BEFORE, b + OPEN_AFTER
        else:
            a, b = a - OPEN_AFTER, b + OPEN_BEFORE
        bit = 2 if f["direction"] > 0 else 1           # droite du sens de marche : bit 1 de a vers b, sinon bit 0
        e["rail"][(S >= a) & (S <= b)] &= ~np.uint8(bit)


def corridor(f, margin=1.5):
    """Coins de la bande (élan, tremplin, réception) élargie de margin."""
    u0, u1 = -RUNIN, RAMP_L + landing(f["target"])
    w = HALF + margin
    return [(f["ox"] + f["tx"] * a + f["rx"] * b, f["oz"] + f["tz"] * a + f["rz"] * b)
            for (a, b) in ((u0, -w), (u1, -w), (u1, w), (u0, w))]


def inside(f, x, z, margin=1.5):
    a = (x - f["ox"]) * f["tx"] + (z - f["oz"]) * f["tz"]
    b = (x - f["ox"]) * f["rx"] + (z - f["oz"]) * f["rz"]
    return -RUNIN - margin < a < RAMP_L + landing(f["target"]) + margin and abs(b) < HALF + margin


def populate(P, sites):
    """Pose les tremplins (objets de la carte) et réserve leurs bandes : ni arbres ni buissons dessus."""
    from map_place import yaw_of
    for f in sites:
        cs = corridor(f)
        P.claim(cs, f["ox"], f["oz"], 2.0)
        P.boxes.clear()                                # la réserve ne bloque que la végétation
        # origine du modèle : au sol, au milieu du tablier ; nez vers l'avant (+Z du modèle)
        mx, mz = f["ox"] + f["tx"] * RAMP_L / 2, f["oz"] + f["tz"] * RAMP_L / 2
        ys = [P.h(f["ox"] + f["rx"] * k, f["oz"] + f["rz"] * k) for k in (-RAMP_W / 2, 0.0, RAMP_W / 2)]
        f["y"] = min(ys) - 0.03
        f["yaw"] = yaw_of(f["tx"], f["tz"]) % 360.0
        P.add(f["item"], mx, mz, f["yaw"], y=f["y"])
    return sites


def clear(places, sites):
    """Retire de la liste les objets (panneaux, balises, bornes) posés dans une bande."""
    ramps = set(RAMPS.values())
    keep = [p for p in places if p["item"] in ramps or not any(inside(f, p["x"], p["z"]) for f in sites)]
    return keep, len(places) - len(keep)


def export(sites):
    """Cascades pour le jeu (map.json) : nez du tremplin (x, y, z), cap, distance à franchir, prime."""
    out = []
    for f in sites:
        out.append(dict(id=f["id"], fr=f["fr"], en=f["en"], x=round(f["ox"] + f["tx"] * RAMP_L, 2), y=round(f["y"], 2),
                        z=round(f["oz"] + f["tz"] * RAMP_L, 2), yaw=round(f["yaw"], 1), length=RAMP_L, width=RAMP_W,
                        target=float(f["target"]), reward=int(f["reward"])))
        if f["id"] in PACK:
            out[-1]["pack"] = PACK[f["id"]]
    return out
