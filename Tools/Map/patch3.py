p='map_terrain.py'; s=open(p,encoding='utf-8').read()
old=s[s.index("    rim = D.RIM_HEIGHT"):s.index("    shape = (g.n, g.n)")]
new='''    f = 1.0 - L.smoothstep(0.0, D.RIM_WIDTH, e)
    rim = D.RIM_HEIGHT * f ** 1.5 + 0.5 * np.minimum(Hc, 600.0)
    Hc = Hc + (L.smax(Hc, rim, 80.0) - Hc) * L.smoothstep(0.0, 0.2, f)

'''
s=s.replace(old,new)
s+='''

def dam_line(g, H):
    """Crête du barrage : perpendiculaire à la vallée au site choisi, prolongée jusqu'aux versants (au-dessus de la
    crête). Renvoie (a, b) en xz."""
    v = next(v for v in D.VALLEYS if v["id"] == "sud")
    line = L.catmull(v["pts"], 12.0)
    site = D.DAM["site"]
    _, i, _ = L.point_polyline(site, line)
    t = line[min(i + 3, len(line) - 1), :2] - line[max(i - 3, 0), :2]
    t /= np.linalg.norm(t)
    nrm = np.array([-t[1], t[0]])
    ends = []
    for sgn in (-1, 1):
        d = 0.0
        while d < 500 and float(g.sample(H, site[0] + nrm[0] * sgn * d, site[1] + nrm[1] * sgn * d)) < D.DAM["crest"] + 1.0:
            d += 2.0
        d += 6.0
        ends.append((site[0] + nrm[0] * sgn * d, site[1] + nrm[1] * sgn * d))
    return ends[0], ends[1]


def cached(step=D.STEP, path="out/terrain_cache.npz"):
    """Relief naturel mis en cache (recalculé si map_def ou map_terrain ont changé)."""
    import hashlib, os, pickle
    here = os.path.dirname(os.path.abspath(__file__))
    key = hashlib.md5()
    src = open(os.path.join(here, "map_def.py"), "rb").read()
    key.update(src.split(b"# ===== ROUTES")[0])          # seule la partie relief compte
    key.update(open(os.path.join(here, "map_terrain.py"), "rb").read())
    key.update(str(step).encode())
    full = os.path.join(here, path.replace(".npz", "_%g.pkl" % step))
    if os.path.exists(full):
        k, data = pickle.load(open(full, "rb"))
        if k == key.hexdigest():
            g = L.Grid(D.HALF, step)
            return g, data[0], data[1]
    g, H, info = build(step)
    pickle.dump((key.hexdigest(), (H, info)), open(full, "wb"))
    return g, H, info
'''
open(p,'w',encoding='utf-8').write(s)
p='map_def.py'; s=open(p,encoding='utf-8').read()
s=s.replace('''level=250.0, depth=0, seed=(300, -1900),
         stops=[((330, -1525), (680, -1445))]),''','''level=250.0, depth=0, seed=(300, -1900), dam=True),''')
open(p,'w',encoding='utf-8').write(s)
p='t_terrain.py'; s=open(p).read()
s=s.replace('g,H,info=T.build(step=8.0)','g,H,info=T.cached(step=8.0)\ndam=T.dam_line(g,H); print("dam",dam)')
s=s.replace('m=T.flood(g,H,lk)','lk=dict(lk); lk["stops"]=lk.get("stops",[])+([dam] if lk.get("dam") else []); m=T.flood(g,H,lk)')
open(p,'w').write(s)
