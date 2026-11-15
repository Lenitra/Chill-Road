import json
def v(x,y,z): return {"x":x,"y":y,"z":z}
A = [
 dict(name="g_jesko_weave", road="Highway", car=7, hour=15.3, traffic=1.6, drive="weave", kmh=270, cam="chase", ui=True, trigger="nearmiss", minKmh=200, count=6, interval=1.8, warmup=10, timeout=70),
 dict(name="g_valkyrie_sunset", road="Highway", car=8, hour=19.2, traffic=1.6, drive="weave", kmh=260, cam="chase", ui=True, trigger="nearmiss", minKmh=190, count=5, interval=1.8, warmup=10, timeout=70),
 dict(name="g_storm_hw", road="Highway", car=1, hour=16.5, weather="storm", traffic=1.5, drive="weave", kmh=200, cam="chase", ui=True, trigger="nearmiss", minKmh=150, count=5, interval=2.0, warmup=10, timeout=70),
 dict(name="g_storm_cine", road="Country", car=6, hour=17.5, weather="storm", traffic=1.2, drive="cruise", kmh=130, cam="rel", camOffset=v(-2.8,1.1,6.4), lookOffset=v(0,0.6,0), fov=42, fx=False, trigger="time", count=4, interval=2.5, warmup=6, timeout=40, hero=True),
 dict(name="g_oncoming_ae86", road="Country", car=5, hour=18.6, traffic=1.4, drive="oncoming", kmh=160, cam="chase", ui=True, trigger="headon", minKmh=120, count=5, interval=1.5, warmup=8, timeout=70),
 dict(name="g_police", road="Highway", car=3, hour=19.0, traffic=1.0, drive="weave", kmh=140, cam="rel", camOffset=v(1.6,1.4,9.0), lookOffset=v(0,0.5,-12.0), fov=30, fx=False, ui=True, police=True, trigger="police", triggerDist=30, count=6, interval=1.2, warmup=3, timeout=90, hero=True),
 dict(name="g_events", road="Highway", car=0, hour=16.0, traffic=1.2, events=4.0, drive="weave", kmh=200, cam="chase", ui=True, trigger="event", count=8, interval=2.5, warmup=4, timeout=110),
 dict(name="g_dirt_ae86", road="Dirt", car=5, hour=17.2, traffic=1.0, drive="cruise", kmh=110, cam="chase", ui=True, trigger="time", count=4, interval=2.5, warmup=6, timeout=40),
 dict(name="g_p50_trucks", road="Highway", car=4, hour=16.8, traffic=1.8, drive="cruise", kmh=60, cam="rel", camOffset=v(2.4,1.3,-5.2), lookOffset=v(0,0.4,4.0), fov=46, fx=False, ui=True, trigger="approach", minKmh=40, count=5, interval=2.0, warmup=6, timeout=60, hero=True),
]
def portrait(i, name, near=False):
    off = v(-1.7,0.9,4.0) if near else v(-2.7,1.2,6.4)
    return dict(name=f"p_{i}_{name}", road="Highway", car=i, hour=17.4, traffic=0.6, drive="cruise", kmh=60 if near else 150,
                cam="rel", sunLit=True, camOffset=off, lookOffset=v(0,0.45,-0.6), fov=36, fx=False, trigger="time", count=2, interval=2.0, warmup=5, timeout=30, hero=True)
names = ["vision74", "supra", "mx5", "mustang", "p50", "ae86", "delorean", "jesko", "valkyrie"]
B = [portrait(i, n, near=(n == "p50")) for i, n in enumerate(names)] + [
 dict(name="s_lineup", road="Highway", car=0, hour=17.9, traffic=0, drive="none", cam="lineup", camOffset=v(-9.0,2.4,17.0), lookOffset=v(0,0.3,-1.5), fov=40, fx=False, count=3, hero=True),
 dict(name="s_roundabout", road="Roundabout", car=2, hour=16.5, traffic=1.4, drive="cruise", kmh=110, cam="aerial", camOffset=v(-40,48,-50), lookOffset=v(0,0,0), fov=45, fx=False, trigger="feature", triggerDist=50, count=3, interval=1.2, warmup=2, timeout=40, hero=True),
 dict(name="s_overpass", road="Overpass", car=7, hour=16.2, traffic=1.5, drive="weave", kmh=200, cam="aerial", camOffset=v(-45,40,-55), lookOffset=v(0,0,0), fov=45, fx=False, trigger="feature", triggerDist=60, count=3, interval=1.0, warmup=2, timeout=40, hero=True),
 dict(name="s_drone", road="Country", car=3, hour=16.8, traffic=1.4, drive="cruise", kmh=140, cam="rel", camOffset=v(-6,16,-20), lookOffset=v(0,0,22), fov=50, fx=False, trigger="time", count=3, interval=3, warmup=6, timeout=40, hero=True),
 dict(name="s_cover_valkyrie", road="Highway", car=8, hour=17.4, traffic=1.5, drive="weave", kmh=220, cam="rel", sunLit=True, camOffset=v(-2.6,1.25,6.6), lookOffset=v(0,0.5,-1.0), fov=36, fx=False, trigger="approach", minKmh=150, count=5, interval=1.5, warmup=8, timeout=50, hero=True),
 dict(name="s_cover_jesko", road="Highway", car=7, hour=17.4, traffic=1.5, drive="weave", kmh=220, cam="rel", sunLit=True, camOffset=v(-2.6,1.25,6.6), lookOffset=v(0,0.5,-1.0), fov=36, fx=False, trigger="approach", minKmh=150, count=5, interval=1.5, warmup=8, timeout=50, hero=True),
]
json.dump({"seed": 5151, "shots": A}, open("plan5a.json", "w"), indent=1)
json.dump({"seed": 6262, "shots": B}, open("plan5b.json", "w"), indent=1)
print(len(A), len(B))
