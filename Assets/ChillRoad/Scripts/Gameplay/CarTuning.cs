using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Améliorations achetées au garage de tuning (<see cref="TuningGarage"/>), voiture par voiture, sauvegardées sur
    /// la machine (PlayerPrefs). Deux sortes de pièces :
    /// - à niveaux (moteur, boîte, freins, allègement, aéro) : série puis niveaux 1 à 3 ;
    /// - à types (pneus, suspensions) : cinq montes au choix, chacune avec ses forces et ses faiblesses, sans meilleure
    ///   absolue (<see cref="TireProfile"/>, <see cref="SuspensionProfile"/>).
    /// Chaque option s'achète seule (acheter le niveau 3 ne donne pas les niveaux 1 et 2) ; une option achetée, comme
    /// la pièce de série, se remonte ensuite à volonté et gratuitement. L'option montée corrige un peu la fiche de la
    /// voiture (<see cref="VehicleSpec"/>) : une voiture améliorée reste elle-même, une Ami au maximum ne rattrape pas
    /// une Supra. Le prix est une part de la valeur de la voiture (<see cref="Wallet.Price"/>, avec un plancher) ; la
    /// voiture offerte au départ (Ami) s'améliore gratuitement.
    /// </summary>
    public static class CarTuning
    {
        /// <summary>Pièces, dans l'ordre du menu. L'indice est aussi la place du chiffre dans la sauvegarde.</summary>
        public enum Part { Engine, Gearbox, Tires, Brakes, Weight, Aero, Suspension }

        public const int PartCount = 7;
        public const int MaxLevel = 3;
        /// <summary>Types de pneus et de suspensions (origine comprise).</summary>
        public const int TypeCount = 5;
        /// <summary>Le plus grand nombre de lignes d'une pièce ouverte.</summary>
        public const int MaxOptions = TypeCount > MaxLevel + 1 ? TypeCount : MaxLevel + 1;

        public const int TireStock = 0, TireRally = 1, TireSlick = 2, TireSnow = 3, TireDrift = 4;
        public const int SuspStock = 0, SuspSport = 1, SuspTrack = 2, SuspRally = 3, SuspDrift = 4;

        /// <summary>Option montée de chaque pièce (un chiffre par pièce).</summary>
        const string Key = "chillroad_tune_";
        /// <summary>Options achetées de chaque pièce (un caractère par pièce : bit 0 = option 1, bit 1 = option 2…).</summary>
        const string OwnedKey = "chillroad_tuneown_";
        /// <summary>Version de la sauvegarde : absente = pneus encore à niveaux (avant les types).</summary>
        const string VersionKey = "chillroad_tunever_";
        const int Version = 2;

        /// <summary>Prix de chaque niveau, en part de la valeur de la voiture.</summary>
        static readonly float[] CostShare = { 0.04f, 0.08f, 0.14f };
        /// <summary>Prix de chaque type de pneus, puis de suspension (origine gratuite).</summary>
        static readonly float[] TireShare = { 0f, 0.05f, 0.07f, 0.05f, 0.06f };
        static readonly float[] SuspShare = { 0f, 0.06f, 0.10f, 0.08f, 0.08f };
        /// <summary>Valeur prise pour les voitures données ou très bon marché (Ami, Twingo).</summary>
        const int MinValue = 4000;

        // Adhérences relatives des surfaces (mêmes valeurs que la conduite), pour les chiffres de la fiche
        const float WetGrip = 0.7f;
        const float OffRoadGrip = 0.65f;
        const float SlideGrip = 0.8f;

        // ------------------------------------------------------------------ profils des types

        /// <summary>
        /// Monte de pneus : multiplicateurs appliqués par la conduite (<see cref="VehicleController"/>).
        /// grip : goudron sec ; wetLoss : perte sous la pluie (1 = celle d'origine) ; dirt : terre et hors route ;
        /// snow : neige ; slide : adhérence gardée en pleine glisse ; peak : glissement au pic (plus grand = plus
        /// progressif) ; rear : adhérence arrière relative (moins de 1 = arrière joueur).
        /// </summary>
        public struct TireProfile { public float grip, wetLoss, dirt, snow, slide, peak, rear; }

        static readonly TireProfile[] Tires =
        {
            new TireProfile { grip = 1f,    wetLoss = 1f,    dirt = 1f,    snow = 1f,    slide = 1f,    peak = 1f,    rear = 1f },    // origine
            new TireProfile { grip = 0.95f, wetLoss = 0.85f, dirt = 1.25f, snow = 1.15f, slide = 1.06f, peak = 1.15f, rear = 1f },    // rallye
            new TireProfile { grip = 1.14f, wetLoss = 1.9f,  dirt = 0.8f,  snow = 0.65f, slide = 0.94f, peak = 0.9f,  rear = 1f },    // slick
            new TireProfile { grip = 0.9f,  wetLoss = 0.75f, dirt = 1.08f, snow = 1.5f,  slide = 1f,    peak = 1.1f,  rear = 1f },    // neige
            new TireProfile { grip = 1f,    wetLoss = 1.1f,  dirt = 0.95f, snow = 0.85f, slide = 1.12f, peak = 1f,    rear = 0.87f }, // drift
        };

        /// <summary>
        /// Suspension : multiplicateurs des réglages de la conduite (<see cref="VehicleController"/>).
        /// sag : enfoncement statique (moins = ressorts plus raides) ; damp : amortissement ; arb : barres antiroulis ;
        /// travel : débattement ; drop : caisse abaissée (m, négatif = relevée) ; steer : braquage en plus (degrés) ;
        /// smooth / rough : adhérence sur chaussée lisse / sur terre, neige et hors route (roues qui suivent le sol).
        /// </summary>
        public struct SuspensionProfile { public float sag, damp, arb, travel, drop, steer, smooth, rough; }

        static readonly SuspensionProfile[] Suspensions =
        {
            new SuspensionProfile { sag = 1f,    damp = 1f,    arb = 1f,    travel = 1f,    drop = 0f,     steer = 0f,  smooth = 1f,    rough = 1f },    // origine
            new SuspensionProfile { sag = 0.8f,  damp = 1.12f, arb = 1.3f,  travel = 0.9f,  drop = 0.025f, steer = 0f,  smooth = 1.02f, rough = 0.97f }, // sport
            new SuspensionProfile { sag = 0.6f,  damp = 1.3f,  arb = 1.7f,  travel = 0.72f, drop = 0.05f,  steer = 0f,  smooth = 1.04f, rough = 0.88f }, // piste
            new SuspensionProfile { sag = 1.3f,  damp = 0.95f, arb = 0.75f, travel = 1.4f,  drop = -0.045f, steer = 0f, smooth = 0.98f, rough = 1.1f },  // rallye
            new SuspensionProfile { sag = 0.75f, damp = 1.2f,  arb = 1.5f,  travel = 0.85f, drop = 0.035f, steer = 14f, smooth = 1f,    rough = 0.95f }, // drift
        };

        public static TireProfile TireOf(VehicleSpec s) => Tires[Mathf.Clamp(s != null ? s.tires : 0, 0, TypeCount - 1)];
        public static SuspensionProfile SuspensionOf(VehicleSpec s) => Suspensions[Mathf.Clamp(s != null ? s.suspension : 0, 0, TypeCount - 1)];

        // ------------------------------------------------------------------ textes

        public static string Name(Part p)
        {
            switch (p)
            {
                case Part.Engine: return Loc.T("Moteur", "Engine");
                case Part.Gearbox: return Loc.T("Boîte de vitesses", "Gearbox");
                case Part.Tires: return Loc.T("Pneus", "Tires");
                case Part.Brakes: return Loc.T("Freins", "Brakes");
                case Part.Weight: return Loc.T("Allègement", "Weight reduction");
                case Part.Aero: return Loc.T("Aérodynamique", "Aero");
                default: return Loc.T("Suspensions", "Suspension");
            }
        }

        public static string Effect(Part p)
        {
            switch (p)
            {
                case Part.Engine: return Loc.T("+8 % de couple par niveau", "+8% torque per level");
                case Part.Gearbox: return Loc.T("Derniers rapports allongés : vitesse de pointe", "Longer top gears: higher top speed");
                case Part.Tires: return Loc.T("Cinq montes, chacune faite pour sa surface", "Five sets, each made for its surface");
                case Part.Brakes: return Loc.T("+12 % de freinage par niveau", "+12% braking per level");
                case Part.Weight: return Loc.T("-4 % de poids par niveau", "-4% weight per level");
                case Part.Aero: return Loc.T("Plus d'appui, moins de traînée", "More downforce, less drag");
                default: return Loc.T("Cinq réglages : fermeté, hauteur, braquage", "Five setups: stiffness, ride height, lock");
            }
        }

        /// <summary>Pièce à types (pneus, suspensions) plutôt qu'à niveaux.</summary>
        public static bool IsTyped(Part p) => p == Part.Tires || p == Part.Suspension;

        /// <summary>Nombre d'options d'une pièce, série / origine comprise.</summary>
        public static int Options(Part p) => IsTyped(p) ? TypeCount : MaxLevel + 1;

        /// <summary>Nom d'une option : « Série », « Niveau 2 », ou le type (« Slick », « Rallye »…).</summary>
        public static string OptionName(Part p, int i)
        {
            if (p == Part.Tires)
                switch (i)
                {
                    case TireRally: return Loc.T("Rallye", "Rally");
                    case TireSlick: return Loc.T("Slick", "Slick");
                    case TireSnow: return Loc.T("Neige", "Snow");
                    case TireDrift: return Loc.T("Drift", "Drift");
                    default: return Loc.T("Origine", "Stock");
                }
            if (p == Part.Suspension)
                switch (i)
                {
                    case SuspSport: return Loc.T("Sport", "Sport");
                    case SuspTrack: return Loc.T("Piste", "Track");
                    case SuspRally: return Loc.T("Rallye", "Rally");
                    case SuspDrift: return Loc.T("Drift", "Drift");
                    default: return Loc.T("Origine", "Stock");
                }
            return i == 0 ? Loc.T("Série", "Stock") : Loc.T($"Niveau {i}", $"Level {i}");
        }

        /// <summary>Ce que change une option (types seulement ; l'effet d'une pièce à niveaux sinon).</summary>
        public static string OptionEffect(Part p, int i)
        {
            if (p == Part.Tires)
                switch (i)
                {
                    case TireRally: return Loc.T("Terre et hors route ; moins vifs sur le goudron", "Dirt and off-road; less sharp on tarmac");
                    case TireSlick: return Loc.T("Imbattables sur le sec, piégeux sous la pluie", "Unbeatable in the dry, treacherous in the rain");
                    case TireSnow: return Loc.T("Mordent dans la neige, mous sur le goudron", "Bite into snow, soft on tarmac");
                    case TireDrift: return Loc.T("Arrière joueur, glisses longues et contrôlables", "Playful rear, long controllable slides");
                    default: return Loc.T("Polyvalents, sans point fort ni point faible", "All-rounders, no strength nor weakness");
                }
            if (p == Part.Suspension)
                switch (i)
                {
                    case SuspSport: return Loc.T("Plus ferme et plus basse : moins de roulis", "Firmer and lower: less body roll");
                    case SuspTrack: return Loc.T("Très ferme, au ras du sol : faite pour le goudron", "Very stiff and slammed: made for tarmac");
                    case SuspRally: return Loc.T("Souple et relevée : avale les bosses et la terre", "Soft and raised: soaks up bumps and dirt");
                    case SuspDrift: return Loc.T("Ferme, avec un grand angle de braquage", "Stiff, with extra steering lock");
                    default: return Loc.T("Réglage d'usine, confortable", "Factory setup, comfortable");
                }
            return Effect(p);
        }

        // ------------------------------------------------------------------ sauvegarde

        class State { public int[] fitted, owned; }
        static readonly Dictionary<string, State> states = new Dictionary<string, State>();
        static readonly Dictionary<VehicleSpec, (int key, VehicleSpec tuned)> cache = new Dictionary<VehicleSpec, (int, VehicleSpec)>();

        static int MaskMax(int part) => (1 << (Options((Part)part) - 1)) - 1;

        static State Load(VehicleSpec spec)
        {
            string id = spec != null ? spec.id ?? "" : "";
            if (states.TryGetValue(id, out var st)) return st;
            st = new State { fitted = new int[PartCount], owned = new int[PartCount] };
            string saved = PlayerPrefs.GetString(Key + id, "");
            string bought = PlayerPrefs.GetString(OwnedKey + id, "");
            for (int i = 0; i < PartCount; i++)
            {
                int f = i < saved.Length ? Mathf.Clamp(saved[i] - '0', 0, Options((Part)i) - 1) : 0;
                st.fitted[i] = f;
                if (bought.Length == 0) { st.owned[i] = (1 << f) - 1; continue; }        // avant les achats séparés : tout payé jusqu'au niveau monté
                int bits = i < bought.Length ? Mathf.Clamp(bought[i] - '0', 0, MaskMax(i)) : 0;
                st.owned[i] = f > 0 ? bits | 1 << (f - 1) : bits;
            }
            states[id] = st;

            // Pneus d'avant les types (niveaux 1 à 3 d'adhérence) : remis d'origine, les niveaux payés sont remboursés
            if (spec != null && saved.Length > 0 && PlayerPrefs.GetInt(VersionKey + id, 0) < Version)
            {
                int t = (int)Part.Tires, refund = 0;
                for (int l = 1; l <= MaxLevel; l++)
                    if ((st.owned[t] & 1 << (l - 1)) != 0) refund += LevelPrice(spec, l);
                st.fitted[t] = 0;
                st.owned[t] = 0;
                Wallet.Add(refund);
                if (refund > 0) Debug.Log($"[ChillRoad] Pneus de {id} : niveaux remplacés par les types, {refund} $ remboursés");
                Save(spec);
            }
            return st;
        }

        static void Save(VehicleSpec spec)
        {
            string id = spec.id ?? "";
            var st = Load(spec);
            var chars = new char[PartCount];
            for (int i = 0; i < PartCount; i++) chars[i] = (char)('0' + st.fitted[i]);
            PlayerPrefs.SetString(Key + id, new string(chars));
            for (int i = 0; i < PartCount; i++) chars[i] = (char)('0' + st.owned[i]);
            PlayerPrefs.SetString(OwnedKey + id, new string(chars));
            PlayerPrefs.SetInt(VersionKey + id, Version);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ achats

        /// <summary>Option montée sur la voiture (0 : série / origine).</summary>
        public static int Level(VehicleSpec spec, Part p) => Load(spec).fitted[(int)p];

        /// <summary>Vrai si cette option est à disposition (série, ou déjà achetée) : la monter est gratuit.</summary>
        public static bool Owns(VehicleSpec spec, Part p, int option)
        {
            if (option <= 0) return true;
            if (option >= Options(p)) return false;
            return (Load(spec).owned[(int)p] & (1 << (option - 1))) != 0;
        }

        /// <summary>Prix pour monter le niveau suivant d'une pièce à niveaux (0 : gratuit ou déjà acheté, -1 : au maximum).</summary>
        public static int NextPrice(VehicleSpec spec, Part p)
        {
            int l = Level(spec, p);
            return l >= MaxLevel ? -1 : Price(spec, p, l + 1);
        }

        /// <summary>Prix d'un niveau (1 à <see cref="MaxLevel"/>), le même pour toutes les pièces à niveaux de la voiture.</summary>
        public static int LevelPrice(VehicleSpec spec, int level) => SharePrice(spec, CostShare[Mathf.Clamp(level, 1, MaxLevel) - 1]);

        /// <summary>Prix catalogue d'une option (0 pour la série / l'origine), qu'elle soit achetée ou non.</summary>
        public static int OptionPrice(VehicleSpec spec, Part p, int option)
        {
            if (option <= 0) return 0;
            if (p == Part.Tires) return SharePrice(spec, TireShare[Mathf.Clamp(option, 0, TypeCount - 1)]);
            if (p == Part.Suspension) return SharePrice(spec, SuspShare[Mathf.Clamp(option, 0, TypeCount - 1)]);
            return LevelPrice(spec, option);
        }

        static int SharePrice(VehicleSpec spec, float share)
        {
            // Voiture offerte (Ami) : ses améliorations le sont aussi
            if (Wallet.Price(spec) <= 0) return 0;
            float value = Mathf.Max(Wallet.Price(spec), MinValue);
            // Arrondi à la dizaine (100 au-delà de 10 000)
            float raw = value * share;
            float step = raw >= 10000f ? 100f : 10f;
            return Mathf.Max(10, Mathf.RoundToInt(raw / step) * (int)step);
        }

        /// <summary>Prix pour monter une option : 0 si elle est déjà achetée (ou de série), son seul prix sinon.</summary>
        public static int Price(VehicleSpec spec, Part p, int option) => Owns(spec, p, option) ? 0 : OptionPrice(spec, p, option);

        /// <summary>
        /// Monte une option d'une pièce (0 : remettre la pièce de série). Gratuit si elle est déjà achetée, sinon payée
        /// seule s'il y a l'argent (les niveaux inférieurs restent à acheter). Vrai si la pièce a changé.
        /// </summary>
        public static bool Fit(VehicleSpec spec, Part p, int option)
        {
            if (spec == null || option < 0 || option >= Options(p) || Level(spec, p) == option) return false;
            var st = Load(spec);
            if (!Owns(spec, p, option))
            {
                if (!Wallet.Spend(OptionPrice(spec, p, option))) return false;
                st.owned[(int)p] |= 1 << (option - 1);
            }
            st.fitted[(int)p] = option;
            Save(spec);
            return true;
        }

        // ------------------------------------------------------------------ fiche modifiée

        /// <summary>
        /// Fiche de la voiture avec ses améliorations (la fiche d'origine si elle n'en a aucune). La copie est gardée
        /// tant que les options ne changent pas : la même voiture garde la même fiche.
        /// </summary>
        public static VehicleSpec Apply(VehicleSpec spec)
        {
            if (spec == null) return null;
            var l = Load(spec).fitted;
            int key = 0;
            for (int i = 0; i < PartCount; i++) key = key * 8 + l[i];
            if (key == 0) return spec;
            if (cache.TryGetValue(spec, out var c) && c.key == key) return c.tuned;
            var tuned = With(spec, l);
            cache[spec] = (key, tuned);
            return tuned;
        }

        /// <summary>Fiche avec une pièce sur une autre option (aperçu au garage). Copie neuve à chaque appel.</summary>
        public static VehicleSpec Preview(VehicleSpec spec, Part p, int option)
        {
            var l = (int[])Load(spec).fitted.Clone();
            l[(int)p] = Mathf.Clamp(option, 0, Options(p) - 1);
            return With(spec, l);
        }

        /// <summary>Fiche avec toutes les pièces à niveaux au maximum et des slicks (échelle des barres du garage).</summary>
        public static VehicleSpec Maxed(VehicleSpec spec)
        {
            var l = new int[PartCount];
            for (int i = 0; i < PartCount; i++) l[i] = IsTyped((Part)i) ? 0 : MaxLevel;
            l[(int)Part.Tires] = TireSlick;
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
            s.tires = l[(int)Part.Tires];
            s.suspension = l[(int)Part.Suspension];
            s.tireGrip = b.tireGrip * TireOf(s).grip;
            s.steerLock = b.steerLock + SuspensionOf(s).steer;
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
            tires = b.tires,
            suspension = b.suspension,
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

        /// <summary>Surfaces de la fiche des pneus.</summary>
        public enum Ground { Dry, Wet, Dirt, Snow, OffRoad }

        /// <summary>Adhérence des pneus montés sur une surface (même calcul que la conduite, hors charge et vitesse).</summary>
        public static float GripOn(VehicleSpec s, Ground g)
        {
            var t = TireOf(s);
            var u = SuspensionOf(s);
            switch (g)
            {
                case Ground.Wet: return s.tireGrip * u.smooth * Mathf.Max(0.3f, 1f - (1f - WetGrip) * t.wetLoss);
                case Ground.Dirt: return s.tireGrip * u.rough * RoadNetwork.DirtGrip * t.dirt;
                case Ground.Snow: return s.tireGrip * u.rough * RoadNetwork.SnowGrip * t.snow;
                case Ground.OffRoad: return s.tireGrip * u.rough * OffRoadGrip * t.dirt;
                default: return s.tireGrip * u.smooth;
            }
        }

        /// <summary>Adhérence gardée en pleine glisse sur le goudron (drift tenu).</summary>
        public static float SlideHold(VehicleSpec s) => s.tireGrip * SlideGrip * TireOf(s).slide;
    }
}
