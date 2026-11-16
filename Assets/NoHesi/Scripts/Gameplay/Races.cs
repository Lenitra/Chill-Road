using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Courses chronométrées, courues directement sur la carte : pas de chargement ni de monde à part, la circulation
    /// et les autres joueurs restent là. Deux genres de tracés :
    /// - sprint : un départ, une arrivée ailleurs ;
    /// - tours : une boucle à parcourir plusieurs fois, la ligne de départ est aussi la ligne d'arrivée.
    /// Un tracé est donné par quelques points de la carte (<see cref="Defs"/>) : son parcours est l'itinéraire le
    /// plus rapide de l'un à l'autre (<see cref="RouteGuide.Plan"/>), et ses points de passage sont posés dessus à
    /// intervalles réguliers. Rien n'est écrit dans la carte générée : les tracés suivent le réseau tel qu'il est.
    /// Déroulé : on s'arrête sur la porte de départ (visible de loin), l'invite propose de lancer le chrono ; décompte,
    /// puis il faut franchir les portes dans l'ordre, dans le sens de la course. Le GPS affiche le parcours (ruban sur
    /// la route, minicarte). À l'arrivée, le temps est comparé au record personnel du tracé, gardé avec ses temps de
    /// passage : à chaque porte, l'écart au record s'affiche.
    /// Chaque joueur court pour lui (rien ne passe par le réseau).
    /// </summary>
    public class Races : MonoBehaviour
    {
        const string BestKey = "nohesi_race_best_", SplitsKey = "nohesi_race_splits_", CarKey = "nohesi_race_car_";
        /// <summary>Couleurs des deux genres de tracés (portes, HUD, carte globale).</summary>
        public static readonly Color SprintTint = new Color(1f, 0.3f, 0.36f);
        public static readonly Color LapsTint = new Color(0.68f, 0.52f, 1f);
        static readonly Color FinishTint = new Color(1f, 0.95f, 0.8f);
        static readonly Color Good = new Color(0.45f, 0.95f, 0.5f), Bad = new Color(1f, 0.32f, 0.26f);
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f), Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color Plate = new Color(0.02f, 0.03f, 0.05f, 0.6f);

        const float StartRadius = 14f;          // on lance la course arrêté à moins de cette distance du départ
        const float StartSpeed = 5.5f;
        const float StartBack = 4f;             // la voiture est posée juste derrière la ligne
        const float Beat = 0.75f;               // durée d'un chiffre du décompte
        const float GateMargin = 4f;            // tolérance de part et d'autre d'une porte
        const float AbandonHold = 0.8f;
        const float LostDistance = 650f, LostTime = 6f, GateTimeout = 300f;

        /// <summary>Tracé : points de la carte (x, z) à relier dans l'ordre ; pour une boucle, le dernier revient au premier.</summary>
        class Def
        {
            public string id, fr, en;
            public int laps = 1;
            /// <summary>Distance visée entre deux portes (m).</summary>
            public float spacing = 500f;
            /// <summary>Sens de départ approximatif (x, z).</summary>
            public Vector2 heading;
            public Vector2[] points;
            /// <summary>Côté de l'étiquette du départ sur la carte globale : -1 à gauche du repère, 0 au-dessus, 1 à droite.</summary>
            public int side;
        }

        static readonly Def[] Defs =
        {
            // Sprint : la route du col depuis Le Plan, du pied de la montée jusque sous le col (≈ 3,4 km, 250 m de dénivelé)
            new Def
            {
                id = "col", fr = "Montée du Col", en = "Pass Hill Climb", laps = 1, spacing = 500f, heading = new Vector2(-0.1f, 1f), side = -1,
                points = new[] { new Vector2(-35.8f, -1717.1f), new Vector2(118.7f, 243.3f) },
            },
            // Tours : départ sur la route de la vallée au bord du lac, vers Les Granges ; retour par l'autoroute et son
            // pont sur le lac, sortie du Plan, puis la route de la vallée par Le Port (≈ 6,2 km le tour)
            new Def
            {
                id = "lac", fr = "Tour du Lac", en = "Lake Loop", laps = 2, spacing = 700f, heading = new Vector2(0.4f, 1f), side = 0,
                points = new[] { new Vector2(1655.9f, -910.5f), new Vector2(2106.3f, -287.1f), new Vector2(484.9f, -2077.8f), new Vector2(550.1f, -1775.2f) },
            },
        };

        /// <summary>Porte : ligne en travers de la route, à franchir dans le sens de <see cref="forward"/>.</summary>
        public class Gate
        {
            public Vector3 pos, forward, right;
            /// <summary>Demi-largeur (m) et indice du point du parcours qui suit la porte.</summary>
            public float half;
            public int index;
        }

        public class Track
        {
            public string id, fr, en;
            public int laps;
            /// <summary>Côté de l'étiquette du départ sur la carte globale (-1 à gauche, 0 au-dessus, 1 à droite).</summary>
            public int side;
            /// <summary>Parcours d'un tour (ou du sprint entier) : points dans la voie, du départ à l'arrivée.</summary>
            public readonly List<Vector3> path = new List<Vector3>();
            /// <summary>Longueur d'un tour (m).</summary>
            public float length;
            public Gate start;
            /// <summary>Portes d'un tour, dans l'ordre ; la dernière est l'arrivée (ou la ligne de la boucle).</summary>
            public Gate[] gates;
            /// <summary>Record personnel (s, 0 = jamais terminé), voiture du record, temps de passage à chaque porte.</summary>
            public float best;
            public string bestCar;
            public float[] splits;

            public bool Loop => laps > 1;
            public string Name => Loc.T(fr, en);
            public Color Tint => Loop ? LapsTint : SprintTint;
            public float Distance => length * laps;
            public int GateCount => gates.Length * laps;
            public Vector3 Finish => path[path.Count - 1];
            /// <summary>« SPRINT » ou « 2 TOURS ».</summary>
            public string Kind => Loop ? Loc.F($"{laps} TOURS", $"{laps} LAPS") : "SPRINT";
            /// <summary>« Sprint · 3,4 km » ou « 2 tours · 12,5 km ».</summary>
            public string Summary => (Loop ? Loc.F($"{laps} tours", $"{laps} laps") : "Sprint") + Loc.F($"  ·  {Distance / 1000f:0.0} km");
        }

        enum Mode { Idle, Countdown, Running }

        static readonly List<Track> None = new List<Track>();

        GameManager game;
        List<Track> tracks;
        Mode mode;
        Track candidate, announced;
        readonly List<Vector3> course = new List<Vector3>();
        float[] times;
        int next, shownBeat, teleports;
        float elapsed, countdown, goTimer, sinceGate, lostTimer, abandonHeld;
        Vector3 prevPos;

        // Dernier écart au record affiché, et résultat de la dernière course (quelques secondes après l'arrivée)
        float deltaTimer, deltaValue;
        Track resultTrack;
        float resultTimer, resultTime, resultDelta;
        bool resultRecord, resultHasDelta;

        /// <summary>Tous les tracés (vide tant que la carte n'est pas chargée).</summary>
        public IReadOnlyList<Track> All { get { Load(); return tracks ?? None; } }
        /// <summary>Tracé en cours (décompte ou course), sinon null.</summary>
        public Track Active { get; private set; }
        public bool Running => mode == Mode.Running;
        /// <summary>Course terminée : tracé, temps, nouveau record.</summary>
        public event Action<Track, float, bool> Finished;

        public void Init(GameManager g)
        {
            game = g;
            if (GameManager.DedicatedServer) return;
            MiniMap.Overlay += DrawOnMiniMap;
        }

        void OnDestroy() => MiniMap.Overlay -= DrawOnMiniMap;

        // ------------------------------------------------------------------ tracés

        void Load()
        {
            if (tracks != null || !game || game.Map == null || !game.Nav || !game.Road || game.Road.Edges.Count == 0) return;
            if (game.State == GameManager.Phase.Loading) return;
            tracks = new List<Track>();
            foreach (var def in Defs)
            {
                var t = Build(def);
                if (t != null) tracks.Add(t);
                else Debug.LogWarning($"[Races] Tracé « {def.id} » introuvable sur le réseau routier");
            }
        }

        Track Build(Def def)
        {
            var t = new Track { id = def.id, fr = def.fr, en = def.en, laps = Mathf.Max(1, def.laps), side = def.side };
            Vector3 heading = new Vector3(def.heading.x, 0f, def.heading.y).normalized;
            int n = def.points.Length;
            int legs = t.Loop ? n : n - 1;
            for (int i = 0; i < legs; i++)
            {
                Vector2 a = def.points[i], b = def.points[(i + 1) % n];
                if (!game.Nav.Plan(new Vector3(a.x, 0f, a.y), heading, new Vector3(b.x, 0f, b.y), t.path, out heading)) return null;
            }
            if (t.path.Count < 2) return null;
            var dist = new float[t.path.Count];
            for (int i = 1; i < dist.Length; i++) dist[i] = dist[i - 1] + Vector3.Distance(t.path[i - 1], t.path[i]);
            t.length = dist[dist.Length - 1];
            if (t.length < 100f) return null;
            if (t.Loop && Vector3.Distance(t.path[0], t.Finish) > 5f)
                Debug.LogWarning($"[Races] La boucle « {def.id} » ne se referme pas ({Vector3.Distance(t.path[0], t.Finish):0} m)");
            t.start = GateAt(t, dist, 0f);
            int count = Mathf.Max(1, Mathf.RoundToInt(t.length / Mathf.Max(def.spacing, 100f)));
            t.gates = new Gate[count];
            for (int k = 1; k <= count; k++) t.gates[k - 1] = GateAt(t, dist, t.length * k / count);

            t.best = PlayerPrefs.GetFloat(BestKey + t.id, 0f);
            t.bestCar = PlayerPrefs.GetString(CarKey + t.id, "");
            if (t.best > 0f)
            {
                // Temps de passage du record : oubliés si le tracé n'a plus le même nombre de portes
                var parts = PlayerPrefs.GetString(SplitsKey + t.id, "").Split(';');
                if (parts.Length == t.GateCount)
                {
                    t.splits = new float[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out t.splits[i])) { t.splits = null; break; }
                }
            }
            return t;
        }

        /// <summary>Porte à la distance d du départ : en travers de la chaussée (de la voie de droite à celle d'en face).</summary>
        Gate GateAt(Track t, float[] dist, float d)
        {
            var path = t.path;
            int i = Array.BinarySearch(dist, d);
            if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, path.Count - 1);
            float seg = dist[i] - dist[i - 1];
            Vector3 p = Vector3.Lerp(path[i - 1], path[i], seg > 0f ? Mathf.Clamp01((d - dist[i - 1]) / seg) : 0f);
            Vector3 f = path[Mathf.Min(path.Count - 1, i + 1)] - path[Mathf.Max(0, i - 2)];
            f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            var g = new Gate { pos = p, forward = f, right = new Vector3(f.z, 0f, -f.x), half = 6f, index = i };
            var hit = game.Road.Nearest(p, 40f);
            if (hit.Found)
            {
                var e = hit.edge;
                // Autoroute : la chaussée de son sens seulement ; ailleurs, toute la route (axe au milieu)
                if (e.kind == RoadKind.Highway) g.half = e.lanes * RoadNetwork.LaneWidth * 0.5f + 2f;
                else if (hit.distance < e.TotalHalfWidth + 1f)
                {
                    g.pos = hit.point;
                    g.half = e.TotalHalfWidth + 1f;
                }
            }
            return g;
        }

        Gate GateOf(int i) => Active.gates[i % Active.gates.Length];

        void Save(Track t)
        {
            PlayerPrefs.SetFloat(BestKey + t.id, t.best);
            PlayerPrefs.SetString(CarKey + t.id, t.bestCar ?? "");
            var parts = new string[t.splits.Length];
            for (int i = 0; i < parts.Length; i++) parts[i] = t.splits[i].ToString("0.000", CultureInfo.InvariantCulture);
            PlayerPrefs.SetString(SplitsKey + t.id, string.Join(";", parts));
            PlayerPrefs.Save();
        }

        /// <summary>Oublie les records (outils, remise à zéro de la progression).</summary>
        public void Clear()
        {
            foreach (var t in All)
            {
                t.best = 0f;
                t.bestCar = "";
                t.splits = null;
                PlayerPrefs.DeleteKey(BestKey + t.id);
                PlayerPrefs.DeleteKey(CarKey + t.id);
                PlayerPrefs.DeleteKey(SplitsKey + t.id);
            }
            PlayerPrefs.Save();
        }

        /// <summary>Temps « 2:31.45 ».</summary>
        public static string Clock(float seconds)
        {
            int cs = Mathf.Max(0, Mathf.RoundToInt(seconds * 100f));
            return $"{cs / 6000}:{cs / 100 % 60:00}.{cs % 100:00}";
        }

        static string Delta(float d) => (d < 0f ? "-" : "+") + Mathf.Abs(d).ToString("0.00", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ déroulé

        void Update()
        {
            if (!game || GameManager.DedicatedServer) return;
            Load();
            if (tracks == null) return;
            if (game.State != GameManager.Phase.Playing)
            {
                if (mode != Mode.Idle) Stop();
                candidate = null;
                UpdateGates(false);
                UpdateHud(0f, false);
                return;
            }
            var car = game.Player;
            Vector3 pos = car.transform.position;
            bool menu = game.Hud && game.Hud.Paused || OptionsMenu.IsOpen;
            float dt = Time.deltaTime;
            switch (mode)
            {
                case Mode.Idle: UpdateIdle(car, pos, menu); break;
                case Mode.Countdown: UpdateCountdown(car, pos, menu, dt); break;
                case Mode.Running: UpdateRace(car, pos, menu, dt); break;
            }
            UpdateGates(true);
            UpdateHud(Time.unscaledDeltaTime, !menu);
        }

        /// <summary>Balade : annonce le départ dont on approche, et propose la course quand on s'y arrête.</summary>
        void UpdateIdle(VehicleController car, Vector3 pos, bool menu)
        {
            Track near = null;
            float best = float.MaxValue;
            foreach (var t in tracks)
            {
                Vector3 d = pos - t.start.pos;
                if (Mathf.Abs(d.y) > 8f) continue;
                d.y = 0f;
                if (d.magnitude < best) { best = d.magnitude; near = t; }
            }
            candidate = near != null && best < StartRadius && car.Speed < StartSpeed && !menu ? near : null;
            if (near != null && best < 140f)
            {
                if (near != announced && !menu && candidate == null)
                {
                    announced = near;
                    string name = near.Name.ToUpperInvariant(), kind = near.Kind;
                    float km = near.Distance / 1000f;
                    game.Hud.Banner(Loc.F($"COURSE  ·  {name}  ·  {kind}  ·  {km:0.0} km  ·  ARRÊTE-TOI AU DÉPART",
                                          $"RACE  ·  {name}  ·  {kind}  ·  {km:0.0} km  ·  STOP AT THE START"), near.Tint);
                }
            }
            else if (best > 220f) announced = null;
            if (candidate != null && GameInput.InteractPressed) Begin(candidate);
        }

        /// <summary>Lance la course : la voiture est posée derrière la ligne, dans le sens du parcours ; décompte.</summary>
        public void Begin(Track t)
        {
            if (t == null || mode != Mode.Idle || game.State != GameManager.Phase.Playing) return;
            Active = t;
            mode = Mode.Countdown;
            candidate = null;
            announced = t;
            resultTimer = 0f;
            deltaTimer = 0f;
            countdown = Beat * 3f;
            shownBeat = 0;
            next = 0;
            elapsed = 0f;
            times = new float[t.GateCount];
            course.Clear();
            for (int lap = 0; lap < t.laps; lap++) course.AddRange(t.path);
            var car = game.Player;
            Vector3 f = t.start.forward;
            Vector3 p = t.path[0] - f * StartBack + Vector3.up * 0.5f;
            game.Traffic.ClearAround(p, 18f);
            car.Teleport(p, Quaternion.LookRotation(f, Vector3.up), 0f);
            game.SetInputEnabled(false);
            game.Nav.SetCourse(course, 0, t.fr, t.en);
        }

        void UpdateCountdown(VehicleController car, Vector3 pos, bool menu, float dt)
        {
            // Voiture tenue sur la ligne (un menu refermé pendant le décompte lui rend la main)
            if (!menu) game.SetInputEnabled(false);
            countdown -= dt;
            int beat = Mathf.CeilToInt(countdown / Beat);
            if (beat != shownBeat && beat >= 1)
            {
                shownBeat = beat;
                if (game.Local.Audio) game.Local.Audio.Rev(beat == 1 ? 1f : 0.7f);
                game.Camera.Shake(0.1f);
            }
            if (countdown > 0f) return;
            mode = Mode.Running;
            if (!menu) game.SetInputEnabled(true);
            elapsed = 0f;
            sinceGate = lostTimer = abandonHeld = 0f;
            goTimer = 0.9f;
            prevPos = pos;
            teleports = car.TeleportCount;
            game.Hud.Flash(Active.Tint, 0.18f);
            if (game.Local.Audio) game.Local.Audio.Chime();
        }

        void UpdateRace(VehicleController car, Vector3 pos, bool menu, float dt)
        {
            elapsed += dt;
            sinceGate += dt;
            // Replacement sur la route : pas de franchissement entre l'ancienne et la nouvelle position
            if (car.TeleportCount != teleports) { teleports = car.TeleportCount; prevPos = pos; }
            var g = GateOf(next);
            float d0 = Vector3.Dot(prevPos - g.pos, g.forward), d1 = Vector3.Dot(pos - g.pos, g.forward);
            if (d0 < 0f && d1 >= 0f)
            {
                // Franchie entre deux images : l'instant exact est interpolé
                float k = d0 / (d0 - d1);
                Vector3 cross = Vector3.Lerp(prevPos, pos, k);
                if (Mathf.Abs(Vector3.Dot(cross - g.pos, g.right)) < g.half + GateMargin && Mathf.Abs(cross.y - g.pos.y) < 9f)
                {
                    prevPos = pos;
                    Pass(elapsed - dt * (1f - k));
                    return;
                }
            }
            prevPos = pos;

            // Abandon : touche maintenue, magasin ouvert, parcours quitté pour de bon ou plus aucune porte franchie
            if (Dealership.IsOpen || TuningGarage.IsOpen) { Abandon(); return; }
            abandonHeld = !menu && AbandonKey ? abandonHeld + Time.unscaledDeltaTime : 0f;
            Vector3 behind = next > 0 ? GateOf(next - 1).pos : Active.start.pos;
            bool lost = Flat(pos - g.pos) > LostDistance && Flat(pos - behind) > LostDistance;
            lostTimer = lost ? lostTimer + dt : 0f;
            if (abandonHeld >= AbandonHold || lostTimer > LostTime || sinceGate > GateTimeout) Abandon();
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static bool AbandonKey
        {
            get
            {
                var kb = Keyboard.current;
                if (kb != null && kb.backspaceKey.isPressed) return true;
                foreach (var p in Gamepad.all) if (p.buttonWest.isPressed) return true;
                return false;
            }
        }

        /// <summary>Porte franchie à l'instant <paramref name="time"/> : écart au record, tour suivant, ou arrivée.</summary>
        void Pass(float time)
        {
            var t = Active;
            times[next] = time;
            bool compare = t.splits != null && t.splits.Length == times.Length;
            if (compare) { deltaValue = time - t.splits[next]; deltaTimer = 4f; }
            if (next == times.Length - 1) { Finish(time); return; }
            int passed = next++;
            sinceGate = 0f;
            int perLap = t.gates.Length, lap = next / perLap;
            if (next % perLap == 0)
            {
                string split = Clock(time);
                game.Hud.Banner(lap == t.laps - 1 ? Loc.F($"DERNIER TOUR  ·  {split}", $"FINAL LAP  ·  {split}")
                                                  : Loc.F($"TOUR {lap + 1}/{t.laps}  ·  {split}", $"LAP {lap + 1}/{t.laps}  ·  {split}"), t.Tint);
            }
            game.Hud.Flash(t.Tint, 0.1f);
            if (game.Local.Audio) game.Local.Audio.Chime();
            // Le GPS ne garde que ce qu'il reste à parcourir
            game.Nav.SetCourse(course, Mathf.Max(0, passed / perLap * t.path.Count + t.gates[passed % perLap].index - 1), t.fr, t.en);
        }

        void Finish(float time)
        {
            var t = Active;
            float old = t.best;
            bool record = old <= 0f || time < old;
            if (record)
            {
                t.best = time;
                t.splits = (float[])times.Clone();
                t.bestCar = game.Player.Spec != null ? game.Player.Spec.displayName : "";
                Save(t);
            }
            resultTrack = t;
            resultTime = time;
            resultRecord = record;
            resultHasDelta = old > 0f;
            resultDelta = time - old;
            resultTimer = 9f;
            string name = t.Name.ToUpperInvariant(), clock = Clock(time), gap = resultHasDelta ? "  (" + Delta(resultDelta) + ")" : "";
            game.Hud.Banner(record
                ? Loc.F($"ARRIVÉE  ·  {name}  ·  {clock}  ·  NOUVEAU RECORD{gap}", $"FINISH  ·  {name}  ·  {clock}  ·  NEW RECORD{gap}")
                : Loc.F($"ARRIVÉE  ·  {name}  ·  {clock}{gap}", $"FINISH  ·  {name}  ·  {clock}{gap}"), record ? Good : t.Tint);
            game.Hud.Flash(record ? Good : t.Tint, 0.3f);
            game.Camera.Shake(0.25f);
            if (game.Local.Audio) game.Local.Audio.Chime();
            Stop();
            Finished?.Invoke(t, time, record);
        }

        void Abandon()
        {
            game.Hud.Banner(Loc.T("COURSE ABANDONNÉE", "RACE ABANDONED"), Bad);
            Stop();
        }

        /// <summary>Fin de la course (arrivée, abandon, retour à l'accueil) : la balade reprend.</summary>
        void Stop()
        {
            if (mode == Mode.Countdown && !(game.Hud && game.Hud.Paused)) game.SetInputEnabled(true);
            mode = Mode.Idle;
            announced = Active;     // pas d'annonce du départ que l'on vient de quitter (ligne d'une boucle)
            Active = null;
            goTimer = abandonHeld = 0f;
            if (game.Nav) game.Nav.ClearCourse();
        }

        // ------------------------------------------------------------------ portes (monde)

        /// <summary>
        /// Porte : portique plein (deux poteaux et une traverse, lumineux, sans collision : on passe au travers) bien
        /// visible de jour, prolongé par deux colonnes de lumière qui la signalent de loin et la nuit.
        /// </summary>
        class GateFx
        {
            static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), EmissionColor = Shader.PropertyToID("_EmissionColor"), BaseMap = Shader.PropertyToID("_BaseMap");
            const float PostHeight = 7f, PostWidth = 0.42f, BeamHeight = 55f;
            readonly Transform root, leftPost, rightPost, bar;
            readonly LineRenderer leftBeam, rightBeam;
            readonly Material solid, glow;

            public GateFx(Transform parent, GameAssets assets)
            {
                root = new GameObject("RaceGate").transform;
                root.SetParent(parent, false);
                root.gameObject.SetActive(false);
                solid = new Material(assets.emissiveWhite) { name = "Race gate (runtime)" };
                solid.EnableKeyword("_EMISSION");
                leftPost = Post("Left");
                rightPost = Post("Right");
                bar = Post("Bar");
                if (!assets.spark) return;
                // Colonnes : additif, sans la texture de point des étincelles (elle ne s'allumerait qu'à mi-hauteur)
                glow = new Material(assets.spark) { name = "Race gate glow (runtime)" };
                glow.SetTexture(BaseMap, Texture2D.whiteTexture);
                leftBeam = Beam("LeftBeam");
                rightBeam = Beam("RightBeam");
            }

            Transform Post(string name)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                var box = go.GetComponent<Collider>();
                box.enabled = false;
                Destroy(box);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = solid;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                return go.transform;
            }

            LineRenderer Beam(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = glow;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                 new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.35f, 0.3f), new GradientAlphaKey(0f, 1f) });
                line.colorGradient = gradient;
                return line;
            }

            public void Show(Gate g, Color tint, float strength, Vector3 eye)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                root.SetPositionAndRotation(g.pos, Quaternion.LookRotation(g.forward, Vector3.up));
                // Poteaux un peu enfoncés : la route peut être en dévers ou en pente
                var post = new Vector3(PostWidth, PostHeight + 1f, PostWidth);
                leftPost.localPosition = new Vector3(-g.half, PostHeight * 0.5f - 0.5f, 0f);
                rightPost.localPosition = new Vector3(g.half, PostHeight * 0.5f - 0.5f, 0f);
                leftPost.localScale = rightPost.localScale = post;
                bar.localPosition = new Vector3(0f, PostHeight - 0.35f, 0f);
                bar.localScale = new Vector3(g.half * 2f + PostWidth, 0.7f, PostWidth * 0.7f);
                // Couleur surtout émise (lisible la nuit), sans dépasser le blanc : la teinte du tracé reste franche
                float lit = Mathf.Clamp01(strength);
                solid.SetColor(BaseColor, new Color(tint.r * 0.3f, tint.g * 0.3f, tint.b * 0.3f, 1f));
                solid.SetColor(EmissionColor, new Color(tint.r * lit, tint.g * lit, tint.b * lit, 1f));
                if (!glow) return;
                glow.SetColor(BaseColor, new Color(tint.r * 1.6f * strength, tint.g * 1.6f * strength, tint.b * 1.6f * strength, 1f));
                // Visibles de loin : l'épaisseur grandit avec la distance
                float width = Mathf.Clamp(Vector3.Distance(eye, g.pos) * 0.0045f, PostWidth, 5f);
                Vector3 a = g.pos - g.right * g.half + Vector3.up * (PostHeight - 0.5f), b = g.pos + g.right * g.half + Vector3.up * (PostHeight - 0.5f);
                leftBeam.SetPosition(0, a);
                leftBeam.SetPosition(1, a + Vector3.up * BeamHeight);
                rightBeam.SetPosition(0, b);
                rightBeam.SetPosition(1, b + Vector3.up * BeamHeight);
                leftBeam.widthMultiplier = rightBeam.widthMultiplier = width;
            }

            public void Hide()
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
            }
        }

        GateFx[] startFx;
        GateFx nextFx, afterFx;

        /// <summary>
        /// En balade : la porte de départ de chaque tracé. En course : la prochaine porte, et la suivante en plus
        /// discret (la dernière, l'arrivée, est blanche).
        /// </summary>
        void UpdateGates(bool playing)
        {
            if (startFx == null)
            {
                if (!game.assets.emissiveWhite) return;
                startFx = new GateFx[tracks.Count];
                for (int i = 0; i < startFx.Length; i++) startFx[i] = new GateFx(transform, game.assets);
                nextFx = new GateFx(transform, game.assets);
                afterFx = new GateFx(transform, game.assets);
            }
            Vector3 eye = game.Camera.transform.position;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.08f;
            for (int i = 0; i < startFx.Length; i++)
            {
                var t = tracks[i];
                bool show = playing && (mode == Mode.Idle ? Flat(eye - t.start.pos) < 1800f : mode == Mode.Countdown && t == Active);
                if (show) startFx[i].Show(t.start, t.Tint, pulse, eye);
                else startFx[i].Hide();
            }
            if (playing && mode != Mode.Idle)
            {
                int last = Active.GateCount - 1;
                nextFx.Show(GateOf(next), next == last ? FinishTint : Active.Tint, pulse, eye);
                if (next < last) afterFx.Show(GateOf(next + 1), next + 1 == last ? FinishTint : Active.Tint, 0.4f, eye);
                else afterFx.Hide();
            }
            else
            {
                nextFx.Hide();
                afterFx.Hide();
            }
        }

        // ------------------------------------------------------------------ minicarte

        readonly List<RectTransform> miniStarts = new List<RectTransform>();
        RectTransform miniNext;

        /// <summary>Minicarte (en gris) : pastille des départs en balade, de la prochaine porte en course (au bord si elle est hors champ).</summary>
        void DrawOnMiniMap(RectTransform root, Func<Vector3, Vector2> toUi, float radius)
        {
            if (tracks == null) return;
            if (!miniNext)
            {
                miniStarts.Clear();
                foreach (var t in tracks) miniStarts.Add(MiniMark(root, 15f));
                miniNext = MiniMark(root, 17f);
            }
            float edge = radius - 9f;
            for (int i = 0; i < miniStarts.Count; i++)
            {
                Vector2 p = toUi(tracks[i].start.pos);
                bool show = mode == Mode.Idle && p.sqrMagnitude < edge * edge;
                miniStarts[i].gameObject.SetActive(show);
                if (show) miniStarts[i].anchoredPosition = p;
            }
            miniNext.gameObject.SetActive(mode != Mode.Idle);
            if (mode != Mode.Idle)
            {
                Vector2 p = toUi(GateOf(next).pos);
                if (p.sqrMagnitude > edge * edge) p = p.normalized * edge;
                miniNext.anchoredPosition = p;
            }
        }

        static RectTransform MiniMark(RectTransform root, float size)
        {
            var mark = Box(root, "RaceMark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var traffic = root.Find("Traffic");
            if (traffic) mark.SetSiblingIndex(traffic.GetSiblingIndex() + 1);
            Disc(mark, size, new Color(0.08f, 0.08f, 0.08f, 1f));
            Disc(mark, size - 4.5f, Color.white);
            Disc(mark, size - 10f, new Color(0.08f, 0.08f, 0.08f, 1f));
            return mark;
        }

        static void Disc(RectTransform parent, float size, Color color)
        {
            var img = Box(parent, "Disc", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
            img.sprite = HudArt.Disc;
            img.color = color;
            img.raycastTarget = false;
        }

        // ------------------------------------------------------------------ HUD

        RectTransform hudRoot;
        CanvasGroup panelGroup, promptGroup, countGroup;
        HudShape panelStripe, promptStripe;
        Text titleText, timeText, progressText, recordText, deltaText, abandonText, countText, promptTitle, promptDetail;
        RectTransform abandonFill;

        void BuildHud()
        {
            var go = new GameObject("RaceHud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            hudRoot = (RectTransform)go.transform;

            // Chrono : haut droite, sous le lieu-dit ; textes calés à droite, liseré de la couleur du tracé
            var corner = new Vector2(1f, 1f);
            var panel = Box(hudRoot, "Timer", corner, new Vector2(-26f, -128f), new Vector2(500f, 168f));
            panel.pivot = corner;
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = panelGroup.interactable = false;
            Shape(panel, "Plate", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(500f, 168f), Plate).Set(16f, 190f, 0f);
            panelStripe = Shape(panel, "Stripe", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(6f, 168f), SprintTint);
            panelStripe.Set(16f, 0f, 0f);
            titleText = Label(panel, 22, TextAnchor.UpperRight, White, corner, new Vector2(-24f, -10f), new Vector2(470f, 28f), FontStyle.BoldAndItalic);
            timeText = Label(panel, 62, TextAnchor.UpperRight, White, corner, new Vector2(-24f, -32f), new Vector2(470f, 70f), FontStyle.BoldAndItalic);
            // Écart au record : à gauche du temps
            deltaText = Label(panel, 30, TextAnchor.UpperRight, Good, corner, new Vector2(-285f, -58f), new Vector2(200f, 36f), FontStyle.BoldAndItalic);
            progressText = Label(panel, 22, TextAnchor.UpperRight, White, corner, new Vector2(-24f, -104f), new Vector2(470f, 28f), FontStyle.Bold);
            recordText = Label(panel, 20, TextAnchor.UpperRight, Dim, corner, new Vector2(-24f, -134f), new Vector2(470f, 26f), FontStyle.Normal);
            // Abandon : rappel de la touche au départ, jauge pendant l'appui
            abandonText = Label(panel, 17, TextAnchor.UpperRight, Dim, corner, new Vector2(-24f, -176f), new Vector2(470f, 22f), FontStyle.Normal);
            var bar = Box(panel, "AbandonBar", corner, new Vector2(-24f, -202f), new Vector2(200f, 4f));
            bar.pivot = corner;
            bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);
            abandonFill = Box(bar, "Fill", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(200f, 4f));
            abandonFill.pivot = new Vector2(1f, 0.5f);
            abandonFill.gameObject.AddComponent<Image>().color = Bad;

            // Décompte : grand chiffre au centre
            countText = Label(hudRoot, 190, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(900f, 220f), FontStyle.BoldAndItalic);
            countText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var edge = countText.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.5f);
            edge.effectDistance = new Vector2(3f, -3f);
            countGroup = countText.gameObject.AddComponent<CanvasGroup>();
            countGroup.alpha = 0f;
            countGroup.blocksRaycasts = countGroup.interactable = false;

            // Invite de départ (bouton : clic ou doigt), à l'arrêt sur la porte
            var prompt = Box(hudRoot, "StartPrompt", new Vector2(0.5f, 0.24f), Vector2.zero, new Vector2(880f, 96f));
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
            promptGroup.blocksRaycasts = promptGroup.interactable = false;
            var plate = Shape(prompt, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 96f), new Color(0.02f, 0.03f, 0.05f, 0.75f));
            plate.Set(18f, 0f, 0f);
            plate.raycastTarget = true;
            promptStripe = Shape(prompt, "Stripe", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(520f, 3f), SprintTint);
            promptStripe.Set(0f, 140f, 140f);
            var button = prompt.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Begin(candidate));
            promptTitle = Label(prompt, 28, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(860f, 40f), FontStyle.BoldAndItalic);
            promptTitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            promptTitle.supportRichText = true;
            promptDetail = Label(prompt, 21, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.78f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(860f, 30f), FontStyle.Normal);
            promptDetail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        void UpdateHud(float dt, bool visible)
        {
            if (!hudRoot)
            {
                if (!visible) return;
                BuildHud();
            }
            if (hudRoot.gameObject.activeSelf != visible) hudRoot.gameObject.SetActive(visible);
            if (!visible) return;

            // Chrono : pendant la course, puis le résultat quelques secondes
            if (resultTimer > 0f) resultTimer -= dt;
            if (deltaTimer > 0f) deltaTimer -= dt;
            var shown = Active ?? (resultTimer > 0f ? resultTrack : null);
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, shown != null ? 1f : 0f, dt * 5f);
            if (Active != null)
            {
                var t = Active;
                int perLap = t.gates.Length;
                panelStripe.color = titleText.color = t.Tint;
                titleText.text = t.Kind + "  ·  " + t.Name.ToUpperInvariant();
                timeText.text = Clock(mode == Mode.Running ? elapsed : 0f);
                timeText.color = White;
                string points = Loc.F($"PORTE {next}/{times.Length}", $"GATE {next}/{times.Length}");
                progressText.text = t.Loop ? Loc.F($"TOUR {next / perLap + 1}/{t.laps}   ·   ", $"LAP {next / perLap + 1}/{t.laps}   ·   ") + points : points;
                recordText.text = t.best > 0f ? Loc.T("RECORD  ", "BEST  ") + Clock(t.best) : Loc.T("PAS ENCORE DE RECORD", "NO BEST TIME YET");
                deltaText.text = deltaTimer > 0f ? Delta(deltaValue) : "";
                deltaText.color = deltaValue <= 0f ? Good : Bad;
                // Rappel de la touche d'abandon au départ, puis seulement pendant l'appui
                bool hint = mode == Mode.Running && !TouchInput.Showing && (elapsed < 7f || abandonHeld > 0f);
                abandonText.text = !hint ? "" : GameInput.UsingGamepad ? Loc.T("Maintenir X : abandonner", "Hold X: abandon")
                                                                       : Loc.T("Maintenir Retour arrière : abandonner", "Hold Backspace: abandon");
                abandonFill.parent.gameObject.SetActive(abandonHeld > 0f);
                abandonFill.localScale = new Vector3(Mathf.Clamp01(abandonHeld / AbandonHold), 1f, 1f);
            }
            else if (shown != null)
            {
                panelStripe.color = titleText.color = shown.Tint;
                titleText.text = Loc.T("ARRIVÉE  ·  ", "FINISH  ·  ") + shown.Name.ToUpperInvariant();
                timeText.text = Clock(resultTime);
                timeText.color = resultRecord ? Good : White;
                progressText.text = resultRecord ? Loc.T("NOUVEAU RECORD", "NEW BEST TIME") : "";
                recordText.text = resultRecord
                    ? (resultHasDelta ? Loc.T("ANCIEN RECORD  ", "PREVIOUS BEST  ") + Clock(resultTime - resultDelta) : "")
                    : Loc.T("RECORD  ", "BEST  ") + Clock(shown.best);
                deltaText.text = resultHasDelta ? Delta(resultDelta) : "";
                deltaText.color = resultDelta <= 0f ? Good : Bad;
                abandonText.text = "";
                abandonFill.parent.gameObject.SetActive(false);
            }

            // Décompte, puis « GO ! » un instant
            if (goTimer > 0f) goTimer -= dt;
            bool counting = mode == Mode.Countdown && shownBeat >= 1;
            if (counting)
            {
                countText.text = shownBeat.ToString();
                countText.color = White;
                // Chaque chiffre arrive en grand puis se pose
                float k = Mathf.Clamp01((shownBeat * Beat - countdown) / 0.25f);
                countText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, k);
            }
            else if (goTimer > 0f)
            {
                countText.text = Loc.T("GO !", "GO!");
                countText.color = Good;
                countText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.3f, 1f - goTimer / 0.9f);
            }
            countGroup.alpha = counting ? 1f : Mathf.Clamp01(goTimer / 0.35f);

            // Invite
            bool ask = candidate != null && mode == Mode.Idle;
            if (ask)
            {
                var t = candidate;
                promptStripe.color = t.Tint;
                string hex = ColorUtility.ToHtmlStringRGB(t.Tint);
                string key = TouchInput.Showing ? Loc.T("touche ici", "tap here") : GameInput.UsingGamepad ? "A" : Loc.T("E ou Entrée", "E or Enter");
                promptTitle.text = Loc.F($"<color=#{hex}>{t.Kind}</color>   ·   {t.Name.ToUpperInvariant()}   ·   {t.Distance / 1000f:0.0} km");
                string best = t.best <= 0f ? Loc.T("Pas encore de record", "No best time yet")
                    : Loc.T("Record ", "Best ") + Clock(t.best) + (string.IsNullOrEmpty(t.bestCar) ? "" : "  (" + t.bestCar + ")");
                promptDetail.text = best + Loc.T($"   ·   {key} : lancer le chrono", $"   ·   {key}: start the clock");
            }
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, ask ? 1f : 0f, dt * 5f);
            promptGroup.blocksRaycasts = promptGroup.interactable = ask;
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            // CanvasRenderer d'office : sans lui, un graphique ajouté ensuite ne s'affiche pas
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static HudShape Shape(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Box(parent, name, anchor, pos, size);
            rt.pivot = anchor;
            var s = rt.gameObject.AddComponent<HudShape>();
            s.color = color;
            s.raycastTarget = false;
            return s;
        }

        static Text Label(Transform parent, int size, TextAnchor align, Color color, Vector2 anchor, Vector2 pos, Vector2 box, FontStyle style)
        {
            var rt = Box(parent, "Text", anchor, pos, box);
            rt.pivot = anchor;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = HudArt.Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }
    }
}
