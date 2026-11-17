"""Low-poly Vision 74 for ChillRoad: Polytope-style facets + vertical gradient palette (one texture, flat shading)."""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix
import hc_lib as H

COL = "V74_Low"
CUT = "V74_Low_Cutters"
FRONT = dict(x=1.45, rt=0.349, rr=0.25, w=0.27, y=0.85)
REAR = dict(x=-1.455, rt=0.361, rr=0.26, w=0.315, y=0.83)
ZMAX = 1.40            # height mapped to the full gradient
# palette columns: (name, bottom sRGB, top sRGB) — soft vertical gradients like the Polytope shaders
PALETTE = [
    ("body", (0.50, 0.53, 0.57), (0.93, 0.94, 0.94)),
    ("trim", (0.06, 0.06, 0.07), (0.21, 0.22, 0.24)),
    ("glass", (0.12, 0.26, 0.32), (0.36, 0.63, 0.68)),
    ("red", (0.52, 0.07, 0.05), (0.93, 0.24, 0.15)),
    ("white", (0.72, 0.73, 0.75), (0.98, 0.98, 0.97)),
    ("tire", (0.07, 0.07, 0.08), (0.18, 0.18, 0.20)),
    ("dark", (0.025, 0.025, 0.03), (0.08, 0.08, 0.09)),
    ("amber", (0.93, 0.72, 0.42), (1.0, 0.90, 0.70)),
]
CI = {p[0]: i for i, p in enumerate(PALETTE)}
SHARP_ANGLE = 35.0     # cartoon style (2026-11-15): smooth normals, the wedge creases stay sharp
NCOL = len(PALETTE)


# ----------------------------------------------------------------- materials / palette
def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def color_materials():
    """One flat material per palette colour (used while modelling, replaced by the palette at bake)."""
    mats = {}
    for name, lo, hi in PALETTE:
        mid = [srgb_to_lin((a + b) / 2) for a, b in zip(lo, hi)]
        m, _ = H.principled("V74L_c_" + name, tuple(mid), rough=0.7)
        mats[name] = m
    mats["led"], _ = H.principled("V74L_LED", (1, 1, 1), emit=(1.0, 0.85, 0.6), strength=6)
    mats["tail"], _ = H.principled("V74L_Tail", (0.6, 0.02, 0.02), emit=(1.0, 0.05, 0.03), strength=6)
    return mats


