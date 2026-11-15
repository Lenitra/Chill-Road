"""Retro-futuristic wedge coupe in the spirit of the Hyundai N Vision 74 (no brand marks)."""
import bpy, bmesh, math
from mathutils import Vector, Matrix
import hc_lib as H

COL = "NV74"
CUT = "NV74_Cutters"
FRONT = dict(x=1.45, rt=0.349, rr=0.254, w=0.27, y=0.85)
REAR = dict(x=-1.455, rt=0.361, rr=0.267, w=0.315, y=0.83)
FDX = -0.10     # front overhang shortened (nose moves back)
CAB = 0.28      # windshield / cabin front moved forward (shorter hood)
SEAT_DX = 0.14  # seats follow the cabin forward a little


# ----------------------------------------------------------------- materials
def grille_material(name="NV_Grille", scale=150):
    m, b = H.principled(name, (0.012, 0.012, 0.013), metal=0.4, rough=0.5)
    nt = m.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    H.sock(mp, "Scale").default_value = (scale, scale, scale)
    br = nt.nodes.new("ShaderNodeTexBrick")
    H.sock(br, "Color1").default_value = (0.003, 0.003, 0.004, 1)
    H.sock(br, "Color2").default_value = (0.004, 0.004, 0.005, 1)
    H.sock(br, "Mortar").default_value = (0.03, 0.03, 0.033, 1)
    H.sock(br, "Scale").default_value = 1.0
    H.sock(br, "Mortar Size").default_value = 0.08
    br.offset = 0.5
    bump = nt.nodes.new("ShaderNodeBump")
    H.sock(bump, "Strength").default_value = 0.6
    nt.links.new(tc.outputs["Object"], H.sock(mp, "Vector"))
    nt.links.new(mp.outputs[0], H.sock(br, "Vector"))
    nt.links.new(br.outputs["Color"], H.sock(b, "Base Color"))
    nt.links.new(br.outputs["Fac"], H.sock(bump, "Height"))
    nt.links.new(bump.outputs[0], H.sock(b, "Normal"))
    return m


