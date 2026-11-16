using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Réglages du joueur (menu Options), enregistrés dans les PlayerPrefs.
    /// Audio et partie : appliqués tout de suite. Génération de route : appliqués à la prochaine route.
    /// </summary>
    public static class GameSettings
    {
        /// <summary>Langue de l'interface (anglais par défaut, changée depuis l'écran d'accueil).</summary>
        public static Language Language = Language.English;

        // Audio
        public static float MusicVolume = 0.6f;
        public static float EffectsVolume = 0.8f;

        // Génération de route (multiplicateurs, 1 = réglage d'origine)
        public static float HighwayShare = 1f;   // part d'autoroute (0 = aucune)
        public static float DirtShare = 1f;      // part de chemins de terre (0 = aucun)
        public static float Curviness = 1f;      // amplitude des virages
        public static float Hills = 1f;          // relief (collines, pentes)
        public static float Junctions = 1f;      // carrefours, ronds-points, ponts, travaux (0 = aucun)
        public static int Seed;                  // 0 = nouvelle route aléatoire à chaque partie

        // Partie
        public static float TrafficDensity = 1f;
        public static float EventFrequency = 1f;
        public static int RelayKm = 2;           // distance entre deux relais de voiture (0 = désactivés)
        public static float RainChance = WeatherSystem.StormShare; // part du temps sous l'orage (0 = jamais, 1 = toujours)

        // Commandes tactiles (téléphone, tablette) : 0 = désactivées, 1 = glisser pour diriger, 2 = boutons ‹ ›, 3 = inclinaison
        public static int TouchSteer = 1;

        // Graphismes : qualité 0 basse, 1 moyenne, 2 haute, 3 ultra ; limite d'images (index de GraphicsQuality.FpsCaps)
        public static int Quality = GraphicsQuality.High;
        public static int FpsCap = 1;
        // Distance des détails (index de GraphicsQuality.DetailLevels) : jusqu'où arbres, maisons et objets gardent leur
        // modèle complet ; remise au niveau conseillé quand la qualité change
        public static int DetailDistance = GraphicsQuality.DefaultDetail(GraphicsQuality.High);

        const string Prefix = "nohesi_opt_";
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Language = PlayerPrefs.GetInt(Prefix + "lang", 0) == 1 ? Language.French : Language.English;
            MusicVolume = PlayerPrefs.GetFloat(Prefix + "music", MusicVolume);
            EffectsVolume = PlayerPrefs.GetFloat(Prefix + "sfx", EffectsVolume);
            HighwayShare = PlayerPrefs.GetFloat(Prefix + "highway", HighwayShare);
            DirtShare = PlayerPrefs.GetFloat(Prefix + "dirt", DirtShare);
            Curviness = PlayerPrefs.GetFloat(Prefix + "curves", Curviness);
            Hills = PlayerPrefs.GetFloat(Prefix + "hills", Hills);
            Junctions = PlayerPrefs.GetFloat(Prefix + "junctions", Junctions);
            Seed = PlayerPrefs.GetInt(Prefix + "seed", Seed);
            TrafficDensity = PlayerPrefs.GetFloat(Prefix + "traffic", TrafficDensity);
            EventFrequency = PlayerPrefs.GetFloat(Prefix + "events", EventFrequency);
            RelayKm = PlayerPrefs.GetInt(Prefix + "relay", RelayKm);
            RainChance = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "rain", RainChance));
            TouchSteer = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "touch", TouchSteer), 0, 3);
            Quality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "quality", Application.isMobilePlatform ? GraphicsQuality.Low : Quality), 0, 3);
            FpsCap = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "fpscap", FpsCap), 0, GraphicsQuality.FpsCaps.Length - 1);
            DetailDistance = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "detail", GraphicsQuality.DefaultDetail(Quality)), 0, GraphicsQuality.DetailLevels - 1);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(Prefix + "lang", Language == Language.French ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "sfx", EffectsVolume);
            PlayerPrefs.SetFloat(Prefix + "highway", HighwayShare);
            PlayerPrefs.SetFloat(Prefix + "dirt", DirtShare);
            PlayerPrefs.SetFloat(Prefix + "curves", Curviness);
            PlayerPrefs.SetFloat(Prefix + "hills", Hills);
            PlayerPrefs.SetFloat(Prefix + "junctions", Junctions);
            PlayerPrefs.SetInt(Prefix + "seed", Seed);
            PlayerPrefs.SetFloat(Prefix + "traffic", TrafficDensity);
            PlayerPrefs.SetFloat(Prefix + "events", EventFrequency);
            PlayerPrefs.SetInt(Prefix + "relay", RelayKm);
            PlayerPrefs.SetFloat(Prefix + "rain", RainChance);
            PlayerPrefs.SetInt(Prefix + "touch", TouchSteer);
            PlayerPrefs.SetInt(Prefix + "quality", Quality);
            PlayerPrefs.SetInt(Prefix + "fpscap", FpsCap);
            PlayerPrefs.SetInt(Prefix + "detail", DetailDistance);
            PlayerPrefs.Save();
        }

        /// <summary>Réglages de route d'origine (le menu propose de revenir à la route « classique »).</summary>
        public static void ResetRoad()
        {
            HighwayShare = DirtShare = Curviness = Hills = Junctions = 1f;
            Seed = 0;
        }

        public static void ResetAll()
        {
            ResetRoad();
            MusicVolume = 0.6f;
            EffectsVolume = 0.8f;
            TrafficDensity = EventFrequency = 1f;
            RelayKm = 2;
            RainChance = WeatherSystem.StormShare;
            TouchSteer = 1;
            Quality = Application.isMobilePlatform ? GraphicsQuality.Low : GraphicsQuality.High;
            FpsCap = 1;
            DetailDistance = GraphicsQuality.DefaultDetail(Quality);
        }

        /// <summary>Signature des réglages de route : sert à signaler qu'une nouvelle route est nécessaire.</summary>
        public static string RoadSignature => $"{HighwayShare:F2}/{DirtShare:F2}/{Curviness:F2}/{Hills:F2}/{Junctions:F2}/{Seed}";
    }
}
