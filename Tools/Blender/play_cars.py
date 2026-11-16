"""Voitures jouables construites avec le moteur du pack (pack_lib) : Toyota Supra (A80, 1993), Mazda MX-5 (NA, 1989),
Ford Mustang fastback 1969 façon John Wick (gris anthracite, bandes noires mates, écope, becquets), Peel P50
(1962-1965, tricycle : roue arrière seule dans l'axe, « wheel-back-center ») ... et Renault Twingo de première
génération (phase 1, 1993 : monocorps, phares « yeux de grenouille », ouïes de capot, boucliers gris enveloppants).

Même direction artistique que le trafic (sections à facettes, palette de dégradés, peinture sur PK_Paint), un peu
plus de détails vus de près (aileron, habitacle ouvert de la MX-5). Les phares escamotables de la MX-5 sont deux
pièces exportées à part (« popup-left », « popup-right ») dont le pivot est la charnière : le jeu les fait
basculer de POPUP_ANGLE (avant vers le haut, autour de l'axe latéral) quand les feux s'allument.
Chaque sortie d'échappement a un repère vide « exhaust-left/right » (centre de la bouche, +Z local Unity vers
l'arrière) : le jeu y place les flammes.
Axes Blender : +X avant, +Y gauche, Z haut, origine au milieu de l'empattement.
"""
import math
import bpy
import bmesh
from mathutils import Vector, Matrix
import pack_lib as P
import hc_lib as H
from pack_cars import reg, rect, ellipse, mirrors


