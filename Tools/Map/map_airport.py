"""Aéroport des Trois Vallons : petit aéroport de montagne dans la haute vallée nord, entre la route de la vallée et la
rivière, à l'est de l'échangeur nord. On y arrive par la route de la vallée : carrefour, route d'accès à deux voies
dans l'axe de l'aérogare, puis boucle de dépose autour d'un îlot en herbe (mâts, avion exposé, bouleaux), le parking
de part et d'autre ; un portail ouvert mène au tarmac, aux voies de circulation et à la piste : tout se roule.

- piste 10/28 de 880 × 30 m et voie de circulation parallèle reliée par quatre bretelles : de l'enrobé (maillages du
  jeu, Airfield.cs) posé sur une plate-forme aplanie en herbe rase ;
- tarmac et parking en béton (aires revêtues, comme la concession) ;
- aérogare, tour de contrôle, hangars, avions et hélicoptère en stationnement, manches à air, balisage lumineux,
  clôture côté ville (modèles : Tools/Blender/alp_airport.py).
Le jeu peint les marquages (seuils, numéros, axes, postes de stationnement, hélisurface) à partir de la fiche
« airfield » de map.json.

Repère local : origine au milieu de la piste, u le long de la piste (vers l'est, cap 095), v perpendiculaire (vers
le nord, la rivière) ; mètres.
"""
import math
import numpy as np

CENTER = (907.5, 1919.0)
HEADING = -5.09                    # angle de l'axe u dans le plan (x est, z nord), en degrés
HALF, HW = 440.0, 15.0             # piste : demi-longueur, demi-largeur
LEVEL = 162.6                      # niveau de la plate-forme (comme une chaussée : le sol passe 0,3 m dessous)
UNDER = 0.3
TAXI_V, TAXI_HW = -58.0, 8.0       # voie de circulation parallèle : axe et demi-largeur
TAXI_HALF = 432.0                  # elle va d'une bretelle d'extrémité à l'autre
LINKS = (-432.0, -150.0, 150.0, 432.0)        # bretelles vers la piste (abscisses u)
# Aires en béton (u0, v0, u1, v1) : tarmac (jusque sous le bord de la voie de circulation), parking, passage du portail
APRON = (-430.0, -128.0, -200.0, -62.0)
PARK = (-402.0, -190.0, -292.0, -146.0)
GATE = (-398.0, -150.0, -374.0, -124.0)
# Plate-forme aplanie (herbe rase hors des aires) : bande de piste et de voie de circulation, zone de l'aérogare
FLAT = [(-470.0, -76.0, 470.0, 30.0), (-440.0, -160.0, -130.0, -70.0), (-404.0, -192.0, -290.0, -150.0)]
JUNCTION = (-350.0, -210.0)        # carrefour sur la route de la vallée (recalé sur son tracé)
# Boucle de dépose devant l'aérogare (dans le parking, dans l'axe du carrefour) : deux lignes droites reliées par deux
# demi-cercles, autour d'un îlot en herbe. La route d'accès arrive au milieu de la ligne droite sud.
LOOP_V = (-181.0, -157.0)          # axes des lignes droites sud et nord (v)
LOOP_HALF = 14.0                   # demi-longueur des lignes droites
LOOP_HW = 2.6                      # demi-largeur de la chaussée de la boucle (route « lane »)
BAY, BAY_DEPTH = 2.6, 5.2          # places de parking : largeur, profondeur
HELIPAD = (-410.0, -96.0, 10.5)    # hélisurface : centre et rayon du cercle
NUMBERS = ("10", "28")             # numéros peints au seuil ouest (cap 095) et au seuil est (cap 275)

