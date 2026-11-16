"""Rédige les notes de version de Chill Road avec Claude, à partir des commits depuis la release précédente.

Utilisé par .github/workflows/release.yml. En local :

    python .github/scripts/release_notes.py --version v0.2.0 --dry-run        # affiche le prompt, sans appel
    python .github/scripts/release_notes.py --version v0.2.0 --out notes.md   # appel réel (ANTHROPIC_API_KEY)
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

DEFAULT_MODEL = "claude-opus-5-5"
REPO_ROOT = Path(__file__).resolve().parents[2]
ITCH_DESCRIPTION = REPO_ROOT / "Marketing" / "itch" / "02_description.md"

SYSTEM_PROMPT = """You write the release notes of Chill Road, an indie arcade driving game (endless traffic weaving, \
near misses, police chases, procedural roads) published on itch.io. Players read these notes, not developers.

Rules:
- Describe only what the commits show. Never invent a feature, a number or a fix that is not in them.
- Write from the player's point of view: what changes when they play. Skip purely internal work (refactors, \
build scripts, debug tools, editor importers) unless it has a visible effect such as better performance.
- Group the changes under short headings such as New, Improved, Fixed (leave out an empty heading). \
Short bullet points, lively but plain tone, no hype, no emojis.
- Open with one or two sentences that sum up the release.
- Write the notes in English first, then the same notes in French (natural French, tutoiement, as on the itch page).

Output exactly this Markdown structure and nothing else:

## English

<notes>

## Français

<notes>"""


def git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args], cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=True
    )
    return result.stdout.strip()


def previous_tag() -> str | None:
    """Dernier tag de version (v*) atteignable depuis HEAD, ou None pour une première release."""
    try:
        return git("describe", "--tags", "--abbrev=0", "--match", "v*", "HEAD") or None
    except subprocess.CalledProcessError:
        return None


def collect_changes(since: str | None) -> tuple[str, str]:
    """Commits (messages complets) et fichiers modifiés depuis `since`."""
    rev_range = f"{since}..HEAD" if since else "HEAD"
    log = git("log", rev_range, "--no-merges", "--reverse", "--format=- %s%n%w(0,2,2)%b")
    # Les lignes « Co-Authored-By » n'apprennent rien sur le jeu.
    commits = "\n".join(line for line in log.splitlines() if not line.strip().lower().startswith("co-authored-by:"))
    if not since:
        # Première release : la liste des fichiers serait le projet entier, les commits suffisent.
        return commits, ""
    files = git(
        "diff", "--name-status", since, "HEAD", "--", ".", ":(exclude)*.meta", ":(exclude)ProjectSettings/*"
    )
    return commits, files


def game_description() -> str:
    """Partie anglaise de la page itch.io, pour que Claude connaisse le jeu et son ton."""
    if not ITCH_DESCRIPTION.exists():
        return ""
    text = ITCH_DESCRIPTION.read_text(encoding="utf-8")
    return text.split("\n# Chill Road (FR)")[0].strip()


def build_prompt(version: str, since: str | None, commits: str, files: str) -> str:
    parts = [f"Write the release notes for Chill Road {version}."]
    parts.append(
        f"Previous release: {since}." if since else "This is the first public release: sum up the game as it is now."
    )
    description = game_description()
    if description:
        parts.append(f"<game_description>\n{description}\n</game_description>")
    parts.append(f"<commits>\n{commits}\n</commits>")
    if files:
        parts.append(f"<changed_files>\n{files}\n</changed_files>")
    return "\n\n".join(parts)


def write_notes(prompt: str, model: str) -> str:
    import anthropic

    client = anthropic.Anthropic()
    response = client.beta.messages.create(
        model=model,
        max_tokens=16000,
        system=SYSTEM_PROMPT,
        messages=[{"role": "user", "content": prompt}],
        output_config={"effort": "medium"},
        # En cas de refus par les filtres de sécurité, l'API relance la requête sur un autre modèle.
        betas=["server-side-fallback-2026-07-01"],
        fallbacks="default",
    )

    usage = response.usage
    print(
        f"Modèle : {response.model} · entrée {usage.input_tokens} tokens · sortie {usage.output_tokens} tokens",
        file=sys.stderr,
    )
    if response.stop_reason == "refusal":
        sys.exit("Claude a refusé de rédiger les notes de version.")
    if response.stop_reason == "max_tokens":
        sys.exit("Les notes de version ont été coupées (max_tokens atteint).")

    notes = "\n".join(block.text for block in response.content if block.type == "text").strip()
    if not notes:
        sys.exit("Réponse vide de Claude.")
    return notes


def main() -> None:
    # La console Windows n'est pas en UTF-8 par défaut (flèches, accents de la page itch).
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", required=True, help="Version publiée, ex. v0.2.0")
    parser.add_argument("--since", help="Tag de départ (par défaut : la release précédente)")
    parser.add_argument("--out", help="Fichier Markdown de sortie (par défaut : la sortie standard)")
    parser.add_argument("--dry-run", action="store_true", help="Affiche le prompt sans appeler l'API")
    args = parser.parse_args()

    since = args.since or previous_tag()
    commits, files = collect_changes(since)
    if not commits:
        sys.exit(f"Aucun commit depuis {since} : rien à publier.")

    prompt = build_prompt(args.version, since, commits, files)
    if args.dry_run:
        print(f"--- system ---\n{SYSTEM_PROMPT}\n\n--- user ---\n{prompt}")
        return

    model = os.environ.get("CLAUDE_MODEL") or DEFAULT_MODEL
    notes = write_notes(prompt, model)
    if args.out:
        Path(args.out).write_text(notes + "\n", encoding="utf-8")
        print(f"Notes écrites dans {args.out}", file=sys.stderr)
    else:
        print(notes)


if __name__ == "__main__":
    main()
