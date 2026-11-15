import json, sys
def v(x,y,z): return {"x":x,"y":y,"z":z}
shots = [
 dict(name="hw_weave", road="Highway", car=0, hour=15.5, traffic=1.5, drive="weave", kmh=225, cam="chase", ui=True, trigger="approach", count=6, interval=2.0, warmup=10, timeout=60),
 dict(name="hw_supra_golden", road="Highway", car=1, hour=18.3, traffic=1.5, drive="weave", kmh=225, cam="chase", ui=True, trigger="nearmiss", count=5, interval=2.0, warmup=10, timeout=60),
 dict(name="cover_rel", road="Highway", car=1, hour=18.6, traffic=1.6, drive="weave", kmh=210, cam="rel", camOffset=v(-2.4,1.15,6.8), lookOffset=v(0,0.45,-0.8), fov=38, fx=False, ui=False, trigger="approach", count=5, interval=1.5, warmup=10, timeout=50, hero=True),
 dict(name="cover_rel_r", road="Highway", car=1, hour=18.6, traffic=1.6, drive="weave", kmh=210, cam="rel", camOffset=v(2.4,1.15,6.8), lookOffset=v(0,0.45,-0.8), fov=38, fx=False, ui=False, trigger="approach", count=5, interval=1.5, warmup=10, timeout=50, hero=True),
 dict(name="cover_rear", road="Highway", car=1, hour=18.6, traffic=1.6, drive="weave", kmh=215, cam="rel", camOffset=v(2.3,1.2,-6.8), lookOffset=v(0,0.6,3.5), fov=44, fx=False, ui=False, trigger="approach", count=5, interval=1.5, warmup=10, timeout=50, hero=True),
 dict(name="country_oncoming", road="Country", car=3, hour=18.9, traffic=1.4, drive="oncoming", kmh=165, cam="chase", ui=True, trigger="approach", count=6, interval=1.5, warmup=8, timeout=60),
 dict(name="trackside_sunset", road="Highway", car=1, hour=19.0, traffic=1.4, drive="weave", kmh=200, cam="trackside", lead=90, side=1, height=0.7, lookOffset=v(0,0.6,0), fov=28, fx=False, ui=False, trigger="distance", triggerDist=20, count=4, interval=1.0, warmup=8, timeout=60, hero=True),
 dict(name="cine_mustang", road="Country", car=3, hour=17.8, traffic=1.2, drive="cruise", kmh=140, cam="rel", camOffset=v(-3.2,0.9,6.5), lookOffset=v(0,0.6,0.5), fov=45, fx=False, ui=False, trigger="time", count=4, interval=3, warmup=6, timeout=40, hero=True),
 dict(name="police", road="Highway", car=2, hour=20.4, traffic=1.4, drive="weave", kmh=165, cam="rel", camOffset=v(2.4,1.6,11.0), lookOffset=v(0,0.5,-7.0), fov=42, fx=False, ui=True, police=True, trigger="police", triggerDist=35, count=5, interval=1.5, warmup=4, timeout=70, hero=True),
 dict(name="night_hw", road="Highway", car=0, hour=22.5, traffic=1.8, drive="weave", kmh=215, cam="chase", ui=True, trigger="approach", count=5, interval=2.0, warmup=10, timeout=60),
 dict(name="aerial_roundabout", road="Roundabout", car=2, hour=16.5, traffic=1.4, drive="cruise", kmh=110, cam="aerial", camOffset=v(-40,48,-50), lookOffset=v(0,0,0), fov=45, fx=False, ui=False, trigger="feature", triggerDist=50, count=3, interval=1.2, warmup=2, timeout=40, hero=True),
 dict(name="dirt_dust", road="Dirt", car=3, hour=18.2, traffic=1.0, drive="cruise", kmh=115, cam="rel", camOffset=v(2.8,1.0,-5.8), lookOffset=v(0,0.7,2.5), fov=50, fx=False, ui=False, trigger="time", count=4, interval=2.5, warmup=6, timeout=40, hero=True),
 dict(name="lineup", road="Country", car=0, hour=18.4, traffic=0, drive="none", cam="lineup", camOffset=v(-7.5,1.3,11.5), lookOffset=v(0,0.6,0), fov=36, fx=False, ui=False, count=3, hero=True),
 dict(name="hw_bumper", road="Highway", car=0, hour=12.5, traffic=1.6, drive="weave", kmh=230, cam="bumper", ui=True, trigger="approach", count=3, interval=2.0, warmup=10, timeout=45),
 dict(name="roadworks", road="Roadworks", car=1, hour=16.0, traffic=1.4, drive="weave", kmh=150, cam="chasefar", ui=True, trigger="feature", triggerDist=70, count=3, interval=1.0, warmup=2, timeout=40),
]
if len(sys.argv) > 2:
    keep = sys.argv[2].split(",")
    shots = [s for s in shots if s["name"] in keep]
json.dump({"seed": int(sys.argv[1]) if len(sys.argv) > 1 else 1234, "shots": shots}, open("plan.json","w"), indent=1)
print(len(shots), "shots")
