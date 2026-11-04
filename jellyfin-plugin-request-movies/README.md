# Jellyfin Request Movies

Plugin pour Jellyfin 10.11 et 12.4 : utilisez une liste partagée de demandes textuelles sans service externe, ou
recherchez et demandez des films et séries via **Seerr**, avec choix de qualité et suivi dans Jellyfin.

> ⚠️ **Pas encore testé sur une instance Jellyfin réelle.**

Lien du dépôt de plugins Jellyfin :
https://raw.githubusercontent.com/JorritRomijn/Movie-request/main/jellyfin-plugin-request-movies/manifest.json
(Dashboard > Plugins > Repositories > Add)

Les releases publient un paquet distinct pour Jellyfin 10.11 (`targetAbi` 10.11.0, .NET 9) et Jellyfin 12.4
(`targetAbi` 12.4.0, .NET 10). Les deux variantes partagent le même numéro de version de plugin.


## Fonctionnalités
- **Shared request list** : always available in the Jellyfin side menu; all signed-in Jellyfin users can view titles, add titles, and vote once per title. The list is ordered by vote count, and administrators can remove entries. This list works regardless of the text-only setting.
- **Recherche** : section « Disponible à la demande » (résultats Seerr absents de la bibliothèque) sur la page de recherche.
- **Demande** : bouton « Demander », choix 1080p/4K, saisons pour les séries, doublons évités (ID TMDB).
- **Statut sur la fiche** : Demander, En attente d’approbation, Demande acceptée, Traitement en cours,
  Partiellement disponible, Importé (indexation en attente), Disponible, Échec.
- **« Disponible » uniquement si Jellyfin possède réellement le média.** Un média que Seerr dit disponible mais
  non indexé affiche « Importé, indexation en attente » ; un rafraîchissement de bibliothèque contrôlé est déclenché.
- **Permissions** : demandes désactivables par utilisateur, suspension globale.
- **Signalements** : bouton sur la fiche, transmis comme *Issue* Seerr (commentaire, saison, épisode).
- **Quotas et approbation** : gérés par Seerr ; le plugin affiche clairement ses refus.

## Installation de l’interface Web

Le paquet du plugin ne charge pas automatiquement son script dans Jellyfin Web. Sans le chargeur, ni la liste latérale ni les contrôles de demande ne peuvent apparaître.

Pour une installation Linux par paquet, depuis le dossier du dépôt :

```sh
sudo bash scripts/install-web-loader.sh /usr/share/jellyfin/web/index.html
```

Pour Docker, le script doit être exécuté dans le conteneur ou sur le fichier `index.html` correspondant au chemin monté par Jellyfin Web. Une mise à jour de Jellyfin peut remplacer ce fichier ; dans ce cas, réinstallez le chargeur. Après l’installation du chargeur ou un changement de mode, rechargez Jellyfin Web (Ctrl+F5).

In the plugin settings, enable **Disable Seerr and TMDB integration (text-only mode)** if you want to use only the shared text list. The list remains available either way. Entries and votes are stored in the Jellyfin plugin data directory.

## Principes
Logique métier et autorisations côté serveur · clé API Seerr jamais exposée · interface minimaliste ·
aucun pourcentage ni délai inventé · aucune notification imposée.