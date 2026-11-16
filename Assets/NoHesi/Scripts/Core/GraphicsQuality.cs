using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using NoHesi.Rendering;

namespace NoHesi
{
    /// <summary>
    /// Qualité graphique (menu Options) : 0 basse, 1 moyenne, 2 haute, 3 ultra. Règle le rendu URP (échelle de rendu,
    /// ombres, anticrénelage, occlusion ambiante), les contours cartoon, les distances de détail de la végétation et
    /// de la flore au sol, et la limite d'images par seconde. Le rendu URP est réglé sur une copie de l'asset du
    /// projet (créée au premier appel) : l'asset d'origine n'est jamais modifié, même dans l'éditeur.
    /// </summary>
    public static class GraphicsQuality
    {
        public const int Low = 0, Medium = 1, High = 2, Ultra = 3;

        struct Preset
        {
            public float renderScale, shadowDistance, lodBias;
            public int cascades, shadowRes, msaa;
            public bool ssao, outline, toonDetail, extraShadows;
            public float vegLod1, vegLod2, floraDensity, floraFar, floraMid, denseFar;
            public bool denseCarpet;
            public float reflectionInterval;
        }

        static readonly Preset[] Presets =
        {
            // Basse : 80 % de la définition, une cascade d'ombre courte, ni SSAO ni contours, flore clairsemée
            new Preset { renderScale = 0.8f, shadowDistance = 60f, lodBias = 0.8f, cascades = 1, shadowRes = 1024, msaa = 1,
                         ssao = false, outline = false, toonDetail = false, extraShadows = false,
                         vegLod1 = 25f, vegLod2 = 110f, floraDensity = 0.45f, floraFar = 120f, floraMid = 60f, denseFar = 0f,
                         denseCarpet = false, reflectionInterval = 2f },
            // Moyenne
            new Preset { renderScale = 0.9f, shadowDistance = 100f, lodBias = 1.2f, cascades = 2, shadowRes = 2048, msaa = 1,
                         ssao = false, outline = true, toonDetail = true, extraShadows = false,
                         vegLod1 = 35f, vegLod2 = 140f, floraDensity = 0.7f, floraFar = 170f, floraMid = 85f, denseFar = 40f,
                         denseCarpet = true, reflectionInterval = 1f },
            // Haute (réglages d'origine du jeu)
            new Preset { renderScale = 1f, shadowDistance = 140f, lodBias = 2f, cascades = 4, shadowRes = 2048, msaa = 1,
                         ssao = true, outline = true, toonDetail = true, extraShadows = true,
                         vegLod1 = 45f, vegLod2 = 170f, floraDensity = 1f, floraFar = 230f, floraMid = 110f, denseFar = 62f,
                         denseCarpet = true, reflectionInterval = 0.5f },
            // Ultra : ombres plus loin et plus fines, MSAA 4x, détails plus loin
            new Preset { renderScale = 1f, shadowDistance = 220f, lodBias = 2.6f, cascades = 4, shadowRes = 4096, msaa = 4,
                         ssao = true, outline = true, toonDetail = true, extraShadows = true,
                         vegLod1 = 65f, vegLod2 = 230f, floraDensity = 1f, floraFar = 280f, floraMid = 140f, denseFar = 75f,
                         denseCarpet = true, reflectionInterval = 0.5f },
        };

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

        /// <summary>Intervalle de mise à jour du reflet du ciel (s), lu par le cycle jour/nuit.</summary>
        public static float ReflectionInterval { get; private set; } = 0.5f;

        static UniversalRenderPipelineAsset runtimeAsset;
        static int applied = -1;

        /// <summary>Applique les réglages de GameSettings (qualité, limite d'images). gm peut être nul (rendu seul).</summary>
        public static void Apply(GameManager gm)
        {
            int level = Mathf.Clamp(GameSettings.Quality, 0, Presets.Length - 1);
            var p = Presets[level];

            // Limite d'images (le mobile garde 60 au plus) ; synchro verticale coupée pour que la limite s'applique
            QualitySettings.vSyncCount = 0;
            int cap = FpsCaps[Mathf.Clamp(GameSettings.FpsCap, 0, FpsCaps.Length - 1)];
            if (Application.isMobilePlatform && (cap == 0 || cap > 60)) cap = 60;
            Application.targetFrameRate = cap == 0 ? -1 : cap;

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
            QualitySettings.lodBias = p.lodBias;
            OutlineFeature.Enabled = p.outline;
            Shader.SetGlobalFloat("_NoHesiToonDetail", p.toonDetail ? 1f : 0f);
            ReflectionInterval = p.reflectionInterval;

            if (gm && gm.Terrain)
            {
                var veg = gm.Terrain.Vegetation;
                if (veg)
                {
                    veg.lod1Distance = p.vegLod1;
                    veg.lod2Distance = p.vegLod2;
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