TERMINAL = (-350.0, -136.0)
TOWER = (-316.0, -137.0)
HANGARS = [(-272.0, -141.0), (-240.0, -141.0)]
FENCE_V = -138.0
# Postes de stationnement : (objet, u, v, cap du nez (du, dv))
PARKED = [
    ("plane_jet", -350.0, -100.0, (1.0, 0.0)),
    ("plane_trainer", -304.0, -106.0, (0.26, 0.97)),
    ("plane_tourer", -288.0, -106.0, (0.26, 0.97)),
    ("plane_tourer", -272.0, -106.0, (0.26, 0.97)),
    ("plane_trainer", -256.0, -106.0, (0.26, 0.97)),
    ("plane_tourer", -240.0, -106.0, (0.26, 0.97)),
    ("plane_tourer", -224.0, -106.0, (0.26, 0.97)),
    ("plane_trainer", -208.0, -106.0, (0.26, 0.97)),
    ("helicopter", HELIPAD[0], HELIPAD[1], (1.0, 0.0)),
    ("plane_glider", -172.0, -98.0, (0.5, 0.87)),
    ("plane_glider", -152.0, -112.0, (0.34, 0.94)),
]
# Mobilier du parvis, devant la façade de l'aérogare (objet, décalage le long de la façade) : l'entrée reste dégagée
FORECOURT = [("bike_rack", -19.0), ("planter", -15.5), ("bench", -12.0), ("bin", -9.8), ("bin", 8.6),
             ("info_board", 19.0)]          # l'abribus prend la place du banc à l'est de l'entrée
ZONE_R = 140.0                     # dégagement : plus d'arbre à moins de 85 m de l'axe, forêt entière au-delà de 140 m


def axes():
    a = math.radians(HEADING)
    return (math.cos(a), math.sin(a)), (-math.sin(a), math.cos(a))


def loc(u, v):
    """Point local (u, v) -> (x, z) de la carte."""
    (ax, az), (nx, nz) = axes()
    return float(CENTER[0] + u * ax + v * nx), float(CENTER[1] + u * az + v * nz)


def local(x, z):
    (ax, az), (nx, nz) = axes()
    dx, dz = x - CENTER[0], z - CENTER[1]
    return dx * ax + dz * az, dx * nx + dz * nz


def yaw_of(face):
    """Cap (degrés) d'un objet dont la façade (ou le nez) regarde la direction locale face = (du, dv)."""
    (ax, az), (nx, nz) = axes()
    dx, dz = face[0] * ax + face[1] * nx, face[0] * az + face[1] * nz
    return math.degrees(math.atan2(dx, dz)) % 360.0


def _rect(r, y):
    """Rectangle local (u0, v0, u1, v1) -> aire de map_roads (centre, (demi-côtés, rotation), altitude)."""
    u0, v0, u1, v1 = r
    x, z = loc((u0 + u1) / 2.0, (v0 + v1) / 2.0)
    return (x, z, ((u1 - u0) / 2.0, (v1 - v0) / 2.0, HEADING), y)


def pads():
    """Plate-forme aplanie (terrassement seul : le sol y reste en herbe)."""
    return [_rect(r, LEVEL) for r in FLAT]


def paved():
    """Aires en béton : tarmac, parking, passage du portail."""
    return [_rect(r, LEVEL) for r in (APRON, PARK, GATE)]


def junction(edges=()):
    """Carrefour (u, v) tel que construit : il est recalé sur le tracé de la route de la vallée."""
    acc = next((e for e in edges if e["id"] == "aero_acces"), None)
    return local(float(acc["pts"][0, 0]), float(acc["pts"][0, 1])) if acc is not None else JUNCTION


def junction_u(edges=()):
    """Abscisse u de l'axe de la route d'accès."""
    return junction(edges)[0]


def loop():
    """Boucle de dépose : (v de son centre, rayon de l'axe des demi-cercles)."""
    return (LOOP_V[0] + LOOP_V[1]) / 2.0, (LOOP_V[1] - LOOP_V[0]) / 2.0


def unpaved(edges=()):
    """Îlot de la boucle de dépose, laissé en herbe au milieu du parking : rectangle et deux disques. Son bord passe
    sous la chaussée de la boucle (le masque du sol n'a pas la finesse d'une bordure)."""
    ju = junction_u(edges)
    cv, r = loop()
    r += 0.5
    out = [(*loc(ju, cv), (LOOP_HALF, r, HEADING), LEVEL)]
    for s in (-1.0, 1.0):
        out.append((*loc(ju + s * LOOP_HALF, cv), r, LEVEL))
    return out


def bays(edges=()):
    """Rangées de places du parking : (u0, v0, u1, v1, profondeur) — ligne de fond de (u0, v0) à (u1, v1), les places
    s'ouvrent à gauche de ce sens. Deux colonnes aux bouts du parking, deux rangées le long de son bord sud, de part
    et d'autre de la route d'accès."""
    ju = junction_u(edges)
    south, top, bottom = PARK[1] + 1.0, PARK[3] - 6.0, PARK[1] + 6.0
    return [(PARK[0] + 1.5, top, PARK[0] + 1.5, bottom, BAY_DEPTH), (PARK[2] - 1.5, bottom, PARK[2] - 1.5, top, BAY_DEPTH),
            (PARK[0] + 8.0, south, ju - 10.0, south, BAY_DEPTH), (ju + 10.0, south, PARK[2] - 8.0, south, BAY_DEPTH)]


