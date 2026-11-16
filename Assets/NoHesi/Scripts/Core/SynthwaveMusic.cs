using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace NoHesi
{
    /// <summary>
    /// Musique de fond générée en temps réel (aucun fichier) : une playlist de huit morceaux de styles bien distincts
    /// (outrun, lo-fi, italo disco, slow 12/8, city pop funk, darksynth, école de Berlin, drum &amp; bass), écrits dans
    /// MusicTracks et joués par MusicSynth, enchaînés automatiquement dans un ordre mélangé à chaque lancement.
    /// Des fichiers .ogg/.mp3/.wav déposés dans StreamingAssets/Music sont joués à la place (le synthé garde la montée
    /// et le « drop » du départ). Pilotage : étouffée au menu et en pause, montée puis drop au lancement de la partie.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SynthwaveMusic : MonoBehaviour
    {
        public static SynthwaveMusic Instance { get; private set; }
        /// <summary>Titre en cours (annoncé dans le HUD).</summary>
        public string NowPlaying { get; private set; } = "";
        public event Action<string> TrackChanged;
        public bool UsingFiles => files.Count > 0;

        MusicSynth synth;
        readonly object gate = new object();
        int announcedSeen = -1;

        // Fichiers de l'utilisateur
        readonly List<string> files = new List<string>();
        AudioSource fileSource;
        AudioLowPassFilter fileFilter;
        int fileIndex;

        void Awake()
        {
            Instance = this;
            int rate = AudioSettings.outputSampleRate;
            var engine = new MusicSynth(rate, Environment.TickCount);
            FindUserFiles();
            engine.Muted = UsingFiles;
            synth = engine;

            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("MusicCarrier", rate, 1, rate, false);
            src.loop = true;
            src.volume = 1f;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.Play();
        }

        // ------------------------------------------------------------------ commandes

        /// <summary>Volume (0-1) et étouffement (0 = ouvert, 1 = menu : filtré, lointain).</summary>
        public void Set(float vol, float muffled)
        {
            synth.Volume = vol;
            synth.Muffle = muffled;
            if (fileSource)
            {
                fileSource.volume = vol * 0.9f;
                fileFilter.cutoffFrequency = Mathf.Lerp(22000f, 700f, Mathf.Sqrt(Mathf.Clamp01(muffled)));
            }
        }

        /// <summary>Montée de tension (bruit qui s'ouvre, roulement de caisse claire) sur une durée donnée.</summary>
        public void Riser(float seconds) => synth.Riser(seconds);

        /// <summary>Le « drop » : cymbale, grosse caisse, et le morceau repart sur sa partie la plus énergique.</summary>
        public void Drop() => synth.Drop();

        /// <summary>Morceau suivant de la playlist (il démarre après son introduction, avec une cymbale).</summary>
        public void Next()
        {
            if (UsingFiles) { if (fileSource) fileSource.Stop(); }
            else synth.Next();
        }

        void Update()
        {
            int a = synth.Announced;
            if (a != announcedSeen && a >= 0 && !UsingFiles)
            {
                announcedSeen = a;
                NowPlaying = synth.TrackName(a);
                TrackChanged?.Invoke(NowPlaying);
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var engine = synth;
            if (engine == null) { Array.Clear(data, 0, data.Length); return; }
            lock (gate) engine.Process(data, channels);
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

        /// <summary>Rendu hors ligne (tests) : quelques secondes stéréo du morceau i à partir d'une mesure donnée.</summary>
        public static float[] RenderPreview(int trackNumber, int startBar, float seconds, out string title)
        {
            var engine = new MusicSynth(AudioSettings.outputSampleRate, 1) { Volume = 1f, Muffle = 0f };
            engine.Process(new float[engine.Rate * 2], 2);       // laisse l'étouffement du menu se dissiper
            engine.Seek(trackNumber, startBar);
            title = engine.TrackName(engine.Announced);
            var buffer = new float[Mathf.RoundToInt(seconds * engine.Rate) * 2];
            engine.Process(buffer, 2);
            return buffer;
        }
    }
}
