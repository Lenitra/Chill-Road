"""Définition de la carte « Tour du massif » : relief, plans d'eau, réseau routier, lieux.

Une vallée fait le tour d'un massif central : l'autoroute la suit en boucle (viaduc sur le lac, deux tunnels), la
route de campagne aussi, par l'intérieur. Trois vallons montent du tour vers le col central.

Repère : x = est, z = nord, en mètres, carte centrée sur l'origine. Zone jouable ≈ 5 × 5 km, relief sur 6,4 km
(les bords remontent en cirque pour fermer l'horizon). Niveau du grand lac = 0.
"""

import math
import numpy as np

HALF = 3200.0        # demi-côté du relief
STEP = 4.0           # pas de la grille finale
LAKE_LEVEL = 0.0
SEED = 11

# Profil en travers des vallées : (distance au bord du fond plat, pente). La pente est interpolée entre les points.
# Tour du massif : versant raide, replat d'alpage, puis parois jusqu'aux sommets.
PROFILE_RING = [(0, 0.03), (80, 0.55), (380, 0.6), (470, 0.3), (580, 0.3), (680, 0.95), (1500, 0.95)]
# Vallons du col : auges glaciaires (fond plat, versants à 40-50 %, épaulement d'alpage, parois)
PROFILE_SIDE = [(0, 0.04), (60, 0.42), (380, 0.5), (520, 0.22), (740, 0.22), (950, 0.85)]
# Combes en cul-de-sac : petites auges suspendues
PROFILE_COMBE = [(0, 0.06), (70, 0.45), (300, 0.5), (420, 0.8)]

# Vallées : ligne de fond (x, z, altitude du fond, demi-largeur du fond plat). La rivière suit cette ligne.
VALLEYS = [
    # Sud : de la gorge du sud-ouest au lac
    dict(id="sud", profile=PROFILE_RING, river=12.0, pts=[
        (-1900, -1350, 78, 60), (-1700, -1780, 62, 120), (-1200, -1900, 52, 220), (-450, -2040, 40, 270),
        (350, -1960, 26, 300), (1000, -1860, 10, 300), (1500, -1700, 3, 340), (1850, -1300, 3, 320),
        (2000, -800, 9, 240)]),
    # Est : du lac au petit col du nord-est
    dict(id="est", profile=PROFILE_RING, river=7.0, river_to=3, pts=[
        (2000, -800, 9, 240), (1940, -150, 36, 270), (2030, 500, 68, 210), (1950, 1120, 104, 150),
        (1890, 1390, 140, 80), (1820, 1560, 168, 45)]),
    # Nord : haute vallée, du petit col vers l'ouest
    dict(id="nord", profile=PROFILE_RING, river=8.0, river_from=2, pts=[
        (1820, 1560, 168, 45), (1700, 1780, 166, 80), (1450, 1920, 164, 150), (750, 2050, 162, 260),
        (-200, 1940, 157, 290), (-1100, 2010, 152, 230), (-1700, 1700, 147, 180), (-1950, 1150, 140, 170)]),
    # Ouest : descente vers la gorge
    dict(id="ouest", profile=PROFILE_RING, river=10.0, pts=[
        (-1950, 1150, 140, 170), (-2040, 400, 118, 270), (-1910, -350, 98, 170), (-1960, -950, 84, 70),
        (-1900, -1350, 78, 60)]),
    # Vallons du col central : sud, nord et est
    dict(id="col_s", profile=PROFILE_SIDE, river=5.0, river_to=6, pts=[
        (-100, -2010, 34, 200), (-170, -1620, 54, 130), (-110, -1330, 72, 100), (-40, -1120, 150, 45),
        (40, -800, 178, 110), (-50, -470, 205, 95), (-20, -150, 278, 55), (60, 380, 300, 150)]),
    dict(id="col_n", profile=PROFILE_SIDE, river=5.0, river_from=1, pts=[
        (60, 380, 300, 150), (-60, 780, 282, 95), (40, 1080, 258, 70), (120, 1300, 196, 45),
        (60, 1620, 172, 100), (60, 1970, 158, 220)]),
    dict(id="col_e", profile=PROFILE_SIDE, river=5.0, river_from=1, pts=[
        (60, 380, 300, 150), (480, 470, 287, 95), (820, 380, 266, 80), (1000, 320, 196, 40),
        (1300, 370, 170, 85), (1480, 410, 118, 45), (1700, 380, 88, 120), (1990, 330, 60, 200)]),
    # Combes : alpage de l'ouest, cirque au-dessus du lac
    dict(id="combe_o", profile=PROFILE_COMBE, river=4.0, river_to=2, pts=[
        (-2000, 150, 112, 200), (-1500, 60, 215, 60), (-1080, 180, 340, 110), (-820, 330, 430, 60)]),
    dict(id="combe_lac", profile=PROFILE_COMBE, river=4.0, river_to=2, pts=[
        (1380, -1560, 6, 150), (1180, -1180, 110, 60), (960, -760, 270, 100), (800, -420, 390, 50)]),
]

