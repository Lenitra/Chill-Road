import json
def v(x,y,z): return {"x":x,"y":y,"z":z}
shots = [
 dict(name="v3_sunset_ui", road="Highway", car=0, hour=19.2, traffic=1.6, drive="weave", kmh=230, cam="chase", ui=True, trigger="nearmiss", minKmh=165, count=5, interval=1.8, warmup=10, timeout=70),
 dict(name="v3_police_front", road="Highway", car=2, hour=19.0, traffic=1.1, drive="weave", kmh=140, cam="rel", camOffset=v(1.9,1.5,8.5), lookOffset=v(0,0.6,-6.0), fov=44, fx=False, ui=True, police=True, trigger="police", triggerDist=16, count=5, interval=1.0, warmup=3, timeout=80, hero=True),
 dict(name="v3_supra_ui", road="Highway", car=1, hour=16.4, traffic=1.6, drive="weave", kmh=235, cam="chase", ui=True, trigger="nearmiss", minKmh=160, count=5, interval=1.8, warmup=10, timeout=70),
 dict(name="v3_sunset_cine", road="Country", car=3, hour=19.1, traffic=1.2, drive="cruise", kmh=135, cam="rel", camOffset=v(-3.0,1.0,6.4), lookOffset=v(0,0.6,0), fov=42, fx=False, trigger="time", count=4, interval=2.5, warmup=6, timeout=40, hero=True),
 dict(name="v3_dirt_ui", road="Dirt", car=0, hour=17.4, traffic=1.0, drive="cruise", kmh=110, cam="chase", ui=True, trigger="time", count=4, interval=2.5, warmup=6, timeout=40),
]
json.dump({"seed": 4242, "shots": shots}, open("plan3.json","w"), indent=1)
print(len(shots))
