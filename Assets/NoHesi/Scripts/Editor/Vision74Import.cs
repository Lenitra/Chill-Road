using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoHesi.EditorTools
{
    /// <summary>
    /// Voiture « Vision 74 » low poly modélisée dans Blender (Assets/NoHesi/Models/Vision74) :
    /// facettes + dégradés verticaux façon Polytope, via une petite texture palette (Vision74_Palette.png)
    /// et un seul matériau URP (plus deux matériaux émissifs pour les feux). Menu : NoHesi > Importer Vision 74.
    /// </summary>
    public static class Vision74Import
    {
        public const string Folder = "Assets/NoHesi/Models/Vision74";
        public const string ModelPath = Folder + "/Vision74.fbx";
        const string PalettePath = Folder + "/Vision74_Palette.png";
        const string MatFolder = Folder + "/Materials";
        const string AssetsPath = "Assets/NoHesi/Generated/GameAssets.asset";

        [MenuItem("NoHesi/Importer Vision 74")]
        public static void Import()
        {
            var model = Prepare();
            if (!model) return;
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (!assets) { Debug.LogError("GameAssets introuvable : lancez d'abord NoHesi > Setup"); return; }
            int index = AddTo(assets, model);
            AssetDatabase.SaveAssets();
            // Sélectionnée pour la prochaine partie
            PlayerPrefs.SetInt("nohesi_car", index);
            PlayerPrefs.Save();
            Debug.Log($"Vision 74 ajoutée au garage (voiture {index + 1}/{assets.playerCars.Length}) et sélectionnée");
        }

        /// <summary>Règle l'import du FBX et de sa palette, branche les matériaux URP ; renvoie le modèle.</summary>
        public static GameObject Prepare()
        {
            if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter mi))
            {
                Debug.LogWarning("Modèle introuvable : " + ModelPath);
                return null;
            }
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            var mats = CreateMaterials();
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
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        /// <summary>Ajoute (ou remplace) la Vision 74 dans la liste des voitures jouables ; renvoie son index.</summary>
        public static int AddTo(GameAssets a, GameObject model)
        {
            const float mass = 1650f;
            var spec = new VehicleSpec
            {
                id = "vision74", displayName = "Vision 74", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 720f, redline = 8000f,
                gearRatios = new[] { 3.3f, 2.2f, 1.62f, 1.28f, 1.04f, 0.86f }, finalDrive = 3.45f,
                wheelRadius = 0.355f, tireGrip = 1.5f, dragArea = 0.66f, downforceArea = 0.95f, steerLock = 34f,
                brakeForce = mass * 11.5f,
                hoodCamera = new Vector3(0f, 1.10f, 0.9f),
                bumperCamera = new Vector3(0f, 0.48f, 2.6f),
            };
            var list = new List<VehicleSpec>(a.playerCars ?? new VehicleSpec[0]);
            list.RemoveAll(s => s != null && s.id == spec.id);
            list.Add(spec);
            a.playerCars = list.ToArray();
            EditorUtility.SetDirty(a);
            return list.Count - 1;
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
            var body = Get("V74L_Palette");
            body.SetTexture("_BaseMap", palette);
            body.SetColor("_BaseColor", Color.white);
            body.SetFloat("_Metallic", 0f);
            body.SetFloat("_Smoothness", 0.3f);        // mat comme les assets Polytope
            EditorUtility.SetDirty(body);

            var d = new Dictionary<string, Material>
            {
                ["V74L_Palette"] = body,
                ["V74L_LED"] = Emissive("V74L_LED", new Color(1f, 0.85f, 0.6f), 3f),
                ["V74L_Tail"] = Emissive("V74L_Tail", new Color(1f, 0.05f, 0.03f), 4f),
            };
            // Anciens matériaux de la version haute définition : plus utilisés
            foreach (var guid in AssetDatabase.FindAssets("NV_ t:Material", new[] { MatFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
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

        static Material Emissive(string name, Color linear, float intensity)
        {
            var m = Get(name);
            m.SetColor("_BaseColor", linear.gamma);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", linear * intensity);
            // Émission temps réel : avec None, la validation d'URP retire _EMISSION (optiques éteintes)
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
