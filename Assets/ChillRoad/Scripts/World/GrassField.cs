using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChillRoad
{
    /// <summary>
    /// Herbe des prés : un champ continu de brins fins autour du joueur (shader ChillRoad/Grass).
    /// - le sol est découpé en carrés de 2 m qui partagent un seul maillage de brins (rangés du plus important au
    ///   moins important : chaque début de liste reste bien réparti) ; un carré ne stocke que son centre, sa pente,
    ///   la hauteur de son herbe et la part de brins gardés à ses quatre coins, sa part fauchée et les masques du sol ;
    /// - les carrés sont générés par cases de 16 m dans un budget de temps, d'après la carte d'herbe cuite par
    ///   Tools/Map/map_grass.py (Resources/Map/map_grass : couverture du milieu, hauteur, prés de fauche, distance au
    ///   bâti) et le bord de route de map_info, lus au plus fin : herbe jusqu'à l'accotement, bord net au pied des murs ;
    /// - les prés de fauche sont découpés en parcelles (Voronoï de 46 m) : fauchée, en repousse ou en foin haut, avec
    ///   une bande non fauchée en lisière de parcelle ;
    /// - au tri (caméra déplacée ou tournée), chaque carré visible va dans un lot selon le nombre de brins utiles à sa
    ///   distance : un seul tampon GPU, un dessin indirect par lot (seulement les premiers brins du maillage) ;
    /// - les voitures proches couchent l'herbe (capsules envoyées au shader).
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        [Tooltip("Fin de la densité pleine (m)")] public float fullDistance = 12f;
        [Tooltip("Fin du champ (m) : au-delà, le terrain seul")] public float endDistance = 100f;
        [Tooltip("Exposant de l'éclaircie avec la distance")] public float thinning = 1.3f;
        [Tooltip("Densité (1 = normale, réglage graphique)")] public float density = 1f;
        [Tooltip("Hauteur de base de l'herbe des prés (m)")] public float meadowHeight = 0.5f;
        [Tooltip("Temps de génération max par image (ms)")] public float budgetMs = 1.5f;

        public const float PatchSize = 2f;
        const float ChunkSize = 16f;
        const int PatchesPerSide = 8;
        /// <summary>Brins par carré de 2 m (densité pleine : 100 brins/m²).</summary>
        public const int BladesPerPatch = 400;
        const int Rows = 4;                       // tronçons par brin (+ la pointe)
        const int VertsPerBlade = Rows * 2 + 1;   // 9 sommets
        const int IndicesPerBlade = (Rows - 1) * 6 + 3 + 0; // 3 quads + pointe = 7 triangles
        // Paliers de densité des lots (part des brins dessinés)
        static readonly float[] Levels = { 1f, 0.7f, 0.5f, 0.35f, 0.25f, 0.17f, 0.12f, 0.085f, 0.06f, 0.042f, 0.03f };

        // a = centre + graine, b = pente + hauteur au centre + part fauchée, c = herbe sèche + sous-bois + foin + nuance,
        // d = densité aux quatre coins, e = hauteur aux quatre coins (x0z0, x1z0, x0z1, x1z1)
        struct Patch { public Vector4 a, b, c, d, e; }
        const int PatchStride = 80;
        // Hauteur des rangées de sommets le long d'un brin (la pointe est à 1), un peu serrées vers le haut pour une
        // pointe de feuille effilée mais douce
        static readonly float[] RowT = { 0f, 0.3f, 0.6f, 0.85f };

        class Chunk
        {
            public Patch[] patches;
            public Bounds bounds;
        }

        class Bucket
        {
            public GraphicsBuffer args, patches;
            public MaterialPropertyBlock props;
            public int capacity, count;
        }

        MapData map;
        byte[] grassMap;          // map_grass.bytes : 4 octets par nœud (couverture, hauteur en cm + bit 7 entretenue,
                                  // prés de fauche, (bâti + 2) × 8)
        Material material;
        Mesh mesh;
        readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        readonly HashSet<Vector2Int> empty = new HashSet<Vector2Int>();
        readonly List<Vector2Int> wanted = new List<Vector2Int>();
        readonly List<Vector2Int> drop = new List<Vector2Int>();
        readonly List<Patch> build = new List<Patch>(64);
        readonly List<Patch>[] sorted = new List<Patch>[Levels.Length];
        readonly Bucket[] buckets = new Bucket[Levels.Length];
        readonly Plane[] planes = new Plane[6];
        readonly GraphicsBuffer.IndirectDrawIndexedArgs[] argsData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        Vector3 focus;
        System.Comparison<Vector2Int> byDistance;

        bool dirty = true;
        Vector3 lastPos;
        Quaternion lastRot;
        float lastSettings = -1f;
        Camera lastCam;

        public int Patches { get; private set; }
        public long Triangles { get; private set; }

        static readonly int PatchesId = Shader.PropertyToID("_GrassPatches");
        static readonly int PatchSizeId = Shader.PropertyToID("_GrassPatchSize");
        static readonly int RangeId = Shader.PropertyToID("_GrassRange");
        static readonly int DensityId = Shader.PropertyToID("_GrassDensity");
        static readonly int PushId = Shader.PropertyToID("_ChillRoadGrassPush");
        static readonly int PushInfoId = Shader.PropertyToID("_ChillRoadGrassPushInfo");

        public bool Ready => material && mesh && SystemInfo.supportsComputeShaders;

        public void Init(MapData m, Material terrain)
        {
            map = m;
            byDistance = (a, b) => ChunkDistance(a, focus).CompareTo(ChunkDistance(b, focus));
            var baked = Resources.Load<TextAsset>("Map/map_grass");
            if (baked && m != null && baked.bytes.Length == m.N * m.N * 4) grassMap = baked.bytes;
            else Debug.LogWarning("GrassField : Resources/Map/map_grass absent ou périmé (lancer Tools/Map/map_grass.py)");
            if (baked) Resources.UnloadAsset(baked);
            var template = Resources.Load<Material>("GrassField");
            if (template) material = new Material(template) { name = "GrassField (runtime)" };
            else
            {
                var shader = Shader.Find("ChillRoad/Grass");
                if (shader) material = new Material(shader) { name = "GrassField (runtime)" };
            }
            if (!material) { Debug.LogWarning("GrassField : shader ChillRoad/Grass introuvable"); return; }
            // Couleurs et bruits du terrain : le pied des brins a exactement la couleur du sol
            if (terrain)
            {
                foreach (var p in new[] { "_Grass", "_GrassLush", "_GrassDry", "_Forest", "_ShadeColor" })
                    if (terrain.HasProperty(p)) material.SetColor(p, terrain.GetColor(p));
                foreach (var p in new[] { "_NoiseScale", "_ShadeLevel", "_RampSoft" })
                    if (terrain.HasProperty(p)) material.SetFloat(p, terrain.GetFloat(p));
                if (terrain.HasProperty("_Noise")) material.SetTexture("_Noise", terrain.GetTexture("_Noise"));
            }
            mesh = BuildMesh();
            for (int i = 0; i < Levels.Length; i++)
            {
                sorted[i] = new List<Patch>();
                buckets[i] = new Bucket
                {
                    props = new MaterialPropertyBlock(),
                    args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size),
                };
            }
        }

        // ------------------------------------------------------------------ maillage d'un carré

        /// <summary>
        /// Brins d'un carré de 2 m, placés par « meilleur candidat » (chaque nouveau brin le plus loin des précédents,
        /// distances repliées sur le carré) : les N premiers brins couvrent toujours le carré régulièrement.
        /// Sommets : position = (racine x, t, racine z), uv = (côté, rang).
        /// </summary>
        static Mesh BuildMesh()
        {
            var rng = new System.Random(7741);
            var roots = new List<Vector2>(BladesPerPatch);
            for (int i = 0; i < BladesPerPatch; i++)
            {
                Vector2 best = default;
                float bestD = -1f;
                int tries = Mathf.Min(1 + i, 12);
                for (int k = 0; k < tries; k++)
                {
                    var cand = new Vector2((float)rng.NextDouble() * PatchSize, (float)rng.NextDouble() * PatchSize);
                    float d = float.MaxValue;
                    foreach (var r in roots)
                    {
                        float dx = Mathf.Abs(cand.x - r.x), dz = Mathf.Abs(cand.y - r.y);
                        dx = Mathf.Min(dx, PatchSize - dx);
                        dz = Mathf.Min(dz, PatchSize - dz);
                        d = Mathf.Min(d, dx * dx + dz * dz);
                    }
                    if (d > bestD) { bestD = d; best = cand; }
                }
                roots.Add(best);
            }

            var pos = new Vector3[BladesPerPatch * VertsPerBlade];
            var uv = new Vector2[pos.Length];
            var idx = new int[BladesPerPatch * IndicesPerBlade];
            int v = 0, n = 0;
            for (int i = 0; i < BladesPerPatch; i++)
            {
                float rank = (i + 0.5f) / BladesPerPatch;
                var r = roots[i];
                int b0 = v;
                for (int row = 0; row < Rows; row++)
                {
                    float t = RowT[row];
                    pos[v] = new Vector3(r.x, t, r.y); uv[v++] = new Vector2(-1f, rank);
                    pos[v] = new Vector3(r.x, t, r.y); uv[v++] = new Vector2(1f, rank);
                }
                pos[v] = new Vector3(r.x, 1f, r.y); uv[v++] = new Vector2(0f, rank);
                for (int row = 0; row < Rows - 1; row++)
                {
                    int a = b0 + row * 2;
                    idx[n++] = a; idx[n++] = a + 2; idx[n++] = a + 1;
                    idx[n++] = a + 1; idx[n++] = a + 2; idx[n++] = a + 3;
                }
                int last = b0 + (Rows - 1) * 2;
                idx[n++] = last; idx[n++] = b0 + Rows * 2; idx[n++] = last + 1;
            }
            var mesh = new Mesh { name = "GrassPatch", indexFormat = IndexFormat.UInt16 };
            mesh.vertices = pos;
            mesh.uv = uv;
            mesh.triangles = idx;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        // ------------------------------------------------------------------ flux autour des joueurs

        public void Stream(IList<Vector3> players)
        {
            if (!Ready || map == null || players == null || players.Count == 0) return;
            float start = Time.realtimeSinceStartup;
            float reach = endDistance + 4f;
            float keep = reach + ChunkSize * 1.5f, want = reach + ChunkSize * 0.5f;

            drop.Clear();
            foreach (var c in chunks.Keys) if (Nearest(c, players) > keep) drop.Add(c);
            foreach (var c in drop) { chunks.Remove(c); dirty = true; }
            drop.Clear();
            foreach (var c in empty) if (Nearest(c, players) > keep) drop.Add(c);
            foreach (var c in drop) empty.Remove(c);

            wanted.Clear();
            int r = Mathf.CeilToInt(want / ChunkSize);
            foreach (var p in players)
            {
                int cx = Mathf.FloorToInt(p.x / ChunkSize), cz = Mathf.FloorToInt(p.z / ChunkSize);
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var c = new Vector2Int(cx + dx, cz + dz);
                    if (chunks.ContainsKey(c) || empty.Contains(c) || wanted.Contains(c) || ChunkDistance(c, p) > want) continue;
                    if (Mathf.Abs((c.x + 0.5f) * ChunkSize) > map.Half || Mathf.Abs((c.y + 0.5f) * ChunkSize) > map.Half) continue;
                    wanted.Add(c);
                }
            }
            if (wanted.Count == 0) return;
            focus = players[0];
            wanted.Sort(byDistance);
            foreach (var c in wanted)
            {
                var chunk = Build(c);
                if (chunk == null) empty.Add(c);
                else { chunks[c] = chunk; dirty = true; }
                if ((Time.realtimeSinceStartup - start) * 1000f > budgetMs) break;
            }
        }

        /// <summary>Recrée tout le champ (réglage changé) : les cases reviennent au fil du flux.</summary>
        public void Rebuild()
        {
            chunks.Clear();
            empty.Clear();
            dirty = true;
        }

        float Nearest(Vector2Int c, IList<Vector3> players)
        {
            float d = float.MaxValue;
            foreach (var p in players) d = Mathf.Min(d, ChunkDistance(c, p));
            return d;
        }

        static float ChunkDistance(Vector2Int c, Vector3 p)
        {
            float x0 = c.x * ChunkSize, z0 = c.y * ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(x0 - p.x, p.x - (x0 + ChunkSize)));
            float dz = Mathf.Max(0f, Mathf.Max(z0 - p.z, p.z - (z0 + ChunkSize)));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float Perlin(float x, float z, float freq, float offset)
            => Mathf.PerlinNoise(x * freq + offset, z * freq - offset * 0.7f);

        // ------------------------------------------------------------------ lecture lissée de la carte

        int i00;
        float fu, fv;

        /// <summary>Prépare une lecture bilinéaire des nœuds de la grille autour d'un point.</summary>
        void Locate(float x, float z)
        {
            float fx = Mathf.Clamp((x + map.Half) / map.Step, 0f, map.N - 1.001f);
            float fz = Mathf.Clamp((z + map.Half) / map.Step, 0f, map.N - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            fu = fx - ix;
            fv = fz - iz;
            i00 = iz * map.N + ix;
        }

        float Lerp4(float a, float b, float c, float d) => Mathf.Lerp(Mathf.Lerp(a, b, fu), Mathf.Lerp(c, d, fu), fv);

        /// <summary>Octet d'un canal (Info ou Masks, 4 octets par nœud), lissé entre les nœuds (0-255).</summary>
        float Smooth(byte[] data, int ch)
        {
            int n = map.N;
            return Lerp4(data[i00 * 4 + ch], data[(i00 + 1) * 4 + ch], data[(i00 + n) * 4 + ch], data[(i00 + n + 1) * 4 + ch]);
        }

        /// <summary>Part des nœuds voisins dont l'octet « Info + 1 » a l'un des bits du masque (lissée, 0-1).</summary>
        float SmoothFlag(int mask)
        {
            int n = map.N;
            var info = map.Info;
            return Lerp4((info[i00 * 4 + 1] & mask) != 0 ? 1f : 0f, (info[(i00 + 1) * 4 + 1] & mask) != 0 ? 1f : 0f,
                         (info[(i00 + n) * 4 + 1] & mask) != 0 ? 1f : 0f, (info[(i00 + n + 1) * 4 + 1] & mask) != 0 ? 1f : 0f);
        }

        static float Smooth01(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>Herbe en un point : part de brins gardés, hauteur de base, part fauchée (gazon, accotement,
        /// parcelle fauchée), foin haut, herbe sèche, sous-bois.</summary>
        struct Sample { public float k, height, mown, hay, tone, dry, forest; }

        /// <summary>Octet d'un canal de la carte d'herbe, lissé entre les nœuds (0-255).</summary>
        float SmoothGrass(int ch)
        {
            int n = map.N;
            var g = grassMap;
            return Lerp4(g[i00 * 4 + ch], g[(i00 + 1) * 4 + ch], g[(i00 + n) * 4 + ch], g[(i00 + n + 1) * 4 + ch]);
        }

        /// <summary>Hauteur du milieu (m) lissée entre les nœuds, et part d'herbe entretenue (bit 7 du canal 1).</summary>
        float SmoothHeight(out float managed)
        {
            int n = map.N;
            var g = grassMap;
            byte a = g[i00 * 4 + 1], b = g[(i00 + 1) * 4 + 1], c = g[(i00 + n) * 4 + 1], d = g[(i00 + n + 1) * 4 + 1];
            managed = Lerp4(a >> 7, b >> 7, c >> 7, d >> 7);
            return Lerp4(a & 127, b & 127, c & 127, d & 127) * 0.01f;
        }

        static float Hash(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }

        const float ParcelSize = 46f;

        /// <summary>
        /// Parcelle d'un pré de fauche (Voronoï sur une grille de 46 m tirée au hasard) : graine de la parcelle et
        /// distance (m) à son bord le plus proche (bissectrice avec la parcelle voisine), bords droits comme un cadastre.
        /// </summary>
        static float Parcel(float x, float z, out float border)
        {
            int cx = Mathf.FloorToInt(x / ParcelSize), cz = Mathf.FloorToInt(z / ParcelSize);
            float bx = 0f, bz = 0f, best = float.MaxValue;
            int bi = 0, bk = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int i = cx + dx, k = cz + dz;
                float sx = (i + 0.15f + 0.7f * Hash(i, k, 11)) * ParcelSize, sz = (k + 0.15f + 0.7f * Hash(i, k, 12)) * ParcelSize;
                float d = (sx - x) * (sx - x) + (sz - z) * (sz - z);
                if (d < best) { best = d; bx = sx; bz = sz; bi = i; bk = k; }
            }
            border = float.MaxValue;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int i = bi + dx, k = bk + dz;
                if (dx == 0 && dz == 0) continue;
                float sx = (i + 0.15f + 0.7f * Hash(i, k, 11)) * ParcelSize, sz = (k + 0.15f + 0.7f * Hash(i, k, 12)) * ParcelSize;
                float ux = sx - bx, uz = sz - bz;
                float len = Mathf.Sqrt(ux * ux + uz * uz);
                if (len < 1e-3f) continue;
                float d = ((x - (bx + sx) * 0.5f) * ux + (z - (bz + sz) * 0.5f) * uz) / -len;
                border = Mathf.Min(border, d);
            }
            return Hash(bi, bk, 13);
        }

        /// <summary>
        /// Herbe en un point, continue : la carte d'herbe donne le milieu (pré, pâture, friche, sous-bois, alpage,
        /// gazon, accotement fauché) ; le bord de route et le pied des murs la coupent net, un bruit fin rend ces bords
        /// irréguliers ; les prés de fauche prennent la hauteur de leur parcelle.
        /// </summary>
        Sample At(float x, float z)
        {
            var s = new Sample();
            Locate(x, z);
            float jitter = Perlin(x, z, 0.55f, 31.7f) - 0.5f;
            float edge = Smooth(map.Info, 0) * 0.25f - 1f;
            // herbe jusqu'au pied de l'accotement (il plonge sous le terrain à 1,6 m du bord)
            float k = Smooth01(0.5f, 1.4f, edge + jitter * 0.7f);
            if (k <= 0f) return s;
            k *= 1f - Smooth01(0.2f, 0.55f, SmoothFlag(7 | 16) + jitter * 0.25f);   // eau, entrées de tunnel
            if (k <= 0f) return s;
            s.forest = Smooth(map.Masks, 0) / 255f;
            s.dry = Smooth(map.Masks, 1) / 255f;
            float field = 0f;
            if (grassMap != null)
            {
                float clear = SmoothGrass(3) / 8f - 2f;
                k *= Smooth01(0.05f, 0.45f, clear + jitter * 0.2f);              // pied des murs, aires de l'aéroport
                k *= SmoothGrass(0) / 255f;
                s.height = SmoothHeight(out s.mown);
                field = SmoothGrass(2) / 255f;
            }
            else
            {
                k *= 1f - Smooth01(0.12f, 0.3f, Smooth(map.Info, 2) / 255f);
                k *= 1f - Smooth01(0.16f, 0.3f, Smooth(map.Info, 3) / 255f);
                s.height = meadowHeight;
            }
            if (k <= 0f) return s;

            // Accotement fauché le long de toutes les routes, au plus fin (la carte d'herbe n'a qu'un nœud tous les 4 m)
            float verge = 1f - Smooth01(3f, 4.4f, edge + jitter * 0.8f);
            field *= 1f - verge;

            // Prés de fauche : chaque parcelle a son état ; bande non fauchée le long de ses bords
            if (field > 0.01f)
            {
                float seed = Parcel(x + jitter * 0.4f, z - jitter * 0.4f, out float border);
                float ph, pm, hay = 0f;
                int sk = (int)(seed * 9e5f);
                if (seed < 0.34f) { ph = 0.2f; pm = 1f; }                                   // fauchée
                else if (seed < 0.56f) { ph = 0.32f + 0.08f * Hash(sk, 1, 5); pm = 0.6f; }  // repousse
                else { ph = 0.92f + 0.24f * Hash(sk, 2, 5); pm = 0f; hay = 1f; }           // foin haut
                // chaque parcelle a sa nuance (le patchwork des fonds de vallée), les fauchées plus claires
                s.tone = ((seed < 0.34f ? 0.5f : 0f) + (Hash(sk, 3, 5) - 0.5f) * 1.2f) * field;
                if (pm > 0f)
                {
                    // bande non fauchée en lisière de parcelle
                    float keep = Smooth01(0.8f, 2.2f, border);
                    ph = Mathf.Lerp(0.66f, ph, keep);
                    pm *= keep;
                }
                s.height = Mathf.Lerp(s.height, ph, field);
                s.mown = Mathf.Lerp(s.mown, pm, field);
                s.hay = hay * field;
            }
            s.height = Mathf.Lerp(s.height, 0.18f, verge);
            s.mown = Mathf.Max(s.mown, verge);
            // Le pré sauvage varie un peu ; gazon et parcelles fauchées restent réguliers
            s.height *= Mathf.Lerp(0.88f + 0.24f * Perlin(x, z, 0.33f, 71.1f), 1f, s.mown);
            // Petites trouées dans les pâtures et les friches, pas dans les prés entretenus
            float wild = (1f - Mathf.Max(field, s.mown));
            if (wild > 0f)
            {
                float wx = x + (Perlin(x, z, 0.012f, 3.3f) - 0.5f) * 60f, wz = z + (Perlin(x, z, 0.012f, 8.1f) - 0.5f) * 60f;
                k *= Mathf.Lerp(1f, Mathf.Lerp(0.4f, 1f, Smooth01(0.12f, 0.24f, Perlin(wx, wz, 0.14f, 44.4f))), wild);
            }
            s.k = Mathf.Clamp01(k);
            return s;
        }

        /// <summary>
        /// Hauteur de l'herbe des prés en un point (m), 0 là où il n'en pousse pas ; plus basse là où le pré est
        /// clairsemé (comme dans le shader). Sert à GroundFlora pour que les fleurs dépassent de l'herbe.
        /// </summary>
        public float HeightAt(float x, float z)
        {
            if (map == null) return 0f;
            var s = At(x, z);
            if (s.k < 0.03f) return 0f;
            return s.height * Mathf.Lerp(0.55f, 1f, s.k);
        }

        /// <summary>Part fauchée de l'herbe en un point (0-1) : gazon, accotement, parcelle fauchée ; 0 sans herbe.</summary>
        public float MownAt(float x, float z)
        {
            if (map == null) return 0f;
            var s = At(x, z);
            return s.k < 0.03f ? 0f : s.mown;
        }

        /// <summary>Distance (m) au bâti le plus proche (murs, aires de l'aéroport), 30 au-delà : GroundFlora n'y
        /// pose pas de plantes.</summary>
        public float ClearAt(float x, float z)
        {
            if (map == null || grassMap == null) return 30f;
            Locate(x, z);
            return SmoothGrass(3) / 8f - 2f;
        }

        readonly Sample[] corners = new Sample[(PatchesPerSide + 1) * (PatchesPerSide + 1)];

        /// <summary>Carrés d'herbe d'une case de 16 m, ou null s'il n'y en a aucun.</summary>
        Chunk Build(Vector2Int c)
        {
            build.Clear();
            float x0 = c.x * ChunkSize, z0 = c.y * ChunkSize;
            const int C = PatchesPerSide + 1;
            bool any = false;
            for (int iz = 0; iz < C; iz++)
            for (int ix = 0; ix < C; ix++)
            {
                var s = At(x0 + ix * PatchSize, z0 + iz * PatchSize);
                corners[iz * C + ix] = s;
                any |= s.k > 0.03f;
            }
            if (!any) return null;

            float hMin = float.MaxValue, hMax = float.MinValue;
            for (int iz = 0; iz < PatchesPerSide; iz++)
            for (int ix = 0; ix < PatchesPerSide; ix++)
            {
                Sample s00 = corners[iz * C + ix], s10 = corners[iz * C + ix + 1], s01 = corners[(iz + 1) * C + ix], s11 = corners[(iz + 1) * C + ix + 1];
                var d = new Vector4(s00.k, s10.k, s01.k, s11.k);
                if (Mathf.Max(Mathf.Max(d.x, d.y), Mathf.Max(d.z, d.w)) < 0.03f) continue;
                float x = x0 + (ix + 0.5f) * PatchSize, z = z0 + (iz + 0.5f) * PatchSize;
                var mid = At(x, z);

                const float e = 1.5f;
                float gx = (map.Height(x + e, z) - map.Height(x - e, z)) / (2f * e);
                float gz = (map.Height(x, z + e) - map.Height(x, z - e)) / (2f * e);

                float y = map.Height(x, z);
                float seed = Mathf.Abs(Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f) % 1f;
                build.Add(new Patch
                {
                    a = new Vector4(x, y, z, seed),
                    b = new Vector4(gx, gz, mid.height, mid.mown),
                    c = new Vector4(mid.dry, mid.forest, mid.hay, mid.tone),
                    d = d,
                    e = new Vector4(s00.height, s10.height, s01.height, s11.height),
                });
                hMin = Mathf.Min(hMin, y);
                hMax = Mathf.Max(hMax, y);
            }
            if (build.Count == 0) return null;
            return new Chunk
            {
                patches = build.ToArray(),
                bounds = new Bounds(new Vector3(x0 + ChunkSize * 0.5f, (hMin + hMax) * 0.5f + 0.5f, z0 + ChunkSize * 0.5f),
                    new Vector3(ChunkSize + 3f, hMax - hMin + 3f, ChunkSize + 3f)),
            };
        }

        /// <summary>Outil de mise au point : carrés, part gardée et hauteur moyennes des cases autour d'un point.</summary>
        public string Describe(Vector3 at, int radius = 3)
        {
            var sb = new System.Text.StringBuilder();
            int cx = Mathf.FloorToInt(at.x / ChunkSize), cz = Mathf.FloorToInt(at.z / ChunkSize);
            sb.AppendLine($"chunks {chunks.Count} empty {empty.Count} patches drawn {Patches}");
            for (int dz = radius; dz >= -radius; dz--)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var k = new Vector2Int(cx + dx, cz + dz);
                    if (!chunks.TryGetValue(k, out var ch)) { sb.Append(empty.Contains(k) ? "   empty      " : "   ----       "); continue; }
                    float kk = 0f, hh = 0f;
                    foreach (var p in ch.patches) { kk += (p.d.x + p.d.y + p.d.z + p.d.w) * 0.25f; hh += p.b.z; }
                    int n = ch.patches.Length;
                    sb.Append($" {n,2} k{kk / n:0.00} h{hh / n:0.00}");
                }
                sb.AppendLine();
            }
            for (int i = 0; i < Levels.Length; i++) sb.Append($"L{i}:{buckets[i].count} ");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ tri et dessin

        /// <summary>Part des brins utiles à une distance (densité pleine jusqu'à fullDistance, puis éclaircie).</summary>
        float KeepAt(float d) => d <= fullDistance ? 1f : Mathf.Pow(fullDistance / d, thinning);

        void LateUpdate()
        {
            if (!Ready) return;
            var cam = Camera.main;
            if (!cam) return;
            UpdatePushers(cam.transform.position);
            if (NeedsSelect(cam)) Select(cam);
            Draw(cam.transform.position);
        }

        const float RebuildMove = 2f, RebuildTurn = 5f, Margin = 3.5f;

        bool NeedsSelect(Camera cam)
        {
            var t = cam.transform;
            float key = fullDistance * 1.3f + endDistance * 2.1f + thinning * 7.7f + density * 11.3f
                        + (VegetationRenderer.NoFrustumCull ? 1000f : 0f);
            if (!dirty && cam == lastCam && Mathf.Approximately(key, lastSettings)
                && (t.position - lastPos).sqrMagnitude < RebuildMove * RebuildMove && Quaternion.Angle(t.rotation, lastRot) < RebuildTurn)
                return false;
            dirty = false;
            lastCam = cam;
            lastSettings = key;
            lastPos = t.position;
            lastRot = t.rotation;
            return true;
        }

        void Select(Camera cam)
        {
            var proj = Matrix4x4.Perspective(Mathf.Min(cam.fieldOfView + 14f, 170f), cam.aspect * 1.15f, 0.1f, endDistance + 40f);
            GeometryUtility.CalculateFrustumPlanes(proj * cam.worldToCameraMatrix, planes);
            Vector3 cp = cam.transform.position;
            foreach (var s in sorted) s.Clear();
            float reach = endDistance + Margin;
            float reach2 = reach * reach;
            foreach (var ch in chunks.Values)
            {
                if (!VegetationRenderer.NoFrustumCull && !GeometryUtility.TestPlanesAABB(planes, ch.bounds)) continue;
                if (ch.bounds.SqrDistance(cp) > reach2) continue;
                foreach (var p in ch.patches)
                {
                    float dx = p.a.x - cp.x, dy = p.a.y - cp.y, dz = p.a.z - cp.z;
                    float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d > reach + 1.5f) continue;
                    // Brins utiles au point le plus proche du carré (avec la marge de déplacement entre deux tris)
                    float need = KeepAt(Mathf.Max(0f, d - 1.5f - Margin)) * density;
                    int level = 0;
                    while (level + 1 < Levels.Length && Levels[level + 1] >= need) level++;
                    sorted[level].Add(p);
                }
            }

            // Un tampon par lot (un décalage passé par MaterialPropertyBlock n'était pas pris en compte par le dessin
            // indirect : tous les lots lisaient le début du tampon commun)
            int total = 0;
            long tris = 0;
            for (int i = 0; i < Levels.Length; i++)
            {
                var b = buckets[i];
                b.count = sorted[i].Count;
                total += b.count;
                int blades = Mathf.Clamp(Mathf.CeilToInt(Levels[i] * BladesPerPatch), 1, BladesPerPatch);
                tris += (long)blades * (IndicesPerBlade / 3) * b.count;
                if (b.count == 0) continue;
                argsData[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = (uint)(blades * IndicesPerBlade),
                    instanceCount = (uint)b.count,
                    startIndex = 0, baseVertexIndex = 0, startInstance = 0,
                };
                b.args.SetData(argsData);
                if (b.patches == null || b.capacity < b.count)
                {
                    b.patches?.Release();
                    b.capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, b.count));
                    b.patches = new GraphicsBuffer(GraphicsBuffer.Target.Structured, b.capacity, PatchStride);
                    b.props.SetBuffer(PatchesId, b.patches);
                }
                b.patches.SetData(sorted[i], 0, 0, b.count);
            }
            Patches = total;
            Triangles = tris;
        }

        void Draw(Vector3 camPos)
        {
            if (Patches == 0) return;
            material.SetFloat(PatchSizeId, PatchSize);
            material.SetVector(RangeId, new Vector4(fullDistance, endDistance * 0.75f, endDistance, thinning));
            material.SetFloat(DensityId, density);
            var bounds = new Bounds(camPos, Vector3.one * (endDistance * 2f + 20f));
            foreach (var b in buckets)
            {
                if (b.count == 0 || b.patches == null) continue;
                var rp = new RenderParams(material)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                    worldBounds = bounds,
                    matProps = b.props,
                };
                Graphics.RenderMeshIndirect(rp, mesh, b.args, 1, 0);
            }
        }

        // ------------------------------------------------------------------ voitures qui couchent l'herbe

        readonly Vector4[] push = new Vector4[4];
        readonly Vector4[] pushInfo = new Vector4[4];
        readonly List<VehicleController> cars = new List<VehicleController>();
        float carsLookup;

        void UpdatePushers(Vector3 camPos)
        {
            if (Time.time > carsLookup)
            {
                carsLookup = Time.time + 1f;
                cars.Clear();
                cars.AddRange(FindObjectsByType<VehicleController>(FindObjectsSortMode.None));
            }
            cars.RemoveAll(v => !v);
            cars.Sort((a, b) => (a.transform.position - camPos).sqrMagnitude.CompareTo((b.transform.position - camPos).sqrMagnitude));
            for (int i = 0; i < 4; i++)
            {
                if (i < cars.Count && (cars[i].transform.position - camPos).sqrMagnitude < endDistance * endDistance)
                {
                    var t = cars[i].transform;
                    Vector3 f = t.forward;
                    f.y = 0f;
                    f = f.sqrMagnitude > 1e-4f ? f.normalized * 1.55f : Vector3.forward * 1.55f;
                    Vector3 a = t.position - f, b = t.position + f;
                    push[i] = new Vector4(a.x, a.z, b.x, b.z);
                    pushInfo[i] = new Vector4(t.position.y, 1.75f, 1f, 0f);
                }
                else
                {
                    push[i] = Vector4.zero;
                    pushInfo[i] = Vector4.zero;
                }
            }
            Shader.SetGlobalVectorArray(PushId, push);
            Shader.SetGlobalVectorArray(PushInfoId, pushInfo);
        }

        void OnDestroy()
        {
            foreach (var b in buckets)
            {
                b?.args?.Release();
                b?.patches?.Release();
            }
            if (material) Destroy(material);
            if (mesh) Destroy(mesh);
        }
    }
}
