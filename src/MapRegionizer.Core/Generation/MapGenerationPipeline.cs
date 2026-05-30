namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationPipeline
{
    private readonly IReadOnlyList<IMapGenerationStage> _stages;
    private readonly Dictionary<MapDataKey, IMapGenerationStage> _producerByDataKey;

    public MapGenerationPipeline(IEnumerable<IMapGenerationStage> stages)
    {
        _stages = stages.ToList();
        _producerByDataKey = BuildProducerMap(_stages);
    }

    public IReadOnlyList<IMapGenerationStage> Stages => _stages;

    public void RunFull(MapGenerationContext context)
    {
        if (context.Options.Debug)
        {
            var totalStages = _stages.Count;
            var stageNumber = 0;
            var prevSnapshot = MemorySnapshot.Capture();
            Console.Error.WriteLine($"[MEM] Pipeline start: managed {prevSnapshot.ManagedBytes / (1024.0 * 1024.0):F1}M, WS {prevSnapshot.WorkingSetBytes / (1024.0 * 1024.0):F1}M");

            foreach (var stage in _stages)
            {
                stageNumber++;
                var before = MemorySnapshot.Capture();
                ExecuteStageIfRequired(context, stage);
                var after = MemorySnapshot.Capture();
                var delta = after.DeltaFrom(before);
                Console.Error.WriteLine($"[MEM] Stage {stageNumber,2}/{totalStages}: {stage.Id,-30} | {delta.Format(after.ManagedBytes, after.WorkingSetBytes)}");
            }
        }
        else
        {
            foreach (var stage in _stages)
                ExecuteStageIfRequired(context, stage);
        }
    }

    public void RunUntil(MapGenerationContext context, MapDataKey target)
    {
        EnsureData(context, target);
    }

    public void Regenerate(MapGenerationContext context, MapDataKey target)
    {
        if (!_producerByDataKey.TryGetValue(target, out var stage))
            throw new InvalidOperationException($"No generation stage produces '{target}'.");

        foreach (var required in stage.Requires)
            EnsureData(context, required);

        ExecuteStage(context, stage);
        MarkDependentsDirty(context, stage.Produces);
    }

    public void MarkDirty(MapGenerationContext context, IEnumerable<MapDataKey> changedKeys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(changedKeys);

        var changed = changedKeys.ToHashSet();
        foreach (var key in changed)
            context.MarkDirty(key);

        MarkDependentsDirty(context, changed);
    }

    private void EnsureData(MapGenerationContext context, MapDataKey key)
    {
        if (context.Has(key))
            return;

        if (!_producerByDataKey.TryGetValue(key, out var stage))
            throw new InvalidOperationException($"No generation stage produces required data '{key}'.");

        ExecuteStageIfRequired(context, stage);
    }

    private void ExecuteStageIfRequired(MapGenerationContext context, IMapGenerationStage stage)
    {
        foreach (var required in stage.Requires)
            EnsureData(context, required);

        if (stage.Produces.All(context.Has))
            return;

        ExecuteStage(context, stage);
    }

    private static void ExecuteStage(MapGenerationContext context, IMapGenerationStage stage)
    {
        foreach (var produced in stage.Produces)
            context.ClearData(produced);

        stage.Execute(context);

        foreach (var produced in stage.Produces)
            context.MarkProduced(produced);
    }

    private void MarkDependentsDirty(MapGenerationContext context, IReadOnlySet<MapDataKey> changedKeys)
    {
        var queue = new Queue<MapDataKey>(changedKeys);
        var visited = new HashSet<MapDataKey>(changedKeys);

        while (queue.Count > 0)
        {
            var changed = queue.Dequeue();
            foreach (var dependentStage in _stages.Where(s => s.Requires.Contains(changed)))
            {
                foreach (var produced in dependentStage.Produces)
                {
                    if (changedKeys.Contains(produced))
                        continue;

                    context.MarkDirty(produced);
                    if (visited.Add(produced))
                        queue.Enqueue(produced);
                }
            }
        }
    }

    private static Dictionary<MapDataKey, IMapGenerationStage> BuildProducerMap(IEnumerable<IMapGenerationStage> stages)
    {
        var result = new Dictionary<MapDataKey, IMapGenerationStage>();

        foreach (var stage in stages)
        {
            foreach (var produced in stage.Produces)
            {
                if (!result.TryAdd(produced, stage))
                    throw new InvalidOperationException($"Multiple generation stages produce '{produced}'.");
            }
        }

        return result;
    }
}
