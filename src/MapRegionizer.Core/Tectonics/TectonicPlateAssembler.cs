using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Options;

namespace MapRegionizer.Core.Tectonics;

internal sealed class TectonicPlateAssembler
{
    public TectonicPlateMap Assemble(TectonicHistory history, CrustFieldMap crustFields, PlateDomainMap plateDomains, TectonicBoundaryMap boundaries, TectonicFeatureMap features, OrogenProvinceMap orogenProvinces, RiftProvinceMap riftProvinces)
    {
        var plates = plateDomains.Domains
            .Select(d => new TectonicPlate(d.Id, d.Kind, d.PointCount, d.Centroid, d.Motion, d.Activity, d.Density, d.Thickness, d.MeanOceanicAge))
            .ToArray();
        var plateBoundaries = BuildPlateBoundaries(boundaries);
        var raster = new TectonicPlateRaster(plateDomains.Width, plateDomains.Height, plateDomains.PlatesSpan.ToArray(), crustFields.CrustSpan.ToArray());

        return new TectonicPlateMap(plateDomains.Width, plateDomains.Height, plates, plateBoundaries, raster, history, crustFields, plateDomains, boundaries, features, orogenProvinces, riftProvinces);
    }

    private static IReadOnlyList<PlateBoundary> BuildPlateBoundaries(TectonicBoundaryMap boundaries)
    {
        return boundaries.Segments
            .GroupBy(s => PlatePair.Create(s.PlateA, s.PlateB))
            .Select(group =>
            {
                var segments = group.ToArray();
                var points = segments.SelectMany(s => s.Points).Distinct().ToArray();
                var weight = Math.Max(1, segments.Sum(s => s.Points.Count));
                var boundaryMode = DominantBoundaryMode(segments, weight);
                var convergence = segments.Sum(s => s.Convergence * s.Points.Count) / weight;
                var divergence = segments.Sum(s => s.Divergence * s.Points.Count) / weight;
                var shear = segments.Sum(s => s.Shear * s.Points.Count) / weight;
                var subductingPlate = IsSubductionMode(boundaryMode) ? DominantSubductingPlate(segments) : null;
                var subductingOceanicAge = IsSubductionMode(boundaryMode)
                    ? WeightedAverageKnown(segments.Select(s => (s.SubductingOceanicAge, s.Points.Count)))
                    : null;
                var pair = group.Key;
                return new PlateBoundary(
                    pair.A,
                    pair.B,
                    points,
                    ToLegacyKind(boundaryMode, convergence, divergence, shear),
                    boundaryMode,
                    convergence,
                    divergence,
                    shear,
                    segments.Sum(s => s.Activity * s.Points.Count) / weight,
                    WeightedAverageKnown(segments.Select(s => (s.MeanOceanicAge, s.Points.Count))),
                    subductingOceanicAge,
                    subductingPlate,
                    segments,
                    segments.Select(s => s.Id).ToArray());
            })
            .ToArray();
    }

    private static PlateBoundaryKind ToLegacyKind(BoundaryMode mode, double convergence, double divergence, double shear) => mode switch
    {
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary or
        BoundaryMode.ContinentContinentCollision or
        BoundaryMode.Transpression => PlateBoundaryKind.Convergent,
        BoundaryMode.ContinentalRift or
        BoundaryMode.MidOceanRidge or
        BoundaryMode.BackArcSpreading or
        BoundaryMode.Transtension => PlateBoundaryKind.Divergent,
        BoundaryMode.PureTransform => PlateBoundaryKind.Transform,
        BoundaryMode.MixedSegmentBoundary => ToLegacyKindFromMotion(convergence, divergence, shear),
        _ => PlateBoundaryKind.Passive
    };

    private static PlateBoundaryKind ToLegacyKindFromMotion(double convergence, double divergence, double shear)
    {
        var normal = Math.Max(convergence, divergence);
        if (normal < 0.1 && shear < 0.1)
            return PlateBoundaryKind.Passive;

        if (shear > normal * 1.75 && normal < 0.18)
            return PlateBoundaryKind.Transform;

        return convergence >= divergence ? PlateBoundaryKind.Convergent : PlateBoundaryKind.Divergent;
    }

    private static BoundaryMode DominantBoundaryMode(IEnumerable<PlateBoundarySegment> segments, int weight)
    {
        var weights = segments
            .GroupBy(s => s.BoundaryMode)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Points.Count));

        if (weights.Count == 0 || weight <= 0)
            return BoundaryMode.MixedSegmentBoundary;

        var dominant = weights.OrderByDescending(kv => kv.Value).First();
        return dominant.Value / (double)weight >= 0.6
            ? dominant.Key
            : BoundaryMode.MixedSegmentBoundary;
    }

    private static bool IsSubductionMode(BoundaryMode mode) => mode is
        BoundaryMode.OceanOceanSubduction or
        BoundaryMode.OceanContinentSubduction or
        BoundaryMode.ObliqueSubduction or
        BoundaryMode.AccretionaryBoundary;

    private static double? WeightedAverageKnown(IEnumerable<(double? Value, int Weight)> values)
    {
        var sum = 0.0;
        var weight = 0;
        foreach (var (value, itemWeight) in values)
        {
            if (!value.HasValue || itemWeight <= 0)
                continue;

            sum += value.Value * itemWeight;
            weight += itemWeight;
        }

        return weight == 0 ? null : sum / weight;
    }

    private static TectonicPlateId? DominantSubductingPlate(IEnumerable<PlateBoundarySegment> segments)
    {
        return segments
            .Where(s => s.SubductingPlate.HasValue)
            .GroupBy(s => s.SubductingPlate!.Value)
            .OrderByDescending(g => g.Sum(s => s.Points.Count))
            .Select(g => (TectonicPlateId?)g.Key)
            .FirstOrDefault();
    }

    private readonly record struct PlatePair(TectonicPlateId A, TectonicPlateId B)
    {
        public static PlatePair Create(TectonicPlateId first, TectonicPlateId second) => first.Value <= second.Value ? new PlatePair(first, second) : new PlatePair(second, first);
    }
}
