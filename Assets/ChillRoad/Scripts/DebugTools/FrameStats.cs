using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Mesure des temps d'image (outil de mise au point) : garde les durées des dernières images et donne la médiane
    /// et le 95e centile. Les images anormalement longues (pause de l'éditeur, compilation d'une commande) comptent
    /// dans le centile mais pas dans la médiane. Usage : FrameStats.Begin(), puis FrameStats.Report() plus tard.
    /// </summary>
    public class FrameStats : MonoBehaviour
    {
        static FrameStats instance;
        readonly List<float> samples = new List<float>(4096);

        public static void Begin()
        {
            if (!instance)
            {
                var go = new GameObject("FrameStats");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<FrameStats>();
            }
            instance.samples.Clear();
            instance.cpu.Clear();
            instance.gpu.Clear();
        }

        readonly List<float> cpu = new List<float>(4096), gpu = new List<float>(4096);
        readonly FrameTiming[] timings = new FrameTiming[1];

        void Update()
        {
            if (samples.Count < 4096) samples.Add(Time.unscaledDeltaTime * 1000f);
            // Temps du processeur (fil principal) et de la carte graphique, si la plateforme les fournit
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && cpu.Count < 4096)
            {
                cpu.Add((float)timings[0].cpuMainThreadFrameTime);
                gpu.Add((float)timings[0].gpuFrameTime);
            }
        }

        static float Median(List<float> l)
        {
            if (l.Count == 0) return 0f;
            var s = new List<float>(l);
            s.Sort();
            return s[s.Count / 2];
        }

        /// <summary>Médianes du temps processeur (fil principal) et carte graphique (ms), 0 si indisponibles.</summary>
        public static string CpuGpu() => instance ? string.Format("cpu {0:F1} ms, gpu {1:F1} ms", Median(instance.cpu), Median(instance.gpu)) : "";

        /// <summary>« n images, médiane x ms (y fps), 95 % : z ms, moyenne w ms ».</summary>
        public static string Report()
        {
            if (!instance || instance.samples.Count < 5) return "FrameStats : pas assez d'images";
            var s = new List<float>(instance.samples);
            s.Sort();
            float med = s[s.Count / 2], p95 = s[Mathf.Min(s.Count - 1, (int)(s.Count * 0.95f))];
            float sum = 0f;
            int n = 0;
            foreach (var v in s) if (v < med * 4f) { sum += v; n++; }
            return string.Format("{0} images, médiane {1:F1} ms ({2:F0} fps), 95 % {3:F1} ms, moyenne {4:F1} ms",
                s.Count, med, 1000f / med, p95, n > 0 ? sum / n : 0f);
        }
    }
}
