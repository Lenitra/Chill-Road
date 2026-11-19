# Chill Road — 3 concepts de logo pensés pour la 3D (2e série Blender).
#   exec(open(r"E:/ChillRoad/Tools/Blender/logo_3d_v2.py", encoding="utf-8").read())
#   build_c1(); build_c2(); build_c3(); render("Logo_C1") ...
# Réutilise les outils de logo_3d.py (matériaux verre/or/cyan, texte, fond flouté, caméra, rendu).
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

exec(open(r"E:/ChillRoad/Tools/Blender/logo_3d.py", encoding="utf-8").read())

MODELS = ROOT + "/Assets/ChillRoad/Models/"


def import_fbx(scene, rel, name, color=None):
    """Importe un FBX du jeu sous un empty (racine au sol, Z haut, avant vers -Y)."""
    win = bpy.context.window
    prev = win.scene
    win.scene = scene
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=MODELS + rel)
    finally:
        win.scene = prev
    new = [o for o in bpy.data.objects if o not in before]
    fix_images()
    root = link(scene, bpy.data.objects.new(name, None))
    for o in new:
        if o.parent is None:
            o.parent = root
    if color:
        for o in new:
            if o.type == "MESH":
                for i, m in enumerate(o.data.materials):
                    if m and m.name.startswith("PK_Paint"):
                        pm = m.copy()
                        pm.name = name + "_Paint"
                        principled(pm).inputs["Base Color"].default_value = lin(color)
                        principled(pm).inputs["Roughness"].default_value = 0.35
                        o.data.materials[i] = pm
    return root


FIX = {"PK_Palette": "CarPack/CarPack_Palette.png", "AP_Palette": "AlpPack/AlpPack_Palette.png",
       "AP_Emission": "AlpPack/AlpPack_Emission.png"}


def fix_images():
    """Certains FBX pointent vers textures/packed/... inexistant : on rebranche sur les palettes du jeu."""
    for im in bpy.data.images:
        if im.source != "FILE" or os.path.exists(bpy.path.abspath(im.filepath)):
            continue
        for key, rel in FIX.items():
            if im.name.startswith(key):
                im.filepath = MODELS + rel
                im.reload()


def mat_snow():
    m = mat_new("LG_Snow2")
    b = principled(m)
    b.inputs["Base Color"].default_value = lin("#F3F6FA")
    b.inputs["Roughness"].default_value = 0.6
    b.inputs["Subsurface Weight"].default_value = 0.25
    b.inputs["Subsurface Radius"].default_value = (0.3, 0.4, 0.6)
    b.inputs["Emission Color"].default_value = lin("#F3F6FA")
    b.inputs["Emission Strength"].default_value = 0.08
    return m


def mat_asphalt():
    return mat_solid("LG_Asphalt", "#3A3F48", rough=0.85)


def mat_line():
    return mat_solid("LG_Line", "#F2F2EE", emit=0.25, rough=0.6)


def flat_text(scene, name, body, fnt, size, mat, z, extrude, offset=0.0, bevel=0.0, space=1.0, x=0.0, y=0.0):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = body
    cu.font = font(fnt)
    cu.size = size
    cu.extrude = extrude
    cu.offset = offset
    cu.bevel_depth = bevel
    cu.bevel_resolution = 3
    cu.space_character = space
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.materials.append(mat)
    ob = link(scene, bpy.data.objects.new(name, cu))
    ob.location = (x, y, z)
    return ob


def lying_rect(scene, name, w, h, r, mat, z):
    ob = rounded_rect_curve(scene, name, w, h, r, 0.06, 0.04, mat)
    ob.rotation_euler = (0, 0, 0)
    ob.location = (0, 0, z)
    return ob


