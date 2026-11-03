# Jellyfin Request Movies

Plugin pour Jellyfin 10.11 et 12.4 : recherche de films et séries à la demande (via **Seerr**), demande avec choix
de qualité, statut sur la fiche, disponibilité réelle et signalement de problèmes — sans quitter Jellyfin.

> ⚠️ **Pas encore testé sur une instance Jellyfin réelle.**

Lien du dépôt de plugins Jellyfin :
https://raw.githubusercontent.com/JorritRomijn/Movie-request/main/jellyfin-plugin-request-movies/manifest.json
(Dashboard > Plugins > Repositories > Add)

Les releases publient un paquet distinct pour Jellyfin 10.11 (`targetAbi` 10.11.0, .NET 9) et Jellyfin 12.4
(`targetAbi` 12.4.0, .NET 10). Les deux variantes partagent le même numéro de version de plugin.


## Fonctionnalités
- **Recherche** : section « Disponible à la demande » (résultats Seerr absents de la bibliothèque) sur la page de recherche.
- **Demande** : bouton « Demander », choix 1080p/4K, saisons pour les séries, doublons évités (ID TMDB).
- **Statut sur la fiche** : Demander, En attente d’approbation, Demande acceptée, Traitement en cours,
  Partiellement disponible, Importé (indexation en attente), Disponible, Échec.
- **« Disponible » uniquement si Jellyfin possède réellement le média.** Un média que Seerr dit disponible mais
  non indexé affiche « Importé, indexation en attente » ; un rafraîchissement de bibliothèque contrôlé est déclenché.
- **Permissions** : demandes désactivables par utilisateur, suspension globale.
- **Signalements** : bouton sur la fiche, transmis comme *Issue* Seerr (commentaire, saison, épisode).
- **Quotas et approbation** : gérés par Seerr ; le plugin affiche clairement ses refus.

## Principes
Logique métier et autorisations côté serveur · clé API Seerr jamais exposée · interface minimaliste ·
aucun pourcentage ni délai inventé · aucune notification imposée.