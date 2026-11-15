"""Diagnostic du tracé : ouvrages, rayons serrés, gros déblais/remblais. python t_roads.py [id d'arête ou de chaîne]"""
import sys
import numpy as np
import map_bake as B, map_lib as L, map_roads as R

g, H, info, net, nodes, edges = B.stage_roads(verbose=False)
want = sys.argv[1:] 
for e in edges:
    if want and not any(w in e["id"] for w in want):
        continue
    s, P = e["s"], e["pts"]
    k = np.abs(L.curvature(P, R.DS))
    dev = P[:, 2] - e["ground"]
    gr = np.gradient(P[:, 2], s)
    print("== %s (%s) %.0f m, h %.0f -> %.0f, a=%s b=%s" % (e["id"], e["kind"], s[-1], P[0, 2], P[-1, 2], e["a"], e["b"]))
    for (s0, s1, kind) in e["spans"]:
        i0, i1 = np.searchsorted(s, s0), min(np.searchsorted(s, s1), len(s) - 1)
        print("   %-7s %6.0f..%6.0f (%4.0f m) (%.0f,%.0f)->(%.0f,%.0f) h %.1f->%.1f, sol max %+.0f min %+.0f" % (
            kind, s0, s1, s1 - s0, P[i0, 0], P[i0, 1], P[i1, 0], P[i1, 1], P[i0, 2], P[i1, 2], -dev[i0:i1 + 1].min(), -dev[i0:i1 + 1].max()))
    mask = np.ones(len(s), dtype=bool)
    for (s0, s1, kind) in e["spans"]:
        mask &= ~((s >= s0) & (s <= s1))
    lim = 9.0 if e["kind"] != "highway" else 12.0
    rlim = 1.0 / (250.0 if e["kind"] == "highway" else 11.0)
    for name, cond in (("deblai", mask & (dev < -lim)), ("remblai", mask & (dev > lim)), ("rayon", k > rlim), ("pente", np.abs(gr) > e["grade"] * 1.12)):
        idx = np.where(cond)[0]
        if not len(idx):
            continue
        for grp in np.split(idx, np.where(np.diff(idx) > 8)[0] + 1):
            j = grp[np.argmax(np.abs(dev[grp])) if name in ("deblai", "remblai") else np.argmax(k[grp]) if name == "rayon" else np.argmax(np.abs(gr[grp]))]
            print("   %-7s s=%5.0f..%5.0f vers (%.0f, %.0f) : ecart %+.1f m, R=%.0f, pente %.1f%%" % (
                name, s[grp[0]], s[grp[-1]], P[j, 0], P[j, 1], dev[j], 1.0 / max(k[j], 1e-5), gr[j] * 100))
