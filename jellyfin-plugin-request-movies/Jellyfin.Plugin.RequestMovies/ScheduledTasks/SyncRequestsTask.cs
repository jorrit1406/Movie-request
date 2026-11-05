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
    private readonly TextRequestAvailabilityService _textRequestAvailability;

    public SyncRequestsTask(
        AvailabilityService availability,
        TextRequestAvailabilityService textRequestAvailability)
    {
        _availability = availability;
        _textRequestAvailability = textRequestAvailability;
    }

    public string Name => "Check request availability";

    public string Key => "RequestMoviesSync";

    public string Description => "Detects imported media that is not yet indexed and triggers a controlled library refresh.";

    public string Category => "Request Movies";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await _availability.ReconcileAsync(false, cancellationToken).ConfigureAwait(false);
        _textRequestAvailability.Reconcile();
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