def glass_material(name="NV_Glass", tint=(0.15, 0.17, 0.17)):
    """See-through tinted car glass: transparent at normal incidence, mirror-like at grazing angles."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    tr.inputs[0].default_value = (*tint, 1)
    gl = nt.nodes.new("ShaderNodeBsdfAnisotropic")
    H.sock(gl, "Color").default_value = (0.72, 0.72, 0.74, 1)
    H.sock(gl, "Roughness").default_value = 0.03
    fr = nt.nodes.new("ShaderNodeFresnel")
    H.sock(fr, "IOR").default_value = 1.52
    add = nt.nodes.new("ShaderNodeMath")
    add.operation = 'ADD'
    add.use_clamp = True
    add.inputs[1].default_value = 0.05
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(fr.outputs[0], add.inputs[0])
    nt.links.new(add.outputs[0], mix.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(gl.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    m.surface_render_method = 'BLENDED'
    m.show_transparent_back = False
    m.use_transparent_shadow = True
    m.use_backface_culling = False
    m.diffuse_color = (0.05, 0.06, 0.06, 0.35)
    return m


def paint_material():
    """Satin silver outside; the inside of the shell (backfaces) reads as dark cabin trim."""
    m, b = H.principled("NV_Paint", (0.52, 0.54, 0.55), metal=0.85, rough=0.3, coat=0.3, coat_rough=0.15)
    nt = m.node_tree
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    dif = nt.nodes.new("ShaderNodeBsdfDiffuse")
    H.sock(dif, "Color").default_value = (0.016, 0.016, 0.018, 1)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(geo.outputs["Backfacing"], mix.inputs[0])
    nt.links.new(b.outputs[0], mix.inputs[1])
    nt.links.new(dif.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    return m


def make_materials():
    M = {}
    M["paint"] = paint_material()
    M["black"], _ = H.principled("NV_SatinBlack", (0.017, 0.017, 0.019), rough=0.5)
    M["gloss"], _ = H.principled("NV_GlossBlack", (0.008, 0.008, 0.009), rough=0.1, coat=0.8)
    M["trim"], _ = H.principled("NV_TrimBlack", (0.008, 0.008, 0.009), rough=0.55, spec=0.12)
    M["glass"] = glass_material()
    # interior
    M["interior"], _ = H.principled("NV_Interior", (0.02, 0.02, 0.022), rough=0.85)
    M["seat"], _ = H.principled("NV_SeatShell", (0.014, 0.014, 0.016), rough=0.7)
    M["insert"], _ = H.principled("NV_SeatInsert", (0.06, 0.06, 0.065), rough=0.95)
    M["harness"], _ = H.principled("NV_Harness", (0.55, 0.02, 0.02), rough=0.6)
    M["cage"], _ = H.principled("NV_Cage", (0.06, 0.06, 0.065), metal=0.8, rough=0.35)
    M["teal"], _ = H.principled("NV_TealAccent", (0.02, 0.35, 0.32), rough=0.4, emit=(0.05, 0.8, 0.7), strength=1.2)
    M["screen"], _ = H.principled("NV_Screen", (0.0, 0.0, 0.0), rough=0.2, emit=(0.45, 0.9, 0.85), strength=2.5)
    M["chrome"], _ = H.principled("NV_MirrorChrome", (0.9, 0.9, 0.92), metal=1.0, rough=0.03)
    M["alu"], _ = H.principled("NV_Alu", (0.72, 0.74, 0.76), metal=1.0, rough=0.28)
    M["blue"], _ = H.principled("NV_H2Blue", (0.02, 0.12, 0.5), rough=0.35, coat=0.5)
    M["grille"] = grille_material()
    M["led"], _ = H.principled("NV_LED", (1, 1, 1), emit=(1.0, 0.80, 0.52), strength=35)
    M["red"], _ = H.principled("NV_TailRed", (0.6, 0.0, 0.0), emit=(1.0, 0.02, 0.01), strength=30)
    M["redpaint"], _ = H.principled("NV_Red", (0.75, 0.02, 0.02), rough=0.3, coat=0.5)
    M["tire"], _ = H.principled("NV_Tire", (0.022, 0.022, 0.024), rough=0.88)
    M["disc"], _ = H.principled("NV_WheelWhite", (0.80, 0.81, 0.82), rough=0.32, coat=0.3)
    M["liner"], _ = H.principled("NV_Liner", (0.012, 0.012, 0.013), rough=0.75)
    return M


# ----------------------------------------------------------------- body
# half sections (y, z): p0 floor centre, p1 floor edge, p2 sill bottom, p3 sill crease, p4 lower side,
# p5 flare top edge, p6 shoulder, p7 hood crease / door top, p8 deck, p9 beltline, p10 roof side, p11 top centre
KEYS = [
    (2.475 + FDX, [(0, .20), (.70, .20), (.82, .22), (.90, .28), (.935, .42), (.935, .56), (.915, .645), (.88, .70), (.62, .706), (.42, .709), (.21, .711), (0, .712)]),
    (2.44 + FDX, [(0, .18), (.78, .18), (.92, .20), (.97, .27), (.985, .42), (.985, .57), (.945, .66), (.895, .705), (.63, .713), (.43, .716), (.21, .718), (0, .719)]),
    (2.02, [(0, .15), (.80, .15), (.95, .17), (.99, .27), (1.0, .43), (1.0, .68), (.955, .73), (.875, .762), (.63, .772), (.43, .782), (.21, .79), (0, .792)]),
    (1.96, [(0, .15), (.80, .15), (.95, .17), (.99, .27), (1.0, .43), (1.0, .735), (.955, .752), (.872, .772), (.63, .782), (.43, .792), (.21, .80), (0, .802)]),
    (1.45, [(0, .14), (.80, .14), (.95, .16), (.99, .27), (1.0, .43), (1.0, .80), (.96, .832), (.83, .85), (.60, .858), (.42, .866), (.21, .872), (0, .874)]),
    (0.93, [(0, .14), (.80, .14), (.95, .16), (.99, .28), (1.0, .43), (1.0, .80), (.955, .84), (.80, .858), (.64, .866), (.48, .872), (.26, .877), (0, .879)]),
    (0.86, [(0, .14), (.78, .14), (.915, .16), (.95, .28), (.955, .43), (.955, .79), (.95, .835), (.81, .852), (.70, .858), (.60, .862), (.40, .866), (0, .868)]),
    (0.45 + CAB, [(0, .14), (.78, .14), (.915, .16), (.95, .28), (.955, .43), (.955, .79), (.95, .835), (.84, .852), (.82, .855), (.80, .857), (.74, .86), (0, .864)]),
    (-0.25 + CAB, [(0, .14), (.78, .14), (.915, .16), (.95, .28), (.955, .43), (.955, .79), (.95, .84), (.84, .858), (.825, .862), (.81, .866), (.66, 1.285), (0, 1.32)]),
    (-0.80, [(0, .14), (.78, .14), (.915, .16), (.95, .28), (.955, .43), (.955, .80), (.953, .848), (.845, .866), (.83, .87), (.815, .875), (.66, 1.29), (0, 1.325)]),
    (-0.88, [(0, .14), (.78, .14), (.915, .16), (.95, .28), (.957, .43), (.957, .805), (.955, .85), (.845, .868), (.83, .872), (.815, .877), (.655, 1.275), (0, 1.31)]),
    (-0.93, [(0, .14), (.80, .14), (.95, .16), (.99, .28), (1.0, .43), (1.0, .83), (.965, .86), (.85, .875), (.835, .88), (.82, .885), (.65, 1.265), (0, 1.30)]),
    (-1.455, [(0, .15), (.80, .15), (.95, .17), (.99, .30), (1.0, .45), (1.0, .845), (.97, .88), (.86, .905), (.845, .91), (.83, .915), (.60, 1.12), (0, 1.14)]),
    (-1.98, [(0, .21), (.80, .21), (.95, .23), (.99, .37), (1.0, .50), (1.0, .85), (.97, .90), (.85, .948), (.82, .953), (.78, .958), (.50, .99), (0, 1.0)]),
    (-2.05, [(0, .23), (.80, .23), (.95, .25), (.99, .38), (1.0, .50), (1.0, .85), (.97, .90), (.85, .952), (.82, .957), (.78, .962), (.50, .99), (0, .995)]),
    (-2.40, [(0, .30), (.78, .30), (.93, .32), (.975, .40), (.985, .52), (.985, .84), (.96, .905), (.86, .962), (.82, .967), (.78, .97), (.50, .977), (0, .98)]),
    (-2.47, [(0, .32), (.74, .32), (.89, .34), (.94, .41), (.955, .52), (.955, .84), (.93, .90), (.85, .975), (.81, .985), (.77, .99), (.50, 1.0), (0, 1.003)]),
]
KX = [k[0] for k in KEYS]
WINDSHIELD = (-0.25 + CAB, 0.45 + CAB)
REAR_GLASS = (-1.98, -0.80)
SIDE_GLASS = (-1.33, 0.45 + CAB)
FLARE_KEYS = (5, 6, 10, 11)


def prof_at(j, x):
    for (x0, P0), (x1, P1) in zip(KEYS, KEYS[1:]):
        if x1 <= x <= x0:
            t = (x0 - x) / (x0 - x1)
            return (P0[j][0] + (P1[j][0] - P0[j][0]) * t, P0[j][1] + (P1[j][1] - P0[j][1]) * t)
    return KEYS[0][1][j] if x > KEYS[0][0] else KEYS[-1][1][j]


def rings_from_keys(step=0.14):
    rings, kidx = [], []
    for i in range(len(KEYS) - 1):
        x0, P0 = KEYS[i]
        x1, P1 = KEYS[i + 1]
        n = max(1, round(abs(x1 - x0) / step))
        for s in range(n):
            t = s / n
            if s == 0:
                kidx.append(len(rings))
            rings.append([Vector((x0 + (x1 - x0) * t, a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
                          for a, b in zip(P0, P1)])
    kidx.append(len(rings))
    rings.append([Vector((KEYS[-1][0], y, z)) for (y, z) in KEYS[-1][1]])
    # nose bow: centre of the front face pushes forward
    for r in rings[:2]:
        for p in r:
            p.x += 0.035 * (1 - min(1.0, (p.y / 0.95)) ** 2)
    return rings, kidx


def build_body(M):
    col = H.get_col(COL)
    rings, kidx = rings_from_keys()
    P = len(rings[0])

    def inset(ring, dx, sy, sz):
        zc = sum(p.z for p in ring) / len(ring)
        return [Vector((p.x + dx, p.y * sy, zc + (p.z - zc) * sz)) for p in ring]

    front = [inset(rings[0], 0.004, 0.97, 0.94), inset(rings[0], 0.008, 0.5, 0.5)]
    rear = [inset(rings[-1], -0.004, 0.97, 0.95), inset(rings[-1], -0.008, 0.5, 0.5)]
    allr = list(reversed(front)) + rings + rear
    nf, nr = len(front), len(rear)
    kid = [k + nf for k in kidx]            # key ring indices inside allr
    bm = bmesh.new()
    crease = bm.edges.layers.float.get('crease_edge') or bm.edges.layers.float.new('crease_edge')
    V = [[bm.verts.new(p) for p in r] for r in allr]
    S = len(V)
    probe = None
    for s in range(S - 1):
        xm = (allr[s][0].x + allr[s + 1][0].x) / 2
        inner = nf <= s < S - 1 - nr
        for j in range(P - 1):
            f = bm.faces.new((V[s][j], V[s][j + 1], V[s + 1][j + 1], V[s + 1][j]))
            mi = 0
            if j <= 2:
                mi = 2
            if inner and j == 10 and (WINDSHIELD[0] <= xm <= WINDSHIELD[1] or REAR_GLASS[0] <= xm <= REAR_GLASS[1]):
                mi = 1
            if inner and j == 9 and SIDE_GLASS[0] <= xm <= SIDE_GLASS[1]:
                mi = 1                                    # side glass (pillars are overlays)
            f.material_index = mi
            if s == S // 2 and j == 4:
                probe = f
    bm.faces.new(list(V[0])).material_index = 0
    bm.faces.new(list(reversed(V[-1]))).material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # consistent winding (caps included)
    bm.normal_update()
    if probe.normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)

    def set_crease(a, b, v):
        e = bm.edges.get((a, b))
        if e:
            e[crease] = max(e[crease], v)

    # longitudinal character lines
    for s in range(nf, S - 1 - nr):
        xm = (allr[s][0].x + allr[s + 1][0].x) / 2
        for j, v in ((1, 1.0), (3, 1.0), (5, 1.0), (6, 0.7), (7, 1.0)):
            set_crease(V[s][j], V[s + 1][j], v)
        if -1.98 <= xm <= 0.45 + CAB:
            set_crease(V[s][9], V[s + 1][9], 0.9)
            set_crease(V[s][10], V[s + 1][10], 0.85)
    # transverse lines
    def ring_crease(s, js, v):
        for j in js:
            set_crease(V[s][j], V[s][j + 1], v)
    ring_crease(kid[0], range(P - 1), 1.0)          # front face edge
    ring_crease(kid[-1], range(P - 1), 1.0)         # tail face edge
    for k in FLARE_KEYS:
        ring_crease(kid[k], range(2, 6), 1.0)       # box flares
    ring_crease(kid[7], (9, 10), 0.9)                # windshield base
    ring_crease(kid[8], (10,), 0.6)                  # header
    ring_crease(kid[9], (10,), 0.7)                  # rear glass top
    ring_crease(kid[13], (9, 10), 0.9)               # rear glass bottom / deck

    for f in bm.faces:
        f.smooth = True
    ob = H.bm_to_obj(bm, "NV_Body", col, [M["paint"], M["glass"], M["black"], M["gloss"], M["liner"], M["trim"]])
    mr = ob.modifiers.new("Mirror", 'MIRROR')
    mr.use_axis = (False, True, False)
    mr.use_clip = True
    mr.use_mirror_merge = True
    ss = ob.modifiers.new("Subsurf", 'SUBSURF')
    ss.levels = 2
    ss.render_levels = 3
    return ob


# ----------------------------------------------------------------- cutters
def cut_col():
    c = H.get_col(CUT, H.get_col(COL))
    c.hide_render = True
    return c


def add_cutter(name, bm, mat):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, name, cut_col(), [mat])
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


def setup_boolean():
    body = bpy.data.objects["NV_Body"]
    for m in list(body.modifiers):
        if m.type == 'BOOLEAN':
            body.modifiers.remove(m)
    b = body.modifiers.new("Cuts", 'BOOLEAN')
    b.operand_type = 'COLLECTION'
    b.collection = cut_col()
    b.operation = 'DIFFERENCE'
    b.solver = 'EXACT'
    b.material_mode = 'TRANSFER'


def arch_poly(W, grow=0.0):
    x, zc = W["x"], W["rt"]
    a, top, chx, low = 0.44 + grow, 0.40 + grow, 0.26 + grow, 0.08
    pts = [(x + a, zc - 0.30), (x + a, zc + low), (x + chx, zc + top), (x - chx, zc + top), (x - a, zc + low), (x - a, zc - 0.30)]
    return H.round_corners(pts, 0.05, 5)


def build_cutters_and_details(M):
    col = H.get_col(COL)
    bvh = H.body_bvh("NV_Body")

    # --- squared wheel arches
    for tag, W in (("F", FRONT), ("R", REAR)):
        bm = bmesh.new()
        poly = arch_poly(W)
        y_in = W["y"] - W["w"] / 2 - 0.04
        H.extrude(bm, poly, 'y', y_in, 1.6)
        H.extrude(bm, poly, 'y', -1.6, -y_in)
        add_cutter("NV_Cut_Arch_" + tag, bm, M["liner"])

    # --- front: headlight band under the hood edge + lower intake
    band = H.round_corners([(-0.93, 0.633), (-0.86, 0.648), (0.86, 0.648), (0.93, 0.633)], 0.05, 5, closed=False)
    L, N = H.proj(bvh, H.resample(band, 0.01), 'front')
    bm = bmesh.new()
    H.sweep_rect(bm, L, N, 0.10, 0.12, 0.04)
    add_cutter("NV_Cut_FrontBand", bm, M["gloss"])
    bm = bmesh.new()
    H.extrude(bm, H.round_corners([(-0.80, 0.218), (0.80, 0.218), (0.86, 0.545), (-0.86, 0.545)], 0.03, 4), 'x', 2.36 + FDX, 2.9)
    add_cutter("NV_Cut_FrontIntake", bm, M["grille"])
    # grille in the band centre + pixel headlight modules
    Lc, Nc = H.proj(bvh, [(0.0, 0.648)], 'front')
    bm = bmesh.new()
    H.extrude(bm, [(-0.50, 0.607), (0.50, 0.607), (0.50, 0.689), (-0.50, 0.689)], 'x', Lc[0].x - 0.048, Lc[0].x - 0.032)
    H.bm_to_obj(bm, "NV_BandGrille", col, [M["grille"]])
    bm = bmesh.new()
    for s in (1, -1):
        for yc in (0.615, 0.785):
            Lp, Np = H.proj(bvh, [(s * yc, 0.648)], 'front')
            base = Lp[0] - Np[0] * 0.032
            n = Np[0]
            t = Vector((0, 0, 1)).cross(n).normalized()
            for r in range(2):
                for c in range(6):
                    ctr = base + t * ((c - 2.5) * 0.024) + Vector((0, 0, (r - 0.5) * 0.028))
                    H.box(bm, ctr, (0.008, 0.02, 0.021), Matrix.Rotation(math.atan2(n.y, n.x), 3, 'Z'))
    H.bm_to_obj(bm, "NV_PixelHeadlights", col, [M["led"]])
    bm = bmesh.new()
    H.box(bm, (2.49 + FDX, -0.36, 0.603), (0.03, 0.05, 0.014))
    H.bm_to_obj(bm, "NV_TowHook", col, [M["redpaint"]])
    # hood shut-lines along the hood crease + across the cowl
    x_cowl = 0.52 + CAB
    for s in (1, -1):
        xs = [2.44 + FDX - i * (2.44 + FDX - x_cowl) / 40 for i in range(41)]
        pts = [(x, s * (prof_at(7, x)[0] - 0.012)) for x in xs]
        H.ribbon_obj("NV_HoodLine_" + ("L" if s > 0 else "R"), bvh, pts, 'top', 0.004, 0.0015, 0.004,
                     M["liner"], col)
    yc = prof_at(7, x_cowl)[0] - 0.012
    H.ribbon_obj("NV_HoodLine_Cowl", bvh, [(x_cowl, -yc), (x_cowl, yc)], 'top', 0.004, 0.0015, 0.004, M["liner"], col)
    # wipers resting on the windshield base
    for i, (a, b) in enumerate((((0.40 + CAB, -0.58), (0.33 + CAB, 0.02)), ((0.40 + CAB, 0.02), (0.33 + CAB, 0.56)))):
        H.ribbon_obj("NV_Wiper_%d" % i, bvh, [a, b], 'top', 0.012, 0.012, 0.0, M["black"], col, step=0.02)

    # --- splitter, centre strut, canards
    bm = bmesh.new()
    plan = [(2.20, -0.99), (2.48, -0.99), (2.565, -0.82), (2.595, -0.50), (2.605, 0.0), (2.595, 0.50), (2.565, 0.82),
            (2.48, 0.99), (2.20, 0.99)]
    plan = [(x + FDX, y) for x, y in plan]
    H.extrude(bm, H.round_corners(plan, 0.03, 3), 'z', 0.15, 0.176)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_Splitter", col, [M["black"]], sharp_angle=35)
    bm = bmesh.new()   # black lip closing the gap between bumper and splitter
    lip = [(2.30, -0.90), (2.47, -0.90), (2.50, 0.0), (2.47, 0.90), (2.30, 0.90)]
    H.extrude(bm, [(x + FDX, y) for x, y in lip], 'z', 0.17, 0.225)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_ChinLip", col, [M["black"]], sharp_angle=35)
    bm = bmesh.new()
    H.extrude(bm, [(2.42 + FDX, 0.17), (2.59 + FDX, 0.17), (2.53 + FDX, 0.24), (2.42 + FDX, 0.27)], 'y', -0.012, 0.012)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_SplitterStrut", col, [M["black"]], sharp_angle=35)

    # --- rear: tail light band, lower black panel, diffuser, rain light
    tband = H.round_corners([(-0.915, 0.855), (-0.85, 0.87), (0.85, 0.87), (0.915, 0.855)], 0.05, 5, closed=False)
    L, N = H.proj(bvh, H.resample(tband, 0.01), 'back')
    bm = bmesh.new()
    H.sweep_rect(bm, L, N, 0.08, 0.12, 0.03)
    add_cutter("NV_Cut_TailBand", bm, M["gloss"])
    H.ribbon_obj("NV_TailLine", bvh, [(-0.84, 0.893), (0.84, 0.893)], 'back', 0.007, 0.003, 0.003, M["red"], col, sink=0.024)
    bm = bmesh.new()
    for s in (1, -1):
        for yc in (0.58, 0.74):
            Lp, Np = H.proj(bvh, [(s * yc, 0.862)], 'back')
            base = Lp[0] - Np[0] * 0.024
            for c in range(6):
                for r in range(2):
                    H.box(bm, base + Vector((0, (c - 2.5) * 0.021, (r - 0.5) * 0.024)), (0.008, 0.017, 0.017))
    H.bm_to_obj(bm, "NV_PixelTail", col, [M["red"]])
    bm = bmesh.new()
    H.extrude(bm, H.round_corners([(-0.82, 0.36), (0.82, 0.36), (0.80, 0.66), (-0.80, 0.66)], 0.03, 4), 'x', -2.43, -2.9)
    add_cutter("NV_Cut_RearPanel", bm, M["grille"])
    bm = bmesh.new()
    for y in (0.14, -0.14, 0.40, -0.40, 0.66, -0.66):
        top = []
        for i in range(9):
            x = -1.95 - 0.52 * i / 8
            Lf, _ = H.proj(bvh, [(x, y)], 'bottom')
            top.append((x, (Lf[0].z + 0.01) if Lf else 0.33))
        poly = [(-1.95, 0.18), (-2.59, 0.20), (-2.59, 0.34), (-2.47, top[-1][1])] + list(reversed(top[:-1]))
        H.extrude(bm, poly, 'y', y - 0.007, y + 0.007)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_Diffuser", col, [M["black"]], sharp_angle=35)
    bm = bmesh.new()
    H.box(bm, (-2.44, 0, 0.40), (0.02, 0.07, 0.05))
    H.bm_to_obj(bm, "NV_RainLight", col, [M["red"]])
    bm = bmesh.new()
    H.box(bm, (-2.485, -0.62, 0.345), (0.03, 0.05, 0.014))
    H.bm_to_obj(bm, "NV_TowHookRear", col, [M["redpaint"]])

    # --- rear fender vent behind the door
    for s in (1, -1):
        bm = bmesh.new()
        poly = H.round_corners([(-0.57, 0.54), (-0.585, 0.815), (-0.80, 0.825), (-0.80, 0.545)], 0.02, 3)
        H.extrude(bm, poly, 'y', s * 0.90, s * 1.5)
        add_cutter("NV_Cut_Vent_" + ("L" if s > 0 else "R"), bm, M["grille"])

    # --- rear window louvres
    build_louvres(M, col, bvh)
    build_small_details(M, col, bvh)
    # --- wing
    build_wing(M, col, bvh)
    # --- mirrors, door lines, handles
    for s in (1, -1):
        tag = "L" if s > 0 else "R"
        door = H.round_corners([(0.73, 0.83), (0.835, 0.30), (0.80, 0.29), (-0.47, 0.29), (-0.50, 0.30), (-0.535, 0.83)],
                               0.04, 5, closed=False)
        H.ribbon_obj("NV_DoorLine_" + tag, bvh, door, 'side', 0.0045, 0.0015, 0.004, M["liner"], col, side=s)
        H.ribbon_obj("NV_Handle_" + tag, bvh, [(-0.30, 0.775), (-0.44, 0.775)], 'side', 0.02, 0.002, 0.004,
                     M["gloss"], col, side=s)
        build_mirror(s, M, col, bvh)


def build_mirror(s, M, col, bvh):
    """Angular paddle mirror on a slim arm from the door ledge."""
    tag = "L" if s > 0 else "R"
    Lm, _ = H.proj(bvh, [(0.33 + CAB, 0.848)], 'side', s)
    base = Lm[0]
    cx, cy, cz = 0.29 + CAB, s * 1.005, 0.925
    bm = bmesh.new()
    rear = [(cx, cy - s * 0.078, cz - 0.036), (cx, cy + s * 0.078, cz - 0.030), (cx, cy + s * 0.078, cz + 0.036), (cx, cy - s * 0.078, cz + 0.036)]
    front = [(cx + 0.085, cy - s * 0.05, cz - 0.022), (cx + 0.07, cy + s * 0.062, cz - 0.018), (cx + 0.07, cy + s * 0.062, cz + 0.022), (cx + 0.085, cy - s * 0.05, cz + 0.024)]
    vr = [bm.verts.new(p) for p in rear]
    vf = [bm.verts.new(p) for p in front]
    bm.faces.new(vr)
    bm.faces.new(list(reversed(vf)))
    for i in range(4):
        bm.faces.new((vr[i], vr[(i + 1) % 4], vf[(i + 1) % 4], vf[i]))
    tip = Vector((cx + 0.045, cy - s * 0.05, cz - 0.012))
    arm = [base + Vector((0.0, 0.0, -0.006)), base.lerp(tip, 0.5) + Vector((0.0, 0.0, 0.012)), tip]
    arm = H.resample(H.round_corners(arm, 0.03, 5, closed=False), 0.01)
    H.sweep_rect(bm, arm, [Vector((1, 0, 0))] * len(arm), 0.024, 0.03, 0.03)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "NV_Mirror_" + tag, col, [M["black"]], sharp_angle=35)
    bv = ob.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.004
    bv.segments = 2
    bv.limit_method = 'ANGLE'
    bm = bmesh.new()
    H.box(bm, (cx - 0.002, cy, cz + 0.002), (0.004, 0.142, 0.058))
    H.bm_to_obj(bm, "NV_MirrorGlass_" + tag, col, [M["chrome"]])


def tube(bm, pts, r, segs=12):
    """Circular tube swept along a polyline (parallel-transport frames)."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    rings, prev = [], None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        if prev is None:
            a = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            nv = t.cross(a).normalized()
        else:
            nv = (prev - t * prev.dot(t)).normalized()
        prev = nv
        bv = t.cross(nv).normalized()
        rings.append([bm.verts.new(p + (nv * math.cos(2 * math.pi * k / segs) + bv * math.sin(2 * math.pi * k / segs)) * r)
                      for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))


