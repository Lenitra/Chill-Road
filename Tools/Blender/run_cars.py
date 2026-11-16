"""Reconstruit les véhicules (pack trafic + voitures jouables) dans un Blender sans interface, exporte les FBX et
rend des aperçus. Ne sauvegarde aucun .blend (CarPack.blend peut être ouvert ailleurs).

E:\\blender\\blender.exe -b --factory-startup --python run_cars.py -- [options] [ids...]
  --export        exporte les FBX (Assets/NoHesi/Models/CarPack ou PlayerCars) + la palette
  --render DIR    rend un aperçu 3/4 avant + arrière par véhicule dans DIR
  --sheet DIR     planche de tous les véhicules demandés (une image)
  ids             identifiants (défaut : tout le trafic + les voitures jouables)
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

import bpy  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
opts = {"export": False, "render": None, "sheet": None}
ids = []
i = 0
while i < len(argv):
    a = argv[i]
    if a == "--export":
        opts["export"] = True
    elif a == "--toon":
        pass
    elif a in ("--render", "--sheet"):
        opts[a[2:]] = argv[i + 1]
        i += 1
    else:
        ids.append(a)
    i += 1

# scène du pack : en mode sans interface, il n'y a pas de fenêtre ; on renomme la scène par défaut
scn = bpy.context.scene
scn.name = "CarPack"
for o in list(bpy.data.objects):          # cube, lampe et caméra de la scène d'usine
    bpy.data.objects.remove(o, do_unlink=True)

import pack_lib as P  # noqa: E402
import pack_cars  # noqa: E402,F401
import pack_utility  # noqa: E402,F401
import play_cars  # noqa: E402

PLAYABLE = list(play_cars.PLAYABLE)
TRAFFIC = [cid for cid, spec in P.CARS.items() if cid not in PLAYABLE and not spec.get("hidden")
           and cid not in ("golf_load",)]
if not ids:
    ids = TRAFFIC + PLAYABLE

t0 = time.time()
report = []
for cid in ids:
    t = time.time()
    tris = P.build(cid)
    report.append((cid, tris, time.time() - t))
    print("[run_cars] %-14s %6d tris  %.1fs" % (cid, tris, time.time() - t), flush=True)

if opts["export"]:
    for cid in ids:
        folder = os.path.join(ROOT, "Assets", "NoHesi", "Models", "PlayerCars" if cid in PLAYABLE else "CarPack")
        path, stats = P.export(cid, folder)
        print("[run_cars] export", path, flush=True)
    P.palette_image(os.path.join(ROOT, "Assets", "NoHesi", "Models", "CarPack", "CarPack_Palette.png"))

def toon_preview():
    """Aperçu proche du shader NoHesi/Toon : rampe à deux tons (ombre bleutée), émission = couleur finale,
    contours Freestyle. Ne concerne que le rendu de prévisualisation (les FBX gardent leurs matériaux)."""
    for name in ("PK_Palette", "PK_Paint"):
        m = bpy.data.materials.get(name)
        if not m:
            continue
        nt = m.node_tree
        tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
        col_out = tex.outputs["Color"]
        mul = next((n for n in nt.nodes if n.type == "VECT_MATH"), None)
        if mul:
            col_out = mul.outputs[0]
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        dif = nt.nodes.new("ShaderNodeBsdfDiffuse")
        dif.inputs["Color"].default_value = (1, 1, 1, 1)
        s2r = nt.nodes.new("ShaderNodeShaderToRGB")
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        el = ramp.color_ramp.elements
        el[0].position, el[0].color = 0.10, (0.30, 0.33, 0.45, 1)
        el[1].position, el[1].color = 0.18, (1.0, 1.0, 1.0, 1)
        prod = nt.nodes.new("ShaderNodeMix")
        prod.data_type = 'RGBA'
        prod.blend_type = 'MULTIPLY'
        prod.inputs[0].default_value = 1.0
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(dif.outputs[0], s2r.inputs[0])
        nt.links.new(s2r.outputs["Color"], ramp.inputs[0])
        nt.links.new(col_out, prod.inputs[6])
        nt.links.new(ramp.outputs["Color"], prod.inputs[7])
        nt.links.new(prod.outputs[2], em.inputs["Color"])
        em.inputs["Strength"].default_value = 1.0
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    scn = bpy.context.scene
    scn.render.use_freestyle = True
    scn.render.line_thickness = 1.3
    ls = scn.view_layers[0].freestyle_settings.linesets
    ls_ = ls[0] if len(ls) else ls.new("L")
    ls_.select_by_visibility = True
    ls_.select_silhouette = True
    ls_.select_border = True
    ls_.select_crease = True
    ls_.linestyle.color = (0.08, 0.07, 0.1)
    scn.view_layers[0].freestyle_settings.crease_angle = 2.3


if opts["render"] or opts["sheet"]:
    P.studio()
    if "--toon" in sys.argv:
        toon_preview()
    if opts["render"]:
        os.makedirs(opts["render"], exist_ok=True)
        for cid in ids:
            P.render_car(cid, os.path.join(opts["render"], cid), res=(960, 540))
    if opts["sheet"]:
        d = opts["sheet"]
        os.makedirs(os.path.dirname(d) or ".", exist_ok=True)
        rows = max(1, round(len(ids) ** 0.5 / 1.6))
        P.show(ids, spacing=3.2, rows=rows)
        P.frame(35, 16, fill=0.9, lens=50)
        scn = bpy.context.scene
        scn.render.resolution_x, scn.render.resolution_y = 1920, 1080
        scn.render.filepath = d
        bpy.ops.render.render(write_still=True)

print("[run_cars] total %.1fs" % (time.time() - t0), flush=True)
