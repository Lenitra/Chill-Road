using UnityEditor;
using UnityEngine;

namespace NoHesi.EditorTools
{
    /// <summary>
    /// Matériaux du monde sans asset générique, dans la direction artistique des voitures : sol du terrain (NoHesi/Terrain :
    /// facettes low poly et aplats), eau (NoHesi/Water), ciel (NoHesi/SkyDayNight : nuages en aplats), béton et terre unis.
    /// Tous découpent leurs formes dans la texture de bruit de la route (Tools/RoadTextures/road_textures.py).
    /// Menu : NoHesi > Matériaux du monde (également appelé par NoHesi > Setup).
    /// </summary>
    public static class WorldMaterials
    {
        const string NoisePath = "Assets/NoHesi/Textures/Road/Road_Noise.png";
        const string Gen = "Assets/NoHesi/Generated";
        const string AssetsPath = Gen + "/GameAssets.asset";

        [MenuItem("NoHesi/Matériaux du monde")]
        public static void Menu()
        {
            var a = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (!a) { Debug.LogError("GameAssets introuvable : lancez d'abord NoHesi > Setup"); return; }
            if (!Apply(a) || !Sky()) { Debug.LogError("Shaders NoHesi (Terrain, Water, SkyDayNight) ou " + NoisePath + " introuvables"); return; }
            AssetDatabase.SaveAssets();
            Debug.Log("Matériaux du monde : terrain, eau, ciel, béton et terre sans asset générique");
        }

        /// <summary>Terrain, eau, béton et terre ; false si un shader ou la texture de bruit manque.</summary>
        public static bool Apply(GameAssets a)
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            var terrain = Shader.Find("NoHesi/Terrain");
            var water = Shader.Find("NoHesi/Water");
            if (!noise || !terrain || !water) return false;
            a.terrain = Make("TerrainBlend", terrain, noise, "_Noise");
            a.water = Make("Water", water, noise, "_Noise");
            a.concrete = Flat("Concrete", new Color(0.74f, 0.74f, 0.72f), 0.1f, true);
            a.dirt = Flat("Dirt", new Color(0.57f, 0.49f, 0.36f), 0.05f, false);
            EditorUtility.SetDirty(a);
            return true;
        }

        /// <summary>Ciel procédural (même asset que l'ancien ciel : la scène et le cycle jour/nuit le gardent).</summary>
        public static Material Sky()
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            var shader = Shader.Find("NoHesi/SkyDayNight");
            if (!noise || !shader || shader.FindPropertyIndex("_CloudNoise") < 0) return null;
            var m = Make("SkyDayNight", shader, noise, "_CloudNoise");
            m.enableInstancing = false;
            return m;
        }

        /// <summary>Matériau sur un shader NoHesi, remis aux valeurs du shader (aucune propriété héritée d'une version précédente).</summary>
        static Material Make(string name, Shader shader, Texture2D noise, string noiseProperty)
        {
            string path = Gen + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            var fresh = new Material(shader);
            m.CopyPropertiesFromMaterial(fresh);
            Object.DestroyImmediate(fresh);
            foreach (var k in m.shaderKeywords) m.DisableKeyword(k);
            m.SetTexture(noiseProperty, noise);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Lit uni (sans texture) : béton des glissières et des bordures, terre de repli.</summary>
        static Material Flat(string name, Color color, float smoothness, bool twoSided)
        {
            string path = Gen + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
