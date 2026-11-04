using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.RequestMovies.ScheduledTasks;

/// <summary>Vérification de secours (événements manqués) : une seule tâche centralisée, à fréquence modérée.</summary>
public sealed class SyncRequestsTask : IScheduledTask
{
    private readonly AvailabilityService _availability;

    public SyncRequestsTask(AvailabilityService availability)
    {
        _availability = availability;
    }

    public string Name => "Vérifier la disponibilité des demandes";

    public string Key => "RequestMoviesSync";

    public string Description => "Détecte les médias importés mais non indexés et rafraîchit la bibliothèque de façon contrôlée.";

    public string Category => "Request Movies";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await _availability.ReconcileAsync(false, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var minutes = Math.Max(5, Plugin.Instance?.Configuration.SyncIntervalMinutes ?? 15);
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromMinutes(minutes).Ticks
        };
    }
}
