"""Essai du traceur : python t_route.py  (une route de col seule, aperçu out/route_test.png)"""
import math, time
import numpy as np
import map_def as D, map_lib as L, map_terrain as T, map_roads as R, map_preview as P, map_route as RT, map_bake as B

g, H, info = T.cached(D.STEP)
water = B.water_masks(g, H)
rt = RT.Router(g, H, water, info["rivers"])
tests = [((-60, -1830), (0.0, 1.0), (150, 278), "col_sud", "mountain"),
         ((1830, 260), (-1.0, 0.0), (222, 350), "col_est", "mountain"),
         ((80, 1840), (0.0, -1.0), (150, 422), "col_nord", "mountain")]
roads = []
for (a, hd, b, name, kind) in tests:
    pts, head = rt.route(a, hd, b, kind, goal_heading=None, label=name)
    P_ = np.array([a] + pts)
    roads.append(dict(pts=P_, kind=kind, hw=3.4, s=L.arclen(P_), spans=[]))
P.render(g, H, "out/route_test.png", roads=roads, water=water, rivers=info["rivers"], size=1300,
         window=(-700, -2000, 1950, 2000), contours=10)