# Plafond des sommets et cirque de bordure
PEAK_BASE, PEAK_VAR = 880.0, 300.0
RIM_WIDTH, RIM_HEIGHT = 900.0, 620.0

# Plans d'eau : super-ellipses (centre, demi-axes, angle en degrés, niveau, profondeur) ; « seed » = point de départ
# du remplissage (l'eau s'étend jusqu'aux rives réelles)
LAKES = [
    dict(id="lac", c=(1700, -1500), a=560, b=300, angle=35, level=LAKE_LEVEL, depth=38, seed=(1700, -1500)),
    dict(id="lac_col", c=(30, 440), a=72, b=46, angle=20, level=297.5, depth=8, seed=(30, 440)),
]

# Replats aménagés dans le relief naturel : (x, z, rayon, raccord, altitude ou None = moyenne locale)
PADS = [
    (50, 420, 140, 170, 301.0),       # col
    (-1080, 180, 80, 110, None),      # alpage
]

# Cônes de déjection aux débouchés des vallons : (x, z de l'apex, altitude de l'apex, rayon, pente). Les villages
# s'y posent, au sec, au-dessus du fond de vallée.
FANS = [
    (-150, -1660, 58, 420, 0.05),     # Le Plan (vallon du col, sud)
    (1720, 390, 92, 400, 0.055),      # Les Granges (vallon du col, est)
    (70, 1700, 178, 360, 0.05),       # Praz-Dessus (vallon du col, nord)
    (-1740, 120, 128, 360, 0.05),     # La Chenal (combe de l'alpage)
    (1270, -1420, 30, 320, 0.08),     # Le Port (delta du torrent du cirque, sur le lac)
]

# Emplacements de villages (zones plates réservées, sans forêt) : (id, nom, x, z, rayon)
VILLAGES = [
    ("plan", "Le Plan", 300, -1810, 340),
    ("port", "Le Port", 1190, -1470, 130),
    ("granges", "Les Granges", 1790, 120, 200),
    ("praz", "Praz-Dessus", 190, 1810, 200),
    ("chenal", "La Chenal", -1830, 290, 190),
    ("col", "Col des Trois Vallons", 50, 420, 150),
    ("alpage", "Alpage de la Combe", -1090, 215, 90),
]
# Hameaux : posés par roads() sur les replats trouvés par le traceur (id, nom, x, z, rayon)
HAMLETS = []


# ===== ROUTES (tout ce qui suit ne modifie pas le relief naturel : le cache du relief reste valable)
def zigzag(p, gr, legs, turn, R=15.0, log=False, until_h=None, near=None):
    """Lacets : traversées (longueur, ou dict d'options de Path.grade) séparées par des épingles alternées.
    until_h / near : arrivée, vérifiée sur chaque traversée."""
    for k, leg in enumerate(legs):
        opts = dict(leg) if isinstance(leg, dict) else dict(length=leg)
        p.grade(gr, log=log, until_h=until_h, near=near, **opts)
        if p.reached or k == len(legs) - 1:
            break
        p.hairpin(turn, R)
        turn = -turn
    return p


