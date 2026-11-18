using System;

namespace ChillRoad
{
    /// <summary>
    /// Sons du jeu synthétisés une fois au lancement (aucun fichier) : vent, crissement, chocs, frôlement, retours de
    /// flamme, rapports, notes du score, tonnerre. Tampons stéréo entrelacés (gauche, droite) à <see cref="Rate"/> Hz,
    /// ou à <see cref="HalfRate"/> pour les sons graves (vent de fond, tonnerre). Les boucles sont périodiques par
    /// construction : aucun raccord. La pluie est dans <see cref="RainSynth"/>.
    /// </summary>
    public static class SfxSynth
    {
        public const int Rate = 44100;
        public const int HalfRate = Rate / 2;
        const float Tau = 2f * MathF.PI;
        const int Slow = 7;     // les ondulations lentes sont recalculées toutes les 8 images

        // ------------------------------------------------------------------ boucles

        /// <summary>Vent, le fond (à <see cref="HalfRate"/>) : l'air qui gronde et bat contre la carrosserie.</summary>
        public static float[] WindBody()
        {
            int n = (int)(8.3f * Rate) / 2 * 2;
            var d = new float[n * 2];
            var rng = new Random(11);
            float[] common = White(n, rng);
            for (int ch = 0; ch < 2; ch++)
            {
                float[] own = White(n, rng);
                var deep = new Band(35f, 150f);
                var body = new Band(150f, 560f);
                float p1 = Phase(rng), p2 = Phase(rng), p3 = Phase(rng), gust = 1f, flutter = 1f;
                // Un instant à vide sur la fin de la boucle : les filtres y entrent déjà établis
                for (int i = -Rate / 4; i < n; i++)
                {
                    int k = i < 0 ? i + n : i;
                    float x = common[k] * 0.7f + own[k] * 0.71f;
                    if ((k & Slow) == 0)
                    {
                        // Nombres entiers de cycles par boucle : les rafales se rejoignent
                        float t = k / (float)n;
                        gust = 1f + 0.22f * MathF.Sin(Tau * 2f * t + p1) + 0.12f * MathF.Sin(Tau * 5f * t + p2);
                        flutter = 1f + 0.16f * MathF.Sin(Tau * 9f * t + p3) + 0.08f * MathF.Sin(Tau * 17f * t + p1);
                    }
                    float y = deep.Run(x) * 1.5f * gust + body.Run(x) * flutter;
                    if (i >= 0) d[k * 2 + ch] = y;
                }
            }
            Normalize(d, 0.9f);
            return Halve(d, true);
        }

        /// <summary>Vent, l'air : le souffle clair de la vitesse et son sifflement qui se promène.</summary>
        public static float[] WindAir()
        {
            int n = (int)(6.7f * Rate);
            var d = new float[n * 2];
            var rng = new Random(12);
            float[] common = White(n, rng);
            for (int ch = 0; ch < 2; ch++)
            {
                float[] own = White(n, rng);
                var hiss = new Band(480f, 2600f);
                float low = 0f, band = 0f, swell = 1f, f = 0.1f;
                float p1 = Phase(rng), p2 = Phase(rng), p3 = Phase(rng);
                for (int i = -Rate / 8; i < n; i++)
                {
                    int k = i < 0 ? i + n : i;
                    float x = common[k] * 0.5f + own[k] * 0.87f;
                    if ((k & Slow) == 0)
                    {
                        float t = k / (float)n;
                        swell = 1f + 0.2f * MathF.Sin(Tau * 3f * t + p1) + 0.1f * MathF.Sin(Tau * 8f * t + p2);
                        // Sifflement : résonance étroite dont la hauteur ondule
                        float fc = 1150f * MathF.Pow(2f, 0.35f * MathF.Sin(Tau * 2f * t + p3) + 0.18f * MathF.Sin(Tau * 7f * t + p1));
                        f = 2f * MathF.Sin(MathF.PI * fc / Rate);
                    }
                    low += f * band;
                    float high = x - low - 0.3f * band;
                    band += f * high;
                    float y = hiss.Run(x) * swell + band * 0.09f * (2f - swell);
                    if (i >= 0) d[k * 2 + ch] = y;
                }
            }
            Normalize(d, 0.9f);
            return d;
        }