# ----------------------------------------------------------------- outils
def limb(c, p0, p1, r, color, n=6, name="Limb"):
    """Tube de p0 à p1 (rayon r)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bm = bmesh.new()
    ring0, ring1 = [], []
    q = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    for k in range(n):
        a = 2 * math.pi * k / n
        o = q @ Vector((math.cos(a) * r, math.sin(a) * r, 0))
        ring0.append(bm.verts.new(p0 + o))
        ring1.append(bm.verts.new(p1 + o))
    for k in range(n):
        j = (k + 1) % n
        bm.faces.new((ring0[k], ring0[j], ring1[j], ring1[k]))
    bm.faces.new(list(reversed(ring0)))
    bm.faces.new(ring1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return c.obj(bm, name, color)


def torus(c, center, normal, R, r, color, n=12, m=5, name="Ring"):
    """Anneau (volant) : rayon R, section r, axe « normal »."""
    center, normal = Vector(center), Vector(normal).normalized()
    q = Vector((0, 0, 1)).rotation_difference(normal).to_matrix()
    bm = bmesh.new()
    rings = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c0 = Vector((math.cos(a), math.sin(a), 0))
        ring = []
        for j in range(m):
            b = 2 * math.pi * j / m
            p = c0 * (R + math.cos(b) * r) + Vector((0, 0, math.sin(b) * r))
            ring.append(bm.verts.new(center + q @ p))
        rings.append(ring)
    for i in range(n):
        A, B = rings[i], rings[(i + 1) % n]
        for j in range(m):
            k = (j + 1) % m
            bm.faces.new((A[j], B[j], B[k], A[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return c.obj(bm, name, color)


POPUP_ANGLE = 60.0      # ouverture des phares escamotables (degrés, avant vers le haut) ; même valeur dans le jeu


def _face(bm, verts, out, mat):
    """Face orientée vers `out`."""
    f = bm.faces.new(verts)
    f.normal_update()
    if f.normal.dot(out) < 0:
        f.normal_flip()
    f.material_index = mat
    return f


def popup(c, x0, x1, yc, half, h, export_name, lens="round"):
    """Phare escamotable en coin (MX-5 NA, AE86) : couvercle = facettes du capot découpées (affleurant), jupe noire,
    bloc dont la face avant, inclinée de POPUP_ANGLE vers le bas, porte l'optique (ronde, ou rectangulaire avec
    lens="rect"). Basculé de POPUP_ANGLE autour de la charnière (arête arrière du couvercle), l'optique regarde droit
    devant. Pivot = charnière."""
    th = math.radians(POPUP_ANGLE)
    L = x1 - x0
    hinge = Vector((x0, yc, c.top(x0, yc) + 0.008))
    zf = c.top(x1, yc) + 0.008 - hinge.z                    # le capot descend vers l'avant

    def line(x):                                            # pente moyenne du couvercle (repère charnière)
        return zf * x / L

    bm = c.cut([(x0, yc - half), (x1, yc - half), (x1, yc + half), (x0, yc + half)], 'top', offset=0.008,
               min_facing=0.3)
    for v in bm.verts:
        v.co -= hinge
    for f in bm.faces:
        f.material_index = 0                                 # couvercle couleur carrosserie
    # jupe : bords du couvercle prolongés jusque sous son point le plus bas
    k = max(line(v.co.x) - v.co.z for v in bm.verts) + 0.01
    centre = sum((v.co for v in bm.verts), Vector()) / len(bm.verts)
    ret = bmesh.ops.extrude_edge_only(bm, edges=[e for e in bm.edges if e.is_boundary])
    for g in ret["geom"]:
        if isinstance(g, bmesh.types.BMVert):
            g.co.z = line(g.co.x) - k
    bm.normal_update()
    for g in ret["geom"]:
        if isinstance(g, bmesh.types.BMFace):
            g.material_index = 1
            out = g.calc_center_median() - centre
            if g.normal.x * out.x + g.normal.y * out.y < 0:
                g.normal_flip()
    # bloc en coin sous la jupe (profil x-z : T0 T1 en haut, face avant T1-F, fond F-B, arrière B-T0)
    t0 = Vector((0.0, -k))
    t1 = Vector((L, line(L) - k))
    fb = t1 + h * Vector((-math.sin(th), -math.cos(th)))
    b0 = Vector((0.0, fb.y))
    prof = [t0, t1, fb, b0]
    vs = {s: [bm.verts.new((p.x, s * half, p.y)) for p in prof] for s in (-1, 1)}
    nf = Vector((math.cos(th), 0.0, -math.sin(th)))          # normale de la face avant (fermé)
    _face(bm, [vs[-1][1], vs[1][1], vs[1][2], vs[-1][2]], nf, 1)
    _face(bm, [vs[-1][2], vs[1][2], vs[1][3], vs[-1][3]], Vector((0, 0, -1)), 1)
    _face(bm, [vs[-1][3], vs[1][3], vs[1][0], vs[-1][0]], Vector((-1, 0, 0)), 1)
    for s in (-1, 1):
        _face(bm, vs[s], Vector((0, s, 0)), 1)
    # optique au milieu de la face avant, légèrement en saillie
    mid = (t1 + fb) / 2
    C = Vector((mid.x, 0.0, mid.y)) + nf * 0.004
    D = Vector((-math.sin(th), 0.0, -math.cos(th)))
    Y = Vector((0.0, 1.0, 0.0))
    if lens == "rect":
        hw, hh = half * 0.8, h * 0.32
        ring = [bm.verts.new(C + Y * sy * hw + D * sd * hh) for sy, sd in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
    else:
        r = min(h, 2 * half) * 0.41
        ring = [bm.verts.new(C + D * (math.sin(a) * r) + Y * (math.cos(a) * r))
                for a in [2 * math.pi * i / 12 for i in range(12)]]
    _face(bm, ring, nf, 2)
    ob = H.bm_to_obj(bm, "PK_%s_%s" % (c.car["id"], export_name), c.col, [c.M["paint"], c.M["black"], c.M["led"]])
    ob.location = hinge
    ob["export_name"] = export_name
    return ob


# ================================================================= Toyota Supra (A80, 1993)
def supra_details(c):
    # avant (photos de l'A80) : grandes optiques en amande au coin des ailes, sous le bord du capot qui plonge vers
    # le nez, trois projecteurs ronds chacune (le plus gros côté intérieur) ; clignotants d'angle en pilule sous les
    # phares ; plaque au centre devant la large prise d'air basse ; écopes de frein aux coins
    c.decal([(0.42, 0.555), (0.46, 0.615), (0.56, 0.645), (0.70, 0.66), (0.82, 0.672), (0.88, 0.655), (0.89, 0.59),
             (0.86, 0.52), (0.74, 0.49), (0.56, 0.485), (0.46, 0.50)], 'front', 'lens', name="Headlight",
            min_facing=0.3)
    for yc, zc, r in ((0.525, 0.565, 0.052), (0.645, 0.575, 0.050), (0.765, 0.585, 0.038)):
        c.decal(ellipse(yc, zc, r, r * 0.95, n=12), 'front', 'led', offset=0.009, name="Projector", min_facing=0.3)
    lamp = pill(0.66, 0.85, 0.418, 0.024)
    c.decal(lamp, 'front', 'lens', name="CornerLamp", min_facing=0.3)
    c.decal(clip_u(lamp, umax=0.72), 'front', 'amber', offset=0.009, name="Indicator", min_facing=0.3)
    c.decal([(0.0, 0.24), (0.44, 0.24), (0.53, 0.255), (0.57, 0.29), (0.55, 0.34), (0.46, 0.355), (0.0, 0.355)],
            'front', 'dark', name="Intake")
    c.decal([(0.64, 0.265), (0.84, 0.275), (0.86, 0.355), (0.67, 0.37)], 'front', 'dark', name="BrakeDuct",
            min_facing=0.3)
    c.decal(rect(0.0, 0.27, 0.21, 0.38), 'front', 'plate', offset=0.012, name="Plate")
    # arrière : quatre feux ronds dans deux panneaux fumés, plaque, diffuseur
    c.decal([(0.30, 0.70), (0.84, 0.70), (0.87, 0.74), (0.86, 0.86), (0.30, 0.865)], 'rear', 'dark', name="TailPanel")
    for yc in (0.45, 0.70):
        c.decal(ellipse(yc, 0.785, 0.075, 0.068, n=14), 'rear', 'tail', offset=0.009, name="Tail")
    c.decal(rect(0.0, 0.47, 0.23, 0.585), 'rear', 'plate', offset=0.012, name="Plate")
    c.decal([(0.0, 0.26), (0.70, 0.26), (0.66, 0.36), (0.0, 0.36)], 'rear', 'dark', name="Diffuser")
    c.decal(ellipse(0.30, 0.66, 0.05, 0.02, n=8), 'rear', 'lens', offset=0.009, name="Reverse")
    # flancs : poignées, repère latéral
    c.decal(rect(-0.12, 0.80, 0.04, 0.83), 'side', 'dark', offset=0.006, name="Handle")
    c.decal(rect(1.95, 0.50, 2.07, 0.53), 'side', 'amber', offset=0.006, name="Marker")
    mirrors(c, 0.70, 0.90, 0.935, size=(0.15, 0.14, 0.09))
    # aileron arrière : deux montants et un profil sur toute la largeur
    zd = c.top(-1.98, 0.55)
    for y in (0.56, -0.56):
        c.prism([(-1.86, zd - 0.02), (-2.02, zd - 0.02), (-2.06, zd + 0.24), (-1.98, zd + 0.24)], 'y', y - 0.025, y + 0.025,
                'paint', name="WingPost")
    wing = [(-1.86, zd + 0.265), (-1.95, zd + 0.285), (-2.12, zd + 0.27), (-2.13, zd + 0.245), (-1.95, zd + 0.235)]
    c.prism(wing, 'y', -0.74, 0.74, 'paint', name="Wing")
    # échappement (une sortie, à droite) et son repère pour les flammes du jeu
    limb(c, (-2.10, -0.42, 0.27), (-2.23, -0.42, 0.27), 0.045, 'chrome', n=8, name="Exhaust")
    c.marker("exhaust-right", (-2.23, -0.42, 0.27))


reg(dict(
    id="supra", file="Supra", name="Toyota Supra", color=(0.89, 0.17, 0.006),
    front=2.345, rear=-2.175, height=1.275, axles=(1.275, -1.275),
    wheel=dict(rt=0.322, rr=0.23, w=0.235, y=0.76, style="alloy5"),
    axle_wheels=[{}, {"w": 0.255, "rt": 0.318}],
    pillar_color="trim",
    z=dict(
        bot=[(2.345, .26), (2.28, .19), (2.10, .15), (-1.95, .15), (-2.10, .24), (-2.175, .32)],
        lo=[(2.345, .30), (2.28, .24), (2.10, .20), (-1.95, .20), (-2.10, .29), (-2.175, .36)],
        rk=[(2.345, .38), (2.28, .34), (2.10, .31), (-1.95, .31), (-2.12, .40), (-2.175, .44)],
        mx=[(2.345, .50), (2.28, .54), (2.10, .56), (1.275, .56), (-1.275, .60), (-2.05, .62), (-2.175, .60)],
        sh=[(2.345, .58), (2.28, .63), (2.10, .67), (1.60, .72), (1.20, .75), (0.50, .78), (-1.30, .84), (-2.05, .85),
            (-2.175, .80)],
        belt=[(2.345, .60), (2.28, .66), (2.10, .71), (1.60, .755), (1.20, .79), (0.92, .86), (0.00, .90), (-1.10, .95),
              (-1.80, .975), (-2.10, .965), (-2.175, .92)],
        re=[(2.345, .605), (2.28, .665), (2.10, .71), (1.60, .745), (1.20, .79), (0.92, .865), (0.12, 1.21),
            (-0.45, 1.24), (-0.85, 1.20), (-1.80, .985), (-2.10, .975), (-2.175, .925)],
        top=[(2.345, .565), (2.28, .645), (2.10, .705), (1.60, .745), (1.20, .80), (0.92, .88), (0.12, 1.245),
             (-0.45, 1.275), (-0.85, 1.235), (-1.80, 1.00), (-2.10, .985), (-2.175, .93)],
    ),
    w=dict(
        W=[(2.345, .62), (2.28, .80), (2.10, .88), (1.275, .905), (-1.275, .905), (-1.95, .895), (-2.10, .86),
           (-2.175, .78)],
        belt=[(2.345, .60), (2.25, .74), (2.05, .80), (0.92, .83), (-1.10, .80), (-2.05, .80), (-2.175, .70)],
        re=[(2.345, .50), (2.25, .64), (2.05, .70), (0.92, .74), (0.12, .63), (-0.85, .64), (-1.80, .72),
            (-2.175, .62)],
    ),
    glass=dict(cowl=0.92, ws_top=0.12, rw_top=-0.85, rw_bot=-1.80, side=[(0.86, -1.10)], pillars=[(-0.44, -0.52)]),
    details=supra_details,
))


# ================================================================= Mazda MX-5 (NA, 1989), capote repliée
COCKPIT = (0.47, -1.0)       # x avant, x arrière de l'habitacle ouvert


def miata_livery(j, xm):
    if COCKPIT[0] >= xm >= COCKPIT[1] and j in (7, 8):
        return "black"                                           # cuve de l'habitacle
    return None


def miata_details(c):
    # phares escamotables (fermés) : trappes sombres dans le capot, blocs pivotants au-dessus
    for side, name in ((1, "popup-left"), (-1, "popup-right")):
        yc = side * 0.555
        c.decal([(1.30, yc - side * 0.145), (1.62, yc - side * 0.145), (1.62, yc + side * 0.145),
                 (1.30, yc + side * 0.145)], 'top', 'dark', mirror=False, name="PopupWell", min_facing=0.3)
        popup(c, 1.315, 1.605, yc, 0.13, 0.21, name)
    # avant (photos de la NA) : le « sourire », large bouche ovale basse avec la plaque devant ; feux d'angle en
    # pilule (verre clair, clignotant orange côté intérieur) juste sous la ligne du capot
    c.decal(ellipse(0.0, 0.30, 0.38, 0.062, n=18), 'front', 'dark', mirror=False, name="Mouth")
    c.decal(rect(0.0, 0.215, 0.19, 0.325), 'front', 'plate', offset=0.012, name="Plate")
    lamp = pill(0.43, 0.735, 0.478, 0.033)
    c.decal(lamp, 'front', 'lens', name="FrontLamp")
    c.decal(clip_u(lamp, umax=0.53), 'front', 'amber', offset=0.009, name="Indicator")
    c.decal(rect(0.0, 0.524, 0.66, 0.532), 'front', 'dark', offset=0.007, name="HoodLine")
    # arrière : feux ovales enveloppants (rouge, clignotant orange, recul blanc), plaque en creux
    c.decal([(0.40, 0.62), (0.62, 0.61), (0.78, 0.625), (0.80, 0.67), (0.76, 0.715), (0.58, 0.725), (0.42, 0.715),
             (0.38, 0.67)], 'rear', 'tail', name="Tail")
    c.decal([(0.66, 0.625), (0.78, 0.63), (0.795, 0.67), (0.77, 0.705), (0.66, 0.71)], 'rear', 'amber', offset=0.009,
            name="TailIndicator")
    c.decal(ellipse(0.47, 0.668, 0.035, 0.03, n=10), 'rear', 'lens', offset=0.009, name="Reverse")
    c.decal(rect(0.0, 0.50, 0.26, 0.65), 'rear', 'dark', name="PlateRecess")
    c.decal(rect(0.0, 0.52, 0.22, 0.63), 'rear', 'plate', offset=0.012, name="Plate")
    # flancs : poignée, repères latéraux
    c.decal(rect(-0.30, 0.78, -0.18, 0.80), 'side', 'chrome', offset=0.006, name="Handle")
    c.decal(rect(1.62, 0.50, 1.74, 0.53), 'side', 'amber', offset=0.006, name="MarkerFront")
    c.decal(rect(-1.85, 0.56, -1.73, 0.59), 'side', 'red', offset=0.006, name="MarkerRear")
    mirrors(c, 0.36, 0.83, 0.88, size=(0.12, 0.13, 0.08), color='black')
    # pare-brise : vitre fine dans un cadre noir
    zb = c.top(0.53, 0.0)
    c.prism([(0.53, zb - 0.01), (0.06, 1.15), (0.04, 1.147), (0.51, zb - 0.02)], 'y', -0.64, 0.64, 'glass',
            name="Windshield")
    for y in (0.655, -0.655):
        limb(c, (0.54, y, zb - 0.01), (0.06, y * 0.95, 1.155), 0.022, 'black', n=6, name="APillar")
    c.box([((0.05, 0, 1.157), (0.05, 1.28, 0.035), None)], 'black', name="Header")
    c.box([((0.09, 0.0, 1.10), (0.03, 0.18, 0.06), None)], 'black', name="RearView")
    # habitacle : planche de bord, console, sièges baquets, volant (conduite à gauche), capote repliée
    c.box([((0.38, 0, 0.72), (0.2, 1.24, 0.2), None)], 'black', name="Dash")
    c.box([((0.30, 0.33, 0.80), (0.08, 0.30, 0.08), None)], 'trim', name="Gauges")
    c.box([((-0.10, 0, 0.55), (0.9, 0.18, 0.18), None)], 'black', name="Console")
    for y in (0.33, -0.33):
        c.box([((-0.42, y, 0.52), (0.46, 0.46, 0.12), None)], 'trim', name="SeatBase")
        c.prism([(-0.60, 0.50), (-0.70, 0.94), (-0.60, 0.96), (-0.50, 0.55)], 'y', y - 0.23, y + 0.23, 'trim',
                name="SeatBack")
        c.prism([(-0.70, 0.93), (-0.73, 1.06), (-0.63, 1.07), (-0.61, 0.95)], 'y', y - 0.13, y + 0.13, 'trim',
                name="Headrest")
    torus(c, (0.12, 0.33, 0.80), (1.0, 0, 0.55), 0.18, 0.022, 'black', name="SteeringWheel")
    limb(c, (0.12, 0.33, 0.80), (0.30, 0.33, 0.72), 0.03, 'black', name="Column")
    c.prism([(-0.93, 0.84), (-0.96, 0.95), (-1.14, 0.96), (-1.20, 0.90), (-1.18, 0.86)], 'y', -0.64, 0.64, 'black',
            name="FoldedTop")
    # échappement (à droite) et son repère pour les flammes du jeu
    limb(c, (-1.98, -0.46, 0.25), (-2.10, -0.46, 0.25), 0.035, 'chrome', n=8, name="Exhaust")
    c.marker("exhaust-right", (-2.10, -0.46, 0.25))


reg(dict(
    id="miata", file="Miata", name="Mazda MX-5", color=(0.57, 0.005, 0.006),
    front=1.93, rear=-2.04, height=1.23, axles=(1.1325, -1.1325),
    wheel=dict(rt=0.289, rr=0.19, w=0.185, y=0.70, style="alloy6"),
    livery=miata_livery,
    z=dict(
        bot=[(1.93, .22), (1.84, .16), (1.62, .14), (-1.65, .14), (-1.92, .20), (-2.04, .26)],
        lo=[(1.93, .25), (1.84, .20), (1.62, .19), (-1.65, .19), (-1.94, .25), (-2.04, .31)],
        rk=[(1.93, .32), (1.84, .30), (1.62, .30), (-1.65, .30), (-1.96, .36), (-2.04, .40)],
        mx=[(1.93, .42), (1.84, .46), (1.62, .50), (1.13, .52), (-1.13, .54), (-1.85, .56), (-2.04, .56)],
        sh=[(1.93, .50), (1.84, .56), (1.62, .61), (1.00, .68), (0.00, .72), (-1.40, .76), (-1.90, .76),
            (-2.04, .72)],
        belt=[(1.93, .54), (1.84, .60), (1.62, .655), (1.00, .73), (0.50, .79), (0.00, .82), (-1.00, .84),
              (-1.60, .855), (-1.95, .85), (-2.04, .81)],
        re=[(1.93, .545), (1.84, .605), (1.62, .66), (1.00, .745), (0.53, .805), (0.47, .83), (-1.00, .845),
            (-1.06, .86), (-1.60, .87), (-1.95, .865), (-2.04, .815)],
        top=[(1.93, .55), (1.84, .615), (1.62, .67), (1.00, .765), (0.53, .825), (0.47, .45), (-1.00, .45),
             (-1.06, .875), (-1.60, .885), (-1.95, .87), (-2.04, .82)],
    ),
    w=dict(
        W=[(1.93, .60), (1.82, .74), (1.60, .81), (1.13, .8375), (-1.13, .8375), (-1.85, .82), (-2.00, .76),
           (-2.04, .68)],
        belt=[(1.93, .50), (1.82, .64), (1.60, .72), (1.00, .77), (-1.00, .77), (-1.85, .74), (-2.04, .60)],
        re=[(1.93, .40), (1.82, .54), (1.60, .62), (1.00, .66), (0.53, .68), (0.47, .67), (-1.00, .67),
            (-1.06, .66), (-1.85, .64), (-2.04, .50)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[], pillars=[]),
    details=miata_details,
))

# ================================================================= Ford Mustang fastback 1969 (John Wick)
def mustang_details(c):
    # bandes de rallye noires mates (capot, pavillon, coffre), écart de 9 cm au centre
    for x0, x1 in ((2.35, 0.58), (-0.15, -0.79), (-1.69, -2.43)):
        c.decal(rect(x1, 0.045, x0, 0.30), 'top', 'black', offset=0.006, name="Stripe", min_facing=0.3)
    c.decal(ellipse(2.22, 0.60, 0.03, 0.03, n=8), 'top', 'chrome', offset=0.008, name="HoodPin", min_facing=0.3)
    # écope de capot (ouverture vers l'avant)
    zf, zb = c.top(1.52, 0.0) - 0.01, c.top(0.92, 0.0) - 0.01
    c.prism([(1.52, zf), (1.52, zf + 0.075), (1.05, zb + 0.04), (0.92, zb)], 'y', -0.20, 0.20, 'black',
            name="HoodScoop")
    c.box([((1.523, 0.0, zf + 0.04), (0.012, 0.36, 0.055), None)], 'dark', name="ScoopMouth")
    # avant : calandre noire, quatre phares ronds cerclés de chrome, feux de position sous le pare-chocs
    c.decal([(0.0, 0.47), (0.62, 0.47), (0.66, 0.50), (0.66, 0.73), (0.62, 0.76), (0.0, 0.76)], 'front', 'dark',
            name="Grille")
    for yc, zc, r in ((0.74, 0.64, 0.078), (0.535, 0.63, 0.072)):
        c.decal(ellipse(yc, zc, r + 0.016, r + 0.016, n=14), 'front', 'chrome', offset=0.008, name="LampRing")
        c.decal(ellipse(yc, zc, r, r, n=14), 'front', 'led', offset=0.011, name="Headlight")
    c.decal(rect(0.34, 0.285, 0.52, 0.33), 'front', 'amber', offset=0.006, name="ParkLight")
    c.box([((2.36, 0.0, 0.40), (0.07, 1.68, 0.085), None)], 'chrome', name="FrontBumper")
    c.box([((2.31, 0.0, 0.215), (0.12, 1.56, 0.05), None)], 'black', name="ChinSpoiler")
    # arrière : panneau noir concave, trois feux verticaux par côté, bouchon d'essence, plaque sous le pare-chocs
    c.decal([(0.0, 0.49), (0.80, 0.49), (0.82, 0.52), (0.82, 0.72), (0.80, 0.745), (0.0, 0.745)], 'rear', 'dark',
            name="TailPanel")
    for yc in (0.56, 0.645, 0.73):
        c.decal(rect(yc - 0.042, 0.523, yc + 0.042, 0.712), 'rear', 'chrome', offset=0.008, name="TailBezel")
        c.decal(rect(yc - 0.03, 0.535, yc + 0.03, 0.70), 'rear', 'tail', offset=0.011, name="Tail")
    c.decal(ellipse(0.0, 0.62, 0.055, 0.055, n=12), 'rear', 'chrome', mirror=False, offset=0.008, name="FuelCap")
    c.decal(rect(0.0, 0.245, 0.155, 0.385), 'rear', 'plate', offset=0.012, name="Plate")
    c.box([((-2.44, 0.0, 0.44), (0.06, 1.62, 0.07), None)], 'chrome', name="RearBumper")
    zd = c.top(-2.28, 0.4)
    for y in (0.55, -0.55):
        c.prism([(-2.17, zd - 0.02), (-2.27, zd - 0.02), (-2.30, zd + 0.06), (-2.22, zd + 0.06)], 'y', y - 0.02,
                y + 0.02, 'black', name="SpoilerPost")
    c.prism([(-2.16, zd + 0.07), (-2.22, zd + 0.085), (-2.40, zd + 0.08), (-2.41, zd + 0.06), (-2.22, zd + 0.055)],
            'y', -0.72, 0.72, 'black', name="Spoiler")
    for y, side in ((0.52, "left"), (-0.52, "right")):
        limb(c, (-2.36, y, 0.25), (-2.50, y, 0.25), 0.04, 'chrome', n=8, name="Exhaust")
        c.marker("exhaust-" + side, (-2.50, y, 0.25))
    # flancs : vitre de custode en pointe (montant large du fastback), fausse écope, poignée, feux latéraux
    c.decal([(-0.52, 1.30), (-0.52, 1.24), (-1.06, 0.895), (-1.30, 0.895), (-1.30, 1.30)], 'side', 'paint',
            offset=0.004, name="Sail", min_facing=0.6)
    c.decal([(-1.00, 0.79), (-1.24, 0.80), (-1.24, 0.86), (-1.00, 0.885)], 'side', 'dark', offset=0.006,
            name="SideScoop")
    c.decal(rect(-0.34, 0.815, -0.22, 0.83), 'side', 'chrome', offset=0.006, name="Handle")
    c.decal(rect(2.08, 0.585, 2.20, 0.615), 'side', 'amber', offset=0.006, name="MarkerFront")
    c.decal(rect(-2.30, 0.60, -2.18, 0.63), 'side', 'red', offset=0.006, name="MarkerRear")
    mirrors(c, 0.42, 0.88, 0.935, size=(0.10, 0.09, 0.07))


reg(dict(
    id="mustang", file="Mustang", name="Ford Mustang 1969", color=(0.089, 0.095, 0.10),
    front=2.34, rear=-2.42, height=1.27, axles=(1.3715, -1.3715),
    wheel=dict(rt=0.34, rr=0.19, w=0.225, y=0.745, style="alloy5"),
    pillar_color="chrome",
    z=dict(
        bot=[(2.34, .26), (2.28, .20), (2.10, .17), (-2.05, .17), (-2.25, .24), (-2.42, .30)],
        lo=[(2.34, .30), (2.28, .25), (2.10, .22), (-2.05, .22), (-2.25, .29), (-2.42, .34)],
        rk=[(2.34, .38), (2.28, .35), (2.10, .33), (-2.05, .33), (-2.25, .38), (-2.42, .42)],
        mx=[(2.34, .55), (2.28, .56), (1.37, .57), (-1.37, .59), (-2.25, .60), (-2.42, .60)],
        sh=[(2.34, .72), (2.28, .73), (1.37, .74), (0.60, .75), (-0.60, .77), (-1.30, .80), (-2.25, .80),
            (-2.42, .79)],
        belt=[(2.34, .80), (2.28, .81), (1.37, .835), (0.56, .86), (-0.50, .875), (-1.10, .895), (-1.45, .93),
              (-1.90, .925), (-2.25, .91), (-2.42, .89)],
        re=[(2.34, .805), (2.28, .815), (1.37, .845), (0.56, .875), (-0.14, 1.235), (-0.78, 1.235), (-1.12, 1.14),
            (-1.70, .975), (-2.25, .945), (-2.42, .915)],
        top=[(2.34, .815), (2.28, .83), (1.37, .865), (0.56, .895), (-0.14, 1.255), (-0.45, 1.27), (-0.78, 1.25),
             (-1.12, 1.15), (-1.70, .985), (-2.25, .955), (-2.42, .925)],
    ),
    w=dict(
        W=[(2.34, .86), (2.28, .875), (2.10, .885), (1.37, .895), (0.0, .895), (-1.37, .905), (-2.10, .90),
           (-2.30, .88), (-2.42, .85)],
        belt=[(2.34, .83), (2.28, .85), (1.37, .86), (0.56, .84), (-0.50, .82), (-1.30, .85), (-2.10, .855),
              (-2.42, .82)],
        re=[(2.34, .70), (2.28, .72), (1.37, .73), (0.56, .74), (-0.14, .63), (-0.78, .62), (-1.30, .70),
            (-1.80, .76), (-2.30, .76), (-2.42, .72)],
    ),
    glass=dict(cowl=0.56, ws_top=-0.14, rw_top=-0.80, rw_bot=-1.68, side=[(0.52, -1.08)], pillars=[(-0.45, -0.48)]),
    details=mustang_details,
))

# ================================================================= Peel P50 (1962-1965) : tricycle, 2 roues avant, 1 arrière
def p50_details(c):
    # avant : un seul phare, au centre du nez (et un seul faisceau dans le jeu : repère « headlight »), petite plaque
    c.decal(ellipse(0.0, 0.505, 0.082, 0.082, n=14), 'front', 'chrome', mirror=False, offset=0.008, name="LampRing")
    c.decal(ellipse(0.0, 0.505, 0.066, 0.066, n=14), 'front', 'led', mirror=False, offset=0.011, name="Headlight")
    c.marker("headlight", (0.685, 0.0, 0.505), flow=(1.0, 0.0, 0.0))
    c.decal(rect(0.0, 0.345, 0.14, 0.425), 'front', 'plate', offset=0.012, name="Plate")
    # bourrelet crème tout autour du bas de caisse
    c.decal(rect(-0.67, 0.300, 0.64, 0.335), 'side', 'plate', offset=0.012, name="RubStrip")
    c.decal(rect(0.0, 0.300, 0.40, 0.335), 'front', 'plate', offset=0.012, name="RubStrip")
    c.decal(rect(0.0, 0.300, 0.40, 0.335), 'rear', 'plate', offset=0.012, name="RubStrip")
    # arrière : feux ronds, plaque, poignée pour soulever la voiture (pas de marche arrière)
    c.decal(ellipse(0.30, 0.47, 0.034, 0.034, n=10), 'rear', 'tail', offset=0.009, name="Tail")
    c.decal(rect(0.0, 0.36, 0.13, 0.44), 'rear', 'plate', offset=0.012, name="Plate")
    limb(c, (-0.735, 0.14, 0.62), (-0.735, -0.14, 0.62), 0.012, 'chrome', n=6, name="Handle")
    for y in (0.14, -0.14):
        limb(c, (-0.69, y, 0.62), (-0.735, y, 0.62), 0.012, 'chrome', n=6, name="HandleArm")
    # porte unique à gauche (joints, poignée), prise d'air du moteur sous la porte
    c.decal(rect(-0.385, 0.41, -0.373, 0.715), 'side', 'dark', mirror=False, offset=0.006, name="DoorGap")
    c.decal(rect(-0.385, 0.41, 0.24, 0.422), 'side', 'dark', mirror=False, offset=0.006, name="DoorGap")
    c.decal(rect(-0.34, 0.63, -0.27, 0.65), 'side', 'chrome', mirror=False, offset=0.008, name="DoorHandle")
    for k in range(3):
        z = 0.345 + 0.02 * k
        c.decal(rect(-0.05, z, 0.20, z + 0.011), 'side', 'dark', mirror=False, offset=0.006, name="EngineVent")
    # essuie-glace unique, clignotants sur le toit, petit rétroviseur à gauche
    p0, p1 = Vector((0.265, 0.10, 0.0)), Vector((0.19, -0.16, 0.0))
    p0.z, p1.z = c.top(p0.x, p0.y) + 0.012, c.top(p1.x, p1.y) + 0.012
    limb(c, p0, p1, 0.008, 'black', n=5, name="Wiper")
    c.box([((0.075, 0.40, 1.175), (0.035, 0.035, 0.03), None)], 'amber', mirror=True, name="RoofIndicator")
    c.box([((0.29, 0.475, 0.80), (0.03, 0.02, 0.045), None)], 'chrome', name="Mirror")
    limb(c, (0.30, 0.44, 0.76), (0.29, 0.47, 0.785), 0.007, 'chrome', n=5, name="MirrorArm")
    # échappement le long du bas de caisse gauche, sortie à l'arrière
    limb(c, (0.05, 0.36, 0.175), (-0.74, 0.36, 0.175), 0.016, 'grey', n=6, name="Exhaust")
    c.marker("exhaust-left", (-0.74, 0.36, 0.175))


reg(dict(
    id="p50", file="PeelP50", name="Peel P50", color=(0.0134, 0.0593, 0.342),
    front=0.67, rear=-0.70, height=1.20, axles=(0.55, -0.55),
    wheel=dict(rt=0.16, rr=0.075, w=0.10, y=0.40, style="steel"),
    axle_wheels=[{}, {"single": True}],
    arch_axles=[0],
    inset=dict(fl=0.12, lo=0.05, rk=0.02),
    z=dict(
        bot=[(0.67, .30), (0.62, .24), (0.50, .21), (-0.58, .21), (-0.68, .25), (-0.70, .30)],
        lo=[(0.67, .34), (0.62, .28), (0.50, .25), (-0.58, .25), (-0.68, .29), (-0.70, .34)],
        rk=[(0.67, .40), (0.62, .34), (0.50, .31), (-0.58, .31), (-0.68, .35), (-0.70, .40)],
        mx=[(0.67, .46), (0.62, .44), (0.50, .43), (-0.58, .43), (-0.68, .45), (-0.70, .48)],
        sh=[(0.67, .54), (0.62, .58), (0.50, .61), (0.28, .64), (-0.58, .66), (-0.68, .66), (-0.70, .62)],
        belt=[(0.67, .57), (0.62, .62), (0.50, .66), (0.28, .70), (-0.58, .72), (-0.68, .72), (-0.70, .68)],
        re=[(0.67, .575), (0.62, .63), (0.50, .675), (0.28, .72), (0.08, 1.15), (-0.62, 1.17), (-0.67, 1.10),
            (-0.70, .70)],
        top=[(0.67, .58), (0.62, .64), (0.50, .69), (0.28, .74), (0.08, 1.18), (-0.30, 1.20), (-0.62, 1.19),
             (-0.67, 1.12), (-0.70, .72)],
    ),
    w=dict(
        W=[(0.67, .36), (0.62, .44), (0.50, .48), (0.0, .495), (-0.58, .49), (-0.68, .45), (-0.70, .38)],
        belt=[(0.67, .33), (0.62, .41), (0.50, .45), (0.28, .47), (-0.58, .47), (-0.68, .44), (-0.70, .36)],
        re=[(0.67, .26), (0.62, .34), (0.50, .39), (0.28, .44), (0.08, .43), (-0.62, .43), (-0.67, .41),
            (-0.70, .34)],
    ),
    glass=dict(cowl=0.28, ws_top=0.08, rw_top=-0.64, rw_bot=-0.69, side=[(0.26, -0.38)], pillars=[]),
    details=p50_details,
))

# ================================================================= Toyota AE86 Sprinter Trueno (1983-1987), façon Initial D
def ae86_details(c):
    # phares escamotables aux coins avant du capot, optiques rectangulaires
    for side, name in ((1, "popup-left"), (-1, "popup-right")):
        yc = side * 0.54
        c.decal([(1.70, yc - side * 0.19), (1.99, yc - side * 0.19), (1.99, yc + side * 0.19),
                 (1.70, yc + side * 0.19)], 'top', 'dark', mirror=False, name="PopupWell", min_facing=0.3)
        popup(c, 1.715, 1.975, yc, 0.175, 0.19, name, lens="rect")
    # avant : bandeau noir sous le capot, pare-chocs blanc en haut et noir en bas, feux dans le noir, plaque
    c.decal(rect(0.0, 0.555, 0.74, 0.628), 'front', 'black', name="NoseBand")
    c.decal(rect(0.0, 0.30, 0.78, 0.46), 'front', 'black', name="Bumper")
    c.decal(rect(0.30, 0.405, 0.45, 0.44), 'front', 'lens', offset=0.009, name="ParkLight")
    c.decal(rect(0.48, 0.405, 0.64, 0.44), 'front', 'amber', offset=0.009, name="Indicator")
    c.decal(rect(0.0, 0.33, 0.16, 0.40), 'front', 'plate', offset=0.012, name="Plate")
    c.decal(rect(1.82, 0.50, 1.92, 0.53), 'side', 'amber', offset=0.006, name="Marker")
    # arrière : feux rouges et orange sur bandeau noir, plaque, pare-chocs noir, becquet de hayon
    c.decal(rect(0.0, 0.62, 0.40, 0.745), 'rear', 'black', name="TailGarnish")
    c.decal(rect(0.40, 0.62, 0.74, 0.745), 'rear', 'tail', name="Tail")
    c.decal(rect(0.40, 0.62, 0.49, 0.745), 'rear', 'amber', offset=0.009, name="TailIndicator")
    c.decal(rect(0.0, 0.525, 0.16, 0.605), 'rear', 'plate', offset=0.012, name="Plate")
    c.decal(rect(0.0, 0.33, 0.78, 0.50), 'rear', 'black', name="Bumper")
    zt = c.top(-1.96, 0.3)
    c.prism([(-1.90, zt - 0.01), (-2.01, zt + 0.035), (-2.07, zt + 0.03), (-2.03, zt - 0.02)], 'y', -0.64, 0.64, 'paint',
            name="HatchLip")
    # poignée, rétroviseurs noirs, échappement unique à droite
    c.decal(rect(-0.55, 0.77, -0.45, 0.79), 'side', 'black', offset=0.006, name="Handle")
    mirrors(c, 0.50, 0.84, 0.90, size=(0.10, 0.12, 0.08), color='black')
    limb(c, (-2.05, -0.45, 0.26), (-2.22, -0.45, 0.26), 0.035, 'chrome', n=8, name="Exhaust")
    c.marker("exhaust-right", (-2.22, -0.45, 0.26))


reg(dict(
    id="ae86", file="AE86", name="Toyota AE86 Trueno", color=(0.848, 0.848, 0.807),
    front=2.04, rear=-2.175, height=1.335, axles=(1.20, -1.20),
    wheel=dict(rt=0.289, rr=0.178, w=0.185, y=0.68, style="multi", rim="grey"),
    pillar_color="black",
    bands=[(2, 99.0, -99.0, 'black'), (3, 99.0, -99.0, 'black')],     # bas de caisse noir (« panda »)
    z=dict(
        bot=[(2.04, .30), (1.98, .24), (1.80, .20), (-1.95, .20), (-2.10, .27), (-2.175, .33)],
        lo=[(2.04, .33), (1.98, .28), (1.80, .25), (-1.95, .25), (-2.10, .31), (-2.175, .37)],
        rk=[(2.04, .40), (1.98, .36), (1.80, .33), (-1.95, .33), (-2.10, .38), (-2.175, .42)],
        mx=[(2.04, .46), (1.98, .465), (1.20, .47), (-1.20, .48), (-2.10, .49), (-2.175, .48)],
        sh=[(2.04, .60), (1.98, .63), (1.60, .66), (0.60, .70), (-1.00, .73), (-2.10, .74), (-2.175, .72)],
        belt=[(2.04, .64), (1.98, .67), (1.60, .72), (0.60, .82), (-0.60, .85), (-1.40, .88), (-2.00, .90),
              (-2.175, .88)],
        re=[(2.04, .645), (1.98, .675), (1.60, .725), (0.60, .84), (-0.25, 1.29), (-1.00, 1.30), (-1.95, .95),
            (-2.10, .93), (-2.175, .90)],
        top=[(2.04, .65), (1.98, .68), (1.60, .735), (0.60, .855), (-0.25, 1.32), (-0.60, 1.335), (-1.00, 1.31),
             (-1.95, .96), (-2.10, .945), (-2.175, .91)],
    ),
    w=dict(
        W=[(2.04, .74), (1.98, .78), (1.80, .80), (1.20, .8125), (-1.20, .8125), (-1.95, .80), (-2.10, .78),
           (-2.175, .74)],
        belt=[(2.04, .72), (1.98, .76), (1.60, .78), (0.60, .77), (-0.60, .76), (-1.95, .76), (-2.175, .70)],
        re=[(2.04, .66), (1.98, .70), (1.60, .72), (0.60, .70), (-0.25, .60), (-1.00, .60), (-1.95, .66),
            (-2.175, .62)],
    ),
    glass=dict(cowl=0.60, ws_top=-0.25, rw_top=-1.02, rw_bot=-1.88, side=[(0.55, -0.62), (-0.66, -1.30)],
               pillars=[(-0.62, -0.66)]),
    details=ae86_details,
))


# ================================================================= DeLorean DMC-12 (1981-1983)
def dmc12_details(c):
    # avant : bandeau noir et quatre phares rectangulaires, pare-chocs noir, clignotants
    c.decal(rect(0.0, 0.405, 0.76, 0.545), 'front', 'black', name="FrontPanel")
    for y0, y1 in ((0.42, 0.57), (0.59, 0.74)):
        c.decal(rect(y0, 0.43, y1, 0.52), 'front', 'led', offset=0.009, name="Headlight")
    c.decal(rect(0.0, 0.25, 0.80, 0.395), 'front', 'black', name="Bumper")
    c.decal(rect(0.52, 0.335, 0.72, 0.37), 'front', 'amber', offset=0.009, name="Indicator")
    c.decal(rect(1.84, 0.42, 1.95, 0.46), 'side', 'amber', offset=0.009, name="Marker")
    # flancs : bandeau noir, portes papillon (joints), prise d'air noire derrière la vitre
    c.decal(rect(-2.18, 0.40, 1.98, 0.49), 'side', 'black', offset=0.008, name="RubStrip")
    c.decal(rect(-0.755, 0.49, -0.74, 0.80), 'side', 'dark', offset=0.006, name="DoorGap")
    c.decal(rect(-0.74, 0.28, -0.20, 0.295), 'top', 'dark', offset=0.006, name="GullwingHinge", min_facing=0.3)
    c.decal([(-0.80, 0.82), (-1.30, 0.86), (-1.30, 0.90), (-0.80, 1.00)], 'side', 'dark', offset=0.006,
            name="QuarterVent")
    mirrors(c, 0.30, 0.90, 0.84, size=(0.12, 0.12, 0.08), color='black')
    # arrière : lamelles sur la lunette et le capot moteur, feux en damier, plaque, pare-chocs noir
    c.decal(rect(-1.96, -0.62, -0.80, 0.62), 'top', 'dark', mirror=False, name="LouverBed", min_facing=0.3)
    for i in range(5):
        x = -0.90 - 0.245 * i
        z0, z1 = c.top(x + 0.06, 0.3), c.top(x - 0.06, 0.3)
        slope = math.atan2(z0 - z1, 0.12)
        c.box([((x, 0.0, (z0 + z1) / 2 + 0.02), (0.13, 1.26, 0.018), Matrix.Rotation(slope, 3, 'Y'))], 'paint',
              name="Louver")
    c.decal(rect(0.0, 0.555, 0.34, 0.72), 'rear', 'black', name="PlatePanel")
    c.decal(rect(0.0, 0.585, 0.15, 0.67), 'rear', 'plate', offset=0.012, name="Plate")
    c.decal(rect(0.36, 0.555, 0.80, 0.72), 'rear', 'tail', name="Tail")
    c.decal(rect(0.36, 0.555, 0.44, 0.72), 'rear', 'lens', offset=0.009, name="Reverse")
    c.decal(rect(0.72, 0.555, 0.80, 0.72), 'rear', 'amber', offset=0.009, name="TailIndicator")
    c.decal(rect(0.0, 0.27, 0.86, 0.53), 'rear', 'black', name="Bumper")
    for y in (0.35, -0.35):
        limb(c, (-2.05, y, 0.22), (-2.26, y, 0.22), 0.035, 'chrome', n=8, name="Exhaust")
    bttf_kit(c)


def bttf_kit(c):
    """Kit « Retour vers le futur » (machine à voyager dans le temps) : tours d'évents à lamelles aux coins arrière
    (les flammes du jeu en sortent : repères exhaust-vent-*), Mr. Fusion sur le capot moteur, conduits de flux gris
    le long des bas de caisse et au-dessus des roues arrière, bobines et tuyaux, boîtiers sur le pare-chocs avant."""
    zd = c.top(-2.10, 0.6)
    for s, side in ((1, "left"), (-1, "right")):
        # tour d'évents : bloc noir incliné, lamelles grises sur la face arrière
        c.prism([(-1.96, zd - 0.02), (-2.25, zd - 0.02), (-2.28, 1.18), (-2.05, 1.24)], 'y', s * 0.42, s * 0.80,
                'black', name="TimeVent")
        for k in range(5):
            z = zd + 0.07 + 0.055 * k
            c.box([((-2.275 + 0.004 * k, s * 0.61, z), (0.03, 0.34, 0.018), None)], 'grey', name="VentSlat")
        c.marker("exhaust-vent-" + side, (-2.30, s * 0.61, zd + 0.17))
        # conduit de flux : bas de caisse, puis au-dessus de la roue arrière jusqu'à la tour
        pts = [(0.80, 0.93, 0.40), (-0.80, 0.93, 0.40), (-0.95, 0.93, 0.62), (-1.20, 0.92, 0.77), (-1.62, 0.91, 0.78),
               (-2.00, 0.84, zd + 0.02)]
        for (x0, y0, z0), (x1, y1, z1) in zip(pts, pts[1:]):
            limb(c, (x0, s * y0, z0), (x1, s * y1, z1), 0.032, 'grey', n=6, name="FluxBand")
        for x in (0.55, 0.25, -0.05, -0.35, -0.65):
            limb(c, (x + 0.02, s * 0.93, 0.40), (x - 0.02, s * 0.93, 0.40), 0.042, 'dark', n=6, name="FluxRib")
        # bobine et tuyau vers Mr. Fusion, boîtier sur le coin du pare-chocs avant
        torus(c, (-1.93, s * 0.30, zd + 0.03), (0, 0, 1), 0.07, 0.015, 'chrome', name="Coil")
        limb(c, (-2.06, s * 0.09, zd + 0.06), (-2.00, s * 0.44, zd + 0.10), 0.02, 'grey', n=6, name="Pipe")
        c.box([((1.99, s * 0.80, 0.33), (0.10, 0.12, 0.08), None)], 'grey', name="FrontBox")
    # Mr. Fusion : socle noir, cuve blanche, couvercle
    limb(c, (-2.06, 0.0, zd - 0.02), (-2.06, 0.0, zd + 0.10), 0.10, 'black', n=10, name="FusionBase")
    limb(c, (-2.06, 0.0, zd + 0.10), (-2.06, 0.0, zd + 0.30), 0.07, 'white', n=10, name="MrFusion")
    limb(c, (-2.06, 0.0, zd + 0.30), (-2.06, 0.0, zd + 0.335), 0.076, 'grey', n=10, name="FusionCap")


reg(dict(
    id="dmc12", file="DeLorean", name="DeLorean DMC-12", color=(0.448, 0.462, 0.477),
    front=2.037, rear=-2.23, height=1.14, axles=(1.2065, -1.2065),
    wheel=dict(rt=0.295, rr=0.178, w=0.195, y=0.795, style="multi"),
    axle_wheels=[{}, {"rt": 0.332, "rr": 0.19, "w": 0.235, "y": 0.79}],
    pillar_color="black",
    z=dict(
        bot=[(2.037, .26), (1.98, .20), (1.80, .17), (-1.95, .17), (-2.15, .22), (-2.23, .27)],
        lo=[(2.037, .30), (1.98, .25), (1.80, .22), (-1.95, .22), (-2.15, .27), (-2.23, .32)],
        rk=[(2.037, .36), (1.98, .33), (1.80, .31), (-1.95, .31), (-2.15, .35), (-2.23, .40)],
        mx=[(2.037, .46), (1.98, .48), (1.20, .52), (-1.20, .54), (-2.15, .56), (-2.23, .55)],
        sh=[(2.037, .52), (1.98, .55), (1.50, .62), (0.45, .70), (-1.00, .72), (-2.15, .74), (-2.23, .72)],
        belt=[(2.037, .55), (1.98, .585), (1.50, .66), (0.45, .78), (-0.50, .80), (-1.40, .83), (-2.15, .86),
              (-2.23, .85)],
        re=[(2.037, .555), (1.98, .59), (1.50, .67), (0.45, .80), (-0.25, 1.11), (-0.72, 1.12), (-1.20, 1.00),
            (-2.00, .89), (-2.15, .88), (-2.23, .87)],
        top=[(2.037, .56), (1.98, .60), (1.50, .68), (0.45, .82), (-0.25, 1.13), (-0.50, 1.14), (-0.75, 1.13),
             (-1.20, 1.02), (-2.00, .905), (-2.15, .895), (-2.23, .88)],
    ),
    w=dict(
        W=[(2.037, .78), (1.98, .84), (1.80, .88), (1.20, .915), (-1.20, .925), (-2.00, .92), (-2.15, .90),
           (-2.23, .86)],
        belt=[(2.037, .76), (1.98, .82), (1.80, .86), (0.45, .87), (-0.50, .84), (-1.40, .86), (-2.15, .86),
              (-2.23, .82)],
        re=[(2.037, .70), (1.98, .74), (1.80, .76), (0.45, .76), (-0.25, .62), (-0.72, .62), (-1.20, .70),
            (-2.00, .80), (-2.23, .76)],
    ),
    glass=dict(cowl=0.45, ws_top=-0.25, rw_top=-0.76, rw_bot=-1.25, side=[(0.40, -0.70)], pillars=[]),
    details=dmc12_details,
))

# ================================================================= Koenigsegg Jesko Attack (2021-)
def accent_material(c, color=(0.40, 0.78, 0.03)):
    """Couleur d'accent hors palette (vert citron de la Jesko) : matériau à part « PK_Accent », teinté dans Unity."""
    m = bpy.data.materials.get("PK_Accent")
    if not m:
        m, _ = H.principled("PK_Accent", color, rough=0.45)
    c.M["accent"] = m


