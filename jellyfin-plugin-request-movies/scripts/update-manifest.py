#!/usr/bin/env python3
"""Ajoute une version au manifest.json du dépôt de plugins Jellyfin.
Usage : update-manifest.py <version> <fichier.zip> <url du zip> [targetAbi]
Lit build.yaml (guid, nom, targetAbi...) sans dépendance externe."""
import datetime
import hashlib
import json
import re
import sys

if len(sys.argv) not in (4, 5):
    raise SystemExit(__doc__)

version, zip_path, url = sys.argv[1:4]
if version.count(".") == 2:
    version += ".0"

meta = {}
for line in open("build.yaml", encoding="utf-8"):
    m = re.match(r'^(\w+):\s*"?([^"\n]*)"?\s*$', line)
    if m:
        meta[m.group(1)] = m.group(2).strip()

checksum = hashlib.md5(open(zip_path, "rb").read()).hexdigest()
entry_version = {
    "version": version,
    "changelog": f"Version {version}",
    "targetAbi": sys.argv[4] if len(sys.argv) == 5 else meta.get("targetAbi", "12.4.0"),
    "sourceUrl": url,
    "checksum": checksum,
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}

try:
    manifest = json.load(open("manifest.json", encoding="utf-8"))
except (FileNotFoundError, json.JSONDecodeError):
    manifest = []

plugin = next((p for p in manifest if p.get("guid") == meta["guid"]), None)
if plugin is None:
    plugin = {
        "guid": meta["guid"],
        "name": meta["name"],
        "description": meta.get("overview", ""),
        "overview": meta.get("overview", ""),
        "owner": meta.get("owner", ""),
        "category": meta.get("category", "General"),
        "versions": [],
    }
    manifest.append(plugin)

plugin["versions"] = [
    v for v in plugin["versions"]
    if (v["version"], v["targetAbi"]) != (version, entry_version["targetAbi"])
]
plugin["versions"].insert(0, entry_version)
json.dump(manifest, open("manifest.json", "w", encoding="utf-8"), indent=2, ensure_ascii=False)
print(f"manifest.json mis à jour : {version} ({checksum})")
