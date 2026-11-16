using System;

namespace NoHesi
{
    public enum Wave { Saw, Pulse, Sine, Tri }

    /// <summary>Réglage d'un instrument : deux oscillateurs (mélangés, ou B qui module A en FM), filtre, enveloppes.</summary>
    public sealed class Patch
    {
        public Wave a = Wave.Saw, b = Wave.Saw;
        public float ratioB = 1.006f, mixB = 1f;             // B : désaccord ou octave par rapport à A
        public float width = 0.5f, pwm, pwmRate = 0.3f;      // largeur d'impulsion et sa modulation lente
        public float fm, fmDecay = 0.25f, fmSustain = 0.2f;  // indice FM (> 0 : sinus A modulé par le sinus B)
        public float sub, noise;                             // sinus une octave dessous, souffle
        public float cutoff = 3000f, envAmt, envDecay = 0.15f, ampToCut, q = 1f;
        public float sweep, sweepRate = 0.03f;               // balayage lent du filtre (octaves, Hz)
        public float attack = 0.004f, decay = 0.4f, sustain = 1f, release = 0.1f;
        public float glide, vibrato, vibRate = 5.5f, vibDelay = 0.25f, drive;
        public float level = 0.12f, delay, reverb, pump = 1f;
        public float stereo = 1f;                            // accords : oscillateur A à gauche, B à droite (0 = mono)

        internal float attackInc, decayK, releaseK, envK, fmK, glideK, widthNow, cutNow, driveGain;

        internal void Prepare(int rate)
        {
            attackInc = 1f / (MathF.Max(attack, 0.0005f) * rate);
            decayK = K(decay, rate);
            releaseK = K(release, rate);
            envK = K(envDecay, rate);
            fmK = K(fmDecay, rate);
            glideK = glide > 0f ? 1f - K(glide, rate) : 1f;
            widthNow = width;
            cutNow = cutoff;
            driveGain = 1f + drive * 5f;
        }

        /// <summary>Coefficient par échantillon d'une décroissance exponentielle de constante de temps tau.</summary>
        internal static float K(float tau, int rate) => MathF.Exp(-1f / (MathF.Max(tau, 0.0005f) * rate));
    }

    /// <summary>Kit de batterie synthétique.</summary>
    public sealed class Kit
    {
        public float kickFrom = 140f, kickTo = 45f, kickSweep = 0.026f, kickDecay = 0.14f, kickClick = 0.15f, kickDrive, kickLevel = 0.55f;
        public float snareTone = 185f, snareToneDecay = 0.033f, snareNoiseDecay = 0.055f, snareGate, snareFloor = 0.36f;
        public float snareColor = 1f, snareBody = 0.25f, snareNoise = 0.35f, snareLevel = 1f, snareReverb = 0.5f;
        public float clapDecay = 0.09f, clapLevel = 0.4f;
        public float hatColor = 0.55f, hatDecay = 0.04f, hatOpen = 0.16f, hatLevel = 0.22f;
        public bool shaker;                                  // percussion : shaker (sinon rimshot)
        public float percLevel = 0.2f;

        internal float kickSweepK, kickK, snareToneK, snareNoiseK, snareColorGain, clapK, hatK, hatOpenK;

        internal void Prepare(int rate)
        {
            kickSweepK = Patch.K(kickSweep, rate);
            kickK = Patch.K(kickDecay, rate);
            snareToneK = Patch.K(snareToneDecay, rate);
            snareNoiseK = Patch.K(snareNoiseDecay, rate);
            snareColorGain = MathF.Sqrt((2f - snareColor) / snareColor);
            clapK = Patch.K(clapDecay, rate);
            hatK = Patch.K(hatDecay, rate);
            hatOpenK = Patch.K(hatOpen, rate);
        }
    }

    public sealed class Chord
    {
        public int degree;
        public int[] shape;
    }

    /// <summary>Motifs de batterie : une chaîne par mesure (« . » rien, « o » léger, « x » normal, « X » accent, « O » charleston ouvert).</summary>
    public sealed class DrumSet
    {
        public string[] kick, snare, clap, hat, perc;
    }

    /// <summary>Partie d'un morceau : quelles variantes de motifs jouent (-1 = muet), et les rampes de filtre et de volume.</summary>
    public sealed class Section
    {
        public int bars = 8;
        public int drums = -1, bass = -1, chords = -1, chords2 = -1, arp = -1, lead = -1;
        public float filterFrom = 1f, filterTo = 1f, fadeFrom = 1f, fadeTo = 1f;
        public bool roll, crash;
    }

    /// <summary>
    /// Un morceau. Les notes s'écrivent en degrés de la gamme : « 0 »-« 9 » puis « A »-« H » (10-17), « a »-« g » (-1 à -7),
    /// « - » tenue, « . » silence. Basse et arpège sont relatifs à la fondamentale de l'accord, la mélodie à la tonique.
    /// Grille : degré + forme (« 7 » septième, « 9 » neuvième sans fondamentale, « a » add9, « s » sus4, « 2 » sus2).
    /// </summary>
    public sealed class Track
    {
        public string name;
        public float bpm = 100f, swing;
        public int stepsPerBeat = 4;                         // 4 = doubles croches, 3 = ternaire (12/8)
        public int[] scale;
        public string prog;
        public int chordSteps = 16, fold = 4;                // durée d'un accord ; degrés ≥ fold joués une octave plus bas
        public int bassBase = 33, chordCentre = 60, chord2Centre = 60, arpBase = 57, leadBase = 57;   // tonique (MIDI) par instrument
        public bool arpStatic;                               // arpège relatif à la tonique (ostinato) et non à l'accord
        public Patch bassPatch, chordPatch, chord2Patch, arpPatch, leadPatch;
        public Kit kit = new Kit();
        public DrumSet[] drums;
        public string[][] bass, chords, chords2, arp, lead;
        public Section[] sections;
        public int dropSection = 2, entrySection = 1;
        public float delaySteps = 3f, delayFeedback = 0.38f, delayDamp = 0.3f, delayMix = 0.35f;
        public float reverbSize = 0.83f, reverbDamp = 0.3f, reverbMix = 0.28f;
        public float chorus, pump = 0.5f, pumpTime = 0.085f, lofi, tone = 18000f, gain = 1f;

