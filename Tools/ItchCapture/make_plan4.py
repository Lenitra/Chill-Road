import json
def v(x,y,z): return {"x":x,"y":y,"z":z}
shots = [
 dict(name="v4_events_hw", road="Highway", car=1, hour=16.0, traffic=1.2, events=4.0, drive="weave", kmh=200, cam="chase", ui=True, trigger="event", count=10, interval=2.5, warmup=4, timeout=120),
 dict(name="v4_events_country", road="Country", car=0, hour=17.3, traffic=1.0, events=4.0, drive="cruise", kmh=150, cam="chase", ui=True, trigger="event", count=8, interval=2.5, warmup=4, timeout=100),
 dict(name="v4_police_tele", road="Highway", car=2, hour=19.0, traffic=1.0, drive="weave", kmh=140, cam="rel", camOffset=v(1.6,1.4,9.0), lookOffset=v(0,0.5,-12.0), fov=30, fx=False, ui=True, police=True, trigger="police", triggerDist=30, count=6, interval=1.2, warmup=3, timeout=90, hero=True),
]
json.dump({"seed": 9090, "shots": shots}, open("plan4.json","w"), indent=1)
print(len(shots))