# ------------------------------------------------------------- C1 : route-lettres
def build_c1():
    sc = new_scene("Logo_C1")
    slab = lying_rect(sc, "Slab", 11.6, 7.4, 0.6, mat_glass(), -0.1)
    # ROAD = morceaux de route : socle asphalte, ligne de rive blanche, bande de roulement
    S, X0, Y0 = 5.0, 0.0, -0.75
    RF = "C:/Windows/Fonts/seguisb.ttf"  # rendu seulement ; contours sans chevauchement
    base = flat_text(sc, "Road_Base", "ROAD", RF, S, mat_asphalt(), 0.09, 0.09, space=1.06, x=X0, y=Y0)
    flat_text(sc, "Road_Line", "ROAD", RF, S, mat_line(), 0.181, 0.002, offset=-0.035, space=1.06, x=X0, y=Y0)
    flat_text(sc, "Road_Top", "ROAD", RF, S, mat_asphalt(), 0.186, 0.002, offset=-0.065, space=1.06, x=X0, y=Y0)
    # CHILL = congère : lettres arrondies en neige
    flat_text(sc, "Chill", "CHILL", "Inter-Medium", 2.3, mat_snow(), 0.1, 0.06, bevel=0.05, space=1.3, x=0, y=1.85)
    flat_text(sc, "Kicker", "TOUR DU MASSIF", "Inter-Medium", 0.34, mat_cyan(), 0.0, 0.004, space=1.9, x=0, y=-2.65)
    # petite MX-5 rouge qui monte le fût du R (mesuré sur le maillage)
    win = bpy.context.window
    prev = win.scene
    win.scene = sc
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(base.evaluated_get(dg))
    finally:
        win.scene = prev
    vs = [base.matrix_world @ p.co for p in me.vertices]
    bpy.data.meshes.remove(me)
    xmin = min(p.x for p in vs)
    ymin, ymax = min(p.y for p in vs), max(p.y for p in vs)
    hgt = ymax - ymin
    band = [p.x for p in vs if ymin + 0.15 * hgt < p.y < ymin + 0.5 * hgt and xmin + 0.03 < p.x < xmin + 0.8]
    stem_r = min(band) if band else xmin + 0.3
    print("R stem", round(xmin, 2), round(stem_r, 2), "height", round(hgt, 2))
    car = import_fbx(sc, "PlayerCars/Miata.fbx", "Car", color="#D8432E")
    cs = (stem_r - xmin - 0.06) / 1.79
    car.scale = (cs, cs, cs)
    car.location = ((xmin + stem_r) / 2, ymin + 0.3 * hgt, 0.188)
    car.rotation_euler = (0, 0, math.radians(180))
    # sapins et congères aux coins
    for i, (x, y, s, kind) in enumerate([(-5.0, 2.6, 0.17, "A"), (-4.35, 3.05, 0.12, "B"), (4.9, 2.55, 0.16, "B"),
                                         (5.15, -2.7, 0.13, "A"), (4.55, -3.05, 0.09, "C")]):
        t = import_fbx(sc, "AlpPack/Spruce_%s.fbx" % kind, "Spruce%d" % i)
        t.scale = (s, s, s)
        t.location = (x, y, -0.04)
        t.rotation_euler = (0, 0, i * 1.3)
    for i, (x, y, s) in enumerate([(-4.6, 2.2, 0.18), (5.2, 2.0, 0.15), (-5.0, -2.9, 0.16)]):
        b = import_fbx(sc, "AlpPack/Snow_Bank_A.fbx", "Bank%d" % i)
        b.scale = (s, s, s)
        b.location = (x, y, -0.04)
        b.rotation_euler = (0, 0, i * 2.0)
    lights(sc)
    area(sc, "Top", (0, -2, 9), (0, 0, 0), 8, 900, (1.0, 0.97, 0.92))
    camera(sc, (-3.2, -15.5, 12.5), (0, 0.1, 0), focal=58, fstop=4.0)
    photo_plane(sc, "lac.jpg", behind=10.0)
    return sc


# ------------------------------------------------------------- C2 : poteau indicateur
def arrow_curve(scene, name, L, h, tip, mat, direction=1, depth=0.05, bevel=0.035):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "2D"
    cu.fill_mode = "BOTH"
    cu.extrude = depth
    cu.bevel_depth = bevel
    cu.bevel_resolution = 5
    sp = cu.splines.new("POLY")
    d = direction
    pts = [(0, -h / 2), (d * (L - tip), -h / 2), (d * L, 0), (d * (L - tip), h / 2), (0, h / 2)]
    if d < 0:
        pts.reverse()
    sp.points.add(len(pts) - 1)
    for p, (x, y) in zip(sp.points, pts):
        p.co = (x, y, 0, 1)
    sp.use_cyclic_u = True
    cu.materials.append(mat)
    ob = link(scene, bpy.data.objects.new(name, cu))
    ob.rotation_euler = (math.radians(90), 0, 0)
    return ob


def snow_ridge(scene, name, x0, x1, z, y, seed=1):
    rnd = random.Random(seed)
    n = 14
    pts = [(x0 + (x1 - x0) * i / (n - 1), y, z + 0.02 + rnd.uniform(0.0, 0.05)) for i in range(n)]
    ob = tube(scene, name, pts, 0.075, mat_snow())
    ob.data.bevel_resolution = 5
    return ob


