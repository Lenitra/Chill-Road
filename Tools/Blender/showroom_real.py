"""Showroom réaliste (Cycles) : concession automobile vitrée pour les voitures du jeu.
Scène « ShowroomReal » de CarPack.blend, construite par build() :
- halle de 48 x 32 m, façades vitrées au sud (rue) et à l'est, mur de lattes de bois au fond, plafond blanc à
  réglettes LED et rails de spots, poteaux ronds ; carrelage poli à joints, plâtre, bois, alu (matériaux procéduraux,
  aucun téléchargement) ;
- zone premium côté vitrine : les 9 voitures jouables sur plaques tournantes (la Vision 74 au centre, cordons), un
  spot par voiture, bornes d'information (écrans vierges) ;
- zone d'exposition au fond : voitures du trafic en épi (sans taxi ni police) ;
- accueil, coin salon, plantes ; dehors : place dallée, rue, trottoir, immeubles, lampadaires ; ciel physique.
Les voitures sont des copies des collections du pack (reconstruites à chaque build) dont les faces reçoivent des
matériaux réalistes selon leur colonne de palette : peinture vernie, vitres teintées, caoutchouc, chromes, feux.
Usage (Blender MCP, appels séparés) : showroom_real.use_scene() ; puis build() ; puis render(dossier, vues).
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix
import hc_lib as H
import pack_lib as P
import lp_lib
import showroom as S

SCENE = "ShowroomReal"
HALL_X, HALL_Y, HALL_H = 24.0, 32.0, 6.5          # demi-largeur, profondeur, hauteur sous plafond

# (id, x, y, cap) : cap = direction de l'avant en degrés (0 = +X), vers la vitrine et l'angle sud-est
PREMIUM = [("supra", -18.0, 6.2, -40.0), ("jesko", -9.0, 5.2, -45.0), ("vision74", 0.0, 8.5, -50.0),
           ("valkyrie", 9.0, 5.2, -45.0), ("p50", 18.0, 6.2, -40.0),
           ("miata", -13.5, 13.8, -45.0), ("ae86", -4.5, 14.8, -45.0), ("dmc12", 4.5, 14.8, -45.0),
           ("mustang", 13.5, 13.8, -45.0)]
STOCK_A = ["fiat500", "civic", "golf", "corolla", "model3"]
STOCK_B = ["rav4", "kangoo", "f150", "transit"]

# Vues : (nom, position, point visé, focale, hauteur du soleil en degrés, exposition)
VIEWS = [
    ("1_interieur", (22.3, 16.5, 1.7), (-5.0, 7.5, 0.9), 22, 32.0, -1.0),
    ("2_vedette", (6.8, 5.0, 1.05), (0.2, 8.7, 0.7), 35, 32.0, -1.0),
    ("3_exterieur_crepuscule", (10.0, -28.5, 1.6), (2.0, 8.0, 3.4), 24, 1.5, 0.3),
    ("4_exposition", (20.5, 17.0, 1.7), (-8.0, 25.0, 0.7), 24, 32.0, -1.0),
    ("5_vue_haute", (-22.5, 30.5, 5.6), (3.0, 10.0, 0.0), 20, 32.0, -1.0),
]


def use_scene():
    """Crée la scène et l'affiche (à appeler seul : le contexte suit au prochain appel)."""
    scn = bpy.data.scenes.get(SCENE) or bpy.data.scenes.new(SCENE)
    for win in bpy.context.window_manager.windows:
        if win.scene != scn:
            win.scene = scn
    return scn


def col(name):
    return H.get_col(name, bpy.data.scenes[SCENE].collection)


def clear(name):
    c = bpy.data.collections.get(name)
    if not c:
        return
    for o in list(c.objects):
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
                if data.name in coll and coll[data.name] == data:
                    coll.remove(data)
                    break


# ----------------------------------------------------------------- matériaux
def _nodes(m):
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    b = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(b.outputs[0], out.inputs[0])
    return nt, b


