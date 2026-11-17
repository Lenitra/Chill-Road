using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ChillRoad.Rendering;

namespace ChillRoad
{
    /// <summary>
    /// Qualité graphique (menu Options) : 0 basse, 1 moyenne, 2 haute, 3 ultra. Règle le rendu URP (échelle de rendu,
    /// ombres, anticrénelage, occlusion ambiante), les contours cartoon, la flore au sol et la limite d'images par
    /// seconde. La distance des détails (niveaux de détail des arbres, rochers, maisons et objets posés) est une
    /// option à part (<see cref="GameSettings.DetailDistance"/>), remise au niveau conseillé quand la qualité change.
    /// Le rendu URP est réglé sur une copie de l'asset du projet (créée au premier appel) : l'asset d'origine n'est
    /// jamais modifié, même dans l'éditeur.
    /// </summary>
    public static class GraphicsQuality
    {
        public const int Low = 0, Medium = 1, High = 2, Ultra = 3;

        struct Preset
        {
            public float renderScale, shadowDistance;
            public int cascades, shadowRes, msaa;
            public bool ssao, outline, toonDetail, extraShadows;
            public float floraDensity, floraFar, floraMid, denseFar;
            public bool denseCarpet;
            public float reflectionInterval;
        }

        static readonly Preset[] Presets =
        {
            // Basse : 80 % de la définition, une cascade d'ombre courte, ni SSAO ni contours, flore clairsemée
            new Preset { renderScale = 0.8f, shadowDistance = 60f, cascades = 1, shadowRes = 1024, msaa = 1,
                         ssao = false, outline = false, toonDetail = false, extraShadows = false,
                         floraDensity = 0.45f, floraFar = 120f, floraMid = 60f, denseFar = 0f,
                         denseCarpet = false, reflectionInterval = 2f },
            // Moyenne
            new Preset { renderScale = 0.9f, shadowDistance = 100f, cascades = 2, shadowRes = 2048, msaa = 1,
                         ssao = false, outline = true, toonDetail = true, extraShadows = false,
                         floraDensity = 0.7f, floraFar = 170f, floraMid = 85f, denseFar = 40f,
                         denseCarpet = true, reflectionInterval = 1f },
            // Haute (réglages d'origine du jeu)
            new Preset { renderScale = 1f, shadowDistance = 140f, cascades = 4, shadowRes = 2048, msaa = 1,
                         ssao = true, outline = true, toonDetail = true, extraShadows = true,
                         floraDensity = 1f, floraFar = 230f, floraMid = 110f, denseFar = 62f,
                         denseCarpet = true, reflectionInterval = 0.5f },
            // Ultra : ombres plus loin et plus fines, MSAA 4x, flore plus loin
            new Preset { renderScale = 1f, shadowDistance = 220f, cascades = 4, shadowRes = 4096, msaa = 4,
                         ssao = true, outline = true, toonDetail = true, extraShadows = true,
                         floraDensity = 1f, floraFar = 280f, floraMid = 140f, denseFar = 75f,
                         denseCarpet = true, reflectionInterval = 0.5f },
        };

        /// <summary>
        /// Distance des détails : facteur des niveaux de détail des LODGroup (maisons, ponts, objets posés) et distances
        /// de la végétation instanciée (modèle complet, intermédiaire, petits objets dessinés seulement de près).
        /// </summary>
        struct DetailPreset
        {
            public float lodBias, vegLod1, vegLod2, vegDetail;
        }

        static readonly DetailPreset[] Details =
        {
            new DetailPreset { lodBias = 0.8f, vegLod1 = 25f, vegLod2 = 110f, vegDetail = 150f },   // courte (ancienne basse)
            new DetailPreset { lodBias = 1.6f, vegLod1 = 45f, vegLod2 = 170f, vegDetail = 220f },   // normale (ancienne haute)
            new DetailPreset { lodBias = 3f, vegLod1 = 70f, vegLod2 = 240f, vegDetail = 320f },     // loin
            new DetailPreset { lodBias = 4.5f, vegLod1 = 110f, vegLod2 = 360f, vegDetail = 450f },  // très loin
            new DetailPreset { lodBias = 7f, vegLod1 = 170f, vegLod2 = 560f, vegDetail = 650f },    // maximale
        };

        public const int DetailLevels = 5;

        static readonly (string fr, string en)[] DetailNames =
            { ("Courte", "Short"), ("Normale", "Normal"), ("Loin", "Far"), ("Très loin", "Very far"), ("Maximale", "Maximum") };

        public static string DetailName(int level) { var n = DetailNames[Mathf.Clamp(level, 0, DetailLevels - 1)]; return Loc.T(n.fr, n.en); }

        /// <summary>Distance des détails conseillée pour un niveau de qualité (basse → courte … ultra → très loin).</summary>
        public static int DefaultDetail(int quality) => Mathf.Clamp(quality, Low, Ultra);

        static readonly (string fr, string en)[] Names = { ("Basse", "Low"), ("Moyenne", "Medium"), ("Haute", "High"), ("Ultra", "Ultra") };

