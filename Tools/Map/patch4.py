p = 'map_roads.py'
s = open(p, encoding='utf-8').read()
# fin de grade : pas de points de contrôle trop rapprochés
s = s.replace('''        if since > 6.0:
            self._add(x, z)
        self._heading = (hx, hz)''', '''        if since > 14.0 or travelled < 24.0:
            self._add(x, z)
        elif since > 0.0:
            self.ctrl[-1] = (float(x), float(z))
        self._heading = (hx, hz)
        self.last_len = travelled''')
s = s.replace('''    def forward(self, dist):''', '''    def climb(self, gr, line, d_in=45.0, d_out=280.0, until_h=None, near=None, max_len=9000.0, R=15.0,
              min_leg=110.0, log=False):
        """Monte (ou descend) une vallée en lacets : les traversées rebondissent entre le torrent (d_in) et le
        haut du versant (d_out), les épingles tournent vers l'amont."""
        net = self.net
        pts = net.lines[line]
        total = 0.0
        while total < max_len:
            x, z = self.pos
            hx, hz = self.heading()
            travelled, since = 0.0, 0.0
            step, rmin = 6.0, 30.0
            done = False
            d_prev = L.point_polyline((x, z), pts)[0]
            while True:
                gx, gz = net.g.gradient(net.Hs, x, z, eps=8.0)
                gx, gz = float(gx), float(gz)
                m = math.hypot(gx, gz)
                if m > abs(gr) * 1.03:
                    ux, uz = gx / m, gz / m
                    phi = math.acos(max(-1.0, min(1.0, gr / m)))
                    best = None
                    for sgn in (-1, 1):
                        c, s_ = math.cos(phi * sgn), math.sin(phi * sgn)
                        cx, cz = ux * c - uz * s_, ux * s_ + uz * c
                        dot = cx * hx + cz * hz
                        if best is None or dot > best[0]:
                            best = (dot, cx, cz)
                    cx, cz = best[1], best[2]
                else:
                    cx, cz = hx, hz
                ang = math.atan2(hx * cz - hz * cx, hx * cx + hz * cz)
                ang = max(-step / rmin, min(step / rmin, ang))
                c, s_ = math.cos(ang), math.sin(ang)
                hx, hz = hx * c - hz * s_, hx * s_ + hz * c
                x, z = x + hx * step, z + hz * step
                travelled += step
                since += step
                if since >= 24.0:
                    self._add(x, z)
                    since = 0.0
                hh = float(net.g.sample(net.Hs, x, z))
                if until_h is not None and ((gr > 0 and hh >= until_h) or (gr < 0 and hh <= until_h)):
                    done = True
                    break
                if near is not None and math.hypot(x - near[0], z - near[1]) < near[2]:
                    done = True
                    break
                d = L.point_polyline((x, z), pts)[0]
                if travelled > min_leg and ((d < d_in and d < d_prev) or (d > d_out and d > d_prev)):
                    break
                if total + travelled > max_len:
                    done = True
                    break
                d_prev = d
            if since > 14.0:
                self._add(x, z)
            elif since > 0.0:
                self.ctrl[-1] = (float(x), float(z))
            self._heading = (hx, hz)
            total += travelled
            if log:
                print("  %s lacet : %.0f m -> (%.0f, %.0f) h=%.0f d=%.0f" % (self.id, travelled, x, z, hh, L.point_polyline((x, z), pts)[0]))
            if done:
                break
            # épingle vers l'amont (pour monter) ou vers l'aval (pour descendre)
            gx, gz = net.g.gradient(net.Hs, x, z, eps=8.0)
            side = hx * float(gz) - hz * float(gx)
            turn = 1.0 if side > 0 else -1.0
            if gr < 0:
                turn = -turn
            self.hairpin(turn, R)
        return self

    def forward(self, dist):''')
s = s.replace('''        self.chains = []
        self.lines = {}''', '''        self.chains = []
        self.lines = {}
        self.fixed = {}              # nœuds à hauteur imposée''')
s = s.replace('''        node_h[name] = float(g.sample(net.Hs, x, z))''', '''        node_h[name] = net.fixed.get(name, float(g.sample(net.Hs, x, z)))''')
s = s.replace('''            for name, v in acc.items():
                node_h[name] = float(np.mean(v))''', '''            for name, v in acc.items():
                node_h[name] = net.fixed.get(name, float(np.mean(v)))''')
open(p, 'w', encoding='utf-8').write(s)

p = 'map_terrain.py'
s = open(p, encoding='utf-8').read()
s = s.replace('''            H[sz, sx] = np.where(f > 0, np.minimum(cur, cur * (1 - f) + floor * f), cur)''',
              '''            # jamais plus d'un lit sous le terrain : sur les parois du cirque, le torrent dévale le relief
            floor = np.maximum(floor, cur - depth - 1.5)
            H[sz, sx] = np.where(f > 0, np.minimum(cur, cur * (1 - f) + floor * f), cur)''')
s = s.replace('''        water = np.minimum.accumulate(L.gauss1d(water, 2.0)) if seg[0, 2] > seg[-1, 2] else \\
            np.minimum.accumulate(L.gauss1d(water, 2.0)[::-1])[::-1]''', '''        water = L.gauss1d(water, 1.5)''')
open(p, 'w', encoding='utf-8').write(s)

p = 'map_bake.py'
s = open(p, encoding='utf-8').read()
s = s.replace('    net.lines = info["lines"]\n', '    net.lines = info["lines"]\n    net.dam = T.dam_line(g, H)\n')
open(p, 'w', encoding='utf-8').write(s)

p = 'map_def.py'
s = open(p, encoding='utf-8').read()
s = s.replace('''    dict(id="main", profile=PROFILE_U, river=14.0, pts=[''', '''    dict(id="main", profile=PROFILE_U, river=14.0, river_from=1, river_to=10, pts=[''')
a = s.index("    N = net.node\n    LOG = True")
b = s.index("# Fenêtres des aperçus")
new = open('roads_body.txt', encoding='utf-8').read()
s = s[:a] + new + "\n\n" + s[b:]
open(p, 'w', encoding='utf-8').write(s)
