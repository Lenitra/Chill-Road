using UnityEditor;
using UnityEngine;

namespace ChillRoad.EditorTools
{
    /// <summary>
    /// Matériaux de route sur le shader ChillRoad/Road, dans la direction artistique des voitures (aplats, formes nettes) :
    /// enrobé (autoroute, nationale), chemin de terre, accotements, marquages. Une seule texture de bruit, générée par
    /// Tools/RoadTextures/road_textures.py ; les couleurs sont celles du shader.
    /// Menu : ChillRoad > Matériaux de route (également appelé par ChillRoad > Setup).
    /// </summary>
    public static class RoadMaterials
    {
        const string NoisePath = "Assets/ChillRoad/Textures/Road/Road_Noise.png";
        const string MatFolder = "Assets/ChillRoad/Generated";
        const string AssetsPath = "Assets/ChillRoad/Generated/GameAssets.asset";
        static readonly string[] Keywords = { "_SURFACE_ASPHALT", "_SURFACE_DIRT", "_SURFACE_VERGE", "_SURFACE_MARKING" };

        [MenuItem("Chill Road/Matériaux de route")]
        public static void Menu()
        {
            var a = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (!a) { Debug.LogError("GameAssets introuvable : lancez d'abord ChillRoad > Setup"); return; }
            if (!Apply(a)) { Debug.LogError("Shader ChillRoad/Road ou " + NoisePath + " introuvable"); return; }
            AssetDatabase.SaveAssets();
            Debug.Log("Matériaux de route : enrobé, chemin, accotements et marquages sur le shader ChillRoad/Road");
        }

        /// <summary>Crée ou remet à zéro les matériaux de route et les branche sur GameAssets ; false si le shader ou le bruit manquent.</summary>
        public static bool Apply(GameAssets a)
        {
            var shader = Shader.Find("ChillRoad/Road");
            if (!shader || !(AssetImporter.GetAtPath(NoisePath) is TextureImporter ti)) return false;
            // Bruits : données linéaires, répétées, filtrées finement (chaussée vue en incidence rasante)
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            a.asphalt = Make("Asphalt", shader, noise, 0);
            a.dirtRoad = Make("DirtRoad", shader, noise, 1);
            a.verge = Make("Verge", shader, noise, 2);
            a.roadMarking = Make("Marking", shader, noise, 3);
            EditorUtility.SetDirty(a);
            return true;
        }

        static Material Make(string name, Shader shader, Texture2D noise, int surface)
        {
            string path = MatFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            // Repart des valeurs du shader : aucune couleur ni mot-clé hérités d'une version précédente
            m.shader = shader;
            var fresh = new Material(shader);
            m.CopyPropertiesFromMaterial(fresh);
            Object.DestroyImmediate(fresh);
            foreach (var k in m.shaderKeywords) m.DisableKeyword(k);
            m.SetFloat("_Surface", surface);
            m.EnableKeyword(Keywords[surface]);
            m.SetTexture("_Noise", noise);
            m.SetVector("_Lanes", new Vector4(RoadNetwork.LaneWidth, RoadNetwork.InnerEdge, RoadNetwork.HighwayOuterEdge, RoadNetwork.CountryEdge));
            m.SetFloat("_DirtLane", RoadNetwork.DirtLaneOffset);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
