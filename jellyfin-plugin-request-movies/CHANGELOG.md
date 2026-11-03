# Changelog

## 12.0.0.1 — version initiale (non testée)
- Plugin serveur : API (recherche, statut, demande, signalement), client Seerr, mapping utilisateurs Jellyfin ↔ Seerr.
- Statuts calculés côté serveur ; « Disponible » uniquement après présence dans la bibliothèque Jellyfin.
- Disponibilité réelle : tâche planifiée + webhook Seerr, rafraîchissement global regroupé, espacé et borné.
- Script client (recherche + fiche), page de configuration, chargeur web, tests unitaires de la logique pure.
- Retiré par rapport au cahier des charges initial : quotas de stockage et de demandes dans le plugin (délégués à Seerr).