        /// <summary>
        /// Crissement : deux pneus qui chantent, chacun sa note et ses harmoniques, hauteur qui tremble, grain rugueux
        /// du caoutchouc qui accroche et décroche, et le frottement sur le bitume.
        /// </summary>
        public static float[] Skid()
        {
            int n = (int)(3.7f * Rate);
            float seconds = n / (float)Rate;
            var d = new float[n * 2];
            var rng = new Random(13);
            float[] notes = { 836f, 689f };
            float[] level = { 1f, 0.62f };
            for (int v = 0; v < 2; v++)
            {
                // Nombre entier de périodes sur la boucle, écarts de hauteur qui s'annulent : aucun raccord
                float f0 = MathF.Round(notes[v] * seconds) / seconds;
                float[] rough = LoopNoise(n, rng, 38f);
                float p1 = Phase(rng), p2 = Phase(rng), p3 = Phase(rng);
                float pan = v == 0 ? 0.36f : 0.66f;
                float left = MathF.Cos(pan * Tau * 0.25f), right = MathF.Sin(pan * Tau * 0.25f);
                double phase = rng.NextDouble();
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n;
                    float f = f0 * (1f + 0.022f * MathF.Sin(Tau * 3f * t + p1) + 0.013f * MathF.Sin(Tau * 11f * t + p2) + 0.006f * MathF.Sin(Tau * 29f * t + p3));
                    phase += f / Rate;
                    phase -= Math.Floor(phase);
                    float a = Tau * (float)phase;
                    float grain = 0.55f + 0.45f * rough[k];
                    float y = (MathF.Sin(a) + 0.5f * MathF.Sin(2f * a) + 0.3f * MathF.Sin(3f * a) + 0.16f * MathF.Sin(4f * a) + 0.08f * MathF.Sin(5f * a))
                        * grain * grain * level[v] * 0.5f;
                    d[k * 2] += y * left;
                    d[k * 2 + 1] += y * right;
                }
            }
            for (int ch = 0; ch < 2; ch++)
            {
                float[] own = White(n, rng);
                float[] rough = LoopNoise(n, rng, 55f);
                var scrub = new Band(350f, 2800f);
                for (int i = -Rate / 10; i < n; i++)
                {
                    int k = i < 0 ? i + n : i;
                    float y = scrub.Run(own[k]) * (0.6f + 0.4f * rough[k]) * 0.75f;
                    if (i >= 0) d[k * 2 + ch] += y;
                }
            }
            SoftClip(d, 1.4f);
            Normalize(d, 0.5f);
            return d;
        }

        // ------------------------------------------------------------------ sons ponctuels

        /// <summary>Frôlement : l'air déchiré par la voiture qu'on croise, qui monte puis retombe.</summary>
        public static float[] Whoosh()
        {
            int n = (int)(0.6f * Rate);
            var d = new float[n * 2];
            var rng = new Random(51);
            float[] common = White(n, rng);
            for (int ch = 0; ch < 2; ch++)
            {
                float[] own = White(n, rng);
                var thump = new Band(60f, 240f);
                float low = 0f, band = 0f;
                for (int k = 0; k < n; k++)
                {
                    float x = common[k] * 0.55f + own[k] * 0.83f;
                    float u = MathF.Pow(k / (float)n, 0.7f);                 // montée vive, retombée plus longue
                    float env = MathF.Pow(MathF.Sin(MathF.PI * u), 2.2f);
                    float fc = u < 0.5f ? Lerp(420f, 1750f, u * 2f) : Lerp(1750f, 360f, (u - 0.5f) * 2f);
                    float f = 2f * MathF.Sin(MathF.PI * fc / Rate);
                    low += f * band;
                    float high = x - low - 0.7f * band;
                    band += f * high;
                    d[k * 2 + ch] = band * env * 0.7f + thump.Run(x) * env * env * 1.6f;
                }
            }
            SoftClip(d, 1.7f);
            Normalize(d, 0.95f);
            FadeOut(d, 0.02f);
            return d;
        }

