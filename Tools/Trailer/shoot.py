"""Tournage de la bande-annonce : lance Capture.exe sur les plans du plan.json qui ne sont pas encore filmés, et le
relance tant qu'il en reste (un plan filmé a son _done.txt). Direct3D 11 : avec Direct3D 12 (le choix par défaut
du lecteur), Unity plante au bout de quelques minutes de capture (D3D12ScratchAllocator::DestroyScratch).

    python Tools/Trailer/shoot.py DOSSIER [--preview] [--lang en] [--only a,b] [--redo a,b]
"""
import argparse, json, shutil, subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXE = Path("E:/ChillRoad_Capture/Capture/Capture.exe")
ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--preview", action="store_true")
ap.add_argument("--lang", default="fr")
ap.add_argument("--only", default="")
ap.add_argument("--redo", default="")
a = ap.parse_args()
out = Path(a.out)
out.mkdir(parents=True, exist_ok=True)
names = [s["name"] for s in json.loads((HERE / "plan.json").read_text(encoding="utf-8"))["shots"]]
if a.only:
    names = [n for n in names if n in a.only.split(",")]
for n in a.redo.split(","):
    if n and (out / n).exists():
        shutil.rmtree(out / n)
fails = {}
run = 0
while True:
    todo = [n for n in names if not (out / n / "_done.txt").exists()]
    if not todo:
        break
    if fails.get(todo[0], 0) >= 3:
        print("abandon :", todo[0], flush=True)
        names.remove(todo[0])
        continue
    run += 1
    # fenêtre sans bordure de 800 x 450 (tient sur un petit écran) ; les images sont suréchantillonnées par le directeur
    cmd = [str(EXE), "-screen-fullscreen", "0", "-popupwindow", "-screen-width", "800", "-screen-height", "450", "-trailer", str(HERE / "plan.json"), "-out", str(out),
           "-only", ",".join(todo), "-lang", a.lang, "-force-d3d11", "-logFile", str(out / f"_run{run}.log")]
    if a.preview:
        cmd.append("-preview")
    code = subprocess.call(cmd)
    left = [n for n in names if not (out / n / "_done.txt").exists()]
    print(f"passe {run} : code {code}, {len(todo) - len(left)} plans filmés, reste {len(left)}", flush=True)
    if left and left[0] == todo[0]:
        fails[todo[0]] = fails.get(todo[0], 0) + 1
print("terminé :", sum((out / n / "_done.txt").exists() for n in names), "plans", flush=True)
