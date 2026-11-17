using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChillRoad.DebugTools
{
    // Outil de test temporaire : enregistre la sortie audio d'un objet (placé après la source et ses filtres) en WAV.
    public class AudioTap : MonoBehaviour
    {
        readonly List<float> samples = new List<float>();
        volatile bool recording;
        int channels = 2;
        public int SampleRate { get; private set; }

        void Awake() => SampleRate = AudioSettings.outputSampleRate;

        public void Begin() { lock (samples) samples.Clear(); recording = true; }

        public void End() => recording = false;

        void OnAudioFilterRead(float[] data, int ch)
        {
            if (!recording) return;
            channels = ch;
            lock (samples) for (int i = 0; i < data.Length; i += ch) samples.Add(data[i]);
        }

        /// <summary>Écrit le mono enregistré (16 bits) et renvoie niveau efficace, crête, et nombre d'échantillons.</summary>
        public string Save(string path)
        {
            float[] d;
            lock (samples) d = samples.ToArray();
            double sum = 0; float peak = 0f; int bad = 0;
            foreach (var x in d)
            {
                if (float.IsNaN(x) || float.IsInfinity(x)) { bad++; continue; }
                sum += x * x; peak = Mathf.Max(peak, Mathf.Abs(x));
            }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = d.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var x in d) w.Write((short)(Mathf.Clamp(float.IsNaN(x) ? 0f : x, -1f, 1f) * 32767f));
            }
            return $"n={d.Length} rms={Mathf.Sqrt((float)(sum / Mathf.Max(1, d.Length))):F3} peak={peak:F3} nan={bad}";
        }
    }
}