def clear(places):
    """Retire les balises et panneaux de virage que la signalisation automatique pose autour de la boucle de dépose
    (ses demi-cercles sont serrés comme une épingle, mais c'est une allée de parking)."""
    u0, v0, u1, v1 = PARK
    out = []
    for p in places:
        if p["item"] in ("chevron", "sign_hairpin", "sign_curve_l", "sign_curve_r", "delineator", "milestone"):
            u, v = local(p["x"], p["z"])
            if u0 - 6.0 <= u <= u1 + 6.0 and v0 - 6.0 <= v <= v1 + 6.0:
                continue
        out.append(p)
    return out, len(places) - len(out)


def strips():
    """Bandes d'enrobé (u0, v0, u1, v1) : piste, voie de circulation parallèle, bretelles."""
    out = [(-HALF, -HW, HALF, HW), (-TAXI_HALF - TAXI_HW, TAXI_V - TAXI_HW, TAXI_HALF + TAXI_HW, TAXI_V + TAXI_HW)]
    for u in LINKS:
        out.append((u - TAXI_HW, TAXI_V, u + TAXI_HW, -HW))
    return out


def corners(r):
    u0, v0, u1, v1 = r
    return [loc(u0, v0), loc(u1, v0), loc(u1, v1), loc(u0, v1)]


def roads(net, vallee, VP, VT):
    """Carrefour sur la route de la vallée, route d'accès à deux voies jusqu'au parking, boucle de dépose devant
    l'aérogare (plus de bout de route : on fait le tour de l'îlot et on repart)."""
    sx, sz = loc(*JUNCTION)
    i = int(np.argmin(np.hypot(VP[:, 0] - sx, VP[:, 1] - sz)))
    x, z = float(VP[i, 0]), float(VP[i, 1])
    net.node("r_aero", x, z)
    net.crossings.add("r_aero")
    vallee.split("r_aero", x, z)
    u, v = local(x, z)
    cv, r = loop()
    net.node("aero_parvis", *loc(u, LOOP_V[0]))        # arrivée de la route d'accès, au milieu de la ligne droite sud
    net.node("aero_depose", *loc(u, LOOP_V[1]))        # devant l'entrée de l'aérogare (une boucle à une arête n'est pas gérée)
    net.fixed["aero_parvis"] = LEVEL
    net.fixed["aero_depose"] = LEVEL
    a = net.road("aero_acces", "main", "r_aero", smooth=10.0)
    a.to(loc(u, v + 9.0), loc(u, LOOP_V[0] - 9.0)).node("aero_parvis")

    def arc(cu, a0):
        return [loc(cu + r * math.cos(math.radians(a)), cv + r * math.sin(math.radians(a))) for a in range(a0, a0 + 181, 30)]

    b = net.road("aero_boucle", "lane", "aero_parvis", closed=True, smooth=10.0, level=LEVEL)
    b.to(loc(u + LOOP_HALF * 0.5, LOOP_V[0]), *arc(u + LOOP_HALF, -90), loc(u + LOOP_HALF * 0.5, LOOP_V[1])).node("aero_depose")
    b.to(loc(u - LOOP_HALF * 0.5, LOOP_V[1]), *arc(u - LOOP_HALF, 90), loc(u - LOOP_HALF * 0.5, LOOP_V[0]))


def zone(edges=None):
    """Zone dégagée comme un village : pas de forêt aux abords de la piste, ni balises ni glissières sur l'accès."""
    # à l'est, la route de la vallée passe à 130 m de l'axe, en balcon : le dégagement s'arrête avant elle (elle garde
    # ses glissières et ses balises)
    out = [(*loc(u, 5.0), ZONE_R if u < 200.0 else ZONE_R - 20.0) for u in np.arange(-480.0, 481.0, 80.0)]
    out.append((*loc(-330.0, -120.0), 130.0))
    return out


def pois(edges=None):
    x, z = loc(-315.0, -100.0)
    return [dict(id="airport", fr="Aéroport des Trois Vallons", en="Trois Vallons Airport", x=round(x, 1), z=round(z, 1),
                 r=330.0, side=1)]


