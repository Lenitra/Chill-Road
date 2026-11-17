using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ChillRoad
{
    /// <summary>
    /// Décor 3D de l'écran de chargement, fait des assets du jeu : une vallée alpine. La voiture choisie est garée sur
    /// une nationale (enrobé, marquages et accotements du shader de route) qui file en lacets doux le long du fond de
    /// vallée, entre prés vallonnés, verger clôturé, fermes, et deux versants de montagnes : forêt de conifères en bas,
    /// roche puis neige sur les sommets (shader de terrain). Une voiture du trafic passe de temps en temps. Construit à
    /// l'écart du monde (qui se génère pendant ce temps), sous le ciel, la lumière et la météo du jeu, et filmé par sa
    /// propre caméra en plans enchaînés, dans une texture affichée derrière l'interface.
    /// </summary>
    public class LoadingStage : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(0f, 2500f, -6000f);
        const float RoadHalf = 1100f;            // demi-longueur de la route (m)
        const float GroundHalf = 1700f;          // demi-côté du terrain (m)
        const float Straight = 110f;             // la route est droite autour de la voiture (verger, clôture)
        const float Embankment = 2.2f;           // la route est sur un remblai, comme en jeu
        const float TrafficSpeed = 21f;

        public RenderTexture Texture { get; private set; }
        /// <summary>Caméra du décor (la pluie de l'orage tombe aussi autour d'elle).</summary>
        public Camera Camera => cam;
        /// <summary>
        /// Fond du menu d'accueil : après le chargement, la réalisation passe (par un fondu au noir) à une seule orbite
        /// lente, la voiture cadrée dans le tiers droit de l'image pour laisser la gauche aux boutons.
        /// </summary>
        public bool Menu { get; set; }
        /// <summary>Part de la demi-largeur de l'image dont la voiture est décalée vers la droite en mode menu.</summary>
        public float MenuShift { get; set; } = 0.4f;

        GameObject root;
        Camera cam;
        Vector3 carCenter;
        float carRadius = 2.5f, angle = 232f, time, menuTime = -1f, toMenuFade;
        Transform traffic;
        float trafficZ = 300f, trafficLift;

        public void Build(GameAssets assets, VehicleSpec car, Camera template)
        {
            root = new GameObject("LoadingStage");
            root.transform.position = Origin;
            var rng = new System.Random(2024);
            BuildRoad(assets);
            BuildTerrain(assets);
            PlaceCar(car, assets);
            Scatter(assets, rng);
            if (assets.trafficCars != null && assets.trafficCars.Length > 0)
            {
                Vector3 at = Axis(trafficZ) - RightAt(trafficZ) * (RoadNetwork.LaneWidth * 0.5f);
                var t = Place(assets.trafficCars[rng.Next(assets.trafficCars.Length)], at, 180f, assets.trafficCarScale);
                traffic = t ? t.transform : null;
                // Hauteur du pivot au-dessus de la chaussée (Place pose le bas du modèle sur la route)
                if (traffic) trafficLift = traffic.localPosition.y - at.y;
            }
            // Décor inerte : aucun collider (le monde réel se construit à côté)
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Destroy(c);
            NoMapSnow();
            BuildCamera(template);
        }

        /// <summary>Détruit le décor, la caméra et la texture (fin du chargement).</summary>
        public void Release()
        {
            if (root) Destroy(root);
            if (cam) Destroy(cam.gameObject);
            if (Texture) { Texture.Release(); Destroy(Texture); Texture = null; }
            Destroy(this);
        }

        /// <summary>
        /// La carte de neige du monde (routes enneigées, neige sur les toits et les sapins) est lue à la position : hors
        /// de la carte, elle prendrait la valeur de son bord, et la vallée du décor se retrouvait sous la neige. Le décor
        /// l'éteint pour ses propres objets (la neige des sommets vient du terrain lui-même).
        /// </summary>
        void NoMapSnow()
        {
            int snowOn = Shader.PropertyToID("_ChillRoadSnowOn"), trailOn = Shader.PropertyToID("_ChillRoadSnowTrailOn");
            var block = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetFloat(snowOn, 0f);
                block.SetFloat(trailOn, 0f);
                r.SetPropertyBlock(block);
            }
        }

        // ------------------------------------------------------------------ tracé

        static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>Axe de la route : droit autour de la voiture, puis grandes courbes en S le long de la vallée.</summary>
        static float AxisX(float z) =>
            70f * S(Straight, 650f, z) - 45f * S(650f, RoadHalf, z) - 50f * S(Straight, 650f, -z) + 40f * S(650f, RoadHalf, -z);

        /// <summary>Profil de la route : elle monte doucement vers l'amont de la vallée et descend un peu vers l'aval.</summary>
        static float AxisY(float z) =>
            12f * S(Straight, 650f, z) + 16f * S(650f, RoadHalf, z) - 5f * S(Straight, 650f, -z) + 6f * S(650f, RoadHalf, -z);

        static Vector3 Axis(float z) => new Vector3(AxisX(z), AxisY(z), z);

        /// <summary>Droite de la route (horizontale) au point z de l'axe.</summary>
        static Vector3 RightAt(float z)
        {
            Vector3 t = Axis(z + 0.5f) - Axis(z - 0.5f);
            return new Vector3(t.z, 0f, -t.x).normalized;
        }

        // ------------------------------------------------------------------ route

        void BuildRoad(GameAssets assets)
        {
            float hw = RoadNetwork.CountryHalfWidth;
            // Données du shader de route : nationale (ni autoroute ni chemin), demi-largeur, voies connues
            var info = new Vector4(0f, 0f, hw, 1f);
            var road = new RoadNetwork.MeshBuilder();
            var verge = new RoadNetwork.MeshBuilder();
            var marks = new RoadNetwork.MeshBuilder();
            road.SetInfo(info, info);
            verge.SetInfo(info, info);
            marks.SetInfo(info, info);
            const float step = 8f;
            float along = 0f;
            for (float z = -RoadHalf; z < RoadHalf; z += step)
            {
                float z2 = z + step;
                Vector3 a = Axis(z), b = Axis(z2);
                Vector3 ra = RightAt(z), rb = RightAt(z2);
                float ga = along, gb = along + Vector3.Distance(a, b);
                along = gb;
                road.Quad(a - ra * hw, a + ra * hw, b - rb * hw, b + rb * hw,
                    new Vector2(-hw, ga), new Vector2(hw, ga), new Vector2(-hw, gb), new Vector2(hw, gb));
                // Accotement en gravier puis talus enherbé qui descend vers la prairie
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 ea = a + ra * (side * hw), eb = b + rb * (side * hw);
                    Vector3 sa = ea + ra * (side * 1.2f) - Vector3.up * 0.06f, sb = eb + rb * (side * 1.2f) - Vector3.up * 0.06f;
                    Vector3 oa = sa + ra * (side * 5f) - Vector3.up * Embankment, ob = sb + rb * (side * 5f) - Vector3.up * Embankment;
                    if (side > 0)
                    {
                        verge.Quad(ea, sa, eb, sb, new Vector2(0f, ga), new Vector2(1.2f, ga), new Vector2(0f, gb), new Vector2(1.2f, gb));
                        verge.Quad(sa, oa, sb, ob, new Vector2(1.2f, ga), new Vector2(6.2f, ga), new Vector2(1.2f, gb), new Vector2(6.2f, gb));
                    }
                    else
                    {
                        verge.Quad(sa, ea, sb, eb, new Vector2(1.2f, ga), new Vector2(0f, ga), new Vector2(1.2f, gb), new Vector2(0f, gb));
                        verge.Quad(oa, sa, ob, sb, new Vector2(6.2f, ga), new Vector2(1.2f, ga), new Vector2(6.2f, gb), new Vector2(1.2f, gb));
                    }
                }
                // Lignes de rive continues
                foreach (float side in new[] { -1f, 1f })
                    Mark(marks, side * RoadNetwork.CountryEdge, 0.2f, z, z2);
            }
            // Ligne centrale discontinue (4,5 m tous les 12 m, comme en jeu)
            for (float z = -RoadHalf; z < RoadHalf; z += 12f) Mark(marks, 0f, 0.15f, z, z + 4.5f);

            AddMesh(root.transform, "Road", road.Build("LoadingRoad"), assets.asphalt, true);
            AddMesh(root.transform, "Verge", verge.Build("LoadingVerge"), assets.verge ? assets.verge : assets.dirt, true);
            AddMesh(root.transform, "Markings", marks.Build("LoadingMarks"), assets.roadMarking, false);
        }

        /// <summary>Marquage le long de l'axe, à une distance latérale donnée.</summary>
        static void Mark(RoadNetwork.MeshBuilder mb, float x, float width, float z0, float z1)
        {
            Vector3 up = Vector3.up * 0.025f;
            float w = width * 0.5f;
            Vector3 a = Axis(z0) + up, b = Axis(z1) + up, ra = RightAt(z0), rb = RightAt(z1);
            mb.Quad(a + ra * (x - w), a + ra * (x + w), b + rb * (x - w), b + rb * (x + w));
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>Distance horizontale (approchée) à l'axe de la route.</summary>
        static float RoadDistance(float x, float z) => Mathf.Abs(x - AxisX(Mathf.Clamp(z, -RoadHalf, RoadHalf)));

        /// <summary>Part « montagne » : nulle dans le fond de vallée, pleine sur les versants et au bout de la vallée.</summary>
        static float Mountain(float x, float z)
        {
            float d = RoadDistance(x, z);
            float walls = S(260f, 1350f, d);
            float ends = S(RoadHalf - 60f, GroundHalf - 100f, Mathf.Abs(z));
            return Mathf.Max(walls, ends);
        }

        /// <summary>Crêtes : bruit « en arêtes » (1 - |2n - 1|) à deux échelles, pour des sommets pointus.</summary>
        static float Ridge(float x, float z)
        {
            float a = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.0019f + 7.1f, z * 0.0019f + 2.3f) * 2f - 1f);
            float b = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.0061f + 1.9f, z * 0.0061f + 9.4f) * 2f - 1f);
            return a * a * 0.85f + b * 0.15f;
        }

        /// <summary>Hauteur du sol : à plat au pied du remblai, prés vallonnés, puis les versants et leurs sommets.</summary>
        static float Ground(float x, float z)
        {
            float d = RoadDistance(x, z);
            float floor = AxisY(Mathf.Clamp(z, -RoadHalf, RoadHalf)) - Embankment - 0.1f;
            float away = S(14f, 140f, d);
            float m = Mountain(x, z);
            float hills = Mathf.PerlinNoise(x * 0.0055f + 11.3f, z * 0.0055f + 4.1f) * 46f + Mathf.PerlinNoise(x * 0.021f + 3.7f, z * 0.021f) * 7f;
            return floor + away * hills + m * m * (150f + 400f * Ridge(x, z));
        }

        /// <summary>Altitude au-dessus du fond de vallée (étages : prés, forêt, roche, neige).</summary>
        static float Altitude(float x, float z) => Ground(x, z) - AxisY(Mathf.Clamp(z, -RoadHalf, RoadHalf));

        static float Forest(float x, float z) => Mathf.PerlinNoise(x * 0.012f + 40f, z * 0.012f + 17f);

        /// <summary>Forêt de conifères sur les versants : dense en bas, clairsemée en montant, absente au-dessus de 230 m.</summary>
        static float ConiferBelt(float x, float z)
        {
            float h = Altitude(x, z);
            return Mountain(x, z) > 0.08f ? S(240f, 150f, h) * S(0.3f, 0.5f, Forest(x * 0.7f, z * 0.7f) + (1f - S(20f, 120f, h)) * 0.25f) : 0f;
        }

        /// <summary>Coordonnées d'un axe de la grille : mailles fines près de la route, larges sur les montagnes au loin.</summary>
        static List<float> GridAxis()
        {
            var half = new List<float>();
            for (float c = 0f; c < GroundHalf; c += Mathf.Clamp(9f + c * 0.04f, 9f, 60f)) half.Add(c);
            half.Add(GroundHalf);
            var axis = new List<float>();
            for (int i = half.Count - 1; i > 0; i--) axis.Add(-half[i]);
            axis.AddRange(half);
            return axis;
        }

        void BuildTerrain(GameAssets assets)
        {
            var xs = GridAxis();
            int n = xs.Count;
            var v = new Vector3[n * n];
            var c = new Color[v.Length];
            var extra = new Vector2[v.Length];
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x = xs[ix], z = xs[iz];
                float h = Ground(x, z), alt = Altitude(x, z), d = RoadDistance(x, z);
                int i = iz * n + ix;
                v[i] = new Vector3(x, h, z);
                // Masques du shader de terrain : r = sous-bois, g = herbe sèche, b = roche, a = terre battue
                float meadowForest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.68f, Forest(x, z))) * Mathf.InverseLerp(20f, 40f, d);
                float forest = Mathf.Max(meadowForest, ConiferBelt(x, z));
                float dry = Mathf.Clamp01(Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f) - 0.45f) * 1.6f + S(150f, 260f, alt) * 0.5f;
                float dirt = Mathf.Clamp01(1f - (d - RoadNetwork.CountryHalfWidth - 4f) / 5f);
                float rock = S(230f, 330f, alt) * 0.7f;
                c[i] = new Color(forest * (1f - dirt) * (1f - rock), Mathf.Clamp01(dry), rock, dirt);
                // Neige des sommets (uv2.y), au-dessus de ~330 m, avec un liseré irrégulier
                float snowLine = 300f + (Mathf.PerlinNoise(x * 0.004f + 3f, z * 0.004f) - 0.5f) * 90f;
                extra[i] = new Vector2(0f, S(snowLine - 40f, snowLine + 50f, alt));
            }
            var t = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                int i = iz * n + ix;
                t[k++] = i; t[k++] = i + n; t[k++] = i + 1;
                t[k++] = i + 1; t[k++] = i + n; t[k++] = i + n + 1;
            }
            var m = new Mesh { name = "LoadingGround" };
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v;
            m.colors = c;
            m.uv2 = extra;
            m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateBounds();
            AddMesh(root.transform, "Ground", m, assets.terrain, true);
        }

        // ------------------------------------------------------------------ voiture et nature

        void PlaceCar(VehicleSpec spec, GameAssets assets)
        {
            if (spec == null || !spec.prefab) return;
            var go = Place(spec.prefab, new Vector3(RoadNetwork.LaneWidth * 0.5f, 0f, 0f), 0f, spec.modelScale);
            if (!go) return;
            CarPaint.Apply(go.transform, CarPaint.Selected(spec));
            CarCosmetics.Apply(go.transform, CarCosmetics.Fitted(spec), assets);
            var b = BoundsOf(go);
            carCenter = b.center;
            carRadius = Mathf.Max(b.extents.x, b.extents.z);
        }

        void Scatter(GameAssets a, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            float Rr(float lo, float hi) => Mathf.Lerp(lo, hi, R());
            bool Has(GameObject[] list) => list != null && list.Length > 0;
            GameObject Pick(GameObject[] list) => list[rng.Next(list.Length)];

            // Verger derrière une clôture en bois, côté gauche (comme le long des nationales du jeu)
            if (Has(a.fruitTrees))
                for (float z = -70f; z <= 90f; z += 9f)
                for (float x = -20f; x >= -52f; x -= 8f)
                    Nature(Pick(a.fruitTrees), x + Rr(-1f, 1f), z + Rr(-1f, 1f), R() * 360f, Rr(0.9f, 1.2f));
            if (a.fence) Fence(a.fence, -14.5f, -80f, 100f);

            // Fermes isolées dans le fond de vallée, tournées vers la route
            if (Has(a.buildings))
            {
                Farm(a, rng, 150f, 34f);
                Farm(a, rng, 230f, -40f);
                Farm(a, rng, -190f, 38f);
                Farm(a, rng, 420f, -46f);
            }

            // Prés : bosquets de pins, buissons et herbes hautes au pied du remblai, rochers et fleurs
            for (int i = 0; i < 900; i++)
            {
                float x = Rr(-260f, 260f), z = Rr(-520f, 520f);
                x += AxisX(z);
                float d = RoadDistance(x, z);
                if (d < 14f || Mountain(x, z) > 0.15f) continue;
                bool orchard = x < -12f && x > -58f && z > -80f && z < 100f;
                float roll = R();
                if (!orchard && Forest(x, z) > 0.52f && d > 22f && Has(a.pines))
                    Nature(Pick(a.pines), x, z, R() * 360f, Rr(0.9f, 1.6f));
                else if (roll < 0.25f && d < 60f && Has(a.shrubs))
                    Nature(Pick(a.shrubs), x, z, R() * 360f, Rr(0.9f, 1.4f));
                else if (roll < 0.45f && d < 45f && Has(a.grass))
                    Nature(Pick(a.grass), x, z, R() * 360f, Rr(0.9f, 1.3f));
                else if (roll < 0.55f && d < 45f && Has(a.flowers))
                    Nature(Pick(a.flowers), x, z, R() * 360f, Rr(0.9f, 1.2f));
                else if (roll < 0.62f && d > 30f && Has(a.bigRocks))
                    Nature(Pick(a.bigRocks), x, z, R() * 360f, Rr(0.7f, 1.3f));
                else if (roll < 0.72f && Has(a.smallRocks))
                    Nature(Pick(a.smallRocks), x, z, R() * 360f, Rr(0.8f, 1.4f));
            }

            // Versants : forêt de conifères (plus grands en bas), quelques gros blocs dans les éboulis au-dessus
            if (Has(a.pines))
            {
                int placed = 0;
                for (int i = 0; i < 6000 && placed < 900; i++)
                {
                    float z = Rr(-950f, 950f), side = R() < 0.5f ? -1f : 1f;
                    float x = AxisX(z) + side * Rr(220f, 950f);
                    float belt = ConiferBelt(x, z);
                    if (belt < 0.5f || R() > belt) continue;
                    float alt = Altitude(x, z);
                    Nature(Pick(a.pines), x, z, R() * 360f, Mathf.Lerp(2.1f, 1.3f, Mathf.InverseLerp(30f, 230f, alt)) * Rr(0.85f, 1.15f));
                    placed++;
                }
            }
            if (Has(a.bigRocks))
                for (int i = 0; i < 400; i++)
                {
                    float z = Rr(-900f, 900f), x = AxisX(z) + (R() < 0.5f ? -1f : 1f) * Rr(350f, 1000f);
                    float alt = Altitude(x, z);
                    if (alt > 220f && alt < 340f) Nature(Pick(a.bigRocks), x, z, R() * 360f, Rr(2.5f, 5f));
                }
        }

        /// <summary>Ferme : un bâtiment du pack, posé en retrait de la route et tourné vers elle, avec quelques arbres.</summary>
        void Farm(GameAssets a, System.Random rng, float z, float offset)
        {
            float x = AxisX(z) + offset;
            float yaw = offset > 0f ? -90f : 90f;
            var b = Place(a.buildings[rng.Next(a.buildings.Length)], new Vector3(x, Ground(x, z) - 0.2f, z), yaw + (float)(rng.NextDouble() * 16.0 - 8.0), 1f);
            if (!b || a.fruitTrees == null || a.fruitTrees.Length == 0) return;
            for (int i = 0; i < 4; i++)
            {
                float tx = x + Mathf.Sign(offset) * (12f + i * 3f), tz = z - 14f + i * 9f;
                Nature(a.fruitTrees[rng.Next(a.fruitTrees.Length)], tx, tz, (float)rng.NextDouble() * 360f, 1.1f);
            }
        }

        void Nature(GameObject prefab, float x, float z, float yaw, float scale)
        {
            if (prefab) Place(prefab, new Vector3(x, Ground(x, z) - 0.15f, z), yaw, scale);
        }

        /// <summary>Clôture : segments du modèle mis bout à bout le long de la route.</summary>
        void Fence(GameObject prefab, float x, float z0, float z1)
        {
            var probe = Place(prefab, Vector3.zero, 0f, 1f);
            if (!probe) return;
            var b = BoundsOf(probe);
            bool alongX = b.size.x >= b.size.z;
            float length = Mathf.Max(0.5f, alongX ? b.size.x : b.size.z);
            DestroyImmediate(probe);
            for (float z = z0; z < z1; z += length)
                Place(prefab, new Vector3(x, Ground(x, z + length * 0.5f) - 0.05f, z + length * 0.5f), alongX ? 90f : 0f, 1f);
        }

        GameObject Place(GameObject prefab, Vector3 local, float yaw, float scale)
        {
            if (!prefab) return null;
            var go = Instantiate(prefab, root.transform);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            // Posé sur le sol : le bas du modèle à la hauteur visée
            var b = BoundsOf(go);
            if (b.size != Vector3.zero) go.transform.position += Vector3.up * (root.transform.position.y + local.y - b.min.y);
            return go;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void AddMesh(Transform parent, string name, Mesh mesh, Material material, bool shadows)
        {
            if (!material) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ caméra

        void BuildCamera(Camera template)
        {
            var go = new GameObject("LoadingCamera");
            cam = go.AddComponent<Camera>();
            if (template)
            {
                cam.CopyFrom(template);
                var d1 = template.GetUniversalAdditionalCameraData();
                var d2 = cam.GetUniversalAdditionalCameraData();
                d2.renderPostProcessing = d1.renderPostProcessing;
                d2.antialiasing = d1.antialiasing;
                d2.antialiasingQuality = d1.antialiasingQuality;
                d2.renderShadows = d1.renderShadows;
                d2.volumeLayerMask = d1.volumeLayerMask;
            }
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 4000f;
            Resize();
            UpdateCamera(0f);
        }

        void Resize()
        {
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            if (Texture && Texture.width == w && Texture.height == h) return;
            if (Texture) { Texture.Release(); Destroy(Texture); }
            Texture = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "LoadingStage (runtime)" };
            Texture.Create();
            cam.targetTexture = Texture;
        }

        /// <summary>Durée d'un plan, et du fondu au noir qui sépare deux plans (s).</summary>
        const float ShotLength = 5.5f, CutFade = 0.35f;

        /// <summary>Part de noir sur l'image (0 = image pleine) : fondu entre deux plans, appliqué par l'écran de chargement.</summary>
        public float Fade { get; private set; } = 1f;

        /// <summary>
        /// Réalisation façon bande-annonce : quatre plans qui s'enchaînent en fondu au noir, chacun en lent mouvement
        /// (orbite à hauteur d'homme, travelling bas sur l'avant, grue qui s'élève sur la vallée, glissé sur l'arrière).
        /// Positions dans le repère de la voiture, en multiples de son rayon.
        /// </summary>
        void UpdateCamera(float dt)
        {
            // Pas de saut quand une image est lente (construction du monde en parallèle)
            angle += Mathf.Min(dt, 0.05f) * 5f;
            if (Menu && menuTime < 0f)
            {
                // Passage au menu : on finit le plan en cours par un fondu au noir, puis l'orbite du menu s'ouvre
                toMenuFade = Mathf.Max(toMenuFade, Fade);
                toMenuFade = Mathf.MoveTowards(toMenuFade, 1f, dt / CutFade);
                if (toMenuFade >= 1f) menuTime = 0f;
                else
                {
                    ShotCamera();
                    Fade = toMenuFade;
                    return;
                }
            }
            if (menuTime >= 0f)
            {
                menuTime += dt;
                Fade = 1f - Mathf.Clamp01(menuTime / 0.6f);
                MenuCamera();
                return;
            }
            ShotCamera();
        }

        /// <summary>Orbite du menu : lente, un peu plus haute et plus large, la voiture décalée vers la droite.</summary>
        void MenuCamera()
        {
            float r = carRadius, fov = 38f;
            float dist = r * 3.6f + 2f;
            Vector3 c = carCenter;
            Vector3 pos = c + Quaternion.Euler(0f, angle * 0.8f + 40f, 0f) * new Vector3(0f, 1.2f, -dist);
            Vector3 aim = c + Vector3.up * 0.3f;
            var look = Quaternion.LookRotation(aim - pos, Vector3.up);
            float halfWidth = Vector3.Distance(pos, aim) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * cam.aspect;
            pos -= look * Vector3.right * (MenuShift * halfWidth);
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, look);
        }

        void ShotCamera()
        {
            int shot = Mathf.FloorToInt(time / ShotLength) % 4;
            float local = Mathf.Repeat(time, ShotLength);
            float e = Mathf.SmoothStep(0f, 1f, local / ShotLength);
            Fade = 1f - Mathf.Clamp01(Mathf.Min(local / CutFade, (ShotLength - local) / CutFade));

            float r = carRadius;
            Vector3 c = carCenter;
            Vector3 pos, aim;
            float fov;
            switch (shot)
            {
                case 1:   // travelling bas qui glisse devant le capot
                    pos = c + Vector3.Lerp(new Vector3(-2.6f * r, -0.15f, 2.3f * r), new Vector3(-0.9f * r, -0.05f, 2.9f * r), e);
                    aim = c + new Vector3(0f, -0.1f, 0.15f * r);
                    fov = 30f;
                    break;
                case 2:   // grue : on s'élève au-dessus du bas-côté, la vallée et ses sommets apparaissent derrière la voiture
                    pos = c + Vector3.Lerp(new Vector3(4.2f * r, 0.6f, -3.6f * r), new Vector3(5.2f * r, 4.6f, -5f * r), e);
                    aim = c + Vector3.Lerp(new Vector3(0f, 0.3f, 0f), new Vector3(0f, 1.2f, 2f * r), e);
                    fov = 42f;
                    break;
                case 3:   // glissé bas le long de l'arrière
                    pos = c + Vector3.Lerp(new Vector3(2.4f * r, -0.1f, -3.4f * r), new Vector3(0.6f * r, 0f, -3.6f * r), e);
                    aim = c + new Vector3(0f, 0f, -0.2f * r);
                    fov = 32f;
                    break;
                default:  // orbite lente à hauteur d'homme (comme la vue de l'accueil)
                {
                    float dist = r * 3.3f + 1.5f;
                    pos = c + Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 1f, -dist);
                    aim = c + Vector3.up * 0.35f;
                    fov = 36f;
                    break;
                }
            }
            // Jamais sous la prairie ni sous la route
            Vector3 o = root.transform.position;
            float ground = o.y + Mathf.Max(AxisY(pos.z - o.z), Ground(pos.x - o.x, pos.z - o.z)) + 0.35f;
            if (pos.y < ground) pos.y = ground;
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(aim - pos, Vector3.up));
        }

        void LateUpdate()
        {
            if (!cam) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            // Les plans suivent le temps réel (le chargement fait des images lentes), les mouvements restent fluides
            time += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            Resize();
            UpdateCamera(dt);
            // Une voiture du trafic passe de temps en temps sur la voie d'en face, en suivant les courbes
            if (traffic)
            {
                trafficZ -= TrafficSpeed * dt;
                if (trafficZ < -RoadHalf + 20f) trafficZ = 420f;
                Vector3 p = Axis(trafficZ) - RightAt(trafficZ) * (RoadNetwork.LaneWidth * 0.5f);
                traffic.localPosition = p + Vector3.up * trafficLift;
                Vector3 fwd = Axis(trafficZ - 1f) - Axis(trafficZ + 1f);
                traffic.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
            }
        }
    }
}
