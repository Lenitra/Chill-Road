"""Alpine A110 1600 S Groupe 4 (berlinette de rallye, début des années 1970) : proposition à valider avant d'entrer au
garage. Même moteur que les autres voitures (pack_lib) ; la fiche est `hidden` et absente de PLAYABLE tant qu'elle n'est
pas validée, pour qu'aucun export groupé ne la pose dans le jeu.

Repères de la vraie voiture : 3,85 m de long, 1,13 m de haut, 2,10 m d'empattement, nez plongeant, quatre phares ronds,
pare-chocs chromés en deux lames, deux longue-portée, grande lunette enveloppante, ouïes de custode, moteur en porte-à-
faux arrière (propulsion). Livrée bleu Alpine unie (sans ronds de course ni texte).
"""
import math
from mathutils import Vector
import pack_lib as P
from pack_cars import reg, rect, ellipse
from play_cars import limb, offset, band


def lamp(c, y, z, r, lens='lens', rim='chrome', lean=0.6, core=0.5, name="Headlight"):
    """Phare rond encastré dans le nez incliné, des deux côtés : axe entre la normale de la carrosserie et +X."""
    for sy in (1, -1):
        loc, n = c.hit((P.FAR, sy * y, z), (-1, 0, 0))
        if loc is None:
            print("phare hors carrosserie :", y, z)
            continue
        a = (n * lean + Vector((1, 0, 0)) * (1 - lean)).normalized()
        limb(c, loc - a * 0.07, loc + a * 0.008, r * 1.13, 'paint', n=18, name="LampPod")
        limb(c, loc + a * 0.008, loc + a * 0.020, r * 1.05, rim, n=18, name="LampRim")
        limb(c, loc + a * 0.020, loc + a * 0.028, r * 0.93, lens, n=18, name=name)
        if core:
            limb(c, loc + a * 0.028, loc + a * 0.032, r * core, 'led', n=12, name=name + "Core")


def blade(c, s, x_end, y_stop, z0, z1, out=0.03, depth=0.035, zs=None, name="Bumper"):
    """Lame de pare-chocs chromée : suit le contour en plan, du flanc (x_end) jusqu'à y_stop près de l'axe."""
    zs = zs if zs is not None else (z0 + z1) / 2
    tip = c.car["front"] if s > 0 else c.car["rear"]
    xc = tip - s * 0.10
    pts = [(x, c.side(x, zs)) for x in (x_end, (x_end + xc) / 2, xc)]
    yc = pts[-1][1]
    a_end = math.acos(min(1.0, y_stop / yc))
    for k in range(1, 9):
        y = yc * math.cos(a_end * k / 8)
        pts.append(((c.front(y, zs) if s > 0 else c.rear(y, zs)), y))
    poly = offset(pts, -s * out) + offset(pts, s * depth)[::-1]
    c.prism(poly, 'z', z0, z1, 'chrome', mirror=True, name=name)
    return pts[-1][0] + s * out


def a110_livery(j, xm):
    if j == 7 and -0.62 >= xm >= -1.34:
        return "glass"                                           # lunette enveloppante, jusque sur les côtés du toit
    return None


