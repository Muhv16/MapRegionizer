using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Generation.Stages;

internal sealed class GenerateRegionsStage : IMapGenerationStage
{
    public void Execute(MapGenerationContext context)
    {
        var generator = new VoronoiRegionGenerator(context.GeometryFactory, context.Random);

        foreach (var landmass in context.Landmasses.Where(l => l.Shape.IsValid))
        {
            var regionPolygons = generator.Generate(landmass.Shape, context.Options.Regions);
            context.Regions.AddRange(regionPolygons.Select(p => new MapRegion(context.CreateRegionId(), landmass.Id, p)));
        }
    }
}
