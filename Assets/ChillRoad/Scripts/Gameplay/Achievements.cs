using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Succès du joueur : 30 objectifs en 5 catégories (route, vitesse, style, courses, carrière), suivis en jouant et
    /// sauvegardés sur la machine (PlayerPrefs). Les compteurs cumulés (distance, frôlements, argent gagné…) vivent ici ;
    /// les objectifs d'état (voitures achetées, courses terminées, tremplins) sont relus régulièrement dans le jeu.
    /// Un succès obtenu déclenche <see cref="Unlocked"/> : la notification en bas à droite (<see cref="AchievementToast"/>)
    /// et son carillon. L'écran des succès s'ouvre depuis le menu pause (<see cref="AchievementsMenu"/>).
    /// Au premier lancement, ce que la sauvegarde prouve déjà (voitures, records, tremplins) est accordé sans notification.
    /// Chaque succès rapporte une récompense en argent (<see cref="Def.Reward"/>), versée à l'obtention ; celles des succès
    /// accordés en silence sont versées ensemble en jeu, avec une notification récapitulative.
    /// </summary>
    public class Achievements : MonoBehaviour
    {
        public enum Category { Road, Speed, Style, Races, Career }
        public const int CategoryCount = 5;

        /// <summary>Compteurs suivis (index dans la sauvegarde des statistiques).</summary>
        public enum Stat
        {
            Distance, NightDistance, RainDistance, SnowDistance, TopSpeed, NearMisses, ExtremeNearMisses, BestCombo,
            Drifts, BestDrift, Jumps, BestAir, Stunts, StuntsAll, RacesFinished, DistinctRaces, AllRaces, Earned,
            CarsOwned, CarsAll, Tuned, Online, BestBeaten, CleanRace,
        }
        const int StatCount = 24;

        public class Def
        {
            public string id;
            public Category category;
            public string fr, en, descFr, descEn;
            public Stat stat;
            /// <summary>Valeur à atteindre (dans l'unité de la statistique : m, km/h, $, nombre…).</summary>
            public float goal;
            /// <summary>Unité affichée avec la progression (le compteur est divisé par <see cref="scale"/>).</summary>
            public string unitFr, unitEn;
            public float scale = 1f;
            public string Name => Loc.T(fr, en);
            public string Description => Loc.T(descFr, descEn);
            /// <summary>Objectif à franchir en une fois (oui / non) : pas de barre de progression.</summary>
            public bool Single => goal <= 1f && scale == 1f && string.IsNullOrEmpty(unitFr);
            /// <summary>Récompense versée au portefeuille à l'obtention ($).</summary>
            public int Reward => Rewards.TryGetValue(id, out int r) ? r : 0;
        }

        /// <summary>Récompense de chaque succès, selon sa difficulté (de 500 $ à 150 000 $).</summary>
        static readonly Dictionary<string, int> Rewards = new Dictionary<string, int>
        {
            { "road_1km", 500 }, { "road_100km", 5000 }, { "road_1000km", 50000 }, { "road_night", 5000 }, { "road_rain", 5000 }, { "road_snow", 5000 },
            { "speed_200", 2000 }, { "speed_300", 15000 }, { "speed_nm1", 500 }, { "speed_nm250", 10000 }, { "speed_extreme", 10000 }, { "speed_combo", 15000 },
            { "style_drift", 500 }, { "style_drift500", 5000 }, { "style_jump", 500 }, { "style_air", 5000 }, { "style_stunt", 2500 }, { "style_stunts", 50000 },
            { "race_1", 1000 }, { "race_10", 10000 }, { "race_pb", 2500 }, { "race_clean", 5000 }, { "race_5", 10000 }, { "race_all", 75000 },
            { "career_10k", 2500 }, { "career_1m", 100000 }, { "career_car", 2500 }, { "career_cars", 150000 }, { "career_tune", 2500 }, { "career_online", 5000 },
        };

        static Def D(string id, Category c, string fr, string en, string dfr, string den, Stat s, float goal,
            string ufr = null, string uen = null, float scale = 1f)
            => new Def { id = id, category = c, fr = fr, en = en, descFr = dfr, descEn = den, stat = s, goal = goal, unitFr = ufr, unitEn = uen ?? ufr, scale = scale };

        /// <summary>Tous les succès, dans l'ordre de l'écran (six par catégorie).</summary>
        public static readonly Def[] All =
        {
            D("road_1km", Category.Road, "Premiers tours de roue", "First miles", "Parcourir 1 km.", "Drive 1 km.", Stat.Distance, 1000f, "km", "km", 1000f),
            D("road_100km", Category.Road, "Grand tourisme", "Grand tourer", "Parcourir 100 km au total.", "Drive 100 km in total.", Stat.Distance, 100000f, "km", "km", 1000f),
            D("road_1000km", Category.Road, "Globe-trotteur", "Globetrotter", "Parcourir 1 000 km au total.", "Drive 1,000 km in total.", Stat.Distance, 1000000f, "km", "km", 1000f),
            D("road_night", Category.Road, "Noctambule", "Night owl", "Parcourir 25 km de nuit.", "Drive 25 km at night.", Stat.NightDistance, 25000f, "km", "km", 1000f),
            D("road_rain", Category.Road, "Chanteur sous la pluie", "Singing in the rain", "Parcourir 25 km sous l'orage.", "Drive 25 km in a storm.", Stat.RainDistance, 25000f, "km", "km", 1000f),
            D("road_snow", Category.Road, "Neige fraîche", "Fresh powder", "Parcourir 10 km sur les routes enneigées de la station.", "Drive 10 km on the ski resort's snowy roads.", Stat.SnowDistance, 10000f, "km", "km", 1000f),

            D("speed_200", Category.Speed, "Deux cents", "Double ton", "Atteindre 200 km/h.", "Reach 200 km/h.", Stat.TopSpeed, 200f, "km/h"),
            D("speed_300", Category.Speed, "Fusée", "Rocket", "Atteindre 300 km/h.", "Reach 300 km/h.", Stat.TopSpeed, 300f, "km/h"),
            D("speed_nm1", Category.Speed, "Frôleur", "Close shave", "Frôler un véhicule du trafic.", "Near miss a traffic vehicle.", Stat.NearMisses, 1f),
            D("speed_nm250", Category.Speed, "Slalom", "Slalom", "Réussir 250 frôlements.", "Pull off 250 near misses.", Stat.NearMisses, 250f, "", ""),
            D("speed_extreme", Category.Speed, "Au millimètre", "Paper-thin", "Réussir 50 frôlements extrêmes.", "Pull off 50 extreme near misses.", Stat.ExtremeNearMisses, 50f, "", ""),
            D("speed_combo", Category.Speed, "Tout ou rien", "All in", "Encaisser un combo de 10 000 $.", "Bank a $10,000 combo.", Stat.BestCombo, 10000f, "$"),

            D("style_drift", Category.Style, "Ça glisse", "Sideways", "Encaisser un premier drift.", "Bank your first drift.", Stat.Drifts, 1f),
            D("style_drift500", Category.Style, "Roi du drift", "Drift king", "Encaisser un drift de 500 $ d'un seul tenant.", "Bank a single $500 drift.", Stat.BestDrift, 500f, "$"),
            D("style_jump", Category.Style, "Décollage", "Liftoff", "Réussir un premier saut.", "Land your first jump.", Stat.Jumps, 1f),
            D("style_air", Category.Style, "Vol plané", "Hang time", "Rester 3 secondes en l'air.", "Stay airborne for 3 seconds.", Stat.BestAir, 3f, "s"),
            D("style_stunt", Category.Style, "Casse-cou", "Daredevil", "Réussir une cascade sur un tremplin.", "Clear a stunt jump.", Stat.Stunts, 1f),
            D("style_stunts", Category.Style, "Cascadeur", "Stunt master", "Réussir toutes les cascades de la carte.", "Clear every stunt jump on the map.", Stat.StuntsAll, 1f),

            D("race_1", Category.Races, "Premier départ", "Off the line", "Terminer une course.", "Finish a race.", Stat.RacesFinished, 1f),
            D("race_10", Category.Races, "Habitué des circuits", "Regular", "Terminer 10 courses.", "Finish 10 races.", Stat.RacesFinished, 10f, "", ""),
            D("race_pb", Category.Races, "Contre la montre", "Against the clock", "Battre un de ses records.", "Beat one of your best times.", Stat.BestBeaten, 1f),
            D("race_clean", Category.Races, "Sans une égratignure", "Not a scratch", "Terminer une course sans aucun choc.", "Finish a race without a single hit.", Stat.CleanRace, 1f),
            D("race_5", Category.Races, "Tour des circuits", "Circuit tour", "Terminer 5 courses différentes.", "Finish 5 different races.", Stat.DistinctRaces, 5f, "", ""),
            D("race_all", Category.Races, "Grand chelem", "Grand slam", "Terminer toutes les courses.", "Finish every race.", Stat.AllRaces, 1f),

            D("career_10k", Category.Career, "Tirelire", "Piggy bank", "Gagner 10 000 $ en conduisant.", "Earn $10,000 on the road.", Stat.Earned, 10000f, "$"),
            D("career_1m", Category.Career, "Millionnaire", "Millionaire", "Gagner 1 000 000 $ en conduisant.", "Earn $1,000,000 on the road.", Stat.Earned, 1000000f, "$"),
            D("career_car", Category.Career, "Nouvelle monture", "New ride", "Acheter une voiture à la concession.", "Buy a car at the dealer.", Stat.CarsOwned, 2f, "", ""),
            D("career_cars", Category.Career, "Collectionneur", "Collector", "Posséder toutes les voitures.", "Own every car.", Stat.CarsAll, 1f),
            D("career_tune", Category.Career, "Préparateur", "Tuner", "Monter une pièce au garage de tuning.", "Fit a part at the tuning garage.", Stat.Tuned, 1f),
            D("career_online", Category.Career, "Entre amis", "With friends", "Jouer une partie en ligne.", "Play an online game.", Stat.Online, 1f),
        };

        public static string CategoryName(Category c)
        {
            switch (c)
            {
                case Category.Road: return Loc.T("ROUTE", "ROAD");
                case Category.Speed: return Loc.T("VITESSE", "SPEED");
                case Category.Style: return Loc.T("STYLE", "STYLE");
                case Category.Races: return Loc.T("COURSES", "RACES");
                default: return Loc.T("CARRIÈRE", "CAREER");
            }
        }

        public static HudIcon.Kind CategoryIcon(Category c)
        {
            switch (c)
            {
                case Category.Road: return HudIcon.Kind.Road;
                case Category.Speed: return HudIcon.Kind.Gauge;
                case Category.Style: return HudIcon.Kind.Ramp;
                case Category.Races: return HudIcon.Kind.Flag;
                default: return HudIcon.Kind.Coin;
            }
        }

        // ------------------------------------------------------------------ sauvegarde

        const string UnlockKey = "chillroad_ach";          // « id:date unix, id:date… »
        const string StatsKey = "chillroad_ach_stats";     // compteurs séparés par « ; »
        const string RacesKey = "chillroad_ach_races";     // identifiants des courses terminées
        const string PaidKey = "chillroad_ach_paid";       // succès dont la récompense a été versée

        static Dictionary<string, long> unlocked;
        static float[] stats;
        static HashSet<string> racesDone, paid;
        static bool dirty;

        /// <summary>Succès obtenu (définition) : notification et carillon. Sa récompense est déjà versée.</summary>
        public static event Action<Def> Unlocked;
        /// <summary>Récompenses de succès obtenus auparavant (sauvegarde existante), versées d'un coup : nombre, montant.</summary>
        public static event Action<int, int> BackPaid;

        static void Load()
        {
            if (unlocked != null) return;
            unlocked = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var part in PlayerPrefs.GetString(UnlockKey, "").Split(','))
            {
                int c = part.IndexOf(':');
                if (c <= 0) continue;
                long.TryParse(part.Substring(c + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long when);
                unlocked[part.Substring(0, c)] = when;
            }
            stats = new float[StatCount];
            var values = PlayerPrefs.GetString(StatsKey, "").Split(';');
            for (int i = 0; i < values.Length && i < StatCount; i++)
                float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out stats[i]);
            racesDone = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in PlayerPrefs.GetString(RacesKey, "").Split(','))
                if (!string.IsNullOrEmpty(id)) racesDone.Add(id);
            paid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in PlayerPrefs.GetString(PaidKey, "").Split(','))
                if (!string.IsNullOrEmpty(id)) paid.Add(id);
        }

        static void Save()
        {
            if (unlocked == null || !dirty) return;
            dirty = false;
            var parts = new List<string>();
            foreach (var kv in unlocked) parts.Add(kv.Key + ":" + kv.Value.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(UnlockKey, string.Join(",", parts));
            var values = new string[StatCount];
            for (int i = 0; i < StatCount; i++) values[i] = stats[i].ToString("0.###", CultureInfo.InvariantCulture);
            PlayerPrefs.SetString(StatsKey, string.Join(";", values));
            PlayerPrefs.SetString(RacesKey, string.Join(",", racesDone));
            PlayerPrefs.SetString(PaidKey, string.Join(",", paid));
            PlayerPrefs.Save();
        }

        public static bool IsUnlocked(Def d) { Load(); return unlocked.ContainsKey(d.id); }

        /// <summary>Date d'obtention (heure locale), si obtenu.</summary>
        public static DateTime? UnlockDate(Def d)
        {
            Load();
            if (!unlocked.TryGetValue(d.id, out long when) || when <= 0) return null;
            return DateTimeOffset.FromUnixTimeSeconds(when).LocalDateTime;
        }

        public static int UnlockedCount
        {
            get { Load(); int n = 0; foreach (var d in All) if (unlocked.ContainsKey(d.id)) n++; return n; }
        }

        public static int CountIn(Category c, bool onlyUnlocked)
        {
            Load();
            int n = 0;
            foreach (var d in All) if (d.category == c && (!onlyUnlocked || unlocked.ContainsKey(d.id))) n++;
            return n;
        }

        /// <summary>Valeur actuelle de la statistique du succès (plafonnée à l'objectif s'il est obtenu).</summary>
        public static float Value(Def d)
        {
            Load();
            float v = stats[(int)d.stat];
            return IsUnlocked(d) ? Mathf.Max(v, d.goal) : v;
        }

        public static float Progress01(Def d) => Mathf.Clamp01(Value(d) / Mathf.Max(d.goal, 1e-4f));

        /// <summary>Progression lisible : « 12,4 / 25 km », « 180 / 200 km/h », « 3 / 5 ».</summary>
        public static string ProgressText(Def d)
        {
            float v = Mathf.Min(Value(d), d.goal) / d.scale, g = d.goal / d.scale;
            string unit = Loc.T(d.unitFr ?? "", d.unitEn ?? "");
            if (unit == "$") return Wallet.Format(Mathf.Floor(v)) + " / " + Wallet.Format(g);
            string vs = d.scale > 1f && v < 100f ? v.ToString("0.0", Loc.Numbers) : Loc.Num(Mathf.Floor(v));
            if (unit == "s") vs = v.ToString("0.0", Loc.Numbers);
            return vs + " / " + Loc.Num(g) + (unit.Length > 0 ? " " + unit : "");
        }

        /// <summary>Statistique cumulée (+=) ou record (max) ; les succès qui en dépendent sont vérifiés.</summary>
        static void Raise(Stat s, float value, bool add, bool notify = true)
        {
            Load();
            int i = (int)s;
            float before = stats[i];
            stats[i] = add ? before + value : Mathf.Max(before, value);
            if (stats[i] == before) return;
            dirty = true;
            foreach (var d in All)
                if (d.stat == s && stats[i] >= d.goal && !unlocked.ContainsKey(d.id)) Grant(d, notify);
        }

        static void Grant(Def d, bool notify)
        {
            unlocked[d.id] = notify ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0L;
            dirty = true;
            // Accordé en silence (déjà mérité dans la sauvegarde) : payé plus tard, en un récapitulatif (PayPending)
            if (notify) Pay(d);
            Save();
            if (notify) Unlocked?.Invoke(d);
        }

        /// <summary>Verse la récompense d'un succès obtenu, une seule fois.</summary>
        static bool Pay(Def d)
        {
            if (!paid.Add(d.id)) return false;
            dirty = true;
            if (d.Reward > 0) Wallet.Add(d.Reward);
            return true;
        }

        /// <summary>Récompense versée pour ce succès.</summary>
        public static bool IsPaid(Def d) { Load(); return paid.Contains(d.id); }

        /// <summary>Total des récompenses déjà versées.</summary>
        public static int RewardsEarned
        {
            get { Load(); int n = 0; foreach (var d in All) if (paid.Contains(d.id)) n += d.Reward; return n; }
        }

        /// <summary>Succès obtenus mais pas encore payés (sauvegarde d'avant les récompenses, accordés en silence) : tout est versé d'un coup.</summary>
        static void PayPending()
        {
            Load();
            int count = 0, cash = 0;
            foreach (var d in All)
                if (unlocked.ContainsKey(d.id) && Pay(d)) { count++; cash += d.Reward; }
            if (count == 0) return;
            Save();
            BackPaid?.Invoke(count, cash);
        }

        /// <summary>Remet tous les succès et compteurs à zéro (outil de test).</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(UnlockKey);
            PlayerPrefs.DeleteKey(StatsKey);
            PlayerPrefs.DeleteKey(RacesKey);
            PlayerPrefs.DeleteKey(PaidKey);
            PlayerPrefs.Save();
            unlocked = null;
            Load();
        }

        // ------------------------------------------------------------------ suivi en jeu

        GameManager game;
        PlayerSlot slot;
        ScoreSystem score;
        float lastRunDistance = -1f, pollTimer, saveTimer = 10f;
        bool catchUp = true;
        // Course en cours : record avant le départ, et aucun choc depuis le départ
        bool raceRunning, raceClean;
        float raceBestBefore;

        public void Init(GameManager g)
        {
            game = g;
            slot = g.Local;
            score = slot.Score;
            Load();
            score.NearMiss += (pts, gap) =>
            {
                if (!Counting) return;
                Raise(Stat.NearMisses, 1f, true);
                if (gap < score.extremeGap) Raise(Stat.ExtremeNearMisses, 1f, true);
            };
            score.DriftScored += pts => { if (!Counting) return; Raise(Stat.Drifts, 1f, true); Raise(Stat.BestDrift, pts, false); };
            score.JumpScored += (pts, air, dist) => { if (!Counting) return; Raise(Stat.Jumps, 1f, true); Raise(Stat.BestAir, air, false); };
            score.Banked += cash => { if (!Counting) return; Raise(Stat.Earned, cash, true); Raise(Stat.BestCombo, cash, false); };
            slot.Car.Impacted += (col, impact) => { if (impact > 2f) raceClean = false; };
        }

        /// <summary>Courses et tremplins : créés après le HUD, on s'y abonne dès qu'ils existent.</summary>
        void Hook()
        {
            if (hooked || !game.Stunts || !game.Races) return;
            hooked = true;
            game.Stunts.Completed += OnStunt;
            game.Races.Finished += OnRaceFinished;
        }
        bool hooked;

        void OnDestroy()
        {
            Save();
            if (!hooked || !game) return;
            if (game.Stunts) game.Stunts.Completed -= OnStunt;
            if (game.Races) game.Races.Finished -= OnRaceFinished;
        }

        void OnStunt(StuntJumps.Stunt s, float distance)
        {
            if (!Counting) return;
            Raise(Stat.Stunts, 1f, true);
            CheckState(true);
        }

        void OnApplicationQuit() => Save();

        /// <summary>Partie en cours, conduite par le joueur (pas la démo de l'écran d'accueil ni un serveur dédié).</summary>
        bool Counting => game && game.State == GameManager.Phase.Playing && !game.Demo && !GameManager.DedicatedServer;

        void OnRaceFinished(Races.Track t, float time, bool record)
        {
            if (!Counting) return;
            Raise(Stat.RacesFinished, 1f, true);
            if (racesDone.Add(t.id)) { dirty = true; Raise(Stat.DistinctRaces, racesDone.Count, false); }
            if (record && raceBestBefore > 0f) Raise(Stat.BestBeaten, 1f, false);
            if (raceClean && raceRunning) Raise(Stat.CleanRace, 1f, false);
            raceRunning = false;
            CheckState(true);
        }

        void Update()
        {
            if (!game || slot == null || !slot.Car) return;
            Hook();
            float dt = Time.deltaTime;

            // Départ d'une course : record d'avant, et la course est propre tant qu'aucun choc n'arrive
            bool running = game.Races && game.Races.Running;
            if (running && !raceRunning)
            {
                raceClean = true;
                raceBestBefore = game.Races.Active != null ? game.Races.Active.best : 0f;
            }
            raceRunning = running;

            if (Counting && dt > 0f)
            {
                // Distance : celle du compteur de la partie (repart de zéro à chaque partie)
                float run = score.RunDistance;
                float step = lastRunDistance < 0f ? 0f : Mathf.Clamp(run - lastRunDistance, 0f, 200f);
                lastRunDistance = run;
                if (step > 0f)
                {
                    Raise(Stat.Distance, step, true);
                    if (game.DayNight && game.DayNight.Night > 0.5f) Raise(Stat.NightDistance, step, true);
                    var w = game.Weather;
                    if (w && w.SnowZone > 0.5f) Raise(Stat.SnowDistance, step, true);
                    else if (w && w.Stormy && w.Rain > 0.3f) Raise(Stat.RainDistance, step, true);
                }
                // Vitesse de pointe, roues au sol (pas en tombant d'une falaise)
                if (slot.Car.GroundedWheels > 0) Raise(Stat.TopSpeed, slot.Car.Speed * 3.6f, false);
            }
            else lastRunDistance = score.RunDistance;

            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer <= 0f && game.State != GameManager.Phase.Loading)
            {
                pollTimer = 1f;
                CheckState(!catchUp);
                // Premier passage : ce que la sauvegarde prouve déjà est accordé en silence
                if (catchUp && game.Races && game.Races.All.Count > 0) catchUp = false;
                // Récompenses en attente : versées en jeu, une fois le rattrapage fait (la notification se voit)
                if (!catchUp && Counting) PayPending();
            }
            saveTimer -= Time.unscaledDeltaTime;
            if (saveTimer <= 0f) { saveTimer = 10f; Save(); }
        }

        /// <summary>Objectifs d'état relus dans le jeu : voitures, tuning, en ligne, courses et tremplins.</summary>
        void CheckState(bool notify)
        {
            var cars = game.assets ? game.assets.playerCars : null;
            if (cars != null && cars.Length > 0)
            {
                int owned = game.OwnedCars;
                Raise(Stat.CarsOwned, owned, false, notify);
                if (owned >= cars.Length) Raise(Stat.CarsAll, 1f, false, notify);
                var spec = cars[Mathf.Clamp(slot.CarIndex, 0, cars.Length - 1)];
                for (int p = 0; p < CarTuning.PartCount; p++)
                    if (CarTuning.Level(spec, (CarTuning.Part)p) > 0) { Raise(Stat.Tuned, 1f, false, notify); break; }
            }
            if (game.Online && !game.Demo) Raise(Stat.Online, 1f, false, notify);
            if (game.Races)
            {
                var tracks = game.Races.All;
                int done = 0;
                foreach (var t in tracks)
                {
                    if (t.best <= 0f) continue;
                    done++;
                    if (racesDone.Add(t.id)) dirty = true;
                }
                Raise(Stat.DistinctRaces, racesDone.Count, false, notify);
                if (done > 0) Raise(Stat.RacesFinished, done, false, notify);
                if (tracks.Count > 0 && done >= tracks.Count) Raise(Stat.AllRaces, 1f, false, notify);
            }
            if (game.Stunts)
            {
                int done = game.Stunts.DoneCount, total = game.Stunts.All.Count;
                if (done > 0) Raise(Stat.Stunts, done, false, notify);
                if (total > 0 && done >= total) Raise(Stat.StuntsAll, 1f, false, notify);
            }
        }
    }
}
