using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Moteur synthétisé en temps réel. Chaque combustion envoie une onde de pression grave dans l'échappement :
    /// une bosse de pression (la « pulsation » du moteur) et quelques résonances amorties de la ligne d'échappement,
    /// au régime réel (pas de boucle étirée). Le grave domine ; le grain et le souffle restent discrets.
    /// V8 à vilebrequin croisé (impulsions inégales entre rangées → borborygme), 6 en ligne turbo (sifflement,
    /// soupape de décharge), 4 cylindres rageur, électrique (voix de synthèse grave, sans combustion).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineSynth : MonoBehaviour
    {
        sealed class Profile
        {
            public EngineVoice voice;
            public float[] fireAt;           // instants d'allumage dans le cycle (0-1 = deux tours de vilebrequin)
            public float[] amp;              // force de chaque combustion
            public Vector3[] modes;          // résonances de l'échappement : (fréquence Hz, décroissance ms, niveau)
            public float thump = 0.7f;       // bosse de pression à chaque combustion
            public float thumpMs = 0.8f;
            public float grit = 0.06f;       // grain : bruit filtré à chaque combustion
            public float gritHz = 900f;      // clarté du grain
            public float synth;              // voix de synthèse (dents de scie filtrées)
            public float turbo;              // sifflement de turbo
            public float crackle;            // pétarades au lever de pied
            public float drive = 1.6f;       // saturation
            public float gain = 1f;          // normalisation (calculée à la création)
        }

        /// <summary>État de synthèse : une voix avec ses filtres (temps réel, ou rendu hors ligne).</summary>
        sealed class State
        {
            public Profile profile;
            public int rate;
            public double phase, nextFire, cycleBase;
            public int nextCyl;
            public float gritA;
            public float impulse, burstEnv, burstK, thump, thump2, thumpK, thumpA, noiseLp, bov, bovK, turboPhase, synthPhase, bassLp;
            public float dcX, dcY;
            public float[] c1, c2, norm, y1, y2;
            public System.Random rng = new System.Random(7);
        }

        // Paramètres écrits par le thread principal, lus par le thread audio
        volatile float rpm = 900f, load, volume, rpm01, boost;
        volatile bool limiter;
        volatile int bovTrigger;
        volatile State state;

        int sampleRate, bovSeen;
        EngineVoice current = EngineVoice.Auto;
        volatile float echoTarget;
        TunnelEcho echo;

        public EngineVoice Voice => current;
        public float Boost => boost;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            echo = new TunnelEcho(sampleRate);
            var src = GetComponent<AudioSource>();
            // Boucle muette : la source doit jouer pour que OnAudioFilterRead soit appelé
            src.clip = AudioClip.Create("EngineCarrier", sampleRate, 1, sampleRate, false);
            src.loop = true;
            src.volume = 1f;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.Play();
        }

        /// <summary>Change de voix (changement de voiture).</summary>
        public void SetVoice(EngineVoice voice)
        {
            if (voice == current && state != null) return;
            current = voice;
            state = Create(voice, sampleRate);
        }

        /// <summary>Régime (tr/min), charge (0-1), régime relatif, rupteur, volume : appelé à chaque image.</summary>
        public void SetState(float rpmValue, float loadValue, float rpmRelative, bool atLimiter, float vol, float dt)
        {
            rpm = rpmValue;
            load = loadValue;
            rpm01 = rpmRelative;
            limiter = atLimiter;
            volume = vol;
            // Turbo : la pression monte avec la charge et le régime, retombe vite au lever de pied
            float target = loadValue * Mathf.InverseLerp(0.3f, 0.75f, rpmRelative);
            boost = Mathf.MoveTowards(boost, target, dt * (target > boost ? 1.1f : 3f));
        }

        /// <summary>Part d'écho (0 à l'air libre, 1 au fond d'un tunnel) : appelé à chaque image.</summary>
        public void SetEcho(float amount) => echoTarget = Mathf.Clamp01(amount);

        /// <summary>Soupape de décharge (« pschitt ») au lever de pied sous pression.</summary>
        public void BlowOff() => bovTrigger++;

        /// <summary>Rendu hors ligne (tests, aperçu) : régime et charge constants, sortie saturée.</summary>
        public static float[] Render(EngineVoice voice, float rpmValue, float loadValue, float rpmRelative, float seconds, int rate)
        {
            var s = Create(voice, rate);
            var d = new float[(int)(seconds * rate)];
            float n = Tanh(s.profile.drive);
            for (int i = 0; i < d.Length; i++)
                d[i] = Mathf.Clamp(Tanh(s.profile.drive * Raw(s, rpmValue, loadValue, rpmRelative, false, loadValue) * s.profile.gain) / n, -1f, 1f);
            return d;
        }

        // ------------------------------------------------------------------ voix

        static Profile Build(EngineVoice voice)
        {
            switch (voice)
            {
                case EngineVoice.V8:
                    // Gros V8 (Boss 429) à vilebrequin croisé, ordre 1-5-4-2-6-3-7-8 : rangées G D G G D G D D
                    // → combustions inégales par rangée, d'où le borborygme à bas régime
                    return new Profile
                    {
                        voice = voice,
                        fireAt = Even(8, new[] { 0f, 0.005f, -0.004f, 0.003f, 0f, -0.005f, 0.004f, -0.003f }),
                        amp = new[] { 1f, 0.68f, 1.08f, 0.94f, 0.75f, 0.98f, 0.7f, 0.64f },
                        modes = new[] { new Vector3(52f, 13f, 1f), new Vector3(150f, 8f, 0.55f), new Vector3(700f, 3f, 0.2f) },
                        thump = 0.9f, thumpMs = 1.1f, grit = 0.08f, crackle = 0.7f, drive = 2f,
                    };
                case EngineVoice.V12:
                    // V12 Cosworth 6,5 L atmosphérique (Valkyrie) : 12 allumages réguliers par cycle, qui montent à
                    // 11 000 tr/min → hurlement aigu et lisse de F1 des années 90, peu de grave, beaucoup d'harmoniques
                    return new Profile
                    {
                        voice = voice,
                        fireAt = Even(12, null),
                        amp = new[] { 1f, 0.97f, 1.01f, 0.96f, 1f, 0.98f, 1.02f, 0.97f, 0.99f, 0.96f, 1.01f, 0.98f },
                        modes = new[] { new Vector3(160f, 7f, 0.55f), new Vector3(520f, 5f, 0.8f), new Vector3(1900f, 2f, 0.42f) },
                        thump = 0.55f, thumpMs = 0.45f, grit = 0.08f, gritHz = 3200f, crackle = 0.35f, drive = 2.1f,
                    };
                case EngineVoice.FlatPlaneV8Turbo:
                    // V8 biturbo à vilebrequin plat (Koenigsegg) : chaque rangée allume comme un 4 cylindres, allumages
                    // réguliers → cri aigu et métallique qui monte très haut, sans le borborygme du V8 américain ; turbos
                    return new Profile
                    {
                        voice = voice,
                        fireAt = Even(8, null),
                        amp = new[] { 1f, 0.93f, 1.02f, 0.95f, 0.99f, 0.92f, 1.01f, 0.94f },
                        modes = new[] { new Vector3(118f, 9f, 0.9f), new Vector3(390f, 5.5f, 0.7f), new Vector3(1450f, 2.2f, 0.32f) },
                        thump = 0.8f, thumpMs = 0.6f, grit = 0.1f, gritHz = 2500f, turbo = 0.8f, crackle = 0.6f, drive = 1.9f,
                    };
                case EngineVoice.V6:
                    // V6 PRV 2,85 L atmosphérique (DeLorean) : V à 90° sans manetons décalés → allumages inégaux
                    // (90° / 150° de vilebrequin), d'où ce son rauque et creux ; rangées G D alternées
                    return new Profile
                    {
                        voice = voice,
                        fireAt = new[] { 0f, 0.125f, 0.3333f, 0.4583f, 0.6667f, 0.7917f },
                        amp = new[] { 1f, 0.84f, 1.05f, 0.88f, 0.97f, 0.82f },
                        modes = new[] { new Vector3(74f, 12f, 1f), new Vector3(205f, 7f, 0.62f), new Vector3(820f, 2.8f, 0.24f) },
                        thump = 0.85f, thumpMs = 0.95f, grit = 0.11f, gritHz = 1300f, crackle = 0.4f, drive = 1.85f,
                    };
                case EngineVoice.Inline4:
                    // 4 cylindres atmosphérique : plus aigu et plus sec, qui monte haut dans les tours
                    return new Profile
                    {
                        voice = voice,
                        fireAt = Even(4, null),
                        amp = new[] { 1f, 0.95f, 1.03f, 0.94f },
                        modes = new[] { new Vector3(135f, 10f, 0.8f), new Vector3(430f, 6f, 0.7f), new Vector3(1500f, 2.5f, 0.28f) },
                        thump = 0.9f, thumpMs = 0.6f, grit = 0.14f, gritHz = 2600f, crackle = 0.5f, drive = 1.9f,
                    };
                case EngineVoice.TwoStroke:
                    // Monocylindre 2-temps 49 cm³ (Peel P50) : une explosion par tour, son nasillard et rageur
                    // de mobylette (pot de détente), pétarades au ralenti
                    return new Profile
                    {
                        voice = voice,
                        fireAt = new[] { 0f, 0.5f },
                        amp = new[] { 1f, 0.9f },
                        modes = new[] { new Vector3(210f, 6f, 0.55f), new Vector3(950f, 3f, 0.8f), new Vector3(2700f, 1.5f, 0.4f) },
                        thump = 0.45f, thumpMs = 0.5f, grit = 0.22f, gritHz = 3600f, crackle = 0.45f, drive = 2.5f,
                    };
                case EngineVoice.Electric:
                    // Hydrogène/électrique : pas de combustion, une voix de synthèse grave qui suit le régime
                    return new Profile
                    {
                        voice = voice,
                        fireAt = Even(6, null),
                        amp = new[] { 1f, 1f, 1f, 1f, 1f, 1f },
                        modes = new[] { new Vector3(110f, 8f, 0.12f), new Vector3(330f, 4f, 0.05f), new Vector3(990f, 2f, 0.02f) },
                        thump = 0.1f, grit = 0f, synth = 1f, crackle = 0f, drive = 1.3f,
                    };
                default:
                    // 6 cylindres en ligne biturbo (2JZ) : régulier, plein, sifflement de turbine
                    return new Profile
                    {
                        voice = EngineVoice.Inline6Turbo,
                        fireAt = Even(6, null),
                        amp = new[] { 1f, 0.98f, 1.01f, 0.99f, 1f, 0.97f },
                        modes = new[] { new Vector3(98f, 14f, 1f), new Vector3(280f, 7f, 0.55f), new Vector3(1150f, 2.5f, 0.18f) },
                        thump = 0.8f, thumpMs = 0.8f, grit = 0.08f, gritHz = 1800f, turbo = 1f, crackle = 0.3f, drive = 1.6f,
                    };
            }
        }

        static float[] Even(int n, float[] jitter)
        {
            var f = new float[n];
            for (int i = 0; i < n; i++) f[i] = i / (float)n + (jitter != null ? jitter[i] : 0f);
            return f;
        }

        static State Create(EngineVoice voice, int rate)
        {
            var p = Build(voice);
            var s = NewState(p, rate);
            p.gain = 1f;
            // Normalisation : même niveau pour toutes les voix (0,4 s à mi-régime, pleine charge)
            var probe = NewState(p, rate);
            int n = rate * 2 / 5;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                float x = Raw(probe, 4200f, 1f, 0.55f, false, 1f);
                if (i > n / 4) sum += x * x;
            }
            float rms = Mathf.Sqrt((float)(sum / (n * 0.75)));
            p.gain = rms > 1e-5f ? 0.3f / rms : 1f;
            return s;
        }

        static State NewState(Profile p, int rate)
        {
            int m = p.modes.Length;
            var s = new State
            {
                profile = p, rate = rate,
                c1 = new float[m], c2 = new float[m], norm = new float[m], y1 = new float[m], y2 = new float[m],
                burstK = Mathf.Exp(-1f / (0.004f * rate)),
                thumpK = Mathf.Exp(-1f / (p.thumpMs * 0.001f * rate)),
                thumpA = 1f - Mathf.Exp(-2f * Mathf.PI * 700f / rate),
                gritA = 1f - Mathf.Exp(-2f * Mathf.PI * p.gritHz / rate),
                bovK = Mathf.Exp(-1f / (0.3f * rate)),
                nextFire = p.fireAt[0],
            };
            for (int k = 0; k < m; k++)
            {
                // Résonateur à deux pôles : une impulsion donne une sinusoïde amortie d'amplitude 1
                float w = 2f * Mathf.PI * Mathf.Min(p.modes[k].x, rate * 0.45f) / rate;
                float r = Mathf.Exp(-1f / (p.modes[k].y * 0.001f * rate));
                s.c1[k] = 2f * r * Mathf.Cos(w);
                s.c2[k] = -r * r;
                s.norm[k] = Mathf.Sin(w);
            }
            return s;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var s = state;
            if (s == null) { System.Array.Clear(data, 0, data.Length); return; }
            var p = s.profile;
            float r = rpm, l = load, rel = rpm01, vol = volume, b = boost;
            bool lim = limiter;
            if (bovTrigger != bovSeen) { bovSeen = bovTrigger; s.bov = Mathf.Max(s.bov, b); }
            float n = Tanh(p.drive);
            for (int i = 0; i < data.Length; i += channels)
            {
                float y = Mathf.Clamp(Tanh(p.drive * Raw(s, r, l, rel, lim, b) * p.gain) / n, -1f, 1f) * vol;
                for (int c = 0; c < channels; c++) data[i + c] = y;
            }
            echo?.Process(data, channels, echoTarget);
        }

        static float Tanh(float x) => (float)System.Math.Tanh(x);

        /// <summary>Un échantillon de moteur (avant saturation).</summary>
        static float Raw(State s, float r, float l, float rel, bool lim, float b)
        {
            var p = s.profile;
            // Avance du vilebrequin : un cycle = 2 tours
            s.phase += r / 120.0 / s.rate;
            while (s.phase >= s.nextFire)
            {
                Fire(s, s.nextCyl, l, rel, lim);
                s.nextCyl++;
                if (s.nextCyl >= p.fireAt.Length) { s.nextCyl = 0; s.cycleBase += 1.0; }
                s.nextFire = s.cycleBase + p.fireAt[s.nextCyl];
            }
            float exc = s.impulse;
            s.impulse = 0f;

            // Bosse de pression (exponentielle adoucie par un second filtre : pas de clic aigu)
            s.thump = s.thump * s.thumpK + exc;
            s.thump2 += (s.thump - s.thump2) * s.thumpA;
            float sum = s.thump2 * p.thump * 0.5f;
            float bright = Mathf.Lerp(0.3f, 1f, l);
            for (int k = 0; k < p.modes.Length; k++)
            {
                float y = s.c1[k] * s.y1[k] + s.c2[k] * s.y2[k] + exc * s.norm[k];
                s.y2[k] = s.y1[k];
                s.y1[k] = y;
                sum += y * p.modes[k].z * (k == p.modes.Length - 1 ? bright : 1f) * 0.3f;
            }

            // Grain : bruit sourd (filtré) juste après chaque combustion
            float white = (float)s.rng.NextDouble() * 2f - 1f;
            s.noiseLp += (white - s.noiseLp) * s.gritA;
            sum += s.noiseLp * s.burstEnv * p.grit * 3f;
            s.burstEnv *= s.burstK;

            if (p.synth > 0f)
            {
                // Voix de synthèse : dents de scie à bande limitée (6 harmoniques) + sous-octave, filtrées selon la charge
                float f = r / 60f * 1.5f;
                s.synthPhase = (s.synthPhase + 2f * Mathf.PI * f / s.rate) % (4f * Mathf.PI);
                float saw = 0f;
                int harmonics = l > 0.5f ? 6 : 4;
                for (int h = 1; h <= harmonics; h++) saw += Mathf.Sin(s.synthPhase * h) / h;
                float sub = Mathf.Sin(s.synthPhase * 0.5f);
                float v = saw * 0.5f + sub * 0.6f;
                s.bassLp += (v - s.bassLp) * Mathf.Lerp(0.08f, 0.3f, l);
                sum += s.bassLp * p.synth * (0.35f + 0.65f * l) * (0.5f + 0.5f * rel);
            }
            if (p.turbo > 0f)
            {
                // Sifflement de turbine (discret) et soupape de décharge
                float f = 2400f + 4200f * b;
                s.turboPhase = (s.turboPhase + 2f * Mathf.PI * f / s.rate) % (2f * Mathf.PI);
                sum += Mathf.Sin(s.turboPhase) * 0.02f * b * b * p.turbo;
                if (s.bov > 0.001f)
                {
                    sum += (white - s.noiseLp) * s.bov * 0.25f;
                    s.bov *= s.bovK;
                }
            }

            // Filtre anti-continu
            float o = sum - s.dcX + 0.995f * s.dcY;
            s.dcX = sum;
            s.dcY = o;
            return o;
        }

        /// <summary>
        /// Écho d'un tube de tunnel : six lignes à retard rebouclées (trois par oreille, durées premières entre elles pour
        /// ne pas sonner métallique), étouffées dans les aigus à chaque retour comme sur du béton. Le son qui entre suit
        /// <c>amount</c> ; les lignes continuent de s'éteindre seules à la sortie du tunnel.
        /// </summary>
        public sealed class TunnelEcho
        {
            static readonly float[] DelaysMs = { 67f, 113f, 191f, 73f, 127f, 211f };
            static readonly float[] Feedback = { 0.46f, 0.40f, 0.34f, 0.44f, 0.39f, 0.33f };
            const float Wet = 0.62f;
            readonly float[][] lines = new float[6][];
            readonly int[] pos = new int[6];
            readonly float[] damp = new float[6];
            readonly float dampK, inputK, smooth;
            float input, send;

            public TunnelEcho(int rate)
            {
                for (int i = 0; i < 6; i++) lines[i] = new float[Mathf.Max(1, Mathf.RoundToInt(DelaysMs[i] * 0.001f * rate))];
                dampK = 1f - Mathf.Exp(-2f * Mathf.PI * 2600f / rate);
                inputK = 1f - Mathf.Exp(-2f * Mathf.PI * 3800f / rate);
                smooth = 1f - Mathf.Exp(-1f / (0.08f * rate));
            }

            public void Process(float[] data, int channels, float amount)
            {
                for (int i = 0; i < data.Length; i += channels)
                {
                    send += (amount - send) * smooth;
                    float dryL = data[i], dryR = channels > 1 ? data[i + 1] : dryL;
                    input += ((dryL + dryR) * 0.5f - input) * inputK;
                    float l = 0f, r = 0f;
                    for (int k = 0; k < 6; k++)
                    {
                        var line = lines[k];
                        float o = line[pos[k]];
                        damp[k] += (o - damp[k]) * dampK;
                        line[pos[k]] = input * send + damp[k] * Feedback[k];
                        if (++pos[k] >= line.Length) pos[k] = 0;
                        if (k < 3) l += o; else r += o;
                    }
                    data[i] = Mathf.Clamp(dryL + l * Wet / 1.6f, -1f, 1f);
                    if (channels > 1) data[i + 1] = Mathf.Clamp(dryR + r * Wet / 1.6f, -1f, 1f);
                    for (int c = 2; c < channels; c++) data[i + c] = data[i];
                }
            }
        }

        static void Fire(State s, int cyl, float l, float rel, bool lim)
        {
            var p = s.profile;
            // Rupteur : une combustion sur deux est coupée → bégaiement
            if (lim && (cyl & 1) == 0) return;
            float a = p.amp[cyl] * Mathf.Lerp(0.35f, 1f, l);
            // Lever de pied à haut régime : pétarades ponctuelles
            if (l < 0.15f && rel > 0.45f && s.rng.NextDouble() < p.crackle * 0.05)
            {
                a *= 3f;
                s.burstEnv = Mathf.Max(s.burstEnv, 2f);
            }
            s.impulse += a;
            s.burstEnv = Mathf.Max(s.burstEnv, a);
        }
    }
}
