"""Pack alpin : station de ski « Les Combes » (Tools/Map/map_station.py pose ces objets).

Engins (dameuse, motoneige), neige (congères de bord de route, tas, bonhomme, igloo, luge), bâtiments du front de neige
(magasin de ski, caisses des remontées, totem d'entrée) coiffés d'une épaisse couche de neige, balisage des pistes
(panneaux de couleur, filets, portes de slalom) et deux panneaux routiers (neige, équipements spéciaux).

Repère du pack : origine au sol au centre de l'emprise, façade avant = -Y (Unity +Z), mètres.
Anti-z-fighting : la neige des toits repose 2 cm au-dessus de la couverture et déborde des rives ; les pièces plaquées
(vitres, enseignes, pictogrammes) sont à des profondeurs différentes d'au moins 2 cm."""
import math
import bmesh
from mathutils import Matrix, Vector
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac
import alp_signs as S

SNOW = "snow"


def item(iid, fname, build, cat="ski", lods=3, **kw):
    reg(dict(id=iid, file=fname, cat=cat, lods=lods, build=build, **kw))


# ----------------------------------------------------------------- outils
def face_out(g, pts, color, outward):
    """Face tournée vers « outward »."""
    f = g.face(pts, color)[0]
    f.normal_update()
    if f.normal.dot(Vector(outward)) < 0:
        f.normal_flip()
    return f


def wheel(g, c, r, w, n, color="metal_dark"):
    """Roue ou galet : cylindre d'axe X centré en c."""
    m = g.mark()
    g.frustum((0, 0, -w / 2), r, r, w, n, color, auto=False)
    g.transform(g.since(m), Matrix.Translation(Vector(c)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))


def stadium(y0, y1, z0, z1, n=6):
    """Contour (y, z) d'une chenille : deux demi-cercles reliés."""
    r, zc = (z1 - z0) / 2, (z0 + z1) / 2
    pts = []
    for k in range(n + 1):
        a = -math.pi / 2 + math.pi * k / n
        pts.append((y1 - r + math.cos(a) * r, zc + math.sin(a) * r))
    for k in range(n + 1):
        a = math.pi / 2 + math.pi * k / n
        pts.append((y0 + r + math.cos(a) * r, zc + math.sin(a) * r))
    return pts


def mound(g, length, width, height, key=0.0, nx=9, ns=7, ends=0.3, base=-0.6, wobble=0.16, c=(0.0, 0.0), rotz=0.0,
          color=SNOW):
    """Bourrelet de neige allongé le long de X : sections arrondies, extrémités fuyantes, pied enfoncé dans le sol
    (pose sur une pente). Les irrégularités dépendent de « key », pas du niveau de détail."""
    bm = g.bm
    before = set(bm.faces)
    rows = []
    for i in range(nx + 1):
        t = i / nx
        e = min(1.0, min(t, 1.0 - t) / ends) if ends > 0 else 1.0
        e = math.sin(e * math.pi / 2) ** 0.7
        hh = height * e * (1.0 + wobble * (g.h(key, t, 1) - 0.5) * 2.0)
        ww = width * (0.5 + 0.5 * e) * (1.0 + wobble * 0.7 * (g.h(key, t, 2) - 0.5) * 2.0)
        yo = width * 0.1 * (g.h(key, t, 3) - 0.5)
        x = (t - 0.5) * length
        row = [bm.verts.new((x, yo - ww / 2 * 1.04, base))]
        for k in range(ns + 1):
            a = math.pi * k / ns
            row.append(bm.verts.new((x, yo - math.cos(a) * ww / 2, max(math.sin(a), 0.0) ** 0.7 * hh)))
        row.append(bm.verts.new((x, yo + ww / 2 * 1.04, base)))
        rows.append(row)
    for ra, rb in zip(rows, rows[1:]):
        for k in range(len(ra) - 1):
            bm.faces.new((ra[k], ra[k + 1], rb[k + 1], rb[k]))
    faces = g.since(before)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    g.paint(faces, color)
    g.move(faces, (c[0], c[1], 0.0), rotz)
    return faces


def blob(g, c, r, sz=0.7, key=0.0, subdiv=2, amount=0.06, color=SNOW):
    """Boule de neige aplatie, légèrement cabossée."""
    f = g.ico(c, r, subdiv, color, scale=(1.0, 1.0, sz), auto=False)
    for v in {v for fc in f for v in fc.verts}:
        d = Vector((g.h(key, v.co.x, v.co.y, 1) - 0.5, g.h(key, v.co.y, v.co.z, 2) - 0.5, g.h(key, v.co.z, v.co.x, 3) - 0.5))
        v.co += d * 2.0 * amount * r
    return f


