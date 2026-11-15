"""Pack alpin v2 : animaux détaillés (niveau hd). Corps modelés par anneaux (sections elliptiques lissées), encolure,
tête avec museau, yeux, oreilles et cornes, pattes articulées à sabots, queue à touffe ; vaches avec mamelle, collier
et cloche. Les niveaux moyen et lointain gardent les modèles d'origine (mêmes proportions). Animaux tournés vers -Y."""
import math, random
from mathutils import Vector, Matrix
import alp_lib as A


def hd(iid):
    def deco(fn):
        if iid not in A.ITEMS:
            return fn
        old = A.ITEMS[iid]["build"]

        def build(g):
            if g.hd:
                fn(g)
            else:
                old(g)
        A.ITEMS[iid]["build"] = build
        return fn
    return deco


def loft(g, secs, n, color, frame=None, expo=2.4, cap0=True, cap1=True, paint=None):
    """Volume par anneaux. secs : [(centre (Vector), demi-largeur, demi-hauteur)] le long d'un axe ; frame = (axe, haut).
    expo : forme des sections (2 ellipse, > 2 plus carré). paint(face_center) -> couleur ou None."""
    bm = g.bm
    rings = []
    for i, (c, hw, hh) in enumerate(secs):
        c = Vector(c)
        if frame is None:
            ax = (Vector(secs[min(i + 1, len(secs) - 1)][0]) - Vector(secs[max(i - 1, 0)][0])).normalized()
            up = Vector((0, 0, 1))
        else:
            ax, up = frame
        side = ax.cross(up).normalized()
        upv = side.cross(ax).normalized()
        ring = []
        for k in range(n):
            a = 2 * math.pi * k / n
            ca, sa = math.cos(a), math.sin(a)
            r = 1.0 / ((abs(ca) ** expo + abs(sa) ** expo) ** (1.0 / expo))
            ring.append(bm.verts.new(c + side * (ca * r * hw) + upv * (sa * r * hh)))
        rings.append(ring)
    faces = []
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            j = (k + 1) % n
            faces.append(bm.faces.new((a[k], a[j], b[j], b[k])))
    if cap0:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if cap1:
        faces.append(bm.faces.new(rings[-1]))
    g.paint(faces, color)
    # orientation : vers l'extérieur de l'axe du volume
    cs = [Vector(s[0]) for s in secs]
    for f in faces:
        f.normal_update()
        fc = f.calc_center_median()
        near = min(cs, key=lambda c: (c - fc).length)
        if f.normal.dot(fc - near) < 0 and len(f.verts) == 4:
            f.normal_flip()
    if cap0:
        f = faces[-2] if cap1 else faces[-1]
        f.normal_update()
        if f.normal.dot(cs[0] - cs[1]) < 0:
            f.normal_flip()
    if cap1:
        f = faces[-1]
        f.normal_update()
        if f.normal.dot(cs[-1] - cs[-2]) < 0:
            f.normal_flip()
    if paint:
        for f in faces:
            c = paint(f.calc_center_median())
            if c:
                f[g.col] = A.CI[c]
    return faces


def leg(g, hip, knee, hock, hoof, r, color, hoof_color="black", n=8, front=True):
    """Patte articulée : cuisse, canon, sabot."""
    hip, knee, hock, hoof = Vector(hip), Vector(knee), Vector(hock), Vector(hoof)
    g.tube([hip, knee, hock, hoof + Vector((0, 0, 0.07))], [r, r * 0.72, r * 0.55, r * 0.5], n, color, cap_end=False)
    g.frustum((hoof.x, hoof.y, -0.03), r * 0.62, r * 0.52, 0.1, n, hoof_color, auto=False)


def ear(g, base, out, length, width, color, inner=None):
    """Oreille : feuille épaisse orientée vers « out »."""
    base, out = Vector(base), Vector(out).normalized()
    up = Vector((0, 0, 1))
    side = out.cross(up)
    if side.length < 1e-3:
        side = Vector((1, 0, 0))
    side.normalize()
    nrm = side.cross(out).normalized()
    tip = base + out * length
    pts = [base - side * width * 0.4, base + out * length * 0.55 - side * width * 0.5, tip, base + out * length * 0.55 + side * width * 0.5,
           base + side * width * 0.4]
    t = 0.02
    m = g.mark()
    F = [g.bm.verts.new(p + nrm * t) for p in pts]
    B = [g.bm.verts.new(p - nrm * t) for p in pts]
    fs = [g.bm.faces.new(F), g.bm.faces.new(list(reversed(B)))]
    for k in range(len(pts) - 1):
        fs.append(g.bm.faces.new((F[k], B[k], B[k + 1], F[k + 1])))
    fs.append(g.bm.faces.new((F[-1], B[-1], B[0], F[0])))
    g.paint(fs, color)
    import bmesh
    bmesh.ops.recalc_face_normals(g.bm, faces=fs)
    if inner:
        fs[0][g.col] = A.CI[inner]