def _lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def offset(line, d):
    """Polyligne 2D décalée de d (nombre ou liste par point) sur la normale à droite du sens de parcours
    (sous la ligne si elle va vers +u, vers l'intérieur d'un contour de section parcouru vers l'extérieur)."""
    n = len(line)
    ds = d if isinstance(d, (list, tuple)) else [d] * n
    out = []
    for k in range(n):
        p, q = line[max(k - 1, 0)], line[min(k + 1, n - 1)]
        du, dv = q[0] - p[0], q[1] - p[1]
        L = math.hypot(du, dv) or 1.0
        out.append((line[k][0] + dv / L * ds[k], line[k][1] - du / L * ds[k]))
    return out


def band(line, d0, d1):
    """Bande entre deux décalages d'une polyligne : feux en lame le long d'une arête, liserés, cols de cygne."""
    return offset(line, d0) + offset(line, d1)[::-1]


def smooth(line, n=2):
    """Arrondit une polyligne (coupe des coins de Chaikin), extrémités conservées."""
    for _ in range(n):
        out = [line[0]]
        for a, b in zip(line, line[1:]):
            out += [_lerp(a, b, 0.25), _lerp(a, b, 0.75)]
        line = out + [line[-1]]
    return line


def pill(u0, u1, vc, r, n=6):
    """Pilule horizontale (bouts arrondis de rayon r) de u0 à u1, centrée en vc."""
    pts = []
    for k in range(n + 1):
        a = -math.pi / 2 + math.pi * k / n
        pts.append((u1 - r + r * math.cos(a), vc + r * math.sin(a)))
    for k in range(n + 1):
        a = math.pi / 2 + math.pi * k / n
        pts.append((u0 + r + r * math.cos(a), vc + r * math.sin(a)))
    return pts


