"""Pack de véhicules, suite de pack_cars : utilitaires, poids lourds, bus et deux-roues (dimensions réelles, m).

Mêmes conventions que pack_cars (+X avant, +Y gauche, sol z = 0). Les véhicules longs ont leur origine au
milieu de leur longueur hors tout (le trafic suppose un pivot centré pour les distances). Les parties hors de la
carrosserie à sections (caisses, remorques, bennes) sont des prismes et des boîtes ; les deux-roues n'ont pas de
carrosserie à sections (fiche sans « z »).
"""
import math
import bpy
import bmesh
from mathutils import Vector, Matrix
import pack_lib as P
import hc_lib as H
from pack_cars import reg, rect, ellipse


# ================================================================= géométrie
def rot_to(d, up=(0, 0, 1)):
    """Matrice 3x3 alignant l'axe X local sur la direction d (Z local au plus près de `up`)."""
    d = Vector(d).normalized()
    u = Vector(up)
    if abs(d.dot(u.normalized())) > 0.98:
        u = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
    y = u.cross(d).normalized()
    z = d.cross(y).normalized()
    return Matrix((d, y, z)).transposed()


def limb(p0, p1, w, h):
    """Boîte (centre, taille, rotation) de p0 à p1 : section w (latérale) x h."""
    p0, p1 = Vector(p0), Vector(p1)
    return ((p0 + p1) / 2, ((p1 - p0).length, w, h), rot_to(p1 - p0))


def ybox(x0, x1, y0, y1, z0, z1):
    """Boîte alignée (bornes) -> (centre, taille, None)."""
    return (((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)), None)


def tube(c, segs, color, n=8, mirror=False, name="Tube"):
    """segs : (p0, p1, r) ou (p0, p1, r0, r1) -> cylindres / cônes à n côtés, fermés."""
    bm = bmesh.new()
    for sg in segs:
        p0, p1, r0 = Vector(sg[0]), Vector(sg[1]), sg[2]
        r1 = sg[3] if len(sg) > 3 else r0
        ax = (p1 - p0).normalized()
        u = ax.orthogonal().normalized()
        v = ax.cross(u)
        A, B = [], []
        for k in range(n):
            t = 2 * math.pi * (k + 0.5) / n
            d = u * math.cos(t) + v * math.sin(t)
            A.append(bm.verts.new(p0 + d * r0))
            B.append(bm.verts.new(p1 + d * r1))
        for k in range(n):
            k2 = (k + 1) % n
            bm.faces.new((A[k], A[k2], B[k2], B[k]))
        bm.faces.new(list(reversed(A)))
        bm.faces.new(B)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if mirror:
        P.mirror_bm(bm)
    return c.obj(bm, name, color)


def blob(c, items, color, subdiv=1, mirror=False, name="Blob"):
    """items : (centre, (rx, ry, rz)) -> ellipsoïdes facettés (icosphère)."""
    bm = bmesh.new()
    for center, radii in items:
        ret = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
        for v in ret["verts"]:
            v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2])) + Vector(center)
    if mirror:
        P.mirror_bm(bm)
    return c.obj(bm, name, color)


def arc_poly(xc, zc, r, a0, a1, n, r_in=None):
    """Polygone (x, z) d'un arc épais (garde-boue) : de a0 à a1 (degrés), épaisseur r - r_in."""
    outer = [(xc + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)),
              zc + r * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]
    if r_in is None:
        return outer
    inner = [(xc + r_in * math.cos(math.radians(a0 + (a1 - a0) * k / n)),
              zc + r_in * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]
    return outer + list(reversed(inner))


def mudguard(c, xa, rt, y0, y1, color="trim", name="Mudguard", a0=10, a1=170, mirror=True):
    c.prism(arc_poly(xa, rt, rt + 0.08, a0, a1, 6, rt + 0.03), 'y', y0, y1, color, mirror=mirror, name=name)


def bike_wheel(c, x, rt, w, rim, export_name, disc=True):
    """Roue de deux-roues centrée (y = 0), pivot au moyeu : pneu torique, jante à branches, disque de frein."""
    M = c.M
    bm = bmesh.new()
    prof = [(rim, w * 0.30), (rt - w * 0.30, w * 0.50), (rt, w * 0.22), (rt, -w * 0.22), (rt - w * 0.30, -w * 0.50),
            (rim, -w * 0.30)]
    H.revolve(bm, prof, (0, 0, 0), 1, nseg=16, closed_prof=True)
    tire = list(bm.faces)
    for f in tire:
        f.material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=tire)
    n = 12
    fans = []
    for side in (1, -1):
        a = side * w * 0.26
        hub = bm.verts.new((0, a * 1.4, 0))
        ring_in, ring_out = [], []
        for k in range(n):
            t = 2 * math.pi * (k + 0.5) / n
            ring_in.append(bm.verts.new((rim * 0.28 * math.cos(t), a, rim * 0.28 * math.sin(t))))
            ring_out.append(bm.verts.new((rim * math.cos(t), a, rim * math.sin(t))))
        for k in range(n):
            k2 = (k + 1) % n
            f = bm.faces.new((ring_in[k], ring_out[k], ring_out[k2], ring_in[k2]))
            f.material_index = 1 if k % 2 == 0 else 2
            g = bm.faces.new((hub, ring_in[k2], ring_in[k]))
            g.material_index = 3
            fans += [(f, side), (g, side)]
    bm.normal_update()
    for f, side in fans:
        if f.normal.y * side < 0:
            f.normal_flip()
    if disc:
        for side in (1, -1):
            before = set(bm.faces)
            H.revolve(bm, [(rim * 0.45, side * w * 0.36), (rim * 0.92, side * w * 0.36), (rim * 0.92, side * w * 0.30),
                           (rim * 0.45, side * w * 0.30)], (0, 0, 0), 1, nseg=12, closed_prof=True)
            new = [f for f in bm.faces if f not in before]
            for f in new:
                f.material_index = 3
            bmesh.ops.recalc_face_normals(bm, faces=new)
    ob = H.bm_to_obj(bm, "PK_%s_%s" % (c.car["id"], export_name), c.col,
                     [M["tire"], M["rim"], M["dark"], M["grey"]])
    ob.location = (x, 0, rt)
    ob["export_name"] = export_name
    return ob


def rider(c, hip, shoulder, head, hand, elbow, knee, ankle, foot, jacket="black", pants="dark", helmet="white"):
    """Pilote assis (points côté gauche +Y ; le côté droit est symétrique)."""
    H0, S0 = Vector(hip), Vector(shoulder)
    # bassin, torse
    c.box([limb(H0 - Vector((0.08, 0, 0.02)), H0 + Vector((0.10, 0, 0.02)), 0.34, 0.20)], pants, name="RiderHips")
    c.box([limb(H0 + Vector((0, 0, 0.04)), S0 + Vector((0, 0, 0.02)), 0.40, 0.24)], jacket, name="RiderTorso")
    # bras (épaule -> coude -> main), jambes (hanche -> genou -> cheville), pieds
    sh = S0 + Vector((0, 0.19, -0.02))
    hp = H0 + Vector((0.03, 0.12, 0.0))
    c.box([limb(sh, elbow, 0.105, 0.105), limb(elbow, hand, 0.095, 0.09)], jacket, mirror=True, name="RiderArms")
    c.box([(Vector(hand) + Vector((0.02, 0, 0)), (0.09, 0.09, 0.08), None)], "black", mirror=True, name="RiderGloves")
    c.box([limb(hp, knee, 0.16, 0.16), limb(knee, ankle, 0.125, 0.12)], pants, mirror=True, name="RiderLegs")
    c.box([limb(Vector(ankle) - Vector((0.04, 0, 0.02)), Vector(foot) + Vector((0.08, 0, -0.02)), 0.10, 0.10)],
          "black", mirror=True, name="RiderBoots")
    # cou (col du blouson), casque + visière
    hd = Vector(head)
    c.box([limb(S0 + Vector((0, 0, 0.01)), hd - Vector((0.02, 0, 0.09)), 0.13, 0.13)], jacket, name="RiderNeck")
    blob(c, [(hd, (0.145, 0.13, 0.145))], helmet, subdiv=2, name="Helmet")
    c.box([(hd + Vector((0.118, 0, -0.005)), (0.04, 0.19, 0.075), None)], "dark", name="Visor")


def truck_mirrors(c, x, y, z, arm_from, color="trim", size=(0.09, 0.20, 0.42)):
    """Rétroviseurs de poids lourd : bras depuis la cabine, grand miroir vertical + miroir d'angle."""
    c.box([limb(arm_from, (x, y - 0.02, z + size[2] * 0.3), 0.05, 0.05),
           limb(arm_from, (x, y - 0.02, z - size[2] * 0.3), 0.05, 0.05),
           ((x, y, z), size, None),
           ((x + 0.02, y - 0.02, z - size[2] / 2 - 0.13), (size[0], size[1] * 0.8, 0.18), None)],
          color, mirror=True, name="TruckMirror")


# ================================================================= bus urbain (type Mercedes Citaro, 12 m)
BUS_PILLARS = [(x + 0.06, x - 0.06) for x in (4.30, 3.00, 1.70, 0.40, -0.90, -2.20, -3.50, -4.80)]


def bus_details(c):
    F, R = 6.0, -6.0
    # face avant : grand pare-brise, girouette, optiques, pare-chocs, essuie-glaces
    c.decal([(0.0, 0.96), (1.10, 0.98), (1.17, 1.08), (1.17, 2.55), (1.10, 2.60), (0.0, 2.60)], 'front', 'glass',
            name="Windshield")
    c.decal(rect(0.0, 2.645, 0.95, 2.87), 'front', 'dark', name="DestSign")
    c.decal(rect(0.0, 2.715, 0.62, 2.80), 'front', 'amber', offset=0.009, name="DestText")
    c.decal([(0.80, 0.58), (1.10, 0.58), (1.15, 0.80), (0.86, 0.80)], 'front', 'lens', name="Headlight")
    c.decal(rect(0.84, 0.83, 1.12, 0.865), 'front', 'led', offset=0.009, name="DRL")
    c.decal(rect(0.0, 0.30, 1.21, 0.52), 'front', 'grey', name="Bumper")
    c.decal(rect(0.0, 0.345, 0.26, 0.455), 'front', 'plate', offset=0.012, name="Plate")
    c.box([limb((F + 0.02, 0.05, 1.03), (F + 0.02, 0.95, 1.12), 0.03, 0.025),
           limb((F + 0.02, -0.95, 1.03), (F + 0.02, -0.05, 1.12), 0.03, 0.025)], 'trim', name="Wipers")
    # rétroviseurs « oreilles de lapin »
    c.box([limb((F - 0.08, 1.20, 2.74), (F + 0.30, 1.30, 2.42), 0.05, 0.05),
           ((F + 0.33, 1.31, 2.12), (0.09, 0.22, 0.46), None)], 'trim', mirror=True, name="BusMirror")
    # flancs : bande de protection ; portes (côté droit) : cadre sombre + deux vantaux vitrés
    c.decal([(F - 0.1, 0.50), (R + 0.1, 0.50), (R + 0.1, 0.55), (F - 0.1, 0.55)], 'side', 'trim', offset=0.006,
            name="RubStrip")
    for x0, x1 in ((5.45, 4.25), (0.55, -0.65)):
        c.decal(rect(x1 - 0.05, 0.34, x0 + 0.05, 2.66), 'rside', 'dark', mirror=False, offset=0.008, name="DoorFrame")
        xm = (x0 + x1) / 2
        c.decal(rect(xm + 0.02, 0.40, x0 - 0.02, 2.60), 'rside', 'glass', mirror=False, offset=0.012, name="DoorL")
        c.decal(rect(x1 + 0.02, 0.40, xm - 0.02, 2.60), 'rside', 'glass', mirror=False, offset=0.012, name="DoorR")
    c.decal(rect(4.45, 2.30, 5.30, 2.45), 'rside', 'amber', mirror=False, offset=0.012, name="SideSign")
    # arrière : lunette, grille moteur, feux verticaux, pare-chocs, plaque
    c.decal(rect(0.0, 1.95, 1.02, 2.62), 'rear', 'glass', name="RearWindow")
    c.decal(rect(0.0, 0.62, 0.98, 1.50), 'rear', 'dark', name="EngineGrille")
    for z in (0.78, 0.96, 1.14, 1.32):
        c.decal(rect(0.0, z, 0.92, z + 0.05), 'rear', 'grey', offset=0.009, name="Louvre")
    c.decal(rect(1.04, 0.62, 1.20, 1.78), 'rear', 'tail', name="Tail")
    c.decal(rect(1.06, 1.20, 1.18, 1.32), 'rear', 'amber', offset=0.009, name="Indicator")
    c.decal(rect(0.0, 0.30, 1.21, 0.55), 'rear', 'grey', name="RearBumper")
    c.decal(rect(0.0, 0.36, 0.26, 0.47), 'rear', 'plate', offset=0.012, name="Plate")
    c.decal(rect(0.0, 2.78, 0.40, 2.84), 'rear', 'tail', offset=0.008, name="Brake")
    # toit : climatisation, trappes
    zr = c.top(-0.5) or 2.95
    c.box([ybox(1.2, -2.2, -0.92, 0.92, zr - 0.02, zr + 0.17)], 'white', name="AirCon")
    c.box([ybox(3.6, 2.9, -0.35, 0.35, zr - 0.02, zr + 0.06), ybox(-3.2, -3.9, -0.35, 0.35, zr - 0.02, zr + 0.06)],
          'grey', name="Hatches")