        internal Chord[] chordList;
        internal int[] sectionStart;
        internal int totalBars, stepsPerBar;
        internal float duckK;
        internal Patch[] patches;

        static readonly int[] Triad = { 0, 2, 4 };

        internal void Prepare(int rate)
        {
            stepsPerBar = stepsPerBeat * 4;
            var tokens = prog.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            chordList = new Chord[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                string tk = tokens[i].Replace(":", "");
                var c = new Chord { degree = tk[0] - '0', shape = Triad };
                switch (tk.Length > 1 ? tk[1] : ' ')
                {
                    case '7': c.shape = new[] { 0, 2, 4, 6 }; break;
                    case '9': c.shape = new[] { 2, 4, 6, 8 }; break;
                    case 'a': c.shape = new[] { 0, 2, 4, 8 }; break;
                    case 's': c.shape = new[] { 0, 3, 4 }; break;
                    case '2': c.shape = new[] { 0, 1, 4 }; break;
                }
                chordList[i] = c;
            }
            Clean(bass, "basse"); Clean(chords, "accords"); Clean(chords2, "accords 2"); Clean(arp, "arpège"); Clean(lead, "mélodie");
            if (drums != null)
                foreach (var d in drums) { Clean(d.kick, "kick"); Clean(d.snare, "snare"); Clean(d.clap, "clap"); Clean(d.hat, "hat"); Clean(d.perc, "perc"); }

            sectionStart = new int[sections.Length];
            totalBars = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                var s = sections[i];
                sectionStart[i] = totalBars;
                totalBars += s.bars;
                Check(s.drums, drums?.Length ?? 0, "batterie"); Check(s.bass, bass?.Length ?? 0, "basse");
                Check(s.chords, chords?.Length ?? 0, "accords"); Check(s.chords2, chords2?.Length ?? 0, "accords 2");
                Check(s.arp, arp?.Length ?? 0, "arpège"); Check(s.lead, lead?.Length ?? 0, "mélodie");
            }
            patches = new[] { bassPatch ?? new Patch(), chordPatch ?? new Patch(), chord2Patch ?? new Patch(), arpPatch ?? new Patch(), leadPatch ?? new Patch() };
            bassPatch = patches[0]; chordPatch = patches[1]; chord2Patch = patches[2]; arpPatch = patches[3]; leadPatch = patches[4];
            foreach (var p in patches) p.Prepare(rate);
            kit.Prepare(rate);
            duckK = Patch.K(pumpTime, rate);
        }

        void Check(int index, int count, string what)
        {
            if (index >= count) throw new ArgumentException(name + " : variante de " + what + " " + index + " inexistante");
        }

        void Clean(string[][] variants, string what)
        {
            if (variants == null) return;
            foreach (var v in variants) Clean(v, what);
        }