def clip_u(poly, umin=-1e9, umax=1e9):
    """Partie d'un polygone convexe comprise entre les verticales u = umin et u = umax."""
    def cut(pts, keep, u):
        out = []
        for i in range(len(pts)):
            p, q = pts[i], pts[(i + 1) % len(pts)]
            if keep(p):
                out.append(p)
            if keep(p) != keep(q):
                out.append((u, p[1] + (q[1] - p[1]) * (u - p[0]) / (q[0] - p[0])))
        return out
    return cut(cut(poly, lambda p: p[0] <= umax, umax), lambda p: p[0] >= umin, umin)


def octagon(uc, vc, hw, hh, ch):
    """Rectangle à coins coupés (demi-largeur hw, demi-hauteur hh, pan ch) centré en (uc, vc)."""
    return [(uc - hw, vc - hh + ch), (uc - hw, vc + hh - ch), (uc - hw + ch, vc + hh), (uc + hw - ch, vc + hh),
            (uc + hw, vc + hh - ch), (uc + hw, vc - hh + ch), (uc + hw - ch, vc - hh), (uc - hw + ch, vc - hh)]


def jesko_details(c):
    accent_material(c)
    # capot : grande sortie d'air gris foncé jusqu'au pare-brise, bordée à l'avant d'un arc vert citron
    c.decal([(1.93, 0.0), (1.91, 0.20), (1.86, 0.33), (1.20, 0.28), (1.02, 0.17), (1.00, 0.0)], 'top', 'grey',
            offset=0.006, name="HoodOutlet", min_facing=0.3)
    c.decal([(2.00, 0.0), (1.98, 0.19), (1.91, 0.37), (1.86, 0.37), (1.92, 0.19), (1.94, 0.0)], 'top', 'accent',
            offset=0.008, name="HoodArc", min_facing=0.3)
    # phares en goutte sur l'avant des ailes : boîtier sombre, fils de LED
    c.decal([(0.46, 0.40), (0.58, 0.39), (0.94, 0.56), (0.93, 0.63), (0.80, 0.61), (0.54, 0.46)], 'front', 'dark',
            name="Headlight")
    c.decal([(0.56, 0.418), (0.91, 0.575), (0.90, 0.592), (0.55, 0.434)], 'front', 'led', offset=0.009,
            name="LedStrip")
    c.decal([(0.64, 0.47), (0.86, 0.565), (0.85, 0.578), (0.63, 0.483)], 'front', 'led', offset=0.009,
            name="LedStrip")
    # bouclier : bandeau noir, grandes prises d'air d'angle, bouche centrale, lame et ailettes vert citron
    c.decal(rect(0.0, 0.12, 0.93, 0.34), 'front', 'black', name="LowerFascia")
    c.decal([(0.50, 0.15), (0.90, 0.15), (0.92, 0.32), (0.53, 0.35)], 'front', 'dark', offset=0.009, name="CornerIntake")
    c.decal(rect(0.0, 0.16, 0.30, 0.27), 'front', 'dark', offset=0.009, name="CenterIntake")
    c.box([((2.30, 0.0, 0.09), (0.16, 1.78, 0.03), None)], 'black', name="Splitter")
    for y in (0.46, 0.80):
        c.box([((2.29, y, 0.135), (0.15, 0.014, 0.085), None)], 'accent', mirror=True, name="SplitterFin")
    for x, z in ((2.18, 0.30), (2.14, 0.38)):
        c.box([((x, 0.95, z), (0.20, 0.10, 0.012), Matrix.Rotation(math.radians(-12), 3, 'Y'))], 'black', mirror=True,
              name="Canard")
    # flancs : ouïe derrière la roue avant, grande prise d'air à lamelles dans la porte, jupe noire liserée
    c.decal([(0.96, 0.40), (0.80, 0.43), (0.78, 0.70), (0.92, 0.73)], 'side', 'black', offset=0.006, name="FenderVent")
    c.decal([(-0.34, 0.46), (-1.12, 0.50), (-1.18, 0.88), (-0.44, 0.85)], 'side', 'black', offset=0.006,
            name="DoorIntake")
    for k in range(3):
        x = -0.58 - 0.17 * k
        c.decal(rect(x - 0.012, 0.52, x + 0.012, 0.82), 'side', 'grey', offset=0.009, name="IntakeSlat")
    c.decal(rect(-2.27, 0.27, 1.92, 0.29), 'side', 'accent', offset=0.009, name="SkirtAccent")
    c.decal(rect(-0.30, 0.46, -0.285, 0.80), 'side', 'dark', offset=0.006, name="DoorGap")
    mirrors(c, 0.60, 0.97, 0.90, size=(0.15, 0.10, 0.08))
    # arrière (photos de la Jesko Attack) : capot moteur vitré cerclé de carbone entre les hanches ; feux en fines
    # lames effilées qui suivent l'arête des hanches ; grande bouche carbone encadrée par les hanches, lèvre de capot
    # au-dessus ; échappement central octogonal chromé à deux sorties ; petits feux bas ; diffuseur à 5 ailettes ;
    # coins bas liserés de vert citron comme les bas de caisse
    c.decal([(-1.36, 0.30), (-2.00, 0.20), (-2.00, -0.20), (-1.36, -0.30)], 'top', 'black', mirror=False,
            offset=0.006, name="EngineFrame", min_facing=0.3)
    c.decal([(-1.40, 0.26), (-1.96, 0.165), (-1.96, -0.165), (-1.40, -0.26)], 'top', 'glass', mirror=False,
            offset=0.009, name="EngineGlass", min_facing=0.3)
    bar = [(0.46, 0.728), (0.60, 0.742), (0.72, 0.760), (0.82, 0.782), (0.875, 0.800)]
    lt = [0.006, 0.018, 0.028, 0.034, 0.034]
    slot = [_lerp(bar[0], bar[1], -0.12)] + bar[1:-1] + [_lerp(bar[-2], bar[-1], 1.25)]
    c.decal(band(slot, [-t / 2 - 0.012 for t in lt], [t / 2 + 0.012 for t in lt]), 'rear', 'dark', offset=0.006,
            name="TailSlot")
    c.decal(band(bar, [-t / 2 for t in lt], [t / 2 for t in lt]), 'rear', 'tail', offset=0.010, name="Tail")
    c.decal([(0.0, 0.25), (0.52, 0.25), (0.60, 0.30), (0.66, 0.40), (0.70, 0.52), (0.73, 0.62), (0.725, 0.675),
             (0.68, 0.70), (0.0, 0.70)], 'rear', 'black', name="RearMouth")
    for z in (0.345, 0.505):
        c.decal(rect(0.0, z - 0.006, 0.66, z + 0.006), 'rear', 'dark', offset=0.008, name="MouthSlat")
    c.decal(octagon(0.0, 0.615, 0.19, 0.055, 0.035), 'rear', 'chrome', mirror=False, offset=0.009, name="ExhaustRing")
    c.decal(octagon(0.088, 0.615, 0.07, 0.034, 0.02), 'rear', 'dark', offset=0.012, name="Exhaust")
    c.marker("exhaust-center", (-2.27, 0.0, 0.615))
    c.decal(rect(0.44, 0.405, 0.58, 0.432), 'rear', 'tail', offset=0.009, name="FogLight")
    c.box([((-2.295, 0.0, c.top(-2.25, 0.0) - 0.016), (0.06, 1.0, 0.03), None)], 'paint', name="DeckLip")
    for y in (0.0, 0.24, 0.46):
        c.prism([(-1.96, 0.105), (-2.27, 0.255), (-2.33, 0.255), (-2.33, 0.10), (-2.10, 0.085)], 'y', y - 0.011,
                y + 0.011, 'black', mirror=y > 0, name="DiffuserFin")
    c.decal(rect(0.58, 0.255, 0.90, 0.285), 'rear', 'accent', offset=0.009, name="CornerAccent")
    # grand aileron sur cols de cygne en boomerang liserés de vert citron (Attack), dérives argent
    zd = c.top(-1.56, 0.36)
    neck = [(-1.56, zd - 0.03), (-1.63, 1.04), (-1.71, 1.17), (-1.79, 1.29), (-1.87, 1.38), (-1.95, 1.43),
            (-2.03, 1.445), (-2.10, 1.405)]
    hw = [0.10, 0.06, 0.045, 0.04, 0.035, 0.034, 0.033, 0.03]
    for y in (0.36, -0.36):
        c.prism(band(neck, [-h for h in hw], hw), 'y', y - 0.025, y + 0.025, 'black', name="SwanNeck")
        c.prism(band(neck, [-h + 0.004 for h in hw], [-h + 0.020 for h in hw]), 'y', y - 0.028, y + 0.028, 'accent',
                name="SwanNeckStripe")
    c.prism([(-1.86, 1.34), (-1.97, 1.375), (-2.30, 1.365), (-2.32, 1.34), (-1.98, 1.325)], 'y', -0.98, 0.98, 'black',
            name="Wing")
    for s in (1, -1):
        c.prism([(-1.80, 1.40), (-1.90, 1.21), (-2.28, 1.19), (-2.36, 1.25), (-2.37, 1.41), (-2.30, 1.44),
                 (-1.86, 1.44)], 'y', s * 0.975, s * 0.99, 'paint', name="WingEndplate")