def ascend(p, gr, line, back=230.0, cycles=9, reach=40.0, R=11.0, until_h=None, near=None, side=1.0, ahead=350.0,
           max_leg=1600.0, log=False):
    """Montée d'un vallon en lacets. Une traversée vers l'amont : la route suit le fond tant qu'il est doux, puis
    prend le versant à pente constante jusqu'à ce que le torrent, qui monte plus vite, la rattrape. Là, une épingle,
    une traversée vers l'aval (longueur « back »), une épingle, et ainsi de suite. La route vise toujours un point
    de la ligne du vallon situé « ahead » mètres plus haut (ou plus bas) qu'elle : elle ne quitte pas le vallon.

    Une épingle de rayon R sur un versant de pente m déplace la route de 2R dans la pente : le terrain y monte de
    2R·m en quelques dizaines de mètres, bien plus que la route. Chaque traversée est donc tracée un peu moins raide
    que « gr » : elle arrive en léger remblai à l'épingle et en repart en léger déblai, et la route garde en moyenne
    la pente « gr », épingles comprises.
    side : versant pris quand la route part du torrent même (+1 = à gauche en montant)."""
    import map_lib as L
    net = p.net
    pts = net.lines[line]
    s = L.arclen(pts)
    up = 1.0 if pts[-1, 2] >= pts[0, 2] else -1.0          # sens de la ligne qui monte
    arc = math.pi * R

    def h_at(q):
        return float(net.g.sample(net.Hs, q[0], q[1]))

    def slope_at(q):
        gx, gz = net.g.gradient(net.Hs, q[0], q[1], eps=10.0)
        return math.hypot(float(gx), float(gz))

    def aim(direction):
        def f(x, z):
            d, i, t = L.point_polyline((x, z), pts)
            # loin du torrent, on vise plus près : la route rejoint d'abord le vallon
            look = min(ahead, max(80.0, 1.3 * d))
            target = min(max(s[i] + t * (s[i + 1] - s[i]) + direction * up * look, 0.0), s[-1])
            return (float(np.interp(target, s, pts[:, 0])), float(np.interp(target, s, pts[:, 1])))
        return f

    def turn():
        # épingle vers l'amont de la pente ; sur un fond presque plat, à l'opposé du torrent
        x, z = p.pos
        hx, hz = p.heading()
        gx, gz = net.g.gradient(net.Hs, x, z, eps=10.0)
        gx, gz = float(gx), float(gz)
        if math.hypot(gx, gz) > 0.07:
            return 1.0 if hx * gz - hz * gx > 0 else -1.0
        _, i, t = L.point_polyline((x, z), pts)
        qx, qz = pts[i, 0] + t * (pts[i + 1, 0] - pts[i, 0]), pts[i, 1] + t * (pts[i + 1, 1] - pts[i, 1])
        c = hx * (z - qz) - hz * (x - qx)
        if math.hypot(x - qx, z - qz) < 12.0:
            tx, tz = (pts[i + 1, 0] - pts[i, 0]) * up, (pts[i + 1, 1] - pts[i, 1]) * up
            c = side * (1.0 if hx * tx + hz * tz >= 0 else -1.0)
        return 1.0 if c > 0 else -1.0

    state = dict(d=0.0)          # hauteur du terrain au-dessus de la route idéale, au point courant

    def leg(length, floor, direction):
        n0, head0, pos0 = len(p.ctrl), p.heading(), p.pos
        g_t = gr
        for _ in range(4):
            del p.ctrl[n0:]
            p._heading = head0
            # vers l'amont, la route vise le vallon ; vers l'aval, elle garde simplement le cap pris à l'épingle
            p.grade(g_t, length=length, floor=floor, until_h=until_h, near=near, aim=aim(direction) if direction > 0 else None)
            if p.reached or (floor is not None and not p.floored):
                break
            # épingle à venir : la moitié de son ressaut est prise avant, en remblai
            half = max(0.0, (2.0 * R * slope_at(p.pos) - arc * gr) * 0.5)
            g_new = min(gr, max(0.02, gr - (state["d"] + half) / max(p.last_len, 60.0)))
            if abs(g_new - g_t) < 0.003:
                break
            g_t = g_new
        h0, h1 = h_at(pos0), h_at(p.pos)
        state["d"] = h1 - (h0 - state["d"] + gr * p.last_len)
        if log:
            print("  %s %s : %.0f m à %.1f %%, h %.0f -> %.0f, fin (%.0f, %.0f), écart %+.1f" % (
                p.id, "amont" if direction > 0 else "aval ", p.last_len, g_t * 100, h0, h1, p.pos[0], p.pos[1], state["d"]))

    def hairpin():
        h0 = h_at(p.pos)
        p.hairpin(turn(), R)
        state["d"] = h_at(p.pos) - (h0 - state["d"] + gr * arc)

    for k in range(cycles):
        leg(max_leg, (line, reach), 1.0)
        if p.reached or not p.floored:
            break
        hairpin()
        leg(back, None, -1.0)
        if p.reached:
            break
        hairpin()
    return p


