using MapRegionizer.Core.Generation.Stages;

namespace MapRegionizer.Core.Generation;

public sealed class MapGenerationPipelineBuilder
{
    private readonly List<IMapGenerationStage> _stages = [];

    public static MapGenerationPipelineBuilder CreateDefault()
    {
        return new MapGenerationPipelineBuilder()
            .AddStage(new ExtractLandmassesStage())
            .AddStage(new ExtractWaterBodiesStage())
            .AddStage(new GenerateRegionsStage())
            .AddStage(new DistortRegionBoundariesStage());
    }

    public MapGenerationPipelineBuilder AddStage(IMapGenerationStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (_stages.Any(s => s.Id == stage.Id))
            throw new InvalidOperationException($"Generation stage '{stage.Id}' is already registered.");

        _stages.Add(stage);
        return this;
    }

    public MapGenerationPipelineBuilder ReplaceStage(string stageId, IMapGenerationStage replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var index = _stages.FindIndex(s => s.Id == stageId);
        if (index < 0)
            throw new InvalidOperationException($"Generation stage '{stageId}' is not registered.");

        _stages[index] = replacement;
        return this;
    }

    public MapGenerationPipelineBuilder RemoveStage(string stageId)
    {
        _stages.RemoveAll(s => s.Id == stageId);
        return this;
    }

    public MapGenerationPipeline Build() => new(_stages);
}
