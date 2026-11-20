# Bande-annonce (60 s, 1080p60)

Tout est filmé dans le jeu, image par image, depuis une **copie du projet** (`E:\ChillRoad_Capture`) : le projet ouvert dans
l'éditeur n'est pas touché. La musique est un morceau du jeu rendu hors ligne, la fin (le paysage qui devient le logo) est
rendue dans Blender, le montage est fait en Python (PIL + ffmpeg). Résultat : `Marketing/trailer/ChillRoad_trailer_fr.mp4`
et `_en.mp4`.

| Fichier | Rôle |
| --- | --- |
| `TrailerDirector.cs` | Directeur de tournage, compilé dans la copie seulement (`Scripts/DebugTools`). Pilote automatique (portion de route, itinéraire GPS, parcours de course, élan de tremplin, ligne droite), slalom dans le trafic, drift en cercle, vraie course avec décompte, menus, heure qui défile ; caméra du jeu avec HUD ou caméra de cinéma. Capture avec `Time.captureFramerate` : 60 images par seconde de jeu, quelle que soit la cadence de la machine. |
| `sync_capture.ps1` | Miroir du projet vers la copie, scripts de tournage, frein à main du pilote automatique (ajout de la copie), build (`-Build`). |
| `make_plan.py` → `plan.json` | Plan de tournage : un plan par ligne (voiture, lieu, heure, météo, vitesse, caméra, durée). |
| `shoot.py` | Lance le tournage des plans pas encore filmés (`_done.txt` par plan), le relance si le lecteur plante. |
| `sheets.py` | Planches-contact d'un tournage, pour juger les plans sans lire la vidéo. |
| `music/make_music.py` | « Neon Overdrive » (synthé du jeu, `MusicSynth.cs`) à 120 à la minute, arrangé sur 30 mesures = 60 s. |
| `outro/trailer_outro.py` | Fin en Blender : la crête et la route du logo « Ligne de crête » en relief, la Mustang trace la route dorée. |
| `edit.py` | Montage calé sur la musique (une mesure = 2 s), légendes en verre dépoli, fin Blender, encodage H.264 + AAC. |

## Refaire la bande-annonce

```
powershell -ExecutionPolicy Bypass -File Tools\Trailer\sync_capture.ps1 -Build      # ~2 min
python Tools/Trailer/make_plan.py
python Tools/Trailer/music/make_music.py

# Essai : une image sur deux, sans suréchantillonnage (quelques minutes), puis planches-contact
python Tools/Trailer/shoot.py E:/ChillRoad_Capture/trailer/prev --preview
python Tools/Trailer/sheets.py E:/ChillRoad_Capture/trailer/prev

# Tournage : images 2400 x 1350 (fenêtre 800 x 450, suréchantillonnage x3), JPEG 93 — environ 25 min, 4 Go
python Tools/Trailer/shoot.py E:/ChillRoad_Capture/trailer/final2
python Tools/Trailer/shoot.py E:/ChillRoad_Capture/trailer/final2_en --lang en --only c1_trafic_a,c1_trafic_b,c1_trafic_c,c6_course,d2_garage,d3_tuning,d4_carte

# Montage (planche du montage : --sheet dense ; essai rapide : --preview)
python Tools/Trailer/edit.py --frames E:/ChillRoad_Capture/trailer/final2 --lang fr
python Tools/Trailer/edit.py --frames E:/ChillRoad_Capture/trailer/final2 --frames-ui E:/ChillRoad_Capture/trailer/final2_en --lang en --out Marketing/trailer/ChillRoad_trailer_en.mp4
```

`shoot.py --only a,b` filme quelques plans, `--redo a,b` les refait. Les images de la fin Blender sont attendues dans
`E:\ChillRoad_Capture\trailer\outro` (0000.jpg…0479.jpg ; les images manquantes de la partie fixe sont fondues).

## À savoir

- **Écran** : la fenêtre du lecteur ne peut pas dépasser l'écran (800 x 480 sur cette machine). Le directeur ouvre une
  fenêtre 16/9 sans bordure de 800 x 450 et la définition vient du suréchantillonnage (`-ss`, 3 par défaut).
  Vérifier la ligne « REC … 800x450 x3 » du journal et la taille d'une image avant de monter.
- **Direct3D 11** (`-force-d3d11`, mis par `shoot.py`) : avec Direct3D 12, le lecteur plante au bout de quelques minutes de
  capture (`D3D12ScratchAllocator::DestroyScratch`). Pour lire une pile d'appels, copier
  `UnityPlayer_Win64_player_mono_x64.pdb` (dossier PlaybackEngines de l'éditeur) à côté de `Capture.exe`.
- **Menus** : le menu pause fige le temps (`timeScale = 0`) ; le directeur le remet à 1 tant que le menu est ouvert
  (voiture garée), pour que la capture image par image continue.
- **Trafic** : le jeu fait apparaître au plus un véhicule tous les 40 m, tirés au sort sur toute la carte ; à 200 km/h la
  route paraît vide. Pour les plans de trafic, le directeur regroupe devant la voiture des véhicules du trafic qui
  roulaient ailleurs (`pack`, `first`, `gap`) : ce sont les véhicules du jeu, conduits par le jeu, seule leur densité
  est mise en scène. Approche lente (`speed0`) puis accélération (`ramp`).
- **Rien n'est reproductible à l'image près** d'un tournage à l'autre (trafic, glisse) : les plans de trafic et de drift
  sont filmés en plusieurs variantes, et le début de chaque plan au montage (`CUTS` dans `edit.py`) se choisit sur les
  planches-contact du tournage qu'on monte. Le journal signale les chocs (`HIT`).
- **Caméra** : à droite de la voiture, elle traverse murets et lampadaires ; la placer côté chaussée (x négatif).
  Soleil à l'ouest (-x) le soir : rouler vers l'ouest pour un couchant de face ; lumière du soir à 18,3 h, nuit noire
  après 20 h ; un versant à l'ombre dès 17 h (série des voitures filmée à 13 h).
- Les images du tournage restent dans `E:\ChillRoad_Capture\trailer`, hors du dépôt.