        /// <summary>
        /// Choc : coup sourd, tôle qui encaisse et résonne ; violent, la carrosserie se froisse et des éclats retombent.
        /// </summary>
        public static float[] Crash(bool hard)
        {
            var d = new float[(int)((hard ? 0.95f : 0.5f) * Rate) * 2];
            var rng = new Random(hard ? 41 : 43);
            Tone(d, 0f, hard ? 135f : 155f, hard ? 46f : 64f, 0.03f, hard ? 0.085f : 0.05f, 1f, 0.5f, 0.001f);
            Noise(d, rng, 0f, 200f, hard ? 6000f : 4200f, hard ? 0.06f : 0.04f, hard ? 1.5f : 1.1f, 0.5f, 0.0004f, 0.5f);
            Noise(d, rng, 0.004f, 90f, 700f, hard ? 0.16f : 0.09f, 0.6f, 0.5f, 0.002f, 0.6f);
            float[] modes = { 287f, 463f, 811f, 1290f, 2130f };
            for (int i = 0; i < modes.Length; i++)
                Tone(d, 0.001f * i, modes[i], modes[i], 1f, (hard ? 0.09f : 0.05f) / (1f + i * 0.5f), 0.24f / (1f + i * 0.3f), 0.2f + 0.15f * i, 0.0006f);
            if (hard)
            {
                for (int i = 0; i < 9; i++)
                {
                    float at = 0.02f + 0.3f * MathF.Pow(Next(rng), 1.6f);
                    Noise(d, rng, at, 300f + 900f * Next(rng), 2500f + 3500f * Next(rng), 0.008f + 0.02f * Next(rng),
                        0.8f * (1f - at / 0.4f), 0.15f + 0.7f * Next(rng), 0.0005f, 0.3f);
                }
                for (int i = 0; i < 26; i++)
                {
                    float at = 0.07f + 0.6f * MathF.Pow(Next(rng), 1.8f);
                    Tick(d, at, 3200f + 4800f * Next(rng), 0.0012f + 0.003f * Next(rng), 0.26f * (1f - at / 0.8f) * (0.4f + 0.6f * Next(rng)), Next(rng));
                }
            }
            d = Reverb(d, hard ? 0.16f : 0.1f, hard ? 0.5f : 0.3f, hard ? 0.25f : 0.15f);
            SoftClip(d, 1.6f);
            Normalize(d, 0.95f);
            FadeOut(d, 0.04f);
            return d;
        }

        /// <summary>
        /// Retour de flamme : claquement sec, coup de pression grave dans la ligne d'échappement, souffle, et son écho
        /// sur la route. Trois variantes (0-2) ; la deuxième est doublée.
        /// </summary>
        public static float[] Pop(int variant)
        {
            var d = new float[(int)(0.3f * Rate) * 2];
            var rng = new Random(61 + variant);
            float body = 92f + 14f * variant;
            Noise(d, rng, 0f, 900f, 9000f, 0.007f, 1.6f, 0.5f, 0.0002f, 0.15f);
            Tone(d, 0f, body * 2.2f, body, 0.006f, 0.022f, 0.9f, 0.5f, 0.0006f);
            Tone(d, 0.0005f, 262f + 30f * variant, 240f + 30f * variant, 0.01f, 0.011f, 0.5f, 0.5f, 0.0005f);
            Noise(d, rng, 0.001f, 200f, 4500f, 0.024f, 1.1f, 0.5f, 0.001f, 0.3f);
            float echo = variant == 1 ? 0.6f : 0.22f, echoAt = 0.045f + 0.008f * variant;
            Noise(d, rng, echoAt, 200f, 4000f, 0.016f, echo, 0.5f, 0.001f, 0.5f);
            Tone(d, echoAt, body * 1.6f, body, 0.006f, 0.018f, echo * 0.6f, 0.5f, 0.0006f);
            SoftClip(d, 2f);
            Normalize(d, 0.95f);
            FadeOut(d, 0.02f);
            return d;
        }

