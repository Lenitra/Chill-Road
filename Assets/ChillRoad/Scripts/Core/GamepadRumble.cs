using UnityEngine;
using UnityEngine.InputSystem;

namespace ChillRoad
{
    /// <summary>
    /// Vibrations de la manette (option GameSettings.Rumble) : deux moteurs, le gros à gauche (grave : chocs, réceptions,
    /// sol accidenté, frottements, vitesse) et le petit à droite (aigu : glisse des pneus, patinage, ABS, rupteur,
    /// rapports, frôlements). Une couche continue suit la conduite, des impulsions brèves s'y ajoutent sur les événements.
    /// Trois déclencheurs se coupent dans les options (<see cref="Trigger"/>) : drift, vitesse et collisions ; le reste
    /// (sol, sauts, ABS, rapports, frôlements, décomptes) suit seulement l'intensité.
    /// Seulement sur la manette utilisée en dernier (pas si l'on joue au clavier, manette posée sur le bureau), et coupée
    /// en pause, dans les menus et quand la fenêtre n'est plus active.
    /// </summary>
    public class GamepadRumble : MonoBehaviour
    {
        static GamepadRumble instance;

        /// <summary>Familles de vibrations réglables dans les options.</summary>
        public enum Trigger { Other, Drift, Speed, Collision }

        static bool Allowed(Trigger k) => k switch
        {
            Trigger.Drift => GameSettings.RumbleDrift,
            Trigger.Speed => GameSettings.RumbleSpeed,
            Trigger.Collision => GameSettings.RumbleCrash,
            _ => true,
        };

        struct Pulse { public float low, high, length, t; public bool ui; public Trigger kind; }
        readonly Pulse[] pulses = new Pulse[8];

        GameManager game;
        VehicleController car;
        float scrapeT, scrapeAmount;
        bool focused = true;
        Gamepad sentTo;
        float sentLow, sentHigh;

        public void Init(GameManager game, VehicleController car, ScoreSystem score)
        {
            instance = this;
            this.game = game;
            this.car = car;
            car.Impacted += (c, impact) =>
            {
                float k = Mathf.Clamp01(impact / 16f);
                Add(0.25f + 0.75f * k, 0.15f + 0.55f * k, 0.12f + 0.4f * k, Trigger.Collision);
            };
            car.Scraped += (point, normal, slide) => { scrapeT = 0.15f; scrapeAmount = Mathf.Max(scrapeAmount, Mathf.Clamp01(slide / 20f)); };
            car.GearChanged += g => { if (Driving) Add(0.04f, 0.16f, 0.06f); };
            if (score)
            {
                score.Landed += (air, distance, onWheels) =>
                {
                    if (air < 0.3f) return;
                    float k = Mathf.Clamp01(air / 1.6f);
                    Add(0.3f + 0.6f * k, 0.1f + 0.3f * k, 0.15f + 0.25f * k);
                };
                score.NearMiss += (gain, gap) => Add(0f, 0.28f, 0.07f);
            }
        }

        /// <summary>Impulsion brève (0-1 par moteur, durée en secondes), pour les événements du jeu (décompte, arrivée…).</summary>
        public static void Kick(float low, float high, float seconds, Trigger kind = Trigger.Other)
        {
            if (instance) instance.Add(low, high, seconds, kind);
        }

        /// <summary>
        /// Courte vibration de test quand on règle l'option (joue aussi en pause, menu ouvert) : un échantillon du
        /// déclencheur qu'on vient d'activer, ou une vibration neutre pour l'intensité.
        /// </summary>
        public static void Preview(Trigger kind = Trigger.Other)
        {
            if (!instance) return;
            switch (kind)
            {
                case Trigger.Drift: instance.Add(0.04f, 0.32f, 0.35f, kind, true); break;
                case Trigger.Speed: instance.Add(0.18f, 0.05f, 0.45f, kind, true); break;
                case Trigger.Collision: instance.Add(0.8f, 0.5f, 0.35f, kind, true); break;
                default: instance.Add(0.45f, 0.35f, 0.22f, kind, true); break;
            }
        }

        void Add(float low, float high, float seconds, Trigger kind = Trigger.Other, bool ui = false)
        {
            if (!Allowed(kind)) return;
            // Remplace l'impulsion terminée, sinon la plus faible
            int slot = 0;
            float weakest = float.MaxValue;
            for (int i = 0; i < pulses.Length; i++)
            {
                var p = pulses[i];
                float left = p.t >= p.length ? 0f : (p.low + p.high) * (1f - p.t / p.length);
                if (left < weakest) { weakest = left; slot = i; }
            }
            pulses[slot] = new Pulse { low = low, high = high, length = Mathf.Max(0.02f, seconds), t = 0f, ui = ui, kind = kind };
        }

        /// <summary>Partie en cours, voiture en main (pas le décompte ni l'écran titre).</summary>
        bool Driving => game && car && game.State == GameManager.Phase.Playing && car.InputEnabled && !car.Autopilot && !car.Remote;

        bool InMenu => Time.timeScale < 0.01f || OptionsMenu.IsOpen || (game && game.Hud && game.Hud.Paused);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool menu = InMenu;
            float low = 0f, high = 0f;

