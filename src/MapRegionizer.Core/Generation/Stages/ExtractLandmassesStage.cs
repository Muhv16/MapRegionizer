using MapRegionizer.Core.Shapes;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class ExtractLandmassesStage : IMapGenerationStage
{
    public string Id => MapStageIds.ExtractLandmasses;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Mask };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses };

    public void Execute(MapGenerationContext context)
    {
        var extractor = new LandShapeExtractor(context.GeometryFactory);
        context.Landmasses.AddRange(extractor.Extract(context.Mask, context.Options));
    }
}
