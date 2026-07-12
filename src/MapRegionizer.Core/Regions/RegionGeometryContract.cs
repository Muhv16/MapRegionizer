using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Union;

namespace MapRegionizer.Core.Regions;

/// <summary>
/// Validates the topology shared by generated and manually edited regions.
/// </summary>
public static class RegionGeometryContract
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<Landmass> landmasses, IReadOnlyList<MapRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(regions);

        var violations = new List<string>();
        var landmassesById = landmasses.GroupBy(landmass => landmass.Id).ToDictionary(group => group.Key, group => group.ToList());
        foreach (var duplicate in landmassesById.Where(pair => pair.Value.Count != 1))
            violations.Add($"Landmass id {duplicate.Key.Value} is not unique.");

        var seenRegionIds = new HashSet<RegionId>();
        foreach (var region in regions)
        {
            if (region.Id.Value <= 0 || !seenRegionIds.Add(region.Id))
                violations.Add($"Region id {region.Id.Value} is not unique and positive.");
            if (!landmassesById.ContainsKey(region.LandmassId))
                violations.Add($"Region {region.Id.Value} references unknown landmass {region.LandmassId.Value}.");
            if (region.Shape.IsEmpty || !region.Shape.IsValid || region.Shape.Area <= 0)
                violations.Add($"Region {region.Id.Value} is not a valid non-empty polygon.");
        }

        foreach (var landmass in landmasses)
        {
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

            ValidateNoOverlaps(landmassRegions, violations);
            ValidateCoverage(landmass, landmassRegions, violations);
            ValidateBoundarySegments(landmass, landmassRegions, violations);
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

    private static void ValidateNoOverlaps(IReadOnlyList<MapRegion> regions, ICollection<string> violations)
    {
        var spatialIndex = new STRtree<int>();
        for (var index = 0; index < regions.Count; index++)
            spatialIndex.Insert(regions[index].Shape.EnvelopeInternal, index);

        spatialIndex.Build();
        for (var i = 0; i < regions.Count; i++)
        {
            foreach (var j in spatialIndex.Query(regions[i].Shape.EnvelopeInternal).Where(index => index > i))
            {
                if (regions[i].Shape.Intersection(regions[j].Shape).Area > RegionGeometryPrecision.GetAreaTolerance(regions[i].Shape))
                    violations.Add($"Regions {regions[i].Id.Value} and {regions[j].Id.Value} overlap.");
            }
        }
    }

    private static void ValidateCoverage(Landmass landmass, IReadOnlyList<MapRegion> regions, ICollection<string> violations)
    {
        var union = UnaryUnionOp.Union(regions.Select(region => region.Shape).ToArray());
        var toleranceArea = RegionGeometryPrecision.GetAreaTolerance(landmass.Shape);
        if (landmass.Shape.Difference(union).Area > toleranceArea)
            violations.Add($"Regions leave uncovered area in landmass {landmass.Id.Value}.");
        if (union.Difference(landmass.Shape).Area > toleranceArea)
            violations.Add($"Regions of landmass {landmass.Id.Value} extend into water or another landmass.");
    }

    private static void ValidateBoundarySegments(Landmass landmass, IReadOnlyList<MapRegion> regions, ICollection<string> violations)
    {
        var landmassBoundaryIndex = new STRtree<LineString>();
        foreach (var segment in GetSegments(landmass.Shape))
            landmassBoundaryIndex.Insert(segment.Geometry.EnvelopeInternal, segment.Geometry);

        landmassBoundaryIndex.Build();
        var regionEdges = regions.SelectMany(region => GetSegments(region.Shape)).GroupBy(segment => segment.UndirectedKey).ToList();
        foreach (var edge in regionEdges)
        {
            if (edge.Count() == 1)
            {
                var segment = edge.First().Geometry;
                var envelope = new Envelope(segment.EnvelopeInternal);
                envelope.ExpandBy(RegionGeometryPrecision.LengthTolerance);
                var isOnLandmassBoundary = landmassBoundaryIndex.Query(envelope)
                    .Any(boundarySegment => boundarySegment.Distance(segment) <= RegionGeometryPrecision.LengthTolerance);
                if (!isOnLandmassBoundary)
                    violations.Add($"Region boundary {edge.Key} is not part of landmass {landmass.Id.Value}.");
            }
            else if (edge.Count() != 2 || !edge.First().IsReverseOf(edge.Last()))
            {
                violations.Add($"Region boundary {edge.Key} is not represented by matching reverse coordinates.");
            }
        }
    }

    private static IEnumerable<Segment> GetSegments(Polygon polygon)
    {
        foreach (var ring in GetRings(polygon))
        {
            for (var index = 0; index < ring.NumPoints - 1; index++)
                yield return new Segment(ring.GetCoordinateN(index), ring.GetCoordinateN(index + 1), polygon.Factory);
        }
    }

    private static IEnumerable<LineString> GetRings(Polygon polygon)
    {
        yield return polygon.ExteriorRing;
        for (var index = 0; index < polygon.NumInteriorRings; index++)
            yield return polygon.GetInteriorRingN(index);
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
