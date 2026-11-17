"""Garage de tuning : construit, contrôle (z-fighting), exporte. Sans interface :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_tuning.py [-- dry]
« dry » : construit et contrôle seulement (ni export, ni manifeste, ni sauvegarde)."""
import os, sys
import bpy
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
A = alp_all.load()
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Models", "AlpPack"))
dry = "dry" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False
ids = ["tuning_garage", "tuning_sign"]
print("TRIS", A.build_all(ids))
print("LEFT", {i: A.ITEMS[i].get("_left") for i in ids})
print("CHECK", A.check(ids, lods=(0, 1, 2)))
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
if not dry:
    print("EXPORT", A.export_all(ids, folder=PACK))
    print("MANIFEST", A.write_manifest(folder=PACK))
    bpy.ops.wm.save_mainfile()
