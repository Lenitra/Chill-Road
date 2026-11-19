"""Ford Focus ST (4e génération, 2019 avant restylage), voiture jouable.
Même moteur que les autres voitures (pack_lib) ; play_cars l'importe et la range dans PLAYABLE.

Repères de la vraie voiture : compacte 5 portes, 4,39 m de long, 1,85 m de large, 1,46 m de haut, 2,70 m
d'empattement. Long capot plongeant, phares effilés qui remontent sur les ailes, calandre hexagonale noire sous le
capot, grande prise d'air basse sur toute la largeur, écopes d'angle sombres aux antibrouillards et lames grises en
dessous ; ceinture qui monte vers l'arrière, petite vitre de custode dans la porte arrière ; aileron de pavillon,
feux horizontaux à cheval sur les ailes et le hayon, diffuseur noir, une sortie d'échappement de chaque côté.
Traction (2.3 EcoBoost). Teinte « Orange Fury » ; sans logo ni texte.
"""
from mathutils import Vector
import pack_lib as P
from pack_cars import reg, rect, ellipse
from play_cars import limb, band


def focusst_details(c):
    z = c.car["_z"]
    xf, xr = c.car["front"], c.car["rear"]
    # ---- avant : calandre hexagonale sous le capot, grande prise d'air basse, plaque posée sur le bas de la calandre
    c.decal([(0.0, 0.735), (0.33, 0.73), (0.455, 0.67), (0.43, 0.56), (0.33, 0.48), (0.0, 0.475)], 'front', 'dark',
            name="Grille", min_facing=0.1)
    c.decal([(0.0, 0.43), (0.47, 0.43), (0.535, 0.38), (0.50, 0.27), (0.0, 0.255)], 'front', 'dark',
            name="LowerGrille", min_facing=0.1)
    c.decal(rect(0.0, 0.465, 0.25, 0.565), 'front', 'plate', offset=0.012, name="Plate", min_facing=0.1)
    # écopes d'angle sombres (antibrouillard rond, deux lames noires), lame grise en dessous
    c.decal([(0.54, 0.505), (0.80, 0.53), (0.86, 0.47), (0.83, 0.36), (0.57, 0.345), (0.535, 0.40)], 'front', 'dark',
            name="CornerVent", min_facing=0.1)
    c.decal(ellipse(0.76, 0.43, 0.035, 0.035, n=12), 'front', 'lens', offset=0.010, name="FogLight", min_facing=0.1)
    for k in range(2):
        zk = 0.44 - 0.045 * k
        c.decal(rect(0.585, zk, 0.70, zk + 0.014), 'front', 'black', offset=0.008, name="VentSlat", min_facing=0.1)
    c.decal([(0.38, 0.30), (0.56, 0.33), (0.84, 0.345), (0.88, 0.30), (0.80, 0.255), (0.42, 0.255)], 'front', 'grey',
            offset=0.008, name="Blade", min_facing=0.1)
    # phares effilés en deux aplats (boîtier noir, optique) : pointe intérieure contre la calandre, ils montent vers
    # l'aile et tournent sur le flanc
    c.decal([(0.40, 0.73), (0.52, 0.722), (0.68, 0.735), (0.82, 0.765), (0.885, 0.805), (0.86, 0.835), (0.70, 0.82),
             (0.52, 0.782), (0.42, 0.757)], 'front', 'black', name="Headlight", min_facing=0.12)
    c.decal([(0.50, 0.742), (0.68, 0.75), (0.81, 0.776), (0.855, 0.805), (0.83, 0.818), (0.68, 0.796), (0.53, 0.765)],
            'front', 'led', offset=0.009, name="HeadlightLens", min_facing=0.12)
    c.decal([(2.16, 0.775), (2.11, 0.83), (1.98, 0.86), (1.90, 0.865), (1.96, 0.83), (2.06, 0.795)], 'side', 'black',
            name="HeadlightTail", min_facing=0.2)
    # capot : joint en travers du nez (au-dessus de la calandre) puis le long des ailes jusqu'à la baie
    c.decal(band([(0.0, 0.752), (0.30, 0.748), (0.40, 0.742)], -0.004, 0.004), 'front', 'trim', offset=0.004,
            name="HoodSeamFront", min_facing=0.1)
    seam = [(1.07, 0.75), (1.45, 0.76), (1.78, 0.735), (1.98, 0.69), (2.10, 0.64)]
    c.decal(band(seam, -0.004, 0.004), 'top', 'trim', offset=0.004, name="HoodSeam", min_facing=0.3)
    # ---- flancs : jupes avec liseré gris, poignées couleur caisse, séparation de la vitre de custode, joint arrière
    # de la porte arrière, rétroviseurs couleur caisse
    xs = [1.70, 1.0, 0.0, -1.0, -1.74]
    c.decal([(x, z["rk"](x) + 0.012) for x in xs] + [(x, z["rk"](x) + 0.028) for x in reversed(xs)], 'side', 'grey',
            offset=0.006, name="SkirtBlade", min_facing=0.15)
    for xh in (0.08, -1.12):
        zh = z["belt"](xh) - 0.085
        yh = c.side(xh, zh)
        if yh is not None:
            c.box([((xh, yh + 0.008, zh), (0.15, 0.03, 0.032), None)], 'paint', mirror=True, name="Handle")
    xq = -1.13
    c.decal(rect(xq - 0.022, z["belt"](xq) - 0.005, xq + 0.022, z["re"](xq) + 0.01), 'side', 'black', offset=0.011,
            name="QuarterBar", min_facing=0.2)
    c.decal([(-1.425, z["belt"](-1.425) + 0.004), (-1.425, z["re"](-1.425) + 0.02), (-1.29, z["re"](-1.29) + 0.02)],
            'side', 'paint', offset=0.010, name="CPillar", min_facing=0.2)
    seam = [(-1.43, z["belt"](-1.43) - 0.01), (-1.455, 0.86), (-1.465, 0.70)]
    c.decal(band(seam, -0.006, 0.006), 'side', 'trim', offset=0.004, name="RearDoorSeam", min_facing=0.15)
    xm = 0.94                                                     # rétroviseurs couleur caisse, collés à la porte
    zm = z["belt"](xm) + 0.06
    ym = c.side(xm, z["belt"](xm) - 0.02) or 0.83
    c.box([((xm, ym + 0.085, zm), (0.11, 0.15, 0.095), None)], 'paint', mirror=True, name="Mirror")
    c.box([((xm - 0.059, ym + 0.09, zm), (0.012, 0.12, 0.068), None)], 'glass', mirror=True, name="MirrorGlass")
    c.box([((xm + 0.01, ym + 0.01, zm - 0.035), (0.09, 0.05, 0.035), None)], 'paint', mirror=True, name="MirrorArm")
    # ---- toit : aileron couleur caisse (lèvre noire), troisième feu stop, antenne requin
    zt = z["top"](-1.82)
    c.prism([(-1.72, zt + 0.016), (-1.94, zt - 0.025), (-1.98, zt - 0.04), (-1.96, zt - 0.06), (-1.80, zt - 0.055),
             (-1.72, zt - 0.02)], 'y', -0.55, 0.55, 'paint', name="Spoiler")
    c.prism([(-1.94, zt - 0.025), (-1.985, zt - 0.041), (-1.965, zt - 0.063), (-1.92, zt - 0.057)], 'y', -0.555, 0.555,
            'black', name="SpoilerLip")
    c.box([((-1.982, 0.0, zt - 0.052), (0.012, 0.24, 0.014), None)], 'red', name="StopLight")
    zr = c.top(-1.30)
    c.prism([(-1.22, zr - 0.01), (-1.36, zr + 0.065), (-1.40, zr + 0.06), (-1.41, zr - 0.01)], 'y', -0.022, 0.022,
            'black', name="Antenna")
    # ---- arrière : feux horizontaux à cheval sur l'aile et le hayon, joint du hayon, plaque, diffuseur noir,
    # catadioptres verticaux aux angles, essuie-glace, deux sorties d'échappement et leurs repères
    c.decal([(0.30, 0.935), (0.34, 0.975), (0.60, 0.985), (0.80, 0.975), (0.865, 0.935), (0.83, 0.895),
             (0.56, 0.885), (0.33, 0.895)], 'rear', 'tail', name="Tail", min_facing=0.12)
    c.decal([(-2.09, 0.90), (-2.00, 0.915), (-1.90, 0.945), (-1.96, 0.97), (-2.08, 0.975)], 'side', 'tail',
            name="TailSide", min_facing=0.3)
    c.decal(band([(0.56, 1.00), (0.545, 0.88), (0.50, 0.72), (0.44, 0.64), (0.0, 0.632)], -0.004, 0.004), 'rear',
            'trim', offset=0.006, name="HatchSeam", min_facing=0.1)
    c.decal(rect(0.0, 0.715, 0.25, 0.82), 'rear', 'plate', offset=0.012, name="Plate", min_facing=0.1)
    c.decal([(0.0, 0.27), (0.70, 0.27), (0.80, 0.33), (0.76, 0.43), (0.52, 0.45), (0.0, 0.45)], 'rear', 'black',
            name="Diffuser", min_facing=0.1)
    c.decal(rect(0.0, 0.33, 0.10, 0.37), 'rear', 'red', offset=0.009, name="RearFog", min_facing=0.1)
    c.decal(rect(0.80, 0.47, 0.855, 0.60), 'rear', 'red', offset=0.008, name="Reflector", min_facing=0.05)
    c.box([((-2.08, 0.0, 1.09), (0.03, 0.30, 0.018), None)], 'black', name="RearWiper")
    for y, tag in ((0.60, "left"), (-0.60, "right")):
        limb(c, (xr + 0.14, y, 0.29), (xr - 0.035, y, 0.29), 0.05, 'chrome', n=12, name="Exhaust")
        limb(c, (xr - 0.035, y, 0.29), (xr - 0.04, y, 0.29), 0.039, 'dark', n=12, name="ExhaustBore")
        c.marker("exhaust-" + tag, (xr - 0.04, y, 0.29))


