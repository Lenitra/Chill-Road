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
        /// Traces de drift permanentes dans l'arène : boucles de pneus (paires de traits, voie de 1,5 m) qui tournent autour
        /// des deux poteaux en spirale irrégulière, et 8 qui se croisent entre eux. Deux tons : traces fraîches et anciennes.
        /// Sous les marquages peints (hauteurs un peu plus basses), tirage fixe : les mêmes à chaque partie.
        /// </summary>
        void DriftMarks(RoadNetwork.MeshBuilder fresh, RoadNetwork.MeshBuilder old)
        {
            var rnd = new System.Random(4207);
            float R(float a, float b) => a + (b - a) * (float)rnd.NextDouble();
            Vector3 axis = pylons[1] - pylons[0];
            axis.y = 0f;
            axis.Normalize();
            Vector3 across = new Vector3(-axis.z, 0f, axis.x);

            // Boucles autour de chaque poteau
            for (int pi = 0; pi < 2; pi++)
                for (int loop = 0; loop < 9; loop++)
                {
                    var m = loop % 3 == 0 ? fresh : old;
                    Vector3 c = pylons[pi] + axis * R(-2.5f, 2.5f) + across * R(-2.5f, 2.5f);
                    float r0 = R(7.5f, 15f), r1 = r0 + R(-2.5f, 2.5f), start = R(0f, 360f), span = R(160f, 380f);
                    float dir = (pi == 0) == (loop % 4 != 3) ? 1f : -1f;
                    var pts = new List<(Vector3 p, Vector3 o)>();
                    int n = Mathf.CeilToInt(span / 6f);
                    for (int i = 0; i <= n; i++)
                    {
                        float t = i / (float)n, a = (start + dir * span * t) * Mathf.Deg2Rad;
                        float r = Mathf.Lerp(r0, r1, t) + Mathf.Sin(t * 9f + loop) * 0.35f;
                        Vector3 e = axis * Mathf.Cos(a) + across * Mathf.Sin(a);
                        pts.Add((c + e * r, e));
                    }
                    Pair(m, pts, R(0.22f, 0.32f), loop);
                }

            // 8 : un cercle autour de chaque poteau, reliés par les deux tangentes intérieures qui se croisent au milieu
            for (int k = 0; k < 6; k++)
            {
                var m = k % 2 == 0 ? fresh : old;
                Vector3 a = pylons[0] + axis * R(-1.5f, 1.5f) + across * R(-1.5f, 1.5f);
                Vector3 b = pylons[1] + axis * R(-1.5f, 1.5f) + across * R(-1.5f, 1.5f);
                a.y = b.y = 0f;
                var line = Eight(a, b, R(10f, 14f));
                float wob = R(0.3f, 1f), phase = R(0f, 6.28f);
                var pts = new List<(Vector3 p, Vector3 o)>();
                for (int i = 0; i < line.Count; i++)
                {
                    Vector3 d = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                    d.y = 0f;
                    float t = i / (float)line.Count * Mathf.PI * 2f;
                    pts.Add((line[i] + across * (Mathf.Sin(t * 3f + phase) * wob), new Vector3(-d.z, 0f, d.x).normalized));
                }
                Pair(m, pts, R(0.22f, 0.3f), 20 + k);
            }
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

        /// <summary>Paire de traces de pneus le long d'une ligne (o : côté), avec des trous pour un rendu usé.</summary>
        void Pair(RoadNetwork.MeshBuilder m, List<(Vector3 p, Vector3 o)> pts, float w, int seed)
        {
            var rnd = new System.Random(seed * 131 + 7);
            for (int side = -1; side <= 1; side += 2)
            {
                float lift = 0.028f + 0.0015f * (seed % 7);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    if (rnd.NextDouble() < 0.08) continue;
                    Vector3 a = pts[i].p + pts[i].o * (side * 0.75f), b = pts[i + 1].p + pts[i + 1].o * (side * 0.75f);
                    Ribbon(m, a, b, w, lift);
                }
            }
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
            var skid = new RoadNetwork.MeshBuilder();
            var skidOld = new RoadNetwork.MeshBuilder();
            DriftMarks(skid, skidOld);
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
            Paint("Skid", skid, new Color(0.2f, 0.19f, 0.19f));
            Paint("SkidOld", skidOld, new Color(0.44f, 0.42f, 0.39f));
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
