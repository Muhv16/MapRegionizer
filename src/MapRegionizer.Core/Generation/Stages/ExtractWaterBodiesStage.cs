using MapRegionizer.Core.Shapes;
using System.Threading;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class ExtractWaterBodiesStage : ICancellableMapGenerationStage
{
    public string Id => MapStageIds.ExtractWaterBodies;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses, MapDataKeys.SpatialContext };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.WaterBodies };

    public void Execute(MapGenerationContext context) => Execute(context, CancellationToken.None);

    public void Execute(MapGenerationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extractor = new WaterShapeExtractor(context.GeometryFactory);
        context.WaterBodies.AddRange(extractor.Extract(
            context.Landmasses,
            context.Mask.Width,
            context.Mask.Height,
            context.Options,
            cancellationToken));
    }
}