def export(edges=()):
    """Fiche « airfield » de map.json (Airfield.cs : enrobé et marquages). Tableaux à plat (JsonUtility)."""
    (ax, az), _ = axes()
    ju = junction_u(edges)
    return dict(x=CENTER[0], z=CENTER[1], y=round(LEVEL - UNDER, 2), ax=round(ax, 6), az=round(az, 6), half=HALF, hw=HW,
                taxiV=TAXI_V, taxiHw=TAXI_HW, taxiHalf=TAXI_HALF, links=list(LINKS), apron=list(APRON), park=list(PARK),
                gate=list(GATE), parkU=round(ju, 2), heli=list(HELIPAD), west=NUMBERS[0], east=NUMBERS[1],
                bays=[round(float(v), 2) for row in bays(edges) for v in row],
                stands=[v for (item, u, w, f) in PARKED if item.startswith("plane_") and item != "plane_glider"
                        for v in (u, w, f[0], f[1], 9.0 if item == "plane_jet" else 6.5)])


def populate(pl, edges):
    """Bâtiments, avions, balisage, clôture et mobilier."""
    from map_place import Placer
    y = LEVEL - UNDER
    for r in FLAT:
        u0, v0, u1, v1 = r
        cx, cz = loc((u0 + u1) / 2.0, (v0 + v1) / 2.0)
        pl.claim(Placer.corners(cx, cz, u1 - u0, v1 - v0, -HEADING), cx, cz, 1.0)
    N, S, E, W = (0.0, 1.0), (0.0, -1.0), (1.0, 0.0), (-1.0, 0.0)

    def put(item, u, v, face=N, flags=0, ground=False):
        """ground : posé sur le sol naturel (hors de la plate-forme) plutôt qu'au niveau de la plate-forme."""
        x, z = loc(u, v)
        if not pl.add(item, x, z, yaw_of(face), y=None if ground else y, flags=flags):
            missing.add(item)

    missing = set()
    INST = 1                                           # MapProps.Instanced : dessiné en instances, sans collision

    # --- Bâtiments : aérogare (façade côté parking), tour, hangars (portes sur le tarmac)
    put("airport_terminal", *TERMINAL, S)
    put("airport_tower", *TOWER, S)
    for h in HANGARS:
        put("airport_hangar", *h, N)
    # --- Appareils en stationnement, point d'avitaillement au bout du tarmac
    for (item, u, v, face) in PARKED:
        put(item, u, v, face)
    put("airport_fuel", APRON[0] + 8.0, -120.0, N)

    # --- Clôture côté ville, sur la ligne des bâtiments ; le passage du portail (GATE) reste ouvert
    def fence(u0, u1):
        n = max(1, int(round((u1 - u0) / 4.0)))
        step = (u1 - u0) / n
        for k in range(n):
            put("airport_fence", u0 + (k + 0.5) * step, FENCE_V, N)

    fence(APRON[0], GATE[0])
    fence(TERMINAL[0] + 21.0, TOWER[0] - 4.0)
    fence(TOWER[0] + 4.0, HANGARS[0][0] - 13.0)
    fence(HANGARS[0][0] + 13.0, HANGARS[1][0] - 13.0)
    fence(HANGARS[1][0] + 13.0, APRON[2])
    for u in (GATE[0], GATE[2]):                       # potelets du portail
        put("bollard", u, FENCE_V, N)

    # --- Parking : lampadaires aux bouts et au fond des rangées, projecteurs du tarmac
    ju, jv = junction(edges)
    cv, r = loop()
    for (u, v, face) in ((PARK[0] + 0.7, cv, E), (PARK[2] - 0.7, cv, W), (ju - 26.0, PARK[1] + 0.5, N),
                         (ju + 26.0, PARK[1] + 0.5, N)):
        put("lamp_road", u, v, face)
    for (u, v) in ((-436.0, -131.0), (-298.0, -131.0), (-206.0, -131.0)):
        put("lamp_road", u, v, N)
    # --- Îlot de la boucle de dépose : mâts dans l'axe de l'arrivée, avion exposé à l'ouest, bouleaux et massifs à
    # l'est, un lampadaire dans chaque bout arrondi
    for k, item in enumerate(("flagpole_blue", "flagpole_red", "flagpole_blue")):
        put(item, ju + (k - 1) * 3.2, cv, S)
    put("plane_trainer", ju - 11.5, cv + 0.5, (-0.94, -0.34))
    for (item, du, dv) in (("birch_b", 9.0, 3.5), ("birch_a", 13.5, -4.0), ("birch_a", 17.5, 2.5), ("bush_rhodo", 7.0, -4.5),
                           ("bush_rhodo", 11.0, 0.5), ("bush_rhodo", 16.0, -1.5), ("bush_rhodo", -4.5, 5.5),
                           ("bush_rhodo", -18.5, -3.5)):
        put(item, ju + du, cv + dv, S)
    for s in (-1.0, 1.0):
        put("lamp_road", ju + s * (LOOP_HALF + 6.5), cv, W if s < 0 else E)
    # --- Dépose devant l'aérogare : abribus au bord de la boucle, à l'est de l'entrée ; mobilier du parvis
    put("busstop", TERMINAL[0] + 13.5, LOOP_V[1] + LOOP_HW + 2.4, S)
    for (item, du) in FORECOURT:
        put(item, TERMINAL[0] + du, -150.5, S)
    # --- Route d'accès : totem au coin du carrefour, cédez-le-passage pour qui repart, panneau du parking, bouleaux
    # sur le talus entre la route de la vallée et le parking
    put("airport_sign", ju + 13.0, jv + 9.5, E, ground=True)
    put("sign_yield", ju - 5.0, jv + 11.0, N, ground=True)
    put("sign_parking", ju + 5.2, PARK[1] - 1.6, S, ground=True)
    for (item, du) in (("birch_a", -44.0), ("birch_b", -33.0), ("birch_a", -22.0), ("birch_b", 24.0), ("birch_a", 35.0),
                       ("birch_b", 46.0)):
        put(item, ju + du, PARK[1] - 5.0, S, ground=True)

    # --- Manches à air et PAPI (à gauche de la piste pour qui se pose)
    put("airport_windsock", -120.0, -34.0, E)
    put("airport_windsock", 320.0, -34.0, E)
    put("airport_papi", -HALF + 110.0, HW + 8.0, W)
    put("airport_papi", HALF - 110.0, -HW - 8.0, E)

    # --- Balisage lumineux : bords de piste blancs, seuils verts, bouts de piste rouges, rampes d'approche,
    # voies de circulation bleues
    for u in np.arange(-420.0, 421.0, 30.0):
        for v in (-HW - 1.6, HW + 1.6):
            put("airfield_light_white", float(u), v, N, INST)
    for s in (-1.0, 1.0):
        for v in np.arange(-13.5, 13.6, 3.0):
            put("airfield_light_green", s * (HALF + 1.5), float(v), N, INST)
            put("airfield_light_red", s * (HALF + 3.5), float(v), N, INST)
        for d in (10.0, 18.0, 26.0):
            for v in (-1.5, 0.0, 1.5):
                put("airfield_light_white", s * (HALF + d), v, N, INST)
    edge = TAXI_HW + 1.2
    for u in np.arange(-420.0, 421.0, 24.0):
        if not any(abs(u - k) < TAXI_HW + 4.0 for k in LINKS):
            put("airfield_light_blue", float(u), TAXI_V + edge, N, INST)       # côté piste
        if not APRON[0] - 4.0 < u < APRON[2] + 4.0:
            put("airfield_light_blue", float(u), TAXI_V - edge, N, INST)       # côté ville, hors tarmac
    for k in LINKS:
        for v in np.arange(TAXI_V + edge + 6.0, -HW - 5.0, 8.0):
            for s in (-1.0, 1.0):
                put("airfield_light_blue", k + s * edge, float(v), N, INST)
    if missing:
        print("  ! aeroport : objets absents du pack : %s" % ", ".join(sorted(missing)))
    print("aeroport : %d objets" % sum(1 for p in pl.places if p["item"].startswith(("air", "plane_", "helicopter"))))


def draw(dr, px, k, asphalt, casing, cas):
    """Piste et voies de circulation sur les cartes (map_overview, map_minimap) : bord puis corps, comme les routes.
    px : (x, z) -> pixel ; k : pixels par mètre ; cas : épaisseur du bord en pixels."""
    S = strips()
    for r in S:
        u0, v0, u1, v1 = r
        g = cas / max(k, 1e-6)
        dr.polygon([px(*p) for p in corners((u0 - g, v0 - g, u1 + g, v1 + g))], fill=casing)
    for r in S:
        dr.polygon([px(*p) for p in corners(r)], fill=asphalt)
