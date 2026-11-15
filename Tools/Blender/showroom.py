"""Showroom : tous les véhicules du jeu exposés dans une halle low poly (même DA à plat que les voitures), pour des
rendus de présentation. Scène « Showroom » de CarPack.blend, construite par build() :
- premier rang : les 9 voitures jouables (Vision 74 comprise, liée depuis Vision74.blend) sur des podiums octogonaux
  sombres cerclés d'or (l'or de l'interface du jeu) ;
- deuxième rang : les voitures du trafic en épi sur une estrade, les deux-roues au bout ;
- troisième rang : utilitaires et camions en épi ;
- le long du mur du fond : bus, car, semi-remorque et citerne de profil.
Les véhicules sont des instances des collections PK_<id> (teinte de carrosserie = couleur de l'instance, comme
pack_lib.show) : rien n'est copié, un véhicule reconstruit apparaît tel quel au prochain rendu.
Usage (Blender MCP, appels séparés) : showroom.use_scene() ; puis showroom.build() ; puis showroom.render(dossier).
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix
import hc_lib as H
import pack_lib as P
import pack_cars, pack_utility, play_cars  # noqa: F401  (remplissent P.CARS)

SCENE = "Showroom"
V74_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Vision74.blend")
V74_COL = "V74_Low"

HERO = ["p50", "miata", "ae86", "supra", "jesko", "vision74", "valkyrie", "mustang", "dmc12"]
CARS = ["fiat500", "civic", "golf", "corolla", "model3", "taxi", "police", "rav4", "kangoo", "f150", "transit"]
BIKES = ["moto", "scooter"]
TRUCKS = ["tractor", "boxvan", "ambulance", "towtruck", "firetruck", "boxtruck", "mixer", "garbage"]
LONG = ["bus", "coach", "semi", "tanker"]

GOLD = (1.0, 0.52, 0.05)          # or de l'interface du jeu (linéaire)
PODIUM_R, PODIUM_H = 3.15, 0.22

# Vues : (nom, position caméra, point visé, focale)
VIEWS = [
    ("1_vue_generale", (-12.0, -46.0, 27.0), (1.5, 16.5, 0.0), 30),
    ("2_jouables", (5.0, -12.5, 1.9), (-3.5, 3.0, 1.0), 24),
    ("3_trafic", (31.0, -6.0, 6.5), (-4.0, 20.0, 1.2), 30),
    ("4_poids_lourds", (-40.0, 8.0, 9.0), (4.0, 30.0, 2.0), 32),
    ("5_plan", (1.0, -9.0, 82.0), (1.0, 19.5, 0.0), 35),
]


def use_scene():
    """Crée la scène du showroom et l'affiche (à appeler seul : le contexte suit au prochain appel)."""
    scn = bpy.data.scenes.get(SCENE) or bpy.data.scenes.new(SCENE)
    for win in bpy.context.window_manager.windows:
        if win.scene != scn:
            win.scene = scn
    return scn


def check_scene():
    if bpy.context.scene.name != SCENE:
        raise RuntimeError("scène active : %s (appeler showroom.use_scene() d'abord)" % bpy.context.scene.name)
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


def col(name):
    return H.get_col(name, bpy.data.scenes[SCENE].collection)


# ----------------------------------------------------------------- véhicules
def vehicle_collection(cid):
    """Collection d'un véhicule : PK_<id> du pack, ou V74_Low liée depuis Vision74.blend pour la Vision 74."""
    if cid == "vision74":
        c = bpy.data.collections.get(V74_COL)
        if c is None:
            with bpy.data.libraries.load(V74_FILE, link=True) as (src, dst):
                dst.collections = [V74_COL]
            c = bpy.data.collections.get(V74_COL)
        return c
    return bpy.data.collections["PK_" + cid]


def world(o):
    """Matrice monde recalculée depuis les transformations locales : la matrix_world des objets d'une collection
    exclue de la couche de vue n'est jamais réévaluée (les roues, placées au moyeu après leur création, y restent à
    l'origine)."""
    m = o.matrix_basis.copy()
    if o.parent:
        m = world(o.parent) @ o.matrix_parent_inverse @ m
    return m


def bounds(c):
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in c.all_objects:
        if o.type != 'MESH' or o.hide_render:
            continue
        for p in o.bound_box:
            w = world(o) @ Vector(p)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def footprint(cid, heading):
    """Largeur occupée le long de X par un véhicule tourné de `heading` degrés."""
    lo, hi = bounds(vehicle_collection(cid))
    L, W = hi.x - lo.x, hi.y - lo.y
    a = math.radians(heading)
    return abs(L * math.cos(a)) + abs(W * math.sin(a))


