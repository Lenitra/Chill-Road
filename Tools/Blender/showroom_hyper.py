"""Showroom hyper réaliste (Cycles) : grande galerie automobile pour toutes les voitures du jeu.
Scène « ShowroomHyper » de CarPack.blend, construite par build() :
- halle de 68 x 36 m sous 8 m, façade entièrement vitrée au sud (terrasse, pelouse, haie, arbres, ciel physique et
  soleil qui entre en biais), mur du fond en lattes de chêne avec gorges lumineuses, poutres acier, plafond à grands
  panneaux lumineux (lampes surfaciques visibles : reflets de studio sur les carrosseries), béton ciré à joints ;
- premier rang : les 10 voitures jouables sur plateaux laqués à liseré LED, un spot chaud par voiture, une borne avec
  le nom de la voiture ;
- second rang : les 11 voitures du trafic en épi sur une estrade en béton clair.
Les voitures sont des copies des collections du pack : chaque face reçoit un matériau réaliste selon sa colonne de
palette (peinture vernie à peau d'orange, paillettes pour les teintes métallisées, inox brossé, verre fumé, caoutchouc,
alu, chromes, optiques), puis le maillage est lissé par angle (les arêtes vives et les changements de matériau restent
nets) : les facettes disparaissent, les silhouettes restent celles du jeu.
Aucun appel à bpy.context.scene : tout passe par bpy.data.scenes[SCENE], la scène peut être construite et rendue en
arrière-plan (blender -b CarPack.blend --python ...).
Usage : build() ; render_views(dossier) (vues d'ensemble) ; render_cars(dossier) (un portrait par voiture).
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix
from bpy_extras.object_utils import world_to_camera_view
import hc_lib as H
import pack_lib as P
import lp_lib
import showroom as S
import showroom_real as R

SCENE = "ShowroomHyper"
HX, HY, HH = 34.0, 36.0, 8.0            # demi-longueur (x), profondeur (y, vitrage en y = 0), hauteur sous plafond

PLAYABLE = ["p50", "twingo", "miata", "ae86", "supra", "vision74", "jesko", "valkyrie", "dmc12", "mustang"]
TRAFFIC = ["fiat500", "civic", "golf", "corolla", "model3", "taxi", "police", "rav4", "kangoo", "f150", "transit"]
ROW1_Y, ROW1_HEAD, ROW1_STEP = 11.0, -50.0, 6.2
ROW2_Y, ROW2_HEAD, ROW2_STEP = 26.0, -40.0, 5.7
PLAT_R, PLAT_H = 2.9, 0.085
STAGE_H = 0.12
TITLES = {"vision74": "Hyundai N Vision 74", "dmc12": "DeLorean DMC-12"}
FILES = {"vision74": "Vision74"}

# Vues d'ensemble : (nom, position, point visé, focale, ouverture f/ (0 = sans flou), exposition)
VIEWS = [
    ("1_galerie", (32.6, 1.6, 1.7), (-3.0, 16.5, 0.9), 22, 0.0, -0.3),
    ("2_jouables", (33.0, 5.5, 0.95), (18.0, 14.0, 0.75), 24, 5.6, -0.3),
    ("3_trafic", (-21.0, 15.5, 1.55), (9.0, 26.5, 0.75), 24, 0.0, -0.3),
    ("4_vue_haute", (-33.3, 1.2, 5.6), (1.0, 18.0, 0.0), 17, 0.0, -0.3),
]
SUN_ELEV, SUN_AZ = 34.0, -62.0           # soleil (degrés) : il entre par le vitrage sud, en biais depuis l'est


def scene():
    scn = bpy.data.scenes.get(SCENE)
    if scn is None:
        scn = bpy.data.scenes.new(SCENE)
    return scn


def col(name):
    return H.get_col(name, scene().collection)


# ----------------------------------------------------------------- matériaux de la halle
def _tc(nt, kind="Object"):
    tc = nt.nodes.new("ShaderNodeTexCoord")
    return tc.outputs[kind]


def concrete_material():
    """Béton ciré sombre : nuances nuageuses, brillance irrégulière, joints de dilatation tous les 6 m."""
    m = bpy.data.materials.get("HY_Concrete") or bpy.data.materials.new("HY_Concrete")
    nt, b = R._nodes(m)
    co = _tc(nt)
    n1 = nt.nodes.new("ShaderNodeTexNoise")
    n1.inputs["Scale"].default_value = 0.9
    n1.inputs["Detail"].default_value = 9.0
    n1.inputs["Roughness"].default_value = 0.62
    nt.links.new(co, n1.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.40
    ramp.color_ramp.elements[0].color = (0.045, 0.047, 0.050, 1.0)
    ramp.color_ramp.elements[1].position = 0.66
    ramp.color_ramp.elements[1].color = (0.105, 0.108, 0.112, 1.0)
    nt.links.new(n1.outputs["Fac"], ramp.inputs["Fac"])
    br = nt.nodes.new("ShaderNodeTexBrick")
    br.offset = 0.0
    br.squash = 1.0
    br.inputs["Scale"].default_value = 1.0
    br.inputs["Brick Width"].default_value = 6.0
    br.inputs["Row Height"].default_value = 6.0
    br.inputs["Mortar Size"].default_value = 0.006
    br.inputs["Mortar Smooth"].default_value = 0.3
    br.inputs["Color1"].default_value = (1, 1, 1, 1)
    br.inputs["Color2"].default_value = (1, 1, 1, 1)
    br.inputs["Mortar"].default_value = (0.25, 0.25, 0.25, 1)
    nt.links.new(co, br.inputs["Vector"])
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(ramp.outputs["Color"], mul.inputs[6])
    nt.links.new(br.outputs["Color"], mul.inputs[7])
    nt.links.new(mul.outputs[2], H.sock(b, "Base Color"))
    n2 = nt.nodes.new("ShaderNodeTexNoise")
    n2.inputs["Scale"].default_value = 1.6
    n2.inputs["Detail"].default_value = 4.0
    nt.links.new(co, n2.inputs["Vector"])
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 0.3
    mr.inputs["From Max"].default_value = 0.7
    mr.inputs["To Min"].default_value = 0.11
    mr.inputs["To Max"].default_value = 0.32
    nt.links.new(n2.outputs["Fac"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], H.sock(b, "Roughness"))
    n3 = nt.nodes.new("ShaderNodeTexNoise")
    n3.inputs["Scale"].default_value = 60.0
    nt.links.new(co, n3.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.025
    nt.links.new(n3.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], H.sock(b, "Normal"))
    return m


def lawn_material():
    m = bpy.data.materials.get("HY_Lawn") or bpy.data.materials.new("HY_Lawn")
    nt, b = R._nodes(m)
    co = _tc(nt)
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 0.8
    nz.inputs["Detail"].default_value = 8.0
    nt.links.new(co, nz.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.035, 0.075, 0.018, 1.0)
    ramp.color_ramp.elements[1].color = (0.09, 0.16, 0.035, 1.0)
    nt.links.new(nz.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], H.sock(b, "Base Color"))
    H.sock(b, "Roughness").default_value = 0.95
    return m


def materials():
    M = {}
    M["concrete"] = concrete_material()
    M["stage"] = R.plaster_material("HY_StageConcrete", (0.20, 0.20, 0.195))
    M["plaster"] = R.plaster_material("HY_Plaster", (0.82, 0.82, 0.80))
    M["ceiling"] = R.plaster_material("HY_Ceiling", (0.78, 0.78, 0.77))
    M["wood"] = R.wood_material("HY_Oak")
    M["glass"] = R.pbr("HY_Glazing", (1.0, 1.0, 1.0), rough=0.0, transmission=1.0, ior=1.45)
    M["frame"] = R.pbr("HY_DarkAlu", (0.025, 0.026, 0.028), rough=0.35, metal=0.9)
    M["steel"] = R.pbr("HY_Steel", (0.05, 0.052, 0.055), rough=0.45, metal=0.7)
    M["plat"] = R.pbr("HY_PlatformLacquer", (0.018, 0.019, 0.021), rough=0.12, coat=1.0)
    M["platled"] = R.pbr("HY_PlatformLED", (1.0, 1.0, 1.0), emit=(1.0, 0.82, 0.6), strength=6.0)
    M["cove"] = R.pbr("HY_CoveLED", (1.0, 1.0, 1.0), emit=(1.0, 0.86, 0.68), strength=10.0)
    M["alu"] = R.pbr("HY_BrushedAlu", (0.75, 0.76, 0.77), rough=0.25, metal=1.0)
    M["screen"] = R.pbr("HY_Screen", (0.006, 0.007, 0.009), rough=0.04, coat=1.0)
    M["text"] = R.pbr("HY_Text", (1.0, 1.0, 1.0), emit=(1.0, 1.0, 1.0), strength=2.5)
    M["gold"] = R.pbr("HY_TextGold", (1.0, 0.6, 0.15), emit=(1.0, 0.55, 0.12), strength=2.0)
    M["pot"] = R.pbr("HY_Planter", (0.03, 0.03, 0.032), rough=0.3, coat=0.5)
    M["leaf"] = R.pbr("HY_Leaf", (0.045, 0.11, 0.03), rough=0.5)
    M["trunk"] = R.pbr("HY_Trunk", (0.10, 0.075, 0.05), rough=0.85)
    M["soil"] = R.pbr("HY_Soil", (0.03, 0.022, 0.016), rough=1.0)
    M["terrace"] = R.tiles_material("HY_Terrace", (0.48, 0.47, 0.44), (0.42, 0.41, 0.39), (0.25, 0.25, 0.24), 0.9,
                                    rough=(0.55, 0.8), mortar=0.006)
    M["lawn"] = lawn_material()
    M["hedge"] = R.pbr("HY_Hedge", (0.025, 0.06, 0.018), rough=0.9)
    return M


# ----------------------------------------------------------------- matériaux des voitures
def _orange_peel(nt, b, strength=0.012):
    """Peau d'orange du vernis : bruit fin sur la normale du vernis seulement."""
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 220.0
    nz.inputs["Detail"].default_value = 2.0
    nt.links.new(_tc(nt), nz.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    nt.links.new(nz.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], H.sock(b, "Coat Normal"))


