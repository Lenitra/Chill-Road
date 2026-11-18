"""Intérieurs des magasins (hall de la concession, atelier de tuning) : construit, contrôle, exporte. Sans interface :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_shops.py [-- dry]"""
import os, sys
import bpy
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
import alp_manifest_keep as K
A = alp_all.load()
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Models", "AlpPack"))
dry = "dry" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False
ids = ["showroom_bay_a", "showroom_bay_b", "showroom_bay_c", "tuning_workshop"]
print("TRIS", A.build_all(ids))
print("CHECK", A.check(ids, lods=(0,)))
if not dry:
    print("EXPORT", A.export_all(ids, folder=PACK))
    print("MANIFEST", K.write(A, PACK))
    bpy.ops.wm.save_mainfile()
