#!/usr/bin/env bash
# Ajoute (ou retire avec --remove) le chargement de /RequestMovies/client.js dans l'index.html de Jellyfin Web.
# Une mise à jour de Jellyfin peut écraser index.html : relancer ce script après chaque mise à jour.
# Chemins usuels : /usr/share/jellyfin/web/index.html (paquet Linux) ; /jellyfin/jellyfin-web/index.html (Docker).
set -euo pipefail

INDEX="${1:-/usr/share/jellyfin/web/index.html}"
TAG='<script plugin="RequestMovies" src="/RequestMovies/client.js" defer></script>'

if [ "${1:-}" = "--remove" ] || [ "${2:-}" = "--remove" ]; then
  [ "${1:-}" = "--remove" ] && INDEX="/usr/share/jellyfin/web/index.html"
  sed -i '/plugin="RequestMovies"/d' "$INDEX"
  echo "Chargeur retiré de $INDEX"
  exit 0
fi

[ -w "$INDEX" ] || { echo "Fichier introuvable ou non modifiable : $INDEX (lancer en root ou adapter le chemin)"; exit 1; }
if grep -q 'plugin="RequestMovies"' "$INDEX"; then
  echo "Déjà installé dans $INDEX"; exit 0
fi

cp "$INDEX" "$INDEX.requestmovies.bak"
sed -i "s#</body>#${TAG}</body>#" "$INDEX"
grep -q 'plugin="RequestMovies"' "$INDEX" && echo "Chargeur installé dans $INDEX (sauvegarde : $INDEX.requestmovies.bak)" \
  || { echo "Échec : balise </body> introuvable."; cp "$INDEX.requestmovies.bak" "$INDEX"; exit 1; }
