"""Pack alpin : auto-école (zone de tutoriel) : panneau d'instructions, cône, pile de pneus, portique d'entrée, guichet.

Repère du pack : origine au sol, façade = -Y (Unity +Z), mètres. Le texte des panneaux est posé par le jeu
(DrivingSchool.cs), dans la langue choisie : le modèle ne porte qu'un panneau blanc à bandeau bleu."""
from alp_lib import reg, lv

BRAND = "sign_blue"


def school_board(g):
    """Panneau d'instructions : deux poteaux, grand panneau blanc (3,4 x 1,9 m), bandeau bleu en haut."""
    for s in (-1, 1):
        g.box((s * 1.4, 0.06, 1.65), (0.12, 0.12, 3.3), "metal_dark")
    g.box((0, 0, 2.35), (3.4, 0.08, 1.9), "sign_white")
    g.box((0, -0.005, 3.16), (3.42, 0.1, 0.3), BRAND)
    g.box((0, -0.005, 1.39), (3.42, 0.1, 0.05), BRAND)


reg(dict(id="school_board", file="School_Board", cat="street", tags=["school", "sign"], seed=610, fp=(3.4, 0.3),
         collider="box", build=school_board))


def school_cone(g):
    """Cône de signalisation : semelle noire, cône orange, bande blanche."""
    n = lv(g, 14, 10, 6)
    g.box((0, 0, 0.015), (0.42, 0.42, 0.03), "black")
    g.frustum((0, 0, 0.03), 0.16, 0.03, 0.62, n, "orange", auto=False)
    g.frustum((0, 0, 0.27), 0.112, 0.09, 0.11, n, "white", cap0=False, cap1=False, auto=False)


reg(dict(id="school_cone", file="School_Cone", cat="sign", tags=["school"], seed=611, fp=(0.42, 0.42),
         collider="capsule", radius=0.16, build=school_cone))


def school_tires(g):
    """Pile de trois pneus peints, rouge et blanc, qui borde la piste."""
    n = lv(g, 18, 12, 8)
    for k, color in enumerate(("sign_red", "white", "sign_red")):
        g.frustum((0, 0, 0.26 * k), 0.38, 0.38, 0.25, n, color, auto=False)
    g.frustum((0, 0, 0.78), 0.2, 0.2, 0.012, n, "black", auto=False)


reg(dict(id="school_tires", file="School_Tires", cat="street", tags=["school"], seed=612, fp=(0.8, 0.8),
         collider="capsule", radius=0.38, build=school_tires))


def school_arch(g):
    """Portique d'entrée : deux mâts, poutre bleue, damier de course."""
    H, W = 5.4, 15.0
    for s in (-1, 1):
        g.box((s * W / 2, 0, H / 2), (0.38, 0.38, H), "metal_dark")
        g.box((s * W / 2, 0, 0.15), (0.9, 0.9, 0.3), "concrete")
    g.box((0, 0, H + 0.1), (W + 0.5, 0.42, 0.75), BRAND)
    step = 0.5
    cols = int(W / step)
    for k in range(cols):
        x = -W / 2 + (k + 0.5) * step
        for j in range(2):
            g.box((x, -0.225, H + 0.3 - j * step * 0.8), (step - 0.01, 0.03, step * 0.8 - 0.01),
                  "white" if (k + j) % 2 == 0 else "black")


reg(dict(id="school_arch", file="School_Arch", cat="street", tags=["school"], seed=613, fp=(15.8, 0.9),
         collider="none", build=school_arch))


def school_kiosk(g):
    """Guichet de l'auto-école : petit bâtiment blanc, bandeau vitré, auvent bleu."""
    W, D, H = 7.0, 4.0, 2.9
    g.box((0, 0, H / 2), (W, D, H), "plaster_white")
    g.box((0, -D / 2 - 0.01, 1.75), (W - 1.6, 0.04, 1.1), "window_lit")
    g.box((-2.55, -D / 2 - 0.02, 1.05), (1.0, 0.05, 2.1), "door")
    g.box((0, 0, H + 0.11), (W + 0.5, D + 0.5, 0.22), "metal_dark")
    g.box((0, -D / 2 - 0.75, 2.55), (W + 0.3, 1.5, 0.12), BRAND)
    for s in (-1, 1):
        g.box((s * (W / 2 - 0.1), -D / 2 - 1.4, 1.25), (0.1, 0.1, 2.5), "metal_dark")


reg(dict(id="school_kiosk", file="School_Kiosk", cat="street", tags=["school"], seed=614, fp=(7.5, 5.8),
         collider="box", build=school_kiosk))

IDS = ["school_board", "school_cone", "school_tires", "school_arch", "school_kiosk"]