        /// <summary>Changement de rapport : la tringlerie se déverrouille, bute, et le rapport s'enclenche.</summary>
        public static float[] Shift()
        {
            var d = new float[(int)(0.16f * Rate) * 2];
            var rng = new Random(71);
            Noise(d, rng, 0f, 900f, 4200f, 0.0025f, 0.5f, 0.5f, 0.0003f, 0.2f);
            Tone(d, 0.001f, 210f, 135f, 0.008f, 0.014f, 1f, 0.5f, 0.001f);
            Noise(d, rng, 0.03f, 600f, 3000f, 0.003f, 0.32f, 0.5f, 0.0003f, 0.2f);
            Tone(d, 0.031f, 320f, 250f, 0.006f, 0.008f, 0.35f, 0.5f, 0.001f);
            Normalize(d, 0.9f);
            FadeOut(d, 0.02f);
            return d;
        }

        /// <summary>Petite note claire (bonus de vitesse), qui s'éteint d'elle-même.</summary>
        public static float[] Blip()
        {
            var d = new float[(int)(0.3f * Rate) * 2];
            Tone(d, 0f, 980f, 980f, 1f, 0.05f, 1f, 0.5f, 0.004f, 0.25f, 0.08f);
            d = Reverb(d, 0.14f, 0.3f, 0.12f);
            Normalize(d, 0.35f);
            FadeOut(d, 0.03f);
            return d;
        }

        /// <summary>Flash d'un radar : deux déclics secs et brillants, coup sur coup.</summary>
        public static float[] Flash()
        {
            var d = new float[(int)(0.24f * Rate) * 2];
            var rng = new Random(81);
            for (int i = 0; i < 2; i++)
            {
                float at = i * 0.085f, amp = i == 0 ? 1f : 0.8f;
                Noise(d, rng, at, 1800f, 9000f, 0.004f, 0.7f * amp, 0.5f, 0.0002f, 0.3f);
                Tone(d, at, 2200f, 2200f, 1f, 0.03f, 0.8f * amp, 0.5f, 0.0008f, 0.3f);
            }
            d = Reverb(d, 0.1f, 0.25f, 0.1f);
            Normalize(d, 0.9f);
            FadeOut(d, 0.03f);
            return d;
        }

        /// <summary>Gain encaissé : arpège montant (do, mi, sol, do) de lames frappées, dans une petite salle.</summary>
        public static float[] Bonus()
        {
            var d = new float[(int)(0.9f * Rate) * 2];
            float[] notes = { 523.25f, 659.26f, 783.99f, 1046.5f };
            for (int i = 0; i < notes.Length; i++)
            {
                bool last = i == notes.Length - 1;
                float at = i * 0.07f, pan = 0.5f + (i % 2 == 0 ? -0.16f : 0.16f), tau = last ? 0.26f : 0.15f;
                Tone(d, at, notes[i], notes[i], 1f, tau, last ? 1f : 0.8f, pan, 0.003f, 0.3f, 0.1f);
                // Le « ting » de la frappe : partiel aigu inharmonique, très bref
                Tone(d, at, notes[i] * 4.07f, notes[i] * 4.07f, 1f, tau * 0.2f, 0.1f, pan, 0.001f);
            }
            d = Reverb(d, 0.2f, 0.7f, 0.45f);
            Normalize(d, 0.63f);
            FadeOut(d, 0.1f);
            return d;
        }

        /// <summary>Score perdu : trois notes qui descendent (mi, si, sol), sourdes, chacune s'affaisse un peu.</summary>
        public static float[] Lose()
        {
            var d = new float[(int)(0.95f * Rate) * 2];
            float[] notes = { 329.63f, 246.94f, 196f };
            for (int i = 0; i < notes.Length; i++)
            {
                bool last = i == notes.Length - 1;
                Tone(d, i * 0.12f, notes[i] * 1.03f, notes[i] * 0.985f, 0.08f, last ? 0.28f : 0.17f, last ? 1f : 0.85f,
                    0.5f + (i - 1) * 0.12f, 0.006f, 0.45f, 0.2f);
            }
            d = Reverb(d, 0.18f, 0.6f, 0.4f);
            Normalize(d, 0.46f);
            FadeOut(d, 0.1f);
            return d;
        }

