using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Améliorations achetées au garage de tuning (<see cref="TuningGarage"/>), voiture par voiture : six pièces à
    /// trois niveaux, sauvegardées sur la machine (PlayerPrefs). Chaque niveau s'achète seul (acheter le niveau 3 ne
    /// donne pas les niveaux 1 et 2) ; un niveau acheté, comme la pièce de série, se remonte ensuite à volonté et
    /// gratuitement. Le niveau monté corrige un peu la fiche de la voiture
    /// (<see cref="VehicleSpec"/>) : une voiture améliorée reste elle-même, une Ami au maximum ne rattrape pas une Supra.
    /// Le prix d'un niveau est une part de la valeur de la voiture (<see cref="Wallet.Price"/>, avec un plancher) ;
    /// la voiture offerte au départ (Ami) s'améliore gratuitement.
    /// </summary>
    public static class CarTuning
    {
        public enum Part { Engine, Gearbox, Tires, Brakes, Weight, Aero }

        public const int PartCount = 6;
        public const int MaxLevel = 3;
        /// <summary>Niveau monté de chaque pièce (un chiffre par pièce).</summary>
        const string Key = "chillroad_tune_";
        /// <summary>Niveaux achetés de chaque pièce (un chiffre par pièce : bit 0 = niveau 1, bit 1 = niveau 2…).</summary>
        const string OwnedKey = "chillroad_tuneown_";

        /// <summary>Prix de chaque niveau, en part de la valeur de la voiture.</summary>
        static readonly float[] CostShare = { 0.04f, 0.08f, 0.14f };
        /// <summary>Valeur prise pour les voitures données ou très bon marché (Ami, Twingo).</summary>
        const int MinValue = 4000;

        static readonly Dictionary<string, int[]> levels = new Dictionary<string, int[]>();
        static readonly Dictionary<string, int[]> owned = new Dictionary<string, int[]>();
        static readonly Dictionary<VehicleSpec, (string key, VehicleSpec tuned)> cache = new Dictionary<VehicleSpec, (string, VehicleSpec)>();

        public static string Name(Part p)
        {
            switch (p)
            {
                case Part.Engine: return Loc.T("Moteur", "Engine");
                case Part.Gearbox: return Loc.T("Boîte de vitesses", "Gearbox");
                case Part.Tires: return Loc.T("Pneus", "Tires");
                case Part.Brakes: return Loc.T("Freins", "Brakes");
                case Part.Weight: return Loc.T("Allègement", "Weight reduction");
                default: return Loc.T("Aérodynamique", "Aero");
            }
        }

        public static string Effect(Part p)
        {
            switch (p)
            {
                case Part.Engine: return Loc.T("+8 % de couple par niveau", "+8% torque per level");
                case Part.Gearbox: return Loc.T("Derniers rapports allongés : vitesse de pointe", "Longer top gears: higher top speed");
                case Part.Tires: return Loc.T("+4 % d'adhérence par niveau", "+4% grip per level");
                case Part.Brakes: return Loc.T("+12 % de freinage par niveau", "+12% braking per level");
                case Part.Weight: return Loc.T("-4 % de poids par niveau", "-4% weight per level");
                default: return Loc.T("Plus d'appui, moins de traînée", "More downforce, less drag");
            }
        }

        static int[] Levels(VehicleSpec spec)
        {
            string id = spec != null ? spec.id ?? "" : "";
            if (levels.TryGetValue(id, out var l)) return l;
            l = new int[PartCount];
            string saved = PlayerPrefs.GetString(Key + id, "");
            for (int i = 0; i < PartCount && i < saved.Length; i++)
                l[i] = Mathf.Clamp(saved[i] - '0', 0, MaxLevel);
            levels[id] = l;
            return l;
        }

        /// <summary>Niveaux achetés, en masque par pièce. Sauvegarde d'avant : les niveaux jusqu'au niveau monté ont été payés.</summary>
        static int[] Owned(VehicleSpec spec)
        {
            string id = spec != null ? spec.id ?? "" : "";
            if (owned.TryGetValue(id, out var o)) return o;
            var l = Levels(spec);
            o = new int[PartCount];
            string saved = PlayerPrefs.GetString(OwnedKey + id, "");
            for (int i = 0; i < PartCount; i++)
            {
                if (saved.Length == 0) { o[i] = (1 << l[i]) - 1; continue; }
                int bits = i < saved.Length ? Mathf.Clamp(saved[i] - '0', 0, (1 << MaxLevel) - 1) : 0;
                // Le niveau monté est forcément acheté
                o[i] = l[i] > 0 ? bits | 1 << (l[i] - 1) : bits;
            }
            owned[id] = o;
            return o;
        }

        /// <summary>Niveau monté sur la voiture (0 : pièce de série).</summary>
        public static int Level(VehicleSpec spec, Part p) => Levels(spec)[(int)p];

        /// <summary>Vrai si ce niveau est à disposition (série, ou déjà acheté) : le monter est gratuit.</summary>
        public static bool Owns(VehicleSpec spec, Part p, int level)
        {
            if (level <= 0) return true;
            if (level > MaxLevel) return false;
            return (Owned(spec)[(int)p] & (1 << (level - 1))) != 0;
        }

        /// <summary>Niveaux montés, toutes pièces confondues (0 à 18).</summary>
        public static int TotalLevels(VehicleSpec spec)
        {
            int n = 0;
            foreach (int l in Levels(spec)) n += l;
            return n;
        }

        /// <summary>Prix pour monter le niveau suivant d'une pièce (0 : gratuit ou déjà acheté, -1 : pièce au maximum).</summary>
        public static int NextPrice(VehicleSpec spec, Part p)
        {
            int l = Level(spec, p);
            return l >= MaxLevel ? -1 : Price(spec, p, l + 1);
        }

        /// <summary>Prix d'un niveau (1 à <see cref="MaxLevel"/>), le même pour toutes les pièces de la voiture.</summary>
        public static int LevelPrice(VehicleSpec spec, int level)
        {
            // Voiture offerte (Ami) : ses améliorations le sont aussi
            if (Wallet.Price(spec) <= 0) return 0;
            float value = Mathf.Max(Wallet.Price(spec), MinValue);
            // Arrondi à la dizaine (100 au-delà de 10 000)
            float raw = value * CostShare[Mathf.Clamp(level, 1, MaxLevel) - 1];
            float step = raw >= 10000f ? 100f : 10f;
            return Mathf.Max(10, Mathf.RoundToInt(raw / step) * (int)step);
        }

        /// <summary>Prix pour monter un niveau d'une pièce : 0 s'il est déjà acheté (ou de série), son seul prix sinon.</summary>
        public static int Price(VehicleSpec spec, Part p, int level) => Owns(spec, p, level) ? 0 : LevelPrice(spec, level);

        /// <summary>
        /// Monte un niveau d'une pièce (0 : remettre la pièce de série). Gratuit s'il est déjà acheté, sinon payé
        /// seul s'il y a l'argent (les niveaux inférieurs restent à acheter). Vrai si la pièce a changé.
        /// </summary>
        public static bool Fit(VehicleSpec spec, Part p, int level)
        {
            if (spec == null || level < 0 || level > MaxLevel || Level(spec, p) == level) return false;
            if (!Owns(spec, p, level))
            {
                if (!Wallet.Spend(LevelPrice(spec, level))) return false;
                Owned(spec)[(int)p] |= 1 << (level - 1);
            }
            Levels(spec)[(int)p] = level;
            Save(spec);
            return true;
        }

        static void Save(VehicleSpec spec)
        {
            string id = spec.id ?? "";
            var l = Levels(spec);
            var o = Owned(spec);
            var chars = new char[PartCount];
            for (int i = 0; i < PartCount; i++) chars[i] = (char)('0' + l[i]);
            PlayerPrefs.SetString(Key + id, new string(chars));
            for (int i = 0; i < PartCount; i++) chars[i] = (char)('0' + o[i]);
            PlayerPrefs.SetString(OwnedKey + id, new string(chars));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Fiche de la voiture avec ses améliorations (la fiche d'origine si elle n'en a aucune). La copie est gardée
        /// tant que les niveaux ne changent pas : la même voiture garde la même fiche.
        /// </summary>
        public static VehicleSpec Apply(VehicleSpec spec)
        {
            if (spec == null) return null;
            var l = Levels(spec);
            if (TotalLevels(spec) == 0) return spec;
            string key = string.Concat(l[0], l[1], l[2], l[3], l[4], l[5]);
            if (cache.TryGetValue(spec, out var c) && c.key == key) return c.tuned;
            var tuned = With(spec, l);
            cache[spec] = (key, tuned);
            return tuned;
        }

        /// <summary>Fiche avec une pièce à un autre niveau (aperçu au garage). Copie neuve à chaque appel.</summary>
        public static VehicleSpec Preview(VehicleSpec spec, Part p, int level)
        {
            var l = (int[])Levels(spec).Clone();
            l[(int)p] = Mathf.Clamp(level, 0, MaxLevel);
            return With(spec, l);
        }

        /// <summary>Fiche avec toutes les pièces au maximum (échelle des barres du garage).</summary>
        public static VehicleSpec Maxed(VehicleSpec spec)
        {
            var l = new int[PartCount];
            for (int i = 0; i < PartCount; i++) l[i] = MaxLevel;
            return With(spec, l);
        }

        static VehicleSpec With(VehicleSpec b, int[] l)
        {
            var s = Copy(b);
            s.maxTorque = b.maxTorque * (1f + 0.08f * l[(int)Part.Engine]);
            // Boîte : seuls les deux derniers rapports s'allongent, les départs gardent leur vivacité
            int g = l[(int)Part.Gearbox], n = s.gearRatios != null ? s.gearRatios.Length : 0;
            if (n > 0) s.gearRatios[n - 1] = b.gearRatios[n - 1] * (1f - 0.04f * g);
            if (n > 2) s.gearRatios[n - 2] = b.gearRatios[n - 2] * (1f - 0.02f * g);
            s.tireGrip = b.tireGrip * (1f + 0.04f * l[(int)Part.Tires]);
            s.brakeForce = b.brakeForce * (1f + 0.12f * l[(int)Part.Brakes]);
            s.mass = b.mass * (1f - 0.04f * l[(int)Part.Weight]);
            int a = l[(int)Part.Aero];
            s.downforceArea = b.downforceArea * (1f + 0.3f * a);
            s.dragArea = b.dragArea * (1f - 0.03f * a);
            return s;
        }

        static VehicleSpec Copy(VehicleSpec b) => new VehicleSpec
        {
            id = b.id,
            displayName = b.displayName,
            prefab = b.prefab,
            modelScale = b.modelScale,
            drivetrain = b.drivetrain,
            mass = b.mass,
            maxTorque = b.maxTorque,
            redline = b.redline,
            gearRatios = b.gearRatios != null ? (float[])b.gearRatios.Clone() : null,
            finalDrive = b.finalDrive,
            wheelRadius = b.wheelRadius,
            tireGrip = b.tireGrip,
            dragArea = b.dragArea,
            downforceArea = b.downforceArea,
            steerLock = b.steerLock,
            brakeForce = b.brakeForce,
            hoodCamera = b.hoodCamera,
            bumperCamera = b.bumperCamera,
            engineVoice = b.engineVoice,
        };

        // ------------------------------------------------------------------ chiffres affichés

        /// <summary>Puissance estimée (ch) : couple max tenu jusqu'à 80 % du rupteur (comme à la concession).</summary>
        public static float Power(VehicleSpec s) => s.maxTorque * s.redline * 0.8f / 9549f * 1.36f;
        public static float TopKmh(VehicleSpec s) => s.TopSpeed * 3.6f;
        public static float PowerToWeight(VehicleSpec s) => Power(s) / Mathf.Max(s.mass, 1f) * 1000f;
        /// <summary>Décélération au freinage, en g (plafonnée par l'adhérence).</summary>
        public static float BrakingG(VehicleSpec s) => Mathf.Min(s.brakeForce / Mathf.Max(s.mass, 1f) / 9.81f, s.tireGrip * 1.1f);
        /// <summary>Appui à 200 km/h (kg).</summary>
        public static float DownforceKg(VehicleSpec s) => 0.5f * 1.225f * s.downforceArea * 55.6f * 55.6f / 9.81f;
    }
}
