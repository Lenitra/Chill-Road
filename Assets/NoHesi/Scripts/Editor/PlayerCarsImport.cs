using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoHesi.EditorTools
{
    /// <summary>
    /// Voitures jouables réelles modélisées dans Blender avec le moteur du pack de voitures
    /// (Tools/Blender/play_cars.py → Assets/NoHesi/Models/PlayerCars) : Toyota Supra (A80, 1993), Mazda MX-5 (NA,
    /// 1989), Ford Mustang fastback 1969 (celle de John Wick), Peel P50 (tricycle, roue « wheel-back-center »),
    /// Toyota AE86 Trueno (Initial D, phares escamotables), DeLorean DMC-12, Koenigsegg Jesko Attack, Aston Martin
    /// Valkyrie AMR-LMH et Renault Twingo (1re génération, 1993, traction).
    /// Un modèle absent est simplement ignoré.
    /// Les phares escamotables de la MX-5 sont des enfants « popup-left/right » dont le pivot est la charnière :
    /// CarLights les fait basculer. Palette et optiques partagées avec le pack de voitures, peinture propre à chaque
    /// voiture. Menu : NoHesi > Importer voitures jouables (également appelé par NoHesi > Setup, après le pack).
    /// </summary>
    public static class PlayerCarsImport
    {
        public const string Folder = "Assets/NoHesi/Models/PlayerCars";
        const string MatFolder = Folder + "/Materials";
        const string PackMats = CarPackImport.Folder + "/Materials";
        const string AssetsPath = "Assets/NoHesi/Generated/GameAssets.asset";

        [MenuItem("NoHesi/Importer voitures jouables")]
        public static void Import()
        {
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(AssetsPath);
            if (!assets) { Debug.LogError("GameAssets introuvable : lancez d'abord NoHesi > Setup"); return; }
            int n = Apply(assets);
            AssetDatabase.SaveAssets();
            Debug.Log($"Voitures jouables : {n} voiture(s) réelle(s) importée(s), {assets.playerCars.Length} au garage");
        }

        /// <summary>Prépare les modèles et ajoute (ou remplace) leurs fiches dans le garage ; renvoie le nombre importé.</summary>
        public static int Apply(GameAssets a)
        {
            var palette = AssetDatabase.LoadAssetAtPath<Material>(PackMats + "/PK_Palette.mat");
            var led = AssetDatabase.LoadAssetAtPath<Material>(PackMats + "/PK_LED.mat");
            var tail = AssetDatabase.LoadAssetAtPath<Material>(PackMats + "/PK_Tail.mat");
            if (!palette || !led || !tail)
            {
                Debug.LogError("Matériaux du pack de voitures absents (" + PackMats + ") : lancez NoHesi > Importer pack voitures");
                return 0;
            }
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            var tex = palette.GetTexture("_BaseMap");
            var cars = new (string file, Color paint, System.Func<GameObject, VehicleSpec> spec)[]
            {
                ("Supra", new Color(0.95f, 0.45f, 0.07f), Supra),     // orange vif
                ("Miata", new Color(0.78f, 0.06f, 0.07f), Miata),     // rouge « Classic Red »
                ("Mustang", new Color(0.33f, 0.34f, 0.35f), Mustang), // gris anthracite, bandes noires (John Wick)
                ("PeelP50", new Color(0.12f, 0.27f, 0.62f), PeelP50), // « Dark Blue » d'origine
                ("AE86", new Color(0.93f, 0.93f, 0.91f), AE86),       // blanc « panda » (bas de caisse noir), Initial D
                ("DeLorean", new Color(0.70f, 0.71f, 0.72f), DeLorean), // inox brossé
                ("Jesko", new Color(0.94f, 0.94f, 0.95f), Jesko),     // blanc, carbone et accents vert citron (Attack)
                ("Valkyrie", new Color(0.05f, 0.56f, 0.53f), Valkyrie), // teal de l'AMR-LMH, accents vert citron
                ("Twingo", new Color(0.97f, 0.77f, 0.10f), Twingo),   // « Jaune Indien » du lancement, boucliers gris
            };
            // Accents hors palette (matériau « PK_Accent » du modèle) : couleur unie par voiture
            var accents = new Dictionary<string, Color>
            {
                ["Jesko"] = new Color(0.70f, 0.92f, 0.12f),           // vert citron de la Jesko de présentation
                ["Valkyrie"] = new Color(0.72f, 0.93f, 0.10f),        // vert citron de la livrée AMR-LMH
            };
            var list = new List<VehicleSpec>(a.playerCars ?? new VehicleSpec[0]);
            int n = 0;
            foreach (var (file, paint, make) in cars)
            {
                var mats = new Dictionary<string, Material>
                {
                    ["PK_Palette"] = palette,
                    ["PK_LED"] = led,
                    ["PK_Tail"] = tail,
                    ["PK_Paint"] = Paint("PC_" + file + "_Paint", tex, paint),
                };
                if (accents.TryGetValue(file, out var accent)) mats["PK_Accent"] = Paint("PC_" + file + "_Accent", null, accent);
                var model = Prepare(file, mats);
                if (!model) continue;
                var spec = make(model);
                list.RemoveAll(s => s != null && s.id == spec.id);
                list.Add(spec);
                n++;
            }
            a.playerCars = list.ToArray();
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            return n;
        }

        // ------------------------------------------------------------------ fiches (valeurs d'origine)

        /// <summary>Supra RZ biturbo (2JZ-GTE) : 1 570 kg, 431 N·m, boîte Getrag 6 rapports, pneus de 17".</summary>
        static VehicleSpec Supra(GameObject model)
        {
            const float mass = 1570f;
            return new VehicleSpec
            {
                id = "supra", displayName = "Toyota Supra", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 431f, redline = 6800f,
                gearRatios = new[] { 3.827f, 2.36f, 1.685f, 1.312f, 1f, 0.793f }, finalDrive = 3.13f,
                wheelRadius = 0.32f, tireGrip = 1.3f, dragArea = 0.62f, downforceArea = 0.3f, steerLock = 34f,
                brakeForce = mass * 10.5f,
                hoodCamera = new Vector3(0f, 1.10f, 0.98f),
                bumperCamera = new Vector3(0f, 0.45f, 2.40f),
            };
        }

        /// <summary>MX-5 1.8 (BP) : 990 kg, 155 N·m, boîte 5 rapports, pneus de 14", capote repliée.</summary>
        static VehicleSpec Miata(GameObject model)
        {
            const float mass = 990f;
            return new VehicleSpec
            {
                id = "miata", displayName = "Mazda MX-5", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 155f, redline = 7000f,
                gearRatios = new[] { 3.136f, 1.888f, 1.33f, 1f, 0.814f }, finalDrive = 4.1f,
                wheelRadius = 0.289f, tireGrip = 1.2f, dragArea = 0.66f, downforceArea = 0.15f, steerLock = 36f,
                brakeForce = mass * 10f,
                hoodCamera = new Vector3(0f, 1.00f, 0.62f),
                bumperCamera = new Vector3(0f, 0.42f, 1.98f),
            };
        }

        /// <summary>Mustang fastback 1969 « Boss 429 » de John Wick : 1 600 kg, 610 N·m, boîte Toploader 4 rapports,
        /// pneus de 15" d'époque (peu d'adhérence, freins faibles).</summary>
        static VehicleSpec Mustang(GameObject model)
        {
            const float mass = 1600f;
            return new VehicleSpec
            {
                id = "mustang", displayName = "Ford Mustang 1969", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 610f, redline = 6000f,
                gearRatios = new[] { 2.32f, 1.69f, 1.29f, 1f }, finalDrive = 3.5f,
                wheelRadius = 0.34f, tireGrip = 1.1f, dragArea = 0.84f, downforceArea = 0.1f, steerLock = 32f,
                brakeForce = mass * 8.5f,
                hoodCamera = new Vector3(0f, 1.12f, 0.85f),
                bumperCamera = new Vector3(0f, 0.50f, 2.42f),
            };
        }

        /// <summary>Peel P50 (1962-1965), la plus petite voiture de série : tricycle (2 roues avant, 1 arrière motrice),
        /// 59 kg + le pilote, monocylindre 2-temps 49 cm³ de 4,2 ch, 3 rapports, 61 km/h en pointe.</summary>
        static VehicleSpec PeelP50(GameObject model)
        {
            const float mass = 135f;
            return new VehicleSpec
            {
                id = "p50", displayName = "Peel P50", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 5f, redline = 6500f,
                gearRatios = new[] { 3f, 1.7f, 1f }, finalDrive = 6.4f,
                wheelRadius = 0.16f, tireGrip = 0.95f, dragArea = 0.5f, downforceArea = 0f, steerLock = 38f,
                brakeForce = mass * 7f,
                hoodCamera = new Vector3(0f, 0.98f, 0.40f),
                bumperCamera = new Vector3(0f, 0.30f, 0.72f),
            };
        }

        /// <summary>Toyota AE86 Sprinter Trueno GT-APEX (1983-1987), celle d'Initial D : 940 kg, 4A-GE 1.6 16 soupapes
        /// (149 N·m, 7 600 tr/min), boîte T50 5 rapports, pont 4,3 : propulsion légère et joueuse.</summary>
        static VehicleSpec AE86(GameObject model)
        {
            const float mass = 940f;
            return new VehicleSpec
            {
                id = "ae86", displayName = "Toyota AE86 Trueno", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 149f, redline = 7600f,
                gearRatios = new[] { 3.587f, 2.022f, 1.384f, 1f, 0.861f }, finalDrive = 4.3f,
                wheelRadius = 0.289f, tireGrip = 1.1f, dragArea = 0.62f, downforceArea = 0.1f, steerLock = 36f,
                brakeForce = mass * 10f,
                hoodCamera = new Vector3(0f, 1.10f, 0.80f),
                bumperCamera = new Vector3(0f, 0.42f, 2.10f),
                engineVoice = EngineVoice.Inline4,
            };
        }

        /// <summary>DeLorean DMC-12 (1981-1983) avec le kit Retour vers le futur (évents temporels, Mr. Fusion) :
        /// 1 233 kg, V6 PRV 2.85 (207 N·m) en porte-à-faux arrière, boîte 5 rapports, pont 3,44, pneus plus larges
        /// à l'arrière.</summary>
        static VehicleSpec DeLorean(GameObject model)
        {
            const float mass = 1233f;
            return new VehicleSpec
            {
                id = "dmc12", displayName = "DeLorean (Retour vers le futur)", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 207f, redline = 6000f,
                gearRatios = new[] { 3.36f, 2.06f, 1.38f, 1.03f, 0.82f }, finalDrive = 3.44f,
                wheelRadius = 0.332f, tireGrip = 1.1f, dragArea = 0.62f, downforceArea = 0.1f, steerLock = 34f,
                brakeForce = mass * 9.5f,
                hoodCamera = new Vector3(0f, 0.98f, 0.70f),
                bumperCamera = new Vector3(0f, 0.40f, 2.08f),
            };
        }

        /// <summary>Koenigsegg Jesko Attack : 1 420 kg, V8 5.0 biturbo (1 500 N·m, 8 500 tr/min), boîte LST 9 rapports,
        /// pneus Cup 2 R (20" avant, 21" arrière), grand aileron : énorme appui et beaucoup de traînée.</summary>
        static VehicleSpec Jesko(GameObject model)
        {
            const float mass = 1420f;
            return new VehicleSpec
            {
                id = "jesko", displayName = "Koenigsegg Jesko Attack", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 1500f, redline = 8500f,
                gearRatios = new[] { 2.9f, 2.2f, 1.72f, 1.39f, 1.15f, 0.98f, 0.85f, 0.75f, 0.67f }, finalDrive = 4.3f,
                wheelRadius = 0.364f, tireGrip = 1.5f, dragArea = 0.9f, downforceArea = 2.6f, steerLock = 32f,
                brakeForce = mass * 14f,
                hoodCamera = new Vector3(0f, 1.00f, 0.95f),
                bumperCamera = new Vector3(0f, 0.36f, 2.40f),
            };
        }

        /// <summary>Aston Martin Valkyrie AMR-LMH (hypercar d'endurance) : ≈1 110 kg avec pilote, V12 Cosworth 6.5
        /// atmosphérique (≈620 N·m, 10 500 tr/min), boîte 7 rapports, appui énorme.</summary>
        static VehicleSpec Valkyrie(GameObject model)
        {
            const float mass = 1110f;
            return new VehicleSpec
            {
                id = "valkyrie", displayName = "Aston Martin Valkyrie", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.RWD, mass = mass, maxTorque = 620f, redline = 10500f,
                gearRatios = new[] { 3.0f, 2.3f, 1.85f, 1.52f, 1.28f, 1.1f, 0.96f }, finalDrive = 4.2f,
                wheelRadius = 0.355f, tireGrip = 1.6f, dragArea = 0.55f, downforceArea = 3.5f, steerLock = 28f,
                brakeForce = mass * 15f,
                hoodCamera = new Vector3(0f, 0.92f, 1.05f),
                bumperCamera = new Vector3(0f, 0.30f, 2.55f),
            };
        }

        /// <summary>Renault Twingo de première génération (phase 1, 1993) : ≈860 kg avec le conducteur, traction, 4 cylindres
        /// C3G 1.2 de 55 ch (90 N·m), boîte 5 rapports, pneus de 13" (145/70) très étroits, freins arrière à tambour.</summary>
        static VehicleSpec Twingo(GameObject model)
        {
            const float mass = 860f;
            return new VehicleSpec
            {
                id = "twingo", displayName = "Renault Twingo", prefab = model, modelScale = 1f,
                drivetrain = Drivetrain.FWD, mass = mass, maxTorque = 90f, redline = 6000f,
                gearRatios = new[] { 3.73f, 2.05f, 1.32f, 0.97f, 0.79f }, finalDrive = 4.21f,
                wheelRadius = 0.267f, tireGrip = 1.0f, dragArea = 0.69f, downforceArea = 0f, steerLock = 38f,
                brakeForce = mass * 9f,
                hoodCamera = new Vector3(0f, 1.18f, 1.15f),
                bumperCamera = new Vector3(0f, 0.42f, 1.78f),
                engineVoice = EngineVoice.Inline4,
            };
        }

        // ------------------------------------------------------------------ modèles et matériaux

        static GameObject Prepare(string file, Dictionary<string, Material> mats)
        {
            string path = Folder + "/" + file + ".fbx";
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

        /// <summary>Peinture : palette du pack (dégradé vertical) teintée à la couleur de la voiture, un peu plus brillante.</summary>
        static Material Paint(string name, Texture palette, Color color)
        {
            string path = MatFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", palette);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.55f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return ToonStyle.Use(m);
        }
    }
}
