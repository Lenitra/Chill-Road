"""Procedural modern hypercar for Blender 5.x — shared helpers + body."""
import bpy, bmesh, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

COL = "Hypercar"
CUT_COL = "Hypercar_Cutters"

# Wheel layout (x, radius tire, radius rim, width, center |y|)
FRONT = dict(x=1.42, rt=0.352, rr=0.268, w=0.275, y=0.852)
REAR = dict(x=-1.33, rt=0.368, rr=0.281, w=0.335, y=0.865)


# ----------------------------------------------------------------- utils
def get_col(name, parent=None):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
    parent = parent or bpy.context.scene.collection
    if col.name not in parent.children.keys():
        parent.children.link(col)
    return col


def clear_col(name):
    col = bpy.data.collections.get(name)
    if not col:
        return
    for o in list(col.all_objects):
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Curve):
                bpy.data.curves.remove(data)


def remove_obj(name):
    o = bpy.data.objects.get(name)
    if o:
        bpy.data.objects.remove(o, do_unlink=True)


def sock(node, name):
    for s in node.inputs:
        if s.identifier == name or s.name == name:
            return s
    return None


def principled(name, base, metal=0.0, rough=0.5, coat=0.0, coat_rough=0.03,
               emit=None, strength=0.0, spec=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (300, 0)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); b.location = (0, 0)
    nt.links.new(b.outputs[0], out.inputs[0])
    sock(b, "Base Color").default_value = (*base, 1)
    sock(b, "Metallic").default_value = metal
    sock(b, "Roughness").default_value = rough
    if sock(b, "Coat Weight"):
        sock(b, "Coat Weight").default_value = coat
        sock(b, "Coat Roughness").default_value = coat_rough
    if spec is not None and sock(b, "Specular IOR Level"):
        sock(b, "Specular IOR Level").default_value = spec
    if emit:
        sock(b, "Emission Color").default_value = (*emit, 1)
        sock(b, "Emission Strength").default_value = strength
    m.diffuse_color = (*base, 1)
    m.metallic = metal
    m.roughness = rough
    return m, b


def carbon_material():
    m, b = principled("HC_Carbon", (0.02, 0.02, 0.022), metal=0.2, rough=0.33,
                      coat=1.0, coat_rough=0.04)
    nt = m.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord"); tc.location = (-800, 0)
    mp = nt.nodes.new("ShaderNodeMapping"); mp.location = (-600, 0)
    sock(mp, "Scale").default_value = (70, 70, 70)
    ck = nt.nodes.new("ShaderNodeTexChecker"); ck.location = (-350, 0)
    sock(ck, "Color1").default_value = (0.012, 0.012, 0.014, 1)
    sock(ck, "Color2").default_value = (0.045, 0.045, 0.05, 1)
    sock(ck, "Scale").default_value = 1.0
    nt.links.new(tc.outputs["Object"], sock(mp, "Vector"))
    nt.links.new(mp.outputs[0], sock(ck, "Vector"))
    nt.links.new(ck.outputs["Color"], sock(b, "Base Color"))
    return m


def make_materials():
    M = {}
    M["paint"], _ = principled("HC_Paint", (0.95, 0.16, 0.008), metal=0.45, rough=0.3,
                               coat=1.0, coat_rough=0.025)
    M["glass"], _ = principled("HC_Glass", (0.004, 0.005, 0.006), rough=0.03,
                               coat=1.0, coat_rough=0.0, spec=0.6)
    M["carbon"] = carbon_material()
    M["black"], _ = principled("HC_GlossBlack", (0.008, 0.008, 0.009), rough=0.12,
                               coat=0.8, coat_rough=0.03)
    M["liner"], _ = principled("HC_Liner", (0.012, 0.012, 0.013), rough=0.75)
    M["led"], _ = principled("HC_LED", (1, 1, 1), emit=(1.0, 0.97, 0.93), strength=40)
    M["red"], _ = principled("HC_TailRed", (0.6, 0.0, 0.0), emit=(1.0, 0.01, 0.005), strength=30)
    M["tire"], _ = principled("HC_Tire", (0.022, 0.022, 0.024), rough=0.88)
    M["rim"], _ = principled("HC_Rim", (0.035, 0.037, 0.04), metal=1.0, rough=0.28)
    M["lip"], _ = principled("HC_RimLip", (0.9, 0.9, 0.92), metal=1.0, rough=0.1)
    M["disc"], _ = principled("HC_Disc", (0.08, 0.08, 0.085), metal=0.6, rough=0.45)
    M["caliper"], _ = principled("HC_Caliper", (0.95, 0.16, 0.008), rough=0.25, coat=0.6)
    M["titanium"], _ = principled("HC_Titanium", (0.55, 0.52, 0.5), metal=1.0, rough=0.22)
    M["grille"], _ = principled("HC_Grille", (0.01, 0.01, 0.011), metal=0.5, rough=0.45)
    return M


def mats():
    names = dict(paint="HC_Paint", glass="HC_Glass", carbon="HC_Carbon", black="HC_GlossBlack",
                 liner="HC_Liner", led="HC_LED", red="HC_TailRed", tire="HC_Tire",
                 rim="HC_Rim", lip="HC_RimLip", disc="HC_Disc", caliper="HC_Caliper",
                 titanium="HC_Titanium", grille="HC_Grille")
    return {k: bpy.data.materials[v] for k, v in names.items()}


def bm_to_obj(bm, name, col, materials=(), smooth=False, sharp_angle=None):
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    if sharp_angle is not None:
        for e in bm.edges:
            if e.is_manifold and e.calc_face_angle(0) > math.radians(sharp_angle):
                e.smooth = False
    if smooth or sharp_angle is not None:
        for f in bm.faces:
            f.smooth = True
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    for m in materials:
        me.materials.append(m)
    return ob


def body_bvh(name="HC_Body"):
    dg = bpy.context.evaluated_depsgraph_get()
    return BVHTree.FromObject(bpy.data.objects[name], dg)


def hit(bvh, origin, direction, dist=10.0):
    loc, nrm, idx, d = bvh.ray_cast(Vector(origin), Vector(direction).normalized(), dist)
    return (loc, nrm) if loc is not None else (None, None)


