"""Plan écran partagé (2 joueurs) pour la page itch.io."""
import json


def v(x, y, z):
    return {"x": x, "y": y, "z": z}


# Garage : 0 Vision 74, 1 Supra, 2 MX-5, 3 Mustang, 4 P50, 5 AE86, 6 DeLorean, 7 Jesko, 8 Valkyrie
shots = [
    dict(name="sp_highway_day", road="Highway", car=7, car2=1, hour=15.5, traffic=1.6, drive="weave", kmh=230, cam="chase", ui=True,
         trigger="nearmiss", minKmh=150, count=6, interval=2.0, warmup=10, timeout=80),
    dict(name="sp_sunset", road="Highway", car=8, car2=3, hour=19.2, traffic=1.6, drive="weave", kmh=220, cam="chase", ui=True,
         trigger="nearmiss", minKmh=150, count=5, interval=2.0, warmup=10, timeout=80),
    dict(name="sp_storm", road="Highway", car=6, car2=5, hour=16.5, weather="storm", traffic=1.5, drive="weave", kmh=200, cam="chase", ui=True,
         trigger="nearmiss", minKmh=130, count=5, interval=2.0, warmup=10, timeout=80),
    dict(name="sp_country", road="Country", car=2, car2=0, hour=17.6, traffic=1.2, drive="cruise", drive2="pair", kmh=140, cam="chase", ui=True,
         trigger="time", count=4, interval=3.0, warmup=8, timeout=40),
    dict(name="sp_duo_cine", road="Highway", car=7, car2=8, hour=17.4, traffic=1.0, drive="cruise", drive2="pair", kmh=170, cam="rel",
         sunLit=True, camOffset=v(-4.5, 1.6, 10.0), lookOffset=v(1.8, 0.5, -3.5), fov=40, fx=False, trigger="time", count=5, interval=2.5,
         warmup=8, timeout=40, hero=True),
    dict(name="sp_duo_drone", road="Country", car=3, car2=5, hour=17.2, traffic=1.0, drive="cruise", drive2="pair", kmh=130, cam="rel",
         camOffset=v(-5, 15, -22), lookOffset=v(0, 0, 14), fov=48, fx=False, trigger="time", count=4, interval=3.0, warmup=8, timeout=40,
         hero=True),
]
json.dump({"seed": 8484, "split": True, "shots": shots}, open("plan6.json", "w"), indent=1)
print(len(shots))
