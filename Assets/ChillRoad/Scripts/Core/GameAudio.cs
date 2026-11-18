using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Sons synthétisés : moteur propre à chaque voiture (<see cref="EngineSynth"/>, en temps réel), vent, crissement,
    /// frôlement, radar, sirène, klaxon, choc, retours de flamme. Les tampons viennent de <see cref="SfxSynth"/> ;
    /// cette classe les joue et les dose selon la conduite.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        const int Rate = 44100;
        // Volumes : voies du menu Options (GameSettings), lues à chaque image
        static float EngineVol => GameSettings.EngineVolume;
        static float Fx => GameSettings.EffectsVolume;
        static float Ambience => GameSettings.AmbienceVolume;
        static float Ui => GameSettings.UiVolume;

        AudioSource windBody, windAir, skid, siren, horn;
        /// <summary>Klaxon tenu (R3 / H) : réglé par <see cref="GameManager"/> chaque image de conduite.</summary>
        public bool Horn;
        EngineSynth engine;
        float lastThrottle, revT, revPeak;
        bool revPop;
        AudioSource[] oneShots;
        int next;
        AudioClip whoosh, flash, crashSoft, crashHard, shift, blip, bonus, lose;
        AudioClip[] pops;
        // Tampons de SfxSynth en cours de calcul hors du thread principal (près d'une seconde) : vent, crissement et
        // sons ponctuels arrivent pendant le chargement ; d'ici là ils sont simplement muets
        System.Threading.Tasks.Task<float[][]> synthesis;
        VehicleController car;

        public void Init(VehicleController c, ScoreSystem score)
        {
            car = c;
            var engineGo = new GameObject("Engine");
            engineGo.transform.SetParent(transform, false);
            engineGo.AddComponent<AudioSource>();
            engine = engineGo.AddComponent<EngineSynth>();
            siren = Loop(Siren());
            horn = Loop(HornClip);
            oneShots = new AudioSource[8];
            for (int i = 0; i < oneShots.Length; i++) { oneShots[i] = gameObject.AddComponent<AudioSource>(); oneShots[i].playOnAwake = false; }
            synthesis = System.Threading.Tasks.Task.Run(() => new[]
            {
                SfxSynth.WindBody(), SfxSynth.WindAir(), SfxSynth.Skid(), SfxSynth.Whoosh(), SfxSynth.Flash(),
                SfxSynth.Crash(false), SfxSynth.Crash(true), SfxSynth.Shift(), SfxSynth.Blip(), SfxSynth.Bonus(), SfxSynth.Lose(),
                SfxSynth.Pop(0), SfxSynth.Pop(1), SfxSynth.Pop(2),
            });

            car.GearChanged += g => Play(shift, 0.2f * Ui, g > 1 ? 1f : 0.85f);
            // Petit choc : coup sourd ; gros choc : la tôle se froisse et des éclats retombent
            car.Impacted += (col, impact) => { if (impact > 3f) Play(impact > 9f ? crashHard : crashSoft, Mathf.Clamp01(impact / 15f) * Fx, Random.Range(0.85f, 1.1f)); };
            score.NearMiss += (pts, gap) => Play(whoosh, 0.8f * Ui, Random.Range(0.9f, 1.2f));
            score.Bonus += (pts, label) => Play(blip, 0.18f * Ui, 1f);         // vitesse : petite note, toutes les 5 s
            score.Banked += cash => Play(bonus, 0.5f * Ui, 1f);
            score.Lost += (pts, why) => Play(lose, 0.6f * Ui, 1f);
            score.DriftLost += pts => Play(lose, 0.45f * Ui, 1.1f);
            score.JumpScored += (pts, air, dist) => Play(bonus, 0.35f * Ui, 1.3f);   // réception d'un saut
        }

        /// <summary>Les tampons sont calculés (même ordre que dans <see cref="Init"/>) : crée les clips, lance les boucles.</summary>
        void Receive(float[][] d)
        {
            windBody = Loop(Stereo("WindBody", d[0], SfxSynth.HalfRate));
            windAir = Loop(Stereo("WindAir", d[1]));
            skid = Loop(Stereo("Skid", d[2]));
            whoosh = Stereo("Whoosh", d[3]);
            flash = Stereo("Flash", d[4]);
            crashSoft = Stereo("CrashSoft", d[5]);
            crashHard = Stereo("CrashHard", d[6]);
            shift = Stereo("Shift", d[7]);
            blip = Stereo("Blip", d[8]);
            bonus = Stereo("Bonus", d[9]);
            lose = Stereo("Lose", d[10]);
            pops = new[] { Stereo("Pop", d[11]), Stereo("Pop", d[12]), Stereo("Pop", d[13]) };
        }

        /// <summary>Carillon (achat à la concession).</summary>
        public void Chime() => Play(bonus, 0.55f * Ui, 1.12f);

        /// <summary>Déclic double du flash d'un radar.</summary>
        public void RadarFlash() => Play(flash, 0.7f * Ui, 1f);

        void Update()
        {
            if (!car) return;
            if (synthesis != null && synthesis.IsCompleted)
            {
                if (synthesis.IsFaulted) Debug.LogException(synthesis.Exception);
                else Receive(synthesis.Result);
                synthesis = null;
            }
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
                if (revPop && revT < 0.12f)
                {
                    revPop = false;
                    int popMode = CarCosmetics.Fitted(car.Spec).pop;
                    if (car.Exhausts.Count > 0 && popMode != CarCosmetics.PopOff)
                        PlayBackfire(popMode == CarCosmetics.PopDiesel ? 1.6f : popMode == CarCosmetics.PopBig ? 1.35f : 1f);
                }
            }
            bool limiter = car.Spec != null && car.Rpm >= car.Spec.redline * 0.985f && car.Throttle > 0.5f;
            engine.SetState(rpm, load, r, limiter, paused ? 0f : EngineVol * 0.3f, Time.unscaledDeltaTime);
            // Soupape de décharge du turbo au lever de pied sous pression
            if ((engine.Voice == EngineVoice.Inline6Turbo || engine.Voice == EngineVoice.FlatPlaneV8Turbo) && lastThrottle > 0.6f && car.Throttle < 0.2f && engine.Boost > 0.35f) engine.BlowOff();
            lastThrottle = car.Throttle;
            if (skid)
            {
                float speed01 = Mathf.Clamp01(car.Speed / 80f);
                // Vent : le fond gronde dès les vitesses moyennes, l'air ne siffle vraiment qu'à haute vitesse
                float windVolume = paused ? 0f : Ambience;
                windBody.volume = windVolume * 0.44f * speed01 * Mathf.Sqrt(speed01);
                windBody.pitch = 0.85f + speed01 * 0.25f;
                windAir.volume = windVolume * 0.28f * speed01 * speed01 * speed01;
                windAir.pitch = 0.8f + speed01 * 0.4f;
                // Crissement : plus aigu quand la voiture glisse davantage
                float skidTarget = car.GroundedWheels > 0 && car.Speed > 4f ? Mathf.Clamp01((car.SkidAmount - 0.25f) * 1.6f) : 0f;
                skid.volume = Mathf.Lerp(skid.volume, paused ? 0f : Fx * skidTarget * 0.35f, Time.unscaledDeltaTime * 10f);
                skid.pitch = Mathf.Lerp(skid.pitch, 0.92f + 0.16f * skidTarget, Time.unscaledDeltaTime * 6f);
            }
            // Klaxon : attaque franche, relâchement très court
            float hornTarget = Horn && !paused ? Fx * HornVolume : 0f;
            horn.volume = HornFade(horn.volume, hornTarget, Time.unscaledDeltaTime);
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
        public void PlayBackfire() => PlayBackfire(1f);

        /// <summary>
        /// Détonation d'un retour de flamme ; loud : volume relatif du pop and bang (1 petit, plus fort et plus grave
        /// pour le conséquent et le gazole).
        /// </summary>
        public void PlayBackfire(float loud)
        {
            float pitch = engine.Voice == EngineVoice.V8 ? 0.72f : engine.Voice == EngineVoice.V6 ? 0.85f : engine.Voice == EngineVoice.FlatPlaneV8Turbo ? 1.05f : engine.Voice == EngineVoice.V12 ? 1.3f : engine.Voice == EngineVoice.TwoStroke ? 1.7f
                : engine.Voice == EngineVoice.Inline4 ? 1.25f : 0.95f;
            float volume = (engine.Voice == EngineVoice.V8 ? 0.7f : 0.55f) * loud;
            pitch /= Mathf.Lerp(1f, 1.25f, Mathf.Clamp01(loud - 1f));
            if (pops != null) Play(pops[Random.Range(0, pops.Length)], volume * Fx, pitch * Random.Range(0.9f, 1.1f));
        }

        /// <summary>Son ponctuel ; volume déjà multiplié par sa voie (effets ou sons du score).</summary>
        void Play(AudioClip clip, float volume, float pitch)
        {
            if (!clip) return;      // pas encore synthétisé (tout début du chargement)
            // Une source libre de préférence : changer la hauteur d'une source qui sonne encore tordrait la fin de
            // son son précédent
            for (int i = 0; i < oneShots.Length && oneShots[next].isPlaying; i++) next = (next + 1) % oneShots.Length;
            var s = oneShots[next];
            next = (next + 1) % oneShots.Length;
            s.pitch = pitch;
            s.PlayOneShot(clip, volume);
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

        /// <summary>Clip stéréo (échantillons entrelacés gauche, droite) synthétisé par <see cref="SfxSynth"/>.</summary>
        static AudioClip Stereo(string name, float[] data, int rate = SfxSynth.Rate)
        {
            var c = AudioClip.Create(name, data.Length / 2, 2, rate, false);
            c.SetData(data, 0);
            return c;
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

        /// <summary>Volume du klaxon entendu de sa propre voiture (part du volume des effets).</summary>
        public const float HornVolume = 0.5f;

        static AudioClip hornClip;

        /// <summary>
        /// Son du klaxon, le même pour toutes les voitures : celle du joueur et celles des autres joueurs d'une partie en
        /// réseau (<see cref="RemoteCar"/>), synthétisé une seule fois.
        /// </summary>
        public static AudioClip HornClip => hornClip ? hornClip : hornClip = HornTone();

        /// <summary>Volume du klaxon une image plus tard : attaque franche, relâchement très court.</summary>
        public static float HornFade(float volume, float target, float dt) => Mathf.MoveTowards(volume, target, dt * (target > volume ? 25f : 12f));

        /// <summary>
        /// Klaxon de voiture : deux trompes accordées à la tierce majeure (420 et 525 Hz), timbre nasillard (harmoniques
        /// écrêtées doucement). Un nombre entier de périodes par seconde : la boucle est sans raccord.
        /// </summary>
        static AudioClip HornTone()
        {
            var d = new float[Rate];
            var tones = new[] { 420f, 525f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                foreach (float f in tones)
                {
                    float a = 2f * Mathf.PI * f * t;
                    float s = Mathf.Sin(a) + 0.55f * Mathf.Sin(2f * a) + 0.35f * Mathf.Sin(3f * a) + 0.2f * Mathf.Sin(4f * a);
                    v += (float)System.Math.Tanh(s * 1.8f);
                }
                d[i] = v * 0.32f;
            }
            return Clip("Horn", d);
        }
    }
}
