p='map_terrain.py'; s=open(p,encoding='utf-8').read()
a=s.index("    # Cuvettes des lacs")
b=s.index("    # Lits de rivières")
new='''    # Cuvettes des lacs (les retenues de barrage n'en ont pas : la vallée est simplement noyée)
    for lk in D.LAKES:
        if lk["depth"] <= 0:
            continue
        r0 = max(lk["a"], lk["b"]) * 1.8
        sz, sx = g.window(lk["c"][0] - r0, lk["c"][1] - r0, lk["c"][0] + r0, lk["c"][1] + r0)
        r = lake_r(lk, X[sz, sx], Z[sz, sx])
        bowl = lk["level"] + np.where(r < 1.0, -lk["depth"] * (1.0 - r ** 2) - 0.8, 70.0 * (r - 1.0) - 0.8)
        w = 1.0 - L.smoothstep(1.05, 1.5, r)
        cur = H[sz, sx]
        H[sz, sx] = cur * (1 - w) + L.smin(cur, bowl, 4.0) * w

'''
s=s[:a]+new+s[b:]
# flood : lignes d'arrêt
s=s.replace('''    if blocked is not None:
        sub &= ~blocked[z0:z1 + 1, x0:x1 + 1]''','''    if blocked is None:
        blocked = np.zeros(H.shape, dtype=bool)
    for (pa, pb) in lk.get("stops", []):
        def block(sz_, sx_, d, t, i):
            blocked[sz_, sx_] |= d < g.step * 1.5
        stamp_polyline(g, np.array([pa, pb], dtype=np.float64), g.step * 3, block)
    sub &= ~blocked[z0:z1 + 1, x0:x1 + 1]''')
open(p,'w',encoding='utf-8').write(s)

p='map_lib.py'; s=open(p,encoding='utf-8').read()
s=s.replace('''    def sample(self, A, x, z):''','''    def window(self, x0, z0, x1, z1):
        """Tranches (z, x) de la grille couvrant un rectangle en mètres."""
        ix0 = int(max(0, math.floor((x0 + self.half) / self.step)))
        ix1 = int(min(self.n - 1, math.ceil((x1 + self.half) / self.step)))
        iz0 = int(max(0, math.floor((z0 + self.half) / self.step)))
        iz1 = int(min(self.n - 1, math.ceil((z1 + self.half) / self.step)))
        return slice(iz0, iz1 + 1), slice(ix0, ix1 + 1)

    def sample(self, A, x, z):''')
open(p,'w',encoding='utf-8').write(s)

p='map_def.py'; s=open(p,encoding='utf-8').read()
s=s.replace('''(1500, 20, 2, 480), (2150, 160, 2, 300), (2620, 330, -22, 55), (3000, 700, -50, 40), (3500, 900, -80, 40)]),''',
 '''(1500, 20, 2, 480), (2150, 160, 3, 300), (2420, 250, 2, 70), (2700, 380, -24, 50), (3000, 700, -50, 40),
        (3500, 900, -80, 40)]),''')
s=s.replace('''level=LAKE_LEVEL, depth=38, seed=(1520, 30)),''','''level=LAKE_LEVEL, depth=38, seed=(1520, 30),
         stops=[((2330, -50), (2290, 560))]),''')
s=s.replace('''level=250.0, depth=30, seed=(300, -1900)),''','''level=250.0, depth=0, seed=(300, -1900),
         stops=[((330, -1525), (680, -1445))]),''')
s=s.replace('DAM = dict(a=(395, -1500), b=(610, -1470), crest=253.0, width=9.0)','DAM = dict(site=(505, -1485), crest=253.0, width=9.0)')
open(p,'w',encoding='utf-8').write(s)
