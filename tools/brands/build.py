"""Génère pc/PhoneDeckPC/Brands.json (logos d'applis) depuis le paquet npm simple-icons (CC0).

Utilisation : npm pack simple-icons && tar xzf simple-icons-*.tgz
              python3 tools/brands/build.py package
Seuls les logos listés dans catalog.txt sont gardés, et seulement ceux sans licence restrictive.
"""
import json, re, sys, unicodedata
from pathlib import Path

root = Path(sys.argv[1])
here = Path(__file__).parent
out = here.parent.parent / "pc" / "PhoneDeckPC" / "Brands.json"

# Licences acceptées pour un logo (absence de licence = CC0 du projet Simple Icons).
OK = {None, "CC0-1.0", "MIT", "Apache-2.0", "BSD-3-Clause", "Unlicense"}
# Licences qui demandent seulement de citer l'auteur : citées dans NOTICE.md.
BY = {"CC-BY-3.0", "CC-BY-4.0"}

def slug(title):
    t = title.lower().replace("+", "plus").replace(".", "dot").replace("&", "and")
    t = unicodedata.normalize("NFD", t)
    return re.sub(r"[^a-z0-9]", "", t)

meta = {}
for e in json.loads((root / "data" / "simple-icons.json").read_text(encoding="utf-8")):
    meta[e.get("slug") or slug(e["title"])] = e

icons, order, cat, credits = {}, [], None, []
for line in (here / "catalog.txt").read_text(encoding="utf-8").splitlines():
    line = line.strip()
    if not line or line.startswith("#"): continue
    if line.startswith("["): cat = line.strip("[]"); continue
    name, _, words = line.partition("=")
    name = name.strip()
    e = meta.get(name)
    svg = root / "icons" / (name + ".svg")
    if e is None or not svg.exists(): print("absent :", name); continue
    lic = (e.get("license") or {}).get("type")
    if lic not in OK | BY: print("licence écartée :", name, lic); continue
    if name not in icons:
        d = re.search(r'<path d="([^"]+)"', svg.read_text(encoding="utf-8")).group(1)
        icons[name] = {"n": name, "t": e["title"], "c": "#" + e["hex"].upper(), "k": [], "w": "", "p": d}
        order.append(name)
        if lic in BY: credits.append(f"- {e['title']} : {lic}, {e['license'].get('url') or e['source']}")
    i = icons[name]
    i["k"].append(cat)
    i["w"] = " ".join(dict.fromkeys((i["w"] + " " + words).split()))

data = [icons[n] for n in order]
out.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(here / "NOTICE.md").write_text(
    "# Logos d'applis\n\nTracés issus de [Simple Icons](https://simpleicons.org) (CC0 1.0). Les logos restent des marques de leurs "
    "propriétaires et ne servent qu'à reconnaître l'appli sur un bouton.\n\nLogos sous licence avec attribution :\n\n" + "\n".join(credits) + "\n",
    encoding="utf-8")
print(len(data), "logos,", out.stat().st_size // 1024, "Ko ->", out)
