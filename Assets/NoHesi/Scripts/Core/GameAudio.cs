using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Sons synthétisés : moteur propre à chaque voiture (<see cref="EngineSynth"/>, en temps réel), vent, crissement,
    /// frôlement, radar, sirène, choc, retours de flamme.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        const int Rate = 44100;
        [Range(0f, 1f)] public float masterVolume = 0.8f;

        AudioSource wind, skid, siren;
        EngineSynth engine;
        float lastThrottle, revT, revPeak;
        bool revPop;
        AudioSource[] oneShots;
        int next;
        AudioClip whoosh, flash, crash, blip, bonus, lose, pop;
        VehicleController car;

        public void Init(VehicleController c, ScoreSystem score)
        {
            car = c;
            var engineGo = new GameObject("Engine");
            engineGo.transform.SetParent(transform, false);
            engineGo.AddComponent<AudioSource>();
            engine = engineGo.AddComponent<EngineSynth>();
            wind = Loop(Noise(0.06f, 2.5f));
            skid = Loop(Screech());
            siren = Loop(Siren());
            oneShots = new AudioSource[8];
            for (int i = 0; i < oneShots.Length; i++) { oneShots[i] = gameObject.AddComponent<AudioSource>(); oneShots[i].playOnAwake = false; }
            whoosh = Whoosh();
            flash = Tones(new[] { 2200f, 2200f }, 0.05f, 0.1f, 0.3f);
            crash = Crash();
            blip = Tones(new[] { 700f }, 0.04f, 0.05f, 0.35f);
            bonus = Tones(new[] { 523f, 659f, 784f, 1047f }, 0.07f, 0.3f, 0.35f);
            lose = Tones(new[] { 330f, 247f, 196f }, 0.12f, 0.3f, 0.35f);
            pop = Pop();

            car.GearChanged += g => Play(blip, 0.1f, g > 1 ? 0.5f : 0.4f);
            car.Impacted += (col, impact) => { if (impact > 3f) Play(crash, Mathf.Clamp01(impact / 15f), Random.Range(0.85f, 1.1f)); };
            score.NearMiss += (pts, gap) => Play(whoosh, 0.8f, Random.Range(0.9f, 1.2f));
            score.Bonus += (pts, label) => Play(blip, 0.18f, 1.4f);       // vitesse : petite note, toutes les 5 s
            score.Banked += cash => Play(bonus, 0.5f, 1f);
            score.Lost += (pts, why) => Play(lose, 0.6f, 1f);
            score.DriftLost += pts => Play(lose, 0.45f, 1.1f);
            score.JumpScored += (pts, air, dist) => Play(bonus, 0.35f, 1.3f);   // réception d'un saut
        }

        /// <summary>Carillon (achat à la concession).</summary>
        public void Chime() => Play(bonus, 0.55f, 1.12f);

        void Update()
        {
            if (!car) return;
            bool paused = Time.timeScale < 0.01f;
            // Moteur : voix de la voiture courante (change aux relais), régime réel, charge = accélérateur
            if (car.Spec != null) engine.SetVoice(car.Spec.Voice);
            float r = car.Rpm01;
            float load = car.Shifting ? 0.1f : car.Throttle;
            float rpm = car.Rpm;
            // Coups d'accélérateur du compte à rebours (voiture à l'arrêt, moteur qui rugit)
            if (revT > 0f && car.Spec != null)
            {
                revT -= Time.unscaledDeltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(1f - revT / 0.5f) * Mathf.PI);
                rpm = Mathf.Lerp(car.Rpm, car.Spec.redline * (0.5f + 0.4f * revPeak), k);
                r = Mathf.InverseLerp(900f, car.Spec.redline, rpm);
                load = Mathf.Max(load, k);
                if (revPop && revT < 0.12f) { revPop = false; if (car.Exhausts.Count > 0) PlayBackfire(); }
            }
            bool limiter = car.Spec != null && car.Rpm >= car.Spec.redline * 0.985f && car.Throttle > 0.5f;
            engine.SetState(rpm, load, r, limiter, paused ? 0f : masterVolume * 0.3f, Time.unscaledDeltaTime);
            // Soupape de décharge du turbo au lever de pied sous pression
            if ((engine.Voice == EngineVoice.Inline6Turbo || engine.Voice == EngineVoice.FlatPlaneV8Turbo) && lastThrottle > 0.6f && car.Throttle < 0.2f && engine.Boost > 0.35f) engine.BlowOff();
            lastThrottle = car.Throttle;
            float speed01 = Mathf.Clamp01(car.Speed / 80f);
            wind.volume = paused ? 0f : masterVolume * speed01 * speed01 * 0.35f;
            wind.pitch = 0.7f + speed01 * 0.6f;
            float skidTarget = car.GroundedWheels > 0 && car.Speed > 4f ? Mathf.Clamp01((car.SkidAmount - 0.25f) * 1.6f) : 0f;
            skid.volume = Mathf.Lerp(skid.volume, paused ? 0f : masterVolume * skidTarget * 0.35f, Time.unscaledDeltaTime * 10f);
            bool chase = false;
            siren.volume = paused || !chase ? 0f : 0f;
        }

        /// <summary>Coup d'accélérateur à l'arrêt (compte à rebours) ; le plus fort finit par une pétarade.</summary>
        public void Rev(float amount)
        {
            revT = 0.5f;
            revPeak = amount;
            revPop = amount >= 1f;
        }

        /// <summary>Détonation d'un retour de flamme à l'échappement : grave pour le V8, sèche pour le 4 cylindres.</summary>
        public void PlayBackfire()
        {
            float pitch = engine.Voice == EngineVoice.V8 ? 0.72f : engine.Voice == EngineVoice.V6 ? 0.85f : engine.Voice == EngineVoice.FlatPlaneV8Turbo ? 1.05f : engine.Voice == EngineVoice.V12 ? 1.3f : engine.Voice == EngineVoice.TwoStroke ? 1.7f
                : engine.Voice == EngineVoice.Inline4 ? 1.25f : 0.95f;
            float volume = engine.Voice == EngineVoice.V8 ? 0.7f : 0.55f;
            Play(pop, volume, pitch * Random.Range(0.9f, 1.1f));
        }

        void Play(AudioClip clip, float volume, float pitch)
        {
            var s = oneShots[next];
            next = (next + 1) % oneShots.Length;
            s.pitch = pitch;
            s.PlayOneShot(clip, volume * masterVolume);
        }

        AudioSource Loop(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = 0f;
            s.Play();
            return s;
        }

        static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Noise(float smoothing, float gain)
        {
            var d = new float[Rate];
            var rng = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++) { lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * smoothing; d[i] = lp * gain; }
            return Clip("Noise", d);
        }

        static AudioClip Screech()
        {
            var d = new float[Rate];
            var rng = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = (float)rng.NextDouble() * 2f - 1f;
                lp += (n - lp) * 0.3f;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 900f * t + 2f * Mathf.Sin(2f * Mathf.PI * 6f * t)) * 0.3f + (n - lp) * 0.35f) * 0.7f;
            }
            return Clip("Screech", d);
        }

        static AudioClip Siren()
        {
            var d = new float[Rate * 2];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = t % 1f < 0.5f ? 950f : 720f; // deux tons
                phase += 2f * Mathf.PI * f / Rate;
                d[i] = (Mathf.Sin(phase) > 0f ? 0.4f : -0.4f) * 0.5f + Mathf.Sin(phase) * 0.3f;
            }
            return Clip("Siren", d);
        }

        static AudioClip Whoosh()
        {
            int len = (int)(0.45f * Rate);
            var d = new float[len];
            var rng = new System.Random(13);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * Mathf.Lerp(0.05f, 0.4f, Mathf.Sin(t * Mathf.PI));
                d[i] = lp * Mathf.Sin(t * Mathf.PI) * 1.4f;
            }
            return Clip("Whoosh", d);
        }

        static AudioClip Pop()
        {
            int len = (int)(0.16f * Rate);
            var d = new float[len];
            var rng = new System.Random(21);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.45f;
                // Claquement bref + grave qui décroît vite, parfois doublé
                float env = Mathf.Exp(-t * 38f) + (t > 0.05f ? Mathf.Exp(-(t - 0.05f) * 45f) * 0.6f : 0f);
                d[i] = Mathf.Clamp((lp * 1.4f + Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.8f) * env, -1f, 1f);
            }
            return Clip("Pop", d);
        }

        static AudioClip Crash()
        {
            int len = (int)(0.6f * Rate);
            var d = new float[len];
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.35f;
                d[i] = (lp + Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.6f) * Mathf.Exp(-t * 7f);
            }
            return Clip("Crash", d);
        }

        static AudioClip Tones(float[] freqs, float note, float tail, float volume)
        {
            int ns = (int)(note * Rate);
            int total = ns * freqs.Length + (int)(tail * Rate);
            var d = new float[total];
            for (int n = 0; n < freqs.Length; n++)
                for (int i = 0; n * ns + i < total; i++)
                {
                    float t = i / (float)Rate;
                    d[n * ns + i] += Mathf.Sin(2f * Mathf.PI * freqs[n] * t) * Mathf.Exp(-t * 12f) * Mathf.Clamp01(i / 150f) * volume;
                }
            return Clip("Tones", d);
        }
    }
}
