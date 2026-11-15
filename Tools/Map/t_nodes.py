"""Contrôle des carrefours : angles entre les branches, virages serrés et ouvrages trop proches, torrent sous le
carrefour, pente des branches à l'arrivée. python t_nodes.py"""
import math
import numpy as np
import map_bake as B, map_lib as L, map_roads as R

g, H, info, net, nodes, edges = B.stage_roads(verbose=False)
by_id = {e["id"]: e for e in edges}
bad = 0
for n in nodes.values():
    if n.get("merge") or len(n["edges"]) == 2 and len(set(n["edges"])) == 2 and all(by_id[i]["chain"] == by_id[n["edges"][0]]["chain"] for i in n["edges"]):
        continue
    arms = []
    notes = []
    for eid in n["edges"]:
        e = by_id[eid]
        for end in ("a", "b"):
            if e[end] != n["id"]:
                continue
            P = e["pts"] if end == "a" else e["pts"][::-1]
            s = L.arclen(P)
            if math.hypot(P[0, 0] - n["x"], P[0, 1] - n["z"]) > 3.0:
                continue                                     # bout de bretelle décalé (nœud d'insertion)
            i15, i40 = int(np.searchsorted(s, 15.0)), int(np.searchsorted(s, min(40.0, s[-1] - 1)))
            i15, i40 = min(i15, len(P) - 1), min(i40, len(P) - 1)
            a15 = math.degrees(math.atan2(P[i15, 1] - P[0, 1], P[i15, 0] - P[0, 0]))
            a40 = math.degrees(math.atan2(P[i40, 1] - P[0, 1], P[i40, 0] - P[0, 0]))
            k = np.abs(L.curvature(P, R.DS))
            i80 = min(int(np.searchsorted(s, 80.0)), len(P) - 1)
            rmin = 1.0 / max(k[:i80 + 1].max(), 1e-4)
            grade = (P[min(i40, len(P) - 1), 2] - P[0, 2]) / max(s[i40], 1.0)
            spans = []
            for (s0, s1, kind) in e["spans"]:
                d0 = s0 if end == "a" else e["s"][-1] - s1
                if d0 < 40.0:
                    spans.append("%s à %.0f m" % (kind, max(d0, 0)))
            arms.append((a15, eid, e["kind"]))
            if abs((a40 - a15 + 180) % 360 - 180) > 18:
                notes.append("%s tourne dès le départ (%.0f°)" % (eid, (a40 - a15 + 180) % 360 - 180))
            if rmin < 25 and e["kind"] != "ramp":
                notes.append("%s : virage R=%.0f à moins de 80 m" % (eid, rmin))
            if spans:
                notes.append("%s : %s" % (eid, ", ".join(spans)))
            if abs(grade) > 0.085:
                notes.append("%s : pente %.0f %% à l'arrivée" % (eid, grade * 100))
    arms.sort()
    for i in range(len(arms)):
        if len(arms) < 2:
            break
        a, b = arms[i], arms[(i + 1) % len(arms)]
        gap = (b[0] - a[0]) % 360
        if gap < 55:
            notes.append("angle de %.0f° entre %s et %s" % (gap, a[1], b[1]))
    dr = min((L.point_polyline((n["x"], n["z"]), rv["pts"])[0] - float(rv["pts"][0][3]) * 0.5) for rv in info["rivers"])
    if dr < 45:
        notes.append("torrent à %.0f m" % dr)
    flag = " !" if notes else ""
    bad += bool(notes)
    print("%-14s (%5.0f, %5.0f) h=%4.0f  %d branches%s" % (n["id"], n["x"], n["z"], n["y"], len(arms), flag))
    for t in notes:
        print("      " + t)
print("%d carrefours à reprendre" % bad)