def roof_snow(g, span, length, z_eave, pitch, over_e=0.8, over_g=1.0, thick=0.24, depth=0.4, ridge='y', center=(0, 0)):
    """Couche de neige d'un toit à deux pans (mêmes cotes que kit.roof_gable) : épaisse, arrondie à l'égout où elle
    déborde un peu, en retrait des rives de pignon."""
    half = span / 2
    th = math.radians(pitch)
    slope = math.tan(th)
    zr = z_eave + half * slope
    e = half + over_e
    ze = z_eave - over_e * slope
    a = length / 2 + over_g - 0.06
    ls = e / math.cos(th)
    m = g.mark()
    for s in (-1, 1):
        def P(u, v, s=s):
            return (s * (e - math.cos(th) * u + math.sin(th) * v * 1.0), ze + thick + math.sin(th) * u + math.cos(th) * v)
        prof = [P(-0.10, 0.02), P(-0.20, 0.14), P(-0.16, depth * 0.7), P(0.02, depth * 0.95), P(0.45, depth)]
        if g.lod == 2:
            prof = [P(-0.12, 0.02), P(-0.12, depth * 0.8), P(0.3, depth)]
        top = (0.0, zr + thick + depth / math.cos(th))
        g.prism(prof + [top, (0.0, zr + thick - 0.05), P(ls * 0.5, 0.02)], -a, a, SNOW, axis=ridge)
    g.move(g.since(m), (center[0], center[1], 0))


def flat_snow(g, c, size, depth=0.35, key=0.0):
    """Neige sur une surface plane (toit plat, auvent) : dalle aux bords arrondis, posée 2 cm au-dessus."""
    x, y, z = c
    w, d = size
    n = 4 if g.lod < 2 else 2
    pts = [(-w / 2, 0.0)]
    for k in range(n + 1):
        a = math.pi / 2 * k / n
        pts.append((-w / 2 + 0.18 - math.cos(a) * 0.22, 0.02 + math.sin(a) * (depth - 0.02)))
    for k in range(n + 1):
        a = math.pi / 2 * (1 - k / n)
        pts.append((w / 2 - 0.18 + math.cos(a) * 0.22, 0.02 + math.sin(a) * (depth - 0.02)))
    pts.append((w / 2, 0.0))
    f = g.prism([(px, pz + 0.02) for px, pz in pts], -d / 2 - 0.05, d / 2 + 0.05, SNOW, axis='y')
    g.move(f, (x, y, z))
    return f


# ================================================================= engins
def snow_groomer(g):
    """Dameuse : deux larges chenilles, cabine vitrée, lame avant, fraise et tapis de lissage à l'arrière."""
    y0 = -0.15
    for sx in (-1, 1):
        xa, xb = sx * 0.78, sx * 1.78
        f = g.prism(stadium(-2.25, 2.25, 0.0, 0.92, lod(g, 7, 5, 3)), min(xa, xb), max(xa, xb), "rubber", axis='x')
        g.move(f, (0, y0, 0))
        if g.lod < 2:
            for k in range(5):                                           # galets
                wheel(g, (sx * 1.80, y0 - 1.72 + k * 0.86, 0.46), 0.34, 0.06, lod(g, 12, 8), "metal_dark")
                if g.hd:
                    wheel(g, (sx * 1.835, y0 - 1.72 + k * 0.86, 0.46), 0.14, 0.03, 8, "yellow")
        if g.hd:
            for k in range(13):                                          # barrettes de la chenille
                g.box((sx * 1.28, y0 - 1.68 + k * 0.28, 0.935), (1.04, 0.07, 0.05), "metal")
    g.box((0, y0 + 0.1, 0.72), (1.6, 4.0, 0.5), "metal_dark")                                  # châssis
    g.box((0, y0 + 1.15, 1.45), (2.5, 2.1, 1.0), "red")                                        # capot moteur
    if g.lod < 2:
        g.box((0, y0 + 1.15, 1.97), (2.2, 1.8, 0.06), "metal_dark")                            # plateau
        for sx in (-1, 1):
            g.box((sx * 1.26, y0 + 1.15, 1.5), (0.03, 1.5, 0.5), "black")                      # grilles
    # cabine : pare-brise incliné
    prof = [(-1.95, 0.98), (-0.05, 0.98), (-0.05, 2.78), (-1.3, 2.78), (-2.0, 1.78)]
    f = g.prism(prof, -1.2, 1.2, "red", axis='x')
    g.move(f, (0, y0, 0))
    n = Vector((0.0, -1.0, 0.7)).normalized()
    a, b = Vector((0, -2.0 + y0, 1.78)), Vector((0, -1.3 + y0, 2.78))

    def W(t, x):
        p = a + (b - a) * t + n * 0.03
        return (x, p.y, p.z)
    face_out(g, [W(0.06, -1.08), W(0.06, 1.08), W(0.94, 1.08), W(0.94, -1.08)], "glass_blue", n)
    for sx in (-1, 1):
        g.quad((sx * 1.23, y0 - 0.85, 2.2), 1.45, 0.82, '+x' if sx > 0 else '-x', "glass_blue")
    g.quad((0, y0 - 0.02, 2.25), 1.9, 0.7, '+y', "glass_blue")
    g.box((0, y0 - 0.72, 2.84), (2.5, 1.5, 0.12), "metal_dark")                                # casquette de toit
    if g.lod < 2:
        for x in (-0.9, -0.3, 0.3, 0.9):
            g.box((x, y0 - 1.5, 2.86), (0.26, 0.14, 0.18), "lamp")                             # rampe de phares
        g.frustum((0.8, y0 - 0.3, 2.9), 0.1, 0.08, 0.2, 8, "orange", auto=False)               # gyrophare
        for sx in (-1, 1):
            g.box((sx * 0.75, y0 - 2.0, 1.3), (0.34, 0.06, 0.2), "lamp")
            g.tube([(sx * 1.2, y0 - 1.75, 2.0), (sx * 1.65, y0 - 1.9, 2.1)], [0.025, 0.025], 5, "metal_dark")
            g.box((sx * 1.68, y0 - 1.93, 2.2), (0.06, 0.2, 0.42), "metal_dark")                # rétroviseurs
        g.tube([(0.95, y0 + 0.3, 1.95), (0.95, y0 + 0.3, 3.0), (0.95, y0 + 0.4, 3.12)], [0.06, 0.06, 0.06], 8, "metal")
    # lame avant : tôle galbée, ailes latérales, bras de poussée
    blade = [(-3.05, 0.04), (-3.3, 0.16), (-3.42, 0.5), (-3.36, 0.85), (-3.2, 1.08), (-3.12, 1.06), (-3.26, 0.82),
             (-3.31, 0.5), (-3.21, 0.24), (-3.05, 0.14)]
    f = g.prism(blade, -2.35, 2.35, "metal", axis='x')
    g.move(f, (0, y0, 0))
    if g.lod < 2:
        for sx in (-1, 1):
            g.box((sx * 2.45, y0 - 3.38, 0.56), (0.5, 0.07, 1.0), "red", rotz=sx * 0.5)
            g.beam((sx * 0.6, y0 - 1.9, 0.7), (sx * 0.6, y0 - 3.15, 0.5), 0.14, "metal_dark")
        g.box((0, y0 - 3.18, 1.1), (4.7, 0.1, 0.1), "red")
    # fraise et tapis de lissage
    g.box((0, y0 + 2.85, 0.42), (4.5, 0.75, 0.5), "metal_dark")
    g.box((0, y0 + 3.45, 0.1), (4.5, 0.6, 0.07), "yellow")
    if g.lod < 2:
        for sx in (-1, 1):
            g.beam((sx * 0.6, y0 + 2.1, 0.8), (sx * 0.6, y0 + 2.7, 0.6), 0.12, "metal_dark")
        for k in range(9):
            g.box((-2.0 + k * 0.5, y0 + 3.47, 0.15), (0.06, 0.56, 0.05), "black")


