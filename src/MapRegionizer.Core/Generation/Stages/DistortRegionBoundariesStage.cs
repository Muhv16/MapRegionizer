using MapRegionizer.Core.Boundaries;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class DistortRegionBoundariesStage : IMapGenerationStage
{
    public string Id => MapStageIds.DistortRegionBoundaries;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses, MapDataKeys.RawRegions };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.Regions };

    public void Execute(MapGenerationContext context)
    {
        RegionGeometryContract.EnsureSatisfied(context.Landmasses, context.RawRegions, "RawRegions before boundary distortion");

        if (!context.Options.Boundaries.Enabled || context.RawRegions.Count <= 1)
        {
            context.Regions.AddRange(context.RawRegions);
            RegionGeometryContract.EnsureSatisfied(context.Landmasses, context.Regions, "Regions after boundary distortion");
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
            var candidate = landmassRegions.Zip(distorted, (region, shape) => region with { Shape = shape }).ToList();
            var violations = ValidateCandidate(landmass, candidate);
            if (violations.Count != 0)
            {
                updatedRegions.AddRange(landmassRegions);
                context.RegionDiagnostics = context.RegionDiagnostics.Concat([
                    new RegionDiagnostic(
                        "distortion-reverted",
                        RegionDiagnosticSeverity.Warning,
                        $"Boundary distortion was reverted for landmass {landmass.Id.Value}: {string.Join(" ", violations)}",
                        LandmassId: landmass.Id)
                ]).ToList();
                continue;
            }

            updatedRegions.AddRange(candidate);
        }

        context.Regions.AddRange(updatedRegions);
        RegionGeometryContract.EnsureSatisfied(context.Landmasses, context.Regions, "Regions after boundary distortion");
    }

    private static IReadOnlyList<string> ValidateCandidate(Landmass landmass, IReadOnlyList<MapRegion> candidate)
    {
        try
        {
            return RegionGeometryContract.Validate([landmass], candidate);
        }
        catch (Exception exception) when (exception is NetTopologySuite.Geometries.TopologyException or ArgumentException)
        {
            return [$"Distortion produced non-noded or otherwise invalid topology: {exception.Message}"];
        }
    }
}
