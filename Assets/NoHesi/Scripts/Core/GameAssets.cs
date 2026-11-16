using System;
using UnityEngine;

namespace NoHesi
{
    public enum Drivetrain { RWD, FWD, AWD }

    /// <summary>Voix moteur synthétisée (Auto = choisie d'après l'identifiant de la voiture).</summary>
    public enum EngineVoice { Auto, V8, Inline6Turbo, Inline4, Electric, TwoStroke, V6, FlatPlaneV8Turbo, V12 }

    /// <summary>Caractéristiques d'une voiture jouable (unités réelles : m, kg, N·m, km/h).</summary>
    [Serializable]
    public class VehicleSpec
    {
        public string id;
        public string displayName;
        public GameObject prefab;
        [Tooltip("Échelle appliquée au modèle (les modèles Kenney sont petits)")] public float modelScale = 1.72f;
        public Drivetrain drivetrain = Drivetrain.RWD;
        public float mass = 1400f;
        [Tooltip("Couple max moteur (N·m)")] public float maxTorque = 380f;
        public float redline = 7000f;
        public float[] gearRatios = { 3.4f, 2.25f, 1.65f, 1.3f, 1.05f, 0.86f };
        public float finalDrive = 3.6f;
        public float wheelRadius = 0.34f;
        [Tooltip("Coefficient d'adhérence des pneus")] public float tireGrip = 1.15f;
        [Tooltip("Surface frontale × Cx (traînée)")] public float dragArea = 0.72f;
        [Tooltip("Surface × Cz (appui)")] public float downforceArea = 0.35f;
        [Tooltip("Angle de braquage max à l'arrêt (degrés)")] public float steerLock = 34f;
        public float brakeForce = 16000f;
        [Tooltip("Position de la caméra capot (repère voiture, m). Zéro = position par défaut")] public Vector3 hoodCamera;
        [Tooltip("Position de la caméra pare-chocs (repère voiture, m). Zéro = position par défaut")] public Vector3 bumperCamera;
        [Tooltip("Son moteur (Auto : V8 pour la Mustang, 4 cylindres pour la MX-5, électrique pour la Vision 74, 6 cylindres turbo sinon)")]
        public EngineVoice engineVoice = EngineVoice.Auto;

        /// <summary>Voix moteur effective (résout Auto d'après l'identifiant).</summary>
        /// <summary>Vitesse de pointe (m/s) : rupteur sur le dernier rapport.</summary>
        public float TopSpeed => gearRatios == null || gearRatios.Length == 0 ? 60f
            : redline / 60f * 2f * Mathf.PI * wheelRadius / (gearRatios[gearRatios.Length - 1] * finalDrive);

        public EngineVoice Voice
        {
            get
            {
                if (engineVoice != EngineVoice.Auto) return engineVoice;
                string key = ((id ?? "") + " " + (displayName ?? "")).ToLowerInvariant();
                if (key.Contains("dmc") || key.Contains("delorean")) return EngineVoice.V6;
                if (key.Contains("jesko") || key.Contains("koenigsegg")) return EngineVoice.FlatPlaneV8Turbo;
                if (key.Contains("valkyrie") || key.Contains("aston")) return EngineVoice.V12;
                if (key.Contains("mustang") || key.Contains("v8")) return EngineVoice.V8;
                if (key.Contains("miata") || key.Contains("mx-5") || key.Contains("mx5")) return EngineVoice.Inline4;
                if (key.Contains("vision")) return EngineVoice.Electric;
                if (key.Contains("p50") || key.Contains("peel")) return EngineVoice.TwoStroke;
                return EngineVoice.Inline6Turbo;
            }
        }
    }

    /// <summary>Références vers les assets utilisés au runtime (générée par NoHesi > Setup).</summary>
    [CreateAssetMenu(menuName = "NoHesi/Game Assets")]
    public class GameAssets : ScriptableObject
    {
        [Header("Voitures")]
        public VehicleSpec[] playerCars;
        public GameObject[] trafficCars;
        [Tooltip("Poids lourds (bus, camions, semi-remorques) : voie de droite, vitesse réduite")] public GameObject[] trafficTrucks;
        [Tooltip("Deux-roues (motos, scooters) : apparition occasionnelle parmi les voitures")] public GameObject[] trafficBikes;
        [Tooltip("Part des deux-roues parmi les voitures du trafic (0-1)")] [Range(0f, 0.3f)] public float bikeShare = 0.06f;
        [Tooltip("Engins agricoles (tracteurs) : routes de campagne et chemins seulement, voie de droite, 25-40 km/h")] public GameObject[] trafficFarm;
        [Tooltip("Part des engins agricoles dans le trafic des routes de campagne (0-1)")] [Range(0f, 0.3f)] public float farmShare = 0.07f;
        public GameObject policeCar;
        [Tooltip("Échelle des voitures du trafic (1 = modèles à l'échelle réelle, 1.72 = Kenney)")] public float trafficCarScale = 1.72f;
        [Tooltip("Échelle des camions du trafic")] public float trafficTruckScale = 2f;
        [Tooltip("Échelle de la voiture de police")] public float policeCarScale = 1.72f;

        [Header("Décor")]
        public GameObject[] buildings;
        public GameObject streetLight;
        [Tooltip("Lampadaire à une seule crosse (carrefours)")] public GameObject streetLightSingle;
        public GameObject stopSign;
        public GameObject warningSign;
        [Header("Travaux")]
        public GameObject cone;
        public GameObject constructionBarrier;
        public GameObject constructionLight;

        [Header("Nature (pack d'environnement)")]
        public GameObject[] pines;
        public GameObject[] deadTrees;
        public GameObject[] fruitTrees;
        public GameObject[] shrubs;
        public GameObject[] grass;
        public GameObject[] flowers;
        public GameObject[] smallRocks;
        public GameObject[] bigRocks;
        public GameObject[] logsAndStumps;
        public GameObject[] mushrooms;
        public GameObject fence;

        [Header("Carte")]
        [Tooltip("Pack alpin : bâtiments, décors, nature (NoHesi > Importer le pack alpin)")] public AlpPack alp;

        [Header("Matériaux")]
        public Material asphalt;
        public Material roadMarking;
        public Material terrain;
        public Material dirt;
        [Tooltip("Revêtement des chemins de terre")] public Material dirtRoad;
        [Tooltip("Accotements en gravier et talus enherbés le long des routes")] public Material verge;
        public Material water;
        public Material concrete;
        public Material metal;
        public Material signBlue;
        public Material signRed;
        public Material emissiveWhite;
        public Material emissiveRed;
        public Material emissiveBlue;
        public Material particle;
        [Tooltip("Étincelles, flammes, traînées (additif)")] public Material spark;
        [Tooltip("Traces de pneus")] public Material skidMark;

        [Header("Shaders")]
        [Tooltip("Portes des courses : lumière au sol, faisceaux, fumée (NoHesi/GateFx)")] public Shader gateFx;
        [Tooltip("Fantôme du record d'une course : coque translucide (NoHesi/Ghost)")] public Shader ghost;
    }
}
