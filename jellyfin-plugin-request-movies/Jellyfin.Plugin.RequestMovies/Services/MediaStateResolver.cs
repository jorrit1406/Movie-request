using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.RequestMovies.Services;

public enum MediaState
{
    None,
    Pending,
    Approved,
    Processing,
    Partial,
    ImportPending,
    Available,
    Failed,
    Declined
}

/// <summary>
/// Logique pure (sans dépendance Jellyfin) de calcul du statut affiché.
/// Règle d'or : « Available » uniquement si Jellyfin possède réellement le média.
/// Codes Seerr supposés : MediaStatus 2=en attente, 3=en traitement, 4=partiel, 5=disponible ;
/// MediaRequestStatus 1=en attente, 2=approuvée, 3=refusée, 4=échec, 5=terminée (à valider au Spike 1).
/// </summary>
public static class MediaStateResolver
{
    public static string Key(MediaState state) => state switch
    {
        MediaState.None => "none",
        MediaState.Pending => "pending",
        MediaState.Approved => "approved",
        MediaState.Processing => "processing",
        MediaState.Partial => "partial",
        MediaState.ImportPending => "importPending",
        MediaState.Available => "available",
        MediaState.Failed => "failed",
        MediaState.Declined => "declined",
        _ => "none"
    };

    public static string Label(MediaState state) => state switch
    {
        MediaState.None => "Demander",
        MediaState.Pending => "En attente d’approbation",
        MediaState.Approved => "Demande acceptée",
        MediaState.Processing => "Traitement en cours",
        MediaState.Partial => "Partiellement disponible",
        MediaState.ImportPending => "Importé, indexation en attente",
        MediaState.Available => "Disponible",
        MediaState.Failed => "Échec du traitement",
        MediaState.Declined => "Demande refusée",
        _ => string.Empty
    };

    /// <summary>Une demande peut être (re)lancée dans ces états.</summary>
    public static bool CanRequest(MediaState state)
        => state is MediaState.None or MediaState.Failed or MediaState.Declined or MediaState.Partial;

    public static MediaState ResolveMovie(bool inJellyfin, int? seerrMediaStatus, int? latestRequestStatus)
    {
        if (inJellyfin)
        {
            return MediaState.Available;
        }

        switch (seerrMediaStatus)
        {
            case 4:
            case 5:
                // Seerr le dit disponible mais Jellyfin ne le voit pas encore.
                return MediaState.ImportPending;
            case 3:
                return MediaState.Processing;
            case 2:
                return latestRequestStatus == 2 ? MediaState.Approved : MediaState.Pending;
        }

        return latestRequestStatus switch
        {
            1 => MediaState.Pending,
            2 => MediaState.Approved,
            3 => MediaState.Declined,
            4 => MediaState.Failed,
            _ => MediaState.None
        };
    }

    public static MediaState ResolveSeason(int jellyfinEpisodes, int seerrEpisodeCount, int? seerrSeasonStatus)
    {
        if (jellyfinEpisodes > 0)
        {
            return seerrEpisodeCount <= 0 || jellyfinEpisodes >= seerrEpisodeCount
                ? MediaState.Available
                : MediaState.Partial;
        }

        return seerrSeasonStatus switch
        {
            4 or 5 => MediaState.ImportPending,
            3 => MediaState.Processing,
            2 => MediaState.Pending,
            _ => MediaState.None
        };
    }

    public static MediaState ResolveSeries(
        IReadOnlyCollection<MediaState> seasonStates, int? seerrMediaStatus, int? latestRequestStatus)
    {
        if (seasonStates.Count > 0)
        {
            if (seasonStates.All(s => s == MediaState.Available))
            {
                return MediaState.Available;
            }

            if (seasonStates.Any(s => s is MediaState.Available or MediaState.Partial))
            {
                return MediaState.Partial;
            }
        }

        return ResolveMovie(false, seerrMediaStatus, latestRequestStatus);
    }
}