def sweep_rect(bm, pts, nrms, w, out, inn, taper=None, cap=True):
    """Rectangular section swept along surface points (width w across, from -inn to +out along normal)."""
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        nn = Vector(nrms[i]).normalized()
        s = nn.cross(t).normalized()
        n2 = t.cross(s).normalized()
        hw = w / 2 * (taper[i] if taper else 1.0)
        ring = [p + s * hw + n2 * out, p - s * hw + n2 * out,
                p - s * hw - n2 * inn, p + s * hw - n2 * inn]
        rings.append([bm.verts.new(v) for v in ring])
    for a, b in zip(rings, rings[1:]):
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    if cap:
        bm.faces.new(rings[0])
        bm.faces.new(list(reversed(rings[-1])))
    return rings


def revolve(bm, prof, C, side, nseg=64, a0=0.0, a1=2 * math.pi, closed_prof=True, cap=False):
    """Revolve profile [(r, axial)] around the wheel axle (world Y). Angle measured in XZ plane."""
    full = abs((a1 - a0) - 2 * math.pi) < 1e-6
    steps = nseg if full else nseg + 1
    cols = []
    for k in range(steps):
        th = a0 + (a1 - a0) * k / nseg
        c, s = math.cos(th), math.sin(th)
        cols.append([bm.verts.new((C[0] + r * c, C[1] + side * a, C[2] + r * s)) for (r, a) in prof])
    P = len(prof)
    for k in range(nseg):
        A = cols[k]
        B = cols[(k + 1) % steps]
        for j in (range(P) if closed_prof else range(P - 1)):
            j2 = (j + 1) % P
            bm.faces.new((A[j], B[j], B[j2], A[j2]))
    if cap and not full:
        bm.faces.new(cols[0])
        bm.faces.new(list(reversed(cols[-1])))
    return cols


def set_view(az, el, dist, target=(0, 0, 0.5), lens=50, ortho=False):
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            sp = area.spaces.active
            r3 = sp.region_3d
            r3.view_perspective = 'ORTHO' if ortho else 'PERSP'
            r3.view_location = target
            r3.view_distance = dist
            a, e = math.radians(az), math.radians(el)
            d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
            r3.view_rotation = d.to_track_quat('Z', 'Y')
            sp.lens = lens


# ----------------------------------------------------------------- body
# Half cross-sections (y, z), 12 points from bottom centre, round the side, to top centre.
# p0 floor centre, p1 floor edge, p2 sill round, p3 sill crease, p4 max width, p5 upper side,
# p6 shoulder crease, p7 fender top, p8 deck/valley, p9 beltline (glass edge), p10 roof side, p11 top centre
KEYS = [
    (2.36, [(0, .17), (.52, .17), (.63, .19), (.68, .24), (.695, .31), (.685, .38), (.66, .43), (.60, .455), (.48, .45), (.34, .445), (.18, .443), (0, .443)]),
    (2.18, [(0, .14), (.71, .14), (.87, .165), (.92, .25), (.94, .36), (.935, .47), (.91, .55), (.845, .60), (.70, .585), (.47, .525), (.24, .515), (0, .512)]),
    (1.88, [(0, .12), (.78, .12), (.945, .15), (.99, .25), (1.0, .40), (.997, .57), (.978, .70), (.91, .775), (.765, .765), (.53, .60), (.26, .575), (0, .572)]),
    (1.42, [(0, .11), (.80, .11), (.965, .14), (1.005, .25), (1.012, .43), (1.006, .66), (.987, .80), (.92, .875), (.775, .86), (.57, .70), (.28, .625), (0, .62)]),
    (0.95, [(0, .10), (.78, .10), (.935, .13), (.972, .25), (.975, .43), (.978, .62), (.96, .735), (.885, .79), (.755, .795), (.62, .80), (.32, .79), (0, .788)]),
    (0.45, [(0, .10), (.76, .10), (.905, .13), (.935, .25), (.918, .43), (.94, .61), (.95, .705), (.87, .755), (.75, .775), (.665, .835), (.54, .985), (0, 1.02)]),
    (-0.10, [(0, .10), (.76, .10), (.905, .13), (.935, .25), (.918, .43), (.942, .61), (.952, .71), (.872, .76), (.755, .782), (.665, .845), (.535, 1.075), (0, 1.12)]),
    (-0.70, [(0, .10), (.78, .10), (.93, .13), (.955, .25), (.958, .43), (.985, .63), (.985, .75), (.915, .81), (.77, .835), (.57, .88), (.43, 1.02), (0, 1.05)]),
    (-1.10, [(0, .105), (.80, .105), (.96, .135), (1.02, .25), (1.04, .43), (1.042, .64), (1.022, .80), (.95, .87), (.78, .885), (.46, .915), (.31, .985), (0, .995)]),
    (-1.33, [(0, .11), (.82, .11), (.98, .14), (1.035, .25), (1.05, .43), (1.05, .66), (1.028, .83), (.955, .895), (.77, .905), (.42, .925), (.23, .95), (0, .955)]),
    (-1.90, [(0, .20), (.80, .18), (.97, .20), (1.02, .29), (1.035, .45), (1.03, .635), (1.01, .775), (.94, .848), (.76, .872), (.50, .882), (.26, .886), (0, .886)]),
    (-2.24, [(0, .28), (.75, .26), (.92, .28), (.965, .37), (.98, .50), (.975, .64), (.955, .76), (.90, .835), (.73, .862), (.50, .87), (.26, .875), (0, .875)]),
    (-2.36, [(0, .31), (.67, .29), (.83, .31), (.875, .39), (.89, .51), (.885, .64), (.865, .75), (.82, .83), (.67, .86), (.47, .868), (.25, .872), (0, .872)]),
]
GLASS_X = (-1.18, 0.951)   # glasshouse range along x