reg(dict(
    id="bus", file="Bus", name="Bus urbain", color=(0.10, 0.30, 0.62),
    front=6.0, rear=-6.0, height=3.12, axles=(3.30, -2.60),
    wheel=dict(rt=0.485, rr=0.29, w=0.28, y=1.10, style="truck"),
    axle_wheels=[{}, {"twin": True}],
    arch_gap=0.06, arch_n=6, arch_sq=1.0,
    inset=dict(fl=0.10, lo=0.03, rk=0.01, sh=0.01, rail=0.26),
    crown=0.75,
    z=dict(
        bot=[(6.0, .36), (5.85, .30), (-5.85, .30), (-6.0, .36)],
        lo=[(6.0, .40), (5.85, .33), (-5.85, .33), (-6.0, .40)],
        rk=[(6.0, .62), (5.85, .58), (-5.85, .58), (-6.0, .62)],
        mx=[(6.0, .95), (-6.0, .95)],
        sh=[(6.0, 1.00), (-6.0, 1.00)],
        belt=[(6.0, 1.10), (5.62, 1.15), (-6.0, 1.15)],
        re=[(6.0, 2.60), (5.62, 2.62), (-6.0, 2.62)],
        top=[(6.0, 2.88), (5.85, 2.95), (-5.85, 2.95), (-6.0, 2.90)],
    ),
    w=dict(
        W=[(6.0, 1.19), (5.93, 1.25), (5.80, 1.275), (-5.82, 1.275), (-5.95, 1.25), (-6.0, 1.20)],
        belt=[(6.0, 1.17), (5.93, 1.235), (5.80, 1.262), (-5.82, 1.262), (-6.0, 1.18)],
        re=[(6.0, 1.14), (5.93, 1.21), (5.80, 1.24), (-5.82, 1.24), (-6.0, 1.15)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[(5.60, -5.62)], pillars=BUS_PILLARS),
    pillar_color="dark",
    details=bus_details,
))

# ================================================================= autocar (type Setra / Tourismo, 12,2 m)
COACH_PILLARS = [(x + 0.07, x - 0.07) for x in (4.30, 2.85, 1.40, -0.05, -1.50, -2.95, -4.40)]


def coach_details(c):
    F, R = 6.1, -6.1
    c.decal([(0.0, 1.30), (1.02, 1.30), (1.13, 1.42), (1.15, 3.18), (1.05, 3.32), (0.0, 3.34)], 'front', 'glass',
            name="Windshield")
    c.decal(rect(0.0, 3.36, 0.90, 3.52), 'front', 'dark', name="DestSign")
    c.decal(rect(0.0, 3.40, 0.55, 3.47), 'front', 'amber', offset=0.009, name="DestText")
    c.decal([(0.72, 0.64), (1.08, 0.64), (1.15, 0.90), (0.80, 0.90)], 'front', 'lens', name="Headlight")
    c.decal(rect(0.78, 0.93, 1.10, 0.965), 'front', 'led', offset=0.009, name="DRL")
    c.decal(rect(0.0, 0.66, 0.60, 1.02), 'front', 'dark', name="Grille")
    for z in (0.74, 0.84, 0.94):
        c.decal(rect(0.0, z, 0.56, z + 0.03), 'front', 'chrome', offset=0.009, name="GrilleBar")
    c.decal(rect(0.0, 0.33, 1.19, 0.60), 'front', 'grey', name="Bumper")
    c.decal(rect(0.0, 0.40, 0.26, 0.51), 'front', 'plate', offset=0.012, name="Plate")
    c.box([limb((F - 0.02, 0.05, 1.36), (F - 0.02, 0.95, 1.44), 0.03, 0.025),
           limb((F - 0.02, -0.95, 1.36), (F - 0.02, -0.05, 1.44), 0.03, 0.025)], 'trim', name="Wipers")
    c.box([limb((F - 0.30, 1.18, 3.45), (F + 0.28, 1.32, 3.02), 0.05, 0.05),
           ((F + 0.31, 1.33, 2.70), (0.09, 0.24, 0.50), None)], 'trim', mirror=True, name="CoachMirror")
    # soutes : joints sombres ; porte avant (côté droit)
    for x0, x1 in ((2.72, -2.12), (-3.55, -5.30)):
        c.decal([(x0, 1.56), (x1, 1.56), (x1, 1.582), (x0, 1.582)], 'side', 'dark', offset=0.004, name="BayGap")
    for xx in (2.72, 1.10, -0.52, -2.12, -3.55, -5.30):
        c.decal(rect(xx - 0.011, 0.62, xx + 0.011, 1.56), 'side', 'dark', offset=0.004, name="BayGap")
    c.decal(rect(4.58, 0.40, 5.58, 3.28), 'rside', 'dark', mirror=False, offset=0.008, name="DoorFrame")
    c.decal(rect(4.64, 0.46, 5.52, 3.22), 'rside', 'glass', mirror=False, offset=0.012, name="Door")
    c.decal([(F - 0.1, 0.60), (R + 0.1, 0.60), (R + 0.1, 0.64), (F - 0.1, 0.64)], 'side', 'trim', offset=0.006,
            name="RubStrip")
    # arrière : lunette, capot moteur, feux, pare-chocs
    c.decal(rect(0.0, 2.20, 1.00, 3.25), 'rear', 'glass', name="RearWindow")
    c.decal(rect(0.0, 0.70, 0.95, 1.70), 'rear', 'dark', name="EngineGrille")
    for z in (0.85, 1.05, 1.25, 1.45):
        c.decal(rect(0.0, z, 0.90, z + 0.05), 'rear', 'grey', offset=0.009, name="Louvre")
    c.decal(rect(1.02, 0.70, 1.18, 1.95), 'rear', 'tail', name="Tail")
    c.decal(rect(1.04, 1.32, 1.16, 1.45), 'rear', 'amber', offset=0.009, name="Indicator")
    c.decal(rect(0.0, 0.33, 1.19, 0.62), 'rear', 'grey', name="RearBumper")
    c.decal(rect(0.0, 0.42, 0.26, 0.53), 'rear', 'plate', offset=0.012, name="Plate")
    c.decal(rect(0.0, 3.42, 0.45, 3.48), 'rear', 'tail', offset=0.008, name="Brake")
    zr = c.top(-0.8) or 3.66
    c.box([ybox(0.8, -2.6, -0.90, 0.90, zr - 0.02, zr + 0.14)], 'white', name="AirCon")


reg(dict(
    id="coach", file="Coach", name="Autocar", color=(0.85, 0.86, 0.88),
    front=6.1, rear=-6.1, height=3.70, axles=(3.35, -2.85),
    wheel=dict(rt=0.49, rr=0.29, w=0.30, y=1.09, style="truck"),
    axle_wheels=[{}, {"twin": True}],
    arch_gap=0.06, arch_n=6,
    inset=dict(fl=0.10, lo=0.03, rk=0.01, sh=0.01, rail=0.30),
    crown=0.8,
    z=dict(
        bot=[(6.1, .40), (5.9, .33), (-5.9, .33), (-6.1, .40)],
        lo=[(6.1, .44), (5.9, .36), (-5.9, .36), (-6.1, .44)],
        rk=[(6.1, .60), (5.9, .56), (-5.9, .56), (-6.1, .60)],
        mx=[(6.1, 1.00), (5.95, 1.05), (-6.1, 1.05)],
        sh=[(6.1, 1.08), (5.95, 1.35), (5.80, 1.60), (-6.0, 1.60), (-6.1, 1.55)],
        belt=[(6.1, 1.12), (5.95, 1.50), (5.80, 1.85), (-5.9, 1.85), (-6.1, 1.80)],
        re=[(6.1, 1.16), (5.95, 2.30), (5.85, 3.10), (5.78, 3.30), (-5.9, 3.30), (-6.1, 3.25)],
        top=[(6.1, 1.20), (5.95, 2.40), (5.85, 3.25), (5.75, 3.55), (5.50, 3.66), (-5.6, 3.68), (-6.0, 3.58),
             (-6.1, 3.45)],
    ),
    w=dict(
        W=[(6.1, 1.18), (6.0, 1.24), (5.85, 1.275), (-5.9, 1.275), (-6.05, 1.24), (-6.1, 1.18)],
        belt=[(6.1, 1.15), (6.0, 1.22), (5.85, 1.262), (-5.9, 1.262), (-6.1, 1.16)],
        re=[(6.1, 1.12), (6.0, 1.18), (5.85, 1.22), (-5.9, 1.22), (-6.1, 1.12)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[(5.70, -5.55)], pillars=COACH_PILLARS),
    pillar_color="dark",
    details=coach_details,
))


# ================================================================= cabine avancée (porteurs, tracteurs)
def cab_front(c, F, zb, zg0, zg1, zw0, zw1, half, lamp_z=(0.68, 0.86), grille_w=0.72, bars=3):
    """Face avant de cabine avancée : pare-chocs, calandre à barrettes, phares d'angle, pare-brise, essuie-glaces."""
    c.decal(rect(0.0, zb, half, zb + 0.42), 'front', 'grey', name="Bumper")
    c.decal(rect(half - 0.34, lamp_z[0], half - 0.03, lamp_z[1]), 'front', 'lens', offset=0.009, name="Headlight")
    c.decal(rect(half - 0.30, lamp_z[1] + 0.02, half - 0.06, lamp_z[1] + 0.05), 'front', 'led', offset=0.012,
            name="DRL")
    c.decal(rect(half - 0.10, lamp_z[0] - 0.08, half - 0.02, lamp_z[0] - 0.02), 'front', 'amber', offset=0.012,
            name="Indicator")
    c.decal(rect(0.0, zg0, grille_w, zg1), 'front', 'dark', name="Grille")
    for k in range(bars):
        z = zg0 + (zg1 - zg0) * (k + 0.6) / (bars + 0.4)
        c.decal(rect(0.0, z, grille_w - 0.04, z + 0.035), 'front', 'chrome', offset=0.009, name="GrilleBar")
    c.decal(rect(0.0, zb + 0.10, 0.26, zb + 0.21), 'front', 'plate', offset=0.013, name="Plate")
    c.decal([(0.0, zw0), (half - 0.10, zw0), (half - 0.04, zw0 + 0.08), (half - 0.04, zw1 - 0.06),
             (half - 0.12, zw1), (0.0, zw1)], 'front', 'glass', name="Windshield")
    c.box([limb((F + 0.02, 0.06, zw0 + 0.05), (F + 0.02, half - 0.15, zw0 + 0.10), 0.03, 0.025),
           limb((F + 0.02, -(half - 0.15), zw0 + 0.05), (F + 0.02, -0.06, zw0 + 0.10), 0.03, 0.025)], 'trim',
          name="Wipers")


def chassis(c, x0, x1, z0=0.72, z1=0.98, y=0.43, color="dark"):
    c.box([ybox(x0, x1, y - 0.05, y + 0.05, z0, z1)], color, mirror=True, name="ChassisRail")


def side_guard(c, x0, x1, y, zs=(0.48, 0.80), color="grey"):
    c.box([ybox(x0, x1, y - 0.03, y + 0.02, z, z + 0.09) for z in zs] +
          [ybox(x, x - 0.06, y - 0.06, y - 0.01, zs[0], zs[-1] + 0.09) for x in (x0, (x0 + x1) / 2, x1 + 0.06)],
          color, mirror=True, name="SideGuard")


def rear_bar(c, xr, half, z=0.50, lights_z=0.88, plate=True):
    """Barre anti-encastrement, feux arrière, clignotants, plaque."""
    c.box([ybox(xr + 0.10, xr + 0.02, -half + 0.10, half - 0.10, z, z + 0.14)], 'grey', name="RearBar")
    c.box([ybox(xr + 0.40, xr + 0.10, 0.55, 0.62, z + 0.07, lights_z)], 'grey', mirror=True, name="RearBarPost")
    c.box([ybox(xr + 0.05, xr - 0.01, half - 0.42, half - 0.10, lights_z - 0.05, lights_z + 0.07)], 'tail',
          mirror=True, name="TailLamp")
    c.box([ybox(xr + 0.05, xr - 0.01, half - 0.52, half - 0.44, lights_z - 0.05, lights_z + 0.07)], 'amber',
          mirror=True, name="RearIndicator")
    if plate:
        c.box([ybox(xr + 0.03, xr - 0.005, -0.26, 0.26, z + 0.20, z + 0.31)], 'plate', name="Plate")


def box_body(c, x0, x1, half, z0, z1, color="white", rails="grey", doors=True, markers=True):
    """Caisse fourgon : volume, lisses et montants, portes arrière à crémones, feux de gabarit."""
    c.prism([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], 'y', -half, half, color, name="Box")
    c.box([ybox(x0, x1, half - 0.03, half + 0.012, z1 - 0.10, z1),
           ybox(x0, x1, half - 0.03, half + 0.012, z0, z0 + 0.14),
           ybox(x1 + 0.08, x1 - 0.012, half - 0.10, half + 0.012, z0, z1),
           ybox(x0 + 0.012, x0 - 0.08, half - 0.10, half + 0.012, z0, z1)], rails, mirror=True, name="BoxRails")
    if doors:
        c.box([ybox(x1 + 0.005, x1 - 0.015, -0.012, 0.012, z0 + 0.10, z1 - 0.08)], 'dark', name="DoorGap")
        c.box([ybox(x1 - 0.005, x1 - 0.035, y - 0.012, y + 0.012, z0 + 0.12, z1 - 0.10) for y in (0.28, 0.86)] +
              [ybox(x1 - 0.005, x1 - 0.04, y - 0.05, y + 0.05, (z0 + z1) / 2 - 0.05, (z0 + z1) / 2 + 0.05)
               for y in (0.28, 0.86)], 'grey', mirror=True, name="DoorBars")
        c.box([ybox(x1 + 0.005, x1 - 0.02, half - 0.12, half - 0.04, z1 - 0.10, z1 - 0.04)], 'tail', mirror=True,
              name="TopMarker")
    if markers:
        n = max(2, int((x0 - x1) / 2.0))
        c.box([ybox(x0 - (x0 - x1) * (k + 0.5) / n + 0.05, x0 - (x0 - x1) * (k + 0.5) / n - 0.05, half,
                    half + 0.02, z0 + 0.02, z0 + 0.10) for k in range(n)], 'amber', mirror=True, name="SideMarker")


# ================================================================= camion porteur fourgon (12-16 t, cabine courte)
def boxtruck_details(c):
    F, R = 4.675, -4.675
    cab_front(c, F, 0.45, 1.00, 1.50, 1.62, 2.52, 1.15)
    c.box([ybox(F + 0.12, F - 0.05, -1.08, 1.08, 2.54, 2.62)], 'trim', name="SunVisor")
    truck_mirrors(c, F + 0.12, 1.36, 2.02, (F - 0.15, 1.15, 2.30))
    c.box([ybox(F - 0.30, F - 0.75, 1.02, 1.13, 0.60, 0.66), ybox(F - 0.30, F - 0.75, 1.02, 1.13, 0.86, 0.92)],
          'trim', mirror=True, name="Steps")
    c.decal(rect(3.35, 1.66, 3.55, 1.70), 'side', 'dark', offset=0.006, name="Handle")
    chassis(c, 3.4, R + 0.12)
    c.box([ybox(2.55, R + 0.05, -0.50, 0.50, 0.98, 1.12)], 'dark', name="Subframe")
    box_body(c, 2.52, R, 1.26, 1.12, 3.75)
    mudguard(c, -1.625, 0.47, 0.70, 1.24)
    side_guard(c, 2.30, -0.95, 1.22)
    c.box([ybox(1.85, 0.95, 0.72, 1.06, 0.52, 0.94)], 'chrome', name="FuelTank")
    c.box([ybox(1.80, 1.20, -1.06, -0.74, 0.55, 0.90)], 'dark', name="Battery")
    rear_bar(c, R, 1.20)


reg(dict(
    id="boxtruck", file="BoxTruck", name="Camion porteur", color=(0.80, 0.12, 0.10),
    front=4.675, rear=2.62, height=3.75, axles=(3.375, -1.625),
    wheel=dict(rt=0.47, rr=0.28, w=0.26, y=1.07, style="truck"),
    axle_wheels=[{}, {"twin": True}],
    arch_axles=[0], arch_gap=0.05, arch_n=6,
    inset=dict(fl=0.05, lo=0.02, rk=0.01, sh=0.005, rail=0.16),
    crown=0.8,
    z=dict(
        bot=[(4.675, .50), (2.62, .50)],
        lo=[(4.675, .53), (2.62, .53)],
        rk=[(4.675, .90), (2.62, 1.00)],
        mx=[(4.675, 1.20), (2.62, 1.20)],
        sh=[(4.675, 1.40), (2.62, 1.45)],
        belt=[(4.675, 1.55), (4.50, 1.60), (2.62, 1.62)],
        re=[(4.675, 2.55), (4.55, 2.60), (2.62, 2.62)],
        top=[(4.675, 2.70), (4.50, 2.90), (4.20, 2.95), (2.62, 2.95)],
    ),
    w=dict(
        W=[(4.675, 1.10), (4.58, 1.14), (4.45, 1.155), (2.62, 1.155)],
        belt=[(4.675, 1.08), (4.50, 1.14), (2.62, 1.14)],
        re=[(4.675, 1.03), (4.50, 1.09), (2.62, 1.10)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[(4.35, 3.40)], pillars=[]),
    details=boxtruck_details,
))


# ================================================================= camion-benne à ordures (cabine basse, benne compacteuse)
def garbage_details(c):
    F, R = 4.9, -4.9
    cab_front(c, F, 0.42, 0.55, 0.95, 1.08, 2.55, 1.24, lamp_z=(0.60, 0.80), grille_w=0.80, bars=2)
    c.box([ybox(F + 0.10, F - 0.05, -1.12, 1.12, 2.57, 2.64)], 'trim', name="SunVisor")
    truck_mirrors(c, F + 0.14, 1.42, 1.85, (F - 0.12, 1.24, 2.20))
    c.decal(rect(3.38, 0.50, 3.97, 2.60), 'rside', 'dark', mirror=False, offset=0.005, name="DoorFrame")
    c.decal(rect(3.40, 0.52, 3.95, 1.10), 'rside', 'glass', mirror=False, offset=0.008, name="DoorLow")
    chassis(c, 3.0, -4.3)
    # benne compacteuse + lève-conteneurs
    c.prism([(2.45, 1.05), (-3.50, 1.05), (-3.50, 3.45), (2.30, 3.45), (2.45, 3.30)], 'y', -1.25, 1.25, 'green',
            name="Body")
    c.prism([(-3.48, 1.00), (-4.75, 0.98), (-4.90, 1.10), (-4.90, 2.35), (-4.80, 2.85), (-4.45, 3.25),
             (-3.95, 3.42), (-3.48, 3.45)], 'y', -1.22, 1.22, 'green', name="Tailgate")
    c.box([ybox(-4.89, -4.93, -0.95, 0.95, 1.22, 2.22)], 'dark', name="Hopper")
    c.box([ybox(-4.85, -5.00, -1.05, 1.05, 0.95, 1.08), ybox(-4.92, -5.02, 0.70, 0.78, 0.55, 1.05),
           ybox(-4.92, -5.02, -0.78, -0.70, 0.55, 1.05)], 'grey', name="BinLifter")
    c.box([ybox(2.2, -3.4, 1.25, 1.262, 2.10, 2.28), ybox(-3.5, -4.6, 1.22, 1.232, 2.10, 2.28)], 'white',
          mirror=True, name="Stripe")
    for k in range(6):
        x = -3.55 - k * 0.20
        c.box([limb((x, 1.23, 1.05), (x - 0.18, 1.23, 1.45), 0.012, 0.09)], 'red' if k % 2 else 'white',
              mirror=True, name="Chevron")
    # marchepieds et poignées des ripeurs, gyrophares
    c.box([ybox(-4.20, -4.85, 0.55, 1.15, 0.52, 0.58)], 'grey', mirror=True, name="RearStep")
    c.box([ybox(-4.90, -4.94, 1.10, 1.16, 1.15, 2.15)], 'grey', mirror=True, name="Handle")
    c.box([ybox(2.95, 2.75, 0.92, 1.10, 2.86, 3.00), ybox(-3.70, -3.90, 1.02, 1.18, 3.45, 3.60)], 'amber',
          mirror=True, name="Beacon")
    mudguard(c, -0.55, 0.50, 0.72, 1.24)
    mudguard(c, -1.90, 0.50, 0.72, 1.24)
    side_guard(c, 2.35, 0.20, 1.20)
    c.box([ybox(1.95, 1.15, 0.74, 1.06, 0.50, 0.92)], 'chrome', name="FuelTank")
    c.box([ybox(-4.92, -4.97, 1.00, 1.18, 1.20, 1.40), ybox(-4.92, -4.97, 1.00, 1.18, 1.42, 1.50)], 'tail',
          mirror=True, name="TailLamp")
    c.box([ybox(-4.95, -4.99, -0.26, 0.26, 0.62, 0.73)], 'plate', name="Plate")


reg(dict(
    id="garbage", file="GarbageTruck", name="Camion-benne à ordures", paint_as="white", color=(0.92, 0.92, 0.9),
    front=4.9, rear=2.55, height=3.60, axles=(3.35, -0.55, -1.90),
    wheel=dict(rt=0.50, rr=0.29, w=0.30, y=1.09, style="truck"),
    axle_wheels=[{}, {"twin": True}, {}],
    arch_axles=[0], arch_gap=0.06, arch_n=6,
    inset=dict(fl=0.05, lo=0.02, rk=0.01, sh=0.005, rail=0.16),
    crown=0.8,
    z=dict(
        bot=[(4.9, .45), (2.55, .45)],
        lo=[(4.9, .48), (2.55, .48)],
        rk=[(4.9, .62), (2.55, .62)],
        mx=[(4.9, .85), (2.55, .85)],
        sh=[(4.9, .95), (2.55, .95)],
        belt=[(4.9, 1.05), (4.75, 1.08), (2.55, 1.10)],
        re=[(4.9, 2.55), (4.75, 2.62), (2.55, 2.62)],
        top=[(4.9, 2.70), (4.75, 2.82), (4.55, 2.86), (2.55, 2.86)],
    ),
    w=dict(
        W=[(4.9, 1.18), (4.8, 1.235), (4.65, 1.25), (2.55, 1.25)],
        belt=[(4.9, 1.16), (4.8, 1.22), (4.65, 1.24), (2.55, 1.24)],
        re=[(4.9, 1.10), (4.8, 1.18), (4.65, 1.20), (2.55, 1.20)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[(4.70, 3.35)], pillars=[]),
    details=garbage_details,
))

# ================================================================= ensembles articulés (tracteur + semi-remorque, 16,6 m)
TRACTOR_Z = dict(
    bot=[(8.3, .55), (6.05, .60)],
    lo=[(8.3, .58), (6.05, .63)],
    rk=[(8.3, .75), (6.05, .95)],
    mx=[(8.3, 1.25), (6.05, 1.30)],
    sh=[(8.3, 1.50), (6.05, 1.55)],
    belt=[(8.3, 1.75), (8.15, 1.80), (6.05, 1.82)],
    re=[(8.3, 2.75), (8.15, 2.80), (6.05, 2.85)],
    top=[(8.3, 2.95), (8.20, 3.60), (8.05, 3.82), (7.70, 3.85), (6.05, 3.85)],
)
TRACTOR_W = dict(
    W=[(8.3, 1.16), (8.2, 1.225), (8.05, 1.245), (6.05, 1.245)],
    belt=[(8.3, 1.14), (8.2, 1.21), (8.05, 1.235), (6.05, 1.235)],
    re=[(8.3, 1.10), (8.2, 1.17), (8.05, 1.20), (6.05, 1.20)],
)


def tractor(c):
    """Tracteur haute cabine (type Volvo FH / Actros) : face avant, carénages, réservoirs, sellette, ailes."""
    F = 8.3
    cab_front(c, F, 0.45, 1.02, 1.72, 1.80, 2.74, 1.24, lamp_z=(0.94, 1.12), grille_w=0.86, bars=4)
    c.box([ybox(F + 0.14, F - 0.05, -1.12, 1.12, 2.76, 2.86)], 'paint', name="SunVisor")
    c.box([ybox(F - 0.02, F - 0.10, y - 0.04, y + 0.04, 3.84, 3.90) for y in (-0.6, -0.3, 0.3, 0.6)], 'amber',
          name="RoofLights")
    truck_mirrors(c, F + 0.14, 1.44, 2.20, (F - 0.18, 1.24, 2.55))
    c.box([ybox(F - 0.30, F - 0.80, 1.14, 1.24, 0.62, 0.68), ybox(F - 0.30, F - 0.80, 1.14, 1.24, 0.90, 0.96)],
          'trim', mirror=True, name="Steps")
    c.decal(rect(6.30, 1.86, 6.52, 1.90), 'side', 'dark', offset=0.006, name="Handle")
    chassis(c, 7.6, 2.35)
    # carénages latéraux derrière la cabine, réservoir (gauche), coffre à batteries (droite)
    c.box([ybox(6.0, 5.95, 0.60, 1.20, 0.45, 1.25)], 'paint', mirror=True, name="SideSkirt")
    c.box([ybox(5.90, 4.60, 0.72, 1.14, 0.50, 1.02)], 'chrome', name="FuelTank")
    c.box([ybox(5.90, 5.00, -1.10, -0.72, 0.52, 0.98)], 'dark', name="BatteryBox")
    c.box([ybox(5.9, 5.45, -0.60, 0.60, 1.08, 1.12)], 'grey', name="Catwalk")
    c.box([ybox(3.95, 3.15, -0.62, 0.62, 1.06, 1.16)], 'dark', name="FifthWheel")
    mudguard(c, 3.15, 0.51, 0.72, 1.24, a0=0, a1=180)


def semi_trailer_frame(c, xf, xr, axles, rt):
    """Châssis de semi-remorque : longerons, béquilles, garde-corps latéraux, aile continue, bavettes, feux."""
    c.box([ybox(xf - 0.2, xr + 0.10, -0.48, 0.48, 0.96, 1.20)], 'dark', name="TrailerFrame")
    xl = xf - 2.1
    c.box([ybox(xl + 0.07, xl - 0.07, 0.78, 0.92, 0.30, 1.00), ybox(xl + 0.18, xl - 0.18, 0.72, 0.98, 0.22, 0.30)],
          'grey', mirror=True, name="LandingGear")
    side_guard(c, xl - 0.3, axles[0] + rt + 0.25, 1.24)
    x0, x1 = axles[0] + rt + 0.12, axles[-1] - rt - 0.12
    c.box([ybox(x0, x1, 0.90, 1.26, rt * 2 + 0.07, rt * 2 + 0.13)], 'trim', mirror=True, name="Fender")
    c.box([ybox(x1 + 0.02, x1 - 0.02, 0.92, 1.24, 0.30, rt * 2 + 0.07)], 'dark', mirror=True, name="MudFlap")
    rear_bar(c, xr, 1.24, z=0.46, lights_z=0.96)
    n = 6
    c.box([ybox(xf - (xf - xr) * (k + 0.5) / n + 0.05, xf - (xf - xr) * (k + 0.5) / n - 0.05, 1.262, 1.282,
                1.20, 1.28) for k in range(n)], 'amber', mirror=True, name="SideMarker")


def curtain_trailer(c):
    xf, xr, z0, z1, half = 5.30, -8.30, 1.20, 4.00, 1.275
    c.prism([(xf, z0), (xr, z0), (xr, z1), (xf, z1)], 'y', -half, half, 'white', name="Curtain")
    # sangles de bâche, lisses haute et basse, montants, portes arrière
    n = 24
    c.box([ybox(xf - 0.25 - k * (xf - xr - 0.5) / (n - 1) + 0.02, xf - 0.25 - k * (xf - xr - 0.5) / (n - 1) - 0.02,
                half, half + 0.012, z0 + 0.14, z1 - 0.12) for k in range(n)], 'grey', mirror=True, name="Straps")
    c.box([ybox(xf, xr, half - 0.02, half + 0.016, z1 - 0.12, z1), ybox(xf, xr, half - 0.02, half + 0.016, z0, z0 + 0.16),
           ybox(xr + 0.10, xr - 0.012, half - 0.10, half + 0.016, z0, z1),
           ybox(xf + 0.012, xf - 0.10, half - 0.10, half + 0.016, z0, z1)], 'grey', mirror=True, name="Rails")
    c.box([ybox(xr + 0.005, xr - 0.015, -0.012, 0.012, z0 + 0.12, z1 - 0.10)], 'dark', name="DoorGap")
    c.box([ybox(xr - 0.005, xr - 0.035, y - 0.012, y + 0.012, z0 + 0.14, z1 - 0.12) for y in (0.30, 0.90)],
          'grey', mirror=True, name="DoorBars")
    c.box([ybox(xr - 0.005, xr - 0.02, half - 0.06, half - 0.02, z0 + 0.2, z1 - 0.2),
           ybox(xr - 0.005, xr - 0.02, 0.05, half - 0.06, z1 - 0.10, z1 - 0.06)], 'red', mirror=True,
          name="ContourTape")
    semi_trailer_frame(c, xf, xr, (-2.84, -4.15, -5.46), 0.535)


def tank_trailer(c):
    """Semi-citerne aluminium : réservoir elliptique à fonds bombés, passerelle, échelle, coffres de vannes."""
    xf, xr = 5.15, -8.15
    zc, ry, rz = 2.52, 1.22, 1.05
    ell = ellipse(0.0, zc, ry, rz, n=16)
    c.prism(ell, 'x', xr + 0.22, xf - 0.22, 'chrome', name="Tank")
    small = ellipse(0.0, zc, ry * 0.80, rz * 0.80, n=16)
    c.prism(small, 'x', xf - 0.22, xf, 'chrome', name="TankEnd")
    c.prism(small, 'x', xr, xr + 0.22, 'chrome', name="TankEnd")
    for x in (3.2, 0.6, -2.0, -4.6, -7.2):
        c.prism(ellipse(0.0, zc, ry + 0.012, rz + 0.012, n=16), 'x', x - 0.05, x + 0.05, 'grey', name="TankRing")
    c.box([ybox(xf - 0.6, xr + 0.6, -0.26, 0.26, zc + rz - 0.02, zc + rz + 0.04)], 'grey', name="Catwalk")
    c.box([ybox(x + 0.015, x - 0.015, 0.26, 0.29, zc + rz + 0.04, zc + rz + 0.40) for x in (4.2, 1.5, -1.2, -3.9,
                                                                                            -6.6)] +
          [ybox(xf - 0.9, xr + 0.9, 0.26, 0.29, zc + rz + 0.36, zc + rz + 0.40)], 'grey', mirror=True, name="Railing")
    c.box([ybox(xr - 0.02, xr - 0.08, 0.30, 0.34, 1.20, zc + rz), ybox(xr - 0.02, xr - 0.08, 0.60, 0.64, 1.20, zc + rz)] +
          [ybox(xr - 0.02, xr - 0.08, 0.30, 0.64, z, z + 0.03) for z in (1.5, 1.9, 2.3, 2.7, 3.1, 3.4)], 'grey',
          name="Ladder")
    c.box([ybox(0.40, -1.90, 0.70, 1.20, 1.02, 1.50)], 'grey', mirror=True, name="ValveBox")
    c.box([ybox(xf - 0.15, xr + 0.15, -0.55, 0.55, 1.20, 1.50)], 'dark', name="Saddle")
    c.box([ybox(xr - 0.005, xr - 0.02, -0.9, 0.9, 1.30, 1.40)], 'red', name="ContourTape")
    semi_trailer_frame(c, xf, xr, (-2.84, -4.15, -5.46), 0.535)


SEMI_COMMON = dict(
    front=8.3, rear=6.05, height=4.0, axles=(6.85, 3.15, -2.84, -4.15, -5.46),
    wheel=dict(rt=0.51, rr=0.29, w=0.30, y=1.08, style="truck"),
    axle_wheels=[{}, {"twin": True}, {"rt": 0.535, "w": 0.385, "y": 1.04}, {"rt": 0.535, "w": 0.385, "y": 1.04},
                 {"rt": 0.535, "w": 0.385, "y": 1.04}],
    arch_axles=[0], arch_gap=0.06, arch_n=6,
    inset=dict(fl=0.05, lo=0.02, rk=0.01, sh=0.005, rail=0.18),
    crown=0.8, z=TRACTOR_Z, w=TRACTOR_W,
    glass=dict(cowl=99.0, ws_top=99.0, side=[(7.85, 6.95)], pillars=[]),
)
reg(dict(SEMI_COMMON, id="semi", file="SemiTrailer", name="Semi-remorque", color=(0.10, 0.22, 0.48),
         details=lambda c: (tractor(c), curtain_trailer(c))))
reg(dict(SEMI_COMMON, id="tanker", file="Tanker", name="Semi-citerne", color=(0.85, 0.85, 0.86),
         details=lambda c: (tractor(c), tank_trailer(c))))

# ================================================================= fourgonnette (type Renault Kangoo Express / Berlingo)
def kangoo_details(c):
    c.decal([(0.0, 0.64), (0.40, 0.64), (0.44, 0.76), (0.0, 0.77)], 'front', 'dark', name="Grille")
    c.decal([(0.44, 0.66), (0.50, 0.80), (0.80, 0.84), (0.86, 0.76), (0.80, 0.66)], 'front', 'lens', name="Headlight")
    c.decal(rect(0.53, 0.765, 0.78, 0.795), 'front', 'led', offset=0.009, name="DRL")
    c.decal([(0.0, 0.30), (0.88, 0.30), (0.90, 0.60), (0.0, 0.60)], 'front', 'trim', name="Bumper")
    c.decal([(0.0, 0.38), (0.46, 0.38), (0.44, 0.50), (0.0, 0.50)], 'front', 'dark', offset=0.009, name="Intake")
    c.decal(rect(0.0, 0.42, 0.23, 0.535), 'front', 'plate', offset=0.013, name="Plate")
    # arrière : portes battantes vitrées, feux verticaux, pare-chocs
    c.decal(rect(0.74, 0.62, 0.86, 1.10), 'rear', 'tail', name="Tail")
    c.decal(rect(0.06, 1.18, 0.68, 1.62), 'rear', 'glass', name="RearGlass")
    c.decal(rect(0.0, 0.46, 0.010, 1.72), 'rear', 'dark', offset=0.007, name="DoorGap")
    c.decal(rect(0.0, 0.30, 0.88, 0.50), 'rear', 'trim', name="RearBumper")
    c.decal(rect(0.0, 0.62, 0.23, 0.735), 'rear', 'plate', offset=0.012, name="Plate")
    c.decal(rect(0.0, 1.74, 0.12, 1.77), 'rear', 'tail', offset=0.008, name="Brake")
    c.decal([(1.30, 0.50), (-1.95, 0.50), (-1.95, 0.56), (1.30, 0.56)], 'side', 'trim', offset=0.004, name="SideStrip")
    c.decal([(0.00, 1.10), (-1.25, 1.10), (-1.25, 1.12), (0.00, 1.12)], 'side', 'dark', offset=0.004, name="Rail")
    c.decal(rect(-0.02, 0.40, 0.00, 1.60), 'side', 'dark', offset=0.004, name="DoorGap")
    C_mirrors(c, 1.15, 0.99, 1.13)


def C_mirrors(c, x, y, z, size=(0.13, 0.17, 0.12)):
    c.box([((x, y, z), size, None)], 'trim', mirror=True, name="Mirror")
    c.box([((x + 0.05, y - size[1] / 2 - 0.02, z - 0.045), (0.08, 0.08, 0.04), None)], 'trim', mirror=True,
          name="MirrorArm")


reg(dict(
    id="kangoo", file="Kangoo", name="Fourgonnette", color=(0.92, 0.92, 0.90),
    front=2.18, rear=-2.03, height=1.805, axles=(1.35, -1.35),
    wheel=dict(rt=0.316, rr=0.19, w=0.195, y=0.78, style="steel"),
    arch_gap=0.05,
    bands=[(2, 2.18, -2.03, "trim")],
    inset=dict(rail=0.10, sh=0.02),
    z=dict(
        bot=[(2.18, .30), (2.08, .22), (1.95, .18), (-1.85, .18), (-1.98, .24), (-2.03, .30)],
        lo=[(2.18, .33), (2.08, .26), (1.95, .225), (-1.85, .225), (-1.98, .28), (-2.03, .33)],
        rk=[(2.18, .44), (2.08, .38), (1.95, .34), (-1.85, .34), (-1.98, .40), (-2.03, .45)],
        mx=[(2.18, .58), (2.10, .62), (1.95, .66), (1.35, .69), (-1.35, .70), (-2.03, .70)],
        sh=[(2.18, .72), (2.10, .80), (1.95, .86), (1.20, .98), (-1.90, 1.02), (-2.03, 1.00)],
        belt=[(2.18, .76), (2.10, .85), (1.95, .91), (1.20, 1.03), (-1.90, 1.07), (-2.03, 1.05)],
        re=[(2.18, .77), (2.10, .87), (1.95, .93), (1.20, 1.06), (0.35, 1.68), (0.10, 1.72), (-1.95, 1.74),
            (-2.03, 1.72)],
        top=[(2.18, .78), (2.10, .88), (1.95, .95), (1.20, 1.08), (0.35, 1.74), (0.10, 1.79), (-1.95, 1.805),
             (-2.03, 1.78)],
    ),
    w=dict(
        W=[(2.18, .74), (2.10, .83), (1.95, .885), (1.35, .915), (-1.35, .915), (-1.95, .905), (-2.03, .87)],
        belt=[(2.18, .64), (2.10, .74), (1.95, .80), (1.20, .86), (-1.90, .87), (-2.03, .84)],
        re=[(2.18, .55), (2.10, .63), (1.95, .68), (1.20, .76), (0.35, .78), (-1.90, .80), (-2.03, .78)],
    ),
    glass=dict(cowl=1.20, ws_top=0.35, side=[(1.20, 0.02)], pillars=[]),
    details=kangoo_details,
))


# ================================================================= fourgon grand volume à caisse (type Renault Master « Luton »)
def luton_box(c, color='white'):
    xf, xr, half = 1.02, -3.45, 1.12
    poly = [(xr, 0.98), (xf, 0.98), (xf, 2.33), (1.62, 2.36), (1.88, 2.46), (1.95, 2.85), (1.80, 3.28), (xr, 3.30)]
    c.prism(poly, 'y', -half, half, color, name="Box")
    c.box([ybox(1.75, xr, half - 0.03, half + 0.012, 3.20, 3.30), ybox(xf, xr, half - 0.03, half + 0.012, 0.98, 1.10),
           ybox(xr + 0.08, xr - 0.012, half - 0.10, half + 0.012, 0.98, 3.30)], 'grey', mirror=True, name="BoxRails")
    # porte arrière à rideau (lames), feux de gabarit
    c.box([ybox(xr + 0.005, xr - 0.012, -0.98, 0.98, z, z + 0.012) for z in [1.15 + 0.14 * k for k in range(15)]],
          'grey', name="Shutter")
    c.box([ybox(xr + 0.005, xr - 0.02, half - 0.12, half - 0.04, 3.18, 3.24)], 'tail', mirror=True, name="TopMarker")
    c.box([ybox(x + 0.04, x - 0.04, half, half + 0.018, 1.00, 1.08) for x in (0.2, -1.5, -3.2)], 'amber', mirror=True,
          name="SideMarker")


def boxvan_details(c):
    c.decal([(0.0, 0.70), (0.46, 0.70), (0.50, 0.92), (0.0, 0.94)], 'front', 'dark', name="Grille")
    for z in (0.76, 0.84):
        c.decal(rect(0.0, z, 0.44, z + 0.025), 'front', 'chrome', offset=0.009, name="GrilleBar")
    c.decal([(0.50, 0.74), (0.54, 0.95), (0.86, 1.00), (0.94, 0.88), (0.86, 0.74)], 'front', 'lens', name="Headlight")
    c.decal(rect(0.60, 0.90, 0.84, 0.93), 'front', 'led', offset=0.009, name="DRL")
    c.decal([(0.0, 0.30), (0.98, 0.30), (1.00, 0.66), (0.0, 0.66)], 'front', 'trim', name="Bumper")
    c.decal(rect(0.0, 0.40, 0.23, 0.515), 'front', 'plate', offset=0.013, name="Plate")
    C_mirrors(c, 2.35, 1.12, 1.32, size=(0.10, 0.14, 0.26))
    c.decal(rect(2.12, 1.26, 2.30, 1.30), 'side', 'dark', offset=0.006, name="Handle")
    chassis(c, 2.2, -3.30, z0=0.62, z1=0.80, y=0.42)
    c.box([ybox(1.0, -3.40, -0.55, 0.55, 0.80, 0.98)], 'dark', name="Subframe")
    luton_box(c)
    mudguard(c, -1.23, 0.345, 0.66, 1.10, a0=0, a1=180)
    rear_bar(c, -3.45, 1.05, z=0.45, lights_z=0.80)


reg(dict(
    id="boxvan", file="BoxVan", name="Fourgon grand volume", color=(0.92, 0.92, 0.90),
    front=3.45, rear=1.05, height=3.30, axles=(2.45, -1.23),
    wheel=dict(rt=0.345, rr=0.205, w=0.215, y=0.83, style="steel"),
    arch_axles=[0], arch_gap=0.05,
    bands=[(2, 3.45, 1.05, "trim")],
    inset=dict(rail=0.12, sh=0.02),
    z=dict(
        bot=[(3.45, .38), (3.35, .30), (3.20, .26), (1.05, .30)],
        lo=[(3.45, .42), (3.35, .34), (3.20, .30), (1.05, .34)],
        rk=[(3.45, .55), (3.35, .50), (3.20, .46), (1.05, .50)],
        mx=[(3.45, .72), (3.30, .80), (3.10, .84), (1.05, .88)],
        sh=[(3.45, .92), (3.35, 1.00), (3.10, 1.06), (2.55, 1.14), (1.05, 1.20)],
        belt=[(3.45, .96), (3.35, 1.05), (3.10, 1.12), (2.55, 1.22), (1.05, 1.27)],
        re=[(3.45, .97), (3.35, 1.07), (3.10, 1.14), (2.55, 1.25), (1.85, 2.18), (1.60, 2.25), (1.05, 2.25)],
        top=[(3.45, .98), (3.35, 1.08), (3.10, 1.16), (2.55, 1.27), (1.85, 2.26), (1.60, 2.32), (1.05, 2.32)],
    ),
    w=dict(
        W=[(3.45, .85), (3.35, .95), (3.20, 1.00), (2.45, 1.035), (1.05, 1.035)],
        belt=[(3.45, .75), (3.35, .87), (3.20, .93), (2.55, .99), (1.05, .99)],
        re=[(3.45, .66), (3.35, .76), (3.20, .82), (2.55, .90), (1.85, .92), (1.05, .93)],
    ),
    glass=dict(cowl=2.55, ws_top=1.85, side=[(2.50, 1.25)], pillars=[]),
    details=boxvan_details,
))


def ambulance_extras(c):
    for x0, x1 in ((1.0, -3.43),):
        c.box([ybox(x0, x1, 1.12, 1.126, 1.62, 1.80)], 'red', mirror=True, name="RedBand")
        c.box([ybox(x0, x1, 1.12, 1.126, 1.44, 1.62)], 'yellow', mirror=True, name="YellowBand")
    c.box([ybox(-3.451, -3.46, -1.0, 1.0, 1.44, 1.62)], 'yellow', name="RearBand")
    c.box([ybox(-3.451, -3.46, -1.0, 1.0, 1.62, 1.80)], 'red', name="RearBand")
    c.box([ybox(1.90, 1.70, 0.80, 1.05, 3.28, 3.40), ybox(-3.20, -3.40, 0.85, 1.08, 3.30, 3.42)], 'blue', mirror=True,
          name="Beacon")
    c.box([ybox(1.70, 1.40, -0.60, 0.60, 2.32, 2.40)], 'trim', name="BarBase")
    c.box([ybox(1.66, 1.44, 0.10, 0.56, 2.40, 2.48)], 'blue', mirror=True, name="LightBar")


reg(dict(id="ambulance", base="boxvan", file="Ambulance", name="Ambulance", paint_as="white",
         color=(0.95, 0.95, 0.93), extras=ambulance_extras))

# ================================================================= moto roadster (type Yamaha MT-07) + pilote
def moto_details(c):
    bike_wheel(c, 0.70, 0.300, 0.12, 0.216, "wheel-front")
    bike_wheel(c, -0.70, 0.315, 0.18, 0.216, "wheel-back", disc=False)
    # fourche, té, phare, guidon, rétroviseurs
    tube(c, [((0.70, 0.085, 0.30), (0.56, 0.085, 0.62), 0.036), ((0.56, 0.085, 0.62), (0.42, 0.085, 0.93), 0.027)],
         'grey', mirror=True, name="Fork")
    c.box([ybox(0.46, 0.38, -0.13, 0.13, 0.92, 0.97)], 'grey', name="TripleClamp")
    blob(c, [((0.53, 0.0, 0.86), (0.07, 0.095, 0.08))], 'dark', name="HeadlightBody")
    blob(c, [((0.585, 0.0, 0.86), (0.03, 0.075, 0.06))], 'led', name="Headlight")
    tube(c, [((0.37, -0.36, 1.03), (0.37, 0.36, 1.03), 0.013)], 'trim', name="Handlebar")
    tube(c, [((0.37, 0.30, 1.03), (0.37, 0.38, 1.03), 0.02)], 'black', mirror=True, name="Grip")
    tube(c, [((0.34, 0.26, 1.05), (0.31, 0.31, 1.19), 0.008)], 'trim', mirror=True, name="MirrorStalk")
    blob(c, [((0.31, 0.32, 1.21), (0.02, 0.06, 0.04))], 'black', mirror=True, name="Mirror")
    c.prism(arc_poly(0.70, 0.30, 0.345, 25, 150, 6, 0.325), 'y', -0.07, 0.07, 'paint', name="FrontFender")
    # réservoir, écopes, selle, coque arrière, feu, support de plaque
    blob(c, [((0.13, 0.0, 0.92), (0.27, 0.17, 0.12))], 'paint', name="Tank")
    c.box([limb((0.36, 0.15, 0.86), (0.16, 0.17, 0.70), 0.05, 0.16)], 'paint', mirror=True, name="Scoop")
    c.prism([(-0.04, 0.82), (-0.62, 0.845), (-0.67, 0.90), (-0.42, 0.895), (-0.08, 0.875)], 'y', -0.14, 0.14, 'dark',
            name="Seat")
    c.prism([(-0.52, 0.78), (-0.96, 0.90), (-1.00, 0.95), (-0.66, 0.935), (-0.50, 0.84)], 'y', -0.09, 0.09, 'paint',
            name="Tail")
    c.box([ybox(-0.97, -1.01, -0.05, 0.05, 0.915, 0.945)], 'tail', name="TailLight")
    c.box([limb((-0.80, 0.0, 0.83), (-0.95, 0.0, 0.66), 0.05, 0.02)], 'trim', name="PlateArm")
    c.box([ybox(-0.95, -0.965, -0.105, 0.105, 0.57, 0.70)], 'plate', name="Plate")
    c.box([ybox(-0.93, -0.96, 0.08, 0.12, 0.72, 0.75)], 'amber', mirror=True, name="Indicator")
    # moteur, radiateur, échappement, cadre, bras oscillant, chaîne, repose-pieds
    c.box([ybox(0.22, -0.18, -0.15, 0.15, 0.34, 0.66)], 'grey', name="Engine")
    c.box([ybox(0.26, -0.02, -0.13, 0.13, 0.62, 0.76)], 'dark', name="Cylinders")
    c.box([limb((0.33, 0.0, 0.52), (0.29, 0.0, 0.80), 0.34, 0.05)], 'dark', name="Radiator")
    tube(c, [((0.24, 0.05, 0.58), (0.28, 0.02, 0.36), 0.022), ((0.28, 0.02, 0.36), (-0.02, 0.0, 0.25), 0.022)],
         'dark', name="Header")
    c.box([ybox(0.02, -0.34, -0.10, 0.16, 0.20, 0.34)], 'dark', name="Muffler")
    tube(c, [((0.42, 0.10, 0.90), (-0.22, 0.10, 0.52), 0.022)], 'trim', mirror=True, name="Frame")
    c.box([limb((-0.22, 0.11, 0.48), (-0.70, 0.11, 0.315), 0.045, 0.07)], 'grey', mirror=True, name="Swingarm")
    c.box([limb((-0.10, 0.13, 0.44), (-0.70, 0.13, 0.315), 0.012, 0.03)], 'dark', name="Chain")
    tube(c, [((-0.28, 0.0, 0.56), (-0.40, 0.0, 0.80), 0.025)], 'amber', name="Shock")
    c.box([ybox(-0.16, -0.24, 0.14, 0.22, 0.39, 0.42)], 'trim', mirror=True, name="Peg")
    rider(c, hip=(-0.36, 0, 0.93), shoulder=(-0.07, 0, 1.36), head=(0.05, 0, 1.55), hand=(0.34, 0.30, 1.06),
          elbow=(0.12, 0.30, 1.15), knee=(0.08, 0.20, 0.84), ankle=(-0.16, 0.19, 0.47), foot=(-0.18, 0.19, 0.42),
          jacket="black", pants="dark", helmet="white")


reg(dict(
    id="moto", file="Motorcycle", name="Moto roadster", color=(0.12, 0.25, 0.60),
    front=1.04, rear=-1.02, height=1.75, axles=(0.70, -0.70),
    details=moto_details,
))


# ================================================================= scooter (type Vespa / PCX) + pilote
def scooter_details(c):
    bike_wheel(c, 0.66, 0.25, 0.11, 0.165, "wheel-front")
    bike_wheel(c, -0.69, 0.26, 0.13, 0.165, "wheel-back", disc=False)
    tube(c, [((0.66, 0.06, 0.25), (0.52, 0.06, 0.62), 0.028)], 'grey', name="Fork")
    c.prism(arc_poly(0.66, 0.25, 0.30, 20, 160, 6, 0.28), 'y', -0.075, 0.075, 'paint', name="FrontFender")
    # tablier, cache-guidon avec phare, plancher, carrosserie arrière, selle
    c.prism([(0.56, 0.40), (0.63, 0.64), (0.60, 0.94), (0.50, 1.00), (0.44, 0.96), (0.47, 0.40)], 'y', -0.21, 0.21,
            'paint', name="LegShield")
    blob(c, [((0.47, 0.0, 1.03), (0.10, 0.30, 0.065))], 'paint', name="HeadsetCover")
    blob(c, [((0.555, 0.0, 1.02), (0.025, 0.09, 0.04))], 'led', name="Headlight")
    c.box([ybox(0.62, 0.60, 0.13, 0.17, 0.88, 0.92)], 'amber', mirror=True, name="Indicator")
    tube(c, [((0.46, 0.28, 1.03), (0.46, 0.36, 1.03), 0.02)], 'black', mirror=True, name="Grip")
    tube(c, [((0.44, 0.24, 1.07), (0.42, 0.29, 1.20), 0.008)], 'trim', mirror=True, name="MirrorStalk")
    blob(c, [((0.42, 0.30, 1.22), (0.02, 0.055, 0.04))], 'black', mirror=True, name="Mirror")
    c.box([ybox(0.46, -0.12, -0.19, 0.19, 0.28, 0.35)], 'dark', name="Floor")
    c.prism([(-0.05, 0.34), (-0.10, 0.62), (-0.40, 0.79), (-0.82, 0.79), (-0.96, 0.70), (-0.93, 0.48), (-0.70, 0.33)],
            'y', -0.21, 0.21, 'paint', name="RearBody")
    c.prism([(-0.10, 0.78), (-0.80, 0.79), (-0.85, 0.85), (-0.60, 0.89), (-0.20, 0.86)], 'y', -0.16, 0.16, 'dark',
            name="Seat")
    c.box([ybox(-0.93, -0.97, -0.08, 0.08, 0.66, 0.73)], 'tail', name="TailLight")
    c.box([ybox(-0.92, -0.935, -0.10, 0.10, 0.44, 0.57)], 'plate', name="Plate")
    c.box([ybox(-0.30, -0.72, -0.12, 0.12, 0.18, 0.34)], 'grey', name="Engine")
    tube(c, [((-0.55, -0.17, 0.30), (-1.00, -0.17, 0.38), 0.05)], 'chrome', name="Exhaust")
    rider(c, hip=(-0.40, 0, 0.90), shoulder=(-0.31, 0, 1.44), head=(-0.27, 0, 1.65), hand=(0.44, 0.30, 1.04),
          elbow=(0.08, 0.30, 1.17), knee=(0.06, 0.19, 0.86), ankle=(0.12, 0.16, 0.44), foot=(0.14, 0.16, 0.38),
          jacket="grey", pants="blue", helmet="black")


reg(dict(
    id="scooter", file="Scooter", name="Scooter", color=(0.62, 0.07, 0.06),
    front=0.95, rear=-1.02, height=1.85, axles=(0.66, -0.69),
    details=scooter_details,
))

# ================================================================= outils : révolution sur un axe quelconque, charge prête
def revolve_axis(c, p0, d, prof, color, n=16, name="Revolve"):
    """Solide de révolution autour de l'axe p0 + s·d ; prof = [(s, r)] ; extrémités fermées."""
    p0, d = Vector(p0), Vector(d).normalized()
    u = (Vector((0, 0, 1)) - d * d.z).normalized()
    v = d.cross(u)
    bm = bmesh.new()
    rings = [[bm.verts.new(p0 + d * s + (u * math.cos(2 * math.pi * k / n) + v * math.sin(2 * math.pi * k / n)) * r)
              for k in range(n)] for s, r in prof]
    for A, B in zip(rings, rings[1:]):
        for k in range(n):
            k2 = (k + 1) % n
            bm.faces.new((A[k], A[k2], B[k2], B[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return c.obj(bm, name, color)


def helix_band(c, p0, d, radius_at, s0, s1, turns, width, color, th0=0.0, n=64, name="Helix"):
    """Bande hélicoïdale posée sur un solide de révolution (rayon radius_at(s)), largeur mesurée le long de l'axe."""
    p0, d = Vector(p0), Vector(d).normalized()
    u = (Vector((0, 0, 1)) - d * d.z).normalized()
    v = d.cross(u)
    bm = bmesh.new()
    rows = []
    for i in range(n + 1):
        s = s0 + (s1 - s0) * i / n
        th = th0 + 2 * math.pi * turns * i / n
        e = u * math.cos(th) + v * math.sin(th)
        rows.append([bm.verts.new(p0 + d * ss + e * (radius_at(ss) + 0.025)) for ss in (s - width / 2, s, s + width / 2)])
    for A, B in zip(rows, rows[1:]):
        for j in range(2):
            bm.faces.new((A[j], A[j + 1], B[j + 1], B[j]))
    bm.normal_update()
    for f in bm.faces:
        cc = f.calc_center_median() - p0
        radial = cc - d * cc.dot(d)
        if f.normal.dot(radial) < 0:
            f.normal_flip()
    return c.obj(bm, name, color)


def load_vehicle(c, cid, offset):
    """Véhicule chargé (plateau) : construit à part (couleur fixe, optiques éteintes), maillages cuits recopiés."""
    P.build(cid)
    src = bpy.data.collections["PK_" + cid]
    off = Matrix.Translation(Vector(offset))
    for ob in list(src.all_objects):
        if ob.type != 'MESH':
            continue
        me = ob.data.copy()
        me.transform(off @ ob.matrix_world)
        o = bpy.data.objects.new("PK_%s_Load_%s" % (c.car["id"], ob.name), me)
        c.col.objects.link(o)
        o["prebaked"] = True
    for ob in list(src.all_objects):
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    bpy.data.collections.remove(src)


# ================================================================= bétonnière 8x4 (toupie inclinée)
MIXER_AXIS = ((2.40, 0.0, 2.02), (-math.cos(math.radians(13)), 0.0, math.sin(math.radians(13))))
MIXER_PROF = [(0.0, 0.55), (0.25, 0.85), (1.1, 1.12), (2.7, 1.15), (4.3, 0.98), (5.3, 0.55), (5.6, 0.42)]


def mixer_radius(s):
    for (s0, r0), (s1, r1) in zip(MIXER_PROF, MIXER_PROF[1:]):
        if s0 <= s <= s1:
            return r0 + (r1 - r0) * (s - s0) / (s1 - s0)
    return MIXER_PROF[-1][1]


def mixer_details(c):
    F, R = 5.0, -4.6
    cab_front(c, F, 0.55, 1.20, 1.72, 1.85, 2.78, 1.24, lamp_z=(0.78, 0.96), grille_w=0.84, bars=3)
    c.box([ybox(F + 0.12, F - 0.05, -1.12, 1.12, 2.80, 2.88)], 'trim', name="SunVisor")
    c.box([ybox(F + 0.22, F + 0.05, -1.20, 1.20, 0.55, 0.72)], 'grey', name="SteelBumper")
    truck_mirrors(c, F + 0.14, 1.44, 2.22, (F - 0.16, 1.24, 2.55))
    c.box([ybox(F - 0.30, F - 0.80, 1.14, 1.24, 0.72, 0.78), ybox(F - 0.30, F - 0.80, 1.14, 1.24, 1.02, 1.08)],
          'trim', mirror=True, name="Steps")
    c.box([ybox(2.95, 2.75, 0.95, 1.12, 3.12, 3.24)], 'amber', mirror=True, name="Beacon")
    chassis(c, 4.6, R + 0.2, z0=0.85, z1=1.15)
    c.box([ybox(2.9, R + 0.25, -0.52, 0.52, 1.15, 1.30)], 'dark', name="Subframe")
    # toupie + bande hélicoïdale, supports, trémie, goulotte, échelle
    p0, d = MIXER_AXIS
    revolve_axis(c, p0, d, MIXER_PROF, 'white', n=18, name="Drum")
    helix_band(c, p0, d, mixer_radius, 0.4, 5.0, 2.2, 0.26, 'orange', name="DrumStripe")
    c.box([ybox(2.55, 2.35, 0.30, 0.42, 1.25, 2.00), ybox(2.55, 2.35, -0.42, 0.42, 1.95, 2.10)], 'grey', mirror=True,
          name="FrontPedestal")
    c.box([ybox(-2.35, -2.60, 0.45, 0.58, 1.25, 2.55)], 'grey', mirror=True, name="RearPedestal")
    c.prism([(-3.10, 3.30), (-3.35, 3.30), (-3.62, 3.95), (-2.95, 3.95)], 'y', -0.42, 0.42, 'grey', name="Hopper")
    c.box([limb((-3.30, 0.0, 3.05), (-4.45, 0.0, 2.40), 0.46, 0.06)], 'grey', name="Chute")
    c.box([limb((-3.30, y, 3.10), (-4.45, y, 2.45), 0.03, 0.14) for y in (-0.22, 0.22)], 'grey', name="ChuteSides")
    c.box([ybox(-3.90, -3.96, 0.70, 0.74, 1.30, 3.60), ybox(-3.90, -3.96, 0.98, 1.02, 1.30, 3.60)] +
          [ybox(-3.90, -3.96, 0.70, 1.02, z, z + 0.03) for z in (1.6, 2.0, 2.4, 2.8, 3.2)], 'grey', name="Ladder")
    c.box([ybox(-3.12, -3.96, 0.46, 1.02, 3.00, 3.05), ybox(-3.12, -3.18, 0.98, 1.02, 3.05, 3.60),
           ybox(-3.12, -3.96, 0.98, 1.02, 3.56, 3.60)], 'grey', name="Platform")
    mudguard(c, 1.80, 0.53, 0.72, 1.24, a0=0, a1=180)
    mudguard(c, -1.75, 0.53, 0.72, 1.24, a0=0, a1=180)
    mudguard(c, -3.10, 0.53, 0.72, 1.24, a0=0, a1=180)
    side_guard(c, 1.15, -1.10, 1.22, zs=(0.55, 0.88))
    c.box([ybox(1.05, 0.20, 0.72, 1.06, 0.62, 1.02)], 'chrome', name="FuelTank")
    c.box([ybox(-0.10, -0.95, -0.60, -1.06, 1.30, 1.75)], 'blue', name="WaterTank")
    rear_bar(c, R, 1.20, z=0.52, lights_z=0.95)


reg(dict(
    id="mixer", file="CementMixer", name="Bétonnière", color=(0.85, 0.40, 0.08),
    front=5.0, rear=2.85, height=3.95, axles=(3.55, 1.80, -1.75, -3.10),
    wheel=dict(rt=0.53, rr=0.29, w=0.315, y=1.09, style="truck"),
    axle_wheels=[{}, {}, {"twin": True}, {"twin": True}],
    arch_axles=[0], arch_gap=0.06, arch_n=6,
    inset=dict(fl=0.05, lo=0.02, rk=0.01, sh=0.005, rail=0.16),
    crown=0.8,
    z=dict(
        bot=[(5.0, .65), (2.85, .65)],
        lo=[(5.0, .68), (2.85, .68)],
        rk=[(5.0, 1.00), (2.85, 1.10)],
        mx=[(5.0, 1.35), (2.85, 1.35)],
        sh=[(5.0, 1.55), (2.85, 1.60)],
        belt=[(5.0, 1.78), (4.85, 1.84), (2.85, 1.86)],
        re=[(5.0, 2.78), (4.85, 2.84), (2.85, 2.86)],
        top=[(5.0, 2.94), (4.85, 3.08), (4.55, 3.12), (2.85, 3.12)],
    ),
    w=dict(
        W=[(5.0, 1.16), (4.9, 1.225), (4.75, 1.245), (2.85, 1.245)],
        belt=[(5.0, 1.14), (4.9, 1.21), (4.75, 1.235), (2.85, 1.235)],
        re=[(5.0, 1.10), (4.9, 1.17), (4.75, 1.20), (2.85, 1.20)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[(4.70, 3.70)], pillars=[]),
    details=mixer_details,
))


# ================================================================= tracteur agricole (routes de campagne)
def tractor_details(c):
    # capot moteur, calandre, phares, masses avant, essieu et ailes avant
    c.prism([(2.28, 1.10), (2.33, 1.62), (2.25, 1.86), (2.05, 1.95), (0.42, 2.02), (0.42, 1.10)], 'y', -0.44, 0.44,
            'paint', name="Hood")
    c.box([ybox(2.345, 2.30, -0.36, 0.36, 1.18, 1.78)], 'dark', name="Grille")
    c.box([ybox(1.85, 0.95, 0.44, 0.452, 1.30, 1.80)], 'dark', mirror=True, name="HoodVent")
    c.box([ybox(2.36, 2.33, 0.20, 0.34, 1.64, 1.76)], 'lens', mirror=True, name="Headlight")
    c.box([ybox(2.37, 2.345, 0.22, 0.32, 1.60, 1.625)], 'led', mirror=True, name="DRL")
    c.box([ybox(2.58, 2.30, -0.40, 0.40, 0.92, 1.36)], 'dark', name="FrontWeights")
    c.box([ybox(2.30, -0.55, -0.34, 0.34, 0.70, 1.12)], 'dark', name="EngineBlock")
    c.box([ybox(1.78, 1.62, -0.58, 0.58, 0.62, 0.80)], 'dark', name="FrontAxle")
    c.prism(arc_poly(1.70, 0.707, 0.78, 15, 165, 6, 0.745), 'y', 0.53, 1.09, 'paint', mirror=True, name="FrontFender")
    # plancher, cabine vitrée (étroite entre les ailes arrière, large au-dessus), montants, toit, gyrophare
    c.box([ybox(0.45, -1.55, -0.56, 0.56, 1.30, 1.62)], 'dark', name="CabBase")
    c.prism([(0.42, 1.62), (0.42, 1.95), (-1.45, 1.95), (-1.45, 1.62)], 'y', -0.55, 0.55, 'glass', name="CabLow")
    c.prism([(0.44, 1.95), (0.40, 2.85), (-1.45, 2.85), (-1.45, 1.95)], 'y', -0.77, 0.77, 'glass', name="CabUp")
    c.box([ybox(0.46, 0.38, 0.72, 0.79, 1.95, 2.87), ybox(-1.40, -1.48, 0.72, 0.79, 1.95, 2.87),
           ybox(0.46, 0.38, 0.50, 0.57, 1.62, 1.97), ybox(-1.40, -1.48, 0.50, 0.57, 1.62, 1.97),
           ybox(0.46, -1.48, 0.50, 0.79, 1.93, 1.99)], 'dark', mirror=True, name="CabPillars")
    c.box([ybox(0.58, -1.60, -0.90, 0.90, 2.85, 3.00)], 'paint', name="Roof")
    c.box([ybox(0.60, -1.62, -0.92, 0.92, 2.84, 2.88)], 'dark', name="RoofEdge")
    c.box([ybox(0.62, 0.56, 0.55, 0.78, 2.88, 2.96)], 'lens', mirror=True, name="WorkLight")
    c.box([ybox(-0.50, -0.70, 0.45, 0.62, 3.00, 3.14)], 'amber', name="Beacon")
    tube(c, [((0.55, -0.42, 1.90), (0.55, -0.42, 3.05), 0.05)], 'dark', name="Exhaust")
    c.box([limb((0.40, 0.76, 2.62), (0.32, 1.22, 2.58), 0.04, 0.04), ((0.32, 1.25, 2.52), (0.05, 0.14, 0.26), None)],
          'dark', mirror=True, name="Mirror")
    # ailes arrière, marchepieds, attelage trois points, feux, plaque
    c.prism(arc_poly(-1.05, 0.905, 1.0, 0, 172, 9, 0.965), 'y', 0.57, 1.29, 'paint', mirror=True, name="RearFender")
    c.box([ybox(0.30, 0.10, 0.62, 0.80, z, z + 0.04) for z in (0.62, 0.95, 1.28)], 'dark', mirror=True, name="Steps")
    c.box([limb((-1.45, 0.42, 0.66), (-2.12, 0.45, 0.56), 0.07, 0.07), limb((-1.55, 0.0, 1.28), (-2.10, 0.0, 1.06),
                                                                             0.07, 0.07)], 'dark', mirror=True,
          name="Hitch")
    c.box([ybox(-1.55, -2.22, -0.08, 0.08, 0.50, 0.60)], 'dark', name="Drawbar")
    tube(c, [((-1.50, 0.0, 0.82), (-1.75, 0.0, 0.82), 0.04)], 'grey', name="PTO")
    c.box([ybox(-1.98, -2.06, 0.95, 1.12, 1.30, 1.42)], 'tail', mirror=True, name="TailLight")
    c.box([ybox(-1.98, -2.06, 1.12, 1.22, 1.30, 1.42)], 'amber', mirror=True, name="RearIndicator")
    c.box([ybox(-1.62, -1.64, 0.20, 0.52, 1.05, 1.16)], 'plate', name="Plate")


reg(dict(
    id="tractor", file="Tractor", name="Tracteur agricole", color=(0.16, 0.40, 0.14),
    front=2.58, rear=-2.22, height=3.14, axles=(1.70, -1.05),
    wheel=dict(rt=0.707, rr=0.36, w=0.54, y=0.80, style="tractor", lugs=24),
    axle_wheels=[{}, {"rt": 0.905, "rr": 0.48, "w": 0.65, "y": 0.92, "lugs": 30}],
    details=tractor_details,
))


# ================================================================= dépanneuse plateau (porte une voiture)
def master_front(c):
    c.decal([(0.0, 0.70), (0.46, 0.70), (0.50, 0.92), (0.0, 0.94)], 'front', 'dark', name="Grille")
    for z in (0.76, 0.84):
        c.decal(rect(0.0, z, 0.44, z + 0.025), 'front', 'chrome', offset=0.009, name="GrilleBar")
    c.decal([(0.50, 0.74), (0.54, 0.95), (0.86, 1.00), (0.94, 0.88), (0.86, 0.74)], 'front', 'lens', name="Headlight")
    c.decal(rect(0.60, 0.90, 0.84, 0.93), 'front', 'led', offset=0.009, name="DRL")
    c.decal([(0.0, 0.30), (0.98, 0.30), (1.00, 0.66), (0.0, 0.66)], 'front', 'trim', name="Bumper")
    c.decal(rect(0.0, 0.40, 0.23, 0.515), 'front', 'plate', offset=0.013, name="Plate")
    C_mirrors(c, 2.35, 1.12, 1.32, size=(0.10, 0.14, 0.26))
    c.decal(rect(2.12, 1.26, 2.30, 1.30), 'side', 'dark', offset=0.006, name="Handle")


def towtruck_details(c):
    master_front(c)
    chassis(c, 2.2, -3.85, z0=0.62, z1=0.84, y=0.42)
    c.box([ybox(1.00, 0.92, -1.10, 1.10, 0.95, 2.35)], 'grey', name="Headboard")
    c.box([ybox(0.98, 0.94, -1.06, 1.06, z, z + 0.05) for z in (1.35, 1.75, 2.10)], 'dark', name="HeadboardBars")
    c.box([ybox(0.90, -3.95, -1.12, 1.12, 0.96, 1.10)], 'grey', name="Deck")
    c.box([ybox(0.90, -3.95, 1.08, 1.13, 1.10, 1.16)], 'dark', mirror=True, name="DeckRail")
    c.box([ybox(0.90, -3.95, y - 0.015, y + 0.015, 1.10, 1.115) for y in (-0.7, -0.35, 0.0, 0.35, 0.7)], 'dark',
          name="DeckSlats")
    c.box([ybox(-3.94, -3.99, y - 0.19, y + 0.19, 1.04, 1.78) for y in (0.64, -0.64)], 'grey', name="Ramps")
    c.box([ybox(-3.99, -4.005, y - 0.17, y + 0.17, z, z + 0.03) for y in (0.64, -0.64) for z in (1.22, 1.42, 1.62)],
          'dark', name="RampRibs")
    c.box([ybox(0.86, 0.55, -0.30, 0.30, 1.10, 1.36)], 'dark', name="Winch")
    c.box([ybox(1.62, 1.30, -0.62, 0.62, 2.32, 2.42)], 'amber', name="LightBar")
    mudguard(c, -1.45, 0.37, 0.64, 1.10, a0=0, a1=180)
    c.box([ybox(-3.90, -3.96, 0.62, 1.00, 0.72, 0.84)], 'tail', mirror=True, name="TailLamp")
    c.box([ybox(-3.90, -3.96, 0.40, 0.60, 0.72, 0.84)], 'amber', mirror=True, name="RearIndicator")
    c.box([ybox(-3.92, -3.96, -0.26, 0.26, 0.60, 0.71)], 'plate', name="Plate")
    c.box([ybox(x + 0.04, x - 0.04, 1.12, 1.14, 0.98, 1.06) for x in (0.2, -1.6, -3.4)], 'amber', mirror=True,
          name="SideMarker")
    # voiture chargée (Golf rouge, optiques éteintes) et sangles sur les roues
    load_vehicle(c, "golf_load", (-1.72, 0.0, 1.10))
    for dx in (1.318, -1.318):
        c.box([ybox(-1.72 + dx + 0.03, -1.72 + dx - 0.03, 0.66, 0.90, 1.10 + 0.63, 1.10 + 0.66)], 'dark', mirror=True,
              name="Strap")


reg(dict(id="golf_load", base="golf", file="GolfLoad", paint_as="red", no_emissive=True, color=(0.62, 0.07, 0.06)))

reg(dict(
    id="towtruck", file="TowTruck", name="Dépanneuse plateau", color=(0.95, 0.75, 0.10),
    front=3.45, rear=1.05, height=3.0, axles=(2.45, -1.45),
    wheel=dict(rt=0.37, rr=0.22, w=0.215, y=0.84, style="steel"),
    axle_wheels=[{}, {"twin": True, "y": 0.87}],
    arch_axles=[0], arch_gap=0.05,
    bands=[(2, 3.45, 1.05, "trim")],
    inset=dict(rail=0.12, sh=0.02),
    z=P.CARS["boxvan"]["z"], w=P.CARS["boxvan"]["w"],
    glass=dict(cowl=2.55, ws_top=1.85, side=[(2.50, 1.25)], pillars=[]),
    details=towtruck_details,
))


# ================================================================= fourgon d'incendie (FPT, double cabine)
def firetruck_details(c):
    F, R = 3.95, -3.95
    cab_front(c, F, 0.45, 1.00, 1.46, 1.58, 2.52, 1.24, lamp_z=(0.66, 0.84), grille_w=0.80, bars=3)
    c.box([ybox(F + 0.10, F - 0.05, -1.10, 1.10, 2.54, 2.62)], 'trim', name="SunVisor")
    truck_mirrors(c, F + 0.14, 1.44, 1.95, (F - 0.16, 1.24, 2.30))
    c.box([ybox(F - 0.30, F - 0.80, 1.14, 1.24, 0.60, 0.66), ybox(2.35, 1.40, 1.14, 1.24, 0.60, 0.66)], 'trim',
          mirror=True, name="Steps")
    c.decal([(F - 0.1, 1.06), (0.40, 1.06), (0.40, 1.20), (F - 0.1, 1.20)], 'side', 'white', offset=0.006,
            name="Stripe")
    c.decal(rect(2.60, 0.62, 2.62, 2.60), 'side', 'dark', offset=0.004, name="DoorGap")
    c.box([ybox(3.62, 3.32, -0.78, 0.78, 2.97, 3.08)], 'blue', name="LightBar")
    c.box([ybox(3.62, 3.32, -0.12, 0.12, 2.98, 3.09)], 'white', name="LightBarMid")
    chassis(c, 3.4, R + 0.2)
    # carrosserie à coffres (rideaux), bande blanche, échelle, gyrophares, arrière
    c.prism([(0.30, 0.98), (R, 0.98), (R, 3.05), (0.30, 3.05)], 'y', -1.25, 1.25, 'red', name="Body")
    for x0, x1 in ((0.18, -1.05), (-1.18, -2.40), (-2.53, -3.80)):
        c.box([ybox(x0, x1, 1.25, 1.262, 1.32, 2.92)], 'grey', mirror=True, name="Shutter")
        c.box([ybox(x0, x1, 1.262, 1.268, z, z + 0.012) for z in [1.45 + 0.18 * k for k in range(8)]], 'dark',
              mirror=True, name="ShutterSlats")
    c.box([ybox(0.30, R, 1.25, 1.262, 1.06, 1.20)], 'white', mirror=True, name="BodyStripe")
    c.box([ybox(0.20, -3.80, 0.33, 0.37, 3.05, 3.12), ybox(0.20, -3.80, -0.37, -0.33, 3.05, 3.12)] +
          [ybox(x + 0.02, x - 0.02, -0.33, 0.33, 3.06, 3.10) for x in [0.0 - 0.34 * k for k in range(12)]], 'grey',
          name="Ladder")
    c.box([ybox(-3.72, -3.92, 1.00, 1.20, 3.05, 3.18)], 'blue', mirror=True, name="Beacon")
    c.box([ybox(R + 0.01, R - 0.012, -1.10, 1.10, 1.25, 2.90)], 'grey', name="RearShutter")
    c.box([ybox(R - 0.01, R - 0.02, -1.08, 1.08, z, z + 0.012) for z in [1.40 + 0.18 * k for k in range(8)]], 'dark',
          name="RearSlats")
    c.box([ybox(R + 0.10, R - 0.12, -1.10, 1.10, 0.62, 0.74)], 'grey', name="RearStep")
    c.box([ybox(R + 0.02, R - 0.02, 0.85, 1.15, 1.00, 1.15)], 'tail', mirror=True, name="TailLamp")
    c.box([ybox(R + 0.02, R - 0.02, 0.70, 0.84, 1.00, 1.15)], 'amber', mirror=True, name="RearIndicator")
    c.box([ybox(R - 0.02, R - 0.03, -0.26, 0.26, 0.80, 0.91)], 'plate', name="Plate")
    mudguard(c, -1.50, 0.50, 0.72, 1.24, a0=0, a1=180)
    side_guard(c, 0.10, -0.90, 1.22)


reg(dict(
    id="firetruck", file="FireTruck", name="Fourgon d'incendie", paint_as="red", color=(0.62, 0.07, 0.06),
    front=3.95, rear=0.35, height=3.18, axles=(2.60, -1.50),
    wheel=dict(rt=0.50, rr=0.29, w=0.30, y=1.09, style="truck"),
    axle_wheels=[{}, {"twin": True}],
    arch_axles=[0], arch_gap=0.06, arch_n=6,
    inset=dict(fl=0.05, lo=0.02, rk=0.01, sh=0.005, rail=0.16),
    crown=0.8,
    z=dict(
        bot=[(3.95, .55), (0.35, .55)],
        lo=[(3.95, .58), (0.35, .58)],
        rk=[(3.95, .90), (0.35, 1.00)],
        mx=[(3.95, 1.20), (0.35, 1.20)],
        sh=[(3.95, 1.40), (0.35, 1.45)],
        belt=[(3.95, 1.52), (3.80, 1.58), (0.35, 1.60)],
        re=[(3.95, 2.55), (3.80, 2.62), (0.35, 2.65)],
        top=[(3.95, 2.72), (3.80, 2.92), (3.50, 2.97), (0.35, 2.97)],
    ),
    w=dict(
        W=[(3.95, 1.15), (3.87, 1.21), (3.75, 1.245), (0.35, 1.245)],
        belt=[(3.95, 1.13), (3.80, 1.225), (0.35, 1.235)],
        re=[(3.95, 1.08), (3.80, 1.18), (0.35, 1.20)],
    ),
    glass=dict(cowl=99.0, ws_top=99.0, side=[(3.60, 2.72), (2.45, 1.30)], pillars=[]),
    details=firetruck_details,
))

UTILITY_TRUCKS = ["bus", "coach", "boxtruck", "garbage", "semi", "tanker", "mixer", "towtruck", "firetruck"]
UTILITY_CARS = ["kangoo", "boxvan", "ambulance"]
UTILITY_BIKES = ["moto", "scooter"]
UTILITY_FARM = ["tractor"]
ALL_UTILITY = UTILITY_TRUCKS + UTILITY_CARS + UTILITY_BIKES + UTILITY_FARM