def palette_image(save_path=None):
    w, h = 8 * NCOL, 64
    img = bpy.data.images.get("V74L_Palette")
    if img is None or img.size[0] != w or img.size[1] != h:
        if img:
            bpy.data.images.remove(img)
        img = bpy.data.images.new("V74L_Palette", w, h, alpha=False)
    px = []
    for y in range(h):
        t = y / (h - 1)
        for x in range(w):
            _, lo, hi = PALETTE[x // 8]
            px += [lo[k] * (1 - t) + hi[k] * t for k in range(3)] + [1.0]
    img.pixels = px
    if save_path:
        img.filepath_raw = save_path
        img.file_format = 'PNG'
        img.save()
    return img


def palette_material():
    m, b = H.principled("V74L_Palette", (1, 1, 1), rough=0.7)
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = 'Linear'
    tex.extension = 'EXTEND'
    nt.links.new(tex.outputs["Color"], H.sock(b, "Base Color"))
    return m


# ----------------------------------------------------------------- body (8-point half sections, no subdivision)
# p0 floor centre, p1 floor edge, p2 top of black sill band, p3 flare/upper side, p4 shoulder,
# p5 door top / beltline (hood crease at the front), p6 roof side / A-pillar, p7 top centre
LKEYS = [
    (2.40, [(0, .20), (.72, .20), (.90, .30), (.92, .56), (.90, .66), (.86, .70), (.45, .71), (0, .712)]),
    (2.30, [(0, .17), (.80, .17), (.985, .30), (.985, .58), (.95, .67), (.88, .72), (.45, .735), (0, .74)]),
    (1.95, [(0, .15), (.82, .15), (1.0, .30), (1.0, .72), (.955, .75), (.87, .775), (.43, .79), (0, .80)]),
    (1.45, [(0, .14), (.82, .14), (1.0, .30), (1.0, .80), (.96, .832), (.83, .85), (.42, .866), (0, .874)]),
    (0.95, [(0, .14), (.82, .14), (1.0, .30), (1.0, .80), (.955, .84), (.80, .858), (.48, .872), (0, .879)]),
    (0.85, [(0, .14), (.80, .14), (.955, .30), (.955, .79), (.95, .835), (.81, .852), (.60, .862), (0, .868)]),
    (0.73, [(0, .14), (.80, .14), (.955, .31), (.955, .79), (.95, .835), (.84, .852), (.74, .86), (0, .864)]),
    (0.03, [(0, .14), (.80, .14), (.955, .33), (.955, .79), (.95, .84), (.81, .866), (.66, 1.285), (0, 1.32)]),
    (-0.42, [(0, .14), (.80, .14), (.955, .36), (.955, .80), (.953, .845), (.815, .87), (.66, 1.29), (0, 1.325)]),
    (-0.50, [(0, .14), (.80, .14), (.955, .37), (.955, .80), (.953, .846), (.815, .871), (.66, 1.29), (0, 1.325)]),
    (-0.80, [(0, .14), (.80, .14), (.955, .40), (.955, .80), (.953, .848), (.815, .875), (.66, 1.29), (0, 1.325)]),
    (-0.93, [(0, .14), (.82, .14), (1.0, .44), (1.0, .83), (.965, .86), (.82, .885), (.65, 1.265), (0, 1.30)]),
    (-1.455, [(0, .15), (.82, .15), (1.0, .46), (1.0, .845), (.97, .88), (.83, .915), (.60, 1.12), (0, 1.14)]),
    (-1.98, [(0, .21), (.82, .21), (1.0, .46), (1.0, .85), (.97, .90), (.78, .958), (.50, .99), (0, 1.0)]),
    (-2.40, [(0, .30), (.80, .30), (.985, .44), (.985, .84), (.96, .905), (.78, .97), (.50, .977), (0, .98)]),
    (-2.47, [(0, .32), (.76, .32), (.955, .44), (.955, .84), (.93, .90), (.77, .99), (.50, 1.0), (0, 1.003)]),
]


def face_color(j, xa, xb):
    xm = (xa + xb) / 2
    if j == 0:
        return "dark"
    if j == 1:
        return "trim"
    if j == 5 and -1.30 <= xm <= 0.73:
        return "trim" if -0.50 <= xm <= -0.42 else "glass"          # side glass, B-pillar
    if j == 6 and (0.03 <= xm <= 0.73 or -1.98 <= xm <= -0.80):
        return "glass"                                                # windshield, rear window
    return "body"


def build_body(M, col):
    names = [p[0] for p in PALETTE]
    P = len(LKEYS[0][1])
    bm = bmesh.new()
    V = [[bm.verts.new((x, y, z)) for (y, z) in prof] for x, prof in LKEYS]
    for s in range(len(V) - 1):
        for j in range(P - 1):
            f = bm.faces.new((V[s][j], V[s][j + 1], V[s + 1][j + 1], V[s + 1][j]))
            f.material_index = names.index(face_color(j, LKEYS[s][0], LKEYS[s + 1][0]))
    bm.faces.new(list(V[0])).material_index = names.index("body")
    bm.faces.new(list(reversed(V[-1]))).material_index = names.index("body")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    probe = [f for f in bm.faces if f.material_index == names.index("body") and f.calc_center_median().y > 0.9]
    if probe and probe[0].normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "V74L_Body", col, [M[n] for n in names])
    mr = ob.modifiers.new("Mirror", 'MIRROR')
    mr.use_axis = (False, True, False)
    mr.use_clip = True
    mr.use_mirror_merge = True
    return ob


