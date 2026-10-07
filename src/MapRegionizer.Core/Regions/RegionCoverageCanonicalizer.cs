using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Precision;
using System.Threading;

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
        => Canonicalize(landmasses, draft, CancellationToken.None);

    public RegionCanonicalizationResult Canonicalize(
        IReadOnlyList<Landmass> landmasses,
        RegionDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(landmasses);
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<RegionDiagnostic>();
        var landmassesById = landmasses.GroupBy(landmass => landmass.Id).ToDictionary(group => group.Key, group => group.Single());
        var usedIds = new HashSet<int>();
        var nextId = 1;
        var canonical = new List<MapRegion>();

        foreach (var draftRegion in draft.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();

            var reduced = _precisionReducer.Reduce(draftRegion.Shape);
            cancellationToken.ThrowIfCancellationRequested();
            if (reduced is not Polygon polygon || polygon.IsEmpty || !polygon.IsValid || polygon.Area <= 0)
            {
                diagnostics.Add(new("non-polygon-after-snap", RegionDiagnosticSeverity.Error,
                    "Snap-rounding collapsed the draft region or produced a non-polygon.", requestedId, landmassId));
                continue;
            }

            var isCoveredByLandmass = landmass.Shape.Covers(polygon);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCoveredByLandmass)
            {
                var clipped = polygon.Intersection(landmass.Shape);
                cancellationToken.ThrowIfCancellationRequested();
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

        InsertSharedBoundaryNodes(canonical, cancellationToken);
        RepairSlivers(canonical, diagnostics, cancellationToken);
        AddContractDiagnostics(landmasses, canonical, diagnostics, cancellationToken);
        return new RegionCanonicalizationResult(canonical, diagnostics);
    }

    private static void RepairSlivers(
        List<MapRegion> regions,
        ICollection<RegionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var slivers = new List<MapRegion>();
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region.Shape.Area <= RegionGeometryPrecision.GetAreaTolerance(region.Shape))
                slivers.Add(region);
        }

        foreach (var sliver in slivers.OrderBy(region => region.LandmassId.Value).ThenBy(region => region.Id.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targets = new List<(MapRegion Region, double SharedLength)>();
            foreach (var region in regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (region == sliver || region.LandmassId != sliver.LandmassId ||
                    !RegionGeometryContract.ShareBoundary(region.Shape, sliver.Shape))
                    continue;
                cancellationToken.ThrowIfCancellationRequested();

                var sharedLength = region.Shape.Boundary.Intersection(sliver.Shape.Boundary).Length;
                cancellationToken.ThrowIfCancellationRequested();
                targets.Add((region, sharedLength));
            }

            var target = targets
                .OrderByDescending(candidate => candidate.SharedLength)
                .ThenBy(candidate => candidate.Region.Id.Value)
                .Select(candidate => candidate.Region)
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
    private static void InsertSharedBoundaryNodes(List<MapRegion> regions, CancellationToken cancellationToken)
    {
        var nodes = new Dictionary<string, Coordinate>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var coordinate in region.Shape.Coordinates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nodes.TryAdd(RegionGeometryPrecision.GetCoordinateKey(coordinate), coordinate.Copy());
            }
        }

        var segments = new List<LineSegment>();
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            segments.AddRange(GetSegments(region.Shape, cancellationToken));
        }

        var segmentIndex = new STRtree<int>();
        for (var index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            segmentIndex.Insert(new Envelope(segments[index].P0, segments[index].P1), index);
        }
        segmentIndex.Build();

        for (var firstIndex = 0; firstIndex < segments.Count; firstIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = segments[firstIndex];
            foreach (var secondIndex in segmentIndex.Query(new Envelope(first.P0, first.P1)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (secondIndex <= firstIndex)
                    continue;

                var intersection = first.Intersection(segments[secondIndex]);
                cancellationToken.ThrowIfCancellationRequested();
                if (intersection is not null)
                    nodes.TryAdd(RegionGeometryPrecision.GetCoordinateKey(intersection), intersection);
            }
        }

        var orderedNodes = nodes.Values.ToArray();
        var nodeIndex = new STRtree<IndexedCoordinate>();
        for (var nodeIndexValue = 0; nodeIndexValue < orderedNodes.Length; nodeIndexValue++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = orderedNodes[nodeIndexValue];
            nodeIndex.Insert(new Envelope(node, node), new IndexedCoordinate(node, nodeIndexValue));
        }
        nodeIndex.Build();

        for (var index = 0; index < regions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regions[index];
            var shell = InsertNodes(region.Shape.ExteriorRing, nodeIndex, region.Shape.Factory, cancellationToken);
            var holes = Enumerable.Range(0, region.Shape.NumInteriorRings)
                .Select(holeIndex => InsertNodes(
                    region.Shape.GetInteriorRingN(holeIndex),
                    nodeIndex,
                    region.Shape.Factory,
                    cancellationToken)).ToArray();
            regions[index] = region with { Shape = region.Shape.Factory.CreatePolygon(shell, holes) };
        }
    }

    private static LinearRing InsertNodes(
        LineString ring,
        STRtree<IndexedCoordinate> nodeIndex,
        GeometryFactory factory,
        CancellationToken cancellationToken)
    {
        var coordinates = new List<Coordinate>();
        for (var index = 0; index < ring.NumPoints - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = ring.GetCoordinateN(index);
            var end = ring.GetCoordinateN(index + 1);
            coordinates.Add(start.Copy());
            var segment = new LineSegment(start, end);
            var searchEnvelope = new Envelope(start, end);
            searchEnvelope.ExpandBy(RegionGeometryPrecision.LengthTolerance);
            var matchingNodes = new List<IndexedCoordinate>();
            foreach (var node in nodeIndex.Query(searchEnvelope))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsStrictlyOnSegment(segment, node.Coordinate))
                    matchingNodes.Add(node);
            }

            coordinates.AddRange(matchingNodes
                .OrderBy(node => segment.ProjectionFactor(node.Coordinate))
                .ThenBy(node => node.Order)
                .Select(node => node.Coordinate.Copy()));
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

    private static IEnumerable<LineSegment> GetSegments(Polygon polygon, CancellationToken cancellationToken)
    {
        foreach (var ring in GetRings(polygon, cancellationToken))
        {
            for (var index = 0; index < ring.NumPoints - 1; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new LineSegment(ring.GetCoordinateN(index), ring.GetCoordinateN(index + 1));
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

    private static void AddContractDiagnostics(
        IReadOnlyList<Landmass> landmasses,
        IReadOnlyList<MapRegion> regions,
        ICollection<RegionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var violation in RegionGeometryContract.Validate(landmasses, regions, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(new("coverage-topology", RegionDiagnosticSeverity.Error, violation));
        }
    }

    private static bool HasFiniteCoordinates(Geometry geometry) => geometry.Coordinates.All(coordinate =>
        double.IsFinite(coordinate.X) && double.IsFinite(coordinate.Y));

    private sealed record IndexedCoordinate(Coordinate Coordinate, int Order);
}
