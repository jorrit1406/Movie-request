# Changelog

## 1.2.6
- Detect Jellyfin administrators using the current user's policy so list management controls appear correctly.

## 1.2.3
- Prevent Jellyfin Web from reusing a stale client script that omits shared-list voting controls.
- Update an existing web loader to use a versioned client-script URL.
- Let Jellyfin administrators mark shared-list requests as done and display them in a separate list.

## 1.2.2
- Make the shared request list available from Jellyfin's side menu, regardless of text-only mode.
- Let each signed-in user cast or remove one vote per title; sort the list by vote count.
- Preserve existing list entries and votes in the plugin's server-side data file.
- Translate remaining user-facing errors and Seerr messages to English.

## 1.2.1
- Show the shared request-list item in Jellyfin's side menu for every signed-in user.
- Display a clear message when text-only mode has not been enabled.
- Translate the plugin settings, request-list UI, scheduled task, and user-facing messages to English.

## 1.2.0
- Ajout du mode liste textuelle partagée, sans appels à Seerr ou TMDB.
- Ajout d’un onglet latéral Jellyfin pour consulter et alimenter la liste ; suppression réservée aux administrateurs.
- Désactivation des fonctions Seerr et TMDB lorsque le mode texte est activé.

## 12.0.0.1 — version initiale (non testée)
- Plugin serveur : API (recherche, statut, demande, signalement), client Seerr, mapping utilisateurs Jellyfin ↔ Seerr.
- Statuts calculés côté serveur ; « Disponible » uniquement après présence dans la bibliothèque Jellyfin.
- Disponibilité réelle : tâche planifiée + webhook Seerr, rafraîchissement global regroupé, espacé et borné.
- Script client (recherche + fiche), page de configuration, chargeur web, tests unitaires de la logique pure.
- Retiré par rapport au cahier des charges initial : quotas de stockage et de demandes dans le plugin (délégués à Seerr).
