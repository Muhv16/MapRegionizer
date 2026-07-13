using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MapRegionizer.Core.Generation;

namespace MapRegionizer.App.Services;

public sealed class GenerationExecutionService
{
    public async Task RunUntilAsync(MapGenerationSession session, MapDataKey target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.RunUntil(target);
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
    }

    public async Task RegenerateAsync(MapGenerationSession session, MapDataKey target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Regenerate(target);
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
    }

    public async Task RunProgressiveAsync(
        MapGenerationSession session,
        IEnumerable<MapDataKey> targets,
        Func<MapDataKey, Task> beforeStage,
        Func<MapDataKey, TimeSpan, Task> afterStage,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await beforeStage(target);
            var started = DateTimeOffset.UtcNow;
            await RunUntilAsync(session, target, cancellationToken);
            await afterStage(target, DateTimeOffset.UtcNow - started);
            await Task.Delay(75, cancellationToken);
        }
    }
}