def place(cid, x, y, heading, z, target):
    """Instance du véhicule centrée en (x, y), avant (+X) tourné de `heading` degrés, roues à la hauteur z."""
    c = vehicle_collection(cid)
    lo, hi = bounds(c)
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    R = Matrix.Rotation(math.radians(heading), 4, 'Z')
    e = bpy.data.objects.new("SR_" + cid, None)
    e.instance_type = 'COLLECTION'
    e.instance_collection = c
    e.matrix_world = Matrix.Translation(Vector((x, y, z)) - R @ centre) @ R
    spec = P.CARS.get(cid)
    if spec:
        e.color = P.lin_color(P.resolve(cid).get("color", (0.6, 0.6, 0.6)))
    target.objects.link(e)
    return e


def row(ids, x0, y, heading, z, target, gap):
    """Rangée le long de X à partir de x0, chaque véhicule prenant sa largeur au sol + `gap`."""
    x = x0
    for cid in ids:
        w = footprint(cid, heading)
        place(cid, x + w / 2, y, heading, z, target)
        x += w + gap
    return x


# ----------------------------------------------------------------- halle
def materials():
    M = {}
    for name, c, rough, emit, strength in (
            ("floor", (0.085, 0.09, 0.10), 0.10, None, 0.0),      # sol sombre poli (reflets des voitures)
            ("stage", (0.20, 0.21, 0.23), 0.30, None, 0.0),       # estrades des rangées, plus claires
            ("podium", (0.030, 0.034, 0.042), 0.25, None, 0.0),   # podiums anthracite
            ("wall", (0.34, 0.35, 0.37), 0.8, None, 0.0),
            ("ceiling", (0.10, 0.105, 0.115), 0.9, None, 0.0),
            ("skirt", (0.05, 0.055, 0.06), 0.6, None, 0.0),       # plinthes sombres
            ("strip", (1.0, 1.0, 1.0), 0.5, (1.0, 0.97, 0.92), 5.0),  # bandeaux et panneaux lumineux
            ("gold", GOLD, 0.4, GOLD, 4.0)):                      # liseré or des podiums
        M[name], _ = H.principled("SR_" + name, c, rough=rough, emit=emit, strength=strength)
    return M


def octagon_ring(bm, cx, cy, r_out, r_in, z0, z1, n=8):
    ang = [math.pi / n + 2 * math.pi * k / n for k in range(n)]
    outer = [(cx + r_out * math.cos(a), cy + r_out * math.sin(a)) for a in ang]
    if r_in <= 0:
        H.extrude(bm, outer, 'z', z0, z1)
        return
    for k in range(n):
        a0, a1 = ang[k], ang[(k + 1) % n]
        quad = [(cx + r_out * math.cos(a0), cy + r_out * math.sin(a0)), (cx + r_out * math.cos(a1), cy + r_out * math.sin(a1)),
                (cx + r_in * math.cos(a1), cy + r_in * math.sin(a1)), (cx + r_in * math.cos(a0), cy + r_in * math.sin(a0))]
        H.extrude(bm, quad, 'z', z0, z1)


