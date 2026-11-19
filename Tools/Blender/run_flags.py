"""Drapeaux animés (2026-11-19) : reconstruit, contrôle et exporte les modèles qui portent une étoffe (Geo.cloth).
Sans interface :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_flags.py [-- dry]
Ensuite : python Tools/Map/map_manifest.py, puis dans Unity « Chill Road > Importer le pack alpin »."""
import os, sys
import bpy
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
import alp_manifest_keep as K
A = alp_all.load()
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Models", "AlpPack"))
dry = "dry" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False
ids = ["flagpole_red", "flagpole_blue", "school_flag", "firestation", "hotel_chalet", "hotel_grand", "refuge", "school",
       "townhall", "tuning_workshop", "summit_cross", "igloo", "slalom_gate_red", "slalom_gate_blue", "stunt_ramp_stone"]
print("TRIS", A.build_all(ids))
print("CHECK", A.check(ids, lods=(0,)))
for iid in ids:
    for ob in sorted(bpy.data.collections["AP_" + iid].objects, key=lambda o: o.name):
        me = ob.data
        uvs = [l.name for l in me.uv_layers]
        n = 0
        if "UVFlag" in uvs:
            ph = me.uv_layers["UVPhase"].data
            n = sum(1 for d in ph if d.uv[1] > 2.5)
        print("FLAG", ob.name, uvs, "loops marqués", n)
if not dry:
    print("EXPORT", A.export_all(ids, folder=PACK))
    print("MANIFEST", K.write(A, PACK))
    bpy.ops.wm.save_mainfile()