        void Clean(string[] bars, string what)
        {
            if (bars == null) return;
            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = bars[i].Replace(" ", "");
                if (bars[i].Length != stepsPerBar)
                    throw new ArgumentException(name + " : " + what + " « " + bars[i] + " » fait " + bars[i].Length + " pas au lieu de " + stepsPerBar);
            }
        }

        internal int Midi(int tonic, int degree)
        {
            int o = degree >= 0 ? degree / 7 : -((6 - degree) / 7);
            return tonic + scale[degree - o * 7] + 12 * o;
        }
    }

    /// <summary>
    /// Synthétiseur et séquenceur de la musique du jeu, sans dépendance à Unity (rendu possible hors ligne).
    /// Les commandes (volume, montée, drop, suivant) viennent du thread principal ; Process tourne sur le thread audio.
    /// </summary>
    public sealed class MusicSynth
    {
        const float TwoPi = 6.2831853f;
        const float Tiny = 1e-18f;                           // évite les nombres dénormalisés dans les boucles de retour

        sealed class Voice
        {
            public float freq, target, pA, pB, pS, env, fenv, menv, age, s1, s2, vel, gl = 1f, gr = 1f;
            public int stage;                                // 0 muette, 1 attaque, 2 tenue, 3 relâchement
        }

        readonly int rate;
        readonly float dt;
        readonly Track[] tracks;

        public int TrackCount => tracks.Length;
        public string TrackName(int i) => tracks[i].name;
        public int Rate => rate;

        // Commandes du thread principal → thread audio
        public volatile float Volume = 0.6f, Muffle = 1f;
        /// <summary>Morceaux muets (fichiers de l'utilisateur en lecture) : il ne reste que la montée et le drop.</summary>
        public volatile bool Muted;
        volatile float riserSeconds = 3f;
        volatile int dropRequests, riserRequests, nextRequests, announced = -1;
        int dropSeen, riserSeen, nextSeen;
        /// <summary>Indice du morceau en cours (-1 avant le premier).</summary>
        public int Announced => announced;

        // ------------------------------------------------------------------ état du thread audio
        Track cur;
        int trackIndex, bar, step, sampleInStep, stepLen, secIndex, ctrl;
        uint noiseSeed = 22222;
        double time;
        float fade = 0.3f, filt = 0.4f, fadeTarget = 0.3f, filtTarget = 0.4f, wow = 1f;
        float smoothMuffle = 1f, lpL1, lpL2, lpR1, lpR2, lpA = 0.1f;

        readonly Voice bass = new Voice(), lead = new Voice();
        readonly Voice[] chordA = Voices(4, 0.6f), chordB = Voices(4, 0.45f), arp = Voices(4, 0.35f);
        readonly float[] chordStateA = new float[4], chordStateB = new float[4];
        int arpNext;

        // Batterie
        float kAmp, kVel, kSweep, kPhase, kClick, duck;
        float sT, sVel, sToneEnv, sNoiseEnv, sGateEnv, sPhase, sLp;
        bool sOn, cOn, pOn;
        float cT, cVel, cEnv, cS1, cS2;
        int cBurst;
        float hEnv, hVel, hK = 0.99f, hLp;
        float pT, pVel, pEnv, pLp;
        float crashEnv, crashLp;
        readonly float clickK, gateK, clapFastK, rimK, shakerK, crashK;
        // Montée
        float riserT = -1f, riserLen = 3f, riserS1, riserS2, riserFc = 300f, rollTimer;
        // Effets : chorus, écho ping-pong, réverbération
        readonly float[] chL = new float[4096], chR = new float[4096];
        int chPos;
        float chPhase;
        readonly float[] delayL, delayR;
        int delayPos, delayLen;
        float dampL, dampR;
        readonly float[][] combL = new float[4][], combR = new float[4][];
        readonly int[] combPosL = new int[4], combPosR = new int[4];
        readonly float[] combFiltL = new float[4], combFiltR = new float[4];
        readonly float[][] apL = new float[2][], apR = new float[2][];
        readonly int[] apPosL = new int[2], apPosR = new int[2];
        float crackle, crackleLp;

        static Voice[] Voices(int n, float spread)
        {
            var v = new Voice[n];
            for (int i = 0; i < n; i++)
            {
                float pan = (i % 2 == 0 ? -1f : 1f) * spread * (i < 2 ? 1f : 0.4f);
                v[i] = new Voice { gl = MathF.Min(1f, 1f - pan), gr = MathF.Min(1f, 1f + pan) };
            }
            return v;
        }

        public MusicSynth(int sampleRate, int shuffleSeed)
        {
            rate = sampleRate;
            dt = 1f / rate;
            clickK = Patch.K(0.0025f, rate);
            gateK = Patch.K(0.0167f, rate);
            clapFastK = Patch.K(0.004f, rate);
            rimK = Patch.K(0.007f, rate);
            shakerK = Patch.K(0.03f, rate);
            crashK = Patch.K(0.45f, rate);

            delayL = new float[rate * 2];
            delayR = new float[rate * 2];
            int[] combs = { 1116, 1188, 1277, 1356 };
            int[] aps = { 556, 441 };
            float k = rate / 44100f;
            for (int i = 0; i < 4; i++)
            {
                combL[i] = new float[(int)MathF.Round(combs[i] * k)];
                combR[i] = new float[(int)MathF.Round((combs[i] + 23) * k)];
            }
            for (int i = 0; i < 2; i++)
            {
                apL[i] = new float[(int)MathF.Round(aps[i] * k)];
                apR[i] = new float[(int)MathF.Round((aps[i] + 23) * k)];
            }

            // Playlist : ordre mélangé à chaque lancement
            var all = MusicTracks.Build();
            foreach (var t in all) t.Prepare(rate);
            var rng = new Random(shuffleSeed);
            var order = new System.Collections.Generic.List<Track>();
            foreach (var t in all) order.Insert(rng.Next(order.Count + 1), t);
            tracks = order.ToArray();
            StartTrack(0);
        }

        // ------------------------------------------------------------------ commandes

        /// <summary>Montée de tension (bruit qui s'ouvre, roulement de caisse claire) sur une durée donnée.</summary>
        public void Riser(float seconds) { riserSeconds = seconds; riserRequests++; }

        /// <summary>Le « drop » : cymbale, grosse caisse, et le morceau repart sur sa partie la plus énergique.</summary>
        public void Drop() => dropRequests++;

        /// <summary>Morceau suivant de la playlist (il démarre après son introduction, avec une cymbale).</summary>
        public void Next() => nextRequests++;

        /// <summary>Place la lecture au début d'une mesure d'un morceau (rendu hors ligne).</summary>
        public void Seek(int trackNumber, int startBar)
        {
            StartTrack(trackNumber);
            bar = Math.Min(Math.Max(startBar, 0), cur.totalBars - 1);
            Locate();
            fade = fadeTarget;
            filt = filtTarget;
        }

        public int BarsOf(int trackNumber) => tracks[trackNumber].totalBars;
        public int DropBarOf(int trackNumber) => tracks[trackNumber].sectionStart[tracks[trackNumber].dropSection];
        public float BarSecondsOf(int trackNumber) => 240f / tracks[trackNumber].bpm;

        // ------------------------------------------------------------------ séquenceur (thread audio)

        void StartTrack(int index)
        {
            trackIndex = ((index % tracks.Length) + tracks.Length) % tracks.Length;
            cur = tracks[trackIndex];
            bar = 0;
            step = 0;
            sampleInStep = 0;
            bass.stage = lead.stage = 0;
            foreach (var v in chordA) v.stage = 0;
            foreach (var v in chordB) v.stage = 0;
            foreach (var v in arp) v.stage = 0;
            float stepSamples = rate * 60f / cur.bpm / cur.stepsPerBeat;
            delayLen = Math.Min(Math.Max((int)MathF.Round(stepSamples * cur.delaySteps), 1), delayL.Length - 1);
            Locate();
            announced = trackIndex;
        }

        /// <summary>Retrouve la section de la mesure courante et recalcule la durée du pas.</summary>
        void Locate()
        {
            secIndex = 0;
            for (int i = cur.sections.Length - 1; i >= 0; i--)
                if (bar >= cur.sectionStart[i]) { secIndex = i; break; }
            SetStepLength();
            SectionTargets();
        }

        void SetStepLength()
        {
            float len = rate * 60f / cur.bpm / cur.stepsPerBeat;
            if (cur.swing > 0f && cur.stepsPerBeat == 4) len *= step % 2 == 0 ? 1f + cur.swing : 1f - cur.swing;
            stepLen = Math.Max(1, (int)MathF.Round(len));
        }

        void SectionTargets()
        {
            var sec = cur.sections[secIndex];
            float p = (bar - cur.sectionStart[secIndex] + step / (float)cur.stepsPerBar) / sec.bars;
            fadeTarget = sec.fadeFrom + (sec.fadeTo - sec.fadeFrom) * p;
            filtTarget = sec.filterFrom + (sec.filterTo - sec.filterFrom) * p;
        }

        static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

        static int Deg(char c) => c <= '9' ? c - '0' : c <= 'Z' ? c - 'A' + 10 : -(c - 'a' + 1);

        static float Vel(char c) => c == 'X' ? 1.15f : c == 'o' ? 0.5f : 1f;

        char At(string[] bars, int sectionBar) => bars[sectionBar % bars.Length][step];

        static void On(Voice v, Patch p, int midi, float vel)
        {
            v.target = Hz(midi);
            if (p.glide <= 0f || v.stage == 0 || v.stage == 3) { v.freq = v.target; v.age = 0f; }
            if (v.stage == 0) { v.env = 0f; v.s1 = v.s2 = 0f; }
            v.stage = 1;
            v.fenv = 1f;
            v.menv = 1f;
            v.vel = vel;
        }

        static void Off(Voice v)
        {
            if (v.stage == 1 || v.stage == 2) v.stage = 3;
        }

        void PlayChord(Voice[] voices, Patch p, Chord chord, int centre, float vel)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (i >= chord.shape.Length) { Off(voices[i]); continue; }
                int n = cur.Midi(cur.bassBase, chord.degree + chord.shape[i]);
                while (n < centre - 6) n += 12;
                while (n >= centre + 6) n -= 12;
                On(voices[i], p, n, vel);
            }
        }

        void Kick(float vel) { kAmp = vel; kVel = vel; kSweep = 1f; kPhase = 0f; kClick = 1f; duck = 1f; }
        void Snare(float vel) { sT = 0f; sVel = vel; sToneEnv = 1f; sNoiseEnv = 1f; sGateEnv = 1f; sPhase = 0f; sOn = true; }

        void OnStep()
        {
            var t = cur;
            var sec = t.sections[secIndex];
            int sb = bar - t.sectionStart[secIndex];
            var chord = t.chordList[(sb * t.stepsPerBar + step) / t.chordSteps % t.chordList.Length];
            int rootDeg = chord.degree >= t.fold ? chord.degree - 7 : chord.degree;
            float vel = step % t.stepsPerBeat == 0 ? 1f : 0.8f;
            char c;

            // Batterie
            if (sec.crash && sb == 0 && step == 0) crashEnv = 1f;
            if (sec.drums >= 0)
            {
                var d = t.drums[sec.drums];
                if (d.kick != null && (c = At(d.kick, sb)) != '.') Kick(Vel(c));
                if (d.snare != null && (c = At(d.snare, sb)) != '.') Snare(Vel(c));
                if (d.clap != null && (c = At(d.clap, sb)) != '.') { cT = 0f; cVel = Vel(c); cBurst = 0; cEnv = 0f; cOn = true; }
                if (d.hat != null && (c = At(d.hat, sb)) != '.') { hEnv = 1f; hVel = c == 'O' ? 0.9f : Vel(c); hK = c == 'O' ? t.kit.hatOpenK : t.kit.hatK; }
                if (d.perc != null && (c = At(d.perc, sb)) != '.') { pT = 0f; pVel = Vel(c); pEnv = 1f; pOn = true; }
            }
            // Roulement de caisse claire sur la dernière demi-mesure de la section
            int half = t.stepsPerBar / 2;
            if (sec.roll && sb == sec.bars - 1 && step >= half) Snare(0.35f + (step - half) / (float)half * 0.65f);

            // Basse
            if (sec.bass >= 0)
            {
                c = At(t.bass[sec.bass], sb);
                if (c == '.') Off(bass);
                else if (c != '-') On(bass, t.bassPatch, t.Midi(t.bassBase, rootDeg + Deg(c)), vel);
            }
            else Off(bass);

            // Accords
            if (sec.chords >= 0)
            {
                c = At(t.chords[sec.chords], sb);
                if (c == '.') foreach (var v in chordA) Off(v);
                else if (c != '-') PlayChord(chordA, t.chordPatch, chord, t.chordCentre, Vel(c));
            }
            else foreach (var v in chordA) Off(v);
            if (sec.chords2 >= 0)
            {
                c = At(t.chords2[sec.chords2], sb);
                if (c == '.') foreach (var v in chordB) Off(v);
                else if (c != '-') PlayChord(chordB, t.chord2Patch, chord, t.chord2Centre, Vel(c));
            }
            else foreach (var v in chordB) Off(v);

            // Arpège / séquence : les notes sonnent jusqu'au bout sur quatre voix tournantes
            if (sec.arp >= 0)
            {
                c = At(t.arp[sec.arp], sb);
                if (c != '.' && c != '-')
                {
                    var v = arp[arpNext];
                    arpNext = (arpNext + 1) % arp.Length;
                    On(v, t.arpPatch, t.Midi(t.arpBase, (t.arpStatic ? 0 : rootDeg) + Deg(c)), vel);
                }
            }

            // Mélodie
            if (sec.lead >= 0)
            {
                c = At(t.lead[sec.lead], sb);
                if (c == '.') Off(lead);
                else if (c != '-') On(lead, t.leadPatch, t.Midi(t.leadBase, Deg(c)), 1f);
            }
            else Off(lead);
        }

        // ------------------------------------------------------------------ synthèse

        float Noise()
        {
            noiseSeed ^= noiseSeed << 13; noiseSeed ^= noiseSeed >> 17; noiseSeed ^= noiseSeed << 5;
            return (noiseSeed & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        /// <summary>sin(2π·phase) approché (parabole corrigée, erreur &lt; 0,1 %), phase dans [0, 1[.</summary>
        static float Sin01(float phase)
        {
            float x = phase < 0.5f ? phase : phase - 1f;
            float y = x * (8f - 16f * (x < 0f ? -x : x));
            return y * (0.775f + 0.225f * (y < 0f ? -y : y));
        }

        /// <summary>tanh approché (Padé), suffisant pour une saturation douce.</summary>
        static float Soft(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        static float Osc(Wave w, ref float phase, float inc, float width)
        {
            phase += inc;
            if (phase >= 1f) phase -= 1f;
            switch (w)
            {
                case Wave.Saw: return 2f * phase - 1f - Blep(phase, inc);
                case Wave.Pulse:
                    float v = phase < width ? 1f : -1f;
                    v += Blep(phase, inc);
                    float t2 = phase - width; if (t2 < 0f) t2 += 1f;
                    return v - Blep(t2, inc);
                case Wave.Tri: return 4f * MathF.Abs(phase - 0.5f) - 1f;
                default: return Sin01(phase);
            }
        }

        /// <summary>Filtre à variable d'état (passe-bas en retour, passe-bande en sortie).</summary>
        float Svf(float x, float fc, float q, ref float s1, ref float s2, out float band)
        {
            float w = MathF.Min(MathF.PI * fc * dt, 1.25f);
            float w2 = w * w;
            float g = w * (1f + w2 * (0.33333f + 0.13333f * w2));
            float k = 1f / q;
            float v1 = (s1 + g * (x - s2)) / (1f + g * (g + k));
            float v2 = s2 + g * v1;
            s1 = 2f * v1 - s1;
            s2 = 2f * v2 - s2;
            band = v1;
            return v2;
        }

        /// <summary>Enveloppes et oscillateurs d'une voix, avant filtre : renvoie A (ou la voix FM entière), et B à part.</summary>
        float Raw(Voice v, Patch p, out float oscB)
        {
            oscB = 0f;
            switch (v.stage)
            {
                case 0: return 0f;
                case 1:
                    v.env += p.attackInc;
                    if (v.env >= 1f) { v.env = 1f; v.stage = 2; }
                    break;
                case 2:
                    v.env = p.sustain + (v.env - p.sustain) * p.decayK;
                    if (p.sustain <= 0f && v.env < 0.0004f) { v.stage = 0; return 0f; }
                    break;
                default:
                    v.env *= p.releaseK;
                    if (v.env < 0.0004f) { v.stage = 0; return 0f; }
                    break;
            }
            v.fenv = v.fenv * p.envK + 1e-9f;        // le plancher évite les nombres dénormalisés (très lents)
            v.menv = v.menv * p.fmK + 1e-9f;
            if (p.glide > 0f) v.freq += (v.target - v.freq) * p.glideK; else v.freq = v.target;
            v.age += dt;
            float f = v.freq * wow;
            if (p.vibrato > 0f)
            {
                float d = (v.age - p.vibDelay) * 3f;
                if (d > 0f)
                {
                    float ph = p.vibRate * v.age;
                    f *= 1f + Sin01(ph - (int)ph) * p.vibrato * (d > 1f ? 1f : d);
                }
            }
            float inc = f * dt, x;
            float amp = v.env * v.vel;
            if (p.fm > 0f)
            {
                v.pB += inc * p.ratioB; if (v.pB >= 1f) v.pB -= (int)v.pB;
                v.pA += inc; if (v.pA >= 1f) v.pA -= 1f;
                float index = p.fm * (p.fmSustain + (1f - p.fmSustain) * v.menv);
                float ph = v.pA + 8f + index * 0.159155f * Sin01(v.pB);
                x = Sin01(ph - (int)ph);
            }
            else
            {
                x = Osc(p.a, ref v.pA, inc, p.widthNow);
                if (p.mixB > 0f) oscB = p.mixB * Osc(p.b, ref v.pB, inc * p.ratioB, p.widthNow) * amp;
            }
            if (p.sub > 0f)
            {
                v.pS += inc * 0.5f; if (v.pS >= 1f) v.pS -= 1f;
                x += p.sub * Sin01(v.pS);
            }
            if (p.noise > 0f) x += p.noise * Noise();
            return x * amp;
        }

        /// <summary>Voix monophonique complète (basse, arpège, mélodie) : oscillateurs, filtre, saturation.</summary>
        float Render(Voice v, Patch p)
        {
            if (v.stage == 0) return 0f;
            float x = Raw(v, p, out float b) + b;
            if (v.stage == 0) return 0f;
            float fc = p.cutNow * (0.3f + 0.7f * filt) + (p.envAmt * v.fenv + p.ampToCut * v.env) * filt;
            x = Svf(x, MathF.Min(fc, 16000f), p.q, ref v.s1, ref v.s2, out _);
            if (p.drive > 0f) x = Soft(x * p.driveGain);
            return x * p.level;
        }

        /// <summary>
        /// Accord : les voix partagent un filtre par canal ; l'oscillateur A part à gauche et B à droite (nappes larges),
        /// les voix FM sont réparties par panoramique.
        /// </summary>
        void RenderChord(Voice[] voices, Patch p, float[] st, out float l, out float r)
        {
            l = 0f; r = 0f;
            float fenv = 0f, env = 0f, cross = 1f - p.stereo;
            for (int n = 0; n < voices.Length; n++)
            {
                var v = voices[n];
                if (v.stage == 0) continue;
                float a = Raw(v, p, out float b);
                if (p.fm > 0f) { l += a * v.gl; r += a * v.gr; }
                else
                {
                    float gl = 1f - (1f - v.gl) * cross, gr = 1f - (1f - v.gr) * cross;
                    l += (a + b * cross) * gl; r += (b + a * cross) * gr;
                }
                if (v.fenv > fenv) fenv = v.fenv;
                if (v.env > env) env = v.env;
            }
            if (env <= 0f && MathF.Abs(st[0]) + MathF.Abs(st[2]) < 1e-6f) return;
            float fc = MathF.Min(p.cutNow * (0.3f + 0.7f * filt) + (p.envAmt * fenv + p.ampToCut * env) * filt, 16000f);
            l = Svf(l + Tiny, fc, p.q, ref st[0], ref st[1], out _);
            r = Svf(r + Tiny, fc, p.q, ref st[2], ref st[3], out _);
            if (p.drive > 0f) { l = Soft(l * p.driveGain); r = Soft(r * p.driveGain); }
            l *= p.level; r *= p.level;
        }

        static float Tap(float[] buffer, int pos, float delaySamples)
        {
            float p = pos - delaySamples + buffer.Length;
            int i = (int)p;
            float frac = p - i;
            float a = buffer[i & (buffer.Length - 1)], b = buffer[(i + 1) & (buffer.Length - 1)];
            return a + (b - a) * frac;
        }

        /// <summary>Mise à jour lente (toutes les 32 trames) : étouffement, pleurage, modulations des instruments, montée.</summary>
        void Control(Track t)
        {
            time += 32.0 * dt;
            smoothMuffle += (Muffle - smoothMuffle) * 0.01f;
            float fc = MathF.Min(t.tone, 18000f);
            fc += (650f - fc) * MathF.Sqrt(MathF.Min(MathF.Max(smoothMuffle, 0f), 1f));
            lpA = 1f - MathF.Exp(-TwoPi * fc * dt);
            wow = t.lofi > 0f
                ? 1f + t.lofi * (0.0022f * (float)Math.Sin(TwoPi * 0.6 * time) + 0.0006f * (float)Math.Sin(TwoPi * 7.1 * time))
                : 1f;
            foreach (var p in t.patches)
            {
                if (p.pwm > 0f) p.widthNow = MathF.Min(MathF.Max(p.width + p.pwm * (float)Math.Sin(TwoPi * p.pwmRate * time), 0.08f), 0.92f);
                if (p.sweep > 0f) p.cutNow = p.cutoff * MathF.Pow(2f, p.sweep * (float)Math.Sin(TwoPi * p.sweepRate * time));
            }
            if (riserT >= 0f) riserFc = 300f * MathF.Pow(20f, MathF.Min(riserT / riserLen, 1f));
        }

        public void Process(float[] data, int channels)
        {
            float vol = Volume;
            bool muted = Muted;
            if (riserRequests != riserSeen) { riserSeen = riserRequests; riserT = 0f; riserLen = MathF.Max(0.5f, riserSeconds); rollTimer = 0f; }
            if (nextRequests != nextSeen)
            {
                nextSeen = nextRequests;
                StartTrack(trackIndex + 1);
                bar = cur.sectionStart[Math.Min(cur.entrySection, cur.sections.Length - 1)];
                Locate();
                crashEnv = 1f;
            }
            if (dropRequests != dropSeen)
            {
                dropSeen = dropRequests;
                riserT = -1f;
                bar = cur.sectionStart[Math.Min(cur.dropSection, cur.sections.Length - 1)];
                step = 0;
                Locate();
                fade = fadeTarget;
                filt = filtTarget;
                if (!muted) OnStep();
                sampleInStep = 1;
                crashEnv = 1f;
                Kick(1.2f);
            }
            var t = cur;
            var kit = t.kit;

            for (int i = 0; i < data.Length; i += channels)
            {
                // ---- séquenceur
                if (sampleInStep == 0 && !muted) OnStep();
                sampleInStep++;
                if (sampleInStep >= stepLen)
                {
                    sampleInStep = 0;
                    step++;
                    if (step >= t.stepsPerBar)
                    {
                        step = 0;
                        bar++;
                        if (bar >= t.totalBars) { StartTrack(trackIndex + 1); t = cur; kit = t.kit; }
                        else if (secIndex + 1 < t.sections.Length && bar >= t.sectionStart[secIndex + 1]) secIndex++;
                    }
                    SetStepLength();
                    SectionTargets();
                }
                if (--ctrl <= 0) { ctrl = 32; Control(t); }
                fade += (fadeTarget - fade) * 0.002f;
                filt += (filtTarget - filt) * 0.002f;

                float L = 0f, R = 0f, rev = 0f, del = 0f;
                float pump = t.pump * duck;
                duck = duck > 1e-5f ? duck * t.duckK : 0f;

                // ---- grosse caisse : sinus à hauteur descendante + attaque
                if (kAmp > 0.0005f)
                {
                    kSweep *= kit.kickSweepK;
                    kPhase += (kit.kickTo + (kit.kickFrom - kit.kickTo) * kSweep) * dt;
                    if (kPhase >= 1f) kPhase -= 1f;
                    float k = Sin01(kPhase) * kAmp;
                    if (kClick > 0.001f) { k += Noise() * kClick * kit.kickClick * kVel; kClick *= clickK; }
                    if (kit.kickDrive > 0f) k = Soft(k * (1f + kit.kickDrive * 3f));
                    kAmp *= kit.kickK;
                    k *= kit.kickLevel;
                    L += k; R += k;
                }

                // ---- caisse claire : corps + bruit, queue « gated » (coupée net) selon le kit
                if (sOn)
                {
                    sToneEnv *= kit.snareToneK;
                    sNoiseEnv *= kit.snareNoiseK;
                    sPhase += kit.snareTone * dt; if (sPhase >= 1f) sPhase -= 1f;
                    float n = Noise();
                    sLp += (n - sLp) * kit.snareColor;
                    float amp;
                    if (kit.snareGate > 0f)
                    {
                        if (sT > kit.snareGate) sGateEnv *= gateK;
                        amp = (kit.snareFloor + (1f - kit.snareFloor) * sNoiseEnv) * sGateEnv;
                        if (sGateEnv < 0.001f) sOn = false;
                    }
                    else
                    {
                        amp = sNoiseEnv;
                        if (sNoiseEnv < 0.001f) sOn = false;
                    }
                    float s = (Sin01(sPhase) * sToneEnv * kit.snareBody + sLp * kit.snareColorGain * amp * kit.snareNoise) * sVel * kit.snareLevel;
                    L += s; R += s;
                    rev += s * kit.snareReverb;
                    sT += dt;
                }

                // ---- clap : trois salves serrées puis la queue
                if (cOn)
                {
                    if (cBurst < 4 && cT >= cBurst * 0.011f) { cEnv = cBurst == 3 ? 1f : 0.8f; cBurst++; }
                    cEnv *= cBurst < 4 ? clapFastK : kit.clapK;
                    Svf(Noise(), 1300f, 1.4f, ref cS1, ref cS2, out float band);
                    float c = band * cEnv * cVel * kit.clapLevel * 2f;
                    L += c; R += c;
                    rev += c * 0.5f;
                    cT += dt;
                    if (cBurst >= 4 && cEnv < 0.001f) cOn = false;
                }

                // ---- charleston
                if (hEnv > 0.0005f)
                {
                    float n = Noise();
                    float hp = n - hLp; hLp += (n - hLp) * kit.hatColor;
                    float h = hp * hEnv * hVel * kit.hatLevel;
                    hEnv *= hK;
                    L += h * 0.8f; R += h;
                }

                // ---- percussion : shaker ou rimshot
                if (pOn)
                {
                    float x;
                    if (kit.shaker)
                    {
                        float a = pT < 0.008f ? pT / 0.008f : pEnv;
                        if (pT >= 0.008f) pEnv *= shakerK;
                        float n = Noise();
                        float hp = n - pLp; pLp += (n - pLp) * 0.75f;
                        x = hp * a;
                    }
                    else
                    {
                        pEnv *= rimK;
                        x = (MathF.Sin(TwoPi * 1740f * pT) + 0.6f * MathF.Sin(TwoPi * 480f * pT)) * pEnv;
                    }
                    x *= pVel * kit.percLevel;
                    L += x; R += x * 0.7f;
                    rev += x * 0.3f;
                    pT += dt;
                    if (pEnv < 0.001f) pOn = false;
                }

                // ---- cymbale (drop, débuts de partie)
                if (crashEnv > 0.0005f)
                {
                    float n = Noise();
                    float hp = n - crashLp; crashLp += (n - crashLp) * 0.4f;
                    float c = hp * crashEnv * 0.22f;
                    crashEnv *= crashK;
                    L += c; R += c * 0.9f;
                    rev += c * 0.3f;
                }

                // ---- basse
                var bp = t.bassPatch;
                float o = Render(bass, bp);
                if (o != 0f)
                {
                    o *= 1f - bp.pump * pump;
                    L += o; R += o;
                    rev += o * bp.reverb;
                }

                // ---- accords (deux instruments), élargis par le chorus
                var cp = t.chordPatch;
                RenderChord(chordA, cp, chordStateA, out float cl, out float cr);
                float g = 1f - cp.pump * pump;
                cl *= g; cr *= g;
                rev += (cl + cr) * 0.5f * cp.reverb; del += (cl + cr) * 0.5f * cp.delay;
                cp = t.chord2Patch;
                RenderChord(chordB, cp, chordStateB, out float c2l, out float c2r);
                g = 1f - cp.pump * pump;
                cl += c2l * g; cr += c2r * g;
                rev += (c2l + c2r) * 0.5f * g * cp.reverb; del += (c2l + c2r) * 0.5f * g * cp.delay;
                if (t.chorus > 0f)
                {
                    chL[chPos] = cl + Tiny; chR[chPos] = cr + Tiny;
                    chPhase += 0.6f * dt; if (chPhase >= 1f) chPhase -= 1f;
                    float tri = 4f * MathF.Abs(chPhase - 0.5f) - 1f;
                    float wl = Tap(chL, chPos, (0.011f + 0.0035f * tri) * rate);
                    float wr = Tap(chR, chPos, (0.011f - 0.0035f * tri) * rate);
                    chPos = (chPos + 1) & (chL.Length - 1);
                    float norm = 1f / (1f + 0.5f * t.chorus);
                    cl = (cl + wl * t.chorus) * norm;
                    cr = (cr + wr * t.chorus) * norm;
                }
                L += cl; R += cr;

                // ---- arpège / séquence
                var ap = t.arpPatch;
                g = 1f - ap.pump * pump;
                for (int n = 0; n < arp.Length; n++)
                {
                    o = Render(arp[n], ap);
                    if (o == 0f) continue;
                    o *= g;
                    L += o * arp[n].gl; R += o * arp[n].gr;
                    rev += o * ap.reverb; del += o * ap.delay;
                }

                // ---- mélodie
                var lp = t.leadPatch;
                o = Render(lead, lp);
                if (o != 0f)
                {
                    L += o; R += o;
                    rev += o * lp.reverb; del += o * lp.delay;
                }

                // ---- écho ping-pong, répétitions de plus en plus sourdes
                int rd = delayPos - delayLen; if (rd < 0) rd += delayL.Length;
                float dl = delayL[rd], dr = delayR[rd];
                dampL += (dr - dampL) * (1f - t.delayDamp);
                dampR += (dl - dampR) * (1f - t.delayDamp);
                delayL[delayPos] = del + dampL * t.delayFeedback + Tiny;
                delayR[delayPos] = dampR * t.delayFeedback + Tiny;
                delayPos++; if (delayPos >= delayL.Length) delayPos = 0;
                L += dl * t.delayMix; R += dr * t.delayMix;

                // ---- réverbération (type Freeverb allégé)
                float rin = rev * 0.2f + Tiny;
                float revL = 0f, revR = 0f;
                for (int c = 0; c < 4; c++)
                {
                    float[] bl = combL[c], br = combR[c];
                    float ol = bl[combPosL[c]], or = br[combPosR[c]];
                    combFiltL[c] = ol * (1f - t.reverbDamp) + combFiltL[c] * t.reverbDamp;
                    combFiltR[c] = or * (1f - t.reverbDamp) + combFiltR[c] * t.reverbDamp;
                    bl[combPosL[c]] = rin + combFiltL[c] * t.reverbSize;
                    br[combPosR[c]] = rin + combFiltR[c] * t.reverbSize;
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
                L += revL * t.reverbMix; R += revR * t.reverbMix;

                // ---- vinyle (lo-fi) : craquements épars et souffle
                if (t.lofi > 0f)
                {
                    float n = Noise();
                    if ((noiseSeed >> 8 & 0x3FFF) < 2) crackle = n;
                    crackleLp += (crackle - crackleLp) * 0.35f;
                    crackle = crackle * 0.8f + Tiny;
                    float c = (crackleLp * 0.3f + n * 0.004f) * t.lofi;
                    L += c; R += c * 0.8f;
                }

                // ---- fondu de section et niveau du morceau
                float mix = (muted ? 1f : fade) * t.gain;
                L *= mix; R *= mix;

                // ---- montée : bruit dont le filtre s'ouvre + roulement de caisse claire qui accélère
                if (riserT >= 0f)
                {
                    float x = MathF.Min(riserT / riserLen, 1f);
                    Svf(Noise(), riserFc, 2.5f, ref riserS1, ref riserS2, out float band);
                    float r = band * x * x * 0.5f;
                    L += r; R += r;
                    if (x > 0.45f)
                    {
                        rollTimer -= dt;
                        if (rollTimer <= 0f)
                        {
                            Snare(0.3f + x * 0.5f);
                            float stepSec = 15f / t.bpm;
                            rollTimer = stepSec * (1f - 0.65f * (x - 0.45f) / 0.55f);
                        }
                    }
                    riserT += dt;
                    if (riserT > riserLen + 1.5f) riserT = -1f;
                }

                // ---- bus maître : étouffement (menu), saturation douce
                L += Tiny; R += Tiny;
                lpL1 += (L - lpL1) * lpA; lpL2 += (lpL1 - lpL2) * lpA;
                lpR1 += (R - lpR1) * lpA; lpR2 += (lpR1 - lpR2) * lpA;
                float oL = Soft(lpL2 * 1.1f) * vol;
                float oR = Soft(lpR2 * 1.1f) * vol;
                if (channels == 1) data[i] = (oL + oR) * 0.5f;
                else
                {
                    data[i] = oL;
                    data[i + 1] = oR;
                    for (int c = 2; c < channels; c++) data[i + c] = 0f;
                }
            }
        }
    }
}