            // Impulsions : décroissance rapide (fin douce), seules celles de l'interface jouent dans les menus
            for (int i = 0; i < pulses.Length; i++)
            {
                ref var p = ref pulses[i];
                if (p.t >= p.length) continue;
                p.t += dt;
                if ((menu && !p.ui) || !Allowed(p.kind)) { p.t = p.length; continue; }
                float e = 1f - Mathf.Clamp01(p.t / p.length);
                e *= e;
                low = Mathf.Max(low, p.low * e);
                high = Mathf.Max(high, p.high * e);
            }

            scrapeT -= dt;
            if (scrapeT <= 0f) scrapeAmount = 0f;

            if (!menu && Driving && car.Body && !car.Body.isKinematic)
            {
                float t = Time.time;
                float speed = car.Speed;
                float grounded = car.WheelCount > 0 ? (float)car.GroundedWheels / car.WheelCount : 0f;

                // Sol meuble ou accidenté (terre, herbe, neige) : grondement qui suit la vitesse, avec du grain
                float rough = Mathf.Max(car.OnRoad ? 0f : 1f, car.SurfaceDirt, car.SurfaceSnow * 0.6f);
                if (rough > 0f && grounded > 0f)
                {
                    float grain = 0.55f + 0.45f * Mathf.PerlinNoise(t * (4f + speed * 0.5f), 0.3f);
                    low += rough * grounded * Mathf.Clamp01(speed / 22f) * 0.3f * grain;
                }

                // Drift : pneus qui glissent (dérapage, frein à main) et roues motrices qui patinent
                if (GameSettings.RumbleDrift)
                {
                    high += Mathf.Clamp01(car.SkidAmount) * Mathf.Clamp01(speed / 6f) * 0.3f * grounded;
                    high += Mathf.Clamp01(car.WheelSpin) * 0.22f;
                    low += Mathf.Clamp01(car.WheelSpin) * 0.06f;
                }

                // Vitesse : léger bourdonnement de la route qui monte à partir de 90 km/h (au plus vers 260 km/h)
                if (GameSettings.RumbleSpeed && grounded > 0f)
                {
                    float fast = Mathf.InverseLerp(25f, 72f, speed);
                    float wobble = 0.8f + 0.2f * Mathf.PerlinNoise(t * 3f, 4.2f);
                    low += fast * fast * 0.14f * grounded * wobble;
                    high += fast * 0.04f * grounded;
                }

                // ABS : battement rapide du petit moteur pendant qu'il retient les roues
                if (car.AbsActive && speed > 3f) high += Mathf.Repeat(t * 11f, 1f) < 0.5f ? 0.3f : 0.06f;

                // Rupteur : gaz à fond au régime maximal
                if (car.Rpm01 > 0.97f && car.Throttle > 0.5f && !car.Shifting) high += Mathf.Repeat(t * 14f, 1f) < 0.5f ? 0.12f : 0f;

                // Collisions : frottement contre une glissière, un mur ou une voiture
                if (scrapeAmount > 0f && GameSettings.RumbleCrash)
                {
                    float grain = 0.6f + 0.4f * Mathf.PerlinNoise(t * 22f, 7.1f);
                    low += scrapeAmount * 0.3f * grain;
                    high += scrapeAmount * 0.45f * grain;
                }
            }

            Send(low, high);
        }

        void Send(float low, float high)
        {
            GameSettings.Load();
            float gain = Mathf.Clamp01(GameSettings.Rumble);
            var pad = focused && GameInput.UsingGamepad ? Gamepad.current : null;
            low = Mathf.Clamp01(low * gain);
            high = Mathf.Clamp01(high * gain);
            if (low < 0.01f) low = 0f;
            if (high < 0.01f) high = 0f;

            if (sentTo != null && sentTo != pad) Stop(sentTo);
            if (pad == null) { sentTo = null; return; }
            // N'envoie au pilote que les changements (chaque appel est un message à la manette)
            if (pad == sentTo && Mathf.Abs(low - sentLow) < 0.015f && Mathf.Abs(high - sentHigh) < 0.015f
                && (low > 0f || sentLow == 0f) && (high > 0f || sentHigh == 0f)) return;
            pad.SetMotorSpeeds(low, high);
            sentTo = pad;
            sentLow = low;
            sentHigh = high;
        }

        void Stop(Gamepad pad)
        {
            if (pad != null && pad.added) pad.SetMotorSpeeds(0f, 0f);
            sentLow = sentHigh = 0f;
        }

        void OnApplicationFocus(bool focus)
        {
            focused = focus;
            if (!focus) StopAll();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) StopAll();
        }

        void StopAll()
        {
            if (sentTo != null) Stop(sentTo);
            sentTo = null;
            for (int i = 0; i < pulses.Length; i++) pulses[i].t = pulses[i].length;
        }

        void OnDisable() => StopAll();

        void OnDestroy()
        {
            StopAll();
            if (instance == this) instance = null;
        }

        void OnApplicationQuit() => InputSystem.ResetHaptics();
    }
}
