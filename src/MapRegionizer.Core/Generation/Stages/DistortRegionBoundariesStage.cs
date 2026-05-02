using MapRegionizer.Core.Boundaries;
using MapRegionizer.Core.Domain;

namespace MapRegionizer.Core.Generation.Stages;

internal sealed class DistortRegionBoundariesStage : IMapGenerationStage
{
    public void Execute(MapGenerationContext context)
    {
        if (!context.Options.Boundaries.Enabled || context.Regions.Count <= 1)
            return;

        var distorter = new BoundaryDistorter(context.GeometryFactory, context.Random);
        var updatedRegions = new List<MapRegion>();

        foreach (var landmass in context.Landmasses)
        {
            var landmassRegions = context.Regions.Where(r => r.LandmassId == landmass.Id).ToList();
            if (landmassRegions.Count <= 1)
            {
                updatedRegions.AddRange(landmassRegions);
                continue;
            }

            var distorted = distorter.Distort(landmassRegions.Select(r => r.Shape).ToList(), landmass.Shape, context.Options.Boundaries);
            updatedRegions.AddRange(landmassRegions.Zip(distorted, (region, shape) => region with { Shape = shape }));
        }

        context.Regions.Clear();
        context.Regions.AddRange(updatedRegions);
    }
}