def _cr(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                  + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


def densify(keys, step=0.11):
    n = len(keys)
    P = len(keys[0][1])
    pts = [[Vector(yz) for yz in k[1]] for k in keys]
    out = []
    for i in range(n - 1):
        x0, x1 = keys[i][0], keys[i + 1][0]
        seg = max(1, round(abs(x1 - x0) / step))
        for s in range(seg):
            t = s / seg
            x = x0 + (x1 - x0) * t
            ring = []
            for j in range(P):
                yz = _cr(pts[max(i - 1, 0)][j], pts[i][j], pts[i + 1][j], pts[min(i + 2, n - 1)][j], t)
                ring.append(Vector((x, max(0.0, yz.x) if j not in (0, P - 1) else 0.0, yz.y)))
            out.append(ring)
    out.append([Vector((keys[-1][0], (0.0 if j in (0, P - 1) else yz[0]), yz[1]))
                for j, yz in enumerate(keys[-1][1])])
    return out


def build_body(M):
    col = get_col(COL)
    rings = densify(KEYS)
    P = len(rings[0])
    # rake the front fascia back (upper nose recedes)
    for ring in rings:
        x = ring[0].x
        if x > 1.95:
            k = min(1.0, (x - 1.95) / (2.36 - 1.95))
            k = k * k * (3 - 2 * k)
            for p in ring:
                p.x -= 0.30 * k * max(0.0, p.z - 0.17)

    def inset(ring, dx, sy, sz):
        zc = sum(p.z for p in ring) / len(ring)
        return [Vector((p.x + dx, p.y * sy, zc + (p.z - zc) * sz)) for p in ring]

    front = [inset(rings[0], 0.020, 0.93, 0.88), inset(rings[0], 0.030, 0.55, 0.55)]
    rear = [inset(rings[-1], -0.020, 0.94, 0.90), inset(rings[-1], -0.028, 0.55, 0.55)]
    allrings = list(reversed(front)) + rings + rear   # front cap -> nose -> ... -> tail -> rear cap
    nf = len(front)
    nr = len(rear)

    bm = bmesh.new()
    crease = bm.edges.layers.float.get('crease_edge') or bm.edges.layers.float.new('crease_edge')
    V = [[bm.verts.new(p) for p in r] for r in allrings]
    S = len(V)
    faces = {}
    for s in range(S - 1):
        for j in range(P - 1):
            f = bm.faces.new((V[s][j], V[s][j + 1], V[s + 1][j + 1], V[s + 1][j]))
            faces[(s, j)] = f
            xm = (allrings[s][0].x + allrings[s + 1][0].x) / 2
            body_s = nf <= s < S - 1 - nr    # between real stations
            mi = 0
            if j <= 2:
                mi = 2                        # carbon floor / sill
            if body_s and j in (9, 10) and GLASS_X[0] <= xm <= GLASS_X[1]:
                if j == 10:
                    mi = 1 if xm > 0.22 else 2    # windshield glass / exposed-carbon roof
                else:
                    mi = 1 if xm > -0.95 else 2   # side glass / carbon buttress
            f.material_index = mi
    capF = bm.faces.new(list(V[0]))
    capR = bm.faces.new(list(reversed(V[-1])))
    capF.material_index = 0
    capR.material_index = 0
    bm.normal_update()
    # outward check: side face near max width should point +Y
    mid = faces[(S // 2, 4)]
    if mid.normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)

    # creases on character lines
    def crease_loop(j, value, xr=None):
        for s in range(nf, S - 1 - nr):
            a, b = V[s][j], V[s + 1][j]
            if xr and not (xr[0] <= (a.co.x + b.co.x) / 2 <= xr[1]):
                continue
            e = bm.edges.get((a, b))
            if e:
                e[crease] = value
    crease_loop(3, 0.9)
    crease_loop(6, 0.8)
    crease_loop(9, 0.9, GLASS_X)
    for j in range(P - 1):   # tail edge
        e = bm.edges.get((V[S - 1 - nr][j], V[S - 1 - nr][j + 1]))
        if e:
            e[crease] = 0.5

    for f in bm.faces:
        f.smooth = True
    ob = bm_to_obj(bm, "HC_Body", col, [M["paint"], M["glass"], M["carbon"], M["black"], M["liner"]])
    mr = ob.modifiers.new("Mirror", 'MIRROR')
    mr.use_axis = (False, True, False)
    mr.use_clip = True
    mr.use_mirror_merge = True
    mr.merge_threshold = 0.001
    ss = ob.modifiers.new("Subsurf", 'SUBSURF')
    ss.levels = 2
    ss.render_levels = 3
    return ob


# ----------------------------------------------------------------- cutters / booleans
def cutter_col():
    parent = get_col(COL)
    c = get_col(CUT_COL, parent)
    c.hide_render = True
    return c


def new_faces_mat(bm, start, mi):
    bm.faces.ensure_lookup_table()
    for i in range(start, len(bm.faces)):
        bm.faces[i].material_index = mi


def cyl_y(bm, cx, cz, r, y0, y1, segs=48, sx=1.0):
    ring0, ring1 = [], []
    for k in range(segs):
        th = 2 * math.pi * k / segs
        x, z = cx + r * sx * math.cos(th), cz + r * math.sin(th)
        ring0.append(bm.verts.new((x, y0, z)))
        ring1.append(bm.verts.new((x, y1, z)))
    for k in range(segs):
        k2 = (k + 1) % segs
        bm.faces.new((ring0[k], ring0[k2], ring1[k2], ring1[k]))
    bm.faces.new(ring0)
    bm.faces.new(list(reversed(ring1)))


def add_cutter(name, bm, M):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = bm_to_obj(bm, name, cutter_col(), [M["liner"]])
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


def build_arch_cutters(M):
    for tag, W in (("F", FRONT), ("R", REAR)):
        bm = bmesh.new()
        for s in (1, -1):
            y_in = W["y"] - W["w"] / 2 - 0.035
            y0, y1 = (y_in, 1.6) if s > 0 else (-1.6, -y_in)
            cyl_y(bm, W["x"], W["rt"], W["rt"] + 0.042, y0, y1, 64, sx=1.03)
        add_cutter("HC_Cut_Arch_" + tag, bm, M)


def setup_body_boolean(M):
    body = bpy.data.objects["HC_Body"]
    for m in list(body.modifiers):
        if m.type == 'BOOLEAN':
            body.modifiers.remove(m)
    b = body.modifiers.new("Cuts", 'BOOLEAN')
    b.operand_type = 'COLLECTION'
    b.collection = cutter_col()
    b.operation = 'DIFFERENCE'
    b.solver = 'EXACT'
    try:
        b.material_mode = 'TRANSFER'
    except Exception:
        pass
    return b


# ----------------------------------------------------------------- wheels
def build_wheel(tag, W, side, M, col):
    rt, rr, w = W["rt"], W["rr"], W["w"]
    h = rt - rr
    bm = bmesh.new()
    # tyre (0)
    tread = w / 2 - 0.045
    grooves = [0.25, 0.07, -0.07, -0.25]
    prof = [(rr + 0.004, w / 2 - 0.018), (rr + 0.018, w / 2 - 0.004), (rr + 0.45 * h, w / 2 + 0.004),
            (rr + 0.72 * h, w / 2 + 0.002), (rt - 0.09 * h, w / 2 - 0.010), (rt - 0.012, w / 2 - 0.028),
            (rt, tread)]
    for g in grooves:
        gc = g * w
        prof += [(rt, gc + 0.008), (rt - 0.007, gc + 0.006), (rt - 0.007, gc - 0.006), (rt, gc - 0.008)]
    prof += [(rt, -tread), (rt - 0.012, -(w / 2 - 0.028)), (rt - 0.09 * h, -(w / 2 - 0.010)),
             (rr + 0.72 * h, -(w / 2 + 0.002)), (rr + 0.45 * h, -(w / 2 + 0.004)),
             (rr + 0.018, -(w / 2 - 0.004)), (rr + 0.004, -(w / 2 - 0.018))]
    s0 = len(bm.faces)
    revolve(bm, prof, (0, 0, 0), side, nseg=96)
    new_faces_mat(bm, s0, 0)
    # polished lip (1)
    s0 = len(bm.faces)
    revolve(bm, [(rr + 0.014, w / 2 - 0.006), (rr + 0.014, w / 2 - 0.020), (rr - 0.012, w / 2 - 0.024),
                 (rr - 0.017, w / 2 - 0.012), (rr - 0.006, w / 2 - 0.003)], (0, 0, 0), side, nseg=96)
    new_faces_mat(bm, s0, 1)
    # barrel (2)
    s0 = len(bm.faces)
    revolve(bm, [(rr - 0.016, w / 2 - 0.02), (rr - 0.016, -w / 2 + 0.012), (rr + 0.006, -w / 2 + 0.006),
                 (rr + 0.006, w / 2 - 0.02)], (0, 0, 0), side, nseg=96)
    new_faces_mat(bm, s0, 2)
    # twisted twin spokes (2)
    a_lip, a_hub = w / 2 - 0.022, w / 2 - 0.10
    hub_r, rim_r = 0.078, rr - 0.012
    s0 = len(bm.faces)
    N = 12
    for k in range(5):
        th0 = k * 2 * math.pi / 5 + math.pi / 2
        for sg in (-1, 1):
            rings = []
            for i in range(N + 1):
                t = i / N
                r = hub_r + (rim_r - hub_r) * t
                th = th0 + sg * (0.06 + 0.11 * t) + 0.38 * t
                af = a_hub + (a_lip - a_hub) * (t ** 1.5)
                dp = 0.05 * (1 - t) + 0.028 * t
                hw = (0.044 * (1 - t) + 0.027 * t) / 2
                c, s = math.cos(th), math.sin(th)
                P0 = Vector((r * c, 0, r * s))
                T = Vector((-s, 0, c))
                A = Vector((0, side, 0))
                rings.append([bm.verts.new(P0 + T * hw + A * af), bm.verts.new(P0 - T * hw + A * af),
                              bm.verts.new(P0 - T * hw + A * (af - dp)), bm.verts.new(P0 + T * hw + A * (af - dp))])
            for a, b in zip(rings, rings[1:]):
                for q in range(4):
                    bm.faces.new((a[q], a[(q + 1) % 4], b[(q + 1) % 4], b[q]))
            bm.faces.new(rings[0])
            bm.faces.new(list(reversed(rings[-1])))
    new_faces_mat(bm, s0, 2)
    # hub face (2)
    s0 = len(bm.faces)
    revolve(bm, [(0.02, a_hub + 0.004), (0.094, a_hub + 0.004), (0.10, a_hub - 0.05), (0.02, a_hub - 0.05)],
            (0, 0, 0), side, nseg=40)
    new_faces_mat(bm, s0, 2)
    # centre-lock nut (4) + accent ring (5)
    s0 = len(bm.faces)
    revolve(bm, [(0.012, a_hub + 0.05), (0.036, a_hub + 0.05), (0.044, a_hub + 0.040),
                 (0.044, a_hub + 0.004), (0.012, a_hub + 0.004)], (0, 0, 0), side, nseg=12)
    new_faces_mat(bm, s0, 4)
    s0 = len(bm.faces)
    revolve(bm, [(0.050, a_hub + 0.014), (0.066, a_hub + 0.014), (0.066, a_hub + 0.004), (0.050, a_hub + 0.004)],
            (0, 0, 0), side, nseg=40)
    new_faces_mat(bm, s0, 5)
    # brake disc (3)
    rd = 0.195 if W is FRONT else 0.188
    ad = -0.03
    s0 = len(bm.faces)
    revolve(bm, [(0.125, ad + 0.017), (rd, ad + 0.017), (rd, ad - 0.017), (0.125, ad - 0.017)], (0, 0, 0), side, nseg=72)
    new_faces_mat(bm, s0, 3)
    s0 = len(bm.faces)
    revolve(bm, [(0.06, a_hub - 0.05), (0.13, ad + 0.017), (0.13, ad + 0.005), (0.06, a_hub - 0.062)], (0, 0, 0), side, nseg=40)
    new_faces_mat(bm, s0, 2)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = bm_to_obj(bm, "HC_Wheel_" + tag, col,
                   [M["tire"], M["lip"], M["rim"], M["disc"], M["titanium"], M["caliper"]], sharp_angle=42)
    ob.location = (W["x"], side * W["y"], W["rt"])
    bv = ob.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.0022
    bv.segments = 2
    bv.limit_method = 'ANGLE'
    bv.angle_limit = math.radians(42)
    bv.harden_normals = False
    # caliper (static)
    bm = bmesh.new()
    revolve(bm, [(0.148, ad + 0.047), (rd + 0.028, ad + 0.047), (rd + 0.028, ad - 0.05), (0.148, ad - 0.05)],
            (0, 0, 0), side, nseg=14, a0=math.radians(100), a1=math.radians(165), cap=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    cal = bm_to_obj(bm, "HC_Caliper_" + tag, col, [M["caliper"]], sharp_angle=30)
    cal.location = ob.location
    bv = cal.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.008
    bv.segments = 3
    bv.limit_method = 'ANGLE'
    return ob


def build_wheels(M):
    col = get_col(COL)
    for tag, W, side in (("FL", FRONT, 1), ("FR", FRONT, -1), ("RL", REAR, 1), ("RR", REAR, -1)):
        build_wheel(tag, W, side, M, col)


# ----------------------------------------------------------------- geometry helpers for details
def round_corners(pts, r=0.03, seg=4, closed=True):
    pts = [Vector(p) for p in pts]
    n = len(pts)
    out = []
    for i in range(n):
        P = pts[i]
        if not closed and (i == 0 or i == n - 1):
            out.append(P)
            continue
        A, B = pts[i - 1], pts[(i + 1) % n]
        u, v = (A - P), (B - P)
        rr = min(r, u.length * 0.45, v.length * 0.45)
        T1, T2 = P + u.normalized() * rr, P + v.normalized() * rr
        for k in range(seg + 1):
            t = k / seg
            out.append(T1 * (1 - t) ** 2 + P * 2 * (1 - t) * t + T2 * t * t)
    return out


def resample(pts, step=0.015):
    pts = [Vector(p) for p in pts]
    out = []
    for a, b in zip(pts, pts[1:]):
        n = max(1, int((b - a).length / step))
        for i in range(n):
            out.append(a.lerp(b, i / n))
    out.append(pts[-1])
    return out


def extrude(bm, poly, axis, a0, a1):
    """poly: 2D points in the plane orthogonal to axis ('x': (y,z), 'y': (x,z), 'z': (x,y))."""
    def v3(p, a):
        if axis == 'x':
            return (a, p[0], p[1])
        if axis == 'y':
            return (p[0], a, p[1])
        return (p[0], p[1], a)
    A = [bm.verts.new(v3(p, a0)) for p in poly]
    B = [bm.verts.new(v3(p, a1)) for p in poly]
    n = len(A)
    for i in range(n):
        bm.faces.new((A[i], A[(i + 1) % n], B[(i + 1) % n], B[i]))
    bm.faces.new(A)
    bm.faces.new(list(reversed(B)))


def box(bm, center, size, rot=None):
    cx, cy, cz = center
    sx, sy, sz = (s / 2 for s in size)
    vs = []
    for dx, dy, dz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)):
        v = Vector((dx * sx, dy * sy, dz * sz))
        if rot is not None:
            v = rot @ v
        vs.append(bm.verts.new(Vector((cx, cy, cz)) + v))
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        bm.faces.new([vs[i] for i in f])


def proj(bvh, pts, mode, side=1):
    """Project points onto the body. mode: 'front' pts=(y,z), 'back' (y,z), 'side' (x,z), 'top' (x,y), 'bottom' (x,y)."""
    L, N = [], []
    for p in pts:
        if mode == 'front':
            o, d = (4.0, p[0], p[1]), (-1, 0, 0)
        elif mode == 'back':
            o, d = (-4.0, p[0], p[1]), (1, 0, 0)
        elif mode == 'side':
            o, d = (p[0], side * 3.0, p[1]), (0, -side, 0)
        elif mode == 'top':
            o, d = (p[0], p[1], 3.0), (0, 0, -1)
        else:
            o, d = (p[0], p[1], -1.0), (0, 0, 1)
        loc, nrm = hit(bvh, o, d)
        if loc is not None:
            L.append(loc)
            N.append(nrm)
    return L, N


def ribbon_obj(name, bvh, pts, mode, w, out, inn, mat, col, side=1, sink=0.0, step=0.012):
    L, N = proj(bvh, resample(pts, step), mode, side)
    if len(L) < 2:
        return None
    L = [l - n * sink for l, n in zip(L, N)]
    bm = bmesh.new()
    sweep_rect(bm, L, N, w, out, inn)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm_to_obj(bm, name, col, [mat], smooth=True)


# ----------------------------------------------------------------- details
def build_cutters_and_details(M):
    col = get_col(COL)
    bvh = body_bvh()          # uncut body surface (booleans not added yet)
    cm = M["liner"]

    # --- front mouth: one wide intake + air-curtain slots
    bm = bmesh.new()
    mouth = round_corners([(-0.57, 0.19), (0.57, 0.19), (0.49, 0.335), (-0.49, 0.335)], 0.045, 5)
    extrude(bm, mouth, 'x', 2.13, 2.9)
    add_cutter("HC_Cut_Front", bm, M)
    bm = bmesh.new()
    extrude(bm, [(-0.62, 0.17), (0.62, 0.17), (0.62, 0.36), (-0.62, 0.36)], 'x', 2.17, 2.19)
    g = bm_to_obj(bm, "HC_Grille", col, [M["grille"]])
    bm = bmesh.new()
    for yy in (-0.30, 0.30):
        box(bm, (2.26, yy, 0.262), (0.22, 0.022, 0.16))
    bm_to_obj(bm, "HC_MouthBlades", col, [M["carbon"]], sharp_angle=40)

    # --- front light band: recessed gloss-black band, LED line, pixel modules
    band = [(-0.665, 0.385), (-0.57, 0.405), (0.57, 0.405), (0.665, 0.385)]
    band = round_corners(band, 0.06, 6, closed=False)
    L, N = proj(bvh, resample(band, 0.01), 'front')
    bm = bmesh.new()
    sweep_rect(bm, L, N, 0.058, 0.09, 0.016)
    cb = add_cutter("HC_Cut_FrontBand", bm, M)
    cb.data.materials[0] = M["black"]
    line = round_corners([(-0.645, 0.402), (-0.57, 0.424), (0.57, 0.424), (0.645, 0.402)], 0.06, 6, closed=False)
    ribbon_obj("HC_LightLine_Front", bvh, line, 'front', 0.007, 0.003, 0.003, M["led"], col, sink=0.012)
    bm = bmesh.new()
    for s in (1, -1):
        for k in range(4):
            y = s * (0.44 + 0.045 * k)
            Lp, Np = proj(bvh, resample([(y, 0.372), (y, 0.402)], 0.01), 'front')
            Lp = [l - n * 0.012 for l, n in zip(Lp, Np)]
            sweep_rect(bm, Lp, Np, 0.016, 0.003, 0.003)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_PixelLights", col, [M["led"]], smooth=True)

    # --- front splitter
    bm = bmesh.new()
    front = []
    for i in range(25):
        y = -0.86 + 1.72 * i / 24
        front.append((2.45 - 0.36 * (abs(y) / 0.86) ** 3, y))
    poly = [(2.0, 0.86)] + list(reversed(front)) + [(2.0, -0.86)]
    extrude(bm, poly, 'z', 0.138, 0.162)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_Splitter", col, [M["carbon"]], sharp_angle=40)

    # --- hood vent + engine-cover louvres (recess cutters + slats)
    def vent(tag, xs, halfw, n_slats, depth):
        bm = bmesh.new()
        poly = round_corners([(xs[0], -halfw[0]), (xs[1], -halfw[1]), (xs[1], halfw[1]), (xs[0], halfw[0])], 0.035, 4)
        L, _ = proj(bvh, [((xs[0] + xs[1]) / 2, 0)], 'top')
        zc = L[0].z
        extrude(bm, poly, 'z', zc - depth, zc + 0.5)
        add_cutter("HC_Cut_" + tag, bm, M)
        bm = bmesh.new()
        for i in range(n_slats):
            x = xs[0] + (xs[1] - xs[0]) * (i + 0.5) / n_slats
            Lx, _ = proj(bvh, [(x, 0)], 'top')
            zt = Lx[0].z - 0.012
            hw = halfw[0] + (halfw[1] - halfw[0]) * (i + 0.5) / n_slats
            rot = Matrix.Rotation(math.radians(-28), 3, 'Y')
            box(bm, (x, 0, zt - depth / 2 + 0.01), (0.010, 2 * hw - 0.02, depth), rot)
        bm_to_obj(bm, "HC_Slats_" + tag, col, [M["carbon"]], sharp_angle=40)
    vent("HoodVent", (1.60, 1.95), (0.30, 0.36), 4, 0.07)
    vent("DeckLouvre", (-1.32, -1.86), (0.30, 0.24), 7, 0.05)

    # --- front fender louvres
    bm = bmesh.new()
    for s in (1, -1):
        for i in range(5):
            x = 1.10 + i * 0.075
            L, _ = proj(bvh, [(x, s * 0.83)], 'top')
            z = L[0].z
            extrude(bm, [(x - 0.013, s * 0.72), (x + 0.013, s * 0.72), (x + 0.013 - 0.03, s * 0.95), (x - 0.013 - 0.03, s * 0.95)],
                    'z', z - 0.03, z + 0.4)
    add_cutter("HC_Cut_FenderLouvres", bm, M)

    # --- side intakes behind doors + blades
    for s in (1, -1):
        bm = bmesh.new()
        poly = round_corners([(-0.40, 0.34), (-0.53, 0.69), (-0.90, 0.71), (-0.86, 0.41)], 0.035, 5)
        extrude(bm, poly, 'y', s * 0.80, s * 1.5)
        add_cutter("HC_Cut_SideIntake_" + ("L" if s > 0 else "R"), bm, M)
        bm = bmesh.new()
        for z in (0.47, 0.585):
            xf = -0.40 + (-0.53 + 0.40) * (z - 0.34) / 0.35
            xr = -0.86 + (-0.90 + 0.86) * (z - 0.41) / 0.30
            L, _ = proj(bvh, [((xf + xr) / 2, z)], 'side', s)
            yo = abs(L[0].y) - 0.006
            box(bm, ((xf + xr) / 2, s * (0.80 + yo) / 2, z), (abs(xf - xr) - 0.02, yo - 0.80, 0.014))
        bm_to_obj(bm, "HC_IntakeBlades_" + ("L" if s > 0 else "R"), col, [M["carbon"]], sharp_angle=40)

    # --- rear wheel-arch outlets
    for s in (1, -1):
        tag = "L" if s > 0 else "R"
        bm = bmesh.new()
        poly = round_corners([(-1.80, 0.27), (-1.785, 0.60), (-1.89, 0.655), (-1.975, 0.30)], 0.02, 4)
        extrude(bm, poly, 'y', s * 0.86, s * 1.5)
        add_cutter("HC_Cut_ArchOutlet_" + tag, bm, M)
        bm = bmesh.new()
        for z in (0.37, 0.465, 0.56):
            xf = -1.80 + 0.015 * (z - 0.27) / 0.33
            xr = -1.975 + 0.085 * (z - 0.30) / 0.355
            L, _ = proj(bvh, [((xf + xr) / 2, z)], 'side', s)
            yo = abs(L[0].y) - 0.006
            box(bm, ((xf + xr) / 2, s * (0.86 + yo) / 2, z), (abs(xf - xr) - 0.012, yo - 0.86, 0.012))
        bm_to_obj(bm, "HC_OutletBlades_" + tag, col, [M["carbon"]], sharp_angle=40)

    # --- door shut-lines and flush handle
    for s in (1, -1):
        tag = "L" if s > 0 else "R"
        door = round_corners([(0.975, 0.76), (0.99, 0.30), (0.95, 0.262), (-0.26, 0.262), (-0.31, 0.30), (-0.345, 0.79)],
                             0.05, 6, closed=False)
        ribbon_obj("HC_DoorLine_" + tag, bvh, door, 'side', 0.0045, 0.0015, 0.004, M["liner"], col, side=s)
        ribbon_obj("HC_GlassLine_" + tag, bvh, [(-0.35, 0.845), (-0.40, 0.95), (-0.45, 1.05)], 'side',
                   0.004, 0.0015, 0.004, M["liner"], col, side=s)
        ribbon_obj("HC_Handle_" + tag, bvh, [(-0.10, 0.70), (-0.24, 0.705)], 'side', 0.018, 0.002, 0.004,
                   M["black"], col, side=s)

    # --- side skirt blades
    bm = bmesh.new()
    for s in (1, -1):
        poly = round_corners([(0.98, s * 0.86), (0.98, s * 0.975), (0.70, s * 1.0), (-0.70, s * 1.0),
                              (-0.88, s * 0.985), (-0.88, s * 0.86)], 0.04, 4)
        extrude(bm, poly, 'z', 0.112, 0.13)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_SideBlades", col, [M["carbon"]], sharp_angle=40)

    # --- camera mirrors
    for s in (1, -1):
        tag = "L" if s > 0 else "R"
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * 0.085 - 0.005 * v.co.z, v.co.y * 0.026, v.co.z * 0.024))
            v.co += Vector((0.60, s * 1.035, 0.845))
        L, N = proj(bvh, [(0.66, 0.77)], 'side', s)
        base = L[0]
        stalk = [base, base.lerp(Vector((0.60, s * 1.035, 0.845)), 0.5) + Vector((0, 0, 0.02)), Vector((0.61, s * 1.03, 0.845))]
        nr = [Vector((1, 0, 0))] * 3
        sweep_rect(bm, stalk, nr, 0.012, 0.018, 0.018)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm_to_obj(bm, "HC_Mirror_" + tag, col, [M["carbon"]], smooth=True)

    # --- tail: recessed black light band + red LED bar, rear outlet grille, exhausts, rain light
    tband = round_corners([(-0.87, 0.755), (-0.76, 0.79), (0.76, 0.79), (0.87, 0.755)], 0.08, 6, closed=False)
    L, N = proj(bvh, resample(tband, 0.01), 'back')
    bm = bmesh.new()
    sweep_rect(bm, L, N, 0.05, 0.09, 0.014)
    ct = add_cutter("HC_Cut_TailBand", bm, M)
    ct.data.materials[0] = M["black"]
    ribbon_obj("HC_TailLight", bvh, tband, 'back', 0.014, 0.003, 0.003, M["red"], col, sink=0.009)
    bm = bmesh.new()
    outlet = round_corners([(-0.66, 0.33), (0.66, 0.33), (0.58, 0.63), (-0.58, 0.63)], 0.05, 5)
    extrude(bm, outlet, 'x', -2.30, -2.8)
    add_cutter("HC_Cut_RearOutlet", bm, M)
    bm = bmesh.new()
    extrude(bm, [(-0.7, 0.30), (0.7, 0.30), (0.7, 0.66), (-0.7, 0.66)], 'x', -2.335, -2.345)
    bm_to_obj(bm, "HC_RearGrille", col, [M["grille"]])
    def hexa(yy, r):
        return [(yy + r * math.cos(math.pi / 6 + k * math.pi / 3), 0.49 + r * math.sin(math.pi / 6 + k * math.pi / 3))
                for k in range(6)]
    bm = bmesh.new()
    for yy in (-0.10, 0.10):
        extrude(bm, hexa(yy, 0.05), 'x', -2.30, -2.405)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_Exhausts", col, [M["titanium"]], sharp_angle=40)
    bm = bmesh.new()
    for yy in (-0.10, 0.10):
        extrude(bm, hexa(yy, 0.037), 'x', -2.30, -2.4075)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_ExhaustInner", col, [M["liner"]])
    bm = bmesh.new()
    box(bm, (-2.345, 0, 0.385), (0.02, 0.16, 0.028))
    bm_to_obj(bm, "HC_RainLight", col, [M["red"]])

    # --- diffuser strakes
    bm = bmesh.new()
    for y in (0.0, 0.30, -0.30, 0.60, -0.60):
        top = []
        for i in range(10):
            x = -1.55 - 0.85 * i / 9
            L, _ = proj(bvh, [(x, y)], 'bottom')
            top.append((x, (L[0].z + 0.01) if L else 0.31))
        poly = [(-1.55, 0.108), (-2.42, 0.235), (-2.42, top[-1][1])] + list(reversed(top[:-1]))
        extrude(bm, poly, 'y', y - 0.006, y + 0.006)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_Diffuser", col, [M["carbon"]], sharp_angle=40)

    # --- rear wing (inverted NACA 6410) on swan-neck mounts
    build_wing(M, col, bvh)