        /// <summary>
        /// Tonnerre (à <see cref="HalfRate"/>) : chaque tronçon de l'éclair arrive à son tour, de plus loin et plus
        /// sourd, d'où un grondement qui roule d'un côté à l'autre et s'éteint en quelques secondes. Proche : l'air se
        /// déchire, claque, puis un coup de canon grave.
        /// </summary>
        public static float[] Thunder(int seed, bool close)
        {
            float seconds = close ? 6.5f : 8f;
            int n = (int)(seconds * Rate) / 2 * 2;
            var d = new float[n * 2];
            var rng = new Random(seed);
            int count = close ? 7 : 9;
            var at = new float[count];
            var amp = new float[count];
            var rise = new float[count];
            var fall = new float[count];
            var pan = new float[count];
            for (int k = 0; k < count; k++)
            {
                at[k] = k == 0 ? (close ? 0.02f : 0.15f) : (close ? 0.12f : 0.3f) + seconds * 0.55f * MathF.Pow(Next(rng), 1.5f);
                amp[k] = k == 0 ? 1f : MathF.Max(0.1f, (0.3f + 0.6f * Next(rng)) * (1f - at[k] / (seconds * 0.75f)));
                rise[k] = close ? (k == 0 ? 0.004f : 0.03f + 0.05f * Next(rng)) : 0.12f + 0.25f * Next(rng);
                fall[k] = 0.35f + 0.8f * Next(rng);
                pan[k] = k == 0 ? 0.5f : 0.5f + (Next(rng) - 0.5f) * 0.8f;
            }
            // Enveloppe des coups, calculée toutes les 64 images puis interpolée
            const int step = 64;
            int points = n / step + 2;
            float[] common = White(n, rng);
            for (int ch = 0; ch < 2; ch++)
            {
                var env = new float[points];
                for (int j = 0; j < points; j++)
                {
                    float t = j * step / (float)Rate, sum = 0f;
                    for (int k = 0; k < count; k++)
                    {
                        float u = t - at[k];
                        if (u <= 0f) continue;
                        float side = ch == 0 ? MathF.Cos(pan[k] * Tau * 0.25f) : MathF.Sin(pan[k] * Tau * 0.25f);
                        sum += amp[k] * side * MathF.Min(1f, u / rise[k]) * MathF.Exp(-u / fall[k]);
                    }
                    env[j] = sum;
                }
                float[] own = White(n, rng);
                var rumble = new Band(24f, 115f);
                var roll = new Band(115f, 620f);
                float bright = close ? 0.8f : 0.5f, dull = MathF.Exp(-1f / ((close ? 1.6f : 1.1f) * Rate));
                for (int i = 0; i < n; i++)
                {
                    float x = common[i] * 0.6f + own[i] * 0.8f;
                    int j = i / step;
                    float e = Lerp(env[j], env[j + 1], (i - j * step) / (float)step);
                    // Les aigus se perdent avec la distance : le roulement s'assourdit au fil des secondes
                    d[i * 2 + ch] = (rumble.Run(x) * 2.2f + roll.Run(x) * bright) * e;
                    bright *= dull;
                }
            }
            if (close)
            {
                Normalize(d, 0.55f);
                for (int j = 0; j < 3; j++)
                    Noise(d, rng, 0.03f * j * Next(rng), 900f, 7000f, 0.006f + 0.01f * Next(rng), 0.22f, 0.2f + 0.6f * Next(rng), 0.0003f, 0.6f);
                Noise(d, rng, 0.02f, 500f, 6000f, 0.045f, 0.8f, 0.5f, 0.0005f, 0.5f);
                Tone(d, 0.02f, 95f, 38f, 0.08f, 0.35f, 0.7f, 0.5f, 0.003f);
            }
            SoftClip(d, 1.2f);
            Normalize(d, close ? 0.85f : 0.75f);
            FadeOut(d, 0.8f);
            return Halve(d, false);
        }

        // ------------------------------------------------------------------ briques

        /// <summary>Note : sinusoïde (et deux harmoniques qui s'éteignent plus vite) dont la hauteur glisse de f0 à f1.</summary>
        static void Tone(float[] d, float at, float f0, float f1, float glide, float tau, float amp, float pan, float attack,
            float second = 0f, float third = 0f)
        {
            int n = d.Length / 2, start = (int)(at * Rate), len = (int)(tau * Rate * 7f);
            float left = MathF.Cos(pan * Tau * 0.25f) * amp, right = MathF.Sin(pan * Tau * 0.25f) * amp;
            double phase = 0.0;
            for (int k = 0; k < len && start + k < n; k++)
            {
                float t = k / (float)Rate;
                phase += (f1 + (f0 - f1) * MathF.Exp(-t / glide)) / Rate;
                phase -= Math.Floor(phase);
                float a = Tau * (float)phase, decay = MathF.Exp(-t / tau);
                float y = (MathF.Sin(a) + second * MathF.Sin(2f * a) * decay + third * MathF.Sin(3f * a) * decay * decay * decay)
                    * MathF.Min(1f, t / attack) * decay;
                d[(start + k) * 2] += y * left;
                d[(start + k) * 2 + 1] += y * right;
            }
        }