def horn(g, base, pts_rel, r0, color="horn", n=8, rings=False):
    pts = [Vector(base)] + [Vector(base) + Vector(p) for p in pts_rel]
    rad = [r0 * (1.0 - 0.85 * k / (len(pts) - 1)) for k in range(len(pts))]
    rad[-1] = 0.0
    g.tube(pts, rad, n, color)
    if rings:                                         # anneaux de croissance (bouquetin)
        for k in range(1, len(pts) - 2):
            d = (pts[k + 1] - pts[k - 1]).normalized()
            g.tube([pts[k] - d * 0.012, pts[k] + d * 0.012], [rad[k] * 1.12, rad[k] * 1.12], n, color, cap_start=True)


def spotted(base, patch, seed, density=0.45, sides=True):
    rr = random.Random(seed)
    cs = [Vector((rr.choice((-1, 1)) * rr.uniform(0.2, 0.4), rr.uniform(-0.8, 0.8), rr.uniform(0.85, 1.25))) for _ in range(7)]
    rs = [rr.uniform(0.16, 0.3) for _ in cs]

    def f(p):
        for c, r in zip(cs, rs):
            if ((p.x - c.x) * 0.6) ** 2 + (p.y - c.y) ** 2 + (p.z - c.z) ** 2 < r * r and (p.x * c.x > 0 or not sides):
                return patch
        return None
    return f


