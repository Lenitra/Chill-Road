using System;
using System.Collections.Generic;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Cascades uniques : des tremplins sont posés au bord des routes (carte générée, Tools/Map/map_stunts.py ; modèle
    /// « stunt_ramp » du pack alpin). Chacun demande une distance à parcourir en l'air : la voiture décolle du
    /// tremplin, et si elle retombe sur ses roues au moins <c>target</c> mètres plus loin, la cascade est réussie.
    /// Une cascade ne rapporte sa prime qu'une fois (versée directement au portefeuille, hors combo) ; ensuite le
    /// tremplin reste un saut comme un autre, et le jeu retient la meilleure distance.
    /// Le temps en l'air et la distance viennent de <see cref="ScoreSystem"/> (le saut rapporte aussi son gain habituel).
    /// </summary>
    public class StuntJumps : MonoBehaviour
    {
        const string DoneKey = "nohesi_stunts";
        const string BestKey = "nohesi_stunt_best_";
        /// <summary>Couleur des cascades (HUD, carte) et des cascades réussies.</summary>
        public static readonly Color Tint = new Color(1f, 0.55f, 0.12f);
        public static readonly Color DoneTint = new Color(0.45f, 0.95f, 0.5f);
        static readonly Color FailTint = new Color(1f, 0.3f, 0.25f);
        /// <summary>Durée des lignes de cascade sous le combo (plus longue que celle d'un gain).</summary>
        const float FeedTime = 2.6f;

        public class Stunt
        {
            public MapData.StuntJson data;
            /// <summary>Pied du nez du tremplin (au sol), sens de la course et droite.</summary>
            public Vector3 lip, forward, right;
            public bool done;
            /// <summary>Meilleure distance réussie (m), 0 si jamais sauté.</summary>
            public float best;
            public string Name => Loc.T(data.fr, data.en);
            public float Target => data.target;
        }

        static readonly List<Stunt> None = new List<Stunt>();

        GameManager game;
        List<Stunt> stunts;
        bool wasAirborne;
        Stunt announced;
        float announceQuiet;
        // Résultat de la réception, affiché en fin de Tick : la ligne passe au-dessus de celle du saut (même image)
        string pendingLabel, pendingValue;
        Color pendingColor;

        /// <summary>Cascade en cours : la voiture a décollé de ce tremplin et n'est pas encore retombée.</summary>
        public Stunt Active { get; private set; }
        /// <summary>Toutes les cascades de la carte (vide tant que la carte n'est pas chargée).</summary>
        public IReadOnlyList<Stunt> All { get { Load(); return stunts ?? None; } }
        public int DoneCount
        {
            get
            {
                int n = 0;
                foreach (var s in All) if (s.done) n++;
                return n;
            }
        }
        /// <summary>Cascade réussie pour la première fois : cascade, distance.</summary>
        public event Action<Stunt, float> Completed;

        public void Init(GameManager g)
        {
            game = g;
            g.Score.Landed += OnLanded;
        }

        void Load()
        {
            if (stunts != null || !game || game.Map == null) return;
            stunts = new List<Stunt>();
            var list = game.Map.Json.stunts;
            if (list == null) return;
            var done = new HashSet<string>(PlayerPrefs.GetString(DoneKey, "").Split(','));
            foreach (var j in list)
            {
                if (string.IsNullOrEmpty(j.id) || j.target <= 0f) continue;
                var rot = Quaternion.Euler(0f, j.yaw, 0f);
                stunts.Add(new Stunt
                {
                    data = j, lip = new Vector3(j.x, j.y, j.z), forward = rot * Vector3.forward, right = rot * Vector3.right,
                    done = done.Contains(j.id), best = PlayerPrefs.GetFloat(BestKey + j.id, 0f),
                });
            }
        }

        /// <summary>À appeler après <see cref="ScoreSystem.Tick"/> (le saut de l'image y est déjà compté).</summary>
        public void Tick(float dt)
        {
            Load();
            if (stunts == null || stunts.Count == 0) return;
            var car = game.Player;
            Vector3 pos = car.transform.position;
            bool air = game.Score.Airborne;
            if (air && !wasAirborne) Active = RampUnder(pos, car.Velocity);
            else if (!air && Active != null)
            {
                // Saut interrompu sans réception (accident en l'air, voiture replacée)
                if (!Active.done) Report(Loc.T("CASCADE RATÉE", "STUNT FAILED"), "", FailTint);
                Active = null;
            }
            wasAirborne = air;
            Announce(pos, car, dt);
            if (pendingLabel != null)
            {
                if (game.Hud) game.Hud.Feed(pendingLabel, pendingValue, pendingColor, FeedTime);
                pendingLabel = null;
            }
        }

        /// <summary>Tremplin d'où la voiture vient de décoller : elle est au-dessus de sa moitié haute et va dans son sens.</summary>
        Stunt RampUnder(Vector3 pos, Vector3 velocity)
        {
            velocity.y = 0f;
            if (velocity.sqrMagnitude < 1f) return null;
            velocity.Normalize();
            foreach (var s in stunts)
            {
                Vector3 d = pos - s.lip;
                float along = Vector3.Dot(d, s.forward), side = Vector3.Dot(d, s.right);
                if (along < -s.data.length * 0.6f || along > 9f || Mathf.Abs(side) > s.data.width * 0.5f + 1.2f) continue;
                if (d.y < 0.5f || d.y > 12f || Vector3.Dot(velocity, s.forward) < 0.5f) continue;
                return s;
            }
            return null;
        }

        /// <summary>Réception d'un saut (ScoreSystem) : la cascade en cours est jugée sur la distance parcourue en l'air.</summary>
        void OnLanded(float air, float distance, bool clean)
        {
            var s = Active;
            Active = null;
            if (s == null) return;
            var hud = game.Hud;
            // Affichée arrondie vers le bas : jamais « 10 / 10 m » pour un saut de 9,6 m raté
            float shown = Mathf.Floor(distance);
            bool record = clean && distance > s.best + 0.5f;
            if (record)
            {
                s.best = distance;
                PlayerPrefs.SetFloat(BestKey + s.data.id, distance);
            }
            if (clean && distance >= s.Target && !s.done)
            {
                s.done = true;
                Save();
                Wallet.Add(s.data.reward);
                string name = s.Name.ToUpperInvariant();
                int n = DoneCount, total = stunts.Count;
                Report(Loc.F($"CASCADE RÉUSSIE · {name} · {shown:0} m · {n}/{total}", $"STUNT COMPLETE · {name} · {shown:0} m · {n}/{total}"),
                       "+" + Wallet.Format(s.data.reward), DoneTint);
                hud.Flash(Tint, 0.3f);
                if (game.Local.Audio) game.Local.Audio.Chime();
                game.Camera.Shake(0.25f);
                Completed?.Invoke(s, distance);
            }
            else if (!s.done)
            {
                if (clean) Report(Loc.T("CASCADE RATÉE", "STUNT FAILED"), Loc.F($"{shown:0} / {s.Target:0} m"), FailTint);
                else Report(Loc.T("CASCADE RATÉE", "STUNT FAILED"), Loc.T("RÉCEPTION MANQUÉE", "BAD LANDING"), FailTint);
            }
            else if (record)
                Report(Loc.F($"{s.Name.ToUpperInvariant()} · NOUVEAU RECORD", $"{s.Name.ToUpperInvariant()} · NEW RECORD"), Loc.F($"{shown:0} m"), DoneTint);
            if (record) PlayerPrefs.Save();
        }

        /// <summary>Ligne de cascade sous le combo, affichée en fin de <see cref="Tick"/>.</summary>
        void Report(string label, string value, Color color)
        {
            pendingLabel = label;
            pendingValue = value;
            pendingColor = color;
        }

        /// <summary>Annonce le tremplin dont la voiture approche (une fois par passage) : nom, distance à franchir, prime.</summary>
        void Announce(Vector3 pos, VehicleController car, float dt)
        {
            Stunt near = null;
            Vector3 v = car.Velocity;
            v.y = 0f;
            if (v.sqrMagnitude > 16f && Active == null)
            {
                v.Normalize();
                foreach (var s in stunts)
                {
                    Vector3 d = pos - s.lip;
                    float along = Vector3.Dot(d, s.forward);
                    if (along > -s.data.length * 0.4f || along < -s.data.length - 160f) continue;
                    if (Mathf.Abs(Vector3.Dot(d, s.right)) > 18f || Mathf.Abs(d.y) > 15f || Vector3.Dot(v, s.forward) < 0.7f) continue;
                    near = s;
                    break;
                }
            }
            if (near == null)
            {
                // Un peu de répit avant de pouvoir réannoncer le même tremplin (demi-tour, nouveau passage)
                if (announced != null && (announceQuiet += dt) > 5f) announced = null;
                return;
            }
            announceQuiet = 0f;
            if (near == announced || !game.Hud) return;
            announced = near;
            string name = near.Name.ToUpperInvariant();
            if (near.done)
                game.Hud.Feed(Loc.F($"CASCADE · {name} · RECORD", $"STUNT · {name} · BEST"), Loc.F($"{near.best:0} m"), DoneTint, FeedTime);
            else
                game.Hud.Feed(Loc.F($"CASCADE · {name} · {near.Target:0} m EN L'AIR", $"STUNT · {name} · {near.Target:0} m AIRBORNE"),
                              "+" + Wallet.Format(near.data.reward), Tint, FeedTime);
        }

        void Save()
        {
            var ids = new List<string>();
            foreach (var s in stunts) if (s.done) ids.Add(s.data.id);
            PlayerPrefs.SetString(DoneKey, string.Join(",", ids));
            PlayerPrefs.Save();
        }

        /// <summary>Oublie les cascades réussies et les records (outils, remise à zéro de la progression).</summary>
        public void Clear()
        {
            Load();
            PlayerPrefs.DeleteKey(DoneKey);
            foreach (var s in All)
            {
                s.done = false;
                s.best = 0f;
                PlayerPrefs.DeleteKey(BestKey + s.data.id);
            }
            PlayerPrefs.Save();
        }
    }
}