        /// <summary>Bouffée de bruit entre deux fréquences, qui s'éteint ; spread : écart entre les deux oreilles (0-1).</summary>
        static void Noise(float[] d, Random rng, float at, float low, float high, float tau, float amp, float pan, float attack, float spread)
        {
            int n = d.Length / 2, start = (int)(at * Rate), len = (int)(tau * Rate * 7f);
            float left = MathF.Cos(pan * Tau * 0.25f) * amp, right = MathF.Sin(pan * Tau * 0.25f) * amp;
            var bandL = new Band(low, high);
            var bandR = new Band(low, high);
            for (int k = 0; k < len && start + k < n; k++)
            {
                float t = k / (float)Rate;
                float c = Next(rng) * 2f - 1f, a = Next(rng) * 2f - 1f, b = Next(rng) * 2f - 1f;
                float env = MathF.Min(1f, t / attack) * MathF.Exp(-t / tau) * 2.5f;
                d[(start + k) * 2] += bandL.Run(c * (1f - spread) + a * spread) * env * left;
                d[(start + k) * 2 + 1] += bandR.Run(c * (1f - spread) + b * spread) * env * right;
            }
        }

        /// <summary>Petit éclat : résonance très amortie.</summary>
        static void Tick(float[] d, float at, float freq, float tau, float amp, float pan)
        {
            int n = d.Length / 2, start = (int)(at * Rate), len = (int)(tau * Rate * 7f);
            float w = Tau * freq / Rate, decay = MathF.Exp(-1f / (tau * Rate));
            float c = 2f * decay * MathF.Cos(w), rr = decay * decay;
            float y1 = 0f, y = amp * MathF.Sin(w);
            float left = MathF.Cos(pan * Tau * 0.25f), right = MathF.Sin(pan * Tau * 0.25f);
            for (int k = 0; k < len && start + k < n; k++)
            {
                d[(start + k) * 2] += y * left;
                d[(start + k) * 2 + 1] += y * right;
                float next = c * y - rr * y1;
                y1 = y;
                y = next;
            }
        }

        /// <summary>
        /// Petite salle (peignes amortis puis passe-tout), ajoutée au son sec ; le tampon s'allonge de « tail »
        /// secondes pour la laisser s'éteindre. decay : temps de réverbération.
        /// </summary>
        static float[] Reverb(float[] dry, float mix, float decay, float tail)
        {
            int n = dry.Length / 2, total = n + (int)(tail * Rate);
            var o = new float[total * 2];
            float[] combMs = { 29.7f, 37.1f, 41.1f, 43.7f };
            float[] passMs = { 5f, 1.7f };
            var wet = new float[total];
            var stage = new float[total];
            for (int ch = 0; ch < 2; ch++)
            {
                Array.Clear(wet, 0, total);
                for (int j = 0; j < combMs.Length; j++)
                {
                    // Retards un peu différents à gauche et à droite : la salle a de la largeur
                    var buf = new float[(int)((combMs[j] + ch * 0.77f * (j + 1)) * 0.001f * Rate)];
                    float g = MathF.Pow(10f, -3f * buf.Length / (decay * Rate)), damp = 0f;
                    int p = 0;
                    for (int i = 0; i < total; i++)
                    {
                        float x = i < n ? (dry[i * 2] + dry[i * 2 + 1]) * 0.35f + dry[i * 2 + ch] * 0.3f : 0f;
                        float y = buf[p];
                        damp += (y - damp) * 0.45f;          // les aigus s'éteignent plus vite
                        buf[p] = x + damp * g;
                        if (++p == buf.Length) p = 0;
                        wet[i] += y;
                    }
                }
                foreach (float ms in passMs)
                {
                    var buf = new float[(int)(ms * 0.001f * Rate)];
                    int p = 0;
                    for (int i = 0; i < total; i++)
                    {
                        float y = buf[p] - 0.5f * wet[i];
                        buf[p] = wet[i] + 0.5f * y;
                        if (++p == buf.Length) p = 0;
                        stage[i] = y;
                    }
                    (wet, stage) = (stage, wet);
                }
                for (int i = 0; i < total; i++) o[i * 2 + ch] = (i < n ? dry[i * 2 + ch] : 0f) + wet[i] * mix * 0.25f;
            }
            return o;
        }