def build_hall(M, target, podiums):
    """Sol, estrades, murs à plinthes et bandeaux lumineux, podiums octogonaux à liseré or."""
    X0, X1, Y0, Y1 = -54.0, 54.0, -46.0, 47.0
    bm = bmesh.new()
    H.box(bm, Vector(((X0 + X1) / 2, (Y0 + Y1) / 2, -0.05)), ((X1 - X0), (Y1 - Y0), 0.1))
    H.bm_to_obj(bm, "SR_Floor", target, [M["floor"]])
    bm = bmesh.new()
    H.box(bm, Vector((2.0, 12.6, 0.04)), (68.0, 10.0, 0.08))       # estrade des voitures
    H.box(bm, Vector((2.0, 25.6, 0.04)), (72.0, 13.0, 0.08))       # estrade des camions
    H.bm_to_obj(bm, "SR_Stages", target, [M["stage"]])
    # murs : fond, côtés ; plinthe sombre, bandeaux verticaux et bandeau haut lumineux
    walls, skirts, strips = bmesh.new(), bmesh.new(), bmesh.new()
    Hh = 13.0
    H.box(walls, Vector(((X0 + X1) / 2, Y1 + 0.3, Hh / 2)), (X1 - X0 + 1.2, 0.6, Hh))
    for x in (X0 - 0.3, X1 + 0.3):
        H.box(walls, Vector((x, (Y0 + Y1) / 2, Hh / 2)), (0.6, Y1 - Y0, Hh))
    H.box(skirts, Vector(((X0 + X1) / 2, Y1 - 0.02, 0.35)), (X1 - X0, 0.12, 0.7))
    for x, s in ((X0 + 0.02, 1), (X1 - 0.02, -1)):
        H.box(skirts, Vector((x, (Y0 + Y1) / 2, 0.35)), (0.12, Y1 - Y0, 0.7))
    for x in [X0 + 6.0 + 8.0 * k for k in range(14) if X0 + 6.0 + 8.0 * k < X1 - 3]:
        H.box(strips, Vector((x, Y1 - 0.05, 6.5)), (0.35, 0.1, 9.0))
    for y in [Y0 + 8.0 + 8.0 * k for k in range(12) if Y0 + 8.0 + 8.0 * k < Y1 - 3]:
        for x in (X0 + 0.05, X1 - 0.05):
            H.box(strips, Vector((x, y, 6.5)), (0.1, 0.35, 9.0))
    H.box(strips, Vector(((X0 + X1) / 2, Y1 - 0.05, 11.6)), (X1 - X0, 0.1, 0.25))
    # plafond sombre et rangées de panneaux lumineux (masqué pour les vues prises au-dessus des murs)
    ceil, panels = bmesh.new(), bmesh.new()
    H.box(ceil, Vector(((X0 + X1) / 2, (Y0 + Y1) / 2, Hh + 0.2)), (X1 - X0 + 1.2, Y1 - Y0, 0.4))
    for y in [Y0 + 6.0 + 7.0 * k for k in range(14) if Y0 + 6.0 + 7.0 * k < Y1 - 3]:
        for x in [X0 + 8.0 + 12.0 * k for k in range(9) if X0 + 8.0 + 12.0 * k < X1 - 4]:
            H.box(panels, Vector((x, y, Hh - 0.03)), (8.0, 1.2, 0.06))
    for o in (H.bm_to_obj(ceil, "SR_Ceiling", target, [M["ceiling"]]),
              H.bm_to_obj(panels, "SR_CeilingPanels", target, [M["strip"]])):
        o.visible_shadow = False                           # le soleil et les panneaux passent au travers
        o["ceiling"] = True
    H.bm_to_obj(walls, "SR_Walls", target, [M["wall"]])
    H.bm_to_obj(skirts, "SR_Skirts", target, [M["skirt"]])
    H.bm_to_obj(strips, "SR_Strips", target, [M["strip"]])
    # podiums : octogone anthracite, liseré or en haut du flanc
    pod, ring = bmesh.new(), bmesh.new()
    for x, y in podiums:
        octagon_ring(pod, x, y, PODIUM_R, 0.0, 0.0, PODIUM_H)
        octagon_ring(ring, x, y, PODIUM_R + 0.012, PODIUM_R - 0.16, PODIUM_H - 0.035, PODIUM_H + 0.004)
    H.bm_to_obj(pod, "SR_Podiums", target, [M["podium"]])
    H.bm_to_obj(ring, "SR_PodiumRings", target, [M["gold"]])


def build_lights(target):
    """Ciel neutre clair, soleil doux pour les ombres portées, grands panneaux de plafond au-dessus des rangées."""
    scn = bpy.data.scenes[SCENE]
    w = bpy.data.worlds.get("SR_World") or bpy.data.worlds.new("SR_World")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs["Color"].default_value = (0.42, 0.44, 0.47, 1.0)
    bg.inputs["Strength"].default_value = 0.5
    scn.world = w
    sun = bpy.data.lights.get("SR_Sun") or bpy.data.lights.new("SR_Sun", 'SUN')
    sun.energy = 2.6
    sun.angle = math.radians(9.0)
    sun.color = (1.0, 0.97, 0.93)
    o = bpy.data.objects.new("SR_Sun", sun)
    o.rotation_euler = (math.radians(42.0), 0.0, math.radians(-28.0))
    target.objects.link(o)
    for k, (y, sx, sy) in enumerate(((1.0, 70.0, 12.0), (13.0, 70.0, 10.0), (26.0, 74.0, 14.0), (38.5, 74.0, 8.0))):
        a = bpy.data.lights.get("SR_Panel%d" % k) or bpy.data.lights.new("SR_Panel%d" % k, 'AREA')
        a.shape = 'RECTANGLE'
        a.size, a.size_y = sx, sy
        a.energy = 9000.0 * sx * sy / 840.0
        a.color = (1.0, 0.98, 0.95)
        a.specular_factor = 0.2                         # pas de grandes taches claires sur le sol poli
        o = bpy.data.objects.new("SR_Panel%d" % k, a)
        o.location = (2.0, y, 14.0)
        target.objects.link(o)


