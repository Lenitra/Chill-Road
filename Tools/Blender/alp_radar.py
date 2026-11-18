"""Pack alpin : radar de vitesse (Tools/Map/map_radars.py le pose au bord des routes, SpeedCameras.cs fait le reste).

Tourelle sur un socle de béton : mât d'acier cerclé de bandes jaunes, tête grise à deux faces (le radar contrôle les
deux sens de circulation). Chaque face porte un objectif et une lampe de flash ; la lampe reste allumée la nuit (couleur
émissive « lamp »), ce qui signale le radar de loin. L'éclair lui-même est un effet du jeu, à la hauteur FLASH_Z.

Repère du pack : origine au pied du mât, mètres. Les faces regardent le long de la route (±Y, soit ±Z Unity)."""
import alp_lib as A
from alp_lib import reg, lv

MAST = 3.5                     # haut du mât, bas de la tête
HEAD_W, HEAD_D, HEAD_H = 0.62, 0.86, 0.84
FLASH_Z = MAST + 0.56          # hauteur des lampes (comme map_radars.FLASH_H)


def face(g, sy):
    """Une face de la tête : plaque noire, objectif, lampe de flash sous sa visière."""
    y = sy * HEAD_D / 2
    g.box((0.0, y + sy * 0.012, MAST + 0.42), (0.5, 0.03, 0.62), "black")
    z = MAST + 0.3
    x = -0.1
    g.limb((x, y + sy * 0.02, z), (x, y + sy * 0.12, z), 0.125, 0.115, lv(g, 16, 8), "metal_dark", rot=0.0)
    g.limb((x, y + sy * 0.1, z), (x, y + sy * 0.135, z), 0.085, 0.08, lv(g, 16, 8), "window", rot=0.0)
    g.box((0.13, y + sy * 0.05, FLASH_Z), (0.2, 0.05, 0.2), "lamp")
    g.box((0.13, y + sy * 0.09, FLASH_Z + 0.125), (0.26, 0.13, 0.03), "metal_dark")
    if g.hd:
        # cadre de la lampe et petit voyant sous l'objectif
        for dz in (-0.112, 0.112):
            g.box((0.13, y + sy * 0.05, FLASH_Z + dz), (0.23, 0.04, 0.024), "metal_dark")
        for dx in (-0.112, 0.112):
            g.box((0.13 + dx, y + sy * 0.05, FLASH_Z), (0.024, 0.04, 0.2), "metal_dark")
        g.box((0.13, y + sy * 0.035, MAST + 0.24), (0.06, 0.02, 0.06), "neon_red")


def speed_camera(g):
    g.box((0.0, 0.0, -0.1), (0.86, 0.86, 0.8), "concrete")     # socle, enterré (le sol peut pencher)
    if g.lod == 2:
        g.box((0.0, 0.0, (0.2 + MAST + 0.06) / 2), (0.22, 0.22, MAST + 0.06 - 0.2), "metal")
        g.box((0.0, 0.0, MAST + HEAD_H / 2), (HEAD_W, HEAD_D, HEAD_H), "roof_slate")
        g.box((0.0, 0.0, MAST + 0.1), (HEAD_W + 0.06, HEAD_D + 0.06, 0.12), "sign_yellow")
        return
    n = lv(g, 16, 8)
    g.frustum((0.0, 0.0, 0.3), 0.13, 0.1, MAST - 0.3, n, "metal", rot=0.0)
    g.frustum((0.0, 0.0, 0.3), 0.2, 0.17, 0.12, n, "concrete_dark", rot=0.0)
    # bandes réfléchissantes du mât, repérables de loin
    for z, c in ((1.5, "sign_yellow"), (1.78, "black"), (2.06, "sign_yellow")):
        k = (z - 0.3) / (MAST - 0.3)
        r = 0.13 + (0.1 - 0.13) * k + 0.012
        g.frustum((0.0, 0.0, z), r, r - 0.008, 0.26, n, c, rot=0.0)
    # tête
    g.box((0.0, 0.0, MAST + HEAD_H / 2), (HEAD_W, HEAD_D, HEAD_H), "roof_slate")
    g.box((0.0, 0.0, MAST + 0.07), (HEAD_W + 0.03, HEAD_D + 0.03, 0.14), "sign_yellow")
    g.box((0.0, 0.0, MAST + HEAD_H + 0.04), (HEAD_W + 0.1, HEAD_D + 0.1, 0.08), "metal_dark")
    for sy in (-1, 1):
        face(g, sy)
    if g.hd:
        # porte de visite sur les flancs, charnières et serrure
        for sx in (-1, 1):
            x = sx * HEAD_W / 2
            g.box((x + sx * 0.008, 0.0, MAST + 0.5), (0.02, 0.56, 0.5), "roof_slate_b")
            g.box((x + sx * 0.024, 0.2, MAST + 0.5), (0.02, 0.05, 0.08), "metal_dark")
            for dz in (-0.17, 0.17):
                g.box((x + sx * 0.024, -0.26, MAST + 0.5 + dz), (0.02, 0.04, 0.07), "metal_dark")
        # coffret au pied du mât, côté opposé à la route (les deux faces se ressemblent : posé sur l'axe X)
        g.box((0.0, 0.2, 0.62), (0.2, 0.14, 0.42), "roof_slate_b")
        g.box((0.0, 0.275, 0.62), (0.12, 0.02, 0.3), "metal_dark")


reg(dict(id="speed_camera", file="Speed_Camera", cat="radar", tags=["radar", "road"], seed=561, fp=(0.86, 0.86),
         collider="capsule", radius=0.32, grad_h=4.4, build=speed_camera,
         note="radar de vitesse à deux faces (±Z Unity : le long de la route), lampes de flash à 4,06 m"))
