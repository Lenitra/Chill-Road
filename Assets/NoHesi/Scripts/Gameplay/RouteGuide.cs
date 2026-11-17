using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// GPS : une destination posée sur la carte globale, et l'itinéraire le plus rapide pour s'y rendre, recalculé en
    /// roulant (on peut sortir de l'itinéraire : il se refait depuis la route où l'on se trouve).
    /// - Itinéraire : plus court chemin en temps sur le graphe des routes (Dijkstra sur les arêtes orientées) ; le temps
    ///   d'une route suit sa vitesse confortable en chaque point (lacets plus lents), les bretelles se prennent dans leur
    ///   sens et du bon côté de l'autoroute, chaque carrefour coûte un peu (plus pour tourner à gauche), un demi-tour
    ///   beaucoup.
    /// - Affichage : un ruban lumineux à chevrons posé sur la route devant la voiture, une colonne de lumière sur la
    ///   destination, le tracé sur la carte globale et sur la minicarte, la distance restante en bas de la minicarte.
    /// - GPS vers un ami (partie en réseau) : la destination peut être un autre joueur, choisi sur la carte globale
    ///   (<see cref="Follow"/>). Elle suit sa voiture, et l'arrivée de l'itinéraire est reportée toutes les deux secondes
    ///   sur la route où il se trouve s'il a bougé. Elle s'efface s'il quitte la partie, ou une fois arrivé auprès de
    ///   lui à l'arrêt ; tant qu'il roule, on continue de le suivre.
    /// Chaque joueur a sa propre destination (rien ne passe par le réseau).
    /// </summary>
    public class RouteGuide : MonoBehaviour
    {
        const float Recompute = 0.5f;
        const float ArriveDistance = 28f;
        const float RibbonLength = 650f, RibbonWidth = 1.7f, RibbonLift = 0.22f, ChevronSpacing = 5f;
        const float UTurnPenalty = 25f, DeadEndPenalty = 12f;
        /// <summary>
        /// Joueur suivi : période (s) à laquelle l'arrivée de l'itinéraire est reportée sur sa route, et déplacement
        /// (m) en dessous duquel elle reste où elle est.
        /// </summary>
        const float FriendRefresh = 2f, FriendMove = 12f;
        /// <summary>Arrivé auprès du joueur suivi : s'il roule moins vite que ceci (m/s) pendant ce temps (s).</summary>
        const float FriendStopSpeed = 3f, FriendStopTime = 2f;
        static readonly Color GpsColor = new Color(0.35f, 0.85f, 1f);

        public bool HasDestination { get; private set; }
        /// <summary>Destination : le point choisi, posé sur la route la plus proche, ou la voiture du joueur suivi.</summary>
        public Vector3 Destination { get; private set; }
        /// <summary>Nom de la destination : celui du lieu, ou le pseudo du joueur suivi.</summary>
        public string DestinationName => !HasDestination ? "" : following ? friendName : Loc.T(nameFr, nameEn);
        /// <summary>Joueur suivi (partie en réseau), quand la destination est sa voiture.</summary>
        public RemoteCar Friend => following && friend ? friend : null;
        /// <summary>Itinéraire (points dans la voie, du point de la route le plus proche de la voiture à la destination).</summary>
        public IReadOnlyList<Vector3> Route => route;
        /// <summary>Distance (m) et temps estimé (s) restant jusqu'à la destination.</summary>
        public float Remaining { get; private set; }
        public float RemainingTime { get; private set; }
        /// <summary>Un itinéraire existe (destination joignable).</summary>
        public bool HasRoute => HasDestination && route.Count > 1;
        /// <summary>Numéro de l'itinéraire, incrémenté à chaque calcul (pour redessiner ce qui en dépend).</summary>
        public int Version { get; private set; }

        GameManager game;
        RoadNetwork road;
        string nameFr = "", nameEn = "";
        float timer;
        int teleports = -1;
        float[][] times;
        // Arrivée de l'itinéraire (sur la route) ; pour un joueur suivi, là où il était au dernier report
        Vector3 goal, goalFrom;
        RemoteCar friend;
        bool following;
        string friendName = "";
        float friendTimer, friendStill;

        readonly List<Vector3> route = new List<Vector3>();
        readonly List<float> routeDist = new List<float>();
        int nearIndex;
        float along;            // position de la voiture le long de l'itinéraire (m)

        // Rendu : ruban, colonne, calque de la minicarte (tracé, destination, distance restante)
        Mesh ribbonMesh;
        MeshRenderer ribbonRenderer;
        Material ribbonMaterial;
        LineRenderer beacon;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();
        RouteLine miniLine;
        RectTransform miniPin;
        Text miniDistance, miniName;

        public void Init(GameManager g)
        {
            game = g;
            if (GameManager.DedicatedServer) return;
            BuildWorld();
            MiniMap.Overlay += DrawOnMiniMap;
        }

        void OnDestroy() => MiniMap.Overlay -= DrawOnMiniMap;

        // ------------------------------------------------------------------ destination

        /// <summary>Pose la destination sur la route la plus proche d'un point de la carte (le nom suit la langue).</summary>
        public bool SetDestination(Vector3 point, string fr, string en)
        {
            road = game.Road;
            if (OnCourse || !road || game.Map == null) return false;
            point.y = game.Map.Height(point.x, point.z);
            var hit = road.Nearest(point, 4000f);
            if (!hit.Found) return false;
            Destination = goal = hit.point;
            nameFr = string.IsNullOrEmpty(fr) ? "Repère" : fr;
            nameEn = string.IsNullOrEmpty(en) ? "Map pin" : en;
            following = false;
            friend = null;
            HasDestination = true;
            timer = 0f;
            Compute();
            return true;
        }

        /// <summary>
        /// Prend pour destination un autre joueur de la partie en réseau : l'itinéraire mène à la route où il se trouve
        /// et le suit dans ses déplacements. Faux s'il n'est pas encore en place, ou pendant une course.
        /// </summary>
        public bool Follow(RemoteCar player)
        {
            road = game.Road;
            if (OnCourse || !player || !player.Ready || !road || game.Map == null) return false;
            Vector3 at = player.transform.position;
            var hit = road.Nearest(at, 4000f);
            if (!hit.Found) return false;
            friend = player;
            following = true;
            friendName = FriendName(player);
            friendTimer = FriendRefresh;
            friendStill = 0f;
            Destination = goalFrom = at;
            goal = hit.point;
            HasDestination = true;
            timer = 0f;
            Compute();
            return true;
        }

        static string FriendName(RemoteCar player) => string.IsNullOrEmpty(player.Name) ? Loc.T("Joueur", "Player") : player.Name;

        public void ClearDestination()
        {
            if (OnCourse) return;       // parcours imposé : il se retire avec ClearCourse
            HasDestination = false;
            following = false;
            friend = null;
            route.Clear();
            routeDist.Clear();
            Remaining = RemainingTime = 0f;
            Version++;
        }

        // ------------------------------------------------------------------ parcours imposé (course)

        /// <summary>
        /// Un parcours imposé (course chronométrée, <see cref="Races"/>) remplace l'itinéraire : pas de recalcul, pas
        /// d'arrivée annoncée, et la destination ne se change plus sur la carte tant qu'il est affiché.
        /// </summary>
        public bool OnCourse { get; private set; }
        bool savedHas;
        Vector3 savedPoint;
        string savedFr, savedEn;
        bool savedFollowing;
        RemoteCar savedFriend;

        /// <summary>
        /// Affiche un parcours imposé à la place de l'itinéraire : les points de <paramref name="points"/> à partir de
        /// l'indice <paramref name="first"/> (ce qu'il reste à parcourir), le dernier étant l'arrivée. La destination
        /// que le joueur avait choisie est rendue par <see cref="ClearCourse"/>.
        /// </summary>
        public void SetCourse(IReadOnlyList<Vector3> points, int first, string fr, string en)
        {
            if (GameManager.DedicatedServer || points == null || points.Count - first < 2) return;
            if (!OnCourse)
            {
                savedHas = HasDestination;
                savedPoint = Destination;
                savedFr = nameFr;
                savedEn = nameEn;
                savedFollowing = following;
                savedFriend = friend;
            }
            OnCourse = true;
            following = false;
            friend = null;
            route.Clear();
            routeDist.Clear();
            for (int i = Mathf.Max(0, first); i < points.Count; i++) Add(route, routeDist, points[i]);
            nearIndex = 0;
            along = 0f;
            Destination = route[route.Count - 1];
            nameFr = fr;
            nameEn = en;
            HasDestination = true;
            Remaining = routeDist[routeDist.Count - 1];
            RemainingTime = Remaining / 25f;
            Version++;
        }

        /// <summary>Retire le parcours imposé et rend au joueur la destination qu'il avait choisie.</summary>
        public void ClearCourse()
        {
            if (!OnCourse) return;
            OnCourse = false;
            ClearDestination();
            // Le joueur suivi avant la course l'est de nouveau, s'il est encore là
            if (savedFollowing) { if (savedFriend) Follow(savedFriend); }
            else if (savedHas) SetDestination(savedPoint, savedFr, savedEn);
            savedFollowing = false;
            savedFriend = null;
        }

        /// <summary>
        /// Joueur suivi : la destination est sa voiture, image par image. S'il a quitté la partie (sa voiture n'existe
        /// plus), la destination s'efface et le joueur en est prévenu.
        /// </summary>
        void TrackFriend()
        {
            if (friend)
            {
                Destination = friend.transform.position;
                friendName = FriendName(friend);
                return;
            }
            string name = friendName;
            ClearDestination();
            if (game.Hud && game.State == GameManager.Phase.Playing)
                game.Hud.Announce(Loc.T("DESTINATION EFFACÉE", "DESTINATION CLEARED"), Loc.T($"{name} a quitté la partie", $"{name} left the game"), GpsColor, 3f);
        }

        /// <summary>
        /// Joueur suivi : toutes les deux secondes, s'il a bougé, l'arrivée de l'itinéraire est reportée sur la route la
        /// plus proche de sa voiture, et l'itinéraire est refait aussitôt.
        /// </summary>
        void FollowFriend(float dt)
        {
            friendTimer -= dt;
            if (friendTimer > 0f) return;
            friendTimer = FriendRefresh;
            if ((Destination - goalFrom).sqrMagnitude < FriendMove * FriendMove) return;
            var hit = game.Road.Nearest(Destination, 4000f);
            if (!hit.Found) return;
            goal = hit.point;
            goalFrom = Destination;
            timer = 0f;
        }

        void Update()
        {
            if (!game || GameManager.DedicatedServer) return;
            if (following) TrackFriend();
            if (OnCourse)
            {
                if (game.State == GameManager.Phase.Playing) Track();
            }
            else if (HasDestination && game.State == GameManager.Phase.Playing && game.Road)
            {
                // Recalcul régulier (on peut quitter l'itinéraire), et tout de suite après un replacement sur la route
                timer -= Time.unscaledDeltaTime;
                if (following) FollowFriend(Time.unscaledDeltaTime);
                var car = game.Player;
                if (timer <= 0f || car.TeleportCount != teleports)
                {
                    teleports = car.TeleportCount;
                    timer = Recompute;
                    Compute();
                }
                Track();
                Vector3 d = car.transform.position - Destination;
                d.y = 0f;
                if (following)
                {
                    // Auprès du joueur suivi : arrivé seulement s'il est à l'arrêt, ou presque, depuis un moment ; tant
                    // qu'il roule, on continue de le suivre
                    bool stopped = d.magnitude < ArriveDistance && friend.Car.Speed < FriendStopSpeed;
                    friendStill = stopped ? friendStill + Time.unscaledDeltaTime : 0f;
                    if (friendStill >= FriendStopTime) ClearDestination();
                }
                else if (d.magnitude < ArriveDistance || HasRoute && Remaining - along < ArriveDistance * 0.6f)
                {
                    ClearDestination();
                }
            }
            UpdateRibbon();
            UpdateBeacon();
        }

        // ------------------------------------------------------------------ itinéraire

        struct Arc
        {
            public RoadNetwork.Edge e;
            public int dir;
            public int Index => e.index * 2 + (dir > 0 ? 0 : 1);
            public RoadNetwork.Node End => dir > 0 ? e.b : e.a;
        }

        struct Step
        {
            public Arc arc;
            public float from, to;    // abscisses parcourues
        }

        float[] cost;
        int[] prev;
        bool[] closed;
        readonly List<(Arc arc, float penalty)> next = new List<(Arc, float)>();
        readonly List<Step> steps = new List<Step>();

        /// <summary>Temps (s) depuis le début d'une arête jusqu'à l'abscisse s, selon la vitesse confortable de chaque tronçon.</summary>
        float TimeAt(RoadNetwork.Edge e, float s)
        {
            var t = times[e.index];
            int i = e.IndexAt(s);
            float seg = e.dist[i] - e.dist[i - 1];
            float k = seg > 0f ? Mathf.Clamp01((Mathf.Clamp(s, 0f, e.Length) - e.dist[i - 1]) / seg) : 0f;
            return Mathf.Lerp(t[i - 1], t[i], k);
        }

        float Span(RoadNetwork.Edge e, float a, float b) => Mathf.Abs(TimeAt(e, b) - TimeAt(e, a));

        void PrepareTimes()
        {
            if (times != null && times.Length == road.Edges.Count) return;
            times = new float[road.Edges.Count][];
            foreach (var e in road.Edges)
            {
                var t = new float[e.pts.Length];
                // Pistes de terre : glissantes, on y roule moins vite que leur vitesse affichée
                float slow = e.kind == RoadKind.Track ? 1.25f : 1f;
                for (int i = 1; i < t.Length; i++)
                    t[i] = t[i - 1] + (e.dist[i] - e.dist[i - 1]) / Mathf.Max(4f, 0.5f * (e.limit[i - 1] + e.limit[i])) * slow;
                times[e.index] = t;
            }
            int n = road.Edges.Count * 2;
            cost = new float[n];
            prev = new int[n];
            closed = new bool[n];
        }

        /// <summary>Cap horizontal d'un parcours à une abscisse.</summary>
        static Vector3 Heading(RoadNetwork.Edge e, float s, int dir)
        {
            e.Evaluate(s, out var tangent, out _);
            var f = new Vector3(tangent.x, 0f, tangent.z) * dir;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        static float EndOf(Arc a) => a.dir > 0 ? a.e.Length : 0f;
        static float StartOf(Arc a) => a.dir > 0 ? 0f : a.e.Length;

        /// <summary>
        /// Suites possibles d'un parcours arrivé à son nœud, avec le coût du passage. Échangeur : tout droit sur
        /// l'autoroute, ou la bretelle de sortie de son côté ; d'une bretelle d'entrée, la chaussée de son côté.
        /// Carrefour : toute route sauf celle d'où l'on vient (sens interdits exclus) ; bout de route : demi-tour.
        /// </summary>
        void Successors(Arc a)
        {
            next.Clear();
            var n = a.End;
            var fin = Heading(a.e, EndOf(a), a.dir);
            if (n.merge && (a.e.kind == RoadKind.Highway || a.e.oneway))
            {
                if (a.e.kind == RoadKind.Highway)
                {
                    var right = new Vector3(fin.z, 0f, -fin.x);
                    foreach (var o in n.edges)
                    {
                        if (o == a.e) continue;
                        if (o.kind == RoadKind.Highway) next.Add((new Arc { e = o, dir = o.a == n ? 1 : -1 }, 0f));
                        else if (o.oneway && o.a == n && o.b != n && Vector3.Dot(o.pts[Mathf.Min(2, o.pts.Length - 1)] - n.pos, right) > 0f)
                            next.Add((new Arc { e = o, dir = 1 }, 1f));
                    }
                }
                else
                {
                    Vector3 end = a.e.pts[a.e.pts.Length - 1];
                    foreach (var o in n.edges)
                    {
                        if (o.kind != RoadKind.Highway) continue;
                        int dir = o.a == n ? 1 : -1;
                        o.Evaluate(dir > 0 ? 0f : o.Length, out _, out var right);
                        if (Vector3.Dot(end - n.pos, right) * dir > 0f) next.Add((new Arc { e = o, dir = dir }, 1f));
                    }
                }
                return;
            }
            bool junction = n.radius > 0f && n.edges.Count >= 3;
            foreach (var o in n.edges)
                for (int k = 0; k < 2; k++)
                {
                    int dir = k == 0 ? 1 : -1;
                    if ((dir > 0 ? o.a : o.b) != n) continue;
                    if (dir < 0 && o.oneway) continue;                    // sens interdit
                    if (o == a.e && dir == -a.dir) continue;              // demi-tour : seulement au bout de la route
                    var arc = new Arc { e = o, dir = dir };
                    float pen = 0f;
                    if (junction)
                    {
                        float angle = Vector3.SignedAngle(fin, Heading(o, StartOf(arc), dir), Vector3.up);
                        pen = 2f + Mathf.Abs(angle) / 90f * (angle < 0f ? 3f : 1.5f);
                    }
                    next.Add((arc, pen));
                }
            if (next.Count == 0 && !a.e.oneway) next.Add((new Arc { e = a.e, dir = -a.dir }, DeadEndPenalty));
        }

        /// <summary>Itinéraire le plus rapide de la voiture à la destination (vide si elle est injoignable).</summary>
        void Compute()
        {
            Version++;
            route.Clear();
            routeDist.Clear();
            nearIndex = 0;
            along = 0f;
            if (!HasDestination) return;
            var car = game.Player.transform;
            float time = Solve(car.position, car.forward, goal);
            if (time < 0f) return;
            RemainingTime = time;
            BuildPolyline(route, routeDist);
            Remaining = routeDist.Count > 0 ? routeDist[routeDist.Count - 1] : 0f;
        }

        readonly List<Vector3> planPoints = new List<Vector3>();
        readonly List<float> planDist = new List<float>();

        /// <summary>
        /// Itinéraire le plus rapide entre deux points de la carte (posés sur la route la plus proche), en partant dans
        /// le sens de <paramref name="heading"/> : ses points (dans la voie, carrefours arrondis) sont ajoutés à
        /// <paramref name="points"/>. Sert à tracer les parcours des courses, tronçon après tronçon : le cap d'arrivée
        /// (<paramref name="endHeading"/>) est le cap de départ du tronçon suivant. Faux si l'arrivée est injoignable.
        /// </summary>
        public bool Plan(Vector3 from, Vector3 heading, Vector3 to, List<Vector3> points, out Vector3 endHeading)
        {
            endHeading = heading;
            if (!game.Road || game.Map == null) return false;
            from.y = game.Map.Height(from.x, from.z);
            to.y = game.Map.Height(to.x, to.z);
            var hit = game.Road.Nearest(to, 4000f);
            if (!hit.Found || Solve(from, heading, hit.point) < 0f) return false;
            planPoints.Clear();
            planDist.Clear();
            BuildPolyline(planPoints, planDist);
            foreach (var p in planPoints)
                if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) >= 0.5f) points.Add(p);
            var last = steps[steps.Count - 1];
            endHeading = Heading(last.arc.e, last.to, last.arc.dir);
            return true;
        }

        /// <summary>
        /// Plus court chemin en temps de <paramref name="from"/> (en partant de préférence dans le sens de
        /// <paramref name="heading"/>) à <paramref name="to"/> : remplit les étapes (<see cref="steps"/>) et renvoie le
        /// temps estimé (s), ou -1 si l'arrivée est injoignable.
        /// </summary>
        float Solve(Vector3 from, Vector3 heading, Vector3 to)
        {
            steps.Clear();
            road = game.Road;
            if (!road || road.Edges.Count == 0) return -1f;
            PrepareTimes();
            var start = road.Nearest(from, 4000f);
            var goal = road.Nearest(to, 50f);
            if (!start.Found || !goal.Found) return -1f;

            for (int i = 0; i < cost.Length; i++) { cost[i] = float.MaxValue; prev[i] = -1; closed[i] = false; }
            var e0 = start.edge;
            var g = goal.edge;
            Vector3 carFwd = heading;
            carFwd.y = 0f;
            int ahead = e0.oneway || Vector3.Dot(Heading(e0, start.s, 1), carFwd) >= 0f ? 1 : -1;

            float best = float.MaxValue;
            int bestArc = -2;          // -1 : directement sur la route de départ
            int bestDir = 0;
            var open = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                int dir = k == 0 ? ahead : -ahead;
                if (dir < 0 && e0.oneway) continue;
                float pen = dir == ahead ? 0f : UTurnPenalty;
                var arc = new Arc { e = e0, dir = dir };
                float c = pen + Span(e0, start.s, EndOf(arc));
                if (c < cost[arc.Index]) { cost[arc.Index] = c; prev[arc.Index] = -1; open.Add(arc.Index); }
                // Destination plus loin sur la même route, dans ce sens
                if (g == e0 && (goal.s - start.s) * dir >= 0f)
                {
                    float direct = pen + Span(e0, start.s, goal.s);
                    if (direct < best) { best = direct; bestArc = -1; bestDir = dir; }
                }
            }

            // Dijkstra sur les parcours orientés (une centaine : liste ouverte simple)
            int goalVia = -1;
            while (open.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < open.Count; i++) if (cost[open[i]] < cost[open[pick]]) pick = i;
                int cur = open[pick];
                open.RemoveAt(pick);
                if (closed[cur]) continue;
                closed[cur] = true;
                if (cost[cur] >= best) break;
                var arc = new Arc { e = road.Edges[cur / 2], dir = cur % 2 == 0 ? 1 : -1 };
                Successors(arc);
                foreach (var (o, pen) in next)
                {
                    float enter = cost[cur] + pen;
                    // La destination est sur cette route : on s'arrête en chemin
                    if (o.e == g)
                    {
                        float c = enter + Span(g, StartOf(o), goal.s);
                        if (c < best) { best = c; bestArc = o.Index; bestDir = o.dir; goalVia = cur; }
                    }
                    float full = enter + Span(o.e, 0f, o.e.Length);
                    if (full < cost[o.Index] && !closed[o.Index])
                    {
                        cost[o.Index] = full;
                        prev[o.Index] = cur;
                        open.Add(o.Index);
                    }
                }
            }
            if (bestArc == -2) return -1f;

            // Étapes de l'itinéraire, du départ à la destination
            if (bestArc == -1) steps.Add(new Step { arc = new Arc { e = e0, dir = bestDir }, from = start.s, to = goal.s });
            else
            {
                var chain = new List<int>();
                for (int a = goalVia; a >= 0; a = prev[a]) chain.Add(a);
                chain.Reverse();
                for (int i = 0; i < chain.Count; i++)
                {
                    var arc = new Arc { e = road.Edges[chain[i] / 2], dir = chain[i] % 2 == 0 ? 1 : -1 };
                    steps.Add(new Step { arc = arc, from = i == 0 ? start.s : StartOf(arc), to = EndOf(arc) });
                }
                var last = new Arc { e = g, dir = bestDir };
                steps.Add(new Step { arc = last, from = StartOf(last), to = goal.s });
            }
            return best;
        }

        /// <summary>Hors autoroute, l'itinéraire suit la voie de droite ; sur l'autoroute, le milieu de la chaussée.</summary>
        static Vector3 OnLane(RoadNetwork.Edge e, float s, int dir)
        {
            float extra = e.lanes > 1 ? (e.lanes - 1) * RoadNetwork.LaneWidth * 0.5f : 0f;
            return e.LanePosition(Mathf.Clamp(s, 0f, e.Length), dir, out _, extra);
        }

        static void Add(List<Vector3> route, List<float> routeDist, Vector3 p)
        {
            if (route.Count > 0)
            {
                float d = Vector3.Distance(route[route.Count - 1], p);
                if (d < 0.5f) return;
                routeDist.Add(routeDist[routeDist.Count - 1] + d);
            }
            else routeDist.Add(0f);
            route.Add(p);
        }

        /// <summary>Points de l'itinéraire (carrefours arrondis), d'après les étapes de <see cref="Solve"/>.</summary>
        void BuildPolyline(List<Vector3> route, List<float> routeDist)
        {
            void Add(Vector3 p) => RouteGuide.Add(route, routeDist, p);
            for (int i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                var e = st.arc.e;
                int dir = st.arc.dir;
                // Bouts de chaussée raccourcis aux carrefours (sauf au départ et à l'arrivée)
                float from = st.from, to = st.to;
                if (i > 0) from = dir > 0 ? Mathf.Max(from, e.trimA) : Mathf.Min(from, e.Length - e.trimB);
                if (i < steps.Count - 1) to = dir > 0 ? Mathf.Min(to, e.Length - e.trimB) : Mathf.Max(to, e.trimA);
                if ((to - from) * dir < 0f) to = from;
                if (i > 0)
                {
                    // Traversée du nœud : courbe entre la chaussée quittée et la suivante
                    var pe = steps[i - 1].arc;
                    Vector3 p0 = route[route.Count - 1];
                    Vector3 f0 = Heading(pe.e, EndOf(pe), pe.dir);
                    Vector3 p3 = OnLane(e, from, dir);
                    Vector3 f3 = Heading(e, from, dir);
                    float k = Vector3.Distance(p0, p3) * 0.4f;
                    for (int j = 1; j < 8; j++)
                    {
                        float t = j / 8f, u = 1f - t;
                        Add(u * u * u * p0 + 3f * u * u * t * (p0 + f0 * k) + 3f * u * t * t * (p3 - f3 * k) + t * t * t * p3);
                    }
                }
                Add(OnLane(e, from, dir));
                // Points de l'axe entre les deux abscisses, dans le sens de parcours
                if (dir > 0) { for (int j = 0; j < e.pts.Length; j++) if (e.dist[j] > from + 0.5f && e.dist[j] < to - 0.5f) Add(OnLane(e, e.dist[j], 1)); }
                else { for (int j = e.pts.Length - 1; j >= 0; j--) if (e.dist[j] < from - 0.5f && e.dist[j] > to + 0.5f) Add(OnLane(e, e.dist[j], -1)); }
                Add(OnLane(e, to, dir));
            }
        }

        /// <summary>Position de la voiture le long de l'itinéraire (projection sur le tronçon le plus proche, près du dernier connu).</summary>
        void Track()
        {
            if (route.Count < 2) return;
            Vector3 p = game.Player.transform.position;
            float bestD = float.MaxValue;
            int lo = Mathf.Max(0, nearIndex - 10), hi = Mathf.Min(route.Count - 2, nearIndex + 80);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = lo; i <= hi; i++)
                {
                    Vector3 a = route[i], ab = route[i + 1] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    float d = (p - (a + ab * t)).sqrMagnitude;
                    if (d < bestD) { bestD = d; nearIndex = i; along = Mathf.Lerp(routeDist[i], routeDist[i + 1], t); }
                }
                // Parcours imposé (pas de recalcul) : la voiture a pu le quitter et le reprendre plus loin
                // (replacement, raccourci) ; à égalité, le point le plus tôt dans le parcours (tours suivants)
                if (!OnCourse || bestD < 60f * 60f) break;
                lo = 0;
                hi = route.Count - 2;
                bestD = float.MaxValue;
            }
        }

        /// <summary>Point de l'itinéraire à une distance donnée depuis son départ.</summary>
        Vector3 PointAt(float d, ref int hint)
        {
            while (hint < routeDist.Count - 2 && routeDist[hint + 1] < d) hint++;
            float seg = routeDist[hint + 1] - routeDist[hint];
            float t = seg > 0f ? Mathf.Clamp01((d - routeDist[hint]) / seg) : 0f;
            return Vector3.Lerp(route[hint], route[hint + 1], t);
        }

        // ------------------------------------------------------------------ ruban sur la route, colonne de lumière

        void BuildWorld()
        {
            var assets = game.assets;
            if (!assets.spark) return;
            ribbonMaterial = new Material(assets.spark) { name = "GPS ribbon (runtime)" };
            ribbonMaterial.SetTexture("_BaseMap", ChevronTexture());
            ribbonMaterial.SetColor("_BaseColor", new Color(0.55f, 1.25f, 1.6f, 1f));
            ribbonMaterial.renderQueue = 3010;
            var go = new GameObject("GpsRibbon");
            go.transform.SetParent(transform, false);
            ribbonMesh = new Mesh { name = "GpsRibbon" };
            ribbonMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = ribbonMesh;
            ribbonRenderer = go.AddComponent<MeshRenderer>();
            ribbonRenderer.sharedMaterial = ribbonMaterial;
            ribbonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ribbonRenderer.receiveShadows = false;
            ribbonRenderer.enabled = false;

            var beam = new GameObject("GpsBeacon");
            beam.transform.SetParent(transform, false);
            beacon = beam.AddComponent<LineRenderer>();
            var m = new Material(assets.spark) { name = "GPS beacon (runtime)" };
            m.SetColor("_BaseColor", new Color(0.5f, 1.1f, 1.5f, 1f));
            beacon.sharedMaterial = m;
            beacon.positionCount = 2;
            beacon.useWorldSpace = true;
            beacon.alignment = LineAlignment.View;
            beacon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beacon.receiveShadows = false;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.25f), new GradientAlphaKey(0f, 1f) });
            beacon.colorGradient = gradient;
            beacon.enabled = false;
        }

        /// <summary>Chevrons pointés vers l'avant (V croissant) sur une bande légèrement lumineuse, bords adoucis.</summary>
        static Texture2D ChevronTexture()
        {
            const int w = 64, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "GPS chevrons", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            tex.wrapModeU = TextureWrapMode.Clamp;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                    float edge = Mathf.Clamp01((1f - Mathf.Abs(u)) / 0.18f);
                    // Pointe en haut : v = 0.62 au centre, descend vers les bords
                    float line = 0.62f - Mathf.Abs(u) * 0.38f;
                    float chevron = Mathf.Clamp01(1f - Mathf.Abs(v - line) / 0.11f);
                    chevron = Mathf.SmoothStep(0f, 1f, chevron * 1.6f);
                    float a = Mathf.Max(0.22f, chevron) * edge;
                    px[y * w + x] = new Color(a, a, a, a);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        void UpdateRibbon()
        {
            if (!ribbonRenderer) return;
            bool show = HasRoute && game.State == GameManager.Phase.Playing;
            ribbonRenderer.enabled = show;
            if (!show) return;
            verts.Clear(); uvs.Clear(); colors.Clear(); tris.Clear();
            float start = Mathf.Max(0f, along - 3f), end = Mathf.Min(Remaining, along + RibbonLength);
            int hint = Mathf.Max(0, nearIndex - 2);
            float d = start;
            Vector3 prevP = PointAt(d, ref hint);
            while (d <= end + 0.01f)
            {
                Vector3 p = PointAt(d, ref hint);
                float dn = Mathf.Min(d + 1f, Remaining);
                int h2 = hint;
                Vector3 f = PointAt(dn, ref h2) - p;
                if (f.sqrMagnitude < 1e-4f) f = p - prevP;
                f.y = 0f;
                Vector3 side = new Vector3(f.z, 0f, -f.x).normalized * (RibbonWidth * 0.5f);
                Vector3 lifted = p + Vector3.up * RibbonLift;
                verts.Add(lifted - side);
                verts.Add(lifted + side);
                float v = d / ChevronSpacing;
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
                float rel = d - along;
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 9f, rel)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RibbonLength - 180f, RibbonLength, rel)));
                var c = new Color(fade, fade, fade, fade);
                colors.Add(c);
                colors.Add(c);
                prevP = p;
                if (d >= end) break;
                // Pas plus fin dans les virages serrés et près de la voiture
                d = Mathf.Min(end, d + (rel < 60f ? 1.5f : 3f));
            }
            for (int i = 0; i + 3 < verts.Count; i += 2)
            {
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 3);
            }
            ribbonMesh.Clear();
            ribbonMesh.SetVertices(verts);
            ribbonMesh.SetUVs(0, uvs);
            ribbonMesh.SetColors(colors);
            ribbonMesh.SetTriangles(tris, 0);
            ribbonMesh.RecalculateBounds();
            // Les chevrons défilent doucement vers l'avant
            ribbonMaterial.SetTextureOffset("_BaseMap", new Vector2(0f, -Time.time * 0.6f));
        }

        void UpdateBeacon()
        {
            if (!beacon) return;
            bool show = HasDestination && game.State == GameManager.Phase.Playing;
            beacon.enabled = show;
            if (!show) return;
            var cam = Camera.main;
            float dist = cam ? Vector3.Distance(cam.transform.position, Destination) : 100f;
            beacon.SetPosition(0, Destination - Vector3.up * 2f);
            beacon.SetPosition(1, Destination + Vector3.up * 260f);
            // Joueur suivi : la colonne s'amincit puis disparaît en approchant de sa voiture (on roule avec lui)
            float close = following ? Mathf.InverseLerp(45f, 110f, dist) : 1f;
            beacon.enabled = close > 0.01f;
            // Visible de loin : la largeur grandit avec la distance
            beacon.widthMultiplier = Mathf.Clamp(dist * 0.006f, 1.6f, 16f) * (1f + Mathf.Sin(Time.time * 3f) * 0.08f) * close;
        }

        static string Distance(float m) => m < 950f
            ? $"{Mathf.Max(0, Mathf.RoundToInt(m / 10f) * 10)} m"
            : Loc.F($"{m / 1000f:0.0} km");

        // ------------------------------------------------------------------ minicarte

        /// <summary>Itinéraire sur la minicarte : trait presque noir liseré de blanc (la minicarte reste en gris), destination au bord si elle est hors champ.</summary>
        void DrawOnMiniMap(RectTransform root, Func<Vector3, Vector2> toUi, float radius)
        {
            if (!miniLine)
            {
                miniLine = Rect(root, "GpsRoute", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(radius * 2f, radius * 2f)).gameObject.AddComponent<RouteLine>();
                miniLine.core = new Color(0.08f, 0.08f, 0.08f, 1f);
                miniLine.edge = new Color(1f, 1f, 1f, 0.95f);
                miniLine.width = 5.5f;
                miniLine.edgeWidth = 1.4f;
                var map = root.Find("Map");
                miniLine.transform.SetSiblingIndex(map ? map.GetSiblingIndex() + 1 : 0);
                miniPin = Rect(root, "GpsPin", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
                var pinEdge = Rect(miniPin, "Edge", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15f, 15f)).gameObject.AddComponent<Image>();
                pinEdge.color = Color.white;
                pinEdge.raycastTarget = false;
                var pinCore = Rect(miniPin, "Core", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f)).gameObject.AddComponent<Image>();
                pinCore.color = new Color(0.08f, 0.08f, 0.08f, 1f);
                pinCore.raycastTarget = false;
                miniPin.localEulerAngles = new Vector3(0f, 0f, 45f);
                miniPin.SetSiblingIndex(miniLine.transform.GetSiblingIndex() + 1);
                // Distance restante : en bas du disque, gris presque noir liseré de blanc (la minicarte reste en gris) ;
                // juste au-dessus, le pseudo du joueur suivi
                Text Caption(int size, float y)
                {
                    var text = Label(root, size, new Color(0.08f, 0.08f, 0.08f, 1f), FontStyle.BoldAndItalic, TextAnchor.MiddleCenter, new Vector2(0f, y), new Vector2(160f, 24f));
                    text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    Destroy(text.GetComponent<Shadow>());
                    var edge = text.gameObject.AddComponent<Outline>();
                    edge.effectColor = new Color(1f, 1f, 1f, 0.95f);
                    edge.effectDistance = new Vector2(1.3f, -1.3f);
                    return text;
                }
                miniDistance = Caption(18, -radius + 18f);
                miniName = Caption(15, -radius + 38f);
                miniName.supportRichText = false;
            }
            miniLine.strips.Clear();
            bool show = HasDestination;
            miniPin.gameObject.SetActive(show);
            miniDistance.gameObject.SetActive(show);
            miniName.gameObject.SetActive(show && following);
            if (show) miniDistance.text = HasRoute ? "◆ " + Distance(Mathf.Max(0f, Remaining - along)) : "◆ —";
            if (show && following) miniName.text = friendName;
            if (show && HasRoute)
            {
                // Seulement la partie devant la voiture, découpée au bord du disque
                float r = radius - 4f, r2 = r * r;
                List<Vector2> strip = null;
                Vector2 prevUi = Vector2.zero;
                bool prevIn = false;
                int first = Mathf.Max(0, nearIndex);
                for (int i = first; i < route.Count; i++)
                {
                    Vector2 p = toUi(route[i]);
                    bool inside = p.sqrMagnitude <= r2;
                    if (i > first && (inside || prevIn))
                    {
                        if (!prevIn) { strip = new List<Vector2> { Clip(p, prevUi, r) }; miniLine.strips.Add(strip); }
                        if (inside) strip.Add(p);
                        else { strip.Add(Clip(prevUi, p, r)); strip = null; }
                    }
                    else if (i == first && inside) { strip = new List<Vector2> { p }; miniLine.strips.Add(strip); }
                    prevUi = p;
                    prevIn = inside;
                }
            }
            miniLine.SetVerticesDirty();
            if (show)
            {
                Vector2 pin = toUi(Destination);
                float edge = radius - 9f;
                if (pin.sqrMagnitude > edge * edge) pin = pin.normalized * edge;
                miniPin.anchoredPosition = pin;
            }
        }

        /// <summary>Point où le segment (dedans → dehors) coupe le cercle de rayon r.</summary>
        static Vector2 Clip(Vector2 inside, Vector2 outside, float r)
        {
            Vector2 d = outside - inside;
            float a = d.sqrMagnitude, b = 2f * Vector2.Dot(inside, d), c = inside.sqrMagnitude - r * r;
            float disc = b * b - 4f * a * c;
            if (a < 1e-6f || disc < 0f) return inside;
            float t = Mathf.Clamp01((-b + Mathf.Sqrt(disc)) / (2f * a));
            return inside + d * t;
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
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

        static Text Label(Transform parent, int size, Color color, FontStyle style, TextAnchor align, Vector2 pos, Vector2 box)
        {
            var rt = Rect(parent, "Text", new Vector2(0.5f, 0.5f), pos, box);
            rt.pivot = new Vector2(0f, 0.5f);
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

    /// <summary>
    /// Trait d'itinéraire (carte globale, minicarte) : une ou plusieurs lignes brisées en coordonnées du rectangle,
    /// d'épaisseur constante (jointures en onglet bornées), avec un liseré dessous.
    /// </summary>
    public class RouteLine : MaskableGraphic
    {
        public readonly List<List<Vector2>> strips = new List<List<Vector2>>();
        public Color core = Color.white, edge = Color.black;
        public float width = 6f, edgeWidth = 1.5f;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (edgeWidth > 0f) foreach (var s in strips) Strip(vh, s, width * 0.5f + edgeWidth, edge);
            foreach (var s in strips) Strip(vh, s, width * 0.5f, core);
        }

        static void Strip(VertexHelper vh, List<Vector2> pts, float half, Color32 c)
        {
            int n = pts.Count;
            if (n < 2) return;
            int b = vh.currentVertCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 dIn = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                Vector2 dOut = i < n - 1 ? (pts[i + 1] - pts[i]).normalized : dIn;
                Vector2 nIn = new Vector2(-dIn.y, dIn.x), nOut = new Vector2(-dOut.y, dOut.x);
                Vector2 miter = (nIn + nOut);
                miter = miter.sqrMagnitude > 1e-6f ? miter.normalized : nIn;
                float scale = 1f / Mathf.Max(0.5f, Vector2.Dot(miter, nIn));
                Vector2 off = miter * half * scale;
                // Bouts arrondis approximés : prolongement d'une demi-épaisseur
                Vector2 p = pts[i];
                if (i == 0) p -= dOut * half * 0.6f;
                if (i == n - 1) p += dIn * half * 0.6f;
                vh.AddVert(p + off, c, Vector2.zero);
                vh.AddVert(p - off, c, Vector2.zero);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int k = b + i * 2;
                vh.AddTriangle(k, k + 2, k + 1);
                vh.AddTriangle(k + 1, k + 2, k + 3);
            }
        }
    }
}
