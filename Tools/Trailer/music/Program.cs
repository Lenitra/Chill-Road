// Musique de la bande-annonce : le morceau « Neon Overdrive » du jeu (MusicSynth + MusicTracks, sans Unity),
// à 120 à la minute (une mesure = 2 s) et arrangé sur 30 mesures = 60 s. Voir make_music.py.
using System;
using System.IO;
using ChillRoad;

static class Program
{
    static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "trailer_music.wav";
        float seconds = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 60f;
        const int rate = 48000, block = 480;
        var synth = new MusicSynth(rate, 1) { Volume = 1f };
        synth.Seek(0, 0);
        int frames = (int)(seconds * rate);
        var all = new float[frames * 2];
        var buf = new float[block * 2];
        // Montées de tension avant les deux temps forts (16 s et 32 s)
        var risers = new (float at, float len)[] { (12f, 4f), (30f, 2f) };
        int next = 0;
        for (int pos = 0; pos < frames; pos += block)
        {
            float t = pos / (float)rate;
            if (next < risers.Length && t >= risers[next].at) { synth.Riser(risers[next].len); next++; }
            Array.Clear(buf, 0, buf.Length);
            synth.Process(buf, 2);
            int n = Math.Min(block, frames - pos);
            Array.Copy(buf, 0, all, pos * 2, n * 2);
        }
        // Fondu de sécurité sur la dernière demi-seconde
        int fade = rate / 2;
        for (int i = 0; i < fade; i++)
        {
            float g = i / (float)fade;
            int f = frames - 1 - i;
            all[f * 2] *= g; all[f * 2 + 1] *= g;
        }
        using var w = new BinaryWriter(File.Create(output));
        int bytes = all.Length * 2;
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
        float peak = 0f;
        foreach (float s in all) peak = Math.Max(peak, Math.Abs(s));
        float norm = peak > 0.98f ? 0.98f / peak : 1f;
        foreach (float s in all) w.Write((short)Math.Round(Math.Clamp(s * norm, -1f, 1f) * 32767f));
        Console.WriteLine($"{output}: {seconds} s, peak {peak:0.000}, gain {norm:0.000}");
    }
}
