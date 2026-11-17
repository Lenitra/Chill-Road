"""Manifeste du pack alpin : lecture pour le placement, et version aplatie pour l'import Unity (JsonUtility ne lit pas
les tableaux imbriqués)."""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PACK = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "ChillRoad", "Models", "AlpPack"))


def load():
    with open(os.path.join(PACK, "AlpPack_manifest.json"), encoding="utf-8") as f:
        items = json.load(f)["items"]
    return {it["id"]: it for it in items}


def write_unity():
    items = []
    for it in load().values():
        lights = [float(v) for p in (it.get("lights") or []) for v in p]
        items.append(dict(id=it["id"], file=it["file"], category=it["category"], tags=it.get("tags") or [],
                          sx=it["size"][0], sy=it["size"][1], sz=it["size"][2],
                          fw=it["footprint"][0], fd=it["footprint"][1], collider=it.get("collider") or "none",
                          radius=float(it.get("radius") or 0), module=float(it.get("module") or 0),
                          ymin=float(it.get("ymin") or 0), lights=lights, material=it.get("material") or ""))
    path = os.path.join(PACK, "AlpPack_unity.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(dict(items=items), f, indent=1)
    return path


if __name__ == "__main__":
    print(write_unity())
