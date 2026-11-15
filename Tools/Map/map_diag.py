"""Diagnostic des superpositions (z-fighting) sur la carte : bâtiments qui se recouvrent, objets en double,
chaussées qui se chevauchent hors carrefour, terrain trop proche de l'eau ou de la chaussée."""
import math
import numpy as np
import map_def as D, map_lib as L, map_roads as R
import map_bake as B
import map_export as E
import map_place as PL


def boxes_overlap_area(A, B):
    """Aire approximative du recouvrement de deux rectangles orientés (échantillonnage)."""
    xs = [p[0] for p in A]
    zs = [p[1] for p in A]
    n = 12
    hits = 0
    for i in range(n):
        for j in range(n):
            u, v = (i + 0.5) / n, (j + 0.5) / n
            # point dans A (interpolation bilinéaire des coins)
            p0 = (A[0][0] + (A[1][0] - A[0][0]) * u, A[0][1] + (A[1][1] - A[0][1]) * u)
            p1 = (A[3][0] + (A[2][0] - A[3][0]) * u, A[3][1] + (A[2][1] - A[3][1]) * u)
            p = (p0[0] + (p1[0] - p0[0]) * v, p0[1] + (p1[1] - p0[1]) * v)
            if inside(p, B):
                hits += 1
    ax = math.dist(A[0], A[1]) * math.dist(A[1], A[2])
    return ax * hits / (n * n)


def inside(p, poly):
    sign = None
    for i in range(4):
        a, b = poly[i], poly[(i + 1) % 4]
        c = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        s = c > 0
        if sign is None:
            sign = s
        elif s != sign:
            return False
    return True


def main():
    g, H, info, net, nodes, edges = B.stage_roads(verbose=False)
    pads = PL.pads_with_height(g, H)
    Hc, edge = R.carve(g, H, nodes, edges, pads)
    water, dam = B.water_masks(g, Hc)
    Hc = B.clear_water(g, Hc, water)
    for e in edges:
        e["rail"] = E.rails(g, Hc, e)
    P = PL.populate(g, Hc, edge, water, nodes, edges, info["rivers"], dam)
    items = P.items

    # 1. Bâtiments : boîtes réelles (débords compris)
    cats = ("chalet", "townhouse", "landmark", "farm", "ski", "lake", "infra", "rural")
    blds = []
    for k, p in enumerate(P.places):
        it = items[p["item"]]
        if it["category"] not in cats or (p["flags"] & 1):
            continue
        w, _, d = it["size"]
        if w < 1.5 or d < 1.5:
            continue
        blds.append((k, p, it, Placer_corners(p, w, d)))
    print("bâtiments et gros objets :", len(blds))
    bad = []
    grid = {}
    for b in blds:
        key = (int(b[1]["x"] // 40), int(b[1]["z"] // 40))
        grid.setdefault(key, []).append(b)
    for b in blds:
        kx, kz = int(b[1]["x"] // 40), int(b[1]["z"] // 40)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for o in grid.get((kx + dx, kz + dz), ()):
                    if o[0] <= b[0]:
                        continue
                    a = boxes_overlap_area(b[3], o[3])
                    if a > 0.05:
                        bad.append((a, b[1]["item"], o[1]["item"], b[1]["x"], b[1]["z"]))
    bad.sort(reverse=True)
    print("recouvrements de bâtiments (boîtes avec débords) :", len(bad))
    for r in bad[:25]:
        print("  %6.1f m²  %-22s %-22s  (%.0f, %.0f)" % r)

    # 2. Objets en double (même modèle à moins de 0,5 m)
    dup = 0
    seen = {}
    for p in P.places:
        key = (p["item"], round(p["x"] / 0.5), round(p["z"] / 0.5))
        if key in seen:
            dup += 1
        seen[key] = 1
    print("objets en double :", dup)

    # 3. Chaussées qui se chevauchent hors des carrefours
    print("chevauchements de chaussées :")
    for i, a in enumerate(edges):
        for b in edges[i + 1:]:
            shared = {a["a"], a["b"]} & {b["a"], b["b"]}
            Pa, Pb = a["pts"], b["pts"]
            # échantillons de a, distance à b
            worst = []
            for k in range(0, len(Pa), 2):
                p = Pa[k]
                # loin des nœuds communs : au-delà de ce qu'un carrefour couvre (rayon 15 m)
                near_node = any(math.hypot(p[0] - nodes[n]["x"], p[1] - nodes[n]["z"]) < 15.0 for n in shared)
                if near_node:
                    continue
                dist, j, t = L.point_polyline(p[:2], Pb)
                lim = a["hw"] + a["sidewalk"] + b["hw"] + b["sidewalk"] + 2.0
                if dist < lim:
                    hb = Pb[j][2] + t * (Pb[j + 1][2] - Pb[j][2])
                    if abs(hb - p[2]) < 4.0:
                        worst.append((dist, k, a["s"][k], p[0], p[1], p[2] - hb))
            if worst:
                ss = [w[2] for w in worst]
                print("  %-16s / %-16s : %3d points, s %.0f..%.0f, écart axe min %.1f m (lim %.1f), dh %.1f, vers (%.0f, %.0f)" % (
                    a["id"], b["id"], len(worst), min(ss), max(ss), min(w[0] for w in worst),
                    a["hw"] + a["sidewalk"] + b["hw"] + b["sidewalk"] + 2.0, worst[0][5], worst[0][3], worst[0][4]))

    # 4. Eau : terrain à moins de 0,4 m du plan d'eau dans la zone couverte par la nappe
    for i, lk in enumerate(D.LAKES):
        wm = water == i + 1
        near = L.blur2d(wm.astype(float), 2.0) > 0.01
        close = near & (np.abs(Hc - lk["level"]) < 0.3)
        print("eau %-8s : %d cellules de terrain à moins de 0,4 m du plan d'eau (%.1f ha)" % (lk["id"], close.sum(), close.sum() * 16 / 1e4))

    # 5. Terrain au-dessus de l'accotement : là où le sol remonte trop près du bord (grille de 4 m)
    over = 0
    for e in edges:
        Pp = e["pts"]
        t = L.tangents(Pp)
        nrm = np.stack([-t[:, 1], t[:, 0]], axis=1)
        for side in (1, -1):
            off = e["hw"] + e["sidewalk"] + 0.9
            x = Pp[:, 0] + nrm[:, 0] * side * off
            z = Pp[:, 1] + nrm[:, 1] * side * off
            h = g.sample(Hc, x, z)
            over += int(np.sum(h > Pp[:, 2] - 0.12))
    print("points d'accotement recouverts par le terrain :", over)


def Placer_corners(p, w, d):
    return PL.Placer.corners(p["x"], p["z"], w, d, p["yaw"])


if __name__ == "__main__":
    main()
