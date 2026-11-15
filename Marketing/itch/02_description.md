# Chill Road - Ho Hesi

**Weave through endless traffic at 200+ km/h.** Brush past cars, trucks and tractors as close as you dare: every near miss adds points and pushes your multiplier up to x10. Slow down and the combo fades. Crash, or get busted by the police, and the whole run is gone.

The road never ends. It is generated as you drive: three-lane highways, two-way country roads, dirt tracks through the woods, roundabouts, crossroads, overpasses and roadworks, under a full day/night cycle and a sky that can turn to storm at any moment.

## What's on the road

- **Near-miss scoring.** The closer and faster the pass, the bigger the payout. Driving in the oncoming lane on country roads doubles everything. Clean drifts score too.
- **Random events** pop up along the way: speed cameras, average-speed zones, checkpoint runs, near-miss chains, drift zones, cone slaloms, flat-out stretches with no braking allowed, oncoming-lane challenges and duels against a rival driver.
- **Police chases.** Flash a speed camera way too fast, or cross a patrol, and a cruiser comes after you. Outrun it or lose everything.
- **9 cars to drive**, each with its own engine sound:
  - a retro-futuristic hydrogen wedge concept;
  - a 90s Japanese turbo coupe;
  - a little roadster with pop-up headlights;
  - a charcoal '69 fastback with black stripes, fit for a retired hitman;
  - a white-and-black 80s coupe that knows every mountain pass (and every tofu shop);
  - a stainless-steel gull-wing with a very particular kit… its backfires come out of the time vents;
  - a Swedish-style megacar with a swan-neck wing;
  - a teal V12 Le Mans hypercar;
  - and the world's smallest production car: three wheels, one headlight, 49 cc and 61 km/h flat out. Good luck with the near misses.
- **Car relays** every 2 km swap you into the next car without stopping.
- **2-player split screen:** race a friend on the same endless road, top and bottom of the screen, each with their own car, camera, HUD and score. Two gamepads, or keyboard for player 1 and a gamepad for player 2.
- **Storms.** Most of the time the sky is clear, but storms roll in: rain, lightning and thunder, a road that slowly gets wet, with reflections, puddles and less grip. Traffic slows down, you probably won't.
- **Busy, varied traffic:** about 25 vehicle types, from city cars and taxis to buses, semis, fuel tankers, cement mixers, tow trucks, fire engines, motorbikes and farm tractors crawling along country lanes.
- **Physics with some weight:** raycast suspension, tire model, gearboxes from 3 to 9 speeds, handbrake. Drifts come from real sliding: they cost speed, and the stability control only catches real spins.
- **Real-time audio:** every engine is synthesized live (cross-plane V8, flat-plane turbo V8, V12, V6, turbo six, screaming four, 49 cc two-stroke, electric whine) with pops and bangs on lift-off. The synthwave soundtrack is generated live too. Want your own music? Drop .ogg, .mp3 or .wav files into the game's `StreamingAssets/Music` folder.
- **Demo mode:** from the main menu, watch an AI driver play a real run, filmed like a trailer with chase, bumper, drone, roadside and wheel cameras.
- **Your road, your rules:** sliders for highway and dirt road share, curves, hills, junctions, traffic density, event frequency and chance of rain. Keep a seed to replay the same road.
- 4 camera views (chase, far chase, hood, bumper), free look and look back.

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Throttle / Brake, reverse | W or Z or ↑ / S or ↓ | RT / LT |
| Steer | A or Q / D, ← → | Left stick |
| Handbrake | Space | B / RB |
| Headlights | L | L3 |
| Change camera | C / V | R3 |
| Look around / Look back | Right-click + mouse / B | Right stick / LB |
| Reset to road | R | Y |
| Pause | Esc / P | Start |

Works on QWERTY and AZERTY keyboards. A gamepad is recommended.

## Made 100% with AI

Chill Road is an experiment in building a complete game with AI. Everything was made by **Claude (Anthropic) through Claude Code**, from a human giving directions, reference photos, feedback and playtesting:

- all the C# code: physics, traffic AI, events, weather, UI and the demo mode's AI driver and camera director;
- every 3D model: the 9 cars, the traffic, trees, buildings and road furniture were built by AI-written Python scripts driving Blender. No asset packs, no store models;
- the road, terrain, sky and water shaders;
- the engine sounds and the music, synthesized in code: there is not a single audio file in the game;
- the texts of this page, and the screenshots, captured by an AI-written camera script.

No third-party assets are used.

## Details

- Windows 64-bit. Unzip and run the .exe.
- 1 or 2 players (split screen).
- Interface in English and French (switch on the title screen).
- Early version: expect rough edges. Feedback and bug reports in the comments are welcome!

---

# Chill Road (FR)

**Faufile-toi dans un trafic sans fin à plus de 200 km/h.** Frôle voitures, camions et tracteurs d'aussi près que tu l'oses : chaque frôlement rapporte des points et fait grimper le multiplicateur jusqu'à x10. Ralentis et le combo retombe. Un accident, ou une arrestation par la police, et tout le run est perdu.

La route ne s'arrête jamais. Elle se génère pendant que tu roules : autoroutes à 2x3 voies, nationales à double sens, chemins de terre en forêt, ronds-points, carrefours, ponts et zones de travaux, avec un cycle jour/nuit complet et un ciel qui peut tourner à l'orage à tout moment.

