using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Precision;

namespace MapRegionizer.Core.Regions;

/// <summary>
/// Applies the unambiguous part of the editable-region repair policy and validates
/// the resulting coverage. It never invents a resolution for a material gap or overlap.
/// </summary>
public sealed class RegionCoverageCanonicalizer
{
    private readonly GeometryPrecisionReducer _precisionReducer = new(new PrecisionModel(RegionGeometryPrecision.Scale))
    {
        ChangePrecisionModel = true,
        RemoveCollapsedComponents = true
    };

    public RegionCanonicalizationResult Canonicalize(
        IReadOnlyList<Landmass> landmasses,
        RegionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(draft);

        var diagnostics = new List<RegionDiagnostic>();
        var landmassesById = landmasses.GroupBy(landmass => landmass.Id).ToDictionary(group => group.Key, group => group.Single());
        var usedIds = new HashSet<int>();
        var nextId = 1;
        var canonical = new List<MapRegion>();

        foreach (var draftRegion in draft.Regions)
        {
            var requestedId = draftRegion.Id;
            if (requestedId.HasValue && (requestedId.Value.Value <= 0 || !usedIds.Add(requestedId.Value.Value)))
            {
                diagnostics.Add(new("duplicate-or-invalid-id", RegionDiagnosticSeverity.Error,
                    "A draft region id must be positive and unique.", requestedId));
                continue;
            }

            if (requestedId is null)
            {
                while (usedIds.Contains(nextId))
                    nextId++;
                requestedId = new RegionId(nextId++);
                usedIds.Add(requestedId.Value.Value);
                diagnostics.Add(new("assigned-region-id", RegionDiagnosticSeverity.Info,
                    "A missing region id was assigned deterministically.", requestedId));
            }

            if (draftRegion.LandmassId is not { } landmassId || !landmassesById.TryGetValue(landmassId, out var landmass))
            {
                diagnostics.Add(new("unknown-landmass", RegionDiagnosticSeverity.Error,
                    "A draft region must reference one existing landmass.", requestedId, draftRegion.LandmassId));
                continue;
            }

            if (draftRegion.Shape is null || !HasFiniteCoordinates(draftRegion.Shape))
            {
                diagnostics.Add(new("non-finite-coordinate", RegionDiagnosticSeverity.Error,
                    "A draft region contains a non-finite coordinate.", requestedId, landmassId));
                continue;
            }

            if (!draftRegion.Shape.IsValid)
            {
                diagnostics.Add(new("invalid-geometry", RegionDiagnosticSeverity.Error,
                    "A draft region has invalid geometry and needs an explicit edit.", requestedId, landmassId));
                continue;
            }

            var reduced = _precisionReducer.Reduce(draftRegion.Shape);
            if (reduced is not Polygon polygon || polygon.IsEmpty || !polygon.IsValid || polygon.Area <= 0)
            {
                diagnostics.Add(new("non-polygon-after-snap", RegionDiagnosticSeverity.Error,
                    "Snap-rounding collapsed the draft region or produced a non-polygon.", requestedId, landmassId));
                continue;
            }

            if (!landmass.Shape.Covers(polygon))
            {
                var clipped = polygon.Intersection(landmass.Shape);
                if (clipped is not Polygon clippedPolygon || clippedPolygon.IsEmpty || clippedPolygon.Area <= 0)
                {
                    diagnostics.Add(new("ambiguous-landmass-clipping", RegionDiagnosticSeverity.Error,
                        "Clipping the draft region to its landmass did not produce one polygon.", requestedId, landmassId));
                    continue;
                }

                polygon = clippedPolygon;
                diagnostics.Add(new("clipped-to-landmass", RegionDiagnosticSeverity.Warning,
                    "The part of the draft region outside its landmass was removed.", requestedId, landmassId));
            }

            canonical.Add(new MapRegion(requestedId.Value, landmassId, polygon));
        }

        InsertSharedBoundaryNodes(canonical);
        RepairSlivers(canonical, diagnostics);
        AddContractDiagnostics(landmasses, canonical, diagnostics);
        return new RegionCanonicalizationResult(canonical, diagnostics);
    }