def render_settings(res=(2560, 1440), samples=64):
    scn = bpy.data.scenes[SCENE]
    try:
        scn.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    ee = scn.eevee
    ee.taa_render_samples = samples
    for attr, val in (("use_raytracing", True), ("ray_tracing_method", 'SCREEN'), ("use_fast_gi", True),
                      ("use_shadows", True)):
        try:
            setattr(ee, attr, val)
        except Exception:
            pass
    scn.view_settings.view_transform = 'AgX'
    for cand in ('AgX - Medium High Contrast', 'Medium High Contrast'):
        try:
            scn.view_settings.look = cand
            break
        except TypeError:
            pass


def build():
    """(Re)construit tout le showroom dans la scène « Showroom »."""
    check_scene()
    for name in ("SR_Hall", "SR_Vehicles", "SR_Lights", "SR_Cameras"):
        H.clear_col(name)
    hall, veh, lights, cams = col("SR_Hall"), col("SR_Vehicles"), col("SR_Lights"), col("SR_Cameras")
    for o in list(lights.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = materials()
    # premier rang : podiums en arc léger, voitures en 3/4 vers la caméra
    pods = []
    for i, cid in enumerate(HERO):
        x = -30.0 + 7.5 * i
        y = 0.0035 * x * x
        pods.append((x, y))
        place(cid, x, y, -50.0, PODIUM_H, veh)
    build_hall(M, hall, pods)
    # deuxième rang : voitures du trafic en épi, deux-roues au bout
    x = row(CARS, -30.0, 12.6, -45.0, 0.08, veh, 0.45)
    row(BIKES, x + 0.6, 12.6, -45.0, 0.08, veh, 0.9)
    # troisième rang : utilitaires et camions en épi
    row(TRUCKS, -32.5, 25.6, -60.0, 0.08, veh, 1.5)
    # fond : véhicules longs de profil, avant vers la gauche
    row(LONG, -33.0, 39.0, 180.0, 0.0, veh, 3.2)
    build_lights(lights)
    for name, pos, look, lens in VIEWS:
        cam_data = bpy.data.cameras.get("SR_" + name) or bpy.data.cameras.new("SR_" + name)
        cam_data.lens = lens
        cam_data.clip_end = 400.0
        cam = bpy.data.objects.new("SR_" + name, cam_data)
        cam.location = pos
        cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        cams.objects.link(cam)
    bpy.data.scenes[SCENE].camera = bpy.data.objects["SR_" + VIEWS[0][0]]
    render_settings()
    return len(veh.objects)


# ----------------------------------------------------------------- export glTF (Sketchfab)
EXPORT_SCENE = "SR_Export"
EXPORT_HALL = ("SR_Stages", "SR_Podiums", "SR_PodiumRings")
GROUPS = [("Playable", HERO), ("Car", CARS), ("Bike", BIKES), ("Utility", TRUCKS), ("Heavy", LONG)]


def use_export_scene():
    """Scène d'export vide (à appeler seul : le contexte suit au prochain appel)."""
    scn = bpy.data.scenes.get(EXPORT_SCENE) or bpy.data.scenes.new(EXPORT_SCENE)
    for win in bpy.context.window_manager.windows:
        if win.scene != scn:
            win.scene = scn
    return scn


def _srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _lin_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def tinted_paint(cid, color):
    """PK_Paint avec la teinte du véhicule figée dans une copie de la palette : le glTF ne connaît pas la couleur
    d'objet (Object Info) qui teinte la peinture dans Blender et dans le jeu."""
    name = "SR_Paint_" + cid
    m = bpy.data.materials.get(name)
    if m:
        return m
    base = bpy.data.images["PK_Palette"]
    w, h = base.size
    px = list(base.pixels)
    for i in range(0, len(px), 4):
        for k in range(3):
            px[i + k] = _lin_to_srgb(_srgb_to_lin(px[i + k]) * color[k])
    img = bpy.data.images.new(name, w, h, alpha=False)
    img.pixels = px
    m = bpy.data.materials["PK_Paint"].copy()
    m.name = name
    nt = m.node_tree
    for n in [n for n in nt.nodes if n.type in ('OBJECT_INFO', 'VECT_MATH')]:
        nt.nodes.remove(n)
    tex = next(n for n in nt.nodes if n.type == 'TEX_IMAGE')
    tex.image = img
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    nt.links.new(tex.outputs["Color"], H.sock(bsdf, "Base Color"))
    return m


def export_prepare():
    """Remplit la scène d'export : copies réelles des véhicules du showroom (maillages copiés, peinture teintée
    figée, noms génériques sans marque), estrades et podiums, sol resserré ; ni murs ni plafond (on tourne autour
    dans la visionneuse)."""
    if bpy.context.scene.name != EXPORT_SCENE:
        raise RuntimeError("scène active : %s (appeler showroom.use_export_scene() d'abord)" % bpy.context.scene.name)
    out = H.get_col("SR_ExportObjects", bpy.data.scenes[EXPORT_SCENE].collection)
    count = 0
    for group, ids in GROUPS:
        for idx, cid in enumerate(ids, 1):
            e = bpy.data.objects["SR_" + cid]
            spec = P.CARS.get(cid)
            paint = tinted_paint(cid, P.resolve(cid).get("color", (0.6, 0.6, 0.6))) if spec else None
            for k, o in enumerate(o for o in e.instance_collection.all_objects if o.type == 'MESH' and not o.hide_render):
                n = o.copy()
                n.data = o.data.copy()
                n.parent = None
                n.matrix_world = world(e) @ world(o)
                n.name = "%s_%02d_%02d" % (group, idx, k)
                if paint:
                    for i, mat in enumerate(n.data.materials):
                        if mat and mat.name == "PK_Paint":
                            n.data.materials[i] = paint
                out.objects.link(n)
                count += 1
    for name in EXPORT_HALL:
        o = bpy.data.objects[name]
        n = o.copy()
        n.data = o.data.copy()
        n.name = name.replace("SR_", "Showroom_")
        out.objects.link(n)
    bm = bmesh.new()
    H.box(bm, Vector((1.5, 18.5, -0.05)), (84.0, 58.0, 0.1))
    H.bm_to_obj(bm, "Showroom_Floor", out, [bpy.data.materials["SR_floor"]])
    return count


def export_join():
    """Un seul objet par véhicule (les décors et les roues sont des objets séparés dans Blender) : scène légère."""
    col = bpy.data.collections["SR_ExportObjects"]
    groups = {}
    for o in col.objects:
        if o.type == 'MESH' and not o.name.startswith("Showroom_"):
            groups.setdefault(o.name[:o.name.rfind("_")], []).append(o)
    for key, objs in groups.items():
        objs.sort(key=lambda o: o.name)
        active = objs[0]
        with bpy.context.temp_override(active_object=active, object=active, selected_objects=objs,
                                       selected_editable_objects=objs):
            bpy.ops.object.join()
        active.name = key
    return len(col.objects)


def export_glb(path):
    """GLB de la scène d'export (appel séparé après export_prepare)."""
    bpy.context.view_layer.update()
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_active_scene=True, export_apply=True,
                              export_cameras=False, export_lights=False, export_yup=True,
                              export_materials='EXPORT', export_image_format='AUTO')
    return os.path.getsize(path)


