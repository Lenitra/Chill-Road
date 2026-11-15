"""Pack alpin : fermes et objets ruraux (lot 5), mobilier urbain (lot 6). Face avant = -Y ; modules linéaires le long de X."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac
from alp_buildings import spread
from alp_landmarks import fronts
from alp_services import log_pile, parasol, table_set
from alp_props import prop, post, ngon


# ================================================================= lot 5 : fermes
def barn_alpine(g):
    """Grange-écurie d'alpage : socle de pierre (étable), fenil en madriers, pont de grange à l'arrière."""
    W, D = 14.0, 10.0
    gh, eave = 2.6, 5.6
    K.foundation(g, W, D, "stone")
    g.box((0, 0, gh / 2), (W, D, gh), "wall_stone")
    zr = K.gable_body(g, W + 0.3, D + 0.3, gh, eave, 24, "wood_dark", 'y')
    K.roof_gable(g, W + 0.3, D + 0.3, eave, 24, "lauze", 'y', over_e=1.0, over_g=1.4, thick=0.26, trim="wood_honey")
    front0, back0, left0, right0 = fronts(g, W, D)
    front = Fac(g, 'y', -D / 2 - 0.15, -1)
    front0.door(-3.5, 1.4, 2.0, "wood", frame=None)
    front0.door(3.5, 1.4, 2.0, "wood", frame=None)
    for u in (-5.8, -1.2, 1.2, 5.8):
        front0.window(u, 1.6, 0.7, 0.6, None, frame="plaster_white", lit=0.2)
    if g.lod == 0:
        front.slab(0, gh + 1.6, 3.6, 2.8, 0.06, "wood")                             # porte du fenil
        front.slab(0, gh + 1.6, 0.1, 2.8, 0.09, "wood_dark")
        for u in (-5.0, 5.0):
            front.slab(u, gh + 1.5, 1.2, 1.0, 0.05, "black")
        front.slab(0, zr - 1.6, 0.8, 0.8, 0.05, "black")
    else:
        front.quad(0, gh + 1.6, 3.6, 2.8, "wood", 0.04)
    for f in (left0, right0):
        for u in spread(3, -D / 2 + 0.5, D / 2 - 0.5):
            f.window(u, 1.6, 0.7, 0.6, None, frame="plaster_white", lit=0.2, keep=False)
    # pont de grange (rampe de terre) à l'arrière
    g.prism([(D / 2, -3.0), (D / 2 + 7.0, -3.0), (D / 2 + 7.0, -0.2), (D / 2, gh)], -2.4, 2.4, "grass", axis='x')
    Fac(g, 'y', D / 2 + 0.15, 1).quad(0, gh + 1.5, 3.6, 3.0, "wood", 0.04)
    if g.lod == 0:
        g.frustum((W / 2 + 2.0, -2.0, 0), 1.6, 0.3, 1.0, 7, "dirt")                 # tas de fumier
        log_pile(g, -W / 2 - 0.6, 0.0, 4, 1.0, 0.14, 1.57)


reg(dict(id="barn_alpine", file="Barn_Alpine", cat="farm", tags=["barn"], seed=601, fp=(14.0, 10.0), build=barn_alpine))


def cowshed(g):
    """Étable longue et basse : murs enduits, bardage sous le toit, portes coulissantes aux pignons."""
    W, D = 18.0, 8.0
    gh, eave = 2.4, 3.4
    K.foundation(g, W, D, "concrete")
    g.box((0, 0, gh / 2), (W, D, gh), "plaster_cream")
    zr = K.gable_body(g, D + 0.2, W + 0.2, gh, eave, 22, "wood", 'x')
    K.roof_gable(g, D + 0.2, W + 0.2, eave, 22, "roof_brown", 'x', over_e=0.8, over_g=0.7, thick=0.2)
    front, back, left, right = fronts(g, W, D)
    for u in spread(7, -W / 2 + 0.5, W / 2 - 0.5):
        front.window(u, 1.7, 1.0, 0.6, None, frame="wood_dark", lit=0.15)
        back.window(u, 1.7, 1.0, 0.6, None, flat=True)
    front.door(0, 1.3, 2.0, "wood_dark", frame=None)
    for f in (left, right):
        if g.lod == 0:
            f.slab(0.4, 1.3, 2.8, 2.6, 0.08, "wood_dark")
            f.slab(0, 2.75, 5.0, 0.1, 0.1, "metal_dark")
        else:
            f.quad(0.4, 1.3, 2.8, 2.6, "wood_dark", 0.04)
    if g.lod < 2:
        for x in (-5.0, 0.0, 5.0):                   # aérateurs de faîtage
            g.box((x, 0, zr + 0.35), (0.8, 0.8, 0.5), "roof_metal")
        g.frustum((W / 2 + 3.0, 1.0, 0), 2.0, 0.4, 1.1, 7, "dirt")
        g.box((-W / 2 - 1.5, -1.0, 0.45), (1.2, 2.6, 0.9), "metal")                 # cuve


reg(dict(id="cowshed", file="Cowshed", cat="farm", tags=["barn", "long"], seed=602, fp=(18.0, 8.0), build=cowshed))


def mazot(g):
    """Mazot (raccard) : petit grenier de madriers sombres sur pilotis de pierre coiffés de dalles."""
    W, D = 4.2, 4.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (W / 2 - 0.4), sy * (D / 2 - 0.4)
            g.box((x, y, -1.2), (0.5, 0.5, 3.6), "stone")
            g.frustum((x, y, 0.6), 0.55, 0.55, 0.12, lod(g, 7, 5, 4), "rock_light")
    g.box((0, 0, 0.82), (W + 0.3, D + 0.3, 0.2), "wood_dark")
    zr = K.gable_body(g, W, D, 0.9, 3.0, 24, "wood_dark", 'y')
    K.roof_gable(g, W, D, 3.0, 24, "lauze", 'y', over_e=0.7, over_g=0.9, thick=0.2, trim="wood")
    f = Fac(g, 'y', -D / 2, -1)
    f.door(0, 0.8, 1.6, "wood", frame="wood_honey", z0=0.95)
    if g.lod == 0:
        f.slab(0, 3.4, 0.4, 0.4, 0.05, "black")
        g.box((0, -D / 2 - 0.5, 0.4), (0.9, 0.3, 0.2), "wood")                     # marche
        g.box((0, -D / 2 - 0.5, 0.15), (0.2, 0.2, 0.5), "wood")
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.box((sx * (W / 2 + 0.05), sy * (D / 2 + 0.05), 1.95), (0.16, 0.16, 2.1), "wood")


reg(dict(id="mazot", file="Mazot", cat="farm", tags=["granary", "small"], seed=603, fp=(4.2, 4.6), build=mazot))


def alpage_hut(g):
    """Cabane d'alpage en pierres sèches, basse, toit de lauzes descendant presque au sol."""
    W, D = 7.0, 5.0
    K.foundation(g, W, D, "stone_dark", top=0.1)
    zr = K.gable_body(g, D, W, 0.0, 1.8, 26, "wall_stone", 'x')
    K.roof_gable(g, D, W, 1.8, 26, "lauze", 'x', over_e=0.7, over_g=0.4, thick=0.24)
    front, back, left, right = fronts(g, W, D)
    front.door(-1.0, 0.9, 1.65, "wood", frame=None)
    front.window(1.4, 1.15, 0.6, 0.5, None, frame="wood", lit=0.3)
    K.chimney(g, 2.2, 0.5, 1.8, zr + 0.5, "wall_stone", 0.6)
    if g.lod < 2:
        g.box((1.4, -D / 2 - 0.4, 0.25), (1.6, 0.4, 0.08), "wood")                 # banc
        for x in (0.8, 2.0):
            g.box((x, -D / 2 - 0.4, 0.1), (0.1, 0.3, 0.3), "wood")
        for k in range(5):
            f = g.ico((g.R(-W / 2, W / 2), g.R(-0.8, 0.8), zr - 0.5 + g.R(0, 0.2)), 0.2, 1, "rock_light", scale=(1.2, 1, 0.5))


