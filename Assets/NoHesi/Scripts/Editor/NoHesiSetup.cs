using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NoHesi.EditorTools
{
    /// <summary>Génère textures, matériaux, ciel, post-process, base d'assets et scène. Menu : NoHesi > Setup.</summary>
    public static class NoHesiSetup
    {
        const string Root = "Assets/NoHesi";
        const string Gen = Root + "/Generated";
        const string ScenePath = Root + "/NoHesi.unity";

        [MenuItem("NoHesi/Setup (assets + scène)")]
        public static void Setup()
        {
            Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh();
            var assets = BuildAssets();
            BuildScene(assets);
            Debug.Log("NoHesi : setup terminé → " + ScenePath);
        }

        // ------------------------------------------------------------------ assets

        static GameAssets BuildAssets()
        {
            string path = Gen + "/GameAssets.asset";
            var a = AssetDatabase.LoadAssetAtPath<GameAssets>(path);
            if (!a) { a = ScriptableObject.CreateInstance<GameAssets>(); AssetDatabase.CreateAsset(a, path); }

            // Voitures jouables : la N Vision 74 modélisée dans Blender, puis les voitures réelles (plus bas)
            a.playerCars = new VehicleSpec[0];
            var vision74 = Vision74Import.Prepare();
            if (vision74) Vision74Import.AddTo(a, vision74);
            else Debug.LogError("N Vision 74 introuvable : aucune voiture jouable");
            // Trafic et police : pack de voitures réelles modélisées dans Blender
            if (!CarPackImport.Apply(a)) Debug.LogError("Pack de voitures absent (" + CarPackImport.Folder + ") : pas de trafic");
            // Voitures jouables réelles (Supra, MX-5...) : même moteur Blender, elles partagent les matériaux du pack
            PlayerCarsImport.Apply(a);
            // Nature, clôture, mobilier routier et bâtiments : pack d'environnement modélisé dans Blender
            // (Tools/Blender/env_*.py → Models/EnvPack), dans la direction artistique des voitures
            if (!EnvPackImport.Apply(a)) Debug.LogWarning("Pack d'environnement absent (" + EnvPackImport.Folder + ") : décor vide");

            a.asphalt = Lit("Asphalt", Color.white, 0.25f, Tex("AsphaltTex", AsphaltPixel, 512), false);
            a.roadMarking = Lit("Marking", new Color(0.95f, 0.95f, 0.92f), 0.3f, null, false);
            // Sol et terre : remplacés plus bas par les matériaux du monde (NoHesi/Terrain) ; repli sur des Lit simples
            a.terrain = Lit("Terrain", new Color(0.93f, 0.97f, 0.9f), 0.05f, Tex("GrassTex", GrassPixel, 512), false);
            a.dirt = Lit("Dirt", new Color(0.57f, 0.49f, 0.36f), 0.05f, null, false);
            a.dirtRoad = Lit("DirtRoad", Color.white, 0.04f, Tex("GravelTex", GravelPixel, 512), false);
            var softDot = Tex("SoftDot", SoftDotPixel, 128, true);
            a.particle = ParticleMaterial("Dust", softDot);
            a.spark = ParticleMaterial("Spark", softDot, "Universal Render Pipeline/Particles/Unlit", true, new Color(5f, 2.6f, 0.9f, 1f));
            a.skidMark = ParticleMaterial("SkidMark", Tex("SkidTex", SkidPixel, 128, true), "Universal Render Pipeline/Particles/Unlit", false, new Color(0.035f, 0.035f, 0.04f, 0.8f));
            a.concrete = Lit("Concrete", new Color(0.72f, 0.72f, 0.7f), 0.1f, Tex("ConcreteTex", ConcretePixel, 256), true);
            a.metal = Lit("Metal", new Color(0.7f, 0.72f, 0.75f), 0.6f, null, true, 0.8f);
            a.signBlue = Lit("SignBlue", new Color(0.1f, 0.3f, 0.75f), 0.3f, null, true);
            a.signRed = Lit("SignRed", new Color(0.8f, 0.15f, 0.12f), 0.3f, null, true);
            a.emissiveWhite = Emissive("EmissiveWhite", new Color(1f, 0.97f, 0.9f), 4f);
            a.emissiveRed = Emissive("EmissiveRed", new Color(1f, 0.08f, 0.05f), 5f);
            a.emissiveBlue = Emissive("EmissiveBlue", new Color(0.15f, 0.45f, 1f), 5f);
            // Référencé ici pour être embarqué dans le jeu construit (ses matériaux sont créés à l'exécution)
            a.gateFx = Shader.Find("NoHesi/GateFx");
            // Route : shader et textures faits maison (remplacent les matériaux Lit ci-dessus s'ils sont présents)
            RoadMaterials.Apply(a);
            // Terrain, eau, béton, terre : sans texture générique (remplacent les matériaux ci-dessus)
            WorldMaterials.Apply(a);
            EnsureEmission();
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            return a;
        }

        /// <summary>
        /// Active réellement l'émission (mot-clé _EMISSION) de tous les matériaux URP du jeu qui ont une couleur
        /// d'émission : sans lui, feux et lampes ne brillent pas. Le drapeau « émission temps réel » empêche
        /// la validation d'URP de le retirer.
        /// </summary>
        static void EnsureEmission()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m || !m.shader || !m.shader.name.StartsWith("Universal Render Pipeline/") || !m.HasProperty("_EmissionColor")) continue;
                if (m.GetColor("_EmissionColor").maxColorComponent < 0.01f) continue;
                if (m.IsKeywordEnabled("_EMISSION") && m.globalIlluminationFlags == MaterialGlobalIlluminationFlags.RealtimeEmissive) continue;
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(m);
            }
        }

        // ------------------------------------------------------------------ textures procédurales

        static float Fbm(float x, float y, int octaves)
        {
            float v = 0f, amp = 0.5f, f = 1f;
            for (int i = 0; i < octaves; i++) { v += Mathf.PerlinNoise(x * f + 17.3f * i, y * f + 5.1f * i) * amp; amp *= 0.5f; f *= 2f; }
            return v;
        }

        static Color AsphaltPixel(int x, int y, int size, System.Random rng)
        {
            float u = x / (float)size, v = y / (float)size;
            // Tuilable : bruit évalué sur un tore
            float n = TileNoise(u, v, 8f, 4);
            float grain = (float)rng.NextDouble();
            float g = 0.2f + n * 0.08f + (grain > 0.93f ? 0.07f : grain < 0.05f ? -0.04f : 0f);
            return new Color(g, g, g * 1.03f);
        }

        static Color GrassPixel(int x, int y, int size, System.Random rng)
        {
            float u = x / (float)size, v = y / (float)size;
            float n = TileNoise(u, v, 6f, 5);
            float m = TileNoise(u + 0.37f, v + 0.11f, 2f, 3);
            float grain = (float)rng.NextDouble() * 0.06f;
            Color a = new Color(0.25f, 0.42f, 0.16f), b = new Color(0.42f, 0.5f, 0.22f);
            Color c = Color.Lerp(a, b, n) * (0.85f + m * 0.3f + grain);
            c.a = 1f;
            return c;
        }

        static Color ConcretePixel(int x, int y, int size, System.Random rng)
        {
            float n = TileNoise(x / (float)size, y / (float)size, 10f, 3);
            float g = 0.85f + n * 0.15f + (float)rng.NextDouble() * 0.04f;
            return new Color(g, g, g);
        }

        /// <summary>Bruit périodique (tuilable) : échantillonne un bruit 2D sur un cercle dans chaque axe.</summary>
        static float TileNoise(float u, float v, float scale, int octaves)
        {
            float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
            float r = scale / (Mathf.PI * 2f);
            float x = Mathf.Cos(a) * r + Mathf.Cos(b) * r * 0.7f;
            float y = Mathf.Sin(a) * r + Mathf.Sin(b) * r * 0.7f;
            return Fbm(x + 50f, y + 50f, octaves);
        }

        /// <summary>Chemin de terre : gravier, ornières tassées aux passages de roues, bords herbeux (u = travers de la route).</summary>
        static Color GravelPixel(int x, int y, int size, System.Random rng)
        {
            float u = x / (float)size, v = y / (float)size;
            float n = TileNoise(u, v, 12f, 4);
            float m = TileNoise(u + 0.3f, v + 0.7f, 3f, 3);
            float grain = (float)rng.NextDouble();
            Color c = Color.Lerp(new Color(0.52f, 0.46f, 0.37f), new Color(0.68f, 0.61f, 0.5f), n) * (0.88f + m * 0.24f);
            if (grain > 0.9f) c = Color.Lerp(c, new Color(0.72f, 0.69f, 0.62f), (grain - 0.9f) * 7f);
            else if (grain < 0.06f) c *= 0.78f;
            // Ornières aux passages de roues des deux voies (demi-largeur 4,3 m, voies à ±1,65 m)
            float rut = 0f;
            foreach (float r in new[] { 0.215f, 0.401f, 0.599f, 0.785f }) rut = Mathf.Max(rut, 1f - Mathf.Abs(u - r) / 0.035f);
            rut = Mathf.Clamp01(rut) * (0.7f + 0.3f * TileNoise(u, v, 20f, 2));
            c = Color.Lerp(c, new Color(0.35f, 0.3f, 0.24f), rut * 0.6f);
            float edge = Mathf.Clamp01(1f - Mathf.Min(u, 1f - u) / 0.07f);
            c = Color.Lerp(c, new Color(0.3f, 0.36f, 0.18f), edge * (0.5f + 0.5f * n));
            c.a = 1f;
            return c;
        }

        /// <summary>Tache ronde et douce (particules de poussière).</summary>
        static Color SoftDotPixel(int x, int y, int size, System.Random rng)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float n = Fbm(x * 0.05f, y * 0.05f, 3);
            float a = Mathf.Clamp01(1f - r);
            a = a * a * (0.6f + 0.6f * n);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }

        static Material ParticleMaterial(string name, Texture2D tex, string shaderName = "Universal Render Pipeline/Particles/Simple Lit", bool additive = false, Color? color = null)
        {
            string path = Gen + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color ?? Color.white);
            // Transparent : mélange alpha ou additif (étincelles, flammes)
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Trace de pneu : sculptures de la bande de roulement, bords estompés (alpha).</summary>
        static Color SkidPixel(int x, int y, int size, System.Random rng)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            float edge = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.18f);
            float tread = Mathf.Repeat(v * 12f + (u > 0.5f ? 0.5f : 0f), 1f) < 0.7f ? 1f : 0.55f;
            float grooves = Mathf.Abs(u - 0.34f) < 0.035f || Mathf.Abs(u - 0.66f) < 0.035f ? 0.35f : 1f;
            float n = 0.7f + 0.3f * (float)rng.NextDouble();
            return new Color(1f, 1f, 1f, Mathf.Clamp01(edge * tread * grooves * n));
        }

        /// <summary>Terre et roche pour les pentes : fissures, cailloux, variations de teinte (tuilable).</summary>
        /// <summary>Quatre bruits indépendants (RGBA) tuilables, pour les variations à grande échelle du sol.</summary>
        static Texture2D Tex(string name, System.Func<int, int, int, System.Random, Color> pixel, int size, bool alpha = false, bool data = false)
        {
            string path = Gen + "/" + name + ".png";
            var tex = new Texture2D(size, size, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, true);
            var rng = new System.Random(name.GetHashCode());
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel(x, y, size, rng));
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = alpha && !data ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            imp.alphaIsTransparency = alpha && !data;
            // Données (bruit) : pas de conversion sRGB
            imp.sRGBTexture = !data;
            imp.anisoLevel = 8;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Lit(string name, Color color, float smoothness, Texture2D tex, bool twoSided, float metallic = 0f)
        {
            string path = Gen + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emissive(string name, Color color, float intensity)
        {
            var m = Lit(name, color, 0.5f, null, true);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ scène

        static void BuildScene(GameAssets assets)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Brume accordée à l'horizon du ciel (NoHesi/SkyDayNight)
            Color haze = new Color(0.6f, 0.8f, 0.9f);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(38f, -40f, 0f);

            // Ciel jour/nuit procédural (nuages en aplats, animé par DayNightCycle) ; repli sur le ciel procédural d'Unity
            var sky = BuildSky();
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            // Lumière ambiante en trois couleurs (ciel, horizon, sol), pilotée en jeu par le cycle jour/nuit
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.67f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.68f, 0.7f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.35f, 0.3f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0011f;
            RenderSettings.fogColor = haze;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2500f;
            camGo.AddComponent<AudioListener>();
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rig = camGo.AddComponent<CameraRig>();

            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildProfile();

            var gameGo = new GameObject("Game");
            var gm = gameGo.AddComponent<GameManager>();
            gm.assets = assets;
            gm.cameraRig = rig;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static Material BuildSky()
        {
            // Ciel procédural (dégradé, nuages en aplats) ; repli sur le ciel procédural d'Unity si le shader manque
            var sky = WorldMaterials.Sky();
            if (sky) return sky;
            string fallback = Gen + "/Sky.mat";
            var proc = AssetDatabase.LoadAssetAtPath<Material>(fallback);
            if (!proc) { proc = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(proc, fallback); }
            return proc;
        }

        static VolumeProfile BuildProfile()
        {
            string path = Gen + "/PostProcess.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);

            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = p.Add<Bloom>(true);
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.65f);
            var color = p.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.25f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
            var vignette = p.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
            // Pas de flou de mouvement : il rendait la voiture floue en permanence (la vitesse passe par les traînées,
            // le champ de vision et l'aberration chromatique)
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);

            foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }
    }
}