reg(dict(
    id="jesko", file="Jesko", name="Koenigsegg Jesko Attack", color=(0.869, 0.869, 0.890),
    front=2.35, rear=-2.27, height=1.21, axles=(1.35, -1.35),
    wheel=dict(rt=0.347, rr=0.254, w=0.265, y=0.85, style="alloy5", rim="grey"),
    axle_wheels=[{}, {"rt": 0.364, "rr": 0.267, "w": 0.325, "y": 0.82}],
    arch_gap=0.035,
    pillar_color="black",
    inset=dict(fl=0.08, rail=0.16),                                   # flanc intérieur des ailes plus large
    crown=[(2.35, 0.6), (-1.40, 0.6), (-2.27, 0.85)],                 # capot moteur creusé entre les hanches
    bands=[(2, 99.0, -99.0, 'black')],                                # bas de caisse carbone
    z=dict(
        bot=[(2.35, .20), (2.30, .14), (2.05, .11), (-1.95, .11), (-2.15, .18), (-2.27, .26)],
        lo=[(2.35, .22), (2.30, .17), (2.05, .15), (-1.95, .15), (-2.15, .21), (-2.27, .30)],
        rk=[(2.35, .27), (2.30, .26), (2.05, .27), (-1.95, .27), (-2.15, .30), (-2.27, .36)],
        mx=[(2.35, .32), (2.30, .36), (1.35, .48), (-1.35, .52), (-2.15, .50), (-2.27, .48)],
        sh=[(2.35, .36), (2.30, .42), (2.10, .52), (1.80, .60), (1.35, .70), (0.90, .66), (0.20, .70), (-0.80, .76),
            (-1.35, .86), (-1.80, .86), (-2.10, .83), (-2.27, .72)],
        belt=[(2.35, .38), (2.30, .45), (2.10, .56), (1.80, .65), (1.35, .77), (0.90, .74), (0.20, .76), (-0.80, .86),
              (-1.35, .96), (-1.80, .96), (-2.10, .93), (-2.27, .83)],
        re=[(2.35, .39), (2.30, .49), (2.10, .64), (1.80, .77), (1.35, .88), (0.95, .85), (0.75, .85), (-0.10, 1.17),
            (-0.55, 1.18), (-0.95, 1.08), (-1.35, 1.03), (-1.80, 1.01), (-2.10, .97), (-2.27, .87)],
        top=[(2.35, .39), (2.30, .44), (2.10, .50), (1.80, .57), (1.35, .64), (0.95, .74), (0.75, .82), (-0.10, 1.20),
             (-0.30, 1.21), (-0.55, 1.20), (-0.95, 1.06), (-1.35, .93), (-1.80, .90), (-2.10, .84), (-2.27, .745)],
    ),
    w=dict(
        W=[(2.35, .50), (2.30, .74), (2.20, .88), (2.05, .96), (1.35, 1.01), (0.60, .96), (-0.40, .95), (-1.35, 1.015),
           (-2.00, .99), (-2.15, .95), (-2.27, .88)],
        belt=[(2.35, .48), (2.30, .71), (2.20, .85), (2.05, .92), (1.35, .97), (0.60, .88), (-0.40, .84), (-1.35, .96),
              (-2.00, .94), (-2.27, .80)],
        re=[(2.35, .40), (2.30, .56), (2.20, .64), (2.05, .70), (1.80, .74), (1.35, .78), (0.95, .76), (0.75, .72),
            (-0.10, .56), (-0.55, .56), (-0.95, .66), (-1.35, .76), (-1.80, .78), (-2.10, .76), (-2.27, .70)],
    ),
    glass=dict(cowl=0.75, ws_top=-0.10, rw_top=-0.56, rw_bot=-0.80, side=[(0.70, -0.28)], pillars=[]),
    details=jesko_details,
))

