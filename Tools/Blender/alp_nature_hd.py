"""Pack alpin v2 : nature détaillée. Remplace les constructeurs des fiches d'alp_nature / alp_nature2 (mêmes ids,
mêmes fichiers, mêmes gabarits). Trois niveaux par objet (g.level) : 0 détaillé, 1 moyen, 2 lointain, bâtis sur la
même structure (étages, branches, boules) pour que les silhouettes se superposent."""
import math, random
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import lv


def override(iid, build, **kw):
    if iid not in A.ITEMS:                            # id repris par une autre session
        return
    A.ITEMS[iid]["build"] = build
    A.ITEMS[iid].update(kw)


def pol(a, r, z=0.0):
    return Vector((math.cos(a) * r, math.sin(a) * r, z))


def sph(az, el):
    return Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))


# ----------------------------------------------------------------- tubes et boules
def tube(g, pts, radii, n, color, cap_start=False, cap_end=True):
    """Tube à section ronde le long d'une polyligne ; rayon nul en bout = pointe."""
    pts = [Vector(p) for p in pts]
    bm = g.bm
    rings = []
    px = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if px is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            x = t.cross(ref).normalized()
        else:
            x = (px - t * px.dot(t))
            x = x.normalized() if x.length > 1e-5 else t.orthogonal().normalized()
        y = t.cross(x)
        px = x
        if radii[i] <= 1e-4:
            rings.append([bm.verts.new(p)])
        else:
            rings.append([bm.verts.new(p + (x * math.cos(6.2832 * k / n) + y * math.sin(6.2832 * k / n)) * radii[i])
                          for k in range(n)])
    faces = []
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for k in range(n):
            j = (k + 1) % n
            if len(b) == 1:
                faces.append(bm.faces.new((a[k], a[j], b[0])))
            elif len(a) == 1:
                faces.append(bm.faces.new((a[0], b[j], b[k])))
            else:
                faces.append(bm.faces.new((a[k], a[j], b[j], b[k])))
    if cap_start and len(rings[0]) > 2:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if cap_end and len(rings[-1]) > 2:
        faces.append(bm.faces.new(rings[-1]))
    return g.paint(faces, color)


CARTOON = True      # style cartoon (2026-11-15) : boules plus lisses, calotte éclairée nette au lieu de facettes éparses


def blob(g, c, r, sub, color, squash=1.0, seed=0, amp=0.16, top=None):
    """Boule de feuillage bosselée (icosphère déformée par un bruit stable). top : couleur des facettes du dessus."""
    if CARTOON:
        amp *= 0.45
        if sub < 3 and r > 0.6:
            sub += 1
    rr = random.Random(seed)
    k1 = Vector((rr.uniform(-1, 1), rr.uniform(-1, 1), rr.uniform(-1, 1))) * 3.0
    k2 = Vector((rr.uniform(-1, 1), rr.uniform(-1, 1), rr.uniform(-1, 1))) * 5.5
    p1, p2 = rr.uniform(0, 6.28), rr.uniform(0, 6.28)
    f = g.ico((0, 0, 0), 1.0, sub, color, auto=False)
    c = Vector(c)
    for v in {v for q in f for v in q.verts}:
        d = v.co.normalized()
        k = 1.0 + amp * (0.6 * math.sin(d.dot(k1) + p1) + 0.4 * math.sin(d.dot(k2) + p2)) + rr.uniform(-0.04, 0.04) * (0.2 if CARTOON else 1.0)
        v.co = Vector((d.x * r * k, d.y * r * k, d.z * r * k * squash)) + c
    if top:
        ci = A.CI[top]
        for q in f:
            q.normal_update()
            if (q.normal.z > 0.62) if CARTOON else (q.normal.z > 0.55 and rr.random() < 0.6):
                q[g.col] = ci
    return f


