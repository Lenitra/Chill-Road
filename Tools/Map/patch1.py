import re
p='map_terrain.py'; s=open(p,encoding='utf-8').read()
a=s.index("    for vi, v in enumerate(D.VALLEYS):")
b=s.index("    shape = (g.n, g.n)")
new='''    for vi, v in enumerate(D.VALLEYS):
        line = valley_line(v)
        lines[v["id"]] = line
        pd, ph = profile_table(v["profile"])
        # Éperons et ravines : le versant avance ou recule (bruit du plan, continu partout)
        spur = L.fbm(Xc / 520.0, Zc / 520.0, 3, seed=D.SEED + 20 + vi) * 0.42
        Hv = np.full(Xc.shape, 1e9)
        Pv = np.full(Xc.shape, 1e9)
        # Minimum sur les segments de la ligne de fond : continu, sans saut au changement de segment le plus proche
        for i in range(len(line) - 1):
            ax, az, ea, wa = line[i]
            bx, bz, eb, wb = line[i + 1]
            dx, dz = bx - ax, bz - az
            t = np.clip(((Xc - ax) * dx + (Zc - az) * dz) / max(dx * dx + dz * dz, 1e-9), 0.0, 1.0)
            d = np.hypot(Xc - ax - t * dx, Zc - az - t * dz)
            w = wa + t * (wb - wa)
            dd = np.maximum(0.0, d - w) * (1.0 + spur)
            P = np.interp(dd, pd, ph) + np.minimum(d, w) ** 2 * (1.5 / np.maximum(w, 1.0) ** 2)
            Hs = ea + t * (eb - ea) + P
            m = Hs < Hv
            Hv = np.where(m, Hs, Hv)
            Pv = np.where(m, P, Pv)
        Up = np.where(Hv < Hc, Pv, Up)
        Hc = L.smin(Hc, Hv, 22.0)
    # Sommets : plafond irrégulier
    cap = D.PEAK_BASE + D.PEAK_VAR * L.fbm(Xc / 1300.0, Zc / 1300.0, 3, seed=D.SEED + 60) \
        + 260.0 * (L.ridged(Xc / 800.0, Zc / 800.0, 4, seed=D.SEED + 61) - 0.5)
    Hc = L.smin(Hc, cap, 70.0)
    # Cirque de bordure (carré arrondi, bord irrégulier) : ferme les vallées qui sortent de la carte
    e = D.HALF - (Xc ** 4 + Zc ** 4) ** 0.25 + 240.0 * L.fbm(Xc / 800.0, Zc / 800.0, 3, seed=D.SEED + 62)
    rim = D.RIM_HEIGHT * (1.0 - L.smoothstep(0.0, D.RIM_WIDTH, e)) ** 1.5 + 0.5 * np.minimum(Hc, 600.0)
    Hc = np.where(e < D.RIM_WIDTH + 100.0, L.smax(Hc, rim, 80.0), Hc)

'''
s=s[:a]+new+s[b:]
s=s.replace("H = H + 30.0 * L.smoothstep(420.0, 800.0, up) * (L.ridged(X / 330.0, Z / 330.0, 4, seed=D.SEED + 71) - 0.55)",
            "H = H + 70.0 * L.smoothstep(400.0, 760.0, up) * (L.ridged(X / 360.0, Z / 360.0, 4, seed=D.SEED + 71) - 0.55)")
open(p,'w',encoding='utf-8').write(s)
p='map_def.py'; s=open(p,encoding='utf-8').read()
s=s.replace("RIM_WIDTH, RIM_HEIGHT = 620.0, 640.0","RIM_WIDTH, RIM_HEIGHT = 900.0, 620.0")
open(p,'w',encoding='utf-8').write(s)
