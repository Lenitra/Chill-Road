using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Terrain d'essai (lieu « school » de la carte, Tools/Map/map_school.py) : bac à sable du jeu dans la vallée est,
    /// sans leçon ni mission. Le circuit (une vraie course, Races.cs), le radar, le tremplin, l'arène de drift et les
    /// décors viennent de la carte ; ce composant ne fait que peindre le sol : vibreurs le long des bords dans les
    /// virages, damier et grille de la ligne de départ, bord de l'arène, cercles guides autour des deux poteaux et
    /// traces de drift permanentes dans l'arène.
    /// </summary>
    public class ProvingGround : MonoBehaviour
    {
        const float RoadHalf = 6f, ArenaR = 30f, Guide = 12f;
        const string RaceId = "ecole";

        GameManager game;
        GameObject world;
        bool searched, found, built;
        readonly List<Vector3> lap = new List<Vector3>();
        readonly List<float> lapS = new List<float>();
        readonly List<Vector3> pylons = new List<Vector3>();
        float length;
        Vector3 center;

        public void Init(GameManager g) => game = g;

        bool Resolve()
        {
            if (searched) return found;
            var json = game.Map != null ? game.Map.Json : null;
            if (json == null || json.edges == null || json.pois == null || json.places == null) return false;
            searched = true;
            var parts = new List<MapData.EdgeJson>();
            foreach (var e in json.edges)
                if (e.id.StartsWith("ecole_piste")) parts.Add(e);
            parts.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            foreach (var e in parts)
                for (int i = lap.Count > 0 ? 3 : 0; i + 2 < e.pts.Length; i += 3)
                    lap.Add(new Vector3(e.pts[i], e.pts[i + 1], e.pts[i + 2]));
            foreach (var p in json.pois)
                if (p.id == "school") center = new Vector3(p.x, 0f, p.z);
            foreach (var p in json.places)
                if (p.item == "school_pylon") pylons.Add(new Vector3(p.x, p.y, p.z));
            found = lap.Count > 10 && pylons.Count == 2;
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

        void Update()
        {
            if (built || !game || !game.Player || !Resolve()) return;
            Vector3 p = game.Player.transform.position;
            if (new Vector2(p.x - center.x, p.z - center.z).magnitude > 900f) return;
            // La course doit être prête : la grille de départ se peint sur sa ligne
            if (!game.Races || game.Races.All.Count == 0) return;
            Build();
        }

        // ------------------------------------------------------------------ piste

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

        float Locate(Vector3 p)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i + 1 < lap.Count; i++)
            {
                Vector3 a = lap[i], d = lap[i + 1] - a, r = p - a;
                d.y = 0f;
                r.y = 0f;
                float k = Mathf.Clamp01(Vector3.Dot(r, d) / Mathf.Max(d.sqrMagnitude, 1e-4f));
                float q = (r - d * k).sqrMagnitude;
                if (q < best) { best = q; bestS = Mathf.Lerp(lapS[i], lapS[i + 1], k); }
            }
            return bestS;
        }

        /// <summary>Point de la chaussée à side mètres à gauche de l'axe, juste au-dessus du revêtement.</summary>
        Vector3 Mark(float s, float side, float lift = 0.05f)
        {
            Vector3 c = At(s, out _, out Vector3 left);
            Vector3 p = c + left * side;
            p.y = c.y + lift;
            return p;
        }

        static void Face(RoadNetwork.MeshBuilder m, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => m.QuadFacing(a, b, c, d, Vector3.up);

        void Band(RoadNetwork.MeshBuilder m, float s0, float s1, float side0, float side1)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 2.5f));
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(s0, s1, i / (float)n), b = Mathf.Lerp(s0, s1, (i + 1) / (float)n);
                Face(m, Mark(a, side0), Mark(a, side1), Mark(b, side0), Mark(b, side1));
            }
        }

        /// <summary>Trait au sol de a à b (largeur w), posé sur le terrain.</summary>
        void Ribbon(RoadNetwork.MeshBuilder m, Vector3 a, Vector3 b, float w, float lift = 0.05f)
        {
            Vector3 d = b - a;
            d.y = 0f;
            Vector3 n = new Vector3(-d.z, 0f, d.x).normalized * (w * 0.5f);
            Vector3 G(Vector3 p)
            {
                p.y = game.Map.Height(p.x, p.z) + lift;
                return p;
            }
            Face(m, G(a - n), G(a + n), G(b - n), G(b + n));
        }

        /// <summary>
        /// Traces de drift permanentes dans l'arène, irrégulières comme de vraies traces : boucles autour des deux poteaux
        /// (centres qui dérivent, rayons bruités), 8 entre les poteaux, quelques têtes-à-queue serrés et des frottements
        /// larges. Chaque paire de pneus a sa voie, sa largeur qui varie et s'effile aux bouts, ses coupures (parfois un
        /// seul pneu marque) et pâlit vers sa fin. Trois tons (marks[0] frais → marks[2] ancien), sous les marquages
        /// peints. Tirage fixe : les mêmes à chaque partie.
        /// </summary>
        void DriftMarks(RoadNetwork.MeshBuilder[] marks)
        {
            var rnd = new System.Random(4207);
            float R(float lo, float hi) => lo + (hi - lo) * (float)rnd.NextDouble();
            Vector3 axis = pylons[1] - pylons[0];
            axis.y = 0f;
            axis.Normalize();
            Vector3 across = new Vector3(-axis.z, 0f, axis.x);
            int seed = 0;

            // Boucles autour de chaque poteau : le centre glisse pendant la boucle, le rayon respire
            for (int pi = 0; pi < 2; pi++)
                for (int loop = 0; loop < 9; loop++)
                {
                    Vector3 c0 = pylons[pi] + axis * R(-2.5f, 2.5f) + across * R(-2.5f, 2.5f);
                    Vector3 drift = axis * R(-2f, 2f) + across * R(-2f, 2f);
                    float r0 = R(7.5f, 15f), r1 = r0 + R(-3f, 3f), start = R(0f, 360f), span = R(140f, 400f);
                    float dir = (pi == 0) == (loop % 4 != 3) ? 1f : -1f, ph = R(0f, 50f);
                    var line = new List<Vector3>();
                    int n = Mathf.CeilToInt(span / 5f);
                    for (int i = 0; i <= n; i++)
                    {
                        float t = i / (float)n, ang = (start + dir * span * t) * Mathf.Deg2Rad;
                        float r = Mathf.Lerp(r0, r1, t) + (Mathf.PerlinNoise(ph, t * span / 60f) - 0.5f) * 2.2f
                                  + Mathf.Sin(t * 23f + loop) * 0.15f;
                        line.Add(c0 + drift * t + (axis * Mathf.Cos(ang) + across * Mathf.Sin(ang)) * r);
                    }
                    Pair(marks, line, R(0.2f, 0.32f), loop % 3, seed++);
                }

            // 8 : un cercle autour de chaque poteau, reliés par les deux tangentes intérieures qui se croisent au milieu
            for (int k = 0; k < 6; k++)
            {
                Vector3 a = pylons[0] + axis * R(-1.5f, 1.5f) + across * R(-1.5f, 1.5f);
                Vector3 b = pylons[1] + axis * R(-1.5f, 1.5f) + across * R(-1.5f, 1.5f);
                a.y = b.y = 0f;
                var line = Eight(a, b, R(10f, 14f));
                float wob = R(0.3f, 1f), phase = R(0f, 6.28f), ph = R(0f, 50f);
                for (int i = 0; i < line.Count; i++)
                {
                    float t = i / (float)line.Count;
                    float off = Mathf.Sin(t * Mathf.PI * 6f + phase) * wob + (Mathf.PerlinNoise(ph, t * 14f) - 0.5f) * 1.6f;
                    line[i] += across * off;
                }
                // Parfois un 8 interrompu : la voiture est partie en ligne droite à mi-parcours
                if (k % 3 == 2) line.RemoveRange(line.Count * 2 / 3, line.Count - line.Count * 2 / 3);
                Pair(marks, line, R(0.2f, 0.3f), k % 3, seed++);
            }

            // Têtes-à-queue : courtes spirales qui se resserrent, près des poteaux
            for (int k = 0; k < 4; k++)
            {
                Vector3 c = pylons[k % 2] + axis * R(-13f, 13f) + across * R(-9f, 9f);
                float r0 = R(5f, 7f), r1 = R(1.6f, 2.6f), start = R(0f, 360f), span = R(150f, 260f), dir = k % 2 == 0 ? 1f : -1f;
                var line = new List<Vector3>();
                for (int i = 0; i <= 24; i++)
                {
                    float t = i / 24f, ang = (start + dir * span * t) * Mathf.Deg2Rad;
                    line.Add(c + (axis * Mathf.Cos(ang) + across * Mathf.Sin(ang)) * Mathf.Lerp(r0, r1, t * t));
                }
                Pair(marks, line, R(0.24f, 0.34f), 1 + k % 2, seed++);
            }

            // Frottements : larges bandes pâles et courtes, là où les voitures se mettent en travers
            for (int k = 0; k < 10; k++)
            {
                Vector3 c = pylons[k % 2];
                float ang = R(0f, Mathf.PI * 2f), r = R(8f, 14f), len = R(3f, 7f), w = R(0.7f, 1.4f);
                Vector3 e = axis * Mathf.Cos(ang) + across * Mathf.Sin(ang), tan = Vector3.Cross(Vector3.up, e);
                Vector3 p0 = c + e * r - tan * (len * 0.5f), p1 = c + e * (r + R(-1f, 1f)) + tan * (len * 0.5f);
                const int n = 6;
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    float w0 = w * Mathf.Sin(t0 * Mathf.PI), w1 = w * Mathf.Sin(t1 * Mathf.PI);
                    Strip(marks[2], Vector3.Lerp(p0, p1, t0), Vector3.Lerp(p0, p1, t1), w0, w1, 0.026f);
                }
            }
        }

        /// <summary>
        /// Paire de traces de pneus le long d'une ligne : voie propre à la voiture, largeur bruitée qui s'effile aux deux
        /// bouts, coupures de longueur variable (un pneu seul parfois), écart latéral bruité, ton qui pâlit en fin de trace.
        /// </summary>
        void Pair(RoadNetwork.MeshBuilder[] marks, List<Vector3> line, float w, int tone, int seed)
        {
            if (line.Count < 2) return;
            var rnd = new System.Random(seed * 131 + 7);
            float R(float lo, float hi) => lo + (hi - lo) * (float)rnd.NextDouble();
            float half = R(0.66f, 0.85f), lift = 0.028f + 0.0012f * (seed % 8);
            var len = new float[line.Count];
            var side = new Vector3[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) len[i] = len[i - 1] + Vector3.Distance(line[i - 1], line[i]);
                Vector3 d = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                d.y = 0f;
                side[i] = new Vector3(-d.z, 0f, d.x).normalized;
            }
            float total = Mathf.Max(len[line.Count - 1], 1f), taper = Mathf.Min(4f, total * 0.2f);
            // Ton par passages (jamais par petits blocs) : un passage plus pâle au milieu une fois sur deux, et la fin
            // de la trace qui pâlit
            int fadeAt = Mathf.RoundToInt(line.Count * R(0.55f, 0.85f));
            int paleAt = rnd.NextDouble() < 0.5 ? rnd.Next(line.Count) : -1, paleTo = paleAt + rnd.Next(8, 25);
            for (int wheel = -1; wheel <= 1; wheel += 2)
            {
                float ph = R(0f, 100f), jit = R(0f, 100f);
                int gap = 0;
                float Width(int i)
                {
                    float ends = Mathf.Clamp01(len[i] / taper) * Mathf.Clamp01((total - len[i]) / taper);
                    return w * Mathf.Lerp(0.55f, 1.25f, Mathf.PerlinNoise(ph, len[i] * 0.18f)) * Mathf.Sqrt(ends);
                }
                Vector3 At(int i) => line[i] + side[i] * (wheel * half + (Mathf.PerlinNoise(jit, len[i] * 0.12f) - 0.5f) * 0.35f);
                Vector3 Normal(int i) => i > 0 && i + 1 < line.Count ? Vector3.Lerp(Edge(i - 1), Edge(i), 0.5f).normalized : Edge(Mathf.Min(i, line.Count - 2));
                Vector3 Edge(int i)
                {
                    Vector3 d = At(i + 1) - At(i);
                    d.y = 0f;
                    return new Vector3(-d.z, 0f, d.x).normalized;
                }
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    if (gap > 0) { gap--; continue; }
                    double roll = rnd.NextDouble();
                    if (roll < 0.035) { gap = rnd.Next(1, 5); continue; }          // petite coupure
                    if (roll < 0.045) { gap = rnd.Next(6, 16); continue; }         // ce pneu a cessé de marquer un moment
                    int t = Mathf.Min(2, tone + (i >= fadeAt ? 1 : 0) + (i >= paleAt && i < paleTo ? 1 : 0));
                    // Bords communs aux jointures (normale moyenne) : la bande reste continue, sans marches
                    Strip(marks[t], At(i), At(i + 1), Normal(i), Normal(i + 1), Width(i), Width(i + 1), lift);
                }
            }
        }

        /// <summary>Bande au sol de a (largeur wa) à b (largeur wb), posée sur le terrain.</summary>
        void Strip(RoadNetwork.MeshBuilder m, Vector3 a, Vector3 b, float wa, float wb, float lift)
        {
            Vector3 d = b - a;
            d.y = 0f;
            Vector3 n = new Vector3(-d.z, 0f, d.x).normalized;
            Strip(m, a, b, n, n, wa, wb, lift);
        }

        /// <summary>Idem, avec la direction des bords imposée à chaque bout (na, nb : horizontales, unitaires).</summary>
        void Strip(RoadNetwork.MeshBuilder m, Vector3 a, Vector3 b, Vector3 na, Vector3 nb, float wa, float wb, float lift)
        {
            if (wa < 0.02f && wb < 0.02f) return;
            Vector3 G(Vector3 p)
            {
                p.y = game.Map.Height(p.x, p.z) + lift;
                return p;
            }
            Face(m, G(a - na * (wa * 0.5f)), G(a + na * (wa * 0.5f)), G(b - nb * (wb * 0.5f)), G(b + nb * (wb * 0.5f)));
        }

        /// <summary>Tracé d'un 8 (plan horizontal) : tangente a→b, tour de b, tangente croisée b→a, tour de a.</summary>
        static List<Vector3> Eight(Vector3 a, Vector3 b, float r)
        {
            var o = new List<Vector3>();
            Vector3 ax = (b - a).normalized, n = new Vector3(-ax.z, 0f, ax.x), mid = (a + b) * 0.5f;
            float phi = Mathf.Asin(Mathf.Min(0.95f, r / (Vector3.Distance(a, b) * 0.5f)));
            Vector3 u1 = ax * Mathf.Cos(phi) + n * Mathf.Sin(phi), u2 = -ax * Mathf.Cos(phi) + n * Mathf.Sin(phi);
            Vector3 Foot(Vector3 c, Vector3 u) => mid + u * Vector3.Dot(c - mid, u);
            void Seg(Vector3 p, Vector3 q)
            {
                int k = Mathf.Max(1, (int)(Vector3.Distance(p, q) / 2f));
                for (int i = 0; i < k; i++) o.Add(Vector3.Lerp(p, q, i / (float)k));
            }
            void Arc(Vector3 c, Vector3 p, Vector3 q, Vector3 u)
            {
                Vector3 dp = p - c, dq = q - c;
                float sgn = dp.x * u.z - dp.z * u.x > 0f ? 1f : -1f;
                float t0 = Mathf.Atan2(dp.z, dp.x), t1 = Mathf.Atan2(dq.z, dq.x);
                float sweep = Mathf.Repeat(sgn * (t1 - t0), Mathf.PI * 2f);
                int k = Mathf.Max(2, (int)(sweep * Mathf.Rad2Deg / 6f));
                for (int i = 0; i < k; i++)
                {
                    float t = t0 + sgn * sweep * i / k;
                    o.Add(c + new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * r);
                }
            }
            Vector3 a1 = Foot(a, u1), b1 = Foot(b, u1), b2 = Foot(b, u2), a2 = Foot(a, u2);
            Seg(a1, b1);
            Arc(b, b1, b2, u1);
            Seg(b2, a2);
            Arc(a, a2, a1, u2);
            o.Add(a1);
            return o;
        }

        void Build()
        {
            built = true;
            world = new GameObject("ProvingGround");
            var white = new RoadNetwork.MeshBuilder();
            var black = new RoadNetwork.MeshBuilder();
            var red = new RoadNetwork.MeshBuilder();
            var orange = new RoadNetwork.MeshBuilder();
            float h = RoadHalf - 0.3f;

            // Ligne de départ de la course : damier en travers, puis la grille (cases décalées) derrière
            Races.Track race = null;
            foreach (var t in game.Races.All) if (t.id == RaceId) race = t;
            if (race != null)
            {
                float s0 = Locate(race.start.pos);
                const int cols = 12;
                for (int row = 0; row < 2; row++)
                    for (int c = 0; c < cols; c++)
                    {
                        float a = -h + 2f * h * c / cols, b = -h + 2f * h * (c + 1) / cols;
                        Band((row + c) % 2 == 0 ? white : black, s0 + row * 0.95f, s0 + (row + 1) * 0.95f, a, b);
                    }
                for (int k = 0; k < 6; k++)
                {
                    float back = s0 - 8f - k * 7f, side = (k % 2 == 0 ? 1f : -1f) * RoadHalf * 0.45f;
                    Band(white, back - 0.25f, back, side - 1.2f, side + 1.2f);
                    Band(white, back - 5f, back, side + 1.05f, side + 1.2f);
                    Band(white, back - 5f, back, side - 1.2f, side - 1.05f);
                }
            }

            // Vibreurs rouges et blancs dans les virages : sur la chaussée, le long de ses deux bords, en blocs de 3 m
            int k2 = 0;
            for (float s = 0f; s < length; s += 3f)
            {
                At(s - 6f, out Vector3 t0, out _);
                At(s + 6f, out Vector3 t1, out _);
                if (Mathf.Abs(Vector3.SignedAngle(t0, t1, Vector3.up)) < 6f) { k2 = 0; continue; }
                var c = k2++ % 2 == 0 ? red : white;
                Band(c, s, s + 3f, RoadHalf - 1.1f, RoadHalf - 0.15f);
                Band(c, s, s + 3f, -RoadHalf + 0.15f, -RoadHalf + 1.1f);
            }

            // Arène de drift : bord de l'ovale en orange, cercle guide en tirets blancs autour de chaque poteau (le 8)
            const int seg = 36;
            Vector3 axis = pylons[1] - pylons[0];
            axis.y = 0f;
            float halfLen = axis.magnitude * 0.5f;
            axis.Normalize();
            Vector3 across = new Vector3(-axis.z, 0f, axis.x), mid = (pylons[0] + pylons[1]) * 0.5f;
            var outline = new List<Vector3>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * (0.5f + i / (float)seg);
                outline.Add(mid - axis * halfLen + (axis * Mathf.Cos(a) + across * Mathf.Sin(a)) * (ArenaR - 1.2f));
            }
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * (-0.5f + i / (float)seg);
                outline.Add(mid + axis * halfLen + (axis * Mathf.Cos(a) + across * Mathf.Sin(a)) * (ArenaR - 1.2f));
            }
            for (int i = 0; i < outline.Count; i++) Ribbon(orange, outline[i], outline[(i + 1) % outline.Count], 0.8f);
            var skid = new[] { new RoadNetwork.MeshBuilder(), new RoadNetwork.MeshBuilder(), new RoadNetwork.MeshBuilder() };
            DriftMarks(skid);
            foreach (var pylon in pylons)
                for (int i = 0; i < 48; i++)
                {
                    if (i % 4 >= 2) continue;
                    float a0 = i * Mathf.PI * 2f / 48f, a1 = (i + 1) * Mathf.PI * 2f / 48f;
                    Vector3 e0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), e1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Ribbon(white, pylon + e0 * Guide, pylon + e1 * Guide, 0.35f);
                }

            Paint("White", white, new Color(0.93f, 0.93f, 0.9f));
            Paint("Black", black, new Color(0.06f, 0.06f, 0.07f));
            Paint("Red", red, new Color(0.86f, 0.17f, 0.13f));
            Paint("Orange", orange, new Color(1f, 0.56f, 0.12f));
            Paint("Skid", skid[0], new Color(0.2f, 0.19f, 0.19f));
            Paint("SkidMid", skid[1], new Color(0.32f, 0.31f, 0.3f));
            Paint("SkidOld", skid[2], new Color(0.45f, 0.43f, 0.4f));
        }

        void Paint(string name, RoadNetwork.MeshBuilder m, Color color)
        {
            var shader = Shader.Find("ChillRoad/Toon");
            if (!shader || m.v.Count == 0) return;
            var mat = new Material(shader) { name = "ProvingGround " + name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_SpecStrength")) mat.SetFloat("_SpecStrength", 0f);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(world.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = m.Build("ProvingGround" + name);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnDestroy()
        {
            if (world) Destroy(world);
        }
    }
}
