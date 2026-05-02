using MapRegionizer.Core.Shapes;

namespace MapRegionizer.Core.Generation.Stages;

internal sealed class ExtractWaterBodiesStage : IMapGenerationStage
{
    public void Execute(MapGenerationContext context)
    {
        var extractor = new WaterShapeExtractor(context.GeometryFactory);
        context.WaterBodies.AddRange(extractor.Extract(context.Landmasses, context.Mask.Width, context.Mask.Height, context.Options));
    }
}
