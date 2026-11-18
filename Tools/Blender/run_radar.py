"""Radar de vitesse : construit, contrôle (z-fighting), exporte. Sans interface :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_radar.py [-- dry] [render=<dossier>]
« dry » : construit et contrôle seulement (ni export, ni manifeste). « render=<dossier> » : rend aussi une image.
Le .blend n'est pas enregistré : ce script reconstruit le modèle, et seule la fiche du radar change dans le manifeste.
Ensuite : python Tools/Map/map_manifest.py, puis dans Unity « Chill Road > Importer le pack alpin »."""
import json, os, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
A = alp_all.load()
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Models", "AlpPack"))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
dry = "dry" in args
ids = ["speed_camera"]
print("TRIS", A.build_all(ids))
print("LEFT", {i: A.ITEMS[i].get("_left") for i in ids})
print("CHECK", A.check(ids, lods=(0, 1, 2)))
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
for a in args:
    if a.startswith("render="):
        A.RENDERS = a[7:]
        print("RENDER", A.sheet(ids, "speed_camera", az=-58, el=12, res=(900, 1200), fill=0.86))
if not dry:
    print("EXPORT", A.export_all(ids, folder=PACK))
    with tempfile.TemporaryDirectory() as tmp:
        A.write_manifest(folder=tmp)
        with open(os.path.join(tmp, "AlpPack_manifest.json"), encoding="utf-8") as fh:
            fresh = {e["id"]: e for e in json.load(fh)["items"]}
    path = os.path.join(PACK, "AlpPack_manifest.json")
    with open(path, encoding="utf-8") as fh:
        man = json.load(fh)
    man["items"] = [e for e in man["items"] if e["id"] not in ids] + [fresh[i] for i in ids]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(man, fh, indent=1, ensure_ascii=False)
    print("MANIFEST", len(man["items"]))
