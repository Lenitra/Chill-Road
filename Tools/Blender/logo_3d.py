# Chill Road — propositions de logo en 3D (style UI « verre »).
# Lancement dans Blender (MCP) :
#   exec(open(r"E:/ChillRoad/Tools/Blender/logo_3d.py", encoding="utf-8").read())
#   build_all(); render("Logo_B1")   # ou render_all()
# Chaque logo est construit dans sa propre scène (Logo_B1/B2/B3), devant une photo
# du jeu floutée par la profondeur de champ (= le fond flouté des menus en verre).
import bpy, bmesh, math, os
from mathutils import Vector

ROOT = r"E:/ChillRoad"
FONTS = ROOT + "/Assets/ChillRoad/Resources/Fonts/"
OUT = ROOT + "/Tools/Blender/Rendus/Logos/"
GOLD = "#FFC233"
CYAN = "#6FDCFF"


def lin(hexstr):
    h = hexstr.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)


def font(name):
    path = name if ":" in name else FONTS + name + ".ttf"
    for f in bpy.data.fonts:
        if os.path.normcase(bpy.path.abspath(f.filepath)) == os.path.normcase(path):
            return f
    return bpy.data.fonts.load(path)


# ---------------------------------------------------------------- matériaux
def principled(mat):
    return next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def mat_new(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    return m


def mat_glass(dark=0.34, rough=0.22):
    """Verre dépoli teinté sombre = Glass.Panel de l'UI (rgba(20,24,30,.42) sur fond flou)."""
    m = mat_new("LG_Glass_%02d" % round(dark * 100))
    nt = m.node_tree
    nt.nodes.clear()
    clear = nt.nodes.new("ShaderNodeBsdfPrincipled")
    clear.inputs["Base Color"].default_value = (0.92, 0.95, 1.0, 1)
    clear.inputs["Transmission Weight"].default_value = 1.0
    clear.inputs["Roughness"].default_value = rough
    clear.inputs["IOR"].default_value = 1.45
    tint = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tint.inputs["Base Color"].default_value = (0.007, 0.009, 0.012, 1)
    tint.inputs["Roughness"].default_value = 0.25
    mix = nt.nodes.new("ShaderNodeMixShader")
    mix.inputs["Fac"].default_value = dark
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(clear.outputs[0], mix.inputs[1])
    nt.links.new(tint.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    return m


def mat_solid(name, hexstr, emit=0.0, metal=0.0, rough=0.4):
    m = mat_new(name)
    b = principled(m)
    col = lin(hexstr)
    b.inputs["Base Color"].default_value = col
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    b.inputs["Emission Color"].default_value = col
    b.inputs["Emission Strength"].default_value = emit
    return m


def mat_white():
    return mat_solid("LG_White", "#F4F6F8", emit=0.35, rough=0.45)


def mat_gold():
    return mat_solid("LG_Gold", GOLD, emit=0.45, metal=0.35, rough=0.32)


def mat_cyan():
    return mat_solid("LG_Cyan", CYAN, emit=2.2)


def mat_photo(img_name):
    m = mat_new("LG_Photo_" + img_name)
    nt = m.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new("ShaderNodeTexImage")
    # fond pré-flouté (PIL, flou gaussien 9 px + 80 % de luminosité) = fond flouté des menus
    blurred = OUT + "bg_" + img_name
    tex.image = bpy.data.images.load(blurred if os.path.exists(blurred) else ROOT + "/docs/img/" + img_name, check_existing=True)
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 0.75
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tex.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return m


# ---------------------------------------------------------------- objets
def link(scene, ob):
    scene.collection.objects.link(ob)
    return ob


def text(scene, name, body, fnt, size, mats, extrude=0.03, space=1.0, align="CENTER", bold=None, bold_from=None):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = body
    cu.font = font(fnt)
    if bold:
        cu.font_bold = font(bold)
    cu.size = size
    cu.extrude = extrude
    cu.bevel_depth = min(0.006, extrude * 0.3)
    cu.bevel_resolution = 2
    cu.space_character = space
    cu.align_x = align
    cu.align_y = "CENTER"
    for m in mats:
        cu.materials.append(m)
    if bold_from is not None:
        for i in range(bold_from, len(body)):
            cu.body_format[i].use_bold = bool(bold)
            cu.body_format[i].material_index = len(mats) - 1
    ob = link(scene, bpy.data.objects.new(name, cu))
    ob.rotation_euler = (math.radians(90), 0, 0)
    return ob


def rounded_rect_curve(scene, name, w, h, r, depth, bevel, mat, seg=24):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "2D"
    cu.fill_mode = "BOTH"
    cu.extrude = depth
    cu.bevel_depth = bevel
    cu.bevel_resolution = 6
    sp = cu.splines.new("POLY")
    pts = []
    corners = [(w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)]
    for cx, cy, a0 in corners:
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    sp.points.add(len(pts) - 1)
    for p, (x, y) in zip(sp.points, pts):
        p.co = (x, y, 0, 1)
    sp.use_cyclic_u = True
    cu.materials.append(mat)
    ob = link(scene, bpy.data.objects.new(name, cu))
    ob.rotation_euler = (math.radians(90), 0, 0)
    return ob


def tube(scene, name, pts, radius, mat, smooth=True, bevel_res=4):
    """Ruban/tube le long de points 3D (coordonnées monde)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = bevel_res
    cu.use_fill_caps = True
    if smooth:
        sp = cu.splines.new("BEZIER")
        sp.bezier_points.add(len(pts) - 1)
        for bp, p in zip(sp.bezier_points, pts):
            bp.co = p
            bp.handle_left_type = bp.handle_right_type = "AUTO"
    else:
        sp = cu.splines.new("POLY")
        sp.points.add(len(pts) - 1)
        for bp, p in zip(sp.points, pts):
            bp.co = (p[0], p[1], p[2], 1)
    cu.materials.append(mat)
    return link(scene, bpy.data.objects.new(name, cu))


def mesh_obj(scene, name, bm, mats, smooth=False):
    me = bpy.data.meshes.new(name)
    for m in mats:
        me.materials.append(m)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    return link(scene, bpy.data.objects.new(name, me))


def photo_plane(scene, img, behind=12.0, margin=1.08):
    """Photo du jeu face à la caméra, à `behind` m derrière le point de mise au point, cadre rempli."""
    cam = scene.camera
    d = cam.data.dof.focus_distance + behind
    width = 2 * d * math.tan(cam.data.angle_x / 2) * margin
    h = width * 9 / 16
    mw = cam.matrix_basis  # matrix_world pas encore à jour dans le même appel
    corners = [mw @ Vector((x * width / 2, y * h / 2, -d)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in corners]
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.new()
    for loop, c in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        loop[uv].uv = c
    ob = mesh_obj(scene, "Photo", bm, [mat_photo(img)])
    ob.visible_shadow = False
    return ob


# ---------------------------------------------------------------- scène
def new_scene(name):
    old = bpy.data.scenes.get(name)
    if old:
        for ob in list(old.collection.all_objects):
            bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.scenes.remove(old)
    sc = bpy.data.scenes.new(name)
    try:
        sc.render.engine = "CYCLES"
    except TypeError as e:
        print(e)
    sc.cycles.samples = 160
    sc.cycles.use_denoising = True
    sc.render.resolution_x = 1920
    sc.render.resolution_y = 1080
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "AgX"
    try:
        sc.view_settings.look = "AgX - Base Contrast"
    except TypeError:
        pass
    w = bpy.data.worlds.new(name + "_World")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.012, 0.014, 0.02, 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.world = w
    return sc


def camera(scene, loc, target, focal=85, fstop=2.0):
    cd = bpy.data.cameras.new("Cam")
    cd.lens = focal
    ob = link(scene, bpy.data.objects.new("Cam", cd))
    ob.location = loc
    d = Vector(target) - Vector(loc)
    ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cd.dof.use_dof = True
    cd.dof.focus_distance = d.length
    cd.dof.aperture_fstop = fstop
    scene.camera = ob
    return ob


def area(scene, name, loc, target, size, power, color=(1, 1, 1)):
    ld = bpy.data.lights.new(name, "AREA")
    ld.size = size
    ld.energy = power
    ld.color = color
    ob = link(scene, bpy.data.objects.new(name, ld))
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return ob


def lights(scene):
    area(scene, "Key", (-4, -6, 5), (0, 0, 0), 4, 900, (1.0, 0.93, 0.85))
    area(scene, "Fill", (5, -5, 1), (0, 0, 0), 5, 300, (0.8, 0.9, 1.0))
    area(scene, "Rim", (0, 4, 4), (0, 0, 0), 6, 700, (0.85, 0.95, 1.0))


def kicker(scene, z, y=-0.02, size=0.15):
    k = text(scene, "Kicker", "TOUR DU MASSIF", "Inter-Medium", size, [mat_cyan()], extrude=0.008, space=1.9)
    k.location = (0, y, z)
    return k


# ---------------------------------------------------------------- B1 : plaque givrée
def build_b1():
    sc = new_scene("Logo_B1")
    card = rounded_rect_curve(sc, "Card", 5.6, 2.3, 0.42, 0.05, 0.035, mat_glass())
    card.location = (0, 0.06, 0)
    t = text(sc, "Wordmark", "CHILL ROAD", "Inter-Light", 0.62, [mat_white(), mat_gold()], extrude=0.035,
             space=1.22, bold="Inter-SemiBold", bold_from=6)
    t.location = (0, -0.06, -0.02)
    # ligne de crête blanche puis route dorée
    ridge = [(-2.1, 0.72), (-1.55, 0.88), (-1.25, 0.8), (-0.7, 1.02), (-0.3, 0.84), (0.0, 0.92), (0.45, 0.7)]
    tube(sc, "Ridge", [(x, -0.05, z) for x, z in ridge], 0.011, mat_white(), smooth=False, bevel_res=2)
    road = [(0.45, 0.7), (0.85, 0.6), (1.2, 0.86), (1.65, 0.78), (2.1, 0.74)]
    tube(sc, "Road", [(x, -0.06, z) for x, z in road], 0.028, mat_gold())
    kicker(sc, -0.62, y=-0.05)
    lights(sc)
    camera(sc, (-2.2, -16.5, 1.5), (0, 0, 0.05), focal=80, fstop=2.8)
    photo_plane(sc, "lac.jpg")
    return sc


# ---------------------------------------------------------------- B2 : diorama lacets
def mountain_bm(R=1.25, H=1.75, sides=9, rings=5):
    bm = bmesh.new()
    rows = []
    for j in range(rings):
        z = H * j / rings
        r = R * (1 - j / rings) * (1 + 0.06 * math.sin(j * 2.1))
        row = []
        for i in range(sides):
            a = 2 * math.pi * (i + 0.5 * j) / sides
            jit = 1 + 0.08 * math.sin(i * 3.7 + j * 1.3)
            row.append(bm.verts.new((r * jit * math.sin(a), -r * jit * math.cos(a), z)))
        rows.append(row)
    top = bm.verts.new((0.05, 0.02, H))
    for j in range(rings - 1):
        for i in range(sides):
            a, b = rows[j][i], rows[j][(i + 1) % sides]
            c, d = rows[j + 1][(i + 1) % sides], rows[j + 1][i]
            bm.faces.new((a, b, c))
            bm.faces.new((a, c, d))
    for i in range(sides):
        bm.faces.new((rows[-1][i], rows[-1][(i + 1) % sides], top))
    bm.faces.new(list(reversed(rows[0])))
    bm.normal_update()
    for f in bm.faces:
        z = f.calc_center_median().z
        f.material_index = 0 if z < H * 0.38 else (1 if z < H * 0.72 else 2)
    return bm


def build_b2():
    sc = new_scene("Logo_B2")
    # palet de verre
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=96, radius1=1.7, radius2=1.7, depth=0.2)
    disc = mesh_obj(sc, "Puck", bm, [mat_glass()], smooth=True)
    bev = disc.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.05
    bev.segments = 6
    bev.harden_normals = False
    disc.location = (-2.75, 0, -0.6)
    H = 1.75
    mats = [mat_solid("LG_Grass", "#6FA84E", rough=0.8), mat_solid("LG_Rock", "#8C929C", rough=0.85),
            mat_solid("LG_Snow", "#F2F5F8", emit=0.1, rough=0.7)]
    mtn = mesh_obj(sc, "Mountain", mountain_bm(H=H), mats)
    mtn.location = (-2.75, 0, -0.5)
    # lacets dorés sur la face avant
    pts = []
    turns = [(-0.05, 0.0), (0.62, 0.08), (-0.55, 0.38), (0.5, 0.64), (-0.38, 0.92), (0.3, 1.14), (-0.12, 1.3)]
    for k in range(len(turns) - 1):
        (a0, z0), (a1, z1) = turns[k], turns[k + 1]
        for s in range(8):
            t = s / 8
            e = t * t * (3 - 2 * t)
            a = a0 + (a1 - a0) * e
            z = z0 + (z1 - z0) * t
            r = 1.25 * (1 - z / H) + 0.07
            pts.append((-2.75 + r * math.sin(a), -r * math.cos(a), -0.5 + z))
    a, z = turns[-1]
    r = 1.25 * (1 - z / H) + 0.07
    pts.append((-2.75 + r * math.sin(a), -r * math.cos(a), -0.5 + z))
    tube(sc, "Lacets", pts, 0.032, mat_gold())
    t = text(sc, "Wordmark", "Chill Road", "Inter-Light", 0.92, [mat_white(), mat_gold()], extrude=0.05,
             space=1.0, align="LEFT", bold="Inter-SemiBold", bold_from=6)
    t.location = (-0.3, 0, 0.3)
    k = text(sc, "Kicker", "TOUR DU MASSIF", "Inter-Medium", 0.17, [mat_cyan()], extrude=0.01, space=1.9, align="LEFT")
    k.location = (-0.25, 0, -0.45)
    lights(sc)
    card = rounded_rect_curve(sc, "Card", 5.0, 2.3, 0.42, 0.05, 0.035, mat_glass())
    card.location = (1.6, 0.12, -0.02)
    camera(sc, (-0.6, -21.0, 4.0), (0.0, 0, 0.1), focal=80, fstop=2.8)
    photo_plane(sc, "alpage.jpg")
    return sc


# ---------------------------------------------------------------- B3 : pastille volant
def wheel(sc, loc, R, mat_rim, mat_hub):
    # jante = tore
    bm = bmesh.new()
    seg, sides, rm = 96, 24, R * 0.2
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            rr = R + rm * math.cos(b)
            ring.append(bm.verts.new((rr * math.cos(a), rm * math.sin(b), rr * math.sin(a))))
        rings.append(ring)
    for i in range(seg):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[(i + 1) % seg][j], rings[(i + 1) % seg][(j + 1) % sides], rings[i][(j + 1) % sides]))
    rim = mesh_obj(sc, "Rim", bm, [mat_rim], smooth=True)
    rim.location = loc
    # moyeu + 3 branches
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=R * 0.28, radius2=R * 0.28, depth=R * 0.3)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(math.radians(90), 3, "X"))
    for ang in (0, 180, 270):
        a = math.radians(ang)
        res = bmesh.ops.create_cube(bm, size=1)
        vs = res["verts"]
        bmesh.ops.scale(bm, vec=(R * 0.86, R * 0.16, R * 0.14), verts=vs)
        bmesh.ops.translate(bm, vec=(R * 0.5, 0, 0), verts=vs)
        bmesh.ops.rotate(bm, verts=vs, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(-a, 3, "Y"))
    hub = mesh_obj(sc, "Hub", bm, [mat_hub], smooth=False)
    hub.location = loc
    bv = hub.modifiers.new("Bevel", "BEVEL")
    bv.width = R * 0.03
    bv.segments = 3
    return rim, hub


def build_b3():
    sc = new_scene("Logo_B3")
    pill = rounded_rect_curve(sc, "Pill", 6.4, 1.7, 0.85, 0.05, 0.04, mat_glass(), seg=40)
    pill.location = (0, 0.08, 0)
    white, gold = mat_white(), mat_gold()
    size = 1.0
    parts = [("chill", "Inter-Light"), ("r", "Inter-SemiBold"), ("WHEEL", None), ("ad", "Inter-SemiBold")]
    objs = []
    for body, fnt in parts:
        if body == "WHEEL":
            objs.append(None)
            continue
        o = text(sc, "T_" + body, body, fnt, size, [white], extrude=0.045, align="LEFT")
        o.data.align_y = "BOTTOM_BASELINE"
        objs.append(o)
    bpy.context.view_layer.update()
    sc_layer = sc.view_layers[0]
    sc_layer.update()
    gaps = [0.30, 0.03, 0.03]
    wheelR = 0.205
    widths = []
    for o in objs:
        widths.append(2 * wheelR * 1.2 + 0.0 if o is None else o.dimensions.x)
    total = sum(widths) + sum(gaps)
    x = -total / 2
    base_z = -0.27
    for i, o in enumerate(objs):
        if o is None:
            wheel(sc, (x + widths[i] / 2, -0.06, base_z + wheelR * 1.2), wheelR, gold, gold)
        else:
            o.location = (x, -0.06, base_z)
        x += widths[i] + (gaps[i] if i < len(gaps) else 0)
    kicker(sc, -1.25, y=0.0, size=0.15)
    lights(sc)
    camera(sc, (1.9, -17.5, 1.2), (0, 0, -0.2), focal=80, fstop=2.8)
    photo_plane(sc, "couchant.jpg")
    return sc


def build_all():
    build_b1()
    build_b2()
    build_b3()


def render(name, samples=None, path=None):
    sc = bpy.data.scenes[name]
    prefs = bpy.context.preferences.addons["cycles"].preferences
    old = prefs.compute_device_type
    try:
        try:
            prefs.compute_device_type = "OPTIX"
            prefs.get_devices()
            for d in prefs.devices:
                d.use = d.type == "OPTIX"
            sc.cycles.device = "GPU"
        except Exception as e:
            print("GPU:", e)
        if samples:
            sc.cycles.samples = samples
        os.makedirs(OUT, exist_ok=True)
        sc.render.filepath = path or (OUT + name + ".png")
        win = bpy.context.window
        prev = win.scene
        win.scene = sc
        try:
            bpy.ops.render.render(write_still=True, scene=sc.name)
        finally:
            win.scene = prev
    finally:
        prefs.compute_device_type = old
    return sc.render.filepath


def render_all():
    return [render(n) for n in ("Logo_B1", "Logo_B2", "Logo_B3")]
