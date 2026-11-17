"""Tremplins de cascade : construit, contrôle (z-fighting), exporte. Sans interface :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_stunt.py [-- dry]
« dry » : construit et contrôle seulement (ni export, ni manifeste, ni sauvegarde).
Ensuite : python Tools/Map/map_manifest.py, puis dans Unity « NoHesi > Importer le pack alpin »."""
import json, os, sys
import bpy
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
A = alp_all.load()
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "NoHesi", "Models", "AlpPack"))
dry = "dry" in sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else False
ids = ["stunt_ramp_stone", "stunt_ramp_road"]
RETIRED = {"stunt_ramp": "Stunt_Ramp"}     # tremplin « chantier » d'avant le 2026-11-17
print("TRIS", A.build_all(ids))
print("LEFT", {i: A.ITEMS[i].get("_left") for i in ids})
print("CHECK", A.check(ids, lods=(0, 1, 2)))
for iid in ids:
    lo, hi = A.bounds(iid)
    print("BOUNDS", iid, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
if not dry:
    for iid, f in RETIRED.items():
        col = bpy.data.collections.get("AP_" + iid)
        if col:
            for o in list(col.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(col)
        for ext in (".fbx", ".fbx.meta"):
            if os.path.exists(os.path.join(PACK, f + ext)):
                os.remove(os.path.join(PACK, f + ext))
    print("EXPORT", A.export_all(ids, folder=PACK))
    path = os.path.join(PACK, "AlpPack_manifest.json")
    with open(path, encoding="utf-8") as fh:
        before = json.load(fh)["items"]
    print("MANIFEST", A.write_manifest(folder=PACK))
    with open(path, encoding="utf-8") as fh:
        man = json.load(fh)
    # write_manifest oublie les fiches construites ailleurs (concession, atelier : leurs collections ne sont pas dans
    # ce fichier) : on les reprend telles quelles ; seules les fiches retirées disparaissent
    have = {e["id"] for e in man["items"]}
    man["items"] = [e for e in man["items"] if e["id"] not in RETIRED] +                    [e for e in before if e["id"] not in have and e["id"] not in RETIRED]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(man, fh, indent=1, ensure_ascii=False)
    bpy.ops.wm.save_mainfile()
