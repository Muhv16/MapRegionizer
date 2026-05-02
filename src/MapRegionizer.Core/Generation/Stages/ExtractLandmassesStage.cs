using MapRegionizer.Core.Shapes;

namespace MapRegionizer.Core.Generation.Stages;

internal sealed class ExtractLandmassesStage : IMapGenerationStage
{
    public void Execute(MapGenerationContext context)
    {
        var extractor = new LandShapeExtractor(context.GeometryFactory);
        context.Landmasses.AddRange(extractor.Extract(context.Mask, context.Options));
    }
}
