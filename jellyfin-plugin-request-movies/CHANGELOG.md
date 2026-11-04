# Changelog

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