item("snow_groomer", "Snow_Groomer", snow_groomer, tags=["piste", "vehicle"], seed=560, fp=(4.9, 7.2),
     collider="box", note="dameuse ; avant = Unity +Z")


def snowmobile(g):
    """Motoneige : deux skis à l'avant, chenille à l'arrière, selle, bulle."""
    g.box((0, 0.55, 0.3), (0.46, 1.55, 0.34), "rubber")                                        # tunnel de chenille
    body = [(-1.2, 0.3), (-1.3, 0.52), (-1.0, 0.82), (-0.45, 1.0), (-0.15, 1.0), (0.05, 0.74), (1.25, 0.7),
            (1.35, 0.5), (1.2, 0.36)]
    g.prism(body, -0.44, 0.44, "blue", axis='x')
    g.box((0, 0.62, 0.84), (0.4, 1.15, 0.2), "black")                                           # selle
    n = Vector((0.0, -0.75, 1.0)).normalized()
    face_out(g, [(-0.36, -0.86, 0.95), (0.36, -0.86, 0.95), (0.3, -0.5, 1.4), (-0.3, -0.5, 1.4)], "glass_blue", n)
    face_out(g, [(-0.36, -0.86, 0.95), (0.36, -0.86, 0.95), (0.3, -0.5, 1.4), (-0.3, -0.5, 1.4)], "glass_blue", -n)
    g.box((0, -1.27, 0.62), (0.4, 0.05, 0.14), "lamp")
    for sx in (-1, 1):
        g.box((sx * 0.52, -1.0, 0.05), (0.15, 1.25, 0.05), "black")                             # skis
        g.box((sx * 0.52, -1.68, 0.11), (0.15, 0.22, 0.05), "black", rot=Matrix.Rotation(-0.6, 3, 'X'))
        if g.lod < 2:
            g.tube([(sx * 0.4, -0.85, 0.55), (sx * 0.52, -1.0, 0.1)], [0.03, 0.03], 5, "metal")
            g.box((sx * 0.46, 0.0, 0.6), (0.06, 1.6, 0.05), "yellow")                           # liseré
    if g.lod < 2:
        g.tube([(-0.38, -0.3, 1.12), (0.0, -0.36, 1.16), (0.38, -0.3, 1.12)], [0.022] * 3, 5, "metal_dark")
        g.box((0, 1.3, 0.92), (0.4, 0.3, 0.3), "metal_dark")                                    # coffre
    if g.hd:
        for k in range(6):
            g.box((0, -0.1 + k * 0.26, 0.125), (0.48, 0.05, 0.03), "black")


