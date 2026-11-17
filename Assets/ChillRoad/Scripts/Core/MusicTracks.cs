namespace ChillRoad
{
    /// <summary>
    /// Les huit morceaux du jeu, écrits à la main : chacun a son style, son tempo, sa gamme, ses instruments, sa batterie,
    /// sa grille, sa mélodie et sa structure (voir Track pour la notation des motifs).
    /// </summary>
    public static class MusicTracks
    {
        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };

        static string[] P(params string[] bars) => bars;

        public static Track[] Build() => new[]
        {
            NeonOverdrive(), MidnightMile(), ChromeHorizon(), Afterglow(),
            SunsetInterstate(), VioletTailLights(), AnalogDreams(), NightShiftCruise(),
        };

        // ------------------------------------------------------------------ 1. Outrun : synthwave classique, La mineur
        static Track NeonOverdrive() => new Track
        {
            name = "Neon Overdrive",
            bpm = 118f, scale = Minor, prog = "0 5 2 6 0 5 3 6",
            bassBase = 33, chordCentre = 60, arpBase = 57, leadBase = 57,
            bassPatch = new Patch { cutoff = 140f, envAmt = 2200f, envDecay = 0.045f, q = 1.4f, decay = 0.06f, sustain = 0.5f, release = 0.05f, level = 0.17f },
            chordPatch = new Patch { ratioB = 1.008f, cutoff = 4700f, q = 0.9f, attack = 0.35f, release = 0.6f, glide = 0.035f, level = 0.1f, reverb = 0.4f, pump = 0.8f },
            arpPatch = new Patch { a = Wave.Pulse, width = 0.25f, ratioB = 0.997f, mixB = 0.43f, cutoff = 800f, envAmt = 5200f, envDecay = 0.17f, q = 1.8f, decay = 0.17f, sustain = 0f, level = 0.14f, delay = 0.9f },
            leadPatch = new Patch { b = Wave.Pulse, ratioB = 0.5f, mixB = 0.67f, cutoff = 3400f, q = 1.2f, attack = 0.005f, release = 0.035f, glide = 0.014f, vibrato = 0.004f, level = 0.1f, delay = 0.6f, reverb = 0.4f },
            kit = new Kit { snareGate = 0.26f },
            drums = new[]
            {
                new DrumSet { kick = P("x.......x......."), hat = P("..x...x...x...x.") },
                new DrumSet { kick = P("x...x...x...x..."), snare = P("....x.......x..."), hat = P("o.x.o.x.o.x.o.x.") },
            },
            bass = new[] { P("0-0-0-0-0-0-0-7-", "0-0-0-0-0-0-0-7-", "0-0-0-0-0-0-0-7-", "0-0-0-0-0-0-4-7-") },
            chords = new[] { P("x---------------") },
            arp = new[] { P("0479747904797479") },
            lead = new[]
            {
                P("4-----2-4---7---", "9-----7-5---7---", "9-----6-4---6---", "8-------6-8-A---",
                  "B-----9-7---9---", "9-----7-5---7---", "A-----7-5---3---", "6-------8-6-4---"),
            },
            sections = new[]
            {
                new Section { bars = 8, chords = 0, arp = 0, filterFrom = 0.35f, filterTo = 0.6f, fadeFrom = 0.3f },
                new Section { bars = 8, chords = 0, arp = 0, bass = 0, drums = 0, roll = true, filterFrom = 0.55f, filterTo = 0.9f },
                new Section { bars = 16, chords = 0, arp = 0, bass = 0, drums = 1, crash = true },
                new Section { bars = 8, chords = 0, arp = 0, bass = 0, roll = true, filterFrom = 0.6f, filterTo = 0.85f },
                new Section { bars = 16, chords = 0, arp = 0, bass = 0, drums = 1, lead = 0, crash = true },
                new Section { bars = 8, chords = 0, arp = 0, drums = 0, filterTo = 0.4f, fadeTo = 0f },
            },
            chorus = 0.35f, gain = 0.9f,
        };

        // ------------------------------------------------------------------ 2. Lo-fi : chillhop traînant, Do majeur jazzy
        static Track MidnightMile() => new Track
        {
            name = "Midnight Mile",
            bpm = 78f, swing = 0.2f, scale = Major, prog = "1:9 4:7 0:9 5:9",
            bassBase = 36, chordCentre = 62, leadBase = 72,
            bassPatch = new Patch { fm = 1.5f, fmDecay = 0.12f, fmSustain = 0.45f, ratioB = 1f, cutoff = 1100f, q = 0.7f, attack = 0.006f, decay = 0.5f, sustain = 0.6f, release = 0.08f, level = 0.27f, pump = 0.6f },
            chordPatch = new Patch { fm = 1.7f, fmDecay = 0.35f, fmSustain = 0.12f, ratioB = 1f, cutoff = 3200f, q = 0.7f, attack = 0.003f, decay = 1.6f, sustain = 0f, release = 0.35f, level = 0.13f, reverb = 0.25f, delay = 0.1f },
            leadPatch = new Patch { fm = 1.1f, fmDecay = 0.06f, fmSustain = 0.05f, ratioB = 4f, cutoff = 6000f, attack = 0.002f, decay = 0.7f, sustain = 0f, release = 0.4f, level = 0.15f, delay = 0.5f, reverb = 0.5f },
            kit = new Kit
            {
                kickFrom = 110f, kickTo = 48f, kickSweep = 0.03f, kickDecay = 0.18f, kickClick = 0.03f, kickLevel = 0.5f,
                snareTone = 210f, snareToneDecay = 0.025f, snareNoiseDecay = 0.06f, snareColor = 0.25f, snareLevel = 0.85f, snareReverb = 0.3f,
                hatColor = 0.35f, hatDecay = 0.025f, hatOpen = 0.09f, hatLevel = 0.13f, percLevel = 0.16f,
            },
            drums = new[]
            {
                new DrumSet { hat = P("x.x.x.x.x.x.x.x."), perc = P("....x.......x...") },
                new DrumSet { kick = P("x.........x.....", "x......x..x....."), snare = P("....x.......x..."), hat = P("x.xox.x.x.xox.xo", "x.x.x.xox.xox.x.") },
            },
            bass = new[] { P("0-----....0-....", "0-----....4-7-..") },
            chords = new[] { P("x-----x---------", "x---------x-----") },
            lead = new[]
            {
                P("....4-5-7---5-4-", "5---4---1-------", "....2-4-6---4-2-", "1---0---a-------",
                  "....7-8-9---7-5-", "8---6---4-------", "....4-6-8---9-8-", "7-------4-------"),
            },
            sections = new[]
            {
                new Section { bars = 4, chords = 0, filterFrom = 0.4f, filterTo = 0.8f, fadeFrom = 0.5f },
                new Section { bars = 8, chords = 0, bass = 0, drums = 0 },
                new Section { bars = 16, chords = 0, bass = 0, drums = 1, lead = 0 },
                new Section { bars = 4, chords = 0, bass = 0, filterFrom = 0.6f, filterTo = 0.9f },
                new Section { bars = 16, chords = 0, bass = 0, drums = 1, lead = 0 },
                new Section { bars = 4, chords = 0, drums = 0, filterTo = 0.5f, fadeTo = 0f },
            },
            delayFeedback = 0.3f, delayDamp = 0.6f, delayMix = 0.3f, reverbSize = 0.8f, reverbDamp = 0.5f, reverbMix = 0.22f,
            chorus = 0.3f, pump = 0.35f, lofi = 1f, tone = 6500f, gain = 1.2f,
        };

        // ------------------------------------------------------------------ 3. Italo disco : basse galopante, Fa# mineur
        static Track ChromeHorizon() => new Track
        {
            name = "Chrome Horizon",
            bpm = 124f, scale = Minor, prog = "0 5 6 4", chordSteps = 32,
            bassBase = 42, chordCentre = 66, arpBase = 66, leadBase = 66,
            bassPatch = new Patch { a = Wave.Pulse, width = 0.35f, ratioB = 1.004f, mixB = 0.6f, cutoff = 260f, envAmt = 2600f, envDecay = 0.06f, q = 1.8f, decay = 0.09f, sustain = 0.35f, release = 0.03f, level = 0.17f },
            chordPatch = new Patch { ratioB = 2.004f, mixB = 0.5f, stereo = 0.35f, cutoff = 5200f, attack = 0.18f, release = 0.45f, level = 0.07f, reverb = 0.5f, pump = 0.7f },
            arpPatch = new Patch { ratioB = 1.007f, mixB = 0.8f, cutoff = 1200f, envAmt = 6000f, envDecay = 0.08f, q = 1.5f, decay = 0.11f, sustain = 0f, level = 0.1f, delay = 0.5f },
            leadPatch = new Patch { a = Wave.Pulse, b = Wave.Pulse, pwm = 0.12f, pwmRate = 0.8f, ratioB = 1.004f, mixB = 0.5f, cutoff = 4200f, decay = 0.2f, sustain = 0.85f, release = 0.05f, glide = 0.008f, vibrato = 0.005f, vibDelay = 0.15f, level = 0.085f, delay = 0.5f, reverb = 0.3f },
            kit = new Kit
            {
                kickFrom = 150f, kickTo = 50f, kickSweep = 0.02f, kickDecay = 0.11f, kickClick = 0.25f, kickLevel = 0.58f,
                hatColor = 0.7f, hatDecay = 0.03f, hatOpen = 0.12f, hatLevel = 0.2f,
            },
            drums = new[]
            {
                new DrumSet { kick = P("x...x...x...x..."), hat = P("..O...O...O...O.") },
                new DrumSet { kick = P("x...x...x...x..."), clap = P("....x.......x..."), hat = P("ooOoooOoooOoooOo") },
            },
            bass = new[] { P("0-770-770-770-77") },
            chords = new[] { P("x---------------") },
            arp = new[] { P("0240240240240247", "2402402402402479") },
            lead = new[]
            {
                P("4.4.4.2.4---7---", "8-7-4-----......", "5.5.5.4.5---9---", "8-7-5-----......",
                  "6.6.6.4.6---A---", "B-A-8-----......", "B-----8-6---4---", "6---8---4-------"),
            },
            sections = new[]
            {
                new Section { bars = 8, drums = 0, bass = 0, filterFrom = 0.3f, filterTo = 0.8f, fadeFrom = 0.5f },
                new Section { bars = 8, drums = 0, bass = 0, arp = 0, roll = true, filterFrom = 0.6f },
                new Section { bars = 16, drums = 1, bass = 0, arp = 0, chords = 0, crash = true },
                new Section { bars = 8, chords = 0, arp = 0, roll = true, filterFrom = 0.5f, filterTo = 0.9f },
                new Section { bars = 16, drums = 1, bass = 0, arp = 0, chords = 0, lead = 0, crash = true },
                new Section { bars = 16, drums = 1, bass = 0, arp = 0, chords = 0 },
                new Section { bars = 8, drums = 0, bass = 0, arp = 0, filterTo = 0.4f, fadeTo = 0f },
            },
            delaySteps = 2f, delayFeedback = 0.3f, delayMix = 0.28f, reverbSize = 0.82f, reverbMix = 0.25f, chorus = 0.6f, pump = 0.55f, gain = 1.06f,
        };

        // ------------------------------------------------------------------ 4. Slow 12/8 : ballade années 80, Mi bémol majeur
        static Track Afterglow() => new Track
        {
            name = "Afterglow 1986",
            bpm = 66f, stepsPerBeat = 3, scale = Major, prog = "0a 5 3a 4 0a 5 1:7 4", chordSteps = 12,
            bassBase = 39, chordCentre = 58, arpBase = 63, leadBase = 63,
            bassPatch = new Patch { fm = 1.6f, fmDecay = 0.25f, fmSustain = 0.25f, ratioB = 1f, cutoff = 1400f, attack = 0.008f, decay = 0.8f, sustain = 0.6f, release = 0.15f, level = 0.3f, pump = 0.5f },
            chordPatch = new Patch { ratioB = 0.5015f, mixB = 0.7f, stereo = 0.35f, cutoff = 1500f, q = 0.8f, attack = 0.6f, release = 1f, glide = 0.05f, level = 0.085f, reverb = 0.5f, pump = 0.5f },
            arpPatch = new Patch { fm = 2.2f, fmDecay = 0.35f, fmSustain = 0.15f, ratioB = 3f, cutoff = 9000f, attack = 0.002f, decay = 1.1f, sustain = 0f, level = 0.11f, reverb = 0.6f, delay = 0.35f, pump = 0.3f },
            leadPatch = new Patch { ratioB = 1.005f, mixB = 0.9f, cutoff = 500f, ampToCut = 2600f, q = 0.9f, attack = 0.07f, decay = 0.5f, sustain = 0.9f, release = 0.18f, glide = 0.02f, vibrato = 0.0045f, vibRate = 5f, vibDelay = 0.35f, level = 0.1f, reverb = 0.6f, delay = 0.35f },
            kit = new Kit { kickDecay = 0.17f, snareGate = 0.34f, snareLevel = 1.1f, snareReverb = 0.8f, hatLevel = 0.14f },
            drums = new[]
            {
                new DrumSet { kick = P("x.....x....."), hat = P("x..x..x..x..") },
                new DrumSet { kick = P("x....ox....."), snare = P("...X.....X.."), hat = P("XooXooXooXoo") },
            },
            bass = new[] { P("0-----0----0", "0-----0--4--") },
            chords = new[] { P("x-----------") },
            arp = new[] { P("024742024742") },
            lead = new[]
            {
                P("2-----4-----", "5-----4--2--", "3-----5--7--", "6--------4--",
                  "9-----7--4--", "5-----7--9--", "8-----7--5--", "6-----4-----"),
            },
            sections = new[]
            {
                new Section { bars = 4, arp = 0, chords = 0, filterFrom = 0.4f, filterTo = 0.8f, fadeFrom = 0.4f },
                new Section { bars = 8, arp = 0, chords = 0, bass = 0, drums = 0, roll = true, filterFrom = 0.7f },
                new Section { bars = 8, arp = 0, chords = 0, bass = 0, drums = 1, lead = 0, crash = true },
                new Section { bars = 4, arp = 0, chords = 0, bass = 0, roll = true, filterFrom = 0.6f, filterTo = 0.9f },
                new Section { bars = 16, arp = 0, chords = 0, bass = 0, drums = 1, lead = 0, crash = true },
                new Section { bars = 4, arp = 0, chords = 0, filterTo = 0.5f, fadeTo = 0f },
            },
            delayFeedback = 0.35f, delayMix = 0.3f, reverbSize = 0.88f, reverbDamp = 0.25f, reverbMix = 0.4f, chorus = 0.5f, pump = 0.3f, gain = 0.84f,
        };

        // ------------------------------------------------------------------ 5. City pop funk : Ré majeur, accords de septième
        static Track SunsetInterstate() => new Track
        {
            name = "Sunset Interstate",
            bpm = 106f, scale = Major, prog = "3:9 4:7 2:7 5:9 3:9 4:7 0:9 0:7",
            bassBase = 38, chordCentre = 64, arpBase = 62, leadBase = 74,
            bassPatch = new Patch { ratioB = 1.004f, cutoff = 220f, envAmt = 3200f, envDecay = 0.07f, q = 3.2f, decay = 0.12f, sustain = 0.3f, release = 0.03f, level = 0.2f, pump = 0.5f },
            chordPatch = new Patch { b = Wave.Pulse, width = 0.4f, ratioB = 1.005f, mixB = 0.7f, stereo = 0.7f, cutoff = 500f, envAmt = 4200f, envDecay = 0.11f, q = 1.3f, decay = 0.18f, sustain = 0.25f, release = 0.06f, level = 0.12f, reverb = 0.3f, delay = 0.15f, pump = 0.4f },
            arpPatch = new Patch { a = Wave.Pulse, width = 0.2f, mixB = 0f, cutoff = 2400f, envAmt = 2500f, envDecay = 0.02f, q = 2.2f, decay = 0.035f, sustain = 0f, level = 0.15f, reverb = 0.15f, pump = 0.2f },
            leadPatch = new Patch { fm = 2f, fmDecay = 0.1f, fmSustain = 0.2f, ratioB = 2f, cutoff = 9000f, attack = 0.002f, decay = 0.45f, sustain = 0f, release = 0.3f, level = 0.17f, delay = 0.4f, reverb = 0.35f },
            kit = new Kit
            {
                kickFrom = 160f, kickTo = 55f, kickSweep = 0.015f, kickDecay = 0.09f, kickClick = 0.3f,
                snareTone = 230f, snareToneDecay = 0.02f, snareNoiseDecay = 0.045f, snareColor = 0.9f, snareLevel = 0.9f, snareReverb = 0.35f,
                clapLevel = 0.22f, hatColor = 0.75f, hatDecay = 0.02f, hatOpen = 0.1f, hatLevel = 0.17f, percLevel = 0.18f,
            },
            drums = new[]
            {
                new DrumSet { kick = P("x......x.x......"), hat = P("xoxoxoxoxoxoxoOo"), perc = P("....x.......x...") },
                new DrumSet { kick = P("x......x.x......", "x......x.x....x."), snare = P("....x.......x..."), clap = P("....x.......x..."), hat = P("xoxoxoxoxoxoxoOo") },
            },
            bass = new[] { P("0..0.07...0.07..", "0..0.07..0.7.4a.") },
            chords = new[] { P("x..x..x.....x.x.", "x..x..x...x.....") },
            arp = new[] { P("..4.4..7..4.7.4.") },
            lead = new[]
            {
                P("9..9..7...5.7...", "8..8..6...4.6...", "6..6..4...2.4...", "5.......4.5.7.9.",
                  "9..9..7...5.7...", "8..8..6...4.6...", "9..9..8...6.4...", "7-------........"),
            },
            sections = new[]
            {
                new Section { bars = 8, drums = 0, bass = 0, arp = 0, filterFrom = 0.5f, fadeFrom = 0.5f },
                new Section { bars = 8, drums = 1, bass = 0, arp = 0, chords = 0, crash = true },
                new Section { bars = 16, drums = 1, bass = 0, arp = 0, chords = 0, lead = 0 },
                new Section { bars = 4, bass = 0, chords = 0, roll = true, filterFrom = 0.6f },
                new Section { bars = 16, drums = 1, bass = 0, arp = 0, chords = 0, lead = 0, crash = true },
                new Section { bars = 8, drums = 1, bass = 0, arp = 0, chords = 0 },
                new Section { bars = 4, drums = 0, bass = 0, arp = 0, filterTo = 0.5f, fadeTo = 0f },
            },
            delayFeedback = 0.3f, delayMix = 0.25f, reverbSize = 0.8f, reverbDamp = 0.35f, reverbMix = 0.2f, chorus = 0.5f, pump = 0.25f, gain = 1.2f,
        };

        // ------------------------------------------------------------------ 6. Darksynth : lourd et menaçant, Mi phrygien
        static Track VioletTailLights() => new Track
        {
            name = "Violet Tail Lights",
            bpm = 96f, scale = Phrygian, prog = "0 0 1 6",
            bassBase = 40, chordCentre = 52, arpBase = 52, leadBase = 64, arpStatic = true,
            bassPatch = new Patch { ratioB = 1.012f, sub = 0.2f, cutoff = 520f, envAmt = 1500f, envDecay = 0.09f, q = 1.2f, drive = 0.5f, decay = 0.15f, sustain = 0.7f, release = 0.04f, level = 0.09f },
            chordPatch = new Patch { a = Wave.Pulse, b = Wave.Pulse, ratioB = 1.007f, pwm = 0.3f, pwmRate = 0.23f, cutoff = 1300f, q = 1.1f, attack = 0.5f, release = 0.8f, level = 0.075f, reverb = 0.6f },
            arpPatch = new Patch { a = Wave.Pulse, width = 0.3f, ratioB = 1.01f, mixB = 0.6f, cutoff = 500f, envAmt = 2400f, envDecay = 0.07f, q = 2.4f, drive = 0.3f, decay = 0.12f, sustain = 0f, level = 0.13f, delay = 0.5f },
            leadPatch = new Patch { a = Wave.Pulse, b = Wave.Pulse, ratioB = 1.012f, pwm = 0.2f, pwmRate = 0.5f, cutoff = 2600f, q = 2f, drive = 0.5f, attack = 0.01f, decay = 0.3f, sustain = 0.9f, release = 0.12f, glide = 0.05f, vibrato = 0.006f, vibDelay = 0.3f, level = 0.09f, delay = 0.5f, reverb = 0.5f },
            kit = new Kit
            {
                kickFrom = 170f, kickTo = 45f, kickSweep = 0.03f, kickDecay = 0.15f, kickClick = 0.3f, kickDrive = 0.5f, kickLevel = 0.5f,
                snareTone = 160f, snareToneDecay = 0.045f, snareNoiseDecay = 0.09f, snareGate = 0.3f, snareFloor = 0.3f, snareColor = 0.6f, snareLevel = 1.2f, snareReverb = 0.9f,
                hatColor = 0.6f, hatDecay = 0.025f, hatLevel = 0.12f,
            },
            drums = new[]
            {
                new DrumSet { kick = P("x.....x...x....."), snare = P("........X......."), hat = P("x.x.x.x.x.x.x.O.") },
                new DrumSet { kick = P("x..x..x...x..x.."), snare = P("........X......."), hat = P("x.xxx.xxx.xxx.O.") },
            },
            bass = new[] { P("0.00.00.0.00.00.", "0.00.00.0.07.0a.") },
            chords = new[] { P("x---------------") },
            arp = new[] { P("7.7.8.7.6.7.4.7.") },
            lead = new[]
            {
                P("7-------------6-", "4-------2---4---", "8-------7---5---", "6-------3---4---",
                  "7-----------9-8-", "7-------4---7---", "A-------9---8---", "A---8---6---4---"),
            },
            sections = new[]
            {
                new Section { bars = 8, arp = 0, chords = 0, filterFrom = 0.25f, filterTo = 0.6f, fadeFrom = 0.4f },
                new Section { bars = 4, arp = 0, chords = 0, bass = 0, roll = true, filterFrom = 0.5f, filterTo = 0.9f },
                new Section { bars = 16, drums = 0, bass = 0, arp = 0, chords = 0, crash = true },
                new Section { bars = 4, arp = 0, chords = 0, roll = true, filterFrom = 0.4f, filterTo = 0.8f },
                new Section { bars = 16, drums = 0, bass = 0, arp = 0, chords = 0, lead = 0, crash = true },
                new Section { bars = 8, drums = 1, bass = 0, arp = 0, chords = 0, lead = 0, crash = true },
                new Section { bars = 8, arp = 0, chords = 0, filterTo = 0.3f, fadeTo = 0f },
            },
            delayFeedback = 0.42f, delayMix = 0.3f, reverbSize = 0.88f, reverbDamp = 0.35f, reverbMix = 0.33f, chorus = 0.2f, pump = 0.6f, tone = 12000f, gain = 0.6f,
        };

        // ------------------------------------------------------------------ 7. École de Berlin : séquence hypnotique, Sol dorien
        static Track AnalogDreams() => new Track
        {
            name = "Analog Dreams",
            bpm = 112f, scale = Dorian, prog = "0a 3 0a 6", chordSteps = 64,
            bassBase = 31, chordCentre = 62, arpBase = 55, leadBase = 67,
            bassPatch = new Patch { ratioB = 1.003f, cutoff = 320f, q = 0.8f, attack = 0.4f, decay = 1f, sustain = 0.9f, release = 1.2f, glide = 0.15f, level = 0.16f, pump = 0.6f },
            chordPatch = new Patch { fm = 0.9f, fmSustain = 1f, ratioB = 2f, cutoff = 6000f, attack = 0.9f, release = 1.6f, vibrato = 0.0015f, vibRate = 4.2f, level = 0.09f, reverb = 0.8f, pump = 0.5f },
            arpPatch = new Patch { b = Wave.Pulse, ratioB = 1.003f, mixB = 0.5f, cutoff = 1500f, sweep = 1.2f, sweepRate = 0.035f, envAmt = 900f, envDecay = 0.05f, q = 3.5f, decay = 0.14f, sustain = 0f, level = 0.11f, delay = 0.8f, reverb = 0.2f, pump = 0.3f },
            leadPatch = new Patch { a = Wave.Sine, b = Wave.Sine, ratioB = 2f, mixB = 0.08f, noise = 0.015f, cutoff = 5000f, attack = 0.09f, release = 0.3f, glide = 0.06f, vibrato = 0.007f, vibRate = 5.2f, vibDelay = 0.3f, level = 0.16f, delay = 0.6f, reverb = 0.9f },
            kit = new Kit
            {
                kickFrom = 90f, kickTo = 45f, kickSweep = 0.025f, kickDecay = 0.16f, kickClick = 0f, kickLevel = 0.45f,
                hatColor = 0.5f, hatDecay = 0.02f, hatLevel = 0.07f, shaker = true, percLevel = 0.1f,
            },
            drums = new[] { new DrumSet { kick = P("x...x...x...x..."), hat = P("o.o.o.o.o.o.o.o."), perc = P("..x...x...x...x.") } },
            bass = new[] { P("0---------------", "----------------") },
            chords = new[] { P("x---------------") },
            arp = new[] { P("074797470747B747"), P("0479B9740479B974") },
            lead = new[]
            {
                P("4---------------", "--------2---4---", "7---------------", "--------6---4---",
                  "5---------------", "--------4---5---", "7---------------", "--------5---3---",
                  "4---------------", "--------6---7---", "9---------------", "--------8---7---",
                  "8---------------", "--------6---3---", "4---------------", "--------........"),
            },
            sections = new[]
            {
                new Section { bars = 8, arp = 0, filterFrom = 0.3f, filterTo = 0.7f, fadeFrom = 0.5f },
                new Section { bars = 16, arp = 0, chords = 0, bass = 0, filterFrom = 0.6f },
                new Section { bars = 16, arp = 0, chords = 0, bass = 0, lead = 0 },
                new Section { bars = 16, drums = 0, arp = 0, chords = 0, bass = 0, lead = 0 },
                new Section { bars = 16, drums = 0, arp = 1, chords = 0, bass = 0 },
                new Section { bars = 8, arp = 0, chords = 0, filterTo = 0.3f, fadeTo = 0f },
            },
            dropSection = 3,
            delayFeedback = 0.55f, delayMix = 0.45f, reverbSize = 0.9f, reverbMix = 0.4f, chorus = 0.6f, pump = 0.2f, gain = 0.82f,
        };

        // ------------------------------------------------------------------ 8. Liquid drum & bass : rapide et aérien, Do mineur
        static Track NightShiftCruise() => new Track
        {
            name = "Night Shift Cruise",
            bpm = 170f, scale = Minor, prog = "0:9 5:9 2:9 6a", chordSteps = 32,
            bassBase = 36, chordCentre = 60, chord2Centre = 63, arpBase = 72, leadBase = 60,
            bassPatch = new Patch { fm = 0.9f, fmSustain = 1f, ratioB = 1f, cutoff = 900f, attack = 0.012f, decay = 1f, sustain = 0.85f, release = 0.1f, glide = 0.04f, level = 0.27f, pump = 0.7f },
            chordPatch = new Patch { ratioB = 1.01f, cutoff = 2600f, attack = 0.8f, release = 1.2f, level = 0.05f, reverb = 0.6f },
            chord2Patch = new Patch { fm = 2.6f, fmDecay = 0.12f, fmSustain = 0.15f, ratioB = 1f, cutoff = 5000f, attack = 0.002f, decay = 0.5f, sustain = 0f, release = 0.2f, level = 0.11f, delay = 0.45f, reverb = 0.4f, pump = 0.5f },
            arpPatch = new Patch { a = Wave.Sine, b = Wave.Sine, ratioB = 2f, mixB = 0.3f, cutoff = 8000f, decay = 0.09f, sustain = 0f, level = 0.12f, delay = 0.9f, reverb = 0.5f, pump = 0.2f },
            leadPatch = new Patch { a = Wave.Tri, ratioB = 1.004f, mixB = 0.35f, cutoff = 2400f, q = 0.8f, attack = 0.03f, release = 0.2f, glide = 0.035f, vibrato = 0.004f, level = 0.15f, delay = 0.5f, reverb = 0.6f },
            kit = new Kit
            {
                kickFrom = 180f, kickTo = 55f, kickSweep = 0.012f, kickDecay = 0.1f, kickClick = 0.35f,
                snareTone = 220f, snareToneDecay = 0.028f, snareNoiseDecay = 0.06f, snareColor = 0.95f, snareLevel = 1.05f, snareReverb = 0.35f,
                hatColor = 0.8f, hatDecay = 0.018f, hatOpen = 0.07f, hatLevel = 0.15f, shaker = true, percLevel = 0.07f,
            },
            drums = new[]
            {
                new DrumSet { hat = P("x.x.x.x.x.x.x.x."), perc = P("..x...x...x...x.") },
                new DrumSet { kick = P("x.........x.....", "x.....x...x....."), snare = P("....X..o.o..X...", "....X..o....X..o"), hat = P("x.x.x.x.x.x.x.x."), perc = P(".o.o.o.o.o.o.o.o") },
            },
            bass = new[] { P("0---------0-----", "0-----....4-7---") },
            chords = new[] { P("x---------------") },
            chords2 = new[] { P("x-----x-----x---", "------x---x-----") },
            arp = new[] { P("..7...9...4...7.", "..7...9...B...9.") },
            lead = new[]
            {
                P("4-------------2-", "3-------1-------", "2-------------0-", "a-------0---2---",
                  "4-------------6-", "8-------6-------", "8-------6---3---", "3-------1-------",
                  "B-------------9-", "A-------8-------", "9-------------7-", "6-------7---9---",
                  "9-------------B-", "D-------B-------", "D-------A---8---", "7---------------"),
            },
            sections = new[]
            {
                new Section { bars = 16, chords = 0, chords2 = 0, drums = 0, filterFrom = 0.3f, filterTo = 0.8f, fadeFrom = 0.4f },
                new Section { bars = 8, chords = 0, chords2 = 0, arp = 0, bass = 0, drums = 0, roll = true, filterFrom = 0.6f },
                new Section { bars = 32, drums = 1, bass = 0, chords = 0, chords2 = 0, arp = 0, crash = true },
                new Section { bars = 16, chords = 0, chords2 = 0, lead = 0, roll = true, filterFrom = 0.7f },
                new Section { bars = 32, drums = 1, bass = 0, chords = 0, chords2 = 0, arp = 0, lead = 0, crash = true },
                new Section { bars = 16, chords = 0, chords2 = 0, drums = 0, filterTo = 0.4f, fadeTo = 0f },
            },
            delayFeedback = 0.4f, delayMix = 0.35f, reverbSize = 0.88f, reverbMix = 0.35f, chorus = 0.5f, pump = 0.45f, gain = 0.84f,
        };
    }
}
