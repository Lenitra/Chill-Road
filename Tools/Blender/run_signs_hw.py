"""Construit et exporte les panneaux d'autoroute (sans interface) :
E:\\blender\\blender.exe -b E:\\ChillRoad\\Tools\\Blender\\AlpPack.blend --python run_signs_hw.py
Les textures de la palette ne sont pas réécrites (aucune couleur nouvelle)."""
import sys
import bpy
sys.path.insert(0, r"E:\ChillRoad\Tools\Blender")
import alp_all
A = alp_all.load()
ids = ["sign_exit", "sign_gore", "sign_motorway"]
tris = A.build_all(ids)
print("TRIS", tris)
print("EXPORT", A.export_all(ids))
print("MANIFEST", A.write_manifest())
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
bpy.ops.wm.save_mainfile()