def build_seat(tag, yc, M, col):
    """Carbon-shell racing bucket with grey inserts and red harness."""
    bm = bmesh.new()
    Rc = Matrix.Rotation(math.radians(-8), 3, 'Y')      # cushion, front edge up
    Rb = Matrix.Rotation(math.radians(-22), 3, 'Y')     # backrest leaning back
    sx = SEAT_DX
    P = Vector((-0.54 + sx, yc, 0.30))
    at = lambda R, loc: P + R @ Vector(loc)
    s0 = len(bm.faces)
    H.box(bm, Vector((-0.32 + sx, yc, 0.27)), (0.46, 0.44, 0.08), Rc)
    for s in (1, -1):
        H.box(bm, Vector((-0.34 + sx, yc + s * 0.215, 0.32)), (0.42, 0.075, 0.14), Rc)
    H.box(bm, at(Rb, (0.0, 0, 0.33)), (0.08, 0.46, 0.68), Rb)
    for s in (1, -1):
        H.box(bm, at(Rb, (0.045, s * 0.22, 0.28)), (0.15, 0.075, 0.52), Rb)
    H.box(bm, at(Rb, (0.012, 0, 0.74)), (0.10, 0.30, 0.20), Rb)
    for s in (1, -1):
        H.box(bm, at(Rb, (0.035, s * 0.12, 0.74)), (0.09, 0.06, 0.20), Rb)
    H.new_faces_mat(bm, s0, 0)
    s0 = len(bm.faces)
    H.box(bm, at(Rb, (0.045, 0, 0.33)), (0.02, 0.27, 0.48), Rb)
    H.box(bm, Vector((-0.31 + sx, yc, 0.316)), (0.36, 0.27, 0.02), Rc)
    H.new_faces_mat(bm, s0, 1)
    s0 = len(bm.faces)
    for s in (1, -1):
        H.box(bm, at(Rb, (0.062, s * 0.075, 0.43)), (0.008, 0.046, 0.58), Rb)
    H.new_faces_mat(bm, s0, 2)
    ob = H.bm_to_obj(bm, "NV_Seat_" + tag, col, [M["seat"], M["insert"], M["harness"]], sharp_angle=40)
    bv = ob.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.016
    bv.segments = 3
    bv.limit_method = 'ANGLE'
    return ob