def roads(net):
    """Réseau routier. Les commandes sont décrites dans map_roads.Path."""
    import map_roads as R
    import map_lib as L
    N = net.node
    LOG = True

    # --- Autoroute : boucle fermée autour du massif (sens du tracé : vers l'est au sud, le nord à l'est, l'ouest
    # au nord, le sud à l'ouest ; la gauche du tracé est donc l'intérieur de la boucle)
    N("a_sud", -300, -2195)
    lifts = [(1680, -1510, 14.0, 520, 300),          # viaduc du lac
             (-1870, -760, 8.0, 120, 250),           # viaduc de la gorge (rivière et route de la vallée)
             (-1160, -1780, 8.5, 110, 230)]          # viaduc du sud-ouest (route de la vallée et rivière)
    au = net.road("autoroute", "highway", "a_sud", closed=True, lift=lifts, smooth=40.0)
    au.to((350, -2130), (850, -2065))
    au.to((1180, -2010)).to((1700, -1560), (2175, -1010)).bridge(2)                 # viaduc du lac
    au.to((2165, -550), (2135, -150), (2190, 480))
    au.to((2110, 1000)).to((2085, 1220), (2050, 1520), (1920, 1830), (1650, 2040), (1300, 2120)).bridge(5, "tunnel")
    au.to((750, 2205), (300, 2170), (-200, 2110), (-700, 2150), (-1150, 2160))
    au.to((-1560, 2010), (-1880, 1780), (-2090, 1350), (-2110, 1150), (-2215, 400))
    # sud-ouest : viaduc par-dessus la gorge, tunnel sous l'éperon, viaduc à la sortie
    au.to((-2090, -300)).to((-1815, -850), (-1740, -1000)).bridge(2)
    au.to((-1560, -1300), (-1380, -1560), (-1230, -1700)).bridge(4, "tunnel")
    au.to((-960, -1960)).bridge(2)
    au.to((-720, -2110))

    # --- Échangeurs : un par côté de la boucle (nœuds <nom>_l à l'intérieur, <nom>_r à l'extérieur)
    for name, at in (("ech_s", (350, -2130)), ("ech_e", (2135, -150)), ("ech_n", (300, 2170)), ("ech_o", (-2215, 400))):
        R.interchange(net, au, name, at, kind="main")

    # --- Route de la vallée : tracée sur le relief de village en village, autour du massif. Chaque carrefour est
    # posé sur un tronçon droit (36 m de part et d'autre), au sec, à l'écart des torrents.
    rt = net.router
    del HAMLETS[:]
    ring = [
        ("r_col_s", (0, -1870)),         # Le Plan : route du col (sud)
        ("r_ech_s", (350, -1800)),       # Le Plan : échangeur sud, route des hameaux
        ("r_plage", (880, -1695)),       # chemin de la plage
        ("r_port", (1180, -1470)),       # Le Port
        ("r_ech_e", (1730, -150)),       # Les Granges : échangeur est
        ("r_col_e", (1900, 190)),        # Les Granges : route du col (est), au pied du cône
        ("col_ne", (1820, 1560)),        # Col du Petit Pas
        ("r_ech_n", (310, 1790)),        # Praz-Dessus : échangeur nord
        ("r_col_n", (200, 1820)),        # Praz-Dessus : route du col (nord), rive est du torrent
        ("r_ech_o", (-1810, 400)),       # La Chenal : échangeur ouest
        ("r_combe", (-1850, 30)),        # La Chenal : route du col de la Combe, rive sud du torrent
        ("r_villard", (-1880, -480)),    # route du Villard
    ]
    # chaque carrefour sur le replat le plus proche (un cône de déjection penche de 5 à 15 %)
    lowest = dict(r_col_e=62.0)               # carrefours à garder au fond de la vallée (pas sur le cône)
    ring = [(name, (x, z) if name == "col_ne" else rt.site(x, z, 70.0 if name not in lowest else 140.0,
                                                           hmax=lowest.get(name, 1e9))) for name, (x, z) in ring]
    for name, (x, z) in ring:
        N(name, x, z)
        net.fixed[name] = net.h(x, z) + (0.5 if name == "col_ne" else 0.3)
    lines = [net.lines[k] for k in ("sud", "est", "nord", "ouest")]

    def valley_dir(x, z, toward):
        """Cap de la vallée au point (axe du fond le plus proche), orienté vers toward."""
        best = None
        for P in lines:
            d, i, t = L.point_polyline((x, z), P)
            if best is None or d < best[0]:
                best = (d, P[i + 1, 0] - P[i, 0], P[i + 1, 1] - P[i, 1])
        _, dx, dz = best
        n = math.hypot(dx, dz) or 1.0
        dx, dz = dx / n, dz / n
        if dx * toward[0] + dz * toward[1] < 0:
            dx, dz = -dx, -dz
        return (dx, dz)

    heads = {}
    for k, (name, (x, z)) in enumerate(ring):
        px, pz = ring[k - 1][1]
        qx, qz = ring[(k + 1) % len(ring)][1]
        heads[name] = valley_dir(x, z, (qx - px, qz - pz))
    heads["col_ne"] = (-0.62, 0.78)                 # le col est franchi en biais, du sud-est au nord-ouest
    HALF = 36.0

    def through(p, name, kind=None, **kw):
        x, z = net.nodes[name]
        t = heads[name]
        p.route((x - t[0] * HALF, z - t[1] * HALF), kind=kind, goal_heading=t, label="%s>%s" % (p.id, name), **kw)
        p.node(name)
        p.to((x + t[0] * HALF, z + t[1] * HALF))
        p._heading = t
        return p

    v = net.road("vallee", "main", "r_col_s", closed=True)
    t0 = heads["r_col_s"]
    v.to((ring[0][1][0] + t0[0] * HALF, ring[0][1][1] + t0[1] * HALF))
    v._heading = t0
    for name in ("r_ech_s", "r_plage", "r_port", "r_ech_e", "r_col_e"):
        through(v, name)
    # Col du Petit Pas : la route de vallée franchit le petit col, en lacets s'il le faut
    through(v, "col_ne", kind="mountain", gmax=0.065, hair_cost=160.0)
    through(v, "r_ech_n", kind="mountain", gmax=0.065, hair_cost=160.0)
    for name in ("r_col_n", "r_ech_o", "r_combe", "r_villard"):
        through(v, name)
    # Gorge du sud-ouest, puis retour au Plan
    v.route((-1890, -1250), label="vallee>gorge")
    x, z = ring[0][1]
    v.route((x - t0[0] * HALF, z - t0[1] * HALF), goal_heading=t0, label="vallee>r_col_s")

    # Branche d'un carrefour : deux points sur la perpendiculaire à la route, à gauche (side = +1 : vers le massif)
    # ou à droite (-1 : vers l'autoroute). Toute route secondaire part ainsi tout droit, à angle droit.
    VP = v.spline()
    VT = L.tangents(VP)

    def stub(node, side, dists=(24.0, 56.0)):
        x, z = net.nodes[node]
        i = int(np.argmin(np.hypot(VP[:, 0] - x, VP[:, 1] - z)))
        nx, nz = -VT[i, 1] * side, VT[i, 0] * side
        return [(x + nx * d, z + nz * d) for d in dists]

    def branch(chain, node, side, dists=(24.0, 56.0)):
        """Branche d'équerre sur n'importe quelle route déjà tracée (côté gauche = +1 dans son sens de tracé)."""
        P = L.catmull(np.array(chain.ctrl, dtype=np.float64), 2.5)
        T_ = L.tangents(P)
        x, z = net.nodes[node]
        i = int(np.argmin(np.hypot(P[:, 0] - x, P[:, 1] - z)))
        nx, nz = -T_[i, 1] * side, T_[i, 0] * side
        return [(x + nx * d, z + nz * d) for d in dists]

    def arrive(p, node, side, **kw):
        """Arrivée d'équerre sur la route de la vallée, au carrefour node."""
        s_ = stub(node, side)
        x, z = net.nodes[node]
        gx, gz = x - s_[1][0], z - s_[1][1]
        n = math.hypot(gx, gz)
        p.route(s_[1], goal_heading=(gx / n, gz / n), label="%s>%s" % (p.id, node), **kw)
        p.to(s_[0]).node(node)
        return p

    # --- Accès aux échangeurs : de la sortie du pont sur l'autoroute à la route de la vallée (pont sur la rivière)
    for name, node in (("s", "r_ech_s"), ("e", "r_ech_e"), ("n", "r_ech_n"), ("o", "r_ech_o")):
        lx, lz = net.nodes["ech_%s_l" % name]
        rx, rz = net.nodes["ech_%s_r" % name]
        dx, dz = lx - rx, lz - rz
        n = math.hypot(dx, dz)
        dx, dz = dx / n, dz / n
        a_ = net.road("acces_" + name, "main", "ech_%s_l" % name, smooth=14.0)
        a_.to((lx + dx * 30.0, lz + dz * 30.0))
        a_._heading = (dx, dz)
        arrive(a_, node, -1.0)

    # --- Routes de cols : trois montées vers le col central, tracées sur le relief. Au col, chaque route arrive par
    # sa branche (sud, nord, est), le chemin du lac part à l'ouest : quatre branches à angle droit.
    N("col", 150, 350)
    net.fixed["col"] = net.h(150, 350) + 0.3
    arms = dict(col_sud=((150, 280), (150, 314), (0.0, 1.0)), col_nord=((150, 420), (150, 386), (0.0, -1.0)),
                col_est=((220, 350), (186, 350), (-1.0, 0.0)), col_ouest=((80, 350), (114, 350), (1.0, 0.0)))
    for name, start in (("col_sud", "r_col_s"), ("col_nord", "r_col_n"), ("col_est", "r_col_e")):
        c = net.road(name, "mountain", start, smooth=14.0)
        c.to(*stub(start, 1.0))
        outer, inner, head = arms[name]
        c.route(outer, goal_heading=head, label=name)
        c.to(inner).node("col")

    # --- Hameaux : sur les replats des versants
    def hamlet(hid, name, near, r, hmin, hmax, radius=90):
        x, z = rt.site(near[0], near[1], r, hmin, hmax)
        N(hid, x, z)
        HAMLETS.append((hid, name, x, z, radius))
        return hid

    # Route de balcon (départementale) : du Plan, lacets jusqu'à l'épaulement du versant sud (au soleil), puis à
    # niveau vers 250-290 m, aux Hauts, au-dessus du cirque, aux Moulins ; descente en lacets sur Les Granges
    RD = dict(wiggle=0.25, noise=0.3)
    hamlet("h_hauts", "Les Hauts", (460, -1212), 60, 220, 290)
    hamlet("h_moulins", "Les Moulins", (1148, -488), 60, 220, 300)
    b_ = net.road("balcon", "mountain", "r_ech_s", smooth=12.0)
    b_.to(*stub("r_ech_s", 1.0))
    b_.route("h_hauts", label="balcon>hauts", **RD).node("h_hauts")
    b_.route("h_moulins", label="balcon>moulins", **RD).node("h_moulins")
    arrive(b_, "r_ech_e", 1.0, **RD)
    # Chemin de l'alpage du cirque, depuis Les Moulins : part d'équerre de la route de balcon, côté amont
    N("alp_cirque", *rt.site(880, -600, 160, 300, 420))
    t = net.road("chemin_cirque", "track", "h_moulins", smooth=12.0)
    t.to(*branch(b_, "h_moulins", 1.0, (18.0, 40.0)))
    t.route("alp_cirque", label="chemin_cirque").node("alp_cirque")

    # Route de l'alpage (départementale) : de La Chenal par la combe jusqu'à l'alpage, en cul-de-sac
    N("alpage", -1105, 240)
    w_ = net.road("route_alpage", "mountain", "r_combe", smooth=14.0)
    w_.to(*stub("r_combe", 1.0))
    w_.route("alpage", label="route_alpage", **RD).node("alpage")

    # Les Chalets : versant nord, au soleil, au-dessus de l'autoroute (depuis l'échangeur nord)
    hamlet("h_chalets", "Les Chalets", (520, 2470), 180, 190, 330)
    lx, lz = net.nodes["ech_n_r"]
    ox_, oz_ = net.nodes["ech_n_l"]
    dx, dz = lx - ox_, lz - oz_
    n = math.hypot(dx, dz)
    h_ = net.road("route_chalets", "lane", "ech_n_r", smooth=12.0)
    h_.to((lx + dx / n * 30.0, lz + dz / n * 30.0))
    h_._heading = (dx / n, dz / n)
    h_.route("h_chalets", label="route_chalets", **RD).node("h_chalets")

    # Le Villard : épaulement du versant ouest (soleil de l'après-midi), depuis la sortie de la gorge
    hamlet("h_villard", "Le Villard", (-1560, -560), 200, 130, 210)
    v_ = net.road("route_villard", "lane", "r_villard", smooth=12.0)
    v_.to(*stub("r_villard", 1.0))
    v_.route("h_villard", label="route_villard", **RD).node("h_villard")

    # Route du lac (départementale) : de la vallée à la plage, puis le long de la rive sud, sous le viaduc de
    # l'autoroute, jusqu'au hameau des Rives
    N("plage", 1140, -1740)
    hamlet("h_rives", "Les Rives", (1950, -1900), 250, 4, 60)
    l_ = net.road("route_lac", "mountain", "r_plage", smooth=12.0)
    l_.to(*stub("r_plage", -1.0))
    l_.route("plage", label="route_lac>plage", **RD).node("plage")
    l_.route("h_rives", label="route_lac>rives", **RD).node("h_rives")


