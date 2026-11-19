# Outro du trailer (8 s, 480 images à 60 i/s) : le paysage devient le logo « Ligne de crête ».
# Une chaîne de montagnes low-poly dont la crête suit exactement la ligne blanche du logo ; la Mustang roule sur
# une route de crête et sa traînée dorée trace la route du logo. La caméra part derrière la voiture, recule et se
# place de face (longue focale) : crête blanche + route dorée + point d'arrivée = le logo, puis CHILL ROAD.
#   E:/blender/blender.exe -b --factory-startup --python Tools/Trailer/outro/trailer_outro.py -- [--test F[,F...]] [--samples S] [--out DIR]
# Repère : 1 unité = 10 px du SVG (Marketing/logo/ligne_de_crete.svg), x = x_svg / 10, z = (80 - y_svg) / 10.
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

ROOT = "E:/ChillRoad"
HERE = ROOT + "/Tools/Trailer/outro/"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def opt(name, default):
    return argv[argv.index(name) + 1] if name in argv else default
TEST = [int(f) for f in opt("--test", "").split(",") if f]
SAMPLES = int(opt("--samples", "48"))
OUTDIR = opt("--out", HERE + "out/frames/")
FPS, N = 60, 480
WHITE, GOLD = "#FFFFFF", "#FFC233"
random.seed(3)


def lin(hexstr):
    h = hexstr.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)


def P(x, y):                      # point du SVG (repère du groupe) -> (x, z)
    return (x / 10.0, (80.0 - y) / 10.0)