def export_cleanup():
    """Supprime la scène d'export et tout ce qu'elle a créé (après être revenu sur une autre scène)."""
    scn = bpy.data.scenes.get(EXPORT_SCENE)
    col = bpy.data.collections.get("SR_ExportObjects")
    if col:
        for o in list(col.objects):
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if me is not None and me.users == 0:
                bpy.data.meshes.remove(me)
        bpy.data.collections.remove(col)
    if scn:
        bpy.data.scenes.remove(scn)
    for m in [m for m in bpy.data.materials if m.name.startswith("SR_Paint_")]:
        bpy.data.materials.remove(m)
    for i in [i for i in bpy.data.images if i.name.startswith("SR_Paint_")]:
        bpy.data.images.remove(i)


def render(folder, views=None, res=(2560, 1440), samples=64):
    """Rendus des vues (toutes par défaut) : Showroom_<vue>.png dans `folder`."""
    check_scene()
    os.makedirs(folder, exist_ok=True)
    render_settings(res, samples)
    scn = bpy.data.scenes[SCENE]
    out = []
    for name, pos, look, lens in VIEWS:
        if views and name not in views:
            continue
        scn.camera = bpy.data.objects["SR_" + name]
        above = pos[2] > 12.5                                  # caméra au-dessus des murs : sans plafond
        for o in bpy.data.collections["SR_Hall"].objects:
            if o.get("ceiling"):
                o.hide_render = above
        path = os.path.join(folder, "Showroom_%s.png" % name)
        scn.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out.append(path)
    return out
