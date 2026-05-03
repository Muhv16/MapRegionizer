using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class GenerateRegionsStage : IMapGenerationStage
{
    public string Id => MapStageIds.GenerateRegions;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.RawRegions };

    public void Execute(MapGenerationContext context)
    {
        var generator = new VoronoiRegionGenerator(context.GeometryFactory, context.Random);

        foreach (var landmass in context.Landmasses.Where(l => l.Shape.IsValid))
        {
            var regionPolygons = generator.Generate(landmass.Shape, context.Options.Regions);
            context.RawRegions.AddRange(regionPolygons.Select(p => new MapRegion(context.CreateRegionId(), landmass.Id, p)));
        }
    }
}
