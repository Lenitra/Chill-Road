using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace NoHesi
{
    /// <summary>
    /// Musique de fond synthwave générée en temps réel (aucun fichier) : une playlist de morceaux ayant chacun leur tempo,
    /// leur tonalité, leur grille d'accords et leurs motifs (batterie années 80 à caisse claire « gated », basse pulsée
    /// qui pompe sur la grosse caisse, arpèges en écho, nappes désaccordées, mélodie), enchaînés automatiquement.
    /// Des fichiers .ogg/.mp3/.wav déposés dans StreamingAssets/Music sont joués à la place (le synthé garde la montée
    /// et le « drop » du départ). Pilotage : étouffée au menu et en pause, montée puis drop au lancement de la partie.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SynthwaveMusic : MonoBehaviour
    {
        static readonly string[] Names =
        {
            "Neon Overdrive", "Midnight Mile", "Chrome Horizon", "Afterglow 1986",
            "Sunset Interstate", "Violet Tail Lights", "Analog Dreams", "Night Shift Cruise",
        };
        // Grilles (degrés de la gamme mineure : 0 = i, 2 = III, 3 = iv, 4 = v, 5 = VI, 6 = VII)
        static readonly int[][] Progressions =
        {
            new[] { 0, 5, 2, 6 }, new[] { 0, 6, 5, 6 }, new[] { 5, 6, 0, 0 },
            new[] { 0, 3, 5, 4 }, new[] { 0, 5, 3, 6 }, new[] { 5, 3, 0, 6 },
        };
        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[][] ArpPatterns = { new[] { 0, 1, 2, 1 }, new[] { 0, 2, 1, 2 }, new[] { 0, 1, 2, 3 }, new[] { 3, 2, 1, 0 }, new[] { 0, 2, 3, 2 } };

        struct Section
        {
            public int bars;
            public bool pad, arp, bass, kick, halfKick, snare, hats, lead, roll;
            public float filterFrom, filterTo, fadeFrom, fadeTo;
        }

        sealed class Track
        {
            public string name;
            public float bpm;
            public int root;                 // tonique (MIDI, octave 2)
            public int[] prog, arp;
            public int bassStyle, hatStyle;
            public bool fourFloor;
            public Section[] sections;
            public int[] sectionStart;
            public int totalBars, dropBar;
            public int[][] melody;           // 8 mesures de mélodie : 8 croches (-1 silence, -2 tenue, sinon MIDI)
        }

        public static SynthwaveMusic Instance { get; private set; }
        /// <summary>Titre en cours (annoncé dans le HUD).</summary>
        public string NowPlaying { get; private set; } = "";
        public event Action<string> TrackChanged;
        public bool UsingFiles => files.Count > 0;

        // Commandes du thread principal → thread audio
        volatile float volume = 0.6f, muffle = 1f, riserSeconds = 3f;
        volatile int dropRequests, riserRequests, nextRequests, announced = -1;
        int dropSeen, riserSeen, nextSeen, announcedSeen = -1;

        int rate;
        readonly List<Track> tracks = new List<Track>();
        int trackIndex;

        // Fichiers de l'utilisateur
        readonly List<string> files = new List<string>();
        AudioSource fileSource;
        AudioLowPassFilter fileFilter;
        int fileIndex;

        // ------------------------------------------------------------------ état du thread audio
        Track cur;
        int bar, step, sampleInStep, stepLen;
        uint noiseSeed = 22222;
        float smoothMuffle = 1f, lpL1, lpL2, lpR1, lpR2, lpA;
        int lpCounter;
        // Batterie
        float kickT = 10f, kickVel, snareT = 10f, snareVel, hatT = 10f, hatVel, hatLen = 0.04f, crashT = 10f, duck;
        float kickPhase;
        // Basse
        float bassFreq, bassP1, bassP2, bassEnv, bassFEnv, bassGate, bassS1, bassS2;
        // Arpège
        float arpFreq, arpP1, arpP2, arpEnv, arpS1, arpS2;
        // Nappe : 3 notes × 2 oscillateurs, glissement d'un accord à l'autre
        readonly float[] padFreq = new float[3], padTarget = new float[3], padPA = new float[3], padPB = new float[3];
        float padLevel, padS1L, padS2L, padS1R, padS2R;
        // Mélodie
        float leadFreq, leadTarget, leadP1, leadP2, leadEnv, leadGate, leadS1, leadS2, leadAge, leadVib;
        // Montée (riser)
        float riserT = -1f, riserLen = 3f, riserS1, riserS2, rollTimer;
        // Effets : écho ping-pong et réverbération
        float[] delayL, delayR;
        int delayPos, delayLen;
        readonly float[][] combL = new float[4][], combR = new float[4][];
        readonly int[] combPosL = new int[4], combPosR = new int[4];
        readonly float[] combFiltL = new float[4], combFiltR = new float[4];
        readonly float[][] apL = new float[2][], apR = new float[2][];
        readonly int[] apPosL = new int[2], apPosR = new int[2];
        float sectionFade = 1f, sectionFilter = 1f;

        void Awake()
        {
            Instance = this;
            Setup(Environment.TickCount);

            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("MusicCarrier", rate, 1, rate, false);
            src.loop = true;
            src.volume = 1f;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.Play();

            FindUserFiles();
        }

        /// <summary>Prépare la playlist et les effets (au lancement, ou pour un rendu hors ligne).</summary>
        void Setup(int shuffleSeed)
        {
            if (tracks.Count > 0) return;
            rate = AudioSettings.outputSampleRate;
            BuildEffects();
            // Playlist : 8 morceaux, ordre mélangé à chaque lancement
            var rng = new System.Random(shuffleSeed);
            var order = new List<int>();
            for (int i = 0; i < Names.Length; i++) order.Insert(rng.Next(order.Count + 1), i);
            foreach (int i in order) tracks.Add(Generate(Names[i], 1000 + i * 7919));
            StartTrack(0);
        }

        // ------------------------------------------------------------------ commandes

        /// <summary>Volume (0-1) et étouffement (0 = ouvert, 1 = menu : filtré, lointain).</summary>
        public void Set(float vol, float muffled)
        {
            volume = vol;
            muffle = muffled;
            if (fileSource)
            {
                fileSource.volume = vol * 0.9f;
                fileFilter.cutoffFrequency = Mathf.Lerp(22000f, 700f, Mathf.Sqrt(Mathf.Clamp01(muffled)));
            }
        }

        /// <summary>Montée de tension (bruit qui s'ouvre, roulement de caisse claire) sur une durée donnée.</summary>
        public void Riser(float seconds) { riserSeconds = seconds; riserRequests++; }

        /// <summary>Le « drop » : cymbale, grosse caisse, et le morceau repart sur sa partie la plus énergique.</summary>
        public void Drop() => dropRequests++;

        /// <summary>Morceau suivant de la playlist (il démarre sur sa montée, avec une cymbale).</summary>
        public void Next()
        {
            if (UsingFiles) { if (fileSource) fileSource.Stop(); }
            else nextRequests++;
        }

        void Update()
        {
            int a = announced;
            if (a != announcedSeen && a >= 0 && a < tracks.Count && !UsingFiles)
            {
                announcedSeen = a;
                NowPlaying = tracks[a].name;
                TrackChanged?.Invoke(NowPlaying);
            }
        }

        // ------------------------------------------------------------------ fichiers de l'utilisateur

        void FindUserFiles()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Music");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".ogg" || ext == ".mp3" || ext == ".wav") files.Add(f);
            }
            if (files.Count == 0) return;
            var rng = new System.Random(Environment.TickCount);
            for (int i = files.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (files[i], files[j]) = (files[j], files[i]); }
            var go = new GameObject("MusicFiles");
            go.transform.SetParent(transform, false);
            fileSource = go.AddComponent<AudioSource>();
            fileSource.spatialBlend = 0f;
            fileSource.playOnAwake = false;
            fileFilter = go.AddComponent<AudioLowPassFilter>();
            StartCoroutine(PlayFiles());
        }

        IEnumerator PlayFiles()
        {
            while (true)
            {
                string path = files[fileIndex % files.Count];
                fileIndex++;
                var type = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG
                    : path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS;
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type))
                {
                    ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning("Musique illisible : " + path); yield return null; continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    fileSource.clip = clip;
                    fileSource.Play();
                    NowPlaying = Path.GetFileNameWithoutExtension(path);
                    TrackChanged?.Invoke(NowPlaying);
                    while (fileSource && (fileSource.isPlaying || Time.timeScale < 0.01f || !Application.isFocused && fileSource.time > 0f && fileSource.time < clip.length - 0.1f))
                        yield return null;
                    Destroy(clip);
                }
            }
        }

        // ------------------------------------------------------------------ génération des morceaux

        static Track Generate(string name, int seed)
        {
            var r = new System.Random(seed);
            var t = new Track
            {
                name = name,
                bpm = 88 + r.Next(0, 5) * 6,             // 88 à 112
                root = 40 + r.Next(0, 8),                // Mi2 à Si2
                prog = Progressions[r.Next(Progressions.Length)],
                arp = ArpPatterns[r.Next(ArpPatterns.Length)],
                bassStyle = r.Next(3),
                hatStyle = r.Next(2),
                fourFloor = r.NextDouble() < 0.6,
            };
            var s = new List<Section>
            {
                new Section { bars = 8, pad = true, arp = true, filterFrom = 0.35f, filterTo = 0.6f, fadeFrom = 0.3f, fadeTo = 1f },
                new Section { bars = 8, pad = true, arp = true, bass = true, kick = true, halfKick = true, hats = true, roll = true, filterFrom = 0.55f, filterTo = 0.9f, fadeFrom = 1f, fadeTo = 1f },
                new Section { bars = 16, pad = true, arp = true, bass = true, kick = true, snare = true, hats = true, filterFrom = 1f, filterTo = 1f, fadeFrom = 1f, fadeTo = 1f },
                new Section { bars = 8, pad = true, arp = true, bass = true, roll = true, filterFrom = 0.6f, filterTo = 0.85f, fadeFrom = 1f, fadeTo = 1f },
                new Section { bars = 16, pad = true, arp = true, bass = true, kick = true, snare = true, hats = true, lead = true, filterFrom = 1f, filterTo = 1f, fadeFrom = 1f, fadeTo = 1f },
                new Section { bars = 8, pad = true, arp = true, kick = true, halfKick = true, filterFrom = 1f, filterTo = 0.4f, fadeFrom = 1f, fadeTo = 0f },
            };
            t.sections = s.ToArray();
            t.sectionStart = new int[t.sections.Length];
            int total = 0;
            for (int i = 0; i < t.sections.Length; i++) { t.sectionStart[i] = total; total += t.sections[i].bars; }
            t.totalBars = total;
            t.dropBar = t.sectionStart[2];

            // Mélodie : notes de l'accord surtout, quelques notes de passage, des silences
            t.melody = new int[8][];
            for (int b = 0; b < 8; b++)
            {
                var bar = new int[8];
                int degree = t.prog[b % 4];
                for (int k = 0; k < 8; k++)
                {
                    double x = r.NextDouble();
                    if (k > 0 && x < 0.3) { bar[k] = -2; continue; }            // tenue
                    if (x < 0.42) { bar[k] = -1; continue; }                    // silence
                    int d = r.NextDouble() < 0.65 ? degree + 2 * r.Next(3) : degree + r.Next(7);
                    bar[k] = Note(t.root, d, 2);
                }
                bar[0] = Note(t.root, degree + 2 * r.Next(3), 2);                // attaque franche en début de mesure
                t.melody[b] = bar;
            }
            return t;
        }

        static int Note(int root, int degree, int octave)
        {
            int o = Mathf.FloorToInt(degree / 7f);
            int d = degree - o * 7;
            return root + Minor[d] + 12 * (o + octave);
        }

        static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        void StartTrack(int index)
        {
            trackIndex = ((index % tracks.Count) + tracks.Count) % tracks.Count;
            cur = tracks[trackIndex];
            bar = 0;
            step = 0;
            sampleInStep = 0;
            stepLen = Mathf.RoundToInt(rate * 60f / cur.bpm / 4f);
            delayLen = Mathf.Clamp(Mathf.RoundToInt(rate * 60f / cur.bpm * 0.75f), 1, delayL.Length - 1);
            SetChord(0);
            announced = trackIndex;
        }

        void BuildEffects()
        {
            delayL = new float[rate * 2];
            delayR = new float[rate * 2];
            int[] combs = { 1116, 1188, 1277, 1356 };
            int[] aps = { 556, 441 };
            float k = rate / 44100f;
            for (int i = 0; i < 4; i++)
            {
                combL[i] = new float[Mathf.RoundToInt(combs[i] * k)];
                combR[i] = new float[Mathf.RoundToInt((combs[i] + 23) * k)];
            }
            for (int i = 0; i < 2; i++)
            {
                apL[i] = new float[Mathf.RoundToInt(aps[i] * k)];
                apR[i] = new float[Mathf.RoundToInt((aps[i] + 23) * k)];
            }
        }

        // ------------------------------------------------------------------ séquenceur (thread audio)

        Section CurrentSection(out float progress)
        {
            for (int i = cur.sections.Length - 1; i >= 0; i--)
                if (bar >= cur.sectionStart[i])
                {
                    progress = (bar - cur.sectionStart[i] + step / 16f) / cur.sections[i].bars;
                    return cur.sections[i];
                }
            progress = 0f;
            return cur.sections[0];
        }

        void SetChord(int barIndex)
        {
            int degree = cur.prog[barIndex % 4];
            for (int i = 0; i < 3; i++) padTarget[i] = Hz(Note(cur.root, degree + 2 * i, 1));
            if (padFreq[0] <= 0f) for (int i = 0; i < 3; i++) padFreq[i] = padTarget[i];
        }

        void OnStep()
        {
            var sec = CurrentSection(out _);
            int degree = cur.prog[bar % 4];
            if (step == 0) SetChord(bar);

            // Batterie
            if (sec.kick)
            {
                bool kick = sec.halfKick ? step == 0 || step == 8 : cur.fourFloor ? step % 4 == 0 : step == 0 || step == 8 || step == 10;
                if (kick) { kickT = 0f; kickVel = 1f; duck = 1f; }
            }
            if (sec.snare && (step == 4 || step == 12)) { snareT = 0f; snareVel = 1f; }
            if (sec.hats)
            {
                bool open = cur.hatStyle == 1 && step % 4 == 2;
                if (cur.hatStyle == 1 || step % 2 == 0) { hatT = 0f; hatVel = step % 4 == 2 ? 1f : 0.6f; hatLen = open ? 0.16f : 0.04f; }
            }
            // Roulement de caisse claire sur la dernière mesure de la section
            if (sec.roll && bar == SectionEnd() - 1 && step >= 8) { snareT = 0f; snareVel = 0.35f + (step - 8) / 8f * 0.65f; }

            // Basse
            if (sec.bass)
            {
                int n = Note(cur.root, degree, -1);
                bool hit = cur.bassStyle == 1 || step % 2 == 0;
                if (cur.bassStyle == 2) hit = step % 4 == 2 || step == 0;
                if (hit)
                {
                    bool octave = cur.bassStyle == 0 && step % 4 == 2;
                    bassFreq = Hz(n + (octave ? 12 : 0));
                    bassEnv = 1f; bassFEnv = 1f; bassGate = 1f;
                }
            }
            // Arpège (doubles croches sur les notes de l'accord)
            if (sec.arp)
            {
                int idx = cur.arp[step % cur.arp.Length];
                int note = idx == 3 ? Note(cur.root, degree, 3) : Note(cur.root, degree + 2 * idx, 2);
                arpFreq = Hz(note);
                arpEnv = step % 4 == 0 ? 1f : 0.7f;
            }
            // Mélodie (croches)
            if (sec.lead && step % 2 == 0)
            {
                int b = (bar - cur.sectionStart[4]) % 8;
                int v = cur.melody[Mathf.Clamp(b, 0, 7)][step / 2];
                if (v >= 0) { leadTarget = Hz(v); if (leadFreq <= 0f) leadFreq = leadTarget; leadEnv = Mathf.Max(leadEnv, 0.2f); leadGate = 1f; leadAge = 0f; }
                else if (v == -1) leadGate = 0f;
            }
            else if (!sec.lead) leadGate = 0f;
        }

        int SectionEnd()
        {
            for (int i = 0; i < cur.sections.Length; i++)
                if (bar < cur.sectionStart[i] + cur.sections[i].bars) return cur.sectionStart[i] + cur.sections[i].bars;
            return cur.totalBars;
        }

        float Noise()
        {
            noiseSeed ^= noiseSeed << 13; noiseSeed ^= noiseSeed >> 17; noiseSeed ^= noiseSeed << 5;
            return (noiseSeed & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        static float Saw(ref float phase, float inc)
        {
            phase += inc;
            if (phase >= 1f) phase -= 1f;
            return 2f * phase - 1f - Blep(phase, inc);
        }

        static float Pulse(ref float phase, float inc, float width)
        {
            phase += inc;
            if (phase >= 1f) phase -= 1f;
            float v = phase < width ? 1f : -1f;
            v += Blep(phase, inc);
            float t2 = phase - width; if (t2 < 0f) t2 += 1f;
            v -= Blep(t2, inc);
            return v;
        }

        /// <summary>Filtre passe-bas résonant (SVF, approximation de tan pour les fréquences modérées).</summary>
        static float Lowpass(float x, float fc, float q, ref float s1, ref float s2, int rate)
        {
            float g = Mathf.Min(Mathf.PI * fc / rate, 1.2f);
            float k = 1f / q;
            float v1 = (s1 + g * (x - s2)) / (1f + g * (g + k));
            float v2 = s2 + g * v1;
            s1 = 2f * v1 - s1;
            s2 = 2f * v2 - s2;
            return v2;
        }

        static float Bandpass(float x, float fc, float q, ref float s1, ref float s2, int rate)
        {
            float g = Mathf.Min(Mathf.PI * fc / rate, 1.2f);
            float k = 1f / q;
            float v1 = (s1 + g * (x - s2)) / (1f + g * (g + k));
            float v2 = s2 + g * v1;
            s1 = 2f * v1 - s1;
            s2 = 2f * v2 - s2;
            return v1;
        }

        readonly object gate = new object();

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (gate) Process(data, channels);
        }

        void Process(float[] data, int channels)
        {
            if (cur == null) { Array.Clear(data, 0, data.Length); return; }
            float vol = volume;
            bool synthMusic = files.Count == 0;
            if (riserRequests != riserSeen) { riserSeen = riserRequests; riserT = 0f; riserLen = Mathf.Max(0.5f, riserSeconds); rollTimer = 0f; }
            if (nextRequests != nextSeen)
            {
                nextSeen = nextRequests;
                StartTrack(trackIndex + 1);
                bar = cur.sectionStart[1];
                crashT = 0f;
            }
            if (dropRequests != dropSeen)
            {
                dropSeen = dropRequests;
                riserT = -1f;
                crashT = 0f; kickT = 0f; kickVel = 1.2f; duck = 1f;
                bar = cur.dropBar; step = 0;
                SetChord(bar);
                OnStep();
                sampleInStep = 1;
            }
            float dt = 1f / rate;
            float stepSec = stepLen * dt;
            for (int i = 0; i < data.Length; i += channels)
            {
                // ---- séquenceur
                if (sampleInStep == 0 && synthMusic) OnStep();
                sampleInStep++;
                if (sampleInStep >= stepLen)
                {
                    sampleInStep = 0;
                    step++;
                    if (step >= 16)
                    {
                        step = 0;
                        bar++;
                        if (bar >= cur.totalBars) StartTrack(trackIndex + 1);
                    }
                }
                var sec = CurrentSection(out float progress);
                sectionFade = Mathf.Lerp(sec.fadeFrom, sec.fadeTo, progress);
                sectionFilter = Mathf.Lerp(sec.filterFrom, sec.filterTo, progress);
                if (!synthMusic) sectionFade = 0f;

                float L = 0f, R = 0f, reverbSend = 0f, delaySend = 0f;
                float pump = 1f - 0.5f * duck;
                duck *= 0.99975f;

                // ---- grosse caisse : sinus à hauteur descendante + attaque
                if (kickT < 0.5f)
                {
                    float f = 45f + 95f * Mathf.Exp(-kickT * 38f);
                    kickPhase += f * dt;
                    float k = Mathf.Sin(2f * Mathf.PI * kickPhase) * Mathf.Exp(-kickT * 7f) * kickVel;
                    k += Noise() * Mathf.Exp(-kickT * 400f) * 0.15f * kickVel;
                    L += k * 0.55f; R += k * 0.55f;
                    kickT += dt;
                }
                else kickPhase = 0f;

                // ---- caisse claire années 80 : corps + bruit, queue « gated » (coupée net)
                if (snareT < 0.35f)
                {
                    float gate = snareT < 0.26f ? 1f : Mathf.Exp(-(snareT - 0.26f) * 60f);
                    float body = Mathf.Sin(2f * Mathf.PI * 185f * snareT) * Mathf.Exp(-snareT * 30f);
                    float n = Noise() * (0.45f * Mathf.Exp(-snareT * 18f) + 0.25f) * gate;
                    float s = (body * 0.5f + n) * snareVel * 0.5f;
                    L += s; R += s;
                    reverbSend += s * 0.5f;
                    snareT += dt;
                }

                // ---- charleston
                if (hatT < hatLen * 3f)
                {
                    float n = Noise();
                    float hp = n - lpHat; lpHat += (n - lpHat) * 0.55f;
                    float h = hp * Mathf.Exp(-hatT / hatLen) * hatVel * 0.22f;
                    L += h * 0.8f; R += h;
                    hatT += dt;
                }

                // ---- cymbale du drop
                if (crashT < 2f)
                {
                    float n = Noise();
                    float hp = n - lpCrash; lpCrash += (n - lpCrash) * 0.4f;
                    float c = hp * Mathf.Exp(-crashT * 2.2f) * 0.22f;
                    L += c; R += c * 0.9f;
                    reverbSend += c * 0.3f;
                    crashT += dt;
                }

                if (synthMusic)
                {
                    // ---- basse : deux dents de scie désaccordées, filtre qui claque à chaque note, pompe sur la grosse caisse
                    if (bassFreq > 0f)
                    {
                        float inc = bassFreq * dt;
                        float b = Saw(ref bassP1, inc) + Saw(ref bassP2, inc * 1.006f);
                        float fc = 140f + 2200f * bassFEnv * sectionFilter;
                        b = Lowpass(b, fc, 1.4f, ref bassS1, ref bassS2, rate);
                        bassEnv += ((sec.bass ? 0.85f : 0f) - bassEnv) * 0.0004f;
                        bassFEnv *= 0.99955f;
                        b *= bassEnv * (0.45f + 0.55f * bassFEnv) * pump * 0.2f;
                        L += b; R += b;
                    }
                    // ---- arpège : impulsion étroite filtrée, écho ping-pong
                    if (sec.arp && arpFreq > 0f)
                    {
                        float inc = arpFreq * dt;
                        float a = Pulse(ref arpP1, inc, 0.25f) * 0.7f + Saw(ref arpP2, inc * 0.997f) * 0.3f;
                        float fc = 800f + 5200f * arpEnv * sectionFilter;
                        a = Lowpass(a, fc, 1.8f, ref arpS1, ref arpS2, rate);
                        a *= arpEnv * pump * 0.2f;
                        arpEnv *= 0.99988f;
                        L += a * 0.7f; R += a * 0.5f;
                        delaySend += a * 0.9f;
                    }
                    // ---- nappe : accord glissant, deux oscillateurs par note écartés en stéréo
                    padLevel += ((sec.pad ? 1f : 0f) - padLevel) * 0.00005f;
                    if (padLevel > 0.001f)
                    {
                        float pl = 0f, pr = 0f;
                        for (int n = 0; n < 3; n++)
                        {
                            padFreq[n] += (padTarget[n] - padFreq[n]) * 0.0006f;
                            float inc = padFreq[n] * dt;
                            pl += Saw(ref padPA[n], inc * 0.996f);
                            pr += Saw(ref padPB[n], inc * 1.004f);
                        }
                        float fc = 1100f + 3600f * sectionFilter;
                        pl = Lowpass(pl, fc, 0.9f, ref padS1L, ref padS2L, rate);
                        pr = Lowpass(pr, fc, 0.9f, ref padS1R, ref padS2R, rate);
                        float g = padLevel * (0.6f + 0.4f * pump) * 0.1f;
                        L += pl * g; R += pr * g;
                        reverbSend += (pl + pr) * g * 0.4f;
                    }
                    // ---- mélodie : dents de scie + carré, portamento et vibrato
                    leadEnv += ((leadGate > 0f ? 1f : 0f) - leadEnv) * (leadGate > 0f ? 0.004f : 0.0006f);
                    if (leadEnv > 0.001f && leadTarget > 0f)
                    {
                        leadFreq += (leadTarget - leadFreq) * 0.0015f;
                        leadAge += dt;
                        leadVib += 5.5f * dt;
                        float vib = 1f + Mathf.Sin(2f * Mathf.PI * leadVib) * 0.004f * Mathf.Clamp01((leadAge - 0.25f) * 3f);
                        float inc = leadFreq * vib * dt;
                        float l = Saw(ref leadP1, inc) * 0.6f + Pulse(ref leadP2, inc * 0.5f, 0.5f) * 0.4f;
                        l = Lowpass(l, 3400f, 1.2f, ref leadS1, ref leadS2, rate) * leadEnv * 0.16f;
                        L += l; R += l;
                        delaySend += l * 0.6f;
                        reverbSend += l * 0.4f;
                    }
                }

                // ---- montée : bruit dont le filtre s'ouvre + roulement de caisse claire qui accélère
                if (riserT >= 0f)
                {
                    float x = Mathf.Clamp01(riserT / riserLen);
                    float fc = 300f * Mathf.Pow(20f, x);
                    float r = Bandpass(Noise(), fc, 2.5f, ref riserS1, ref riserS2, rate) * x * x * 0.5f;
                    L += r; R += r;
                    reverbSend += r * 0.3f;
                    if (x > 0.45f)
                    {
                        rollTimer -= dt;
                        if (rollTimer <= 0f) { snareT = 0f; snareVel = 0.3f + x * 0.5f; rollTimer = Mathf.Lerp(stepSec, stepSec * 0.35f, (x - 0.45f) / 0.55f); }
                    }
                    riserT += dt;
                    if (riserT > riserLen + 1.5f) riserT = -1f;
                }

                // ---- écho ping-pong (croche pointée)
                int rd = delayPos - delayLen; if (rd < 0) rd += delayL.Length;
                float dl = delayL[rd], dr = delayR[rd];
                delayL[delayPos] = delaySend + dr * 0.38f;
                delayR[delayPos] = dl * 0.38f;
                delayPos++; if (delayPos >= delayL.Length) delayPos = 0;
                L += dl * 0.35f; R += dr * 0.35f;

                // ---- réverbération (type Freeverb allégé)
                float rin = reverbSend * 0.2f;
                float revL = 0f, revR = 0f;
                for (int c = 0; c < 4; c++)
                {
                    float[] bl = combL[c], br = combR[c];
                    float ol = bl[combPosL[c]], or = br[combPosR[c]];
                    combFiltL[c] = ol * 0.7f + combFiltL[c] * 0.3f;
                    combFiltR[c] = or * 0.7f + combFiltR[c] * 0.3f;
                    bl[combPosL[c]] = rin + combFiltL[c] * 0.83f;
                    br[combPosR[c]] = rin + combFiltR[c] * 0.83f;
                    if (++combPosL[c] >= bl.Length) combPosL[c] = 0;
                    if (++combPosR[c] >= br.Length) combPosR[c] = 0;
                    revL += ol; revR += or;
                }
                for (int a = 0; a < 2; a++)
                {
                    float[] bl = apL[a], br = apR[a];
                    float bol = bl[apPosL[a]], bor = br[apPosR[a]];
                    bl[apPosL[a]] = revL + bol * 0.5f; br[apPosR[a]] = revR + bor * 0.5f;
                    revL = bol - revL; revR = bor - revR;
                    if (++apPosL[a] >= bl.Length) apPosL[a] = 0;
                    if (++apPosR[a] >= br.Length) apPosR[a] = 0;
                }
                L += revL * 0.28f; R += revR * 0.28f;

                // ---- bus maître : fondu de section, étouffement (menu), saturation douce
                L *= sectionFade; R *= sectionFade;
                if (--lpCounter <= 0)
                {
                    lpCounter = 64;
                    smoothMuffle += (muffle - smoothMuffle) * 0.02f;
                    float fc = Mathf.Lerp(18000f, 650f, Mathf.Sqrt(Mathf.Clamp01(smoothMuffle)));
                    lpA = 1f - Mathf.Exp(-2f * Mathf.PI * fc / rate);
                }
                lpL1 += (L - lpL1) * lpA; lpL2 += (lpL1 - lpL2) * lpA;
                lpR1 += (R - lpR1) * lpA; lpR2 += (lpR1 - lpR2) * lpA;
                float oL = (float)Math.Tanh(lpL2 * 1.1f) * vol;
                float oR = (float)Math.Tanh(lpR2 * 1.1f) * vol;
                if (channels == 1) data[i] = (oL + oR) * 0.5f;
                else
                {
                    data[i] = oL;
                    data[i + 1] = oR;
                    for (int c = 2; c < channels; c++) data[i + c] = 0f;
                }
            }
        }

        float lpHat, lpCrash;

        /// <summary>Rendu hors ligne (tests) : quelques secondes du morceau i à partir d'une mesure donnée.</summary>
        public float[] RenderPreview(int trackNumber, int startBar, float seconds)
        {
            lock (gate)
            {
                Setup(1);
                StartTrack(trackNumber);
                bar = startBar;
                muffle = 0f; smoothMuffle = 0f; volume = 1f;
            }
            var buffer = new float[Mathf.RoundToInt(seconds * rate) * 2];
            lock (gate) Process(buffer, 2);
            return buffer;
        }
    }
}
