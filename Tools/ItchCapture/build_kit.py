"""Génère Marketing/itch/index.html : aperçu du kit itch.io (tagline, description copiable, tags, images)."""
import html, os, re, glob, sys, base64, io
from PIL import Image

ROOT = sys.argv[1] if len(sys.argv) > 1 else r"E:\ChillRoad\Marketing\itch"


def inline(t):
    t = html.escape(t, quote=False)
    t = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", t)
    t = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", t)
    t = re.sub(r"`(.+?)`", r"<code>\1</code>", t)
    return t


def md_to_html(md):
    out, lines, i = [], md.splitlines(), 0
    while i < len(lines):
        ln = lines[i]
        if not ln.strip():
            i += 1
            continue
        m = re.match(r"^(#{1,3}) (.*)", ln)
        if m:
            lvl = len(m.group(1))
            out.append(f"<h{lvl}>{inline(m.group(2))}</h{lvl}>")
            i += 1
            continue
        if ln.strip() == "---":
            out.append("<hr>")
            i += 1
            continue
        if ln.startswith("- "):
            items = []
            while i < len(lines) and (lines[i].startswith("- ") or lines[i].startswith("  - ")):
                if lines[i].startswith("- "):
                    items.append([inline(lines[i][2:]), []])
                else:
                    items[-1][1].append(f"<li>{inline(lines[i][4:])}</li>")
                i += 1
            out.append("<ul>" + "".join(f"<li>{t}" + (f"<ul>{''.join(sub)}</ul>" if sub else "") + "</li>" for t, sub in items) + "</ul>")
            continue
        if ln.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append([c.strip() for c in lines[i].strip("|").split("|")])
                i += 1
            head, body = rows[0], [r for r in rows[2:]]
            t = "<table><thead><tr>" + "".join(f"<th>{inline(c)}</th>" for c in head) + "</tr></thead><tbody>"
            t += "".join("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>" for r in body)
            out.append(t + "</tbody></table>")
            continue
        para = []
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#|- |\||---)", lines[i]):
            para.append(lines[i])
            i += 1
        out.append(f"<p>{inline(' '.join(para))}</p>")
    return "\n".join(out)


def thumb(rel, width):
    im = Image.open(os.path.join(ROOT, rel)).convert("RGB")
    im.thumbnail((width, width))
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=84)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def read(name):
    with open(os.path.join(ROOT, name), encoding="utf-8") as f:
        return f.read()


desc = read("02_description.md")
en, fr = desc.split("\n---\n", 1)
tagline = read("01_tagline.txt")
tags = read("03_tags_et_champs.txt")

shots = sorted(os.path.basename(p) for p in glob.glob(os.path.join(ROOT, "screenshots", "*.png")))
duo = sorted("2_joueurs/" + os.path.basename(p) for p in glob.glob(os.path.join(ROOT, "screenshots", "2_joueurs", "*.png")))
covers = [c for c in ("cover_1260x1000.png", "cover_1260x1000_titre.png", "cover_alt_1260x1000.png", "cover_alt_1260x1000_titre.png") if os.path.exists(os.path.join(ROOT, c))]

shot_html = "\n".join(
    f'<figure><a href="screenshots/{s}"><img src="{thumb("screenshots/" + s, 960)}" alt="{html.escape(s)}"></a><figcaption>{html.escape(s)}</figcaption></figure>' for s in shots)
duo_html = "\n".join(
    f'<figure><a href="screenshots/{d}"><img src="{thumb("screenshots/" + d, 960)}" alt="{html.escape(d)}"></a><figcaption>{html.escape(d)}</figcaption></figure>' for d in duo)
cover_html = "\n".join(
    f'<figure class="cover"><a href="{c}"><img src="{thumb(c, 630)}" alt="{c}"></a><figcaption>{c}</figcaption></figure>' for c in covers)