# ----------------------------------------------------------------- vaches
def cow(g, pose, coat, patch, bell=True):
    L, H, W = 1.9, 1.38, 0.7
    lie = pose == "lie"
    drop = 0.62 if lie else 0.0
    zc = 1.0 - drop
    secs = []
    prof = [(0.0, 0.2, 0.24, 0.04), (0.06, 0.28, 0.32, 0.03), (0.18, 0.33, 0.37, 0.04), (0.35, 0.36, 0.38, 0.0), (0.55, 0.37, 0.38, -0.03),
            (0.72, 0.35, 0.36, -0.01), (0.86, 0.32, 0.34, 0.02), (0.95, 0.26, 0.3, 0.03), (1.0, 0.14, 0.18, 0.04)]
    for t, hw, hh, dz in prof:
        secs.append(((0, -L / 2 + L * t, zc + dz), hw * (1.12 if lie else 1.0), hh))
    loft(g, secs, 14, coat, frame=(Vector((0, 1, 0)), Vector((0, 0, 1))), expo=2.3,
         paint=spotted(coat, patch, g.seed) if patch != coat else None)
    # garrot et hanches marqués
    g.ico((0, -L / 2 + L * 0.2, zc + 0.36), 0.12, 2, coat, scale=(1.4, 1.6, 0.6), auto=False)
    for sx in (-1, 1):
        g.ico((sx * 0.22, L / 2 - 0.28, zc + 0.28), 0.1, 2, coat, scale=(1, 1.4, 0.8), auto=False)
    # encolure et tête
    sh = Vector((0, -L / 2 + 0.12, zc + 0.12))
    if pose == "graze":
        hb, hd_ = Vector((0, -L / 2 - 0.35, zc - 0.3)), Vector((0, -0.35, -0.94)).normalized()
    elif lie:
        hb, hd_ = Vector((0, -L / 2 - 0.25, zc + 0.3)), Vector((0, -0.9, -0.42)).normalized()
    else:
        hb, hd_ = Vector((0, -L / 2 - 0.28, zc + 0.36)), Vector((0, -0.82, -0.57)).normalized()
    loft(g, [(sh, 0.22, 0.27), (sh.lerp(hb, 0.5) + Vector((0, 0, 0.04)), 0.17, 0.22), (hb, 0.14, 0.17)], 12, coat, expo=2.2)
    up = Vector((0, 0, 1)).cross(hd_).cross(hd_) * -1
    hsecs = [(hb, 0.14, 0.16), (hb + hd_ * 0.12, 0.15, 0.16), (hb + hd_ * 0.3, 0.12, 0.12), (hb + hd_ * 0.44, 0.11, 0.1), (hb + hd_ * 0.5, 0.09, 0.08)]
    loft(g, hsecs, 12, patch if patch != coat else coat, frame=(hd_, up.normalized()), expo=2.5)
    nose = hb + hd_ * 0.5
    g.ico(nose - hd_ * 0.03, 0.095, 2, "skin_pink", scale=(1.15, 1.0, 0.85), auto=False)
    for sx in (-1, 1):
        g.octa(nose + Vector((sx * 0.04, 0, 0)) + hd_ * 0.06, 0.018, "black")
        g.octa(hb + hd_ * 0.18 + Vector((sx * 0.13, 0, 0.05)), 0.028, "black")      # yeux
        ear(g, hb + Vector((sx * 0.12, 0, 0.06)), Vector((sx, 0.2, 0.15)), 0.17, 0.1, coat, inner="skin_pink")
        horn(g, hb + Vector((sx * 0.08, 0.02, 0.14)), [(sx * 0.1, 0.0, 0.07), (sx * 0.17, -0.04, 0.17), (sx * 0.16, -0.07, 0.24)], 0.03)
    # pattes
    legs = [(sx, sy) for sx in (-1, 1) for sy in (-1, 1)]
    for sx, sy in legs:
        y = sy * L * 0.33
        hip = (sx * 0.2, y, zc - 0.12)
        if lie:
            leg(g, hip, (sx * 0.3, y - 0.15, 0.12), (sx * 0.28, y - 0.45, 0.08), (sx * 0.26, y - 0.6, 0.05), 0.075, coat)
        else:
            kz = 0.36 if sy < 0 else 0.42
            knee = (sx * 0.21, y + (0.02 if sy < 0 else 0.08), kz)
            hock = (sx * 0.21, y + (-0.01 if sy < 0 else 0.0), 0.16)
            leg(g, hip, knee, hock, (sx * 0.21, y, 0.0), 0.085 if sy > 0 else 0.078, coat if sy < 0 else coat)
    # queue à touffe
    tb = Vector((0, L / 2 - 0.02, zc + 0.3))
    tail = [tb, tb + Vector((0, 0.1, -0.15)), tb + Vector((0, 0.12, -0.45)), tb + Vector((0.02, 0.1, -0.68))] if not lie else \
        [tb, tb + Vector((0, 0.12, -0.12)), tb + Vector((0.12, 0.25, -0.25)), tb + Vector((0.25, 0.25, -0.3))]
    g.tube(tail, [0.035, 0.028, 0.022, 0.02], 6, coat)
    g.ico(tail[-1] + Vector((0, 0, -0.04)), 0.06, 1, "cow_black" if coat != "cow_black" else "fur_tan", scale=(1, 1, 1.8), auto=False)
    if not lie:                                       # mamelle
        g.ico((0, L / 2 - 0.45, zc - 0.36), 0.15, 2, "skin_pink", scale=(1.1, 1.2, 0.7), auto=False)
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.frustum((sx * 0.06, L / 2 - 0.45 + sy * 0.06, zc - 0.5), 0.015, 0.02, 0.06, 6, "skin_pink", auto=False)
    if bell:                                          # collier de cuir et cloche
        mid = sh.lerp(hb, 0.62)
        nax = (hb - sh).normalized()
        side = nax.cross(Vector((0, 0, 1))).normalized()
        upn = side.cross(nax)
        ring = [mid + side * math.cos(a) * 0.205 + upn * math.sin(a) * 0.245 for a in [2 * math.pi * k / 14 for k in range(15)]]
        g.tube(ring, [0.03] * 15, 6, "wood_red", cap_end=False)
        bp = mid - upn * 0.3
        g.tube([mid - upn * 0.24, bp], [0.012, 0.012], 4, "metal_dark")
        g.frustum((bp.x, bp.y, bp.z - 0.17), 0.085, 0.05, 0.17, 10, "brass", auto=False)
        g.frustum((bp.x, bp.y, bp.z - 0.012), 0.05, 0.03, 0.03, 10, "brass", auto=False)
        g.octa((bp.x, bp.y, bp.z - 0.2), 0.025, "metal_dark")


hd("cow_a")(lambda g: cow(g, "stand", "cow_brown", "cow_white"))
hd("cow_b")(lambda g: cow(g, "graze", "cow_white", "cow_black"))
hd("cow_lying")(lambda g: cow(g, "lie", "cow_brown", "cow_white", bell=False))