# ================================================================= Aston Martin Valkyrie AMR-LMH (hypercar d'endurance)
def valkyrie_details(c):
    accent_material(c)
    sh = c.car["_z"]["sh"]
    # coques d'ailes avant : grande ouïe sombre en goutte sur le dessus, phare en goutte à matrice de LED
    c.decal([(1.64, 0.74), (1.48, 0.87), (1.14, 0.86), (1.04, 0.76), (1.18, 0.63), (1.46, 0.62)], 'top', 'dark',
            offset=0.008, name="FenderVent", min_facing=0.3)
    for x in (1.18, 1.30, 1.42, 1.54):
        c.decal(rect(x - 0.014, 0.60, x + 0.014, 0.90), 'top', 'paint', offset=0.011, name="FenderLouvre",
                min_facing=0.3)
    c.decal([(0.63, 0.47), (0.71, 0.40), (0.86, 0.41), (0.93, 0.50), (0.89, 0.61), (0.74, 0.63), (0.65, 0.57)], 'front',
            'dark', name="HeadlightBezel")
    for z in (0.455, 0.505, 0.555):
        for y in (0.71, 0.765, 0.82, 0.875):
            if (y - 0.78) ** 2 / 0.018 + (z - 0.51) ** 2 / 0.0062 <= 1.0:
                c.decal(rect(y - 0.015, z - 0.015, y + 0.015, z + 0.015), 'front', 'led', offset=0.011,
                        name="HeadlightLed")
    # lignes vert citron le long des flancs intérieurs des coques, en V jusqu'à la pointe du nez
    c.decal([(0.96, 0.47), (0.96, 0.505), (2.42, 0.205), (2.42, 0.17)], 'top', 'accent', offset=0.009,
            name="HoodAccent", min_facing=0.15)
    c.box([((2.36, 0.86, 0.30), (0.16, 0.24, 0.012), Matrix.Rotation(math.radians(-14), 3, 'Y'))], 'paint', mirror=True,
          name="DivePlane")
    c.box([((2.43, 0.86, 0.318), (0.012, 0.24, 0.014), Matrix.Rotation(math.radians(-14), 3, 'Y'))], 'accent',
          mirror=True, name="DivePlaneEdge")
    # nez en quille au centre, flanqué de deux grands tunnels sombres qui passent sous les coques d'ailes (signature
    # Valkyrie), une lame verticale dans chaque tunnel ; lame avant carbone à bord lime
    c.decal([(0.17, 0.11), (0.575, 0.11), (0.585, 0.31), (0.55, 0.385), (0.46, 0.415), (0.24, 0.375), (0.17, 0.32)],
            'front', 'dark', name="NoseTunnel")
    c.box([((2.44, 0.375, 0.25), (0.12, 0.022, 0.26), None)], 'black', mirror=True, name="TunnelVane")
    c.box([((2.58, 0.0, 0.095), (0.30, 1.96, 0.03), None)], 'black', name="Splitter")
    c.box([((2.725, 0.0, 0.095), (0.012, 1.92, 0.034), None)], 'accent', name="SplitterEdge")
    # flancs : entrée de radiateur derrière la roue avant, liseré lime le long de l'épaule, plaque de numéro
    c.decal([(0.98, 0.20), (0.60, 0.20), (0.58, 0.56), (0.96, 0.60)], 'side', 'black', offset=0.006, name="SidepodInlet")
    xs = [2.15, 1.90, 1.40, 1.00, 0.80, 0.55, -0.20, -0.60, -1.00, -1.40, -1.95]
    c.decal([(x, sh(x) - 0.050) for x in xs] + [(x, sh(x) - 0.022) for x in reversed(xs)], 'side', 'accent',
            offset=0.009, name="AccentLine")
    c.decal(rect(-0.72, 0.41, -0.33, 0.555), 'side', 'red', offset=0.008, name="NumberPlate")   # panneau Hypercar
    c.decal(rect(-0.31, 0.41, -0.21, 0.555), 'side', 'white', offset=0.008, name="ClassPlate")   # (sans texte)
    mirrors(c, 0.62, 0.80, 0.80, size=(0.09, 0.08, 0.06), color='accent')
    # canopée : bandeau sombre en haut du pare-brise, prise d'air de toit, aileron de requin jusqu'à l'aileron
    c.decal(rect(0.14, -0.34, 0.21, 0.34), 'top', 'black', mirror=False, offset=0.007, name="Banner", min_facing=0.3)
    c.box([((-0.22, 0.0, c.top(-0.22, 0.0) + 0.015), (0.16, 0.14, 0.05), None)], 'black', name="RoofScoop")
    c.prism([(-0.40, 0.98), (-0.80, 0.89), (-1.20, 0.78), (-2.20, 0.68), (-2.20, 1.09), (-0.60, 1.06)], 'y', -0.016, 0.016,
            'black', name="SharkFin")
    # coques arrière : grandes ouïes à lamelles sur le dessus
    c.decal([(-1.14, 0.80), (-1.30, 0.91), (-1.66, 0.90), (-1.80, 0.80), (-1.62, 0.69), (-1.30, 0.69)], 'top', 'dark',
            offset=0.008, name="RearPodVent", min_facing=0.3)
    for x in (-1.31, -1.43, -1.55, -1.67):
        c.decal(rect(x - 0.014, 0.66, x + 0.014, 0.94), 'top', 'paint', offset=0.011, name="PodLouvre", min_facing=0.3)
    # aileron très haut et reculé : deux éléments carbone sur deux pylônes, immenses dérives teal en aileron de requin
    # (bord d'attaque incliné, arête supérieure lime) qui descendent derrière la voiture
    c.prism([(-2.10, 1.085), (-2.16, 1.12), (-2.46, 1.105), (-2.50, 1.08), (-2.20, 1.06)], 'y', -0.965, 0.965, 'black',
            name="WingMain")
    c.prism([(-2.42, 1.135), (-2.46, 1.16), (-2.62, 1.15), (-2.63, 1.125), (-2.48, 1.115)], 'y', -0.965, 0.965, 'black',
            name="WingFlap")
    for y in (0.46, -0.46):
        c.prism([(-2.00, 0.69), (-2.36, 0.66), (-2.36, 1.075), (-2.14, 1.075)], 'y', y - 0.012, y + 0.012, 'black',
                name="WingPylon")
    plate = [(-1.55, 0.80), (-1.92, 1.17), (-2.60, 1.23), (-2.66, 1.17), (-2.66, 0.52), (-2.58, 0.42), (-2.42, 0.42),
             (-2.35, 0.70), (-2.10, 0.78)]
    edge = [(-1.80, 1.05), (-1.92, 1.17), (-2.60, 1.23), (-2.66, 1.17)]
    for s in (1, -1):
        c.prism(plate, 'y', s * 0.962, s * 0.987, 'paint', name="Endplate")
        c.prism(band(edge, 0.004, -0.03), 'y', s * 0.958, s * 0.991, 'accent', name="EndplateEdge")
    # arrière très ouvert : les coques finissent en grandes arches sombres (sorties de roues, tunnels du diffuseur)
    # soulignées par les feux en fine lame qui suivent leur contour ; bloc boîte-structure carbone au centre avec feu
    # de pluie et échappements ; grandes ailettes de diffuseur dans les arches
    sec = P.section(c.car, c.car["rear"])
    pod = smooth([_lerp(sec[9], sec[8], 0.7), sec[8], _lerp(sec[8], sec[7], 0.5), sec[7], sec[6], sec[5],
                  _lerp(sec[5], sec[4], 0.5)], n=1)
    n = len(pod) - 1
    c.decal(band(pod, 0.022, [0.026 + 0.022 * min(1.0, 5.0 * k / n) for k in range(n + 1)]), 'rear', 'tail',
            offset=0.010, name="Tail")
    arch = offset(pod, 0.05)
    c.decal(arch + [(arch[-1][0], 0.09), (arch[0][0], 0.09)], 'rear', 'dark', offset=0.006, name="PodExit")
    c.decal(rect(0.0, 0.09, arch[0][0] - 0.02, 0.60), 'rear', 'black', name="GearboxBay")
    c.decal(rect(-0.06, 0.52, 0.06, 0.58), 'rear', 'tail', mirror=False, offset=0.009, name="RainLight")
    for y in (0.50, 0.68):
        c.prism([(-2.10, 0.09), (-2.10, 0.20), (-2.35, 0.40), (-2.46, 0.38), (-2.52, 0.27), (-2.52, 0.09)], 'y',
                y - 0.011, y + 0.011, 'black', mirror=True, name="DiffuserFin")
    c.prism([(-2.10, 0.09), (-2.10, 0.30), (-2.35, 0.54), (-2.50, 0.50), (-2.60, 0.34), (-2.60, 0.09)], 'y', 0.84,
            0.87, 'black', mirror=True, name="DiffuserFence")
    for y, side in ((0.20, "left"), (-0.20, "right")):
        limb(c, (-2.22, y, 0.44), (-2.42, y, 0.44), 0.035, 'chrome', n=8, name="Exhaust")
        c.marker("exhaust-" + side, (-2.42, y, 0.44))


