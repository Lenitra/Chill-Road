using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Gains en conduisant : frôlements du trafic, drifts et vitesse rapportent de l'argent.
    /// Les gains s'ajoutent à la cagnotte du combo en cours (<see cref="RunScore"/>), sans multiplicateur.
    /// Tant qu'on enchaîne (ou qu'on roule vite), le combo continue ; quand il retombe
    /// (<see cref="comboWindow"/> secondes sans rien), la cagnotte est encaissée dans le portefeuille (<see cref="Wallet"/>).
    /// Un accident avant l'encaissement fait tout perdre.
    /// Suit aussi la distance parcourue.
    /// </summary>
    public class ScoreSystem : MonoBehaviour
    {
        public const string BestKey = "chillroad_best";

        [Tooltip("Durée du combo sans nouvelle action avant l'encaissement (s)")] public float comboWindow = 6f;

        [Header("Frôlements")]
        [Tooltip("Vitesse minimale du joueur (km/h)")] public float nearMissMinKmh = 60f;
        [Tooltip("Écart maximal entre les carrosseries (m)")] public float nearMissGap = 1.6f;
        [Tooltip("Écart d'un frôlement extrême (m)")] public float extremeGap = 0.6f;
        [Tooltip("Gain d'un frôlement à l'écart maximal / extrême, à 100 km/h")] public float nearMissCash = 40f, extremeCash = 110f;

        [Header("Drift")]
        [Tooltip("Angle de dérive à partir duquel on drifte (degrés)")] public float driftMinAngle = 14f;
        [Tooltip("Vitesse minimale du drift (km/h)")] public float driftMinKmh = 30f;
        [Tooltip("Gain par seconde d'un drift à 30° et 60 km/h")] public float driftCashPerSecond = 22f;

        [Header("Vitesse")]
        [Tooltip("Vitesse à partir de laquelle la vitesse rapporte (km/h)")] public float speedMinKmh = 130f;
        [Tooltip("Gain par seconde et par km/h au-delà du seuil")] public float speedCashPerKmh = 0.3f;

        [Header("Sauts")]
        [Tooltip("Temps en l'air minimal d'un saut (s)")] public float jumpMinAir = 0.45f;
        [Tooltip("Vitesse minimale au décollage (km/h)")] public float jumpMinKmh = 40f;
        [Tooltip("Gain par seconde en l'air")] public float jumpCashPerSecond = 60f;
        [Tooltip("Gain par mètre parcouru en l'air")] public float jumpCashPerMeter = 1.5f;
        [Tooltip("Temps en l'air au-delà duquel le saut ne rapporte plus davantage (s) : pas de fortune en tombant d'une falaise")] public float jumpMaxAir = 4f;

        [Header("Accidents")]
        [Tooltip("Choc (m/s) qui fait perdre la cagnotte ; un choc contre un véhicule du trafic la fait toujours perdre")] public float crashImpact = 5f;

        /// <summary>Cagnotte du combo en cours (pas encore encaissée).</summary>
        public float RunScore { get; private set; }
        public float ComboTimer { get; private set; }
        /// <summary>Combo en cours (cagnotte en jeu).</summary>
        public bool ComboActive => RunScore > 0f || Drifting || Speeding || Jumping;
        /// <summary>Meilleur combo encaissé.</summary>
        public float Best { get; private set; }
        /// <summary>Distance parcourue depuis le départ (m).</summary>
        public float RunDistance { get; private set; }
        public bool Recording { get; set; } = true;

        public bool Drifting { get; private set; }
        /// <summary>Gain du drift en cours, encaissé dans le combo à la sortie du drift.</summary>
        public float DriftPoints { get; private set; }
        public float DriftAngle { get; private set; }

        /// <summary>Au-dessus du seuil de vitesse : la vitesse rapporte et entretient le combo.</summary>
        public bool Speeding { get; private set; }

        /// <summary>Les quatre roues ont quitté le sol (à vitesse suffisante).</summary>
        public bool Airborne { get; private set; }
        /// <summary>Saut en cours, assez long pour compter : il entretient le combo et rapporte à la réception.</summary>
        public bool Jumping => Airborne && JumpTime >= jumpMinAir;
        public float JumpTime { get; private set; }
        /// <summary>Distance parcourue en l'air, à plat (m).</summary>
        public float JumpDistance { get; private set; }
        /// <summary>Gain du saut en cours, versé dans le combo à la réception.</summary>
        public float JumpPoints => jumpCashPerSecond * Mathf.Min(JumpTime, jumpMaxAir) + jumpCashPerMeter * Mathf.Min(JumpDistance, jumpMaxAir * 40f);

        public event Action<float, float, float> JumpScored; // gain, temps en l'air, distance
        /// <summary>Réception de tout saut, même court ou raté (cascades) : temps en l'air, distance, retombée sur les roues.</summary>
        public event Action<float, float, bool> Landed;
        public event Action<float, float> NearMiss;         // gain, écart
        public event Action<float, string> Lost;            // cagnotte perdue, raison
        public event Action<float, string> Bonus;           // gain, libellé (vitesse)
        public event Action<Vector3> NearMissAt;            // point entre la voiture et le véhicule frôlé
        public event Action<float> DriftScored;             // gain encaissé dans le combo
        public event Action<float> DriftLost;               // gain du drift perdu
        public event Action<float> Banked;                  // cagnotte versée au portefeuille

        VehicleController player;
        TrafficSystem traffic;
        float protect;
        float driftCalm, speedChunk, speedChunkTime, speedTop;
        Vector3 takeoff;
        float jumpMinUp, sinceLand = 10f;
        int teleports;

        /// <summary>Véhicule du trafic en train d'être dépassé ou croisé.</summary>
        class Pass
        {
            public TrafficSystem.Car car;
            public float startAlong, minGap = float.MaxValue;
            public bool overlapped, hit;
            public float relSpeed;
            public Vector3 closest;
        }
        readonly List<Pass> passes = new List<Pass>();

        public void Init(VehicleController car, TrafficSystem trafficSystem)
        {
            player = car;
            traffic = trafficSystem;
            Best = PlayerPrefs.GetFloat(BestKey, 0f);
            teleports = car.TeleportCount;
            car.Impacted += OnImpact;
        }

        /// <summary>Aucune perte de cagnotte pendant quelques secondes (voiture replacée, changée).</summary>
        public void Protect(float seconds) => protect = Mathf.Max(protect, seconds);

        public void Tick(float dt)
        {
            if (!player) return;
            RunDistance += player.Speed * dt;
            protect -= dt;
            // Voiture replacée : le drift et la vitesse en cours s'arrêtent sans rien rapporter
            if (player.TeleportCount != teleports)
            {
                teleports = player.TeleportCount;
                Drifting = false; DriftPoints = 0f;
                Speeding = false; speedChunk = 0f;
                Airborne = false;
                passes.Clear();
                Protect(2f);
            }
            if (!Recording) return;

            UpdateNearMisses();
            UpdateJump(dt);
            UpdateDrift(dt);
            UpdateSpeed(dt);

            // Combo : entretenu par le drift, la vitesse et les sauts, encaissé quand il retombe
            if (Drifting || Speeding || Jumping) ComboTimer = comboWindow;
            else if (ComboTimer > 0f)
            {
                ComboTimer -= dt;
                if (ComboTimer <= 0f) Bank();
            }
        }

        /// <summary>
        /// Seuil de vitesse à la portée de la voiture : une petite voiture (Ami, P50) gagne aussi, à une fraction de sa
        /// vitesse maximale ; les voitures rapides gardent le seuil réglé.
        /// </summary>
        float MinKmh(float kmh, float fractionOfTop) => player.Spec != null ? Mathf.Min(kmh, player.Spec.TopSpeed * 3.6f * fractionOfTop) : kmh;

        // ------------------------------------------------------------------ frôlements

        /// <summary>
        /// Un frôlement : un véhicule du trafic passe d'un côté à l'autre de la voiture (dépassé ou croisé) à moins de
        /// <see cref="nearMissGap"/> de la carrosserie, sans la toucher, avec une vraie différence de vitesse.
        /// </summary>
        void UpdateNearMisses()
        {
            Vector3 pos = player.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var b = player.LocalBounds;
            float halfLen = b.extents.z, halfWidth = b.extents.x;
            bool fastEnough = player.SpeedKmh >= MinKmh(nearMissMinKmh, 0.6f);

            // Fin des passages : véhicule passé de l'autre côté, éloigné ou disparu
            for (int i = passes.Count - 1; i >= 0; i--)
            {
                var p = passes[i];
                var c = p.car;
                if (c == null || !c.go || !c.go.activeInHierarchy || c.crashed || c.veiled || !traffic.Cars.Contains(c)) { passes.RemoveAt(i); continue; }
                Vector3 d = c.go.transform.position - pos; d.y = 0f;
                float along = Vector3.Dot(d, fwd), side = Vector3.Dot(d, right);
                bool overlap = Mathf.Abs(along) < TrafficSystem.Extent(c, fwd) + halfLen + 0.3f;
                if (overlap)
                {
                    float gap = Mathf.Abs(side) - TrafficSystem.Extent(c, right) - halfWidth;
                    if (gap < p.minGap) { p.minGap = gap; p.closest = pos + d * 0.5f; }
                    p.overlapped = true;
                    p.relSpeed = Mathf.Max(p.relSpeed, (player.Velocity - c.velocity).magnitude);
                }
                bool crossed = p.overlapped && !overlap && Mathf.Sign(along) != Mathf.Sign(p.startAlong);
                if (crossed && !p.hit && fastEnough && p.relSpeed > 5f && p.minGap < nearMissGap) ScoreNearMiss(p);
                if (crossed || d.sqrMagnitude > 30f * 30f || (!p.overlapped && Mathf.Sign(along) != Mathf.Sign(p.startAlong)))
                    passes.RemoveAt(i);
            }

            if (!fastEnough) return;
            // Nouveaux passages : véhicules proches, encore devant ou derrière (pas ceux qu'une course en réseau masque)
            foreach (var c in traffic.Cars)
            {
                if (c.crashed || c.veiled || !c.go || !c.go.activeInHierarchy) continue;
                Vector3 d = c.go.transform.position - pos; d.y = 0f;
                if (d.sqrMagnitude > 25f * 25f) continue;
                float along = Vector3.Dot(d, fwd);
                if (Mathf.Abs(along) < TrafficSystem.Extent(c, fwd) + halfLen + 0.3f) continue;
                if (Mathf.Abs(Vector3.Dot(d, right)) > 8f) continue;
                if (Tracked(c)) continue;
                passes.Add(new Pass { car = c, startAlong = along });
            }
        }

        bool Tracked(TrafficSystem.Car c)
        {
            foreach (var p in passes) if (p.car == c) return true;
            return false;
        }

        void ScoreNearMiss(Pass p)
        {
            float gap = Mathf.Max(0f, p.minGap);
            bool extreme = gap < extremeGap;
            float baseCash = extreme ? extremeCash : Mathf.Lerp(nearMissCash * 1.6f, nearMissCash, Mathf.InverseLerp(extremeGap, nearMissGap, gap));
            float speedFactor = Mathf.Clamp(player.SpeedKmh / 100f, 0.7f, 2.5f);
            float cash = baseCash * speedFactor;
            Add(cash);
            NearMiss?.Invoke(cash, gap);
            NearMissAt?.Invoke(p.closest);
        }

        // ------------------------------------------------------------------ drift

        void UpdateDrift(float dt)
        {
            float angle = player.SlipAngle;
            DriftAngle = angle;
            float minKmh = MinKmh(driftMinKmh, 0.5f);
            bool sliding = angle > driftMinAngle && angle < 110f && player.SpeedKmh > minKmh && player.GroundedWheels >= 2
                           && player.ForwardSpeed > 0f;
            if (sliding)
            {
                if (!Drifting) { Drifting = true; DriftPoints = 0f; }
                driftCalm = 0f;
                float angleFactor = Mathf.Clamp(angle / 30f, 0.5f, 2f);
                float speedFactor = Mathf.Clamp(player.SpeedKmh / 60f, 0.5f, 2.5f);
                DriftPoints += driftCashPerSecond * angleFactor * speedFactor * dt;
            }
            else if (Drifting)
            {
                // Petit répit entre deux virages : le drift continue s'il reprend vite (enchaînement) ;
                // en l'air, il attend la réception (drift, saut, drift)
                if (!Airborne) driftCalm += dt;
                if (driftCalm > 0.7f || player.SpeedKmh < minKmh * 0.6f) EndDrift();
            }
        }

        void EndDrift()
        {
            Drifting = false;
            float cash = DriftPoints;
            DriftPoints = 0f;
            // Simple coup de volant : rien
            if (cash < 8f) return;
            Add(cash);
            DriftScored?.Invoke(cash);
        }

        // ------------------------------------------------------------------ vitesse

        /// <summary>Au-delà du seuil, la vitesse rapporte un peu chaque seconde, versé par tranches de 5 s.</summary>
        void UpdateSpeed(float dt)
        {
            if (Airborne) return;   // en l'air, la vitesse en cours attend la réception
            float kmh = player.SpeedKmh, minKmh = MinKmh(speedMinKmh, 0.85f);
            bool fast = player.OnRoad && player.GroundedWheels >= 2 && kmh > (Speeding ? minKmh - 10f : minKmh);
            if (fast)
            {
                Speeding = true;
                speedChunk += (kmh - minKmh + 10f) * speedCashPerKmh * dt;
                speedChunkTime += dt;
                speedTop = Mathf.Max(speedTop, kmh);
                if (speedChunkTime >= 5f) FlushSpeed();
            }
            else if (Speeding)
            {
                Speeding = false;
                FlushSpeed();
            }
        }

        void FlushSpeed()
        {
            float cash = speedChunk;
            float top = speedTop;
            speedChunk = speedChunkTime = speedTop = 0f;
            if (cash < 1f) return;
            Add(cash);
            Bonus?.Invoke(cash, Loc.F($"VITESSE · {top:0} km/h", $"SPEED · {top:0} km/h"));
        }

        // ------------------------------------------------------------------ sauts

        /// <summary>
        /// Un saut : les quatre roues quittent le sol à plus de <see cref="jumpMinKmh"/> (bosse, crête, talus) pendant au
        /// moins <see cref="jumpMinAir"/> s. Il rapporte selon le temps en l'air et la distance, à condition de retomber
        /// sur ses roues ; un accident avant la réception fait tout perdre.
        /// </summary>
        void UpdateJump(float dt)
        {
            sinceLand += dt;
            if (player.GroundedWheels == 0)
            {
                if (!Airborne)
                {
                    // Rebond juste après une réception : pas un nouveau saut
                    if (player.SpeedKmh < MinKmh(jumpMinKmh, 0.55f) || sinceLand < 0.6f) return;
                    Airborne = true;
                    JumpTime = JumpDistance = 0f;
                    takeoff = player.transform.position;
                    jumpMinUp = 1f;
                }
                JumpTime += dt;
                jumpMinUp = Mathf.Min(jumpMinUp, Vector3.Dot(player.transform.up, Vector3.up));
                Vector3 d = player.transform.position - takeoff; d.y = 0f;
                JumpDistance = d.magnitude;
                // Immobile sans roue au sol : posée sur le toit ou sur le flanc, le saut est raté
                if (JumpTime > 0.3f && player.Speed < 2f) Airborne = false;
            }
            else if (Airborne) Land();
        }

        void Land()
        {
            Airborne = false;
            sinceLand = 0f;
            Landed?.Invoke(JumpTime, JumpDistance, Vector3.Dot(player.transform.up, Vector3.up) >= 0.5f && jumpMinUp >= 0.2f);
            if (JumpTime < jumpMinAir) return;
            // Réception sur le toit ou sur le flanc, ou tonneau en l'air : rien
            if (Vector3.Dot(player.transform.up, Vector3.up) < 0.5f || jumpMinUp < 0.2f) return;
            float cash = JumpPoints;
            Add(cash);
            JumpScored?.Invoke(cash, JumpTime, JumpDistance);
        }

        // ------------------------------------------------------------------ combo

        void Add(float cash)
        {
            RunScore += cash;
            ComboTimer = comboWindow;
        }

        /// <summary>Verse la cagnotte du combo au portefeuille.</summary>
        void Bank()
        {
            if (Drifting) EndDrift();
            if (Speeding) { Speeding = false; FlushSpeed(); }
            float cash = RunScore;
            ResetCombo();
            if (cash < 1f) return;
            Deposit(cash);
            Banked?.Invoke(cash);
        }

        void Deposit(float cash)
        {
            Wallet.Add(cash);
            if (cash > Best) { Best = cash; PlayerPrefs.SetFloat(BestKey, Best); }
        }

        void ResetCombo()
        {
            Drifting = false; DriftPoints = 0f;
            Speeding = false; speedChunk = speedChunkTime = speedTop = 0f;
            Airborne = false;
            RunScore = 0f;
            ComboTimer = 0f;
        }

        /// <summary>Tout ce qui est en jeu : cagnotte, drift et vitesse pas encore versés.</summary>
        public float AtStake => RunScore + DriftPoints + speedChunk;

        void OnImpact(Collision c, float impact)
        {
            if (!Recording || player.InputEnabled == false) return;
            var car = traffic ? traffic.Find(c.collider.gameObject) : null;
            if (car != null) foreach (var p in passes) if (p.car == car) p.hit = true;
            // Décor (glissière, mur, rocher) : seul le choc de face compte, pas la glissière frôlée à pleine vitesse
            float hard = c.contactCount > 0 ? Mathf.Abs(Vector3.Dot(c.relativeVelocity, c.GetContact(0).normal)) : impact;
            // Sol sous une voiture à l'endroit (réception d'un saut un peu sèche, bas de caisse qui touche) : pas un accident
            bool floor = c.contactCount > 0 && c.GetContact(0).normal.y > 0.65f && Vector3.Dot(player.transform.up, Vector3.up) > 0.6f;
            if (protect > 0f || (car == null && (hard < crashImpact || floor))) return;
            float lost = AtStake;
            bool drifting = Drifting && DriftPoints >= 8f && RunScore < 1f;
            ResetCombo();
            if (drifting) DriftLost?.Invoke(lost);
            else if (lost >= 1f) Lost?.Invoke(lost, Loc.T("ACCIDENT", "CRASH"));
            Protect(1.5f);
        }

        /// <summary>Fin de partie (retour au menu, quitter) : ce qui est en jeu est encaissé, sans annonce.</summary>
        public void SaveBest()
        {
            float cash = AtStake;
            ResetCombo();
            if (cash >= 1f) Deposit(cash);
        }

        void OnDestroy() => SaveBest();
        void OnApplicationQuit() => SaveBest();

    }
}