reg(dict(id="alpage_hut", file="Alpage_Hut", cat="farm", tags=["alpage", "small"], seed=604, fp=(7.0, 5.0),
         build=alpage_hut))


def hayloft(g):
    """Fenil : petite grange à claire-voie sur dés de pierre."""
    W, D = 5.0, 4.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.box((sx * (W / 2 - 0.3), sy * (D / 2 - 0.3), -1.3), (0.6, 0.6, 3.4), "stone")
    zr = K.gable_body(g, D, W, 0.4, 2.6, 30, "wood_grey", 'x')
    K.roof_gable(g, D, W, 2.6, 30, "roof_brown", 'x', over_e=0.6, over_g=0.5, thick=0.16)
    f = Fac(g, 'y', -D / 2, -1)
    f.door(0, 1.4, 1.7, "wood", frame=None, z0=0.5)
    if g.lod == 0:
        for z in (0.9, 1.4, 1.9, 2.4):
            for ff in fronts(g, W, D)[1:]:
                ff.slab(0, z, (W if ff.axis == 'y' else D) - 0.2, 0.08, 0.03, "black")
        g.box((0, -D / 2 - 0.5, 0.15), (1.6, 1.0, 0.4), "hay")


reg(dict(id="hayloft", file="Hayloft", cat="farm", tags=["barn", "small"], seed=605, fp=(5.0, 4.0), build=hayloft))


def chicken_coop(g):
    g.box((0, 0.8, 0.9), (2.0, 1.6, 1.2), "wood_red")
    for sx in (-1, 1):
        for sy in (0.1, 1.5):
            g.box((sx * 0.9, sy, 0.0), (0.1, 0.1, 1.0), "wood")
    K.roof_shed(g, 2.0, 1.6, 1.9, 1.5, "roof_brown", over=0.2, thick=0.08, center=(0, 0.8), low='-y')
    f = Fac(g, 'y', 0.0, -1)
    f.quad(0.4, 0.75, 0.4, 0.5, "black", 0.02)
    if g.lod == 0:
        m = g.mark()
        g.box((0, 0, 0), (0.4, 1.0, 0.04), "wood")
        g.transform(g.since(m), Matrix.Translation((0.4, -0.45, 0.25)) @ Matrix.Rotation(-0.5, 4, 'X'))
    # enclos grillagé
    for x, y in ((-1.6, -2.2), (1.6, -2.2), (-1.6, 0.0), (1.6, 0.0)):
        g.box((x, y, 0.3), (0.07, 0.07, 1.6), "wood")
    for a, b in (((-1.6, -2.2), (1.6, -2.2)), ((-1.6, -2.2), (-1.6, 0.0)), ((1.6, -2.2), (1.6, 0.0))):
        g.beam((a[0], a[1], 1.05), (b[0], b[1], 1.05), 0.05, "wood")
        if g.lod == 0:
            g.beam((a[0], a[1], 0.55), (b[0], b[1], 0.55), 0.015, "metal", 0.9)
    if g.lod == 0:
        for x, y in ((-0.6, -1.3), (0.5, -0.9), (0.9, -1.7)):                       # poules
            g.ico((x, y, 0.2), 0.14, 1, "white" if x < 0.6 else "fur_tan", scale=(1, 1.4, 1))
            g.octa((x, y - 0.2, 0.36), 0.07, "red")


