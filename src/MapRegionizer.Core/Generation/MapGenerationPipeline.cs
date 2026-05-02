namespace MapRegionizer.Core.Generation;

internal sealed class MapGenerationPipeline
{
    private readonly IReadOnlyList<IMapGenerationStage> _stages;

    public MapGenerationPipeline(IEnumerable<IMapGenerationStage> stages)
    {
        _stages = stages.ToList();
    }

    public void Execute(MapGenerationContext context)
    {
        foreach (var stage in _stages)
            stage.Execute(context);
    }
}
