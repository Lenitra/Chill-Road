using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ChillRoad.EditorTools
{
    /// <summary>
    /// Pack de véhicules réels low poly modélisés dans Blender (Assets/ChillRoad/Models/CarPack) : remplace les
    /// voitures et camions Kenney du trafic, ajoute utilitaires, poids lourds (bus, autocar,
    /// porteur, benne à ordures, semi-remorques, bétonnière, dépanneuse, pompiers), deux-roues et tracteurs agricoles. Une palette de dégradés partagée (CarPack_Palette.png),
    /// la peinture sur un matériau à part (PK_Paint, teintée au hasard par TrafficSystem), des optiques émissives.
    /// Menu : ChillRoad > Importer pack voitures (également appelé par ChillRoad > Setup).
    /// </summary>
    public static class CarPackImport
    {
        public const string Folder = "Assets/ChillRoad/Models/CarPack";
        const string PalettePath = Folder + "/CarPack_Palette.png";
        const string MatFolder = Folder + "/Materials";
        const string AssetsPath = "Assets/ChillRoad/Generated/GameAssets.asset";
        static readonly string[] Traffic = { "Corolla", "Taxi", "Civic", "Golf", "RAV4", "Model3", "Transit", "F150", "Fiat500",
                                             "Kangoo", "BoxVan", "Ambulance" };
        static readonly string[] Trucks = { "Bus", "Coach", "BoxTruck", "GarbageTruck", "SemiTrailer", "Tanker",
                                            "CementMixer", "TowTruck", "FireTruck" };
        static readonly string[] Bikes = { "Motorcycle", "Scooter" };
        static readonly string[] Farm = { "Tractor" };

        [MenuItem("Chill Road/Importer pack voitures")]
        public static void Import()
        {
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (!assets) { Debug.LogError("GameAssets introuvable : lancez d'abord ChillRoad > Setup"); return; }
            if (!Apply(assets)) return;
            AssetDatabase.SaveAssets();
            Debug.Log($"Pack véhicules : {assets.trafficCars.Length} voitures et utilitaires, {assets.trafficTrucks.Length} poids lourds, " +
                      $"{(assets.trafficBikes != null ? assets.trafficBikes.Length : 0)} deux-roues, " +
                      $"{(assets.trafficFarm != null ? assets.trafficFarm.Length : 0)} engin(s) agricole(s) + police");
        }

        static List<GameObject> PrepareAll(IEnumerable<string> names, Dictionary<string, Material> mats)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var g = Prepare(n, mats);
                if (g) list.Add(g);
            }
            return list;
        }

        /// <summary>Prépare les modèles et les branche sur le trafic et la police ; false si le pack est absent.</summary>
        public static bool Apply(GameAssets a)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return false;
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            var mats = CreateMaterials();
            var traffic = PrepareAll(Traffic, mats);
            if (traffic.Count == 0) return false;
            a.trafficCars = traffic.ToArray();
            a.trafficCarScale = 1f;                      // modèles à l'échelle réelle
            var trucks = PrepareAll(Trucks, mats);
            if (trucks.Count > 0)
            {
                a.trafficTrucks = trucks.ToArray();      // remplacent les camions Kenney
                a.trafficTruckScale = 1f;
            }
            a.trafficBikes = PrepareAll(Bikes, mats).ToArray();
            a.trafficFarm = PrepareAll(Farm, mats).ToArray();       // routes de campagne seulement
            EditorUtility.SetDirty(a);
            return true;
        }

        static GameObject Prepare(string name, Dictionary<string, Material> mats)
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
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var old in mi.GetExternalObjectMap()) mi.RemoveRemap(old.Key);
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // ------------------------------------------------------------------ matériaux

        static Dictionary<string, Material> CreateMaterials()
        {
            // Palette : une colonne par teinte, dégradé vertical du bas (sombre) vers le haut (clair)
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
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            var d = new Dictionary<string, Material>
            {
                ["PK_Palette"] = PaletteMaterial("PK_Palette", palette, 0.3f),
                ["PK_Paint"] = PaletteMaterial("PK_Paint", palette, 0.45f),   // teinte appliquée par voiture
                ["PK_LED"] = Emissive("PK_LED", new Color(1f, 0.92f, 0.8f), 2.5f),
                ["PK_Tail"] = Emissive("PK_Tail", new Color(1f, 0.04f, 0.02f), 3f),
                ["PK_SirenRed"] = Emissive("PK_SirenRed", new Color(1f, 0.03f, 0.02f), 6f),
                ["PK_SirenBlue"] = Emissive("PK_SirenBlue", new Color(0.05f, 0.22f, 1f), 6f),
            };
            AssetDatabase.SaveAssets();
            return d;
        }

        static Material Get(string name)
        {
            string path = MatFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static Material PaletteMaterial(string name, Texture2D palette, float smoothness)
        {
            var m = Get(name);
            m.SetTexture("_BaseMap", palette);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return ToonStyle.Use(m);
        }

        static Material Emissive(string name, Color linear, float intensity)
        {
            var m = Get(name);
            m.SetColor("_BaseColor", linear.gamma);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", linear * intensity);
            // Émission temps réel : avec None, la validation d'URP retire _EMISSION (optiques éteintes, cubes de secours)
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return name.StartsWith("PK_Siren") ? m : ToonStyle.Use(m);
        }
    }
}