reg(dict(id="chicken_coop", file="Chicken_Coop", cat="rural", tags=["farm"], seed=606, lods=2, fp=(3.4, 4.0),
         collider="box", build=chicken_coop))


def beehives(g):
    cols = ("yellow", "sign_blue", "white", "green", "orange")
    g.box((0, 0, 0.35), (3.6, 0.7, 0.08), "wood")
    for x in (-1.6, 0.0, 1.6):
        g.box((x, 0, 0.0), (0.1, 0.6, 0.7 + 0.5), "wood")
    for k in range(5):
        x = -1.44 + k * 0.72
        g.box((x, 0, 0.7), (0.5, 0.55, 0.62), cols[k])
        g.box((x, 0, 1.05), (0.6, 0.66, 0.08), "roof_metal")
        if g.lod == 0:
            g.box((x, -0.28, 0.47), (0.2, 0.02, 0.03), "black")
    K.roof_shed(g, 4.0, 1.2, 2.1, 1.8, "roof_brown", over=0.1, thick=0.08, center=(0, 0.1), low='-y')
    for sx in (-1, 1):
        g.box((sx * 1.9, 0.6, 0.75), (0.1, 0.1, 2.6), "wood")


reg(dict(id="beehives", file="Beehives", cat="rural", tags=["farm"], seed=607, lods=2, fp=(4.0, 1.4), collider="box",
         build=beehives))


