p = 'map_roads.py'
s = open(p, encoding='utf-8').read()
a = s.index("    def climb(self")
b = s.index("    def forward(self, dist):")
new = '''    def climb(self, gr, line, d_in=45.0, d_out=280.0, until_h=None, near=None, max_len=9000.0, R=15.0,
              min_leg=110.0, max_legs=16, log=False):
        """Monte (ou descend) une vallée en lacets : les traversées rebondissent entre le torrent (d_in) et le
        haut du versant (d_out), les épingles tournent vers l'amont. Là où le fond est trop plat pour la pente
        voulue, la route suit simplement l'axe de la vallée."""
        net = self.net
        pts = net.lines[line]
        total = 0.0
        legs = 0
        while total < max_len and legs < max_legs:
            legs += 1
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
                    # fond plat : on suit l'axe de la vallée, vers l'amont (ou l'aval en descente)
                    _, i, _ = L.point_polyline((x, z), pts)
                    tx, tz = pts[i + 1, 0] - pts[i, 0], pts[i + 1, 1] - pts[i, 1]
                    n = math.hypot(tx, tz) or 1.0
                    sgn = 1.0 if (pts[i + 1, 2] - pts[i, 2]) * gr >= 0 else -1.0
                    cx, cz = tx / n * sgn, tz / n * sgn
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
                if travelled > min_leg and m > abs(gr) and ((d < d_in and d < d_prev) or (d > d_out and d > d_prev)):
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

'''
s = s[:a] + new + s[b:]
open(p, 'w', encoding='utf-8').write(s)

p = 'map_def.py'
s = open(p, encoding='utf-8').read()
s = s.replace('c.climb(0.088, "col", d_in=45, d_out=330, near=(-560, 2480, 150), log=LOG)',
              'c.climb(0.088, "col", d_in=45, d_out=330, until_h=408.0, near=(-560, 2480, 200), log=LOG)')
open(p, 'w', encoding='utf-8').write(s)
