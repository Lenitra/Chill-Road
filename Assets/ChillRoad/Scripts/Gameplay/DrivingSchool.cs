using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Auto-école (lieu « school » de la carte, Tools/Map/map_school.py) : un petit circuit relié à la route de la vallée,
    /// où les leçons s'enchaînent dès qu'on y arrive : accélérer sur la zone verte de la ligne droite, freiner dans la zone
    /// rouge jusqu'à s'arrêter dans le cadre, faire un tour de piste, puis glisser sur l'aire de drift. Des panneaux
    /// expliquent chaque zone, un bandeau du HUD donne la consigne en cours. Quitter le circuit interrompt la leçon.
    /// Les distances sont comptées le long de la piste (arêtes ecole_piste_*) depuis le carrefour d'arrivée.
    /// </summary>
    public class DrivingSchool : MonoBehaviour
    {
        enum Step { None, ToStart, Accelerate, Brake, Circuit, Drift, Finished }

        // Mêmes valeurs que Tools/Map/map_school.py
        const float StartS = 8f, GreenFrom = 14f, GreenEnd = 95f, BoxFrom = 130f, BoxTo = 146f, DriftR = 22f;
        const float RoadHalf = 3.6f, GateRadius = 9f, DriftGoal = 2.5f, DriftTimeout = 60f;
        const int Reward = 1500;
        const string DoneKey = "chillroad_school_done";
        static readonly float[] GateShares = { 0.42f, 0.56f, 0.7f, 0.86f, 0.985f };

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
        Vector3 driftCenter;
        Step step;
        int gate;
        float timer, driftTime, targetKmh;
        bool reached;

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
                if (p.id == "school") driftCenter = new Vector3(p.x, game.Map.Height(p.x, p.z), p.z);
            found = lap.Count > 10 && access.Count > 1;
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
            var car = game.Player;
            Vector3 pos = car.transform.position;
            if (!built && Flat(pos, driftCenter) < 700f) BuildWorld();
            float s = Locate(pos, out float off);
            float toDrift = Flat(pos, driftCenter);
            bool onSite = off < 24f || toDrift < DriftR + 5f || NearAccess(pos);
            bool running = game.State == GameManager.Phase.Playing && (!game.Races || game.Races.Active == null) && car.InputEnabled;
            if (game.Hud && game.Hud.Paused) { ShowPanel(step != Step.None && step != Step.Finished); return; }
            if (!onSite || !running)
            {
                if (!onSite) step = Step.None;
                ShowPanel(false);
                ShowRing(false, Vector3.zero, 0f, Gold);
                return;
            }
            if (step == Step.None) Go(Step.ToStart);
            timer += Time.deltaTime;
            Tick(car, pos, s, off, toDrift);
            ShowPanel(step != Step.Finished);
        }

        void Go(Step s)
        {
            step = s;
            timer = 0f;
            reached = false;
            gate = 0;
            driftTime = 0f;
            barRoot.SetActive(false);
        }

        static string Key(GameInput.Bind b) => GameInput.Short(b, GameInput.UsingGamepad);

        void Tick(VehicleController car, Vector3 pos, float s, float off, float toDrift)
        {
            float kmh = car.SpeedKmh;
            switch (step)
            {
                case Step.ToStart:
                    Vector3 start = At(StartS);
                    Say(Loc.T("AUTO-ÉCOLE", "DRIVING SCHOOL"), Loc.T("Rejoignez le cercle de départ, au début de la ligne droite", "Reach the start circle at the beginning of the straight"));
                    ShowRing(true, start, 5f, Gold);
                    if (Flat(pos, start) < 5f)
                    {
                        // Objectif à la portée de chaque voiture, même des plus lentes
                        targetKmh = Mathf.Min(60f, Mathf.Floor(car.Spec.TopSpeed * 3.6f * 0.8f / 5f) * 5f);
                        Go(Step.Accelerate);
                    }
                    break;
                case Step.Accelerate:
                    ShowRing(false, Vector3.zero, 0f, Gold);
                    if (kmh >= targetKmh) reached = true;
                    Say(Loc.T("ACCÉLÉRER", "ACCELERATE"), reached
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
                    Say(Loc.T("FREINER", "BRAKE"), Loc.T($"Appuyez sur {Key(GameInput.Bind.Brake)} : arrêtez-vous dans le cadre blanc", $"Press {Key(GameInput.Bind.Brake)}: stop inside the white box"));
                    ShowRing(true, At((BoxFrom + BoxTo) * 0.5f), 3f, Good);
                    if (car.Speed < 0.8f && timer > 0.5f)
                    {
                        if (inBox) { Praise(Loc.T("Arrêt parfait", "Perfect stop")); Go(Step.Circuit); }
                        else Retry(Loc.T("Arrêtez-vous dans le cadre", "Stop inside the box"));
                    }
                    else if (s > BoxTo + 8f) Retry(Loc.T("Freinez plus tôt", "Brake earlier"));
                    break;
                case Step.Circuit:
                    Vector3 g = At(GateShares[gate] * length);
                    Say(Loc.T("CIRCUIT", "CIRCUIT"), Loc.F($"Faites le tour, freinez avant les virages  ·  repère {gate + 1} / {GateShares.Length}",
                                                             $"Drive the lap, brake before the bends  ·  marker {gate + 1} / {GateShares.Length}"));
                    ShowRing(true, g, 6f, Info);
                    Bar(gate / (float)GateShares.Length, Info);
                    if (Flat(pos, g) < GateRadius && ++gate >= GateShares.Length)
                    {
                        Praise(Loc.T("Tour terminé", "Lap complete"));
                        Go(Step.Drift);
                    }
                    break;
                case Step.Drift:
                    bool inside = toDrift < DriftR + 1f;
                    Say(Loc.T("DRIFT", "DRIFT"), inside
                        ? Loc.T($"Prenez de la vitesse, braquez et tirez {Key(GameInput.Bind.Handbrake)} : faites glisser l'arrière", $"Build speed, steer and pull {Key(GameInput.Bind.Handbrake)}: let the rear slide")
                        : Loc.T("Rejoignez l'aire ronde, au bord de la ligne droite", "Head for the round pad beside the straight"));
                    ShowRing(!inside, driftCenter, DriftR, Gold);
                    if (inside && car.SlipAngle > 14f && car.Speed > 5f && car.GroundedWheels > 0) driftTime += Time.deltaTime;
                    Bar(driftTime / DriftGoal, Gold);
                    if (driftTime >= DriftGoal || timer > DriftTimeout) Finish();
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
            if (game.Hud) game.Hud.Announce(Loc.T("AUTO-ÉCOLE TERMINÉE", "DRIVING SCHOOL COMPLETE"), detail, Good, 4f);
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

            // Vibreurs rouges et blancs dans les virages
            int k = 0;
            for (float s = BoxTo + 20f; s < length - 12f; s += 3f)
            {
                At(s - 4f, out Vector3 t0, out _);
                At(s + 4f, out Vector3 t1, out _);
                float bend = Vector3.SignedAngle(t0, t1, Vector3.up);
                if (Mathf.Abs(bend) < 5f) continue;
                float sign = bend < 0f ? 1f : -1f;      // côté intérieur du virage
                Band(k++ % 2 == 0 ? red : white, s, s + 3f, sign * RoadHalf, sign * (RoadHalf + 1f));
            }

            // Aire de drift : grand cercle et cercle intérieur en tirets
            const int seg = 72;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 e0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), e1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Disc(orange, e0, e1, DriftR - 1.6f, DriftR - 0.6f);
                if (i % 6 < 3) Disc(orange, e0, e1, DriftR * 0.45f - 0.2f, DriftR * 0.45f + 0.2f);
            }

            Paint("White", white, new Color(0.93f, 0.93f, 0.9f));
            Paint("Green", green, new Color(0.25f, 0.8f, 0.36f));
            Paint("Red", red, new Color(0.86f, 0.17f, 0.13f));
            Paint("Orange", orange, new Color(1f, 0.56f, 0.12f));
            BuildRing();
            BuildBoards();
        }

        void Disc(RoadNetwork.MeshBuilder m, Vector3 e0, Vector3 e1, float r0, float r1)
        {
            Vector3 P(Vector3 e, float r)
            {
                Vector3 p = driftCenter + e * r;
                p.y = game.Map.Height(p.x, p.z) + 0.05f;
                return p;
            }
            Face(m, P(e0, r0), P(e1, r0), P(e0, r1), P(e1, r1));
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
                (Loc.T("AUTO-ÉCOLE", "DRIVING SCHOOL"), Loc.T("Apprenez les bases :\naccélérer, freiner,\ntourner et glisser", "Learn the basics:\naccelerate, brake,\nsteer and drift")),
                (Loc.T("1 · ACCÉLÉRER", "1 · ACCELERATE"), Loc.T($"Zone verte : pied au plancher\n({throttle}) en ligne droite", $"Green zone: full throttle\n({throttle}) in a straight line")),
                (Loc.T("2 · FREINER", "2 · BRAKE"), Loc.T($"Zone rouge : freinez fort ({brake})\net arrêtez-vous dans le cadre", $"Red zone: brake hard ({brake})\nand stop inside the box")),
                (Loc.T("3 · CIRCUIT", "3 · CIRCUIT"), Loc.T("Freinez avant le virage,\nréaccélérez à la sortie", "Brake before the bend,\naccelerate out of it")),
                (Loc.T("4 · DRIFT", "4 · DRIFT"), Loc.T($"Braquez, frein à main ({hand}),\npuis contre-braquez", $"Steer, handbrake ({hand}),\nthen countersteer")),
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