    private static void RepairSlivers(List<MapRegion> regions, ICollection<RegionDiagnostic> diagnostics)
    {
        foreach (var sliver in regions
                     .Where(region => region.Shape.Area <= RegionGeometryPrecision.GetAreaTolerance(region.Shape))
                     .OrderBy(region => region.LandmassId.Value).ThenBy(region => region.Id.Value).ToList())
        {
            var target = regions
                .Where(region => region != sliver && region.LandmassId == sliver.LandmassId)
                .Where(region => RegionGeometryContract.ShareBoundary(region.Shape, sliver.Shape))
                .OrderByDescending(region => region.Shape.Boundary.Intersection(sliver.Shape.Boundary).Length)
                .ThenBy(region => region.Id.Value)
                .FirstOrDefault();
            if (target is null)
                continue;

            var merged = target.Shape.Union(sliver.Shape);
            if (merged is not Polygon mergedPolygon || !mergedPolygon.IsValid)
                continue;

            var targetIndex = regions.IndexOf(target);
            regions[targetIndex] = target with { Shape = mergedPolygon };
            regions.Remove(sliver);
            diagnostics.Add(new("merged-sliver", RegionDiagnosticSeverity.Warning,
                "A microscopic face was merged into its longest-edge neighbour.", sliver.Id, sliver.LandmassId));
        }
    }

    /// <summary>
    /// Makes a T-junction explicit in both faces. This is an unambiguous repair:
    /// it only inserts an already existing endpoint (or a segment intersection),
    /// without moving an edge or changing area.
    /// </summary>
    private static void InsertSharedBoundaryNodes(List<MapRegion> regions)
    {
        var nodes = regions.SelectMany(region => region.Shape.Coordinates)
            .GroupBy(RegionGeometryPrecision.GetCoordinateKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Copy(), StringComparer.Ordinal);
        var segments = regions.SelectMany(region => GetSegments(region.Shape)).ToList();
        var segmentIndex = new STRtree<int>();
        for (var index = 0; index < segments.Count; index++)
            segmentIndex.Insert(new Envelope(segments[index].P0, segments[index].P1), index);
        segmentIndex.Build();

        for (var firstIndex = 0; firstIndex < segments.Count; firstIndex++)
        {
            var first = segments[firstIndex];
            foreach (var secondIndex in segmentIndex.Query(new Envelope(first.P0, first.P1)).Where(index => index > firstIndex))
            {
                var intersection = first.Intersection(segments[secondIndex]);
                if (intersection is not null)
                    nodes.TryAdd(RegionGeometryPrecision.GetCoordinateKey(intersection), intersection);
            }
        }

        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            var shell = InsertNodes(region.Shape.ExteriorRing, nodes.Values, region.Shape.Factory);
            var holes = Enumerable.Range(0, region.Shape.NumInteriorRings)
                .Select(holeIndex => InsertNodes(region.Shape.GetInteriorRingN(holeIndex), nodes.Values, region.Shape.Factory)).ToArray();
            regions[index] = region with { Shape = region.Shape.Factory.CreatePolygon(shell, holes) };
        }
    }

    private static LinearRing InsertNodes(LineString ring, IEnumerable<Coordinate> nodes, GeometryFactory factory)
    {
        var coordinates = new List<Coordinate>();
        for (var index = 0; index < ring.NumPoints - 1; index++)
        {
            var start = ring.GetCoordinateN(index);
            var end = ring.GetCoordinateN(index + 1);
            coordinates.Add(start.Copy());
            var segment = new LineSegment(start, end);
            coordinates.AddRange(nodes
                .Where(node => IsStrictlyOnSegment(segment, node))
                .OrderBy(node => segment.ProjectionFactor(node))
                .Select(node => node.Copy()));
        }
        coordinates.Add(coordinates[0].Copy());
        return factory.CreateLinearRing(coordinates.ToArray());
    }

    private static bool IsStrictlyOnSegment(LineSegment segment, Coordinate coordinate)
    {
        var projection = segment.ProjectionFactor(coordinate);
        return projection > RegionGeometryPrecision.LengthTolerance
            && projection < 1 - RegionGeometryPrecision.LengthTolerance
            && segment.Distance(coordinate) <= RegionGeometryPrecision.LengthTolerance;
    }

    private static IEnumerable<LineSegment> GetSegments(Polygon polygon)
    {
        foreach (var ring in GetRings(polygon))
        {
            for (var index = 0; index < ring.NumPoints - 1; index++)
                yield return new LineSegment(ring.GetCoordinateN(index), ring.GetCoordinateN(index + 1));
        }
    }

    private static IEnumerable<LineString> GetRings(Polygon polygon)
    {
        yield return polygon.ExteriorRing;
        for (var index = 0; index < polygon.NumInteriorRings; index++)
            yield return polygon.GetInteriorRingN(index);
    }

    private static void AddContractDiagnostics(
        IReadOnlyList<Landmass> landmasses,
        IReadOnlyList<MapRegion> regions,
        ICollection<RegionDiagnostic> diagnostics)
    {
        foreach (var violation in RegionGeometryContract.Validate(landmasses, regions))
            diagnostics.Add(new("coverage-topology", RegionDiagnosticSeverity.Error, violation));
    }

    private static bool HasFiniteCoordinates(Geometry geometry) => geometry.Coordinates.All(coordinate =>
        double.IsFinite(coordinate.X) && double.IsFinite(coordinate.Y));
}
