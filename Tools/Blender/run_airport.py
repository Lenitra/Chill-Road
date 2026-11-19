"""Aéroport : construit, contrôle, exporte les modèles. Sans interface :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_airport.py [-- dry]
Ensuite : python Tools/Map/map_manifest.py, puis dans Unity « Chill Road > Importer le pack alpin »."""
import os, sys
import bpy
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
import alp_manifest_keep as K
A = alp_all.load()
import alp_airport
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Models", "AlpPack"))
dry = "dry" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False
ids = alp_airport.IDS
print("TRIS", A.build_all(ids))
print("CHECK", A.check(ids, lods=(0, 1)))
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
if not dry:
    print("EXPORT", A.export_all(ids, folder=PACK))
    print("MANIFEST", K.write(A, PACK))
    bpy.ops.wm.save_mainfile()
