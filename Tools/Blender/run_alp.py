"""Reconstruit le pack alpin sans interface (style cartoon), avec aperçus et export optionnels.

E:\\blender\\blender.exe -b E:\\ChillRoad\\Tools\\Blender\\AlpPack.blend --python run_alp.py -- [options] [ids...]
  --export        exporte les FBX + écrit le manifeste (garde les entrées étrangères et AP_Flora)
  --save          sauvegarde AlpPack.blend à la fin
  --sheet PNG     planche des objets demandés (niveau détaillé)
  --cat CAT       ajoute tous les objets d'une catégorie (répétable)
  ids             identifiants (défaut avec --all : tout le pack sauf FROZEN)
  --all           tout le pack
"""
import os
import sys
import time

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import alp_all  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
opts = {"export": False, "save": False, "sheet": None, "all": False, "cats": []}
ids = []
i = 0
while i < len(argv):
    a = argv[i]
    if a in ("--export", "--save", "--all"):
        opts[a[2:]] = True
    elif a == "--sheet":
        opts["sheet"] = argv[i + 1]
        i += 1
    elif a == "--cat":
        opts["cats"].append(argv[i + 1])
        i += 1
    else:
        ids.append(a)
    i += 1

A = alp_all.load()
if bpy.context.scene.name != A.SCENE:
    bpy.context.window_manager  # (pas de fenêtre en mode sans interface)
    scn = bpy.data.scenes.get(A.SCENE)
    if scn is not None and bpy.context.window is not None:
        bpy.context.window.scene = scn
for c in opts["cats"]:
    ids += [k for k, it in A.ITEMS.items() if it.get("cat") == c]
if opts["all"]:
    ids = [k for k in A.ITEMS if k not in A.FROZEN]
ids = [k for k in dict.fromkeys(ids) if k in A.ITEMS and k not in A.FROZEN]
print("[run_alp] scene", bpy.context.scene.name, "items", len(ids), flush=True)

t0 = time.time()
tris = A.build_all(ids)
for k in ids:
    print("[run_alp] %-22s %s" % (k, tris.get(k)), flush=True)
print("[run_alp] build %.1fs" % (time.time() - t0), flush=True)

if opts["export"]:
    A.export_all(ids)
    print("[run_alp] manifest", A.write_manifest(), flush=True)

def sheet_show(ids, gap=1.5, row_w=None):
    """Copies des maillages détaillés (LOD0) en rangées dans une collection visible ; renvoie (min, max)."""
    from mathutils import Vector
    col = bpy.data.collections.get("AP_Sheet") or bpy.data.collections.new("AP_Sheet")
    if col.name not in bpy.context.scene.collection.children.keys():
        bpy.context.scene.collection.children.link(col)
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    root_lc = bpy.context.scene.view_layers[0].layer_collection
    for lc in root_lc.children:
        if lc.name == A.ROOT:
            lc.exclude = True
        if lc.name == "AP_Show":
            lc.exclude = True
    meshes = []
    for iid in ids:
        src = bpy.data.collections["AP_" + iid]
        ob = sorted([o for o in src.objects if o.type == 'MESH'], key=lambda o: o.name)[0]
        vs = [v.co for v in ob.data.vertices]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        meshes.append((iid, ob.data, lo, hi))
    if row_w is None:
        total = sum(hi.x - lo.x + gap for _, _, lo, hi in meshes)
        row_w = max(12.0, (total * 1.0) ** 0.5 * 4.0)
    x, y, row_d = 0.0, 0.0, 0.0
    LO = Vector((1e9, 1e9, 1e9))
    HI = -LO
    for iid, me, lo, hi in meshes:
        w, d = hi.x - lo.x, hi.y - lo.y
        if x > 0 and x + w > row_w:
            x = 0.0
            y += row_d + gap * 2
            row_d = 0.0
        o = bpy.data.objects.new("S_" + iid, me)
        o.location = (x - lo.x, y - lo.y, 0)
        col.objects.link(o)
        LO = Vector(map(min, LO, o.location + lo))
        HI = Vector(map(max, HI, o.location + hi))
        x += w + gap
        row_d = max(row_d, d)
    LO.z = max(LO.z, 0.0)
    return LO, HI


def sheet_camera(lo, hi, az=-62, el=20, fill=0.92, lens=50):
    import math
    from mathutils import Vector
    from bpy_extras.object_utils import world_to_camera_view
    scn = bpy.context.scene
    cam = scn.camera
    target = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) * 0.45))
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    dist = (hi - lo).length * 1.5
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    for _ in range(6):
        cam.location = target + d * dist
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        bpy.context.view_layer.update()
        pts = [world_to_camera_view(scn, cam, c) for c in corners]
        span = max(max(p.x for p in pts) - min(p.x for p in pts), max(p.y for p in pts) - min(p.y for p in pts))
        dist *= span / fill
    cam.location = target + d * dist
    cam.data.clip_end = max(1000.0, dist * 4)


if opts["sheet"]:
    A.studio()
    lo, hi = sheet_show(ids)
    sheet_camera(lo, hi)
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = 1920, 1080
    scn.render.filepath = opts["sheet"]
    bpy.ops.render.render(write_still=True)
    print("[run_alp] sheet", opts["sheet"], flush=True)

if opts["save"]:
    bpy.ops.wm.save_mainfile()
    print("[run_alp] saved", flush=True)
print("[run_alp] total %.1fs" % (time.time() - t0), flush=True)