# ----------------------------------------------------------------- conifères
def conifer(g, H, width, tiers, color="spruce", tip="spruce_tip", trunk=0.3, bare=0.12, droop=0.16, taper=0.8,
            overlap=2.0, sparse=False, bark="bark"):
    """Épicéa / mélèze : étages de branches tombantes à bord dentelé, tronc à empattement, branches mortes basses.
    sparse : étages aérés (mélèze), branches visibles."""
    L = g.level
    T = tiers
    z0, z1 = H * bare, H * 0.80

    def tier(k):
        t = k / max(T - 1, 1)
        zb = z0 + (z1 - z0) * t
        r = (width * (1.0 - taper * t) + 0.12) * (0.93 + 0.14 * g.h(k, 1))
        hh = (H - zb) if k == T - 1 else min(H - zb, (H - z0) / T * overlap * (1.0 + 0.6 * t))
        return t, zb, r, hh, g.h(k, 2) * 6.2832

    bm = g.bm
    # ---- tronc
    if L == 0:
        tube(g, [(0, 0, -0.5), (0, 0, 0.15), (0, 0, 0.9), (0, 0, H * 0.5), (0, 0, H * 0.9)],
             [trunk * 1.7, trunk * 1.25, trunk, trunk * 0.55, 0.03], 9, bark)
        for k in range(5):                           # empattement des racines
            a = 6.2832 * k / 5 + g.h(k, 30) * 0.8
            tube(g, [pol(a, trunk * 0.5, 0.55), pol(a, trunk * 1.7, 0.08), pol(a, trunk * 2.9, -0.35)],
                 [trunk * 0.42, trunk * 0.3, trunk * 0.1], 5, bark, cap_end=False)
        nd = 8 if not sparse else 5                  # branches mortes sous la couronne
        for k in range(nd):
            a = g.h(k, 40) * 6.2832
            z = 1.2 + (z0 * 1.05 - 1.2) * (k + 0.5) / nd
            Lb = 0.5 + 1.1 * g.h(k, 41)
            p1 = pol(a, Lb * 0.6, z - 0.08 * Lb)
            p2 = pol(a + 0.25, Lb, z - 0.3 * Lb)
            tube(g, [pol(a, trunk * 0.5, z), p1, p2], [0.045, 0.03, 0.008], 4, "dead", cap_end=False)
            if k % 2 == 0:
                tube(g, [p1, p1 + pol(a - 0.9, Lb * 0.35, -0.08)], [0.02, 0.005], 3, "dead", cap_end=False)
    elif L == 1:
        tube(g, [(0, 0, -0.5), (0, 0, 0.8), (0, 0, H * 0.6)], [trunk * 1.4, trunk, trunk * 0.4], 5, bark)
    else:
        tube(g, [(0, 0, -0.5), (0, 0, H * 0.5)], [trunk * 1.2, trunk * 0.4], 3, bark, cap_end=False)

    # ---- niveau lointain : trois cônes sur le même profil
    if L == 2:
        for (ta, tb) in ((0.0, 0.5), (0.36, 0.8), (0.7, 1.0)):
            ka = round(ta * (T - 1))
            _, zb, r, hh, rot = tier(ka)
            top = z0 + (H - z0) * tb if tb < 1.0 else H
            n = 5
            rim = [bm.verts.new(pol(rot + 6.2832 * i / n, r * 0.9, zb - droop * r)) for i in range(n)]
            apex = bm.verts.new((0, 0, top))
            fs = [bm.faces.new((rim[i], rim[(i + 1) % n], apex)) for i in range(n)]
            fs.append(bm.faces.new(list(reversed(rim))))
            g.paint(fs, color)
        return

    notch = 0.6 if not sparse else 0.36
    for k in range(T):
        t, zb, r, hh, rot = tier(k)
        if L == 1:
            n = 7
            rim = [bm.verts.new(pol(rot + 6.2832 * i / n, r * (0.86 + 0.12 * g.h(k, i, 3)), zb - droop * r)) for i in range(n)]
            apex = bm.verts.new((0, 0, zb + hh))
            fs = [bm.faces.new((rim[i], rim[(i + 1) % n], apex)) for i in range(n)]
            if k < 4:
                inner = [bm.verts.new(pol(rot + 6.2832 * (i + 0.5) / n, r * 0.28, zb + hh * 0.1)) for i in range(n)]
                for i in range(n):
                    j = (i + 1) % n
                    fs.append(bm.faces.new((rim[j], rim[i], inner[i])))
                    fs.append(bm.faces.new((rim[i], inner[i - 1], inner[i])))
                fs.append(bm.faces.new(list(reversed(inner))))
            else:
                fs.append(bm.faces.new(list(reversed(rim))))
            g.paint(fs, color)
            continue
        # ---- niveau détaillé : bord dentelé (pointes de branches et creux), dessus bombé, dessous concave
        n = max(7, round(15 - 7 * t))
        m = 2 * n
        tips = []
        rim, mid = [], []
        for i in range(m):
            a = rot + 6.2832 * i / m + (g.h(k, i, 4) - 0.5) * 0.12
            if i % 2 == 0:
                rr = r * (0.84 + 0.34 * g.h(k, i, 5))
                z = zb - droop * rr * (0.9 + 0.7 * g.h(k, i, 6))
                tips.append((a, rr, z))
            else:
                rr = r * (notch + 0.14 * g.h(k, i, 5))
                z = zb - droop * r * 0.25
            rim.append(bm.verts.new(pol(a, rr, z)))
            mid.append(bm.verts.new(pol(a, rr * 0.52, zb + hh * (0.3 if not sparse else 0.2) + (0.06 * r if i % 2 == 0 else -0.04 * r))))
        apex = bm.verts.new((g.h(k, 7) * 0.08 - 0.04, g.h(k, 8) * 0.08 - 0.04, zb + hh))
        inner = [bm.verts.new(pol(rot + 6.2832 * (2 * j) / m, r * 0.22, zb + hh * 0.08)) for j in range(n)]
        top_f, tip_f, under = [], [], []
        for i in range(m):
            j = (i + 1) % m
            top_f.append(bm.faces.new((mid[i], mid[j], apex)))
            q = bm.faces.new((rim[i], rim[j], mid[j], mid[i]))
            lobe = (i + 1) // 2 % n
            (tip_f if g.h(k, lobe, 9) < 0.3 else top_f).append(q)
        for j in range(n):
            j1 = (j + 1) % n
            under.append(bm.faces.new((rim[2 * j + 1], rim[2 * j], inner[j])))
            under.append(bm.faces.new((rim[(2 * j + 2) % m], rim[2 * j + 1], inner[j1])))
            under.append(bm.faces.new((rim[2 * j + 1], inner[j], inner[j1])))
        under.append(bm.faces.new(list(reversed(inner))))
        g.paint(top_f + under, color)
        g.paint(tip_f, tip)
        # rameaux qui dépassent du bord
        for (a, rr, z) in tips:
            if g.h(k, a, 10) < (0.5 if not sparse else 0.75):
                w = 0.1 + 0.05 * r
                b0, b1 = bm.verts.new(pol(a - w / rr, rr * 0.92, z + 0.04)), bm.verts.new(pol(a + w / rr, rr * 0.92, z + 0.04))
                b2 = bm.verts.new(pol(a, rr * 0.86, z - 0.16 - 0.06 * r))
                tp = bm.verts.new(pol(a, rr * (1.14 + 0.1 * g.h(k, a, 11)), z - 0.2 - 0.14 * r))
                g.paint([bm.faces.new((b0, b1, tp)), bm.faces.new((b1, b2, tp)), bm.faces.new((b2, b0, tp))], tip)
        if sparse:                                   # mélèze : branches visibles sous chaque étage
            for q, (a, rr, z) in enumerate(tips):
                if q % 2 == 0:
                    tube(g, [(0, 0, zb + hh * 0.12), pol(a, rr * 0.5, zb - 0.02), pol(a, rr * 0.86, z + 0.02)],
                         [0.04, 0.028, 0.01], 4, bark, cap_end=False)