def arch_cutters(M, col):
    cc = H.get_col(CUT, col)
    cc.hide_render = True
    for tag, W in (("F", FRONT), ("R", REAR)):
        x, zc = W["x"], W["rt"]
        a, top, chx, low = 0.44, 0.40, 0.26, 0.08
        pts = [(x + a, zc - 0.30), (x + a, zc + low), (x + chx, zc + top), (x - chx, zc + top), (x - a, zc + low), (x - a, zc - 0.30)]
        y_in = W["y"] - W["w"] / 2 - 0.04
        bm = bmesh.new()
        H.extrude(bm, pts, 'y', y_in, 1.6)
        H.extrude(bm, pts, 'y', -1.6, -y_in)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        c = H.bm_to_obj(bm, "V74L_Cut_Arch_" + tag, cc, [M["dark"]])
        c.display_type = 'WIRE'
        c.hide_render = True
    body = bpy.data.objects["V74L_Body"]
    b = body.modifiers.new("Arches", 'BOOLEAN')
    b.operand_type = 'COLLECTION'
    b.collection = cc
    b.operation = 'DIFFERENCE'
    b.solver = 'EXACT'
    b.material_mode = 'TRANSFER'
    return cc


# ----------------------------------------------------------------- simple parts
def part(name, col, mat, boxes):
    """boxes: list of (center, size, rot or None)."""
    bm = bmesh.new()
    for c, s, r in boxes:
        H.box(bm, Vector(c), s, r)
    return H.bm_to_obj(bm, name, col, [mat])


def prism(name, col, mat, poly, axis, a0, a1):
    bm = bmesh.new()
    H.extrude(bm, poly, axis, a0, a1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return H.bm_to_obj(bm, name, col, [mat])


def top_z(x):
    """Centre-line height of the body at x (profile point p7)."""
    for (x0, P0), (x1, P1) in zip(LKEYS, LKEYS[1:]):
        if x1 <= x <= x0:
            t = (x0 - x) / (x0 - x1)
            return P0[7][1] + (P1[7][1] - P0[7][1]) * t
    return LKEYS[-1][1][7][1]


def build_details(M, col):
    xf = 2.40
    # front: black light band, pixel headlights, intake panel + silver slat, splitter, centre strut
    part("V74L_FrontBand", col, M["trim"], [((xf + 0.004, 0, 0.65), (0.03, 1.74, 0.085), None)])
    px = []
    for s in (1, -1):
        for yc in (0.60, 0.76):
            for k in (-1, 0, 1):
                px.append(((xf + 0.02, s * yc + k * 0.042, 0.65), (0.012, 0.036, 0.05), None))
    part("V74L_Headlights", col, M["led"], px)
    part("V74L_Intake", col, M["dark"], [((xf + 0.004, 0, 0.39), (0.03, 1.60, 0.30), None)])
    part("V74L_IntakeSlat", col, M["body"], [((xf + 0.022, 0, 0.395), (0.012, 1.56, 0.022), None)])
    part("V74L_TowHook", col, M["red"], [((xf + 0.025, -0.36, 0.60), (0.03, 0.05, 0.016), None)])
    split = [(2.10, -0.99), (2.40, -0.99), (2.49, -0.82), (2.52, -0.40), (2.52, 0.40), (2.49, 0.82), (2.40, 0.99), (2.10, 0.99)]
    prism("V74L_Splitter", col, M["trim"], split, 'z', 0.15, 0.18)
    prism("V74L_Strut", col, M["trim"], [(2.38, 0.18), (2.50, 0.18), (2.44, 0.25), (2.38, 0.26)], 'y', -0.012, 0.012)
    # side: rear fender vent + mirrors
    for s in (1, -1):
        part("V74L_Vent_" + ("L" if s > 0 else "R"), col, M["dark"],
             [((-0.685, s * 0.958, 0.68), (0.20, 0.012, 0.26), None)])
        part("V74L_Mirror_" + ("L" if s > 0 else "R"), col, M["trim"],
             [((0.57, s * 1.00, 0.925), (0.08, 0.15, 0.07), None),
              ((0.60, s * 0.91, 0.885), (0.05, 0.14, 0.03), Matrix.Rotation(s * math.radians(-15), 3, 'X'))])
    # rear: light band + red strip + pixel units, lower panel, diffuser fins, rain light
    xr = -2.47
    part("V74L_TailBand", col, M["trim"], [((xr - 0.004, 0, 0.87), (0.03, 1.80, 0.08), None)])
    tl = [((xr - 0.02, 0, 0.895), (0.012, 1.66, 0.012), None)]
    for s in (1, -1):
        for yc in (0.58, 0.74):
            tl.append(((xr - 0.02, s * yc, 0.862), (0.012, 0.13, 0.035), None))
    part("V74L_TailLights", col, M["tail"], tl)
    part("V74L_RearPanel", col, M["dark"], [((xr - 0.004, 0, 0.51), (0.03, 1.60, 0.30), None)])
    part("V74L_RainLight", col, M["tail"], [((xr - 0.02, 0, 0.42), (0.012, 0.07, 0.05), None)])
    bm = bmesh.new()
    for y in (0.14, -0.14, 0.42, -0.42, 0.68, -0.68):
        H.extrude(bm, [(-1.95, 0.17), (-2.60, 0.20), (-2.60, 0.33), (-2.47, 0.33), (-1.95, 0.20)], 'y', y - 0.008, y + 0.008)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "V74L_Diffuser", col, [M["trim"]])
    # louvres over the rear window (6 slats following the fastback)
    slats = []
    for i in range(6):
        x = -0.92 - 0.95 * i / 5
        z = top_z(x) + 0.03
        slope = math.atan2(top_z(x - 0.05) - top_z(x + 0.05), 0.10)
        yw = 0.66 - 0.16 * (i / 5)
        slats.append(((x, 0, z), (0.07, 2 * yw - 0.06, 0.012), Matrix.Rotation(-slope - math.radians(18), 3, 'Y')))
    part("V74L_Louvres", col, M["trim"], slats)
    # rear wing: slab, end plates, pylons
    wing_rot = Matrix.Rotation(math.radians(8), 3, 'Y')
    part("V74L_Wing", col, M["trim"], [((-2.21, 0, 1.28), (0.36, 1.86, 0.035), wing_rot),
                                        ((-2.21, 0.935, 1.26), (0.46, 0.012, 0.20), None),
                                        ((-2.21, -0.935, 1.26), (0.46, 0.012, 0.20), None)])
    pyl = []
    for s in (1, -1):
        zb = top_z(-2.24)
        pyl.append(((-2.25, s * 0.42, (zb + 1.26) / 2), (0.16, 0.022, 1.26 - zb), None))
    part("V74L_WingPylons", col, M["trim"], pyl)


