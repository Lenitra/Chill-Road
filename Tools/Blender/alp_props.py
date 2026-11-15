"""Mobilier urbain et rural, signalisation du pack alpin. Pieds prolongés de 0,5 m sous le sol."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac


def prop(iid, fname, build, cat="street", lods=2, **kw):
    reg(dict(id=iid, file=fname, cat=cat, lods=lods, build=build, **kw))


def ngon(r, n, rot=0.0, c=(0, 0)):
    return [(c[0] + math.cos(rot + 2 * math.pi * k / n) * r, c[1] + math.sin(rot + 2 * math.pi * k / n) * r)
            for k in range(n)]


def post(g, h, r=0.05, color="metal", n=None, x=0.0, y=0.0, r1=None):
    """Poteau vertical prolongé de 0,5 m sous le sol."""
    return g.frustum((x, y, -0.5), r, r if r1 is None else r1, h + 0.5, n or lod(g, 6, 4), color, cap0=False)


# ----------------------------------------------------------------- lampadaires
def lamp_alpine(g):
    """Lanterne de ville en fonte : fût cannelé, console, lanterne à quatre faces, suspension fleurie."""
    n = lod(g, 6, 4)
    g.frustum((0, 0, -0.5), 0.16, 0.13, 1.2, n, "metal_dark", cap0=False)
    g.frustum((0, 0, 0.7), 0.075, 0.055, 3.0, n, "metal_dark", cap0=False)
    if g.lod == 0:
        g.frustum((0, 0, 0.68), 0.12, 0.12, 0.08, n, "metal_dark")
        g.frustum((0, 0, 3.0), 0.09, 0.09, 0.06, n, "metal_dark")
    # lanterne
    g.frustum((0, 0, 3.7), 0.1, 0.2, 0.12, 4, "metal_dark", rot=math.pi / 4)
    g.frustum((0, 0, 3.82), 0.2, 0.27, 0.5, 4, "lamp", rot=math.pi / 4, cap0=False, cap1=False)
    g.frustum((0, 0, 4.32), 0.34, 0.0, 0.28, 4, "metal_dark", rot=math.pi / 4)
    if g.lod == 0:
        g.frustum((0, 0, 4.58), 0.03, 0.0, 0.2, 4, "metal_dark")
        # panier fleuri en potence
        g.box((0.3, 0, 2.9), (0.6, 0.04, 0.04), "metal_dark")
        g.frustum((0.55, 0, 2.35), 0.1, 0.24, 0.3, 6, "wood_dark")
        f = g.ico((0.55, 0, 2.72), 0.27, 1, "geranium", scale=(1, 1, 0.6))
        g.jitter(f, 0.04)
        g.box((0.55, 0, 2.8), (0.015, 0.015, 0.25), "metal_dark")


prop("lamp_alpine", "Lamp_Alpine", lamp_alpine, tags=["lamp"], collider="capsule", radius=0.16,
     lights=[(0, 0, 4.05)])


def lamp_road(g):
    """Lampadaire routier : mât galvanisé de 8 m, crosse et tête côté -Y (vers la chaussée)."""
    n = lod(g, 6, 4)
    g.frustum((0, 0, -0.5), 0.11, 0.065, 8.3, n, "metal", cap0=False)
    if g.lod == 0:
        g.box((0, 0, 0.5), (0.26, 0.26, 1.0), "metal_dark")
    g.limb((0, 0, 7.75), (0, -1.5, 8.05), 0.055, 0.045, n, "metal")
    g.box((0, -1.9, 8.06), (0.34, 0.9, 0.14), "metal_dark")
    g.box((0, -1.95, 7.975), (0.24, 0.6, 0.04), "lamp")


prop("lamp_road", "Lamp_Road", lamp_road, tags=["lamp"], collider="capsule", radius=0.12, lights=[(0, -1.95, 7.95)])
