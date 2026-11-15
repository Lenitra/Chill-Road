"""Mobilier routier et bâtiments du pack d'environnement (remplacent les modèles Kenney).

Construits en mètres puis exportés à la taille des modèles remplacés (« scale » = 1 / échelle de pose du code :
lampadaires x9, panneaux x5, cônes / barrières / feux et bâtiments x7), avec les mêmes repères :
Unity +X = Blender -X, Unity +Z = Blender -Y, Z haut.
"""
import math
from mathutils import Vector, Matrix
import env_lib as E
from env_lib import reg


def lod(g, *values):
    return values[min(g.lod, len(values) - 1)]


# ----------------------------------------------------------------- lampadaires (têtes attendues par le code)
def lamp_pole(g, arms):
    """Mât sur socle ; bras cintrés vers chaque tête (y Blender, z hauteur) ; tête : capot sombre et vasque claire."""
    g.box((0, 0, 0.15), (0.34, 0.34, 0.3), "metal_dark")
    g.frustum((0, 0, 0.3), 0.1, 0.065, 5.2, 8, "metal", cap0=False)
    for y in arms:
        s = 1 if y > 0 else -1
        g.limb((0, 0, 5.35), (0, s * 0.55, 5.72), 0.05, 0.045, 6, "metal")
        g.limb((0, s * 0.55, 5.72), (0, y - s * 0.2, 5.82), 0.045, 0.04, 6, "metal")
        g.box((0, y, 5.84), (0.3, 0.62, 0.12), "metal_dark")
        g.box((0, y, 5.765), (0.22, 0.48, 0.035), "lamp")


# Lampadaire double du terre-plein : têtes à ±1,62 m (Unity z), 5,76 m ; lampadaire simple : tête vers Unity -Z
reg(dict(id="light_double", file="StreetLightDouble", group="streetLight", lods=1, scale=1 / 9,
         build=lambda g: lamp_pole(g, (1.62, -1.62))))
reg(dict(id="light_single", file="StreetLightSingle", group="streetLightSingle", lods=1, scale=1 / 9,
         build=lambda g: lamp_pole(g, (1.665,))))


# ----------------------------------------------------------------- panneaux (face vers Unity +X = Blender -X)
def sign_pole(g, top):
    g.box((0.03, 0, top / 2), (0.07, 0.07, top), "metal")


def ngon(r, n, rot):
    return [(math.cos(rot + 2 * math.pi * k / n) * r, math.sin(rot + 2 * math.pi * k / n) * r) for k in range(n)]


def stop_sign(g):
    sign_pole(g, 2.1)
    zc = 2.1
    g.prism([(y, z + zc) for y, z in ngon(0.36, 8, math.pi / 8)], -0.01, 0.012, "sign_white", axis='x')
    g.prism([(y, z + zc) for y, z in ngon(0.31, 8, math.pi / 8)], -0.022, -0.01, "sign_red", axis='x')
    g.box((-0.025, 0, zc), (0.01, 0.4, 0.11), "sign_white")                     # « STOP »


def warning_sign(g):
    sign_pole(g, 1.9)
    zc = 2.05
    g.prism([(y, z + zc) for y, z in ngon(0.44, 3, math.pi / 2)], -0.01, 0.012, "sign_red", axis='x')
    g.prism([(y, z + zc) for y, z in ngon(0.3, 3, math.pi / 2)], -0.022, -0.01, "sign_white", axis='x')
    g.box((-0.025, 0, zc + 0.03), (0.01, 0.045, 0.16), "black")                 # « ! »
    g.box((-0.025, 0, zc - 0.1), (0.01, 0.045, 0.045), "black")


reg(dict(id="stop_sign", file="StopSign", group="stopSign", lods=1, scale=1 / 5, build=stop_sign))
reg(dict(id="warning_sign", file="WarningSign", group="warningSign", lods=1, scale=1 / 5, build=warning_sign))


# ----------------------------------------------------------------- travaux
def cone(g):
    g.box((0, 0, 0.015), (0.4, 0.4, 0.03), "black")
    n = lod(g, 8, 6)
    bands = [(0.03, 0.26, "orange"), (0.26, 0.34, "sign_white"), (0.34, 0.44, "orange"), (0.44, 0.5, "sign_white"),
             (0.5, 0.63, "orange")]
    for z0, z1, col in bands:
        r0 = 0.16 - (0.16 - 0.035) * (z0 - 0.03) / 0.6
        r1 = 0.16 - (0.16 - 0.035) * (z1 - 0.03) / 0.6
        g.frustum((0, 0, z0), r0, r1, z1 - z0, n, col, cap0=False, cap1=z1 > 0.62)


def barrier(g):
    """Barrière de chantier : planche rayée rouge et blanc (le long de Unity Z = Blender Y) sur deux chevalets."""
    for y in (-0.62, 0.62):
        g.limb((0.35, y, 0.0), (0.0, y, 0.95), 0.03, 0.03, 4, "metal")
        g.limb((-0.35, y, 0.0), (0.0, y, 0.95), 0.03, 0.03, 4, "metal")
    stripes = 6
    for k in range(stripes):
        y0 = -0.8 + 1.6 * k / stripes
        g.box((0.0, y0 + 0.8 / stripes, 0.78), (0.04, 1.6 / stripes, 0.26), "sign_red" if k % 2 == 0 else "sign_white")