def a110_details(c):
    z = c.car["_z"]
    # ---- nez : deux grands phares dans les ailes, deux plus petits vers l'axe
    lamp(c, 0.50, 0.555, 0.096)
    lamp(c, 0.255, 0.492, 0.066, core=0.42, name="HeadlightInner")
    # bas du nez : bouche sombre, plaque, clignotants, ouïes de refroidissement
    c.decal([(0.0, 0.285), (0.20, 0.285), (0.235, 0.325), (0.20, 0.395), (0.0, 0.405)], 'front', 'dark', name="Mouth",
            min_facing=0.1)
    c.decal(rect(0.0, 0.312, 0.165, 0.372), 'front', 'plate', offset=0.012, name="Plate", min_facing=0.1)
    c.decal([(0.60, 0.398), (0.70, 0.398), (0.685, 0.438), (0.60, 0.438)], 'front', 'amber', offset=0.007,
            name="Indicator", min_facing=0.1)
    for k in range(3):
        zk = 0.452 + 0.024 * k
        c.decal(rect(0.415, zk, 0.595 - 0.012 * k, zk + 0.011), 'front', 'dark', offset=0.006, name="NoseSlit",
                min_facing=0.1)
    # pare-chocs en deux lames, longue-portée fixés dessus (cerclage chromé, verre jaune de rallye, cœur allumé)
    xb = blade(c, 1, 1.46, 0.235, 0.325, 0.372, name="FrontBumper")
    for sy in (1, -1):
        y = sy * 0.335
        xs = c.front(y, 0.348) + 0.045
        limb(c, (xs - 0.05, y, 0.348), (xs + 0.045, y, 0.348), 0.083, 'chrome', n=18, name="RallyLampPot")
        limb(c, (xs + 0.045, y, 0.348), (xs + 0.052, y, 0.348), 0.072, 'yellow', n=18, name="RallyLamp")
        limb(c, (xs + 0.052, y, 0.348), (xs + 0.056, y, 0.348), 0.036, 'led', n=12, name="RallyLampCore")
    # ---- capot : joint du capot, deux sangles de rallye
    c.decal(band([(0.55, 0.44), (1.05, 0.475), (1.40, 0.43), (1.60, 0.31), (1.70, 0.15), (1.725, 0.0)], -0.004, 0.004),
            'top', 'trim', offset=0.004, name="HoodSeam", min_facing=0.25)
    c.decal(band([(1.56, 0.20), (1.70, 0.155)], -0.013, 0.013), 'top', 'black', offset=0.008, name="HoodStrap",
            min_facing=0.2)
    # ---- flancs : bandeau jaune sur l'aile, rétroviseurs, élargisseurs d'ailes
    c.decal(rect(0.66, 0.625, 0.84, 0.66), 'side', 'yellow', offset=0.005, name="FenderStripe", min_facing=0.15)
    for sy in (1, -1):                                               # petits rétroviseurs ronds sur la porte
        xm, zm = 0.50, 0.835
        ym = sy * (c.side(xm, 0.76) + 0.07)
        limb(c, (xm + 0.035, ym, zm), (xm - 0.012, ym, zm), 0.04, 'chrome', n=14, name="Mirror")
        limb(c, (xm - 0.012, ym, zm), (xm - 0.016, ym, zm), 0.033, 'glass', n=14, name="MirrorGlass")
        limb(c, (xm + 0.01, sy * (abs(ym) - 0.075), 0.77), (xm + 0.01, ym, zm - 0.02), 0.011, 'trim', n=6, name="MirrorArm")
    c.flares(width=0.036, out=0.016, color='paint')
    # bavettes de rallye derrière chaque roue
    for xa in c.car["axles"]:
        x = xa - c.car["wheel"]["rt"] - 0.075
        c.box([((x, 0.65, 0.185), (0.012, 0.20, 0.15), None)], 'black', mirror=True, name="MudFlap")
    # ---- arrière : ouïes de custode, capot moteur, feux d'angle, lames de pare-chocs, plaque, échappement
    for k in range(4):
        y0 = 0.50 + 0.031 * k
        c.decal([(-1.30 - 0.012 * k, y0), (-1.62 - 0.012 * k, y0 + 0.018), (-1.62 - 0.012 * k, y0 + 0.033),
                 (-1.30 - 0.012 * k, y0 + 0.015)], 'top', 'dark', offset=0.006, name="QuarterLouvre", min_facing=0.2)
    c.decal(band([(-1.38, 0.42), (-1.74, 0.47), (-1.88, 0.36), (-1.94, 0.0)], -0.004, 0.004), 'top', 'trim',
            offset=0.004, name="LidSeam", min_facing=0.2)
    c.decal([(0.0, 0.31), (0.47, 0.31), (0.50, 0.37), (0.42, 0.415), (0.0, 0.415)], 'rear', 'dark', name="RearPanel",
            min_facing=0.1)
    c.decal(rect(0.0, 0.335, 0.17, 0.398), 'rear', 'plate', offset=0.012, name="Plate", min_facing=0.1)
    c.decal([(0.44, 0.505), (0.525, 0.50), (0.535, 0.572), (0.45, 0.578)], 'rear', 'tail', offset=0.007, name="Tail",
            min_facing=0.1)
    c.decal([(0.365, 0.51), (0.435, 0.505), (0.445, 0.578), (0.375, 0.582)], 'rear', 'amber', offset=0.007,
            name="TailIndicator", min_facing=0.1)
    xr = blade(c, -1, -1.60, 0.27, 0.425, 0.465, out=0.028, name="RearBumper")
    limb(c, (xr + 0.20, -0.30, 0.285), (xr - 0.05, -0.30, 0.285), 0.036, 'chrome', n=12, name="Exhaust")
    limb(c, (xr - 0.05, -0.30, 0.285), (xr - 0.054, -0.30, 0.285), 0.027, 'dark', n=12, name="ExhaustBore")
    c.marker("exhaust-right", (xr - 0.054, -0.30, 0.285))