# ----------------------------------------------------------------- wheels (16 sides)
def build_wheel(tag, W, side, M, col):
    rt, rr, w = W["rt"], W["rr"], W["w"]
    bm = bmesh.new()
    names = ["tire", "white", "dark", "red"]
    s0 = len(bm.faces)
    H.revolve(bm, [(rr, w / 2 - 0.012), (rt - 0.035, w / 2), (rt, w / 2 - 0.045), (rt, -(w / 2 - 0.045)),
                   (rt - 0.035, -w / 2), (rr, -(w / 2 - 0.012))], (0, 0, 0), side, nseg=16)
    H.new_faces_mat(bm, s0, 0)
    a = w / 2 - 0.018
    s0 = len(bm.faces)
    H.revolve(bm, [(0.004, a), (0.19, a - 0.006), (0.19, a - 0.03), (0.004, a - 0.03)], (0, 0, 0), side, nseg=16)
    H.revolve(bm, [(0.232, a - 0.002), (rr + 0.012, a - 0.002), (rr + 0.012, a - 0.03), (0.232, a - 0.03)], (0, 0, 0), side, nseg=16)
    H.new_faces_mat(bm, s0, 1)
    s0 = len(bm.faces)
    H.revolve(bm, [(0.19, a - 0.02), (0.232, a - 0.02), (0.232, a - 0.04), (0.19, a - 0.04)], (0, 0, 0), side, nseg=16)
    H.new_faces_mat(bm, s0, 2)
    s0 = len(bm.faces)
    H.revolve(bm, [(0.004, a + 0.012), (0.05, a + 0.004), (0.05, a - 0.02), (0.004, a - 0.02)], (0, 0, 0), side, nseg=8)
    H.new_faces_mat(bm, s0, 3)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "V74L_Wheel_" + tag, col, [M[n] for n in names])
    ob.location = (W["x"], side * W["y"], W["rt"])
    return ob


