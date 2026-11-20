"""Musique de la bande-annonce : rend « Neon Overdrive » (le synthé du jeu, hors ligne) en 60 s.

    python Tools/Trailer/music/make_music.py [sortie.wav]

Le morceau du jeu est repris tel quel (instruments, grille, mélodie) ; seuls le tempo (118 → 120, une mesure = 2 s)
et la structure changent : 4 mesures d'introduction, 4 de montée, 8 + 8 de temps fort (la mélodie arrive à 32 s),
4 de retombée pour le logo, 2 de fondu.
"""
import re, subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SECTIONS = """sections = new[]
            {
                new Section { bars = 4, chords = 0, arp = 0, filterFrom = 0.3f, filterTo = 0.6f, fadeFrom = 0.25f },
                new Section { bars = 4, chords = 0, arp = 0, bass = 0, drums = 0, roll = true, filterFrom = 0.55f, filterTo = 0.95f },
                new Section { bars = 8, chords = 0, arp = 0, bass = 0, drums = 1, crash = true },
                new Section { bars = 8, chords = 0, arp = 0, bass = 0, drums = 1, lead = 0, crash = true },
                new Section { bars = 4, chords = 0, arp = 0, bass = 0, drums = 0, crash = true, filterTo = 0.6f },
                new Section { bars = 2, chords = 0, arp = 0, filterFrom = 0.6f, filterTo = 0.3f, fadeTo = 0f },
            },"""

src = (ROOT / "Assets/ChillRoad/Scripts/Core/MusicTracks.cs").read_text(encoding="utf-8")
a = src.index("static Track NeonOverdrive()")
b = src.index("// ---", a)
neon = src[a:b]
neon = neon.replace("bpm = 118f", "bpm = 120f")
neon, n = re.subn(r"sections = new\[\]\s*\{.*?\n            \},", lambda m: SECTIONS, neon, flags=re.S)
assert n == 1 and "bpm = 120f" in neon
src = src[:a] + neon + src[b:]
src, n = re.subn(r"public static Track\[\] Build\(\) => new\[\]\s*\{.*?\};", "public static Track[] Build() => new[] { NeonOverdrive() };", src, flags=re.S)
assert n == 1
gen = HERE / "obj/gen"
gen.mkdir(parents=True, exist_ok=True)
(gen / "MusicTracks.cs").write_text(src, encoding="utf-8")
out = sys.argv[1] if len(sys.argv) > 1 else str(HERE / "trailer_music.wav")
subprocess.check_call(["dotnet", "run", "-c", "Release", "--project", str(HERE / "TrailerMusic.csproj"), "--", out, "60"])
