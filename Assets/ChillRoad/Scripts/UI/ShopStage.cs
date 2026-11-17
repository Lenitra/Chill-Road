using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Décor intérieur d'une boutique (hall de la concession, atelier de tuning) : construit à l'écart du monde, en
    /// formes simples aux matériaux cartoon, filmé par sa propre caméra qui prend la place de celle du jeu tant que la
    /// boutique est ouverte. L'éclairage est celui d'un studio (<see cref="DayNightCycle.SetStudio"/>) : le même à
    /// toute heure et par tous les temps. Les classes dérivées construisent le décor et animent la caméra.
    /// </summary>
    public abstract class ShopStage : MonoBehaviour
    {
        /// <summary>Couche réservée aux décors de boutique : leur caméra ne rend qu'elle.</summary>
        public const int Layer = 31;

        protected GameManager game;
        protected Transform root;
        protected Camera cam;
        protected StudioLighting lighting;
        /// <summary>Coup de focale (degrés) qui retombe en une fraction de seconde (achat).</summary>
        protected float punch;

        readonly List<Object> owned = new List<Object>();
        readonly List<(Transform pivot, VehicleSpec spec)> placed = new List<(Transform, VehicleSpec)>();
        Camera mainCam;
        int mainMask;
        static Mesh cube, cylinder;

        public bool Visible { get; private set; }

        // ------------------------------------------------------------------ cycle de vie

        /// <summary>Racine du décor (active pendant la construction : les encombrements des modèles sont justes).</summary>
        protected void Create(GameManager g, string name, Vector3 origin, Color background)
        {
            game = g;
            root = new GameObject(name).transform;
            root.position = origin;

            mainCam = game.Camera.GetComponent<Camera>();
            var go = new GameObject(name + "Camera");
            cam = go.AddComponent<Camera>();
            cam.CopyFrom(mainCam);
            var d1 = mainCam.GetUniversalAdditionalCameraData();
            var d2 = cam.GetUniversalAdditionalCameraData();
            d2.renderPostProcessing = d1.renderPostProcessing;
            d2.antialiasing = d1.antialiasing;
            d2.antialiasingQuality = d1.antialiasingQuality;
            d2.renderShadows = d1.renderShadows;
            d2.volumeLayerMask = d1.volumeLayerMask;
            cam.targetTexture = null;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.depth = mainCam.depth + 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 260f;
            cam.enabled = false;
        }

        /// <summary>Fin de la construction : décor inerte (aucun collider), sur sa couche, caché jusqu'à l'ouverture.</summary>
        protected void Finish()
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Destroy(c);
            SetLayer(root);
            root.gameObject.SetActive(false);
        }

        protected static void SetLayer(Transform t)
        {
            foreach (var child in t.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
        }

        /// <summary>
        /// Le décor remplace le monde à l'écran. Avec un point de vue (<see cref="viewPoint"/>), la caméra du jeu filme
        /// le vrai paysage depuis l'emplacement réel de la boutique, dans une texture affichée au fond du décor : on
        /// le voit par les vitres. Sinon, elle ne rend plus rien pendant ce temps.
        /// </summary>
        public void Show()
        {
            if (Visible || !root) return;
            Visible = true;
            root.gameObject.SetActive(true);
            cam.enabled = true;
            mainMask = mainCam.cullingMask;
            if (viewPoint.HasValue && game.Map != null) BeginBackdrop();
            else mainCam.cullingMask = 0;
            if (game.DayNight) game.DayNight.SetStudio(lighting);
            Tick(0f);
            UpdateBackdrop();
        }

        public void Hide()
        {
            if (!Visible) return;
            Visible = false;
            if (root) root.gameObject.SetActive(false);
            if (cam) cam.enabled = false;
            EndBackdrop();
            if (mainCam) mainCam.cullingMask = mainMask;
            if (game && game.DayNight) game.DayNight.SetStudio(null);
        }

        void LateUpdate()
        {
            if (!Visible) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            punch = Mathf.MoveTowards(punch, 0f, dt * 26f);
            Tick(dt);
            UpdateBackdrop();
        }

        // ------------------------------------------------------------------ vue sur le vrai paysage

        /// <summary>Point de la carte (emplacement réel de la boutique) d'où l'on voit le paysage par les vitres ; nul : pas de vue.</summary>
        protected Vector3? viewPoint;
        bool viewPicked, backdropOn, mainPost;
        Vector3 viewEye;
        Quaternion viewYaw = Quaternion.identity;
        RenderTexture backdrop;
        Transform backdropQuad;
        Material backdropMaterial;

        /// <summary>
        /// Œil et cap de la vue : au-dessus des toits voisins (18 m au-dessus du sol), tourné vers le relief le plus haut
        /// à l'horizon, sans bâtiment ni talus dans les 150 premiers mètres.
        /// </summary>
        void PickView()
        {
            viewPicked = true;
            var map = game.Map;
            Vector3 p = viewPoint.Value;
            viewEye = new Vector3(p.x, map.Height(p.x, p.z) + 18f, p.z);
            float best = float.MinValue, bestYaw = 0f, limit = map.Half - 50f;
            int mask = ~((1 << 2) | (1 << Layer));
            for (int k = 0; k < 24; k++)
            {
                float yaw = k * 15f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float score = -1f;
                for (float d = 150f; d <= 3500f; d += 50f)
                {
                    Vector3 q = viewEye + dir * d;
                    if (Mathf.Abs(q.x) > limit || Mathf.Abs(q.z) > limit) break;
                    score = Mathf.Max(score, (map.Height(q.x, q.z) - viewEye.y) / d);
                }
                if (Physics.Raycast(viewEye, dir, 150f, mask, QueryTriggerInteraction.Ignore)) score -= 2f;
                if (Physics.Raycast(viewEye, dir + Vector3.down * 0.12f, 150f, mask, QueryTriggerInteraction.Ignore)) score -= 0.5f;
                if (score > best) { best = score; bestYaw = yaw; }
            }
            viewYaw = Quaternion.Euler(0f, bestYaw, 0f);
        }

        void BeginBackdrop()
        {
            if (!viewPicked) PickView();
            if (!backdropQuad)
            {
                // Toile de fond collée au loin devant la caméra du décor : aplat noir, image du monde en émission
                var go = new GameObject("Backdrop", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(cam.transform, false);
                go.layer = Layer;
                var mesh = new Mesh { name = "ShopBackdrop" };
                mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
                mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                owned.Add(mesh);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                backdropMaterial = Flat(Color.black, 0f, 0f);
                backdropMaterial.EnableKeyword("_EMISSION");
                backdropMaterial.SetColor("_EmissionColor", Color.white);
                if (backdropMaterial.HasProperty("_RimStrength")) backdropMaterial.SetFloat("_RimStrength", 0f);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = backdropMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                backdropQuad = go.transform;
            }
            backdropQuad.gameObject.SetActive(true);
            ResizeBackdrop();
            // La caméra du jeu quitte la voiture : elle filme le paysage pour le décor, sans post-traitement (le décor
            // l'applique une fois à toute l'image)
            game.Camera.Directed = true;
            var data = mainCam.GetUniversalAdditionalCameraData();
            mainPost = data.renderPostProcessing;
            data.renderPostProcessing = false;
            mainCam.targetTexture = backdrop;
            backdropOn = true;
        }

        void EndBackdrop()
        {
            if (!backdropOn) return;
            backdropOn = false;
            if (mainCam)
            {
                mainCam.targetTexture = null;
                mainCam.GetUniversalAdditionalCameraData().renderPostProcessing = mainPost;
            }
            if (game && game.Camera) game.Camera.Directed = false;
            if (backdropQuad) backdropQuad.gameObject.SetActive(false);
        }

        void ResizeBackdrop()
        {
            int w = Mathf.Max(64, Screen.width * 3 / 4), h = Mathf.Max(64, Screen.height * 3 / 4);
            if (backdrop && backdrop.width == w && backdrop.height == h) return;
            if (backdrop) { if (mainCam && mainCam.targetTexture == backdrop) mainCam.targetTexture = null; backdrop.Release(); Destroy(backdrop); }
            backdrop = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "ShopBackdrop (runtime)" };
            backdrop.Create();
            backdropMaterial.SetTexture("_EmissionMap", backdrop);
            if (backdropOn) mainCam.targetTexture = backdrop;
        }

        /// <summary>Caméra du jeu alignée sur celle du décor (même cap relatif, même focale) ; toile de fond à la taille du champ.</summary>
        void UpdateBackdrop()
        {
            if (!backdropOn || !mainCam) return;
            ResizeBackdrop();
            // Un peu de parallaxe : la caméra du décor glisse le long du hall, celle du monde bouge dix fois moins
            Vector3 local = cam.transform.position - root.position;
            mainCam.transform.SetPositionAndRotation(viewEye + viewYaw * new Vector3(local.x * 0.1f, 0f, local.z * 0.1f),
                viewYaw * cam.transform.rotation);
            mainCam.fieldOfView = cam.fieldOfView;
            float d = cam.farClipPlane * 0.9f;
            float hh = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            backdropQuad.localPosition = new Vector3(0f, 0f, d);
            backdropQuad.localRotation = Quaternion.identity;
            backdropQuad.localScale = new Vector3(hh * cam.aspect * 1.02f, hh * 1.02f, 1f);
        }

        /// <summary>Animation du décor et de sa caméra, à chaque image tant qu'il est affiché.</summary>
        protected abstract void Tick(float dt);

        /// <summary>Coup de focale et léger sursaut de lumière (achat).</summary>
        public void Punch(float degrees = 7f) => punch = Mathf.Max(punch, degrees);

        void OnDestroy()
        {
            Hide();
            if (root) Destroy(root.gameObject);
            if (cam) Destroy(cam.gameObject);
            foreach (var o in owned) if (o) Destroy(o);
            if (backdrop) { backdrop.Release(); Destroy(backdrop); }
        }

        // ------------------------------------------------------------------ matériaux

        /// <summary>Aplat cartoon uni (shader du jeu), sans reflet dessiné par défaut.</summary>
        protected Material Flat(Color color, float smoothness = 0.1f, float spec = 0f)
        {
            var shader = Shader.Find("ChillRoad/Toon");
            var m = shader ? new Material(shader) : new Material(game.assets.concrete);
            m.name = "Shop (runtime)";
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", spec);
            m.enableInstancing = true;
            owned.Add(m);
            return m;
        }

        /// <summary>Surface lumineuse (tubes, néons, enseignes) : émission au-delà du blanc, reprise par le halo.</summary>
        protected Material Glow(Color color, float intensity)
        {
            var m = Flat(color * 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            return m;
        }

        /// <summary>Copie d'un matériau du jeu (retirée avec le décor).</summary>
        protected Material Copy(Material source)
        {
            var m = new Material(source) { name = source.name + " (shop)" };
            owned.Add(m);
            return m;
        }

        // ------------------------------------------------------------------ formes

        /// <summary>Boîte (centre et taille dans le repère du parent).</summary>
        protected Transform Box(Transform parent, Vector3 center, Vector3 size, Material m, bool shadows = false, Vector3 euler = default)
        {
            return Piece(parent, "Box", Cube(), center, size, euler, m, shadows);
        }

        /// <summary>Cylindre d'axe vertical (centre, rayon, hauteur), couché par « euler » au besoin.</summary>
        protected Transform Cyl(Transform parent, Vector3 center, float radius, float height, Material m, bool shadows = false, Vector3 euler = default)
        {
            return Piece(parent, "Cylinder", Cylinder(), center, new Vector3(radius * 2f, height, radius * 2f), euler, m, shadows);
        }

        static Transform Piece(Transform parent, string name, Mesh mesh, Vector3 center, Vector3 scale, Vector3 euler, Material m, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        protected Transform Group(Transform parent, string name, Vector3 local, float yaw = 0f)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        /// <summary>Projecteur (position et point visé dans le repère du décor).</summary>
        protected Light Spot(Vector3 local, Vector3 target, Color color, float intensity, float range, float angle)
        {
            var go = new GameObject("Spot");
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.LookRotation(target - local, Vector3.forward);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.5f;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>Lumière ponctuelle d'ambiance (néon coloré).</summary>
        protected Light Glint(Vector3 local, Color color, float intensity, float range)
        {
            var go = new GameObject("Glint");
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>
        /// Modèle d'une voiture posé sur un pivot (repère du parent, non tourné à cet instant) : centré sur le pivot,
        /// roues à sa hauteur. bounds : encombrement dans le repère du pivot.
        /// </summary>
        protected Transform PlaceCar(VehicleSpec spec, Transform parent, Vector3 local, out Bounds bounds)
        {
            var pivot = Group(parent, "Car " + (spec != null ? spec.id : ""), local);
            bounds = new Bounds(new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 1.3f, 4.2f));
            if (spec == null || !spec.prefab) return pivot;
            var go = Instantiate(spec.prefab, pivot);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * spec.modelScale;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                Vector3 c = b.center - pivot.position;
                go.transform.position += new Vector3(-c.x, pivot.position.y - b.min.y, -c.z);
                bounds = new Bounds(new Vector3(0f, b.extents.y, 0f), b.size);
            }
            SetLayer(pivot);
            // Couleur choisie au garage de tuning
            CarPaint.Apply(pivot, CarPaint.Selected(spec));
            placed.Add((pivot, spec));
            return pivot;
        }

        /// <summary>Remet aux voitures posées la couleur choisie au garage de tuning (elle a pu changer depuis).</summary>
        public void Repaint()
        {
            placed.RemoveAll(p => !p.pivot);
            foreach (var (pivot, spec) in placed) CarPaint.Apply(pivot, CarPaint.Selected(spec));
        }

        Material packMaterial;

        /// <summary>
        /// Matériau du pack alpin pour les décors de boutique : fenêtres, néons et lampes toujours allumés (dans le
        /// monde, leur émission suit la nuit). Nul si le pack n'est pas importé.
        /// </summary>
        protected Material PackMaterial
        {
            get
            {
                if (packMaterial) return packMaterial;
                var alp = game.assets.alp;
                if (!alp || !alp.material) return null;
                packMaterial = Copy(alp.material);
                packMaterial.EnableKeyword("_EMISSION");
                packMaterial.SetColor("_EmissionColor", Color.white * 1.6f);
                return packMaterial;
            }
        }

        /// <summary>Modèle du pack alpin existe-t-il (décor Blender importé) ?</summary>
        protected bool HasPack(string id) => game.assets.alp && game.assets.alp.Prefab(id) && PackMaterial;

        /// <summary>
        /// Décor du pack alpin (repère du menu : x et y du fichier Blender deviennent x et z) : niveau détaillé seul,
        /// matériau à lampes allumées. mirror : symétrie gauche-droite (mur de bout de droite du hall).
        /// </summary>
        protected GameObject PlacePack(string id, Transform parent, Vector3 local, bool mirror = false, float yaw = 0f)
        {
            var prefab = game.assets.alp ? game.assets.alp.Prefab(id) : null;
            if (!prefab || !PackMaterial) return null;
            var go = Instantiate(prefab, parent);
            go.name = id;
            go.transform.localPosition = local;
            // Le pack est exporté façade vers +z : un demi-tour remet x et y de Blender sur x et z du menu
            go.transform.localRotation = Quaternion.Euler(0f, 180f + yaw, 0f);
            go.transform.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            foreach (Transform child in go.transform)
                if (!child.name.EndsWith("_LOD0")) child.gameObject.SetActive(false);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            // Pas d'ombre portée : le plafond plongerait toute la pièce dans l'ombre de la lumière de studio
            // (seules les voitures portent la leur). Le verre passe dans un second sous-maillage, transparent.
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf ? SplitGlass(mf.sharedMesh) : null;
                if (mesh) mf.sharedMesh = mesh;
                r.sharedMaterials = mesh && mesh.subMeshCount > 1 && GlassMaterial
                    ? new[] { PackMaterial, GlassMaterial } : new[] { PackMaterial };
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return go;
        }

        /// <summary>Colonne « glass_blue » de la palette du pack (Tools/Blender/alp_lib.py, 128 colonnes) : le verre.</summary>
        const int GlassColumn = 74, PaletteColumns = 128;
        readonly Dictionary<Mesh, Mesh> glassMeshes = new Dictionary<Mesh, Mesh>();
        Material glassMaterial;

        /// <summary>Verre : teinte bleutée presque transparente (matériau transparent du jeu, celui des particules).</summary>
        Material GlassMaterial
        {
            get
            {
                if (glassMaterial || !game.assets.particle) return glassMaterial;
                glassMaterial = Copy(game.assets.particle);
                glassMaterial.name = "ShopGlass (runtime)";
                if (glassMaterial.HasProperty("_BaseMap")) glassMaterial.SetTexture("_BaseMap", null);
                glassMaterial.SetColor("_BaseColor", new Color(0.72f, 0.86f, 0.95f, 0.16f));
                return glassMaterial;
            }
        }

        /// <summary>Copie du maillage où les triangles de verre forment un second sous-maillage (le même s'il n'en a pas).</summary>
        Mesh SplitGlass(Mesh src)
        {
            if (!src) return null;
            if (glassMeshes.TryGetValue(src, out var done)) return done;
            if (!src.isReadable) { glassMeshes[src] = src; return src; }
            var uv = src.uv;
            var tris = src.triangles;
            var solid = new List<int>(tris.Length);
            var glass = new List<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                bool isGlass = uv.Length > tris[i] && Mathf.FloorToInt(uv[tris[i]].x * PaletteColumns) == GlassColumn;
                var list = isGlass ? glass : solid;
                list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
            }
            if (glass.Count == 0) { glassMeshes[src] = src; return src; }
            var m = Instantiate(src);
            m.name = src.name + " (verre)";
            m.subMeshCount = 2;
            m.SetTriangles(solid, 0);
            m.SetTriangles(glass, 1);
            owned.Add(m);
            glassMeshes[src] = m;
            return m;
        }

        /// <summary>Modèle du jeu (cône, podium…) posé au sol du décor, bas du modèle à la hauteur donnée.</summary>
        protected GameObject PlaceProp(GameObject prefab, Transform parent, Vector3 local, float yaw, float scale = 1f)
        {
            if (!prefab) return null;
            var go = Instantiate(prefab, parent);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += Vector3.up * (parent.TransformPoint(local).y - b.min.y);
            }
            return go;
        }

        // ------------------------------------------------------------------ maillages

        static Mesh Cube()
        {
            if (cube) return cube;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 a = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(d, a);
                int i = v.Count;
                v.Add((d - a - b) * 0.5f); v.Add((d + a - b) * 0.5f); v.Add((d + a + b) * 0.5f); v.Add((d - a + b) * 0.5f);
                for (int k = 0; k < 4; k++) n.Add(d);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            cube = new Mesh { name = "ShopCube" };
            cube.SetVertices(v);
            cube.SetNormals(n);
            cube.SetUVs(0, new Vector2[v.Count]);
            cube.SetTriangles(t, 0);
            cube.RecalculateBounds();
            return cube;
        }

        /// <summary>Cylindre d'axe Y, diamètre 1, hauteur 1, centré : flanc lisse, deux couvercles.</summary>
        static Mesh Cylinder()
        {
            if (cylinder) return cylinder;
            const int seg = 24;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(dir * 0.5f - Vector3.up * 0.5f); n.Add(dir);
                v.Add(dir * 0.5f + Vector3.up * 0.5f); n.Add(dir);
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 2;
                t.Add(b); t.Add(b + 3); t.Add(b + 1);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            foreach (float side in new[] { 1f, -1f })
            {
                int c = v.Count;
                v.Add(Vector3.up * 0.5f * side); n.Add(Vector3.up * side);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Sin(a) * 0.5f, 0.5f * side, Mathf.Cos(a) * 0.5f)); n.Add(Vector3.up * side);
                }
                for (int i = 0; i < seg; i++)
                {
                    if (side > 0f) { t.Add(c); t.Add(c + 1 + i); t.Add(c + 2 + i); }
                    else { t.Add(c); t.Add(c + 2 + i); t.Add(c + 1 + i); }
                }
            }
            cylinder = new Mesh { name = "ShopCylinder" };
            cylinder.SetVertices(v);
            cylinder.SetNormals(n);
            cylinder.SetUVs(0, new Vector2[v.Count]);
            cylinder.SetTriangles(t, 0);
            cylinder.RecalculateBounds();
            return cylinder;
        }
    }

    /// <summary>Coupe au noir puis fondu, à l'entrée et à la sortie d'une boutique (le décor change d'un coup derrière).</summary>
    public class ShopFade : MonoBehaviour
    {
        static ShopFade instance;
        Image image;
        float alpha, speed = 3f;

        public static void Cut(float duration)
        {
            if (!instance)
            {
                var go = new GameObject("ShopFade", typeof(Canvas));
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 40;
                var img = new GameObject("Black", typeof(RectTransform), typeof(Image));
                img.transform.SetParent(go.transform, false);
                var rt = (RectTransform)img.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                instance = go.AddComponent<ShopFade>();
                instance.image = img.GetComponent<Image>();
                instance.image.raycastTarget = false;
            }
            instance.alpha = 1f;
            instance.speed = 1f / Mathf.Max(0.05f, duration);
            instance.enabled = true;
            instance.Apply();
        }

        void Update()
        {
            alpha = Mathf.MoveTowards(alpha, 0f, Mathf.Min(Time.unscaledDeltaTime, 0.05f) * speed);
            Apply();
            if (alpha <= 0f) enabled = false;
        }

        void Apply()
        {
            image.color = new Color(0f, 0f, 0f, alpha * alpha * (3f - 2f * alpha));
            image.enabled = alpha > 0f;
        }
    }
}
