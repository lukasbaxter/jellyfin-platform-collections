using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamingCollections.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.StreamingCollections.ScheduledTasks;

public class UpdateStreamingCollectionsTask : IScheduledTask
{
    private readonly StreamingCollectionSyncer _syncer;

    public UpdateStreamingCollectionsTask(StreamingCollectionSyncer syncer)
    {
        _syncer = syncer;
    }

    public string Name => "Update streaming collections";

    public string Key => "StreamingCollections.Update";

    public string Description => "Tags movies and shows by streaming service and rebuilds matching collections. Uses the on-disk cache to avoid hammering TMDB.";

    public string Category => "Streaming Collections";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _syncer.RunAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        }
    };
}
