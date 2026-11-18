"""Écrit le manifeste du pack alpin sans perdre les fiches absentes du fichier .blend ouvert (write_manifest ne connaît que
les collections présentes : la caméra de radar ou le bout du hall, construits ailleurs, en disparaîtraient)."""
import json, os


def write(A, folder):
    path = os.path.join(folder, "AlpPack_manifest.json")
    with open(path, encoding="utf-8") as f:
        before = json.load(f)["items"]
    count = A.write_manifest(folder=folder)
    with open(path, encoding="utf-8") as f:
        man = json.load(f)
    have = {e["id"] for e in man["items"]}
    man["items"] += [e for e in before if e["id"] not in have]
    with open(path, "w", encoding="utf-8") as f:
        json.dump(man, f, indent=1, ensure_ascii=False)
    return count, len(man["items"])
