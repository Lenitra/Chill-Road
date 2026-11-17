using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NoHesi
{
    public enum RoadKind { Main, Mountain, Street, Lane, Track, Highway, Ramp }

    /// <summary>
    /// Réseau routier de la carte : arêtes (routes entre deux nœuds) et nœuds (carrefours, extrémités), lus dans la
    /// carte générée. Construit les maillages (chaussée, accotements fondus dans le terrain, marquages, glissières,
    /// ponts, tunnels, carrefours, voies d'insertion) et répond aux requêtes du jeu : route la plus proche, surface
    /// sous la voiture, position d'une voie.
    /// Circulation à droite : la voie d'un sens de parcours est à droite de l'axe. Les bretelles d'autoroute sont à
    /// sens unique (de a vers b) et rejoignent l'autoroute en un nœud « merge », sans carrefour.
    /// </summary>
    public class RoadNetwork : MonoBehaviour
    {
        // Gabarit de référence (écran de chargement, shader de route)
        public const float LaneWidth = 3.5f;
        public const float InnerEdge = 2.4f;
        public const float HighwayOuterEdge = InnerEdge + LaneWidth * 3f;
        public const float CountryEdge = LaneWidth + 0.1f;
        public const float CountryHalfWidth = LaneWidth + 1.0f;
        public const float DirtLaneOffset = 1.1f;
        public const float DirtGrip = 0.72f;
        /// <summary>Adhérence sur chaussée enneigée (neige tassée), relative au goudron sec.</summary>
        public const float SnowGrip = 0.62f;
        const float BucketSize = 32f;
        const int SectionsPerChunk = 48;

        public static RoadNetwork Instance { get; private set; }

        public class Node
        {
            public string id;
            public Vector3 pos;
            public readonly List<Edge> edges = new List<Edge>();
            /// <summary>Rayon du carrefour (distance de l'axe où les chaussées s'arrêtent).</summary>
            public float radius;
            /// <summary>Point de l'autoroute où s'accroche une bretelle : ni carrefour ni raccourcissement des chaussées.</summary>
            public bool merge;
            /// <summary>Véhicule du trafic engagé dans le carrefour.</summary>
            public object occupant;
            public float occupiedUntil;
        }

        public class Edge
        {
            public int index;
            public string id;
            public RoadKind kind;
            public Node a, b;
            public Vector3[] pts;
            public float[] dist;
            public Vector3[] tangents;
            public float hw, lane, sidewalk, speed;
            /// <summary>Voies par sens (autoroute : 3, la voie 0 longe le terre-plein) et demi-largeur du terre-plein.</summary>
            public int lanes = 1;
            public float median;
            public bool dirt;
            /// <summary>Sens unique (bretelle) : toujours parcourue de a vers b, sur son axe.</summary>
            public bool oneway;
            /// <summary>Voies d'insertion et de décélération accolées à la chaussée (autoroute).</summary>
            public MapData.AuxJson[] aux;
            public byte[] rail;
            public MapData.SpanJson[] spans;
            /// <summary>Vitesse confortable en chaque point (virages), m/s.</summary>
            public float[] limit;
            public float trimA, trimB;
            public float Length => dist[dist.Length - 1];
            public float TotalHalfWidth => hw + sidewalk;

            /// <summary>
            /// Largeur de la voie d'insertion ou de décélération à l'abscisse s, du côté donné (+1 : à droite de
            /// l'axe dans le sens a → b) : pleine largeur près du nœud de la bretelle, biseau sur le dernier tiers.
            /// </summary>
            public float AuxWidth(float s, int side)
            {
                if (aux == null) return 0f;
                float w = 0f;
                foreach (var x in aux)
                {
                    if (x.side != side) continue;
                    float len = x.sb - x.sa;
                    if (Mathf.Abs(len) < 0.01f) continue;
                    float u = (s - x.sa) / len;
                    if (u < 0f || u > 1f) continue;
                    w = Mathf.Max(w, x.width * Mathf.Clamp01((1f - u) / 0.35f));
                }
                return w;
            }

            /// <summary>Demi-largeur roulable à l'abscisse s, du côté de l'écart latéral donné (voie d'insertion comprise).</summary>
            public float HalfWidthAt(float s, float lateral) => TotalHalfWidth + AuxWidth(s, lateral >= 0f ? 1 : -1);

            public int IndexAt(float s)
            {
                int i = System.Array.BinarySearch(dist, Mathf.Clamp(s, 0f, Length));
                if (i < 0) i = ~i;
                return Mathf.Clamp(i, 1, pts.Length - 1);
            }

            /// <summary>Point de l'axe à l'abscisse s, tangente (sens a → b) et vecteur « droite » horizontal.</summary>
            public Vector3 Evaluate(float s, out Vector3 tangent, out Vector3 right)
            {
                int i = IndexAt(s);
                float seg = dist[i] - dist[i - 1];
                float t = seg > 0f ? Mathf.Clamp01((Mathf.Clamp(s, 0f, Length) - dist[i - 1]) / seg) : 0f;
                tangent = Vector3.Slerp(tangents[i - 1], tangents[i], t).normalized;
                Vector3 flat = new Vector3(tangent.x, 0f, tangent.z).normalized;
                right = new Vector3(flat.z, 0f, -flat.x);
                return Vector3.Lerp(pts[i - 1], pts[i], t);
            }

            /// <summary>Position dans la voie d'un sens de parcours (direction +1 : a → b), et cap.</summary>
            public Vector3 LanePosition(float s, int direction, out Vector3 forward, float extra = 0f)
            {
                Vector3 p = Evaluate(s, out var tangent, out var right);
                forward = tangent * direction;
                return p + right * ((lane + extra) * direction);
            }

            public string SpanAt(float s)
            {
                if (spans != null)
                    foreach (var sp in spans)
                        if (s >= sp.s0 && s <= sp.s1) return sp.kind;
                return null;
            }

            /// <summary>Épaisseur de la coque au-dessus du gabarit, dans le tunnel en s : la dalle d'un passage inférieur
            /// (sous le remblai d'une autre route), sinon <see cref="TunnelRoof"/>.</summary>
            public float RoofAt(float s)
            {
                if (spans != null)
                    foreach (var sp in spans)
                        if (s >= sp.s0 && s <= sp.s1 && sp.kind == "tunnel" && sp.roof > 0f) return sp.roof;
                return TunnelRoof;
            }

            public float LimitAt(float s) => limit[IndexAt(s)];
        }

        /// <summary>Résultat d'une recherche de route.</summary>
        public struct Hit
        {
            public Edge edge;
            public float s, lateral, distance;
            public Vector3 point;
            public bool Found => edge != null;
        }

        public struct Surface { public bool onRoad; public float grip, dirt, snow; }

        public readonly List<Edge> Edges = new List<Edge>();
        public readonly Dictionary<string, Node> Nodes = new Dictionary<string, Node>();
        /// <summary>Lampadaires posés par le décor et leurs têtes (éclairées la nuit).</summary>
        public readonly List<Transform> Lamps = new List<Transform>();
        public readonly List<Transform> LampHeads = new List<Transform>();
        /// <summary>Matériau des globes de lampadaires (son émission suit le cycle jour/nuit).</summary>
        public Material LampGlow { get; private set; }
        public GameAssets Assets => assets;
        public MapData Map { get; private set; }
        public float TotalLength { get; private set; }

        readonly Dictionary<Vector2Int, List<(int edge, int index)>> buckets = new Dictionary<Vector2Int, List<(int, int)>>();
        GameAssets assets;
        Transform root;

        static RoadKind KindOf(string k) => k == "main" ? RoadKind.Main : k == "mountain" ? RoadKind.Mountain : k == "street" ? RoadKind.Street
            : k == "lane" ? RoadKind.Lane : k == "highway" ? RoadKind.Highway : k == "ramp" ? RoadKind.Ramp : RoadKind.Track;

        Material tunnelGlow;

        /// <summary>Lit le réseau (sans construire les maillages : voir <see cref="Build"/>).</summary>
        public void Init(GameAssets a, MapData map)
        {
            Instance = this;
            assets = a;
            Map = map;
            root = new GameObject("Roads").transform;
            root.SetParent(transform, false);
            LampGlow = new Material(a.emissiveWhite) { name = "LampGlow (runtime)" };
            LampGlow.SetColor("_BaseColor", new Color(0.75f, 0.73f, 0.68f));
            // Marquages : posés à 2,5 cm de la chaussée, et tirés vers la caméra dans le tampon de profondeur (pas de scintillement au loin)
            if (a.roadMarking && a.roadMarking.HasProperty("_DepthOffset")) a.roadMarking.SetFloat("_DepthOffset", -1.5f);

            // Éclairage des tunnels : toujours allumé (les lampadaires, eux, suivent le cycle jour/nuit)
            tunnelGlow = new Material(a.emissiveWhite) { name = "TunnelGlow (runtime)" };

            foreach (var n in map.Json.nodes) Nodes[n.id] = new Node { id = n.id, pos = new Vector3(n.x, n.y, n.z), merge = n.merge != 0 };
            foreach (var j in map.Json.edges)
            {
                int count = j.pts.Length / 3;
                var e = new Edge
                {
                    index = Edges.Count, id = j.id, kind = KindOf(j.kind), a = Nodes[j.a], b = Nodes[j.b],
                    hw = j.hw, lane = j.lane, sidewalk = j.sidewalk, speed = j.speed, dirt = j.dirt != 0, spans = j.spans,
                    lanes = Mathf.Max(1, j.lanes), median = j.median, oneway = j.oneway != 0,
                    aux = j.aux != null && j.aux.Length > 0 ? j.aux : null,
                    pts = new Vector3[count], dist = new float[count], tangents = new Vector3[count], rail = new byte[count], limit = new float[count],
                };
                for (int i = 0; i < count; i++)
                {
                    e.pts[i] = new Vector3(j.pts[i * 3], j.pts[i * 3 + 1], j.pts[i * 3 + 2]);
                    e.rail[i] = (byte)(j.rail != null && i < j.rail.Length ? j.rail[i] : 0);
                    if (i > 0) e.dist[i] = e.dist[i - 1] + Vector3.Distance(e.pts[i - 1], e.pts[i]);
                }
                for (int i = 0; i < count; i++)
                    e.tangents[i] = (e.pts[Mathf.Min(i + 1, count - 1)] - e.pts[Mathf.Max(i - 1, 0)]).normalized;
                // Vitesse confortable : accélération latérale limitée dans les virages
                for (int i = 0; i < count; i++)
                {
                    int i0 = Mathf.Max(i - 2, 0), i1 = Mathf.Min(i + 2, count - 1);
                    Vector3 t0 = e.tangents[i0], t1 = e.tangents[i1];
                    float angle = Vector2.Angle(new Vector2(t0.x, t0.z), new Vector2(t1.x, t1.z)) * Mathf.Deg2Rad;
                    float radius = angle > 1e-3f ? (e.dist[i1] - e.dist[i0]) / angle : 1e4f;
                    e.limit[i] = Mathf.Min(e.speed, Mathf.Sqrt(3.4f * radius));
                }
                // Voies d'insertion : leur départ est calé exactement sur l'extrémité de l'arête (nœud de la bretelle)
                if (e.aux != null)
                    foreach (var x in e.aux)
                    {
                        if (x.sa < 1.5f) x.sa = 0f;
                        else if (x.sa > e.Length - 1.5f) x.sa = e.Length;
                    }
                e.a.edges.Add(e);
                e.b.edges.Add(e);
                Edges.Add(e);
                TotalLength += e.Length;
                for (int i = 0; i < count; i++)
                {
                    var key = BucketOf(e.pts[i]);
                    if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<(int, int)>();
                    list.Add((e.index, i));
                }
            }
            foreach (var n in Nodes.Values) SetupNode(n);
        }

        static Vector2Int BucketOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / BucketSize), Mathf.FloorToInt(p.z / BucketSize));

        /// <summary>Direction horizontale d'une arête en quittant un nœud.</summary>
        static Vector3 Outward(Node n, Edge e)
        {
            bool atA = e.a == n;
            Vector3 d = atA ? e.pts[Mathf.Min(3, e.pts.Length - 1)] - e.pts[0] : e.pts[Mathf.Max(e.pts.Length - 4, 0)] - e.pts[e.pts.Length - 1];
            d.y = 0f;
            return d.normalized;
        }

        /// <summary>
        /// Carrefour : chaque chaussée s'arrête là où elle ne recouvre plus aucune des autres routes du nœud (accotement
        /// de gravier compris), le polygone du carrefour couvre le reste. Deux surfaces superposées au même niveau
        /// clignotent (z-fighting) : il n'en reste aucune. Bout de route : aire de demi-tour.
        /// </summary>
        void SetupNode(Node n)
        {
            n.radius = 0f;
            int count = n.edges.Count;
            if (n.merge)
            {
                // Bretelle accolée à l'autoroute : rien ne se recouvre (la bretelle arrive à côté de la chaussée)
                foreach (var e in n.edges) { if (e.a == n) e.trimA = 0f; if (e.b == n) e.trimB = 0f; }
                return;
            }
            if (count == 1)
            {
                var e = n.edges[0];
                float r = DeadEndRadius(e), w = e.TotalHalfWidth;
                float trim = Mathf.Min(Mathf.Sqrt(r * r - w * w), e.Length * 0.45f);
                if (e.a == n) e.trimA = trim; else e.trimB = trim;
                n.radius = r;
                return;
            }
            float max = 0f;
            for (int i = 0; i < count; i++)
            {
                var e = n.edges[i];
                // une route peut partir et revenir au même nœud (boucle) : chacune de ses extrémités compte
                foreach (bool atA in new[] { true, false })
                {
                    if ((atA ? e.a : e.b) != n) continue;
                    float trim = 0f;
                    for (int k = 0; k < count; k++)
                    {
                        var o = n.edges[k];
                        foreach (bool oAtA in new[] { true, false })
                        {
                            if ((oAtA ? o.a : o.b) != n || (o == e && oAtA == atA)) continue;
                            trim = Mathf.Max(trim, OverlapTrim(e, atA, o, oAtA));
                        }
                    }
                    trim = Mathf.Min(trim, e.Length * 0.45f);
                    if (atA) e.trimA = trim; else e.trimB = trim;
                    max = Mathf.Max(max, trim);
                }
            }
            // Vrai carrefour (trois branches et plus) : les chaussées s'arrêtent un peu plus loin, pour laisser la
            // place d'arrondir les angles
            if (count >= 3 && max > 0.35f)
            {
                foreach (var e in n.edges)
                {
                    if (e.a == n) e.trimA = Mathf.Min(e.trimA + JunctionFlare, e.Length * 0.45f);
                    if (e.b == n) e.trimB = Mathf.Min(e.trimB + JunctionFlare, e.Length * 0.45f);
                }
                max += JunctionFlare;
            }
            // Simple continuation (presque dans l'axe) : rien à recouvrir, pas de carrefour
            n.radius = max > 0.35f ? max : 0f;
            if (n.radius <= 0f)
                foreach (var e in n.edges) { if (e.a == n) e.trimA = 0f; if (e.b == n) e.trimB = 0f; }
        }

        /// <summary>Rayon de l'aire de demi-tour au bout d'une route.</summary>
        static float DeadEndRadius(Edge e) => e.TotalHalfWidth + 5f;

        /// <summary>Largeur hors tout d'une route, gravier des accotements compris (ce qui ne doit pas se superposer).</summary>
        static float Clearance(Edge e) => e.TotalHalfWidth + 1.1f;

        /// <summary>
        /// Distance depuis le nœud à partir de laquelle la route e ne recouvre plus la route o : on avance le long de e
        /// et on teste son axe et ses deux bords (gravier compris) contre la bande de o.
        /// </summary>
        float OverlapTrim(Edge e, bool atA, Edge o, bool oAtA)
        {
            float we = Clearance(e), wo = Clearance(o);
            float maxT = Mathf.Min(60f, e.Length * 0.45f);
            float last = 0f;
            for (float t = 0f; t <= maxT; t += 0.5f)
            {
                float s = atA ? t : e.Length - t;
                Vector3 p = e.Evaluate(s, out _, out var right);
                if (InsideBand(o, oAtA, p, wo) || InsideBand(o, oAtA, p + right * we, wo) || InsideBand(o, oAtA, p - right * we, wo))
                    last = t + 0.5f;
            }
            return last;
        }

        /// <summary>
        /// Le point est-il sur la bande de la route o (près du nœud, sur ses 90 premiers mètres) ? Un point situé avant
        /// le départ de o (projection sur son extrémité) n'y est pas : c'est le cas d'une route qui la prolonge.
        /// </summary>
        static bool InsideBand(Edge o, bool oAtA, Vector3 q, float width)
        {
            Vector2 p2 = new Vector2(q.x, q.z);
            float best = float.MaxValue, bestAlong = 0f, bestY = 0f;
            int n = o.pts.Length;
            float walked = 0f;
            for (int k = 0; k < n - 1 && walked < 90f; k++)
            {
                int i0 = oAtA ? k : n - 1 - k, i1 = oAtA ? k + 1 : n - 2 - k;
                Vector3 a = o.pts[i0], b = o.pts[i1];
                Vector2 pa = new Vector2(a.x, a.z), ab = new Vector2(b.x, b.z) - pa;
                float len = ab.magnitude;
                if (len < 1e-4f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(p2 - pa, ab) / (len * len));
                float d = (p2 - (pa + ab * t)).sqrMagnitude;
                if (d < best) { best = d; bestAlong = walked + t * len; bestY = Mathf.Lerp(a.y, b.y, t); }
                walked += len;
            }
            if (bestAlong < 0.05f) return false;
            if (Mathf.Abs(bestY - q.y) > 3f) return false;      // passe dessus ou dessous (pont)
            return best < width * width;
        }

        // ------------------------------------------------------------------ requêtes

        /// <summary>Route la plus proche d'un point (recherche jusqu'à maxDistance mètres).</summary>
        public Hit Nearest(Vector3 pos, float maxDistance = 90f)
        {
            var c = BucketOf(pos);
            int rings = Mathf.CeilToInt(maxDistance / BucketSize);
            float best = float.MaxValue;
            int bestEdge = -1, bestIndex = 0;
            Vector2 p2 = new Vector2(pos.x, pos.z);
            for (int ring = 0; ring <= rings; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                    if (!buckets.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list)) continue;
                    foreach (var (ei, i) in list)
                    {
                        Vector3 q = Edges[ei].pts[i];
                        // À plat, mais une route nettement plus haute ou plus basse (lacet voisin, pont) compte moins
                        float d = (p2 - new Vector2(q.x, q.z)).sqrMagnitude + Mathf.Pow(Mathf.Max(0f, Mathf.Abs(q.y - pos.y) - 3f), 2f);
                        if (d < best) { best = d; bestEdge = ei; bestIndex = i; }
                    }
                }
                // Un point trouvé dans cet anneau peut encore être battu par l'anneau suivant, pas au-delà
                if (bestEdge >= 0 && best < Mathf.Pow(ring * BucketSize, 2f)) break;
            }
            var hit = new Hit();
            if (bestEdge < 0) return hit;
            var e = Edges[bestEdge];
            float bestD = float.MaxValue;
            for (int i = Mathf.Max(1, bestIndex); i <= Mathf.Min(e.pts.Length - 1, bestIndex + 1); i++)
            {
                Vector2 pa = new Vector2(e.pts[i - 1].x, e.pts[i - 1].z), pb = new Vector2(e.pts[i].x, e.pts[i].z);
                Vector2 ab = pb - pa;
                float t = Mathf.Clamp01(Vector2.Dot(p2 - pa, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                float d = (p2 - (pa + ab * t)).sqrMagnitude;
                if (d < bestD) { bestD = d; hit.s = Mathf.Lerp(e.dist[i - 1], e.dist[i], t); }
            }
            hit.edge = e;
            hit.point = e.Evaluate(hit.s, out _, out var right);
            hit.lateral = Vector3.Dot(pos - hit.point, right);
            hit.distance = Mathf.Sqrt(bestD);
            return hit;
        }

        /// <summary>État de la surface sous une voiture : sur la route ou non, adhérence, part de terre.</summary>
        public Surface SurfaceAt(Vector3 pos, out Hit hit)
        {
            hit = Nearest(pos, 60f);
            var r = new Surface { grip = 1f };
            if (hit.Found && Mathf.Abs(hit.point.y - pos.y) < 6f)
            {
                var e = hit.edge;
                r.onRoad = hit.distance < e.HalfWidthAt(hit.s, hit.lateral) + 0.5f;
                if (!r.onRoad)
                {
                    // Dans un carrefour : jusqu'au rayon du nœud
                    var n = hit.s < e.Length * 0.5f ? e.a : e.b;
                    Vector3 d = pos - n.pos;
                    d.y = 0f;
                    r.onRoad = d.magnitude < n.radius + 1f;
                }
                if (r.onRoad) r.dirt = e.dirt ? 1f : 0f;
            }
            // Places et cours pavées de la ville : on y roule comme sur la route
            if (!r.onRoad && Map.Paved(pos.x, pos.z) > 0.45f) r.onRoad = true;
            r.grip = Mathf.Lerp(1f, DirtGrip, r.dirt);
            // Chaussée enneigée (station de ski) : la neige tassée glisse, franchement au cœur de la zone
            r.snow = Mathf.InverseLerp(0.35f, 0.75f, Map.Snow(pos.x, pos.z));
            r.grip *= Mathf.Lerp(1f, SnowGrip, r.snow);
            return r;
        }

        static readonly int TexOriginId = Shader.PropertyToID("_NoHesiTexOrigin");

        /// <summary>Origine des motifs des shaders de route et de terrain (précision loin du centre de la carte).</summary>
        public void UpdateOrigin(Vector3 player)
        {
            Shader.SetGlobalVector(TexOriginId, new Vector4(Mathf.Floor(player.x / 960f) * 960f, 0f, Mathf.Floor(player.z / 960f) * 960f, 0f));
        }

        // ------------------------------------------------------------------ maillages

        public class MeshBuilder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Vector4> info = new List<Vector4>();
            /// <summary>Couleurs par sommet (facultatives : utilisées si chaque sommet a la sienne).</summary>
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<int> t = new List<int>();
            /// <summary>
            /// Données du shader de route (UV2) de la section courante (a, b) et de la suivante (c, d) :
            /// autoroute 0-1, chemin 0-1, demi-largeur de chaussée (m), 1 = voies connues.
            /// </summary>
            public Vector4 infoA, infoB;

            public void SetInfo(Vector4 a, Vector4 b) { infoA = a; infoB = b; }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                info.Add(infoA); info.Add(infoA); info.Add(infoB); info.Add(infoB);
                // a-b = section courante (gauche→droite), c-d = section suivante
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
                t.Add(s + 1); t.Add(s + 2); t.Add(s + 3);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Quad(a, b, c, d, Vector2.zero, Vector2.right, Vector2.up, Vector2.one);

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                info.Add(infoA); info.Add(infoA); info.Add(infoA);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }

            /// <summary>Boîte alignée sur des axes donnés (piles de pont, poteaux).</summary>
            public void Box(Vector3 center, Vector3 right, Vector3 up, Vector3 forward)
            {
                Vector3 a = center - right - forward, b = center + right - forward, c = center - right + forward, d = center + right + forward;
                Quad(a + up, b + up, c + up, d + up);
                Quad(b - up, a - up, d - up, c - up);
                Quad(a - up, b - up, a + up, b + up);
                Quad(d - up, c - up, d + up, c + up);
                Quad(c - up, a - up, c + up, a + up);
                Quad(b - up, d - up, b + up, d + up);
            }

            /// <summary>Bande de largeur constante le long d'une polyligne (marquages).</summary>
            public void Strip(IList<Vector3> line, float width)
            {
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vector3 dir = line[i + 1] - line[i];
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    Vector3 ra = Side(line, i), rb = Side(line, i + 1);
                    Quad(line[i] - ra * width * 0.5f, line[i] + ra * width * 0.5f, line[i + 1] - rb * width * 0.5f, line[i + 1] + rb * width * 0.5f);
                }
            }

            static Vector3 Side(IList<Vector3> line, int i)
            {
                Vector3 d = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                d.y = 0f;
                d.Normalize();
                return new Vector3(d.z, 0f, -d.x);
            }

            public Mesh Build(string name, Color? vertexColor = null)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetUVs(0, uv);
                m.SetUVs(1, info);
                if (colors.Count == v.Count && v.Count > 0) m.SetColors(colors);
                else if (vertexColor.HasValue)
                {
                    var colors = new Color[v.Count];
                    for (int i = 0; i < colors.Length; i++) colors[i] = vertexColor.Value;
                    m.SetColors(colors);
                }
                m.SetTriangles(t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        public static GameObject AddMesh(GameObject parent, string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (name == "Markings") mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>Construit tous les maillages, par petites tranches (écran de chargement). progress : 0 → 1.</summary>
        public IEnumerator Build(System.Action<float> progress)
        {
            float done = 0f;
            float lastYield = Time.realtimeSinceStartup;
            foreach (var e in Edges)
            {
                BuildEdge(e);
                done += e.Length;
                if (Time.realtimeSinceStartup - lastYield > 0.03f)
                {
                    progress?.Invoke(done / Mathf.Max(TotalLength, 1f));
                    yield return null;
                    lastYield = Time.realtimeSinceStartup;
                }
            }
            var junctions = new GameObject("Junctions");
            junctions.transform.SetParent(root, false);
            var paved = new MeshBuilder();
            var dirt = new MeshBuilder();
            var ground = new MeshBuilder();
            var lines = new MeshBuilder();
            foreach (var n in Nodes.Values) BuildNode(n, paved, dirt, ground, lines);
            if (paved.v.Count > 0) AddMesh(junctions, "Surface", paved.Build("Junctions"), assets.asphalt, true);
            if (dirt.v.Count > 0) AddMesh(junctions, "DirtSurface", dirt.Build("JunctionsDirt"), assets.dirtRoad ? assets.dirtRoad : assets.dirt, true);
            if (ground.v.Count > 0) AddMesh(junctions, "Shoulder", ground.Build("JunctionShoulder"), assets.terrain, true);
            if (lines.v.Count > 0) AddMesh(junctions, "Markings", lines.Build("JunctionMarks"), assets.roadMarking, false);
            progress?.Invoke(1f);
        }

        struct Section
        {
            public Vector3 p, right;
            public float s, curve;
            public int rail;
            public string span;
            /// <summary>Épaisseur de la coque du tunnel au-dessus du gabarit (Edge.RoofAt).</summary>
            public float roof;
            /// <summary>Largeur de la voie d'insertion à gauche et à droite de l'axe.</summary>
            public float auxL, auxR;
            public float Aux(float side) => side < 0f ? auxL : auxR;
        }

        List<Section> Sections(Edge e)
        {
            var list = new List<Section>();
            float s0 = e.trimA, s1 = e.Length - e.trimB;
            // Abscisses : les points du tracé, plus les extrémités exactes des tunnels (le tube commence et finit
            // précisément là où le relief est percé)
            var stations = new List<(float s, int priority)> { (s0, 2), (s1, 2) };
            for (int i = 0; i < e.pts.Length; i++)
                if (e.dist[i] > s0 + 0.3f && e.dist[i] < s1 - 0.3f) stations.Add((e.dist[i], 0));
            if (e.spans != null)
                foreach (var sp in e.spans)
                {
                    if (sp.kind != "tunnel") continue;
                    if (sp.s0 > s0 + 0.3f && sp.s0 < s1 - 0.3f) stations.Add((sp.s0, 1));
                    if (sp.s1 > s0 + 0.3f && sp.s1 < s1 - 0.3f) stations.Add((sp.s1, 1));
                }
            stations.Sort((p, q) => p.s.CompareTo(q.s));
            var kept = new List<(float s, int priority)>();
            foreach (var st in stations)
            {
                if (kept.Count > 0 && st.s - kept[kept.Count - 1].s < 0.3f)
                {
                    // Deux abscisses trop proches : on garde la plus importante
                    if (st.priority > kept[kept.Count - 1].priority) kept[kept.Count - 1] = st;
                    continue;
                }
                kept.Add(st);
            }
            void Add(float s)
            {
                int i = e.IndexAt(s);
                var sec = new Section { s = s, p = e.Evaluate(s, out var tangent, out var right), right = right, rail = e.rail[i], span = e.SpanAt(s), roof = e.RoofAt(s) };
                if (e.aux != null) { sec.auxL = e.AuxWidth(s, -1); sec.auxR = e.AuxWidth(s, 1); }
                // Courbure (1/m) : pour tracer la ligne d'axe continue dans les virages serrés
                int a = Mathf.Max(i - 3, 0), b = Mathf.Min(i + 3, e.pts.Length - 1);
                float angle = Vector2.Angle(new Vector2(e.tangents[a].x, e.tangents[a].z), new Vector2(e.tangents[b].x, e.tangents[b].z)) * Mathf.Deg2Rad;
                sec.curve = angle / Mathf.Max(e.dist[b] - e.dist[a], 0.1f);
                list.Add(sec);
            }
            foreach (var st in kept) Add(st.s);
            return list;
        }

        static readonly Vector2[] RailProfile = { new Vector2(-0.05f, 0.42f), new Vector2(-0.12f, 0.58f), new Vector2(-0.05f, 0.74f), new Vector2(0.05f, 0.74f), new Vector2(0.05f, 0.42f) };
        static readonly Vector2[] RailProfileMirrored = { new Vector2(-0.05f, 0.42f), new Vector2(-0.05f, 0.74f), new Vector2(0.05f, 0.74f), new Vector2(0.12f, 0.58f), new Vector2(0.05f, 0.42f) };
        // Glissière en béton du terre-plein central (profil décrit de gauche à droite en passant par le haut)
        static readonly Vector2[] JerseyProfile = { new Vector2(-0.35f, 0f), new Vector2(-0.3f, 0.3f), new Vector2(-0.12f, 0.85f), new Vector2(0.12f, 0.85f), new Vector2(0.3f, 0.3f), new Vector2(0.35f, 0f) };
        static readonly Vector2[] ParapetProfile = { new Vector2(-0.18f, -0.2f), new Vector2(-0.18f, 0.95f), new Vector2(0.18f, 0.95f), new Vector2(0.18f, -0.2f) };

        static void Extrude(MeshBuilder mb, Vector3 a, Vector3 ra, Vector3 b, Vector3 rb, float offset, Vector2[] profile)
            => Extrude(mb, a, ra, b, rb, offset, offset, profile);

        /// <summary>Profil extrudé entre deux sections, décalé latéralement de offsetA à la première et offsetB à la seconde.</summary>
        static void Extrude(MeshBuilder mb, Vector3 a, Vector3 ra, Vector3 b, Vector3 rb, float offsetA, float offsetB, Vector2[] profile)
        {
            for (int k = 0; k < profile.Length - 1; k++)
            {
                Vector2 p0 = profile[k], p1 = profile[k + 1];
                Vector3 a0 = a + ra * (offsetA + p0.x) + Vector3.up * p0.y, a1 = a + ra * (offsetA + p1.x) + Vector3.up * p1.y;
                Vector3 b0 = b + rb * (offsetB + p0.x) + Vector3.up * p0.y, b1 = b + rb * (offsetB + p1.x) + Vector3.up * p1.y;
                // Profil décrit en montant côté gauche, puis le dessus, puis en descendant côté droit : faces vers l'extérieur
                mb.Quad(a0, a1, b0, b1, Vector2.zero, Vector2.right, Vector2.up, Vector2.one);
            }
        }

        void BuildEdge(Edge e)
        {
            var sections = Sections(e);
            var parent = new GameObject("Road_" + e.id);
            parent.transform.SetParent(root, false);
            for (int c0 = 0; c0 < sections.Count - 1; c0 += SectionsPerChunk)
                BuildChunk(e, sections, c0, Mathf.Min(c0 + SectionsPerChunk, sections.Count - 1), parent);
        }

        void BuildChunk(Edge e, List<Section> sec, int i0, int i1, GameObject parent)
        {
            var go = new GameObject("Chunk_" + i0);
            go.transform.SetParent(parent.transform, false);
            var road = new MeshBuilder();
            var shoulder = new MeshBuilder();
            var walk = new MeshBuilder();
            var marks = new MeshBuilder();
            var rails = new MeshBuilder();
            var deck = new MeshBuilder();
            var barrier = new MeshBuilder();
            var tube = new MeshBuilder();
            var glow = new MeshBuilder();
            bool highway = e.kind == RoadKind.Highway, ramp = e.kind == RoadKind.Ramp;
            float hw = e.hw, sw = e.sidewalk, tot = hw + sw;
            bool center = e.kind == RoadKind.Main || e.kind == RoadKind.Mountain || e.kind == RoadKind.Street;
            bool edgeLines = e.kind == RoadKind.Main || e.kind == RoadKind.Mountain || ramp;
            // Route large : ligne de rive à 1 m du bord (accotement revêtu) ; sinon au ras de la chaussée
            float edgeLine = e.kind == RoadKind.Main && hw > 4.2f ? hw - 1.0f : hw - 0.3f;
            float dirtness = e.dirt ? 1f : 0f;
            // Bretelle : enrobé d'autoroute uni (pas de voies connues du shader)
            var info = new Vector4(highway || ramp ? 1f : 0f, dirtness, hw, ramp ? 0f : 1f);
            var auxInfo = new Vector4(1f, 0f, hw, 0f);
            road.SetInfo(info, info); walk.SetInfo(info, info); marks.SetInfo(info, info);
            Vector3 up = Vector3.up;
            // Aux abords d'un carrefour, pas de glissière : elle barrerait la vue et les branches voisines
            float railFrom = IsJunction(e.a) ? e.trimA + JunctionClear : -1f;
            float railTo = IsJunction(e.b) ? e.Length - e.trimB - JunctionClear : float.MaxValue;
            // Tunnel : gabarit intérieur (1 m de trottoir de service de chaque côté) et coque extérieure
            float win = tot + 1f, hin = highway ? TunnelHeightHighway : TunnelHeight;
            Vector2[] tubeInside = { new Vector2(win, 0f), new Vector2(win, hin), new Vector2(-win, hin), new Vector2(-win, 0f) };
            Vector2[] tubeOutside = { new Vector2(-win - TunnelShell, -1.5f), new Vector2(-win - TunnelShell, hin + TunnelRoof), new Vector2(win + TunnelShell, hin + TunnelRoof), new Vector2(win + TunnelShell, -1.5f) };
            Vector2[] curbRight = { new Vector2(tot, 0f), new Vector2(tot, 0.12f), new Vector2(win, 0.12f) };
            Vector2[] curbLeft = { new Vector2(-win, 0.12f), new Vector2(-tot, 0.12f), new Vector2(-tot, 0f) };

            for (int i = i0; i < i1; i++)
            {
                var A = sec[i];
                var B = sec[i + 1];
                Vector3 a = A.p, b = B.p, ra = A.right, rb = B.right;
                float sa = A.s, sb = B.s, mid = (sa + sb) * 0.5f;
                bool tunnel = A.span == "tunnel" && B.span == "tunnel";
                bool bridge = !tunnel && A.span != null && B.span != null && A.span != "tunnel" && B.span != "tunnel";
                bool dam = bridge && A.span == "dam";

                road.Quad(a - ra * hw, a + ra * hw, b - rb * hw, b + rb * hw,
                    new Vector2(-hw, sa), new Vector2(hw, sa), new Vector2(-hw, sb), new Vector2(hw, sb));

                foreach (float side in new[] { -1f, 1f })
                {
                    // Voie d'insertion ou de décélération : enrobé uni accolé à la chaussée, qui repousse le bord
                    float wa = A.Aux(side), wb = B.Aux(side);
                    Vector3 ea = a + ra * hw * side, eb = b + rb * hw * side;
                    if (wa > 0.01f || wb > 0.01f)
                    {
                        Vector3 xa = a + ra * (hw + wa) * side, xb = b + rb * (hw + wb) * side;
                        road.SetInfo(auxInfo, auxInfo);
                        if (side > 0) road.Quad(ea, xa, eb, xb, new Vector2(0f, sa), new Vector2(0f, sa), new Vector2(0f, sb), new Vector2(0f, sb));
                        else road.Quad(xa, ea, xb, eb, new Vector2(0f, sa), new Vector2(0f, sa), new Vector2(0f, sb), new Vector2(0f, sb));
                        road.SetInfo(info, info);
                        ea = xa;
                        eb = xb;
                    }
                    Vector3 oa = ea, ob = eb;      // bord extérieur de la plate-forme (trottoir compris)
                    if (sw > 0f)
                    {
                        // Trottoir : bordure et dalle
                        Vector3 ca = ea + up * 0.14f, cb = eb + up * 0.14f;
                        oa = ea + ra * sw * side + up * 0.14f;
                        ob = eb + rb * sw * side + up * 0.14f;
                        if (side > 0)
                        {
                            walk.Quad(ea, ca, eb, cb);
                            walk.Quad(ca, oa, cb, ob);
                        }
                        else
                        {
                            walk.Quad(ca, ea, cb, eb);
                            walk.Quad(oa, ca, ob, cb);
                        }
                    }
                    if (tunnel) continue;          // le tube remplace accotements et glissières
                    if (bridge)
                    {
                        // Tablier : rive et parapet
                        Vector3 da = oa - up * 1.1f, db = ob - up * 1.1f;
                        if (!dam)
                        {
                            if (side > 0) deck.Quad(oa, da, ob, db); else deck.Quad(da, oa, db, ob);
                        }
                        Extrude(deck, a, ra * side, b, rb * side, tot + wa + 0.18f, tot + wb + 0.18f, side > 0 ? ParapetProfile : Mirror(ParapetProfile));
                        continue;
                    }
                    // Côté nu : une autre chaussée y est accolée (bretelle qui rejoint ou quitte l'autoroute)
                    int bareBit = side < 0 ? 16 : 32;
                    if ((A.rail & bareBit) != 0 && (B.rail & bareBit) != 0) continue;
                    // Hors pont : accotement en pente douce, aux couleurs du terrain, qui plonge sous lui (la route se
                    // fond dans le sol, sans fossé ni marche), puis jupe cachée dessous pour les remblais
                    Vector3 ga = oa + ra * ShoulderWidth * side - up * ShoulderDrop, gb = ob + rb * ShoulderWidth * side - up * ShoulderDrop;
                    Vector3 ka = ga + ra * 1.2f * side - up * 2.5f, kb = gb + rb * 1.2f * side - up * 2.5f;
                    if (side > 0)
                    {
                        GroundQuad(shoulder, oa, ga, ob, gb);
                        GroundQuad(shoulder, ga, ka, gb, kb);
                    }
                    else
                    {
                        GroundQuad(shoulder, ga, oa, gb, ob);
                        GroundQuad(shoulder, ka, ga, kb, gb);
                    }
                    // Glissière : partout sur l'autoroute, ailleurs là où le terrain plonge (bit 0 : gauche, bit 1 : droite)
                    int bit = side < 0 ? 1 : 2;
                    bool nearJunction = !highway && (mid < railFrom || mid > railTo);
                    if (sw <= 0f && !nearJunction && (A.rail & bareBit) == 0 && (B.rail & bareBit) == 0 && (highway || (A.rail & bit) != 0 && (B.rail & bit) != 0))
                    {
                        Extrude(rails, a, ra * side, b, rb * side, hw + wa + 0.45f, hw + wb + 0.45f, side > 0 ? RailProfile : Mirror(RailProfile));
                        if (i % 2 == 0)
                        {
                            Vector3 post = a + ra * (hw + wa + 0.52f) * side + up * 0.2f;
                            rails.Box(post, ra * 0.05f, up * 0.5f, Vector3.Cross(ra, up) * 0.05f);
                        }
                    }
                }

                if (bridge && !dam)
                {
                    // Dessous du tablier
                    deck.Quad(a + ra * (tot + A.auxR) - up * 1.1f, a - ra * (tot + A.auxL) - up * 1.1f, b + rb * (tot + B.auxR) - up * 1.1f, b - rb * (tot + B.auxL) - up * 1.1f);
                }

                if (tunnel)
                {
                    // Tube : parois et voûte vues de l'intérieur, trottoirs de service, coque vue de l'extérieur (dalle
                    // mince sous une route en remblai : passage inférieur)
                    tubeOutside[1].y = tubeOutside[2].y = hin + A.roof;
                    Extrude(tube, a, ra, b, rb, 0f, tubeInside);
                    Extrude(tube, a, ra, b, rb, 0f, tubeOutside);
                    Extrude(tube, a, ra, b, rb, 0f, curbRight);
                    Extrude(tube, a, ra, b, rb, 0f, curbLeft);
                    // Têtes de tunnel : une façade à chaque extrémité de la travée (une seule fois, même à cheval sur deux
                    // tronçons) ; une travée qui touche le bout de l'arête se poursuit sur l'arête suivante, sans façade
                    Vector3 along = (b - a).normalized;
                    if (i > 0 && sec[i - 1].span != "tunnel") Portal(tube, a, ra, -along, win, hin);
                    if (i + 2 < sec.Count && sec[i + 2].span != "tunnel") Portal(tube, b, rb, along, win, hin);
                    // Rampes lumineuses au plafond, tous les 12 m, au-dessus de chaque chaussée
                    if (Mathf.Floor(sa / 12f) != Mathf.Floor(sb / 12f))
                    {
                        Vector3 f = new Vector3(along.x, 0f, along.z).normalized;
                        float off = highway ? e.median + e.lanes * LaneWidth * 0.5f : 0f;
                        foreach (float side in highway ? new[] { -1f, 1f } : new[] { 0f })
                        {
                            Vector3 c = b + rb * off * side + up * (hin - 0.06f);
                            glow.Quad(c + rb * 0.3f - f * 1.1f, c - rb * 0.3f - f * 1.1f, c + rb * 0.3f + f * 1.1f, c - rb * 0.3f + f * 1.1f);
                        }
                    }
                }

                // Autoroute : glissière en béton sur le terre-plein, lignes de rive de chaque chaussée, voies en pointillés
                if (highway)
                {
                    Extrude(barrier, a, ra, b, rb, 0f, JerseyProfile);
                    Vector3 lift = up * 0.025f;
                    bool dash = Mathf.Repeat(mid, 13f) < 4f;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float wa = A.Aux(side), wb = B.Aux(side);
                        bool aux = wa > 0.01f || wb > 0.01f;
                        for (int k = 0; k <= e.lanes; k++)
                        {
                            bool solid = k == 0 || k == e.lanes;
                            // Le long d'une voie d'insertion, la ligne de rive devient une ligne de courts tirets
                            if (k == e.lanes && aux) { if (Mathf.Repeat(mid, 6f) >= 3f) continue; }
                            else if (!solid && !dash) continue;
                            float o = (e.median + k * LaneWidth) * side, w = solid ? 0.1f : 0.07f;
                            marks.Quad(a + ra * (o - w) + lift, a + ra * (o + w) + lift, b + rb * (o - w) + lift, b + rb * (o + w) + lift);
                        }
                        // Rive de la voie d'insertion (là où elle est assez large pour en porter une)
                        if (aux && wa > 0.8f && wb > 0.8f)
                        {
                            float oa2 = (hw + wa - 0.3f) * side, ob2 = (hw + wb - 0.3f) * side;
                            marks.Quad(a + ra * (oa2 - 0.1f) + lift, a + ra * (oa2 + 0.1f) + lift, b + rb * (ob2 - 0.1f) + lift, b + rb * (ob2 + 0.1f) + lift);
                        }
                    }
                }
                // Marquages : axe discontinu (continu dans les virages serrés), lignes de rive
                else if (!e.dirt)
                {
                    Vector3 lift = up * 0.025f;
                    bool tight = Mathf.Max(A.curve, B.curve) > 1f / 55f;
                    if (center && (tight || Mathf.Repeat(mid, 12f) < 4.5f))
                        marks.Quad(a - ra * 0.07f + lift, a + ra * 0.07f + lift, b - rb * 0.07f + lift, b + rb * 0.07f + lift);
                    if (edgeLines)
                        foreach (float side in new[] { -1f, 1f })
                            marks.Quad(a + ra * (edgeLine * side - 0.07f) + lift, a + ra * (edgeLine * side + 0.07f) + lift,
                                b + rb * (edgeLine * side - 0.07f) + lift, b + rb * (edgeLine * side + 0.07f) + lift);
                }
            }

            // Piles de pont, espacées d'une vingtaine de mètres
            if (e.spans != null)
                foreach (var sp in e.spans)
                {
                    if (sp.kind == "dam" || sp.kind == "tunnel" || sp.s1 - sp.s0 < 26f) continue;
                    // un viaduc d'autoroute a des piles plus espacées
                    int count = Mathf.Max(1, Mathf.RoundToInt((sp.s1 - sp.s0) / (highway ? 44f : 22f)) - 1);
                    for (int k = 1; k <= count; k++)
                    {
                        float s = Mathf.Lerp(sp.s0, sp.s1, k / (count + 1f));
                        if (s < sec[i0].s || s >= sec[i1].s) continue;
                        Vector3 p = e.Evaluate(s, out var tangent, out var right);
                        float ground = Map.Height(p.x, p.z) - 2f;
                        float top = p.y - 1.1f;
                        if (top - ground < 1.5f) continue;
                        // pas de pile sur une route qui passe dessous
                        if (RoadBelow(p, e, tot + 5f)) continue;
                        Vector3 fwd = new Vector3(tangent.x, 0f, tangent.z).normalized;
                        deck.Box(new Vector3(p.x, (top + ground) * 0.5f, p.z), right * (tot * (highway ? 0.55f : 0.72f)), up * ((top - ground) * 0.5f), fwd * (highway ? 1.4f : 0.9f));
                    }
                }

            var surface = e.dirt ? (assets.dirtRoad ? assets.dirtRoad : assets.dirt) : assets.asphalt;
            if (road.v.Count > 0) AddMesh(go, e.dirt ? "DirtSurface" : "Surface", road.Build("Road"), surface, true);
            if (shoulder.v.Count > 0) AddMesh(go, "Shoulder", shoulder.Build("Shoulder"), assets.terrain, true);
            if (walk.v.Count > 0) AddMesh(go, "Sidewalk", walk.Build("Sidewalk"), assets.concrete, true);
            if (marks.v.Count > 0) AddMesh(go, "Markings", marks.Build("Marks"), assets.roadMarking, false);
            if (rails.v.Count > 0) AddMesh(go, "Rails", rails.Build("Rails"), assets.metal, true);
            if (barrier.v.Count > 0) AddMesh(go, "Barrier", barrier.Build("Barrier"), assets.concrete, true);
            if (deck.v.Count > 0) AddMesh(go, "Deck", deck.Build("Deck"), assets.concrete, true);
            if (tube.v.Count > 0) AddMesh(go, "Tunnel", tube.Build("Tunnel"), assets.concrete, true);
            if (glow.v.Count > 0)
                AddMesh(go, "TunnelLights", glow.Build("TunnelLights"), tunnelGlow, false).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Accotement : largeur et dénivelé de la pente douce qui raccorde la chaussée au terrain (0,3 m sous la route)
        const float ShoulderWidth = 1.6f, ShoulderDrop = 0.48f;
        /// <summary>Tunnels : hauteur libre (autoroute, autres routes) et épaisseur de la coque autour du gabarit.
        /// Le relief est percé d'après ces mêmes cotes par le générateur de carte.</summary>
        public const float TunnelHeightHighway = 6.4f, TunnelHeight = 5f, TunnelShell = 6f;
        /// <summary>Hauteur de la coque au-dessus du gabarit : aux têtes, elle dépasse le bord du relief percé (qui peut
        /// monter d'une maille au-dessus de la voûte) ; ailleurs elle est enterrée.</summary>
        public const float TunnelRoof = 6.5f;

        /// <summary>
        /// Face d'accotement aux couleurs du terrain : chaque sommet reçoit les masques et annexes du sol à sa
        /// position (ce que portent les tuiles du terrain), pour que le shader du terrain y dessine la même chose.
        /// </summary>
        void GroundQuad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int s = mb.v.Count;
            mb.Quad(a, b, c, d);
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = mb.v[s + k];
                Map.Ground(p.x, p.z, out var masks, out var extra);
                mb.colors.Add(masks);
                mb.info[s + k] = new Vector4(extra.x, extra.y, 0f, 0f);
            }
        }

        /// <summary>
        /// Tête de tunnel : mur de façade dans le plan de la section, percé du gabarit (visible des deux côtés), et
        /// radier de béton devant l'entrée (outward : direction horizontale vers l'extérieur du tunnel, pente comprise).
        /// </summary>
        void Portal(MeshBuilder mb, Vector3 p, Vector3 right, Vector3 outward, float win, float hin)
        {
            float wo = win + TunnelShell + 2f, bottom = -3f;
            Vector3 inward = -new Vector3(outward.x, 0f, outward.z).normalized;
            // Le haut du mur suit le terrain qui se trouve derrière lui (là où le relief est percé, c'est la hauteur
            // qu'il aurait) : vu de la route, aucun jour entre la façade et le versant
            float Top(float x)
            {
                float h = hin + 1.5f;
                for (float d = 0f; d <= 24f; d += 2f)
                {
                    Vector3 q = p + right * x + inward * d;
                    h = Mathf.Max(h, Map.Height(q.x, q.z) - p.y + 0.8f);
                }
                return Mathf.Min(h, hin + 18f);
            }
            var xs = new List<float>();
            for (float x = -wo; x < -win - 0.5f; x += 3f) xs.Add(x);
            xs.Add(-win);
            for (float x = -win + 3f; x < win - 0.5f; x += 3f) xs.Add(x);
            xs.Add(win);
            for (float x = win + 3f; x < wo - 0.5f; x += 3f) xs.Add(x);
            xs.Add(wo);
            for (int k = 0; k + 1 < xs.Count; k++)
            {
                float x0 = xs[k], x1 = xs[k + 1];
                bool opening = x0 > -win - 0.01f && x1 < win + 0.01f;
                float y0 = opening ? hin : bottom;
                Vector3 a = p + right * x0 + Vector3.up * y0, b = p + right * x1 + Vector3.up * y0;
                Vector3 c = p + right * x0 + Vector3.up * Top(x0), d = p + right * x1 + Vector3.up * Top(x1);
                mb.Quad(a, b, c, d);
                mb.Quad(b, a, d, c);
            }
            // Sous la chaussée
            Vector3 u0 = p - right * win + Vector3.up * bottom, u1 = p + right * win + Vector3.up * bottom;
            Vector3 u2 = p - right * win - Vector3.up * 0.02f, u3 = p + right * win - Vector3.up * 0.02f;
            mb.Quad(u0, u1, u2, u3);
            mb.Quad(u1, u0, u3, u2);
            // Radier : dalle juste sous la chaussée, devant la façade (cache le bord dentelé du relief percé)
            Vector3 r = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 n0 = p - Vector3.up * 0.2f, n1 = n0 + outward * 6f;
            float wr = win + TunnelShell;
            mb.Quad(n0 - r * wr, n0 + r * wr, n1 - r * wr, n1 + r * wr);
        }

        /// <summary>Une autre route passe-t-elle sous ce point (à moins de « radius » mètres à plat, au moins 3 m plus bas) ?</summary>
        bool RoadBelow(Vector3 p, Edge self, float radius)
        {
            var c = BucketOf(p);
            int rings = Mathf.CeilToInt(radius / BucketSize);
            for (int dx = -rings; dx <= rings; dx++)
            for (int dz = -rings; dz <= rings; dz++)
            {
                if (!buckets.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list)) continue;
                foreach (var (ei, i) in list)
                {
                    var o = Edges[ei];
                    if (o == self) continue;
                    Vector3 q = o.pts[i];
                    float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                    if (d < radius + o.TotalHalfWidth && q.y < p.y - 3f) return true;
                }
            }
            return false;
        }

        static Vector2[] Mirror(Vector2[] profile)
        {
            // Même profil vu de l'autre côté : l'ordre est inversé pour garder les faces vers l'extérieur
            var m = new Vector2[profile.Length];
            for (int i = 0; i < profile.Length; i++) m[i] = profile[profile.Length - 1 - i];
            return m;
        }

        /// <summary>Distance sans glissière de part et d'autre d'un carrefour (m).</summary>
        const float JunctionClear = 18f;
        /// <summary>Recul supplémentaire des chaussées à un carrefour : rayon des angles arrondis (m).</summary>
        const float JunctionFlare = 3.5f;

        static bool IsJunction(Node n) => !n.merge && n.edges.Count >= 3;

        /// <summary>Lignes de rive sur ce type de route ?</summary>
        static bool EdgeLined(Edge e) => e.kind == RoadKind.Main || e.kind == RoadKind.Mountain || e.kind == RoadKind.Ramp;

        struct Arm
        {
            public Edge e;
            public bool atA;
            /// <summary>Bout de la chaussée, direction horizontale en s'éloignant du nœud, coins gauche et droit vus du nœud.</summary>
            public Vector3 p, dir, left, right;
            public float angle, w;
        }

        /// <summary>
        /// Carrefour : polygone entre les bouts des chaussées, aux angles arrondis (la rive de chaque route se prolonge
        /// en courbe jusqu'à la rive de la voisine), bordé du même accotement en herbe que les routes ; lignes de rive
        /// le long des arrondis, cédez-le-passage sur les branches secondaires. Bout de route : aire de demi-tour.
        /// </summary>
        void BuildNode(Node n, MeshBuilder paved, MeshBuilder dirtMesh, MeshBuilder ground, MeshBuilder marks)
        {
            if (n.edges.Count == 0 || n.merge) return;
            bool allDirt = true;
            foreach (var e in n.edges) allDirt &= e.dirt;
            var mb = allDirt ? dirtMesh : paved;
            var info = new Vector4(0f, allDirt ? 1f : 0f, 30f, 0f);
            mb.SetInfo(info, info);
            var ring = new List<Vector3>();
            // nature du côté ring[k] → ring[k + 1] : 0 = bout de chaussée, 1 = bord avec ligne de rive, 2 = bord nu
            var side = new List<int>();
            Vector3 c = n.pos;
            if (n.edges.Count == 1)
            {
                // Aire de demi-tour au bout de la route : la route s'arrête sur le cercle, l'aire prend le relais sans
                // la recouvrir (arc de cercle hors du secteur de la route, plus les deux coins de la chaussée)
                var e = n.edges[0];
                float r = DeadEndRadius(e);
                bool atA = e.a == n;
                Vector3 end = e.Evaluate(atA ? e.trimA : e.Length - e.trimB, out _, out var right);
                float w = e.TotalHalfWidth;
                Vector3 l = end - right * w, rr = end + right * w;
                ring.Add(l);
                ring.Add(rr);
                Vector3 outward = Outward(n, e);
                float sector = Mathf.Asin(Mathf.Clamp01(w / r)) * Mathf.Rad2Deg + 4f;
                for (int k = 0; k < 24; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    if (Vector3.Angle(dir, outward) < sector) continue;
                    ring.Add(c + dir * r);
                }
                ring.Sort((p, q) => Mathf.Atan2(p.z - c.z, p.x - c.x).CompareTo(Mathf.Atan2(q.z - c.z, q.x - c.x)));
                for (int k = 0; k < ring.Count; k++)
                {
                    Vector3 p = ring[k], q = ring[(k + 1) % ring.Count];
                    side.Add(p == l && q == rr || p == rr && q == l ? 0 : 2);
                }
            }
            else
            {
                if (n.radius <= 0f) return;
                var arms = new List<Arm>();
                foreach (var e in n.edges)
                    foreach (bool atA in new[] { true, false })
                    {
                        if ((atA ? e.a : e.b) != n) continue;
                        float s = atA ? e.trimA : e.Length - e.trimB;
                        Vector3 p = e.Evaluate(s, out var tangent, out var right);
                        Vector3 dir = new Vector3(tangent.x, 0f, tangent.z).normalized * (atA ? 1f : -1f);
                        Vector3 r = right * (atA ? 1f : -1f);
                        float w = e.TotalHalfWidth;
                        arms.Add(new Arm { e = e, atA = atA, p = p, dir = dir, w = w, left = p - r * w, right = p + r * w, angle = Mathf.Atan2(dir.z, dir.x) });
                    }
                arms.Sort((x, y) => x.angle.CompareTo(y.angle));
                for (int i = 0; i < arms.Count; i++)
                {
                    Arm a = arms[i], b = arms[(i + 1) % arms.Count];
                    // bout de la chaussée (de son coin droit à son coin gauche, sens trigonométrique)
                    ring.Add(a.right);
                    side.Add(0);
                    ring.Add(a.left);
                    int lined = !allDirt && EdgeLined(a.e) && EdgeLined(b.e) ? 1 : 2;
                    // arrondi jusqu'au coin droit de la branche suivante : courbe tendue vers le point où les deux
                    // rives se croiseraient
                    Vector3 p0 = a.left, p2 = b.right;
                    float cross = a.dir.x * b.dir.z - a.dir.z * b.dir.x;
                    if (Mathf.Abs(cross) > 0.25f)
                    {
                        Vector3 d = p2 - p0;
                        float t = (d.x * b.dir.z - d.z * b.dir.x) / cross;
                        Vector3 x = p0 + a.dir * t;
                        x.y = (p0.y + p2.y) * 0.5f;
                        Vector3 flat = x - c;
                        flat.y = 0f;
                        if (t < 0f && flat.magnitude < n.radius + 12f)
                            for (int k = 1; k <= 4; k++)
                            {
                                float u = k / 5f;
                                side.Add(lined);
                                ring.Add((1f - u) * (1f - u) * p0 + 2f * u * (1f - u) * x + u * u * p2);
                            }
                    }
                    side.Add(lined);
                }
                foreach (var a in arms) GiveWay(n, a, arms, marks);
            }
            for (int k = 0; k < ring.Count; k++)
            {
                Vector3 p = ring[k], q = ring[(k + 1) % ring.Count];
                // Angle croissant = sens trigonométrique vu du dessus : (c, q, p) regarde vers le haut
                // UV : décalage latéral constant (le shader voyait les coordonnées du monde comme un point hors chaussée,
                // donc en herbe) : milieu de chaussée pour le goudron, entre les ornières pour la terre
                float lat = allDirt ? DirtLaneOffset + 0.4f : 0f;
                mb.Tri(c, q, p, new Vector2(lat, c.x + c.z), new Vector2(lat, q.x + q.z), new Vector2(lat, p.x + p.z));
                if (side[k] == 0) continue;
                // Accotement : même pente douce aux couleurs du terrain que le long des routes, puis jupe cachée
                Vector3 np = Radial(c, p), nq = Radial(c, q);
                Vector3 gp = p + np * ShoulderWidth - Vector3.up * ShoulderDrop, gq = q + nq * ShoulderWidth - Vector3.up * ShoulderDrop;
                GroundQuad(ground, p, gp, q, gq);
                GroundQuad(ground, gp, gp + np * 1.2f - Vector3.up * 2.5f, gq, gq + nq * 1.2f - Vector3.up * 2.5f);
                if (side[k] != 1) continue;
                Vector3 lift = Vector3.up * 0.025f;
                marks.Quad(p - np * 0.37f + lift, p - np * 0.23f + lift, q - nq * 0.37f + lift, q - nq * 0.23f + lift);
            }
        }

        static Vector3 Radial(Vector3 c, Vector3 p)
        {
            Vector3 d = p - c;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>
        /// Cédez-le-passage : gros tirets en travers de la voie qui arrive au carrefour, sur les branches qui ne sont
        /// pas la route principale (celle dont les deux branches se prolongent le mieux l'une l'autre).
        /// </summary>
        static void GiveWay(Node n, Arm arm, List<Arm> arms, MeshBuilder marks)
        {
            if (arm.e.dirt || arm.e.kind == RoadKind.Highway) return;
            if (arm.e.oneway && arm.e.a == n) return;                 // bretelle qui part du carrefour
            // la route principale : la paire de branches la plus alignée, à largeur égale la plus large
            float best = float.MaxValue;
            int m0 = -1, m1 = -1;
            for (int i = 0; i < arms.Count; i++)
                for (int j = i + 1; j < arms.Count; j++)
                {
                    float score = Vector3.Dot(arms[i].dir, arms[j].dir) - 0.05f * (arms[i].w + arms[j].w);
                    if (score < best) { best = score; m0 = i; m1 = j; }
                }
            if (m0 < 0 || Vector3.Dot(arms[m0].dir, arms[m1].dir) > -0.7f) return;      // pas de route qui traverse
            if (arms[m0].e == arm.e && arms[m0].atA == arm.atA || arms[m1].e == arm.e && arms[m1].atA == arm.atA) return;
            // vue du nœud, la voie qui arrive est à gauche (on roule à droite) ; une bretelle l'occupe en entier
            Vector3 l = (arm.left - arm.p).normalized;
            float from = arm.e.oneway ? -(arm.w - 0.3f) : 0.15f, to = arm.w - 0.3f;
            Vector3 o = arm.p + arm.dir * 0.7f + Vector3.up * 0.025f;
            for (float x = from; x + 0.5f <= to; x += 0.85f)
                marks.Quad(o + l * x, o + l * x + arm.dir * 0.4f, o + l * (x + 0.5f), o + l * (x + 0.5f) + arm.dir * 0.4f);
        }

        /// <summary>Enregistre un lampadaire posé par le décor : ses têtes s'allument la nuit.</summary>
        public void RegisterLamp(Transform lamp, Vector3[] heads)
        {
            Lamps.Add(lamp);
            foreach (var h in heads)
            {
                var head = new GameObject("LampHead").transform;
                head.SetParent(lamp, false);
                head.localPosition = h;
                LampHeads.Add(head);
            }
        }
    }
}