# ----------------------------------------------------------------- petit bétail
def small_quad(g, L, H, W, coat, head_c, leg_c, legs_frac, head_len, wool=False, beard=None, horns=None, ears=0.1,
               neck_up=0.62, tail=0.15):
    zc = H * legs_frac + (H - H * legs_frac) / 2
    hh = (H - H * legs_frac) / 2
    if wool:                                          # toison bosselée
        from alp_nature_hd import blob
        m = g.mark()
        blob(g, (0, 0, 0), 1.0, 2, coat, 1.0, g.seed, 0.1)
        for v in {v for f in g.since(m) for v in f.verts}:
            v.co = Vector((v.co.x * W * 0.62, v.co.y * L * 0.56, v.co.z * hh * 1.15 + zc))
        for k in range(16):                           # boucles de laine
            a, e = g.D(0, 6.28), g.D(-0.3, 1.0)
            d = Vector((math.cos(a) * math.cos(e) * W * 0.6, math.sin(a) * math.cos(e) * L * 0.54, math.sin(e) * hh * 1.1))
            g.ico(Vector((0, 0, zc)) + d, 0.09, 1, coat, auto=False)
    else:
        prof = [(0.0, 0.5, 0.6), (0.06, 0.78, 0.84), (0.18, 0.93, 0.95), (0.35, 1.0, 1.0), (0.55, 0.98, 0.97), (0.75, 0.94, 0.92),
                (0.9, 0.82, 0.82), (1.0, 0.5, 0.58)]
        loft(g, [((0, -L / 2 + L * t, zc), W / 2 * a, hh * b) for t, a, b in prof], 18, coat,
             frame=(Vector((0, 1, 0)), Vector((0, 0, 1))), expo=2.2)
    sh = Vector((0, -L / 2 + 0.06, zc + hh * 0.3))
    hb = Vector((0, -L / 2 - head_len * 0.35, zc + hh * 0.3 + H * (neck_up - 0.45)))
    loft(g, [(sh, W * 0.28, hh * 0.65), (sh.lerp(hb, 0.5), W * 0.24, hh * 0.58), (hb, W * 0.2, hh * 0.5)], 14, coat if not wool else head_c, expo=2.2)
    hd_ = Vector((0, -0.75, -0.66)).normalized()
    up = Vector((0, -0.66, 0.75))
    loft(g, [(hb, W * 0.2, hh * 0.52), (hb + hd_ * head_len * 0.25, W * 0.2, hh * 0.5), (hb + hd_ * head_len * 0.55, W * 0.16, hh * 0.4),
             (hb + hd_ * head_len * 0.85, W * 0.12, hh * 0.3), (hb + hd_ * head_len, W * 0.09, hh * 0.22)], 14,
         head_c, frame=(hd_, up), expo=2.4)
    for sx in (-1, 1):
        g.octa(hb + hd_ * head_len * 0.3 + Vector((sx * W * 0.17, 0, H * 0.04)), 0.02, "black")
        ear(g, hb + Vector((sx * W * 0.15, 0.02, H * 0.05)), Vector((sx, 0.3, 0.1 if not wool else -0.2)), ears, ears * 0.5, head_c)
        if horns:
            horn(g, hb + Vector((sx * W * 0.08, 0.03, H * 0.07)), [(sx * p[0], p[1], p[2]) for p in horns["pts"]], horns["r"],
                 rings=horns.get("rings", False))
    g.octa(hb + hd_ * head_len, 0.02, "black")
    if beard:
        g.tube([hb + hd_ * head_len * 0.8 - up * 0.04, hb + hd_ * head_len * 0.75 - up * 0.04 + Vector((0, 0, -beard))],
               [0.025, 0.0], 5, head_c)
    for sx in (-1, 1):
        for sy in (-1, 1):
            y = sy * L * 0.34
            hip = (sx * W * 0.3, y, zc - hh * 0.4)
            leg(g, hip, (sx * W * 0.3, y + 0.03 * sy, H * legs_frac * 0.55), (sx * W * 0.3, y - 0.01, H * legs_frac * 0.2),
                (sx * W * 0.3, y, 0.0), W * 0.1, leg_c, "black", 10)
    if tail:
        tb = Vector((0, L / 2, zc + hh * 0.5))
        g.tube([tb, tb + Vector((0, tail * 0.4, tail * 0.3)), tb + Vector((0, tail * 0.7, tail * 0.1))], [0.03, 0.022, 0.0], 6, coat)
    return hb, hd_