reg(dict(
    id="valkyrie", file="Valkyrie", name="Aston Martin Valkyrie", color=(0.0037, 0.274, 0.243),
    front=2.45, rear=-2.35, height=1.20, axles=(1.40, -1.40),
    wheel=dict(rt=0.355, rr=0.229, w=0.29, y=0.80, style="multi", rim="black"),
    axle_wheels=[{}, {"w": 0.34}],
    arch_gap=0.04,
    pillar_color="black",
    crown=[(2.45, 0.5), (0.95, 0.5), (0.72, 0.7), (-0.85, 0.7), (-1.10, 0.5), (-1.80, 0.6), (-2.35, 0.85)],
    inset=dict(fl=0.06, rail=[(2.45, .26), (0.95, .26), (0.72, .08), (-0.85, .08), (-1.10, .24), (-2.35, .24)]),
    bands=[(2, 99.0, -99.0, 'black'), (3, 99.0, -99.0, 'black')],      # flancs bas en carbone
    z=dict(
        bot=[(2.45, .10), (2.30, .08), (-2.20, .08), (-2.35, .12)],
        lo=[(2.45, .13), (2.30, .11), (-2.20, .11), (-2.35, .15)],
        rk=[(2.45, .20), (2.30, .18), (-2.20, .18), (-2.35, .22)],
        mx=[(2.45, .34), (2.30, .38), (1.40, .42), (0.60, .40), (-0.60, .40), (-1.40, .44), (-2.35, .46)],
        sh=[(2.45, .46), (2.30, .58), (2.00, .68), (1.40, .72), (1.00, .70), (0.80, .62), (0.55, .60), (-0.60, .62),
            (-1.00, .70), (-1.40, .77), (-1.80, .78), (-2.15, .76), (-2.35, .72)],
        belt=[(2.45, .50), (2.30, .64), (2.00, .74), (1.40, .78), (1.00, .76), (0.80, .64), (0.55, .62), (-0.60, .64),
              (-1.00, .74), (-1.40, .82), (-1.80, .83), (-2.15, .81), (-2.35, .77)],
        re=[(2.45, .56), (2.30, .70), (2.00, .80), (1.40, .84), (1.00, .80), (0.80, .67), (0.72, .70), (0.30, .94),
            (-0.30, .98), (-0.80, .88), (-1.10, .86), (-1.40, .90), (-1.80, .90), (-2.15, .87), (-2.35, .83)],
        top=[(2.45, .38), (2.30, .42), (2.00, .46), (1.40, .50), (1.00, .56), (0.80, .62), (0.72, .70), (0.30, .98),
             (-0.30, 1.02), (-0.80, .90), (-1.10, .80), (-1.40, .74), (-1.80, .72), (-2.15, .70), (-2.35, .66)],
    ),
    w=dict(
        W=[(2.45, .90), (2.30, .98), (2.10, 1.0), (-1.60, 1.0), (-2.10, .98), (-2.35, .92)],
        # canopée en goutte : étroite au pied du pare-brise, la plus large au niveau du pilote, effilée vers l'aileron
        belt=[(2.45, .80), (2.30, .88), (2.00, .92), (1.00, .92), (0.80, .86), (0.72, .46), (0.45, .53), (0.10, .565),
              (-0.20, .55), (-0.45, .49), (-0.65, .43), (-0.90, .80), (-1.20, .90), (-1.60, .92), (-2.35, .86)],
        re=[(2.45, .64), (2.30, .70), (2.00, .72), (1.00, .72), (0.80, .70), (0.72, .33), (0.45, .40), (0.10, .435),
            (-0.20, .425), (-0.45, .37), (-0.65, .31), (-0.90, .74), (-1.20, .80), (-1.60, .80), (-2.15, .80),
            (-2.35, .76)],
    ),
    glass=dict(cowl=0.72, ws_top=0.12, side=[(0.62, -0.32)], pillars=[]),
    details=valkyrie_details,
))