reg(dict(
    id="a110", file="AlpineA110", name="Alpine A110 Rally", color=(0.006, 0.078, 0.50),
    hidden=True,                                                       # proposition : pas de trafic ni d'export groupé
    front=1.87, rear=-1.98, height=1.13, axles=(1.05, -1.05),
    wheel=dict(rt=0.288, rr=0.195, w=0.19, y=0.645, style="multi", rim="rim"),
    axle_wheels=[{}, {"w": 0.215, "y": 0.665}],
    arch_n=10, arch_gap=0.035,
    livery=a110_livery,
    inset=dict(sh=[(1.87, .03), (-1.98, .03)],
               rail=[(1.87, .10), (0.62, .09), (0.08, .07), (-1.34, .06), (-1.98, .10)]),
    z=dict(
        bot=[(1.87, .29), (1.80, .215), (1.65, .18), (-1.75, .185), (-1.92, .25), (-1.98, .31)],
        lo=[(1.87, .32), (1.80, .255), (1.65, .225), (-1.75, .23), (-1.92, .28), (-1.98, .34)],
        rk=[(1.87, .355), (1.80, .31), (1.65, .29), (-1.75, .295), (-1.92, .34), (-1.98, .385)],
        mx=[(1.87, .40), (1.82, .45), (1.70, .49), (1.05, .50), (0.0, .50), (-1.05, .53), (-1.80, .52),
            (-1.98, .44)],
        # nez rond et plongeant, ailes hautes ; ceinture basse (grandes vitres), hanches, poupe fuyante
        sh=[(1.87, .445), (1.84, .515), (1.78, .58), (1.68, .63), (1.50, .675), (1.25, .705), (1.05, .712), (0.62, .71),
            (0.0, .695), (-0.60, .71), (-1.05, .755), (-1.40, .745), (-1.75, .685), (-1.90, .625), (-1.98, .55)],
        belt=[(1.87, .47), (1.84, .55), (1.78, .615), (1.68, .665), (1.50, .71), (1.25, .745), (1.05, .752),
              (0.62, .752), (0.0, .735), (-0.60, .75), (-1.05, .79), (-1.34, .79), (-1.75, .715), (-1.90, .65),
              (-1.98, .575)],
        re=[(1.87, .475), (1.84, .556), (1.78, .62), (1.68, .672), (1.50, .718), (1.25, .752), (1.05, .762),
            (0.62, .765), (0.08, 1.06), (-0.16, 1.095), (-0.62, 1.06), (-1.34, .805), (-1.75, .725), (-1.90, .665),
            (-1.98, .585)],
        top=[(1.87, .48), (1.84, .565), (1.78, .635), (1.68, .69), (1.50, .74), (1.25, .775), (1.05, .785),
             (0.62, .79), (0.08, 1.095), (-0.05, 1.12), (-0.20, 1.13), (-0.62, 1.10), (-1.34, .82), (-1.75, .74),
             (-1.90, .68), (-1.98, .595)],
    ),
    w=dict(
        W=[(1.87, .47), (1.84, .56), (1.78, .645), (1.68, .70), (1.40, .75), (1.05, .76), (0.70, .735), (0.0, .72),
           (-0.60, .735), (-1.05, .785), (-1.45, .78), (-1.80, .73), (-1.93, .67), (-1.98, .59)],
        belt=[(1.87, .43), (1.84, .52), (1.78, .60), (1.68, .655), (1.40, .70), (1.05, .70), (0.62, .675), (0.0, .645),
              (-0.60, .625), (-1.05, .61), (-1.34, .60), (-1.75, .63), (-1.90, .60), (-1.98, .54)],
        re=[(1.87, .39), (1.84, .48), (1.78, .55), (1.68, .60), (1.40, .64), (1.05, .64), (0.62, .61), (0.08, .50),
            (-0.16, .475), (-0.62, .465), (-1.34, .53), (-1.75, .57), (-1.90, .55), (-1.98, .51)],
    ),
    glass=dict(cowl=0.62, ws_top=0.08, rw_top=-0.62, rw_bot=-1.34, side=[(0.56, -0.22), (-0.265, -0.76)],
               pillars=[(-0.22, -0.265)]),
    details=a110_details,
))