page = f"""<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kit itch.io</title>
<style>
:root {{ --bg:#f4f1ec; --fg:#1d1b19; --muted:#6b645c; --card:#ffffff; --line:#e2dcd3; --accent:#e0561f; }}
@media (prefers-color-scheme: dark) {{
  :root {{ --bg:#15171a; --fg:#ece8e2; --muted:#9a948c; --card:#1e2125; --line:#2e3238; --accent:#ff7a3d; }}
}}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--bg); color:var(--fg); font:16px/1.55 system-ui, "Segoe UI", sans-serif; }}
main {{ max-width:1100px; margin:0 auto; padding:32px 16px 80px; }}
h1.page {{ font-size:28px; margin:0 0 4px; }}
.sub {{ color:var(--muted); margin:0 0 28px; }}
section {{ background:var(--card); border:1px solid var(--line); border-radius:10px; padding:20px 22px; margin:0 0 22px; }}
section > h2 {{ margin:0 0 12px; font-size:15px; letter-spacing:.06em; text-transform:uppercase; color:var(--accent); }}
pre {{ white-space:pre-wrap; margin:0; font:14px/1.5 ui-monospace, Consolas, monospace; }}
.desc h1 {{ font-size:24px; }} .desc h2 {{ font-size:19px; margin-top:22px; }}
.desc table {{ border-collapse:collapse; margin:8px 0; }} .desc td, .desc th {{ border:1px solid var(--line); padding:4px 10px; text-align:left; }}
.desc code {{ background:var(--bg); padding:1px 5px; border-radius:4px; }}
button {{ font:inherit; border:1px solid var(--line); background:var(--bg); color:var(--fg); border-radius:6px; padding:6px 12px; cursor:pointer; margin:0 8px 12px 0; }}
button:hover {{ border-color:var(--accent); }}
.grid {{ display:grid; grid-template-columns:repeat(auto-fill, minmax(320px, 1fr)); gap:14px; }}
figure {{ margin:0; }} figure img {{ width:100%; height:auto; display:block; border-radius:6px; border:1px solid var(--line); }}
figcaption {{ color:var(--muted); font-size:13px; margin-top:4px; }}
.covers {{ display:flex; flex-wrap:wrap; gap:14px; }} .cover img {{ width:315px; }}
</style>
</head>
<body><main>
<h1 class="page">Chill Road · kit itch.io</h1>
<p class="sub">Tagline, description, tags et images prêts à coller dans la page du projet.</p>

<section><h2>Short description / tagline</h2><pre>{html.escape(tagline)}</pre></section>

<section><h2>Couverture</h2><div class="covers">{cover_html}</div></section>

<section><h2>Captures d'écran</h2><div class="grid">{shot_html}</div></section>

<section><h2>Écran partagé (2 joueurs)</h2><div class="grid">{duo_html}</div></section>

<section><h2>Description</h2>
<button data-copy="desc-en">Copier la version EN</button><button data-copy="desc-fr">Copier la version FR</button><button data-copy="desc-all">Copier EN + FR</button>
<div class="desc" id="desc-all"><div id="desc-en">{md_to_html(en)}</div><hr><div id="desc-fr">{md_to_html(fr)}</div></div>
</section>

<section><h2>Tags et champs</h2><pre>{html.escape(tags)}</pre></section>
</main>
<script>
document.querySelectorAll('button[data-copy]').forEach(b => b.addEventListener('click', async () => {{
  const el = document.getElementById(b.dataset.copy);
  try {{
    await navigator.clipboard.write([new ClipboardItem({{
      'text/html': new Blob([el.innerHTML], {{type: 'text/html'}}),
      'text/plain': new Blob([el.innerText], {{type: 'text/plain'}})
    }})]);
  }} catch (e) {{
    const r = document.createRange(); r.selectNodeContents(el);
    const s = getSelection(); s.removeAllRanges(); s.addRange(r); document.execCommand('copy'); s.removeAllRanges();
  }}
  const t = b.textContent; b.textContent = 'Copié !'; setTimeout(() => b.textContent = t, 1500);
}}));
</script>
</body></html>
"""
with open(os.path.join(ROOT, "index.html"), "w", encoding="utf-8") as f:
    f.write(page)
# Description seule, prête à coller (ouvrir dans un navigateur, tout sélectionner, copier)
with open(os.path.join(ROOT, "02_description.html"), "w", encoding="utf-8") as f:
    f.write('<!doctype html><meta charset="utf-8"><title>Chill Road</title>'
            '<body style="max-width:760px;margin:40px auto;font:16px/1.55 system-ui,sans-serif">'
            + md_to_html(en) + "<hr>" + md_to_html(fr) + "</body>")
print("kit ok", len(shots), "shots", covers)