## Au programme

- **Score au frôlement.** Plus c'est serré et rapide, plus ça paie. Rouler à contresens sur les nationales double tous les gains. Les drifts propres rapportent aussi.
- **Événements aléatoires** sur la route : radars, zones de vitesse moyenne, courses à checkpoints, séries de frôlements, zones de drift, slaloms entre les cônes, « pied au plancher » sans freiner, défis à contresens et duels contre un pilote rival.
- **Courses-poursuites.** Flashé bien trop vite, ou croisé par une patrouille, et la police te prend en chasse. Sème-la ou perds tout.
- **9 voitures jouables**, chacune avec son propre son moteur :
  - un concept rétro-futuriste en coin, à hydrogène ;
  - un coupé turbo japonais des années 90 ;
  - un petit roadster à phares escamotables ;
  - un fastback de 1969 gris anthracite à bandes noires, parfait pour un tueur à gages retraité ;
  - un coupé des années 80 blanc et noir qui connaît tous les cols de montagne (et toutes les boutiques de tofu) ;
  - une propulsion en inox à portes papillon avec un kit très particulier… ses pétarades sortent des évents temporels ;
  - une mégacar d'inspiration suédoise à aileron col de cygne ;
  - une hypercar V12 du Mans, bleu sarcelle ;
  - et la plus petite voiture de série du monde : trois roues, un seul phare, 49 cm³ et 61 km/h à fond. Bon courage pour les frôlements.
- **Des relais** tous les 2 km te font passer à la voiture suivante sans t'arrêter.
- **Écran partagé à 2 joueurs :** affronte un ami sur la même route sans fin, un en haut, un en bas, chacun avec sa voiture, sa caméra, son HUD et son score. Deux manettes, ou le clavier pour le joueur 1 et une manette pour le joueur 2.
- **Des orages.** Le ciel est le plus souvent dégagé, mais l'orage finit par arriver : pluie, éclairs et tonnerre, une route qui se mouille peu à peu, avec reflets, flaques et moins d'adhérence. Le trafic ralentit, toi sans doute pas.
- **Un trafic varié :** environ 25 types de véhicules, des citadines et taxis aux bus, semi-remorques, citernes, bétonnières, dépanneuses, camions de pompiers, motos et tracteurs agricoles qui se traînent sur les routes de campagne.
- **Une physique avec du poids :** suspensions, modèle de pneus, boîtes de 3 à 9 rapports, frein à main. Les drifts viennent d'une vraie glisse : ils coûtent de la vitesse, et l'ESP ne rattrape que les vraies toupies.
- **Son en temps réel :** chaque moteur est synthétisé en direct (V8 à vilebrequin croisé, V8 turbo à vilebrequin plat, V12, V6, 6 cylindres turbo, 4 cylindres rageur, 2-temps de 49 cm³, électrique) avec pétarades au lever de pied. La bande-son synthwave est générée en direct elle aussi. Tu préfères ta musique ? Dépose des fichiers .ogg, .mp3 ou .wav dans le dossier `StreamingAssets/Music` du jeu.
- **Mode démo :** depuis le menu principal, regarde un pilote IA jouer une vraie partie, filmée comme une bande-annonce (poursuite, pare-chocs, drone, bord de route, caméra sur la roue).
- **Ta route, tes règles :** réglages de la part d'autoroute et de chemins, des virages, du relief, des carrefours, de la densité du trafic, de la fréquence des événements et du risque de pluie. Garde une graine pour rejouer la même route.
- 4 vues (poursuite, poursuite lointaine, capot, pare-chocs), regard libre et vue arrière.

## Commandes

| Action | Clavier | Manette |
|---|---|---|
| Accélérer / Freiner, marche arrière | Z ou W ou ↑ / S ou ↓ | RT / LT |
| Diriger | Q ou A / D, ← → | Stick gauche |
| Frein à main | Espace | B / RB |
| Phares | L | L3 |
| Changer de vue | C / V | R3 |
| Regarder autour / derrière | Clic droit + souris / B | Stick droit / LB |
| Replacer sur la route | R | Y |
| Pause | Échap / P | Start |

Clavier AZERTY et QWERTY. Manette recommandée.

## Fait à 100 % avec l'IA

Chill Road est une expérience : créer un jeu complet avec l'IA. Tout a été réalisé par **Claude (Anthropic) via Claude Code**, à partir des directions, photos de référence, retours et tests d'un humain :

- tout le code C# : physique, IA du trafic, événements, météo, interface, ainsi que le pilote IA et le réalisateur du mode démo ;
- chaque modèle 3D : les 9 voitures, le trafic, les arbres, les bâtiments et le mobilier routier ont été construits par des scripts Python écrits par l'IA qui pilotent Blender. Aucun pack d'assets, aucun modèle acheté ;
- les shaders de la route, du terrain, du ciel et de l'eau ;
- les sons moteur et la musique, synthétisés dans le code : il n'y a pas un seul fichier audio dans le jeu ;
- les textes de cette page, et les captures d'écran, prises par un script de caméra écrit par l'IA.

Aucun asset tiers n'est utilisé.

## Infos

- Windows 64 bits. Dézippe et lance le .exe.
- 1 ou 2 joueurs (écran partagé).
- Interface en anglais et en français (choix sur l'écran d'accueil).
- Version en cours de développement : il reste des angles à polir. Retours et bugs bienvenus en commentaire !