item("snowmobile", "Snowmobile", snowmobile, tags=["piste", "vehicle"], seed=561, fp=(1.2, 3.1), collider="box",
     note="motoneige ; avant = Unity +Z")


# ================================================================= neige
def snow_bank(g, length, width, height, key):
    mound(g, length, width, height, key=key, nx=lod(g, 10, 6, 4), ns=lod(g, 6, 4, 3))
    if g.hd:                                                                                    # mottes rejetées
        for k in range(int(length / 2.2)):
            x = (g.h(key, k, 7) - 0.5) * length * 0.8
            y = (g.h(key, k, 8) - 0.5) * width * 0.5
            blob(g, (x, y, height * (0.5 + 0.4 * g.h(key, k, 9))), 0.32 + 0.22 * g.h(key, k, 10), 0.75, key + k, 1)


item("snow_bank_a", "Snow_Bank_A", lambda g: snow_bank(g, 9.0, 2.3, 1.05, 1.0), tags=["snow", "roadside"], seed=562,
     fp=(9.0, 2.3), collider="none", module=8.0,
     note="congère de bord de route, allongée le long de X Unity ; pied enfoncé de 0,6 m")
item("snow_bank_b", "Snow_Bank_B", lambda g: snow_bank(g, 6.0, 2.0, 0.8, 2.0), tags=["snow", "roadside"], seed=563,
     fp=(6.0, 2.0), collider="none", module=5.2, note="congère courte")


def snow_pile(g):
    """Tas de neige de déneigement (parkings, bouts de rue)."""
    blob(g, (0, 0, 0.1), 2.4, 0.78, 1.0, lod(g, 3, 2, 1), 0.05)
    if g.lod < 2:
        blob(g, (1.7, 0.5, 0.0), 1.5, 0.7, 2.0, 2, 0.06)
        blob(g, (-1.5, -0.9, 0.0), 1.3, 0.65, 3.0, 2, 0.06)
        blob(g, (-0.4, 1.6, 0.0), 1.2, 0.6, 4.0, 2, 0.06)
    if g.hd:
        for k in range(7):
            a = k * 0.9
            blob(g, (math.cos(a) * 2.2, math.sin(a) * 2.0, 0.05), 0.4 + 0.25 * g.h(k, 5), 0.7, 5.0 + k, 1)


item("snow_pile", "Snow_Pile", snow_pile, tags=["snow"], seed=564, fp=(5.4, 5.0), collider="capsule", radius=2.2,
     note="tas de neige ; centre enfoncé")


