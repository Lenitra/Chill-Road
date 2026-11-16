"""Concession automobile : construit, contrôle (z-fighting), exporte. Sans interface :
E:\blender\blender.exe -b E:\ChillRoad\Tools\Blender\AlpPack.blend --python run_dealer.py [-- dry]
« dry » : construit et contrôle seulement (ni export, ni manifeste, ni sauvegarde)."""
import sys
import bpy
sys.path.insert(0, r"E:\ChillRoad\Tools\Blender")
import alp_all
A = alp_all.load()
dry = "dry" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False
ids = ["car_showroom", "dealer_sign", "car_podium"]
print("TRIS", A.build_all(ids))
print("LEFT", {i: A.ITEMS[i].get("_left") for i in ids})
print("CHECK", A.check(ids, lods=(0, 1, 2)))
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
if not dry:
    print("EXPORT", A.export_all(ids))
    print("MANIFEST", A.write_manifest())
    bpy.ops.wm.save_mainfile()