def build_interior(M, col):
    c = CAB
    # dashboard
    bm = bmesh.new()
    H.extrude(bm, [(0.08 + c, 0.60), (0.25 + c, 0.55), (0.43 + c, 0.56), (0.43 + c, 0.842), (0.16 + c, 0.79), (0.08 + c, 0.73)],
              'y', -0.76, 0.76)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "NV_Dashboard", col, [M["interior"]], sharp_angle=40)
    bv = ob.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.012
    bv.segments = 3
    bv.limit_method = 'ANGLE'
    bm = bmesh.new()
    H.box(bm, (0.077 + c, 0, 0.716), (0.006, 1.46, 0.016))
    H.bm_to_obj(bm, "NV_DashAccent", col, [M["teal"]])
    # driver cluster hood + screens
    bm = bmesh.new()
    H.box(bm, (0.13 + c, 0.37, 0.80), (0.13, 0.28, 0.07))
    H.box(bm, (0.09, 0.0, 0.26), (0.68, 0.18, 0.22))           # centre console
    H.box(bm, (0.25, 0.0, 0.39), (0.05, 0.035, 0.05))          # gear selector
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "NV_Console", col, [M["interior"]], sharp_angle=40)
    bv = ob.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.01
    bv.segments = 2
    bv.limit_method = 'ANGLE'
    bm = bmesh.new()
    H.box(bm, (0.063 + c, 0.37, 0.795), (0.004, 0.25, 0.055))
    H.box(bm, (0.078 + c, 0.0, 0.655), (0.006, 0.20, 0.09))
    H.bm_to_obj(bm, "NV_Screens", col, [M["screen"]])
    # interior rear-view mirror hanging from the header
    bm = bmesh.new()
    H.box(bm, (0.11, 0.0, 1.215), (0.03, 0.22, 0.065))
    H.box(bm, (0.085, 0.0, 1.255), (0.02, 0.02, 0.03))
    H.bm_to_obj(bm, "NV_RearViewMirror", col, [M["seat"]])
    bm = bmesh.new()
    H.box(bm, (0.094, 0.0, 1.215), (0.004, 0.2, 0.052))
    H.bm_to_obj(bm, "NV_RearViewGlass", col, [M["chrome"]])
    # steering wheel (flat-bottom racing rim, 3 spokes, column into the dash)
    C = Vector((0.18, 0.37, 0.73))
    d = Vector((-0.93, 0, 0.36)).normalized()          # column axis, toward the driver
    u = Vector((0, 1, 0))
    v = u.cross(d).normalized()
    R = 0.165
    rim = []
    for k in range(48):
        th = 2 * math.pi * k / 48
        px, py = R * math.cos(th), R * math.sin(th)
        py = max(py, -0.72 * R)
        rim.append(C + u * px + v * py)
    rim.append(rim[0])
    bm = bmesh.new()
    tube(bm, rim, 0.017, 10)
    for ang in (0.0, math.pi, -math.pi / 2):
        tip = C + u * (R * math.cos(ang)) + v * max(R * math.sin(ang), -0.72 * R)
        H.sweep_rect(bm, [C, tip], [d, d], 0.035, 0.008, 0.008)
    tube(bm, [C - d * 0.02, C - d * 0.34], 0.022, 12)
    tube(bm, [C + d * 0.005, C - d * 0.03], 0.048, 16)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_SteeringWheel", col, [M["seat"]], smooth=True)
    # seats
    build_seat("L", 0.37, M, col)
    build_seat("R", -0.37, M, col)
    # rear bulkhead + hydrogen tank
    bm = bmesh.new()
    H.box(bm, (-1.0, 0, 0.55), (0.03, 1.56, 0.78))
    H.bm_to_obj(bm, "NV_Bulkhead", col, [M["interior"]])
    bm = bmesh.new()
    H.revolve(bm, [(0.005, 0.56), (0.12, 0.555), (0.16, 0.50), (0.16, -0.50), (0.12, -0.555), (0.005, -0.56)],
              (-1.33, 0, 0.62), 1, nseg=40)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_H2Tank", col, [M["alu"]], smooth=True)
    bm = bmesh.new()
    for a0 in (0.30, -0.25):
        H.revolve(bm, [(0.158, a0), (0.167, a0), (0.167, a0 - 0.05), (0.158, a0 - 0.05)], (-1.33, 0, 0.62), 1, nseg=40)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_H2Straps", col, [M["blue"]], smooth=True)
    # roll cage
    bm = bmesh.new()
    rnd = lambda pts, r: H.resample(H.round_corners(pts, r, 6, closed=False), 0.03)
    tube(bm, rnd([(-0.95, 0.80, 0.16), (-0.95, 0.80, 0.84), (-0.95, 0.63, 1.20), (-0.95, -0.63, 1.20),
                  (-0.95, -0.80, 0.84), (-0.95, -0.80, 0.16)], 0.10), 0.019)
    tube(bm, [(-0.95, 0.62, 1.18), (-0.95, -0.78, 0.26)], 0.017)
    tube(bm, [(-0.95, 0.77, 0.95), (-0.95, -0.77, 0.95)], 0.017)
    for s in (1, -1):
        tube(bm, rnd([(0.36 + c, s * 0.74, 0.80), (-0.20 + c, s * 0.61, 1.20), (-0.95, s * 0.62, 1.205)], 0.12), 0.018)
        tube(bm, [(-0.95, s * 0.62, 1.19), (-1.75, s * 0.55, 0.76)], 0.017)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_RollCage", col, [M["cage"]], smooth=True)


