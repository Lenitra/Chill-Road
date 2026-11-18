using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Radars de vitesse : des tourelles sont posées au bord des routes (carte générée, Tools/Map/map_radars.py ; modèle
    /// « speed_camera » du pack alpin). Chacun contrôle toute la largeur de la route, dans les deux sens : passer devant
    /// à <c>target</c> km/h ou plus déclenche le flash. La première fois, le radar rapporte sa prime (versée directement
    /// au portefeuille, hors combo) ; ensuite il flashe toujours, et le jeu retient la meilleure vitesse.
    /// La vitesse est celle du compteur, relevée un court instant avant le radar : le flash part devant la voiture, là où
    /// le joueur le voit, et non quand la caméra a déjà dépassé le mât.
    /// </summary>
    public class SpeedCameras : MonoBehaviour
    {
        const string DoneKey = "chillroad_radars";
        const string BestKey = "chillroad_radar_best_";
        /// <summary>Couleur des radars (HUD, carte) et des radars déjà flashés.</summary>
        public static readonly Color Tint = new Color(1f, 0.9f, 0.35f);
        public static readonly Color DoneTint = StuntJumps.DoneTint;
        static readonly Color FailTint = new Color(1f, 0.3f, 0.25f);

        /// <summary>Avance du contrôle (s) : le radar se déclenche quand la voiture sera à sa hauteur dans ce délai.</summary>
        const float Lead = 0.18f;
        /// <summary>Écart entre les deux éclairs d'un flash (s), et délai avant qu'un radar puisse se redéclencher.</summary>
        const float SecondPulse = 0.12f, Rearm = 4f;
        /// <summary>Écart de hauteur toléré avec la route (m) : une voiture sur une autre route ne compte pas.</summary>
        const float MaxHeight = 8f;

        public class Radar
        {
            public MapData.RadarJson data;
            /// <summary>Point de l'axe de la route, sens de la route et droite ; position des lampes.</summary>
            public Vector3 center, forward, right, lamp;
            public bool done;
            /// <summary>Meilleure vitesse flashée (km/h), 0 si jamais flashé.</summary>
            public float best;
            public float rearm;
            public string Name => Loc.T(data.fr, data.en);
            public float Target => data.target;
        }

        static readonly List<Radar> None = new List<Radar>();

        GameManager game;
        List<Radar> radars;
        Vector3 lastAim;
        bool hasLast;
        int teleports;
        Radar announced;
        Vector3 pulseAt;
        float pulseTimer, announceQuiet;

        /// <summary>Tous les radars de la carte (vide tant que la carte n'est pas chargée).</summary>
        public IReadOnlyList<Radar> All { get { Load(); return radars ?? None; } }
        public int DoneCount
        {
            get
            {
                int n = 0;
                foreach (var r in All) if (r.done) n++;
                return n;
            }
        }

        /// <summary>Passage flashé (premier ou non) : radar, vitesse en km/h.</summary>
        public event Action<Radar, float> Flashed;

        public void Init(GameManager g) => game = g;

        void Load()
        {
            if (radars != null || !game || game.Map == null) return;
            radars = new List<Radar>();
            var list = game.Map.Json.radars;
            if (list == null) return;
            var done = new HashSet<string>(PlayerPrefs.GetString(DoneKey, "").Split(','));
            foreach (var j in list)
            {
                if (string.IsNullOrEmpty(j.id) || j.target <= 0f) continue;
                var rot = Quaternion.Euler(0f, j.yaw, 0f);
                radars.Add(new Radar
                {
                    data = j, center = new Vector3(j.x, j.y, j.z), forward = rot * Vector3.forward, right = rot * Vector3.right,
                    lamp = new Vector3(j.flashX, j.flashY, j.flashZ),
                    done = done.Contains(j.id), best = PlayerPrefs.GetFloat(BestKey + j.id, 0f),
                });
            }
        }

        bool Racing => game.Races && game.Races.Running;

        public void Tick(float dt)
        {
            Load();
            if (radars == null || radars.Count == 0) return;
            var car = game.Player;
            Vector3 velocity = car.Velocity;
            Vector3 aim = car.transform.position + velocity * Lead;
            bool moved = car.TeleportCount != teleports;
            teleports = car.TeleportCount;
            float reach = Mathf.Max(30f, car.Speed * dt * 3f);
            if (hasLast && !moved && (aim - lastAim).sqrMagnitude < reach * reach)
                foreach (var r in radars) Check(r, lastAim, aim, velocity, car);
            lastAim = aim;
            hasLast = true;

            foreach (var r in radars) if (r.rearm > 0f) r.rearm -= dt;
            if (pulseTimer > 0f && (pulseTimer -= dt) <= 0f) Burst(pulseAt, 0.8f);
            Announce(car, dt);
        }

        /// <summary>La voiture franchit-elle, en avançant, la ligne de contrôle du radar entre ces deux points ?</summary>
        void Check(Radar r, Vector3 from, Vector3 to, Vector3 velocity, VehicleController car)
        {
            float a = Vector3.Dot(from - r.center, r.forward), b = Vector3.Dot(to - r.center, r.forward);
            if (a < 0f == b < 0f || r.rearm > 0f) return;
            // Dans le sens de la marche : un choc qui ramène le point visé en arrière ne compte pas
            float travel = Vector3.Dot(velocity, r.forward);
            if (travel * (b - a) <= 0f) return;
            Vector3 p = Vector3.Lerp(from, to, a / (a - b));
            if (Mathf.Abs(Vector3.Dot(p - r.center, r.right)) > r.data.halfWidth || Mathf.Abs(p.y - r.center.y) > MaxHeight) return;
            r.rearm = Rearm;
            Pass(r, car.SpeedKmh, r.lamp - r.forward * (Mathf.Sign(travel) * 0.6f));
        }

        void Pass(Radar r, float kmh, Vector3 flashAt)
        {
            // Affichée arrondie vers le bas : jamais « 150 / 150 km/h » pour un passage raté à 149,6
            float shown = Mathf.Floor(kmh);
            string name = r.Name.ToUpperInvariant();
            if (kmh < r.Target)
            {
                if (!r.done && !Racing && kmh >= Mathf.Max(25f, r.Target * 0.5f))
                    Report(Loc.T("TROP LENT", "TOO SLOW"), Loc.F($"{name}  ·  {shown:0} / {r.Target:0} km/h"), FailTint);
                return;
            }
            Fire(flashAt);
            bool record = kmh > r.best + 1f;
            if (record)
            {
                r.best = kmh;
                PlayerPrefs.SetFloat(BestKey + r.data.id, kmh);
            }
            if (!r.done)
            {
                r.done = true;
                Save();
                Wallet.Add(r.data.reward);
                Report(Loc.T("FLASHÉ !", "FLASHED!"),
                       Loc.F($"{name}  ·  {shown:0} km/h  ·  +{Wallet.Format(r.data.reward)}  ·  {DoneCount}/{radars.Count}"), DoneTint);
                if (game.Local.Audio) game.Local.Audio.Chime();
            }
            else if (!Racing)
            {
                if (record) Report(Loc.T("NOUVEAU RECORD", "NEW RECORD"), Loc.F($"{name}  ·  {shown:0} km/h"), DoneTint);
                else Report(Loc.T("FLASHÉ !", "FLASHED!"), Loc.F($"{name}  ·  {shown:0} km/h"), Tint);
            }
            if (record) PlayerPrefs.Save();
            Flashed?.Invoke(r, kmh);
        }

        /// <summary>Flash : deux éclairs rapprochés sur le radar, voile blanc à l'écran et déclic.</summary>
        void Fire(Vector3 at)
        {
            Burst(at, 1f);
            pulseAt = at;
            pulseTimer = SecondPulse;
            if (game.Hud) game.Hud.Flash(Color.white, 0.2f);
            if (game.Local.Audio) game.Local.Audio.RadarFlash();
        }

        static void Burst(Vector3 at, float strength)
        {
            var fx = FxBursts.Instance;
            if (fx) fx.RadarFlash(at, strength);
        }

        void Report(string title, string detail, Color color)
        {
            if (game.Hud) game.Hud.Announce(title, detail, color, 3f);
        }

        /// <summary>Annonce le radar dont la voiture approche (une fois par passage) : nom, vitesse à atteindre, prime.</summary>
        void Announce(VehicleController car, float dt)
        {
            Radar near = null;
            Vector3 v = car.Velocity;
            v.y = 0f;
            if (v.sqrMagnitude > 64f && !Racing)
            {
                v.Normalize();
                Vector3 pos = car.transform.position;
                foreach (var r in radars)
                {
                    Vector3 to = r.center - pos;
                    if (Mathf.Abs(to.y) > 15f || Mathf.Abs(Vector3.Dot(v, r.forward)) < 0.7f) continue;
                    float along = Vector3.Dot(to, v);
                    if (along < 40f || along > 240f) continue;
                    if (Mathf.Abs(Vector3.Dot(to, r.right)) > r.data.halfWidth + 8f) continue;
                    near = r;
                    break;
                }
            }
            if (near == null)
            {
                // Un peu de répit avant de pouvoir réannoncer le même radar (demi-tour, nouveau passage)
                if (announced != null && (announceQuiet += dt) > 5f) announced = null;
                return;
            }
            announceQuiet = 0f;
            if (near == announced) return;
            announced = near;
            string title = near.Name.ToUpperInvariant();
            if (near.done)
                Report(title, Loc.F($"Flashé  ·  record {near.best:0} km/h", $"Flashed  ·  best {near.best:0} km/h"), DoneTint);
            else
                Report(title, Loc.F($"{near.Target:0} km/h  ·  +{Wallet.Format(near.data.reward)}"), Tint);
        }

        void Save()
        {
            var ids = new List<string>();
            foreach (var r in radars) if (r.done) ids.Add(r.data.id);
            PlayerPrefs.SetString(DoneKey, string.Join(",", ids));
            PlayerPrefs.Save();
        }

        /// <summary>Oublie les radars flashés et les records (outils, remise à zéro de la progression).</summary>
        public void Clear()
        {
            Load();
            PlayerPrefs.DeleteKey(DoneKey);
            foreach (var r in All)
            {
                r.done = false;
                r.best = 0f;
                r.rearm = 0f;
                PlayerPrefs.DeleteKey(BestKey + r.data.id);
            }
            PlayerPrefs.Save();
        }
    }
}
