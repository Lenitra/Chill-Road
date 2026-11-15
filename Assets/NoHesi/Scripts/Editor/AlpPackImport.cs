using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NoHesi.EditorTools
{
    /// <summary>
    /// Pack alpin modélisé dans Blender (Tools/Blender/alp_*.py → Assets/NoHesi/Models/AlpPack) : chalets, maisons de
    /// ville, monuments, station de ski, mobilier, signalisation, infrastructures, nature et animaux.
    /// Un seul matériau (AP_Palette) : palette de dégradés + carte d'émission (fenêtres éclairées, lampes) dont
    /// l'intensité suit la nuit. Le catalogue (AlpPack.asset) reprend le manifeste exporté par Blender.
    /// Menu : NoHesi > Importer le pack alpin.
    /// </summary>
    public static class AlpPackImport
    {
        public const string Folder = "Assets/NoHesi/Models/AlpPack";
        const string PalettePath = Folder + "/AlpPack_Palette.png";
        const string EmissionPath = Folder + "/AlpPack_Emission.png";
        // Végétation au sol « Flora » (Tools/Blender/alp_flora.py) : palette et matériau à part, shader NoHesi/Foliage
        const string FloraPalettePath = Folder + "/AlpFlora_Palette.png";
        const string NoisePath = "Assets/NoHesi/Textures/Road/Road_Noise.png";
        const string ManifestPath = Folder + "/AlpPack_unity.json";
        const string MatFolder = Folder + "/Materials";
        const string PackPath = "Assets/NoHesi/Generated/AlpPack.asset";
        const string AssetsPath = "Assets/NoHesi/Generated/GameAssets.asset";

        [Serializable]
        class ItemJson
        {
            public string id, file, category, collider, material;
            public string[] tags;
            public float sx, sy, sz, fw, fd, radius, module, ymin;
            public float[] lights;
        }

        [Serializable] class ManifestJson { public ItemJson[] items; }

        [MenuItem("NoHesi/Importer le pack alpin")]
        public static void Import()
        {
            if (!File.Exists(ManifestPath)) { Debug.LogError("Manifeste introuvable : " + ManifestPath + " (lancer Tools/Map/map_manifest.py)"); return; }
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            var mat = PaletteMaterial();
            var flora = FloraMaterial();
            var manifest = JsonUtility.FromJson<ManifestJson>(File.ReadAllText(ManifestPath));
            var items = new List<AlpPack.Item>();
            int missing = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var j in manifest.items) Configure(Folder + "/" + j.file + ".fbx", mat, j.material == "AP_Flora" ? flora : null);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            foreach (var j in manifest.items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + j.file + ".fbx");
                if (!prefab) { missing++; continue; }
                var it = new AlpPack.Item
                {
                    id = j.id, category = j.category, tags = j.tags, prefab = prefab, size = new Vector3(j.sx, j.sy, j.sz),
                    footprint = new Vector2(j.fw, j.fd), collider = j.collider, radius = j.radius, module = j.module, ymin = j.ymin,
                };
                int n = j.lights != null ? j.lights.Length / 3 : 0;
                it.lights = new Vector3[n];
                for (int k = 0; k < n; k++) it.lights[k] = new Vector3(j.lights[k * 3], j.lights[k * 3 + 1], j.lights[k * 3 + 2]);
                items.Add(it);
            }
            var pack = AssetDatabase.LoadAssetAtPath<AlpPack>(PackPath);
            if (!pack)
            {
                pack = ScriptableObject.CreateInstance<AlpPack>();
                AssetDatabase.CreateAsset(pack, PackPath);
            }
            pack.items = items.ToArray();
            pack.material = mat;
            EditorUtility.SetDirty(pack);
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (assets)
            {
                assets.alp = pack;
                EditorUtility.SetDirty(assets);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Pack alpin : {items.Count} modèles importés" + (missing > 0 ? $", {missing} introuvables" : ""));
        }

        static void Configure(string path, Material mat, Material flora)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return;
            bool dirty = mi.importCameras || mi.importLights || mi.importAnimation || mi.isReadable == false
                         || mi.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;      // facettes exportées par Blender
            mi.isReadable = true;                                // collisions en maillage et fusion possibles
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            // Deux matériaux possibles dans les FBX : AP_Palette (tout le pack) et AP_Flora (végétation au sol)
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "AP_Palette");
            var idFlora = new AssetImporter.SourceAssetIdentifier(typeof(Material), "AP_Flora");
            var map = mi.GetExternalObjectMap();
            if (!map.TryGetValue(id, out var current) || current != mat
                || (flora && (!map.TryGetValue(idFlora, out var currentFlora) || currentFlora != flora)))
            {
                foreach (var old in map) mi.RemoveRemap(old.Key);
                mi.AddRemap(id, mat);
                if (flora) mi.AddRemap(idFlora, flora);
                dirty = true;
            }
            if (dirty) mi.SaveAndReimport();
        }

        static void Texture(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }

        /// <summary>
        /// Matériau de la végétation au sol : shader NoHesi/Foliage (normales douces, translucidité, vent, fondu),
        /// palette Flora (dégradé de la base à la pointe de chaque brin), bruits partagés pour les rafales et les teintes.
        /// </summary>
        public static Material FloraMaterial()
        {
            var shader = Shader.Find("NoHesi/Foliage");
            if (!shader) { Debug.LogError("Shader NoHesi/Foliage introuvable"); return null; }
            if (File.Exists(FloraPalettePath)) Texture(FloraPalettePath);
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            string path = MatFolder + "/AP_Flora.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FloraPalettePath));
            m.SetTexture("_Noise", AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath));
            m.SetFloat("_FadeEnd", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material PaletteMaterial()
        {
            Texture(PalettePath);
            Texture(EmissionPath);
            string path = MatFolder + "/AP_Palette.mat";
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
            // Émission : fenêtres éclairées et lampes ; la couleur (noire le jour) est pilotée par le cycle jour/nuit
            m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath));
            m.SetColor("_EmissionColor", Color.black);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