reg(dict(
    id="focusst", file="FocusST", name="Ford Focus ST", color=(0.93, 0.32, 0.011),
    front=2.22, rear=-2.17, height=1.46, axles=(1.35, -1.35),
    wheel=dict(rt=0.323, rr=0.241, w=0.235, y=0.785, style="multi", rim="grey"),
    arch_n=10, arch_gap=0.04,
    smooth=dict(dx=0.07, sub={2: 2, 3: 2, 4: 2, 5: 2, 6: 2, 7: 2, 8: 3}),
    inset=dict(sh=[(2.22, .03), (-2.17, .035)],
               rail=[(2.22, .10), (1.05, .07), (0.12, .06), (-1.82, .05), (-2.09, .06), (-2.17, .10)]),
    z=dict(
        bot=[(2.22, .24), (2.14, .19), (1.97, .15), (-1.82, .16), (-2.09, .24), (-2.17, .32)],
        lo=[(2.22, .28), (2.14, .23), (1.97, .195), (-1.82, .205), (-2.09, .28), (-2.17, .35)],
        rk=[(2.22, .38), (2.14, .32), (1.97, .29), (-1.82, .30), (-2.09, .38), (-2.17, .44)],
        mx=[(2.22, .53), (2.14, .57), (1.97, .60), (1.35, .63), (-1.35, .65), (-2.09, .66), (-2.17, .64)],
        # nez haut et trapu, capot court (la baie de pare-brise est juste derrière la roue avant), pare-brise très
        # incliné ; ceinture qui monte vers l'arrière ; pavillon qui descend vers l'aileron, lunette inclinée,
        # hayon presque vertical sous les feux
        sh=[(2.22, .69), (2.18, .76), (2.10, .80), (1.97, .83), (1.35, .87), (1.05, .895), (0.0, .925),
            (-1.35, .96), (-1.82, .975), (-2.09, .95), (-2.17, .91)],
        belt=[(2.22, .73), (2.18, .80), (2.10, .83), (1.97, .855), (1.35, .905), (1.05, .935), (0.0, .97),
              (-1.00, 1.005), (-1.42, 1.055), (-1.88, 1.045), (-2.09, 1.01), (-2.17, .95)],
        re=[(2.22, .74), (2.18, .81), (2.10, .845), (1.97, .87), (1.35, .925), (1.05, .95), (0.12, 1.36),
            (-0.30, 1.40), (-1.00, 1.385), (-1.42, 1.335), (-1.82, 1.29), (-2.09, 1.03), (-2.14, 1.005),
            (-2.17, .96)],
        top=[(2.22, .75), (2.18, .82), (2.10, .86), (1.97, .885), (1.35, .945), (1.05, .97), (0.12, 1.405),
             (-0.30, 1.46), (-1.00, 1.445), (-1.42, 1.405), (-1.82, 1.36), (-2.09, 1.045), (-2.14, 1.015),
             (-2.17, .97)],
    ),
    w=dict(
        W=[(2.22, .70), (2.18, .80), (2.10, .86), (1.97, .90), (1.35, .924), (0.0, .912), (-1.35, .924),
           (-1.88, .91), (-2.09, .87), (-2.17, .80)],
        belt=[(2.22, .58), (2.18, .70), (2.10, .77), (1.97, .82), (1.35, .85), (1.05, .84), (0.0, .82),
              (-1.35, .795), (-1.88, .775), (-2.09, .74), (-2.17, .66)],
        re=[(2.22, .50), (2.18, .62), (2.10, .70), (1.97, .75), (1.35, .78), (1.05, .765), (0.12, .67),
            (-1.30, .645), (-1.82, .59), (-2.09, .66), (-2.17, .58)],
    ),
    glass=dict(cowl=1.05, ws_top=0.12, rw_top=-1.82, rw_bot=-2.09, side=[(1.00, -0.10), (-0.18, -1.42)],
               pillars=[(-0.10, -0.18)]),
    details=focusst_details,
))