def works_light(g):
    """Feu de chantier : socle, mât, boîtier orange à 1,12 m (le clignotant du code s'y superpose)."""
    g.box((0, 0, 0.08), (0.5, 0.3, 0.16), "black")
    g.box((0, 0, 0.75), (0.05, 0.05, 1.3), "metal")
    g.box((0, 0, 1.12), (0.22, 0.16, 0.22), "yellow")
    g.box((-0.12, 0, 1.12), (0.03, 0.13, 0.13), "orange")
    g.box((0, 0, 1.48), (0.04, 0.5, 0.18), "sign_red")


reg(dict(id="cone", file="Cone", group="cone", lods=1, scale=1 / 7, build=cone))
reg(dict(id="works_barrier", file="WorksBarrier", group="constructionBarrier", lods=1, scale=1 / 7, build=barrier))
reg(dict(id="works_light", file="WorksLight", group="constructionLight", lods=1, scale=1 / 7, build=works_light))


# ----------------------------------------------------------------- bâtiments (façade vers Unity +Z = Blender -Y)
def gable_roof(g, x0, x1, half_w, z_eave, z_ridge, color, over=0.35, thick=0.22):
    """Toit à deux pans (faîtage le long de X) : deux dalles épaisses qui débordent des murs."""
    for s in (-1, 1):
        w = half_w + over
        slope = (z_ridge - z_eave) / half_w
        pts = [(s * w, z_eave - over * slope), (0.0, z_ridge), (0.0, z_ridge + thick), (s * w, z_eave - over * slope + thick)]
        g.prism(pts, x0 - over, x1 + over, color, axis='x')


def gable_walls(g, x0, x1, half_w, z_eave, z_ridge, color):
    for x in (x0, x1):
        g.prism([(-half_w, z_eave), (half_w, z_eave), (0.0, z_ridge)], x - 0.001, x + 0.001, color, axis='x')
    g.prism([(-half_w, z_eave), (half_w, z_eave), (0.0, z_ridge - 0.05)], x0, x1, color, axis='x')


def hip_roof(g, hx, hy, z0, h, ridge, color, over=0.4):
    """Toit à quatre pans : rectangle de base (débord compris) et faîtage de demi-longueur « ridge » le long de X."""
    bm = g.bm
    before = set(bm.faces)
    ax, ay = hx + over, hy + over
    b = [bm.verts.new(Vector(p)) for p in ((-ax, -ay, z0), (ax, -ay, z0), (ax, ay, z0), (-ax, ay, z0))]
    r0, r1 = bm.verts.new(Vector((-ridge, 0, z0 + h))), bm.verts.new(Vector((ridge, 0, z0 + h)))
    bm.faces.new((b[0], b[1], r1, r0))
    bm.faces.new((b[2], b[3], r0, r1))
    bm.faces.new((b[1], b[2], r1))
    bm.faces.new((b[3], b[0], r0))
    bm.faces.new((b[3], b[2], b[1], b[0]))
    g.paint(g.new_faces(before), color)


def window(g, x, y_face, z, w=1.0, h=1.25, shutter="shutter", face=-1):
    """Fenêtre sur une façade perpendiculaire à Y (face = -1 : façade avant) : vitre, encadrement, volets ouverts."""
    d = 0.06 * face
    g.box((x, y_face + d * 0.5, z), (w + 0.16, 0.06, h + 0.16), "wall")
    g.box((x, y_face + d, z), (w, 0.06, h), "window")
    if shutter:
        for s in (-1, 1):
            g.box((x + s * (w * 0.5 + 0.28), y_face + d, z), (0.5, 0.06, h), shutter)


def side_window(g, x_face, y, z, w=0.9, h=1.1, shutter="shutter", face=1):
    d = 0.06 * face
    g.box((x_face + d, y, z), (0.06, w, h), "window")
    if shutter:
        for s in (-1, 1):
            g.box((x_face + d, y + s * (w * 0.5 + 0.25), z), (0.06, 0.46, h), shutter)


def longere(g):
    """Longère : maison de ferme basse et longue, murs de pierre, toit de tuiles, cheminée."""
    L, W, H = 12.0, 6.8, 3.2
    g.box((0, 0, H / 2), (L, W, H), "wall_stone")
    gable_walls(g, -L / 2, L / 2, W / 2, H, 6.6, "wall_stone")
    gable_roof(g, -L / 2, L / 2, W / 2, H, 6.8, "roof_tile")
    g.box((3.6, 1.0, 6.4), (0.75, 0.75, 2.0), "wall_stone")
    g.box((-2.0, -W / 2 - 0.05, 1.05), (1.05, 0.1, 2.1), "door")
    for x in (-4.6, 0.5, 3.6):
        window(g, x, -W / 2, 1.75)
    for x in (-3.0, 2.5):
        window(g, x, W / 2, 1.75, face=1)
    for s in (-1, 1):
        side_window(g, s * L / 2, 0.0, 4.2, w=0.7, h=0.9, shutter=None, face=s)