def bez(p0, p1, p2, p3, n):
    out = []
    for i in range(n + 1):
        t = i / n
        a, b, c, d = (1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out


RIDGE = [P(*q) for q in ((2, 74), (62, 46), (96, 58), (158, 14), (204, 48), (240, 34), (296, 66))]
# M296 66 C 340 92, 372 34, 412 48 S 476 78, 518 56  (S : 1er point de contrôle = reflet de 372 34 par 412 48)
ROAD = bez(P(296, 66), P(340, 92), P(372, 34), P(412, 48), 60)[:-1] + bez(P(412, 48), P(452, 62), P(476, 78), P(518, 56), 60)
END = P(518, 56)

# ------------------------------------------------------------- scène
sc = bpy.context.scene
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
sc.render.engine = "CYCLES"
sc.render.fps = FPS
sc.frame_start, sc.frame_end = 0, N - 1
sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
sc.view_settings.view_transform = "AgX"
try:
    sc.view_settings.look = "AgX - Base Contrast"
except TypeError:
    pass
world = bpy.data.worlds.new("OutroWorld")
sc.world = world
world.use_nodes = True
wn = world.node_tree
bg = next(n for n in wn.nodes if n.type == "BACKGROUND")
# ciel du soir (direction de vue) : lueur chaude à l'horizon, bleu nuit au-dessus
tc = wn.nodes.new("ShaderNodeTexCoord")
sep = wn.nodes.new("ShaderNodeSeparateXYZ")
ramp = wn.nodes.new("ShaderNodeValToRGB")
mr = wn.nodes.new("ShaderNodeMapRange")
mr.inputs["From Min"].default_value = -1.0
wn.links.new(tc.outputs["Generated"], sep.inputs[0])
wn.links.new(sep.outputs["Z"], mr.inputs["Value"])
wn.links.new(mr.outputs["Result"], ramp.inputs["Fac"])
cr = ramp.color_ramp
cr.elements[0].position, cr.elements[0].color = 0.0, lin("#06080B")
cr.elements[1].position, cr.elements[1].color = 0.75, lin("#090C12")
for pos, hx in ((0.49, "#0A0D12"), (0.5, "#3A2216"), (0.515, "#2A1B19"), (0.55, "#141820"), (0.61, "#0C1016")):
    e = cr.elements.new(pos); e.color = lin(hx)
wn.links.new(ramp.outputs["Color"], bg.inputs["Color"])
bg.inputs["Strength"].default_value = 1.0


def mat(name, hexstr, rough=0.8, emit=0.0, emit_hex=None, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = lin(hexstr)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = lin(emit_hex or hexstr)
        b.inputs["Emission Strength"].default_value = emit
    return m


def link(ob):
    sc.collection.objects.link(ob)
    return ob


def mesh_obj(name, bm, mats, smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = smooth
    return link(bpy.data.objects.new(name, me))


M_ROCK = mat("Rock", "#2B323D", rough=0.9)
M_ROCK2 = mat("RockDark", "#1C222B", rough=0.95)
M_SNOW = mat("Snow", "#DCE6F0", rough=0.7)
M_GRASS = mat("Foot", "#1E2A26", rough=0.95)


def backdrop():
    """Toile de fond du soir derrière la chaîne : lueur chaude à l'horizon, bleu nuit au-dessus (dégradé en Z)."""
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in ((-180, 140, -90), (230, 140, -90), (230, 140, 130), (-180, 140, 130))]
    bm.faces.new(vs)
    m = bpy.data.materials.new("Backdrop")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value, mr.inputs["From Max"].default_value = -6.0, 60.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position, cr.elements[0].color = 0.0, lin("#0B0E13")
    cr.elements[1].position, cr.elements[1].color = 1.0, lin("#090C12")
    for pos, hx in ((0.09, "#3A2216"), (0.16, "#24191A"), (0.34, "#11151D")):
        e = cr.elements.new(pos); e.color = lin(hx)
    em = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    ob = mesh_obj("Backdrop", bm, [m])
    ob.visible_shadow = False
    return ob




def crest_at(pts, x):
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if x0 <= x <= x1:
            return z0 + (z1 - z0) * (x - x0) / (x1 - x0)
    return None


def ridge(name, pts, x0, x1, cols, back, front, flat=0.0, snow_from=3.6, seed=1, base=-0.6):
    """Chaîne dont la ligne de crête (y = 0) suit pts : sa silhouette vue de face est exactement la ligne."""
    rnd = random.Random(seed)
    xs = sorted(set([x0, x1] + [p[0] for p in pts if x0 < p[0] < x1] + [x0 + (x1 - x0) * i / cols for i in range(1, cols)]))
    fr = [0.06, 0.15, 0.28, 0.44, 0.62, 0.82, 1.0]
    ys = sorted(set([-flat / 2 - (front - flat / 2) * u for u in fr] + [flat / 2 + (back - flat / 2) * u for u in fr]
                    + [-flat / 2, flat / 2]))
    bm = bmesh.new()
    grid = []
    for x in xs:
        cz = crest_at(pts, x)
        if cz is None:                       # au-delà des extrémités : la crête descend jusqu'au sol
            ex = pts[0] if x < pts[0][0] else pts[-1]
            d = abs(x - ex[0])
            cz = max(base, ex[1] - d * 0.9)
        row = []
        for y in ys:
            if abs(y) <= flat / 2 + 1e-6:
                z, jx, jy = cz, 0.0, 0.0
            else:
                L = (back if y > 0 else front)
                u = (abs(y) - flat / 2) / max(L - flat / 2, 1e-3)
                z = base + (cz - base) * (1 - u ** 1.25) - rnd.uniform(0.0, 0.35) * u * (1 - u) * 4 * max(cz - base, 0) * 0.25
                jx, jy = rnd.uniform(-0.3, 0.3) * u, rnd.uniform(-0.25, 0.25) * u
                if abs(y) >= L - 1e-6:
                    z = base
            row.append(bm.verts.new((x + jx, y + jy, z)))
        grid.append(row)
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            a, b, c, d = grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]
            if (i + j) % 2:
                bm.faces.new((a, b, c)); bm.faces.new((a, c, d))
            else:
                bm.faces.new((a, b, d)); bm.faces.new((b, c, d))
    bm.normal_update()
    for f in bm.faces:
        if f.normal.y > 0:                   # normales vers l'extérieur (faces avant vers -y)
            pass
        zc = sum(v.co.z for v in f.verts) / len(f.verts)
        f.material_index = 2 if (zc > snow_from and f.normal.z > 0.35) else (1 if f.normal.z < 0.45 else 0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, [M_ROCK, M_ROCK2, M_SNOW])


ridge("Ridge", RIDGE, -3.5, RIDGE[-1][0], 34, back=16, front=10, snow_from=3.4, seed=2)
ridge("RoadHills", ROAD, RIDGE[-1][0] - 0.6, END[0] + 5.0, 30, back=9, front=8, flat=0.7, snow_from=9, seed=5)
# arrière-plan : montagnes plus hautes, perdues dans la brume du soir (couleurs très proches du ciel)
M_FAR = mat("Far", "#10141B", rough=1.0)
for k, (yb, scale, seed) in enumerate(((34, 1.0, 11), (70, 1.6, 12))):
    rnd = random.Random(seed)
    pts = []
    x = -40.0
    while x < 95:
        pts.append((x, rnd.uniform(0.8, 4.2) * scale - 0.5))
        x += rnd.uniform(5, 11)
    ob = ridge("Far%d" % k, pts, -40, 95, 40, back=14, front=12, snow_from=99, seed=seed, base=-1)
    ob.data.materials[0] = M_FAR; ob.data.materials[1] = M_FAR; ob.data.materials[2] = M_FAR
    ob.location.y = yb

# ------------------------------------------------------------- traits du logo : crête blanche, route dorée
def stroke(name, pts, radius, m, y=0.0, lift=0.0):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 3
    cu.use_fill_caps = True
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for p, (x, z) in zip(sp.points, pts):
        p.co = (x, y, z + lift, 1)
    cu.bevel_factor_mapping_end = "SPLINE"
    ob = link(bpy.data.objects.new(name, cu))
    ob.data.materials.append(m)
    return ob


def densify(pts, step=0.25):
    out = []
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        n = max(1, int(math.hypot(x1 - x0, z1 - z0) / step))
        out += [(x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * i / n) for i in range(n)]
    return out + [pts[-1]]


M_LINE = mat("Line", WHITE, emit=3.0)
M_GOLD = mat("Gold", GOLD, rough=0.3, emit=2.4)
crest = stroke("Crest", densify(RIDGE), 0.075, M_LINE, lift=0.04)
trail = stroke("Trail", ROAD, 0.15, M_GOLD, lift=0.05)
bm = bmesh.new()
rows = []
for x, z in ROAD:
    rows.append((bm.verts.new((x, -0.3, z + 0.012)), bm.verts.new((x, 0.3, z + 0.012))))
for (a, b), (c, d) in zip(rows, rows[1:]):
    bm.faces.new((a, c, d, b))
mesh_obj("Asphalt", bm, [mat("Asphalt", "#25282E", rough=0.85)])
dot_me = bpy.data.meshes.new("Dot")
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=0.4)
bm.to_mesh(dot_me)
dot_me.materials.append(M_GOLD)
dot = link(bpy.data.objects.new("Dot", dot_me))
dot.location = (END[0], 0, END[1] + 0.1)

