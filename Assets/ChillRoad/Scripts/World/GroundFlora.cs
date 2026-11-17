using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Végétation au sol autour des joueurs : herbes, fleurs, trèfle, fougères, myrtilles (pack alpin, modèles « Flora »).
    /// Deux couches générées à la volée et dessinées en instancing par <see cref="VegetationRenderer"/> :
    /// - couche de fond : carrés de 32 m jusqu'à ~260 m, en trois paliers (tout de près, une partie à mi-distance,
    ///   une petite part au loin) ;
    /// - tapis dense : carrés de 16 m jusqu'à ~70 m, quatre à cinq fois plus serré (herbe rase, trèfle, petites fleurs),
    ///   modèles détaillés tout près puis moyens.
    /// Le shader ChillRoad/Foliage fait rétrécir chaque plante vers son pied avant qu'elle ne quitte son palier, et fait
    /// onduler le pré au vent (rafales qui le traversent, plus fortes sous l'orage). Les fleurs poussent en dérives
    /// (marguerites, lupins, trèfle, gentianes) plutôt qu'au hasard uniforme.
    /// Raccord avec le champ d'herbe (<see cref="GrassField"/>) : les matériaux des plantes reprennent les couleurs
    /// du terrain et les réglages de l'herbe (<see cref="MatchField"/>), et chaque plante porte la normale du terrain
    /// dans l'axe Y de sa matrice : le shader la redresse et l'éclaire comme le pré.
    /// </summary>
    public class GroundFlora : MonoBehaviour
    {
        [Tooltip("Couche de fond : fin des paliers tout / partie moyenne / part lointaine (m)")]
        public float nearDistance = 20f, midDistance = 110f, farDistance = 230f;
        [Tooltip("Tapis dense : fin des modèles détaillés, puis fin du tapis (m)")]
        public float denseNear = 14f, denseFar = 62f;
        [Tooltip("Largeur du rétrécissement avant disparition (m)")] public float fadeWidth = 14f;
        [Tooltip("Densité (1 = normale)")] public float density = 1f;
        [Tooltip("Tapis dense près du joueur")] public bool denseCarpet = true;
        [Tooltip("Temps de génération max par image (ms)")] public float budgetMs = 2.5f;
        [Tooltip("Vent : vitesse de défilement des rafales")] public float windSpeed = 1f;

        // Couche de fond : part des plantes dessinées jusqu'au loin, puis jusqu'à mi-distance ; le reste de près
        const float BandFar = 0.3f, BandMid = 0.62f;
        // Copies du matériau par palier (distance de disparition) : loin, mi-distance, près, tapis dense
        const int MatFar = 0, MatMid = 1, MatNear = 2, MatDense = 3;

        static readonly int WindId = Shader.PropertyToID("_ChillRoadWind");
        static readonly int WindScrollId = Shader.PropertyToID("_ChillRoadWindScroll");
        static readonly int FadeId = Shader.PropertyToID("_ChillRoadFloraFade");
        static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
        static readonly int SlopeLitId = Shader.PropertyToID("_SlopeLit");

        // Réglages repris du terrain (couleurs du pré, aplats cartoon) et de l'herbe (dégradé des brins, vent)
        static readonly string[] TerrainColors = { "_Grass", "_GrassLush", "_ShadeColor" };
        static readonly string[] TerrainFloats = { "_NoiseScale", "_ShadeLevel", "_RampSoft" };
        static readonly string[] FieldColors = { "_RootColor", "_TipColor", "_TransColor" };
        static readonly string[] FieldFloats = { "_TipAmount", "_TipBright", "_Saturation", "_GustBright", "_Translucency",
                                                 "_WindScale", "_WindStrength", "_WindFlutter", "_Comb" };

        /// <summary>
        /// Raccorde un matériau ChillRoad/Foliage au pré : couleurs et aplats du terrain, dégradé et vent de l'herbe
        /// (Resources/GrassField). Sans effet sur les propriétés absentes d'un côté ou de l'autre.
        /// </summary>
        public static void MatchField(Material m, Material terrain, Material field)
        {
            if (!m) return;
            Copy(terrain, m, TerrainColors, TerrainFloats);
            Copy(field, m, FieldColors, FieldFloats);
            if (terrain && terrain.HasProperty("_Noise") && terrain.GetTexture("_Noise") && m.HasProperty("_Noise"))
                m.SetTexture("_Noise", terrain.GetTexture("_Noise"));
        }

        static void Copy(Material from, Material to, string[] colors, string[] floats)
        {
            if (!from) return;
            foreach (var p in colors) if (from.HasProperty(p) && to.HasProperty(p)) to.SetColor(p, from.GetColor(p));
            foreach (var p in floats) if (from.HasProperty(p) && to.HasProperty(p)) to.SetFloat(p, from.GetFloat(p));
        }

        class Part { public Mesh mesh; public int submesh; public Material[] mats; public Matrix4x4 local; }
        class Kind { public string id; public Part[][] lods; public float scaleMin = 0.8f, scaleMax = 1.25f, rise, riseMax = 1.5f; }

        /// <summary>Hauteur d'herbe (m) à partir de laquelle la plante grandit pour garder ses fleurs au-dessus des brins.</summary>
        static void Rise(Kind k, float height, float max = 1.5f)
        {
            if (k == null) return;
            k.rise = height;
            k.riseMax = max;
        }

        class Layer
        {
            public bool dense;
            public float chunk, cell;
            public int keyOffset;
            public readonly HashSet<Vector2Int> live = new HashSet<Vector2Int>();
            public Vector2Int Key(Vector2Int c) => new Vector2Int(c.x + keyOffset, c.y);
        }

        readonly Layer baseLayer = new Layer { dense = false, chunk = 32f, cell = 1.1f, keyOffset = 300000 };
        readonly Layer denseLayer = new Layer { dense = true, chunk = 16f, cell = 0.5f, keyOffset = 400000 };

        MapData map;
        VegetationRenderer veg;
        Kind grassShort, grassTall, grassDry, meadow, daisy, lupin, gentian, edelweiss, clover, fern, fernYoung, blueberry;
        readonly Dictionary<Material, Material[]> variants = new Dictionary<Material, Material[]>();
        readonly HashSet<Vector2Int> queued = new HashSet<Vector2Int>();
        readonly List<Vector2Int> wanted = new List<Vector2Int>();
        readonly List<Vector2Int> drop = new List<Vector2Int>();
        readonly Dictionary<VegetationRenderer.BatchKey, List<Matrix4x4>> scratch = new Dictionary<VegetationRenderer.BatchKey, List<Matrix4x4>>();
        readonly Kind[] pick = new Kind[12];
        readonly float[] weight = new float[12];
        int picks;
        Vector3 focus;
        float focusChunk;
        System.Comparison<Vector2Int> byDistance;
        WeatherSystem weather;
        float weatherLookup;
        Vector2 windScroll;
        GrassField grass;
        Material terrainMat, fieldMat;
        Kind buttercup, poppy;

        /// <summary>Herbe des prés (champ de brins, shader ChillRoad/Grass) : remplace les touffes d'herbe du pack.</summary>
        public GrassField Grass => grass;
        bool FieldOn => grass && grass.Ready;

        public void Init(GameAssets assets, MapData m, VegetationRenderer renderer)
        {
            map = m;
            veg = renderer;
            byDistance = (a, b) => ChunkDistance(a, focus, focusChunk).CompareTo(ChunkDistance(b, focus, focusChunk));
            var pack = assets ? assets.alp : null;
            terrainMat = assets ? assets.terrain : null;
            fieldMat = Resources.Load<Material>("GrassField");
            Kind K(string id, float s0 = 0.8f, float s1 = 1.25f)
            {
                var prefab = pack ? pack.Prefab(id) : null;
                return prefab ? MakeKind(id, prefab, s0, s1) : null;
            }
            grassShort = K("grass_short", 0.85f, 1.3f);
            grassTall = K("grass_tall");
            grassDry = K("grass_dry");
            meadow = K("flowers_meadow", 0.9f, 1.2f);
            daisy = K("flowers_daisy", 0.9f, 1.2f);
            lupin = K("flowers_lupin", 0.85f, 1.2f);
            gentian = K("flowers_gentian", 0.9f, 1.3f);
            edelweiss = K("flowers_edelweiss", 0.9f, 1.3f);
            clover = K("clover", 0.9f, 1.3f);
            fern = K("fern", 0.8f, 1.2f);
            fernYoung = K("fern_young", 0.85f, 1.25f);
            blueberry = K("blueberry", 0.8f, 1.3f);
            buttercup = K("flowers_buttercup", 0.9f, 1.2f);
            poppy = K("flowers_poppy", 0.9f, 1.2f);
            Rise(meadow, 0.36f); Rise(daisy, 0.36f); Rise(buttercup, 0.3f); Rise(poppy, 0.42f); Rise(lupin, 0.6f, 1.3f);
            Rise(clover, 0.22f, 1.3f); Rise(gentian, 0.14f, 1.4f); Rise(edelweiss, 0.16f, 1.4f);
            grass = gameObject.AddComponent<GrassField>();
            grass.Init(m, assets ? assets.terrain : null);
        }

        Kind MakeKind(string id, GameObject prefab, float s0, float s1)
        {
            var k = new Kind { id = id, lods = new Part[3][], scaleMin = s0, scaleMax = s1 };
            var group = prefab.GetComponentInChildren<LODGroup>();
            var levels = group ? group.GetLODs() : null;
            for (int l = 0; l < 3; l++)
            {
                var renderers = levels != null && levels.Length > 0 ? levels[Mathf.Min(l, levels.Length - 1)].renderers
                    : prefab.GetComponentsInChildren<Renderer>();
                var parts = new List<Part>();
                foreach (var r in renderers)
                {
                    if (!r) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;
                    var local = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var mats = r.sharedMaterials;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                        if (mats[s]) parts.Add(new Part { mesh = mf.sharedMesh, submesh = s, mats = Variants(mats[s]), local = local });
                }
                k.lods[l] = parts.ToArray();
            }
            return k;
        }

        /// <summary>Une copie du matériau par palier : chacune rétrécit ses plantes avant sa distance de fin.</summary>
        Material[] Variants(Material m)
        {
            if (!variants.TryGetValue(m, out var v))
            {
                v = new Material[4];
                for (int b = 0; b < 4; b++)
                {
                    v[b] = new Material(m) { enableInstancing = true, name = m.name + " (sol " + b + ")" };
                    MatchField(v[b], terrainMat, fieldMat);
                    // L'axe Y de la matrice de chaque plante porte la normale du terrain (voir Build)
                    if (v[b].HasProperty(SlopeLitId)) v[b].SetFloat(SlopeLitId, 1f);
                }
                variants[m] = v;
                ApplyFade(v);
            }
            return v;
        }

        void ApplyFade(Material[] v)
        {
            v[MatFar].SetFloat(FadeEndId, farDistance);
            v[MatMid].SetFloat(FadeEndId, midDistance);
            v[MatNear].SetFloat(FadeEndId, nearDistance);
            v[MatDense].SetFloat(FadeEndId, denseFar);
        }

        // ------------------------------------------------------------------ flux autour des joueurs

        /// <summary>Crée les carrés manquants près des joueurs (dans le budget de temps), libère les carrés lointains.</summary>
        public void Stream(IList<Vector3> players)
        {
            if (map == null || veg == null || players == null || players.Count == 0) return;
            veg.floraNear = nearDistance;
            veg.floraMid = midDistance;
            veg.floraFar = farDistance;
            veg.denseNear = denseNear;
            veg.denseFar = denseCarpet ? denseFar : 0f;
            if (FieldOn)
            {
                // Herbe des prés : moins loin et moins serrée sur les petits réglages graphiques
                grass.density = Mathf.Clamp(density, 0.3f, 1.2f);
                grass.endDistance = Mathf.Lerp(55f, 100f, Mathf.InverseLerp(0.45f, 1f, density));
                grass.Stream(players);
            }
            float start = Time.realtimeSinceStartup;
            // Le tapis dense d'abord : c'est lui qu'on voit sous les roues
            if (denseCarpet) StreamLayer(denseLayer, players, denseFar, start);
            else Clear(denseLayer);
            StreamLayer(baseLayer, players, farDistance, start);
        }

        /// <summary>Recrée toute la flore (changement de densité) : les carrés reviennent au fil du flux.</summary>
        public void Rebuild()
        {
            Clear(baseLayer);
            Clear(denseLayer);
        }

        void Clear(Layer layer)
        {
            foreach (var c in layer.live) veg.Remove(layer.Key(c));
            layer.live.Clear();
        }

        void StreamLayer(Layer layer, IList<Vector3> players, float reach, float start)
        {
            float size = layer.chunk;
            float keep = reach + size * 1.5f, want = reach + size * 0.5f;

            drop.Clear();
            foreach (var c in layer.live)
            {
                float d = float.MaxValue;
                foreach (var p in players) d = Mathf.Min(d, ChunkDistance(c, p, size));
                if (d > keep) drop.Add(c);
            }
            foreach (var c in drop)
            {
                layer.live.Remove(c);
                veg.Remove(layer.Key(c));
            }

            if ((Time.realtimeSinceStartup - start) * 1000f > budgetMs) return;
            wanted.Clear();
            queued.Clear();
            int r = Mathf.CeilToInt(want / size);
            foreach (var p in players)
            {
                int cx = Mathf.FloorToInt(p.x / size), cz = Mathf.FloorToInt(p.z / size);
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var c = new Vector2Int(cx + dx, cz + dz);
                    if (layer.live.Contains(c) || queued.Contains(c) || ChunkDistance(c, p, size) > want) continue;
                    if (Mathf.Abs((c.x + 0.5f) * size) > map.Half || Mathf.Abs((c.y + 0.5f) * size) > map.Half) continue;
                    queued.Add(c);
                    wanted.Add(c);
                }
            }
            if (wanted.Count == 0) return;
            focus = players[0];
            focusChunk = size;
            wanted.Sort(byDistance);
            foreach (var c in wanted)
            {
                Build(layer, c);
                layer.live.Add(c);
                if ((Time.realtimeSinceStartup - start) * 1000f > budgetMs) break;
            }
        }

        static float ChunkDistance(Vector2Int c, Vector3 p, float size)
        {
            float x0 = c.x * size, z0 = c.y * size;
            float dx = Mathf.Max(0f, Mathf.Max(x0 - p.x, p.x - (x0 + size)));
            float dz = Mathf.Max(0f, Mathf.Max(z0 - p.z, p.z - (z0 + size)));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ génération d'un carré

        void Build(Layer layer, Vector2Int c)
        {
            foreach (var list in scratch.Values) list.Clear();
            var rng = new System.Random(c.x * 73856093 ^ c.y * 19349663 ^ (layer.dense ? 0x2f6b3c1d : 0x5bd1e995));
            float R() => (float)rng.NextDouble();
            float size = layer.chunk;
            int cells = Mathf.RoundToInt(size / layer.cell);
            float step = size / cells;
            float x0 = c.x * size, z0 = c.y * size;
            float hMin = float.MaxValue, hMax = float.MinValue;
            int count = 0;
            for (int iz = 0; iz < cells; iz++)
            for (int ix = 0; ix < cells; ix++)
            {
                float x = x0 + (ix + R()) * step, z = z0 + (iz + R()) * step;
                float roll = R(), band = R(), yaw = R() * 360f, sr = R();
                int gi = map.Nearest(x, z);
                float edge = map.Info[gi * 4] * 0.25f - 1f;
                if (edge < 2.0f || (map.Info[gi * 4 + 1] & 15) != 0 || map.Info[gi * 4 + 2] > 60 || map.Info[gi * 4 + 3] > 120) continue;
                // Terre battue (rives, plages, chemins) : rien n'y pousse, le champ d'herbe non plus
                if (map.Masks[gi * 4 + 3] > 90) continue;
                var kind = layer.dense ? ChooseDense(x, z, gi, edge, roll) : Choose(x, z, gi, edge, roll);
                if (kind == null) continue;
                float y = map.Height(x, z);
                // Pente du terrain (même pas que le champ d'herbe) : la matrice penche la plante sur la normale du
                // terrain, le shader la redresse et s'en sert pour l'éclairer comme le pré
                const float e = 1.5f;
                float gx = (map.Height(x + e, z) - map.Height(x - e, z)) / (2f * e);
                float gz = (map.Height(x, z + e) - map.Height(x, z - e)) / (2f * e);
                float sink = 0.03f + Mathf.Min(Mathf.Sqrt(gx * gx + gz * gz), 1f) * 0.18f;
                var tilt = Quaternion.FromToRotation(Vector3.up, new Vector3(-gx, 1f, -gz));
                float grow = Mathf.Lerp(kind.scaleMin, kind.scaleMax, sr);
                // Les fleurs dépassent de l'herbe : elles grandissent avec elle (hautes herbes, grandes fleurs)
                if (kind.rise > 0f && FieldOn) grow *= Mathf.Clamp(grass.HeightAt(x, z) / kind.rise, 1f, kind.riseMax);
                var place = Matrix4x4.TRS(new Vector3(x, y - sink, z), tilt * Quaternion.Euler(0f, yaw, 0f), Vector3.one * grow);
                if (layer.dense)
                {
                    // Tapis dense : modèle détaillé tout près, moyen ensuite, puis rétrécit avant la fin du tapis
                    Register(kind, 0, MatDense, VegetationRenderer.FloraDense, place);
                    Register(kind, 1, MatDense, VegetationRenderer.FloraDenseMid, place);
                }
                else
                {
                    // Palier : loin (3 niveaux), mi-distance (2 niveaux) ou de près seulement
                    int b = band < BandFar ? MatFar : band < BandMid ? MatMid : MatNear;
                    for (int l = 0; l <= 2 - b; l++) Register(kind, l, b, VegetationRenderer.FloraNear + l, place);
                }
                hMin = Mathf.Min(hMin, y);
                hMax = Mathf.Max(hMax, y);
                count++;
            }
            if (count == 0) return;
            var bounds = new Bounds(new Vector3(x0 + size * 0.5f, (hMin + hMax) * 0.5f + 1f, z0 + size * 0.5f),
                new Vector3(size + 4f, hMax - hMin + 5f, size + 4f));
            veg.AddBatch(layer.Key(c), bounds, scratch);
        }

        void Register(Kind kind, int lod, int mat, int level, Matrix4x4 place)
        {
            foreach (var part in kind.lods[lod])
            {
                var key = new VegetationRenderer.BatchKey { mesh = part.mesh, submesh = part.submesh, material = part.mats[mat], lod = level };
                if (!scratch.TryGetValue(key, out var list)) scratch[key] = list = new List<Matrix4x4>();
                list.Add(place * part.local);
            }
        }

        static float Patch(float x, float z, float freq, float offset)
        {
            float p = Mathf.PerlinNoise(x * freq + offset, z * freq - offset * 0.7f);
            return Mathf.Clamp01((p - 0.45f) * 3.3f);
        }

        /// <summary>Dérive de fleurs : taches nettes sur une petite part du pré, le reste sans fleurs (pré sobre, les
        /// fleurs en accents de couleur plutôt qu'en semis uniforme).</summary>
        static float Drift(float x, float z, float freq, float offset)
        {
            float p = Mathf.PerlinNoise(x * freq + offset, z * freq - offset * 0.7f);
            return Mathf.Clamp01((p - 0.53f) * 4.5f);
        }

        /// <summary>Part du trèfle gardée : il vit dans l'herbe courte, les hautes herbes le cachent.</summary>
        float ShortGrass(float x, float z) => FieldOn && grass.HeightAt(x, z) > 0.42f ? 0.15f : 1f;

        void Option(Kind k, float w, bool withField = false)
        {
            if (k == null || w <= 0f) return;
            // Le champ d'herbe remplace les touffes d'herbe (sauf sur la roche, où il ne pousse pas)
            if (!withField && FieldOn && (k == grassShort || k == grassTall || k == grassDry)) return;
            pick[picks] = k;
            weight[picks] = w * density;
            picks++;
        }

        Kind Pick(float roll, float edge)
        {
            // Bas-côté (2-5 m de la route) : herbe rase surtout
            if (edge < 5f)
                for (int i = 0; i < picks; i++)
                    if (pick[i] != grassShort && pick[i] != clover && pick[i] != grassDry) weight[i] *= 0.35f;
            float total = 0f;
            for (int i = 0; i < picks; i++) total += weight[i];
            float scale = total > 0.95f ? 0.95f / total : 1f;
            float acc = 0f;
            for (int i = 0; i < picks; i++)
            {
                acc += weight[i] * scale;
                if (roll < acc) return pick[i];
            }
            return null;
        }

        /// <summary>Couche de fond : plante (ou rien) d'une case, selon le milieu (pré, alpage, sous-bois, lisière).</summary>
        Kind Choose(float x, float z, int gi, float edge, float roll)
        {
            float forest = map.Masks[gi * 4] / 255f, dry = map.Masks[gi * 4 + 1] / 255f, rock = map.Masks[gi * 4 + 2] / 255f;
            float h = map.H[gi];
            picks = 0;
            if (rock > 0.5f)
            {
                Option(grassDry, 0.05f, true);
                Option(edelweiss, h > 300f ? 0.02f * Patch(x, z, 0.06f, 71.3f) : 0f);
            }
            else if (forest > 0.5f)
            {
                // Sous-bois : fougères en colonies, myrtilles, un peu d'herbe
                float colony = Patch(x, z, 0.05f, 17.7f);
                Option(fern, 0.03f + 0.12f * colony);
                Option(fernYoung, 0.02f + 0.07f * colony);
                Option(blueberry, 0.03f + 0.1f * Patch(x, z, 0.04f, 53.1f));
                Option(grassShort, 0.05f);
            }
            else if (forest > 0.18f)
            {
                // Lisière : hautes herbes, fougères et myrtilles éparses, quelques marguerites
                Option(grassShort, 0.22f);
                Option(grassTall, 0.16f);
                Option(fern, 0.04f);
                Option(fernYoung, 0.03f);
                Option(blueberry, 0.04f);
                Option(daisy, 0.08f * Drift(x, z, 0.031f, 11.3f));
                Option(buttercup, 0.06f * Drift(x, z, 0.035f, 23.9f));
            }
            else if (dry > 0.5f)
            {
                // Alpage : herbe rase et sèche, gentianes et edelweiss en taches, landes de myrtilles
                float alp = Patch(x, z, 0.045f, 29.3f);
                Option(grassDry, 0.28f);
                Option(grassShort, 0.16f);
                Option(gentian, 0.1f * alp);
                Option(edelweiss, h > 320f ? 0.07f * Patch(x, z, 0.05f, 91.7f) : 0f);
                Option(clover, 0.04f * Patch(x, z, 0.05f, 83.7f));
                Option(meadow, 0.03f);
                Option(blueberry, 0.05f * Patch(x, z, 0.035f, 41.9f));
            }
            else
            {
                // Pré : nappes de hautes herbes, dérives de marguerites et de lupins, trèfle
                Option(grassShort, 0.28f);
                Option(grassTall, 0.08f + 0.2f * Patch(x, z, 0.04f, 5.9f));
                Option(clover, 0.1f * Drift(x, z, 0.05f, 83.7f) * ShortGrass(x, z));
                Option(meadow, 0.015f);
                // Dérives d'une seule espèce : marguerites, lupins, boutons d'or, coquelicots
                Option(daisy, 0.22f * Drift(x, z, 0.031f, 11.3f));
                Option(lupin, 0.12f * Drift(x, z, 0.022f, 47.1f));
                Option(buttercup, 0.2f * Drift(x, z, 0.035f, 23.9f));
                Option(poppy, 0.12f * Drift(x, z, 0.027f, 61.3f));
                Option(gentian, h > 260f ? 0.03f * Patch(x, z, 0.045f, 29.3f) : 0f);
            }
            return Pick(roll, edge);
        }

        /// <summary>Tapis dense : herbe rase, trèfle et petites fleurs serrés près du joueur.</summary>
        Kind ChooseDense(float x, float z, int gi, float edge, float roll)
        {
            float forest = map.Masks[gi * 4] / 255f, dry = map.Masks[gi * 4 + 1] / 255f, rock = map.Masks[gi * 4 + 2] / 255f;
            float h = map.H[gi];
            picks = 0;
            if (rock > 0.5f)
                Option(grassDry, 0.08f, true);
            else if (forest > 0.5f)
            {
                Option(grassShort, 0.07f);
                Option(fernYoung, 0.02f * Patch(x, z, 0.05f, 17.7f));
                Option(blueberry, 0.03f * Patch(x, z, 0.04f, 53.1f));
            }
            else if (forest > 0.18f)
            {
                Option(grassShort, 0.42f);
                Option(grassTall, 0.05f);
                Option(clover, 0.05f * Drift(x, z, 0.05f, 83.7f) * ShortGrass(x, z));
            }
            else if (dry > 0.5f)
            {
                Option(grassDry, 0.3f);
                Option(grassShort, 0.26f);
                Option(gentian, 0.06f * Patch(x, z, 0.045f, 29.3f));
                Option(edelweiss, h > 320f ? 0.04f * Patch(x, z, 0.05f, 91.7f) : 0f);
                Option(clover, 0.04f * Patch(x, z, 0.05f, 83.7f));
            }
            else
            {
                Option(grassShort, 0.5f);
                Option(clover, 0.12f * Drift(x, z, 0.05f, 83.7f) * ShortGrass(x, z));
                Option(meadow, 0.006f);
                Option(daisy, 0.06f * Drift(x, z, 0.031f, 11.3f));
                Option(buttercup, 0.06f * Drift(x, z, 0.035f, 23.9f));
                Option(grassTall, 0.04f * Patch(x, z, 0.04f, 5.9f));
            }
            return Pick(roll, edge);
        }

        // ------------------------------------------------------------------ vent et fondu

        void LateUpdate()
        {
            if (Time.time > weatherLookup)
            {
                weatherLookup = Time.time + 3f;
                if (!weather) weather = FindFirstObjectByType<WeatherSystem>();
            }
            float storm = weather ? weather.Storm : 0f;
            // Le vent tourne lentement ; les rafales défilent dans sa direction (intégré ici : pas de saut quand il tourne)
            float a = 0.6f + Mathf.PerlinNoise(Time.time * 0.004f, 0.37f) * 2.2f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float force = 1f + 1.7f * storm;
            windScroll += dir * (Time.deltaTime * windSpeed * 0.11f * (0.7f + 0.6f * force));
            windScroll.x = Mathf.Repeat(windScroll.x, 64f);
            windScroll.y = Mathf.Repeat(windScroll.y, 64f);
            Shader.SetGlobalVector(WindId, new Vector4(dir.x, dir.y, force, 0f));
            Shader.SetGlobalVector(WindScrollId, new Vector4(windScroll.x, windScroll.y, 0f, 0f));
            Shader.SetGlobalFloat(FadeId, fadeWidth);
            foreach (var v in variants.Values) ApplyFade(v);
        }

        void OnDestroy()
        {
            foreach (var v in variants.Values)
                foreach (var m in v)
                    if (m) Destroy(m);
        }
    }
}