def build_c2():
    sc = new_scene("Logo_C2")
    glass, gold = mat_glass(dark=0.38), mat_gold()
    post_m = mat_solid("LG_Post", "#2A2F37", metal=0.6, rough=0.35)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=32, radius1=0.1, radius2=0.1, depth=7.0)
    post = mesh_obj(sc, "Post", bm, [post_m], smooth=True)
    post.location = (0, 0.12, -2.0)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=16, radius=0.16)
    cap = mesh_obj(sc, "Cap", bm, [gold], smooth=True)
    cap.location = (0, 0.12, 1.55)
    # flèche 1 : CHILL ROAD vers la droite
    a1 = arrow_curve(sc, "Arrow1", 6.0, 1.25, 0.62, glass, 1)
    a1.location = (-0.45, 0, 0.6)
    tip = arrow_curve(sc, "Tip1", 0.5, 0.82, 0.5, gold, 1, depth=0.015, bevel=0.01)
    tip.location = (-0.45 + 6.0 - 0.62 - 0.08, -0.09, 0.6)
    t = text(sc, "Wordmark", "CHILL ROAD", "Inter-Light", 0.6, [mat_white(), gold], extrude=0.03, space=1.12,
             bold="Inter-SemiBold", bold_from=6)
    t.location = (-0.45 + (6.0 - 0.62) / 2 - 0.05, -0.1, 0.6)
    snow_ridge(sc, "Snow1", -0.35, 5.0, 0.6 + 0.625, 0.0, seed=3)
    # flèche 2 : TOUR DU MASSIF vers la gauche
    a2 = arrow_curve(sc, "Arrow2", 4.6, 0.82, 0.45, glass, -1)
    a2.location = (0.45, 0, -0.62)
    t2 = text(sc, "Kicker", "TOUR DU MASSIF", "Inter-Medium", 0.26, [mat_cyan()], extrude=0.008, space=1.8)
    t2.location = (0.45 - (4.6 - 0.45) / 2 - 0.12, -0.09, -0.62)
    tip2 = arrow_curve(sc, "Tip2", 0.36, 0.52, 0.36, gold, -1, depth=0.015, bevel=0.01)
    tip2.location = (0.45 - 4.6 + 0.45 + 0.06, -0.09, -0.62)
    snow_ridge(sc, "Snow2", -3.8, 0.35, -0.62 + 0.41, 0.0, seed=7)
    lights(sc)
    camera(sc, (-3.0, -17.5, -0.2), (0.7, 0, 0.3), focal=58, fstop=2.8)
    photo_plane(sc, "lac.jpg", behind=12.0)
    return sc


# ------------------------------------------------------------- C3 : titre sur île flottante
def puffy_letters(scene, name, body, fnt, size, mat_body, loc, extrude=0.3, bevel=0.08, space=1.05, snow_above=0.55):
    """Grosses lettres arrondies ; faces orientées vers le haut = neige (2e matériau)."""
    cu = bpy.data.curves.new(name + "_c", "FONT")
    cu.body = body
    cu.font = font(fnt)
    cu.size = size
    cu.extrude = extrude
    cu.bevel_depth = bevel
    cu.bevel_resolution = 5
    cu.space_character = space
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    tmp = link(scene, bpy.data.objects.new(name + "_tmp", cu))
    win = bpy.context.window
    prev = win.scene
    win.scene = scene
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    finally:
        win.scene = prev
    bpy.data.objects.remove(tmp, do_unlink=True)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(Matrix.Rotation(math.radians(90), 4, "X"))
    bm.normal_update()
    for f in bm.faces:
        f.material_index = 1 if f.normal.z > snow_above else 0
    me.materials.clear()
    me.materials.append(mat_body)
    me.materials.append(mat_snow())
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = link(scene, bpy.data.objects.new(name, me))
    ob.location = loc
    return ob


def island_bm(rx=6.2, ry=3.4, top=0.0, depth=2.6, seed=4):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=10, radius=1.0)
    for v in bm.verts:
        x, y, z = v.co
        j = 1 + rnd.uniform(-0.07, 0.07)
        if z >= -0.05:
            v.co = (x * rx * j, y * ry * j, top + z * 0.06 + rnd.uniform(0.0, 0.025))
        else:
            v.co = (x * rx * j * (1 + z * 0.25), y * ry * j * (1 + z * 0.25), top + z * depth + rnd.uniform(-0.15, 0.15))
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        f.material_index = 0 if n.z > 0.55 else (1 if n.z > -0.2 else 2)
    return bm


