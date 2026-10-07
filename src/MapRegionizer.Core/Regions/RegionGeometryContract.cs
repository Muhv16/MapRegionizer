using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Union;
using System.Threading;

namespace MapRegionizer.Core.Regions;

/// <summary>
/// Validates the topology shared by generated and manually edited regions.
/// </summary>
public static class RegionGeometryContract
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<Landmass> landmasses, IReadOnlyList<MapRegion> regions)
        => Validate(landmasses, regions, CancellationToken.None);

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<Landmass> landmasses,
        IReadOnlyList<MapRegion> regions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(regions);
        cancellationToken.ThrowIfCancellationRequested();

        var violations = new List<string>();
        var landmassesById = landmasses.GroupBy(landmass => landmass.Id).ToDictionary(group => group.Key, group => group.ToList());
        foreach (var duplicate in landmassesById.Where(pair => pair.Value.Count != 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            violations.Add($"Landmass id {duplicate.Key.Value} is not unique.");
        }

        var seenRegionIds = new HashSet<RegionId>();
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region.Id.Value <= 0 || !seenRegionIds.Add(region.Id))
                violations.Add($"Region id {region.Id.Value} is not unique and positive.");
            if (!landmassesById.ContainsKey(region.LandmassId))
                violations.Add($"Region {region.Id.Value} references unknown landmass {region.LandmassId.Value}.");
            if (region.Shape.IsEmpty || !region.Shape.IsValid || region.Shape.Area <= 0)
                violations.Add($"Region {region.Id.Value} is not a valid non-empty polygon.");
        }

        foreach (var landmass in landmasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (landmass.Shape.IsEmpty || !landmass.Shape.IsValid)
            {
                violations.Add($"Landmass {landmass.Id.Value} is not a valid polygon.");
                continue;
            }
            var landmassRegions = regions.Where(region => region.LandmassId == landmass.Id).ToList();
            if (landmassRegions.Count == 0)
            {
                violations.Add($"Landmass {landmass.Id.Value} has no regions.");
                continue;
            }

            ValidateNoOverlaps(landmassRegions, violations, cancellationToken);
            ValidateCoverage(landmass, landmassRegions, violations, cancellationToken);
            ValidateBoundarySegments(landmass, landmassRegions, violations, cancellationToken);
        }

        return violations;
    }

    public static void EnsureSatisfied(IReadOnlyList<Landmass> landmasses, IReadOnlyList<MapRegion> regions, string dataName)
    {
        var violations = Validate(landmasses, regions);
        if (violations.Count != 0)
            throw new InvalidOperationException($"{dataName} violates the region geometry contract:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>
    /// Returns true only when two regions share a boundary with non-zero length.
    /// A vertex-only touch is not adjacency.
    /// </summary>
    public static bool ShareBoundary(Geometry first, Geometry second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var firstSegments = GetLineStrings(first.Boundary)
            .SelectMany(GetSegmentKeys)
            .ToHashSet(StringComparer.Ordinal);
        return GetLineStrings(second.Boundary)
            .SelectMany(GetSegmentKeys)
            .Any(firstSegments.Contains);
    }

    private static void ValidateNoOverlaps(
        IReadOnlyList<MapRegion> regions,
        ICollection<string> violations,
        CancellationToken cancellationToken)
    {
        var spatialIndex = new STRtree<int>();
        for (var index = 0; index < regions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spatialIndex.Insert(regions[index].Shape.EnvelopeInternal, index);
        }

        spatialIndex.Build();
        for (var i = 0; i < regions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var j in spatialIndex.Query(regions[i].Shape.EnvelopeInternal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (j <= i)
                    continue;

                if (regions[i].Shape.Intersection(regions[j].Shape).Area > RegionGeometryPrecision.GetAreaTolerance(regions[i].Shape))
                    violations.Add($"Regions {regions[i].Id.Value} and {regions[j].Id.Value} overlap.");
            }
        }
    }

    private static void ValidateCoverage(
        Landmass landmass,
        IReadOnlyList<MapRegion> regions,
        ICollection<string> violations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var union = UnaryUnionOp.Union(regions.Select(region => region.Shape).ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        var toleranceArea = RegionGeometryPrecision.GetAreaTolerance(landmass.Shape);
        if (landmass.Shape.Difference(union).Area > toleranceArea)
            violations.Add($"Regions leave uncovered area in landmass {landmass.Id.Value}.");
        cancellationToken.ThrowIfCancellationRequested();
        if (union.Difference(landmass.Shape).Area > toleranceArea)
            violations.Add($"Regions of landmass {landmass.Id.Value} extend into water or another landmass.");
    }

    private static void ValidateBoundarySegments(
        Landmass landmass,
        IReadOnlyList<MapRegion> regions,
        ICollection<string> violations,
        CancellationToken cancellationToken)
    {
        var landmassBoundaryIndex = new STRtree<LineString>();
        foreach (var segment in GetSegments(landmass.Shape, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            landmassBoundaryIndex.Insert(segment.Geometry.EnvelopeInternal, segment.Geometry);
        }

        landmassBoundaryIndex.Build();
        var regionEdges = new Dictionary<string, List<Segment>>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var segment in GetSegments(region.Shape, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!regionEdges.TryGetValue(segment.UndirectedKey, out var edge))
                {
                    edge = [];
                    regionEdges.Add(segment.UndirectedKey, edge);
                }
                edge.Add(segment);
            }
        }

        foreach (var edge in regionEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge.Value.Count == 1)
            {
                var segment = edge.Value[0].Geometry;
                var envelope = new Envelope(segment.EnvelopeInternal);
                envelope.ExpandBy(RegionGeometryPrecision.LengthTolerance);
                var isOnLandmassBoundary = false;
                foreach (var boundarySegment in landmassBoundaryIndex.Query(envelope))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (boundarySegment.Distance(segment) <= RegionGeometryPrecision.LengthTolerance)
                    {
                        isOnLandmassBoundary = true;
                        break;
                    }
                }
                if (!isOnLandmassBoundary)
                    violations.Add($"Region boundary {edge.Key} is not part of landmass {landmass.Id.Value}.");
            }
            else if (edge.Value.Count != 2 || !edge.Value[0].IsReverseOf(edge.Value[^1]))
            {
                violations.Add($"Region boundary {edge.Key} is not represented by matching reverse coordinates.");
            }
        }
    }

    private static IEnumerable<Segment> GetSegments(Polygon polygon, CancellationToken cancellationToken)
    {
        foreach (var ring in GetRings(polygon, cancellationToken))
        {
            for (var index = 0; index < ring.NumPoints - 1; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new Segment(ring.GetCoordinateN(index), ring.GetCoordinateN(index + 1), polygon.Factory);
            }
        }
    }

    private static IEnumerable<LineString> GetRings(Polygon polygon, CancellationToken cancellationToken)
    {
        yield return polygon.ExteriorRing;
        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return polygon.GetInteriorRingN(index);
        }
    }

    private static IEnumerable<LineString> GetLineStrings(Geometry geometry)
    {
        if (geometry is LineString lineString)
        {
            yield return lineString;
            yield break;
        }

        for (var index = 0; index < geometry.NumGeometries; index++)
        {
            foreach (var childLineString in GetLineStrings(geometry.GetGeometryN(index)))
                yield return childLineString;
        }
    }

    private static IEnumerable<string> GetSegmentKeys(LineString lineString)
    {
        for (var index = 0; index < lineString.NumPoints - 1; index++)
            yield return RegionGeometryPrecision.GetUndirectedSegmentKey(lineString.GetCoordinateN(index), lineString.GetCoordinateN(index + 1));
    }

    private readonly record struct Segment(LineString Geometry, string Start, string End)
    {
        public Segment(Coordinate start, Coordinate end, GeometryFactory geometryFactory)
            : this(geometryFactory.CreateLineString([start.Copy(), end.Copy()]), Format(start), Format(end))
        {
        }
        public string UndirectedKey => string.CompareOrdinal(Start, End) <= 0 ? $"{Start}|{End}" : $"{End}|{Start}";
        public bool IsReverseOf(Segment other) => Start == other.End && End == other.Start;
        private static string Format(Coordinate coordinate) => RegionGeometryPrecision.GetCoordinateKey(coordinate);
    }
}
