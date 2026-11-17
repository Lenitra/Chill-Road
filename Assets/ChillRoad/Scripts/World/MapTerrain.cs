using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Terrain de la carte : tuiles de 256 m taillées dans la grille de hauteurs, à trois niveaux de détail selon la
    /// distance aux joueurs (4 m avec collision près d'eux, 8 m, puis 32 m pour les montagnes lointaines), plans
    /// d'eau, rivières, barrage, et nature dispersée d'après les masques de la carte (forêts, alpages, rochers).
    /// Toute la carte reste affichée : on voit les lacets de l'autre côté de la vallée.
    /// </summary>
    public class MapTerrain : MonoBehaviour
    {
        public const int TileCells = 64;
        static readonly int[] LodStep = { 1, 2, 8 };

        [Tooltip("Distance (m) du niveau fin (4 m, avec collision) et du niveau moyen (8 m)")]
        public float fineDistance = 330f, midDistance = 1500f;
        [Tooltip("Densité globale de la végétation")] public float density = 1f;

        class Tile
        {
            public Vector2Int key;
            public GameObject go;
            public MeshFilter filter;
            public MeshCollider collider;
            public readonly Mesh[] meshes = new Mesh[3];
            public int lod = -1;
            public Bounds bounds;
            public GameObject colliders;
            public readonly List<Vector4> trunks = new List<Vector4>();   // xyz + rayon : collisions des arbres proches des routes
        }

        MapData map;
        GameAssets assets;
        Transform root;
        Tile[] tiles;
        int side;
        float tileSize;
        public VegetationRenderer Vegetation { get; private set; }
        /// <summary>Herbes, fleurs, fougères et myrtilles autour des joueurs (carrés de 32 m générés à la volée).</summary>
        public GroundFlora Ground { get; private set; }
        public MapData Map => map;

        public void Init(GameAssets a, MapData m)
        {
            assets = a;
            map = m;
            root = new GameObject("Terrain").transform;
            root.SetParent(transform, false);
            Vegetation = gameObject.AddComponent<VegetationRenderer>();
            Vegetation.cullDistance = 5200f;
            // Les modèles détaillés coûtent cher : niveau complet tout près seulement (les arbres sont rangés par
            // cases de 64 m, voir Scatter.Add), niveau intermédiaire jusqu'à 170 m, silhouettes au-delà
            Vegetation.lod1Distance = 45f;
            Vegetation.lod2Distance = 170f;
            Ground = gameObject.AddComponent<GroundFlora>();
            Ground.density = density;
            Ground.Init(a, m, Vegetation);
            side = (map.N - 1) / TileCells;
            tileSize = TileCells * map.Step;
            tiles = new Tile[side * side];
            BuildSnowMap();
            gameObject.AddComponent<SnowTrails>().Init(m);
        }

        Texture2D snowMap;

        /// <summary>
        /// Carte de l'enneigement pour les shaders (routes enneigées, neige sur les toits et les sapins) : un texel
        /// pour deux nœuds de la grille (8 m), lue par position dans le monde.
        /// </summary>
        void BuildSnowMap()
        {
            int n = (map.N + 1) / 2;
            var px = new byte[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                px[z * n + x] = map.Info[map.Index(x * 2, z * 2) * 4 + 3];
            snowMap = new Texture2D(n, n, TextureFormat.R8, false, true) { name = "SnowMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            snowMap.SetPixelData(px, 0);
            snowMap.Apply(false, true);
            float size = (n - 1) * map.Step * 2f;
            // centres des texels sur les nœuds : décalage d'un demi-texel
            float texel = size / (n - 1);
            Shader.SetGlobalTexture("_ChillRoadSnowMap", snowMap);
            Shader.SetGlobalVector("_ChillRoadSnowRect", new Vector4(-map.Half - texel * 0.5f, -map.Half - texel * 0.5f, 1f / (size + texel), 0f));
            Shader.SetGlobalFloat("_ChillRoadSnowOn", 1f);
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat("_ChillRoadSnowOn", 0f);
            if (snowMap) Destroy(snowMap);
        }

        /// <summary>Construit le terrain lointain de toute la carte, l'eau et la nature (écran de chargement).</summary>
        public IEnumerator Build(System.Action<float> progress)
        {
            float last = Time.realtimeSinceStartup;
            for (int tz = 0; tz < side; tz++)
            {
                for (int tx = 0; tx < side; tx++)
                {
                    var t = CreateTile(tx, tz);
                    SetLod(t, 2);
                    new Scatter(this, t).Trees();
                }
                if (Time.realtimeSinceStartup - last > 0.03f)
                {
                    progress?.Invoke((tz + 1f) / side * 0.9f);
                    yield return null;
                    last = Time.realtimeSinceStartup;
                }
            }
            BuildWater();
            BuildRivers();
            BuildDam();
            progress?.Invoke(1f);
        }

        Tile CreateTile(int tx, int tz)
        {
            var t = new Tile { key = new Vector2Int(tx, tz) };
            t.go = new GameObject($"Tile_{tx}_{tz}");
            t.go.transform.SetParent(root, false);
            t.go.transform.position = new Vector3(-map.Half + tx * tileSize, 0f, -map.Half + tz * tileSize);
            t.filter = t.go.AddComponent<MeshFilter>();
            t.go.AddComponent<MeshRenderer>().sharedMaterial = assets.terrain;
            float min = float.MaxValue, max = float.MinValue;
            for (int z = 0; z <= TileCells; z += 4)
            for (int x = 0; x <= TileCells; x += 4)
            {
                float h = map.H[map.Index(tx * TileCells + x, tz * TileCells + z)];
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            t.bounds = new Bounds(t.go.transform.position + new Vector3(tileSize * 0.5f, (min + max) * 0.5f + 10f, tileSize * 0.5f),
                new Vector3(tileSize + 30f, max - min + 60f, tileSize + 30f));
            tiles[tz * side + tx] = t;
            return t;
        }

        /// <summary>
        /// Ajuste le niveau de détail des tuiles autour des joueurs ; construit au plus maxBuilds maillages fins par appel.
        /// Renvoie le nombre de tuiles fines encore à construire.
        /// </summary>
        public int UpdateAround(IList<Vector3> players, int maxBuilds = 1)
        {
            int pending = 0, built = 0;
            foreach (var t in tiles)
            {
                float d = float.MaxValue;
                foreach (var p in players) d = Mathf.Min(d, Mathf.Sqrt(t.bounds.SqrDistance(new Vector3(p.x, t.bounds.center.y, p.z))));
                // Hystérésis : une tuile fine le reste un peu plus loin
                int want = d < (t.lod == 0 ? fineDistance + 90f : fineDistance) ? 0 : d < (t.lod <= 1 ? midDistance + 150f : midDistance) ? 1 : 2;
                if (want == t.lod) continue;
                if (want < t.lod && t.meshes[want] == null)
                {
                    if (built >= maxBuilds) { if (want == 0) pending++; continue; }
                    built++;
                }
                SetLod(t, want);
            }
            if (Ground) Ground.Stream(players);
            return pending;
        }

        void SetLod(Tile t, int lod)
        {
            if (t.meshes[lod] == null) t.meshes[lod] = BuildMesh(t, lod);
            t.filter.sharedMesh = t.meshes[lod];
            if (lod == 0)
            {
                if (!t.collider) t.collider = t.go.AddComponent<MeshCollider>();
                t.collider.sharedMesh = t.meshes[0];
                t.collider.enabled = true;
                if (!t.colliders) BuildTrunkColliders(t);
            }
            else
            {
                if (t.collider) t.collider.enabled = false;
                if (t.lod == 0)
                {
                    // Le maillage fin est libéré quand on s'éloigne (mémoire)
                    if (t.colliders) Destroy(t.colliders);
                    if (lod == 2 && t.meshes[0] != null)
                    {
                        if (t.collider) t.collider.sharedMesh = null;
                        Destroy(t.meshes[0]);
                        t.meshes[0] = null;
                    }
                }
            }
            t.lod = lod;
        }

        Mesh BuildMesh(Tile t, int lod)
        {
            int k = LodStep[lod];
            int cells = TileCells / k, n = cells + 1;
            int ox = t.key.x * TileCells, oz = t.key.y * TileCells;
            int count = n * n + 4 * n;
            var verts = new Vector3[count];
            var colors = new Color32[count];
            var extra = new Vector2[count];
            float step = map.Step * k;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int gx = ox + x * k, gz = oz + z * k;
                int gi = map.Index(gx, gz);
                float h = map.H[gi];
                // Niveaux grossiers : près des routes, on garde le point le plus bas du voisinage pour que le relief
                // simplifié ne recouvre jamais la chaussée
                if (k > 1 && map.Info[gi * 4] < (k * 4 + 8) * 4)
                {
                    int r = k / 2;
                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        h = Mathf.Min(h, map.H[map.Index(gx + dx, gz + dz)]);
                }
                int i = z * n + x;
                verts[i] = new Vector3(x * step, h, z * step);
                colors[i] = new Color32(map.Masks[gi * 4], map.Masks[gi * 4 + 1], map.Masks[gi * 4 + 2], map.Masks[gi * 4 + 3]);
                extra[i] = new Vector2(map.Info[gi * 4 + 2] / 255f, map.Info[gi * 4 + 3] / 255f);
            }
            var tris = new List<int>(cells * cells * 6 + cells * 24);
            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                // Entrées de tunnel : les mailles marquées ne sont pas dessinées (ni en collision) ; au loin, le relief
                // simplifié reste plein
                if (lod < 2 && HoleQuad(ox + x * k, oz + z * k, k)) continue;
                int i = z * n + x;
                tris.Add(i); tris.Add(i + n); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(i + n); tris.Add(i + n + 1);
            }
            // Jupes : cachent les fentes entre deux tuiles de niveaux différents
            float drop = 4f + step * 1.5f;
            int s = n * n;
            for (int e = 0; e < 4; e++)
            for (int j = 0; j < n; j++)
            {
                int src = e == 0 ? j : e == 1 ? (n - 1) * n + j : e == 2 ? j * n : j * n + n - 1;
                int dst = s + e * n + j;
                // Au-dessus d'un tunnel, pas de jupe : elle descendrait à travers le tube
                bool tunnel = map.OverTunnel(ox + src % n * k, oz + src / n * k, k);
                verts[dst] = verts[src] - Vector3.up * (tunnel ? 0.05f : drop);
                colors[dst] = colors[src];
                extra[dst] = extra[src];
                if (j == 0) continue;
                int a = e == 0 ? j - 1 : e == 1 ? (n - 1) * n + j - 1 : e == 2 ? (j - 1) * n : (j - 1) * n + n - 1;
                int b = src, c = dst - 1, d = dst;
                // Deux faces : visibles de quelque côté que s'ouvre la fente
                tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
            }
            var mesh = new Mesh { name = $"{t.go.name}_L{lod}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.colors32 = colors;
            mesh.uv2 = extra;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Une des mailles fines couvertes par ce carré (k × k) est-elle un trou du terrain ?</summary>
        bool HoleQuad(int gx, int gz, int k)
        {
            for (int dz = 0; dz < k; dz++)
            for (int dx = 0; dx < k; dx++)
                if (map.Hole(gx + dx, gz + dz)) return true;
            return false;
        }

        void BuildTrunkColliders(Tile t)
        {
            t.colliders = new GameObject("Colliders");
            t.colliders.transform.SetParent(t.go.transform, false);
            foreach (var trunk in t.trunks)
            {
                var c = new GameObject("Arbre (collision)");
                c.transform.SetParent(t.colliders.transform, false);
                c.transform.position = new Vector3(trunk.x, trunk.y + 2f, trunk.z);
                var cap = c.AddComponent<CapsuleCollider>();
                cap.radius = trunk.w;
                cap.height = 5f;
            }
        }

        // ------------------------------------------------------------------ eau, rivières, barrage

        /// <summary>Profondeur au-delà de laquelle l'eau d'un lac garde la même couleur (m), profondeur au fil d'une rivière (m).</summary>
        const float WaterDeep = 24f, RiverDepth = 3f;

        /// <summary>Plans d'eau : une nappe par lac, découpée en bandes sur les cellules marquées dans la carte.</summary>
        void BuildWater()
        {
            if (!assets.water) return;
            var waters = map.Json.waters;
            for (int w = 0; w < waters.Length; w++)
            {
                var lk = waters[w];
                int id = w + 1;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((lk.x - lk.r + map.Half) / map.Step), 1, map.N - 2);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((lk.x + lk.r + map.Half) / map.Step), 1, map.N - 2);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lk.z - lk.r + map.Half) / map.Step), 1, map.N - 2);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((lk.z + lk.r + map.Half) / map.Step), 1, map.N - 2);
                var verts = new List<Vector3>();
                var depths = new List<Vector2>();
                var flow = new List<Vector2>();
                var tris = new List<int>();
                // Profondeur aux nœuds (UV0.x, plafonnée) : le shader y lit la couleur de l'eau et l'écume de rive.
                // Même découpage en triangles que le terrain : la ligne de rive tombe exactement sur le sol.
                float Depth(int x, int z) => Mathf.Min(lk.level - map.H[map.Index(x, z)], WaterDeep);
                void Strip(int xa, int xb, int z)
                {
                    int s = verts.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        int gx = (k & 1) == 0 ? xa : xb, gz = z + (k >> 1);
                        var p = new Vector3(-map.Half + gx * map.Step, lk.level, -map.Half + gz * map.Step);
                        verts.Add(p);
                        depths.Add(new Vector2(Depth(gx, gz), 0f));
                        flow.Add(new Vector2(p.x, p.z));
                    }
                    tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                    tris.Add(s + 1); tris.Add(s + 2); tris.Add(s + 3);
                }
                for (int z = z0; z < z1; z++)
                {
                    // Le large (profondeur plafonnée aux quatre coins) part en longues bandes, la rive maille par maille
                    int run = -1;
                    for (int x = x0; x <= x1; x++)
                    {
                        // Cellule mouillée si l'un de ses coins ou de ses voisins l'est : la nappe passe sous la rive
                        bool wet = false;
                        if (x < x1)
                            for (int dz = -1; dz <= 2 && !wet; dz++)
                            for (int dx = -1; dx <= 2 && !wet; dx++)
                                wet = (map.Info[map.Index(x + dx, z + dz) * 4 + 1] & 7) == id;
                        bool deep = wet && Depth(x, z) >= WaterDeep && Depth(x + 1, z) >= WaterDeep
                            && Depth(x, z + 1) >= WaterDeep && Depth(x + 1, z + 1) >= WaterDeep;
                        if (!deep && run >= 0) { Strip(run, x, z); run = -1; }
                        if (deep && run < 0) run = x;
                        if (wet && !deep) Strip(x, x + 1, z);
                    }
                }
                if (verts.Count == 0) continue;
                var mesh = new Mesh { name = "Water_" + lk.id, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, depths);
                mesh.SetUVs(1, flow);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var go = new GameObject("Water_" + lk.id);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.water;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Rivières et torrents : rubans d'eau qui suivent le lit creusé dans le relief.</summary>
        void BuildRivers()
        {
            if (!assets.water || map.Json.rivers == null) return;
            foreach (var rv in map.Json.rivers)
            {
                int count = rv.pts.Length / 3;
                if (count < 2) continue;
                float hw = rv.width * 0.5f + 1.2f;
                Vector3 P(int i) => new Vector3(rv.pts[i * 3], rv.pts[i * 3 + 1], rv.pts[i * 3 + 2]);
                Vector3 Right(int i)
                {
                    Vector3 d = P(Mathf.Min(i + 1, count - 1)) - P(Mathf.Max(i - 1, 0));
                    d.y = 0f;
                    d.Normalize();
                    return new Vector3(d.z, 0f, -d.x);
                }
                // Trois sommets par section (berge, fil de l'eau, berge). UV0 = (profondeur, 1 = rivière) : nulle aux
                // berges, d'où l'écume ; UV1 = (travers, longueur le long du cours) en mètres : le motif suit le courant.
                var verts = new List<Vector3>();
                var depths = new List<Vector2>();
                var flow = new List<Vector2>();
                var tris = new List<int>();
                float along = 0f;
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = P(i), r = Right(i);
                    if (i > 0) along += Vector3.Distance(P(i - 1), p);
                    for (int k = -1; k <= 1; k++)
                    {
                        verts.Add(p + r * (hw * k));
                        depths.Add(new Vector2(k == 0 ? RiverDepth : 0f, 1f));
                        flow.Add(new Vector2(hw * k, along));
                    }
                    if (i == 0) continue;
                    int s = (i - 1) * 3;
                    for (int k = 0; k < 2; k++, s++)
                    {
                        tris.Add(s); tris.Add(s + 3); tris.Add(s + 1);
                        tris.Add(s + 1); tris.Add(s + 3); tris.Add(s + 4);
                    }
                }
                var mesh = new Mesh { name = "River_" + rv.id };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, depths);
                mesh.SetUVs(1, flow);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var go = RoadNetwork.AddMesh(root.gameObject, "River_" + rv.id, mesh, assets.water, false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Barrage : mur de béton en travers de la vallée, parement aval incliné ; la route passe sur la crête.</summary>
        void BuildDam()
        {
            var d = map.Json.dam;
            if (d == null || d.crest == 0f) return;
            Vector3 a = new Vector3(d.ax, 0f, d.az), b = new Vector3(d.bx, 0f, d.bz);
            Vector3 axis = (b - a).normalized;
            a -= axis * 8f;
            b += axis * 8f;
            Vector3 n = new Vector3(axis.z, 0f, -axis.x);
            // Côté amont : celui de la retenue
            Vector3 mid = (a + b) * 0.5f;
            foreach (var w in map.Json.waters)
                if (Mathf.Abs(w.level - d.crest) < 8f && Vector3.Dot(new Vector3(w.x, 0f, w.z) - mid, n) < 0f) n = -n;
            float top = d.crest - 0.06f, depth = 70f, half = d.width * 0.5f + 1.2f;
            var mb = new RoadNetwork.MeshBuilder();
            // Profil (décalage vers l'amont, hauteur) : parement amont vertical, crête, parement aval incliné
            var profile = new[] { new Vector2(half, top - depth), new Vector2(half, top), new Vector2(-half, top), new Vector2(-half - depth * 0.55f, top - depth) };
            for (int k = 0; k < profile.Length - 1; k++)
            {
                Vector3 p0 = n * profile[k].x + Vector3.up * profile[k].y, p1 = n * profile[k + 1].x + Vector3.up * profile[k + 1].y;
                // Deux faces : le mur se voit de l'amont comme de l'aval, quel que soit le sens de l'axe
                mb.Quad(a + p1, a + p0, b + p1, b + p0);
                mb.Quad(a + p0, a + p1, b + p0, b + p1);
            }
            RoadNetwork.AddMesh(root.gameObject, "Dam", mb.Build("Dam"), assets.concrete, true);
        }

        // ------------------------------------------------------------------ dispersion de la nature

        /// <summary>Modèles de la nature, pris dans le pack alpin et complétés par le premier pack d'environnement.</summary>
        class Flora
        {
            public GameObject[] spruces, larches, highTrees, valleyTrees, dead, shrubs, alpineShrubs, boulders, bigRocks, cliffs, scree;
            public GameObject[] cows, sheep, wild, snow;

            public Flora(GameAssets a)
            {
                var p = a.alp;
                GameObject[] Get(params string[] ids) => p ? p.Prefabs(ids) : new GameObject[0];
                spruces = Get("spruce_a", "spruce_b", "spruce_c", "spruce_a", "spruce_b", "spruce_young");
                larches = Get("larch_a", "larch_b");
                highTrees = Get("swisspine_a", "swisspine_b", "larch_a", "larch_b", "spruce_young");
                valleyTrees = Get("deciduous_a", "deciduous_b", "deciduous_c", "birch_a", "birch_b", "poplar", "willow");
                dead = Get("dead_spruce");
                shrubs = Get("juniper", "bush_rhodo");
                alpineShrubs = Get("bush_rhodo", "juniper");
                boulders = Get("boulder_a", "boulder_b", "boulder_c", "rock_slab");
                bigRocks = Get("boulder_c", "rock_slab");
                cliffs = Get("cliff_a", "cliff_b", "cliff_c");
                scree = Get("scree");
                cows = Get("cow_a", "cow_b", "cow_lying", "cow_a", "cow_b");
                sheep = Get("sheep", "goat");
                wild = Get("ibex", "marmot");
                snow = Get("snow_patch");
            }
        }

        Flora flora;

        class Scatter
        {
            readonly MapTerrain mt;
            readonly MapData map;
            readonly Tile tile;
            readonly System.Random rng;
            readonly Flora f;
            readonly Vector3 origin;
            readonly float size;

            public Scatter(MapTerrain terrain, Tile t)
            {
                mt = terrain;
                map = terrain.map;
                tile = t;
                rng = new System.Random(7919 ^ (t.key.x * 73856093) ^ (t.key.y * 19349663));
                f = mt.flora ?? (mt.flora = new Flora(mt.assets));
                origin = t.go.transform.position;
                size = mt.tileSize;
            }

            float R() => (float)rng.NextDouble();
            float R(float a, float b) => Mathf.Lerp(a, b, R());
            GameObject Pick(GameObject[] arr) => arr == null || arr.Length == 0 ? null : arr[rng.Next(arr.Length)];

            /// <summary>Arbres, buissons, rochers et troupeaux : enregistrés une fois pour toute la partie.</summary>
            public void Trees()
            {
                float dens = mt.density;
                const float cell = 8f;
                int cells = Mathf.RoundToInt(size / cell);
                // Hors de la zone jouable, les montagnes de bordure n'ont pas besoin d'arbres un par un : un sur deux
                Vector3 c = tile.bounds.center;
                float far = Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.z)) > map.Half - 500f ? 0.5f : 1f;
                for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    float x = origin.x + (cx + R()) * cell, z = origin.z + (cz + R()) * cell;
                    float roll = R(), roll2 = R();
                    int gi = map.Nearest(x, z);
                    float edge = map.Info[gi * 4] * 0.25f - 1f;
                    int flags = map.Info[gi * 4 + 1];
                    if (edge < 5f || (flags & 31) != 0 || map.Info[gi * 4 + 2] > 70) continue;
                    float forest = map.Masks[gi * 4] / 255f, dry = map.Masks[gi * 4 + 1] / 255f, rock = map.Masks[gi * 4 + 2] / 255f;
                    float snow = map.Info[gi * 4 + 3] / 255f;
                    float h = map.H[gi];
                    Vector3 p = new Vector3(x, map.Height(x, z), z);
                    bool collide = edge < 22f;
                    if (rock > 0.55f)
                    {
                        // Éboulis et blocs au pied des parois
                        if (roll < 0.05f * far) Add(Pick(f.boulders), p, R(0.8f, 2.4f), collide ? 1.2f : 0f);
                        else if (roll < 0.07f * far && snow < 0.3f) Add(Pick(f.scree), p, R(1f, 2f), 0f);
                        else if (roll < 0.075f && snow > 0.5f) Add(Pick(f.snow), p, R(1.5f, 4f), 0f);
                        continue;
                    }
                    if (forest > 0.5f)
                    {
                        if (roll < 0.8f * forest * dens * far)
                        {
                            bool high = h > 330f + roll2 * 120f;
                            var tree = roll2 < 0.04f ? Pick(f.dead) : high ? Pick(f.highTrees) : roll2 < 0.22f ? Pick(f.larches) : Pick(f.spruces);
                            Add(tree, p, R(0.75f, 1.25f), collide ? 0.45f : 0f);
                        }
                        continue;
                    }
                    if (forest > 0.18f)
                    {
                        // Lisière : arbres épars, buissons
                        if (roll < 0.22f * dens * far) Add(h > 360f ? Pick(f.highTrees) : roll2 < 0.5f ? Pick(f.larches) : Pick(f.spruces), p, R(0.7f, 1.15f), collide ? 0.45f : 0f);
                        else if (roll < 0.3f * far && snow < 0.4f) Add(Pick(f.shrubs), p, R(0.8f, 1.5f), 0f);
                        continue;
                    }
                    if (dry > 0.55f)
                    {
                        // Pelouse alpine : rhododendrons, genévriers, blocs épars
                        if (roll < 0.03f * far && snow < 0.4f) Add(Pick(f.alpineShrubs), p, R(0.8f, 1.6f), 0f);
                        else if (roll < 0.045f * far) Add(Pick(f.boulders), p, R(0.5f, 1.6f), collide ? 0.9f : 0f);
                        continue;
                    }
                    // Prés du fond de vallée et des replats : feuillus isolés, haies
                    if (roll < 0.012f * dens) Add(h < 150f ? Pick(f.valleyTrees) : snow > 0.4f ? Pick(f.spruces) : Pick(f.larches), p, R(0.8f, 1.2f), collide ? 0.5f : 0f);
                    else if (roll < 0.02f && snow < 0.4f) Add(Pick(f.shrubs), p, R(0.7f, 1.3f), 0f);
                }
                Herds();
            }

            /// <summary>Troupeaux : quelques vaches (ou moutons) regroupés sur les prés peu pentus, loin des routes.</summary>
            void Herds()
            {
                for (int k = 0; k < 2; k++)
                {
                    float x = origin.x + R(30f, size - 30f), z = origin.z + R(30f, size - 30f);
                    if (R() > 0.3f) continue;
                    if (map.Forest(x, z) > 0.15f || map.Rock(x, z) > 0.3f || map.Slope(x, z) > 0.28f || map.Water(x, z) != 0
                        || map.RoadEdge(x, z) < 25f || map.Paved(x, z) > 0.1f || map.Snow(x, z) > 0.3f || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > map.Half - 700f) continue;
                    bool high = map.Dry(x, z) > 0.6f;
                    var herd = high ? f.sheep : f.cows;
                    int count = rng.Next(4, 10);
                    for (int i = 0; i < count; i++)
                    {
                        float px = x + R(-22f, 22f), pz = z + R(-22f, 22f);
                        if (map.Blocked(px, pz) || map.RoadEdge(px, pz) < 12f || map.Water(px, pz) != 0) continue;
                        Add(Pick(herd), new Vector3(px, map.Height(px, pz), pz), R(0.92f, 1.08f), 0f, 0.02f);
                    }
                }
            }

            /// <summary>Objet principal : ses 3 niveaux de détail sont enregistrés séparément. Collision optionnelle (rayon).</summary>
            void Add(GameObject prefab, Vector3 pos, float scale, float colliderRadius, float sink = 0.15f)
            {
                if (!prefab) return;
                Matrix4x4 place = Matrix4x4.TRS(pos - Vector3.up * sink, Quaternion.Euler(0f, R() * 360f, 0f), Vector3.one * scale);
                // Case de 64 m : le niveau de détail se choisit par case, pas par tuile de 256 m (sinon des centaines
                // d'arbres détaillés s'affichent d'un coup autour du joueur)
                int cx = Mathf.FloorToInt(pos.x / TreeCell), cz = Mathf.FloorToInt(pos.z / TreeCell);
                var key = new Vector2Int(cx + 500000, cz);
                var bounds = new Bounds(new Vector3((cx + 0.5f) * TreeCell, tile.bounds.center.y, (cz + 0.5f) * TreeCell),
                    new Vector3(TreeCell + 24f, tile.bounds.size.y, TreeCell + 24f));
                var group = prefab.GetComponentInChildren<LODGroup>();
                if (group)
                {
                    var levels = group.GetLODs();
                    for (int l = 0; l < 3; l++)
                        foreach (var r in levels[Mathf.Min(l, levels.Length - 1)].renderers) Collect(key, bounds, l, r, prefab, place);
                }
                else
                    foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
                        for (int l = 0; l < 3; l++) Collect(key, bounds, l, r, prefab, place);
                if (colliderRadius > 0f) tile.trunks.Add(new Vector4(pos.x, pos.y, pos.z, colliderRadius * scale));
            }

            const float TreeCell = 64f;

            void Collect(Vector2Int key, Bounds bounds, int level, Renderer r, GameObject prefab, Matrix4x4 place)
            {
                if (!r) return;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) return;
                Matrix4x4 world = place * (prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix);
                var mats = r.sharedMaterials;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount && sub < mats.Length; sub++)
                {
                    if (!mats[sub]) continue;
                    var k = new VegetationRenderer.BatchKey { mesh = mf.sharedMesh, submesh = sub, material = mats[sub], lod = level };
                    mt.Vegetation.Add(key, bounds, k, world);
                }
            }
        }
    }
}