# ================================================================= Renault Twingo (1re génération, phase 1, 1993)
def bumper(c, x_end, z0, z1, zs=0.40, out=0.035, depth=0.07, color='grey', name="Bumper"):
    """Bouclier enveloppant (Twingo) : prisme vertical qui suit le contour en plan de la carrosserie à la hauteur zs,
    décalé de `out` vers l'extérieur, de l'avant du passage de roue gauche (x_end) jusqu'à celui de droite."""
    s = 1 if x_end > 0 else -1
    tip = c.car["front"] if s > 0 else c.car["rear"]
    xc = tip - s * 0.07
    pts = [(x, c.side(x, zs)) for x in (x_end, (x_end + xc) / 2, xc)]
    yc = pts[-1][1]
    for k in range(1, 11):
        y = yc * math.cos(math.pi / 2 * k / 10)
        pts.append(((c.front(y, zs) if s > 0 else c.rear(y, zs)), y))
    ring = pts + [(x, -y) for x, y in reversed(pts[:-1])]
    # trois étages : corps, chanfrein en haut (bord arrondi du plastique), retour en bas
    for a0, a1, o in ((z0 + 0.03, z1 - 0.035, out), (z1 - 0.035, z1, out - 0.018), (z0, z0 + 0.03, out - 0.022)):
        poly = offset(ring, -s * o) + offset(ring, s * depth)[::-1]
        c.prism(poly, 'z', a0, a1, color, name=name)
    return ring[len(pts) - 1][0] + s * out              # x de la face avant (arrière) du bouclier dans l'axe


def twingo_livery(j, xm):
    if j == 6 and 0.95 >= xm >= 0.76:
        return "black"                                           # écusson noir du rétroviseur, devant la vitre
    if j == 7 and 0.30 >= xm >= -1.28:
        return "black"                                           # joncs noirs de gouttière le long du pavillon
    return None


def twingo_details(c):
    # boucliers gris (plastique brut de la phase 1), enveloppants jusqu'aux passages de roue
    xf = bumper(c, 1.50, 0.215, 0.545, name="FrontBumper")
    xr = bumper(c, -1.50, 0.235, 0.635, name="RearBumper")
    # avant : fente de calandre en haut du bouclier, plaque, prise d'air basse ; clignotants orange posés sur le
    # bouclier sous les phares
    c.box([((xf + 0.002, 0.0, 0.475), (0.012, 0.58, 0.04), None)], 'dark', name="GrilleSlot")
    c.box([((xf + 0.004, 0.0, 0.355), (0.012, 0.50, 0.11), None)], 'plate', name="Plate")
    c.box([((xf + 0.002, 0.0, 0.255), (0.012, 0.84, 0.035), None)], 'dark', name="LowIntake")
    c.box([((xf - 0.035, 0.56, 0.556), (0.05, 0.19, 0.024), None)], 'amber', mirror=True, name="Indicator")
    # phares ronds « yeux de grenouille » au coin du nez : paupière couleur caisse qui se fond dans le capot,
    # cerclage noir, verre, optique
    zl = 0.665
    for y in (0.56, -0.56):
        xs = c.front(y, zl)
        limb(c, (xs - 0.11, y, zl), (xs - 0.004, y, zl), 0.101, 'paint', n=16, name="LampPod")
        limb(c, (xs - 0.004, y, zl), (xs + 0.008, y, zl), 0.094, 'black', n=16, name="LampBezel")
        limb(c, (xs + 0.008, y, zl), (xs + 0.016, y, zl), 0.083, 'lens', n=16, name="Headlight")
        limb(c, (xs + 0.016, y, zl), (xs + 0.020, y, zl), 0.048, 'led', n=12, name="HeadlightCore")
    # capot : les quatre ouïes côté droit, juste devant le pare-brise
    for k in range(4):
        y = -0.31 - 0.055 * k
        c.decal([(1.15, y), (1.31, y - 0.02), (1.31, y - 0.045), (1.15, y - 0.025)], 'top', 'dark', mirror=False,
                offset=0.007, name="HoodGill", min_facing=0.3)
    # flancs : répétiteur orange derrière la roue avant, joint arrière et poignée de porte, rétroviseurs noirs
    c.decal(ellipse(0.95, 0.535, 0.032, 0.014, n=8), 'side', 'amber', offset=0.006, name="Repeater")
    c.decal(rect(-0.447, 0.29, -0.437, 0.855), 'side', 'dark', offset=0.006, name="DoorGap")
    c.decal(rect(-0.40, 0.765, -0.31, 0.79), 'side', 'black', offset=0.007, name="Handle")
    mirrors(c, 0.86, 0.86, 0.93, size=(0.10, 0.14, 0.10), color='black')
    # arrière : feux d'angle rouges au-dessus du bouclier, clignotant rond orange côté intérieur ; plaque sur le hayon
    c.decal([(0.49, 0.645), (0.75, 0.645), (0.78, 0.70), (0.775, 0.83), (0.72, 0.878), (0.53, 0.868), (0.49, 0.82)],
            'rear', 'tail', name="Tail", min_facing=0.3)
    c.decal(ellipse(0.57, 0.735, 0.048, 0.048, n=10), 'rear', 'amber', offset=0.009, name="TailIndicator",
            min_facing=0.3)
    c.decal(rect(0.0, 0.665, 0.245, 0.775), 'rear', 'plate', offset=0.012, name="Plate", min_facing=0.3)
    # essuie-glace de la grande lunette, échappement sous le bouclier arrière (à droite) et son repère
    p0 = Vector((c.rear(0.03, 0.99) - 0.012, 0.03, 0.99))
    p1 = Vector((c.rear(0.36, 1.21) - 0.012, 0.36, 1.21))
    limb(c, p0, p1, 0.009, 'black', n=5, name="RearWiper")
    limb(c, p0 + Vector((0.006, 0, 0)), p0 + Vector((-0.02, 0, 0)), 0.022, 'black', n=8, name="WiperPivot")
    limb(c, (xr + 0.18, -0.42, 0.21), (xr - 0.015, -0.42, 0.21), 0.026, 'chrome', n=8, name="Exhaust")
    c.marker("exhaust-right", (xr - 0.015, -0.42, 0.21))


reg(dict(
    id="twingo", file="Twingo", name="Renault Twingo", color=(0.913, 0.527, 0.010),
    front=1.70, rear=-1.675, height=1.423, axles=(1.1735, -1.1735),
    wheel=dict(rt=0.267, rr=0.165, w=0.145, y=0.693, style="hubcap", nseg=20),
    axle_wheels=[{}, {"y": 0.68}],
    arch_n=10,
    pillar_color="paint",
    livery=twingo_livery,
    # carrosserie « dragée » : profils lissés, stations tous les 7 cm, sections subdivisées (flancs, épaules, pavillon)
    smooth=dict(dx=0.07, sub={3: 2, 4: 2, 5: 2, 6: 2, 7: 2, 8: 4}),
    inset=dict(rail=[(1.10, .09), (0.40, .09), (0.30, .05), (-1.20, .05), (-1.30, .08)]),
    z=dict(
        bot=[(1.70, .26), (1.62, .19), (1.45, .15), (-1.45, .15), (-1.60, .21), (-1.675, .28)],
        lo=[(1.70, .30), (1.62, .23), (1.45, .20), (-1.45, .20), (-1.60, .25), (-1.675, .32)],
        rk=[(1.70, .38), (1.62, .32), (1.45, .28), (-1.45, .28), (-1.60, .33), (-1.675, .40)],
        mx=[(1.70, .50), (1.62, .54), (1.17, .56), (-1.17, .58), (-1.63, .60), (-1.675, .58)],
        sh=[(1.70, .551), (1.69, .60), (1.665, .66), (1.62, .70), (1.54, .73), (1.40, .76), (1.10, .80), (0.76, .80),
            (-0.35, .83), (-1.00, .88), (-1.30, .92), (-1.45, .92), (-1.57, .88), (-1.63, .82), (-1.675, .67)],
        belt=[(1.70, .553), (1.69, .63), (1.665, .70), (1.62, .75), (1.54, .79), (1.40, .83), (1.26, .865), (1.10, .915),
              (0.95, .885), (0.76, .85), (-0.35, .875), (-1.00, .94), (-1.30, .985), (-1.45, .975), (-1.57, .92),
              (-1.63, .85), (-1.675, .69)],
        re=[(1.70, .555), (1.69, .635), (1.665, .712), (1.62, .76), (1.54, .80), (1.40, .845), (1.26, .885), (1.10, .94),
            (0.62, 1.205), (0.44, 1.30), (0.30, 1.35), (0.14, 1.38), (0.00, 1.392), (-0.90, 1.395), (-1.10, 1.38),
            (-1.243, 1.35), (-1.35, 1.295), (-1.49, 1.145), (-1.57, .935), (-1.63, .86), (-1.675, .695)],
        top=[(1.70, .56), (1.69, .64), (1.665, .72), (1.62, .77), (1.54, .81), (1.40, .855), (1.26, .895), (1.10, .955),
             (0.62, 1.225), (0.44, 1.32), (0.30, 1.372), (0.14, 1.402), (0.00, 1.415), (-0.40, 1.423), (-0.90, 1.415),
             (-1.10, 1.40), (-1.243, 1.375), (-1.35, 1.32), (-1.49, 1.165), (-1.57, .945), (-1.63, .865),
             (-1.675, .70)],
    ),
    w=dict(
        W=[(1.70, .66), (1.69, .70), (1.665, .74), (1.62, .77), (1.54, .795), (1.40, .81), (1.17, .815), (-1.17, .815),
           (-1.45, .81), (-1.57, .80), (-1.63, .78), (-1.675, .75)],
        belt=[(1.70, .60), (1.69, .64), (1.665, .67), (1.62, .70), (1.54, .725), (1.40, .745), (1.10, .755), (0.76, .768),
              (-0.35, .775), (-1.30, .755), (-1.57, .725), (-1.63, .70), (-1.675, .66)],
        re=[(1.70, .57), (1.69, .61), (1.665, .64), (1.62, .67), (1.54, .695), (1.40, .715), (1.10, .72), (0.32, .665),
            (0.00, .66), (-0.90, .655), (-1.21, .645), (-1.35, .64), (-1.49, .635), (-1.57, .64), (-1.63, .62),
            (-1.675, .60)],
    ),
    glass=dict(cowl=1.10, ws_top=0.32, rw_top=-1.30, rw_bot=-1.56, side=[(0.76, -0.33), (-0.45, -1.30)],
               pillars=[(-0.33, -0.45)]),
    details=twingo_details,
))

PLAYABLE = ["supra", "miata", "mustang", "p50", "ae86", "dmc12", "jesko", "valkyrie", "twingo"]