        /// <summary>Nom du niveau, dans la langue de l'interface.</summary>
        public static string Name(int level) { var n = Names[Mathf.Clamp(level, 0, 3)]; return Loc.T(n.fr, n.en); }

        /// <summary>Limites d'images par seconde proposées (0 = illimité).</summary>
        public static readonly int[] FpsCaps = { 30, 60, 120, 0 };

        public static string FpsName(int index)
        {
            int cap = FpsCaps[Mathf.Clamp(index, 0, FpsCaps.Length - 1)];
            return cap == 0 ? Loc.T("Illimitée", "Unlimited") : Loc.T($"{cap} images/s", $"{cap} fps");
        }

        // ------------------------------------------------------------------ flou de mouvement

        /// <summary>Force du flou de mouvement une fois activé (léger : le décor file, l'image reste lisible).</summary>
        const float MotionBlurIntensity = 0.18f;

        static Volume volume;
        static MotionBlur motionBlur;

        /// <summary>
        /// Flou de mouvement d'URP sur le volume global, caméra et objets : le décor file, la voiture suivie par la caméra
        /// reste nette (passe MotionVectors du shader Toon).
        /// </summary>
        static void ApplyMotionBlur(int quality)
        {
            if (!motionBlur || !volume)
            {
                volume = Object.FindAnyObjectByType<Volume>();
                if (!volume) return;
                var profile = volume.profile; // instance propre à la partie (partagée avec DayNightCycle et ScreenFx)
                if (!profile.TryGet(out motionBlur)) motionBlur = profile.Add<MotionBlur>(true);
            }
            bool on = GameSettings.MotionBlur;
            motionBlur.active = on;
            motionBlur.mode.Override(MotionBlurMode.CameraAndObjects);
            motionBlur.quality.Override(quality <= Low ? MotionBlurQuality.Low : quality >= Ultra ? MotionBlurQuality.High : MotionBlurQuality.Medium);
            motionBlur.intensity.Override(on ? MotionBlurIntensity : 0f);
            motionBlur.clamp.Override(0.05f);
        }

        // ------------------------------------------------------------------ écran (ordinateur)

        /// <summary>Synchro verticale, mode d'affichage et définition proposés (ni téléphone, ni tablette, ni navigateur).</summary>
        public static bool HasDisplayOptions => !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer;

        /// <summary>Modes d'affichage proposés, dans l'ordre du curseur (GameSettings.WindowMode).</summary>
        public static readonly FullScreenMode[] WindowModes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
        static readonly (string fr, string en)[] WindowNames = { ("Fenêtré", "Windowed"), ("Plein écran", "Borderless"), ("Plein écran exclusif", "Exclusive fullscreen") };

        public static string WindowModeName(int index) { var n = WindowNames[Mathf.Clamp(index, 0, WindowModes.Length - 1)]; return Loc.T(n.fr, n.en); }

        /// <summary>Définition de l'écran (celle du bureau, pas celle de la fenêtre du jeu).</summary>
        public static Vector2Int NativeResolution
        {
            get
            {
                var d = Display.main;
                int w = d != null ? d.systemWidth : 0, h = d != null ? d.systemHeight : 0;
                if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
                return new Vector2Int(w, h);
            }
        }

        static Vector2Int[] resolutions;

        /// <summary>Définitions proposées, de la plus petite à celle de l'écran (une seule fois chaque taille, toutes fréquences confondues).</summary>
        public static Vector2Int[] Resolutions
        {
            get
            {
                if (resolutions != null) return resolutions;
                var native = NativeResolution;
                var list = new System.Collections.Generic.List<Vector2Int>();
                foreach (var r in Screen.resolutions)
                {
                    var v = new Vector2Int(r.width, r.height);
                    if (v.x < 800 || v.y < 600 || v.x > native.x || v.y > native.y || list.Contains(v)) continue;
                    list.Add(v);
                }
                if (!list.Contains(native)) list.Add(native);
                list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                return resolutions = list.ToArray();
            }
        }

        /// <summary>Définition choisie (0 × 0 = celle de l'écran).</summary>
        public static Vector2Int ChosenResolution =>
            GameSettings.ScreenWidth > 0 && GameSettings.ScreenHeight > 0 ? new Vector2Int(GameSettings.ScreenWidth, GameSettings.ScreenHeight) : NativeResolution;

        /// <summary>Rang de la définition choisie dans <see cref="Resolutions"/> (la plus proche si elle n'y est plus).</summary>
        public static int ResolutionIndex
        {
            get
            {
                var list = Resolutions;
                var want = ChosenResolution;
                int best = list.Length - 1;
                long bestGap = long.MaxValue;
                for (int i = 0; i < list.Length; i++)
                {
                    long gap = System.Math.Abs((long)list[i].x * list[i].y - (long)want.x * want.y) + System.Math.Abs(list[i].x - want.x);
                    if (gap < bestGap) { bestGap = gap; best = i; }
                }
                return best;
            }
        }

