using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ChillRoad.EditorTools
{
    /// <summary>
    /// Pack d'environnement modélisé dans Blender (Tools/Blender/env_lib.py, env_nature.py, env_props.py →
    /// Assets/ChillRoad/Models/EnvPack) : conifères, fruitiers, arbres morts, buissons, herbe, coquelicots, rochers, bûches,
    /// champignons, clôture, lampadaires, panneaux, matériel de chantier et bâtiments de ferme.
    /// Même direction artistique que les voitures : low poly à facettes, une palette de dégradés partagée
    /// (EnvPack_Palette.png), trois niveaux de détail pour la nature (objets _LOD0..2 → LODGroup à l'import).
    /// Remplace les modèles Polytope et Kenney du décor. Menu : ChillRoad > Importer pack environnement (également appelé par Setup).
    /// </summary>
    public static class EnvPackImport
    {
        public const string Folder = "Assets/ChillRoad/Models/EnvPack";
        const string PalettePath = Folder + "/EnvPack_Palette.png";
        const string MatFolder = Folder + "/Materials";
        const string AssetsPath = "Assets/ChillRoad/Generated/GameAssets.asset";

        [MenuItem("Chill Road/Importer pack environnement")]
        public static void Import()
        {
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (!assets) { Debug.LogError("GameAssets introuvable : lancez d'abord ChillRoad > Setup"); return; }
            if (!Apply(assets)) { Debug.LogError("Pack d'environnement introuvable : " + Folder); return; }
            AssetDatabase.SaveAssets();
            Debug.Log($"Pack environnement : {assets.pines.Length} conifères, {assets.fruitTrees.Length} fruitiers, " +
                      $"{assets.shrubs.Length} buissons, rochers, herbe, fleurs, clôture, bâtiments");
        }

        /// <summary>Prépare les modèles et les branche sur GameAssets ; false si le pack est absent.</summary>
        public static bool Apply(GameAssets a)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return false;
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            var mat = PaletteMaterial();
            var pines = All(mat, "Pine_A", "Pine_B", "Pine_C");
            if (pines.Length == 0) return false;
            a.pines = pines;
            a.fruitTrees = All(mat, "FruitTree", "AppleTree", "PearTree", "PlumTree");
            a.shrubs = All(mat, "Shrub_A", "Shrub_B", "DeadShrub");
            a.grass = All(mat, "GrassTuft");
            a.flowers = All(mat, "Poppies");
            a.smallRocks = All(mat, "Rock", "RockPile");
            a.bigRocks = All(mat, "Menhir");
            // Clôture fusionnée le long des nationales : son maillage doit être lisible
            var fence = Prepare("Fence", mat, true);
            if (fence) a.fence = fence;
            // Bâtiments : exportés à la taille des modèles Kenney remplacés
            var buildings = All(mat, "Longere", "Maison", "Grange", "Maisonnette", "Hangar");
            if (buildings.Length > 0) a.buildings = buildings;
            EditorUtility.SetDirty(a);
            return true;
        }

        static GameObject[] All(Material mat, params string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var g = Prepare(n, mat, false);
                if (g) list.Add(g);
            }
            return list.ToArray();
        }

        static GameObject Prepare(string name, Material mat, bool readable)
        {
            string path = Folder + "/" + name + ".fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi))
            {
                Debug.LogWarning("Modèle introuvable : " + path);
                return null;
            }
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;      // facettes exportées par Blender
            mi.isReadable = readable;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var old in mi.GetExternalObjectMap()) mi.RemoveRemap(old.Key);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "EV_Palette"), mat);
            mi.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Material PaletteMaterial()
        {
            if (AssetImporter.GetAtPath(PalettePath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Bilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            string path = MatFolder + "/EV_Palette.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.12f);
            m.enableInstancing = true;                          // la végétation est rendue en GPU instancing
            EditorUtility.SetDirty(m);
            ToonStyle.Use(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
