"""Plan de tournage de la bande-annonce : écrit Tools/Trailer/plan.json (lu par TrailerDirector.cs).

    python Tools/Trailer/make_plan.py

Chaque plan est filmé un peu plus long que sa place au montage (edit.py choisit le début et la durée).
Repères de la carte : Tools/Map, Assets/ChillRoad/Resources/Map/map.json (x, z en mètres).
Soleil : à l'est le matin (+x), à l'ouest le soir (-x) ; lumière du soir 18,3-18,6 h, nuit noire après 20 h.
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent


def V(x, y, z):
    return {"x": x, "y": y, "z": z}


shots = []


def shot(name, **kw):
    kw["name"] = name
    shots.append(kw)


# ---------------------------------------------------------------- ouverture : le massif au matin
shot("a1_village_haut", car="twingo", paint=9, x=300, z=-1810, hour=7.8, speed=35, drive=4, len=5.4, traffic=0.6,
     world=True, track=True, move=True, cam=V(-80, 60, -90), cam2=V(-66, 44, -76), look=V(0, 0, 0), look2=V(0, 0, 0), fov=45)
shot("a2_col_lac", car="miata", kind="Mountain", x=50, z=420, hour=8.4, speed=50, drive=5, len=5.4, traffic=0.5,
     world=True, track=True, move=True, cam=V(70, 25, -50), cam2=V(58, 15, -40), look=V(0, 0, 0), look2=V(0, 0, 0), fov=42)
# ---------------------------------------------------------------- la route : village, lac, col, tunnel
shot("b1_village", car="twingo", paint=9, x=300, z=-1810, hour=10.5, speed=34, drive=4, len=3.2, traffic=0.4,
     cam=V(-3.2, 1.2, 7.5), look=V(0, 0.7, 0), move=True, cam2=V(-3.6, 1.3, 6.2), look2=V(0, 0.7, 0), fov=40)
shot("b2_rives", car="supra", kind="Mountain", x=1450, z=-1900, hour=16.6, speed=75, drive=5, len=3.4, traffic=0.5,
     world=True, track=True, move=True, cam=V(-22, 6, 34), cam2=V(-16, 4, 27), look=V(0, 0, 0), look2=V(0, 0, 0), fov=28)
shot("b3_lacets", car="ae86", kind="Mountain", x=60, z=200, hour=16.4, speed=62, drive=5, len=3.4, traffic=0.5, lat=8.5,
     world=True, track=True, move=True, cam=V(-22, 16, -30), cam2=V(-15, 10, -22), look=V(0, 0, 0), look2=V(0, 0, 0), fov=38)
shot("b4_tunnel", car="valkyrie", kind="Highway", span="tunnel", x=0, z=0, spanOffset=-110, lane=1, hour=15.5, speed=190, drive=6, len=3.6,
     traffic=1.0, cam=V(1.6, 1.25, -7.5), look=V(0, 1.1, 30), fov=58, fov2=64, blur=True)
# ---------------------------------------------------------------- temps fort 1 : le jeu
for tag, seed in (("a", 3), ("b", 8), ("c", 21)):
    shot(f"c1_trafic_{tag}", car="jesko", kind="Highway", x=1500, z=-1500, lane=1, hour=16.6, speed=208, speed0=120, drive=14, len=6.5,
         traffic=2.3, pack=16, first=125, gap=38, weave=True, hud=True, blur=True, seed=seed)
DRIFT = dict(car="mustang", mode="drift", cx=1772, cz=-893, radius=20, turn=1, speed=45, slip=28, assist=0.6, tires=4, engine=3,
             hour=17.4, drive=9, len=5.0, traffic=0.3)
# a : caméra devant la voiture, dans l'axe de sa trajectoire ; b : caméra au centre du cercle (le pylône passe dans le champ)
shot("c2_drift_a", cam=V(-4.5, 1.0, 8), look=V(0, 0.6, 0), fov=42, velFrame=True, follow=0.25, **DRIFT)
shot("c2_drift_b", useEye=True, eye=V(1774.5, 6.9, -893), look=V(0, 0.5, 0), fov=36, shake=0.2, **DRIFT)
shot("c3_saut", car="gryaris", stunt="port", runup=110, speed=150, hour=17.0, drive=2.4, len=3.6, traffic=0.3,
     world=True, local=True, track=True, cam=V(9, 1.3, 38), look=V(0, 0.6, 0), fov=40)
shot("c4_orage", car="supra", kind="Highway", x=-1000, z=1500, lane=1, hour=17, storm=True, speed=170, speed0=115, drive=14, len=3.8,
     traffic=2.3, pack=12, first=110, gap=34, weave=True, hug=0.3, cam=V(-2.2, 0.9, -7.5), look=V(0, 1.0, 20), fov=62, blur=True, seed=5)
shot("c5_neige", car="gryaris", race="station_tours", along=90, hour=14.5, speed=70, lat=6, tires=3, drive=5, len=3.6, traffic=0.4,
     cam=V(-3.6, 1.2, 8), look=V(0, 0.7, 0), move=True, cam2=V(-4.4, 1.4, 6.5), look2=V(0, 0.7, 0), fov=42, follow=0.5)
shot("c6_course", car="a110", race="col", raceStart=True, hour=16.2, speed=140, lat=9, drive=0.3, len=5.5, traffic=0.4, hud=True, blur=True)
shot("c7_nuit", car="vision", x=250, z=-1780, hour=20.6, speed=45, drive=4, len=3.6, neon=2, flame=1, traffic=0.8,
     cam=V(-3.4, 0.9, -6.5), look=V(0, 0.8, 6), move=True, cam2=V(-3.9, 1.1, -5.4), look2=V(0, 0.8, 6), fov=48)
# ---------------------------------------------------------------- temps fort 2 : le contenu
for i, car in enumerate(["miata", "ae86", "dmc12", "mustang", "a110", "focusst", "valkyrie", "vision"]):
    shot(f"d1_voiture_{i}_{car}", car=car, kind="Main", x=1790, z=120, hour=13.0, speed=70, drive=3, len=0.9, traffic=0.3,
         cam=V(-4.2, 1.25, 8.2), look=V(0, 0.7, 0), fov=38)
shot("d2_garage", car="supra", x=250, z=-1780, hour=15, speed=30, drive=3, len=4.2, menu="garage", step=0.5)
shot("d3_tuning", car="supra", x=250, z=-1780, hour=15, speed=30, drive=3, len=3.0, menu="tuning")
shot("d4_carte", car="supra", x=250, z=-1780, hour=15, speed=30, drive=3, len=3.0, menu="carte")
shot("d5_jour_nuit", car="twingo", paint=9, x=300, z=-1810, hour=16.4, hour2=20.7, speed=35, drive=4, len=4.2, traffic=1.6,
     world=True, cam=V(70, 45, 80), look=V(0, 0, 0), fov=45)
shot("d6_aeroport", car="jesko", line=True, x=500, z=1955, tox=1330, toz=1881, along=330, hour=17.4, speed=250, drive=4.5, len=3.2, traffic=0.3,
     world=True, track=True, cam=V(140, 1.7, 10), look=V(0, 0.5, 0), fov=34, shake=0.2, blur=True)
shot("d7_terre", car="a110", kind="Track", x=-1740, z=120, hour=14, speed=75, lat=7, tires=1, drive=5, len=3.4, traffic=0.3,
     cam=V(3.4, 1.0, 8.5), look=V(0, 0.6, 0), fov=40, follow=0.4)
shot("d8_peel", car="p50", kind="Main", x=700, z=-1700, hour=15, speed=45, drive=4, len=3.0, traffic=1.8,
     cam=V(-2.4, 1.0, 4.5), look=V(0, 0.5, 0), fov=40)
# ---------------------------------------------------------------- fin : la route du lac au couchant, cap à l'ouest, puis le logo
for tag, hour in (("a", 18.3),):
    shot("e1_couchant", car="mustang", kind="Mountain", x=1500, z=-1900, dir=-1, hour=hour, speed=70, drive=5, len=5.5, traffic=0.5, lights=1,
         cam=V(-2.8, 1.5, -7.5), look=V(0, 1.0, 8), move=True, cam2=V(-1.0, 2.8, -11), look2=V(0, 1.3, 12), fov=45, fov2=40, follow=0.5)
shot("e2_fond_logo", car="mustang", kind="Mountain", x=1500, z=-1900, dir=-1, hour=18.3, speed=70, drive=9, len=9.0, traffic=0.5, lights=1,
     cam=V(-1.0, 2.8, -11), look=V(0, 1.3, 12), move=True, cam2=V(0, 9, -22), look2=V(0, 3, 20), fov=40, follow=0.8, linear=True)

(HERE / "plan.json").write_text(json.dumps({"shots": shots}, indent=1), encoding="utf-8")
print(len(shots), "plans,", round(sum(s.get("len", 4) for s in shots), 1), "s filmées")
