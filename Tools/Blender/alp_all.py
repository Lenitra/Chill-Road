"""Charge (ou recharge) tout le pack alpin : `import alp_all; A = alp_all.load()`."""
import importlib, os, sys

MODULES = ["alp_buildings", "alp_landmarks", "alp_services", "alp_props", "alp_props2", "alp_signs", "alp_infra", "alp_nature", "alp_nature2", "alp_nature_hd", "alp_props_hd", "alp_landmarks_hd", "alp_rural_hd", "alp_animals_hd", "alp_dealer"]


def load():
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)
    import alp_lib, alp_kit
    importlib.reload(alp_lib)
    importlib.reload(alp_kit)
    alp_lib.palette_material(refresh=True)
    for name in MODULES:
        if os.path.exists(os.path.join(here, name + ".py")):
            m = importlib.import_module(name)
            importlib.reload(m)
    return alp_lib
