"""Aperçus du garage de tuning (sans interface, ne sauvegarde rien) :
blender -b Tools/Blender/AlpPack.blend --python Tools/Blender/run_tuning_preview.py"""
import sys, math, os
import bpy
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alp_all
A = alp_all.load()
ids = ["tuning_garage", "tuning_sign"]
A.build_all(ids + ["car_podium"])
OUT = os.path.join(HERE, "Rendus", "AlpPack")
scn = bpy.data.scenes.new("TuningPreview")
w = bpy.data.worlds.new("TuningWorld")
w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs[0].default_value = (0.55, 0.72, 0.92, 1)
bg.inputs[1].default_value = 1.0
scn.world = w
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'BLENDER_WORKBENCH'):
    try:
        scn.render.engine = eng
        break
    except TypeError:
        pass
print("ENGINE", scn.render.engine)
try:
    scn.eevee.taa_render_samples = 16
except Exception:
    pass
try:
    scn.view_settings.view_transform = 'Standard'
except TypeError:
    pass
if scn.render.engine == 'BLENDER_WORKBENCH':
    scn.display.shading.light = 'STUDIO'
    scn.display.shading.color_type = 'TEXTURE'
scn.render.image_settings.file_format = 'PNG'
scn.render.resolution_x, scn.render.resolution_y = 1600, 900
# sol, soleil, caméra
me = bpy.data.meshes.new("Ground")
s = 400
me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
gm = bpy.data.materials.new("GroundMat")
gm.use_nodes = True
b = next(n for n in gm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
b.inputs["Base Color"].default_value = (0.25, 0.25, 0.26, 1)
b.inputs["Roughness"].default_value = 0.95
me.materials.append(gm)
scn.collection.objects.link(bpy.data.objects.new("Ground", me))
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 3.5
sun = bpy.data.objects.new("Sun", sun_d)
sun.rotation_euler = (math.radians(48), 0, math.radians(-38))
scn.collection.objects.link(sun)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.clip_end = 2000
scn.collection.objects.link(cam)
scn.camera = cam
placed = []


def put(iid, lod, loc, rotz=0.0):
    col = bpy.data.collections["AP_" + iid]
    ob = sorted(col.objects, key=lambda o: o.name)[lod]
    o = bpy.data.objects.new("%s_%d_%d" % (iid, lod, len(placed)), ob.data)
    o.location = loc
    o.rotation_euler = (0, 0, rotz)
    scn.collection.objects.link(o)
    placed.append(o)
    return o


def clear():
    for o in placed:
        bpy.data.objects.remove(o, do_unlink=True)
    placed.clear()


def shot(name, eye, target, lens=35):
    cam.location = Vector(eye)
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    scn.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True, scene=scn.name)
    print("SHOT", scn.render.filepath)


# 1. ensemble (comme sur la carte) : garage, totem, podium devant
put("tuning_garage", 0, (0, 0, 0))
put("tuning_sign", 0, (-20.0, -14.0, 0), math.radians(20))
put("car_podium", 0, (-11.0, -16.0, 0))
shot("tuning_ensemble", (-30, -52, 16), (0, -3, 3), 32)
shot("tuning_baie", (4, -24, 1.8), (3.6, -2, 2.4), 30)
shot("tuning_bureau", (-18, -22, 2.0), (-10, -8, 2.8), 30)
shot("tuning_arriere", (32, 36, 20), (0, 0, 3), 32)
clear()
# 2. niveaux de détail côte à côte
for k in range(3):
    put("tuning_garage", k, (-36 + k * 36, 0, 0))
shot("tuning_lods", (0, -100, 34), (0, 0, 2), 35)
clear()
# 3. totem de près
put("tuning_sign", 0, (0, 0, 0))
shot("tuning_totem", (3.0, -11, 3.5), (0, 0, 4.2), 32)
