"""Écran partagé : plans cinéma des deux voitures côte à côte (joueur 2 dans la voie de droite, 6 m derrière)."""
import json


def v(x, y, z):
    return {"x": x, "y": y, "z": z}


shots = [
    dict(name="sp_duo_rear", road="Highway", car=7, car2=8, hour=17.4, traffic=0.8, drive="cruise", drive2="pair", kmh=170, cam="rel",
         camOffset=v(1.9, 2.4, -12.0), lookOffset=v(1.9, 0.6, 4.0), fov=42, fx=False, trigger="time", count=5, interval=2.5, warmup=10,
         timeout=40, hero=True),
    dict(name="sp_duo_front", road="Highway", car=1, car2=3, hour=17.6, traffic=0.8, drive="cruise", drive2="pair", kmh=160, cam="rel",
         camOffset=v(1.4, 1.3, 11.0), lookOffset=v(1.9, 0.5, -3.5), fov=44, fx=False, trigger="time", count=5, interval=2.5, warmup=10,
         timeout=40, hero=True),
    dict(name="sp_duo_drone_hw", road="Highway", car=6, car2=5, hour=16.8, traffic=1.0, drive="cruise", drive2="pair", kmh=160, cam="rel",
         camOffset=v(1.9, 13.0, -18.0), lookOffset=v(1.9, 0.0, 10.0), fov=46, fx=False, trigger="time", count=4, interval=3.0, warmup=10,
         timeout=40, hero=True),
]
json.dump({"seed": 9595, "split": True, "shots": shots}, open("plan6b.json", "w"), indent=1)
print(len(shots))