# ----------------------------------------------------------------- bake: apply modifiers, palette UVs, one material
def bake(col, pal, keep=("led", "tail")):
    dg = bpy.context.evaluated_depsgraph_get()
    names = [p[0] for p in PALETTE]
    for ob in list(col.objects):
        if ob.type != 'MESH' or ob.hide_render:
            continue
        ev = ob.evaluated_get(dg)
        me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
        old = [m.name if m else "" for m in me.materials]
        bm = bmesh.new()
        bm.from_mesh(me)
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method='BEAUTY', ngon_method='BEAUTY')
        uv = bm.loops.layers.uv.verify()
        new_mats, remap = [pal], {}
        for i, n in enumerate(old):
            key = n.replace("V74L_c_", "")
            if key in names:
                remap[i] = (0, names.index(key))
            else:
                mat = bpy.data.materials.get(n)
                if mat not in new_mats:
                    new_mats.append(mat)
                remap[i] = (new_mats.index(mat), None)
        zoff = ob.matrix_world.translation.z
        for f in bm.faces:
            slot, ci = remap.get(f.material_index, (0, names.index("body")))
            f.material_index = slot
            f.smooth = True             # style cartoon : normales lisses, arêtes vives au-delà de SHARP_ANGLE
            for lp in f.loops:
                if ci is None:
                    lp[uv].uv = (0.5, 0.5)
                else:
                    v = min(0.99, max(0.01, (lp.vert.co.z + zoff) / ZMAX))
                    lp[uv].uv = ((ci + 0.5) / NCOL, v)
        lim = math.radians(SHARP_ANGLE)
        for e in bm.edges:
            e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0) <= lim
        # set the material slots first: clearing them afterwards would reset the face indices
        me.materials.clear()
        for m in new_mats:
            me.materials.append(m)
        bm.to_mesh(me)
        bm.free()
        ob.modifiers.clear()
        old_me = ob.data
        ob.data = me
        if old_me.users == 0:
            bpy.data.meshes.remove(old_me)
    cc = bpy.data.collections.get(CUT)
    if cc:
        for o in list(cc.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(cc)


def build_all():
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for name in ("NV74", "Hypercar", "FuturisticCar_v1"):
        c = bpy.data.collections.get(name)
        if c:
            for lc in bpy.context.view_layer.layer_collection.children:
                if lc.name == name:
                    lc.exclude = True
    M = color_materials()
    col = H.get_col(COL)
    H.clear_col(COL)
    build_body(M, col)
    arch_cutters(M, col)
    build_details(M, col)
    for tag, W, side in (("FL", FRONT, 1), ("FR", FRONT, -1), ("RL", REAR, 1), ("RR", REAR, -1)):
        build_wheel(tag, W, side, M, col)
    pal = palette_material()
    bake(col, pal)
    tris = 0
    for o in col.objects:
        if o.type == 'MESH':
            tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return tris


# ----------------------------------------------------------------- Unity export
WHEELS = {"V74L_Wheel_FL": "wheel-front-left", "V74L_Wheel_FR": "wheel-front-right",
          "V74L_Wheel_RL": "wheel-back-left", "V74L_Wheel_RR": "wheel-back-right"}


def export_for_unity(fbx_path, png_path):
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    palette_image(png_path)
    src = bpy.data.collections[COL]
    R = Matrix.Rotation(math.radians(-90), 4, 'Z')
    exp = H.get_col("V74L_Export")
    for o in list(exp.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    body_parts, wheels = [], []
    for ob in src.objects:
        if ob.type != 'MESH':
            continue
        me = ob.data.copy()
        if ob.name in WHEELS:
            me.transform(R)
            o = bpy.data.objects.new(WHEELS[ob.name], me)
            o.location = R @ ob.matrix_world.translation
            exp.objects.link(o)
            wheels.append(o)
        else:
            me.transform(R @ ob.matrix_world)
            o = bpy.data.objects.new(ob.name + "_x", me)
            exp.objects.link(o)
            body_parts.append(o)
    with bpy.context.temp_override(active_object=body_parts[0], selected_objects=body_parts,
                                   selected_editable_objects=body_parts, object=body_parts[0]):
        bpy.ops.object.join()
    body = body_parts[0]
    body.name = "body"
    body.data.name = "Vision74_Body"
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in [body] + wheels:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='AUTO', embed_textures=False)
    stats = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in [body] + wheels}
    for o in list(exp.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me.users == 0:
            bpy.data.meshes.remove(me)
    bpy.data.collections.remove(exp)
    return stats