def build_small_details(M, col, bvh):
    # hood pins
    bm = bmesh.new()
    for s in (1, -1):
        L, N = H.proj(bvh, [(2.28 + FDX, s * 0.62)], 'top')
        p = L[0]
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=20, radius1=0.022, radius2=0.022,
                              depth=0.006, matrix=Matrix.Translation(p + Vector((0, 0, 0.002))))
        H.box(bm, p + Vector((0, 0, 0.008)), (0.034, 0.006, 0.006))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_HoodPins", col, [M["alu"]], sharp_angle=40)
    # silver slat across the lower intake
    bm = bmesh.new()
    H.box(bm, (2.462 + FDX, 0, 0.385), (0.045, 1.62, 0.014))
    H.bm_to_obj(bm, "NV_IntakeSlat", col, [M["paint"]])
    # hydrogen filler lid (right rear fender)
    ring = [(-2.12 + 0.055 * math.cos(2 * math.pi * k / 36), 0.70 + 0.055 * math.sin(2 * math.pi * k / 36)) for k in range(37)]
    H.ribbon_obj("NV_FillerLid", bvh, ring, 'side', 0.004, 0.0015, 0.004, M["liner"], col, side=-1, step=0.008)


def build_louvres(M, col, bvh):
    bm = bmesh.new()
    n = 12
    for i in range(n):
        x = -0.90 - (1.00 * i / (n - 1))
        Lh, Nh = H.proj(bvh, [(x, 0.0)], 'top')
        p, nn = Lh[0], Nh[0]
        yw = prof_at(10, x)[0] - 0.03
        chord = Vector((-math.cos(math.radians(18)), 0, math.sin(math.radians(18))))
        c = p + nn * 0.018
        v = []
        for (dy, dc, dt) in ((-1, 0, 0), (1, 0, 0), (1, 1, 0), (-1, 1, 0), (-1, 0, 1), (1, 0, 1), (1, 1, 1), (-1, 1, 1)):
            q = c + Vector((0, dy * yw, 0)) + chord * (dc * 0.068) + nn * (dt * 0.007)
            v.append(bm.verts.new(q))
        for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            bm.faces.new([v[k] for k in f])
    # side rails + centre rail
    for yy in (-1, 0, 1):
        pts, nrm = [], []
        for i in range(12):
            x = -0.86 - 1.10 * i / 11
            y = yy * (prof_at(10, x)[0] - 0.03)
            Lh, Nh = H.proj(bvh, [(x, y)], 'top')
            if Lh:
                pts.append(Lh[0] + Nh[0] * 0.012)
                nrm.append(Nh[0])
        H.sweep_rect(bm, pts, nrm, 0.012, 0.03, 0.012)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_Louvres", col, [M["black"]], sharp_angle=35)


