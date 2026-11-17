"""Construit et exporte la balise de virage (sans interface) :
E:\blender\blender.exe -b E:\ChillRoad\Tools\Blender\AlpPack.blend --python run_chevron.py"""
import sys
import bpy
sys.path.insert(0, r"E:\ChillRoad\Tools\Blender")
import alp_all
A = alp_all.load()
ids = ["chevron"]
print("TRIS", A.build_all(ids))
print("EXPORT", A.export_all(ids))
print("MANIFEST", A.write_manifest())
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
bpy.ops.wm.save_mainfile()
