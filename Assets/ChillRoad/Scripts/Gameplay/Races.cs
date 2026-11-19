using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Courses chronométrées, courues directement sur la carte : pas de chargement ni de monde à part, la circulation
    /// et les autres joueurs restent là. Deux genres de tracés :
    /// - sprint : un départ, une arrivée ailleurs ;
    /// - tours : une boucle à parcourir plusieurs fois, la ligne de départ est aussi la ligne d'arrivée.
    /// Un tracé est donné par quelques points de la carte (<see cref="Defs"/>) : son parcours est l'itinéraire le
    /// plus rapide de l'un à l'autre (<see cref="RouteGuide.Plan"/>), et ses points de passage sont posés dessus à
    /// intervalles réguliers. Rien n'est écrit dans la carte générée : les tracés suivent le réseau tel qu'il est.
    /// Déroulé : on s'arrête sur la porte de départ (visible de loin), l'invite ouvre le menu d'avant-course
    /// (<see cref="RaceSetup"/> : record, fantôme, trafic) d'où l'on lance le chrono ; décompte,
    /// puis il faut franchir les portes dans l'ordre, dans le sens de la course. Une porte n'a rien de solide : une
    /// ligne de lumière posée sur la route, deux projecteurs de bas-côté et leur fumée (<see cref="GateFx"/>). Le GPS affiche le parcours (ruban sur
    /// la route, minicarte). À l'arrivée, le temps est comparé au record personnel du tracé, gardé avec ses temps de
    /// passage : à chaque porte, l'écart au record s'annonce en haut au centre (<see cref="Hud.Announce"/>).
    /// Pendant la course, le cadre en haut à droite ne montre que l'avancement (pourcentage du sprint, ou tour) et le chrono.
    /// Le record court avec nous : sa voiture, en fantôme translucide, refait son parcours (<see cref="RaceGhost"/>).
    /// Seul, chaque joueur court pour lui. En partie réseau, « Lancer la course » propose la course à tous : le serveur
    /// tient la séance (<see cref="NetRace"/>), les autres joueurs reçoivent une invitation, et ceux qui l'acceptent
    /// sont posés sur la grille pour un décompte commun. La course elle-même se déroule ici comme en solo (portes,
    /// chrono, abandon, record et fantôme personnels) ; chacun remonte ses portes franchies, son avancement et son temps
    /// final, et le serveur en tire le classement, affiché en direct dans le cadre du chrono puis en tableau à la fin.
    /// </summary>
    public class Races : MonoBehaviour
    {
        const string BestKey = "chillroad_race_best_", SplitsKey = "chillroad_race_splits_", CarKey = "chillroad_race_car_";
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
        // Sol relevé autour d'une porte, pour y poser sa lumière : de GateBefore m avant la ligne à GateAfter m après
        const float GateBefore = 16f, GateAfter = 5f, GateStep = 1f;
        const float AbandonHold = 0.8f;
        const float LostDistance = 650f, LostTime = 6f, GateTimeout = 300f;
        /// <summary>Course à plusieurs avec collisions entre joueurs : les coureurs partent en file, espacés de cette distance (m).</summary>
        const float GridSpacing = 9f;

        /// <summary>Tracé : points de la carte (x, z) à relier dans l'ordre ; pour une boucle, le dernier revient au premier.</summary>
        class Def
        {
            public string id, fr, en;
            /// <summary>Pack de contenu qui l'a ajoutée (<see cref="ContentPacks"/>), vide = jeu de base.</summary>
            public string pack;
            public int laps = 1;
            /// <summary>Distance visée entre deux portes (m).</summary>
            public float spacing = 500f;
            /// <summary>Sens de départ approximatif (x, z).</summary>
            public Vector2 heading;
            public Vector2[] points;
            /// <summary>Côté de l'étiquette du départ sur la carte globale : -1 à gauche du repère, 0 au-dessus, 1 à droite.</summary>
            public int side;
            /// <summary>
            /// Sprint : nom du même tracé couru dans l'autre sens (de l'arrivée au départ). Donné, le sprint se court
            /// dans les deux sens : deux tracés, un départ à chaque bout, chacun son record (id + « _r »).
            /// </summary>
            public string fr2, en2;
            /// <summary>Côté de l'étiquette du départ à rebours sur la carte globale.</summary>
            public int side2 = 1;
        }

        static readonly Def[] Defs =
        {
            // Sprint : la route du col depuis Le Plan, du pied de la montée jusque sous le col (≈ 3,4 km, 250 m de dénivelé)
            new Def
            {
                id = "col", fr = "Montée du Col", en = "Pass Hill Climb", laps = 1, spacing = 500f, heading = new Vector2(-0.1f, 1f), side = -1, fr2 = "Descente du Col", en2 = "Pass Descent", side2 = -1,
                points = new[] { new Vector2(-35.8f, -1717.1f), new Vector2(118.7f, 243.3f) },
            },
            // Tours : départ sur la route de la vallée au bord du lac, vers Les Granges ; retour par l'autoroute et son
            // pont sur le lac, sortie du Plan, puis la route de la vallée par Le Port (≈ 6,2 km le tour)
            new Def
            {
                id = "lac", fr = "Tour du Lac", en = "Lake Loop", laps = 2, spacing = 700f, heading = new Vector2(0.4f, 1f), side = 0,
                points = new[] { new Vector2(1655.9f, -910.5f), new Vector2(2106.3f, -287.1f), new Vector2(484.9f, -2077.8f), new Vector2(550.1f, -1775.2f) },
            },
            // Les points intermédiaires sont posés au milieu des routes à suivre : chaque étape est l'itinéraire le plus
            // rapide d'un point au suivant, ils obligent le tracé à passer par là.
            // Sprint : la route de la station des Combes, de La Chenal au front de neige (≈ 3 km, haut enneigé)
            new Def
            {
                id = "station", fr = "Montée des Combes", en = "Les Combes Climb", spacing = 500f, heading = new Vector2(0.23f, 0.97f), side = 1, fr2 = "Descente des Combes", en2 = "Les Combes Descent", side2 = -1,
                points = new[] { new Vector2(-1679.8f, 165.6f), new Vector2(-1107.3f, 116.4f) },
            },
            // Sprint : la route de balcon, du Plan aux Granges par Les Hauts et Les Moulins (≈ 6,6 km)
            new Def
            {
                id = "balcon", fr = "Route du Balcon", en = "Balcony Road", spacing = 650f, heading = new Vector2(-0.97f, -0.23f), side = 1, fr2 = "Balcon à rebours", en2 = "Balcony Road Reversed", side2 = -1,
                points = new[] { new Vector2(124.5f, -1631.9f), new Vector2(841.2f, -865.6f), new Vector2(1494.0f, 131.7f), new Vector2(1618.0f, -246.3f) },
            },
            // Sprint : la route du col par l'est, des Granges au col (≈ 3,2 km)
            new Def
            {
                id = "col_est", fr = "Col par l'Est", en = "East Pass Climb", spacing = 500f, heading = new Vector2(-0.94f, -0.35f), side = -1, fr2 = "Descente de l'Est", en2 = "East Descent", side2 = 1,
                points = new[] { new Vector2(1825.6f, 120.8f), new Vector2(1180.4f, 298.3f), new Vector2(298.2f, 420.8f) },
            },
            // Sprint : descente du col vers Praz-Dessus (≈ 2 km)
            new Def
            {
                id = "col_nord", fr = "Descente du Nord", en = "North Descent", spacing = 400f, heading = new Vector2(-0.14f, 0.99f), side = 1, fr2 = "Col par le Nord", en2 = "North Pass Climb", side2 = -1,
                points = new[] { new Vector2(122.2f, 578.8f), new Vector2(205.1f, 1680.0f) },
            },
            // Sprint : l'autoroute par l'échangeur des Granges et le tunnel du nord-est, sortie à Praz-Dessus (≈ 4,5 km)
            new Def
            {
                id = "tunnel", fr = "Tunnel du Nord-Est", en = "North-East Tunnel", spacing = 700f, heading = new Vector2(0.94f, 0.34f), side = 1, fr2 = "Tunnel du Nord-Est, retour", en2 = "North-East Tunnel Return", side2 = 1,
                points = new[] { new Vector2(1896.4f, -117.5f), new Vector2(1992.9f, 1698.5f), new Vector2(332.0f, 1948.0f) },
            },
            // Sprints sur la terre : piste du Petit Pas (≈ 1,7 km, lacets), piste du Villard (≈ 1,7 km), route puis chemin
            // des Chalets (≈ 2,2 km)
            new Def
            {
                id = "petit_pas", fr = "Piste du Petit Pas", en = "Petit Pas Trail", spacing = 400f, heading = new Vector2(-0.45f, 0.89f), side = -1, fr2 = "Descente du Petit Pas", en2 = "Petit Pas Descent", side2 = 1,
                points = new[] { new Vector2(1731.0f, 1545.0f), new Vector2(1353.9f, 1124.5f) },
            },
            new Def
            {
                id = "villard", fr = "Piste du Villard", en = "Villard Trail", spacing = 400f, heading = new Vector2(-0.27f, -0.96f), side = 1, fr2 = "Descente du Villard", en2 = "Villard Descent", side2 = 1,
                points = new[] { new Vector2(-1691.2f, -772.7f), new Vector2(-1403.5f, -805.5f) },
            },
            new Def
            {
                id = "chalets", fr = "Chemin des Chalets", en = "Chalets Track", spacing = 450f, heading = new Vector2(0.88f, 0.48f), side = 1, fr2 = "Chalets, retour", en2 = "Chalets Track Return", side2 = 0,
                points = new[] { new Vector2(347.6f, 2353.4f), new Vector2(-37.8f, 2509.7f), new Vector2(-435.8f, 2490.1f) },
            },
            // Tours : le circuit du terrain d'essai, dans la vallée est (Tools/Map/map_school.py, ≈ 1 km le tour)
            new Def
            {
                id = "ecole", fr = "Circuit d'essai", en = "Proving Ground Circuit", laps = 2, spacing = 250f, heading = new Vector2(0f, 1f), side = 1,
                points = new[] { new Vector2(1738f, -830f), new Vector2(1930f, -830f), new Vector2(1830f, -1008f) },
            },
            // Tours : route du lac jusqu'aux Rives, retour par le chemin des Bois (terre) et l'échangeur sud (≈ 3,7 km)
            new Def
            {
                id = "rives", fr = "Boucle des Rives", en = "Lakeshore Loop", laps = 2, spacing = 600f, heading = new Vector2(0.99f, 0.12f), side = 0,
                points = new[] { new Vector2(542.7f, -1776.2f), new Vector2(1430.4f, -1939.0f), new Vector2(1116.1f, -2176.9f), new Vector2(349.9f, -1927.7f) },
            },
            // Tours : route de la vallée vers le Villard, chemin de la Gorge sous l'autoroute (passage inférieur), retour par l'échangeur ouest (≈ 2,7 km)
            new Def
            {
                id = "gorge", fr = "Boucle de la Gorge", en = "Gorge Loop", laps = 3, spacing = 450f, heading = new Vector2(-0.17f, -0.99f), side = -1,
                points = new[] { new Vector2(-1795.6f, -227.8f), new Vector2(-2263.9f, -218.4f), new Vector2(-1972.8f, 403.5f), new Vector2(-1853.5f, 215.6f) },
            },
            // Tours : le triangle des rues enneigées de la station (≈ 0,5 km)
            new Def
            {
                id = "station_tours", fr = "Critérium des Neiges", en = "Snow Criterium", laps = 5, spacing = 160f, heading = new Vector2(-0.46f, -0.89f), side = 0,
                points = new[] { new Vector2(-1039.1f, 305.9f), new Vector2(-1012.5f, 176.5f), new Vector2(-970.8f, 267.2f) },
            },
        };

        /// <summary>Porte : ligne en travers de la route, à franchir dans le sens de <see cref="forward"/>.</summary>
        public class Gate
        {
            public Vector3 pos, forward, right;
            /// <summary>Demi-largeur (m) et indice du point du parcours qui suit la porte.</summary>
            public float half;
            public int index;
            /// <summary>
            /// Profil de la route autour de la porte, tous les <see cref="GateStep"/> m le long du parcours : point de
            /// la chaussée dans l'axe de la porte, et vecteur « droite » à cet endroit (la route tourne et monte).
            /// </summary>
            public Vector3[] line, across;
        }

        public class Track
        {
            public string id, fr, en;
            /// <summary>Pack de contenu qui l'a ajoutée (<see cref="ContentPacks"/>), vide = jeu de base.</summary>
            public string pack;
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
            /// <summary>Parcours du record, rejoué en fantôme (null : pas de record) ; lu à la première course du tracé.</summary>
            public RaceGhost.Run ghost;
            public bool ghostLoaded;

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
        Track candidate;
        readonly List<Vector3> course = new List<Vector3>();
        float[] times;
        int next, shownBeat, teleports;
        float elapsed, countdown, goTimer, sinceGate, lostTimer, abandonHeld;
        Vector3 prevPos;
        // Fantôme du record, et relevé de la course en cours (le prochain fantôme si le record tombe)
        RaceGhost ghostFx;
        RaceGhost.Run recording;

        // Sprint : longueur du parcours jusqu'à chaque point, et avancement (0-1, ne recule pas)
        float[] alongPath;
        float progress;
        // Départ déjà annoncé en approchant (une fois par passage)
        Track announced;
        // Résultat de la dernière course (quelques secondes après l'arrivée)
        Track resultTrack;
        // Chrono teinté de la couleur du dernier écart au record, qui revient au blanc
        Color clockTint;
        float clockTintTimer;
        const float ClockTintTime = 3f;
        float resultTimer, resultTime;
        bool resultRecord;

        /// <summary>Tous les tracés (vide tant que la carte n'est pas chargée).</summary>
        public IReadOnlyList<Track> All { get { Load(); return tracks ?? None; } }
        /// <summary>Tracé en cours (décompte ou course), sinon null.</summary>
        public Track Active { get; private set; }
        public bool Running => mode == Mode.Running;
        /// <summary>Course terminée : tracé, temps, nouveau record.</summary>
        public event Action<Track, float, bool> Finished;

        RaceSetup setup;

        // Course à plusieurs (partie en réseau) : séance du serveur que l'on suit (0 : aucune), notre réponse à cette
        // séance puis notre état de coureur, et ce que l'on en remonte au serveur
        byte netSession;
        NetRaceStatus netStatus;
        NetRacePhase netPhase;
        int netGates;
        float netProgress, netTime;
        /// <summary>La course en cours ici est celle de la séance du serveur.</summary>
        bool netRace;
        /// <summary>Voiture imposée au volant : la nôtre revient à la fin de la course.</summary>
        bool borrowed;
        /// <summary>Invitation affichée, en attente de réponse.</summary>
        bool inviteOpen;
        /// <summary>Course que l'on vient de proposer au serveur, tant qu'il ne l'a pas reprise.</summary>
        Track proposed;

        /// <summary>Course en cours (décompte compris), seul ou à plusieurs : on ne change pas de voiture avant la fin.</summary>
        public bool CarLocked => borrowed || Active != null;
        /// <summary>Invitation à une course à plusieurs affichée, en attente de réponse (la touche d'entrée y répond).</summary>
        public bool InvitePending => inviteOpen;

        public void Init(GameManager g)
        {
            game = g;
            if (GameManager.DedicatedServer) return;
            MiniMap.Overlay += DrawOnMiniMap;
            setup = gameObject.AddComponent<RaceSetup>();
            setup.Init(g, this);
            if (g.Net) g.Net.ProposalDropped += OnProposalDropped;
        }

        void OnDestroy()
        {
            MiniMap.Overlay -= DrawOnMiniMap;
            if (game && game.Net) game.Net.ProposalDropped -= OnProposalDropped;
        }

        // ------------------------------------------------------------------ tracés

        void Load()
        {
            if (tracks != null || !game || game.Map == null || !game.Nav || !game.Road || game.Road.Edges.Count == 0) return;
            if (game.State == GameManager.Phase.Loading) return;
            tracks = new List<Track>();
            foreach (var def in Defs)
            {
                var t = Build(def, false, new Vector3(def.heading.x, 0f, def.heading.y));
                if (t != null) tracks.Add(t);
                else Debug.LogWarning($"[Races] Tracé « {def.id} » introuvable sur le réseau routier");
                if (t == null || t.Loop || string.IsNullOrEmpty(def.fr2)) continue;
                // Sprint dans l'autre sens : départ à l'arrivée, cap opposé à celui de l'arrivée
                Vector3 back = t.path[Mathf.Max(0, t.path.Count - 8)] - t.Finish;
                back.y = 0f;
                var r = Build(def, true, back);
                if (r != null) tracks.Add(r);
                else Debug.LogWarning($"[Races] Tracé « {def.id} » à rebours introuvable sur le réseau routier");
            }
        }

        Track Build(Def def, bool reverse, Vector3 heading)
        {
            var t = reverse
                ? new Track { id = def.id + "_r", fr = def.fr2, en = def.en2, pack = def.pack, laps = 1, side = def.side2 }
                : new Track { id = def.id, fr = def.fr, en = def.en, pack = def.pack, laps = Mathf.Max(1, def.laps), side = def.side };
            heading.y = 0f;
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;
            var points = (Vector2[])def.points.Clone();
            if (reverse) Array.Reverse(points);
            int n = points.Length;
            int legs = t.Loop ? n : n - 1;
            for (int i = 0; i < legs; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % n];
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
            // Profil du sol : le parcours donne la hauteur et le sens de la route, décalé jusqu'à l'axe de la porte
            float shift = Vector3.Dot(g.pos - p, g.right);
            int n = Mathf.RoundToInt((GateBefore + GateAfter) / GateStep) + 1;
            g.line = new Vector3[n];
            g.across = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float at = d - GateBefore + k * GateStep;
                Vector3 c = PathAt(t, dist, at);
                Vector3 dir = PathAt(t, dist, at + 1.5f) - PathAt(t, dist, at - 1.5f);
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : f;
                g.across[k] = new Vector3(dir.z, 0f, -dir.x);
                g.line[k] = c + g.across[k] * shift;
            }
            return g;
        }

        /// <summary>Point du parcours à la distance d du départ : une boucle se referme, un sprint se prolonge tout droit.</summary>
        static Vector3 PathAt(Track t, float[] dist, float d)
        {
            var path = t.path;
            float length = dist[dist.Length - 1];
            if (t.Loop) d = Mathf.Repeat(d, length);
            float c = Mathf.Clamp(d, 0f, length);
            int i = Array.BinarySearch(dist, c);
            if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, path.Count - 1);
            float seg = dist[i] - dist[i - 1];
            if (seg <= 0f) return path[i];
            return path[i - 1] + (path[i] - path[i - 1]) * ((d - dist[i - 1]) / seg);
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
                t.ghost = null;
                RaceGhost.Run.Delete(t.id);
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
                ghostFx?.Hide();
                UpdateGates(false);
                UpdateHud(0f, false);
                return;
            }
            var car = game.Player;
            Vector3 pos = car.transform.position;
            bool menu = game.Hud && game.Hud.Paused || OptionsMenu.IsOpen;
            float dt = Time.deltaTime;
            UpdateNet(menu);
            switch (mode)
            {
                case Mode.Idle: UpdateIdle(car, pos, menu); break;
                case Mode.Countdown: UpdateCountdown(car, pos, menu, dt); break;
                case Mode.Running: UpdateRace(car, pos, menu, dt); break;
            }
            UpdateGates(true);
            UpdateGhost();
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
            // Course à plusieurs proposée ou acceptée : on attend son départ, pas de menu d'avant-course
            bool waiting = proposed != null || netSession != 0 && netStatus == NetRaceStatus.Accepted;
            candidate = near != null && best < StartRadius && car.Speed < StartSpeed && !menu && !waiting ? near : null;
            if (near != null && best < 140f)
            {
                if (near != announced && !menu && candidate == null && !waiting)
                {
                    announced = near;
                    string hint = Loc.T("arrête-toi au départ", "stop at the start");
                    game.Hud.Announce(near.Kind + "  ·  " + near.Name.ToUpperInvariant(), Loc.F($"{near.Distance / 1000f:0.0} km  ·  {hint}"), near.Tint, 3f);
                }
            }
            else if (best > 220f) announced = null;
            if (candidate != null && !inviteOpen && GameInput.InteractPressed) setup.Open(candidate);
        }

        /// <summary>Lance la course : la voiture est posée derrière la ligne, dans le sens du parcours ; décompte.</summary>
        public void Begin(Track t)
        {
            if (t == null || mode != Mode.Idle || game.State != GameManager.Phase.Playing) return;
            StartRace(t, false, 0, RaceSetup.TrafficFactor);
        }

        /// <summary>
        /// Départ d'une course. Seul : sur la ligne, décompte de trois temps. Course à plusieurs (<paramref name="net"/>) :
        /// à sa place de la grille, et le décompte s'achève à l'instant fixé par le serveur, le même pour tous.
        /// </summary>
        void StartRace(Track t, bool net, int slot, float traffic)
        {
            Active = t;
            netRace = net;
            mode = Mode.Countdown;
            candidate = null;
            announced = t;
            resultTimer = 0f;
            progress = 0f;
            clockTintTimer = 0f;
            countdown = net ? game.Net.SecondsTo(game.Net.Race.StartMs) : Beat * 3f;
            shownBeat = 0;
            next = 0;
            elapsed = 0f;
            times = new float[t.GateCount];
            recording = new RaceGhost.Run();
            if (!t.ghostLoaded)
            {
                t.ghostLoaded = true;
                t.ghost = t.best > 0f ? RaceGhost.Run.Load(t.id, t.best) ?? GhostFromSplits(t) : null;
            }
            course.Clear();
            for (int lap = 0; lap < t.laps; lap++) course.AddRange(t.path);
            alongPath = new float[t.path.Count];
            for (int i = 1; i < t.path.Count; i++) alongPath[i] = alongPath[i - 1] + Flat(t.path[i] - t.path[i - 1]);
            if (net) PlaceOnGrid(t, slot);
            else PlaceOnLine(t);
            ApplyTraffic(traffic);
            game.SetInputEnabled(false);
            game.Nav.SetCourse(course, 0, t.fr, t.en);
        }

        /// <summary>Voiture posée juste derrière la ligne de départ, dans le sens du parcours (menu d'avant-course, départ).</summary>
        public void PlaceOnLine(Track t)
        {
            Vector3 f = t.start.forward;
            Vector3 p = t.path[0] - f * StartBack + Vector3.up * 0.5f;
            game.Traffic.ClearAround(p, 18f);
            game.Player.Teleport(p, Quaternion.LookRotation(f, Vector3.up), 0f);
        }

        /// <summary>
        /// Place de grille d'une course à plusieurs. La première est sur la ligne, comme en solo ; les suivantes sont en
        /// file derrière elle, tous les <see cref="GridSpacing"/> m le long de la route qui mène au départ.
        /// </summary>
        void PlaceOnGrid(Track t, int slot)
        {
            if (slot <= 0) { PlaceOnLine(t); return; }
            Vector3 f = t.start.forward;
            float back = StartBack + slot * GridSpacing;
            // À défaut de route derrière la ligne (départ au bout d'une route) : tout droit dans l'axe du départ
            Vector3 p = t.path[0] - f * back, forward = f;
            var hit = game.Road.Nearest(t.path[0], 40f);
            if (hit.Found)
            {
                var e = hit.edge;
                e.Evaluate(hit.s, out var tangent, out _);
                int direction = Vector3.Dot(tangent, f) >= 0f ? 1 : -1;
                float s = hit.s - direction * back;
                if (s >= 0f && s <= e.Length)
                {
                    // Même écart à l'axe de la route que la ligne de départ : la même voie
                    p = e.Evaluate(s, out tangent, out var right) + right * hit.lateral;
                    forward = tangent * direction;
                }
            }
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = f;
            var ground = game.Road.Nearest(p, 20f);
            if (ground.Found) p.y = ground.point.y;
            p += Vector3.up * 0.5f;
            game.Traffic.ClearAround(p, 12f);
            game.Player.Teleport(p, Quaternion.LookRotation(forward.normalized, Vector3.up), 0f);
        }

        /// <summary>
        /// Trafic de la course (<see cref="RaceSetup"/>) : 1 normal ; moins, une partie des véhicules présents s'en va
        /// tout de suite et il en vient moins ; 0, la route est vidée pour toute la course.
        /// En partie réseau, le trafic est celui de tous les joueurs : il reste entier, et seul ce joueur en perd une
        /// partie de vue (<see cref="TrafficSystem.LocalFactor"/>), qu'il traverse.
        /// </summary>
        void ApplyTraffic(float factor)
        {
            var traffic = game.Traffic;
            if (game.Online)
            {
                traffic.LocalFactor = factor;
                return;
            }
            traffic.RaceFactor = factor;
            if (factor >= 1f) return;
            var cars = traffic.Cars;
            for (int i = cars.Count - 1; i >= 0; i--)
                if (factor <= 0f || (i * 0.618f) % 1f >= factor) traffic.Despawn(cars[i]);
        }

        void UpdateCountdown(VehicleController car, Vector3 pos, bool menu, float dt)
        {
            // Voiture tenue sur la ligne (un menu refermé pendant le décompte lui rend la main)
            if (!menu) game.SetInputEnabled(false);
            // Course à plusieurs : le départ est donné à l'heure du serveur ; les trois temps du décompte le précèdent
            if (netRace) countdown = game.Net.SecondsTo(game.Net.Race.StartMs);
            else countdown -= dt;
            int beat = Mathf.CeilToInt(countdown / Beat);
            if (beat != shownBeat && beat >= 1 && beat <= 3)
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
            recording.Record(0f, car.transform);
            game.Hud.Flash(Active.Tint, 0.18f);
            if (game.Local.Audio) game.Local.Audio.Chime();
        }

        void UpdateRace(VehicleController car, Vector3 pos, bool menu, float dt)
        {
            elapsed += dt;
            sinceGate += dt;
            recording.Record(elapsed, car.transform);
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
            if (!Active.Loop || netRace)
            {
                float lap = LapProgress(pos);
                if (!Active.Loop) progress = Mathf.Max(progress, lap);
                // Course à plusieurs : part de toute la course déjà couverte, pour le classement du serveur
                if (netRace) netProgress = Mathf.Max(netProgress, Mathf.Clamp01((next / Active.gates.Length + lap) / Active.laps));
            }

            // Abandon : touche maintenue, magasin ouvert, parcours quitté pour de bon ou plus aucune porte franchie
            if (Dealership.IsOpen || TuningGarage.IsOpen) { Abandon(); return; }
            abandonHeld = !menu && AbandonKey ? abandonHeld + Time.unscaledDeltaTime : 0f;
            Vector3 behind = next > 0 ? GateOf(next - 1).pos : Active.start.pos;
            bool lost = Flat(pos - g.pos) > LostDistance && Flat(pos - behind) > LostDistance;
            lostTimer = lost ? lostTimer + dt : 0f;
            if (abandonHeld >= AbandonHold || lostTimer > LostTime || sinceGate > GateTimeout) Abandon();
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        void TintClock(Color c)
        {
            clockTint = c;
            clockTintTimer = ClockTintTime;
        }

        /// <summary>
        /// Part du sprint (ou du tour en cours) déjà couverte (0-1) : point du parcours le plus proche, entre la dernière
        /// porte franchie (la ligne, en début de tour) et la suivante.
        /// </summary>
        float LapProgress(Vector3 pos)
        {
            var path = Active.path;
            float total = alongPath[alongPath.Length - 1];
            if (path.Count < 2 || total <= 0f) return 0f;
            int gate = next % Active.gates.Length;
            int lo = Mathf.Clamp(gate > 0 ? Active.gates[gate - 1].index - 1 : 0, 0, path.Count - 2);
            int hi = Mathf.Clamp(Active.gates[gate].index, lo, path.Count - 2);
            float bestDist = float.MaxValue, along = 0f;
            for (int i = lo; i <= hi; i++)
            {
                Vector3 a = path[i], ab = path[i + 1] - a;
                ab.y = 0f;
                float len = ab.magnitude;
                Vector3 ap = pos - a;
                ap.y = 0f;
                float k = len > 0.01f ? Mathf.Clamp(Vector3.Dot(ap, ab) / len, 0f, len) : 0f;
                float d = (ap - (len > 0.01f ? ab / len * k : Vector3.zero)).sqrMagnitude;
                if (d < bestDist) { bestDist = d; along = alongPath[i] + k; }
            }
            return Mathf.Clamp01(along / total);
        }

        /// <summary>Abandon : le bouton de replacement (Y / R) maintenu ; un appui bref ne fait que replacer (<see cref="GameManager"/>).</summary>
        static bool AbandonKey => GameInput.ResetHeld;

        /// <summary>Remplissage de la jauge d'abandon (0-1) : elle ne part qu'au-delà d'un simple appui de replacement.</summary>
        float AbandonFill => Mathf.Clamp01((abandonHeld - GameManager.ResetTapTime) / (AbandonHold - GameManager.ResetTapTime));

        /// <summary>Porte franchie à l'instant <paramref name="time"/> : écart au record, tour suivant, ou arrivée.</summary>
        void Pass(float time)
        {
            var t = Active;
            times[next] = time;
            if (next == times.Length - 1) { Finish(time); return; }
            // Écart au record à ce point de passage (le temps tout court tant qu'il n'y a pas de record) ; nouveau tour
            int perLap = t.gates.Length;
            string what = t.Loop && (next + 1) % perLap == 0
                ? ((next + 1) / perLap == t.laps - 1 ? Loc.T("DERNIER TOUR", "FINAL LAP") : Loc.F($"TOUR {(next + 1) / perLap + 1}/{t.laps}", $"LAP {(next + 1) / perLap + 1}/{t.laps}"))
                : Loc.T("POINT DE PASSAGE", "CHECKPOINT");
            if (t.splits != null && t.splits.Length == times.Length)
            {
                float delta = time - t.splits[next];
                game.Hud.Announce(Delta(delta), what + "  ·  " + Clock(time), delta <= 0f ? Good : Bad, 3.2f, true);
                TintClock(delta <= 0f ? Good : Bad);
            }
            else game.Hud.Announce(Clock(time), what, White, 2.4f);
            int passed = next++;
            netGates = next;
            sinceGate = 0f;
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
                // Le relevé de cette course devient le fantôme du tracé
                recording.time = time;
                recording.car = game.Player.Spec != null ? game.Player.Spec.id ?? "" : "";
                recording.Save(t.id);
                t.ghost = recording;
                t.ghostLoaded = true;
            }
            resultTrack = t;
            resultTime = time;
            resultRecord = record;
            resultTimer = 9f;
            clockTintTimer = 0f;
            string finish = Loc.T("ARRIVÉE  ·  ", "FINISH  ·  ") + Clock(time);
            // L'écart au record en très grand ; « nouveau record » en titre pour un premier temps
            if (old <= 0f) game.Hud.Announce(Loc.T("NOUVEAU RECORD", "NEW BEST TIME"), finish, Good, 4f, true);
            else if (record) game.Hud.Announce(Delta(time - old), Loc.T("NOUVEAU RECORD  ·  ", "NEW BEST TIME  ·  ") + finish, Good, 4.5f, true);
            else game.Hud.Announce(Delta(time - old), finish, Bad, 4.5f, true);
            game.Hud.Flash(record ? Good : t.Tint, 0.3f);
            game.Camera.Shake(0.25f);
            if (game.Local.Audio) game.Local.Audio.Chime();
            if (netRace)
            {
                // Course à plusieurs : notre temps part au serveur, qui nous classe
                netStatus = NetRaceStatus.Finished;
                netTime = time;
                netGates = times.Length;
                netProgress = 1f;
            }
            Stop();
            Finished?.Invoke(t, time, record);
        }

        void Abandon()
        {
            game.Hud.Announce(Loc.T("COURSE ABANDONNÉE", "RACE ABANDONED"), "", Bad, 2.4f);
            Stop();
        }

        /// <summary>
        /// Fin de la course (arrivée, abandon, retour à l'accueil, séance du serveur fermée ou perdue) : la balade
        /// reprend, avec tout le trafic et sa propre voiture.
        /// </summary>
        void Stop()
        {
            if (mode == Mode.Countdown && !(game.Hud && game.Hud.Paused)) game.SetInputEnabled(true);
            // Course à plusieurs quittée autrement que par l'arrivée : un abandon pour le serveur
            if (netRace && netStatus == NetRaceStatus.Racing) netStatus = NetRaceStatus.Abandoned;
            netRace = false;
            mode = Mode.Idle;
            announced = Active;     // pas d'annonce du départ que l'on vient de quitter (ligne d'une boucle)
            Active = null;
            goTimer = abandonHeld = 0f;
            recording = null;
            if (game.Traffic)
            {
                game.Traffic.RaceFactor = 1f;
                game.Traffic.LocalFactor = 1f;
            }
            if (borrowed)
            {
                // La voiture imposée est rendue : on retrouve la sienne, avec ses améliorations
                borrowed = false;
                game.ShowCar(game.Local.CarIndex);
            }
            // Le temps et l'heure, tenus pendant la course (menu d'avant-course), reprennent leur cours
            if (game.Weather) game.Weather.Release();
            if (game.DayNight) game.DayNight.frozen = false;
            if (game.Nav) game.Nav.ClearCourse();
        }

        // ------------------------------------------------------------------ course à plusieurs (partie en réseau)

        /// <summary>
        /// « Lancer la course » du menu d'avant-course. Seul : le décompte part. En partie réseau : la course est
        /// proposée au serveur, qui invite les autres joueurs (<see cref="NetRace"/>) ; s'il y a déjà une course sur le
        /// serveur (une seule à la fois), on court seul, pour son chrono.
        /// </summary>
        public void Launch(Track t)
        {
            if (t == null || tracks == null || mode != Mode.Idle) return;
            var net = game.Net;
            int car = RaceSetup.ImposeCar ? game.Local.CarIndex : NetRace.FreeCar;
            if (game.Online && !net.Race.Exists && net.ProposeRace(tracks.IndexOf(t), RaceSetup.TrafficLevel, car)) proposed = t;
            else Begin(t);
        }

        void OnProposalDropped()
        {
            if (proposed == null) return;
            proposed = null;
            game.Hud.Announce(Loc.T("COURSE NON LANCÉE", "RACE NOT STARTED"), Loc.T("le serveur ne l'a pas reprise", "the server did not take it up"), Bad, 3f);
        }

        static string Racer(NetRace.Entry e) => e != null && !string.IsNullOrEmpty(e.name) ? e.name : Loc.T("Pilote", "Driver");

        /// <summary>
        /// Partie en réseau : suit la séance de course du serveur. Invitation reçue : on y répond (sauf occupé) ;
        /// acceptée, on est posé sur la grille au décompte commun, puis la course se déroule ici comme en solo. Notre
        /// réponse, puis notre avancement, repartent vers le serveur avec l'état de la voiture (<see cref="NetGame.LocalRace"/>).
        /// </summary>
        void UpdateNet(bool menu)
        {
            var net = game.Net;
            bool online = net && net.Active;
            var s = online && net.Race.Exists && net.Race.Track < tracks.Count ? net.Race : null;
            inviteOpen = false;
            if (!online) proposed = null;
            byte id = s != null ? s.Id : (byte)0;
            if (id != netSession)
            {
                if (netSession != 0) LeaveSession(online);
                if (s != null) EnterSession(s, net);
            }
            if (s == null)
            {
                if (net) net.LocalRace = default;
                return;
            }
            var t = tracks[s.Track];
            var me = s.Find(net.LocalId);
            bool busy = Dealership.IsOpen || TuningGarage.IsOpen || RaceSetup.IsOpen;
            switch (s.Phase)
            {
                case NetRacePhase.Invite:
                    // Invite à répondre, sauf pour qui est occupé (course en solo, magasin, menu d'avant-course) :
                    // l'annonce lui a suffi, et l'invitation expirera sans lui
                    if (netStatus != NetRaceStatus.Invited || me == null || Active != null || busy) break;
                    inviteOpen = true;
                    if (menu) break;
                    if (GameInput.InteractPressed) Answer(true);
                    else if (GameInput.DeclinePressed) Answer(false);
                    break;
                case NetRacePhase.Countdown:
                case NetRacePhase.Running:
                    if (netStatus == NetRaceStatus.Invited) netStatus = NetRaceStatus.Declined;     // invitation expirée
                    else if (netStatus == NetRaceStatus.Accepted)
                    {
                        if (me == null || me.status == NetRaceStatus.Abandoned)
                        {
                            // Réponse arrivée après la fin de l'invitation, ou départ donné depuis trop longtemps
                            netStatus = NetRaceStatus.Declined;
                            game.Hud.Announce(Loc.T("COURSE MANQUÉE", "RACE MISSED"), Loc.T("le départ a été donné sans toi", "it started without you"), Bad, 3f);
                        }
                        else if (mode != Mode.Idle || busy) netStatus = NetRaceStatus.Abandoned;     // occupé au moment du départ
                        else JoinGrid(s, t, s.Entries.IndexOf(me));
                    }
                    break;
                case NetRacePhase.Results:
                    if (netRace && mode != Mode.Idle)
                    {
                        // Délai dépassé après le premier arrivé : la course est finie pour tous
                        netStatus = NetRaceStatus.Abandoned;
                        game.Hud.Announce(Loc.T("COURSE TERMINÉE", "RACE OVER"), Loc.T("délai dépassé", "time limit reached"), Bad, 3f);
                        Stop();
                    }
                    else if (netStatus == NetRaceStatus.Invited || netStatus == NetRaceStatus.Accepted) netStatus = NetRaceStatus.Declined;
                    break;
            }
            if (s.Phase != netPhase)
            {
                netPhase = s.Phase;
                // Fin de la course, pour qui n'y courait pas : le vainqueur
                var winner = s.Winner;
                if (s.Phase == NetRacePhase.Results && me == null && winner != null && s.Entries.Count > 1)
                    game.Hud.Announce(Loc.T($"{Racer(winner)} gagne la course", $"{Racer(winner)} wins the race"), t.Name + "  ·  " + Clock(winner.time), t.Tint, 3.5f);
            }
            net.LocalRace = new NetGame.RaceReply { id = netSession, status = netStatus, gates = netGates, progress = netProgress, time = netTime };
        }

        /// <summary>Nouvelle séance sur le serveur : la nôtre si nous l'avons lancée, sinon une invitation.</summary>
        void EnterSession(NetRace s, NetGame net)
        {
            netSession = s.Id;
            bool ours = s.Launcher == net.LocalId;
            netStatus = ours ? NetRaceStatus.Accepted : NetRaceStatus.Invited;
            netPhase = NetRacePhase.None;
            netGates = 0;
            netProgress = netTime = 0f;
            if (ours) proposed = null;
            // Occupé (course en solo, magasin) : une annonce, sans invite à répondre
            if (!ours && s.Phase == NetRacePhase.Invite && (Active != null || Dealership.IsOpen || TuningGarage.IsOpen))
            {
                var t = tracks[s.Track];
                string who = Racer(s.Find(s.Launcher));
                game.Hud.Announce(Loc.T($"{who} lance une course", $"{who} is starting a race"), t.Kind + "  ·  " + t.Name.ToUpperInvariant(), t.Tint, 3.5f);
            }
        }

        /// <summary>La séance suivie n'existe plus (finie, annulée, partie quittée ou connexion perdue).</summary>
        void LeaveSession(bool online)
        {
            if (netRace && mode != Mode.Idle)
            {
                game.Hud.Announce(Loc.T("COURSE INTERROMPUE", "RACE INTERRUPTED"),
                    online ? Loc.T("le serveur a fermé la course", "the server closed the race") : Loc.T("connexion au serveur perdue", "connection to the server lost"), Bad, 3f);
                Stop();
            }
            else if (online && netStatus == NetRaceStatus.Accepted && netPhase == NetRacePhase.Invite)
                game.Hud.Announce(Loc.T("COURSE ANNULÉE", "RACE CANCELLED"), Loc.T("le lanceur a quitté la partie", "the starter left the game"), Bad, 3f);
            netSession = 0;
            netStatus = NetRaceStatus.Invited;
            netPhase = NetRacePhase.None;
        }

        /// <summary>Réponse à l'invitation.</summary>
        void Answer(bool accept)
        {
            netStatus = accept ? NetRaceStatus.Accepted : NetRaceStatus.Declined;
            inviteOpen = false;
            if (accept && game.Local.Audio) game.Local.Audio.Chime();
        }

        /// <summary>Départ de la course à plusieurs : voiture imposée, place sur la grille, décompte réglé sur l'heure du serveur.</summary>
        void JoinGrid(NetRace s, Track t, int slot)
        {
            if (s.Car != NetRace.FreeCar && s.Car < game.assets.playerCars.Length)
            {
                // Prêtée, avec sa fiche d'origine (la même pour tous) ; notre voiture revient à la fin de la course
                borrowed = true;
                game.ShowCar(s.Car, true);
            }
            netStatus = NetRaceStatus.Racing;
            // Sans collisions entre joueurs, tous partent de la ligne (ils se traversent) ; sinon en file derrière elle
            StartRace(t, true, game.Net.Collisions ? Mathf.Max(0, slot) : 0, RaceSetup.FactorOf(s.Traffic));
            int n = s.Entries.Count;
            game.Hud.Announce(t.Kind + "  ·  " + t.Name.ToUpperInvariant(),
                n > 1 ? Loc.T($"{n} coureurs  ·  départ imminent", $"{n} racers  ·  starting soon") : Loc.T("départ imminent", "starting soon"), t.Tint, 3f);
        }

        /// <summary>Fantôme du record : sur la ligne pendant le décompte, puis à sa place de l'instant du chrono.</summary>
        void UpdateGhost()
        {
            var run = Active != null && RaceSetup.ShowGhost ? Active.ghost : null;
            if (run == null)
            {
                ghostFx?.Hide();
                return;
            }
            ghostFx ??= new RaceGhost(transform, game);
            ghostFx.Show(run, mode == Mode.Running ? elapsed : 0f, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Fantôme d'un record sans relevé (établi avant les fantômes) : dans la voie du parcours, chaque porte franchie
        /// à l'instant du record, à vitesse régulière d'une porte à l'autre ; la voiture du record si on la retrouve.
        /// </summary>
        RaceGhost.Run GhostFromSplits(Track t)
        {
            var path = t.path;
            int n = path.Count;
            var dist = new float[n];
            for (int i = 1; i < n; i++) dist[i] = dist[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            float length = dist[n - 1];
            int count = t.GateCount, per = t.gates.Length;
            bool splits = t.splits != null && t.splits.Length == count;
            // Repères (temps, distance depuis la ligne) : départ arrêté juste derrière la ligne, portes, arrivée
            var keyT = new List<float> { 0f };
            var keyD = new List<float> { -StartBack };
            for (int k = 0; k < count; k++)
            {
                float at = k == count - 1 ? t.best : splits ? t.splits[k] : -1f;
                if (at <= keyT[keyT.Count - 1]) continue;
                keyT.Add(at);
                keyD.Add(k / per * length + length * (k % per + 1) / per);
            }
            if (keyT.Count < 2) return null;

            var run = new RaceGhost.Run { time = t.best };
            foreach (var spec in game.assets.playerCars)
                if (spec != null && spec.displayName == t.bestCar) { run.car = spec.id ?? ""; break; }
            int seg = 1;
            for (int s = 0; s / RaceGhost.Rate <= t.best; s++)
            {
                float time = s / RaceGhost.Rate;
                while (seg < keyT.Count - 1 && time > keyT[seg]) seg++;
                float d = Mathf.Lerp(keyD[seg - 1], keyD[seg], Mathf.InverseLerp(keyT[seg - 1], keyT[seg], time));
                Vector3 p = PathAt(t, dist, d), dir = PathAt(t, dist, d + 2.5f) - PathAt(t, dist, d - 2.5f);
                run.Add(p, dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.LookRotation(t.start.forward, Vector3.up));
            }
            return run.Count >= 2 ? run : null;
        }

        // ------------------------------------------------------------------ portes (monde)

        /// <summary>
        /// Porte, sans rien de solide en travers de la route : une ligne de lumière posée sur la chaussée (elle en suit
        /// la pente et le virage), un tapis de lumière où des barres glissent vers la ligne, un voile de brume éclairé
        /// au-dessus, et de chaque côté un projecteur au sol qui éclaire les abords et d'où monte une fumée colorée
        /// (le repère de loin). La voie reste dégagée : la fumée ne naît que sur les bas-côtés et dérive vers
        /// l'extérieur, et brume comme fumée s'effacent à l'approche de la caméra (shader ChillRoad/GateFx).
        /// </summary>
        class GateFx
        {
            static readonly int TintId = Shader.PropertyToID("_Tint"), StrengthId = Shader.PropertyToID("_Strength"),
                HalfId = Shader.PropertyToID("_Half"), BackId = Shader.PropertyToID("_Back"), NearId = Shader.PropertyToID("_Near"),
                BaseColor = Shader.PropertyToID("_BaseColor"), EmissionColor = Shader.PropertyToID("_EmissionColor");
            const float Lift = 0.05f, Margin = 1.4f, CurtainHeight = 1.5f;
            const float LampInset = 0.35f, LampHeight = 0.2f, SmokeDistance = 600f, LightDistance = 160f;

            readonly Transform root;
            readonly Mesh groundMesh, curtainMesh;
            readonly Material ground, curtain, smoke, housing, lens;
            readonly Transform[] lamps = new Transform[2];
            readonly Light[] lights = new Light[2];
            readonly ParticleSystem[] plumes = new ParticleSystem[2];
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> tris = new List<int>();
            Gate bound;
            float level;

            /// <summary>Nuit (0-1) : les projecteurs n'éclairent vraiment leurs abords qu'à la nuit tombée.</summary>
            public static float Night;

            /// <summary>Porte affichée (null si l'effet est éteint).</summary>
            public Gate Bound => root.gameObject.activeSelf ? bound : null;

            public static Shader Find(GameAssets assets) => assets.gateFx ? assets.gateFx : Shader.Find("ChillRoad/GateFx");

            public GateFx(Transform parent, GameAssets assets, Shader shader)
            {
                root = new GameObject("RaceGate").transform;
                root.SetParent(parent, false);
                root.gameObject.SetActive(false);

                ground = Layer(shader, "_GATE_GROUND", 3008, Vector4.zero);
                ground.SetFloat(BackId, GateBefore);
                curtain = Layer(shader, "_GATE_CURTAIN", 3012, new Vector4(9f, 34f, 0f, 0f));
                smoke = Layer(shader, "_GATE_SMOKE", 3013, new Vector4(5f, 16f, 0f, 0f));
                groundMesh = Surface("Ground", ground);
                curtainMesh = Surface("Curtain", curtain);

                // Projecteurs : petit boîtier sombre, lentille de la couleur du tracé
                housing = new Material(assets.emissiveWhite) { name = "Race gate lamp (runtime)" };
                housing.SetColor(BaseColor, new Color(0.07f, 0.075f, 0.09f, 1f));
                housing.SetColor(EmissionColor, Color.black);
                lens = new Material(assets.emissiveWhite) { name = "Race gate lens (runtime)" };
                lens.EnableKeyword("_EMISSION");
                for (int i = 0; i < 2; i++)
                {
                    lamps[i] = new GameObject("Lamp").transform;
                    lamps[i].SetParent(root, false);
                    Solid("Housing", lamps[i], housing, new Vector3(0.62f, LampHeight * 0.5f, 0.62f), new Vector3(0f, LampHeight * 0.5f, 0f));
                    Solid("Lens", lamps[i], lens, new Vector3(0.44f, 0.02f, 0.44f), new Vector3(0f, LampHeight + 0.005f, 0f));
                    lights[i] = Glow(lamps[i]);
                    plumes[i] = Plume(lamps[i]);
                }
            }

            static Material Layer(Shader shader, string keyword, int queue, Vector4 near)
            {
                var m = new Material(shader) { name = "Race gate " + keyword + " (runtime)", renderQueue = queue };
                foreach (var k in new[] { "_GATE_GROUND", "_GATE_CURTAIN", "_GATE_SMOKE" })
                {
                    if (k == keyword) m.EnableKeyword(k);
                    else m.DisableKeyword(k);
                }
                m.SetVector(NearId, near);
                return m;
            }

            Mesh Surface(string name, Material material)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                var mesh = new Mesh { name = "RaceGate" + name };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                Plain(go.AddComponent<MeshRenderer>(), material);
                return mesh;
            }

            static void Plain(Renderer r, Material material)
            {
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            /// <summary>Cylindre sans collision (on roule au travers).</summary>
            static void Solid(string name, Transform parent, Material material, Vector3 scale, Vector3 pos)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = name;
                go.transform.SetParent(parent, false);
                go.transform.localScale = scale;
                go.transform.localPosition = pos;
                var col = go.GetComponent<Collider>();
                col.enabled = false;
                Destroy(col);
                Plain(go.GetComponent<MeshRenderer>(), material);
            }

            /// <summary>Lumière du projecteur : éclaire la chaussée, le bas-côté et la fumée (de près seulement).</summary>
            static Light Glow(Transform lamp)
            {
                var go = new GameObject("Light");
                go.transform.SetParent(lamp, false);
                go.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 9f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                return l;
            }

            /// <summary>Fumée d'un projecteur : bouffées lentes qui montent en grossissant et s'écartent de la route.</summary>
            ParticleSystem Plume(Transform lamp)
            {
                var go = new GameObject("Smoke");
                go.transform.SetParent(lamp, false);
                go.transform.localPosition = new Vector3(0f, LampHeight + 0.1f, 0f);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.prewarm = true;
                main.duration = 5f;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4.2f, 5.8f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.6f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Shape;
                main.maxParticles = 64;
                var emission = ps.emission;
                emission.rateOverTime = 7f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.2f;
                var spin = ps.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                var wave = ps.noise;
                wave.enabled = true;
                wave.strength = 0.22f;
                wave.frequency = 0.35f;
                wave.scrollSpeed = 0.25f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f, 0f, 2.4f), new Keyframe(1f, 2.1f, 0.6f, 0f)));
                // Éclairée par le projecteur à la naissance, la fumée pâlit en montant puis se dissout
                var colors = ps.colorOverLifetime;
                colors.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.88f, 0.88f, 0.88f), 0.45f), new GradientColorKey(new Color(0.74f, 0.74f, 0.74f), 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.12f), new GradientAlphaKey(0.5f, 0.55f), new GradientAlphaKey(0f, 1f) });
                colors.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>();
                Plain(r, smoke);
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sortMode = ParticleSystemSortMode.Distance;
                return ps;
            }

            /// <summary>Pose l'effet sur une porte : sol et voile taillés sur le profil de la route, projecteurs aux deux bouts de la ligne.</summary>
            void Bind(Gate g)
            {
                bound = g;
                level = 0f;
                root.SetPositionAndRotation(g.pos, Quaternion.identity);
                int n = g.line.Length, k0 = Mathf.Clamp(Mathf.RoundToInt(GateBefore / GateStep), 0, n - 1);
                float w = g.half + Margin;
                Vector3 up = Vector3.up * Lift;

                verts.Clear(); uvs.Clear(); tris.Clear();
                for (int k = 0; k < n; k++)
                {
                    Vector3 c = g.line[k] - g.pos + up;
                    float z = (k - k0) * GateStep;
                    verts.Add(c - g.across[k] * w);
                    verts.Add(c + g.across[k] * w);
                    uvs.Add(new Vector2(-w, z));
                    uvs.Add(new Vector2(w, z));
                }
                for (int i = 0; i + 3 < verts.Count; i += 2)
                {
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 3);
                }
                Fill(groundMesh);

                verts.Clear(); uvs.Clear(); tris.Clear();
                Vector3 mid = g.line[k0] - g.pos + up, side = g.across[k0];
                verts.Add(mid - side * g.half); verts.Add(mid + side * g.half);
                verts.Add(mid - side * g.half + Vector3.up * CurtainHeight); verts.Add(mid + side * g.half + Vector3.up * CurtainHeight);
                uvs.Add(new Vector2(-g.half, 0f)); uvs.Add(new Vector2(g.half, 0f));
                uvs.Add(new Vector2(-g.half, 1f)); uvs.Add(new Vector2(g.half, 1f));
                tris.Add(0); tris.Add(2); tris.Add(1); tris.Add(1); tris.Add(2); tris.Add(3);
                Fill(curtainMesh);
                ground.SetFloat(HalfId, g.half);
                curtain.SetFloat(HalfId, g.half);

                for (int i = 0; i < 2; i++)
                {
                    float s = i == 0 ? -1f : 1f;
                    lamps[i].localPosition = g.line[k0] - g.pos + side * (s * (g.half - LampInset));
                    // La fumée monte et s'écarte de la chaussée
                    Vector3 away = side * s;
                    var drift = plumes[i].velocityOverLifetime;
                    drift.enabled = true;
                    drift.space = ParticleSystemSimulationSpace.World;
                    drift.x = new ParticleSystem.MinMaxCurve(away.x * 0.3f, away.x * 0.6f);
                    drift.y = new ParticleSystem.MinMaxCurve(1.2f, 1.9f);
                    drift.z = new ParticleSystem.MinMaxCurve(away.z * 0.3f, away.z * 0.6f);
                    plumes[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            void Fill(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
            }

            /// <summary>Affiche la porte ; l'intensité rejoint <paramref name="strength"/> en douceur (allumage, porte suivante qui devient la prochaine).</summary>
            public void Show(Gate g, Color tint, float strength, Vector3 eye, float dt)
            {
                if (!root.gameObject.activeSelf)
                {
                    root.gameObject.SetActive(true);
                    bound = null;
                }
                if (bound != g) Bind(g);
                level = Mathf.MoveTowards(level, strength, dt * 2.2f);
                float lit = Mathf.Clamp01(level);
                var hdr = new Color(tint.r * 1.5f, tint.g * 1.5f, tint.b * 1.5f, 1f);
                ground.SetColor(TintId, hdr);
                ground.SetFloat(StrengthId, level);
                curtain.SetColor(TintId, hdr);
                curtain.SetFloat(StrengthId, level);
                smoke.SetFloat(StrengthId, lit);
                lens.SetColor(BaseColor, new Color(tint.r * 0.3f, tint.g * 0.3f, tint.b * 0.3f, 1f));
                lens.SetColor(EmissionColor, new Color(tint.r * lit, tint.g * lit, tint.b * lit, 1f));

                float dist = Vector3.Distance(eye, g.pos);
                // Fumée teintée par le projecteur, un peu blanchie ; inutile de la simuler de très loin
                bool smoking = dist < SmokeDistance;
                var born = Color.Lerp(tint, Color.white, 0.3f);
                // Vraie lumière seulement à l'approche (elle s'éteint en douceur avec la distance)
                float glow = lit * Mathf.Clamp01((LightDistance - dist) / 40f);
                for (int i = 0; i < 2; i++)
                {
                    lights[i].enabled = glow > 0.01f;
                    lights[i].color = tint;
                    lights[i].intensity = glow * Mathf.Lerp(0.8f, 4f, Night);
                    var ps = plumes[i];
                    var main = ps.main;
                    main.startColor = born;
                    if (smoking && !ps.isPlaying) ps.Play();
                    else if (!smoking && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            public void Hide()
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
            }
        }

        GateFx[] startFx;
        readonly GateFx[] raceFx = new GateFx[2];

        /// <summary>
        /// En balade : la porte de départ de chaque tracé. En course : la prochaine porte, et la suivante en plus
        /// discret (la dernière, l'arrivée, est blanche).
        /// </summary>
        void UpdateGates(bool playing)
        {
            if (startFx == null)
            {
                var shader = GateFx.Find(game.assets);
                if (!shader || !game.assets.emissiveWhite) return;
                startFx = new GateFx[tracks.Count];
                for (int i = 0; i < startFx.Length; i++) startFx[i] = new GateFx(transform, game.assets, shader);
                for (int i = 0; i < raceFx.Length; i++) raceFx[i] = new GateFx(transform, game.assets, shader);
            }
            Vector3 eye = game.Camera.transform.position;
            float dt = Time.unscaledDeltaTime;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.08f;
            GateFx.Night = game.DayNight ? game.DayNight.Night : 0f;
            for (int i = 0; i < startFx.Length; i++)
            {
                var t = tracks[i];
                bool show = playing && (mode == Mode.Idle ? Flat(eye - t.start.pos) < 1800f : mode == Mode.Countdown && t == Active);
                if (show) startFx[i].Show(t.start, t.Tint, pulse, eye, dt);
                else startFx[i].Hide();
            }

            Gate first = null, second = null;
            int last = 0;
            if (playing && mode != Mode.Idle)
            {
                last = Active.GateCount - 1;
                first = GateOf(next);
                if (next < last && GateOf(next + 1) != first) second = GateOf(next + 1);
            }
            // Une porte garde son effet quand, de suivante, elle devient la prochaine : sa fumée reste en place
            GateFx fxFirst = null, fxSecond = null;
            foreach (var fx in raceFx)
            {
                var b = fx.Bound;
                if (b == null) continue;
                if (b == first && fxFirst == null) fxFirst = fx;
                else if (b == second && fxSecond == null) fxSecond = fx;
            }
            foreach (var fx in raceFx)
            {
                if (fx == fxFirst || fx == fxSecond) continue;
                if (first != null && fxFirst == null) fxFirst = fx;
                else if (second != null && fxSecond == null) fxSecond = fx;
                else fx.Hide();
            }
            if (fxFirst != null) fxFirst.Show(first, next == last ? FinishTint : Active.Tint, pulse, eye, dt);
            if (fxSecond != null) fxSecond.Show(second, next + 1 == last ? FinishTint : Active.Tint, 0.4f, eye, dt);
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
            var map = root.Find("Map");
            if (map) mark.SetSiblingIndex(map.GetSiblingIndex() + 1);
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
        Text progressText, timeText, abandonText, countText, promptTitle, promptDetail;
        RectTransform abandonFill;
        // Course à plusieurs : place dans le cadre du chrono, invitation (ou attente du départ), classement
        HudShape timerPlate, inviteStripe, boardPlate, boardStripe;
        Text placeText, inviteTitle, inviteDetail, inviteKeys, boardTitle, boardNote;
        CanvasGroup inviteGroup, boardGroup;
        RectTransform inviteFill;
        Image inviteFillImage;
        const int BoardRows = 8;
        const float BoardWidth = 490f, BoardTop = 74f, BoardStep = 30f;
        readonly Text[,] boardCells = new Text[BoardRows, 4];
        readonly List<NetRace.Entry> boardOrder = new List<NetRace.Entry>();
        /// <summary>Ordre du tableau : les classés par place, puis les abandons.</summary>
        static readonly Comparison<NetRace.Entry> ByPlace = (a, b) => (a.rank > 0 ? a.rank : 1000 + a.id).CompareTo(b.rank > 0 ? b.rank : 1000 + b.id);

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

            // Haut droite : avancement en grand (pourcentage du sprint, ou tour), chrono en dessous ; liseré de la couleur du tracé
            var corner = new Vector2(1f, 1f);
            var panel = Box(hudRoot, "Timer", corner, new Vector2(-26f, -28f), new Vector2(250f, 100f));
            panel.pivot = corner;
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = panelGroup.interactable = false;
            timerPlate = Shape(panel, "Plate", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(250f, 100f), Plate);
            timerPlate.Set(16f, 120f, 0f);
            panelStripe = Shape(panel, "Stripe", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(6f, 100f), SprintTint);
            panelStripe.Set(16f, 0f, 0f);
            progressText = Label(panel, 48, TextAnchor.UpperRight, White, corner, new Vector2(-22f, -4f), new Vector2(230f, 56f), FontStyle.BoldAndItalic);
            timeText = Label(panel, 24, TextAnchor.UpperRight, new Color(1f, 1f, 1f, 0.85f), corner, new Vector2(-22f, -62f), new Vector2(230f, 30f), FontStyle.Bold);
            // Abandon : rappel de la touche au départ, jauge pendant l'appui
            abandonText = Label(panel, 16, TextAnchor.UpperRight, Dim, corner, new Vector2(-22f, -108f), new Vector2(470f, 22f), FontStyle.Normal);
            var bar = Box(panel, "AbandonBar", corner, new Vector2(-22f, -134f), new Vector2(200f, 4f));
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
            button.onClick.AddListener(() => setup.Open(candidate));
            promptTitle = Label(prompt, 28, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(860f, 40f), FontStyle.BoldAndItalic);
            promptTitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            promptTitle.supportRichText = true;
            promptDetail = Label(prompt, 21, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.78f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(860f, 30f), FontStyle.Normal);
            promptDetail.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            // Course à plusieurs : place au classement, à gauche de l'avancement (la plaque du chrono s'élargit)
            placeText = Label(panel, 60, TextAnchor.MiddleRight, White, corner, new Vector2(-266f, -12f), new Vector2(120f, 76f), FontStyle.BoldAndItalic);
            placeText.supportRichText = true;

            // Invitation à une course à plusieurs (bouton : clic ou doigt pour accepter), ou attente de son départ :
            // à la place de l'invite de départ, dans la même allure ; une fine jauge donne le temps qui reste
            var invite = Box(hudRoot, "RaceInvite", new Vector2(0.5f, 0.24f), new Vector2(0f, 16f), new Vector2(880f, 128f));
            inviteGroup = invite.gameObject.AddComponent<CanvasGroup>();
            inviteGroup.alpha = 0f;
            inviteGroup.blocksRaycasts = inviteGroup.interactable = false;
            var invitePlate = Shape(invite, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 128f), new Color(0.02f, 0.03f, 0.05f, 0.75f));
            invitePlate.Set(18f, 0f, 0f);
            invitePlate.raycastTarget = true;
            inviteStripe = Shape(invite, "Stripe", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(520f, 3f), SprintTint);
            inviteStripe.Set(0f, 140f, 140f);
            var accept = invite.gameObject.AddComponent<Button>();
            // Sans transition avant de lui désigner la plaque : sinon le bouton, créé dans un groupe non interactif, la
            // teinte en « désactivé » (moitié de son opacité) pour de bon, et le texte se lit mal sur une voiture claire
            accept.transition = Selectable.Transition.None;
            accept.targetGraphic = invitePlate;
            accept.navigation = new Navigation { mode = Navigation.Mode.None };
            accept.onClick.AddListener(() => { if (inviteOpen) Answer(true); });
            inviteTitle = Label(invite, 28, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), new Vector2(0f, 36f), new Vector2(860f, 40f), FontStyle.BoldAndItalic);
            inviteTitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            inviteTitle.supportRichText = true;
            inviteDetail = Label(invite, 21, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.78f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(860f, 30f), FontStyle.Normal);
            inviteDetail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            inviteKeys = Label(invite, 21, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), new Vector2(0f, -32f), new Vector2(860f, 30f), FontStyle.Bold);
            inviteKeys.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var inviteBar = Box(invite, "TimeBar", new Vector2(0.5f, 0f), new Vector2(0f, 9f), new Vector2(520f, 3f));
            inviteBar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);
            inviteFill = Box(inviteBar, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 3f));
            inviteFillImage = inviteFill.gameObject.AddComponent<Image>();
            inviteBar.GetComponent<Image>().raycastTarget = inviteFillImage.raycastTarget = false;

            // Classement de la course à plusieurs, sous le cadre du chrono : place, pseudo, temps (ou abandon), écart au premier
            var board = Box(hudRoot, "RaceBoard", corner, new Vector2(-26f, -182f), new Vector2(BoardWidth, 100f));
            board.pivot = corner;
            boardGroup = board.gameObject.AddComponent<CanvasGroup>();
            boardGroup.alpha = 0f;
            boardGroup.blocksRaycasts = boardGroup.interactable = false;
            boardPlate = Shape(board, "Plate", corner, Vector2.zero, new Vector2(BoardWidth, 100f), Plate);
            boardPlate.Set(16f, 40f, 0f);
            boardStripe = Shape(board, "Stripe", corner, Vector2.zero, new Vector2(6f, 100f), SprintTint);
            boardStripe.Set(16f, 0f, 0f);
            boardTitle = Label(board, 26, TextAnchor.UpperRight, White, corner, new Vector2(-22f, -8f), new Vector2(BoardWidth - 50f, 32f), FontStyle.BoldAndItalic);
            boardNote = Label(board, 17, TextAnchor.UpperRight, Dim, corner, new Vector2(-22f, -43f), new Vector2(BoardWidth - 50f, 22f), FontStyle.Normal);
            for (int i = 0; i < BoardRows; i++)
            {
                float y = -BoardTop - i * BoardStep;
                boardCells[i, 0] = Label(board, 21, TextAnchor.UpperRight, Dim, corner, new Vector2(-432f, y), new Vector2(34f, 28f), FontStyle.BoldAndItalic);
                boardCells[i, 1] = Label(board, 21, TextAnchor.UpperLeft, White, corner, new Vector2(-232f, y), new Vector2(190f, 28f), FontStyle.Bold);
                boardCells[i, 2] = Label(board, 21, TextAnchor.UpperRight, White, corner, new Vector2(-116f, y), new Vector2(110f, 28f), FontStyle.Bold);
                boardCells[i, 3] = Label(board, 19, TextAnchor.UpperRight, Dim, corner, new Vector2(-22f, y), new Vector2(86f, 28f), FontStyle.Normal);
                boardCells[i, 1].supportRichText = false;
            }
        }

        /// <summary>
        /// Course à plusieurs : place au classement dans le cadre du chrono, invitation à rejoindre la course (ou attente
        /// de son départ pour qui l'a lancée ou acceptée), et classement des coureurs une fois sa propre course finie.
        /// </summary>
        void UpdateNetHud(float dt)
        {
            var net = game.Net;
            var s = netSession != 0 && net && net.Active && net.Race.Exists && net.Race.Id == netSession ? net.Race : null;
            var me = s != null ? s.Find(net.LocalId) : null;

            // Place au classement du serveur, dès qu'il y a un adversaire
            bool placed = netRace && Active != null && me != null && me.rank > 0 && s.Entries.Count > 1;
            placeText.text = placed ? $"{me.rank}<size=30>/{s.Ranked}</size>" : "";
            float plate = 250f;
            if (placed)
            {
                // À gauche de l'avancement, quelle que soit sa largeur (« 41 % », « TOUR 3/5 ») ; la plaque s'allonge d'autant
                placeText.color = Active.Tint;
                float x = 22f + progressText.preferredWidth + 34f;
                placeText.rectTransform.anchoredPosition = new Vector2(-x, -12f);
                plate = Mathf.Max(plate, x + placeText.preferredWidth + 140f);
            }
            timerPlate.rectTransform.sizeDelta = new Vector2(plate, 100f);

            // Invitation, ou attente du départ pour qui a lancé ou accepté la course
            bool invited = inviteOpen && s != null;
            bool waiting = !invited && mode == Mode.Idle && (proposed != null || s != null && s.Phase == NetRacePhase.Invite && netStatus == NetRaceStatus.Accepted);
            if (invited || waiting)
            {
                var t = s != null ? tracks[s.Track] : proposed;
                inviteStripe.color = inviteFillImage.color = t.Tint;
                string hex = ColorUtility.ToHtmlStringRGB(t.Tint);
                inviteTitle.text = Loc.F($"<color=#{hex}>{t.Kind}</color>   ·   {t.Name.ToUpperInvariant()}   ·   {t.Distance / 1000f:0.0} km");
                float left = s != null ? Mathf.Clamp(net.SecondsTo(s.StartMs), 0f, NetRace.InviteTime) : NetRace.InviteTime;
                int seconds = Mathf.CeilToInt(left);
                inviteFill.localScale = new Vector3(left / NetRace.InviteTime, 1f, 1f);
                if (invited)
                {
                    var cars = game.assets.playerCars;
                    string who = Racer(s.Find(s.Launcher));
                    string car = s.Car != NetRace.FreeCar && s.Car < cars.Length
                        ? Loc.T("voiture imposée : ", "same car for all: ") + cars[s.Car].displayName : Loc.T("voiture libre", "any car");
                    inviteDetail.text = Loc.T($"{who} lance une course   ·   {car}", $"{who} is starting a race   ·   {car}");
                    string yes = GameInput.UsingGamepad ? "A" : Loc.T("E ou Entrée", "E or Enter");
                    string no = GameInput.UsingGamepad ? Loc.T("Croix ↓", "D-pad ↓") : "N";
                    inviteKeys.text = TouchInput.Showing ? Loc.T($"touche ici : rejoindre   ·   {seconds} s", $"tap here: join   ·   {seconds} s")
                        : Loc.T($"{yes} : rejoindre   ·   {no} : refuser   ·   {seconds} s", $"{yes}: join   ·   {no}: decline   ·   {seconds} s");
                }
                else if (s == null)
                {
                    inviteDetail.text = Loc.T("Course proposée au serveur…", "Race sent to the server…");
                    inviteKeys.text = "";
                }
                else
                {
                    int ready = 0, pending = 0;
                    foreach (var e in s.Entries)
                    {
                        if (e.status == NetRaceStatus.Accepted) ready++;
                        else if (e.status == NetRaceStatus.Invited) pending++;
                    }
                    inviteDetail.text = (ready > 1 ? Loc.T($"{ready} coureurs prêts", $"{ready} racers ready") : Loc.T("1 coureur prêt", "1 racer ready"))
                        + (pending > 0 ? Loc.T($"   ·   {pending} sans réponse", $"   ·   {pending} yet to answer") : "");
                    inviteKeys.text = Loc.T($"Départ dès que tous ont répondu   ·   {seconds} s au plus", $"Starts once everyone has answered   ·   {seconds} s at most");
                }
            }
            inviteGroup.alpha = Mathf.MoveTowards(inviteGroup.alpha, invited || waiting ? 1f : 0f, dt * 5f);
            inviteGroup.blocksRaycasts = inviteGroup.interactable = invited;

            // Classement : en direct pour un coureur qui a fini ou abandonné pendant que d'autres roulent, puis résultats
            bool done = netStatus == NetRaceStatus.Finished || netStatus == NetRaceStatus.Abandoned;
            bool board = s != null && me != null && s.Entries.Count > 1 && mode == Mode.Idle
                && (s.Phase == NetRacePhase.Results || s.Phase == NetRacePhase.Running && done);
            if (board)
            {
                var t = tracks[s.Track];
                boardOrder.Clear();
                boardOrder.AddRange(s.Entries);
                boardOrder.Sort(ByPlace);
                int rows = Mathf.Min(boardOrder.Count, BoardRows);
                float height = BoardTop + rows * BoardStep + 12f;
                boardPlate.rectTransform.sizeDelta = new Vector2(BoardWidth, height);
                boardStripe.rectTransform.sizeDelta = new Vector2(6f, height);
                boardStripe.color = t.Tint;
                bool final = s.Phase == NetRacePhase.Results;
                boardTitle.text = final ? Loc.T("RÉSULTATS", "RESULTS") : Loc.T("CLASSEMENT", "STANDINGS");
                int left = !final && s.EndMs != 0 ? Mathf.CeilToInt(net.SecondsTo(s.EndMs)) : 0;
                boardNote.text = t.Name + (left > 0 ? Loc.T($"   ·   fin dans {left} s", $"   ·   ends in {left} s") : "");
                var winner = s.Winner;
                for (int i = 0; i < BoardRows; i++)
                {
                    if (i >= rows)
                    {
                        for (int k = 0; k < 4; k++) boardCells[i, k].text = "";
                        continue;
                    }
                    var e = boardOrder[i];
                    bool arrived = e.status == NetRaceStatus.Finished, retired = e.status == NetRaceStatus.Abandoned;
                    boardCells[i, 0].text = e.rank > 0 ? e.rank.ToString() : "—";
                    boardCells[i, 1].text = Racer(e);
                    boardCells[i, 1].color = e.id == net.LocalId ? t.Tint : retired ? Dim : White;
                    boardCells[i, 2].text = arrived ? Clock(e.time) : retired ? Loc.T("abandon", "retired") : Loc.T("en course", "racing");
                    boardCells[i, 2].color = arrived ? (e == winner ? Good : White) : Dim;
                    float gap = arrived && winner != null ? e.time - winner.time : 0f;
                    boardCells[i, 3].text = arrived ? (e == winner ? "" : gap < 60f ? Delta(gap) : "+" + Clock(gap))
                        : retired ? "" : Loc.F($"{Mathf.FloorToInt(e.progress * 100f)} %", $"{Mathf.FloorToInt(e.progress * 100f)}%");
                }
            }
            boardGroup.alpha = Mathf.MoveTowards(boardGroup.alpha, board ? 1f : 0f, dt * 5f);
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
            if (clockTintTimer > 0f) clockTintTimer = Mathf.Max(0f, clockTintTimer - dt);
            Color clockColor = Color.Lerp(new Color(1f, 1f, 1f, 0.85f), clockTint, Mathf.Clamp01(clockTintTimer / (ClockTintTime * 0.5f)));
            var shown = Active ?? (resultTimer > 0f ? resultTrack : null);
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, shown != null ? 1f : 0f, dt * 5f);
            if (Active != null)
            {
                var t = Active;
                panelStripe.color = t.Tint;
                progressText.color = White;
                progressText.text = t.Loop
                    ? Loc.F($"TOUR {Mathf.Min(next / t.gates.Length + 1, t.laps)}/{t.laps}", $"LAP {Mathf.Min(next / t.gates.Length + 1, t.laps)}/{t.laps}")
                    : Loc.F($"{Mathf.FloorToInt(progress * 100f)} %", $"{Mathf.FloorToInt(progress * 100f)}%");
                timeText.text = Clock(mode == Mode.Running ? elapsed : 0f);
                timeText.color = clockColor;
                // Rappel de la touche d'abandon au départ, puis seulement pendant l'appui
                bool charging = AbandonFill > 0f;
                bool hint = mode == Mode.Running && (charging || !TouchInput.Showing && elapsed < 7f);
                abandonText.text = !hint ? "" : TouchInput.Showing ? Loc.T("Maintenir REPLACER : abandonner", "Hold RESET: abandon")
                    : GameInput.UsingGamepad ? Loc.T("Maintenir Y : abandonner", "Hold Y: abandon")
                    : Loc.T("Maintenir R : abandonner", "Hold R: abandon");
                abandonFill.parent.gameObject.SetActive(charging);
                abandonFill.localScale = new Vector3(AbandonFill, 1f, 1f);
            }
            else if (shown != null)
            {
                panelStripe.color = shown.Tint;
                progressText.text = Loc.T("ARRIVÉE", "FINISH");
                progressText.color = resultRecord ? Good : White;
                timeText.text = Clock(resultTime);
                timeText.color = resultRecord ? Good : new Color(1f, 1f, 1f, 0.85f);
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
            bool ask = candidate != null && mode == Mode.Idle && !inviteOpen;
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
            UpdateNetHud(dt);
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
