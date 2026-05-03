using MapRegionizer.Core.Shapes;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class ExtractWaterBodiesStage : IMapGenerationStage
{
    public string Id => MapStageIds.ExtractWaterBodies;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.WaterBodies };

    public void Execute(MapGenerationContext context)
    {
        var extractor = new WaterShapeExtractor(context.GeometryFactory);
        context.WaterBodies.AddRange(extractor.Extract(context.Landmasses, context.Mask.Width, context.Mask.Height, context.Options));
    }
}