# ------------------------------------------------------------- voiture : Mustang du jeu (dernier plan du jeu avant l'outro)
MODELS = ROOT + "/Assets/ChillRoad/Models/"
FIX = {"PK_Palette": "CarPack/CarPack_Palette.png", "AP_Palette": "AlpPack/AlpPack_Palette.png"}
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=MODELS + "PlayerCars/Mustang.fbx")
for im in bpy.data.images:
    if im.source == "FILE" and not os.path.exists(bpy.path.abspath(im.filepath)):
        for k, rel in FIX.items():
            if im.name.startswith(k):
                im.filepath = MODELS + rel
                im.reload()
new = [o for o in bpy.data.objects if o not in before]
car = link(bpy.data.objects.new("Car", None))
for o in new:
    if o.parent is None:
        o.parent = car
    if o.type == "MESH":
        for i, m in enumerate(o.data.materials):
            if m and m.name.startswith("PK_Paint"):
                pm = m.copy()
                b = next(n for n in pm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
                b.inputs["Base Color"].default_value = (0.089, 0.095, 0.10, 1)
                b.inputs["Roughness"].default_value = 0.3
                o.data.materials[i] = pm
CAR_SCALE = 0.11
car.scale = (CAR_SCALE,) * 3
# phares : deux petites lampes spot devant la voiture
for side in (-1, 1):
    ld = bpy.data.lights.new("Head%d" % side, "SPOT")
    ld.energy = 0.8
    ld.spot_size = math.radians(55)
    ld.color = (1.0, 0.92, 0.75)
    ld.shadow_soft_size = 0.01
    lo = link(bpy.data.objects.new("Head%d" % side, ld))
    lo.parent = car
    lo.location = (side * 0.6, -2.3, 0.7)   # repère voiture (avant = -Y), mis à l'échelle par le parent
    lo.rotation_euler = (math.radians(95), 0, 0)

path_cu = bpy.data.curves.new("CarPath", "CURVE")
path_cu.dimensions = "3D"
sp = path_cu.splines.new("POLY")
sp.points.add(len(ROAD) - 1)
for p, (x, z) in zip(sp.points, ROAD):
    p.co = (x, 0, z, 1)
path = link(bpy.data.objects.new("CarPath", path_cu))
path.hide_render = True
fp = car.constraints.new("FOLLOW_PATH")
fp.target = path
fp.use_curve_follow = True
fp.use_fixed_location = True
fp.forward_axis = "TRACK_NEGATIVE_Y"
fp.up_axis = "UP_Z"


def fcurves(idb):
    ad = idb.animation_data
    if not ad or not ad.action:
        return []
    act = ad.action
    if hasattr(act, "fcurves") and len(act.fcurves):
        return list(act.fcurves)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out += list(bag.fcurves)
    return out


def keys(idb, path, frames_values, interp="BEZIER", holder=None):
    h = holder or idb
    for f, v in frames_values:
        setattr(h, path, v)
        h.keyframe_insert(path, frame=f)
    for fc in fcurves(idb):
        if fc.data_path.endswith(path):
            for kp in fc.keyframe_points:
                kp.interpolation = interp


# Voiture : de l'entrée de la route (0) à l'arrivée (1) entre les images 10 et 200 ; la traînée la suit.
T0, T1 = 6, 196
def u_at(f):
    t = min(max((f - T0) / (T1 - T0), 0.0), 1.0)
    return 0.02 + 0.98 * (t * t * (3 - 2 * t) * 0.55 + t * 0.45)
for f in range(0, N, 4):
    fp.offset_factor = u_at(f)
    fp.keyframe_insert("offset_factor", frame=f)
    trail.data.bevel_factor_end = max(u_at(f) - 0.012, 0.0)
    trail.data.keyframe_insert("bevel_factor_end", frame=f)
for fc in fcurves(car) + fcurves(trail.data):
    for kp in fc.keyframe_points:
        kp.interpolation = "LINEAR"
keys(trail.data, "bevel_depth", ((0, 0.035), (105, 0.035), (205, 0.15)))
# la voiture s'efface à l'arrivée (elle « devient » le point doré)
car_objs = [o for o in car.children_recursive]
for o in car_objs:
    for f, hide in ((T1 + 2, False), (T1 + 3, True)):
        o.hide_render = hide
        o.keyframe_insert("hide_render", frame=f)
# point d'arrivée : saut quand la voiture arrive
for f, s in ((T1 - 1, 0.001), (T1, 0.001), (T1 + 8, 1.35), (T1 + 14, 0.9), (T1 + 20, 1.0)):
    dot.scale = (s, s, s)
    dot.keyframe_insert("scale", frame=f)
# crête : la lumière court le long de la ligne, de gauche à droite
keys(crest.data, "bevel_factor_end", ((40, 0.0), (190, 1.0)))
crest.data.bevel_factor_start = 0.0

# ------------------------------------------------------------- texte CHILL ROAD (Inter Light, espacé 0,3 em)
fnt = bpy.data.fonts.load(ROOT + "/Assets/ChillRoad/Resources/Fonts/Inter-Light.ttf")
cu = bpy.data.curves.new("Title", "FONT")
cu.body = "CHILL ROAD"
cu.font = fnt
cu.size = 7.2
cu.space_character = 1.5
cu.align_x = "CENTER"
cu.extrude = 0.0
title = link(bpy.data.objects.new("Title", cu))
M_TITLE = mat("Title", WHITE, emit=2.2)
cu.materials.append(M_TITLE)
title.rotation_euler = (math.radians(90), 0, 0)
def title_width(space):
    cu.space_character = space
    dg = bpy.context.evaluated_depsgraph_get()
    ev = title.evaluated_get(dg)
    tm = ev.to_mesh()
    xs = [v.co.x for v in tm.vertices]
    ev.to_mesh_clear()
    return max(xs) - min(xs)
s0, s1 = 1.0, 2.0                        # largeur visible du texte du SVG : 619 px -> 61,9 unités
w0, w1 = title_width(s0), title_width(s1)
wnow = title_width(s0 + (61.9 - w0) * (s1 - s0) / (w1 - w0))
print("title width", wnow, "space", cu.space_character)
TX, TZ = 26.0, -9.0
title.location = (TX, -2.0, TZ - 0.8)
# apparition : fondu (alpha) + légère montée
mix_nodes = M_TITLE.node_tree
bsdf = next(n for n in mix_nodes.nodes if n.type == "BSDF_PRINCIPLED")
keys(M_TITLE.node_tree, "default_value", ((200, 0.0), (236, 1.0)), holder=bsdf.inputs["Alpha"])
keys(title, "location", ((200, (TX, -2.0, TZ - 1.1)), (238, (TX, -2.0, TZ))))
for fc in fcurves(title):
    for kp in fc.keyframe_points:
        kp.interpolation = "BEZIER"

# ------------------------------------------------------------- lumières du soir
def light(name, kind, loc, rot_target, energy, color, size=1.0):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = energy
    ld.color = color
    if kind == "SUN":
        ld.angle = math.radians(2)
    else:
        ld.size = size
    lo = link(bpy.data.objects.new(name, ld))
    lo.location = loc
    lo.rotation_euler = (Vector(rot_target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return lo


light("Sun", "SUN", (60, 80, 12), (20, 0, 0), 3.6, (1.0, 0.62, 0.32))         # soleil couchant derrière la chaîne
light("Sky", "SUN", (-20, -60, 50), (25, 0, 0), 0.45, (0.55, 0.65, 1.0))      # ciel bleu sur les faces avant

# ------------------------------------------------------------- (pas de neige : le logo final doit rester net)
if False:
    emit_me = bpy.data.meshes.new("SnowEmitter")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=1.0)
    bm.to_mesh(emit_me)
    emitter = link(bpy.data.objects.new("SnowEmitter", emit_me))
    emitter.location = (34, -30, 22)
    emitter.scale = (45, 32, 1)
    emitter.rotation_euler = (math.pi, 0, 0)
    flake_me = bpy.data.meshes.new("Flake")
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    bm.to_mesh(flake_me)
    flake_me.materials.append(mat("Flake", "#FFFFFF", emit=1.2))
    flake = link(bpy.data.objects.new("Flake", flake_me))
    flake.location = (0, 0, -200)
    flake.hide_render = True
    ps = emitter.modifiers.new("Snow", "PARTICLE_SYSTEM").particle_system
    st = ps.settings
    st.count = 9000
    st.frame_start, st.frame_end = -2400, N
    st.lifetime = 4000
    st.normal_factor = 1.1
    st.factor_random = 0.25
    st.effector_weights.gravity = 0.0
    st.render_type = "OBJECT"
    st.instance_object = flake
    st.particle_size = 0.05
    st.size_random = 0.6
    emitter.show_instancer_for_render = False
    ps.seed = 7

# ------------------------------------------------------------- caméra
camd = bpy.data.cameras.new("Cam")
camd.sensor_width = 36
cam = link(bpy.data.objects.new("Cam", camd))
sc.camera = cam
target = link(bpy.data.objects.new("CamTarget", None))
tt = cam.constraints.new("TRACK_TO")
tt.target = target
tt.track_axis = "TRACK_NEGATIVE_Z"
tt.up_axis = "UP_Y"
camd.dof.use_dof = True
camd.dof.focus_object = target
camd.dof.aperture_fstop = 2.0


def car_at(f):
    sc.frame_set(f)
    m = car.matrix_world
    return m.translation.copy(), (m.to_3x3() @ Vector((0, -1, 0))).normalized()


# plan final : de face, longue focale, logo au-dessus du quart inférieur
CX, CZ = 26.0, 1.0
FINAL_LENS = 85
DIST = 43.0 * FINAL_LENS / 18.0           # largeur visible ≈ 86 unités (logo ≈ 60 % de l'image)
FINAL_EYE = Vector((CX, -DIST, CZ + 2.0))
FINAL_AT = Vector((CX, 0.0, CZ))
p0, d0 = car_at(0)
p1, d1 = car_at(60)
p2, d2 = car_at(120)
side0 = d0.cross(Vector((0, 0, 1)))
p3, d3 = car_at(70)
cam_keys = [
    # devant la voiture, de trois quarts, au ras de la route : la crête et sa lumière derrière elle
    (0, p0 + d0 * 0.95 + side0 * 0.6 + Vector((0, 0, 0.2)), p0 + Vector((-0.35, 0, 0.16)), 22, 2.0),
    # travelling latéral
    (70, p3 + Vector((0.6, -2.1, 0.75)), p3 + Vector((0.2, 0, 0.12)), 30, 2.6),
    # recul et montée : la chaîne entière
    (140, Vector((36.0, -26.0, 5.5)), Vector((30.0, 0.0, 2.2)), 30, 5.0),
    (215, FINAL_EYE + Vector((-1.0, 45.0, 0.8)), FINAL_AT, 70, 9.0),
    (290, FINAL_EYE + Vector((0, 4.0, 0)), FINAL_AT, 84, 11.0),
    (N - 1, FINAL_EYE + Vector((0, 12.0, -0.2)), FINAL_AT, 85, 11.0),
]
for f, eye, at, lens, fst in cam_keys:
    cam.location = eye; cam.keyframe_insert("location", frame=f)
    target.location = at; target.keyframe_insert("location", frame=f)
    camd.lens = lens; camd.keyframe_insert("lens", frame=f)
    camd.dof.aperture_fstop = fst; camd.dof.keyframe_insert("aperture_fstop", frame=f)
for idb in (cam, target, camd):
    for fc in fcurves(idb):
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
            kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"

# ------------------------------------------------------------- rendu
sc.render.use_motion_blur = True
sc.render.motion_blur_shutter = 0.3
sc.cycles.samples = SAMPLES
sc.cycles.use_denoising = True
sc.render.image_settings.file_format = "PNG"
prefs = bpy.context.preferences.addons["cycles"].preferences
try:
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for d in prefs.devices:
        d.use = d.type == "OPTIX"
    sc.cycles.device = "GPU"
except Exception as e:
    print("GPU:", e)
sc.render.use_persistent_data = True
# exposition : plus clair pendant le gros plan, sombre au plan du logo
for f, ev in ((0, 0.9), (110, 0.8), (220, 0.0)):
    sc.view_settings.exposure = ev
    sc.view_settings.keyframe_insert("exposure", frame=f)
print("DEVICES", sc.cycles.device, [(d.name, d.type, d.use) for d in prefs.devices])
os.makedirs(OUTDIR, exist_ok=True)
if TEST:
    for f in TEST:
        sc.frame_set(f)
        sc.render.filepath = HERE + "out/test_%04d.png" % f
        bpy.ops.render.render(write_still=True)
else:
    # Ordre de rendu pour un montage possible à tout moment (le monteur fond deux images voisines) :
    # 1) partie animée une image sur deux, 2) poussée lente finale une sur quatre + la dernière, 3) images impaires.
    bpy.ops.wm.save_as_mainfile(filepath=HERE + "out/TrailerOutro.blend", copy=True)
    order = list(range(0, 240, 2)) + list(range(240, 477, 4)) + [N - 1] + list(range(1, 240, 2))
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 95
    os.makedirs(OUTDIR, exist_ok=True)
    for f in order:
        path = os.path.join(OUTDIR, "%04d.jpg" % f)
        if os.path.exists(path):
            continue
        sc.frame_set(f)
        sc.render.filepath = path + ".tmp.jpg"
        bpy.ops.render.render(write_still=True)
        os.replace(path + ".tmp.jpg", path)      # le fichier n'apparaît qu'une fois complet
        print("FRAME", f, flush=True)
print("OUTRO DONE")