def maison(g):
    """Maison à étage : enduit clair, toit d'ardoise à quatre pans, volets bleus."""
    L, W, H = 9.0, 7.6, 5.8
    g.box((0, 0, H / 2), (L, W, H), "wall")
    hip_roof(g, L / 2, W / 2, H, 3.0, 2.2, "roof_slate")
    g.box((-2.6, 1.2, H + 2.4), (0.7, 0.7, 2.2), "wall")
    g.box((0.0, -W / 2 - 0.05, 1.1), (1.1, 0.1, 2.2), "door")
    g.box((0.0, -W / 2 - 0.45, 2.45), (1.8, 0.9, 0.12), "roof_slate")          # auvent
    for x in (-2.8, 2.8):
        window(g, x, -W / 2, 1.7, shutter="shutter_blue")
    for x in (-2.8, 0.0, 2.8):
        window(g, x, -W / 2, 4.3, shutter="shutter_blue")
        window(g, x, W / 2, 4.3, shutter="shutter_blue", face=1)
    for s in (-1, 1):
        side_window(g, s * L / 2, -1.5, 4.3, shutter="shutter_blue", face=s)


def grange(g):
    """Grange : bardage rouge, grand portail, toit d'ardoise."""
    L, W, H = 13.0, 8.6, 4.6
    g.box((0, 0, H / 2), (L, W, H), "barn")
    gable_walls(g, -L / 2, L / 2, W / 2, H, 8.6, "barn")
    gable_roof(g, -L / 2, L / 2, W / 2, H, 8.8, "roof_slate", over=0.45)
    g.box((0.0, -W / 2 - 0.06, 1.9), (4.0, 0.12, 3.8), "door")
    for x in (-1.0, 1.0):
        g.box((x, -W / 2 - 0.13, 1.9), (0.12, 0.05, 3.7), "wall")                # traverses du portail
    g.box((0.0, -W / 2 - 0.13, 1.9), (3.9, 0.05, 0.14), "wall")
    g.box((L / 2 + 0.06, 0.0, 6.0), (0.12, 1.3, 1.2), "door")                    # porte du fenil (pignon)
    for s in (-1, 1):
        side_window(g, s * L / 2, 0.0, 2.6, w=1.4, h=0.8, shutter=None, face=s)


def maisonnette(g):
    """Petite maison de plain-pied : enduit, toit de tuiles, volets verts."""
    L, W, H = 7.4, 6.0, 3.0
    g.box((0, 0, H / 2), (L, W, H), "wall")
    gable_walls(g, -L / 2, L / 2, W / 2, H, 5.6, "wall")
    gable_roof(g, -L / 2, L / 2, W / 2, H, 5.8, "roof_tile")
    g.box((2.2, -0.9, 5.2), (0.6, 0.6, 1.6), "wall_stone")
    g.box((1.2, -W / 2 - 0.05, 1.05), (1.0, 0.1, 2.1), "door")
    window(g, -1.6, -W / 2, 1.7)
    window(g, 0.0, W / 2, 1.7, face=1)
    for s in (-1, 1):
        side_window(g, s * L / 2, 0.0, 1.7, face=s)


def hangar(g):
    """Hangar agricole ouvert : poteaux, toit de tôle, bottes de foin rondes."""
    L, W, H = 12.0, 8.0, 5.0
    for x in (-L / 2, 0.0, L / 2):
        for y in (-W / 2, W / 2):
            g.box((x, y, H / 2), (0.25, 0.25, H), "metal_dark")
    g.box((0, W / 2, H * 0.45), (L, 0.12, H * 0.9), "wood")                      # fond bardé
    gable_roof(g, -L / 2, L / 2, W / 2, H, 6.6, "roof_slate", over=0.4, thick=0.12)
    n = lod(g, 10, 7)
    for k, (x, y) in enumerate(((-4.2, 1.8), (-2.6, 1.8), (-1.0, 1.8), (-3.4, 1.8), (-4.2, 0.2), (-2.6, 0.2),
                                (2.0, 1.8), (3.6, 1.8))):
        z = 0.75 if k != 3 else 2.2
        faces = g.limb((x, y - 0.75, z), (x, y + 0.75, z), 0.75, 0.75, n, "dry", cap0=True, cap1=True)
    g.box((3.0, -1.6, 1.0), (2.6, 1.4, 2.0), "barn")                             # remorque rangée


for bid, fname, fn in (("longere", "Longere", longere), ("maison", "Maison", maison), ("grange", "Grange", grange),
                       ("maisonnette", "Maisonnette", maisonnette), ("hangar", "Hangar", hangar)):
    reg(dict(id=bid, file=fname, group="buildings", lods=1, scale=1 / 7, build=fn))

PROPS = ["light_double", "light_single", "stop_sign", "warning_sign", "cone", "works_barrier", "works_light"]
BUILDINGS = ["longere", "maison", "grange", "maisonnette", "hangar"]