def pbr(name, base, rough=0.5, metal=0.0, coat=0.0, emit=None, strength=0.0, transmission=0.0, ior=1.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = _nodes(m)
    H.sock(b, "Base Color").default_value = (*base, 1.0)
    H.sock(b, "Roughness").default_value = rough
    H.sock(b, "Metallic").default_value = metal
    H.sock(b, "IOR").default_value = ior
    if coat:
        H.sock(b, "Coat Weight").default_value = coat
        H.sock(b, "Coat Roughness").default_value = 0.03
    if transmission:
        H.sock(b, "Transmission Weight").default_value = transmission
    if emit is not None:
        H.sock(b, "Emission Color").default_value = (*emit, 1.0)
        H.sock(b, "Emission Strength").default_value = strength
    return m


def tiles_material(name, c1, c2, grout, size, rough=(0.06, 0.16), mortar=0.004):
    """Dalles carrées (Brick Texture sans décalage) à légère variation de teinte, joints plus sombres, brillance
    irrégulière."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = _nodes(m)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    br = nt.nodes.new("ShaderNodeTexBrick")
    br.offset = 0.0
    br.squash = 1.0
    nt.links.new(tc.outputs["Object"], br.inputs["Vector"])
    br.inputs["Color1"].default_value = (*c1, 1.0)
    br.inputs["Color2"].default_value = (*c2, 1.0)
    br.inputs["Mortar"].default_value = (*grout, 1.0)
    br.inputs["Scale"].default_value = 1.0
    br.inputs["Mortar Size"].default_value = mortar
    br.inputs["Brick Width"].default_value = size
    br.inputs["Row Height"].default_value = size
    nt.links.new(br.outputs["Color"], H.sock(b, "Base Color"))
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 0.35
    nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["To Min"].default_value = rough[0]
    mr.inputs["To Max"].default_value = rough[1]
    nt.links.new(nz.outputs["Fac"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], H.sock(b, "Roughness"))
    return m


def wood_material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = _nodes(m)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wv = nt.nodes.new("ShaderNodeTexWave")
    wv.wave_type = 'BANDS'
    wv.bands_direction = 'Z'
    wv.inputs["Scale"].default_value = 3.0
    wv.inputs["Distortion"].default_value = 7.0
    wv.inputs["Detail"].default_value = 3.0
    nt.links.new(tc.outputs["Object"], wv.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.16, 0.075, 0.03, 1.0)
    ramp.color_ramp.elements[1].color = (0.36, 0.18, 0.08, 1.0)
    nt.links.new(wv.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], H.sock(b, "Base Color"))
    H.sock(b, "Roughness").default_value = 0.45
    return m


def plaster_material(name, base):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = _nodes(m)
    H.sock(b, "Base Color").default_value = (*base, 1.0)
    H.sock(b, "Roughness").default_value = 0.9
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 40.0
    nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.04
    nt.links.new(nz.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], H.sock(b, "Normal"))
    return m


def building_material(name, wall, glass):
    """Façade d'immeuble : grille de fenêtres (Brick Texture), vitres sombres réfléchissantes."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = _nodes(m)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    nt.links.new(sep.outputs["X"], comb.inputs["X"])           # grille sur le plan vertical (x, z)
    nt.links.new(sep.outputs["Z"], comb.inputs["Y"])
    br = nt.nodes.new("ShaderNodeTexBrick")
    br.offset = 0.0
    nt.links.new(comb.outputs[0], br.inputs["Vector"])
    br.inputs["Color1"].default_value = (*glass, 1.0)
    br.inputs["Color2"].default_value = (*[c * 1.15 for c in glass], 1.0)
    br.inputs["Mortar"].default_value = (*wall, 1.0)
    br.inputs["Scale"].default_value = 1.0
    br.inputs["Mortar Size"].default_value = 0.45
    br.inputs["Brick Width"].default_value = 2.4
    br.inputs["Row Height"].default_value = 3.2
    nt.links.new(br.outputs["Color"], H.sock(b, "Base Color"))
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["To Min"].default_value = 0.08
    mr.inputs["To Max"].default_value = 0.85
    nt.links.new(br.outputs["Fac"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], H.sock(b, "Roughness"))
    return m


def materials():
    M = {}
    M["floor"] = tiles_material("RL_FloorTiles", (0.62, 0.62, 0.60), (0.56, 0.56, 0.55), (0.30, 0.30, 0.30), 1.2)
    M["plaza"] = tiles_material("RL_Plaza", (0.36, 0.35, 0.33), (0.30, 0.30, 0.29), (0.18, 0.18, 0.18), 0.8,
                                rough=(0.55, 0.85), mortar=0.01)
    M["walk"] = tiles_material("RL_Sidewalk", (0.45, 0.44, 0.42), (0.40, 0.40, 0.39), (0.22, 0.22, 0.22), 0.5,
                               rough=(0.6, 0.9), mortar=0.008)
    M["asphalt"] = pbr("RL_Asphalt", (0.035, 0.035, 0.038), rough=0.85)
    M["line"] = pbr("RL_RoadLine", (0.75, 0.75, 0.72), rough=0.7)
    M["plaster"] = plaster_material("RL_Plaster", (0.80, 0.80, 0.78))
    M["ceiling"] = plaster_material("RL_Ceiling", (0.86, 0.86, 0.85))
    M["wood"] = wood_material("RL_Wood")
    M["glass"] = pbr("RL_WindowGlass", (1.0, 1.0, 1.0), rough=0.0, transmission=1.0, ior=1.45)
    M["mullion"] = pbr("RL_Mullion", (0.02, 0.02, 0.022), rough=0.35, metal=0.8)
    M["alu"] = pbr("RL_BrushedAlu", (0.78, 0.79, 0.8), rough=0.22, metal=1.0)
    M["turntop"] = pbr("RL_TurntableTop", (0.03, 0.032, 0.035), rough=0.35, coat=0.6)
    M["chrome"] = pbr("RL_Chrome", (0.92, 0.92, 0.94), rough=0.04, metal=1.0)
    M["velvet"] = pbr("RL_Velvet", (0.32, 0.015, 0.025), rough=0.95)
    M["led"] = pbr("RL_LEDStrip", (1.0, 1.0, 1.0), emit=(1.0, 0.96, 0.9), strength=9.0)
    M["fixture"] = pbr("RL_Fixture", (0.04, 0.04, 0.045), rough=0.4, metal=0.6)
    M["screen"] = pbr("RL_Screen", (0.01, 0.012, 0.016), rough=0.05, emit=(0.55, 0.7, 0.95), strength=0.6)
    M["desk"] = pbr("RL_DeskWhite", (0.85, 0.85, 0.84), rough=0.25, coat=0.5)
    M["fabric"] = pbr("RL_Fabric", (0.14, 0.15, 0.16), rough=1.0)
    M["rug"] = pbr("RL_Rug", (0.28, 0.26, 0.23), rough=1.0)
    M["leaf"] = pbr("RL_Leaf", (0.05, 0.16, 0.035), rough=0.55)
    M["pot"] = pbr("RL_Pot", (0.12, 0.12, 0.13), rough=0.4, coat=0.4)
    M["trunk"] = pbr("RL_Trunk", (0.12, 0.08, 0.05), rough=0.8)
    M["building1"] = building_material("RL_Building1", (0.55, 0.53, 0.5), (0.05, 0.07, 0.09))
    M["building2"] = building_material("RL_Building2", (0.32, 0.3, 0.29), (0.06, 0.08, 0.1))
    M["lamp"] = pbr("RL_LampHead", (1.0, 0.95, 0.85), emit=(1.0, 0.85, 0.6), strength=6.0)
    M["fascia"] = pbr("RL_Fascia", (0.09, 0.095, 0.1), rough=0.35, metal=0.6)
    return M


# matériaux réalistes des voitures, par colonne de palette
def car_materials():
    C = {}
    C["glass"] = pbr("RL_CarGlass", (0.008, 0.01, 0.012), rough=0.02, coat=1.0)
    C["tire"] = pbr("RL_Rubber", (0.018, 0.018, 0.02), rough=0.85)
    C["rim"] = pbr("RL_Rim", (0.72, 0.73, 0.75), rough=0.25, metal=1.0)
    C["chrome"] = bpy.data.materials.get("RL_Chrome") or pbr("RL_Chrome", (0.92, 0.92, 0.94), rough=0.04, metal=1.0)
    C["trim"] = pbr("RL_Plastic", (0.03, 0.03, 0.033), rough=0.45)
    C["dark"] = pbr("RL_DarkPlastic", (0.015, 0.015, 0.018), rough=0.5)
    C["black"] = pbr("RL_BlackGloss", (0.02, 0.02, 0.022), rough=0.2, coat=0.6)
    C["lens"] = pbr("RL_Lens", (0.85, 0.87, 0.9), rough=0.05, metal=0.8)
    C["red"] = pbr("RL_RedGlass", (0.4, 0.012, 0.01), rough=0.08, coat=1.0, emit=(1.0, 0.03, 0.02), strength=0.6)
    C["amber"] = pbr("RL_AmberGlass", (0.75, 0.3, 0.02), rough=0.08, coat=1.0)
    C["white"] = pbr("RL_WhitePaint", (0.78, 0.78, 0.77), rough=0.35, coat=1.0)
    C["yellow"] = pbr("RL_YellowPaint", (0.85, 0.5, 0.03), rough=0.35, coat=1.0)
    C["plate"] = pbr("RL_Plate", (0.82, 0.82, 0.8), rough=0.5)
    C["blue"] = pbr("RL_BluePaint", (0.03, 0.08, 0.35), rough=0.35, coat=1.0)
    C["grey"] = pbr("RL_GreyPlastic", (0.18, 0.18, 0.19), rough=0.5)
    C["orange"] = pbr("RL_OrangePaint", (0.8, 0.22, 0.01), rough=0.35, coat=1.0)
    C["green"] = pbr("RL_GreenPaint", (0.03, 0.2, 0.06), rough=0.35, coat=1.0)
    C["led"] = pbr("RL_Headlight", (0.9, 0.9, 0.9), rough=0.05, emit=(1.0, 0.95, 0.88), strength=4.0)
    C["tail"] = bpy.data.materials.get("RL_RedGlass")
    C["accent"] = pbr("RL_Accent", (0.4, 0.78, 0.03), rough=0.3, coat=1.0)
    C["siren_red"] = pbr("RL_SirenRed", (0.5, 0.01, 0.01), rough=0.1, emit=(1.0, 0.03, 0.02), strength=2.0)
    C["siren_blue"] = pbr("RL_SirenBlue", (0.02, 0.05, 0.5), rough=0.1, emit=(0.05, 0.2, 1.0), strength=2.0)
    return C


def paint_material(cid):
    """Peinture vernie de la voiture (couleur de la fiche) ; inox brossé pour la DeLorean, argent métallisé pour la
    Vision 74."""
    name = "RL_Paint_" + cid
    if cid == "dmc12":
        return pbr(name, (0.62, 0.63, 0.64), rough=0.3, metal=1.0)
    if cid == "vision74":
        return pbr(name, (0.72, 0.74, 0.76), rough=0.25, metal=0.5, coat=1.0)
    c = P.resolve(cid).get("color", (0.6, 0.6, 0.6))
    metal = 0.35 if cid in ("mustang", "model3", "golf") else 0.0
    return pbr(name, tuple(c), rough=0.35 if metal else 0.3, metal=metal, coat=1.0)


def remap(ob, cid, C):
    """Matériaux réalistes par face : peinture de la voiture, puis colonne de palette (UV) pour le reste."""
    me = ob.data
    old = [m.name if m else "" for m in me.materials]
    uvl = me.uv_layers.active.data if me.uv_layers else None
    paint = paint_material(cid)
    mats, index, assign = [], {}, []

    def slot(m):
        if m.name not in index:
            index[m.name] = len(mats)
            mats.append(m)
        return index[m.name]
    for p in me.polygons:
        src = old[p.material_index] if p.material_index < len(old) else ""
        if src in ("PK_Palette", "V74L_Palette") and uvl is not None:
            ncol = P.NCOL if src == "PK_Palette" else lp_lib.NCOL
            names = P.NAMES if src == "PK_Palette" else [c[0] for c in lp_lib.PALETTE]
            u = uvl[p.loop_start].uv.x
            key = names[min(ncol - 1, max(0, int(u * ncol)))]
            if key in ("paint", "body"):
                target = paint
            else:
                target = C.get(key) or C["trim"]
        elif src == "PK_Paint":
            target = paint
        elif src in ("PK_LED", "V74L_LED"):
            target = C["led"]
        elif src in ("PK_Tail", "V74L_Tail"):
            target = C["tail"]
        elif src == "PK_Accent":
            target = C["accent"]
        elif src == "PK_SirenRed":
            target = C["siren_red"]
        elif src == "PK_SirenBlue":
            target = C["siren_blue"]
        else:
            target = C["trim"]
        assign.append(slot(target))
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", assign)
    me.update()


def place_car(cid, x, y, heading, z, target, C):
    """Copie réelle de la voiture (maillages copiés, matériaux réalistes), centrée en (x, y), avant tourné de
    `heading` degrés, roues à la hauteur z."""
    c = S.vehicle_collection(cid)
    lo, hi = S.bounds(c)
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    R = Matrix.Rotation(math.radians(heading), 4, 'Z')
    Mw = Matrix.Translation(Vector((x, y, z)) - R @ centre) @ R
    n_obj = 0
    for o in c.all_objects:
        if o.type != 'MESH' or o.hide_render:
            continue
        n = o.copy()
        n.data = o.data.copy()
        n.parent = None
        n.matrix_world = Mw @ S.world(o)
        n.name = "RL_%s_%02d" % (cid, n_obj)
        remap(n, cid, C)
        target.objects.link(n)
        n_obj += 1
    return n_obj


# ----------------------------------------------------------------- géométrie
def box(bm, c, s, rot=None):
    H.box(bm, Vector(c), s, rot)


def cylinder(bm, c, r, h, n=40, z0=0.0):
    pts = [(c[0] + r * math.cos(2 * math.pi * k / n), c[1] + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    H.extrude(bm, pts, 'z', z0, z0 + h)


def obj(bm, name, target, mat, shadow=True):
    o = H.bm_to_obj(bm, name, target, [mat])
    o.visible_shadow = shadow
    return o


def build_hall(M, target):
    X, Y, Hh = HALL_X, HALL_Y, HALL_H
    bm = bmesh.new()
    box(bm, (0, Y / 2, -0.05), (2 * X, Y, 0.1))
    obj(bm, "RL_Floor", target, M["floor"])
    # mur du fond (plâtre) et mur ouest ; lattes de bois devant le fond, rubans LED en haut et en bas des lattes
    bm = bmesh.new()
    box(bm, (0, Y + 0.15, Hh / 2), (2 * X + 0.6, 0.3, Hh))
    box(bm, (-X - 0.15, Y / 2, Hh / 2), (0.3, Y, Hh))
    obj(bm, "RL_Walls", target, M["plaster"])
    bm = bmesh.new()
    x = -12.0
    while x <= 12.0:
        box(bm, (x, Y - 0.08, Hh / 2 - 0.2), (0.07, 0.05, Hh - 1.2))
        x += 0.12
    obj(bm, "RL_WoodSlats", target, M["wood"])
    bm = bmesh.new()
    box(bm, (0, Y - 0.14, 0.62), (24.4, 0.02, 0.03))
    box(bm, (0, Y - 0.14, Hh - 0.58), (24.4, 0.02, 0.03))
    obj(bm, "RL_SlatLED", target, M["led"], shadow=False)
    # plafond, réglettes LED, rails de spots
    bm = bmesh.new()
    box(bm, (0, Y / 2, Hh + 0.15), (2 * X + 0.6, Y + 0.6, 0.3))
    obj(bm, "RL_Ceiling", target, M["ceiling"])
    bm = bmesh.new()
    for yy in (4.0, 10.0, 16.0, 22.0, 28.0):
        for xx in (-18.0, -9.0, 0.0, 9.0, 18.0):
            box(bm, (xx, yy, Hh - 0.02), (6.0, 0.12, 0.04))
    obj(bm, "RL_CeilingLED", target, M["led"], shadow=False)
    # façades vitrées : vitres (sans ombre, la lumière du jour passe) et montants noirs
    bm = bmesh.new()
    box(bm, (0, -0.02, Hh / 2), (2 * X, 0.02, Hh))
    box(bm, (X + 0.02, Y / 2, Hh / 2), (0.02, Y, Hh))
    obj(bm, "RL_Glazing", target, M["glass"], shadow=False)
    bm = bmesh.new()
    x = -X
    while x <= X + 0.01:
        box(bm, (x, -0.02, Hh / 2), (0.08, 0.16, Hh))
        x += 3.0
    y = 0.0
    while y <= Y + 0.01:
        box(bm, (X + 0.02, y, Hh / 2), (0.16, 0.08, Hh))
        y += 3.2
    for z in (0.04, 3.4, Hh - 0.05):
        box(bm, (0, -0.02, z), (2 * X, 0.16, 0.08))
        box(bm, (X + 0.02, Y / 2, z), (0.16, Y, 0.08))
    obj(bm, "RL_Mullions", target, M["mullion"])
    # poteaux ronds entre les deux zones
    bm = bmesh.new()
    for xx in (-12.0, 0.0, 12.0):
        cylinder(bm, (xx, 17.2), 0.32, Hh, n=32)
    obj(bm, "RL_Columns", target, M["plaster"])
    # volume du bâtiment vu de dehors : acrotère en panneaux sombres au-dessus des façades
    bm = bmesh.new()
    box(bm, (0, Y / 2, Hh + 0.85), (2 * X + 0.8, Y + 0.8, 1.1))
    obj(bm, "RL_Fascia", target, M["fascia"])


def build_premium(M, C, target, lights):
    """Plaques tournantes (alu brossé, dessus sombre), voitures, spots, bornes ; cordons autour de la Vision 74."""
    rnd = random.Random(7)
    for cid, x, y, heading in PREMIUM:
        big = cid == "vision74"
        R = 3.35 if big else 2.95
        bm = bmesh.new()
        cylinder(bm, (x, y), R, 0.15, n=64)
        obj(bm, "RL_Turntable_" + cid, target, M["alu"])
        bm = bmesh.new()
        cylinder(bm, (x, y), R - 0.06, 0.03, n=64, z0=0.15)
        obj(bm, "RL_TurntableTop_" + cid, target, M["turntop"])
        place_car(cid, x, y, heading, 0.18, target, C)
        # spot au plafond braqué sur la voiture (et son boîtier)
        sp = bpy.data.lights.get("RL_Spot_" + cid) or bpy.data.lights.new("RL_Spot_" + cid, 'SPOT')
        sp.energy = 2600.0 if big else 1700.0
        sp.spot_size = math.radians(38.0)
        sp.spot_blend = 0.45
        sp.shadow_soft_size = 0.25
        sp.color = (1.0, 0.95, 0.88)
        so = bpy.data.objects.new("RL_Spot_" + cid, sp)
        so.location = (x - 1.2, y - 1.6, HALL_H - 0.35)
        so.rotation_euler = (Vector((x, y, 0.6)) - so.location).to_track_quat('-Z', 'Y').to_euler()
        lights.objects.link(so)
        bm = bmesh.new()
        cylinder(bm, (x - 1.2, y - 1.6), 0.09, 0.28, n=16, z0=HALL_H - 0.3)
        obj(bm, "RL_SpotHousing_" + cid, target, M["fixture"])
        # borne d'information (écran vierge) à l'avant gauche de la plaque
        a = math.radians(heading + 135.0)
        bx, by = x + (R + 0.9) * math.cos(a), y + (R + 0.9) * math.sin(a)
        rot = Matrix.Rotation(a + math.pi / 2, 3, 'Z')
        bm = bmesh.new()
        box(bm, (bx, by, 0.8), (0.55, 0.06, 1.6), rot)
        obj(bm, "RL_Totem_" + cid, target, M["fixture"])
        bm = bmesh.new()
        off = rot @ Vector((0.0, -0.035, 0.0))
        box(bm, (bx + off.x, by + off.y, 1.05), (0.45, 0.005, 0.8), rot)
        obj(bm, "RL_TotemScreen_" + cid, target, M["screen"], shadow=False)
        if big:
            # poteaux chromés et cordons de velours tout autour
            n = 8
            posts = [(x + 4.3 * math.cos(2 * math.pi * k / n + 0.2), y + 4.3 * math.sin(2 * math.pi * k / n + 0.2))
                     for k in range(n)]
            bm = bmesh.new()
            for px, py in posts:
                cylinder(bm, (px, py), 0.025, 0.95, n=12)
                cylinder(bm, (px, py), 0.17, 0.03, n=24)
                cylinder(bm, (px, py), 0.05, 0.06, n=12, z0=0.95)
            obj(bm, "RL_Stanchions", target, M["chrome"])
            for k in range(n):
                (ax, ay), (bx2, by2) = posts[k], posts[(k + 1) % n]
                if k == 5:
                    continue                                  # passage vers la voiture
                cu = bpy.data.curves.new("RL_Rope%d" % k, 'CURVE')
                cu.dimensions = '3D'
                cu.bevel_depth = 0.018
                sp_ = cu.splines.new('BEZIER')
                sp_.bezier_points.add(2)
                mid = ((ax + bx2) / 2, (ay + by2) / 2, 0.72)
                for bp, co in zip(sp_.bezier_points, ((ax, ay, 0.92), mid, (bx2, by2, 0.92))):
                    bp.co = co
                    bp.handle_left_type = bp.handle_right_type = 'AUTO'
                ro = bpy.data.objects.new("RL_Rope%d" % k, cu)
                ro.data.materials.append(M["velvet"])
                target.objects.link(ro)
    return rnd


def build_stock(C, target):
    x = -15.5
    for cid in STOCK_A:
        place_car(cid, x, 21.0, -35.0, 0.0, target, C)
        x += 6.2
    x = -12.5
    for cid in STOCK_B:
        place_car(cid, x, 27.2, -35.0, 0.0, target, C)
        x += 6.8


def plant(bm_pot, bm_leaf, bm_trunk, x, y, rnd, h=1.9):
    cylinder(bm_pot, (x, y), 0.36, 0.72, n=28)
    cylinder(bm_trunk, (x, y), 0.03, h - 0.5, n=8, z0=0.7)
    for _ in range(70):
        th = rnd.uniform(0, 2 * math.pi)
        ph = rnd.uniform(-0.3, 1.0)
        r = rnd.uniform(0.25, 0.62)
        c = Vector((x + r * math.cos(th) * math.cos(ph), y + r * math.sin(th) * math.cos(ph),
                    h - 0.45 + 0.55 * math.sin(ph)))
        L = rnd.uniform(0.22, 0.32)
        rot = (Matrix.Rotation(th, 3, 'Z') @ Matrix.Rotation(rnd.uniform(-0.9, 0.9), 3, 'Y') @
               Matrix.Rotation(rnd.uniform(-0.6, 0.6), 3, 'X'))
        box(bm_leaf, c, (L, L * 0.45, 0.006), rot)


def build_props(M, target):
    rnd = random.Random(11)
    X, Y = HALL_X, HALL_Y
    # accueil : comptoir blanc à plateau bois, côté ouest
    bm = bmesh.new()
    box(bm, (-20.6, 21.0, 0.55), (0.9, 4.2, 1.1))
    box(bm, (-21.3, 21.0, 0.38), (0.6, 4.2, 0.76))
    obj(bm, "RL_Desk", target, M["desk"])
    bm = bmesh.new()
    box(bm, (-20.6, 21.0, 1.12), (1.0, 4.3, 0.04))
    obj(bm, "RL_DeskTop", target, M["wood"])
    # coin salon côté est : tapis, deux canapés, fauteuils, table basse
    bm = bmesh.new()
    box(bm, (20.0, 24.0, 0.005), (4.2, 5.2, 0.01))
    obj(bm, "RL_Rug", target, M["rug"])
    bm = bmesh.new()
    for sx in (18.3, 21.7):
        box(bm, (sx, 24.0, 0.22), (0.95, 2.6, 0.44))
        bx = sx - 0.38 if sx < 20 else sx + 0.38
        box(bm, (bx, 24.0, 0.55), (0.22, 2.6, 0.6))
        for sy in (22.85, 25.15):
            box(bm, (sx, sy, 0.45), (0.95, 0.2, 0.22))
    for sy in (21.7, 26.3):
        box(bm, (20.0, sy, 0.22), (1.0, 0.9, 0.44))
        box(bm, (20.0, sy + (0.38 if sy > 24 else -0.38), 0.55), (1.0, 0.18, 0.6))
    obj(bm, "RL_Sofas", target, M["fabric"])
    bm = bmesh.new()
    box(bm, (20.0, 24.0, 0.38), (1.1, 1.8, 0.04))
    obj(bm, "RL_CoffeeTable", target, M["wood"])
    bm = bmesh.new()
    for dx in (-0.45, 0.45):
        for dy in (-0.8, 0.8):
            box(bm, (20.0 + dx, 24.0 + dy, 0.18), (0.04, 0.04, 0.36))
    obj(bm, "RL_TableLegs", target, M["fixture"])
    # plantes en pot
    pot, leaf, trunk = bmesh.new(), bmesh.new(), bmesh.new()
    for px, py in ((-22.8, 1.3), (22.8, 1.3), (-22.8, 30.6), (22.8, 30.6), (-12.9, 17.2), (12.9, 17.2),
                   (-20.4, 24.6), (22.6, 20.6), (22.6, 27.8)):
        plant(pot, leaf, trunk, px, py, rnd)
    obj(pot, "RL_Pots", target, M["pot"])
    obj(leaf, "RL_Leaves", target, M["leaf"])
    obj(trunk, "RL_Trunks", target, M["trunk"])


def build_outside(M, target):
    """Place dallée devant la vitrine, trottoir, rue, immeubles en face, lampadaires."""
    X = HALL_X
    bm = bmesh.new()
    box(bm, (0, -7.5, -0.06), (140.0, 15.0, 0.1))
    box(bm, (X + 20.0, 16.0, -0.06), (40.0, 48.0, 0.1))
    obj(bm, "RL_Plaza", target, M["plaza"])
    bm = bmesh.new()
    box(bm, (0, -17.0, 0.03), (220.0, 4.0, 0.16))
    box(bm, (0, -33.5, 0.03), (220.0, 4.0, 0.16))
    obj(bm, "RL_Sidewalks", target, M["walk"])
    bm = bmesh.new()
    box(bm, (0, -25.25, -0.06), (220.0, 12.5, 0.1))
    obj(bm, "RL_Road", target, M["asphalt"])
    bm = bmesh.new()
    x = -100.0
    while x < 100.0:
        box(bm, (x, -25.25, 0.0), (3.0, 0.15, 0.012))
        x += 6.0
    obj(bm, "RL_RoadLines", target, M["line"])
    bm1, bm2 = bmesh.new(), bmesh.new()
    x = -80.0
    k = 0
    while x < 90.0:
        w = 18.0 + (k % 3) * 6.0
        h = 14.0 + ((k * 7) % 4) * 6.4
        box(bm1 if k % 2 == 0 else bm2, (x + w / 2, -48.0, h / 2), (w, 18.0, h))
        x += w + 4.0
        k += 1
    obj(bm1, "RL_Buildings1", target, M["building1"])
    obj(bm2, "RL_Buildings2", target, M["building2"])
    poles, heads = bmesh.new(), bmesh.new()
    for x in range(-60, 61, 20):
        cylinder(poles, (float(x), -18.3), 0.07, 6.0, n=12)
        box(poles, (x, -18.9, 5.95), (0.12, 1.3, 0.1))
        box(heads, (x, -19.4, 5.88), (0.3, 0.5, 0.04))
    obj(poles, "RL_LampPoles", target, M["mullion"])
    obj(heads, "RL_LampHeads", target, M["lamp"], shadow=False)
    # halos des lampadaires sur la chaussée et le trottoir (visibles au crépuscule)
    lights = col("RL_Lights")
    for x in range(-60, 61, 20):
        pl = bpy.data.lights.get("RL_Street%d" % x) or bpy.data.lights.new("RL_Street%d" % x, 'SPOT')
        pl.energy = 900.0
        pl.spot_size = math.radians(110.0)
        pl.spot_blend = 0.6
        pl.color = (1.0, 0.8, 0.55)
        o = bpy.data.objects.new("RL_Street%d" % x, pl)
        o.location = (float(x), -19.4, 5.8)
        lights.objects.link(o)


def sky_world(sun_elev_deg, strength=1.0):
    """Ciel physique (sans disque solaire : c'est la lampe soleil qui éclaire), soleil au sud-est."""
    scn = bpy.data.scenes[SCENE]
    w = bpy.data.worlds.get("RL_World") or bpy.data.worlds.new("RL_World")
    w.use_nodes = True
    nt = w.node_tree
    sky = next((n for n in nt.nodes if n.type == 'TEX_SKY'), None)
    if sky is None:
        for n in list(nt.nodes):
            nt.nodes.remove(n)
        out = nt.nodes.new("ShaderNodeOutputWorld")
        bg = nt.nodes.new("ShaderNodeBackground")
        sky = nt.nodes.new("ShaderNodeTexSky")
        nt.links.new(sky.outputs[0], bg.inputs["Color"])
        nt.links.new(bg.outputs[0], out.inputs[0])
        items = [i.identifier for i in sky.bl_rna.properties["sky_type"].enum_items]
        for cand in ("MULTIPLE_SCATTERING", "NISHITA", "SINGLE_SCATTERING"):
            if cand in items:
                sky.sky_type = cand
                break
    bg = next(n for n in nt.nodes if n.type == 'BACKGROUND')
    bg.inputs["Strength"].default_value = strength
    for attr, val in (("sun_disc", False), ("sun_elevation", math.radians(sun_elev_deg)),
                      ("sun_rotation", math.radians(135.0)), ("air_density", 1.0), ("dust_density", 1.6),
                      ("altitude", 30.0)):
        if hasattr(sky, attr):
            setattr(sky, attr, val)
    scn.world = w
    sun = bpy.data.lights.get("RL_Sun") or bpy.data.lights.new("RL_Sun", 'SUN')
    sun.energy = 3.2 if sun_elev_deg > 10 else 0.0
    sun.angle = math.radians(0.6)
    sun.color = (1.0, 0.95, 0.88)
    so = bpy.data.objects.get("RL_Sun") or bpy.data.objects.new("RL_Sun", sun)
    if so.name not in col("RL_Lights").objects:
        col("RL_Lights").objects.link(so)
    e = math.radians(sun_elev_deg)
    d = Vector((math.cos(e) * math.cos(math.radians(-50.0)), math.cos(e) * math.sin(math.radians(-50.0)), math.sin(e)))
    so.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()


def interior_lights(lights):
    """Plafonniers : grandes surfaces diffuses au-dessus des deux zones (en plus des réglettes émissives)."""
    for k, (x, y, sx, sy, e) in enumerate(((-12.0, 9.0, 18.0, 10.0, 1300.0), (12.0, 9.0, 18.0, 10.0, 1300.0),
                                           (-12.0, 24.0, 18.0, 12.0, 1200.0), (12.0, 24.0, 18.0, 12.0, 1200.0))):
        a = bpy.data.lights.get("RL_Ceil%d" % k) or bpy.data.lights.new("RL_Ceil%d" % k, 'AREA')
        a.shape = 'RECTANGLE'
        a.size, a.size_y = sx, sy
        a.energy = e
        a.color = (1.0, 0.97, 0.92)
        o = bpy.data.objects.new("RL_Ceil%d" % k, a)
        o.location = (x, y, HALL_H - 0.08)
        lights.objects.link(o)


def render_settings(res=(2560, 1440), samples=256):
    scn = bpy.data.scenes[SCENE]
    scn.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for dev in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = dev
            break
        except TypeError:
            continue
    prefs.refresh_devices()
    for d in prefs.devices:
        d.use = d.type != 'CPU'
    scn.cycles.device = 'GPU'
    scn.cycles.samples = samples
    scn.cycles.use_adaptive_sampling = True
    scn.cycles.use_denoising = True
    scn.cycles.max_bounces = 8
    scn.cycles.glossy_bounces = 4
    scn.cycles.transmission_bounces = 8
    scn.cycles.caustics_reflective = False
    scn.cycles.caustics_refractive = False
    scn.cycles.blur_glossy = 1.0
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    scn.view_settings.view_transform = 'AgX'
    try:
        scn.view_settings.look = 'AgX - Base Contrast'
    except TypeError:
        pass


def build():
    """(Re)construit tout le showroom réaliste dans la scène « ShowroomReal »."""
    if bpy.context.scene.name != SCENE:
        raise RuntimeError("scène active : %s (appeler showroom_real.use_scene() d'abord)" % bpy.context.scene.name)
    for name in ("RL_Hall", "RL_Cars", "RL_Props", "RL_Outside", "RL_Lights", "RL_Cameras"):
        clear(name)
    hall, cars, props, outside, lights, cams = (col(n) for n in ("RL_Hall", "RL_Cars", "RL_Props", "RL_Outside",
                                                                 "RL_Lights", "RL_Cameras"))
    M = materials()
    C = car_materials()
    build_hall(M, hall)
    build_premium(M, C, cars, lights)
    build_stock(C, cars)
    build_props(M, props)
    build_outside(M, outside)
    interior_lights(lights)
    sky_world(VIEWS[0][4])
    for name, pos, look, lens, sun, expo in VIEWS:
        cd = bpy.data.cameras.get("RL_" + name) or bpy.data.cameras.new("RL_" + name)
        cd.lens = lens
        cd.clip_start = 0.05
        cd.clip_end = 500.0
        cam = bpy.data.objects.new("RL_" + name, cd)
        cam.location = pos
        cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        cams.objects.link(cam)
    bpy.data.scenes[SCENE].camera = bpy.data.objects["RL_" + VIEWS[0][0]]
    render_settings()
    return len(cars.objects)


def render(folder, views=None, res=(2560, 1440), samples=256, prefix="ShowroomReel"):
    """Rendus Cycles des vues (toutes par défaut) : <prefix>_<vue>.png dans `folder`."""
    os.makedirs(folder, exist_ok=True)
    render_settings(res, samples)
    scn = bpy.data.scenes[SCENE]
    out = []
    for name, pos, look, lens, sun, expo in VIEWS:
        if views and name not in views:
            continue
        sky_world(sun, strength=1.0 if sun > 10 else 0.6)
        for o in bpy.data.collections["RL_Lights"].objects:
            if o.name.startswith("RL_Street"):
                o.hide_render = sun > 10                      # lampadaires allumés au crépuscule seulement
        scn.view_settings.exposure = expo
        scn.camera = bpy.data.objects["RL_" + name]
        path = os.path.join(folder, "%s_%s.png" % (prefix, name))
        scn.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out.append(path)
    return out
