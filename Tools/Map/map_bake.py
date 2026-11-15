"""Génère la carte : relief, routes, terrassement, masques, aperçus et données pour Unity.

python map_bake.py [--no-preview] [--no-export] [--roads]      (--roads : tracé et aperçus seulement, sans terrassement)
"""
import os, sys, time
import numpy as np
import map_def as D, map_lib as L, map_terrain as T, map_roads as R, map_preview as P


def stage_roads(step=D.STEP, verbose=True):
    g, H, info = T.cached(step)
    net = R.Net(g, H)
    net.lines = info["lines"]
    import map_route as RT
    net.router = RT.Router(g, H, water_masks(g, H), info["rivers"])
    D.roads(net)
    nodes, edges = R.build_edges(net, info["rivers"], D.LAKE_LEVEL, verbose)
    return g, H, info, net, nodes, edges


def water_masks(g, H):
    water = np.zeros(H.shape, dtype=np.uint8)
    for i, lk in enumerate(D.LAKES):
        water[T.flood(g, H, lk)] = i + 1
    return water


def clear_water(g, H, water):
    """Écarte le terrain du plan d'eau là où la nappe est dessinée : le fond passe au moins 0,8 m dessous, la rive
    sèche au moins 0,35 m dessus (sinon les deux surfaces, presque confondues, clignotent)."""
    H = H.copy()
    for i, lk in enumerate(D.LAKES):
        wet = water == i + 1
        if not wet.any():
            continue
        # zone que la nappe peut couvrir (elle déborde de deux cellules sur la rive)
        near = L.blur2d(wet.astype(np.float64), 2.5) > 0.002
        lvl = lk["level"]
        H = np.where(wet, np.minimum(H, lvl - 0.8), H)
        dry_near = near & ~wet
        H = np.where(dry_near & (H < lvl + 0.35), lvl + 0.35, H)
    return H


def previews(g, H, nodes, edges, water, rivers):
    P.render(g, H, "out/map.png", roads=edges, water=water, rivers=rivers, nodes=list(nodes.values()), size=1600,
             labels=False)
    for name, win in D.VIEWS.items():
        P.render(g, H, "out/view_%s.png" % name, roads=edges, water=water, rivers=rivers,
                 nodes=list(nodes.values()), size=1100, window=win, contours=10)


def bake(step=D.STEP, export=True, preview=True, roads_only=False):
    import map_export as E
    t = time.time()
    g, H, info, net, nodes, edges = stage_roads(step)
    if roads_only:
        previews(g, H, nodes, edges, water_masks(g, H), info["rivers"])
        print("%.1f s" % (time.time() - t))
        return g, H, info, nodes, edges
    Hc, edge = R.carve(g, H, nodes, edges)
    water = water_masks(g, Hc)
    Hc = clear_water(g, Hc, water)
    hole = R.holes(g, Hc, edges, edge)
    print("tetes de tunnel : %d mailles ouvertes" % int(hole.sum()))
    for e in edges:
        e["rail"] = E.rails(g, Hc, e)
    urban = [(v[2], v[3], v[4]) for v in D.VILLAGES + D.HAMLETS]
    rgba, paved, snow, forest, urban_m, slope = E.masks(g, Hc, info, edge, water, edges, urban_pts=urban)
    import map_signs as SG
    places = SG.build(g, Hc, nodes, edges)
    print("signalisation : %d objets" % len(places))
    if export:
        E.export(g, Hc, info, nodes, edges, water, edge, rgba, paved, snow, places=places, pois=D.pois(nodes, edges),
                 holes=hole, tunnels=R.tunnel_zone(g, edges))
        import map_overview as OV
        for e in edges:
            tun = np.where(R.tunnel_mask(e))[0]
            e["tunnels"] = [(int(grp[0]), int(grp[-1])) for grp in np.split(tun, np.where(np.diff(tun) > 1)[0] + 1) if len(grp)]
        OV.render(g, Hc, rgba, paved, snow, water, edges, info["rivers"], [], os.path.join(E.OUT, "map_overview.png"))
    if preview:
        previews(g, Hc, nodes, edges, water, info["rivers"])
        from PIL import Image
        Image.fromarray(np.stack([rgba[..., 2], rgba[..., 0], paved], axis=-1)[::-2, ::2]).save("out/masks.png")
    print("%.1f s" % (time.time() - t))
    return g, Hc, info, nodes, edges


if __name__ == "__main__":
    bake(export="--no-export" not in sys.argv, preview="--no-preview" not in sys.argv, roads_only="--roads" in sys.argv)