def road_ribbon(scene, name, pts, width, asphalt, line, z=0.0):
    """Ruban de route plat le long de points (x, y) + tirets centraux."""
    bm = bmesh.new()
    left, right, centers = [], [], []
    P = [Vector((x, y, h)) for x, y, h in pts]
    for i, p in enumerate(P):
        t = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        side = t.cross(Vector((0, 0, 1))).normalized()
        left.append(bm.verts.new(p + side * width / 2))
        right.append(bm.verts.new(p - side * width / 2))
        centers.append((p, t, side))
    for i in range(len(P) - 1):
        bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
    road = mesh_obj(scene, name, bm, [asphalt], smooth=True)
    road.location.z = z
    # tirets
    bm = bmesh.new()
    for i in range(1, len(P) - 1, 2):
        p, t, side = centers[i]
        q = P[i + 1]
        a, b = p + Vector((0, 0, 0.012)), p + (q - p) * 0.55 + Vector((0, 0, 0.012))
        w = side * width * 0.035
        bm.faces.new([bm.verts.new(v) for v in (a + w, a - w, b - w, b + w)])
    dash = mesh_obj(scene, name + "_Dash", bm, [line])
    dash.location.z = z
    return road


def build_c3():
    sc = new_scene("Logo_C3")
    isl = mesh_obj(sc, "Island", island_bm(), [mat_snow(), mat_solid("LG_Rock2", "#7C828C", rough=0.9),
                                                 mat_solid("LG_RockDark", "#4A4F58", rough=0.9)])
    isl.location = (0, 0.6, -1.3)
    # route en S qui traverse l'île
    pts = []
    for i in range(40):
        t = i / 39
        x = -5.6 + 11.2 * t
        y = -1.9 + 2.6 * t + 0.9 * math.sin(t * math.pi * 1.6)
        pts.append((x, y, 0.0))
    road_ribbon(sc, "Road", pts, 0.62, mat_asphalt(), mat_line(), z=-1.2)
    car = import_fbx(sc, "PlayerCars/AE86.fbx", "Car", color="#F4F4F0")
    car.scale = (0.2, 0.2, 0.2)
    i = 31
    p, q = Vector(pts[i]), Vector(pts[i + 1])
    d = (q - p).normalized()
    car.location = (p.x, p.y, -1.19)
    car.rotation_euler = (0, 0, math.atan2(d.x, -d.y))
    gold = mat_solid("LG_GoldToon", "#FFB51A", emit=0.12, metal=0.15, rough=0.38)
    chill = puffy_letters(sc, "Chill", "CHILL", "Inter-SemiBold", 1.55, mat_solid("LG_Ice", "#EAF6FF", emit=0.1, rough=0.35),
                          (-0.15, 0.9, 1.35), extrude=0.26, snow_above=2.0)
    road = puffy_letters(sc, "RoadWord", "ROAD", "Inter-SemiBold", 2.3, gold, (0.1, 0.55, -0.4), extrude=0.34, bevel=0.1)
    for i, (x, y, s, kind) in enumerate([(-4.9, 1.6, 0.3, "A"), (-4.2, 2.6, 0.24, "B"), (-5.4, 0.3, 0.2, "C"),
                                         (4.7, 2.2, 0.28, "B"), (5.3, 0.9, 0.22, "A"), (3.9, 2.9, 0.2, "C")]):
        tr = import_fbx(sc, "AlpPack/Spruce_%s.fbx" % kind, "Spruce%d" % i)
        tr.scale = (s, s, s)
        tr.location = (x, y, -1.2)
        tr.rotation_euler = (0, 0, i * 1.1)
    k = text(sc, "Kicker", "TOUR DU MASSIF", "Inter-Medium", 0.3, [mat_cyan()], extrude=0.01, space=1.9)
    k.location = (0, -3.3, -2.05)
    lights(sc)
    area(sc, "Sun", (6, -4, 7), (0, 0, 0), 3, 1400, (1.0, 0.85, 0.65))
    camera(sc, (-2.6, -19.0, 4.2), (0, 0.3, -0.5), focal=55, fstop=4.0)
    photo_plane(sc, "couchant.jpg", behind=12.0)
    return sc
