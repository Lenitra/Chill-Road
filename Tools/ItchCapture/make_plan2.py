import json, sys
def v(x,y,z): return {"x":x,"y":y,"z":z}
shots = [
 dict(name="v2_hw_weave", road="Highway", car=0, hour=15.3, traffic=1.5, drive="weave", kmh=235, cam="chase", ui=True, trigger="nearmiss", minKmh=175, count=6, interval=2.0, warmup=10, timeout=70),
 dict(name="v2_cover_a", road="Highway", car=1, hour=17.4, traffic=1.5, drive="weave", kmh=215, cam="rel", sunLit=True, camOffset=v(-2.6,1.3,6.6), lookOffset=v(0,0.5,-1.0), fov=36, fx=False, trigger="time", count=8, interval=2.2, warmup=8, timeout=40, hero=True),
 dict(name="v2_cover_b", road="Highway", car=1, hour=18.0, traffic=1.6, drive="weave", kmh=215, cam="rel", sunLit=True, camOffset=v(-2.4,1.1,-6.8), lookOffset=v(0,0.6,3.0), fov=40, fx=False, trigger="approach", minKmh=160, count=6, interval=1.5, warmup=8, timeout=50, hero=True),
 dict(name="v2_supra_ui", road="Highway", car=1, hour=17.6, traffic=1.6, drive="weave", kmh=235, cam="chase", ui=True, trigger="approach", minKmh=180, count=6, interval=1.8, warmup=10, timeout=70),
 dict(name="v2_oncoming", road="Country", car=3, hour=17.0, traffic=1.4, drive="oncoming", kmh=170, cam="chase", ui=True, trigger="headon", minKmh=130, count=5, interval=1.5, warmup=8, timeout=70),
 dict(name="v2_trackside", road="Highway", car=1, hour=17.8, traffic=1.4, drive="weave", kmh=210, cam="trackside", lead=90, side=1, height=1.1, lookOffset=v(0,0.6,0), fov=30, fx=False, trigger="distance", triggerDist=22, count=5, interval=1.0, warmup=8, timeout=60, hero=True),
 dict(name="v2_police", road="Highway", car=2, hour=19.25, traffic=1.2, drive="weave", kmh=150, cam="police", camOffset=v(-1.8,1.7,-7.5), lookOffset=v(0,0.6,0), fov=45, fx=False, ui=True, police=True, trigger="police", triggerDist=22, count=6, interval=1.2, warmup=3, timeout=80, hero=True),
 dict(name="v2_dusk_hw", road="Highway", car=0, hour=20.9, traffic=1.6, drive="weave", kmh=215, cam="chase", ui=True, trigger="approach", minKmh=160, count=5, interval=2.0, warmup=10, timeout=60),
 dict(name="v2_dusk_cine", road="Country", car=2, hour=20.9, traffic=1.2, drive="cruise", kmh=130, cam="rel", camOffset=v(-2.8,1.0,6.2), lookOffset=v(0,0.5,0), fov=42, fx=False, trigger="time", count=4, interval=2.5, warmup=6, timeout=40, hero=True),
 dict(name="v2_drone", road="Country", car=3, hour=16.8, traffic=1.4, drive="cruise", kmh=140, cam="rel", camOffset=v(-6,16,-20), lookOffset=v(0,0,22), fov=50, fx=False, trigger="time", count=4, interval=3, warmup=6, timeout=40, hero=True),
 dict(name="v2_overpass", road="Overpass", car=0, hour=16.2, traffic=1.5, drive="weave", kmh=180, cam="aerial", camOffset=v(-45,40,-55), lookOffset=v(0,0,0), fov=45, fx=False, trigger="feature", triggerDist=60, count=3, interval=1.0, warmup=2, timeout=40, hero=True),
 dict(name="v2_lineup", road="Highway", car=0, hour=17.9, traffic=0, drive="none", cam="lineup", camOffset=v(-7.5,1.4,12.0), lookOffset=v(0,0.6,0), fov=36, fx=False, count=3, hero=True),
]
json.dump({"seed": int(sys.argv[1]) if len(sys.argv) > 1 else 777, "shots": shots}, open("plan2.json","w"), indent=1)
print(len(shots), "shots")