        /// <summary>Filtre passe-bande : passe-haut à un pôle, passe-bas à deux pôles (les aigus tombent vite).</summary>
        struct Band
        {
            readonly float a, b;
            float lo, hi1, hi2;

            public Band(float low, float high)
            {
                a = 1f - MathF.Exp(-Tau * low / Rate);
                b = 1f - MathF.Exp(-Tau * high / Rate);
                lo = hi1 = hi2 = 0f;
            }

            public float Run(float x)
            {
                hi1 += (x - hi1) * b;
                hi2 += (hi1 - hi2) * b;
                lo += (hi2 - lo) * a;
                return hi2 - lo;
            }
        }

        /// <summary>Bruit lent (sous « cutoff » Hz) périodique sur n échantillons, entre -1 et 1 : grain, tremblement.</summary>
        static float[] LoopNoise(int n, Random rng, float cutoff)
        {
            float[] w = White(n, rng);
            var o = new float[n];
            float a = 1f - MathF.Exp(-Tau * cutoff / Rate), lp1 = 0f, lp2 = 0f, max = 1e-6f;
            for (int i = -Rate / 4; i < n; i++)
            {
                int k = i < 0 ? i + n : i;
                lp1 += (w[k] - lp1) * a;
                lp2 += (lp1 - lp2) * a;
                if (i >= 0) { o[k] = lp2; max = MathF.Max(max, MathF.Abs(lp2)); }
            }
            for (int i = 0; i < n; i++) o[i] /= max;
            return o;
        }

        /// <summary>Divise la fréquence d'échantillonnage par deux (son sans aigus) ; loop : le tampon est une boucle.</summary>
        static float[] Halve(float[] d, bool loop)
        {
            int n = d.Length / 2, half = n / 2;
            var o = new float[half * 2];
            for (int ch = 0; ch < 2; ch++)
                for (int j = 0; j < half; j++)
                {
                    int i = j * 2, before = i > 0 ? i - 1 : loop ? n - 1 : 0, after = i + 1 < n ? i + 1 : loop ? 0 : i;
                    o[j * 2 + ch] = d[before * 2 + ch] * 0.25f + d[i * 2 + ch] * 0.5f + d[after * 2 + ch] * 0.25f;
                }
            return o;
        }

        static float[] White(int n, Random rng)
        {
            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = (float)rng.NextDouble() * 2f - 1f;
            return w;
        }

        static void Normalize(float[] d, float peak)
        {
            float max = 1e-6f;
            foreach (float x in d) max = MathF.Max(max, MathF.Abs(x));
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        /// <summary>Écrêtage doux : arrondit les crêtes au lieu de les couper.</summary>
        static void SoftClip(float[] d, float drive)
        {
            float max = 1e-6f;
            foreach (float x in d) max = MathF.Max(max, MathF.Abs(x));
            float norm = MathF.Tanh(drive);
            for (int i = 0; i < d.Length; i++) d[i] = MathF.Tanh(d[i] / max * drive) / norm;
        }

        /// <summary>Fin en douceur : le tampon s'achève sur un silence, sans clic.</summary>
        static void FadeOut(float[] d, float seconds)
        {
            int n = d.Length / 2, len = Math.Min(n, (int)(seconds * Rate));
            for (int i = 0; i < len; i++)
            {
                float k = i / (float)len;
                d[(n - 1 - i) * 2] *= k;
                d[(n - 1 - i) * 2 + 1] *= k;
            }
        }

        static float Next(Random rng) => (float)rng.NextDouble();

        static float Phase(Random rng) => (float)rng.NextDouble() * Tau;

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