def naca(m=0.06, p=0.4, t=0.10, n=24):
    up, lo = [], []
    for i in range(n + 1):
        x = (1 - math.cos(math.pi * i / n)) / 2
        yt = 5 * t * (0.2969 * math.sqrt(x) - 0.126 * x - 0.3516 * x ** 2 + 0.2843 * x ** 3 - 0.1036 * x ** 4)
        if x < p:
            yc, dy = m / p ** 2 * (2 * p * x - x * x), 2 * m / p ** 2 * (p - x)
        else:
            yc, dy = m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x - x * x), 2 * m / (1 - p) ** 2 * (p - x)
        th = math.atan(dy)
        up.append((x - yt * math.sin(th), yc + yt * math.cos(th)))
        lo.append((x + yt * math.sin(th), yc - yt * math.cos(th)))
    return up + list(reversed(lo))[1:-1]     # closed loop (LE -> TE on top, back on bottom)


def build_wing(M, col, bvh):
    chord, span = 0.34, 1.74
    le = Vector((-1.97, 0, 1.03))
    aoa = math.radians(9)
    prof = naca()
    sec = []
    for (u, v) in prof:
        # inverted airfoil: camber points down; rotate so trailing edge rises
        x, z = -u * chord, -v * chord
        xr = x * math.cos(aoa) - z * math.sin(aoa) * -1
        zr = z * math.cos(aoa) + (-x) * math.sin(aoa)
        sec.append((le.x + xr, le.z + zr))
    bm = bmesh.new()
    n_span = 18
    rings = []
    for i in range(n_span + 1):
        y = -span / 2 + span * i / n_span
        bow = 0.03 * (1 - (2 * y / span) ** 2)          # slight downward bow at centre
        rings.append([bm.verts.new((x, y, z - bow)) for (x, z) in sec])
    P = len(sec)
    for a, b in zip(rings, rings[1:]):
        for j in range(P):
            bm.faces.new((a[j], a[(j + 1) % P], b[(j + 1) % P], b[j]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_Wing", col, [M["carbon"]], sharp_angle=50)
    # end plates
    bm = bmesh.new()
    xs = [x for x, _ in sec]
    zs = [z for _, z in sec]
    x0, x1 = max(xs) + 0.03, min(xs) - 0.04
    plate = round_corners([(x0, min(zs) - 0.08), (x1 + 0.02, min(zs) - 0.02), (x1, max(zs) + 0.08), (x0 - 0.12, max(zs) + 0.02)], 0.03, 4)
    for s in (1, -1):
        extrude(bm, plate, 'y', s * (span / 2 - 0.004), s * (span / 2 + 0.008))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_WingPlates", col, [M["carbon"]], sharp_angle=40)
    # swan necks
    bm = bmesh.new()
    for s in (1, -1):
        y = s * 0.32
        L, _ = proj(bvh, [(-1.84, y)], 'top')
        zb = L[0].z - 0.02
        top_z = max(z for (x, z) in sec if abs(x + 2.13) < 0.05) - 0.03 * (1 - (2 * y / span) ** 2)
        path = [Vector((-1.84, y, zb)), Vector((-1.86, y, zb + 0.10)), Vector((-1.92, y, top_z + 0.085)),
                Vector((-2.03, y, top_z + 0.105)), Vector((-2.11, y, top_z + 0.05)), Vector((-2.13, y, top_z - 0.005))]
        path = resample(round_corners(path, 0.06, 6, closed=False), 0.02)
        nr = []
        for i, p in enumerate(path):
            t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            nr.append(t.cross(Vector((0, 1, 0))).normalized())
        sweep_rect(bm, path, nr, 0.02, 0.042, 0.042)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm_to_obj(bm, "HC_WingMounts", col, [M["carbon"]], smooth=True)


def build_side_panels(M):
    """Exposed-carbon scoop panels on the doors, shrink-wrapped onto the body."""
    col = get_col(COL)
    body = bpy.data.objects["HC_Body"]
    poly = [(0.90, 0.275), (0.90, 0.33), (0.30, 0.372), (-0.25, 0.45), (-0.44, 0.49), (-0.387, 0.35), (-0.22, 0.282)]
    poly = round_corners(poly, 0.04, 5)
    for s in (1, -1):
        tag = "L" if s > 0 else "R"
        bm = bmesh.new()
        bm.faces.new([bm.verts.new((x, s * 1.4, z)) for (x, z) in poly])
        ob = bm_to_obj(bm, "HC_SideScoop_" + tag, col, [M["carbon"]], smooth=True)
        sd = ob.modifiers.new("Sub", 'SUBSURF')
        sd.subdivision_type = 'SIMPLE'
        sd.levels = sd.render_levels = 3
        sw = ob.modifiers.new("Wrap", 'SHRINKWRAP')
        sw.target = body
        sw.wrap_method = 'PROJECT'
        sw.use_project_y = True
        sw.use_negative_direction = s > 0
        sw.use_positive_direction = s < 0
        sw.offset = 0.0012
        so = ob.modifiers.new("Solid", 'SOLIDIFY')
        so.thickness = 0.003
        so.offset = 0.0


def build_all():
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    M = make_materials()
    clear_col(COL)
    build_body(M)
    build_arch_cutters(M)
    build_cutters_and_details(M)
    setup_body_boolean(M)
    build_wheels(M)
    build_side_panels(M)
    for o in cutter_col().objects:
        o.hide_set(True)
    return M


# ----------------------------------------------------------------- studio
def build_studio():
    scn = bpy.context.scene
    col = get_col("Hypercar_Studio")
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    # world: dark vertical gradient
    w = scn.world or bpy.data.worlds.new("World")
    scn.world = w
    w.use_nodes = True
    nt = w.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs[2], ramp.inputs[0])
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.004, 0.0045, 0.005, 1)
    ramp.color_ramp.elements[1].position = 0.9
    ramp.color_ramp.elements[1].color = (0.075, 0.078, 0.085, 1)
    nt.links.new(ramp.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = 1.0
    nt.links.new(bg.outputs[0], out.inputs[0])
    # floor
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40)
    fm, fb = principled("HC_Floor", (0.006, 0.006, 0.007), rough=0.16, spec=0.5)
    floor = bm_to_obj(bm, "HC_Floor", col, [fm])
    pr = bpy.data.lightprobes.get("HC_Probe")
    if pr is None:
        try:
            pr = bpy.data.lightprobes.new("HC_Probe", 'SPHERE')
        except TypeError:
            pr = None
    if pr is not None:
        po = bpy.data.objects.new("HC_Probe", pr)
        col.objects.link(po)
        po.location = (0, 0, 2.4)
        try:
            pr.influence_distance = 8.0
        except Exception:
            pass
    # lights
    def area(name, loc, size, energy, color=(1, 1, 1), target=(0, 0, 0.5), shape='RECTANGLE'):
        ld = bpy.data.lights.get(name) or bpy.data.lights.new(name, 'AREA')
        ld.shape = shape
        ld.size, ld.size_y = size
        ld.energy = energy
        ld.color = color
        o = bpy.data.objects.new(name, ld)
        col.objects.link(o)
        o.location = loc
        o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        return o
    area("HC_Softbox_Top", (0.2, 0, 5.2), (2.4, 7.0), 650, (0.96, 0.98, 1.0), target=(0.2, 0, 0))
    area("HC_Strip_Front", (2.5, -5.0, 1.5), (6.0, 0.4), 450, target=(0.3, 0, 0.6))
    area("HC_Strip_Back", (-2.5, 5.0, 1.8), (6.0, 0.4), 300, target=(-0.3, 0, 0.6))
    area("HC_Rim", (-6.0, 2.2, 2.4), (3.0, 1.2), 900, (1.0, 0.92, 0.85), target=(-1, 0, 0.6))
    area("HC_Fill", (7.0, -1.0, 1.0), (2.0, 1.5), 200, (0.9, 0.95, 1.0), target=(1.5, 0, 0.5))
    # visible soft panels so metallic paint has something to reflect
    em, eb = principled("HC_PanelEmit", (0, 0, 0), emit=(1, 1, 1), strength=1.3)
    for name, loc, size, tgt in (("HC_Panel_Top", (0.2, 0, 4.6), (7.5, 2.8), (0.2, 0, 0)),
                                 ("HC_Panel_Back", (-4.0, 5.5, 2.2), (6.0, 1.2), (-0.3, 0, 0.8))):
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5)
        for v in bm.verts:
            v.co.x *= size[0]
            v.co.y *= size[1]
        po = bm_to_obj(bm, name, col, [em])
        po.location = loc
        po.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        po.visible_camera = False
        po.visible_shadow = False
    # camera
    cam = bpy.data.objects.get("HC_Camera")
    if cam is None:
        cam = bpy.data.objects.new("HC_Camera", bpy.data.cameras.new("HC_Camera"))
        col.objects.link(cam)
    scn.camera = cam
    return cam


def aim_camera(az, el, dist, target=(0.1, 0, 0.5), lens=55):
    cam = bpy.data.objects["HC_Camera"]
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    cam.location = Vector(target) + d * dist
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    cam.data.dof.use_dof = False
    return cam


def render_settings(res=(1920, 1080), pct=100, samples=64):
    scn = bpy.context.scene
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            pass
    ee = scn.eevee
    ee.taa_render_samples = samples
    ee.use_raytracing = True
    ee.ray_tracing_method = 'SCREEN'
    ee.use_shadows = True
    try:
        ee.ray_tracing_options.resolution_scale = '1'
    except Exception:
        pass
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = pct
    vs = scn.view_settings
    try:
        vs.view_transform = 'AgX'
    except TypeError:
        pass
    for cand in ('AgX - Punchy', 'Punchy', 'AgX - Medium High Contrast', 'Medium High Contrast'):
        try:
            vs.look = cand
            break
        except TypeError:
            pass
    scn.render.image_settings.file_format = 'PNG'


def render_to(path):
    scn = bpy.context.scene
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path