def larch(g, H, width, tiers):
    conifer(g, H, width, tiers, color="larch", tip="larch_tip", trunk=0.26, bare=0.2, droop=0.07, taper=0.8,
            overlap=1.3, sparse=True, bark="bark_grey")


override("spruce_a", lambda g: conifer(g, 14.0, 2.5, 12))
override("spruce_b", lambda g: conifer(g, 18.0, 2.9, 14, trunk=0.36))
override("spruce_c", lambda g: conifer(g, 22.0, 3.3, 16, trunk=0.42, bare=0.15))
override("spruce_young", lambda g: conifer(g, 5.5, 1.5, 8, trunk=0.14, bare=0.08, overlap=1.8))
override("larch_a", lambda g: larch(g, 15.0, 2.6, 10))
override("larch_b", lambda g: larch(g, 19.0, 3.0, 12))


def dead_spruce(g):
    H = 14.0
    L = g.level
    tube(g, [(0, 0, -0.5), (0, 0, 0.2), (0, 0, 1.0), (0.1, 0.05, H * 0.6), (0.0, 0.15, H)],
         [0.5, 0.36, 0.3, 0.17, 0.05] if L == 0 else [0.42, 0.34, 0.3, 0.17, 0.05], lv(g, 9, 5, 3), "dead")
    if L == 0:
        for k in range(4):
            a = 6.2832 * k / 4 + 0.4
            tube(g, [pol(a, 0.15, 0.5), pol(a, 0.6, 0.05), pol(a, 1.0, -0.3)], [0.13, 0.09, 0.03], 5, "dead", cap_end=False)
    nb = lv(g, 24, 14, 0)
    for k in range(nb):
        z = H * (0.22 + 0.72 * (k + 0.5) / nb)
        a = g.h(k, 1) * 6.2832
        Lb = (1.0 - z / H) * 2.8 + 0.5
        p1 = pol(a, Lb * 0.55, z - Lb * 0.12)
        p2 = pol(a + (g.h(k, 2) - 0.5) * 0.5, Lb, z - Lb * (0.2 + 0.3 * g.h(k, 3)))
        if L == 0:
            tube(g, [pol(a, 0.08, z), p1, p2], [0.06, 0.04, 0.01], 5, "dead", cap_end=False)
            for s in (-1, 1):
                if g.h(k, s, 4) < 0.7:
                    tube(g, [p1, p1 + pol(a + s * 0.9, Lb * 0.4, -0.1 - 0.1 * g.h(k, s, 5))], [0.025, 0.006], 3, "dead",
                         cap_end=False)
        else:
            tube(g, [pol(a, 0.05, z), p2], [0.055, 0.012], 3, "dead", cap_end=False)


override("dead_spruce", dead_spruce)


