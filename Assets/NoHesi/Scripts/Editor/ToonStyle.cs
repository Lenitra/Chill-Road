using UnityEditor;
using UnityEngine;

namespace NoHesi.EditorTools
{
    /// <summary>
    /// Style cartoon (2026-11-15) : les matériaux des modèles du jeu (voitures, packs de décor) passent sur le shader
    /// NoHesi/Toon, qui reprend les noms de propriétés d'URP/Lit. Appelé par chaque importeur après le réglage du
    /// matériau ; l'émission (mot-clé _EMISSION, émission temps réel) est conservée.
    /// </summary>
    public static class ToonStyle
    {
        const string ShaderName = "NoHesi/Toon";

        public static Material Use(Material m)
        {
            if (!m) return m;
            var shader = Shader.Find(ShaderName);
            if (!shader) { Debug.LogWarning("Shader " + ShaderName + " introuvable"); return m; }
            bool emissive = m.IsKeywordEnabled("_EMISSION");
            var gi = m.globalIlluminationFlags;
            if (m.shader != shader) m.shader = shader;
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetFloat("_EmissionToggle", 1f);
                m.globalIlluminationFlags = gi == MaterialGlobalIlluminationFlags.None ? MaterialGlobalIlluminationFlags.RealtimeEmissive : gi;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Traits de détail dessinés (planches, pierres, tuiles, écorce) d'après une carte de motifs par
        /// colonne de palette (Tools/Blender/alp_detail_map.py).</summary>
        public static void UseDetail(Material m, string mapPath)
        {
            if (!m) return;
            if (AssetImporter.GetAtPath(mapPath) is TextureImporter ti)
            {
                bool dirty = ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.sRGBTexture
                             || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.npotScale != TextureImporterNPOTScale.None;
                if (dirty)
                {
                    ti.filterMode = FilterMode.Point;
                    ti.mipmapEnabled = false;
                    ti.sRGBTexture = false;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
            }
            var map = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
            if (!map) { Debug.LogWarning("Carte de motifs introuvable : " + mapPath); return; }
            m.SetTexture("_DetailMap", map);
            m.SetFloat("_DetailLines", 1f);
            m.SetFloat("_SnowCap", 1f);          // décors : neige sur les toits et les branches dans les zones enneigées
            m.EnableKeyword("_DETAIL_LINES");
            EditorUtility.SetDirty(m);
        }

        public const string AlpDetailMap = "Assets/NoHesi/Models/AlpPack/AlpPack_Detail.png";

        [MenuItem("NoHesi/Style cartoon (matériaux)")]
        public static void ApplyAll()
        {
            int n = 0;
            foreach (var folder in new[] { "Assets/NoHesi/Models/CarPack/Materials", "Assets/NoHesi/Models/PlayerCars/Materials",
                                           "Assets/NoHesi/Models/Vision74/Materials", "Assets/NoHesi/Models/EnvPack/Materials" })
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (m && m.name.StartsWith("PK_Siren")) continue;      // gyrophares : URP/Lit (pas d'ombre propre)
                    Use(m); n++;
                }
            var ap = AssetDatabase.LoadAssetAtPath<Material>("Assets/NoHesi/Models/AlpPack/Materials/AP_Palette.mat");
            Use(ap); UseDetail(ap, AlpDetailMap); n++;
            AssetDatabase.SaveAssets();
            Debug.Log("Style cartoon : " + n + " matériaux sur " + ShaderName);
        }
    }
}