def trough(g):
    """Bassin creusé dans un tronc, goulot en bois, filet d'eau."""
    n = lod(g, 8, 6)
    m = g.mark()
    g.frustum((0, 0, -1.4), 0.42, 0.42, 2.8, n, "bark", cap0=True, cap1=True)
    g.transform(g.since(m), Matrix.Translation((0, 0, 0.42)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    g.box((0, 0, 0.84), (2.5, 0.52, 0.02), "wood_cut")
    g.box((0, 0, 0.855), (2.3, 0.4, 0.02), "water")
    for x in (-0.9, 0.9):
        g.box((x, 0, 0.1), (0.3, 0.9, 0.3), "wood")
    g.box((-1.55, 0, 0.5), (0.2, 0.2, 2.0), "wood")                               # chèvre
    g.limb((-1.5, 0, 1.3), (-0.9, 0, 1.2), 0.05, 0.04, 5, "wood_dark")
    if g.lod == 0:
        g.limb((-0.9, 0, 1.2), (-0.88, 0, 0.87), 0.025, 0.03, 4, "water", cap1=False)
        f = g.ico((1.45, 0.3, 0.2), 0.25, 1, "geranium", scale=(1, 1, 0.6))


reg(dict(id="trough", file="Trough", cat="rural", tags=["fountain"], seed=608, lods=2, fp=(3.0, 1.0), collider="box",
         build=trough))


def haybale_round(g):
    n = lod(g, 10, 7)
    m = g.mark()
    f = g.frustum((0, 0, -0.6), 0.7, 0.7, 1.2, n, "hay", cap0=True, cap1=True)
    g.paint([q for q in f if len(q.verts) == n], "dry")
    g.transform(g.since(m), Matrix.Translation((0, 0, 0.68)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))


reg(dict(id="haybale_round", file="HayBale_Round", cat="rural", tags=["hay"], seed=609, lods=2, collider="box",
         build=haybale_round))


def haybale_stack(g):
    for k, (x, y, z, r) in enumerate(((-0.55, 0, 0.25, 0.0), (0.55, 0.05, 0.25, 0.05), (0, 0.9, 0.25, 1.57),
                                      (0.0, 0.0, 0.75, 0.1), (0.1, 0.85, 0.75, 1.5), (0.3, 0.3, 1.25, 0.4))):
        if g.lod == 1 and k == 5:
            continue
        g.box((x, y, z), (1.0, 0.5, 0.48), "hay" if k % 2 else "dry", rotz=r)


reg(dict(id="haybale_stack", file="HayBale_Stack", cat="rural", tags=["hay"], seed=610, lods=2, collider="box",
         build=haybale_stack))


def woodpile(g):
    """Bûcher : bois fendu empilé entre deux montants, petit toit de planches."""
    L = 3.0
    for sx in (-1, 1):
        g.box((sx * L / 2, 0, 0.55), (0.1, 0.5, 2.1), "wood")
    K.roof_shed(g, L + 0.3, 0.7, 1.75, 1.55, "wood_grey", over=0.12, thick=0.06, low='-y')
    if g.lod == 0:
        rows, cols = 5, 7
        for r in range(rows):
            for c in range(cols):
                x = -L / 2 + 0.17 + (L - 0.34) * c / (cols - 1) + (0.1 if r % 2 else 0)
                g.box((min(x, L / 2 - 0.2), g.R(-0.03, 0.03), 0.15 + r * 0.28), (0.36, 0.5, 0.25),
                      "wood_cut" if (r + c) % 3 else "wood", rot=Matrix.Rotation(g.R(-0.4, 0.4), 3, 'Y'))
    else:
        g.box((0, 0, 0.72), (L - 0.12, 0.5, 1.44), "wood_cut")


reg(dict(id="woodpile", file="WoodPile", cat="rural", tags=["wood"], seed=611, lods=2, fp=(3.2, 0.7), collider="box",
         build=woodpile))


def cart_wheel(g, x, y, z, r, color="wood_dark"):
    """Roue de charrette dans le plan YZ."""
    m = g.mark()
    n = lod(g, 10, 6)
    g.frustum((0, 0, -0.04), r, r, 0.08, n, color, cap0=True, cap1=True)
    g.transform(g.since(m), Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))


def hay_cart(g):
    g.box((0, 0, 0.9), (1.5, 3.2, 0.12), "wood")
    for sx in (-1, 1):
        g.box((sx * 0.72, 0, 1.3), (0.06, 3.2, 0.7), "wood")
        for sy in (-1, 1):
            cart_wheel(g, sx * 0.86, sy * 1.0, 0.55, 0.55)
    g.limb((0, -1.6, 0.85), (0, -3.6, 0.5), 0.05, 0.04, 4, "wood")
    f = g.ico((0, 0, 1.7), 1.0, lod(g, 2, 1), "hay", scale=(0.85, 1.6, 0.8))
    g.jitter(f, 0.1)


reg(dict(id="hay_cart", file="Hay_Cart", cat="rural", tags=["hay", "farm"], seed=612, lods=2, fp=(1.8, 3.4),
         collider="box", build=hay_cart))


def scarecrow(g):
    g.box((0, 0, 0.7), (0.08, 0.08, 2.4), "wood")
    g.box((0, 0, 1.45), (1.6, 0.07, 0.07), "wood")
    g.box((0, 0, 1.2), (0.55, 0.3, 0.75), "sign_red")
    for sx in (-1, 1):
        g.box((sx * 0.55, 0, 1.45), (0.6, 0.22, 0.22), "sign_red")
        f = g.ico((sx * 0.9, 0, 1.45), 0.12, 1, "hay")
    g.ico((0, 0, 1.85), 0.22, 1, "wood_cut")
    g.frustum((0, 0, 1.95), 0.42, 0.42, 0.03, 8, "hay")
    g.frustum((0, 0, 1.98), 0.22, 0.14, 0.22, 7, "hay")
    g.box((0, 0, 0.6), (0.5, 0.26, 0.5), "sign_blue")


reg(dict(id="scarecrow", file="Scarecrow", cat="rural", tags=["farm"], seed=613, lods=1, build=scarecrow))


def wood_fence(g):
    """Clôture de pâturage : module de 3 m le long de X (un poteau à chaque extrémité partagé avec le suivant)."""
    L = 3.0
    g.box((-L / 2, 0, 0.35), (0.12, 0.12, 1.7), "wood")
    if g.lod == 0:
        g.frustum((-L / 2, 0, 1.2), 0.085, 0.0, 0.1, 4, "wood", rot=math.pi / 4)
    for z in (0.45, 0.95):
        g.box((0, 0, z), (L, 0.05, 0.13), "wood")


reg(dict(id="wood_fence", file="Wood_Fence", cat="rural", tags=["fence", "linear"], seed=614, lods=2, fp=(3.0, 0.15),
         module=3.0, build=wood_fence, note="module along X, post at Unity x = +1.5 end only"))


def stone_wall(g):
    """Muret de pierre sèche : module de 4 m le long de X, 0,9 m de haut, couronnement irrégulier."""
    L = 4.0
    f = g.box((0, 0, 0.2), (L, 0.6, 1.4), "wall_stone")
    if g.lod == 0:
        n = 7
        for k in range(n):
            x = -L / 2 + L * (k + 0.5) / n
            q = g.box((x, g.R(-0.03, 0.03), 0.9 + g.R(0.02, 0.1)), (L / n - 0.03, 0.66, 0.2),
                      "rock_light" if k % 2 else "stone")
        for k in range(5):
            x = g.R(-L / 2 + 0.3, L / 2 - 0.3)
            for sy in (-1, 1):
                g.box((x + sy * 0.3, sy * 0.3, g.R(0.2, 0.7)), (g.R(0.3, 0.6), 0.05, g.R(0.15, 0.25)), "rock_light")


reg(dict(id="stone_wall", file="Stone_Wall", cat="rural", tags=["wall", "linear"], seed=615, lods=2, fp=(4.0, 0.6),
         module=4.0, collider="box", build=stone_wall))


def gate_wood(g):
    """Portail de pré : 3 m le long de X, entre deux poteaux."""
    L = 3.0
    for sx in (-1, 1):
        g.box((sx * L / 2, 0, 0.45), (0.18, 0.18, 1.9), "wood")
    for z in (0.3, 0.65, 1.0):
        g.box((0, 0, z), (L - 0.3, 0.05, 0.12), "wood_honey")
    g.beam((-L / 2 + 0.15, 0, 0.3), (L / 2 - 0.15, 0, 1.0), 0.05, "wood_honey", 0.1)
    for x in (-L / 2 + 0.2, L / 2 - 0.2):
        g.box((x, 0, 0.65), (0.1, 0.06, 0.9), "wood_honey")


reg(dict(id="gate_wood", file="Gate_Wood", cat="rural", tags=["fence"], seed=616, lods=1, fp=(3.2, 0.2),
         collider="box", build=gate_wood))


def picnic_table(g):
    g.box((0, 0, 0.75), (1.8, 0.8, 0.06), "wood_honey")
    for sy in (-1, 1):
        g.box((0, sy * 0.72, 0.45), (1.8, 0.28, 0.05), "wood_honey")
    for sx in (-1, 1):
        g.beam((sx * 0.7, -0.75, 0.0), (sx * 0.7, 0.25, 0.75), 0.08, "wood", 0.06)
        g.beam((sx * 0.7, 0.75, 0.0), (sx * 0.7, -0.25, 0.75), 0.08, "wood", 0.06)
        g.box((sx * 0.7, 0, 0.42), (0.06, 1.7, 0.08), "wood")


reg(dict(id="picnic_table", file="Picnic_Table", cat="rural", tags=["leisure"], seed=617, lods=1, fp=(1.8, 1.8),
         collider="box", build=picnic_table))


def tent(g):
    g.prism([(-1.1, 0), (1.1, 0), (0, 1.3)], -1.2, 1.2, "orange", axis='y')
    g.face([(-0.45, -1.205, 0), (0.45, -1.205, 0), (0, -1.205, 1.0)], "black")
    if g.lod == 0:
        for sx in (-1, 1):
            g.limb((sx * 1.1, -1.2, 0.0), (sx * 1.6, -1.7, -0.05), 0.01, 0.01, 3, "white", cap1=False)
            g.limb((sx * 1.1, 1.2, 0.0), (sx * 1.6, 1.7, -0.05), 0.01, 0.01, 3, "white", cap1=False)
        g.box((1.6, 0.3, 0.2), (0.4, 0.6, 0.4), "sign_blue")                       # sac


reg(dict(id="tent", file="Tent", cat="rural", tags=["camping", "leisure"], seed=618, lods=2, fp=(2.2, 2.4),
         collider="box", build=tent))


def caravan(g):
    g.box((0, 0, 1.45), (2.2, 4.6, 1.9), "white")
    g.box((0, 0, 0.72), (2.24, 4.64, 0.3), "awning_green")
    g.box((0, 0, 2.45), (2.0, 4.2, 0.12), "white")
    for sx in (-1, 1):
        m = g.mark()
        g.frustum((0, 0, -0.1), 0.34, 0.34, 0.2, lod(g, 8, 6), "rubber")
        g.transform(g.since(m), Matrix.Translation((sx * 1.05, 0.3, 0.34)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
        f = Fac(g, 'x', sx * 1.1, sx)
        f.quad(-1.0, 1.65, 1.2, 0.7, "window", 0.02)
        if sx > 0:
            f.quad(1.2, 1.3, 0.65, 1.7, "plaster_cream", 0.02)
        else:
            f.quad(1.0, 1.65, 1.0, 0.7, "window", 0.02)
    Fac(g, 'y', -2.3, -1).quad(0, 1.7, 1.5, 0.7, "window", 0.02)
    g.limb((0, -2.3, 0.6), (0, -3.6, 0.45), 0.05, 0.05, 4, "metal")
    g.box((0, -3.5, 0.2), (0.08, 0.08, 0.5), "metal")
    if g.lod == 0:
        g.box((0, -2.75, 0.75), (0.5, 0.4, 0.5), "sign_red")                       # bouteille de gaz


reg(dict(id="caravan", file="Caravan", cat="rural", tags=["camping", "leisure"], seed=619, lods=2, fp=(2.3, 6.0),
         collider="box", build=caravan))


# ================================================================= lot 6 : mobilier urbain
def bench(g):
    for sx in (-1, 1):
        g.box((sx * 0.75, 0.0, 0.2), (0.08, 0.5, 0.5), "metal_dark")
        g.box((sx * 0.75, 0.22, 0.6), (0.08, 0.06, 0.5), "metal_dark")
    if g.lod == 0:
        for k in range(3):
            g.box((0, -0.16 + k * 0.16, 0.45), (1.8, 0.13, 0.04), "wood_honey")
        for k in range(2):
            g.box((0, 0.25, 0.62 + k * 0.17), (1.8, 0.04, 0.13), "wood_honey")
    else:
        g.box((0, 0, 0.45), (1.8, 0.45, 0.04), "wood_honey")
        g.box((0, 0.25, 0.7), (1.8, 0.04, 0.3), "wood_honey")


prop("bench", "Bench", bench, tags=["seat"], collider="box", fp=(1.8, 0.6))


def bin_(g):
    n = lod(g, 8, 6)
    g.frustum((0, 0, 0.15), 0.26, 0.3, 0.75, n, "awning_green", cap0=True)
    g.frustum((0, 0, 0.9), 0.32, 0.26, 0.1, n, "metal_dark")
    g.frustum((0, 0, -0.3), 0.05, 0.05, 0.5, 4, "metal_dark", cap0=False)


prop("bin", "Bin", bin_, tags=["small"], collider="capsule", radius=0.3)


def flower_blob(g, c, r, colors=("geranium", "flower_pink", "flower_yellow"), n=None):
    f = g.ico((c[0], c[1], c[2]), r * 0.9, 1, "leaf", scale=(1, 1, 0.55))
    g.jitter(f, r * 0.08)
    if g.lod == 0:
        for k in range(n or 7):
            a = g.R(0, 6.28)
            d = g.R(0, r * 0.75)
            g.octa((c[0] + math.cos(a) * d, c[1] + math.sin(a) * d, c[2] + r * 0.45 * (1 - d / r) + r * 0.12), r * 0.2,
                   colors[k % len(colors)])
    else:
        g.ico((c[0], c[1], c[2] + r * 0.25), r * 0.6, 1, colors[0], scale=(1, 1, 0.5))


def planter(g):
    """Bac à fleurs en bois."""
    g.box((0, 0, 0.3), (1.6, 0.6, 0.6), "wood")
    if g.lod == 0:
        for z in (0.12, 0.48):
            g.box((0, 0, z), (1.66, 0.66, 0.06), "wood_dark")
    for x in (-0.5, 0.0, 0.5):
        flower_blob(g, (x, 0, 0.62), 0.32, n=5)


prop("planter", "Planter", planter, tags=["flowers"], collider="box", fp=(1.6, 0.6))


def flower_pot(g):
    n = lod(g, 8, 6)
    g.frustum((0, 0, 0), 0.28, 0.4, 0.55, n, "cap", cap0=True)
    g.frustum((0, 0, 0.55), 0.44, 0.44, 0.07, n, "cap")
    flower_blob(g, (0, 0, 0.66), 0.44, ("geranium", "geranium", "flower_white"))


prop("flower_pot", "Flower_Pot", flower_pot, tags=["flowers", "small"], collider="capsule", radius=0.4)


def bollard(g):
    n = lod(g, 6, 5)
    g.frustum((0, 0, -0.5), 0.09, 0.08, 1.35, n, "metal_dark", cap0=False)
    g.ico((0, 0, 0.9), 0.1, 1, "metal_dark")
    g.frustum((0, 0, 0.62), 0.085, 0.085, 0.08, n, "white", cap0=False, cap1=False)


prop("bollard", "Bollard", bollard, tags=["small"], collider="capsule", radius=0.1)


def busstop(g):
    """Abribus vitré : ouvert côté -Y (chaussée), banc, poteau d'arrêt."""
    W = 3.6
    for sx in (-1, 1):
        for sy in (0.0, 1.3):
            g.box((sx * W / 2, sy, 1.0), (0.08, 0.08, 3.0), "metal_dark")
    g.box((0, 0.6, 2.5), (W + 0.5, 1.9, 0.1), "metal_dark")
    g.box((0, 1.3, 1.3), (W, 0.04, 1.9), "glass_blue")
    g.box((W / 2, 0.65, 1.3), (0.04, 1.3, 1.9), "glass_blue")
    g.box((-W / 2, 0.85, 1.3), (0.04, 0.9, 1.9), "glass_blue")
    g.box((0, 1.0, 0.45), (2.4, 0.4, 0.06), "wood_honey")
    if g.lod == 0:
        g.box((W / 2 - 0.02, 0.65, 1.4), (0.06, 1.0, 1.4), "awning_cream")          # affiche
        g.box((0, 0.6, 2.42), (2.0, 0.2, 0.04), "lamp")
    g.box((-W / 2 - 0.9, 0, 1.0), (0.07, 0.07, 3.0), "metal")
    g.box((-W / 2 - 0.9, -0.03, 2.3), (0.5, 0.05, 0.5), "sign_blue")


prop("busstop", "BusStop", busstop, tags=["shelter"], collider="mesh", fp=(4.2, 2.0), lights=[(0, 0.6, 2.4)])


def mailbox(g):
    g.box((0, 0, 0.25), (0.1, 0.1, 1.5), "metal_dark")
    g.box((0, 0, 1.2), (0.45, 0.3, 0.55), "yellow")
    g.box((0, 0, 1.5), (0.5, 0.34, 0.06), "yellow")
    if g.lod == 0:
        g.box((0, -0.16, 1.33), (0.3, 0.02, 0.04), "black")
        g.box((0, -0.16, 1.1), (0.2, 0.02, 0.14), "white")


prop("mailbox", "Mailbox", mailbox, tags=["small"], collider="capsule", radius=0.25)


def hydrant(g):
    n = lod(g, 7, 5)
    g.frustum((0, 0, -0.3), 0.13, 0.11, 0.95, n, "sign_red", cap0=False)
    g.frustum((0, 0, 0.65), 0.14, 0.05, 0.16, n, "sign_red")
    for sx in (-1, 1):
        g.limb((0, 0, 0.45), (sx * 0.2, 0, 0.45), 0.06, 0.06, 5, "sign_red")
    g.limb((0, 0, 0.4), (0, -0.2, 0.4), 0.07, 0.07, 5, "metal")


prop("hydrant", "Hydrant", hydrant, tags=["small"], collider="capsule", radius=0.15)


def bicycle(g, x=0.0, color="sign_red", lean=0.12):
    m = g.mark()
    for y in (-0.52, 0.52):
        cw = g.mark()
        g.frustum((0, 0, -0.02), 0.34, 0.34, 0.04, lod(g, 10, 6), "rubber", cap0=True, cap1=True)
        g.transform(g.since(cw), Matrix.Translation((0, y, 0.34)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    for a, b in (((0, 0.52, 0.34), (0, 0.15, 0.85)), ((0, 0.15, 0.85), (0, -0.4, 0.82)), ((0, -0.4, 0.82), (0, -0.52, 0.34)),
                 ((0, 0.15, 0.85), (0, 0.05, 0.36)), ((0, 0.05, 0.36), (0, -0.4, 0.82)), ((0, 0.05, 0.36), (0, 0.52, 0.34))):
        g.beam(a, b, 0.035, color)
    g.box((0, 0.2, 0.92), (0.12, 0.26, 0.05), "black")
    g.box((0, -0.42, 1.0), (0.5, 0.04, 0.04), "metal_dark")
    g.beam((0, -0.4, 0.82), (0, -0.42, 1.0), 0.03, "metal")
    g.transform(g.since(m), Matrix.Translation((x, 0, 0)) @ Matrix.Rotation(lean, 4, 'Y'))


prop("bicycle", "Bicycle", lambda g: bicycle(g), tags=["small"], fp=(0.6, 1.8))


def bike_rack(g):
    for x in (-0.9, -0.3, 0.3, 0.9):
        g.box((x, 0, 0.2), (0.04, 0.04, 1.3), "metal")
        g.box((x, 0.25, 0.4), (0.04, 0.5, 0.04), "metal")
    g.box((0, 0, 0.82), (2.0, 0.04, 0.04), "metal")
    bicycle(g, -0.62, "sign_blue", 0.08)
    if g.lod == 0:
        bicycle(g, 0.6, "sign_green", -0.06)


prop("bike_rack", "Bike_Rack", bike_rack, tags=["bikes"], collider="box", fp=(2.2, 1.8))


def cafe_table(g):
    table_set(g, 0, 0, 3)


prop("cafe_table", "Cafe_Table", cafe_table, tags=["terrace"], collider="capsule", radius=0.5, fp=(2.0, 2.0))


def parasol_table(g):
    table_set(g, 0, 0, 4)
    parasol(g, 0, 0, "awning_red" if g.seed % 2 else "awning_cream", 1.5, 2.4)


prop("parasol_table", "Parasol_Table", parasol_table, tags=["terrace"], collider="capsule", radius=0.5, fp=(3.0, 3.0))


def market_stall(g, canopy, canopy2, goods):
    W, D = 3.0, 1.6
    g.box((0, 0, 0.45), (W, D * 0.75, 0.9), "wood")
    g.box((0, -0.05, 0.93), (W + 0.1, D * 0.8, 0.06), "wood_honey")
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.box((sx * (W / 2 + 0.05), sy * D / 2, 1.0), (0.07, 0.07, 3.0), "wood")
    n = 6 if g.lod == 0 else 1
    seg = (W + 0.6) / n
    for k in range(n):
        x0 = -W / 2 - 0.3 + seg * k
        c = canopy2 if (canopy2 and k % 2) else canopy
        g.prism([(-D / 2 - 0.5, 2.2), (0, 2.75), (D / 2 + 0.3, 2.3), (D / 2 + 0.3, 2.36), (0, 2.81), (-D / 2 - 0.5, 2.26)],
                x0, x0 + seg, c, axis='x')
    if g.lod == 0:
        for k, (col, dx) in enumerate(goods):
            g.box((dx, -0.3, 1.02), (0.55, 0.45, 0.14), "wood_cut")
            for j in range(5):
                g.octa((dx - 0.18 + 0.09 * j, -0.3 + (j % 2) * 0.12 - 0.06, 1.14), 0.085, col)
    else:
        g.box((0, -0.3, 1.02), (W - 0.6, 0.45, 0.16), goods[0][0])


prop("market_stall_a", "Market_Stall_A",
     lambda g: market_stall(g, "awning_red", "awning_cream", [("apple", -1.0), ("pear", -0.3), ("orange", 0.4), ("leaf", 1.05)]),
     tags=["market"], collider="box", fp=(3.2, 2.2))
prop("market_stall_b", "Market_Stall_B",
     lambda g: market_stall(g, "awning_green", None, [("yellow", -1.0), ("wood_cut", -0.3), ("yellow", 0.4), ("plum", 1.05)]),
     tags=["market"], collider="box", fp=(3.2, 2.2))


def flagpole(g, color, cross=None):
    n = lod(g, 6, 4)
    g.frustum((0, 0, -0.5), 0.07, 0.04, 9.5, n, "white", cap0=False)
    g.ico((0, 0, 9.05), 0.08, 1, "brass")
    g.box((0.85, 0, 8.1), (1.6, 0.03, 1.6), color)
    if cross:
        g.box((0.85, 0, 8.1), (1.0, 0.05, 0.3), cross)
        g.box((0.85, 0, 8.1), (0.3, 0.05, 1.0), cross)
    if g.lod == 0:
        g.frustum((0, 0, 0), 0.14, 0.1, 0.3, n, "stone")


prop("flagpole_red", "Flagpole_Red", lambda g: flagpole(g, "red", "white"), tags=["flag"], collider="capsule", radius=0.1)
prop("flagpole_blue", "Flagpole_Blue", lambda g: flagpole(g, "blue", "yellow"), tags=["flag"], collider="capsule",
     radius=0.1)


def clock_post(g):
    n = lod(g, 6, 4)
    g.frustum((0, 0, -0.5), 0.14, 0.1, 1.3, n, "metal_dark", cap0=False)
    g.frustum((0, 0, 0.8), 0.07, 0.06, 2.6, n, "metal_dark", cap0=False)
    g.box((0, 0, 3.9), (0.9, 0.3, 0.9), "metal_dark")
    for s in (-1, 1):
        Fac(g, 'y', s * 0.15, s).clock(0, 3.9, 0.36, out=0.01)
    g.frustum((0, 0, 4.35), 0.3, 0.0, 0.3, 4, "metal_dark", rot=math.pi / 4)


prop("clock_post", "Clock_Post", clock_post, tags=["plaza"], collider="capsule", radius=0.14)


def info_board(g):
    """Panneau d'information touristique (plan de la station) sous un petit toit de bois."""
    for sx in (-1, 1):
        g.box((sx * 0.9, 0, 0.75), (0.12, 0.12, 2.5), "wood")
    g.box((0, 0, 1.4), (1.7, 0.08, 1.1), "wood_honey")
    g.box((0, -0.045, 1.4), (1.5, 0.02, 0.9), "awning_cream")
    if g.lod == 0:
        g.box((-0.2, -0.06, 1.5), (0.8, 0.02, 0.5), "leaf_light")
        g.box((0.4, -0.06, 1.3), (0.3, 0.02, 0.3), "water")
        g.box((0.0, -0.065, 1.15), (1.2, 0.02, 0.05), "sign_red")
    K.roof_gable(g, 0.6, 2.0, 2.05, 28, "wood_dark", 'x', over_e=0.1, over_g=0.15, thick=0.06)


prop("info_board", "Info_Board", info_board, tags=["tourism"], collider="box", fp=(2.0, 0.3))


def billboard(g):
    for sx in (-1, 1):
        g.box((sx * 2.0, 0, 1.5), (0.2, 0.2, 4.0), "metal")
    g.box((0, 0, 4.2), (6.0, 0.2, 3.0), "metal_dark")
    g.box((0, -0.11, 4.2), (5.7, 0.04, 2.7), "awning_cream")
    if g.lod == 0:
        g.box((-1.2, -0.14, 4.4), (2.6, 0.02, 1.6), "sign_blue")
        g.prism([(-2.4, 3.6), (-0.1, 3.6), (-1.2, 5.0)], -0.16, -0.14, "white", axis='y')   # montagne stylisée
        g.box((1.6, -0.14, 4.6), (1.6, 0.02, 0.5), "sign_red")
        g.box((1.6, -0.14, 3.9), (1.9, 0.02, 0.25), "black")
        g.box((0, -0.5, 5.9), (5.0, 0.08, 0.08), "metal")
        for x in (-1.8, 0, 1.8):
            g.box((x, -0.5, 5.82), (0.3, 0.14, 0.08), "lamp")


prop("billboard", "Billboard", billboard, tags=["advert"], collider="box", fp=(6.0, 0.4), lights=[(0, -0.5, 5.8)])


def recycling_bins(g):
    for k, c in enumerate(("sign_green", "sign_blue", "yellow")):
        x = (k - 1) * 1.5
        g.box((x, 0, 0.75), (1.35, 1.2, 1.5), c)
        g.prism([(-0.6, 1.5), (0.6, 1.5), (0.35, 1.8), (-0.35, 1.8)], x - 0.675, x + 0.675, c, axis='x')
        if g.lod == 0:
            m = g.mark()
            g.frustum((0, 0, 0), 0.17, 0.17, 0.03, 8, "black")
            g.transform(g.since(m), Matrix.Translation((x, -0.61, 1.15)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))


prop("recycling_bins", "Recycling_Bins", recycling_bins, tags=["service"], collider="box", fp=(4.4, 1.2))


def utility_pole(g):
    """Poteau en bois à traverse et isolateurs (lignes le long de Y) ; lampe d'éclairage public en option basse."""
    n = lod(g, 6, 4)
    g.frustum((0, 0, -0.5), 0.15, 0.1, 9.0, n, "wood_dark", cap0=False)
    g.box((0, 0, 7.9), (2.2, 0.12, 0.14), "wood_dark")
    for x in (-0.95, -0.35, 0.35, 0.95):
        g.frustum((x, 0, 7.97), 0.05, 0.04, 0.2, 5, "sign_white")
    if g.lod == 0:
        g.beam((0, 0, 7.2), (0.7, 0, 7.85), 0.05, "wood_dark")
        g.beam((0, 0, 7.2), (-0.7, 0, 7.85), 0.05, "wood_dark")
        g.box((0.12, 0, 1.6), (0.04, 0.2, 0.3), "sign_yellow")
    g.limb((0, 0, 6.3), (0, -0.9, 6.5), 0.03, 0.03, 4, "metal")
    g.box((0, -1.05, 6.5), (0.22, 0.4, 0.1), "metal_dark")
    g.box((0, -1.05, 6.44), (0.16, 0.3, 0.03), "lamp")


prop("utility_pole", "Utility_Pole", utility_pole, tags=["pole", "lamp"], collider="capsule", radius=0.15,
     lights=[(0, -1.05, 6.42)], note="wires along Z at y = 8.1, Unity x = ±0.95 / ±0.35")


def transformer(g):
    """Poste de transformation : édicule en béton à toit débordant, porte métallique, signal danger."""
    g.box((0, 0, 1.5 - 0.25), (2.6, 2.2, 3.0 + 0.5), "concrete")
    K.roof_pyramid(g, 2.6, 2.2, 3.0, 0.9, "roof_tile", over=0.3)
    f = Fac(g, 'y', -1.1, -1)
    f.door(0, 1.1, 2.0, "metal_dark", frame=None)
    if g.lod == 0:
        f.poly([(-0.2, 1.3), (0.2, 1.3), (0, 1.65)], "sign_yellow", 0.1)
        for sx in (-1, 1):
            Fac(g, 'x', sx * 1.3, sx).quad(0, 2.3, 1.0, 0.5, "metal_dark", 0.03)


prop("transformer", "Transformer", transformer, tags=["service"], collider="box", fp=(2.6, 2.2))


def phone_booth(g):
    g.box((0, 0, 0.05), (1.0, 1.0, 0.1 + 0.4), "metal_dark")
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.box((sx * 0.46, sy * 0.46, 1.2), (0.07, 0.07, 2.3), "sign_red")
    g.box((0, 0, 2.4), (1.05, 1.05, 0.25), "sign_red")
    g.box((0, 0.47, 1.2), (0.86, 0.03, 2.1), "glass_blue")
    for sx in (-1, 1):
        g.box((sx * 0.47, 0, 1.2), (0.03, 0.86, 2.1), "glass_blue")
    if g.lod == 0:
        g.box((0, 0.36, 1.35), (0.4, 0.14, 0.55), "metal")
        g.box((0, -0.53, 2.4), (0.7, 0.02, 0.14), "white")


prop("phone_booth", "Phone_Booth", phone_booth, tags=["service"], collider="box", fp=(1.05, 1.05))