def build_wing(M, col, bvh):
    chord, span = 0.36, 1.86
    le = Vector((-2.03, 0, 1.25))
    aoa = math.radians(8)
    sec = []
    for (u, v) in H.naca(0.05, 0.4, 0.11):
        x, z = -u * chord, -v * chord
        sec.append((le.x + x * math.cos(aoa) + z * math.sin(aoa), le.z - x * math.sin(aoa) + z * math.cos(aoa)))
    bm = bmesh.new()
    rings = []
    for i in range(13):
        y = -span / 2 + span * i / 12
        rings.append([bm.verts.new((x, y, z)) for (x, z) in sec])
    Pn = len(sec)
    for a, b in zip(rings, rings[1:]):
        for j in range(Pn):
            bm.faces.new((a[j], a[(j + 1) % Pn], b[(j + 1) % Pn], b[j]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_Wing", col, [M["black"]], sharp_angle=50)
    xs = [x for x, _ in sec]
    zs = [z for _, z in sec]
    bm = bmesh.new()
    plate = H.round_corners([(max(xs) + 0.05, min(zs) - 0.10), (min(xs) - 0.05, min(zs) - 0.04),
                             (min(xs) - 0.05, max(zs) + 0.09), (max(xs) - 0.10, max(zs) + 0.03)], 0.03, 4)
    for s in (1, -1):
        H.extrude(bm, plate, 'y', s * (span / 2 - 0.004), s * (span / 2 + 0.010))
    # pylons from the deck to the wing underside
    for s in (1, -1):
        y = s * 0.42
        Ld, _ = H.proj(bvh, [(-2.24, y)], 'top')
        zd = Ld[0].z - 0.01
        zw = min(z for (x, z) in sec if abs(x + 2.20) < 0.06) + 0.01
        prof = [(-2.14, zd), (-2.36, zd), (-2.30, zw), (-2.16, zw)]
        H.extrude(bm, prof, 'y', y - 0.011, y + 0.011)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    H.bm_to_obj(bm, "NV_WingPlates", col, [M["black"]], sharp_angle=35)


# ----------------------------------------------------------------- shrink-wrapped overlays (glass, blades)
def overlay(name, poly, side, mat, offset, col, levels=3):
    bm = bmesh.new()
    f = bm.faces.new([bm.verts.new((x, side * 1.45, z)) for (x, z) in poly])
    bm.normal_update()
    if f.normal.y * side < 0:
        bmesh.ops.reverse_faces(bm, faces=[f])
    ob = H.bm_to_obj(bm, name, col, [mat], smooth=True)
    sd = ob.modifiers.new("Sub", 'SUBSURF')
    sd.subdivision_type = 'SIMPLE'
    sd.levels = sd.render_levels = levels
    sw = ob.modifiers.new("Wrap", 'SHRINKWRAP')
    sw.target = bpy.data.objects["NV_Body"]
    sw.wrap_method = 'PROJECT'
    sw.use_project_y = True
    sw.use_negative_direction = side > 0
    sw.use_positive_direction = side < 0
    sw.offset = offset
    return ob


def grid_overlay(name, bot, top, xb, xt, side, mat, offset, col, nu=48, nv=10):
    """Quad-grid patch between a bottom and a top edge (x ranges xb / xt), shrink-wrapped from the side."""
    bm = bmesh.new()
    rows = []
    for i in range(nu + 1):
        u = i / nu
        x0 = xb[0] + (xb[1] - xb[0]) * u
        x1 = xt[0] + (xt[1] - xt[0]) * u
        p0, p1 = Vector((x0, 0, bot(x0))), Vector((x1, 0, top(x1)))
        rows.append([bm.verts.new(p0.lerp(p1, j / nv) + Vector((0, side * 1.45, 0))) for j in range(nv + 1)])
    for a, b in zip(rows, rows[1:]):
        for j in range(nv):
            bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
    bm.normal_update()
    if next(iter(bm.faces)).normal.y * side < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    ob = H.bm_to_obj(bm, name, col, [mat])
    sw = ob.modifiers.new("Wrap", 'SHRINKWRAP')
    sw.target = bpy.data.objects["NV_Body"]
    sw.wrap_method = 'PROJECT'
    sw.use_project_y = True
    sw.use_negative_direction = side > 0
    sw.use_positive_direction = side < 0
    sw.offset = offset
    return ob


def grid_overlay_top(name, xr, ylo, yhi, mat, offset, col, nu=40, nv=6):
    """Quad-grid patch in plan view (x, y), shrink-wrapped downward onto the body (windshield frit etc.)."""
    bm = bmesh.new()
    rows = []
    for i in range(nu + 1):
        x = xr[0] + (xr[1] - xr[0]) * i / nu
        a, b = ylo(x), yhi(x)
        rows.append([bm.verts.new((x, a + (b - a) * j / nv, 3.0)) for j in range(nv + 1)])
    for a, b in zip(rows, rows[1:]):
        for j in range(nv):
            bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
    bm.normal_update()
    if next(iter(bm.faces)).normal.z < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    ob = H.bm_to_obj(bm, name, col, [mat])
    sw = ob.modifiers.new("Wrap", 'SHRINKWRAP')
    sw.target = bpy.data.objects["NV_Body"]
    sw.wrap_method = 'PROJECT'
    sw.use_project_z = True
    sw.use_negative_direction = True
    sw.use_positive_direction = False
    sw.offset = offset
    return ob


def build_overlays(M):
    col = H.get_col(COL)
    z9 = lambda x: prof_at(9, x)[1]
    z10 = lambda x: prof_at(10, x)[1]
    y10 = lambda x: prof_at(10, x)[0]

    def blade_top(x):
        pts = [(0.835, 0.335), (-0.55, 0.47), (-0.915, 0.50)]
        for (xa, za), (xb_, zb) in zip(pts, pts[1:]):
            if xb_ <= x <= xa:
                return za + (zb - za) * (xa - x) / (xa - xb_)
        return pts[-1][1]

    def seal_th(x):          # A-pillar is wider than the roof rail
        t = min(1.0, max(0.0, (x + 0.30 - CAB) / 0.20))
        return 0.028 + (0.062 - 0.028) * t

    def seal_bot(x):
        return min(max(z9(x) + 0.004, z10(x) - seal_th(x)), z10(x) - 0.007)

    x0 = 0.40 + CAB
    for s in (1, -1):
        tag = "L" if s > 0 else "R"
        # black A-pillar + roof rail, beltline seal, B-pillar, slanted C-seal, silver C-pillar
        grid_overlay("NV_Seal_Top_" + tag, seal_bot, lambda x: z10(x) - 0.001,
                     (x0, -1.02), (x0, -1.02), s, M["trim"], 0.0012, col, nu=220, nv=4)
        grid_overlay("NV_Seal_Belt_" + tag, lambda x: z9(x) + 0.004, lambda x: min(z9(x) + 0.022, z10(x) - 0.006),
                     (x0, -1.34), (x0, -1.34), s, M["trim"], 0.0012, col, nu=220, nv=2)
        grid_overlay("NV_BPillar_" + tag, lambda x: z9(x) + 0.004, lambda x: z10(x) - 0.004,
                     (-0.41, -0.485), (-0.51, -0.585), s, M["trim"], 0.0014, col, nu=4, nv=16)
        grid_overlay("NV_CSeal_" + tag, lambda x: z9(x) + 0.004, lambda x: z10(x) - 0.004,
                     (-1.272, -1.31), (-0.952, -0.99), s, M["trim"], 0.0016, col, nu=3, nv=16)
        grid_overlay("NV_CPillar_" + tag, lambda x: z9(x) + 0.002, lambda x: z10(x) - 0.001,
                     (-1.295, -1.40), (-0.975, -1.40), s, M["paint"], 0.0024, col, nu=12, nv=16)
        grid_overlay("NV_SideBlade_" + tag, lambda x: 0.292, blade_top, (0.835, -0.915), (0.835, -0.915),
                     s, M["black"], 0.0016, col, nu=60, nv=6)
        # windshield side frit
        if s > 0:
            grid_overlay_top("NV_Frit_Side_L", (0.445 + CAB, -0.25 + CAB), lambda x: y10(x) - 0.042,
                             lambda x: y10(x) + 0.010, M["trim"], 0.0010, col, nu=140, nv=5)
        else:
            grid_overlay_top("NV_Frit_Side_R", (0.445 + CAB, -0.25 + CAB), lambda x: -y10(x) - 0.010,
                             lambda x: -y10(x) + 0.042, M["trim"], 0.0010, col, nu=140, nv=5)
    grid_overlay_top("NV_Frit_Header", (-0.19 + CAB, -0.248 + CAB), lambda x: -y10(x) + 0.004, lambda x: y10(x) - 0.004,
                     M["gloss"], 0.0012, col, nu=3, nv=40)
    grid_overlay_top("NV_Frit_Base", (0.448 + CAB, 0.418 + CAB), lambda x: -y10(x) + 0.004, lambda x: y10(x) - 0.004,
                     M["gloss"], 0.0012, col, nu=2, nv=40)


# ----------------------------------------------------------------- wheels (aero discs)
def build_wheel(tag, W, side, M, col):
    rt, rr, w = W["rt"], W["rr"], W["w"]
    h = rt - rr
    bm = bmesh.new()
    tread = w / 2 - 0.04
    prof = [(rr + 0.004, w / 2 - 0.018), (rr + 0.018, w / 2 - 0.004), (rr + 0.45 * h, w / 2 + 0.004),
            (rr + 0.72 * h, w / 2 + 0.002), (rt - 0.09 * h, w / 2 - 0.010), (rt - 0.012, w / 2 - 0.026), (rt, tread)]
    for g in (0.24, 0.07, -0.07, -0.24):
        gc = g * w
        prof += [(rt, gc + 0.008), (rt - 0.007, gc + 0.006), (rt - 0.007, gc - 0.006), (rt, gc - 0.008)]
    prof += [(rt, -tread), (rt - 0.012, -(w / 2 - 0.026)), (rt - 0.09 * h, -(w / 2 - 0.010)),
             (rr + 0.72 * h, -(w / 2 + 0.002)), (rr + 0.45 * h, -(w / 2 + 0.004)),
             (rr + 0.018, -(w / 2 - 0.004)), (rr + 0.004, -(w / 2 - 0.018))]
    s0 = len(bm.faces)
    H.revolve(bm, prof, (0, 0, 0), side, nseg=96)
    H.new_faces_mat(bm, s0, 0)
    a = w / 2 - 0.012
    # white aero disc (1): lip + face
    s0 = len(bm.faces)
    H.revolve(bm, [(rr + 0.012, a + 0.004), (rr + 0.012, a - 0.02), (rr - 0.02, a - 0.024), (rr - 0.02, a - 0.004)],
              (0, 0, 0), side, nseg=96)
    H.revolve(bm, [(0.05, a - 0.006), (0.19, a - 0.016), (0.19, a - 0.032), (0.05, a - 0.022)], (0, 0, 0), side, nseg=96)
    H.new_faces_mat(bm, s0, 1)
    # dark slot ring behind (2)
    s0 = len(bm.faces)
    H.revolve(bm, [(0.185, a - 0.045), (rr - 0.015, a - 0.045), (rr - 0.015, a - 0.06), (0.185, a - 0.06)],
              (0, 0, 0), side, nseg=96)
    H.revolve(bm, [(rr - 0.018, a - 0.03), (rr - 0.018, -w / 2 + 0.012), (rr + 0.006, -w / 2 + 0.006), (rr + 0.006, a - 0.03)],
              (0, 0, 0), side, nseg=64)
    H.new_faces_mat(bm, s0, 2)
    # radial blades between the disc face and the lip (1)
    s0 = len(bm.faces)
    nb = 44
    for k in range(nb):
        th = 2 * math.pi * k / nb
        c, s = math.cos(th), math.sin(th)
        T = Vector((-s, 0, c))
        R = Vector((c, 0, s))
        A = Vector((0, side, 0))
        vs = []
        for rr_, aa in ((0.187, a - 0.016), (rr - 0.018, a - 0.006)):
            for dt in (-1, 1):
                for da in (0, -0.03):
                    vs.append(R * rr_ + T * dt * 0.0105 + A * (aa + da))
        v = [bm.verts.new(p) for p in vs]
        for f in ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
            bm.faces.new([v[i] for i in f])
    H.new_faces_mat(bm, s0, 1)
    # red centre cap (3)
    s0 = len(bm.faces)
    H.revolve(bm, [(0.012, a + 0.004), (0.035, a + 0.002), (0.055, a - 0.008), (0.055, a - 0.03), (0.012, a - 0.03)],
              (0, 0, 0), side, nseg=48)
    H.new_faces_mat(bm, s0, 3)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = H.bm_to_obj(bm, "NV_Wheel_" + tag, col, [M["tire"], M["disc"], M["liner"], M["redpaint"]], sharp_angle=42)
    ob.location = (W["x"], side * W["y"], W["rt"])
    bv = ob.modifiers.new("Bevel", 'BEVEL')
    bv.width = 0.002
    bv.segments = 2
    bv.limit_method = 'ANGLE'
    bv.angle_limit = math.radians(42)
    return ob


def build_wheels(M):
    col = H.get_col(COL)
    for tag, W, side in (("FL", FRONT, 1), ("FR", FRONT, -1), ("RL", REAR, 1), ("RR", REAR, -1)):
        build_wheel(tag, W, side, M, col)


# ----------------------------------------------------------------- Unity export
WHEEL_NAMES = {"NV_Wheel_FL": "wheel-front-left", "NV_Wheel_FR": "wheel-front-right",
               "NV_Wheel_RL": "wheel-back-left", "NV_Wheel_RR": "wheel-back-right"}


def export_for_unity(path):
    """Bake the car into game meshes and export an FBX that faces +Z in Unity (metres, Y up).

    Blender +X (car front) is turned to -Y, which the FBX axis conversion maps to Unity +Z.
    Body parts are merged into one mesh; the 4 wheels stay separate with their pivot at the hub and
    Kenney-style names so the NoHesi VehicleController finds them. An inward-facing dark lining is added
    around the cabin because Unity culls the back faces the Blender material used as cabin trim.
    """
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    src = bpy.data.collections[COL]
    dg = bpy.context.evaluated_depsgraph_get()
    R = Matrix.Rotation(math.radians(-90), 4, 'Z')
    exp = H.get_col("NV74_Export")
    for o in list(exp.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    interior = bpy.data.materials["NV_Interior"]
    body_parts, wheels = [], []
    # game budget: wheels without their edge bevel (halves their triangle count)
    muted = []
    for name in WHEEL_NAMES:
        for m in bpy.data.objects[name].modifiers:
            if m.type == 'BEVEL' and m.show_viewport:
                m.show_viewport = False
                muted.append(m)
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()

    def triangulate_ngons(me):
        # Unity drops self-intersecting n-gons (boolean cuts): triangulate them here, keep quads
        bm = bmesh.new()
        bm.from_mesh(me)
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.to_mesh(me)
        bm.free()

    for ob in src.objects:
        if ob.type != 'MESH' or ob.hide_render:
            continue
        ev = ob.evaluated_get(dg)
        me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
        triangulate_ngons(me)
        if ob.name in WHEEL_NAMES:
            me.transform(R)
            o = bpy.data.objects.new(WHEEL_NAMES[ob.name], me)
            o.location = R @ ob.matrix_world.translation
            exp.objects.link(o)
            wheels.append(o)
            continue
        me.transform(ob.matrix_world)
        if ob.name == "NV_Body":
            # cabin lining: non-glass shell faces around the cabin, duplicated, flipped, pushed 4 mm inward
            bm = bmesh.new()
            bm.from_mesh(me)
            glass = {i for i, m in enumerate(me.materials) if m and m.name == "NV_Glass"}
            keep = [f for f in bm.faces if -1.42 <= f.calc_center_median().x <= 0.80 and f.material_index not in glass]
            dup = bmesh.ops.duplicate(bm, geom=keep)
            new_faces = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
            new_set = set(new_faces)
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in new_set], context='FACES')
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
            bm.normal_update()
            for v in bm.verts:
                v.co += v.normal * 0.004
            for f in bm.faces:
                f.material_index = 0
                f.smooth = True
            lm = bpy.data.meshes.new("NV74_Lining")
            bm.to_mesh(lm)
            bm.free()
            lm.materials.append(interior)
            lm.transform(R)
            lo = bpy.data.objects.new("NV74_Lining", lm)
            exp.objects.link(lo)
            body_parts.append(lo)
        me.transform(R)
        o = bpy.data.objects.new(ob.name + "_x", me)
        exp.objects.link(o)
        body_parts.append(o)
    # merge every body part into a single multi-material mesh
    with bpy.context.temp_override(active_object=body_parts[0], selected_objects=body_parts,
                                   selected_editable_objects=body_parts, object=body_parts[0]):
        bpy.ops.object.join()
    body = body_parts[0]
    body.name = "body"
    body.data.name = "Vision74_Body"
    # export only the baked objects
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in [body] + wheels:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='AUTO', embed_textures=False)
    stats = {o.name: len(o.data.polygons) for o in [body] + wheels}
    for m in muted:
        m.show_viewport = True
    # clean up the temporary export copies
    for o in list(exp.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me.users == 0:
            bpy.data.meshes.remove(me)
    bpy.data.collections.remove(exp)
    return stats


# ----------------------------------------------------------------- build
def hide_collection(name):
    def walk(lc):
        for c in lc.children:
            if c.name == name:
                c.exclude = True
            walk(c)
    walk(bpy.context.view_layer.layer_collection)


def build_all():
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    hide_collection("Hypercar")
    M = make_materials()
    H.clear_col(COL)
    build_body(M)
    build_cutters_and_details(M)
    setup_boolean()
    build_wheels(M)
    build_overlays(M)
    build_interior(M, H.get_col(COL))
    for o in cut_col().objects:
        o.hide_set(True)
    return M
