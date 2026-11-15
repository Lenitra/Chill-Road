"""Aperçu rapide du relief naturel seul (grille de 8 m) : python t_terrain.py"""
import time
import numpy as np
import map_def as D, map_lib as L, map_terrain as T, map_preview as P

t = time.time()
g, H, info = T.build(8.0)
water = np.zeros(H.shape, dtype=np.uint8)
for i, lk in enumerate(D.LAKES):
    water[T.flood(g, H, lk)] = i + 1
P.render(g, H, "out/terrain.png", water=water, rivers=info["rivers"], size=1400)
for (x, z) in [(60, 380), (-800, -700), (800, -500), (-800, 1100), (900, 1100), (1780, 1640), (-1500, -1350), (2000, 1750)]:
    print("h(%5d, %5d) = %.0f" % (x, z, float(g.sample(H, x, z))))
print("%.1f s" % (time.time() - t))