# ----------------------------------------------------------------- feuillus (charpente + houppier en boules)
def leafy(g, H, trunk_h, trunk_r, spread, colors, bark="bark", n_scaf=5, blob_r=0.17, squash=0.85, elev=(35, 60),
          fill=3, along=False, top=None, marks=None, lean=0.0, amp=0.16, sec=2, sat=5):
    """Feuillu générique. La structure (branches, boules) est tirée une fois, identique aux trois niveaux."""
    L = g.level
    rs = random.Random(g.seed * 31 + 7)
    base_top = Vector((rs.uniform(-1, 1) * trunk_r, rs.uniform(-1, 1) * trunk_r, trunk_h))
    scaff, secs, blobs = [], [], []
    R = blob_r * H
    for k in range(n_scaf):
        az = 6.2832 * (k + rs.uniform(-0.25, 0.25)) / n_scaf
        el = math.radians(rs.uniform(*elev))
        Lb = spread * H * rs.uniform(0.78, 1.0)
        start = base_top * rs.uniform(0.8, 1.0)
        start.x, start.y = base_top.x * 0.5, base_top.y * 0.5
        mid = start + sph(az, el * 0.75) * Lb * 0.5
        end = mid + sph(az + rs.uniform(-0.4, 0.4), min(el * 1.3, 1.4)) * Lb * 0.5
        scaff.append((start, mid, end))
        blobs.append((end, R * rs.uniform(0.9, 1.1), 0))
        if along:
            blobs.append((mid + Vector((0, 0, R * 0.2)), R * rs.uniform(0.7, 0.85), 1))
        for s in range(sec):
            az2 = az + (1 if s == 0 else -1) * rs.uniform(0.6, 1.1)
            e2 = mid.lerp(end, 0.4) + sph(az2, math.radians(rs.uniform(15, 55))) * Lb * rs.uniform(0.4, 0.55)
            secs.append((mid.lerp(end, 0.4), e2))
            blobs.append((e2, R * rs.uniform(0.68, 0.85), 1))
    lead_r = R * 1.08
    lead_end = Vector((base_top.x * 0.3, base_top.y * 0.3, H - lead_r * squash * 0.95))
    blobs.append((lead_end, lead_r, 0))
    for k in range(fill):                            # remplissage du cœur du houppier
        a = 6.2832 * k / max(fill, 1) + rs.uniform(-0.4, 0.4)
        zc = trunk_h + (H - trunk_h) * rs.uniform(0.45, 0.72)
        blobs.append((pol(a, spread * H * rs.uniform(0.25, 0.45), zc), R * rs.uniform(0.8, 1.0), 0))
    M = Matrix.Rotation(lean, 4, 'X') @ Matrix.Rotation(g.seed * 1.3, 4, 'Z') if lean else None
    mk = g.mark()
    # ---- bois
    if L == 0:
        bend = Vector((base_top.x * 0.6, base_top.y * 0.6, trunk_h * 0.55))
        tube(g, [(0, 0, -0.5), (0, 0, 0.12), (0, 0, 0.8), bend, base_top, lead_end],
             [trunk_r * 1.75, trunk_r * 1.3, trunk_r, trunk_r * 0.9, trunk_r * 0.75, trunk_r * 0.2], 9, bark, cap_end=False)
        for k in range(5):
            a = 6.2832 * k / 5 + rs.uniform(-0.3, 0.3)
            tube(g, [pol(a, trunk_r * 0.5, 0.45), pol(a, trunk_r * 1.8, 0.05), pol(a, trunk_r * 3.0, -0.35)],
                 [trunk_r * 0.4, trunk_r * 0.28, trunk_r * 0.08], 5, bark, cap_end=False)
        for (s, m, e) in scaff:
            tube(g, [s, m, e], [trunk_r * 0.5, trunk_r * 0.34, trunk_r * 0.14], 6, bark, cap_end=False)
        for (s, e) in secs:
            tube(g, [s, s.lerp(e, 0.5) + Vector((0, 0, 0.06 * (e - s).length)), e], [trunk_r * 0.24, trunk_r * 0.16, trunk_r * 0.06],
                 5, bark, cap_end=False)
        if marks:                                    # écorce marquée (bouleau) : traits sombres plaqués sur le tronc
            for k in range(18):
                z = 0.3 + (trunk_h * 1.05) * (k + rs.random()) / 18
                a = rs.uniform(0, 6.28)
                rr = trunk_r * (1.0 if z > 0.8 else 1.25) * (1.0 - 0.22 * min(1.0, z / max(trunk_h, 0.1))) + 0.008
                w, hh = rs.uniform(0.25, 0.6), rs.uniform(0.035, 0.08)
                off = Vector((bend.x, bend.y, 0)) * min(1.0, z / (trunk_h * 0.55)) if z < trunk_h * 0.55 else Vector((base_top.x, base_top.y, 0)) * 0.8
                p = [pol(a - w, rr, z - hh) + off, pol(a + w, rr, z - hh * 0.4) + off, pol(a + w, rr, z + hh) + off,
                     pol(a - w, rr, z + hh * 0.4) + off]
                g.face(p, marks)
    elif L == 1:
        tube(g, [(0, 0, -0.5), (0, 0, 0.6), base_top, lead_end], [trunk_r * 1.4, trunk_r, trunk_r * 0.75, trunk_r * 0.2], 5,
             bark, cap_end=False)
        for (s, m, e) in scaff:
            tube(g, [s, e], [trunk_r * 0.45, trunk_r * 0.14], 4, bark, cap_end=False)
    else:
        tube(g, [(0, 0, -0.5), base_top], [trunk_r * 1.2, trunk_r * 0.7], 3, bark, cap_end=False)
    # ---- houppier
    if L == 0:
        cc = Vector((0, 0, sum(c.z for c, r, _ in blobs) / len(blobs)))
        for i, (c, r, kind) in enumerate(blobs):
            col = colors[(i * 7 + g.seed) % len(colors)]
            blob(g, c, r, 2, col, squash, g.seed * 100 + i, amp, top=top if i % 3 == 0 else None)
            # petites touffes sur le pourtour : cassent l'effet « boule », donnent le grain du feuillage
            out = (c - cc)
            out = out.normalized() if out.length > 1e-3 else Vector((0, 0, 1))
            for j in range((sat if kind == 0 else max(2, sat - 2)) // (2 if CARTOON else 1)):
                d = Vector((rs.uniform(-1, 1), rs.uniform(-1, 1), rs.uniform(-0.6, 1))) + out * 0.9
                d.normalize()
                pc = c + Vector((d.x * r, d.y * r, d.z * r * squash)) * 0.86
                blob(g, pc, r * rs.uniform(0.3, 0.44), 2 if CARTOON else 1, colors[(i * 7 + j + 1 + g.seed) % len(colors)], squash,
                     g.seed * 1000 + i * 10 + j, amp * 1.3)
    elif L == 1:
        for i, (c, r, kind) in enumerate(blobs):
            if kind == 0:
                blob(g, c, r * 1.22, 1, colors[(i * 7 + g.seed) % len(colors)], squash, g.seed * 100 + i, amp * 0.7)
    else:
        lo = Vector((min(c.x - r for c, r, _ in blobs), min(c.y - r for c, r, _ in blobs), min(c.z - r * squash for c, r, _ in blobs)))
        hi = Vector((max(c.x + r for c, r, _ in blobs), max(c.y + r for c, r, _ in blobs), max(c.z + r * squash for c, r, _ in blobs)))
        f = g.ico((0, 0, 0), 1.0, 1, colors[0], auto=False)
        ce, ex = (lo + hi) / 2, (hi - lo) / 2
        for v in {v for q in f for v in q.verts}:
            v.co = Vector((v.co.x * ex.x * 0.93, v.co.y * ex.y * 0.93, v.co.z * ex.z)) + ce
    if M:
        g.transform(g.since(mk), M)


GREENS = ("leaf", "leaf_light", "leaf_dark")
override("deciduous_a", lambda g: leafy(g, 10.0, 3.2, 0.28, 0.34, GREENS, top="leaf_light"))
override("deciduous_b", lambda g: leafy(g, 14.0, 4.5, 0.36, 0.24, ("leaf_dark", "leaf", "leaf_dark"), n_scaf=5, blob_r=0.14,
                                        squash=1.15, elev=(50, 72), fill=4, along=True, top="leaf"))
override("deciduous_c", lambda g: leafy(g, 8.0, 2.4, 0.3, 0.46, ("leaf_light", "leaf", "leaf_yellow"), n_scaf=6,
                                        blob_r=0.2, squash=0.75, elev=(22, 45), top="leaf_light"))
override("birch_a", lambda g: leafy(g, 9.0, 4.0, 0.13, 0.2, ("leaf_light", "leaf_yellow", "leaf_light"), bark="birch_bark",
                                    n_scaf=6, blob_r=0.11, squash=1.3, elev=(45, 75), fill=4, along=True, marks="black",
                                    amp=0.2, sat=3))
override("birch_b", lambda g: leafy(g, 12.0, 5.2, 0.16, 0.19, ("leaf_light", "leaf_yellow", "leaf"), bark="birch_bark",
                                    n_scaf=7, blob_r=0.1, squash=1.3, elev=(45, 75), fill=5, along=True, marks="black",
                                    amp=0.2, sat=3))


def swiss_pine(g, H, width, tiers):
    """Arolle : conifère trapu au houppier dense et ovoïde, tronc épais, souvent penché."""
    mk = g.mark()
    conifer(g, H, width, tiers, color="arolla", tip="arolla_tip", trunk=0.36, bare=0.2, droop=0.12, taper=0.62,
            overlap=2.3, bark="bark_grey")
    g.transform(g.since(mk), Matrix.Rotation(g.seed * 1.3, 4, 'Z') @ Matrix.Rotation(0.04 + (g.seed % 7) * 0.012, 4, 'X'))


override("swisspine_a", lambda g: swiss_pine(g, 9.0, 2.5, 9))
override("swisspine_b", lambda g: swiss_pine(g, 12.0, 2.9, 11))


def poplar(g):
    """Peuplier d'Italie : colonne de feuillage étroite, branches dressées."""
    H = 19.0
    L = g.level
    rs = random.Random(g.seed + 5)
    tube(g, [(0, 0, -0.5), (0, 0, 0.2), (0, 0, 1.0), (0, 0, 4.0), (0, 0, H * 0.9)], [0.55, 0.38, 0.3, 0.24, 0.04],
         lv(g, 9, 5, 3), "bark", cap_end=False)
    n = 13
    cols = ("leaf", "leaf_dark", "leaf")
    if L == 2:
        f = g.ico((0, 0, 0), 1.0, 1, "leaf", auto=False)
        for v in {v for q in f for v in q.verts}:
            v.co = Vector((v.co.x * 1.75, v.co.y * 1.75, v.co.z * 8.0 + 10.8))
        return
    for k in range(n):
        t = k / (n - 1)
        z = 3.6 + (H - 5.2) * t
        r = 1.75 * math.sin(math.pi * (0.16 + 0.8 * t)) ** 0.7
        c = Vector((rs.uniform(-0.25, 0.25), rs.uniform(-0.25, 0.25), z))
        if L == 0:
            blob(g, c, r, 2, cols[k % 3], 1.55, g.seed * 50 + k, 0.2, top="leaf_light" if k % 2 else None)
            a = rs.uniform(0, 6.28)
            blob(g, c + pol(a, r * 0.75, -r * 0.4), r * 0.55, 2 if k % 2 else 1, cols[(k + 1) % 3], 1.7, g.seed * 50 + 100 + k, 0.2)
            tube(g, [(0, 0, z - r * 1.4), c + pol(a, r * 0.6, -r * 0.3)], [0.07, 0.02], 4, "bark", cap_end=False)
        elif k % 2 == 0:
            blob(g, c, r * 1.1, 1, cols[k % 3], 1.9, g.seed * 50 + k, 0.12)


override("poplar", poplar)


def willow(g):
    """Saule pleureur : charpente étalée, dôme de feuillage, rideaux de rameaux retombants."""
    L = g.level
    rs = random.Random(g.seed + 9)
    tube(g, [(0, 0, -0.5), (0, 0, 0.15), (0.05, 0, 1.2), (0.15, 0.1, 3.0)], [0.75, 0.52, 0.42, 0.34], lv(g, 9, 5, 3), "bark")
    n_s = 6
    ends = []
    for k in range(n_s):
        a = 6.2832 * k / n_s + rs.uniform(-0.2, 0.2)
        m = pol(a, 1.5, 4.6)
        e = pol(a + rs.uniform(-0.2, 0.2), 2.9, 5.3)
        ends.append((a, e))
        if L < 2:
            tube(g, [(0.15, 0.1, 2.8), m, e], [0.2, 0.13, 0.05], lv(g, 6, 4), "bark", cap_end=False)
    if L == 2:
        f = g.ico((0, 0, 0), 1.0, 1, "leaf_light", auto=False)
        for v in {v for q in f for v in q.verts}:
            v.co = Vector((v.co.x * 3.9, v.co.y * 3.9, v.co.z * 2.6 + 4.4))
        return
    blob(g, (0, 0, 5.9), 2.7, lv(g, 3, 1), "leaf_light", 0.62, g.seed, 0.14, top="leaf_yellow" if L == 0 else None)
    for k, (a, e) in enumerate(ends):
        blob(g, e + Vector((0, 0, 0.1)), 1.45, lv(g, 2, 1), "leaf" if k % 2 else "leaf_light", 0.7, g.seed + 10 + k, 0.16)
    # rideaux : rubans effilés qui pendent du pourtour
    ns = lv(g, 46, 14)
    for k in range(ns):
        a = 6.2832 * k / ns + rs.uniform(-0.06, 0.06)
        r0 = rs.uniform(2.9, 3.9)
        top = pol(a, r0, 5.0 + rs.uniform(-0.3, 0.3))
        drop = rs.uniform(2.6, 4.3)
        w = rs.uniform(0.5, 0.9) if L == 0 else 1.6
        g.blade(top, (math.cos(a) * 0.12, math.sin(a) * 0.12, -1.0), drop, w, "leaf" if k % 3 else "leaf_light",
                bend=rs.uniform(-0.05, 0.1), segs=lv(g, 3, 1))


override("willow", willow)


# ----------------------------------------------------------------- buissons
def bush_rhodo(g):
    L = g.level
    spots = [((0, 0, 0.45), 0.62), ((0.6, 0.2, 0.36), 0.46), ((-0.55, 0.28, 0.36), 0.46), ((0.1, -0.55, 0.34), 0.44),
             ((-0.2, 0.6, 0.3), 0.38), ((0.5, -0.45, 0.28), 0.34)]
    use = spots if L == 0 else spots[:4] if L == 1 else [((0, 0, 0.42), 0.95)]
    rs = random.Random(g.seed)
    for k, (c, r) in enumerate(use):
        blob(g, c, r, lv(g, 2, 1, 1), "leaf_dark" if k % 2 == 0 else "leaf", 0.78, g.seed + k, 0.14)
        for j in range(lv(g, 7, 3, 0)):
            a, e = rs.uniform(0, 6.28), rs.uniform(0.25, 1.35)
            d = sph(a, e)
            d.z *= 0.78
            g.octa(Vector(c) + d * r * 1.0, lv(g, 0.085, 0.11), "rhodo" if j % 4 else "flower_pink", scale=(1, 1, 0.75))
    if L == 2:
        blob(g, (0, 0, 0.75), 0.6, 1, "rhodo", 0.5, g.seed + 9, 0.1)


override("bush_rhodo", bush_rhodo)


def juniper(g):
    L = g.level
    rs = random.Random(g.seed)
    for k in range(lv(g, 7, 5, 1)):
        a = 6.2832 * k / 6 + rs.uniform(-0.3, 0.3)
        d = 0.0 if k == 0 else rs.uniform(0.5, 0.95)
        r = rs.uniform(0.45, 0.7) if L < 2 else 1.15
        c = pol(a, d, r * 0.3)
        blob(g, c, r, lv(g, 2, 1, 1), ("spruce", "arolla", "arolla_tip")[k % 3], 0.5, g.seed + k, 0.2)
        if L == 0:
            for j in range(5):                       # pousses dressées
                aa = rs.uniform(0, 6.28)
                p = c + pol(aa, r * rs.uniform(0.2, 0.8), r * 0.3)
                g.frustum(p, 0.07, 0.0, rs.uniform(0.25, 0.45), 3, "arolla_tip", rot=aa, cap0=False)


override("juniper", juniper)


# ----------------------------------------------------------------- rochers
def rock(g, c, size, color="rock", seed=0, sub=2, block=0.7, strata=0.0, rot=0.0, crack=0, moss=None,
         moss_p=0.5):
    """Bloc rocheux facetté : icosphère rendue anguleuse, bruitée, avec strates (nombre de bancs), fissures sombres
    (nombre) et mousse sur les facettes du dessus."""
    rr = random.Random(seed)
    k1 = Vector((rr.uniform(-1, 1), rr.uniform(-1, 1), rr.uniform(-1, 1))) * 3.1
    k2 = Vector((rr.uniform(-1, 1), rr.uniform(-1, 1), rr.uniform(-1, 1))) * 5.7
    k3 = Vector((rr.uniform(-1, 1), rr.uniform(-1, 1), rr.uniform(-1, 1))) * 11.0
    p1, p2, p3 = rr.uniform(0, 6.28), rr.uniform(0, 6.28), rr.uniform(0, 6.28)
    cracks = []
    for k in range(int(crack)):
        ca = rr.uniform(0, 6.28)
        cracks.append((Vector((math.cos(ca), math.sin(ca), rr.uniform(-0.3, 0.3))).normalized(), rr.uniform(-0.45, 0.45)))
    f = g.ico((0, 0, 0), 1.0, sub, color, auto=False)
    wide = {1: 0.2, 2: 0.12, 3: 0.075, 4: 0.045}.get(sub, 0.08)
    groove = set()
    for v in {v for q in f for v in q.verts}:
        p = Vector([math.copysign(abs(a) ** block, a) for a in v.co])
        d = p.normalized()
        p *= (1.0 + 0.12 * math.sin(d.dot(k1) + p1) + 0.07 * math.sin(d.dot(k2) + p2) + 0.035 * math.sin(d.dot(k3) + p3)
              + rr.uniform(-0.03, 0.03))
        if strata:
            band = math.floor((p.z + 2.0) * strata / 2.0)
            k = 1.06 if band % 2 else 0.95
            p.x *= k
            p.y *= k
        for cn, co in cracks:
            if abs(p.dot(cn) - co) < wide:
                p *= 0.88
                groove.add(v)
        v.co = p
    if cracks:
        g.paint([q for q in f if sum(1 for v in q.verts if v in groove) >= 2], "stone_dark")
    g.transform(f, Matrix.Translation(Vector(c)) @ Matrix.Rotation(rot, 4, 'Z')
                @ Matrix.Diagonal((size[0] / 2, size[1] / 2, size[2] / 2, 1.0)))
    if moss:
        ci = A.CI[moss]
        dk = A.CI["stone_dark"]
        for q in f:
            q.normal_update()
            cm = q.calc_center_median()
            if CARTOON:     # calotte de mousse d'un seul tenant, au bord ondulé (et non des facettes éparses)
                wav = 0.12 * math.sin(cm.x * 1.7 + seed) + 0.10 * math.cos(cm.y * 2.1 + seed * 0.7)
                if q.normal.z + wav > 1.0 - moss_p * 0.55 and q[g.col] != dk:
                    q[g.col] = ci
            elif q.normal.z > 0.8 and q[g.col] != dk and g.h(round(cm.x, 1), round(cm.y, 1), seed) < moss_p:
                q[g.col] = ci
    return f


def boulders(g, parts, moss="moss"):
    """parts : (centre, taille, couleur) ; le premier bloc existe aux trois niveaux."""
    L = g.level
    use = parts if L < 2 else parts[:1]
    for k, (c, s, col) in enumerate(use):
        big = max(s) > 2.0
        rock(g, c, s, col, g.seed * 10 + k, lv(g, 3, 2, 1) if big else lv(g, 2, 1, 1), 0.72, rot=k * 1.1,
             crack=1 if (L == 0 and big) else 0, moss=moss if L < 2 else None, moss_p=0.45)
    if L == 0 and CARTOON:      # détail : cailloux éparpillés au pied des blocs
        rs = random.Random(g.seed * 17 + 3)
        c0, s0, _ = parts[0]
        R = max(s0[0], s0[1]) * 0.55
        for k in range(9):
            a = rs.uniform(0, 6.283)
            r = R * rs.uniform(1.0, 1.45)
            sz = rs.uniform(0.12, 0.32) * (1.0 + R * 0.08)
            rock(g, (c0[0] + math.cos(a) * r, c0[1] + math.sin(a) * r, sz * 0.18),
                 (sz * rs.uniform(0.9, 1.3), sz, sz * 0.65), "rock_light" if k % 3 else "rock", g.seed * 100 + k, 1, 0.7,
                 rot=a)


override("boulder_a", lambda g: boulders(g, [((0, 0, 0.15), (2.9, 2.2, 2.2), "rock"), ((1.5, 0.7, 0.05), (1.1, 0.9, 0.8), "rock_light"),
                                             ((-1.4, -0.6, 0.0), (0.9, 0.8, 0.6), "rock")]))
override("boulder_b", lambda g: boulders(g, [((0, 0, 0.25), (4.0, 4.6, 3.2), "rock_light"), ((2.0, -1.4, 0.1), (1.6, 1.3, 1.1), "rock"),
                                             ((-1.9, 1.6, 0.05), (1.3, 1.1, 0.9), "rock_light"), ((1.2, 2.2, 0.0), (0.9, 0.8, 0.6), "rock")]))
override("boulder_c", lambda g: boulders(g, [((0, 0, 0.5), (6.2, 5.0, 5.6), "rock"), ((2.9, 1.0, 0.35), (2.9, 2.5, 1.9), "rock_light"),
                                             ((-2.7, -1.5, 0.2), (2.2, 2.4, 1.5), "rock"), ((0.5, -2.7, 0.05), (1.3, 1.1, 0.9), "rock_light"),
                                             ((-1.0, 2.6, 0.05), (1.1, 1.0, 0.8), "rock")]))


def rock_slab(g):
    L = g.level
    m = g.mark()
    rock(g, (0, 0, 0.15), (3.6, 2.4, 1.0), "rock", g.seed, lv(g, 3, 2, 1), 0.45, strata=4 if L == 0 else 0, crack=1 if L == 0 else 0,
         moss="moss" if L < 2 else None, moss_p=0.35)
    g.transform(g.since(m), Matrix.Rotation(0.14, 4, 'Y') @ Matrix.Rotation(0.08, 4, 'X'))
    if L < 2:
        rock(g, (1.3, -1.0, 0.25), (1.5, 1.1, 0.6), "rock_light", g.seed + 1, lv(g, 2, 1), 0.5)
        if L == 0:
            rock(g, (-1.6, 0.9, 0.1), (0.8, 0.7, 0.4), "rock", g.seed + 2, 2, 0.6)


override("rock_slab", rock_slab)


def cliff(g, parts, tufts=(), stones=6):
    """Affleurement : blocs anguleux stratifiés et fissurés, replats herbeux, éboulis au pied."""
    L = g.level
    use = parts if L == 0 else parts[:max(2, len(parts) * 2 // 3)] if L == 1 else parts[:2]
    for k, (c, s) in enumerate(use):
        sub = lv(g, 4 if k == 0 else 3, 2, 1)
        rock(g, c, s, "rock" if k % 3 else "rock_light", g.seed * 10 + k, sub, 0.55,
             strata=lv(g, 9 if k == 0 else 6, 0, 0), rot=((k * 37) % 50) / 100.0 - 0.25,
             crack=lv(g, 3 if k == 0 else 2, 0, 0), moss="grass" if L < 2 else None, moss_p=0.55)
    if L == 0:
        rs = random.Random(g.seed)
        for (x, y, z, r) in tufts:
            blob(g, (x, y, z), r, 2, "grass", 0.3, g.seed + int(x * 10), 0.2)
        xs = [c[0] for c, s in parts]
        for k in range(stones):
            x = rs.uniform(min(xs) - 3, max(xs) + 3)
            rock(g, (x, rs.uniform(-4.5, -2.6), 0.1), (rs.uniform(0.7, 1.6), rs.uniform(0.6, 1.3), rs.uniform(0.5, 1.0)),
                 "rock" if k % 2 else "rock_light", g.seed + 50 + k, 2, 0.65, rot=rs.uniform(0, 3))


override("cliff_a", lambda g: cliff(g, [((0, 0, 2.0), (9.0, 5.0, 12.0)), ((-3.8, 0.4, 0.8), (5.0, 4.5, 8.5)),
                                        ((3.9, 0.6, 0.0), (4.5, 4.0, 7.0)), ((1.0, -1.6, -1.2), (4.0, 3.0, 5.0))],
                                    [(-3.8, 0.4, 5.1, 1.4), (3.9, 0.6, 3.6, 1.2)]))
override("cliff_b", lambda g: cliff(g, [((0, 0.5, 4.0), (10.0, 6.0, 16.0)), ((-5.5, 0.3, 2.5), (6.5, 5.0, 12.0)),
                                        ((5.5, 0.6, 1.5), (6.0, 5.5, 10.0)), ((-1.5, -2.2, -0.5), (5.0, 3.5, 7.0)),
                                        ((3.0, -2.0, -1.5), (4.0, 3.0, 5.0)), ((-7.5, -0.5, -0.5), (3.5, 3.5, 5.5))],
                                    [(-5.5, 0.3, 8.6, 1.8), (5.5, 0.6, 6.6, 1.6), (-1.5, -2.2, 3.1, 1.2)], stones=8))
override("cliff_c", lambda g: cliff(g, [((0, 0, 5.5), (5.5, 5.0, 19.0)), ((3.2, 0.8, 2.0), (4.0, 4.0, 12.0)),
                                        ((-2.8, -0.5, 0.5), (3.6, 3.5, 9.0)), ((0.8, -2.4, -1.0), (3.0, 2.8, 6.0))],
                                    [(3.2, 0.8, 8.1, 1.0)], stones=5))


def scree(g):
    L = g.level
    rs = random.Random(g.seed)
    for k in range(lv(g, 14, 12, 3)):
        a = rs.uniform(0, 6.28)
        d = 2.8 * math.sqrt(rs.random())
        r = rs.uniform(0.4, 1.1) * (1.0 if L < 2 else 2.2)
        rock(g, pol(a, d, r * 0.08), (r * rs.uniform(0.9, 1.3), r * rs.uniform(0.8, 1.1), r * rs.uniform(0.5, 0.75)),
             "rock" if k % 2 else "rock_light", g.seed + k, lv(g, 2, 1, 1), 0.7, rot=rs.uniform(0, 3))


override("scree", scree)


def snow_patch(g):
    f = g.ico((0, 0, 0.0), 1.0, lv(g, 3, 2, 1), "snow", scale=(4.2, 2.8, 0.28), auto=False)
    rs = random.Random(g.seed)
    for v in {v for q in f for v in q.verts}:
        v.co.x += math.sin(v.co.y * 1.3) * 0.5
        v.co.x *= 1.0 + 0.08 * math.sin(v.co.y * 3.1)
        v.co.z = max(v.co.z + rs.uniform(-0.02, 0.02), -0.12)


override("snow_patch", snow_patch)


# ----------------------------------------------------------------- herbes et fleurs (légères : instanciées en masse)
def grass_tall(g):
    count = 22 if g.hd else A.lod(g, 16, 10, 6)
    for k in range(count):
        a = 6.2832 * k / count + g.R(-0.3, 0.3)
        tilt = g.R(0.08, 0.4)
        d = Vector((math.cos(a) * tilt, math.sin(a) * tilt, 1.0))
        rr = 0.38 * math.sqrt(g.R(0.05, 1.0))
        g.blade((math.cos(a) * rr, math.sin(a) * rr, 0), d, g.R(0.5, 0.95), g.R(0.08, 0.12),
                "dry" if k % 3 == 0 else "grass", bend=g.R(0.15, 0.35), segs=2 if g.lod == 0 else 1)


override("grass_tall", grass_tall)


def fern(g):
    n = 13 if g.hd else A.lod(g, 9, 6, 4)
    for k in range(n):
        a = 6.2832 * k / n + g.R(-0.2, 0.2)
        d = Vector((math.cos(a) * 0.75, math.sin(a) * 0.75, 0.75))
        g.blade((0, 0, 0), d, g.R(0.7, 1.0), g.R(0.24, 0.32), "leaf" if k % 2 else "leaf_light", bend=g.R(0.3, 0.5),
                segs=3 if g.hd else 2 if g.lod == 0 else 1)


override("fern", fern)
for _i in ("flowers_gentian", "flowers_edelweiss", "flowers_meadow"):
    if _i in A.ITEMS:
        A.ITEMS[_i]["flat_lods"] = True
