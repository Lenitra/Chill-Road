using System;

namespace ChillRoad
{
    /// <summary>
    /// Bruit de la pluie, synthétisé en trois couches stéréo jouées en boucle, de longueurs différentes pour que
    /// l'ensemble ne se répète jamais à l'oreille : le rideau (souffle continu de l'averse, avec son grave), le
    /// crépitement (des centaines de petites gouttes par seconde) et les gouttes (rares, bien détachées, certaines
    /// tombent dans l'eau). <see cref="Mix"/> dose les couches selon l'intensité : gouttes isolées au début de
    /// l'averse, rideau dense au plus fort. Les boucles sont périodiques par construction : aucun raccord.
    /// </summary>
    public static class RainSynth
    {
        public const int Rate = 44100;
        const float Tau = 2f * MathF.PI;

        /// <summary>Volumes des couches pour une intensité de pluie (0-1).</summary>
        public static void Mix(float rain, out float wash, out float patter, out float drops)
        {
            float r = Math.Clamp(rain, 0f, 1f);
            wash = 0.4f * r * Smooth(0.05f, 0.5f, r);
            patter = 0.3f * Smooth(0.08f, 0.6f, r);
            drops = 0.4f * Smooth(0f, 0.2f, r) * (1f - 0.4f * r);
        }

        /// <summary>Rideau : bruit en trois bandes (grave, médium, aigu doux) qui ondulent lentement.</summary>
        public static float[] Wash()
        {
            int n = (int)(9.7f * Rate);
            var d = new float[n * 2];
            var rng = new Random(101);
            float[] common = White(n, rng);
            for (int ch = 0; ch < 2; ch++)
            {
                float[] own = White(n, rng);
                var low = new Band(90f, 320f);
                var mid = new Band(320f, 2200f);
                var high = new Band(2200f, 7000f);
                float air = 0f, airCoef = 1f - MathF.Exp(-Tau * 10000f / Rate); // pas de sifflement dans l'extrême aigu
                float p1 = (float)rng.NextDouble() * Tau, p2 = (float)rng.NextDouble() * Tau, p3 = (float)rng.NextDouble() * Tau;
                // Quelques millisecondes à vide : les filtres entrent dans la boucle déjà établis
                const int warm = Rate / 10;
                for (int i = -warm; i < n; i++)
                {
                    int k = i < 0 ? i + n : i;
                    float x = common[k] * 0.55f + own[k] * 0.83f;
                    float t = k / (float)n;
                    // Nombres entiers de cycles par boucle : les ondulations se rejoignent
                    float swellMid = 1f + 0.13f * MathF.Sin(Tau * 3f * t + p1) + 0.07f * MathF.Sin(Tau * 7f * t + p2);
                    float swellHigh = 1f + 0.16f * MathF.Sin(Tau * 5f * t + p2) + 0.08f * MathF.Sin(Tau * 11f * t + p3);
                    float y = low.Run(x) * 0.4f + mid.Run(x) * swellMid * 0.6f + high.Run(x) * swellHigh * 0.5f;
                    air += (y - air) * airCoef;
                    if (i >= 0) d[k * 2 + ch] = air;
                }
            }
            Normalize(d, 0.9f);
            return d;
        }

        /// <summary>Crépitement : nuée de minuscules impacts, nombreux petits et aigus, plus rares gros et ronds.</summary>
        public static float[] Patter()
        {
            int n = (int)(4.7f * Rate);
            var d = new float[n * 2];
            var rng = new Random(202);
            int count = (int)(4.7f * 620f);
            for (int i = 0; i < count; i++)
            {
                float u = (float)rng.NextDouble();
                float size = u * u;
                float f = 7000f * MathF.Pow(1500f / 7000f, size) * Vary(rng, 0.15f);
                float tau = Lerp(0.0006f, 0.0026f, size);
                float amp = Lerp(0.14f, 1f, size);
                Tick(d, n, rng.Next(n), f, tau, amp, (float)rng.NextDouble());
            }
            Normalize(d, 0.9f);
            return d;
        }

        /// <summary>Gouttes détachées : impacts secs sur la route et la carrosserie, quelques « plocs » dans l'eau.</summary>
        public static float[] Drops()
        {
            int n = (int)(11.3f * Rate);
            var d = new float[n * 2];
            var rng = new Random(303);
            int count = (int)(11.3f * 24f);
            for (int i = 0; i < count; i++)
            {
                int at = rng.Next(n);
                float u = (float)rng.NextDouble();
                float amp = Lerp(0.2f, 1f, u * u * u);
                float pan = 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.9f;
                if (rng.NextDouble() < 0.28)
                    Plip(d, n, at, 520f + (float)rng.NextDouble() * 900f, 0.3f + (float)rng.NextDouble() * 0.5f,
                        0.01f + (float)rng.NextDouble() * 0.012f, amp * 0.55f, pan);
                else
                    Tick(d, n, at, 900f + (float)rng.NextDouble() * 2300f, 0.0022f + (float)rng.NextDouble() * 0.0045f, amp, pan);
            }
            Normalize(d, 0.9f);
            return d;
        }

        /// <summary>Impact : résonance très amortie, ajoutée en bouclant sur la fin du tampon.</summary>
        static void Tick(float[] d, int n, int start, float freq, float tau, float amp, float pan)
        {
            float w = Tau * freq / Rate;
            float decay = MathF.Exp(-1f / (tau * Rate));
            float c = 2f * decay * MathF.Cos(w), rr = decay * decay;
            float y1 = 0f, y = amp * MathF.Sin(w);
            float left = MathF.Cos(pan * Tau * 0.25f), right = MathF.Sin(pan * Tau * 0.25f);
            int len = (int)(tau * Rate * 7f);
            for (int k = 0; k < len; k++)
            {
                int j = (start + k) % n;
                d[j * 2] += y * left;
                d[j * 2 + 1] += y * right;
                float next = c * y - rr * y1;
                y1 = y;
                y = next;
            }
        }

        /// <summary>Goutte dans l'eau : la bulle qu'elle emprisonne chante une note brève qui monte.</summary>
        static void Plip(float[] d, int n, int start, float freq, float rise, float tau, float amp, float pan)
        {
            float left = MathF.Cos(pan * Tau * 0.25f), right = MathF.Sin(pan * Tau * 0.25f);
            float phase = 0f;
            int len = (int)(tau * Rate * 6f);
            for (int k = 0; k < len; k++)
            {
                float t = k / (float)Rate;
                phase += Tau * freq * (1f + rise * (1f - MathF.Exp(-t / 0.02f))) / Rate;
                float y = MathF.Sin(phase) * (1f - MathF.Exp(-t / 0.0012f)) * MathF.Exp(-t / tau) * amp;
                int j = (start + k) % n;
                d[j * 2] += y * left;
                d[j * 2 + 1] += y * right;
            }
        }

        /// <summary>Filtre passe-bande doux : différence de deux passe-bas à un pôle.</summary>
        struct Band
        {
            readonly float a, b;
            float lo, hi;

            public Band(float low, float high)
            {
                a = 1f - MathF.Exp(-Tau * low / Rate);
                b = 1f - MathF.Exp(-Tau * high / Rate);
                lo = hi = 0f;
            }

            public float Run(float x)
            {
                lo += (x - lo) * a;
                hi += (x - hi) * b;
                return hi - lo;
            }
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

        static float Vary(Random rng, float amount) => 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Smooth(float from, float to, float x)
        {
            float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