@hd("sheep")
def sheep(g):
    small_quad(g, 1.0, 0.75, 0.5, "cow_white", "cow_black", "cow_black", 0.4, 0.26, wool=True, ears=0.09, neck_up=0.55, tail=0.0)


@hd("goat")
def goat(g):
    small_quad(g, 1.0, 0.8, 0.36, "white", "white", "white", 0.52, 0.3, beard=0.16, ears=0.11,
               horns=dict(r=0.022, pts=[(0.03, 0.06, 0.1), (0.05, 0.16, 0.2), (0.07, 0.28, 0.22)]))


@hd("ibex")
def ibex(g):
    pts = []
    for k in range(1, 9):                             # grandes cornes en arc, annelées
        a = math.pi * 0.95 * k / 8
        pts.append((0.02 + 0.07 * k / 8, 0.33 * (1 - math.cos(a)) * 0.9, 0.42 * math.sin(a) * 0.95))
    small_quad(g, 1.3, 0.95, 0.46, "fur_tan", "fur_tan", "bark", 0.46, 0.34, beard=0.14, ears=0.1,
               horns=dict(r=0.045, pts=pts, rings=True))


@hd("dog")
def dog(g):
    hb, hd_ = small_quad(g, 0.95, 0.68, 0.34, "cow_black", "cow_black", "fur_tan", 0.45, 0.28, ears=0.12, neck_up=0.55, tail=0.3)
    g.ico(hb + hd_ * 0.2 + Vector((0, 0, -0.04)), 0.07, 2, "white", scale=(1, 1.2, 0.8), auto=False)      # liste blanche
    g.ico((0, -0.47, 0.42), 0.11, 2, "white", scale=(1.2, 0.8, 1.3), auto=False)                         # poitrail
    mid = hb + Vector((0, 0.1, -0.06))
    g.tube([mid + Vector((math.cos(a) * 0.1, 0, math.sin(a) * 0.1)) for a in [2 * math.pi * k / 10 for k in range(11)]],
           [0.018] * 11, 5, "red", cap_end=False)


@hd("marmot")
def marmot(g):
    """Marmotte dressée en sentinelle : corps en poire, pattes avant repliées, queue courte."""
    secs = [((0, 0.03, z), r * 1.0, r * 0.95) for z, r in ((0.0, 0.12), (0.08, 0.19), (0.2, 0.21), (0.32, 0.18), (0.4, 0.13))]
    loft(g, secs, 12, "fur_tan", frame=(Vector((0, 0, 1)), Vector((0, -1, 0))), expo=2.0)
    hb = Vector((0, -0.02, 0.45))
    loft(g, [(hb + Vector((0, 0, -0.04)), 0.1, 0.1), (hb + Vector((0, -0.06, 0.02)), 0.11, 0.1), (hb + Vector((0, -0.14, 0.0)), 0.07, 0.06),
             (hb + Vector((0, -0.17, -0.01)), 0.04, 0.035)], 10, "fur_tan", expo=2.0)
    g.box(hb + Vector((0, -0.165, -0.035)), (0.05, 0.03, 0.03), "bark")
    g.octa(hb + Vector((0, -0.18, 0.0)), 0.015, "black")
    for sx in (-1, 1):
        g.octa(hb + Vector((sx * 0.07, -0.09, 0.04)), 0.014, "black")
        g.ico(hb + Vector((sx * 0.08, 0.0, 0.08)), 0.03, 1, "bark", scale=(0.6, 1, 1), auto=False)
        g.tube([(sx * 0.1, -0.12, 0.3), (sx * 0.08, -0.19, 0.24), (sx * 0.05, -0.2, 0.2)], [0.03, 0.025, 0.02], 6, "fur_tan")
        g.ico((sx * 0.05, -0.21, 0.19), 0.022, 1, "bark", auto=False)
        g.tube([(sx * 0.1, 0.0, 0.05), (sx * 0.11, -0.1, 0.03)], [0.045, 0.035], 6, "fur_tan")
        g.box((sx * 0.11, -0.13, 0.015), (0.07, 0.07, 0.03), "bark")
    g.tube([(0, 0.18, 0.06), (0, 0.32, 0.04), (0.03, 0.42, 0.03)], [0.045, 0.035, 0.0], 6, "bark")