def snowman(g):
    """Bonhomme de neige : trois boules, carotte, charbons, écharpe, chapeau, bras en branches."""
    s = lod(g, 2, 2, 1)
    g.ico((0, 0, 0.42), 0.55, s, SNOW, scale=(1, 1, 0.9))
    g.ico((0, 0, 1.08), 0.4, s, SNOW, scale=(1, 1, 0.92))
    g.ico((0, 0, 1.6), 0.28, s, SNOW)
    m = g.mark()
    g.frustum((0, 0, 0), 0.05, 0.0, 0.3, 6, "orange", auto=False)
    g.transform(g.since(m), Matrix.Translation((0, -0.26, 1.6)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    g.frustum((0, 0, 1.82), 0.3, 0.3, 0.03, lod(g, 12, 8, 6), "black", auto=False)              # chapeau
    g.frustum((0, 0, 1.84), 0.19, 0.17, 0.3, lod(g, 12, 8, 6), "black", auto=False)
    if g.lod == 2:
        return
    g.frustum((0, 0, 1.4), 0.3, 0.27, 0.11, 10, "red", cap0=False, cap1=False, auto=False)     # écharpe
    g.box((0.16, -0.27, 1.22), (0.11, 0.04, 0.4), "red", rot=Matrix.Rotation(0.2, 3, 'Y'))
    for sx in (-1, 1):
        g.octa((sx * 0.1, -0.25, 1.68), 0.032, "black")
        g.tube([(sx * 0.34, 0, 1.15), (sx * 0.75, -0.05, 1.42), (sx * 0.95, -0.02, 1.7)], [0.03, 0.022, 0.012], 5, "bark")
        if g.hd:
            g.tube([(sx * 0.75, -0.05, 1.42), (sx * 0.9, -0.12, 1.42)], [0.015, 0.008], 4, "bark")
    for z, y in ((1.22, -0.37), (1.05, -0.4), (0.88, -0.38)):
        g.octa((0, y, z), 0.035, "black")
    if g.hd:
        g.box((0, 0, 1.93), (0.4, 0.4, 0.035), "red")                                          # ruban du chapeau
        for k in range(5):
            a = -0.5 + k * 0.25
            g.octa((math.sin(a) * 0.17, -0.235 - 0.02 * math.cos(a * 3), 1.52 - 0.04 * math.cos(a * 2.6)), 0.018, "black")


item("snowman", "Snowman", snowman, tags=["snow", "prop"], seed=565, fp=(1.2, 1.1), collider="capsule", radius=0.5)


def igloo(g):
    """Igloo : dôme de blocs de neige, tunnel d'entrée, fanion."""
    f = g.ico((0, 0, 0), 2.1, lod(g, 3, 2, 2), SNOW, scale=(1, 1, 0.88), auto=False)
    kill = [fc for fc in f if all(v.co.z < -0.35 for v in fc.verts)]
    bmesh.ops.delete(g.bm, geom=kill, context='FACES')
    n = lod(g, 8, 6, 4)
    arch_o = [(-0.95, -0.4)] + [(-math.cos(math.pi * k / n) * 0.95, math.sin(math.pi * k / n) * 1.25) for k in range(n + 1)] + [(0.95, -0.4)]
    g.prism(arch_o, -3.0, -1.4, SNOW, axis='y')
    arch_i = [(-math.cos(math.pi * k / n) * 0.6, math.sin(math.pi * k / n) * 0.92) for k in range(n + 1)]
    face_out(g, [(x, -3.02, z) for x, z in arch_i], "center", (0, -1, 0))
    if g.lod == 2:
        return
    if g.hd:                                                                                    # assises de blocs
        for k, z in enumerate((0.45, 0.9, 1.3, 1.62)):
            r = 2.1 * math.sqrt(max(1.0 - (z / (2.1 * 0.88)) ** 2, 0.01)) + 0.012
            g.frustum((0, 0, z), r, r * 0.995, 0.035, 24, "roof_metal", cap0=False, cap1=False, auto=False)
    g.tube([(0.5, 0.2, 1.75), (0.5, 0.2, 3.0)], [0.025, 0.02], 5, "wood")
    g.cloth((0.52, 0.2, 2.98), (0.55, 0, 0), (0, 0, -0.32), lod(g, 4, 3), lod(g, 2, 1), "orange", amp=0.07)


item("igloo", "Igloo", igloo, tags=["snow", "prop"], seed=566, fp=(4.3, 5.2), collider="capsule", radius=2.0)


def sled(g):
    """Luge en bois : deux patins relevés, lattes, cordelette."""
    for sx in (-1, 1):
        g.tube([(sx * 0.2, 0.55, 0.03), (sx * 0.2, -0.35, 0.03), (sx * 0.2, -0.55, 0.1), (sx * 0.2, -0.62, 0.26)],
               [0.022] * 4, lod(g, 6, 4), "wood_honey", cap_start=True)
        for y in (-0.25, 0.35):
            g.box((sx * 0.2, y, 0.13), (0.035, 0.035, 0.2), "wood_honey")
    for k in range(lod(g, 5, 3)):
        g.box((-0.16 + k * (0.32 / (lod(g, 5, 3) - 1)), 0.05, 0.245), (0.06, 0.95, 0.02), "wood_cut")
    if g.hd:
        g.tube([(-0.2, -0.62, 0.26), (0.0, -0.9, 0.04), (0.2, -0.62, 0.26)], [0.008] * 3, 4, "red")


item("sled", "Sled", sled, tags=["snow", "prop", "small"], lods=2, seed=567, fp=(0.5, 1.3), collider="none")


# ================================================================= bâtiments du front de neige
def crossed_skis(f, u, z, size=1.5, out=0.08):
    """Emblème : deux skis croisés (rouge et jaune) et un flocon, plaqués sur une façade."""
    g = f.g
    for k, (col, a) in enumerate((("red", 0.62), ("yellow", -0.62))):
        rot = Matrix.Rotation(a, 3, 'Y') if f.axis == 'y' else Matrix.Rotation(a, 3, 'X')
        g.box(f.P(u, out + 0.03 * k, z), f.S(0.16, 0.025, size), col, rot=rot)


def flake(g, c, r, color="white", w=0.09, depth=0.03, y_step=0.02, facing=-1):
    """Flocon : trois barres croisées à 60°, à des profondeurs différentes (jamais coplanaires)."""
    x, y, z = c
    for k in range(3):
        rot = Matrix.Rotation(math.pi / 3 * k, 3, 'Y')
        g.box((x, y + facing * y_step * k, z), (w, depth, 2 * r), color, rot=rot)
        if g.hd:
            for s in (-1, 1):
                d = rot @ Vector((0, 0, s * r * 0.62))
                for t in (-1, 1):
                    rr = Matrix.Rotation(math.pi / 3 * k + t * 0.8, 3, 'Y')
                    tip = rr @ Vector((0, 0, s * r * 0.3))
                    g.box((x + d.x + tip.x / 2, y + facing * (y_step * k + 0.012 * (t + 2)), z + d.z + tip.z / 2),
                          (w * 0.7, depth, r * 0.3), color, rot=rr)


def ski_shop(g):
    """Magasin de location de skis : petit chalet à pignon sur rue, vitrine éclairée, skis en façade, toit enneigé."""
    W, D, Z0, ZE, PITCH = 9.0, 7.0, 0.4, 3.4, 28.0
    K.foundation(g, W, D, "stone", top=Z0)
    zr = K.gable_body(g, W, D, Z0, ZE, PITCH, "wood_honey", ridge='y')
    K.roof_gable(g, W, D, ZE, PITCH, "roof_slate", ridge='y', over_e=0.9, over_g=1.1, trim="wood_dark")
    roof_snow(g, W, D, ZE, PITCH, over_e=0.9, over_g=1.1, depth=0.42)
    f = Fac(g, 'y', -D / 2, -1)
    # vitrine et porte
    f.slab(-1.7, Z0 + 1.35, 4.0, 2.0, 0.06, "window_lit")
    f.door(2.7, w=1.15, h=2.25, color="door", z0=Z0, glass=True)
    if g.lod < 2:
        for u in (-3.7, -1.7, 0.3):
            f.slab(u, Z0 + 1.35, 0.12, 2.1, 0.1, "wood_dark")
        f.slab(-1.7, Z0 + 2.42, 4.2, 0.14, 0.1, "wood_dark")
        f.slab(-1.7, Z0 + 0.28, 4.2, 0.14, 0.12, "wood_dark")
        f.awning(-1.7, Z0 + 3.0, 4.6, depth=1.3, drop=0.5, color="awning_red", stripe="awning_cream")
        crossed_skis(f, 0.0, ZE + 1.0, 1.9)
        f.lantern(1.75, Z0 + 2.3)
        K.steps(g, 2.7, -D / 2, 1.7, n=2, color="stone")
        # skis à louer, debout contre la façade
        cols = ("sign_red", "sign_blue", "yellow", "green", "orange", "white")
        for k in range(lod(g, 6, 4)):
            g.box((-4.15 + k * 0.17, -D / 2 - 0.1, Z0 + 0.92), (0.1, 0.03, 1.75), cols[k % 6],
                  rot=Matrix.Rotation(0.09, 3, 'X'))
    if g.hd:
        # mannequin de vitrine : snowboards exposés derrière la vitre (devant le verre, donc visibles)
        for k, col in enumerate(("orange", "sign_blue", "green")):
            g.box((-2.9 + k * 1.2, -D / 2 - 0.085, Z0 + 1.3), (0.28, 0.02, 1.45), col)
        g.box((0.3 + 1.0, -D / 2 - 0.2, Z0 + 0.25), (1.5, 0.4, 0.06), "wood")                    # banc
        for sx in (-1, 1):
            g.box((1.3 + sx * 0.6, -D / 2 - 0.2, Z0 + 0.1), (0.08, 0.36, 0.25), "wood_dark")
    # façades latérales et arrière
    for sx in (-1, 1):
        side = Fac(g, 'x', sx * W / 2, sx)
        for u in (-1.6, 1.6):
            side.window(u, Z0 + 1.5, 1.0, 1.2, shutter="shutter", lit=0.7)
    back = Fac(g, 'y', D / 2, 1)
    back.window(0.0, Z0 + 1.5, 1.0, 1.2, flat=True, lit=0.3)
    if g.lod < 2:
        K.chimney(g, 2.6, 1.2, ZE + 0.6, zr + 0.9, "stone")
        flat_snow(g, (2.6, 1.2, zr + 0.98), (0.6, 0.6), 0.2)
        mound(g, 7.0, 1.3, 0.55, key=11.0, nx=6, ns=4, c=(-W / 2 - 0.7, 0.0), rotz=math.pi / 2)   # congères au pied
        mound(g, 6.0, 1.2, 0.5, key=12.0, nx=6, ns=4, c=(0.5, D / 2 + 0.6))


item("ski_shop", "Ski_Shop", ski_shop, tags=["piste", "shop"], seed=570, fp=(9.0, 7.0),
     note="magasin de location de skis ; façade (vitrine) = Unity +Z")


def ticket_office(g):
    """Caisses des remontées : kiosque à deux guichets, auvent, bandeau de couleur, toit plat enneigé, files."""
    W, D, Z0, H = 7.0, 3.6, 0.3, 3.1
    K.foundation(g, W, D, "concrete", top=Z0, stones=False)
    g.slab((0, 0, (Z0 + H) / 2), (W, D, H - Z0), "wood_honey", open='-z')
    g.box((0, -0.5, H + 0.12), (W + 0.5, D + 1.9, 0.24), "wood_dark")                            # toiture + auvent
    flat_snow(g, (0, -0.5, H + 0.24), (W + 0.4, D + 1.8), 0.36)
    f = Fac(g, 'y', -D / 2, -1)
    f.slab(0, H - 0.42, W - 0.1, 0.5, 0.05, "sign_blue")
    for u in (-1.75, 1.75):
        f.slab(u, Z0 + 1.55, 1.9, 1.1, 0.05, "window_lit")
        if g.lod < 2:
            f.slab(u, Z0 + 0.95, 2.1, 0.08, 0.3, "wood_dark")                                   # tablette
            f.slab(u, Z0 + 2.16, 2.1, 0.1, 0.09, "wood_dark")
    if g.lod == 2:
        return
    flake(g, (0.0, -D / 2 - 0.08, H - 0.42), 0.19, "white", w=0.05, depth=0.012, y_step=0.03)
    for sx in (-1, 1):
        g.box((sx * (W / 2 + 0.05), -D / 2 - 1.2, (H - 0.1) / 2), (0.16, 0.16, H + 0.1), "wood_dark")   # poteaux d'auvent
    # couloirs de file d'attente
    for u in (-2.9, -0.6, 0.6, 2.9):
        g.tube([(u, -D / 2 - 0.3, 0.0), (u, -D / 2 - 0.3, 1.0), (u, -D / 2 - 2.6, 1.0), (u, -D / 2 - 2.6, 0.0)],
               [0.03] * 4, 6, "metal")
    back = Fac(g, 'y', D / 2, 1)
    back.door(1.8, w=0.9, h=2.1, color="door", z0=Z0)
    if g.hd:
        for u in (-1.75, 1.75):
            g.box((u, -D / 2 - 0.06, Z0 + 2.45), (0.5, 0.03, 0.22), "sign_yellow")              # tarifs (sans texte)
        mound(g, 5.0, 1.1, 0.45, key=21.0, nx=6, ns=4, c=(W / 2 + 0.6, 0.2), rotz=math.pi / 2)


item("ticket_office", "Ticket_Office", ticket_office, tags=["piste", "station"], seed=571, fp=(7.0, 5.6),
     note="caisses des remontées ; guichets = Unity +Z")


def resort_totem(g):
    """Totem d'entrée de la station : deux piles de pierre, portique de bois, panneau au flocon, petit toit enneigé."""
    for sx in (-1, 1):
        g.box((sx * 2.2, 0, 0.3), (1.0, 1.0, 2.6), "stone")
        g.box((sx * 2.2, 0, 1.68), (1.14, 1.14, 0.16), "stone_dark")
        g.box((sx * 2.2, 0, 3.2), (0.34, 0.34, 3.0), "wood_dark")
        if g.lod < 2:
            flat_snow(g, (sx * 2.2 + sx * 0.35, 0, 1.76), (0.42, 1.1), 0.16)
            g.beam((sx * 2.2, 0, 3.6), (sx * 1.3, 0, 4.55), 0.16, "wood_dark")
    g.box((0, 0, 4.72), (5.6, 0.3, 0.3), "wood_dark")
    K.roof_gable(g, 1.5, 5.4, 4.88, 32.0, "roof_slate", ridge='x', over_e=0.25, over_g=0.3, thick=0.12, gutters=False)
    roof_snow(g, 1.5, 5.4, 4.88, 32.0, over_e=0.25, over_g=0.3, thick=0.12, depth=0.3, ridge='x')
    # panneau suspendu : fond bleu, cadre de bois, flocon blanc, bandeau jaune
    g.box((0, 0, 3.55), (3.4, 0.14, 1.7), "sign_blue")
    if g.lod == 2:
        return
    for z in (2.66, 4.44):
        g.box((0, 0, z), (3.6, 0.2, 0.1), "wood_honey")
    for sx in (-1, 1):
        g.box((sx * 1.75, 0, 3.55), (0.1, 0.2, 1.88), "wood_honey")
        g.tube([(sx * 1.2, 0, 4.58), (sx * 1.2, 0, 4.48)], [0.02, 0.02], 4, "metal_dark")
    for facing in (-1, 1):
        flake(g, (-0.85, facing * 0.1, 3.6), 0.6, "white", w=0.1, depth=0.02, y_step=0.045, facing=facing)
        g.box((0.75, facing * 0.085, 3.85), (1.3, 0.03, 0.34), "sign_yellow")
        g.box((0.75, facing * 0.085, 3.3), (1.3, 0.03, 0.2), "white")
        g.box((0.75, facing * 0.085, 3.0), (0.9, 0.03, 0.14), "white")
    if g.hd:
        mound(g, 2.4, 1.6, 0.4, key=31.0, nx=5, ns=4, c=(-2.3, 0.2))
        mound(g, 2.2, 1.5, 0.35, key=32.0, nx=5, ns=4, c=(2.3, -0.1))


item("resort_totem", "Resort_Totem", resort_totem, tags=["piste", "sign"], seed=572, fp=(5.8, 1.6), collider="box",
     note="totem d'entrée de station ; panneau lisible des deux côtés (Unity ±Z)")


# ================================================================= balisage des pistes
def piste_sign(g, color):
    """Panneau de piste : poteau de bois, disque de la couleur de la piste, flèche de direction."""
    g.box((0, 0.05, 1.0), (0.11, 0.11, 3.0), "wood")
    n = lod(g, 16, 10)
    m = g.mark()
    g.frustum((0, 0, 0), 0.34, 0.34, 0.04, n, "white", auto=False)
    g.frustum((0, 0, 0.04), 0.27, 0.27, 0.02, n, color, auto=False)
    g.transform(g.since(m), Matrix.Translation((0, -0.01, 2.2)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    g.box((0, -0.03, 1.6), (0.9, 0.04, 0.3), "white")
    g.box((-0.08, -0.06, 1.6), (0.5, 0.02, 0.1), color)
    g.prism([(0.14, 1.47), (0.36, 1.6), (0.14, 1.73)], -0.07, -0.05, color, axis='y')
    if g.hd:
        flat_snow(g, (0, 0.05, 2.5), (0.16, 0.16), 0.1)


for _c, _col in (("green", "sign_green"), ("blue", "sign_blue"), ("red", "sign_red"), ("black", "black")):
    item("piste_sign_" + _c, "Piste_Sign_" + _c.capitalize(), (lambda g, col=_col: piste_sign(g, col)), lods=2,
         tags=["piste", "prop"], seed=573, fp=(0.9, 0.2), collider="capsule", radius=0.08, no_resolve=True)


def piste_net(g):
    """Filet de protection (module de 6 m le long de X) : piquets orange, mailles."""
    for k in range(4):
        x = -3.0 + k * 2.0
        g.tube([(x, 0.0, -0.4), (x, 0.06, 1.55)], [0.025, 0.02], lod(g, 6, 4), "orange")
    for z in (0.3, 0.62, 0.94, 1.26):
        g.box((0, 0.03 * (z / 0.3) * 0.33, z), (6.0, 0.012, 0.05), "orange")
    g.box((0, 0.045, 1.48), (6.0, 0.02, 0.07), "sign_blue")
    if g.lod == 0:
        for k in range(25):
            x = -3.0 + 0.25 * k
            if k % 8 == 0:
                continue
            g.box((x, 0.075, 0.8), (0.022, 0.008, 1.1), "orange")


item("piste_net", "Piste_Net", piste_net, lods=2, tags=["piste", "net"], seed=574, fp=(6.0, 0.2), collider="none",
     module=6.0, no_resolve=True, note="filet de piste, module de 6 m le long de X Unity")


def slalom_gate(g, color):
    for sx in (-1, 1):
        g.tube([(sx * 0.32, 0, -0.3), (sx * 0.32, 0, 1.75)], [0.018, 0.014], lod(g, 5, 4), color)
    # toile tenue par les deux piquets : elle ondule au milieu (shader Toon, FlagWave)
    if g.lod == 0:
        cuts_u, cuts_v = (0, 0.258, 0.5, 0.742, 1), (0, 0.292, 0.5, 0.708, 1)
        cell = lambda i, j: "white" if i in (1, 2) and j in (1, 2) else color
    else:
        cuts_u, cuts_v, cell = 3, 1, color
    g.cloth((-0.31, 0, 1.61), (0.62, 0, 0), (0, 0, -0.72), cuts_u, cuts_v, cell, amp=0.035,
            profile=lambda u: math.sin(math.pi * u))


item("slalom_gate_red", "Slalom_Gate_Red", lambda g: slalom_gate(g, "sign_red"), lods=2, tags=["piste", "small"],
     seed=575, fp=(0.7, 0.1), collider="none", no_resolve=True)
item("slalom_gate_blue", "Slalom_Gate_Blue", lambda g: slalom_gate(g, "sign_blue"), lods=2, tags=["piste", "small"],
     seed=576, fp=(0.7, 0.1), collider="none", no_resolve=True)


# ================================================================= panneaux routiers
def sign_snow(g):
    """Danger : neige ou verglas (triangle, flocon noir)."""
    f = S.tri_sign(g)
    z = 2.23
    S.relief(f, S.ngon(0.03, 6, 0.0, (0, z)), "black", 0)
    for k in range(6):
        a = math.pi / 6 + math.pi / 3 * k
        dx, dz = math.cos(a), math.sin(a)
        S.stroke(f, (dx * 0.036, z + dz * 0.036), (dx * 0.17, z + dz * 0.17), 0.03, "black", 0)
        if g.hd:
            for t in (-1, 1):
                b = a + t * 0.9
                p = (dx * 0.11, z + dz * 0.11)
                S.stroke(f, p, (p[0] + math.cos(b) * 0.055, p[1] + math.sin(b) * 0.055), 0.022, "black", 1)


S.sign("sign_snow", "Sign_Snow", sign_snow, tags=["warning", "snow"])


def sign_chains(g):
    """Obligation : équipements spéciaux (disque bleu, pneu blanc à chaînes)."""
    f = S.round_sign(g, rim="sign_white", face="sign_blue")
    z = 2.3
    n = 20 if g.hd else 10
    S.relief(f, S.ngon(0.19, n, 0.0, (0, z)), "sign_white", 0)
    S.relief(f, S.ngon(0.095, n, 0.0, (0, z)), "sign_blue", 1)
    for k in range(6):
        a = math.pi / 3 * k + 0.3
        dx, dz = math.cos(a), math.sin(a)
        S.stroke(f, (dx * 0.105, z + dz * 0.105), (dx * 0.185, z + dz * 0.185), 0.03, "black", 1)


S.sign("sign_chains", "Sign_Chains", sign_chains, tags=["snow", "info"])