        public static void SetResolutionIndex(int index)
        {
            var list = Resolutions;
            var r = list[Mathf.Clamp(index, 0, list.Length - 1)];
            if (r == NativeResolution) GameSettings.ScreenWidth = GameSettings.ScreenHeight = 0;
            else { GameSettings.ScreenWidth = r.x; GameSettings.ScreenHeight = r.y; }
        }

        public static string ResolutionName(int index)
        {
            var list = Resolutions;
            var r = list[Mathf.Clamp(index, 0, list.Length - 1)];
            string size = $"{r.x} × {r.y}";
            return r == NativeResolution ? Loc.T(size + " (écran)", size + " (native)") : size;
        }

        /// <summary>
        /// Applique le mode d'affichage et la définition choisis (au lancement, puis depuis le menu Options, une fois le
        /// curseur lâché). Rien dans l'éditeur : la vue Game garde sa taille.
        /// </summary>
        public static void ApplyDisplay()
        {
            if (!HasDisplayOptions || Application.isEditor) return;
            var mode = WindowModes[Mathf.Clamp(GameSettings.WindowMode, 0, WindowModes.Length - 1)];
            var size = ChosenResolution;
            if (Screen.fullScreenMode == mode && Screen.width == size.x && Screen.height == size.y) return;
            Screen.SetResolution(size.x, size.y, mode);
        }

        /// <summary>Intervalle de mise à jour du reflet du ciel (s), lu par le cycle jour/nuit.</summary>
        public static float ReflectionInterval { get; private set; } = 0.5f;

        static UniversalRenderPipelineAsset runtimeAsset;
        static int applied = -1;

        /// <summary>
        /// Applique les réglages de GameSettings (qualité, synchro verticale, limite d'images, flou de mouvement ; mode
        /// d'affichage et définition au premier appel, ensuite par <see cref="ApplyDisplay"/>). gm peut être nul (rendu seul).
        /// </summary>
        public static void Apply(GameManager gm)
        {
            int level = Mathf.Clamp(GameSettings.Quality, 0, Presets.Length - 1);
            var p = Presets[level];

            // Synchro verticale (ordinateur) : les images suivent l'écran, la limite ne s'applique plus. Sinon synchro
            // coupée pour que la limite d'images s'applique (le mobile garde 60 au plus).
            bool vsync = GameSettings.VSync && HasDisplayOptions;
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            int cap = FpsCaps[Mathf.Clamp(GameSettings.FpsCap, 0, FpsCaps.Length - 1)];
            if (Application.isMobilePlatform && (cap == 0 || cap > 60)) cap = 60;
            Application.targetFrameRate = vsync || cap == 0 ? -1 : cap;
            if (applied < 0) ApplyDisplay();
            ApplyMotionBlur(level);

            var asset = RuntimeAsset();
            if (asset)
            {
                asset.renderScale = p.renderScale;
                asset.shadowDistance = p.shadowDistance;
                asset.shadowCascadeCount = p.cascades;
                asset.mainLightShadowmapResolution = p.shadowRes;
                asset.msaaSampleCount = p.msaa;
                foreach (var data in asset.rendererDataList)
                {
                    if (!data) continue;
                    foreach (var f in data.rendererFeatures)
                        if (f && f.GetType().Name == "ScreenSpaceAmbientOcclusion") f.SetActive(p.ssao);
                }
            }
            var dp = Details[Mathf.Clamp(GameSettings.DetailDistance, 0, DetailLevels - 1)];
            QualitySettings.lodBias = dp.lodBias;
            OutlineFeature.Enabled = p.outline;
            Shader.SetGlobalFloat("_ChillRoadToonDetail", p.toonDetail ? 1f : 0f);
            ReflectionInterval = p.reflectionInterval;

            if (gm && gm.Terrain)
            {
                var veg = gm.Terrain.Vegetation;
                if (veg)
                {
                    veg.lod1Distance = dp.vegLod1;
                    veg.lod2Distance = dp.vegLod2;
                    veg.detailDistance = dp.vegDetail;
                }
                var flora = gm.Terrain.Ground;
                if (flora)
                {
                    flora.farDistance = p.floraFar;
                    flora.midDistance = p.floraMid;
                    flora.denseFar = p.denseFar;
                    flora.denseCarpet = p.denseCarpet && p.denseFar > 0f;
                    if (!Mathf.Approximately(flora.density, p.floraDensity))
                    {
                        flora.density = p.floraDensity;
                        if (applied >= 0) flora.Rebuild();
                    }
                }
            }
            applied = level;
        }

        /// <summary>Copie modifiable de l'asset URP du niveau de qualité courant, installée comme pipeline actif.</summary>
        static UniversalRenderPipelineAsset RuntimeAsset()
        {
            if (runtimeAsset) return runtimeAsset;
            var src = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                      ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (!src) return null;
            runtimeAsset = Object.Instantiate(src);
            runtimeAsset.name = src.name + " (runtime)";
            QualitySettings.renderPipeline = runtimeAsset;
            return runtimeAsset;
        }
    }
}