def pois(nodes, edges):
    """Lieux-dits affichés par le jeu (id, noms, position, rayon) ; « depart » : point de départ, r = cap (degrés,
    0 = nord, 90 = est), sur la route de la vallée, loin de tout carrefour."""
    out = []
    e = next(e for e in edges if e["a"] == "r_col_s" and e["b"] == "r_ech_s")
    i = int(np.searchsorted(e["s"], min(150.0, e["s"][-1] * 0.45)))
    dx, dz = e["pts"][i + 2, 0] - e["pts"][i, 0], e["pts"][i + 2, 1] - e["pts"][i, 1]
    out.append(dict(id="depart", fr="", en="", x=round(float(e["pts"][i, 0]), 1), z=round(float(e["pts"][i, 1]), 1),
                    r=round(math.degrees(math.atan2(dx, dz)) % 360.0, 1)))
    left = {"plan", "port", "chenal"}
    for (vid, name, x, z, r) in VILLAGES + HAMLETS:
        en = dict(col="Three Valleys Pass", alpage="Combe Pasture").get(vid, name)
        out.append(dict(id=vid, fr=name, en=en, x=float(x), z=float(z), r=float(r) * 1.6, side=-1 if vid in left else 1))
    out.append(dict(id="lac", fr="Lac de l'Envers", en="Lake Envers", x=1700.0, z=-1500.0, r=520.0))
    out.append(dict(id="col_ne", fr="Col du Petit Pas", en="Petit Pas Pass", x=1820.0, z=1560.0, r=150.0))
    out.append(dict(id="gorge", fr="Gorge du Sud-Ouest", en="South-West Gorge", x=-1900.0, z=-1300.0, r=230.0))
    out.append(dict(id="plage", fr="La Plage", en="La Plage", x=1140.0, z=-1740.0, r=120.0))
    return out


# Fenêtres des aperçus détaillés (x0, z0, x1, z1)
VIEWS = {
    "so": (-2400, -2400, -400, -400),
    "se": (400, -2400, 2400, -400),
    "ne": (1000, 800, 2400, 2300),
    "col": (-700, -1900, 1900, 1900),
    "plan": (-300, -2150, 800, -1450),
}
