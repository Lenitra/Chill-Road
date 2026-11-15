using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NoHesi
{
    /// <summary>
    /// Décor 3D de l'écran de chargement, fait des assets du jeu : la voiture choisie garée sur une nationale (enrobé,
    /// marquages et accotements du shader de route), prairie vallonnée à facettes (shader de terrain), pins, verger et
    /// sa clôture, buissons, rochers et fleurs du pack d'environnement, une voiture du trafic qui passe, sous le ciel, la
    /// lumière et la météo du jeu. Construit à l'écart du monde (qui se génère pendant ce temps) et filmé par sa propre
    /// caméra, qui tourne lentement autour de la voiture, dans une texture affichée derrière l'interface.
    /// </summary>
    public class LoadingStage : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(0f, 2500f, -6000f);
        const float Half = 360f;                 // demi-longueur de la route et du terrain (m)
        const float Embankment = 2.2f;           // la route est sur un remblai, comme en jeu
        const float TrafficSpeed = 21f;

        public RenderTexture Texture { get; private set; }
        /// <summary>Caméra du décor (la pluie de l'orage tombe aussi autour d'elle).</summary>
        public Camera Camera => cam;

        GameObject root;
        Camera cam;
        Vector3 carCenter;
        float carRadius = 2.5f, angle = 232f, time;
        Transform traffic;
        float trafficZ = 300f;
        readonly List<Collider> colliders = new List<Collider>();

        public void Build(GameAssets assets, VehicleSpec car, Camera template)
        {
            root = new GameObject("LoadingStage");
            root.transform.position = Origin;
            var rng = new System.Random(2024);
            BuildRoad(assets);
            BuildTerrain(assets);
            PlaceCar(car);
            Scatter(assets, rng);
            if (assets.trafficCars != null && assets.trafficCars.Length > 0)
            {
                var t = Place(assets.trafficCars[rng.Next(assets.trafficCars.Length)], new Vector3(-RoadNetwork.LaneWidth * 0.5f, 0f, trafficZ), 180f, assets.trafficCarScale);
                traffic = t ? t.transform : null;
            }
            // Décor inerte : aucun collider (le monde réel se construit à côté)
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Destroy(c);
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
            const float step = 10f;
            for (float z = -Half; z < Half; z += step)
            {
                float z2 = z + step;
                Vector3 a = new Vector3(0f, 0f, z), b = new Vector3(0f, 0f, z2);
                road.Quad(a - Vector3.right * hw, a + Vector3.right * hw, b - Vector3.right * hw, b + Vector3.right * hw,
                    new Vector2(-hw, z + Half), new Vector2(hw, z + Half), new Vector2(-hw, z2 + Half), new Vector2(hw, z2 + Half));
                // Accotement en gravier puis talus enherbé qui descend vers la prairie
                float ga = z + Half, gb = z2 + Half;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 r = Vector3.right * side;
                    Vector3 ea = a + r * hw, eb = b + r * hw;
                    Vector3 sa = ea + r * 1.2f - Vector3.up * 0.06f, sb = eb + r * 1.2f - Vector3.up * 0.06f;
                    Vector3 oa = sa + r * 5f - Vector3.up * Embankment, ob = sb + r * 5f - Vector3.up * Embankment;
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
            for (float z = -Half; z < Half; z += 12f) Mark(marks, 0f, 0.15f, z, z + 4.5f);

            AddMesh(root.transform, "Road", road.Build("LoadingRoad"), assets.asphalt, true);
            AddMesh(root.transform, "Verge", verge.Build("LoadingVerge"), assets.verge ? assets.verge : assets.dirt, true);
            AddMesh(root.transform, "Markings", marks.Build("LoadingMarks"), assets.roadMarking, false);
        }

        static void Mark(RoadNetwork.MeshBuilder mb, float x, float width, float z0, float z1)
        {
            Vector3 up = Vector3.up * 0.025f;
            float w = width * 0.5f;
            mb.Quad(new Vector3(x - w, 0f, z0) + up, new Vector3(x + w, 0f, z0) + up, new Vector3(x - w, 0f, z1) + up, new Vector3(x + w, 0f, z1) + up);
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>Hauteur de la prairie : à plat au pied du remblai, collines à facettes au loin.</summary>
        static float Ground(float x, float z)
        {
            float away = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 140f, Mathf.Abs(x)));
            float hills = Mathf.PerlinNoise(x * 0.0055f + 11.3f, z * 0.0055f + 4.1f) * 46f + Mathf.PerlinNoise(x * 0.021f + 3.7f, z * 0.021f) * 7f;
            return -Embankment - 0.1f + away * hills;
        }

        static float Forest(float x, float z) => Mathf.PerlinNoise(x * 0.012f + 40f, z * 0.012f + 17f);

        void BuildTerrain(GameAssets assets)
        {
            const float step = 16f;
            int n = Mathf.RoundToInt(Half * 2f / step);
            var v = new Vector3[(n + 1) * (n + 1)];
            var c = new Color[v.Length];
            for (int iz = 0; iz <= n; iz++)
            for (int ix = 0; ix <= n; ix++)
            {
                float x = -Half + ix * step, z = -Half + iz * step;
                float h = Ground(x, z);
                int i = iz * (n + 1) + ix;
                v[i] = new Vector3(x, h, z);
                // Masques du shader de terrain : r = sous-bois, g = herbe sèche, b = roche, a = terre battue
                float forest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.68f, Forest(x, z))) * Mathf.InverseLerp(20f, 40f, Mathf.Abs(x));
                float dry = Mathf.Clamp01(Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f) - 0.45f) * 1.6f;
                float dirt = Mathf.Clamp01(1f - (Mathf.Abs(x) - RoadNetwork.CountryHalfWidth - 4f) / 5f);
                c[i] = new Color(forest * (1f - dirt), dry, Mathf.InverseLerp(30f, 44f, h) * 0.6f, dirt);
            }
            var t = new List<int>();
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                int i = iz * (n + 1) + ix;
                t.Add(i); t.Add(i + n + 1); t.Add(i + 1);
                t.Add(i + 1); t.Add(i + n + 1); t.Add(i + n + 2);
            }
            var m = new Mesh { name = "LoadingGround" };
            m.vertices = v;
            m.colors = c;
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            AddMesh(root.transform, "Ground", m, assets.terrain, true);
        }

        // ------------------------------------------------------------------ voiture et nature

        void PlaceCar(VehicleSpec spec)
        {
            if (spec == null || !spec.prefab) return;
            var go = Place(spec.prefab, new Vector3(RoadNetwork.LaneWidth * 0.5f, 0f, 0f), 0f, spec.modelScale);
            if (!go) return;
            var b = BoundsOf(go);
            carCenter = b.center;
            carRadius = Mathf.Max(b.extents.x, b.extents.z);
        }

        void Scatter(GameAssets a, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            float Rr(float lo, float hi) => Mathf.Lerp(lo, hi, R());

            // Verger derrière une clôture en bois, côté gauche (comme le long des nationales du jeu)
            if (a.fruitTrees != null && a.fruitTrees.Length > 0)
                for (float z = -70f; z <= 90f; z += 9f)
                for (float x = -20f; x >= -52f; x -= 8f)
                    Nature(a.fruitTrees[rng.Next(a.fruitTrees.Length)], x + Rr(-1f, 1f), z + Rr(-1f, 1f), R() * 360f, Rr(0.9f, 1.2f));
            if (a.fence) Fence(a.fence, -14.5f, -80f, 100f);

            // Forêts de pins sur les collines, buissons et herbes hautes au pied du remblai, rochers et fleurs
            for (int i = 0; i < 700; i++)
            {
                float x = Rr(-Half, Half), z = Rr(-Half, Half);
                float ax = Mathf.Abs(x);
                if (ax < 14f) continue;
                bool orchard = x < -12f && x > -58f && z > -80f && z < 100f;
                float roll = R();
                if (!orchard && Forest(x, z) > 0.52f && ax > 22f && a.pines != null && a.pines.Length > 0)
                    Nature(a.pines[rng.Next(a.pines.Length)], x, z, R() * 360f, Rr(0.9f, 1.6f));
                else if (roll < 0.25f && ax < 60f && a.shrubs != null && a.shrubs.Length > 0)
                    Nature(a.shrubs[rng.Next(a.shrubs.Length)], x, z, R() * 360f, Rr(0.9f, 1.4f));
                else if (roll < 0.45f && ax < 45f && a.grass != null && a.grass.Length > 0)
                    Nature(a.grass[rng.Next(a.grass.Length)], x, z, R() * 360f, Rr(0.9f, 1.3f));
                else if (roll < 0.55f && ax < 45f && a.flowers != null && a.flowers.Length > 0)
                    Nature(a.flowers[rng.Next(a.flowers.Length)], x, z, R() * 360f, Rr(0.9f, 1.2f));
                else if (roll < 0.62f && a.bigRocks != null && a.bigRocks.Length > 0 && ax > 30f)
                    Nature(a.bigRocks[rng.Next(a.bigRocks.Length)], x, z, R() * 360f, Rr(0.7f, 1.3f));
                else if (roll < 0.72f && a.smallRocks != null && a.smallRocks.Length > 0)
                    Nature(a.smallRocks[rng.Next(a.smallRocks.Length)], x, z, R() * 360f, Rr(0.8f, 1.4f));
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
            cam.farClipPlane = 1500f;
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

        /// <summary>Caméra qui tourne lentement autour de la voiture, à hauteur d'homme (comme la vue de l'accueil).</summary>
        void UpdateCamera(float dt)
        {
            // Pas de saut quand une image est lente (construction du monde en parallèle)
            angle += Mathf.Min(dt, 0.05f) * 5f;
            float dist = carRadius * 3.3f + 1.5f;
            Vector3 center = carCenter + Vector3.up * 0.1f;
            Quaternion around = Quaternion.Euler(0f, angle, 0f);
            Vector3 pos = center + around * new Vector3(0f, 0.9f, -dist);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center + Vector3.up * 0.25f - pos, Vector3.up));
        }

        void LateUpdate()
        {
            if (!cam) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            time += dt;
            Resize();
            UpdateCamera(dt);
            // Une voiture du trafic passe de temps en temps sur la voie d'en face
            if (traffic)
            {
                trafficZ -= TrafficSpeed * dt;
                if (trafficZ < -Half + 20f) trafficZ = Half - 20f;
                var p = traffic.localPosition;
                p.z = trafficZ;
                traffic.localPosition = p;
            }
        }
    }
}
