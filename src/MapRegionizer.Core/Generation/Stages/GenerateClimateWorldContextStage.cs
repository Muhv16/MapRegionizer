using MapRegionizer.Core.Climate;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateClimateWorldContextStage : IMapGenerationStage, ISpatialBoundaryAwareStage
{
    public string Id => MapStageIds.GenerateClimateWorldContext;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey>
    {
        MapDataKeys.SpatialContext,
        MapDataKeys.ClimateBoundaryContext,
        MapDataKeys.WorldSeed
    };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.ClimateWorldContext };
    public StageBoundaryMetadata BoundaryMetadata => StageBoundaryMetadata.Global();

    public void Execute(MapGenerationContext context)
    {
        context.ClimateWorldContext = ClimateWorldContext.Create(context.WorldSeed, context.GenerationMode, context.ClimateBoundary);
    }
}
