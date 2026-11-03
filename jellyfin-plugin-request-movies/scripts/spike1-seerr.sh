#!/usr/bin/env bash
# Spike 1 : valider l'API Seerr AVANT d'écrire la logique du plugin.
# À lancer sur une instance Seerr DE TEST. Crée puis supprime une demande et un signalement.
#
# Usage :
#   export SEERR_URL=http://seerr:5055 SEERR_KEY=xxxx
#   export TMDB_MOVIE=603          # un film de test (ex. 603), absent de votre bibliothèque
#   export SEERR_USER_ID=2         # ID Seerr d'un utilisateur NON admin (voir étape 3)
#   ./spike1-seerr.sh
set -u
: "${SEERR_URL:?}" "${SEERR_KEY:?}"
TMDB_MOVIE="${TMDB_MOVIE:-603}"
OUT=./spike1-out; mkdir -p "$OUT"
API="${SEERR_URL%/}/api/v1"

call() { # call <nom> <méthode> <chemin> [json]
  local name="$1" method="$2" path="$3" body="${4:-}"
  local args=(-sS -o "$OUT/$name.json" -w "%{http_code}" -X "$method" -H "X-Api-Key: $SEERR_KEY")
  [ -n "$body" ] && args+=(-H "Content-Type: application/json" -d "$body")
  local code; code=$(curl "${args[@]}" "$API/$path")
  echo "[$code] $method /$path  -> $OUT/$name.json"
}

echo "== 1. Statut"
call 01-status GET status

echo "== 2. Recherche (encodage strict)"
curl -sS -o "$OUT/02-search.json" -w "[%{http_code}] GET /search -> $OUT/02-search.json\n" \
  -H "X-Api-Key: $SEERR_KEY" -G --data-urlencode "query=Matrix" "$API/search"

echo "== 3. Utilisateurs (repérer id Seerr et jellyfinUserId)"
call 03-users GET "user?take=100"

if [ -z "${SEERR_USER_ID:-}" ]; then
  echo "Renseignez SEERR_USER_ID (voir $OUT/03-users.json) puis relancez."; exit 0
fi

echo "== 4. Quota de l'utilisateur"
call 04-quota GET "user/$SEERR_USER_ID/quota"

echo "== 5. Demande AU NOM de l'utilisateur (champ userId)"
call 05-request POST request "{\"mediaType\":\"movie\",\"mediaId\":$TMDB_MOVIE,\"is4k\":false,\"userId\":$SEERR_USER_ID}"
REQ_ID=$(sed -n 's/.*"id":\([0-9]*\).*/\1/p' "$OUT/05-request.json" | head -1)
echo "   -> vérifier requestedBy.id == $SEERR_USER_ID dans 05-request.json (ID demande : ${REQ_ID:-?})"

echo "== 6. Doublon (attendu : 409)"
call 06-duplicate POST request "{\"mediaType\":\"movie\",\"mediaId\":$TMDB_MOVIE,\"is4k\":false,\"userId\":$SEERR_USER_ID}"

echo "== 7. Lecture du statut de la demande (status, media.status, downloadStatus)"
[ -n "${REQ_ID:-}" ] && call 07-request-get GET "request/$REQ_ID"

echo "== 8. Signalement (Issue) : mediaId = media.id INTERNE Seerr"
MEDIA_ID=$(sed -n 's/.*"media":{[^}]*"id":\([0-9]*\).*/\1/p' "$OUT/05-request.json" | head -1)
if [ -n "${MEDIA_ID:-}" ]; then
  call 08-issue POST issue "{\"issueType\":1,\"message\":\"Test spike 1\",\"mediaId\":$MEDIA_ID}"
else
  echo "   media.id introuvable : lire 05-request.json et créer l'issue à la main."
fi

echo
echo "== 9. TEST DE QUOTA (manuel)"
echo "   Dans Seerr : utilisateur $SEERR_USER_ID > limite de demandes de films = 1 / 7 jours."
echo "   Relancez l'étape 5 avec un AUTRE film (TMDB_MOVIE=...) et conservez la réponse (403 attendu)."
echo
echo "== Nettoyage (à faire à la main si besoin) :"
[ -n "${REQ_ID:-}" ] && echo "   curl -X DELETE -H 'X-Api-Key: ...' $API/request/$REQ_ID"
echo "   Supprimer aussi l'issue de test depuis l'interface Seerr."
echo "Envoyez-moi le contenu de $OUT (en masquant la clé API) ou les codes HTTP."
