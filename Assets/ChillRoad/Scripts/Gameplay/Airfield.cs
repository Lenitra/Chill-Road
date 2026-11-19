using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Aéroport des Trois Vallons (fiche « airfield » de la carte, Tools/Map/map_airport.py) : la carte fournit la
    /// plate-forme aplanie, le tarmac et le parking en béton, les bâtiments, les avions et le balisage ; ce composant
    /// pose l'enrobé de la piste, de la voie de circulation et de ses bretelles (maillage avec collision, hors du
    /// réseau routier : pas de circulation, pas de GPS) et peint les marquages : rives, seuils, numéros, axe et points
    /// de visée de la piste, axes jaunes et points d'arrêt des voies de circulation, postes de stationnement,
    /// hélisurface, places du parking.
    /// Repère local : u le long de la piste, v en travers (à gauche de u), origine au milieu de la piste, au sol.
    /// </summary>
    public class Airfield : MonoBehaviour
    {
        /// <summary>Dessus de l'enrobé au-dessus du sol aplani, peinture au-dessus de son support (m).</summary>
        const float Lift = 0.06f, Paint = 0.03f;
        /// <summary>Jupe de l'enrobé : débord et profondeur sous le sol (le chant ne se voit jamais flotter).</summary>
        const float SkirtOut = 0.3f, SkirtDown = 0.12f;
        const float Fillet = 10f, TurnRadius = 14f;

        static Airfield current;
        GameManager game;
        GameObject world;
        Material yellowPaint;
        bool built;
        MapData.AirfieldJson a;
        Vector3 origin, U, V;
        /// <summary>Bandes d'enrobé (u0, v0, u1, v1) : on y roule comme sur la route.</summary>
        readonly List<Vector4> strips = new List<Vector4>();
        /// <summary>Aires en béton (tarmac, parking, passage du portail), même format.</summary>
        readonly List<Vector4> aprons = new List<Vector4>();

        /// <summary>Feux du balisage d'une couleur : un losange lumineux par feu, tourné vers la caméra.</summary>
        class Beads
        {
            public readonly List<Vector3> at = new List<Vector3>();
            public Color color;
            public Mesh mesh;
            public Material material;
            public MeshRenderer renderer;
            public Vector3[] verts;
        }
        readonly List<Beads> beads = new List<Beads>();

        public void Init(GameManager g)
        {
            game = g;
            current = this;
        }

        /// <summary>La position est-elle sur l'enrobé de l'aéroport (piste, voie de circulation, bretelles) ?</summary>
        public static bool OnAsphalt(Vector3 pos)
        {
            var f = current;
            if (!f || !f.built || f.strips.Count == 0) return false;
            Vector3 d = pos - f.origin;
            if (Mathf.Abs(d.y) > 4f) return false;
            float u = Vector3.Dot(d, f.U), v = Vector3.Dot(d, f.V);
            foreach (var r in f.strips)
                if (u >= r.x && u <= r.z && v >= r.y && v <= r.w) return true;
            return false;
        }

        /// <summary>
        /// La position est-elle sur une surface roulante de l'aéroport (enrobé, tarmac, parking) ? Le bouton de
        /// replacement y redresse la voiture sur place : la route la plus proche est à des centaines de mètres.
        /// </summary>
        public static bool Inside(Vector3 pos)
        {
            if (OnAsphalt(pos)) return true;
            var f = current;
            if (!f || !f.built) return false;
            Vector3 d = pos - f.origin;
            if (Mathf.Abs(d.y) > 4f) return false;
            float u = Vector3.Dot(d, f.U), v = Vector3.Dot(d, f.V);
            foreach (var r in f.aprons)
                if (u >= r.x && u <= r.z && v >= r.y && v <= r.w) return true;
            return false;
        }

        void Update()
        {
            if (built || !game || !game.Player || game.Map == null || game.Map.Json == null || !game.assets) return;
            built = true;
            a = game.Map.Json.airfield;
            if (a == null || a.half <= 0f || !game.assets.asphalt || !game.assets.roadMarking) return;
            Build();
        }

        // ------------------------------------------------------------------ géométrie

        Vector3 P(float u, float v, float h) => origin + U * u + V * v + Vector3.up * h;

        /// <summary>
        /// Dalle d'enrobé (u0 &lt; u1, v0 &lt; v1) : dessus découpé en tronçons, jupe inclinée sur les quatre côtés.
        /// lateral : les UV portent l'écart à l'axe (piste : usure des bords comme une route), sinon l'axe partout.
        /// </summary>
        void Slab(RoadNetwork.MeshBuilder m, float u0, float v0, float u1, float v1, bool lateral)
        {
            strips.Add(new Vector4(u0, v0, u1, v1));
            int n = Mathf.Max(1, Mathf.CeilToInt((u1 - u0) / 44f));
            for (int i = 0; i < n; i++)
            {
                float s0 = Mathf.Lerp(u0, u1, i / (float)n), s1 = Mathf.Lerp(u0, u1, (i + 1) / (float)n);
                float l0 = lateral ? v0 : 0f, l1 = lateral ? v1 : 0f;
                m.Quad(P(s0, v1, Lift), P(s0, v0, Lift), P(s1, v1, Lift), P(s1, v0, Lift),
                    new Vector2(l1, s0), new Vector2(l0, s0), new Vector2(l1, s1), new Vector2(l0, s1));
            }
            Skirt(m, new Vector2(u0, v0), new Vector2(u1, v0));
            Skirt(m, new Vector2(u1, v0), new Vector2(u1, v1));
            Skirt(m, new Vector2(u1, v1), new Vector2(u0, v1));
            Skirt(m, new Vector2(u0, v1), new Vector2(u0, v0));
        }

        /// <summary>Jupe le long du bord p → q (contour parcouru dans le sens trigonométrique : l'extérieur est à droite).</summary>
        void Skirt(RoadNetwork.MeshBuilder m, Vector2 p, Vector2 q)
        {
            Vector2 d = (q - p).normalized, o = new Vector2(d.y, -d.x) * SkirtOut;
            Vector3 facing = U * o.x + V * o.y + Vector3.up * 0.5f;
            m.QuadFacing(P(p.x, p.y, Lift), P(q.x, q.y, Lift), P(p.x + o.x, p.y + o.y, -SkirtDown), P(q.x + o.x, q.y + o.y, -SkirtDown), facing);
        }

        /// <summary>Congé à 45° dans l'angle rentrant c, le long des directions du, dv (unitaires, en repère local).</summary>
        void Corner(RoadNetwork.MeshBuilder m, Vector2 c, Vector2 du, Vector2 dv)
        {
            Vector2 p = c + du * Fillet, q = c + dv * Fillet;
            m.TriFacing(P(c.x, c.y, Lift), P(p.x, p.y, Lift), P(q.x, q.y, Lift), Vector3.up);
            // bord libre : l'hypoténuse, extérieur du côté opposé à l'angle
            Vector2 mid = (p + q) * 0.5f - c;
            Vector2 d = (q - p).normalized, o = new Vector2(d.y, -d.x);
            if (Vector2.Dot(o, mid) < 0f) Skirt(m, q, p); else Skirt(m, p, q);
        }

        /// <summary>Rectangle peint (repère local), à la hauteur h au-dessus du sol.</summary>
        void Mark(RoadNetwork.MeshBuilder m, float u0, float v0, float u1, float v1, float h)
        {
            m.QuadFacing(P(u0, v0, h), P(u1, v0, h), P(u0, v1, h), P(u1, v1, h), Vector3.up);
        }

        /// <summary>Trait peint de p à q (repère local), de largeur w.</summary>
        void Line(RoadNetwork.MeshBuilder m, Vector2 p, Vector2 q, float w, float h)
        {
            Vector2 d = (q - p).normalized, n = new Vector2(-d.y, d.x) * (w * 0.5f);
            m.QuadFacing(P(p.x - n.x, p.y - n.y, h), P(p.x + n.x, p.y + n.y, h), P(q.x - n.x, q.y - n.y, h), P(q.x + n.x, q.y + n.y, h), Vector3.up);
        }

        /// <summary>Trait courbe : polyligne aux jointures communes (pas d'entailles à l'extérieur des virages).</summary>
        void Curve(RoadNetwork.MeshBuilder m, List<Vector2> pts, float w, float h)
        {
            var left = new Vector3[pts.Count];
            var right = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 d = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f);
                left[i] = P(pts[i].x + n.x, pts[i].y + n.y, h);
                right[i] = P(pts[i].x - n.x, pts[i].y - n.y, h);
            }
            for (int i = 0; i + 1 < pts.Count; i++) m.QuadFacing(left[i], right[i], left[i + 1], right[i + 1], Vector3.up);
        }

        // Segments d'un chiffre (afficheur à 7 segments : a haut, b et c à droite, d bas, e et f à gauche, g milieu)
        static readonly string[] Digits = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgecd", "abc", "abcdefg", "abcdfg" };

        /// <summary>
        /// Numéro de piste : chiffres en bâtons de 9 m, lus par qui roule dans le sens dir (±1 le long de u) ; le pied
        /// des chiffres est à l'abscisse uFoot.
        /// </summary>
        void Number(RoadNetwork.MeshBuilder m, string text, float uFoot, float dir, float h)
        {
            const float W = 4f, H = 9f, T = 0.9f, Gap = 2.4f;
            if (string.IsNullOrEmpty(text)) return;
            float total = text.Length * W + (text.Length - 1) * Gap;
            for (int k = 0; k < text.Length; k++)
            {
                int digit = text[k] - '0';
                if (digit < 0 || digit > 9) continue;
                float cx = -total * 0.5f + W * 0.5f + k * (W + Gap);
                // (x vers la droite du lecteur, y vers le haut du chiffre) → (u, v) : la droite du lecteur est -v · dir
                void Seg(float x0, float y0, float x1, float y1)
                {
                    float ua = uFoot + dir * y0, ub = uFoot + dir * y1, va = -dir * (cx + x0), vb = -dir * (cx + x1);
                    Mark(m, Mathf.Min(ua, ub), Mathf.Min(va, vb), Mathf.Max(ua, ub), Mathf.Max(va, vb), h);
                }
                if (digit == 1) { Seg(-T * 0.5f, 0f, T * 0.5f, H); continue; }      // un simple bâton, centré
                foreach (char s in Digits[digit])
                    switch (s)
                    {
                        case 'a': Seg(-W / 2, H - T, W / 2, H); break;
                        case 'd': Seg(-W / 2, 0f, W / 2, T); break;
                        case 'g': Seg(-W / 2 + T, (H - T) / 2, W / 2 - T, (H + T) / 2); break;
                        case 'b': Seg(W / 2 - T, H / 2, W / 2, H - T); break;
                        case 'c': Seg(W / 2 - T, T, W / 2, H / 2); break;
                        case 'f': Seg(-W / 2, H / 2, -W / 2 + T, H - T); break;
                        case 'e': Seg(-W / 2, T, -W / 2 + T, H / 2); break;
                    }
            }
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            origin = new Vector3(a.x, a.y, a.z);
            U = new Vector3(a.ax, 0f, a.az).normalized;
            V = new Vector3(-U.z, 0f, U.x);
            world = new GameObject("Airfield");
            float half = a.half, hw = a.hw, tv = a.taxiV, thw = a.taxiHw, th = a.taxiHalf;

            // --- Enrobé : piste sombre (comme l'autoroute), voies de circulation plus claires
            var runway = new RoadNetwork.MeshBuilder();
            var taxi = new RoadNetwork.MeshBuilder();
            var dark = new Vector4(1f, 0f, hw, 0f);
            var pale = new Vector4(0.35f, 0f, 30f, 0f);
            runway.SetInfo(dark, dark);
            taxi.SetInfo(pale, pale);
            Slab(runway, -half, -hw, half, hw, true);
            Slab(taxi, -th - thw, tv - thw, th + thw, tv + thw, false);
            Vector2 east = Vector2.right, north = Vector2.up;
            if (a.links != null)
                foreach (float k in a.links)
                {
                    Slab(taxi, k - thw, tv + thw, k + thw, -hw, false);
                    // congés aux quatre angles de la bretelle, là où la bande voisine continue
                    foreach (float s in new[] { -1f, 1f })
                    {
                        float corner = k + s * thw;
                        if (Mathf.Abs(corner + s * Fillet) <= th + thw) Corner(taxi, new Vector2(corner, tv + thw), east * s, north);
                        if (Mathf.Abs(corner + s * Fillet) <= half) Corner(taxi, new Vector2(corner, -hw), east * s, -north);
                    }
                }

            // --- Peinture
            var white = new RoadNetwork.MeshBuilder();
            var yellow = new RoadNetwork.MeshBuilder();
            var clean = new Vector4(1f, 0f, hw, 0f);
            white.SetInfo(clean, clean);
            yellow.SetInfo(clean, clean);
            float top = Lift + Paint, ground = Paint + 0.015f;

            // Piste : rives, seuils (touches de piano), numéros, points de visée, axe en tirets
            foreach (float s in new[] { -1f, 1f }) Mark(white, -half + 1f, s * (hw - 0.95f) - 0.25f, half - 1f, s * (hw - 0.95f) + 0.25f, top);
            foreach (float s in new[] { -1f, 1f })
            {
                float end = s * half;
                for (int i = 0; i < 4; i++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float vc = side * (2.25f + 3f * i);
                        Mark(white, Mathf.Min(end - s * 6f, end - s * 30f), vc - 0.75f, Mathf.Max(end - s * 6f, end - s * 30f), vc + 0.75f, top);
                    }
                Number(white, s < 0f ? a.west : a.east, end - s * 38f, -s, top);
                foreach (float side in new[] { -1f, 1f })
                    Mark(white, Mathf.Min(end - s * 150f, end - s * 186f), side * 6f - 2.2f, Mathf.Max(end - s * 150f, end - s * 186f), side * 6f + 2.2f, top);
            }
            for (float u = -half + 62f; u + 30f <= half - 62f; u += 50f) Mark(white, u, -0.3f, u + 30f, 0.3f, top);

            // Voies de circulation : axe jaune, virages vers chaque bretelle, point d'arrêt avant la piste
            const float yw = 0.3f;
            Mark(yellow, -th, tv - yw * 0.5f, th, tv + yw * 0.5f, top);
            if (a.links != null)
                foreach (float k in a.links)
                {
                    float r = TurnRadius;
                    foreach (float dir in new[] { -1f, 1f })
                    {
                        if (Mathf.Abs(k + dir * r) > th) continue;
                        var arc = new List<Vector2>();
                        for (int i = 0; i <= 10; i++)
                        {
                            float t = i / 10f * Mathf.PI * 0.5f;
                            arc.Add(new Vector2(k + dir * r * (1f - Mathf.Sin(t)), tv + r * (1f - Mathf.Cos(t))));
                        }
                        Curve(yellow, arc, yw, top);
                    }
                    Mark(yellow, k - yw * 0.5f, tv + r, k + yw * 0.5f, -hw - 0.6f, top);
                    // point d'arrêt : deux lignes continues côté voie, deux lignes en tirets côté piste
                    float hold = -hw - 12f, x0 = k - thw + 0.8f, x1 = k + thw - 0.8f;
                    Mark(yellow, x0, hold - 1.05f, x1, hold - 0.75f, top);
                    Mark(yellow, x0, hold - 0.45f, x1, hold - 0.15f, top);
                    for (float x = x0; x + 0.9f <= x1; x += 1.8f)
                    {
                        Mark(yellow, x, hold + 0.15f, x + 0.9f, hold + 0.45f, top);
                        Mark(yellow, x, hold + 0.75f, x + 0.9f, hold + 1.05f, top);
                    }
                }

            // Tarmac (béton) : ligne de roulage, té de chaque poste de stationnement
            if (a.apron != null && a.apron.Length >= 4)
            {
                float lane = tv - thw - 14f;
                Mark(yellow, a.apron[0] + 8f, lane - yw * 0.5f, a.apron[2] - 6f, lane + yw * 0.5f, ground);
            }
            if (a.stands != null)
                for (int i = 0; i + 4 < a.stands.Length; i += 5)
                {
                    Vector2 c = new Vector2(a.stands[i], a.stands[i + 1]);
                    Vector2 d = new Vector2(a.stands[i + 2], a.stands[i + 3]).normalized, n = new Vector2(-d.y, d.x);
                    float len = a.stands[i + 4];
                    Line(yellow, c - d * len, c + d * len * 0.62f, yw, ground);
                    Line(yellow, c + d * len * 0.62f - n * 1.6f, c + d * len * 0.62f + n * 1.6f, yw, ground);
                }

            // Hélisurface : cercle blanc et H
            if (a.heli != null && a.heli.Length >= 3)
            {
                Vector2 c = new Vector2(a.heli[0], a.heli[1]);
                float r = a.heli[2];
                var ring = new List<Vector2>();
                for (int i = 0; i <= 48; i++)
                {
                    float t = i / 48f * Mathf.PI * 2f;
                    ring.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * (r - 0.3f));
                }
                Curve(white, ring, 0.6f, ground);
                foreach (float s in new[] { -1f, 1f }) Mark(white, c.x - 3.2f, c.y + s * 2.1f - 0.45f, c.x + 3.2f, c.y + s * 2.1f + 0.45f, ground);
                Mark(white, c.x - 0.45f, c.y - 1.65f, c.x + 0.45f, c.y + 1.65f, ground);
            }

            // Parking : rangées de places données par la carte (ligne de fond, places ouvertes à gauche de son sens),
            // centrées sur la longueur de la rangée
            if (a.bays != null)
            {
                const float bay = 2.6f, lw = 0.14f;
                for (int i = 0; i + 4 < a.bays.Length; i += 5)
                {
                    Vector2 p = new Vector2(a.bays[i], a.bays[i + 1]), q = new Vector2(a.bays[i + 2], a.bays[i + 3]);
                    float length = (q - p).magnitude, depth = a.bays[i + 4];
                    int count = Mathf.FloorToInt(length / bay);
                    if (count < 1) continue;
                    Vector2 d = (q - p) / length, n = new Vector2(-d.y, d.x);
                    Vector2 from = p + d * ((length - count * bay) * 0.5f);
                    for (int k = 0; k <= count; k++)
                        Line(white, from + d * (k * bay), from + d * (k * bay) + n * depth, lw, ground);
                    Line(white, from - d * (lw * 0.5f), from + d * (count * bay + lw * 0.5f), lw, ground);
                }
            }

            // --- Objets : l'enrobé porte la collision (les roues roulent dessus, les traces de pneus s'y posent)
            var assets = game.assets;
            RoadNetwork.AddMesh(world, "Runway", runway.Build("AirfieldRunway"), assets.asphalt, true);
            RoadNetwork.AddMesh(world, "Taxiways", taxi.Build("AirfieldTaxiways"), assets.asphalt, true);
            RoadNetwork.AddMesh(world, "Markings", white.Build("AirfieldWhite"), assets.roadMarking, false);
            yellowPaint = new Material(assets.roadMarking) { name = "Airfield Yellow" };
            if (yellowPaint.HasProperty("_PaintColor")) yellowPaint.SetColor("_PaintColor", new Color(0.97f, 0.76f, 0.1f));
            var lines = RoadNetwork.AddMesh(world, "TaxiMarkings", yellow.Build("AirfieldYellow"), yellowPaint, false);
            lines.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var r in new[] { a.apron, a.park, a.gate })
                if (r != null && r.Length >= 4) aprons.Add(new Vector4(r[0], r[1], r[2], r[3]));
            BuildBeads();
        }

        // ------------------------------------------------------------------ balisage lumineux

        /// <summary>
        /// Les feux posés par la carte (airfield_light_*) font 25 cm : de nuit, à quelques centaines de mètres, ils
        /// disparaissent sous le pixel. Chaque feu reçoit un losange émissif tourné vers la caméra, dont la taille suit
        /// la distance : le balisage se lit de loin comme sur un vrai terrain, et reste discret de près.
        /// </summary>
        void BuildBeads()
        {
            var places = game.Map.Json.places;
            if (places == null || !game.assets.emissiveWhite) return;
            var kinds = new (string item, Color color)[]
            {
                ("airfield_light_white", new Color(1f, 0.93f, 0.78f)), ("airfield_light_blue", new Color(0.22f, 0.45f, 1f)),
                ("airfield_light_green", new Color(0.2f, 1f, 0.42f)), ("airfield_light_red", new Color(1f, 0.2f, 0.14f)),
            };
            foreach (var kind in kinds)
            {
                var b = new Beads { color = kind.color };
                foreach (var p in places)
                    if (p.item == kind.item) b.at.Add(new Vector3(p.x, p.y + 0.36f, p.z));
                if (b.at.Count == 0) continue;
                b.verts = new Vector3[b.at.Count * 4];
                var tris = new int[b.at.Count * 6];
                for (int i = 0; i < b.at.Count; i++)
                {
                    int v = i * 4, t = i * 6;
                    tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                    tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                }
                b.mesh = new Mesh { name = "AirfieldLights" };
                b.mesh.MarkDynamic();
                b.mesh.vertices = b.verts;
                b.mesh.triangles = tris;
                b.mesh.bounds = new Bounds(origin, new Vector3(a.half * 2f + 200f, 60f, a.half * 2f + 200f));
                b.material = new Material(game.assets.emissiveWhite) { name = "Airfield light (runtime)" };
                b.material.SetColor("_BaseColor", kind.color * 0.25f);
                b.material.EnableKeyword("_EMISSION");
                b.material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                var go = new GameObject("Lights " + kind.item, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(world.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = b.mesh;
                b.renderer = go.GetComponent<MeshRenderer>();
                b.renderer.sharedMaterial = b.material;
                b.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.renderer.receiveShadows = false;
                b.renderer.enabled = false;
                beads.Add(b);
            }
            if (beads.Count > 0) UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += FaceCamera;
        }

        /// <summary>Avant le rendu de chaque caméra : losanges face à elle, grossis avec la distance ; éteints de jour.</summary>
        void FaceCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            if (!this || !cam || beads.Count == 0) return;
            float dark = game && game.DayNight ? game.DayNight.Dark : 0f;
            Vector3 eye = cam.transform.position;
            bool on = dark > 0.04f && (eye - origin).sqrMagnitude < 3200f * 3200f;
            Vector3 right = cam.transform.right, up = cam.transform.up;
            foreach (var b in beads)
            {
                if (b.renderer.enabled != on) b.renderer.enabled = on;
                if (!on) continue;
                // modéré : au-delà, le flou lumineux blanchit les couleurs (bleu des voies, vert et rouge des seuils)
                b.material.SetColor("_EmissionColor", b.color * (dark * 1.9f));
                for (int i = 0; i < b.at.Count; i++)
                {
                    Vector3 p = b.at[i];
                    float size = Mathf.Clamp((p - eye).magnitude * 0.0023f, 0.11f, 4f);
                    int v = i * 4;
                    b.verts[v] = p + up * size;
                    b.verts[v + 1] = p + right * size;
                    b.verts[v + 2] = p - up * size;
                    b.verts[v + 3] = p - right * size;
                }
                b.mesh.vertices = b.verts;
            }
        }

        void OnDestroy()
        {
            if (current == this) current = null;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= FaceCamera;
            foreach (var b in beads)
            {
                if (b.mesh) Destroy(b.mesh);
                if (b.material) Destroy(b.material);
            }
            if (world) Destroy(world);
            if (yellowPaint) Destroy(yellowPaint);
        }
    }
}
