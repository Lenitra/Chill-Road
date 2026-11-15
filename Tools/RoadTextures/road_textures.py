"""Texture de bruit de la route de NoHesi, générée par programme (aucun asset externe).

La route suit la direction artistique des voitures : aplats de couleur et formes nettes, sans grain
photographique. Le shader NoHesi/Road ne lit donc qu'une texture de bruits doux et périodiques, qu'il
seuille pour découper ses formes (bords de gravier, herbe, ornières, écaillage de la peinture).

    python road_textures.py [dossier_de_sortie]

Sortie (convention Unity : v = 0 en bas de l'image) :
  Road_Noise.png  1024², tuile de 32 m, quatre bandes de bruit (moyenne 0,5)
      R  plaques de 8 à 32 m    G  formes de 2 à 8 m    B  détours de 0,8 à 2,7 m    A  éclats de 30 à 80 cm
"""
import os
import sys
import numpy as np
from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")


def band_noise(n, seed, fmin, fmax, beta=2.0):
    """Bruit périodique limité à la bande [fmin, fmax] (cycles par tuile), ramené à 0,5 ± 0,2 (écart-type)."""
    r = np.random.default_rng(seed)
    spec = np.fft.fft2(r.standard_normal((n, n)))
    k = np.fft.fftfreq(n) * n
    f = np.hypot(k[:, None], k[None, :])
    f[0, 0] = 1.0
    amp = f ** (-beta / 2.0)
    amp *= np.exp(-np.maximum(fmin - f, 0.0) ** 2 / (0.3 * fmin) ** 2)
    amp *= np.exp(-np.maximum(f - fmax, 0.0) ** 2 / (0.3 * fmax) ** 2)
    amp[0, 0] = 0.0
    out = np.real(np.fft.ifft2(spec * amp))
    return 0.5 + 0.2 * (out - out.mean()) / (out.std() + 1e-9)


def main(n=1024):
    os.makedirs(OUT, exist_ok=True)
    noise = np.stack([band_noise(n, 11, 1, 4), band_noise(n, 12, 4, 16), band_noise(n, 13, 12, 40),
                      band_noise(n, 14, 40, 110)], axis=-1)
    img = (np.clip(np.flipud(noise), 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    path = os.path.join(OUT, "Road_Noise.png")
    Image.fromarray(img, "RGBA").save(path)
    print("Texture de route ->", path, img.shape)


if __name__ == "__main__":
    main()
