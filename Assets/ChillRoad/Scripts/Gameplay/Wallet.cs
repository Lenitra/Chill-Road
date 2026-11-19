using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Argent du joueur et voitures achetées à la concession, sauvegardés sur la machine (PlayerPrefs).
    /// On commence avec la Citroën Ami seule ; les autres voitures s'achètent avec l'argent gagné en conduisant
    /// (frôlements, drifts, vitesse : voir <see cref="ScoreSystem"/>).
    /// </summary>
    public static class Wallet
    {
        const string CashKey = "chillroad_cash";
        const string OwnedKey = "chillroad_owned";
        public const string StarterCar = "ami";
        /// <summary>Argent d'un joueur qui débute (aucune sauvegarde d'argent).</summary>
        public const int StarterCash = 10000;

        /// <summary>Prix des voitures par identifiant (les voitures absentes de la table coûtent <see cref="DefaultPrice"/>).</summary>
        static readonly Dictionary<string, int> Prices = new Dictionary<string, int>
        {
            { "ami", 0 },
            { "twingo", 400 },
            { "p50", 3000 },
            { "ae86", 15000 },
            { "miata", 20000 },
            { "dmc12", 40000 },
            { "gryaris", 45000 },
            { "a110", 50000 },
            { "supra", 55000 },
            { "mustang", 70000 },
            { "vision74", 140000 },
            { "valkyrie", 260000 },
            { "jesko", 320000 },
        };
        const int DefaultPrice = 50000;

        static HashSet<string> owned;
        static int cash = -1;

        /// <summary>Argent disponible.</summary>
        public static int Cash
        {
            get { if (cash < 0) cash = Mathf.Max(0, PlayerPrefs.GetInt(CashKey, StarterCash)); return cash; }
        }

        /// <summary>Montant formaté selon la langue : « 12 345 $ » ou « $12,345 ».</summary>
        public static string Format(float amount) => Loc.T(Loc.Num(amount) + " $", "$" + Loc.Num(amount));

        public static int Price(VehicleSpec spec) => spec != null && Prices.TryGetValue(spec.id ?? "", out int p) ? p : DefaultPrice;

        public static void Add(float amount)
        {
            int a = Mathf.RoundToInt(amount);
            if (a <= 0) return;
            cash = (int)Math.Min((long)Cash + a, int.MaxValue);
            PlayerPrefs.SetInt(CashKey, cash);
            PlayerPrefs.Save();
        }

        /// <summary>Dépense une somme si l'argent suffit (vrai si payée).</summary>
        public static bool Spend(int amount)
        {
            if (amount < 0 || Cash < amount) return false;
            cash = Cash - amount;
            PlayerPrefs.SetInt(CashKey, cash);
            PlayerPrefs.Save();
            return true;
        }

        public static bool Owns(VehicleSpec spec)
        {
            if (spec == null) return false;
            Load();
            return owned.Contains(spec.id ?? "") || Price(spec) <= 0;
        }

        /// <summary>Achète une voiture si l'argent suffit (vrai si achetée).</summary>
        public static bool Buy(VehicleSpec spec)
        {
            if (spec == null || Owns(spec)) return false;
            int price = Price(spec);
            if (Cash < price) return false;
            cash = Cash - price;
            owned.Add(spec.id ?? "");
            PlayerPrefs.SetInt(CashKey, cash);
            PlayerPrefs.SetString(OwnedKey, string.Join(",", owned));
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Indice de la voiture de départ dans le garage (l'Ami, sinon la moins chère).</summary>
        public static int StarterIndex(VehicleSpec[] cars)
        {
            int best = 0;
            for (int i = 0; i < cars.Length; i++)
            {
                if (cars[i].id == StarterCar) return i;
                if (Price(cars[i]) < Price(cars[best])) best = i;
            }
            return best;
        }

        static void Load()
        {
            if (owned != null) return;
            owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in PlayerPrefs.GetString(OwnedKey, "").Split(','))
                if (!string.IsNullOrEmpty(id)) owned.Add(id);
            owned.Add(StarterCar);
        }
    }
}
