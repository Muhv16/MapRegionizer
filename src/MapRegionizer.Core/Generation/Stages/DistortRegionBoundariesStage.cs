using MapRegionizer.Core.Boundaries;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class DistortRegionBoundariesStage : IMapGenerationStage
{
    public string Id => MapStageIds.DistortRegionBoundaries;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses, MapDataKeys.RawRegions };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.Regions };

    public void Execute(MapGenerationContext context)
    {
        if (!context.Options.Boundaries.Enabled || context.RawRegions.Count <= 1)
        {
            context.Regions.AddRange(context.RawRegions);
            return;
        }

        var distorter = new BoundaryDistorter(context.GeometryFactory, context.Random);
        var updatedRegions = new List<MapRegion>();

        foreach (var landmass in context.Landmasses)
        {
            var landmassRegions = context.RawRegions.Where(r => r.LandmassId == landmass.Id).ToList();
            if (landmassRegions.Count <= 1)
            {
                updatedRegions.AddRange(landmassRegions);
                continue;
            }

            var distorted = distorter.Distort(landmassRegions.Select(r => r.Shape).ToList(), landmass.Shape, context.Options.Boundaries);
            updatedRegions.AddRange(landmassRegions.Zip(distorted, (region, shape) => region with { Shape = shape }));
        }

        context.Regions.AddRange(updatedRegions);
    }
}
