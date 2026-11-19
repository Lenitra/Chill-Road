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


def school_tire(g, color):
    """Pneu seul (débris d'une pile ou d'un mur cassé, posé par le jeu) : 0,35 m de rayon, 0,24 m de haut, trou noir
    visible dessus et dessous."""
    n = lv(g, 14, 10, 7)
    g.frustum((0, 0, 0), 0.35, 0.35, 0.24, n, color, auto=False)
    g.frustum((0, 0, 0.236), 0.19, 0.19, 0.008, n, "black", cap0=False, auto=False)
    g.frustum((0, 0, -0.004), 0.19, 0.19, 0.008, n, "black", cap1=False, auto=False)


reg(dict(id="school_tire_red", file="School_TireRed", cat="street", tags=["school"], seed=619, fp=(0.7, 0.7),
         collider="none", build=lambda g: school_tire(g, "sign_red")))
reg(dict(id="school_tire_white", file="School_TireWhite", cat="street", tags=["school"], seed=620, fp=(0.7, 0.7),
         collider="none", build=lambda g: school_tire(g, "white")))


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

def school_pylon(g):
    """Poteau du 8 de drift : socle, mât rayé rouge et blanc, boule en tête (repère lisible de loin)."""
    n = lv(g, 16, 10, 8)
    g.frustum((0, 0, 0), 0.45, 0.4, 0.2, n, "concrete", auto=False)
    for k in range(6):
        g.frustum((0, 0, 0.2 + k * 0.5), 0.16, 0.16, 0.5, n, "sign_red" if k % 2 == 0 else "white", cap0=False,
                  cap1=k == 5, auto=False)
    g.frustum((0, 0, 3.2), 0.28, 0.28, 0.4, n, "yellow", auto=False)


reg(dict(id="school_pylon", file="School_Pylon", cat="street", tags=["school"], seed=615, fp=(0.9, 0.9),
         collider="capsule", radius=0.4, build=school_pylon))

def school_tirewall(g):
    """Mur de pneus de 4 m : deux rangées de piles de trois pneus, rouge et blanc en alternance (barrière de circuit)."""
    n = lv(g, 12, 9, 6)
    k = 0
    for row, y in enumerate((-0.33, 0.33)):
        for i in range(6):
            x = -1.67 + i * 0.67 + (0.0 if row == 0 else 0.0)
            for z in range(3):
                color = "sign_red" if (i + z) % 2 == 0 else "white"
                g.frustum((x, y, z * 0.25), 0.33, 0.33, 0.24, n, color, cap0=(z == 0), auto=False)
    g.box((0, 0, 0.76), (4.0, 1.3, 0.04), "black")            # sangle du dessus


reg(dict(id="school_tirewall", file="School_TireWall", cat="street", tags=["school"], seed=616, fp=(4.0, 1.3),
         collider="box", build=school_tirewall))


def school_gantry(g):
    """Portique de départ du circuit : deux pylônes, poutre noire, damier, panneau de cinq feux rouges au centre."""
    H, W = 6.2, 16.0
    for s in (-1, 1):
        g.box((s * W / 2, 0, H / 2), (0.5, 0.5, H), "metal_dark")
        g.box((s * W / 2, 0, 0.2), (1.1, 1.1, 0.4), "concrete")
    g.box((0, 0, H + 0.2), (W + 0.6, 0.5, 0.8), "black")
    step = 0.5
    for k in range(int(W / step)):
        x = -W / 2 + (k + 0.5) * step
        for j in range(2):
            g.box((x, -0.27, H + 0.4 - j * 0.38), (step - 0.01, 0.03, 0.37), "white" if (k + j) % 2 == 0 else "black")
    g.box((0, -0.3, H - 0.55), (3.4, 0.3, 0.7), "black")
    for i in range(5):
        g.frustum((-1.3 + i * 0.65, -0.46, H - 0.55), 0.2, 0.2, 0.05, lv(g, 12, 8, 6), "sign_red", rot=0.0,
                  auto=False) if False else g.box((-1.3 + i * 0.65, -0.46, H - 0.55), (0.36, 0.04, 0.36), "sign_red")


reg(dict(id="school_gantry", file="School_Gantry", cat="street", tags=["school"], seed=617, fp=(16.6, 1.1),
         collider="none", build=school_gantry))


def school_banner(g):
    """Banderole de bord de piste sans texte : panneau de 6 m sur pieds, bandes aux couleurs de l'école."""
    for s in (-1, 1):
        g.box((s * 2.9, 0.05, 0.6), (0.08, 0.08, 1.2), "metal_dark")
    g.box((0, 0, 0.75), (6.0, 0.06, 0.9), "sign_blue")
    g.box((0, -0.035, 0.95), (6.0, 0.02, 0.2), "white")
    g.box((-1.6, -0.04, 0.62), (1.6, 0.02, 0.32), "yellow")
    g.box((1.4, -0.04, 0.62), (2.2, 0.02, 0.32), "white")


reg(dict(id="school_banner", file="School_Banner", cat="street", tags=["school"], seed=619, fp=(6.0, 0.3),
         collider="box", build=school_banner))


def school_flag(g):
    """Mât à drapeau à damier."""
    g.frustum((0, 0, 0), 0.06, 0.04, 7.0, lv(g, 8, 6, 4), "metal", auto=False)
    m = lv(g, 2, 1, 1)                                 # damier 4 × 3, étoffe qui claque au vent (shader Toon, FlagWave)
    g.cloth((0.05, 0, 6.8), (1.6, 0, 0), (0, 0, -1.2), 4 * m, 3 * m,
            lambda i, j: "black" if (i // m + j // m) % 2 == 0 else "white", amp=0.11 * 1.6, sag=0.04)


reg(dict(id="school_flag", file="School_Flag", cat="street", tags=["school"], seed=620, fp=(0.4, 0.4),
         collider="capsule", radius=0.1, build=school_flag))

IDS = ["school_board", "school_cone", "school_tires", "school_tire_red", "school_tire_white", "school_arch", "school_kiosk",
       "school_pylon", "school_tirewall", "school_gantry", "school_banner", "school_flag"]
