using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Tournage de la bande-annonce (copie du projet uniquement) :
    /// « Capture.exe -trailer plan.json -out dossier [-only a,b] [-preview] [-lang en] ».
    /// Chaque plan est filmé image par image (Time.captureFramerate, 60 images/s) dans dossier/nom/00000.jpg :
    /// la cadence de la machine ne compte pas, la physique et le trafic avancent d'un soixantième par image.
    /// Conduite : pilote automatique sur un tracé (portion de route, itinéraire du GPS, parcours d'une course,
    /// élan d'un tremplin), slalom dans le trafic de l'autoroute, drift en cercle. Caméra : vue du jeu avec le HUD,
    /// ou caméra de cinéma (repère de la voiture, point fixe, repère monde) avec mouvement du début à la fin du plan.
    /// </summary>
    [DefaultExecutionOrder(5000)]
    public class TrailerDirector : MonoBehaviour
    {
        [System.Serializable]
        public class Shot
        {
            public string name;
            // Voiture et préparation (garage de tuning) : 0 = de série
            public string car = "";
            public int paint, neon, flame, tint, pop = 1, tires, engine;
            // Où : point de la carte sur une route (type, pont / tunnel), itinéraire, course, tremplin
            public float x, z;
            public string kind = "", span = "";
            public float spanOffset;
            public int dir = 1;
            public int lane = -1;            // voie de l'autoroute (0 = rapide), -1 : voie par défaut
            public bool route;               // itinéraire du GPS de (x, z) à (tox, toz)
            public bool line;                // ligne droite de (x, z) à (tox, toz), hors du réseau (piste de l'aéroport)
            public float tox, toz;
            public string race = "";         // parcours d'une course (id)
            public bool raceStart;           // vraie course : décompte, portes, chrono
            public float along;              // abscisse du point de la prise sur le tracé (m)
            public string stunt = "";        // tremplin (id) : élan dans l'axe
            public float runup = 90f;
            // Conditions
            public float hour = 15f, hour2 = -1f;   // hour2 : l'heure défile pendant le plan
            public bool storm;
            public float traffic = 1.3f;
            public int lights = -1;          // -1 : selon l'heure
            // Conduite
            public float speed;              // km/h
            public float speed0;             // km/h avant la prise (le trafic repeuple la route devant), puis accélération
            public float ramp = 5f;          // secondes d'accélération avant la prise
            public float spawnMax, despawn;  // trafic resserré autour du joueur (m ; 0 : réglages du jeu)
            public int pack;                 // véhicules du trafic regroupés devant la voiture avant la prise (figuration)
            public float first = 110f, gap = 34f;   // distance du premier (m), espacement moyen (m)
            public bool nohb;                // drift amorcé aux gaz seulement (sans frein à main)
            public float drive = 5f;         // secondes de route avant de filmer
            public float len = 4f;           // secondes filmées
            public float lat = 7f;           // accélération latérale visée dans les virages (m/s²)
            public bool weave;               // slalom dans le trafic
            public float hug = 0.6f;         // frôlement : écart pris vers le véhicule doublé (m)
            public string mode = "";         // drift : cercle autour de (cx, cz)
            public float cx, cz, radius = 14f, slip = 35f;
            public int turn = 1;             // 1 : à droite
            public float assist = -1f;       // aides à la conduite (-1 : réglage du jeu)
            // Caméra
            public bool hud;                 // vue du jeu (view) avec le HUD
            public int view;
            public string menu = "";         // accueil, pause, meteo, garage, tuning, carte
            public float menuAt = -1f;       // le menu s'ouvre pendant le plan, à cet instant (s) ; sinon avant
            public float step;               // garage : voiture suivante toutes les « step » secondes
            public Vector3 cam = new Vector3(4f, 1.4f, 7f);   // droite, haut, avant (repère voiture)
            public Vector3 look = new Vector3(0f, 0.7f, 0f);
            public bool move;                // cam → cam2, look → look2 du début à la fin
            public Vector3 cam2, look2;
            public float orbit;              // rotation de la caméra autour de la voiture pendant le plan (degrés)
            public float fov = 40f, fov2 = -1f;
            public bool world;               // cam / look en repère monde, autour du point de la prise
            public bool track;               // repère monde : la caméra vise la voiture
            public bool local;               // repère monde tourné selon le cap de la voiture au début de la prise
            public bool useEye;              // caméra en un point absolu (eye → eye2), visant la voiture + look
            public Vector3 eye, eye2;
            public bool eyeMove;
            public bool velFrame;            // repère de la voiture pris sur sa vitesse (drift)
            public float follow;             // lissage du repère de la voiture (s)
            public float shake;              // caméra à l'épaule (degrés)
            public bool linear;              // mouvement à vitesse constante (sinon départ et arrivée adoucis)
            public bool blur;                // flou de mouvement du jeu
            public int seed = 1;
        }

        [System.Serializable] class Plan { public Shot[] shots; }

        static string Arg(string key)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return null;
        }

        static bool Flag(string key) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), key) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Prefs()
        {
            if (Arg("-trailer") == null) return;
            PlayerPrefs.SetInt("chillroad_opt_lang", Arg("-lang") == "en" ? 0 : 1);
            PlayerPrefs.SetInt("chillroad_opt_quality", GraphicsQuality.Ultra);
            PlayerPrefs.SetInt("chillroad_opt_detail", GraphicsQuality.DetailLevels - 1);
            PlayerPrefs.SetInt("chillroad_opt_tracknames", 0);
            PlayerPrefs.SetInt("chillroad_opt_vsync", 0);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Arg("-trailer") == null) return;
            new GameObject("TrailerDirector").AddComponent<TrailerDirector>();
        }

        const int Fps = 60;
        GameManager game;
        Shot current;
        string outDir;
        bool preview;
        int superSize = 3, every = 1, jpg = 93;

        // Tracé suivi : points, abscisses cumulées
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> cum = new List<float>();
        float total, prog, latNow;
        int laneNow, laneBase, laneFrom;
        float weaveHold, lastKmh;
        RoadNetwork.Edge edge;
        float edgeS;
        Vector3 anchor;
        Quaternion anchorRot = Quaternion.identity;
        bool anchored;
        // Prise en cours
        bool recording;
        int recFrame, recTotal = 1;
        float frameYaw;
        bool frameInit;
        int fixedCount;
        float driftHold, driftPull, cruise;
        int driftState;
        float spawnMax0 = -1f, despawn0;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            AudioListener.volume = 0f;
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Arg("-trailer")));
            outDir = Arg("-out") ?? "trailer_frames";
            preview = Flag("-preview");
            if (preview) { superSize = 1; every = 2; jpg = 80; }
            // Fenêtre 16/9 qui tient sur l'écran (ici 800 x 480) ; la définition des images vient du suréchantillonnage
            // (« -ss », 3 par défaut : 2400 x 1350 pour une fenêtre de 800 x 450)
            int width = int.Parse(Arg("-w") ?? "800"), height = int.Parse(Arg("-h") ?? "450");
            if (!preview && Arg("-ss") != null) superSize = int.Parse(Arg("-ss"));
            var only = new HashSet<string>((Arg("-only") ?? "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            Directory.CreateDirectory(outDir);
            while (!(game = FindFirstObjectByType<GameManager>())) yield return null;
            while (game.State != GameManager.Phase.Title && game.State != GameManager.Phase.Playing) yield return null;
            // Le jeu applique sa propre définition au lancement : celle du tournage est remise ensuite
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return Frames(5);
            VegetationRenderer.NoFrustumCull = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Time.captureFramerate = Fps;

            // Écran d'accueil, avant de lancer la partie
            foreach (var shot in plan.shots)
            {
                if (shot.menu != "accueil" || game.State != GameManager.Phase.Title) continue;
                if (only.Count > 0 && !only.Contains(shot.name)) continue;
                yield return Frames(Mathf.RoundToInt(Mathf.Max(shot.drive, 4f) * Fps));
                yield return Record(shot);
                File.WriteAllText(Path.Combine(outDir, shot.name, "_done.txt"), recTotal.ToString());
            }
            if (game.State == GameManager.Phase.Title) game.StartCountdown();
            while (game.State != GameManager.Phase.Playing) yield return null;
            yield return Frames(150);
            AudioListener.volume = 0f;
            var cars = game.assets.playerCars;
            for (int i = 0; i < cars.Length; i++) Debug.Log($"[Trailer] car {i} {cars[i].id} {cars[i].displayName}");
            foreach (var t in game.Races.All) Debug.Log($"[Trailer] race {t.id} {t.length:0} m, {t.path.Count} points");
            // Sauvegarde de la copie de capture seulement : tout le garage
            Wallet.Add(100000000f);
            foreach (var c in cars) Wallet.Buy(c);
            yield return Frames(60);

            foreach (var shot in plan.shots)
            {
                if (shot.menu == "accueil") continue;
                if (only.Count > 0 && !only.Contains(shot.name)) continue;
                yield return Run(shot);
            }
            current = null;
            game.Player.Autopilot = false;
            Debug.Log("[Trailer] done");
            Application.Quit();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Run(Shot shot)
        {
            current = null;
            recording = false;
            anchored = false;
            frameInit = false;
            Random.InitState(shot.seed * 7919 + 13);
            Application.targetFrameRate = -1;
            var car = game.Player;
            var cars = game.assets.playerCars;

            // Voiture, peinture, pièces : payées avec la cagnotte de la copie de capture, puis cagnotte crédible
            int ci = -1;
            for (int i = 0; i < cars.Length; i++)
            {
                if (string.IsNullOrEmpty(shot.car)) break;
                string key = shot.car.ToLowerInvariant();
                if ((cars[i].id ?? "").ToLowerInvariant() == key || (cars[i].displayName ?? "").ToLowerInvariant().Contains(key)) { ci = i; break; }
            }
            if (ci < 0) ci = game.Local.CarIndex;
            var basic = cars[ci];
            if (Wallet.Cash < 50000000) Wallet.Add(100000000f);
            CarPaint.Buy(basic, shot.paint);
            CarCosmetics.Fit(basic, CarCosmetics.Item.Neon, shot.neon);
            CarCosmetics.Fit(basic, CarCosmetics.Item.Flame, shot.flame);
            CarCosmetics.Fit(basic, CarCosmetics.Item.Tint, shot.tint);
            CarCosmetics.Fit(basic, CarCosmetics.Item.Pop, shot.pop);
            CarTuning.Fit(basic, CarTuning.Part.Tires, shot.tires);
            for (int lv = 1; lv <= shot.engine; lv++) CarTuning.Fit(basic, CarTuning.Part.Engine, lv);
            if (shot.engine == 0) CarTuning.Fit(basic, CarTuning.Part.Engine, 0);
            Wallet.Spend(Wallet.Cash - 186450);
            game.SelectCar(ci, false);
            game.ShowCar(ci);
            car = game.Player;
            if (shot.assist >= 0f) car.assist = shot.assist;

            if (GameSettings.MotionBlur != shot.blur)
            {
                GameSettings.MotionBlur = shot.blur;
                GraphicsQuality.Apply(game);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            game.Weather.Force(shot.storm, true);
            game.DayNight.frozen = true;
            game.DayNight.SetTime(shot.hour);
            game.Traffic.density = shot.traffic;
            if (spawnMax0 < 0f) { spawnMax0 = game.Traffic.spawnMax; despawn0 = game.Traffic.despawnDistance; }
            game.Traffic.spawnMax = shot.spawnMax > 0f ? shot.spawnMax : spawnMax0;
            game.Traffic.despawnDistance = shot.despawn > 0f ? shot.despawn : despawn0;
            bool dark = shot.hour >= 19.3f || shot.hour < 6.8f || shot.storm;
            game.Lights.Set(shot.lights < 0 ? dark : shot.lights > 0);

            float v = shot.speed / 3.6f;
            float anchorS;
            if (!BuildPath(shot, v, out anchorS)) { Debug.LogWarning("[Trailer] no path for " + shot.name); yield break; }
            bool chase = shot.hud || !string.IsNullOrEmpty(shot.menu);
            game.Hud.SetVisible(chase);
            game.Camera.Directed = !chase;
            if (chase) game.Camera.SetView(shot.view);
            prog = 0f;
            latNow = 0f;
            fixedCount = 0;
            driftHold = driftPull = 0f;
            driftState = 0;
            cruise = v;
            car.AutoHandbrake = false;

            if (shot.raceStart)
            {
                // Vraie course : la voiture est posée sur la ligne par le jeu, le pilote automatique part au « GO »
                Races.Track track = null;
                foreach (var t in game.Races.All) if (t.id == shot.race) track = t;
                game.Races.Clear();
                car.Autopilot = false;
                current = shot;
                game.Races.Begin(track);
                yield return Frames(Mathf.RoundToInt(shot.drive * Fps));
            }
            else
            {
                float v0 = shot.speed0 > 0f ? shot.speed0 / 3.6f : v;
                float ramp = shot.speed0 > 0f ? Mathf.Min(shot.ramp, shot.drive) : 0f;
                cruise = v0;
                float start = shot.mode == "drift" ? 0f : Mathf.Max(0f, anchorS - v0 * (shot.drive - ramp) - (v0 + v) * 0.5f * ramp);
                Vector3 pos, fwd;
                if (shot.mode == "drift")
                {
                    Vector3 c = Ground(new Vector3(shot.cx, 0f, shot.cz));
                    pos = Ground(c + Vector3.right * shot.radius);
                    Vector3 n = Vector3.right;
                    fwd = Vector3.Cross(Vector3.up, n) * shot.turn;
                }
                else
                {
                    pos = At(start, out fwd);
                    prog = start;
                }
                game.Traffic.ClearAround(pos, 16f);
                car.Teleport(pos + Vector3.up * 0.4f, Quaternion.LookRotation(fwd, Vector3.up), shot.mode == "drift" ? v * 0.5f : v0);
                car.InputEnabled = false;
                car.Parked = v < 0.1f;
                car.Autopilot = v > 0.1f;
                current = shot;
                if (chase) game.Camera.SetView(shot.view);
                int lead = Mathf.RoundToInt(Mathf.Max(shot.drive, 1.5f) * Fps);
                for (int i = 0; i < lead; i++)
                {
                    // Dernières images avant la prise : on laisse le temps réel aux chargements en tâche de fond
                    if (i >= lead - 20) System.Threading.Thread.Sleep(25);
                    if (i >= lead - Mathf.RoundToInt(ramp * Fps)) cruise = v;
                    if (shot.pack > 0 && i == Mathf.Max(0, lead - Mathf.RoundToInt((ramp + 0.6f) * Fps))) StageTraffic(shot);
                    yield return null;
                }
            }
            anchor = car.transform.position;
            {
                Vector3 af = car.transform.forward; af.y = 0f;
                anchorRot = shot.local && af.sqrMagnitude > 0.01f ? Quaternion.LookRotation(af.normalized, Vector3.up) : Quaternion.identity;
            }
            anchored = true;

            if (!string.IsNullOrEmpty(shot.menu) && shot.menuAt < 0f)
            {
                car.Autopilot = false;
                car.Parked = true;
                yield return OpenMenu(shot.menu);
            }
            Debug.Log($"[Trailer] REC {shot.name} {Screen.width}x{Screen.height} x{superSize} car={car.Spec.displayName} kmh={car.SpeedKmh:0} pos={car.transform.position} prog={prog:0}/{total:0}");
            yield return Record(shot);
            Debug.Log($"[Trailer] END {shot.name} kmh={car.SpeedKmh:0} pos={car.transform.position} prog={prog:0}/{total:0} score={game.Score.RunScore:0}");
            // Plan complet : shoot.py relance le tournage après celui-ci si le lecteur plante
            File.WriteAllText(Path.Combine(outDir, shot.name, "_done.txt"), recTotal.ToString());
            if (!string.IsNullOrEmpty(shot.menu)) yield return CloseMenu(shot.menu);
            if (shot.raceStart)
            {
                typeof(Races).GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(game.Races, null);
                game.Nav.ClearCourse();
            }
            current = null;
            car.Autopilot = false;
            car.Parked = true;
            car.InputEnabled = false;
        }

        // ------------------------------------------------------------------ prise de vues

        IEnumerator Record(Shot shot)
        {
            string dir = Path.Combine(outDir, shot.name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            recTotal = Mathf.Max(2, Mathf.RoundToInt(shot.len * Fps));
            recording = true;
            int menuFrame = shot.menuAt >= 0f && !string.IsNullOrEmpty(shot.menu) && shot.menu != "accueil" ? Mathf.RoundToInt(shot.menuAt * Fps) : -1;
            int stepFrames = shot.step > 0f ? Mathf.Max(1, Mathf.RoundToInt(shot.step * Fps)) : 0;
            for (int i = 0; i < recTotal; i++)
            {
                recFrame = i;
                if (shot.hour2 >= 0f)
                {
                    float u = i / (float)(recTotal - 1);
                    game.DayNight.SetTime(Mathf.Lerp(shot.hour, shot.hour2, u));
                    bool dark = game.DayNight.timeOfDay >= 19.3f || game.DayNight.timeOfDay < 6.8f;
                    if (shot.lights < 0) game.Lights.Set(dark);
                }
                if (i == menuFrame)
                {
                    game.Player.Autopilot = false;
                    game.Player.Parked = true;
                    StartCoroutine(OpenMenu(shot.menu));
                }
                if (stepFrames > 0 && i > 0 && i % stepFrames == 0) StepGarage();
                yield return new WaitForEndOfFrame();
                if (i % every != 0) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture(superSize);
                File.WriteAllBytes(Path.Combine(dir, i.ToString("D5") + ".jpg"), tex.EncodeToJPG(jpg));
                Destroy(tex);
            }
            recording = false;
        }

        void StepGarage()
        {
            var picker = FindFirstObjectByType<CarPicker>();
            if (!picker) return;
            typeof(CarPicker).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(picker, new object[] { 1 });
        }

        /// <summary>Bouton dont le libellé (espaces de mise en forme retirés) vaut <paramref name="label"/>.</summary>
        static bool Click(string label, string english)
        {
            foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!t.isActiveAndEnabled) continue;
                string flat = t.text.Replace(" ", "").Replace(" ", "").Replace(" ", "");
                if (t.text.Trim() != label && flat != label && t.text.Trim() != english && flat != english.ToUpperInvariant()) continue;
                var b = t.GetComponentInParent<Button>();
                if (b && b.isActiveAndEnabled && b.interactable) { b.onClick.Invoke(); return true; }
            }
            Debug.LogWarning("[Trailer] no button " + label);
            return false;
        }

        /// <summary>Menu ouvert pendant le tournage : le jeu se met en pause (temps figé), ce que la capture image par
        /// image ne supporte pas ; le temps continue donc, voiture garée.</summary>
        bool menuOpen;

        void Update()
        {
            if (menuOpen && !Flag("-menureal") && Time.timeScale < 1f) Time.timeScale = 1f;
        }

        void Pause(bool on)
        {
            menuOpen = on;
            if (on && Flag("-menureal")) Time.captureFramerate = 0;
            game.Hud.SetPaused(on);
            if (on && !Flag("-menureal")) Time.timeScale = 1f;
            if (!on) Time.captureFramerate = Fps;
        }

        IEnumerator OpenMenu(string menu)
        {
            switch (menu)
            {
                case "tuning":
                    menuOpen = true;
                    FindFirstObjectByType<TuningGarage>().Open();
                    Time.timeScale = 1f;
                    yield return Frames(180);
                    yield break;
                case "carte":
                    Pause(true);
                    yield return Frames(60);
                    Click("Carte", "Map");
                    yield return Frames(150);
                    yield break;
            }
            Pause(true);
            if (menu == "pause") yield break;
            yield return Frames(90);
            if (menu == "meteo") Click("Heure et météo", "Time and weather");
            else if (menu == "garage") Click("Voiture", "Car");
            else if (menu == "options") Click("Options", "Options");
            yield return Frames(menu == "garage" ? 240 : 120);
        }

        IEnumerator CloseMenu(string menu)
        {
            if (menu == "accueil") yield break;
            if (menu == "tuning") { FindFirstObjectByType<TuningGarage>().Close(); menuOpen = false; yield return Frames(120); yield break; }
            if (menu == "carte") { foreach (var w in FindObjectsByType<WorldMap>(FindObjectsSortMode.None)) w.SetOpen(false); }
            else if (menu != "pause") Click("Retour", "Back");
            yield return Frames(90);
            Pause(false);
            yield return Frames(60);
        }

        // ------------------------------------------------------------------ tracé

        Vector3 Ground(Vector3 p)
        {
            if (game.Map != null) p.y = game.Map.Height(p.x, p.z);
            return p;
        }

        void SetPath(IEnumerable<Vector3> points)
        {
            path.Clear();
            cum.Clear();
            foreach (var p in points)
            {
                if (path.Count > 0)
                {
                    float d = Vector3.Distance(path[path.Count - 1], p);
                    if (d < 0.25f) continue;
                    cum.Add(cum[cum.Count - 1] + d);
                }
                else cum.Add(0f);
                path.Add(p);
            }
            total = cum.Count > 0 ? cum[cum.Count - 1] : 0f;
        }

        /// <summary>Point du tracé à l'abscisse s, et cap (horizontal).</summary>
        Vector3 At(float s, out Vector3 forward)
        {
            forward = Vector3.forward;
            if (path.Count < 2) return path.Count == 1 ? path[0] : Vector3.zero;
            s = Mathf.Clamp(s, 0f, total);
            int lo = 0, hi = cum.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cum[mid] <= s) lo = mid; else hi = mid;
            }
            float seg = cum[hi] - cum[lo];
            Vector3 d = path[hi] - path[lo];
            forward = new Vector3(d.x, 0f, d.z).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            return Vector3.Lerp(path[lo], path[hi], seg > 0f ? (s - cum[lo]) / seg : 0f);
        }

        Vector3 Tangent(float s)
        {
            Vector3 a = At(s - 2f, out _), b = At(s + 2f, out _);
            Vector3 d = b - a;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        }

        bool BuildPath(Shot shot, float v, out float anchorS)
        {
            anchorS = 0f;
            edge = null;
            laneBase = laneNow = laneFrom = Mathf.Max(shot.lane, 0);
            weaveHold = 0f;
            if (shot.mode == "drift") { path.Clear(); cum.Clear(); total = 0f; return true; }
            if (!string.IsNullOrEmpty(shot.race))
            {
                foreach (var t in game.Races.All)
                {
                    if (t.id != shot.race) continue;
                    SetPath(t.path);
                    anchorS = shot.along;
                    return path.Count > 1;
                }
                return false;
            }
            if (!string.IsNullOrEmpty(shot.stunt))
            {
                foreach (var st in game.Stunts.All)
                {
                    if (st.data.id != shot.stunt) continue;
                    var pts = new List<Vector3>();
                    for (float d = -shot.runup; d <= 400f; d += 2f) pts.Add(Ground(st.lip + st.forward * d));
                    SetPath(pts);
                    anchorS = shot.runup + shot.along;
                    return true;
                }
                return false;
            }
            if (shot.line)
            {
                var pts = new List<Vector3>();
                Vector3 a = new Vector3(shot.x, 0f, shot.z), b = new Vector3(shot.tox, 0f, shot.toz);
                float n = Mathf.Max(2f, Vector3.Distance(a, b) / 2f);
                for (int i = 0; i <= n; i++) pts.Add(Ground(Vector3.Lerp(a, b, i / n)));
                SetPath(pts);
                anchorS = shot.along;
                return true;
            }
            if (shot.route)
            {
                var pts = new List<Vector3>();
                Vector3 from = new Vector3(shot.x, 0f, shot.z), to = new Vector3(shot.tox, 0f, shot.toz);
                var hit = game.Road.Nearest(Ground(from), 400f);
                Vector3 heading = to - from;
                if (hit.Found)
                {
                    hit.edge.Evaluate(hit.s, out var tangent, out _);
                    heading = tangent * shot.dir;
                }
                if (!game.Nav.Plan(from, heading, to, pts, out _)) return false;
                SetPath(pts);
                anchorS = shot.along;
                return path.Count > 1;
            }
            if (!Find(shot)) return false;
            {
                float back = v * shot.drive + 6f;
                float extra = shot.lane >= 0 ? shot.lane * RoadNetwork.LaneWidth : 0f;
                float s0 = Mathf.Clamp(edgeS - shot.dir * back, 2f, edge.Length - 2f);
                float s1 = shot.dir > 0 ? edge.Length - 2f : 2f;
                var pts = new List<Vector3>();
                for (float s = s0; shot.dir > 0 ? s <= s1 : s >= s1; s += shot.dir * 2f) pts.Add(edge.LanePosition(s, shot.dir, out _, extra));
                SetPath(pts);
                anchorS = Mathf.Abs(edgeS - s0);
                return path.Count > 1;
            }
        }

        bool Find(Shot shot)
        {
            edge = null;
            float best = float.MaxValue;
            foreach (var e in game.Road.Edges)
            {
                if (!string.IsNullOrEmpty(shot.kind) && e.kind.ToString() != shot.kind) continue;
                if (!string.IsNullOrEmpty(shot.span))
                {
                    if (e.spans == null) continue;
                    foreach (var sp in e.spans)
                    {
                        if (sp.kind != shot.span) continue;
                        float mid = (sp.s0 + sp.s1) * 0.5f;
                        Vector3 p = e.Evaluate(mid, out _, out _);
                        float d = new Vector2(p.x - shot.x, p.z - shot.z).sqrMagnitude;
                        if (d < best) { best = d; edge = e; edgeS = shot.dir > 0 ? sp.s0 + shot.spanOffset : sp.s1 - shot.spanOffset; }
                    }
                    continue;
                }
                for (int i = 0; i < e.pts.Length; i++)
                {
                    float d = new Vector2(e.pts[i].x - shot.x, e.pts[i].z - shot.z).sqrMagnitude;
                    if (d < best) { best = d; edge = e; edgeS = e.dist[i]; }
                }
            }
            if (edge != null && edge.oneway && shot.dir < 0) shot.dir = 1;
            if (edge != null) Debug.Log($"[Trailer] {shot.name}: edge {edge.id} {edge.kind} s={edgeS:0}/{edge.Length:0} lanes={edge.lanes}");
            return edge != null;
        }

        /// <summary>
        /// Figuration : des véhicules du trafic (ceux qui roulent ailleurs, hors champ) sont regroupés sur la route devant
        /// la voiture, un par voie en quinconce, avant la prise. Ce sont les véhicules du jeu, conduits par le jeu ;
        /// seule leur densité est mise en scène (le jeu en fait apparaître moins sur une même portion).
        /// </summary>
        void StageTraffic(Shot shot)
        {
            if (edge == null) return;
            var tr = game.Traffic;
            var spawn = typeof(TrafficSystem).GetMethod("TrySpawn", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int k = 0; k < 600 && tr.Cars.Count < shot.pack + 8 && spawn != null; k++) spawn.Invoke(tr, new object[] { -1 });
            var hit = game.Road.Nearest(game.Player.transform.position, 60f);
            if (!hit.Found || hit.edge != edge) { Debug.LogWarning("[Trailer] figuration : voiture hors de la route du plan"); return; }
            int dir = current.dir;
            var pool = new List<TrafficSystem.Car>();
            foreach (var c in tr.Cars)
                if (c != null && c.go && !c.crashed && !c.crossing && !c.farm && !c.remote) pool.Add(c);
            // les plus éloignés d'abord : jamais ceux que la caméra voit déjà
            Vector3 me = game.Player.transform.position;
            pool.Sort((a, b) => (b.go.transform.position - me).sqrMagnitude.CompareTo((a.go.transform.position - me).sqrMagnitude));
            int lanes = Mathf.Max(1, edge.lanes), lane = 1, placed = 0;
            float d = shot.first;
            foreach (var c in pool)
            {
                if (placed >= shot.pack) break;
                float s = hit.s + dir * d;
                if (s < 30f || s > edge.Length - 30f) break;
                // voie : jamais deux fois de suite la même ; poids lourds à droite
                int next = (lane + 1 + Random.Range(0, lanes - 1)) % lanes;
                lane = c.truck ? lanes - 1 : next;
                if (c.node != null && c.node.occupant == c) c.node.occupant = null;
                c.node = null;
                c.nextEdge = null;
                c.exitRamp = null;
                c.exitChecked = null;
                c.crossing = false;
                c.stuck = 0f;
                c.edge = edge;
                c.direction = dir;
                c.lane = c.nextLane = lane;
                c.laneShift = 0f;
                c.s = s;
                c.pace = Random.Range(0.93f, 0.97f);
                c.cruise = edge.speed * c.pace;
                c.speed = c.cruise;
                Vector3 p = edge.LanePosition(s, dir, out var fwd, lane * RoadNetwork.LaneWidth);
                c.go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up));
                c.rb.position = p;
                c.rb.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                d += shot.gap * Random.Range(0.75f, 1.3f);
                placed++;
            }
            Debug.Log($"[Trailer] figuration : {placed} véhicules sur {d:0} m, trafic {tr.Cars.Count}");
        }

        // ------------------------------------------------------------------ pilote automatique

        void FixedUpdate()
        {
            if (current == null || !game) return;
            var car = game.Player;
            if (current.raceStart)
            {
                if (!game.Races.Running) return;
                car.InputEnabled = false;
                car.Parked = false;
                car.Autopilot = true;
            }
            if (!car.Autopilot) return;
            fixedCount++;
            if (recording && lastKmh - car.SpeedKmh > 12f) Debug.Log($"[Trailer] HIT {current.name} rec={recFrame} {lastKmh:0} -> {car.SpeedKmh:0} km/h");
            lastKmh = car.SpeedKmh;
            if (current.mode == "drift") Drift(car);
            else Follow(car);
            if (fixedCount % 50 == 0)
                Debug.Log($"[Trailer] {current.name} t={fixedCount * Time.fixedDeltaTime:0.0} kmh={car.SpeedKmh:0} slip={car.SlipAngle:0} prog={prog:0}/{total:0} lane={laneNow} lat={latNow:0.0} gnd={car.GroundedWheels} ds={driftState} cars={game.Traffic.Cars.Count} rec={(recording ? recFrame : -1)}");
        }

        void Follow(VehicleController car)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 p = car.transform.position;
            float bestD = float.MaxValue, bestS = prog;
            for (float t = prog - 6f; t <= prog + 30f; t += 1f)
            {
                Vector3 q = At(t, out _);
                float d = new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude;
                if (d < bestD) { bestD = d; bestS = Mathf.Clamp(t, 0f, total); }
            }
            prog = bestS;
            float v = car.Speed;
            float want = cruise;
            float lateral = current.weave ? Weave(car, v, ref want) : 0f;
            latNow = Mathf.MoveTowards(latNow, lateral, 4.8f * dt);

            float ahead = 6f + v * 0.5f;
            Vector3 target = At(prog + ahead, out var tangent);
            Vector3 right = new Vector3(tangent.z, 0f, -tangent.x);
            target += right * latNow;
            Vector3 to = target - p; to.y = 0f;
            Vector3 f = car.transform.forward; f.y = 0f;
            float angle = Vector3.SignedAngle(f, to, Vector3.up);
            car.AutoSteer = car.GroundedWheels == 0 ? 0f : Mathf.Clamp(angle / 22f, -1f, 1f);

            // Vitesse : celle du plan, réduite avant les virages (accélération latérale visée, freinage anticipé)
            const float brake = 6.5f;
            float horizon = Mathf.Max(40f, v * 4f);
            for (float d = 4f; d < horizon; d += 4f)
            {
                float ang = Vector3.Angle(Tangent(prog + d - 4f), Tangent(prog + d + 4f)) * Mathf.Deg2Rad;
                float k = ang / 8f;
                if (k < 2e-4f) continue;
                float vc = Mathf.Sqrt(current.lat / k);
                float allowed = Mathf.Sqrt(vc * vc + 2f * brake * Mathf.Max(0f, d - 8f));
                if (allowed < want) want = allowed;
            }
            if (prog > total - 12f && string.IsNullOrEmpty(current.stunt)) want = 0f;
            float err = want - v;
            car.AutoThrottle = Mathf.Clamp01(err * 0.25f + 0.15f);
            car.AutoBrake = err < -2f ? Mathf.Clamp01(-err * 0.12f) : 0f;
            if (car.AutoBrake > 0f) car.AutoThrottle = 0f;
        }

        /// <summary>
        /// Slalom dans le trafic de l'autoroute : voie libre la plus longue parmi la voie suivie et ses voisines, et léger
        /// écart vers le véhicule doublé (frôlement). Renvoie l'écart latéral par rapport à la voie du tracé.
        /// </summary>
        float Weave(VehicleController car, float v, ref float want)
        {
            int lanes = edge != null ? Mathf.Max(1, edge.lanes) : 3;
            Vector3 origin = At(prog, out var tangent);
            Vector3 right = new Vector3(tangent.z, 0f, -tangent.x);
            var free = new float[lanes];
            var slow = new float[lanes];
            var beside = new bool[lanes];
            for (int k = 0; k < lanes; k++) { free[k] = 999f; slow[k] = 999f; }
            float hugTo = 0f, hugD = 999f;
            foreach (var c in game.Traffic.Cars)
            {
                if (c == null || !c.go) continue;
                Vector3 d = c.go.transform.position - origin;
                float along = Vector3.Dot(d, tangent), lat = Vector3.Dot(d, right);
                if (along < -14f || along > 320f) continue;
                for (int k = 0; k < lanes; k++)
                {
                    float off = (k - laneBase) * RoadNetwork.LaneWidth;
                    if (Mathf.Abs(lat - off) > 2.1f) continue;
                    float half = c.length * 0.5f + 2.6f;
                    if (along > -half - 4f && along < half + 9f) beside[k] = true;
                    if (along > 0f && along - half < free[k]) { free[k] = along - half; slow[k] = c.speed; }
                }
                float mine = (laneNow - laneBase) * RoadNetwork.LaneWidth;
                float side = lat - mine;
                if (along > -5f && along < 22f && Mathf.Abs(side) > 2.4f && Mathf.Abs(side) < 4.6f && Mathf.Abs(along) < hugD) { hugD = Mathf.Abs(along); hugTo = Mathf.Sign(side); }
            }
            // Voie quittée tant que la voiture n'est pas rangée dans la nouvelle : ses véhicules comptent encore
            float centre = (laneNow - laneBase) * RoadNetwork.LaneWidth;
            bool settled = Mathf.Abs(latNow - centre) < 0.9f;
            if (settled) laneFrom = laneNow;
            weaveHold -= Time.fixedDeltaTime;
            float dvNow = Mathf.Max(0f, v - Mathf.Min(slow[laneNow], v));
            float need = 26f + dvNow * 2.6f;
            if (free[laneNow] < need && weaveHold <= 0f)
            {
                int pick = laneNow;
                float bestFree = free[laneNow] + 18f;
                for (int k = laneNow - 1; k <= laneNow + 1; k += 2)
                {
                    if (k < 0 || k >= lanes || beside[k]) continue;
                    if (free[k] > bestFree) { bestFree = free[k]; pick = k; }
                }
                // Changement de voie seulement s'il reste la place de sortir de la sienne
                if (pick != laneNow && free[laneNow] > 7f + dvNow * 0.75f)
                {
                    laneFrom = laneNow;
                    laneNow = pick;
                    weaveHold = 0.45f;
                }
            }
            // Trop près du véhicule de devant (voie suivie, ou voie que l'on quitte) : freinage jusqu'à sa vitesse
            for (int pass = 0; pass < 2; pass++)
            {
                int k = pass == 0 ? laneNow : laneFrom;
                float dv = Mathf.Max(0f, v - slow[k]);
                if (free[k] < 9f + dv * dv / 14f) want = Mathf.Min(want, Mathf.Max(0f, slow[k] - 2f));
            }
            return (laneNow - laneBase) * RoadNetwork.LaneWidth + (settled ? hugTo * current.hug : 0f);
        }

        /// <summary>
        /// Drift en cercle autour de (cx, cz) : gaz pour faire glisser l'arrière, le volant vise un cap rentré de
        /// « slip » degrés par rapport à la trajectoire voulue (tangente au cercle, corrigée vers le bon rayon).
        /// </summary>
        void Drift(VehicleController car)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 c = new Vector3(current.cx, 0f, current.cz);
            Vector3 p = car.transform.position;
            Vector3 r = new Vector3(p.x - c.x, 0f, p.z - c.z);
            float dist = Mathf.Max(r.magnitude, 0.5f);
            Vector3 n = r / dist;
            Vector3 t = Vector3.Cross(Vector3.up, n) * current.turn;
            Vector3 vdes = (t - n * Mathf.Clamp((dist - current.radius) * 0.12f, -0.7f, 0.7f)).normalized;
            float v = car.Speed, want = current.speed / 3.6f;
            Vector3 f = car.transform.forward; f.y = 0f;
            float yawRate = car.Body.angularVelocity.y * Mathf.Rad2Deg;
            float wantRate = current.turn * v / current.radius * Mathf.Rad2Deg;
            car.AutoBrake = 0f;
            car.AutoHandbrake = false;
            // 0 : élan sur le cercle, sans glisse ; 1 : amorce au frein à main ; 2 : glisse tenue
            if (driftState == 0)
            {
                float err = Vector3.SignedAngle(f, vdes, Vector3.up);
                car.AutoSteer = Mathf.Clamp(err / 20f, -1f, 1f);
                car.AutoThrottle = Mathf.Clamp01((want - v) * 0.3f + 0.2f);
                if (v > want * 0.92f) { driftState = current.nohb ? 2 : 1; driftPull = 0.38f; driftHold = 0f; }
                return;
            }
            if (driftState == 1)
            {
                car.AutoSteer = current.turn;
                car.AutoThrottle = 1f;
                car.AutoHandbrake = true;
                driftPull -= dt;
                if (driftPull <= 0f || car.SlipAngle > current.slip * 0.8f) { driftState = 2; driftHold = 0f; }
                return;
            }
            Vector3 body = Quaternion.AngleAxis(current.slip * current.turn, Vector3.up) * vdes;
            float e2 = Vector3.SignedAngle(f, body, Vector3.up);
            car.AutoSteer = Mathf.Clamp(e2 / 18f - (yawRate - wantRate) * 0.006f, -1f, 1f);
            float thr = 0.85f + (current.slip - car.SlipAngle) * 0.02f + (want - v) * 0.06f;
            if (car.SlipAngle > current.slip + 25f) thr = Mathf.Min(thr, 0.4f);
            car.AutoThrottle = Mathf.Clamp(thr, 0.4f, 1f);
            // L'arrière a raccroché : nouvelle amorce ; voiture presque arrêtée : nouvel élan
            if (car.SlipAngle < 10f) driftHold += dt; else driftHold = 0f;
            if (v < want * 0.45f) driftState = 0;
            else if (driftHold > 0.7f && !current.nohb) { driftState = 1; driftPull = 0.3f; }
        }

        // ------------------------------------------------------------------ caméra

        void LateUpdate()
        {
            // Pas de toast de succès sur les images (l'achat du garage au départ en débloque plusieurs)
            var toast = GameObject.Find("AchievementToast");
            if (toast && toast.GetComponent<Canvas>() && toast.GetComponent<Canvas>().enabled) toast.GetComponent<Canvas>().enabled = false;
            if (current == null || current.hud || !string.IsNullOrEmpty(current.menu) || !game) return;
            var cam = game.Camera.GetComponent<Camera>();
            var t = game.Player.transform;
            float u = recording ? Mathf.Clamp01(recFrame / (float)(recTotal - 1)) : 0f;
            float e = current.linear ? u : Mathf.SmoothStep(0f, 1f, u);
            Vector3 camOff = current.move ? Vector3.Lerp(current.cam, current.cam2, e) : current.cam;
            Vector3 lookOff = current.move ? Vector3.Lerp(current.look, current.look2, e) : current.look;
            if (current.orbit != 0f) camOff = Quaternion.AngleAxis(current.orbit * e, Vector3.up) * camOff;
            Vector3 eye, at;
            if (current.useEye)
            {
                eye = current.eyeMove ? Vector3.Lerp(current.eye, current.eye2, e) : current.eye;
                at = t.position + lookOff;
            }
            else if (current.world)
            {
                Vector3 o = anchored ? anchor : t.position;
                Quaternion ar = anchored ? anchorRot : Quaternion.identity;
                eye = o + ar * camOff;
                at = current.track ? t.position + lookOff : o + ar * lookOff;
            }
            else
            {
                Vector3 f = t.forward;
                var vel = game.Player.Velocity;
                if (current.velFrame && new Vector2(vel.x, vel.z).magnitude > 4f) f = vel;
                f.y = 0f;
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                if (!frameInit || current.follow <= 0f) frameYaw = yaw;
                else frameYaw = Mathf.LerpAngle(frameYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime / current.follow));
                frameInit = true;
                var frame = Quaternion.Euler(0f, frameYaw, 0f);
                eye = t.position + frame * camOff;
                at = t.position + frame * lookOff;
            }
            if (game.Map != null && !game.Map.UnderRelief(t.position))
            {
                float ground = game.Map.Height(eye.x, eye.z) + 0.5f;
                if (eye.y < ground) eye.y = ground;
            }
            var rot = Quaternion.LookRotation(at - eye, Vector3.up);
            if (current.shake > 0f)
            {
                float time = Time.time * 0.9f;
                rot *= Quaternion.Euler((Mathf.PerlinNoise(time, 3.1f) - 0.5f) * 2f * current.shake, (Mathf.PerlinNoise(7.7f, time) - 0.5f) * 2f * current.shake, 0f);
            }
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = current.fov2 > 0f ? Mathf.Lerp(current.fov, current.fov2, e) : current.fov;
        }
    }
}
