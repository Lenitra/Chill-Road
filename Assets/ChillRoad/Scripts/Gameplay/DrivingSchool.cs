using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Centre de pilotage (lieu « school » de la carte, Tools/Map/map_school.py) : un terrain d'essai dans la vallée est,
    /// relié à la route. Les leçons s'enchaînent dès qu'on y arrive, mais tout y reste libre (bac à sable) : accélérer
    /// sur la zone verte de la ligne droite ouest, freiner dans la zone rouge jusqu'à s'arrêter dans le cadre, passer le
    /// radar et prendre le tremplin de la ligne droite est, tourner en 8 en glisse autour des deux poteaux de l'arène,
    /// puis courir le « Circuit de l'école » (une vraie course, avec ses portes, Races.cs). Des panneaux expliquent
    /// chaque zone, un bandeau du HUD donne la consigne en cours. Quitter la zone interrompt la leçon.
    /// Les distances sont comptées le long de la piste (arêtes ecole_piste_*) depuis le carrefour d'arrivée.
    /// </summary>
    public class DrivingSchool : MonoBehaviour
    {
        enum Step { None, ToStart, Accelerate, Brake, Radar, Jump, Eight, Race, Finished }

        // Mêmes valeurs que Tools/Map/map_school.py
        const float StartS = 8f, GreenFrom = 14f, GreenEnd = 105f, BoxFrom = 140f, BoxTo = 155f;
        const float RoadHalf = 6f, ArenaR = 30f, PylonReach = 24f, EightGoal = 300f, EightTimeout = 120f;
        const string Id = "ecole";
        const int Reward = 2500;
        const string DoneKey = "chillroad_school_done";

        static readonly Color Gold = new Color(1f, 0.76f, 0.2f), Info = new Color(0.35f, 0.85f, 1f), Good = new Color(0.5f, 1f, 0.6f);
        static readonly Color Bad = new Color(1f, 0.4f, 0.32f), Ink = new Color(0.08f, 0.1f, 0.14f);

        GameManager game;
        RectTransform panel;
        CanvasGroup panelGroup;
        Text titleText, bodyText;
        RectTransform barFill;
        GameObject barRoot;
        Transform ring;
        Material ringMat;
        GameObject world;

        bool searched, found, built;
        readonly List<Vector3> lap = new List<Vector3>(), access = new List<Vector3>();
        readonly List<float> lapS = new List<float>();
        float length;
        Vector3 arena;
        readonly List<Vector3> pylons = new List<Vector3>();
        Step step;
        float timer, targetKmh, turnA, turnB, lastA, lastB;
        bool reached, hasA, hasB, hooked;
        /// <summary>Événements reçus pendant l'image (radar flashé, saut réussi, course terminée).</summary>
        bool flashed, jumped, raced;

        public void Init(GameManager g, RectTransform parent)
        {
            game = g;
            BuildPanel(parent);
        }

        // ------------------------------------------------------------------ tracé

        bool Resolve()
        {
            if (searched) return found;
            var json = game.Map != null ? game.Map.Json : null;
            if (json == null || json.edges == null || json.pois == null) return false;
            searched = true;
            var parts = new List<MapData.EdgeJson>();
            foreach (var e in json.edges)
            {
                if (e.id == "ecole_acces") Read(e, access);
                else if (e.id.StartsWith("ecole_piste")) parts.Add(e);
            }
            parts.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            foreach (var e in parts) Read(e, lap);
            foreach (var p in json.pois)
                if (p.id == "school") arena = new Vector3(p.x, game.Map.Height(p.x, p.z), p.z);
            if (json.places != null)
                foreach (var p in json.places)
                    if (p.item == "school_pylon") pylons.Add(new Vector3(p.x, p.y, p.z));
            found = lap.Count > 10 && access.Count > 1 && pylons.Count == 2;
            if (!found) return false;
            float s = 0f;
            for (int i = 0; i < lap.Count; i++)
            {
                if (i > 0) s += Vector3.Distance(lap[i - 1], lap[i]);
                lapS.Add(s);
            }
            length = s;
            return true;
        }

        static void Read(MapData.EdgeJson e, List<Vector3> into)
        {
            for (int i = into.Count > 0 ? 3 : 0; i + 2 < e.pts.Length; i += 3)
                into.Add(new Vector3(e.pts[i], e.pts[i + 1], e.pts[i + 2]));
        }

        /// <summary>Point de la piste à l'abscisse s, tangente et gauche (à plat).</summary>
        Vector3 At(float s, out Vector3 tangent, out Vector3 left)
        {
            s = Mathf.Repeat(s, length);
            int lo = 0, hi = lapS.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (lapS[mid] <= s) lo = mid; else hi = mid;
            }
            float t = Mathf.InverseLerp(lapS[lo], lapS[hi], s);
            tangent = lap[hi] - lap[lo];
            tangent.y = 0f;
            tangent.Normalize();
            left = new Vector3(-tangent.z, 0f, tangent.x);
            return Vector3.Lerp(lap[lo], lap[hi], t);
        }

        Vector3 At(float s) => At(s, out _, out _);

        /// <summary>Abscisse sur la piste du point le plus proche (négative juste avant le carrefour d'arrivée) et distance.</summary>
        float Locate(Vector3 p, out float distance)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i + 1 < lap.Count; i++)
            {
                Vector3 a = lap[i], d = lap[i + 1] - a;
                d.y = 0f;
                Vector3 r = p - a;
                r.y = 0f;
                float k = Mathf.Clamp01(Vector3.Dot(r, d) / Mathf.Max(d.sqrMagnitude, 1e-4f));
                float q = (r - d * k).sqrMagnitude;
                if (q < best) { best = q; bestS = Mathf.Lerp(lapS[i], lapS[i + 1], k); }
            }
            distance = Mathf.Sqrt(best);
            return bestS > length - 60f ? bestS - length : bestS;
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        bool NearAccess(Vector3 p)
        {
            foreach (var q in access) if (Flat(p, q) < 12f) return true;
            return false;
        }

        // ------------------------------------------------------------------ leçons

        void Update()
        {
            if (!game || !Resolve() || !game.Player) return;
            Hook();
            var car = game.Player;
            Vector3 pos = car.transform.position;
            if (!built && Flat(pos, arena) < 800f) BuildWorld();
            float s = Locate(pos, out float off);
            bool onSite = off < 30f || InArena(pos, 6f) || NearAccess(pos);
            bool racing = game.Races && game.Races.Active != null;
            // La course de l'école se court avec le système de courses : la leçon attend son arrivée
            if (racing && step == Step.Race) { ShowPanel(false); ShowRing(false, Vector3.zero, 0f, Gold); return; }
            bool running = game.State == GameManager.Phase.Playing && !racing && car.InputEnabled;
            if (game.Hud && game.Hud.Paused) { ShowPanel(step != Step.None && step != Step.Finished); return; }
            if (!onSite || !running)
            {
                // Hors de la zone, la leçon s'arrête ; terminée, elle ne recommence qu'après être parti loin
                if (!onSite && (step != Step.Finished || Flat(pos, arena) > 500f)) step = Step.None;
                ShowPanel(false);
                ShowRing(false, Vector3.zero, 0f, Gold);
                flashed = jumped = raced = false;
                return;
            }
            if (step == Step.None) Go(Step.ToStart);
            timer += Time.deltaTime;
            Tick(car, pos, s, off);
            flashed = jumped = raced = false;
            ShowPanel(step != Step.Finished);
        }

        void Go(Step s)
        {
            step = s;
            timer = 0f;
            reached = false;
            turnA = turnB = 0f;
            hasA = hasB = false;
            barRoot.SetActive(false);
        }

        /// <summary>Radar, tremplin et course de l'école : on écoute leurs systèmes (une fois prêts).</summary>
        void Hook()
        {
            if (hooked || !game.Radars || !game.Stunts || !game.Races) return;
            hooked = true;
            game.Radars.Flashed += (r, kmh) => { if (r.data.id == Id) flashed = true; };
            game.Stunts.Completed += (st, dist) => { if (st.data.id == Id) jumped = true; };
            game.Races.Finished += (t, time, record) => { if (t.id == Id) raced = true; };
        }

        /// <summary>Dans l'arène ovale (marge en mètres) : à moins de son rayon du segment entre les deux poteaux.</summary>
        bool InArena(Vector3 p, float margin)
        {
            Vector3 a = pylons[0], b = pylons[1], d = b - a, r = p - a;
            d.y = 0f;
            r.y = 0f;
            float k = Mathf.Clamp01(Vector3.Dot(r, d) / Mathf.Max(d.sqrMagnitude, 1e-4f));
            return Flat(p, Vector3.Lerp(a, b, k)) < ArenaR + margin;
        }

        /// <summary>Tour autour d'un poteau : angle parcouru (degrés, signé) depuis la dernière image.</summary>
        static float Around(Vector3 pylon, Vector3 pos, ref float last, ref bool has)
        {
            float a = Mathf.Atan2(pos.z - pylon.z, pos.x - pylon.x) * Mathf.Rad2Deg;
            float d = has ? Mathf.DeltaAngle(last, a) : 0f;
            last = a;
            has = true;
            return d;
        }

        SpeedCameras.Radar SchoolRadar
        {
            get
            {
                foreach (var r in game.Radars.All) if (r.data.id == Id) return r;
                return null;
            }
        }

        static string Key(GameInput.Bind b) => GameInput.Short(b, GameInput.UsingGamepad);

        void Tick(VehicleController car, Vector3 pos, float s, float off)
        {
            float kmh = car.SpeedKmh;
            switch (step)
            {
                case Step.ToStart:
                    Vector3 start = At(StartS);
                    Say(Loc.T("CENTRE DE PILOTAGE", "DRIVING SCHOOL"), Loc.T("Rejoignez le cercle de départ, au bas de la ligne droite", "Reach the start circle at the foot of the straight"));
                    ShowRing(true, start, 5f, Gold);
                    if (Flat(pos, start) < 5.5f)
                    {
                        // Objectif à la portée de chaque voiture, même des plus lentes
                        targetKmh = Mathf.Min(70f, Mathf.Floor(car.Spec.TopSpeed * 3.6f * 0.8f / 5f) * 5f);
                        Go(Step.Accelerate);
                    }
                    break;
                case Step.Accelerate:
                    ShowRing(false, Vector3.zero, 0f, Gold);
                    if (kmh >= targetKmh) reached = true;
                    Say(Loc.T("1 · ACCÉLÉRER", "1 · ACCELERATE"), reached
                        ? Loc.T("Parfait, gardez le cap jusqu'à la zone rouge", "Well done, hold your line to the red zone")
                        : Loc.F($"Maintenez {Key(GameInput.Bind.Throttle)} : atteignez {targetKmh:0} km/h avant la zone rouge",
                                $"Hold {Key(GameInput.Bind.Throttle)}: reach {targetKmh:0} km/h before the red zone"));
                    Bar(Mathf.Clamp01(kmh / targetKmh), reached ? Good : Gold);
                    if (s >= GreenEnd)
                    {
                        if (reached) Go(Step.Brake);
                        else Retry(Loc.T("Pas assez vite", "Not fast enough"));
                    }
                    break;
                case Step.Brake:
                    bool inBox = s >= BoxFrom && s <= BoxTo && off <= RoadHalf + 0.6f;
                    Say(Loc.T("2 · FREINER", "2 · BRAKE"), Loc.T($"Appuyez sur {Key(GameInput.Bind.Brake)} : arrêtez-vous dans le cadre blanc", $"Press {Key(GameInput.Bind.Brake)}: stop inside the white box"));
                    ShowRing(true, At((BoxFrom + BoxTo) * 0.5f), 4f, Good);
                    if (car.Speed < 0.8f && timer > 0.5f)
                    {
                        if (inBox) { Praise(Loc.T("Arrêt parfait", "Perfect stop")); Go(Step.Radar); }
                        else Retry(Loc.T("Arrêtez-vous dans le cadre", "Stop inside the box"));
                    }
                    else if (s > BoxTo + 10f && s < BoxTo + 60f) Retry(Loc.T("Freinez plus tôt", "Brake earlier"));
                    break;
                case Step.Radar:
                    var radar = SchoolRadar;
                    if (radar == null || car.Spec.TopSpeed * 3.6f < radar.Target + 8f)
                    {
                        // Voiture trop lente pour ce radar : la leçon passe, l'annonce l'explique
                        if (radar != null && game.Hud) game.Hud.Announce(Loc.T("RADAR : VOITURE TROP LENTE", "SPEED CAMERA: CAR TOO SLOW"), Loc.F($"Il faut dépasser {radar.Target:0} km/h", $"You need over {radar.Target:0} km/h"), Info, 3f);
                        Go(Step.Jump);
                        break;
                    }
                    Say(Loc.T("3 · RADAR", "3 · SPEED CAMERA"), Loc.F($"Passez le radar de la ligne droite est à plus de {radar.Target:0} km/h", $"Pass the speed camera on the east straight above {radar.Target:0} km/h"));
                    ShowRing(true, radar.center, 5f, Gold);
                    if (flashed) { Praise(Loc.T("Flashé !", "Flashed!")); Go(Step.Jump); }
                    break;
                case Step.Jump:
                    StuntJumps.Stunt jump = null;
                    foreach (var st in game.Stunts.All) if (st.data.id == Id) jump = st;
                    if (jump == null) { Go(Step.Eight); break; }
                    Say(Loc.T("4 · TREMPLIN", "4 · JUMP"), Loc.F($"Prenez le tremplin à droite de la ligne droite est : {jump.data.target:0} m en l'air", $"Take the ramp on the right of the east straight: {jump.data.target:0} m in the air"));
                    ShowRing(true, jump.lip - jump.forward * 40f, 4f, Gold);
                    if (jumped) { Praise(Loc.T("Beau saut", "Nice jump")); Go(Step.Eight); }
                    break;
                case Step.Eight:
                    bool inside = InArena(pos, 0f);
                    bool sliding = car.SlipAngle > 10f && car.Speed > 4f && car.GroundedWheels > 0;
                    float dA = Around(pylons[0], pos, ref lastA, ref hasA), dB = Around(pylons[1], pos, ref lastB, ref hasB);
                    if (inside && sliding)
                    {
                        if (Flat(pos, pylons[0]) < PylonReach) turnA += dA;
                        if (Flat(pos, pylons[1]) < PylonReach) turnB += dB;
                    }
                    bool both = Mathf.Abs(turnA) >= EightGoal && Mathf.Abs(turnB) >= EightGoal;
                    bool eight = both && Mathf.Sign(turnA) != Mathf.Sign(turnB);
                    string hint = both
                        ? Loc.T("Dans l'autre sens autour du second poteau : c'est un 8 !", "The other way round the second pylon: make an 8!")
                        : Loc.T($"Glissez autour des deux poteaux en dessinant un 8 ({Key(GameInput.Bind.Handbrake)} pour lancer la glisse)", $"Slide round both pylons in a figure 8 ({Key(GameInput.Bind.Handbrake)} to kick the slide)");
                    Say(Loc.T("5 · DRIFT EN 8", "5 · FIGURE-8 DRIFT"), inside ? hint : Loc.T("Entrez dans l'arène ovale, le long de la ligne droite ouest", "Enter the oval arena beside the west straight"));
                    ShowRing(!inside, arena, 8f, Gold);
                    Bar((Mathf.Min(Mathf.Abs(turnA), EightGoal) + Mathf.Min(Mathf.Abs(turnB), EightGoal)) / (2f * EightGoal), Gold);
                    if (eight || timer > EightTimeout) { Praise(Loc.T("Beau 8 !", "Nice figure 8!")); Go(Step.Race); }
                    break;
                case Step.Race:
                    Races.Track track = null;
                    foreach (var t in game.Races.All) if (t.id == Id) track = t;
                    if (track == null) { Finish(); break; }
                    Say(Loc.T("6 · COURSE", "6 · RACE"), Loc.T($"Arrêtez-vous sur la ligne de départ de « {track.Name} », validez, puis bouclez les 2 tours", $"Stop on the “{track.Name}” start line, confirm, then finish both laps"));
                    ShowRing(true, track.start.pos, 6f, Info);
                    if (raced) Finish();
                    break;
            }
        }

        void Retry(string why)
        {
            if (game.Hud) game.Hud.Announce(why, Loc.T("On recommence depuis le départ", "Back to the start line"), Bad, 2.6f);
            Go(Step.ToStart);
        }

        void Praise(string text)
        {
            if (game.Hud) game.Hud.Announce(text, "", Good, 2f);
            if (game.Local.Audio) game.Local.Audio.Chime();
        }

        void Finish()
        {
            ShowRing(false, Vector3.zero, 0f, Gold);
            bool first = PlayerPrefs.GetInt(DoneKey, 0) == 0;
            if (first)
            {
                PlayerPrefs.SetInt(DoneKey, 1);
                PlayerPrefs.Save();
                Wallet.Add(Reward);
            }
            string detail = first ? Loc.F($"Permis en poche  ·  +{Wallet.Format(Reward)}", $"License earned  ·  +{Wallet.Format(Reward)}")
                                  : Loc.T("Toutes les leçons sont réussies", "All lessons passed");
            if (game.Hud) game.Hud.Announce(Loc.T("CENTRE DE PILOTAGE TERMINÉ", "DRIVING SCHOOL COMPLETE"), detail, Good, 4f);
            if (game.Local.Audio) game.Local.Audio.Chime();
            Go(Step.Finished);
        }

        // ------------------------------------------------------------------ bandeau du HUD

        void BuildPanel(RectTransform parent)
        {
            panel = Node(parent, "SchoolPanel", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(760f, 92f));
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.blocksRaycasts = panelGroup.interactable = false;
            panelGroup.alpha = 0f;
            var plate = Graphic<HudShape>(Node(panel, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 92f)));
            plate.color = new Color(0.03f, 0.04f, 0.06f, 0.92f);
            plate.Set(18f, 0f, 0f);
            titleText = Label("Title", 26, FontStyle.BoldAndItalic, new Vector2(0f, 22f), 34f, Gold);
            bodyText = Label("Body", 20, FontStyle.Normal, new Vector2(0f, -10f), 28f, Color.white);
            var bar = Node(panel, "Bar", new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(420f, 5f));
            barRoot = bar.gameObject;
            Graphic<Image>(bar).color = new Color(1f, 1f, 1f, 0.15f);
            barFill = Node(bar, "Fill", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(420f, 5f));
            barFill.pivot = new Vector2(0f, 0.5f);
            Graphic<Image>(barFill);
            barRoot.SetActive(false);
        }

        static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static T Graphic<T>(RectTransform rt) where T : Graphic
        {
            rt.gameObject.AddComponent<CanvasRenderer>();
            var g = rt.gameObject.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        Text Label(string name, int size, FontStyle style, Vector2 pos, float h, Color color)
        {
            var t = Graphic<Text>(Node(panel, name, new Vector2(0.5f, 0.5f), pos, new Vector2(720f, h)));
            t.font = HudArt.Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.color = color;
            return t;
        }

        void Say(string title, string body)
        {
            titleText.text = title;
            bodyText.text = body;
        }

        void Bar(float value, Color color)
        {
            barRoot.SetActive(true);
            barFill.localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
            barFill.GetComponent<Image>().color = color;
        }

        void ShowPanel(bool show)
        {
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        }

        // ------------------------------------------------------------------ décor : marquage au sol, repère, panneaux

        void ShowRing(bool show, Vector3 at, float radius, Color color)
        {
            if (!ring) return;
            ring.gameObject.SetActive(show);
            if (!show) return;
            at.y = Mathf.Max(at.y, game.Map.Height(at.x, at.z)) + 0.12f;
            ring.position = at;
            ring.rotation = Quaternion.Euler(0f, Time.time * 40f, 0f);
            ring.localScale = new Vector3(radius, 1f, radius);
            float pulse = 1.1f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
            var c = color * pulse;
            c.a = 1f;
            ringMat.SetColor("_BaseColor", c);
        }

        /// <summary>Point au sol à side mètres à gauche de la piste (posé sur la chaussée ou sur le terrain s'il est plus haut).</summary>
        Vector3 Mark(float s, float side, float lift = 0.05f)
        {
            Vector3 c = At(s, out _, out Vector3 left);
            Vector3 p = c + left * side;
            p.y = c.y;
            if (Mathf.Abs(side) > RoadHalf) p.y = Mathf.Max(p.y, game.Map.Height(p.x, p.z));
            p.y += lift;
            return p;
        }

        static void Face(RoadNetwork.MeshBuilder m, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => m.QuadFacing(a, b, c, d, Vector3.up);

        /// <summary>Bande en travers ou en long de la piste, de s0 à s1 et de side0 à side1, découpée pour suivre les courbes.</summary>
        void Band(RoadNetwork.MeshBuilder m, float s0, float s1, float side0, float side1)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 2.5f));
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(s0, s1, i / (float)n), b = Mathf.Lerp(s0, s1, (i + 1) / (float)n);
                Face(m, Mark(a, side0), Mark(a, side1), Mark(b, side0), Mark(b, side1));
            }
        }

        void BuildWorld()
        {
            built = true;
            world = new GameObject("DrivingSchool");
            var white = new RoadNetwork.MeshBuilder();
            var green = new RoadNetwork.MeshBuilder();
            var red = new RoadNetwork.MeshBuilder();
            var orange = new RoadNetwork.MeshBuilder();
            float h = RoadHalf - 0.45f;

            // Ligne droite : trait de départ, chevrons verts, barres rouges de plus en plus serrées, cadre d'arrêt
            Band(white, GreenFrom - 3f, GreenFrom - 2.2f, -h, h);
            for (float s = GreenFrom; s < GreenEnd - 4f; s += 7f)
            {
                const float len = 2.2f, thick = 1f, half = 2.5f;
                Face(green, Mark(s, -half), Mark(s + thick, -half), Mark(s + len, 0f), Mark(s + len + thick, 0f));
                Face(green, Mark(s + len, 0f), Mark(s + len + thick, 0f), Mark(s, half), Mark(s + thick, half));
            }
            float gap = 4.2f;
            for (float s = GreenEnd; s < BoxFrom - 1.5f; s += gap, gap = Mathf.Max(1.9f, gap * 0.9f)) Band(red, s, s + 1f, -h, h);
            Band(white, BoxFrom, BoxFrom + 0.35f, -h, h);
            Band(white, BoxTo - 0.35f, BoxTo, -h, h);
            Band(white, BoxFrom, BoxTo, -h, -h + 0.3f);
            Band(white, BoxFrom, BoxTo, h - 0.3f, h);

            // Vibreurs rouges et blancs dans les virages : posés SUR la chaussée, le long de ses deux bords, en blocs de 3 m
            // qui suivent la courbe (jamais en travers de la route)
            int k = 0;
            for (float s = BoxTo + 25f; s < length - 6f; s += 3f)
            {
                At(s - 6f, out Vector3 t0, out _);
                At(s + 6f, out Vector3 t1, out _);
                if (Mathf.Abs(Vector3.SignedAngle(t0, t1, Vector3.up)) < 6f) { k = 0; continue; }
                var c = k++ % 2 == 0 ? red : white;
                Band(c, s, s + 3f, RoadHalf - 1.1f, RoadHalf - 0.15f);
                Band(c, s, s + 3f, -RoadHalf + 0.15f, -RoadHalf + 1.1f);
            }

            // Arène de drift : bord de l'ovale en orange, cercle guide en tirets blancs autour de chaque poteau (le 8)
            const int seg = 36;
            Vector3 axis = pylons[1] - pylons[0];
            axis.y = 0f;
            float halfLen = axis.magnitude * 0.5f;
            axis.Normalize();
            Vector3 side = new Vector3(-axis.z, 0f, axis.x), mid = (pylons[0] + pylons[1]) * 0.5f;
            var outline = new List<Vector3>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * (0.5f + i / (float)seg);
                outline.Add(mid - axis * halfLen + (axis * Mathf.Cos(a) + side * Mathf.Sin(a)) * (ArenaR - 1.2f));
            }
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * (-0.5f + i / (float)seg);
                outline.Add(mid + axis * halfLen + (axis * Mathf.Cos(a) + side * Mathf.Sin(a)) * (ArenaR - 1.2f));
            }
            for (int i = 0; i < outline.Count; i++) Ribbon(orange, outline[i], outline[(i + 1) % outline.Count], 0.8f);
            foreach (var pylon in pylons)
                for (int i = 0; i < 48; i++)
                {
                    if (i % 4 >= 2) continue;
                    float a0 = i * Mathf.PI * 2f / 48f, a1 = (i + 1) * Mathf.PI * 2f / 48f;
                    Vector3 e0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), e1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Ribbon(white, pylon + e0 * 12f, pylon + e1 * 12f, 0.35f);
                }

            Paint("White", white, new Color(0.93f, 0.93f, 0.9f));
            Paint("Green", green, new Color(0.25f, 0.8f, 0.36f));
            Paint("Red", red, new Color(0.86f, 0.17f, 0.13f));
            Paint("Orange", orange, new Color(1f, 0.56f, 0.12f));
            BuildRing();
            BuildBoards();
        }

        /// <summary>Trait au sol de a à b (largeur w), posé sur le terrain.</summary>
        void Ribbon(RoadNetwork.MeshBuilder m, Vector3 a, Vector3 b, float w)
        {
            Vector3 d = b - a;
            d.y = 0f;
            Vector3 n = new Vector3(-d.z, 0f, d.x).normalized * (w * 0.5f);
            Vector3 G(Vector3 p)
            {
                p.y = game.Map.Height(p.x, p.z) + 0.05f;
                return p;
            }
            Face(m, G(a - n), G(a + n), G(b - n), G(b + n));
        }

        void Paint(string name, RoadNetwork.MeshBuilder m, Color color)
        {
            var shader = Shader.Find("ChillRoad/Toon");
            if (!shader || m.v.Count == 0) return;
            var mat = new Material(shader) { name = "School " + name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_SpecStrength")) mat.SetFloat("_SpecStrength", 0f);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(world.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = m.Build("School" + name);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Anneau lumineux qui montre le prochain point à atteindre (rayon 1 m, mis à l'échelle).</summary>
        void BuildRing()
        {
            var spark = game.assets ? game.assets.spark : null;
            if (!spark) return;
            var m = new RoadNetwork.MeshBuilder();
            const int seg = 48;
            for (int i = 0; i < seg; i++)
            {
                if (i % 4 == 3) continue;
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 e0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), e1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                m.QuadFacing(e0 * 0.93f, e1 * 0.93f, e0, e1, Vector3.up);
            }
            ringMat = new Material(spark) { name = "SchoolRing (runtime)" };
            if (ringMat.HasProperty("_BaseMap")) ringMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (ringMat.HasProperty("_Cull")) ringMat.SetFloat("_Cull", 0f);
            var go = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(world.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = m.Build("SchoolRing", Color.white);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = ringMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring = go.transform;
            go.SetActive(false);
        }

        /// <summary>Texte des panneaux (dans l'ordre de pose de map_school.py), dans la langue du jeu.</summary>
        void BuildBoards()
        {
            string throttle = GameInput.Short(GameInput.Bind.Throttle, false), brake = GameInput.Short(GameInput.Bind.Brake, false);
            string hand = GameInput.Short(GameInput.Bind.Handbrake, false);
            var texts = new[]
            {
                (Loc.T("CENTRE DE PILOTAGE", "DRIVING SCHOOL"), Loc.T("Accélérer, freiner, radar,\ntremplin, drift en 8, course :\ntout s'essaie ici", "Accelerate, brake, camera,\nramp, figure-8 drift, race:\ntry it all here")),
                (Loc.T("1 · ACCÉLÉRER", "1 · ACCELERATE"), Loc.T($"Zone verte : pied au plancher\n({throttle}) en ligne droite", $"Green zone: full throttle\n({throttle}) in a straight line")),
                (Loc.T("2 · FREINER", "2 · BRAKE"), Loc.T($"Zone rouge : freinez fort ({brake})\net arrêtez-vous dans le cadre", $"Red zone: brake hard ({brake})\nand stop inside the box")),
                (Loc.T("3 · RADAR", "3 · SPEED CAMERA"), Loc.T("Passez vite devant le radar :\nla photo s'affiche en haut", "Pass the camera fast:\nthe photo shows top right")),
                (Loc.T("4 · TREMPLIN", "4 · RAMP"), Loc.T("Quittez la piste à droite,\nlancé, sur la bande d'élan", "Leave the track on the right,\nat speed, onto the run-up")),
                (Loc.T("5 · DRIFT EN 8", "5 · FIGURE-8 DRIFT"), Loc.T($"Frein à main ({hand}), braquez,\nglissez autour des deux poteaux", $"Handbrake ({hand}), steer,\nslide round both pylons")),
                (Loc.T("6 · COURSE", "6 · RACE"), Loc.T("Arrêtez-vous sur la ligne,\nvalidez, 2 tours de circuit", "Stop on the line, confirm,\nthen 2 laps of the circuit")),
            };
            int k = 0;
            foreach (var p in game.Map.Json.places)
            {
                if (p.item != "school_board" || k >= texts.Length) continue;
                Vector3 face = Quaternion.Euler(0f, p.yaw, 0f) * Vector3.forward;
                var go = new GameObject("BoardText", typeof(RectTransform), typeof(Canvas));
                go.transform.SetParent(world.transform, false);
                var rt = (RectTransform)go.transform;
                rt.position = new Vector3(p.x, p.y + 2.22f, p.z) + face * 0.06f;
                rt.rotation = Quaternion.LookRotation(-face, Vector3.up);
                rt.sizeDelta = new Vector2(330f, 170f);
                rt.localScale = Vector3.one * 0.01f;
                go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                BoardLabel(rt, texts[k].Item1, 25, FontStyle.BoldAndItalic, new Vector2(0f, 94f), 30f, Color.white);
                BoardLabel(rt, texts[k].Item2, 23, FontStyle.Bold, new Vector2(0f, -2f), 130f, Ink);
                k++;
            }
        }

        static void BoardLabel(RectTransform parent, string text, int size, FontStyle style, Vector2 pos, float h, Color color)
        {
            var t = Graphic<Text>(Node(parent, "Text", new Vector2(0.5f, 0.5f), pos, new Vector2(320f, h)));
            t.font = HudArt.Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.05f;
            t.color = color;
            t.text = text;
        }

        void OnDestroy()
        {
            if (world) Destroy(world);
        }
    }
}
