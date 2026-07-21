using MapRegionizer.Core.Boundaries;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;
using MapRegionizer.Core.Regions;

namespace MapRegionizer.Core.Generation.Stages;

public sealed class DistortRegionBoundariesStage : IMapGenerationStage
{
    private static readonly IReadOnlyList<DistortionAttempt> DistortionAttempts =
    [
        new(1, 1),
        new(.5, .75),
        new(.25, .5),
        new(.125, .35),
        new(.0625, .25)
    ];

    public string Id => MapStageIds.DistortRegionBoundaries;
    public IReadOnlySet<MapDataKey> Requires { get; } = new HashSet<MapDataKey> { MapDataKeys.Landmasses, MapDataKeys.RawRegions };
    public IReadOnlySet<MapDataKey> Produces { get; } = new HashSet<MapDataKey> { MapDataKeys.Regions };

    public void Execute(MapGenerationContext context)
    {
        RegionGeometryContract.EnsureSatisfied(context.Landmasses, context.RawRegions, "RawRegions before boundary distortion");
        context.RegionDiagnostics = context.RegionDiagnostics
            .Where(diagnostic => diagnostic.Code is not ("distortion-reduced" or "distortion-reverted"))
            .ToList();

        if (!context.Options.Boundaries.Enabled || context.RawRegions.Count <= 1)
        {
            context.Regions.AddRange(context.RawRegions);
            RegionGeometryContract.EnsureSatisfied(context.Landmasses, context.Regions, "Regions after boundary distortion");
            return;
        }

        var updatedRegions = new List<MapRegion>();

        foreach (var landmass in context.Landmasses)
        {
            var landmassRegions = context.RawRegions.Where(r => r.LandmassId == landmass.Id).ToList();
            if (landmassRegions.Count <= 1)
            {
                updatedRegions.AddRange(landmassRegions);
                continue;
            }

            if (!TryDistortLandmass(context, landmass, landmassRegions, out var candidate, out var appliedAttempt, out var violations))
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
            if (appliedAttempt != DistortionAttempts[0])
            {
                context.RegionDiagnostics = context.RegionDiagnostics.Concat([
                    new RegionDiagnostic(
                        "distortion-reduced",
                        RegionDiagnosticSeverity.Info,
                        $"Boundary distortion was reduced for landmass {landmass.Id.Value} to preserve valid topology.",
                        LandmassId: landmass.Id)
                ]).ToList();
            }
        }

        context.Regions.AddRange(updatedRegions);
        RegionGeometryContract.EnsureSatisfied(context.Landmasses, context.Regions, "Regions after boundary distortion");
    }

    private static bool TryDistortLandmass(
        MapGenerationContext context,
        Landmass landmass,
        IReadOnlyList<MapRegion> landmassRegions,
        out IReadOnlyList<MapRegion> candidate,
        out DistortionAttempt appliedAttempt,
        out IReadOnlyList<string> violations)
    {
        candidate = [];
        appliedAttempt = DistortionAttempts[0];
        violations = [];

        foreach (var attempt in DistortionAttempts)
        {
            var options = ScaleOptions(context.Options.Boundaries, attempt);
            var distorter = new BoundaryDistorter(
                context.GeometryFactory,
                context.CreateStageRandom($"{MapStageIds.DistortRegionBoundaries}:{landmass.Id.Value}"));
            var distorted = distorter.Distort(landmassRegions.Select(region => region.Shape).ToList(), landmass.Shape, options);
            candidate = landmassRegions.Zip(distorted, (region, shape) => region with { Shape = shape }).ToList();
            violations = ValidateCandidate(landmass, candidate);
            if (violations.Count == 0)
            {
                appliedAttempt = attempt;
                return true;
            }
        }

        return false;
    }

    private static BoundaryDistortionOptions ScaleOptions(BoundaryDistortionOptions options, DistortionAttempt attempt)
    {
        return new BoundaryDistortionOptions
        {
            Enabled = options.Enabled,
            Detail = Math.Max(.01, options.Detail * attempt.DetailScale),
            MaxOffset = options.MaxOffset * attempt.OffsetScale,
            MinLineLengthToCurve = options.MinLineLengthToCurve
        };
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

    private readonly record struct DistortionAttempt(double OffsetScale, double DetailScale);
}
