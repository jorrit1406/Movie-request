#!/usr/bin/env bash
# Ajoute ou met à jour le chargement versionné de /RequestMovies/client.js dans Jellyfin Web.
# Une mise à jour de Jellyfin peut écraser index.html : relancer ce script après chaque mise à jour.
# Chemins usuels : /usr/share/jellyfin/web/index.html (paquet Linux) ; /jellyfin/jellyfin-web/index.html (Docker).
set -euo pipefail

INDEX="${1:-/usr/share/jellyfin/web/index.html}"
TAG='<script plugin="RequestMovies" src="/RequestMovies/client.js?v=1.2.6" defer></script>'

if [ "${1:-}" = "--remove" ] || [ "${2:-}" = "--remove" ]; then
  [ "${1:-}" = "--remove" ] && INDEX="/usr/share/jellyfin/web/index.html"
  sed -i 's#<script plugin="RequestMovies"[^>]*></script>##g' "$INDEX"
  echo "Chargeur retiré de $INDEX"
  exit 0
fi

[ -w "$INDEX" ] || { echo "Fichier introuvable ou non modifiable : $INDEX (lancer en root ou adapter le chemin)"; exit 1; }
if grep -q 'plugin="RequestMovies"' "$INDEX"; then
  if grep -Fq "$TAG" "$INDEX"; then
    echo "Chargeur déjà à jour dans $INDEX"
    exit 0
  fi

  cp "$INDEX" "$INDEX.requestmovies.bak"
  sed -i "s#<script plugin=\"RequestMovies\"[^>]*></script>#$TAG#g" "$INDEX"
  if grep -Fq "$TAG" "$INDEX"; then
    echo "Chargeur mis à jour dans $INDEX (sauvegarde : $INDEX.requestmovies.bak)"
  else
    echo "Échec de la mise à jour du chargeur."
    cp "$INDEX.requestmovies.bak" "$INDEX"
    exit 1
  fi
  exit 0
fi

cp "$INDEX" "$INDEX.requestmovies.bak"
sed -i "s#</body>#${TAG}</body>#" "$INDEX"
if grep -q 'plugin="RequestMovies"' "$INDEX"; then
  echo "Chargeur installé dans $INDEX (sauvegarde : $INDEX.requestmovies.bak)"
else
  echo "Échec : balise </body> introuvable."
  cp "$INDEX.requestmovies.bak" "$INDEX"
  exit 1
fi