def _flakes(nt, b, strength=0.35):
    """Paillettes des teintes métallisées : cellules de Voronoï très fines sur la normale de la couche de base."""
    vo = nt.nodes.new("ShaderNodeTexVoronoi")
    vo.inputs["Scale"].default_value = 1400.0
    nt.links.new(_tc(nt), vo.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.0005
    nt.links.new(vo.outputs["Distance"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], H.sock(b, "Normal"))


def car_paint(name, color, metallic=False, rough=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = R._nodes(m)
    H.sock(b, "Base Color").default_value = (*color, 1.0)
    H.sock(b, "Metallic").default_value = 0.7 if metallic else 0.0
    H.sock(b, "Roughness").default_value = rough if rough is not None else (0.32 if metallic else 0.38)
    H.sock(b, "Coat Weight").default_value = 1.0
    H.sock(b, "Coat Roughness").default_value = 0.025
    H.sock(b, "Coat IOR").default_value = 1.5
    _orange_peel(nt, b)
    if metallic:
        _flakes(nt, b)
    return m


def brushed_steel(name):
    """Inox brossé (DeLorean) : rugosité striée dans le sens de la longueur."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = R._nodes(m)
    H.sock(b, "Base Color").default_value = (0.66, 0.665, 0.67, 1.0)
    H.sock(b, "Metallic").default_value = 1.0
    co = _tc(nt)
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (2.0, 300.0, 300.0)
    nt.links.new(co, mp.inputs["Vector"])
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 1.0
    nz.inputs["Detail"].default_value = 6.0
    nt.links.new(mp.outputs["Vector"], nz.inputs["Vector"])
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["To Min"].default_value = 0.16
    mr.inputs["To Max"].default_value = 0.34
    nt.links.new(nz.outputs["Fac"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], H.sock(b, "Roughness"))
    return m


def textured_plastic(name, color, rough=0.62, scale=180.0, strength=0.06):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt, b = R._nodes(m)
    H.sock(b, "Base Color").default_value = (*color, 1.0)
    H.sock(b, "Roughness").default_value = rough
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = scale
    nt.links.new(_tc(nt), nz.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    nt.links.new(nz.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], H.sock(b, "Normal"))
    return m


def car_materials():
    C = {}
    C["glass"] = R.pbr("HY_CarGlass", (0.006, 0.008, 0.01), rough=0.0, coat=1.0)
    C["tire"] = textured_plastic("HY_Rubber", (0.016, 0.016, 0.018), rough=0.72, scale=600.0, strength=0.04)
    C["rim"] = R.pbr("HY_Alloy", (0.80, 0.81, 0.82), rough=0.18, metal=1.0)
    C["chrome"] = R.pbr("HY_Chrome", (0.94, 0.94, 0.95), rough=0.03, metal=1.0)
    C["trim"] = textured_plastic("HY_SatinBlack", (0.022, 0.022, 0.025), rough=0.45)
    C["dark"] = R.pbr("HY_DarkPlastic", (0.010, 0.010, 0.012), rough=0.55)
    C["black"] = R.pbr("HY_GlossBlack", (0.012, 0.012, 0.014), rough=0.15, coat=1.0)
    C["lens"] = R.pbr("HY_Reflector", (0.9, 0.91, 0.93), rough=0.06, metal=0.85, coat=1.0)
    C["red"] = R.pbr("HY_RedGlass", (0.35, 0.008, 0.006), rough=0.05, coat=1.0, emit=(1.0, 0.03, 0.02), strength=0.8)
    C["amber"] = R.pbr("HY_AmberGlass", (0.8, 0.32, 0.02), rough=0.05, coat=1.0, emit=(1.0, 0.4, 0.05), strength=0.15)
    C["white"] = car_paint("HY_PaintWhite", (0.80, 0.80, 0.79))
    C["yellow"] = car_paint("HY_PaintYellow", (0.86, 0.52, 0.02))
    C["plate"] = R.pbr("HY_Plate", (0.85, 0.85, 0.83), rough=0.35)
    C["blue"] = car_paint("HY_PaintBlue", (0.02, 0.06, 0.32))
    C["grey"] = textured_plastic("HY_GreyPlastic", (0.10, 0.10, 0.105), rough=0.6)
    C["orange"] = car_paint("HY_PaintOrange", (0.8, 0.22, 0.01))
    C["green"] = car_paint("HY_PaintGreen", (0.03, 0.2, 0.06))
    C["led"] = R.pbr("HY_Headlight", (0.92, 0.92, 0.92), rough=0.03, coat=1.0, emit=(1.0, 0.95, 0.88), strength=3.0)
    C["tail"] = C["red"]
    C["accent"] = car_paint("HY_PaintLime", (0.40, 0.78, 0.03))
    C["siren_red"] = R.pbr("HY_SirenRed", (0.5, 0.01, 0.01), rough=0.1, emit=(1.0, 0.03, 0.02), strength=2.5)
    C["siren_blue"] = R.pbr("HY_SirenBlue", (0.02, 0.05, 0.5), rough=0.1, emit=(0.05, 0.2, 1.0), strength=2.5)
    return C


METALLIC = {"mustang", "model3", "golf", "vision74", "corolla"}


def paint_material(cid):
    name = "HY_Paint_" + cid
    if cid == "dmc12":
        return brushed_steel(name)
    if cid == "vision74":
        return car_paint(name, (0.70, 0.72, 0.74), metallic=True, rough=0.28)
    c = tuple(P.resolve(cid).get("color", (0.6, 0.6, 0.6)))
    return car_paint(name, c, metallic=cid in METALLIC)


def remap(ob, cid, C):
    """Matériau réaliste de chaque face : peinture de la voiture, puis colonne de palette (UV) pour le reste."""
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
            key = names[min(ncol - 1, max(0, int(uvl[p.loop_start].uv.x * ncol)))]
            target = paint if key in ("paint", "body") else (C.get(key) or C["trim"])
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


def smooth_by_angle(me, angle=34.0):
    """Lissage des normales : faces lisses, arêtes vives au-delà de `angle` degrés et entre deux matériaux."""
    bm = bmesh.new()
    bm.from_mesh(me)
    th = math.radians(angle)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) != 2:
            e.smooth = False
            continue
        a, b = e.link_faces
        e.smooth = a.material_index == b.material_index and e.calc_face_angle(0.0) <= th
    bm.to_mesh(me)
    bm.free()


def place_car(cid, x, y, heading, z, target, C):
    """Copie réelle de la voiture centrée en (x, y), avant tourné de `heading` degrés, roues à la hauteur z ; renvoie
    la liste des objets créés."""
    c = S.vehicle_collection(cid)
    lo, hi = S.bounds(c)
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    Rz = Matrix.Rotation(math.radians(heading), 4, 'Z')
    Mw = Matrix.Translation(Vector((x, y, z)) - Rz @ centre) @ Rz
    out = []
    for o in c.all_objects:
        if o.type != 'MESH' or o.hide_render:
            continue
        n = o.copy()
        n.data = o.data.copy()
        n.parent = None
        n.matrix_world = Mw @ S.world(o)
        n.name = "HY_%s_%02d" % (cid, len(out))
        n["car"] = cid
        remap(n, cid, C)
        smooth_by_angle(n.data)
        target.objects.link(n)
        out.append(n)
    return out


def car_bounds(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in objs:
        M = o.matrix_basis
        for v in o.data.vertices:
            w = M @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


# ----------------------------------------------------------------- géométrie
def box(bm, c, s, rot=None):
    H.box(bm, Vector(c), s, rot)


def cyl(bm, c, r, h, n=48, z0=0.0):
    R.cylinder(bm, c, r, h, n=n, z0=z0)


def obj(bm, name, target, mat, shadow=True, smooth=False):
    o = H.bm_to_obj(bm, name, target, [mat], smooth=smooth)
    o.visible_shadow = shadow
    return o


def build_hall(M, target):
    bm = bmesh.new()
    box(bm, (0, HY / 2, -0.05), (2 * HX, HY, 0.1))
    obj(bm, "HY_Floor", target, M["concrete"])
    # murs : fond, est, ouest (plâtre) ; lattes de chêne sur tout le fond, gorges lumineuses en haut et en bas
    bm = bmesh.new()
    box(bm, (0, HY + 0.2, HH / 2), (2 * HX + 0.8, 0.4, HH))
    for x in (-HX - 0.2, HX + 0.2):
        box(bm, (x, HY / 2, HH / 2), (0.4, HY, HH))
    obj(bm, "HY_Walls", target, M["plaster"])
    bm = bmesh.new()
    x = -HX + 0.6
    while x < HX - 0.6:
        box(bm, (x, HY - 0.07, HH / 2 - 0.35), (0.045, 0.06, HH - 1.6))
        x += 0.095
    obj(bm, "HY_OakBattens", target, M["wood"])
    bm = bmesh.new()
    box(bm, (0, HY - 0.16, 0.5), (2 * HX - 1.2, 0.03, 0.03))
    box(bm, (0, HY - 0.16, HH - 1.12), (2 * HX - 1.2, 0.03, 0.03))
    obj(bm, "HY_CoveLED", target, M["cove"], shadow=False)
    bm = bmesh.new()
    box(bm, (0, HY - 0.25, 0.3), (2 * HX, 0.5, 0.6))                   # banquette basse sous les lattes
    obj(bm, "HY_Plinth", target, M["steel"])
    # plafond, poutres acier, grands panneaux lumineux (lampes surfaciques, voir lights())
    bm = bmesh.new()
    box(bm, (0, HY / 2, HH + 0.15), (2 * HX + 0.8, HY + 0.6, 0.3))
    obj(bm, "HY_Ceiling", target, M["ceiling"])
    bm = bmesh.new()
    x = -HX + 4.25
    while x < HX:
        box(bm, (x, HY / 2, HH - 0.35), (0.35, HY, 0.7))
        x += 8.5
    obj(bm, "HY_Beams", target, M["steel"])
    # façade vitrée sud
    bm = bmesh.new()
    box(bm, (0, -0.02, HH / 2), (2 * HX, 0.02, HH))
    obj(bm, "HY_Glazing", target, M["glass"], shadow=False)
    bm = bmesh.new()
    x = -HX
    while x <= HX + 0.01:
        box(bm, (x, -0.02, HH / 2), (0.07, 0.18, HH))
        x += 2.43
    for z in (0.04, 3.6, HH - 0.05):
        box(bm, (0, -0.02, z), (2 * HX, 0.18, 0.08))
    obj(bm, "HY_Mullions", target, M["frame"])


def lights(target):
    scn = scene()
    # ciel physique (sans disque) et soleil
    w = bpy.data.worlds.get("HY_World") or bpy.data.worlds.new("HY_World")
    w.use_nodes = True
    nt = w.node_tree
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
    for attr, val in (("sun_disc", False), ("sun_elevation", math.radians(SUN_ELEV)),
                      ("sun_rotation", math.radians(SUN_AZ + 90.0)), ("air_density", 1.0), ("dust_density", 1.2),
                      ("altitude", 100.0)):
        if hasattr(sky, attr):
            setattr(sky, attr, val)
    bg.inputs["Strength"].default_value = 0.9
    scn.world = w
    sun = bpy.data.lights.get("HY_Sun") or bpy.data.lights.new("HY_Sun", 'SUN')
    sun.energy = 4.0
    sun.angle = math.radians(0.55)
    sun.color = (1.0, 0.95, 0.87)
    so = bpy.data.objects.new("HY_Sun", sun)
    e, a = math.radians(SUN_ELEV), math.radians(SUN_AZ)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    so.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    target.objects.link(so)
    # panneaux lumineux du plafond, entre les poutres : visibles par la caméra et dans les reflets
    k = 0
    x = -HX + 8.5
    while x < HX - 1.0:
        for y in (6.0, 15.0, 24.0, 32.0):
            a = bpy.data.lights.get("HY_Panel%d" % k) or bpy.data.lights.new("HY_Panel%d" % k, 'AREA')
            a.shape = 'RECTANGLE'
            a.size, a.size_y = 6.6, 3.2
            a.energy = 520.0
            a.color = (1.0, 0.975, 0.95)
            o = bpy.data.objects.new("HY_Panel%d" % k, a)
            o.location = (x, y, HH - 0.02)
            o.visible_camera = True
            o.visible_glossy = True
            target.objects.link(o)
            k += 1
        x += 8.5


def spot(target, name, at, aim, energy=1400.0, size=34.0):
    sp = bpy.data.lights.get(name) or bpy.data.lights.new(name, 'SPOT')
    sp.energy = energy
    sp.spot_size = math.radians(size)
    sp.spot_blend = 0.55
    sp.shadow_soft_size = 0.35
    sp.color = (1.0, 0.9, 0.78)
    o = bpy.data.objects.new(name, sp)
    o.location = at
    o.rotation_euler = (Vector(aim) - Vector(at)).to_track_quat('-Z', 'Y').to_euler()
    o.visible_camera = False
    target.objects.link(o)
    return o


def text_obj(body, size, mat, target, name):
    cu = bpy.data.curves.get(name) or bpy.data.curves.new(name, 'FONT')
    cu.body = body
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.extrude = 0.0015
    cu.materials.clear()
    cu.materials.append(mat)
    o = bpy.data.objects.new(name, cu)
    target.objects.link(o)
    return o


def title(cid):
    return TITLES.get(cid) or P.resolve(cid).get("name", cid)


def build_playable(M, C, cars, props, lts):
    """Plateaux laqués à liseré LED, voitures, spots, bornes au nom de la voiture."""
    out = {}
    pods, rings = bmesh.new(), bmesh.new()
    totems, screens = bmesh.new(), bmesh.new()
    n = len(PLAYABLE)
    for i, cid in enumerate(PLAYABLE):
        x = ROW1_STEP * (i - (n - 1) / 2)
        y = ROW1_Y + 0.0035 * x * x
        cyl(pods, (x, y), PLAT_R, PLAT_H - 0.02, n=96, z0=0.02)
        cyl(rings, (x, y), PLAT_R - 0.07, 0.02, n=96)
        objs = place_car(cid, x, y, ROW1_HEAD, PLAT_H, cars, C)
        out[cid] = (objs, ROW1_HEAD)
        spot(lts, "HY_Spot_" + cid, (x + 2.2, y - 3.0, HH - 0.8), (x, y, 0.5))
        # borne : derrière à droite de la voiture (vue depuis la vitrine)
        a = math.radians(ROW1_HEAD + 125.0)
        bx, by = x + (PLAT_R + 0.55) * math.cos(a), y + (PLAT_R + 0.55) * math.sin(a)
        face = math.radians(-90.0)                                           # écran tourné vers la vitrine
        rot = Matrix.Rotation(face + math.pi / 2, 3, 'Z')
        box(totems, (bx, by, 0.62), (0.42, 0.05, 1.24), rot)
        off = rot @ Vector((0.0, -0.028, 0.0))
        box(screens, (bx + off.x, by + off.y, 0.78), (0.36, 0.004, 0.62), rot)
        t = text_obj(title(cid), 0.034, M["text"], props, "HY_Title_" + cid)
        t.location = (bx + off.x * 1.2, by + off.y * 1.2, 0.98)
        t.rotation_euler = (math.radians(90.0), 0.0, face + math.pi / 2)
        t2 = text_obj("CHILL ROAD", 0.022, M["gold"], props, "HY_Brand_" + cid)
        t2.location = (bx + off.x * 1.2, by + off.y * 1.2, 1.04)
        t2.rotation_euler = (math.radians(90.0), 0.0, face + math.pi / 2)
    obj(pods, "HY_Platforms", props, M["plat"], smooth=False)
    obj(rings, "HY_PlatformLED", props, M["platled"], shadow=False)
    obj(totems, "HY_Totems", props, M["alu"])
    obj(screens, "HY_TotemScreens", props, M["screen"])
    return out


def build_traffic(M, C, cars, props):
    out = {}
    bm = bmesh.new()
    box(bm, (0, ROW2_Y, STAGE_H / 2), (2 * HX - 3.0, 7.4, STAGE_H))
    obj(bm, "HY_Stage", props, M["stage"])
    n = len(TRAFFIC)
    for i, cid in enumerate(TRAFFIC):
        x = ROW2_STEP * (i - (n - 1) / 2)
        out[cid] = (place_car(cid, x, ROW2_Y, ROW2_HEAD, STAGE_H, cars, C), ROW2_HEAD)
    return out


def tree(bm_leaf, bm_trunk, x, y, rnd, h=3.2, spread=1.2, leaves=420, leaf=0.16):
    """Arbre : tronc et houppier fait de petites feuilles orientées au hasard."""
    cyl(bm_trunk, (x, y), 0.07, h * 0.7, n=10)
    for _ in range(leaves):
        th = rnd.uniform(0, 2 * math.pi)
        ph = rnd.uniform(-0.4, 1.2)
        r = spread * rnd.uniform(0.35, 1.0)
        c = Vector((x + r * math.cos(th) * math.cos(ph), y + r * math.sin(th) * math.cos(ph),
                    h * 0.72 + 0.75 * spread * math.sin(ph)))
        L = leaf * rnd.uniform(0.8, 1.3)
        rot = (Matrix.Rotation(th, 3, 'Z') @ Matrix.Rotation(rnd.uniform(-1.0, 1.0), 3, 'Y') @
               Matrix.Rotation(rnd.uniform(-0.8, 0.8), 3, 'X'))
        box(bm_leaf, c, (L, L * 0.5, 0.004), rot)


def build_props(M, target):
    rnd = random.Random(5)
    pots, soil, leaf, trunk = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    for px, py in ((-32.2, 19.0), (32.2, 19.0), (-32.2, 33.0), (32.2, 33.0)):
        cyl(pots, (px, py), 0.75, 0.85, n=40)
        cyl(soil, (px, py), 0.7, 0.02, n=40, z0=0.84)
        tree(leaf, trunk, px, py, rnd, h=3.6, spread=1.25)
    obj(pots, "HY_Planters", target, M["pot"])
    obj(soil, "HY_PlanterSoil", target, M["soil"])
    obj(leaf, "HY_Leaves", target, M["leaf"])
    obj(trunk, "HY_Trunks", target, M["trunk"])


def build_outside(M, target):
    rnd = random.Random(9)
    bm = bmesh.new()
    box(bm, (0, -6.0, -0.06), (2 * HX + 20.0, 12.0, 0.1))
    obj(bm, "HY_Terrace", target, M["terrace"])
    bm = bmesh.new()
    box(bm, (0, -60.0, -0.08), (400.0, 96.0, 0.1))
    obj(bm, "HY_Lawn", target, M["lawn"])
    bm = bmesh.new()
    box(bm, (0, -13.0, 0.55), (2 * HX + 20.0, 1.1, 1.1))
    obj(bm, "HY_Hedge", target, M["hedge"])
    leaf, trunk = bmesh.new(), bmesh.new()
    x = -HX - 6.0
    while x < HX + 8.0:
        tree(leaf, trunk, x + rnd.uniform(-1.5, 1.5), -21.0 + rnd.uniform(-2.0, 2.0), rnd, h=7.5, spread=2.6,
             leaves=700, leaf=0.32)
        x += 9.0
    obj(leaf, "HY_OutLeaves", target, M["leaf"])
    obj(trunk, "HY_OutTrunks", target, M["trunk"])


# ----------------------------------------------------------------- caméras et rendu
def camera(name, pos, look, lens, fstop=0.0, focus=None, target=None):
    cd = bpy.data.cameras.get(name) or bpy.data.cameras.new(name)
    cd.lens = lens
    cd.sensor_width = 36.0
    cd.clip_start = 0.05
    cd.clip_end = 600.0
    cd.dof.use_dof = fstop > 0.0
    if fstop > 0.0:
        cd.dof.aperture_fstop = fstop
        cd.dof.focus_distance = focus if focus else (Vector(look) - Vector(pos)).length
    o = bpy.data.objects.get(name)
    if o is None:
        o = bpy.data.objects.new(name, cd)
        (target or col("HY_Cameras")).objects.link(o)
    o.data = cd
    rot = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_matrix().to_4x4()
    o.matrix_world = Matrix.Translation(Vector(pos)) @ rot
    return o


def frame_car(cid, objs, heading, lens, rel_az=-40.0, elev=7.0, fill=0.74, fstop=4.0):
    """Caméra de portrait : 3/4 avant (azimut relatif au cap de la voiture), voiture remplissant `fill` du cadre."""
    scn = scene()
    lo, hi = car_bounds(objs)
    target = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z + (hi.z - lo.z) * 0.42))
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    a, e = math.radians(heading + rel_az), math.radians(elev)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    dist = (hi - lo).length * 1.6
    name = "HY_Car_" + cid
    for _ in range(7):
        cam = camera(name, target + d * dist, target, lens)
        pts = [world_to_camera_view(scn, cam, c) for c in corners]
        span = max(max(p.x for p in pts) - min(p.x for p in pts), max(p.y for p in pts) - min(p.y for p in pts))
        dist *= span / fill
    return camera(name, target + d * dist, target, lens, fstop=fstop, focus=dist)


def render_settings(res=(2560, 1440), samples=384):
    scn = scene()
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
    cy = scn.cycles
    cy.device = 'GPU'
    cy.samples = samples
    cy.use_adaptive_sampling = True
    cy.adaptive_threshold = 0.012
    cy.use_denoising = True
    cy.max_bounces = 12
    cy.diffuse_bounces = 4
    cy.glossy_bounces = 6
    cy.transmission_bounces = 8
    cy.caustics_reflective = False
    cy.caustics_refractive = False
    cy.blur_glossy = 0.6
    cy.sample_clamp_indirect = 8.0
    try:
        cy.use_light_tree = True
    except AttributeError:
        pass
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    scn.render.image_settings.file_format = 'PNG'
    scn.view_settings.view_transform = 'AgX'
    for cand in ('AgX - Medium High Contrast', 'AgX - Base Contrast'):
        try:
            scn.view_settings.look = cand
            break
        except TypeError:
            pass


def build():
    """(Re)construit tout le showroom hyper réaliste dans la scène « ShowroomHyper » ; renvoie {id: (objets, cap)}."""
    scn = scene()
    for name in ("HY_Hall", "HY_Cars", "HY_Props", "HY_Outside", "HY_Lights", "HY_Cameras"):
        R.clear(name)
    hall, cars, props, outside, lts, cams = (col(n) for n in ("HY_Hall", "HY_Cars", "HY_Props", "HY_Outside",
                                                              "HY_Lights", "HY_Cameras"))
    for o in list(lts.objects) + list(cams.objects) + list(props.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = materials()
    C = car_materials()
    build_hall(M, hall)
    placed = build_playable(M, C, cars, props, lts)
    placed.update(build_traffic(M, C, cars, props))
    build_props(M, props)
    build_outside(M, outside)
    lights(lts)
    render_settings()
    for name, pos, look, lens, fstop, expo in VIEWS:
        camera("HY_" + name, pos, look, lens, fstop=fstop, target=cams)
    scn.camera = bpy.data.objects["HY_" + VIEWS[0][0]]
    return placed


def _render(path):
    scn = scene()
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=SCENE)
    return path


def render_views(folder, views=None, res=(2560, 1440), samples=384):
    os.makedirs(folder, exist_ok=True)
    render_settings(res, samples)
    scn = scene()
    out = []
    for name, pos, look, lens, fstop, expo in VIEWS:
        if views and name not in views:
            continue
        scn.view_settings.exposure = expo
        scn.camera = bpy.data.objects["HY_" + name]
        out.append(_render(os.path.join(folder, "%s.png" % name)))
    return out


def render_cars(folder, ids=None, res=(1920, 1080), samples=320, log=None):
    """Un portrait par voiture (3/4 avant, flou d'arrière-plan) : <Fichier>.png dans `folder`."""
    os.makedirs(folder, exist_ok=True)
    render_settings(res, samples)
    scn = scene()
    scn.view_settings.exposure = 0.0
    placed = {}
    for o in bpy.data.collections["HY_Cars"].objects:
        placed.setdefault(o["car"], []).append(o)
    out = []
    for cid in (ids or PLAYABLE + TRAFFIC):
        objs = placed[cid]
        heading = ROW1_HEAD if cid in PLAYABLE else ROW2_HEAD
        cam = frame_car(cid, objs, heading, lens=50.0 if cid in PLAYABLE else 45.0)
        scn.camera = cam
        name = FILES.get(cid) or P.resolve(cid).get("file", cid)
        out.append(_render(os.path.join(folder, "%s.png" % name)))
        if log:
            log(out[-1])
    return out
