"""Nature et animaux du pack alpin : épicéas, mélèzes, arolles, feuillus, buissons, fleurs, rochers, falaises, bétail."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod


def nat(iid, fname, seed, build, radius=None, tags=(), **kw):
    d = dict(id=iid, file=fname, cat="nature", tags=list(tags), seed=seed, build=build, **kw)
    if radius:
        d["collider"] = "capsule"
        d["radius"] = radius
    reg(d)


# ----------------------------------------------------------------- conifères
def conifer(g, H, width, tiers, color="spruce", trunk=0.3, bare=0.12, droop=0.14, taper=0.78, trunk_color="bark"):
    """Conifère : tronc et étages en cônes qui se chevauchent ; dessous concave (ombré par le dégradé)."""
    n = lod(g, 7, 5, 4)
    T = lod(g, tiers, max(3, (tiers + 1) // 2), 2)
    g.frustum((0, 0, -0.5), trunk, trunk * 0.3, H * 0.6 + 0.5, lod(g, 6, 5, 4), trunk_color, cap0=False)
    z0, z1 = H * bare, H * 0.74
    for k in range(T):
        t = k / max(T - 1, 1)
        zb = z0 + (z1 - z0) * t
        r = width * (1.0 - taper * t) + 0.15
        h = (H - zb) if k == T - 1 else (H - z0) / T * 1.9
        rot = g.R(0, 6.28)
        rim = [g.bm.verts.new(Vector((math.cos(rot + 6.2832 * i / n) * r * g.R(0.86, 1.12),
                                      math.sin(rot + 6.2832 * i / n) * r * g.R(0.86, 1.12),
                                      zb - droop * r + g.R(-0.08, 0.08)))) for i in range(n)]
        apex = g.bm.verts.new(Vector((g.R(-0.06, 0.06), g.R(-0.06, 0.06), zb + h)))
        faces = []
        if g.lod < 2:
            inner = [g.bm.verts.new(Vector((math.cos(rot + 6.2832 * (i + 0.5) / n) * r * 0.3,
                                            math.sin(rot + 6.2832 * (i + 0.5) / n) * r * 0.3, zb + h * 0.12)))
                     for i in range(n)]
        for i in range(n):
            j = (i + 1) % n
            faces.append(g.bm.faces.new((rim[i], rim[j], apex)))
            if g.lod < 2:
                faces.append(g.bm.faces.new((rim[j], rim[i], inner[i])))
                faces.append(g.bm.faces.new((rim[i], inner[i - 1], inner[i])))
        if g.lod < 2:
            faces.append(g.bm.faces.new(list(reversed(inner))))
        else:
            faces.append(g.bm.faces.new(list(reversed(rim))))
        g.paint(faces, color)


nat("spruce_a", "Spruce_A", 401, lambda g: conifer(g, 14.0, 2.5, 7), radius=0.32, tags=["tree", "conifer"])
nat("spruce_b", "Spruce_B", 402, lambda g: conifer(g, 18.0, 2.9, 8, trunk=0.36), radius=0.36, tags=["tree", "conifer"])
nat("spruce_c", "Spruce_C", 403, lambda g: conifer(g, 22.0, 3.3, 9, trunk=0.42, bare=0.16), radius=0.42,
    tags=["tree", "conifer"])


def larch(g, H, width, tiers):
    """Mélèze : silhouette claire et aérée, tronc visible, étages espacés et relevés."""
    conifer(g, H, width, tiers, color="larch", trunk=0.26, bare=0.22, droop=0.05, taper=0.82)
    if g.lod == 0:
        for k in range(5):
            z = H * g.R(0.1, 0.22)
            a = g.R(0, 6.28)
            g.limb((0, 0, z), (math.cos(a) * 1.3, math.sin(a) * 1.3, z - 0.25), 0.05, 0.015, 3, "dead", cap1=False)


nat("larch_a", "Larch_A", 411, lambda g: larch(g, 15.0, 2.6, 6), radius=0.3, tags=["tree", "conifer"])
nat("larch_b", "Larch_B", 412, lambda g: larch(g, 19.0, 3.0, 7), radius=0.34, tags=["tree", "conifer"])


# ----------------------------------------------------------------- rochers
def boulder(g, r, scale, color="rock", sub=None, bury=0.25, flat=0.3):
    f = g.ico((0, 0, 0), r, sub if sub is not None else lod(g, 2, 1, 1), color, scale=scale)
    g.jitter(f, r * 0.13)
    floor = -r * scale[2] * (1 - flat)
    g.transform(f, Matrix.Translation((0, 0, -floor - bury * r)))
    return f


nat("boulder_a", "Boulder_A", 421, lambda g: boulder(g, 1.1, (1.3, 1.0, 0.8)), radius=1.0, tags=["rock"])
nat("boulder_b", "Boulder_B", 422, lambda g: boulder(g, 1.8, (1.1, 1.3, 0.75), "rock_light"), radius=1.7, tags=["rock"])


def boulder_c(g):
    boulder(g, 2.6, (1.2, 0.95, 0.9))
    if g.lod < 2:
        m = g.mark()
        boulder(g, 1.2, (1.2, 1.0, 0.7), "rock_light", sub=1)
        g.move(g.since(m), (2.6, 0.9, -0.2), 0.6)
        m = g.mark()
        boulder(g, 0.8, (1.0, 1.2, 0.7), "rock", sub=1)
        g.move(g.since(m), (-2.4, -1.4, -0.1), 1.9)


nat("boulder_c", "Boulder_C", 423, boulder_c, radius=2.4, tags=["rock"])
